"""Seed ONLY a new, isolated XY emulator save copy, never a habitual save.

Independent implementation of the documented XY inventory/BEEF layout;
references: PKHeX a5311f9f55a84376fd2e2499b25d9fb9640d9f2d,
PlayerBag6XY.cs, SaveBlockAccessor6XY.cs, BlockInfo3DS.cs. No PKHeX code/data
bundled. CRC uses Python's standard crc_hqx. Test medicine pocket is replaced
with 21 mints x3 plus Potion x7. All other bytes except the item-block CRC stay
exact. No Pokemon, progression, money, timestamps, keys or network state edited.
"""
import argparse
import binascii
import hashlib
import json
import struct
from pathlib import Path
from inspect_mints import no_links, require

IDS=[113,114,115,*range(120,134),426,427,622,623]
SIZE,FOOTER=0x65600,0x65400
ITEM_START,ITEM_LENGTH,MEDICINE=0x400,0xb88,0x400+0x968
CRC_AT=FOOTER+0x14+8+6


def validate(data):
    require(len(data)==SIZE and data[FOOTER+16:FOOTER+20]==b'FEEB','Not a recognized raw XY save')
    position=0
    for index in range(55):
        length,ident,crc=struct.unpack_from('<IHH',data,FOOTER+20+index*8)
        require(ident==index and length>0 and position+length<=FOOTER,'Invalid save block metadata')
        require(binascii.crc_hqx(data[position:position+length],0xffff)==crc,'Invalid source block checksum')
        if index==1:require(position==ITEM_START and length==ITEM_LENGTH,'Unexpected XY item block')
        position+=(length+0x1ff)&~0x1ff


def seed(data):
    validate(data)
    # Refuse to hide an old hacked/reserved item in a different pocket.
    for offset,count in ((0,400),(0x640,96),(0x7c0,106),(0xa68,72)):
        rows=struct.iter_unpack('<HH',data[ITEM_START+offset:ITEM_START+offset+count*4])
        require(not any(item in IDS and quantity for item,quantity in rows),'Reserved mint ID already exists outside medicine')
    result=bytearray(data)
    result[MEDICINE:MEDICINE+256]=bytes(256)
    for slot,(item,count) in enumerate([(i,3) for i in IDS]+[(17,7)]):
        struct.pack_into('<HH',result,MEDICINE+slot*4,item,count)
    struct.pack_into('<H',result,CRC_AT,binascii.crc_hqx(result[ITEM_START:ITEM_START+ITEM_LENGTH],0xffff))
    validate(result)
    require(all(a==b or MEDICINE<=i<MEDICINE+256 or CRC_AT<=i<CRC_AT+2
                for i,(a,b) in enumerate(zip(data,result))),'Test preparation changed unrelated bytes')
    return bytes(result)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root',type=Path);parser.add_argument('source_backup',type=Path)
    parser.add_argument('new_save',type=Path);parser.add_argument('--report',type=Path,required=True)
    args=parser.parse_args();root=args.root.resolve();source=args.source_backup.resolve();target=args.new_save.resolve();report=args.report.resolve()
    require(root.name.startswith('PruebasMentasXY-X-'),'First save candidate is X only')
    require(source.is_relative_to(root/'evidence') and target.is_relative_to(root/'emulator'/'user') and
            report.parent==root/'evidence' and target.name=='main','Explicit isolated source/output paths required')
    no_links(source);no_links(root)
    marker=json.loads((root/'evidence/source.json').read_text(encoding='utf-8-sig'))
    require(marker['Purpose']=='xy-mint-isolated-research-v1','Missing isolated research marker')
    require(not target.exists() and not report.exists(),'Output must be new')
    before=source.read_bytes();output=seed(before)
    for parent in target.parents:
        if parent.exists():no_links(parent)
    target.parent.mkdir(parents=True,exist_ok=True);no_links(target.parent)
    require(source.read_bytes()==before,'Backup changed concurrently')
    with target.open('xb') as stream:stream.write(output)
    require(target.read_bytes()==output and source.read_bytes()==before,'Readback/source verification failed')
    with report.open('x',encoding='utf-8') as stream:
        json.dump({'purpose':'isolated mint medicine test supply','source_sha256':hashlib.sha256(before).hexdigest(),
                   'test_save_sha256':hashlib.sha256(output).hexdigest(),'mint_ids':IDS,'count_each':3,
                   'only_medicine_and_item_crc_changed':True,'all_55_block_checksums_valid':True,
                   'gameplay_verified':False},stream,indent=2)
    print('New isolated test save created; only medicine and item CRC changed, 55/55 checksums valid.')


if __name__=='__main__':main()
