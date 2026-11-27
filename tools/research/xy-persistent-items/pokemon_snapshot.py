"""Read-only block-A decoding for observed XY native Accessor snapshots.

No save writer or game patch. Block positions are read from the pinned native
executable, not extrapolated from USUM. Format/LCG cross-check references:
https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/PKM/PK6.cs
https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/PKM/Util/PokeCrypto.cs
Implementation here is independent; no PKHeX code or block table is bundled.
"""
import hashlib
import struct

from inspect_persistent_items import require, region


def block_a_positions(code, code_base):
    # Observed accessor block selector: LDR literal at VA 0x16B3A8, pool at
    # 0x16B3D0; 32 rows of four byte positions. Caller must pin the executable.
    address = struct.unpack('<I', region(code, 0x16b3d0-code_base, 4))[0]
    table = region(code, address-code_base, 128)
    require(all(sorted(table[i:i+4]) == [0, 1, 2, 3] for i in range(0, 128, 4)),
            'Invalid native block-position table')
    return tuple(table[::4]), address, table


def decode_block_a(raw, encrypted, positions, max_item_id):
    require(len(raw) == 0xe8, 'Incomplete native Pokemon data')
    require(isinstance(encrypted, int) and encrypted in (0, 1), 'Unknown Accessor encryption state')
    require(len(positions) == 32 and all(isinstance(p, int) and p in (0, 1, 2, 3) for p in positions),
            'Invalid native block-A positions')
    require(isinstance(max_item_id, int) and 0 <= max_item_id <= 65535, 'Invalid item catalog limit')
    ec, sanity, checksum = struct.unpack_from('<IHH', raw)
    require(not sanity & 4, 'Native Accessor marks this Pokemon as corrupt')
    body = bytearray(raw[8:])
    if encrypted:
        seed = ec
        for offset in range(0, len(body), 2):
            seed = (seed*0x41c64e6d+0x6073) & 0xffffffff
            value = struct.unpack_from('<H', body, offset)[0] ^ (seed >> 16)
            struct.pack_into('<H', body, offset, value)
    require(sum(struct.unpack('<112H', body)) & 65535 == checksum, 'Native Pokemon checksum mismatch')
    block = positions[(ec >> 13) & 31]*56
    species, item = struct.unpack_from('<HH', body, block)
    require(1 <= species <= 721 and item <= max_item_id, 'Decoded species/item outside observed Gen6 catalog')
    owner = struct.unpack_from('<I', body, block+4)[0]
    pid = struct.unpack_from('<I', body, block+16)[0]
    tag = hashlib.sha256(struct.pack('<III', ec, pid, owner)).hexdigest()[:16].upper()
    return dict(species=species, held_item=item, identity_tag=tag, checksum_valid=True,
                encrypted_at_capture=bool(encrypted))
