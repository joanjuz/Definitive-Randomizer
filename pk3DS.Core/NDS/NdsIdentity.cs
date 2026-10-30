#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace pk3DS.Core.NDS;

public sealed record NdsGameProfile(string GameCode, byte Version, string Game, int Generation);

public sealed record NdsIdentityResult(NdsGameProfile Profile, string Sha1, bool KnownDump);

/// <summary>
/// Strict identification of supported DS games by game code and revision. Known dumps are the originals the owner
/// provided (SHA-1 recorded on 2026-10-30); other files with a supported code are recognised but marked unverified.
/// </summary>
public static class NdsIdentity
{
    public static IReadOnlyList<NdsGameProfile> Profiles { get; } =
    [
        new("ADAE", 5, "Pokémon Diamante (USA, rev. 5)", 4),
        new("CPUE", 1, "Pokémon Platino (USA, rev. 1)", 4),
        new("IPKE", 0, "Pokémon Oro HeartGold (USA)", 4),
        new("IRBO", 0, "Pokémon Negro (USA/EUR)", 5),
        new("IRAS", 0, "Pokémon Blanco (España)", 5),
        new("IRDO", 0, "Pokémon Blanco 2 (USA/EUR)", 5),
    ];

    private static readonly Dictionary<string, string> KnownDumps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ADAE/5"] = "a46233d8b79a69ea87aa295a0efad5237d02841e",
        ["CPUE/1"] = "0862ec35b24de5c7e2dcb88c9eea0873110d755c",
        ["IPKE/0"] = "4fcded0e2713dc03929845de631d0932ea2b5a37",
        ["IRBO/0"] = "26ad0b9967aa279c4a266ee69f52b9b2332399a5",
        ["IRAS/0"] = "4cc963ca925409ff0f12f3c417d1cbe1094bf934",
        ["IRDO/0"] = "b5d7490be7b415b8f1e672a53e978a9cc667e56a",
    };

    public static NdsIdentityResult Identify(NdsRom rom)
    {
        var profile = Profiles.FirstOrDefault(p => p.GameCode == rom.Header.GameCode && p.Version == rom.Header.Version)
            ?? throw new InvalidDataException($"Juego de NDS no admitido: {rom.Header.GameCode} revisión {rom.Header.Version} ({rom.Header.Title}).");
        string sha1;
        using (var stream = new FileStream(rom.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            sha1 = Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
        return new NdsIdentityResult(profile, sha1,
            KnownDumps.TryGetValue(profile.GameCode + "/" + profile.Version, out string? known) && known == sha1);
    }
}
