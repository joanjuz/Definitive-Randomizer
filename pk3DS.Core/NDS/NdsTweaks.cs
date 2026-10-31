#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>Game tweaks from Universal Pokémon Randomizer ZX (GPLv3); each one is optional.</summary>
public sealed record NdsTweakOptions
{
    /// <summary>Gen4 only (ARM9).</summary>
    public bool FastestText { get; init; }
    /// <summary>Gen4 only (script).</summary>
    public bool NationalDexAtStart { get; init; }
    public bool RunWithoutRunningShoes { get; init; }
    /// <summary>Platinum only: the Distortion World sequence is shortened.</summary>
    public bool FastDistortionWorld { get; init; }
    /// <summary>Steel no longer resists Ghost and Dark, as from Gen6 on (UPR-ZX "update type effectiveness").</summary>
    public bool UpdateTypeEffectiveness { get; init; }
    /// <summary>Every ball catches at the first throw (UPR-ZX guaranteed catching).</summary>
    public bool GuaranteedCatch { get; init; }
    /// <summary>Gen5 only: the low-HP music never plays.</summary>
    public bool DisableLowHpMusic { get; init; }
    /// <summary>Pickup finds random items, chosen with <see cref="Seed"/>.</summary>
    public bool RandomPickupItems { get; init; }
    public uint Seed { get; init; }
}

/// <summary>
/// Code and script tweaks for Diamond, Platinum, HeartGold, Black, White and White 2, applied the way UPR-ZX applies them (IPS
/// patches and single instructions), on the original code only: the ARM9 and every overlay must still be the original (as the
/// profile checks), the bytes to change are found in the original and must still be there, and every patched script must
/// still be its original file, so a tweak never lands on code it was not made for nor twice. Gen5's ARM9 is never touched.
/// </summary>
public static class NdsTweaks
{
    // Items Pickup may find: held items and the usual consumables (Full Restore … PP Up, Nugget), the same ids in Gen4 and Gen5.
    private static readonly int[] PickupPool = [.. NdsTrainerTools.HeldItemIds, 23, 24, 25, 26, 28, 29, 38, 39, 40, 41, 50, 51, 92];

    // UPR-ZX gen4_offsets.ini NationalDexScriptOffset, Gen4Constants.ptSpearPillarPortalScriptFile and runningShoesCheckPrefix*.
    private static int NationalDexScript(Gen4Kind kind) => kind switch { Gen4Kind.DiamondPearl => 990, Gen4Kind.Platinum => 1064, _ => 229 };
    private const int SpearPillarPortalScript = 237;

    private static string Prefix(Gen4Kind kind) => kind switch { Gen4Kind.DiamondPearl => "dp", Gen4Kind.Platinum => "plat", _ => "hgss" };

    /// <summary>Why the ARM9 tweaks (fastest text, running without shoes) cannot be used on this ROM, or null when they can.</summary>
    public static string? Problem(Gen4Data data) => data.Unavailable.ContainsKey(Gen5Feature.Shops)
        ? "el romhack modifica el ARM9, donde se aplican los ajustes" : null;

    public static Gen5RandomizerResult Apply(Gen4Data data, NdsTweakOptions options)
    {
        if ((options.FastestText || options.RunWithoutRunningShoes) && Problem(data) is { } problem)
            throw new ArgumentException("Ajustes no disponibles: " + problem + ".", nameof(options));
        if (options.DisableLowHpMusic)
            throw new ArgumentException("Quitar la música de PS bajos solo está disponible en Negro, Blanco y Blanco 2.", nameof(options));
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
        var overlays = new Dictionary<int, byte[]>();
        int battle = data.Profile.BattleOverlay;
        byte[] Battle() => overlays.TryGetValue(battle, out var o) ? o : overlays[battle] = data.ReadOverlay(battle);
        if (options.UpdateTypeEffectiveness)
        {
            // Gen4 lists (attacker, defender, multiplier×10) until 0xFE; Ghost is 7, Steel 8, Dark 17 (UPR-ZX Gen4 type table).
            byte[] overlay = Battle();
            int start = Locate(overlay, data.OriginalOverlay(battle), "000505000805", 0, "la tabla de tipos");
            int fixedCount = 0;
            for (int at = start; at + 3 <= overlay.Length && overlay[at] != 0xFE; at += 3)
                if (overlay[at + 1] == 8 && overlay[at] is 7 or 17)
                {
                    if (overlay[at + 2] != 5)
                        throw new InvalidDataException("La tabla de tipos ya está actualizada.");
                    overlay[at + 2] = 10;
                    fixedCount++;
                }
            if (fixedCount != 2)
                throw new InvalidDataException("La tabla de tipos no tiene la forma esperada.");
            changes.Add(new Gen5Change("Ajustes · Tabla de tipos de Gen6", "Bytes del overlay", 0, 2));
        }
        if (options.GuaranteedCatch)
        {
            // cmp r1, #0xFF / bcc: cleared, so every throw counts as a 255 catch rate.
            byte[] overlay = Battle();
            int at = Locate(overlay, data.OriginalOverlay(battle), "FF2901D30425", 4, "la comprobación de captura");
            overlay.AsSpan(at, 4).Clear();
            changes.Add(new Gen5Change("Ajustes · Captura garantizada", "Bytes del overlay", 0, 4));
        }
        if (options.RandomPickupItems)
        {
            // 18 common and 11 rare items (UPR-ZX pickupTableLocator and rarePickupTableLocator), found in the original overlay.
            byte[] overlay = Battle(), original = data.OriginalOverlay(battle);
            changes.AddRange(Pickup(overlay, Gen4Data.FindUnique(original, Convert.FromHexString("110012001A000300"), "la tabla de Recogida"), 18, options.Seed, "common"));
            changes.AddRange(Pickup(overlay, Gen4Data.FindUnique(original, Convert.FromHexString("19005C00DD00"), "la tabla de Recogida rara"), 11, options.Seed, "rare"));
        }
        var files = new Dictionary<string, byte[]>();
        if (scripts.IsValueCreated)
            files[data.PathOf(Gen4Profile.Scripts)] = scripts.Value.Write();
        return new Gen5RandomizerResult(files, changes, overlays) { Arm9 = arm9, RecompressOverlays = overlays.Keys.ToHashSet() };
    }

