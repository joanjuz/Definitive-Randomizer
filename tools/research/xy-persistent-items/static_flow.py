"""Bounded ARM CFG and write inventory for pinned XY research resources.

Requires capstone==5.0.7. Output may contain private native disassembly: keep it
outside Git. Calls are summarized, not assumed pure; indirect exits stay explicit.
"""
import argparse
import json
import struct
from collections import deque
from pathlib import Path

from capstone import Cs, CS_ARCH_ARM, CS_MODE_ARM
from capstone.arm import (ARM_OP_IMM, ARM_OP_MEM, ARM_OP_REG, ARM_REG_PC,
                          ARM_REG_LR, ARM_CC_AL, ARM_CC_LO, ARM_INS_B,
                          ARM_INS_BL, ARM_INS_BLX, ARM_INS_BX, ARM_INS_CMP,
                          ARM_INS_LDR, ARM_SFT_LSL)
from inspect_persistent_items import Cro, inspect, region, require, u32


# Explicit bounded regions, not a global function-discovery heuristic.
REGIONS = {'direct_set': (0x8524, 0x8584), 'direct_remember_clear': (0xc2ec, 0xc354),
           'callback_lookup': (0xbc28, 0xbcc0), 'callback_dispatch': (0xa034, 0xa328),
           'script_dispatch': (0x19600, 0x1ad4c), 'replace_item': (0x1f36c, 0x1f660),
           'exchange_items': (0x20c9c, 0x21000), 'activation': (0x6c06c, 0x6c214),
           'mutation': (0x6b140, 0x6b284), 'participant_ctor': (0x763d4, 0x76684),
           'roster_builder': (0x6d974, 0x6dc34), 'finish': (0x19308, 0x19534),
           'result_metadata': (0x15e24, 0x16020), 'setup_phase': (0x13b64, 0x141bc),
           'cleanup_phase': (0x14218, 0x143c8),
           'packet_emitter': (0x6c22c, 0x6c2c4), 'packet_serializer': (0x4dbf4, 0x4e808),
           'packet_reader': (0x9e8bc, 0x9eb74), 'write_u16_be': (0x62078, 0x620d0),
           'read_u16_be': (0x4e808, 0x4e854)}


def internal_links(data):
    """Decode internal relocation destinations; retain unknown relocation kinds."""
    cro = Cro(data)
    at, count = u32(data, 0x128), u32(data, 0x12c)
    region(data, at, count*12)
    result = {}
    for index in range(count):
        location = at+index*12
        tag, kind, segment, reserved, addend = struct.unpack('<IBBHI', region(data, location, 12))
        slot = cro.address(tag)
        if kind == 2:
            require(slot % 4 == 0 and (tag >> 4)+4 <= cro.segments[tag & 15][1],
                    'Absolute relocation destination is not an aligned word inside its segment')
        require(slot not in result, 'Duplicate internal relocation destination')
        require(segment < len(cro.segments), 'Internal relocation references missing segment')
        _, size, _ = cro.segments[segment]
        # CROs may contain end sentinels. They are not executable switch targets.
        require(addend <= size, 'Internal relocation addend outside segment')
        target = cro.segments[segment][0]+addend if segment < 3 else None
        result[slot] = dict(kind=kind, segment=segment, reserved=reserved, addend=addend,
                            target=target, record=location)
    return result


def _decoder():
    md = Cs(CS_ARCH_ARM, CS_MODE_ARM)
    md.detail = True
    return md


def writer(ins):
    """Report store addressing forms without assigning a Pokemon semantic type."""
    mnemonic = ins.mnemonic
    if not mnemonic.startswith(('str', 'stm', 'push', 'vpush', 'vstr', 'vstm', 'stc', 'srs', 'swp')):
        return None
    memory = next((op for op in ins.operands if op.type == ARM_OP_MEM), None)
    info = dict(file_offset=hex(ins.address), mnemonic=mnemonic, operands=ins.op_str,
                writeback=ins.writeback, semantic_type='unclassified')
    if memory is not None:
        info.update(base=ins.reg_name(memory.mem.base), index=ins.reg_name(memory.mem.index),
                    displacement=memory.mem.disp, shift_type=memory.shift.type,
                    shift_value=memory.shift.value)
    else:
        info['addressing'] = 'multiple/register-list or implicit; extent unresolved'
    return info


def write_inventory(data):
    cro = Cro(data)
    start, size, _ = cro.segments[0]
    require(start % 4 == 0 and size % 4 == 0, 'Unaligned ARM code segment')
    md, result = _decoder(), []
    for at in range(start, start+size, 4):
        ins = next(md.disasm(region(data, at, 4), at, count=1), None)
        if ins is not None:
            item = writer(ins)
            if item:
                result.append(item)
    return result


