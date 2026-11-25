// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace pk3DS.Core.Modding.Research;

public sealed record DisguiseUnlockFiles(byte[] Battle, byte[] Crr);

/// <summary>
/// «Disfraz en otras especies» for Ultra Sun / Ultra Moon. In Gen7, «Disguise intact» means Mimikyu in form 0 or 2 and
/// busting it is a form change to 1 or 3. Mimikyu keeps that path. For every other species with Disguise, bit 15 of the
/// battle Pokémon's 16-bit field at +0x1C records «busted»: the first damaging hit is cancelled with the decoy message
/// and no form change; the bit lives for the whole battle (switching, fainting, reviving) and is gone with the battle
/// Pokémon object. Only Battle.cro and static.crr are written; Battle.cro is the same file in both games.
/// </summary>
public static class DisguiseUnlockPatch
{
    public const string CapabilityId = "usum.disguise-unlock";
    public const ulong UltraSunTitleId = 0x00040000001B5000, UltraMoonTitleId = 0x00040000001B5100;
    public const string ProfileId = "Ultra Sol / Ultra Luna · Battle.cro 334AB920 (común a los dos juegos)";
    private const int TemplateSize = 0xF0, MarkerSize = 0x10, BlockSize = TemplateSize + MarkerSize, GuardBytes = 8;
    private static readonly byte[] ActiveMarker = "DGDISG01"u8.ToArray(), DisabledMarker = "DGDISG00"u8.ToArray();
    private const uint SpeciesTest = 0xE2401C03; // sub r1, r0, #0x300

    public static IReadOnlyList<(int Offset, string Role, int Entry)> Hooks { get; } =
    [
        (0xD8DA8, "Rotura del Disfraz", DisguiseUsumPayload.Bust),
        (0xDE078, "Comprobación previa (1)", DisguiseUsumPayload.PreA),
        (0xDE118, "Comprobación previa (2)", DisguiseUsumPayload.PreB),
        (0xDE530, "Comprobación previa (3)", DisguiseUsumPayload.PreC),
    ];

    private static readonly (int Offset, int Length, string Hash)[] Windows =
    [
        (0xD8D8C, 0x110, "D751EA9D18BCA87C8CDF82A3ED1C3AA7F99B32D052CB3184F92F526379E59B95"),
        (0xDE034, 0xA0, "ABDA1BE7161A30435A39D96170BBC78893E90BD8B7E4729CDAE9D564D569BEE4"),
        (0xDE0D4, 0x94, "0E816D09A6C935993A6A4CD80F01CDDBBEC557BA0FECA7D29028CD60014B426C"),
        (0xDE4DC, 0xB0, "AE3196EE82295DE0026C03B109A276159A4F581FB24FB377AC3D4BDC4129B0AF"),
        (0x7F7A8, 0x10, "13EE24826F74CCA3CD2D82BADF39F7FABFFF52599089549CB103B4476C2B6116"),
        (0x876F8, 0x10, "7B72E98A0306437EF56CED94DAE1FCAB56CAD8595B2A30117772E2A925770236"),
        (0x89C6C, 0x10, "5D3A1854FADA31169843BF2CC0BA8428EA2D3958205466E93C2C263DBFAED14D"),
        (0x89E28, 0x10, "FA175A6B9AD66545E6953294EA783C3CFA351E2B65D517E6C23BE8EE1CFACCC5"),
        (0x7CCF4, 0x10, "6876930268B95193F346272D49E6484CB90C6725A775282508EBA8BBB42E1E02"),
    ];

    /// <summary>Byte ranges another Battle.cro patch must leave untouched.</summary>
    public static IReadOnlyList<(int Start, int End)> Ranges { get; } = Windows.Select(w => (w.Offset, w.Offset + w.Length)).ToArray();

