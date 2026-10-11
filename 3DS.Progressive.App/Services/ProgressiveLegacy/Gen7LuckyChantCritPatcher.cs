// Ported from pk3DS.WinForms/Misc/Gen7LuckyChantCritPatcher.cs at f459e40.
// Only namespace and temporary IO roots differ; battle instructions are unchanged.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

/// <summary>
/// Gen7 Lucky Chant / Conjuro critical-boost v17.
///
/// Validated in-game behavior:
/// - preserves vanilla Lucky Chant: opponents cannot critically hit the
///   protected side;
/// - while the side condition is active, attacks made by that protected side
///   receive +1 critical-hit stage;
/// - naturally applies to both allies in doubles and persists through switching;
/// - duration is 8 turns.
///
/// Architecture:
/// side slot 5 -> BF908 -> event 0x44.
/// The stock BFE90 defender-side critical veto is preserved inside the custom
/// handler, which additionally applies work[0x30] += 1 when work[3] (attacker)
/// belongs to the protected side.
///
/// A1 factory D9EE4, A1 descriptors, the 24/8/2/1 crit table and the central
/// critical resolver are intentionally untouched.
/// </summary>
internal static class Gen7LuckyChantCritPatcher
{
    private const int LuckyChant = 381;

    private const int ExpectedSize = 0x13C000;

    private const int Duration = 0x000C52F8;

    private const int SideSlotRecord = 0x00104260;
    private const int SideSlotFactory = 0x000BF908;
    private const int SideSlotFactoryRelocationIndex = 5396;
    private const uint SideSlotFactoryRelocationTag = 0x00072641;
    private const uint SideSlotFactoryRelocationAddend = 0x000BF788;

    private const int Descriptor = 0x001041F0;
    private const int DescriptorHandlerPointer = 0x001041F4;
    private const int DescriptorHandlerRelocationIndex = 5413;
    private const uint DescriptorHandlerRelocationTag = 0x00071F41;

    private const uint StockHandlerAddend = 0x000BFD10; // -> BFE90
    private const uint FinalHandlerAddend = 0x000FC7F8; // -> FC978

    private const int StockHandler = 0x000BFE90;
    private const int FinalHandler = 0x000FC978;
    private const int Cave = FinalHandler;

    private static readonly byte[] DurationStock = Hex("05 20 A0 E3");
    private static readonly byte[] DurationFinal = Hex("08 20 A0 E3");

    private static readonly byte[] SideSlotFactorySignature = Hex(
        "01 10 A0 E3 00 10 80 E5 00 00 9F E5 1E FF 2F E1");

    private static readonly byte[] StockHandlerSignature = Hex(
        "70 40 2D E9 01 60 A0 E1 02 40 A0 E1 04 00 A0 E3 " +
        "2C 1F FF EB FF 50 00 E2 06 00 A0 E1 04 00 90 E5 " +
        "05 10 A0 E1 C4 2F FF EB 04 00 50 E1 03 00 00 1A " +
        "70 40 BD E8 01 10 A0 E3 46 00 A0 E3 9C 0B FF EA " +
        "70 80 BD E8");

    private static readonly byte[] CritFlowSignature = Hex(
        "44 10 A0 E3 0A 00 A0 E1 8A CC FF EB " +
        "46 00 A0 E3 00 F0 20 E3 2B 23 00 EB");

    private static readonly byte[] A1FactoryStockSignature = Hex(
        "03 10 A0 E3 00 10 80 E5 00 00 9F E5 " +
        "1E FF 2F E1 00 00 00 00");

    // Exact bytes copied from the in-game validated v17 Battle.cro.
    private static readonly byte[] FinalPayload = Hex("70 40 2D E9 01 60 A0 E1 02 40 A0 E1 04 00 A0 E3 72 2C FE EB FF 50 00 E2 04 00 96 E5 05 10 A0 E1 0B 3D FE EB 04 00 50 E1 02 00 00 1A 01 10 A0 E3 46 00 A0 E3 E4 18 FE EB 03 00 A0 E3 67 2C FE EB FF 50 00 E2 04 00 96 E5 05 10 A0 E1 00 3D FE EB 04 00 50 E1 06 00 00 1A 30 00 A0 E3 5F 2C FE EB 01 00 80 E2 FF 10 00 E2 70 40 BD E8 30 00 A0 E3 D5 18 FE EA 70 80 BD E8");

