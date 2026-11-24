using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.ProjectDetection;
using Button = System.Windows.Controls.Button;

/// <summary>
/// The map and event editor on Pokémon Black (IRBO/0): its own Gen 5 reader (48-byte headers, u32 matrices, zone event files of 20/36/20/22-byte records,
/// warps in sixteenths of a tile, rail records locked, item balls by script), terrain collision and terrain models from WB/GC blocks, and the shared
/// editor through <see cref="INdsEventData"/>. Synthetic checks, off-screen checks with a Black session, and a probe on a private Black copy. No gameplay.
/// </summary>
internal static class NdsGen5BlackChecks
{
    private static readonly Gen5MapProfile Black = Gen5MapProfile.Black;
    internal sealed record Fixture(byte[] Headers, byte[] Events, byte[] Matrices, byte[] Scripts)
    {
        internal Gen5ZoneEvents Read() => Gen5ZoneEvents.Parse(Black, Headers, Events, Matrices, Scripts, ["Pueblo A", "Ruta B", "Casa C"]);
        internal byte[] Rom(string code = "IRBO", byte revision = 0) => NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]>
        { [Gen5MapProfile.HeadersPath] = Headers, [Gen5MapProfile.MatrixPath] = Matrices, [Black.EventPath] = Events, [Black.ScriptPath] = Scripts, ["untouched.bin"] = [7, 1, 99, 18] }, code, revision);
    }
    /// <summary>Map 0 (Pueblo A) and map 1 (Ruta B) share matrix 0 (2×1 blocks); map 2 (Casa C) is an interior (matrix 1, 1×1).</summary>
    internal const int Town = 0, House = 2;
    internal const int Plain = 0, Trainer = 1, Ball = 2, Rail = 3;
    private static void P16(byte[] b, int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)v);
    internal static byte[] Npc(int id, int x, int y, int movement = 0, int type = 0, int script = 0, int direction = 1, int sight = 0, int position = 0)
    {
        var r = new byte[36]; P16(r, 0, id); P16(r, 2, 20); P16(r, 4, movement); P16(r, 6, type); P16(r, 10, script); P16(r, 12, direction); P16(r, 14, sight);
        P16(r, 24, position); P16(r, 28, x); P16(r, 30, y); P16(r, 34, 0xBEEF); return r;
    }
    internal static byte[] Background(int script, int type, int facing, int x, int y) { var r = new byte[20]; P16(r, 0, script); P16(r, 2, type); P16(r, 4, facing); P16(r, 8, x); P16(r, 12, y); P16(r, 16, 0x30); return r; }
    internal static byte[] Warp(int header, int anchor, int x, int y, int w = 1, int h = 1) { var r = new byte[20]; P16(r, 0, header); P16(r, 2, anchor); r[4] = 1; r[5] = 1; P16(r, 8, x * 16 + 8); P16(r, 12, y * 16 + 8); P16(r, 14, w); P16(r, 16, h); return r; }
    internal static byte[] Trigger(int script, int x, int y, int w, int h) { var r = new byte[22]; P16(r, 0, script); P16(r, 2, 1); P16(r, 4, 0x4000); P16(r, 10, x); P16(r, 12, y); P16(r, 14, w); P16(r, 16, h); return r; }
    internal static byte[] EventFile(byte[][] bgs, byte[][] npcs, byte[][] warps, byte[][] triggers, byte[] tail)
    {
        var body = new List<byte> { (byte)bgs.Length, (byte)npcs.Length, (byte)warps.Length, (byte)triggers.Length };
        foreach (var r in bgs.Concat(npcs).Concat(warps).Concat(triggers)) body.AddRange(r);
        return [.. BitConverter.GetBytes(body.Count), .. body, .. tail];
    }
    internal static byte[] Matrix(int w, int h, uint[] lands) { var m = new byte[8 + w * h * 4]; P16(m, 4, w); P16(m, 6, h); for (int i = 0; i < lands.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(8 + i * 4), lands[i]); return m; }
    internal static Fixture Make()
    {
        byte[] headers = new byte[3 * 48];
        void H(int id, int matrix, int script, int events, int name) { P16(headers, id * 48 + 2, 0); P16(headers, id * 48 + 4, matrix); P16(headers, id * 48 + 6, script); P16(headers, id * 48 + 22, events); headers[id * 48 + 26] = (byte)name; }
        H(0, 0, 0, 0, 0); H(1, 0, 0, 1, 1); H(2, 1, 0, 2, 2);
        var town = EventFile([Background(8001, 2, 4, 5, 5), Background(12, 1, 0, 6, 2)],
            [Npc(0, 3, 3, script: 1), Npc(1, 10, 10, movement: 15, type: 1, script: 3001, direction: 1, sight: 3), Npc(2, 12, 12, script: 7000), Npc(3, 20, 4, position: 1)],
            [Warp(2, 0, 8, 8), Warp(0xFFFF, 0, 9, 8)], [Trigger(5, 30, 6, 1, 3)], [1, 0, 0, 0]);
        var route = EventFile([], [Npc(0, 40, 5)], [], [], [0, 0, 0, 0]);
        var house = EventFile([], [], [Warp(0, 0, 5, 9, 3, 1)], [], [0, 0, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD]);
        return new(NdsGen5RandomizerChecks.BuildNarc([headers]), NdsGen5RandomizerChecks.BuildNarc([town, route, house, new byte[4]]),
            NdsGen5RandomizerChecks.BuildNarc([Matrix(2, 1, [0, 1]), Matrix(1, 1, [2])]), NdsGen5RandomizerChecks.BuildNarc([[1, 2, 3, 4]]));
    }

    internal static void Run(Action<bool, string> check)
    {
        // ---- profiles: Black only; nothing of Gen 4 applies
        check(Gen5MapProfile.For("IRBO", 0) == Black && Gen5MapProfile.For("IRAS", 0) == Gen5MapProfile.White && Gen5MapProfile.For("IRDO", 0) is null && Gen5MapProfile.For("IREO", 0) is null && Gen5MapProfile.For("IRBO", 1) is null,
            "Gen5_BlackAndWhiteHaveAProfile_NotBlack2White2OrOtherRevisions");
        check(Equals(NdsEventProfiles.For("IRBO", 0), Black) && Equals(NdsEventProfiles.For("CPUE", 1), Gen4EventProfile.Platinum) && Gen4EventProfile.For("IRBO", 0) is null && NdsEventProfiles.SupportedLabels.Contains("Negro IRBO/0"),
            "Gen5_TheRegistryNamesBothGenerations");
        check(Black is { EventPath: "a/1/2/5", ScriptPath: "a/0/5/7", HeaderCount: 427, EventFileCount: 428, LocationNamesText: 89, SupportsNpcCreation: false, EditsObjectFacing: false, MaxTrainerSight: 0 }
            && Gen4EventProfile.All.All(p => p.EditsObjectFacing), "Gen5_BlackProfileIsMeasuredAndConservative");

        // ---- reading
        var f = Make(); var data = f.Read();
        check(data.Maps.Count == 3 && data.Map(Town) is { Name: "Pueblo A", Matrix: 0, Width: 64, Height: 32, EventFile: 0 } && data.Map(House) is { Name: "Casa C", Width: 32 }, "Gen5_HeadersGiveNameMatrixAndEvents");
        var ev = data.Events(Town);
        check(ev.Count(e => e.Kind == PlatinumEventKind.Object) == 2 && ev.Count(e => e.Kind == PlatinumEventKind.Npc) == 4 && ev.Count(e => e.Kind == PlatinumEventKind.Warp) == 2 && ev.Count(e => e.Kind == PlatinumEventKind.Trigger) == 1,
            "Gen5_RecordsOfTheFourKinds");
        check(data.Npc(Town, Trainer) is { X: 10, Y: 10, Direction: 1, Movement: 15, Type: 1, Sight: 3, Script: 3001 } && data.Event(Town, PlatinumEventKind.Warp, 0) is { X: 8, Y: 8, Destination: 2, Anchor: 0 }
            && data.Event(Town, PlatinumEventKind.Trigger, 0) is { X: 30, Y: 6, RangeX: 1, RangeY: 3 } && data.Event(Town, PlatinumEventKind.Object, 0) is { X: 5, Y: 5, Type: 2, Script: 8001 },
            "Gen5_FieldsAreReadAtTheirMeasuredOffsets_WarpsInSixteenths");
        check(data.Npc(Town, Ball) is { IsGroundItem: true, EditLock: null } && data.EditRefusal(data.Npc(Town, Ball)) is { } ballWhy && ballWhy.Contains("Poké Ball"), "Gen5_ItemBallsAreGroundItems");
        check(data.Npc(Town, Rail).EditLock is { } rail && rail.Contains("raíl") && data.Event(Town, PlatinumEventKind.Warp, 1).EditLock is { } special && special.Contains("especial")
            && !NdsNpcMovePlanner.IsDraggable(data.Npc(Town, Rail)) && !NdsTrainerVision.CanTurn(data.Npc(Town, Rail)), "Gen5_RailRecordsAndSpecialWarpsAreLocked");
        var layout = data.Layout(Town);
        check(layout is { Width: 2, Height: 1 } && layout.Cell(0, 0).LandDataId == 0 && layout.Cell(1, 0).LandDataId == 1 && data.Layout(House).Cell(0, 0).LandDataId == 2, "Gen5_MatrixIsPresentedAsALayout");
        check(Throws<InvalidDataException>(() => Gen5ZoneEvents.MatrixShape(f.Matrices[..^4])) && Throws<InvalidDataException>(() => Gen5ZoneEvents.Parse(Black, f.Headers, NdsGen5RandomizerChecks.BuildNarc([[9, 0, 0, 0, 1, 0, 0, 0]]), f.Matrices, f.Scripts)),
            "Gen5_InconsistentMatricesAndEventFilesAreRefused");

        // ---- writing: only the accredited fields
        var t = data.Npc(Town, Trainer); var tv = new PlatinumNpcValues(t.X, t.Y, t.Direction, t.Movement);
        byte[] turned = data.ReplaceNpc(Town, Trainer, tv, tv with { Direction = 0, Movement = 14 });
        var after = data.WithEvents(turned);
        check(after.Npc(Town, Trainer) is { Direction: 0, Movement: 14, Sight: 3, Script: 3001 } && turned.Where((b, i) => b != f.Events[i]).Count() == 2, "Gen5_TurningAFixedLookTrainerWritesDirectionAndMovementOnly");
        check(Throws<InvalidOperationException>(() => data.ReplaceNpc(Town, Trainer, tv with { Sight = 3 }, tv with { Sight = 4 })), "Gen5_SightIsNeverWritten");
        check(Throws<InvalidOperationException>(() => data.ReplaceNpc(Town, Ball, PlatinumEvents.Values(data.Npc(Town, Ball)), new(13, 12, 1)))
            && Throws<InvalidOperationException>(() => data.ReplaceNpc(Town, Rail, PlatinumEvents.Values(data.Npc(Town, Rail)), new(21, 4, 1))), "Gen5_ItemBallsAndRailNpcAreNotMoved");
        var plain = data.Npc(Town, Plain);
        check(after.WithEvents(after.ReplaceNpc(Town, Plain, PlatinumEvents.Values(plain), new(4, 3, 2))).Npc(Town, Plain) is { X: 4, Y: 3, Direction: 2 }
            && Throws<ArgumentOutOfRangeException>(() => data.ReplaceNpc(Town, Plain, PlatinumEvents.Values(plain), new(64, 3, 1))), "Gen5_NpcMoveWithinTheMatrix");
        var w = data.Event(Town, PlatinumEventKind.Warp, 0); var wv = PlatinumEvents.EventValues(w);
        var moved = data.WithEvents(data.ReplaceEvent(Town, PlatinumEventKind.Warp, 0, wv, wv with { X = 11, Destination = 2, Anchor = 0 }));
        byte[] raw = Convert.FromHexString(moved.Event(Town, PlatinumEventKind.Warp, 0).Raw);
        check(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(8)) == 11 * 16 + 8 && moved.Event(Town, PlatinumEventKind.Warp, 0).X == 11, "Gen5_WarpPositionIsWrittenInSixteenthsAtTheTileCentre");
        check(Throws<ArgumentOutOfRangeException>(() => data.ReplaceEvent(Town, PlatinumEventKind.Warp, 0, wv, wv with { Destination = 1, Anchor = 0 }))
            && Throws<InvalidOperationException>(() => data.ReplaceEvent(Town, PlatinumEventKind.Warp, 1, PlatinumEvents.EventValues(data.Event(Town, PlatinumEventKind.Warp, 1)), PlatinumEvents.EventValues(data.Event(Town, PlatinumEventKind.Warp, 1)) with { X = 10 })),
            "Gen5_WarpsLeadOnlyToExistingWarps_SpecialOnesAreNotEdited");
        var o = data.Event(Town, PlatinumEventKind.Object, 0); var ov = PlatinumEvents.EventValues(o);
        check(data.WithEvents(data.ReplaceEvent(Town, PlatinumEventKind.Object, 0, ov, ov with { X = 6 })).Event(Town, PlatinumEventKind.Object, 0) is { X: 6, Direction: 4, Script: 8001 }
            && Throws<ArgumentOutOfRangeException>(() => data.ReplaceEvent(Town, PlatinumEventKind.Object, 0, ov, ov with { Facing = 0 })), "Gen5_ObjectsMove_FacingIsKept");
        var tr = data.Event(Town, PlatinumEventKind.Trigger, 0); var trv = PlatinumEvents.EventValues(tr);
        check(data.WithEvents(data.ReplaceEvent(Town, PlatinumEventKind.Trigger, 0, trv, trv with { Width = 2 })).Event(Town, PlatinumEventKind.Trigger, 0) is { RangeX: 2, RangeY: 3, Script: 5 }
            && Throws<ArgumentOutOfRangeException>(() => data.ReplaceEvent(Town, PlatinumEventKind.Trigger, 0, trv, trv with { Width = 33 })), "Gen5_TriggersResizeWithinLimits");
        byte[] script = (byte[])turned.Clone(); int scriptAt = IndexOf(script, Npc(1, 10, 10, movement: 14, type: 1, script: 3001, direction: 0, sight: 3)) + 10; script[scriptAt] ^= 1;
        check(Throws<InvalidDataException>(() => data.WithEvents(script)), "Gen5_ChangingAProtectedByteIsRefused");
        check(Narc.Read(data.WithEvents(turned).Bytes()).Files[2].AsSpan(^4).SequenceEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }), "Gen5_TheTailOfAnEventFileIsKept");

        // ---- terrain from permissions, and graphics
        var permissions = new byte[4 + 8192]; P16(permissions, 0, 32); P16(permissions, 2, 32); permissions[4 + 8 * (2 * 32 + 3) + 6] = 0x81; permissions[4 + 8 * (2 * 32 + 4) + 6] = 0x80;
        byte[] Block(string kind, params byte[][] sections)
        {
            int n = sections.Length, at = 4 + 4 * (n + 1); var b = new List<byte> { (byte)kind[0], (byte)kind[1], (byte)n, 0 };
            foreach (var s in sections) { b.AddRange(BitConverter.GetBytes(at)); at += s.Length; }
            b.AddRange(BitConverter.GetBytes(at)); foreach (var s in sections) b.AddRange(s); return [.. b];
        }
        byte[] model = NdsMapRenderChecks.Bmd(1, [NdsMapRenderChecks.DisplayList([(0x40, [1u]), .. NdsMapRenderChecks.Vtx16((0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)), (0x41, [])])]);
        byte[] buildings = [2, 0, 0, 0, .. new byte[32]];
        var blocks = NdsGen5RandomizerChecks.BuildNarc([Block("WB", model, permissions, buildings), Block("NG", model, [0, 0, 0, 0]), Block("GC", model, permissions, permissions, [0, 0, 0, 0])]);
        var graphics = new Gen5MapGraphics(f.Headers, blocks, NdsGen5RandomizerChecks.BuildNarc([NdsMapRenderChecks.Btx([], [])]));
        var terrain = graphics.Terrain();
        check(terrain.Status(layout, 3, 2) == PlatinumTerrainStatus.Blocked && terrain.Status(layout, 4, 2) == PlatinumTerrainStatus.ClearOfCollision && terrain.Status(layout, 35, 2) == PlatinumTerrainStatus.Unknown
            && terrain.Sample(layout, 3, 2).Reason.Contains("Gen5"), "Gen5_CollisionIsBit0OfByte6_BlocksWithoutPermissionsAreUnknown");
        check(graphics.BlockScene(0, Town, [], out var skipped).Single().Model.Triangles.Count == 2 && skipped.Single().Contains("2 estructuras") && graphics.LandCount == 3 && graphics.HeaderCount == 3,
            "Gen5_TheTerrainModelIsDrawn_BuildingsAreReportedNotDrawn");
        check(Throws<InvalidDataException>(() => Gen5MapGraphics.Block([(byte)'X', (byte)'X', 1, 0, 12, 0, 0, 0, 12, 0, 0, 0])), "Gen5_UnknownBlockKindsAreRefused");

        // ---- ROM, checkpoints, export, restore, detector
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen5-black-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "negro.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source);
            check(Throws<InvalidDataException>(() => Gen5ZoneEvents.Open(rom)) && Throws<Exception>(() => Gen5MapGraphics.Open(rom)), "Gen5_UnaccreditedBlackTablesAreRefused");
            string white2 = Path.Combine(temp, "blanco2.nds"); File.WriteAllBytes(white2, f.Rom("IRDO", 0));
            using (var other = NdsRom.Open(white2)) check(Throws<InvalidDataException>(() => Gen5ZoneEvents.Open(other)) && Throws<InvalidDataException>(() => Gen5MapGraphics.Open(other)), "Gen5_White2IsNotOpenedWithBlacksProfile");
            var session = new NdsEventSession(rom, f.Read());
            string first = Path.Combine(temp, "1.ndsevents.json"); session.Save(session.Preview(Town, Trainer, new(t.X, t.Y, 0, 14)), first);
            var doc = NdsEventSession.ReadDocument(first);
            check(doc is { Game: "IRBO", Revision: 0, Version: 4 }, "Gen5_CheckpointNamesBlack");
            var reopened = new NdsEventSession(rom, f.Read()); reopened.Load(doc, first);
            check(reopened.Data.Npc(Town, Trainer) is { Direction: 0, Movement: 14 } && Equals(reopened.Data.Profile, Black), "Gen5_CheckpointReopens");
            check(Throws<InvalidDataException>(() => new NdsEventSession(rom, f.Read()).Load(doc with { Game = "CPUE", Revision = 1 }, first)), "Gen5_APlatinumCheckpointIsRefusedOnBlack");
            check(Throws<InvalidOperationException>(() => session.PreviewAdd(Town, Npc(9, 1, 1))) && Throws<InvalidOperationException>(() => session.PreviewRemoveAdded(Town, 0)), "Gen5_NoNpcIsAddedOrRemoved");
            var wEvent = session.Data.Event(Town, PlatinumEventKind.Trigger, 0);
            session.Save(session.PreviewEvent(Town, PlatinumEventKind.Trigger, 0, PlatinumEvents.EventValues(wEvent) with { Width = 2 }), Path.Combine(temp, "2.ndsevents.json"));
            string built = Path.Combine(temp, "editado.nds"); rom.Save(built, new Dictionary<int, byte[]> { [rom.File(Black.EventPath).Id] = session.Replacement()! }); session.VerifyExport(built);
            using (var b = NdsRom.Open(built))
                check(b.ReadFile(Black.EventPath).SequenceEqual(session.Data.Bytes()) && rom.Files.Where(x => x.Path != Black.EventPath).All(x => rom.ReadFile(x).AsSpan().SequenceEqual(b.ReadFile(x.Path))), "Gen5_ExportChangesOnlyTheEventArchive");
            check(session.ScriptReplacement() is null, "Gen5_TheScriptArchiveIsNeverReplaced");
            while (session.CanUndo) session.Save(session.PreviewUndo(), Path.Combine(temp, $"undo-{session.Revision}.ndsevents.json"));
            check(session.Replacement() is null && session.Data.Bytes().SequenceEqual(f.Events) && NdsMapCanvasChecks.Sha(source) == sourceHash, "Gen5_RestoringEveryEditGivesTheSourceBack");
            string roms = Path.Combine(temp, "detector");
            string bSource = ProjectDetectorChecks.Write(roms, "Negro.nds", ProjectDetectorChecks.Rom("IRBO", 0)), pSource = ProjectDetectorChecks.Write(roms, "Platino.nds", ProjectDetectorChecks.Rom("CPUE", 1));
            var ok = ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "negro.ndsevents.json", bSource, 1, "IRBO", 0));
            check(ok is { Status: DetectionStatus.Compatible, Kind: DetectedKind.DsEventCheckpoint } && ok.Game.Contains("Negro"), "Gen5_DetectorAcceptsABlackCheckpoint");
            check(ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "cruzado.ndsevents.json", pSource, 1, "IRBO", 0)).Status == DetectionStatus.Unsupported
                && ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "blanco.ndsevents.json", bSource, 1, "IRAS", 0)).Status == DetectionStatus.Unsupported
                && ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "blanco2.ndsevents.json", bSource, 1, "IRDO", 0)).Status == DetectionStatus.Unsupported, "Gen5_DetectorRefusesMixedGames_WhiteOnBlackAndWhite2");
        }
        finally { Directory.Delete(temp, true); }
    }
    private static int IndexOf(byte[] haystack, byte[] needle) { for (int i = 0; i + needle.Length <= haystack.Length; i++) if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i; throw new Exception("not found"); }

    // ------------------------------------------------------------------------------------------------ WPF (off-screen)
    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject => NdsMapCanvasChecks.Children<T>(root).Single(e => AutomationProperties.GetName(e) == name);
    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen5-black-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = Make(); string source = Path.Combine(temp, "negro.nds"); File.WriteAllBytes(source, f.Rom()); string hash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read());
            var view = new NdsEventEditorView(session); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var texts = NdsMapCanvasChecks.Children<TextBlock>(view).ToList(); var buttons = NdsMapCanvasChecks.Children<Button>(view).ToList();
            check(texts.Any(t => t.Text == "Eventos · Negro IRBO/0") && texts.Any(t => t.Text.Contains("En Negro la visión de los entrenadores") && t.Text.Contains("no se crean")), "Gen5Ui_TitleAndIntroNameTheGame");
            string[] structural = ["añadir NPC", "añadir enfermera", "duplicar NPC", "copiar NPC", "cortar NPC", "pegar NPC", "eliminar NPC"];
            check(!buttons.Any(b => structural.Any(s => AutomationProperties.GetName(b) == "Eventos DS · " + s)) && NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single().ContextMenu is null, "Gen5Ui_NoStructuralNpcControls");
            var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single(); maps.SelectedItem = maps.Items[Town]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var tabs = NdsMapCanvasChecks.Children<TabControl>(view).Single(); var tables = NdsMapCanvasChecks.Children<DataGrid>(view).ToList();
            tables[0].SelectedItem = tables[0].Items.Cast<PlatinumEvent>().Single(e => e.Index == Trainer); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var sight = Find<TextBox>(view, "Eventos DS · Alcance de visión");
            check(Find<Border>(view, "Eventos DS · orientación y visión").Visibility == Visibility.Visible && sight.Text == "" && !Find<Button>(view, "Eventos DS · alcance de visión · una casilla más").IsEnabled,
                "Gen5Ui_TrainersTurnButSightIsNotOffered");
            void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Click(Find<Button>(view, "Eventos DS · orientación · mirar hacia arriba"));
            var differences = Find<TextBox>(view, "Eventos DS · diferencias previstas");
            check(differences.Text.Contains("Abajo → Arriba") && differences.Text.Contains("15 → 14") && session.Revision == 0 && NdsMapCanvasChecks.Sha(source) == hash, "Gen5Ui_TurningPreviewsWithoutWriting");
            tables[0].SelectedItem = tables[0].Items.Cast<PlatinumEvent>().Single(e => e.Index == Rail); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var details = NdsMapCanvasChecks.Children<TextBox>(view).Concat<FrameworkElement>(NdsMapCanvasChecks.Children<TextBlock>(view)).Select(e => e is TextBox b ? b.Text : ((TextBlock)e).Text);
            check(details.Any(t => t.Contains("No editable") && t.Contains("raíl")) && Find<Border>(view, "Eventos DS · orientación y visión").Visibility != Visibility.Visible, "Gen5Ui_ARailNpcSaysWhyItIsNotEditable");
            tabs.SelectedIndex = 1; tables[1].SelectedItem = tables[1].Items.Cast<PlatinumEvent>().First(e => e.Kind == PlatinumEventKind.Object); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var facing = NdsMapCanvasChecks.Children<ComboBox>(view).Single(c => AutomationProperties.GetName(c) == "Eventos DS · objeto · dirección de interacción");
            check(!facing.IsEnabled && facing.Items[0]!.ToString()!.Contains("sin interpretar"), "Gen5Ui_ObjectFacingIsShownRawAndNotEditable");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "gen5-black-editor-1100");
        }
        finally { Directory.Delete(temp, true); }
    }

    /// <summary>
    /// The real editor, off-screen, on a private Black copy (read-only): the session, terrain and graphics of the ROM; Route 1 with the graphic layer on, a
    /// trainer selected and its turn previewed. Writes PNG captures; nothing is saved.
    /// </summary>
    private static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { var frame = new System.Windows.Threading.DispatcherFrame(); System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => frame.Continue = false); System.Windows.Threading.Dispatcher.PushFrame(frame); System.Threading.Thread.Sleep(20); }
    }
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    internal static void ViewProbe(string output, string source)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "Negro-copia.nds"); File.Copy(source, copy);
        using (var project = NdsProject.Open(copy, false))
        {
            var session = project.Events; var (terrain, tNote) = project.LoadTerrain(); var (props, pNote) = project.LoadProps(); var (assets, aNote) = project.LoadMapAssets();
            var view = new NdsEventEditorView(session, terrain, tNote, props, pNote, assets, aNote); NdsMapCanvasChecks.Arrange(view, 1280, 860);
            var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single();
            maps.SelectedItem = maps.Items.Cast<PlatinumMap>().Single(m => m.Name == "Route 1" && m.Matrix == 0); NdsMapCanvasChecks.Arrange(view, 1280, 860);
            var models = Find<CheckBox>(view, "Eventos DS · capa Modelos/texturas");
            if (!models.IsEnabled) throw new Exception("The graphic layer is not available on Black");
            models.IsChecked = true; NdsMapCanvasChecks.Arrange(view, 1280, 860);
            var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single(); var trainer = session.Data.Events(maps.SelectedItem is PlatinumMap m ? m.Id : 0).First(e => e.Type == 1 && e.Kind == PlatinumEventKind.Npc && e.EditLock is null);
            var tables = NdsMapCanvasChecks.Children<DataGrid>(view).ToList(); tables[0].SelectedItem = tables[0].Items.Cast<PlatinumEvent>().Single(e => e.Index == trainer.Index);
            NdsMapCanvasChecks.CenterOn(canvas, PlatinumEventKind.Npc, trainer.Index, 12); canvas.Invalidate(); NdsMapCanvasChecks.Arrange(view, 1280, 860);
            Pump(4000); canvas.Invalidate(); NdsMapCanvasChecks.Arrange(view, 1280, 860); Pump(500);
            Click(Find<Button>(view, "Eventos DS · orientación · mirar hacia arriba")); NdsMapCanvasChecks.Arrange(view, 1280, 860); Pump(1500);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1280, 860, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(view);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, "negro-ruta1-entrenador.png"))) png.Save(stream);
            if (session.Revision != 0) throw new Exception("The view probe saved something");
            Console.WriteLine($"PASS Gen5View_RealBlackEditorOpens: terrain {(terrain is null ? "no" : "sí")}, graphics {(assets is null ? "no" : "sí")}, props note: {pNote}");
        }
        File.Delete(copy);
    }

    // ------------------------------------------------------------------------------------------------ real ROM (private copy)
    /// <summary>
    /// A private Black IRBO/0 copy: tables, fingerprint and census; terrain and graphics open; in Nuvema Town the door of one neighbour's house leads into the
    /// other's; on Route 1 a fixed-look NPC and a fixed-look trainer look up and a hidden item moves one tile; an item ball is not moved; a trigger is
    /// widened and restored (technical only). Save, reopen, export, reopen the exported ROM, compare every other file, restore and compare hashes; MANUAL.txt.
    /// </summary>
    internal static void Probe(string output, string source)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "Negro-copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        var manual = new List<string> { "Prueba jugable (Pokémon Negro IRBO/0, copia privada). Respalda la partida antes. Las cuatro pruebas se hacen con una partida NUEVA, en orden: la 1 en Pueblo Arcilla tras recibir el primer Pokémon; las 2 a 4 en la Ruta 1. Ninguna edición toca tu casa ni el laboratorio de la Profesora Encina." };
        string built = Path.Combine(output, "Negro-eventos.nds"), restored = Path.Combine(output, "Negro-restaurado.nds");
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var data = events.Data;
            if (data is not Gen5ZoneEvents gen5 || !Equals(data.Profile, Black)) throw new Exception("The copy did not open with the Black profile");
            var files = data.Maps.GroupBy(m => m.EventFile).Select(g => g.First().Id).ToList(); var all = files.SelectMany(data.Events).ToList();
            var warps = all.Where(e => e.Kind == PlatinumEventKind.Warp).ToList();
            int resolved = warps.Count(w => data.WarpTargetExists(w.Destination, w.Anchor));
            if (data.Maps.Count != 427 || all.Count(e => e.Kind == PlatinumEventKind.Npc) != 2257 || warps.Count != 886 || resolved != 880) throw new Exception("Census differs");
            Console.WriteLine($"PASS Gen5Real_ProfileAndCensus: {data.Maps.Count} maps, {all.Count(e => e.Kind == PlatinumEventKind.Npc)} NPC ({all.Count(e => e.IsGroundItem)} item balls), {warps.Count} warps ({resolved} resolve, {warps.Count - resolved} special), {all.Count(e => e.EditLock is not null)} locked records");
            var (terrain, tNote) = project.LoadTerrain(); var (assets, aNote) = project.LoadMapAssets();
            if (terrain is null || assets is null) throw new Exception($"Terrain or graphics did not open: {tNote} {aNote}");
            Console.WriteLine($"PASS Gen5Real_TerrainAndGraphicsOpen; props: {project.LoadProps().Note}");

            var saves = new List<string>();
            void Save(NdsEventProposal p, string name) { string file = Path.Combine(output, name); events.Save(p, file); project.EventChanged(); saves.Add(file); }
            PlatinumMap Named(string name, int matrix) => data.Maps.Single(m => m.Name == name && m.Matrix == matrix);
            int step = 1;
            var nuvema = Named("Nuvema Town", 0); var doors = data.Events(nuvema.Id).Where(e => e.Kind == PlatinumEventKind.Warp).ToList();
            // 390/391 are the player's house (the game starts in 391), 396 is the professor's lab; 392 and 394 are the two neighbours' houses (each with a 2F).
            var from = doors.Single(d => d.Destination == 392); var to = doors.Single(d => d.Destination == 394);
            Save(events.PreviewEvent(nuvema.Id, PlatinumEventKind.Warp, from.Index, PlatinumEvents.EventValues(from) with { Destination = to.Destination, Anchor = to.Anchor }), $"{step}-warp.ndsevents.json");
            manual.Add($"{step++}. Pueblo Arcilla: hay tres casas además del laboratorio. La de abajo a la izquierda (puerta en X {from.X}, Y {from.Y}) ahora lleva al interior de la casa de abajo a la derecha (la de la puerta en X {to.X}, Y {to.Y}). Entra por la puerta de abajo a la izquierda: debes aparecer dentro de la casa de abajo a la derecha. Al salir apareces ante la puerta de abajo a la derecha (el warp de vuelta no cambia).");

            var route1 = Named("Route 1", 0); var layout = data.Layout(route1.Id);
            var look = data.Events(route1.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0 && e.Flag == 0 && e.EditLock is null && !e.IsGroundItem && e.Movement == 15 && e.Direction == 1);
            Save(events.Preview(route1.Id, look.Index, new(look.X, look.Y, 0, 14)), $"{step}-npc.ndsevents.json");
            manual.Add($"{step++}. Ruta 1, NPC {look.Index} (X {look.X}, Y {look.Y}, script {look.Script}): mira hacia arriba en lugar de abajo (movimiento «mirar fijo» 15 → 14). Míralo antes de hablarle; al hablarle se gira hacia ti.");
            var hidden = data.Events(route1.Id).Single(e => e.Kind == PlatinumEventKind.Object && e.Type == 2);
            int dx = terrain.Status(layout, hidden.X + 1, hidden.Y) == PlatinumTerrainStatus.ClearOfCollision ? 1 : terrain.Status(layout, hidden.X - 1, hidden.Y) == PlatinumTerrainStatus.ClearOfCollision ? -1 : 0;
            if (dx == 0) throw new Exception("No free tile beside the hidden item of Route 1");
            Save(events.PreviewEvent(route1.Id, PlatinumEventKind.Object, hidden.Index, PlatinumEvents.EventValues(hidden) with { X = hidden.X + dx }), $"{step}-objeto.ndsevents.json");
            manual.Add($"{step++}. Ruta 1, objeto oculto (script {hidden.Script}): de X {hidden.X}, Y {hidden.Y} a X {hidden.X + dx} (una casilla a la {(dx > 0 ? "derecha" : "izquierda")}). Pulsa A en la casilla nueva; la anterior ya no debe dar el objeto.");
            var trainer = data.Events(route1.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 1 && e.Movement == 15 && e.Direction == 1 && e.EditLock is null);
            Save(events.Preview(route1.Id, trainer.Index, new(trainer.X, trainer.Y, 0, 14)), $"{step}-entrenador.ndsevents.json");
            manual.Add($"{step++}. Ruta 1, entrenador NPC {trainer.Index} (script {trainer.Script}, X {trainer.X}, Y {trainer.Y}): mira hacia arriba en lugar de abajo (movimiento 15 → 14). Su alcance de visión no se ha tocado. Si aún no lo has vencido, comprueba que mira hacia arriba y que te ve al pasar por encima de él, no por debajo.");

            var ball = data.Events(route1.Id).First(e => e.IsGroundItem);
            if (!Throws<InvalidOperationException>(() => data.ReplaceNpc(route1.Id, ball.Index, PlatinumEvents.Values(ball), new(ball.X + 1, ball.Y, ball.Direction)))) throw new Exception("An item ball was moved");
            if (!Throws<InvalidOperationException>(() => events.PreviewAdd(route1.Id, new byte[36]))) throw new Exception("Black accepted a new NPC");
            var trig = data.Events(route1.Id).First(e => e.Kind == PlatinumEventKind.Trigger && e.EditLock is null);
            Save(events.PreviewEvent(route1.Id, PlatinumEventKind.Trigger, trig.Index, PlatinumEvents.EventValues(trig) with { Width = trig.RangeX + 1 }), "5-activador-tecnico.ndsevents.json");
            Save(events.PreviewUndo(), "6-activador-restaurado.ndsevents.json");
            Console.WriteLine("PASS Gen5Real_ItemBallsAndNewNpcAreRefused_TriggerEditedAndRestored");
            if (gen5.EditableHash() != Black.EditableEventsSha256) throw new Exception("An edit changed a protected byte");

            project.Export(built);
            using (var reopened = NdsProject.Open(saves[^1], false))
            {
                if (!reopened.Events.Data.Bytes().SequenceEqual(events.Data.Bytes())) throw new Exception("Checkpoint did not persist");
                while (reopened.Events.CanUndo) { reopened.Events.Save(reopened.Events.PreviewUndo(), Path.Combine(output, $"restaurar-{reopened.Events.Revision}.ndsevents.json")); reopened.EventChanged(); }
                reopened.Export(restored);
            }
            using (var rebuilt = NdsProject.Open(built, false))
                if (!rebuilt.Events.Data.Bytes().SequenceEqual(events.Data.Bytes())) throw new Exception("Exported ROM did not persist");
            using var baseline = NdsRom.Open(copy); using var builtRom = NdsRom.Open(built);
            foreach (var file in baseline.Files) if (file.Path != Black.EventPath && !baseline.ReadFile(file).SequenceEqual(builtRom.ReadFile(file.Path))) throw new Exception("Unrelated file changed: " + file.Path);
            if (!baseline.ReadArm9().SequenceEqual(builtRom.ReadArm9())) throw new Exception("ARM9 changed");
            Console.WriteLine($"PASS Gen5Real_SaveReopenExportReopenAndUndo: {saves.Count} checkpoints; exported ROM reopens in the editor; ARM9 and every other NDS file byte-exact");
        }
        File.Delete(copy);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(restored))) != hash)
            throw new Exception("Source changed or whole-ROM restoration differs");
        manual.Add($"{manual.Count}. Negro-restaurado.nds es idéntica (SHA-256) a la copia original; no hace falta probarla en juego.");
        File.WriteAllLines(Path.Combine(output, "MANUAL.txt"), manual);
        Console.WriteLine("PASS Gen5Real_OriginalAndRestoredRomHashesMatch; gameplay NOT tested");
    }
    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}
