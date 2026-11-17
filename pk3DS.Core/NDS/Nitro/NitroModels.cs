#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace pk3DS.Core.NDS.Nitro;

/// <summary>A material: the texture and palette bound to it by name, its alpha (0–31) and diffuse colour, and the S/T scale of its texture matrix.</summary>
public sealed record NitroMaterial(string Name, string? Texture, string? Palette, int Alpha, uint Diffuse, bool UsesVertexColour, float ScaleS, float ScaleT, int Width, int Height);

/// <summary>One triangle in model space (after node matrices and position scale). UV in texels of its material's texture; colours ARGB32.</summary>
public readonly record struct NitroTriangle(int Material, Vector3 A, Vector3 B, Vector3 C, Vector2 Ua, Vector2 Ub, Vector2 Uc, uint Ca, uint Cb, uint Cc);

/// <summary>A decoded model: its materials and every triangle of every shape drawn by its render bytecode.</summary>
public sealed record NitroModel(string Name, IReadOnlyList<NitroMaterial> Materials, IReadOnlyList<NitroTriangle> Triangles, int DeclaredVertices, int DeclaredPolygons)
{
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        if (Triangles.Count == 0) return (Vector3.Zero, Vector3.Zero);
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var t in Triangles) foreach (var v in new[] { t.A, t.B, t.C }) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        return (min, max);
    }
}

/// <summary>
/// Reader of BMD0 models (MDL0 block), following the Nitro SDK structures: model dictionary, model info (position scale), nodes, materials with their
/// texture/palette name bindings, the render bytecode (SBC) that places each shape with a node matrix, and the shapes' GX display lists (vertices,
/// texture coordinates, colours; triangles, quads and strips). Geometry only: no lighting, animation or texture matrices other than S/T scale. Read-only.
/// </summary>
public static class NitroModels
{
    public static IReadOnlyList<NitroModel> Read(byte[] file)
    {
        var nitro = NitroFile.Read(file, "BMD0");
        var (mdl, _) = nitro.Block("MDL0") ?? throw new InvalidDataException("El archivo BMD0 no tiene bloque MDL0.");
        return Nitro.Dictionary(file, mdl + 8, 4).Select(e => Model(file, e.Name, mdl + (int)Nitro.U32(file, e.DataOffset))).ToList();
    }

    private static NitroModel Model(byte[] d, string name, int m)
    {
        Nitro.Need(d, m, 0x40);
        int sbc = m + (int)Nitro.U32(d, m + 4), mat = m + (int)Nitro.U32(d, m + 8), shp = m + (int)Nitro.U32(d, m + 0x0C);
        int info = m + 0x14; float posScale = Nitro.I32(d, info + 8) / 4096f;
        int vertices = Nitro.U16(d, info + 0x10), polygons = Nitro.U16(d, info + 0x12);
        // Nodes: dictionary at m + 0x40 whose units are offsets relative to that dictionary.
        int nodeInfo = m + 0x40;
        var nodes = Nitro.Dictionary(d, nodeInfo, 4).Select(e => NodeMatrix(d, nodeInfo + (int)Nitro.U32(d, e.DataOffset))).ToList();
        var materials = Materials(d, mat);
        var shapes = Nitro.Dictionary(d, shp + 0, 4).Select(e => shp + (int)Nitro.U32(d, e.DataOffset)).ToList();
        var triangles = new List<NitroTriangle>();
        RunBytecode(d, sbc, nodes, shapes, posScale, triangles);
        return new(name, materials, triangles, vertices, polygons);
    }

