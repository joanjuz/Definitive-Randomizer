"""Synthetic RSP/guest-memory tests. Never connect to or launch an emulator."""
import socket
import struct
import unittest

import gdb_trace as trace


def packet(body):
    if isinstance(body, str):
        body = body.encode('ascii')
    return b'$' + body + b'#' + f'{sum(body) & 255:02x}'.encode('ascii')


class FakeSocket:
    def __init__(self, chunks=()):
        self.chunks = list(chunks)
        self.sent = []

    def settimeout(self, value):
        self.timeout = value

    def sendall(self, data):
        self.sent.append(data)

    def recv(self, size):
        if not self.chunks:
            return b''
        value = self.chunks.pop(0)
        if isinstance(value, Exception):
            raise value
        return value


class MemoryRsp(trace.Rsp):
    """A deterministic fake remote with Citra's transient RAM breakpoint behavior."""
    def __init__(self):
        super().__init__(FakeSocket())
        self.ram = {}
        self.saved = {}
        self.commands = []
        self.bad_restore = False
        self.fail_install = False

    def memory(self, address, size):
        self.commands.append(('memory', address, size))
        if address not in self.ram or len(self.ram[address]) != size:
            raise ValueError('Synthetic unmapped memory')
        return self.ram[address]

    def command(self, command):
        self.commands.append(command)
        address = int(command.split(',')[1], 16)
        if command.startswith('Z0,'):
            self.saved[address] = self.ram[address]
            self.ram[address] = trace.TRAP
            if self.fail_install:
                raise socket.timeout('Synthetic post-install timeout')
        elif command.startswith('z0,'):
            self.ram[address] = b'BAD!' if self.bad_restore else self.saved[address]
        else:
            raise AssertionError('Unexpected command: ' + command)
        return 'OK'


class PayloadTests(unittest.TestCase):
    def test_decode_unescapes_reserved_characters_and_then_rle(self):
        self.assertEqual(trace.decode_payload(b'}\x03}\x04}]}' + b'\x0a'), '#$}*')
        self.assertEqual(trace.decode_payload(b'0* 1'), '00001')
        self.assertEqual(trace.decode_payload(b'a*"b'), 'aaaaaab')

    def test_decode_rejects_truncated_escape_repeat_and_expansion_limit(self):
        for body in (b'}', b'* ', b'0*', b'0*\x1f', b'0*\x7f', b'x' * 65537):
            with self.subTest(body=body[:12]), self.assertRaises(ValueError):
                trace.decode_payload(body)

    def test_decode_rejects_reserved_rle_counts(self):
        for count in (b'#', b'$', b'+', b'-'):
            with self.subTest(count=count), self.assertRaises(ValueError):
                trace.decode_payload(b'0*' + count)


