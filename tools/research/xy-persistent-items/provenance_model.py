"""Pure reference model for a future XY held-item provenance design.

Inputs are synthetic semantic assertions, NOT classifications inferred from ARM
callers or current traces. ``semantic_admitted=True`` means a caller supplied
independent proof of OWN_CONSUMPTION for this particular event. A native caller
address alone provides no such proof. No current tracing routine creates that
proof automatically.

The immutable PRE-actions snapshot is mandatory. Only the symbolic X/07 and
X/08 trainer-mode observations admit the model's mode guard; X wild mode and Y
have separate, unaccredited policies. This is NOT production accreditation.

Proposals are logical comparisons against completed native reconciliation;
they never overwrite a nonzero native result and never contain a payload.
This module performs no I/O and exposes no installer or binary writer.
"""
from dataclasses import dataclass, field
from enum import Enum
from types import MappingProxyType


X_TITLE = '0004000000055D00'
Y_TITLE = '0004000000055E00'
X_TRAINER_EVIDENCE = frozenset(('X/07', 'X/08'))


@dataclass(frozen=True)
class TitlePolicy:
    name: str
    admitted_model_mode: tuple[int, int] | None
    required_evidence: frozenset[str]


TITLE_POLICIES = MappingProxyType({
    X_TITLE: TitlePolicy('Pokémon X', (1, 0), X_TRAINER_EVIDENCE),
    Y_TITLE: TitlePolicy('Pokémon Y', None, frozenset()),
})


@dataclass(frozen=True)
class PokemonRef:
    owner: str
    identity: str
    slot: int


@dataclass(frozen=True)
class PokemonSnapshot:
    owner: str
    identity: str
    slot: int
    item: int

    @property
    def ref(self):
        return PokemonRef(self.owner, self.identity, self.slot)


@dataclass(frozen=True)
class PreActionsSnapshot:
    entries: tuple[PokemonSnapshot, ...]
    captured_before_actions: bool


@dataclass(frozen=True)
class BattleContext:
    title_id: str
    mode: int | None
    mode_flag_4b: int | None
    mode_evidence: frozenset[str]


class EventKind(Enum):
    OWN_CONSUMPTION = 'own-consumption'
    FLING = 'fling'
    TRANSFER = 'transfer'
    THEFT = 'theft'
    KNOCK_OFF = 'knock-off'
    UNKNOWN_WRITE = 'unknown-write'


@dataclass(frozen=True)
class BattleEvent:
    sequence: int
    kind: EventKind
    actor: PokemonRef
    item: int | None = None
    target: PokemonRef | None = None
    semantic_admitted: bool = False
    caller: int | None = None


@dataclass(frozen=True)
class NativeHeldItem:
    pokemon: PokemonRef
    item: int


@dataclass(frozen=True)
class LogicalRestore:
    original: PokemonSnapshot
    item: int


@dataclass(frozen=True)
class PokemonDecision:
    original: PokemonSnapshot
    native_item: int | None
    proposed_item: int | None
    reasons: tuple[str, ...]


PRODUCTION_BLOCKERS = (
    'Semantic consumption and ownership evidence has no accredited native implementation.',
    'Direct item stores and unobserved mutations remain unclassified.',
    'Wild, excluded battle modes and Pokémon Y remain unaccredited.',
    'Loader/CRR policy and a reversible native payload remain unimplemented.',
)


@dataclass(frozen=True)
class ModelResult:
    proposals: tuple[LogicalRestore, ...] = ()
    decisions: tuple[PokemonDecision, ...] = ()
    rejections: tuple[str, ...] = ()
    production_blockers: tuple[str, ...] = PRODUCTION_BLOCKERS
    installable: bool = field(default=False, init=False)
    production_enabled: bool = field(default=False, init=False)
    payload_present: bool = field(default=False, init=False)


def _item(value):
    return type(value) is int and 0 <= value <= 65535


def _ref(value):
    return (isinstance(value, PokemonRef) and isinstance(value.owner, str) and bool(value.owner)
            and isinstance(value.identity, str) and bool(value.identity)
            and type(value.slot) is int and 0 <= value.slot < 6)


