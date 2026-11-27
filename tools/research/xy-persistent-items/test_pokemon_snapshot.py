"""Synthetic Gen6 accessor decoding checks; no saves, ROM data or emulator access."""
import itertools
import struct
import unittest

import gdb_trace
from pokemon_snapshot import block_a_positions, decode_block_a


POSITIONS = tuple((3 * i + 1) % 4 for i in range(32))
ENCRYPTED = bytes.fromhex(
    '78563412000061847201fb9c95ffcdc0bf647ec8fbcfe0696f098eeadd5275b42cc4e358e5eaef692e55e9b81ebc78ba57c4741a'
    '116249c9d787fa553ff9be06a420beaf1d8a6fad56ce2448f30c9d1ee6d6afc7842c039e154a8574bb725e967cd032e11988021'
    'a17ceda6c2032af3a685e65928f3e70169d9cb3783cd264e1d11f386793ae45e8db0a326b3038b7b2b054014d4ec23c9979034f36c'
    'd1caa557dff78126690f2b18cb6748c6eb227077b4fcdfcbc2854a6f53561c7baf0b3cea06c63aeede508b467ff6adfe5b1096b4d2'
    '3ecb1720c03b403103289ad155234c610fcb3d22466b7')


def plain(slot=2, species=25, item=158, ec=None, owner=0x10203040, pid=0x55667788, sanity=0):
    if ec is None:
        ec = (slot << 13) | 0x567
    body = bytearray(b'\xff' * 224)
    block = POSITIONS[(ec >> 13) & 31] * 56
    struct.pack_into('<HHI', body, block, species, item, owner)
    struct.pack_into('<I', body, block + 16, pid)
    checksum = sum(int.from_bytes(body[i:i+2], 'little') for i in range(0, 224, 2)) % 65536
    return bytearray(struct.pack('<IHH', ec, sanity, checksum) + body)


class BlockTableTests(unittest.TestCase):
    def table_code(self):
        base, address = 0x16b000, 0x16b400
        rows = list(itertools.permutations(range(4)))
        rows += rows[:8]
        table = bytes(position for row in rows for position in row)
        code = bytearray(0x480)
        struct.pack_into('<I', code, 0x3d0, address)
        code[0x400:0x480] = table
        return code, base, address, table, rows

    def test_reads_all_32_native_rows_from_literal_pointer_including_exact_end_boundary(self):
        code, base, address, table, rows = self.table_code()
        before = bytes(code)
        positions, actual_address, actual_table = block_a_positions(code, base)
        self.assertEqual(positions, tuple(row[0] for row in rows))
        self.assertEqual(actual_address, address)
        self.assertEqual(actual_table, table)
        self.assertEqual(bytes(code), before)

    def test_rejects_literal_or_table_bounds_and_every_invalid_row(self):
        for mutation in ('missing_literal', 'negative_literal', 'table_before_base', 'table_overrun',
                         'last_row_duplicate', 'first_row_outside_range'):
            code, base, address, _, _ = self.table_code()
            if mutation == 'missing_literal':
                code = code[:0x3d3]
            elif mutation == 'negative_literal':
                base = 0x16b3d1
            elif mutation == 'table_before_base':
                struct.pack_into('<I', code, 0x3d0, base - 1)
            elif mutation == 'table_overrun':
                struct.pack_into('<I', code, 0x3d0, address + 1)
            elif mutation == 'last_row_duplicate':
                code[-4:] = b'\x00\x00\x02\x03'
            else:
                code[0x400:0x404] = b'\x00\x01\x02\x04'
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                block_a_positions(code, base)


