// Ported from pk3DS.WinForms/Misc/Gen7FairyLockV11Patcher.cs at f459e40.
// Only namespace and temporary IO roots differ; battle instructions are unchanged.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

/// <summary>
/// Gen7 Fairy Lock / Cerrojo Feerico v11.
///
/// Validated in-game behavior:
/// - Fairy / Status
/// - Quality 13 (Unique Effect)
/// - Effect 294 (Soak special route)
/// - AllPokemon targeting
/// - every active Pokemon has its typing replaced with pure Fairy.
///
/// The Battle.cro payload/event/relocation below are copied exactly from the
/// validated implementation. It intentionally does not use Soak's vanilla
/// "already Water" eligibility check.
/// </summary>
internal static class Gen7FairyLockV11Patcher
{
    private const int FairyLock = 587;

    private const int Descriptor = 0x00105D60;
    private const int DescriptorHandlerPointer = 0x00105D64;

    private const int Cave = 0x000FCFA8;
    private const int CaveLength = 0x58;

    private const int RelocationIndex = 5843;

    private const uint StockEvent = 0x000000BD;
    private const uint FinalEvent = 0x000000BF;

    private const int StockHandler = 0x000C5C94;
    private const int FinalHandler = 0x000FCFA8;

    private static readonly byte[] FinalPayload = Hex("F0 5F 2D E9 01 80 A0 E1 02 70 A0 E1 05 00 A0 E3 E6 2A FE EB 00 60 50 E2 00 40 A0 E3 05 00 00 0A 31 AA 04 E3 00 90 A0 E3 01 B0 A0 E3 AE 2D FF EA 00 F0 20 E3 00 F0 20 E3 F0 9F BD E8 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00");

    internal static bool IsRequested(CustomBattleEffectPatcher.BattlePatchRequest request)
    {
        if (request is null || request.Move != FairyLock)
            return false;

        return HasToken(request.BattlePatch);
    }

    internal static void ConfigureMoveData(int generation, int move, byte[] data, string battlePatch)
    {
        if (generation != 7 ||
            move != FairyLock ||
            data is null ||
            data.Length < 0x1E ||
            !HasToken(battlePatch))
        {
            return;
        }

        // Exact move-data route validated with the working v11 build.
        data[0x00] = 17; // Fairy
        data[0x01] = 13; // Unique Effect
        data[0x02] = 0;  // Status

        // Clear generic infliction fields.
        data[0x08] = 0;
        data[0x09] = 0;
        data[0x0A] = 0;

        data[0x10] = 0x26; // Effect 294 = 0x0126
        data[0x11] = 0x01;

        data[0x13] = 0;
        data[0x14] = 8; // AllPokemon

        // Type replacement is emitted by Battle.cro, not generic stat data.
        for (int i = 0x15; i <= 0x1D; i++)
            data[i] = 0;
    }

    internal static int Apply()
    {
        string path = FindBattleCro();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return -1;

        byte[] data = File.ReadAllBytes(path);

        if (data.Length < Cave+CaveLength ||
            data.Length < Descriptor+8)
        {
            return -1;
        }

        if (!TryGetRelocationInfo(
                data,
                RelocationIndex,
                out int relocation,
                out int output,
                out int target,
                out int seg0))
        {
            return -1;
        }

        if (output != DescriptorHandlerPointer)
            return -1;

        bool alreadyFinal =
            ReadUInt32(data,Descriptor) == FinalEvent &&
            target == FinalHandler &&
            Match(data,Cave,FinalPayload);

        if (alreadyFinal)
            return 0;

        bool cleanBase =
            ReadUInt32(data,Descriptor) == StockEvent &&
            target == StockHandler &&
            IsZero(data,Cave,CaveLength);

        if (!cleanBase)
            return -1;

        BackupOnce(path,".bak_fairy_lock_v11");

        int changed = 0;

        changed += WriteBytesIfDifferent(data,Cave,FinalPayload) ? 1 : 0;
        changed += WriteUInt32IfDifferent(data,Descriptor,FinalEvent) ? 1 : 0;

        uint finalAddend = checked((uint)(FinalHandler-seg0));

        changed +=
            WriteUInt32IfDifferent(data,relocation+8,finalAddend)
                ? 1
                : 0;

        File.WriteAllBytes(path,data);
        return changed;
    }

    internal static void EnsureTemplateRow(string path)
    {
        EnsureMoveRow(path,FairyLock,ConfigureTemplate);
    }

    private static void ConfigureTemplate(string[] fields)
    {
        fields[1] = "Fairy";
        fields[2] = "Status";
        fields[3] = "13";

        fields[12] = "294";
        fields[20] = "AllPokemon";

        // Keep the successful v11 route free of generic stat payload.
        for (int i = 22; i <= 36; i++)
            fields[i] = string.Empty;

        fields[41] = "Gen7FairyLockAllFairyV11";
        fields[42] =
            "Cerrojo Feerico / Fairy Lock: all active Pokemon become pure Fairy (v11)";
    }

    private static bool HasToken(string value)
        => HasAnyToken(
            value,
            "Gen7FairyLockAllFairyV11",
            "Gen7FairyLockAllFairy",
            "FairyLockAllFairy",
            "CerrojoFeericoTodosHada");

