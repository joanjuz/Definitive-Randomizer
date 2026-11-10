using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

internal enum NdsTileVerdict { Allowed, Blocked, Indeterminate }

/// <summary>Answer for one tile. Never a guarantee of safety: every answer lists its provenance and what was NOT evaluated.</summary>
internal sealed record NdsTileAssessment(int X, int Y, NdsTileVerdict Verdict, PlatinumTerrainSample Terrain, NdsMarkerPlacement Placement,
    IReadOnlyList<string> Reasons, IReadOnlyList<string> Provenance, IReadOnlyList<string> Limitations, IReadOnlyList<PlatinumEvent> Occupants, IReadOnlyList<PlatinumEvent> Overlapping, bool EditorAccepts)
{
    internal string VerdictText => Verdict switch
    {
        NdsTileVerdict.Allowed => "Permitido según datos conocidos",
        NdsTileVerdict.Blocked => "Bloqueado según datos conocidos",
        _ => "Indeterminado",
    };
    internal string ToText()
    {
        var lines = new List<string> { $"{VerdictText} · X {X} · Y {Y}" };
        lines.AddRange(Reasons.Select(r => "• " + r));
        lines.Add("Procedencia: " + string.Join("; ", Provenance) + ".");
        lines.Add("No evaluado: " + string.Join("; ", Limitations) + ".");
        return string.Join("\n", lines);
    }
}

/// <summary>
/// Read-only spatial query for a map (no writes, no events moved). It combines the matrix layout, static terrain collision and the
/// events of the map. A future move/drag feature can consult it; "Allowed" is still not proof of playability.
/// </summary>
internal sealed class NdsSpatialQuery
{
    internal const string LimitationText = "alturas y BDHC; comportamiento del tile; puertas y escaleras; scripts y movimiento del NPC; estado dinámico del juego";
    private readonly PlatinumMapLayout _layout;
    private readonly PlatinumTerrain? _terrain;
    private readonly IReadOnlyList<PlatinumEvent> _events;
    private readonly (int Width, int Height)? _editorBounds;

    internal NdsSpatialQuery(PlatinumMapLayout layout, PlatinumTerrain? terrain, IEnumerable<PlatinumEvent> events, (int Width, int Height)? editorBounds = null)
    {
        _layout = layout; _terrain = terrain; _events = events.ToList(); _editorBounds = editorBounds;
    }

    internal bool HasTerrain => _terrain is not null;

    internal PlatinumTerrainStatus Status(int x, int y) => _terrain?.Status(_layout, x, y) ?? PlatinumTerrainStatus.Unknown;
    internal PlatinumTerrainSample Terrain(int x, int y) => _terrain?.Sample(_layout, x, y) ?? new(PlatinumTerrainStatus.Unknown, null, null, "sin datos de terreno para este proyecto");

    /// <param name="ignore">The event being considered for a move (its own tile does not count as occupied).</param>
    internal NdsTileAssessment Assess(int x, int y, (PlatinumEventKind Kind, int Index)? ignore = null)
    {
        var terrain = Terrain(x, y); var placement = NdsMapCanvasModel.Place(_layout, x, y);
        var reasons = new List<string>(); var provenance = new List<string> { "matriz de la cabecera " + _layout.MapId };
        if (_terrain is not null) provenance.Add("land_data (bit 15 = colisión, pokeplatinum)");
        bool accepts = _editorBounds is not { } b || (x >= 0 && y >= 0 && x < b.Width && y < b.Height);
        if (!accepts) reasons.Add("fuera de los límites que acepta el editor de eventos");
        var here = _events.Where(e => e.X == x && e.Y == y && (ignore is not { } skip || e.Kind != skip.Kind || e.Index != skip.Index)).ToList();
        var overlapping = _events.Where(e => e.Kind == PlatinumEventKind.Trigger && (ignore is not { } s || e.Kind != s.Kind || e.Index != s.Index)
            && x >= e.X && y >= e.Y && x < e.X + Math.Max(1, e.RangeX) && y < e.Y + Math.Max(1, e.RangeY)).ToList();
        var occupants = here.Where(e => e.Kind != PlatinumEventKind.Trigger).ToList();
        NdsTileVerdict verdict;
        if (terrain.Status == PlatinumTerrainStatus.Blocked) { verdict = NdsTileVerdict.Blocked; reasons.Add("colisión de terreno en los datos del juego"); }
        else if (terrain.Status == PlatinumTerrainStatus.Unknown) { verdict = NdsTileVerdict.Indeterminate; reasons.Add("sin datos de terreno: " + terrain.Reason); }
        else if (placement == NdsMarkerPlacement.OutsideOwnedBlocks) { verdict = NdsTileVerdict.Indeterminate; reasons.Add("sin colisión de terreno, pero la celda está asignada a otra cabecera: no hay evidencia de que pertenezca a este mapa"); }
        else if (occupants.Count > 0) { verdict = NdsTileVerdict.Indeterminate; reasons.Add("sin colisión de terreno, pero ocupada por " + string.Join(", ", occupants.Select(Name))); }
        else { verdict = NdsTileVerdict.Allowed; reasons.Add("sin colisión de terreno y sin otros eventos en la casilla"); if (placement == NdsMarkerPlacement.InsideContextualBlock) reasons.Add("la matriz no guarda cabeceras: la celda se toma como de esta cabecera por contexto"); }
        if (overlapping.Count > 0) reasons.Add("dentro del rango declarado de " + string.Join(", ", overlapping.Select(Name)));
        if (terrain.Behavior is { } behavior && behavior != 0) reasons.Add($"comportamiento de tile 0x{behavior:X2} (sin interpretar)");
        return new(x, y, verdict, terrain, placement, reasons, provenance, [LimitationText], occupants, overlapping, accepts);
    }
    private static string Name(PlatinumEvent e) => e.Kind switch
    {
        PlatinumEventKind.Npc when e.Type == 3 => $"NPC objeto de suelo {e.Index}",
        PlatinumEventKind.Npc => $"NPC {e.Index}",
        PlatinumEventKind.Object => $"objeto de fondo {e.Index}",
        PlatinumEventKind.Warp => $"warp {e.Index}",
        _ => $"activador {e.Index}",
    };
}
