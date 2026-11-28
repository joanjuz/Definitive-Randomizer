"""Commercial-data-free admission and ARM helper checks; never invokes Unicorn."""
import copy
import struct
import unittest
import tempfile
from pathlib import Path

import inspect_mints as inspector
from replay_accessor import branch_link


X_TITLE = '0004000000055D00'
Y_TITLE = '0004000000055E00'


def synthetic_resources(title, marker):
    ncch = bytearray(0x120)
    ncch[0x100:0x104] = b'NCCH'
    ncch[0x118:0x120] = bytes.fromhex(title)[::-1]
    exheader = bytearray(0x208)
    exheader[0x200:0x208] = ncch[0x118:0x120]
    # These are opaque synthetic dependency blobs, not executable fixtures.
    return {'ncchheader.bin': bytes(ncch), 'exheader.bin': bytes(exheader),
            'RomFS/DllBag.cro': b'synthetic-bag-' + bytes([marker]),
            'ExeFS/.code.bin': b'synthetic-code-' + bytes([marker])}


class AdmissionChecks(unittest.TestCase):
    def setUp(self):
        self.x = synthetic_resources(X_TITLE, 1)
        self.y = synthetic_resources(Y_TITLE, 2)
        self.profiles = {'titles': {
            title: {'sha256': {name: inspector.sha(blob) for name, blob in blobs.items()}}
            for title, blobs in ((X_TITLE, self.x), (Y_TITLE, self.y))}}

    def test_independent_title_profiles_accept_only_their_exact_dependencies(self):
        self.assertEqual(X_TITLE, inspector.identify(self.x, self.profiles))
        self.assertEqual(Y_TITLE, inspector.identify(self.y, self.profiles))
        for original, crossed in ((self.x, self.y), (self.y, self.x)):
            with self.subTest(title=original['ncchheader.bin'][-8:].hex()):
                for name in ('ExeFS/.code.bin', 'RomFS/DllBag.cro', 'exheader.bin'):
                    candidate = dict(original, **{name: crossed[name]})
                    with self.subTest(dependency=name), self.assertRaises(ValueError):
                        inspector.identify(candidate, self.profiles)

    def test_unknown_dependency_rejected_without_mutating_inputs_or_profiles(self):
        candidate = dict(self.x)
        candidate['ExeFS/.code.bin'] += b'unknown'
        snapshot, profiles = copy.deepcopy(candidate), copy.deepcopy(self.profiles)
        with self.assertRaisesRegex(ValueError, 'Unknown or crossed'):
            inspector.identify(candidate, self.profiles)
        self.assertEqual(snapshot, candidate)
        self.assertEqual(profiles, self.profiles)

    def test_missing_or_additional_dependency_rejected(self):
        missing = dict(self.x)
        del missing['ExeFS/.code.bin']
        excess = dict(self.x, unexpected=b'synthetic')
        for candidate in (missing, excess):
            with self.subTest(keys=sorted(candidate)), self.assertRaisesRegex(ValueError, 'Missing or excess'):
                inspector.identify(candidate, self.profiles)

    def test_truncated_header_and_exheader_rejected(self):
        for name, lengths in (('ncchheader.bin', (0, 0x103, 0x117, 0x11f)),
                              ('exheader.bin', (0, 0x1ff, 0x207))):
            for length in lengths:
                with self.subTest(name=name, length=length), self.assertRaises(ValueError):
                    inspector.identify(dict(self.x, **{name: self.x[name][:length]}), self.profiles)

    def test_corrupt_magic_and_unknown_title_rejected_even_with_matching_hashes(self):
        for offset in (0x100, 0x118):
            ncch = bytearray(self.x['ncchheader.bin'])
            ncch[offset] ^= 0x80
            blobs = dict(self.x, **{'ncchheader.bin': bytes(ncch)})
            profiles = copy.deepcopy(self.profiles)
            profiles['titles'][X_TITLE]['sha256']['ncchheader.bin'] = inspector.sha(ncch)
            with self.subTest(offset=offset), self.assertRaises(ValueError):
                inspector.identify(blobs, profiles)

    def test_verify_unchanged_rejects_resource_race_without_rewriting_files(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for name, blob in self.x.items():
                path = root/name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(blob)
            inspector.verify_unchanged(root, self.x)
            changed = root/'RomFS/DllBag.cro'
            changed.write_bytes(b'synthetic-race')
            with self.assertRaisesRegex(ValueError, 'changed concurrently'):
                inspector.verify_unchanged(root, self.x)
            self.assertEqual(b'synthetic-race', changed.read_bytes())
            self.assertEqual(4, len([p for p in root.rglob('*') if p.is_file()]))


class ArmHelperChecks(unittest.TestCase):
    def test_branch_link_roundtrip_forward_backward_and_signed_limits(self):
        at = 0x4000000
        for delta in (-0x2000000, -4, 0, 4, 0x1fffffc):
            target = at + 8 + delta
            with self.subTest(delta=delta):
                encoded = branch_link(at, target)
                self.assertEqual(0xeb000000, encoded & 0xff000000)
                self.assertEqual(target, inspector.bl_target(encoded, at))

    def test_branch_link_rejects_just_outside_range_and_misaligned_delta(self):
        for target in (0x4000000 + 8 - 0x2000004, 0x4000000 + 8 + 0x2000000,
                       0x4000000 + 9):
            with self.subTest(target=target), self.assertRaises(ValueError):
                branch_link(0x4000000, target)

    def test_branch_link_rejects_unaligned_or_out_of_address_space_addresses(self):
        for at, target in ((1, 9), (-4, 4), (0x100000000, 0x100000008),
                           (0xfffffffc, 0x100000004)):
            with self.subTest(at=at, target=target), self.assertRaises(ValueError):
                branch_link(at, target)

    def test_branch_link_rejects_boolean_and_noninteger_addresses(self):
        for at, target in ((False, 8), (0, False), (0.0, 8), (0, '8')):
            with self.subTest(at=at, target=target), self.assertRaises(ValueError):
                branch_link(at, target)

    def test_bl_excludes_b_bx_and_nv_blx_but_accepts_conditional_bl(self):
        self.assertIsNone(inspector.bl_target(0xea000000, 0x1000))
        self.assertIsNone(inspector.bl_target(0xe12fff1e, 0x1000))
        for word in (0xfa000000, 0xfb000000):
            with self.subTest(word=hex(word)):
                self.assertIsNone(inspector.bl_target(word, 0x1000))
        self.assertEqual(0x1008, inspector.bl_target(0x1b000000, 0x1000))

    def test_calls_respects_va_mapping_bounds_and_preserves_input(self):
        raw = struct.pack('<4I', 0xea000000, branch_link(0x1004, 0x1030),
                          0xe12fff1e, branch_link(0x100c, 0x1000))
        self.assertEqual([{'at': '0x1004', 'target': '0x1030'},
                          {'at': '0x100c', 'target': '0x1000'}],
                         inspector.calls(raw, 0x1000, 0x1010, 0x1000))
        self.assertEqual(16, len(raw))
        with self.assertRaises(ValueError):
            inspector.calls(raw, 0x1000, 0x1014, 0x1000)


if __name__ == '__main__':
    unittest.main()
