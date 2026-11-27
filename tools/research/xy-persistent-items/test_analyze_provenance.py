"""Synthetic diagnostic provenance and ARM inventory checks; never load a ROM."""
import copy
import struct
import unittest

import analyze_provenance as analyze
from gdb_trace import POINTS, ITEM_POINTS


def party(item=158):
    return dict(count=1, members=[dict(index=0, core='0x220000', native_fields=dict(
        species=25, held_item=item, identity_tag='0123456789ABCDEF', checksum_valid=True,
        encrypted_at_capture=True))])


def mutation(offset, item12, item14, core=0x220000):
    raw = bytearray(0x16b)
    struct.pack_into('<I', raw, 0, core)
    struct.pack_into('<HH', raw, 0x12, item12, item14)
    raw[0x19] = 3
    result = dict(event='item_mutation_candidate', file_offset=hex(offset),
                  battle_parameter_pointer='0x440000', opaque_parameter_hex=raw.hex(),
                  item_12=item12, item_14=item14, participant_id_19=3, flags_167=0, flags_16a=0)
    if offset == ITEM_POINTS[0]:
        result.update(requested_item=0, remember_argument=1, return_file_offset_candidate='0x6c1f0')
    return result


def events():
    return [dict(event='session', title_id='0004000000055D00', case='Synthetic', installable=False,
                 item_events_requested=True),
            dict(event='instrumented', points=[hex(p) for p in POINTS+ITEM_POINTS]),
            mutation(ITEM_POINTS[0], 158, 0), mutation(ITEM_POINTS[1], 0, 0), mutation(ITEM_POINTS[2], 0, 158),
            dict(event='breakpoint', file_offset='0x1945c', mode_byte=1, mode_flag_4b=0,
                 parties=dict(after_candidate=party(158))),
            dict(event='breakpoint', file_offset='0x19460', mode_byte=1, mode_flag_4b=0,
                 parties=dict(after_candidate=party(0))),
            dict(event='native_reconciliation_returned'),
            dict(event='cleanup', all_owned_instructions_restored=True),
            dict(event='control_returned', continue_acknowledged=True)]


def replace_raw(event, at, value, fmt='<H'):
    raw = bytearray.fromhex(event['opaque_parameter_hex'])
    struct.pack_into(fmt, raw, at, value)
    event['opaque_parameter_hex'] = raw.hex()


