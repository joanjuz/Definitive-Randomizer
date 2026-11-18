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
        check(backend.Capabilities is { Graphics: true, Events: true, Editing: false, GraphicsByDefault: true } && backend.Mutations is null && backend.Events.ZoneEvents(0).Count == 0,
            "Gen7Map_ReadOnlyAndAreasWithoutEventPackHaveNoEvents");
        check(backend.Resources.Graphic(0, 0, 64) is null, "Gen7Map_BlocksWithoutModelsHaveNoGraphic");

        // Event pack: part 1 (NPC) with one zone file holding one 120-byte record of type 1 at world (5250, 0, 2100).
        byte[] Zone(int size, int type, float x, float z, int count = 1)
        {
            var d = new byte[4 + (size * count)];
            BinaryPrimitives.WriteInt32LittleEndian(d, count);
            for (int i = 0; i < count; i++)
            {
                int o = 4 + (i * size);
                BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o), (uint)type);
                BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(o + 4), x); BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(o + 12), z);
                BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o + 44), 77);
                if (size == 120) BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o + 52), 300);
            }
            return d;
        }
        var parts = Enumerable.Range(0, 17).Select(_ => Array.Empty<byte>()).ToArray();
        parts[1] = Map3dsGen6Checks.Pack("EM", Zone(120, 1, 5250, 2100));
        parts[2] = Map3dsGen6Checks.Pack("EM", Zone(88, 3, 100, 100));
        files[0] = Map3dsGen6Checks.Pack("ED", parts);
        var withEvents = new Gen7MapBackend("Prueba", 22, i => files.GetValueOrDefault(i, []), 10, _ => []);
        var evs = withEvents.Events.ZoneEvents(0);
        var npc = evs.First(e => e.Kind == MapEventKind.Npc);
        check(evs.Count == 2 && npc.X == 50 && npc.Y == 20 && npc.Placed && npc.Properties.Any(p => p.Name == "Modelo" && p.Value == "300")
            && npc.Properties.Any(p => p.Name == "Evento" && p.Value == "77") && npc.Properties.All(p => !p.Writable),
            "Gen7Events_NpcRecordPositionModelAndEventFromTheCornerOrigin");
        parts[1] = Map3dsGen6Checks.Pack("EM", Zone(120, 9, 5250, 2100));
        files[0] = Map3dsGen6Checks.Pack("ED", parts);
        var wrongType = new Gen7MapBackend("Prueba", 22, i => files.GetValueOrDefault(i, []), 10, _ => []);
        var wrongEvents = wrongType.Events.ZoneEvents(0);
        check(wrongEvents.Count == 1 && wrongEvents[0].Kind == MapEventKind.Warp && wrongType.LastGraphicError!.Contains("tipo 9"),
            "Gen7Events_KindsWithUnexpectedRecordTypesAreNotShown");
        bool overflow = false;
        try { _ = Gen7Events.Records(Zone(60, 0, 0, 0, 2)[..70], 60, 0).ToList(); } catch (MapFormatException) { overflow = true; }
        check(overflow, "Gen7Events_TruncatedTablesAreRejected");

        // Collision: two triangles, attributes 3 and 5.
        var col = new byte[8 + (2 * 72)];
        BinaryPrimitives.WriteUInt32LittleEndian(col, Gen7Collision.Magic); BinaryPrimitives.WriteInt32LittleEndian(col.AsSpan(4), 2);
        float[][] verts = [[0, 0, 0], [4200, 0, 0], [0, 0, 4200], [4200, 5, 0], [4200, 5, 4200], [0, 5, 4200]];
        for (int k = 0; k < 6; k++) for (int c = 0; c < 3; c++) BinaryPrimitives.WriteSingleLittleEndian(col.AsSpan(8 + ((k / 3) * 72) + ((k % 3) * 16) + (c * 4)), verts[k][c]);
        BinaryPrimitives.WriteUInt32LittleEndian(col.AsSpan(8 + 64), 3); BinaryPrimitives.WriteUInt32LittleEndian(col.AsSpan(8 + 72 + 64), 5);
        var tris = Gen7Collision.Read(col);
        check(tris.Count == 2 && tris[0].B.X == 4200 && tris[1].A.Y == 5 && tris[0].Attribute == 3 && tris[1].Attribute == 5, "Gen7Collision_ReadsVerticesAndAttributes");
        var diagImage = Gen6TopDownRenderer.Render([Gen7Collision.ToDiagnosticModel(tris)], _ => null, 32, 0, 0, 4200);
        int covered = 0; for (int i = 3; i < diagImage.Pixels.Length; i += 4) if (diagImage.Pixels[i] != 0) covered++;
        check(covered > 32 * 32 * 0.95 && diagImage.Pixels[2] != diagImage.Pixels[((31 * 32) + 31) * 4 + 2] | diagImage.Pixels[1] != diagImage.Pixels[((31 * 32) + 31) * 4 + 1],
            "Gen7Collision_DiagnosticModelCoversTheTrianglesWithOneColourPerAttribute");
        bool badMagic = false, badCount = false;
        try { Gen7Collision.Read(new byte[16]); } catch (MapFormatException) { badMagic = true; }
        try { var broken = (byte[])col.Clone(); BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(4), 9); Gen7Collision.Read(broken); } catch (MapFormatException) { badCount = true; }
        check(badMagic && badCount, "Gen7Collision_RejectsOtherFilesAndTruncatedMeshes");
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
        var statics = backend.Statics(world);
        Console.WriteLine($"Área {backend.Zones.FirstOrDefault(z => z.Id == world)?.Name}: objetos estáticos {statics.Models.Count}, colocaciones {backend.StaticPlacements(world).Count}, texturas de objetos {statics.Textures.Count}");
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
        var here = backend.ZoneEvents(world);
        Console.WriteLine($"Eventos del área {world}: {here.Count} · " + string.Join(", ", here.GroupBy(e => e.Properties[0].Value).Select(g => $"{g.Key} {g.Count()}")) + $" · fuera de la matriz {here.Count(e => !e.Placed)}");
        if (Environment.GetEnvironmentVariable("MAP3DS_EVENTS") == "1")
        {
            var totals = new Dictionary<string, int>(); int outside = 0, all = 0, areasWithErrors = 0;
            foreach (var z in backend.Zones)
            {
                string? previous = backend.LastGraphicError;
                var evs = backend.ZoneEvents(z.Id);
                if (backend.LastGraphicError != previous && backend.LastGraphicError is { } err && (err.StartsWith("NPC") || err.StartsWith("Entrenador") || err.StartsWith("Warp") || err.StartsWith("Interacción") || err.StartsWith("Objeto") || err.StartsWith("Activador"))) { areasWithErrors++; if (areasWithErrors <= 5) Console.WriteLine($"  área {z.Id}: {err}"); }
                foreach (var e in evs) { totals[e.Properties[0].Value] = totals.GetValueOrDefault(e.Properties[0].Value) + 1; all++; if (!e.Placed) outside++; }
            }
            Console.WriteLine($"Todas las áreas: {all} eventos · " + string.Join(", ", totals.Select(k => $"{k.Key} {k.Value}")) + $" · fuera de la matriz {outside} · áreas con registros no válidos {areasWithErrors}");
        }
        int opaque = 0; for (int i = 3; i < canvas.Length; i += 4) if (canvas[i] != 0) opaque++;
        Console.WriteLine($"Cobertura: {opaque * 100.0 / (canvas.Length / 4):0.0}% de la imagen con contenido");
        if (Environment.GetEnvironmentVariable("MAP3DS_FAST") == "1") return;
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

    /// <summary>Opens the editor off-screen on a real USUM area (graphics on by default), waits for the blocks and saves a capture.</summary>
    internal static void ProbeUi(string output, string root, int area, bool diagnostic = false)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Usa una carpeta nueva de salida.");
        Directory.CreateDirectory(output);
        var backend = Gen7MapBackend.FromConfig(Map3dsGen6Checks.OpenConfig(root));
        var view = new ThreeDS.Progressive.Map3dsEditorView(backend);
        view.ShowZone(area);
        if (diagnostic) view.DiagnosticToggle!.IsChecked = true;
        var until = DateTime.UtcNow.AddSeconds(120);
        void Lay() { view.Measure(new System.Windows.Size(1600, 1000)); view.Arrange(new System.Windows.Rect(0, 0, 1600, 1000)); view.UpdateLayout(); UiPump.Idle(); }
        do { Lay(); System.Threading.Thread.Sleep(50); } while ((view.Canvas.PendingGraphics > 0 || view.Canvas.GraphicsDrawn == 0 || (diagnostic && view.Canvas.DiagnosticsDrawn == 0)) && DateTime.UtcNow < until);
        Lay();
        var image = new RenderTargetBitmap(1600, 1000, 96, 96, PixelFormats.Pbgra32); image.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        string file = Path.Combine(output, $"gen7-area-{area:000}-editor{(diagnostic ? "-diagnostico" : "")}.png");
        using (var f = File.Create(file)) png.Save(f);
        Console.WriteLine($"Área {area}: {view.Canvas.GraphicsDrawn} bloques, {view.Canvas.DiagnosticsDrawn} de diagnóstico, {view.Canvas.RenderedMarkers} marcadores, {view.EventList.Items.Count} eventos · {file}");
    }

    /// <summary>
    /// Read-only diagnosis of a USUM area: visual coverage (block models and placed statics)
    /// against walkable-ground collision coverage, plus matrix variants. Saves both images and
    /// an overlay (visual in colour, collision-only pixels in magenta).
    /// </summary>
    internal static void GroundProbe(string output, string root, int area, int pixels)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Usa una carpeta nueva de salida.");
        Directory.CreateDirectory(output);
        var config = Map3dsGen6Checks.OpenConfig(root);
        var backend = Gen7MapBackend.FromConfig(config);
        var fields = config.GetlzGARCData("encdata");
        var tr = pk3DS.Core.Maps.Gen6.Gen6Pack.Read(fields[(area * Gen7MapBackend.SlotsPerWorld) + 2], "TR");
        var grid = tr.Part(0);
        Console.WriteLine($"Área {backend.Zones.FirstOrDefault(z => z.Id == area)?.Name}: TR con {tr.Count} archivo(s); u32 +12 = {BinaryPrimitives.ReadUInt32LittleEndian(grid[12..])}; u32 +16 (variantes) = {BinaryPrimitives.ReadUInt32LittleEndian(grid[16..])}");
        var layout = backend.Resources.Layout(area);
        int w = layout.Width * pixels, h = layout.Height * pixels;
        byte[] Compose(Func<int, MapBlockImage?> f)
        {
            var c = new byte[w * h * 4];
            for (int r = 0; r < layout.Height; r++) for (int col = 0; col < layout.Width; col++)
            {
                int b = layout.BlockAt(col, r); if (b < 0) continue;
                var img = f(b); if (img is null) continue;
                for (int y = 0; y < pixels; y++) Buffer.BlockCopy(img.Pixels, y * pixels * 4, c, (((r * pixels) + y) * w + (col * pixels)) * 4, pixels * 4);
            }
            return c;
        }
        var visual = Compose(b => backend.Resources.Graphic(area, b, pixels));
        var collision = Compose(b => backend.Resources.Diagnostic(area, b, pixels));
        int vis = 0, col2 = 0, both = 0, onlyCollision = 0, onlyVisual = 0;
        var overlay = new byte[visual.Length];
        for (int i = 0; i < visual.Length; i += 4)
        {
            bool v = visual[i + 3] != 0, c = collision[i + 3] != 0;
            if (v) vis++; if (c) col2++; if (v && c) both++; if (c && !v) onlyCollision++; if (v && !c) onlyVisual++;
            if (v) { overlay[i] = visual[i]; overlay[i + 1] = visual[i + 1]; overlay[i + 2] = visual[i + 2]; overlay[i + 3] = 255; }
            else if (c) { overlay[i] = 0xC0; overlay[i + 1] = 0x30; overlay[i + 2] = 0xC0; overlay[i + 3] = 255; }
        }
        int n = visual.Length / 4;
        var tris = backend.GroundCollision(area);
        Console.WriteLine($"Colisión de suelo: {tris.Count} triángulos, {tris.Select(t => t.Attribute).Distinct().Count()} atributos; altura {(tris.Count > 0 ? tris.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))) : 0):0}..{(tris.Count > 0 ? tris.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))) : 0):0}");
        Console.WriteLine($"Cobertura visual {vis * 100.0 / n:0.0}% · colisión {col2 * 100.0 / n:0.0}% · ambas {both * 100.0 / n:0.0}% · solo colisión {onlyCollision * 100.0 / n:0.0}% · solo visual {onlyVisual * 100.0 / n:0.0}%");
        void Save(byte[] px, string name)
        {
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
            using var f = File.Create(Path.Combine(output, name)); png.Save(f);
        }
        Save(visual, $"area-{area:000}-visual.png"); Save(collision, $"area-{area:000}-colision.png"); Save(overlay, $"area-{area:000}-superpuesto.png");
        Console.WriteLine("Solo lectura; imágenes privadas en " + output);
    }
}
