#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
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
/// is centred on (c·size, r·size). Events are not identified yet, so the
/// adapter only draws the world.
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

    public string Family => "gen7-usum";
    public string Title { get; }
    public IReadOnlyList<MapZoneEntry> Zones => _entries;
    public IMapResourceProvider Resources => this;
    public IMapEventProvider Events => this;
    public IMapMutationValidator? Mutations => null;
    public MapCapabilities Capabilities { get; } = new(true, false, false,
        "Lectura: modelos y texturas reales de cada mundo vistos desde arriba. Los eventos de Gen7 todavía no están identificados y no se muestran.", GraphicsByDefault: true);
    public IReadOnlyList<(int World, string Reason)> Skipped { get; }
    public string? LastGraphicError { get; private set; }

    public Gen7MapBackend(string title, int worldFiles, Func<int, byte[]> world, int chunkCount, Func<int, byte[]> chunk)
    {
        Title = title; _world = world; _chunk = chunk; _chunkCount = chunkCount;
        var skipped = new List<(int, string)>();
        for (int w = 0; w < worldFiles / SlotsPerWorld; w++)
        {
            try
            {
                var tr = Gen6Pack.Read(world((w * SlotsPerWorld) + 2), "TR");
                var g = new Gen7World(tr.Part(0).ToArray());
                if (!g.Blocks.Any(b => b >= 0 && b < chunkCount)) { skipped.Add((w, "sin bloques")); continue; }
                _worlds[w] = g;
                _entries.Add(new MapZoneEntry(w, $"Mundo {w:000}", w, $"{g.Width}×{g.Height} bloques"));
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
    public IReadOnlyList<MapEventView> ZoneEvents(int zone) => [];

    /// <summary>Models of the world: the distinct block ids of its matrix that have data.</summary>
    public IReadOnlyList<H3dModel> Models(int world)
    {
        var list = new List<H3dModel>();
        foreach (int id in World(world).Blocks.Where(b => b >= 0 && b < _chunkCount).Distinct()) list.AddRange(ModelsOfBlock(id));
        return list;
    }

    public int BlockCount => _chunkCount;

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
        float half = w.BlockSize / 2f;
        var render = Gen6TopDownRenderer.Render(models, name => Texture(zone, name), size, (c * w.BlockSize) - half, (r * w.BlockSize) - half, w.BlockSize);
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
        return new Gen7MapBackend("Pokémon Ultra Sol / Ultra Luna", worlds.FileCount, W, chunks.FileCount, C);
    }
}

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
