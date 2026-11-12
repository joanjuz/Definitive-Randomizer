"""Contract checks for Definitive Game Data v1 and its optional playerLevelCaps block.

Usage:
    python tools/contracts/check_definitive_game_data.py [--tracker <NuzlockeTracker checkout>]
                                                         [--producer <folder of exported documents>]

Validates the published examples against the JSON Schema, checks the playerLevelCaps
status rules with valid and invalid variants, applies the semantic rules the schema cannot
express, and with --tracker imports the examples through that Tracker's own DGD validator
and import service. With --producer it applies the same rules to documents written by the real
exporter (one per status, named after it). Reads only those files; it writes nothing outside a
temporary folder.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import struct
import sys
import tempfile
from pathlib import Path

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[2]
CONTRACTS = ROOT / 'docs' / 'contracts'
SCHEMA = CONTRACTS / 'definitive-game-data-v1.schema.json'
ORIGINAL = CONTRACTS / 'examples' / 'definitive-game-data-v1.synthetic.json'
EXTENDED = CONTRACTS / 'examples' / 'definitive-game-data-v1.player-level-caps.synthetic.json'

ROOT_REQUIRED = ['format', 'schemaVersion', 'producer', 'game', 'source', 'coverage',
                 'species', 'types', 'abilities', 'moves', 'evolutions']
ROOT_SCOPE = 'personal,moves,evolutions,gametext'
TRACKER_MAX_STAGES = 64
BLOCK_MAX_BYTES = 64 * 1024

failures: list[str] = []
passed = 0


def check(condition: bool, name: str) -> None:
    global passed
    if condition:
        passed += 1
        print('PASS: ' + name)
    else:
        failures.append(name)
        print('FAIL: ' + name)


def load(path: Path):
    return json.loads(path.read_text(encoding='utf-8'))


def table_sha256(checkpoints) -> str:
    """SHA-256 of the runtime table: 6 bytes per checkpoint plus the Lv100 terminator."""
    data = bytearray()
    for point in checkpoints:
        condition = point['condition']
        if condition['kind'] == 'event-flag-set':
            data += struct.pack('<BBHH', 0, point['cap'], condition['byte'], condition['mask'])
        else:
            data += struct.pack('<BBHH', 1, point['cap'], condition['index'], condition['minimum'])
    data += bytes([0xFF, 100, 0, 0, 0, 0])
    return hashlib.sha256(bytes(data)).hexdigest().upper()


def semantic_problems(block) -> list[str]:
    """Rules of block version 1 that JSON Schema cannot express. Only meaningful for 'installed'."""
    problems = []
    points = block.get('checkpoints', [])
    if [p['order'] for p in points] != list(range(1, len(points) + 1)):
        problems.append('order must be 1..n in array order')
    caps = [p['cap'] for p in points]
    if any(b < a for a, b in zip(caps, caps[1:])):
        problems.append('caps must never decrease')
    keys = [tuple(sorted(p['condition'].items())) for p in points]
    if len(set(keys)) != len(keys):
        problems.append('each condition appears once')
    if block['source'].get('tableSha256') != table_sha256(points):
        problems.append('tableSha256 does not match the checkpoints')
    if len(json.dumps(block, ensure_ascii=False, separators=(',', ':')).encode('utf-8')) > BLOCK_MAX_BYTES:
        problems.append('block larger than 64 KiB')
    return problems


def proposal_allowed(block) -> bool:
    """What a consumer may offer as a stage proposal: never truncated, never from another status."""
    return (block.get('version') == 1 and block['status'] == 'installed' and not semantic_problems(block)
            and len(block['checkpoints']) <= TRACKER_MAX_STAGES)


def privacy_findings(text: str) -> list[str]:
    patterns = {
        'drive path': r'(?<![A-Za-z])[A-Za-z]:[\\/]',
        'UNC path': r'\\\\[^\\"]+\\',
        'home path': r'/(?:Users|home)/',
        'long encoded blob': r'[A-Za-z0-9+/=]{120,}',
    }
    return [name for name, pattern in patterns.items() if re.search(pattern, text)]


def strings(value):
    """Every key and string value of a parsed document."""
    if isinstance(value, dict):
        for key, item in value.items():
            yield key
            yield from strings(item)
    elif isinstance(value, list):
        for item in value:
            yield from strings(item)
    elif isinstance(value, str):
        yield value


def variants(extended):
    """(name, document, expected schema validity)."""
    base = copy.deepcopy(extended)
    installed = base['playerLevelCaps']
    fingerprint = installed['source']['fingerprint']

    def block(**fields):
        doc = copy.deepcopy(base)
        doc['playerLevelCaps'] = fields
        return doc

    def edited(change):
        doc = copy.deepcopy(base)
        change(doc['playerLevelCaps'])
        return doc

    src = {'fingerprintAlgorithm': 'SHA-256', 'scope': 'battle.cro,code.bin', 'fingerprint': fingerprint}
    yield 'NotDetected_StockHooks', block(version=1, status='not-detected', hooks={'battleCro': 'stock', 'codeBin': 'stock'}, source=src), True
    yield 'Unsupported_MissingFileWithoutSource', block(version=1, status='unsupported', hooks={'battleCro': 'applied', 'codeBin': 'missing'}, detail='code.bin is missing'), True
    yield 'Unsupported_ForeignHookWithSource', block(version=1, status='unsupported', hooks={'battleCro': 'unrecognized', 'codeBin': 'stock'}, source=src), True
    yield 'Inconsistent_OneCopyApplied', block(version=1, status='inconsistent', hooks={'battleCro': 'applied', 'codeBin': 'stock'}, source=src), True
    yield 'Inconsistent_DifferentTables', block(version=1, status='inconsistent', hooks={'battleCro': 'applied', 'codeBin': 'applied'}, source=src, detail='the two tables differ'), True
    yield 'LegacyFormat_DiagnosticOnly', block(version=1, status='legacy-format', hooks={'battleCro': 'applied-legacy', 'codeBin': 'applied-legacy'}, source=src, detail='v1 3-byte table'), True
    yield 'FutureBlockVersionIgnoredBySchema', block(version=2, anything={'new': True}), True

    for status, hooks in (('not-detected', ('stock', 'stock')), ('inconsistent', ('applied', 'stock')),
                          ('legacy-format', ('applied-legacy', 'applied-legacy')), ('unsupported', ('missing', 'stock'))):
        doc = block(version=1, status=status, hooks={'battleCro': hooks[0], 'codeBin': hooks[1]}, source=src,
                    checkpoints=copy.deepcopy(installed['checkpoints']))
        yield 'Rejected_' + status + '_WithCheckpoints', doc, False
        doc = block(version=1, status=status, hooks={'battleCro': hooks[0], 'codeBin': hooks[1]}, source=dict(src, tableSha256=installed['source']['tableSha256']))
        yield 'Rejected_' + status + '_WithTableHash', doc, False
        for field, value in (('evaluation', 'ordered-first-incomplete'), ('enforcement', ['exp-gain', 'rare-candy']), ('ceiling', 100)):
            doc = block(version=1, status=status, hooks={'battleCro': hooks[0], 'codeBin': hooks[1]}, source=src, **{field: value})
            yield 'Rejected_' + status + '_With_' + field, doc, False
    for status in ('not-detected', 'inconsistent', 'legacy-format'):
        hooks = {'not-detected': ('stock', 'stock'), 'inconsistent': ('applied', 'stock'), 'legacy-format': ('applied-legacy', 'stock')}[status]
        yield 'Rejected_' + status + '_WithoutSource', block(version=1, status=status, hooks={'battleCro': hooks[0], 'codeBin': hooks[1]}), False
    yield 'Rejected_NotDetected_WithAppliedHook', block(version=1, status='not-detected', hooks={'battleCro': 'applied', 'codeBin': 'stock'}, source=src), False
    yield 'Rejected_Inconsistent_BothStock', block(version=1, status='inconsistent', hooks={'battleCro': 'stock', 'codeBin': 'stock'}, source=src), False
    yield 'Rejected_Inconsistent_WithLegacyHook', block(version=1, status='inconsistent', hooks={'battleCro': 'applied-legacy', 'codeBin': 'applied'}, source=src), False
    yield 'Rejected_Legacy_WithoutLegacyHook', block(version=1, status='legacy-format', hooks={'battleCro': 'applied', 'codeBin': 'stock'}, source=src), False
    yield 'Rejected_Legacy_WithMissingFile', block(version=1, status='legacy-format', hooks={'battleCro': 'applied-legacy', 'codeBin': 'missing'}, source=src), False
    yield 'Rejected_Unsupported_BothApplied', block(version=1, status='unsupported', hooks={'battleCro': 'applied', 'codeBin': 'applied'}), False

    def remove(field):
        return lambda b: b.pop(field)
    for field in ('status', 'hooks', 'evaluation', 'enforcement', 'ceiling', 'source', 'checkpoints'):
        yield 'Rejected_Installed_Without_' + field, edited(remove(field)), False
    yield 'Rejected_Installed_WithoutTableHash', edited(lambda b: b['source'].pop('tableSha256')), False
    yield 'Rejected_Installed_StockHook', edited(lambda b: b['hooks'].update(codeBin='stock')), False
    yield 'Rejected_Installed_LegacyHook', edited(lambda b: b['hooks'].update(battleCro='applied-legacy')), False
    yield 'Rejected_Installed_EmptyCheckpoints', edited(lambda b: b.update(checkpoints=[])), False
    yield 'Rejected_Installed_129Checkpoints', edited(lambda b: b.update(checkpoints=[dict(b['checkpoints'][0], order=i + 1) for i in range(129)])), False
    yield 'Rejected_EnforcementExpOnly', edited(lambda b: b.update(enforcement=['exp-gain'])), False
    yield 'Rejected_EnforcementReordered', edited(lambda b: b.update(enforcement=['rare-candy', 'exp-gain'])), False
    yield 'Rejected_Ceiling99', edited(lambda b: b.update(ceiling=99)), False
    yield 'Rejected_OtherEvaluation', edited(lambda b: b.update(evaluation='any-complete')), False
    yield 'Rejected_UnknownStatus', edited(lambda b: b.update(status='partial')), False
    yield 'Rejected_UnknownHookState', edited(lambda b: b['hooks'].update(battleCro='patched')), False
    yield 'Rejected_ExtraHook', edited(lambda b: b['hooks'].update(staticCrr='stock')), False
    yield 'Rejected_ExtraBlockField', edited(lambda b: b.update(currentCheckpoint=1)), False
    yield 'Rejected_VersionString', edited(lambda b: b.update(version='1')), False
    yield 'Rejected_VersionZero', edited(lambda b: b.update(version=0)), False
    yield 'Rejected_BlockNotObject', block_value([1]), False
    yield 'Rejected_DetailEmpty', edited(lambda b: b.update(detail='')), False
    yield 'Rejected_DetailTooLong', edited(lambda b: b.update(detail='x' * 501)), False
    yield 'Rejected_SourceLowercaseHex', edited(lambda b: b['source'].update(fingerprint='a' * 64)), False
    yield 'Rejected_SourceShortHash', edited(lambda b: b['source'].update(tableSha256='A' * 63)), False
    yield 'Rejected_SourceOtherScope', edited(lambda b: b['source'].update(scope='battle.cro')), False
    yield 'Rejected_SourceOtherAlgorithm', edited(lambda b: b['source'].update(fingerprintAlgorithm='SHA-1')), False
    yield 'Rejected_SourceExtraField', edited(lambda b: b['source'].update(path='project')), False

    def point(change):
        return edited(lambda b: change(b['checkpoints'][0]))
    def cond(change):
        return point(lambda p: change(p['condition']))
    yield 'Rejected_Cap0', point(lambda p: p.update(cap=0)), False
    yield 'Rejected_Cap101', point(lambda p: p.update(cap=101)), False
    yield 'Rejected_CapBoolean', point(lambda p: p.update(cap=True)), False
    yield 'Rejected_CapFraction', point(lambda p: p.update(cap=14.5)), False
    yield 'Rejected_Order0', point(lambda p: p.update(order=0)), False
    yield 'Rejected_Order129', point(lambda p: p.update(order=129)), False
    yield 'Rejected_CheckpointWithoutCondition', point(lambda p: p.pop('condition')), False
    yield 'Rejected_CheckpointExtraField', point(lambda p: p.update(reached=True)), False
    yield 'Rejected_FlagByte620', cond(lambda c: c.update(byte=620)), False
    yield 'Rejected_FlagByteNegative', cond(lambda c: c.update(byte=-1)), False
    yield 'Rejected_FlagMask0', cond(lambda c: c.update(mask=0)), False
    yield 'Rejected_FlagMask256', cond(lambda c: c.update(mask=256)), False
    yield 'Rejected_FlagWithWorkFields', cond(lambda c: c.update(index=1)), False
    yield 'Rejected_UnknownConditionKind', cond(lambda c: c.update(kind='event-flag-clear')), False
    yield 'Rejected_FlagWithoutMask', cond(lambda c: c.pop('mask')), False
    work = lambda change: edited(lambda b: change(b['checkpoints'][4]['condition']))
    yield 'Rejected_WorkIndex1000', work(lambda c: c.update(index=1000)), False
    yield 'Rejected_WorkMinimum0', work(lambda c: c.update(minimum=0)), False
    yield 'Rejected_WorkMinimum65536', work(lambda c: c.update(minimum=65536)), False
    yield 'Rejected_WorkWithFlagFields', work(lambda c: c.update(mask=1)), False
    yield 'Rejected_LabelWithoutSource', point(lambda p: p.pop('labelSource')), False
    yield 'Rejected_SourceWithoutLabel', point(lambda p: p.pop('label')), False
    yield 'Rejected_OtherLabelSource', point(lambda p: p.update(labelSource='game-text')), False
    yield 'Rejected_Label61', point(lambda p: p.update(label='x' * 61)), False
    yield 'Rejected_LabelEmpty', point(lambda p: p.update(label='')), False
    yield 'Rejected_LabelControlChar', point(lambda p: p.update(label='Ilima\nTrial')), False
    yield 'Rejected_LabelEdgeSpace', point(lambda p: p.update(label=' Ilima')), False


def block_value(value):
    doc = load(EXTENDED)
    doc['playerLevelCaps'] = value
    return doc


def check_tracker(tracker: Path, original, extended) -> None:
    sys.path.insert(0, str(tracker))
    from tracker import __version__ as tracker_version  # noqa: E402
    from tracker.game_data import GameDataManager, validate_document  # noqa: E402
    print(f'Tracker under test: {tracker_version}')
    raw_original = ORIGINAL.read_text(encoding='utf-8')
    raw_extended = EXTENDED.read_text(encoding='utf-8')
    old = validate_document(raw_original)
    new = validate_document(raw_extended)
    check('playerLevelCaps' not in old and old == original, 'Tracker_AcceptsOriginalUnchanged')
    stripped = {k: v for k, v in new.items() if k != 'playerLevelCaps'}
    check(stripped == old and new['playerLevelCaps'] == extended['playerLevelCaps'], 'Tracker_AcceptsExtendedAndKeepsBlockIntact')
    for name, doc, valid in variants(extended):
        if not valid or name.startswith('Future'):
            continue
        validate_document(json.dumps(doc, ensure_ascii=False))
    validate_document(json.dumps(block_value({'version': 2, 'anything': True})))
    check(True, 'Tracker_AcceptsEveryValidStatusAndFutureBlockVersion')
    game = 'Ultra Sun 1.0'
    with tempfile.TemporaryDirectory() as folder:
        statuses = {}
        for label, raw in (('original', raw_original), ('extended', raw_extended)):
            path = Path(folder) / label / 'definitive-game-data.json'
            manager = GameDataManager(path)
            preview = manager.preview(raw, game, lambda doc: [])
            status = manager.apply(preview['previewToken'], game, acknowledge_unknown=True)
            reopened = GameDataManager(path)
            statuses[label] = (preview, status, reopened.status(game),
                               reopened.species(1, 0, game), reopened.move(1, game), reopened.ability_name(1, game))
            if label == 'extended':
                stored = json.loads(path.read_text(encoding='utf-8'))
                check(stored.get('playerLevelCaps') == extended['playerLevelCaps'] and reopened.error is None,
                      'Tracker_StoresBlockVerbatimAndReopensCleanly')
                reopened.remove(game)
                check(not path.exists() and reopened.status(game)['loaded'] is False, 'Tracker_RemoveStillWorksWithBlock')
        o, e = statuses['original'], statuses['extended']
        same_preview = {k: v for k, v in o[0].items() if k != 'previewToken'} == {k: v for k, v in e[0].items() if k != 'previewToken'}
        check(same_preview and o[1] == e[1] and o[2] == e[2] and o[3:] == e[3:],
              'Tracker_PreviewStatusAndLookupsIdenticalWithBlock')
        check(e[2]['fingerprint'] == original['source']['fingerprint'], 'Tracker_RootFingerprintUnchanged')


STATUSES = ('installed', 'not-detected', 'unsupported', 'inconsistent', 'legacy-format')


def check_producer(folder: Path, validator, tracker: Path | None) -> None:
    """Documents written by the real exporter: '<status>.json' or '<status>-<variant>.json'."""
    validate_document = None
    if tracker:
        sys.path.insert(0, str(tracker))
        from tracker.game_data import validate_document  # noqa: E402
    seen, roots = set(), set()
    for path in sorted(folder.glob('*.json')):
        name = path.stem
        expected = next(s for s in sorted(STATUSES, key=len, reverse=True) if name == s or name.startswith(s + '-'))
        raw = path.read_text(encoding='utf-8')
        doc = json.loads(raw)
        block = doc.get('playerLevelCaps')
        check(not list(validator.iter_errors(doc)), f'Producer_{name}_SchemaValid')
        check(isinstance(block, dict) and block.get('version') == 1 and block.get('status') == expected, f'Producer_{name}_Status')
        if not isinstance(block, dict):
            continue
        seen.add(block.get('status'))
        roots.add(json.dumps({k: v for k, v in doc.items() if k != 'playerLevelCaps'}, sort_keys=True))
        check(doc['source']['scope'] == ROOT_SCOPE and doc['schemaVersion'] == 1, f'Producer_{name}_RootScopeAndVersion')
        check(len(json.dumps(block, ensure_ascii=False, separators=(',', ':')).encode('utf-8')) <= BLOCK_MAX_BYTES,
              f'Producer_{name}_BlockWithinLimit')
        check(not any(privacy_findings(text) for text in strings(block)), f'Producer_{name}_NoLocalPaths')
        if expected == 'installed':
            check(not semantic_problems(block) and proposal_allowed(block), f'Producer_{name}_ConsumerRulesHold')
        else:
            check('checkpoints' not in block and not proposal_allowed(block), f'Producer_{name}_NoUsableList')
        if validate_document:
            accepted = validate_document(raw)
            check(accepted.get('playerLevelCaps') == block, f'Producer_{name}_AcceptedByTracker')
    check(seen == set(STATUSES), 'Producer_EveryStatusExported')
    check(len(roots) == 1, 'Producer_RootDocumentIndependentOfPatchState')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--tracker', type=Path, help='checkout of NuzlockeTracker whose DGD importer is exercised')
    parser.add_argument('--producer', type=Path, help='folder of documents written by the exporter, one per status')
    args = parser.parse_args()

    schema = load(SCHEMA)
    Draft202012Validator.check_schema(schema)
    validator = Draft202012Validator(schema)
    original, extended = load(ORIGINAL), load(EXTENDED)

    check(schema['required'] == ROOT_REQUIRED and 'playerLevelCaps' not in schema['required'], 'Schema_RootRequiredUnchanged')
    check(schema['properties']['source']['properties']['scope'] == {'const': ROOT_SCOPE}, 'Schema_RootScopeUnchanged')
    check('additionalProperties' not in schema, 'Schema_RootStillAcceptsUnknownOptionalFields')
    check(not list(validator.iter_errors(original)), 'Schema_OriginalExampleValid')
    check(not list(validator.iter_errors(extended)), 'Schema_ExtendedExampleValid')
    stripped = {k: v for k, v in extended.items() if k != 'playerLevelCaps'}
    check(stripped == original, 'Example_ExtendedEqualsOriginalPlusBlock')
    check(extended['source'] == original['source'] and extended['source']['scope'] == ROOT_SCOPE, 'Example_RootSourceAndFingerprintIntact')

    block = extended['playerLevelCaps']
    check(not semantic_problems(block), 'Semantic_ExtendedExampleConsistent')
    check(table_sha256(block['checkpoints']) == block['source']['tableSha256'], 'Semantic_TableHashRecomputable')
    check(any('label' not in p for p in block['checkpoints']), 'Example_ShowsUnlabelledCondition')
    check(proposal_allowed(block), 'Proposal_InstalledExampleAllowed')

    for name, doc, valid in variants(extended):
        errors = list(validator.iter_errors(doc))
        check((not errors) == valid, 'Schema_' + name)
        if valid and isinstance(doc.get('playerLevelCaps'), dict) and doc['playerLevelCaps'].get('version') == 1:
            check(doc['playerLevelCaps']['status'] == 'installed' or not proposal_allowed(doc['playerLevelCaps']),
                  'Proposal_None_' + name)

    def semantic(change, expected, name):
        doc = copy.deepcopy(block)
        change(doc)
        check(not list(validator.iter_errors(dict(extended, playerLevelCaps=doc))), 'SchemaAccepts_' + name)
        check(expected in semantic_problems(doc) and not proposal_allowed(doc), 'Semantic_' + name)
    semantic(lambda b: b['checkpoints'][2].update(cap=10), 'caps must never decrease', 'DecreasingCapRejected')
    semantic(lambda b: b['checkpoints'][2].update(order=7), 'order must be 1..n in array order', 'OrderGapRejected')
    semantic(lambda b: b['checkpoints'][1].update(condition=copy.deepcopy(b['checkpoints'][0]['condition'])),
             'each condition appears once', 'DuplicateConditionRejected')
    semantic(lambda b: b['source'].update(tableSha256='0' * 64), 'tableSha256 does not match the checkpoints', 'StaleTableHashRejected')

    many = copy.deepcopy(block)
    many['checkpoints'] = [{'order': i + 1, 'cap': min(100, 10 + i), 'condition': {'kind': 'event-flag-set', 'byte': i, 'mask': 1}}
                           for i in range(TRACKER_MAX_STAGES + 1)]
    many['source']['tableSha256'] = table_sha256(many['checkpoints'])
    check(not list(validator.iter_errors(dict(extended, playerLevelCaps=many))) and not semantic_problems(many),
          'Schema_65CheckpointsValidForRandomizer')
    check(not proposal_allowed(many), 'Proposal_MoreThan64RejectedNotTruncated')
    full = copy.deepcopy(many)
    full['checkpoints'] = [{'order': i + 1, 'cap': 100, 'condition': {'kind': 'event-work-at-least', 'index': i, 'minimum': 65535},
                            'label': 'x' * 60, 'labelSource': 'randomizer-catalog'} for i in range(128)]
    full['source']['tableSha256'] = table_sha256(full['checkpoints'])
    check(not semantic_problems(full), 'Size_128LongestCheckpointsFitBlockLimit')

    samples = ['C:\\Users\\player\\rom', 'D:/Progra/project', '\\\\server\\share\\rom', '/home/player/rom', 'QUJD' * 40]
    check(all(privacy_findings(text) for text in samples) and not privacy_findings('https://json-schema.org/draft/2020-12/schema'),
          'Privacy_DetectorSelfTest')
    for path, doc in ((ORIGINAL, original), (EXTENDED, extended)):
        check(not any(privacy_findings(text) for text in strings(doc)), 'Privacy_' + path.name)

    if args.tracker:
        check_tracker(args.tracker.resolve(), original, extended)
    if args.producer:
        check_producer(args.producer.resolve(), validator, args.tracker.resolve() if args.tracker else None)

    print(f'Completed {passed} Definitive Game Data contract checks; {len(failures)} failed.')
    for name in failures:
        print('  failed: ' + name)
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
