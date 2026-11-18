using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Ctr;
using pk3DS.Core.Maps.Gen6;
using pk3DS.Core.Maps.Gen7;

/// <summary>Gen7 (Ultra Sun / Ultra Moon) world matrix, GFL2 textures and the read-only adapter.</summary>
internal static class Map3dsGen7Checks
{
    internal static byte[] Tr(int width, int height, int size, params int[] ids)
    {
        var d = new byte[Gen7World.HeaderSize + (width * height * Gen7World.RecordSize) + 16];
        BinaryPrimitives.WriteInt32LittleEndian(d, width); BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(4), height); BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(8), size);
        for (int i = 0; i < width * height; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(Gen7World.HeaderSize + (i * Gen7World.RecordSize)), (ushort)(i < ids.Length && ids[i] >= 0 ? ids[i] : 0xFFFF));
        return d;
    }

    internal static byte[] Texture(string name, int format, int width, int height, byte[] pixels)
    {
        var d = new byte[0x80 + pixels.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(d, Gfl2File.TextureMagic); BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(4), 1);
        Encoding.ASCII.GetBytes("texture").CopyTo(d, 8);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(0x10), pixels.Length + 0x68);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(0x18), pixels.Length);
        Encoding.ASCII.GetBytes(name).CopyTo(d, 0x28);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(0x68), (ushort)width); BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(0x6A), (ushort)height);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(0x6C), (ushort)format);
        pixels.CopyTo(d, 0x80);
        return d;
    }

    internal static void Run(Action<bool, string> check)
    {
        var w = new Gen7World(Tr(8, 2, 4200, 392, 394, 396, -1, 400, 402, 404, 406, 393, 395, 397, 399, 401, 403, 405, 407));
        check(w.Width == 8 && w.Height == 2 && w.BlockSize == 4200 && w.Blocks[0] == 392 && w.Blocks[3] == -1 && w.Blocks[8] == 393 && w.Blocks.Count == 16, "Gen7World_MatrixHeaderAndRecords");
        bool bad = false;
        try { _ = new Gen7World(Tr(40, 40, 4200)[..60]); } catch (MapFormatException) { bad = true; }
        bool zero = false;
        try { _ = new Gen7World(Tr(2, 2, 0)); } catch (MapFormatException) { zero = true; }
        check(bad && zero, "Gen7World_RejectsTruncatedMatricesAndZeroBlockSize");

        var px = new byte[8 * 8 * 4]; px[0] = 0xFF; px[3] = 0x80; // RGBA8 first texel
        var tex = Gfl2File.ReadTexture(Texture("grass.tga", 0x04, 8, 8, px));
        check(tex is { Name: "grass.tga", Width: 8, Height: 8, Format: PicaTextureFormat.RGBA8 } && tex.Raw.Length == 256 && tex.Raw[3] == 0x80, "Gfl2_TextureHeaderAndPixels");
        var shader = Texture("shader", 0x04, 8, 8, px); Encoding.ASCII.GetBytes("shader\0\0").CopyTo(shader, 8);
        check(Gfl2File.ReadTexture(shader) is null && Gfl2File.ReadTexture(new byte[0x90]) is null, "Gfl2_ShadersAndOtherBlobsAreNotTextures");
        bool unknown = false;
        try { Gfl2File.ReadTexture(Texture("x", 0x99, 8, 8, px)); } catch (MapFormatException) { unknown = true; }
        bool shortData = false;
        try { Gfl2File.ReadTexture(Texture("x", 0x04, 16, 16, px)); } catch (MapFormatException) { shortData = true; }
        check(unknown && shortData, "Gfl2_UnknownFormatsAndIncompleteTexturesAreRejected");
        bool notModel = false;
        try { Gfl2File.ReadModel(new byte[0x80]); } catch (MapFormatException) { notModel = true; }
        check(notModel, "Gfl2_ModelMagicIsRequired");

        // Adapter with a synthetic world: 11 files per world; slot 2 = TR pack.
        var files = new Dictionary<int, byte[]> { [2] = Map3dsGen6Checks.Pack("TR", Tr(2, 1, 4200, 5, -1)), [11 + 2] = Map3dsGen6Checks.Pack("TR", Tr(1, 1, 4200, -1)) };
        var backend = new Gen7MapBackend("Prueba", 22, i => files.GetValueOrDefault(i, []), 10, _ => []);
        check(backend.Zones.Count == 1 && backend.Zones[0].Id == 0 && backend.Skipped.Any(s => s.World == 1), "Gen7Map_ListsWorldsWithBlocks");
        var layout = backend.Resources.Layout(0);
        check(layout.Width == 2 && layout.BlockAt(0, 0) == 0 && layout.BlockAt(1, 0) == -1 && backend.Resources.Cells(0) is null, "Gen7Map_LayoutFollowsTheMatrix");
        check(backend.Capabilities is { Graphics: true, Events: false, Editing: false, GraphicsByDefault: true } && backend.Mutations is null && backend.Events.ZoneEvents(0).Count == 0,
            "Gen7Map_ReadOnlyGraphicsWithoutEvents");
        check(backend.Resources.Graphic(0, 0, 64) is null, "Gen7Map_BlocksWithoutModelsHaveNoGraphic");
    }

    /// <summary>Read-only: renders a whole world and reads every world model and texture of a USUM copy.</summary>
    internal static void RenderProbe(string output, string root, int world, int pixels)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Usa una carpeta nueva de salida.");
        Directory.CreateDirectory(output);
        string romfs = Directory.GetDirectories(root).First(x => Path.GetFileName(x).Contains("rom", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(x, "a")));
        string[] watched = ["a/0/8/2", "a/0/8/3", "a/0/8/6"];
        string Hashes() => string.Join("|", watched.Select(p => Path.Combine(romfs, p)).Where(File.Exists).Select(p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))));
        string before = Hashes();
        var backend = Gen7MapBackend.FromConfig(Map3dsGen6Checks.OpenConfig(root));
        Console.WriteLine($"{backend.Title}: {backend.Zones.Count} mundos con bloques, {backend.Skipped.Count} omitidos");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var layout = backend.Resources.Layout(world);
        int w = layout.Width * pixels, h = layout.Height * pixels, triangles = 0, untextured = 0, drawn = 0;
        var canvas = new byte[w * h * 4];
        for (int r = 0; r < layout.Height; r++) for (int c = 0; c < layout.Width; c++)
        {
            int block = layout.BlockAt(c, r);
            if (block < 0) continue;
            var img = backend.Resources.Graphic(world, block, pixels);
            if (img is null) continue;
            drawn++; triangles += img.Triangles; untextured += img.Untextured;
            for (int y = 0; y < pixels; y++) Buffer.BlockCopy(img.Pixels, y * pixels * 4, canvas, (((r * pixels) + y) * w + (c * pixels)) * 4, pixels * 4);
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, canvas, w * 4);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
        string file = Path.Combine(output, $"gen7-world-{world:000}.png");
        using (var f = File.Create(file)) png.Save(f);
        Console.WriteLine($"Mundo {world}: {layout.Width}×{layout.Height}, {drawn} celdas dibujadas, {triangles} triángulos (suma por celda), {untextured} sin textura, {sw.ElapsedMilliseconds} ms · {file}");
        sw.Restart();
        int models = 0, textures = 0, textureErrors = 0;
        foreach (var z in backend.Zones)
        {
            models += backend.Models(z.Id).Count;
            foreach (var t in backend.Textures(z.Id).Values.Distinct())
            {
                try { _ = t.Decode(); textures++; } catch (MapFormatException) { textureErrors++; }
            }
        }
        Console.WriteLine($"Modelos leídos {models} · texturas decodificadas {textures} · con error {textureErrors} · último error {backend.LastGraphicError ?? "ninguno"} · {sw.ElapsedMilliseconds} ms");
        bool same = Hashes() == before;
        Console.WriteLine((same ? "PASS" : "FAIL") + " Gen7RenderReal_SourceUnchanged; gameplay NOT tested");
        if (!same) throw new InvalidDataException("La copia de origen cambió.");
    }
}
