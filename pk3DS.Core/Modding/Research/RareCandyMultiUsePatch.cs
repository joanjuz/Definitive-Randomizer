// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace pk3DS.Core.Modding.Research;

/// <summary>What the four resources of the Rare Candy multi-use patch look like together.</summary>
public enum RareCandyMultiUseState
{
    /// <summary>Accredited Omega Ruby files with every site original.</summary>
    Original,

    /// <summary>Every site carries this installer's bytes.</summary>
    Installed,

    /// <summary>Some sites are installed and others original.</summary>
    Partial,

    /// <summary>A site holds bytes that are neither original nor ours (another mod, the external reference patch…).</summary>
    Conflict,

    /// <summary>Not the accredited title, revision or file layout; nothing can be written.</summary>
    NotAccredited,
}

public sealed record RareCandyMultiUseInspection(RareCandyMultiUseState State, PatchCapabilityReport Report);

public sealed record RareCandyMultiUseFiles(byte[] Code, byte[] Bag, byte[] Crr);

/// <summary>
/// Fingerprints of one accredited revision. Hashes cover only the bytes the routine depends on, so other Progressive
/// edits of code.bin data tables do not invalidate the profile. <see cref="RareCandyMultiUsePatch.OmegaRuby"/> is the
/// only profile used by the application.
/// </summary>
public sealed record RareCandyMultiUseProfile(
    string Id,
    ulong TitleId,
    int CodeLength,
    string DetourWindowHash,
    IReadOnlyList<(uint Address, string Role, string Hash)> Callees,
    int BagLength,
    string BagMaskedHash,
    uint TextPages,
    uint TextSize,
    uint DataAddress,
    uint DataSize,
    uint BssSize);

/// <summary>
/// Rare Candy multi-use for Pokémon Omega Ruby: using a Rare Candy from the Bag asks how many to use, applies them one by
/// one while the game allows it (it stops at level 100 without spending the rest) and also teaches the level-up moves of
/// the skipped levels. Native rewrite of the reference patch by alvarocm21; only its reachable code is installed.
/// <para>
/// Only one profile is accredited: title 000400000011C400 with the code.bin whose layout and native functions match the
/// fingerprints below and the original DllBag.cro. Nothing is located by searching: every site is checked at its exact
/// offset, and any difference is reported instead of written. Inspection never modifies its inputs; installation and
/// removal return new buffers.
/// </para>
/// </summary>
public static class RareCandyMultiUsePatch
{
    public const string CapabilityId = "oras.rare-candy-multiuse";
    public const ulong OmegaRubyTitleId = 0x000400000011C400;
    public const ulong AlphaSapphireTitleId = 0x000400000011C500;
    public const string ProfileId = "Rubí Omega (000400000011C400) · perfil or-d587c98a";
    private const uint CodeBase = 0x100000;

    // Zero padding between the end of .text (0x479604) and the end of its last page (0x47A000).
    public const int CaveStart = 0x479604, CaveEnd = 0x47A000;

    // code.bin VA 0x3B5160: "bne skip" in the level-up move loop.
    public const int DetourOffset = 0x2B5160;
    private static readonly byte[] DetourOriginal = [0x3D, 0x00, 0x00, 0x1A];
    public const int DetourWindowStart = 0x2B5100, DetourWindowEnd = 0x2B5270;

    /// <summary>Native functions called by the routine (VA); a profile fingerprints their first 32 bytes.</summary>
    public static readonly IReadOnlyList<(uint Address, string Role)> NativeCalls =
    [
        (0x46DA9C, "comprobar si el objeto se puede usar"),
        (0x4635D0, "restar objetos de la Bolsa"),
        (0x46CC70, "aplicar el efecto del objeto"),
        (0x14FD1C, "nivel actual"),
    ];

