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

    internal static void RunUi(Action<bool, string> check)
    {
        if (Application.Current is null) { var app = new ThreeDS.Progressive.App(); app.InitializeComponent(); }
        foreach (var version in new[] { GameVersion.USUM, GameVersion.SM })
        {
            using var f = new Fixture(version);
            var view = new ThreeDS.Progressive.SystemsView(f.Session, "RomTools", _ => { }, () => { }, () => { });
            check(Buttons(view).Any(b => b.Content?.ToString() == "Exportar diagnóstico de megaevoluciones…") == (version == GameVersion.USUM), "MegaDiagnosisUi_AvailableOnlyUsum_" + version);
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
