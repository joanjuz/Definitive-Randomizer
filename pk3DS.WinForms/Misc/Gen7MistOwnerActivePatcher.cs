using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace pk3DS.WinForms;

/// <summary>
/// Gen7 Mist / Neblina owner-active lifetime v5.76.2.
///
/// Validated in-game behavior:
/// - +3 priority comes from normal move data / the balance template;
/// - vanilla side stat-drop protection remains;
/// - persists while the original Mist user remains active;
/// - ally switching does not end it;
/// - original owner switch/faint ends it;
/// - immediate recast transfers ownership;
/// - old owner returning does not resurrect the old Mist;
/// - duplicate recast remains rejected while the owner is active;
/// - Mist itself contributes no critical stage.
///
/// The recast helpers share canonical zero holes inside the permanent
/// Meditate/Smokescreen v8.2 CombinedPayload. This class owns the Mist hooks
/// and the FCA00 lifecycle payload; Gen7MeditateV73Patcher preserves the
/// shared helper bytes when it runs later.
/// </summary>
internal static class Gen7MistOwnerActivePatcher
{
    private const int Mist = 54;

    private const int ExpiryHook = 0x000C0C80;
    private const int ActivationHook = 0x000C744C;
    private const int CallbackHook = 0x000C0790;
    private const int GateHook = 0x00022460;
    private const int CommitHook = 0x0002247C;
    private const int CritHook = 0x0007EE34;

    private const int BaseCave = 0x000FCA00;
    private const int CritShared = 0x000FCB78;
    private const int GateCommitShared = 0x000FCE88;

    private const uint ExpiryStock = 0xEBFE825D;
    private const uint ActivationStock = 0xE92D40F0;
    private const uint CallbackStock = 0xE92D41F0;
    private const uint GateStock = 0xEB01ADDE;
    private const uint CommitStock = 0xEB02793C;
    private const uint CritStock = 0xEB018DF3;

    private const uint ExpiryPatched = 0xEB00EF5E;
    private const uint ActivationPatched = 0xEA00D57B;
    private const uint CallbackPatched = 0xEA00F0BE;
    private const uint GatePatched = 0xEB036A88;
    private const uint CommitPatched = 0xEB036AA9;
    private const uint CritPatched = 0xEB01F74F;

    private static readonly byte[] BasePayload = Hex("00 00 50 E3 09 00 00 0A 10 10 D0 E5 02 00 51 E3 06 00 00 1A 30 10 90 E5 07 10 01 E2 02 00 51 E3 02 00 00 1A B8 13 D0 E1 03 00 51 E3 1E FF 2F 01 F1 92 FD EA 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 F0 40 2D E9 02 50 A0 E1 1C D0 4D E2 00 60 A0 E1 01 40 A0 E1 03 70 A0 E1 04 00 A0 E1 05 10 A0 E1 0C 56 FE EB 00 00 50 E3 01 00 00 0A 09 10 A0 E3 00 F0 20 E3 7A 2A FF EA 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 F0 41 2D E9 00 80 A0 E1 02 50 A0 E1 01 70 A0 E1 03 60 A0 E1 30 10 98 E5 A1 11 A0 E1 1F 10 01 E2 18 00 51 E3 07 00 00 2A 07 00 A0 E1 F5 55 FE EB 00 00 50 E3 03 00 00 0A 09 10 A0 E3 19 4D FE EB 00 00 50 E3 02 00 00 1A 08 00 A0 E1 C8 92 FD EB FF 50 A0 E3 2D 0F FF EA");
    private static readonly byte[] CritSharedPayload = Hex("F0 41 2D E9 00 40 A0 E1 19 80 D4 E5 00 50 A0 E3 05 10 A0 E1 02 00 A0 E3 CE 92 FD EB 00 60 B0 E1 0A 00 00 0A B8 03 D6 E1 03 00 50 E3 03 00 00 1A 30 00 96 E5 D0 01 E4 E7 08 00 50 E1 09 00 00 0A 06 00 A0 E1 79 92 FD EB 00 60 B0 E1 F4 FF FF 1A 01 50 85 E2 18 00 55 E3 EC FF FF 3A 04 00 A0 E1 F0 41 BD E8 89 96 FF EA 3B 02 D4 E5 68 11 94 E5 07 00 11 E3 03 00 80 12 03 00 50 E3 03 00 A0 83 F0 81 BD E8");
    private static readonly byte[] GateCommitSharedPayload = Hex("30 40 2D E9 00 40 A0 E1 01 50 A0 E1 03 00 58 E3 19 00 00 1A 07 10 A0 E1 02 00 A0 E3 09 92 FD EB 00 00 50 E3 0F 00 00 0A B8 13 D0 E1 03 00 51 E3 01 00 00 0A B9 91 FD EB F8 FF FF EA 30 10 90 E5 A1 11 A0 E1 1F 10 01 E2 06 00 A0 E1 EF 54 FE EB 00 00 50 E3 03 00 00 0A 09 10 A0 E3 13 4C FE EB 00 00 50 E3 08 00 00 1A 14 00 94 E5 00 00 50 E3 01 00 00 0A 04 00 A0 E1 5D 41 FE EB 05 10 A0 E1 04 00 A0 E1 33 43 FE EB 30 80 BD E8 00 00 A0 E3 30 80 BD E8 00 00 00 00 00 00 00 00 00 00 00 00 B0 40 2D E9 01 40 A0 E1 02 50 A0 E1 8E 0E FF EB 00 70 A0 E1 03 00 54 E3 12 00 00 1A 00 00 57 E3 10 00 00 0A 04 20 95 E5 06 10 A0 E3 07 00 A0 E1 5F 91 FD EB 00 20 95 E5 05 10 A0 E3 07 00 A0 E1 5B 91 FD EB 00 10 95 E5 A1 11 A0 E1 1F 10 01 E2 06 00 A0 E1 C5 54 FE EB 00 00 50 E3 01 00 00 0A 09 10 A0 E3 6C 46 FE EB 07 00 A0 E1 B0 80 BD E8");

