#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using pk3DS.Core.NDS.Nitro;

namespace pk3DS.Core.NDS;

/// <summary>What the top-down map renderer needs from a game's map graphics (Gen 4: <see cref="PlatinumMapAssets"/>; Gen 5: <see cref="Gen5MapGraphics"/>).</summary>
public interface INdsMapGraphics
{
    int LandCount { get; }
    int HeaderCount { get; }
    /// <summary>The terrain model of a block (drawn first by <see cref="BlockScene"/>).</summary>
    IReadOnlyList<NitroModel> LandModel(int land);
    IReadOnlyList<NitroPlacedModel> BlockScene(int land, int header, IReadOnlyList<PlatinumMapProp> props, out IReadOnlyList<string> skipped);
    bool TerrainTexturesBind(int land, int header);
    int FirstBindingHeader(int land, IReadOnlyList<int> candidates);
}

/// <summary>
/// Read-only map blocks of Black, White, Black 2 and White 2 (a/0/0/8), measured on the owner's dumps. Each block is a container: u16 magic, u16 section
/// count, one u32 offset per section and the u32 end. WB (3 sections) and GC (4) hold the terrain model (NSBMD), 32×32 permissions (u16 32, u16 32, then
/// 8 bytes per tile, row-major) and the building list; GC has a second permission layer; NG (2) has no permissions; RD (3) has a rail-format permission
/// section that is not interpreted. Terrain textures come from the texture set (a/0/1/4) a header names at +2.
/// <para>
/// Collision is an inference from the data: bit 0 of byte 6 of a tile's permission is set on 78 % of all tiles of Black but only on 5 % of the tiles where a
/// grid NPC stands (doors, where warps stand, have it on 66 %). Nothing else of the permission is interpreted.
/// </para>
/// <para>
/// Buildings (read-only, see <see cref="Gen5BuildingProfile"/>): each block lists its buildings; the header's area (+2) picks a pack of BMD0 models and
/// its texture set, and every building is drawn with its position and yaw. A building whose pack or id cannot be resolved is reported, never guessed.
/// When the building archives are missing or are not the accredited ones, the terrain is drawn alone and the buildings are reported as before.
/// </para>
/// <para><see cref="Season"/> is a preview choice only (nothing is written): seasonal areas load the texture set and pack of the chosen season.</para>
/// </summary>
public sealed class Gen5MapGraphics : INdsMapGraphics
{
    private readonly byte[] _headers;
    private readonly Narc _blocks, _textures;
    private readonly Dictionary<int, NitroTextureSet> _sets = new();
    private readonly Dictionary<int, IReadOnlyList<NitroModel>> _models = new();
    private readonly Gen5BuildingArchives? _buildings;
    /// <summary>Bounded caches (parsed packs with their textures, decoded building models): switching maps never holds more than this.</summary>
    public const int PackCacheLimit = 12, ModelCacheLimit = 384;
    private readonly BoundedCache<(bool Interior, int Pack), (Gen5BuildingPack Pack, NitroTextureSet Textures)> _packs = new(PackCacheLimit);
    private readonly BoundedCache<(bool Interior, int Pack, int Id), IReadOnlyList<NitroModel>> _buildingModels = new(ModelCacheLimit);
    private int _season;
    /// <summary>Why the buildings are not drawn (archives missing or not accredited), or null when they are.</summary>
    public string? BuildingsUnavailable { get; }
    public bool DrawsBuildings => _buildings is not null;
    /// <summary>Preview season (0 spring — the area every header names —, 1 summer, 2 autumn, 3 winter). Only changes what is drawn.</summary>
    public int Season
    {
        get => _season;
        set => _season = value is >= 0 and < Gen5BuildingProfile.Seasons ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public static Gen5MapGraphics Open(NdsRom rom)
    {
        var p = Gen5MapProfile.Require(rom, "Gráficos de mapa");
        byte[] Pinned(string path, uint crc)
        {
            byte[] bytes = rom.ReadFile(path);
            if (Gen5Data.Crc32(bytes) != crc) throw new InvalidDataException($"Gráficos de mapa {p.Key}: {path} no coincide con el archivo acreditado.");
            return bytes;
        }
        byte[] headers = Pinned(Gen5MapProfile.HeadersPath, p.HeadersCrc32), blocks = Pinned(Gen5MapProfile.BlocksPath, p.BlocksCrc32), textures = Pinned(Gen5MapProfile.TexturesPath, p.TexturesCrc32);
        // The buildings never block the terrain: a missing or different archive only leaves them undrawn, with the reason.
        Gen5BuildingArchives? buildings = null; string? unavailable = null;
        try { buildings = Gen5BuildingArchives.Open(rom, p); }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException or ArgumentException) { unavailable = ex.Message; }
        var g = new Gen5MapGraphics(headers, blocks, textures, buildings, unavailable);
        if (g.HeaderCount != p.HeaderCount || g.LandCount != p.BlockCount || g._textures.Files.Count != p.TextureSetCount) throw new InvalidDataException($"Gráficos de mapa {p.Key}: número inesperado de cabeceras, bloques o texturas.");
        if (buildings is not null && buildings.Profile.AreaCount != g._textures.Files.Count) throw new InvalidDataException($"Gráficos de mapa {p.Key}: la tabla de áreas no corresponde a los conjuntos de texturas.");
        return g;
    }

