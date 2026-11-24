using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.ProjectDetection;

/// <summary>
/// The map and event editor on Pokémon White 2 (IRDO/0): the Gen 5 reader with White 2's own tables and paths, and the map-id limit of the matrix
/// presentation taken from the game's header count (615) instead of Platinum's 593. Synthetic checks, off-screen checks with a White 2 session, and a probe
/// on a private White 2 copy. No gameplay.
/// </summary>
internal static class NdsGen5White2Checks
{
    private static readonly Gen5MapProfile White2 = Gen5MapProfile.White2;
    /// <summary>The Black fixture with 610 headers: headers 3…609 copy header 2 (the interior), so map 605 exists only beyond Platinum's 593.</summary>
    internal sealed record Fixture(byte[] Headers, byte[] Events, byte[] Matrices, byte[] Scripts)
    {
        internal Gen5ZoneEvents Read() => Gen5ZoneEvents.Parse(White2, Headers, Events, Matrices, Scripts, ["Ciudad A", "Ruta B", "Casa C"]);
        internal byte[] Rom(string code = "IRDO") => NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]>
        { [Gen5MapProfile.HeadersPath] = Headers, [Gen5MapProfile.MatrixPath] = Matrices, [White2.EventPath] = Events, [White2.ScriptPath] = Scripts, ["untouched.bin"] = [7, 1, 99, 18] }, code, 0);
    }
    internal const int Far = 605;
    internal static Fixture Make()
    {
        var b = NdsGen5BlackChecks.Make(); byte[] three = Narc.Read(b.Headers).Files[0]; byte[] headers = new byte[610 * 48];
        three.CopyTo(headers, 0); for (int i = 3; i < 610; i++) three.AsSpan(2 * 48, 48).CopyTo(headers.AsSpan(i * 48));
        return new(NdsGen5RandomizerChecks.BuildNarc([headers]), b.Events, b.Matrices, b.Scripts);
    }

    internal static void Run(Action<bool, string> check)
    {
        // ---- profile
        check(Gen5MapProfile.For("IRDO", 0) == White2 && NdsEventProfiles.For("IRDO", 0) is Gen5MapProfile { GameName: "Blanco 2" } && Gen5MapProfile.For("IREO", 0) is null && Gen5MapProfile.For("IRDO", 1) is null,
            "Gen5White2_OnlyWhite2Rev0HasAProfile_NotBlack2");
        check(White2 is { EventPath: "a/1/2/6", ScriptPath: "a/0/5/6", HeaderCount: 615, EventFileCount: 616, MatrixCount: 416, BlockCount: 1065, TextureSetCount: 409, ScriptFileCount: 1289, LocationNamesText: 109, MaxMatrixSide: 29 }
            && White2.EditableEventsSha256 != Gen5MapProfile.Black.EditableEventsSha256 && White2.HeadersCrc32 != Gen5MapProfile.Black.HeadersCrc32, "Gen5White2_OwnPathsCountsAndFingerprints");
        check(White2 is { SupportsNpcCreation: false, EditsObjectFacing: false, MaxTrainerSight: 0 }, "Gen5White2_SameConservativeRules");

        // ---- the map-id limit: the game's header count, not Platinum's
        var f = Make(); var data = f.Read();
        check(data.Maps.Count == 610 && data.Map(Far) is { Matrix: 1, Width: 32 } && data.Layout(Far) is { MapId: Far } layout && layout.Cell(0, 0).LandDataId == 2, "Gen5White2_AMapBeyond593OpensItsMatrix");
        check(data.Events(Far).Single().Kind == PlatinumEventKind.Warp && data.WarpTargetExists(0, 0), "Gen5White2_EventsOfAMapBeyond593");
        byte[] matrix = [1, 1, 0, 0, 0, 2, 0];
        check(Throws<ArgumentOutOfRangeException>(() => PlatinumMapLayout.ParseMatrix(matrix, 600, 0)) && Throws<ArgumentOutOfRangeException>(() => PlatinumMapLayout.ParseMatrix(matrix, 600, 0, 30)),
            "Gen5White2_Gen4OverloadsKeepPlatinumsLimit");
        check(PlatinumMapLayout.ParseMatrix(matrix, 614, 0, 30, 615).MapId == 614 && Throws<ArgumentOutOfRangeException>(() => PlatinumMapLayout.ParseMatrix(matrix, 615, 0, 30, 615))
            && Throws<ArgumentOutOfRangeException>(() => PlatinumMapLayout.ParseMatrix(matrix, 0, 0, 30, 0)) && Throws<ArgumentOutOfRangeException>(() => PlatinumMapLayout.ParseMatrix(matrix, 0, 0, 30, 0x10000)),
            "Gen5White2_TheLimitIsTheGamesHeaderCount");
        check(Throws<ArgumentOutOfRangeException>(() => data.Map(610)), "Gen5White2_NoMapBeyondTheTable");
        var black = NdsGen5BlackChecks.Make().Read();
        check(Throws<ArgumentOutOfRangeException>(() => black.Map(605)) && black.Layout(NdsGen5BlackChecks.House).MapId == NdsGen5BlackChecks.House, "Gen5White2_BlackKeepsItsOwnTable");

        // ---- edits on a map beyond 593, checkpoints, export, restore, detector
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen5-white2-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "blanco2.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source);
            check(Throws<InvalidDataException>(() => Gen5ZoneEvents.Open(rom)) && Throws<Exception>(() => Gen5MapGraphics.Open(rom)), "Gen5White2_UnaccreditedWhite2TablesAreRefused");
            var session = new NdsEventSession(rom, f.Read());
            var w = session.Data.Event(Far, PlatinumEventKind.Warp, 0); var wv = PlatinumEvents.EventValues(w);
            string first = Path.Combine(temp, "1.ndsevents.json"); session.Save(session.PreviewEvent(Far, PlatinumEventKind.Warp, 0, wv with { X = w.X + 1 }), first);
            var doc = NdsEventSession.ReadDocument(first);
            check(doc is { Game: "IRDO", Revision: 0, Version: 5 } && doc.Operations![0].Map == Far, "Gen5White2_ACheckpointOnAMapBeyond593");
            var reopened = new NdsEventSession(rom, f.Read()); reopened.Load(doc, first);
            check(reopened.Data.Event(Far, PlatinumEventKind.Warp, 0).X == w.X + 1, "Gen5White2_ItReopens");
            check(Throws<InvalidDataException>(() => new NdsEventSession(rom, f.Read()).Load(doc with { Game = "IRAS" }, first)), "Gen5White2_AWhiteCheckpointIsRefusedOnWhite2");
            string built = Path.Combine(temp, "editado.nds"); rom.Save(built, new Dictionary<int, byte[]> { [rom.File(White2.EventPath).Id] = session.Replacement()! }); session.VerifyExport(built);
            using (var x = NdsRom.Open(built)) check(rom.Files.Where(e => e.Path != White2.EventPath).All(e => rom.ReadFile(e).AsSpan().SequenceEqual(x.ReadFile(e.Path))), "Gen5White2_ExportChangesOnlyTheEventArchive");
            session.Save(session.PreviewUndo(), Path.Combine(temp, "2.ndsevents.json"));
            check(session.Replacement() is null && NdsMapCanvasChecks.Sha(source) == sourceHash, "Gen5White2_RestoringGivesTheSourceBack");
            string roms = Path.Combine(temp, "detector");
            string wSource = ProjectDetectorChecks.Write(roms, "Blanco2.nds", ProjectDetectorChecks.Rom("IRDO", 0)), bSource = ProjectDetectorChecks.Write(roms, "Negro.nds", ProjectDetectorChecks.Rom("IRBO", 0));
            check(ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "b2.ndsevents.json", wSource, 1, "IRDO", 0)) is { Status: DetectionStatus.Compatible } ok && ok.Game.Contains("Blanco 2")
                && ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "cruzado.ndsevents.json", bSource, 1, "IRDO", 0)).Status == DetectionStatus.Unsupported
                && ProjectDetector.Detect(ProjectDetectorChecks.Checkpoint(roms, "negro2.ndsevents.json", wSource, 1, "IREO", 0)).Status == DetectionStatus.Unsupported, "Gen5White2_DetectorAcceptsWhite2_RefusesMixedAndBlack2");
        }
        finally { Directory.Delete(temp, true); }
    }

    // ------------------------------------------------------------------------------------------------ WPF (off-screen)
    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen5-white2-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = Make(); string source = Path.Combine(temp, "blanco2.nds"); File.WriteAllBytes(source, f.Rom());
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read());
            var view = new NdsEventEditorView(session); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            check(NdsMapCanvasChecks.Children<TextBlock>(view).Any(t => t.Text == "Eventos · Blanco 2 IRDO/0"), "Gen5White2Ui_TitleNamesTheGame");
            var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single();
            check(maps.Items.Count == 610, "Gen5White2Ui_EveryMapIsListed");
            maps.SelectedItem = maps.Items[Far]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single();
            check(canvas.Model is { } model && model.Layout.MapId == Far && NdsMapCanvasChecks.Children<DataGrid>(view).ToList()[2].Items.Count == 1, "Gen5White2Ui_AMapBeyond593OpensOnTheCanvas");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "gen5-white2-editor-1100");
        }
        finally { Directory.Delete(temp, true); }
    }

    // ------------------------------------------------------------------------------------------------ real ROM (private copy)
    /// <summary>
    /// A private White 2 IRDO/0 copy: profile and census; every one of the 615 maps opens its matrix (ids up to 614); terrain and graphics. In Aspertia City
    /// the door of one plain house leads into another; an NPC that does not move turns up; on Route 20 a fixed-look trainer turns and a hidden item moves
    /// one tile. Item balls and new NPC are refused; a trigger is widened and restored. Save, reopen, export, reopen, compare, restore; MANUAL.txt.
    /// </summary>
    internal static void Probe(string output, string source)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "Blanco2-copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        var manual = new List<string> { "Prueba jugable (Pokémon Blanco 2 IRDO/0, copia privada; textos en inglés). Respalda la partida antes. Las cuatro pruebas se hacen con una partida NUEVA, en orden: la 1 y la 2 en Ciudad Engobe (Aspertia City) tras recibir el primer Pokémon; la 3 y la 4 en la Ruta 20 (después de Pueblo Floresta). Ninguna edición toca tu casa, la del rival ni la Escuela de Entrenadores." };
        string built = Path.Combine(output, "Blanco2-eventos.nds"), restored = Path.Combine(output, "Blanco2-restaurado.nds");
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var data = events.Data;
            if (data is not Gen5ZoneEvents gen5 || !Equals(data.Profile, White2)) throw new Exception("The copy did not open with the White 2 profile");
            var files = data.Maps.GroupBy(m => m.EventFile).Select(g => g.First().Id).ToList(); var all = files.SelectMany(data.Events).ToList();
            var warps = all.Where(e => e.Kind == PlatinumEventKind.Warp).ToList(); int resolved = warps.Count(w => data.WarpTargetExists(w.Destination, w.Anchor));
            if (data.Maps.Count != 615 || all.Count(e => e.Kind == PlatinumEventKind.Npc) != 3146 || warps.Count != 1089 || resolved != 1084 || warps.Where(w => !data.WarpTargetExists(w.Destination, w.Anchor)).Any(w => w.Destination != 0xFFFF))
                throw new Exception("Census differs");
            int layouts = data.Maps.Count(m => data.Layout(m.Id).MapId == m.Id);
            if (layouts != 615) throw new Exception("Some map did not open its matrix");
            Console.WriteLine($"PASS Gen5White2Real_ProfileCensusAndEveryMatrix: {data.Maps.Count} maps (all open their matrix, {data.Maps.Count(m => m.Id >= 593)} beyond 593), {all.Count(e => e.Kind == PlatinumEventKind.Npc)} NPC ({all.Count(e => e.IsGroundItem)} item balls), {warps.Count} warps ({resolved} resolve, {warps.Count - resolved} with destination 65535), {all.Count(e => e.EditLock is not null)} locked");
            var (terrain, tNote) = project.LoadTerrain(); var (assets, aNote) = project.LoadMapAssets();
            if (terrain is null || assets is null) throw new Exception($"Terrain or graphics did not open: {tNote} {aNote}");
            Console.WriteLine("PASS Gen5White2Real_TerrainAndGraphicsOpen");

            var saves = new List<string>();
            void Save(NdsEventProposal p, string name) { string file = Path.Combine(output, name); events.Save(p, file); project.EventChanged(); saves.Add(file); }
            PlatinumMap Map(int id, string name) => data.Map(id) is { Matrix: 0 } m && m.Name == name ? m : throw new Exception($"Map {id} is not {name}");
            int step = 1;
            var aspertia = Map(427, "Aspertia City"); var doors = data.Events(aspertia.Id).Where(e => e.Kind == PlatinumEventKind.Warp).ToList();
            // 428 and 429/430 hold story NPC (flags); 431 and 433 are plain houses (same interior, NPC without flags); 435 Pokémon Center, 436 the school.
            var from = doors.Single(d => d.Destination == 431); var to = doors.Single(d => d.Destination == 433);
            if (data.Events(431).Concat(data.Events(433)).Any(e => e.Kind == PlatinumEventKind.Npc && e.Flag != 0)) throw new Exception("The chosen houses are not plain");
            Save(events.PreviewEvent(aspertia.Id, PlatinumEventKind.Warp, from.Index, PlatinumEvents.EventValues(from) with { Destination = to.Destination, Anchor = to.Anchor }), $"{step}-warp.ndsevents.json");
            manual.Add($"{step++}. Ciudad Engobe: la casa cuya puerta está en X {from.X}, Y {from.Y} (abajo a la izquierda) ahora lleva al interior de la casa de la puerta en X {to.X}, Y {to.Y} (arriba a la derecha). Las dos casas tienen el mismo interior, pero distintos vecinos: en la de abajo a la izquierda hay dos personas junto a la pared superior izquierda; en la de arriba a la derecha hay tres, una de ellas junto a la pared derecha. Entra por la puerta de abajo a la izquierda: debes ver a los vecinos de la casa de arriba a la derecha. Al salir apareces ante la puerta de arriba a la derecha.");
            var still = data.Events(aspertia.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0 && e.Flag == 0 && e.Movement == 0 && !e.IsGroundItem && e.EditLock is null && e.Direction == 1);
            Save(events.Preview(aspertia.Id, still.Index, new(still.X, still.Y, 0)), $"{step}-npc.ndsevents.json");
            manual.Add($"{step++}. Ciudad Engobe, NPC {still.Index} (X {still.X}, Y {still.Y}, script {still.Script}; no se mueve solo): mira hacia arriba en lugar de abajo. Es un NPC sin movimiento propio (movimiento 0): solo cambia la dirección guardada. Míralo antes de hablarle.");

            var route20 = Map(446, "Route 20"); var layout = data.Layout(route20.Id);
            var trainer = data.Events(route20.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 1 && e.Movement == 14 && e.Direction == 0 && e.EditLock is null);
            Save(events.Preview(route20.Id, trainer.Index, new(trainer.X, trainer.Y, 1, 15)), $"{step}-entrenador.ndsevents.json");
            manual.Add($"{step++}. Ruta 20, entrenador NPC {trainer.Index} (script {trainer.Script}, X {trainer.X}, Y {trainer.Y}): mira hacia abajo en lugar de arriba (movimiento 14 → 15). Su visión no se ha tocado. Si aún no lo has vencido, comprueba que mira hacia abajo y que te ve al pasar por debajo de él, no por encima.");
            (int Dx, int Dy)[] around = [(0, -1), (0, 1), (1, 0), (-1, 0)];
            var (item, (dx, dy)) = data.Events(route20.Id).Where(e => e.Kind == PlatinumEventKind.Object && e.Type == 2 && e.EditLock is null && !data.Events(route20.Id).Any(n => n.Kind == PlatinumEventKind.Npc && n.X == e.X && n.Y == e.Y))
                .Select(e => (Item: e, Step: around.FirstOrDefault(d => terrain.Status(layout, e.X + d.Dx, e.Y + d.Dy) == PlatinumTerrainStatus.ClearOfCollision)))
                .First(c => c.Step != default);
            Save(events.PreviewEvent(route20.Id, PlatinumEventKind.Object, item.Index, PlatinumEvents.EventValues(item) with { X = item.X + dx, Y = item.Y + dy }), $"{step}-objeto.ndsevents.json");
            string where = dy < 0 ? "arriba" : dy > 0 ? "abajo" : dx > 0 ? "a la derecha" : "a la izquierda";
            manual.Add($"{step++}. Ruta 20, objeto oculto {item.Index} (script {item.Script}): de X {item.X}, Y {item.Y} a X {item.X + dx}, Y {item.Y + dy} (una casilla {where}). Pulsa A en la casilla nueva; la anterior ya no debe dar el objeto.");

            var ball = data.Events(route20.Id).First(e => e.IsGroundItem);
            if (!Throws<InvalidOperationException>(() => data.ReplaceNpc(route20.Id, ball.Index, PlatinumEvents.Values(ball), new(ball.X + 1, ball.Y, ball.Direction)))) throw new Exception("An item ball was moved");
            if (!Throws<InvalidOperationException>(() => events.PreviewAdd(route20.Id, new byte[36]))) throw new Exception("White 2 accepted a new NPC");
            var trig = data.Events(route20.Id).First(e => e.Kind == PlatinumEventKind.Trigger && e.EditLock is null);
            Save(events.PreviewEvent(route20.Id, PlatinumEventKind.Trigger, trig.Index, PlatinumEvents.EventValues(trig) with { Width = trig.RangeX + 1 }), "5-activador-tecnico.ndsevents.json");
            Save(events.PreviewUndo(), "6-activador-restaurado.ndsevents.json");
            var far = data.Maps.First(m => m.Id >= 593 && data.Events(m.Id).Any(e => e.Kind == PlatinumEventKind.Npc && e.EditLock is null && !e.IsGroundItem));
            var farNpc = data.Events(far.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.EditLock is null && !e.IsGroundItem);
            Save(events.Preview(far.Id, farNpc.Index, new(farNpc.X, farNpc.Y, (farNpc.Direction + 1) % 4, PlatinumTrainerVision.IsFixedLook(farNpc.Movement) ? PlatinumTrainerVision.FixedLookFor((farNpc.Direction + 1) % 4) : null)), "7-mapa-alto-tecnico.ndsevents.json");
            Save(events.PreviewUndo(), "8-mapa-alto-restaurado.ndsevents.json");
            Console.WriteLine($"PASS Gen5White2Real_ItemBallsAndNewNpcAreRefused_TriggerAndMap{far.Id}EditedAndRestored");
            if (gen5.EditableHash() != White2.EditableEventsSha256) throw new Exception("An edit changed a protected byte");

            project.Export(built);
            using (var reopened = NdsProject.Open(saves[^1], false))
            {
                if (!reopened.Events.Data.Bytes().SequenceEqual(events.Data.Bytes())) throw new Exception("Checkpoint did not persist");
                while (reopened.Events.CanUndo) { reopened.Events.Save(reopened.Events.PreviewUndo(), Path.Combine(output, $"restaurar-{reopened.Events.Revision}.ndsevents.json")); reopened.EventChanged(); }
                reopened.Export(restored);
            }
            using (var rebuilt = NdsProject.Open(built, false))
                if (!rebuilt.Events.Data.Bytes().SequenceEqual(events.Data.Bytes()) || !Equals(rebuilt.Events.Data.Profile, White2)) throw new Exception("Exported ROM did not persist");
            using var baseline = NdsRom.Open(copy); using var builtRom = NdsRom.Open(built);
            foreach (var file in baseline.Files) if (file.Path != White2.EventPath && !baseline.ReadFile(file).SequenceEqual(builtRom.ReadFile(file.Path))) throw new Exception("Unrelated file changed: " + file.Path);
            if (!baseline.ReadArm9().SequenceEqual(builtRom.ReadArm9())) throw new Exception("ARM9 changed");
            Console.WriteLine($"PASS Gen5White2Real_SaveReopenExportReopenAndUndo: {saves.Count} checkpoints; exported ROM reopens in the editor; ARM9 and every other NDS file byte-exact");
        }
        File.Delete(copy);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(restored))) != hash)
            throw new Exception("Source changed or whole-ROM restoration differs");
        manual.Add($"{manual.Count}. Blanco2-restaurado.nds es idéntica (SHA-256) a la copia original; no hace falta probarla en juego.");
        File.WriteAllLines(Path.Combine(output, "MANUAL.txt"), manual);
        Console.WriteLine("PASS Gen5White2Real_OriginalAndRestoredRomHashesMatch; gameplay NOT tested");
    }
    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}
