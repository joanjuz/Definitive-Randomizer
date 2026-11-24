using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

internal enum NdsMapTool { Select, MoveNpc }

/// <summary>Live evaluation of a tile while choosing where to place an NPC (ghost state + one-line text).</summary>
internal sealed record NdsPlacementEval(NdsMoveOutcome Outcome, string Text);

internal enum NdsMoveOutcome { NoChange, Rejected, NeedsWarning, Ready }

/// <summary>
/// Result of evaluating a proposed NPC position. Nothing here writes anything: accepting a plan only fills the existing
/// X/Y fields so that the regular NdsEventSession.Preview / checkpoint flow does the validation and the saving.
/// </summary>
internal sealed record NdsNpcMovePlan(PlatinumEvent Npc, int FromX, int FromY, int ToX, int ToY, NdsMoveOutcome Outcome, NdsTileAssessment? Assessment, IReadOnlyList<string> Reasons)
{
    internal bool CanPropose => Outcome is NdsMoveOutcome.Ready or NdsMoveOutcome.NeedsWarning;
    internal string Summary => Outcome switch
    {
        NdsMoveOutcome.Ready => "Sin conflictos estáticos conocidos",
        NdsMoveOutcome.NeedsWarning => "Indeterminado: no se considera seguro",
        NdsMoveOutcome.Rejected => "Rechazado",
        _ => "Sin cambios",
    };
    /// <summary>Short one-line text for live feedback while dragging.</summary>
    internal string Live => Outcome switch
    {
        NdsMoveOutcome.NoChange => $"Destino: la posición original (X {ToX}, Y {ToY})",
        _ => $"Destino X {ToX}, Y {ToY} · {Summary}" + (Reasons.Count > 0 ? " · " + Reasons[0] : ""),
    };
}

internal static class NdsNpcMovePlanner
{
    /// <summary>Existing NPC records (not ground-object NPCs), background objects, warps and triggers can be dragged.</summary>
    internal static bool IsDraggable(PlatinumEvent e) => e.EditLock is null && (e.Kind == PlatinumEventKind.Npc ? !e.IsGroundItem : true);

    internal static NdsNpcMovePlan Plan(NdsMapCanvasModel model, PlatinumEvent npc, int x, int y)
    {
        if (!IsDraggable(npc)) return new(npc, npc.X, npc.Y, x, y, NdsMoveOutcome.Rejected, null, ["este evento no se puede mover con el ratón"]);
        if (x == npc.X && y == npc.Y) return new(npc, npc.X, npc.Y, x, y, NdsMoveOutcome.NoChange, null, []);
        if (npc.Kind != PlatinumEventKind.Npc) return PlanEvent(model, npc, x, y);
        var a = model.Query.Assess(x, y, (npc.Kind, npc.Index));
        // Structural limits of the existing editor cannot be bypassed by any confirmation.
        var structural = new List<string>();
        if (a.Placement == NdsMarkerPlacement.OutsideMatrix) structural.Add("fuera de la matriz de este mapa");
        if (!a.EditorAccepts) structural.Add("fuera de los límites que acepta el editor de eventos (los más conservadores si varias cabeceras comparten el archivo)");
        if (structural.Count > 0) return new(npc, npc.X, npc.Y, x, y, NdsMoveOutcome.Rejected, a, structural);
        if (a.Verdict == NdsTileVerdict.Blocked) return new(npc, npc.X, npc.Y, x, y, NdsMoveOutcome.Rejected, a, a.Reasons);
        return new(npc, npc.X, npc.Y, x, y, a.Verdict == NdsTileVerdict.Allowed ? NdsMoveOutcome.Ready : NdsMoveOutcome.NeedsWarning, a, a.Reasons);
    }

    /// <summary>
    /// Background objects, warps and triggers: only the editor bounds (the whole area for a trigger) are structural. Terrain and occupancy rules of NPC do
    /// not apply (a warp usually sits on a door, a sign on a wall), and their own in-game rules are not known, so a valid tile is always a warning.
    /// </summary>
    private static NdsNpcMovePlan PlanEvent(NdsMapCanvasModel model, PlatinumEvent e, int x, int y)
    {
        int w = e.Kind == PlatinumEventKind.Trigger ? Math.Max(1, e.RangeX) : 1, h = e.Kind == PlatinumEventKind.Trigger ? Math.Max(1, e.RangeY) : 1;
        var corner = model.Query.Assess(x, y, (e.Kind, e.Index)); var far = model.Query.Assess(x + w - 1, y + h - 1, (e.Kind, e.Index));
        if (x < 0 || y < 0 || !corner.EditorAccepts || !far.EditorAccepts)
            return new(e, e.X, e.Y, x, y, NdsMoveOutcome.Rejected, corner, [w * h > 1 ? "el área del activador sale de los límites que acepta el editor" : "fuera de los límites que acepta el editor de eventos"]);
        string what = e.Kind switch { PlatinumEventKind.Warp => "un warp", PlatinumEventKind.Trigger => "un activador", _ => "un objeto de fondo" };
        var reasons = new List<string> { $"no se comprueba el terreno ni la ocupación para {what}: revisa la casilla en el mapa" };
        if (e.Kind == PlatinumEventKind.Warp) reasons.Add("el jugador entra por esta casilla y llega aquí desde el warp de origen");
        return new(e, e.X, e.Y, x, y, NdsMoveOutcome.NeedsWarning, corner, reasons);
    }
}

/// <summary>Pure drag state (no WPF): pixel → integer tile via the canvas model; the session data is never touched.</summary>
internal sealed class NdsNpcDragSession
{
    internal const double StartDistance = 4;
    private readonly NdsMapCanvasModel _model;
    private readonly double _startX, _startY;
    internal PlatinumEvent Npc { get; }
    internal bool Active { get; private set; }
    internal int TargetX { get; private set; }
    internal int TargetY { get; private set; }
    internal NdsNpcMovePlan Plan { get; private set; }

    internal NdsNpcDragSession(NdsMapCanvasModel model, PlatinumEvent npc, double pixelX, double pixelY)
    {
        _model = model; Npc = npc; _startX = pixelX; _startY = pixelY; TargetX = npc.X; TargetY = npc.Y;
        Plan = NdsNpcMovePlanner.Plan(model, npc, npc.X, npc.Y);
    }

    /// <summary>Returns true when the target tile (or activation) changed.</summary>
    internal bool Update(double pixelX, double pixelY)
    {
        bool activated = false;
        if (!Active)
        {
            if (Math.Abs(pixelX - _startX) < StartDistance && Math.Abs(pixelY - _startY) < StartDistance) return false;
            Active = true; activated = true;
        }
        var (x, y) = _model.TileAt(pixelX, pixelY);
        if (!activated && x == TargetX && y == TargetY) return false;
        TargetX = x; TargetY = y; Plan = NdsNpcMovePlanner.Plan(_model, Npc, x, y); return true;
    }

    /// <summary>The plan to present, or null when nothing was dragged or the NPC returned to its own tile.</summary>
    internal NdsNpcMovePlan? Complete() => Active && Plan.Outcome != NdsMoveOutcome.NoChange ? Plan : null;
}