    /// <summary>Structural constructor (fixtures and the real ROM): the header archive, the map blocks and the texture sets; the buildings are optional.</summary>
    public Gen5MapGraphics(byte[] headersNarc, byte[] blocks, byte[] textures, Gen5BuildingArchives? buildings = null, string? buildingsUnavailable = null)
    {
        var h = Narc.Read(headersNarc);
        if (h.Files.Count != 1 || h.Files[0].Length == 0 || h.Files[0].Length % Gen5ZoneEvents.HeaderSize != 0) throw new InvalidDataException("Tabla de cabeceras Gen5 desconocida.");
        _headers = h.Files[0]; _blocks = Narc.Read(blocks); _textures = Narc.Read(textures); _buildings = buildings;
        BuildingsUnavailable = buildings is null ? buildingsUnavailable ?? "faltan los archivos de edificios" : null;
    }

    public int LandCount => _blocks.Files.Count;
    public int HeaderCount => _headers.Length / Gen5ZoneEvents.HeaderSize;
    /// <summary>The area a header names at +2 (for seasonal areas, the spring one).</summary>
    public int Area(int header) => header >= 0 && header < HeaderCount ? U16(_headers, header * Gen5ZoneEvents.HeaderSize + 2) : throw new ArgumentOutOfRangeException(nameof(header));
    /// <summary>The area loaded in the preview <see cref="Season"/>; without building archives, the header's own area as before.</summary>
    public int LoadedArea(int header) => _buildings is { } b ? b.Profile.SeasonalArea(Area(header), _season) : Area(header);
    /// <summary>The terrain texture set (a/0/1/4) of the loaded area: +2 of its area record, which equals the area index in every area of the three games.</summary>
    public int TextureSet(int header)
    {
        int area = LoadedArea(header);
        return _buildings is { } b && area < b.Profile.AreaCount ? U16(b.Areas, area * Gen5BuildingProfile.AreaSize + 2) : area;
    }

