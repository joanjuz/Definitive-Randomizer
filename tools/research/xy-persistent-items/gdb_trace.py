"""Original-game tracing for a prepared, isolated Citra session. Never installs a patch.

Breakpoints transiently replace words in GUEST RAM in Citra, including Z1.
No memory/register-write packet is exposed. Logs contain private RAM/save data.
"""
import argparse
import json
import re
import socket
import struct
import subprocess
import time
from pathlib import Path

from inspect_persistent_items import Cro, region, require, sha


POINTS = (0x1469c, 0x14850, 0x14854, 0x14888, 0x148a0, 0x148ac,
          0x148b8, 0x148cc, 0x148d0, 0x14948, 0x14950, 0x14954, 0x1945c, 0x19460)
TRAP = bytes.fromhex('700020e1')


def decode_payload(raw):
    result = bytearray()
    i = 0
    while i < len(raw):
        byte = raw[i]
        if byte == 0x7d:
            i += 1
            require(i < len(raw), 'Truncated RSP escape')
            result.append(raw[i] ^ 0x20)
        elif byte == 0x2a:
            i += 1
            require(result and i < len(raw) and raw[i] not in b'#$+-' and 3 <= raw[i]-29 <= 97,
                    'Invalid RSP repetition')
            result.extend([result[-1]] * (raw[i]-29))
        else:
            result.append(byte)
        require(len(result) <= 65536, 'Oversized RSP payload')
        i += 1
    return bytes(result).decode('ascii')