class PokemonDecodeTests(unittest.TestCase):
    def test_all_32_selectors_read_the_selected_block_and_leave_input_unchanged(self):
        for slot in range(32):
            raw = plain(slot, species=1+slot, item=600+slot)
            before = bytes(raw)
            with self.subTest(slot=slot):
                result = decode_block_a(raw, 0, POSITIONS, 639)
                self.assertEqual(result['species'], 1+slot)
                self.assertEqual(result['held_item'], 600+slot)
                self.assertTrue(result['checksum_valid'])
                self.assertFalse(result['encrypted_at_capture'])
                self.assertEqual(bytes(raw), before)

    def test_fixed_ciphertext_vector_checks_lcg_seed_order_shuffle_and_checksum(self):
        # Fixed independently prepared synthetic vector: EC12345678, selector2,
        # blockA at168, checksum8461, PID55667788 and owner10203040.
        raw = bytearray(ENCRYPTED)
        result = decode_block_a(raw, 1, POSITIONS, 639)
        self.assertEqual(result, dict(species=25, held_item=158, identity_tag='2109DA26DCABFBCE',
                                     checksum_valid=True, encrypted_at_capture=True))
        self.assertEqual(bytes(raw), ENCRYPTED)
        self.assertEqual(decode_block_a(plain(ec=0x12345678), 0, POSITIONS, 639)['identity_tag'], result['identity_tag'])
        with self.assertRaises(ValueError):
            decode_block_a(ENCRYPTED, 0, POSITIONS, 639)

    def test_rejects_wrong_lengths_checksum_and_native_corruption_flag(self):
        cases = [('truncated', plain()[:-1]), ('extended', plain()+b'\0')]
        changed_body = plain()
        changed_body[-1] ^= 1
        changed_checksum = plain()
        changed_checksum[6] ^= 1
        changed_cipher = bytearray(ENCRYPTED)
        changed_cipher[-1] ^= 1
        cases += [('body', changed_body), ('checksum', changed_checksum)]
        for name, raw in cases:
            with self.subTest(case=name), self.assertRaises(ValueError):
                decode_block_a(raw, 0, POSITIONS, 639)
        with self.assertRaises(ValueError):
            decode_block_a(changed_cipher, 1, POSITIONS, 639)
        for sanity in (4, 5, 6, 0xffff):
            with self.subTest(sanity=sanity), self.assertRaises(ValueError):
                decode_block_a(plain(sanity=sanity), 0, POSITIONS, 639)

    def test_rejects_unknown_encryption_flag_bad_position_catalog_and_item_limits(self):
        for flag in (-1, 2, '1', None):
            with self.subTest(flag=flag), self.assertRaises(ValueError):
                decode_block_a(plain(), flag, POSITIONS, 639)
        for positions in (POSITIONS[:-1], POSITIONS+(0,), (-1,)+POSITIONS[1:],
                          (4,)+POSITIONS[1:], ('0',)+POSITIONS[1:]):
            with self.subTest(positions=positions), self.assertRaises(ValueError):
                decode_block_a(plain(), 0, positions, 639)
        for limit in (-1, 65536, '639', None):
            with self.subTest(limit=limit), self.assertRaises(ValueError):
                decode_block_a(plain(), 0, POSITIONS, limit)

    def test_species_and_item_catalog_boundaries_include_no_item(self):
        for species, item, limit in ((1, 0, 0), (721, 639, 639), (25, 65535, 65535)):
            with self.subTest(species=species, item=item):
                result = decode_block_a(plain(species=species, item=item), 0, POSITIONS, limit)
                self.assertEqual((result['species'], result['held_item']), (species, item))
        for species, item, limit in ((0, 0, 639), (722, 0, 639), (25, 640, 639), (25, 1, 0)):
            with self.subTest(species=species, item=item), self.assertRaises(ValueError):
                decode_block_a(plain(species=species, item=item), 0, POSITIONS, limit)

    def test_identity_tag_survives_item_consumption_and_distinguishes_identity_components(self):
        initial = decode_block_a(plain(item=158), 0, POSITIONS, 639)['identity_tag']
        self.assertRegex(initial, r'^[0-9A-F]{16}$')
        self.assertEqual(decode_block_a(plain(item=0), 0, POSITIONS, 639)['identity_tag'], initial)
        self.assertEqual(decode_block_a(plain(item=275), 0, POSITIONS, 639)['identity_tag'], initial)
        for arguments in (dict(ec=0x12345678), dict(owner=0x10203041), dict(pid=0x55667789)):
            with self.subTest(arguments=arguments):
                self.assertNotEqual(decode_block_a(plain(**arguments), 0, POSITIONS, 639)['identity_tag'], initial)


class SnapshotIntegrationTests(unittest.TestCase):
    def setup_memory(self, flag=1, boxed=ENCRYPTED):
        context, meta, before, after, core, accessor, data = (0x100000+i*0x1000 for i in range(7))
        metadata = bytearray(0x60)
        struct.pack_into('<I', metadata, 0x4c, before)
        raw_core = bytearray(0x20)
        struct.pack_into('<I', raw_core, 0xc, accessor)
        raw_accessor = bytearray(0xe8)
        struct.pack_into('<I', raw_accessor, 8, data)
        raw_accessor[12] = flag
        memory = {(context+0x10, 4): struct.pack('<I', meta), (meta, 0x60): metadata,
                  (context+0x815, 1): b'\x01', (context+0x49c+4, 4): struct.pack('<I', after),
                  (before+0x18, 1): b'\x01', (after+0x18, 1): b'\x00',
                  (before, 4): struct.pack('<I', core), (core, 0x20): raw_core,
                  (accessor, 0xe8): raw_accessor, (accessor, 0x10): raw_accessor[:0x10], (data, 0xe8): boxed}
        class Reader:
            def memory(self, address, count):
                if (address, count) not in memory:
                    raise ValueError('Synthetic memory missing')
                return memory[(address, count)]
        return Reader(), dict(r0=context), data, raw_accessor, memory

    def test_snapshot_follows_accessor_plus8_and_records_only_checksum_valid_native_fields(self):
        reader, regs, data, accessor, _ = self.setup_memory()
        result = gdb_trace.snapshot(reader, regs, 0x1469c,
                                    pokemon_decoder=lambda raw, flag: decode_block_a(raw, flag, POSITIONS, 639))
        member = result['parties']['before_candidate']['members'][0]
        self.assertEqual(member['data_pointer'], hex(data))
        self.assertEqual(member['opaque_pokemon_hex'], accessor.hex())
        self.assertEqual(member['opaque_boxed_hex'], ENCRYPTED.hex())
        self.assertEqual(member['native_fields'], dict(species=25, held_item=158, identity_tag='2109DA26DCABFBCE',
                                                     checksum_valid=True, encrypted_at_capture=True))

    def test_snapshot_keeps_opaque_member_and_never_invents_fields_after_decode_failure(self):
        for case in ('checksum', 'flag', 'missing_data'):
            with self.subTest(case=case):
                boxed = bytearray(ENCRYPTED)
                if case == 'checksum':
                    boxed[6] ^= 1
                reader, regs, data, accessor, memory = self.setup_memory(2 if case == 'flag' else 1, boxed)
                if case == 'missing_data':
                    del memory[(data, 0xe8)]
                result = gdb_trace.snapshot(reader, regs, 0x1469c,
                                           pokemon_decoder=lambda raw, flag: decode_block_a(raw, flag, POSITIONS, 639))
                member = result['parties']['before_candidate']['members'][0]
                self.assertIn('decode_error', member)
                self.assertNotIn('native_fields', member)
                self.assertEqual(member['opaque_pokemon_hex'], accessor.hex())


if __name__ == '__main__':
    unittest.main()
