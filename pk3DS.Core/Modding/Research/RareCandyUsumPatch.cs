// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace pk3DS.Core.Modding.Research;

public sealed record RareCandyUsumFiles(byte[] Bag, byte[] Crr);

/// <summary>
/// Rare Candy multi-use for Ultra Sun / Ultra Moon. Using a Rare Candy from the Bag with more than one asks how many
/// (the shop quantity window of FieldBag.cro), then repeats the game's own single-candy flow: every level shows its
/// window, learns its moves (rejecting is allowed), consumes one candy and checks evolution. It stops on evolution, at
/// level 100, when the level does not rise (level caps) or when no candy is left.
/// <para>
/// Only Bag.cro and static.crr are written. Bag.cro is the same file in both games and imports by name, so one routine
/// serves both. The routine is recognised by anchors (what it depends on), not by a whole-file hash, so it coexists
/// with the nature mints and with other Bag.cro patches that leave those anchors and the mint anchors alone.
/// </para>
/// </summary>
public static class RareCandyUsumPatch
{
    public const string CapabilityId = "usum.rare-candy-multiuse";
    public const ulong UltraSunTitleId = 0x00040000001B5000, UltraMoonTitleId = 0x00040000001B5100;
    public const string ProfileId = "Ultra Sol / Ultra Luna · Bag.cro BD97393E (común a los dos juegos)";

    private const int TemplateSize = 0x440, MarkerSize = 0x10, BlockSize = TemplateSize + MarkerSize;
    private static readonly byte[] ActiveMarker = "DGCANDY1"u8.ToArray(), DisabledMarker = "DGCANDY0"u8.ToArray();

    /// <summary>Hook sites: original word and entry of the routine they lead to.</summary>
    public static IReadOnlyList<(int Offset, string Role, uint Original, int Entry, bool Link)> Hooks { get; } =
    [
        (0x9C68, "Usar sobre un Pokémon: abrir el selector", 0xEB0009A0, RareCandyUsumPayload.Entry, true),
        (0x6144, "Estados de la Bolsa: selector y encadenado", 0xE5940004, RareCandyUsumPayload.Select, false),
        (0xE9A0, "Fin de la subida de nivel: siguiente caramelo", 0xE5847004, RareCandyUsumPayload.Repeat, false),
    ];

    private static readonly (int Offset, int Length, string Hash)[] BagWindows =
    [
        (0x09C58, 0x24, "BEA84ED593F1882A8123F5230CCBCC3FDF73E236F7DC6B542276BEF0FA4DEB5F"),
        (0x06134, 0x24, "0211678CD71CE855DCFCE74B05396CC4F166926F758FF51654011172A6C8615B"),
        (0x0E990, 0x24, "30956CF4FDA7D685C1D4DFA31592F2C1A5F5B2B4701CD59A9C3230C322307589"),
        (0x0C2F0, 0x10, "8EB48A3ECFB6BB9AF258E0C6ED34D2C66097EC18D98B358A10552DC917666FDC"),
        (0x09FA8, 0x10, "185F5AA6DB6BA1650EA10DCAA17FFB5332EC2E839DB641F463FCF00E793CCE7F"),
        (0x0A5E4, 0x10, "6130B8E9EAD152AF552F37317EBBD9225141AC03D07A78A4374316D667374E7B"),
        (0x0A860, 0x10, "C970FBE5920CB8C187366932B4D11EADD3EF7B5C9C2676354E20F95C04388041"),
        (0x0A92C, 0x10, "F79E79AFF53F4064DF0A3C7FA8283FB5D7E2182C9246B4EC21304E4227719611"),
        (0x0A944, 0x10, "4526C33CCFF0A9D67A0424914F8A0C5C5B5DDEED162D96D4CA891B8950E6D317"),
        (0x04D60, 0x10, "912F8FF0D446BC6EE23C083C87D32B4F061F531064E7731AD24E8FEA4D1A1D69"),
        (0x06148, 0x10, "FCD9D4DCDE600E8E0A0F4D926392DCC06B68F5BDC4EB6540A9C8B1B982081457"),
        (0x066B4, 0x10, "D2E990831B1F904BCD126D9B093D3C054E7697182BDF09F87F40D698E50A3112"),
        (0x0E9A4, 0x10, "851347ED1181A66F8835CFD16A692FC3E6FB34DEB91C724EDD21DD952F499733"),
    ];