    /// <summary>Local matrix of a node (NNSG3dResNodeData): optional translation, rotation (full 3×3 or pivot form) and scale.</summary>
    private static Matrix4x4 NodeMatrix(byte[] d, int at)
    {
        int flag = Nitro.U16(d, at); float m00 = Nitro.I16(d, at + 2) / 4096f; int p = at + 4;
        var translation = Vector3.Zero; var rotation = Matrix4x4.Identity; var scale = Vector3.One;
        if ((flag & 1) == 0) { translation = new(Nitro.I32(d, p) / 4096f, Nitro.I32(d, p + 4) / 4096f, Nitro.I32(d, p + 8) / 4096f); p += 12; }
        if ((flag & 2) == 0)
        {
            if ((flag & 8) == 0)
            {
                float F(int i) => Nitro.I16(d, p + i * 2) / 4096f;
                rotation = new Matrix4x4(m00, F(0), F(1), 0, F(2), F(3), F(4), 0, F(5), F(6), F(7), 0, 0, 0, 0, 1); p += 16;
            }
            else
            {
                // Pivot form: one axis is ±1 and the other two come from A and B with sign flags (NNS_G3D_SRTFLAG_PIVOT_*).
                float a = Nitro.I16(d, p) / 4096f, b = Nitro.I16(d, p + 2) / 4096f; p += 4;
                int pivot = flag >> 4 & 15, neg = flag >> 8 & 15;
                float one = (neg & 1) != 0 ? -1 : 1, c = (neg & 2) != 0 ? -b : b, dd = (neg & 4) != 0 ? -a : a;
                var r = new float[9]; r[pivot] = one;
                int row = pivot / 3, col = pivot % 3;
                var rest = Enumerable.Range(0, 9).Where(i => i / 3 != row && i % 3 != col).ToArray();
                r[rest[0]] = a; r[rest[1]] = b; r[rest[2]] = c; r[rest[3]] = dd;
                rotation = new Matrix4x4(r[0], r[1], r[2], 0, r[3], r[4], r[5], 0, r[6], r[7], r[8], 0, 0, 0, 0, 1);
            }
        }
        if ((flag & 4) == 0) { scale = new(Nitro.I32(d, p) / 4096f, Nitro.I32(d, p + 4) / 4096f, Nitro.I32(d, p + 8) / 4096f); }
        return Matrix4x4.CreateScale(scale) * rotation * Matrix4x4.CreateTranslation(translation);
    }

    private static List<NitroMaterial> Materials(byte[] d, int mat)
    {
        int texToMat = mat + Nitro.U16(d, mat), palToMat = mat + Nitro.U16(d, mat + 2);
        var entries = Nitro.Dictionary(d, mat + 4, 4);
        var texture = new string?[entries.Count]; var palette = new string?[entries.Count];
        void Bind(int dict, string?[] target)
        {
            foreach (var e in Nitro.Dictionary(d, dict, 4))
            {
                int list = mat + Nitro.U16(d, e.DataOffset), count = d[e.DataOffset + 2];
                for (int i = 0; i < count; i++) { int index = d[list + i]; if (index < target.Length) target[index] = e.Name; }
            }
        }
        Bind(texToMat, texture); Bind(palToMat, palette);
        var result = new List<NitroMaterial>();
        for (int i = 0; i < entries.Count; i++)
        {
            int at = mat + (int)Nitro.U32(d, entries[i].DataOffset); Nitro.Need(d, at, 0x2C);
            // NNSG3dResMatData: +4 diffuse/ambient, +0x0C polygon attributes, +0x14 TEXIMAGE_PARAM, +0x1C palette base, +0x1E flags, +0x20/+0x22 original size.
            uint difAmb = Nitro.U32(d, at + 4), polyAttr = Nitro.U32(d, at + 0x0C); int flag = Nitro.U16(d, at + 0x1E);
            int width = Nitro.U16(d, at + 0x20), height = Nitro.U16(d, at + 0x22);
            float sS = 1, sT = 1;
            if ((flag & 1) != 0 && (flag & 2) == 0) { sS = Nitro.I32(d, at + 0x2C) / 4096f; sT = Nitro.I32(d, at + 0x30) / 4096f; }
            result.Add(new(entries[i].Name, texture[i], palette[i], (int)(polyAttr >> 16 & 31), Nitro.Bgr555((int)(difAmb & 0x7FFF)), (difAmb & 0x8000) != 0, sS, sT, width, height));
        }
        return result;
    }

