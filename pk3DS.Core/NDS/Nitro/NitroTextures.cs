#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS.Nitro;

/// <summary>DS texture formats (TEXIMAGE_PARAM bits 26–28).</summary>
public enum NitroTextureFormat { None = 0, A3I5 = 1, Palette4 = 2, Palette16 = 3, Palette256 = 4, Compressed4x4 = 5, A5I3 = 6, Direct = 7 }

/// <summary>One texture of a TEX0 block. <c>Offset</c> is in bytes inside the texel area of its kind (normal or 4x4 compressed).</summary>
public sealed record NitroTextureInfo(string Name, int Offset, int Width, int Height, NitroTextureFormat Format, bool Color0Transparent, bool RepeatS, bool RepeatT, bool FlipS, bool FlipT);
public sealed record NitroPaletteInfo(string Name, int Offset);

/// <summary>A decoded texture: <c>Width × Height</c> ARGB32 pixels, row by row.</summary>
public sealed record NitroImage(int Width, int Height, uint[] Pixels);

/// <summary>
/// Reader of a TEX0 block (inside a BTX0 file, or embedded in a BMD0). Layout per the Nitro SDK structures (NNSG3dResTex): texel area, 4x4 compressed texel
/// area and its palette-index area, palette area, and the texture and palette dictionaries. Decodes the seven DS formats to ARGB32. Read-only.
/// </summary>
public sealed class NitroTextureSet
{
    private readonly byte[] _d;
    private readonly int _texData, _texDataSize, _compData, _compDataSize, _compInfo, _palData, _palDataSize;
    public IReadOnlyList<NitroTextureInfo> Textures { get; }
    public IReadOnlyList<NitroPaletteInfo> Palettes { get; }

    public static NitroTextureSet FromBtx(byte[] file)
    {
        var nitro = NitroFile.Read(file, "BTX0");
        var block = nitro.Block("TEX0") ?? throw new InvalidDataException("El archivo BTX0 no tiene bloque TEX0.");
        return new NitroTextureSet(file, block.Offset);
    }
    /// <summary>The TEX0 block of a BMD0 model, or null when the model carries no textures.</summary>
    public static NitroTextureSet? FromBmd(byte[] file)
    {
        var nitro = NitroFile.Read(file, "BMD0");
        return nitro.Block("TEX0") is { } block ? new NitroTextureSet(file, block.Offset) : null;
    }

    public NitroTextureSet(byte[] data, int at)
    {
        _d = data; Nitro.Need(data, at, 0x3C);
        if (data[at] != 'T' || data[at + 1] != 'E' || data[at + 2] != 'X' || data[at + 3] != '0') throw new InvalidDataException("No es un bloque TEX0.");
        _texDataSize = Nitro.U16(data, at + 0x0C) << 3; int texDict = at + Nitro.U16(data, at + 0x0E); _texData = at + (int)Nitro.U32(data, at + 0x14);
        _compDataSize = Nitro.U16(data, at + 0x1C) << 3; _compData = at + (int)Nitro.U32(data, at + 0x24); _compInfo = at + (int)Nitro.U32(data, at + 0x28);
        _palDataSize = (int)(Nitro.U32(data, at + 0x30) << 3); int palDict = at + (int)Nitro.U32(data, at + 0x34); _palData = at + (int)Nitro.U32(data, at + 0x38);
        Nitro.Need(data, _texData, _texDataSize); Nitro.Need(data, _compData, _compDataSize); Nitro.Need(data, _compInfo, _compDataSize / 2); Nitro.Need(data, _palData, _palDataSize);
        Textures = Nitro.Dictionary(data, texDict, 8).Select(e =>
        {
            uint p = Nitro.U32(data, e.DataOffset);
            var format = (NitroTextureFormat)(p >> 26 & 7);
            return new NitroTextureInfo(e.Name, (int)(p & 0xFFFF) << 3, 8 << (int)(p >> 20 & 7), 8 << (int)(p >> 23 & 7), format, (p >> 29 & 1) != 0, (p >> 16 & 1) != 0, (p >> 17 & 1) != 0, (p >> 18 & 1) != 0, (p >> 19 & 1) != 0);
        }).ToList();
        Palettes = Nitro.Dictionary(data, palDict, 4).Select(e => new NitroPaletteInfo(e.Name, Nitro.U16(data, e.DataOffset) << 3)).ToList();
    }

    public NitroTextureInfo? Texture(string name) => Textures.FirstOrDefault(t => t.Name == name);
    public NitroPaletteInfo? Palette(string name) => Palettes.FirstOrDefault(p => p.Name == name);

