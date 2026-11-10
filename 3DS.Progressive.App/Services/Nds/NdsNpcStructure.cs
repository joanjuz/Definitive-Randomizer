using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>In-app clipboard entry: a typed copy of an existing NPC record with its provenance. Cutting never deletes the original.</summary>
internal sealed record NdsNpcClip(PlatinumEvent Source, bool Cut, int SourceMap, int SourceEventFile)
{
    internal string Origin => $"mapa {SourceMap}, NPC {Source.Index} (ID {Source.Id}), archivo de eventos {SourceEventFile}";
}

internal sealed record NdsAddDraft(int Id, int Graphics, int Movement, int Direction, int X, int Y);

/// <summary>Result of evaluating a new-NPC draft. Nothing is written; <c>Record</c> is set when it can be previewed.</summary>
internal sealed record NdsAddPlan(NdsMoveOutcome Outcome, byte[]? Record, IReadOnlyList<string> Reasons, NdsTileAssessment? Assessment)
{
    internal bool CanPropose => Outcome is NdsMoveOutcome.Ready or NdsMoveOutcome.NeedsWarning && Record is not null;
    internal string Summary => Outcome switch { NdsMoveOutcome.Ready => "Sin conflictos estáticos conocidos", NdsMoveOutcome.NeedsWarning => "Indeterminado: no se considera seguro", _ => "Rechazado" };
}

internal sealed record NdsDeleteAssessment(bool Allowed, IReadOnlyList<string> Reasons);

internal static class NdsNpcStructurePlanner
{
    /// <summary>Only normal, existing NPC records are templates: ground objects carry script references and are never copied.</summary>
    internal static bool IsTemplate(PlatinumEvent e) => e.Kind == PlatinumEventKind.Npc && e.Type == 0;

    /// <summary>A free local id: one above the largest used in the event file (maps in the game number their NPC consecutively).</summary>
    internal static int NextFreeId(PlatinumEvents data, int mapId)
    {
        var ids = data.Events(mapId).Where(e => e.Kind == PlatinumEventKind.Npc).Select(e => e.Id).ToList();
        return ids.Count == 0 ? 0 : ids.Max() + 1;
    }

    internal static NdsAddDraft DraftFrom(PlatinumEvents data, int mapId, PlatinumEvent template, int x, int y) =>
        new(NextFreeId(data, mapId), template.Graphics, template.Movement, template.Direction, x, y);

    internal static NdsAddPlan PlanAdd(PlatinumEvents data, NdsMapCanvasModel model, int mapId, PlatinumEvent template, NdsAddDraft draft)
    {
        if (!IsTemplate(template)) return new(NdsMoveOutcome.Rejected, null, ["la plantilla debe ser un NPC normal existente (no un NPC objeto de suelo, objeto, warp ni activador)"], null);
        byte[] record;
        try { record = PlatinumEvents.NewNpcRecord(template, draft.Id, draft.Graphics, draft.Movement, draft.Direction, draft.X, draft.Y); }
        catch (OverflowException) { return new(NdsMoveOutcome.Rejected, null, ["algún valor está fuera del rango de 16 bits que admite el registro"], null); }
        var problems = data.ValidateNewNpc(mapId, record);
        var a = model.Query.Assess(draft.X, draft.Y);
        var reasons = new List<string>(problems);
        if (a.Placement == NdsMarkerPlacement.OutsideMatrix) reasons.Add("fuera de la matriz de este mapa");
        if (problems.Count > 0 || a.Placement == NdsMarkerPlacement.OutsideMatrix) return new(NdsMoveOutcome.Rejected, null, reasons, a);
        if (a.Verdict == NdsTileVerdict.Blocked) { reasons.AddRange(a.Reasons); return new(NdsMoveOutcome.Rejected, null, reasons, a); }
        reasons.AddRange(a.Reasons);
        return new(a.Verdict == NdsTileVerdict.Allowed ? NdsMoveOutcome.Ready : NdsMoveOutcome.NeedsWarning, record, reasons, a);
    }

    /// <summary>
    /// Compatibility of pasting a clip into a map. A copy only needs the target event file to accept a new record (its ids and bounds are re-checked by PlanAdd);
    /// a cut is a MOVE of the original and therefore needs the SAME event file. Anything across different event files is rejected: scripts, flags and local ids
    /// belong to their own map and nothing here can prove that they carry over.
    /// </summary>
    internal static IReadOnlyList<string> PasteProblems(PlatinumEvents data, NdsNpcClip clip, int targetMap)
    {
        var problems = new List<string>(); var target = data.Map(targetMap);
        if (clip.Cut)
        {
            if (target.EventFile != clip.SourceEventFile)
                problems.Add($"cortar y pegar entre archivos de eventos distintos (origen {clip.SourceEventFile}, destino {target.EventFile}) exigiría eliminar el NPC y reasignar su ID y sus referencias de script, algo que no se puede demostrar; solo se traslada dentro del mismo archivo");
            else if (!data.Events(targetMap).Any(e => e.Kind == PlatinumEventKind.Npc && e.Index == clip.Source.Index && e.Id == clip.Source.Id))
                problems.Add("el NPC original ya no existe con ese índice e ID");
        }
        else if (target.EventFile != clip.SourceEventFile)
            problems.Add($"el destino usa otro archivo de eventos ({target.EventFile}) distinto del origen ({clip.SourceEventFile}): no se puede demostrar que su gráfico, movimiento y numeración sean compatibles, y los scripts no se trasladan");
        return problems;
    }

    /// <summary>Deleting is refused for every NPC of the original data. The reasons list what makes it unprovable; it never offers an override.</summary>
    internal static NdsDeleteAssessment AssessDelete(PlatinumEvents data, int mapId, PlatinumEvent npc, bool lastOperationAddedIt)
    {
        var reasons = new List<string>();
        if (lastOperationAddedIt) reasons.Add("este NPC lo añadió la última operación guardada: usa «Restaurar último cambio…» para quitarlo de forma exacta y reversible");
        reasons.Add($"no existe un analizador de scripts: no se puede demostrar que ningún script se refiera al NPC con ID {npc.Id} (los scripts localizan a los NPC por su ID local)");
        if (npc.Script != 0) reasons.Add($"el NPC tiene asociado el script {npc.Script}");
        var same = data.Events(mapId).Where(e => e.Kind == PlatinumEventKind.Npc && e.Id == npc.Id && e.Index != npc.Index).ToList();
        if (same.Count > 0) reasons.Add($"otros {same.Count} NPC comparten el ID {npc.Id}");
        int after = data.NpcCount(mapId) - npc.Index - 1;
        if (after > 0) reasons.Add($"eliminar el índice {npc.Index} reindexaría {after} NPC posteriores; el editor y los checkpoints identifican a los NPC por índice");
        int sharing = data.Maps.Count(m => m.EventFile == data.Map(mapId).EventFile);
        if (sharing > 1) reasons.Add($"el archivo de eventos lo comparten {sharing} cabeceras");
        reasons.Add("el juego original podría depender de él (aparición por flags, escenas, combates)");
        return new(false, reasons);
    }
}
