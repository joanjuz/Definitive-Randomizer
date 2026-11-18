using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Nds.Project;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Border = System.Windows.Controls.Border;
using Point = System.Windows.Point;

/// <summary>
/// Facing and trainer sight of existing NPC (Platinum CPUE/1): engine contracts, byte-exact writes, checkpoints v1–v4, persistent projects, the geometric
/// sight overlay and the WPF controls. Synthetic data; the real ROM is exercised by <see cref="Probe"/>.
/// </summary>
internal static class NdsTrainerVisionChecks
{
    // ------------------------------------------------------------------------------------------------------------------- fixture
    internal sealed record Fixture(byte[] Headers, byte[] Names, byte[] Events, byte[] Matrices, byte[] Scripts)
    {
        internal PlatinumEvents Read() => PlatinumEvents.Parse(Headers, Names, Events, Matrices, Scripts, ["Zona A"]);
        internal byte[] Rom() => NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]>
        { [PlatinumEvents.EventPath] = Events, [PlatinumEvents.NamesPath] = Names, [PlatinumEvents.MatrixPath] = Matrices, [PlatinumEvents.ScriptPath] = Scripts, ["untouched.bin"] = [7, 1, 99, 18] }, "CPUE", 1);
    }
    private static void Put(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), checked((ushort)value));
    private static byte[] Npc(int id, int x, int y, int type, int movement, int direction, int sight, int script)
    {
        var b = new byte[32]; Put(b, 0, id); Put(b, 2, 33); Put(b, 4, movement); Put(b, 6, type); Put(b, 10, script); Put(b, 12, direction); Put(b, 14, sight);
        Put(b, 16, 0xCAFE); Put(b, 18, 0xBEEF); Put(b, 24, x); Put(b, 26, y); return b;
    }
    // Map 1 and map 2 share event file 1 (trainers); map 0 has its own small file. NPC of file 1:
    //  0 trainer, LOOK_SOUTH (15), facing down, sight 3, battle script 3001 · 1 ordinary NPC whose +14 is 77 (not a sight)
    //  2 trainer that wanders (3), facing left, sight 2 · 3 trainer that sees all directions (type 2) · 4 ground item · 5 NPC two tiles below trainer 0
    //  6 trainer LOOK_WEST at X 1 (the matrix edge is one tile to its left)
    internal const int Map = 1, Trainer = 0, Ordinary = 1, Wanderer = 2, AllWays = 3, Item = 4, Blocker = 5, Edge = 6;
    internal static Fixture Make()
    {
        byte[] File(byte[][] npcs)
        {
            using var output = new MemoryStream(); using var w = new BinaryWriter(output);
            w.Write(0); w.Write(npcs.Length); foreach (var r in npcs) w.Write(r); w.Write(0); w.Write(0); return output.ToArray();
        }
        byte[] headers = new byte[72], names = new byte[48]; int[] matrixOf = [1, 0, 0], eventOf = [0, 1, 1];
        for (int i = 0; i < 3; i++) { Encoding.ASCII.GetBytes("MAP_" + (char)('A' + i)).CopyTo(names, i * 16); Put(headers, i * 24 + 2, matrixOf[i]); Put(headers, i * 24 + 16, eventOf[i]); }
        var events = NdsGen5RandomizerChecks.BuildNarc([
            File([Npc(10, 2, 3, 0, 0, 0, 0, 0)]),
            File([Npc(20, 119, 878, 1, 15, 1, 3, 3001), Npc(21, 112, 878, 0, 0, 0, 77, 1), Npc(22, 110, 884, 1, 3, 2, 2, 3005), Npc(23, 100, 870, 2, 51, 1, 1, 3010),
                Npc(24, 130, 885, 3, 0, 0, 0, 7000), Npc(25, 119, 880, 0, 0, 0, 0, 0), Npc(26, 1, 870, 1, 16, 2, 4, 5002)])]);
        var matrices = NdsGen5RandomizerChecks.BuildNarc([PlatinumTerrainChecks.Matrix(30, 30, i => i == 3 + 27 * 30 ? 1 : 2, _ => 0), PlatinumTerrainChecks.Matrix(2, 3, null, _ => 0)]);
        return new(headers, names, events, matrices, NdsGen5RandomizerChecks.BuildNarc([[2, 0, 0, 0, 0x13, 0xFD, 2, 0, 0xAB, 0xCD]]));
    }
    /// <summary>Local X 28–31 of every block collides (so tiles 124–127 of block 3 are blocked); everything else has no collision.</summary>
    internal static PlatinumTerrain Terrain() => PlatinumTerrain.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([PlatinumTerrainChecks.Land((x, _) => x >= 28 ? (ushort)0x8000 : (ushort)0)]));
    private static NdsMapCanvasModel Model(PlatinumEvents data, PlatinumTerrain? terrain)
    {
        var model = new NdsMapCanvasModel(data.Layout(Map), data.Events(Map), terrain, data.Bounds(Map)); model.SetViewport(1000, 700); return model;
    }
    private static void Expect<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); } catch (T) { check(true, name); return; }
        throw new Exception("FAIL expected " + typeof(T).Name + ": " + name);
    }
    /// <summary>Offsets inside the NPC records of a map that differ between two event archives (absolute archive offsets are converted to record offsets).</summary>
    private static int[] ChangedOffsets(byte[] before, byte[] after) => Enumerable.Range(0, Math.Min(before.Length, after.Length)).Where(i => before[i] != after[i]).ToArray();
    private static PlatinumNpcValues Plain(PlatinumEvent e) => PlatinumEvents.Values(e);

    // ------------------------------------------------------------------------------------------------------------------- synthetic
    internal static void Run(Action<bool, string> check)
    {
        // ---- engine contracts (pret/pokeplatinum constants, checked against the owner's dump)
        check(Enumerable.Range(0, 4).Select(PlatinumTrainerVision.DirectionName).SequenceEqual(["Arriba", "Abajo", "Izquierda", "Derecha"]), "Vision_DirectionValuesAreNorthSouthWestEast");
        check(PlatinumTrainerVision.Step(0) == (0, -1) && PlatinumTrainerVision.Step(1) == (0, 1) && PlatinumTrainerVision.Step(2) == (-1, 0) && PlatinumTrainerVision.Step(3) == (1, 0), "Vision_DirectionStepsMatchTheMapAxes");
        check(Enumerable.Range(0, 4).Select(PlatinumTrainerVision.FixedLookFor).SequenceEqual([14, 15, 16, 17]) && PlatinumTrainerVision.IsFixedLook(14) && PlatinumTrainerVision.IsFixedLook(17) && !PlatinumTrainerVision.IsFixedLook(13) && !PlatinumTrainerVision.IsFixedLook(18), "Vision_FixedLookMovementsAreLookNorthSouthWestEast");
        Expect<ArgumentOutOfRangeException>(() => PlatinumTrainerVision.FixedLookFor(4), check, "Vision_NoFixedLookForAnInvalidDirection");
        check(Enumerable.Range(0, 12).Where(PlatinumTrainerVision.ChecksSight).SequenceEqual([1, 2, 4, 5, 6, 7, 8]), "Vision_OnlyTheTrainerTypesTheEngineChecksHaveSight");
        check(PlatinumTrainerVision.TrainerId(3000) == 1 && PlatinumTrainerVision.TrainerId(3001) == 2 && PlatinumTrainerVision.TrainerId(5000) == 1 && PlatinumTrainerVision.TrainerId(1) is null && PlatinumTrainerVision.TrainerId(7000) is null, "Vision_BattleScriptRangesFollowTheEngine");
        check(PlatinumTrainerVision.KeepsFacing(0) && PlatinumTrainerVision.KeepsFacing(15) && !PlatinumTrainerVision.KeepsFacing(3) && !PlatinumTrainerVision.KeepsFacing(2), "Vision_OnlyNoneAndFixedLooksKeepTheFacing");
        check(PlatinumTrainerVision.MaxAccreditedSight == 6, "Vision_MaxSightIsTheLargestOfTheOriginalData");

        var f = Make(); var data = f.Read(); var original = data.Bytes();
        PlatinumEvent N(int index) => data.Npc(Map, index);
        int RecordStart(int index) { var e = N(index); var raw = Convert.FromHexString(e.Raw); for (int i = 0; i + 32 <= original.Length; i++) if (original.AsSpan(i, 32).SequenceEqual(raw)) return i; throw new Exception("record not found"); }
        int[] Relative(byte[] after, int index) => ChangedOffsets(original, after).Select(o => o - RecordStart(index)).ToArray();
        check(N(Trainer) is { Type: 1, Movement: 15, Direction: 1, Sight: 3, Script: 3001 } && N(Ordinary).Sight == 77 && PlatinumTrainerVision.SightRefusal(N(Ordinary)) is { } why && why.Contains("no es un alcance"), "Vision_FixtureReadsTheStoredFacingAndSight");
        check(PlatinumTrainerVision.SightRefusal(N(Trainer)) is null && PlatinumTrainerVision.SightRefusal(N(AllWays)) is null && PlatinumTrainerVision.SightRefusal(N(Item)) is { } item && item.Contains("objeto de suelo"), "Vision_SightRefusalExplainsNonTrainers");

        // ---- byte-exact writes
        var t = N(Trainer); var legacy = Plain(t);
        check(Relative(data.ReplaceNpc(Map, Trainer, legacy, legacy with { Direction = 3 }), Trainer).SequenceEqual([12]), "Vision_LegacyTurnStillWritesOnlyTheDirection");
        var turn = new PlatinumNpcValues(t.X, t.Y, 3, 17, null); var turnBefore = new PlatinumNpcValues(t.X, t.Y, 1, 15, null);
        byte[] turned = data.ReplaceNpc(Map, Trainer, turnBefore, turn);
        check(Relative(turned, Trainer).SequenceEqual([4, 12]) && data.WithEvents(turned).Npc(Map, Trainer) is { Direction: 3, Movement: 17, Sight: 3 }, "Vision_FixedLookTurnWritesMovementAndDirectionOnly");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceNpc(Map, Trainer, turnBefore, turn with { Movement = 15 }), check, "Vision_FixedLookMovementMustMatchTheDirection");
        var w = N(Wanderer);
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceNpc(Map, Wanderer, new(w.X, w.Y, w.Direction, w.Movement, null), new(w.X, w.Y, 3, 17, null)), check, "Vision_AMovementThatIsNotAFixedLookIsNeverRewritten");
        var sightBefore = new PlatinumNpcValues(t.X, t.Y, t.Direction, null, 3);
        foreach (int s in new[] { 0, 1, 5, 6 })
            check(Relative(data.ReplaceNpc(Map, Trainer, sightBefore, sightBefore with { Sight = s }), Trainer).SequenceEqual(s == 3 ? Array.Empty<int>() : new[] { 14 }) && data.WithEvents(data.ReplaceNpc(Map, Trainer, sightBefore, sightBefore with { Sight = s })).Npc(Map, Trainer).Sight == s, $"Vision_SightOf{s}WritesOnlyItsField");
        foreach (int s in new[] { -1, 7, 117, 65535 })
            Expect<ArgumentOutOfRangeException>(() => data.ReplaceNpc(Map, Trainer, sightBefore, sightBefore with { Sight = s }), check, $"Vision_SightOf{s}IsRefused");
        var o = N(Ordinary);
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceNpc(Map, Ordinary, new(o.X, o.Y, o.Direction, null, 77), new(o.X, o.Y, o.Direction, null, 3)), check, "Vision_SightIsNeverWrittenOnAnOrdinaryNpc");
        var g = N(Item);
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceNpc(Map, Item, new(g.X, g.Y, g.Direction, null, 0), new(g.X, g.Y, g.Direction, null, 2)), check, "Vision_SightIsNeverWrittenOnAGroundItem");
        var all = new PlatinumNpcValues(t.X, t.Y, 1, 15, 3); var combined = data.ReplaceNpc(Map, Trainer, all, new(t.X + 1, t.Y - 2, 2, 16, 5));
        check(Relative(combined, Trainer).OrderBy(i => i).SequenceEqual([4, 12, 14, 24, 26]) && data.WithEvents(combined).Npc(Map, Trainer) is { X: 120, Y: 876, Direction: 2, Movement: 16, Sight: 5 } && data.WithEvents(combined).Npc(Map, Trainer).Raw[32..40] == "FECAEFBE", "Vision_PositionDirectionMovementAndSightTogetherKeepUnknownBytes");
        Expect<ArgumentException>(() => data.ReplaceNpc(Map, Trainer, legacy, legacy with { Sight = 4 }), check, "Vision_BeforeAndAfterMustCarryTheSameFields");
        Expect<InvalidOperationException>(() => data.ReplaceNpc(Map, Trainer, sightBefore with { Sight = 4 }, sightBefore with { Sight = 5 }), check, "Vision_StaleExpectedSightIsRefused");
        Expect<InvalidOperationException>(() => data.ReplaceNpc(Map, Trainer, turnBefore with { Movement = 16 }, turn), check, "Vision_StaleExpectedMovementIsRefused");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceNpc(Map, Trainer, legacy, legacy with { Direction = 4 }), check, "Vision_DirectionOutsideZeroToThreeIsRefused");

        // ---- fingerprints: the editable one ignores exactly the editable fields
        var vision = data.WithEvents(combined);
        check(vision.EditableHash() == data.EditableHash() && vision.NormalizedHash() != data.NormalizedHash() && data.WithEvents(data.ReplaceNpc(Map, Trainer, legacy, legacy with { Direction = 0 })).NormalizedHash() == data.NormalizedHash(), "Vision_EditableFingerprintIgnoresOnlyTheEditableFields");
        byte[] Patch(int index, int offset, int value) { var b = data.Bytes(); Put(b, RecordStart(index) + offset, value); return b; }
        Expect<InvalidDataException>(() => data.WithEvents(Patch(Ordinary, 14, 3)), check, "Vision_ARawSightOnAnOrdinaryNpcIsRejected");
        Expect<InvalidDataException>(() => data.WithEvents(Patch(Trainer, 4, 3)), check, "Vision_AFixedLookTurnedIntoAnotherMovementIsRejected");
        Expect<InvalidDataException>(() => data.WithEvents(Patch(Wanderer, 4, 15)), check, "Vision_AnotherMovementTurnedIntoAFixedLookIsRejected");
        Expect<InvalidDataException>(() => data.WithEvents(Patch(Trainer, 16, 1)), check, "Vision_UnknownBytesStayProtected");
        check(data.WithEvents(Patch(Trainer, 14, 6)).Npc(Map, Trainer).Sight == 6, "Vision_ARawSightOnATrainerPassesTheFingerprint");

        // ---- geometric sight (the overlay)
        var terrain = Terrain(); var model = Model(data, terrain);
        string Cells(IReadOnlyList<NdsSightCell> cells) => string.Join(" ", cells.Select(c => $"{c.X},{c.Y}:{c.State}"));
        check(Cells(NdsTrainerVision.Cells(model, t, t.X, t.Y, 1, 3)) == "119,879:Open 119,880:Occupied 119,881:Shadowed", "Vision_AnotherNpcHidesTheTilesBehindIt");
        check(Cells(NdsTrainerVision.Cells(model, t, t.X, t.Y, 3, 6)) == "120,878:Open 121,878:Open 122,878:Open 123,878:Open 124,878:Blocked 125,878:Shadowed", "Vision_ATerrainCollisionHidesTheTilesBehindIt");
        check(NdsTrainerVision.Cells(model, t, t.X, t.Y, 0, 2).Select(c => (c.X, c.Y, c.Distance)).SequenceEqual([(119, 877, 1), (119, 876, 2)]) && NdsTrainerVision.Cells(model, t, t.X, t.Y, 2, 1).Single() is { X: 118, Y: 878, Direction: 2 }, "Vision_UpAndLeftCellsFollowTheDirection");
        check(NdsTrainerVision.Cells(model, t, t.X, t.Y, 1, 0).Count == 0, "Vision_SightZeroSeesNothing");
        var a = N(AllWays);
        check(NdsTrainerVision.Cells(model, a, a.X, a.Y, a.Direction, 1).Select(c => c.Direction).SequenceEqual([0, 1, 2, 3]) && NdsTrainerVision.Directions(a, 0).Count == 4, "Vision_TypeTwoSeesTheFourDirections");
        var e = N(Edge);
        check(Cells(NdsTrainerVision.Cells(model, e, e.X, e.Y, 2, 4)) == "0,870:Open" && NdsTrainerVision.Cells(model, e, 0, 0, 0, 4).Count == 0 && NdsTrainerVision.Cells(model, e, 959, 959, 1, 4).Count == 0 && NdsTrainerVision.Cells(model, e, 959, 959, 3, 4).Count == 0, "Vision_TheMatrixEdgeCutsTheSight");
        check(NdsTrainerVision.Cells(model, o, o.X, o.Y, 3, 77).Count == 0 && NdsTrainerVision.Cells(model, g, g.X, g.Y, 0, 3).Count == 0, "Vision_NonTrainersHaveNoSightOverlay");
        var bare = Model(data, null);
        check(NdsTrainerVision.Cells(bare, t, t.X, t.Y, 3, 6).All(c => c.State == NdsSightState.Unknown), "Vision_WithoutTerrainEveryTileIsUnknownNotOpen");
        check(NdsTrainerVision.GeometricNotice.Contains("estimado") && NdsTrainerVision.GeometricNotice.Contains("No es una reproducción exacta"), "Vision_TheOverlayIsAnnouncedAsAnEstimate");
        check(NdsTrainerVision.Notes(t).Any(n => n.Contains("Mira fijo") && n.Contains("14–17")) && NdsTrainerVision.Notes(t).Any(n => n.Contains("entrenador 2")), "Vision_NotesExplainFixedLookAndTheBattle");
        check(NdsTrainerVision.Notes(w).Any(n => n.Contains("solo la inicial")), "Vision_NotesWarnThatAWalkingTrainerMayTurn");
        check(NdsTrainerVision.Notes(a).Any(n => n.Contains("cuatro direcciones")) && NdsTrainerVision.Notes(o).Any(n => n.Contains("No es un entrenador")) && NdsTrainerVision.Notes(e).Any(n => n.Contains("doble")), "Vision_NotesDescribeEachKindOfNpc");
        check(NdsTrainerVision.CanTurn(t) && NdsTrainerVision.CanTurn(o) && !NdsTrainerVision.CanTurn(g), "Vision_GroundItemsCannotBeTurned");
        var clock = System.Diagnostics.Stopwatch.StartNew(); for (int i = 0; i < 20000; i++) _ = NdsTrainerVision.Cells(model, t, t.X, t.Y, i % 4, 6);
        check(clock.ElapsedMilliseconds < 3000, "Vision_OverlayComputationStaysFast");

        string temp = Path.Combine(Path.GetTempPath(), "nds-trainer-vision-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try { SessionChecks(f, temp, check); }
        finally { Directory.Delete(temp, true); }
    }

    private static void SessionChecks(Fixture f, string temp, Action<bool, string> check)
    {
        string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(source));
        using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read()); var t = session.Data.Npc(Map, Trainer);

        // A plain turn of an NPC that keeps no fixed look stays a version 1 checkpoint (the shape older builds read).
        var o = session.Data.Npc(Map, Ordinary);
        var plain = session.Preview(Map, Ordinary, new(o.X, o.Y, 2, null, null));
        check(plain.Operation.Move is { Before.HasVisionFields: false, After.HasVisionFields: false } && plain.Changes.Single().Field.Contains("Arriba → Izquierda"), "VisionSession_PlainTurnNamesTheDirections");
        string v1 = Path.Combine(temp, "v1.ndsevents.json"); session.Save(plain, v1);
        string v1Json = File.ReadAllText(v1);
        check(NdsEventSession.ReadDocument(v1).Version == 1 && !v1Json.Contains("Movement") && !v1Json.Contains("Sight"), "VisionSession_PlainTurnStaysAVersion1Checkpoint");
        // Unchanged movement or sight are dropped from the proposal, so they never force a newer checkpoint.
        var same = session.Preview(Map, Trainer, new(t.X, t.Y, t.Direction, t.Movement, t.Sight));
        check(same.Changes.Count == 0 && same.Operation.Move!.After.HasVisionFields == false, "VisionSession_UnchangedFieldsAreDropped");

        // Turn a fixed-look trainer and raise its sight: version 4, readable back, exact restoration.
        var proposal = session.Preview(Map, Trainer, new(t.X, t.Y, 3, 17, 5));
        check(proposal.Changes.Select(c => c.Field).SequenceEqual(["Dirección (Abajo → Derecha)", "Movimiento «mirar fijo»", "Alcance de visión (casillas)"]) && proposal.Changes[2] is { Before: 3, After: 5 }, "VisionSession_PreviewListsDirectionMovementAndSight");
        string v4 = Path.Combine(temp, "v4.ndsevents.json"); session.Save(proposal, v4);
        var document = NdsEventSession.ReadDocument(v4);
        check(document.Version == 4 && document.Operations!.Count == 2 && document.Operations[1].Move!.After == new PlatinumNpcValues(t.X, t.Y, 3, 17, 5) && session.Data.Npc(Map, Trainer) is { Direction: 3, Movement: 17, Sight: 5 }, "VisionSession_VisionEditWritesAVersion4Checkpoint");
        var reopened = new NdsEventSession(rom, f.Read()); reopened.Load(document, v4);
        check(reopened.Data.Bytes().SequenceEqual(session.Data.Bytes()) && reopened.CanUndo, "VisionSession_ReopenReproducesTheBytes");
        check(session.Data.Npc(2, Trainer) is { Direction: 3, Sight: 5 } && session.Data.Bounds(1) == session.Data.Bounds(2), "VisionSession_SharedEventFileShowsTheChangeInEveryHeader");
        // Shapes an older or a broken writer could produce are refused instead of replayed differently.
        Expect<InvalidDataException>(() => reopened.Load(document with { Version = 2 }, v4), check, "VisionSession_VisionFieldsInAVersion2CheckpointAreRefused");
        Expect<InvalidDataException>(() => reopened.Load(document with { Version = 3 }, v4), check, "VisionSession_VisionFieldsInAVersion3CheckpointAreRefused");
        var asV1 = new NdsEventDocument(document.Format, 1, document.Game, document.Revision, document.SourcePath, document.SourceSha256, document.Operations!.Select(op => op.Move!).ToList(), document.Audit);
        Expect<InvalidDataException>(() => reopened.Load(asV1, v4), check, "VisionSession_VisionFieldsInAVersion1CheckpointAreRefused");
        var noVision = document with { Operations = [document.Operations![0]] };
        Expect<InvalidDataException>(() => reopened.Load(noVision, v4), check, "VisionSession_AVersion4CheckpointWithoutVisionFieldsIsRefused");
        var outOfRange = document with { Operations = [document.Operations[0], NdsEventOperation.OfMove(document.Operations[1].Move! with { After = document.Operations[1].Move!.After with { Sight = 9 } })] };
        Expect<ArgumentOutOfRangeException>(() => reopened.Load(outOfRange, v4), check, "VisionSession_ACheckpointWithAnUnaccreditedSightIsRefused");
        string tampered = Path.Combine(temp, "tampered.ndsevents.json"); File.WriteAllText(tampered, File.ReadAllText(v4).Replace("\"Sight\": 3", "\"Sight\": 4"));
        Expect<InvalidOperationException>(() => new NdsEventSession(rom, f.Read()).Load(NdsEventSession.ReadDocument(tampered), tampered), check, "VisionSession_ACheckpointWhoseStoredSightIsNotTheRomsIsRefused");
        // Older checkpoints (written before these fields existed) are read as before.
        string handV1 = Path.Combine(temp, "hand.ndsevents.json");
        File.WriteAllText(handV1, JsonSerializer.Serialize(new { Format = NdsEventSession.Format, Version = 1, Game = "CPUE", Revision = 1, SourcePath = source, SourceSha256 = Convert.ToHexString(sourceHash),
            Edits = new[] { new { Map, Index = Trainer, Before = new { X = t.X, Y = t.Y, Direction = 1 }, After = new { X = t.X, Y = t.Y, Direction = 0 } } }, Audit = new[] { "a mano" } }));
        var legacy = new NdsEventSession(rom, f.Read()); legacy.Load(NdsEventSession.ReadDocument(handV1), handV1);
        check(legacy.Data.Npc(Map, Trainer) is { Direction: 0, Movement: 15, Sight: 3 }, "VisionSession_AnOldVersion1CheckpointStillLoads");

        // Export: only the event archive changes; restoration gives the original ROM byte for byte.
        string built = Path.Combine(temp, "vision.nds"); rom.Save(built, new Dictionary<int, byte[]> { [rom.File(PlatinumEvents.EventPath).Id] = reopened.Replacement()! });
        using (var verify = NdsRom.Open(built))
        {
            var read = PlatinumEvents.Parse(f.Headers, f.Names, verify.ReadFile(PlatinumEvents.EventPath), f.Matrices, f.Scripts);
            check(read.Npc(Map, Trainer) is { Direction: 3, Movement: 17, Sight: 5 } && verify.ReadFile("untouched.bin").SequenceEqual(rom.ReadFile("untouched.bin")) && read.EditableHash() == f.Read().EditableHash(), "VisionSession_ExportedRomCarriesTheTurnAndTheSightOnly");
        }
        var undo = reopened.PreviewUndo();
        check(undo.Undo && undo.Changes.Any(c => c.Field == "Alcance de visión (casillas)" && c.Before == 5 && c.After == 3), "VisionSession_UndoPreviewShowsTheOriginalValues");
        string undone = Path.Combine(temp, "undo.ndsevents.json"); reopened.Save(undo, undone);
        check(reopened.Data.Npc(Map, Trainer) is { Direction: 1, Movement: 15, Sight: 3 } && NdsEventSession.ReadDocument(undone).Version == 1, "VisionSession_UndoRestoresTheFieldsAndTheVersion");
        reopened.Save(reopened.PreviewUndo(), Path.Combine(temp, "undo2.ndsevents.json"));
        check(reopened.Replacement() is null && reopened.Data.Bytes().SequenceEqual(f.Events), "VisionSession_UndoingEverythingRestoresTheOriginalArchive");
        string restoredRom = Path.Combine(temp, "restored.nds"); rom.Save(restoredRom, new Dictionary<int, byte[]>());
        check(SHA256.HashData(File.ReadAllBytes(restoredRom)).SequenceEqual(sourceHash) && SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(sourceHash), "VisionSession_RestoredRomIsByteExactAndTheSourceIsIntact");

        // Persistent project: the reader version follows the history (2 with turns or sight, 1 otherwise).
        var ops = document.Operations!; var projectEvents = new NdsProjectEvents(ops, document.Audit);
        check(projectEvents.CheckpointVersion == 4 && projectEvents.RequiredReader == 2 && new NdsProjectEvents([ops[0]], ["x"]).RequiredReader == 1 && new NdsProjectEvents([ops[0]], ["x"]).CheckpointVersion == 1, "VisionProject_ReaderVersionFollowsTheHistory");
        var imported = NdsProjectEvents.FromCheckpoint(document);
        check(imported.ImportedFrom == 4 && imported.ToCheckpoint(source, document.SourceSha256).Version == 4, "VisionProject_Version4CheckpointsImportAndExport");
        check(NdsProjectCodec.ContainerVersion >= 2, "VisionProject_ThisBuildReadsProjectsOfReader2");

        // End to end through a persistent project: saved with reader 2, rebuilt exactly from the stored history, back to reader 1 after undoing.
        string platinum = Path.Combine(temp, "platino.nds"); File.WriteAllBytes(platinum, NdsGen4Checks.SyntheticRom(Gen4Profile.All.Single(p => p.GameCode == "CPUE"), true));
        using var project = NdsProject.Open(platinum, true); var own = new NdsEventSession(rom, f.Read()); project.AdoptEventSession(own);
        var medium = new NdsProjectMedium(); project.AttachPersistence(new InMemoryNdsProjectStore(medium), NdsProjectRomIdentity.Compute(platinum, true), null, "comprobaciones");
        var pt = own.Data.Npc(Map, Trainer); project.SaveEvent(own.Preview(Map, Trainer, new(pt.X, pt.Y, 3, 17, 5)));
        var stored = NdsProjectCodec.Read(medium.Main!);
        check(stored.Status == NdsProjectReadStatus.Ok && stored.Header!.MinReaderVersion == 2 && stored.Document!.Meta.MinReaderVersion == 2 && stored.Document.Meta.Events!.CheckpointVersion == 4, "VisionProject_SavingAVisionEditRequiresReader2");
        var rebuilt = new NdsEventSession(rom, f.Read()); rebuilt.Restore(stored.Document!.Meta.Events!.Operations, stored.Document.Meta.Events.Audit, stored.Document.State.EventData, stored.Document.State.EventScripts);
        check(rebuilt.Data.Bytes().SequenceEqual(own.Data.Bytes()) && rebuilt.Data.Npc(Map, Trainer) is { Direction: 3, Movement: 17, Sight: 5 }, "VisionProject_TheStoredHistoryRebuildsTheSameBytes");
        project.SaveEvent(own.PreviewUndo()); var back = NdsProjectCodec.Read(medium.Main!);
        check(back.Header!.MinReaderVersion == 1 && own.Data.Bytes().SequenceEqual(f.Events), "VisionProject_UndoingTheVisionEditReturnsToReader1");
    }

    // ------------------------------------------------------------------------------------------------------------------- WPF (off-screen)
    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject => NdsMapCanvasChecks.Children<T>(root).Single(e => AutomationProperties.GetName(e) == name);

    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-trainer-vision-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = Make(); string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read()); byte[] original = session.Data.Bytes();
            var view = new NdsEventEditorView(session, Terrain(), null); var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single();
            var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single(); var npcTable = NdsMapCanvasChecks.Children<DataGrid>(view).First();
            Button Btn(string name) => Find<Button>(view, "Eventos DS · " + name); TextBox Box(string name) => Find<TextBox>(view, "Eventos DS · " + name);
            var panel = Find<Border>(view, "Eventos DS · orientación y visión"); var state = Find<TextBlock>(view, "Eventos DS · orientación · guardado y propuesto");
            var notes = Find<TextBlock>(view, "Eventos DS · orientación · notas"); var differences = Box("diferencias previstas"); var status = Find<TextBlock>(view, "Eventos DS · estado");
            var sight = Box("Alcance de visión"); var direction = Box("Dirección (0–3)");
            NdsMapCanvasChecks.Arrange(view, 1100, 760); maps.SelectedItem = maps.Items[Map]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            void Select(int index) { npcTable.SelectedItem = npcTable.Items.Cast<PlatinumEvent>().Single(e => e.Index == index); NdsMapCanvasChecks.Arrange(view, 1100, 760); }
            void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            bool Untouched() => session.Revision == 0 && session.Data.Bytes().SequenceEqual(original) && NdsMapCanvasChecks.Sha(source) == sourceHash && Directory.GetFiles(temp).Length == 1;

            // Selecting a trainer shows where it looks and how far it sees; nothing is proposed yet.
            Select(Trainer);
            check(panel.Visibility == Visibility.Visible && canvas.Facing is { Direction: 1, Sight: 3, Changed: false } && sight.Text == "3" && state.Text.Contains("Abajo · visión 3") && state.Text.Contains("sin cambios"), "VisionUi_SelectingATrainerShowsItsFacingAndSight");
            check(new[] { "arriba", "abajo", "izquierda", "derecha" }.All(n => Btn("orientación · mirar hacia " + n).Content.ToString()!.Length > 2) && notes.Text.Contains("Mira fijo") && notes.Text.Contains("estimado"), "VisionUi_TheControlsUseDirectionNamesAndExplainTheOverlay");
            NdsMapCanvasChecks.CenterOn(canvas, PlatinumEventKind.Npc, Trainer, 20); canvas.Invalidate();
            byte[] before = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check(Untouched(), "VisionUi_SelectionChangesNothing");

            // Turning with the panel prepares a proposal with the old and new direction; the map redraws.
            Click(Btn("orientación · mirar hacia derecha"));
            check(direction.Text == "3" && canvas.Facing is { Direction: 3, SavedDirection: 1, Changed: true } && differences.Text.Contains("Abajo → Derecha") && differences.Text.Contains("Movimiento «mirar fijo»: 15 → 17") && Btn("Guardar cambios…").IsEnabled, "VisionUi_TurnButtonPreparesTheProposal");
            check(state.Text.Contains("Guardado: Abajo · visión 3") && state.Text.Contains("Propuesta: Derecha · visión 3") && status.Text.Contains("Aún no se ha guardado nada"), "VisionUi_SavedAndProposedAreBothShown");
            canvas.Invalidate(); byte[] after = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check(!before.SequenceEqual(after) && session.Revision == 0, "VisionUi_TheMapDrawsTheProposedFacingWithoutSaving");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "vision-turn-right-1100");

            // Sight: + and − buttons, limits 0 and 6.
            Click(Btn("alcance de visión · una casilla más")); Click(Btn("alcance de visión · una casilla más"));
            check(sight.Text == "5" && canvas.Facing is { Sight: 5, SavedSight: 3 } && differences.Text.Contains("Alcance de visión (casillas): 3 → 5"), "VisionUi_PlusRaisesTheSightAndPreviews");
            Click(Btn("alcance de visión · una casilla más")); Click(Btn("alcance de visión · una casilla más"));
            check(sight.Text == "6" && !Btn("alcance de visión · una casilla más").IsEnabled, "VisionUi_SightStopsAtTheAccreditedMaximum");
            for (int i = 0; i < 8; i++) canvas.FacingKey(Key.OemMinus, ModifierKeys.Shift);
            check(sight.Text == "0" && !Btn("alcance de visión · una casilla menos").IsEnabled && canvas.Facing!.Sight == 0, "VisionUi_ShiftMinusLowersTheSightDownToZero");
            sight.Text = "9"; view.PreviewSelection();
            check(status.Text.StartsWith("No se guardó") && !Btn("Guardar cambios…").IsEnabled, "VisionUi_ATypedSightOutsideTheRangeIsRefused");
            sight.Text = "5"; view.PreviewSelection();

            // On the canvas: the handle next to the NPC turns it (also with the move tool, without starting a drag); Mayús+flecha too; plain arrows still navigate.
            var up = canvas.HandleCentre(0)!.Value;
            check(canvas.TurnAt(up) && canvas.Facing!.Direction == 0 && canvas.Drag is null && direction.Text == "0", "VisionUi_ClickingTheUpHandleTurnsTheNpc");
            Find<RadioButton>(view, "Eventos DS · herramienta mover NPC").IsChecked = true;
            var left = canvas.HandleCentre(2)!.Value;
            check(canvas.TurnAt(left) && canvas.Drag is null && canvas.Facing!.Direction == 2 && !canvas.BeginDragAt(left), "VisionUi_HandlesNeverStartADragWithTheMoveTool");
            check(canvas.FacingKey(Key.Down, ModifierKeys.Shift) && canvas.Facing!.Direction == 1 && !canvas.FacingKey(Key.Down, ModifierKeys.None) && !canvas.FacingKey(Key.Down, ModifierKeys.Shift | ModifierKeys.Control), "VisionUi_ShiftArrowTurnsAndPlainArrowsKeepTheirMeaning");
            canvas.FacingKey(Key.Right, ModifierKeys.Shift);
            check(differences.Text.Contains("Abajo → Derecha") && differences.Text.Contains("3 → 5") && canvas.Model!.SelectedMarker?.Index == Trainer, "VisionUi_TurningKeepsTheSelectionAndTheSightProposal");
            check(canvas.Model!.Zoom == 20, "VisionUi_TurningKeepsTheZoom");

            // Restoring the fields cancels the proposal; nothing was written.
            Click(Btn("orientación · restaurar valores guardados"));
            check(direction.Text == "1" && sight.Text == "3" && canvas.Facing is { Changed: false } && !Btn("Guardar cambios…").IsEnabled && differences.Text.Length == 0 && Untouched(), "VisionUi_RestoreSavedValuesCancelsTheProposal");

            // Save to a checkpoint: version 4, the trainer stays selected, the map keeps its zoom.
            Click(Btn("orientación · mirar hacia derecha")); Click(Btn("alcance de visión · una casilla más")); Click(Btn("alcance de visión · una casilla más"));
            string checkpoint = Path.Combine(temp, "vision.ndsevents.json"); NdsMapCanvasChecks.Pump(view.SaveToAsync(checkpoint));
            check(session.Revision == 1 && session.Data.Npc(Map, Trainer) is { Direction: 3, Movement: 17, Sight: 5 } && NdsEventSession.ReadDocument(checkpoint).Version == 4 && NdsMapCanvasChecks.Sha(source) == sourceHash, "VisionUi_SaveWritesAVersion4Checkpoint");
            check(canvas.Model!.SelectedMarker?.Index == Trainer && canvas.Facing is { Direction: 3, Sight: 5, Changed: false } && state.Text.Contains("Derecha · visión 5") && canvas.Model.Zoom == 20, "VisionUi_AfterSavingTheTrainerStaysSelectedWithTheNewValues");
            // Restoring the last change goes through the existing flow.
            Click(Btn("Restaurar último cambio…"));
            check(differences.Text.Contains("5 → 3") && Btn("Guardar cambios…").IsEnabled, "VisionUi_RestoreLastChangePreviewsTheOldValues");
            NdsMapCanvasChecks.Pump(view.SaveToAsync(Path.Combine(temp, "restored.ndsevents.json")));
            check(session.Data.Bytes().SequenceEqual(original) && canvas.Facing is { Direction: 1, Sight: 3 }, "VisionUi_RestoringGivesBackTheOriginalBytes");

            // Other kinds of NPC.
            Select(Ordinary);
            check(panel.Visibility == Visibility.Visible && sight.Text.Length == 0 && Find<TextBlock>(view, "Eventos DS · orientación · notas").Text.Contains("No es un entrenador") && canvas.Facing is { Sight: null }, "VisionUi_AnOrdinaryNpcCanTurnButHasNoSight");
            check(NdsMapCanvasChecks.Children<TextBlock>(view).Any(b => b.Text.StartsWith("No aplica")) && !Btn("alcance de visión · una casilla más").IsEnabled, "VisionUi_SightControlsExplainWhyTheyDoNotApply");
            Select(Wanderer);
            check(notes.Text.Contains("solo la inicial"), "VisionUi_AWalkingTrainerWarnsThatTheFacingMayChange");
            Select(Item);
            check(panel.Visibility == Visibility.Collapsed && canvas.Facing is null, "VisionUi_AGroundItemShowsNoFacingControls");
            Select(AllWays);
            check(notes.Text.Contains("cuatro direcciones") && canvas.Facing is { Sight: 1 }, "VisionUi_AnAllDirectionsTrainerIsExplained");

            // Layers: the selected trainer's sight can be hidden; all trainers' sight can be shown.
            var visionLayer = Find<CheckBox>(view, "Eventos DS · capa Visión"); var allLayer = Find<CheckBox>(view, "Eventos DS · capa Visión de todos");
            Select(Trainer); NdsMapCanvasChecks.CenterOn(canvas, PlatinumEventKind.Npc, Trainer, 20); canvas.Invalidate(); byte[] withSight = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            visionLayer.IsChecked = false; canvas.Invalidate(); byte[] withoutSight = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check(!canvas.ShowVision && !withSight.SequenceEqual(withoutSight), "VisionUi_TheVisionLayerCanBeHidden");
            visionLayer.IsChecked = true; allLayer.IsChecked = true; canvas.Invalidate(); byte[] everyone = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check(canvas.ShowAllVision && !everyone.SequenceEqual(withSight) && Find<CheckBox>(view, "Eventos DS · capa NPC").IsChecked == true, "VisionUi_AllTrainersSightCanBeShown");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "vision-all-1100");
            allLayer.IsChecked = false;

            // Small windows and high DPI: the controls stay reachable and the map keeps a usable size.
            foreach (var (w, h) in new[] { (760, 560), (1100, 760) })
                foreach (int dpi in new[] { 120, 144 })
                {
                    NdsMapCanvasChecks.Arrange(view, w, h); var image = new System.Windows.Media.Imaging.RenderTargetBitmap(w * dpi / 96, h * dpi / 96, dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32); image.Render(view);
                    check(image.PixelWidth == w * dpi / 96 && canvas.ActualHeight >= 180 && Btn("orientación · mirar hacia derecha").IsEnabled, $"VisionUi_{w}x{h}_At{dpi * 100 / 96}PercentKeepsTheControls");
                }
            foreach (var (w, h) in new[] { (1100, 760), (760, 560) })
            {
                NdsMapCanvasChecks.Arrange(view, w, h); var right = Btn("orientación · mirar hacia derecha");
                DependencyObject? up2 = right; while (up2 is not null and not ScrollViewer) up2 = System.Windows.Media.VisualTreeHelper.GetParent(up2);
                var viewer = (ScrollViewer)up2!; var edge = right.TranslatePoint(new Point(right.ActualWidth, 0), viewer);
                check(right.ActualWidth > 0 && edge.X <= viewer.ViewportWidth + 0.5, $"VisionUi_{w}x{h}_TheTurnButtonsFitTheInspector");
            }
            NdsMapCanvasChecks.Capture(view, 760, 560, "vision-small-760");

            // The existing move tool still works on the same trainer after these edits.
            NdsMapCanvasChecks.Arrange(view, 1100, 760); Select(Trainer); NdsMapCanvasChecks.CenterOn(canvas, PlatinumEventKind.Npc, Trainer, 20);
            var centre = canvas.Model!.TileToPixel(119.5, 878.5);
            check(canvas.BeginDragAt(new Point(centre.X, centre.Y)) && canvas.Drag is not null, "VisionUi_TheMoveToolStillDragsTheTrainer");
            canvas.CancelDrag();
        }
        finally { Directory.Delete(temp, true); }
    }

    // ------------------------------------------------------------------------------------------------------------------- real ROM (private copy)
    /// <summary>
    /// Real CPUE/1 copy: pick an existing fixed-look trainer with a battle script, turn it and change its sight, checkpoint, reopen, export, verify that the
    /// exported ROM is accepted by the event profile, restore and compare whole-ROM hashes. Writes MANUAL.txt for the in-game test. Gameplay is NOT tested.
    /// </summary>
    internal static void Probe(string output, string source)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "Platino-copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        int mapId, index; PlatinumNpcValues before, after; string label;
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var data = events.Data;
            Console.WriteLine($"PASS VisionReal_ProfileFingerprints: normalized {data.NormalizedHash() == PlatinumEvents.NormalizedEventsSha256}, editable {data.EditableHash() == PlatinumEvents.EditableEventsSha256}");
            if (data.EditableHash() != PlatinumEvents.EditableEventsSha256) throw new Exception("Editable fingerprint of the original differs from the accredited constant");
            // Census of the original data: the accredited facts this editor relies on.
            var npcs = data.Maps.GroupBy(m => m.EventFile).Select(g => g.First()).SelectMany(m => data.Events(m.Id).Where(e => e.Kind == PlatinumEventKind.Npc)).ToList();
            var trainers = npcs.Where(e => PlatinumTrainerVision.ChecksSight(e.Type)).ToList();
            int maxSight = trainers.Max(e => e.Sight), fixedLook = trainers.Count(e => PlatinumTrainerVision.IsFixedLook(e.Movement)), coupled = trainers.Count(e => PlatinumTrainerVision.IsFixedLook(e.Movement) && e.Movement == PlatinumTrainerVision.FixedLookFor(e.Direction));
            Console.WriteLine($"PASS VisionReal_Census: {npcs.Count} NPC, {trainers.Count} trainers checked by sight, max sight {maxSight}, fixed-look {fixedLook} (matching direction {coupled})");
            if (maxSight != PlatinumTrainerVision.MaxAccreditedSight) throw new Exception("The accredited maximum sight differs from the data");
            // A fixed-look single-battle trainer on a route, facing down, with sight 2–4, alone in its event file.
            var candidate = data.Maps.Where(m => data.Maps.Count(o => o.EventFile == m.EventFile) == 1 && m.InternalName.StartsWith("R2"))
                .SelectMany(m => data.Events(m.Id).Where(e => e.Kind == PlatinumEventKind.Npc && e.Type == 1 && e.Movement == 15 && e.Direction == 1 && e.Sight is >= 2 and <= 4 && e.Script is >= 3000 and < 5000).Select(e => (Map: m, Npc: e)))
                .OrderBy(c => c.Map.Id).ThenBy(c => c.Npc.Index).First();
            mapId = candidate.Map.Id; index = candidate.Npc.Index; var npc = candidate.Npc;
            before = new(npc.X, npc.Y, npc.Direction, npc.Movement, npc.Sight); after = new(npc.X, npc.Y, 3, 17, npc.Sight + 2);
            label = $"mapa {mapId} · {candidate.Map.Name} ({candidate.Map.InternalName}) · NPC índice {index}, ID {npc.Id}, script {npc.Script} (entrenador {PlatinumTrainerVision.TrainerId(npc.Script)}) · X/Y {npc.X}/{npc.Y}";
            string checkpoint = Path.Combine(output, "vision.ndsevents.json");
            var proposal = events.Preview(mapId, index, after);
            if (proposal.Changes.Count != 3) throw new Exception("Unexpected change list: " + string.Join("; ", proposal.Changes.Select(c => c.Field)));
            events.Save(proposal, checkpoint); project.EventChanged();
            if (NdsEventSession.ReadDocument(checkpoint).Version != 4) throw new Exception("Checkpoint is not version 4");
            string built = Path.Combine(output, "Platino-vision.nds"); project.Export(built);
            using (var reopened = NdsProject.Open(checkpoint, false))
            {
                var r = reopened.Events.Data.Npc(mapId, index);
                if (r.Direction != 3 || r.Movement != 17 || r.Sight != after.Sight) throw new Exception("Checkpoint did not persist");
                reopened.Events.Save(reopened.Events.PreviewUndo(), Path.Combine(output, "restored.ndsevents.json")); reopened.EventChanged();
                reopened.Export(Path.Combine(output, "Platino-restaurado.nds"));
            }
            Console.WriteLine("PASS VisionReal_TurnAndSightSaveReopenAndUndo: " + label);
            using (var rebuilt = NdsProject.Open(built, false))
            {
                var r = rebuilt.Events.Data.Npc(mapId, index);
                if (r.Direction != 3 || r.Movement != 17 || r.Sight != after.Sight) throw new Exception("Exported ROM did not persist");
                if (rebuilt.Events.Data.NormalizedHash() == PlatinumEvents.NormalizedEventsSha256 || rebuilt.Events.Data.EditableHash() != PlatinumEvents.EditableEventsSha256) throw new Exception("Exported ROM was accepted for the wrong reason");
                Console.WriteLine("PASS VisionReal_ExportedRomReopensInTheEventEditor (accepted by the editable fingerprint)");
            }
            using var baselineRom = NdsRom.Open(copy); using var builtRom = NdsRom.Open(built);
            foreach (var file in baselineRom.Files) if (file.Path != PlatinumEvents.EventPath && !baselineRom.ReadFile(file).SequenceEqual(builtRom.ReadFile(file.Path))) throw new Exception("Unrelated file changed: " + file.Path);
            byte[] a = baselineRom.ReadFile(PlatinumEvents.EventPath), b = builtRom.ReadFile(PlatinumEvents.EventPath);
            int changed = Enumerable.Range(0, a.Length).Count(i => a[i] != b[i]);
            if (a.Length != b.Length || changed is < 1 or > 3) throw new Exception($"Event archive changed {changed} bytes; expected the 3 low bytes of +4, +12 and +14");
            Console.WriteLine($"PASS VisionReal_OnlyTheEventArchiveChanged ({changed} bytes); every other NDS file byte-exact");
        }
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "Platino-restaurado.nds")))) != hash)
            throw new Exception("Source changed or whole-ROM restoration differs");
        File.WriteAllText(Path.Combine(output, "MANUAL.txt"),
            $"Prueba jugable (Platino CPUE/1, copia privada). Entrenador: {label}.\n" +
            $"Original: mira hacia {PlatinumTrainerVision.DirectionName(before.Direction)} (movimiento {before.Movement}), visión {before.Sight}.\n" +
            $"Platino-vision.nds: mira hacia {PlatinumTrainerVision.DirectionName(after.Direction)} (movimiento {after.Movement}), visión {after.Sight}.\n" +
            "Usa una partida guardada FUERA de ese mapa (el juego restaura los NPC del mapa en el que guardaste).\n" +
            $"1. Acércate por la derecha del entrenador en su misma fila: debe verte a {after.Sight} casillas o menos, si nada se interpone.\n" +
            $"2. A {after.Sight + 1} casillas no debe verte. 3. Pasa por debajo (por donde miraba antes): ya no debe verte.\n" +
            "4. Platino-restaurado.nds debe comportarse como el original. Checkpoints: vision.ndsevents.json y restored.ndsevents.json.\nNo probado en juego.");
        Console.WriteLine("PASS VisionReal_OriginalAndRestoredRomHashesMatch; gameplay NOT tested");
    }
}
