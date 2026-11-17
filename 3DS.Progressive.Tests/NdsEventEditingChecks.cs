using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Nds.Project;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Border = System.Windows.Controls.Border;
using Point = System.Windows.Point;

/// <summary>
/// Editing existing background objects, warps and triggers (Platinum CPUE/1): byte-exact writes, destination validation, protected fields, checkpoint v5,
/// project reader 3 and the contextual WPF panel. Fixture of phase 1: map 0 and map 2 share event file 0 (object, NPC, warp to map 1, trigger).
/// </summary>
internal static class NdsEventEditingChecks
{
    private static void Expect<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); } catch (T) { check(true, name); return; }
        throw new Exception("FAIL expected " + typeof(T).Name + ": " + name);
    }
    private static int[] Changed(byte[] a, byte[] b) => Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToArray();
    private static int Start(byte[] archive, PlatinumEvent e) { var raw = Convert.FromHexString(e.Raw); for (int i = 0; i + raw.Length <= archive.Length; i++) if (archive.AsSpan(i, raw.Length).SequenceEqual(raw)) return i; throw new Exception("record not found"); }

    internal static void Run(Action<bool, string> check)
    {
        var f = NdsEventChecks.Make(); var data = f.Read(); byte[] original = data.Bytes();
        var obj = data.Event(0, PlatinumEventKind.Object, 0); var warp = data.Event(0, PlatinumEventKind.Warp, 0); var trig = data.Event(0, PlatinumEventKind.Trigger, 0);
        check(PlatinumEvents.EventValues(obj) == new PlatinumEventValues(3, 4, Facing: 0) && PlatinumEvents.EventValues(warp) == new PlatinumEventValues(1, 2, Destination: 1, Anchor: 0)
            && PlatinumEvents.EventValues(trig) == new PlatinumEventValues(3, 4, Width: 1, Length: 2), "EventEdit_ValuesExposeOnlyTheFieldsOfEachKind");
        check(Enumerable.Range(0, 7).Select(PlatinumEventFields.FacingName).Distinct().Count() == 7 && PlatinumEventFields.FacingName(4) == "Desde cualquier lado" && PlatinumEventFields.ObjectTypeName(2) == "objeto oculto", "EventEdit_InteractionDirectionsHaveNames");
        Expect<ArgumentException>(() => PlatinumEvents.EventValues(data.Npc(0, 0)), check, "EventEdit_NpcUseTheirOwnValues");

        // Objects: position (32-bit) and interaction direction only
        int os = Start(original, obj);
        var movedObj = data.ReplaceEvent(0, PlatinumEventKind.Object, 0, PlatinumEvents.EventValues(obj), new(10, 11, Facing: 4));
        check(Changed(original, movedObj).Select(i => i - os).SequenceEqual([4, 8, 16]) && data.WithEvents(movedObj).Event(0, PlatinumEventKind.Object, 0) is { X: 10, Y: 11, Direction: 4, Script: 1, Type: 2 }, "EventEdit_ObjectWritesPositionAndDirectionOnly");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceEvent(0, PlatinumEventKind.Object, 0, PlatinumEvents.EventValues(obj), new(3, 4, Facing: 7)), check, "EventEdit_UnknownInteractionDirectionIsRefused");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceEvent(0, PlatinumEventKind.Object, 0, PlatinumEvents.EventValues(obj), new(32, 4, Facing: 0)), check, "EventEdit_ObjectOutsideTheBoundsIsRefused");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceEvent(0, PlatinumEventKind.Object, 0, PlatinumEvents.EventValues(obj), new(-1, 4, Facing: 0)), check, "EventEdit_NegativePositionIsRefused");

        // Warps: position; destination only to an existing warp of an existing map
        int ws = Start(original, warp);
        var retarget = data.ReplaceEvent(0, PlatinumEventKind.Warp, 0, PlatinumEvents.EventValues(warp), new(5, 6, Destination: 0, Anchor: 0));
        check(Changed(original, retarget).Select(i => i - ws).SequenceEqual([0, 2, 4]) && data.WithEvents(retarget).Event(0, PlatinumEventKind.Warp, 0) is { X: 5, Y: 6, Destination: 0, Anchor: 0 }, "EventEdit_WarpWritesPositionAndDestinationOnly");
        check(data.WarpTargetExists(1, 0) && !data.WarpTargetExists(1, 1) && !data.WarpTargetExists(9, 0) && !data.WarpTargetExists(1, PlatinumEventFields.DynamicAnchor), "EventEdit_WarpTargetsMustExist");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceEvent(0, PlatinumEventKind.Warp, 0, PlatinumEvents.EventValues(warp), new(1, 2, Destination: 1, Anchor: 1)), check, "EventEdit_AMissingArrivalWarpIsRefused");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceEvent(0, PlatinumEventKind.Warp, 0, PlatinumEvents.EventValues(warp), new(1, 2, Destination: 9, Anchor: 0)), check, "EventEdit_AMissingMapIsRefused");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceEvent(0, PlatinumEventKind.Warp, 0, PlatinumEvents.EventValues(warp), new(1, 2, Destination: 1, Anchor: PlatinumEventFields.DynamicAnchor)), check, "EventEdit_AWarpIsNeverMadeDynamic");
        // A dynamic warp (anchor 256): it can move, its destination is the save's and is never written.
        byte[] dynamicArchive = (byte[])original; dynamicArchive = (byte[])dynamicArchive.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(dynamicArchive.AsSpan(ws + 6), PlatinumEventFields.DynamicAnchor);
        var dyn = PlatinumEvents.Parse(f.Headers, f.Names, dynamicArchive, f.Matrices, f.Scripts); var dynWarp = dyn.Event(0, PlatinumEventKind.Warp, 0);
        check(dyn.ReplaceEvent(0, PlatinumEventKind.Warp, 0, PlatinumEvents.EventValues(dynWarp), PlatinumEvents.EventValues(dynWarp) with { X = 7 }) is { } moved && Changed(dynamicArchive, moved).Select(i => i - ws).SequenceEqual([0]), "EventEdit_ADynamicWarpCanMove");
        Expect<ArgumentOutOfRangeException>(() => dyn.ReplaceEvent(0, PlatinumEventKind.Warp, 0, PlatinumEvents.EventValues(dynWarp), PlatinumEvents.EventValues(dynWarp) with { Anchor = 0, Destination = 1 }), check, "EventEdit_ADynamicWarpKeepsItsDestination");
        var tamperDyn = (byte[])dynamicArchive.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(tamperDyn.AsSpan(ws + 4), 2);
        Expect<InvalidDataException>(() => dyn.WithEvents(tamperDyn), check, "EventEdit_TheFingerprintProtectsDynamicDestinations");

        // Triggers: position and size within the bounds; script, variable and value protected
        int ts = Start(original, trig);
        var resized = data.ReplaceEvent(0, PlatinumEventKind.Trigger, 0, PlatinumEvents.EventValues(trig), new(4, 5, Width: 3, Length: 4));
        check(Changed(original, resized).Select(i => i - ts).SequenceEqual([2, 4, 6, 8]) && data.WithEvents(resized).Event(0, PlatinumEventKind.Trigger, 0) is { X: 4, Y: 5, RangeX: 3, RangeY: 4, Script: 1 }, "EventEdit_TriggerWritesPositionAndSizeOnly");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceEvent(0, PlatinumEventKind.Trigger, 0, PlatinumEvents.EventValues(trig), new(3, 4, Width: 0, Length: 2)), check, "EventEdit_AnEmptyTriggerIsRefused");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceEvent(0, PlatinumEventKind.Trigger, 0, PlatinumEvents.EventValues(trig), new(30, 4, Width: 3, Length: 2)), check, "EventEdit_ATriggerAreaOutsideTheBoundsIsRefused");
        Expect<ArgumentOutOfRangeException>(() => data.ReplaceEvent(0, PlatinumEventKind.Trigger, 0, PlatinumEvents.EventValues(trig), new(0, 0, Width: 33, Length: 1)), check, "EventEdit_ATriggerLargerThanABlockIsRefused");
        var tamperScript = (byte[])original.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(tamperScript.AsSpan(ts), 9);
        Expect<InvalidDataException>(() => data.WithEvents(tamperScript), check, "EventEdit_TriggerScriptsStayProtected");
        var tamperType = (byte[])original.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(tamperType.AsSpan(os + 2), 0);
        Expect<InvalidDataException>(() => data.WithEvents(tamperType), check, "EventEdit_ObjectTypesStayProtected");

        // Common rules
        Expect<InvalidOperationException>(() => data.ReplaceEvent(0, PlatinumEventKind.Object, 0, new(9, 9, Facing: 0), new(10, 10, Facing: 0)), check, "EventEdit_StaleValuesAreRefused");
        Expect<ArgumentException>(() => data.ReplaceEvent(0, PlatinumEventKind.Object, 0, PlatinumEvents.EventValues(obj), new(5, 5)), check, "EventEdit_BeforeAndAfterMustCarryTheSameFields");
        Expect<ArgumentException>(() => data.ReplaceEvent(0, PlatinumEventKind.Npc, 0, new(2, 3), new(4, 3)), check, "EventEdit_NpcAreNotEditedThroughEvents");
        var all = data.WithEvents(movedObj).WithEvents(data.WithEvents(movedObj).ReplaceEvent(0, PlatinumEventKind.Trigger, 0, PlatinumEvents.EventValues(trig), new(4, 5, Width: 3, Length: 4)));
        check(all.EventsHash() == data.EventsHash() && all.EditableHash() != data.EditableHash() && all.Events(2).Single(e => e.Kind == PlatinumEventKind.Trigger).RangeX == 3, "EventEdit_EventsFingerprintIgnoresOnlyTheEditableFieldsAndSharedFilesFollow");

        string temp = Path.Combine(Path.GetTempPath(), "nds-event-editing-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try { SessionChecks(f, temp, check); }
        finally { Directory.Delete(temp, true); }
    }

    private static void SessionChecks(NdsEventChecks.Fixture f, string temp, Action<bool, string> check)
    {
        string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(source));
        using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read());
        var warp = session.Data.Event(0, PlatinumEventKind.Warp, 0);
        var proposal = session.PreviewEvent(0, PlatinumEventKind.Warp, 0, PlatinumEvents.EventValues(warp) with { X = 5, Destination = 0, Anchor = 0 });
        check(proposal.Changes.Select(c => c.Field).SequenceEqual(["Coordenada X", "Mapa de destino (Zona B → Zona A)"]) && proposal.Changes[0].Location == "Mapa 0 · Warp 0", "EventSession_PreviewNamesTheDestinationMaps");
        string v5 = Path.Combine(temp, "v5.ndsevents.json"); session.Save(proposal, v5);
        var doc = NdsEventSession.ReadDocument(v5);
        check(doc.Version == 5 && doc.Operations!.Single().Kind == NdsEventOperation.EventKind && doc.Operations![0].Event!.Kind == "Warp" && session.Data.Event(0, PlatinumEventKind.Warp, 0) is { X: 5, Destination: 0 }, "EventSession_EditsWriteAVersion5Checkpoint");
        var reopened = new NdsEventSession(rom, f.Read()); reopened.Load(doc, v5);
        check(reopened.Data.Bytes().SequenceEqual(session.Data.Bytes()) && reopened.CanUndo, "EventSession_ReopenReproducesTheBytes");
        Expect<InvalidDataException>(() => reopened.Load(doc with { Version = 4 }, v5), check, "EventSession_EditsInAnOlderVersionAreRefused");
        Expect<InvalidDataException>(() => reopened.Load(doc with { Version = 2 }, v5), check, "EventSession_EditsInVersion2AreRefused");
        var bad = doc with { Operations = [NdsEventOperation.OfEvent(doc.Operations![0].Event! with { After = doc.Operations![0].Event!.After with { Anchor = 7 } })] };
        Expect<ArgumentOutOfRangeException>(() => new NdsEventSession(rom, f.Read()).Load(bad, v5), check, "EventSession_ACheckpointWithAMissingTargetIsRefused");
        var unknownKind = doc with { Operations = [NdsEventOperation.OfEvent(doc.Operations![0].Event! with { Kind = "Npc" })] };
        Expect<InvalidDataException>(() => new NdsEventSession(rom, f.Read()).Load(unknownKind, v5), check, "EventSession_UnknownEventKindsAreRefused");
        var obj = session.Data.Event(0, PlatinumEventKind.Object, 0);
        session.Save(session.PreviewEvent(0, PlatinumEventKind.Object, 0, PlatinumEvents.EventValues(obj) with { Facing = 3 }), Path.Combine(temp, "obj.ndsevents.json"));
        check(session.Data.Event(2, PlatinumEventKind.Object, 0).Direction == 3, "EventSession_SharedFilesShowTheEditInEveryHeader");
        string built = Path.Combine(temp, "edited.nds"); rom.Save(built, new Dictionary<int, byte[]> { [rom.File(PlatinumEvents.EventPath).Id] = session.Replacement()! });
        using (var verify = NdsRom.Open(built))
        {
            var read = PlatinumEvents.Parse(f.Headers, f.Names, verify.ReadFile(PlatinumEvents.EventPath), f.Matrices, f.Scripts);
            check(read.Event(0, PlatinumEventKind.Warp, 0) is { X: 5, Destination: 0 } && read.Event(0, PlatinumEventKind.Object, 0).Direction == 3 && read.EventsHash() == f.Read().EventsHash() && verify.ReadFile("untouched.bin").SequenceEqual(rom.ReadFile("untouched.bin")), "EventSession_ExportCarriesOnlyTheEdits");
        }
        var undo = session.PreviewUndo();
        check(undo.Undo && undo.Changes.Any(c => c.Field.StartsWith("Interacción (Mirando abajo")), "EventSession_UndoPreviewsTheOldValues");
        session.Save(undo, Path.Combine(temp, "u1.ndsevents.json")); session.Save(session.PreviewUndo(), Path.Combine(temp, "u2.ndsevents.json"));
        check(session.Replacement() is null && session.Data.Bytes().SequenceEqual(f.Events), "EventSession_UndoRestoresTheOriginalArchive");
        string restored = Path.Combine(temp, "restored.nds"); rom.Save(restored, new Dictionary<int, byte[]>());
        check(SHA256.HashData(File.ReadAllBytes(restored)).SequenceEqual(sourceHash) && SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(sourceHash), "EventSession_RestoredRomIsByteExact");

        // Persistent project: reader 3 while the history edits objects, warps or triggers; back to reader 1 after undoing.
        var events = new NdsProjectEvents(doc.Operations, doc.Audit);
        check(events.CheckpointVersion == 5 && events.RequiredReader == 3 && NdsProjectCodec.ContainerVersion == 3 && NdsProjectEvents.FromCheckpoint(doc).ToCheckpoint(source, doc.SourceSha256).Version == 5, "EventProject_EditsNeedReader3");
        string platinum = Path.Combine(temp, "platino.nds"); File.WriteAllBytes(platinum, NdsGen4Checks.SyntheticRom(Gen4Profile.All.Single(p => p.GameCode == "CPUE"), true));
        using var project = NdsProject.Open(platinum, true); var own = new NdsEventSession(rom, f.Read()); project.AdoptEventSession(own);
        var medium = new NdsProjectMedium(); project.AttachPersistence(new InMemoryNdsProjectStore(medium), NdsProjectRomIdentity.Compute(platinum, true), null, "comprobaciones");
        var trig = own.Data.Event(0, PlatinumEventKind.Trigger, 0); project.SaveEvent(own.PreviewEvent(0, PlatinumEventKind.Trigger, 0, PlatinumEvents.EventValues(trig) with { Width = 3 }));
        var stored = NdsProjectCodec.Read(medium.Main!);
        check(stored.Header!.MinReaderVersion == 3 && stored.Document!.Meta.Events!.CheckpointVersion == 5, "EventProject_SavingAnEditRequiresReader3");
        var rebuilt = new NdsEventSession(rom, f.Read()); rebuilt.Restore(stored.Document!.Meta.Events!.Operations, stored.Document.Meta.Events.Audit, stored.Document.State.EventData, stored.Document.State.EventScripts);
        check(rebuilt.Data.Event(0, PlatinumEventKind.Trigger, 0).RangeX == 3, "EventProject_TheStoredHistoryRebuildsTheEdit");
        project.SaveEvent(own.PreviewUndo());
        check(NdsProjectCodec.Read(medium.Main!).Header!.MinReaderVersion == 1, "EventProject_UndoingReturnsToReader1");
    }

    // ------------------------------------------------------------------------------------------------ real ROM (private copy)
    /// <summary>
    /// Twinleaf (411) on a private CPUE/1 copy: warp 0 retargeted to the destination of another warp, one background object moved, one trigger widened (when
    /// the map has them). Save (v5), reopen, export, reopen the exported ROM in the event editor, restore and compare hashes. Writes MANUAL.txt. No gameplay.
    /// </summary>
    internal static void Probe(string output, string source)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "Platino-copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        var manual = new List<string> { "Prueba jugable (Platino CPUE/1, copia privada). Usa una partida guardada FUERA de Pueblo Hojaverde." };
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var data = events.Data; var map = data.Map(411);
            Console.WriteLine($"PASS EventEditReal_Fingerprints: events {data.EventsHash() == PlatinumEvents.EventsEditableSha256}");
            if (data.EventsHash() != PlatinumEvents.EventsEditableSha256) throw new Exception("Events fingerprint of the original differs from the accredited constant");
            var warps = data.Events(411).Where(e => e.Kind == PlatinumEventKind.Warp).ToList();
            var w0 = warps[0]; var w1 = warps.First(w => w.Index != 0 && (w.Destination != w0.Destination || w.Anchor != w0.Anchor) && w.Anchor != PlatinumEventFields.DynamicAnchor);
            var saves = new List<string>();
            void Save(NdsEventProposal p, string name) { string file = Path.Combine(output, name); events.Save(p, file); project.EventChanged(); saves.Add(file); }
            Save(events.PreviewEvent(411, PlatinumEventKind.Warp, 0, PlatinumEvents.EventValues(w0) with { Destination = w1.Destination, Anchor = w1.Anchor }), "1-warp.ndsevents.json");
            manual.Add($"1. Warp 0 de Hojaverde (X {w0.X}, Y {w0.Y}): antes llevaba a {data.Map(w0.Destination).Name} ({data.Map(w0.Destination).InternalName}), warp {w0.Anchor}; ahora lleva a {data.Map(w1.Destination).Name} ({data.Map(w1.Destination).InternalName}), warp {w1.Anchor}, como la puerta del warp {w1.Index} (X {w1.X}, Y {w1.Y}). Entra por la puerta del warp 0 y comprueba dónde apareces.");
            if (data.Events(411).FirstOrDefault(e => e.Kind == PlatinumEventKind.Object) is { } o)
            {
                Save(events.PreviewEvent(411, PlatinumEventKind.Object, o.Index, PlatinumEvents.EventValues(o) with { X = o.X + 1 }), "2-objeto.ndsevents.json");
                manual.Add($"2. Objeto de fondo {o.Index} ({PlatinumEventFields.ObjectTypeName(o.Type)}, script {o.Script}): de X {o.X}, Y {o.Y} a X {o.X + 1}. Interactúa con la casilla nueva ({PlatinumEventFields.FacingName(o.Direction).ToLowerInvariant()}); la anterior ya no debe responder.");
            }
            if (data.Events(411).FirstOrDefault(e => e.Kind == PlatinumEventKind.Trigger) is { } t)
            {
                Save(events.PreviewEvent(411, PlatinumEventKind.Trigger, t.Index, PlatinumEvents.EventValues(t) with { Width = t.RangeX + 1 }), "3-activador.ndsevents.json");
                manual.Add($"3. Activador {t.Index} (script {t.Script}): de {t.RangeX}×{t.RangeY} a {t.RangeX + 1}×{t.RangeY} casillas desde X {t.X}, Y {t.Y}; la casilla X {t.X + t.RangeX} también lo dispara (si su condición se cumple).");
            }
            if (NdsEventSession.ReadDocument(saves[^1]).Version != 5) throw new Exception("Checkpoint is not version 5");
            string built = Path.Combine(output, "Platino-eventos.nds"); project.Export(built);
            using (var reopened = NdsProject.Open(saves[^1], false))
            {
                if (!reopened.Events.Data.Bytes().SequenceEqual(events.Data.Bytes())) throw new Exception("Checkpoint did not persist");
                while (reopened.Events.CanUndo) { reopened.Events.Save(reopened.Events.PreviewUndo(), Path.Combine(output, $"restaurar-{reopened.Events.Revision}.ndsevents.json")); reopened.EventChanged(); }
                reopened.Export(Path.Combine(output, "Platino-restaurado.nds"));
            }
            using (var rebuilt = NdsProject.Open(built, false))
            {
                var r = rebuilt.Events.Data.Event(411, PlatinumEventKind.Warp, 0);
                if (r.Destination != w1.Destination || r.Anchor != w1.Anchor || rebuilt.Events.Data.NormalizedHash() == PlatinumEvents.NormalizedEventsSha256) throw new Exception("Exported ROM did not persist or was accepted for the wrong reason");
            }
            using var baseline = NdsRom.Open(copy); using var builtRom = NdsRom.Open(built);
            foreach (var file in baseline.Files) if (file.Path != PlatinumEvents.EventPath && !baseline.ReadFile(file).SequenceEqual(builtRom.ReadFile(file.Path))) throw new Exception("Unrelated file changed: " + file.Path);
            Console.WriteLine($"PASS EventEditReal_SaveReopenExportReopenAndUndo: {saves.Count} edits on {map.Name}; exported ROM reopens in the event editor; other NDS files byte-exact");
        }
        File.Delete(copy);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "Platino-restaurado.nds")))) != hash)
            throw new Exception("Source changed or whole-ROM restoration differs");
        manual.Add("4. Platino-restaurado.nds debe comportarse como el original. No probado en juego.");
        File.WriteAllLines(Path.Combine(output, "MANUAL.txt"), manual);
        Console.WriteLine("PASS EventEditReal_OriginalAndRestoredRomHashesMatch; gameplay NOT tested");
    }

    // ------------------------------------------------------------------------------------------------ WPF (off-screen)
    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject => NdsMapCanvasChecks.Children<T>(root).Single(e => AutomationProperties.GetName(e) == name);
    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-event-editing-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = NdsEventChecks.Make(); string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); string hash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read()); byte[] original = session.Data.Bytes();
            var view = new NdsEventEditorView(session); var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single();
            var tabs = NdsMapCanvasChecks.Children<TabControl>(view).Single(); var tables = NdsMapCanvasChecks.Children<DataGrid>(view).ToList();
            TextBox Box(string name) => Find<TextBox>(view, "Eventos DS · " + name); Button Btn(string name) => Find<Button>(view, "Eventos DS · " + name);
            var panel = Find<Border>(view, "Eventos DS · objeto, warp o activador"); var state = Find<TextBlock>(view, "Eventos DS · evento · guardado y propuesto"); var notes = Find<TextBlock>(view, "Eventos DS · evento · notas");
            var differences = Box("diferencias previstas");
            void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            void Select(int tab, PlatinumEventKind kind) { tabs.SelectedIndex = tab; tables[tab].SelectedItem = tables[tab].Items.Cast<PlatinumEvent>().First(e => e.Kind == kind); NdsMapCanvasChecks.Arrange(view, 1100, 760); }
            NdsMapCanvasChecks.Arrange(view, 1100, 760);
            check(panel.Visibility == Visibility.Collapsed, "EventUi_NoPanelForAnNpc");

            // Object: interaction direction with names
            Select(1, PlatinumEventKind.Object);
            var facing = Find<ComboBox>(view, "Eventos DS · objeto · dirección de interacción");
            check(panel.Visibility == Visibility.Visible && state.Text.Contains("mirando arriba") && notes.Text.Contains("no se editan") && notes.Text.Contains("Objeto oculto") && facing.Items.Count == 7, "EventUi_ObjectShowsItsDirectionByName");
            facing.SelectedIndex = 4;
            check(differences.Text.Contains("Interacción (Mirando arriba → Desde cualquier lado)") && Btn("Guardar cambios…").IsEnabled && state.Text.Contains("Propuesta") && session.Revision == 0, "EventUi_ChangingTheDirectionPreparesAProposal");
            Click(Btn("evento · restaurar valores guardados"));
            check(facing.SelectedIndex == 0 && differences.Text.Length == 0 && !Btn("Guardar cambios…").IsEnabled, "EventUi_RestoreCancelsWithoutSaving");

            // Warp: destination map and arrival warp from lists of existing warps; the map shows where it leads
            Select(2, PlatinumEventKind.Warp);
            var map = Find<ComboBox>(view, "Eventos DS · warp · mapa de destino"); var anchor = Find<ComboBox>(view, "Eventos DS · warp · warp de llegada");
            check(map.SelectedItem is PlatinumMap { Id: 1 } && anchor.Items.Count == 1 && notes.Text.Contains("vuelve a este warp") && canvas.SelectionNote!.Contains("Zona B"), "EventUi_WarpShowsItsDestinationAndTheWayBack");
            map.SelectedItem = session.Data.Map(2);
            check(anchor.Items.Count == 1 && differences.Text.Contains("Mapa de destino (Zona B → Zona A)") && canvas.SelectionNote!.StartsWith("propuesta:") && notes.Text.Contains("la vuelta no regresa aquí"), "EventUi_RetargetingListsOnlyExistingWarps");
            string checkpoint = Path.Combine(temp, "warp.ndsevents.json"); NdsMapCanvasChecks.Pump(view.SaveToAsync(checkpoint));
            check(NdsEventSession.ReadDocument(checkpoint).Version == 5 && session.Data.Event(0, PlatinumEventKind.Warp, 0).Destination == 2 && canvas.Model!.SelectedMarker?.Kind == PlatinumEventKind.Warp, "EventUi_SaveKeepsTheWarpSelected");

            // Trigger: size with + and −
            Select(3, PlatinumEventKind.Trigger);
            Click(Btn("activador · ancho una casilla más")); Click(Btn("activador · ancho una casilla más"));
            check(Box("activador · ancho").Text == "3" && differences.Text.Contains("Ancho (casillas): 1 → 3") && notes.Text.Contains("variable"), "EventUi_TriggerSizeChangesWithTheButtons");
            Click(Btn("activador · alto una casilla menos")); Click(Btn("activador · alto una casilla menos")); Click(Btn("activador · alto una casilla menos"));
            check(Box("activador · alto").Text == "1", "EventUi_TriggerSizeNeverGoesBelowOne");
            Click(Btn("evento · restaurar valores guardados"));

            // On the map: dragging a warp gives a proposal with a warning, accepting fills X/Y and previews; nothing is saved
            Find<System.Windows.Controls.RadioButton>(view, "Eventos DS · herramienta mover NPC").IsChecked = true;
            NdsMapCanvasChecks.CenterOn(canvas, PlatinumEventKind.Warp, 0, 16); var mk = canvas.Model!.Find(PlatinumEventKind.Warp, 0)!; var c = canvas.Model.TileToPixel(mk.CenterX, mk.CenterY);
            int revision = session.Revision;
            check(canvas.BeginDragAt(new Point(c.X, c.Y)), "EventUi_AWarpCanBeDraggedOnTheMap");
            var to = canvas.Model.TileToPixel(6.5, 7.5); canvas.DragTo(new Point(to.X, to.Y)); canvas.DropAt(new Point(to.X, to.Y));
            var proposalText = Find<TextBlock>(view, "Eventos DS · detalle de la propuesta").Text;
            check(proposalText.Contains("warp 0") && proposalText.Contains("no se comprueba el terreno") && Find<Button>(view, "Eventos DS · aceptar propuesta").Content.ToString()!.Contains("advertencia"), "EventUi_TheDropExplainsWhatIsNotChecked");
            Click(Find<Button>(view, "Eventos DS · aceptar propuesta"));
            check(Box("Coordenada X").Text == "6" && Box("Coordenada Y").Text == "7" && differences.Text.Contains("Coordenada X: 1 → 6") && session.Revision == revision && NdsMapCanvasChecks.Sha(source) == hash, "EventUi_AcceptingTheDropPreviewsWithoutSaving");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "event-warp-drag-1100");
        }
        finally { Directory.Delete(temp, true); }
    }
}
