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
/// The map and event editor on Pokémon White, Spanish edition (IRAS/0): the Gen 5 reader of Black with White's own profile (its map tables are
/// byte-identical to Black's; its scripts and texts are its own). Synthetic checks of the profile rules, off-screen checks with a White session, and a
/// probe on a private White copy that also compares every table with a Black copy. No gameplay.
/// </summary>
internal static class NdsGen5WhiteChecks
{
    private static readonly Gen5MapProfile White = Gen5MapProfile.White, Black = Gen5MapProfile.Black;
    private static Gen5ZoneEvents Read(NdsGen5BlackChecks.Fixture f) => Gen5ZoneEvents.Parse(White, f.Headers, f.Events, f.Matrices, f.Scripts, ["Pueblo A", "Ruta B", "Casa C"]);

    internal static void Run(Action<bool, string> check)
    {
        // ---- profile: the same tables as Black, its own scripts and texts
        check(Gen5MapProfile.For("IRAS", 0) == White && NdsEventProfiles.For("IRAS", 0) is Gen5MapProfile { GameName: "Blanco" } && Gen5MapProfile.For("IRAS", 1) is null && Gen5MapProfile.For("IRAO", 0) is null,
            "Gen5White_OnlySpanishWhiteRev0HasAProfile");
        check(White with { GameCode = Black.GameCode, GameName = Black.GameName, ScriptsCrc32 = Black.ScriptsCrc32 } == Black && White.ScriptsCrc32 != Black.ScriptsCrc32,
            "Gen5White_EveryValueButIdentityAndScriptsIsBlacks");
        check(White is { SupportsNpcCreation: false, EditsObjectFacing: false, MaxTrainerSight: 0, LocationNamesText: 89 }, "Gen5White_SameConservativeRules");

        // ---- the shared reader with White's profile: same records, same writes
        var f = NdsGen5BlackChecks.Make(); var w = Read(f); var b = f.Read();
        check(w.Profile == White && w.Events(NdsGen5BlackChecks.Town).Select(e => e.Raw).SequenceEqual(b.Events(NdsGen5BlackChecks.Town).Select(e => e.Raw)), "Gen5White_ReadsTheSameRecordsAsBlack");
        var t = w.Npc(NdsGen5BlackChecks.Town, NdsGen5BlackChecks.Trainer); var tv = new PlatinumNpcValues(t.X, t.Y, t.Direction, t.Movement);
        check(w.ReplaceNpc(NdsGen5BlackChecks.Town, NdsGen5BlackChecks.Trainer, tv, tv with { Direction = 0, Movement = 14 })
            .SequenceEqual(b.ReplaceNpc(NdsGen5BlackChecks.Town, NdsGen5BlackChecks.Trainer, tv, tv with { Direction = 0, Movement = 14 })), "Gen5White_WritesTheSameBytesAsBlack");
        check(Throws<InvalidOperationException>(() => w.ReplaceNpc(NdsGen5BlackChecks.Town, NdsGen5BlackChecks.Trainer, tv with { Sight = 3 }, tv with { Sight = 4 }))
            && w.Npc(NdsGen5BlackChecks.Town, NdsGen5BlackChecks.Rail).EditLock is not null && w.Npc(NdsGen5BlackChecks.Town, NdsGen5BlackChecks.Ball).IsGroundItem, "Gen5White_SameLocksAsBlack");

        // ---- ROM, checkpoints and detector: White's own identity, never mixed with Black's
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen5-white-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "blanco.nds"); File.WriteAllBytes(source, f.Rom("IRAS", 0)); string sourceHash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source);
            check(Throws<InvalidDataException>(() => Gen5ZoneEvents.Open(rom)) && Throws<Exception>(() => Gen5MapGraphics.Open(rom)), "Gen5White_UnaccreditedWhiteTablesAreRefused");
            var session = new NdsEventSession(rom, Read(f));
            string first = Path.Combine(temp, "1.ndsevents.json"); session.Save(session.Preview(NdsGen5BlackChecks.Town, NdsGen5BlackChecks.Trainer, new(t.X, t.Y, 0, 14)), first);
            var doc = NdsEventSession.ReadDocument(first);
            check(doc is { Game: "IRAS", Revision: 0, Version: 4 }, "Gen5White_CheckpointNamesWhite");
            check(Throws<InvalidDataException>(() => new NdsEventSession(rom, Read(f)).Load(doc with { Game = "IRBO" }, first)), "Gen5White_ABlackCheckpointIsRefusedOnWhite");
            using (var blackRom = NdsRom.Open(ProjectDetectorChecks.Write(temp, "negro.nds", f.Rom())))
                check(Throws<InvalidDataException>(() => new NdsEventSession(blackRom, f.Read()).Load(doc, first)), "Gen5White_AWhiteCheckpointIsRefusedOnBlack");
            string built = Path.Combine(temp, "editado.nds"); rom.Save(built, new Dictionary<int, byte[]> { [rom.File(White.EventPath).Id] = session.Replacement()! }); session.VerifyExport(built);
            using (var x = NdsRom.Open(built))
                check(rom.Files.Where(e => e.Path != White.EventPath).All(e => rom.ReadFile(e).AsSpan().SequenceEqual(x.ReadFile(e.Path))) && x.Header.GameCode == "IRAS", "Gen5White_ExportChangesOnlyTheEventArchive");
            while (session.CanUndo) session.Save(session.PreviewUndo(), Path.Combine(temp, $"undo-{session.Revision}.ndsevents.json"));
            check(session.Replacement() is null && NdsMapCanvasChecks.Sha(source) == sourceHash, "Gen5White_RestoringEveryEditGivesTheSourceBack");
            string roms = Path.Combine(temp, "detector");
            string wSource = ProjectDetectorChecks.Write(roms, "Blanco.nds", ProjectDetectorChecks.Rom("IRAS", 0)), bSource = ProjectDetectorChecks.Write(roms, "Negro.nds", ProjectDetectorChecks.Rom("IRBO", 0));
            var ok = ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "blanco.ndsevents.json", wSource, 1, "IRAS", 0));
            check(ok is { Status: DetectionStatus.Compatible, Kind: DetectedKind.DsEventCheckpoint } && ok.Game.Contains("Blanco"), "Gen5White_DetectorAcceptsAWhiteCheckpoint");
            check(ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "cruzado.ndsevents.json", bSource, 1, "IRAS", 0)).Status == DetectionStatus.Unsupported, "Gen5White_DetectorRefusesAWhiteCheckpointOnBlack");
        }
        finally { Directory.Delete(temp, true); }
    }

    // ------------------------------------------------------------------------------------------------ WPF (off-screen)
    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen5-white-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = NdsGen5BlackChecks.Make(); string source = Path.Combine(temp, "blanco.nds"); File.WriteAllBytes(source, f.Rom("IRAS", 0)); string hash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, Read(f));
            var view = new NdsEventEditorView(session); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var texts = NdsMapCanvasChecks.Children<TextBlock>(view).ToList(); var buttons = NdsMapCanvasChecks.Children<Button>(view).ToList();
            check(texts.Any(t => t.Text == "Eventos · Blanco IRAS/0") && texts.Any(t => t.Text.Contains("En Blanco la visión de los entrenadores")), "Gen5WhiteUi_TitleAndIntroNameTheGame");
            check(!buttons.Any(b => AutomationProperties.GetName(b) is "Eventos DS · añadir NPC" or "Eventos DS · añadir enfermera" or "Eventos DS · eliminar NPC"), "Gen5WhiteUi_NoStructuralNpcControls");
            var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single(); maps.SelectedItem = maps.Items[NdsGen5BlackChecks.Town]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var tables = NdsMapCanvasChecks.Children<DataGrid>(view).ToList(); tables[0].SelectedItem = tables[0].Items.Cast<PlatinumEvent>().Single(e => e.Index == NdsGen5BlackChecks.Trainer); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            Find<Button>(view, "Eventos DS · orientación · mirar hacia derecha").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var differences = Find<TextBox>(view, "Eventos DS · diferencias previstas");
            check(differences.Text.Contains("Abajo → Derecha") && differences.Text.Contains("15 → 17") && session.Revision == 0 && NdsMapCanvasChecks.Sha(source) == hash, "Gen5WhiteUi_TurningPreviewsWithoutWriting");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "gen5-white-editor-1100");
        }
        finally { Directory.Delete(temp, true); }
    }
    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject => NdsMapCanvasChecks.Children<T>(root).Single(e => AutomationProperties.GetName(e) == name);

    // ------------------------------------------------------------------------------------------------ real ROM (private copies)
    /// <summary>
    /// A private White IRAS/0 copy (and, read-only, a private Black copy to compare the tables): profile, census, the tables equal to Black's and those
    /// that differ; terrain and graphics. In Nuvema Town (Pueblo Arcilla) the door of the lower right house leads into the lower left one; on Route 1 a
    /// fixed-look trainer turns right and a hidden item moves one tile vertically; in Accumula Town (Pueblo Terracota) a fixed-look NPC turns down. An
    /// item ball and new NPC are refused; a trigger is widened and restored. Save, reopen, export, reopen, compare, restore and compare hashes; MANUAL.txt.
    /// </summary>
    internal static void Probe(string output, string source, string blackSource)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "Blanco-copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        using (var white = NdsRom.Open(source)) using (var black = NdsRom.Open(blackSource))
        {
            if (Gen5MapProfile.For(white.Header) != White || Gen5MapProfile.For(black.Header) != Black) throw new Exception("The copies are not White IRAS/0 and Black IRBO/0");
            string[] same = [Gen5MapProfile.HeadersPath, Gen5MapProfile.MatrixPath, Gen5MapProfile.BlocksPath, White.EventPath, Gen5MapProfile.TexturesPath, "a/0/1/0"];
            if (same.Any(p => !white.ReadFile(p).AsSpan().SequenceEqual(black.ReadFile(p)))) throw new Exception("A map table of White differs from Black's");
            if (white.ReadFile(White.ScriptPath).AsSpan().SequenceEqual(black.ReadFile(Black.ScriptPath))) throw new Exception("White's scripts are Black's");
            Console.WriteLine($"PASS Gen5WhiteReal_MapTablesEqualBlack_ScriptsAndTextsOwn: {string.Join(", ", same)} byte-identical; {White.ScriptPath} differs");
        }
        var manual = new List<string> { "Prueba jugable (Pokémon Blanco, edición española IRAS/0, copia privada). Respalda la partida antes. Las cuatro pruebas se hacen con una partida NUEVA, en orden: la 1 en Pueblo Arcilla tras recibir el primer Pokémon; la 2 en la Ruta 1; la 3 en Pueblo Terracota; la 4 en la Ruta 3 (después de Ciudad Gres). Ninguna edición toca tu casa ni el laboratorio de la Profesora Encina." };
        string built = Path.Combine(output, "Blanco-eventos.nds"), restored = Path.Combine(output, "Blanco-restaurado.nds");
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var data = events.Data;
            if (data is not Gen5ZoneEvents gen5 || !Equals(data.Profile, White)) throw new Exception("The copy did not open with the White profile");
            var files = data.Maps.GroupBy(m => m.EventFile).Select(g => g.First().Id).ToList(); var all = files.SelectMany(data.Events).ToList();
            var warps = all.Where(e => e.Kind == PlatinumEventKind.Warp).ToList(); int resolved = warps.Count(w => data.WarpTargetExists(w.Destination, w.Anchor));
            if (data.Maps.Count != 427 || all.Count(e => e.Kind == PlatinumEventKind.Npc) != 2257 || warps.Count != 886 || resolved != 880) throw new Exception("Census differs");
            Console.WriteLine($"PASS Gen5WhiteReal_ProfileAndCensus: {data.Maps.Count} maps, {all.Count(e => e.Kind == PlatinumEventKind.Npc)} NPC ({all.Count(e => e.IsGroundItem)} item balls), {warps.Count} warps ({resolved} resolve, {warps.Count - resolved} special), {all.Count(e => e.EditLock is not null)} locked; names: {data.Map(389).Name}, {data.Map(317).Name}, {data.Map(397).Name}");
            var (terrain, tNote) = project.LoadTerrain(); var (assets, aNote) = project.LoadMapAssets();
            if (terrain is null || assets is null) throw new Exception($"Terrain or graphics did not open: {tNote} {aNote}");
            Console.WriteLine("PASS Gen5WhiteReal_TerrainAndGraphicsOpen");

            var saves = new List<string>();
            void Save(NdsEventProposal p, string name) { string file = Path.Combine(output, name); events.Save(p, file); project.EventChanged(); saves.Add(file); }
            PlatinumMap Map(int id, string name) => data.Map(id) is { Matrix: 0 } m && m.Name == name ? m : throw new Exception($"Map {id} is not {name}");
            int step = 1;
            var arcilla = Map(389, "Pueblo Arcilla"); var doors = data.Events(arcilla.Id).Where(e => e.Kind == PlatinumEventKind.Warp).ToList();
            // 390/391 player's house, 396 the professor's lab, 392 and 394 the neighbours' houses (Black's test went 392 → 394; this one goes the other way).
            var from = doors.Single(d => d.Destination == 394); var to = doors.Single(d => d.Destination == 392);
            Save(events.PreviewEvent(arcilla.Id, PlatinumEventKind.Warp, from.Index, PlatinumEvents.EventValues(from) with { Destination = to.Destination, Anchor = to.Anchor }), $"{step}-warp.ndsevents.json");
            manual.Add($"{step++}. Pueblo Arcilla: la casa de abajo a la DERECHA (puerta en X {from.X}, Y {from.Y}) ahora lleva al interior de la casa de abajo a la IZQUIERDA (la de la puerta en X {to.X}, Y {to.Y}). Entra por la puerta de abajo a la derecha: debes aparecer dentro de la casa de abajo a la izquierda. Al salir apareces ante la puerta de abajo a la izquierda (el warp de vuelta no cambia).");

            var route1 = Map(317, "Ruta 1"); var layout = data.Layout(route1.Id);
            var trainer = data.Events(route1.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 1 && e.Movement == 16 && e.Direction == 2 && e.EditLock is null);
            Save(events.Preview(route1.Id, trainer.Index, new(trainer.X, trainer.Y, 3, 17)), $"{step}-entrenador.ndsevents.json");
            manual.Add($"{step++}. Ruta 1, entrenador NPC {trainer.Index} (script {trainer.Script}, X {trainer.X}, Y {trainer.Y}): mira hacia la DERECHA en lugar de la izquierda (movimiento 16 → 17). Su alcance de visión no se ha tocado. Si aún no lo has vencido, comprueba que mira a la derecha y que te ve al pasar por su derecha, no por su izquierda.");
            var terracota = Map(397, "Pueblo Terracota");
            var look = data.Events(terracota.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0 && e.Flag == 0 && e.EditLock is null && !e.IsGroundItem && e.Movement == 14 && e.Direction == 0);
            Save(events.Preview(terracota.Id, look.Index, new(look.X, look.Y, 1, 15)), $"{step}-npc.ndsevents.json");
            manual.Add($"{step++}. Pueblo Terracota, NPC {look.Index} (X {look.X}, Y {look.Y}, script {look.Script}): mira hacia ABAJO en lugar de arriba (movimiento «mirar fijo» 14 → 15). Míralo antes de hablarle; al hablarle se gira hacia ti.");

            // Route 1 has a single hidden item whose only free neighbour was Black's test, and Route 2 has none: the first hidden item of Route 3 (early in
            // the game, after Striaton City) with a free neighbouring tile.
            var route3 = data.Maps.First(m => m.Name == "Ruta 3" && m.Matrix == 0); var layout3 = data.Layout(route3.Id);
            (int Dx, int Dy)[] around = [(0, -1), (0, 1), (1, 0), (-1, 0)];
            var (item, (dx, dy)) = data.Events(route3.Id).Where(e => e.Kind == PlatinumEventKind.Object && e.Type == 2 && e.EditLock is null)
                .Select(e => (Item: e, Step: around.FirstOrDefault(d => terrain.Status(layout3, e.X + d.Dx, e.Y + d.Dy) == PlatinumTerrainStatus.ClearOfCollision)))
                .First(c => c.Step != default);
            Save(events.PreviewEvent(route3.Id, PlatinumEventKind.Object, item.Index, PlatinumEvents.EventValues(item) with { X = item.X + dx, Y = item.Y + dy }), $"{step}-objeto.ndsevents.json");
            string where = dy < 0 ? "arriba" : dy > 0 ? "abajo" : dx > 0 ? "a la derecha" : "a la izquierda";
            manual.Add($"{step++}. Ruta 3 (entre Ciudad Gres y Ciudad Esmalte), objeto oculto {item.Index} (script {item.Script}): de X {item.X}, Y {item.Y} a X {item.X + dx}, Y {item.Y + dy} (una casilla {where}). Pulsa A en la casilla nueva; la anterior ya no debe dar el objeto.");

            var ball = data.Events(route1.Id).First(e => e.IsGroundItem);
            if (!Throws<InvalidOperationException>(() => data.ReplaceNpc(route1.Id, ball.Index, PlatinumEvents.Values(ball), new(ball.X + 1, ball.Y, ball.Direction)))) throw new Exception("An item ball was moved");
            if (!Throws<InvalidOperationException>(() => events.PreviewAdd(route1.Id, new byte[36]))) throw new Exception("White accepted a new NPC");
            var trig = data.Events(route1.Id).First(e => e.Kind == PlatinumEventKind.Trigger && e.EditLock is null);
            Save(events.PreviewEvent(route1.Id, PlatinumEventKind.Trigger, trig.Index, PlatinumEvents.EventValues(trig) with { Width = trig.RangeX + 1 }), "5-activador-tecnico.ndsevents.json");
            Save(events.PreviewUndo(), "6-activador-restaurado.ndsevents.json");
            Console.WriteLine("PASS Gen5WhiteReal_ItemBallsAndNewNpcAreRefused_TriggerEditedAndRestored");
            if (gen5.EditableHash() != White.EditableEventsSha256) throw new Exception("An edit changed a protected byte");

            project.Export(built);
            using (var reopened = NdsProject.Open(saves[^1], false))
            {
                if (!reopened.Events.Data.Bytes().SequenceEqual(events.Data.Bytes())) throw new Exception("Checkpoint did not persist");
                while (reopened.Events.CanUndo) { reopened.Events.Save(reopened.Events.PreviewUndo(), Path.Combine(output, $"restaurar-{reopened.Events.Revision}.ndsevents.json")); reopened.EventChanged(); }
                reopened.Export(restored);
            }
            using (var rebuilt = NdsProject.Open(built, false))
                if (!rebuilt.Events.Data.Bytes().SequenceEqual(events.Data.Bytes()) || !Equals(rebuilt.Events.Data.Profile, White)) throw new Exception("Exported ROM did not persist");
            using var baseline = NdsRom.Open(copy); using var builtRom = NdsRom.Open(built);
            foreach (var file in baseline.Files) if (file.Path != White.EventPath && !baseline.ReadFile(file).SequenceEqual(builtRom.ReadFile(file.Path))) throw new Exception("Unrelated file changed: " + file.Path);
            if (!baseline.ReadArm9().SequenceEqual(builtRom.ReadArm9())) throw new Exception("ARM9 changed");
            Console.WriteLine($"PASS Gen5WhiteReal_SaveReopenExportReopenAndUndo: {saves.Count} checkpoints; exported ROM reopens in the editor; ARM9 and every other NDS file byte-exact");
        }
        File.Delete(copy);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(restored))) != hash)
            throw new Exception("Source changed or whole-ROM restoration differs");
        manual.Add($"{manual.Count}. Blanco-restaurado.nds es idéntica (SHA-256) a la copia original; no hace falta probarla en juego.");
        File.WriteAllLines(Path.Combine(output, "MANUAL.txt"), manual);
        Console.WriteLine("PASS Gen5WhiteReal_OriginalAndRestoredRomHashesMatch; gameplay NOT tested");
    }
    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}