    private static readonly (int Stub, string Symbol)[] NamedStubs =
    [
        (0x968, "_ZNK8Savedata6MyItem10GetItemNumEt"),
        (0x6D8, "_ZNK8Savedata6MyItem5CheckEtt"),
        (0x350, "_ZNK3pml8pokepara9CoreParam8GetLevelEv"),
        (0x240, "_ZnwjPN4gfl24heap11CtrHeapBaseE"),
        (0x3A8, "_ZNK3app2ui6UIView10GetG2DUtilEv"),
        (0x4C0, "_ZN3app2ui6UIView10AddSubViewEPS1_"),
    ];

    private static readonly (int Stub, int FieldBagOffset)[] FieldBagStubs =
        [(0x9E8, 0x1DBC), (0x9F0, 0x1F34), (0x9F8, 0x1F74), (0xA00, 0x1BCC), (0xA08, 0x1DAC), (0xA18, 0x1E7C), (0xD70, 0x1F90)];

    private static readonly (int Offset, string Hash)[] FieldBagWindows =
    [
        (0x1BCC, "6ED86F7F69D0F88DD99F2298DE7BBA7A385D7B23827BF1BFF04832398270F1AF"),
        (0x1DAC, "19601D06742ACA840DE9562FA460619C9055AFA7F5D109327BE68A85C22EDD1D"),
        (0x1DBC, "454B2765E8F3A0584EE76CE420D615A7875ECC290E8AE5799CC49127E759E894"),
        (0x1E7C, "7182B264D746775200648D061855ADCE17EC1532F84B1AF5D5CE0EFB7CE18D41"),
        (0x1F34, "6BEB4466B09B969268E4E19F7C5B68EEBED467766718B78881F26FF30D271517"),
        (0x1F74, "699D10F64FEA4CEEDBA665AD26CBDDC6612B7240A6F9FDB14BB83AEA27CF31CD"),
        (0x1F90, "BB13627FCEF92213AF344B628DBA0B3C09902F79A367D180E55EFF23D04D17CC"),
    ];

    private static readonly byte[] StubCode = [0x04, 0xF0, 0x1F, 0xE5];

    /// <summary>Byte ranges another Bag.cro patch must leave untouched for this routine to keep working.</summary>
    public static IReadOnlyList<(int Start, int End)> Ranges { get; } =
        BagWindows.Select(w => (w.Offset, w.Offset + w.Length))
            .Concat(NamedStubs.Select(s => (s.Stub, s.Stub + 8)))
            .Concat(FieldBagStubs.Select(s => (s.Stub, s.Stub + 8))).ToArray();

