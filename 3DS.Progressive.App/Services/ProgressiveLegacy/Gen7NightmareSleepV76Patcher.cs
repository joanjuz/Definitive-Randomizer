// Ported from pk3DS.WinForms/Misc/Gen7NightmareSleepV76Patcher.cs at f459e40.
// Only namespace and temporary IO roots differ; battle instructions are unchanged.
#nullable disable
using System;
using System.IO;

namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

// USUM Nightmare / Pesadilla permanent patch v76.
// Validated in-game:
//   awake target  -> stock Sleep only
//   asleep target -> vanilla Nightmare
//
// The callback recreates B24F4's exact frame and branches directly into
// the stock Sleep sub-branch at B26F8. The only auxiliary cave used is
// FCAE8..FCAFF, between the permanent Mist and Meditate payload regions.
internal static class Gen7NightmareSleepV76Patcher
{
    internal const string BattlePatchToken = "Gen7NightmareSleepV76";

    private const int BattleCroSize = 0x13C000;
    private const int CallbackOffset = 0x000C9628;
    private const int CaveOffset = 0x000FCAE8;
    private const int CaveLength = 0x18;

    private static readonly uint[] StockCallback =
    {
        0xE92D4070u, 0xE1A05001u, 0xE1A04002u, 0xE3A00003u, 0xEBFEF946u,
        0xE1500004u, 0x1A00000Cu, 0xE3A00004u, 0xEBFEF942u, 0xE20010FFu,
        0xE1A00005u, 0xEBFF230Fu, 0xE3A01001u, 0xEBFF1D1Au, 0xE3500000u,
        0x1A000003u, 0xE8BD4070u, 0xE3A01001u, 0xE3A00045u, 0xEAFEE5B2u,
        0xE8BD8070u,
    };

    private static readonly uint[] PatchedCallback =
    {
        0xE92D4FF0u, 0xE24DD04Cu, 0xE1A05001u, 0xE1A04002u, 0xE3A00003u,
        0xEBFEF945u, 0xE1500004u, 0x1AFFA451u, 0xE1A00180u, 0xE58D0010u,
        0xE3A00004u, 0xEBFEF93Fu, 0xE20010FFu, 0xE1A00005u, 0xEBFF230Cu,
        0xE1A04000u, 0xE3A01001u, 0xEBFF1D16u, 0xE3500000u, 0x1AFFA445u,
        0xEA00CD1Au,
    };

    private static readonly uint[] PatchedCave =
    {
        0xE28D6010u, // ADD r6,sp,#0x10
        0xE3A01001u, // MOV r1,#1
        0xE3A00045u, // MOV r0,#0x45
        0xEBFE1892u, // BL 82D44
        0xEAFED6FEu, // B B26F8
        0x00000000u,
    };

    internal static int Apply()
    {
        string path = FindBattleCro();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return -1;

        byte[] data = File.ReadAllBytes(path);
        if (data.Length != BattleCroSize)
            return -1;

        if (!ValidateStockSleepSupport(data))
            return -1;

        bool callbackStock = WordsAt(data, CallbackOffset, StockCallback);
        bool callbackPatched = WordsAt(data, CallbackOffset, PatchedCallback);
        bool caveStock = IsZeroRange(data, CaveOffset, CaveLength);
        bool cavePatched = WordsAt(data, CaveOffset, PatchedCave);

        if (callbackPatched && cavePatched)
            return 0;

        // Reject partial or foreign versions instead of trying to repair them.
        if (!callbackStock || !caveStock)
            return -1;

        BackupOnce(path, ".bak_nightmare_sleep_v76");

        WriteWords(data, CallbackOffset, PatchedCallback);
        WriteWords(data, CaveOffset, PatchedCave);

        if (!WordsAt(data, CallbackOffset, PatchedCallback) ||
            !WordsAt(data, CaveOffset, PatchedCave))
        {
            return -1;
        }

        File.WriteAllBytes(path, data);

        byte[] verify = File.ReadAllBytes(path);
        if (!WordsAt(verify, CallbackOffset, PatchedCallback) ||
            !WordsAt(verify, CaveOffset, PatchedCave))
        {
            return -1;
        }

        return 2;
    }

