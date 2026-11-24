#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace pk3DS.Core.Maps.Ctr;

/// <summary>
/// Minimal read-only reader of the GFL2 model and texture blobs used by the
/// Gen7 field (Sun/Moon, Ultra Sun/Ultra Moon). Sequential layout as
/// documented by SPICA (gdkchan, public domain); sections carry their length,
/// so unknown parts are skipped by length instead of being interpreted.
/// The meshes are returned as <see cref="H3dMesh"/> records so the same
/// top-down rasteriser can draw them.
/// </summary>
public static class Gfl2File
{
    public const uint ModelMagic = 0x15122117, TextureMagic = 0x15041213;
    private static readonly float[] Scales = [1f / sbyte.MaxValue, 1f / byte.MaxValue, 1f / short.MaxValue, 1f];

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _d = data;
        public int Position;
        public readonly int Length => _d.Length;
        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || Position + n > _d.Length) throw new MapFormatException($"GFL2 truncado en 0x{Position:X}.");
            var s = _d.Slice(Position, n); Position += n; return s;
        }
        public byte U8() => Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
        public void Skip(int n) => Take(n);
        public ReadOnlySpan<byte> Bytes(int n) => Take(n);
        public void Align16() { if ((Position & 15) != 0) Skip(16 - (Position & 15)); }
        public string Padded(int n) { var s = Take(n); int e = s.IndexOf((byte)0); return Encoding.ASCII.GetString(e < 0 ? s : s[..e]); }
        // Length-prefixed strings may include their terminator.
        public string ByteString() => Encoding.ASCII.GetString(Take(U8())).TrimEnd('\0');
        public string IntString() { int n = I32(); if (n < 0 || n > 0x400) throw new MapFormatException("Cadena GFL2 no válida."); return Encoding.ASCII.GetString(Take(n)).TrimEnd('\0'); }
        public void Seek(int p) { if (p < 0 || p > _d.Length) throw new MapFormatException("Salto GFL2 fuera del archivo."); Position = p; }
    }

    public const uint ModelPackMagic = 0x10000;

    /// <summary>
    /// Model resource pack (magic 0x10000): five u32 counts (models, textures, two unknown, shaders),
    /// a pointer table, then per entry a byte-length name and the u32 address of its payload.
    /// </summary>
    public static (List<H3dModel> Models, List<H3dTexture> Textures) ReadModelPack(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24 || BinaryPrimitives.ReadUInt32LittleEndian(data) != ModelPackMagic) throw new MapFormatException("No es un paquete de modelos GFL2.");
        var counts = new int[5]; long total = 0;
        for (int i = 0; i < 5; i++) { counts[i] = BinaryPrimitives.ReadInt32LittleEndian(data[(4 + (i * 4))..]); total += counts[i]; }
        if (total < 0 || total > 0x20000 || 24 + (total * 4) > data.Length) throw new MapFormatException("Tabla del paquete de modelos no válida.");
        var entries = new List<(int Category, int Address)>();
        int pointer = 24;
        for (int c = 0; c < 5; c++)
            for (int i = 0; i < counts[c]; i++, pointer += 4)
            {
                int pos = BinaryPrimitives.ReadInt32LittleEndian(data[pointer..]);
                if (pos == 0) continue;
                if (pos < 24 + (total * 4) || pos >= data.Length) throw new MapFormatException("Nombre de recurso fuera del paquete.");
                int field = pos + 1 + data[pos];
                if (field + 4 > data.Length) throw new MapFormatException("Recurso truncado.");
                int address = BinaryPrimitives.ReadInt32LittleEndian(data[field..]);
                if (address <= field + 3 || address >= data.Length) throw new MapFormatException("Dirección de recurso no válida.");
                entries.Add((c, address));
            }
        entries.Sort((a, b) => a.Address.CompareTo(b.Address));
        var models = new List<H3dModel>(); var textures = new List<H3dTexture>();
        for (int i = 0; i < entries.Count; i++)
        {
            int end = i + 1 < entries.Count ? entries[i + 1].Address : data.Length;
            var payload = data[entries[i].Address..end];
            if (entries[i].Category == 0) models.Add(ReadModel(payload));
            else if (entries[i].Category == 1 && ReadTexture(payload) is { } t) textures.Add(t);
        }
        return (models, textures);
    }

    /// <summary>One texture blob (magic 0x15041213 followed by the "texture" section).</summary>
    public static H3dTexture? ReadTexture(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x80 || BinaryPrimitives.ReadUInt32LittleEndian(data) != TextureMagic) return null;
        var r = new Reader(data);
        r.Skip(8);
        string section = r.Padded(8);
        if (section != "texture") return null; // shaders share the magic
        r.Skip(8);
        int length = r.I32();
        r.Skip(0xC);
        string name = r.Padded(0x40);
        int width = r.U16(), height = r.U16(), format = r.U16();
        r.Skip(2 + 0x10);
        var pica = format switch
        {
            0x02 => PicaTextureFormat.RGB565, 0x03 => PicaTextureFormat.RGB8, 0x04 => PicaTextureFormat.RGBA8,
            0x16 => PicaTextureFormat.RGBA4, 0x17 => PicaTextureFormat.RGBA5551, 0x23 => PicaTextureFormat.LA8,
            0x24 => PicaTextureFormat.HiLo8, 0x25 => PicaTextureFormat.L8, 0x26 => PicaTextureFormat.A8,
            0x27 => PicaTextureFormat.LA4, 0x28 => PicaTextureFormat.L4, 0x29 => PicaTextureFormat.A4,
            0x2A => PicaTextureFormat.ETC1, 0x2B => PicaTextureFormat.ETC1A4,
            _ => throw new MapFormatException($"Formato de textura GFL2 0x{format:X} desconocido."),
        };
        int need = PicaTexture.Length(width, height, pica);
        if (need <= 0 || need > length) throw new MapFormatException($"Textura {name} incompleta.");
        return new H3dTexture(name, width, height, pica, r.Bytes(need).ToArray());
    }

    /// <summary>One model blob (magic 0x15122117).</summary>
    public static H3dModel ReadModel(ReadOnlySpan<byte> data) => ReadModel(data, null);

    /// <summary>Where the editable values of a model blob are: bounding box, transform and each vertex buffer.</summary>
    public static Gfl2ModelLayout ReadLayout(ReadOnlySpan<byte> data)
    {
        var layout = new Gfl2ModelLayout();
        ReadModel(data, layout);
        return layout;
    }

    private static H3dModel ReadModel(ReadOnlySpan<byte> data, Gfl2ModelLayout? layout)
    {
        if (data.Length < 0x40 || BinaryPrimitives.ReadUInt32LittleEndian(data) != ModelMagic) throw new MapFormatException("No es un modelo GFL2.");
        var r = new Reader(data);
        r.Skip(8); r.Align16();
        r.Skip(16); // "gfmodel" section header

        Table(ref r); Table(ref r); var materialNames = Table(ref r); var meshNames = Table(ref r);
        if (layout is not null) { layout.BoundingBox = r.Position; layout.MeshNames = meshNames.Length; }
        r.Skip(32); // bounding box
        if (layout is not null) layout.Transform = r.Position;
        var m = new float[16];
        for (int i = 0; i < 16; i++) m[i] = r.F32();
        var transform = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        uint unknownLength = r.U32(), unknownOffset = r.U32();
        r.Skip(8);
        r.Seek(checked(r.Position + (int)unknownOffset + (int)unknownLength));
        int bones = r.I32();
        if (bones < 0 || bones > 1024) throw new MapFormatException("Demasiados huesos.");
        r.Skip(0xC);
        for (int i = 0; i < bones; i++) { r.ByteString(); r.ByteString(); r.Skip(1 + 36); }
        r.Align16();
        int luts = r.I32(), lutLength = r.I32();
        r.Align16();
        if (luts < 0 || lutLength < 0) throw new MapFormatException("LUT no válida.");
        r.Skip(luts * (0x10 + lutLength));
        var materials = new List<H3dMaterial>(materialNames.Length);
        var materialIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < materialNames.Length; i++)
        {
            r.Skip(8); int length = r.I32(); r.Skip(4);
            int start = r.Position;
            r.Skip(4); string name = r.ByteString();
            for (int k = 0; k < 3; k++) { r.Skip(4); r.ByteString(); } // shader names
            r.Skip(16 + 8 + (12 * 4) + 16 + 16 + 8 + 36 + 4 + 16);
            uint units = r.U32();
            var textures = new[] { "", "", "" };
            var coords = new H3dTextureCoord[3];
            for (int u = 0; u < 3; u++) coords[u] = new H3dTextureCoord(0, 0, Vector2.One, 0, Vector2.Zero);
            for (int u = 0; u < units; u++)
            {
                r.Skip(4); string tex = r.ByteString();
                int unit = r.U8(), mapping = r.U8();
                var scale = new Vector2(r.F32(), r.F32()); float rotation = r.F32(); var translation = new Vector2(r.F32(), r.F32());
                r.Skip(20);
                if (u < 3) { textures[u] = tex; coords[u] = new H3dTextureCoord(0, (byte)mapping, scale, rotation, translation); }
                _ = unit;
            }
            uint? blend = null;
            try
            {
                // After the units (16-byte aligned): u32 command length, render priority, u32, render layer,
                // four u32 hashes, then PICA register writes; 0x101 is GPUREG_BLEND_FUNC.
                r.Align16();
                int cmdLength = r.I32();
                if (cmdLength > 0 && cmdLength <= 0x2000 && r.Position + 28 + cmdLength <= start + length)
                {
                    r.Skip(4 + 4 + 4 + 16);
                    var words = new uint[cmdLength / 4];
                    for (int k = 0; k < words.Length; k++) words[k] = r.U32();
                    foreach (var c in PicaCommands.Read(words)) if (c.Register == 0x101) blend = c.Value;
                }
            }
            catch (MapFormatException) { blend = null; }
            r.Seek(start + length);
            materialIndex.TryAdd(name, materials.Count);
            materials.Add(new H3dMaterial(name, textures, coords, blend));
        }
        var meshes = new List<H3dMesh>();
        for (int i = 0; i < meshNames.Length; i++)
        {
            r.Skip(8); int length = r.I32(); r.Skip(4);
            int start = r.Position;
            r.Skip(4 + 0x40 + 4 + 32);
            int subCount = r.I32();
            if (subCount < 0 || subCount > 256) throw new MapFormatException("Demasiadas submallas GFL2.");
            r.Skip(4 + 0x10);
            var blocks = new List<uint[]>();
            uint index, count;
            do
            {
                int cmdLength = r.I32(); index = r.U32(); count = r.U32(); r.Skip(4);
                if (cmdLength < 0 || cmdLength > 0x10000) throw new MapFormatException("Comandos GFL2 no válidos.");
                var cmds = new uint[cmdLength / 4];
                for (int k = 0; k < cmds.Length; k++) cmds[k] = r.U32();
                blocks.Add(cmds);
            } while (index < count - 1 && count > 0);
            var sizes = new (string Material, int VertexLength, int IndexLength)[subCount];
            for (int s = 0; s < subCount; s++)
            {
                r.Skip(4); string mat = r.IntString();
                r.Skip(1 + 0x1F);
                r.Skip(8); int vl = r.I32(), il = r.I32();
                sizes[s] = (mat, vl, il);
            }
            if (blocks.Count < subCount * 3) throw new MapFormatException("Faltan comandos de submalla GFL2.");
            for (int s = 0; s < subCount; s++)
            {
                var (attrs, stride) = Attributes(blocks[s * 3]);
                uint indexAddress = 0; int indexCount = 0, mode = 0;
                foreach (var c in PicaCommands.Read(blocks[(s * 3) + 2]))
                {
                    if (c.Register == 0x227) indexAddress = c.Value;
                    else if (c.Register == 0x228) indexCount = (int)c.Value;
                    else if (c.Register == 0x25E) mode = (int)((c.Value >> 8) & 3);
                }
                layout?.Buffers.Add(new Gfl2VertexBuffer(r.Position, sizes[s].VertexLength, stride, attrs));
                var vertices = r.Bytes(sizes[s].VertexLength).ToArray();
                int indexStart = r.Position;
                bool wide = (indexAddress >> 31) != 0;
                var raw = r.Bytes(Math.Min(sizes[s].IndexLength, indexCount * (wide ? 2 : 1)));
                var indices = new ushort[indexCount];
                for (int k = 0; k < indexCount; k++) indices[k] = wide ? BinaryPrimitives.ReadUInt16LittleEndian(raw[(k * 2)..]) : raw[k];
                r.Seek(indexStart + sizes[s].IndexLength);
                if (!materialIndex.TryGetValue(sizes[s].Material, out int material) || stride <= 0) continue;
                meshes.Add(new H3dMesh(material, attrs, stride, Vector4.Zero, vertices, [new H3dSubMesh(mode, indices)]));
            }
            r.Seek(start + length);
        }
        // Field vertices are already in world coordinates (they match the header bounding box);
        // the model transform only holds a reference point, so it is not applied.
        _ = transform;
        return new H3dModel("gfmodel", Matrix4x4.Identity, materials, meshes);
    }

    private static string[] Table(ref Reader r)
    {
        uint n = r.U32();
        if (n > 4096) throw new MapFormatException("Tabla GFL2 demasiado grande.");
        var a = new string[n];
        for (int i = 0; i < n; i++) { r.Skip(4); a[i] = r.Padded(0x40); }
        return a;
    }

    private static (List<PicaAttribute> Attributes, int Stride) Attributes(uint[] enable)
    {
        ulong formats = 0, attributes = 0, permutation = 0; int stride = 0, total = 0;
        foreach (var c in PicaCommands.Read(enable))
        {
            switch (c.Register)
            {
                case 0x201: formats |= c.Value; break;
                case 0x202: formats |= (ulong)c.Value << 32; break;
                case 0x204: attributes |= c.Value; break;
                case 0x205: attributes |= (ulong)(c.Value & 0xFFFF) << 32; stride = (int)((c.Value >> 16) & 0xFF); break;
                case 0x242: total = (int)c.Value + 1; break;
                case 0x2BB: permutation |= c.Value; break;
                case 0x2BC: permutation |= (ulong)c.Value << 32; break;
            }
        }
        var list = new List<PicaAttribute>();
        for (int i = 0; i < total; i++)
        {
            if (((formats >> (48 + i)) & 1) != 0) continue;
            int slot = (int)((attributes >> (i * 4)) & 0xF);
            int name = (int)((permutation >> (slot * 4)) & 0xF), fmt = (int)((formats >> (slot * 4)) & 0xF);
            list.Add(new PicaAttribute(name, fmt & 3, (fmt >> 2) + 1, name == 7 ? 1 : Scales[fmt & 3]));
        }
        return (list, stride);
    }
}

/// <summary>Offsets inside a GFL2 model blob (see <see cref="Gfl2File.ReadLayout"/>).</summary>
public sealed class Gfl2ModelLayout
{
    /// <summary>Offset of the 32-byte bounding box (min x, y, z, w; max x, y, z, w as floats).</summary>
    public int BoundingBox { get; internal set; } = -1;
    /// <summary>Offset of the 4×4 float transform (reference point of a field model).</summary>
    public int Transform { get; internal set; } = -1;
    public int MeshNames { get; internal set; }
    public List<Gfl2VertexBuffer> Buffers { get; } = [];
}

/// <summary>One vertex buffer of a model blob: offset, length, stride and attribute layout.</summary>
public sealed record Gfl2VertexBuffer(int Offset, int Length, int Stride, IReadOnlyList<PicaAttribute> Attributes);
