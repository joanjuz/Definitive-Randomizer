using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

// Research-only CLI. No production service/UI or USUM code is modified.
internal static class Program
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    const string Purpose = "xy-persistent-items-two-gates-experiment-v1";
    const string CandidateCro = "84E125B4DEB74D523DFC742E3A1D46AE3601B5B24A2F9E4D5CF0EA80A7AB7A42";
    record Source(string Purpose, string Rom, string Sha256);
    static string P(string root) => Path.Combine(root, "Proyecto");
    static string E(string root) => Path.Combine(root, "evidence");
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static string HashFile(string path) { NoLinks(path); using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    static void Check(bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); Console.WriteLine("PASS " + reason); }
    static void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    static T Load<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path))!;
    static void NoLinks(string path)
    {
        for (var d = new DirectoryInfo(Path.GetFullPath(path)); d is not null; d = d.Parent)
            Check((d.Attributes & FileAttributes.ReparsePoint) == 0, "path_not_link_" + d.Name);
    }
    static string Target(string root)
    {
        string name = Path.GetFileName(Path.GetFullPath(root));
        if (name.StartsWith("PruebasPersistentItems-X-", StringComparison.Ordinal)) return "0004000000055D00";
        if (name.StartsWith("PruebasPersistentItems-Y-", StringComparison.Ordinal)) return "0004000000055E00";
        throw new InvalidDataException("Use a new explicit PruebasPersistentItems-X- or PruebasPersistentItems-Y- folder");
    }
    static string Root(string root)
    {
        string title = Target(root);
        NoLinks(root);
        NoLinks(P(root)); NoLinks(E(root));
        Check(Load<Source>(Path.Combine(E(root), "source.json")).Purpose == Purpose, "research_marker");
        var header = File.ReadAllBytes(Path.Combine(P(root), "HeaderNCCH0.bin"));
        var ex = File.ReadAllBytes(Path.Combine(P(root), "DecryptedExHeader.bin"));
        Check(header.Length >= 0x120 && header.AsSpan(0x100, 4).SequenceEqual("NCCH"u8)
            && BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(0x118)) == Convert.ToUInt64(title, 16)
            && ex.Length >= 0x208 && BinaryPrimitives.ReadUInt64LittleEndian(ex.AsSpan(0x200)) == Convert.ToUInt64(title, 16), "real_headers_match_explicit_target");
        using var profile = JsonDocument.Parse(typeof(Program).Assembly.GetManifestResourceStream("ObservedProfiles")!);
        foreach (var dependency in profile.RootElement.GetProperty("titles").GetProperty(title).GetProperty("sha256").EnumerateObject())
        {
            string actual = HashFile(Path.Combine(P(root), dependency.Name));
            Check(actual == dependency.Value.GetString() || (dependency.Name == "ExtractedRomFS/DllBattle.cro" && actual == CandidateCro),
                "pinned_dependency_" + title + "_" + dependency.Name);
        }
        return title;
    }
    static Dictionary<string, string> Inventory(string folder) => new[] { "ExtractedRomFS", "ExtractedExeFS" }
        .SelectMany(name => Directory.EnumerateFiles(Path.Combine(folder, name), "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        .ToDictionary(path => Path.GetRelativePath(folder, path).Replace('\\', '/'), HashFile, StringComparer.Ordinal);
    static void VerifySource(string root)
    {
        var source = Load<Source>(Path.Combine(E(root), "source.json"));
        Check(HashFile(source.Rom) == source.Sha256, "original_ROM_unchanged");
    }
    static void Extract(string rom, string root)
    {
        rom = Path.GetFullPath(rom); root = Path.GetFullPath(root);
        _ = Target(root);
        Check(!File.Exists(root) && !Directory.Exists(root) && !rom.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "destination_new_and_not_source");
        NoLinks(Path.GetDirectoryName(root)!); NoLinks(Path.GetDirectoryName(rom)!);
        Check((File.GetAttributes(rom) & FileAttributes.ReparsePoint) == 0, "source_ROM_not_link");
        Directory.CreateDirectory(E(root));
        var result = new GameRomExtractionService().ExtractAsync(rom, P(root)).GetAwaiter().GetResult();
        Directory.Move(Path.Combine(P(root), "RomFS"), Path.Combine(P(root), "ExtractedRomFS"));
        Directory.Move(Path.Combine(P(root), "ExeFS"), Path.Combine(P(root), "ExtractedExeFS"));
        File.Copy(Path.Combine(P(root), "ncchheader.bin"), Path.Combine(P(root), "HeaderNCCH0.bin"));
        File.Copy(Path.Combine(P(root), "exheader.bin"), Path.Combine(P(root), "DecryptedExHeader.bin"));
        Save(Path.Combine(E(root), "source.json"), new Source(Purpose, rom, result.SourceSha256));
        Root(root);
        Save(Path.Combine(E(root), "baseline-files.json"), Inventory(P(root)));
        VerifySource(root);
        Console.WriteLine("EXTRACTED " + P(root));
    }
    static void VerifyInventory(string root, string? patchedSha = null)
    {
        var baseline = Load<Dictionary<string, string>>(Path.Combine(E(root), "baseline-files.json"));
        var actual = Inventory(P(root));
        Check(actual.Keys.Order(StringComparer.Ordinal).SequenceEqual(baseline.Keys.Order(StringComparer.Ordinal)), "physical_file_names_unchanged");
        foreach (var pair in baseline)
        {
            string expected = pair.Key == "ExtractedRomFS/DllBattle.cro" && patchedSha is not null ? patchedSha : pair.Value;
            Check(actual[pair.Key] == expected, "exact_resource_" + pair.Key);
        }
    }
    static void Stage(string root, string experiment)
    {
        string title = Root(root); VerifySource(root); NoLinks(experiment);
        foreach (string asset in new[] { "experiment.json", "original/DllBattle.cro", "patched/DllBattle.cro" }) NoLinks(Path.Combine(experiment, asset));
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(experiment, "experiment.json")));
        var m = doc.RootElement;
        Check(m.GetProperty("purpose").GetString() == Purpose && m.GetProperty("title_id").GetString() == title
            && !m.GetProperty("production_enabled").GetBoolean(), "matching_title_disabled_experiment");
        string baseline = m.GetProperty("baseline_sha256").GetString()!, patchedHash = m.GetProperty("patched_sha256").GetString()!;
        Check(baseline == "9C07A3342DFBA4ADFC3569271D65FE9FAD88F1E271D5BA650179377E60D0A5A1" && patchedHash == CandidateCro, "independently_pinned_CRO_pair");
        var original = File.ReadAllBytes(Path.Combine(experiment, "original", "DllBattle.cro"));
        var proposed = File.ReadAllBytes(Path.Combine(experiment, "patched", "DllBattle.cro"));
        Check(Hash(original) == baseline && Hash(proposed) == patchedHash && original.Length == proposed.Length, "experiment_asset_hashes");
        Check(CroIntegrity.Rebuild(proposed).AsSpan().SequenceEqual(proposed), "CRO_integrity_valid");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(0x14854)) == 0x1A000020
            && BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(0x148AC)) == 0x0A000025
            && BinaryPrimitives.ReadUInt32LittleEndian(proposed.AsSpan(0x14854)) == 0x8A000020
            && BinaryPrimitives.ReadUInt32LittleEndian(proposed.AsSpan(0x148AC)) == 0xEA000025, "two_native_conditions");
        Check(Enumerable.Range(0, original.Length).All(i => original[i] == proposed[i]
            || i < 0x80 || i == 0x14857 || i == 0x148AF), "only_condition_bytes_and_integrity_fields");
        string path = Path.Combine(P(root), "ExtractedRomFS", "DllBattle.cro");
        byte[] current = File.ReadAllBytes(path);
        if (current.AsSpan().SequenceEqual(proposed)) { VerifyInventory(root, patchedHash); Console.WriteLine("IDEMPOTENT"); return; }
        Check(current.AsSpan().SequenceEqual(original), "project_still_has_exact_baseline");
        VerifyInventory(root);
        var project = GameProjectSession.Open(P(root), false);
        void Commit() => project.History.Commit([new FileChange(path, original, proposed)], "Persistent Items " + (title.EndsWith("5D00", StringComparison.Ordinal) ? "X" : "Y") + " · experimento",
            "BNE→BHI y BEQ→B", [new BatchChange(1, "DllBattle.cro", "ARM", "2 condiciones originales", "2 condiciones experimentales")],
            new Dictionary<string, string> { ["ActionId"] = "research.xy.persistent-items.two-gates", ["Scope"] = "ExperimentalCopyOnly", ["Gameplay"] = "pending" });
        Commit();
        var first = project.History.Read().Entries.Last();
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(proposed), "transaction_readback");
        RestorePoints.Undo(project.History, project.ProjectRoot, first.Id);
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(original), "exact_history_rollback");
        VerifyInventory(root);
        Commit();
        var final = project.History.Read().Entries.Last();
        Save(Path.Combine(E(root), "patch-entry.json"), final);
        File.Copy(Path.Combine(experiment, "experiment.json"), Path.Combine(E(root), "experiment.json"), overwrite: false);
        VerifyInventory(root, patchedHash); VerifySource(root);
    }
    static void Build(string root, string image)
    {
        Root(root); VerifySource(root);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(E(root), "experiment.json")));
        VerifyInventory(root, doc.RootElement.GetProperty("patched_sha256").GetString());
        var project = GameProjectSession.Open(P(root), false);
        var result = new GameRomRebuildService(project).BuildAsync(RomRebuildKind.Trimmed3DS, image).GetAwaiter().GetResult();
        Save(Path.Combine(E(root), "build.json"), new { result.File, result.Hash, result.Bytes });
        VerifySource(root); Console.WriteLine("BUILT " + result.File);
    }
    static void Verify(string root, string image, string output)
    {
        Root(root);
        using var build = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(E(root), "build.json")));
        Check(HashFile(image) == build.RootElement.GetProperty("Hash").GetString(), "candidate_image_hash");
        new GameRomExtractionService(P(root)).ExtractAsync(image, output).GetAwaiter().GetResult();
        int count = 0;
        foreach (var (source, target) in new[] { ("ExtractedRomFS", "RomFS"), ("ExtractedExeFS", "ExeFS") })
        {
            string a = Path.Combine(P(root), source), b = Path.Combine(output, target);
            string[] files = Directory.GetFiles(a, "*", SearchOption.AllDirectories);
            Check(files.Length == Directory.GetFiles(b, "*", SearchOption.AllDirectories).Length, "reextracted_file_count_" + target);
            foreach (var file in files) { Check(HashFile(file) == HashFile(Path.Combine(b, Path.GetRelativePath(a, file))), "roundtrip_" + Path.GetRelativePath(a, file)); count++; }
        }
        Check(Directory.GetFiles(Path.Combine(output, "RomFS"), "*", SearchOption.TopDirectoryOnly).Count(f => Path.GetFileName(f).Equals("DllBattle.cro", StringComparison.Ordinal)) == 1,
            "physical_DllBattle_name_preserved");
        Save(Path.Combine(E(root), "rebuild-verification.json"), new { Files = count, AllExact = true, ProductionEnabled = false, GameplayVerified = false });
        VerifySource(root);
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "extract") Extract(args[1], args[2]);
            else if (args.Length == 3 && args[0] == "stage") Stage(args[1], args[2]);
            else if (args.Length == 3 && args[0] == "build") Build(args[1], args[2]);
            else if (args.Length == 4 && args[0] == "verify") Verify(args[1], args[2], args[3]);
            else { Console.Error.WriteLine("extract SOURCE_ROM NEW_PruebasPersistentItems-X_OR_Y_ROOT | stage ROOT EXPERIMENT | build ROOT NEW_3DS | verify ROOT IMAGE NEW_EXTRACTED"); return 2; }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
