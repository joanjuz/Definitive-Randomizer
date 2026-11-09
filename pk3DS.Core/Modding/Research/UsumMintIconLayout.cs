using System;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>Item-to-image table in the two explicitly fingerprinted USUM executables.</summary>
public static class UsumMintIconLayout
{
    public const int TableOffset = 0x4BC8BC;
    public const int ItemCount = 960;
    public const int OriginalImageCount = 769;
    public const int InstalledImageCount = OriginalImageCount + 21;
    public const string ArchivePath = "a/0/6/1";
    public static int OriginalImage(int itemId) => itemId == 622 ? 492 : 768;
    public static bool HasMapping(byte[] code, bool installed)
    {
        if (code.Length < TableOffset + ItemCount * 4) return false;
        return UsumNatureMintCatalog.Mints.Select((mint, i) =>
            BitConverter.ToUInt32(code, TableOffset + mint.ItemId * 4) ==
            (uint)(installed ? OriginalImageCount + i : OriginalImage(mint.ItemId))).All(matches => matches);
    }
    /// <summary>Normalize only our exact 21-word mapping; callers still verify every other protected executable region.</summary>
    public static byte[] NormalizeCode(byte[] code)
    {
        if (!HasMapping(code, true)) return code;
        byte[] normalized = (byte[])code.Clone();
        foreach (var mint in UsumNatureMintCatalog.Mints)
            BitConverter.GetBytes(OriginalImage(mint.ItemId)).CopyTo(normalized, TableOffset + mint.ItemId * 4);
        return normalized;
    }
    public static byte[] InstallMapping(byte[] code)
    {
        if (!HasMapping(code, false)) throw new InvalidDataException("La tabla de iconos de las mentas no conserva sus referencias originales.");
        byte[] result = (byte[])code.Clone();
        for (int i = 0; i < UsumNatureMintCatalog.Mints.Count; i++)
            BitConverter.GetBytes(OriginalImageCount + i).CopyTo(result, TableOffset + UsumNatureMintCatalog.Mints[i].ItemId * 4);
        return result;
    }
}
