#nullable enable
using System;
using System.Buffers.Binary;

namespace pk3DS.Core.Maps.Ctr;

/// <summary>
/// Decoder of PICA200 textures: 8×8 tiles in Morton order, stored bottom-up,
/// and ETC1/ETC1A4 in 4×4 blocks grouped by tile.
/// </summary>
public static class PicaTexture
{
    private static readonly int[] Bpp = [32, 24, 16, 16, 16, 16, 16, 8, 8, 8, 4, 4, 4, 8];
    private static readonly int[] Swizzle =
    [
        0, 1, 8, 9, 2, 3, 10, 11, 16, 17, 24, 25, 18, 19, 26, 27,
        4, 5, 12, 13, 6, 7, 14, 15, 20, 21, 28, 29, 22, 23, 30, 31,
        32, 33, 40, 41, 34, 35, 42, 43, 48, 49, 56, 57, 50, 51, 58, 59,
        36, 37, 44, 45, 38, 39, 46, 47, 52, 53, 60, 61, 54, 55, 62, 63,
    ];
    private static readonly int[,] Etc1 =
    {
        { 2, 8, -2, -8 }, { 5, 17, -5, -17 }, { 9, 29, -9, -29 }, { 13, 42, -13, -42 },
        { 18, 60, -18, -60 }, { 24, 80, -24, -80 }, { 33, 106, -33, -106 }, { 47, 183, -47, -183 },
    };

    public static int Length(int width, int height, PicaTextureFormat format) =>
        (int)format >= Bpp.Length || width % 8 != 0 || height % 8 != 0 ? -1 : width * height * Bpp[(int)format] / 8;

