using pk3DS.Core;
using pk3DS.Core.Randomizers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace pk3DS.WinForms;

internal static class Gen6TradePatcher
{
    private sealed record BytePatch(int Offset, byte[] Clean, byte[] Patched, string Label);

    private sealed record TradePatchProfile(
        string Name,
        int[] RequestSpeciesOffsets,
        int[] OfferSpeciesOffsets,
        ushort[] ExpectedRequests,
        BytePatch[] ExtraBytePatches);

    private static readonly BytePatch[] ORASPcSelectPatches =
    [
        new(
            0x3196C8,
            [0x01, 0x40, 0xA0, 0xE1],
            [0x03, 0x40, 0xA0, 0xE3],
            "ORAS PC/box select mode patch A"),
        new(
            0x319F38,
            [0x00, 0x50, 0xD0, 0xE5],
            [0x03, 0x50, 0xA0, 0xE3],
            "ORAS PC/box select mode patch B"),
    ];

    // Offsets identified by comparing a clean ORAS code.bin against a ROM where in-game trades already accept any Pokemon.
    private static readonly TradePatchProfile ORAS = new(
        "ORAS",
        [0x486B04, 0x486B28, 0x486B4C],
        [0x486AE4, 0x486B08, 0x486B2C, 0x486B50],
        [287, 327, 182], // Slakoth, Spinda, Bellossom in the compared ORAS build.
        ORASPcSelectPatches);

    // Offsets identified by comparing a clean XY code.bin against a ROM where in-game trades already accept any Pokemon.
    // XY trade entries are 0x24 bytes apart. Offer species is at entry + 0x00, request species is at entry + 0x20.
    private static readonly TradePatchProfile XY = new(
        "XY",
        [0x4450E0, 0x445104, 0x445128],
        [0x4450C0, 0x4450E4, 0x445108],
        [659, 370, 39], // Bunnelby, Luvdisc, Jigglypuff in the compared XY build.
        []);

    public static string ApplyGen6TradePatch(string exefsPath, GameConfig config, bool randomizeOffers)
    {
        if (config.Generation != 6)
            throw new InvalidOperationException("This trade patch is only for Gen 6.");

        TradePatchProfile profile = config.ORAS ? ORAS : XY;
        string codePath = GetCodePath(exefsPath);
        byte[] data = File.ReadAllBytes(codePath);
        if(config.ORAS)
        {
            var selectors=Gen6TradeSelectorProfile.Resolve(data);
            profile=profile with { ExtraBytePatches=ORASPcSelectPatches.Select((patch,i)=>patch with {Offset=selectors[i]}).ToArray() };
        }

        int maxOffset = profile.RequestSpeciesOffsets
            .Concat(profile.OfferSpeciesOffsets)
            .Concat(profile.ExtraBytePatches.Select(z => z.Offset + z.Clean.Length))
            .DefaultIfEmpty(0)
            .Max();

        if (data.Length <= maxOffset + 2)
            throw new InvalidOperationException($"code.bin is too small for the known {profile.Name} trade offsets. Make sure ExeFS is unpacked and code.bin is decompressed.");

        foreach (BytePatch patch in profile.ExtraBytePatches)
            ValidateOrAlreadyPatched(data, patch.Offset, patch.Clean, patch.Patched, patch.Label);

        for (int i = 0; i < profile.RequestSpeciesOffsets.Length; i++)
        {
            ushort current = ReadU16(data, profile.RequestSpeciesOffsets[i]);
            if (current != profile.ExpectedRequests[i] && current != 0)
                throw new InvalidOperationException($"Unexpected request species at 0x{profile.RequestSpeciesOffsets[i]:X}. Expected {profile.ExpectedRequests[i]} or 0, found {current}. This does not look like the {profile.Name} build that was compared.");
        }

        int bytePatchCount = 0;
        foreach (BytePatch patch in profile.ExtraBytePatches)
            bytePatchCount += WritePatch(data, patch.Offset, patch.Patched);

        int requestCount = 0;
        foreach (int offset in profile.RequestSpeciesOffsets)
        {
            if (ReadU16(data, offset) != 0)
            {
                WriteU16(data, offset, 0);
                requestCount++;
            }
        }

        int offerCount = 0;
        if (randomizeOffers)
            offerCount = RandomizeOffers(data, config, profile.OfferSpeciesOffsets);

        File.WriteAllBytes(codePath, data);

        string extraPatchLine = profile.ExtraBytePatches.Length == 0
            ? "Extra selector patches applied: not needed for this XY profile"
            : $"PC/box selector patches applied: {bytePatchCount}/{profile.ExtraBytePatches.Length}";

        return $"Patched code binary: {Path.GetFileName(codePath)}{Environment.NewLine}" +
               $"Game profile: {profile.Name}{Environment.NewLine}" +
               $"{extraPatchLine}{Environment.NewLine}" +
               $"Trade requests set to any Pokemon: {requestCount}/{profile.RequestSpeciesOffsets.Length}{Environment.NewLine}" +
               $"Trade offers randomized: {offerCount}/{profile.OfferSpeciesOffsets.Length}";
    }

    // Kept for compatibility with older button code/scripts.
    public static string ApplyORASTradePatch(string exefsPath, GameConfig config, bool randomizeOffers)
        => ApplyGen6TradePatch(exefsPath, config, randomizeOffers);

    private static int RandomizeOffers(byte[] data, GameConfig config, IReadOnlyList<int> offerOffsets)
    {
        var rand = new SpeciesRandomizer(config)
        {
            rBST = false,
            rEXP = false,
            rType = false,
            L = false,
            E = false,
            Shedinja = false,
        };
        rand.Initialize();

        int count = 0;
        foreach (int offset in offerOffsets)
        {
            ushort oldSpecies = ReadU16(data, offset);
            if (oldSpecies == 0 || oldSpecies > config.MaxSpeciesID)
                continue;

            int newSpecies = rand.GetRandomSpecies(oldSpecies);
            WriteU16(data, offset, (ushort)newSpecies);
            count++;
        }

        return count;
    }

    private static string GetCodePath(string exefsPath)
    {
        if (string.IsNullOrWhiteSpace(exefsPath) || !Directory.Exists(exefsPath))
            throw new DirectoryNotFoundException("ExeFS folder is not loaded.");

        string[] candidates =
        [
            Path.Combine(exefsPath, ".code.bin"),
            Path.Combine(exefsPath, "code.bin"),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        string found = Directory.GetFiles(exefsPath, "*code*.bin", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (found != null)
            return found;

        throw new FileNotFoundException("Could not find code.bin or .code.bin in the loaded ExeFS folder.");
    }

    private static void ValidateOrAlreadyPatched(byte[] data, int offset, byte[] clean, byte[] patched, string label)
    {
        byte[] current = data.Skip(offset).Take(clean.Length).ToArray();
        if (current.SequenceEqual(clean) || current.SequenceEqual(patched))
            return;

        throw new InvalidOperationException($"Unexpected bytes for {label} at 0x{offset:X}. Make sure code.bin is decompressed and matches the expected build.");
    }

    private static int WritePatch(byte[] data, int offset, byte[] patch)
    {
        bool already = data.Skip(offset).Take(patch.Length).SequenceEqual(patch);
        Array.Copy(patch, 0, data, offset, patch.Length);
        return already ? 0 : 1;
    }

    private static ushort ReadU16(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);

    private static void WriteU16(byte[] data, int offset, ushort value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        data[offset] = bytes[0];
        data[offset + 1] = bytes[1];
    }
}
