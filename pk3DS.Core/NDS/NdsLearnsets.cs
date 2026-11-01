#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

public sealed record NdsLearnsetMove(int Move, int Level);

/// <summary>
/// Level-up moves of one species with any length, as the 3DS Level Up editor needs (add, remove, reorder by level). Each species
/// is its own file in the Learnsets NARC: Gen5 entries are a u16 move and a u16 level ending with FFFF FFFF; Gen4 entries are one
/// u16 (move in 9 bits, level in the top 7) ending with FFFF, and the files are padded to four bytes.
/// </summary>
public static class NdsLearnsets
{
    public static IReadOnlyList<NdsLearnsetMove> Parse(byte[] file, int generation)
    {
        var moves = new List<NdsLearnsetMove>();
        int size = generation == 5 ? 4 : 2;
        for (int at = 0; at + size <= file.Length; at += size)
        {
            int value = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at));
            if (value == 0xFFFF)
                break;
            moves.Add(generation == 5 ? new(value, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 2))) : new(value & 0x1FF, value >> 9));
        }
        return moves;
    }

    public static byte[] Encode(IReadOnlyList<NdsLearnsetMove> moves, int generation, int maxMove)
    {
        if (moves.Count > 100)
            throw new ArgumentException("No se admiten más de 100 movimientos por especie.");
        if (moves.Any(m => m.Level is < 1 or > 100 || m.Move < 1 || m.Move > (generation == 5 ? maxMove : Math.Min(maxMove, 0x1FF))))
            throw new ArgumentException("Cada movimiento debe tener un nivel 1–100 y un ID válido.");
        var ordered = moves.OrderBy(m => m.Level).ToArray();
        if (generation == 5)
        {
            var data = new byte[(ordered.Length + 1) * 4];
            for (int i = 0; i < ordered.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 4), (ushort)ordered[i].Move);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 4 + 2), (ushort)ordered[i].Level);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(ordered.Length * 4), 0xFFFFFFFF);
            return data;
        }
        var gen4 = new byte[(ordered.Length * 2 + 2 + 3) & ~3];
        for (int i = 0; i < ordered.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(gen4.AsSpan(i * 2), (ushort)(ordered[i].Move | ordered[i].Level << 9));
        BinaryPrimitives.WriteUInt16LittleEndian(gen4.AsSpan(ordered.Length * 2), 0xFFFF);
        return gen4;
    }

    public static IReadOnlyList<NdsLearnsetMove> Read(Gen5Data data, int species) => Parse(File(data.CopyNarc(Gen5Profile.Learnsets), species), 5);
    public static IReadOnlyList<NdsLearnsetMove> Read(Gen4Data data, int species) => Parse(File(data.CopyNarc(Gen4Profile.Learnsets), species), 4);

    /// <summary>Every species of a Learnsets NARC (as written in a randomizer result), for previews.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<NdsLearnsetMove>> ParseAll(byte[] narc, int generation, int maxSpecies)
    {
        var files = Narc.Read(narc).Files;
        return Enumerable.Range(1, Math.Min(maxSpecies, files.Count - 1)).ToDictionary(s => s, s => Parse(files[s], generation));
    }

    public static Gen5RandomizerResult Replace(Gen5Data data, IReadOnlyDictionary<int, IReadOnlyList<NdsLearnsetMove>> sets) =>
        Replace(data.CopyNarc(Gen5Profile.Learnsets), data.PathOf(Gen5Profile.Learnsets), 5, NdsDataSource.Of(data).MoveNames.Count - 1, data.SpeciesNames(), sets);

    public static Gen5RandomizerResult Replace(Gen4Data data, IReadOnlyDictionary<int, IReadOnlyList<NdsLearnsetMove>> sets) =>
        Replace(data.CopyNarc(Gen4Profile.Learnsets), data.PathOf(Gen4Profile.Learnsets), 4, NdsDataSource.Of(data).MoveNames.Count - 1, data.SpeciesNames(), sets);

    private static Gen5RandomizerResult Replace(Narc narc, string path, int generation, int maxMove, IReadOnlyList<string> names,
        IReadOnlyDictionary<int, IReadOnlyList<NdsLearnsetMove>> sets)
    {
        var changes = new List<Gen5Change>();
        // The game loads a list into a fixed buffer: no list may be longer than the longest one the game already has.
        int limit = narc.Files.Skip(1).Max(f => Parse(f, generation).Count);
        foreach (var (species, moves) in sets.OrderBy(p => p.Key))
        {
            if (moves.Count > limit)
                throw new ArgumentException($"Este juego admite como máximo {limit} movimientos por nivel por especie (la lista más larga del juego original).");
            var before = Parse(File(narc, species), generation);
            narc.Files[species] = Encode(moves, generation, maxMove);
            var after = Parse(narc.Files[species], generation);
            string name = $"Level Up · {(species < names.Count ? names[species] : "#" + species)}";
            for (int i = 0; i < Math.Max(before.Count, after.Count); i++)
            {
                var b = i < before.Count ? before[i] : null;
                var a = i < after.Count ? after[i] : null;
                if (b?.Move != a?.Move)
                    changes.Add(new Gen5Change(name, $"Movimiento {i + 1}", b?.Move ?? 0, a?.Move ?? 0));
                if (b?.Level != a?.Level)
                    changes.Add(new Gen5Change(name, $"Nivel {i + 1}", b?.Level ?? 0, a?.Level ?? 0));
            }
        }
        return new Gen5RandomizerResult(new Dictionary<string, byte[]> { [path] = narc.Write() }, changes, new Dictionary<int, byte[]>());
    }

    private static byte[] File(Narc narc, int species) =>
        species >= 1 && species < narc.Files.Count ? narc.Files[species] : throw new ArgumentOutOfRangeException(nameof(species));
}
