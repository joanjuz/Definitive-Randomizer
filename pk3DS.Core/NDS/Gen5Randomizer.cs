#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace pk3DS.Core.NDS;

public enum Gen5SpeciesMode { Keep, Random, SimilarBst, ProgressiveBst }

public enum Gen5StarterMode { Keep, Random, Custom }

/// <summary>Species allowed for a level band in progressive mode. Defaults follow the Switch ranges as a design choice, not shared data.</summary>
public sealed record Gen5BstRange(int MinLevel, int MaxLevel, int MinBst, int MaxBst)
{
    public static IReadOnlyList<Gen5BstRange> Defaults { get; } = Array.AsReadOnly(new[]
    {
        new Gen5BstRange(1, 10, 180, 320), new(11, 20, 220, 380), new(21, 30, 280, 450),
        new(31, 40, 340, 520), new(41, 50, 400, 580), new(51, 100, 480, 680),
    });
}

public sealed record Gen5RandomizerOptions(uint Seed)
{
    public Gen5SpeciesMode Trainers { get; init; }
    public Gen5SpeciesMode Wild { get; init; }
    public Gen5StarterMode Starters { get; init; }
    /// <summary>Three distinct species (Grass, Fire and Water slots, in that order) for <see cref="Gen5StarterMode.Custom"/>.</summary>
    public IReadOnlyList<int>? CustomStarters { get; init; }
    public IReadOnlyList<Gen5BstRange> BstRanges { get; init; } = Gen5BstRange.Defaults;
}

public sealed record Gen5Change(string Location, string Field, int Before, int After);

public sealed record Gen5RandomizerResult(IReadOnlyDictionary<string, byte[]> Files, IReadOnlyList<Gen5Change> Changes,
    IReadOnlyDictionary<int, byte[]> Overlays);

/// <summary>
/// Black/White trainer and wild species randomizer. Choices depend only on the seed and the slot, so a result is reproducible.
/// Output is a set of rebuilt NARCs keyed by ROM path; nothing is written until <see cref="Gen5Data.Export"/>.
/// </summary>
public static class Gen5Randomizer
{
    public const int MaxSpecies = 649;
    private const int SeasonLength = 232;
    private static readonly int[] SlotsPerType = [12, 12, 12, 5, 5, 5, 5];

    public static Gen5RandomizerResult Generate(Gen5Data data, Gen5RandomizerOptions options)
    {
        foreach (var range in options.BstRanges)
            if (range.MinLevel < 1 || range.MaxLevel > 100 || range.MinLevel > range.MaxLevel || range.MinBst < 0 || range.MinBst > range.MaxBst)
                throw new ArgumentException("Rango de BST inválido.", nameof(options));
        var stats = data.BaseStats();
        if (stats.Count <= MaxSpecies)
            throw new InvalidDataException("La tabla de estadísticas no cubre las 649 especies de Gen5.");
        var pool = Enumerable.Range(1, MaxSpecies).Select(s => (Species: s, Bst: stats[s].Total)).ToArray();
        var files = new Dictionary<string, byte[]>();
        var changes = new List<Gen5Change>();

        int Pick(Gen5SpeciesMode mode, string key, int original, int level)
        {
            var candidates = pool;
            if (mode == Gen5SpeciesMode.SimilarBst)
            {
                int bst = stats[original].Total, tolerance = Math.Max(1, bst / 10);
                // Widen until at least a few species qualify, so rare BSTs do not always map to themselves.
                while ((candidates = pool.Where(p => Math.Abs(p.Bst - bst) <= tolerance).ToArray()).Length < 5)
                    tolerance += Math.Max(1, bst / 20);
            }
            else if (mode == Gen5SpeciesMode.ProgressiveBst)
            {
                var range = options.BstRanges.FirstOrDefault(r => level >= r.MinLevel && level <= r.MaxLevel)
                    ?? throw new InvalidDataException("Ningún rango de BST cubre el nivel " + level + ".");
                candidates = pool.Where(p => p.Bst >= range.MinBst && p.Bst <= range.MaxBst).ToArray();
                if (candidates.Length == 0)
                    throw new InvalidDataException($"Ninguna especie tiene BST {range.MinBst}–{range.MaxBst} para el nivel {level}.");
            }
            return candidates[Index(options.Seed, key, candidates.Length)].Species;
        }

        if (options.Trainers != Gen5SpeciesMode.Keep)
            RandomizeTrainers(data, options, Pick, files, changes);
        if (options.Wild != Gen5SpeciesMode.Keep)
            RandomizeWild(data, options, Pick, files, changes);
        var overlays = new Dictionary<int, byte[]>();
        if (options.Starters != Gen5StarterMode.Keep)
            SetStarters(data, options, stats, files, overlays, changes);
        return new Gen5RandomizerResult(files, changes, overlays);
    }

