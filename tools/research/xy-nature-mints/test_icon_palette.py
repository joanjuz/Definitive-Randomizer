"""Palette regressions without copyrighted resources or emulator execution."""
import struct
import unittest
from replay_icon_palette import synthetic, expand_reference, decompress


class PaletteChecks(unittest.TestCase):
    def test_native_count_partitions(self):
        for count in (1,4,15,16,17,70):
            with self.subTest(count=count):
                source=synthetic(count);expanded=expand_reference(source)
                self.assertEqual(2048,len(expanded))
                colors=struct.unpack_from('<'+'H'*count,source,4)
                self.assertEqual(tuple(colors[i%count] for i in range(1024)),struct.unpack('<'+'H'*1024,expanded))

    def test_high_first_and_rgb5a1_alpha(self):
        source=bytearray(synthetic(4));source[12]=0x12
        struct.pack_into('<HH',source,6,0xf801,0x0001)
        self.assertEqual(b'\x01\xf8\x01\0',expand_reference(source)[:4])
        source[12]=0x21
        self.assertEqual(b'\x01\0\x01\xf8',expand_reference(source)[:4])

    def test_unknown_palette_rejected(self):
        for count in (0,71):
            source=bytearray(2088);struct.pack_into('<HH',source,0,2,count)
            with self.assertRaises(ValueError):expand_reference(source)
        with self.assertRaises(ValueError):expand_reference(bytes(2088))
        source=bytearray(synthetic(4));source[12]=0xf0
        with self.assertRaises(ValueError):expand_reference(source)
        with self.assertRaises(ValueError):expand_reference(source[:-1])

    def test_literal_decompression_and_truncation(self):
        raw=synthetic(17);record=b'\x11\x28\x08\0'+b''.join(b'\0'+raw[i:i+8] for i in range(0,len(raw),8))
        self.assertEqual(raw,decompress(record))
        for mutated in (record[:-1],b'\x11\x28\x08\0\x80\x20\0',b'\x10\x28\x08\0'):
            with self.assertRaises(ValueError):decompress(mutated)


if __name__=='__main__':unittest.main()