    public static RareCandyMultiUseInspection Inspect(ulong? titleId, byte[]? bag, byte[]? fieldBag, byte[]? crr)
    {
        var resources = new List<PatchResourceReport>();
        RareCandyMultiUseInspection Result(RareCandyMultiUseState state, PatchCapabilityLevel level, string summary, IReadOnlyList<PatchChangedRange>? changes = null, IReadOnlyList<string>? resized = null) =>
            new(state, new PatchCapabilityReport(CapabilityId, level, resources.ToArray(), changes ?? [], resized ?? [], summary));
        RareCandyMultiUseInspection Reject(string summary) => Result(RareCandyMultiUseState.NotAccredited, PatchCapabilityLevel.NotAccredited, summary);

        if (titleId is null) return Reject("Datos insuficientes: falta la cabecera NCCH para identificar el juego.");
        if (titleId is not (UltraSunTitleId or UltraMoonTitleId)) return Reject($"Juego {titleId:X16} sin perfil USUM.");
        if (bag is null) return Reject("Datos insuficientes: falta Bag.cro.");
        if (bag.Length < 0x180 || !bag.AsSpan(0x80, 4).SequenceEqual("CRO0"u8)) return Reject("Bag.cro no tiene la cabecera CRO0.");
        if (!HashesConsistent(bag)) return Reject("Los hashes internos de Bag.cro no corresponden a su contenido.");
        if (!BagAnchorsIntact(bag, out string why)) return Reject(why);
        if (fieldBag is null) return Reject("Datos insuficientes: falta FieldBag.cro.");
        foreach (var (offset, hash) in FieldBagWindows)
            if (offset + 0x20 > fieldBag.Length || Convert.ToHexString(SHA256.HashData(fieldBag.AsSpan(offset, 0x20))) != hash)
                return Reject($"FieldBag.cro no tiene la ventana de cantidad acreditada (0x{offset:X}).");
        resources.Add(new("FieldBag.cro · ventana de cantidad", "Selector de la tienda, reutilizado", PatchResourceState.Original, null, "Funciones acreditadas por huella; no se modifica."));
        if (crr is null) return Reject("Datos insuficientes: falta .crr/static.crr.");
        if (!TryReadCrr(crr, out int crrStart, out int crrCount, out string crrError)) return Reject(crrError);
        int crrMatches = CountCrr(crr, crrStart, crrCount, SHA256.HashData(bag.AsSpan(0, 0x80)));
        if (crrMatches != 1) return Reject("static.crr no acredita Bag.cro; usa Integridad CRO/CRR antes de instalar.");
        resources.Add(new("static.crr · hash de Bag.cro", "Acreditar Bag.cro ante el sistema", PatchResourceState.Original, null, "Exactamente una entrada para Bag.cro."));

        int active = Find(bag, ActiveMarker), disabled = Find(bag, DisabledMarker);
        if (active == -2 || disabled == -2 || (active >= 0 && disabled >= 0))
            return Result(RareCandyMultiUseState.Conflict, PatchCapabilityLevel.Conflict, "Hay más de un bloque de esta función en Bag.cro.");
        int marker = Math.Max(active, disabled);
        int block = marker >= 0 ? marker - TemplateSize : -1;
        if (block >= 0 && (block < 0x180 || !bag.AsSpan(block, TemplateSize).SequenceEqual(Payload(block))))
            return Result(RareCandyMultiUseState.Conflict, PatchCapabilityLevel.Conflict, "El bloque de la rutina cambió externamente.");
        resources.Add(new("Bag.cro · rutina", "Selector y encadenado", block < 0 ? PatchResourceState.Original : PatchResourceState.OwnInstall,
            block < 0 ? null : (uint)block, block < 0 ? "Sin bloque; se añadirá ampliando el segmento de código." : active >= 0 ? "Rutina instalada." : "Rutina desactivada; se reutiliza al instalar."));

        foreach (var hook in Hooks)
        {
            uint word = BitConverter.ToUInt32(bag, hook.Offset);
            PatchResourceState state = word == hook.Original ? PatchResourceState.Original
                : block >= 0 && word == Branch(hook.Offset, block + hook.Entry, hook.Link) ? PatchResourceState.OwnInstall
                : PatchResourceState.ExternalModification;
            resources.Add(new($"Bag.cro · 0x{hook.Offset:X4}", hook.Role, state, (uint)hook.Offset,
                state switch { PatchResourceState.Original => "Instrucción original.", PatchResourceState.OwnInstall => "Enlace a la rutina instalado.", _ => "Instrucción desconocida: otra modificación ocupa este punto." }));
        }
        var sites = resources.Where(r => r.Resource.StartsWith("Bag.cro · 0x", StringComparison.Ordinal)).ToArray();
        if (sites.Any(r => r.State == PatchResourceState.ExternalModification))
            return Result(RareCandyMultiUseState.Conflict, PatchCapabilityLevel.Conflict, "Otra modificación ocupa un punto que necesita la función; no se sobrescribe.");
        bool allOriginal = sites.All(r => r.State == PatchResourceState.Original), allOwn = sites.All(r => r.State == PatchResourceState.OwnInstall);
        if (allOwn && active < 0 || allOriginal && active >= 0 || !allOriginal && !allOwn)
            return Result(RareCandyMultiUseState.Partial, PatchCapabilityLevel.NotAccredited, "Instalación parcial: restaura desde el historial; no se completa a ciegas.");
        var changes = Hooks.Select(h => new PatchChangedRange("Bag.cro", (uint)h.Offset, 4)).ToList();
        changes.Add(new("Bag.cro", 0, 0x80));
        changes.Add(new("static.crr", (uint)crrStart, crrCount * 32));
        if (block >= 0) changes.Add(new("Bag.cro", (uint)block, BlockSize));
        return allOwn
            ? Result(RareCandyMultiUseState.Installed, PatchCapabilityLevel.Verified, "Instalado. " + ProfileId + ".", changes)
            : Result(RareCandyMultiUseState.Original, block < 0 ? PatchCapabilityLevel.CompatibleByAdaptation : PatchCapabilityLevel.Verified,
                "Sin instalar; los tres puntos tienen el código original." + (block < 0 ? " La instalación amplía el segmento de código de Bag.cro." : "") + " " + ProfileId + ".",
                changes, block < 0 ? ["Bag.cro"] : []);
    }

