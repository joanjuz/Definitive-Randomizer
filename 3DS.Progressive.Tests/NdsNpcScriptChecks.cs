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

/// <summary>NPC behaviour copy: minimal script reader/writer, nurse pattern, shared message scripts, checkpoint v3, export and a real-ROM probe.</summary>
internal static class NdsNpcScriptChecks
{
    private static void Reject<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); } catch (T) { check(true, name); return; }
        throw new Exception("FAIL expected " + typeof(T).Name + ": " + name);
    }
    private static void Put(byte[] b, int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), checked((ushort)v));
    internal static byte[] Message(int id) => [0x49, 0, 0xDC, 5, 0x60, 0, 0x68, 0, 0x2C, 0, (byte)id, 0x31, 0, 0x34, 0, 0x61, 0, 2, 0];
    /// <summary>A script member: u32 relative offsets, 0xFD13, then the code of each entry one after another.</summary>
    internal static byte[] ScriptMember(params byte[][] entries)
    {
        int header = entries.Length * 4 + 2; var code = new List<byte>(); var table = new byte[header];
        for (int i = 0; i < entries.Length; i++) { BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(i * 4), (uint)(header + code.Count - (i * 4 + 4))); code.AddRange(entries[i]); }
        Put(table, header - 2, 0xFD13); return [.. table, .. code];
    }
    private static byte[] Npc(int id, int x, int y, int script, int type = 0)
    {
        var b = new byte[32]; Put(b, 0, id); Put(b, 2, 33); Put(b, 6, type); Put(b, 10, script); Put(b, 24, x); Put(b, 26, y); return b;
    }
    private static byte[] EventFile(params byte[][] npcs)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(0); w.Write(npcs.Length); foreach (var n in npcs) w.Write(n); w.Write(0); w.Write(0); return s.ToArray();
    }
    /// <summary>Maps 0 and 1 share script file 0 (nurse, message, unknown); map 2 uses script file 1 (one message).</summary>
    internal static NdsMapCanvasChecks.Fixture Make()
    {
        var f = NdsMapCanvasChecks.Make(); var headers = (byte[])f.Headers.Clone(); Put(headers, 2 * 24 + 4, 1);
        var events = NdsGen5RandomizerChecks.BuildNarc([EventFile(Npc(10, 2, 3, 2)), EventFile(Npc(20, 119, 878, 1), Npc(21, 110, 875, 2), Npc(22, 112, 875, 3), Npc(23, 114, 875, 3000, 1)), EventFile(Npc(30, 3, 4, 1))]);
        var scripts = NdsGen5RandomizerChecks.BuildNarc([ScriptMember(PlatinumScriptFile.NurseScript(20), Message(3), [2, 0]), ScriptMember(Message(0)), ScriptMember([2, 0])]);
        return f with { Headers = headers, Events = events, Scripts = scripts };
    }
    private static PlatinumTerrain Terrain() => PlatinumTerrain.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([PlatinumTerrainChecks.Land((x, z) => x >= 28 ? (ushort)0x8000 : (ushort)0)]));

    internal static void Run(Action<bool, string> check)
    {
        // ---- script file reader / writer
        var nurse3 = PlatinumScriptFile.NurseScript(3);
        check(Convert.ToHexString(nurse3) == "2800078003001400D2070200", "NpcScript_NurseScriptMatchesTheGame");
        var member = ScriptMember(PlatinumScriptFile.NurseScript(20), Message(3), [2, 0]);
        var offsets = PlatinumScriptFile.EntryOffsets(member);
        check(offsets.Count == 3 && offsets[0] == 14 && offsets[1] == 26 && offsets[2] == 45, "NpcScript_EntryTableIsRead");
        check(PlatinumScriptFile.Classify(member, 1) is { Pattern: PlatinumScriptPattern.PokecenterNurse, LocalIdReference: 20, CommonScript: 2002 } && PlatinumScriptFile.Classify(member, 2) is { Pattern: PlatinumScriptPattern.PlainMessage, MessageId: 3 } && PlatinumScriptFile.Classify(member, 3).Pattern == PlatinumScriptPattern.Unknown, "NpcScript_PatternsAreRecognisedExactly");
        var almostNurse = ScriptMember([0x28, 0, 7, 0x80, 20, 0, 0x14, 0, 0xD3, 7, 2, 0]); var almostMessage = (byte[])Message(1).Clone(); almostMessage[17] = 3;
        check(PlatinumScriptFile.Classify(almostNurse, 1).Pattern == PlatinumScriptPattern.Unknown && PlatinumScriptFile.Classify(ScriptMember(almostMessage), 1).Pattern == PlatinumScriptPattern.Unknown, "NpcScript_NearMissesAreUnknown");
        Reject<ArgumentOutOfRangeException>(() => PlatinumScriptFile.Classify(member, 4), check, "NpcScript_MissingEntryRejected");
        Reject<InvalidDataException>(() => PlatinumScriptFile.EntryOffsets([1, 0, 0, 0]), check, "NpcScript_TableWithoutTerminatorRejected");
        var bad = (byte[])member.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(bad, 5000);
        Reject<InvalidDataException>(() => PlatinumScriptFile.EntryOffsets(bad), check, "NpcScript_OffsetOutsideTheMemberRejected");
        var grown = PlatinumScriptFile.AppendEntry(member, PlatinumScriptFile.NurseScript(40), out int number);
        var newOffsets = PlatinumScriptFile.EntryOffsets(grown);
        check(number == 4 && newOffsets.Count == 4 && Enumerable.Range(0, 3).All(i => newOffsets[i] == offsets[i] + 4) && grown.AsSpan(18, member.Length - 14).SequenceEqual(member.AsSpan(14)), "NpcScript_AppendKeepsEveryExistingEntryAndShiftsTheCodeAsAWhole");
        check(newOffsets[3] % 4 == 0 && PlatinumScriptFile.Classify(grown, 4) is { Pattern: PlatinumScriptPattern.PokecenterNurse, LocalIdReference: 40 } && Enumerable.Range(1, 2).All(i => PlatinumScriptFile.Classify(grown, i).Bytes == PlatinumScriptFile.Classify(member, i).Bytes) && PlatinumScriptFile.Classify(grown, 3).Pattern == PlatinumScriptPattern.Unknown, "NpcScript_NewEntryIsTheLastAndOthersDecodeTheSame");
        Reject<ArgumentException>(() => PlatinumScriptFile.AppendEntry(member, [1, 2, 3], out _), check, "NpcScript_OddCodeIsRejected");

        // ---- events instance carrying scripts
        var f = Make(); var data = f.Read(); byte[] eventsBefore = data.Bytes(), scriptsBefore = data.ScriptArchiveBytes();
        check(data.ScriptEntryCount(1) == 3 && data.ScriptEntryCount(2) == 1 && data.ClassifyScript(1, 1)!.Pattern == PlatinumScriptPattern.PokecenterNurse && data.ClassifyScript(2, 1)!.Pattern == PlatinumScriptPattern.PlainMessage && data.ClassifyScript(1, 9) is null, "NpcScript_ScriptsAreClassifiedPerMap");
        var appended = data.WithAppendedScript(2, PlatinumScriptFile.NurseScript(31), out int n2);
        var before = Narc.Read(scriptsBefore).Files; var after = Narc.Read(appended.ScriptArchiveBytes()).Files;
        check(n2 == 2 && appended.ScriptEntryCount(2) == 2 && appended.Bytes().SequenceEqual(eventsBefore) && before[0].AsSpan().SequenceEqual(after[0]) && before[2].AsSpan().SequenceEqual(after[2]) && data.ScriptArchiveBytes().SequenceEqual(scriptsBefore), "NpcScript_AppendTouchesOnlyTheMapScriptFile");
        check(appended.ScriptEntryCount(0) == 3 && appended.ScriptEntryCount(1) == 3, "NpcScript_MapsSharingAnotherFileAreUnaffected");

        // ---- behaviour options
        NdsNpcClip Clip(int map, int index) => new(data.Npc(map, index), false, map, data.Map(map).EventFile);
        var nurseOptions = NdsNpcStructurePlanner.BehaviourOptions(data, Clip(1, 0), 2);
        check(nurseOptions.Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Available && !nurseOptions.Single(o => o.Mode == NdsBehaviourMode.ShareScript).Available && NdsNpcStructurePlanner.DefaultMode(nurseOptions) == NdsBehaviourMode.WithBehaviour, "NpcScript_NurseCanBeCopiedWithBehaviourAcrossMaps");
        var messageSame = NdsNpcStructurePlanner.BehaviourOptions(data, Clip(1, 1), 1); var messageOther = NdsNpcStructurePlanner.BehaviourOptions(data, Clip(1, 1), 2); var messageMap0 = NdsNpcStructurePlanner.BehaviourOptions(data, Clip(1, 1), 0);
        check(messageSame.Single(o => o.Mode == NdsBehaviourMode.ShareScript).Available && messageMap0.Single(o => o.Mode == NdsBehaviourMode.ShareScript).Available && NdsNpcStructurePlanner.DefaultMode(messageSame) == NdsBehaviourMode.ShareScript, "NpcScript_MessageScriptIsSharedInsideTheSameScriptFile");
        check(!messageOther.Single(o => o.Mode == NdsBehaviourMode.ShareScript).Available && messageOther.Single(o => o.Mode == NdsBehaviourMode.ShareScript).Reason.Contains("banco de textos de otro mapa") && !messageOther.Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Available, "NpcScript_MessageAcrossScriptFilesIsNotCopied");
        check(NdsNpcStructurePlanner.BehaviourOptions(data, Clip(1, 2), 2) is var unknown && !unknown.Any(o => o.Mode != NdsBehaviourMode.Appearance && o.Available) && unknown.Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Reason.Contains("no es un patrón reconocido"), "NpcScript_UnknownScriptsAreRefusedWithAReason");
        check(NdsNpcStructurePlanner.BehaviourOptions(data, Clip(1, 3), 2).Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Reason.Contains("entrenador") && NdsNpcStructurePlanner.BehaviourOptions(data, Clip(0, 0), 0).Single(o => o.Mode == NdsBehaviourMode.ShareScript).Available, "NpcScript_TrainerScriptsAreForALaterPhase");
        var nurseOfOther = new NdsNpcClip(data.Npc(1, 0) with { Id = 99 }, false, 1, 1);
        check(!NdsNpcStructurePlanner.BehaviourOptions(data, nurseOfOther, 2).Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Available && NdsNpcStructurePlanner.BehaviourOptions(data, nurseOfOther, 2).Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Reason.Contains("otro NPC"), "NpcScript_NurseScriptOfAnotherNpcIsNotPortable");

        // ---- «Añadir enfermera»: accredited template from the ROM itself
        var nurseTemplate = NdsNpcStructurePlanner.NurseTemplate(data, out int nurseCount);
        check(nurseTemplate is { SourceMap: 1, Cut: false } && nurseTemplate.Source is { Index: 0, Id: 20 } && nurseCount == 1, "Nurse_TemplateIsARealNurseOfTheRom");
        check(NdsNpcStructurePlanner.BehaviourOptions(data, nurseTemplate!, 2).Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Available && NdsNpcStructurePlanner.BehaviourOptions(data, nurseTemplate!, 1).Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Available, "Nurse_TemplateGivesBehaviourInAnyMapWithAScriptTable");
        var noNurse = (Make() with { Scripts = NdsGen5RandomizerChecks.BuildNarc([ScriptMember(Message(1), Message(3), [2, 0]), ScriptMember(Message(0)), ScriptMember([2, 0])]) }).Read();
        var foreignNurse = (Make() with { Scripts = NdsGen5RandomizerChecks.BuildNarc([ScriptMember(PlatinumScriptFile.NurseScript(99), Message(3), [2, 0]), ScriptMember(Message(0)), ScriptMember([2, 0])]) }).Read();
        check(NdsNpcStructurePlanner.NurseTemplate(noNurse, out int none) is null && none == 0 && NdsNpcStructurePlanner.NurseTemplate(foreignNurse, out _) is null, "Nurse_NoTemplateIsInventedWhenTheRomHasNoExactNurse");
        var levelScripts = (Make() with { Scripts = NdsGen5RandomizerChecks.BuildNarc([ScriptMember(PlatinumScriptFile.NurseScript(20), Message(3), [2, 0]), [1, 0, 0, 0], ScriptMember([2, 0])]) }).Read();
        var blocked = NdsNpcStructurePlanner.BehaviourOptions(levelScripts, NdsNpcStructurePlanner.NurseTemplate(levelScripts, out _)!, 2).Single(o => o.Mode == NdsBehaviourMode.WithBehaviour);
        check(!blocked.Available && blocked.Reason.Contains("tabla de entradas reconocible"), "Nurse_MapWithoutAScriptTableIsRefusedWithAReason");
        var withAdded = data.WithAppendedScript(2, PlatinumScriptFile.NurseScript(31), out _);
        check(NdsNpcStructurePlanner.NurseTemplate(withAdded, out _)!.Source.Id == 20, "Nurse_TemplateChoiceIsDeterministic");

        string temp = Path.Combine(Path.GetTempPath(), "nds-npc-script-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read());
            var (script, binding) = NdsNpcStructurePlanner.BindingFor(session.Data, Clip(1, 0), 2, NdsBehaviourMode.WithBehaviour, 31);
            var record = PlatinumEvents.NewNpcRecord(data.Npc(1, 0), 31, 33, 0, 0, 10, 10); Put(record, 10, script);
            check(script == 2 && binding is { Mode: NdsScriptBinding.Append, ScriptFile: 1, Number: 2 } && binding.CodeHex == Convert.ToHexString(PlatinumScriptFile.NurseScript(31)), "NpcScript_BindingRetargetsTheNurseToTheNewLocalId");
            var proposal = session.PreviewAdd(2, record, binding);
            check(proposal.Lines!.Any(l => l.Contains("AÑADIDO")) && session.Revision == 0 && session.ScriptReplacement() is null && session.Replacement() is null, "NpcScript_PreviewWritesNothing");
            string cp = Path.Combine(temp, "nurse.ndsevents.json"); session.Save(proposal, cp); var doc = NdsEventSession.ReadDocument(cp);
            check(doc.Version == 3 && doc.Operations!.Single().Add!.Script is { Mode: "AppendScript", Number: 2 } && File.ReadAllText(cp).Contains("AppendScript"), "NpcScript_CheckpointV3RecordsTheScript");
            check(session.Data.NpcCount(2) == 2 && session.Data.Npc(2, 1) is { Id: 31, Script: 2 } && session.Data.ClassifyScript(2, 2) is { Pattern: PlatinumScriptPattern.PokecenterNurse, LocalIdReference: 31 } && session.ScriptReplacement() is not null, "NpcScript_NewNurseUsesItsOwnScript");
            var entry = rom.File(PlatinumEvents.EventPath); var scriptsEntry = rom.File(PlatinumEvents.ScriptPath); string built = Path.Combine(temp, "built.nds");
            rom.Save(built, new Dictionary<int, byte[]> { [entry.Id] = session.Replacement()!, [scriptsEntry.Id] = session.ScriptReplacement()! }); session.VerifyExport(built);
            using (var rebuilt = NdsRom.Open(built))
            {
                bool others = rom.Files.Where(x => x.Path != PlatinumEvents.EventPath && x.Path != PlatinumEvents.ScriptPath).All(x => rom.ReadFile(x).SequenceEqual(rebuilt.ReadFile(x.Path)));
                var rs = Narc.Read(rebuilt.ReadFile(PlatinumEvents.ScriptPath)).Files;
                check(others && rs[0].AsSpan().SequenceEqual(before[0]) && rs[2].AsSpan().SequenceEqual(before[2]) && PlatinumScriptFile.Classify(rs[1], 2).LocalIdReference == 31, "NpcScript_ExportChangesOnlyEventsAndTheTargetScriptFile");
            }
            using (var again = NdsRom.Open(source)) { var re = new NdsEventSession(again, f.Read()); re.Load(doc, cp); check(re.Data.Bytes().SequenceEqual(session.Data.Bytes()) && re.Data.ScriptArchiveBytes().SequenceEqual(session.Data.ScriptArchiveBytes()), "NpcScript_V3CheckpointReopensExactly"); }
            // merge with another module's script archive
            var other = Narc.Read(scriptsBefore); other.Files[0] = PlatinumScriptFile.AppendEntry(other.Files[0], [2, 0], out _); var merged = Narc.Read(session.MergeScriptArchive(other.Write())).Files;
            check(merged[0].AsSpan().SequenceEqual(other.Files[0]) && merged[1].AsSpan().SequenceEqual(Narc.Read(session.Data.ScriptArchiveBytes()).Files[1]), "NpcScript_ScriptsOfAnotherModuleAreMergedWhenFilesDiffer");
            var clash = Narc.Read(scriptsBefore); clash.Files[1] = PlatinumScriptFile.AppendEntry(clash.Files[1], [2, 0], out _);
            Reject<InvalidOperationException>(() => session.MergeScriptArchive(clash.Write()), check, "NpcScript_TwoEditsOfTheSameScriptFileAreRefused");
            // undo restores both archives
            session.Save(session.PreviewUndo(), Path.Combine(temp, "undo.ndsevents.json"));
            check(session.Data.Bytes().SequenceEqual(eventsBefore) && session.Data.ScriptArchiveBytes().SequenceEqual(scriptsBefore) && session.ScriptReplacement() is null && NdsEventSession.ReadDocument(Path.Combine(temp, "undo.ndsevents.json")).Version == 1, "NpcScript_UndoRestoresEventsAndScriptsExactly");

            // share a message script in the same file: nothing is written to the scripts
            var (shared, shareBinding) = NdsNpcStructurePlanner.BindingFor(session.Data, Clip(1, 1), 1, NdsBehaviourMode.ShareScript, 24);
            var shareRecord = PlatinumEvents.NewNpcRecord(data.Npc(1, 1), 24, 33, 0, 0, 111, 876); Put(shareRecord, 10, shared);
            session.Save(session.PreviewAdd(1, shareRecord, shareBinding), Path.Combine(temp, "share.ndsevents.json"));
            check(shared == 2 && session.Data.Npc(1, 4) is { Id: 24, Script: 2 } && session.ScriptReplacement() is null && NdsEventSession.ReadDocument(Path.Combine(temp, "share.ndsevents.json")).Version == 3, "NpcScript_SharingAMessageScriptWritesNoScript");
            session.Save(session.PreviewUndo(), Path.Combine(temp, "undo2.ndsevents.json"));

            // invalid v3 operations never load
            using var guardRom = NdsRom.Open(source); var guard = new NdsEventSession(guardRom, f.Read()); var op = doc.Operations!.Single(); var add = op.Add!;
            void Bad(NdsEventDocument d, string name) { try { guard.Load(d, "x.ndsevents.json"); throw new Exception("FAIL accepted: " + name); } catch (InvalidDataException) { check(guard.Revision == 0 && guard.Data.ScriptArchiveBytes().SequenceEqual(scriptsBefore), name); } }
            Bad(doc with { Operations = [op with { Add = add with { Script = add.Script! with { CodeHex = Convert.ToHexString(Message(1)) } } }] }, "NpcScript_OnlyTheNurseScriptCanBeAppended");
            Bad(doc with { Operations = [op with { Add = add with { Script = add.Script! with { CodeHex = Convert.ToHexString(PlatinumScriptFile.NurseScript(99)) } } }] }, "NpcScript_AppendedNurseMustTargetTheNewNpc");
            Bad(doc with { Operations = [op with { Add = add with { Script = add.Script! with { Number = 5 } } }] }, "NpcScript_AppendedNumberMustMatch");
            Bad(doc with { Operations = [op with { Add = add with { Script = add.Script! with { ScriptFile = 0 } } }] }, "NpcScript_ScriptFileMustBeTheMapOne");
            Bad(doc with { Operations = [op with { Add = add with { Script = new NdsScriptBinding(NdsScriptBinding.Share, 1, 2, null) } }] }, "NpcScript_SharingANonExistingScriptIsRefused");
            Bad(doc with { Operations = [op with { Add = add with { Script = add.Script! with { Mode = "CopyAnything" } } }] }, "NpcScript_UnknownScriptModeIsRefused");
            Bad(doc with { Version = 2 }, "NpcScript_V2CannotCarryScripts");
            var nurseShare = new NdsNpcAddition(1, 4, Convert.ToHexString(Npc(24, 111, 876, 1)), new NdsScriptBinding(NdsScriptBinding.Share, 0, 1, null));
            Bad(doc with { Operations = [NdsEventOperation.OfAdd(nurseShare)] }, "NpcScript_TheNurseScriptIsNeverShared");
            check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) == sourceHash, "NpcScript_SourceNeverChanges");
        }
        finally { Directory.Delete(temp, true); }
    }

    // ------------------------------------------------------------------------------------------ WPF integration
    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject => NdsMapCanvasChecks.Children<T>(root).Single(e => AutomationProperties.GetName(e) == name);
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-npc-script-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = Make(); string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom());
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read()); byte[] eventsBefore = session.Data.Bytes(), scriptsBefore = session.Data.ScriptArchiveBytes();
            var view = new NdsEventEditorView(session, Terrain(), null); var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single();
            var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single(); var tabs = NdsMapCanvasChecks.Children<TabControl>(view).Single(); var npcTable = NdsMapCanvasChecks.Children<DataGrid>(view).First();
            Button Btn(string name) => Find<Button>(view, "Eventos DS · " + name); var details = Find<TextBox>(view, "Eventos DS · propiedades y referencias (copiable)");
            var form = Find<Border>(view, "Eventos DS · NPC nuevo"); var info = Find<TextBlock>(view, "Eventos DS · validación del NPC nuevo"); var mode = Find<ComboBox>(view, "Eventos DS · nuevo NPC · comportamiento"); var diff = Find<TextBox>(view, "Eventos DS · diferencias previstas");
            void Select(int index) { tabs.SelectedIndex = 0; npcTable.SelectedItem = npcTable.Items.Cast<PlatinumEvent>().First(e => e.Index == index); }
            Point Tile(int x, int y) { var c = canvas.Model!.TileToPixel(x + 0.5, y + 0.5); return new Point(c.X, c.Y); }
            int saves = 0; void Save() { NdsMapCanvasChecks.Pump(view.SaveToAsync(Path.Combine(temp, $"s{++saves}.ndsevents.json"))); }
            NdsMapCanvasChecks.Arrange(view, 1100, 760); maps.SelectedItem = maps.Items[1]; NdsMapCanvasChecks.Arrange(view, 1100, 760);

            Select(0);
            check(details.Text.Contains("enfermera del Centro Pokémon") && details.Text.Contains("Copiar con comportamiento: disponible"), "NpcScriptUi_InspectorShowsTheBehaviourAndWhetherItCanBeCopied");
            Select(2); check(details.Text.Contains("no reconocido") && details.Text.Contains("Copiar con comportamiento: no"), "NpcScriptUi_UnknownScriptIsExplainedInTheInspector");
            // Copy the nurse to another map: behaviour preselected, dependencies listed
            Select(0); Click(Btn("copiar NPC")); maps.SelectedItem = maps.Items[2]; NdsMapCanvasChecks.Arrange(view, 1100, 760); Click(Btn("pegar NPC"));
            check(canvas.Placing && canvas.PlacementClick(Tile(10, 10)) && form.Visibility == Visibility.Visible, "NpcScriptUi_PastingTheNurseOpensTheForm");
            var selected = (ComboBoxItem)mode.SelectedItem;
            check(((NdsBehaviourOption)selected.Tag).Mode == NdsBehaviourMode.WithBehaviour && info.Text.Contains("Dependencias") && info.Text.Contains("0x8007") && info.Text.Contains("script común 2002") && info.Text.Contains("comprobado por el propietario en el emulador"), "NpcScriptUi_NurseBehaviourIsPreselectedWithItsDependencies");
            check(mode.Items.Cast<ComboBoxItem>().Single(i => ((NdsBehaviourOption)i.Tag).Mode == NdsBehaviourMode.ShareScript) is { IsEnabled: false } shareItem && (string)shareItem.ToolTip! != "", "NpcScriptUi_UnavailableModesAreDisabledWithTheirReason");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-script-nurse-form-1100");
            mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().First(i => ((NdsBehaviourOption)i.Tag).Mode == NdsBehaviourMode.Appearance);
            check(info.Text.Contains("script 0") && !info.Text.Contains("Dependencias"), "NpcScriptUi_AppearanceOnlyRemainsAvailable");
            mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().First(i => ((NdsBehaviourOption)i.Tag).Mode == NdsBehaviourMode.WithBehaviour);
            Click(Btn("previsualizar propuesta de NPC nuevo"));
            check(diff.Text.Contains("AÑADIDO") && session.Revision == 0 && session.ScriptReplacement() is null, "NpcScriptUi_PreviewDescribesTheScriptAndWritesNothing");
            Save();
            check(session.Data.NpcCount(2) == 2 && session.Data.ClassifyScript(2, 2) is { Pattern: PlatinumScriptPattern.PokecenterNurse, LocalIdReference: 31 } && session.Data.Npc(2, 1).Script == 2, "NpcScriptUi_SavedNurseHasItsOwnRetargetedScript");
            // Duplicate a message NPC in the same map: shares the script by default
            maps.SelectedItem = maps.Items[1]; NdsMapCanvasChecks.Arrange(view, 1100, 760); Select(1); Click(Btn("duplicar NPC"));
            check(canvas.PlacementClick(Tile(111, 876)) && form.Visibility == Visibility.Visible && ((NdsBehaviourOption)((ComboBoxItem)mode.SelectedItem).Tag).Mode == NdsBehaviourMode.ShareScript, "NpcScriptUi_MessageDuplicateSharesTheScriptByDefault");
            Click(Btn("previsualizar propuesta de NPC nuevo")); Save();
            check(session.Data.Npc(1, 4) is { Script: 2 } && Narc.Read(session.Data.ScriptArchiveBytes()).Files[0].AsSpan().SequenceEqual(Narc.Read(scriptsBefore).Files[0]), "NpcScriptUi_SharedScriptWritesNothingToTheScripts");
            while (session.CanUndo) { Click(Btn("Restaurar último cambio…")); Save(); }
            check(session.Data.Bytes().SequenceEqual(eventsBefore) && session.Data.ScriptArchiveBytes().SequenceEqual(scriptsBefore), "NpcScriptUi_RestoringReturnsToTheOriginalEventsAndScripts");
            // «Añadir enfermera»: button → placement with the pending nurse → form with behaviour preselected → save → restore
            var nurseButton = Btn("añadir enfermera"); maps.SelectedItem = maps.Items[2]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            check(nurseButton.IsEnabled && (string)nurseButton.Content == "Añadir enfermera" && ((string)nurseButton.ToolTip!).Contains("Ctrl+E"), "NurseUi_ButtonIsAvailableWithItsShortcut");
            var clipBefore = Btn("pegar NPC").IsEnabled; Click(nurseButton);
            check(canvas.Placing && clipBefore == Btn("pegar NPC").IsEnabled, "NurseUi_ButtonStartsPlacementWithoutTouchingTheClipboard");
            check(canvas.PlacementClick(Tile(12, 12)) && form.Visibility == Visibility.Visible && ((NdsBehaviourOption)((ComboBoxItem)mode.SelectedItem).Tag).Mode == NdsBehaviourMode.WithBehaviour, "NurseUi_FormOpensWithTheNurseBehaviour");
            var templateBox = Find<ComboBox>(view, "Eventos DS · nuevo NPC · plantilla");
            check(templateBox.SelectedItem?.ToString()?.Contains("Enfermera Joy") == true, "NurseUi_TemplateIsNamedAsTheRomNurse");
            check(info.Text.Contains("Dependencias") && info.Text.Contains("0x8007") && info.Text.Contains("script común 2002"), "NurseUi_DependenciesAreListed");
            Click(Btn("previsualizar propuesta de NPC nuevo"));
            check(diff.Text.Contains("AÑADIDO") && session.Data.Bytes().SequenceEqual(eventsBefore) && canvas.PendingAddition is { X: 12, Y: 12 }, "NurseUi_PreviewShowsThePendingNurseAndWritesNothing");
            Save();
            var placedNurse = session.Data.Npc(2, session.Data.NpcCount(2) - 1);
            check(placedNurse is { X: 12, Y: 12, Graphics: 33 } && session.Data.ClassifyScript(2, placedNurse.Script) is { Pattern: PlatinumScriptPattern.PokecenterNurse } pn && pn.LocalIdReference == placedNurse.Id && NdsEventSession.ReadDocument(Path.Combine(temp, $"s{saves}.ndsevents.json")).Version == 3, "NurseUi_SavedNurseIsFunctionalAndRecordedInV3");
            Click(Btn("Restaurar último cambio…")); Save();
            check(session.Data.Bytes().SequenceEqual(eventsBefore) && session.Data.ScriptArchiveBytes().SequenceEqual(scriptsBefore), "NurseUi_RestoreRemovesTheNurseAndItsScript");
            Click(nurseButton); bool placingAgain = canvas.Placing; canvas.CancelPlacement();
            check(placingAgain && !canvas.Placing && form.Visibility != Visibility.Visible && session.Data.Bytes().SequenceEqual(eventsBefore), "NurseUi_CancellingThePlacementChangesNothing");
            check(AutomationProperties.GetName(mode).Length > 0 && mode.Items.Cast<ComboBoxItem>().All(i => AutomationProperties.GetName(i).Length > 0 && AutomationProperties.GetHelpText(i).Length > 0), "NpcScriptUi_ModesAreAccessible");
        }
        finally { Directory.Delete(temp, true); }
    }

    /// <summary>Real CPUE/1: copy the Jubilife Pokémon Center nurse into Twinleaf Town with her behaviour, export, reopen and restore (on a COPY).</summary>
    internal static void Probe(string source, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        void Require(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
        string copy = Path.Combine(output, "Platino-copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        object report;
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var data = events.Data; byte[] eventsBefore = data.Bytes(), scriptsBefore = data.ScriptArchiveBytes();
            int nurses = 0; foreach (var m in data.Maps.GroupBy(m => m.EventFile).Select(g => g.First())) foreach (var e in data.Events(m.Id).Where(e => e.Kind == PlatinumEventKind.Npc && e.Script is >= 1 and < 2000)) if (data.ClassifyScript(m.Id, e.Script) is { Pattern: PlatinumScriptPattern.PokecenterNurse } i && i.LocalIdReference == e.Id) nurses++;
            Require(nurses == 18, "NpcScriptReal_EighteenNursesUseTheSameRecognisedScript");
            var pc = data.Maps.First(m => m.InternalName == "C01PC0101"); var nurse = data.Events(pc.Id).First(e => e.Kind == PlatinumEventKind.Npc && data.ClassifyScript(pc.Id, e.Script) is { Pattern: PlatinumScriptPattern.PokecenterNurse });
            var twin = data.Maps.First(m => m.InternalName == "T01"); var clip = new NdsNpcClip(nurse, false, pc.Id, pc.EventFile);
            var options = NdsNpcStructurePlanner.BehaviourOptions(data, clip, twin.Id); Require(options.Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Available, "NpcScriptReal_NurseCopyIntoTwinleafIsAvailable");
            var terrain = project.LoadTerrain().Terrain; var model = new NdsMapCanvasModel(data.Layout(twin.Id), data.Events(twin.Id), terrain, data.Bounds(twin.Id));
            var draft = NdsNpcStructurePlanner.PasteDraft(data, clip, twin.Id, 115, 876); var (script, binding) = NdsNpcStructurePlanner.BindingFor(data, clip, twin.Id, NdsBehaviourMode.WithBehaviour, draft.Id);
            var plan = NdsNpcStructurePlanner.PlanAdd(data, model, twin.Id, nurse, draft with { Script = script });
            Require(plan.CanPropose && plan.Record is not null, "NpcScriptReal_PlanAtAClearTwinleafTileIsProposable");
            int entriesBefore = data.ScriptEntryCount(twin.Id);
            var proposal = events.PreviewAdd(twin.Id, plan.Record!, binding); Require(events.Revision == 0 && events.ScriptReplacement() is null, "NpcScriptReal_PreviewWritesNothing");
            string cp = Path.Combine(output, "nurse.ndsevents.json"); events.Save(proposal, cp); project.EventChanged();
            var after = events.Data; var added = after.Npc(twin.Id, after.NpcCount(twin.Id) - 1);
            Require(after.ScriptEntryCount(twin.Id) == entriesBefore + 1 && added.Script == entriesBefore + 1 && after.ClassifyScript(twin.Id, added.Script) is { Pattern: PlatinumScriptPattern.PokecenterNurse } ni && ni.LocalIdReference == added.Id && added.Graphics == nurse.Graphics, "NpcScriptReal_TwinleafGetsANurseWithItsOwnRetargetedScript");
            string built = Path.Combine(output, "Platino-enfermera-Hojaverde.nds"); project.Export(built);
            using (var baseline = NdsRom.Open(copy)) using (var rebuilt = NdsRom.Open(built))
            {
                Require(baseline.Files.Where(x => x.Path != PlatinumEvents.EventPath && x.Path != PlatinumEvents.ScriptPath).All(x => baseline.ReadFile(x).SequenceEqual(rebuilt.ReadFile(x.Path))), "NpcScriptReal_EveryOtherNdsFileIsByteExact");
                var o = Narc.Read(baseline.ReadFile(PlatinumEvents.ScriptPath)).Files; var w = Narc.Read(rebuilt.ReadFile(PlatinumEvents.ScriptPath)).Files;
                Require(o.Count == w.Count && Enumerable.Range(0, o.Count).Where(i => i != twin.ScriptFile).All(i => o[i].AsSpan().SequenceEqual(w[i])), "NpcScriptReal_EveryOtherScriptFileIsByteExact");
                int h = 4 * entriesBefore + 2; Require(w[twin.ScriptFile].AsSpan(h + 4, o[twin.ScriptFile].Length - h).SequenceEqual(o[twin.ScriptFile].AsSpan(h)) && Enumerable.Range(0, entriesBefore).All(i => PlatinumScriptFile.EntryOffsets(w[twin.ScriptFile])[i] == PlatinumScriptFile.EntryOffsets(o[twin.ScriptFile])[i] + 4), "NpcScriptReal_TwinleafExistingScriptCodeIsUnchanged");
            }
            using (var reopened = NdsProject.Open(cp, false))
            {
                Require(reopened.Events.Data.Bytes().SequenceEqual(after.Bytes()) && reopened.Events.Data.ScriptArchiveBytes().SequenceEqual(after.ScriptArchiveBytes()), "NpcScriptReal_V3CheckpointReopensExactly");
                reopened.Events.Save(reopened.Events.PreviewUndo(), Path.Combine(output, "restored.ndsevents.json")); reopened.EventChanged();
                Require(reopened.Events.Data.Bytes().SequenceEqual(eventsBefore) && reopened.Events.Data.ScriptArchiveBytes().SequenceEqual(scriptsBefore), "NpcScriptReal_UndoRestoresEventsAndScripts");
                reopened.Export(Path.Combine(output, "Platino-restaurado.nds"));
            }
            report = new { profile = "CPUE/1", sourceSha256 = hash, nurses, sourceMap = new { MapId = pc.Id, pc.InternalName, NpcIndex = nurse.Index, NpcId = nurse.Id, nurse.Script }, target = new { MapId = twin.Id, twin.InternalName, twin.ScriptFile, NpcIndex = added.Index, NpcId = added.Id, added.X, added.Y, added.Script },
                scriptBytes = Convert.ToHexString(PlatinumScriptFile.NurseScript(added.Id)), gameplay = "NOT tested: heal the party in Twinleaf in the emulator" };
        }
        // «Añadir enfermera» on a route (outside any Pokémon Center), from the accredited template of the ROM
        object nurseReport;
        using (var project = NdsProject.Open(copy, false))
        {
            var events = project.Events; var data = events.Data; byte[] eventsBefore = data.Bytes(), scriptsBefore = data.ScriptArchiveBytes();
            var template = NdsNpcStructurePlanner.NurseTemplate(data, out int count);
            Require(template is not null && count == 18 && data.Map(template.SourceMap).InternalName.Contains("PC"), "NurseReal_TemplateIsOneOfTheEighteenRomNurses");
            var route = data.Maps.First(m => m.InternalName == "R201");
            Require(NdsNpcStructurePlanner.BehaviourOptions(data, template!, route.Id).Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Available, "NurseReal_Route201AcceptsAFunctionalNurse");
            var model = new NdsMapCanvasModel(data.Layout(route.Id), data.Events(route.Id), project.LoadTerrain().Terrain, data.Bounds(route.Id));
            var anchor = data.Events(route.Id).First(e => e.Kind == PlatinumEventKind.Npc);
            NdsAddPlan? plan = null; NdsAddDraft? draft = null; NdsScriptBinding? binding = null;
            for (int r = 1; r <= 12 && plan is not { Outcome: NdsMoveOutcome.Ready }; r++)
                for (int dx = -r; dx <= r && plan is not { Outcome: NdsMoveOutcome.Ready }; dx++)
                    foreach (int dy in new[] { -r, r })
                    {
                        var d = NdsNpcStructurePlanner.PasteDraft(data, template!, route.Id, anchor.X + dx, anchor.Y + dy); var (sc, b) = NdsNpcStructurePlanner.BindingFor(data, template!, route.Id, NdsBehaviourMode.WithBehaviour, d.Id);
                        var p = NdsNpcStructurePlanner.PlanAdd(data, model, route.Id, template!.Source, d with { Script = sc });
                        if (p.Outcome == NdsMoveOutcome.Ready) { plan = p; draft = d with { Script = sc }; binding = b; break; }
                    }
            Require(plan is { CanPropose: true }, "NurseReal_AFreeRouteTileIsFound");
            events.Save(events.PreviewAdd(route.Id, plan!.Record!, binding), Path.Combine(output, "nurse-route.ndsevents.json")); project.EventChanged();
            var added = events.Data.Npc(route.Id, events.Data.NpcCount(route.Id) - 1);
            Require(added.Graphics == template!.Source.Graphics && events.Data.ClassifyScript(route.Id, added.Script) is { Pattern: PlatinumScriptPattern.PokecenterNurse } ri && ri.LocalIdReference == added.Id, "NurseReal_RouteNurseHasHerOwnScript");
            string built = Path.Combine(output, "Platino-enfermera-Ruta201.nds"); project.Export(built);
            using (var baseline = NdsRom.Open(copy)) using (var rebuilt = NdsRom.Open(built))
                Require(baseline.Files.Where(x => x.Path != PlatinumEvents.EventPath && x.Path != PlatinumEvents.ScriptPath).All(x => baseline.ReadFile(x).SequenceEqual(rebuilt.ReadFile(x.Path))), "NurseReal_EveryOtherNdsFileIsByteExact");
            events.Save(events.PreviewUndo(), Path.Combine(output, "nurse-route-restored.ndsevents.json")); project.EventChanged();
            Require(events.Data.Bytes().SequenceEqual(eventsBefore) && events.Data.ScriptArchiveBytes().SequenceEqual(scriptsBefore), "NurseReal_RestoreIsExact");
            nurseReport = new { template = new { MapId = template.SourceMap, data.Map(template.SourceMap).InternalName, NpcIndex = template.Source.Index, NpcId = template.Source.Id, template.Source.Graphics }, route = new { MapId = route.Id, route.InternalName, route.ScriptFile, NpcIndex = added.Index, NpcId = added.Id, added.X, added.Y, added.Script }, gameplay = "NOT tested: heal the party on Route 201 in the emulator" };
        }
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "Platino-restaurado.nds")))) == hash && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) == hash, "NpcScriptReal_RestoredRomAndSourceAreIdentical");
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { copy = report, addNurse = nurseReport }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Real nurse copy finished; gameplay NOT tested");
    }
}
