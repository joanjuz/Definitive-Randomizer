#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace pk3DS.Core.Maps.Ctr;

/// <summary>
/// Minimal read-only reader of the H3D ("BCH") container used by the Gen6 map
/// models and textures. It covers the parts needed to draw a map from above:
/// textures, materials (texture names and coordinate transforms) and mesh
/// geometry. The layout follows the format as documented by SPICA (gdkchan,
/// public domain) and was checked on X and Omega Ruby/Alpha Sapphire files;
/// only the versions listed in <see cref="SupportedVersions"/> are accepted.
/// The input is copied before pointers are relocated, so the caller's bytes
/// never change.
/// </summary>
public sealed class H3dFile
{
    /// <summary>Backward-compatibility values observed in the Gen6 map files.</summary>
    public static readonly IReadOnlySet<byte> SupportedVersions = new HashSet<byte> { 7 };
    private const int MeshSize = 0x38, SubMeshSize = 0x34, MaterialSize = 0x58;

    private readonly byte[] _d;
    public byte Version { get; }
    public IReadOnlyList<H3dTexture> Textures { get; }
    public IReadOnlyList<H3dModel> Models { get; }

    public H3dFile(ReadOnlySpan<byte> source)
    {
        if (source.Length < 0x38 || source[0] != 'B' || source[1] != 'C' || source[2] != 'H' || source[3] != 0)
            throw new MapFormatException("No es un archivo BCH.");
        Version = source[4];
        if (!SupportedVersions.Contains(Version)) throw new MapFormatException($"Versión BCH 0x{Version:X2} no verificada.");
        _d = source.ToArray();
        int contents = I32(0x08), strings = I32(0x0C), commands = I32(0x10), raw = I32(0x14), relocation = I32(0x18);
        int relocationLength = I32(0x2C);
        if (relocation < 0 || relocationLength < 0 || relocation + relocationLength > _d.Length) throw new MapFormatException("Tabla de reubicación fuera del archivo.");
        uint Section(int id) => id switch
        {
            0 => (uint)contents,
            1 => (uint)strings,
            2 or 3 => (uint)commands,
            4 or 5 or 6 or 8 => (uint)raw,
            7 => (uint)raw | 0x80000000u,
            _ => throw new MapFormatException($"Sección de reubicación {id} no admitida."),
        };
        for (int o = 0; o < relocationLength; o += 4)
        {
            uint value = U32(relocation + o);
            uint pointer = value & 0x1FFFFFF;
            int target = (int)((value >> 25) & 0xF), source2 = (int)(value >> 29);
            // Version 7 uses the current section numbering (older and 8–0x20 versions are shifted, not accepted here).
            if (target != 1) pointer <<= 2;
            long at = Section(source2) + (long)pointer;
            if (at < 0 || at + 4 > _d.Length) throw new MapFormatException("Puntero reubicado fuera del archivo.");
            BinaryPrimitives.WriteUInt32LittleEndian(_d.AsSpan((int)at), U32((int)at) + Section(target));
        }
        Textures = ReadTextures(contents + (3 * 12));
        Models = ReadModels(contents);
    }

