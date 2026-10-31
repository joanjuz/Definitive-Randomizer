#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>Gen4 game tweaks from Universal Pokémon Randomizer ZX (GPLv3); each one is optional.</summary>
public sealed record NdsTweakOptions
{
    public bool FastestText { get; init; }
    public bool NationalDexAtStart { get; init; }
    public bool RunWithoutRunningShoes { get; init; }
    /// <summary>Platinum only: the Distortion World sequence is shortened.</summary>
    public bool FastDistortionWorld { get; init; }
}

/// <summary>
/// Code and script tweaks for Diamond, Platinum and HeartGold, applied the way UPR-ZX applies them (IPS patches and one
/// instruction cleared), on the original code only: the ARM9 must still be the original (as the profile checks) and every
/// patched script must still be its original file, so a tweak never lands on code it was not made for.
/// </summary>
public static class NdsTweaks
{
    // UPR-ZX gen4_offsets.ini NationalDexScriptOffset, Gen4Constants.ptSpearPillarPortalScriptFile and runningShoesCheckPrefix*.
    private static int NationalDexScript(Gen4Kind kind) => kind switch { Gen4Kind.DiamondPearl => 990, Gen4Kind.Platinum => 1064, _ => 229 };
    private const int SpearPillarPortalScript = 237;

    private static string Prefix(Gen4Kind kind) => kind switch { Gen4Kind.DiamondPearl => "dp", Gen4Kind.Platinum => "plat", _ => "hgss" };

    /// <summary>Why the tweaks cannot be used on this ROM, or null when they can.</summary>
    public static string? Problem(Gen4Data data) => data.Unavailable.ContainsKey(Gen5Feature.Shops)
        ? "el romhack modifica el ARM9, donde se aplican los ajustes" : null;

    public static Gen5RandomizerResult Apply(Gen4Data data, NdsTweakOptions options)
    {
        if (Problem(data) is { } problem)
            throw new ArgumentException("Ajustes no disponibles: " + problem + ".", nameof(options));
        if (options.FastDistortionWorld && data.Profile.Kind != Gen4Kind.Platinum)
            throw new ArgumentException("El Mundo Distorsión rápido solo existe en Platino.", nameof(options));
        var changes = new List<Gen5Change>();
        byte[]? arm9 = null;
        var scripts = new Lazy<Narc>(() => data.CopyNarc(Gen4Profile.Scripts));
        string prefix = Prefix(data.Profile.Kind);
        if (options.FastestText)
        {
            arm9 ??= data.Arm9();
            int bytes = Ips.Apply(arm9, Patch(prefix + "_instant_text.ips"));
            changes.Add(new Gen5Change("Ajustes · Texto instantáneo", "Bytes del ARM9", 0, bytes));
        }
        if (options.RunWithoutRunningShoes)
        {
            arm9 ??= data.Arm9();
            // The jump taken when the Running Shoes flag is unset is cleared (UPR-ZX applyRunWithoutRunningShoesPatch).
            byte[] check = Convert.FromHexString(data.Profile.Kind == Gen4Kind.HeartGoldSoulSilver ? "301C0C24" : "281C0C24");
            int at = Gen4Data.FindUnique(arm9, check, "la comprobación de las Deportivas en el ARM9") + 0xE;
            arm9[at] = 0;
            arm9[at + 1] = 0;
            changes.Add(new Gen5Change("Ajustes · Correr sin Deportivas", "Bytes del ARM9", 0, 2));
        }
        if (options.NationalDexAtStart)
            changes.Add(new Gen5Change("Ajustes · Pokédex Nacional desde el inicio", "Bytes del script",
                0, PatchScript(data, scripts.Value, NationalDexScript(data.Profile.Kind), prefix + "_national_dex.ips", data.Profile.Kind == Gen4Kind.HeartGoldSoulSilver ? 4 : 0)));
        if (options.FastDistortionWorld)
            changes.Add(new Gen5Change("Ajustes · Mundo Distorsión rápido", "Bytes del script", 0, PatchScript(data, scripts.Value, SpearPillarPortalScript, "pt_fast_distortion_world.ips", 12)));
        var files = new Dictionary<string, byte[]>();
        if (scripts.IsValueCreated)
            files[data.PathOf(Gen4Profile.Scripts)] = scripts.Value.Write();
        return new Gen5RandomizerResult(files, changes, new Dictionary<int, byte[]>()) { Arm9 = arm9 };
    }

    // Some patches expect zero bytes appended first: 4 for HeartGold's national dex, 12 for the Distortion World (UPR-ZX).
    private static int PatchScript(Gen4Data data, Narc scripts, int file, string patch, int grow)
    {
        byte[] script = scripts.Files[file];
        if (!script.AsSpan().SequenceEqual(data.OriginalScript(file)))
            throw new InvalidDataException($"El script {file} ya no es el original: este ajuste ya se aplicó o lo cambió otra operación.");
        byte[] patched = [.. script, .. new byte[grow]];
        int bytes = Ips.Apply(patched, Patch(patch));
        scripts.Files[file] = patched;
        return bytes;
    }

    private static byte[] Patch(string name)
    {
        using var stream = typeof(NdsTweaks).Assembly.GetManifestResourceStream("pk3DS.Core.NDS.Patches." + name)
            ?? throw new InvalidDataException("Falta el parche " + name + ".");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

/// <summary>IPS patches applied in place: records may not grow the target.</summary>
public static class Ips
{
    /// <summary>Applies <paramref name="patch"/> to <paramref name="target"/> and returns how many bytes it wrote.</summary>
    public static int Apply(byte[] target, byte[] patch)
    {
        if (patch.Length < 8 || !patch.AsSpan(0, 5).SequenceEqual("PATCH"u8))
            throw new InvalidDataException("El parche no es IPS.");
        int at = 5, written = 0;
        while (true)
        {
            if (at + 3 > patch.Length)
                throw new InvalidDataException("Parche IPS truncado.");
            if (patch.AsSpan(at, 3).SequenceEqual("EOF"u8))
                return written;
            int offset = patch[at] << 16 | patch[at + 1] << 8 | patch[at + 2], size = patch[at + 3] << 8 | patch[at + 4];
            at += 5;
            if (size == 0)
            {
                int run = patch[at] << 8 | patch[at + 1];
                if (offset + run > target.Length)
                    throw new InvalidDataException("El parche IPS sale del archivo.");
                target.AsSpan(offset, run).Fill(patch[at + 2]);
                at += 3;
                written += run;
            }
            else
            {
                if (offset + size > target.Length || at + size > patch.Length)
                    throw new InvalidDataException("El parche IPS sale del archivo.");
                patch.AsSpan(at, size).CopyTo(target.AsSpan(offset));
                at += size;
                written += size;
            }
        }
    }
}
