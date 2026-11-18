#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Ctr;
using pk3DS.Core.Maps.Gen6;

namespace pk3DS.Core.Maps.Gen7;

/// <summary>
/// Read-only Gen7 adapter (Ultra Sun / Ultra Moon first). The field data is
/// grouped by world: eleven files per world in the encounter GARC. Slot 1 is
/// a "BG" pack whose part 0 holds the world textures; slot 2 ("TR") is the
/// block matrix: u32 width, u32 height, u32 block size (4200), two u32, then
/// 12-byte records whose first u16 is a block id. Block models live in the
/// map GARC (a/0/8/6 in USUM) at that id, in world coordinates; block (c, r)
/// spans [c·size, (c+1)·size) on X and Z. Static objects (slot 4, "AS") are placed
/// with the ED part 4 records; events come from the other ED parts (Gen7Events).
/// </summary>
public sealed class Gen7MapBackend : IMapEditorBackend, IMapResourceProvider, IMapEventProvider
{
    public const int SlotsPerWorld = 11;
    private readonly Func<int, byte[]> _world, _chunk;
    private readonly int _chunkCount;
    private readonly List<MapZoneEntry> _entries = [];
    private readonly Dictionary<int, Gen7World> _worlds = [];
    private readonly Dictionary<int, IReadOnlyList<H3dModel>> _models = [];
    private readonly Dictionary<int, Dictionary<string, DecodedTexture?>> _decoded = [];
    private readonly Dictionary<int, Dictionary<string, H3dTexture>> _textures = [];
    private readonly object _gate = new();
    private readonly Dictionary<int, (IReadOnlyDictionary<int, IReadOnlyList<H3dModel>> Models, IReadOnlyList<H3dTexture> Textures)> _statics = [];
    private readonly Dictionary<int, IReadOnlyList<H3dModel>> _placed = [];
    private readonly IReadOnlyDictionary<int, string> _areaNames;
    private readonly IReadOnlyDictionary<int, Gen7ZoneInfo> _zoneTable;
    /// <summary>Global zone → area and location name (empty when the tables are not available).</summary>
    public IReadOnlyDictionary<int, Gen7ZoneInfo> ZoneTable => _zoneTable;

    public string Family => "gen7-usum";
    public string Title { get; }
    public IReadOnlyList<MapZoneEntry> Zones => _entries;
    public IMapResourceProvider Resources => this;
    public IMapEventProvider Events => this;
    public IMapMutationValidator? Mutations => null;
    public MapCapabilities Capabilities { get; } = new(true, true, false,
        "Lectura: modelos y texturas reales de cada área vistos desde arriba y eventos (NPC, entrenadores, warps, interacciones, objetos recogibles y activadores). Sin edición.", GraphicsByDefault: true,
        DiagnosticLayer: "Colisión del suelo (diagnóstico)");
    private readonly Dictionary<int, H3dModel?> _collision = [];
    public IReadOnlyList<(int World, string Reason)> Skipped { get; }
    public string? LastGraphicError { get; private set; }

