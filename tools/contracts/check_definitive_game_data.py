"""Contract checks for Definitive Game Data v1 and its optional playerLevelCaps block.

Usage:
    python tools/contracts/check_definitive_game_data.py [--tracker <NuzlockeTracker checkout>]
                                                         [--producer <folder of exported documents>]
                                                         [--consumer <NuzlockeTracker checkout with proposals>]

Validates the published examples against the JSON Schema, checks the playerLevelCaps
status rules with valid and invalid variants, applies the semantic rules the schema cannot
express, and with --tracker imports the examples through that Tracker's own DGD validator
and import service. With --producer it applies the same rules to documents written by the real
exporter (one per status, named after it). With --consumer and --producer it takes those documents
through the import service and the level-caps proposal flow of a Tracker that understands the block
(preview, accept, reject, keep, provenance, reopening, isolation and limits), and with --tracker it
also checks that the published Tracker opens the run folders that version writes. Reads only those
files; it writes nothing outside a temporary folder.
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


# Runs inside a separate interpreter with the published Tracker on sys.path: the two Tracker
# versions never share a process. Prints one JSON line with what that version sees in a run folder.
LEGACY_PROBE = r'''
import json, sys
sys.path.insert(0, sys.argv[1])
from tracker import __version__
from tracker.profiles import PROFILES
from tracker.progress import RunProgress, level_caps_view
from tracker.service import TrackerService
service = TrackerService(sys.argv[2])
game = sys.argv[3]
service.profile = PROFILES[game]
service.progress = RunProgress(service.output.with_name(service.output.stem + service.progress_suffix(game) + '-progress.json'))
service.update(game=game, stale=True)
service._refresh_game_data()
if len(sys.argv) > 4:
    preview = service.preview_game_data(open(sys.argv[4], encoding='utf-8').read(), game)
    service.apply_game_data(preview['previewToken'], game, True)
state = service.snapshot()
caps = state['level_caps']
print(json.dumps({'version': __version__, 'status': caps['status'], 'current': caps['current'], 'show': caps['show'],
                  'stages': [[s['name'], s['cap']] for s in caps['stages']],
                  'gameData': {k: state['game_data'][k] for k in ('loaded', 'active', 'fingerprint', 'error')},
                  'species': service.game_data.species(1, 0, game),
                  'warning': state.get('persist_warning')}))
'''


def check_consumer(tracker: Path, producer: Path, legacy: Path | None) -> None:
    """Real exporter documents through the import service and the proposal flow of a Tracker checkout.

    ``tracker`` must be a version that understands playerLevelCaps. ``legacy`` (optional) is the
    published Tracker 1.4.0: it must open the run folders the new version writes, and the new
    version must open the ones it wrote.
    """
    import subprocess
    sys.path.insert(0, str(tracker))
    from companion.snapshot import snapshot as soul_link_snapshot  # noqa: E402
    from tracker import __version__ as tracker_version  # noqa: E402
    from tracker.profiles import PROFILES  # noqa: E402
    from tracker.service import TrackerService  # noqa: E402
    print(f'Consumer under test: {tracker_version}')
    documents = {path.stem: path.read_text(encoding='utf-8') for path in sorted(producer.glob('*.json'))}
    installed = json.loads(documents['installed'])
    game = {'ultra-sun': 'Ultra Sun 1.0', 'ultra-moon': 'Ultra Moon 1.0'}[installed['game']['id']]
    other_game = 'Ultra Moon 1.0' if game == 'Ultra Sun 1.0' else 'Ultra Sun 1.0'
    block = installed['playerLevelCaps']

    def open_run(folder: Path, selected: str = game):
        service = TrackerService(folder / 'state.json')
        # What connect() does when the selected game changes, without an emulator.
        service.profile = PROFILES[selected]
        service.open_progress(service.progress_suffix(selected))
        service.update(game=selected, stale=True)
        service._refresh_game_data()
        return service

    def load(service, raw: str) -> None:
        preview = service.preview_game_data(raw, game)
        service.apply_game_data(preview['previewToken'], game, True)

    def command(service, action='level_caps_accept_proposal', **extra):
        state = service.snapshot()
        caps = state['game_data']['levelCaps'] or {}
        base = {'action': action, 'game': state['game'], 'dataFingerprint': state['game_data']['fingerprint'],
                'capsFingerprint': caps.get('fingerprint'), 'tableSha256': caps.get('tableSha256')}
        if action == 'level_caps_accept_proposal':
            base.update(finalStage=False, replace=False, listRevision=state['level_caps']['revision'])
        return {**base, **extra}

    def refused(service, cmd) -> bool:
        try:
            service.validate_level_caps(cmd)
        except ValueError:
            return True
        return False

    def legacy_view(folder: Path, selected: str = game, document: Path | None = None):
        result = subprocess.run([sys.executable, '-I', '-c', LEGACY_PROBE, str(legacy), str(folder / 'state.json'), selected,
                                 *([str(document)] if document else [])], capture_output=True, text=True, encoding='utf-8', timeout=120)
        if result.returncode != 0:
            print(result.stderr[-2000:])
            return None
        return json.loads(result.stdout.strip().splitlines()[-1])

    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        run_a = open_run(root / 'run-a')
        run_a.handle_command({'action': 'level_caps_show', 'show': False})
        before = run_a.snapshot()
        load(run_a, documents['installed'])
        state = run_a.snapshot()
        proposal = state['game_data']['levelCaps']
        # Preview: the real exporter document becomes a proposal and nothing else changes.
        check(proposal['state'] == 'proposal' and proposal['count'] == len(block['checkpoints'])
              and proposal['tableSha256'] == block['source']['tableSha256'] and proposal['fingerprint'] == block['source']['fingerprint']
              and state['game_data']['fingerprint'] == installed['source']['fingerprint'], 'Consumer_ProposalMatchesExportedBlock')
        check([(s['name'], s['cap']) for s in proposal['stages']] == [(p['label'], p['cap']) for p in block['checkpoints']],
              'Consumer_StageNamesAreCatalogLabels')
        check(state['level_caps'] == before['level_caps'] and state['level_caps_source']['accepted'] is None
              and state['progress'] == before['progress'], 'Consumer_ImportAloneChangesNoCaps')
        # Soul Link shares a live reading only: the same state, marked as a current one.
        def shared(snapshot):
            return soul_link_snapshot({**snapshot, 'stale': False, 'connection': {'status': 'connected'}})
        shared_before = shared(state)

        # Keep and reject: the list is untouched; only the decision is remembered.
        run_a.handle_command(command(run_a, 'level_caps_dismiss_proposal', dismissed=True))
        state = run_a.snapshot()
        check(state['level_caps'] == before['level_caps'] and state['level_caps_source']['dismissed']['tableSha256'] == proposal['tableSha256'],
              'Consumer_RejectKeepsCurrentCaps')
        run_a.handle_command(command(run_a, 'level_caps_dismiss_proposal', dismissed=False))
        check(run_a.snapshot()['level_caps_source'] == before['level_caps_source'], 'Consumer_RejectionCanBeUndone')

        # Accept: stages copied in order, no current stage, display option kept, provenance apart.
        run_a.handle_command(command(run_a))
        state = run_a.snapshot()
        caps, source = state['level_caps'], state['level_caps_source']
        check('last_command_error' not in state and [(s['name'], s['cap']) for s in caps['stages']] == [(p['label'], p['cap']) for p in block['checkpoints']]
              and caps['current'] is None and caps['current_cap'] is None and caps['show'] is False, 'Consumer_AcceptCopiesStagesWithoutCurrent')
        accepted = source['accepted'] or {}
        check(accepted.get('tableSha256') == block['source']['tableSha256'] and accepted.get('capsFingerprint') == block['source']['fingerprint']
              and accepted.get('dataFingerprint') == installed['source']['fingerprint'] and accepted.get('producer') == installed['producer']
              and accepted.get('game') == game and accepted.get('checkpoints') == len(block['checkpoints']) and accepted.get('finalStage') is False
              and source['matchesCurrent'], 'Consumer_ProvenanceRecordsFingerprints')
        stored = json.loads((root / 'run-a' / f'state{run_a.progress_suffix(game)}-progress.json').read_text(encoding='utf-8'))['level_caps']
        check(set(stored) == {'version', 'stages', 'current', 'show', 'updated_at', 'removed_current'} and stored['version'] == 1
              and all(set(s) == {'id', 'name', 'cap'} for s in stored['stages']), 'Consumer_LevelCapsBlockKeepsVersion1Shape')
        source_file = root / 'run-a' / f'state{run_a.progress_suffix(game)}-level-caps-source.json'
        check(source_file.is_file() and not any(privacy_findings(text) for text in strings(json.loads(source_file.read_text(encoding='utf-8')))),
              'Consumer_ProvenanceInSeparateFileWithoutPaths')
        after = dict(state['progress'])
        after.pop('level_caps', None)
        plain = dict(before['progress'])
        plain.pop('level_caps', None)
        check(after == plain and shared(state) == shared_before and 'level_caps' not in json.dumps(shared(state)), 'Consumer_DeathsEncountersAndSoulLinkPayloadUnchanged')
        current_species = run_a.game_data.species(1, 0, game)
        check(current_species is not None and current_species == next(r for r in installed['species'] if r['speciesId'] == 1 and r['formId'] == 0),
              'Consumer_GameDataLookupsUnchanged')

        # Close and reopen.
        run_a.flush_state(force=True)
        reopened = open_run(root / 'run-a')
        again = reopened.snapshot()
        check(again['level_caps']['stages'] == caps['stages'] and again['level_caps']['current'] is None
              and again['level_caps_source']['accepted'] == accepted and again['level_caps_source']['matchesCurrent']
              and again['game_data']['levelCaps']['state'] == 'proposal', 'Consumer_ReopenKeepsStagesProvenanceAndProposal')

        # Isolation between runs and between games of one run.
        run_b = open_run(root / 'run-b')
        isolated = run_b.snapshot()
        check(isolated['level_caps']['total'] == 0 and isolated['level_caps_source']['accepted'] is None
              and isolated['game_data']['levelCaps'] is None and refused(run_b, {**command(reopened, replace=True), 'listRevision': isolated['level_caps']['revision']}),
              'Consumer_OtherRunIsIsolated')
        other = open_run(root / 'run-a', other_game).snapshot()
        check(other['level_caps']['total'] == 0 and other['level_caps_source']['accepted'] is None and other['game_data']['levelCaps'] is None
              and other['game_data']['active'] is False, 'Consumer_OtherGameOfTheRunIsIsolated')

        # Replacement needs confirmation; the optional final stage is only added on request.
        check(refused(reopened, command(reopened)) and refused(reopened, command(reopened, replace=True, listRevision='0' * 16))
              and refused(reopened, command(reopened, replace=True, tableSha256='0' * 64)), 'Consumer_StaleOrUnconfirmedAcceptRefused')
        reopened.handle_command(command(reopened, replace=True, finalStage=True))
        final = reopened.snapshot()['level_caps']
        check(final['total'] == len(block['checkpoints']) + 1 and final['stages'][-1]['cap'] == 100 and final['current'] is None,
              'Consumer_FinalStageOnlyOnRequest')

        # Every status that is not installed, and a customised table with an unlabelled condition.
        for name, raw in documents.items():
            status = json.loads(raw)['playerLevelCaps']['status']
            run = open_run(root / ('status-' + name))
            load(run, raw)
            summary = run.snapshot()['game_data']['levelCaps']
            if status == 'installed':
                custom = json.loads(raw)['playerLevelCaps']['checkpoints']
                check(summary['state'] == 'proposal' and [s['cap'] for s in summary['stages']] == [p['cap'] for p in custom]
                      and all(('label' in p) == (c['label'] is not None) for p, c in zip(custom, summary['checkpoints'])),
                      f'Consumer_{name}_IsProposal')
            else:
                check(summary['state'] == 'unavailable' and summary['status'] == status and not summary['stages']
                      and refused(run, command(run, replace=True)) and run.snapshot()['level_caps']['total'] == 0,
                      f'Consumer_{name}_NoProposalAndAcceptRefused')

        # More than 64 checkpoints: a valid table for the Randomizer, refused whole by the Tracker.
        many = copy.deepcopy(installed)
        many['playerLevelCaps']['checkpoints'] = [{'order': i + 1, 'cap': min(100, 10 + i), 'condition': {'kind': 'event-flag-set', 'byte': i, 'mask': 1}}
                                                  for i in range(TRACKER_MAX_STAGES + 1)]
        many['playerLevelCaps']['source']['tableSha256'] = table_sha256(many['playerLevelCaps']['checkpoints'])
        run_many = open_run(root / 'run-many')
        load(run_many, json.dumps(many, ensure_ascii=False))
        rejected = run_many.snapshot()['game_data']['levelCaps']
        check(rejected['state'] == 'rejected' and not rejected['stages'] and rejected['count'] == TRACKER_MAX_STAGES + 1
              and refused(run_many, command(run_many)) and run_many.snapshot()['level_caps']['total'] == 0,
              'Consumer_MoreThan64RefusedNotTruncated')

        # Withdrawing the imported data keeps what was accepted.
        reopened.remove_game_data()
        kept = reopened.snapshot()
        check(kept['game_data']['loaded'] is False and kept['level_caps']['stages'] == final['stages']
              and kept['level_caps_source']['accepted']['checkpoints'] == len(block['checkpoints']), 'Consumer_WithdrawKeepsAcceptedStages')
        reopened.flush_state(force=True)

        if legacy:
            # The published Tracker opens the run folder the new version wrote (stages, display option, imported data).
            load(reopened, documents['installed'])
            reopened.flush_state(force=True)
            old = legacy_view(root / 'run-a')
            check(old is not None and old['status'] == 'ok' and old['stages'] == [[s['name'], s['cap']] for s in final['stages']]
                  and old['current'] is None and old['show'] is False and old['gameData']['loaded'] and old['gameData']['active']
                  and old['gameData']['error'] is None and old['gameData']['fingerprint'] == installed['source']['fingerprint']
                  and old['species'] == current_species, f"Legacy_{'' if old is None else old['version']}_OpensRunWrittenByNewTracker")
            # And a folder written by the published Tracker (block stored verbatim) opens as a proposal in the new one.
            document = root / 'installed.json'
            document.write_text(documents['installed'], encoding='utf-8')
            written = legacy_view(root / 'run-legacy', document=document)
            upgraded = open_run(root / 'run-legacy').snapshot()
            check(written is not None and written['gameData']['loaded'] and upgraded['game_data']['levelCaps']['state'] == 'proposal'
                  and upgraded['game_data']['levelCaps']['tableSha256'] == block['source']['tableSha256']
                  and upgraded['level_caps']['total'] == 0, 'Legacy_RunWrittenByPublishedTrackerOpensAsProposal')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--tracker', type=Path, help='checkout of NuzlockeTracker whose DGD importer is exercised')
    parser.add_argument('--producer', type=Path, help='folder of documents written by the exporter, one per status')
    parser.add_argument('--consumer', type=Path, help='checkout of a NuzlockeTracker that offers playerLevelCaps as a proposal')
    parser.add_argument('--consumer-only', action='store_true', help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.consumer and not args.producer:
        parser.error('--consumer needs --producer')
    if args.consumer_only:
        check_consumer(args.consumer.resolve(), args.producer.resolve(), args.tracker.resolve() if args.tracker else None)
        print(f'Completed {passed} consumer checks; {len(failures)} failed.')
        for name in failures:
            print('  failed: ' + name)
        return 1 if failures else 0

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
    if args.consumer:
        # Two Tracker versions cannot share an interpreter (same package name): the consumer runs in its own.
        import subprocess
        child = subprocess.run([sys.executable, '-I', str(Path(__file__).resolve()), '--consumer-only',
                                '--producer', str(args.producer.resolve()), '--consumer', str(args.consumer.resolve()),
                                *(['--tracker', str(args.tracker.resolve())] if args.tracker else [])])
        check(child.returncode == 0, 'Consumer_ChecksPassedInTheirOwnInterpreter')

    print(f'Completed {passed} Definitive Game Data contract checks; {len(failures)} failed.')
    for name in failures:
        print('  failed: ' + name)
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