    private static void RandomizeTrainers(Gen5Data data, Gen5RandomizerOptions options, Func<Gen5SpeciesMode, string, int, int, int> pick,
        Dictionary<string, byte[]> files, List<Gen5Change> changes)
    {
        var headers = data.CopyNarc(Gen5Profile.TrainerData);
        var teams = data.CopyNarc(Gen5Profile.TrainerPokemon);
        foreach (var trainer in data.Trainers())
        {
            string location = $"Entrenador {trainer.Id} · {trainer.ClassName} {trainer.Name}".TrimEnd();
            // New species cannot keep moves picked for the old one: drop custom moves so the game uses level-up moves.
            int flags = trainer.Flags & ~1;
            var team = new List<byte>();
            for (int slot = 0; slot < trainer.Team.Count; slot++)
            {
                var p = trainer.Team[slot];
                int species = pick(options.Trainers, $"trainer/{trainer.Id}/{slot}", p.Species, p.Level);
                if (species != p.Species)
                    changes.Add(new Gen5Change($"{location} · Pokémon {slot + 1} · nivel {p.Level}", "Especie", p.Species, species));
                byte[] entry = new byte[8 + (trainer.HasItems ? 2 : 0)];
                entry[0] = (byte)p.Difficulty;
                entry[1] = (byte)((p.AbilitySlot << 4) | p.GenderFlag);
                BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(2), (ushort)p.Level);
                BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(4), (ushort)species);
                // Forms belong to the old species; form 0 always exists.
                BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(6), 0);
                if (trainer.HasItems)
                    BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(8), (ushort)p.HeldItem);
                team.AddRange(entry);
            }
            if (flags != trainer.Flags)
            {
                changes.Add(new Gen5Change(location, "Movimientos personalizados", 1, 0));
                headers.Files[trainer.Id] = (byte[])headers.Files[trainer.Id].Clone();
                headers.Files[trainer.Id][0] = (byte)flags;
            }
            // Keep any bytes after the team (padding in some entries) as they were.
            byte[] original = teams.Files[trainer.Id];
            int originalTeam = trainer.Team.Count * (8 + (trainer.HasItems ? 2 : 0) + (trainer.HasMoves ? 8 : 0));
            teams.Files[trainer.Id] = team.Concat(original.Skip(originalTeam)).ToArray();
        }
        files[data.PathOf(Gen5Profile.TrainerData)] = headers.Write();
        files[data.PathOf(Gen5Profile.TrainerPokemon)] = teams.Write();
    }

    private static void RandomizeWild(Gen5Data data, Gen5RandomizerOptions options, Func<Gen5SpeciesMode, string, int, int, int> pick,
        Dictionary<string, byte[]> files, List<Gen5Change> changes)
    {
        var wild = data.CopyNarc(Gen5Profile.WildPokemon);
        for (int file = 0; file < wild.Files.Count; file++)
        {
            byte[] table = (byte[])wild.Files[file].Clone();
            for (int season = 0; season < table.Length / SeasonLength; season++)
            {
                int start = season * SeasonLength, slot = start + 8;
                for (int type = 0; type < SlotsPerType.Length; type++)
                {
                    bool used = table[start + type] != 0;
                    for (int i = 0; used && i < SlotsPerType[type]; i++)
                    {
                        int at = slot + i * 4;
                        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(at));
                        int species = value & 0x7FF;
                        if (species is < 1 or > MaxSpecies)
                            continue;
                        // Same original species in one area maps to the same new species, keeping the area's rates.
                        int replacement = pick(options.Wild, $"wild/{file}/{season}/{type}/{species}", species, table[at + 3]);
                        BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(at), (ushort)replacement);
                        if (replacement != species)
                            changes.Add(new Gen5Change($"Encuentros {file} · estación {season + 1} · {Gen5EncounterArea.TypeNames[type]} · hueco {i + 1}", "Especie", species, replacement));
                    }
                    slot += SlotsPerType[type] * 4;
                }
            }
            wild.Files[file] = table;
        }
        files[data.PathOf(Gen5Profile.WildPokemon)] = wild.Write();
    }

    private static readonly byte[] StarterScriptSignature = [0x24, 0x00, 0xA7, 0x02, 0xE7, 0x00, 0x00, 0x00];
    // Replays the two instructions displaced by the 6-byte call, adds two commands and returns (UPR-ZX bw1NewStarterScript, GPLv3).
    private static readonly byte[] StarterScriptPatch = [0x24, 0x00, 0xA7, 0x02, 0xE7, 0x00, 0x00, 0x00, 0xDE, 0x00, 0x00, 0x00, 0xF8, 0x01, 0x05, 0x00];
    private static readonly byte[] StarterCryPrefix = [0x08, 0x0A, 0x07, 0x00, 0x08, 0x00, 0x00];

    /// <summary>
    /// Starters across every place the game uses them: script positions, the Pokédex script fix, the selection-screen
    /// sprites and palettes, the cry table in overlay 223 and the type named in the selection text. Every guard runs before
    /// anything is returned, so an unexpected layout rejects the whole operation.
    /// </summary>
    private static void SetStarters(Gen5Data data, Gen5RandomizerOptions options, IReadOnlyList<Gen5BaseStats> stats,
        Dictionary<string, byte[]> files, Dictionary<int, byte[]> overlays, List<Gen5Change> changes)
    {
        int[] chosen = options.Starters == Gen5StarterMode.Custom
            ? (options.CustomStarters ?? throw new ArgumentException("Faltan los iniciales personalizados.", nameof(options))).ToArray()
            : PickStarters(options.Seed);
        if (chosen.Length != 3 || chosen.Distinct().Count() != 3 || chosen.Any(s => s is < 1 or > MaxSpecies))
            throw new ArgumentException("Se necesitan tres iniciales distintos entre 1 y 649.", nameof(options));
        var original = data.Starters();

        var scripts = data.CopyNarc(Gen5Profile.Scripts);
        for (int i = 0; i < 3; i++)
            foreach (var (file, offset) in data.Profile.StarterOffsets[i])
                BinaryPrimitives.WriteUInt16LittleEndian(scripts.Files[file].AsSpan(offset), (ushort)chosen[i]);
        byte[] dex = scripts.Files[Gen5Profile.PokedexGivenScript];
        int at = Find(dex, StarterScriptSignature, "la firma del script del Pokédex");
        byte[] patched = [.. dex, .. StarterScriptPatch];
        patched[at] = 0x04;
        patched[at + 1] = 0x00;
        BinaryPrimitives.WriteInt32LittleEndian(patched.AsSpan(at + 2), dex.Length - (at + 2 + 4));
        scripts.Files[Gen5Profile.PokedexGivenScript] = patched;
        files[data.PathOf(Gen5Profile.Scripts)] = scripts.Write();

        var graphics = data.CopyNarc(Gen5Profile.PokemonGraphics);
        var selection = data.CopyNarc(Gen5Profile.StarterGraphics);
        for (int i = 0; i < 3; i++)
        {
            byte[] palette = graphics.Files[chosen[i] * 20 + 18];
            byte[] sprite = Lz11(graphics.Files[chosen[i] * 20]);
            if (!palette.AsSpan(0, 4).SequenceEqual("RLCN"u8) || !sprite.AsSpan(0, 4).SequenceEqual("RGCN"u8) || sprite.Length != selection.Files[12 + i].Length)
                throw new InvalidDataException($"Los gráficos de la especie {chosen[i]} no tienen el formato de la pantalla de elección.");
            selection.Files[i * 2] = palette;
            selection.Files[12 + i] = sprite;
        }
        files[data.PathOf(Gen5Profile.StarterGraphics)] = selection.Write();

        byte[] cries = data.StarterCryOverlay();
        int table = Find(cries, StarterCryPrefix, "la tabla de gritos de los iniciales") + StarterCryPrefix.Length;
        for (int i = 0; i < 3; i++)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(cries.AsSpan(table + i * 2)) != original[i])
                throw new InvalidDataException("La tabla de gritos de los iniciales no contiene los iniciales originales.");
            BinaryPrimitives.WriteUInt16LittleEndian(cries.AsSpan(table + i * 2), (ushort)chosen[i]);
        }
        overlays[Gen5Profile.StarterCryOverlay] = cries;

        // Names come from a text variable; only the type is written into each selection line.
        var types = data.Text(Gen5Profile.TypeNamesText);
        var story = data.CopyNarc(Gen5Profile.TextStory);
        byte[] storyFile = story.Files[Gen5Profile.StarterStoryText];
        var lines = Gen5Text.Read(storyFile);
        var edits = new Dictionary<int, string>();
        for (int i = 0; i < 3; i++)
        {
            int line = Gen5Profile.StarterTypeLines[i];
            string before = types[stats[original[i]].Type1], after = types[stats[chosen[i]].Type1];
            if (CountOf(lines[line], before) != 1)
                throw new InvalidDataException($"La línea de texto del inicial {i + 1} no contiene el tipo {before} una sola vez.");
            edits[line] = lines[line].Replace(before, after, StringComparison.Ordinal);
        }
        story.Files[Gen5Profile.StarterStoryText] = Gen5Text.Replace(storyFile, edits);
        files[data.PathOf(Gen5Profile.TextStory)] = story.Write();

        for (int i = 0; i < 3; i++)
            if (chosen[i] != original[i])
                changes.Add(new Gen5Change($"Inicial {i + 1}", "Especie", original[i], chosen[i]));
    }

    private static int[] PickStarters(uint seed)
    {
        var picked = new List<int>();
        for (int attempt = 0; picked.Count < 3; attempt++)
        {
            int species = Index(seed, $"starter/{attempt}", MaxSpecies) + 1;
            if (!picked.Contains(species))
                picked.Add(species);
        }
        return [.. picked];
    }

    private static int Find(byte[] data, byte[] pattern, string what)
    {
        int found = -1;
        for (int i = 0; i + pattern.Length <= data.Length; i++)
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                if (found >= 0)
                    throw new InvalidDataException($"Se encontró más de una vez {what}.");
                found = i;
            }
        return found >= 0 ? found : throw new InvalidDataException($"No se encontró {what}.");
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static byte[] Lz11(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var output = new MemoryStream();
        pk3DS.Core.CTR.LZSS.Decompress(input, compressed.Length, output);
        return output.ToArray();
    }

    // Deterministic per (seed, slot key): adding or skipping other slots never shifts a choice.
    private static int Index(uint seed, string key, int count)
    {
        ulong hash = 0xCBF29CE484222325;
        foreach (byte b in Encoding.UTF8.GetBytes(key))
            hash = (hash ^ b) * 0x100000001B3;
        ulong z = unchecked(hash ^ ((ulong)seed << 32) + 0x9E3779B97F4A7C15);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EB);
        z ^= z >> 31;
        return (int)(z % (uint)count);
    }
}
