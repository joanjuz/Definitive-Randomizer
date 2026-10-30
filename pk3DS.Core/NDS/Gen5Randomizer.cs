#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace pk3DS.Core.NDS;

public enum Gen5SpeciesMode { Keep, Random, SimilarBst, ProgressiveBst }

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
    public IReadOnlyList<Gen5BstRange> BstRanges { get; init; } = Gen5BstRange.Defaults;
}

public sealed record Gen5Change(string Location, string Field, int Before, int After);

public sealed record Gen5RandomizerResult(IReadOnlyDictionary<string, byte[]> Files, IReadOnlyList<Gen5Change> Changes);

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
        return new Gen5RandomizerResult(files, changes);
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
