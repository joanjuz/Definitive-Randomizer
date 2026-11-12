using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;
using Caps = PlayerLevelCapRecipeChecks;
using Level = pk3DS.Core.Modding.Research.PatchCapabilityLevel;
using State = pk3DS.Core.Modding.Research.PatchResourceState;

/// <summary>
/// Read-only capability report of Player Level Caps: every state a hook site can be in, the level that follows from
/// it and its agreement with the installer. Synthetic binaries only (a CRO skeleton and a zero-filled code image).
/// </summary>
internal static class PatchCapabilityChecks
{
    private const int BattleHook = (int)LevelCapPatch.BattleHook, CandyHook = (int)LevelCapPatch.CandyHook;
    private static byte[] Battle() => DefinitiveGameDataCapsChecks.StockBattle();
    private static byte[] Code() => DefinitiveGameDataCapsChecks.StockCode();
    private static uint[] Words(string field) => (uint[])typeof(LevelCapPatch).GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    private static void Set32(byte[] data, int at, uint value) => BitConverter.GetBytes(value).CopyTo(data, at);
    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static byte[] With(byte[] data, Action<byte[]> edit) { var copy = (byte[])data.Clone(); edit(copy); return copy; }

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var cases = new List<(string Name, byte[]? Battle, byte[]? Code)>();
        PatchCapabilityReport Inspect(string name, byte[]? battle, byte[]? code)
        {
            cases.Add((name, battle, code));
            string a = battle is null ? "" : Sha(battle), b = code is null ? "" : Sha(code);
            var report = LevelCapPatch.InspectCapability(battle!, code!);
            check((battle is null || Sha(battle) == a) && (code is null || Sha(code) == b), "Capability_InspectionNeverModifiesItsInput_" + name);
            return report;
        }

        // Original files.
        var stock = Inspect("Original", Battle(), Code());
        check(stock.Level == Level.Verified && stock.CanApply && stock.PatchId == "usum.player-level-caps" &&
            stock.Resource("Battle.cro").State == State.Original && stock.Resource("code.bin").State == State.Original &&
            stock.Resized.Count == 0 && stock.Changes.Any(c => c.Resource == "Battle.cro") && stock.Changes.Any(c => c.Resource == "code.bin"),
            "Capability_OriginalFilesAreVerifiedWithThePreviewOfWhatWouldChange");
        check(stock.Changes.Any(c => c.Resource == "Battle.cro" && c.Offset <= BattleHook && BattleHook < c.Offset + c.Length) &&
            stock.Changes.Any(c => c.Resource == "code.bin" && c.Offset <= CandyHook + 8 && CandyHook + 8 < c.Offset + c.Length), "Capability_PreviewIncludesBothHookSites");

        // Our own installation, and installing again.
        var table = Caps.Table();
        var installed = LevelCapPatch.InstallManaged(Battle(), Code(), table);
        var own = Inspect("Installed", installed.BattleCro, installed.CodeBin);
        check(installed.Success && own.Level == Level.Verified && own.Resource("Battle.cro").State == State.OwnInstall && own.Resource("code.bin").State == State.OwnInstall &&
            own.Changes.Count == 0 && own.Resized.Count == 0 && own.Summary.Contains("coinciden"), "Capability_OwnInstallationIsRecognisedAndReinstallingChangesNothing");
        var again = LevelCapPatch.InstallManaged(installed.BattleCro, installed.CodeBin, table);
        check(again.Success && again.ChangedCount == 0 && again.BattleCro.SequenceEqual(installed.BattleCro) && again.CodeBin.SequenceEqual(installed.CodeBin), "Capability_ReinstallingTheSameTableIsIdempotent");

        // One file only, and two different tables: states of our own that installing repairs.
        var half = Inspect("OneFileOnly", installed.BattleCro, Code());
        check(half.Level == Level.Verified && half.Resource("Battle.cro").State == State.OwnInstall && half.Resource("code.bin").State == State.Original && half.Summary.Contains("uno solo"),
            "Capability_PatchInOneFileOnlyIsReportedAndRepairable");
        var other = LevelCapPatch.InstallManaged(Battle(), Code(), LevelCapTable.Default());
        var differ = Inspect("DifferentTables", installed.BattleCro, other.CodeBin);
        check(differ.Level == Level.Verified && differ.Summary.Contains("tablas distintas"), "Capability_DifferentTablesAreReportedAndRepairable");

