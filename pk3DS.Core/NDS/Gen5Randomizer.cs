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

/// <summary>Items are only shuffled: the set of items in the game never changes, and TMs, HMs and key items stay in place.</summary>
public enum Gen5ItemMode { Keep, Shuffle }

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
    public Gen5ItemMode FieldItems { get; init; }
    public Gen5ItemMode Shops { get; init; }
    public IReadOnlyList<Gen5BstRange> BstRanges { get; init; } = Gen5BstRange.Defaults;
}

public sealed record Gen5Change(string Location, string Field, int Before, int After);

public sealed record Gen5RandomizerResult(IReadOnlyDictionary<string, byte[]> Files, IReadOnlyList<Gen5Change> Changes,
    IReadOnlyDictionary<int, byte[]> Overlays)
{
    /// <summary>Overlays written BLZ-compressed again (too large to store uncompressed); others are written uncompressed.</summary>
    public IReadOnlySet<int> RecompressOverlays { get; init; } = new HashSet<int>();
}

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
        var stats = data.BaseStats();
        var Pick = SpeciesPicker(stats, MaxSpecies, options);
        var files = new Dictionary<string, byte[]>();
        var changes = new List<Gen5Change>();

        // One working copy per table, shared by every step, so steps that touch the same table do not overwrite each other.
        var working = new Dictionary<string, Narc>();
        Narc Work(string key) => working.TryGetValue(key, out var narc) ? narc : working[key] = data.CopyNarc(key);
        if (options.Trainers != Gen5SpeciesMode.Keep)
            RandomizeTrainers(data, options, Pick, Work, changes);
        if (options.Wild != Gen5SpeciesMode.Keep)
            RandomizeWild(data, options, Pick, Work, changes);
        var overlays = new Dictionary<int, byte[]>();
        if (options.Starters != Gen5StarterMode.Keep)
            SetStarters(data, options, stats, Work, overlays, changes);
        if (options.FieldItems == Gen5ItemMode.Shuffle)
            ShuffleFieldItems(data, options.Seed, Work, changes);
        if (options.Shops == Gen5ItemMode.Shuffle)
            ShuffleShops(data, options.Seed, Work, overlays, changes);
        foreach (var (key, narc) in working)
            files[data.PathOf(key)] = narc.Write();
        return new Gen5RandomizerResult(files, changes, overlays)
        {
            RecompressOverlays = overlays.Keys.Where(id => id == data.Profile.ShopOverlay
                || (id == data.Profile.StarterCryOverlay && data.Profile.RecompressStarterCryOverlay)).ToHashSet(),
        };
    }

    /// <summary>
    /// Species choice shared by the Gen4 and Gen5 randomizers: (mode, slot key, original species, level) → new species among 1..<paramref name="maxSpecies"/>.
    /// </summary>
    internal static Func<Gen5SpeciesMode, string, int, int, int> SpeciesPicker(IReadOnlyList<Gen5BaseStats> stats, int maxSpecies, Gen5RandomizerOptions options)
    {
        foreach (var range in options.BstRanges)
            if (range.MinLevel < 1 || range.MaxLevel > 100 || range.MinLevel > range.MaxLevel || range.MinBst < 0 || range.MinBst > range.MaxBst)
                throw new ArgumentException("Rango de BST inválido.", nameof(options));
        if (stats.Count <= maxSpecies)
            throw new InvalidDataException($"La tabla de estadísticas no cubre las {maxSpecies} especies.");
        var pool = Enumerable.Range(1, maxSpecies).Select(s => (Species: s, Bst: stats[s].Total)).ToArray();

        return (mode, key, original, level) =>
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
        };
    }

    private static void RandomizeTrainers(Gen5Data data, Gen5RandomizerOptions options, Func<Gen5SpeciesMode, string, int, int, int> pick,
        Func<string, Narc> work, List<Gen5Change> changes)
    {
        var headers = work(Gen5Profile.TrainerData);
        var teams = work(Gen5Profile.TrainerPokemon);
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
    }

    private static void RandomizeWild(Gen5Data data, Gen5RandomizerOptions options, Func<Gen5SpeciesMode, string, int, int, int> pick,
        Func<string, Narc> work, List<Gen5Change> changes)
    {
        var wild = work(Gen5Profile.WildPokemon);
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
    }

    /// <summary>
    /// Starters across every place the game uses them: script positions, the Pokédex script fix, the selection-screen
    /// sprites and palettes, the cry table in overlay 223 and the type named in the selection text. Every guard runs before
    /// anything is returned, so an unexpected layout rejects the whole operation.
    /// </summary>
    private static void SetStarters(Gen5Data data, Gen5RandomizerOptions options, IReadOnlyList<Gen5BaseStats> stats,
        Func<string, Narc> work, Dictionary<int, byte[]> overlays, List<Gen5Change> changes)
    {
        int[] chosen = options.Starters == Gen5StarterMode.Custom
            ? (options.CustomStarters ?? throw new ArgumentException("Faltan los iniciales personalizados.", nameof(options))).ToArray()
            : PickStarters(options.Seed);
        if (chosen.Length != 3 || chosen.Distinct().Count() != 3 || chosen.Any(s => s is < 1 or > MaxSpecies))
            throw new ArgumentException("Se necesitan tres iniciales distintos entre 1 y 649.", nameof(options));
        var original = data.Starters();

        var scripts = work(Gen5Profile.Scripts);
        for (int i = 0; i < 3; i++)
            foreach (var (file, offset) in data.Profile.StarterOffsets[i])
                BinaryPrimitives.WriteUInt16LittleEndian(scripts.Files[file].AsSpan(offset), (ushort)chosen[i]);
        // The displaced instruction is replayed by the appended code (UPR-ZX bw1/bw2NewStarterScript, GPLv3).
        var profile = data.Profile;
        byte[] dex = scripts.Files[profile.PokedexGivenScript];
        int at = Find(dex, [.. profile.PokedexSignature], "la firma del script del Pokédex");
        byte[] patched = [.. dex, .. profile.PokedexPatch];
        patched[at] = profile.PokedexJump;
        patched[at + 1] = 0x00;
        BinaryPrimitives.WriteInt32LittleEndian(patched.AsSpan(at + 2), dex.Length - (at + 2 + 4));
        scripts.Files[profile.PokedexGivenScript] = patched;

        var graphics = data.CopyNarc(Gen5Profile.PokemonGraphics);
        var selection = work(Gen5Profile.StarterGraphics);
        for (int i = 0; i < 3; i++)
        {
            byte[] palette = graphics.Files[chosen[i] * 20 + 18];
            byte[] sprite = Lz11(graphics.Files[chosen[i] * 20]);
            if (!palette.AsSpan(0, 4).SequenceEqual("RLCN"u8) || !sprite.AsSpan(0, 4).SequenceEqual("RGCN"u8) || sprite.Length != selection.Files[12 + i].Length)
                throw new InvalidDataException($"Los gráficos de la especie {chosen[i]} no tienen el formato de la pantalla de elección.");
            selection.Files[i * 2] = palette;
            selection.Files[12 + i] = sprite;
        }

        byte[] cries = data.StarterCryOverlay();
        int table = Find(cries, [.. profile.StarterCryPrefix], "la tabla de gritos de los iniciales") + profile.StarterCryPrefix.Count;
        for (int i = 0; i < 3; i++)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(cries.AsSpan(table + i * 2)) != original[i])
                throw new InvalidDataException("La tabla de gritos de los iniciales no contiene los iniciales originales.");
            BinaryPrimitives.WriteUInt16LittleEndian(cries.AsSpan(table + i * 2), (ushort)chosen[i]);
        }
        overlays[profile.StarterCryOverlay] = cries;

        // Names come from a text variable; only the type is written into each selection line.
        var types = data.Text(profile.TypeNamesText);
        var story = work(Gen5Profile.TextStory);
        byte[] storyFile = story.Files[profile.StarterStoryText];
        var lines = Gen5Text.Read(storyFile);
        var edits = new Dictionary<int, string>();
        for (int i = 0; i < 3; i++)
        {
            int line = profile.StarterTypeLines[i];
            string before = types[stats[original[i]].Type1], after = types[stats[chosen[i]].Type1];
            if (CountOf(lines[line], before) != 1)
                throw new InvalidDataException($"La línea de texto del inicial {i + 1} no contiene el tipo {before} una sola vez.");
            edits[line] = lines[line].Replace(before, after, StringComparison.Ordinal);
        }
        story.Files[profile.StarterStoryText] = Gen5Text.Replace(storyFile, edits);

        for (int i = 0; i < 3; i++)
            if (chosen[i] != original[i])
                changes.Add(new Gen5Change($"Inicial {i + 1}", "Especie", original[i], chosen[i]));
    }

    /// <summary>
    /// TMs, HMs and key items (ids read from Black's and White 2's own item names) never move; neither do unknown ids.
    /// Every sequel-only item from 621 on is a key item.
    /// </summary>
    public static bool IsMovableItem(int item) => item is >= 1 and <= 627
        && item is not (>= 120 and <= 133) and not (>= 328 and <= 425) and not (>= 428 and <= 503) and not (>= 505 and <= 536)
        and not 574 and not 578 and not 579 and not 616 and not 617 and not (>= 618 and <= 620) and not (>= 621);

    private static void ShuffleFieldItems(Gen5Data data, uint seed, Func<string, Narc> work, List<Gen5Change> changes)
    {
        var slots = data.FieldItems().Where(s => IsMovableItem(s.Item)).ToArray();
        int[] items = Shuffle(slots.Select(s => s.Item).ToArray(), seed, "field");
        var scripts = work(Gen5Profile.Scripts);
        for (int i = 0; i < slots.Length; i++)
        {
            var (file, offset, before) = slots[i];
            BinaryPrimitives.WriteUInt16LittleEndian(scripts.Files[file].AsSpan(offset), (ushort)items[i]);
            if (items[i] != before)
                changes.Add(new Gen5Change($"{(file == data.Profile.ItemBallScript ? "Objeto visible" : "Objeto oculto")} {i + 1}", "Objeto", before, items[i]));
        }
    }

    private static void ShuffleShops(Gen5Data data, uint seed, Func<string, Narc> work, Dictionary<int, byte[]> overlays, List<Gen5Change> changes)
    {
        var profile = data.Profile;
        var shops = data.Shops();
        var slots = new List<(int Shop, int Index, int Item)>();
        for (int shop = 0; shop < shops.Count; shop++)
            if (!profile.FixedShops.Contains(shop))
                for (int i = 0; i < shops[shop].Count; i++)
                    if (IsMovableItem(shops[shop][i]))
                        slots.Add((shop, i, shops[shop][i]));
        int[] items = Shuffle(slots.Select(s => s.Item).ToArray(), seed, "shop");
        // Sequels keep one list per file of the shop NARC; Black/White keep them all in one overlay.
        byte[]? overlay = profile.ShopOverlay >= 0 ? data.ShopOverlay() : null;
        var narc = overlay is null ? work(Gen5Profile.ShopItems) : null;
        for (int i = 0; i < slots.Count; i++)
        {
            var (shop, index, before) = slots[i];
            var span = overlay is not null ? overlay.AsSpan(profile.ShopItemOffsets[shop] + index * 2) : narc!.Files[shop].AsSpan(index * 2);
            BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)items[i]);
            if (items[i] != before)
                changes.Add(new Gen5Change($"Tienda {shop} · producto {index + 1}", "Objeto", before, items[i]));
        }
        if (overlay is not null)
            overlays[profile.ShopOverlay] = overlay;
    }

    // Fisher-Yates with the same seeded index as every other choice.
    private static int[] Shuffle(int[] values, uint seed, string key)
    {
        var result = (int[])values.Clone();
        for (int i = result.Length - 1; i > 0; i--)
        {
            int j = Index(seed, $"{key}/{i}", i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
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
    internal static int Index(uint seed, string key, int count)
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
