"""Original/patched Gen6 ARM reconciliation replay with explicit import contracts.

Executes the original loop and two changed conditions in Unicorn synthetic RAM.
Party/getter/setter/item-manager imports are contract stubs, not gameplay.
"""
import argparse
import json
import struct
from pathlib import Path
from collections import Counter

from experimental_two_gates import patch, X_TITLE, Y_TITLE
from inspect_persistent_items import inspect, require, sha, garc_members


def run_loop(data, mode, original_items, current_items, item_parameters):
    from unicorn import Uc, UC_ARCH_ARM, UC_MODE_ARM, UC_HOOK_CODE
    from unicorn.arm_const import (UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R2, UC_ARM_REG_R4,
                                   UC_ARM_REG_R7, UC_ARM_REG_R8, UC_ARM_REG_R9, UC_ARM_REG_SP,
                                   UC_ARM_REG_LR, UC_ARM_REG_PC)
    require(0 <= mode <= 255 and len(original_items) == len(current_items) <= 6, 'Invalid replay inputs')
    load, context, metadata, original_party, current_party, manager, stack = (
        0x100000, 0x300000, 0x301000, 0x310000, 0x311000, 0x312000, 0x408000)
    uc = Uc(UC_ARCH_ARM, UC_MODE_ARM)
    uc.mem_map(load, (len(data)+4095) & ~4095)
    uc.mem_write(load, bytes(data))
    uc.mem_map(context, 0x80000)
    uc.mem_map(0x400000, 0x10000)
    uc.mem_write(context+0x10, struct.pack('<I', metadata))
    uc.mem_write(metadata, bytes([mode]))
    uc.mem_write(metadata+0x4c, struct.pack('<I', original_party))
    uc.mem_write(manager+0x5c, struct.pack('<I', manager))
    originals = [0x320000+i*0x100 for i in range(len(original_items))]
    current = [0x330000+i*0x100 for i in range(len(current_items))]
    held = dict(zip(originals+current, list(original_items)+list(current_items)))
    calls, writes = [], []
    def hook(machine, pc, size, _):
        offset = pc-load
        r0, r1 = machine.reg_read(UC_ARM_REG_R0), machine.reg_read(UC_ARM_REG_R1)
        if offset == 0x264:
            require(r0 in (original_party, current_party) and r1 < len(originals), 'Unexpected native party access')
            result = (originals if r0 == original_party else current)[r1]
        elif offset == 0x6ac:
            require(r0 in held, 'Unexpected native item getter target')
            result = held[r0]
        elif offset == 0x3ac:
            require(machine.reg_read(UC_ARM_REG_R2) == 17 and r1 < len(item_parameters), 'Unexpected item parameter query')
            result = item_parameters[r1]
        elif offset == 0x6b4:
            require(r0 in current, 'Setter does not target a working-party member')
            writes.append(dict(slot=current.index(r0), item=r1))
            held[r0] = r1
            result = 0
        else:
            require(0x14848 <= offset < 0x14960, 'Execution escaped the bounded reconciliation loop')
            return
        calls.append(hex(offset))
        machine.reg_write(UC_ARM_REG_R0, result)
        machine.reg_write(UC_ARM_REG_PC, machine.reg_read(UC_ARM_REG_LR))
    uc.hook_add(UC_HOOK_CODE, hook)
    for register, value in ((UC_ARM_REG_R4, len(originals)), (UC_ARM_REG_R7, manager),
                             (UC_ARM_REG_R8, current_party), (UC_ARM_REG_R9, context), (UC_ARM_REG_SP, stack)):
        uc.reg_write(register, value)
    uc.emu_start(load+0x14848, load+0x148dc, count=4096)
    require(uc.reg_read(UC_ARM_REG_PC) == load+0x148dc, 'Native loop did not return to its original continuation')
    require([held[p] for p in originals] == list(original_items), 'Replay mutated original-party input')
    return dict(items=[held[p] for p in current], setters=writes, imports=calls, gameplay_verified=False)


