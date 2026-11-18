#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using pk3DS.Core.Maps.Ctr;

namespace pk3DS.Core.Maps.Gen6;

/// <summary>Decoded texture ready to sample (BGRA, first row at the top), with box-filtered mip levels built on demand.</summary>
public sealed record DecodedTexture(int Width, int Height, byte[] Pixels)
{
    private readonly List<DecodedTexture> _mips = [];
    private readonly object _gate = new();
    private bool? _ramp;

    /// <summary>
    /// True for a colour ramp: in every channel each row is almost uniform (mean deviation under 8)
    /// while some channel changes a lot from top to bottom (range over 96). Ramps are lookup tables
    /// for the 3DS texture combiner (e.g. the USUM sea gradient), not images to paint with UV coordinates.
    /// </summary>
    public bool IsRamp
    {
        get
        {
            lock (_gate)
            {
                if (_ramp is bool known) return known;
                bool ramp = Width >= 8 && Height >= 8;
                double worstRow = 0, widest = 0;
                for (int c = 0; c < 3 && ramp; c++)
                {
                    double deviation = 0, lo = 255, hi = 0;
                    for (int y = 0; y < Height; y++)
                    {
                        double sum = 0, sq = 0;
                        for (int x = 0; x < Width; x++) { int v = Pixels[(((y * Width) + x) * 4) + c]; sum += v; sq += (double)v * v; }
                        double mean = sum / Width;
                        deviation += Math.Sqrt(Math.Max(0, (sq / Width) - (mean * mean)));
                        lo = Math.Min(lo, mean); hi = Math.Max(hi, mean);
                    }
                    worstRow = Math.Max(worstRow, deviation / Height); widest = Math.Max(widest, hi - lo);
                }
                _ramp = ramp && worstRow < 8 && widest > 96;
                return _ramp.Value;
            }
        }
    }

    /// <summary>Mip level <paramref name="level"/> (0 = this texture); each level averages 2×2 texels of the previous one.</summary>
    public DecodedTexture Level(int level)
    {
        if (level <= 0) return this;
        lock (_gate)
        {
            while (_mips.Count < level)
            {
                var src = _mips.Count == 0 ? this : _mips[^1];
                if (src.Width <= 1 && src.Height <= 1) break;
                int w = Math.Max(1, src.Width / 2), h = Math.Max(1, src.Height / 2);
                var px = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        for (int c = 0; c < 4; c++)
                        {
                            int sum = 0;
                            for (int dy = 0; dy < 2; dy++)
                                for (int dx = 0; dx < 2; dx++)
                                    sum += src.Pixels[((Math.Min(src.Height - 1, (y * 2) + dy) * src.Width) + Math.Min(src.Width - 1, (x * 2) + dx)) * 4 + c];
                            px[((y * w) + x) * 4 + c] = (byte)(sum / 4);
                        }
                _mips.Add(new DecodedTexture(w, h, px));
            }
            return _mips.Count == 0 ? this : _mips[Math.Min(level, _mips.Count) - 1];
        }
    }
}

/// <summary>Result of drawing one block.</summary>
public sealed record BlockRender(int Size, byte[] Pixels, int Triangles, int Untextured, IReadOnlyList<string> MissingTextures);

/// <summary>
/// Orthographic top-down software rasteriser for a Gen6 map block. A block
/// model is centred on the origin and spans 40 cells of 18 units (−360…360 on
/// X and Z); the highest surface wins. Material choice is approximate: the
/// UV-mapped texture whose name matches the material, else the first UV-mapped
/// one the area provides; vertex colours are multiplied in and texels with low
/// alpha are discarded. Lighting, blending and animation are not reproduced.
/// </summary>
public static class Gen6TopDownRenderer
{
    public const float Half = 360f;

    public static BlockRender Render(H3dModel model, Func<string, DecodedTexture?> texture, int size) =>
        Render(model, texture, size, -Half, -Half, 2 * Half);

    /// <summary>Draws the square of the XZ plane that starts at (<paramref name="minX"/>, <paramref name="minZ"/>) and spans <paramref name="span"/> units.</summary>
    public static BlockRender Render(H3dModel model, Func<string, DecodedTexture?> texture, int size, float minX, float minZ, float span) =>
        Render([model], texture, size, minX, minZ, span);

