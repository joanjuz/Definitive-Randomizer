// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// What the USUM nature mints depend on inside Bag.cro, so other Bag.cro patches can coexist with them.
/// <para>
/// The mints were accredited against one Bag.cro (identical in Ultra Sun and Ultra Moon). Requiring that whole file to be
/// byte-identical made every other Bag.cro patch incompatible with them. Instead, the mints now require their anchors:
/// the windows around their four hook sites, the start of every Bag.cro function their routine branches to, and the
/// imported symbol behind every stub it calls. Anything outside those anchors may be changed by another patch, as long
/// as the CRO keeps consistent hashes. Another patch must not write inside <see cref="MintRanges"/>.
/// </para>
/// </summary>
public static class UsumBagAnchors
{
    public const string OriginalBagSha256 = "BD97393E3440A04CA6F61C98771C11E595C4EE649F9C4985E793A81E7721155C";

    /// <summary>Mint hook sites; masked to zero inside the windows so the same hashes hold before and after installing.</summary>
    public static IReadOnlyList<int> MintSites { get; } = [0xC318, 0xDDC8, 0xC358, 0x80DC];

    /// <summary>Windows of the original Bag.cro (offset, length, SHA-256 with the mint sites zeroed).</summary>
    public static IReadOnlyList<(int Offset, int Length, string Hash)> MintWindows { get; } =
    [
        (0x0C2F8, 0x44, "C3AB79A3963F0F719B2EC04CCFA5883DC38479349CF90F5AD506B2F70DB2CC77"),
        (0x0DDA8, 0x44, "988B79AE901BA9684BAF1C6B7F89CA14F9B002911BF48FB55A61271165F80D24"),
        (0x0C338, 0x44, "13326FA1883ADABB491D884E9368056DE4ACE8CA09B1047378462D34BC261A90"),
        (0x080BC, 0x44, "88B1B6F5EAC7983026794C93FC0197A4B19CAFAEE1B430B8A2927E8057A7D6E0"),
        (0x080A4, 0x10, "8E25926512BCD473966C27C22A4EC997860383D01CBC574F0EBE2F784375CF81"),
        (0x08450, 0x10, "C3273DAB8AB047441A3181F035342CD26469AA48330997C88CEBC3CA172A3934"),
        (0x08460, 0x10, "07FCD9EE4E707F3FBC800E4FD8C810B1C1430773FBB223C39CBDD981EEEAF84F"),
        (0x0C85C, 0x10, "FA80C8C709270AD8A27B27AABBE27E192F1E778F269636A15F87579AC54FFE8B"),
        (0x0C8E4, 0x10, "82EE684AF986C205343E619C84B9B367EE18F1DEB5D3162C98D593F949FD4BD1"),
    ];

    /// <summary>Import stubs the mint routine calls, with the symbol the loader patches into each.</summary>
    public static IReadOnlyList<(int Stub, string Symbol)> MintStubs { get; } =
    [
        (0x210, "_ZN5Sound6PlaySEEjjij"),
        (0x470, "_ZN3app4util7G2DUtil15GetStringExpandEPN4gfl23str6StrBufEj"),
        (0x5F8, "_ZN3app4util7G2DUtil23SetRegisterPokeNickNameEjPKN3pml8pokepara9CoreParamE"),
        (0x700, "_ZNK3pml8pokepara9CoreParam5IsEggENS0_12EggCheckTypeE"),
        (0x798, "_ZN8PokeTool21ITEM_RCV_RecoverCheckEPKN3pml8pokepara12PokemonParamEttPN7itemman12ITEM_MANAGERE"),
    ];

    /// <summary>Byte ranges [start, end) another Bag.cro patch must leave untouched for the mints to keep working.</summary>
    public static IReadOnlyList<(int Start, int End)> MintRanges { get; } =
        MintWindows.Select(w => (w.Offset, w.Offset + w.Length)).Concat(MintStubs.Select(s => (s.Stub, s.Stub + 8))).ToArray();

    private static readonly byte[] StubCode = [0x04, 0xF0, 0x1F, 0xE5];

    /// <summary>True when every mint anchor is intact (sites themselves are not judged here).</summary>
    public static bool MintAnchorsIntact(byte[] bag, out string reason)
    {
        reason = "";
        foreach (var (offset, length, hash) in MintWindows)
        {
            if (offset + length > bag.Length) { reason = $"Bag.cro truncado en 0x{offset:X}."; return false; }
            byte[] window = bag.AsSpan(offset, length).ToArray();
            foreach (int site in MintSites)
                if (site >= offset && site + 4 <= offset + length)
                    window.AsSpan(site - offset, 4).Clear();
            if (Convert.ToHexString(SHA256.HashData(window)) != hash)
            { reason = $"Otra modificación cambió el código del que dependen las mentas (0x{offset:X5})."; return false; }
        }
        Dictionary<int, string> imports;
        try { imports = NamedImportTargets(bag); }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        { reason = "La tabla de importaciones de Bag.cro no se puede leer."; return false; }
        foreach (var (stub, symbol) in MintStubs)
        {
            if (!bag.AsSpan(stub, 4).SequenceEqual(StubCode) || !imports.TryGetValue(stub + 4, out var name) || name != symbol)
            { reason = $"El stub 0x{stub:X} ya no importa {symbol}."; return false; }
        }
        return true;
    }

    /// <summary>File offset patched by each named import → symbol name.</summary>
    internal static Dictionary<int, string> NamedImportTargets(byte[] cro)
    {
        uint U(int o) => BitConverter.ToUInt32(cro, o);
        int segTable = (int)U(0xC8), segCount = (int)U(0xCC);
        var segments = Enumerable.Range(0, segCount).Select(i => (int)U(segTable + i * 12)).ToArray();
        int table = (int)U(0x100), count = (int)U(0x104);
        var result = new Dictionary<int, string>();
        for (int i = 0; i < count; i++)
        {
            int nameOffset = (int)U(table + i * 8), patch = (int)U(table + i * 8 + 4);
            int end = Array.IndexOf(cro, (byte)0, nameOffset);
            string name = Encoding.ASCII.GetString(cro, nameOffset, end - nameOffset);
            for (int guard = 0; guard < 4096; guard++, patch += 12)
            {
                uint target = U(patch);
                result[segments[target & 0xF] + (int)(target >> 4)] = name;
                if (cro[patch + 5] != 0) break;
            }
        }
        return result;
    }
}