    public static readonly RareCandyMultiUseProfile OmegaRuby = new(
        ProfileId, OmegaRubyTitleId, 0x530000,
        "83C1A8865DAB56A7334D32348E2FD1BF6AE7F8A6A6D979222EE868389AA7D216",
        [
            (0x46DA9C, "comprobar si el objeto se puede usar", "EB59E20E4F760B07DADE5046706F209444A4B03EB379450EDAFF77D450A69C1B"),
            (0x4635D0, "restar objetos de la Bolsa", "BB329C8680D08836C4BE4AC1EAB707A4D5F77EE606298C258DAF246B4EDA48ED"),
            (0x46CC70, "aplicar el efecto del objeto", "1C454E583CA709A58C261712F7F192189873C7248EBDA8498010C8E01A9555CC"),
            (0x14FD1C, "nivel actual", "F83BF674853312496240EE77C782F3DD7049D6300B0FE42551B8D2C608A2CB2E"),
        ],
        106496,
        // DllBag.cro with its 0x80-byte hash header and the four hook sites zeroed.
        "9DBA2C5147195E3492E171865E1EA411CED589938E85BC02A4BD29510F3964EF",
        0x47A, 0x479604, 0x5EC000, 0x439F4, 0x7EC2C);

    /// <summary>DllBag.cro hooks: 12 original bytes each, replaced by "mov r12, pc; ldr pc, [pc, #-4]; .word target".</summary>
    public static readonly IReadOnlyList<(int Offset, string Role, byte[] Original, uint Target)> Hooks =
    [
        (0x2B48, "Usar: abrir el selector de cantidad", Convert.FromHexString("F0432DE90060A0E1087080E2"), RareCandyMultiUsePayload.UseEntry),
        (0x2C88, "Aplicar los caramelos pendientes", Convert.FromHexString("580094E5EFF5FFEB0050A0E3"), RareCandyMultiUsePayload.ApplyLoop),
        (0x3618, "Continuar tras cada caramelo", Convert.FromHexString("300194E5041097E5D420D0E1"), RareCandyMultiUsePayload.ApplyNext),
        (0x6C30, "Respuesta del selector", Convert.FromHexString("0B0050E300F19F377C0000EA"), RareCandyMultiUsePayload.Dispatch),
    ];

    public static ReadOnlySpan<byte> DetourOriginalBytes => DetourOriginal;
    public const uint StateAddress = 0x6AEFF0;

    private const string CodeCave = "code.bin · rutina (relleno de .text)";
    private const string CodeDetour = "code.bin · movimientos de niveles saltados";
    private const string CrrEntry = "static.crr · hash de DllBag.cro";
    private const string StateMemory = "memoria · estado de 12 bytes";

    private static readonly RareCandyMultiUsePayload.Block[] Payload = RareCandyMultiUsePayload.Create();

    /// <summary>Copies of the routine blocks (VA and bytes), generated from native/3ds/rare-candy-multiuse/candy.s.</summary>
    public static IReadOnlyList<(uint Address, byte[] Bytes)> PayloadBlocks => Payload.Select(b => (b.Address, (byte[])b.Bytes.Clone())).ToArray();