    private int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(Span(at, 4));
    private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(Span(at, 4));
    private ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Span(at, 2));
    private float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(Span(at, 4));

    private ReadOnlySpan<byte> Span(long at, long length)
    {
        if (at < 0 || length < 0 || at + length > _d.Length) throw new MapFormatException($"Lectura fuera del archivo BCH (0x{at:X}).");
        return _d.AsSpan((int)at, (int)length);
    }

    private string Str(uint at)
    {
        if (at == 0) return "";
        var s = Span(at, Math.Min(256, _d.Length - at));
        int end = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? s : s[..end]);
    }

    private uint[] Words(int at)
    {
        uint ptr = U32(at); int count = I32(at + 4);
        if (count < 0 || count > 0x10000) throw new MapFormatException("Lista de comandos demasiado larga.");
        var words = new uint[count];
        for (int i = 0; i < count; i++) words[i] = U32((int)ptr + (i * 4));
        return words;
    }

    private (uint Pointer, int Count) Dict(int at)
    {
        uint ptr = U32(at); int count = I32(at + 4);
        if (count < 0 || count > 4096) throw new MapFormatException("Diccionario demasiado grande.");
        return (ptr, count);
    }

    private List<H3dTexture> ReadTextures(int dict)
    {
        var (table, count) = Dict(dict);
        var list = new List<H3dTexture>(count);
        for (int i = 0; i < count; i++)
        {
            int t = (int)U32((int)table + (i * 4));
            int width = 0, height = 0; uint address = 0;
            foreach (var c in PicaCommands.Read(Words(t)))
            {
                if (c.Register == 0x82) { height = (int)(c.Value & 0x7FF); width = (int)((c.Value >> 16) & 0x7FF); }
                else if (c.Register == 0x85) address = c.Value;
            }
            var format = (PicaTextureFormat)_d[t + 0x18];
            string name = Str(U32(t + 0x1C));
            int length = PicaTexture.Length(width, height, format);
            if (width <= 0 || height <= 0 || length <= 0) throw new MapFormatException($"Textura {name} con dimensiones no válidas.");
            list.Add(new H3dTexture(name, width, height, format, Span(address, length).ToArray()));
        }
        return list;
    }

    private List<H3dModel> ReadModels(int contents)
    {
        var (table, count) = Dict(contents);
        var models = new List<H3dModel>(count);
        for (int i = 0; i < count; i++)
        {
            int m = (int)U32((int)table + (i * 4));
            // 3×4 row-major (translation in the fourth column), turned into a row-vector Matrix4x4.
            float A(int r, int c) => F32(m + 4 + (((r * 4) + c) * 4));
            var world = new Matrix4x4(
                A(0, 0), A(1, 0), A(2, 0), 0,
                A(0, 1), A(1, 1), A(2, 1), 0,
                A(0, 2), A(1, 2), A(2, 2), 0,
                A(0, 3), A(1, 3), A(2, 3), 1);
            var (matPtr, matCount) = Dict(m + 0x34);
            var materials = new List<H3dMaterial>(matCount);
            for (int k = 0; k < matCount; k++) materials.Add(ReadMaterial((int)matPtr + (k * MaterialSize)));
            uint meshPtr = U32(m + 0x40); int meshCount = I32(m + 0x44);
            if (meshCount < 0 || meshCount > 4096) throw new MapFormatException("Demasiadas mallas.");
            var meshes = new List<H3dMesh>(meshCount);
            for (int k = 0; k < meshCount; k++) meshes.Add(ReadMesh((int)meshPtr + (k * MeshSize), materials.Count));
            models.Add(new H3dModel(Str(U32(m + 0x84)), world, materials, meshes));
        }
        return models;
    }

    private H3dMaterial ReadMaterial(int at)
    {
        int p = (int)U32(at);
        var coords = new H3dTextureCoord[3];
        for (int i = 0; i < 3; i++)
        {
            int c = p + 0x0C + (i * 0x18);
            coords[i] = new H3dTextureCoord(_d[c + 1], _d[c + 2], new Vector2(F32(c + 4), F32(c + 8)), F32(c + 12), new Vector2(F32(c + 16), F32(c + 20)));
        }
        return new H3dMaterial(Str(U32(at + 0x54)), [Str(U32(at + 0x48)), Str(U32(at + 0x4C)), Str(U32(at + 0x50))], coords);
    }

    private H3dMesh ReadMesh(int at, int materialCount)
    {
        int material = U16(at);
        if (material >= materialCount) throw new MapFormatException($"Malla con material {material} inexistente.");
        uint bufferAddress = 0; ulong formats = 0, attributes = 0, permutation = 0; int stride = 0, total = 0;
        var uniforms = new Vector4[96];
        foreach (var c in PicaCommands.Read(Words(at + 8), uniforms))
        {
            switch (c.Register)
            {
                case 0x201: formats |= c.Value; break;
                case 0x202: formats |= (ulong)c.Value << 32; break;
                case 0x203: bufferAddress = c.Value; break;
                case 0x204: attributes |= c.Value; break;
                case 0x205: attributes |= (ulong)(c.Value & 0xFFFF) << 32; stride = (int)((c.Value >> 16) & 0xFF); break;
                case 0x242: total = (int)c.Value + 1; break;
                case 0x2BB: permutation |= c.Value; break;
                case 0x2BC: permutation |= (ulong)c.Value << 32; break;
            }
        }
        var attrs = new List<PicaAttribute>();
        for (int i = 0; i < total; i++)
        {
            if (((formats >> (48 + i)) & 1) != 0) continue; // fixed attribute, not in the buffer
            int slot = (int)((attributes >> (i * 4)) & 0xF);
            int name = (int)((permutation >> (slot * 4)) & 0xF), fmt = (int)((formats >> (slot * 4)) & 0xF);
            float scale = name switch
            {
                0 => uniforms[7].X, 1 => uniforms[7].Y, 2 => uniforms[7].Z, 3 => uniforms[7].W,
                4 => uniforms[8].X, 5 => uniforms[8].Y, 6 => uniforms[8].Z, 8 => uniforms[8].W,
                _ => 1,
            };
            attrs.Add(new PicaAttribute(name, fmt & 3, (fmt >> 2) + 1, scale));
        }
        var subs = new List<H3dSubMesh>();
        uint subPtr = U32(at + 0x10); int subCount = I32(at + 0x14);
        if (subCount < 0 || subCount > 1024) throw new MapFormatException("Demasiadas submallas.");
        int maxIndex = 0;
        for (int s = 0; s < subCount; s++)
        {
            int sb = (int)subPtr + (s * SubMeshSize);
            uint indexAddress = 0; int indexCount = 0, mode = 0;
            foreach (var c in PicaCommands.Read(Words(sb + 0x2C)))
            {
                if (c.Register == 0x227) indexAddress = c.Value;
                else if (c.Register == 0x228) indexCount = (int)c.Value;
                else if (c.Register == 0x25E) mode = (int)((c.Value >> 8) & 3);
            }
            bool wide = (indexAddress >> 31) != 0;
            var raw = Span(indexAddress & 0x7FFFFFFF, (long)indexCount * (wide ? 2 : 1));
            var indices = new ushort[indexCount];
            for (int k = 0; k < indexCount; k++)
            {
                indices[k] = wide ? BinaryPrimitives.ReadUInt16LittleEndian(raw[(k * 2)..]) : raw[k];
                maxIndex = Math.Max(maxIndex, indices[k]);
            }
            subs.Add(new H3dSubMesh(mode, indices));
        }
        int vertexCount = subs.Count == 0 ? 0 : maxIndex + 1;
        var vertices = Span(bufferAddress, (long)vertexCount * stride).ToArray();
        return new H3dMesh(material, attrs, stride, uniforms[6], vertices, subs);
    }
}

