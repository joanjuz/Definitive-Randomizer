using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

/// <summary>Producer of the optional playerLevelCaps block. Every binary here is synthetic: a CRO skeleton and a
/// zero-filled code image that only carry the two original hook sites.</summary>
internal static class DefinitiveGameDataCapsChecks
{
    private const int BattleHook = (int)LevelCapPatch.BattleHook, CandyHook = (int)LevelCapPatch.CandyHook;
    private const string RootScope = "personal,moves,evolutions,gametext";
    private static void Set32(byte[] data, int at, uint value) => BitConverter.GetBytes(value).CopyTo(data, at);
    private static uint[] Words(string field) =>
        (uint[])typeof(LevelCapPatch).GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    internal static byte[] StockBattle()
    {
        // Minimal CRO0 skeleton: code / rodata / data segments, a relocation table and zero padding at the end of .text.
        const uint code = 0x180, codeEnd = 0x2F800, rodata = 0x30000, sectionData = 0x31000, relocations = 0x32000;
        byte[] cro = new byte[0x33000];
        Set32(cro, 0x80, 0x304F5243); Set32(cro, 0x90, (uint)cro.Length);
        Set32(cro, 0xB0, code); Set32(cro, 0xB4, rodata - code); Set32(cro, 0xB8, sectionData); Set32(cro, 0xBC, 0x1000);
        Set32(cro, 0xC8, 0x140); Set32(cro, 0xCC, 4); Set32(cro, 0x128, relocations); Set32(cro, 0x12C, 8);
        Set32(cro, 0x140, code); Set32(cro, 0x144, codeEnd - code); Set32(cro, 0x14C, rodata); Set32(cro, 0x150, sectionData - rodata);
        Set32(cro, 0x154, 1); Set32(cro, 0x158, sectionData); Set32(cro, 0x15C, 0x1000); Set32(cro, 0x160, 2); Set32(cro, 0x16C, 3);
        for (int i = 0; i < 8; i++) Set32(cro, (int)relocations + i * 12, 3);
        var original = Words("BattleOriginal");
        for (int i = 0; i < original.Length; i++) Set32(cro, BattleHook + i * 4, original[i]);
        return cro;
    }
    internal static byte[] StockCode()
    {
        byte[] code = new byte[0x226000];
        var original = Words("CandyOriginal");
        for (int i = 0; i < original.Length; i++) Set32(code, CandyHook + i * 4, original[i]);
        return code;
    }
    private static string BattlePath(Fixture f) => Path.Combine(f.Game.RomFS, "Battle.cro");
    private static string CodePath(Fixture f) => Path.Combine(f.Game.ExeFS, "code.bin");
    private static void WriteStock(Fixture f) { File.WriteAllBytes(BattlePath(f), StockBattle()); File.WriteAllBytes(CodePath(f), StockCode()); }
    private static uint Bl(int from, int to) => 0xEB000000u | ((uint)((to - (from + 8)) >> 2) & 0x00FFFFFF);
    /// <summary>Writes a routine of an earlier table format and its hook, as an older build would have left it.</summary>
    internal static byte[] WithEarlierFormat(byte[] stock, bool battle, bool directFlags)
    {
        byte[] data = (byte[])stock.Clone();
        int at = battle ? 0x20000 : 0x225C00;
        var routine = Words(directFlags ? "DirectFlagRoutine" : "LegacyRoutine");
        for (int i = 0; i < routine.Length; i++) Set32(data, at + i * 4, routine[i]);
        byte[] table = directFlags ? [0x10, 0x00, 0x08, 14, 0x11, 0x00, 0x08, 19, 0, 0, 0, 100] : [0x10, 0x08, 14, 0x11, 0x08, 19, 0, 0, 100];
        table.CopyTo(data, at + routine.Length * 4);
        if (battle) { Set32(data, BattleHook, Bl(BattleHook, at)); Set32(data, BattleHook + 4, 0xE3500001); Set32(data, BattleHook + 8, 0x1A000004); }
        else Set32(data, CandyHook + 8, Bl(CandyHook + 8, at + 0x0C));
        return data;
    }
    private static JsonObject Caps(Fixture f) => DefinitiveGameData.Build(f.Session)["playerLevelCaps"]!.AsObject();
    private static string Hooks(JsonObject block) => block["hooks"]!["battleCro"]!.GetValue<string>() + "/" + block["hooks"]!["codeBin"]!.GetValue<string>();
    private static bool NoList(JsonObject block) => !block.ContainsKey("checkpoints") && !block.ContainsKey("evaluation") && !block.ContainsKey("enforcement") &&
        !block.ContainsKey("ceiling") && block["source"]?["tableSha256"] is null;
    private static bool Is(JsonObject block, string status, string hooks) =>
        block["version"]!.GetValue<int>() == 1 && block["status"]!.GetValue<string>() == status && Hooks(block) == hooks &&
        (status == "installed" || NoList(block)) && (status == "unsupported" || block["source"] is not null);
    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static string Fingerprint(Fixture f, string titleId) => Sha(Encoding.UTF8.GetBytes(
        titleId + "\nbattle.cro:" + Sha(File.ReadAllBytes(BattlePath(f))) + "\ncode.bin:" + Sha(File.ReadAllBytes(CodePath(f)))));
    private static bool Matches(JsonObject block, LevelCapTable table) =>
        block["source"]!["tableSha256"]!.GetValue<string>() == Sha(table.ToBytes()) &&
        block["checkpoints"]!.AsArray().Count == table.Entries.Count &&
        block["checkpoints"]!.AsArray().Select((p, i) => p!["order"]!.GetValue<int>() == i + 1 && p["cap"]!.GetValue<int>() == table.Entries[i].Cap).All(ok => ok);
    private static void Install(Fixture f, LevelCapTable table)
    { var service = new GamePlayerLevelCapService(f.Session); service.Apply(service.Preview(table)); }
    private static LevelCapTable Custom() => new()
    {
        Entries =
        [
            new("Clear Normal Trial - Ilima (1dom)", 0x0010, 0x0008, 15),
            new("vista: nombre que no es del catálogo", 0x0150, 0x0004, 50),
            new("Defeat Champion Hau (Tilo)", LevelCapConditionKind.EventWorkAtLeast, 0x0044, 2000, 70),
        ],
    };

