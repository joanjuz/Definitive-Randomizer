"""Replay XY's native shop item loads on authorized, isolated code buffers.

Usage: python verify_shop_pointer_reads.py CODE EXHEADER EXPECTED_ROWS_JSON
Requires Unicorn. Reads inputs only; this is not a gameplay test.
EXPECTED_ROWS_JSON contains {"Rows": [[item_id, ...], ...]}.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

from unicorn import Uc, UC_ARCH_ARM, UC_MODE_ARM
from unicorn.arm_const import UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R5
from unicorn.arm_const import UC_ARM_REG_R7, UC_ARM_REG_R9, UC_ARM_REG_R10


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("code", type=Path)
    parser.add_argument("exheader", type=Path)
    parser.add_argument("expected", type=Path)
    args = parser.parse_args()
    code, header = args.code.read_bytes(), args.exheader.read_bytes()
    title = struct.unpack_from("<Q", header, 0x200)[0]
    assert title in (0x0004000000055D00, 0x0004000000055E00), "XY title required"
    base, = struct.unpack_from("<I", header, 16)
    assert code[0x2300D8:0x2300DC] == bytes.fromhex("0700d9e7")
    assert code[0x2300EC:0x2300FC] == bytes.fromhex("07019ae70120a0e3850080e0b010d0e1")
    # PC-relative literals used by the native constructor, not the editor's descriptor.
    counts, pointers = struct.unpack_from("<II", code, 0x23022C)
    expected = json.loads(args.expected.read_text(encoding="utf-8-sig"))["Rows"]
    assert len(expected) == 26
    emulator = Uc(UC_ARCH_ARM, UC_MODE_ARM)
    emulator.mem_map(base, (len(code) + 4095) & ~4095)
    emulator.mem_write(base, code)
    actual = []
    for shop in range(26):
        emulator.reg_write(UC_ARM_REG_R9, counts)
        emulator.reg_write(UC_ARM_REG_R7, shop)
        emulator.emu_start(base + 0x2300D8, base + 0x2300DC, count=1)
        count = emulator.reg_read(UC_ARM_REG_R0)
        assert 1 <= count <= 60, "Native item array capacity exceeded"
        row = []
        for slot in range(count):
            emulator.reg_write(UC_ARM_REG_R10, pointers)
            emulator.reg_write(UC_ARM_REG_R5, slot)
            emulator.emu_start(base + 0x2300EC, base + 0x2300FC, count=4)
            row.append(emulator.reg_read(UC_ARM_REG_R1))
        actual.append(row)
    matches = [i + 1 for i, (a, b) in enumerate(zip(actual, expected)) if a == b]
    print(json.dumps({"TitleId": f"{title:016X}", "CodeSha256": hashlib.sha256(code).hexdigest(),
                      "MatchedShops": matches, "All26Match": len(matches) == 26,
                      "GameplayValidated": False}, indent=2))
    return 0 if len(matches) == 26 else 1


if __name__ == "__main__":
    raise SystemExit(main())
