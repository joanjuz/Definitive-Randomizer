"""Synthetic research-parser checks. No commercial resources or installable patch."""
import hashlib
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import inspect_persistent_items as reader


def put(data, at, *values):
    struct.pack_into('<' + 'I' * len(values), data, at, *values)


def cro():
    data = bytearray(0x600)
    data[0x80:0x84] = b'CRO0'
    put(data, 0x90, len(data))
    put(data, 0xb0, 0x200, 0x100, 0x400, 0x200, 0x300)
    put(data, 0xc8, 0x180, 4)
    for i, segment in enumerate(((0x200, 0x100, 0), (0x300, 0x100, 1),
                                 (0x400, 0x200, 2), (0, 0x40, 3))):
        put(data, 0x180 + i * 12, *segment)
    return data


def hash_cro(data):
    for slot, start, size in ((0, 0x80, 0x100), (1, 0x200, 0x100),
                              (2, 0x300, 0x100), (3, 0x400, 0x200)):
        data[slot * 32:(slot + 1) * 32] = hashlib.sha256(data[start:start + size]).digest()
    return data


def imports():
    battle, static = cro(), cro()
    put(battle, 0xf0, 0x480, 1)
    put(battle, 0xf8, 0x4c0, 2)
    put(battle, 0x110, 0xffffffff, 0xffffffff)  # A decoy: these are not relocation fields.
    put(battle, 0x480, 0x4e0, 0x4a0, 1, 0x4b0, 1)
    battle[0x4e0:0x4e9] = b'|static|\0'
    put(battle, 0x4a0, 2, 0x4c0)
    put(battle, 0x4b0, 0x301, 0x4cc)
    put(battle, 0x4c0, 0x200, 0x102, 0)
    put(battle, 0x4cc, 0x240, 0x102, 4)
    put(static, 0xd8, 0x500, 3)
    put(static, 0x508, 0x101)
    return battle, static


def garc():
    data = bytearray(0x58)
    data[:4] = b'CRAG'
    put(data, 4, 0x1c)
    put(data, 16, 0x54)
    data[0x1c:0x20] = b'OTAF'
    put(data, 0x20, 0x10)
    struct.pack_into('<H', data, 0x24, 1)
    put(data, 0x28, 0)
    data[0x2c:0x30] = b'BTAF'
    put(data, 0x30, 0x1c, 1, 1, 0, 4, 3)
    data[0x48:0x4c] = b'BMIF'
    put(data, 0x4c, 0xc, 4)
    data[0x54:] = b'ABC\0'
    return data


