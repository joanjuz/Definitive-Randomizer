#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using pk3DS.Core.Maps.Ctr;

namespace pk3DS.Core.Maps.Gen6;

/// <summary>Decoded texture ready to sample (BGRA, first row at the top).</summary>
public sealed record DecodedTexture(int Width, int Height, byte[] Pixels);

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

    public static BlockRender Render(H3dModel model, Func<string, DecodedTexture?> texture, int size)
    {
        var colour = new byte[size * size * 4];
        var depth = new float[size * size];
        Array.Fill(depth, float.NegativeInfinity);
        int triangles = 0, untextured = 0;
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        float scale = size / (2 * Half);
        foreach (var mesh in model.Meshes)
        {
            var pos = mesh.Read(PicaAttribute.Position);
            if (pos is null) continue;
            var material = model.Materials[mesh.Material];
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
                    tex, size, scale, colour, depth);
            }
        }
        return new BlockRender(size, colour, triangles, untextured, [.. missing]);
    }

    private static int PickSlot(H3dMaterial material, Func<string, DecodedTexture?> texture, ISet<string> missing)
    {
        int first = -1;
        for (int i = 0; i < 3; i++)
        {
            string name = material.TextureNames[i];
            if (name.Length == 0 || material.Coords[i].MappingType != 0) continue;
            if (texture(name) is null) { missing.Add(name); continue; }
            if (string.Equals(name, material.Name, StringComparison.Ordinal)) return i;
            if (first < 0) first = i;
        }
        return first;
    }

    private static void Raster(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 t0, Vector2 t1, Vector2 t2, Vector4 c0, Vector4 c1, Vector4 c2,
        DecodedTexture? tex, int size, float scale, byte[] colour, float[] depth)
    {
        float x0 = (p0.X + Half) * scale, y0 = (p0.Z + Half) * scale;
        float x1 = (p1.X + Half) * scale, y1 = (p1.Z + Half) * scale;
        float x2 = (p2.X + Half) * scale, y2 = (p2.Z + Half) * scale;
        float area = ((x1 - x0) * (y2 - y0)) - ((x2 - x0) * (y1 - y0));
        if (MathF.Abs(area) < 1e-6f) return; // vertical faces are invisible from above
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
                float r = 0.55f, g = 0.6f, b = 0.55f;
                if (tex is not null)
                {
                    var t = (t0 * w0) + (t1 * w1) + (t2 * w2);
                    int tx = Wrap((int)MathF.Floor(t.X * tex.Width), tex.Width), ty = Wrap((int)MathF.Floor((1 - t.Y) * tex.Height), tex.Height);
                    int s = ((ty * tex.Width) + tx) * 4;
                    if (tex.Pixels[s + 3] < 128) continue;
                    b = tex.Pixels[s] / 255f; g = tex.Pixels[s + 1] / 255f; r = tex.Pixels[s + 2] / 255f;
                }
                depth[i] = h;
                int o = i * 4;
                colour[o] = Byte(b * vc.Z); colour[o + 1] = Byte(g * vc.Y); colour[o + 2] = Byte(r * vc.X); colour[o + 3] = 0xFF;
            }
    }

    private static int Wrap(int v, int n) => ((v % n) + n) % n;
    private static byte Byte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
}
