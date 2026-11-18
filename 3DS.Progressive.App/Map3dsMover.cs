using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive.Services;

namespace ThreeDS.Progressive;

/// <summary>
/// Connects the map view to the project: writes a previewed NPC move through
/// <see cref="Map3dsNpcMoveService"/> (history, undo, rebuild) and rebuilds the
/// adapter from the files on disk afterwards. Tests can pass their own delegates.
/// </summary>
internal sealed class Map3dsMover(Func<IMapEditorBackend, int, int, int, int, string> move, Func<IMapEditorBackend> reload)
{
    internal string Move(IMapEditorBackend backend, int zone, int npc, int x, int y) => move(backend, zone, npc, x, y);
    internal IMapEditorBackend Reload() => reload();

    internal static Map3dsMover? For(GameProjectSession session)
    {
        var config = session.GameConfiguration;
        if (!config.XY && !config.ORAS) return null;
        var service = new Map3dsNpcMoveService(session);
        return new Map3dsMover((backend, zone, npc, x, y) =>
        {
            if (backend is not Gen6MapBackend gen6) throw new InvalidOperationException("El adaptador no admite escritura.");
            var proposal = service.Preview(gen6, zone, npc, x, y);
            service.Apply(proposal);
            return proposal.Action;
        }, () => { service.Refresh(); return Gen6MapBackend.FromConfig(config); });
    }
}
