"""Reproducible XY ARM template. Requires keystone-engine==0.9.2, no game data.

Assemble each instruction at a fixed template address, resolve local labels,
and reject overlapping .org ranges. B/BL to CRO thunks are rebased at install.
"""
import hashlib
import re
import struct
import sys
from pathlib import Path
from keystone import Ks, KS_ARCH_ARM, KS_MODE_ARM

ROOT = Path(__file__).resolve().parent
BASE, SIZE = 0x20000, 0xb00


def generate():
    labels, source, cursor = {}, [], 0
    for raw in (ROOT/'mints.s').read_text(encoding='utf-8').splitlines():
        line = raw.split('//')[0].strip()
        if not line:
            continue
        if line.startswith('.org '):
            target = int(line.split()[1], 0)
            assert cursor <= target <= SIZE, f'Overlapping or excessive range at {line}: {cursor:x}'
            cursor = target
            continue
        if ':' in line:
            label, line = line.split(':',1)
            assert label not in labels
            labels[label] = cursor
            line = line.strip()
            if not line:
                continue
        source.append((cursor,line))
        cursor += 4
    result = bytearray(SIZE)
    assembler = Ks(KS_ARCH_ARM, KS_MODE_ARM)
    for offset, text in source:
        if text.startswith('.word '):
            encoded = struct.pack('<I',int(text.split()[1],0))
        else:
            load = re.fullmatch(r'ldr (r\d+), (\w+)',text)
            adr = re.fullmatch(r'adr (r\d+), (\w+)',text)
            if load:
                text = f'ldr {load[1]}, [pc, #{labels[load[2]]-offset-8}]'
            elif adr:
                text = f'add {adr[1]}, pc, #{labels[adr[2]]-offset-8}'
            else:
                tail = text.split()[-1]
                if tail in labels:
                    text = text[:text.rfind(tail)]+hex(BASE+labels[tail])
            encoded = bytes(assembler.asm(text, addr=BASE+offset)[0])
        assert len(encoded)==4 and offset+4<=SIZE, text
        result[offset:offset+4] = encoded
    return bytes(result), labels


if __name__ == '__main__':
    data, labels = generate()
    output = ROOT.parents[2]/'pk3DS.Core/Modding/Research/Gen6NatureMintPayload.cs'
    generated = ('// SPDX-License-Identifier: GPL-3.0-only\n'
        '// Generated from native/3ds/xy-nature-mints/mints.s; do not hand edit.\n'
        'namespace pk3DS.Core.Modding.Research;\n'
        'internal static class Gen6NatureMintPayload\n{\n'
        '    internal static byte[] Create() => System.Convert.FromHexString(\n'
        '        "'+data.hex().upper()+'");\n}\n')
    if '--check' in sys.argv:
        assert output.read_text(encoding='utf-8')==generated, 'Committed template differs from source'
    else:
        output.write_text(generated,encoding='utf-8')
    print('XY template', len(data), hashlib.sha256(data).hexdigest(), labels)