    internal static bool IsRequested(CustomBattleEffectPatcher.BattlePatchRequest request)
    {
        if (request is null || request.Move != Mist)
            return false;

        return HasAnyToken(
            request.BattlePatch,
            "Gen7MistOwnerActiveV5762",
            "Gen7MistOwnerActive",
            "MistOwnerActive",
            "NeblinaOwnerActive");
    }

    internal static void ConfigureMoveData(
        int generation,
        int move,
        byte[] data,
        string battlePatch)
    {
        // Intentionally no-op.
        //
        // Category and +3 Priority are normal template fields and are applied
        // by the standard move-data pipeline before this Battle.cro patch.
        // The owner-active lifetime and crit correction live only in Battle.cro.
    }

    internal static int Apply()
    {
        string path = Path.Combine(Main.RomFSPath, "Battle.cro");

        if (!File.Exists(path))
            return -1;

        byte[] data = File.ReadAllBytes(path);

        if (data.Length < 0x13C000 ||
            data.Length < BaseCave + BasePayload.Length ||
            data.Length < CritShared + CritSharedPayload.Length ||
            data.Length < GateCommitShared + GateCommitSharedPayload.Length)
        {
            return -1;
        }

        bool sharedCritExact =
            Match(data, CritShared, CritSharedPayload);

        bool sharedGateExact =
            Match(data, GateCommitShared, GateCommitSharedPayload);

        bool sharedCritFree =
            IsZero(data, CritShared, CritSharedPayload.Length);

        bool sharedGateFree =
            IsZero(data, GateCommitShared, GateCommitSharedPayload.Length);

        // Refuse any foreign use of the canonical shared holes.
        if ((!sharedCritExact && !sharedCritFree) ||
            (!sharedGateExact && !sharedGateFree))
        {
            return -1;
        }

        bool alreadyFinal =
            ReadUInt32(data, ExpiryHook) == ExpiryPatched &&
            ReadUInt32(data, ActivationHook) == ActivationPatched &&
            ReadUInt32(data, CallbackHook) == CallbackPatched &&
            ReadUInt32(data, GateHook) == GatePatched &&
            ReadUInt32(data, CommitHook) == CommitPatched &&
            ReadUInt32(data, CritHook) == CritPatched &&
            Match(data, BaseCave, BasePayload) &&
            sharedCritExact &&
            sharedGateExact;

        if (alreadyFinal)
            return 0;

        bool cleanHooks =
            ReadUInt32(data, ExpiryHook) == ExpiryStock &&
            ReadUInt32(data, ActivationHook) == ActivationStock &&
            ReadUInt32(data, CallbackHook) == CallbackStock &&
            ReadUInt32(data, GateHook) == GateStock &&
            ReadUInt32(data, CommitHook) == CommitStock &&
            ReadUInt32(data, CritHook) == CritStock;

        if (!cleanHooks || !IsZero(data, BaseCave, BasePayload.Length))
            return -1;

        BackupOnce(path, ".bak_mist_owner_active_v5762");

        int changed = 0;

        // These two regions are canonical zero holes in Smokescreen v8.2.
        // They may already be present if Gen7MeditateV73Patcher ran first.
        changed += WriteBytesIfDifferent(data, CritShared, CritSharedPayload) ? 1 : 0;
        changed += WriteBytesIfDifferent(data, GateCommitShared, GateCommitSharedPayload) ? 1 : 0;

        changed += WriteBytesIfDifferent(data, BaseCave, BasePayload) ? 1 : 0;

        changed += WriteUInt32IfDifferent(data, ExpiryHook, ExpiryPatched) ? 1 : 0;
        changed += WriteUInt32IfDifferent(data, ActivationHook, ActivationPatched) ? 1 : 0;
        changed += WriteUInt32IfDifferent(data, CallbackHook, CallbackPatched) ? 1 : 0;
        changed += WriteUInt32IfDifferent(data, GateHook, GatePatched) ? 1 : 0;
        changed += WriteUInt32IfDifferent(data, CommitHook, CommitPatched) ? 1 : 0;
        changed += WriteUInt32IfDifferent(data, CritHook, CritPatched) ? 1 : 0;

        File.WriteAllBytes(path, data);
        return changed;
    }