public enum PicaTextureFormat : byte { RGBA8, RGB8, RGBA5551, RGB565, RGBA4, LA8, HiLo8, L8, A8, LA4, L4, A4, ETC1, ETC1A4 }

public sealed record H3dTexture(string Name, int Width, int Height, PicaTextureFormat Format, byte[] Raw)
{
    /// <summary>BGRA pixels, first row at the top (v = 1).</summary>
    public byte[] Decode() => PicaTexture.Decode(Raw, Width, Height, Format);
}

public sealed record H3dTextureCoord(byte TransformType, byte MappingType, Vector2 Scale, float Rotation, Vector2 Translation)
{
    /// <summary>Texture coordinate transform (Maya, 3ds Max or SoftImage conventions).</summary>
    public Vector2 Apply(Vector2 uv)
    {
        float ca = MathF.Cos(Rotation), sa = MathF.Sin(Rotation), sx = Scale.X, sy = Scale.Y, tx = Translation.X, ty = Translation.Y;
        float m11 = sx * ca, m12 = sy * sa, m21 = sx * -sa, m22 = sy * ca, m41, m42;
        switch (TransformType)
        {
            case 0: m41 = sx * ((0.5f * sa) - (0.5f * ca) + 0.5f - tx); m42 = sy * ((0.5f * -sa) - (0.5f * ca) + 0.5f - ty); break;
            case 2: m41 = sx * ((-ca * tx) - (sa * ty)); m42 = sy * ((sa * tx) - (ca * ty)); break;
            default: m41 = (sx * ca * (-tx - 0.5f)) - (sx * sa * (ty - 0.5f)) + 0.5f; m42 = (sy * sa * (-tx - 0.5f)) + (sy * ca * (ty - 0.5f)) + 0.5f; break;
        }
        return new Vector2((uv.X * m11) + (uv.Y * m21) + m41, (uv.X * m12) + (uv.Y * m22) + m42);
    }
}

public sealed record H3dMaterial(string Name, IReadOnlyList<string> TextureNames, IReadOnlyList<H3dTextureCoord> Coords);

public sealed record PicaAttribute(int Name, int Format, int Elements, float Scale)
{
    public const int Position = 0, Color = 3, TexCoord0 = 4, TexCoord1 = 5, TexCoord2 = 6;
    public int Size => Elements * Format switch { 2 => 2, 3 => 4, _ => 1 };
    public int Align => Format switch { 2 => 2, 3 => 4, _ => 1 };
}

public sealed record H3dSubMesh(int PrimitiveMode, IReadOnlyList<ushort> Indices);

public sealed record H3dMesh(int Material, IReadOnlyList<PicaAttribute> Attributes, int Stride, Vector4 PositionOffset, byte[] Vertices, IReadOnlyList<H3dSubMesh> SubMeshes)
{
    public int VertexCount => Stride == 0 ? 0 : Vertices.Length / Stride;

