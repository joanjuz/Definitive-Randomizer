using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Border = System.Windows.Controls.Border;
using Point = System.Windows.Point;

/// <summary>Structural NPC operations: record construction, NARC rebuild, checkpoint v2, planner policy and a read-only real-ROM probe.</summary>
internal static class NdsNpcStructureChecks
{
    private static void Reject<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); } catch (T) { check(true, name); return; }
        throw new Exception("FAIL expected " + typeof(T).Name + ": " + name);
    }
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Replace(byte[] bytes, int at, byte[] with) { var copy = (byte[])bytes.Clone(); with.CopyTo(copy, at); return copy; }
    private static PlatinumEvent FirstNpc(PlatinumEvents data, int map) => data.Events(map).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0);
    private static byte[] Record(PlatinumEvents data, int map, int id, int x, int y, int direction = 2, int gfx = 33, int movement = 0) =>
        PlatinumEvents.NewNpcRecord(FirstNpc(data, map), id, gfx, movement, direction, x, y);

    internal static void Run(Action<bool, string> check)
    {
        var f = NdsMapCanvasChecks.Make(); var data = f.Read(); byte[] originalBytes = data.Bytes(); string originalHash = Sha(originalBytes);

        // ---- record construction
        var template = FirstNpc(data, 1);
        byte[] rec = PlatinumEvents.NewNpcRecord(template, 40, 33, 0, 3, 120, 878); byte[] tpl = Convert.FromHexString(template.Raw);
        check(rec.Length == 32 && BinaryPrimitives.ReadUInt16LittleEndian(rec) == 40 && BinaryPrimitives.ReadInt16LittleEndian(rec.AsSpan(24)) == 120 && BinaryPrimitives.ReadInt16LittleEndian(rec.AsSpan(26)) == 878 && BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(12)) == 3, "NpcStructure_NewRecordCarriesTheChosenFields");
        check(BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(6)) == 0 && BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(8)) == 0 && BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(10)) == 0 && template.Script == 1, "NpcStructure_ScriptFlagAndTypeAreNeverCopiedFromTheTemplate");
        check(rec.AsSpan(14, 10).SequenceEqual(tpl.AsSpan(14, 10)) && rec.AsSpan(28, 4).SequenceEqual(tpl.AsSpan(28, 4)), "NpcStructure_UnknownBytesAreCopiedFromTheTemplate");
        Reject<ArgumentException>(() => PlatinumEvents.NewNpcRecord(data.Events(1).First(e => e.Type == 3), 40, 33, 0, 0, 1, 1), check, "NpcStructure_GroundObjectIsNotATemplate");
        Reject<ArgumentException>(() => PlatinumEvents.NewNpcRecord(data.Events(1).First(e => e.Kind == PlatinumEventKind.Warp), 40, 33, 0, 0, 1, 1), check, "NpcStructure_WarpIsNotATemplate");

        // ---- structural validation
        check(data.ValidateNewNpc(1, rec).Count == 0, "NpcStructure_ValidRecordHasNoProblems");
        bool Has(byte[] r, string text) => data.ValidateNewNpc(1, r).Any(p => p.Contains(text));
        check(Has(Record(data, 1, 20, 120, 878), "ID 20 ya lo usa") && Has(Record(data, 1, 0xFFFF, 120, 878), "reservado"), "NpcStructure_DuplicateAndReservedIdsAreRejected");
        check(Has(Record(data, 1, 40, 120, 878, gfx: 999), "gráfico 999") && Has(Record(data, 1, 40, 120, 878, movement: 77), "movimiento 77"), "NpcStructure_OnlyAccreditedGraphicsAndMovementsAreOffered");
        check(Has(Replace(rec, 6, [3, 0]), "tipo 0") && Has(Replace(rec, 10, [5, 0]), "script ni flag") && Has(Replace(rec, 8, [9, 0]), "script ni flag") && Has(Replace(rec, 12, [4, 0]), "dirección"), "NpcStructure_TypeScriptFlagAndDirectionRulesHold");
        check(Has(Record(data, 1, 40, 960, 878), "límites") && Has(Record(data, 1, 40, 120, 960), "límites") && data.ValidateNewNpc(1, new byte[31]).Count == 1, "NpcStructure_OutOfBoundsAndWrongLengthAreRejected");
        check(data.AccreditedGraphics.SequenceEqual([33]) && data.AccreditedMovements.SequenceEqual([0]) && PlatinumEvents.MaxVanillaNpcCount == 51, "NpcStructure_AccreditedSetsAndCapComeFromTheData");

        // ---- NARC rebuild
        var added = data.AddNpc(1, rec); var newBytes = added.Bytes();
        var oldFiles = Narc.Read(originalBytes).Files; var newFiles = Narc.Read(newBytes).Files;
        check(added.NpcCount(1) == data.NpcCount(1) + 1 && data.NpcCount(1) == 4 && data.Bytes().SequenceEqual(originalBytes), "NpcStructure_AddReturnsANewInstanceAndLeavesTheSourceAlone");
        check(newFiles.Count == oldFiles.Count && Enumerable.Range(0, oldFiles.Count).Where(i => i != 1).All(i => oldFiles[i].AsSpan().SequenceEqual(newFiles[i])) && newFiles[1].Length == oldFiles[1].Length + 32, "NpcStructure_OtherEventFilesAreByteIdentical");
        check(added.Events(1).Where(e => e.Kind == PlatinumEventKind.Npc).Last() is { Index: 4, Id: 40, X: 120, Y: 878 } && added.Events(1).Count == data.Events(1).Count + 1 && added.Events(0).SequenceEqual(data.Events(0)) && added.Events(2).SequenceEqual(data.Events(2)), "NpcStructure_NewNpcIsAppendedAndOthersKeepTheirIndices");
        check(Enumerable.Range(0, 4).All(i => added.Npc(1, i) == data.Npc(1, i)) && added.Events(1).Where(e => e.Kind != PlatinumEventKind.Npc).SequenceEqual(data.Events(1).Where(e => e.Kind != PlatinumEventKind.Npc)), "NpcStructure_ExistingRecordsAndOtherBlocksAreUntouched");
        check(newBytes.Length % 4 == 0 && Narc.IsNarc(newBytes) && added.Maps.SequenceEqual(data.Maps), "NpcStructure_RebuiltArchiveIsAlignedAndHeadersAreUnchanged");
        var moved = added.WithEvents(added.ReplaceNpc(1, 4, PlatinumEvents.Values(added.Npc(1, 4)), new(121, 878, 1)));
        check(moved.Npc(1, 4) is { X: 121, Direction: 1 } && moved.NpcCount(1) == 5, "NpcStructure_AMoveCanFollowAnAddition");
        Reject<InvalidOperationException>(() => data.AddNpc(1, Record(data, 1, 20, 1, 1)), check, "NpcStructure_AddRejectsDuplicateIds");
        var many = data; for (int i = 0; i < PlatinumEvents.MaxVanillaNpcCount - 4; i++) many = many.AddNpc(1, Record(many, 1, 100 + i, 10 + i, 10));
        check(many.NpcCount(1) == 51 && many.Events(1).Where(e => e.Kind == PlatinumEventKind.Npc).Select(e => e.Id).Distinct().Count() == 51, "NpcStructure_AddsUpToTheObservedMaximum");
        Reject<InvalidOperationException>(() => many.AddNpc(1, Record(many, 1, 999, 1, 1)), check, "NpcStructure_NoNpcBeyondTheObservedMaximum");

        var shared = f with { Headers = (byte[])f.Headers.Clone() }; BinaryPrimitives.WriteUInt16LittleEndian(shared.Headers.AsSpan(2 * 24 + 16), 1);
        var sharedData = shared.Read(); var sharedAdded = sharedData.AddNpc(1, Record(sharedData, 1, 40, 5, 5));
        check(sharedAdded.NpcCount(1) == 5 && sharedAdded.NpcCount(2) == 5 && sharedAdded.Events(2).SequenceEqual(sharedAdded.Events(1)) && sharedData.Bounds(1) == (64, 96), "NpcStructure_SharedEventFileGrowsForEveryHeader");

        // ---- sessions and checkpoints
        string temp = Path.Combine(Path.GetTempPath(), "nds-npc-structure-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = Sha(File.ReadAllBytes(source));
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read());
            string cp1 = Path.Combine(temp, "add.ndsevents.json"), cp2 = Path.Combine(temp, "undo.ndsevents.json");
            var proposal = session.PreviewAdd(1, rec);
            check(proposal.IsStructural && !proposal.Undo && proposal.Changes.Count == 1 && proposal.Changes[0].Field == "Cantidad de NPC" && proposal.Changes[0] is { Before: 4, After: 5 } && proposal.Lines![1].Contains("ID 40") && proposal.Lines.Any(l => l.Contains("script 0")), "NpcStructure_PreviewShowsTheCountAndTheNewRecord");
            check(session.Revision == 0 && session.Data.Bytes().SequenceEqual(originalBytes) && !session.CanUndo && !File.Exists(cp1) && session.Replacement() is null, "NpcStructure_PreviewWritesNothing");
            session.Save(proposal, cp1); string text = File.ReadAllText(cp1);
            var doc = NdsEventSession.ReadDocument(cp1);
            check(doc.Version == 2 && doc.Edits.Count == 0 && doc.Operations!.Count == 1 && doc.Operations[0] is { Kind: "AddNpc", Move: null, Add: { Map: 1, Index: 4 } } && text.Contains("AddNpc") && doc.SourceSha256 == sourceHash, "NpcStructure_CheckpointV2RecordsTheAddition");
            check(session.Revision == 1 && session.Data.NpcCount(1) == 5 && session.Replacement() is { } replacement && replacement.Length >= originalBytes.Length && session.HasStructuralOperations && session.Audit.Single().StartsWith("Añadir mapa 1"), "NpcStructure_SaveCommitsTheDerivedDataAndTheAudit");
            check(session.Data.Bytes().SequenceEqual(added.Bytes()), "NpcStructure_ReplayIsDeterministic");

            // Export through the real NDS writer, which must handle the larger member
            var entry = rom.File(PlatinumEvents.EventPath); string built = Path.Combine(temp, "built.nds");
            rom.Save(built, new Dictionary<int, byte[]> { [entry.Id] = session.Replacement()! }); session.VerifyExport(built);
            using (var rebuilt = NdsRom.Open(built))
            {
                var parsed = PlatinumEvents.Parse(f.Headers, f.Names, rebuilt.ReadFile(PlatinumEvents.EventPath), f.Matrices, f.Scripts, ["Zona A", "Zona B"]);
                bool others = rom.Files.Where(x => x.Path != PlatinumEvents.EventPath).All(x => rom.ReadFile(x).SequenceEqual(rebuilt.ReadFile(x.Path)));
                check(parsed.NpcCount(1) == 5 && parsed.Npc(1, 4).Id == 40 && others && Sha(File.ReadAllBytes(source)) == sourceHash, "NpcStructure_RebuiltRomKeepsEveryOtherFileAndTheSource");
            }
            using (var derivedRom = NdsRom.Open(built)) Reject<InvalidDataException>(() => PlatinumEvents.Open(derivedRom), check, "NpcStructure_DerivedEventsAreNotAnAccreditedSource");

            // Reopen from the checkpoint
            using (var second = NdsRom.Open(source))
            {
                var reopened = new NdsEventSession(second, f.Read()); reopened.Load(NdsEventSession.ReadDocument(cp1), cp1);
                check(reopened.Data.Bytes().SequenceEqual(session.Data.Bytes()) && reopened.CanUndo && reopened.Audit.SequenceEqual(session.Audit), "NpcStructure_ReopeningTheCheckpointRebuildsTheSameEvents");
            }

            // Undo: exact restoration
            var undo = session.PreviewUndo();
            check(undo.IsStructural && undo.Undo && undo.Changes[0] is { Before: 5, After: 4 } && undo.Lines![0].Contains("quitar el NPC añadido") && session.Data.NpcCount(1) == 5, "NpcStructure_UndoPreviewDoesNotChangeAnything");
            session.Save(undo, cp2);
            var restored = NdsEventSession.ReadDocument(cp2);
            check(session.Data.Bytes().SequenceEqual(originalBytes) && Sha(session.Data.Bytes()) == originalHash && !session.CanUndo && session.Replacement() is null && restored.Version == 1 && restored.Edits.Count == 0 && File.ReadAllText(cp1) == text, "NpcStructure_UndoRestoresTheOriginalBytesAndKeepsEarlierCheckpoints");

            // Add, move the new NPC, then undo twice
            var s3 = new NdsEventSession(rom, f.Read()); string a = Path.Combine(temp, "a.ndsevents.json"), b = Path.Combine(temp, "b.ndsevents.json"), c = Path.Combine(temp, "c.ndsevents.json"), d = Path.Combine(temp, "d.ndsevents.json");
            s3.Save(s3.PreviewAdd(1, rec), a); byte[] afterAdd = s3.Data.Bytes();
            s3.Save(s3.Preview(1, 4, new(121, 879, 1)), b); byte[] afterMove = s3.Data.Bytes();
            var mixed = NdsEventSession.ReadDocument(b);
            check(mixed.Version == 2 && mixed.Operations!.Select(o => o.Kind).SequenceEqual(["AddNpc", "MoveNpc"]) && s3.Data.Npc(1, 4) is { X: 121, Y: 879, Direction: 1 }, "NpcStructure_OperationsKeepTheirOrder");
            s3.Save(s3.PreviewUndo(), c); check(s3.Data.Bytes().SequenceEqual(afterAdd), "NpcStructure_FirstUndoRevertsTheMove");
            s3.Save(s3.PreviewUndo(), d); check(s3.Data.Bytes().SequenceEqual(originalBytes) && !afterMove.SequenceEqual(afterAdd), "NpcStructure_SecondUndoRemovesTheNpcExactly");
            using (var third = NdsRom.Open(source)) { var r3 = new NdsEventSession(third, f.Read()); r3.Load(NdsEventSession.ReadDocument(b), b); check(r3.Data.Bytes().SequenceEqual(afterMove), "NpcStructure_MixedCheckpointReopensExactly"); }

            // Version 1 stays what it was
            var s4 = new NdsEventSession(rom, f.Read()); string v1 = Path.Combine(temp, "v1.ndsevents.json"); s4.Save(s4.Preview(1, 0, new(120, 878, 2)), v1);
            string v1Text = File.ReadAllText(v1); var v1Doc = NdsEventSession.ReadDocument(v1);
            check(v1Doc.Version == 1 && v1Doc.Operations is null && !v1Text.Contains("Operations") && !v1Text.Contains("AddNpc") && v1Doc.Edits.Count == 1, "NpcStructure_MoveOnlySessionsStillWriteVersion1");
            string legacy = Path.Combine(temp, "legacy.ndsevents.json");
            File.WriteAllText(legacy, v1Text.Replace("\r\n", "\n"));   // exactly the shape older builds wrote: no Operations key
            using (var legacyRom = NdsRom.Open(source)) { var l = new NdsEventSession(legacyRom, f.Read()); l.Load(NdsEventSession.ReadDocument(legacy), legacy); check(l.Data.Npc(1, 0).X == 120 && l.CanUndo && !l.HasStructuralOperations, "NpcStructure_LegacyVersion1CheckpointsStillLoad"); }

            // Invalid documents never load
            using var guardRom = NdsRom.Open(source); var guard = new NdsEventSession(guardRom, f.Read());
            void Bad(NdsEventDocument document, string name) { try { guard.Load(document, "x.ndsevents.json"); throw new Exception("FAIL accepted: " + name); } catch (InvalidDataException) { check(guard.Revision == 0 && guard.Data.Bytes().SequenceEqual(originalBytes), name); } }
            var good = NdsEventSession.ReadDocument(cp1); var op = good.Operations![0];
            Bad(good with { Operations = [] }, "NpcStructure_V2WithoutOperationsRejected");
            Bad(good with { Edits = [new NdsNpcEdit(1, 0, new(1, 1, 0), new(2, 2, 0))] }, "NpcStructure_V2WithEditsRejected");
            Bad(v1Doc with { Operations = good.Operations }, "NpcStructure_V1WithOperationsRejected");
            Bad(good with { Version = 3 }, "NpcStructure_UnknownVersionRejected");
            Bad(good with { Operations = [op with { Kind = "DeleteNpc" }] }, "NpcStructure_UnknownOperationKindRejected");
            Bad(good with { Operations = [op with { Move = new NdsNpcEdit(1, 0, new(1, 1, 0), new(2, 2, 0)) }] }, "NpcStructure_OperationWithTwoPayloadsRejected");
            Bad(good with { Operations = [op with { Add = op.Add! with { Index = 3 } }] }, "NpcStructure_AdditionAtTheWrongIndexRejected");
            Bad(good with { Operations = [op with { Add = op.Add! with { RecordHex = Convert.ToHexString(Record(data, 1, 20, 1, 1)) } }] }, "NpcStructure_AdditionWithADuplicateIdRejected");
            Bad(good with { Operations = [op with { Add = op.Add! with { RecordHex = "ZZ" + op.Add.RecordHex[2..] } }] }, "NpcStructure_AdditionWithGarbageHexRejected");
            Bad(good with { Operations = [op with { Add = op.Add! with { RecordHex = op.Add.RecordHex[..62] } }] }, "NpcStructure_AdditionWithATruncatedRecordRejected");
            Bad(good with { Operations = [op with { Add = op.Add! with { Map = 99 } }] }, "NpcStructure_AdditionForAMissingMapRejected2");
            Bad(good with { SourceSha256 = new string('0', 64) }, "NpcStructure_CheckpointOfAnotherRomRejected");
            Bad(good with { Operations = [op, op] }, "NpcStructure_RepeatedAdditionOfTheSameRecordRejected");

            // Stale proposals, failed writes and concurrency
            var s5 = new NdsEventSession(rom, f.Read()); var p1 = s5.PreviewAdd(1, rec); var p2 = s5.Preview(1, 0, new(120, 878, 2));
            s5.Save(p2, Path.Combine(temp, "s5a.ndsevents.json"));
            Reject<InvalidOperationException>(() => s5.Save(p1, Path.Combine(temp, "s5b.ndsevents.json")), check, "NpcStructure_StaleStructuralProposalIsRejected");
            var s6 = new NdsEventSession(rom, f.Read()); var q = s6.PreviewAdd(1, rec); string taken = Path.Combine(temp, "taken.ndsevents.json"); File.WriteAllText(taken, "x");
            Reject<IOException>(() => s6.Save(q, taken), check, "NpcStructure_ExistingCheckpointIsNeverOverwritten");
            Reject<IOException>(() => s6.Save(q, Path.Combine(temp, "missing", "x.ndsevents.json")), check, "NpcStructure_UnwritableDestinationFails");
            check(s6.Revision == 0 && s6.Data.Bytes().SequenceEqual(originalBytes) && File.ReadAllText(taken) == "x" && Directory.GetFiles(temp, "*.partial-*").Length == 0, "NpcStructure_FailedSaveLeavesMemoryAndFilesUntouched");
            var s7 = new NdsEventSession(rom, f.Read()); var pa = s7.PreviewAdd(1, rec); var pb = s7.PreviewAdd(1, Record(data, 1, 41, 130, 130)); int wins = 0;
            Parallel.Invoke(() => { try { s7.Save(pa, Path.Combine(temp, "race-a.ndsevents.json")); Interlocked.Increment(ref wins); } catch (InvalidOperationException) { } },
                            () => { try { s7.Save(pb, Path.Combine(temp, "race-b.ndsevents.json")); Interlocked.Increment(ref wins); } catch (InvalidOperationException) { } });
            check(wins == 1 && s7.Revision == 1 && s7.Data.NpcCount(1) == 5 && Directory.GetFiles(temp, "race-*.ndsevents.json").Length == 1, "NpcStructure_ConcurrentStructuralSavesCommitExactlyOne");
            Reject<InvalidOperationException>(() => session.PreviewAdd(1, Record(data, 1, 20, 1, 1)), check, "NpcStructure_PreviewRejectsInvalidRecordsBeforeAnything");
        }
        finally { Directory.Delete(temp, true); }

        // ---- planner policy
        var terrain = PlatinumTerrain.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([PlatinumTerrainChecks.Land((x, z) => x >= 28 ? (ushort)0x8000 : (ushort)0)]));
        var model = new NdsMapCanvasModel(data.Layout(1), data.Events(1), terrain, data.Bounds(1));
        NdsAddPlan Plan(NdsAddDraft draft, PlatinumEvent? tpl2 = null) => NdsNpcStructurePlanner.PlanAdd(data, model, 1, tpl2 ?? template, draft);
        check(NdsNpcStructurePlanner.NextFreeId(data, 1) == 24 && NdsNpcStructurePlanner.NextFreeId(data, 0) == 11 && NdsNpcStructurePlanner.DraftFrom(data, 1, template, 120, 878) is { Id: 24, Graphics: 33, Movement: 0, Direction: 0, X: 120, Y: 878 }, "NpcStructurePlanner_FreeIdFollowsTheLargestInTheFile");
        var ready = Plan(new(24, 33, 0, 2, 120, 878));
        check(ready.Outcome == NdsMoveOutcome.Ready && ready.CanPropose && ready.Record!.Length == 32 && ready.Summary == "Sin conflictos estáticos conocidos" && !ready.Summary.Contains("segur"), "NpcStructurePlanner_ClearTileIsReadyButNeverCalledSafe");
        check(Plan(new(24, 33, 0, 2, 124, 878)) is { Outcome: NdsMoveOutcome.Rejected, CanPropose: false } blocked && blocked.Reasons.Any(r => r.Contains("colisión")), "NpcStructurePlanner_CollisionIsRejected");
        check(Plan(new(20, 33, 0, 2, 120, 878)) is { Outcome: NdsMoveOutcome.Rejected } dup && dup.Reasons.Any(r => r.Contains("ID 20")), "NpcStructurePlanner_DuplicateIdIsRejected");
        check(Plan(new(24, 33, 0, 2, 960, 878)) is { Outcome: NdsMoveOutcome.Rejected } oob && oob.Reasons.Any(r => r.Contains("límites")) && Plan(new(24, 33, 0, 2, -5, 878)).Outcome == NdsMoveOutcome.Rejected, "NpcStructurePlanner_OutOfBoundsIsRejected");
        check(Plan(new(24, 999, 0, 2, 120, 878)).Outcome == NdsMoveOutcome.Rejected && Plan(new(24, 33, 99, 2, 120, 878)).Outcome == NdsMoveOutcome.Rejected && Plan(new(24, 33, 0, 7, 120, 878)).Outcome == NdsMoveOutcome.Rejected && Plan(new(70000, 33, 0, 2, 120, 878)).Outcome == NdsMoveOutcome.Rejected, "NpcStructurePlanner_UnaccreditedValuesAreRejected");
        check(Plan(new(24, 33, 0, 2, 119, 878)) is { Outcome: NdsMoveOutcome.NeedsWarning, CanPropose: true } onNpc && onNpc.Reasons.Any(r => r.Contains("NPC 0")), "NpcStructurePlanner_OverlappingAnNpcNeedsAWarning");
        check(Plan(new(24, 33, 0, 2, 6, 5)) is { Outcome: NdsMoveOutcome.NeedsWarning } other && other.Reasons.Any(r => r.Contains("otra cabecera")), "NpcStructurePlanner_CellOfAnotherHeaderNeedsAWarning");
        check(NdsNpcStructurePlanner.PlanAdd(data, new NdsMapCanvasModel(data.Layout(1), data.Events(1), null, data.Bounds(1)), 1, template, new(24, 33, 0, 2, 120, 878)) is { Outcome: NdsMoveOutcome.NeedsWarning } noData && noData.Reasons.Any(r => r.Contains("sin datos de terreno")), "NpcStructurePlanner_WithoutTerrainNothingIsReady");
        check(Plan(new(24, 33, 0, 2, 120, 878), data.Events(1).First(e => e.Type == 3)).Outcome == NdsMoveOutcome.Rejected && Plan(new(24, 33, 0, 2, 120, 878), data.Events(1).First(e => e.Kind == PlatinumEventKind.Warp)).Outcome == NdsMoveOutcome.Rejected, "NpcStructurePlanner_OnlyNormalNpcAreTemplates");

        // copy / cut / paste compatibility
        var copy = new NdsNpcClip(template, false, 1, 1); var cut = new NdsNpcClip(template, true, 1, 1);
        check(NdsNpcStructurePlanner.PasteProblems(data, copy, 1).Count == 0 && NdsNpcStructurePlanner.PasteProblems(data, cut, 1).Count == 0, "NpcStructurePlanner_CopyAndCutPasteInTheSameMap");
        check(NdsNpcStructurePlanner.PasteProblems(data, copy, 2) is { Count: 1 } across && across[0].Contains("otro archivo de eventos") && NdsNpcStructurePlanner.PasteProblems(data, copy, 0).Count == 1, "NpcStructurePlanner_CopyAcrossEventFilesIsRejected");
        check(NdsNpcStructurePlanner.PasteProblems(data, cut, 2) is { Count: 1 } cutAcross && cutAcross[0].Contains("no se puede demostrar") && data.NpcCount(1) == 4, "NpcStructurePlanner_CutAcrossEventFilesIsRejectedAndKeepsTheOriginal");
        check(NdsNpcStructurePlanner.PasteProblems(sharedData, new NdsNpcClip(FirstNpc(sharedData, 1), false, 1, 1), 2).Count == 0 && NdsNpcStructurePlanner.PasteProblems(sharedData, new NdsNpcClip(FirstNpc(sharedData, 1), true, 1, 1), 2).Count == 0, "NpcStructurePlanner_PastingIntoAHeaderThatSharesTheEventFileIsAllowed");
        check(NdsNpcStructurePlanner.PasteProblems(data, new NdsNpcClip(template with { Id = 999 }, true, 1, 1), 1).Any(p => p.Contains("ya no existe")), "NpcStructurePlanner_CutOfAnNpcThatChangedIsRejected");
        check(copy.Origin.Contains("mapa 1") && copy.Origin.Contains("ID 20"), "NpcStructurePlanner_ClipKeepsItsProvenance");

        // delete is never offered for original data
        var del = NdsNpcStructurePlanner.AssessDelete(data, 1, template, false);
        check(!del.Allowed && del.Reasons.Any(r => r.Contains("analizador de scripts")) && del.Reasons.Any(r => r.Contains("script 1")) && del.Reasons.Any(r => r.Contains("reindexaría 3")) && !del.Reasons.Any(r => r.Contains("Restaurar")), "NpcStructurePlanner_DeleteIsRefusedWithTheEvidence");
        check(!NdsNpcStructurePlanner.AssessDelete(sharedData, 1, FirstNpc(sharedData, 1), true).Allowed && NdsNpcStructurePlanner.AssessDelete(sharedData, 1, FirstNpc(sharedData, 1), true).Reasons.Any(r => r.Contains("Restaurar último cambio") || r.Contains("comparten 2 cabeceras")), "NpcStructurePlanner_DeleteMentionsRestoreAndSharedFiles");
        var dupIds = data.AddNpc(1, Record(data, 1, 30, 130, 130)); var lastNpc = dupIds.Events(1).Last(e => e.Kind == PlatinumEventKind.Npc);
        check(NdsNpcStructurePlanner.AssessDelete(dupIds, 1, lastNpc, true).Reasons.Any(r => r.Contains("Restaurar último cambio")) && !NdsNpcStructurePlanner.AssessDelete(dupIds, 1, lastNpc, true).Allowed, "NpcStructurePlanner_AddedNpcPointsToTheExactUndo");
    }

    /// <summary>Real CPUE/1 round trip in a NEW folder: add → checkpoint v2 → export → reopen → undo → identical. Source is read-only and its SHA-256 is compared before and after.</summary>
    internal static void Probe(string source, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        void Require(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
        string copy = Path.Combine(output, "Platino-copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        object report;
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var map = events.Data.Maps.First(m => m.InternalName == "T01"); var originalData = events.Data; byte[] original = events.Data.Bytes(); string originalHash = Sha(original);
            int maxNpc = events.Data.Maps.Select(m => events.Data.NpcCount(m.Id)).Max();
            Require(maxNpc == PlatinumEvents.MaxVanillaNpcCount, "NpcStructureReal_ObservedMaximumMatchesTheConstant");
            var tpl = events.Data.Events(map.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0); int before = events.Data.NpcCount(map.Id);
            int id = NdsNpcStructurePlanner.NextFreeId(events.Data, map.Id);
            byte[] record = PlatinumEvents.NewNpcRecord(tpl, id, tpl.Graphics, tpl.Movement, tpl.Direction, tpl.X + 1, tpl.Y);
            Require(events.Data.ValidateNewNpc(map.Id, record).Count == 0, "NpcStructureReal_NewRecordPassesTheStructuralRules");
            var proposal = events.PreviewAdd(map.Id, record);
            Require(events.Revision == 0 && events.Data.Bytes().SequenceEqual(original), "NpcStructureReal_PreviewChangesNothing");
            string checkpoint = Path.Combine(output, "add.ndsevents.json"); events.Save(proposal, checkpoint); project.EventChanged();
            var after = events.Data;
            Require(after.NpcCount(map.Id) == before + 1 && after.Npc(map.Id, before) is { } n && n.Id == id && n.X == tpl.X + 1, "NpcStructureReal_AdditionIsAppendedAtTheEndOfTheNpcBlock");
            Require(Enumerable.Range(0, before).All(i => after.Npc(map.Id, i) == originalData.Npc(map.Id, i)) && originalData.Maps.Where(m => m.EventFile != map.EventFile).All(m => after.Events(m.Id).SequenceEqual(originalData.Events(m.Id))), "NpcStructureReal_ExistingNpcAndOtherMapsKeepTheirRecords");
            string built = Path.Combine(output, "Platino-NPC-nuevo.nds"); project.Export(built);
            using var baseline = NdsRom.Open(copy); using var builtRom = NdsRom.Open(built);
            var eventFile = baseline.File(PlatinumEvents.EventPath); var newFile = builtRom.File(PlatinumEvents.EventPath);
            Require(builtRom.ReadFile(PlatinumEvents.EventPath).SequenceEqual(after.Bytes()), "NpcStructureReal_ExportedRomHoldsTheNewEventArchive");
            Require(baseline.Files.Where(x => x.Path != PlatinumEvents.EventPath).All(x => baseline.ReadFile(x).SequenceEqual(builtRom.ReadFile(x.Path))), "NpcStructureReal_EveryOtherNdsFileIsByteExact");
            var reparsed = Narc.Read(builtRom.ReadFile(PlatinumEvents.EventPath)); var oldNarc = Narc.Read(original);
            Require(reparsed.Files.Count == oldNarc.Files.Count && Enumerable.Range(0, oldNarc.Files.Count).Where(i => i != map.EventFile).All(i => oldNarc.Files[i].SequenceEqual(reparsed.Files[i])), "NpcStructureReal_EveryOtherEventMemberIsByteExact");
            using (var reopened = NdsProject.Open(checkpoint, false))
            {
                Require(reopened.Events.Data.Bytes().SequenceEqual(after.Bytes()), "NpcStructureReal_ReopeningTheV2CheckpointRebuildsTheSameEvents");
                reopened.Events.Save(reopened.Events.PreviewUndo(), Path.Combine(output, "restored.ndsevents.json")); reopened.EventChanged();
                Require(reopened.Events.Data.Bytes().SequenceEqual(original) && Sha(reopened.Events.Data.Bytes()) == originalHash, "NpcStructureReal_UndoRestoresTheOriginalEventBytesExactly");
                reopened.Export(Path.Combine(output, "Platino-restaurado.nds"));
            }
            bool derivedRejected = false;
            using (var again = NdsProject.Open(built, false)) { try { _ = again.Events; } catch (InvalidDataException) { derivedRejected = true; } }
            Require(derivedRejected, "NpcStructureReal_ExportedRomIsNotTreatedAsAnAccreditedSource");
            report = new { profile = "CPUE/1", sourceSha256 = hash, map = new { map.Id, map.InternalName, map.EventFile }, template = new { tpl.Index, tpl.Id, tpl.X, tpl.Y }, newNpc = new { Index = before, Id = id, X = tpl.X + 1, tpl.Y },
                eventArchiveBytesBefore = original.Length, eventArchiveBytesAfter = after.Bytes().Length, slotBytes = baseline.SlotLength(eventFile), movedInRom = newFile.Start != eventFile.Start,
                observedMaxNpcCount = maxNpc, readOnly = "source copy only", gameplay = "NOT tested" };
        }
        using (var restoredRom = NdsRom.Open(Path.Combine(output, "Platino-restaurado.nds"))) using (var sourceRom = NdsRom.Open(source))
            Require(restoredRom.ReadFile(PlatinumEvents.EventPath).SequenceEqual(sourceRom.ReadFile(PlatinumEvents.EventPath)) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "Platino-restaurado.nds")))) == hash, "NpcStructureReal_RestoredRomIsIdenticalToTheSource");
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) == hash, "NpcStructureReal_SourceSha256Unchanged");
        Console.WriteLine("Real round trip finished; gameplay NOT tested");
    }

    // ------------------------------------------------------------------------------------------ WPF integration
    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject => NdsMapCanvasChecks.Children<T>(root).Single(e => AutomationProperties.GetName(e) == name);
    private static PlatinumTerrain UiTerrain() => PlatinumTerrain.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([PlatinumTerrainChecks.Land((x, z) => x >= 28 ? (ushort)0x8000 : (ushort)0)]));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-npc-structure-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = NdsMapCanvasChecks.Make(); string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = NdsMapCanvasChecks.Sha(source);
            string outputDir = Path.Combine(temp, "out"); Directory.CreateDirectory(outputDir);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read()); byte[] eventsBefore = session.Data.Bytes();
            var view = new NdsEventEditorView(session, UiTerrain(), null); var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single();
            var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single(); var tabs = NdsMapCanvasChecks.Children<TabControl>(view).Single(); var grids = NdsMapCanvasChecks.Children<DataGrid>(view).ToList(); var npcTable = grids[0];
            Button Btn(string name) => Find<Button>(view, "Eventos DS · " + name); TextBox Box(string name) => Find<TextBox>(view, "Eventos DS · " + name);
            var add = Btn("añadir NPC"); var dup = Btn("duplicar NPC"); var copy = Btn("copiar NPC"); var cut = Btn("cortar NPC"); var paste = Btn("pegar NPC"); var delete = Btn("eliminar NPC");
            var newBox = Find<Border>(view, "Eventos DS · NPC nuevo"); var info = Find<TextBlock>(view, "Eventos DS · validación del NPC nuevo"); var moveBox = Find<Border>(view, "Eventos DS · propuesta de movimiento"); var moveText = Find<TextBlock>(view, "Eventos DS · detalle de la propuesta");
            var status = Find<TextBlock>(view, "Eventos DS · estado"); var diff = Box("diferencias previstas"); var preview = Btn("previsualizar propuesta de NPC nuevo"); var save = Btn("Guardar cambios…"); var accept = Btn("aceptar propuesta");
            bool Visible(UIElement e) => e.Visibility == Visibility.Visible;
            NdsMapCanvasChecks.Arrange(view, 1100, 760); maps.SelectedItem = maps.Items[1]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            Point Tile(int x, int y) { var c = canvas.Model!.TileToPixel(x + 0.5, y + 0.5); return new Point(c.X, c.Y); }
            void Select(int index) { tabs.SelectedIndex = 0; npcTable.SelectedItem = npcTable.Items.Cast<PlatinumEvent>().First(e => e.Index == index); }
            bool Untouched() => session.Revision == 0 && session.Data.Bytes().SequenceEqual(eventsBefore) && NdsMapCanvasChecks.Sha(source) == sourceHash && Directory.GetFiles(temp).Length == 1 && Directory.GetFiles(outputDir).Length == 0;
            Select(0);

            // Buttons follow what is selected
            check(add.IsEnabled && dup.IsEnabled && copy.IsEnabled && cut.IsEnabled && !paste.IsEnabled && delete.IsEnabled, "NpcStructureUi_ButtonsForAnOrdinaryNpc");
            Select(2); check(!add.IsEnabled && !dup.IsEnabled && !copy.IsEnabled && !cut.IsEnabled && !paste.IsEnabled && delete.IsEnabled, "NpcStructureUi_GroundObjectNpcCannotBeCopiedOrDuplicated");
            tabs.SelectedIndex = 2; grids[2].SelectedItem = grids[2].Items.Cast<PlatinumEvent>().First();
            check(!add.IsEnabled && !copy.IsEnabled && !delete.IsEnabled, "NpcStructureUi_WarpsOfferNoNpcOperations"); Select(0);
            var menu = canvas.ContextMenu!;
            check(menu.Items.Count == 6 && menu.Items.Cast<MenuItem>().Select(i => i.IsEnabled).SequenceEqual(new[] { add, dup, copy, cut, paste, delete }.Select(b => b.IsEnabled)) && menu.Items.Cast<MenuItem>().All(i => AutomationProperties.GetName(i).StartsWith("Eventos DS · menú")), "NpcStructureUi_ContextMenuMirrorsTheButtons");
            check(new[] { add, dup, copy, cut, paste, delete }.All(b => b.ToolTip is string { Length: > 0 }), "NpcStructureUi_ButtonsExplainThemselves");

            // Copy and cut write nothing and delete nothing
            Click(copy);
            check(paste.IsEnabled && status.Text.Contains("Copiado") && status.Text.Contains("Nada se ha modificado") && Untouched(), "NpcStructureUi_CopyOnlyFillsTheClipboard");
            Click(cut);
            check(status.Text.Contains("No se ha eliminado nada") && session.Data.NpcCount(1) == 4 && Untouched() && ((string)paste.ToolTip).StartsWith("Trasladar"), "NpcStructureUi_CutDoesNotDeleteTheOriginal");
            Click(copy);

            // Duplicate: ghost, evaluation, form, proposal
            Click(dup);
            check(canvas.Placing && !Visible(newBox) && Untouched(), "NpcStructureUi_DuplicateStartsAGhostPlacement");
            canvas.PlacementMoveTo(Tile(124, 878));
            check(canvas.PlacementState is { Eval.Outcome: NdsMoveOutcome.Rejected } && !canvas.PlacementClick(Tile(124, 878)) && canvas.Placing && !Visible(newBox), "NpcStructureUi_CollisionTileCannotBeChosen");
            canvas.PlacementMoveTo(Tile(119, 878));
            check(canvas.PlacementState is { Eval.Outcome: NdsMoveOutcome.NeedsWarning }, "NpcStructureUi_TileWithAnotherNpcIsAnIndeterminateGhost");
            canvas.CancelPlacement();
            check(!canvas.Placing && !Visible(newBox) && Untouched(), "NpcStructureUi_CancellingThePlacementChangesNothing");
            Click(dup); canvas.PlacementMoveTo(Tile(120, 878));
            check(canvas.PlacementState is { Eval.Outcome: NdsMoveOutcome.Ready }, "NpcStructureUi_ClearTileIsAReadyGhost");
            NdsMapCanvasChecks.CenterOn(canvas, PlatinumEventKind.Npc, 0, 14); canvas.PlacementMoveTo(Tile(120, 878)); NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-structure-placement-1100");
            check(canvas.PlacementClick(Tile(120, 878)) && !canvas.Placing && Visible(newBox) && Untouched(), "NpcStructureUi_ChoosingATileOpensTheNewNpcForm");
            check(Box("nuevo NPC · ID").Text == "24" && Box("nuevo NPC · X").Text == "120" && Box("nuevo NPC · Y").Text == "878" && Box("nuevo NPC · dirección").Text == "0" && Find<ComboBox>(view, "Eventos DS · nuevo NPC · gráfico").SelectedItem is 33, "NpcStructureUi_FormIsPrefilledWithAFreeIdAndTheTemplateValues");
            check(info.Text.Contains("Estado: Sin conflictos estáticos conocidos") && info.Text.Contains("script 0, flag 0, tipo 0") && info.Text.Contains("4 → 5") && info.Text.Contains("No evaluado:") && preview.IsEnabled && !info.Text.Contains("seguro en el juego."), "NpcStructureUi_FormShowsTheStateTheCountAndWhatIsNotEvaluated");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-structure-form-1100");

            // Validation of the form
            Box("nuevo NPC · ID").Text = "20"; check(!preview.IsEnabled && info.Text.Contains("ID 20 ya lo usa") && info.Text.Contains("Estado: Rechazado"), "NpcStructureUi_DuplicateIdBlocksThePreview"); Box("nuevo NPC · ID").Text = "25";
            Box("nuevo NPC · X").Text = "960"; check(!preview.IsEnabled && info.Text.Contains("límites"), "NpcStructureUi_OutOfBoundsBlocksThePreview");
            Box("nuevo NPC · X").Text = "124"; check(!preview.IsEnabled && info.Text.Contains("colisión"), "NpcStructureUi_CollisionBlocksThePreview");
            Box("nuevo NPC · X").Text = "abc"; check(!preview.IsEnabled && info.Text.Contains("enteros"), "NpcStructureUi_NonNumericInputIsExplained");
            Box("nuevo NPC · X").Text = "120"; Box("nuevo NPC · dirección").Text = "9"; check(!preview.IsEnabled && info.Text.Contains("dirección"), "NpcStructureUi_InvalidDirectionBlocksThePreview"); Box("nuevo NPC · dirección").Text = "2";
            Box("nuevo NPC · X").Text = "119"; check(preview.IsEnabled && preview.Content.ToString() == "Previsualizar propuesta con advertencia" && info.Text.Contains("NPC 0"), "NpcStructureUi_OverlapNeedsAnExplicitWarning"); Box("nuevo NPC · X").Text = "120";
            check(preview.IsEnabled && preview.Content.ToString() == "Previsualizar propuesta" && Untouched(), "NpcStructureUi_ValidatingWritesNothing");

            // Preview -> checkpoint v2 -> restore
            Click(preview);
            check(!Visible(newBox) && diff.Text.Contains("NPC nuevo (índice 4)") && diff.Text.Contains("ID 25") && diff.Text.Contains("Cantidad de NPC: 4 → 5") && save.IsEnabled && Untouched(), "NpcStructureUi_PreviewIsAReviewableProposalAndStillWritesNothing");
            string first = Path.Combine(outputDir, "dup.ndsevents.json"); NdsMapCanvasChecks.Pump(view.SaveToAsync(first)); var doc = NdsEventSession.ReadDocument(first);
            check(session.Revision == 1 && session.Data.NpcCount(1) == 5 && doc.Version == 2 && doc.Operations!.Single().Add is { Map: 1, Index: 4 } && NdsMapCanvasChecks.Sha(source) == sourceHash, "NpcStructureUi_CheckpointV2StoresTheNewNpc");
            check(npcTable.Items.Count == 5 && canvas.Model!.Markers.Count(m => m.Kind == PlatinumEventKind.Npc) == 5 && canvas.Model.Find(PlatinumEventKind.Npc, 4) is { X: 120, Y: 878 } && canvas.Model.Layout.OwnedCells.Count == 1, "NpcStructureUi_TableAndCanvasRefreshFromTheRealSession");
            Select(4); check(npcTable.SelectedItem is PlatinumEvent { Id: 25, Script: 0 } && Box("Coordenada X").Text == "120", "NpcStructureUi_NewNpcIsSelectableAndHasNoScript");
            Click(delete);
            check(Visible(moveBox) && moveText.Text.Contains("NO permitido") && moveText.Text.Contains("Restaurar último cambio") && !Visible(accept) && session.Data.NpcCount(1) == 5, "NpcStructureUi_DeletingTheAddedNpcPointsToTheExactUndo");
            Click(Btn("cancelar propuesta"));
            Click(Btn("Restaurar último cambio…")); check(diff.Text.Contains("quitar el NPC añadido") && diff.Text.Contains("5 → 4") && session.Data.NpcCount(1) == 5, "NpcStructureUi_RestorePreviewNamesTheRemoval");
            string second = Path.Combine(outputDir, "undo.ndsevents.json"); NdsMapCanvasChecks.Pump(view.SaveToAsync(second));
            check(session.Revision == 2 && session.Data.Bytes().SequenceEqual(eventsBefore) && npcTable.Items.Count == 4 && canvas.Model!.Markers.Count(m => m.Kind == PlatinumEventKind.Npc) == 4 && NdsEventSession.ReadDocument(second).Version == 1 && File.Exists(first), "NpcStructureUi_RestoreReturnsToTheOriginalBytesAndKeepsBothCheckpoints");

            // Delete of an original NPC: refused with the evidence
            Select(0); Click(delete);
            check(Visible(moveBox) && moveText.Text.Contains("NO permitido") && moveText.Text.Contains("analizador de scripts") && moveText.Text.Contains("script 1") && moveText.Text.Contains("reindexaría 3") && moveText.Text.Contains("No se modificó nada") && !Visible(accept) && session.Data.NpcCount(1) == 4, "NpcStructureUi_DeleteIsRefusedWithTheEvidence");
            Click(Btn("cancelar propuesta")); check(!Visible(moveBox), "NpcStructureUi_DeleteReportCloses");

            // Add button: a form without choosing a tile
            Select(0); Click(add);
            check(Visible(newBox) && Box("nuevo NPC · ID").Text == "24" && Box("nuevo NPC · X").Text == "120" && !canvas.Placing, "NpcStructureUi_AddOpensTheFormOnTheTemplateNeighbour");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-structure-add-form-1100"); Click(Btn("cancelar NPC nuevo"));
            check(!Visible(newBox) && session.Revision == 2, "NpcStructureUi_CancellingTheFormChangesNothing");

            // Paste: same map copy
            Select(0); Click(copy); Select(1); Click(paste);
            check(canvas.Placing && canvas.PlacementState!.Value.Eval is null, "NpcStructureUi_PasteStartsAPlacementOfTheCopiedNpc");
            check(canvas.PlacementClick(Tile(121, 879)) && Visible(newBox) && Box("nuevo NPC · ID").Text == "24" && Find<ComboBox>(view, "Eventos DS · nuevo NPC · gráfico").SelectedItem is 33 && info.Text.Contains("NPC nuevo") == false, "NpcStructureUi_PasteOpensTheNewNpcFormWithAFreshId"); Click(Btn("cancelar NPC nuevo"));
            // Paste across event files is refused and nothing is lost
            maps.SelectedItem = maps.Items[2]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            check(paste.IsEnabled, "NpcStructureUi_ClipboardSurvivesAMapChange"); Click(paste);
            check(!canvas.Placing && Visible(moveBox) && moveText.Text.Contains("otro archivo de eventos") && moveText.Text.Contains("No se modificó nada") && Untouched() == false && session.Data.NpcCount(2) == 1 && session.Data.NpcCount(1) == 4, "NpcStructureUi_PasteIntoAnIncompatibleMapIsRefused");
            Click(Btn("cancelar propuesta"));
            // Cut across files
            maps.SelectedItem = maps.Items[1]; NdsMapCanvasChecks.Arrange(view, 1100, 760); Select(0); Click(cut); maps.SelectedItem = maps.Items[2]; NdsMapCanvasChecks.Arrange(view, 1100, 760); Click(paste);
            check(!canvas.Placing && moveText.Text.Contains("cortar y pegar entre archivos de eventos distintos") && session.Data.Npc(1, 0) is { X: 119, Y: 878 } && session.Data.NpcCount(1) == 4, "NpcStructureUi_CutAcrossEventFilesIsRefusedAndKeepsTheOriginal"); Click(Btn("cancelar propuesta"));
            // Cut and paste in the same map is a reversible move through the existing preview
            maps.SelectedItem = maps.Items[1]; NdsMapCanvasChecks.Arrange(view, 1100, 760); Select(0); Click(cut); Click(paste);
            check(canvas.Placing, "NpcStructureUi_CutPasteStartsAPlacement");
            canvas.PlacementMoveTo(Tile(124, 878)); check(canvas.PlacementState is { Eval.Outcome: NdsMoveOutcome.Rejected } && !canvas.PlacementClick(Tile(124, 878)), "NpcStructureUi_CutPasteOnACollisionIsRefused");
            canvas.CancelPlacement(); check(session.Data.Npc(1, 0) is { X: 119 } && !Visible(moveBox), "NpcStructureUi_CancellingACutKeepsTheOriginal");
            Click(paste); check(canvas.PlacementClick(Tile(120, 878)) && Visible(moveBox) && moveText.Text.Contains("Propuesto: X=120, Y=878") && Visible(accept), "NpcStructureUi_CutPasteBecomesTheMoveProposal");
            Click(accept);
            check(Box("Coordenada X").Text == "120" && diff.Text.Contains("Coordenada X: 119 → 120") && save.IsEnabled && session.Data.NpcCount(1) == 4 && session.Revision == 2, "NpcStructureUi_AcceptedCutGoesThroughTheExistingPreview");

            // Tool change and map change cancel placements and forms
            maps.SelectedItem = maps.Items[1]; NdsMapCanvasChecks.Arrange(view, 1100, 760); Select(0); Click(dup); var tool = Find<RadioButton>(view, "Eventos DS · herramienta mover NPC"); tool.IsChecked = true;
            check(!canvas.Placing, "NpcStructureUi_ChangingToolCancelsThePlacement"); Find<RadioButton>(view, "Eventos DS · herramienta seleccionar").IsChecked = true;
            Select(0); Click(add); maps.SelectedItem = maps.Items[2];
            check(!Visible(newBox), "NpcStructureUi_ChangingMapClosesTheForm");
            maps.SelectedItem = maps.Items[1]; NdsMapCanvasChecks.Arrange(view, 1100, 760);

            // Pending preview is protected by the usual authorization before a structural operation starts
            Select(0); Box("Coordenada X").Text = "130"; view.PreviewSelection(); check(save.IsEnabled, "NpcStructureUi_APreviewIsPending");
            Click(dup); check(canvas.Placing && !save.IsEnabled && diff.Text == "", "NpcStructureUi_StartingAnOperationAsksBeforeDiscardingAPendingPreview"); canvas.CancelPlacement();

            // Layout and accessibility
            foreach (int width in new[] { 1100, 760 })
            {
                NdsMapCanvasChecks.Arrange(view, width, 760); Select(0); Click(add);
                check(Visible(newBox) && canvas.ActualHeight >= 110 && view.DesiredSize.Width <= width && newBox.ActualHeight > 0 && preview.ActualWidth > 0, "NpcStructureUi_FormLayoutAt" + width);
                NdsMapCanvasChecks.Capture(view, width, 760, "npc-structure-form-layout-" + width); Click(Btn("cancelar NPC nuevo"));
            }
            check(NdsMapCanvasChecks.Children<Button>(view).Concat<Control>(NdsMapCanvasChecks.Children<TextBox>(view)).Concat(NdsMapCanvasChecks.Children<ComboBox>(view)).All(c => AutomationProperties.GetName(c).Length > 0), "NpcStructureUi_NewControlsHaveAccessibleNames");

            // Shared event file: the new NPC shows up for every header
            var shared = f with { Headers = (byte[])f.Headers.Clone() }; BinaryPrimitives.WriteUInt16LittleEndian(shared.Headers.AsSpan(2 * 24 + 16), 1);
            string sharedSource = Path.Combine(temp, "shared.nds"); File.WriteAllBytes(sharedSource, shared.Rom()); using var sharedRom = NdsRom.Open(sharedSource); var sharedSession = new NdsEventSession(sharedRom, shared.Read());
            var sv = new NdsEventEditorView(sharedSession, UiTerrain(), null); var sm = NdsMapCanvasChecks.Children<ListBox>(sv).Single(); var sg = NdsMapCanvasChecks.Children<DataGrid>(sv).ToList(); var st = NdsMapCanvasChecks.Children<TabControl>(sv).Single();
            NdsMapCanvasChecks.Arrange(sv, 1100, 760); sm.SelectedItem = sm.Items[1]; NdsMapCanvasChecks.Arrange(sv, 1100, 760); st.SelectedIndex = 0; sg[0].SelectedItem = sg[0].Items.Cast<PlatinumEvent>().First(e => e.Index == 0);
            Click(Find<Button>(sv, "Eventos DS · añadir NPC")); var sInfo = Find<TextBlock>(sv, "Eventos DS · validación del NPC nuevo");
            // map 2 is only 64x96: the conservative bounds reject the default position (121,878), so pick a tile inside both
            Find<TextBox>(sv, "Eventos DS · nuevo NPC · X").Text = "10"; Find<TextBox>(sv, "Eventos DS · nuevo NPC · Y").Text = "10";
            check(sInfo.Text.Contains("compartido por 2 cabeceras (1, 2)") && sInfo.Text.Contains("aparecerá en todas"), "NpcStructureUi_SharedFileFormWarnsThatEveryHeaderGetsTheNpc");
            Click(Find<Button>(sv, "Eventos DS · previsualizar propuesta de NPC nuevo")); NdsMapCanvasChecks.Pump(sv.SaveToAsync(Path.Combine(outputDir, "shared.ndsevents.json")));
            sm.SelectedItem = sm.Items[2]; NdsMapCanvasChecks.Arrange(sv, 1100, 760);
            check(sharedSession.Data.NpcCount(1) == 5 && sharedSession.Data.NpcCount(2) == 5 && sg[0].Items.Count == 5, "NpcStructureUi_SharedFileNpcAppearsInTheOtherHeader");
        }
        finally { Directory.Delete(temp, true); }
    }
}
