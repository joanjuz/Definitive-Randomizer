#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace pk3DS.Core.Maps.Gen6;

/// <summary>
/// BinLinker container used by the Gen6 field files ("ZO", "MM", "GR"):
/// two ASCII bytes, a 16-bit count and count+1 absolute offsets.
/// The original bytes are kept so callers can patch fields in place.
/// </summary>
public sealed class Gen6Pack
{
    public string Magic { get; }
    public byte[] Raw { get; }
    private readonly int[] _offsets;

    private Gen6Pack(string magic, byte[] raw, int[] offsets) { Magic = magic; Raw = raw; _offsets = offsets; }

    public int Count => _offsets.Length - 1;
    public int Start(int index) => _offsets[index];
    public int Length(int index) => _offsets[index + 1] - _offsets[index];
    public ReadOnlySpan<byte> Part(int index) => Raw.AsSpan(_offsets[index], Length(index));

    public static Gen6Pack Read(byte[] data, string magic)
    {
        if (data is null || data.Length < 8 || data[0] != magic[0] || data[1] != magic[1])
            throw new MapFormatException($"No es un paquete {magic}.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        if (count == 0 || 4 + ((count + 1) * 4) > data.Length)
            throw new MapFormatException($"Paquete {magic} con {count} partes no válido.");
        var offsets = new int[count + 1];
        for (int i = 0; i <= count; i++)
        {
            offsets[i] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4 + (i * 4)));
            if (offsets[i] < 0 || offsets[i] > data.Length || (i > 0 && offsets[i] < offsets[i - 1]))
                throw new MapFormatException($"Desplazamiento {i} del paquete {magic} fuera de rango.");
        }
        return new Gen6Pack(magic, data, offsets);
    }
}

/// <summary>Map matrix ("MM"): grid of map block (GR) indices, 0xFFFF = empty.</summary>
public sealed class Gen6MapMatrix
{
    public const int BlockCells = 40;
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<int> Blocks { get; }

    public Gen6MapMatrix(byte[] data)
    {
        var pack = Gen6Pack.Read(data, "MM");
        var head = pack.Part(0);
        if (head.Length < 8) throw new MapFormatException("Matriz sin cabecera.");
        Width = BinaryPrimitives.ReadUInt16LittleEndian(head[4..]);
        Height = BinaryPrimitives.ReadUInt16LittleEndian(head[6..]);
        int area = Width * Height;
        if (area == 0 || 8 + (area * 2) > head.Length) throw new MapFormatException($"Matriz {Width}×{Height} no cabe en sus datos.");
        var blocks = new int[area];
        for (int i = 0; i < area; i++)
        {
            int v = BinaryPrimitives.ReadUInt16LittleEndian(head[(8 + (i * 2))..]);
            blocks[i] = v == 0xFFFF ? -1 : v;
        }
        Blocks = blocks;
    }

    public MapLayout ToLayout() => new(Width, Height, BlockCells, Blocks);
}

/// <summary>
/// Map block ("GR"): part 0 is the 40×40 permission grid (u16 width, u16 height,
/// u32 per cell), part 1 the BCH model and part 2 the collision mesh.
/// </summary>
public sealed class Gen6MapBlock
{
    public Gen6Pack Pack { get; }
    public MapCellGrid Cells { get; }

    public Gen6MapBlock(byte[] data)
    {
        Pack = Gen6Pack.Read(data, "GR");
        if (Pack.Count < 3) throw new MapFormatException($"Bloque GR con {Pack.Count} partes.");
        var grid = Pack.Part(0);
        int w = BinaryPrimitives.ReadUInt16LittleEndian(grid), h = BinaryPrimitives.ReadUInt16LittleEndian(grid[2..]);
        if (w != Gen6MapMatrix.BlockCells || h != Gen6MapMatrix.BlockCells || 4 + (w * h * 4) > grid.Length)
            throw new MapFormatException($"Rejilla de permisos {w}×{h} inesperada.");
        var cells = new uint[w * h];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = BinaryPrimitives.ReadUInt32LittleEndian(grid[(4 + (i * 4))..]);
        Cells = new MapCellGrid(w, h, cells);
    }

    public ReadOnlySpan<byte> Model => Pack.Part(1);
}
