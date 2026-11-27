"""Bounded CFG and store-inventory tests built from synthetic ARM words/CROs."""
import struct
import unittest

import static_flow as flow


NOP, BX_LR = 0xe1a00000, 0xe12fff1e


def cro(words=(), code_size=0x80):
    data = bytearray(0x800)
    data[0x80:0x84] = b'CRO0'
    struct.pack_into('<I', data, 0x90, len(data))
    struct.pack_into('<II', data, 0xc8, 0x180, 4)
    for index, segment in enumerate(((0x200, code_size, 0), (0x300, 0x100, 1),
                                     (0x400, 0x400, 2), (0, 0x80, 3))):
        struct.pack_into('<III', data, 0x180+index*12, *segment)
    struct.pack_into('<II', data, 0x128, 0x600, 0)
    for at in range(0x200, 0x200+code_size, 4):
        struct.pack_into('<I', data, at, NOP)
    for index, word in enumerate(words):
        struct.pack_into('<I', data, 0x200+index*4, word)
    return data


def branch(at, target, condition=14, link=False):
    return condition << 28 | (0x0b000000 if link else 0x0a000000) | (((target-at-8)//4) & 0xffffff)


def relocations(data, records):
    struct.pack_into('<II', data, 0x128, 0x600, len(records))
    for i, record in enumerate(records):
        struct.pack_into('<IBBHI', data, 0x600+i*12, *record)
    return data


def switch():
    # CMP r0,#2; LDRLO pc,[pc,r0,LSL#2]; BX lr; two relocated table slots.
    data = cro((0xe3500002, 0x379ff100, BX_LR, 0, 0))
    struct.pack_into('<II', data, 0x228, 0xe3a00001, BX_LR)
    return relocations(data, ((0xc0, 2, 0, 0, 0x28), (0x100, 2, 0, 0, 0x2c)))


class CfgTests(unittest.TestCase):
    def test_conditional_branch_has_taken_and_false_edges_and_backward_loop_is_bounded(self):
        data = cro((branch(0x200, 0x200, condition=1), BX_LR))
        result = flow.bounded_cfg(data, 0x200, 0x208, max_nodes=2)
        self.assertEqual([node['file_offset'] for node in result['nodes']], ['0x200', '0x204'])
        self.assertEqual([(e['source'], e['target'], e['kind']) for e in result['edges']],
                         [('0x200', '0x200', 'branch'), ('0x200', '0x204', 'condition_false'),
                          ('0x204', None, 'return')])
        self.assertEqual([edge['target'] for edge in result['back_edges']], ['0x200'])
        self.assertFalse(result['global_coverage'])

    def test_direct_call_is_summarized_with_continuation_and_callee_body_is_not_assumed_pure(self):
        data = cro((branch(0x200, 0x230, link=True), BX_LR))
        struct.pack_into('<I', data, 0x230, 0xe5801004)  # Callee could write memory.
        result = flow.bounded_cfg(data, 0x200, 0x240)
        self.assertEqual([node['file_offset'] for node in result['nodes']], ['0x200', '0x204'])
        self.assertIn(dict(source='0x200', target='0x230', kind='call', inside_region=True), result['edges'])
        self.assertIn(dict(source='0x200', target='0x204', kind='call_continuation_overapproximation',
                           inside_region=True), result['edges'])
        self.assertTrue(result['calls_summarized'])
        self.assertFalse(result['global_coverage'])

    def test_pop_pc_returns_and_conditional_return_keeps_false_path(self):
        result = flow.bounded_cfg(cro((0x18bd8010, BX_LR)), 0x200, 0x208)
        self.assertEqual([(e['source'], e['target'], e['kind']) for e in result['edges']],
                         [('0x200', None, 'return'), ('0x200', '0x204', 'condition_false'),
                          ('0x204', None, 'return')])

    def test_indirect_blx_bx_and_pc_write_remain_explicit_unresolved_edges(self):
        result = flow.bounded_cfg(cro((0xe12fff33, 0xe12fff14)), 0x200, 0x208)
        self.assertEqual([(e['source'], e['kind']) for e in result['unresolved']],
                         [('0x200', 'indirect_call'), ('0x204', 'indirect_branch')])
        result = flow.bounded_cfg(cro((0xe1a0f000,)), 0x200, 0x204)
        self.assertEqual(result['unresolved'][0]['kind'], 'unresolved_pc_write')

    def test_store_of_pc_is_a_store_source_not_a_control_transfer(self):
        result = flow.bounded_cfg(cro((0xe580f000, BX_LR)), 0x200, 0x208)
        self.assertEqual([node['file_offset'] for node in result['nodes']], ['0x200', '0x204'])
        self.assertEqual(result['edges'][0]['kind'], 'fallthrough')
        self.assertEqual(result['nodes'][0]['writer']['mnemonic'], 'str')
        self.assertEqual(result['unresolved'], [])

    def test_switch_resolves_only_bounded_abs32_code_relocations_and_exact_count(self):
        result = flow.bounded_cfg(switch(), 0x200, 0x240)
        self.assertEqual([edge['target'] for edge in result['edges'] if edge['kind'] == 'relocated_switch_case'],
                         ['0x228', '0x22c'])
        self.assertEqual([node['file_offset'] for node in result['nodes']],
                         ['0x200', '0x204', '0x208', '0x228', '0x22c'])
        self.assertEqual(result['unresolved'], [])
        self.assertFalse(result['global_coverage'])

    def test_missing_wrong_kind_segment_reserved_or_one_past_end_switch_target_remains_unresolved(self):
        for mutation in ('missing', 'kind', 'segment', 'reserved', 'one_past_end', 'unaligned_target'):
            data = switch()
            links = flow.internal_links(data)
            if mutation == 'missing':
                del links[0x210]
            elif mutation == 'kind':
                links[0x210]['kind'] = 99
            elif mutation == 'segment':
                links[0x210]['segment'] = 1
            elif mutation == 'reserved':
                links[0x210]['reserved'] = 1
            elif mutation == 'one_past_end':
                data = relocations(data, ((0xc0, 2, 0, 0, 0x28), (0x100, 2, 0, 0, 0x80)))
                links = flow.internal_links(data)
                self.assertEqual(links[0x210]['target'], 0x280)  # Retained sentinel, never executable.
            else:
                links[0x210]['target'] = 0x22d
            with self.subTest(mutation=mutation):
                result = flow.bounded_cfg(data, 0x200, 0x240, links=links)
                self.assertFalse(any(edge['kind'] == 'relocated_switch_case' for edge in result['edges']))
                self.assertTrue(result['unresolved'])

    def test_switch_rejects_conditional_guard_outside_entry_bypass_and_unbounded_count(self):
        for mutation in ('conditional_cmp', 'guard_outside_region', 'bypass', 'count_zero', 'count_260'):
            data = switch()
            start = 0x200
            if mutation == 'conditional_cmp':
                struct.pack_into('<I', data, 0x200, 0x13500002)  # CMPNE does not always define flags.
            elif mutation == 'guard_outside_region':
                start = 0x204
            elif mutation == 'bypass':
                # Reach LDRLO directly, skipping a physically adjacent CMP.
                data = cro((branch(0x200, 0x20c), NOP, 0xe3500002, 0x379ff100, BX_LR, 0, 0))
                struct.pack_into('<II', data, 0x228, 0xe3a00001, BX_LR)
                relocations(data, ((0x140, 2, 0, 0, 0x28), (0x180, 2, 0, 0, 0x2c)))
            else:
                struct.pack_into('<I', data, 0x200, 0xe3500000 if mutation == 'count_zero' else 0xe3500f41)
            with self.subTest(mutation=mutation):
                result = flow.bounded_cfg(data, start, 0x240)
                self.assertFalse(any(edge['kind'] == 'relocated_switch_case' for edge in result['edges']))
                self.assertTrue(result['unresolved'])

    def test_budget_boundary_and_invalid_region_or_reachable_opcode_fail_without_partial_graph(self):
        data = cro((NOP, BX_LR))
        self.assertEqual(len(flow.bounded_cfg(data, 0x200, 0x208, max_nodes=2)['nodes']), 2)
        for budget in (1, 0, -1, True, 1.5):
            with self.subTest(budget=budget), self.assertRaises(ValueError):
                flow.bounded_cfg(data, 0x200, 0x208, max_nodes=budget)
        for start, end in ((0x1fc, 0x208), (0x201, 0x208), (0x200, 0x209), (0x200, 0x284), (0x208, 0x200)):
            with self.subTest(start=start, end=end), self.assertRaises(ValueError):
                flow.bounded_cfg(data, start, end)
        with self.assertRaises(ValueError):
            flow.bounded_cfg(cro((0xffffffff,)), 0x200, 0x204)


class RelocationTests(unittest.TestCase):
    def test_internal_relocations_retain_unknown_kind_bss_and_one_past_end_without_semantic_claims(self):
        data = relocations(cro(), ((0, 99, 3, 7, 0x80), (0x40, 2, 0, 0, 0x80)))
        result = flow.internal_links(data)
        self.assertEqual(result[0x200], dict(kind=99, segment=3, reserved=7, addend=0x80,
                                           target=None, record=0x600))
        self.assertEqual(result[0x204]['target'], 0x280)

    def test_internal_relocations_reject_duplicate_destinations_abs32_width_bounds_and_bad_segments(self):
        cases = [((0, 2, 0, 0, 0), (0, 2, 0, 0, 4)),
                 ((0x7f0, 2, 0, 0, 0),), ((0x10, 2, 0, 0, 0),), ((0x800, 2, 0, 0, 0),),
                 ((0, 2, 4, 0, 0),), ((0, 2, 0, 0, 0x81),)]
        for records in cases:
            with self.subTest(records=records), self.assertRaises(ValueError):
                flow.internal_links(relocations(cro(), records))
        data = cro()
        struct.pack_into('<I', data, 0x12c, 10000)
        with self.assertRaises(ValueError):
            flow.internal_links(data)

    def test_callback_command_ids_are_preserved_without_invented_move_or_reachability_labels(self):
        data = cro()
        struct.pack_into('<IIIIII', data, 0x400, 77, 0, 0x10, 0xdead, 0, 0x20)
        links = {0x404: dict(kind=99, target=0x220)}
        self.assertEqual(flow.callback_records(data, links, start=0x400, count=2),
                         [dict(record='0x400', command_id=77, callback='0x220', relocation_kind=99, descriptor_adjustment=0x10),
                          dict(record='0x40c', command_id=0xdead, callback=None, relocation_kind=None, descriptor_adjustment=0x20)])
        with self.assertRaises(ValueError):
            flow.callback_records(data, links, start=0x7fc, count=1)


class WriterTests(unittest.TestCase):
    def test_store_forms_keep_raw_addressing_and_never_assert_pokemon_semantics(self):
        words = (0xe1c421b2, 0xe1e421b2, 0xe0c421b2, 0xe7801102, 0xe5a01004, 0xe4801004, 0xe8a0000e, 0xe92d4010)
        data = cro(words)
        struct.pack_into('<I', data, 0x300, 0xe1c421b2)  # rodata is not scanned as code.
        result = flow.write_inventory(data)
        self.assertEqual([r['file_offset'] for r in result], [hex(0x200+4*i) for i in range(8)])
        self.assertEqual([r['mnemonic'] for r in result], ['strh', 'strh', 'strh', 'str', 'str', 'str', 'stm', 'push'])
        self.assertEqual([r['writeback'] for r in result], [False, True, True, False, True, True, True, False])
        self.assertEqual((result[0]['base'], result[0]['displacement']), ('r4', 0x12))
        self.assertEqual((result[3]['base'], result[3]['index'], result[3]['shift_value']), ('r0', 'r2', 2))
        self.assertEqual(result[2]['operands'], 'r2, [r4], #0x12')
        self.assertIn('extent unresolved', result[6]['addressing'])
        self.assertIn('extent unresolved', result[7]['addressing'])
        self.assertTrue(all(r['semantic_type'] == 'unclassified' for r in result))


class ReferenceAndPathTests(unittest.TestCase):
    def test_direct_references_distinguish_b_bl_signed_displacements_and_ignore_nv_or_outside_text(self):
        data = cro((branch(0x200, 0x1f0, condition=1), branch(0x204, 0x228, link=True),
                    branch(0x208, 0x228, condition=15)))
        struct.pack_into('<I', data, 0x300, branch(0x300, 0x228, link=True))
        self.assertEqual(flow.direct_references(data, {0x1f0, 0x228}),
                         [dict(source='0x200', target='0x1f0', kind='B', condition=1),
                          dict(source='0x204', target='0x228', kind='BL', condition=14)])
        self.assertEqual(flow.direct_references(data, {0x500}), [])

    def test_possible_path_traverses_branch_cycles_but_not_calls_indirect_calls_or_external_edges(self):
        graph = dict(edges=[
            dict(source='0x200', target='0x300', kind='call', inside_region=True),
            dict(source='0x200', target='0x400', kind='indirect_call', inside_region=True),
            dict(source='0x200', target='0x500', kind='branch', inside_region=False),
            dict(source='0x200', target='0x204', kind='fallthrough', inside_region=True),
            dict(source='0x204', target='0x208', kind='branch', inside_region=True),
            dict(source='0x208', target='0x204', kind='branch', inside_region=True),
            dict(source='0x208', target='0x20c', kind='condition_false', inside_region=True)])
        self.assertTrue(flow.possible_path(graph, '0x200', '0x20c'))
        for target in ('0x300', '0x400', '0x500', '0x600'):
            with self.subTest(target=target):
                self.assertFalse(flow.possible_path(graph, '0x200', target))


if __name__ == '__main__':
    unittest.main()