    /// <summary>Several models drawn into the same image (Gen7 worlds are split in models placed in world coordinates).</summary>
    public static BlockRender Render(IReadOnlyList<H3dModel> models, Func<string, DecodedTexture?> texture, int size, float minX, float minZ, float span)
    {
        var colour = new byte[size * size * 4];
        var depth = new float[size * size];
        Array.Fill(depth, float.NegativeInfinity);
        int triangles = 0, untextured = 0;
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        float scale = size / span;
        // Pass 0: opaque and alpha-blended materials; pass 1: additive materials (highlights), which only add light on top.
        // An additive mesh adds once per pixel, even where two of its triangles share an edge.
        var addedBy = new int[size * size];
        int meshStamp = 0;
        for (int pass = 0; pass < 2; pass++)
        foreach (var model in models)
        foreach (var mesh in model.Meshes)
        {
            var material = model.Materials[mesh.Material];
            if (material.Additive != (pass == 1)) continue;
            meshStamp++;
            var pos = mesh.Read(PicaAttribute.Position);
            if (pos is null) continue;
            int slot = PickSlot(material, texture, missing);
            DecodedTexture? tex = slot >= 0 ? texture(material.TextureNames[slot]) : null;
            var uvs = slot >= 0 ? mesh.Read(PicaAttribute.TexCoord0 + slot) ?? mesh.Read(PicaAttribute.TexCoord0) : null;
            var colours = mesh.Read(PicaAttribute.Color);
            var coord = slot >= 0 ? material.Coords[slot] : null;
            var world = new Vector3[pos.Length];
            for (int i = 0; i < pos.Length; i++) world[i] = Vector3.Transform(new Vector3(pos[i].X, pos[i].Y, pos[i].Z), model.World);
            var uv = new Vector2[pos.Length];
            if (uvs is not null && coord is not null)
                for (int i = 0; i < pos.Length && i < uvs.Length; i++) uv[i] = coord.Apply(new Vector2(uvs[i].X, uvs[i].Y));
            foreach (var (a, b, c) in mesh.Triangles())
            {
                if (a >= world.Length || b >= world.Length || c >= world.Length) continue;
                triangles++;
                if (tex is null) untextured++;
                Raster(world[a], world[b], world[c], uv[a], uv[b], uv[c],
                    colours is null ? Vector4.One : colours[a], colours is null ? Vector4.One : colours[b], colours is null ? Vector4.One : colours[c],
                    tex, size, scale, minX, minZ, colour, depth, material.Additive ? addedBy : null, meshStamp);
            }
        }
        return new BlockRender(size, colour, triangles, untextured, [.. missing]);
    }

    private static int PickSlot(H3dMaterial material, Func<string, DecodedTexture?> texture, ISet<string> missing)
    {
        int first = -1, named = -1, ramp = -1;
        for (int i = 0; i < 3; i++)
        {
            string name = material.TextureNames[i];
            if (name.Length == 0 || material.Coords[i].MappingType != 0) continue;
            if (texture(name) is not { } tex) { missing.Add(name); continue; }
            if (tex.IsRamp) { if (ramp < 0) ramp = i; continue; } // lookup table: only used when nothing else is available
            if (named < 0 && string.Equals(name, material.Name, StringComparison.Ordinal)) named = i;
            if (first < 0) first = i;
        }
        return named >= 0 ? named : first >= 0 ? first : ramp;
    }

