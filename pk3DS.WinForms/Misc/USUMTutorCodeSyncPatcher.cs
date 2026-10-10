using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.WinForms;

/// <summary>
/// Keeps the native USUM Move Tutor lookup table in code.bin synchronized with
/// the 67 BP Tutor move IDs stored in Shop.cro.
///
/// The game first resolves a displayed Tutor move ID through the native
/// code.bin table. Only after that lookup succeeds does it read the slot-based
/// Personal compatibility bits. A randomized Shop.cro paired with a stale
/// code.bin table therefore makes every Pokemon appear incompatible.
///
/// The table address is located from the native ARM compatibility routine
/// instead of from a fixed Ultra Sun / Ultra Moon file offset.
/// </summary>
internal static class USUMTutorCodeSyncPatcher
{
    internal sealed class Plan
    {
        internal string CodePath { get; init; }
        internal byte[] OriginalCode { get; init; }
        internal byte[] PatchedCode { get; init; }
        internal int TableOffset { get; init; }
        internal int ChangedSlots { get; init; }
    }

    private const int CodeBaseAddress = 0x00100000;
    private const int TutorCount = 67;

    // Native USUM Tutor compatibility routine.
    //
    // Relative to MOV r0,#0x29:
    //   -0x58  LDR r2,[pc,#imm]  -> literal pointer to Tutor move table
    //   -0x54  PUSH {r4,r5,r6,lr}
    //   -0x28  CMP r0,#0x43      -> 67 move slots
    //    0x00  MOV r0,#0x29      -> first Personal selector block
    //   +0x0C  CMP r4,#0x2B      -> last selector block used by 67 slots
    private const uint BaseSelectorInstruction = 0xE3A00029;
    private const uint LoopCountInstruction = 0xE3500043;
    private const uint MaxSelectorInstruction = 0xE354002B;
    private const uint PushInstruction = 0xE92D4070;

    private const int EntryFromBase = -0x58;
    private const int LoopCmpFromBase = -0x28;
    private const int MaxCmpFromBase = 0x0C;

    internal static Plan Prepare(
        string exefsPath,
        IReadOnlyList<ushort> tutorMoves)
    {
        if (tutorMoves is null)
            throw new ArgumentNullException(nameof(tutorMoves));

        if (tutorMoves.Count != TutorCount)
        {
            throw new InvalidDataException(
                $"USUM code.bin expects exactly {TutorCount} BP Tutor move IDs; found {tutorMoves.Count}.");
        }

        string codePath = FindCodeBinary(exefsPath);
        if (string.IsNullOrWhiteSpace(codePath) ||
            !File.Exists(codePath))
        {
            throw new FileNotFoundException(
                "code.bin was not found in the loaded USUM ExeFS. " +
                "Move Tutor IDs cannot be changed safely without synchronizing code.bin.");
        }

        byte[] original = File.ReadAllBytes(codePath);

        if (!IsDecompressedCode(original))
        {
            throw new InvalidDataException(
                "code.bin appears to be compressed. Decompress it before changing USUM Move Tutor IDs.");
        }

        int tableOffset = FindTutorTableOffset(original);

        byte[] patched = (byte[])original.Clone();
        int changedSlots = 0;

        for (int i = 0; i < TutorCount; i++)
        {
            int offset = tableOffset + (i * 2);
            ushort oldMove = BitConverter.ToUInt16(
                patched,
                offset);

            ushort newMove = tutorMoves[i];
            if (oldMove == newMove)
                continue;

            byte[] bytes = BitConverter.GetBytes(newMove);
            bytes.CopyTo(
                patched,
                offset);

            changedSlots++;
        }

        return new Plan
        {
            CodePath = codePath,
            OriginalCode = original,
            PatchedCode = patched,
            TableOffset = tableOffset,
            ChangedSlots = changedSlots,
        };
    }

    internal static int Apply(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.ChangedSlots == 0)
            return 0;

        byte[] current = File.ReadAllBytes(
            plan.CodePath);

        if (!current.SequenceEqual(plan.OriginalCode))
        {
            throw new InvalidDataException(
                "code.bin changed after the Tutor synchronization was validated. " +
                "No Tutor code bytes were written.");
        }

        PatchBackupManager.BackupOnce(
            plan.CodePath,
            "usum-tutor-code-sync");