    private static bool ValidateStockSleepSupport(byte[] data)
    {
        // Exact B24F4 frame used by the graft.
        if (ReadUInt32(data, 0x000B24F4) != 0xE92D4FF0u ||
            ReadUInt32(data, 0x000B24F8) != 0xE24DD04Cu)
        {
            return false;
        }

        // Exact stock Sleep sub-branch live-ins and epilogue.
        if (ReadUInt32(data, 0x000B26F8) != 0xE1A07005u ||
            ReadUInt32(data, 0x000B2714) != 0xE1A00004u ||
            ReadUInt32(data, 0x000B2728) != 0xE1A00006u ||
            ReadUInt32(data, 0x000B2790) != 0xE28DD04Cu ||
            ReadUInt32(data, 0x000B2794) != 0xE8BD8FF0u)
        {
            return false;
        }

        // 841C8: packed 5-bit source extractor.
        if (ReadUInt32(data, 0x000841C8) != 0xE5900000u ||
            ReadUInt32(data, 0x000841CC) != 0xE1A00C00u ||
            ReadUInt32(data, 0x000841D0) != 0xE1A00DA0u ||
            ReadUInt32(data, 0x000841D4) != 0xE12FFF1Eu)
        {
            return false;
        }

        // 92298 falls through into 922A0 and resolves [context+8], battler ID.
        if (ReadUInt32(data, 0x00092298) != 0xE5900008u ||
            ReadUInt32(data, 0x0009229C) != 0xE1A00000u ||
            ReadUInt32(data, 0x000922A0) != 0xE92D4070u)
        {
            return false;
        }

        // Critical stock calls used by B26F8.
        return
            BranchTargets(data, 0x000B2704, 0x000B08BC) &&
            BranchTargets(data, 0x000B2718, 0x0008205C) &&
            BranchTargets(data, 0x000B272C, 0x000841C8) &&
            BranchTargets(data, 0x000B274C, 0x000876F8) &&
            BranchTargets(data, 0x000B2774, 0x00089CC4) &&
            BranchTargets(data, 0x000B278C, 0x0007CCF4);
    }

    private static bool BranchTargets(byte[] data, int offset, int target)
    {
        uint word = ReadUInt32(data, offset);
        if (word == uint.MaxValue || (word & 0x0E000000u) != 0x0A000000u)
            return false;

        int imm = (int)(word & 0x00FFFFFFu);
        if ((imm & 0x00800000) != 0)
            imm -= 0x01000000;

        long decoded = offset + 8L + (imm * 4L);
        return decoded == target;
    }

    private static string FindBattleCro()
    {
        if (string.IsNullOrWhiteSpace(Main.RomFSPath))
            return string.Empty;

        string[] candidates =
        {
            Path.Combine(Main.RomFSPath, "Battle.cro"),
            Path.Combine(Main.RomFSPath, "ExtractedRomFS", "Battle.cro"),
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return string.Empty;
    }

    private static bool WordsAt(byte[] data, int offset, uint[] words)
    {
        if (offset < 0 || words is null || offset + (words.Length * 4) > data.Length)
            return false;

        for (int i = 0; i < words.Length; i++)
        {
            if (ReadUInt32(data, offset + (i * 4)) != words[i])
                return false;
        }

        return true;
    }

    private static void WriteWords(byte[] data, int offset, uint[] words)
    {
        for (int i = 0; i < words.Length; i++)
        {
            byte[] bytes = BitConverter.GetBytes(words[i]);
            Array.Copy(bytes, 0, data, offset + (i * 4), 4);
        }
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
            return uint.MaxValue;

        return BitConverter.ToUInt32(data, offset);
    }

    private static bool IsZeroRange(byte[] data, int offset, int length)
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
}