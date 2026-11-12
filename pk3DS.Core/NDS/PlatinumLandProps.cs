#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>
/// One map-prop record of a land_data block (the engine's "MapPropFile"): raw 32-bit fixed-point values, 20.12 (0x1000 = 1.0), exactly as stored.
/// Format facts: pret/pokeplatinum overlay005/map_prop.c; 48 bytes per entry: model id, position, rotation, scale (3 × s32 each) and 8 unused bytes.
/// </summary>
public sealed record PlatinumMapProp(int Index, int ModelId, int RawX, int RawY, int RawZ, int RawRotationX, int RawRotationY, int RawRotationZ, int RawScaleX, int RawScaleY, int RawScaleZ, long Unused)
{
    public const double FixedOne = 4096.0, UnitsPerTile = 16.0;
    /// <summary>Position in engine units (1 tile = 16 units). Horizontal plane is X/Z; Y is the vertical axis.</summary>
    public double X => RawX / FixedOne;
    public double Y => RawY / FixedOne;
    public double Z => RawZ / FixedOne;
    /// <summary>Horizontal position in tiles from the CENTRE of the 32×32 block (the engine's map origin): -16 … +16.</summary>
    public double TileOffsetX => X / UnitsPerTile;
    public double TileOffsetZ => Z / UnitsPerTile;
    /// <summary>Position inside the block, in tiles from its north-west corner (0 … 32).</summary>
    public double LocalTileX => PlatinumMapLayout.BlockSize / 2.0 + TileOffsetX;
    public double LocalTileZ => PlatinumMapLayout.BlockSize / 2.0 + TileOffsetZ;
    public bool HasUnitScale => RawScaleX == 0x1000 && RawScaleY == 0x1000 && RawScaleZ == 0x1000;
    public bool HasRotation => RawRotationX != 0 || RawRotationY != 0 || RawRotationZ != 0;
}

/// <summary>A prop placed on a matrix: which cell it belongs to and its position in the matrix's tile space (same space as event coordinates).</summary>
public sealed record PlatinumPlacedProp(PlatinumMapProp Prop, int BlockX, int BlockY, int LandDataId, double TileX, double TileZ, bool InOwnedBlock);

/// <summary>
/// Read-only map props of Platinum CPUE/1 land_data. Only the props section is read: model ids and placements. The NSBMD building models and their
/// textures are NOT parsed, so nothing here describes the real shape of a structure. The block-centre origin and the X/Z-to-tile mapping were checked
/// against the collision footprints of the owner's dump (see NDS_MAP_PROPS_PHASE5B.md); they are an interpretation backed by data, not a guarantee.
/// </summary>
public sealed class PlatinumLandProps
{
    public const int EntrySize = 48;
    private readonly PlatinumMapProp[][] _props;
    private readonly int[] _trailing;

    public int Count => _props.Length;

    /// <summary>CPUE/1 only: requires the profile and the pinned CRC32 of the unmodified land_data archive (same contract as PlatinumTerrain).</summary>
    public static PlatinumLandProps Open(NdsRom rom)
    {
        if (rom.Header.GameCode != "CPUE" || rom.Header.Version != 1 || rom.Header.IsDsiEnhanced)
            throw new InvalidDataException("Estructuras: se requiere Platino CPUE/1; no se aplican perfiles de otros juegos.");
        byte[] bytes = rom.ReadFile(PlatinumTerrain.LandDataPath);
        if (Gen5Data.Crc32(bytes) != PlatinumTerrain.LandDataCrc32) throw new InvalidDataException("Estructuras CPUE/1: land_data no coincide con el archivo acreditado.");
        var props = ParseNarc(bytes);
        if (props.Count != PlatinumTerrain.FileCount) throw new InvalidDataException("Estructuras CPUE/1: número inesperado de miembros de land_data.");
        return props;
    }

    /// <summary>Structural parser (fixtures); does not identify or authorize a ROM.</summary>
    public static PlatinumLandProps ParseNarc(byte[] narcBytes)
    {
        var narc = Narc.Read(narcBytes); var all = new PlatinumMapProp[narc.Files.Count][]; var trailing = new int[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            var file = narc.Files[i];
            if (file.Length < PlatinumTerrain.HeaderSize + PlatinumTerrain.AttributesSize) throw new InvalidDataException("land_data truncado: " + i);
            uint terrain = BinaryPrimitives.ReadUInt32LittleEndian(file), props = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)),
                 model = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8)), bdhc = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(12));
            if (terrain != PlatinumTerrain.AttributesSize || (ulong)PlatinumTerrain.HeaderSize + terrain + props + model + bdhc != (ulong)file.Length)
                throw new InvalidDataException("Cabecera de land_data inconsistente con su longitud: " + i);
            // The engine reads size / 48 entries and ignores a remainder (one member of the original game has 2 spare bytes).
            int count = (int)(props / EntrySize); trailing[i] = (int)(props % EntrySize);
            var list = new PlatinumMapProp[count]; int start = PlatinumTerrain.HeaderSize + PlatinumTerrain.AttributesSize;
            for (int k = 0; k < count; k++)
            {
                int o = start + k * EntrySize; int S(int at) => BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(o + at, 4));
                list[k] = new(k, S(0), S(4), S(8), S(12), S(16), S(20), S(24), S(28), S(32), S(36), BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(o + 40, 8)));
            }
            all[i] = list;
        }
        return new(all, trailing);
    }

    private PlatinumLandProps(PlatinumMapProp[][] props, int[] trailing) { _props = props; _trailing = trailing; }

    public bool Contains(int landId) => (uint)landId < (uint)_props.Length;
    public IReadOnlyList<PlatinumMapProp> Props(int landId) => Contains(landId) ? _props[landId] : throw new ArgumentOutOfRangeException(nameof(landId));
    /// <summary>Bytes of the props section that do not form a whole entry (ignored by the engine as well).</summary>
    public int TrailingBytes(int landId) => Contains(landId) ? _trailing[landId] : throw new ArgumentOutOfRangeException(nameof(landId));

    /// <summary>
    /// Every prop of every cell of a matrix, positioned in the matrix's tile space. Props belong to the CELL's land_data (a property of the matrix),
    /// independently of which header owns the cell; <c>InOwnedBlock</c> says whether the stored header of the cell is the selected map.
    /// Empty cells (0xFFFF) and ids outside the archive contribute nothing.
    /// </summary>
    public IReadOnlyList<PlatinumPlacedProp> Place(PlatinumMapLayout layout)
    {
        var placed = new List<PlatinumPlacedProp>();
        for (int by = 0; by < layout.Height; by++)
            for (int bx = 0; bx < layout.Width; bx++)
            {
                var cell = layout.Cell(bx, by); int id = cell.LandDataId;
                if (id == PlatinumTerrain.EmptyLandId || !Contains(id)) continue;
                bool owned = !layout.HasStoredHeaders || cell.HeaderId == layout.MapId;
                foreach (var prop in _props[id])
                    placed.Add(new(prop, bx, by, id, bx * PlatinumMapLayout.BlockSize + prop.LocalTileX, by * PlatinumMapLayout.BlockSize + prop.LocalTileZ, owned));
            }
        return placed.AsReadOnly();
    }
}
