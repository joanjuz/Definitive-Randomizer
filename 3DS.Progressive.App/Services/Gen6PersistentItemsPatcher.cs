using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace ThreeDS.Progressive.Services;

/// <summary>XY reconciliation only. No USUM signatures, code allocation or CRR signing.</summary>
internal static class Gen6PersistentItemsPatcher
{
    internal const int Gate = 0x14854, Restore = 0x148AC, TypeChart = 0xD12A8, KingShield = 0xBAA80;
    internal const string KnownShape = "240594E9F384A96822F5142A034B3E60B24F25ED83ED124BA826C174DADD9F2B";
    internal enum State { Original, Installed, Partial }
    private static readonly byte[] GateSignature = Convert.FromHexString("100099E50000D0E5010050E32000001A000054E30050A0E31D00009A");
    private static readonly byte[] RestoreSignature = Convert.FromHexString("0600A0E186AFFfEB0060A0E10010A0E15C0097E51120A0E3C1AEFFEB000050E300F020E32500000A");
    internal static bool HasAppliedPattern(byte[] data) => data.Length > Restore + 4
        && Word(data, Gate) == 0x8A000020 && Word(data, Restore) == 0xEA000025;
    private static uint Word(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
    private static void Set(byte[] data, int at, uint word) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), word);
    internal static string Shape(byte[] source)
    {
        byte[] data = (byte[])source.Clone();
        data.AsSpan(0, 0x80).Clear(); data.AsSpan(TypeChart, 324).Clear();
        Set(data, Gate, 0x1A000020); Set(data, Restore, 0x0A000025); data[KingShield + 4] = 0xFE;
        return Convert.ToHexString(SHA256.HashData(data));
    }
    internal static State Inspect(byte[] source, string expectedShape = KnownShape)
    {
        if (source.Length < TypeChart + 324 || !CroIntegrity.Rebuild(source).AsSpan().SequenceEqual(source))
            throw new InvalidDataException("DllBattle.cro está truncado o tiene hashes CRO inválidos.");
        uint gate = Word(source, Gate), restore = Word(source, Restore);
        if (gate is not (0x1A000020 or 0x8A000020) || restore is not (0x0A000025 or 0xEA000025))
            throw new InvalidDataException("Las instrucciones ARM fueron modificadas externamente.");
        byte[] normalized = (byte[])source.Clone(); Set(normalized, Gate, 0x1A000020); Set(normalized, Restore, 0x0A000025);
        if (Count(normalized, GateSignature) != 1 || !normalized.AsSpan(Gate - 12, GateSignature.Length).SequenceEqual(GateSignature)
            || Count(normalized, RestoreSignature) != 1 || !normalized.AsSpan(0x14888, RestoreSignature.Length).SequenceEqual(RestoreSignature))
            throw new InvalidDataException("Falta una firma Gen6 única en su posición acreditada.");
        Gen6KingShieldSignature.Validate(source);
        if (!source.AsSpan(KingShield, 8).SequenceEqual(Gen6KingShieldSignature.Original)
            && !source.AsSpan(KingShield, 8).SequenceEqual(Gen6KingShieldSignature.Patched))
            throw new InvalidDataException("La firma de Escudo Real está desplazada.");
        if (source.AsSpan(TypeChart, 324).ToArray().Any(v => v is not (0 or 2 or 4 or 8)) || Shape(source) != expectedShape)
            throw new InvalidDataException("DllBattle.cro contiene cambios ajenos a los rangos acreditados.");
        bool a = gate == 0x8A000020, b = restore == 0xEA000025;
        return a == b ? a ? State.Installed : State.Original : State.Partial;
    }
    internal static byte[] Patch(byte[] source, bool enable, string expectedShape = KnownShape)
    {
        if (Inspect(source, expectedShape) == State.Partial)
            throw new InvalidDataException("Instalación parcial: restaura la operación desde el historial antes de continuar.");
        byte[] output = (byte[])source.Clone();
        Set(output, Gate, enable ? 0x8A000020u : 0x1A000020u); Set(output, Restore, enable ? 0xEA000025u : 0x0A000025u);
        output = CroIntegrity.Rebuild(output);
        if (Inspect(output, expectedShape) != (enable ? State.Installed : State.Original))
            throw new InvalidDataException("Falló la verificación de la simulación de Persistent Items.");
        return output;
    }
    private static int Count(byte[] data, byte[] signature)
    {
        int count = 0, at = 0;
        while (at <= data.Length - signature.Length)
        { int relative = data.AsSpan(at).IndexOf(signature); if (relative < 0) break; count++; at += relative + 1; }
        return count;
    }
}
