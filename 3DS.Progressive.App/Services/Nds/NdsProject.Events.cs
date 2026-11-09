using System.IO;
using System.Text.Json;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

internal sealed partial class NdsProject
{
    private NdsEventSession? _events;
    internal bool SupportsEventProfile => Identity.Profile is { GameCode: "CPUE", Version: 1 };
    internal NdsEventSession Events => _events ??= SupportsEventProfile
        ? new NdsEventSession(_rom, PlatinumEvents.Open(_rom, _gen4!.Text(433)))
        : throw new InvalidDataException("Eventos fase 1: solo Pokémon Platino CPUE/1, con tablas acreditadas.");
    internal IReadOnlyList<string> EventHistory => _events?.Audit ?? Array.Empty<string>();
    internal int OperationCount => _history.Count + EventHistory.Count;
    internal void EventChanged() { Revision++; _gen4View = null; _gen5View = null; }

    private static NdsProject OpenEventProject(string path, bool allowRomhack)
    {
        NdsEventDocument document;
        try
        {
            document = NdsEventSession.ReadDocument(path);
            if (document.Format != NdsEventSession.Format || document.Version != 1 || document.Game != "CPUE" || document.Revision != 1 ||
                string.IsNullOrWhiteSpace(document.SourcePath) || !document.SourcePath.EndsWith(".nds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checkpoint de eventos no compatible con Platino CPUE/1.");
        }
        catch (JsonException ex) { throw new InvalidDataException("El checkpoint de eventos está truncado o no es JSON válido.", ex); }
        var project = Open(document.SourcePath, allowRomhack);
        try { project.Events.Load(document, path); project.EventChanged(); return project; }
        catch { project.Dispose(); throw; }
    }
}