def bounded_cfg(data, start, end, links=None, max_nodes=4096):
    """Instruction graph over one supplied region, with explicit unresolved edges."""
    cro = Cro(data)
    text, size, _ = cro.segments[0]
    require(text <= start < end <= text+size and start % 4 == end % 4 == 0, 'CFG outside aligned code region')
    require(type(max_nodes) is int and max_nodes > 0, 'Invalid CFG node budget')
    links = internal_links(data) if links is None else links
    md, queue, nodes, edges, switch_guards = _decoder(), deque([start]), {}, [], {}
    def edge(at, target, kind, traverse=False):
        inside = target is not None and start <= target < end and target % 4 == 0
        edges.append(dict(source=hex(at), target=hex(target) if target is not None else None,
                          kind=kind, inside_region=inside))
        if traverse and inside and target not in nodes:
            queue.append(target)
    while queue:
        at = queue.popleft()
        if at in nodes:
            continue
        require(len(nodes) < max_nodes, 'CFG node budget exhausted; no complete graph emitted')
        ins = next(md.disasm(region(data, at, 4), at, count=1), None)
        require(ins is not None and ins.size == 4, 'Undecodable reachable ARM word')
        nodes[at] = dict(file_offset=hex(at), mnemonic=ins.mnemonic, operands=ins.op_str,
                         writer=writer(ins))
        conditional = ins.cc != ARM_CC_AL
        if ins.id in (ARM_INS_BL, ARM_INS_BLX):
            target = ins.operands[0].imm if ins.operands[0].type == ARM_OP_IMM else None
            edge(at, target, 'call' if target is not None else 'indirect_call')
            edge(at, at+4, 'call_continuation_overapproximation', True)
            continue
        if ins.id == ARM_INS_B:
            target = ins.operands[0].imm
            edge(at, target, 'branch', True)
            if conditional:
                edge(at, at+4, 'condition_false', True)
            continue
        if ins.id == ARM_INS_BX:
            edge(at, None, 'return' if ins.operands[0].reg == ARM_REG_LR else 'indirect_branch')
            if conditional:
                edge(at, at+4, 'condition_false', True)
            continue
        writes_pc = ARM_REG_PC in ins.regs_access()[1]
        if writes_pc:
            resolved = False
            # Only one certified idiom: CMP index,#count; LDRLO pc,[pc,index,LSL#2].
            # Every table slot must be a known internal absolute code relocation.
            if ins.id == ARM_INS_LDR and len(ins.operands) == 2 and ins.operands[1].type == ARM_OP_MEM:
                operand = ins.operands[1]
                previous = next(md.disasm(region(data, at-4, 4), at-4, count=1), None)
                if (ins.cc == ARM_CC_LO and operand.mem.base == ARM_REG_PC and operand.mem.index and
                    operand.mem.disp == 0 and operand.shift.type == ARM_SFT_LSL and operand.shift.value == 2 and
                    at-4 >= start and previous is not None and previous.id == ARM_INS_CMP and
                    previous.cc == ARM_CC_AL and len(previous.operands) == 2 and
                    previous.operands[0].type == ARM_OP_REG and previous.operands[0].reg == operand.mem.index and
                    previous.operands[1].type == ARM_OP_IMM and 0 < previous.operands[1].imm <= 256):
                    count = previous.operands[1].imm
                    entries = [links.get(at+8+i*4) for i in range(count)]
                    if all(r and r['kind'] == 2 and r['segment'] == 0 and r['reserved'] == 0 and
                           r['target'] is not None and r['target'] % 4 == 0 and
                           text <= r['target'] < text+size for r in entries):
                        for record in entries:
                            edge(at, record['target'], 'relocated_switch_case', True)
                        switch_guards[at] = at-4
                        resolved = True
            if not resolved:
                edge(at, None, 'return' if ins.mnemonic.startswith(('pop', 'ldm')) else 'unresolved_pc_write')
            if conditional:
                edge(at, at+4, 'condition_false', True)
            continue
        edge(at, at+4, 'fallthrough', True)
    # A neighboring CMP is insufficient if another reachable branch bypasses it.
    # Remove such inferred edges, then prune nodes reachable only through them.
    for at, guard in switch_guards.items():
        incoming = [e for e in edges if e['target'] == hex(at) and e['kind'] != 'call']
        if (at == start or guard not in nodes or not incoming or
            any(e['source'] != hex(guard) or e['kind'] != 'fallthrough' for e in incoming)):
            edges = [e for e in edges if not (e['source'] == hex(at) and e['kind'] == 'relocated_switch_case')]
            edge(at, None, 'unresolved_pc_write')
    reachable, work = set(), [start]
    successors = {}
    for e in edges:
        if e['inside_region'] and e['kind'] not in ('call', 'indirect_call'):
            successors.setdefault(int(e['source'], 16), []).append(int(e['target'], 16))
    while work:
        at = work.pop()
        if at in reachable:
            continue
        reachable.add(at)
        work.extend(successors.get(at, ()))
    nodes = {at: node for at, node in nodes.items() if at in reachable}
    edges = [e for e in edges if int(e['source'], 16) in reachable]
    backward = [e for e in edges if e['target'] and int(e['target'], 16) <= int(e['source'], 16) and
                e['kind'] in ('branch', 'relocated_switch_case')]
    return dict(start=hex(start), end_exclusive=hex(end), nodes=[nodes[k] for k in sorted(nodes)], edges=edges,
                back_edges=backward, calls_summarized=True, global_coverage=False,
                unresolved=[e for e in edges if e['kind'] in ('indirect_call', 'indirect_branch', 'unresolved_pc_write')])


