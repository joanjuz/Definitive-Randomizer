using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
using TabControl = System.Windows.Controls.TabControl;

internal static class NdsEventChecks
{
    internal sealed record Fixture(byte[] Headers, byte[] Names, byte[] Events, byte[] Matrices, byte[] Scripts)
    {
        internal PlatinumEvents Read() => PlatinumEvents.Parse(Headers, Names, Events, Matrices, Scripts, ["Zona A", "Zona B"]);
        internal byte[] Rom(string code = "CPUE", byte revision = 1) => NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]>
        { [PlatinumEvents.EventPath] = Events, [PlatinumEvents.NamesPath] = Names, [PlatinumEvents.MatrixPath] = Matrices, [PlatinumEvents.ScriptPath] = Scripts, ["untouched.bin"] = [7, 1, 99, 18] }, code, revision);
    }
    internal static Fixture Make()
    {
        byte[] Npc(int id, int x, int y, int type, int script)
        {
            var bytes = new byte[32]; Put(bytes, 0, id); Put(bytes, 2, 33); Put(bytes, 6, type); Put(bytes, 10, script);
            Put(bytes, 16, 0xCAFE); Put(bytes, 18, 0xBEEF); Put(bytes, 24, x); Put(bytes, 26, y); return bytes;
        }
        byte[] Event(byte[][] npcs, int target)
        {
            using var output = new MemoryStream(); using var w = new BinaryWriter(output);
            byte[] background = new byte[20]; Put(background, 0, 1); Put(background, 2, 2); Put(background, 4, 3); Put(background, 8, 4);
            w.Write(1); w.Write(background); w.Write(npcs.Length); foreach (var npc in npcs) w.Write(npc);
            w.Write(1); byte[] warp = new byte[12]; Put(warp, 0, 1); Put(warp, 2, 2); Put(warp, 4, target); w.Write(warp);
            w.Write(1); byte[] trigger = new byte[16]; Put(trigger, 0, 1); Put(trigger, 2, 3); Put(trigger, 4, 4); Put(trigger, 6, 1); Put(trigger, 8, 2); w.Write(trigger); return output.ToArray();
        }
        byte[] headers = new byte[72], names = new byte[48];
        for (int i = 0; i < 3; i++)
        {
            Encoding.ASCII.GetBytes("MAP_" + (char)('A' + i)).CopyTo(names, i * 16);
            Put(headers, i * 24 + 2, i == 0 ? 0 : 1); Put(headers, i * 24 + 16, i == 1 ? 1 : 0); headers[i * 24 + 18] = (byte)(i % 2);
        }
        return new(headers, names,
            NdsGen5RandomizerChecks.BuildNarc([Event([Npc(10, 2, 3, 0, 1), Npc(11, 7, 8, 3, 7000)], 1), Event([Npc(20, 4, 5, 0, 1)], 0)]),
            NdsGen5RandomizerChecks.BuildNarc([[1, 1, 0, 0, 0, 0, 0], [2, 1, 0, 0, 0, 0, 0, 0, 0]]),
            NdsGen5RandomizerChecks.BuildNarc([[2, 0, 0, 0, 0x13, 0xFD, 2, 0, 0xAB, 0xCD]]));
    }
    private static void Put(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), checked((ushort)value));
    private static void Expect<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); } catch (T) { check(true, name); return; }
        throw new Exception("FAIL expected " + typeof(T).Name + ": " + name);
    }
    internal static void Run(Action<bool, string> check)
    {
        var f = Make(); var data = f.Read(); var npc = data.Npc(0, 0); var values = PlatinumEvents.Values(npc);
        check(data.Maps.Count == 3 && data.Map(0) is { EventFile: 0, ScriptFile: 0, Width: 32, Height: 32, Name: "Zona A" } &&
            data.Events(0).Count == 5 && data.Events(0).Count(e => e.Kind == PlatinumEventKind.Npc) == 2 && npc is { Id: 10, X: 2, Y: 3, Graphics: 33 }, "NdsEvents_ReadMapsAndAllEventKinds");
        check(data.Events(0).Any(e => e.Kind == PlatinumEventKind.Object && e.Type == 2) && data.Npc(0, 1).Type == 3,
            "NdsEvents_DistinguishesBackgroundObjectsFromItemNpcs");
        byte[] edited = data.ReplaceNpc(0, 0, values, new(12, 13, 3)); var next = data.WithEvents(edited);
        check(next.Npc(0, 0) is { X: 12, Y: 13, Direction: 3 } && next.Npc(2, 0).X == 12 && next.Bounds(2) == (32, 32), "NdsEvents_SharedFileUsesConservativeMatrixBounds");
        var beforeMember = Narc.Read(f.Events).Files[0]; var afterMember = Narc.Read(edited).Files[0];
        int npcAt = 4 + 20 + 4;
        check(Enumerable.Range(0, f.Events.Length).Count(i => f.Events[i] != edited[i]) == 3 &&
            afterMember.AsSpan(npcAt + 16, 4).SequenceEqual(beforeMember.AsSpan(npcAt + 16, 4)) &&
            next.Npc(0, 1).Raw == data.Npc(0, 1).Raw && Narc.Read(edited).Files[1].SequenceEqual(Narc.Read(f.Events).Files[1]) &&
            data.Bytes().SequenceEqual(f.Events) && data.NormalizedHash() == next.NormalizedHash(), "NdsEvents_EditChangesOnlyValidatedNpcBytes");
        check(next.WithEvents(next.ReplaceNpc(0, 0, new(12, 13, 3), values)).Bytes().SequenceEqual(f.Events), "NdsEvents_RestoreArchiveByteExactly");
        foreach (var (id, invalid) in new[] { ("NegativeX", new PlatinumNpcValues(-1, 3, 0)), ("UpperX", new PlatinumNpcValues(32, 3, 0)), ("NegativeY", new PlatinumNpcValues(2, -1, 0)), ("UpperY", new PlatinumNpcValues(2, 32, 0)), ("Direction", new PlatinumNpcValues(2, 3, 4)) })
            Expect<ArgumentOutOfRangeException>(() => data.ReplaceNpc(0, 0, values, invalid), check, "NdsEvents_Reject" + id);
        check(data.WithEvents(data.ReplaceNpc(0, 0, values, new(0, 31, 0))).Npc(0, 0) is { X: 0, Y: 31 }, "NdsEvents_AcceptsCoordinateBoundaries");
        Expect<ArgumentOutOfRangeException>(() => data.Npc(0, 99), check, "NdsEvents_RejectMissingNpc");
        Expect<ArgumentOutOfRangeException>(() => data.Events(3), check, "NdsEvents_RejectMissingMap");
        Expect<InvalidOperationException>(() => data.ReplaceNpc(0, 0, new(99, 3, 0), new(3, 3, 0)), check, "NdsEvents_RejectStaleRecord");
        var unknown = (byte[])edited.Clone();
        int opaque = Enumerable.Range(0, unknown.Length - 4).First(i => unknown.AsSpan(i, 4).SequenceEqual(new byte[] { 0xFE, 0xCA, 0xEF, 0xBE }));
        unknown[opaque] ^= 1;
        Expect<InvalidDataException>(() => data.WithEvents(unknown), check, "NdsEvents_RejectUnknownByteChanges");
        var badHeaders = (byte[])f.Headers.Clone(); Put(badHeaders, 16, 99);
        Expect<InvalidDataException>(() => PlatinumEvents.Parse(badHeaders, f.Names, f.Events, f.Matrices, f.Scripts), check, "NdsEvents_RejectBrokenMapReference");
        var badEvent = (byte[])Narc.Read(f.Events).Files[0].Clone(); BinaryPrimitives.WriteUInt32LittleEndian(badEvent, uint.MaxValue);
        Expect<InvalidDataException>(() => PlatinumEvents.Parse(f.Headers, f.Names, NdsGen5RandomizerChecks.BuildNarc([badEvent]), f.Matrices, f.Scripts), check, "NdsEvents_RejectOversizeRecordCount");
        Expect<InvalidDataException>(() => PlatinumEvents.Parse(f.Headers, f.Names, f.Events[..^1], f.Matrices, f.Scripts), check, "NdsEvents_RejectTruncatedNarc");
        var warp = data.Events(0).Single(e => e.Kind == PlatinumEventKind.Warp);
        check(data.WarpStatus(warp).Contains("Referencia existente") && data.WarpStatus(warp with { Destination = 65535 }).Contains("no navegable") &&
            data.WarpStatus(warp with { Anchor = 99 }).Contains("no corresponde"), "NdsEvents_WarpNavigationRequiresExistingDestinationAndAnchor");
        check(data.Script(0, 1) is { Archive: 0, Entry: 1, Offset: 6, Bytes: "0200ABCD" } && data.Script(0, 0).Entry is null &&
            data.Script(0, 3000).Status.Contains("externa") && data.Script(0, 2).Offset is null, "NdsEvents_InspectLocalScriptsWithoutInventingExternalLinks");
        string temp = Path.Combine(Path.GetTempPath(), "nds-events-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try { SessionChecks(f, temp, check); }
        finally { Directory.Delete(temp, true); }
    }
    private static void SessionChecks(Fixture f, string temp, Action<bool, string> check)
    {
        string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(source));
        using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read());
        var first = session.Preview(0, 0, new(12, 13, 3)); string checkpoint = Path.Combine(temp, "edit.ndsevents.json");
        session.Save(first, checkpoint);
        check(session.Data.Npc(0, 0) is { X: 12, Y: 13, Direction: 3 } && session.Revision == 1 && session.Audit.Count == 1 && File.Exists(checkpoint), "NdsEvents_SavePublishesCheckpointBeforeApplyingState");
        var reopened = new NdsEventSession(rom, f.Read()); reopened.Load(NdsEventSession.ReadDocument(checkpoint), checkpoint);
        check(reopened.Data.Bytes().SequenceEqual(session.Data.Bytes()) && reopened.CanUndo, "NdsEvents_ReopenCheckpointPersistsChangesAndUndo");
        Expect<InvalidOperationException>(() => session.Save(first, Path.Combine(temp, "stale.ndsevents.json")), check, "NdsEvents_RejectStaleCheckpointProposal");
        Expect<InvalidOperationException>(() => reopened.Save(session.Preview(0, 0, new(14, 13, 3)), Path.Combine(temp, "foreign.ndsevents.json")), check, "NdsEvents_RejectForeignCheckpointProposal");
        int revision = session.Revision;
        Expect<IOException>(() => session.Save(session.Preview(0, 0, new(14, 13, 3)), checkpoint), check, "NdsEvents_NeverOverwriteExistingCheckpoint");
        check(session.Revision == revision && session.Data.Npc(0, 0).X == 12, "NdsEvents_FailedSaveLeavesMemoryUnchanged");
        var corrupt = NdsEventSession.ReadDocument(checkpoint) with { SourceSha256 = "00" };
        Expect<InvalidDataException>(() => reopened.Load(corrupt, checkpoint), check, "NdsEvents_RejectChangedSourceFingerprint");
        Expect<InvalidDataException>(() => reopened.Load(NdsEventSession.ReadDocument(checkpoint) with { Version = 9 }, checkpoint), check, "NdsEvents_RejectUnknownCheckpointVersion");
        string broken = Path.Combine(temp, "broken.ndsevents.json"); File.WriteAllText(broken, "{\"Format\":");
        Expect<JsonException>(() => NdsEventSession.ReadDocument(broken), check, "NdsEvents_RejectTruncatedCheckpoint");
        string failed = Path.Combine(temp, "failed.ndsevents.json");
        Expect<IOException>(() => NdsEventSession.WriteNew(failed, [1, 2, 3], _ => throw new IOException("Injected pre-publication failure")), check, "NdsEvents_WriteFailureBeforePublishIsRecoverable");
        check(!File.Exists(failed) && !Directory.GetFiles(temp, "*.partial-*").Any() && File.Exists(checkpoint), "NdsEvents_WriteFailureKeepsPreviousCheckpointAndCleansOwnStage");
        Expect<IOException>(() => { using var writer = new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }, check, "NdsEvents_SourceHandleRejectsConcurrentWriter");
        string built = Path.Combine(temp, "edited.nds"); rom.Save(built, new Dictionary<int, byte[]> { [rom.File(PlatinumEvents.EventPath).Id] = reopened.Replacement()! });
        using (var verify = NdsRom.Open(built))
            check(verify.ReadFile(PlatinumEvents.EventPath).SequenceEqual(reopened.Data.Bytes()) && verify.ReadFile("untouched.bin").SequenceEqual(rom.ReadFile("untouched.bin")), "NdsEvents_RebuildReopensWithOnlyEventArchiveChanged");
        string undo = Path.Combine(temp, "undo.ndsevents.json"); reopened.Save(reopened.PreviewUndo(), undo);
        var restored = new NdsEventSession(rom, f.Read()); restored.Load(NdsEventSession.ReadDocument(undo), undo);
        check(restored.Replacement() is null && !restored.CanUndo && restored.Data.Bytes().SequenceEqual(f.Events) && restored.Audit.Count == 2, "NdsEvents_PersistentUndoRestoresOriginalArchiveAndAudit");
        string restoredRom = Path.Combine(temp, "restored.nds"); rom.Save(restoredRom, new Dictionary<int, byte[]>());
        check(SHA256.HashData(File.ReadAllBytes(restoredRom)).SequenceEqual(sourceHash) && SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(sourceHash), "NdsEvents_RestoreWholeRomByteExactlyAndKeepSource");
        string revisionRom = Path.Combine(temp, "revision.nds"); File.WriteAllBytes(revisionRom, f.Rom(revision: 2));
        using (var wrong = NdsRom.Open(revisionRom)) Expect<InvalidDataException>(() => PlatinumEvents.Open(wrong), check, "NdsEvents_RejectUnknownGameRevision");
        string otherRom = Path.Combine(temp, "other.nds"); File.WriteAllBytes(otherRom, f.Rom("ADAE", 5));
        using (var wrong = NdsRom.Open(otherRom)) Expect<InvalidDataException>(() => PlatinumEvents.Open(wrong), check, "NdsEvents_RejectOtherGameProfile");
        Expect<InvalidDataException>(() => PlatinumEvents.Open(rom), check, "NdsEvents_RejectUncreditedProfileTables");
        var invalidEdit = NdsEventSession.ReadDocument(checkpoint) with { Edits = [new NdsNpcEdit(0, 999, new(2, 3, 0), new(4, 5, 0))] };
        Expect<ArgumentOutOfRangeException>(() => session.Load(invalidEdit, checkpoint), check, "NdsEvents_RejectInvalidCheckpointIndex");
        check(session.Data.Npc(0, 0).X == 12 && session.Revision == revision, "NdsEvents_InvalidCheckpointLeavesCurrentStateUnchanged");
        var concurrent = new NdsEventSession(rom, f.Read());
        var a = concurrent.Preview(0, 0, new(3, 3, 0)); var b = concurrent.Preview(0, 0, new(4, 3, 0));
        bool Attempt(NdsEventProposal proposal, string path) { try { concurrent.Save(proposal, path); return true; } catch (InvalidOperationException) { return false; } }
        string raceA = Path.Combine(temp, "race-a.ndsevents.json"), raceB = Path.Combine(temp, "race-b.ndsevents.json");
        var tasks = new[] { Task.Run(() => Attempt(a, raceA)), Task.Run(() => Attempt(b, raceB)) }; Task.WaitAll(tasks);
        check(tasks.Count(t => t.Result) == 1 && new[] { raceA, raceB }.Count(File.Exists) == 1 && concurrent.Revision == 1,
            "NdsEvents_ConcurrentSavesCommitExactlyOneRevision");
        var winner = NdsEventSession.ReadDocument(File.Exists(raceA) ? raceA : raceB);
        check(winner.Edits.Single().After.X == concurrent.Data.Npc(0, 0).X && SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(sourceHash),
            "NdsEvents_ConcurrentWinnerMatchesDiskAndKeepsSource");
    }
    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-events-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = Make(); string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); using var rom = NdsRom.Open(source);
            var session = new NdsEventSession(rom, f.Read()); var view = new NdsEventEditorView(session);
            var maps = Children<ListBox>(view).Single(); var tabs = Children<TabControl>(view).Single();
            check(maps.Items.Count == 3 && tabs.Items.Cast<TabItem>().Select(t => (string)t.Header).SequenceEqual(new[] { "NPC", "Objetos", "Warps", "Scripts" }), "NdsEventsUi_MapsAndFourTabsAreRealData");
            TextBox Box(string name) => Children<TextBox>(view).Single(e => AutomationProperties.GetName(e) == "Eventos DS · " + name);
            Button Action(string label) => Children<Button>(view).Single(b => AutomationProperties.GetName(b) == "Eventos DS · " + label);
            Box("Coordenada X").Text = "12"; view.PreviewSelection();
            check(Box("diferencias previstas").Text.Contains("Coordenada X: 2 → 12") && Action("Guardar cambios…").IsEnabled, "NdsEventsUi_PreviewShowsActualBeforeAfter");
            NdsUiChecksPump(view.SaveToAsync(Path.Combine(temp, "saved.ndsevents.json")));
            check(session.Data.Npc(0, 0).X == 12 && File.Exists(Path.Combine(temp, "saved.ndsevents.json")), "NdsEventsUi_SaveCreatesPersistentCheckpoint");
            tabs.SelectedIndex = 2;
            check(Box("Coordenada X").IsReadOnly && !Action("Previsualizar diferencias").IsEnabled && Action("Ir al destino existente").IsEnabled, "NdsEventsUi_WarpsReadOnlyWithValidatedNavigation");
            Action("Ir al destino existente").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check((maps.SelectedItem as PlatinumMap)?.Id == 1, "NdsEventsUi_WarpButtonNavigatesToDestinationMap");
            Box("buscar mapa por nombre o ID").Text = "MAP_C";
            check(maps.Items.Count == 1 && (maps.SelectedItem as PlatinumMap)?.Id == 2, "NdsEventsUi_SearchFiltersByTechnicalName");
            tabs.SelectedIndex = 0; Box("Coordenada X").Text = "-1"; view.PreviewSelection();
            check(!Action("Guardar cambios…").IsEnabled && Children<TextBlock>(view).Any(t => t.Text.Contains("fuera de la matriz")), "NdsEventsUi_InvalidCoordinatesCannotBeSaved");
            check(Children<Control>(view).Where(c => c is TextBox or ListBox or TabControl).All(c => AutomationProperties.GetName(c).Length > 0), "NdsEventsUi_InputsAndCopyableInspectorHaveAccessibleNames");
            foreach (int width in new[] { 1100, 760 })
            {
                view.Margin = new Thickness(0); view.Width = width; view.Height = 760; view.Measure(new Size(width, 760)); view.Arrange(new Rect(0, 0, width, 760)); view.UpdateLayout();
                check(view.DesiredSize.Width <= width && Action("Guardar cambios…").ActualWidth > 0, "NdsEventsUi_AdaptiveLayout" + width);
                var status = Children<TextBlock>(view).Single(t => AutomationProperties.GetName(t) == "Eventos DS · estado");
                check(status.ActualHeight > 0 && status.TranslatePoint(new Point(0, status.ActualHeight), view).Y <= 761,
                    "NdsEventsUi_StatusStaysVisible" + width);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 760, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(view);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                string captures = Path.Combine(AppContext.BaseDirectory, "nds-events-ui"); Directory.CreateDirectory(captures);
                using var stream = File.Create(Path.Combine(captures, "events-" + width + ".png")); png.Save(stream);
            }
        }
        finally { Directory.Delete(temp, true); }
    }
    private static void NdsUiChecksPump(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted) { if (timer.Elapsed.TotalSeconds > 90) throw new TimeoutException(); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); }
        task.GetAwaiter().GetResult();
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (object child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject item) foreach (var descendant in Children<T>(item)) yield return descendant;
    }
    internal static void Probe(string source, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "Platino-copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var map = events.Data.Maps.First(m => m.InternalName == "T01");
            var npc = events.Data.Events(map.Id).First(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0);
            var before = PlatinumEvents.Values(npc); var after = before with { Direction = (before.Direction + 1) % 4 };
            string checkpoint = Path.Combine(output, "edited.ndsevents.json"); events.Save(events.Preview(map.Id, npc.Index, after), checkpoint); project.EventChanged();
            string built = Path.Combine(output, "Platino-eventos.nds"); project.Export(built);
            using (var reopened = NdsProject.Open(checkpoint, false))
            {
                if (PlatinumEvents.Values(reopened.Events.Data.Npc(map.Id, npc.Index)) != after) throw new Exception("Checkpoint did not persist");
                Expect<InvalidOperationException>(() => reopened.CaptureGlobal(), (_, _) => { }, "No event Global integration");
                reopened.Events.Save(reopened.Events.PreviewUndo(), Path.Combine(output, "restored.ndsevents.json")); reopened.EventChanged();
                reopened.Export(Path.Combine(output, "Platino-restaurado.nds"));
            }
            using var rebuilt = NdsProject.Open(built, false);
            if (PlatinumEvents.Values(rebuilt.Events.Data.Npc(map.Id, npc.Index)) != after) throw new Exception("Rebuilt ROM did not persist");
            using var baselineRom = NdsRom.Open(copy); using var builtRom = NdsRom.Open(built);
            foreach (var file in baselineRom.Files) if (file.Path != PlatinumEvents.EventPath && !baselineRom.ReadFile(file).SequenceEqual(builtRom.ReadFile(file.Path))) throw new Exception("Unrelated file changed: " + file.Path);
            File.WriteAllText(Path.Combine(output, "MANUAL.txt"), $"CPUE/1, mapa {map.Id} {map.Name} ({map.InternalName}), NPC índice {npc.Index}, ID {npc.Id}, X/Y {npc.X}/{npc.Y}. Dirección {before.Direction} -> {after.Direction}. Probar Platino-eventos.nds en una partida de prueba y comparar orientación antes de interactuar. No probado en juego.\nCheckpoint: edited.ndsevents.json. Restauración: restored.ndsevents.json y Platino-restaurado.nds.");
            Console.WriteLine($"PASS NdsEventsReal_ProfileMapsAndRecords: {events.Data.Maps.Count} maps; map {map.Id}; NPC {npc.Id}");
            Console.WriteLine("PASS NdsEventsReal_SaveReopenRebuildAndUndo; all unrelated NDS files byte-exact");
        }
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "Platino-restaurado.nds")))) != hash)
            throw new Exception("Source changed or whole-ROM restoration differs");
        Console.WriteLine("PASS NdsEventsReal_OriginalAndRestoredRomHashesMatch; gameplay NOT tested");
    }
}
