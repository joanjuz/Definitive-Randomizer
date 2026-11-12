using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public static partial class LevelCapPatch
{
    public const string CapabilityId = "usum.player-level-caps";

    /// <summary>
    /// Read-only capability report of Player Level Caps for these two files. Neither array is modified: every hook site
    /// is classified and, when both are in a known state, the managed installation is simulated on copies to confirm
    /// that it succeeds, what it would change and that the installed table reads back.
    /// A signature found somewhere else is evidence, never permission: it is reported and nothing is accredited.
    /// </summary>
    public static PatchCapabilityReport InspectCapability(
        byte[] battleCro,
        byte[] codeBin)
    {
        var battle = Classify(battleCro, "Battle.cro", "EXP en combate", BattleHook, BattleOriginal, 0, true, out LevelCapTable battleTable);
        var candy = Classify(codeBin, "code.bin", "Caramelo Raro", CandyHook, CandyOriginal, 2, false, out LevelCapTable candyTable);
        PatchResourceReport[] resources = [battle, candy];

        PatchCapabilityReport Report(PatchCapabilityLevel level, string summary, IReadOnlyList<PatchChangedRange> changes = null, IReadOnlyList<string> resized = null) =>
            new(CapabilityId, level, resources, changes ?? [], resized ?? [], summary);

        if (resources.Any(r => r.State is PatchResourceState.Missing or PatchResourceState.Truncated))
            return Report(PatchCapabilityLevel.NotAccredited, "Datos insuficientes: falta un archivo o está incompleto.");
        if (resources.Any(r => r.State == PatchResourceState.ExternalModification))
            return Report(PatchCapabilityLevel.Conflict, "Otra modificación ocupa un punto de enganche que este parche necesita.");
        if (resources.Any(r => r.State == PatchResourceState.Displaced))
            return Report(PatchCapabilityLevel.NotAccredited, "El código original no está donde se espera. Se encontró una secuencia igual en otro lugar, pero eso no acredita la compatibilidad.");

        // Every site is the original or an installation of this patch (complete, earlier or damaged): the managed
        // installer decides, simulated on copies with the table that is (or would be) installed.
        bool own = resources.Any(r => r.State is PatchResourceState.PartialInstall or PatchResourceState.OwnInstallEarlierFormat);
        LevelCapTable table = battleTable ?? candyTable ?? LevelCapTable.Default();
        LevelCapInstallResult result;
        try { result = InstallManaged(battleCro, codeBin, table); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException or OverflowException or System.IO.InvalidDataException)
        { return Report(PatchCapabilityLevel.NotAccredited, "La estructura de los archivos no permitió simular la instalación: " + e.Message); }

        if (!result.Success)
        {
            string why = string.Join(" | ", result.Sites.Where(s => !s.Success).Select(s => s.ToString()));
            return own
                ? Report(PatchCapabilityLevel.NotAccredited, "Hay una instalación anterior o incompleta de este parche que no se puede reemplazar con seguridad: " + why)
                : Report(PatchCapabilityLevel.Conflict, "Los puntos de enganche son correctos, pero la instalación simulada no encontró los recursos que necesita: " + why);
        }

        if (InspectBattle(result.BattleCro, out LevelCapTable installedBattle) != LevelCapCopyState.Applied ||
            InspectCandy(result.CodeBin, out LevelCapTable installedCandy) != LevelCapCopyState.Applied ||
            !installedBattle.ToBytes().SequenceEqual(table.ToBytes()) || !installedCandy.ToBytes().SequenceEqual(table.ToBytes()))
        {
            return Report(PatchCapabilityLevel.NotAccredited, "La instalación simulada no se pudo leer de vuelta.");
        }

        var changes = new List<PatchChangedRange>();
        var resized = new List<string>();
        Compare("Battle.cro", battleCro, result.BattleCro, changes, resized);
        Compare("code.bin", codeBin, result.CodeBin, changes, resized);
        bool agree = battleTable is not null && candyTable is not null && battleTable.ToBytes().SequenceEqual(candyTable.ToBytes());
        string state =
            own ? "Hay una instalación anterior o incompleta de este parche; instalarlo la reemplaza." :
            agree ? "El parche actual está instalado y sus dos copias coinciden." :
            battleTable is not null && candyTable is not null ? "Las dos copias del parche tienen tablas distintas; instalarlo las iguala." :
            battle.State == candy.State ? "Los dos puntos de enganche tienen el código original." :
            "El parche está en uno solo de los dos archivos; instalarlo completa el otro.";
        return resized.Count == 0
            ? Report(PatchCapabilityLevel.Verified, state + " La instalación simulada cabe en el espacio existente.", changes, resized)
            : Report(PatchCapabilityLevel.CompatibleByAdaptation, state + " La instalación simulada necesita ampliar el segmento de código, y la reubicación se verificó.", changes, resized);
    }

    private static PatchResourceReport Classify(
        byte[] bin,
        string name,
        string role,
        uint hook,
        uint[] original,
        int branchWord,
        bool battle,
        out LevelCapTable table)
    {
        table = null;
        if (bin is null)
            return new(name, role, PatchResourceState.Missing, null, "No se encontró el archivo.");
        if (bin.Length < hook + (original.Length * 4))
            return new(name, role, PatchResourceState.Truncated, null, $"El archivo termina antes del punto de enganche 0x{hook:X6}.");

        LevelCapCopyState copy = battle ? InspectBattle(bin, out table) : InspectCandy(bin, out table);
        switch (copy)
        {
            case LevelCapCopyState.Applied:
                return new(name, role, PatchResourceState.OwnInstall, hook, $"Rutina actual enganchada en 0x{hook:X6}; tabla de {table.Entries.Count} puntos.");
            case LevelCapCopyState.AppliedLegacy:
                return new(name, role, PatchResourceState.OwnInstallEarlierFormat, hook, "Rutina de un formato de tabla anterior.");
            case LevelCapCopyState.Stock:
                return FindRoutine(bin) is uint loose
                    ? new(name, role, PatchResourceState.PartialInstall, loose, $"El enganche es el original, pero hay una rutina del parche sin enganchar en 0x{loose:X6}.")
                    : new(name, role, PatchResourceState.Original, hook, $"Código original en 0x{hook:X6}.");
        }

        // Not the original and not a complete installation of ours.
        table = null;
        uint branchAt = hook + (uint)(branchWord * 4);
        if (TryReadWord(bin, branchAt, out uint word) && IsBranchLink(word) && TryDecodeBranchTarget(branchAt, word, out uint target))
        {
            uint start = battle || target < EntryCandy ? target : target - EntryCandy;
            return StartsLikeRoutine(bin, start)
                ? new(name, role, PatchResourceState.PartialInstall, start, $"El enganche salta a una rutina del parche en 0x{start:X6} que está incompleta o tiene una tabla no válida.")
                : new(name, role, PatchResourceState.ExternalModification, target, $"El enganche salta a código que no es de este parche (0x{target:X6}).");
        }

        var elsewhere = FindWords(bin, original).Where(at => at != hook).Take(2).ToArray();
        return elsewhere.Length == 1
            ? new(name, role, PatchResourceState.Displaced, elsewhere[0], $"El código original no está en 0x{hook:X6}; una secuencia igual aparece en 0x{elsewhere[0]:X6}.")
            : new(name, role, PatchResourceState.ExternalModification, hook, $"El punto de enganche 0x{hook:X6} no contiene el código original ni este parche.");
    }

    private static bool StartsLikeRoutine(byte[] bin, uint at)
    {
        for (int i = 0; i < 4; i++)
        {
            if (!TryReadWord(bin, at + (uint)(i * 4), out uint word) || word != Routine[i])
                return false;
        }

        return true;
    }

    private static uint? FindRoutine(byte[] bin)
    {
        foreach (uint at in FindWords(bin, Routine[..4]))
            return at;
        return null;
    }

    /// <summary>Word-aligned occurrences of a sequence of ARM words.</summary>
    private static IEnumerable<uint> FindWords(byte[] bin, uint[] words)
    {
        byte[] pattern = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
            BitConverter.GetBytes(words[i]).CopyTo(pattern, i * 4);

        int from = 0;
        while (from <= bin.Length - pattern.Length)
        {
            int at = bin.AsSpan(from).IndexOf(pattern);
            if (at < 0)
                yield break;
            at += from;
            if ((at & 3) == 0)
                yield return (uint)at;
            from = at + 1;
        }
    }

    private static void Compare(string name, byte[] before, byte[] after, List<PatchChangedRange> changes, List<string> resized)
    {
        if (before.Length != after.Length)
        {
            resized.Add($"{name}: {before.Length:N0} → {after.Length:N0} bytes");
            return;
        }

        int i = 0;
        while (i < before.Length)
        {
            if (before[i] == after[i]) { i++; continue; }
            int start = i;
            int last = i;
            // Runs separated by fewer than 16 equal bytes are one region.
            while (i < before.Length && i - last <= 16)
            {
                if (before[i] != after[i]) last = i;
                i++;
            }

            changes.Add(new PatchChangedRange(name, (uint)start, last - start + 1));
        }
    }
}