    internal static bool IsRequested(CustomBattleEffectPatcher.BattlePatchRequest request)
    {
        if (request is null || request.Move != LuckyChant)
            return false;

        return HasToken(request.BattlePatch);
    }

        internal static void ConfigureMoveData(
        int generation,
        int move,
        byte[] data,
        string battlePatch)
    {
        // v17 was validated in-game against Lucky Chant's VANILLA move data.
        //
        // Do not rewrite Category, Effect, TurnMin, TurnMax, CriticalStage,
        // Quality, Targeting, or any other move-data field here.
        //
        // The 8-turn duration is patched directly in Battle.cro at C52F8.
        // The protected-side +1 critical stage and the vanilla incoming-crit
        // prevention are both handled by the validated v17 Battle.cro handler.
        //
        // This method intentionally remains a no-op so the existing central
        // integration hook does not need to be removed.
    }

    internal static int Apply()
    {
        string path = FindBattleCro();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return -1;

        byte[] data = File.ReadAllBytes(path);

        if (data.Length != ExpectedSize ||
            data.Length < Cave+FinalPayload.Length ||
            data.Length < Descriptor+8)
        {
            return -1;
        }

        string magic = Encoding.ASCII.GetString(data,0x80,4);
        if (magic != "CRO0" && magic != "FIXD")
            return -1;

        if (ReadUInt32(data,SideSlotRecord) != 5)
            return -1;

        if (!TryGetRelocationEntry(
                data,
                SideSlotFactoryRelocationIndex,
                SideSlotFactoryRelocationTag,
                2,
                0,
                SideSlotFactoryRelocationAddend,
                SideSlotFactoryRelocationAddend,
                out _))
        {
            return -1;
        }

        if (!Match(data,SideSlotFactory,SideSlotFactorySignature) ||
            ReadUInt32(data,Descriptor) != 0x44 ||
            !Match(data,StockHandler,StockHandlerSignature) ||
            !Match(data,0x0007EE90,CritFlowSignature) ||
            !Match(data,0x000D9EE4,A1FactoryStockSignature))
        {
            return -1;
        }

        if (!TryGetRelocationEntry(
                data,
                DescriptorHandlerRelocationIndex,
                DescriptorHandlerRelocationTag,
                2,
                0,
                StockHandlerAddend,
                FinalHandlerAddend,
                out int handlerRelocation))
        {
            return -1;
        }

        uint currentHandlerAddend =
            ReadUInt32(data,handlerRelocation+8);

        bool alreadyFinal =
            Match(data,Duration,DurationFinal) &&
            currentHandlerAddend == FinalHandlerAddend &&
            Match(data,Cave,FinalPayload);

        if (alreadyFinal)
            return 0;

        bool cleanBase =
            Match(data,Duration,DurationStock) &&
            currentHandlerAddend == StockHandlerAddend &&
            IsZero(data,Cave,FinalPayload.Length);

        if (!cleanBase)
            return -1;

        BackupOnce(path,".bak_lucky_chant_crit_v17");

        int changed = 0;

        changed +=
            WriteBytesIfDifferent(data,Cave,FinalPayload)
                ? 1
                : 0;

        changed +=
            WriteUInt32IfDifferent(
                data,
                handlerRelocation+8,
                FinalHandlerAddend)
                ? 1
                : 0;

        changed +=
            WriteBytesIfDifferent(data,Duration,DurationFinal)
                ? 1
                : 0;

        File.WriteAllBytes(path,data);
        return changed;
    }

    internal static void EnsureTemplateRow(string path)
    {
        EnsureMoveRow(path,LuckyChant,ConfigureTemplate);
    }

