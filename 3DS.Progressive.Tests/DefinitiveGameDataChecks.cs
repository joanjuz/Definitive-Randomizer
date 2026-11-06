using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class DefinitiveGameDataChecks
{
    private static Fixture Create(bool moon = false)
    {
        var f = new Fixture(GameVersion.USUM);
        ModuleChecks.CreateArchives(f);
        if (moon) typeof(GameConfig).GetProperty("Files")!.SetValue(f.Game, GARCReference.GARCReference_UM);
        var banks = Enumerable.Range(0, f.Game.GameText.Max(t => t.Index) + 1).Select(_ => new[] { "Unused" }).ToArray();
        foreach (var name in new[] { TextName.SpeciesNames, TextName.MoveNames, TextName.Types, TextName.AbilityNames })
            banks[f.Game.GameText.Single(t => t.Name == name).Index] = f.Game.GetText(name);
        f.WriteGarc(ModuleChecks.PathFor(f, "gametext"), banks.Select(b => TextFile.GetBytes(f.Game, b)).ToArray(), f.Game.GetGARCReference("gametext"));
        return f;
    }
    private static JsonNode Species(JsonObject doc, int id, int form = 0) => doc["species"]!.AsArray().Single(r => r!["speciesId"]!.GetValue<int>() == id && r["formId"]!.GetValue<int>() == form)!;
    private static void SetRecords(Fixture f, Action<byte[][]> edit)
    {
        var records = f.PersonalRecords; edit(records); GarcTransaction.RebuildPersonalMaster(records);
        f.WriteGarc(f.PersonalPath, records, f.Game.GetGARCReference("personal"));
    }
    private static void SaveEvolution(Fixture f)
    {
        var editor = new GameEvolutionService(f.Session);
        var values = EditorValues.Flatten(editor.Read(1));
        values["E0.Method"] = 4; values["E0.Argument"] = 0; values["E0.Species"] = 3; values["E0.Form"] = 0; values["E0.Level"] = 27;
        editor.Save(1, values);
    }
    internal static void WriteInteropFixture(string destination)
    {
        using var f = Create();
        f.Session.SavePersonal(1, f.Session.ReadPersonal(1) with { HP = 123, Attack = 141, FirstType = 9, SecondType = 9, Ability1 = 65 });
        var m = new GameMoveService(f.Session); m.Save(1, m.Read(1) with { Power = 137, Type = 9, Priority = -2 });
        SaveEvolution(f);
        DefinitiveGameData.Export(f.Session, destination);
    }
    internal static void Run(Action<bool,string> check, Action<Action,string> reject)
    {
        using var state = new TestState();
        foreach (bool moon in new[] { false, true })
        {
            using var f = Create(moon);
            string suffix = moon ? "Moon" : "Sun";
            var original = DefinitiveGameData.Build(f.Session);
            check(original["format"]!.GetValue<string>() == DefinitiveGameData.Format && original["schemaVersion"]!.GetValue<int>() == 1 &&
                original["game"]!["id"]!.GetValue<string>() == (moon ? "ultra-moon" : "ultra-sun") &&
                original["game"]!["titleId"]!.GetValue<string>() == (moon ? "00040000001B5100" : "00040000001B5000") &&
                original["game"]!["revision"] is null && original["game"]!["revisionStatus"]!.GetValue<string>() == "unknown", "Dgd_IdentityAndHonestRevision_" + suffix);
            check(original["species"]!.AsArray().Count == 807 && original["moves"]!.AsArray().Count == 729 &&
                original["abilities"]!.AsArray().Count == 234 && original["types"]!.AsArray().Count == 18, "Dgd_CompleteBaseCatalogs_" + suffix);
            var pending = f.Session.ReadPersonal(1) with { HP = 222 };
            check(Species(DefinitiveGameData.Build(f.Session), 1)["baseStats"]!["hp"]!.GetValue<int>() == 60 && pending.HP == 222, "Dgd_UnsavedOptionsNotExported_" + suffix);
            f.Session.SavePersonal(1, f.Session.ReadPersonal(1) with { HP = 123, Attack = 141, FirstType = 9, SecondType = 9, Ability1 = 65 });
            var edited = DefinitiveGameData.Build(f.Session);
            var row = Species(edited, 1);
            check(row["baseStats"]!["hp"]!.GetValue<int>() == 123 && row["baseStats"]!["attack"]!.GetValue<int>() == 141 &&
                row["types"]![0]!.GetValue<int>() == 9 && row["abilities"]![0]!.GetValue<int>() == 65, "Dgd_SavedPersonalValues_" + suffix);
            Guid point = f.Session.History.Read().Entries.Last().Id;
            RestorePoints.Undo(f.Session.History, f.Root, point);
            var undone = DefinitiveGameData.Build(f.Session);
            check(Species(undone, 1)["baseStats"]!["hp"]!.GetValue<int>() == 60 &&
                undone["source"]!["fingerprint"]!.GetValue<string>() == original["source"]!["fingerprint"]!.GetValue<string>(), "Dgd_UndoReflectsDisk_" + suffix);
            SetRecords(f, r => r[1][0] = 99);
            check(Species(DefinitiveGameData.Build(f.Session), 1)["baseStats"]!["hp"]!.GetValue<int>() == 99, "Dgd_StaleSessionReadsCurrentDisk_" + suffix);
            SetRecords(f, r => { var p = new PersonalInfoSM(r[1]) { FormeCount = 2 }; r[1] = p.Write(); BitConverter.GetBytes((ushort)808).CopyTo(r[1],0x1C); r[808][0] = 77; });
            var forms = DefinitiveGameData.Build(f.Session);
            check(Species(forms, 1, 1)["personalIndex"]!.GetValue<int>() == 808 && Species(forms, 1, 1)["baseStats"]!["hp"]!.GetValue<int>() == 77,
                "Dgd_AlternateFormUsesOwnRecord_" + suffix);
            var evolutions = ModuleChecks.Read(f, "evolution"); Array.Clear(evolutions[808]);
            f.WriteGarc(ModuleChecks.PathFor(f, "evolution"), evolutions, f.Game.GetGARCReference("evolution"));
            var emptyRoute = DefinitiveGameData.Build(f.Session)["evolutions"]!.AsArray().Single(r => r!["speciesId"]!.GetValue<int>() == 1 && r["formId"]!.GetValue<int>() == 1)!;
            check(emptyRoute["routes"]!.AsArray().Count == 0, "Dgd_RemovedEvolutionsExplicitlyEmpty_" + suffix);
            var move = new GameMoveService(f.Session); move.Save(1, move.Read(1) with { Power = 137, Type = 9, Priority = -2 });
            var moveRow = DefinitiveGameData.Build(f.Session)["moves"]![1]!;
            check(moveRow["power"]!.GetValue<int>() == 137 && moveRow["typeId"]!.GetValue<int>() == 9 && moveRow["priority"]!.GetValue<int>() == -2, "Dgd_SignedMovePropertiesFromSavedFile_" + suffix);
            SaveEvolution(f);
            var route = DefinitiveGameData.Build(f.Session)["evolutions"]!.AsArray().Single(r => r!["speciesId"]!.GetValue<int>() == 1 && r["formId"]!.GetValue<int>() == 0)!["routes"]![0]!;
            check(route["method"]!.GetValue<int>() == 4 && route["targetSpeciesId"]!.GetValue<int>() == 3 && route["targetFormId"]!.GetValue<int>() == 0 && route["level"]!.GetValue<int>() == 27,
                "Dgd_SavedEvolutionValues_" + suffix);
            var before = Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
            string output = Path.Combine(Path.GetTempPath(), "dgd-export-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                DefinitiveGameData.Export(f.Session, output);
                var exported = JsonNode.Parse(File.ReadAllText(output))!;
                check(exported["source"]!["fingerprint"]!.GetValue<string>().Length == 64 && !File.ReadAllText(output).Contains(f.Root) &&
                    before.All(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key))) == p.Value), "Dgd_ExportPreservesSourcesAndPrivacy_" + suffix);
                check(exported.ToJsonString() == DefinitiveGameData.Build(f.Session).ToJsonString(), "Dgd_DeterministicExport_" + suffix);
                reject(() => DefinitiveGameData.Export(f.Session, output), "Dgd_ExistingOutputRejected_" + suffix);
            }
            finally { if (File.Exists(output)) File.Delete(output); }
            reject(() => DefinitiveGameData.Export(f.Session, Path.Combine(f.Root, "data.json")), "Dgd_OutputInsideProjectRejected_" + suffix);
            byte[] header = new byte[0x200]; "NCCH"u8.CopyTo(header.AsSpan(0x100));
            BitConverter.GetBytes(0x0004000000000000UL).CopyTo(header, 0x118); File.WriteAllBytes(Path.Combine(f.Root, "ncchheader.bin"), header);
            reject(() => DefinitiveGameData.Build(f.Session), "Dgd_MismatchedTitleHeaderRejected_" + suffix);
            File.Delete(Path.Combine(f.Root, "ncchheader.bin"));
            var malformedMoves = f.MoveRecords; malformedMoves[1][0] = 18;
            f.WriteGarc(f.MovePath, [Mini.PackMini(malformedMoves,"WD")], f.Game.GetGARCReference("move"));
            reject(() => DefinitiveGameData.Build(f.Session), "Dgd_InvalidMoveTypeRejected_" + suffix);
            malformedMoves[1][0] = 9;
            f.WriteGarc(f.MovePath, [Mini.PackMini(malformedMoves,"WD")], f.Game.GetGARCReference("move"));
            var corrupt = f.PersonalRecords; corrupt[1][0] ^= 1; f.WriteGarc(f.PersonalPath, corrupt, f.Game.GetGARCReference("personal"));
            reject(() => DefinitiveGameData.Build(f.Session), "Dgd_InconsistentPersonalMasterRejected_" + suffix);
        }
        using var other = new Fixture(GameVersion.SM);
        reject(() => DefinitiveGameData.Build(other.Session), "Dgd_SunMoonUnsupported");
    }
    internal static void RunUi(Action<bool,string> check)
    {
        if (Application.Current is null) { var app = new ThreeDS.Progressive.App(); app.InitializeComponent(); }
        foreach (var version in new[] { GameVersion.USUM, GameVersion.SM })
        {
            using var f = new Fixture(version);
            var view = new ThreeDS.Progressive.SystemsView(f.Session, "RomTools", _ => { }, () => { }, () => { });
            bool found = Buttons(view).Any(b => b.Content?.ToString() == "Exportar datos para Progressive Tracker");
            check(found == (version == GameVersion.USUM), "DgdUi_ExportAvailableOnlyUsum_" + version);
        }
    }
    private static IEnumerable<Button> Buttons(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        { if (child is Button button) yield return button; foreach (var b in Buttons(child)) yield return b; }
    }
}
