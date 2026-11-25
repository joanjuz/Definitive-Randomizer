"""Assemble candy.s into the C# template. Requires keystone-engine==0.9.2; no ROM inputs.

  python generate.py            regenerate RareCandyMultiUsePayload.cs
  python generate.py --check    fail if the committed template differs from candy.s
  python generate.py --reference <code.ips>
                                development only: compare with the private reference patch
"""
from pathlib import Path
from keystone import Ks, KS_ARCH_ARM, KS_MODE_ARM
import hashlib
import re
import struct
import sys

root = Path(__file__).resolve().parent
lines = [line.split('//')[0].strip() for line in (root / 'candy.s').read_text(encoding='utf-8').splitlines()]

# Resolve labels ourselves and assemble each instruction at its exact address
# (same approach as nature-mints/generate.py).
labels, instructions, address = {}, [], None
for line in lines:
    if not line:
        continue
    if line.startswith('.org '):
        address = int(line.split()[1], 0)
        continue
    if ':' in line:
        label, line = line.split(':', 1)
        labels[label.strip()] = address
        line = line.strip()
        if not line:
            continue
    assert address is not None, line
    instructions.append((address, line))
    address += 4

assembler = Ks(KS_ARCH_ARM, KS_MODE_ARM)
words = {}
for address, line in instructions:
    if line.startswith('.word '):
        encoded = struct.pack('<I', int(line.split()[1], 0))
    else:
        load = re.fullmatch(r'ldr (r\d+), (\w+)', line)
        if load and load[2] in labels:
            line = f'ldr {load[1]}, [pc, #{labels[load[2]] - address - 8}]'
        else:
            parts = line.split()
            if parts[-1] in labels:
                line = line[:line.rfind(parts[-1])] + hex(labels[parts[-1]])
        encoded = bytes(assembler.asm(line, addr=address)[0])
    assert len(encoded) == 4, line
    assert address not in words, hex(address)
    words[address] = encoded

# Contiguous blocks.
blocks = []
for address in sorted(words):
    if blocks and blocks[-1][0] + len(blocks[-1][1]) == address:
        blocks[-1][1].extend(words[address])
    else:
        blocks.append([address, bytearray(words[address])])

if '--reference' in sys.argv:
    ips = Path(sys.argv[sys.argv.index('--reference') + 1]).read_bytes()
    assert ips[:5] == b'PATCH'
    position, reference = 5, {}
    while ips[position:position + 3] != b'EOF':
        offset = int.from_bytes(ips[position:position + 3], 'big')
        size = int.from_bytes(ips[position + 3:position + 5], 'big')
        position += 5
        assert size != 0, 'RLE records are not expected'
        reference[offset] = ips[position:position + size]
        position += size
    cave = reference[0x479610]
    for address, data in blocks:
        start = address - 0x100000 - 0x479610
        assert cave[start:start + len(data)] == bytes(data), f'block 0x{address:X} differs from the reference'
    assert reference[0x2B5160] == bytes.fromhex('261207EA'), 'reference detour differs'
    print('reference: reachable blocks identical')

body = ',\n'.join(f'        new(0x{address:06X}, System.Convert.FromHexString("{bytes(data).hex().upper()}"))' for address, data in blocks)
generated = ('// SPDX-License-Identifier: GPL-3.0-only\n'
             '// Generated from native/3ds/rare-candy-multiuse/candy.s; do not hand edit.\n'
             'namespace pk3DS.Core.Modding.Research;\n'
             'internal static class RareCandyMultiUsePayload\n'
             '{\n'
             '    internal sealed record Block(uint Address, byte[] Bytes);\n'
             '    internal static Block[] Create() =>\n'
             '    [\n' + body + '\n'
             '    ];\n'
             f'    internal const uint LearnDetour = 0x{labels["learn_detour"]:06X};\n'
             f'    internal const uint UseEntry = 0x{labels["use_entry"]:06X};\n'
             f'    internal const uint ApplyLoop = 0x{labels["apply_loop"]:06X};\n'
             f'    internal const uint ApplyNext = 0x{labels["apply_next"]:06X};\n'
             f'    internal const uint Dispatch = 0x{labels["dispatch"]:06X};\n'
             '}\n')
target = root.parents[2] / 'pk3DS.Core/Modding/Research/RareCandyMultiUsePayload.cs'
if '--check' in sys.argv:
    assert target.read_text(encoding='utf-8') == generated, 'Committed template differs from assembly source'
else:
    target.write_text(generated, encoding='utf-8', newline='\n')
for address, data in blocks:
    print(f'block 0x{address:X} {len(data)} bytes sha256 {hashlib.sha256(data).hexdigest()}')
