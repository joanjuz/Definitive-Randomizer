"""X-only experimental adaptation of native held-item reconciliation.

Exactly two ARM conditions plus existing CRO SHA-256 fields. No production
admission, snapshots, payload allocation, CRR table rewrite or RSA signing.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

from inspect_persistent_items import Cro, GATE, RESTORE, inspect, matches, region, require, sha, u32


WORDS = ((0x14854, 0x1a000020, 0x8a000020), (0x148ac, 0x0a000025, 0xea000025))
X_TITLE = '0004000000055D00'


def rebuild_hashes(data):
    Cro(data)
    code, code_size, content, content_size, readonly = [u32(data, at) for at in (0xb0, 0xb4, 0xb8, 0xbc, 0xc0)]
    require(content >= readonly, 'CRO regions out of order')
    output = bytearray(data)
    for slot, (start, length) in enumerate(((0x80, 0x100), (code, code_size),
                                          (readonly, content-readonly), (content, content_size))):
        require(slot == 0 or length == 0 or start >= 0x180, 'CRO content overlaps integrity header')
        output[slot*32:slot*32+32] = hashlib.sha256(region(data, start, length)).digest()
    require(Cro(output).hashes_valid(), 'CRO hash readback failed')
    return bytes(output)


def classify(data, original_sha256):
    require(Cro(data).hashes_valid(), 'Invalid CRO integrity')
    normalized = bytearray(data)
    states = []
    for offset, original, patched in WORDS:
        word = u32(data, offset)
        require(word in (original, patched), 'Unknown instruction at a patch point')
        states.append(word == patched)
        struct.pack_into('<I', normalized, offset, original)
    require(matches(normalized, GATE) == [0x14848] and matches(normalized, RESTORE) == [0x14888],
            'Missing, moved or duplicate Gen6 signatures')
    normalized = rebuild_hashes(normalized)
    require(sha(normalized) == original_sha256, 'Unrecognized baseline or external modification')
    return 'applied-pattern' if all(states) else 'original' if not any(states) else 'partial'


def patch(data, original_sha256):
    state = classify(data, original_sha256)
    require(state != 'partial', 'Partial experiment; refuse implicit repair')
    if state == 'applied-pattern':
        return bytes(data)
    output = bytearray(data)
    for offset, _, patched in WORDS:
        struct.pack_into('<I', output, offset, patched)
    output = rebuild_hashes(output)
    allowed = set(range(0x80)) | {i for at, _, _ in WORDS for i in range(at, at+4)}
    require(len(output) == len(data) and all(a == b or i in allowed for i, (a, b) in enumerate(zip(data, output))),
            'Unexpected change outside the two ARM words and CRO integrity fields')
    require(classify(output, original_sha256) == 'applied-pattern', 'Patched CRO readback failed')
    return output


def rollback(data, original_sha256):
    require(classify(data, original_sha256) == 'applied-pattern', 'No complete compatible experiment to reverse')
    output = bytearray(data)
    for offset, original, _ in WORDS:
        struct.pack_into('<I', output, offset, original)
    output = rebuild_hashes(output)
    require(sha(output) == original_sha256, 'Rollback differs from accredited baseline')
    return output


def prepare(project, output):
    from prepare_trace import no_links
    project, output = project.absolute(), output.absolute()
    no_links(project)
    no_links(output.parent)
    require(not output.exists(), 'Experimental output must be new')
    require(not output.is_relative_to(project) and not project.is_relative_to(output), 'Output overlaps input')
    profiles = json.loads(Path(__file__).with_name('observed_profiles.json').read_text(encoding='utf-8'))
    identity = inspect(project, profiles)
    require(identity['observation_matches'] and identity['title_id'] == X_TITLE, 'Only the observed X resource set is admitted experimentally')
    path = project/'ExtractedRomFS/DllBattle.cro'
    no_links(path)
    data = path.read_bytes()
    expected = identity['sha256']['ExtractedRomFS/DllBattle.cro']
    require(sha(data) == expected, 'Resource changed since inspection')
    proposed = patch(data, expected)
    require(rollback(proposed, expected) == data, 'Exact rollback failed')
    crr = (project/'ExtractedRomFS/.crr/static.crr').read_bytes()
    require(sha(crr) == identity['sha256']['ExtractedRomFS/.crr/static.crr'], 'CRR changed during inspection')
    manifest = dict(purpose='xy-persistent-items-two-gates-experiment-v1', title_id=X_TITLE,
                    production_enabled=False, gameplay_verified=False, hardware_loader_verified=False,
                    baseline_sha256=expected, patched_sha256=sha(proposed), source_dependencies=identity['sha256'],
                    words=[dict(file_offset=hex(at), original=hex(before), patched=hex(after),
                                branch_target=hex(at+8+((before & 0xffffff) if not before & 0x800000 else (before & 0xffffff)-0x1000000)*4))
                           for at, before, after in WORDS],
                    crr_unchanged=True, crr_candidate_registration_valid=False,
                    limitation='Experimental HLE-emulator candidate only; no valid CRR signature is asserted.')
    # All validation completes before creating a new output directory. No input writes.
    output.mkdir()
    (output/'original').mkdir()
    (output/'patched').mkdir()
    (output/'original/DllBattle.cro').write_bytes(data)
    (output/'patched/DllBattle.cro').write_bytes(proposed)
    (output/'experiment.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    require((output/'patched/DllBattle.cro').read_bytes() == proposed, 'Experimental file readback failed')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.project, args.output), indent=2))
