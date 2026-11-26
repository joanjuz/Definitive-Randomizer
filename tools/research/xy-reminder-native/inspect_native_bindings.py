"""Read-only X/Y AMX import investigation. Never writes game resources.

Print metadata by default; --disassemble VA LENGTH emits a private ARM listing.
Do not commit listings or game resources. Requires capstone only for disassembly.
"""
import argparse
import hashlib
import json
import struct
import re
from pathlib import Path


def u32(b, at):
    if at < 0 or at > len(b)-4:
        raise ValueError('32-bit field outside resource')
    return struct.unpack_from('<I', b, at)[0]


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def name_hash(name):
    value = 0
    for byte in name:
        value = ((value * 131) ^ byte) & 0xffffffff
    return value


def native_imports(amx):
    require(len(amx)>=60 and u32(amx,4)==0x0a0af1e0, 'AMX header')
    stride=struct.unpack_from('<H',amx,10)[0]
    start,end=u32(amx,36),u32(amx,40)
    require(stride==8 and 60<=start<=end<=u32(amx,12)<=len(amx) and (end-start)%stride==0, 'AMX native table bounds')
    return {i:u32(amx,at+4) for i,at in enumerate(range(start,end,stride))}


def garc_member(b, index):
    require(b[:4] == b'CRAG','GARC magic')
    head = u32(b, 4)
    count = struct.unpack_from('<H', b, head + 8)[0]
    require(0 <= index < count,'GARC member index')
    fatb = head + u32(b, head + 4)
    entry = fatb + 12 + u32(b, head + 12 + index * 4)
    require(u32(b, entry) == 1,'Expected single-file GARC member')
    start, end, length = struct.unpack_from('<III', b, entry + 4)
    require(0 <= length <= end - start,'GARC member length')
    at = u32(b, 16) + start
    require(at + length <= len(b),'GARC member bounds')
    return b[at:at + length]