    public static RareCandyMultiUseInspection Inspect(ulong? titleId, byte[]? exheader, byte[]? code, byte[]? bag, byte[]? crr, RareCandyMultiUseProfile? profile = null)
    {
        profile ??= OmegaRuby;
        var resources = new List<PatchResourceReport>();
        RareCandyMultiUseInspection Result(RareCandyMultiUseState state, PatchCapabilityLevel level, string summary, IReadOnlyList<PatchChangedRange>? changes = null) =>
            new(state, new PatchCapabilityReport(CapabilityId, level, resources.ToArray(), changes ?? [], [], summary));
        RareCandyMultiUseInspection Reject(string summary) => Result(RareCandyMultiUseState.NotAccredited, PatchCapabilityLevel.NotAccredited, summary);

        if (titleId is null)
            return Reject("Datos insuficientes: falta la cabecera NCCH para identificar el juego.");
        if (titleId == AlphaSapphireTitleId)
            return Reject("Zafiro Alfa todavía no está investigado; solo se admite Rubí Omega.");
        if (titleId != profile.TitleId)
            return Reject($"Juego {titleId:X16} sin perfil: solo se admite Rubí Omega (000400000011C400).");

        if (exheader is null)
            return Reject("Datos insuficientes: falta el ExHeader para comprobar la memoria que usa la rutina.");
        if (!TryCheckExHeader(exheader, profile, out string layoutError))
            return Reject("El ExHeader no coincide con la revisión acreditada: " + layoutError);
        resources.Add(new(StateMemory, "Contador y fase del selector", PatchResourceState.Original, StateAddress,
            $"El final real de .bss es 0x{profile.DataAddress + profile.DataSize + profile.BssSize:X6}; el estado ocupa 0x{StateAddress:X6}–0x{StateAddress + 11:X6} dentro de la misma página."));

        if (code is null)
            return Reject("Datos insuficientes: falta code.bin.");
        if (code.Length != profile.CodeLength)
            return Reject($"code.bin no está descomprimido o no es la revisión acreditada ({code.Length} bytes; se esperan {profile.CodeLength}).");
        foreach (var (address, role, hash) in profile.Callees)
        {
            if (!HashEquals(code.AsSpan((int)(address - CodeBase), 32), hash))
                return Reject($"La función nativa de 0x{address:X6} ({role}) no es la acreditada: otra revisión o una modificación.");
        }
        if (!HashEquals(MaskedDetourWindow(code), profile.DetourWindowHash))
            return Reject("El bucle de aprendizaje de movimientos no es el acreditado: otra revisión o una modificación.");

        if (bag is null)
            return Reject("Datos insuficientes: falta DllBag.cro.");
        if (bag.Length != profile.BagLength || bag.Length < 0x180 || !bag.AsSpan(0x80, 4).SequenceEqual("CRO0"u8))
            return Reject("DllBag.cro no tiene la cabecera ni el tamaño acreditados.");
        if (!HashEquals(MaskedBag(bag), profile.BagMaskedHash))
            return Reject("DllBag.cro tiene cambios fuera de los cuatro puntos del parche: otra revisión o una modificación.");
        byte[] rehashed = (byte[])bag.Clone();
        if (!CroSegmentExpander.TryUpdateHashes(rehashed, out string hashError) || !rehashed.AsSpan(0, 0x80).SequenceEqual(bag.AsSpan(0, 0x80)))
            return Reject("Los hashes internos de DllBag.cro no corresponden a su contenido. " + hashError);

        if (crr is null)
            return Reject("Datos insuficientes: falta .crr/static.crr.");
        if (!TryReadCrr(crr, out int crrStart, out int crrCount, out string crrError))
            return Reject(crrError);
        byte[] bagHeaderHash = SHA256.HashData(bag.AsSpan(0, 0x80));
        int crrMatches = CountCrr(crr, crrStart, crrCount, bagHeaderHash);
        resources.Add(new(CrrEntry, "Acreditar DllBag.cro ante el sistema", crrMatches == 1 ? PatchResourceState.Original : PatchResourceState.ExternalModification, null,
            crrMatches == 1 ? "La tabla CRR contiene exactamente una entrada para DllBag.cro." : $"La tabla CRR contiene {crrMatches} entradas para DllBag.cro; debe ser una."));
        if (crrMatches != 1)
            return Reject("static.crr no acredita DllBag.cro; usa Integridad CRO/CRR antes de instalar.");

        // Sites.
        var cave = CaveState(code);
        resources.Add(new(CodeCave, "Rutina de uso múltiple", cave.State, (uint)CaveStart, cave.Evidence));
        var detour = code.AsSpan(DetourOffset, 4);
        PatchResourceState detourState = detour.SequenceEqual(DetourOriginal) ? PatchResourceState.Original
            : detour.SequenceEqual(DetourInstalled()) ? PatchResourceState.OwnInstall : PatchResourceState.ExternalModification;
        resources.Add(new(CodeDetour, "Aprender los movimientos de los niveles saltados", detourState, (uint)DetourOffset,
            detourState switch
            {
                PatchResourceState.Original => "Instrucción original.",
                PatchResourceState.OwnInstall => "Desvío hacia la rutina instalado.",
                _ => "Instrucción desconocida: otra modificación ocupa este punto.",
            }));
        foreach (var hook in Hooks)
        {
            var site = bag.AsSpan(hook.Offset, 12);
            PatchResourceState state = site.SequenceEqual(hook.Original) ? PatchResourceState.Original
                : site.SequenceEqual(Trampoline(hook.Target)) ? PatchResourceState.OwnInstall : PatchResourceState.ExternalModification;
            resources.Add(new($"DllBag.cro · 0x{hook.Offset:X4}", hook.Role, state, (uint)hook.Offset,
                state switch
                {
                    PatchResourceState.Original => "Instrucciones originales.",
                    PatchResourceState.OwnInstall => $"Enlace a 0x{hook.Target:X6} instalado.",
                    _ => "Bytes desconocidos: otra modificación ocupa este punto.",
                }));
        }

        var sites = resources.Where(r => r.Resource != CrrEntry && r.Resource != StateMemory).ToArray();
        if (sites.Any(r => r.State == PatchResourceState.ExternalModification))
        {
            string detail = cave.Reference
                ? "Se detecta el parche de referencia instalado fuera del Randomizer. Restaura los archivos originales desde el historial antes de usar esta opción."
                : "Otra modificación ocupa un recurso que necesita el parche; no se sobrescribe.";
            return Result(RareCandyMultiUseState.Conflict, PatchCapabilityLevel.Conflict, detail);
        }
        bool allOriginal = sites.All(r => r.State == PatchResourceState.Original);
        bool allInstalled = sites.All(r => r.State == PatchResourceState.OwnInstall);
        if (!allOriginal && !allInstalled)
            return Result(RareCandyMultiUseState.Partial, PatchCapabilityLevel.NotAccredited,
                "Instalación parcial: algunos puntos tienen el parche y otros no. Restaura desde el historial; no se completa a ciegas.");

        var changes = new List<PatchChangedRange>();
        foreach (var block in Payload)
            changes.Add(new("code.bin", block.Address - CodeBase, block.Bytes.Length));
        changes.Add(new("code.bin", DetourOffset, 4));
        foreach (var hook in Hooks)
            changes.Add(new("DllBag.cro", (uint)hook.Offset, 12));
        changes.Add(new("DllBag.cro", 0x20, 32));
        changes.Add(new("static.crr", (uint)crrStart, crrCount * 32));
        return allInstalled
            ? Result(RareCandyMultiUseState.Installed, PatchCapabilityLevel.Verified, "Instalado. " + profile.Id + ".", changes)
            : Result(RareCandyMultiUseState.Original, PatchCapabilityLevel.Verified, "Sin instalar; todos los puntos tienen el código original. " + profile.Id + ".", changes);
    }

