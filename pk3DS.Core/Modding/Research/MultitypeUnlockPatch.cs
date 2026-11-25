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
        new("Ultra Sol · code.bin b5388f75", 0x00040000001B5000, 0x5A4000, 0x3AF280, 0x12C, "0E5E70AFB6FCC4E8F01490AD81D34BE99FF34E109405D5275797B2395B580B28", 0x3AF2A0, 0x3AF334),
        new("Ultra Luna · code.bin 0aa12056", 0x00040000001B5100, 0x5A4000, 0x3AF288, 0x12C, "43CF94113CB9178302F149794F6DA6C2F0AF11A324354781ABB42020D5FC250D", 0x3AF2A8, 0x3AF33C),
    ];

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
        byte[] window = code.AsSpan(profile.WindowStart, profile.WindowLength).ToArray();
        foreach (int site in new[] { profile.Type1Site, profile.Type2Site }) window.AsSpan(site - profile.WindowStart, 8).Clear();
        if (Convert.ToHexString(SHA256.HashData(window)) != profile.WindowHash)
            return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "Las funciones de tipo no son las acreditadas: otra revisión u otra modificación.");
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
