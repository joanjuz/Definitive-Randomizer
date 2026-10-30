"""Build our freestanding runtime and convert its ELF to a hashed, uncompressed NSO.
No Nintendo binaries are inputs. Requires an explicitly supplied local Zig 0.13 compiler.
"""
import argparse, hashlib, json, struct, subprocess
from pathlib import Path


def build(zig, output):
    source=Path(__file__).resolve().parent
    output.mkdir(parents=True,exist_ok=False)
    objects=[]
    for name in ("policy.c","adapter.c","module.c","module.S"):
        obj=output/(name+".o")
        cmd=[str(zig),"cc","-target","aarch64-linux-musl","-ffreestanding","-fPIC","-fvisibility=hidden","-fno-stack-protector","-fno-unwind-tables","-fno-asynchronous-unwind-tables","-mno-outline-atomics","-O2","-Wall","-Wextra","-Werror","-c",str(source/name),"-o",str(obj)]
        subprocess.run(cmd,check=True);objects.append(str(obj))
    elf_path=output/"sword-surprise.elf"
    subprocess.run([str(zig),"ld.lld","-m","aarch64elf","-shared","-Bsymbolic","--hash-style=sysv","--no-undefined","-init","dr_module_init","-T",str(source/"module.ld"),*objects,"-o",str(elf_path)],check=True)
    elf=elf_path.read_bytes()
    if elf[:6]!=b"\x7fELF\x02\x01" or struct.unpack_from("<H",elf,18)[0]!=183:raise ValueError("Expected AArch64 ELF64")
    phoff=struct.unpack_from("<Q",elf,32)[0];phsize,phcount=struct.unpack_from("<HH",elf,54)
    segments={}
    for i in range(phcount):
        typ,flags,offset,va,pa,filesize,memsize,align=struct.unpack_from("<II6Q",elf,phoff+i*phsize)
        if typ==1:
            if flags not in (5,4,6) or flags in segments:raise ValueError("Unexpected load segment")
            segments[flags]=(va,elf[offset:offset+filesize],memsize)
    if set(segments)!={4,5,6} or segments[5][0]!=0:raise ValueError("Expected text/rodata/data")
    header=bytearray(256);header[:4]=b"NSO0";struct.pack_into("<I",header,12,0x38);header[64:96]=hashlib.sha256(elf).digest()
    image=bytearray(header)
    for i,flags in enumerate((5,4,6)):
        va,data,memsize=segments[flags]
        while len(image)%16:image.append(0)
        offset=len(image);image+=data
        struct.pack_into("<III",image,16+i*16,offset,va,len(data));struct.pack_into("<I",image,96+i*4,len(data));image[160+i*32:192+i*32]=hashlib.sha256(data).digest()
        if flags==6:struct.pack_into("<I",image,60,memsize-len(data))
    marker=b"DRSURPRISECFG1!\0"
    at=image.find(marker)
    if at<0 or image.find(marker,at+1)>=0:raise ValueError("Unique runtime config marker missing")
    # No undefined symbols in any static/dynamic symbol table.
    shoff=struct.unpack_from("<Q",elf,40)[0];shsize,shcount=struct.unpack_from("<HH",elf,58)
    for i in range(shcount):
        section=struct.unpack_from("<II4QII2Q",elf,shoff+i*shsize)
        if section[1] not in (2,11):continue
        for j in range(section[5]//section[9]):
            name,info,other,index,value,size=struct.unpack_from("<IBBHQQ",elf,section[4]+j*section[9])
            if name and index==0:raise ValueError("Undefined symbol in runtime")
    nso=output/"sword-surprise.nso";nso.write_bytes(image)
    manifest={"format":"DefinitiveRandomizer.SwordSurpriseRuntime","version":1,"sha256":hashlib.sha256(image).hexdigest().upper(),"configOffset":at,"configSize":32+898*2,"elfSha256":hashlib.sha256(elf).hexdigest().upper(),"nativeGameplayValidated":False}
    (output/"sword-surprise.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(manifest,indent=2))

if __name__=="__main__":
    p=argparse.ArgumentParser();p.add_argument("--zig",type=Path,required=True);p.add_argument("--output",type=Path,required=True);a=p.parse_args();build(a.zig,a.output)