        try
        {
            File.WriteAllBytes(
                plan.CodePath,
                plan.PatchedCode);
        }
        catch
        {
            try
            {
                File.WriteAllBytes(
                    plan.CodePath,
                    plan.OriginalCode);
            }
            catch
            {
                // Preserve the original write exception. A persistent project
                // backup also exists through PatchBackupManager.
            }

            throw;
        }

        byte[] verify = File.ReadAllBytes(
            plan.CodePath);

        if (!verify.SequenceEqual(plan.PatchedCode))
        {
            try
            {
                File.WriteAllBytes(
                    plan.CodePath,
                    plan.OriginalCode);
            }
            catch
            {
                // Preserve the verification exception below.
            }

            throw new IOException(
                "code.bin Tutor synchronization failed post-write verification.");
        }

        return plan.ChangedSlots;
    }

    private static int FindTutorTableOffset(
        byte[] code)
    {
        var candidates =
            new HashSet<int>();

        for (int baseOffset = -EntryFromBase;
             baseOffset + MaxCmpFromBase + 4 <= code.Length;
             baseOffset += 4)
        {
            if (ReadUInt32(
                    code,
                    baseOffset) !=
                BaseSelectorInstruction)
            {
                continue;
            }

            int entryOffset =
                baseOffset +
                EntryFromBase;

            int loopCmpOffset =
                baseOffset +
                LoopCmpFromBase;

            int maxCmpOffset =
                baseOffset +
                MaxCmpFromBase;

            if (entryOffset < 0 ||
                loopCmpOffset < 0)
            {
                continue;
            }

            if (ReadUInt32(
                    code,
                    loopCmpOffset) !=
                LoopCountInstruction ||
                ReadUInt32(
                    code,
                    maxCmpOffset) !=
                MaxSelectorInstruction ||
                ReadUInt32(
                    code,
                    entryOffset + 4) !=
                PushInstruction)
            {
                continue;
            }

            uint ldr =
                ReadUInt32(
                    code,
                    entryOffset);

            // ARM LDR r2,[pc,#imm12], positive PC-relative literal.
            if ((ldr & 0xFFFFF000u) !=
                0xE59F2000u)
            {
                continue;
            }

            int literalOffset =
                checked(
                    entryOffset +
                    8 +
                    (int)(ldr & 0xFFFu));

            if (literalOffset < 0 ||
                literalOffset + 4 >
                code.Length)
            {
                continue;
            }

            uint runtimeAddress =
                ReadUInt32(
                    code,
                    literalOffset);

            if (runtimeAddress <
                CodeBaseAddress)
            {
                continue;
            }

            long tableOffset =
                (long)runtimeAddress -
                CodeBaseAddress;

            int tableLength =
                TutorCount * 2;

            if (tableOffset < 0 ||
                tableOffset >
                    code.Length -
                    tableLength)
            {
                continue;
            }

            candidates.Add(
                (int)tableOffset);
        }

        if (candidates.Count != 1)
        {
            throw new InvalidDataException(
                "The native USUM Tutor compatibility table could not be located uniquely. " +
                $"Expected 1 structural match, found {candidates.Count}. No bytes were changed.");
        }

        return candidates.Single();
    }

    private static uint ReadUInt32(
        byte[] data,
        int offset)
    {
        if (offset < 0 ||
            offset + 4 >
            data.Length)
        {
            throw new InvalidDataException(
                "code.bin contains an invalid Tutor routine range.");
        }

        return BitConverter.ToUInt32(
            data,
            offset);
    }

    private static bool IsDecompressedCode(
        byte[] data) =>
        data is { Length: > 0 } &&
        (data.Length & 0x1FF) == 0;

    private static string FindCodeBinary(
        string exefsPath)
    {
        if (string.IsNullOrWhiteSpace(exefsPath) ||
            !Directory.Exists(exefsPath))
        {
            return string.Empty;
        }

        string dotCode =
            Path.Combine(
                exefsPath,
                ".code.bin");

        if (File.Exists(dotCode))
            return dotCode;

        string code =
            Path.Combine(
                exefsPath,
                "code.bin");

        if (File.Exists(code))
            return code;

        try
        {
            return Directory
                       .EnumerateFiles(
                           exefsPath,
                           "*",
                           SearchOption.TopDirectoryOnly)
                       .FirstOrDefault(z =>
                           Path.GetFileName(z)
                               .Contains(
                                   "code",
                                   StringComparison.OrdinalIgnoreCase))
                   ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}