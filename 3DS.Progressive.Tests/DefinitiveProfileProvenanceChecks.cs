using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class DefinitiveProfileProvenanceChecks
{
    private static JsonObject? Block(Fixture f) => DefinitiveGameData.Build(f.Session)["profileProvenance"] as JsonObject;
    private static void Prepare(Fixture f, bool moon = false)
    {
        GlobalApplicationChecks.PrepareOpenable(f);
        string path = Path.Combine(f.Root, "ExHeader.bin");
        byte[] header = File.ReadAllBytes(path);
        BitConverter.GetBytes(moon ? 0x00040000001B5100UL : 0x00040000001B5000UL).CopyTo(header, 0x200);
        File.WriteAllBytes(path, header);
    }
    private static string HistoryPath(Fixture f) => (string)typeof(ProjectHistory).GetField("_path", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Session.History)!;
    private static GlobalTemplateDocument Recipe(Fixture f, string[] selected)
    {
        var doc = GlobalTemplateDocument.Empty("USUM", 7);
        NativeMasterBatchBridge.Capture(doc, new MasterBatchState(BatchAction.PersonalRandomize, new() { Abilities = true }, 0), 7);
        NativeMasterBatchBridge.Capture(doc, new MasterBatchState(BatchAction.MoveRandomize, new() { MoveTypes = true }, 0), 7);
        doc.SetSection("Actions", new JsonArray(new JsonObject { ["Id"] = "personal.randomize", ["Parameters"] = new JsonObject() },
            new JsonObject { ["Id"] = "rng.seed", ["Parameters"] = new JsonObject { ["Seed"] = "1234" } }));
        var profile = ShareableProfile.Create(f.Session, doc, "Perfil aplicado", "Autora declarada", "");
        return profile.Select(profile.Evaluate(f.Session), selected);
    }
    private static void Edit(Fixture f) { f.Session.RefreshPersonalAfterExternalEdit(); f.Session.SavePersonal(1, f.Session.ReadPersonal(1) with { HP = (byte)(f.Session.ReadPersonal(1).HP == 123 ? 124 : 123) }); }

    internal static async Task Run(Action<bool, string> check)
    {
        using var state = new TestState();
        foreach (bool moon in new[] { false, true })
        {
            using var f = DefinitiveGameDataChecks.Create(moon);
            Prepare(f, moon);
            string suffix = moon ? "_UM" : "_US";
            void Check(bool ok, string name) => check(ok, "DgdProfile_" + name + suffix);
            Check(Block(f) is null, "NoProfileOrOldProjectOmitsBlock");
            string legacyPath = HistoryPath(f);
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            string legacy = System.Text.Json.JsonSerializer.Serialize(new HistoryDocument(1, "USUM", "old", []));
            File.WriteAllText(legacyPath, legacy);
            Check(Block(f) is null && File.ReadAllText(legacyPath) == legacy, "LegacyVersionOneJournalUnchanged");
            File.Delete(legacyPath);
            var recipe = Recipe(f, ["actions/personal"]);
            Check(Block(f) is null, "ImportingOrSelectingWithoutApplyingOmitsBlock");
            var application = new GlobalTemplateApplication(f.Session);
            using var preview = await application.PreviewAsync(recipe);
            Check(Block(f) is null && preview.Files.Count > 0, "PreviewWithoutApplyingOmitsBlock");
            application.Apply(preview);
            var block = Block(f)!;
            Check(block["gameId"]!.GetValue<string>() == (moon ? "ultra-moon" : "ultra-sun") &&
                block["status"]!.GetValue<string>() == "historical" && block["currentMatch"]!.GetValue<string>() == "not-verified" &&
                block["subsequentChanges"]!.GetValue<string>() == "none-recorded" && block["profile"]!["name"]!.GetValue<string>() == "Perfil aplicado" &&
                block["profile"]!["author"]!.GetValue<string>() == "Autora declarada", "ActualExecutorCommitExportsHistoricalDeclaration");
            Check(block["sections"]!.AsArray().Select(s => s!.GetValue<string>()).SequenceEqual(["actions/personal"]), "OnlyExecutedSelectedSectionsIncluded");
            var files = GlobalTemplateApplication.Snapshot(f.Session);
            byte[] history = File.ReadAllBytes(HistoryPath(f));
            string first = DefinitiveGameData.Build(f.Session).ToJsonString();
            Check(first == DefinitiveGameData.Build(f.Session).ToJsonString() &&
                GlobalTemplateApplication.Snapshot(f.Session).All(p => files[p.Key] == p.Value) && File.ReadAllBytes(HistoryPath(f)).SequenceEqual(history),
                "RepeatedExportsStableAndNeverWriteGameOrHistory");
            Check(!first.Contains(f.Root, StringComparison.OrdinalIgnoreCase) && !block.ToJsonString().Contains("GlobalTemplateSnapshot"), "NoPathsOrRecipeLeak");
            File.WriteAllBytes(Path.Combine(f.Game.RomFS, "preview-fixture.bin"), [1, 2, 3]);
            Check(Block(f)!["currentMatch"]!.GetValue<string>() == "not-verified" && Block(f)!["subsequentChanges"]!.GetValue<string>() == "none-recorded",
                "ExternalChangesNeverBecomeAnExactMatchClaim");
            var applicationId = f.Session.History.Read().Entries.Last(e => e.Parameters.ContainsKey("GlobalTemplateSnapshot")).Id;
            Edit(f);
            Check(Block(f)!["subsequentChanges"]!.GetValue<string>() == "recorded" && Block(f)!["currentMatch"]!.GetValue<string>() == "not-verified", "LaterEditIsHistoricalNotExact");
            var editId = f.Session.History.Read().Entries.Last().Id;
            RestorePoints.Undo(f.Session.History, f.Root, editId);
            RestorePoints.Undo(f.Session.History, f.Root, applicationId);
            Check(Block(f) is null, "UndoneApplicationNotAttributed");

            // Incomplete/corrupt journal is isolated from otherwise valid DGD. Reading never repairs it.
            byte[] saved = File.ReadAllBytes(HistoryPath(f));
            File.WriteAllText(HistoryPath(f), "{broken");
            var corrupt = DefinitiveGameData.Build(f.Session);
            Check(!corrupt.ContainsKey("profileProvenance") && corrupt["species"]!.AsArray().Count > 0 && File.ReadAllText(HistoryPath(f)) == "{broken", "CorruptHistoryIgnoredWithoutRecoveryWrites");
            File.WriteAllBytes(HistoryPath(f), saved);
        }
        using var other = DefinitiveGameDataChecks.Create();
        Prepare(other);
        var both = Recipe(other, ["actions/personal", "actions/rng"]);
        var executor = new GlobalTemplateApplication(other.Session);
        using (var p = await executor.PreviewAsync(both)) executor.Apply(p);
        check(Block(other)!["sections"]!.AsArray().Count == 2, "DgdProfile_MultipleAppliedSections");
        string path = HistoryPath(other), good = File.ReadAllText(path);
        void Mutate(Action<JsonObject> mutate)
        {
            var root = JsonNode.Parse(good)!.AsObject();
            var entry = root["Entries"]!.AsArray().First(e => e!["Parameters"]?["GlobalTemplateSnapshot"] is not null)!;
            var snap = GlobalTemplateDocument.Parse(entry["Parameters"]!["GlobalTemplateSnapshot"]!.GetValue<string>(), "USUM", 7);
            var app = snap.Section("ProgressiveApp")!.AsObject(); mutate(app); snap.SetSection("ProgressiveApp", app);
            entry["Parameters"]!["GlobalTemplateSnapshot"] = snap.ToJson(); entry["Parameters"]!["TemplateSha256"] = snap.Sha256;
            File.WriteAllText(path, root.ToJsonString());
        }
        Mutate(app => app["Profile"]!["Sections"] = new JsonArray("actions/personal", "actions/missing"));
        check(Block(other)!["sections"]!.AsArray().Select(s => s!.GetValue<string>()).SequenceEqual(["actions/personal"]),
            "DgdProfile_EditedSelectionOnlyClaimsPresentSections");
        Mutate(app => app["ManualEdits"] = new JsonObject { ["Operations"] = new JsonArray(new JsonObject()) });
        check(Block(other) is null, "DgdProfile_MalformedNestedRecipeIgnored" );
        Mutate(app => app["Profile"]!["Name"] = "C:\\private");
        check(Block(other) is null, "DgdProfile_PrivatePathRejectedInMetadata");
        Mutate(app => app["Profile"]!["Version"] = 2);
        check(Block(other) is null, "DgdProfile_FutureProfileIgnored");
        Mutate(app => app["Profile"]!["Version"] = long.MaxValue);
        check(Block(other) is null, "DgdProfile_OversizedNumericVersionIgnored");
        Mutate(app => app["Profile"]!["Version"] = true);
        check(Block(other) is null, "DgdProfile_BooleanVersionIgnored");
        Mutate(app => app["Profile"]!.AsObject().Remove("RecipeSha256"));
        check(Block(other) is null, "DgdProfile_IncompleteMetadataIgnored");
        Mutate(app => app["Profile"]!["Sections"] = new JsonArray("actions/missing"));
        check(Block(other) is null, "DgdProfile_UnexecutedSectionsNeverClaimed");
        Mutate(app => app["Profile"]!["Author"] = "");
        check(Block(other)!["profile"]!["author"] is null, "DgdProfile_EmptyAuthorOmitted");
        var badHash = JsonNode.Parse(good)!.AsObject();
        badHash["Entries"]!.AsArray().First(e => e!["Parameters"]?["GlobalTemplateSnapshot"] is not null)!["Parameters"]!["TemplateSha256"] = new string('0', 64);
        File.WriteAllText(path, badHash.ToJsonString());
        check(Block(other) is null, "DgdProfile_SnapshotHashMismatchIgnored");
        File.WriteAllText(path, good);
        PlayerLevelCapRecipeChecks.WriteStock(other); PlayerLevelCapRecipeChecks.Install(other, PlayerLevelCapRecipeChecks.Table());
        var withCaps = DefinitiveGameData.Build(other.Session);
        check(withCaps["profileProvenance"] is not null && withCaps["playerLevelCaps"]!["status"]!.GetValue<string>() == "installed", "DgdProfile_CoexistsWithPlayerLevelCaps");
        // A newer committed recipe without profile metadata must clear older attribution.
        var noProfile = GlobalTemplateDocument.Empty("USUM", 7);
        string historyHash = ProjectHistory.Hash(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(other.Session.History.Read()));
        other.Session.History.CommitGlobalTemplate([], noProfile, 0, historyHash);
        check(Block(other) is null, "DgdProfile_LatestRecipeWithoutProfileDoesNotResurrectOlderOne");
    }

    internal static async Task WriteFixtures(string folder)
    {
        string full = Path.GetFullPath(folder);
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()) throw new IOException("La carpeta de fixtures debe ser nueva o vacía.");
        Directory.CreateDirectory(full);
        using var state = new TestState();
        using var f = DefinitiveGameDataChecks.Create(); Prepare(f);
        DefinitiveGameData.Export(f.Session, Path.Combine(full, "absent.json"));
        var executor = new GlobalTemplateApplication(f.Session);
        using (var preview = await executor.PreviewAsync(Recipe(f, ["actions/personal"])))
        {
            DefinitiveGameData.Export(f.Session, Path.Combine(full, "preview-only.json")); executor.Apply(preview);
        }
        DefinitiveGameData.Export(f.Session, Path.Combine(full, "applied.json"));
        var application = f.Session.History.Read().Entries.First(e => e.Parameters.ContainsKey("GlobalTemplateSnapshot")).Id;
        Edit(f); DefinitiveGameData.Export(f.Session, Path.Combine(full, "modified.json"));
        RestorePoints.Undo(f.Session.History, f.Root, f.Session.History.Read().Entries.Last().Id);
        RestorePoints.Undo(f.Session.History, f.Root, application);
        DefinitiveGameData.Export(f.Session, Path.Combine(full, "undone.json"));
    }
}
