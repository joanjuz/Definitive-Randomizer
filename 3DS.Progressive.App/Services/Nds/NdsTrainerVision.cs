using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>
/// What the editor can say about one tile in front of a trainer. The engine (trainer_encounter.c) sees the player at distance k when the player is in the
/// same row or column, 1 ≤ k ≤ range, and no tile between them collides (terrain, tile behaviour, another map object or a height change).
/// <list type="bullet">
/// <item><c>Open</c>: in range, and land_data has no collision there. Tile behaviour and heights are NOT read, so this is not a confirmation.</item>
/// <item><c>Unknown</c>: in range, without terrain data.</item>
/// <item><c>Blocked</c>: land_data marks a collision: the player cannot stand there and the trainer cannot see beyond it.</item>
/// <item><c>Occupied</c>: another NPC stands there in the stored data (it may move in the game): it hides the tiles beyond.</item>
/// <item><c>Shadowed</c>: in range, but behind a blocked or occupied tile.</item>
/// </list>
/// </summary>
internal enum NdsSightState { Open, Unknown, Blocked, Occupied, Shadowed }

internal sealed record NdsSightCell(int X, int Y, int Direction, int Distance, NdsSightState State);

/// <summary>A trainer's facing and sight as stored and as proposed; null sight when the NPC is not a trainer the engine checks.</summary>
internal sealed record NdsFacingState(PlatinumEvent Npc, int SavedX, int SavedY, int SavedDirection, int? SavedSight, int X, int Y, int Direction, int? Sight)
{
    internal bool Changed => X != SavedX || Y != SavedY || Direction != SavedDirection || Sight != SavedSight;
}

internal static class NdsTrainerVision
{
    internal const string GeometricNotice = "Alcance geométrico estimado: casillas en línea recta según la dirección y el alcance (regla del motor). Marca las colisiones de land_data y los NPC en su posición guardada; no evalúa el comportamiento de cada casilla, las alturas, NPC que se mueven ni scripts. No es una reproducción exacta del juego.";

    /// <summary>Directions the engine checks: all four for trainer type 2, else the facing direction.</summary>
    internal static IReadOnlyList<int> Directions(PlatinumEvent npc, int direction) =>
        PlatinumTrainerVision.SeesAllDirections(npc.Type) ? [0, 1, 2, 3] : direction is >= 0 and <= 3 ? [direction] : [];

    /// <summary>Only existing NPC that the move tool also accepts can be turned (not ground items, objects, warps or triggers).</summary>
    internal static bool CanTurn(PlatinumEvent e) => NdsNpcMovePlanner.IsDraggable(e);

    /// <summary>
    /// Tiles a trainer at (<paramref name="x"/>, <paramref name="y"/>) could see with this direction and range. Tiles outside the matrix are not listed.
    /// Pure: reads the model's terrain and markers, never the session.
    /// </summary>
    internal static IReadOnlyList<NdsSightCell> Cells(NdsMapCanvasModel model, PlatinumEvent npc, int x, int y, int direction, int sight)
    {
        var cells = new List<NdsSightCell>();
        if (sight <= 0 || !PlatinumTrainerVision.ChecksSight(npc.Type)) return cells;
        int width = model.Layout.TileWidth, height = model.Layout.TileHeight;
        var occupied = new HashSet<(int, int)>(model.Markers.Where(m => m.Kind == PlatinumEventKind.Npc && m.Index != npc.Index).Select(m => (m.X, m.Y)));
        foreach (int d in Directions(npc, direction))
        {
            var (dx, dy) = PlatinumTrainerVision.Step(d); bool hidden = false;
            for (int k = 1; k <= sight; k++)
            {
                int tx = x + dx * k, ty = y + dy * k;
                if (tx < 0 || ty < 0 || tx >= width || ty >= height) break;
                NdsSightState state;
                if (hidden) state = NdsSightState.Shadowed;
                else if (model.HasTerrain && model.TerrainStatus(tx, ty) == PlatinumTerrainStatus.Blocked) { state = NdsSightState.Blocked; hidden = true; }
                else if (occupied.Contains((tx, ty))) { state = NdsSightState.Occupied; hidden = true; }
                else state = model.HasTerrain && model.TerrainStatus(tx, ty) == PlatinumTerrainStatus.ClearOfCollision ? NdsSightState.Open : NdsSightState.Unknown;
                cells.Add(new(tx, ty, d, k, state));
            }
        }
        return cells;
    }

    /// <summary>Plain-language notes for the inspector: what kind of trainer this is, whether the facing lasts in the game, and what the overlay is.</summary>
    internal static IReadOnlyList<string> Notes(PlatinumEvent npc)
    {
        var notes = new List<string>();
        if (!PlatinumTrainerVision.ChecksSight(npc.Type)) notes.Add(npc.Type == 0 ? "No es un entrenador que detecte al jugador: solo se cambia hacia dónde mira." : $"Tipo {npc.Type}: el juego no comprueba su visión; solo se cambia hacia dónde mira.");
        else if (PlatinumTrainerVision.SeesAllDirections(npc.Type)) notes.Add("Entrenador que ve en las cuatro direcciones (tipo 2): la dirección no cambia lo que detecta; el alcance se aplica en las cuatro.");
        else if (PlatinumTrainerVision.TurnsByItself(npc.Type)) notes.Add($"Entrenador que gira solo (tipo {npc.Type}): detecta en la dirección a la que mira en cada momento; la guardada es la inicial.");
        else notes.Add("Entrenador: detecta al jugador en línea recta hacia donde mira, hasta su alcance.");
        if (PlatinumTrainerVision.ChecksSight(npc.Type))
            notes.Add(PlatinumTrainerVision.TrainerId(npc.Script) is int id ? $"Combate: entrenador {id} (script {npc.Script}, {(npc.Script >= 5000 ? "doble" : "individual")})." : $"Script {npc.Script}: no es un script de combate reconocido; el juego puede detectar al jugador y ejecutar otra cosa.");
        if (PlatinumTrainerVision.IsFixedLook(npc.Movement)) notes.Add($"Mira fijo (movimiento {npc.Movement}): al girarlo también cambia su movimiento a «mirar» en la dirección nueva (14–17), como hace el propio juego; así la orientación se mantiene.");
        else if (npc.Movement == 0) notes.Add("Sin movimiento propio: conserva la dirección guardada.");
        else notes.Add($"Su movimiento ({npc.Movement}) le hace girar o caminar: la dirección guardada es solo la inicial y puede no mantenerse durante el juego.");
        notes.Add("Un script puede girarlo durante el juego, y un entrenador ya derrotado no detecta.");
        return notes;
    }
}
