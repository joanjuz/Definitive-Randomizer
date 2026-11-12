using System.IO;
using System.Windows;
using pk3DS.Core;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Services;
using Caps = PlayerLevelCapRecipeChecks;

/// <summary>
/// Read-only diagnosis of the abilities of Mega Evolutions (Ultra Sun / Ultra Moon). Synthetic Personal data: it checks
/// what the report states about saved data, not the behaviour of a battle.
/// </summary>
internal static class MegaAbilityDiagnosticChecks
{
    private const int Venusaur = 3, Charizard = 6, Bulbasaur = 1;
    /// <summary>Gives a species extra forms stored from <paramref name="first"/> on, with the abilities given for each.</summary>
    private static void Forms(Fixture f, int species, int first, params int[][] abilities)
    {
        var records = f.PersonalRecords;
        records[species] = new PersonalInfoSM(records[species]) { FormeCount = abilities.Length + 1 }.Write();
        BitConverter.GetBytes((ushort)first).CopyTo(records[species], 0x1C);
        for (int i = 0; i < abilities.Length; i++) records[first + i] = new PersonalInfoSM(records[first + i]) { Abilities = abilities[i] }.Write();
        GarcTransaction.RebuildPersonalMaster(records);
        f.WriteGarc(f.PersonalPath, records, f.Game.GetGARCReference("personal"));
    }

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        using var state = new TestState();
        using var f = DefinitiveGameDataChecks.Create();
        var none = MegaAbilityDiagnostics.Inspect(f.Session);
        check(MegaAbilityDiagnostics.Supports(f.Session) && none.Rows.Count == 0 && none.SlotDependent == 0 && none.PersonalSha256.Length == 64, "MegaDiagnosis_NoMegaFormsNothingToReport");

        // As in the original game: one ability in the three slots of the Mega form.
        Forms(f, Venusaur, 808, [47, 47, 47]);
        var before = Caps.Files(f); int entries = f.Session.History.Read().Entries.Count;
        var vanilla = MegaAbilityDiagnostics.Inspect(f.Session);
        check(vanilla.Rows.Single() is { Species: Venusaur, Form: 1, PersonalIndex: 808, SlotDependent: false } row && row.FormAbilities.SequenceEqual([47, 47, 47]) &&
            row.BaseAbilities.SequenceEqual([1, 2, 3]) && vanilla.SlotDependent == 0, "MegaDiagnosis_SingleAbilityMegaIsNotFlagged");
        check(vanilla.PersonalSha256 != none.PersonalSha256, "MegaDiagnosis_FingerprintFollowsThePersonalTable");
        check(MegaAbilityDiagnostics.Inspect(f.Session).PersonalSha256 == vanilla.PersonalSha256, "MegaDiagnosis_FingerprintIsStable");

        // Three different abilities, as an ability randomization leaves a Mega form.
        Forms(f, Venusaur, 808, [47, 65, 34]);
        // Both Mega forms of one species, and a species whose extra form is not a Mega.
        Forms(f, Charizard, 810, [181, 181, 181], [70, 70, 94]);
        Forms(f, Bulbasaur, 812, [5, 6, 7]);
        before = Caps.Files(f);
        var mixed = MegaAbilityDiagnostics.Inspect(f.Session);
        check(mixed.Rows.Select(r => (r.Species, r.Form, r.PersonalIndex, r.SlotDependent)).SequenceEqual([(Venusaur, 1, 808, true), (Charizard, 1, 810, false), (Charizard, 2, 811, true)]) &&
            mixed.SlotDependent == 2, "MegaDiagnosis_SlotDependentMegasAreFlaggedAndOtherFormsIgnored");
        string text = MegaAbilityDiagnostics.Describe(f.Session, mixed);
        check(text.Contains("Formas Mega, Primigenias y Ultra revisadas: 3") && text.Contains("Con habilidad distinta según la ranura: 2") && text.Contains(mixed.PersonalSha256) &&
            text.Contains("No reproduce un combate") && text.Contains("sigue sin acreditarse") && text.Contains("(Personal 811)"), "MegaDiagnosis_ReportStatesFactsFingerprintAndLimits");
        check(!text.Contains(f.Root, StringComparison.OrdinalIgnoreCase), "MegaDiagnosis_ReportCarriesNoLocalPath");
        check(Caps.Same(before, Caps.Files(f)) && f.Session.History.Read().Entries.Count == entries, "MegaDiagnosis_WritesNothing");

