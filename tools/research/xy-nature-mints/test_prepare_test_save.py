import binascii
import struct
import unittest
from prepare_test_save import seed,validate,SIZE,FOOTER,ITEM_START,ITEM_LENGTH,MEDICINE,CRC_AT,IDS


def fixture():
    data=bytearray(SIZE);data[FOOTER+16:FOOTER+20]=b'FEEB';position=0
    for index in range(55):
        length=(0x2c8 if index==0 else ITEM_LENGTH if index==1 else 4)
        struct.pack_into('<IHH',data,FOOTER+20+index*8,length,index,binascii.crc_hqx(data[position:position+length],0xffff))
        position+=(length+511)&~511
    return data


class SaveCopyChecks(unittest.TestCase):
    def test_seed_has_exactly_21_mints_and_preserves_every_unrelated_byte(self):
        original=bytes(fixture());output=seed(original);validate(output)
        rows=list(struct.iter_unpack('<HH',output[MEDICINE:MEDICINE+256]))
        self.assertEqual([(i,3) for i in IDS]+[(17,7)]+[(0,0)]*42,rows)
        self.assertEqual(original,bytes(fixture()))
        self.assertTrue(all(a==b for i,(a,b) in enumerate(zip(original,output)) if
                            not(MEDICINE<=i<MEDICINE+256 or CRC_AT<=i<CRC_AT+2)))
    def test_bad_checksum_and_non_xy_size_are_rejected(self):
        for mutate in (lambda b:b.__setitem__(0,1),lambda b:b.pop(),lambda b:b.__setitem__(slice(FOOTER+16,FOOTER+20),b'BEEF')):
            data=fixture();mutate(data)
            with self.assertRaises(ValueError):seed(data)
    def test_reserved_item_in_a_different_pocket_is_rejected(self):
        data=fixture();struct.pack_into('<HH',data,ITEM_START,623,1)
        struct.pack_into('<H',data,CRC_AT,binascii.crc_hqx(data[ITEM_START:ITEM_START+ITEM_LENGTH],0xffff))
        with self.assertRaises(ValueError):seed(data)
    def test_duplicate_old_medicine_entries_are_replaced_only_in_the_copy(self):
        data=fixture();struct.pack_into('<HHHH',data,MEDICINE,623,999,623,999)
        struct.pack_into('<H',data,CRC_AT,binascii.crc_hqx(data[ITEM_START:ITEM_START+ITEM_LENGTH],0xffff))
        original=bytes(data);output=seed(data)
        self.assertEqual(original,bytes(data))
        self.assertEqual(1,sum(i==623 for i,n in struct.iter_unpack('<HH',output[MEDICINE:MEDICINE+256])))


if __name__=='__main__':unittest.main()