    private static bool HasAnyToken(string value,params string[] wanted)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var set = wanted
            .Select(NormalizeToken)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return value
            .Split(
                [',',';','|'],
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

    private static bool TryGetRelocationInfo(
        byte[] data,
        int relocationIndex,
        out int relocation,
        out int output,
        out int target,
        out int seg0)
    {
        relocation = 0;
        output = 0;
        target = 0;
        seg0 = 0;

        if (data.Length < 0x12C)
            return false;

        int segTable = checked((int)ReadUInt32(data,0xC8));

        if (segTable < 0 || segTable+16 > data.Length)
            return false;

        seg0 = checked((int)ReadUInt32(data,segTable));
        int seg1 = checked((int)ReadUInt32(data,segTable+12));
        int relocBase = checked((int)ReadUInt32(data,0x128));

        relocation = checked(relocBase+(relocationIndex*12));

        if (relocation < 0 || relocation+12 > data.Length)
            return false;

        uint tag = ReadUInt32(data,relocation);
        ulong tag64 = tag;
        long outOff = (long)Math.Truncate(tag64/16.0);

        output = checked((int)(seg1+outOff));

        uint addend = ReadUInt32(data,relocation+8);
        target = checked((int)(seg0+(long)addend));

        return true;
    }

    private static void EnsureMoveRow(
        string path,
        int move,
        Action<string[]> configure)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string[] lines = File.ReadAllLines(path);

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            string[] fields = SplitCsvLine(lines[i]);

            if (fields.Length == 0 ||
                !int.TryParse(
                    Get(fields,0),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int rowMove) ||
                rowMove != move)
            {
                continue;
            }

            if (fields.Length < 43)
                Array.Resize(ref fields,43);

            configure(fields);
            lines[i] = JoinCsvLine(fields);
            File.WriteAllLines(path,lines);
            return;
        }

        string[] row = new string[43];
        row[0] = move.ToString(CultureInfo.InvariantCulture);
        configure(row);

        string existing = File.ReadAllText(path);
        string separator =
            existing.Length == 0 || existing.EndsWith('\n')
                ? string.Empty
                : Environment.NewLine;

        File.AppendAllText(
            path,
            separator+JoinCsvLine(row)+Environment.NewLine);
    }

    private static string FindBattleCro()
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(Main.RomFSPath))
            roots.Add(Main.RomFSPath);

        roots.Add(Main.Workspace);
        roots.Add(Main.Workspace);

        foreach (string root in roots
                     .Where(Directory.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] candidates =
            [
                Path.Combine(root,"Battle.cro"),
                Path.Combine(root,"ExtractedRomFS","Battle.cro"),
                Path.Combine(root,"RomFS","Battle.cro"),
                Path.Combine(root,"romfs","Battle.cro"),
            ];

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            try
            {
                string match = Directory
                    .GetFiles(root,"Battle.cro",SearchOption.AllDirectories)
                    .OrderByDescending(
                        z => z.Contains(
                            "ExtractedRomFS",
                            StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static void BackupOnce(string path,string suffix)
    {
        if (!AutomaticBackups.Enabled)
            return;

        string backup = path+suffix;

        if (!File.Exists(backup))
            File.Copy(path,backup,false);
    }

    private static uint ReadUInt32(byte[] data,int offset)
    {
        if (offset < 0 || offset+4 > data.Length)
            return uint.MaxValue;

        return BitConverter.ToUInt32(data,offset);
    }

    private static bool WriteUInt32IfDifferent(
        byte[] data,
        int offset,
        uint value)
    {
        if (ReadUInt32(data,offset) == value)
            return false;

        BitConverter.GetBytes(value).CopyTo(data,offset);
        return true;
    }

    private static bool WriteBytesIfDifferent(
        byte[] data,
        int offset,
        byte[] value)
    {
        if (Match(data,offset,value))
            return false;

        value.CopyTo(data,offset);
        return true;
    }

    private static bool Match(
        byte[] data,
        int offset,
        byte[] value)
    {
        if (offset < 0 || offset+value.Length > data.Length)
            return false;

        for (int i=0; i<value.Length; i++)
        {
            if (data[offset+i] != value[i])
                return false;
        }

        return true;
    }

    private static bool IsZero(
        byte[] data,
        int offset,
        int length)
    {
        if (length < 0 ||
            offset < 0 ||
            offset+length > data.Length)
        {
            return false;
        }

        for (int i=0; i<length; i++)
        {
            if (data[offset+i] != 0)
                return false;
        }

        return true;
    }

    private static byte[] Hex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        string[] parts =
            value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

        byte[] result = new byte[parts.Length];

        for (int i=0; i<parts.Length; i++)
            result[i] = Convert.ToByte(parts[i],16);

        return result;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;

        for (int i=0; i<line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (quoted &&
                    i+1 < line.Length &&
                    line[i+1] == '"')
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

    private static string Get(string[] fields,int index)
        => index >= 0 && index < fields.Length
            ? fields[index]?.Trim() ?? string.Empty
            : string.Empty;

    private static string JoinCsvLine(IEnumerable<string> fields)
        => string.Join(",",fields.Select(EscapeCsvField));

    private static string EscapeCsvField(string value)
    {
        value ??= string.Empty;

        if (value.IndexOfAny([',','"','\r','\n']) < 0)
            return value;

        return "\"" + value.Replace("\"","\"\"") + "\"";
    }
}