#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using pk3DS.Core.Maps.Ctr;

namespace pk3DS.Core.Maps.Gen7;

/// <summary>One collision triangle with its surface attribute.</summary>
public readonly record struct Gen7CollisionTriangle(Vector3 A, Vector3 B, Vector3 C, uint Attribute);

/// <summary>
/// Collision mesh of a Gen7 terrain block (part 6 = walkable ground, part 7 = walls):
/// u32 0x14120500, u32 triangle count, then 72-byte triangles with three float3
/// vertices at +0, +16 and +32 and the surface attribute u32 at +64.
/// Layout cross-checked against USUM-Studio (Aqua-0, GPL-3.0); read-only.
/// </summary>
public static class Gen7Collision
{
    public const uint Magic = 0x14120500;
    public const int TriangleSize = 72;

    public static IReadOnlyList<Gen7CollisionTriangle> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic) throw new MapFormatException("No es una malla de colisión Gen7.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        if (count < 0 || 8 + ((long)count * TriangleSize) > data.Length) throw new MapFormatException($"Malla de colisión con {count} triángulos no cabe.");
        var list = new List<Gen7CollisionTriangle>(count);
        Vector3 P(ReadOnlySpan<byte> s) => new(BinaryPrimitives.ReadSingleLittleEndian(s), BinaryPrimitives.ReadSingleLittleEndian(s[4..]), BinaryPrimitives.ReadSingleLittleEndian(s[8..]));
        for (int i = 0; i < count; i++)
        {
            var t = data.Slice(8 + (i * TriangleSize), TriangleSize);
            var tri = new Gen7CollisionTriangle(P(t), P(t[16..]), P(t[32..]), BinaryPrimitives.ReadUInt32LittleEndian(t[64..]));
            if (!float.IsFinite(tri.A.X + tri.A.Y + tri.A.Z + tri.B.X + tri.B.Y + tri.B.Z + tri.C.X + tri.C.Y + tri.C.Z)) throw new MapFormatException("Vértice de colisión no finito.");
            list.Add(tri);
        }
        return list;
    }

    /// <summary>
    /// Flat model of the triangles for the diagnostic layer: no texture, one colour per
    /// attribute (so different surfaces are distinguishable). It is a diagnostic view of the
    /// collision data, not the game's ground graphics.
    /// </summary>
    public static H3dModel ToDiagnosticModel(IReadOnlyList<Gen7CollisionTriangle> triangles)
    {
        var attrs = new List<PicaAttribute> { new(PicaAttribute.Position, 3, 3, 1), new(PicaAttribute.Color, 1, 4, 1f / 255) };
        const int stride = 16;
        var chunks = new List<H3dMesh>();
        foreach (var batch in triangles.Chunk(20000))
        {
            var vertices = new byte[batch.Length * 3 * stride];
            var indices = new ushort[batch.Length * 3];
            int v = 0;
            foreach (var t in batch)
            {
                uint h = (t.Attribute + 1) * 2654435761u;
                byte r = (byte)(150 + (h & 0x5F)), g = (byte)(150 + ((h >> 8) & 0x5F)), b = (byte)(170 + ((h >> 16) & 0x4F));
                foreach (var p in new[] { t.A, t.B, t.C })
                {
                    var s = vertices.AsSpan(v * stride);
                    BinaryPrimitives.WriteSingleLittleEndian(s, p.X); BinaryPrimitives.WriteSingleLittleEndian(s[4..], p.Y); BinaryPrimitives.WriteSingleLittleEndian(s[8..], p.Z);
                    s[12] = r; s[13] = g; s[14] = b; s[15] = 255;
                    indices[v] = (ushort)(v % 60000);
                    v++;
                }
            }
            // Indices are local to the batch (at most 60000 vertices).
            chunks.Add(new H3dMesh(0, attrs, stride, Vector4.Zero, vertices, [new H3dSubMesh(0, indices)]));
        }
        var material = new H3dMaterial("collision", ["", "", ""], [new(0, 0, Vector2.One, 0, Vector2.Zero), new(0, 0, Vector2.One, 0, Vector2.Zero), new(0, 0, Vector2.One, 0, Vector2.Zero)]);
        return new H3dModel("collision", Matrix4x4.Identity, [material], chunks);
    }
}
