#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace pk3DS.Core.NDS.Nitro;

/// <summary>
/// Common container of the Nitro (DS) graphics files: <c>BMD0</c> (models), <c>BTX0</c> (textures). Header: magic, byte-order mark 0xFEFF, version,
/// file size, header size 16, block count, then the absolute offset of each block. Read-only; every offset is bounds-checked.
/// </summary>
public sealed class NitroFile
{
    public string Magic { get; }
    public IReadOnlyList<(string Magic, int Offset, int Size)> Blocks { get; }
    public byte[] Data { get; }

    private NitroFile(string magic, List<(string, int, int)> blocks, byte[] data) { Magic = magic; Blocks = blocks; Data = data; }

    public static NitroFile Read(byte[] data, string expectedMagic)
    {
        Nitro.Need(data, 0, 16);
        string magic = Encoding.ASCII.GetString(data, 0, 4);
        if (magic != expectedMagic) throw new InvalidDataException($"Se esperaba un archivo {expectedMagic} y se encontró «{magic}».");
        if (Nitro.U16(data, 4) != 0xFEFF) throw new InvalidDataException("Marca de orden de bytes Nitro desconocida.");
        uint size = Nitro.U32(data, 8); int header = Nitro.U16(data, 12), count = Nitro.U16(data, 14);
        if (size > data.Length || size < 16 || header != 16 || count is 0 or > 16) throw new InvalidDataException("Cabecera Nitro inconsistente.");
        var blocks = new List<(string, int, int)>();
        for (int i = 0; i < count; i++)
        {
            int at = checked((int)Nitro.U32(data, 16 + i * 4)); Nitro.Need(data, at, 8);
            int blockSize = checked((int)Nitro.U32(data, at + 4));
            if (blockSize < 8 || (long)at + blockSize > size) throw new InvalidDataException("Bloque Nitro fuera del archivo.");
            blocks.Add((Encoding.ASCII.GetString(data, at, 4), at, blockSize));
        }
        return new(magic, blocks, data);
    }

    public (int Offset, int Size)? Block(string magic)
    {
        foreach (var b in Blocks) if (b.Magic == magic) return (b.Offset, b.Size);
        return null;
    }
}

/// <summary>An entry of a Nitro name dictionary: its 16-byte name and the offset of its data unit (absolute in the file).</summary>
public sealed record NitroDictEntry(string Name, int DataOffset);

internal static class Nitro
{
    internal static void Need(byte[] data, long at, long length)
    {
        if (at < 0 || length < 0 || at + length > data.Length) throw new InvalidDataException("Datos Nitro truncados.");
    }
    internal static int U16(byte[] d, int at) { Need(d, at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at)); }
    internal static short I16(byte[] d, int at) { Need(d, at, 2); return BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(at)); }
    internal static uint U32(byte[] d, int at) { Need(d, at, 4); return BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at)); }
    internal static int I32(byte[] d, int at) { Need(d, at, 4); return BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(at)); }

    /// <summary>
    /// Reads a Nitro dictionary (NNSG3dResDict): u8 revision, u8 count, u16 size, u16 unused, u16 offset of the entry area; the search tree is not needed
    /// to list entries. Entry area: u16 unit size, u16 area size, count data units, then count 16-byte NUL-padded names.
    /// </summary>
    internal static IReadOnlyList<NitroDictEntry> Dictionary(byte[] d, int at, int minUnit)
    {
        Need(d, at, 8);
        int count = d[at + 1], size = U16(d, at + 2), entries = at + U16(d, at + 6);
        if (d[at] != 0 || size < 8) throw new InvalidDataException("Diccionario Nitro con revisión o tamaño desconocidos.");
        Need(d, entries, 4);
        int unit = U16(d, entries);
        if (unit < minUnit) throw new InvalidDataException("Unidad de diccionario Nitro demasiado pequeña.");
        int data = entries + 4, names = data + unit * count;
        Need(d, names, count * 16L); Need(d, at, size);
        var list = new List<NitroDictEntry>(count);
        for (int i = 0; i < count; i++)
        {
            int n = names + i * 16, length = 0; while (length < 16 && d[n + length] != 0) length++;
            list.Add(new(Encoding.ASCII.GetString(d, n, length), data + i * unit));
        }
        return list;
    }

    /// <summary>BGR555 (bit 15 ignored) to opaque ARGB32.</summary>
    internal static uint Bgr555(int c)
    {
        int r = c & 31, g = c >> 5 & 31, b = c >> 10 & 31;
        return 0xFF000000u | (uint)(r << 3 | r >> 2) << 16 | (uint)(g << 3 | g >> 2) << 8 | (uint)(b << 3 | b >> 2);
    }
}