    /// <summary>BGRA pixels, first row at the top.</summary>
    public static byte[] Decode(byte[] input, int width, int height, PicaTextureFormat format)
    {
        int length = Length(width, height, format);
        if (length < 0 || input.Length < length) throw new MapFormatException($"Textura {format} {width}×{height} incompleta.");
        var output = new byte[width * height * 4];
        if (format is PicaTextureFormat.ETC1 or PicaTextureFormat.ETC1A4) { DecodeEtc1(input, width, height, format == PicaTextureFormat.ETC1A4, output); return output; }
        int inc = Math.Max(1, Bpp[(int)format] / 8), io = 0;
        for (int ty = 0; ty < height; ty += 8)
            for (int tx = 0; tx < width; tx += 8)
                for (int px = 0; px < 64; px++, io += inc)
                {
                    int x = Swizzle[px] & 7, y = Swizzle[px] >> 3;
                    int o = (tx + x + ((height - 1 - (ty + y)) * width)) * 4;
                    byte b, g, r, a = 0xFF;
                    switch (format)
                    {
                        case PicaTextureFormat.RGBA8: a = input[io]; b = input[io + 1]; g = input[io + 2]; r = input[io + 3]; break;
                        case PicaTextureFormat.RGB8: b = input[io]; g = input[io + 1]; r = input[io + 2]; break;
                        case PicaTextureFormat.RGBA5551:
                        {
                            int v = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(io));
                            r = X5((v >> 11) & 31); g = X5((v >> 6) & 31); b = X5((v >> 1) & 31); a = (byte)((v & 1) * 0xFF); break;
                        }
                        case PicaTextureFormat.RGB565:
                        {
                            int v = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(io));
                            r = X5((v >> 11) & 31); g = (byte)((((v >> 5) & 63) << 2) | (((v >> 5) & 63) >> 4)); b = X5(v & 31); break;
                        }
                        case PicaTextureFormat.RGBA4:
                        {
                            int v = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(io));
                            r = X4((v >> 12) & 15); g = X4((v >> 8) & 15); b = X4((v >> 4) & 15); a = X4(v & 15); break;
                        }
                        case PicaTextureFormat.LA8: r = g = b = input[io + 1]; a = input[io]; break;
                        case PicaTextureFormat.HiLo8: r = input[io + 1]; g = input[io]; b = 0; break;
                        case PicaTextureFormat.L8: r = g = b = input[io]; break;
                        case PicaTextureFormat.A8: r = g = b = 0xFF; a = input[io]; break;
                        case PicaTextureFormat.LA4: r = g = b = X4(input[io] >> 4); a = X4(input[io] & 15); break;
                        case PicaTextureFormat.L4: r = g = b = X4((input[io >> 1] >> ((io & 1) << 2)) & 15); break;
                        default: r = g = b = 0xFF; a = X4((input[io >> 1] >> ((io & 1) << 2)) & 15); break; // A4
                    }
                    output[o] = b; output[o + 1] = g; output[o + 2] = r; output[o + 3] = a;
                }
        return output;
    }

    private static byte X5(int v) => (byte)((v << 3) | (v >> 2));
    private static byte X4(int v) => (byte)((v << 4) | v);

    private static void DecodeEtc1(byte[] input, int width, int height, bool alpha, byte[] output)
    {
        int io = 0;
        Span<byte> tile = stackalloc byte[48];
        for (int ty = 0; ty < height; ty += 8)
            for (int tx = 0; tx < width; tx += 8)
                for (int t = 0; t < 4; t++)
                {
                    ulong alphaBlock = ulong.MaxValue;
                    if (alpha) { alphaBlock = BinaryPrimitives.ReadUInt64LittleEndian(input.AsSpan(io)); io += 8; }
                    ulong color = BinaryPrimitives.ReadUInt64LittleEndian(input.AsSpan(io)); io += 8;
                    EtcBlock(color, tile);
                    int bx = (t & 1) * 4, by = (t >> 1) * 4;
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int o = (((height - 1 - (ty + by + py)) * width) + tx + bx + px) * 4, s = ((py * 4) + px) * 3;
                            output[o] = tile[s]; output[o + 1] = tile[s + 1]; output[o + 2] = tile[s + 2];
                            int shift = ((px * 4) + py) << 2;
                            output[o + 3] = X4((int)((alphaBlock >> shift) & 15));
                        }
                }
    }

    /// <summary>One ETC1 4×4 block (little-endian on 3DS) into BGR triples, row-major.</summary>
    private static void EtcBlock(ulong block, Span<byte> bgr)
    {
        uint low = (uint)block, high = (uint)(block >> 32);
        // Standard ETC1 word: colours in bits 63..32, then table 1 (39..37), table 2 (36..34), diff (33), flip (32).
        bool flip = (high & 1) != 0, diff = (high & 2) != 0;
        int r1, g1, b1, r2, g2, b2;
        if (diff)
        {
            int rb = (int)((high >> 27) & 31), gb = (int)((high >> 19) & 31), bb = (int)((high >> 11) & 31);
            int rd = (int)((high >> 24) & 7), gd = (int)((high >> 16) & 7), bd = (int)((high >> 8) & 7);
            rd = (rd << 29) >> 29; gd = (gd << 29) >> 29; bd = (bd << 29) >> 29;
            int R2 = rb + rd, G2 = gb + gd, B2 = bb + bd;
            r1 = (rb << 3) | (rb >> 2); g1 = (gb << 3) | (gb >> 2); b1 = (bb << 3) | (bb >> 2);
            r2 = ((R2 & 31) << 3) | ((R2 & 31) >> 2); g2 = ((G2 & 31) << 3) | ((G2 & 31) >> 2); b2 = ((B2 & 31) << 3) | ((B2 & 31) >> 2);
        }
        else
        {
            r1 = (int)((high >> 28) & 15) * 17; g1 = (int)((high >> 20) & 15) * 17; b1 = (int)((high >> 12) & 15) * 17;
            r2 = (int)((high >> 24) & 15) * 17; g2 = (int)((high >> 16) & 15) * 17; b2 = (int)((high >> 8) & 15) * 17;
        }
        int table1 = (int)((high >> 5) & 7), table2 = (int)((high >> 2) & 7);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                bool second = flip ? y >= 2 : x >= 2;
                int index = (x * 4) + y;
                int lsb = (int)((low >> index) & 1), msb = (int)((low >> (index + 16)) & 1);
                int delta = Etc1[second ? table2 : table1, lsb + (msb * 2)];
                int s = ((y * 4) + x) * 3;
                bgr[s] = Clamp((second ? b2 : b1) + delta); bgr[s + 1] = Clamp((second ? g2 : g1) + delta); bgr[s + 2] = Clamp((second ? r2 : r1) + delta);
            }
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
}