        private static void ConfigureTemplate(string[] fields)
    {
        // BattlePatch-only row.
        //
        // Empty cells mean "keep the current/vanilla move-data value".
        // v17 must not write any move-data field for Lucky Chant.
        for (int i = 1; i <= 40; i++)
            fields[i] = string.Empty;

        fields[41] = "Gen7LuckyChantCritBoost";
        fields[42] =
            "Conjuro / Lucky Chant: Battle.cro v17 only; preserve vanilla move data; protected side +1 critical stage; duration 8 turns";
    }

    private static bool HasToken(string value)
        => HasAnyToken(
            value,
            "Gen7LuckyChantCritBoost",
            "LuckyChantCritBoost",
            "ConjuroCritBoost");

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

    private static bool TryGetRelocationEntry(
        byte[] data,
        int index,
        uint expectedTag,
        byte expectedType,
        byte expectedRefSegment,
        uint stockAddend,
        uint finalAddend,
        out int entryOffset)
    {
        entryOffset = -1;

        if (data.Length < 0x130)
            return false;

        uint tableOffset = ReadUInt32(data,0x128);
        uint count = ReadUInt32(data,0x12C);

        if (tableOffset == uint.MaxValue ||
            count == uint.MaxValue ||
            index < 0 ||
            (uint)index >= count)
        {
            return false;
        }

        long entry = tableOffset+((long)index*12);

        if (entry < 0 || entry+12 > data.Length)
            return false;

        entryOffset = (int)entry;

        if (ReadUInt32(data,entryOffset) != expectedTag ||
            data[entryOffset+4] != expectedType ||
            data[entryOffset+5] != expectedRefSegment)
        {
            return false;
        }

        uint addend = ReadUInt32(data,entryOffset+8);
        return addend == stockAddend || addend == finalAddend;
    }

    private static string FindBattleCro()
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(Main.RomFSPath))
            roots.Add(Main.RomFSPath);

        if (!string.IsNullOrWhiteSpace(Main.ExeFSPath))
            roots.Add(Main.ExeFSPath);

        roots.Add(Main.Workspace);
        roots.Add(Main.Workspace);

        foreach (string root in roots
                     .Where(z => !string.IsNullOrWhiteSpace(z))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;

                string direct = Path.Combine(root,"Battle.cro");
                if (File.Exists(direct))
                    return direct;

                string extracted =
                    Path.Combine(root,"ExtractedRomFS","Battle.cro");

                if (File.Exists(extracted))
                    return extracted;

                string nested = Directory
                    .EnumerateFiles(
                        root,
                        "Battle.cro",
                        SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
            catch
            {
                // Try the next root.
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
            File.Copy(path,backup);
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
        if (offset < 0 || offset+length > data.Length)
            return false;

        for (int i=0; i<length; i++)
        {
            if (data[offset+i] != 0)
                return false;
        }

        return true;
    }

    private static byte[] Hex(string value)
    {
        string[] parts =
            value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

        byte[] result = new byte[parts.Length];

        for (int i=0; i<parts.Length; i++)
            result[i] = Convert.ToByte(parts[i],16);

        return result;
    }

    private static void EnsureMoveRow(
        string path,
        int move,
        Action<string[]> configure)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string[] lines = File.ReadAllLines(path);
        bool found = false;

        for (int i=0; i<lines.Length; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            string[] fields = SplitCsvLine(lines[i]);

            if (fields.Length == 0 ||
                !int.TryParse(Get(fields,0),out int rowMove) ||
                rowMove != move)
            {
                continue;
            }

            if (fields.Length < 43)
                Array.Resize(ref fields,43);

            configure(fields);
            lines[i] = JoinCsvLine(fields);
            found = true;
            break;
        }

        if (!found)
        {
            var fields = new string[43];
            fields[0] = move.ToString();

            configure(fields);

            Array.Resize(ref lines,lines.Length+1);
            lines[^1] = JoinCsvLine(fields);
        }

        File.WriteAllLines(path,lines);
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