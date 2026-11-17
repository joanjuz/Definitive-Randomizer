#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace pk3DS.Core.NDS.Nitro;

/// <summary>A model to draw with a transform (model space → map units) and the texture set its materials bind to by name.</summary>
public sealed record NitroPlacedModel(NitroModel Model, Matrix4x4 Transform, NitroTextureSet? Textures);

/// <summary>
/// Orthographic top-down software rasterizer: X (east) and Z (south) map to the image, Y (up) is the depth, and the highest surface wins — what is seen
/// from straight above, not the game's tilted camera. Textures are sampled nearest-neighbour with the DS repeat/flip flags; texels with alpha 0 are
/// skipped; vertex colours modulate when the material uses them. No lighting, fog, edge marking or animation.
/// </summary>
public sealed class NitroTopDownRenderer
{
    private readonly int _width, _height; private readonly float _originX, _originZ, _scale;
    private readonly uint[] _pixels; private readonly float[] _depth;
    private readonly Dictionary<(NitroTextureSet, string, string?), (NitroImage Image, NitroTextureInfo Info)?> _cache = new();
    public int Width => _width; public int Height => _height;
    public uint[] Pixels => _pixels;
    /// <summary>Triangles drawn and triangles whose texture or palette was not found (drawn flat with the material colour, never invented).</summary>
    public int Drawn { get; private set; }
    public int Untextured { get; private set; }

    /// <param name="originX">Map units at the left edge.</param><param name="originZ">Map units at the top edge.</param><param name="pixelsPerUnit">Scale.</param>
    public NitroTopDownRenderer(int width, int height, float originX, float originZ, float pixelsPerUnit, uint background = 0)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 64L * 1024 * 1024 || pixelsPerUnit <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _width = width; _height = height; _originX = originX; _originZ = originZ; _scale = pixelsPerUnit;
        _pixels = new uint[width * height]; _depth = new float[width * height];
        Array.Fill(_pixels, background); Array.Fill(_depth, float.NegativeInfinity);
    }

    public void Draw(NitroPlacedModel placed)
    {
        foreach (var t in placed.Model.Triangles)
        {
            var material = t.Material < placed.Model.Materials.Count ? placed.Model.Materials[t.Material] : null;
            var texture = material is { Texture: { } name } && placed.Textures is { } set ? Texture(set, name, material.Palette) : null;
            if (material is { Texture: not null } && texture is null) Untextured++;
            Triangle(Vector3.Transform(t.A, placed.Transform), Vector3.Transform(t.B, placed.Transform), Vector3.Transform(t.C, placed.Transform), t.Ua, t.Ub, t.Uc, t.Ca, t.Cb, t.Cc, material, texture);
        }
    }

    private (NitroImage Image, NitroTextureInfo Info)? Texture(NitroTextureSet set, string name, string? palette)
    {
        if (_cache.TryGetValue((set, name, palette), out var hit)) return hit;
        (NitroImage, NitroTextureInfo)? result = null;
        if (set.Texture(name) is { } info)
        {
            var pal = palette is null ? null : set.Palette(palette);
            if (info.Format == NitroTextureFormat.Direct || pal is not null)
                try { result = (set.Decode(info, pal), info); } catch (System.IO.InvalidDataException) { result = null; }
        }
        return _cache[(set, name, palette)] = result;
    }

    private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, uint ca, uint cb, uint cc, NitroMaterial? material, (NitroImage Image, NitroTextureInfo Info)? texture)
    {
        float ax = (a.X - _originX) * _scale, az = (a.Z - _originZ) * _scale, bx = (b.X - _originX) * _scale, bz = (b.Z - _originZ) * _scale, cx = (c.X - _originX) * _scale, cz = (c.Z - _originZ) * _scale;
        float area = (bx - ax) * (cz - az) - (bz - az) * (cx - ax);
        if (MathF.Abs(area) < 1e-6f) return;
        int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx)))), x1 = Math.Min(_width - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
        int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(az, MathF.Min(bz, cz)))), y1 = Math.Min(_height - 1, (int)MathF.Ceiling(MathF.Max(az, MathF.Max(bz, cz))));
        if (x0 > x1 || y0 > y1) return;
        Drawn++;
        bool vertexColour = material?.UsesVertexColour == true; uint flat = material?.Diffuse ?? 0xFFC0C0C0;
        int alpha = material?.Alpha is int al and > 0 ? al * 255 / 31 : 255;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, pz = y + 0.5f;
                float w0 = ((bx - px) * (cz - pz) - (bz - pz) * (cx - px)) / area, w1 = ((cx - px) * (az - pz) - (cz - pz) * (ax - px)) / area, w2 = 1 - w0 - w1;
                if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                float depth = w0 * a.Y + w1 * b.Y + w2 * c.Y; int i = y * _width + x;
                if (depth < _depth[i]) continue;
                uint colour;
                if (texture is { } tex)
                {
                    float s = (w0 * ua.X + w1 * ub.X + w2 * uc.X) * (material?.ScaleS ?? 1), t = (w0 * ua.Y + w1 * ub.Y + w2 * uc.Y) * (material?.ScaleT ?? 1);
                    colour = Sample(tex.Image, tex.Info, s, t);
                    if (colour >> 24 == 0) continue;
                }
                else colour = flat;
                if (vertexColour) colour = Modulate(colour, Blend(ca, cb, cc, w0, w1, w2));
                if (alpha < 255) colour = Over(colour, _pixels[i], alpha);
                _pixels[i] = colour | 0xFF000000; _depth[i] = depth;
            }
    }

    private static uint Sample(NitroImage image, NitroTextureInfo info, float s, float t)
    {
        int x = Wrap((int)MathF.Floor(s), image.Width, info.RepeatS, info.FlipS), y = Wrap((int)MathF.Floor(t), image.Height, info.RepeatT, info.FlipT);
        return image.Pixels[y * image.Width + x];
    }
    private static int Wrap(int v, int size, bool repeat, bool flip)
    {
        if (!repeat) return Math.Clamp(v, 0, size - 1);
        if (!flip) return ((v % size) + size) % size;
        int period = size * 2, m = ((v % period) + period) % period; return m < size ? m : period - 1 - m;
    }
    private static uint Blend(uint a, uint b, uint c, float wa, float wb, float wc)
    {
        uint Ch(int shift) => (uint)Math.Clamp((int)(((a >> shift) & 0xFF) * wa + ((b >> shift) & 0xFF) * wb + ((c >> shift) & 0xFF) * wc), 0, 255) << shift;
        return 0xFF000000u | Ch(16) | Ch(8) | Ch(0);
    }
    private static uint Modulate(uint x, uint y)
    {
        uint Ch(int shift) => (((x >> shift) & 0xFF) * ((y >> shift) & 0xFF) / 255) << shift;
        return (x & 0xFF000000) | Ch(16) | Ch(8) | Ch(0);
    }
    private static uint Over(uint top, uint bottom, int alpha)
    {
        uint Ch(int shift) => (uint)((((top >> shift) & 0xFF) * alpha + ((bottom >> shift) & 0xFF) * (255 - alpha)) / 255) << shift;
        return 0xFF000000u | Ch(16) | Ch(8) | Ch(0);
    }
}
