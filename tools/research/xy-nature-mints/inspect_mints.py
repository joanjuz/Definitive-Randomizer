"""Read-only, pinned XY-MINT native research. No installer or payload generator.

Offsets in DllBag.cro are FILE offsets; executable addresses are native VAs.
The existing XY CRO reader is reused, never the USUM patch or its profiles.
Only hashes, addresses and bounded call summaries are emitted, not game dumps.
"""
import argparse
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'xy-persistent-items'))
from inspect_persistent_items import Cro, garc_members, region, require, sha, u32


SLOTS = {0x2e4, 0x36c, 0x42c, 0x44c, 0x454, 0x504}
NATIVE_REGIONS = {
    'nature_accessor_get': (0x152c3c, 0x152c68),
    'nature_core_get': (0x4a0d40, 0x4a0d54),
    'accessor_begin': (0x168660, 0x1686e8),
    'accessor_end': (0x1685f0, 0x168660),
    'party_refresh_candidate': (0x12b808, 0x12b8e0),
    'refresh_attack_candidate': (0x14fb00, 0x14fbd4),
    'refresh_defense_candidate': (0x14fbd4, 0x14fca8),
    'refresh_spattack_candidate': (0x14fa2c, 0x14fb00),
    'refresh_spdefense_candidate': (0x14fda4, 0x14fe78),
    'refresh_speed_candidate': (0x14fe78, 0x14ff4c),
    'remove_bag_quantity': (0x434944, 0x434a30),
    'pocket_cache_builder': (0x107788, 0x107878),
    'pocket_cached_getter': (0x39faf4, 0x39fb44),
    'message_handle_accessor': (0x3480a4, 0x3480f4),
}
BAG_REGIONS = {
    'medicine_action': (0x2b00, 0x30f8),
    'medicine_message': (0x9074, 0x9450),
    'consume_and_finalize': (0x97e0, 0x9848),
    'pre_action_callback': (0x976c, 0x97e0),
}


def bl_target(word, address):
    if word & 0x0f000000 != 0x0b000000 or word >> 28 == 15:
        return None
    delta = (word & 0xffffff) << 2
    if delta & 0x2000000:
        delta -= 0x4000000
    return address + 8 + delta


def calls(data, start, end, base=0):
    block = region(data, start-base, end-start)
    result = []
    for offset in range(0, len(block)-3, 4):
        target = bl_target(u32(block, offset), start+offset)
        if target is not None:
            result.append({'at': hex(start+offset), 'target': hex(target)})
    return result


def no_links(path):
    for part in (path, *path.parents):
        st = part.stat()
        require(not part.is_symlink() and not getattr(st, 'st_file_attributes', 0) & 0x400,
                'Research inputs must not traverse links or reparse points')


def identify(blobs, profiles):
    ncch, ex = blobs['ncchheader.bin'], blobs['exheader.bin']
    require(region(ncch, 0x100, 4) == b'NCCH', 'Missing NCCH')
    title = region(ncch, 0x118, 8)[::-1].hex().upper()
    require(title in profiles['titles'], 'Unsupported title')
    require(region(ex, 0x200, 8) == region(ncch, 0x118, 8), 'Crossed exheader')
    expected = profiles['titles'][title]['sha256']
    require(set(blobs) == set(expected), 'Missing or excess dependencies')
    require({key: sha(value) for key, value in blobs.items()} == expected,
            'Unknown or crossed research resources: fixed-offset analysis refused')
    return title


def analyze(blobs, profiles):
    title = identify(blobs, profiles)
    bag = Cro(blobs['RomFS/DllBag.cro'])
    require(bag.hashes_valid(), 'Invalid CRO integrity')
    static = Cro(blobs['RomFS/static.crs'], static=True)
    links = bag.static_links(static, SLOTS)
    code = blobs['ExeFS/.code.bin']
    base = u32(blobs['exheader.bin'], 0x10)
    require(base == 0x100000, 'Unexpected executable mapping')
    # Each assertion is performed independently after title-specific dependency admission.
    require(u32(code, 0x4a0d44-base) == 0xe590000c, 'Core/Accessor layout differs')
    require(u32(code, 0x152c54-base) == 0xe5d04014, 'Nature getter differs')
    require(bl_target(u32(code, 0x152c50-base), 0x152c50) == 0x16b358,
            'Nature getter block selector differs')
    require(u32(bag.data, 0x97fc) == 0xe3a02001 and
            bl_target(u32(bag.data, 0x9800), 0x9800) == 0x500,
            'Bag quantity path differs')
    members = garc_members(blobs['RomFS/a/2/2/0'])
    require(len(members) == 718 and all(len(m) == 36 for m in members), 'Item catalog differs')
    crr = blobs['RomFS/.crr/static.crr']
    entries = region(crr, u32(crr, 0x350), u32(crr, 0x354)*32)
    covered = sha(bag.data[:0x80])
    return {
        'title_id': title, 'purpose': 'Read-only research, not installation admission',
        'production_enabled': False, 'installable': False,
        'code_base': hex(base), 'item_count': len(members),
        'sha256': {key: sha(value) for key, value in blobs.items()},
        'bag': {'physical_name': 'DllBag.cro', 'size': len(bag.data),
                'segments': bag.segments, 'hashes_valid': True, 'imports': links,
                'crr_original_header_hash_matches': sum(entries[i:i+32].hex().upper() == covered
                    for i in range(0, len(entries), 32)),
                'regions': {name: {'start_file': hex(a), 'end_file': hex(z),
                    'sha256': sha(region(bag.data, a, z-a)), 'direct_bl': calls(bag.data, a, z)}
                    for name, (a, z) in BAG_REGIONS.items()}},
        'native_regions': {name: {'start_va': hex(a), 'end_va': hex(z),
            'sha256': sha(region(code, a-base, z-a)), 'direct_bl': calls(code, a, z, base)}
            for name, (a, z) in NATIVE_REGIONS.items()},
        'limits': ['BL summaries are syntactic; literal pools can resemble instructions.',
                   'No item allocation, write hook, ABI or installation is accredited.',
                   'Original CRR membership does not authorize a modified CRO or forge signatures.'],
    }


def read_project(root, profiles):
    no_links(root)
    names = [p.name for p in (root/'RomFS').iterdir() if p.name.lower() == 'dllbag.cro']
    require(names == ['DllBag.cro'], 'Expected exactly the physical name DllBag.cro')
    paths = set().union(*(p['sha256'] for p in profiles['titles'].values()))
    blobs = {}
    for name in sorted(paths):
        path = root/name
        no_links(path)
        blobs[name] = path.read_bytes()
    return blobs


def verify_unchanged(root, blobs):
    for name, original in blobs.items():
        no_links(root/name)
        require(sha((root/name).read_bytes()) == sha(original), 'Research input changed concurrently')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project', type=Path)
    parser.add_argument('--output', type=Path, required=True, help='New report path; never overwritten')
    args = parser.parse_args()
    profiles = json.loads(Path(__file__).with_name('observed_profiles.json').read_text())
    blobs = read_project(args.project, profiles)
    report = analyze(blobs, profiles)
    # Refuse a report from a changing resource set, without modifying either set.
    verify_unchanged(args.project, blobs)
    with args.output.open('x', encoding='utf-8') as out:
        json.dump(report, out, indent=2)
        out.write('\n')
    print(f"READ ONLY {report['title_id']}: CRO hashes valid; production blocked")


if __name__ == '__main__':
    main()