def evaluate(context, snapshot, events, native_final, *, events_complete, reconciliation_complete):
    """Compare explicit semantic evidence; return disabled logical proposals.

    Events must form the complete contiguous sequence 1..N. Unknown provenance
    invalidates a tracked Pokémon permanently for this battle. Transfers and
    theft invalidate both endpoints; an explicit external endpoint may be absent
    from our PRE snapshot, but at least one endpoint must be tracked. Ambiguous
    identity, owner or slot correlations reject the entire evaluation.
    """
    rejected = []
    if not isinstance(context, BattleContext):
        return ModelResult(rejections=('Missing battle context.',))
    policy = TITLE_POLICIES.get(context.title_id) if isinstance(context.title_id, str) else None
    if policy is None or policy.admitted_model_mode is None:
        rejected.append('Title has no admitted reference-model mode.')
    elif (type(context.mode) is not int or type(context.mode_flag_4b) is not int
          or (context.mode, context.mode_flag_4b) != policy.admitted_model_mode
          or not isinstance(context.mode_evidence, frozenset)
          or context.mode_evidence != policy.required_evidence):
        rejected.append('Battle mode or its independent evidence is unaccredited.')
    if events_complete is not True or reconciliation_complete is not True:
        rejected.append('Event sequence or native reconciliation is incomplete.')
    if (not isinstance(snapshot, PreActionsSnapshot) or snapshot.captured_before_actions is not True
            or type(snapshot.entries) is not tuple or not 1 <= len(snapshot.entries) <= 6):
        return ModelResult(rejections=tuple(rejected)+('Missing immutable PRE-actions snapshot.',))
    tracked, identities, slots = {}, set(), set()
    for original in snapshot.entries:
        if not isinstance(original, PokemonSnapshot) or not _ref(original.ref) or not _item(original.item):
            return ModelResult(rejections=tuple(rejected)+('Invalid original owner, identity, slot or item.',))
        if original.identity in identities or (original.owner, original.slot) in slots:
            return ModelResult(rejections=tuple(rejected)+('Ambiguous PRE-actions identity or slot.',))
        identities.add(original.identity)
        slots.add((original.owner, original.slot))
        tracked[original.ref] = original
    if type(events) is not tuple or type(native_final) is not tuple:
        return ModelResult(rejections=tuple(rejected)+('Events and native results must be immutable tuples.',))
    final = {}
    for native in native_final:
        if (not isinstance(native, NativeHeldItem) or not _ref(native.pokemon) or not _item(native.item)
                or native.pokemon not in tracked or native.pokemon in final):
            return ModelResult(rejections=tuple(rejected)+('Ambiguous or unknown final native identity/item.',))
        final[native.pokemon] = native.item
    if final.keys() != tracked.keys():
        rejected.append('Final native party does not match the original snapshot.')
    invalid = {key: [] for key in tracked}
    consumed = set()

    def invalidate(keys, reason):
        for key in keys:
            if reason not in invalid[key]:
                invalid[key].append(reason)

    for expected_sequence, event in enumerate(events, 1):
        if (not isinstance(event, BattleEvent) or type(event.sequence) is not int
                or event.sequence != expected_sequence or not isinstance(event.kind, EventKind)
                or not _ref(event.actor) or (event.target is not None and not _ref(event.target))):
            rejected.append('Unknown, incomplete or noncontiguous semantic event.')
            break
        refs = (event.actor,) if event.target is None else (event.actor, event.target)
        # A familiar identity under a changed owner/slot is ambiguous, even if a
        # second independent Pokémon has an otherwise valid consumption event.
        if any(ref.identity in identities and ref not in tracked for ref in refs):
            rejected.append('Event ownership/slot differs from the original snapshot.')
            break
        affected = tuple(ref for ref in refs if ref in tracked)
        if not affected:
            rejected.append('Event cannot be correlated with an original participant.')
            break
        if event.kind in (EventKind.TRANSFER, EventKind.THEFT):
            if event.target is None or event.target == event.actor:
                rejected.append('Transfer/theft has no distinct destination.')
                break
            invalidate(affected, 'Transfer or theft invalidates original-item provenance.')
        elif event.kind is not EventKind.OWN_CONSUMPTION:
            invalidate(affected, 'Fling, knock-off or unknown mutation invalidates provenance.')
        else:
            original = tracked.get(event.actor)
            if (original is None or event.target is not None or event.semantic_admitted is not True
                    or not _item(event.item) or event.item == 0 or event.item != original.item):
                invalidate(affected, 'Own-consumption evidence does not prove the original item and owner.')
            elif event.actor in consumed:
                invalidate(affected, 'Multiple consumption events require unaccredited intervening provenance.')
            else:
                consumed.add(event.actor)
    proposals, decisions = [], []
    for key, original in tracked.items():
        reasons = list(invalid[key])
        if rejected:
            reasons.append('Global evidence rejected.')
        if key not in consumed:
            reasons.append('No independently admitted original-item consumption.')
        native_item = final.get(key)
        if native_item is None:
            reasons.append('Final native item is absent.')
        elif native_item != 0:
            reasons.append('Native reconciliation kept an item; never overwrite it.')
        proposed = original.item if not reasons else None
        if proposed is not None:
            proposals.append(LogicalRestore(original, proposed))
        decisions.append(PokemonDecision(original, native_item, proposed, tuple(reasons)))
    return ModelResult(tuple(proposals), tuple(decisions), tuple(rejected))