class TransportTests(unittest.TestCase):
    def test_receive_fragments_checksum_and_preserves_coalesced_next_packet(self):
        wire = b'+' + packet(b'0* ') + packet('T05thread:2;')
        sock = FakeSocket([bytes([byte]) for byte in wire[:8]] + [wire[8:]])
        rsp = trace.Rsp(sock)
        rsp.running = True
        self.assertEqual(rsp.receive(), '0000')
        self.assertEqual(rsp.receive(), 'T05thread:2;')
        self.assertEqual(sock.sent, [b'+', b'+'])
        self.assertFalse(rsp.running)
        self.assertEqual(rsp.last_stop, 'T05thread:2;')

    def test_receive_checksum_is_computed_before_unescape(self):
        sock = FakeSocket([packet(b'}\x03')])
        self.assertEqual(trace.Rsp(sock).receive(), '#')
        self.assertEqual(sock.sent, [b'+'])

    def test_bad_checksum_and_nonhex_checksum_nack_then_accept_retransmission(self):
        sock = FakeSocket([b'$OK#00$OK#xx' + packet('OK')])
        self.assertEqual(trace.Rsp(sock).receive(), 'OK')
        self.assertEqual(sock.sent, [b'-', b'-', b'+'])

    def test_disconnect_and_timeout_are_not_synthetic_stop_responses(self):
        for chunks, error in (([b'$T05'], EOFError), ([socket.timeout('No stop')], socket.timeout)):
            rsp = trace.Rsp(FakeSocket(chunks))
            rsp.running = True
            with self.subTest(error=error), self.assertRaises(error):
                rsp.receive()
            self.assertTrue(rsp.running)
            self.assertEqual(rsp.last_stop, '')

    def test_remote_nack_is_not_retried_as_an_ambiguous_breakpoint_write(self):
        sock = FakeSocket([b'-'])
        with self.assertRaises(ValueError):
            trace.Rsp(sock).command('Z0,100000,4')
        self.assertEqual(sock.sent, [packet('Z0,100000,4')])

    def test_send_rejects_write_kill_and_address_control_packets_without_sending(self):
        for command in ('M100000,4:00000000', 'X100000,4:ABCD', 'P0=00000000', 'G00000000',
                        'k', 'c100000', 's100000', 'vCont;c', 'Z1,100000,4', 'm100000,4\nM0,1:ff'):
            sock = FakeSocket()
            with self.subTest(command=command), self.assertRaises(ValueError):
                trace.Rsp(sock).send(command)
            self.assertEqual(sock.sent, [])

    def test_remote_error_and_incomplete_memory_fail_closed(self):
        for reply in ('E01', '00', 'zzzzzzzz'):
            sock = FakeSocket([packet(reply)])
            with self.subTest(reply=reply), self.assertRaises(ValueError):
                trace.Rsp(sock).memory(0x100000, 4)

    def test_memory_address_size_boundaries_and_exact_hex_bytes(self):
        for address, size in ((0xfffff, 1), (0x10000000, 0), (0xfffffff, 2),
                              (0x100000, -1), (0x100000, 4097)):
            sock = FakeSocket()
            with self.subTest(address=address, size=size), self.assertRaises(ValueError):
                trace.Rsp(sock).memory(address, size)
            self.assertEqual(sock.sent, [])
        sock = FakeSocket([packet('1234abcd')])
        self.assertEqual(trace.Rsp(sock).memory(0xffffffc, 4), b'\x12\x34\xab\xcd')
        self.assertEqual(sock.sent[0], packet('mffffffc,4'))

    def test_registers_select_stop_thread_before_individual_little_endian_reads(self):
        replies = [packet('OK')] + [packet(struct.pack('<I', value).hex()) for value in range(17)]
        sock = FakeSocket(replies)
        rsp = trace.Rsp(sock)
        rsp.last_stop = 'T05thread:a;'
        result = rsp.registers()
        sent = [p for p in sock.sent if p.startswith(b'$')]
        self.assertEqual(sent, [packet('Hga')] + [packet(f'p{i:x}') for i in list(range(16)) + [25]])
        self.assertEqual(result, dict(**{f'r{i}': i for i in range(16)}, cpsr=16, thread='a'))

    def test_registers_fall_back_from_empty_qc_to_thread_list_and_reject_selection_failure(self):
        sock = FakeSocket([packet(''), packet('m3,4'), packet('E01')])
        with self.assertRaises(ValueError):
            trace.Rsp(sock).registers()
        self.assertEqual([p for p in sock.sent if p.startswith(b'$')],
                         [packet('qC'), packet('qfThreadInfo'), packet('Hg3')])

    def test_resume_requires_stopped_cpu_and_interrupt_waits_for_real_stop(self):
        sock = FakeSocket([packet('S05'), packet('PacketSize=2000')])
        rsp = trace.Rsp(sock)
        rsp.resume()
        self.assertTrue(rsp.running)
        with self.assertRaises(ValueError):
            rsp.resume()
        rsp.interrupt()
        self.assertFalse(rsp.running)
        self.assertEqual(sock.sent, [packet('c'), b'\x03', b'+', packet('qSupported'), b'+'])

    def test_citra_empty_interrupt_trailer_is_drained_before_memory_without_resending(self):
        wire = packet('T05thread:1;')+packet('')+packet('PacketSize=2000')+packet('41424344')
        for chunks in ([wire], [bytes([byte]) for byte in wire]):
            with self.subTest(fragmented=len(chunks)>1):
                sock = FakeSocket(chunks)
                rsp = trace.Rsp(sock)
                rsp.running = True
                rsp.interrupt()
                self.assertEqual(rsp.memory(0x100000, 4), b'ABCD')
                self.assertEqual([p for p in sock.sent if p.startswith(b'$')],
                                 [packet('qSupported'), packet('m100000,4')])
                self.assertFalse(rsp.running)

    def test_interrupt_fence_rejects_unknown_reply_and_more_than_one_empty_trailer(self):
        for replies in ((packet(''), packet('')), (packet('E01'),), (packet('OK'),)):
            with self.subTest(replies=replies):
                sock = FakeSocket([packet('T05'), *replies])
                rsp = trace.Rsp(sock)
                rsp.running = True
                with self.assertRaises(ValueError):
                    rsp.interrupt()
                self.assertNotIn(packet('c'), sock.sent)

    def test_single_step_sends_no_address_and_requires_stop_reply(self):
        sock = FakeSocket([packet('T05thread:1;')])
        rsp = trace.Rsp(sock)
        rsp.step()
        self.assertFalse(rsp.running)
        self.assertEqual(sock.sent, [packet('s'), b'+'])


