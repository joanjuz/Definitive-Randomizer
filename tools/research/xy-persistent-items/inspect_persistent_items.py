"""Read-only X/Y held-item reconciliation research. No patch or admission switch.

Usage: python inspect_persistent_items.py PROJECT [--listing]
The optional ARM listing is private evidence; do not commit it or game resources.
Only --listing requires capstone. All offsets printed for CROs are FILE offsets.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path


def require(ok, message):
    if not ok:
        raise ValueError(message)


def region(data, start, size):
    require(start >= 0 and size >= 0 and start <= len(data) - size, 'Resource range outside file')
    return data[start:start + size]


def u32(data, at):
    return struct.unpack('<I', region(data, at, 4))[0]


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


class Cro:
    def __init__(self, data, static=False):
        self.data = data
        require(region(data, 0x80, 4) == b'CRO0', 'CRO0 magic missing')
        require(len(data) >= 0x180, 'CRO header truncated')
        at, count = u32(data, 0xc8), u32(data, 0xcc)
        require(4 <= count <= 16, 'Unsupported segment count')
        self.segments = [struct.unpack('<III', region(data, at + i * 12, 12)) for i in range(count)]
        if not static:
            require(u32(data, 0x90) == len(data), 'Unexpected CRO file size')
            for start, size, _ in self.segments[:3]:
                region(data, start, size)

    def address(self, tag):
        index, offset = tag & 15, tag >> 4
        require(index < 3, 'Expected file-backed segment')
        start, size, _ = self.segments[index]
        require(offset < size, 'Segment offset outside segment')
        return start + offset

    def hashes_valid(self):
        b = self.data
        code, size, data, dsize, ro = [u32(b, i) for i in (0xb0, 0xb4, 0xb8, 0xbc, 0xc0)]
        return all(hashlib.sha256(region(b, start, length)).digest() == region(b, slot * 32, 32)
                   for slot, (start, length) in enumerate(((0x80, 0x100), (code, size), (ro, data-ro), (data, dsize))))

    def static_links(self, static, wanted):
        """Resolve indexed/anonymous static imports, including bounded relocation batches."""
        b, result = self.data, []
        at, count = u32(b, 0xf0), u32(b, 0xf4)
        region(b, at, count * 20)
        # 0x110 is the anonymous-import table, NOT the relocation table.
        patch_at, patch_count = u32(b, 0xf8), u32(b, 0xfc)
        region(b, patch_at, patch_count * 12)
        for i in range(count):
            name, idx, nidx, anon, nanon = struct.unpack('<IIIII', region(b, at+i*20, 20))
            end = b.find(b'\0', name)
            require(name >= 0x180 and name < end < len(b), 'Invalid import name')
            module = b[name:end].decode('ascii')
            for anonymous, table, length in ((False, idx, nidx), (True, anon, nanon)):
                region(b, table, length * 8)
                for j in range(length):
                    ordinal, cursor = struct.unpack('<II', region(b, table+j*8, 8))
                    while True:
                        require(patch_at <= cursor <= patch_at+patch_count*12-12 and (cursor-patch_at)%12 == 0,
                                'Import batch outside external relocation table')
                        slot, kind, addend = struct.unpack('<III', region(b, cursor, 12))
                        cursor += 12
                        address = self.address(slot)
                        if address in wanted:
                            require(module == '|static|', 'Expected static import')
                            if anonymous:
                                tag = ordinal
                            else:
                                require(ordinal < u32(static.data, 0xdc), 'Export index outside table')
                                tag = u32(static.data, u32(static.data, 0xd8)+ordinal*4)
                            result.append(dict(slot=hex(address), kind=hex(kind), addend=addend,
                                               target_va=hex(static.address(tag))))
                        if kind & 0xff00:
                            break
        require(sorted(int(r['slot'], 16) for r in result) == sorted(wanted), 'Missing or duplicate import link')
        return sorted(result, key=lambda r: int(r['slot'], 16))


def garc_members(data):
    require(region(data, 0, 4) == b'CRAG', 'GARC magic missing')
    head = u32(data, 4)
    require(region(data, head, 4) == b'OTAF', 'FATO magic missing')
    count = struct.unpack('<H', region(data, head+8, 2))[0]
    fatb = head + u32(data, head+4)
    require(region(data, fatb, 4) == b'BTAF', 'FATB magic missing')
    result = []
    for i in range(count):
        entry = fatb + 12 + u32(data, head+12+i*4)
        require(u32(data, entry) == 1, 'Expected one item record per GARC entry')
        start, end, length = struct.unpack('<III', region(data, entry+4, 12))
        require(start <= end and length <= end-start, 'Invalid GARC member length')
        region(data, u32(data, 16)+start, end-start)
        result.append(region(data, u32(data, 16)+start, length))
    return result


def matches(data, pattern):
    return [i for i in range(0, len(data)-len(pattern)+1, 4) if data[i:i+len(pattern)] == pattern]


# Observations of this revision, NOT an installer profile or reusable Gen7 signatures.
GATE = bytes.fromhex('100099e50000d0e5010050e32000001a000054e30050a0e31d00009a')
RESTORE = bytes.fromhex('0600a0e186afffeb0060a0e10010a0e15c0097e51120a0e3c1aeffeb000050e300f020e32500000a')
SLOTS = {0x268, 0x3b0, 0x620, 0x6b0, 0x6b8}


def inspect(root, profiles):
    resources = profiles['resources']
    # Preserve and report the physical directory entry; never normalize DLLBattle.
    names = [p.name for p in (root/'ExtractedRomFS').iterdir() if p.name.lower() == 'dllbattle.cro']
    require(names == ['DllBattle.cro'], 'Expected exactly the physical name DllBattle.cro')
    blobs = {name: (root/name).read_bytes() for name in resources}
    ncch, ex = blobs['HeaderNCCH0.bin'], blobs['DecryptedExHeader.bin']
    require(region(ncch, 0x100, 4) == b'NCCH', 'NCCH magic missing')
    title = region(ncch, 0x118, 8)[::-1].hex().upper()
    require(region(ex, 0x200, 8) == region(ncch, 0x118, 8), 'NCCH/exheader title mismatch')
    require(title in profiles['titles'], 'Not an observed X/Y title')
    observed = profiles['titles'][title]
    hashes = {name: sha(b) for name, b in blobs.items()}
    if hashes != observed['sha256']:
        return dict(title_id=title, observation_matches=False, sha256=hashes,
                    production_enabled=False, installable=False,
                    blocker='Unknown or crossed dependencies: no fixed-offset analysis was attempted.')
    battle = Cro(blobs['ExtractedRomFS/DllBattle.cro'])
    static = Cro(blobs['ExtractedRomFS/static.crs'], static=True)
    code = blobs['ExtractedExeFS/.code.bin']
    # Derive field access for parameter 17 from its actual switch table.
    base = u32(ex, 0x10)
    param_target = u32(code, 0x4a22c4 + 17*4 - base)
    param_bytes = region(code, param_target-base, 8).hex().upper()
    items = garc_members(blobs['ExtractedRomFS/a/2/2/0'])
    require(len(items) > 639, 'Missing candidate item records')
    candidates = {'White Herb':214, 'Sitrus Berry':158, 'Power Herb':271, 'Focus Sash':275,
                  'Red Card':542, 'Eject Button':547, 'Weakness Policy':639}
    item_values = {name: dict(id=i, parameter17_byte=region(items[i], 14, 1)[0]) for name, i in candidates.items()}
    consumable_ids = [i for i, item in enumerate(items) if region(item, 14, 1)[0] != 0]
    crr = blobs['ExtractedRomFS/.crr/static.crr']
    at, count = u32(crr, 0x350), u32(crr, 0x354)
    table = region(crr, at, count*32)
    header_hash = hashlib.sha256(battle.data[:0x80]).digest()
    return dict(title_id=title, ncch_container_version=struct.unpack('<H', region(ncch, 0x112, 2))[0],
                observation_matches=hashes == observed['sha256'], sha256=hashes,
                physical_name=names[0], cro_size=len(battle.data), cro_hashes_valid=battle.hashes_valid(),
                battle_header_in_crr=any(table[i:i+32] == header_hash for i in range(0, len(table), 32)),
                code_segments=battle.segments[:3],
                candidate_gate_offsets=[hex(i) for i in matches(battle.data, GATE)],
                candidate_restore_offsets=[hex(i) for i in matches(battle.data, RESTORE)],
                static_links=battle.static_links(static, SLOTS),
                parameter17=dict(target_va=hex(param_target), first_two_instructions=param_bytes),
                item_candidates=item_values, item_parameter17_nonzero_ids=consumable_ids,
                production_enabled=False, installable=False,
                blocker='Battle-mode values and held-item provenance through transfers require runtime tracing; no Gen6 installer is accredited.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project', type=Path)
    parser.add_argument('--listing', action='store_true')
    args = parser.parse_args()
    profiles = json.loads(Path(__file__).with_name('observed_profiles.json').read_text(encoding='utf-8'))
    report = inspect(args.project, profiles)
    print(json.dumps(report, indent=2))
    if args.listing:
        require(report['observation_matches'], 'Listing offsets require the observed resource set')
        from capstone import Cs, CS_ARCH_ARM, CS_MODE_ARM
        battle = (args.project/'ExtractedRomFS/DllBattle.cro').read_bytes()
        for start, size in ((0x1469c, 0x2e4), (0x1944c, 0x20)):
            for ins in Cs(CS_ARCH_ARM, CS_MODE_ARM).disasm(region(battle, start, size), start):
                print(f'{ins.address:08X} {ins.mnemonic:10} {ins.op_str}')


if __name__ == '__main__':
    main()
