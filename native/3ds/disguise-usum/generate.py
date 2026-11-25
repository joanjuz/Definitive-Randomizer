"""Assemble disguise.s into the C# template. Requires keystone-engine==0.9.2; no ROM inputs.

  python generate.py                       regenerate DisguiseUsumPayload.cs
  python generate.py --check               fail if the committed template differs from disguise.s
  python generate.py --prototype <hex>     development only: compare with the template validated in game
"""
from pathlib import Path
from keystone import Ks, KS_ARCH_ARM, KS_MODE_ARM
import hashlib
import sys

BASE, SIZE = 0x18010, 0xF0
root = Path(__file__).resolve().parent
lines = [l.split('//')[0].strip() for l in (root / 'disguise.s').read_text(encoding='utf-8').splitlines()]
labels, instructions, offset = {}, [], 0
for line in lines:
    if not line:
        continue
    if line.startswith('.org '):
        offset = int(line.split()[1], 0)
        continue
    if ':' in line:
        name, line = line.split(':', 1)
        labels[name.strip()] = offset
        line = line.strip()
        if not line:
            continue
    instructions.append((offset, line))
    offset += 4
assembler = Ks(KS_ARCH_ARM, KS_MODE_ARM)
data = bytearray(SIZE)
for offset, line in instructions:
    parts = line.split()
    if parts[-1] in labels:
        line = line[:line.rfind(parts[-1])] + hex(BASE + labels[parts[-1]])
    encoded = bytes(assembler.asm(line, addr=BASE + offset)[0])
    assert len(encoded) == 4 and offset + 4 <= SIZE and data[offset:offset + 4] == bytes(4), (hex(offset), line)
    data[offset:offset + 4] = encoded
data = bytes(data)
if '--prototype' in sys.argv:
    prototype = bytes.fromhex(Path(sys.argv[sys.argv.index('--prototype') + 1]).read_text().strip())
    assert prototype == data, 'template differs from the prototype validated in game'
    print('prototype: identical')
generated = ('// SPDX-License-Identifier: GPL-3.0-only\n'
             '// Generated from native/3ds/disguise-usum/disguise.s; do not hand edit.\n'
             'namespace pk3DS.Core.Modding.Research;\n'
             'internal static class DisguiseUsumPayload\n'
             '{\n'
             f'    internal const int TemplateBase = 0x{BASE:X};\n'
             f'    internal const int Bust = 0x{labels["bust"]:X}, PreA = 0x{labels["pre_a"]:X}, PreB = 0x{labels["pre_b"]:X}, PreC = 0x{labels["pre_c"]:X};\n'
             '    internal static byte[] Create() => System.Convert.FromHexString(\n'
             f'        "{data.hex().upper()}");\n'
             '}\n')
target = root.parents[2] / 'pk3DS.Core/Modding/Research/DisguiseUsumPayload.cs'
if '--check' in sys.argv:
    assert target.read_text(encoding='utf-8') == generated, 'Committed template differs from assembly source'
else:
    target.write_text(generated, encoding='utf-8', newline='\n')
print('template', len(data), hashlib.sha256(data).hexdigest())
