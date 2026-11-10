#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>Behaviours this editor can recognise exactly. Everything else is <c>Unknown</c> and is never copied or shared.</summary>
public enum PlatinumScriptPattern { Unknown, PokecenterNurse, PlainMessage }

/// <summary>What a local script entry is, with the references that matter to copy it.</summary>
public sealed record PlatinumScriptInfo(int Number, PlatinumScriptPattern Pattern, int? LocalIdReference, int? CommonScript, int? MessageId, string Bytes)
{
    public string Describe() => Pattern switch
    {
        PlatinumScriptPattern.PokecenterNurse => $"script local {Number}: enfermera del Centro Pokémon (fija la variable 0x8007 = ID local {LocalIdReference} y llama al script común {CommonScript})",
        PlatinumScriptPattern.PlainMessage => $"script local {Number}: mensaje simple (texto {MessageId} del banco de textos del mapa)",
        _ => $"script local {Number}: no reconocido (no se copia ni se comparte)",
    };
}

/// <summary>
/// Minimal, exact reader/writer for one member of <c>fielddata/script/scr_seq.narc</c> (Platinum CPUE/1). Format (pret/pokeplatinum,
/// <c>asm/macros/scrcmd.inc</c>): a table of u32 offsets, each relative to the end of its own entry, terminated by u16 0xFD13, followed by the
/// code. Commands are u16 opcodes with fixed-size parameters; only the handful needed to recognise the patterns above are decoded.
/// Script numbers 1…N are the entries of the map's own file; 2000+ are common scripts (scripts_common, entry = number − 2000).
/// </summary>
public static class PlatinumScriptFile
{
    public const ushort EndOfTable = 0xFD13;
    public const int PokecenterNurseCommonScript = 2002, LastTalkedNpcVar = 0x8007, MaxEntries = 512;
    private const ushort OpEnd = 0x0002, OpCallCommonScript = 0x0014, OpSetVarFromValue = 0x0028, OpMessage = 0x002C, OpWaitButton = 0x0031,
        OpCloseMessage = 0x0034, OpPlaySE = 0x0049, OpLockAll = 0x0060, OpReleaseAll = 0x0061, OpFacePlayer = 0x0068;

    /// <summary>Absolute offsets of every entry; throws if the table is malformed or points outside the member.</summary>
    public static IReadOnlyList<int> EntryOffsets(byte[] member)
    {
        var offsets = new List<int>(); int at = 0;
        while (true)
        {
            if (at + 2 > member.Length) throw new InvalidDataException("Tabla de scripts sin terminador 0xFD13.");
            if (BinaryPrimitives.ReadUInt16LittleEndian(member.AsSpan(at)) == EndOfTable) break;
            if (at + 4 > member.Length || offsets.Count >= MaxEntries) throw new InvalidDataException("Tabla de scripts truncada o demasiado larga.");
            long target = at + 4L + BinaryPrimitives.ReadUInt32LittleEndian(member.AsSpan(at));
            offsets.Add(target > int.MaxValue ? -1 : (int)target); at += 4;
        }
        int header = at + 2;
        if (offsets.Any(o => o < header || o >= member.Length)) throw new InvalidDataException("Una entrada de script apunta fuera del código.");
        return offsets.AsReadOnly();
    }

    public static bool TryEntryCount(byte[] member, out int count)
    {
        try { count = EntryOffsets(member).Count; return true; } catch (InvalidDataException) { count = 0; return false; }
    }

    /// <summary>
    /// Appends one entry at the END of the table (existing script numbers do not change) and its code at the end of the member. The code block
    /// moves 4 bytes as a whole, so every relative jump inside it stays valid; each existing table value grows by 4. Verified before returning.
    /// </summary>
    public static byte[] AppendEntry(byte[] member, byte[] code, out int number)
    {
        if (code.Length == 0 || code.Length % 2 != 0) throw new ArgumentException("Código de script vacío o impar.", nameof(code));
        var offsets = EntryOffsets(member); int n = offsets.Count, header = 4 * n + 2, newHeader = header + 4;
        byte[] body = member.AsSpan(header).ToArray();
        int start = newHeader + body.Length; start = (start + 3) & ~3;
        byte[] result = new byte[start + code.Length];
        for (int i = 0; i < n; i++) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 * i), (uint)(offsets[i] + 4 - (4 * i + 4)));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 * n), (uint)(start - (4 * n + 4)));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4 * n + 4), EndOfTable);
        body.CopyTo(result, newHeader); code.CopyTo(result, start);
        var check = EntryOffsets(result);
        if (check.Count != n + 1 || !Enumerable.Range(0, n).All(i => check[i] == offsets[i] + 4) || check[n] != start
            || !result.AsSpan(newHeader, body.Length).SequenceEqual(body) || !result.AsSpan(start).SequenceEqual(code))
            throw new InvalidDataException("La ampliación del archivo de scripts no cumple las invariantes.");
        number = n + 1; return result;
    }

    /// <summary>The 12-byte local script every Pokémon Center nurse of the game uses, for a given local NPC id.</summary>
    public static byte[] NurseScript(int localId)
    {
        if ((uint)localId > 0xFFFE) throw new ArgumentOutOfRangeException(nameof(localId));
        byte[] code = new byte[12];
        Put(code, 0, OpSetVarFromValue); Put(code, 2, LastTalkedNpcVar); Put(code, 4, localId); Put(code, 6, OpCallCommonScript); Put(code, 8, PokecenterNurseCommonScript); Put(code, 10, OpEnd);
        return code;
    }

    /// <summary>Recognises script <paramref name="number"/> (1-based) of a member. Only exact, complete patterns are recognised.</summary>
    public static PlatinumScriptInfo Classify(byte[] member, int number)
    {
        var offsets = EntryOffsets(member);
        if (number < 1 || number > offsets.Count) throw new ArgumentOutOfRangeException(nameof(number));
        int at = offsets[number - 1]; byte[] code = member[at..]; string Hex(int length) => Convert.ToHexString(member.AsSpan(at, Math.Min(length, member.Length - at)));
        ushort U(int o) => o + 2 <= code.Length ? BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(o)) : (ushort)0xFFFF;
        if (U(0) == OpSetVarFromValue && U(2) == LastTalkedNpcVar && U(6) == OpCallCommonScript && U(8) == PokecenterNurseCommonScript && U(10) == OpEnd)
            return new(number, PlatinumScriptPattern.PokecenterNurse, U(4), PokecenterNurseCommonScript, null, Hex(12));
        if (U(0) == OpPlaySE && U(2) == 0x05DC && U(4) == OpLockAll && U(6) == OpFacePlayer && U(8) == OpMessage && code.Length >= 19
            && BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(11)) == OpWaitButton && BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(13)) == OpCloseMessage
            && BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(15)) == OpReleaseAll && BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(17)) == OpEnd)
            return new(number, PlatinumScriptPattern.PlainMessage, null, null, code[10], Hex(19));
        return new(number, PlatinumScriptPattern.Unknown, null, null, null, Hex(24));
    }

    private static void Put(byte[] b, int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), checked((ushort)v));
}