        // Another modification at a hook site.
        var taken = Inspect("HookEdited", With(Battle(), b => b[BattleHook] ^= 0x5A), Code());
        check(taken.Level == Level.Conflict && !taken.CanApply && taken.Resource("Battle.cro").State == State.ExternalModification && taken.Changes.Count == 0,
            "Capability_EditedHookIsAConflict");
        var foreign = Inspect("HookToForeignCode", Battle(), With(Code(), c => Set32(c, CandyHook + 8, 0xEB000100)));
        check(foreign.Level == Level.Conflict && foreign.Resource("code.bin").State == State.ExternalModification && foreign.Resource("code.bin").Evidence.Contains("no es de este parche"),
            "Capability_HookBranchingToForeignCodeIsAConflict");

        // The original sequence somewhere else: evidence, never permission.
        var original = Words("BattleOriginal");
        var moved = Inspect("SignatureMoved", With(Battle(), b =>
        {
            for (int i = 0; i < original.Length; i++) { Set32(b, BattleHook + i * 4, 0xE1A00000); Set32(b, 0x18000 + i * 4, original[i]); }
        }), Code());
        check(moved.Level == Level.NotAccredited && !moved.CanApply && moved.Resource("Battle.cro") is { State: State.Displaced, Offset: 0x18000 } && moved.Summary.Contains("no acredita"),
            "Capability_MovedSignatureIsReportedButNotAccredited");
        var twice = Inspect("SignatureTwiceElsewhere", With(Battle(), b =>
        {
            for (int i = 0; i < original.Length; i++) { Set32(b, BattleHook + i * 4, 0xE1A00000); Set32(b, 0x18000 + i * 4, original[i]); Set32(b, 0x19000 + i * 4, original[i]); }
        }), Code());
        check(twice.Level == Level.Conflict && twice.Resource("Battle.cro").State == State.ExternalModification, "Capability_AmbiguousSignatureIsNotAMovedOne");

        // Insufficient data.
        var cut = Inspect("Truncated", Battle()[..0x8000], Code());
        check(cut.Level == Level.NotAccredited && cut.Resource("Battle.cro").State == State.Truncated && cut.Resource("Battle.cro").Offset is null, "Capability_TruncatedFileIsInsufficientData");
        var none = Inspect("Missing", null, Code());
        check(none.Level == Level.NotAccredited && none.Resource("Battle.cro").State == State.Missing && !none.CanApply, "Capability_MissingFileIsInsufficientData");
        var noCode = Inspect("CodeMissing", Battle(), null);
        check(noCode.Level == Level.NotAccredited && noCode.Resource("code.bin").State == State.Missing, "Capability_MissingCodeIsInsufficientData");

        // Partial states of our own patch.
        var routine = Words("Routine");
        LevelCapPatch.GetBattleState(installed.BattleCro, out uint block);
        var damaged = Inspect("RoutineDamaged", With(installed.BattleCro, b => Set32(b, (int)block + 10 * 4, 0)), installed.CodeBin);
        check(damaged.Level == Level.NotAccredited && !damaged.CanApply && damaged.Resource("Battle.cro") is { State: State.PartialInstall } partial && partial.Offset == block,
            "Capability_HookToADamagedRoutineIsAPartialInstallation");
        var loose = Inspect("RoutineWithoutHook", With(Battle(), b => { for (int i = 0; i < routine.Length; i++) Set32(b, 0x18000 + i * 4, routine[i]); }), Code());
        check(loose.Resource("Battle.cro") is { State: State.PartialInstall, Offset: 0x18000 } && loose.Summary.Contains("anterior o incompleta"), "Capability_RoutineWithoutItsHookIsReported");

