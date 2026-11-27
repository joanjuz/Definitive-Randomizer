"""Offline XY provenance research. Reads traces/resources; never writes a patch.

Candidates are observations consistent with a hypothesis, not approved restores.
Static stores may target other structures or literal pools; no reachability claim.
"""
import argparse
import json
import re
import struct
from pathlib import Path

from inspect_persistent_items import Cro, inspect, region, require
from gdb_trace import POINTS, ITEM_POINTS


BLOCKERS = ['Direct item writes outside the traced mutation routine remain unclassified.',
            'A pre-action ownership snapshot is not supplied by this tracer.',
            'Transfers, ownership changes and all consumable paths are not accredited.',
            'Excluded battle modes and loader/CRR policy remain unverified.',
            'No native provenance payload or Gen6 installer is implemented.']


def audit_instructions(data):
    """Inventory aligned ARM encodings in the declared code segment, not a CFG."""
    cro = Cro(data)
    start, size, _ = cro.segments[0]
    code = region(data, start, size)
    require(start % 4 == 0 and size % 4 == 0, 'Unaligned ARM code segment')
    calls, stores = [], []
    for index in range(0, size, 4):
        word = struct.unpack_from('<I', code, index)[0]
        at = start+index
        condition = word >> 28
        if condition == 15:
            continue  # Do not interpret unconditional extension encodings as ARM BL/STRH.
        if word & 0x0f000000 == 0x0b000000:
            immediate = word & 0xffffff
            if immediate & 0x800000:
                immediate -= 0x1000000
            if at+8+immediate*4 == 0x6b140:
                calls.append(dict(file_offset=hex(at), return_file_offset=hex(at+4), condition=condition))
        # STRH immediate, pre-index, no writeback. Other forms are deliberately
        # not claimed covered. Exclude SP destinations from this focused inventory.
        if word & 0x0f7000f0 == 0x014000b0:
            displacement = ((word >> 4) & 0xf0) | (word & 15)
            if not word & (1 << 23):
                displacement = -displacement
            base, source = (word >> 16) & 15, (word >> 12) & 15
            if displacement in (0x12, 0x14) and base != 13:
                stores.append(dict(file_offset=hex(at), base_register=f'r{base}',
                                   source_register=f'r{source}', displacement=hex(displacement),
                                   condition=condition))
    return dict(direct_call_candidates=calls, immediate_halfword_store_candidates=stores,
                complete_write_coverage=False, reachability_verified=False)


def _one(events, predicate, label):
    found = [e for e in events if predicate(e)]
    require(len(found) == 1, 'Missing or duplicate '+label)
    return found[0]


def _party(event):
    party = event['parties']['after_candidate']
    members = party['members']
    require(0 < party['count'] <= 6 and len(members) == party['count'], 'Invalid party count')
    result, tags, indexes = {}, set(), set()
    for m in members:
        fields = m['native_fields']
        require(fields['checksum_valid'] is True and not m.get('decode_error'), 'Unverified Pokemon data')
        require(type(fields['species']) is int and 1 <= fields['species'] <= 721 and
                type(fields['held_item']) is int and 0 <= fields['held_item'] <= 65535 and
                isinstance(fields['identity_tag'], str) and re.fullmatch('[0-9A-F]{16}', fields['identity_tag']) and
                type(m['index']) is int and 0 <= m['index'] < party['count'] and m['index'] not in indexes,
                'Invalid Pokemon identity or item fields')
        core = m['core']
        require(core not in result and fields['identity_tag'] not in tags, 'Ambiguous party identity')
        result[core] = dict(index=m['index'], **fields)
        tags.add(fields['identity_tag'])
        indexes.add(m['index'])
    return result


def _mutation_raw(event):
    raw = bytes.fromhex(event['opaque_parameter_hex'])
    require(len(raw) == 0x16b, 'Incomplete battle parameter bytes')
    values = dict(item_12=struct.unpack_from('<H', raw, 0x12)[0],
                  item_14=struct.unpack_from('<H', raw, 0x14)[0], participant_id_19=raw[0x19],
                  flags_167=raw[0x167], flags_16a=raw[0x16a])
    require(all(type(event.get(k)) is int and event[k] == v for k, v in values.items()),
            'Raw battle parameter disagrees with reported fields')
    return hex(struct.unpack_from('<I', raw)[0])


