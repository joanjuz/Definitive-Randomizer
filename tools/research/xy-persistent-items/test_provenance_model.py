"""Synthetic reference-model requirements; no native semantics or ROM fixtures."""
import copy
import dataclasses
import unittest

import provenance_model as model


A = model.PokemonSnapshot('player', 'pokemon-A', 0, 158)
B = model.PokemonSnapshot('player', 'pokemon-B', 1, 275)
C = model.PokemonSnapshot('player', 'pokemon-C', 2, 214)
CONTEXT = model.BattleContext(model.X_TITLE, 1, 0, model.X_TRAINER_EVIDENCE)


def own(pokemon=A, admitted=True, item=None, caller=None):
    return model.BattleEvent(1, model.EventKind.OWN_CONSUMPTION, pokemon.ref,
                            pokemon.item if item is None else item,
                            semantic_admitted=admitted, caller=caller)


def sequence(*events):
    return tuple(dataclasses.replace(event, sequence=i) for i, event in enumerate(events, 1))


def run(entries=(A,), events=None, native=None, context=CONTEXT, snapshot=True,
        events_complete=True, reconciliation_complete=True):
    if events is None:
        events = tuple(own(pokemon) for pokemon in entries)
        events = sequence(*events)
    if native is None:
        native = tuple(model.NativeHeldItem(pokemon.ref, 0) for pokemon in entries)
    pre = model.PreActionsSnapshot(entries, True) if snapshot is True else snapshot
    return model.evaluate(context, pre, events, native, events_complete=events_complete,
                          reconciliation_complete=reconciliation_complete)