class CroTests(unittest.TestCase):
    def test_region_accepts_last_byte_and_empty_end(self):
        self.assertEqual(reader.region(b'abcd', 3, 1), b'd')
        self.assertEqual(reader.region(b'abcd', 4, 0), b'')

    def test_region_rejects_negative_and_outside_ranges(self):
        for start, size in ((-1, 1), (0, -1), (4, 1), (2, 3), (5, 0)):
            with self.subTest(start=start, size=size), self.assertRaises(ValueError):
                reader.region(b'abcd', start, size)

    def test_cro_rejects_magic_truncation_count_and_segment_bounds(self):
        cases = [bytearray(0x80), cro()[:0x100]]
        for at, value in ((0x80, 0), (0x90, 0x601), (0xcc, 3), (0xcc, 17),
                          (0xc8, 0x5ff), (0x184, 0x401)):
            data = cro()
            put(data, at, value)
            cases.append(data)
        for i, data in enumerate(cases):
            with self.subTest(case=i), self.assertRaises(ValueError):
                reader.Cro(data)

    def test_segment_tags_map_each_backed_segment_and_reject_bss_or_end(self):
        module = reader.Cro(cro())
        for tag, expected in ((0, 0x200), (0xff0, 0x2ff), (0x101, 0x310), (0x1ff2, 0x5ff)):
            self.assertEqual(module.address(tag), expected)
        for tag in (3, 0x1000, 0x2002):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                module.address(tag)

    def test_four_hashes_detect_changed_code_rodata_data_and_header(self):
        data = hash_cro(cro())
        self.assertTrue(reader.Cro(data).hashes_valid())
        for at in (0x88, 0x220, 0x320, 0x420, 0x20):
            changed = bytearray(data)
            changed[at] ^= 1
            with self.subTest(offset=at):
                self.assertFalse(reader.Cro(changed).hashes_valid())

    def test_hash_ranges_reject_rodata_after_data_and_code_overrun(self):
        for at, value in ((0xc0, 0x401), (0xb4, 0x1000)):
            changed = hash_cro(cro())
            put(changed, at, value)
            changed[:32] = hashlib.sha256(changed[0x80:0x180]).digest()
            with self.subTest(offset=at), self.assertRaises(ValueError):
                reader.Cro(changed).hashes_valid()

    def test_indexed_and_anonymous_static_links_resolve_independent_targets(self):
        battle, static = imports()
        result = reader.Cro(battle).static_links(reader.Cro(static), {0x220, 0x224})
        self.assertEqual(result, [dict(slot='0x220', kind='0x102', addend=0, target_va='0x310'),
                                  dict(slot='0x224', kind='0x102', addend=4, target_va='0x330')])

    def test_import_batch_rejects_unaligned_outside_and_unterminated_cursors(self):
        for at, value in ((0x4a4, 0x4bf), (0x4a4, 0x4c1), (0x4a4, 0x4d8),
                          (0x4c4, 2), (0x4d0, 2), (0xfc, 0x1000), (0xf4, 0x1000)):
            battle, static = imports()
            put(battle, at, value)
            with self.subTest(offset=at, value=value), self.assertRaises(ValueError):
                reader.Cro(battle).static_links(reader.Cro(static), {0x220, 0x224})

    def test_import_links_reject_missing_duplicate_foreign_and_unknown_export(self):
        for kind in ('missing', 'duplicate', 'foreign', 'export'):
            battle, static = imports()
            wanted = {0x220, 0x224}
            if kind == 'missing':
                wanted.add(0x228)
            elif kind == 'duplicate':
                put(battle, 0x4cc, 0x200)
            elif kind == 'foreign':
                battle[0x4e0] = ord('X')
            else:
                put(battle, 0x4a0, 3)
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                reader.Cro(battle).static_links(reader.Cro(static), wanted)


class GarcAndSignatureTests(unittest.TestCase):
    def test_garc_returns_payload_without_alignment_padding(self):
        self.assertEqual(reader.garc_members(garc()), [b'ABC'])

    def test_garc_rejects_truncated_tables_and_invalid_member_lengths(self):
        cases = [garc()[:0x1c], garc()[:0x48], bytearray(0x58)]
        for at, value in ((0x38, 3), (0x3c, 5), (0x44, 5), (16, 0x1000), (0x28, 0x1000)):
            data = garc()
            put(data, at, value)
            cases.append(data)
        for i, data in enumerate(cases):
            with self.subTest(case=i), self.assertRaises(ValueError):
                reader.garc_members(data)

    def test_garc_rejects_padded_end_outside_file_even_if_payload_fits(self):
        data = garc()
        put(data, 0x40, 0xffffffff)
        with self.assertRaises(ValueError):
            reader.garc_members(data)

    def test_matches_absent_unique_duplicate_and_unaligned(self):
        pattern = b'abcd'
        for data, expected in ((b'xxxx', []), (b'abcdxxxx', [0]), (b'abcdabcd', [0, 4]),
                               (b'xabcdxxx', []), (b'abc', [])):
            with self.subTest(data=data):
                self.assertEqual(reader.matches(data, pattern), expected)


