// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace pk3DS.Core.Modding.Research;

public enum MultitypeUnlockState { Original, Installed, Partial, Conflict, NotAccredited }

public sealed record MultitypeUnlockInspection(MultitypeUnlockState State, PatchCapabilityReport Report);

/// <summary>One accredited code.bin revision: the two type getters and a fingerprint of both functions.</summary>
public sealed record MultitypeUnlockProfile(string Id, ulong TitleId, int CodeLength, int WindowStart, int WindowLength, string WindowHash, int Type1Site, int Type2Site);

/// <summary>
/// «Permitir Multitipo en otras especies» for Ultra Sun / Ultra Moon.
/// <para>
/// CoreParam's type getters (Type 1 and Type 2) return the type of the held Plate (Normal without one) only when the
/// species is Arceus (493) and the ability is Multitype (121); Silvally (773) with RKS System has its own branch; every
/// other Pokémon reads its Personal types. The species test <c>sub r2, r5, #0x100; subs r2, r2, #0xED</c> is replaced
/// by <c>cmp r0, #0x79; nop</c> (r0 holds the ability): Multitype takes the Plate branch for any species, and every
/// other ability follows the original path, Silvally included. Arceus with Multitype behaves as before. Nothing reads
/// or writes forms, models or stats.
/// </para>
/// </summary>
public static class MultitypeUnlockPatch
{
    public const string CapabilityId = "usum.multitype-unlock";
    private static readonly byte[] Original = Convert.FromHexString("012C45E2ED2052E2"); // sub r2, r5, #0x100 ; subs r2, r2, #0xED
    private static readonly byte[] Patched = Convert.FromHexString("790050E300F020E3");  // cmp r0, #0x79 ; nop

    public static IReadOnlyList<MultitypeUnlockProfile> Profiles { get; } =
    [
        new("Ultra Sol · code.bin b5388f75", 0x00040000001B5000, 0x5A4000, 0x3AF280, 0x12C, "00BA94AAA794FE108853F11677D24C088BC5DFA4F6889C46E5688E7847922DD3", 0x3AF2A0, 0x3AF334),
        new("Ultra Luna · code.bin 0aa12056", 0x00040000001B5100, 0x5A4000, 0x3AF288, 0x12C, "EC4CE0907CA88FEC218D0606286022F4E20CF077C39C3BC0686F071A166AD4EE", 0x3AF2A8, 0x3AF33C),
    ];

    // RKS System (RksUnlockPatch) shares both functions: two more words per function, relative to the Multitype site.
    // The window fingerprint excludes the sites of both options, so either can be installed without making the other
    // look modified; each option only accepts the original or the other's own bytes in the other's sites.
    internal const int RksExitDelta = 0x10, RksCompareDelta = 0x24;
    internal const uint RksExitOriginal = 0x1A000007, RksExitPatched = 0x1A000003;     // bne → Personal path / → RKS check
    internal const uint RksCompareOriginal = 0xE1550001, RksComparePatched = 0xE35000E1; // cmp r5, r1 (Silvally) / cmp r0, #0xE1 (RKS System)

    /// <summary>Both type functions with the Multitype and RKS System sites zeroed.</summary>
    public static byte[] MaskedWindow(byte[] code, MultitypeUnlockProfile profile)
    {
        byte[] window = code.AsSpan(profile.WindowStart, profile.WindowLength).ToArray();
        foreach (int site in new[] { profile.Type1Site, profile.Type2Site })
        {
            window.AsSpan(site - profile.WindowStart, 8).Clear();
            window.AsSpan(site + RksExitDelta - profile.WindowStart, 4).Clear();
            window.AsSpan(site + RksCompareDelta - profile.WindowStart, 4).Clear();
        }
        return window;
    }

    /// <summary>Original, own (installed) or unknown state of the RKS System words of one game.</summary>
    internal static PatchResourceState RksState(byte[] code, MultitypeUnlockProfile profile)
    {
        // Each word is judged on its own: an interrupted write leaves a mix of original and own words (partial).
        var words = new[] { profile.Type1Site, profile.Type2Site }
            .SelectMany(s => new[] { (BitConverter.ToUInt32(code, s + RksExitDelta), RksExitOriginal, RksExitPatched), (BitConverter.ToUInt32(code, s + RksCompareDelta), RksCompareOriginal, RksComparePatched) }).ToArray();
        if (words.Any(w => w.Item1 != w.Item2 && w.Item1 != w.Item3)) return PatchResourceState.ExternalModification;
        if (words.All(w => w.Item1 == w.Item2)) return PatchResourceState.Original;
        return words.All(w => w.Item1 == w.Item3) ? PatchResourceState.OwnInstall : PatchResourceState.PartialInstall;
    }

