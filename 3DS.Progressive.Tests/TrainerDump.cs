using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Switch;

/// <summary>
/// --trainer-dump: every trainer of a game (id, class, name, team species and levels) and the species names, read without modifying
/// the source. tools/story-chronology matches these teams with the WikiDex walkthrough to build the story chronology per game.
/// Sources: a 3DS extraction (romfs/a + exefs), a Nintendo DS ROM (.nds) or a Sword/Shield extraction (romfs/bin).
/// </summary>
internal static class TrainerDump
{
    private sealed record Pokemon(int Species, int Level);
    private sealed record Trainer(int Id, string Class, string Name, IReadOnlyList<Pokemon> Team);
    private sealed record Dump(string Game, IReadOnlyList<string> Species, IReadOnlyList<Trainer> Trainers);

    internal static void Run(string source, string output)
    {
        var dump = source.EndsWith(".nds", StringComparison.OrdinalIgnoreCase) ? Nds(source)
            : Directory.Exists(Path.Combine(source, "romfs", "bin", "trainer")) ? Switch(source) : ThreeDs(source);
        File.WriteAllText(output, JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = false }));
        Console.WriteLine($"Dumped {dump.Trainers.Count} trainers of {dump.Game} to {output}");
    }

    private static Dump Nds(string rom)
    {
        using var project = NdsProject.Open(rom, false);
        var trainers = project.Trainers().Select(t => new Trainer(t.Id, t.ClassName, t.Name, t.Team.Select(p => new Pokemon(p.Species, p.Level)).ToArray())).ToArray();
        return new(project.Game, project.SpeciesNames.ToArray(), trainers);
    }

    private static Dump Switch(string root)
    {
        // Teams straight from the extraction (read only); names through the project's text catalogs.
        var project = SwordShieldProject.Open(root);
        string folder = Path.Combine(root, "romfs", "bin", "trainer");
        var trainers = new List<Trainer>();
        foreach (string file in Directory.GetFiles(Path.Combine(folder, "trainer_poke"), "trainer_poke_*.bin").Order(StringComparer.Ordinal))
        {
            int id = int.Parse(Path.GetFileNameWithoutExtension(file)["trainer_poke_".Length..]);
            byte[] team = File.ReadAllBytes(file), header = File.ReadAllBytes(Path.Combine(folder, "trainer_data", $"trainer_data_{id:000}.bin"));
            var pokemon = Enumerable.Range(0, Math.Min(header[3], team.Length / 32))
                .Select(s => new Pokemon(BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(s * 32 + 12)), BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(s * 32 + 10)))).ToArray();
            trainers.Add(new(id, "", project.TrainerName(id), pokemon));
        }
        return new(project.Identity.Game, Enumerable.Range(0, 899).Select(project.SpeciesName).ToArray(), trainers);
    }

    private static Dump ThreeDs(string root)
    {
        string romfs = Directory.GetDirectories(root).First(d => Path.GetFileName(d).Contains("rom", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(d, "a")));
        string exefs = Directory.GetDirectories(root).First(d => Path.GetFileName(d).Contains("exe", StringComparison.OrdinalIgnoreCase));
        int count = Directory.EnumerateFiles(Path.Combine(romfs, "a"), "*", SearchOption.AllDirectories).Count(f => Path.GetFileName(f).Length == 1);
        var config = new GameConfig(count);
        // Language 6 is Spanish (the species names WikiDex uses).
        config.Initialize(romfs, exefs, 6);
        var data = config.GetGARCData("trdata").Files;
        var poke = config.GetGARCData("trpoke").Files;
        string[] classes = config.GetText(TextName.TrainerClasses), names = config.GetText(TextName.TrainerNames), species = config.GetText(TextName.SpeciesNames);
        var trainers = new List<Trainer>();
        for (int id = 1; id < data.Length; id++)
        {
            IReadOnlyList<Pokemon> team;
            int classId;
            if (config.Generation == 7)
            {
                var t = new TrainerData7(data[id], poke[id]);
                classId = t.TrainerClass;
                team = t.Pokemon.Select(p => new Pokemon(p.Species, p.Level)).ToArray();
            }
            else
            {
                var t = new TrainerData6(data[id], poke[id], config.ORAS);
                classId = t.Class;
                team = t.Team.Select(p => new Pokemon(p.Species, p.Level)).ToArray();
            }
            trainers.Add(new(id, classId < classes.Length ? classes[classId] : "", id < names.Length ? names[id] : "", team));
        }
        return new(config.Version.ToString(), species, trainers);
    }
}