    internal static void EnsureTemplateRow(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string[] lines = File.ReadAllLines(path);

        int headerIndex = -1;
        string[] header = Array.Empty<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("Move,", StringComparison.OrdinalIgnoreCase))
                continue;

            headerIndex = i;
            header = SplitCsvLine(lines[i]);
            break;
        }

        if (headerIndex < 0 || header.Length == 0)
            return;

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Length; i++)
            columns[header[i].Trim()] = i;

        int rowIndex = -1;
        string[] fields = Array.Empty<string>();

        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            string[] candidate = SplitCsvLine(lines[i]);

            if (candidate.Length == 0 ||
                !int.TryParse(
                    Get(candidate, 0),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int move) ||
                move != Mist)
            {
                continue;
            }

            rowIndex = i;
            fields = candidate;
            break;
        }

        if (fields.Length < header.Length)
            Array.Resize(ref fields, header.Length);

        if (fields.Length == 0)
            fields = new string[header.Length];

        Set(fields, columns, "Move", Mist.ToString(CultureInfo.InvariantCulture));
        Set(fields, columns, "Category", "Status");
        Set(fields, columns, "Priority", "3");
        Set(fields, columns, "CriticalStage", string.Empty);
        Set(fields, columns, "TurnMin", string.Empty);
        Set(fields, columns, "TurnMax", string.Empty);
        Set(fields, columns, "BattlePatch", "Gen7MistOwnerActiveV5762");
        Set(
            fields,
            columns,
            "Notes",
            "Neblina / Mist: +3 priority; remains active while the original user stays active; ally switching does not end it; owner switch/faint ends it; immediate recast transfers ownership; Mist itself gives no critical-stage boost");
        Set(fields, columns, "ZEffect", string.Empty);

        string finalRow = JoinCsvLine(fields);

        if (rowIndex >= 0)
        {
            lines[rowIndex] = finalRow;
            File.WriteAllLines(path, lines);
            return;
        }

        string existing = File.ReadAllText(path);
        string separator =
            existing.Length == 0 || existing.EndsWith('\n')
                ? string.Empty
                : Environment.NewLine;

        File.AppendAllText(path, separator + finalRow + Environment.NewLine);
    }

    private static void Set(
        string[] fields,
        Dictionary<string, int> columns,
        string name,
        string value)
    {
        if (!columns.TryGetValue(name, out int index) ||
            index < 0 ||
            index >= fields.Length)
        {
            return;
        }

        fields[index] = value ?? string.Empty;
    }

    private static bool HasAnyToken(string value, params string[] wanted)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var set = wanted
            .Select(NormalizeToken)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return value
            .Split(
                new[] { ',', ';', '|' },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(NormalizeToken)
            .Any(set.Contains);
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
            return uint.MaxValue;

        return BitConverter.ToUInt32(data, offset);
    }

    private static bool WriteUInt32IfDifferent(byte[] data, int offset, uint value)
    {
        if (ReadUInt32(data, offset) == value)
            return false;

        BitConverter.GetBytes(value).CopyTo(data, offset);
        return true;
    }

    private static bool WriteBytesIfDifferent(byte[] data, int offset, byte[] value)
    {
        if (Match(data, offset, value))
            return false;

        value.CopyTo(data, offset);
        return true;
    }

    private static bool Match(byte[] data, int offset, byte[] value)
    {
        if (offset < 0 || offset + value.Length > data.Length)
            return false;

        for (int i = 0; i < value.Length; i++)
        {
            if (data[offset + i] != value[i])
                return false;
        }

        return true;
    }

    private static bool IsZero(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
            return false;

        for (int i = 0; i < length; i++)
        {
            if (data[offset + i] != 0)
                return false;
        }

        return true;
    }

    private static void BackupOnce(string path, string suffix)
    {
        if (!AutomaticBackups.Enabled)
            return;

        string backup = path + suffix;
        if (!File.Exists(backup))
            File.Copy(path, backup);
    }

    private static byte[] Hex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<byte>();

        string[] parts =
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        byte[] result = new byte[parts.Length];

        for (int i = 0; i < parts.Length; i++)
            result[i] = Convert.ToByte(parts[i], 16);

        return result;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result.ToArray();
    }

    private static string Get(string[] fields, int index)
        => index >= 0 && index < fields.Length
            ? fields[index]?.Trim() ?? string.Empty
            : string.Empty;

    private static string JoinCsvLine(IEnumerable<string> fields)
        => string.Join(",", fields.Select(EscapeCsvField));

    private static string EscapeCsvField(string value)
    {
        value ??= string.Empty;

        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}