using System.IO;
using System.Text.Json;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

internal sealed partial class NdsProject
{
    private NdsEventSession? _events;
    /// <summary>The measured map and event profile of this ROM (Platinum CPUE/1, Diamond ADAE/5), or null: no other game or revision has one.</summary>
    internal Gen4EventProfile? EventProfile => Gen4EventProfile.For(Identity.Profile.GameCode, Identity.Profile.Version);
    internal bool SupportsEventProfile => EventProfile is not null;
    /// <summary>Paths of the event and script archives in this game (Platinum names them otherwise than Diamond).</summary>
    private string EventArchivePath => (EventProfile ?? Gen4EventProfile.Platinum).EventPath;
    private string EventScriptPath => (EventProfile ?? Gen4EventProfile.Platinum).ScriptPath;
    internal NdsEventSession Events => _events ??= EventProfile is { } profile
        ? new NdsEventSession(_rom, PlatinumEvents.Open(_rom, _gen4!.Text(profile.LocationNamesText)))
        : throw new InvalidDataException($"Eventos: solo {Gen4EventProfile.SupportedLabels}, con tablas acreditadas.");
    /// <summary>Uses an event session built elsewhere instead of opening the one of the ROM. Only the checks need it: a synthetic ROM has no accredited event tables.</summary>
    internal void AdoptEventSession(NdsEventSession session) => _events = _events is null ? session : throw new InvalidOperationException("La sesión de eventos ya está abierta.");
    private PlatinumTerrain? _terrain; private string? _terrainNote; private bool _terrainRead;
    /// <summary>Read-only land_data terrain attributes for the canvas. Missing or unaccredited data never blocks the event editor.</summary>
    internal (PlatinumTerrain? Terrain, string? Note) LoadTerrain()
    {
        if (!_terrainRead)
        {
            try { _terrain = PlatinumTerrain.Open(_rom); }
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
            try { _props = PlatinumLandProps.Open(_rom); }
            catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException) { _propsNote = ex.Message; }
            _propsRead = true;
        }
        return (_props, _propsNote);
    }
    private PlatinumMapAssets? _assets; private string? _assetsNote; private bool _assetsRead;
    /// <summary>Read-only map graphics (terrain and prop models, textures). Missing or unaccredited data never blocks the event editor.</summary>
    internal (PlatinumMapAssets? Assets, string? Note) LoadMapAssets()
    {
        if (!_assetsRead)
        {
            try { _assets = PlatinumMapAssets.Open(_rom); }
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
            if (document.Format != NdsEventSession.Format || document.Version is not (1 or 2 or 3 or 4 or 5) || Gen4EventProfile.For(document.Game, document.Revision) is null ||
                string.IsNullOrWhiteSpace(document.SourcePath) || !document.SourcePath.EndsWith(".nds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Checkpoint de eventos no compatible: solo {Gen4EventProfile.SupportedLabels}.");
        }
        catch (JsonException ex) { throw new InvalidDataException("El checkpoint de eventos está truncado o no es JSON válido.", ex); }
        var project = Open(document.SourcePath, allowRomhack);
        try { project.Events.Load(document, path); project.EventChanged(); return project; }
        catch { project.Dispose(); throw; }
    }
}
