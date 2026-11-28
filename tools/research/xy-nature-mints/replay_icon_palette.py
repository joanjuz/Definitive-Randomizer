"""Read-only replay of original XY palette expansion, not emulator/gameplay testing.

Executes 0x338B04 plus the original memcpy, without stubs, on synthetic RAM.
Pinned X/Y executable inputs remain unchanged. Optional image archives stay private.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path
from inspect_mints import analyze, read_project, verify_unchanged, require, garc_members


def decompress(record):
    require(record[:4] == b'\x11\x28\x08\0', 'Expected LZ11 2088-byte XY icon')
    out=bytearray();at=4
    while len(out)<2088:
        require(at<len(record),'Truncated LZ flags');flags=record[at];at+=1
        for bit in range(7,-1,-1):
            if len(out)==2088:break
            require(at<len(record),'Truncated LZ item');a=record[at];at+=1
            if not (flags>>bit)&1:out.append(a);continue
            require(at<len(record),'Truncated LZ pair');b=record[at];at+=1;kind=a>>4
            if kind==0:
                require(at<len(record),'Truncated LZ medium');c=record[at];at+=1
                length=((a&15)<<4|(b>>4))+0x11;distance=((b&15)<<8|c)+1
            elif kind==1:
                require(at+1<len(record),'Truncated LZ long');c,d=record[at:at+2];at+=2
                length=((a&15)<<12|b<<4|c>>4)+0x111;distance=((c&15)<<8|d)+1
            else:length=kind+1;distance=((a&15)<<8|b)+1
            require(0<distance<=len(out) and len(out)+length<=2088,'Invalid LZ back-reference')
            for _ in range(length):out.append(out[-distance])
    return bytes(out)


def expand_reference(texture):
    require(len(texture)==2088,'Wrong texture size');width,count=struct.unpack_from('<HH',texture)
    require(width==2 and 1<=count<=70,'Missing XY native palette')
    palette=struct.unpack_from('<'+'H'*count,texture,4);start=4+count*2
    indexes=[((texture[start+i//2]>>(4 if i%2==0 else 0))&15) if count<=16 else texture[start+i] for i in range(1024)]
    require(all(i<count for i in indexes),'Palette index outside count')
    return struct.pack('<'+'H'*1024,*(palette[i] for i in indexes))


def native(code,texture):
    from unicorn import Uc, UC_ARCH_ARM, UC_MODE_ARM
    from unicorn.arm_const import UC_ARM_REG_R0,UC_ARM_REG_R1,UC_ARM_REG_R2,UC_ARM_REG_SP,UC_ARM_REG_LR,UC_ARM_REG_PC
    uc=Uc(UC_ARCH_ARM,UC_MODE_ARM);uc.mem_map(0x100000,(len(code)+4095)&~4095);uc.mem_write(0x100000,code)
    uc.mem_map(0x8000000,0x20000);pixels,scratch,context,length,stop=0x8001000,0x8004000,0x8008000,0x8009000,0x800f000
    uc.mem_write(pixels,texture);uc.mem_write(context,struct.pack('<III',pixels,0,length));uc.mem_write(length,struct.pack('<I',2048))
    uc.reg_write(UC_ARM_REG_SP,0x801f000);uc.reg_write(UC_ARM_REG_LR,stop)
    for reg,value in [(UC_ARM_REG_R0,context),(UC_ARM_REG_R1,scratch),(UC_ARM_REG_R2,2048)]:uc.reg_write(reg,value)
    uc.emu_start(0x338b04,stop,count=100000)
    require(uc.reg_read(UC_ARM_REG_PC)==stop and uc.reg_read(UC_ARM_REG_SP)==0x801f000,'Native expansion did not return with intact SP')
    return bytes(uc.mem_read(pixels,2048))


def synthetic(count):
    texture=bytearray(2088);struct.pack_into('<HH',texture,0,2,count)
    for i in range(count):struct.pack_into('<H',texture,4+i*2,0xfffe if i==0 else ((i*0x421)&0xfffe)|1)
    start=4+count*2
    for i in range(1024):
        index=i%count
        if count<=16:texture[start+i//2]|=index<<(4 if i%2==0 else 0)
        else:texture[start+i]=index
    return bytes(texture)


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('project',type=Path);p.add_argument('--archive',type=Path);p.add_argument('--failed-archive',type=Path);p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();profiles=json.loads(Path(__file__).with_name('observed_profiles.json').read_text())
    blobs=read_project(args.project,profiles);report=analyze(blobs,profiles)
    code=blobs['ExeFS/.code.bin'];cases=[]
    for count in [1,4,15,16,17,70]:
        texture=synthetic(count);actual=native(code,texture);require(actual==expand_reference(texture),'Native palette partition differs')
        cases.append({'synthetic_count':count,'exact':True})
    # Actual failed candidate representation: transparent top-left direct pixels,
    # footer-identical but no native palette. The original decoder expands zeros.
    direct=bytearray(2088);direct[512:1024]=b'\xff'*512
    require(native(code,bytes(direct))==bytes(2048),'Direct-pixel empty-icon regression did not reproduce')
    cases.append({'direct_pixels_regression':'all 1024 native pixels become transparent'})
    if args.failed_archive:
        failed=args.failed_archive.read_bytes();old=garc_members(failed);require(len(old)==600,'Expected failed 21-image candidate')
        for index in range(579,600):
            texture=decompress(old[index]);actual=native(code,texture)
            require(all((word&1)==0 for word in struct.unpack('<'+'H'*1024,actual)),'Failed candidate did not reproduce empty native alpha')
            cases.append({'failed_candidate_image':index,'all_native_alpha_transparent':True})
        require(args.failed_archive.read_bytes()==failed,'Failed archive changed concurrently')
    archive=args.archive.read_bytes() if args.archive else blobs['RomFS/a/0/9/4'];records=garc_members(archive)
    indexes=range(579,600) if len(records)==600 else [17,492,493,577,578]
    for index in indexes:
        texture=decompress(records[index]);actual=native(code,texture)
        require(actual==expand_reference(texture),'Native image differs from palette specification')
        visible=sum((word&1)!=0 for word in struct.unpack('<'+'H'*1024,actual))
        require(visible>0,'Native expansion has no visible pixels')
        cases.append({'image':index,'visible_native_pixels':visible,'exact':True,'expanded_sha256':hashlib.sha256(actual).hexdigest()})
    if args.archive:require(args.archive.read_bytes()==archive,'Archive changed concurrently')
    verify_unchanged(args.project,blobs)
    with args.output.open('x',encoding='utf-8') as out:json.dump({'title_id':report['title_id'],'cases':cases,'gameplay_verified':False,'stubs':[]},out,indent=2)
    print(report['title_id'],len(cases),'original ARM palette cases passed; no stubs, no gameplay claim')


if __name__=='__main__':main()