    /// <summary>Installs on copies. Throws unless every resource is original.</summary>
    public static RareCandyMultiUseFiles Install(ulong? titleId, byte[] exheader, byte[] code, byte[] bag, byte[] crr, RareCandyMultiUseProfile? profile = null)
    {
        var inspection = Inspect(titleId, exheader, code, bag, crr, profile);
        if (inspection.State != RareCandyMultiUseState.Original)
            throw new InvalidDataException("No se escribirá nada. " + inspection.Report.Summary);
        byte[] newCode = (byte[])code.Clone(), newBag = (byte[])bag.Clone();
        foreach (var block in Payload)
            block.Bytes.CopyTo(newCode, (int)(block.Address - CodeBase));
        DetourInstalled().CopyTo(newCode, DetourOffset);
        foreach (var hook in Hooks)
            Trampoline(hook.Target).CopyTo(newBag, hook.Offset);
        return Finish(titleId, exheader, bag, crr, newCode, newBag, RareCandyMultiUseState.Installed, profile);
    }

    /// <summary>Restores the original bytes on copies. Throws unless the patch is fully installed.</summary>
    public static RareCandyMultiUseFiles Remove(ulong? titleId, byte[] exheader, byte[] code, byte[] bag, byte[] crr, RareCandyMultiUseProfile? profile = null)
    {
        var inspection = Inspect(titleId, exheader, code, bag, crr, profile);
        if (inspection.State != RareCandyMultiUseState.Installed)
            throw new InvalidDataException("No se escribirá nada. " + inspection.Report.Summary);
        byte[] newCode = (byte[])code.Clone(), newBag = (byte[])bag.Clone();
        newCode.AsSpan(CaveStart, CaveEnd - CaveStart).Clear();
        DetourOriginal.CopyTo(newCode, DetourOffset);
        foreach (var hook in Hooks)
            hook.Original.CopyTo(newBag, hook.Offset);
        return Finish(titleId, exheader, bag, crr, newCode, newBag, RareCandyMultiUseState.Original, profile);
    }