    /// <summary>The sections of a block, after checking that the offsets are ordered and end where the member ends.</summary>
    public static (string Kind, byte[][] Sections) Block(byte[] b)
    {
        if (b.Length < 8) throw new InvalidDataException("Bloque de mapa Gen5 truncado.");
        string kind = $"{(char)b[0]}{(char)b[1]}"; int n = U16(b, 2);
        int expected = kind switch { "WB" => 3, "GC" => 4, "NG" => 2, "RD" => 3, _ => -1 };
        if (n != expected || b.Length < 4 + 4 * (n + 1)) throw new InvalidDataException($"Bloque de mapa Gen5 de tipo desconocido ({kind}, {n} secciones).");
        var offsets = Enumerable.Range(0, n + 1).Select(i => (int)U32(b, 4 + 4 * i)).ToArray();
        if (offsets[0] != 4 + 4 * (n + 1) || offsets[n] != b.Length || offsets.Zip(offsets.Skip(1)).Any(p => p.Second < p.First)) throw new InvalidDataException("Secciones de bloque Gen5 fuera de orden.");
        return (kind, Enumerable.Range(0, n).Select(i => b.AsSpan(offsets[i], offsets[i + 1] - offsets[i]).ToArray()).ToArray());
    }
    /// <summary>The 32×32 permissions of a WB or GC block (first layer), or null for blocks whose permissions are not interpreted (NG, RD, other sizes).</summary>
    public static byte[]? Permissions(byte[] block)
    {
        var (kind, s) = Block(block);
        if (kind is not ("WB" or "GC")) return null;
        var p = s[1];
        if (p.Length < 4) throw new InvalidDataException("Permisos de bloque Gen5 truncados.");
        // One block of Black stores 64×64 permissions: its layout is not interpreted, so its terrain stays unknown.
        if (U16(p, 0) != 32 || U16(p, 2) != 32) return null;
        if (p.Length < 4 + 1024 * 8) throw new InvalidDataException("Permisos de bloque Gen5 truncados.");
        return p;
    }
    /// <summary>Collision as inferred (see the class remarks): bit 0 of byte 6 of the tile's permission.</summary>
    public static bool Collides(byte[] permissions, int localX, int localY) => (permissions[4 + 8 * (localY * 32 + localX) + 6] & 1) != 0;
    /// <summary>Buildings listed by a block (u32 count, 16 bytes each).</summary>
    public static int BuildingCount(byte[] block) { var (_, s) = Block(block); var list = s[^1]; return list.Length >= 4 ? (int)U32(list, 0) : 0; }
    /// <summary>The buildings of a block (its last section).</summary>
    public static IReadOnlyList<Gen5Building> Buildings(byte[] block) => Gen5Building.Read(Block(block).Sections[^1]);
    public IReadOnlyList<Gen5Building> BlockBuildings(int land) => Buildings(Member(_blocks, land));
    /// <summary>The building pack of the header's loaded area, or null when it has none (or there are no building archives).</summary>
    public (int Pack, bool Interior)? BuildingPack(int header) => _buildings?.Profile.Pack(_buildings.Areas, LoadedArea(header));

    /// <summary>
    /// The terrain of every block in the shared presentation form: bit 15 set where the tile collides (inference above), low byte 0 (tile behaviour is not
    /// interpreted). Blocks without interpreted permissions (NG, RD) are unknown, never free.
    /// </summary>
    public PlatinumTerrain Terrain()
    {
        var attributes = new ushort[]?[LandCount];
        for (int land = 0; land < LandCount; land++)
        {
            if (Permissions(_blocks.Files[land]) is not { } p) continue;
            var a = new ushort[PlatinumTerrain.TileCount];
            for (int t = 0; t < a.Length; t++) a[t] = (p[4 + 8 * t + 6] & 1) != 0 ? PlatinumTerrain.CollisionMask : (ushort)0;
            attributes[land] = a;
        }
        return PlatinumTerrain.FromAttributes(attributes, "permisos Gen5 (bit 0 del byte 6, inferido de los datos)");
    }

    public IReadOnlyList<NitroModel> LandModel(int land) => _models.TryGetValue(land, out var m) ? m : _models[land] = NitroModels.Read(Block(Member(_blocks, land)).Sections[0]);
    private NitroTextureSet Set(int index) => _sets.TryGetValue(index, out var s) ? s : _sets[index] = NitroTextureSet.FromBtx(Member(_textures, index));