class Rsp:
    def __init__(self, sock, timeout=5):
        self.sock = sock
        self.sock.settimeout(timeout)
        self.buffer = bytearray()
        self.breakpoints = {}
        self.running = False
        self.last_stop = ''
        self.unsafe = False

    def send(self, command):
        # Deliberately exclude M/X/P/G, kill, and resume/step with an address.
        require(re.fullmatch(r'(qSupported|qfThreadInfo|qC|\?|Hg[0-9a-f]+|p[0-9a-f]+|'
                             r'm[0-9a-f]+,[0-9a-f]+|[Zz]0,[0-9a-f]+,4|c|s|D)', command),
                'RSP command outside tracing allowlist')
        body = command.encode('ascii')
        self.sock.sendall(b'$'+body+b'#'+f'{sum(body)&255:02x}'.encode())

    def receive(self):
        while True:
            if self.buffer.startswith(b'-'):
                raise ValueError('Server rejected checksum; do not retry a possibly applied breakpoint command')
            start = self.buffer.find(b'$')
            if start >= 0:
                end = self.buffer.find(b'#', start+1)
                if end >= 0 and len(self.buffer) >= end+3:
                    raw = bytes(self.buffer[start+1:end])
                    checksum = bytes(self.buffer[end+1:end+3])
                    del self.buffer[:end+3]
                    try:
                        valid = sum(raw)&255 == int(checksum, 16)
                    except ValueError:
                        valid = False
                    self.sock.sendall(b'+' if valid else b'-')
                    if valid:
                        packet = decode_payload(raw)
                        if packet.startswith(('T', 'S')):
                            self.running = False
                            self.last_stop = packet
                        return packet
                    continue
            require(len(self.buffer) <= 131072, 'Oversized RSP frame')
            data = self.sock.recv(8192)
            if not data:
                raise EOFError('Debugger disconnected; breakpoint cleanup is NOT guaranteed')
            self.buffer.extend(data)

    def command(self, command):
        self.send(command)
        reply = self.receive()
        require(not reply.startswith('E'), f'GDB rejected {command}: {reply}')
        return reply

    def memory(self, address, size):
        require(0x100000 <= address < 0x10000000 and 0 <= size <= 4096 and address+size <= 0x10000000,
                'Memory request outside bounded 3DS application space')
        reply = self.command(f'm{address:x},{size:x}')
        require(len(reply) == size*2,
                f'Incomplete memory response: expected {size*2} hex characters, received {len(reply)}')
        return bytes.fromhex(reply)

    def registers(self):
        thread = re.search(r'thread:([0-9a-f]+)', self.last_stop)
        if thread:
            selected = thread.group(1)
        else:
            current = self.command('qC')
            if current.startswith('QC'):
                selected = current[2:]
            else:
                threads = self.command('qfThreadInfo')
                require(threads.startswith('m'), 'No ARM thread available')
                selected = threads[1:].split(',')[0]
        require(self.command('Hg'+selected) == 'OK', 'Cannot select ARM thread')
        values = {}
        # Avoid the old Citra bulk-g FPU layout issue; select thread explicitly.
        for index in list(range(16))+[25]:
            reply = self.command(f'p{index:x}')
            require(len(reply) == 8, 'Incomplete ARM register')
            values['cpsr' if index == 25 else f'r{index}'] = struct.unpack('<I', bytes.fromhex(reply))[0]
        values['thread'] = selected
        return values

    def interrupt(self):
        if not self.running:
            return
        self.sock.sendall(b'\x03')
        require(self.receive().startswith(('T', 'S')), 'No stop response to interrupt')
        # Observed Citra 2104 wire sequence after Ctrl+C: T05 followed by $#00.
        # Fence with a read-only, supported query and drain at most that one
        # empty trailer. Never retry the next command (especially Z/z).
        self.send('qSupported')
        reply = self.receive()
        if reply == '':
            reply = self.receive()
        require(reply.startswith('PacketSize='), 'Debugger did not resynchronise after interrupt')

    def resume(self):
        require(not self.running and not self.unsafe, 'Already running or RAM cleanup is unverified')
        self.send('c')
        self.running = True

    def step(self):
        require(not self.running and not self.unsafe, 'Step requires stopped CPU and verified RAM state')
        self.send('s')
        self.running = True
        require(self.receive().startswith(('T', 'S')), 'No single-step stop response')

    def add_breakpoint(self, address, expected):
        require(not self.running and address % 4 == 0 and len(expected) == 4, 'Invalid breakpoint request')
        require(address not in self.breakpoints, 'Breakpoint already owned; duplicate Z would lose original bytes')
        require(self.memory(address, 4) == expected, 'Instruction changed before breakpoint')
        # Record ownership before sending: a timeout may follow a successful RAM write.
        self.breakpoints[address] = expected
        try:
            require(self.command(f'Z0,{address:x},4') == 'OK', 'Breakpoint was not accepted')
            require(self.memory(address, 4) == TRAP, 'Citra RAM trap was not verified')
        except Exception:
            self.unsafe = True
            raise

    def remove_breakpoint(self, address):
        require(not self.running and address in self.breakpoints, 'Breakpoint is not owned or CPU running')
        original = self.breakpoints[address]
        try:
            require(self.memory(address, 4) == TRAP, 'RAM breakpoint was replaced externally; do not resume')
            require(self.command(f'z0,{address:x},4') == 'OK', 'Breakpoint removal failed')
            require(self.memory(address, 4) == original, 'Original RAM instruction was not restored')
        except Exception:
            self.unsafe = True
            raise
        del self.breakpoints[address]

    def cleanup(self):
        self.interrupt()
        for address in list(self.breakpoints):
            self.remove_breakpoint(address)

    def resume_clean(self):
        """Return control only after every owned trap has been verified removed."""
        require(not self.breakpoints and not self.unsafe, 'Cannot return control with unverified RAM traps')
        self.resume()
        # Continue has no ordinary reply. Await its ACK before disconnecting so
        # the old Citra stub has received the complete command.
        while True:
            if self.buffer:
                value = self.buffer.pop(0)
            else:
                chunk = self.sock.recv(8192)
                require(bool(chunk), 'Disconnected before continue acknowledgement')
                self.buffer.extend(chunk)
                continue
            if value == ord('+'):
                return
            require(False, 'Unexpected response while returning control; close the isolated emulator')


def module_text(rsp, base, stock):
    header = rsp.memory(base, 0x180)
    require(header[0x80:0x84] in (b'CRO0', b'FIXD'), 'Loaded CRO header missing')
    live_code = struct.unpack_from('<I', header, 0xb0)[0]
    file_code = struct.unpack_from('<I', stock, 0xb0)[0]
    # Read the loaded header; do not assume a constant load address.
    if live_code == file_code:
        text = base+file_code
    else:
        require(base <= live_code < base+len(stock), 'Loaded .text pointer outside module')
        text = live_code
    require(text == base+file_code, 'Unexpected CRO mapping; this reader needs investigation')
    for offset in POINTS:
        require(rsp.memory(text+offset-file_code, 4) == region(stock, offset, 4),
                f'Live instruction at file offset {offset:#x} differs from observed original')
    return text