    /// <summary>Black, White and White 2: tweaks in overlays only (their ARM9 is DSi and never modified).</summary>
    public static Gen5RandomizerResult Apply(Gen5Data data, NdsTweakOptions options)
    {
        if (options.FastestText || options.NationalDexAtStart || options.FastDistortionWorld)
            throw new ArgumentException("Negro, Blanco y Blanco 2 no admiten ese ajuste: está en el ARM9 DSi o en el script de los iniciales.", nameof(options));
        var overlays = new Dictionary<int, byte[]>();
        var changes = new List<Gen5Change>();
        byte[] Overlay(int id) => overlays.TryGetValue(id, out var o) ? o : overlays[id] = data.ReadOverlay(id);
        var p = data.Profile;
        if (options.RunWithoutRunningShoes)
        {
            // The branch taken when the Running Shoes flag is unset is cleared (UPR-ZX runningShoesPrefix).
            byte[] overlay = Overlay(p.FieldOverlay);
            int at = Locate(overlay, data.OriginalOverlay(p.FieldOverlay), "01D0012008BD002008BD63", 2, "la comprobación de las Deportivas");
            overlay.AsSpan(at, 2).Clear();
            changes.Add(new Gen5Change("Ajustes · Correr sin Deportivas", "Bytes del overlay", 0, 2));
        }
        if (options.UpdateTypeEffectiveness)
        {
            // A 17×17 matrix of multipliers×4 by internal type: Ghost 7, Steel 8, Dark 16 (UPR-ZX Gen5 type table).
            byte[] overlay = Overlay(p.BattleOverlay);
            int start = Gen4Data.FindUnique(data.OriginalOverlay(p.BattleOverlay), Convert.FromHexString("0404040404020400"), "la tabla de tipos");
            foreach (int at in new[] { start + 7 * 17 + 8, start + 16 * 17 + 8 })
            {
                if (overlay[at] != 2)
                    throw new InvalidDataException("La tabla de tipos ya está actualizada o no tiene la forma esperada.");
                overlay[at] = 4;
            }
            changes.Add(new Gen5Change("Ajustes · Tabla de tipos de Gen6", "Bytes del overlay", 0, 2));
        }
        if (options.GuaranteedCatch)
        {
            // The blt after comparing the catch odds with 255 is cleared (UPR-ZX perfectOddsBranchLocator).
            byte[] overlay = Overlay(p.BattleOverlay);
            int at = Locate(overlay, data.OriginalOverlay(p.BattleOverlay), "08DB002801D0012000E0", 2, "la comprobación de captura");
            overlay.AsSpan(at, 2).Clear();
            changes.Add(new Gen5Change("Ajustes · Captura garantizada", "Bytes del overlay", 0, 2));
        }
        if (options.DisableLowHpMusic)
        {
            // bne → b: the game never sees a Pokémon with low HP when choosing the music (UPR-ZX lowHealthMusicLocator).
            byte[] overlay = Overlay(p.LowHpOverlay);
            int at = Locate(overlay, data.OriginalOverlay(p.LowHpOverlay), "00D10127", 2, "la música de PS bajos");
            overlay[at + 1] = 0xE0;
            changes.Add(new Gen5Change("Ajustes · Sin música de PS bajos", "Bytes del overlay", 0, 1));
        }
        if (options.RandomPickupItems)
            changes.AddRange(Pickup(Overlay(p.PickupOverlay), Gen4Data.FindUnique(data.OriginalOverlay(p.PickupOverlay), Convert.FromHexString("19005C00DD00"), "la tabla de Recogida"),
                29, options.Seed, "items"));
        return new Gen5RandomizerResult(new Dictionary<string, byte[]>(), changes, overlays) { RecompressOverlays = overlays.Keys.ToHashSet() };
    }

    // Offset of a code pattern found once in the original binary; its first <paramref name="patched"/> bytes must still be the
    // original ones in the current binary, or the tweak was already applied.
    private static int Locate(byte[] current, byte[] original, string hex, int patched, string what)
    {
        byte[] pattern = Convert.FromHexString(hex);
        int at = Gen4Data.FindUnique(original, pattern, what);
        if (!current.AsSpan(at, Math.Max(patched, 1)).SequenceEqual(pattern.AsSpan(0, Math.Max(patched, 1))))
            throw new InvalidDataException($"Ese ajuste ya está aplicado ({what}).");
        return at;
    }

    private static IEnumerable<Gen5Change> Pickup(byte[] overlay, int at, int count, uint seed, string table)
    {
        var changes = new List<Gen5Change>();
        for (int i = 0; i < count; i++)
        {
            int before = BinaryPrimitives.ReadUInt16LittleEndian(overlay.AsSpan(at + i * 2));
            int item = PickupPool[Gen5Randomizer.Index(seed, $"pickup/{table}/{i}", PickupPool.Length)];
            BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(at + i * 2), (ushort)item);
            changes.Add(new Gen5Change("Recogida", "Objeto " + (i + 1) + (table == "rare" ? " (raro)" : ""), before, item));
        }
        return changes;
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
