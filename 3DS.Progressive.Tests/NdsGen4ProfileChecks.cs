using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Nds.Project;
using ThreeDS.Progressive.Services.ProjectDetection;
using Button = System.Windows.Controls.Button;

/// <summary>
/// The map and event editor beyond Platinum: the measured Gen 4 profiles (Platinum CPUE/1, Diamond ADAE/5). Synthetic checks of the profile rules, off-screen
/// checks of the editor with a Diamond session, and a probe on a private Diamond copy (edit, checkpoint, export, reopen, restore). No gameplay.
/// </summary>
internal static class NdsGen4ProfileChecks
{
    private static readonly Gen4EventProfile Diamond = Gen4EventProfile.Diamond, Platinum = Gen4EventProfile.Platinum;

    /// <summary>The event fixture of the phase-1 checks, read with the Diamond profile and packed with Diamond's file names.</summary>
    private static PlatinumEvents ReadDiamond(NdsEventChecks.Fixture f) => PlatinumEvents.Parse(Diamond, f.Headers, f.Names, f.Events, f.Matrices, f.Scripts, ["Zona A", "Zona B"]);
    private static byte[] DiamondRom(NdsEventChecks.Fixture f, string code = "ADAE", byte revision = 5) => NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]>
    { [Diamond.EventPath] = f.Events, [Gen4EventProfile.NamesPath] = f.Names, [Gen4EventProfile.MatrixPath] = f.Matrices, [Diamond.ScriptPath] = f.Scripts, ["untouched.bin"] = [7, 1, 99, 18] }, code, revision);

    internal static void Run(Action<bool, string> check)
    {
        // ---- profiles: identity, nothing shared that differs
        check(Gen4EventProfile.For("CPUE", 1) == Platinum && Gen4EventProfile.For("ADAE", 5) == Diamond, "Gen4Profile_MeasuredGamesHaveAProfile");
        check(Gen4EventProfile.For("ADAE", 4) is null && Gen4EventProfile.For("APAE", 5) is null && Gen4EventProfile.For("CPUE", 0) is null && Gen4EventProfile.For("IPGE", 0) is null && Gen4EventProfile.For("IRBO", 0) is null,
            "Gen4Profile_OtherRevisionsPearlSoulSilverAndGen5HaveNone");
        check(Diamond.EventPath != Platinum.EventPath && Diamond.ScriptPath != Platinum.ScriptPath && Diamond.LandDataPath != Platinum.LandDataPath && Diamond.HeaderOffset != Platinum.HeaderOffset
            && Diamond.HeaderCount == 559 && Platinum.HeaderCount == 593 && Diamond.LocationNamesText == 382 && Platinum.LocationNamesText == 433, "Gen4Profile_PathsTablesAndTextsAreTheirOwn");
        check(Platinum.SupportsNpcCreation && !Diamond.SupportsNpcCreation && Platinum.MaxObjectFacing == 6 && Diamond.MaxObjectFacing == 5, "Gen4Profile_NpcCreationAndFacingFollowTheAccreditedData");
        check(PlatinumEvents.EventPath == Platinum.EventPath && PlatinumEvents.ScriptPath == Platinum.ScriptPath && PlatinumEvents.HeaderOffset == Platinum.HeaderOffset && PlatinumEvents.NormalizedEventsSha256 == Platinum.NormalizedEventsSha256
            && PlatinumEvents.EditableEventsSha256 == Platinum.EditableEventsSha256 && PlatinumEvents.EventsEditableSha256 == Platinum.EventsEditableSha256 && PlatinumTerrain.LandDataCrc32 == Platinum.LandDataCrc32,
            "Gen4Profile_PlatinumProfileKeepsThePhase1Constants");

        var f = NdsEventChecks.Make(); var pt = f.Read(); var dp = ReadDiamond(f);
        check(pt.Profile == Platinum && dp.Profile == Diamond && pt.Bytes().SequenceEqual(dp.Bytes()) && pt.Events(0).SequenceEqual(dp.Events(0)), "Gen4Profile_TheSameRecordsReadTheSameInBothGames");

        // ---- writes allowed in Diamond: the same fields as in Platinum, nothing else
        var npc = dp.Npc(0, 0); byte[] moved = dp.ReplaceNpc(0, 0, PlatinumEvents.Values(npc), new(npc.X + 1, npc.Y, 3));
        check(moved.SequenceEqual(pt.ReplaceNpc(0, 0, PlatinumEvents.Values(npc), new(npc.X + 1, npc.Y, 3))) && dp.WithEvents(moved).Profile == Diamond, "Gen4Profile_DiamondNpcMoveWritesTheSameBytes");
        var obj = dp.Event(0, PlatinumEventKind.Object, 0);
        check(dp.ReplaceEvent(0, PlatinumEventKind.Object, 0, PlatinumEvents.EventValues(obj), PlatinumEvents.EventValues(obj) with { Facing = 5 }).Length == f.Events.Length, "Gen4Profile_DiamondObjectFacingUpTo5");
        check(Throws<ArgumentOutOfRangeException>(() => dp.ReplaceEvent(0, PlatinumEventKind.Object, 0, PlatinumEvents.EventValues(obj), PlatinumEvents.EventValues(obj) with { Facing = 6 }))
            && !Throws<ArgumentOutOfRangeException>(() => pt.ReplaceEvent(0, PlatinumEventKind.Object, 0, PlatinumEvents.EventValues(obj), PlatinumEvents.EventValues(obj) with { Facing = 6 })), "Gen4Profile_Facing6IsOnlyAcceptedInPlatinum");

        // ---- refused in Diamond: new NPC and appended scripts
        byte[] record = PlatinumEvents.NewNpcRecord(npc, 99, npc.Graphics, npc.Movement, 0, 1, 1);
        check(dp.ValidateNewNpc(0, record) is [var only] && only.Contains("Diamante ADAE/5") && pt.ValidateNewNpc(0, record).All(p => !p.Contains("acreditaron")), "Gen4Profile_DiamondRefusesNewNpcWithItsReason");
        check(Throws<InvalidOperationException>(() => dp.AddNpc(0, record)) && Throws<InvalidOperationException>(() => dp.WithAppendedScript(0, PlatinumScriptFile.NurseScript(99), out _)), "Gen4Profile_DiamondAddsNoNpcAndNoScript");

        // ---- script references: Diamond's own item archives, unresolved when the archive is missing (never Platinum's numbers)
        check(dp.Script(0, 7000).Archive == 370 && dp.Script(0, 8000).Archive == 374 && pt.Script(0, 7000).Archive == 404 && pt.Script(0, 8000).Archive == 408 && dp.Script(0, 7000).Entry is null, "Gen4Profile_ItemScriptsUseTheArchivesOfEachGame");

        // ---- Open: a Diamond ROM whose tables are not the accredited ones is refused like any other (InvalidDataException)
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen4-profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "diamante.nds"); File.WriteAllBytes(source, DiamondRom(f)); string sourceHash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source);
            check(Throws<InvalidDataException>(() => PlatinumEvents.Open(rom)) && Throws<Exception>(() => PlatinumTerrain.Open(rom)) && Throws<Exception>(() => PlatinumLandProps.Open(rom)),
                "Gen4Profile_UnaccreditedDiamondTablesAreRefused");
            string pearl = Path.Combine(temp, "perla.nds"); File.WriteAllBytes(pearl, DiamondRom(f, "APAE", 5));
            using (var other = NdsRom.Open(pearl)) check(Throws<InvalidDataException>(() => PlatinumEvents.Open(other)) && Throws<InvalidDataException>(() => PlatinumMapAssets.Open(other)), "Gen4Profile_PearlIsNotOpenedWithDiamondsProfile");

            // ---- checkpoints carry the game of the session; a checkpoint of another game is refused
            var session = new NdsEventSession(rom, dp);
            string first = Path.Combine(temp, "1.ndsevents.json"); session.Save(session.Preview(0, 0, new(npc.X + 1, npc.Y, 3)), first);
            var doc = NdsEventSession.ReadDocument(first);
            check(doc.Game == "ADAE" && doc.Revision == 5 && doc.Version == 1, "Gen4Profile_DiamondCheckpointNamesItsGame");
            var reopened = new NdsEventSession(rom, ReadDiamond(f)); reopened.Load(doc, first);
            check(reopened.Data.Npc(0, 0).X == npc.X + 1 && Equals(reopened.Data.Profile, Diamond), "Gen4Profile_DiamondCheckpointReopens");
            check(Throws<InvalidDataException>(() => new NdsEventSession(rom, ReadDiamond(f)).Load(doc with { Game = "CPUE", Revision = 1 }, first)), "Gen4Profile_APlatinumCheckpointIsRefusedOnDiamond");
            string eventEdit = Path.Combine(temp, "2.ndsevents.json"); var trig = session.Data.Event(0, PlatinumEventKind.Trigger, 0);
            session.Save(session.PreviewEvent(0, PlatinumEventKind.Trigger, 0, PlatinumEvents.EventValues(trig) with { Width = trig.RangeX + 1 }), eventEdit);
            check(NdsEventSession.ReadDocument(eventEdit) is { Version: 5, Game: "ADAE", Revision: 5 }, "Gen4Profile_DiamondEventEditIsACheckpointV5OfItsGame");

            // ---- export: only Diamond's event archive changes; restoring everything gives the source back
            string built = Path.Combine(temp, "editado.nds"); rom.Save(built, new Dictionary<int, byte[]> { [rom.File(Diamond.EventPath).Id] = session.Replacement()! }); session.VerifyExport(built);
            using (var b = NdsRom.Open(built))
                check(b.ReadFile(Diamond.EventPath).SequenceEqual(session.Data.Bytes()) && rom.Files.Where(x => x.Path != Diamond.EventPath).All(x => rom.ReadFile(x).AsSpan().SequenceEqual(b.ReadFile(x.Path))), "Gen4Profile_ExportChangesOnlyDiamondsEventArchive");
            while (session.CanUndo) session.Save(session.PreviewUndo(), Path.Combine(temp, $"undo-{session.Revision}.ndsevents.json"));
            check(session.Replacement() is null && session.Data.Bytes().SequenceEqual(f.Events) && NdsMapCanvasChecks.Sha(source) == sourceHash, "Gen4Profile_RestoringEveryEditGivesTheSourceBack");

            // ---- project model and detector
            var events = NdsProjectEvents.FromCheckpoint(doc);
            check(events.ToCheckpoint(source, doc.SourceSha256, "ADAE", 5) is { Game: "ADAE", Revision: 5 } && events.ToCheckpoint(source, doc.SourceSha256).Game == "CPUE", "Gen4Profile_ProjectHistoryExportsWithItsGame");
            string roms = Path.Combine(temp, "detector");
            string dSource = ProjectDetectorChecks.Write(roms, "Diamante.nds", ProjectDetectorChecks.Rom("ADAE", 5)), pSource = ProjectDetectorChecks.Write(roms, "Platino.nds", ProjectDetectorChecks.Rom("CPUE", 1));
            var ok = ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "dia.ndsevents.json", dSource, 1, "ADAE", 5));
            check(ok is { Status: DetectionStatus.Compatible, Kind: DetectedKind.DsEventCheckpoint } && ok.Game.Contains("Diamante"), "Gen4Profile_DetectorAcceptsADiamondCheckpoint");
            check(ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "cruzado.ndsevents.json", pSource, 1, "ADAE", 5)).Status == DetectionStatus.Unsupported
                && ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "perla.ndsevents.json", dSource, 1, "APAE", 5)).Status == DetectionStatus.Unsupported, "Gen4Profile_DetectorRefusesMixedOrUnmeasuredGames");
        }
        finally { Directory.Delete(temp, true); }
    }

    // ------------------------------------------------------------------------------------------------ WPF (off-screen)
    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen4-profile-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = NdsEventChecks.Make(); string source = Path.Combine(temp, "diamante.nds"); File.WriteAllBytes(source, DiamondRom(f)); string hash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, ReadDiamond(f));
            var view = new NdsEventEditorView(session); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var texts = NdsMapCanvasChecks.Children<TextBlock>(view).ToList(); var buttons = NdsMapCanvasChecks.Children<Button>(view).ToList();
            check(texts.Any(t => t.Text == "Eventos · Diamante ADAE/5") && texts.Any(t => t.Text.Contains("En Diamante no se crean")), "Gen4ProfileUi_TitleAndIntroNameTheGame");
            string[] structural = ["añadir NPC", "añadir enfermera", "duplicar NPC", "copiar NPC", "cortar NPC", "pegar NPC", "eliminar NPC"];
            check(!buttons.Any(b => structural.Any(s => AutomationProperties.GetName(b) == "Eventos DS · " + s)) && texts.Any(t => t.Text.StartsWith("NPC: en Diamante ADAE/5 solo se mueven")), "Gen4ProfileUi_NoStructuralNpcControlsInDiamond");
            var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single();
            check(canvas.ContextMenu is null, "Gen4ProfileUi_NoStructuralContextMenuInDiamond");
            var tabs = NdsMapCanvasChecks.Children<TabControl>(view).Single(); var tables = NdsMapCanvasChecks.Children<DataGrid>(view).ToList();
            tabs.SelectedIndex = 1; tables[1].SelectedItem = tables[1].Items.Cast<PlatinumEvent>().First(e => e.Kind == PlatinumEventKind.Object); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var facing = NdsMapCanvasChecks.Children<ComboBox>(view).Single(c => AutomationProperties.GetName(c) == "Eventos DS · objeto · dirección de interacción");
            check(facing.Items.Count == 6, "Gen4ProfileUi_ObjectFacingOffersOnlyDiamondsValues");
            facing.SelectedIndex = 5;
            var differences = NdsMapCanvasChecks.Children<TextBox>(view).Single(t => AutomationProperties.GetName(t) == "Eventos DS · diferencias previstas");
            check(differences.Text.Contains("Interacción") && session.Revision == 0 && NdsMapCanvasChecks.Sha(source) == hash, "Gen4ProfileUi_EditingPreviewsWithoutWriting");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "gen4-diamond-editor-1100");
            NdsMapCanvasChecks.Capture(view, 760, 620, "gen4-diamond-editor-760");
        }
        finally { Directory.Delete(temp, true); }
    }

    // ------------------------------------------------------------------------------------------------ real ROM (private copy)
    /// <summary>
    /// A private Diamond ADAE/5 copy: profile fingerprints, then in Twinleaf (411) an NPC turned, warp 0 retargeted, one background object moved and one trigger
    /// widened, and on Route 202 a fixed-look trainer turned. Save, reopen the checkpoint, export, reopen the exported ROM, restore and compare hashes. Writes
    /// MANUAL.txt with the playable test. No gameplay.
    /// </summary>
    internal static void Probe(string output, string source)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "Diamante-copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        var manual = new List<string> { "Prueba jugable (Diamante ADAE/5, copia privada). Usa una partida guardada FUERA de Pueblo Hojaverde y de la Ruta 202; respalda la partida antes." };
        string built = Path.Combine(output, "Diamante-eventos.nds"), restored = Path.Combine(output, "Diamante-restaurado.nds");
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var data = events.Data;
            if (!Equals(data.Profile, Diamond)) throw new Exception("The copy did not open with the Diamond profile");
            if (data.NormalizedHash() != Diamond.NormalizedEventsSha256 || data.EditableHash() != Diamond.EditableEventsSha256 || data.EventsHash() != Diamond.EventsEditableSha256) throw new Exception("Fingerprints of the original differ from the profile");
            var all = data.Maps.Select(m => m.EventFile).Distinct().SelectMany(file => data.Events(data.Maps.First(m => m.EventFile == file).Id)).ToList();
            int warps = all.Count(e => e.Kind == PlatinumEventKind.Warp), dynamic = all.Count(e => e.Kind == PlatinumEventKind.Warp && e.Anchor == PlatinumEventFields.DynamicAnchor);
            int resolved = all.Count(e => e.Kind == PlatinumEventKind.Warp && e.Anchor != PlatinumEventFields.DynamicAnchor && data.WarpTargetExists(e.Destination, e.Anchor));
            if (resolved + dynamic != warps) throw new Exception("Some warp of the original data does not resolve");
            Console.WriteLine($"PASS Gen4Real_ProfileAndFingerprints: {data.Maps.Count} maps, {all.Count(e => e.Kind == PlatinumEventKind.Npc)} NPC, {warps} warps ({dynamic} dynamic, all others resolve)");
            if (project.LoadTerrain().Terrain is null || project.LoadProps().Props is null || project.LoadMapAssets().Assets is null) throw new Exception("Terrain, props or graphics did not open with the Diamond profile");
            Console.WriteLine("PASS Gen4Real_TerrainPropsAndGraphicsOpen");

            var twinleaf = data.Map(411); if (twinleaf.InternalName != "T01") throw new Exception("Map 411 is not T01");
            var saves = new List<string>();
            void Save(NdsEventProposal p, string name) { string file = Path.Combine(output, name); events.Save(p, file); project.EventChanged(); saves.Add(file); }
            int step = 1;
            if (data.Events(411).FirstOrDefault(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0 && e.Movement == 0) is { } n)
            {
                int turn = n.Direction == 1 ? 0 : 1;
                Save(events.Preview(411, n.Index, new(n.X, n.Y, turn)), $"{step}-npc.ndsevents.json");
                manual.Add($"{step++}. Hojaverde, NPC {n.Index} (ID {n.Id}, X {n.X}, Y {n.Y}, movimiento 0): dirección {PlatinumTrainerVision.DirectionName(n.Direction)} → {PlatinumTrainerVision.DirectionName(turn)}. Míralo antes de hablarle; un script puede imponer otra orientación al interactuar.");
            }
            var warpList = data.Events(411).Where(e => e.Kind == PlatinumEventKind.Warp).ToList();
            var w0 = warpList[0]; var w1 = warpList.FirstOrDefault(w => w.Index != 0 && (w.Destination != w0.Destination || w.Anchor != w0.Anchor) && w.Anchor != PlatinumEventFields.DynamicAnchor);
            if (w1 is not null)
            {
                Save(events.PreviewEvent(411, PlatinumEventKind.Warp, 0, PlatinumEvents.EventValues(w0) with { Destination = w1.Destination, Anchor = w1.Anchor }), $"{step}-warp.ndsevents.json");
                manual.Add($"{step++}. Warp 0 de Hojaverde (X {w0.X}, Y {w0.Y}): antes llevaba a {data.Map(w0.Destination).Name} ({data.Map(w0.Destination).InternalName}), warp {w0.Anchor}; ahora lleva a {data.Map(w1.Destination).Name} ({data.Map(w1.Destination).InternalName}), warp {w1.Anchor}, como la puerta del warp {w1.Index} (X {w1.X}, Y {w1.Y}). Entra por la puerta del warp 0 y comprueba dónde apareces.");
            }
            if (data.Events(411).FirstOrDefault(e => e.Kind == PlatinumEventKind.Object) is { } o)
            {
                Save(events.PreviewEvent(411, PlatinumEventKind.Object, o.Index, PlatinumEvents.EventValues(o) with { X = o.X + 1 }), $"{step}-objeto.ndsevents.json");
                manual.Add($"{step++}. Objeto de fondo {o.Index} de Hojaverde ({PlatinumEventFields.ObjectTypeName(o.Type)}, script {o.Script}): de X {o.X}, Y {o.Y} a X {o.X + 1}. Interactúa con la casilla nueva ({PlatinumEventFields.FacingName(o.Direction).ToLowerInvariant()}); la anterior ya no debe responder.");
            }
            if (data.Events(411).FirstOrDefault(e => e.Kind == PlatinumEventKind.Trigger) is { } t)
            {
                Save(events.PreviewEvent(411, PlatinumEventKind.Trigger, t.Index, PlatinumEvents.EventValues(t) with { Width = t.RangeX + 1 }), $"{step}-activador.ndsevents.json");
                manual.Add($"{step++}. Activador {t.Index} de Hojaverde (script {t.Script}): de {t.RangeX}×{t.RangeY} a {t.RangeX + 1}×{t.RangeY} casillas desde X {t.X}, Y {t.Y}; la casilla X {t.X + t.RangeX} también lo dispara (si su condición se cumple).");
            }
            var route = data.Maps.First(m => m.InternalName == "R202");
            if (data.Events(route.Id).FirstOrDefault(e => e.Kind == PlatinumEventKind.Npc && PlatinumTrainerVision.ChecksSight(e.Type) && !PlatinumTrainerVision.SeesAllDirections(e.Type) && PlatinumTrainerVision.IsFixedLook(e.Movement)) is { } tr)
            {
                int turn = tr.Direction switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
                Save(events.Preview(route.Id, tr.Index, new(tr.X, tr.Y, turn, PlatinumTrainerVision.FixedLookFor(turn))), $"{step}-entrenador.ndsevents.json");
                manual.Add($"{step++}. Ruta 202, entrenador NPC {tr.Index} (script {tr.Script}{(PlatinumTrainerVision.TrainerId(tr.Script) is int id ? $", entrenador {id}" : "")}, X {tr.X}, Y {tr.Y}, visión {tr.Sight}): mira hacia {PlatinumTrainerVision.DirectionName(turn).ToLowerInvariant()} en lugar de {PlatinumTrainerVision.DirectionName(tr.Direction).ToLowerInvariant()} (movimiento {tr.Movement} → {PlatinumTrainerVision.FixedLookFor(turn)}). Comprueba que te ve al cruzar su nueva línea de visión y ya no por la anterior.");
            }
            byte[] record = PlatinumEvents.NewNpcRecord(data.Events(411).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0), 200, 0, 0, 0, 0, 0);
            if (data.ValidateNewNpc(411, record) is not [var refusal] || !refusal.Contains("no se crean NPC")) throw new Exception("Diamond accepted a new NPC");
            Console.WriteLine("PASS Gen4Real_NewNpcIsRefusedInDiamond");

            project.Export(built);
            using (var reopened = NdsProject.Open(saves[^1], false))
            {
                if (!reopened.Events.Data.Bytes().SequenceEqual(events.Data.Bytes())) throw new Exception("Checkpoint did not persist");
                while (reopened.Events.CanUndo) { reopened.Events.Save(reopened.Events.PreviewUndo(), Path.Combine(output, $"restaurar-{reopened.Events.Revision}.ndsevents.json")); reopened.EventChanged(); }
                reopened.Export(restored);
            }
            using (var rebuilt = NdsProject.Open(built, false))
                if (!rebuilt.Events.Data.Bytes().SequenceEqual(events.Data.Bytes()) || rebuilt.Events.Data.NormalizedHash() == Diamond.NormalizedEventsSha256) throw new Exception("Exported ROM did not persist or was accepted for the wrong reason");
            using var baseline = NdsRom.Open(copy); using var builtRom = NdsRom.Open(built);
            foreach (var file in baseline.Files) if (file.Path != Diamond.EventPath && !baseline.ReadFile(file).SequenceEqual(builtRom.ReadFile(file.Path))) throw new Exception("Unrelated file changed: " + file.Path);
            Console.WriteLine($"PASS Gen4Real_SaveReopenExportReopenAndUndo: {saves.Count} edits; exported ROM reopens in the editor; every other NDS file byte-exact");
        }
        File.Delete(copy);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(restored))) != hash)
            throw new Exception("Source changed or whole-ROM restoration differs");
        manual.Add($"{manual.Count}. Diamante-restaurado.nds es idéntica (SHA-256) a la copia original; no hace falta probarla en juego.");
        File.WriteAllLines(Path.Combine(output, "MANUAL.txt"), manual);
        Console.WriteLine("PASS Gen4Real_OriginalAndRestoredRomHashesMatch; gameplay NOT tested");
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}
