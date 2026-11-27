"""Bounded native Accessor ABI experiment in Unicorn; never patches a game.

Synthetic Pokemon/party memory; original XY accessor/crypto code. A small test
wrapper changes block-A+0x14. This is NOT a Bag handler, mint payload, save
editor or gameplay verification. Native stat functions use a synthetic personal
table (all base stats 100), initialized in Unicorn-owned memory. No calls are
stubbed; this does not establish the live game's manager ownership or UI ABI.
"""
import argparse
import json
import struct
from pathlib import Path

from inspect_mints import analyze, read_project, require, region, u32, verify_unchanged


def branch_link(at, target):
    require(type(at) is int and type(target) is int and 0 <= at <= 0xfffffffc and
            0 <= target <= 0xfffffffc and at % 4 == 0 and target % 4 == 0,
            'BL requires aligned uint32 ARM addresses')
    delta = target-at-8
    require(delta % 4 == 0 and -0x2000000 <= delta < 0x2000000, 'BL outside ARM range')
    return 0xeb000000 | ((delta >> 2) & 0xffffff)


def replay(code, base, old_nature, new_nature, ec):
    from unicorn import Uc, UC_ARCH_ARM, UC_MODE_ARM, UC_HOOK_CODE
    from unicorn.arm_const import (UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R4,
        UC_ARM_REG_SP, UC_ARM_REG_LR, UC_ARM_REG_PC, UC_ARM_REG_C1_C0_2, UC_ARM_REG_FPEXC)
    require(type(old_nature) is int and 0 <= old_nature < 25 and
            type(new_nature) is int and 0 <= new_nature < 25 and
            type(ec) is int and 0 <= ec <= 0xffffffff, 'Invalid synthetic Pokemon')
    # Derive the shuffled block-A position from this title's native table.
    table_va = u32(code, 0x16b3d0-base)
    positions = region(code, table_va-base, 128)
    require(all(sorted(positions[i:i+4]) == [0,1,2,3] for i in range(0,128,4)), 'Native shuffle table differs')
    body = bytearray(224)
    block_at = positions[((ec >> 13) & 31)*4]*56
    struct.pack_into('<HHII', body, block_at, 282, 158, 1234, 5678)
    struct.pack_into('<I', body, block_at+16, 0xcafef00d)
    body[block_at+20] = old_nature
    raw = struct.pack('<IHH', ec, 0, sum(struct.unpack('<112H', body)) & 65535) + body
    party = struct.pack('<IBBHHH5H6x', 0, 50, 0, 0, 120, 150, 100,101,102,103,104)
    require(len(party) == 28, 'Synthetic party size differs')

    uc = Uc(UC_ARCH_ARM, UC_MODE_ARM)
    uc.mem_map(base, (len(code)+4095) & ~4095)
    uc.mem_write(base, code)
    scratch, wrapper, stop = 0x800000, 0x900000, 0x901000
    uc.mem_map(scratch, 0x30000)
    uc.mem_map(wrapper, 0x2000)
    core, accessor, boxed, stats, stack = (scratch+n for n in (0,0x100,0x200,0x300,0xf000))
    uc.mem_write(core+12, struct.pack('<I', accessor))
    uc.mem_write(accessor, struct.pack('<III', 0, stats, boxed)+bytes(4))
    uc.mem_write(boxed, bytes(raw))
    uc.mem_write(stats, party)
    # Native personal-data reader: two pinned globals reference a manager and
    # a species*64 table. Only this synthetic process memory is initialized.
    manager, cache, table = scratch+0x4000, scratch+0x4100, scratch+0x10000
    uc.mem_write(u32(code,0x167e40-base)+0x14,struct.pack('<I',manager))
    uc.mem_write(u32(code,0x174ca4-base)+4,struct.pack('<I',table))
    uc.mem_write(manager+8,struct.pack('<I',cache))
    uc.mem_write(table+282*64, bytes([100]*6)+bytes(58))
    uc.reg_write(UC_ARM_REG_C1_C0_2, 0xf << 20)
    uc.reg_write(UC_ARM_REG_FPEXC, 0x40000000)
    callee_saved = {UC_ARM_REG_R4+i: 0x11110000+i for i in range(8)}

    def invoke(entry, arg0, arg1=0):
        uc.reg_write(UC_ARM_REG_R0, arg0)
        uc.reg_write(UC_ARM_REG_R1, arg1)
        uc.reg_write(UC_ARM_REG_SP, stack)
        uc.reg_write(UC_ARM_REG_LR, stop)
        for reg, value in callee_saved.items():
            uc.reg_write(reg, value)
        uc.emu_start(entry, stop, count=100000)
        require(uc.reg_read(UC_ARM_REG_PC) == stop, 'Native execution exceeded instruction budget')
        require(uc.reg_read(UC_ARM_REG_SP) == stack and all(uc.reg_read(r) == v for r,v in callee_saved.items()),
                'Native invocation corrupted callee-saved registers or SP')
        return uc.reg_read(UC_ARM_REG_R0)

    # Encrypt the synthetic structures using the original native end function.
    invoke(0x1685f0, accessor)
    encrypted_before = bytes(uc.mem_read(boxed, 232))
    encrypted_stats = bytes(uc.mem_read(stats, 28))
    require(uc.mem_read(accessor+12,1) == b'\x01', 'Accessor did not close encrypted')
    require(invoke(0x4a0d40, core) == old_nature, 'Original native getter disagrees')
    require(bytes(uc.mem_read(boxed,232)) == encrypted_before and bytes(uc.mem_read(stats,28)) == encrypted_stats,
            'Read-only native getter modified synthetic data')
    # Independent ABI wrapper, located only in Unicorn-owned RAM. No CRO payload.
    instructions = [0xe92d4070, 0xe1a05001, 0xe590400c, 0xe1a00004,
        branch_link(wrapper+16,0x168660), 0xe3a01000, 0xe1a00004,
        branch_link(wrapper+28,0x16b358), 0xe5c05014, 0xe1a00004,
        branch_link(wrapper+40,0x1685f0), 0xe8bd8070]
    uc.mem_write(wrapper, struct.pack('<12I', *instructions))
    invoke(wrapper, core, new_nature)
    require(invoke(0x4a0d40, core) == new_nature, 'Native getter did not observe changed nature')
    require(bytes(uc.mem_read(stats,28)) == encrypted_stats, 'Accessor write unexpectedly changed party stats')
    # Execute the five original native calculators/setters, not the cached
    # getters and not the HP/level/status part of the broader refresh routine.
    for entry in (0x14fb00,0x14fbd4,0x14fe78,0x14fa2c,0x14fda4):
        invoke(entry,core)
    # Inspect through the original native begin, without a host implementation of crypto.
    invoke(0x168660, accessor)
    after = bytes(uc.mem_read(boxed,232))
    expected = bytearray(raw)
    expected[8+block_at+20] = new_nature
    struct.pack_into('<H',expected,6,sum(struct.unpack('<112H',expected[8:])) & 65535)
    require(after == bytes(expected), 'Native encrypted roundtrip changed fields other than nature/checksum')
    expected_party = bytearray(party)
    for stat in range(5):
        value=105
        if new_nature//5 != new_nature%5:
            if stat == new_nature//5: value=value*110//100
            elif stat == new_nature%5: value=value*90//100
        struct.pack_into('<H',expected_party,12+stat*2,value)
    require(bytes(uc.mem_read(stats,28)) == bytes(expected_party),
            f'Native refresh disagrees with expected nature modifiers or changed other party fields: '
            f'{bytes(uc.mem_read(stats,28)).hex()} != {expected_party.hex()}')
    return {'old_nature': old_nature, 'new_nature': new_nature, 'shuffle_row': (ec >> 13) & 31,
            'native_checksum_roundtrip': True, 'unchanged_other_fields': True,
            'party_stats_recomputed': True, 'unchanged_hp_level_status': True,
            'native_calls_stubbed': False, 'gameplay_verified': False, 'installable': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    profiles = json.loads(Path(__file__).with_name('observed_profiles.json').read_text())
    blobs = read_project(args.project, profiles)
    report = analyze(blobs, profiles)
    cases = [replay(blobs['ExeFS/.code.bin'], 0x100000, 0, nature, row << 13)
             for row in range(32) for nature in range(25)]
    verify_unchanged(args.project, blobs)
    with args.output.open('x', encoding='utf-8') as output:
        json.dump({'title_id': report['title_id'], 'production_enabled': False,
                   'cases': cases, 'limits': __doc__}, output, indent=2)
        output.write('\n')
    print(f"{report['title_id']}: {len(cases)} native Accessor/stat experiments; synthetic inputs, no gameplay claim")


if __name__ == '__main__':
    main()