        // Earlier table formats of the patch.
        foreach (bool direct in new[] { false, true })
        {
            var earlier = Inspect("EarlierFormat" + (direct ? "V2" : "V1"), DefinitiveGameDataCapsChecks.WithEarlierFormat(Battle(), true, direct), DefinitiveGameDataCapsChecks.WithEarlierFormat(Code(), false, direct));
            check(earlier.Resource("Battle.cro").State == State.OwnInstallEarlierFormat && earlier.Resource("code.bin").State == State.OwnInstallEarlierFormat &&
                earlier.Level is Level.Verified or Level.NotAccredited, "Capability_EarlierFormatIsRecognisedAsOurs_" + (direct ? "V2" : "V1"));
        }

        // Correct hooks but no room: another modification took the free executable space.
        var full = Inspect("NoFreeSpaceInCode", Battle(), With(Code(), c =>
        {
            var hook = Words("CandyOriginal");
            Array.Fill(c, (byte)0xAA);
            for (int i = 0; i < hook.Length; i++) Set32(c, CandyHook + i * 4, hook[i]);
        }));
        check(full.Level == Level.Conflict && !full.CanApply && full.Resource("code.bin").State == State.Original && full.Summary.Contains("recursos"), "Capability_NoRoomForTheRoutineIsAConflict");

        // A CRO without spare room in its code segment: the installer relocates, and that is an adaptation.
        var tight = Inspect("NoPaddingInCro", With(Battle(), b =>
        {
            // As in a real module, the segment table lives after the code, so it moves when the code segment grows.
            Array.Copy(b, 0x140, b, 0x30800, 0x40); Array.Clear(b, 0x140, 0x40); Set32(b, 0xC8, 0x30800);
            for (int i = 0x16000; i < 0x30000; i++) b[i] = 0xAA;
        }), Code());
        check(tight.Level == Level.CompatibleByAdaptation && tight.CanApply && tight.Resized.Count == 1 && tight.Resized[0].StartsWith("Battle.cro:", StringComparison.Ordinal) &&
            tight.Resource("Battle.cro").State == State.Original && tight.Changes.All(c => c.Resource == "code.bin") && tight.Summary.Contains("reubicación se verificó"),
            "Capability_CroWithoutSpareRoomIsCompatibleByAdaptation");