class ReferenceModelTests(unittest.TestCase):
    def test_admitted_original_consumption_can_only_propose_a_disabled_logical_restore(self):
        result = run()
        self.assertEqual(result.proposals, (model.LogicalRestore(A, 158),))
        self.assertEqual(result.decisions, (model.PokemonDecision(A, 0, 158, ()),))
        self.assertFalse(result.installable)
        self.assertFalse(result.production_enabled)
        self.assertFalse(result.payload_present)
        self.assertTrue(any('CRR' in blocker for blocker in result.production_blockers))
        self.assertTrue(any('unclassified' in blocker for blocker in result.production_blockers))
        with self.assertRaises(TypeError):
            model.ModelResult(installable=True)

    def test_pre_snapshot_and_decisions_are_frozen_and_evaluation_never_mutates_inputs(self):
        snapshot = model.PreActionsSnapshot((A,), True)
        events = (own(),)
        native = (model.NativeHeldItem(A.ref, 0),)
        before = copy.deepcopy((CONTEXT, snapshot, events, native))
        result = model.evaluate(CONTEXT, snapshot, events, native, events_complete=True,
                                reconciliation_complete=True)
        self.assertEqual((CONTEXT, snapshot, events, native), before)
        for obj, field, value in ((A, 'item', 275), (A.ref, 'owner', 'other'),
                                  (snapshot, 'entries', (B,)), (result, 'installable', True)):
            with self.subTest(field=field), self.assertRaises(dataclasses.FrozenInstanceError):
                setattr(obj, field, value)
        with self.assertRaises(TypeError):
            model.TITLE_POLICIES[model.Y_TITLE] = model.TITLE_POLICIES[model.X_TITLE]

    def test_x_and_y_policies_are_independent_and_wild_unknown_modes_have_no_proposal(self):
        self.assertIsNone(model.TITLE_POLICIES[model.Y_TITLE].admitted_model_mode)
        for context in (dataclasses.replace(CONTEXT, title_id=model.Y_TITLE),
                        dataclasses.replace(CONTEXT, title_id='unknown'),
                        dataclasses.replace(CONTEXT, mode=0), dataclasses.replace(CONTEXT, mode=2),
                        dataclasses.replace(CONTEXT, mode=3), dataclasses.replace(CONTEXT, mode=None),
                        dataclasses.replace(CONTEXT, mode_flag_4b=1),
                        dataclasses.replace(CONTEXT, mode=True), dataclasses.replace(CONTEXT, mode_flag_4b=False)):
            with self.subTest(context=context):
                result = run(context=context)
                self.assertEqual(result.proposals, ())
                self.assertTrue(result.rejections)
                self.assertFalse(result.installable)

    def test_mode_requires_both_explicit_observations_and_no_invented_evidence(self):
        for evidence in (frozenset(), frozenset(('X/07',)), frozenset(('X/08',)),
                         frozenset(('X/07', 'X/08', 'wild-unverified')), ('X/07', 'X/08')):
            with self.subTest(evidence=evidence):
                self.assertEqual(run(context=dataclasses.replace(CONTEXT, mode_evidence=evidence)).proposals, ())

    def test_two_independent_pokemon_can_receive_independent_logical_proposals(self):
        result = run(entries=(A, B))
        self.assertEqual(result.proposals, (model.LogicalRestore(A, 158), model.LogicalRestore(B, 275)))
        self.assertEqual([decision.native_item for decision in result.decisions], [0, 0])
        self.assertFalse(result.installable)

    def test_final_nonzero_native_item_is_never_overwritten_even_after_admitted_consumption(self):
        for native_item in (158, 275, 639):
            with self.subTest(native_item=native_item):
                result = run(native=(model.NativeHeldItem(A.ref, native_item),))
                self.assertEqual(result.proposals, ())
                self.assertEqual(result.decisions[0].native_item, native_item)
                self.assertIsNone(result.decisions[0].proposed_item)

    def test_caller_alone_does_not_admit_consumption_or_reclassify_an_unknown_write(self):
        for event in (own(admitted=False, caller=0x6c1f0),
                      model.BattleEvent(1, model.EventKind.UNKNOWN_WRITE, A.ref, 158,
                                        semantic_admitted=True, caller=0x6c1f0)):
            with self.subTest(event=event):
                self.assertEqual(run(events=(event,)).proposals, ())
        self.assertEqual(run(events=(own(admitted=True, caller=0x123456),)).proposals,
                         (model.LogicalRestore(A, 158),))

    def test_original_item_mismatch_missing_consumption_and_original_empty_slot_fail_closed(self):
        for arguments in (dict(events=(own(item=275),)), dict(events=()),
                          dict(entries=(dataclasses.replace(A, item=0),)),
                          dict(events=(dataclasses.replace(own(), item=0),))):
            with self.subTest(arguments=arguments):
                self.assertEqual(run(**arguments).proposals, ())

    def test_fling_knockoff_unknown_write_are_sticky_before_or_after_own_consumption(self):
        for kind in (model.EventKind.FLING, model.EventKind.KNOCK_OFF, model.EventKind.UNKNOWN_WRITE):
            invalid = model.BattleEvent(1, kind, A.ref, 158)
            for events in (sequence(invalid, own(), own(B)), sequence(own(), invalid, own(B))):
                with self.subTest(kind=kind, events=events):
                    result = run(entries=(A, B), events=events)
                    self.assertEqual(result.proposals, (model.LogicalRestore(B, 275),))
                    self.assertIsNone(result.decisions[0].proposed_item)

    def test_transfer_and_theft_invalidate_both_endpoints_but_not_an_independent_pokemon(self):
        for kind in (model.EventKind.TRANSFER, model.EventKind.THEFT):
            for first, second in ((A, B), (B, A)):
                invalid = model.BattleEvent(1, kind, first.ref, first.item, target=second.ref)
                result = run(entries=(A, B, C), events=sequence(own(A), invalid, own(B), own(C)))
                with self.subTest(kind=kind, origin=first.identity):
                    self.assertEqual(result.proposals, (model.LogicalRestore(C, 214),))
                    self.assertTrue(result.decisions[0].reasons)
                    self.assertTrue(result.decisions[1].reasons)

    def test_transfer_to_or_from_an_external_participant_still_invalidates_the_tracked_owner(self):
        external = model.PokemonRef('opponent', 'external-pokemon', 0)
        for actor, target in ((A.ref, external), (external, A.ref)):
            invalid = model.BattleEvent(1, model.EventKind.THEFT, actor, 158, target)
            result = run(entries=(A, B), events=sequence(invalid, own(), own(B)))
            self.assertEqual(result.proposals, (model.LogicalRestore(B, 275),))

    def test_incomplete_noncontiguous_unknown_events_or_missing_native_reconciliation_reject_globally(self):
        for arguments in (dict(events_complete=False), dict(reconciliation_complete=False),
                          dict(events=(dataclasses.replace(own(), sequence=2),)),
                          dict(events=(own(), own())), dict(events=(dataclasses.replace(own(), kind='unknown'),)),
                          dict(events=[own()]), dict(native=())):
            with self.subTest(arguments=arguments):
                result = run(**arguments)
                self.assertEqual(result.proposals, ())
                self.assertTrue(result.rejections)

    def test_no_post_or_mutable_snapshot_and_ambiguous_identity_or_slot_never_authorize(self):
        snapshots = (None, model.PreActionsSnapshot((A,), False), model.PreActionsSnapshot([A], True),
                     model.PreActionsSnapshot((), True), model.PreActionsSnapshot((A, A), True),
                     model.PreActionsSnapshot((A, dataclasses.replace(B, identity=A.identity)), True),
                     model.PreActionsSnapshot((A, dataclasses.replace(B, slot=A.slot)), True))
        for snapshot in snapshots:
            with self.subTest(snapshot=snapshot):
                result = run(snapshot=snapshot)
                self.assertEqual(result.proposals, ())
                self.assertTrue(result.rejections)

    def test_changed_owner_slot_or_unknown_final_identity_rejects_without_inventing_provenance(self):
        for altered in (dataclasses.replace(A.ref, owner='new-owner'),
                        dataclasses.replace(A.ref, slot=3), model.PokemonRef('player', 'unknown', 0)):
            with self.subTest(altered=altered):
                self.assertEqual(run(native=(model.NativeHeldItem(altered, 0),)).proposals, ())
                self.assertEqual(run(events=(dataclasses.replace(own(), actor=altered),)).proposals, ())
        self.assertEqual(run(native=(model.NativeHeldItem(A.ref, 0), model.NativeHeldItem(A.ref, 0))).proposals, ())

    def test_duplicate_consumption_and_destinationless_transfer_remain_excluded(self):
        for events in (sequence(own(), own()),
                       (model.BattleEvent(1, model.EventKind.TRANSFER, A.ref, 158),),
                       (model.BattleEvent(1, model.EventKind.TRANSFER, A.ref, 158, A.ref),),
                       (dataclasses.replace(own(), target=B.ref),)):
            with self.subTest(events=events):
                self.assertEqual(run(events=events).proposals, ())

    def test_invalid_item_slot_and_native_scalar_types_fail_closed(self):
        for snapshot in (model.PreActionsSnapshot((dataclasses.replace(A, item=-1),), True),
                         model.PreActionsSnapshot((dataclasses.replace(A, item=65536),), True),
                         model.PreActionsSnapshot((dataclasses.replace(A, slot=True),), True),
                         model.PreActionsSnapshot((dataclasses.replace(A, identity=''),), True)):
            with self.subTest(snapshot=snapshot):
                self.assertEqual(run(snapshot=snapshot).proposals, ())
        for native_item in (-1, 65536, False, None):
            with self.subTest(native_item=native_item):
                self.assertEqual(run(native=(model.NativeHeldItem(A.ref, native_item),)).proposals, ())


if __name__ == '__main__':
    unittest.main()
