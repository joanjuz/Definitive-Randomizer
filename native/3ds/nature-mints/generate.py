"""Reproduce the source template. Requires keystone-engine==0.9.2; no ROM inputs."""
from pathlib import Path
from keystone import Ks, KS_ARCH_ARM, KS_MODE_ARM
import hashlib
import re
import struct
import sys
root = Path(__file__).resolve().parent
lines = [line.split('//')[0].strip() for line in (root/'mints.s').read_text().splitlines()]
# Lay out .org and resolve labels ourselves. Keystone's fragment handling of
# numeric B/BL operands across .org gaps is inconsistent: assemble each real
# instruction at its exact address instead, never treating .org as executable.
labels, instructions, offset = {}, [], 0
for line in lines:
    if not line: continue
    if line.startswith('.org '): offset = int(line.split()[1], 0); continue
    if ':' in line:
        label,line = line.split(':',1); labels[label] = offset; line=line.strip()
        if not line: continue
    instructions.append((offset,line)); offset+=4
data=bytearray(0x400)
assembler=Ks(KS_ARCH_ARM, KS_MODE_ARM)
for offset,line in instructions:
    if line.startswith('.word '): encoded=struct.pack('<I',int(line.split()[1],0))
    else:
        load=re.fullmatch(r'ldr (r\d+), (\w+)',line)
        adr=re.fullmatch(r'adr (r\d+), (\w+)',line)
        if load: line=f'ldr {load[1]}, [pc, #{labels[load[2]]-offset-8}]'
        elif adr: line=f'add {adr[1]}, pc, #{labels[adr[2]]-offset-8}'
        else:
            parts=line.split()
            if parts[-1] in labels: line=line[:line.rfind(parts[-1])]+hex(0x18010+labels[parts[-1]])
        encoded=bytes(assembler.asm(line,addr=0x18010+offset)[0])
    assert len(encoded)==4 and offset+4<=len(data),line
    data[offset:offset+4]=encoded
data=bytes(data)
assert len(data) == 0x400
target = root.parents[2]/'pk3DS.Core/Modding/Research/UsumNatureMintPayload.cs'
generated='// SPDX-License-Identifier: GPL-3.0-only\n// Generated from native/3ds/nature-mints/mints.s; do not hand edit.\nnamespace pk3DS.Core.Modding.Research;\ninternal static class UsumNatureMintPayload\n{\n    internal static byte[] Create() => System.Convert.FromHexString(\n        "'+data.hex().upper()+'");\n}\n'
if '--check' in sys.argv: assert target.read_text(encoding='utf-8')==generated, 'Committed template differs from assembly source'
else: target.write_text(generated, encoding='utf-8')
print('template', len(data), hashlib.sha256(data).hexdigest())