class BreakpointTests(unittest.TestCase):
    def test_owned_breakpoint_verifies_stock_trap_and_exact_restoration(self):
        rsp = MemoryRsp()
        rsp.ram[0x100000] = b'ABCD'
        rsp.add_breakpoint(0x100000, b'ABCD')
        self.assertEqual(rsp.ram[0x100000], trace.TRAP)
        self.assertEqual(rsp.breakpoints, {0x100000: b'ABCD'})
        rsp.remove_breakpoint(0x100000)
        self.assertEqual(rsp.ram[0x100000], b'ABCD')
        self.assertEqual(rsp.breakpoints, {})
        self.assertEqual(rsp.commands, [('memory', 0x100000, 4), 'Z0,100000,4',
                                       ('memory', 0x100000, 4), ('memory', 0x100000, 4),
                                       'z0,100000,4', ('memory', 0x100000, 4)])

    def test_duplicate_unaligned_or_changed_stock_never_inserts_second_trap(self):
        rsp = MemoryRsp()
        rsp.ram[0x100000] = b'ABCD'
        rsp.add_breakpoint(0x100000, b'ABCD')
        for address, expected in ((0x100000, b'ABCD'), (0x100002, b'ABCD'), (0x100004, b'ABCD')):
            rsp.ram[0x100004] = b'EDIT'
            with self.subTest(address=address), self.assertRaises(ValueError):
                rsp.add_breakpoint(address, expected)
        self.assertEqual([c for c in rsp.commands if isinstance(c, str)], ['Z0,100000,4'])

    def test_timeout_after_remote_insert_retains_ownership_for_cleanup(self):
        rsp = MemoryRsp()
        rsp.ram[0x100000] = b'ABCD'
        rsp.fail_install = True
        with self.assertRaises(socket.timeout):
            rsp.add_breakpoint(0x100000, b'ABCD')
        self.assertEqual(rsp.breakpoints, {0x100000: b'ABCD'})
        rsp.fail_install = False
        rsp.cleanup()
        self.assertEqual(rsp.ram[0x100000], b'ABCD')
        self.assertEqual(rsp.breakpoints, {})

    def test_cleanup_does_not_resume_when_original_restoration_fails(self):
        rsp = MemoryRsp()
        rsp.ram[0x100000] = b'ABCD'
        rsp.add_breakpoint(0x100000, b'ABCD')
        rsp.bad_restore = True
        with self.assertRaises(ValueError):
            rsp.cleanup()
        self.assertEqual(rsp.breakpoints, {0x100000: b'ABCD'})
        self.assertFalse(rsp.running)
        self.assertEqual(rsp.sock.sent, [])
        with self.assertRaises(ValueError):
            rsp.resume()
        with self.assertRaises(ValueError):
            rsp.step()
        self.assertEqual(rsp.sock.sent, [])

    def test_external_trap_replacement_and_unowned_removal_never_emit_z(self):
        rsp = MemoryRsp()
        with self.assertRaises(ValueError):
            rsp.remove_breakpoint(0x100000)
        rsp.ram[0x100000] = b'ABCD'
        rsp.add_breakpoint(0x100000, b'ABCD')
        rsp.ram[0x100000] = b'EDIT'
        with self.assertRaises(ValueError):
            rsp.remove_breakpoint(0x100000)
        self.assertNotIn('z0,100000,4', rsp.commands)
        self.assertEqual(rsp.breakpoints, {0x100000: b'ABCD'})


