#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace pk3DS.Core.NDS;

/// <summary>
/// Gen4 message files: the pointer table and each line are XOR-encrypted with keys derived from the file key and the line
/// number, lines may be 9-bit compressed (first character 0xF100), and characters map through the Gen4 table instead of
/// Unicode. Unknown characters are kept as \xNNNN and variables as \vNNNN\zNNNN. Based on UPR-ZX thenewpoketext (GPLv3).
/// </summary>
public static class Gen4Text
{
    private static readonly Lazy<string?[]> Table = new(LoadTable);

    public static IReadOnlyList<string> Read(byte[] data)
    {
        if (data.Length < 4)
            throw new InvalidDataException("Archivo de texto Gen4 truncado.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        int fileKey = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        if (4 + count * 8 > data.Length)
            throw new InvalidDataException("La tabla de líneas del texto Gen4 está truncada.");
        int key = (fileKey * 0x2FD) & 0xFFFF;
        var lines = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int lineKey = (key * (i + 1)) & 0xFFFF;
            uint mask = (uint)(lineKey | (lineKey << 16));
            int offset = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4 + i * 8)) ^ mask));
            int length = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8 + i * 8)) ^ mask));
            if (offset < 0 || length < 0 || offset + length * 2L > data.Length)
                throw new InvalidDataException($"La línea {i} del texto Gen4 sale del archivo.");
            var chars = new int[length];
            int charKey = (0x91BD3 * (i + 1)) & 0xFFFF;
            for (int c = 0; c < length; c++)
            {
                chars[c] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + c * 2)) ^ charKey;
                charKey = (charKey + 0x493D) & 0xFFFF;
            }
            lines.Add(Decode(chars.Length > 0 && chars[0] == 0xF100 ? Decompress(chars) : chars));
        }
        return lines;
    }

    // 9-bit characters packed into 15-bit units after the 0xF100 marker; 0x1FF ends the line.
    private static int[] Decompress(int[] chars)
    {
        var result = new List<int>();
        int buffer = 0, bufferBits = 0;
        for (int j = 1; j < chars.Length; j++)
        {
            buffer |= (chars[j] & 0x7FFF) << bufferBits;
            bufferBits += 15;
            while (bufferBits >= 9)
            {
                int value = buffer & 0x1FF;
                if (value == 0x1FF)
                    return [.. result];
                result.Add(value);
                buffer >>= 9;
                bufferBits -= 9;
            }
        }
        return [.. result];
    }

    private static string Decode(int[] chars)
    {
        var table = Table.Value;
        var text = new StringBuilder();
        for (int i = 0; i < chars.Length; i++)
        {
            int c = chars[i];
            if (c == 0xFFFF)
                break;
            if (c == 0xFFFE && i + 2 < chars.Length)
            {
                text.Append("\\v").Append(chars[++i].ToString("X4"));
                int total = chars[++i];
                if (total == 0)
                    text.Append("\\x0000");
                for (int z = 0; z < total && i + 1 < chars.Length; z++)
                    text.Append("\\z").Append(chars[++i].ToString("X4"));
                continue;
            }
            text.Append(table[c] ?? "\\x" + c.ToString("X4"));
        }
        return text.ToString();
    }

    private static string?[] LoadTable()
    {
        var table = new string?[0x10000];
        using var stream = typeof(Gen4Text).Assembly.GetManifestResourceStream("pk3DS.Core.NDS.Generation4.tbl")
            ?? throw new InvalidDataException("Falta la tabla de caracteres Gen4.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            int split = line.IndexOf('=');
            if (split == 4)
                table[Convert.ToInt32(line[..4], 16)] = line[5..];
        }
        return table;
    }
}