    public Gen7MapBackend(string title, int worldFiles, Func<int, byte[]> world, int chunkCount, Func<int, byte[]> chunk,
        IReadOnlyDictionary<int, string>? areaNames = null, IReadOnlyDictionary<int, Gen7ZoneInfo>? zoneTable = null)
    {
        Title = title; _world = world; _chunk = chunk; _chunkCount = chunkCount; _areaNames = areaNames ?? new Dictionary<int, string>();
        _zoneTable = zoneTable ?? new Dictionary<int, Gen7ZoneInfo>();
        var skipped = new List<(int, string)>();
        for (int w = 0; w < worldFiles / SlotsPerWorld; w++)
        {
            try
            {
                var tr = Gen6Pack.Read(world((w * SlotsPerWorld) + 2), "TR");
                var g = new Gen7World(tr.Part(0).ToArray());
                if (!g.Blocks.Any(b => b >= 0 && b < chunkCount)) { skipped.Add((w, "sin bloques")); continue; }
                _worlds[w] = g;
                string name = _areaNames.TryGetValue(w, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "Área sin zona";
                _entries.Add(new MapZoneEntry(w, $"{w:000} · {name}", w, $"{g.Width}×{g.Height} bloques"));
            }
            catch (MapFormatException ex) { skipped.Add((w, ex.Message)); }
        }
        Skipped = skipped;
    }

    public Gen7World World(int id) => _worlds.TryGetValue(id, out var w) ? w : throw new ArgumentOutOfRangeException(nameof(id), $"El mundo {id} no está disponible.");

    public MapLayout Layout(int zone)
    {
        var w = World(zone);
        // Layout cells are the world's matrix positions; the graphic of each is drawn from every model of the world.
        return new MapLayout(w.Width, w.Height, 40, Enumerable.Range(0, w.Width * w.Height).Select(i => w.Blocks[i] >= 0 ? i : -1).ToArray());
    }

    public MapCellGrid? Cells(int block) => null;
    /// <summary>Cell size of the editor grid: 40 cells per 4200-unit block (105 units).</summary>
    public const float UnitsPerCell = 105f;

    /// <summary>
    /// Events of the area from its ED pack, every zone of the area together. Positions are
    /// world units (block (c, r) spans [c·4200, (c+1)·4200)) converted to editor cells.
    /// Records whose type field does not match the expected layout make the whole kind unreadable.
    /// </summary>
    public IReadOnlyList<MapEventView> ZoneEvents(int zone)
    {
        var list = new List<MapEventView>();
        var ed = EventPack(zone);
        if (ed is null) return list;
        var w = World(zone);
        int counter = 0;
        foreach (var layout in Gen7Events.Layouts)
        {
            if (layout.Pack >= ed.Count || ed.Length(layout.Pack) == 0) continue;
            var zones = Gen7Events.Container(ed.Part(layout.Pack).ToArray());
            if (zones is null) continue;
            List<(int Zone, Gen7Record Record)> rows = [];
            try
            {
                for (int z = 0; z < zones.Count; z++)
                    foreach (var r in Gen7Events.Records(zones.Part(z).ToArray(), layout.Size, layout.Type)) rows.Add((z, r));
            }
            catch (MapFormatException ex) { LastGraphicError = $"{layout.Name}: {ex.Message}"; continue; }
            foreach (var (z, r) in rows)
            {
                var pos = r.Position;
                double cx = pos.X / UnitsPerCell, cy = pos.Z / UnitsPerCell;
                var props = new List<MapEventProperty>
                {
                    new("Tipo", layout.Name),
                    new("Zona local", z.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new("Posición (mundo)", $"{pos.X:0.#}, {pos.Z:0.#} · altura {pos.Y:0.#}"),
                };
                if (layout.Type != 0) props.Add(new("Evento", r.U32(44).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                if (layout.Model >= 0) props.Add(new("Modelo", r.U32(layout.Model).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                if (layout.Script >= 0) props.Add(new("Script", r.U32(layout.Script).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                string N(long v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (layout.Pack == Gen7Events.WarpPack)
                {
                    var warp = new Gen7Warp(zone, z, r.Index, pos, (int)r.U32(44), r.U16(52), r.U16(54), (int)r.U32(48), (int)r.U32(56), new Vector3(r.F(60), r.F(64), r.F(68)));
                    string ZoneName(int id) => _zoneTable.TryGetValue(id, out var info) ? $"{info.Name} (zona {id}, área {info.Area})" : $"zona {id}";
                    props.Add(new("Zona del warp", ZoneName(warp.Zone)));
                    var link = Resolve(warp);
                    props.Add(new("Destino", ZoneName(warp.DestinationZone) + $" · warp con evento {N(warp.DestinationEvent)}",
                        Note: link?.Target is null ? "No se encontró el warp de destino en las tablas leídas." : null));
                    if (link?.Target is { } target)
                    {
                        props.Add(new("Llegada en el destino", $"{target.Arrival.X:0.#}, {target.Arrival.Z:0.#} · altura {target.Arrival.Y:0.#}",
                            Note: "Punto de aparición del warp de destino (campo +0x3C), en coordenadas de su área."));
                        props.Add(new("Retorno", link.ReturnVerified ? "Verificado: el warp de destino vuelve a este." : "No verificado: el warp de destino apunta a otro lugar."));
                    }
                    props.Add(new("Aparición al llegar aquí", $"{warp.Arrival.X:0.#}, {warp.Arrival.Z:0.#} · altura {warp.Arrival.Y:0.#}"));
                    props.Add(new("Transición", N(warp.Transition), Note: "Tipo de entrada (0–13); sus nombres no se han verificado."));
                }
                else if (layout.Pack == Gen7Events.TriggerPack)
                {
                    props.Add(new("Campos +0x20 / +0x24 / +0x28", $"{N(r.U32(32))} / {N(r.U32(36))} / {N(r.U32(40))}", Note: "Sin verificar (posibles versión, banderas y condición)."));
                    props.Add(new("Forma de activación", r.U32(56) == 0 ? "sin forma" : $"datos en +0x{r.U32(56):X} del archivo de zona", Note: "Formas sin interpretar todavía."));
                }
                bool inside = cx >= 0 && cy >= 0 && cx < w.Width * 40 && cy < w.Height * 40 && float.IsFinite(pos.X) && float.IsFinite(pos.Z);
                list.Add(new MapEventView(layout.Kind, counter++, Math.Floor(cx), Math.Floor(cy), 1, 1, null,
                    $"{layout.Name} {r.Index} · zona {z}", props, inside ? null : "Fuera de la matriz del área."));
            }
        }
        return list;
    }

    /// <summary>Terrain models of the area (distinct block ids of its matrix) plus its placed static objects.</summary>
    public IReadOnlyList<H3dModel> Models(int world)
    {
        var list = new List<H3dModel>();
        foreach (int id in World(world).Blocks.Where(b => b >= 0 && b < _chunkCount).Distinct()) list.AddRange(ModelsOfBlock(id));
        list.AddRange(PlacedStatics(world));
        list.AddRange(AreaScenery(world));
        return list;
    }

    /// <summary>
    /// Static objects of the area (slot 4, "AS"): one "SM" pack each, whose part 0 starts with the
    /// u16 object id and part 1 is a GFL2 model pack.
    /// </summary>
    public (IReadOnlyDictionary<int, IReadOnlyList<H3dModel>> Models, IReadOnlyList<H3dTexture> Textures) Statics(int world)
    {
        lock (_gate)
        {
            if (_statics.TryGetValue(world, out var cached)) return cached;
            var models = new Dictionary<int, IReadOnlyList<H3dModel>>();
            var textures = new List<H3dTexture>();
            try
            {
                var data = _world((world * SlotsPerWorld) + 4);
                if (data.Length >= 8 && data[0] == 'A' && data[1] == 'S')
                {
                    var pack = Gen6Pack.Read(data, "AS");
                    for (int i = 0; i < pack.Count; i++)
                    {
                        var part = pack.Part(i).ToArray();
                        if (part.Length < 8 || part[0] != 'S' || part[1] != 'M') continue;
                        var sm = Gen6Pack.Read(part, "SM");
                        if (sm.Count < 2 || sm.Length(0) < 2) continue;
                        int id = BinaryPrimitives.ReadUInt16LittleEndian(sm.Part(0));
                        var body = sm.Part(1);
                        if (body.Length < 24 || BinaryPrimitives.ReadUInt32LittleEndian(body) != Gfl2File.ModelPackMagic) continue;
                        var (m, tx) = Gfl2File.ReadModelPack(body);
                        models.TryAdd(id, m);
                        textures.AddRange(tx);
                    }
                }
            }
            catch (MapFormatException ex) { LastGraphicError = $"Objetos estáticos del área {world}: {ex.Message}"; }
            return _statics[world] = (models, textures);
        }
    }

    /// <summary>Static placements of the area: ED part 4 ("ES"), one file per zone.</summary>
    public IReadOnlyList<Gen7StaticPlacement> StaticPlacements(int world)
    {
        var list = new List<Gen7StaticPlacement>();
        var ed = EventPack(world);
        if (ed is null || ed.Count <= Gen7Events.StaticPack) return list;
        var zones = Gen7Events.Container(ed.Part(Gen7Events.StaticPack).ToArray());
        if (zones is null) return list;
        for (int z = 0; z < zones.Count; z++)
            foreach (var r in Gen7Events.Records(zones.Part(z).ToArray(), Gen7Events.StaticSize, Gen7Events.StaticType))
                list.Add(new Gen7StaticPlacement(z, r.Index, r.Position, new Quaternion(r.F(16), r.F(20), r.F(24), r.F(28)), r.U16(48)));
        return list;
    }

    private IReadOnlyList<H3dModel> PlacedStatics(int world)
    {
        lock (_gate) { if (_placed.TryGetValue(world, out var done)) return done; }
        var (models, _) = Statics(world);
        var list = new List<H3dModel>();
        try
        {
            foreach (var p in StaticPlacements(world))
            {
                if (!models.TryGetValue(p.Model, out var ms)) continue;
                var matrix = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(p.Rotation)) * Matrix4x4.CreateTranslation(p.Position);
                foreach (var m in ms) list.Add(m with { World = matrix });
            }
        }
        catch (MapFormatException ex) { LastGraphicError = $"Colocaciones del área {world}: {ex.Message}"; }
        lock (_gate) _placed[world] = list;
        return list;
    }

    private readonly Dictionary<int, IReadOnlyList<H3dModel>> _scenery = [];

    /// <summary>
    /// Area-wide scenery: the models of part 4 of the area's BG pack (a pack with one model per
    /// entry). It holds the ground of the large outdoor areas (meshes such as ground_01_01_*),
    /// the sea and distant rocks; confirmed against USUM-Studio's "Distant scenery" on Route 1.
    /// </summary>
    public IReadOnlyList<H3dModel> AreaScenery(int world)
    {
        lock (_gate)
        {
            if (_scenery.TryGetValue(world, out var done)) return done;
            var list = new List<H3dModel>();
            try
            {
                var bg = Gen6Pack.Read(_world((world * SlotsPerWorld) + 1), "BG");
                if (bg.Count > 4 && bg.Length(4) >= 8)
                {
                    var part = bg.Part(4).ToArray();
                    var inner = Gen7Events.Container(part);
                    if (inner is not null)
                        for (int i = 0; i < inner.Count; i++)
                        {
                            var m = inner.Part(i);
                            if (m.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(m) == Gfl2File.ModelMagic) list.Add(Gfl2File.ReadModel(m));
                        }
                }
            }
            catch (MapFormatException ex) { LastGraphicError = $"Paisaje del área {world}: {ex.Message}"; }
            return _scenery[world] = list;
        }
    }

    /// <summary>Warps of an area (ED part 2, 88-byte records of type 3).</summary>
    public IReadOnlyList<Gen7Warp> Warps(int area)
    {
        var list = new List<Gen7Warp>();
        var ed = EventPack(area);
        if (ed is null || ed.Count <= Gen7Events.WarpPack || ed.Length(Gen7Events.WarpPack) == 0) return list;
        var zones = Gen7Events.Container(ed.Part(Gen7Events.WarpPack).ToArray());
        if (zones is null) return list;
        for (int z = 0; z < zones.Count; z++)
            foreach (var r in Gen7Events.Records(zones.Part(z).ToArray(), 88, 3))
                list.Add(new Gen7Warp(area, z, r.Index, r.Position, (int)r.U32(44), r.U16(52), r.U16(54), (int)r.U32(48), (int)r.U32(56), new Vector3(r.F(60), r.F(64), r.F(68))));
        return list;
    }

    /// <summary>
    /// Destination of a warp: the zone at +0x36 gives the area through the zone table, and the
    /// destination warp is the one of that zone whose event id equals +0x30. Null when any step
    /// is missing. The return is verified when that warp points back to the source.
    /// </summary>
    public Gen7WarpLink? Resolve(Gen7Warp warp)
    {
        if (!_zoneTable.TryGetValue(warp.DestinationZone, out var zone)) return null;
        if (!_worlds.ContainsKey(zone.Area)) return new Gen7WarpLink(zone, null, false);
        var target = Warps(zone.Area).FirstOrDefault(w => w.Zone == warp.DestinationZone && w.Event == warp.DestinationEvent);
        bool back = target is not null && target.DestinationZone == warp.Zone && target.DestinationEvent == warp.Event;
        return new Gen7WarpLink(zone, target, back);
    }

    /// <summary>The area's event pack ("ED", slot 0), or null.</summary>
    public Gen6Pack? EventPack(int world)
    {
        try
        {
            var data = _world(world * SlotsPerWorld);
            return data.Length >= 8 && data[0] == 'E' && data[1] == 'D' ? Gen6Pack.Read(data, "ED") : null;
        }
        catch (MapFormatException) { return null; }
    }

    public int BlockCount => _chunkCount;

    /// <summary>Walkable-ground collision of the area: part 6 of every block of its matrix.</summary>
    public IReadOnlyList<Gen7CollisionTriangle> GroundCollision(int world)
    {
        var list = new List<Gen7CollisionTriangle>();
        foreach (int id in World(world).Blocks.Where(b => b >= 0 && b < _chunkCount).Distinct())
        {
            try
            {
                var data = _chunk(id);
                if (data.Length < 8 || data[0] != 'B' || data[1] != 'G') continue;
                var pack = Gen6Pack.Read(data, "BG");
                if (pack.Count > 6 && pack.Length(6) >= 8) list.AddRange(Gen7Collision.Read(pack.Part(6)));
            }
            catch (MapFormatException ex) { LastGraphicError = $"Colisión del bloque {id}: {ex.Message}"; }
        }
        return list;
    }

    /// <summary>Diagnostic layer: the walkable-ground collision drawn flat, one colour per surface attribute.</summary>
    public MapBlockImage? Diagnostic(int zone, int block, int size)
    {
        var w = World(zone);
        if (block < 0 || block >= w.Width * w.Height || size <= 0) return null;
        H3dModel? model;
        lock (_gate)
        {
            if (!_collision.TryGetValue(zone, out model))
            {
                var tris = GroundCollision(zone);
                _collision[zone] = model = tris.Count == 0 ? null : Gen7Collision.ToDiagnosticModel(tris);
            }
        }
        if (model is null) return null;
        int c = block % w.Width, r = block / w.Width;
        var render = Gen6TopDownRenderer.Render([model], _ => null, size, c * w.BlockSize, r * w.BlockSize, w.BlockSize);
        return new MapBlockImage(render.Size, render.Pixels, render.Triangles, render.Untextured);
    }

    /// <summary>GFL2 models stored for one block id of the map GARC.</summary>
    public IReadOnlyList<H3dModel> ModelsOfBlock(int id)
    {
        var list = new List<H3dModel>();
        if (id < 0 || id >= _chunkCount) return list;
        {
            IReadOnlyList<H3dModel>? found;
            lock (_gate)
            {
                if (!_models.TryGetValue(id, out found))
                {
                    var read = new List<H3dModel>();
                    try
                    {
                        var data = _chunk(id);
                        if (data.Length >= 8 && data[0] == 'B' && data[1] == 'G')
                        {
                            // Every part that is a GFL2 model is drawn (objects in part 0, terrain in part 1 in the checked blocks).
                            var pack = Gen6Pack.Read(data, "BG");
                            for (int i = 0; i < pack.Count; i++)
                            {
                                var part = pack.Part(i);
                                if (part.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(part) == Gfl2File.ModelMagic) read.Add(Gfl2File.ReadModel(part));
                            }
                        }
                    }
                    catch (MapFormatException ex) { LastGraphicError = $"Bloque {id}: {ex.Message}"; }
                    _models[id] = found = read;
                }
            }
            list.AddRange(found);
        }
        return list;
    }

    /// <summary>World textures from part 0 of the world's BG pack.</summary>
    public IReadOnlyDictionary<string, H3dTexture> Textures(int world)
    {
        lock (_gate)
        {
            if (_textures.TryGetValue(world, out var set)) return set;
            set = new Dictionary<string, H3dTexture>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var bg = Gen6Pack.Read(_world((world * SlotsPerWorld) + 1), "BG");
                var part = bg.Part(0).ToArray();
                if (part.Length >= 8 && part[0] == 'B' && part[1] == 'G')
                {
                    var textures = Gen6Pack.Read(part, "BG");
                    for (int i = 0; i < textures.Count; i++)
                    {
                        if (Gfl2File.ReadTexture(textures.Part(i)) is { } t)
                        {
                            set.TryAdd(t.Name, t);
                            set.TryAdd(System.IO.Path.GetFileNameWithoutExtension(t.Name), t);
                        }
                    }
                }
            }
            catch (MapFormatException ex) { LastGraphicError = $"Mundo {world}: {ex.Message}"; }
            foreach (var t in Statics(world).Textures)
            {
                set.TryAdd(t.Name, t);
                set.TryAdd(System.IO.Path.GetFileNameWithoutExtension(t.Name), t);
            }
            _textures[world] = set;
            return set;
        }
    }

    private DecodedTexture? Texture(int world, string name)
    {
        lock (_gate)
        {
            if (!_decoded.TryGetValue(world, out var cache)) _decoded[world] = cache = new(StringComparer.OrdinalIgnoreCase);
            if (cache.TryGetValue(name, out var done)) return done;
            DecodedTexture? result = null;
            var set = Textures(world);
            if (set.TryGetValue(name, out var t) || set.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(name), out t))
            {
                try { result = new DecodedTexture(t.Width, t.Height, t.Decode()); }
                catch (MapFormatException ex) { LastGraphicError = $"Textura {name}: {ex.Message}"; }
            }
            cache[name] = result;
            return result;
        }
    }

    public MapBlockImage? Graphic(int zone, int block, int size)
    {
        var w = World(zone);
        if (block < 0 || block >= w.Width * w.Height || size <= 0) return null;
        var models = Models(zone);
        if (models.Count == 0) return null;
        int c = block % w.Width, r = block / w.Width;
        // Block (c, r) spans [c·size, (c+1)·size) on X and Z (measured on 1×1, 1×2, 2×2, 3×3 and 9×7 areas).
        var render = Gen6TopDownRenderer.Render(models, name => Texture(zone, name), size, c * w.BlockSize, r * w.BlockSize, w.BlockSize);
        return new MapBlockImage(render.Size, render.Pixels, render.Triangles, render.Untextured);
    }

    public static Gen7MapBackend FromConfig(GameConfig config)
    {
        if (!config.USUM) throw new NotSupportedException("El adaptador de mapas Gen7 solo admite por ahora Ultra Sol / Ultra Luna.");
        var worlds = config.GetlzGARCData("encdata");
        var chunks = new GARC.LazyGARC(System.IO.File.ReadAllBytes(System.IO.Path.Combine(config.RomFS, "a/0/8/6")));
        object gate = new();
        byte[] W(int i) { lock (gate) return worlds[i]; }
        byte[] C(int i) { lock (gate) return chunks[i]; }
        var table = ReadZoneTable(config);
        var names = new Dictionary<int, string>();
        foreach (var z in table.Values.Where(z => z.Listed).OrderBy(z => z.Zone)) names.TryAdd(z.Area, z.Name);
        return new Gen7MapBackend("Pokémon Ultra Sol / Ultra Luna", worlds.FileCount, W, chunks.FileCount, C, names, table);
    }

    /// <summary>
    /// Area names: zone table (a/0/7/7: 84-byte zones; u16 per zone = world) → world header
    /// (WD part 0: u32 at 8/12 bound (zone, area) u16 pairs) → area. The name is the location
    /// text at zone+0x1C. Zones with byte 31 set are skipped, as in the game data tooling.
    /// </summary>
    public static IReadOnlyDictionary<int, Gen7ZoneInfo> ReadZoneTable(GameConfig config)
    {
        var result = new Dictionary<int, Gen7ZoneInfo>();
        try
        {
            var zd = config.GetlzGARCData("zonedata");
            byte[] zones = zd[0], worldOf = zd[1];
            var worlds = config.GetlzGARCData("worlddata");
            string[] names = config.GetText(TextName.metlist_000000);
            var headers = new Dictionary<int, byte[]>();
            for (int z = 0; z < zones.Length / 84 && (z * 2) + 2 <= worldOf.Length; z++)
            {
                bool listed = zones[(z * 84) + 31] == 0;
                int w = BinaryPrimitives.ReadUInt16LittleEndian(worldOf.AsSpan(z * 2));
                if (!headers.TryGetValue(w, out var h)) headers[w] = h = Gen6Pack.Read(worlds[w], "WD").Part(0).ToArray();
                int begin = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(8)), end = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(12));
                if (begin < 0 || end > h.Length || end < begin) continue;
                int name = BinaryPrimitives.ReadUInt16LittleEndian(zones.AsSpan((z * 84) + 28));
                for (int p = begin; p + 4 <= end; p += 4)
                    if (BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(p)) == z)
                        result.TryAdd(z, new Gen7ZoneInfo(z, BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(p + 2)), name < names.Length ? names[name] : $"Ubicación {name}", listed));
            }
        }
        catch (Exception ex) when (ex is MapFormatException or ArgumentException or IndexOutOfRangeException) { }
        return result;
    }
}

/// <summary>Global zone of the zone table: its area, location name and whether it is listed (byte 31 = 0).</summary>
public sealed record Gen7ZoneInfo(int Zone, int Area, string Name, bool Listed);

/// <summary>One warp record: own zone (+0x34), event (+0x2C), destination zone (+0x36) and event (+0x30), transition (+0x38), arrival (+0x3C).</summary>
public sealed record Gen7Warp(int Area, int LocalZone, int Row, Vector3 Position, int Event, int Zone, int DestinationZone, int DestinationEvent, int Transition, Vector3 Arrival);

/// <summary>Resolved destination of a warp.</summary>
public sealed record Gen7WarpLink(Gen7ZoneInfo Zone, Gen7Warp? Target, bool ReturnVerified);

/// <summary>Block matrix of a Gen7 world (the "TR" file).</summary>
public sealed class Gen7World
{
    public const int HeaderSize = 0x14, RecordSize = 12;
    public int Width { get; }
    public int Height { get; }
    public int BlockSize { get; }
    /// <summary>Block id per matrix position, row-major; -1 when empty.</summary>
    public IReadOnlyList<int> Blocks { get; }

    public Gen7World(byte[] data)
    {
        if (data.Length < HeaderSize) throw new MapFormatException("Matriz TR demasiado corta.");
        Width = BinaryPrimitives.ReadInt32LittleEndian(data);
        Height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        BlockSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        int area = Width * Height;
        if (Width <= 0 || Height <= 0 || area > 1024 || HeaderSize + (area * RecordSize) > data.Length) throw new MapFormatException($"Matriz TR {Width}×{Height} no válida.");
        if (BlockSize <= 0 || BlockSize > 100000) throw new MapFormatException($"Tamaño de bloque {BlockSize} no válido.");
        var blocks = new int[area];
        for (int i = 0; i < area; i++)
        {
            int id = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(HeaderSize + (i * RecordSize)));
            blocks[i] = id == 0xFFFF ? -1 : id;
        }
        Blocks = blocks;
    }
}