    internal static PatchResourceState MultitypeSitesState(byte[] code, MultitypeUnlockProfile profile)
    {
        var sites = new[] { profile.Type1Site, profile.Type2Site }.Select(s => code.AsSpan(s, 8).ToArray()).ToArray();
        if (sites.All(b => b.AsSpan().SequenceEqual(Original))) return PatchResourceState.Original;
        if (sites.All(b => b.AsSpan().SequenceEqual(Patched))) return PatchResourceState.OwnInstall;
        return sites.All(b => b.AsSpan().SequenceEqual(Original) || b.AsSpan().SequenceEqual(Patched)) ? PatchResourceState.PartialInstall : PatchResourceState.ExternalModification;
    }

    public static MultitypeUnlockInspection Inspect(ulong? titleId, byte[]? code, IReadOnlyList<MultitypeUnlockProfile>? profiles = null)
    {
        var resources = new List<PatchResourceReport>();
        MultitypeUnlockInspection Result(MultitypeUnlockState state, PatchCapabilityLevel level, string summary, IReadOnlyList<PatchChangedRange>? changes = null) =>
            new(state, new PatchCapabilityReport(CapabilityId, level, resources.ToArray(), changes ?? [], [], summary));
        if (titleId is null) return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "Datos insuficientes: falta la cabecera NCCH.");
        var profile = (profiles ?? Profiles).FirstOrDefault(p => p.TitleId == titleId);
        if (profile is null) return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, $"Juego {titleId:X16} sin perfil: solo Ultra Sol y Ultra Luna.");
        if (code is null) return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "Datos insuficientes: falta code.bin.");
        if (code.Length != profile.CodeLength) return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "code.bin no está descomprimido o no es la revisión acreditada.");
        if (Convert.ToHexString(SHA256.HashData(MaskedWindow(code, profile))) != profile.WindowHash)
            return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "Las funciones de tipo no son las acreditadas: otra revisión u otra modificación.");
        if (RksState(code, profile) == PatchResourceState.ExternalModification)
            return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "Otra modificación cambió la comprobación de Sistema Alfa en las funciones de tipo.");
        var states = new List<PatchResourceState>();
        foreach (var (site, role) in new[] { (profile.Type1Site, "Tipo 1"), (profile.Type2Site, "Tipo 2") })
        {
            var bytes = code.AsSpan(site, 8);
            var state = bytes.SequenceEqual(Original) ? PatchResourceState.Original : bytes.SequenceEqual(Patched) ? PatchResourceState.OwnInstall : PatchResourceState.ExternalModification;
            states.Add(state);
            resources.Add(new($"code.bin · 0x{site:X6}", role + ": condición de Multitipo", state, (uint)site,
                state switch { PatchResourceState.Original => "Comprobación original de Arceus.", PatchResourceState.OwnInstall => "Comprobación de Multitipo para cualquier especie.", _ => "Instrucciones desconocidas: otra modificación." }));
        }
        var changes = new[] { new PatchChangedRange("code.bin", (uint)profile.Type1Site, 8), new PatchChangedRange("code.bin", (uint)profile.Type2Site, 8) };
        if (states.Contains(PatchResourceState.ExternalModification)) return Result(MultitypeUnlockState.Conflict, PatchCapabilityLevel.Conflict, "Otra modificación ocupa la comprobación de Multitipo; no se sobrescribe.");
        if (states.Distinct().Count() > 1) return Result(MultitypeUnlockState.Partial, PatchCapabilityLevel.NotAccredited, "Instalación parcial (solo un tipo); restaura desde el historial.");
        return states[0] == PatchResourceState.OwnInstall
            ? Result(MultitypeUnlockState.Installed, PatchCapabilityLevel.Verified, "Instalado. " + profile.Id + ".", changes)
            : Result(MultitypeUnlockState.Original, PatchCapabilityLevel.Verified, "Sin instalar. " + profile.Id + ".", changes);
    }

    public static byte[] Apply(ulong? titleId, byte[] code, bool enable, IReadOnlyList<MultitypeUnlockProfile>? profiles = null)
    {
        var inspection = Inspect(titleId, code, profiles);
        var expected = enable ? MultitypeUnlockState.Original : MultitypeUnlockState.Installed;
        if (inspection.State != expected) throw new InvalidDataException("No se escribirá nada. " + inspection.Report.Summary);
        var profile = (profiles ?? Profiles).First(p => p.TitleId == titleId);
        byte[] output = (byte[])code.Clone();
        foreach (int site in new[] { profile.Type1Site, profile.Type2Site }) (enable ? Patched : Original).CopyTo(output, site);
        if (Inspect(titleId, output, profiles).State != (enable ? MultitypeUnlockState.Installed : MultitypeUnlockState.Original))
            throw new InvalidDataException("La relectura de Multitipo no coincide.");
        return output;
    }

    public static ReadOnlySpan<byte> OriginalBytes => Original;
    public static ReadOnlySpan<byte> PatchedBytes => Patched;
}