def snapshot(rsp, regs, offset, context=None, pokemon_decoder=None):
    """Read bounded structures only; do not invent decoded species or item provenance."""
    result = dict(registers=regs, file_offset=hex(offset))
    if context is None:
        context = regs['r0'] if offset in (0x1469c, 0x1945c) else regs['r9']
    def read(address, count):
        return rsp.memory(address, count)
    try:
        meta = struct.unpack('<I', read(context+0x10, 4))[0]
        meta_bytes = read(meta, 0x60)
        side = read(context+0x815, 1)[0]
        require(side < 4, 'Unrecognised participant side')
        before = struct.unpack_from('<I', meta_bytes, 0x4c)[0]
        after = struct.unpack('<I', read(context+0x49c+4*side, 4))[0]
        result.update(context=hex(context), mode_byte=meta_bytes[0], mode_flag_4b=meta_bytes[0x4b],
                      participant_side=side, metadata_hex=meta_bytes.hex(), parties={})
        for label, party in (('before_candidate', before), ('after_candidate', after)):
            count = read(party+0x18, 1)[0]
            require(count <= 6, 'Unrecognised party size')
            members = []
            for i in range(count):
                core = struct.unpack('<I', read(party+i*4, 4))[0]
                raw = read(core, 0x20)
                pokemon = struct.unpack_from('<I', raw, 0xc)[0]
                member = dict(index=i, core=hex(core), core_hex=raw.hex(),
                              pokemon=hex(pokemon), opaque_pokemon_hex=read(pokemon, 0xe8).hex())
                if pokemon_decoder is not None:
                    try:
                        accessor = read(pokemon, 0x10)
                        data_pointer = struct.unpack_from('<I', accessor, 8)[0]
                        boxed = read(data_pointer, 0xe8)
                        member.update(data_pointer=hex(data_pointer), opaque_boxed_hex=boxed.hex(),
                                      native_fields=pokemon_decoder(boxed, accessor[12]))
                    except ValueError as error:
                        member['decode_error'] = str(error)
                members.append(member)
            result['parties'][label] = dict(pointer=hex(party), count=count, members=members)
        if offset in (0x148a0, 0x148ac, 0x148b8, 0x148cc, 0x14948, 0x14950):
            result['original_item_candidate'] = regs['r6']
        if offset == 0x148ac:
            result['parameter17_result'] = regs['r0']
        if offset == 0x148b8:
            result['current_item_getter_result'] = regs['r0']
        if offset in (0x148cc, 0x14950):
            result['pending_setter'] = dict(destination=hex(regs['r0']), item=regs['r1'])
    except (ValueError, EOFError, socket.timeout) as error:
        result['structure_error'] = str(error)
    return result


