#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using pk3DS.Core.Maps.Gen6;

namespace pk3DS.Core.Maps.Gen7;

/// <summary>
/// One wild-encounter region of an Ultra Sun / Ultra Moon area: a record of part 6 of the area event pack (ED).
/// The record (28 bytes) is {u32 type 6, u32 table, u32 ground mask, u32, u32, u32, u32 shape offset}; the shape is
/// a box {u32 1, u32 1, f32×3 position, quaternion, f32×3 size} (48 bytes) or a cylinder {u32 1, u32 0, f32×3 position,
/// f32 radius, f32 height} (28 bytes). <see cref="Table"/> is an entry of the area's encounter table pack (EA, slot 9)
/// and <see cref="Mask"/> has bit <c>n</c> set for each ground collision attribute <c>n</c> the region applies to.
/// Checked on every area of Ultra Moon (231 regions, 336 areas): no area has regions without tables, every table index
/// is in range, and the triangles inside the shapes carry the attributes of the mask (Route 1: grass 0x1F, bit 31).
/// </summary>
public sealed record Gen7EncounterRegion(int ZoneFile, int Row, uint Table, uint Mask, bool Box, Vector3 Position, Quaternion Rotation, Vector3 Size, float Radius, float Height)
{
    public bool Allows(uint attribute) => attribute < 32 && ((Mask >> (int)attribute) & 1) != 0;

    /// <summary>
    /// Whether a point is inside the shape. The box size is a half-extent in its own frame: read that way the 12 grass boxes
    /// of Ultra Moon cover 99.8 % of their grass patch and no other patch (44.6 % read as the full width).
    /// </summary>
    public bool Contains(Vector3 p)
    {
        if (Box)
        {
            var l = Vector3.Transform(p - Position, Quaternion.Inverse(Quaternion.Normalize(Rotation)));
            return Math.Abs(l.X) <= Size.X && Math.Abs(l.Z) <= Size.Z && Math.Abs(l.Y) <= Math.Max(Size.Y, 1);
        }
        float dx = p.X - Position.X, dz = p.Z - Position.Z;
        return (dx * dx) + (dz * dz) <= Radius * Radius && Math.Abs(p.Y - Position.Y) <= Math.Max(Height, 1);
    }
}

/// <summary>Reading and writing the two pieces of a Gen7 encounter zone: regions (ED part 6) and tables (EA).</summary>
public static class Gen7CatchZones
{
    /// <summary>Data start of every vanilla EA pack of Ultra Moon.</summary>
    public const int VanillaTableStart = 0x80;
    public const int RegionPack = 6, RecordSize = 28, BoxSize = 48, CylinderSize = 28, TableSlot = 9, TableSize = 768, RegionType = 6;
    /// <summary>
    /// Ground attribute of the tall grass of Route 1 (the only one written for now): every grass region of Route 1 has
    /// mask bit 31 and covers triangles with attribute 0x1F.
    /// </summary>
    public const uint GrassAttribute = 0x1F;

    /// <summary>
    /// Ground attributes a new region can allow: those that vanilla Ultra Moon regions use as the only bit of their mask
    /// (regions: 0x00 124, 0x16 44, 0x1F 12, 0x19 5, 0x15 5). Attributes only seen combined with others (cave floors) or
    /// never seen in a mask are not written.
    /// </summary>
    public static readonly IReadOnlyList<uint> WritableAttributes = [0x00, 0x15, 0x16, 0x19, 0x1F];
    public static bool IsWritable(uint attribute) => WritableAttributes.Contains(attribute);
    /// <summary>The attribute of a single-bit mask, or null.</summary>
    public static uint? AttributeOf(uint mask) => mask != 0 && (mask & (mask - 1)) == 0 ? (uint)System.Numerics.BitOperations.TrailingZeroCount(mask) : null;
    public static uint MaskOf(uint attribute) => 1u << (int)attribute;