class ProvenanceTests(unittest.TestCase):
    def assert_excluded(self, trace):
        try:
            result = analyze.analyze_events(trace)
        except ValueError:
            return
        self.assertFalse(result['installable'])
        self.assertFalse(result['production_enabled'])
        self.assertTrue(all(not row['hypothesis_candidate'] for row in result['observations']))

    def test_complete_consistent_sitrus_trace_yields_only_a_disabled_hypothesis_without_mutating_events(self):
        trace = events()
        frozen = copy.deepcopy(trace)
        result = analyze.analyze_events(trace)
        self.assertEqual(trace, frozen)
        self.assertFalse(result['installable'])
        self.assertFalse(result['production_enabled'])
        self.assertFalse(result['native_payload_present'])
        self.assertEqual([(r['core'], r['party_index'], r['caller_return'], r['hypothesis_candidate'])
                          for r in result['observations']], [('0x220000', 0, '0x6c1f0', True)])
        blockers = ' '.join(result['blockers']).lower()
        for required in ('direct item writes', 'ownership', 'battle modes', 'crr', 'installer'):
            self.assertIn(required, blockers)

    def test_missing_duplicate_or_failed_capture_proofs_never_admit_a_candidate(self):
        for index in (0, 1, 5, 6, 7, 8, 9):
            for duplicate in (False, True):
                trace = events()
                if duplicate:
                    trace.insert(index, copy.deepcopy(trace[index]))
                else:
                    del trace[index]
                with self.subTest(index=index, duplicate=duplicate):
                    self.assert_excluded(trace)
        for index, field, value in ((0, 'installable', True), (0, 'item_events_requested', False),
                                     (8, 'all_owned_instructions_restored', False), (9, 'continue_acknowledged', False),
                                     (1, 'points', [hex(p) for p in POINTS]), (5, 'structure_error', 'Read failed')):
            trace = events()
            trace[index][field] = value
            with self.subTest(index=index, field=field):
                self.assert_excluded(trace)
        trace = events()
        trace.append(dict(event='cleanup_failed', reason='Synthetic failure'))
        self.assert_excluded(trace)

    def test_adversarial_capture_and_mutation_order_never_admits_a_candidate(self):
        for left, right in ((0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (6, 7), (7, 8), (8, 9)):
            trace = events()
            trace[left], trace[right] = trace[right], trace[left]
            with self.subTest(left=left, right=right):
                self.assert_excluded(trace)

    def test_y_unknown_and_nontrainer_modes_or_flags_never_admit_a_candidate(self):
        for title in ('0004000000055E00', '000400000011C400', None):
            trace = events()
            trace[0]['title_id'] = title
            with self.subTest(title=title):
                self.assert_excluded(trace)
        for index, mode, flag in ((5, 0, 0), (5, 2, 0), (6, 3, 0), (5, 1, 1), (6, None, None)):
            trace = events()
            trace[index].update(mode_byte=mode, mode_flag_4b=flag)
            with self.subTest(index=index, mode=mode, flag=flag):
                self.assert_excluded(trace)

    def test_reconciliation_events_cannot_bypass_mode_checks_by_changing_event_kind(self):
        trace = events()
        for index in (5, 6):
            trace[index].pop('mode_byte')
            trace[index].pop('mode_flag_4b')
            trace[index]['event'] = 'unrelated'
        self.assert_excluded(trace)

    def test_fling_unknown_caller_item_or_nonconsumption_triplets_remain_excluded(self):
        for field, value in (('return_file_offset_candidate', '0x1a474'),
                             ('return_file_offset_candidate', '0x12344'), ('requested_item', 275),
                             ('remember_argument', 0), ('item_12', 275), ('item_14', 158)):
            trace = events()
            trace[2][field] = value
            if field in ('item_12', 'item_14'):
                replace_raw(trace[2], 0x12 if field == 'item_12' else 0x14, value)
            with self.subTest(field=field, value=value):
                self.assert_excluded(trace)
        trace = events()
        trace[5]['parties']['after_candidate']['members'][0]['native_fields']['held_item'] = 275
        self.assert_excluded(trace)
        trace = events()
        trace[6]['parties']['after_candidate']['members'][0]['native_fields']['held_item'] = 158
        self.assert_excluded(trace)

    def test_incomplete_stage_or_mismatched_pointer_participant_never_admits_a_candidate(self):
        for deleted in (2, 3, 4):
            trace = events()
            del trace[deleted]
            with self.subTest(deleted=deleted):
                self.assert_excluded(trace)
        for index, field, value in ((3, 'battle_parameter_pointer', '0x550000'),
                                     (4, 'participant_id_19', 4), (3, 'item_12', 158), (4, 'item_14', 0)):
            trace = events()
            trace[index][field] = value
            if field in ('item_12', 'item_14', 'participant_id_19'):
                replace_raw(trace[index], {'item_12': 0x12, 'item_14': 0x14, 'participant_id_19': 0x19}[field],
                            value, '<B' if field == 'participant_id_19' else '<H')
            with self.subTest(index=index, field=field):
                self.assert_excluded(trace)

    def test_raw_field_or_core_contradiction_cannot_be_hidden_by_json_decoded_fields(self):
        for index, at, value, fmt in ((2, 0x12, 275, '<H'), (3, 0x14, 158, '<H'),
                                      (4, 0x19, 4, '<B'), (3, 0, 0x330000, '<I'), (4, 0, 0x330000, '<I')):
            trace = events()
            replace_raw(trace[index], at, value, fmt)
            with self.subTest(index=index, offset=at):
                self.assert_excluded(trace)

    def test_same_core_additional_mutation_poisons_the_entire_hypothesis(self):
        trace = events()
        extra = copy.deepcopy(trace[2:5])
        extra[0]['return_file_offset_candidate'] = '0x1a474'
        trace[5:5] = extra
        result = analyze.analyze_events(trace)
        self.assertEqual(len(result['observations']), 2)
        self.assertTrue(all(not row['hypothesis_candidate'] for row in result['observations']))

    def test_missing_changed_or_ambiguous_party_identity_never_admits_a_candidate(self):
        for mutation in ('core_changed', 'tag_changed', 'species_changed', 'duplicate_core', 'duplicate_tag',
                         'count_mismatch', 'checksum_invalid', 'decode_error'):
            trace = events()
            before = trace[5]['parties']['after_candidate']
            final = trace[6]['parties']['after_candidate']['members'][0]
            if mutation == 'core_changed':
                final['core'] = '0x330000'
            elif mutation == 'tag_changed':
                final['native_fields']['identity_tag'] = 'FEDCBA9876543210'
            elif mutation == 'species_changed':
                final['native_fields']['species'] = 26
            elif mutation in ('duplicate_core', 'duplicate_tag'):
                other = copy.deepcopy(before['members'][0])
                if mutation == 'duplicate_tag':
                    other['core'] = '0x330000'
                before['members'].append(other)
                before['count'] = 2
            elif mutation == 'count_mismatch':
                before['count'] = 2
            elif mutation == 'checksum_invalid':
                final['native_fields']['checksum_valid'] = False
            else:
                final['decode_error'] = 'Synthetic corrupt data'
            with self.subTest(mutation=mutation):
                self.assert_excluded(trace)

    def test_invalid_native_fields_cannot_be_used_as_ownership_evidence(self):
        for field, value in (('species', 999), ('identity_tag', ''), ('identity_tag', 'NOT_HEX_IDENTITY!')):
            trace = events()
            for index in (5, 6):
                trace[index]['parties']['after_candidate']['members'][0]['native_fields'][field] = value
            with self.subTest(field=field, value=value):
                self.assert_excluded(trace)


def arm_cro(start=0x200):
    data = bytearray(start+0x200)
    data[0x80:0x84] = b'CRO0'
    struct.pack_into('<I', data, 0x90, len(data))
    struct.pack_into('<II', data, 0xc8, 0x180, 4)
    for i, segment in enumerate(((start, 0x40, 0), (start+0x40, 0x40, 1),
                                 (start+0x80, 0x100, 2), (0, 0x10, 3))):
        struct.pack_into('<III', data, 0x180+i*12, *segment)
    return data


def bl(at, target=0x6b140, condition=14):
    return condition << 28 | 0x0b000000 | (((target-at-8)//4) & 0xffffff)


def strh(base=4, source=2, displacement=0x12, condition=14):
    return (condition << 28 | 0x01c000b0 | base << 16 | source << 12 |
            (displacement & 0xf0) << 4 | displacement & 15)


class ArmInventoryTests(unittest.TestCase):
    def test_bl_forward_backward_and_conditional_calls_have_exact_file_and_return_offsets(self):
        for start, condition in ((0x200, 14), (0x6c200, 0)):
            data = arm_cro(start)
            struct.pack_into('<I', data, start, bl(start, condition=condition))
            before = bytes(data)
            result = analyze.audit_instructions(data)
            self.assertEqual(result['direct_call_candidates'], [dict(file_offset=hex(start),
                             return_file_offset=hex(start+4), condition=condition)])
            self.assertFalse(result['complete_write_coverage'])
            self.assertFalse(result['reachability_verified'])
            self.assertEqual(bytes(data), before)

    def test_strh_inventory_includes_only_supported_immediate_nonsp_positive_forms(self):
        data = arm_cro()
        words = [strh(), strh(base=7, source=11, displacement=0x14, condition=1),
                 strh(base=13), strh() ^ (1 << 23), strh() | (1 << 21),
                 strh() ^ (1 << 24), strh() ^ (1 << 22), strh(displacement=0x16),
                 strh(condition=15), bl(0x224, target=0x6b144), bl(0x228, condition=15)]
        for i, word in enumerate(words):
            struct.pack_into('<I', data, 0x200+i*4, word)
        struct.pack_into('<I', data, 0x240, strh())  # Outside declared .text.
        result = analyze.audit_instructions(data)
        self.assertEqual(result['immediate_halfword_store_candidates'], [
            dict(file_offset='0x200', base_register='r4', source_register='r2', displacement='0x12', condition=14),
            dict(file_offset='0x204', base_register='r7', source_register='r11', displacement='0x14', condition=1)])
        self.assertEqual(result['direct_call_candidates'], [])

    def test_corrupt_or_unaligned_cro_segment_is_not_scanned(self):
        for mutation in ('magic', 'header', 'start_alignment', 'size_alignment', 'outside_segment'):
            data = arm_cro()
            if mutation == 'magic':
                data[0x80:0x84] = b'FAIL'
            elif mutation == 'header':
                data = data[:0x100]
            elif mutation == 'start_alignment':
                struct.pack_into('<I', data, 0x180, 0x201)
            elif mutation == 'size_alignment':
                struct.pack_into('<I', data, 0x184, 0x3f)
            else:
                struct.pack_into('<I', data, 0x184, len(data))
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                analyze.audit_instructions(data)


if __name__ == '__main__':
    unittest.main()
