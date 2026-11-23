#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Maps.Gen6;

/// <summary>
/// Wild-encounter cells of X/Y. Two pieces make a cell produce encounters, both seen in the game and in
/// «Enable Catch Zones»:
/// <list type="bullet">
/// <item>its permission value is <see cref="EncounterGrass"/> (the value of every tall-grass cell of Route 2);</item>
/// <item>the zone that owns the cell (zone map of the matrix) has an encounter table: part 3 of its ZO pack,
/// <see cref="TableSize"/> bytes (0x10 of header and 0x178 of slots), empty in zones without encounters.</item>
/// </list>
/// The value appears in 11 zones without a table, where it gives no encounters. Only plain ground
/// (<see cref="Ground"/>) is painted; the 3D model of the block is not changed, so the new grass is not drawn.
/// </summary>
public static class Gen6CatchZones
{
    public const uint Ground = 0x20, EncounterGrass = 0x20002004;
    public const int TableSize = 0x188, TablePart = 3;

    /// <summary>Copy of a decompressed GR block with cells (x, y of the block, 0–39) set to <see cref="EncounterGrass"/>; each must be <see cref="Ground"/>.</summary>
    public static byte[] PaintBlock(byte[] block, IReadOnlyCollection<(int X, int Y)> cells)
    {
        var parsed = new Gen6MapBlock(block);
        if (cells.Count == 0) throw new ArgumentException("No hay casillas.", nameof(cells));
        int grid = parsed.Pack.Start(0), w = parsed.Cells.Width;
        var output = (byte[])block.Clone();
        var offsets = new HashSet<int>();
        foreach (var (x, y) in cells)
        {
            if (x < 0 || y < 0 || x >= w || y >= parsed.Cells.Height) throw new ArgumentOutOfRangeException(nameof(cells), "Casilla fuera del bloque.");
            int at = grid + 4 + (((y * w) + x) * 4);
            if (BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(at)) != Ground) throw new InvalidOperationException($"La casilla {x}, {y} del bloque no es suelo normal.");
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at), EncounterGrass);
            for (int k = 0; k < 4; k++) offsets.Add(at + k);
        }
        for (int i = 0; i < block.Length; i++)
            if (block[i] != output[i] && !offsets.Contains(i)) throw new InvalidOperationException("La escritura tocó bytes ajenos a las casillas.");
        _ = new Gen6MapBlock(output);
        return output;
    }

    /// <summary>Encounter table of a zone (part 3 of its ZO pack), or null when it has none.</summary>
    public static byte[]? Table(byte[] zone)
    {
        var pack = new Gen6Zone(zone).Pack;
        if (pack.Count <= TablePart || pack.Length(TablePart) == 0) return null;
        return pack.Part(TablePart).ToArray();
    }

    /// <summary>
    /// Copy of a zone without encounters with <paramref name="table"/> as its part 3. Part 3 is the last part
    /// of every ZO pack of X, so the table is appended and only the end offset changes.
    /// </summary>
    public static byte[] AddTable(byte[] zone, ReadOnlySpan<byte> table)
    {
        if (table.Length != TableSize) throw new ArgumentException($"La tabla de encuentros debe tener {TableSize} bytes.", nameof(table));
        var parsed = new Gen6Zone(zone);
        var pack = parsed.Pack;
        if (pack.Count != TablePart + 1) throw new InvalidOperationException($"La zona tiene {pack.Count} partes; solo se añade la tabla a zonas de {TablePart + 1}.");
        if (pack.Length(TablePart) != 0) throw new InvalidOperationException("La zona ya tiene tabla de encuentros.");
        if (pack.Start(TablePart) != zone.Length) throw new InvalidOperationException("La parte de encuentros no está al final del archivo.");
        var output = new byte[zone.Length + table.Length];
        zone.CopyTo(output, 0);
        table.CopyTo(output.AsSpan(zone.Length));
        int end = 4 + ((TablePart + 1) * 4);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(end), output.Length);
        var reread = new Gen6Zone(output);
        for (int i = 0; i < zone.Length; i++)
            if (zone[i] != output[i] && (i < end || i >= end + 4)) throw new InvalidOperationException("La tabla nueva cambió bytes de la zona.");
        if (!reread.Pack.Part(TablePart).SequenceEqual(table)) throw new InvalidOperationException("La relectura de la tabla no coincide.");
        return output;
    }
}
