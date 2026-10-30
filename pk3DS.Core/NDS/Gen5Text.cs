#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
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
