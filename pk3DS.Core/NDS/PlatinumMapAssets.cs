#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using pk3DS.Core.NDS.Nitro;

namespace pk3DS.Core.NDS;

/// <summary>What one map header loads (pret/pokeplatinum, docs/maps/file_format_specifications.md and overlay005/area_data.c).</summary>
public sealed record PlatinumAreaInfo(int Area, int PropArchive, int MapTextureArchive, int LightArchive, IReadOnlyList<int> PropModels);

/// <summary>
/// Read-only access to the graphics resources of Platinum CPUE/1 maps: header → area (area_data) → terrain textures (map_tex_set), prop model list
/// (area_build) and prop textures (areabm_texset); land_data → terrain model (NSBMD) of each block; build_model → prop models. Every archive is pinned by
/// CRC32 (as land_data already was); nothing is written and nothing is cached across instances. The decoded geometry and images are never stored by the
/// application: they are rebuilt from the user's own ROM.
/// </summary>
public sealed class PlatinumMapAssets
{
    public const string AreaDataPath = "fielddata/areadata/area_data.narc", AreaBuildPath = "fielddata/areadata/area_build_model/area_build.narc",
        AreaBuildTexturesPath = "fielddata/areadata/area_build_model/areabm_texset.narc", MapTexturesPath = "fielddata/areadata/area_map_tex/map_tex_set.narc",
        BuildModelPath = "fielddata/build_model/build_model.narc";
    public const uint AreaDataCrc32 = 0x1B96433F, AreaBuildCrc32 = 0xA511632E, AreaBuildTexturesCrc32 = 0x50AA2DA7, MapTexturesCrc32 = 0xE869CBCB, BuildModelCrc32 = 0xF850843C;
    private readonly byte[] _headers;
    private readonly Narc _area, _areaBuild, _areaBuildTextures, _mapTextures, _buildModels, _land;
    private readonly Dictionary<int, NitroTextureSet> _mapTextureSets = new(), _propTextureSets = new();
    private readonly Dictionary<int, IReadOnlyList<NitroModel>> _propModels = new(), _landModels = new();

    public static PlatinumMapAssets Open(NdsRom rom)
    {
        var profile = Gen4EventProfile.Require(rom, "Gráficos de mapa");
        byte[] Pinned(string path, uint crc)
        {
            byte[] bytes = rom.ReadFile(path);
            if (Gen5Data.Crc32(bytes) != crc) throw new InvalidDataException($"Gráficos de mapa {profile.Key}: {path} no coincide con el archivo acreditado.");
            return bytes;
        }
        byte[] arm9 = rom.ReadArm9();
        return new(arm9.AsSpan(profile.HeaderOffset, profile.HeaderCount * 24).ToArray(), Pinned(AreaDataPath, profile.AreaDataCrc32), Pinned(AreaBuildPath, profile.AreaBuildCrc32),
            Pinned(AreaBuildTexturesPath, profile.AreaBuildTexturesCrc32), Pinned(MapTexturesPath, profile.MapTexturesCrc32), Pinned(BuildModelPath, profile.BuildModelCrc32), Pinned(profile.LandDataPath, profile.LandDataCrc32));
    }

    /// <summary>Structural constructor (fixtures and the real ROM): headers table (24 bytes per header) and the six archives.</summary>
    public PlatinumMapAssets(byte[] headers, byte[] areaData, byte[] areaBuild, byte[] areaBuildTextures, byte[] mapTextures, byte[] buildModels, byte[] landData)
    {
        if (headers.Length == 0 || headers.Length % 24 != 0) throw new InvalidDataException("Tabla de cabeceras truncada.");
        _headers = (byte[])headers.Clone();
        _area = Narc.Read(areaData); _areaBuild = Narc.Read(areaBuild); _areaBuildTextures = Narc.Read(areaBuildTextures);
        _mapTextures = Narc.Read(mapTextures); _buildModels = Narc.Read(buildModels); _land = Narc.Read(landData);
    }

    public int HeaderCount => _headers.Length / 24;
    public int LandCount => _land.Files.Count;

    public PlatinumAreaInfo Area(int header)
    {
        if (header < 0 || header >= HeaderCount) throw new ArgumentOutOfRangeException(nameof(header));
        int area = _headers[header * 24];
        if (area >= _area.Files.Count) throw new InvalidDataException($"La cabecera {header} usa un área inexistente ({area}).");
        var a = _area.Files[area]; if (a.Length < 8) throw new InvalidDataException("Datos de área truncados.");
        int props = U16(a, 0), textures = U16(a, 2), light = U16(a, 6);
        if (props >= _areaBuild.Files.Count || props >= _areaBuildTextures.Files.Count || textures >= _mapTextures.Files.Count) throw new InvalidDataException($"El área {area} apunta fuera de sus archivos.");
        var list = _areaBuild.Files[props]; int count = list.Length >= 2 ? U16(list, 0) : -1;
        if (count < 0 || 2 + count * 2 > list.Length) throw new InvalidDataException("Lista de modelos del área truncada.");
        var models = Enumerable.Range(0, count).Select(i => U16(list, 2 + i * 2)).ToArray();
        if (models.Any(m => m >= _buildModels.Files.Count)) throw new InvalidDataException("La lista del área nombra un modelo inexistente.");
        return new(area, props, textures, light, models);
    }