    private static RareCandyMultiUseFiles Finish(ulong? titleId, byte[] exheader, byte[] oldBag, byte[] crr, byte[] newCode, byte[] newBag, RareCandyMultiUseState expected, RareCandyMultiUseProfile? profile)
    {
        if (!CroSegmentExpander.TryUpdateHashes(newBag, out string error))
            throw new InvalidDataException("No se pudieron recalcular los hashes de DllBag.cro: " + error);
        byte[] newCrr = ReplaceCrrEntry(crr, SHA256.HashData(oldBag.AsSpan(0, 0x80)), SHA256.HashData(newBag.AsSpan(0, 0x80)));
        var check = Inspect(titleId, exheader, newCode, newBag, newCrr, profile);
        if (check.State != expected)
            throw new InvalidDataException("La relectura del resultado no coincide: " + check.Report.Summary);
        return new(newCode, newBag, newCrr);
    }

    public static byte[] Trampoline(uint target)
    {
        byte[] data = [0x0F, 0xC0, 0xA0, 0xE1, 0x04, 0xF0, 0x1F, 0xE5, 0, 0, 0, 0];
        BitConverter.GetBytes(target).CopyTo(data, 8);
        return data;
    }

    public static byte[] DetourInstalled()
    {
        int delta = (int)(RareCandyMultiUsePayload.LearnDetour - (DetourOffset + CodeBase + 8)) >> 2;
        return BitConverter.GetBytes(0xEA000000u | ((uint)delta & 0x00FFFFFF));
    }

    private static (PatchResourceState State, string Evidence, bool Reference) CaveState(byte[] code)
    {
        var region = code.AsSpan(CaveStart, CaveEnd - CaveStart);
        if (!region.ContainsAnyExcept((byte)0))
            return (PatchResourceState.Original, "Relleno vacío (ceros) tras el final de .text.", false);
        bool ours = Payload.All(b => code.AsSpan((int)(b.Address - CodeBase), b.Bytes.Length).SequenceEqual(b.Bytes));
        byte[] masked = region.ToArray();
        foreach (var block in Payload)
            masked.AsSpan((int)(block.Address - CodeBase - CaveStart), block.Bytes.Length).Clear();
        bool rest = !masked.AsSpan().ContainsAnyExcept((byte)0);
        if (ours && rest)
            return (PatchResourceState.OwnInstall, "Rutina instalada; el resto del relleno sigue vacío.", false);
        // The external reference patch carries the same reachable code plus unreachable leftovers at 0x579610.
        bool reference = ours && masked.AsSpan(0x0C, 0x80).ContainsAnyExcept((byte)0);
        return (PatchResourceState.ExternalModification,
            reference ? "Contiene la rutina de referencia con restos no alcanzables: instalada fuera del Randomizer." : "El relleno contiene código o datos desconocidos.", reference);
    }