def entry_route(data, mode, flag):
    from unicorn import Uc, UC_ARCH_ARM, UC_MODE_ARM, UC_HOOK_CODE
    from unicorn.arm_const import UC_ARM_REG_R0, UC_ARM_REG_SP, UC_ARM_REG_PC
    uc = Uc(UC_ARCH_ARM, UC_MODE_ARM)
    base, context, metadata = 0x100000, 0x300000, 0x301000
    uc.mem_map(base, (len(data)+4095) & ~4095)
    uc.mem_write(base, bytes(data))
    uc.mem_map(context, 0x20000)
    uc.mem_write(context+0x10, struct.pack('<I', metadata))
    uc.mem_write(metadata, bytes([mode]))
    uc.mem_write(metadata+0x4b, bytes([flag]))
    uc.reg_write(UC_ARM_REG_R0, context)
    uc.reg_write(UC_ARM_REG_SP, context+0x18000)
    stops = {0x146c8, 0x14ab8, 0x14c80}
    def stop(machine, pc, size, _):
        if pc-base in stops:
            machine.emu_stop()
    uc.hook_add(UC_HOOK_CODE, stop)
    uc.emu_start(base+0x1469c, base+len(data), count=32)
    pc = uc.reg_read(UC_ARM_REG_PC)-base
    require(pc in stops, 'Native entry did not reach an expected gate destination')
    return hex(pc)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project', type=Path)
    parser.add_argument('--title-id', choices=(X_TITLE, Y_TITLE), default=X_TITLE)
    args = parser.parse_args()
    profiles = json.loads(Path(__file__).with_name('observed_profiles.json').read_text(encoding='utf-8'))
    identity = inspect(args.project, profiles)
    require(identity['observation_matches'] and identity['title_id'] == args.title_id,
            'Replay requires the full observed profile of the explicitly selected title')
    original = (args.project/'ExtractedRomFS/DllBattle.cro').read_bytes()
    require(sha(original) == identity['sha256']['ExtractedRomFS/DllBattle.cro'], 'Input changed after inspection')
    candidate = patch(original, sha(original))
    item_data = (args.project/'ExtractedRomFS/a/2/2/0').read_bytes()
    require(sha(item_data) == identity['sha256']['ExtractedRomFS/a/2/2/0'], 'Item resource changed after inspection')
    parameters = [record[14] for record in garc_members(item_data)]
    for mode in range(256):
        before = run_loop(original, mode, [158], [0], parameters)
        after = run_loop(candidate, mode, [158], [0], parameters)
        require(after['items'] == ([158] if mode <= 1 else [0]), 'Patched unsigned battle-mode gate differs')
        require(before['items'] == [0], 'Unexpected original consumable control')
        if mode > 1:
            require(before == after, 'Non-admitted numeric mode changed in the replay')
    routes = []
    for mode in (0, 1, 2, 3, 255):
        for flag in (0, 1, 2, 255):
            a, b = entry_route(original, mode, flag), entry_route(candidate, mode, flag)
            require(a == b, 'The unmodified native outer entry changed')
            routes.append(dict(mode=mode, flag_4b=flag, destination=a))
    scenarios = []
    for name, initial, current in (
        ('X-07-observed-item-values', [158, 664, 234, 268, 270], [0, 664, 234, 268, 270]),
        ('X-08-observed-item-values', [158, 664, 234, 268, 270], [0, 664, 234, 268, 270]),
        ('transfer-between-party-members', [158, 0], [0, 158]),
        ('swap-two-original-items', [158, 275], [275, 158]),
        ('nonconsumed-control', [657], [657]),
        ('two-consumers', [158, 275], [0, 0]),
        ('empty-party-loop-control', [], [])):
        for mode in (0, 1):
            row = run_loop(candidate, mode, initial, current, parameters)
            require(row['items'] == initial and Counter(row['items']) == Counter(initial), 'Original equipped distribution not preserved')
            scenarios.append(dict(case=name, mode=mode, initial=initial, current=current, result=row['items']))
    print(json.dumps(dict(title_id=identity['title_id'], production_enabled=False, gameplay_verified=False,
                          candidate_sha256=sha(candidate), mode_gate_comparisons=256, outer_routes=routes,
                          scenarios=scenarios, limitations=['Native imports use explicit contract stubs.',
                            'No full battle, save persistence, Bag or facility/PvP semantic validation.']), indent=2))


if __name__ == '__main__':
    main()
