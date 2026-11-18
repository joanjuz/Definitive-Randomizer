#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Ctr;

namespace pk3DS.Core.Maps.Gen6;

public enum Gen6Title { XY, ORAS }

/// <summary>
/// Read-only Gen6 adapter: zones from the zone GARC (XY a/0/1/2, ORAS a/0/1/3),
/// matrices from mapMatrix and blocks from mapGR. Every file is read through
/// the delegates, so the adapter never touches the project on disk.
/// </summary>
public sealed class Gen6MapBackend : IMapEditorBackend, IMapResourceProvider, IMapEventProvider
{
    private readonly Func<int, byte[]> _zone, _matrix, _block;
    private readonly int _matrixCount, _blockCount;
    private readonly Dictionary<int, Gen6Zone> _zones = [];
    private readonly Dictionary<int, Gen6MapMatrix> _matrices = [];
    private readonly Dictionary<int, Gen6MapBlock?> _blocks = [];
    private readonly List<MapZoneEntry> _entries = [];
    private readonly object _gate = new();
    private readonly Func<int, byte[]>? _area;
    private readonly int _areaCount;
    private readonly Dictionary<int, Dictionary<string, H3dTexture>?> _areaTextures = [];
    private readonly Dictionary<(int Area, string Name), DecodedTexture?> _decoded = [];
    private readonly Dictionary<int, H3dModel?> _models = [];
    /// <summary>Why the last block could not be drawn, for diagnostics.</summary>
    public string? LastGraphicError { get; private set; }

    public Gen6Title Game { get; }
    public string Family => Game == Gen6Title.XY ? "gen6-xy" : "gen6-oras";
    public string Title => Game == Gen6Title.XY ? "Pokémon X / Y" : "Pokémon Rubí Omega / Zafiro Alfa";
    public IReadOnlyList<MapZoneEntry> Zones => _entries;
    public IMapResourceProvider Resources => this;
    public IMapEventProvider Events => this;
    public IMapMutationValidator? Mutations => null;
    public MapCapabilities Capabilities { get; }
    /// <summary>Zones that could not be read, with the reason; they are not listed.</summary>
    public IReadOnlyList<(int Zone, string Reason)> Skipped { get; }

    public Gen6MapBackend(Gen6Title game, int zoneCount, Func<int, byte[]> zone, int matrixCount, Func<int, byte[]> matrix,
        int blockCount, Func<int, byte[]> block, IReadOnlyList<string>? locationNames, int areaCount = 0, Func<int, byte[]>? area = null)
    {
        Game = game; _zone = zone; _matrix = matrix; _block = block; _matrixCount = matrixCount; _blockCount = blockCount;
        _area = area; _areaCount = area is null ? 0 : areaCount;
        Capabilities = new(_area is not null, true, false, _area is not null
            ? "Lectura: modelos y texturas reales de cada bloque (vista cenital aproximada), rejilla de permisos y eventos."
            : "Lectura: rejilla de permisos y eventos.");
        var skipped = new List<(int, string)>();
        for (int i = 0; i < zoneCount; i++)
        {
            Gen6Zone z;
            try { z = new Gen6Zone(zone(i)); }
            catch (MapFormatException ex) { skipped.Add((i, ex.Message)); continue; }
            if (z.Matrix >= matrixCount) { skipped.Add((i, $"matriz {z.Matrix} inexistente")); continue; }
            _zones[i] = z;
            string place = locationNames is not null && z.ParentMap < locationNames.Count && !string.IsNullOrWhiteSpace(locationNames[z.ParentMap])
                ? locationNames[z.ParentMap] : $"Ubicación {z.ParentMap}";
            int count = z.Entities.Records.Count;
            _entries.Add(new MapZoneEntry(i, $"{i:000} · {place}", z.Matrix, $"matriz {z.Matrix} · {count} eventos"));
        }
        Skipped = skipped;
    }

    public Gen6Zone Zone(int id) => _zones.TryGetValue(id, out var z) ? z : throw new ArgumentOutOfRangeException(nameof(id), $"La zona {id} no está disponible.");

    public MapLayout Layout(int zone)
    {
        int id = Zone(zone).Matrix;
        lock (_gate)
        {
            if (!_matrices.TryGetValue(id, out var m)) _matrices[id] = m = new Gen6MapMatrix(_matrix(id));
            return m.ToLayout();
        }
    }

    public MapCellGrid? Cells(int block) => Block(block)?.Cells;