        // The report never promises more or less than the installer delivers.
        foreach (var (name, battle, image) in cases.Where(c => c.Battle is not null && c.Code is not null))
        {
            bool installs;
            try { installs = LevelCapPatch.InstallManaged(battle!, image!, LevelCapTable.Default()).Success; }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException or InvalidDataException) { installs = false; }
            check(LevelCapPatch.InspectCapability(battle!, image!).CanApply == installs, "Capability_AgreesWithTheInstaller_" + name);
        }

        // Through the project service: status, refusal without writing, undo and a failed transaction.
        using var state = new TestState();
        using var f = DefinitiveGameDataChecks.Create();
        Caps.WriteStock(f);
        var service = new GamePlayerLevelCapService(f.Session);
        check(service.Capability().Level == Level.Verified && service.Status.StartsWith("Compatibilidad verificada.", StringComparison.Ordinal) && service.Status.Contains("Battle.cro") && service.Status.Contains("code.bin"),
            "Capability_ServiceStatusExplainsTheLevelAndEachFile");
        var before = Caps.Files(f);
        string code = Caps.CodePath(f);
        File.SetAttributes(code, File.GetAttributes(code) | FileAttributes.ReadOnly);
        try
        {
            bool failed = false;
            try { Caps.Install(f, table); } catch (Exception e) when (e is UnauthorizedAccessException or IOException) { failed = true; }
            check(failed, "Capability_FailedTransactionIsReported");
        }
        finally { File.SetAttributes(code, File.GetAttributes(code) & ~FileAttributes.ReadOnly); }
        check(Caps.Same(before, Caps.Files(f)) && new GamePlayerLevelCapService(f.Session).Capability() is { Level: Level.Verified } clean && clean.Resources.All(r => r.State == State.Original),
            "Capability_FailedTransactionLeavesNoPartialInstallation");
        Caps.Install(f, table);
        check(new GamePlayerLevelCapService(f.Session).Capability().Resources.All(r => r.State == State.OwnInstall), "Capability_InstalledThroughTheServiceIsOwn");
        RestorePoints.Undo(f.Session.History, f.Root, f.Session.History.Read().Entries[^1].Id);
        check(Caps.Same(before, Caps.Files(f)) && new GamePlayerLevelCapService(f.Session).Capability().Resources.All(r => r.State == State.Original), "Capability_UndoGivesBackTheOriginalState");

        // Adaptation through the service: installed by relocating, read back and undone to the exact original bytes.
        File.WriteAllBytes(Caps.BattlePath(f), cases.Single(c => c.Name == "NoPaddingInCro").Battle!);
        before = Caps.Files(f);
        var adapting = new GamePlayerLevelCapService(f.Session);
        check(adapting.Capability().Level == Level.CompatibleByAdaptation && adapting.Status.StartsWith("Compatibilidad compatible por adaptación.", StringComparison.Ordinal),
            "Capability_ServiceReportsTheAdaptation");
        adapting.Apply(adapting.Preview(table));
        check(Caps.Installed(f, table) && new FileInfo(Caps.BattlePath(f)).Length > Battle().Length &&
            new GamePlayerLevelCapService(f.Session).Capability() is { Level: Level.Verified } adapted && adapted.Resources.All(r => r.State == State.OwnInstall),
            "Capability_AdaptedInstallationReadsBackAsOwn");
        RestorePoints.Undo(f.Session.History, f.Root, f.Session.History.Read().Entries[^1].Id);
        check(Caps.Same(before, Caps.Files(f)), "Capability_AdaptedInstallationIsUndoneToTheExactBytes");

        File.WriteAllBytes(Caps.BattlePath(f), With(Battle(), b => b[BattleHook] ^= 0x5A));
        before = Caps.Files(f); int entries = f.Session.History.Read().Entries.Count;
        var blocked = new GamePlayerLevelCapService(f.Session);
        check(blocked.Status.StartsWith("Compatibilidad conflicto.", StringComparison.Ordinal), "Capability_ServiceStatusNamesTheConflict");
        try { blocked.Preview(table); check(false, "Capability_ConflictIsRefusedWithItsExplanation"); }
        catch (InvalidDataException e) { check(e.Message.Contains("No se escribirá nada") && e.Message.Contains("conflicto") && e.Message.Contains("Battle.cro"), "Capability_ConflictIsRefusedWithItsExplanation"); }
        check(Caps.Same(before, Caps.Files(f)) && f.Session.History.Read().Entries.Count == entries, "Capability_RefusalWritesNothing");
        File.Delete(Caps.BattlePath(f));
        check(new GamePlayerLevelCapService(f.Session).Capability().Resource("Battle.cro").State == State.Missing, "Capability_ServiceReportsAMissingFileInsteadOfFailing");
    }

    /// <summary>Second patch on the same model: Persistent Items, located by unique masked signatures instead of fixed offsets.</summary>
    internal static void RunPersistentItems(Action<bool, string> check, Action<Action, string> reject)
    {
        byte[] cro = Review73Checks.Cro();
        var cases = new List<(string Name, byte[] Cro)>();
        PatchCapabilityReport Inspect(string name, byte[] data)
        {
            cases.Add((name, data)); string hash = Sha(data);
            var report = GamePersistentItemsService.InspectCapability(data);
            check(Sha(data) == hash, "PersistentCapability_InspectionNeverModifiesItsInput_" + name);
            return report;
        }
        var stock = Inspect("Original", cro);
        check(stock.Level == Level.CompatibleByAdaptation && stock.CanApply && stock.PatchId == "usum.persistent-items" && stock.Resources.Count == 2 &&
            stock.Resources.All(r => r.State == State.Original && r.Offset is not null) && stock.Changes.Any(c => c.Offset <= 0x18B && 0x18B < c.Offset + c.Length) &&
            stock.Summary.Contains("firma única"), "PersistentCapability_OriginalIsLocatedBySignatureAndPreviewed");
        byte[] patched = GamePersistentItemsService.Patch(cro).Bytes;
        var own = Inspect("Installed", patched);
        check(own.Level == Level.CompatibleByAdaptation && own.Resources.All(r => r.State == State.OwnInstall) && own.Changes.Count == 0 && own.Summary.StartsWith("El parche está instalado.", StringComparison.Ordinal),
            "PersistentCapability_OwnInstallationChangesNothing");
        var half = Inspect("OnePointOnly", With(cro, b => b[0x18B] = 0x8A));
        check(half.CanApply && half.Resources.Count(r => r.State == State.OwnInstall) == 1 && half.Summary.Contains("uno solo"), "PersistentCapability_PartialInstallationIsRepairable");
        var twice = Inspect("SignatureTwice", With(cro, b => cro.AsSpan(0x180, 24).CopyTo(b.AsSpan(0x1C0))));
        check(twice.Level == Level.NotAccredited && !twice.CanApply && twice.Resources.Any(r => r.State == State.ExternalModification && r.Evidence.Contains("veces")), "PersistentCapability_AmbiguousSignatureIsNotAccredited");
        var gone = Inspect("SignatureGone", With(cro, b => Array.Clear(b, 0x180, 24)));
        check(gone.Level == Level.NotAccredited && !gone.CanApply && gone.Resources.Any(r => r.State == State.ExternalModification && r.Offset is null) && gone.Changes.Count == 0,
            "PersistentCapability_MissingSignatureIsNotAccredited");
        var notCro = Inspect("NotACro", new byte[0x400]);
        check(notCro.Level == Level.NotAccredited && notCro.Resources.All(r => r.State == State.Truncated), "PersistentCapability_FileWithoutCroHeaderIsInsufficientData");
        check(GamePersistentItemsService.InspectCapability(null) is { Level: Level.NotAccredited } none && none.Resources.All(r => r.State == State.Missing), "PersistentCapability_MissingFileIsInsufficientData");
        foreach (var (name, data) in cases)
        {
            bool installs; try { _ = GamePersistentItemsService.Patch(data); installs = true; } catch (InvalidDataException) { installs = false; }
            check(GamePersistentItemsService.InspectCapability(data).CanApply == installs, "PersistentCapability_AgreesWithTheInstaller_" + name);
        }
        using var state = new TestState();
        using var f = new Fixture(pk3DS.Core.GameVersion.USUM);
        string path = Path.Combine(f.Game.RomFS, "Battle.cro");
        File.WriteAllBytes(path, With(cro, b => Array.Clear(b, 0x180, 24)));
        try { _ = new GamePersistentItemsService(f.Session); check(false, "PersistentCapability_ServiceRefusesWithItsExplanation"); }
        catch (InvalidDataException e) { check(e.Message.Contains("No se modificó ningún archivo") && e.Message.Contains("no acreditada") && e.Message.Contains("No se encuentra el código"), "PersistentCapability_ServiceRefusesWithItsExplanation"); }
        check(File.ReadAllBytes(path).SequenceEqual(With(cro, b => Array.Clear(b, 0x180, 24))), "PersistentCapability_RefusalWritesNothing");
    }

    internal static void RunUi(Action<bool, string> check)
    {
        if (Application.Current is null) { var app = new ThreeDS.Progressive.App(); app.InitializeComponent(); }
        using var state = new TestState();
        using var f = DefinitiveGameDataChecks.Create();
        Caps.WriteStock(f);
        var view = new ThreeDS.Progressive.PlayerLevelCapsView(f.Session);
        check(Texts(view).Any(t => t.StartsWith("Compatibilidad verificada.", StringComparison.Ordinal)), "CapabilityUi_ViewShowsTheLevelOnOpening");
        File.WriteAllBytes(Caps.BattlePath(f), With(Battle(), b => b[BattleHook] ^= 0x5A));
        var conflict = new ThreeDS.Progressive.PlayerLevelCapsView(f.Session);
        check(Texts(conflict).Any(t => t.StartsWith("Compatibilidad conflicto.", StringComparison.Ordinal) && t.Contains("Otra modificación")), "CapabilityUi_ViewExplainsAConflictOnOpening");
    }
    private static IEnumerable<string> Texts(DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is System.Windows.Controls.TextBlock text) yield return text.Text;
            if (child is DependencyObject node) foreach (string inner in Texts(node)) yield return inner;
        }
    }
}