def analyze_events(events):
    """Validate one captured battle and compare a narrow hypothesis without writes."""
    require(isinstance(events, list) and all(isinstance(e, dict) for e in events), 'Invalid trace events')
    session = _one(events, lambda e: e.get('event') == 'session', 'session')
    require(session.get('installable') is False and session.get('item_events_requested') is True,
            'Not an original-game item-events capture')
    instrumented = _one(events, lambda e: e.get('event') == 'instrumented', 'instrumentation')
    require(instrumented.get('points') == [hex(p) for p in POINTS+ITEM_POINTS], 'Incomplete instrumentation')
    cleanup = _one(events, lambda e: e.get('event') == 'cleanup', 'cleanup')
    returned = _one(events, lambda e: e.get('event') == 'control_returned', 'continuation')
    reconciled = _one(events, lambda e: e.get('event') == 'native_reconciliation_returned', 'native return')
    require(cleanup.get('all_owned_instructions_restored') is True and returned.get('continue_acknowledged') is True,
            'Unverified cleanup or continuation')
    require(not any(e.get('structure_error') or e.get('event') == 'cleanup_failed' for e in events), 'Trace contains errors')
    before = _one(events, lambda e: e.get('file_offset') == '0x1945c', 'reconciliation entry')
    after = _one(events, lambda e: e.get('file_offset') == '0x19460', 'reconciliation return')
    require(before.get('event') == 'breakpoint' and after.get('event') == 'breakpoint', 'Invalid reconciliation event kind')
    require(events.index(session) < events.index(instrumented) < events.index(before) < events.index(after)
            < events.index(reconciled) < events.index(cleanup) < events.index(returned), 'Invalid capture order')
    first_party, last_party = _party(before), _party(after)
    require(first_party.keys() == last_party.keys(), 'Party pointers changed during reconciliation')
    for core, member in first_party.items():
        require(member['identity_tag'] == last_party[core]['identity_tag'] and
                member['species'] == last_party[core]['species'] and
                member['index'] == last_party[core]['index'], 'Pokemon identity changed')
    mode_allowed = (session.get('title_id') == '0004000000055D00' and
                    all(e.get('mode_byte') == 1 and e.get('mode_flag_4b') == 0
                        for e in events if e.get('event') == 'breakpoint'))
    observations = []
    mutations = [e for e in events if e.get('event') == 'item_mutation_candidate']
    require(len(mutations) % 3 == 0, 'Incomplete item mutation triple')
    touched = {}
    for index in range(0, len(mutations), 3):
        entry, stored, remembered = mutations[index:index+3]
        require([e.get('file_offset') for e in (entry, stored, remembered)] == [hex(p) for p in ITEM_POINTS],
                'Unexpected item mutation sequence')
        require(all(events.index(instrumented) < events.index(e) < events.index(before)
                    for e in (entry, stored, remembered)), 'Item mutation outside battle capture')
        pointer = entry['battle_parameter_pointer']
        require(all(e['battle_parameter_pointer'] == pointer for e in (stored, remembered)), 'Mutation pointer changed')
        core = _mutation_raw(entry)
        require(all(_mutation_raw(e) == core for e in (stored, remembered)), 'Mutation CoreParam changed')
        touched[core] = touched.get(core, 0)+1
        member = first_party.get(core)
        consistent = (entry['item_12'] == 158 and entry['item_14'] == 0 and entry['requested_item'] == 0
                      and entry['remember_argument'] == 1 and stored['item_12'] == 0 and stored['item_14'] == 0
                      and remembered['item_12'] == 0 and remembered['item_14'] == 158
                      and all(e['participant_id_19'] == entry['participant_id_19'] for e in (stored, remembered)))
        eligible = bool(mode_allowed and member and member['held_item'] == 158 and
                        last_party[core]['held_item'] == 0 and consistent and
                        entry['return_file_offset_candidate'] == '0x6c1f0')
        observations.append(dict(core=core, party_index=member['index'] if member else None,
                                 caller_return=entry['return_file_offset_candidate'],
                                 hypothesis_candidate=eligible,
                                 reason='Observed own Sitrus activation route' if eligible else
                                 'Unaccredited mode, caller, ownership or mutation; excluded'))
    for observation in observations:
        if touched[observation['core']] != 1:
            observation.update(hypothesis_candidate=False, reason='Multiple mutations of same CoreParam; excluded')
    return dict(title_id=session.get('title_id'), case=session.get('case'), installable=False,
                production_enabled=False, native_payload_present=False, observations=observations,
                blockers=list(BLOCKERS))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True, help='Prepared stock extraction, read only')
    parser.add_argument('--trace', type=Path, required=True, help='Private completed item-events JSONL')
    args = parser.parse_args()
    profiles = json.loads(Path(__file__).with_name('observed_profiles.json').read_text(encoding='utf-8'))
    identity = inspect(args.project, profiles)
    require(identity['observation_matches'], 'Unrecognized research dependencies')
    events = [json.loads(line) for line in args.trace.read_text(encoding='utf-8').splitlines()]
    result = analyze_events(events)
    require(result['title_id'] == identity['title_id'], 'Trace/project title mismatch')
    result['static_inventory'] = audit_instructions((args.project/'ExtractedRomFS/DllBattle.cro').read_bytes())
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