    /// <summary>Reads one attribute of every vertex as four floats (scaled; positions offset).</summary>
    public Vector4[]? Read(int name)
    {
        int offset = 0; PicaAttribute? found = null;
        foreach (var a in Attributes)
        {
            offset = (offset + a.Align - 1) / a.Align * a.Align;
            if (a.Name == name) { found = a; break; }
            offset += a.Size;
        }
        if (found is null) return null;
        var output = new Vector4[VertexCount];
        Span<float> e = stackalloc float[4];
        for (int v = 0; v < output.Length; v++)
        {
            var s = Vertices.AsSpan((v * Stride) + offset);
            e.Clear();
            for (int k = 0; k < found.Elements; k++)
                e[k] = found.Format switch
                {
                    0 => (sbyte)s[k],
                    1 => s[k],
                    2 => BinaryPrimitives.ReadInt16LittleEndian(s[(k * 2)..]),
                    _ => BinaryPrimitives.ReadSingleLittleEndian(s[(k * 4)..]),
                };
            var value = new Vector4(e[0], e[1], e[2], e[3]) * found.Scale;
            output[v] = name == PicaAttribute.Position ? value + PositionOffset : value;
        }
        return output;
    }

    /// <summary>Triangles of every sub-mesh, expanding strips and fans.</summary>
    public IEnumerable<(int A, int B, int C)> Triangles()
    {
        foreach (var sm in SubMeshes)
        {
            var ix = sm.Indices;
            switch (sm.PrimitiveMode)
            {
                case 1: // strip
                    for (int i = 2; i < ix.Count; i++) yield return (i & 1) == 0 ? (ix[i - 2], ix[i - 1], ix[i]) : (ix[i - 1], ix[i - 2], ix[i]);
                    break;
                case 2: // fan
                    for (int i = 2; i < ix.Count; i++) yield return (ix[0], ix[i - 1], ix[i]);
                    break;
                default:
                    for (int i = 0; i + 2 < ix.Count; i += 3) yield return (ix[i], ix[i + 1], ix[i + 2]);
                    break;
            }
        }
    }
}

public sealed record H3dModel(string Name, Matrix4x4 World, IReadOnlyList<H3dMaterial> Materials, IReadOnlyList<H3dMesh> Meshes);

/// <summary>PICA200 command list decoder (register writes and vertex shader float uniforms).</summary>
internal static class PicaCommands
{
    internal readonly record struct Write(int Register, uint Value);

    internal static List<Write> Read(uint[] words, Vector4[]? uniforms = null)
    {
        var list = new List<Write>();
        int uIndex = 0; bool u32 = false; var pending = new List<uint>(4);
        void Uniform(uint word)
        {
            if (uniforms is null) return;
            pending.Add(word);
            if (u32 && pending.Count == 4)
            {
                static float F(uint w) => BitConverter.UInt32BitsToSingle(w);
                if (uIndex < uniforms.Length) uniforms[uIndex] = new Vector4(F(pending[3]), F(pending[2]), F(pending[1]), F(pending[0]));
                uIndex++; pending.Clear();
            }
            else if (!u32 && pending.Count == 3)
            {
                uint w0 = pending[0], w1 = pending[1], w2 = pending[2];
                if (uIndex < uniforms.Length)
                    uniforms[uIndex] = new Vector4(F24(w2 & 0xFFFFFF), F24((w2 >> 24) | ((w1 & 0xFFFF) << 8)), F24((w1 >> 16) | ((w0 & 0xFF) << 16)), F24(w0 >> 8));
                uIndex++; pending.Clear();
            }
        }
        void Apply(int register, uint value)
        {
            list.Add(new Write(register, value));
            if (register == 0x2C0) { uIndex = (int)(value & 0xFF); u32 = (value >> 31) != 0; pending.Clear(); }
            else if (register is >= 0x2C1 and <= 0x2C8) Uniform(value);
        }
        int i = 0;
        while (i + 1 < words.Length)
        {
            uint parameter = words[i++], command = words[i++];
            int id = (int)(command & 0xFFFF), extra = (int)((command >> 20) & 0x7FF);
            bool consecutive = (command >> 31) != 0;
            if (i + extra > words.Length) throw new MapFormatException("Lista de comandos PICA truncada.");
            Apply(id, parameter);
            for (int k = 0; k < extra; k++) Apply(consecutive ? id + k + 1 : id, words[i++]);
            if ((i & 1) != 0) i++;
        }
        return list;
    }

    private static float F24(uint v)
    {
        if ((v & 0x7FFFFF) == 0) return 0;
        uint bits = (((v >> 23) & 1) << 31) | ((((v >> 16) & 0x7F) + 64) << 23) | ((v & 0xFFFF) << 7);
        return BitConverter.UInt32BitsToSingle(bits);
    }
}