    public MapBlockImage? Graphic(int zone, int block, int size)
    {
        if (_area is null || size <= 0) return null;
        int area = Zone(zone).Area;
        H3dModel? model;
        lock (_gate)
        {
            if (!_models.TryGetValue(block, out model))
            {
                var b = Block(block);
                try { model = b is null ? null : new H3dFile(b.Model).Models.FirstOrDefault(); }
                catch (MapFormatException ex) { model = null; LastGraphicError = $"Bloque {block}: {ex.Message}"; }
                _models[block] = model;
            }
        }
        if (model is null) return null;
        var render = Gen6TopDownRenderer.Render(model, name => Texture(area, name), size);
        return new MapBlockImage(render.Size, render.Pixels, render.Triangles, render.Untextured);
    }

    /// <summary>Texture of an area by name, decoded once.</summary>
    public DecodedTexture? Texture(int area, string name)
    {
        lock (_gate)
        {
            if (_decoded.TryGetValue((area, name), out var done)) return done;
            var set = AreaTextures(area);
            DecodedTexture? result = null;
            if (set is not null && set.TryGetValue(name, out var t))
            {
                try { result = new DecodedTexture(t.Width, t.Height, t.Decode()); }
                catch (MapFormatException ex) { LastGraphicError = $"Textura {name}: {ex.Message}"; }
            }
            _decoded[(area, name)] = result;
            return result;
        }
    }

    /// <summary>Every texture of every BCH part of the area pack ("AD"); the first one with a name wins.</summary>
    public IReadOnlyDictionary<string, H3dTexture>? AreaTextures(int area)
    {
        if (_area is null || area < 0 || area >= _areaCount) return null;
        lock (_gate)
        {
            if (_areaTextures.TryGetValue(area, out var cached)) return cached;
            Dictionary<string, H3dTexture>? set = null;
            try
            {
                var pack = Gen6Pack.Read(_area(area), "AD");
                set = new Dictionary<string, H3dTexture>(StringComparer.Ordinal);
                for (int i = 0; i < pack.Count; i++)
                {
                    var part = pack.Part(i);
                    if (part.Length < 4 || part[0] != 'B' || part[1] != 'C' || part[2] != 'H') continue;
                    foreach (var tex in new H3dFile(part).Textures) set.TryAdd(tex.Name, tex);
                }
            }
            catch (MapFormatException ex) { set = null; LastGraphicError = $"Área {area}: {ex.Message}"; }
            _areaTextures[area] = set;
            return set;
        }
    }

    public Gen6MapBlock? Block(int block)
    {
        if (block < 0 || block >= _blockCount) return null;
        lock (_gate)
        {
            if (_blocks.TryGetValue(block, out var b)) return b;
            try { b = new Gen6MapBlock(_block(block)); }
            catch (MapFormatException) { b = null; }
            _blocks[block] = b;
            return b;
        }
    }

    public IReadOnlyList<MapEventView> ZoneEvents(int zone)
    {
        var e = Zone(zone).Entities;
        var layout = Layout(zone);
        var events = e.Records.Select(r => Describe(e, r)).ToArray();
        bool Inside(MapEventView v) => v.X >= 0 && v.Y >= 0 && v.X < layout.Width * layout.BlockCells && v.Y < layout.Height * layout.BlockCells;
        // Some X/Y zones (Lumiose City among them) store props and NPCs with values
        // that are not cells. When most of them fall outside the matrix, none of the
        // zone's positions is trusted.
        var anchored = events.Where(v => v.Kind is MapEventKind.Npc or MapEventKind.Prop).ToArray();
        bool zoneUntrusted = anchored.Length > 0 && anchored.Count(v => !Inside(v)) * 2 > anchored.Length;
        return events.Select(v => zoneUntrusted
            ? v with { PositionNote = "Esta zona guarda posiciones que no son casillas; su formato aún no está verificado y no se dibujan." }
            : Inside(v) ? v : v with { PositionNote = "Fuera de la matriz de bloques de la zona." }).ToArray();
    }

    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Hex(int v) => "0x" + v.ToString("X4", CultureInfo.InvariantCulture);