    public static RareCandyUsumFiles Install(ulong? titleId, byte[] bag, byte[] fieldBag, byte[] crr)
    {
        var inspection = Inspect(titleId, bag, fieldBag, crr);
        if (inspection.State != RareCandyMultiUseState.Original) throw new InvalidDataException("No se escribirá nada. " + inspection.Report.Summary);
        byte[] image; int block;
        int disabled = Find(bag, DisabledMarker);
        if (disabled >= 0) { image = (byte[])bag.Clone(); block = disabled - TemplateSize; }
        else
        {
            byte[] blockBytes = new byte[BlockSize];
            if (!CroPatchSession.TryCreate(bag, out var session, out string error) ||
                !session.TryAllocateRelocatableCode(BlockSize, "USUM rare candy multi-use", out var grant, out error))
                throw new InvalidDataException("No se pudo ampliar Bag.cro: " + error);
            block = checked((int)grant.Offset);
            Payload(block).CopyTo(blockBytes, 0);
            if (!session.TryWriteCode(grant, blockBytes, out error) || !session.TryBuildImage(out var built, out _, out error))
                throw new InvalidDataException("No se pudo preparar Bag.cro: " + error);
            image = built;
        }
        ActiveMarker.CopyTo(image, block + TemplateSize);
        foreach (var hook in Hooks) BitConverter.GetBytes(Branch(hook.Offset, block + hook.Entry, hook.Link)).CopyTo(image, hook.Offset);
        return Finish(titleId, bag, image, fieldBag, crr, RareCandyMultiUseState.Installed);
    }

    /// <summary>Restores the three original instructions and disables the block (kept for a later reinstall).</summary>
    public static RareCandyUsumFiles Remove(ulong? titleId, byte[] bag, byte[] fieldBag, byte[] crr)
    {
        var inspection = Inspect(titleId, bag, fieldBag, crr);
        if (inspection.State != RareCandyMultiUseState.Installed) throw new InvalidDataException("No se escribirá nada. " + inspection.Report.Summary);
        byte[] image = (byte[])bag.Clone();
        int block = Find(bag, ActiveMarker) - TemplateSize;
        DisabledMarker.CopyTo(image, block + TemplateSize);
        foreach (var hook in Hooks) BitConverter.GetBytes(hook.Original).CopyTo(image, hook.Offset);
        return Finish(titleId, bag, image, fieldBag, crr, RareCandyMultiUseState.Original);
    }

    private static RareCandyUsumFiles Finish(ulong? titleId, byte[] oldBag, byte[] image, byte[] fieldBag, byte[] crr, RareCandyMultiUseState expected)
    {
        if (!CroSegmentExpander.TryUpdateHashes(image, out string error)) throw new InvalidDataException("No se pudieron recalcular los hashes de Bag.cro: " + error);
        byte[] newCrr = RareCandyMultiUsePatch.ReplaceCrrEntry(crr, SHA256.HashData(oldBag.AsSpan(0, 0x80)), SHA256.HashData(image.AsSpan(0, 0x80)));
        var check = Inspect(titleId, image, fieldBag, newCrr);
        if (check.State != expected) throw new InvalidDataException("La relectura del resultado no coincide: " + check.Report.Summary);
        return new(image, newCrr);
    }

    /// <summary>The routine as it is written at Bag.cro offset <paramref name="block"/>.</summary>
    public static byte[] RoutineAt(int block) => Payload(block);

