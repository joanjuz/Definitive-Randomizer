"""Synthetic two-condition experiment checks. No ROMs, saves or emulator use."""
import copy
import hashlib
import json
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import experimental_two_gates as experiment
from inspect_persistent_items import Cro, GATE, RESTORE


def original(marker=7):
    data = bytearray((i*11+marker) & 255 for i in range(0x15800))
    data[0x80:0x84] = b'CRO0'
    struct.pack_into('<I', data, 0x90, len(data))
    struct.pack_into('<IIIII', data, 0xb0, 0x180, 0x15000, 0x15400, 0x400, 0x15200)
    struct.pack_into('<II', data, 0xc8, 0x15200, 4)
    for i, segment in enumerate(((0x180, 0x15000, 0), (0x15200, 0x200, 1),
                                 (0x15400, 0x400, 2), (0, 0x40, 3))):
        struct.pack_into('<III', data, 0x15200+i*12, *segment)
    data[0x14848:0x14848+len(GATE)] = GATE
    data[0x14888:0x14888+len(RESTORE)] = RESTORE
    return experiment.rebuild_hashes(data)


def fingerprint(data):
    return hashlib.sha256(data).hexdigest().upper()


def target(at, word):
    immediate = word & 0xffffff
    if immediate & 0x800000:
        immediate -= 0x1000000
    return at+8+immediate*4


class TwoGateTests(unittest.TestCase):
    def test_original_and_patched_words_preserve_both_branch_targets_and_change_only_two_conditions(self):
        data = original()
        result = experiment.patch(data, fingerprint(data))
        expected = ((0x14854, 0x1a000020, 0x8a000020, 0x148dc),
                    (0x148ac, 0x0a000025, 0xea000025, 0x14948))
        for offset, before, after, branch_target in expected:
            with self.subTest(offset=offset):
                self.assertEqual(struct.unpack_from('<I', data, offset)[0], before)
                self.assertEqual(struct.unpack_from('<I', result, offset)[0], after)
                self.assertEqual(target(offset, before), branch_target)
                self.assertEqual(target(offset, after), branch_target)
        self.assertEqual({i for i, (a, b) in enumerate(zip(data, result)) if a != b and i >= 0x80},
                         {0x14857, 0x148af})
        self.assertEqual(len(result), len(data))
        self.assertTrue(Cro(result).hashes_valid())
        for slot, start, size in ((0, 0x80, 0x100), (1, 0x180, 0x15000),
                                  (2, 0x15200, 0x200), (3, 0x15400, 0x400)):
            self.assertEqual(result[slot*32:(slot+1)*32], hashlib.sha256(result[start:start+size]).digest())

    def test_input_is_immutable_installation_idempotent_and_rollback_is_byte_exact(self):
        data = bytearray(original())
        before = bytes(data)
        baseline = fingerprint(data)
        self.assertEqual(experiment.classify(data, baseline), 'original')
        applied = experiment.patch(data, baseline)
        self.assertEqual(bytes(data), before)
        self.assertEqual(experiment.classify(applied, baseline), 'applied-pattern')
        self.assertEqual(experiment.patch(applied, baseline), applied)
        self.assertEqual(experiment.rollback(applied, baseline), before)
        with self.assertRaises(ValueError):
            experiment.rollback(data, baseline)

    def test_each_partial_install_is_recognized_but_never_implicitly_repaired_or_reversed(self):
        data = original()
        baseline = fingerprint(data)
        for offset, word in ((0x14854, 0x8a000020), (0x148ac, 0xea000025)):
            changed = bytearray(data)
            struct.pack_into('<I', changed, offset, word)
            changed = experiment.rebuild_hashes(changed)
            with self.subTest(offset=offset):
                self.assertEqual(experiment.classify(changed, baseline), 'partial')
                for action in (experiment.patch, experiment.rollback):
                    with self.assertRaises(ValueError):
                        action(changed, baseline)

    def test_corrupt_truncated_unknown_instruction_and_bad_segment_header_are_rejected(self):
        data = original()
        cases = [data[:0x7f], data[:0x180], data[:0x14850]]
        corrupt = bytearray(data)
        corrupt[-1] ^= 1
        cases.append(corrupt)
        for offset in (0x14854, 0x148ac):
            changed = bytearray(data)
            struct.pack_into('<I', changed, offset, 0xe1a00000)
            cases.append(experiment.rebuild_hashes(changed))
        header = bytearray(data)
        struct.pack_into('<I', header, 0xb4, len(data))
        cases.append(header)
        for i, changed in enumerate(cases):
            frozen = bytes(changed)
            with self.subTest(case=i), self.assertRaises(ValueError):
                experiment.patch(changed, fingerprint(data))
            self.assertEqual(bytes(changed), frozen)

    def test_absent_or_duplicate_gate_and_restore_signatures_are_rejected_even_with_valid_hashes(self):
        data = original()
        for at, signature, duplicate_at in ((0x14848, GATE, 0x400), (0x14888, RESTORE, 0x500)):
            for duplicate in (False, True):
                changed = bytearray(data)
                if duplicate:
                    changed[duplicate_at:duplicate_at+len(signature)] = signature
                else:
                    changed[at] ^= 1
                changed = experiment.rebuild_hashes(changed)
                for baseline in (fingerprint(data), fingerprint(changed)):
                    with self.subTest(at=at, duplicate=duplicate, baseline=baseline), self.assertRaises(ValueError):
                        experiment.patch(changed, baseline)

    def test_later_external_change_or_crossed_baseline_never_gets_overwritten_by_rollback(self):
        data = original()
        baseline = fingerprint(data)
        applied = experiment.patch(data, baseline)
        changed = bytearray(applied)
        changed[0x15300] ^= 1
        changed = experiment.rebuild_hashes(changed)
        frozen = bytes(changed)
        for action in (experiment.patch, experiment.rollback):
            with self.subTest(action=action.__name__), self.assertRaises(ValueError):
                action(changed, baseline)
            self.assertEqual(bytes(changed), frozen)
        other = original(13)
        for candidate in (data, applied):
            with self.subTest(candidate_hash=fingerprint(candidate)), self.assertRaises(ValueError):
                experiment.patch(candidate, fingerprint(other))