def cells(b):
    require(u32(b, 4) == 0x0A0AF1E0,'AMX magic')
    at = u32(b, 12)
    end=u32(b,20)
    require(60<=at<=len(b) and end>=at and (end-at)%4==0 and (end-at)//4<=len(b)-at,'AMX cell bounds')
    count = (end - at) // 4
    out = []
    for _ in range(count):
        require(at<len(b),'AMX truncated cell')
        byte = b[at]
        at += 1
        value = byte & 127
        if value & 64:
            value -= 128
        groups = 1
        while byte & 128:
            require(groups < 5 and at<len(b),'AMX malformed cell')
            byte = b[at]
            at += 1
            value = ((value << 7) | (byte & 127)) & 0xFFFFFFFF
            groups += 1
        out.append(value & 0xFFFFFFFF)
    return out


def hits(b, value):
    needle = struct.pack('<I', value)
    result = []
    at = b.find(needle)
    while at >= 0:
        result.append(at)
        at = b.find(needle, at + 1)
    return result


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('project', type=Path)
    ap.add_argument('--disassemble', nargs=2, metavar=('VA', 'LENGTH'))
    ap.add_argument('--thumb', action='store_true')
    ap.add_argument('--cro', help='Disassemble a top-level CRO by file offset instead of code.bin VA')
    ap.add_argument('--relocations',nargs=2,metavar=('START','LENGTH'),help='Private CRO pointer metadata in range')
    ap.add_argument('--amx-cells',nargs=2,metavar=('START','LENGTH'),help='Private decoded AMX cell listing')
    ap.add_argument('--scan-resources', action='store_true')
    ap.add_argument('--xref-string')
    ap.add_argument('--word-refs', help='Private literal word references in code.bin')
    ap.add_argument('--read-words',nargs='+',help='Private code.bin pointer-table words by VA')
    ap.add_argument('--imports',action='store_true',help='Private imported symbol names and relocation batches')
    ap.add_argument('--link-slots',nargs='+',help='Resolve CRO import write slots to static CRS indexed exports')
    ap.add_argument('--resolve-names', action='store_true', help='Match native name hashes to strings in private CRO resources')
    ap.add_argument('--profile',action='store_true',help='Metadata and hashes only; no production admission')
    args = ap.parse_args()
    code = (args.project / 'ExtractedExeFS/.code.bin').read_bytes()
    amx = garc_member((args.project / 'ExtractedRomFS/a/0/3/1').read_bytes(), 42)
    words = cells(amx)
    ex = (args.project / 'DecryptedExHeader.bin').read_bytes()
    base = u32(ex, 0x10)
    if args.profile:
        resources=['DllField.cro','DllWazaOmoidashi.cro','DllPokeList.cro','DllWazaOboe.cro','static.crs']
        print(json.dumps(dict(title_id=f'{struct.unpack_from("<Q",ex,0x200)[0]:016X}',
                              code_sha256=hashlib.sha256(code).hexdigest().upper(),
                              common42_sha256=hashlib.sha256(amx).hexdigest().upper(),
                              cro_sha256={name:hashlib.sha256((args.project/'ExtractedRomFS'/name).read_bytes()).hexdigest().upper() for name in resources},
                              production_allowed=False,gameplay='not tested'),indent=2));return
    if args.read_words:
        print(json.dumps({v:hex(u32(code,int(v,0)-base)) for v in args.read_words}));return
    if args.link_slots:
        if not args.cro or Path(args.cro).name!=args.cro: raise ValueError('Expected --cro')
        data=(args.project/'ExtractedRomFS'/args.cro).read_bytes()
        seg=[struct.unpack_from('<III',data,u32(data,0xc8)+i*12) for i in range(u32(data,0xcc))]
        static=(args.project/'ExtractedRomFS/static.crs').read_bytes()
        sseg=[struct.unpack_from('<III',static,u32(static,0xc8)+i*12) for i in range(u32(static,0xcc))]
        wanted={int(v,0) for v in args.link_slots};result=[]
        for i in range(u32(data,0xf4)):
            name,idx,count,anon,nanon=struct.unpack_from('<IIIII',data,u32(data,0xf0)+i*20)
            module=data[name:data.find(b'\0',name)].decode('ascii','replace')
            entries=[(False,*struct.unpack_from('<II',data,idx+j*8)) for j in range(count)]
            entries += [(True,*struct.unpack_from('<II',data,anon+j*8)) for j in range(nanon)]
            for anonymous,ordinal,batch in entries:
                cursor=batch
                for k in range(100000):
                    slot,kind,addend=struct.unpack_from('<III',data,cursor);cursor+=12
                    if (slot&15)<3:
                        at=seg[slot&15][0]+(slot>>4)
                        if at in wanted:
                            tag=ordinal if anonymous else u32(static,u32(static,0xd8)+ordinal*4)
                            target=sseg[tag&15][0]+(tag>>4) if module=='|static|' else None
                            result.append(dict(slot=hex(at),module=module,anonymous=anonymous,index_or_tag=hex(ordinal),kind=hex(kind),addend=hex(addend),target=None if target is None else hex(target)))
                    if kind&0xff00:break
        print(json.dumps(dict(static_segments=sseg,links=result),indent=2));return
    if args.imports:
        if not args.cro or Path(args.cro).name!=args.cro: raise ValueError('Expected --cro')
        data=(args.project/'ExtractedRomFS'/args.cro).read_bytes()
        result=[]
        for i in range(u32(data,0x104)):
            name,batch=struct.unpack_from('<II',data,u32(data,0x100)+i*8)
            end=data.find(b'\0',name)
            patches=[];cursor=batch
            for j in range(100000):
                a,b,c=struct.unpack_from('<III',data,cursor)
                patches.append(dict(slot=hex(a),kind=hex(b),addend=hex(c)))
                cursor+=12
                if b&0xff00:break
            result.append(dict(name=data[name:end].decode('ascii','replace'),batch=hex(batch),patches=patches))
        print(json.dumps(result,indent=2));return
    if args.word_refs:
        print(json.dumps([hex(base+at) for at in hits(code,int(args.word_refs,0))]));return
    if args.relocations:
        if not args.cro or Path(args.cro).name!=args.cro: raise ValueError('Expected --cro file name')
        data=(args.project/'ExtractedRomFS'/args.cro).read_bytes()
        seg=[struct.unpack_from('<III',data,u32(data,0xc8)+i*12) for i in range(u32(data,0xcc))]
        start,length=(int(v,0) for v in args.relocations)
        result=[]
        for i in range(u32(data,0x12c)):
            slot,kind,addend=struct.unpack_from('<III',data,u32(data,0x128)+i*12)
            if (slot&15)<3:
                at=seg[slot&15][0]+(slot>>4)
                if start<=at<start+length:
                    target=seg[(kind>>8)&255][0]+addend if ((kind>>8)&255)<3 else None
                    result.append(dict(slot=hex(at),kind=hex(kind),segment=(kind>>8)&255,addend=hex(addend),target=None if target is None else hex(target)))
        print(json.dumps(result,indent=2));return
    if args.amx_cells:
        start,length=(int(v,0) for v in args.amx_cells)
        if start%4 or length%4 or start<0 or start+length>len(words)*4: raise ValueError('Cell range')
        for at in range(start,start+length,4):
            print(f'{at:04X} {words[at//4]:08X} {struct.unpack("<i",struct.pack("<I",words[at//4]))[0]}')
        return
    if args.xref_string:
        needle = args.xref_string.encode('ascii')
        result = []
        at = code.find(needle)
        while at >= 0:
            begin = code.rfind(b'\0', 0, at) + 1
            rom_begin=code.rfind(b'rom:/',max(0,at-64),at+1)
            if rom_begin>=0: begin=rom_begin
            end = code.find(b'\0', at)
            refs = hits(code, base + begin)
            result.append(dict(va=hex(base + begin), text=code[begin:end].decode('ascii', 'replace'),
                               xrefs=[hex(base + h) for h in refs]))
            at = code.find(needle, at + 1)
        print(json.dumps(result, indent=2))
        return
    if args.disassemble:
        import capstone
        if args.cro:
            if Path(args.cro).name != args.cro or not args.cro.endswith('.cro'):
                raise ValueError('Expected a top-level CRO file name')
            code=(args.project/'ExtractedRomFS'/args.cro).read_bytes()
            base=0
        va, length = (int(v, 0) for v in args.disassemble)
        mode = capstone.CS_MODE_THUMB if args.thumb else capstone.CS_MODE_ARM
        cs = capstone.Cs(capstone.CS_ARCH_ARM, mode)
        cs.skipdata = True
        at = va - base
        require(0 <= at <= len(code) - length and length>=0,'Disassembly range')
        for ins in cs.disasm(code[at:at + length], va):
            print(f'{ins.address:08X} {ins.mnemonic:10} {ins.op_str}')
        return
    imports_by_index=native_imports(amx)
    section = [u32(amx, 28 + i * 4) for i in range(8)]
    if args.resolve_names:
        # Hash recurrence documented by XAMX; independently matched against private resources.
        # No XAMX code or command catalogue is imported or distributed.
        keys = {value:index for index,value in imports_by_index.items()}
        results = []
        for file in (args.project / 'ExtractedRomFS').glob('*.cro'):
            data = file.read_bytes()
            segments = [struct.unpack_from('<III',data,u32(data,0xc8)+i*12) for i in range(u32(data,0xcc))]
            refs = {}
            for i in range(u32(data,0x12c)):
                slot,kind,addend=struct.unpack_from('<III',data,u32(data,0x128)+i*12)
                if (slot&15)<3 and ((kind>>8)&255)<3 and kind&255==2:
                    refs[segments[slot&15][0]+(slot>>4)]=segments[(kind>>8)&255][0]+addend
            for match in re.finditer(rb'[A-Za-z_][A-Za-z_0-9]{2,}\x00', data):
                whole=match.group()[:-1]
                for delta in range(len(whole)-2):
                    name=whole[delta:];value=name_hash(name);offset=match.start()+delta
                    if value in keys:
                        pointers=[slot for slot,target in refs.items() if target==offset and slot+4 in refs]
                        if not pointers: continue
                        results.append(dict(index=keys[value], name=name.decode(), hash=hex(value),
                                            resource=file.name, string_offset=hex(offset),
                                            relocated_slots=[dict(slot=hex(slot), function_offset=hex(refs[slot+4])) for slot in pointers]))
        print(json.dumps(sorted(results, key=lambda row: (row['index'],row['resource'])), indent=2))
        return
    if args.scan_resources:
        keys = {value:index for index,value in imports_by_index.items()}
        results = []
        for file in (args.project / 'ExtractedRomFS').rglob('*'):
            if not file.is_file() or file.stat().st_size > 100_000_000:
                continue
            data = file.read_bytes()
            matches = {str(index): [hex(at) for at in hits(data, key)]
                       for key, index in keys.items() if struct.pack('<I', key) in data}
            if matches:
                results.append(dict(path=str(file.relative_to(args.project)),
                                    size=len(data), matches=matches))
        print(json.dumps(results, indent=2))
        return
    imports = []
    # This is a candidate layout, not a claim about import binding semantics.
    for index, at in enumerate(range(section[2], section[3], 8)):
        first, second = struct.unpack_from('<II', amx, at)
        imports.append(dict(index=index, at=hex(at), first=hex(first), second=hex(second),
                            first_hits=[] if first == 0 else [hex(h) for h in hits(code, first)],
                            second_hits=[] if second == 0 else [hex(h) for h in hits(code, second)],
                            transformed_hits={label: [hex(h + base) for h in hits(code, value)]
                                              for label, value in {
                                                  'not': second ^ 0xFFFFFFFF,
                                                  'neg': (-second) & 0xFFFFFFFF,
                                                  'swap': int.from_bytes(second.to_bytes(4, 'little'), 'big'),
                                                  'rotate16': ((second << 16) | (second >> 16)) & 0xFFFFFFFF,
                                              }.items() if struct.pack('<I', value) in code}))
    result = dict(title_id=f'{struct.unpack_from("<Q", ex, 0x200)[0]:016X}',
                  code_sha256=hashlib.sha256(code).hexdigest(), base=hex(base),
                  amx_header_sections=[hex(v) for v in section],
                  amx_sha256=hashlib.sha256(amx).hexdigest(),
                  candidate_imports=imports,
                  magic_hits={hex(v): [hex(h + base) for h in hits(code, v)]
                              for v in (0x0A0AF1E0, 0x0A0AF1EF, 0xF1E0, 0xF1EF)})
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
