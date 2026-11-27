"""Replay input/import-contract tests without Unicorn or ARM execution.

The fake CPU below invokes the real harness callbacks directly. These checks
prove harness bounds and contract checks, NOT native reconciliation behavior.
"""
import contextlib
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

import replay_two_gates as replay
from test_experimental_two_gates import original, fingerprint


@contextlib.contextmanager
def fake_cpu(program):
    class Cpu:
        instances = []

        def __init__(self, arch, mode):
            self.registers = {}
            self.observed = []
            self.instances.append(self)

        def mem_map(self, address, size):
            pass

        def mem_write(self, address, data):
            pass

        def reg_read(self, register):
            return self.registers.get(register, 0)

        def reg_write(self, register, value):
            self.registers[register] = value

        def hook_add(self, kind, hook):
            self.hook = hook

        def emu_start(self, start, end, count):
            self.request = (start, end, count)
            program(self)

        def invoke(self, offset, r0, r1=0, r2=0):
            self.reg_write(0, r0)
            self.reg_write(1, r1)
            self.reg_write(2, r2)
            self.reg_write(14, 0x114850)
            self.hook(self, 0x100000+offset, 4, None)
            self.observed.append(self.reg_read(0))

    unicorn = types.ModuleType('unicorn')
    unicorn.Uc, unicorn.UC_ARCH_ARM, unicorn.UC_MODE_ARM, unicorn.UC_HOOK_CODE = Cpu, 1, 2, 3
    constants = types.ModuleType('unicorn.arm_const')
    for name, value in (('R0', 0), ('R1', 1), ('R2', 2), ('R4', 4), ('R7', 7),
                        ('R8', 8), ('R9', 9), ('SP', 13), ('LR', 14), ('PC', 15)):
        setattr(constants, 'UC_ARM_REG_'+name, value)
    with patch.dict(sys.modules, {'unicorn': unicorn, 'unicorn.arm_const': constants}):
        yield Cpu


class ReplayContractTests(unittest.TestCase):
    def test_import_callbacks_read_original_write_only_current_and_keep_replay_claims_synthetic(self):
        def program(cpu):
            cpu.invoke(0x264, 0x310000)             # Party getter: original member 0.
            cpu.invoke(0x6ac, 0x320000)            # Held-item getter on original core.
            cpu.invoke(0x3ac, 0x312000, 158, 17)   # Explicit item-manager parameter contract.
            cpu.invoke(0x6b4, 0x330000, 158)       # Working member setter only.
            cpu.reg_write(15, 0x1148dc)
        parameters = [0]*640
        parameters[158] = 1
        with fake_cpu(program) as cpu:
            result = replay.run_loop(bytes(0x15000), 1, [158], [0], parameters)
        self.assertEqual(result, dict(items=[158], setters=[dict(slot=0, item=158)],
                                      imports=['0x264', '0x6ac', '0x3ac', '0x6b4'], gameplay_verified=False))
        self.assertEqual(cpu.instances[0].observed, [0x320000, 158, 1, 0])
        self.assertEqual(cpu.instances[0].request, (0x114848, 0x1148dc, 4096))

    def test_unexpected_import_targets_parameters_escaped_code_and_missing_return_fail_closed(self):
        programs = [lambda cpu: cpu.invoke(0x264, 0x310000, 1),
                    lambda cpu: cpu.invoke(0x6ac, 0x350000),
                    lambda cpu: cpu.invoke(0x3ac, 0x312000, 158, 16),
                    lambda cpu: cpu.invoke(0x3ac, 0x312000, 640, 17),
                    lambda cpu: cpu.invoke(0x6b4, 0x320000, 0),
                    lambda cpu: cpu.invoke(0x14b00, 0),
                    lambda cpu: cpu.reg_write(15, 0x114850)]
        for index, program in enumerate(programs):
            with self.subTest(index=index), fake_cpu(program), self.assertRaises(ValueError):
                replay.run_loop(bytes(0x15000), 1, [158], [0], [0]*640)

    def test_replay_rejects_numeric_modes_party_mismatch_and_more_than_six_members(self):
        def unexpected_execution(cpu):
            raise AssertionError('Invalid inputs reached CPU execution')
        for mode, initial, current in ((-1, [158], [0]), (256, [158], [0]),
                                       (1, [158], []), (1, [158]*7, [0]*7)):
            with self.subTest(mode=mode, length=len(initial)), fake_cpu(unexpected_execution), self.assertRaises(ValueError):
                replay.run_loop(bytes(0x15000), mode, initial, current, [0]*640)


class ReplayPreflightTests(unittest.TestCase):
    def inputs(self, directory):
        project = Path(directory)
        cro = project/'ExtractedRomFS/DllBattle.cro'
        cro.parent.mkdir()
        cro.write_bytes(original())
        items = project/'ExtractedRomFS/a/2/2/0'
        items.parent.mkdir(parents=True)
        items.write_bytes(b'Synthetic item archive used only for hash-race checking')
        identity = dict(observation_matches=True, title_id='0004000000055D00', sha256={
            'ExtractedRomFS/DllBattle.cro': fingerprint(cro.read_bytes()),
            'ExtractedRomFS/a/2/2/0': fingerprint(items.read_bytes())})
        return project, items, identity

    def test_changed_item_archive_after_inspection_is_rejected_before_any_replay(self):
        with tempfile.TemporaryDirectory() as directory:
            project, items, identity = self.inputs(directory)
            changed = b'Concurrent external item edit'
            def inspected(*args):
                items.write_bytes(changed)
                return identity
            with patch.object(sys, 'argv', ['replay_two_gates.py', str(project)]), \
                 patch.object(replay, 'inspect', side_effect=inspected), \
                 patch.object(replay, 'run_loop') as loop, patch.object(replay, 'garc_members') as unpack:
                with self.assertRaisesRegex(ValueError, 'Item resource changed'):
                    replay.main()
                loop.assert_not_called()
                unpack.assert_not_called()
            self.assertEqual(items.read_bytes(), changed)

    def test_y_or_unknown_observation_cannot_start_the_x_replay(self):
        for case in ('Y', 'unknown'):
            with self.subTest(case=case), tempfile.TemporaryDirectory() as directory:
                project, _, identity = self.inputs(directory)
                if case == 'Y':
                    identity['title_id'] = '0004000000055E00'
                else:
                    identity['observation_matches'] = False
                with patch.object(sys, 'argv', ['replay_two_gates.py', str(project)]), \
                     patch.object(replay, 'inspect', return_value=identity), patch.object(replay, 'run_loop') as loop:
                    with self.assertRaises(ValueError):
                        replay.main()
                    loop.assert_not_called()


if __name__ == '__main__':
    unittest.main()
