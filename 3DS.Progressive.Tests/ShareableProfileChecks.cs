using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using pk3DS.Core;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Caps = PlayerLevelCapRecipeChecks;

/// <summary>
/// Shareable profiles, format 1 (Ultra Sun / Ultra Moon): export, strict reading, compatibility with the open project,
/// conflicts, choosing sections, applying through the existing recipe replay, history and undo. Synthetic binaries only.
/// </summary>
internal static class ShareableProfileChecks
{
    private static string Sha(JsonNode node) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));
    /// <summary>A recipe as «Guardar template global» would give it: the caps installation of the history plus two actions.</summary>
    private static GlobalTemplateDocument Recipe(Fixture origin, bool withUnshareable = false, bool mints = false)
    {
        var recipe = GlobalTemplateDocument.FromHistory(origin.Session.History.Read(), "USUM", 7, null);
        var actions = recipe.Section("Actions")!.AsArray();
        actions.Insert(0, new JsonObject { ["Id"] = "rng.seed", ["Parameters"] = new JsonObject { ["Seed"] = 1234 } });
        actions.Insert(1, new JsonObject { ["Id"] = "trainers.randomize", ["Parameters"] = new JsonObject { ["Level"] = 10 } });
        recipe.SetSection("Actions", actions);
        if (withUnshareable)
        {
            var app = (JsonObject)recipe.Section("ProgressiveApp")!;
            app["ManualEdits"]!["Operations"]!.AsArray().Add(new JsonObject
            {
                ["Kind"] = "resources", ["Key"] = "resource:RomFS/x:file:",
                ["Payload"] = new JsonObject { ["Files"] = new JsonArray(new JsonObject { ["Path"] = "RomFS/x", ["Mode"] = "file", ["BeforeHash"] = "00",
                    ["Asset"] = new JsonObject { ["Name"] = "00.bin", ["Sha256"] = "00", ["Directory"] = Path.Combine(origin.Root, "manual-assets") } }) },
            });
            app["ManualEdits"]!["Entries"]!.AsArray().Add(new JsonObject { ["Kind"] = "gametext", ["Entry"] = 1, ["Field"] = "Línea 0", ["Before"] = "Texto del juego", ["After"] = "Otro", ["Language"] = 2 });
            app["ManualEdits"]!["Entries"]!.AsArray().Add(new JsonObject { ["Kind"] = "personal", ["Entry"] = 1, ["Field"] = "HP", ["Before"] = "60", ["After"] = "61", ["Language"] = -1 });
            recipe.SetSection("ProgressiveApp", app);
        }
        if (mints) UsumMintItemPolicy.Capture(recipe, true);
        return recipe;
    }
    /// <summary>The profile text with an edit made to its JSON; the recipe hash is recomputed unless the edit is meant to break it.</summary>
    private static string Edited(ShareableProfile profile, Action<JsonObject> edit, bool rehash = true)
    {
        var root = JsonNode.Parse(profile.ToJson())!.AsObject();
        edit(root);
        if (rehash && root["recipe"] is JsonObject recipe) root["recipeSha256"] = Sha(recipe);
        return root.ToJsonString();
    }
    private static string[] Ids(ShareableProfile profile) => profile.Sections.Select(s => s.Id).ToArray();
    private static ProfileSectionReport Report(ProfileCompatibility c, string id) => c.Sections.Single(r => r.Section.Id == id);

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        using var state = new TestState();
        using var origin = DefinitiveGameDataChecks.Create();
        using var target = DefinitiveGameDataChecks.Create(moon: true);
        Caps.WriteStock(origin); Caps.WriteStock(target);
        var table = Caps.Table();
        Caps.Install(origin, table);
        var originFiles = Caps.Files(origin);

        // ---- export ----
        var profile = ShareableProfile.Create(origin.Session, Recipe(origin, withUnshareable: true, mints: true), "  Mi perfil  ", "Autora", "Para probar");
        string json = profile.ToJson();
        var root = JsonNode.Parse(json)!.AsObject();
        check(root["format"]!.GetValue<string>() == "definitive-randomizer-profile" && root["version"]!.GetValue<int>() == 1 && profile.Name == "Mi perfil" &&
            root["target"]!.ToJsonString() == "{\"platform\":\"3ds\",\"game\":\"USUM\",\"generation\":7}" && root["recipeSha256"]!.GetValue<string>() == Sha(root["recipe"]!),
            "Profile_ExportHasIdentityVersionTargetAndHash");
        check(Ids(profile).SequenceEqual(["actions/rng", "actions/trainers", "manual/playercaps", "manual/personal-fields", "options/nature-mints"]),
            "Profile_SectionsDerivedFromTheRecipe");
        check(profile.Sections.Single(s => s.Id == "manual/playercaps").Summary == "3 puntos; límites de Nv. 15 a Nv. 80" &&
            profile.Sections.Single(s => s.Id == "manual/playercaps").Requires.SequenceEqual(["game:usum", "patch:player-level-caps"]), "Profile_CapsSectionSummaryAndRequirement");
        check(profile.Omitted.Count == 2 && profile.Omitted.Any(o => o.What == "resources" && o.Count == 1) && profile.Omitted.Any(o => o.What == "text" && o.Count == 1) &&
            !root["recipe"]!.ToJsonString().Contains("resources") && !json.Contains("Texto del juego") && !json.Contains("manual-assets"), "Profile_GameResourcesAndTextAreLeftOutAndReported");
        check(!json.Contains(origin.Root, StringComparison.OrdinalIgnoreCase) && !json.Contains(origin.Root.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase) &&
            !json.Contains(Path.GetTempPath().TrimEnd('\\').Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase), "Profile_ExportCarriesNoLocalPath");
        check(Caps.Same(originFiles, Caps.Files(origin)), "Profile_ExportWritesNothingToTheProject");
        var withPath = Recipe(origin);
        var pathActions = withPath.Section("Actions")!.AsArray(); pathActions[1]!["Parameters"]!["File"] = "C:\\Users\\alguien\\tabla.json"; withPath.SetSection("Actions", pathActions);
        reject(() => ShareableProfile.Create(origin.Session, withPath, "x", "", ""), "Profile_ExportRefusesALocalPath");
        var withRoot = Recipe(origin);
        var rootActions = withRoot.Section("Actions")!.AsArray(); rootActions[1]!["Parameters"]!["Nota"] = "ver " + origin.Root; withRoot.SetSection("Actions", rootActions);
        reject(() => ShareableProfile.Create(origin.Session, withRoot, "x", "", ""), "Profile_ExportRefusesTheProjectFolder");
        reject(() => ShareableProfile.Create(origin.Session, Recipe(origin), "   ", "", ""), "Profile_ExportNeedsAName");
        using (var empty = DefinitiveGameDataChecks.Create())
            reject(() => ShareableProfile.Create(empty.Session, GlobalTemplateDocument.Empty("USUM", 7), "x", "", ""), "Profile_NothingToShareIsRefused");
        foreach (var version in new[] { GameVersion.SM, GameVersion.ORAS, GameVersion.XY })
        {
            using var other = new Fixture(version);
            string game = ShareableProfile.GameOf(other.Session);
            reject(() => ShareableProfile.Create(other.Session, GlobalTemplateDocument.Empty(game, other.Session.Generation), "x", "", ""), "Profile_ExportOnlyForUsum_" + version);
        }

        // ---- file round trip ----
        string folder = Path.Combine(Path.GetTempPath(), "profile-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "perfil" + ShareableProfile.Extension);
            profile.Save(file);
            var loaded = ShareableProfile.Load(file);
            check(loaded.ToJson() == json && Ids(loaded).SequenceEqual(Ids(profile)) && loaded.Author == "Autora" && loaded.Description == "Para probar" &&
                Directory.GetFiles(folder).Length == 1, "Profile_SavedFileLoadsBackIdenticalAndAlone");
            File.WriteAllBytes(Path.Combine(folder, "grande.json"), new byte[ShareableProfile.MaxBytes + 1]);
            reject(() => ShareableProfile.Load(Path.Combine(folder, "grande.json")), "Profile_FileOverTheSizeLimitRefused");
            reject(() => ShareableProfile.Load(Path.Combine(folder, "no-existe.json")), "Profile_MissingFileRefused");
        }
        finally { Directory.Delete(folder, true); }

        // ---- strict reading ----
        (string Name, string Text)[] refused =
        [
            ("NotJson", "{ esto no es json"),
            ("Truncated", json[..(json.Length / 2)]),
            ("ArrayRoot", "[]"),
            ("OtherFormat", Edited(profile, r => r["format"] = "progressive-tracker-stage-list")),
            ("NoFormat", Edited(profile, r => r.Remove("format"))),
            ("FutureVersion", Edited(profile, r => r["version"] = 2)),
            ("VersionZero", Edited(profile, r => r["version"] = 0)),
            ("VersionAsText", Edited(profile, r => r["version"] = "1")),
            ("NoName", Edited(profile, r => r.Remove("name"))),
            ("NameNotText", Edited(profile, r => r["name"] = 5)),
            ("NameTooLong", Edited(profile, r => r["name"] = new string('a', 121))),
            ("NoTarget", Edited(profile, r => r.Remove("target"))),
            ("DsPlatform", Edited(profile, r => r["target"]!["platform"] = "nds")),
            ("SwitchPlatform", Edited(profile, r => r["target"]!["platform"] = "switch")),
            ("OtherGame", Edited(profile, r => { r["target"]!["game"] = "SM"; r["recipe"]!["Game"] = "SM"; })),
            ("OtherGeneration", Edited(profile, r => r["target"]!["generation"] = 6)),
            ("RecipeOfAnotherGame", Edited(profile, r => r["recipe"]!["Game"] = "SM")),
            ("NoRecipe", Edited(profile, r => r.Remove("recipe"))),
            ("NoHash", Edited(profile, r => r.Remove("recipeSha256"), rehash: false)),
            ("RecipeChangedAfterHashing", Edited(profile, r => r["recipe"]!["Actions"]![1]!["Parameters"]!["Level"] = 99, rehash: false)),
            ("RecipeNotATemplate", Edited(profile, r => r["recipe"]!.AsObject().Remove("Actions"))),
            ("SectionRemovedFromTheList", Edited(profile, r => r["sections"]!.AsArray().RemoveAt(2))),
            ("SectionAddedToTheList", Edited(profile, r => r["sections"]!.AsArray().Add(new JsonObject { ["id"] = "actions/moves" }))),
            ("SectionRepeated", Edited(profile, r => r["sections"]!.AsArray().Add(r["sections"]![0]!.DeepClone()))),
            ("SectionsNotAList", Edited(profile, r => r["sections"] = "todas")),
            ("ResourceOperationInside", Edited(profile, r => r["recipe"]!["ProgressiveApp"]!["ManualEdits"]!["Operations"]!.AsArray().Add(new JsonObject
                { ["Kind"] = "resources", ["Key"] = "resource:x", ["Payload"] = new JsonObject { ["Files"] = new JsonArray() } }))),
            ("GameTextInside", Edited(profile, r => r["recipe"]!["ProgressiveApp"]!["ManualEdits"]!["Entries"]!.AsArray().Add(new JsonObject
                { ["Kind"] = "gametext", ["Entry"] = 1, ["Field"] = "Línea 0", ["Before"] = "a", ["After"] = "b", ["Language"] = 2 }))),
            ("InvalidCapsTable", Edited(profile, r => r["recipe"]!["ProgressiveApp"]!["ManualEdits"]!["Operations"]![0]!["Payload"]!["Table"]!["Entries"]![0]!["Cap"] = 0)),
            ("OverTheSizeLimit", Edited(profile, r => r["description"] = new string('a', 10)) + new string(' ', ShareableProfile.MaxBytes)),
        ];
        foreach (var (name, text) in refused) reject(() => ShareableProfile.Parse(text), "Profile_Refused_" + name);
        try { ShareableProfile.Parse(Edited(profile, r => r["version"] = 2)); check(false, "Profile_FutureVersionAsksToUpdate"); }
        catch (InvalidDataException e) { check(e.Message.Contains("más reciente") && e.Message.Contains("Actualiza"), "Profile_FutureVersionAsksToUpdate"); }
        var tolerant = ShareableProfile.Parse(Edited(profile, r => { r["futuro"] = new JsonObject { ["x"] = 1 }; r.Remove("author"); r.Remove("description"); r.Remove("generator"); r.Remove("createdUtc"); r.Remove("omitted"); r.Remove("dependencies"); }));
        check(Ids(tolerant).SequenceEqual(Ids(profile)) && tolerant.Author == "" && tolerant.Omitted.Count == 0, "Profile_UnknownFieldsAndMissingOptionalFieldsTolerated");

        // ---- compatibility (read-only) ----
        var targetFiles = Caps.Files(target); int entries = target.Session.History.Read().Entries.Count;
        var fresh = profile.Evaluate(target.Session);
        check(fresh.SameGame && Report(fresh, "actions/rng").Status == ProfileSectionStatus.Compatible && Report(fresh, "actions/trainers").Status == ProfileSectionStatus.Compatible &&
            Report(fresh, "manual/playercaps").Status == ProfileSectionStatus.Compatible && Report(fresh, "manual/playercaps").Detail.Contains("se instalaría") &&
            Report(fresh, "manual/personal-fields").Status == ProfileSectionStatus.Compatible, "Profile_CompatibleSectionsOnAStockProject");
        check(Report(fresh, "options/nature-mints").Status == ProfileSectionStatus.Incompatible && !Report(fresh, "options/nature-mints").Selectable &&
            fresh.Verdict == "4 de 5 secciones se pueden usar en este proyecto.", "Profile_MintsNeedTheirPatchInstalled");
        var unknown = ShareableProfile.Parse(Edited(profile, r => r["sections"]![1]!["requires"]!.AsArray().Add("patch:algo-futuro"))).Evaluate(target.Session);
        check(Report(unknown, "actions/trainers").Status == ProfileSectionStatus.Unknown && !Report(unknown, "actions/trainers").Selectable, "Profile_UnknownRequirementBlocksItsSection");
        check(Caps.Same(targetFiles, Caps.Files(target)) && target.Session.History.Read().Entries.Count == entries, "Profile_EvaluatingWritesNothing");
        using (var sun = new Fixture(GameVersion.SM))
        {
            var foreign = profile.Evaluate(sun.Session);
            check(!foreign.SameGame && foreign.Sections.All(r => r.Status == ProfileSectionStatus.Incompatible) && foreign.Verdict.Contains("USUM") && foreign.Verdict.Contains("SM"),
                "Profile_OtherGameIsIncompatibleAsAWhole");
            reject(() => profile.Select(foreign, ["actions/trainers"]), "Profile_OtherGameCannotSelectAnything");
        }

        // ---- choosing sections ----
        reject(() => profile.Select(fresh, []), "Profile_SelectingNothingRefused");
        reject(() => profile.Select(fresh, ["options/nature-mints"]), "Profile_IncompatibleSectionCannotBeForced");
        reject(() => profile.Select(fresh, ["actions/no-existe"]), "Profile_UnknownSectionRefused");
        var onlyActions = profile.Select(fresh, ["actions/trainers"]);
        check(onlyActions.Actions.SequenceEqual(["trainers.randomize"]) && onlyActions.Section("ProgressiveApp")?["ManualEdits"] is null && !UsumMintItemPolicy.Read(onlyActions) &&
            onlyActions.Section("ProgressiveApp")!["Profile"]!["Name"]!.GetValue<string>() == "Mi perfil" &&
            onlyActions.Section("ProgressiveApp")!["Profile"]!["RecipeSha256"]!.GetValue<string>() == profile.RecipeSha256, "Profile_SelectingActionsLeavesTheRestOutAndKeepsProvenance");
        var onlyCaps = profile.Select(fresh, ["manual/playercaps"]);
        var kept = ManualOperations.Read(onlyCaps);
        check(onlyCaps.Actions.SequenceEqual([ManualEditTemplate.ActionId]) && kept.Length == 1 && kept[0]["Kind"]!.GetValue<string>() == "playercaps" &&
            onlyCaps.Section("ProgressiveApp")!["ManualEdits"]!["Entries"]!.AsArray().Count == 0, "Profile_SelectingCapsKeepsOnlyThatOperation");
        check(Caps.Same(targetFiles, Caps.Files(target)) && target.Session.History.Read().Entries.Count == entries, "Profile_ChoosingSectionsWritesNothing");

        // ---- apply through the existing replay, history and undo ----
        ManualEditTemplate.Apply(target.Session, onlyCaps, validateOnly: true);
        check(Caps.Same(targetFiles, Caps.Files(target)), "Profile_ValidationOnlyWritesNothing");
        ManualEditTemplate.Apply(target.Session, onlyCaps);
        check(Caps.Changed(targetFiles, Caps.Files(target)).SequenceEqual(["ExeFS/code.bin", "RomFS/Battle.cro"]) && Caps.Installed(target, table), "Profile_ApplyingCapsChangesOnlyThePatchFiles");
        var history = target.Session.History.Read();
        var applied = history.Entries.Last(e => e.Parameters.GetValueOrDefault("ManualOperation")?.Contains("playercaps") == true);
        check(history.Entries.Count > entries, "Profile_ApplyingIsRecordedInTheHistory");
        check(Report(profile.Evaluate(target.Session), "manual/playercaps") is { Status: ProfileSectionStatus.Compatible } same && same.Detail.Contains("misma tabla"), "Profile_SameTableAlreadyInstalledIsNotAConflict");
        check(Caps.Same(originFiles, Caps.Files(origin)), "Profile_ApplyingLeavesTheOriginProjectAlone");
        RestorePoints.Undo(target.Session.History, target.Root, applied.Id);
        check(Caps.Same(targetFiles, Caps.Files(target)) && Caps.IsStock(target), "Profile_UndoRestoresEveryFile");

        // ---- conflicts ----
        Caps.Install(target, LevelCapTable.Default());
        var conflict = profile.Evaluate(target.Session);
        check(Report(conflict, "manual/playercaps") is { Status: ProfileSectionStatus.Conflict, Selectable: true } replaced && replaced.Detail.Contains("21 puntos") && replaced.Detail.Contains("3 puntos"),
            "Profile_DifferentInstalledTableIsAConflict");
        string touched = Path.Combine(target.Game.RomFS, "entrenadores-sinteticos.bin");
        File.WriteAllBytes(touched, [1, 2, 3]);
        target.Session.History.Commit([new FileChange(touched, [1, 2, 3], [3, 2, 1])], "Entrenadores", "Randomizar entrenadores", [],
            new Dictionary<string, string> { ["ActionId"] = "trainers.randomize", ["Scope"] = "All" });
        check(Report(profile.Evaluate(target.Session), "actions/trainers") is { Status: ProfileSectionStatus.Conflict } repeated && repeated.Detail.Contains("trainers.randomize"),
            "Profile_ActionAlreadyAppliedIsAConflict");
        byte[] foreignHook = DefinitiveGameDataCapsChecks.StockBattle(); foreignHook[(int)LevelCapPatch.BattleHook] ^= 0x5A;
        File.WriteAllBytes(Caps.BattlePath(target), foreignHook);
        var modified = profile.Evaluate(target.Session);
        check(Report(modified, "manual/playercaps") is { Status: ProfileSectionStatus.Incompatible, Selectable: false } blocked && blocked.Detail.Contains("no acreditada"),
            "Profile_UnrecognizedHookIsNotAccredited");
        reject(() => profile.Select(modified, ["manual/playercaps"]), "Profile_UnaccreditedPatchCannotBeForced");
        File.Delete(Caps.BattlePath(target));
        check(Report(profile.Evaluate(target.Session), "manual/playercaps").Status == ProfileSectionStatus.Incompatible, "Profile_MissingPatchFileIsNotAccredited");
    }

    /// <summary>
    /// The whole route with the real Global Template executor (a child process over a synthetic project): a profile made of
    /// two randomization actions, one of them chosen, previewed, applied, recorded and undone.
    /// </summary>
    internal static async Task RunExecutor(Action<bool, string> check)
    {
        using var state = new TestState();
        using var f = new Fixture(GameVersion.USUM);
        ModuleChecks.CreateArchives(f); GlobalApplicationChecks.PrepareOpenable(f);
        var doc = GlobalTemplateDocument.Empty("USUM", 7);
        NativeMasterBatchBridge.Capture(doc, new MasterBatchState(BatchAction.PersonalRandomize, new() { Abilities = true }, 0), 7);
        NativeMasterBatchBridge.Capture(doc, new MasterBatchState(BatchAction.MoveRandomize, new() { MoveTypes = true }, 0), 7);
        doc.SetSection("Actions", new JsonArray(new JsonObject { ["Id"] = "personal.randomize", ["Parameters"] = new JsonObject() },
            new JsonObject { ["Id"] = "moves.randomize", ["Parameters"] = new JsonObject() }));
        var profile = ShareableProfile.Parse(ShareableProfile.Create(f.Session, doc, "Perfil del ejecutor", "", "").ToJson());
        check(Ids(profile).SequenceEqual(["actions/personal", "actions/moves"]), "ProfileRun_SectionsOfTheRandomizationRecipe");
        var compatibility = profile.Evaluate(f.Session);
        var recipe = profile.Select(compatibility, ["actions/personal"]);
        var before = GlobalTemplateApplication.Snapshot(f.Session);
        var application = new GlobalTemplateApplication(f.Session);
        using (var cancelled = await application.PreviewAsync(recipe))
            check(cancelled.Files.Count > 0 && GlobalTemplateApplication.Snapshot(f.Session).All(pair => before[pair.Key] == pair.Value) && f.Session.History.Read().Entries.Count == 0,
                "ProfileRun_PreviewShowsChangesAndCancellingWritesNothing");
        using var preview = await application.PreviewAsync(recipe);
        var expected = preview.Files.ToDictionary(file => file.RelativePath, file => file.AfterSha256);
        application.Apply(preview);
        check(expected.Count > 0 && expected.All(pair => ProjectHistory.Hash(File.ReadAllBytes(Path.Combine(f.Root, pair.Key))) == pair.Value), "ProfileRun_ApplyWritesThePreviewedResult");
        var history = f.Session.History.Read();
        check(history.Entries.Count(e => e.Parameters.GetValueOrDefault("ActionId") == "personal.randomize") == 1 &&
            history.Entries.All(e => e.Parameters.GetValueOrDefault("ActionId") != "moves.randomize"), "ProfileRun_OnlyTheChosenSectionIsRecorded");
        var snapshot = history.Entries.Select(e => e.Parameters.GetValueOrDefault("GlobalTemplateSnapshot")).Last(json => json is not null)!;
        check(JsonNode.Parse(snapshot)!["ProgressiveApp"]!["Profile"]!["RecipeSha256"]!.GetValue<string>() == profile.RecipeSha256 &&
            JsonNode.Parse(snapshot)!["ProgressiveApp"]!["Profile"]!["Sections"]!.ToJsonString() == "[\"actions/personal\"]", "ProfileRun_HistoryKeepsTheProvenanceOfTheProfile");
        check(Report(profile.Evaluate(f.Session), "actions/personal").Status == ProfileSectionStatus.Conflict &&
            Report(profile.Evaluate(f.Session), "actions/moves").Status == ProfileSectionStatus.Compatible, "ProfileRun_AppliedSectionBecomesAConflictNextTime");
        var undoable = RestorePoints.LatestUndoable(f.Session.History, f.Root);
        check(undoable is not null, "ProfileRun_TheApplicationCanBeUndone");
        RestorePoints.Undo(f.Session.History, f.Root, undoable!.Id);
        check(GlobalTemplateApplication.Snapshot(f.Session).All(pair => before[pair.Key] == pair.Value), "ProfileRun_UndoRestoresTheProjectFiles");
    }

    internal static void RunUi(Action<bool, string> check)
    {
        if (Application.Current is null) { var app = new ThreeDS.Progressive.App(); app.InitializeComponent(); }
        using var state = new TestState();
        using var origin = DefinitiveGameDataChecks.Create();
        using var target = DefinitiveGameDataChecks.Create(moon: true);
        Caps.WriteStock(origin); Caps.WriteStock(target);
        Caps.Install(origin, Caps.Table());
        Caps.Install(target, LevelCapTable.Default());
        var targetFiles = Caps.Files(target); int entries = target.Session.History.Read().Entries.Count;

        var exportView = new ThreeDS.Progressive.GlobalTemplateView(origin.Session);
        check(Buttons(exportView).Any(b => b.Content?.ToString() == "Exportar perfil compartible…") && Buttons(exportView).Any(b => b.Content?.ToString() == "Importar perfil compartible…"),
            "ProfileUi_ExportAndImportAreVisibleInGlobalTemplate");
        var draft = exportView.ProfileDraft("Perfil de prueba", "Autora");
        check(draft.Sections.Select(s => s.Id).SequenceEqual(["manual/playercaps"]) && exportView.LoadedTemplate is not null, "ProfileUi_DraftComesFromTheHistoryWhenNoRecipeIsLoaded");
        var exportWindow = new ThreeDS.Progressive.ProfileExportWindow(draft);
        check(exportWindow.ProfileName == "Perfil de prueba" && exportWindow.Author == "Autora" && Texts(exportWindow).Any(t => t.Contains("Player Level Caps")) &&
            Texts(exportWindow).Any(t => t.Contains("No incluye la ROM")), "ProfileUi_ExportWindowShowsContentAndWhatIsNeverIncluded");
        exportWindow.Close();

        var profile = ShareableProfile.Parse(JsonNode.Parse(draft.ToJson())!.AsObject().ToJsonString());
        var compatibility = profile.Evaluate(target.Session);
        var window = new ThreeDS.Progressive.ProfileImportWindow(profile, compatibility);
        var row = window.Rows.Single();
        check(row.Report.Status == ProfileSectionStatus.Conflict && row.Box.IsEnabled && row.Box.IsChecked == false && !window.AcceptButton.IsEnabled &&
            window.AcceptButton.Content?.ToString() == "Marca al menos una sección", "ProfileUi_ConflictIsOfferedButNotTickedAndNothingCanBeLoadedYet");
        row.Box.IsChecked = true;
        check(window.AcceptButton.IsEnabled && window.AcceptButton.Content?.ToString() == "Cargar 1 sección" && window.Chosen.SequenceEqual(["manual/playercaps"]) &&
            Texts(window).Any(t => t.Contains("Autora")) && Texts(window).Any(t => t.Contains("no modifica el proyecto")), "ProfileUi_TickingTheConflictEnablesLoadingAndShowsProvenance");
        var importView = new ThreeDS.Progressive.GlobalTemplateView(target.Session);
        importView.UseProfile(profile, compatibility, window.Chosen);
        window.Close();
        check(importView.LoadedTemplate!.Section("ProgressiveApp")!["Profile"]!["Name"]!.GetValue<string>() == "Perfil de prueba" &&
            importView.LoadedTemplate.Actions.SequenceEqual([ManualEditTemplate.ActionId]) && Buttons(importView).Single(b => b.Content?.ToString() == "Previsualizar cambios").IsEnabled &&
            !Buttons(importView).Single(b => b.Content?.ToString() == "Aplicar cambios").IsEnabled, "ProfileUi_LoadedSectionsWaitForPreviewBeforeApplying");
        check(Caps.Same(targetFiles, Caps.Files(target)) && target.Session.History.Read().Entries.Count == entries, "ProfileUi_ImportingWritesNothing");

        File.WriteAllBytes(Caps.BattlePath(target), new byte[64]);
        var blocked = new ThreeDS.Progressive.ProfileImportWindow(profile, profile.Evaluate(target.Session));
        check(!blocked.Rows.Single().Box.IsEnabled && blocked.Rows.Single().Box.IsChecked == false && !blocked.AcceptButton.IsEnabled &&
            Texts(blocked).Any(t => t.Contains("no acreditada")), "ProfileUi_IncompatibleSectionCannotBeTickedAndSaysWhy");
        blocked.Close();

        using var sunMoon = new Fixture(GameVersion.SM);
        var otherView = new ThreeDS.Progressive.GlobalTemplateView(sunMoon.Session);
        try { otherView.ProfileDraft("x"); check(false, "ProfileUi_OtherGamesExplainTheLimit"); }
        catch (InvalidOperationException e) { check(e.Message.Contains("Ultra Sol"), "ProfileUi_OtherGamesExplainTheLimit"); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is T match) yield return match;
            if (child is DependencyObject node) foreach (var inner in Descendants<T>(node)) yield return inner;
        }
    }
    private static IEnumerable<System.Windows.Controls.Button> Buttons(DependencyObject parent) => Descendants<System.Windows.Controls.Button>(parent);
    private static IEnumerable<string> Texts(DependencyObject parent) => Descendants<System.Windows.Controls.TextBlock>(parent).Select(t => t.Text);
}
