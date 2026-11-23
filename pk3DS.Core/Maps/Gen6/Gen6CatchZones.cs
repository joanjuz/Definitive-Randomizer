#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Maps.Gen6;

/// <summary>
/// What makes a cell produce wild encounters in one Gen6 family. Each value was read from that family's own
/// data, never carried over from the other one:
/// <list type="bullet">
/// <item><b>X/Y:</b> grass <c>0x20002004</c> (every tall-grass cell of Route 2 of X, and Y has the same values);
/// table of 0x188 bytes (0x10 of header, 0x178 of slots) in part 3 of 4, the last part of the pack.</item>
/// <item><b>OR/AS:</b> grass <c>0x20004004</c> (8693 cells in 45 zones with a table, in Omega Ruby and in Alpha
/// Sapphire; <c>0x20002004</c> does not exist in either game); table of 0x104 bytes (0x0E of header, 0xF6 of slots,
/// as read by the encounter editor) in part 3 of 5, followed by a 12-byte part 4.</item>
/// </list>
/// </summary>
public sealed record Gen6CatchFormat(string Name, uint EncounterGrass, int TableSize, int PartCount)
{
    public static readonly Gen6CatchFormat XY = new("X/Y", 0x20002004, 0x188, 4);
    public static readonly Gen6CatchFormat ORAS = new("Rubí Omega/Zafiro Alfa", 0x20004004, 0x104, 5);

    public static Gen6CatchFormat Of(Gen6Title game) => game == Gen6Title.XY ? XY : ORAS;
}

/// <summary>
/// Wild-encounter cells of Gen6. Two pieces make a cell produce encounters, both seen in the game data and in
/// «Enable Catch Zones»:
/// <list type="bullet">
/// <item>its permission value is the family's grass value (<see cref="Gen6CatchFormat.EncounterGrass"/>);</item>
/// <item>the zone that owns the cell (zone map of the matrix) has an encounter table in part 3 of its ZO pack,
/// empty in zones without encounters.</item>
/// </list>
/// The grass value appears in zones without a table, where it gives no encounters. Only plain ground
/// (<see cref="Ground"/>) is painted; the 3D model of the block is not changed, so the new grass is not drawn.
/// Approved in the game only for X (Aquacorde Town).
/// </summary>
public static class Gen6CatchZones
{
    public const uint Ground = 0x20;
    /// <summary>X/Y values, kept for the callers written for X/Y.</summary>
    public const uint EncounterGrass = 0x20002004;
    public const int TableSize = 0x188, TablePart = 3;

    public static byte[] PaintBlock(byte[] block, IReadOnlyCollection<(int X, int Y)> cells) => PaintBlock(block, cells, Gen6CatchFormat.XY);