    private static bool TryCheckExHeader(byte[] exheader, RareCandyMultiUseProfile profile, out string error)
    {
        error = "";
        if (exheader.Length < 0x40) { error = "ExHeader incompleto."; return false; }
        uint U(int offset) => BitConverter.ToUInt32(exheader, offset);
        if (U(0x10) != CodeBase || U(0x14) != profile.TextPages || U(0x18) != profile.TextSize)
            error = $".text en 0x{U(0x10):X} ({U(0x14):X} páginas, {U(0x18):X} bytes).";
        else if (U(0x30) != profile.DataAddress || U(0x38) != profile.DataSize || U(0x3C) != profile.BssSize)
            error = $".data en 0x{U(0x30):X} ({U(0x38):X} bytes) y .bss de {U(0x3C):X} bytes.";
        return error.Length == 0;
    }

    public static byte[] MaskedDetourWindow(byte[] code)
    {
        byte[] window = code.AsSpan(DetourWindowStart, DetourWindowEnd - DetourWindowStart).ToArray();
        window.AsSpan(DetourOffset - DetourWindowStart, 4).Clear();
        return window;
    }

    public static byte[] MaskedBag(byte[] bag)
    {
        byte[] masked = (byte[])bag.Clone();
        masked.AsSpan(0, 0x80).Clear();
        foreach (var hook in Hooks)
            masked.AsSpan(hook.Offset, 12).Clear();
        return masked;
    }

    private static bool HashEquals(ReadOnlySpan<byte> data, string hash) =>
        Convert.ToHexString(SHA256.HashData(data)) == hash;

    private static bool TryReadCrr(byte[] crr, out int start, out int count, out string error)
    {
        start = count = 0; error = "";
        if (crr.Length < 0x358) { error = "static.crr incompleto."; return false; }
        start = BitConverter.ToInt32(crr, 0x350);
        count = BitConverter.ToInt32(crr, 0x354);
        if (count < 1 || start < 0x358 || (long)start + 32L * count > crr.Length)
        { error = "La tabla de hashes de static.crr está fuera de rango."; return false; }
        return true;
    }

    private static int CountCrr(byte[] crr, int start, int count, byte[] hash)
    {
        int matches = 0;
        for (int i = 0; i < count; i++)
            if (crr.AsSpan(start + 32 * i, 32).SequenceEqual(hash))
                matches++;
        return matches;
    }

    /// <summary>Replaces one CRR entry and keeps the list sorted, as the system expects.</summary>
    public static byte[] ReplaceCrrEntry(byte[] crr, byte[] oldHash, byte[] newHash)
    {
        if (!TryReadCrr(crr, out int start, out int count, out string error))
            throw new InvalidDataException(error);
        var hashes = Enumerable.Range(0, count).Select(i => Convert.ToHexString(crr, start + 32 * i, 32)).ToList();
        string old = Convert.ToHexString(oldHash);
        if (hashes.Count(h => h == old) != 1)
            throw new InvalidDataException("static.crr no contiene exactamente una entrada para DllBag.cro.");
        hashes[hashes.IndexOf(old)] = Convert.ToHexString(newHash);
        hashes.Sort(StringComparer.Ordinal);
        byte[] result = (byte[])crr.Clone();
        for (int i = 0; i < count; i++)
            Convert.FromHexString(hashes[i]).CopyTo(result, start + 32 * i);
        return result;
    }

    /// <summary>Title ID of an NCCH header (magic "NCCH" at 0x100), or null.</summary>
    public static ulong? ReadTitleId(byte[]? ncch) =>
        ncch is { Length: >= 0x120 } && Encoding.ASCII.GetString(ncch, 0x100, 4) == "NCCH" ? BitConverter.ToUInt64(ncch, 0x118) : null;
}