        // Refusals.
        using (var other = new Fixture(GameVersion.SM))
        {
            check(!MegaAbilityDiagnostics.Supports(other.Session), "MegaDiagnosis_OnlyUsum");
            reject(() => MegaAbilityDiagnostics.Inspect(other.Session), "MegaDiagnosis_OtherGameRefused");
        }
        var records = f.PersonalRecords; records[^1][0] ^= 0xFF;
        f.WriteGarc(f.PersonalPath, records, f.Game.GetGARCReference("personal"));
        reject(() => MegaAbilityDiagnostics.Inspect(f.Session), "MegaDiagnosis_InconsistentPersonalTableRefused");
        File.Delete(f.PersonalPath);
        reject(() => MegaAbilityDiagnostics.Inspect(f.Session), "MegaDiagnosis_MissingPersonalRefused");
    }

    /// <summary>Two players: the comparison reads two «Definitive Game Data» exports made by the real exporter.</summary>
    internal static void RunComparison(Action<bool, string> check, Action<Action, string> reject)
    {
        using var state = new TestState();
        using var one = DefinitiveGameDataChecks.Create();
        using var two = DefinitiveGameDataChecks.Create(moon: true);
        Forms(one, Venusaur, 808, [47, 65, 34]); Forms(one, Charizard, 810, [181, 181, 181], [70, 70, 94]);
        Forms(two, Venusaur, 808, [47, 65, 34]); Forms(two, Charizard, 810, [181, 181, 181], [70, 70, 94]);
        string first = DefinitiveGameData.Build(one.Session).ToJsonString(), same = DefinitiveGameData.Build(two.Session).ToJsonString();
        var equal = MegaAbilityComparison.Compare(first, same);
        check(equal.FirstGame == "ultra-sun" && equal.SecondGame == "ultra-moon" && equal.MegasCompared == 3 && equal.MegaDifferences.Count == 0 &&
            equal.FormsWithDifferentAbilities == 0 && equal.OnlyInOne.Count == 0, "MegaComparison_SameAbilitiesNoDifferences");
        check(MegaAbilityComparison.Describe(equal).Contains("- Ninguna") && MegaAbilityComparison.Describe(equal).Contains("No reproduce un combate"), "MegaComparison_ReportSaysNothingDiffersAndItsLimits");

        // The second player randomized differently: one Mega form and one ordinary species differ.
        Forms(two, Charizard, 810, [181, 181, 181], [91, 70, 94]);
        var records = two.PersonalRecords; records[25] = new PersonalInfoSM(records[25]) { Abilities = [9, 9, 31] }.Write();
        GarcTransaction.RebuildPersonalMaster(records); two.WriteGarc(two.PersonalPath, records, two.Game.GetGARCReference("personal"));
        string other = DefinitiveGameData.Build(two.Session).ToJsonString();
        var differs = MegaAbilityComparison.Compare(first, other);
        check(!differs.SameFingerprint && differs.MegaDifferences.Single() is { Species: Charizard, Form: 2 } d && d.First.SequenceEqual([70, 70, 94]) && d.Second.SequenceEqual([91, 70, 94]) &&
            differs.FormsWithDifferentAbilities == 2 && differs.MegasCompared == 3, "MegaComparison_DifferentMegaAbilitiesAreListed");
        string text = MegaAbilityComparison.Describe(differs);
        check(text.Contains("NO coinciden") && text.Contains("con habilidades distintas: 1") && text.Contains("Es una hipótesis") && !text.Contains(one.Root, StringComparison.OrdinalIgnoreCase),
            "MegaComparison_ReportListsTheDifferenceWithoutClaimingTheCause");
        var mirrored = MegaAbilityComparison.Compare(other, first);
        check(mirrored.MegaDifferences.Single().First.SequenceEqual([91, 70, 94]) && mirrored.MegaDifferences.Count == differs.MegaDifferences.Count, "MegaComparison_IsSymmetric");

        // Files.
        string folder = Path.Combine(Path.GetTempPath(), "mega-compare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string a = Path.Combine(folder, "a.json"), b = Path.Combine(folder, "b.json");
            File.WriteAllText(a, first); File.WriteAllText(b, other);
            check(MegaAbilityComparison.CompareFiles(a, b).MegaDifferences.Count == 1 && File.ReadAllText(a) == first && File.ReadAllText(b) == other && Directory.GetFiles(folder).Length == 2,
                "MegaComparison_ReadsTwoFilesAndWritesNothing");
            reject(() => MegaAbilityComparison.CompareFiles(a, Path.Combine(folder, "no-existe.json")), "MegaComparison_MissingFileRefused");
        }
        finally { Directory.Delete(folder, true); }

        // Refusals: each names which of the two files is wrong.
        string Edit(Action<System.Text.Json.Nodes.JsonObject> change) { var root = System.Text.Json.Nodes.JsonNode.Parse(first)!.AsObject(); change(root); return root.ToJsonString(); }
        (string Name, string Json)[] refused =
        [
            ("NotJson", "{ no"), ("ArrayRoot", "[]"), ("OtherFormat", Edit(r => r["format"] = "definitive-randomizer-profile")), ("FutureSchema", Edit(r => r["schemaVersion"] = 2)),
            ("OtherGame", Edit(r => r["game"]!["id"] = "sun")), ("NoSpecies", Edit(r => r.Remove("species"))), ("TwoAbilitySlots", Edit(r => r["species"]![0]!["abilities"]!.AsArray().RemoveAt(0))),
            ("AbilityAsText", Edit(r => r["species"]![0]!["abilities"]![0] = "1")), ("RepeatedForm", Edit(r => r["species"]!.AsArray().Add(r["species"]![0]!.DeepClone()))),
            ("NoFingerprint", Edit(r => r["source"]!.AsObject().Remove("fingerprint"))),
        ];
        foreach (var (name, json) in refused)
        {
            reject(() => MegaAbilityComparison.Compare(json, first), "MegaComparison_RefusedAsFirst_" + name);
            try { MegaAbilityComparison.Compare(first, json); check(false, "MegaComparison_RefusedAsSecondNamesTheFile_" + name); }
            catch (InvalidDataException e) { check(e.Message.StartsWith("El segundo archivo", StringComparison.Ordinal), "MegaComparison_RefusedAsSecondNamesTheFile_" + name); }
        }
    }

    internal static void RunUi(Action<bool, string> check)
    {
        if (Application.Current is null) { var app = new ThreeDS.Progressive.App(); app.InitializeComponent(); }
        foreach (var version in new[] { GameVersion.USUM, GameVersion.SM })
        {
            using var f = new Fixture(version);
            var view = new ThreeDS.Progressive.SystemsView(f.Session, "RomTools", _ => { }, () => { }, () => { });
            check(Buttons(view).Any(b => b.Content?.ToString() == "Exportar diagnóstico de megaevoluciones…") == (version == GameVersion.USUM), "MegaDiagnosisUi_AvailableOnlyUsum_" + version);
            check(Buttons(view).Any(b => b.Content?.ToString() == "Comparar con otro jugador…") == (version == GameVersion.USUM), "MegaComparisonUi_AvailableOnlyUsum_" + version);
        }
    }
    private static IEnumerable<System.Windows.Controls.Button> Buttons(DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is System.Windows.Controls.Button button) yield return button;
            if (child is DependencyObject node) foreach (var inner in Buttons(node)) yield return inner;
        }
    }
}
