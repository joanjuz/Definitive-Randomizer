#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>
/// NARC archive used by Gen4/Gen5 data. The header and the name table (BTNF) are kept as read; only the allocation table
/// and the data block are rebuilt, so an unchanged archive serializes to the original bytes.
/// </summary>
public sealed class Narc
{
    private readonly byte[] _header;
    private readonly byte[] _names;
    private readonly byte _padding;

    public List<byte[]> Files { get; }

    private Narc(byte[] header, byte[] names, byte padding, List<byte[]> files)
    {
        _header = header;
        _names = names;
        _padding = padding;
        Files = files;
    }

    public static bool IsNarc(ReadOnlySpan<byte> data) => data.Length >= 4 && data[..4].SequenceEqual("NARC"u8);

    public static Narc Read(byte[] data)
    {
        if (!IsNarc(data) || data.Length < 0x1C)
            throw new InvalidDataException("El archivo no es un NARC válido.");
        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x0C));
        int at = headerSize;
        (int Start, int Size) Section(string magic)
        {
            if (at + 8 > data.Length || !data.AsSpan(at, 4).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(magic)))
                throw new InvalidDataException($"Falta la sección {magic} del NARC.");
            int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4)));
            if (size < 8 || at + size > data.Length)
                throw new InvalidDataException($"La sección {magic} del NARC está truncada.");
            var section = (at, size);
            at += size;
            return section;
        }
        var fat = Section("BTAF");
        var names = Section("BTNF");
        var image = Section("GMIF");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(fat.Start + 8));
        if (12 + count * 8 > fat.Size)
            throw new InvalidDataException("La tabla BTAF del NARC está truncada.");
        int dataStart = image.Start + 8, dataEnd = image.Start + image.Size;
        var files = new List<byte[]>(count);
        byte padding = 0xFF;
        int previousEnd = 0;
        for (int i = 0; i < count; i++)
        {
            int start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(fat.Start + 12 + i * 8)));
            int end = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(fat.Start + 16 + i * 8)));
            if (end < start || dataStart + end > dataEnd)
                throw new InvalidDataException("Entrada BTAF fuera del bloque de datos del NARC: " + i);
            if (start > previousEnd && i > 0)
                padding = data[dataStart + previousEnd];
            files.Add(data.AsSpan(dataStart + start, end - start).ToArray());
            previousEnd = end;
        }
        return new Narc(data.AsSpan(0, headerSize).ToArray(), data.AsSpan(names.Start, names.Size).ToArray(), padding, files);
    }

    public byte[] Write()
    {
        int count = Files.Count;
        if (count > ushort.MaxValue)
            throw new InvalidDataException("Demasiados archivos para un NARC.");
        int imageSize = 8 + Files.Sum(f => Align4(f.Length));
        int fatSize = 12 + count * 8;
        int total = _header.Length + fatSize + _names.Length + imageSize;
        byte[] output = new byte[total];
        _header.CopyTo(output, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x08), (uint)total);
        int at = _header.Length;
        "BTAF"u8.CopyTo(output.AsSpan(at));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 4), (uint)fatSize);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at + 8), (ushort)count);
        int image = at + fatSize + _names.Length;
        _names.CopyTo(output, at + fatSize);
        "GMIF"u8.CopyTo(output.AsSpan(image));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(image + 4), (uint)imageSize);
        int offset = 0;
        for (int i = 0; i < count; i++)
        {
            byte[] file = Files[i];
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 12 + i * 8), (uint)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 16 + i * 8), (uint)(offset + file.Length));
            file.CopyTo(output, image + 8 + offset);
            output.AsSpan(image + 8 + offset + file.Length, Align4(file.Length) - file.Length).Fill(_padding);
            offset += Align4(file.Length);
        }
        return output;
    }

    private static int Align4(int value) => (value + 3) & ~3;
}
