#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace pk3DS.Core.NDS;

/// <summary>A matrix cell, not geometry. Absent stored headers use the selected map's context.</summary>
public sealed record PlatinumMapCell(int BlockX, int BlockY, ushort? StoredHeaderId, int HeaderId, byte? RawAltitude, ushort LandDataId);
public sealed record PlatinumMapPosition(PlatinumMapCell Cell, int LocalX, int LocalY);

/// <summary>
/// Immutable, read-only CPUE/1 matrix layout. X/Y are horizontal tile coordinates within this matrix;
/// they are not world coordinates across matrices. No model, collision or event editing is performed.
/// Format facts: pret/pokeplatinum map_matrix.c (see NDS_MAP_LAYOUT_PHASE1.md).
/// </summary>
public sealed class PlatinumMapLayout
{
    public const int BlockSize = 32, MaxDimension = 30, MaxPrefixLength = 16;
    public int MapId { get; }
    public int MatrixId { get; }
    public int Width { get; }
    public int Height { get; }
    public int TileWidth => Width * BlockSize;
    public int TileHeight => Height * BlockSize;
    public bool HasStoredHeaders { get; }
    public bool HasStoredAltitudes { get; }
    public string ModelPrefix { get; }
    public IReadOnlyList<PlatinumMapCell> Cells { get; }
    public IReadOnlyList<PlatinumMapCell> OwnedCells { get; }

    /// <summary>Uses the existing CPUE/1 identity and table checks, including normalized event bytes.</summary>
    public static PlatinumMapLayout Open(NdsRom rom, int mapId)
    {
        var events = PlatinumEvents.Open(rom);
        var map = events.Map(mapId);
        var matrices = Narc.Read(rom.ReadFile(PlatinumEvents.MatrixPath));
        return ParseMatrix(matrices.Files[map.Matrix], map.Id, map.Matrix);
    }

    /// <summary>Structural fixture parser only; does not authorize a ROM. Use Open for game data.</summary>
    public static PlatinumMapLayout ParseMatrix(ReadOnlySpan<byte> bytes, int mapId, int matrixId)
    {
        if ((uint)mapId >= PlatinumEvents.HeaderCount) throw new ArgumentOutOfRangeException(nameof(mapId));
        if (matrixId < 0) throw new ArgumentOutOfRangeException(nameof(matrixId));
        return new(bytes, mapId, matrixId);
    }

    private PlatinumMapLayout(ReadOnlySpan<byte> bytes, int mapId, int matrixId)
    {
        if (bytes.Length < 5) throw new InvalidDataException("Matriz truncada.");
        int width = bytes[0], height = bytes[1], headers = bytes[2], altitudes = bytes[3], prefixLength = bytes[4];
        if (width is < 1 or > MaxDimension || height is < 1 or > MaxDimension || headers > 1 || altitudes > 1 || prefixLength > MaxPrefixLength)
            throw new InvalidDataException("Dimensiones, secciones o prefijo de matriz desconocidos.");
        int count = width * height, start = 5 + prefixLength;
        if (bytes.Length != start + count * (2 + headers * 2 + altitudes))
            throw new InvalidDataException("Longitud de matriz incompatible con sus secciones.");
        for (int i = 5; i < start; i++)
            if (bytes[i] is < 32 or > 126) throw new InvalidDataException("Prefijo de matriz no ASCII imprimible.");
        MapId = mapId; MatrixId = matrixId; Width = width; Height = height;
        HasStoredHeaders = headers != 0; HasStoredAltitudes = altitudes != 0;
        ModelPrefix = Encoding.ASCII.GetString(bytes.Slice(5, prefixLength));
        int altitudeStart = start + headers * count * 2, landStart = altitudeStart + altitudes * count;
        var cells = new PlatinumMapCell[count];
        for (int i = 0; i < count; i++)
        {
            ushort? stored = HasStoredHeaders ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(start + i * 2, 2)) : null;
            byte? altitude = HasStoredAltitudes ? bytes[altitudeStart + i] : null;
            // Preserve raw IDs, including zero and unknown values; neither implies empty terrain.
            cells[i] = new(i % width, i / width, stored, stored ?? mapId, altitude,
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(landStart + i * 2, 2)));
        }
        Cells = Array.AsReadOnly(cells);
        OwnedCells = Array.AsReadOnly(cells.Where(c => c.HeaderId == mapId).ToArray());
    }

    public PlatinumMapCell Cell(int blockX, int blockY)
    {
        if ((uint)blockX >= Width) throw new ArgumentOutOfRangeException(nameof(blockX));
        if ((uint)blockY >= Height) throw new ArgumentOutOfRangeException(nameof(blockY));
        return Cells[blockX + blockY * Width];
    }

    /// <summary>Does not require ownership: off-header events remain inspectable without inventing a correction.</summary>
    public PlatinumMapPosition Locate(int x, int y)
    {
        if ((uint)x >= TileWidth) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= TileHeight) throw new ArgumentOutOfRangeException(nameof(y));
        return new(Cell(x / BlockSize, y / BlockSize), x % BlockSize, y % BlockSize);
    }

    public (int X, int Y) ToTiles(int blockX, int blockY, int localX, int localY)
    {
        _ = Cell(blockX, blockY);
        if ((uint)localX >= BlockSize) throw new ArgumentOutOfRangeException(nameof(localX));
        if ((uint)localY >= BlockSize) throw new ArgumentOutOfRangeException(nameof(localY));
        return (blockX * BlockSize + localX, blockY * BlockSize + localY);
    }
}