    public static MultitypeUnlockInspection Inspect(ulong? titleId, byte[]? battle, byte[]? crr)
    {
        var resources = new List<PatchResourceReport>();
        MultitypeUnlockInspection Result(MultitypeUnlockState state, PatchCapabilityLevel level, string summary, IReadOnlyList<PatchChangedRange>? changes = null, IReadOnlyList<string>? resized = null) =>
            new(state, new PatchCapabilityReport(CapabilityId, level, resources.ToArray(), changes ?? [], resized ?? [], summary));
        MultitypeUnlockInspection Reject(string summary) => Result(MultitypeUnlockState.NotAccredited, PatchCapabilityLevel.NotAccredited, summary);
        if (titleId is null) return Reject("Datos insuficientes: falta la cabecera NCCH.");
        if (titleId is not (UltraSunTitleId or UltraMoonTitleId)) return Reject($"Juego {titleId:X16} sin perfil: solo Ultra Sol y Ultra Luna.");
        if (battle is null) return Reject("Datos insuficientes: falta Battle.cro.");
        if (battle.Length < 0x180 || !battle.AsSpan(0x80, 4).SequenceEqual("CRO0"u8)) return Reject("Battle.cro no tiene la cabecera CRO0.");
        var copy = (byte[])battle.Clone();
        if (!CroSegmentExpander.TryUpdateHashes(copy, out _) || !copy.AsSpan().SequenceEqual(battle)) return Reject("Los hashes internos de Battle.cro no corresponden a su contenido.");
        foreach (var (offset, length, hash) in Windows)
        {
            if (offset + length > battle.Length) return Reject("Battle.cro truncado.");
            byte[] window = battle.AsSpan(offset, length).ToArray();
            foreach (var hook in Hooks) if (hook.Offset >= offset && hook.Offset + 4 <= offset + length) window.AsSpan(hook.Offset - offset, 4).Clear();
            if (Convert.ToHexString(SHA256.HashData(window)) != hash)
                return Reject($"Battle.cro no coincide con la revisión acreditada o otra modificación cambió el código del que depende (0x{offset:X5}).");
        }
        if (crr is null) return Reject("Datos insuficientes: falta .crr/static.crr.");
        if (crr.Length < 0x358) return Reject("static.crr incompleto.");
        int crrStart = BitConverter.ToInt32(crr, 0x350), crrCount = BitConverter.ToInt32(crr, 0x354);
        if (crrCount < 1 || crrStart < 0x358 || (long)crrStart + 32L * crrCount > crr.Length) return Reject("La tabla de static.crr está fuera de rango.");
        byte[] header = SHA256.HashData(battle.AsSpan(0, 0x80));
        if (Enumerable.Range(0, crrCount).Count(i => crr.AsSpan(crrStart + 32 * i, 32).SequenceEqual(header)) != 1)
            return Reject("static.crr no acredita Battle.cro; usa Integridad CRO/CRR antes de instalar.");

        int active = Find(battle, ActiveMarker), disabled = Find(battle, DisabledMarker);
        if (active == -2 || disabled == -2 || (active >= 0 && disabled >= 0))
            return Result(MultitypeUnlockState.Conflict, PatchCapabilityLevel.Conflict, "Hay más de un bloque de esta función en Battle.cro.");
        int block = Math.Max(active, disabled) >= 0 ? Math.Max(active, disabled) - TemplateSize : -1;
        if (block >= 0 && (block < 0x180 || !battle.AsSpan(block, TemplateSize).SequenceEqual(Payload(block))))
            return Result(MultitypeUnlockState.Conflict, PatchCapabilityLevel.Conflict, "El bloque de la rutina cambió externamente.");
        var states = new List<PatchResourceState>();
        foreach (var hook in Hooks)
        {
            uint word = BitConverter.ToUInt32(battle, hook.Offset);
            var state = word == SpeciesTest ? PatchResourceState.Original
                : block >= 0 && word == Branch(hook.Offset, block + hook.Entry) ? PatchResourceState.OwnInstall : PatchResourceState.ExternalModification;
            states.Add(state);
            resources.Add(new($"Battle.cro · 0x{hook.Offset:X5}", hook.Role, state, (uint)hook.Offset,
                state switch { PatchResourceState.Original => "Comprobación original de Mimikyu.", PatchResourceState.OwnInstall => "Enlace a la rutina instalado.", _ => "Instrucción desconocida: otra modificación." }));
        }
        if (states.Contains(PatchResourceState.ExternalModification))
            return Result(MultitypeUnlockState.Conflict, PatchCapabilityLevel.Conflict, "Otra modificación ocupa un punto de Disfraz; no se sobrescribe.");
        bool allOriginal = states.All(s => s == PatchResourceState.Original), allOwn = states.All(s => s == PatchResourceState.OwnInstall);
        if ((allOwn && active < 0) || (allOriginal && active >= 0) || (!allOriginal && !allOwn))
            return Result(MultitypeUnlockState.Partial, PatchCapabilityLevel.NotAccredited, "Instalación parcial: restaura desde el historial.");
        var changes = Hooks.Select(h => new PatchChangedRange("Battle.cro", (uint)h.Offset, 4)).Append(new("Battle.cro", 0, 0x80)).Append(new("static.crr", (uint)crrStart, crrCount * 32)).ToList();
        if (block >= 0) changes.Add(new("Battle.cro", (uint)block, BlockSize));
        return allOwn
            ? Result(MultitypeUnlockState.Installed, PatchCapabilityLevel.Verified, "Instalado. " + ProfileId + ".", changes)
            : Result(MultitypeUnlockState.Original, block < 0 ? PatchCapabilityLevel.CompatibleByAdaptation : PatchCapabilityLevel.Verified,
                "Sin instalar." + (block < 0 ? " La instalación añade un bloque de código a Battle.cro (en el relleno libre o ampliando el segmento)." : "") + " " + ProfileId + ".", changes, block < 0 ? ["Battle.cro"] : []);
    }

