#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace pk3DS.Core.NDS;

/// <summary>
/// Gen5 message files: each line is XOR-encrypted with a rolling key derived from its terminator and may be
/// 9-bit compressed (first character 0xF100). Control codes are kept as \xNNNN. Based on UPR-ZX PPTxtHandler (GPLv3).
/// </summary>
public static class Gen5Text
{
    public static IReadOnlyList<string> Read(byte[] data)
    {
        if (data.Length < 16)
            throw new InvalidDataException("Archivo de texto Gen5 truncado.");
        int sections = BinaryPrimitives.ReadUInt16LittleEndian(data);
        int entries = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        if (sections < 1)
            return Array.Empty<string>();
        int section = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)));
        if (section + 4 + entries * 8 > data.Length)
            throw new InvalidDataException("La tabla de líneas del texto Gen5 está truncada.");
        var lines = new List<string>(entries);
        for (int i = 0; i < entries; i++)
        {
            int table = section + 4 + i * 8;
            int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(table)));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(table + 4));
            if (count == 0)
            {
                lines.Add("");
                continue;
            }
            int start = section + offset;
            if (start + count * 2 > data.Length)
                throw new InvalidDataException("Línea de texto Gen5 fuera del archivo: " + i);
            var chars = new ushort[count];
            for (int k = 0; k < count; k++)
                chars[k] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start + k * 2));
            int key = chars[count - 1] ^ 0xFFFF;
            for (int k = count - 1; k >= 0; k--)
            {
                chars[k] = (ushort)(chars[k] ^ key);
                key = ((key >> 3) | (key << 13)) & 0xFFFF;
            }
            IReadOnlyList<ushort> plain = chars[0] == 0xF100 ? Decompress(chars) : chars;
            var line = new StringBuilder();
            foreach (ushort c in plain)
            {
                if (c == 0xFFFF)
                    break;
                if (c > 20 && c <= 0xFFF0)
                    line.Append((char)c);
                else
                    line.Append("\\x").Append(c.ToString("X4"));
            }
            lines.Add(line.ToString());
        }
        return lines;
    }

    /// <summary>
    /// Rewrites only the given lines. Unchanged lines keep their encrypted bytes; changed lines are encrypted with their original
    /// key, compressed again when the original line was (and the text allows it) and stored uncompressed otherwise. Line metadata and
    /// a second section are preserved, so no changes returns the same bytes.
    /// </summary>
    public static byte[] Replace(byte[] data, IReadOnlyDictionary<int, string> changes)
    {
        if (changes.Count == 0)
            return (byte[])data.Clone();
        int sections = BinaryPrimitives.ReadUInt16LittleEndian(data);
        int entries = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        if (sections is < 1 or > 2)
            throw new InvalidDataException("Archivo de texto Gen5 con un número de secciones inesperado: " + sections);
        int section = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)));
        int oldSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(section)));
        var lines = new List<ushort[]>(entries);
        var unknowns = new ushort[entries];
        for (int i = 0; i < entries; i++)
        {
            int table = section + 4 + i * 8;
            int start = section + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(table)));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(table + 4));
            unknowns[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(table + 6));
            var raw = new ushort[count];
            for (int k = 0; k < count; k++)
                raw[k] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start + k * 2));
            lines.Add(raw);
        }
        foreach (var (index, text) in changes)
        {
            if (index < 0 || index >= entries || lines[index].Length == 0)
                throw new ArgumentOutOfRangeException(nameof(changes), "Línea de texto Gen5 fuera de rango o vacía: " + index);
            int key = FirstKey(lines[index]);
            var plain = Encode(text);
            // A compressed line stays compressed, so moving lines around keeps the file size (the DSi games have no room to grow).
            if ((lines[index][0] ^ key) == 0xF100 && plain.All(c => c > 1 && c < 0x1FF && (c & 0xFF) != 0xFF))
                plain = Compress(plain);
            // A compressed stream that ends in a word of ones already carries the terminator, as the game files store it.
            if (plain[^1] != 0xFFFF || plain[0] != 0xF100)
                plain.Add(0xFFFF);
            var encrypted = new ushort[plain.Count];
            for (int k = 0; k < plain.Count; k++)
            {
                encrypted[k] = (ushort)(plain[k] ^ key);
                key = ((key << 3) | (key >> 13)) & 0xFFFF;
            }
            lines[index] = encrypted;
        }
        int words = lines.Sum(l => l.Length);
        // Sections end on a 4-byte boundary: pad the last line with one more encrypted terminator, as the game files do.
        if (words % 2 == 1 && entries > 0)
        {
            var last = lines[^1];
            int key = FirstKey(last);
            for (int k = 0; k < last.Length; k++)
                key = ((key << 3) | (key >> 13)) & 0xFFFF;
            lines[^1] = [.. last, (ushort)(0xFFFF ^ key)];
            words++;
        }
        int size = 4 + entries * 8 + words * 2;
        byte[] newSection = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(newSection, (uint)size);
        int offset = 4 + entries * 8, at = offset;
        for (int i = 0; i < entries; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(newSection.AsSpan(4 + i * 8), (uint)offset);
            BinaryPrimitives.WriteUInt16LittleEndian(newSection.AsSpan(8 + i * 8), (ushort)lines[i].Length);
            BinaryPrimitives.WriteUInt16LittleEndian(newSection.AsSpan(10 + i * 8), unknowns[i]);
            foreach (ushort w in lines[i])
            {
                BinaryPrimitives.WriteUInt16LittleEndian(newSection.AsSpan(at), w);
                at += 2;
            }
            offset += lines[i].Length * 2;
        }
        var output = new byte[data.Length - oldSize + size];
        data.AsSpan(0, section).CopyTo(output);
        newSection.CopyTo(output, section);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), (uint)size);
        if (sections == 2)
        {
            int second = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16)));
            int secondSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(second)));
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(16), (uint)(section + size));
            data.AsSpan(second, secondSize).CopyTo(output.AsSpan(section + size));
        }
        return output;
    }

    // Key used for the first character, recovered by walking back from the terminator.
    private static int FirstKey(ushort[] encrypted)
    {
        int key = encrypted[^1] ^ 0xFFFF;
        for (int k = encrypted.Length - 1; k > 0; k--)
            key = ((key >> 3) | (key << 13)) & 0xFFFF;
        return key;
    }

    // Inverse of Read: characters as UTF-16 units and a backslash-x escape with four hex digits as a raw value.
    private static List<ushort> Encode(string text)
    {
        var output = new List<ushort>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 5 < text.Length && text[i + 1] == 'x'
                && ushort.TryParse(text.AsSpan(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out ushort value))
            {
                output.Add(value);
                i += 5;
            }
            else
                output.Add(text[i]);
        }
        return output;
    }

    // Inverse of Decompress: 0xF100, then 9-bit values packed from the low bits up and ended by 0x1FF; the last word is filled with ones.
    private static List<ushort> Compress(IReadOnlyList<ushort> chars)
    {
        var output = new List<ushort> { 0xF100 };
        int buffer = 0, bits = 0;
        foreach (int value in chars.Append((ushort)0x1FF))
        {
            buffer |= value << bits;
            bits += 9;
            for (; bits >= 16; bits -= 16, buffer >>= 16)
                output.Add((ushort)buffer);
        }
        if (bits > 0)
            output.Add((ushort)(buffer | (0xFFFF << bits)));
        return output;
    }

    private static List<ushort> Decompress(ushort[] chars)
    {
        var output = new List<ushort>();
        int j = 1, shift = 0, carry = 0;
        while (j < chars.Length)
        {
            int value;
            if (shift >= 0x10)
            {
                shift -= 0x10;
                if (shift <= 0)
                    continue;
                value = carry | ((chars[j] << (9 - shift)) & 0x1FF);
                if ((value & 0xFF) == 0xFF)
                    break;
                if (value > 1)
                    output.Add((ushort)value);
            }
            else
            {
                value = (chars[j] >> shift) & 0x1FF;
                if ((value & 0xFF) == 0xFF)
                    break;
                if (value > 1)
                    output.Add((ushort)value);
                shift += 9;
                if (shift < 0x10)
                {
                    carry = (chars[j] >> shift) & 0x1FF;
                    shift += 9;
                }
                j++;
            }
        }
        return output;
    }
}