    public NitroTextureSet MapTextures(int archive) => _mapTextureSets.TryGetValue(archive, out var set) ? set : _mapTextureSets[archive] = NitroTextureSet.FromBtx(Member(_mapTextures, archive));
    public NitroTextureSet PropTextures(int archive) => _propTextureSets.TryGetValue(archive, out var set) ? set : _propTextureSets[archive] = NitroTextureSet.FromBtx(Member(_areaBuildTextures, archive));
    public IReadOnlyList<NitroModel> PropModel(int id) => _propModels.TryGetValue(id, out var m) ? m : _propModels[id] = NitroModels.Read(Member(_buildModels, id));
    /// <summary>The terrain model (NSBMD) of a land_data block: the section between the props and the BDHC.</summary>
    public IReadOnlyList<NitroModel> LandModel(int land) => _landModels.TryGetValue(land, out var m) ? m : _landModels[land] = NitroModels.Read(LandModelBytes(land));
    public byte[] LandModelBytes(int land)
    {
        var b = Member(_land, land); if (b.Length < 16) throw new InvalidDataException("Bloque de land_data truncado.");
        long attributes = U32(b, 0), props = U32(b, 4), model = U32(b, 8);
        if (attributes != 0x800 || 16 + attributes + props + model > b.Length) throw new InvalidDataException($"Bloque de land_data {land} con secciones fuera de rango.");
        return b.AsSpan((int)(16 + attributes + props), (int)model).ToArray();
    }
    /// <summary>
    /// The models of one matrix block as the engine places them, in map units relative to the block centre (1 tile = 16 units): the land_data terrain model
    /// with the area's terrain textures, and each prop with its position, rotation (0–65535 per axis, applied X, Y, Z) and scale, textured with the area's
    /// prop textures. A prop whose model is not loaded by the area is NOT drawn (the engine would substitute its dummy box); it is reported in
    /// <paramref name="skipped"/>. Prop rotation signs are an interpretation; the original Platinum maps checked so far use no rotation.
    /// </summary>
    public IReadOnlyList<NitroPlacedModel> BlockScene(int land, int header, IReadOnlyList<PlatinumMapProp> props, out IReadOnlyList<string> skipped)
    {
        var area = Area(header); var scene = new List<NitroPlacedModel>(); var notes = new List<string>();
        var terrainTextures = MapTextures(area.MapTextureArchive); var propTextures = PropTextures(area.PropArchive);
        foreach (var model in LandModel(land)) scene.Add(new(model, System.Numerics.Matrix4x4.Identity, terrainTextures));
        foreach (var prop in props)
        {
            if (!area.PropModels.Contains(prop.ModelId)) { notes.Add($"prop {prop.Index}: modelo {prop.ModelId} no cargado por el área {area.Area}"); continue; }
            static float Angle(int raw) => (raw & 0xFFFF) / 65536f * MathF.Tau;
            var transform = System.Numerics.Matrix4x4.CreateScale(prop.RawScaleX / 4096f, prop.RawScaleY / 4096f, prop.RawScaleZ / 4096f)
                * System.Numerics.Matrix4x4.CreateRotationX(Angle(prop.RawRotationX)) * System.Numerics.Matrix4x4.CreateRotationY(Angle(prop.RawRotationY)) * System.Numerics.Matrix4x4.CreateRotationZ(Angle(prop.RawRotationZ))
                * System.Numerics.Matrix4x4.CreateTranslation(prop.RawX / 4096f, prop.RawY / 4096f, prop.RawZ / 4096f);
            foreach (var model in PropModel(prop.ModelId)) scene.Add(new(model, transform, propTextures));
        }
        skipped = notes; return scene;
    }
    /// <summary>Whether the header's area provides the texture and palette of every textured material of the block's terrain model.</summary>
    public bool TerrainTexturesBind(int land, int header)
    {
        var set = MapTextures(Area(header).MapTextureArchive);
        return LandModel(land).SelectMany(m => m.Materials).Where(m => m.Texture is not null)
            .All(m => set.Texture(m.Texture!) is { } t && (t.Format == NitroTextureFormat.Direct || m.Palette is not null && set.Palette(m.Palette) is not null));
    }
    /// <summary>The first header (in the given order) whose area binds every terrain texture of the block; the first one when none does.</summary>
    public int FirstBindingHeader(int land, IReadOnlyList<int> candidates)
    {
        foreach (int header in candidates) if (TerrainTexturesBind(land, header)) return header;
        return candidates.Count > 0 ? candidates[0] : 0;
    }
    public int BuildModelCount => _buildModels.Files.Count;
    public int MapTextureCount => _mapTextures.Files.Count;
    public int PropTextureCount => _areaBuildTextures.Files.Count;

    private static byte[] Member(Narc narc, int index) => index >= 0 && index < narc.Files.Count ? narc.Files[index] : throw new ArgumentOutOfRangeException(nameof(index));
    private static int U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static long U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