class StructureTests(unittest.TestCase):
    def module(self, base, magic=b'CRO0', relocated=False):
        stock = bytearray(0x20000)
        struct.pack_into('<I', stock, 0xb0, 0x180)
        stock[0x80:0x84] = b'CRO0'
        header = bytearray(stock[:0x180])
        header[0x80:0x84] = magic
        struct.pack_into('<I', header, 0xb0, base + 0x180 if relocated else 0x180)
        rsp = MemoryRsp()
        rsp.ram[base] = header
        for index, offset in enumerate(trace.POINTS):
            struct.pack_into('<I', stock, offset, index + 0x12000000)
            rsp.ram[base + offset] = bytes(stock[offset:offset + 4])
        return rsp, stock

    def test_module_mapping_accepts_original_and_fixed_relocated_headers_at_different_bases(self):
        for base, magic, relocated in ((0x1000000, b'CRO0', False), (0x1800000, b'FIXD', True)):
            with self.subTest(base=base):
                rsp, stock = self.module(base, magic, relocated)
                self.assertEqual(trace.module_text(rsp, base, stock), base + 0x180)
                self.assertEqual(rsp.commands, [('memory', base, 0x180)] +
                                 [('memory', base + offset, 4) for offset in trace.POINTS])

    def test_module_mapping_rejects_unknown_mapping_and_any_changed_instruction(self):
        base = 0x1000000
        for mutation in ('magic', 'mapping', 'last_instruction'):
            rsp, stock = self.module(base)
            if mutation == 'magic':
                rsp.ram[base][0x80:0x84] = b'FAIL'
            elif mutation == 'mapping':
                struct.pack_into('<I', rsp.ram[base], 0xb0, base + 0x200)
            else:
                rsp.ram[base + trace.POINTS[-1]] = b'EDIT'
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                trace.module_text(rsp, base, stock)

    def test_missing_snapshot_memory_is_recorded_as_error_without_invented_fields(self):
        rsp = MemoryRsp()
        regs = dict(r0=0x100000, r9=0x200000)
        result = trace.snapshot(rsp, regs, 0x1469c)
        self.assertEqual(result, dict(registers=regs, file_offset='0x1469c',
                                     structure_error='Synthetic unmapped memory'))

    def test_snapshot_preserves_opaque_bytes_and_labels_candidates_without_species_claims(self):
        rsp = MemoryRsp()
        context, meta, before, after, core, pokemon = (0x100000 + i * 0x1000 for i in range(6))
        regs = dict(r0=context, r9=0, r6=158, r1=275)
        rsp.ram[context + 0x10] = struct.pack('<I', meta)
        raw_meta = bytearray(0x60)
        raw_meta[0], raw_meta[0x4b] = 7, 9
        struct.pack_into('<I', raw_meta, 0x4c, before)
        rsp.ram[meta] = raw_meta
        rsp.ram[context + 0x815] = b'\x02'
        rsp.ram[context + 0x49c + 8] = struct.pack('<I', after)
        rsp.ram[before + 0x18] = b'\x01'
        rsp.ram[after + 0x18] = b'\x00'
        rsp.ram[before] = struct.pack('<I', core)
        raw_core = bytearray(0x20)
        struct.pack_into('<I', raw_core, 0xc, pokemon)
        rsp.ram[core] = raw_core
        rsp.ram[pokemon] = bytes(range(0xe8))
        result = trace.snapshot(rsp, regs, 0x148ac, context)
        self.assertNotIn('structure_error', result)
        self.assertEqual((result['mode_byte'], result['mode_flag_4b'], result['participant_side']), (7, 9, 2))
        self.assertEqual(result['original_item_candidate'], 158)
        self.assertEqual(result['parameter17_result'], context)
        self.assertEqual(result['parties']['before_candidate']['members'][0]['opaque_pokemon_hex'], bytes(range(0xe8)).hex())
        self.assertEqual(result['parties']['after_candidate'], dict(pointer=hex(after), count=0, members=[]))
        self.assertNotIn('species', str(result))


if __name__ == '__main__':
    unittest.main()