def capture(session, case, launch=False):
    from prepare_trace import no_links, file_sha
    no_links(session.absolute())
    session = session.resolve()
    manifest = json.loads((session/'session.json').read_text(encoding='utf-8'))
    require(manifest['purpose'] == 'xy-persistent-items-original-trace' and not manifest['installable'], 'Wrong session')
    stock = (session/'stock/ExtractedRomFS/DllBattle.cro').read_bytes()
    require(sha(stock) == manifest['stock_sha256']['ExtractedRomFS/DllBattle.cro'], 'Stock CRO changed')
    Cro(stock)
    for name, expected in manifest['stock_sha256'].items():
        no_links(session/'stock'/name)
        require(file_sha(session/'stock'/name) == expected, 'Prepared research dependency changed')
    no_links(session/'game.3ds')
    require(file_sha(session/'game.3ds') == manifest['copied_rom_sha256'], 'Prepared ROM changed')
    require(re.fullmatch(r'[A-Za-z0-9_-]{1,64}', case), 'Use a short case name without paths')
    output = session/'evidence'/f'{case}.jsonl'
    require(not output.exists(), 'Case output already exists; choose a new case name')
    if launch:
        executable = session/'runtime/citra-qt.exe'
        no_links(executable)
        require(sha(executable.read_bytes()) == manifest['emulator_sha256'], 'Prepared emulator changed')
        subprocess.Popen([str(executable), str(session/'game.3ds')], cwd=executable.parent)
    deadline = time.monotonic()+45
    while True:
        try:
            sock = socket.create_connection(('127.0.0.1', manifest['port']), timeout=5)
            break
        except OSError:
            if time.monotonic() >= deadline:
                raise RuntimeError('No local GDB connection. Close only the isolated emulator and report its private log.')
            time.sleep(0.2)
    rsp = Rsp(sock)
    log = open(output, 'x', encoding='utf-8')
    completed = False
    def record(event):
        log.write(json.dumps(dict(time_utc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), **event))+'\n')
        log.flush()
    try:
        record(dict(event='session', case=case, title_id=manifest['title_id'], installable=False,
                    bag_delta_verified=False, item_provenance_verified=False, gameplay_verified=False))
        record(dict(event='gdb', features=rsp.command('qSupported'), stop=rsp.command('?'), registers=rsp.registers()))
        code = (session/'stock/ExtractedExeFS/.code.bin').read_bytes()
        ex = (session/'stock/DecryptedExHeader.bin').read_bytes()
        code_base = struct.unpack_from('<I', ex, 0x10)[0]
        for address in (0x39e508, 0x39d804, 0x4a16c8, 0x427f5c, 0x4a23b4):
            require(rsp.memory(address, 16) == region(code, address-code_base, 16),
                    'Loaded main executable differs at a native dependency; no trace admitted')
        from pokemon_snapshot import block_a_positions, decode_block_a
        from inspect_persistent_items import garc_members
        for address in (0x168660, 0x1685f0, 0x16b358, 0x174e6c):
            require(rsp.memory(address, 32) == region(code, address-code_base, 32),
                    'Loaded native Accessor dependency differs; no decoded trace admitted')
        positions, table_address, table = block_a_positions(code, code_base)
        require(rsp.memory(table_address, len(table)) == table, 'Loaded native block table differs')
        max_item_id = len(garc_members((session/'stock/ExtractedRomFS/a/2/2/0').read_bytes()))-1
        pokemon_decoder = lambda raw, encrypted: decode_block_a(raw, encrypted, positions, max_item_id)
        record(dict(event='native_dependency_samples_verified', code_base=hex(code_base), full_runtime_hash_verified=False))
        rsp.resume()
        print('Play manually in the isolated emulator. Waiting for DllBattle. Ctrl+C here ends capture.', flush=True)
        text = None
        context = None
        addresses = {}
        loader = session/'runtime/user/log/citra_log.txt'
        while True:
            if text is None:
                contents = loader.read_text(encoding='utf-8', errors='replace') if loader.exists() else ''
                loads = re.findall(r'CRO "DllBattle" loaded at 0x([0-9A-Fa-f]+)', contents)
                if loads:
                    rsp.interrupt()
                    text = module_text(rsp, int(loads[-1], 16), stock)
                    addresses = {text+point-0x180: point for point in POINTS}
                    for address, offset in addresses.items():
                        rsp.add_breakpoint(address, region(stock, offset, 4))
                    record(dict(event='instrumented', text_va=hex(text), formula='T+(F-0x180)', points=[hex(p) for p in POINTS]))
                    print('Original DllBattle verified; RAM breakpoints active. Finish the labelled battle.', flush=True)
                    rsp.resume()
                else:
                    time.sleep(0.1)
                    continue
            try:
                reply = rsp.receive()
            except socket.timeout:
                continue
            require(reply.startswith(('T', 'S')), 'Unexpected asynchronous debugger response')
            regs = rsp.registers()
            address = regs['r15']
            require(address in addresses, f'Unexpected ARM stop at {address:#x}; no automatic resume')
            offset = addresses[address]
            if offset in (0x1469c, 0x1945c):
                context = regs['r0']
            record(dict(event='breakpoint', **snapshot(rsp, regs, offset, context, pokemon_decoder)))
            if offset == 0x19460:
                record(dict(event='native_reconciliation_returned', gameplay_verified=False))
                completed = True
                break  # One battle per capture; never leave traps in an unloading CRO.
            rsp.remove_breakpoint(address)
            rsp.step()
            rsp.add_breakpoint(address, region(stock, offset, 4))
            rsp.resume()
    except KeyboardInterrupt:
        print('Stopping capture and restoring owned RAM breakpoints...', flush=True)
    finally:
        try:
            rsp.cleanup()
            record(dict(event='cleanup', all_owned_instructions_restored=True))
            if completed:
                rsp.resume_clean()
                record(dict(event='control_returned', continue_acknowledged=True))
                print('Owned RAM instructions restored; game resumed. No Gen6 patch installed.', flush=True)
            else:
                print('Owned RAM instructions restored. Close the isolated emulator after this incomplete capture.', flush=True)
        except Exception as error:
            record(dict(event='cleanup_failed', reason=str(error), remaining=[hex(p) for p in rsp.breakpoints]))
            print('CLEANUP UNVERIFIED: close ONLY the isolated emulator before saving or continuing. '+str(error), flush=True)
            raise
        finally:
            sock.close()
            log.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--session', type=Path, required=True)
    parser.add_argument('--case', required=True)
    parser.add_argument('--launch', action='store_true')
    args = parser.parse_args()
    capture(args.session, args.case, args.launch)
