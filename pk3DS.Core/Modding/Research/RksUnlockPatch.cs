// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// «Sistema Alfa en otras especies» for Ultra Sun / Ultra Moon. The CoreParam type getters return the held Memory's type
/// only when the species is Silvally (773) and the ability is RKS System (225). Per function, two words change:
/// <list type="bullet">
/// <item><c>cmp r5, r1</c> (Silvally) becomes <c>cmp r0, #0xE1</c> (RKS System), so any species with the ability takes
/// the Memory branch (Normal without one) and Silvally behaves as before.</item>
/// <item>The Arceus branch's exit for abilities other than Multitype (<c>bne</c> to Personal) is retargeted to that RKS
/// check, so an Arceus with RKS System also uses its Memory. Without RKS System the check fails and the result is the
/// original Personal path.</item>
/// </list>
/// Shares profiles and fingerprint with <see cref="MultitypeUnlockPatch"/>; both options are independent and combine
/// (Multitype first, then RKS System, then Personal). Does not read or write forms, models or stats.
/// </summary>
public static class RksUnlockPatch
{
    public const string CapabilityId = "usum.rks-unlock";

    public static MultitypeUnlockInspection Inspect(ulong? titleId, byte[]? code, IReadOnlyList<MultitypeUnlockProfile>? profiles = null)
    {
        var resources = new List<PatchResourceReport>();
        MultitypeUnlockInspection Result(MultitypeUnlockState state, PatchCapabilityLevel level, string summary, IReadOnlyList<PatchChangedRange>? changes = null) =>
            new(state, new PatchCapabilityReport(CapabilityId, level, resources.ToArray(), changes ?? [], [], summary));
        if (titleId is null) return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "Datos insuficientes: falta la cabecera NCCH.");
        var profile = (profiles ?? MultitypeUnlockPatch.Profiles).FirstOrDefault(p => p.TitleId == titleId);
        if (profile is null) return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, $"Juego {titleId:X16} sin perfil: solo Ultra Sol y Ultra Luna.");
        if (code is null) return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "Datos insuficientes: falta code.bin.");
        if (code.Length != profile.CodeLength) return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "code.bin no está descomprimido o no es la revisión acreditada.");
        if (Convert.ToHexString(SHA256.HashData(MultitypeUnlockPatch.MaskedWindow(code, profile))) != profile.WindowHash)
            return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "Las funciones de tipo no son las acreditadas: otra revisión u otra modificación.");
        if (MultitypeUnlockPatch.MultitypeSitesState(code, profile) == PatchResourceState.ExternalModification)
            return Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, "Otra modificación cambió la comprobación de Multitipo en las funciones de tipo.");
        var state = MultitypeUnlockPatch.RksState(code, profile);
        foreach (var (site, role) in new[] { (profile.Type1Site, "Tipo 1"), (profile.Type2Site, "Tipo 2") })
            resources.Add(new($"code.bin · 0x{site + MultitypeUnlockPatch.RksCompareDelta:X6}", role + ": condición de Sistema Alfa", state, (uint)(site + MultitypeUnlockPatch.RksCompareDelta),
                state switch { PatchResourceState.Original => "Comprobación original de Silvally.", PatchResourceState.OwnInstall => "Comprobación de Sistema Alfa para cualquier especie.", _ => "Estado mixto o desconocido." }));
        var changes = new[] { profile.Type1Site, profile.Type2Site }
            .SelectMany(s => new[] { new PatchChangedRange("code.bin", (uint)(s + MultitypeUnlockPatch.RksExitDelta), 4), new PatchChangedRange("code.bin", (uint)(s + MultitypeUnlockPatch.RksCompareDelta), 4) }).ToArray();
        return state switch
        {
            PatchResourceState.Original => Result(MultitypeUnlockState.Original, PatchCapabilityLevel.Verified, "Sin instalar. " + profile.Id + ".", changes),
            PatchResourceState.OwnInstall => Result(MultitypeUnlockState.Installed, PatchCapabilityLevel.Verified, "Instalado. " + profile.Id + ".", changes),
            PatchResourceState.PartialInstall => Result(MultitypeUnlockState.Partial, PatchCapabilityLevel.NotAccredited, "Instalación parcial de Sistema Alfa; restaura desde el historial."),
            _ => Result(MultitypeUnlockState.Conflict, PatchCapabilityLevel.Conflict, "Otra modificación ocupa la comprobación de Sistema Alfa; no se sobrescribe."),
        };
    }

    public static byte[] Apply(ulong? titleId, byte[] code, bool enable, IReadOnlyList<MultitypeUnlockProfile>? profiles = null)
    {
        var inspection = Inspect(titleId, code, profiles);
        if (inspection.State != (enable ? MultitypeUnlockState.Original : MultitypeUnlockState.Installed))
            throw new InvalidDataException("No se escribirá nada. " + inspection.Report.Summary);
        var profile = (profiles ?? MultitypeUnlockPatch.Profiles).First(p => p.TitleId == titleId);
        byte[] output = (byte[])code.Clone();
        foreach (int site in new[] { profile.Type1Site, profile.Type2Site })
        {
            BitConverter.GetBytes(enable ? MultitypeUnlockPatch.RksExitPatched : MultitypeUnlockPatch.RksExitOriginal).CopyTo(output, site + MultitypeUnlockPatch.RksExitDelta);
            BitConverter.GetBytes(enable ? MultitypeUnlockPatch.RksComparePatched : MultitypeUnlockPatch.RksCompareOriginal).CopyTo(output, site + MultitypeUnlockPatch.RksCompareDelta);
        }
        if (Inspect(titleId, output, profiles).State != (enable ? MultitypeUnlockState.Installed : MultitypeUnlockState.Original))
            throw new InvalidDataException("La relectura de Sistema Alfa no coincide.");
        return output;
    }
}