    /// <summary>Interprets the render bytecode: node matrices (with the matrix stack), material selection, position scale and shape drawing.</summary>
    private static void RunBytecode(byte[] d, int at, List<Matrix4x4> local, List<int> shapes, float posScale, List<NitroTriangle> output)
    {
        var world = new Matrix4x4[Math.Max(1, local.Count)]; for (int i = 0; i < world.Length; i++) world[i] = Matrix4x4.Identity;
        var stack = new Matrix4x4[32]; for (int i = 0; i < 32; i++) stack[i] = Matrix4x4.Identity;
        var current = Matrix4x4.Identity; int material = 0; bool visible = true;
        for (int guard = 0; guard < 100000; guard++)
        {
            Nitro.Need(d, at, 1); int op = d[at], cmd = op & 0x1F, opt = op >> 5;
            switch (cmd)
            {
                case 0x00: at += 1; break;
                case 0x01: return;
                case 0x02: visible = d[at + 2] == 1; at += 3; break;
                case 0x03: current = stack[d[at + 1] & 31]; at += 2; break;
                case 0x04: material = d[at + 1]; at += 2; break;
                case 0x05:
                    if (visible && d[at + 1] < shapes.Count) DrawShape(d, shapes[d[at + 1]], current, stack, posScale, material, output);
                    at += 2; break;
                case 0x06:
                    {
                        int node = d[at + 1], parent = d[at + 2], p = at + 4;
                        int? dst = opt is 1 or 3 ? d[p++] : null, src = opt is 2 or 3 ? d[p++] : null;
                        var parentMatrix = src is int s ? stack[s & 31] : node == parent || parent >= world.Length ? Matrix4x4.Identity : world[parent];
                        var mine = node < local.Count ? local[node] : Matrix4x4.Identity;
                        current = mine * parentMatrix; if (node < world.Length) world[node] = current;
                        if (dst is int t) stack[t & 31] = current;
                        at = p; break;
                    }
                case 0x07 or 0x08: at += 2 + (opt is 1 or 2 ? 1 : opt == 3 ? 2 : 0); break;
                case 0x09:
                    {
                        // Weighted matrix (skinning). Static map geometry does not use it; the first matrix is used as an approximation.
                        int dst = d[at + 1], count = d[at + 2]; if (count > 0) stack[dst & 31] = world[Math.Min(d[at + 4], world.Length - 1)];
                        at += 3 + count * 3; break;
                    }
                case 0x0A: at += 9; break;
                case 0x0B: at += 1; break;
                case 0x0C or 0x0D: at += 3; break;
                default: throw new InvalidDataException($"Instrucción SBC desconocida 0x{op:X2}.");
            }
        }
        throw new InvalidDataException("Código SBC sin final.");
    }

    private static readonly int[] ParamCount = BuildParamCount();
    private static int[] BuildParamCount()
    {
        var p = Enumerable.Repeat(-1, 256).ToArray(); p[0x00] = 0;
        foreach (var (c, n) in new[] { (0x10, 1), (0x11, 0), (0x12, 1), (0x13, 1), (0x14, 1), (0x15, 0), (0x16, 16), (0x17, 12), (0x18, 16), (0x19, 12), (0x1A, 9), (0x1B, 3), (0x1C, 3),
            (0x20, 1), (0x21, 1), (0x22, 1), (0x23, 2), (0x24, 1), (0x25, 1), (0x26, 1), (0x27, 1), (0x28, 1), (0x29, 1), (0x2A, 1), (0x2B, 1),
            (0x30, 1), (0x31, 1), (0x32, 1), (0x33, 1), (0x34, 32), (0x40, 1), (0x41, 0), (0x50, 1), (0x60, 1), (0x70, 3), (0x71, 2), (0x72, 1) }) p[c] = n;
        return p;
    }

