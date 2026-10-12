using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Port of stable USUMTutorCodeSyncPatcher's native ARM lookup. Pure byte
/// transformation, no WinForms globals, no filesystem mutation. The existing
/// code table may already have different moves from Shop.cro.
/// </summary>
internal static class UsumTutorCodePatch
{
    internal sealed record Proposal(byte[] Output,int TableOffset,int ChangedSlots);
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


    internal static Proposal Prepare(byte[] original, IReadOnlyList<ushort> tutorMoves)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(tutorMoves);
        if(tutorMoves.Count!=TutorCount)
            throw new InvalidDataException($"USUM requiere {TutorCount} movimientos de tutor, recibidos {tutorMoves.Count}.");
        if(!IsDecompressedCode(original))
            throw new InvalidDataException("code.bin de tutores está comprimido o no está alineado a 0x200.");
        int offset=FindTutorTableOffset(original);
        var result=(byte[])original.Clone();
        int changed=0;
        for(int i=0;i<TutorCount;i++)
        {
            int at=offset+i*2;
            if(BitConverter.ToUInt16(result,at)==tutorMoves[i])continue;
            BitConverter.GetBytes(tutorMoves[i]).CopyTo(result,at);
            changed++;
        }
        if(!Enumerable.Range(0,TutorCount).All(i=>BitConverter.ToUInt16(result,offset+i*2)==tutorMoves[i]))
            throw new InvalidDataException("La lectura de la tabla de tutores de code.bin no coincide.");
        return new Proposal(result,offset,changed);
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


}
