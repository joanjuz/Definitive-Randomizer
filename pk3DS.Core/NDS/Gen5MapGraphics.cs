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
/// grid NPC stands (doors, where warps stand, have it on 66 %). Nothing else of the permission is interpreted. Buildings are listed per block but their
/// models are not drawn: which archive and pack a block's building ids refer to is not accredited.
/// </para>
/// </summary>
public sealed class Gen5MapGraphics : INdsMapGraphics
{
    private readonly byte[] _headers;
    private readonly Narc _blocks, _textures;
    private readonly Dictionary<int, NitroTextureSet> _sets = new();
    private readonly Dictionary<int, IReadOnlyList<NitroModel>> _models = new();

    public static Gen5MapGraphics Open(NdsRom rom)
    {
        var p = Gen5MapProfile.Require(rom, "Gráficos de mapa");
        byte[] Pinned(string path, uint crc)
        {
            byte[] bytes = rom.ReadFile(path);
            if (Gen5Data.Crc32(bytes) != crc) throw new InvalidDataException($"Gráficos de mapa {p.Key}: {path} no coincide con el archivo acreditado.");
            return bytes;
        }
        var g = new Gen5MapGraphics(Pinned(Gen5MapProfile.HeadersPath, p.HeadersCrc32), Pinned(Gen5MapProfile.BlocksPath, p.BlocksCrc32), Pinned(Gen5MapProfile.TexturesPath, p.TexturesCrc32));
        if (g.HeaderCount != p.HeaderCount || g.LandCount != p.BlockCount || g._textures.Files.Count != p.TextureSetCount) throw new InvalidDataException($"Gráficos de mapa {p.Key}: número inesperado de cabeceras, bloques o texturas.");
        return g;
    }

    /// <summary>Structural constructor (fixtures and the real ROM): the header archive, the map blocks and the texture sets.</summary>
    public Gen5MapGraphics(byte[] headersNarc, byte[] blocks, byte[] textures)
    {
        var h = Narc.Read(headersNarc);
        if (h.Files.Count != 1 || h.Files[0].Length == 0 || h.Files[0].Length % Gen5ZoneEvents.HeaderSize != 0) throw new InvalidDataException("Tabla de cabeceras Gen5 desconocida.");
        _headers = h.Files[0]; _blocks = Narc.Read(blocks); _textures = Narc.Read(textures);
    }

    public int LandCount => _blocks.Files.Count;
    public int HeaderCount => _headers.Length / Gen5ZoneEvents.HeaderSize;
    public int TextureSet(int header) => header >= 0 && header < HeaderCount ? U16(_headers, header * Gen5ZoneEvents.HeaderSize + 2) : throw new ArgumentOutOfRangeException(nameof(header));

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
    /// <summary>Buildings listed by a block (u32 count, 16 bytes each). Counted for the record of what is not drawn.</summary>
    public static int BuildingCount(byte[] block) { var (_, s) = Block(block); var list = s[^1]; return list.Length >= 4 ? (int)U32(list, 0) : 0; }

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

    /// <summary>The terrain model of a block as the engine places it (block-centred, 16 units per tile), with the header's terrain textures. Buildings are reported, not drawn.</summary>
    public IReadOnlyList<NitroPlacedModel> BlockScene(int land, int header, IReadOnlyList<PlatinumMapProp> props, out IReadOnlyList<string> skipped)
    {
        var set = Set(TextureSet(header)); var scene = LandModel(land).Select(m => new NitroPlacedModel(m, System.Numerics.Matrix4x4.Identity, set)).ToList();
        int buildings = BuildingCount(Member(_blocks, land));
        skipped = buildings > 0 ? [$"bloque {land}: {buildings} estructuras no se dibujan (modelos de edificios Gen5 no acreditados)"] : [];
        return scene;
    }
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