    /// <summary>Runs a shape's GX display list (NNSG3dResShpData: offset 8 = display list, 12 = its size) and appends its triangles.</summary>
    private static void DrawShape(byte[] d, int shape, Matrix4x4 start, Matrix4x4[] stack, float posScale, int material, List<NitroTriangle> output)
    {
        int list = shape + (int)Nitro.U32(d, shape + 8), size = (int)Nitro.U32(d, shape + 12); Nitro.Need(d, list, size);
        int end = list + size, at = list; var matrix = start;
        var v = Vector3.Zero; var uv = Vector2.Zero; uint colour = 0xFFFFFFFF; int primitive = -1;
        var strip = new List<(Vector3 P, Vector2 T, uint C)>();
        void Emit(int a, int b, int c) => output.Add(new(material, strip[a].P, strip[b].P, strip[c].P, strip[a].T, strip[b].T, strip[c].T, strip[a].C, strip[b].C, strip[c].C));
        void AddVertex()
        {
            strip.Add((Vector3.Transform(v * posScale, matrix), uv, colour)); int n = strip.Count;
            switch (primitive)
            {
                case 0 when n % 3 == 0: Emit(n - 3, n - 2, n - 1); break;
                case 1 when n % 4 == 0: Emit(n - 4, n - 3, n - 2); Emit(n - 4, n - 2, n - 1); break;
                case 2 when n >= 3: if (n % 2 == 1) Emit(n - 3, n - 2, n - 1); else Emit(n - 2, n - 3, n - 1); break;
                case 3 when n >= 4 && n % 2 == 0: Emit(n - 4, n - 3, n - 1); Emit(n - 4, n - 1, n - 2); break;
            }
        }
        static float S10(int value, int shift) { int x = value >> shift & 0x3FF; return (x & 0x200) != 0 ? x - 0x400 : x; }
        while (at + 4 <= end)
        {
            uint packed = Nitro.U32(d, at); at += 4;
            for (int k = 0; k < 4; k++)
            {
                int cmd = (int)(packed >> (k * 8) & 0xFF), count = ParamCount[cmd];
                if (count < 0) throw new InvalidDataException($"Comando GX desconocido 0x{cmd:X2}.");
                if (cmd == 0 && k > 0) continue;
                Nitro.Need(d, at, count * 4L); int p0 = count > 0 ? Nitro.I32(d, at) : 0;
                switch (cmd)
                {
                    case 0x14: matrix = stack[p0 & 31]; break;
                    case 0x15: matrix = Matrix4x4.Identity; break;
                    case 0x1B: matrix = Matrix4x4.CreateScale(p0 / 4096f, Nitro.I32(d, at + 4) / 4096f, Nitro.I32(d, at + 8) / 4096f) * matrix; break;
                    case 0x1C: matrix = Matrix4x4.CreateTranslation(p0 / 4096f, Nitro.I32(d, at + 4) / 4096f, Nitro.I32(d, at + 8) / 4096f) * matrix; break;
                    case 0x20: colour = Nitro.Bgr555(p0 & 0x7FFF); break;
                    case 0x22: uv = new((short)(p0 & 0xFFFF) / 16f, (short)(p0 >> 16) / 16f); break;
                    case 0x23: v = new((short)(p0 & 0xFFFF) / 4096f, (short)(p0 >> 16) / 4096f, Nitro.I16(d, at + 4) / 4096f); AddVertex(); break;
                    case 0x24: v = new(S10(p0, 0) / 64f, S10(p0, 10) / 64f, S10(p0, 20) / 64f); AddVertex(); break;
                    case 0x25: v = new((short)(p0 & 0xFFFF) / 4096f, (short)(p0 >> 16) / 4096f, v.Z); AddVertex(); break;
                    case 0x26: v = new((short)(p0 & 0xFFFF) / 4096f, v.Y, (short)(p0 >> 16) / 4096f); AddVertex(); break;
                    case 0x27: v = new(v.X, (short)(p0 & 0xFFFF) / 4096f, (short)(p0 >> 16) / 4096f); AddVertex(); break;
                    case 0x28: v += new Vector3(S10(p0, 0), S10(p0, 10), S10(p0, 20)) / 4096f; AddVertex(); break;
                    case 0x40: primitive = p0 & 3; strip.Clear(); break;
                    case 0x41: primitive = -1; strip.Clear(); break;
                }
                at += count * 4;
            }
        }
    }
}
