using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;

internal static class SwordShieldChecks
{
    private const string Sword = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000";
    private const string Shield = "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000";
    internal static byte[] Table()
    {
        byte[] data = new byte[SwordShieldPersonalTable.EntryCount * SwordShieldPersonalTable.EntrySize];
        // Opaque unrelated bytes are deliberately nonzero, not a self-serialized fixture.
        for (int i = 1; i <= 3; i++)
        {
            int at = i * 0xB0;
            for (int f = 0; f < 6; f++) data[at + f] = (byte)(40 + f + i);
            data[at + 6] = 11; data[at + 7] = 3; data[at + 8] = 45;
            data[at + 0x21] = 0x40; data[at + 0x20] = 1;
            for(int slot=0;slot<3;slot++)BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at+0x18+slot*2),(ushort)(1+slot));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at + 0x4C), (ushort)i);
            for (int f = 0x60; f < 0xB0; f++) data[at + f] = (byte)(f ^ i);
        }
        return data;
    }
    private static (byte[] Npdm, byte[] Nso) Executable(bool shield = false)
    {
        byte[] n = new byte[0x180], s = new byte[0x100];
        "META"u8.CopyTo(n); BinaryPrimitives.WriteUInt32LittleEndian(n.AsSpan(0x70), 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(n.AsSpan(0x74), 0x80); "ACI0"u8.CopyTo(n.AsSpan(0x100));
        BinaryPrimitives.WriteUInt64LittleEndian(n.AsSpan(0x110), shield ? 0x01008DB008C2C000UL : 0x0100ABF008968000UL);
        "NSO0"u8.CopyTo(s); Convert.FromHexString(shield ? Shield : Sword).CopyTo(s, 0x40); return (n, s);
    }
    internal static string Fixture(string parent, string name, bool shield = false)
    {
        string root = Path.Combine(parent, name); Directory.CreateDirectory(Path.Combine(root, "exefs"));
        var exe = Executable(shield); File.WriteAllBytes(Path.Combine(root, "exefs/main.npdm"), exe.Npdm); File.WriteAllBytes(Path.Combine(root, "exefs/main"), exe.Nso);
        string personal = Path.Combine(root, "romfs", SwordShieldProject.PersonalRelativePath); Directory.CreateDirectory(Path.GetDirectoryName(personal)!); File.WriteAllBytes(personal, Table());
        return root;
    }
    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        foreach (bool shield in new[] { false, true })
        {
            var exe = Executable(shield); var id = SwordShieldIdentity.Read(exe.Npdm, exe.Nso);
            check(id.CanEdit && id.ProfileVersion == "1.3.2" && id.TitleId == (shield ? "01008DB008C2C000" : "0100ABF008968000"), shield ? "Swsh_Identity_Shield132" : "Swsh_Identity_Sword132");
        }
        var bad = Executable(); bad.Npdm[0] = 0; reject(() => SwordShieldIdentity.Read(bad.Npdm, bad.Nso), "Swsh_Identity_RejectInvalidMeta");
        bad = Executable(); BinaryPrimitives.WriteUInt32LittleEndian(bad.Npdm.AsSpan(0x70), uint.MaxValue); reject(() => SwordShieldIdentity.Read(bad.Npdm, bad.Nso), "Swsh_Identity_RejectInvalidAciBounds");
        bad = Executable(); bad.Npdm[0x100] = 0; reject(() => SwordShieldIdentity.Read(bad.Npdm, bad.Nso), "Swsh_Identity_RejectInvalidAciMagic");
        bad = Executable(); bad.Npdm[0x110] = 1; reject(() => SwordShieldIdentity.Read(bad.Npdm, bad.Nso), "Swsh_Identity_RejectOtherTitle");
        bad = Executable(); bad.Nso[0] = 0; reject(() => SwordShieldIdentity.Read(bad.Npdm, bad.Nso), "Swsh_Identity_RejectInvalidNso");
        bad = Executable(); bad.Nso[0x40] ^= 1; check(!SwordShieldIdentity.Read(bad.Npdm, bad.Nso).CanEdit, "Swsh_Identity_UnknownBuildReadOnly");
        bad = Executable(); Convert.FromHexString(Shield).CopyTo(bad.Nso, 0x40); check(!SwordShieldIdentity.Read(bad.Npdm, bad.Nso).CanEdit, "Swsh_Identity_RejectTitleBuildMismatch");
        bad = Executable(); reject(() => SwordShieldIdentity.Read(bad.Npdm.AsSpan(0, 16), bad.Nso), "Swsh_Identity_RejectTruncatedNpdm");
        reject(() => SwordShieldIdentity.Read(bad.Npdm, bad.Nso.AsSpan(0, 32)), "Swsh_Identity_RejectTruncatedNso");
        var bytes = Table(); var table = new SwordShieldPersonalTable(bytes);
        check(table.ToArray().SequenceEqual(bytes) && table.Entries().Count == 3 && table.Read(1).Values[0] == 41 && table.Read(1).BST == 261, "Swsh_Personal_ReadAndIdentityRoundtrip");
        var identityBytes = (byte[])bytes.Clone();
        Array.Copy(bytes, 0xB0, identityBytes, 810 * 0xB0, 0xB0);
        BinaryPrimitives.WriteUInt32LittleEndian(identityBytes.AsSpan(810 * 0xB0 + 0x4C), 948);
        BinaryPrimitives.WriteUInt16LittleEndian(identityBytes.AsSpan(810 * 0xB0 + 0x5E), 17);
        var speciesTable = new SwordShieldPersonalTable(identityBytes);
        check(speciesTable.Read(810).Species == 810 && speciesTable.Read(810).Form == 0, "Swsh_Personal_NationalSpeciesDoesNotUseGraphicsModelId");
        check(speciesTable.ToArray().SequenceEqual(identityBytes), "Swsh_Personal_IdentityResolutionPreservesOpaqueBytes");
        var forms = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(forms.AsSpan(0xB0 + 0x1E), 899); forms[0xB0 + 0x20] = 3;
        Array.Copy(bytes, 0xB0, forms, 899 * 0xB0, 0xB0); Array.Copy(bytes, 0xB0, forms, 900 * 0xB0, 0xB0);
        BinaryPrimitives.WriteUInt32LittleEndian(forms.AsSpan(899 * 0xB0 + 0x4C), 999);
        var formsTable = new SwordShieldPersonalTable(forms);
        check(formsTable.Read(899).Species == 1 && formsTable.Read(899).Form == 1 && formsTable.Read(900).Species == 1 && formsTable.Read(900).Form == 2, "Swsh_Personal_AlternateFormsResolveFromBaseLinks");
        check(formsTable.WithValue(899, SwordShieldField.Type1, 17)[899 * 0xB0 + 6] == 17 && formsTable.Read(1).Values[6] == 11, "Swsh_Personal_FormEditPreservesBaseEntry");
        var collision = (byte[])forms.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(collision.AsSpan(2 * 0xB0 + 0x1E), 899); collision[2 * 0xB0 + 0x20] = 2;
        reject(() => new SwordShieldPersonalTable(collision), "Swsh_Personal_RejectAmbiguousFormOwners");
        var baseLink = (byte[])forms.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(baseLink.AsSpan(0xB0 + 0x1E), 898);
        reject(() => new SwordShieldPersonalTable(baseLink), "Swsh_Personal_RejectFormLinkIntoBaseSpecies");
        var unmapped = (byte[])bytes.Clone(); Array.Copy(bytes, 0xB0, unmapped, 1191 * 0xB0, 0xB0); var unmappedTable = new SwordShieldPersonalTable(unmapped);
        check(unmappedTable.Read(1191).Species == 0 && unmappedTable.Read(1191).Label.Contains("sin resolver"), "Swsh_Personal_UnmappedFormNotMisidentified");
        reject(() => unmappedTable.WithValue(1191, SwordShieldField.HP, 75), "Swsh_Personal_RejectUnmappedFormEdit");
        for (int field = 0; field <= 8; field++)
        {
            int min = field < 6 ? 1 : 0, max = field is 6 or 7 ? 17 : 255;
            foreach (int value in new[] { min, max })
            {
                byte[] edited = table.WithValue(2, (SwordShieldField)field, value);
                int at = 2 * 0xB0 + field;
                check(edited[at] == value && edited.Where((b, i) => i != at).SequenceEqual(bytes.Where((b, i) => i != at)) && table.ToArray().SequenceEqual(bytes), $"Swsh_Personal_OnlySelectedByte_{field}_{value}");
            }
            reject(() => table.WithValue(1, (SwordShieldField)field, min - 1), $"Swsh_Personal_RejectBelowRange_{field}");
            reject(() => table.WithValue(1, (SwordShieldField)field, max + 1), $"Swsh_Personal_RejectAboveRange_{field}");
        }
        reject(() => table.Read(0), "Swsh_Personal_RejectDummyEntry"); reject(() => table.Read(1192), "Swsh_Personal_RejectOutsideEntry");
        reject(() => table.WithValue(4, SwordShieldField.HP, 90), "Swsh_Personal_RejectUnavailableEntry");
        reject(() => table.WithValue(1, (SwordShieldField)SwordShieldPersonalTable.FieldCount, 5), "Swsh_Personal_RejectUnknownField");
        reject(() => new SwordShieldPersonalTable(bytes.AsSpan(0, bytes.Length - 1)), "Swsh_Personal_RejectTruncatedTable");
        reject(() => new SwordShieldPersonalTable(new byte[bytes.Length + 1]), "Swsh_Personal_RejectOversizedTable");
        reject(() => new SwordShieldPersonalTable(new byte[bytes.Length]), "Swsh_Personal_RejectEmptyTable");
        var invalid = (byte[])bytes.Clone(); invalid[0xB0 + 6] = 18; reject(() => new SwordShieldPersonalTable(invalid), "Swsh_Personal_RejectUnknownType");
        invalid = (byte[])bytes.Clone(); invalid[0xB0] = 0; reject(() => new SwordShieldPersonalTable(invalid), "Swsh_Personal_RejectZeroStat");
        invalid = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(0xB0 + 0x1E), 1192); reject(() => new SwordShieldPersonalTable(invalid), "Swsh_Personal_RejectFormPointer");
        string scratch = Path.Combine(Path.GetTempPath(), "DR-Swsh-Checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        try
        {
            string root = Fixture(scratch, "sword"), otherRoot = Fixture(scratch, "shield", true);
            var before = SourceFiles(root); var session = SwordShieldProject.Open(root); var other = SwordShieldProject.Open(otherRoot);
            check(SourceFiles(root).SequenceEqual(before), "Swsh_Project_OpenCreatesNoFiles");
            var noop = session.Preview(1, SwordShieldField.HP, 41); session.Apply(noop);
            check(!session.HasChanges && session.History.Count == 0, "Swsh_Project_NoOpLeavesEmptyHistory");
            reject(() => session.Export(Path.Combine(scratch, "empty")), "Swsh_Export_NoOpProducesNoMod");
            check(!Directory.Exists(Path.Combine(scratch, "empty")), "Swsh_Export_NoOpCreatesNoFolder");
            var preview = session.Preview(1, SwordShieldField.HP, 77);
            check(session.Read(1).Values[0] == 41 && session.History.Count == 0, "Swsh_Project_PreviewDoesNotApply");
            reject(() => other.Apply(preview), "Swsh_Project_RejectForeignPreview");
            byte[] detached = preview.Result; detached[0xB0] = 1;
            check(preview.Result[0xB0] == 77, "Swsh_Project_PreviewBytesAreFrozen");
            var stale = session.Preview(2, SwordShieldField.Attack, 89); session.Apply(preview);
            reject(() => session.Apply(stale), "Swsh_Project_RejectStalePreview");
            check(session.Read(1).Values[0] == 77 && other.Read(1).Values[0] == 41 && session.History.Single() == new SwordShieldChange(1, "HP", 41, 77), "Swsh_Project_ApplyIsolatedAndJournaled");
            reject(() => session.Export(Path.Combine(root, "bad-mod")), "Swsh_Export_RejectInsideSource");
            reject(() => session.Export(scratch), "Swsh_Export_RejectSourceAncestor");
            string exported = Path.Combine(scratch, "exported"); var result = session.Export(exported);
            string resource = Path.Combine(exported, "atmosphere/contents/0100ABF008968000/romfs", SwordShieldProject.PersonalRelativePath);
            var actual = File.ReadAllBytes(resource);
            check(actual[0xB0] == 77 && actual.Where((b, i) => i != 0xB0).SequenceEqual(bytes.Where((b, i) => i != 0xB0)), "Swsh_Export_OnlyIntendedByteChanges");
            check(SourceFiles(root).SequenceEqual(before), "Swsh_Export_OriginalExtractionByteIdentical");
            check(Directory.GetFiles(exported, "*", SearchOption.AllDirectories).Length == 3 && !Directory.GetFiles(exported, "*", SearchOption.AllDirectories).Any(p => p.EndsWith("main") || p.EndsWith("main.npdm")), "Swsh_Export_OnlyResourceManifestAndReadme");
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(exported, "definitive-mod.json")));
            var doc = manifest.RootElement;
            check(doc.GetProperty("TitleId").GetString() == "0100ABF008968000" && doc.GetProperty("Changes").GetArrayLength() == 1 && doc.GetProperty("Operations").GetArrayLength() == 1 && doc.GetProperty("SourcePersonalSha256").GetString() == Convert.ToHexString(SHA256.HashData(bytes)) && doc.GetProperty("ResourceSha256").GetString() == Convert.ToHexString(SHA256.HashData(actual)) && result.ChangedValues == 1, "Swsh_Export_ManifestMatchesBytesAndHistory");
            reject(() => session.Export(exported), "Swsh_Export_RejectExistingDestination");
            string occupied = Path.Combine(scratch, "occupied"); File.WriteAllText(occupied, "keep");
            reject(() => session.Export(occupied), "Swsh_Export_RejectExistingFile");
            check(File.ReadAllText(occupied) == "keep", "Swsh_Export_ExistingFilePreserved");

            check(!Directory.GetDirectories(scratch).Any(p => Path.GetFileName(p).StartsWith(".definitive-swsh-")), "Swsh_Export_NoLeakedStaging");
            session.Apply(session.Preview(1, SwordShieldField.HP, 41));
            check(!session.HasChanges && session.NetChanges().Count == 0, "Swsh_Project_RevertingValueProducesNoMod");
            session.Reset(); check(session.History.Count == 0 && !session.HasChanges, "Swsh_Project_ResetClearsOnlyDraftAndHistory");
            session.Apply(session.Preview(2, SwordShieldField.Attack, 99));
            var pending = session.Preview(1, SwordShieldField.HP, 70);
            string main = Path.Combine(root, "exefs/main"); var changedMain = File.ReadAllBytes(main); changedMain[^1] ^= 1; File.WriteAllBytes(main, changedMain);
            reject(() => session.Apply(pending), "Swsh_Project_RejectChangedSourceApply");
            check(session.Read(1).Values[0] == 41 && session.History.Count == 1, "Swsh_Project_SourceFailurePreservesDraftAndHistory");
            reject(() => session.Export(Path.Combine(scratch, "stale-source")), "Swsh_Export_RejectChangedSource");
            reject(() => session.Preview(1, SwordShieldField.HP, 70), "Swsh_Project_RejectChangedSourcePreview");
            string unknownRoot = Fixture(scratch, "unknown"); string unknownMain = Path.Combine(unknownRoot, "exefs/main"); var unknown = File.ReadAllBytes(unknownMain); unknown[0x40] = 0; File.WriteAllBytes(unknownMain, unknown);
            var readOnly = SwordShieldProject.Open(unknownRoot); check(readOnly.Entries.Count == 3, "Swsh_Project_UnknownBuildStillReadable");
            reject(() => readOnly.Preview(1, SwordShieldField.HP, 77), "Swsh_Project_UnknownBuildCannotEdit"); reject(() => readOnly.Export(Path.Combine(scratch, "unknown-mod")), "Swsh_Project_UnknownBuildCannotExport");
            reject(() => SwordShieldProject.Open(scratch), "Swsh_Project_RejectMissingExtraction");
        }
        finally { Directory.Delete(scratch, true); }
    }
    internal static void RunUi(Action<bool, string> check)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "DR-Swsh-Ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        var window = new ThreeDS.Progressive.SwordShieldPrototypeWindow();
        try
        {
            var entries = (System.Windows.Controls.ListBox)window.FindName("SwitchEntries");
            var field = (System.Windows.Controls.ComboBox)window.FindName("SwitchField");
            var value = (System.Windows.Controls.TextBox)window.FindName("SwitchValue");
            var preview = (System.Windows.Controls.Button)window.FindName("SwitchPreview");
            var apply = (System.Windows.Controls.Button)window.FindName("SwitchApply");
            var export = (System.Windows.Controls.Button)window.FindName("SwitchExport");
            var journal = (System.Windows.Controls.TextBox)window.FindName("SwitchChanges");
            check(!preview.IsEnabled && !apply.IsEnabled && !export.IsEnabled, "Swsh_UI_NoProjectDisablesEditing");
            bool openSwitch = false;
            var start = new ThreeDS.Progressive.StartView(null, false, _ => { }, () => { }, () => { }, () => { }, () => openSwitch = true);
            start.Measure(new System.Windows.Size(900, 700)); start.Arrange(new System.Windows.Rect(0, 0, 900, 700)); start.UpdateLayout();
            var switchEntry = Descendants<System.Windows.Controls.Button>(start).Single(b => b.Content?.ToString()?.Contains("editor Switch") == true);
            switchEntry.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            check(openSwitch && System.Windows.Automation.AutomationProperties.GetName(switchEntry).Contains("Espada/Escudo"), "Swsh_UI_InicioInvokesSwitchEditor");
            var project = SwordShieldProject.Open(Fixture(scratch, "ui")); window.PresentProject(project);
            check(entries.Items.Count == 3 && preview.IsEnabled && !apply.IsEnabled && !export.IsEnabled && !field.IsEditable, "Swsh_UI_ProjectLoadsEditableEntries");
            check(System.Windows.Automation.AutomationProperties.GetLabeledBy(field) is System.Windows.Controls.Label && System.Windows.Automation.AutomationProperties.GetLabeledBy(value) is System.Windows.Controls.Label, "Swsh_UI_LabelsAssociatedWithControls");
            check(System.Windows.Automation.AutomationProperties.GetName(entries).Length > 0 && System.Windows.Automation.AutomationProperties.GetName(journal).Length > 0 && journal.IsReadOnly && preview.Focusable && apply.Focusable && export.Focusable, "Swsh_UI_AccessibleNamesAndFocusableActions");
            value.Text = "78"; preview.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            check(project.History.Count == 0 && apply.IsEnabled && journal.Text.Contains("41 → 78"), "Swsh_UI_PreviewWaitsForApply");
            apply.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            check(project.Read(1).Values[0] == 78 && project.History.Count == 1 && export.IsEnabled && !apply.IsEnabled, "Swsh_UI_ApplyUpdatesDraftAndEnablesExport");
            value.Text = "79"; preview.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); value.Text = "80";
            check(!apply.IsEnabled && project.History.Count == 1, "Swsh_UI_EditingInvalidatesPreview");
            var root = (System.Windows.FrameworkElement)window.Content;
            foreach (var size in new[] { new System.Windows.Size(600, 440), new System.Windows.Size(950, 700) })
            {
                root.Measure(size); root.Arrange(new System.Windows.Rect(size)); root.UpdateLayout();
                var bounds = export.TransformToAncestor(root).TransformBounds(new System.Windows.Rect(export.RenderSize));
                var applyBounds = apply.TransformToAncestor(root).TransformBounds(new System.Windows.Rect(apply.RenderSize));
                check(bounds.Bottom <= root.ActualHeight + 1 && bounds.Top >= 0 && bounds.Left >= 0 && bounds.Right <= root.ActualWidth + 1 && applyBounds.Bottom <= root.ActualHeight + 1 && applyBounds.Top >= 0 && applyBounds.Right <= root.ActualWidth + 1, $"Swsh_UI_ExportVisible_{size.Width}");
            }
            var palette = System.Windows.Application.Current;
            try
            {
                ThreeDS.Progressive.AccessiblePalette.Update(palette, true);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                check(window.Foreground == (System.Windows.Media.Brush)palette.FindResource("TextMain") && window.Background == (System.Windows.Media.Brush)palette.FindResource("Bg"), "Swsh_UI_HighContrastUsesSharedResources");
            }
            finally { ThreeDS.Progressive.AccessiblePalette.Update(palette, false); }
            var unknown = Executable(); unknown.Nso[0x40] = 0;
            string readOnlyRoot = Fixture(scratch, "readonly"); File.WriteAllBytes(Path.Combine(readOnlyRoot, "exefs/main"), unknown.Nso);
            window.PresentProject(SwordShieldProject.Open(readOnlyRoot));
            check(entries.Items.Count == 3 && !preview.IsEnabled && !apply.IsEnabled && !export.IsEnabled && !value.IsEnabled, "Swsh_UI_UnknownBuildReadOnly");
        }
        finally { ThreeDS.Progressive.StudioDialog.AutomatedChecks = true; window.Close(); Directory.Delete(scratch, true); }
    }
    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);
            if(child is T match)yield return match;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
    private static string[] SourceFiles(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().Select(p => Path.GetRelativePath(root, p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    internal static void ProbeReal(string root, string? destination)
    {
        var project = SwordShieldProject.Open(root); Console.WriteLine($"Read-only input: {project.Identity.Game}, {project.Identity.TitleId}, profile {project.Identity.ProfileVersion ?? "unknown"}, {project.Entries.Count} present entries, source SHA256 {project.SourceHash}");
        Console.WriteLine("Build ID: " + project.Identity.BuildId);
        if (destination is null) return;
        var entry = project.Entries.First(e => e.Species == 1 && e.Form == 0);
        int original = entry.Values[0]; var preview = project.Preview(entry.Index, SwordShieldField.HP, original == 255 ? 254 : original + 1); project.Apply(preview);
        var result = project.Export(destination); Console.WriteLine($"Exported diagnostic mod: {result.Folder}; {result.ChangedValues} byte changed; output SHA256 {result.ResourceSha256}. Gameplay NOT tested.");
    }
}