    /// <summary>Template relocated to <paramref name="block"/>: branches leaving the template keep their Bag.cro target.</summary>
    internal static byte[] Payload(int block)
    {
        byte[] data = RareCandyUsumPayload.Create();
        int baseAddress = RareCandyUsumPayload.TemplateBase;
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

    private static uint Branch(int from, int target, bool link)
    {
        int delta = checked(target - from - 8);
        if ((delta & 3) != 0 || delta is < -0x2000000 or >= 0x2000000) throw new InvalidDataException("Salto ARM fuera de rango.");
        return (link ? 0xEB000000u : 0xEA000000u) | ((uint)(delta >> 2) & 0xFFFFFF);
    }

    /// <returns>Offset of the single occurrence, -1 if absent, -2 if repeated.</returns>
    private static int Find(byte[] bag, byte[] marker)
    {
        int first = bag.AsSpan().IndexOf(marker);
        if (first < 0) return -1;
        return bag.AsSpan(first + 1).IndexOf(marker) >= 0 ? -2 : first;
    }

    private static bool HashesConsistent(byte[] cro)
    {
        var copy = (byte[])cro.Clone();
        return CroSegmentExpander.TryUpdateHashes(copy, out _) && copy.AsSpan().SequenceEqual(cro);
    }

    private static bool BagAnchorsIntact(byte[] bag, out string reason)
    {
        reason = "";
        foreach (var (offset, length, hash) in BagWindows)
        {
            if (offset + length > bag.Length) { reason = "Bag.cro truncado."; return false; }
            byte[] window = bag.AsSpan(offset, length).ToArray();
            foreach (var hook in Hooks)
                if (hook.Offset >= offset && hook.Offset + 4 <= offset + length) window.AsSpan(hook.Offset - offset, 4).Clear();
            if (Convert.ToHexString(SHA256.HashData(window)) != hash)
            { reason = $"Bag.cro no coincide con la revisión acreditada o otra modificación cambió el código del que depende la función (0x{offset:X5})."; return false; }
        }
        Dictionary<int, string> named; Dictionary<int, (string Module, int Offset)> anonymous;
        try { named = UsumBagAnchors.NamedImportTargets(bag); anonymous = AnonymousImportTargets(bag); }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        { reason = "La tabla de importaciones de Bag.cro no se puede leer."; return false; }
        foreach (var (stub, symbol) in NamedStubs)
            if (!bag.AsSpan(stub, 4).SequenceEqual(StubCode) || !named.TryGetValue(stub + 4, out var name) || name != symbol)
            { reason = $"El stub 0x{stub:X} de Bag.cro ya no importa {symbol}."; return false; }
        foreach (var (stub, target) in FieldBagStubs)
            if (!bag.AsSpan(stub, 4).SequenceEqual(StubCode) || !anonymous.TryGetValue(stub + 4, out var import) || import.Module != "FieldBag" || import.Offset != target)
            { reason = $"El stub 0x{stub:X} de Bag.cro ya no apunta a FieldBag.cro 0x{target:X}."; return false; }
        return true;
    }

    /// <summary>File offset patched by each anonymous import → (module, offset inside that module's segment 0 file image).</summary>
    private static Dictionary<int, (string Module, int Offset)> AnonymousImportTargets(byte[] cro)
    {
        uint U(int o) => BitConverter.ToUInt32(cro, o);
        var segments = Enumerable.Range(0, (int)U(0xCC)).Select(i => (int)U((int)U(0xC8) + i * 12)).ToArray();
        var result = new Dictionary<int, (string, int)>();
        int modules = (int)U(0xF0), moduleCount = (int)U(0xF4);
        for (int m = 0; m < moduleCount; m++)
        {
            int entry = modules + m * 20, nameOffset = (int)U(entry);
            string module = Encoding.ASCII.GetString(cro, nameOffset, Array.IndexOf(cro, (byte)0, nameOffset) - nameOffset);
            int table = (int)U(entry + 12), count = (int)U(entry + 16);
            for (int i = 0; i < count; i++)
            {
                uint source = U(table + i * 8); int patch = (int)U(table + i * 8 + 4);
                // The imported module's segment 0 starts at 0x180 in its file image (code segment).
                int target = (source & 0xF) == 0 ? 0x180 + (int)(source >> 4) : -1;
                for (int guard = 0; guard < 4096; guard++, patch += 12)
                {
                    uint at = U(patch);
                    result[segments[at & 0xF] + (int)(at >> 4)] = (module, target);
                    if (cro[patch + 5] != 0) break;
                }
            }
        }
        return result;
    }

    private static bool TryReadCrr(byte[] crr, out int start, out int count, out string error)
    {
        start = count = 0; error = "";
        if (crr.Length < 0x358) { error = "static.crr incompleto."; return false; }
        start = BitConverter.ToInt32(crr, 0x350); count = BitConverter.ToInt32(crr, 0x354);
        if (count < 1 || start < 0x358 || (long)start + 32L * count > crr.Length) { error = "La tabla de hashes de static.crr está fuera de rango."; return false; }
        return true;
    }

    private static int CountCrr(byte[] crr, int start, int count, byte[] hash)
    {
        int matches = 0;
        for (int i = 0; i < count; i++) if (crr.AsSpan(start + 32 * i, 32).SequenceEqual(hash)) matches++;
        return matches;
    }
}