    /// <summary>Bits per texel of the formats stored in the normal texel area (4x4 compressed: 2 bits plus 1 bit of index data).</summary>
    public static int BitsPerTexel(NitroTextureFormat f) => f switch
    {
        NitroTextureFormat.Palette4 => 2, NitroTextureFormat.Palette16 => 4, NitroTextureFormat.Compressed4x4 => 2,
        NitroTextureFormat.A3I5 or NitroTextureFormat.Palette256 or NitroTextureFormat.A5I3 => 8, NitroTextureFormat.Direct => 16, _ => 0,
    };
    private static int PaletteSize(NitroTextureFormat f) => f switch
    {
        NitroTextureFormat.Palette4 => 4, NitroTextureFormat.Palette16 => 16, NitroTextureFormat.Palette256 => 256, NitroTextureFormat.A3I5 => 32, NitroTextureFormat.A5I3 => 8, _ => 0,
    };

    /// <summary>Decodes a texture with a palette (ignored by the direct format). Texels or palette entries outside their areas throw, never read beyond.</summary>
    public NitroImage Decode(NitroTextureInfo t, NitroPaletteInfo? palette)
    {
        int w = t.Width, h = t.Height; var px = new uint[w * h];
        if (t.Format == NitroTextureFormat.None) throw new InvalidDataException($"La textura {t.Name} no tiene formato.");
        int bits = BitsPerTexel(t.Format); long bytes = (long)w * h * bits / 8;
        int area = t.Format == NitroTextureFormat.Compressed4x4 ? _compData : _texData, areaSize = t.Format == NitroTextureFormat.Compressed4x4 ? _compDataSize : _texDataSize;
        if (t.Offset + bytes > areaSize) throw new InvalidDataException($"La textura {t.Name} sale de su área de datos.");
        int texels = area + t.Offset;
        uint Color(int index)
        {
            if (palette is null) throw new InvalidDataException($"La textura {t.Name} necesita una paleta.");
            int at = palette.Offset + index * 2;
            if (at + 2 > _palDataSize) throw new InvalidDataException($"La paleta {palette.Name} es demasiado corta para {t.Name}.");
            return Nitro.Bgr555(Nitro.U16(_d, _palData + at));
        }
        uint WithAlpha(uint c, int alpha) => (uint)alpha << 24 | c & 0x00FFFFFF;
        switch (t.Format)
        {
            case NitroTextureFormat.Direct:
                for (int i = 0; i < w * h; i++) { int c = Nitro.U16(_d, texels + i * 2); px[i] = (c & 0x8000) == 0 ? 0 : Nitro.Bgr555(c); }
                break;
            case NitroTextureFormat.Palette4 or NitroTextureFormat.Palette16 or NitroTextureFormat.Palette256:
                {
                    int perByte = 8 / bits, mask = (1 << bits) - 1; var cache = new uint?[PaletteSize(t.Format)];
                    for (int i = 0; i < w * h; i++)
                    {
                        int index = _d[texels + i / perByte] >> (i % perByte * bits) & mask;
                        px[i] = index == 0 && t.Color0Transparent ? 0 : cache[index] ??= Color(index);
                    }
                    break;
                }
            case NitroTextureFormat.A3I5 or NitroTextureFormat.A5I3:
                {
                    int indexBits = t.Format == NitroTextureFormat.A3I5 ? 5 : 3, alphaBits = 8 - indexBits;
                    for (int i = 0; i < w * h; i++)
                    {
                        int v = _d[texels + i], index = v & (1 << indexBits) - 1, a = v >> indexBits;
                        px[i] = WithAlpha(Color(index), a * 255 / ((1 << alphaBits) - 1));
                    }
                    break;
                }
            case NitroTextureFormat.Compressed4x4:
                {
                    if (palette is null) throw new InvalidDataException($"La textura {t.Name} necesita una paleta.");
                    int blocksWide = w / 4, info = _compInfo + t.Offset / 2;
                    for (int by = 0; by < h / 4; by++)
                        for (int bx = 0; bx < blocksWide; bx++)
                        {
                            int block = by * blocksWide + bx; uint rows = Nitro.U32(_d, texels + block * 4); int pi = Nitro.U16(_d, info + block * 2);
                            int mode = pi >> 14, baseIndex = (pi & 0x3FFF) * 2;   // palette offset in 4-byte units = 2 colours
                            uint c0 = Color(baseIndex), c1 = Color(baseIndex + 1);
                            uint[] colours = mode switch
                            {
                                0 => [c0, c1, Color(baseIndex + 2), 0],
                                1 => [c0, c1, Mix(c0, c1, 1, 1), 0],
                                2 => [c0, c1, Color(baseIndex + 2), Color(baseIndex + 3)],
                                _ => [c0, c1, Mix(c0, c1, 5, 3), Mix(c0, c1, 3, 5)],
                            };
                            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) px[(by * 4 + y) * w + bx * 4 + x] = colours[rows >> (y * 8 + x * 2) & 3];
                        }
                    break;
                }
        }
        return new(w, h, px);
    }
    private static uint Mix(uint a, uint b, int wa, int wb)
    {
        uint Channel(int shift) => (uint)((((a >> shift) & 0xFF) * wa + ((b >> shift) & 0xFF) * wb) / (wa + wb)) << shift;
        return 0xFF000000u | Channel(16) | Channel(8) | Channel(0);
    }
}