class ProjectIdentityTests(unittest.TestCase):
    resources = ['HeaderNCCH0.bin', 'DecryptedExHeader.bin', 'ExtractedRomFS/DllBattle.cro',
                 'ExtractedRomFS/static.crs', 'ExtractedExeFS/.code.bin',
                 'ExtractedRomFS/a/2/2/0', 'ExtractedRomFS/.crr/static.crr']

    def blobs(self, title):
        ncch, ex = bytearray(0x200), bytearray(0x400)
        ncch[0x100:0x104] = b'NCCH'
        ncch[0x118:0x120] = int(title, 16).to_bytes(8, 'little')
        ex[0x200:0x208] = ncch[0x118:0x120]
        put(ex, 0x10, 0x4a2000)
        code = bytearray(0x400)
        put(code, 0x308, 0x4a2100)
        battle = hash_cro(cro())
        crr = bytearray(0x3a0)
        put(crr, 0x350, 0x380, 1)
        crr[0x380:] = hashlib.sha256(battle[:0x80]).digest()
        return dict(zip(self.resources, (ncch, ex, battle, cro(), code, garc(), crr)))

    def inspect(self, root, blobs, titles):
        for name, data in blobs.items():
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        items = [bytearray(15) for _ in range(640)]
        items[158][14], items[275][14] = 1, 2
        # This test isolates identity/accreditation; binary links and GARC have direct tests above.
        with patch.object(reader.Cro, 'static_links', return_value=[]), \
             patch.object(reader, 'garc_members', return_value=items):
            return reader.inspect(root, dict(resources=self.resources, titles=titles))

    def test_x_y_observation_identity_is_independent_and_never_enables_production(self):
        x, y = '0004000000055D00', '0004000000055E00'
        profiles = {title: dict(sha256={name: hashlib.sha256(data).hexdigest().upper()
                                       for name, data in self.blobs(title).items()}) for title in (x, y)}
        self.assertNotEqual(profiles[x]['sha256']['HeaderNCCH0.bin'], profiles[y]['sha256']['HeaderNCCH0.bin'])
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for title in (x, y):
                with self.subTest(title=title):
                    blobs = self.blobs(title)
                    report = self.inspect(root, blobs, profiles)
                    self.assertEqual(report['title_id'], title)
                    self.assertTrue(report['observation_matches'])
                    self.assertFalse(report['production_enabled'])
                    self.assertFalse(report['installable'])
                    self.assertEqual({name: (root/name).read_bytes() for name in blobs}, blobs)
            crossed = {y: profiles[x]}
            self.assertFalse(self.inspect(root, self.blobs(y), crossed)['observation_matches'])

    def test_unknown_dependencies_stop_before_binary_analysis(self):
        title = '0004000000055D00'
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(reader, 'Cro', side_effect=AssertionError('Untrusted binary analysis')):
                report = self.inspect(Path(directory), self.blobs(title), {title: dict(sha256={})})
        self.assertFalse(report['observation_matches'])
        self.assertFalse(report['installable'])
        self.assertNotIn('candidate_gate_offsets', report)

    def test_missing_and_duplicate_signatures_remain_observations_not_installation(self):
        title = '0004000000055D00'
        for duplicate in (False, True):
            with self.subTest(duplicate=duplicate), tempfile.TemporaryDirectory() as directory:
                blobs = self.blobs(title)
                battle = blobs['ExtractedRomFS/DllBattle.cro']
                if duplicate:
                    for at in (0x200, 0x240):
                        battle[at:at+len(reader.GATE)] = reader.GATE
                    for at in (0x280, 0x400):
                        battle[at:at+len(reader.RESTORE)] = reader.RESTORE
                    hash_cro(battle)
                profiles = {title: dict(sha256={name: hashlib.sha256(data).hexdigest().upper()
                                              for name, data in blobs.items()})}
                report = self.inspect(Path(directory), blobs, profiles)
                self.assertEqual(report['candidate_gate_offsets'], ['0x200', '0x240'] if duplicate else [])
                self.assertEqual(report['candidate_restore_offsets'], ['0x280', '0x400'] if duplicate else [])
                self.assertFalse(report['production_enabled'])
                self.assertFalse(report['installable'])

    def test_project_rejects_unknown_or_mismatched_titles_and_wrong_physical_name(self):
        x = '0004000000055D00'
        profiles = {x: dict(sha256={})}
        for case in ('unknown', 'mismatch', 'name'):
            with self.subTest(case=case), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                blobs = self.blobs(x if case != 'unknown' else '000400000011C400')
                if case == 'mismatch':
                    blobs['DecryptedExHeader.bin'][0x200] ^= 1
                if case == 'name':
                    blobs['ExtractedRomFS/DLLBattle.cro'] = blobs.pop('ExtractedRomFS/DllBattle.cro')
                with self.assertRaises(ValueError):
                    self.inspect(root, blobs, profiles)


if __name__ == '__main__':
    unittest.main()
