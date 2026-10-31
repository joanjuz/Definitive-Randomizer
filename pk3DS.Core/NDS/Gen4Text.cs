#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private static readonly Lazy<Dictionary<string, int>> Codes = new(() =>
    {
        // Reverse table; for a text mapped twice the later code wins, as in UPR-ZX.
        var codes = new Dictionary<string, int>(StringComparer.Ordinal);
        var table = Table.Value;
        for (int c = 0; c < table.Length; c++)
            if (table[c] is { Length: > 0 } text)
                codes[text] = c;
        return codes;
    });

    public static IReadOnlyList<string> Read(byte[] data)
    {
        var lines = new List<string>();
        foreach (int[] chars in RawLines(data))
            lines.Add(Decode(chars.Length > 0 && chars[0] == 0xF100 ? Decompress(chars) : chars));
        return lines;
    }

    /// <summary>
    /// Rewrites the given lines and keeps every other line's characters exactly as they were (compressed lines included).
    /// Changed lines are stored uncompressed; the file keeps its key, so the result reads back with the same text elsewhere.
    /// </summary>
    public static byte[] Replace(byte[] data, IReadOnlyDictionary<int, string> changes)
    {
        var lines = RawLines(data);
        foreach (var (line, text) in changes)
        {
            if (line < 0 || line >= lines.Count)
                throw new ArgumentOutOfRangeException(nameof(changes), "Línea de texto Gen4 fuera de rango: " + line);
            lines[line] = Encode(text);
        }
        int fileKey = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        var result = new byte[4 + lines.Count * 8 + lines.Sum(l => l.Length * 2)];
        BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)lines.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)fileKey);
        int key = (fileKey * 0x2FD) & 0xFFFF, offset = 4 + lines.Count * 8;
        for (int i = 0; i < lines.Count; i++)
        {
            int lineKey = (key * (i + 1)) & 0xFFFF;
            uint mask = (uint)(lineKey | (lineKey << 16));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + i * 8), (uint)offset ^ mask);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8 + i * 8), (uint)lines[i].Length ^ mask);
            int charKey = (0x91BD3 * (i + 1)) & 0xFFFF;
            foreach (int c in lines[i])
            {
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset), (ushort)(c ^ charKey));
                charKey = (charKey + 0x493D) & 0xFFFF;
                offset += 2;
            }
        }
        return result;
    }

    // Text to characters, the inverse of Decode: \xNNNN raw, \vNNNN\zNNNN… variables, \n \p \l breaks, the rest by table.
    private static int[] Encode(string text)
    {
        var codes = Codes.Value;
        var chars = new List<int>();
        int i = 0;
        int Hex(int at) => at + 6 <= text.Length ? Convert.ToInt32(text.Substring(at + 2, 4), 16) : throw new InvalidDataException("Código incompleto en el texto Gen4: " + text);
        while (i < text.Length)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                switch (text[i + 1])
                {
                    case 'x': chars.Add(Hex(i)); i += 6; continue;
                    case 'v':
                        chars.Add(0xFFFE);
                        chars.Add(Hex(i));
                        i += 6;
                        var values = new List<int>();
                        while (i + 1 < text.Length && text[i] == '\\' && text[i + 1] == 'z')
                        {
                            values.Add(Hex(i));
                            i += 6;
                        }
                        // A variable without arguments is written as \x0000 (its zero count).
                        if (values.Count > 0)
                        {
                            chars.Add(values.Count);
                            chars.AddRange(values);
                        }
                        continue;
                    case 'n': chars.Add(0xE000); i += 2; continue;
                    case 'p': chars.Add(0x25BC); i += 2; continue;
                    case 'l': chars.Add(0x25BD); i += 2; continue;
                }
            }
            int length = Math.Min(6, text.Length - i);
            while (length > 0 && !codes.ContainsKey(text.Substring(i, length)))
                length--;
            if (length == 0)
                throw new InvalidDataException($"El carácter «{text[i]}» no existe en la tabla de texto Gen4.");
            chars.Add(codes[text.Substring(i, length)]);
            i += length;
        }
        chars.Add(0xFFFF);
        return [.. chars];
    }

    private static List<int[]> RawLines(byte[] data)
    {
        if (data.Length < 4)
            throw new InvalidDataException("Archivo de texto Gen4 truncado.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        int fileKey = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        if (4 + count * 8 > data.Length)
            throw new InvalidDataException("La tabla de líneas del texto Gen4 está truncada.");
        int key = (fileKey * 0x2FD) & 0xFFFF;
        var lines = new List<int[]>(count);
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
            lines.Add(chars);
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