    private MapEventView Describe(Gen6ZoneEntities e, Gen6EntityRecord r)
    {
        var p = new List<MapEventProperty>();
        switch (r.Kind)
        {
            case MapEventKind.Npc:
            {
                int x = e.U16(r, 0x28), y = e.U16(r, 0x2A);
                p.Add(new("ID", N(e.U16(r, 0x00))));
                p.Add(new("Modelo", N(e.U16(r, 0x02))));
                p.Add(new("Movimiento", N(e.U16(r, 0x04))));
                p.Add(new("Bandera de aparición", Hex(e.U16(r, 0x08))));
                p.Add(new("Script", N(e.U16(r, 0x0A))));
                p.Add(new("Orientación (valor)", N(e.U16(r, 0x0C)), Note: "Valores 0–7 en el juego; su correspondencia con las direcciones aún no está verificada."));
                p.Add(new("Alcance de visión", N(e.U16(r, 0x0E))));
                p.Add(new("Casilla", $"{N(x)}, {N(y)}"));
                p.Add(new("Ángulo", e.F32(r, 0x2C).ToString("0.##", CultureInfo.InvariantCulture) + "°"));
                return new(r.Kind, r.Index, x, y, 1, 1, null, $"NPC {r.Index} · modelo {e.U16(r, 0x02)}", p);
            }
            case MapEventKind.Prop:
            {
                int x = e.U16(r, 0x08), y = e.U16(r, 0x0A), w = e.S16(r, 0x0C), h = e.S16(r, 0x0E);
                p.Add(new("Script", N(e.U16(r, 0x00))));
                p.Add(new("Casilla", $"{N(x)}, {N(y)}"));
                p.Add(new("Tamaño", $"{N(w)} × {N(h)}"));
                return new(r.Kind, r.Index, x, y, Math.Max(1, w), Math.Max(1, h), null, $"Objeto {r.Index} · script {e.U16(r, 0x00)}", p);
            }
            case MapEventKind.Warp:
            {
                int rawX = e.S16(r, 0x08), rawZ = e.S16(r, 0x0A), rawY = e.S16(r, 0x0C), w = e.S16(r, 0x0E), h = e.S16(r, 0x10);
                int type = e.U16(r, 0x06);
                bool cell = type == 0 && rawX % 18 == 9 && rawY % 18 == 9;
                p.Add(new("Zona de destino", N(e.U16(r, 0x00))));
                p.Add(new("Warp de destino", N(e.U16(r, 0x02))));
                p.Add(new("Tipo de coordenadas", N(type), Note: cell ? null : "Coordenadas fuera de la rejilla de casillas: la posición mostrada es aproximada."));
                p.Add(new("Posición (1/18 casilla)", $"{N(rawX)}, {N(rawY)} · altura {N(rawZ)}"));
                p.Add(new("Tamaño", $"{N(w)} × {N(h)}"));
                return new(r.Kind, r.Index, Math.Floor(rawX / 18.0), Math.Floor(rawY / 18.0), Math.Max(1, w), Math.Max(1, h), null,
                    $"Warp {r.Index} → zona {e.U16(r, 0x00)}", p);
            }
            default:
            {
                int x = e.U16(r, 0x0C), y = e.U16(r, 0x0E), w = e.S16(r, 0x10), h = e.S16(r, 0x12);
                p.Add(new("Script", N(e.U16(r, 0x00))));
                p.Add(new("Constante", N(e.U16(r, 0x04))));
                p.Add(new("Casilla", $"{N(x)}, {N(y)}"));
                p.Add(new("Tamaño", $"{N(w)} × {N(h)}"));
                string what = r.Kind == MapEventKind.Trigger ? "Activador" : "Activador de zona";
                return new(r.Kind, r.Index, x, y, Math.Max(1, w), Math.Max(1, h), null, $"{what} {r.Index} · script {e.U16(r, 0x00)}", p);
            }
        }
    }

    /// <summary>Builds the adapter from a 3DS project configuration.</summary>
    public static Gen6MapBackend FromConfig(GameConfig config)
    {
        if (!config.XY && !config.ORAS) throw new NotSupportedException("El adaptador de mapas Gen6 solo admite X/Y y Rubí Omega/Zafiro Alfa.");
        var zones = config.GetlzGARCData("encdata");
        var matrices = config.GetlzGARCData("mapMatrix");
        var blocks = config.GetlzGARCData("mapGR");
        // Area data packs ("AD"): XY a/0/1/3, ORAS a/0/1/4, right after the zone GARC. Checked on real copies.
        var areas = new GARC.LazyGARC(System.IO.File.ReadAllBytes(System.IO.Path.Combine(config.RomFS, config.XY ? "a/0/1/3" : "a/0/1/4")));
        string[]? names = null;
        try { names = config.GetText(TextName.metlist_000000); } catch (Exception) { }
        return new Gen6MapBackend(config.XY ? Gen6Title.XY : Gen6Title.ORAS,
            zones.FileCount, i => zones[i], matrices.FileCount, i => matrices[i], blocks.FileCount, i => blocks[i], names, areas.FileCount, i => areas[i]);
    }
}