    /// <summary>Parts of a BinLinker pack (two letters, u16 count, count+1 offsets).</summary>
    public static byte[][] Parts(byte[] data, string magic)
    {
        // An empty pack (count 0, a single offset pointing at its own end) is how areas without encounters store EA.
        if (data.Length == 8 && data[0] == magic[0] && data[1] == magic[1] && BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)) == 0
            && BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)) == 8) return [];
        var pack = Gen6Pack.Read(data, magic);
        return Enumerable.Range(0, pack.Count).Select(i => pack.Part(i).ToArray()).ToArray();
    }

    /// <summary>
    /// BinLinker pack with the parts one after the other and no padding between them (all part sizes are multiples of 4
    /// in USUM). The parts start at <paramref name="dataStart"/> when given (zero padding after the offsets), as in the
    /// vanilla EA packs, whose data always starts at 0x80; otherwise right after the offsets, as in every ED pack and in
    /// the EA packs written by the randomizer. Callers check that the original rebuilds byte for byte.
    /// </summary>
    public static byte[] Pack(string magic, IReadOnlyList<byte[]> parts, int? dataStart = null)
    {
        if (magic.Length != 2) throw new ArgumentException("El identificador del paquete tiene dos letras.", nameof(magic));
        if (parts.Any(p => p.Length % 4 != 0)) throw new InvalidOperationException("Las partes del paquete deben medir un múltiplo de 4 bytes.");
        int header = 4 + ((parts.Count + 1) * 4);
        if (dataStart is int start)
        {
            if (start < header || start % 4 != 0) throw new InvalidOperationException("La cabecera del paquete no cabe antes de sus datos.");
            header = start;
        }
        var output = new byte[header + parts.Sum(p => p.Length)];
        Encoding.ASCII.GetBytes(magic).CopyTo(output, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(2), (ushort)parts.Count);
        int at = header;
        for (int i = 0; i <= parts.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4 + (i * 4)), at);
            if (i < parts.Count) { parts[i].CopyTo(output, at); at += parts[i].Length; }
        }
        return output;
    }

    /// <summary>Where the parts of a pack start (its first offset).</summary>
    public static int DataStart(byte[] data) => data.Length >= 8 ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)) : 0;

    /// <summary>Whether <paramref name="data"/> is exactly what <see cref="Pack"/> writes for its own parts and data start.</summary>
    public static bool RoundTrips(byte[] data, string magic)
    {
        try { return Pack(magic, Parts(data, magic), DataStart(data)).AsSpan().SequenceEqual(data); }
        catch (Exception ex) when (ex is MapFormatException or InvalidOperationException or ArgumentException) { return false; }
    }

    /// <summary>Encounter regions of one zone file of ED part 6 (empty file = none).</summary>
    public static IReadOnlyList<Gen7EncounterRegion> Regions(byte[] file, int zoneFile)
    {
        var list = new List<Gen7EncounterRegion>();
        if (file.Length == 0) return list;
        if (file.Length < 4) throw new MapFormatException("Archivo de regiones truncado.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(file);
        if (count < 0 || count > 256 || 4 + ((long)count * RecordSize) > file.Length) throw new MapFormatException($"Recuento de regiones {count} no válido.");
        for (int i = 0; i < count; i++)
        {
            var r = file.AsSpan(4 + (i * RecordSize), RecordSize);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(r);
            if (type != RegionType) throw new MapFormatException($"Registro de tipo {type} en la parte de regiones.");
            uint table = BinaryPrimitives.ReadUInt32LittleEndian(r[4..]), mask = BinaryPrimitives.ReadUInt32LittleEndian(r[8..]);
            int off = BinaryPrimitives.ReadInt32LittleEndian(r[24..]);
            if (off < 4 + (count * RecordSize) || off + CylinderSize > file.Length) throw new MapFormatException("Forma de región fuera del archivo.");
            int shape = off;
            float F(int k) => BinaryPrimitives.ReadSingleLittleEndian(file.AsSpan(shape + k));
            bool box = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(shape + 4)) == 1;
            if (box && off + BoxSize > file.Length) throw new MapFormatException("Caja de región truncada.");
            var pos = new Vector3(F(8), F(12), F(16));
            list.Add(box
                ? new Gen7EncounterRegion(zoneFile, i, table, mask, true, pos, new Quaternion(F(20), F(24), F(28), F(32)), new Vector3(F(36), F(40), F(44)), 0, 0)
                : new Gen7EncounterRegion(zoneFile, i, table, mask, false, pos, Quaternion.Identity, Vector3.Zero, F(20), F(24)));
        }
        return list;
    }

    /// <summary>Encounter regions of every zone file of an area event pack.</summary>
    public static IReadOnlyList<Gen7EncounterRegion> Regions(byte[] ed)
    {
        var parts = Parts(ed, "ED");
        if (parts.Length <= RegionPack) return [];
        var files = Parts(parts[RegionPack], Encoding.ASCII.GetString(parts[RegionPack], 0, 2));
        return files.SelectMany((f, z) => Regions(f, z)).ToArray();
    }

    /// <summary>Magic of ED part 6 (a pack of one file per zone of the area).</summary>
    private static string Magic(byte[] part) => part.Length >= 2 ? Encoding.ASCII.GetString(part, 0, 2) : throw new MapFormatException("Parte de regiones vacía.");

    /// <summary>Number of zone files in ED part 6, or -1 when the pack does not have the expected layout.</summary>
    public static int RegionFiles(byte[] ed)
    {
        try
        {
            if (!RoundTrips(ed, "ED")) return -1;
            var parts = Parts(ed, "ED");
            if (parts.Length <= RegionPack || !RoundTrips(parts[RegionPack], Magic(parts[RegionPack]))) return -1;
            return Gen6Pack.Read(parts[RegionPack], Magic(parts[RegionPack])).Count;
        }
        catch (MapFormatException) { return -1; }
    }

    /// <summary>
    /// Copy of the area event pack with one more box region in zone file <paramref name="zoneFile"/> of part 6: the new
    /// record goes after the existing ones (their shape offsets move by one record) and its shape at the end of the file.
    /// Every other part, every other zone file and every existing region is checked to keep its bytes.
    /// </summary>
    public static byte[] AddRegion(byte[] ed, int zoneFile, uint table, uint mask, Vector3 position, Vector3 size)
    {
        if (!RoundTrips(ed, "ED")) throw new InvalidOperationException("El paquete de eventos no tiene la estructura verificada.");
        if (!float.IsFinite(position.X + position.Y + position.Z) || size.X <= 0 || size.Y <= 0 || size.Z <= 0 || !float.IsFinite(size.X + size.Y + size.Z))
            throw new ArgumentOutOfRangeException(nameof(size), "La caja necesita posición finita y tamaño positivo.");
        if (mask == 0) throw new ArgumentOutOfRangeException(nameof(mask), "La región necesita al menos un atributo de suelo.");
        var parts = Parts(ed, "ED");
        if (parts.Length <= RegionPack) throw new InvalidOperationException("El paquete de eventos no tiene parte de regiones.");
        string magic = Magic(parts[RegionPack]);
        if (!RoundTrips(parts[RegionPack], magic)) throw new InvalidOperationException("La parte de regiones no tiene la estructura verificada.");
        var files = Parts(parts[RegionPack], magic);
        if (zoneFile < 0 || zoneFile >= files.Length) throw new ArgumentOutOfRangeException(nameof(zoneFile));
        var old = files[zoneFile];
        var before = Regions(old, zoneFile);
        int n = before.Count, oldHeaders = 4 + (n * RecordSize);
        if (n > 0 && before.Min(r => ShapeOffset(old, r.Row)) != oldHeaders) throw new InvalidOperationException("Las formas de las regiones no siguen a los registros.");
        byte[] shapes = n > 0 ? old.AsSpan(oldHeaders).ToArray() : [];
        var output = new byte[4 + ((n + 1) * RecordSize) + shapes.Length + BoxSize];
        BinaryPrimitives.WriteInt32LittleEndian(output, n + 1);
        for (int i = 0; i < n; i++)
        {
            old.AsSpan(4 + (i * RecordSize), RecordSize).CopyTo(output.AsSpan(4 + (i * RecordSize)));
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4 + (i * RecordSize) + 24), ShapeOffset(old, i) + RecordSize);
        }
        int rec = 4 + (n * RecordSize), shapeAt = 4 + ((n + 1) * RecordSize) + shapes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(rec), RegionType);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(rec + 4), table);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(rec + 8), mask);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(rec + 24), shapeAt);
        shapes.CopyTo(output, 4 + ((n + 1) * RecordSize));
        var s = output.AsSpan(shapeAt);
        BinaryPrimitives.WriteUInt32LittleEndian(s, 1); BinaryPrimitives.WriteUInt32LittleEndian(s[4..], 1);
        float[] values = [position.X, position.Y, position.Z, 0, 0, 0, 1, size.X, size.Y, size.Z];
        for (int k = 0; k < values.Length; k++) BinaryPrimitives.WriteSingleLittleEndian(s[(8 + (k * 4))..], values[k]);

        files[zoneFile] = output;
        parts[RegionPack] = Pack(magic, files);
        byte[] result = Pack("ED", parts);

        // Re-read: the old regions and every other byte are kept; the new region reads back as written.
        var after = Regions(output, zoneFile);
        if (after.Count != n + 1 || !before.SequenceEqual(after.Take(n))) throw new InvalidOperationException("La escritura cambió una región existente.");
        var added = after[n];
        if (added.Table != table || added.Mask != mask || !added.Box || added.Position != position || added.Size != size || added.Rotation != Quaternion.Identity)
            throw new InvalidOperationException("La región nueva no se relee igual.");
        var reread = Parts(result, "ED");
        var oldParts = Parts(ed, "ED");
        for (int p = 0; p < oldParts.Length; p++)
            if (p != RegionPack && !oldParts[p].AsSpan().SequenceEqual(reread[p])) throw new InvalidOperationException($"La escritura cambió la parte {p} de los eventos.");
        var oldFiles = Parts(oldParts[RegionPack], magic);
        var newFiles = Parts(reread[RegionPack], magic);
        for (int z = 0; z < oldFiles.Length; z++)
            if (z != zoneFile && !oldFiles[z].AsSpan().SequenceEqual(newFiles[z])) throw new InvalidOperationException($"La escritura cambió las regiones de la zona {z}.");
        return result;
    }

    private static int ShapeOffset(byte[] file, int row) => BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(4 + (row * RecordSize) + 24));

    /// <summary>
    /// Tables (non-empty entries) of an area's encounter table pack (EA), or -1 when it is not one. Vanilla areas without
    /// encounters have one empty entry (and data at 0x80); the randomizer writes them as a pack of no entries. Vanilla packs
    /// never mix empty and full entries.
    /// </summary>
    public static int TableCount(byte[] ea)
    {
        if (ea.Length < 8 || ea[0] != 'E' || ea[1] != 'A' || !RoundTrips(ea, "EA")) return -1;
        var parts = Parts(ea, "EA");
        return parts.Count(p => p.Length > 0);
    }

    /// <summary>Entry <paramref name="index"/> of an encounter table pack when it is a table, or null.</summary>
    public static byte[]? Table(byte[] ea, int index)
    {
        if (TableCount(ea) < 0) return null;
        var parts = Parts(ea, "EA");
        return index < 0 || index >= parts.Length || parts[index].Length == 0 ? null : parts[index];
    }

    /// <summary>
    /// Copy of an encounter table pack with <paramref name="entry"/> as a new table. A pack whose only entry is empty (vanilla
    /// area without encounters) gets it in that entry, index 0; otherwise it is appended. The data start is kept (0x80 in
    /// vanilla packs), so a vanilla pack whose offsets would no longer fit before it is refused. Every other entry keeps its bytes.
    /// </summary>
    public static (byte[] Pack, int Index) AddTable(byte[] ea, ReadOnlySpan<byte> entry)
    {
        if (entry.Length != TableSize) throw new ArgumentException($"La tabla de encuentros de USUM mide {TableSize} bytes.", nameof(entry));
        if (TableCount(ea) < 0) throw new InvalidOperationException("El paquete de tablas no tiene la estructura verificada.");
        var old = Parts(ea, "EA");
        var parts = old.ToList();
        int index;
        if (parts.Count == 1 && parts[0].Length == 0) { parts[0] = entry.ToArray(); index = 0; }
        else { if (parts.Any(p => p.Length == 0)) throw new InvalidOperationException("El paquete de tablas mezcla entradas vacías y llenas; no se escribe."); parts.Add(entry.ToArray()); index = parts.Count - 1; }
        // Vanilla packs keep their data at 0x80; the randomizer's packs have none after the offsets.
        int start = DataStart(ea), needed = 4 + ((parts.Count + 1) * 4);
        bool vanilla = start == VanillaTableStart;
        if (vanilla && needed > start) throw new InvalidOperationException("Las tablas del área ya llenan su cabecera; no se añaden más.");
        byte[] output = Pack("EA", parts, vanilla ? start : null);
        var reread = Parts(output, "EA");
        if (reread.Length != parts.Count || !reread[index].AsSpan().SequenceEqual(entry) || old.Where((p, i) => i != index && !p.AsSpan().SequenceEqual(reread[i])).Any())
            throw new InvalidOperationException("La relectura del paquete de tablas no coincide.");
        return (output, index);
    }
}
