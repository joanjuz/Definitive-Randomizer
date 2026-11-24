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
/// The map and event editor on Pokémon HeartGold USA (IPKE/0): its own header layout (Johto), land_data with a background-sound section, indoor prop models,
/// item balls placed as type-0 NPC, trainer sight up to 7 and background-object facing up to 4. Synthetic checks of the adapter, off-screen checks of the
/// editor with a HeartGold session, and a probe on a private HeartGold copy (measure, edit, checkpoint, export, reopen, restore). No gameplay.
/// </summary>
internal static class NdsGen4HeartGoldChecks
{
    private static readonly Gen4EventProfile HeartGold = Gen4EventProfile.HeartGold, Platinum = Gen4EventProfile.Platinum, Diamond = Gen4EventProfile.Diamond;
    /// <summary>The vision fixture (trainers, a ground item, ordinary NPC) in HeartGold form: Johto headers, and its ground item as a type-0 NPC with script 7000.</summary>
    internal sealed record Fixture(byte[] Headers, byte[] Names, byte[] Events, byte[] Matrices, byte[] Scripts)
    {
        internal PlatinumEvents Read() => PlatinumEvents.Parse(HeartGold, Headers, Names, Events, Matrices, Scripts, ["Pueblo Primavera", "Ruta 29"]);
        internal byte[] Rom(string code = "IPKE", byte revision = 0) => NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]>
        { [HeartGold.EventPath] = Events, [HeartGold.MapNamesPath] = Names, [HeartGold.MapMatrixPath] = Matrices, [HeartGold.ScriptPath] = Scripts, ["untouched.bin"] = [7, 1, 99, 18] }, code, revision);
    }
    internal const int Map = NdsTrainerVisionChecks.Map, Trainer = NdsTrainerVisionChecks.Trainer, ItemBall = NdsTrainerVisionChecks.Item;

    /// <summary>Moves matrix (+2 → +4) and scripts (+4 → +6) to the HeartGold places, and fills the bytes HeartGold has there with values the reader must ignore.</summary>
    internal static byte[] ToJohto(byte[] sinnoh)
    {
        byte[] johto = (byte[])sinnoh.Clone();
        for (int at = 0; at < johto.Length; at += 24)
        {
            ushort matrix = BinaryPrimitives.ReadUInt16LittleEndian(sinnoh.AsSpan(at + 2)), scripts = BinaryPrimitives.ReadUInt16LittleEndian(sinnoh.AsSpan(at + 4));
            johto[at] = 0xFF; johto[at + 1] = 0; BinaryPrimitives.WriteUInt16LittleEndian(johto.AsSpan(at + 2), 0xFFFF);
            BinaryPrimitives.WriteUInt16LittleEndian(johto.AsSpan(at + 4), matrix); BinaryPrimitives.WriteUInt16LittleEndian(johto.AsSpan(at + 6), scripts);
        }
        return johto;
    }
    internal static Fixture Make()
    {
        var v = NdsTrainerVisionChecks.Make(); var archive = Narc.Read(v.Events);
        byte[] file = (byte[])archive.Files[1].Clone(); int itemType = 8 + ItemBall * 32 + 6;
        if (BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(itemType)) != 3 || BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(itemType + 4)) != 7000) throw new Exception("The vision fixture changed");
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(itemType), 0);
        var files = archive.Files.ToArray(); files[1] = file;
        return new(ToJohto(v.Headers), v.Names, NdsGen5RandomizerChecks.BuildNarc(files), v.Matrices, v.Scripts);
    }

    /// <summary>A HeartGold land_data member: four sizes, u16 0x1234, u16 sound length, the sound bytes, 0x800 attributes, then props, model and BDHC.</summary>
    internal static byte[] Land(Func<int, int, ushort> attribute, int sound, byte[] props, byte[] model)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(0x800u); w.Write((uint)props.Length); w.Write((uint)model.Length); w.Write(0u); w.Write((ushort)0x1234); w.Write((ushort)sound); w.Write(new byte[sound]);
        for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++) w.Write(attribute(x, z));
        w.Write(props); w.Write(model); return stream.ToArray();
    }
    private static byte[] Prop(int model)
    {
        var p = new byte[48]; BinaryPrimitives.WriteInt32LittleEndian(p, model);
        for (int i = 0; i < 3; i++) BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(28 + i * 4), 4096); return p;
    }

    internal static void Run(Action<bool, string> check)
    {
        // ---- profile: identity and every value that differs from Diamond and Platinum
        check(Gen4EventProfile.For("IPKE", 0) == HeartGold && Gen4EventProfile.For("IPGE", 0) is null && Gen4EventProfile.For("IPKE", 1) is null && Gen4EventProfile.For("IPKS", 0) is null,
            "Hgss_OnlyHeartGoldUsaRev0HasAProfile_NotSoulSilverOrOtherRevisions");
        check(HeartGold.HeaderLayout == Gen4HeaderLayout.Johto && Platinum.HeaderLayout == Gen4HeaderLayout.Sinnoh && Diamond.HeaderLayout == Gen4HeaderLayout.Sinnoh
            && Gen4HeaderLayout.Johto is { Area: 1, Matrix: 4, Scripts: 6, Events: 16, Label: 18 } && Gen4HeaderLayout.Sinnoh is { Area: 0, Matrix: 2, Scripts: 4, Events: 16, Label: 18 },
            "Hgss_HeaderLayoutIsItsOwn");
        check(HeartGold is { HeaderOffset: 0xF6BE0, HeaderCount: 540, EventPath: "a/0/3/2", ScriptPath: "a/0/1/2", LandDataPath: "a/0/6/5", MapMatrixPath: "a/0/4/1", LocationNamesText: 279,
            ItemBallScriptArchive: 141, HiddenItemScriptArchive: 145, MaxObjectFacing: 4, MaxTrainerSight: 7, SupportsNpcCreation: false, LandDataHasSoundSection: true, ItemBallsAreScriptedNpc: true, IndoorBuildModelPath: "a/1/4/8" },
            "Hgss_PathsTablesAndLimitsAreMeasured");
        check(new[] { Platinum, Diamond }.All(p => p is { MaxTrainerSight: 6, LandDataHasSoundSection: false, ItemBallsAreScriptedNpc: false, IndoorBuildModelPath: null }
            && p.MapMatrixPath == Gen4EventProfile.MatrixPath && p.MapNamesPath == Gen4EventProfile.NamesPath && p.BuildModelPath == PlatinumMapAssets.BuildModelPath && p.AreaDataPath == PlatinumMapAssets.AreaDataPath),
            "Hgss_DiamondAndPlatinumKeepEveryPreviousValue");
        check(Gen4EventProfile.SupportedLabels.Contains("HeartGold IPKE/0") && Gen4EventProfile.All.Count == 3, "Hgss_SupportedGamesNameHeartGold");

        // ---- headers: the same maps through the Johto layout; the Sinnoh layout would read other tables
        var sinnoh = NdsTrainerVisionChecks.Make(); var f = Make(); var hg = f.Read(); var pt = sinnoh.Read();
        check(hg.Maps.Select(m => (m.Matrix, m.ScriptFile, m.EventFile, m.Name)).SequenceEqual(pt.Maps.Select(m => (m.Matrix, m.ScriptFile, m.EventFile, m.Name.Replace("Zona A", "Pueblo Primavera").Replace("Zona B", "Ruta 29")))),
            "Hgss_JohtoHeadersReadTheSameMatrixScriptsAndEvents");
        check(Throws<InvalidDataException>(() => PlatinumEvents.Parse(Platinum, f.Headers, f.Names, f.Events, f.Matrices, f.Scripts)), "Hgss_ReadingJohtoHeadersWithTheSinnohLayoutIsRefused");

        // ---- item balls: type-0 NPC with a 7000+ script are ground items in HeartGold only
        var ball = hg.Npc(Map, ItemBall);
        check(ball is { Type: 0, Script: 7000, IsGroundItem: true, ScriptedGroundItem: true } && HeartGold.IsGroundItem(ball) && ball.Description.Contains("Poké Ball"), "Hgss_ItemBallIsAGroundItem");
        var sameInPlatinum = PlatinumEvents.Parse(Platinum, sinnoh.Headers, sinnoh.Names, f.Events, sinnoh.Matrices, sinnoh.Scripts).Npc(Map, ItemBall);
        check(!sameInPlatinum.IsGroundItem && pt.Npc(Map, ItemBall) is { Type: 3, IsGroundItem: true, ScriptedGroundItem: false }, "Hgss_PlatinumStillUsesType3Only");
        check(!NdsNpcMovePlanner.IsDraggable(ball) && !NdsTrainerVision.CanTurn(ball) && HeartGold.SightRefusal(ball) is { } why && why.Contains("objeto de suelo"), "Hgss_ItemBallIsNeitherDraggedNorTurned");
        check(Throws<InvalidOperationException>(() => hg.ReplaceNpc(Map, ItemBall, PlatinumEvents.Values(ball), new(ball.X + 1, ball.Y, ball.Direction))), "Hgss_TheCoreRefusesToMoveAnItemBall");

        // ---- trainers: sight up to 7 (the HeartGold maximum), fixed look 14–17 as in Diamond and Platinum
        var trainer = hg.Npc(Map, Trainer); var withVision = new PlatinumNpcValues(trainer.X, trainer.Y, trainer.Direction, trainer.Movement, trainer.Sight);
        byte[] seven = hg.ReplaceNpc(Map, Trainer, withVision, withVision with { Sight = 7 });
        check(hg.WithEvents(seven).Npc(Map, Trainer).Sight == 7 && HeartGold.SightRefusal(trainer) is null, "Hgss_TrainerSight7IsAccepted");
        check(Throws<ArgumentOutOfRangeException>(() => hg.ReplaceNpc(Map, Trainer, withVision, withVision with { Sight = 8 }))
            && Throws<ArgumentOutOfRangeException>(() => pt.ReplaceNpc(Map, Trainer, withVision, withVision with { Sight = 7 })), "Hgss_Sight8AndPlatinumSight7AreRefused");
        byte[] turned = hg.ReplaceNpc(Map, Trainer, withVision, withVision with { Direction = 0, Movement = PlatinumTrainerVision.FixedLookFor(0) });
        check(hg.WithEvents(turned).Npc(Map, Trainer) is { Direction: 0, Movement: 14 } && turned.SequenceEqual(PlatinumEvents.Parse(Platinum, sinnoh.Headers, sinnoh.Names, f.Events, sinnoh.Matrices, sinnoh.Scripts).ReplaceNpc(Map, Trainer, withVision, withVision with { Direction = 0, Movement = 14 })), "Hgss_TurningATrainerWritesTheSameBytesAsPlatinum");

        // ---- background objects (facing 0–4), warps and triggers: the shared writer with the HeartGold limits
        var e = NdsEventChecks.Make(); var hgEvents = PlatinumEvents.Parse(HeartGold, ToJohto(e.Headers), e.Names, e.Events, e.Matrices, e.Scripts);
        var obj = hgEvents.Event(0, PlatinumEventKind.Object, 0); var ov = PlatinumEvents.EventValues(obj);
        check(hgEvents.ReplaceEvent(0, PlatinumEventKind.Object, 0, ov, ov with { Facing = 4 }).Length == e.Events.Length
            && Throws<ArgumentOutOfRangeException>(() => hgEvents.ReplaceEvent(0, PlatinumEventKind.Object, 0, ov, ov with { Facing = 5 })), "Hgss_ObjectFacingUpTo4");
        var trig = hgEvents.Event(0, PlatinumEventKind.Trigger, 0); var tv = PlatinumEvents.EventValues(trig);
        check(hgEvents.ReplaceEvent(0, PlatinumEventKind.Trigger, 0, tv, tv with { Width = 2 }).SequenceEqual(e.Read().ReplaceEvent(0, PlatinumEventKind.Trigger, 0, tv, tv with { Width = 2 })), "Hgss_TriggerEditWritesTheSameBytes");
        check(hgEvents.Script(0, 7000).Archive == 141 && hgEvents.Script(0, 8000).Archive == 145, "Hgss_ItemScriptsUseHeartGoldArchives");
        byte[] record = PlatinumEvents.NewNpcRecord(hgEvents.Npc(0, 0), 99, 33, 0, 0, 1, 1);
        check(hgEvents.ValidateNewNpc(0, record) is [var only] && only.Contains("HeartGold IPKE/0") && Throws<InvalidOperationException>(() => hgEvents.AddNpc(0, record))
            && Throws<InvalidOperationException>(() => hgEvents.WithAppendedScript(0, PlatinumScriptFile.NurseScript(99), out _)), "Hgss_NoNewNpcAndNoScripts");

        // ---- land_data with the background-sound section
        Func<int, int, ushort> pattern = (x, z) => (ushort)(x == 3 && z == 4 ? 0x8000 : x + z);
        byte[] land = Land(pattern, 8, Prop(1), NdsMapRenderChecks.Bmd(1, [Quad()]));
        var terrain = PlatinumTerrain.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([land]), soundSection: true);
        check(terrain.Attribute(0, 3, 4) == 0x8000 && terrain.Attribute(0, 5, 6) == 11 && terrain.PropBytes(0) == 48, "Hgss_LandTerrainStartsAfterTheSoundSection");
        var props = PlatinumLandProps.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([land]), soundSection: true);
        check(props.Props(0) is [{ ModelId: 1 }] && props.TrailingBytes(0) == 0, "Hgss_LandPropsStartAfterTheTerrain");
        check(Throws<InvalidDataException>(() => PlatinumTerrain.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([land])))
            && Throws<InvalidDataException>(() => PlatinumTerrain.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([PlatinumTerrainChecks.Land(pattern)]), soundSection: true)), "Hgss_LandLayoutsAreNotMixed");
        byte[] badSignature = (byte[])land.Clone(); badSignature[16] = 0x35;
        byte[] lyingSound = (byte[])land.Clone(); lyingSound[18] = 9;
        check(Throws<InvalidDataException>(() => PlatinumTerrain.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([badSignature]), true))
            && Throws<InvalidDataException>(() => PlatinumLandProps.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([lyingSound]), true)), "Hgss_ABadSoundSectionIsRefused");

        // ---- graphics: area at +1; indoor areas (+6 = 0) load the indoor prop models
        byte[] headers = new byte[48]; headers[0] = 0xFF; headers[1] = 1; headers[24] = 0xFF; headers[25] = 0;
        byte[] outdoorArea = [0, 0, 0, 0, 0xFF, 0xFF, 1, 2], indoorArea = [1, 0, 0, 0, 0xFF, 0xFF, 0, 1];
        byte[] lists = NdsGen5RandomizerChecks.BuildNarc([new byte[] { 1, 0, 0, 0 }, new byte[] { 1, 0, 1, 0 }]);
        byte[] btx = NdsMapRenderChecks.Btx([], []);
        byte[] outdoor = NdsGen5RandomizerChecks.BuildNarc([NdsMapRenderChecks.Bmd(1, [Tri()])]), indoor = NdsGen5RandomizerChecks.BuildNarc([NdsMapRenderChecks.Bmd(1, [Quad()]), NdsMapRenderChecks.Bmd(1, [Quad()])]);
        var assets = new PlatinumMapAssets(HeartGold, headers, NdsGen5RandomizerChecks.BuildNarc([indoorArea, outdoorArea]), lists, NdsGen5RandomizerChecks.BuildNarc([btx, btx]),
            NdsGen5RandomizerChecks.BuildNarc([btx]), outdoor, NdsGen5RandomizerChecks.BuildNarc([land]), indoor);
        check(assets.Area(0) is { Area: 1, IndoorModels: false, LightArchive: 2 } && assets.Area(1) is { Area: 0, IndoorModels: true, LightArchive: 1, PropModels: [1] }, "Hgss_AreaComesFromByte1AndSaysIndoorOrOutdoor");
        check(assets.PropModel(0, false).Single().Triangles.Count == 1 && assets.PropModel(1, true).Single().Triangles.Count == 2 && assets.IndoorBuildModelCount == 2, "Hgss_IndoorAreasUseTheIndoorModels");
        check(assets.LandModel(0).Single().Triangles.Count == 2, "Hgss_TheLandModelIsFoundAfterTheSoundSection");
        var indoorScene = assets.BlockScene(0, 1, [new PlatinumMapProp(0, 1, 0, 0, 0, 0, 0, 0, 4096, 4096, 4096, 0)], out var skipped);
        check(indoorScene.Count == 2 && skipped.Count == 0 && indoorScene[1].Model.Triangles.Count == 2, "Hgss_AnIndoorBlockDrawsTheIndoorModel");
        check(Throws<ArgumentException>(() => new PlatinumMapAssets(Platinum, headers, NdsGen5RandomizerChecks.BuildNarc([indoorArea]), lists, NdsGen5RandomizerChecks.BuildNarc([btx]), NdsGen5RandomizerChecks.BuildNarc([btx]), outdoor, NdsGen5RandomizerChecks.BuildNarc([land]), indoor))
            && Throws<ArgumentException>(() => new PlatinumMapAssets(HeartGold, headers, NdsGen5RandomizerChecks.BuildNarc([indoorArea]), lists, NdsGen5RandomizerChecks.BuildNarc([btx]), NdsGen5RandomizerChecks.BuildNarc([btx]), outdoor, NdsGen5RandomizerChecks.BuildNarc([land]), null)),
            "Hgss_TheIndoorArchiveBelongsToItsProfileOnly");

        // ---- ROM, checkpoints, export, restore, detector
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen4-hgss-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "heartgold.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source);
            check(Throws<InvalidDataException>(() => PlatinumEvents.Open(rom)) && Throws<Exception>(() => PlatinumTerrain.Open(rom)) && Throws<Exception>(() => PlatinumMapAssets.Open(rom)),
                "Hgss_UnaccreditedHeartGoldTablesAreRefused");
            string soulSilver = Path.Combine(temp, "soulsilver.nds"); File.WriteAllBytes(soulSilver, f.Rom("IPGE", 0));
            using (var other = NdsRom.Open(soulSilver)) check(Throws<InvalidDataException>(() => PlatinumEvents.Open(other)) && Throws<InvalidDataException>(() => PlatinumLandProps.Open(other)), "Hgss_SoulSilverIsNotOpenedWithHeartGoldsProfile");

            var session = new NdsEventSession(rom, f.Read());
            string first = Path.Combine(temp, "1.ndsevents.json"); session.Save(session.Preview(Map, Trainer, withVision with { Direction = 0, Movement = 14, Sight = 7 }), first);
            var doc = NdsEventSession.ReadDocument(first);
            check(doc is { Game: "IPKE", Revision: 0, Version: 4 }, "Hgss_CheckpointNamesHeartGold");
            var reopened = new NdsEventSession(rom, f.Read()); reopened.Load(doc, first);
            check(reopened.Data.Npc(Map, Trainer) is { Direction: 0, Sight: 7 } && reopened.Data.Profile == HeartGold, "Hgss_CheckpointReopens");
            check(Throws<InvalidDataException>(() => new NdsEventSession(rom, f.Read()).Load(doc with { Game = "ADAE", Revision = 5 }, first)), "Hgss_ADiamondCheckpointIsRefusedOnHeartGold");
            string built = Path.Combine(temp, "editado.nds"); rom.Save(built, new Dictionary<int, byte[]> { [rom.File(HeartGold.EventPath).Id] = session.Replacement()! }); session.VerifyExport(built);
            using (var b = NdsRom.Open(built))
                check(b.ReadFile(HeartGold.EventPath).SequenceEqual(session.Data.Bytes()) && rom.Files.Where(x => x.Path != HeartGold.EventPath).All(x => rom.ReadFile(x).AsSpan().SequenceEqual(b.ReadFile(x.Path))), "Hgss_ExportChangesOnlyTheEventArchive");
            while (session.CanUndo) session.Save(session.PreviewUndo(), Path.Combine(temp, $"undo-{session.Revision}.ndsevents.json"));
            check(session.Replacement() is null && session.Data.Bytes().SequenceEqual(f.Events) && NdsMapCanvasChecks.Sha(source) == sourceHash, "Hgss_RestoringEveryEditGivesTheSourceBack");

            string roms = Path.Combine(temp, "detector");
            string hSource = ProjectDetectorChecks.Write(roms, "HeartGold.nds", ProjectDetectorChecks.Rom("IPKE", 0)), dSource = ProjectDetectorChecks.Write(roms, "Diamante.nds", ProjectDetectorChecks.Rom("ADAE", 5));
            var ok = ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "hg.ndsevents.json", hSource, 1, "IPKE", 0));
            check(ok is { Status: DetectionStatus.Compatible, Kind: DetectedKind.DsEventCheckpoint } && ok.Game.Contains("HeartGold"), "Hgss_DetectorAcceptsAHeartGoldCheckpoint");
            check(ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "cruzado.ndsevents.json", dSource, 1, "IPKE", 0)).Status == DetectionStatus.Unsupported
                && ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "ss.ndsevents.json", hSource, 1, "IPGE", 0)).Status == DetectionStatus.Unsupported, "Hgss_DetectorRefusesMixedGamesAndSoulSilver");
        }
        finally { Directory.Delete(temp, true); }
    }
    private static byte[] Quad() => NdsMapRenderChecks.DisplayList([(0x40, [1u]), .. NdsMapRenderChecks.Vtx16((0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)), (0x41, [])]);
    private static byte[] Tri() => NdsMapRenderChecks.DisplayList([(0x40, [0u]), .. NdsMapRenderChecks.Vtx16((0, 0, 0), (1, 0, 0), (1, 0, 1)), (0x41, [])]);

    // ------------------------------------------------------------------------------------------------ WPF (off-screen)
    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject => NdsMapCanvasChecks.Children<T>(root).Single(e => AutomationProperties.GetName(e) == name);
    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen4-hgss-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = Make(); string source = Path.Combine(temp, "heartgold.nds"); File.WriteAllBytes(source, f.Rom()); string hash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read());
            var view = new NdsEventEditorView(session, NdsTrainerVisionChecks.Terrain(), null); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var texts = NdsMapCanvasChecks.Children<TextBlock>(view).ToList(); var buttons = NdsMapCanvasChecks.Children<Button>(view).ToList();
            check(texts.Any(t => t.Text == "Eventos · HeartGold IPKE/0") && texts.Any(t => t.Text.Contains("En HeartGold no se crean")), "HgssUi_TitleAndIntroNameTheGame");
            string[] structural = ["añadir NPC", "añadir enfermera", "duplicar NPC", "copiar NPC", "cortar NPC", "pegar NPC", "eliminar NPC"];
            check(!buttons.Any(b => structural.Any(s => AutomationProperties.GetName(b) == "Eventos DS · " + s)) && texts.Any(t => t.Text.StartsWith("NPC: en HeartGold IPKE/0 solo se mueven"))
                && NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single().ContextMenu is null, "HgssUi_NoStructuralNpcControls");
            var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single(); maps.SelectedItem = maps.Items[Map]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var tabs = NdsMapCanvasChecks.Children<TabControl>(view).Single(); var tables = NdsMapCanvasChecks.Children<DataGrid>(view).ToList();
            check(tables[1].Items.Cast<PlatinumEvent>().Any(e => e.Kind == PlatinumEventKind.Npc && e.Index == ItemBall), "HgssUi_ItemBallIsListedWithTheObjects");
            tabs.SelectedIndex = 1; tables[1].SelectedItem = tables[1].Items.Cast<PlatinumEvent>().Single(e => e.Kind == PlatinumEventKind.Npc && e.Index == ItemBall); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            check(Find<Border>(view, "Eventos DS · orientación y visión").Visibility != Visibility.Visible, "HgssUi_AnItemBallHasNoFacingOrSightControls");

            tabs.SelectedIndex = 0; tables[0].SelectedItem = tables[0].Items.Cast<PlatinumEvent>().Single(e => e.Index == Trainer); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            Button Btn(string name) => Find<Button>(view, "Eventos DS · " + name); var sight = Find<TextBox>(view, "Eventos DS · Alcance de visión");
            void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            for (int i = 0; i < 6; i++) Click(Btn("alcance de visión · una casilla más"));
            check(sight.Text == "7" && !Btn("alcance de visión · una casilla más").IsEnabled, "HgssUi_SightStopsAtSeven");
            var differences = Find<TextBox>(view, "Eventos DS · diferencias previstas");
            check(differences.Text.Contains("3 → 7") && session.Revision == 0 && NdsMapCanvasChecks.Sha(source) == hash, "HgssUi_EditingPreviewsWithoutWriting");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "gen4-heartgold-editor-1100");
        }
        finally { Directory.Delete(temp, true); }
    }

    // ------------------------------------------------------------------------------------------------ real ROM (private copy)
    /// <summary>
    /// Measures a private HeartGold copy and compares it with the profile (CRC32 of every table, the three event fingerprints, the census). With
    /// <paramref name="output"/> null it only measures. Otherwise: open terrain, props and graphics; in New Bark Town retarget warp 0 and widen trigger 0;
    /// on Route 29 turn a fixed-look NPC; on Route 30 move a hidden item and turn a fixed-look trainer; refuse moving an item ball and creating an NPC.
    /// Save, reopen the checkpoint, export, reopen the exported ROM, compare every other file, restore and compare hashes; write MANUAL.txt. No gameplay.
    /// </summary>
    internal static void Probe(string? output, string source)
    {
        using (var rom = NdsRom.Open(source))
        {
            if (Gen4EventProfile.For(rom.Header) != HeartGold) throw new Exception($"Not HeartGold IPKE/0: {rom.Header.GameCode}/{rom.Header.Version}");
            byte[] arm9 = rom.ReadArm9(), headers = arm9.AsSpan(HeartGold.HeaderOffset, HeartGold.HeaderCount * 24).ToArray();
            var measured = new (string Name, uint Value, uint Expected)[]
            {
                ("ARM9", Gen5Data.Crc32(arm9), HeartGold.Arm9Crc32), ("headers", Gen5Data.Crc32(headers), HeartGold.HeadersCrc32),
                ("names", Gen5Data.Crc32(rom.ReadFile(HeartGold.MapNamesPath)), HeartGold.NamesCrc32), ("matrices", Gen5Data.Crc32(rom.ReadFile(HeartGold.MapMatrixPath)), HeartGold.MatricesCrc32),
                ("scripts", Gen5Data.Crc32(rom.ReadFile(HeartGold.ScriptPath)), HeartGold.ScriptsCrc32), ("events", Gen5Data.Crc32(rom.ReadFile(HeartGold.EventPath)), HeartGold.EventsCrc32),
                ("land", Gen5Data.Crc32(rom.ReadFile(HeartGold.LandDataPath)), HeartGold.LandDataCrc32), ("area", Gen5Data.Crc32(rom.ReadFile(HeartGold.AreaDataPath)), HeartGold.AreaDataCrc32),
                ("areaBuild", Gen5Data.Crc32(rom.ReadFile(HeartGold.AreaBuildPath)), HeartGold.AreaBuildCrc32), ("areaTextures", Gen5Data.Crc32(rom.ReadFile(HeartGold.AreaBuildTexturesPath)), HeartGold.AreaBuildTexturesCrc32),
                ("mapTextures", Gen5Data.Crc32(rom.ReadFile(HeartGold.MapTexturesPath)), HeartGold.MapTexturesCrc32), ("models", Gen5Data.Crc32(rom.ReadFile(HeartGold.BuildModelPath)), HeartGold.BuildModelCrc32),
                ("indoorModels", Gen5Data.Crc32(rom.ReadFile(HeartGold.IndoorBuildModelPath!)), HeartGold.IndoorBuildModelCrc32),
            };
            var data = PlatinumEvents.Parse(HeartGold, headers, rom.ReadFile(HeartGold.MapNamesPath), rom.ReadFile(HeartGold.EventPath), rom.ReadFile(HeartGold.MapMatrixPath), rom.ReadFile(HeartGold.ScriptPath));
            var hashes = new (string Name, string Value, string Expected)[]
            { ("normalized", data.NormalizedHash(), HeartGold.NormalizedEventsSha256), ("editable", data.EditableHash(), HeartGold.EditableEventsSha256), ("events", data.EventsHash(), HeartGold.EventsEditableSha256) };
            foreach (var m in measured) Console.WriteLine($"{(m.Value == m.Expected ? "same" : "DIFF")} {m.Name} 0x{m.Value:X8} (profile 0x{m.Expected:X8})");
            foreach (var h in hashes) Console.WriteLine($"{(h.Value == h.Expected ? "same" : "DIFF")} {h.Name} {h.Value}");
            var census = Census(data);
            Console.WriteLine($"census: {data.Maps.Count} maps, {census.Npc} NPC ({census.ItemBalls} item balls), {census.Warps} warps ({census.Dynamic} dynamic, {census.Resolved} resolve), {census.Trainers} trainers, sight ≤ {census.MaxSight}, object facing ≤ {census.MaxFacing}, fixed-look trainers off their direction: {census.Mismatch}");
            if (measured.Any(m => m.Value != m.Expected) || hashes.Any(h => h.Value != h.Expected)) { if (output is null) return; throw new Exception("The copy differs from the HeartGold profile"); }
            if (census.Resolved + census.Dynamic != census.Warps || census.MaxSight > HeartGold.MaxTrainerSight || census.MaxFacing > HeartGold.MaxObjectFacing || data.Maps.Count != 540)
                throw new Exception("The census contradicts the profile");
            Console.WriteLine($"PASS Hgss_ProfileAndFingerprints: {data.Maps.Count} maps, {census.Npc} NPC, {census.Warps} warps ({census.Dynamic} dynamic, all others resolve)");
            if (output is null) return;
        }
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "HeartGold-copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        var manual = new List<string> { "Prueba jugable (HeartGold IPKE/0, copia privada). Respalda la partida antes. Las cinco pruebas se hacen en orden con una partida NUEVA: 1 y 2 en Pueblo Primavera antes de recibir el primer Pokémon; 3 a 5 después, en las Rutas 29 y 30. Ninguna edición toca el Laboratorio de Elm ni la casa del jugador." };
        string built = Path.Combine(output, "HeartGold-eventos.nds"), restored = Path.Combine(output, "HeartGold-restaurado.nds");
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var data = events.Data;
            if (data.Profile != HeartGold) throw new Exception("The copy did not open with the HeartGold profile");
            var (terrain, terrainNote) = project.LoadTerrain(); var (props, propsNote) = project.LoadProps(); var (assets, assetsNote) = project.LoadMapAssets();
            if (terrain is null || props is null || assets is null) throw new Exception($"Terrain, props or graphics did not open: {terrainNote} {propsNote} {assetsNote}");
            int indoor = data.Maps.Count(m => assets.Area(m.Id).IndoorModels);
            Console.WriteLine($"PASS Hgss_TerrainPropsAndGraphicsOpen: {terrain.Count} land blocks, {indoor} maps with indoor models");

            var saves = new List<string>();
            void Save(NdsEventProposal p, string name) { string file = Path.Combine(output, name); events.Save(p, file); project.EventChanged(); saves.Add(file); }
            PlatinumMap Named(string internalName) => data.Maps.Single(m => m.InternalName == internalName);
            int step = 1;
            var newBark = Named("T20");
            // The south-west house (T20R0301) door now leads into the rival's house (T20R0401): neither is needed to get the first Pokémon
            // (pret/pokeheartgold include/constants/maps.h: 61-62 Elm's lab, 63-64 player's house, 65 south-west house, 66 rival's house 1F).
            var warps = data.Events(newBark.Id).Where(e => e.Kind == PlatinumEventKind.Warp).ToList();
            var w0 = warps.Single(w => data.Map(w.Destination).InternalName == "T20R0301"); var w1 = warps.Single(w => data.Map(w.Destination).InternalName == "T20R0401");
            Save(events.PreviewEvent(newBark.Id, PlatinumEventKind.Warp, w0.Index, PlatinumEvents.EventValues(w0) with { Destination = w1.Destination, Anchor = w1.Anchor }), $"{step}-warp.ndsevents.json");
            manual.Add($"{step++}. {newBark.Name}, warp {w0.Index}, puerta de la casa del suroeste (X {w0.X}, Y {w0.Y}): antes llevaba a {data.Map(w0.Destination).InternalName}, warp {w0.Anchor}; ahora lleva a la casa del rival ({data.Map(w1.Destination).InternalName}, planta baja), warp {w1.Anchor}, como la puerta del warp {w1.Index} (X {w1.X}, Y {w1.Y}). Entra por la puerta de la casa del suroeste: debes aparecer dentro de la casa del rival. Al salir, apareces ante la puerta de la casa del rival (el warp de vuelta no cambia).");
            var t = data.Events(newBark.Id).First(e => e.Kind == PlatinumEventKind.Trigger);
            var layout20 = data.Layout(newBark.Id);
            if (Enumerable.Range(t.Y, t.RangeY).Any(y => terrain.Status(layout20, t.X + t.RangeX, y) != PlatinumTerrainStatus.ClearOfCollision)) throw new Exception("The column beside trigger 0 of New Bark Town collides");
            Save(events.PreviewEvent(newBark.Id, PlatinumEventKind.Trigger, t.Index, PlatinumEvents.EventValues(t) with { Width = t.RangeX + 1 }), $"{step}-activador.ndsevents.json");
            manual.Add($"{step++}. {newBark.Name}, activador {t.Index} (script {t.Script}, se dispara mientras la variable {t.Flag} valga {BinaryPrimitives.ReadUInt16LittleEndian(Convert.FromHexString(t.Raw).AsSpan(12))}): de {t.RangeX}×{t.RangeY} a {t.RangeX + 1}×{t.RangeY} casillas desde X {t.X}, Y {t.Y}. Es la salida oeste hacia la Ruta 29: antes de tener Pokémon, la vecina te detiene al pisar la columna X {t.X}. Ahora también debe detenerte al pisar la columna X {t.X + t.RangeX} (una casilla más al este). El script calcula los pasos suponiendo la columna X {t.X}: si la animación queda desplazada una casilla, es lo esperado. Lo que se prueba es que el activador se dispara.");

            var route29 = Named("R29");
            var look = data.Events(route29.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0 && !e.IsGroundItem && e.Flag == 0 && PlatinumTrainerVision.IsFixedLook(e.Movement) && e.Movement - PlatinumTrainerVision.LookNorthMovement == e.Direction);
            int lookTurn = look.Direction == 1 ? 0 : 1;
            Save(events.Preview(route29.Id, look.Index, new(look.X, look.Y, lookTurn, PlatinumTrainerVision.FixedLookFor(lookTurn))), $"{step}-npc.ndsevents.json");
            manual.Add($"{step++}. {route29.Name}, NPC {look.Index} (ID {look.Id}, X {look.X}, Y {look.Y}, script {look.Script}): mira hacia {PlatinumTrainerVision.DirectionName(lookTurn).ToLowerInvariant()} en lugar de {PlatinumTrainerVision.DirectionName(look.Direction).ToLowerInvariant()} (movimiento «mirar fijo» {look.Movement} → {PlatinumTrainerVision.FixedLookFor(lookTurn)}). Míralo antes de hablarle; al hablarle puede girarse hacia ti.");

            var route30 = Named("R30"); var layout30 = data.Layout(route30.Id);
            var hidden = data.Events(route30.Id).First(e => e.Kind == PlatinumEventKind.Object && e.Type == 2 && data.Maps.Count(m => data.Events(m.Id).Any(o => o.Kind == PlatinumEventKind.Object && o.Script == e.Script)) == 1);
            int dx = terrain.Status(layout30, hidden.X + 1, hidden.Y) == PlatinumTerrainStatus.ClearOfCollision ? 1 : terrain.Status(layout30, hidden.X - 1, hidden.Y) == PlatinumTerrainStatus.ClearOfCollision ? -1 : 0;
            if (dx == 0) throw new Exception("No free tile beside the hidden item of Route 30");
            Save(events.PreviewEvent(route30.Id, PlatinumEventKind.Object, hidden.Index, PlatinumEvents.EventValues(hidden) with { X = hidden.X + dx }), $"{step}-objeto.ndsevents.json");
            manual.Add($"{step++}. {route30.Name}, objeto oculto {hidden.Index} (script {hidden.Script}): de X {hidden.X}, Y {hidden.Y} a X {hidden.X + dx}. Busca con el Zahorí o pulsa A mirando la casilla nueva (desde cualquier lado); la anterior ya no debe dar el objeto.");
            var trainer = data.Events(route30.Id).First(e => e.Kind == PlatinumEventKind.Npc && PlatinumTrainerVision.ChecksSight(e.Type) && !PlatinumTrainerVision.SeesAllDirections(e.Type) && PlatinumTrainerVision.IsFixedLook(e.Movement) && e.Movement - PlatinumTrainerVision.LookNorthMovement == e.Direction && e.Direction is 0 or 1);
            int turn = trainer.Direction == 1 ? 0 : 1;
            Save(events.Preview(route30.Id, trainer.Index, new(trainer.X, trainer.Y, turn, PlatinumTrainerVision.FixedLookFor(turn))), $"{step}-entrenador.ndsevents.json");
            manual.Add($"{step++}. {route30.Name}, entrenador NPC {trainer.Index} (script {trainer.Script}{(PlatinumTrainerVision.TrainerId(trainer.Script) is int id ? $", entrenador {id}" : "")}, X {trainer.X}, Y {trainer.Y}, visión {trainer.Sight}): mira hacia {PlatinumTrainerVision.DirectionName(turn).ToLowerInvariant()} en lugar de {PlatinumTrainerVision.DirectionName(trainer.Direction).ToLowerInvariant()} (movimiento {trainer.Movement} → {PlatinumTrainerVision.FixedLookFor(turn)}). Si aún no lo has vencido, comprueba que te ve al cruzar su nueva línea de visión y ya no por la anterior.");

            var ball = data.Events(route30.Id).First(e => e.IsGroundItem);
            if (!Throws<InvalidOperationException>(() => data.ReplaceNpc(route30.Id, ball.Index, PlatinumEvents.Values(ball), new(ball.X + 1, ball.Y, ball.Direction)))) throw new Exception("An item ball was moved");
            byte[] record = PlatinumEvents.NewNpcRecord(data.Events(newBark.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0 && !e.IsGroundItem), 200, 0, 0, 0, 0, 0);
            if (data.ValidateNewNpc(newBark.Id, record) is not [var refusal] || !refusal.Contains("no se crean NPC")) throw new Exception("HeartGold accepted a new NPC");
            Console.WriteLine("PASS Hgss_ItemBallsAndNewNpcAreRefused");

            project.Export(built);
            using (var reopened = NdsProject.Open(saves[^1], false))
            {
                if (!reopened.Events.Data.Bytes().SequenceEqual(events.Data.Bytes())) throw new Exception("Checkpoint did not persist");
                while (reopened.Events.CanUndo) { reopened.Events.Save(reopened.Events.PreviewUndo(), Path.Combine(output, $"restaurar-{reopened.Events.Revision}.ndsevents.json")); reopened.EventChanged(); }
                reopened.Export(restored);
            }
            using (var rebuilt = NdsProject.Open(built, false))
                if (!rebuilt.Events.Data.Bytes().SequenceEqual(events.Data.Bytes()) || rebuilt.Events.Data.NormalizedHash() == HeartGold.NormalizedEventsSha256) throw new Exception("Exported ROM did not persist or was accepted for the wrong reason");
            using var baseline = NdsRom.Open(copy); using var builtRom = NdsRom.Open(built);
            foreach (var file in baseline.Files) if (file.Path != HeartGold.EventPath && !baseline.ReadFile(file).SequenceEqual(builtRom.ReadFile(file.Path))) throw new Exception("Unrelated file changed: " + file.Path);
            if (!baseline.ReadArm9().SequenceEqual(builtRom.ReadArm9())) throw new Exception("ARM9 changed");
            Console.WriteLine($"PASS Hgss_SaveReopenExportReopenAndUndo: {saves.Count} edits; exported ROM reopens in the editor; ARM9 and every other NDS file byte-exact");
        }
        File.Delete(copy);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(restored))) != hash)
            throw new Exception("Source changed or whole-ROM restoration differs");
        manual.Add($"{manual.Count}. HeartGold-restaurado.nds es idéntica (SHA-256) a la copia original; no hace falta probarla en juego.");
        File.WriteAllLines(Path.Combine(output, "MANUAL.txt"), manual);
        Console.WriteLine("PASS Hgss_OriginalAndRestoredRomHashesMatch; gameplay NOT tested");
    }

    private static (int Npc, int ItemBalls, int Warps, int Dynamic, int Resolved, int Trainers, int MaxSight, int MaxFacing, int Mismatch) Census(PlatinumEvents data)
    {
        var all = data.Maps.Select(m => m.EventFile).Distinct().SelectMany(file => data.Events(data.Maps.First(m => m.EventFile == file).Id)).ToList();
        var trainers = all.Where(e => e.Kind == PlatinumEventKind.Npc && PlatinumTrainerVision.ChecksSight(e.Type)).ToList();
        var warps = all.Where(e => e.Kind == PlatinumEventKind.Warp).ToList();
        return (all.Count(e => e.Kind == PlatinumEventKind.Npc), all.Count(e => e.IsGroundItem), warps.Count, warps.Count(w => w.Anchor == PlatinumEventFields.DynamicAnchor),
            warps.Count(w => w.Anchor != PlatinumEventFields.DynamicAnchor && data.WarpTargetExists(w.Destination, w.Anchor)), trainers.Count, trainers.Max(e => e.Sight),
            all.Where(e => e.Kind == PlatinumEventKind.Object).Max(e => e.Direction), trainers.Count(e => PlatinumTrainerVision.IsFixedLook(e.Movement) && e.Movement - PlatinumTrainerVision.LookNorthMovement != e.Direction));
    }
    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}
