using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using System.IO;
using ThreeDS.Progressive.Services;

namespace ThreeDS.Progressive;

/// <summary>
/// Connects the map view to the project: writes a previewed NPC move through
/// <see cref="Map3dsNpcMoveService"/> (history, undo, rebuild) and rebuilds the
/// adapter from the files on disk afterwards. Tests can pass their own delegates.
/// </summary>
internal sealed class Map3dsMover(Func<IMapEditorBackend, int, int, int, int, string> move, Func<IMapEditorBackend> reload,
    Func<int, bool>? canRestore = null, Func<int, string>? restore = null,
    Func<IMapEditorBackend, int, int, double, int?, string>? view = null,
    Func<IMapEditorBackend, int, int, int, double, string>? addNurse = null,
    Func<IMapEditorBackend, int, IReadOnlyCollection<(int X, int Y)>, int?, string>? paintCatch = null,
    Func<IMapEditorBackend, int, (int X, int Y), int, string>? addGrass = null, Func<int, bool>? canUndoGrass = null, Func<int, string>? undoGrass = null)
{
    /// <summary>Adds visible grass (a vanilla grass patch) centred on a cell of the area; it does not create encounters.</summary>
    internal string AddGrass(IMapEditorBackend backend, int zone, (int X, int Y) centre, int template) =>
        (addGrass ?? throw new InvalidOperationException("Este juego no admite hierba visible."))(backend, zone, centre, template);
    internal bool CanAddGrass => addGrass is not null;
    /// <summary>Whether the area's last visible grass can be undone from its restore point.</summary>
    internal bool CanUndoGrass(int zone) => canUndoGrass?.Invoke(zone) == true;
    internal string UndoGrass(int zone) => (undoGrass ?? throw new InvalidOperationException("Este juego no admite hierba visible."))(zone);
    /// <summary>Turns plain-ground cells of a zone into cells with encounters, copying the table of <paramref name="source"/> when the zone has none.</summary>
    internal string PaintCatch(IMapEditorBackend backend, int zone, IReadOnlyCollection<(int X, int Y)> cells, int? source) =>
        (paintCatch ?? throw new InvalidOperationException("Este juego no admite zonas de captura."))(backend, zone, cells, source);
    internal bool CanPaintCatch => paintCatch is not null;
    /// <summary>Adds a Pokémon Center nurse on cell (x, y) of the zone, looking towards <paramref name="facing"/> (compass degrees).</summary>
    internal string AddNurse(IMapEditorBackend backend, int zone, int x, int y, double facing) =>
        (addNurse ?? throw new InvalidOperationException("Este juego no admite añadir enfermeras."))(backend, zone, x, y, facing);
    internal bool CanAddNurse => addNurse is not null;
    /// <summary>Writes a previewed facing (compass degrees) and, where the game has one, sight range.</summary>
    internal string SetView(IMapEditorBackend backend, int zone, int npc, double facing, int? sight) =>
        (view ?? throw new InvalidOperationException("Este juego no escribe la orientación."))(backend, zone, npc, facing, sight);
    internal bool CanSetView => view is not null;
    internal string Move(IMapEditorBackend backend, int zone, int npc, int x, int y) => move(backend, zone, npc, x, y);
    internal IMapEditorBackend Reload() => reload();
    /// <summary>Whether the zone/area can go back exactly to its state before the last move made here.</summary>
    internal bool CanRestore(int zone) => canRestore?.Invoke(zone) == true;
    internal string Restore(int zone) => restore?.Invoke(zone) ?? throw new InvalidOperationException("Este juego deshace los movimientos desde el historial.");

    private static bool Try(Func<bool> check)
    {
        try { return check(); } catch (Exception ex) when (ex is IOException or InvalidDataException) { return false; }
    }

    internal static Map3dsMover? For(GameProjectSession session)
    {
        var config = session.GameConfiguration;
        if (config.USUM)
        {
            var gen7 = new Map3dsGen7NpcMoveService(session);
            var gen7Catch = new Map3dsGen7CatchZoneService(session);
            var gen7Grass = new Map3dsGen7VisibleGrassService(session);
            bool CatchRestorable(int area) { try { return gen7Catch.CanRestore(area); } catch (Exception ex) when (ex is IOException or InvalidDataException) { return false; } }
            return new Map3dsMover((backend, area, index, x, y) =>
            {
                if (backend is not pk3DS.Core.Maps.Gen7.Gen7MapBackend b7) throw new InvalidOperationException("El adaptador no admite escritura.");
                var proposal = gen7.Preview(b7, area, index, x, y);
                gen7.Apply(proposal);
                return proposal.Action;
            }, () => { gen7.Refresh(); return pk3DS.Core.Maps.Gen7.Gen7MapBackend.FromConfig(config); },
            area => CatchRestorable(area) || (Try(() => gen7.CanRestore(area))),
            area =>
            {
                // The last change of the area decides which originals match: a catch zone (events and tables) or an NPC move.
                if (CatchRestorable(area)) { var c = gen7Catch.PreviewRestore(area); gen7Catch.Apply(c); return c.Action; }
                var p = gen7.PreviewRestore(area); gen7.Apply(p); return p.Action;
            },
            (backend, area, index, facing, _) =>
            {
                if (backend is not pk3DS.Core.Maps.Gen7.Gen7MapBackend b7) throw new InvalidOperationException("El adaptador no admite escritura.");
                var proposal = gen7.PreviewView(b7, area, index, facing);
                gen7.Apply(proposal);
                return proposal.Action;
            }, paintCatch: (backend, area, cells, source) =>
            {
                if (backend is not pk3DS.Core.Maps.Gen7.Gen7MapBackend b7) throw new InvalidOperationException("El adaptador no admite escritura.");
                var proposal = gen7Catch.Preview(b7, area, cells, source);
                gen7Catch.Apply(proposal);
                return proposal.Action;
            }, addGrass: (backend, area, centre, template) =>
            {
                if (backend is not pk3DS.Core.Maps.Gen7.Gen7MapBackend b7) throw new InvalidOperationException("El adaptador no admite escritura.");
                // The preview checks the block archive against what the editor read; the commit checks it did not change since.
                var proposal = gen7Grass.Preview(b7, area, centre, template);
                gen7Grass.Apply(proposal);
                return proposal.Action;
            }, canUndoGrass: area => Try(() => gen7Grass.LastUndoable(area) is not null), undoGrass: area => gen7Grass.Undo(area));
        }
        if (!config.XY && !config.ORAS) return null;
        var service = new Map3dsNpcMoveService(session);
        var catchZones = new Map3dsCatchZoneService(session);
        return new Map3dsMover((backend, zone, npc, x, y) =>
        {
            if (backend is not Gen6MapBackend gen6) throw new InvalidOperationException("El adaptador no admite escritura.");
            var proposal = service.Preview(gen6, zone, npc, x, y);
            service.Apply(proposal);
            return proposal.Action;
        }, () => { service.Refresh(); return Gen6MapBackend.FromConfig(config); }, view: (backend, zone, npc, facing, sight) =>
        {
            if (backend is not Gen6MapBackend gen6) throw new InvalidOperationException("El adaptador no admite escritura.");
            var proposal = service.PreviewView(gen6, zone, npc, facing, sight);
            service.Apply(proposal);
            return proposal.Action;
        }, addNurse: (backend, zone, x, y, facing) =>
        {
            if (backend is not Gen6MapBackend gen6) throw new InvalidOperationException("El adaptador no admite escritura.");
            var proposal = service.PreviewAddNurse(gen6, zone, x, y, facing);
            service.Apply(proposal);
            return proposal.Action;
        }, paintCatch: (backend, zone, cells, source) =>
        {
            if (backend is not Gen6MapBackend gen6) throw new InvalidOperationException("El adaptador no admite escritura.");
            var proposal = catchZones.Preview(gen6, zone, cells, source);
            catchZones.Apply(proposal);
            return proposal.Action;
        });
    }
}
