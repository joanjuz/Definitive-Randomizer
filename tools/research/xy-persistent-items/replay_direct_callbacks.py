"""Execute two original XY callbacks in isolated synthetic ARM memory.

Optional dependency: unicorn==2.1.4. No emulator process, save, ROM candidate or
native patch is written. This is bounded instruction execution, not gameplay.
"""
import argparse
import json
import struct
from pathlib import Path

from inspect_persistent_items import inspect, require


def replay(data, entry, index, requested=158, null_target=False):
    from unicorn import Uc, UC_ARCH_ARM, UC_MODE_ARM, UC_HOOK_CODE, UC_HOOK_MEM_WRITE
    from unicorn.arm_const import (UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R2,
                                   UC_ARM_REG_R4, UC_ARM_REG_SP, UC_ARM_REG_LR, UC_ARM_REG_PC)
    require(entry in (0x8524, 0xc2ec), 'Callback outside the bounded experiment')
    require(type(index) is int and 0 <= index <= 255 and type(requested) is int and 0 <= requested <= 0xffffffff,
            'Invalid synthetic callback arguments')
    load, context, arguments, table, pokemon, stack, stop = (
        0x100000, 0x300000, 0x301000, 0x310000, 0x320000, 0x508000, 0x600000)
    uc = Uc(UC_ARCH_ARM, UC_MODE_ARM)
    uc.mem_map(load, (len(data)+4095) & ~4095)
    uc.mem_write(load, bytes(data))
    uc.mem_map(context, 0x30000)
    uc.mem_map(0x500000, 0x10000)
    uc.mem_map(stop, 4096)
    uc.mem_write(context+8, struct.pack('<I', table))
    if index < 24:
        uc.mem_write(table+0x84+index*4, struct.pack('<I', 0 if null_target else pokemon))
    uc.mem_write(arguments, struct.pack('<II', index, requested))
    uc.mem_write(pokemon+0x12, struct.pack('<HH', 657, 0))
    uc.reg_write(UC_ARM_REG_R0, context)
    uc.reg_write(UC_ARM_REG_R1, context+0x100)
    uc.reg_write(UC_ARM_REG_R2, arguments)
    uc.reg_write(UC_ARM_REG_SP, stack)
    uc.reg_write(UC_ARM_REG_LR, stop)
    saved = {UC_ARM_REG_R4+i: 0x11110000+i for i in range(8)}
    for reg, value in saved.items():
        uc.reg_write(reg, value)
    writes, diagnostic = [], []
    end = 0x8584 if entry == 0x8524 else 0xc354
    def code_hook(machine, address, size, _):
        if address == load+0x394:
            diagnostic.append('Native diagnostic import reached; stopped before executing it')
            machine.emu_stop()
        else:
            require(load+entry <= address < load+end, 'Execution escaped the bounded callback')
    def write_hook(machine, access, address, size, value, _):
        if pokemon+0x12 <= address < pokemon+0x16:
            writes.append(dict(relative_offset=hex(address-pokemon), size=size, value=value))
    uc.hook_add(UC_HOOK_CODE, code_hook)
    uc.hook_add(UC_HOOK_MEM_WRITE, write_hook)
    uc.emu_start(load+entry, stop, count=256)
    returned = uc.reg_read(UC_ARM_REG_PC) == stop
    require(returned or diagnostic, 'Instruction budget ended without a native return or diagnostic')
    current, remembered = struct.unpack('<HH', uc.mem_read(pokemon+0x12, 4))
    if returned:
        require(uc.reg_read(UC_ARM_REG_SP) == stack and all(uc.reg_read(reg) == value for reg, value in saved.items()),
                'Native callback did not preserve expected callee-saved registers/SP')
    return dict(entry=hex(entry), index=index, requested=requested, null_target=null_target,
                returned=returned, native_return=uc.reg_read(UC_ARM_REG_R0) if returned else None,
                item_12=current, item_14=remembered, writes=writes, diagnostics=diagnostic,
                gameplay_verified=False, installable=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project', type=Path)
    args = parser.parse_args()
    profiles = json.loads(Path(__file__).with_name('observed_profiles.json').read_text(encoding='utf-8'))
    identity = inspect(args.project, profiles)
    require(identity['observation_matches'], 'Unrecognized research dependencies')
    data = (args.project/'ExtractedRomFS/DllBattle.cro').read_bytes()
    result = []
    for entry in (0x8524, 0xc2ec):
        for index, requested, null_target in ((0, 158, False), (23, 158, False),
                                              (0, 0x12345678, False), (24, 158, False), (0, 158, True)):
            row = replay(data, entry, index, requested, null_target)
            if index >= 24 or null_target:
                require(not row['returned'] and row['writes'] == [], 'Invalid participant wrote item fields')
            else:
                require(row['returned'] and row['native_return'] == 1, 'Valid native callback did not return 1')
                require((row['item_12'], row['item_14']) ==
                        ((requested & 65535, 0) if entry == 0x8524 else (0, requested & 65535)),
                        'Unexpected native item write')
            result.append(row)
    print(json.dumps(dict(title_id=identity['title_id'], installable=False, results=result), indent=2))


if __name__ == '__main__':
    main()
