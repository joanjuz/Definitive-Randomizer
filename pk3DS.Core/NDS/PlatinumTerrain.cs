#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace pk3DS.Core.NDS;

public enum PlatinumTerrainStatus { Unknown, Blocked, ClearOfCollision }

/// <summary>
/// Static terrain-collision fact for one tile. <c>ClearOfCollision</c> only means that bit 15 of the tile's land_data attribute is not set;
/// it does NOT confirm that the tile is walkable (heights, tile behaviour, doors, scripts, objects and NPC movement are not evaluated).
/// </summary>
public sealed record PlatinumTerrainSample(PlatinumTerrainStatus Status, int? LandId, ushort? Attribute, string Reason)
{
    /// <summary>Low byte of the attribute (tile behaviour id). Stored raw; its meaning is not interpreted here.</summary>
    public byte? Behavior => Attribute is ushort a ? (byte)(a & PlatinumTerrain.BehaviorMask) : null;
}

/// <summary>
/// Read-only terrain attributes of Platinum CPUE/1 land_data. Format facts (pret/pokeplatinum, constants/field/map.h and
/// overlay005/land_data.c, cross-checked with the owner's dump): each land_data member starts with four u32 sizes
/// (terrain attributes, map props, map model, BDHC), followed by 0x800 bytes of terrain attributes = 32×32 u16, row-major
/// (index = z×32 + x); bit 15 = collision, low byte = tile behaviour. Props, models and BDHC are not parsed or copied.
/// </summary>
public sealed class PlatinumTerrain
{
    public const string LandDataPath = "fielddata/land_data/land_data.narc";
    public const uint LandDataCrc32 = 0xE1E68D47;
    public const int FileCount = 666, HeaderSize = 0x10, AttributesSize = 0x800, TilesPerSide = 32, TileCount = 1024;
    public const ushort CollisionMask = 0x8000, BehaviorMask = 0xFF, EmptyLandId = 0xFFFF;
    private readonly ushort[][] _attributes;
    private readonly int[] _propBytes, _modelBytes, _bdhcBytes;

    public int Count => _attributes.Length;

    /// <summary>CPUE/1 only: requires the profile and the pinned CRC32 of the unmodified land_data archive.</summary>
    public static PlatinumTerrain Open(NdsRom rom)
    {
        if (rom.Header.GameCode != "CPUE" || rom.Header.Version != 1 || rom.Header.IsDsiEnhanced)
            throw new InvalidDataException("Terreno: se requiere Platino CPUE/1; no se aplican perfiles de otros juegos.");
        byte[] bytes = rom.ReadFile(LandDataPath);
        if (Gen5Data.Crc32(bytes) != LandDataCrc32) throw new InvalidDataException("Terreno CPUE/1: land_data no coincide con el archivo acreditado.");
        var terrain = ParseNarc(bytes);
        if (terrain.Count != FileCount) throw new InvalidDataException("Terreno CPUE/1: número inesperado de miembros de land_data.");
        return terrain;
    }

    /// <summary>Structural parser (fixtures); does not identify or authorize a ROM. Use <see cref="Open"/> for game data.</summary>
    public static PlatinumTerrain ParseNarc(byte[] narcBytes)
    {
        var narc = Narc.Read(narcBytes);
        var attributes = new ushort[narc.Files.Count][]; var props = new int[attributes.Length]; var models = new int[attributes.Length]; var bdhc = new int[attributes.Length];
        for (int i = 0; i < attributes.Length; i++)
        {
            var file = narc.Files[i];
            if (file.Length < HeaderSize + AttributesSize) throw new InvalidDataException("land_data truncado: " + i);
            uint terrain = BinaryPrimitives.ReadUInt32LittleEndian(file), p = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)),
                 m = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8)), b = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(12));
            if (terrain != AttributesSize || (ulong)HeaderSize + terrain + p + m + b != (ulong)file.Length)
                throw new InvalidDataException("Cabecera de land_data inconsistente con su longitud: " + i);
            var values = new ushort[TileCount];
            for (int t = 0; t < TileCount; t++) values[t] = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(HeaderSize + t * 2));
            attributes[i] = values; props[i] = (int)p; models[i] = (int)m; bdhc[i] = (int)b;
        }
        return new(attributes, props, models, bdhc);
    }

    private PlatinumTerrain(ushort[][] attributes, int[] props, int[] models, int[] bdhc) { _attributes = attributes; _propBytes = props; _modelBytes = models; _bdhcBytes = bdhc; }

    public bool Contains(int landId) => (uint)landId < (uint)_attributes.Length;
    public int PropBytes(int landId) => _propBytes[landId];
    public int ModelBytes(int landId) => _modelBytes[landId];
    public int BdhcBytes(int landId) => _bdhcBytes[landId];
    /// <summary>Raw u16 attribute of tile (x, z) inside a 32×32 land block. Row-major: index = z×32 + x.</summary>
    public ushort Attribute(int landId, int localX, int localZ)
    {
        if (!Contains(landId)) throw new ArgumentOutOfRangeException(nameof(landId));
        if ((uint)localX >= TilesPerSide) throw new ArgumentOutOfRangeException(nameof(localX));
        if ((uint)localZ >= TilesPerSide) throw new ArgumentOutOfRangeException(nameof(localZ));
        return _attributes[landId][localZ * TilesPerSide + localX];
    }

    /// <summary>Allocation-free status of a tile (same rules as <see cref="Sample"/>); for bulk work such as overlays.</summary>
    public PlatinumTerrainStatus Status(PlatinumMapLayout layout, int x, int y)
    {
        if (x < 0 || y < 0 || x >= layout.TileWidth || y >= layout.TileHeight) return PlatinumTerrainStatus.Unknown;
        int id = layout.Cell(x / TilesPerSide, y / TilesPerSide).LandDataId;
        if (id == EmptyLandId || !Contains(id)) return PlatinumTerrainStatus.Unknown;
        return (_attributes[id][y % TilesPerSide * TilesPerSide + x % TilesPerSide] & CollisionMask) != 0 ? PlatinumTerrainStatus.Blocked : PlatinumTerrainStatus.ClearOfCollision;
    }

    /// <summary>Terrain fact for an event-space tile of a matrix. Unknown never means free.</summary>
    public PlatinumTerrainSample Sample(PlatinumMapLayout layout, int x, int y)
    {
        if (x < 0 || y < 0 || x >= layout.TileWidth || y >= layout.TileHeight) return new(PlatinumTerrainStatus.Unknown, null, null, "fuera de la matriz");
        var at = layout.Locate(x, y); int id = at.Cell.LandDataId;
        if (id == EmptyLandId) return new(PlatinumTerrainStatus.Unknown, null, null, "celda de la matriz sin land_data (ID 0xFFFF)");
        if (!Contains(id)) return new(PlatinumTerrainStatus.Unknown, id, null, $"ID de land_data {id} fuera de la tabla ({Count})");
        ushort attribute = Attribute(id, at.LocalX, at.LocalY);
        return new((attribute & CollisionMask) != 0 ? PlatinumTerrainStatus.Blocked : PlatinumTerrainStatus.ClearOfCollision, id, attribute,
            $"land_data {id}, atributo 0x{attribute:X4} (bit de colisión {(attribute & CollisionMask) >> 15}, comportamiento 0x{attribute & BehaviorMask:X2} sin interpretar)");
    }
}
