using System.IO;
using System.Text.Json;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

internal sealed partial class NdsProject
{
    private NdsEventSession? _events;
    /// <summary>The measured map and event profile of this ROM (Gen 4: Platinum, Diamond, HeartGold; Gen 5: Black), or null: no other game or revision has one.</summary>
    internal INdsEventProfile? EventProfile => NdsEventProfiles.For(Identity.Profile.GameCode, Identity.Profile.Version);
    internal bool SupportsEventProfile => EventProfile is not null;
    /// <summary>Paths of the event and script archives in this game (each game names them its own way).</summary>
    private string EventArchivePath => (EventProfile ?? Gen4EventProfile.Platinum).EventPath;
    private string EventScriptPath => (EventProfile ?? Gen4EventProfile.Platinum).ScriptPath;
    internal NdsEventSession Events => _events ??= EventProfile switch
    {
        Gen4EventProfile p when _gen4 is not null => new NdsEventSession(_rom, PlatinumEvents.Open(_rom, _gen4.Text(p.LocationNamesText))),
        Gen5MapProfile p when _gen5 is not null => new NdsEventSession(_rom, Gen5ZoneEvents.Open(_rom, _gen5.Text(p.LocationNamesText))),
        _ => throw new InvalidDataException($"Eventos: solo {NdsEventProfiles.SupportedLabels}, con tablas acreditadas."),
    };
    /// <summary>Uses an event session built elsewhere instead of opening the one of the ROM. Only the checks need it: a synthetic ROM has no accredited event tables.</summary>
    internal void AdoptEventSession(NdsEventSession session) => _events = _events is null ? session : throw new InvalidOperationException("La sesión de eventos ya está abierta.");
    private PlatinumTerrain? _terrain; private string? _terrainNote; private bool _terrainRead;
    /// <summary>Read-only land_data terrain attributes for the canvas. Missing or unaccredited data never blocks the event editor.</summary>
    internal (PlatinumTerrain? Terrain, string? Note) LoadTerrain()
    {
        if (!_terrainRead)
        {
            try { _terrain = EventProfile is Gen5MapProfile ? Gen5MapGraphics.Open(_rom).Terrain() : PlatinumTerrain.Open(_rom); }
            catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException) { _terrainNote = ex.Message; }
            _terrainRead = true;
        }
        return (_terrain, _terrainNote);
    }
    private PlatinumLandProps? _props; private string? _propsNote; private bool _propsRead;
    /// <summary>Read-only map props (structure positions and model ids). Missing or unaccredited data never blocks the event editor.</summary>
    internal (PlatinumLandProps? Props, string? Note) LoadProps()
    {
        if (!_propsRead)
        {
            try { _props = EventProfile is Gen5MapProfile ? throw new InvalidDataException("Estructuras Gen5: los edificios se dibujan con sus modelos en la capa «Modelos/texturas» (solo lectura); esta capa de marcadores es de Gen4.") : PlatinumLandProps.Open(_rom); }
            catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException) { _propsNote = ex.Message; }
            _propsRead = true;
        }
        return (_props, _propsNote);
    }
    private INdsMapGraphics? _assets; private string? _assetsNote; private bool _assetsRead;
    /// <summary>Read-only map graphics (terrain and prop models, textures). Missing or unaccredited data never blocks the event editor.</summary>
    internal (INdsMapGraphics? Assets, string? Note) LoadMapAssets()
    {
        if (!_assetsRead)
        {
            try { _assets = EventProfile is Gen5MapProfile ? Gen5MapGraphics.Open(_rom) : PlatinumMapAssets.Open(_rom); }
            catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException or ArgumentException) { _assetsNote = ex.Message; }
            _assetsRead = true;
        }
        return (_assets, _assetsNote);
    }
    internal IReadOnlyList<string> EventHistory => _events?.Audit ?? Array.Empty<string>();
    internal int OperationCount => _history.Count + EventHistory.Count;
    internal void EventChanged() { Revision++; _gen4View = null; _gen5View = null; }

    private static NdsProject OpenEventProject(string path, bool allowRomhack)
    {
        NdsEventDocument document;
        try
        {
            document = NdsEventSession.ReadDocument(path);
            if (document.Format != NdsEventSession.Format || document.Version is not (1 or 2 or 3 or 4 or 5) || NdsEventProfiles.For(document.Game, document.Revision) is null ||
                string.IsNullOrWhiteSpace(document.SourcePath) || !document.SourcePath.EndsWith(".nds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Checkpoint de eventos no compatible: solo {NdsEventProfiles.SupportedLabels}.");
        }
        catch (JsonException ex) { throw new InvalidDataException("El checkpoint de eventos está truncado o no es JSON válido.", ex); }
        var project = Open(document.SourcePath, allowRomhack);
        try { project.Events.Load(document, path); project.EventChanged(); return project; }
        catch { project.Dispose(); throw; }
    }
}