    public static DisguiseUnlockFiles Install(ulong? titleId, byte[] battle, byte[] crr)
    {
        var inspection = Inspect(titleId, battle, crr);
        if (inspection.State != MultitypeUnlockState.Original) throw new InvalidDataException("No se escribirá nada. " + inspection.Report.Summary);
        byte[] image; int block;
        int disabled = Find(battle, DisabledMarker);
        if (disabled >= 0) { image = (byte[])battle.Clone(); block = disabled - TemplateSize; }
        else
        {
            // Ordinary allocation (as Mega Ability Sync): it takes a genuinely free run of the executable padding and
            // only grows segment 0 when nothing fits, so code that older patches (Player Level Caps) keep in that
            // padding is neither overwritten nor moved. The routine only has relative branches: no relocations needed.
            if (!CroPatchSession.TryCreate(battle, out var session, out string error) ||
                !session.TryAllocateCode(GuardBytes + BlockSize, "USUM disguise for other species", out var grant, out error))
                throw new InvalidDataException("No se pudo reservar espacio en Battle.cro: " + error);
            block = checked((int)grant.Offset) + GuardBytes;
            byte[] bytes = new byte[GuardBytes + BlockSize]; Payload(block).CopyTo(bytes, GuardBytes);
            if (!session.TryWriteCode(grant, bytes, out error) || !session.TryBuildImage(out var built, out _, out error))
                throw new InvalidDataException("No se pudo preparar Battle.cro: " + error);
            image = built;
        }
        ActiveMarker.CopyTo(image, block + TemplateSize);
        foreach (var hook in Hooks) BitConverter.GetBytes(Branch(hook.Offset, block + hook.Entry)).CopyTo(image, hook.Offset);
        return Finish(titleId, battle, image, crr, MultitypeUnlockState.Installed);
    }

    /// <summary>Restores the four original instructions and disables the block (kept for a later reinstall).</summary>
    public static DisguiseUnlockFiles Remove(ulong? titleId, byte[] battle, byte[] crr)
    {
        var inspection = Inspect(titleId, battle, crr);
        if (inspection.State != MultitypeUnlockState.Installed) throw new InvalidDataException("No se escribirá nada. " + inspection.Report.Summary);
        byte[] image = (byte[])battle.Clone();
        DisabledMarker.CopyTo(image, Find(battle, ActiveMarker));
        foreach (var hook in Hooks) BitConverter.GetBytes(SpeciesTest).CopyTo(image, hook.Offset);
        return Finish(titleId, battle, image, crr, MultitypeUnlockState.Original);
    }

    private static DisguiseUnlockFiles Finish(ulong? titleId, byte[] old, byte[] image, byte[] crr, MultitypeUnlockState expected)
    {
        if (!CroSegmentExpander.TryUpdateHashes(image, out string error)) throw new InvalidDataException("No se pudieron recalcular los hashes de Battle.cro: " + error);
        byte[] newCrr = RareCandyMultiUsePatch.ReplaceCrrEntry(crr, SHA256.HashData(old.AsSpan(0, 0x80)), SHA256.HashData(image.AsSpan(0, 0x80)));
        var check = Inspect(titleId, image, newCrr);
        if (check.State != expected) throw new InvalidDataException("La relectura del resultado no coincide: " + check.Report.Summary);
        return new(image, newCrr);
    }

    /// <summary>The routine as written at Battle.cro offset <paramref name="block"/>.</summary>
    public static byte[] RoutineAt(int block) => Payload(block);

    private static byte[] Payload(int block)
    {
        byte[] data = DisguiseUsumPayload.Create();
        int baseAddress = DisguiseUsumPayload.TemplateBase;
        for (int o = 0; o < data.Length; o += 4)
        {
            uint word = BitConverter.ToUInt32(data, o);
            if ((word & 0x0E000000) != 0x0A000000 || word >> 28 == 0xF) continue;
            int target = baseAddress + o + 8 + ((int)(word << 8) >> 6);
            if (target >= baseAddress && target < baseAddress + data.Length) target = target - baseAddress + block;
            int delta = target - (block + o) - 8;
            BitConverter.GetBytes((word & 0xFF000000) | ((uint)(delta >> 2) & 0xFFFFFF)).CopyTo(data, o);
        }
        return data;
    }

    private static uint Branch(int from, int target)
    {
        int delta = checked(target - from - 8);
        if ((delta & 3) != 0 || delta is < -0x2000000 or >= 0x2000000) throw new InvalidDataException("Salto ARM fuera de rango.");
        return 0xEA000000u | ((uint)(delta >> 2) & 0xFFFFFF);
    }

    private static int Find(byte[] data, byte[] marker)
    {
        int first = data.AsSpan().IndexOf(marker);
        if (first < 0) return -1;
        return data.AsSpan(first + 1).IndexOf(marker) >= 0 ? -2 : first;
    }
}
