#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace pk3DS.Core.NX;

public sealed record SwordShieldLearnMove(int Move, int Level);

/// <summary>
/// Whole level-up lists for the 3DS Level Up view. Every personal entry has a fixed 0x104-byte record in wazaoboe_total.bin:
/// 65 slots of u16 move + u16 level, the list ends with FFFF FFFF and the rest of the record is FF. The game reads the record at a
/// fixed offset, so a list may use every slot but the terminator (64 moves); level 0 is an evolution move.
/// </summary>
public static class SwordShieldLearnsets
{
    public const int Capacity = 64;
    private const int RecordSize = 0x104, MaxMove = 826;

    public static IReadOnlyList<SwordShieldLearnMove> Read(byte[] learn, int entry)
    {
        Check(learn, entry);
        var moves = new List<SwordShieldLearnMove>();
        for (int slot = 0; slot <= Capacity; slot++)
        {
            int at = entry * RecordSize + slot * 4, move = BinaryPrimitives.ReadUInt16LittleEndian(learn.AsSpan(at)), level = BinaryPrimitives.ReadUInt16LittleEndian(learn.AsSpan(at + 2));
            if (move == 0xFFFF && level == 0xFFFF)
                break;
            moves.Add(new(move, level));
        }
        return moves;
    }

    public static IReadOnlyDictionary<int, IReadOnlyList<SwordShieldLearnMove>> ReadAll(byte[] learn) =>
        Enumerable.Range(0, learn.Length / RecordSize).ToDictionary(e => e, e => Read(learn, e));

    /// <summary>Lists replaced whole (sorted by level, stable), as one data result with a change per slot that moves.</summary>
    public static SwordShieldDataResult Replace(IReadOnlyDictionary<string, byte[]> files, IReadOnlyDictionary<int, IReadOnlyList<SwordShieldLearnMove>> sets)
    {
        var learn = (byte[])files[SwordShieldDataEditor.LearnPath].Clone();
        var changes = new List<SwordShieldDataChange>();
        foreach (var (entry, list) in sets.OrderBy(p => p.Key))
        {
            Check(learn, entry);
            if (list.Count > Capacity)
                throw new InvalidDataException($"Como máximo {Capacity} movimientos por nivel por Pokémon.");
            if (list.Any(m => m.Move is < 1 or > MaxMove || m.Level is < 0 or > 100))
                throw new InvalidDataException("Cada movimiento debe tener un ID válido y un nivel 0–100 (0 = al evolucionar).");
            var before = Read(learn, entry);
            var ordered = list.OrderBy(m => m.Level).ToArray();
            int start = entry * RecordSize;
            learn.AsSpan(start, RecordSize).Fill(0xFF);
            for (int slot = 0; slot < ordered.Length; slot++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(learn.AsSpan(start + slot * 4), (ushort)ordered[slot].Move);
                BinaryPrimitives.WriteUInt16LittleEndian(learn.AsSpan(start + slot * 4 + 2), (ushort)ordered[slot].Level);
            }
            for (int slot = 0; slot < Math.Max(before.Count, ordered.Length); slot++)
            {
                var b = slot < before.Count ? before[slot] : null;
                var a = slot < ordered.Length ? ordered[slot] : null;
                if (b?.Move != a?.Move)
                    changes.Add(new(SwordShieldDataEditor.LearnPath, entry + " / " + slot, "Movimiento", b?.Move ?? 0xFFFF, a?.Move ?? 0xFFFF));
                if (b?.Level != a?.Level)
                    changes.Add(new(SwordShieldDataEditor.LearnPath, entry + " / " + slot, "Nivel", b?.Level ?? 0xFFFF, a?.Level ?? 0xFFFF));
            }
        }
        return new(new Dictionary<string, byte[]>(StringComparer.Ordinal) { [SwordShieldDataEditor.LearnPath] = learn }, changes);
    }

    /// <summary>
    /// The 3DS LearnsetRandomizer options on Gen8: random usable moves with a chance of the species' type, the first one damaging,
    /// the list grown to <see cref="SwordShieldDataOptions.LearnExpandTo"/>, the levels spread up to <see cref="SwordShieldDataOptions.LearnSpreadTo"/>
    /// and the first four at level 1. Entries without moves (unused forms) stay empty.
    /// </summary>
    internal static SwordShieldDataResult Randomize(IReadOnlyDictionary<string, byte[]> files, byte[] personal, SwordShieldDataOptions options)
    {
        if (options.LearnStabPercent > 100 || options.LearnExpandTo is < 0 or > Capacity || options.LearnSpreadTo is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(options));
        int Pick(string key, int n) => (int)(BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(options.Seed + "|" + key))) % (ulong)n);
        var excluded = options.LearnExcludeFixedDamage ? new HashSet<int> { 49, 82 } : [];
        var usable = SwordShieldDataEditor.UsableMoves(files).Where(m => !excluded.Contains(m)).ToHashSet();
        var info = SwordShieldDataEditor.Read(SwordShieldDataSection.Moves, files, personal).GroupBy(c => c.Entry).Where(g => usable.Contains(g.Key))
            .ToDictionary(g => g.Key, g => (Type: g.First(c => c.Field == "Tipo").Value, Damaging: g.First(c => c.Field == "Categoría").Value != 0 && g.First(c => c.Field == "Potencia").Value > 0));
        int[] pool = info.Keys.Order().ToArray(), damaging = pool.Where(m => info[m].Damaging).ToArray();
        if (damaging.Length == 0)
            throw new InvalidDataException("No hay movimientos ofensivos utilizables.");
        var types = new SwordShieldPersonalTable(personal).Entries().ToDictionary(e => e.Index, e => new[] { e.Values[6], e.Values[7] });
        var sets = new Dictionary<int, IReadOnlyList<SwordShieldLearnMove>>();
        foreach (var (entry, list) in ReadAll(files[SwordShieldDataEditor.LearnPath]))
        {
            if (list.Count == 0)
                continue;
            int count = Math.Max(list.Count, options.LearnExpandTo);
            var own = types.TryGetValue(entry, out var t) ? t : [];
            int[] stab = damaging.Where(m => own.Contains(info[m].Type)).ToArray();
            var chosen = new List<int>();
            for (int i = 0; i < count; i++)
            {
                int move = 0;
                for (int attempt = 0; attempt < 12 && (move == 0 || chosen.Contains(move)); attempt++)
                {
                    string key = $"{entry}/{i}/{attempt}";
                    int[] from = stab.Length > 0 && options.LearnStabPercent > 0 && Pick(key + "/stab", 100) < options.LearnStabPercent ? stab : i == 0 ? damaging : pool;
                    move = from[Pick(key, from.Length)];
                }
                chosen.Add(move);
            }
            // Levels: kept where the slot existed unless spread; added slots continue past the last level.
            int top = options.LearnSpreadTo > 0 ? options.LearnSpreadTo : Math.Max(list.Max(m => m.Level), 50);
            bool respread = options.LearnSpreadTo > 0 || count > list.Count;
            sets[entry] = chosen.Select((move, i) => new SwordShieldLearnMove(move,
                options.LearnStartWithFour && i < 4 ? 1 : respread ? Math.Max(1, i * top / count) : list[i].Level)).ToArray();
        }
        return Replace(files, sets);
    }

    private static void Check(byte[] learn, int entry)
    {
        if (learn.Length % RecordSize != 0 || entry < 0 || (entry + 1) * RecordSize > learn.Length)
            throw new InvalidDataException("Entrada de aprendizaje fuera de la tabla.");
    }
}