    private static void Raster(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 t0, Vector2 t1, Vector2 t2, Vector4 c0, Vector4 c1, Vector4 c2,
        DecodedTexture? tex, int size, float scale, float originX, float originZ, byte[] colour, float[] depth, int[]? addedBy = null, int stamp = 0)
    {
        float x0 = (p0.X - originX) * scale, y0 = (p0.Z - originZ) * scale;
        float x1 = (p1.X - originX) * scale, y1 = (p1.Z - originZ) * scale;
        float x2 = (p2.X - originX) * scale, y2 = (p2.Z - originZ) * scale;
        float area = ((x1 - x0) * (y2 - y0)) - ((x2 - x0) * (y1 - y0));
        if (MathF.Abs(area) < 1e-6f) return; // vertical faces are invisible from above
        if (tex is not null)
        {
            // Orthographic view: UV is affine on screen, so one level of detail per triangle (texels per pixel).
            float dudx = (((t1.X - t0.X) * (y2 - y0)) - ((t2.X - t0.X) * (y1 - y0))) / area, dudy = (((t2.X - t0.X) * (x1 - x0)) - ((t1.X - t0.X) * (x2 - x0))) / area;
            float dvdx = (((t1.Y - t0.Y) * (y2 - y0)) - ((t2.Y - t0.Y) * (y1 - y0))) / area, dvdy = (((t2.Y - t0.Y) * (x1 - x0)) - ((t1.Y - t0.Y) * (x2 - x0))) / area;
            float footprint = MathF.Max(MathF.Max(MathF.Abs(dudx), MathF.Abs(dudy)) * tex.Width, MathF.Max(MathF.Abs(dvdx), MathF.Abs(dvdy)) * tex.Height);
            if (footprint > 1.5f) tex = tex.Level((int)MathF.Floor(MathF.Log2(footprint)));
        }
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(x0, MathF.Min(x1, x2)))), maxX = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(x0, MathF.Max(x1, x2))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(y0, MathF.Min(y1, y2)))), maxY = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(y0, MathF.Max(y1, y2))));
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = (((x1 - px) * (y2 - py)) - ((x2 - px) * (y1 - py))) / area;
                float w1 = (((x2 - px) * (y0 - py)) - ((x0 - px) * (y2 - py))) / area;
                float w2 = 1 - w0 - w1;
                if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                float h = (w0 * p0.Y) + (w1 * p1.Y) + (w2 * p2.Y);
                int i = (y * size) + x;
                if (h < depth[i]) continue;
                var vc = (c0 * w0) + (c1 * w1) + (c2 * w2);
                float r = 0.55f, g = 0.6f, b = 0.55f, a = 1;
                if (tex is not null)
                {
                    var t = (t0 * w0) + (t1 * w1) + (t2 * w2);
                    int tx = Wrap((int)MathF.Floor(t.X * tex.Width), tex.Width), ty = Wrap((int)MathF.Floor((1 - t.Y) * tex.Height), tex.Height);
                    int s = ((ty * tex.Width) + tx) * 4;
                    // Nearly transparent texels are cut out; partial alpha (terrain layer masks) is blended over what is below.
                    if (tex.Pixels[s + 3] < 16) continue;
                    b = tex.Pixels[s] / 255f; g = tex.Pixels[s + 1] / 255f; r = tex.Pixels[s + 2] / 255f; a = tex.Pixels[s + 3] / 255f;
                }
                int o = i * 4;
                float nb = b * vc.Z, ng = g * vc.Y, nr = r * vc.X;
                if (addedBy is not null)
                {
                    // Additive (destination factor ONE): light added over what is already drawn; never hides it.
                    if (colour[o + 3] == 0 || addedBy[i] == stamp) continue;
                    addedBy[i] = stamp;
                    colour[o] = Byte((colour[o] / 255f) + (nb * a)); colour[o + 1] = Byte((colour[o + 1] / 255f) + (ng * a)); colour[o + 2] = Byte((colour[o + 2] / 255f) + (nr * a));
                    continue;
                }
                if (a < 0.98f && colour[o + 3] != 0)
                {
                    nb = (nb * a) + (colour[o] / 255f * (1 - a)); ng = (ng * a) + (colour[o + 1] / 255f * (1 - a)); nr = (nr * a) + (colour[o + 2] / 255f * (1 - a));
                }
                if (a >= 0.5f) depth[i] = h;
                colour[o] = Byte(nb); colour[o + 1] = Byte(ng); colour[o + 2] = Byte(nr); colour[o + 3] = 0xFF;
            }
    }

    private static int Wrap(int v, int n) => ((v % n) + n) % n;
    private static byte Byte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
}