    /// <summary>Copy of a decompressed GR block with cells (x, y of the block, 0–39) set to the family's grass value; each must be <see cref="Ground"/>.</summary>
    public static byte[] PaintBlock(byte[] block, IReadOnlyCollection<(int X, int Y)> cells, Gen6CatchFormat format)
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
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at), format.EncounterGrass);
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
    /// OR/AS keep a second copy of every encounter table in the last member of the zone archive: an «EN» pack with
    /// u16 magic, u16 zone count and count+1 u32 offsets, one slice per zone (empty when the zone has no table).
    /// In Omega Ruby and Alpha Sapphire every slice is byte-identical to part 3 of its zone (536 of 536), and the
    /// encounter editor writes both. Null when <paramref name="data"/> is not such a pack.
    /// </summary>
    public static IReadOnlyList<(int Start, int End)>? SharedSlices(ReadOnlySpan<byte> data, int zones)
    {
        if (data.Length < 4 || data[0] != (byte)'E' || data[1] != (byte)'N') return null;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        int header = 4 + ((count + 1) * 4);
        if (count != zones || header > data.Length) return null;
        var slices = new (int, int)[count];
        int previous = header;
        for (int i = 0; i <= count; i++)
        {
            int at = BinaryPrimitives.ReadInt32LittleEndian(data[(4 + (i * 4))..]);
            if (at < previous || at > data.Length) return null;
            if (i > 0) slices[i - 1] = (previous, at);
            previous = at;
        }
        if (BinaryPrimitives.ReadInt32LittleEndian(data[(4 + (count * 4))..]) != data.Length) return null;
        return slices;
    }

    /// <summary>Copy of the «EN» pack with <paramref name="table"/> as the (empty) slice of <paramref name="zone"/>; the other slices only move.</summary>
    public static byte[] AddSharedTable(byte[] shared, int zones, int zone, ReadOnlySpan<byte> table)
    {
        var slices = SharedSlices(shared, zones) ?? throw new InvalidOperationException("La copia compartida de las tablas (EN) no tiene el formato esperado.");
        if (zone < 0 || zone >= slices.Count) throw new ArgumentOutOfRangeException(nameof(zone));
        var (start, end) = slices[zone];
        if (end != start) throw new InvalidOperationException("La copia compartida ya tiene tabla para esa zona.");
        var output = new byte[shared.Length + table.Length];
        shared.AsSpan(0, start).CopyTo(output);
        table.CopyTo(output.AsSpan(start));
        shared.AsSpan(start).CopyTo(output.AsSpan(start + table.Length));
        for (int i = zone + 1; i <= slices.Count; i++)
        {
            int at = 4 + (i * 4);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at), BinaryPrimitives.ReadInt32LittleEndian(shared.AsSpan(at)) + table.Length);
        }
        var after = SharedSlices(output, zones) ?? throw new InvalidOperationException("La relectura de la copia compartida falló.");
        for (int i = 0; i < slices.Count; i++)
        {
            var o = shared.AsSpan(slices[i].Start, slices[i].End - slices[i].Start);
            var n = output.AsSpan(after[i].Start, after[i].End - after[i].Start);
            if (!(i == zone ? n.SequenceEqual(table) : n.SequenceEqual(o))) throw new InvalidOperationException($"La copia compartida cambió la tabla de la zona {i}.");
        }
        return output;
    }

    public static byte[] AddTable(byte[] zone, ReadOnlySpan<byte> table) => AddTable(zone, table, Gen6CatchFormat.XY);

    /// <summary>
    /// Copy of a zone without encounters with <paramref name="table"/> as its part 3. The table is inserted where
    /// the empty part 3 starts and the offsets of the later parts grow by its size: in X/Y part 3 is the last part
    /// (only the end offset moves); in OR/AS the 12-byte part 4 follows and is moved unchanged. Every other part is
    /// checked to keep its bytes.
    /// </summary>
    public static byte[] AddTable(byte[] zone, ReadOnlySpan<byte> table, Gen6CatchFormat format)
    {
        if (table.Length != format.TableSize) throw new ArgumentException($"La tabla de encuentros de {format.Name} debe tener {format.TableSize} bytes.", nameof(table));
        var parsed = new Gen6Zone(zone);
        var pack = parsed.Pack;
        if (pack.Count != format.PartCount) throw new InvalidOperationException($"La zona tiene {pack.Count} partes; en {format.Name} tienen {format.PartCount}.");
        if (pack.Length(TablePart) != 0) throw new InvalidOperationException("La zona ya tiene tabla de encuentros.");
        int insert = pack.Start(TablePart);
        var output = new byte[zone.Length + table.Length];
        zone.AsSpan(0, insert).CopyTo(output);
        table.CopyTo(output.AsSpan(insert));
        zone.AsSpan(insert).CopyTo(output.AsSpan(insert + table.Length));
        for (int i = TablePart + 1; i <= pack.Count; i++)
        {
            int at = 4 + (i * 4);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at), BinaryPrimitives.ReadInt32LittleEndian(zone.AsSpan(at)) + table.Length);
        }
        var reread = new Gen6Zone(output);
        for (int p = 0; p < pack.Count; p++)
            if (p != TablePart && !pack.Part(p).SequenceEqual(reread.Pack.Part(p))) throw new InvalidOperationException($"La tabla nueva cambió la parte {p} de la zona.");
        if (!reread.Pack.Part(TablePart).SequenceEqual(table)) throw new InvalidOperationException("La relectura de la tabla no coincide.");
        int offsetsEnd = 4 + ((pack.Count + 1) * 4);
        for (int i = 0; i < insert; i++)
            if (zone[i] != output[i] && (i < 4 + ((TablePart + 1) * 4) || i >= offsetsEnd)) throw new InvalidOperationException("La tabla nueva cambió bytes de la zona.");
        return output;
    }
}
