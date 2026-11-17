using System.IO;
using System.Numerics;
using System.Text;
using pk3DS.Core.NDS;
using pk3DS.Core.NDS.Nitro;

internal static partial class NdsMapRenderChecks
{
    // ------------------------------------------------------------------------------------------------ minimal Nitro builders (synthetic, no ROM data)
    private sealed class Writer
    {
        private readonly MemoryStream _s = new();
        internal int Length => (int)_s.Length;
        internal void U8(int v) => _s.WriteByte((byte)v);
        internal void U16(int v) { U8(v); U8(v >> 8); }
        internal void U32(long v) { U16((int)(v & 0xFFFF)); U16((int)(v >> 16 & 0xFFFF)); }
        internal void Bytes(byte[] b) => _s.Write(b);
        internal void Pad4() { while (_s.Length % 4 != 0) U8(0); }
        internal byte[] ToArray() => _s.ToArray();
        internal void Put32(int at, long v) { var b = _s.GetBuffer(); b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
        internal void Put16(int at, int v) { var b = _s.GetBuffer(); b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
    }
    /// <summary>A Nitro dictionary: header, an empty search tree (not needed to list entries) and the entry area.</summary>
    private static byte[] Dict(IReadOnlyList<(string Name, byte[] Unit)> entries, int emptyUnit = 4)
    {
        int unit = entries.Count == 0 ? emptyUnit : entries[0].Unit.Length, tree = 4 * (entries.Count + 1), ofsEntry = 8 + tree;
        var w = new Writer(); w.U8(0); w.U8(entries.Count); w.U16(ofsEntry + 4 + unit * entries.Count + 16 * entries.Count); w.U16(0); w.U16(ofsEntry);
        w.Bytes(new byte[tree]); w.U16(unit); w.U16(4 + unit * entries.Count);
        foreach (var e in entries) w.Bytes(e.Unit);
        foreach (var e in entries) { var n = new byte[16]; Encoding.ASCII.GetBytes(e.Name).CopyTo(n, 0); w.Bytes(n); }
        return w.ToArray();
    }
    private static byte[] U32Bytes(long v) => BitConverter.GetBytes((uint)v);
    private static byte[] Nitro(string magic, params byte[][] blocks)
    {
        var w = new Writer(); int header = 16 + 4 * blocks.Length; long size = header + blocks.Sum(b => (long)b.Length);
        w.Bytes(Encoding.ASCII.GetBytes(magic)); w.U16(0xFEFF); w.U16(1); w.U32(size); w.U16(16); w.U16(blocks.Length);
        int at = header; foreach (var b in blocks) { w.U32(at); at += b.Length; }
        foreach (var b in blocks) w.Bytes(b);
        return w.ToArray();
    }
    internal sealed record SynthTexture(string Name, NitroTextureFormat Format, int Width, int Height, byte[] Texels, bool Color0Transparent = false, bool Repeat = false, bool Flip = false, byte[]? Info = null);
    /// <summary>A TEX0 block: normal texels, 4x4 texels with their index data, palettes (BGR555), and both dictionaries.</summary>
    private static byte[] Tex0(IReadOnlyList<SynthTexture> textures, IReadOnlyList<(string Name, ushort[] Colours)> palettes)
    {
        var normal = new Writer(); var comp = new Writer(); var info = new Writer(); var pal = new Writer();
        var texUnits = new List<(string, byte[])>(); var palUnits = new List<(string, byte[])>();
        foreach (var t in textures)
        {
            bool c = t.Format == NitroTextureFormat.Compressed4x4; var area = c ? comp : normal; area.Pad4(); while (area.Length % 8 != 0) area.U8(0);
            int offset = area.Length; area.Bytes(t.Texels);
            if (c) { while (info.Length < offset / 2) info.U8(0); info.Bytes(t.Info!); }
            int log(int v) => (int)Math.Log2(v / 8);
            long param = (offset >> 3) | (t.Repeat ? 3L << 16 : 0) | (t.Flip ? 3L << 18 : 0) | (long)log(t.Width) << 20 | (long)log(t.Height) << 23 | (long)t.Format << 26 | (t.Color0Transparent ? 1L << 29 : 0);
            texUnits.Add((t.Name, U32Bytes(param).Concat(U32Bytes(0)).ToArray()));
        }
        foreach (var (name, colours) in palettes)
        {
            while (pal.Length % 8 != 0) pal.U8(0); int offset = pal.Length; foreach (var c in colours) pal.U16(c);
            palUnits.Add((name, BitConverter.GetBytes((ushort)(offset >> 3)).Concat(new byte[2]).ToArray()));
        }
        foreach (var a in new[] { normal, comp, pal }) while (a.Length % 8 != 0) a.U8(0);
        while (info.Length < comp.Length / 2) info.U8(0);
        byte[] texDict = Dict(texUnits, 8), palDict = Dict(palUnits, 4);
        int texDictAt = 0x3C, palDictAt = texDictAt + texDict.Length, texAt = palDictAt + palDict.Length, compAt = texAt + normal.Length, infoAt = compAt + comp.Length, palAt = infoAt + info.Length;
        var w = new Writer(); w.Bytes(Encoding.ASCII.GetBytes("TEX0")); w.U32(0); w.U32(0);
        w.U16(normal.Length >> 3); w.U16(texDictAt); w.U32(0); w.U32(texAt);
        w.U32(0); w.U16(comp.Length >> 3); w.U16(texDictAt); w.U32(0); w.U32(compAt); w.U32(infoAt);
        w.U32(0); w.U32(pal.Length >> 3); w.U32(palDictAt); w.U32(palAt);
        w.Bytes(texDict); w.Bytes(palDict); w.Bytes(normal.ToArray()); w.Bytes(comp.ToArray()); w.Bytes(info.ToArray()); w.Bytes(pal.ToArray());
        w.Pad4(); w.Put32(4, w.Length); return w.ToArray();
    }
    internal static byte[] Btx(IReadOnlyList<SynthTexture> textures, IReadOnlyList<(string, ushort[])> palettes) => Nitro("BTX0", Tex0(textures, palettes));

    /// <summary>A display list from (command, parameters) pairs, packed four commands per word as the GX FIFO expects.</summary>
    internal static byte[] DisplayList(params (int Cmd, uint[] Params)[] commands)
    {
        var w = new Writer();
        for (int i = 0; i < commands.Length; i += 4)
        {
            var group = commands.Skip(i).Take(4).ToArray(); uint packed = 0;
            for (int k = 0; k < group.Length; k++) packed |= (uint)group[k].Cmd << (k * 8);
            w.U32(packed); foreach (var c in group) foreach (var p in c.Params) w.U32(p);
        }
        return w.ToArray();
    }
    internal static uint Xy(int x, int y) => (uint)(ushort)x | (uint)(ushort)y << 16;
    internal static (int, uint[])[] Vtx16(params (float X, float Y, float Z)[] v) => v.Select(p => (0x23, new[] { Xy((int)(p.X * 4096), (int)(p.Y * 4096)), (uint)(ushort)(int)(p.Z * 4096) })).ToArray();
    internal static (int, uint[]) TexCoord(float s, float t) => (0x22, new[] { Xy((int)(s * 16), (int)(t * 16)) });

    /// <summary>One model "m": one identity node, one material bound to texture "t" and palette "p", and the given shapes drawn by SHP commands.</summary>
    internal static byte[] Bmd(float posScale, IReadOnlyList<byte[]> displayLists, byte[]? tex0 = null, bool vertexColour = false, int alpha = 31, byte[]? sbc = null, byte[]? node = null)
    {
        var model = new Writer();
        model.U32(0); model.U32(0); model.U32(0); model.U32(0); model.U32(0);                    // size, ofsSbc, ofsMat, ofsShp, ofsEvpMtx (patched)
        model.U8(0); model.U8(0); model.U8(0); model.U8(1); model.U8(1); model.U8(displayLists.Count); model.U8(0); model.U8(0);
        model.U32((long)(posScale * 4096)); model.U32((long)(4096 / posScale)); model.U16(3 * displayLists.Count); model.U16(displayLists.Count); model.U16(displayLists.Count); model.U16(0);
        model.Bytes(new byte[12]); model.U32(4096); model.U32(4096);
        // Nodes: dictionary at +0x40, its unit = offset of the node data relative to the dictionary.
        byte[] nodeData = node ?? [0x07, 0x00, 0x00, 0x10];   // flag = translation 0, rotation 0, scale 1; _00 = 1.0
        int nodeDictLength = Dict([("n", U32Bytes(0))]).Length;
        model.Bytes(Dict([("n", U32Bytes(nodeDictLength))])); model.Bytes(nodeData); model.Pad4();
        int sbcAt = model.Length;
        var code = sbc ?? [0x06, 0, 0, 0, 0x04, 0, .. displayLists.SelectMany((_, i) => new byte[] { 0x05, (byte)i }), 0x01];
        model.Bytes(code); model.Pad4();
        // Materials: offsets of the texture→material and palette→material dictionaries, the material dictionary, the lists and the material data.
        int matAt = model.Length; var mat = new Writer(); mat.U16(0); mat.U16(0);
        int matDictLength = Dict([("mat", U32Bytes(0))]).Length, texToMatLength = Dict([("t", new byte[4])]).Length, palToMatLength = Dict([("p", new byte[4])]).Length;
        int texToMatAt = 4 + matDictLength, palToMatAt = texToMatAt + texToMatLength, listAt = palToMatAt + palToMatLength, matDataAt = listAt + 4;
        mat.Bytes(Dict([("mat", U32Bytes(matDataAt))]));
        mat.Bytes(Dict([("t", new byte[] { (byte)listAt, (byte)(listAt >> 8), 1, 1 })])); mat.Bytes(Dict([("p", new byte[] { (byte)(listAt + 1), (byte)((listAt + 1) >> 8), 1, 1 })]));
        mat.U8(0); mat.U8(0); mat.U16(0);
        mat.U16(0); mat.U16(0x2C); mat.U32(0x7FFF | (vertexColour ? 0x8000 : 0)); mat.U32(0); mat.U32((long)alpha << 16); mat.U32(0); mat.U32(0); mat.U32(0);
        mat.U16(0); mat.U16(0x0002 | 0x0004 | 0x0008); mat.U16(8); mat.U16(8); mat.U32(4096); mat.U32(4096);
        var matBytes = mat.ToArray(); BitConverter.GetBytes((ushort)texToMatAt).CopyTo(matBytes, 0); BitConverter.GetBytes((ushort)palToMatAt).CopyTo(matBytes, 2);
        model.Bytes(matBytes); model.Pad4();
        // Shapes: dictionary, shape headers (16 bytes) and the display lists.
        int shpAt = model.Length; int shpDictLength = Dict(displayLists.Select((_, i) => ("s" + i, U32Bytes(0))).ToList()).Length;
        int header0 = shpDictLength, listsAt = header0 + 16 * displayLists.Count;
        var shapeOffsets = new List<(string, byte[])>(); var headers = new Writer(); var lists = new Writer(); int running = listsAt;
        for (int i = 0; i < displayLists.Count; i++)
        {
            shapeOffsets.Add(("s" + i, U32Bytes(header0 + 16 * i)));
            int me = header0 + 16 * i; headers.U16(0); headers.U16(16); headers.U32(0); headers.U32(running + lists.Length - me); headers.U32(displayLists[i].Length);
            lists.Bytes(displayLists[i]);
        }
        model.Bytes(Dict(shapeOffsets)); model.Bytes(headers.ToArray()); model.Bytes(lists.ToArray()); model.Pad4();
        model.Put32(0, model.Length); model.Put32(4, sbcAt); model.Put32(8, matAt); model.Put32(12, shpAt); model.Put32(16, model.Length);
        var modelBytes = model.ToArray();
        int mdlDictLength = Dict([("m", U32Bytes(0))]).Length;
        var mdl = new Writer(); mdl.Bytes(Encoding.ASCII.GetBytes("MDL0")); mdl.U32(0); mdl.Bytes(Dict([("m", U32Bytes(8 + mdlDictLength))])); mdl.Bytes(modelBytes); mdl.Pad4(); mdl.Put32(4, mdl.Length);
        return tex0 is null ? Nitro("BMD0", mdl.ToArray()) : Nitro("BMD0", mdl.ToArray(), tex0);
    }

    // ------------------------------------------------------------------------------------------------ checks
    internal static void Run(Action<bool, string> check)
    {
        static void Throws<T>(Action a, Action<bool, string> check, string name) where T : Exception { try { a(); } catch (T) { check(true, name); return; } throw new Exception("FAIL expected " + typeof(T).Name + ": " + name); }
        // Containers and dictionaries
        var red = (ushort)0x001F; var green = (ushort)0x03E0; var blue = (ushort)0x7C00; var white = (ushort)0x7FFF;
        uint R = 0xFFFF0000, G = 0xFF00FF00, B = 0xFF0000FF, W = 0xFFFFFFFF;
        byte[] pal16 = Enumerable.Range(0, 32).Select(i => (byte)(i % 2 == 0 ? 0x10 : 0x32)).ToArray();     // indices 0,1 / 2,3 alternating
        var btx = Btx([new("p4", NitroTextureFormat.Palette4, 8, 8, Enumerable.Repeat((byte)0xE4, 16).ToArray()),
                       new("p16", NitroTextureFormat.Palette16, 8, 8, pal16, Color0Transparent: true),
                       new("p256", NitroTextureFormat.Palette256, 8, 8, Enumerable.Range(0, 64).Select(i => (byte)(i % 4)).ToArray()),
                       new("a3i5", NitroTextureFormat.A3I5, 8, 8, Enumerable.Range(0, 64).Select(i => (byte)(i % 8 << 5 | 1)).ToArray()),
                       new("a5i3", NitroTextureFormat.A5I3, 8, 8, Enumerable.Range(0, 64).Select(i => (byte)(31 << 3 | 2)).ToArray()),
                       new("direct", NitroTextureFormat.Direct, 8, 8, Enumerable.Range(0, 64).SelectMany(i => BitConverter.GetBytes((ushort)(i == 0 ? 0x001F : 0x83E0))).ToArray())],
                      [("p4", [red, green, blue, white]), ("p16", [red, green, blue, white]), ("p256", [red, green, blue, white]), ("a3i5", [red, green, blue, white]), ("a5i3", [red, green, blue, white])]);
        var set = NitroTextureSet.FromBtx(btx);
        check(set.Textures.Select(t => t.Name).SequenceEqual(["p4", "p16", "p256", "a3i5", "a5i3", "direct"]) && set.Palettes.Count == 5 && set.Textures.All(t => t.Width == 8 && t.Height == 8), "MapRender_DictionariesListTexturesAndPalettes");
        var p4 = set.Decode(set.Texture("p4")!, set.Palette("p4"));
        check(p4.Pixels.Take(4).SequenceEqual([R, G, B, W]), "MapRender_Palette4DecodesTwoBitsLowFirst");
        var p16 = set.Decode(set.Texture("p16")!, set.Palette("p16"));
        check(p16.Pixels.Take(4).SequenceEqual([0u, G, B, W]), "MapRender_Palette16DecodesNibblesAndColour0Transparency");
        check(set.Decode(set.Texture("p256")!, set.Palette("p256")).Pixels.Take(4).SequenceEqual([R, G, B, W]), "MapRender_Palette256DecodesBytes");
        var a3 = set.Decode(set.Texture("a3i5")!, set.Palette("a3i5"));
        check(a3.Pixels[0] >> 24 == 0 && a3.Pixels[7] >> 24 == 255 && (a3.Pixels[7] & 0xFFFFFF) == (G & 0xFFFFFF), "MapRender_A3I5DecodesAlphaAndIndex");
        check(set.Decode(set.Texture("a5i3")!, set.Palette("a5i3")).Pixels.All(p => p == B), "MapRender_A5I3DecodesAlphaAndIndex");
        var direct = set.Decode(set.Texture("direct")!, null);
        check(direct.Pixels[0] == 0 && direct.Pixels[1] == G, "MapRender_DirectColourUsesBit15AsOpacity");
        Throws<InvalidDataException>(() => set.Decode(set.Texture("p16")!, null), check, "MapRender_PaletteFormatsRequireAPalette");
        Throws<InvalidDataException>(() => set.Decode(set.Texture("p256")!, new NitroPaletteInfo("short", set.Palette("a5i3")!.Offset + 4)), check, "MapRender_APaletteTooShortIsRefusedNotReadBeyond");
        // 4x4 compressed: one 4x4 texel row word per block, mode in the index data (0: three colours + transparent, 1: mid-colour, 2: four, 3: 5:3/3:5 mixes)
        foreach (var (mode, expected) in new[] { (0, new[] { R, G, B, 0u }), (1, new[] { R, G, 0xFF7F7F00u, 0u }), (2, new[] { R, G, B, W }), (3, new[] { R, G, 0xFF9F5F00u, 0xFF5F9F00u }) })
        {
            byte[] texels = new byte[8]; for (int i = 0; i < 2; i++) { texels[i * 4] = 0xE4; }   // first row of both blocks: indices 0,1,2,3
            byte[] info = [0, (byte)(mode << 6), 0, (byte)(mode << 6)];
            var cset = NitroTextureSet.FromBtx(Btx([new("c", NitroTextureFormat.Compressed4x4, 8, 4 + 4, texels.Concat(texels).ToArray(), Info: info.Concat(info).ToArray())], [("c", mode == 2 ? [red, green, blue, white] : [red, green, blue, white])]));
            var image = cset.Decode(cset.Texture("c")!, cset.Palette("c"));
            check(image.Pixels.Take(4).Select(p => p >> 24 == 0 ? 0u : p).SequenceEqual(expected), $"MapRender_Compressed4x4Mode{mode}");
        }
        Throws<InvalidDataException>(() => NitroTextureSet.FromBtx(Encoding.ASCII.GetBytes("BMD0").Concat(new byte[40]).ToArray()), check, "MapRender_WrongMagicIsRefused");
        Throws<InvalidDataException>(() => NitroTextureSet.FromBtx(btx.Take(btx.Length / 2).ToArray()), check, "MapRender_TruncatedFileIsRefused");

        // Models: a triangle, a quad, strips, vertex formats, position scale and node translation
        var tri = DisplayList([(0x40, [0u]), TexCoord(0, 0), .. Vtx16((0, 0, 0)), TexCoord(8, 0), .. Vtx16((1, 0, 0)), TexCoord(0, 8), .. Vtx16((0, 0, 1)), (0x41, [])]);
        var model = NitroModels.Read(Bmd(2, [tri])).Single();
        check(model.Name == "m" && model.Triangles.Count == 1 && model.Triangles[0].B == new Vector3(2, 0, 0) && model.Triangles[0].C == new Vector3(0, 0, 2) && model.Triangles[0].Ub == new Vector2(8, 0), "MapRender_TriangleUsesPositionScaleAndTexels");
        check(model.Materials.Single() is { Texture: "t", Palette: "p", Alpha: 31, Width: 8, Height: 8 }, "MapRender_MaterialBindsTextureAndPaletteByName");
        var quad = DisplayList([(0x40, [1u]), .. Vtx16((0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)), (0x41, [])]);
        check(NitroModels.Read(Bmd(1, [quad])).Single().Triangles.Count == 2, "MapRender_QuadIsTwoTriangles");
        var tstrip = DisplayList([(0x40, [2u]), .. Vtx16((0, 0, 0), (1, 0, 0), (0, 0, 1), (1, 0, 1), (0, 0, 2)), (0x41, [])]);
        var qstrip = DisplayList([(0x40, [3u]), .. Vtx16((0, 0, 0), (1, 0, 0), (0, 0, 1), (1, 0, 1), (0, 0, 2), (1, 0, 2)), (0x41, [])]);
        check(NitroModels.Read(Bmd(1, [tstrip])).Single().Triangles.Count == 3 && NitroModels.Read(Bmd(1, [qstrip])).Single().Triangles.Count == 4, "MapRender_StripsProduceEveryTriangle");
        var formats = DisplayList([(0x40, [0u]), (0x23, [Xy(4096, 0), 0u]), (0x25, [Xy(0, 4096)]), (0x28, [(uint)(64 | 64 << 20)]), (0x41, [])]);
        var f = NitroModels.Read(Bmd(1, [formats])).Single().Triangles.Single();
        check(f.A == new Vector3(1, 0, 0) && f.B == new Vector3(0, 1, 0) && Vector3.Distance(f.C, new Vector3(64 / 4096f, 1, 64 / 4096f)) < 1e-6, "MapRender_VertexXyAndDifferenceFormats");
        var v10 = DisplayList([(0x40, [0u]), (0x24, [(uint)(64 | 128 << 10 | 0x3C0 << 20)]), .. Vtx16((0, 0, 0), (1, 0, 0)), (0x41, [])]);
        check(NitroModels.Read(Bmd(1, [v10])).Single().Triangles.Single().A == new Vector3(1, 2, -1), "MapRender_Vertex10BitIsSixPointSixFixed");
        byte[] translated = [0x06, 0x00, 0x00, 0x10, .. BitConverter.GetBytes(10 * 4096), .. BitConverter.GetBytes(0), .. BitConverter.GetBytes(-5 * 4096)];   // flag 6: rotation 0, scale 1
        check(NitroModels.Read(Bmd(1, [tri], node: translated)).Single().Triangles[0].A == new Vector3(10, 0, -5), "MapRender_NodeTranslationAppliesThroughTheBytecode");
        check(NitroModels.Read(Bmd(1, [tri, quad])).Single().Triangles.Count == 3, "MapRender_EveryShapeIsDrawn");
        check(NitroModels.Read(Bmd(1, [tri], sbc: [0x06, 0, 0, 0, 0x02, 0, 0, 0x05, 0, 0x01])).Single().Triangles.Count == 0, "MapRender_HiddenNodesAreNotDrawn");
        Throws<InvalidDataException>(() => NitroModels.Read(Bmd(1, [tri], sbc: [0x1F, 0x01])), check, "MapRender_UnknownBytecodeIsRefused");
        Throws<InvalidDataException>(() => NitroModels.Read(Bmd(1, [DisplayList((0x99, []))])), check, "MapRender_UnknownGxCommandIsRefused");
        Throws<InvalidDataException>(() => NitroModels.Read(Bmd(1, [tri]).Take(60).ToArray()), check, "MapRender_TruncatedModelIsRefused");

        // Renderer: textured triangle, highest surface wins, transparent texels and missing textures
        var texSet = NitroTextureSet.FromBtx(Btx([new("t", NitroTextureFormat.Palette4, 8, 8, Enumerable.Repeat((byte)0x55, 16).ToArray(), Repeat: true)], [("p", [red, green, blue, white])]));
        var floor = NitroModels.Read(Bmd(1, [quad])).Single();
        var big = new NitroPlacedModel(floor, Matrix4x4.CreateScale(8), texSet);
        var r = new NitroTopDownRenderer(8, 8, 0, 0, 1, 0xFF000000); r.Draw(big);
        check(r.Pixels.All(p => p == G) && r.Drawn == 2 && r.Untextured == 0, "MapRender_TexturedQuadCoversItsArea");
        var high = NitroModels.Read(Bmd(1, [DisplayList([(0x40, [1u]), .. Vtx16((0, 1, 0), (1, 1, 0), (1, 1, 1), (0, 1, 1)), (0x41, [])])])).Single();
        var noTexture = new NitroPlacedModel(high, Matrix4x4.CreateScale(4, 1, 4), null);
        r.Draw(noTexture); r.Draw(big);
        check(r.Pixels[0] == 0xFFFFFFFF && r.Pixels[63] == G, "MapRender_TheHighestSurfaceIsSeenFromAbove");
        var missing = new NitroTopDownRenderer(4, 4, 0, 0, 1); missing.Draw(new NitroPlacedModel(floor, Matrix4x4.CreateScale(4), NitroTextureSet.FromBtx(Btx([], []))));
        check(missing.Untextured == 2 && missing.Pixels.All(p => p == 0xFFFFFFFF), "MapRender_MissingTexturesAreReportedAndDrawnFlat");
        var transparent = NitroTextureSet.FromBtx(Btx([new("t", NitroTextureFormat.Palette4, 8, 8, new byte[16], Color0Transparent: true)], [("p", [red, green, blue, white])]));
        var holes = new NitroTopDownRenderer(4, 4, 0, 0, 1, 0xFF123456); holes.Draw(new NitroPlacedModel(floor, Matrix4x4.CreateScale(4), transparent));
        check(holes.Pixels.All(p => p == 0xFF123456), "MapRender_TransparentTexelsLeaveWhatIsBelow");
        Throws<ArgumentOutOfRangeException>(() => new NitroTopDownRenderer(0, 4, 0, 0, 1), check, "MapRender_EmptyCanvasIsRefused");

        // Resolver: header → area → archives, refusals
        byte[] headers = new byte[48]; headers[0] = 1; headers[24] = 9;
        byte[] area0 = [0, 0, 0, 0, 0, 0, 0, 0], area1 = [0, 0, 1, 0, 0, 0, 2, 0];
        byte[] build = [2, 0, 1, 0, 0, 0];
        var assets = new PlatinumMapAssets(headers, NdsGen5RandomizerChecks.BuildNarc([area0, area1]), NdsGen5RandomizerChecks.BuildNarc([build]), NdsGen5RandomizerChecks.BuildNarc([btx]),
            NdsGen5RandomizerChecks.BuildNarc([btx, btx]), NdsGen5RandomizerChecks.BuildNarc([Bmd(1, [tri]), Bmd(1, [quad])]), NdsGen5RandomizerChecks.BuildNarc([LandData(Bmd(1, [quad]))]));
        check(assets.Area(0) is { Area: 1, PropArchive: 0, MapTextureArchive: 1, LightArchive: 2 } a && a.PropModels.SequenceEqual([1, 0]), "MapRender_HeaderResolvesItsAreaAndModelList");
        Throws<InvalidDataException>(() => assets.Area(1), check, "MapRender_AnAreaOutsideTheArchiveIsRefused");
        check(assets.LandModel(0).Single().Triangles.Count == 2 && assets.PropModel(0).Single().Triangles.Count == 1, "MapRender_LandAndPropModelsAreRead");
        var props = new[] { new PlatinumMapProp(0, 1, 16 * 4096, 0, 0, 0, 0, 0, 4096, 4096, 4096, 0), new PlatinumMapProp(1, 5, 0, 0, 0, 0, 0, 0, 4096, 4096, 4096, 0) };
        var scene = assets.BlockScene(0, 0, props, out var skipped);
        check(scene.Count == 2 && Vector3.Transform(Vector3.Zero, scene[1].Transform) == new Vector3(16, 0, 0) && skipped.Single().Contains("modelo 5"), "MapRender_BlockScenePlacesPropsAndSkipsModelsTheAreaDoesNotLoad");
        Throws<InvalidDataException>(() => new PlatinumMapAssets(headers, NdsGen5RandomizerChecks.BuildNarc([area0, area1]), NdsGen5RandomizerChecks.BuildNarc([build]), NdsGen5RandomizerChecks.BuildNarc([btx]),
            NdsGen5RandomizerChecks.BuildNarc([btx, btx]), NdsGen5RandomizerChecks.BuildNarc([Bmd(1, [tri])]), NdsGen5RandomizerChecks.BuildNarc([LandData(Bmd(1, [quad]))])).Area(0), check, "MapRender_AModelListNamingAMissingModelIsRefused");
    }
    /// <summary>A land_data member: 0x800 bytes of attributes, no props, the model, no BDHC.</summary>
    internal static byte[] LandData(byte[] model)
    {
        var w = new Writer(); w.U32(0x800); w.U32(0); w.U32(model.Length); w.U32(0); w.Bytes(new byte[0x800]); w.Bytes(model); w.Pad4(); return w.ToArray();
    }
}
