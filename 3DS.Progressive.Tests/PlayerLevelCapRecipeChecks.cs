using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;

/// <summary>
/// A Global Template recipe that carries a manual Player Level Caps installation: what is recorded, reading it back,
/// replaying it on another project, undoing it and refusing what is not understood. Every binary is synthetic
/// (the CRO skeleton and zero-filled code image of <see cref="DefinitiveGameDataCapsChecks"/>).
/// </summary>
internal static class PlayerLevelCapRecipeChecks
{
    // The payload exactly as the application records it (and as earlier builds did): numbers for the condition kind.
    private const string Recorded =
        "{\"Table\":{\"Entries\":[" +
        "{\"Label\":\"Clear Normal Trial - Ilima (1dom)\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":15}," +
        "{\"Label\":\"punto propio\",\"Kind\":0,\"FlagOffset\":336,\"FlagBit\":4,\"Cap\":50}," +
        "{\"Label\":\"\",\"Kind\":1,\"FlagOffset\":68,\"FlagBit\":2000,\"Cap\":80}]}}";
    internal static LevelCapTable Table() => new()
    {
        Entries =
        [
            new("Clear Normal Trial - Ilima (1dom)", 0x0010, 0x0008, 15),
            new("punto propio", 0x0150, 0x0004, 50),
            new("", LevelCapConditionKind.EventWorkAtLeast, 0x0044, 2000, 80),
        ],
    };
    internal static string BattlePath(Fixture f) => Path.Combine(f.Game.RomFS, "Battle.cro");
    internal static string CodePath(Fixture f) => Path.Combine(f.Game.ExeFS, "code.bin");
    internal static void WriteStock(Fixture f)
    { File.WriteAllBytes(BattlePath(f), DefinitiveGameDataCapsChecks.StockBattle()); File.WriteAllBytes(CodePath(f), DefinitiveGameDataCapsChecks.StockCode()); }
    internal static void Install(Fixture f, LevelCapTable table) { var service = new GamePlayerLevelCapService(f.Session); service.Apply(service.Preview(table)); }
    /// <summary>Every file of the project folder with its hash. History and restore points live outside it in these checks.</summary>
    internal static Dictionary<string, string> Files(Fixture f) => Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(f.Root, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))), StringComparer.OrdinalIgnoreCase);
    internal static string[] Changed(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).Where(k => before.GetValueOrDefault(k) != after.GetValueOrDefault(k))
            .Select(k => k.Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
    internal static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => Changed(a, b).Length == 0;
    private static JsonObject? Operation(GlobalTemplateDocument doc) =>
        ManualOperations.Read(doc).SingleOrDefault(o => o["Kind"]!.GetValue<string>() == "playercaps");
    internal static bool Installed(Fixture f, LevelCapTable table) =>
        LevelCapPatch.InspectBattle(File.ReadAllBytes(BattlePath(f)), out var battle) == LevelCapCopyState.Applied &&
        LevelCapPatch.InspectCandy(File.ReadAllBytes(CodePath(f)), out var candy) == LevelCapCopyState.Applied &&
        battle.ToBytes().SequenceEqual(table.ToBytes()) && candy.ToBytes().SequenceEqual(table.ToBytes());
    internal static bool IsStock(Fixture f) =>
        File.ReadAllBytes(BattlePath(f)).SequenceEqual(DefinitiveGameDataCapsChecks.StockBattle()) &&
        File.ReadAllBytes(CodePath(f)).SequenceEqual(DefinitiveGameDataCapsChecks.StockCode());
    /// <summary>The recipe with the payload of its playercaps operation replaced.</summary>
    private static GlobalTemplateDocument With(GlobalTemplateDocument recipe, string payload)
    {
        var copy = recipe.Clone();
        var app = (JsonObject)copy.Section("ProgressiveApp")!;
        var operation = app["ManualEdits"]!["Operations"]!.AsArray().Single(o => o!["Kind"]!.GetValue<string>() == "playercaps")!;
        operation["Payload"] = JsonNode.Parse(payload);
        copy.SetSection("ProgressiveApp", app);
        return copy;
    }
    private static string Entry(string fields) => "{\"Table\":{\"Entries\":[{" + fields + "}]}}";

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        using var state = new TestState();
        foreach (bool moon in new[] { false, true })
        {
            string suffix = moon ? "Moon" : "Sun";
            using var origin = DefinitiveGameDataChecks.Create(moon);
            using var target = DefinitiveGameDataChecks.Create(moon);
            string game = origin.Session.GameName; int generation = origin.Session.Generation;
            var table = Table();
            WriteStock(origin); WriteStock(target);
            Install(origin, table);

            // Serialization: what the installation records, and the recipe taken from that history.
            var recipe = GlobalTemplateDocument.FromHistory(origin.Session.History.Read(), game, generation, null);
            var operation = Operation(recipe);
            check(operation?["Payload"]?.ToJsonString() == Recorded && operation["Key"]!.GetValue<string>().StartsWith("playercaps", StringComparison.Ordinal),
                "CapsRecipe_RecordedPayloadKeepsItsFormat_" + suffix);

            // Deserialization: the recorded payload gives back the same table, names included.
            var read = PlayerLevelCapRecipe.Read(operation!["Payload"]!.AsObject());
            check(read.ToBytes().SequenceEqual(table.ToBytes()) && read.Entries.SequenceEqual(table.Entries), "CapsRecipe_PayloadReadsBackTheSameTable_" + suffix);
            var literal = PlayerLevelCapRecipe.Read(JsonNode.Parse(Recorded)!.AsObject());
            check(literal.Entries.SequenceEqual(table.Entries), "CapsRecipe_PayloadOfEarlierBuildsIsRead_" + suffix);
            check(PlayerLevelCapRecipe.Read(JsonNode.Parse(
                    "{\"Table\":{\"Entries\":[{\"Label\":null,\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":15,\"Nota\":\"x\"}],\"Otro\":1},\"Extra\":true}")!.AsObject())
                .Entries.Single() == new LevelCapEntry("", 0x0010, 0x0008, 15), "CapsRecipe_UnknownNodesAndMissingNameTolerated_" + suffix);

            // Reopening: the recipe written to a file and loaded again is the same recipe and still validates.
            string folder = Path.Combine(Path.GetTempPath(), "caps-recipe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string file = Path.Combine(folder, "receta.json");
                recipe.Save(file);
                var loaded = GlobalTemplateDocument.Load(file, game, generation);
                ManualOperations.Validate(loaded);
                check(Operation(loaded)?["Payload"]?.ToJsonString() == Recorded && loaded.ToJson() == recipe.ToJson(), "CapsRecipe_SavedAndReloadedRecipeIsUnchanged_" + suffix);

                // Validation only: nothing is written.
                var before = Files(target); int entries = target.Session.History.Read().Entries.Count;
                ManualEditTemplate.Apply(target.Session, loaded, validateOnly: true);
                check(Same(before, Files(target)) && target.Session.History.Read().Entries.Count == entries && IsStock(target),
                    "CapsRecipe_ValidationWritesNothing_" + suffix);

                // Replay: the two files of the patch change, and nothing else of the project does.
                ManualEditTemplate.Apply(target.Session, loaded);
                var after = Files(target);
                check(Changed(before, after).SequenceEqual(["ExeFS/code.bin", "RomFS/Battle.cro"]), "CapsRecipe_ReplayChangesOnlyThePatchFiles_" + suffix);
                check(Installed(target, table) &&
                    File.ReadAllBytes(BattlePath(target)).SequenceEqual(File.ReadAllBytes(BattlePath(origin))) &&
                    File.ReadAllBytes(CodePath(target)).SequenceEqual(File.ReadAllBytes(CodePath(origin))), "CapsRecipe_ReplayInstallsTheSameBytesAsTheOrigin_" + suffix);
                var exported = DefinitiveGameData.Build(target.Session)["playerLevelCaps"]!.AsObject();
                check(exported["status"]!.GetValue<string>() == "installed" &&
                    exported["source"]!["tableSha256"]!.GetValue<string>() == Convert.ToHexString(SHA256.HashData(table.ToBytes())),
                    "CapsRecipe_ReplayedInstallIsWhatTheExportReports_" + suffix);

                // Reopening the project: a new reading of the history and of the saved files gives the same recipe.
                var reopened = new ProjectHistory(target.Root, game).Read();
                var again = GlobalTemplateDocument.FromHistory(reopened, game, generation, null);
                check(Operation(again)?["Payload"]?.ToJsonString() == Recorded && new GamePlayerLevelCapService(target.Session) is not null && Installed(target, table),
                    "CapsRecipe_ReplayedProjectGivesTheSameRecipeAgain_" + suffix);

                // Undo: every file is back to what it was, and the recipe no longer carries the installation.
                var replayed = reopened.Entries.Last(e => e.Parameters.GetValueOrDefault("ManualOperation")?.Contains("playercaps") == true);
                RestorePoints.Undo(target.Session.History, target.Root, replayed.Id);
                check(Same(before, Files(target)) && IsStock(target), "CapsRecipe_UndoRestoresEveryFile_" + suffix);
                check(Operation(GlobalTemplateDocument.FromHistory(target.Session.History.Read(), game, generation, null)) is null,
                    "CapsRecipe_UndoneReplayLeavesTheRecipe_" + suffix);

                // Replay over another installed table replaces it; undoing gives that table back.
                Install(target, LevelCapTable.Default());
                var withDefault = Files(target);
                ManualEditTemplate.Apply(target.Session, loaded);
                check(Installed(target, table) && Changed(withDefault, Files(target)).SequenceEqual(["ExeFS/code.bin", "RomFS/Battle.cro"]),
                    "CapsRecipe_ReplayReplacesAnInstalledTable_" + suffix);
                replayed = target.Session.History.Read().Entries.Last(e => e.Parameters.GetValueOrDefault("ManualOperation")?.Contains("playercaps") == true);
                RestorePoints.Undo(target.Session.History, target.Root, replayed.Id);
                check(Same(withDefault, Files(target)) && Installed(target, LevelCapTable.Default()), "CapsRecipe_UndoGivesBackThePreviousTable_" + suffix);

                // Recipes that are not understood are refused when they are loaded and when they are applied, and write nothing.
                WriteStock(target);
                before = Files(target); entries = target.Session.History.Read().Entries.Count;
                string many = "{\"Table\":{\"Entries\":[" + string.Join(",", Enumerable.Repeat(
                    "{\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":15}", LevelCapTable.MaxEntries + 1)) + "]}}";
                (string Name, string Payload)[] invalid =
                [
                    ("NoTable", "{}"),
                    ("TableNotAnObject", "{\"Table\":[]}"),
                    ("EntriesNotAList", "{\"Table\":{\"Entries\":{}}}"),
                    ("NoPoints", "{\"Table\":{\"Entries\":[]}}"),
                    ("TooManyPoints", many),
                    ("PointNotAnObject", "{\"Table\":{\"Entries\":[7]}}"),
                    ("NullPoint", "{\"Table\":{\"Entries\":[null]}}"),
                    ("UnknownKind", Entry("\"Label\":\"x\",\"Kind\":2,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":15")),
                    ("KindAsText", Entry("\"Label\":\"x\",\"Kind\":\"0\",\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":15")),
                    ("KindMissing", Entry("\"Label\":\"x\",\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":15")),
                    ("NameNotText", Entry("\"Label\":5,\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":15")),
                    ("CapZero", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":0")),
                    ("CapAboveCeiling", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":101")),
                    ("CapBeyondAByte", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":271")),
                    ("CapFraction", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":15.5")),
                    ("CapExponent", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":1e1")),
                    ("CapNegative", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8,\"Cap\":-15")),
                    ("CapMissing", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":8")),
                    ("OffsetBeyondAWord", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":65552,\"FlagBit\":8,\"Cap\":15")),
                    ("OffsetOutsideTheFlags", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":65535,\"FlagBit\":8,\"Cap\":15")),
                    ("MaskZero", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":0,\"Cap\":15")),
                    ("MaskBeyondAByte", Entry("\"Label\":\"x\",\"Kind\":0,\"FlagOffset\":16,\"FlagBit\":256,\"Cap\":15")),
                    ("WorkOutsideTheArray", Entry("\"Label\":\"x\",\"Kind\":1,\"FlagOffset\":65535,\"FlagBit\":1,\"Cap\":15")),
                ];
                foreach (var (name, payload) in invalid)
                {
                    var broken = With(loaded, payload);
                    reject(() => ManualOperations.Validate(broken), "CapsRecipe_Invalid_" + name + "_RefusedOnLoad_" + suffix);
                    reject(() => ManualEditTemplate.Apply(target.Session, broken), "CapsRecipe_Invalid_" + name + "_RefusedOnApply_" + suffix);
                }
                check(Same(before, Files(target)) && target.Session.History.Read().Entries.Count == entries && IsStock(target),
                    "CapsRecipe_InvalidRecipesWriteNothing_" + suffix);

                // A project whose hook sites are not the original ones: the valid recipe is refused and nothing is written.
                byte[] foreign = DefinitiveGameDataCapsChecks.StockBattle();
                foreign[(int)LevelCapPatch.BattleHook] ^= 0x5A;
                File.WriteAllBytes(BattlePath(target), foreign);
                before = Files(target);
                reject(() => ManualEditTemplate.Apply(target.Session, loaded), "CapsRecipe_IncompatibleProjectRefused_" + suffix);
                check(Same(before, Files(target)) && target.Session.History.Read().Entries.Count == entries, "CapsRecipe_IncompatibleProjectUntouched_" + suffix);

                // What is left in the history is the earlier installation (the default table): the undone replay is not in the recipe.
                var remaining = GlobalTemplateDocument.FromHistory(target.Session.History.Read(), game, generation, null);
                ManualOperations.Validate(remaining);
                check(PlayerLevelCapRecipe.Read(Operation(remaining)!["Payload"]!.AsObject()).Entries.SequenceEqual(LevelCapTable.Default().Entries),
                    "CapsRecipe_RecipeAfterUndoCarriesThePreviousTable_" + suffix);

                // A recipe without the operation validates as before.
                using var untouched = DefinitiveGameDataChecks.Create(moon);
                var plain = GlobalTemplateDocument.FromHistory(untouched.Session.History.Read(), game, generation, null);
                ManualOperations.Validate(plain);
                check(Operation(plain) is null, "CapsRecipe_RecipeWithoutTheOperationStillValidates_" + suffix);
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
