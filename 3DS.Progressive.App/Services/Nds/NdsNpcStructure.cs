using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>In-app clipboard entry: a typed copy of an existing NPC record with its provenance. Cutting never deletes the original.</summary>
internal sealed record NdsNpcClip(PlatinumEvent Source, bool Cut, int SourceMap, int SourceEventFile)
{
    internal string Origin => $"mapa {SourceMap}, NPC {Source.Index} (ID {Source.Id}), archivo de eventos {SourceEventFile}";
}

/// <summary>Values of a new NPC. <c>RawHeight</c> overrides the template's raw height (+28) when the template comes from another map.</summary>
internal sealed record NdsAddDraft(int Id, int Graphics, int Movement, int Direction, int X, int Y, int? RawHeight = null, int Script = 0);

internal enum NdsBehaviourMode { Appearance, WithBehaviour, ShareScript }

/// <summary>One way of bringing an NPC's behaviour along, with whether it is possible here and why.</summary>
internal sealed record NdsBehaviourOption(NdsBehaviourMode Mode, bool Available, string Label, string Reason);

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
        try { record = PlatinumEvents.NewNpcRecord(template, draft.Id, draft.Graphics, draft.Movement, draft.Direction, draft.X, draft.Y, draft.RawHeight); System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), checked((ushort)draft.Script)); }
        catch (OverflowException) { return new(NdsMoveOutcome.Rejected, null, ["algún valor está fuera del rango de 16 bits que admite el registro"], null); }
        var problems = data.ValidateNewNpc(mapId, record, draft.Script == 0 ? null : draft.Script);
        var a = model.Query.Assess(draft.X, draft.Y);
        var reasons = new List<string>(problems);
        if (a.Placement == NdsMarkerPlacement.OutsideMatrix) reasons.Add("fuera de la matriz de este mapa");
        if (problems.Count > 0 || a.Placement == NdsMarkerPlacement.OutsideMatrix) return new(NdsMoveOutcome.Rejected, null, reasons, a);
        if (a.Verdict == NdsTileVerdict.Blocked) { reasons.AddRange(a.Reasons); return new(NdsMoveOutcome.Rejected, null, reasons, a); }
        reasons.AddRange(a.Reasons);
        return new(a.Verdict == NdsTileVerdict.Allowed ? NdsMoveOutcome.Ready : NdsMoveOutcome.NeedsWarning, record, reasons, a);
    }

    /// <summary>
    /// What makes a paste impossible. A COPY always creates a NEW NPC in the target event file (same file or another map: "basic copy", see
    /// <see cref="CrossMapNotes"/>); its id, values and bounds are re-checked by PlanAdd. A CUT is a MOVE of the original and therefore needs the SAME
    /// event file: moving across files would mean deleting an original NPC, which is never done.
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
        return problems;
    }

    /// <summary>
    /// The behaviour modes for pasting <paramref name="clip"/> into <paramref name="targetMap"/>. Only exact, recognised local scripts are offered:
    /// the Pokémon Center nurse (copied, re-targeted to the new NPC) and plain-message scripts (shared inside the same script file).
    /// </summary>
    internal static IReadOnlyList<NdsBehaviourOption> BehaviourOptions(PlatinumEvents data, NdsNpcClip clip, int targetMap)
    {
        var source = clip.Source; int script = source.Script; bool sameScriptFile = data.Map(targetMap).ScriptFile == data.Map(clip.SourceMap).ScriptFile;
        PlatinumScriptInfo? info = null;
        try { info = data.ClassifyScript(clip.SourceMap, script); } catch (System.IO.InvalidDataException) { }
        string why = script switch
        {
            0 or 0xFFFF => "el NPC original no tiene script",
            >= 3000 and < 7000 => $"el script {script} es de combate de entrenador: los entrenadores nuevos se crean en otra fase",
            >= 7000 and < 8800 => $"el script {script} es de objeto: no se copia",
            >= 2000 => $"el script {script} es común/global y depende de su contexto: no está acreditado para copiarse",
            _ when info is null => $"el script local {script} no existe en el archivo de scripts",
            _ when info.Pattern == PlatinumScriptPattern.PokecenterNurse && info.LocalIdReference != source.Id => $"el script local {script} se refiere a otro NPC (ID {info.LocalIdReference})",
            _ when info.Pattern == PlatinumScriptPattern.Unknown => $"el script local {script} no es un patrón reconocido; no se puede demostrar qué textos, flags, variables o NPC usa",
            _ => "",
        };
        bool targetScripts = data.HasReadableScriptFile(targetMap);
        bool nurse = info is { Pattern: PlatinumScriptPattern.PokecenterNurse } && info.LocalIdReference == source.Id && targetScripts;
        if (!targetScripts && why.Length == 0) why = "el archivo de scripts del mapa destino no tiene una tabla de entradas reconocible";
        bool message = info is { Pattern: PlatinumScriptPattern.PlainMessage };
        return
        [
            new(NdsBehaviourMode.Appearance, true, "Solo apariencia", "script 0 y flag 0: el NPC no hace nada al hablarle"),
            new(NdsBehaviourMode.WithBehaviour, nurse, "Con comportamiento (enfermera)", nurse
                ? $"se añade un script local nuevo al archivo de scripts del mapa: fija la variable 0x8007 con el ID local del NPC nuevo y llama al script común {PlatinumScriptFile.PokecenterNurseCommonScript} (cura al equipo); sin textos locales"
                : why.Length > 0 ? why : "solo está acreditado el script de la enfermera del Centro Pokémon"),
            new(NdsBehaviourMode.ShareScript, message && sameScriptFile, "Compartir script del mapa", message && sameScriptFile
                ? $"usa el mismo script local {script} (mensaje {info!.MessageId} del banco de textos del mapa); no se escribe ningún script"
                : message ? "el mensaje está en el banco de textos de otro mapa; copiar textos no está implementado" : why.Length > 0 ? why : "solo se comparten scripts de mensaje simple"),
        ];
    }

    internal static NdsBehaviourMode DefaultMode(IReadOnlyList<NdsBehaviourOption> options) =>
        options.FirstOrDefault(o => o.Mode == NdsBehaviourMode.WithBehaviour && o.Available) is not null ? NdsBehaviourMode.WithBehaviour
        : options.FirstOrDefault(o => o.Mode == NdsBehaviourMode.ShareScript && o.Available) is not null ? NdsBehaviourMode.ShareScript : NdsBehaviourMode.Appearance;

    /// <summary>The script number the new record uses and the binding the session must apply (null for appearance only).</summary>
    internal static (int Script, NdsScriptBinding? Binding) BindingFor(PlatinumEvents data, NdsNpcClip clip, int targetMap, NdsBehaviourMode mode, int newLocalId)
    {
        var map = data.Map(targetMap);
        return mode switch
        {
            NdsBehaviourMode.WithBehaviour => (data.ScriptEntryCount(targetMap) + 1, new NdsScriptBinding(NdsScriptBinding.Append, map.ScriptFile, data.ScriptEntryCount(targetMap) + 1, Convert.ToHexString(PlatinumScriptFile.NurseScript(newLocalId)))),
            NdsBehaviourMode.ShareScript => (clip.Source.Script, new NdsScriptBinding(NdsScriptBinding.Share, map.ScriptFile, clip.Source.Script, null)),
            _ => (0, null),
        };
    }

    internal static bool IsCrossMap(PlatinumEvents data, NdsNpcClip clip, int targetMap) => data.Map(targetMap).EventFile != clip.SourceEventFile;

    /// <summary>
    /// Copy between maps, mode A ("apariencia y configuración básica"): what is kept, what is reset and why. Never promises dialogue or behaviour.
    /// </summary>
    internal static IReadOnlyList<string> CrossMapNotes(NdsNpcClip clip) =>
    [
        $"copia básica desde {clip.Origin}: se crea un NPC NUEVO en este mapa; el original no cambia",
        $"se conservan: gráfico {clip.Source.Graphics}, movimiento {clip.Source.Movement}, dirección, rango de visión y rangos de movimiento",
        "se reinician: script 0 y flag 0 (los del origen pertenecen a otro mapa); el ID local es uno libre de este mapa",
        "no se trasladan diálogos, combates ni comportamiento de scripts del NPC original",
        "altura: la del NPC más cercano de este mapa (no se lee el BDHC)",
    ];

    /// <summary>Raw height (+28) for a NPC brought from another map: that of the nearest NPC of the target map, else the template's own.</summary>
    internal static (int RawHeight, string Source) TargetHeight(PlatinumEvents data, int targetMap, PlatinumEvent template, int x, int y)
    {
        var near = data.Events(targetMap).Where(e => e.Kind == PlatinumEventKind.Npc).OrderBy(e => Math.Max(Math.Abs(e.X - x), Math.Abs(e.Y - y))).ThenBy(e => e.Index).FirstOrDefault();
        return near is null ? (template.Z, "la de la plantilla (el mapa no tiene NPC)") : (near.Z, $"la del NPC {near.Index} de este mapa");
    }

    /// <summary>Draft for pasting a clip at (x, y): a free id, the clip's appearance and, across maps, a height taken from the target map.</summary>
    internal static NdsAddDraft PasteDraft(PlatinumEvents data, NdsNpcClip clip, int targetMap, int x, int y)
    {
        var draft = DraftFrom(data, targetMap, clip.Source, x, y);
        return IsCrossMap(data, clip, targetMap) ? draft with { RawHeight = TargetHeight(data, targetMap, clip.Source, x, y).RawHeight } : draft;
    }

    /// <summary>Deleting is refused for every NPC of the original data. The reasons list what makes it unprovable; it never offers an override.</summary>
    internal static NdsDeleteAssessment AssessDelete(PlatinumEvents data, int mapId, PlatinumEvent npc, bool addedByEditor)
    {
        if (addedByEditor) return new(true, ["este NPC lo añadió el editor: se puede eliminar con una propuesta reversible (los NPC añadidos posteriores bajan un índice)", "nada del juego original se refiere a él: se creó con script 0, flag 0 y un ID libre"]);
        var reasons = new List<string>();
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
