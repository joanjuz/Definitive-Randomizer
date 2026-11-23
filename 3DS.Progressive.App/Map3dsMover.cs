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
    Func<IMapEditorBackend, int, int, int, double, string>? addNurse = null)
{
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

    internal static Map3dsMover? For(GameProjectSession session)
    {
        var config = session.GameConfiguration;
        if (config.USUM)
        {
            var gen7 = new Map3dsGen7NpcMoveService(session);
            return new Map3dsMover((backend, area, index, x, y) =>
            {
                if (backend is not pk3DS.Core.Maps.Gen7.Gen7MapBackend b7) throw new InvalidOperationException("El adaptador no admite escritura.");
                var proposal = gen7.Preview(b7, area, index, x, y);
                gen7.Apply(proposal);
                return proposal.Action;
            }, () => { gen7.Refresh(); return pk3DS.Core.Maps.Gen7.Gen7MapBackend.FromConfig(config); },
            area => { try { return gen7.CanRestore(area); } catch (Exception ex) when (ex is IOException or InvalidDataException) { return false; } },
            area => { var p = gen7.PreviewRestore(area); gen7.Apply(p); return p.Action; },
            (backend, area, index, facing, _) =>
            {
                if (backend is not pk3DS.Core.Maps.Gen7.Gen7MapBackend b7) throw new InvalidOperationException("El adaptador no admite escritura.");
                var proposal = gen7.PreviewView(b7, area, index, facing);
                gen7.Apply(proposal);
                return proposal.Action;
            });
        }
        if (!config.XY && !config.ORAS) return null;
        var service = new Map3dsNpcMoveService(session);
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
        });
    }
}
