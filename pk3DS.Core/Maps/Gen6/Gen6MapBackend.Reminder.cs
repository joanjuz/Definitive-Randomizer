#nullable enable
using System;
using System.Linq;

namespace pk3DS.Core.Maps.Gen6;

/// <summary>The move reminder's record copied from its original zone, never from an added NPC.</summary>
public sealed record Gen6MoveReminderTemplate(int Zone, int Index, int Model, byte[] Record);

/// <summary>
/// X/Y candidate identified independently from the owner's zone 034/NPC 2/model 54 reference.
/// Admission of the executable, common script and original record belongs to the project profile;
/// these structural checks alone do not accredit a game or prove gameplay.
/// </summary>
public static class Gen6MoveReminder
{
    public const int Zone = 34, Index = 2, Model = 54, Script = 2261;

    public static Gen6MoveReminderTemplate? Find(Gen6Zone zone)
    {
        var e = zone.Entities;
        var r = e.Records.FirstOrDefault(r => r.Kind == MapEventKind.Npc && r.Index == Index);
        if (r.Size != Gen6ZoneEntities.NpcSize || zone.Area != 108 || zone.Matrix != 157 || zone.ParentMap != 76
            || e.U16(r, Gen6ZoneEdit.NpcId) != Index || e.U16(r, Gen6ZoneEdit.NpcModel) != Model
            || e.U16(r, Gen6ZoneEdit.NpcScript) != Script || e.U16(r, Gen6ZoneEdit.NpcMovement) != 0
            || e.U16(r, Gen6ZoneEdit.NpcFlag) != 0 || e.U16(r, Gen6ZoneEdit.NpcFacing) != 2
            || e.U16(r, Gen6ZoneEdit.NpcSight) != 0 || e.U16(r, Gen6ZoneEdit.NpcX) != 14
            || e.U16(r, Gen6ZoneEdit.NpcY) != 3 || e.F32(r, 0x2C) != 0) return null;
        return new(Zone, Index, Model, e.Bytes(r).ToArray());
    }
}

public sealed partial class Gen6MapBackend
{
    /// <summary>True only for X/Y with an explicitly accredited project profile.</summary>
    public bool CanAddReminder { get; }

    public Gen6MoveReminderTemplate? Reminder => CanAddReminder && _zones.TryGetValue(Gen6MoveReminder.Zone, out var zone)
        ? Gen6MoveReminder.Find(zone) : null;

    public string ReminderSummary => !CanAddReminder
        ? "Esta versión no tiene un perfil acreditado del recordador de movimientos de Pokémon X/Y; no se puede añadir."
        : Reminder is { } r
            ? $"Copia el recordador original de la zona {r.Zone:000} (NPC {r.Index}, modelo {r.Model}, script {Gen6MoveReminder.Script}). Reutiliza el servicio común del juego, con sus requisitos y coste originales. Validación jugable pendiente."
            : "La zona 034 no conserva el recordador original acreditado (NPC 2, modelo 54, script 2261); no se puede añadir.";

    /// <summary>A single consistent entry of model 54 from the game's own model lists.</summary>
    public byte[]? ReminderModelEntry => Reminder is { } r && _modelList is not null
        ? Gen6AreaModels.Entry(Enumerable.Range(0, _modelListCount).Select(_modelList), r.Model) : null;

    public bool? AreaLoadsReminder(int zone)
    {
        if (Reminder is not { } r || !_zones.TryGetValue(zone, out var z) || ModelList(z.Area) is not { } list) return null;
        return Gen6AreaModels.Models(list).Contains(r.Model);
    }

    public MapMutationCheck ValidateAddReminder(int zone, int x, int y, double facing)
    {
        if (Reminder is not { } reminder) return new(false, ReminderSummary);
        return ValidateAddService(zone, x, y, facing, reminder.Model, reminder.Zone, () => ReminderModelEntry, "recordador",
            "Validación jugable pendiente: comprueba que aparece, abre el servicio de recordar movimientos y conserva sus requisitos y coste originales.");
    }
}