def callback_records(data, links, start=0xd15c4, count=109):
    records = []
    region(data, start, count*12)
    for index in range(count):
        at = start+index*12
        callback = links.get(at+4)
        records.append(dict(record=hex(at), command_id=u32(data, at),
                            callback=hex(callback['target']) if callback and callback['target'] is not None else None,
                            relocation_kind=callback['kind'] if callback else None,
                            descriptor_adjustment=u32(data, at+8)))
    return records


def direct_references(data, targets):
    """Direct ARM B/BL candidates, including tail branches; not discovered roots."""
    cro = Cro(data)
    start, size, _ = cro.segments[0]
    result = []
    for at in range(start, start+size, 4):
        word = u32(data, at)
        if word >> 28 == 15 or word & 0x0e000000 != 0x0a000000:
            continue
        immediate = word & 0xffffff
        if immediate & 0x800000:
            immediate -= 0x1000000
        target = at+8+immediate*4
        if target in targets:
            result.append(dict(source=hex(at), target=hex(target),
                               kind='BL' if word & 0x01000000 else 'B', condition=word >> 28))
    return result


def possible_path(graph, source, target):
    edges = {}
    for edge in graph['edges']:
        if edge['inside_region'] and edge['kind'] not in ('call', 'indirect_call'):
            edges.setdefault(edge['source'], []).append(edge['target'])
    seen, todo = set(), [source]
    while todo:
        node = todo.pop()
        if node == target:
            return True
        if node not in seen:
            seen.add(node)
            todo.extend(edges.get(node, ()))
    return False


def report(root):
    profiles = json.loads(Path(__file__).with_name('observed_profiles.json').read_text(encoding='utf-8'))
    identity = inspect(root, profiles)
    require(identity['observation_matches'], 'Unrecognized research dependencies')
    data = (root/'ExtractedRomFS/DllBattle.cro').read_bytes()
    links = internal_links(data)
    emitter_format = links[0x6c2c4]
    reader_format = links[0x9eb70]
    require(emitter_format['kind'] == reader_format['kind'] == 2 and
            emitter_format['segment'] == reader_format['segment'] == 1 and
            emitter_format['target'] == reader_format['target'], 'Packet format tables do not agree')
    format_table = emitter_format['target']
    graphs = {name: bounded_cfg(data, *bounds, links=links) for name, bounds in REGIONS.items()}
    contexts = []
    for site, outer_sites, function in ((0x1a470, (0x1a470,), 0x19600),
                                       (0x1f4ec, (0x1a304,), 0x1f36c),
                                       (0x20e68, (0x1a4a8,), 0x20c9c),
                                       (0x20e7c, (0x1a4a8,), 0x20c9c),
                                       (0x6c1ec, (0x19948, 0x19ad0), 0x6c06c)):
        commands = []
        for index in range(63):
            link = links.get(0x196bc+index*4)
            if link and any(possible_path(graphs['script_dispatch'], hex(link['target']), hex(p)) for p in outer_sites):
                commands.append(index)
        contexts.append(dict(mutation_call=hex(site), containing_region=hex(function),
                             outer_calls=[hex(p) for p in outer_sites], possible_script_commands=commands))
    return dict(title_id=identity['title_id'], sha256=identity['sha256'], production_enabled=False,
                installable=False, graphs=graphs, mutation_call_contexts=contexts,
                direct_references=direct_references(data, {start for start, _ in REGIONS.values()}),
                relocation_references=[dict(slot=hex(slot), target=hex(record['target']),
                                             relocation_record=hex(record['record']), kind=record['kind'])
                                       for slot, record in links.items()
                                       if record['target'] in {start for start, _ in REGIONS.values()}],
                packet_format=dict(table=hex(format_table), commands={hex(i): region(data, format_table+i, 1)[0]
                                                                      for i in (0x19, 0x21)},
                                   emitter_literal='0x6c2c4', reader_literal='0x9eb70',
                                   same_runtime_queue_proven=False),
                copy_clear_call_candidates=direct_references(data, {0x414, 0x42c}),
                copy_clear_imports=Cro(data).static_links(Cro((root/'ExtractedRomFS/static.crs').read_bytes(), static=True),
                                                        {0x418, 0x430}),
                callback_records=callback_records(data, links), writer_candidates=write_inventory(data),
                limitations=['Calls summarized; possible side effects retained.',
                             'Unresolved indirect edges and pointer aliases prevent a complete write-coverage proof.',
                             'Linear store inventory may include literal data and unrelated structures.'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project', type=Path)
    args = parser.parse_args()
    print(json.dumps(report(args.project), indent=2))