    internal static void Run(Action<bool,string> check, Action<Action,string> reject)
    {
        using var state = new TestState();
        string? replay = Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY");
        foreach (bool moon in new[] { false, true })
        {
            string suffix = moon ? "Moon" : "Sun", titleId = moon ? "00040000001B5100" : "00040000001B5000";
            using var f = DefinitiveGameDataChecks.Create(moon);
            var missing = DefinitiveGameData.Build(f.Session);
            var block = missing["playerLevelCaps"]!.AsObject();
            check(Is(block, "unsupported", "missing/missing") && block["source"] is null && block["detail"] is not null &&
                missing["source"]!["scope"]!.GetValue<string>() == RootScope, "DgdCaps_MissingFilesReportedExportKept_" + suffix);
            string rootFingerprint = missing["source"]!["fingerprint"]!.GetValue<string>();
            string Root(JsonObject doc) { var copy = doc.DeepClone().AsObject(); copy.Remove("playerLevelCaps"); return copy.ToJsonString(); }
            string rootDocument = Root(missing);

            File.WriteAllBytes(BattlePath(f), StockBattle());
            check(Is(Caps(f), "unsupported", "stock/missing") && Caps(f)["source"] is null, "DgdCaps_OneFileMissingHasNoSource_" + suffix);
            WriteStock(f);
            block = Caps(f);
            check(Is(block, "not-detected", "stock/stock") && block["detail"] is null &&
                block["source"]!["fingerprint"]!.GetValue<string>() == Fingerprint(f, titleId) &&
                block["source"]!["scope"]!.GetValue<string>() == "battle.cro,code.bin", "DgdCaps_StockIsNotDetectedWithOwnFingerprint_" + suffix);

            // A preview is a draft: it never reaches the saved files, so it is not exported.
            var service = new GamePlayerLevelCapService(f.Session);
            var preview = service.Preview(LevelCapTable.Default());
            check(Is(Caps(f), "not-detected", "stock/stock") && File.ReadAllBytes(BattlePath(f)).SequenceEqual(StockBattle()),
                "DgdCaps_PreviewWithoutApplyNotExported_" + suffix);
            service.Apply(preview);
            var installedDoc = DefinitiveGameData.Build(f.Session);
            block = installedDoc["playerLevelCaps"]!.AsObject();
            check(Is(block, "installed", "applied/applied") && Matches(block, LevelCapTable.Default()) &&
                block["evaluation"]!.GetValue<string>() == "ordered-first-incomplete" && block["ceiling"]!.GetValue<int>() == 100 &&
                block["enforcement"]!.ToJsonString() == "[\"exp-gain\",\"rare-candy\"]" && block["detail"] is null &&
                block["source"]!["fingerprint"]!.GetValue<string>() == Fingerprint(f, titleId), "DgdCaps_InstalledExportsSavedTable_" + suffix);
            check(block["checkpoints"]!.AsArray().All(p => p!["labelSource"]!.GetValue<string>() == "randomizer-catalog" &&
                LevelCapTable.KnownFlags.Any(k => k.Label == p["label"]!.GetValue<string>())), "DgdCaps_DefaultLabelsFromCatalog_" + suffix);
            check(installedDoc["source"]!["fingerprint"]!.GetValue<string>() == rootFingerprint && Root(installedDoc) == rootDocument &&
                installedDoc["source"]!["scope"]!.GetValue<string>() == RootScope && installedDoc["schemaVersion"]!.GetValue<int>() == 1,
                "DgdCaps_RootFingerprintScopeAndRecordsUnchanged_" + suffix);
            check(DefinitiveGameData.Build(f.Session).ToJsonString() == installedDoc.ToJsonString(), "DgdCaps_DeterministicBlock_" + suffix);

            // The table of the view (edited, with its own labels) is not the installed one until it is applied.
            var edited = Custom();
            _ = new GamePlayerLevelCapService(f.Session).Preview(edited);
            check(Matches(Caps(f), LevelCapTable.Default()), "DgdCaps_EditedViewTableNotExported_" + suffix);
            Install(f, edited);
            block = Caps(f);
            var points = block["checkpoints"]!.AsArray();
            check(Is(block, "installed", "applied/applied") && Matches(block, edited) &&
                points[0]!["label"]!.GetValue<string>() == "Clear Normal Trial - Ilima (1dom)" &&
                points[1]!["label"] is null && points[1]!["labelSource"] is null &&
                points[1]!["condition"]!.ToJsonString() == "{\"kind\":\"event-flag-set\",\"byte\":336,\"mask\":4}" &&
                points[2]!["condition"]!.ToJsonString() == "{\"kind\":\"event-work-at-least\",\"index\":68,\"minimum\":2000}" &&
                !block.ToJsonString().Contains("vista"), "DgdCaps_UncataloguedConditionHasNoLabel_" + suffix);

            // Undo rewrites the saved files; the export follows them, never the history.
            var entries = f.Session.History.Read().Entries;
            RestorePoints.Undo(f.Session.History, f.Root, entries[^1].Id);
            check(Is(Caps(f), "installed", "applied/applied") && Matches(Caps(f), LevelCapTable.Default()), "DgdCaps_UndoRestoresPreviousTable_" + suffix);
            RestorePoints.Undo(f.Session.History, f.Root, entries[^2].Id);
            check(Is(Caps(f), "not-detected", "stock/stock") && Caps(f)["source"]!["fingerprint"]!.GetValue<string>() == Fingerprint(f, titleId),
                "DgdCaps_UndoOfInstallIsNotDetected_" + suffix);

            // Global Template: a recipe that carries a Player Level Caps table (here, captured from another project) is only
            // a recipe. Loading and validating it writes nothing, so nothing of it is exported.
            using (var origin = DefinitiveGameDataChecks.Create(moon))
            {
                WriteStock(origin); Install(origin, edited);
                var recipe = GlobalTemplateDocument.FromHistory(origin.Session.History.Read(), "USUM", 7, null);
                var operation = ManualOperations.Read(recipe).SingleOrDefault(o => o["Kind"]!.GetValue<string>() == "playercaps");
                ManualOperations.Validate(recipe);
                check(operation?["Payload"]?["Table"]?["Entries"]?.AsArray().Count == edited.Entries.Count && Is(Caps(f), "not-detected", "stock/stock") &&
                    Is(Caps(origin), "installed", "applied/applied"), "DgdCaps_GlobalTemplateRecipeNotExported_" + suffix);
            }

            // Batch replay path (no manual-operation scope) and the bytes the legacy Batch engine writes directly.
            WriteStock(f);
            Environment.SetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY", "1");
            try { Install(f, LevelCapTable.Default()); }
            finally { Environment.SetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY", replay); }
            check(Is(Caps(f), "installed", "applied/applied") && Matches(Caps(f), LevelCapTable.Default()), "DgdCaps_BatchReplayInstallIsExported_" + suffix);
            var direct = LevelCapPatch.InstallManaged(StockBattle(), StockCode(), edited);
            File.WriteAllBytes(BattlePath(f), direct.BattleCro); File.WriteAllBytes(CodePath(f), direct.CodeBin);
            check(direct.Success && Is(Caps(f), "installed", "applied/applied") && Matches(Caps(f), edited), "DgdCaps_LegacyBatchBytesAreExported_" + suffix);

            // Copies that disagree.
            var other = LevelCapPatch.InstallManaged(StockBattle(), StockCode(), LevelCapTable.Default());
            File.WriteAllBytes(CodePath(f), other.CodeBin);
            block = Caps(f);
            check(Is(block, "inconsistent", "applied/applied") && block["detail"] is not null &&
                block["source"]!["fingerprint"]!.GetValue<string>() == Fingerprint(f, titleId), "DgdCaps_DivergentTablesInconsistent_" + suffix);
            var sameLengthOther = edited.Clone(); sameLengthOther.Entries[1] = sameLengthOther.Entries[1] with { Cap = 51 };
            File.WriteAllBytes(CodePath(f), LevelCapPatch.InstallManaged(StockBattle(), StockCode(), sameLengthOther).CodeBin);
            check(Is(Caps(f), "inconsistent", "applied/applied"), "DgdCaps_OneDifferentCapInconsistent_" + suffix);
            File.WriteAllBytes(CodePath(f), StockCode());
            check(Is(Caps(f), "inconsistent", "applied/stock"), "DgdCaps_BattleOnlyInconsistent_" + suffix);
            File.WriteAllBytes(BattlePath(f), StockBattle()); File.WriteAllBytes(CodePath(f), direct.CodeBin);
            check(Is(Caps(f), "inconsistent", "stock/applied"), "DgdCaps_CodeOnlyInconsistent_" + suffix);

            // Earlier table formats are diagnosed, never reinterpreted.
            foreach (bool directFlags in new[] { false, true })
            {
                string format = directFlags ? "V2" : "V1";
                File.WriteAllBytes(BattlePath(f), WithEarlierFormat(StockBattle(), true, directFlags));
                File.WriteAllBytes(CodePath(f), WithEarlierFormat(StockCode(), false, directFlags));
                check(LevelCapPatch.TryReadInstalledBattleTable(File.ReadAllBytes(BattlePath(f)), out var old) && old.Entries.Count == 2 &&
                    Is(Caps(f), "legacy-format", "applied-legacy/applied-legacy") && Caps(f)["detail"] is not null, "DgdCaps_EarlierFormat" + format + "NoCheckpoints_" + suffix);
                File.WriteAllBytes(CodePath(f), direct.CodeBin);
                check(Is(Caps(f), "legacy-format", "applied-legacy/applied"), "DgdCaps_EarlierFormat" + format + "MixedWithCurrent_" + suffix);
                File.WriteAllBytes(BattlePath(f), StockBattle()); File.WriteAllBytes(CodePath(f), WithEarlierFormat(StockCode(), false, directFlags));
                check(Is(Caps(f), "legacy-format", "stock/applied-legacy"), "DgdCaps_EarlierFormat" + format + "OnlyInCode_" + suffix);
            }

            // Unusable or foreign files: reported, with the rest of the document intact.
            File.WriteAllBytes(BattlePath(f), direct.BattleCro); File.WriteAllBytes(CodePath(f), direct.CodeBin);
            byte[] foreign = (byte[])direct.BattleCro.Clone(); Set32(foreign, BattleHook + 4, 0xE1A00000);
            File.WriteAllBytes(BattlePath(f), foreign);
            check(Is(Caps(f), "unsupported", "unrecognized/applied") && Caps(f)["source"] is not null, "DgdCaps_ForeignHookUnsupported_" + suffix);
            File.WriteAllBytes(BattlePath(f), direct.BattleCro);
            byte[] compressed = direct.CodeBin.AsSpan(0, direct.CodeBin.Length - 7).ToArray();
            File.WriteAllBytes(CodePath(f), compressed);
            check(Is(Caps(f), "unsupported", "applied/unrecognized"), "DgdCaps_CompressedCodeUnsupported_" + suffix);
            byte[] noHeader = (byte[])direct.BattleCro.Clone(); Set32(noHeader, 0x80, 0);
            File.WriteAllBytes(BattlePath(f), noHeader); File.WriteAllBytes(CodePath(f), direct.CodeBin);
            check(Is(Caps(f), "unsupported", "unrecognized/applied"), "DgdCaps_NotACroUnsupported_" + suffix);
            // Current routine with a table the runtime validation rejects (a cap that goes backwards).
            byte[] badTable = (byte[])direct.CodeBin.Clone();
            LevelCapPatch.GetCandyState(badTable, out uint blockAt);
            badTable[blockAt + LevelCapPatch.PrologueSize + LevelCapTable.EntrySize + 1] = 3;
            File.WriteAllBytes(BattlePath(f), direct.BattleCro); File.WriteAllBytes(CodePath(f), badTable);
            check(!LevelCapPatch.TryReadInstalledCandyTable(badTable, out _) && Is(Caps(f), "unsupported", "applied/unrecognized"),
                "DgdCaps_InvalidInstalledTableUnsupported_" + suffix);
            File.WriteAllBytes(CodePath(f), direct.CodeBin);
            using (new FileStream(BattlePath(f), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var locked = DefinitiveGameData.Build(f.Session);
                check(Is(locked["playerLevelCaps"]!.AsObject(), "unsupported", "unrecognized/applied") && locked["playerLevelCaps"]!["source"] is null &&
                    Root(locked) == rootDocument, "DgdCaps_UnreadableFileKeepsExport_" + suffix);
            }

            // Export: sources untouched, no local paths, block within the contract size.
            var before = Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => Sha(File.ReadAllBytes(p)));
            string output = Path.Combine(Path.GetTempPath(), "dgd-caps-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                DefinitiveGameData.Export(f.Session, output);
                string text = File.ReadAllText(output);
                var exported = JsonNode.Parse(text)!["playerLevelCaps"]!.AsObject();
                check(Is(exported, "installed", "applied/applied") && Matches(exported, edited) && !text.Contains(f.Root) && !text.Contains(Path.GetTempPath().TrimEnd('\\')) &&
                    before.All(p => Sha(File.ReadAllBytes(p.Key)) == p.Value), "DgdCaps_ExportPreservesSourcesAndPrivacy_" + suffix);
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }
        var largest = new LevelCapTable { Entries = Enumerable.Range(0, LevelCapTable.MaxEntries)
            .Select(i => new LevelCapEntry("x", LevelCapConditionKind.EventWorkAtLeast, (ushort)i, 65535, 100)).ToList() };
        check(largest.Validate().Count == 0 && Encoding.UTF8.GetByteCount(DefinitivePlayerLevelCaps.Checkpoints(largest).ToJsonString()) + 1024 < DefinitivePlayerLevelCaps.MaxBlockBytes,
            "DgdCaps_LargestTableFitsBlockLimit");
        check(LevelCapPatch.InspectBattle(StockBattle(), out var none) == LevelCapCopyState.Stock && none is null &&
            LevelCapPatch.InspectCandy(StockCode(), out none) == LevelCapCopyState.Stock && none is null &&
            LevelCapPatch.InspectCandy(new byte[16], out _) == LevelCapCopyState.Unrecognized, "DgdCaps_ReadOnlyInspectionStates");
        byte[] probeBattle = LevelCapPatch.InstallManaged(StockBattle(), StockCode(), LevelCapTable.Default()).BattleCro, probeCopy = (byte[])probeBattle.Clone();
        check(LevelCapPatch.InspectBattle(probeBattle, out var read) == LevelCapCopyState.Applied && probeBattle.AsSpan().SequenceEqual(probeCopy) &&
            read.ToBytes().AsSpan().SequenceEqual(LevelCapTable.Default().ToBytes()), "DgdCaps_InspectionDoesNotMutateInput");
    }

    /// <summary>One document per status, written by the real exporter, for the cross-language contract check.</summary>
    internal static void WriteFixtures(string folder)
    {
        using var state = new TestState();
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("La carpeta de salida debe estar vacía.");
        Directory.CreateDirectory(folder);
        using var f = DefinitiveGameDataChecks.Create();
        void Export(string name) => DefinitiveGameData.Export(f.Session, Path.Combine(folder, name + ".json"));
        Export("unsupported");
        WriteStock(f); Export("not-detected");
        var installed = LevelCapPatch.InstallManaged(StockBattle(), StockCode(), LevelCapTable.Default());
        File.WriteAllBytes(BattlePath(f), installed.BattleCro); Export("inconsistent");
        File.WriteAllBytes(CodePath(f), WithEarlierFormat(StockCode(), false, false)); Export("legacy-format");
        File.WriteAllBytes(CodePath(f), StockCode()); File.WriteAllBytes(BattlePath(f), StockBattle());
        Install(f, LevelCapTable.Default()); Export("installed");
        Install(f, Custom()); Export("installed-custom");
    }
}
