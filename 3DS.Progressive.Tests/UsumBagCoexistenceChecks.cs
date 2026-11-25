using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Modding.Research;

/// <summary>
/// Nature mints and other Bag.cro patches (USUM). The mint anchors are fingerprints of the real Bag.cro, so these checks
/// need the original file and are not part of CI; everything happens in memory.
/// </summary>
internal static class UsumBagCoexistenceChecks
{
    private static void Rehash(byte[] c)
    {
        int U(int o) => BitConverter.ToInt32(c, o);
        int code = U(0xB0), codeSize = U(0xB4), data = U(0xB8), dataSize = U(0xBC);
        SHA256.HashData(c.AsSpan(0x80, code - 0x80)).CopyTo(c, 0x00);
        SHA256.HashData(c.AsSpan(code, codeSize)).CopyTo(c, 0x20);
        SHA256.HashData(c.AsSpan(code + codeSize, data - code - codeSize)).CopyTo(c, 0x40);
        SHA256.HashData(c.AsSpan(data, dataSize)).CopyTo(c, 0x60);
    }

    private static uint Branch(int from, int target, uint kind) => kind | ((uint)((target - from - 8) >> 2) & 0xFFFFFF);

    /// <summary>A neutral foreign patch: moves the instruction at <paramref name="site"/> into new code and jumps back.</summary>
    private static byte[] Foreign(byte[] bag, int site)
    {
        if (!CroPatchSession.TryCreate(bag, out var session, out string error) ||
            !session.TryAllocateRelocatableCode(0x10, "foreign test patch", out var grant, out error))
            throw new InvalidDataException(error);
        int at = checked((int)grant.Offset);
        byte[] code = new byte[0x10];
        BitConverter.GetBytes(BitConverter.ToUInt32(bag, site)).CopyTo(code, 0);
        BitConverter.GetBytes(Branch(at + 4, site + 4, 0xEA000000)).CopyTo(code, 4);
        if (!session.TryWriteCode(grant, code, out error) || !session.TryBuildImage(out var image, out _, out error))
            throw new InvalidDataException(error);
        BitConverter.GetBytes(Branch(site, at, 0xEA000000)).CopyTo(image, site);
        Rehash(image);
        return image;
    }

    internal static void RunReal(string romfs, string? otherBag, Action<bool, string> check)
    {
        byte[] bag = File.ReadAllBytes(Path.Combine(romfs, "Bag.cro"));
        check(Convert.ToHexString(SHA256.HashData(bag)) == UsumBagAnchors.OriginalBagSha256, "BagCoexist_OriginalBag");
        check(UsumBagAnchors.MintAnchorsIntact(bag, out _), "BagCoexist_AnchorsMatchTheOriginal");
        foreach (var profile in UsumNatureMintCatalog.Profiles)
        {
            string id = profile.Moon ? "UM" : "US";
            check(UsumNatureMintPatch.Inspect(bag, profile).State == UsumMintState.NotInstalled, "BagCoexist_OriginalNotInstalled_" + id);
            // Another patch first, mints after.
            byte[] foreign = Foreign(bag, 0xE9A0);
            var before = UsumNatureMintPatch.Inspect(foreign, profile);
            check(before.State == UsumMintState.NotInstalled && before.Detail.Contains("otras modificaciones"), "BagCoexist_ForeignPatchStillInstallable_" + id);
            byte[] both = UsumNatureMintPatch.Prepare(foreign, profile);
            check(UsumNatureMintPatch.Inspect(both, profile).State == UsumMintState.Installed && BitConverter.ToUInt32(both, 0xE9A0) == BitConverter.ToUInt32(foreign, 0xE9A0),
                "BagCoexist_MintsInstalledOverForeignPatch_" + id);
            // Mints first, another patch after.
            byte[] mints = UsumNatureMintPatch.Prepare(bag, profile);
            byte[] later = Foreign(mints, 0x6144);
            var after = UsumNatureMintPatch.Inspect(later, profile);
            check(after.State == UsumMintState.Installed && after.Detail.Contains("otras modificaciones compatibles"), "BagCoexist_LaterForeignPatchKeepsMintsInstalled_" + id);
            // A patch that touches what the mints need is still refused.
            byte[] clash = Foreign(bag, 0xC85C);
            check(UsumNatureMintPatch.Inspect(clash, profile).State == UsumMintState.Incompatible, "BagCoexist_PatchOnMintDependencyRefused_" + id);
            byte[] clashAfter = Foreign(mints, 0xC8E4);
            check(UsumNatureMintPatch.Inspect(clashAfter, profile).State == UsumMintState.Modified, "BagCoexist_LaterPatchOnMintDependencyDetected_" + id);
            byte[] stale = (byte[])foreign.Clone(); stale[0x9000] ^= 1;
            check(UsumNatureMintPatch.Inspect(stale, profile).State == UsumMintState.Incompatible, "BagCoexist_StaleCroHashesRefused_" + id);
            byte[] site = Foreign(bag, 0xE9A0); BitConverter.GetBytes(0xE320F000u).CopyTo(site, 0xC318); Rehash(site);
            check(UsumNatureMintPatch.Inspect(site, profile).State == UsumMintState.Incompatible, "BagCoexist_ForeignHookOnMintSiteRefused_" + id);
            if (otherBag is not null)
            {
                byte[] other = File.ReadAllBytes(otherBag);
                check(UsumNatureMintPatch.Inspect(other, profile).State == UsumMintState.NotInstalled, "BagCoexist_GivenBagAcceptsMints_" + id);
                byte[] withMints = UsumNatureMintPatch.Prepare(other, profile);
                check(UsumNatureMintPatch.Inspect(withMints, profile).State == UsumMintState.Installed, "BagCoexist_GivenBagWithMintsRecognised_" + id);
            }
        }
    }
}