    /// <summary>
    /// The terrain model of a block as the engine places it (block-centred, 16 units per tile) with the loaded area's terrain textures, then each building
    /// of the block: the model its id names in the area's pack, turned by its yaw and moved to its position, with the pack's textures. <paramref name="props"/>
    /// belongs to Gen 4 and is ignored. Buildings that cannot be resolved are listed in <paramref name="skipped"/> and not drawn.
    /// </summary>
    public IReadOnlyList<NitroPlacedModel> BlockScene(int land, int header, IReadOnlyList<PlatinumMapProp> props, out IReadOnlyList<string> skipped)
    {
        var set = Set(TextureSet(header)); var scene = LandModel(land).Select(m => new NitroPlacedModel(m, System.Numerics.Matrix4x4.Identity, set)).ToList();
        var buildings = BlockBuildings(land); var notes = new List<string>();
        if (buildings.Count == 0) { skipped = notes; return scene; }
        if (_buildings is null) { skipped = [$"bloque {land}: {buildings.Count} estructuras no se dibujan ({BuildingsUnavailable})"]; return scene; }
        if (BuildingPack(header) is not { } pack) { skipped = [$"bloque {land}: {buildings.Count} estructuras no se dibujan (el área {LoadedArea(header)} no tiene paquete de edificios)"]; return scene; }
        var (models, textures) = Pack(pack.Interior, pack.Pack);
        foreach (var b in buildings)
        {
            if (models.Model(b.Id) is not { } bytes)
            {
                notes.Add($"edificio {b.Index} del bloque {land}: el id {b.Id} {(models.IsAmbiguous(b.Id) ? "es ambiguo" : "no está")} en el paquete {(pack.Interior ? "interior" : "exterior")} {pack.Pack}");
                continue;
            }
            IReadOnlyList<NitroModel> model;
            try { model = _buildingModels.Get((pack.Interior, pack.Pack, b.Id), () => NitroModels.Read(bytes)); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException) { notes.Add($"edificio {b.Index} del bloque {land}: el modelo {b.Id} no se puede leer ({ex.Message})"); continue; }
            var transform = Placement(b);
            foreach (var m in model) scene.Add(new(m, transform, textures));
        }
        skipped = notes; return scene;
    }
    /// <summary>
    /// Yaw about the vertical axis, then the position in map units (fx32 1.19.12). The stored Z grows to the NORTH, the opposite of the terrain model, the
    /// permissions and the events: it is negated. Measured on every block with buildings: with −Z, 91.1 % (Black) and 92.7 % (White 2) of the tiles the
    /// buildings cover are blocked in the permissions (75.7 % and 79.7 % of all tiles are), and 4× more warps touch a building (its doors); with +Z,
    /// 72.2 % and 76.5 %, below chance. The yaw turns counter-clockwise seen from above, as documented. The statistics cannot tell the two directions apart
    /// (most turned buildings are symmetric); the off-centre mall of Route 9 (id 146, yaw 0x4000) does: counter-clockwise it stands on its terrain
    /// footprint with the door on its warp, clockwise it covers the end of the road.
    /// </summary>
    public static System.Numerics.Matrix4x4 Placement(Gen5Building b) =>
        System.Numerics.Matrix4x4.CreateRotationY(b.Yaw / 65536f * MathF.Tau) * System.Numerics.Matrix4x4.CreateTranslation(b.RawX / 4096f, b.RawY / 4096f, -b.RawZ / 4096f);
    private (Gen5BuildingPack Pack, NitroTextureSet Textures) Pack(bool interior, int pack) => _packs.Get((interior, pack), () =>
        (new Gen5BuildingPack(Member(interior ? _buildings!.InteriorPacks : _buildings!.ExteriorPacks, pack)),
         NitroTextureSet.FromBtx(Member(interior ? _buildings.InteriorTextures : _buildings.ExteriorTextures, pack))));
    /// <summary>Entries currently held by the bounded building caches.</summary>
    public (int Packs, int Models) CachedBuildings => (_packs.Count, _buildingModels.Count);
    public bool TerrainTexturesBind(int land, int header)
    {
        var set = Set(TextureSet(header));
        return LandModel(land).SelectMany(m => m.Materials).Where(m => m.Texture is not null)
            .All(m => set.Texture(m.Texture!) is { } t && (t.Format == NitroTextureFormat.Direct || m.Palette is not null && set.Palette(m.Palette) is not null));
    }
    public int FirstBindingHeader(int land, IReadOnlyList<int> candidates)
    {
        foreach (int header in candidates) if (TerrainTexturesBind(land, header)) return header;
        return candidates.Count > 0 ? candidates[0] : 0;
    }

    private static byte[] Member(Narc narc, int index) => index >= 0 && index < narc.Files.Count ? narc.Files[index] : throw new ArgumentOutOfRangeException(nameof(index));
    private static int U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