class PreparationTests(unittest.TestCase):
    def inputs(self, directory):
        root = Path(directory)
        project, output = root/'project', root/'candidate'
        cro_path = project/'ExtractedRomFS/DllBattle.cro'
        cro_path.parent.mkdir(parents=True)
        data = original()
        cro_path.write_bytes(data)
        crr_path = project/'ExtractedRomFS/.crr/static.crr'
        crr_path.parent.mkdir()
        crr_path.write_bytes(b'Pure synthetic CRR bytes; never signed or modified')
        identity = dict(observation_matches=True, title_id='0004000000055D00', sha256={
            'ExtractedRomFS/DllBattle.cro': fingerprint(data),
            'ExtractedRomFS/.crr/static.crr': fingerprint(crr_path.read_bytes())})
        return project, output, cro_path, crr_path, identity

    def test_prepare_creates_new_exact_case_outputs_and_leaves_sources_crr_and_admission_unchanged(self):
        with tempfile.TemporaryDirectory() as directory:
            project, output, cro_path, crr_path, identity = self.inputs(directory)
            source = {p: p.read_bytes() for p in project.rglob('*') if p.is_file()}
            frozen_identity = copy.deepcopy(identity)
            with patch.object(experiment, 'inspect', return_value=identity):
                manifest = experiment.prepare(project, output)
            self.assertEqual({p: p.read_bytes() for p in source}, source)
            self.assertEqual(identity, frozen_identity)
            self.assertEqual([p.name for p in (output/'original').iterdir()], ['DllBattle.cro'])
            self.assertEqual([p.name for p in (output/'patched').iterdir()], ['DllBattle.cro'])
            self.assertEqual((output/'original/DllBattle.cro').read_bytes(), source[cro_path])
            self.assertEqual(experiment.rollback((output/'patched/DllBattle.cro').read_bytes(),
                                                identity['sha256']['ExtractedRomFS/DllBattle.cro']), source[cro_path])
            self.assertEqual(crr_path.read_bytes(), source[crr_path])
            self.assertTrue(manifest['crr_unchanged'])
            for field in ('production_enabled', 'gameplay_verified', 'hardware_loader_verified',
                          'crr_candidate_registration_valid'):
                self.assertFalse(manifest[field])
            self.assertEqual([word['branch_target'] for word in manifest['words']], ['0x148dc', '0x14948'])
            self.assertEqual(json.loads((output/'experiment.json').read_text()), manifest)

    def test_y_unknown_profile_and_resource_races_reject_before_any_output_creation(self):
        for case in ('Y', 'unknown', 'cro_race', 'crr_race'):
            with self.subTest(case=case), tempfile.TemporaryDirectory() as directory:
                project, output, cro_path, crr_path, identity = self.inputs(directory)
                if case == 'Y':
                    identity['title_id'] = '0004000000055E00'
                elif case == 'unknown':
                    identity['observation_matches'] = False
                raced = {}
                def inspected(*args):
                    if case.endswith('_race'):
                        path = cro_path if case == 'cro_race' else crr_path
                        changed = bytearray(path.read_bytes())
                        changed[-1] ^= 1
                        path.write_bytes(changed)
                        raced[path] = bytes(changed)
                    return identity
                with patch.object(experiment, 'inspect', side_effect=inspected), self.assertRaises(ValueError):
                    experiment.prepare(project, output)
                self.assertFalse(output.exists())
                for path, changed in raced.items():
                    self.assertEqual(path.read_bytes(), changed)

    def test_existing_destination_and_overlap_never_overwrite_sources_or_old_candidates(self):
        for case in ('existing', 'overlap'):
            with self.subTest(case=case), tempfile.TemporaryDirectory() as directory:
                project, output, cro_path, _, _ = self.inputs(directory)
                before = cro_path.read_bytes()
                if case == 'existing':
                    output.mkdir()
                    (output/'keep.txt').write_text('Existing experiment')
                else:
                    output = project/'inside-source'
                with patch.object(experiment, 'inspect') as inspected, self.assertRaises(ValueError):
                    experiment.prepare(project, output)
                inspected.assert_not_called()
                self.assertEqual(cro_path.read_bytes(), before)
                if case == 'existing':
                    self.assertEqual((output/'keep.txt').read_text(), 'Existing experiment')
                else:
                    self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main()
