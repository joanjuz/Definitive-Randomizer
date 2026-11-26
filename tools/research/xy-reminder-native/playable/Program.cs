using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

// Explicit research experiment. Production admission is never changed or called with an override.
internal static class Program
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    record Experiment(string Source, Dictionary<string, string> Sources, int Zone, int X, int Y, double Facing);
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static string HashFile(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
    static void Check(bool condition, string name) { if (!condition) throw new InvalidDataException(name); Console.WriteLine("PASS " + name); }
    static void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    static string Project(string root) => Path.Combine(root, "Proyecto");
    static string Evidence(string root) => Path.Combine(root, "evidence");
    static Experiment Load(string root) => JsonSerializer.Deserialize<Experiment>(File.ReadAllText(Path.Combine(Evidence(root), "experiment.json")))!;
    static void NoLinks(string path)
    {
        for (var d = new DirectoryInfo(Path.GetFullPath(path)); d is not null; d = d.Parent)
            Check((d.Attributes & FileAttributes.ReparsePoint) == 0, "path_not_link_" + d.Name);
    }
    static void Pin(string root)
    {
        byte[] ex = File.ReadAllBytes(Path.Combine(root, "DecryptedExHeader.bin"));
        Check(ex.Length >= 0x208 && BinaryPrimitives.ReadUInt64LittleEndian(ex.AsSpan(0x200)) == 0x0004000000055D00, "experiment_is_X_only");
        var pins = new Dictionary<string, string>
        {
            ["ExtractedExeFS/.code.bin"] = "0F6D3E1D66CBCD55D46FA453DB4B6AA489E03C3537C6B24026F2A53AF142797A",
            ["ExtractedRomFS/DllField.cro"] = "ED043EB6C5D4057D64CEEFDA99B59B0978412B982427551F6C99BF6084BE6489",
            ["ExtractedRomFS/DllWazaOmoidashi.cro"] = "B76C2045E0D7988AEB1B82F132F9F5C38C7A622FEBDCEDD144C1538A1BB4E5DD",
            ["ExtractedRomFS/DllPokeList.cro"] = "D673A3B9A38BF301C8337672EB7E67AECABB83DB5B8D2A989F26CDB754E22D38",
        };
        foreach (var (path, hash) in pins) Check(HashFile(Path.Combine(root, path)) == hash, "known_reference_" + path);
        var common = new GARC.MemGARC(File.ReadAllBytes(Path.Combine(root, "ExtractedRomFS/a/0/3/1"))).Files;
        Check(Hash(common[42]) == "4068FC8704B1DD9529B2351E0173EF5216A2F4B0225B1282B193498704744D6F", "original_common42");
        string[] story96 = [
            "BD4D1C509737E10D77EA7E387A3FB774FAB9A076C7F05F89FD5675580B9AA329",
            "237B0BB54419DE448A67ABE4C907821181943F22A75AC9A6B1CCC568E849D556",
            "C2E03639A6986DF5D67213A1E121051B3FB5EA8A0699A6447F204B923759E815",
            "5AF8301CA250BF6724E06FB5B9102B29B7AD82A6D17F6D439100EF897C99CE5D",
            "4AE040D6DF42F3426AD7D711127EDF0D10850BC995DB7FADFAF517CFDD8F3F2F",
            "C0BA403E14B23B6A623269E299E6855609AD90E42325F808D5ACD2A1A5BC0F21",
            "14812B46DFE158FD5159C93643793F194BD699051A2B4E7007F2860EFA5793FA",
            "597D3C2DAFB8E785A267AFABA3A480A5364C3F749B972261DE624E9D1FD0A9D3"];
        for (int language = 0; language < story96.Length; language++)
        {
            var texts = new GARC.MemGARC(File.ReadAllBytes(Path.Combine(root, $"ExtractedRomFS/a/0/8/{language}"))).Files;
            Check(Hash(texts[96]) == story96[language], "original_story96_language_" + language);
        }
    }
    static Gen6MapBackend StructuralBackend(GameConfig cfg)
    {
        // The source was pinned above. This enables structural planning in this separate research CLI only.
        var zones = cfg.GetlzGARCData("encdata"); var matrix = cfg.GetlzGARCData("mapMatrix"); var blocks = cfg.GetlzGARCData("mapGR");
        var models = new GARC.MemGARC(File.ReadAllBytes(Path.Combine(cfg.RomFS, "a/1/4/9"))).Files;
        var b = new Gen6MapBackend(Gen6Title.XY, zones.FileCount, i => zones[i], matrix.FileCount, i => matrix[i], blocks.FileCount, i => blocks[i],
            cfg.GetText(TextName.metlist_000000), modelListCount: models.Length, modelList: i => models[i], reminderAccredited: true);
        Check(b.Reminder is { } r && Hash(r.Record) == "C7AB122F70C27E674959DF808E5B595C86AADABE64036B590BB4B993B0E13393", "original_record_exact");
        Check(b.ReminderModelEntry is { } entry && Hash(entry) == "1A7FC7BF2E1E93ED1093D166EFC2A823D5459039D28126E2B3139915A25361E4", "original_model_entry_exact");
        Check(!Gen6MapBackend.FromConfig(cfg).CanAddReminder && !Gen6MoveReminderProfile.Inspect(cfg).Allowed, "production_gate_stays_closed");
        return b;
    }
    static void VerifySources(Experiment plan)
    {
        foreach (var (path, hash) in plan.Sources) Check(HashFile(Path.Combine(plan.Source, path)) == hash, "source_unchanged_" + path);
    }
    static GameProjectSession Open(string root)
    {
        Check(Path.GetFileName(Path.GetFullPath(root)).StartsWith("PruebasRecordador-", StringComparison.Ordinal), "explicit_experiment_folder");
        NoLinks(root); Pin(Project(root));
        return GameProjectSession.Open(Project(root), recoverInterruptedSave: false);
    }
    static void Prepare(string source, string root, int zone, int x, int y)
    {
        source = Path.GetFullPath(source); root = Path.GetFullPath(root);
        Check(!source.Equals(root, StringComparison.OrdinalIgnoreCase) && !source.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !root.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "source_output_do_not_overlap");
        Check(Path.GetFileName(root).StartsWith("PruebasRecordador-", StringComparison.Ordinal), "explicit_experiment_folder");
        Check(!Directory.Exists(root) && !File.Exists(root), "destination_must_be_new");
        Check(zone != 34 && zone >= 0, "experiment_outside_source_zone");
        NoLinks(source); NoLinks(Path.GetDirectoryName(root)!); Pin(source);
        var folders = new[] { "ExtractedRomFS", "ExtractedExeFS" };
        foreach (var folder in folders) NoLinks(Path.Combine(source, folder));
        Check(folders.SelectMany(f => Directory.EnumerateDirectories(Path.Combine(source, f), "*", SearchOption.AllDirectories))
            .All(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0), "no_linked_source_directories");
        var files = folders.SelectMany(f => Directory.EnumerateFiles(Path.Combine(source, f), "*", SearchOption.AllDirectories))
            .Concat(new[] { "DecryptedExHeader.bin", "HeaderNCCH.bin", "HeaderNCCH0.bin" }.Select(f => Path.Combine(source, f)).Where(File.Exists)).ToArray();
        Check(files.All(f => (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0), "no_linked_input_files");
        var hashes = files.ToDictionary(f => Path.GetRelativePath(source, f), HashFile);
        Directory.CreateDirectory(Evidence(root)); Directory.CreateDirectory(Project(root));
        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(source, file), target = Path.Combine(Project(root), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, overwrite: false);
            Check(HashFile(target) == hashes[relative], "copy_readback_" + relative);
        }
        var plan = new Experiment(source, hashes, zone, x, y, 180);
        Save(Path.Combine(Evidence(root), "experiment.json"), plan);
        var project = Open(root); var backend = StructuralBackend(project.GameConfiguration);
        var check = backend.ValidateAddReminder(zone, x, y, plan.Facing);
        Check(check.Allowed, "valid_destination_" + check.Reason);
        Check(!backend.ZoneEvents(zone).Any(e => e.Kind == MapEventKind.Npc && e.Properties.Any(p => p.Name == "Script" && p.Value == "2261")), "destination_has_no_reminder");
        VerifySources(plan);
        Console.WriteLine("PREPARED " + root);
    }
    static byte[] Decode(byte[] raw)
    {
        if (raw[0] != 0x11) return raw;
        using var output = new MemoryStream(); LZSS.Decompress(new MemoryStream(raw), raw.Length, output); return output.ToArray();
    }
    static Guid Install(string root, bool nurse = false)
    {
        var plan = Load(root); VerifySources(plan); var project = Open(root); var cfg = project.GameConfiguration; var b = StructuralBackend(cfg);
        int x = nurse ? plan.X + 1 : plan.X;
        var check = nurse ? b.ValidateAddNurse(plan.Zone, x, plan.Y, plan.Facing) : b.ValidateAddReminder(plan.Zone, x, plan.Y, plan.Facing);
        Check(check.Allowed, "placement_validation_" + check.Reason);
        byte[] record = nurse ? b.Nurse!.Record : b.Reminder!.Record;
        int model = nurse ? b.Nurse!.Model : b.Reminder!.Model;
        string zonesPath = Path.Combine(cfg.RomFS, cfg.GetGARCFileName("encdata")), modelsPath = Path.Combine(cfg.RomFS, "a/1/4/9");
        byte[] zonesBefore = File.ReadAllBytes(zonesPath), modelsBefore = File.ReadAllBytes(modelsPath);
        var originalMembers = new GARC.MemGARC(zonesBefore).Files; var members = (byte[][])originalMembers.Clone();
        byte[] current = Decode(members[plan.Zone]); Check(current.AsSpan().SequenceEqual(b.ZoneBytes(plan.Zone)), "preview_matches_zone");
        byte[] edited = Gen6ZoneEdit.AddNpc(current, record, x, plan.Y, Gen6MapBackend.FacingValue(plan.Facing)!.Value);
        members[plan.Zone] = originalMembers[plan.Zone][0] == 0x11 ? GameCatchZoneService.Compress(edited) : edited;
        Check(Decode(members[plan.Zone]).AsSpan().SequenceEqual(edited), "compression_roundtrip");
        byte[] zonesAfter = GarcTransaction.Build(zonesBefore, cfg.GetGARCReference("encdata"), members, preserveRecordSize: false);
        var outputMembers = new GARC.MemGARC(zonesAfter).Files;
        Check(outputMembers.Length == originalMembers.Length && Enumerable.Range(0, outputMembers.Length)
            .All(i => i == plan.Zone || outputMembers[i].AsSpan().SequenceEqual(originalMembers[i])), "all_other_zones_identical");
        var files = new List<FileChange> { new(zonesPath, zonesBefore, zonesAfter) };
        int area = b.Zone(plan.Zone).Area;
        var originalModels = new GARC.MemGARC(modelsBefore).Files; var models = (byte[][])originalModels.Clone();
        if (!Gen6AreaModels.Models(models[area]).Contains(model))
        {
            models[area] = Gen6AreaModels.Add(models[area], nurse ? b.NurseModelEntry! : b.ReminderModelEntry!);
            byte[] modelsAfter = GarcTransaction.Build(modelsBefore, cfg.GetGARCReference("encdata").GetRelativeGARC(137, "npcmodels"), models, preserveRecordSize: false);
            files.Add(new(modelsPath, modelsBefore, modelsAfter));
        }
        Check(HashFile(zonesPath) == Hash(zonesBefore) && HashFile(modelsPath) == Hash(modelsBefore), "preview_writes_nothing");
        foreach (var file in files) GarcTransaction.ReadUnchanged(file.Path, file.Fingerprint);
        GarcTransaction.ReadUnchanged(modelsPath, SHA256.HashData(modelsBefore));
        project.History.Commit(files, "Editor de mapas", nurse ? "Experimento: enfermera" : "Experimento: recordador X fuera de Pueblo Fresco",
            [new BatchChange(plan.Zone, "Experimento aislado", "NPC", "sin NPC nuevo", $"script {BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(10))}, modelo {model}", "npc.experiment")],
            new Dictionary<string, string> { ["ActionId"] = "research.xy.move-reminder", ["Gameplay"] = "not tested", ["Scope"] = "ExperimentalCopyOnly" });
        var reopened = StructuralBackend(GameProjectSession.Open(Project(root), false).GameConfiguration);
        var added = reopened.ZoneEvents(plan.Zone).Last(e => e.Kind == MapEventKind.Npc);
        Check(added.X == x && added.Y == plan.Y && added.Properties.Any(p => p.Name == "Modelo" && p.Value == model.ToString()), "reopened_added_npc");
        var after = reopened.Zone(plan.Zone); var before = b.Zone(plan.Zone);
        Check(before.Entities.Records.All(r => before.Entities.Bytes(r).SequenceEqual(after.Entities.Bytes(after.Entities.Records.Single(a => a.Kind == r.Kind && a.Index == r.Index)))), "other_entities_exact");
        Check(Enumerable.Range(0, before.Pack.Count).Where(i => i != 1).All(i => before.Pack.Part(i).SequenceEqual(after.Pack.Part(i))), "other_zone_parts_exact");
        Check(Gen6AreaModels.Models(reopened.ModelList(area)!).Contains(model), "area_loads_model");
        var entry = project.History.Read().Entries.Last();
        Save(Path.Combine(Evidence(root), nurse ? "nurse-entry.json" : "reminder-entry.json"), entry);
        VerifySources(plan); return entry.Id;
    }
    static void Undo(string root, Guid id)
    {
        var project = Open(root); RestorePoints.Undo(project.History, project.ProjectRoot, id); VerifySources(Load(root));
    }
    static void Persistence(string root)
    {
        var plan = Load(root); string zones = Path.Combine(Project(root), "ExtractedRomFS/a/0/1/2"), models = Path.Combine(Project(root), "ExtractedRomFS/a/1/4/9");
        string zoneHash = HashFile(zones), modelHash = HashFile(models);
        Check(zoneHash == plan.Sources[Path.Combine("ExtractedRomFS", "a", "0", "1", "2")]
            && modelHash == plan.Sources[Path.Combine("ExtractedRomFS", "a", "1", "4", "9")], "persistence_starts_from_unchanged_copy");
        Guid first = Install(root); Undo(root, first);
        Check(HashFile(zones) == zoneHash && HashFile(models) == modelHash, "undo_restores_both_archives_exactly");
        Guid nurse = Install(root, nurse: true); Guid reminder = Install(root);
        var b = StructuralBackend(Open(root).GameConfiguration);
        var scripts = b.ZoneEvents(plan.Zone).Where(e => e.Kind == MapEventKind.Npc).Select(e => e.Properties.Single(p => p.Name == "Script").Value).ToArray();
        Check(scripts.Contains("2100") && scripts.Contains("2261"), "nurse_and_reminder_coexist_on_disk");
        Undo(root, reminder); Undo(root, nurse);
        Check(HashFile(zones) == zoneHash && HashFile(models) == modelHash, "coexistence_undo_restores_original_archives");
        Install(root); // Leave only the reminder in the copy for the owner's gameplay experiment.
        var changes = plan.Sources.Select(p => new { Path = p.Key, Before = p.Value, After = HashFile(Path.Combine(Project(root), p.Key)) }).Where(p => p.Before != p.After).ToArray();
        Check(changes.Length is 1 or 2 && changes.All(p => p.Path == Path.Combine("ExtractedRomFS", "a", "0", "1", "2") || p.Path == Path.Combine("ExtractedRomFS", "a", "1", "4", "9")), "only_zone_and_model_archives_changed");
        Save(Path.Combine(Evidence(root), "changed-files.json"), changes);
    }
    static void Build(string root, string image)
    {
        var project = Open(root);
        Check(File.Exists(Path.Combine(Evidence(root), "changed-files.json")), "persistence_checks_completed");
        var result = new GameRomRebuildService(project).BuildAsync(RomRebuildKind.Trimmed3DS, image).GetAwaiter().GetResult();
        Save(Path.Combine(Evidence(root), "build.json"), new { result.File, result.Hash, result.Bytes });
        Console.WriteLine($"BUILT {result.File} {result.Hash} {result.Bytes}"); VerifySources(Load(root));
    }
    static void VerifyImage(string root, string image, string extracted)
    {
        var plan = Load(root); Open(root);
        using var build = JsonDocument.Parse(File.ReadAllText(Path.Combine(Evidence(root), "build.json")));
        Check(HashFile(image) == build.RootElement.GetProperty("Hash").GetString(), "candidate_image_matches_build");
        Check(!Directory.Exists(extracted) && !File.Exists(extracted), "reextraction_destination_new");
        NoLinks(Path.GetDirectoryName(Path.GetFullPath(extracted))!);
        new GameRomExtractionService(Project(root)).ExtractAsync(image, extracted).GetAwaiter().GetResult();
        int count = 0;
        foreach (var (sourceFolder, targetFolder) in new[] { ("ExtractedRomFS", "RomFS"), ("ExtractedExeFS", "ExeFS") })
        {
            string original = Path.Combine(Project(root), sourceFolder), output = Path.Combine(extracted, targetFolder);
            var expected = Directory.GetFiles(original, "*", SearchOption.AllDirectories);
            Check(expected.Length == Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length, "reextracted_file_count_" + targetFolder);
            foreach (string file in expected)
            {
                Check(HashFile(file) == HashFile(Path.Combine(output, Path.GetRelativePath(original, file))), "rebuild_roundtrip_" + Path.GetRelativePath(original, file));
                count++;
            }
        }
        var reopened = GameProjectSession.Open(extracted, false);
        var b = Gen6MapBackend.FromConfig(reopened.GameConfiguration);
        var npc = b.ZoneEvents(plan.Zone).Last(e => e.Kind == MapEventKind.Npc);
        Check(npc.X == plan.X && npc.Y == plan.Y && npc.Properties.Any(p => p.Name == "Script" && p.Value == "2261")
            && npc.Properties.Any(p => p.Name == "Modelo" && p.Value == "54"), "reconstructed_reminder_preserved");
        Check(Gen6AreaModels.Models(b.ModelList(b.Zone(plan.Zone).Area)!).Contains(54), "reconstructed_area_loads_model54");
        Check(!b.CanAddReminder && !Gen6MoveReminderProfile.Inspect(reopened.GameConfiguration).Allowed, "reconstructed_project_keeps_production_gate_closed");
        Save(Path.Combine(Evidence(root), "rebuild-verification.json"), new { Files = count, AllExact = true, Gameplay = "not tested", ProductionAllowed = false });
        VerifySources(plan);
    }
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 6 && args[0] == "prepare") Prepare(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]));
            else if (args.Length == 2 && args[0] == "check") { StructuralBackend(Open(args[1]).GameConfiguration); VerifySources(Load(args[1])); }
            else if (args.Length == 2 && args[0] == "persistence") Persistence(args[1]);
            else if (args.Length == 3 && args[0] == "build") Build(args[1], args[2]);
            else if (args.Length == 4 && args[0] == "verify") VerifyImage(args[1], args[2], args[3]);
            else if (args.Length == 3 && args[0] == "undo") Undo(args[1], Guid.Parse(args[2]));
            else { Console.Error.WriteLine("prepare SOURCE NEW_PruebasRecordador-X ZONE X Y | check ROOT | persistence ROOT | build ROOT NEW_3DS | verify ROOT IMAGE NEW_EXTRACTED | undo ROOT ENTRY_GUID"); return 2; }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
