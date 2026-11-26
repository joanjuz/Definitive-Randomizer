"""Synthetic parser regressions. No game dumps or original instructions are fixtures."""
import struct
import unittest
from inspect_native_bindings import u32, native_imports, cells, garc_member, name_hash


def header():
    data=bytearray(84)
    struct.pack_into('<I',data,0,len(data))
    struct.pack_into('<I',data,4,0x0a0af1e0)
    struct.pack_into('<HH',data,8,0x10,8)  # flags deliberately are NOT a section-table pointer
    struct.pack_into('<III',data,12,84,84,84)
    struct.pack_into('<III',data,32,60,68,84)  # one public export before two native imports
    struct.pack_into('<II',data,68,0,0x12345678)
    struct.pack_into('<II',data,76,0,0xabcdef01)
    return data


class InspectionTests(unittest.TestCase):
    def test_native_table_uses_header_offsets_not_flags_or_public_entries(self):
        self.assertEqual(native_imports(header()),{0:0x12345678,1:0xabcdef01})

    def test_native_table_rejects_stride_and_bounds(self):
        for offset,value,size in [(10,4,2),(36,61,4),(40,88,4),(40,70,4)]:
            with self.subTest(offset=offset,value=value):
                data=header();struct.pack_into('<H' if size==2 else '<I',data,offset,value)
                with self.assertRaises(ValueError): native_imports(data)

    def test_native_name_hash_matches_independently_identified_names(self):
        self.assertEqual(name_hash(b'MsgLoad'),0xf845bdff)
        self.assertEqual(name_hash(b'Assert_'),0xec163e7d)
        self.assertEqual(name_hash(b'ItemSub'),0x8d5e8e31)
        self.assertNotEqual(name_hash(b'ItemSub'),name_hash(b'ItemAdd'))

    def test_signed_cells_decode_negative_and_multi_group_values(self):
        data=header();data.extend(bytes([0x7f,0x80,0x40,0x00]))
        struct.pack_into('<I',data,20,96)
        self.assertEqual(cells(data),[0xffffffff,64,0])

    def test_cells_reject_unterminated_and_oversized_groups(self):
        for payload in (b'\x80',b'\x80'*6):
            data=header();data.extend(payload);struct.pack_into('<I',data,20,88)
            with self.assertRaises(ValueError):cells(data)

    def test_u32_rejects_negative_and_truncated_offsets(self):
        for at in (-1,1,4):
            with self.subTest(at=at),self.assertRaises(ValueError):u32(b'1234',at)

    def test_garc_checks_magic_before_interpreting_offsets(self):
        with self.assertRaises(ValueError):garc_member(b'wrong-header'*8,0)

    def test_native_table_rejects_foreign_magic(self):
        data=header();data[4]=0
        with self.assertRaises(ValueError):native_imports(data)


if __name__=='__main__':unittest.main()
