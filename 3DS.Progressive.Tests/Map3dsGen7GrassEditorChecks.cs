using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Ctr;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;

/// <summary>
/// «Añadir hierba visible» in the USUM map editor: a synthetic grass model (a GFL2 blob the project's reader parses: one
/// material, one mesh of float positions) and synthetic areas, then the panel (template, centre, preview, save, undo)
/// and, on real copies, the whole flow through the editor and the history.
/// </summary>
internal static class Map3dsGen7GrassEditorChecks
{
    private static byte[] Name40(string name) { var b = new byte[0x44]; Encoding.ASCII.GetBytes(name).CopyTo(b, 4); return b; }
    private static byte[] Table(params string[] names) => [.. BitConverter.GetBytes(names.Length), .. names.SelectMany(Name40)];
    private static byte[] ByteString(string s) => [(byte)s.Length, .. Encoding.ASCII.GetBytes(s)];
    private static void Align16(List<byte> b) { while (b.Count % 16 != 0) b.Add(0); }
    private static byte[] Section(string name, byte[] data)
    {
        var head = new byte[16];
        Encoding.ASCII.GetBytes(name).CopyTo(head, 0);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(8), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(12), -1);
        return [.. head, .. data];
    }
    private static byte[] Commands(params (uint Value, int Register)[] writes) =>
        [.. writes.SelectMany(w => BitConverter.GetBytes(w.Value).Concat(BitConverter.GetBytes((uint)w.Register | 0xF0000u)))];

    /// <summary>
    /// A grass model blob ("Map07_encgrass01", one material with texture <paramref name="texture"/>, one mesh): a quad of
    /// float32 positions from (x, z) to (x + size, z + size) at height y, padded to 0x80 as the game's.
    /// </summary>
    internal static byte[] GrassModel(float x, float z, float size, float y, string texture = "fe_grass.tga")
    {
        var model = new List<byte>();
        model.AddRange(Table("Map07_encgrass01GREF")); model.AddRange(Table(texture)); model.AddRange(Table("blc00")); model.AddRange(Table("blc00_OptMesh"));
        foreach (float f in new[] { x, y, z, 0f, x + size, y + 50, z + size, 0f }) model.AddRange(BitConverter.GetBytes(f));
        foreach (float f in new[] { 1f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1 }) model.AddRange(BitConverter.GetBytes(f));
        model.AddRange(new byte[16]); // unknown length/offset and 8 bytes
        model.AddRange(new byte[16]); // no bones, 12 bytes
        Align16(model);
        model.AddRange(new byte[8]); // no LUTs
        // gfmodel content starts at 32 in the blob, so 16-byte alignment inside it is the blob's.
        while ((32 + model.Count) % 16 != 0) model.Add(0);

        var material = new List<byte>();
        material.AddRange(new byte[4]); material.AddRange(ByteString("blc00"));
        for (int k = 0; k < 3; k++) { material.AddRange(new byte[4]); material.AddRange(ByteString("Kusa")); }
        material.AddRange(new byte[168]);
        material.AddRange(BitConverter.GetBytes(1u));
        material.AddRange(new byte[4]); material.AddRange(ByteString(texture)); material.Add(0); material.Add(0);
        foreach (float f in new[] { 1f, 1f, 0f, 0f, 0f }) material.AddRange(BitConverter.GetBytes(f));
        material.AddRange(new byte[20]);
        material.AddRange(new byte[32]); // aligned command length 0 and room for the reader's skip

        float[] quad = [x, y, z, x + size, y, z, x, y, z + size, x + size, y, z + size];
        byte[] vertices = [.. quad.SelectMany(BitConverter.GetBytes)];
        byte[] indices = [.. new ushort[] { 0, 1, 2, 1, 3, 2 }.SelectMany(BitConverter.GetBytes)];
        var mesh = new List<byte>();
        mesh.AddRange(new byte[104]);
        mesh.AddRange(BitConverter.GetBytes(1)); // one sub-mesh
        mesh.AddRange(new byte[20]);
        byte[][] blocks =
        [
            Commands((0xB, 0x201), (0, 0x204), (12u << 16, 0x205), (0, 0x242), (0, 0x2BB)),
            [],
            Commands((0x80000000, 0x227), (6, 0x228), (0, 0x25E)),
        ];
        for (int i = 0; i < 3; i++)
        {
            mesh.AddRange(BitConverter.GetBytes(blocks[i].Length)); mesh.AddRange(BitConverter.GetBytes((uint)i)); mesh.AddRange(BitConverter.GetBytes(3u)); mesh.AddRange(new byte[4]);
            mesh.AddRange(blocks[i]);
        }
        mesh.AddRange(new byte[4]); mesh.AddRange(BitConverter.GetBytes(5)); mesh.AddRange(Encoding.ASCII.GetBytes("blc00"));
        mesh.AddRange(new byte[32]); mesh.AddRange(new byte[8]);
        mesh.AddRange(BitConverter.GetBytes(vertices.Length)); mesh.AddRange(BitConverter.GetBytes(indices.Length));
        mesh.AddRange(vertices); mesh.AddRange(indices);

        var body = new List<byte>();
        body.AddRange(Section("gfmodel", [.. model])); body.AddRange(Section("material", [.. material])); body.AddRange(Section("mesh", [.. mesh]));
        var output = new byte[(16 + body.Count + 0x7F) / 0x80 * 0x80];
        BinaryPrimitives.WriteUInt32LittleEndian(output, Gfl2File.ModelMagic);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), 3);
        body.ToArray().CopyTo(output, 16);
        return output;
    }

    /// <summary>Ground collision (part 6) with the given triangles (A, B, C, attribute), padded to 0x80.</summary>
    internal static byte[] Collision(params (Vector3 A, Vector3 B, Vector3 C, uint Attribute)[] tris)
    {
        var col = new byte[(8 + (tris.Length * 72) + 0x7F) / 0x80 * 0x80];
        BinaryPrimitives.WriteUInt32LittleEndian(col, Gen7Collision.Magic); BinaryPrimitives.WriteInt32LittleEndian(col.AsSpan(4), tris.Length);
        for (int i = 0; i < tris.Length; i++)
        {
            int at = 8 + (i * 72);
            foreach (var (k, p) in new[] { (0, tris[i].A), (16, tris[i].B), (32, tris[i].C) })
            { BinaryPrimitives.WriteSingleLittleEndian(col.AsSpan(at + k), p.X); BinaryPrimitives.WriteSingleLittleEndian(col.AsSpan(at + k + 4), p.Y); BinaryPrimitives.WriteSingleLittleEndian(col.AsSpan(at + k + 8), p.Z); }
            BinaryPrimitives.WriteUInt32LittleEndian(col.AsSpan(at + 64), tris[i].Attribute);
        }
        return col;
    }

    /// <summary>Flat ground over X/Z 0–2100 × 0–1050 at height y: grass 0x1F on X &lt; 1050, plain 0x01 beyond.</summary>
    internal static byte[] Ground(float y = 0) => Collision(
        (new(0, y, 0), new(1050, y, 0), new(0, y, 1050), 0x1F), (new(1050, y, 0), new(1050, y, 1050), new(0, y, 1050), 0x1F),
        (new(1050, y, 0), new(2100, y, 0), new(1050, y, 1050), 0x01), (new(2100, y, 0), new(2100, y, 1050), new(1050, y, 1050), 0x01));

    internal static byte[] MapBlock(byte[] grass, byte[] ground) => Gen7VisibleGrass.PackBlock([new byte[0x80], grass, [], [], [], [], ground, new byte[0x80]]);

    /// <summary>
    /// Blocks: 1 ground without grass; 2 ground with the template patch (X/Z 100–400); 3 ground without grass; 4 two
    /// floors (0 and 200). Areas: 0 block 1 with the grass textures (the target); 1 block 2 with textures (the template,
    /// used by the game); 2 block 3 without textures; 3 block 4 with textures.
    /// </summary>
    internal static (Dictionary<int, byte[]> Areas, byte[][] Blocks) Data()
    {
        // PICA RGBA8 stores A, B, G, R: an opaque green texel.
        var px = new byte[8 * 8 * 4]; for (int i = 0; i < px.Length; i += 4) { px[i] = 0xFF; px[i + 1] = 0x40; px[i + 2] = 0xA0; px[i + 3] = 0x40; }
        byte[] textures = Gen7CatchZones.Pack("BG", [Gen7CatchZones.Pack("BG", [Map3dsGen7Checks.Texture("fe_grass.tga", 0x04, 8, 8, px)])]);
        byte[] none = Gen7CatchZones.Pack("BG", [Gen7CatchZones.Pack("BG", [Map3dsGen7Checks.Texture("other.tga", 0x04, 8, 8, px)])]);
        byte[] Tr(int block) => Map3dsGen6Checks.Pack("TR", Map3dsGen7Checks.Tr(1, 1, 4200, block));
        var ed = Map3dsGen7CatchZoneChecks.Ed(Map3dsGen7CatchZoneChecks.RegionFile());
        var areas = new Dictionary<int, byte[]>();
        void Area(int a, int block, byte[] bg) { areas[a * 11] = ed; areas[(a * 11) + 1] = bg; areas[(a * 11) + 2] = Tr(block); areas[(a * 11) + 9] = Gen7CatchZones.Pack("EA", []); }
        Area(0, 1, textures); Area(1, 2, textures); Area(2, 3, none); Area(3, 4, textures);
        var twoFloors = Collision((new(0, 0, 0), new(2100, 0, 0), new(0, 0, 1050), 0x01), (new(0, 200, 0), new(2100, 200, 0), new(0, 200, 1050), 0x01));
        byte[][] blocks = [MapBlock([], Ground()), MapBlock([], Ground()), MapBlock(GrassModel(100, 100, 300, 0), Ground()), MapBlock([], Ground()), MapBlock([], twoFloors)];
        return (areas, blocks);
    }

    internal static Gen7MapBackend Backend((Dictionary<int, byte[]> Areas, byte[][] Blocks) d) =>
        new("Prueba", 44, i => d.Areas.GetValueOrDefault(i, []), d.Blocks.Length, id => d.Blocks[id], new Dictionary<int, string> { [0] = "Destino", [1] = "Ruta de prueba", [2] = "Sin texturas", [3] = "Dos pisos" });

    internal static void Run(Action<bool, string> check)
    {
        var blob = GrassModel(100, 100, 300, 0);
        var model = Gfl2File.ReadModel(blob);
        var (min, max) = Gen7VisibleGrass.Bounds(blob);
        check(model.Meshes.Count == 1 && model.Materials[0].Name == "blc00" && Gen7VisibleGrass.IsGrassModel(blob) && Gen7VisibleGrass.MeshCount(blob) == 1
            && min == new Vector3(100, 0, 100) && max == new Vector3(400, 0, 400) && Gen7VisibleGrass.TextureNames(blob).SequenceEqual(["fe_grass"]),
            "Gen7GrassEditor_SyntheticGrassModelIsReadByTheProjectReader");
        var moved = Gen7VisibleGrass.Translate(blob, new Vector3(1000, 5, 200));
        check(Gen7VisibleGrass.Bounds(moved) == (new Vector3(1100, 5, 300), new Vector3(1400, 5, 600)), "Gen7GrassEditor_SyntheticModelMoves");

        var d = Data(); var b = Backend(d);
        var choices = b.GrassChoices(0);
        check(b.GrassTemplates.Select(t => t.Block).SequenceEqual([2]) && choices.Count == 1 && choices[0].Name.StartsWith("Hierba alta de Ruta de prueba") && choices[0].Name.Contains("3 × 3 casillas")
            && b.GrassChoices(2).Count == 0 && b.AreasWithBlock(2).SequenceEqual([1]), "Gen7GrassEditor_TemplatesHaveReadableNamesAndNeedTheAreaTextures");
        var (plan, reason) = b.PlanGrass(0, (10, 5), 2);
        check(plan is { TargetBlock: 1, Covered.Count: 9 } && plan.Delta == new Vector3(852.5f, 0, 327.5f) && plan.GroundSpread == 0, "Gen7GrassEditor_PlanCentresThePatchOnTheCell " + reason);
        var parts = Gen7VisibleGrass.BlockParts(b.BlockWithGrass(plan!));
        check(Gen7VisibleGrass.Bounds(parts[1]) == (new Vector3(952.5f, 0, 427.5f), new Vector3(1252.5f, 0, 727.5f)) && parts[6].AsSpan().SequenceEqual(Gen7VisibleGrass.BlockParts(d.Blocks[1])[6]),
            "Gen7GrassEditor_WrittenBlockHasThePatchAndTheSameGround");
        var (noTex, texReason) = b.PlanGrass(2, (10, 5), 2);
        var (hasGrass, grassReason) = b.PlanGrass(1, (10, 5), 2);
        var (edge, edgeReason) = b.PlanGrass(0, (0, 0), 2);
        var (bare, bareReason) = b.PlanGrass(0, (30, 5), 2);
        var (floors, floorsReason) = b.PlanGrass(3, (10, 5), 2);
        check(noTex is null && texReason.Contains("texturas") && hasGrass is null && grassReason.Contains("ya tiene hierba") && edge is null && edgeReason.Contains("se saldría del bloque")
            && bare is null && bareReason.Contains("no tiene suelo") && floors is null && floorsReason.Contains("varios niveles"), "Gen7GrassEditor_InvalidPlacesAreRefused");
        check(b.VisibleGrassCells(1).Count == 9 && b.VisibleGrassCells(0).Count == 0, "Gen7GrassEditor_VisibleGrassCellsOfAnArea");
        var copied = (d.Areas, d.Blocks.Select((x, i) => i == 1 ? b.BlockWithGrass(plan!) : x).ToArray());
        var c = Backend(copied);
        check(c.GrassTemplates.Select(t => t.Block).SequenceEqual([1, 2]) && c.SamePatch(c.GrassTemplates[0], c.GrassTemplates[1])
            && c.GrassChoices(0).Select(x => x.Template.Block).SequenceEqual([1]) && c.GrassChoices(0).Count == 1, "Gen7GrassEditor_AMovedCopyIsNotASecondTemplate");
        var (img, ix, iz, span) = b.GrassPreviewImage(plan!, 64);
        check(img.Size == 64 && img.Pixels.Where((_, i) => i % 4 == 3).Any(a => a != 0) && ix == 952.5f && iz == 427.5f && span == 300, "Gen7GrassEditor_PreviewDrawsTheRealModelFromAbove");
    }

    internal static void RunUi(Action<bool, string> check)
    {
        var d = Data();
        var calls = new List<(int Zone, (int X, int Y) Centre, int Template)>();
        int undos = 0; bool undoable = true;
        var mover = new Map3dsMover((b, z, n, x, y) => "m", () => Backend(d),
            paintCatch: (b, z, cells, source) => "Zona de captura",
            addGrass: (b, z, centre, template) => { calls.Add((z, centre, template)); return $"Hierba visible en el área {z:000}"; },
            canUndoGrass: z => undoable, undoGrass: z => { undos++; undoable = false; return "Deshecho"; });
        var view = new Map3dsEditorView(Backend(d), mover);
        view.Measure(new Size(1400, 860)); view.Arrange(new Rect(0, 0, 1400, 860)); view.UpdateLayout(); UiPump.Idle();
        var g6 = Map3dsCatchZoneChecks.Data();
        var gen6View = new Map3dsEditorView(Map3dsCatchZoneChecks.Backend(g6), new Map3dsMover((b, z, n, x, y) => "m", () => Map3dsCatchZoneChecks.Backend(g6),
            addGrass: (b, z, c, t) => "no"));
        var noGrassMover = new Map3dsEditorView(Backend(d), new Map3dsMover((b, z, n, x, y) => "m", () => Backend(d)));
        check(view.GrassButton is { Content: "Añadir hierba visible" } && gen6View.GrassButton is null && noGrassMover.GrassButton is null, "Map3dsGen7GrassUi_ButtonOnlyInUsumWithAProject");

        view.ShowZone(0);
        check(view.ShowGrassPanel() && view.Canvas.PickingCell && view.GrassTemplate is { Items.Count: 1 } && view.GrassSave is { IsEnabled: false }
            && view.GrassUndo is not null && view.Canvas.GrassOverlay is { Footprint: null }, "Map3dsGen7GrassUi_PanelOffersTheGameTemplates");
        view.Canvas.PickCell(10, 5); UiPump.Idle();
        check(view.PendingGrass is { TargetBlock: 1 } && view.GrassResult!.Text.Contains("Se copiará") && view.GrassResult.Text.Contains("no crea encuentros") == false
            && view.GrassResult.Text.Contains("suelo admitido") && view.GrassSave!.IsEnabled
            && view.Canvas.GrassOverlay is { Valid: true, Image: not null, Footprint: not null, Block: not null } o && o.Soil.Count == 3,
            "Map3dsGen7GrassUi_ClickPreviewsTheRealPatchAndItsSurface");
        view.Canvas.PickCell(0, 0); UiPump.Idle();
        check(view.PendingGrass is null && view.GrassResult!.Text.StartsWith("No se puede") && view.GrassSave!.IsEnabled == false && view.Canvas.GrassOverlay is { Valid: false },
            "Map3dsGen7GrassUi_InvalidPlaceIsShownAndCannotBeSaved");
        view.Canvas.PickCell(10, 5); UiPump.Idle();
        check(view.SaveGrass() && calls.SequenceEqual([(0, (10, 5), 2)]), "Map3dsGen7GrassUi_SaveGoesThroughTheProject");
        check(view.ShowGrassPanel() && view.UndoGrass() && undos == 1 && view.GrassUndo is null, "Map3dsGen7GrassUi_UndoFromThePanel");
        view.ShowZone(2);
        check(view.ShowGrassPanel() && view.GrassTemplate is null && view.GrassSave is null && !view.Canvas.PickingCell, "Map3dsGen7GrassUi_AreaWithoutTexturesCannotAddGrass");
        view.ShowZone(1);
        check(view.ShowCatchPanel() && view.Canvas.GrassOverlay is null, "Map3dsGen7GrassUi_CatchZonePanelStillWorksAndClearsThePreview");
    }

    /// <summary>
    /// Real copy in a test folder, through the editor as the app builds it: panel, template, click, preview, save, reopen
    /// (the visible grass is read back, catch cells unchanged), undo from the panel (block archive byte for byte), reopen.
    /// </summary>
    internal static void Probe(string copyRoot, int area, int x, int y, int template)
    {
        string full = Path.GetFullPath(copyRoot);
        if (!full.Contains("Pruebas", StringComparison.OrdinalIgnoreCase)) throw new IOException("Por seguridad, la prueba solo escribe en una copia dentro de una carpeta de pruebas.");
        var session = GameProjectSession.Open(full, false);
        string archive = new Map3dsGen7VisibleGrassService(session).BlockArchive;
        byte[] original = File.ReadAllBytes(archive);
        var view = new Map3dsEditorView(session);
        view.Measure(new Size(1400, 860)); view.Arrange(new Rect(0, 0, 1400, 860)); view.UpdateLayout(); UiPump.Idle();
        var before = (Gen7MapBackend)view.Backend;
        var visibleBefore = before.VisibleGrassCells(area).ToHashSet(); var paintBefore = before.PaintableCells(area).ToHashSet(); var encBefore = before.EncounterCells(area).ToHashSet();
        view.ShowZone(area);
        bool panel = view.GrassButton is not null && view.ShowGrassPanel() && view.GrassTemplate is { Items.Count: > 0 };
        if (view.GrassTemplate is not null) view.GrassTemplate.SelectedValue = template;
        UiPump.Idle();
        view.Canvas.PickCell(x, y); UiPump.Idle();
        bool preview = view.PendingGrass is not null && view.Canvas.GrassOverlay is { Valid: true, Image: not null } && File.ReadAllBytes(archive).AsSpan().SequenceEqual(original);
        Console.WriteLine((panel ? "PASS" : "FAIL") + $" Gen7GrassEditorReal_PanelWithTheGameTemplates ({view.GrassTemplate?.Items.Count} plantillas)");
        Console.WriteLine((preview ? "PASS" : "FAIL") + $" Gen7GrassEditorReal_PreviewWritesNothing: {view.GrassResult?.Text}");
        var plan = view.PendingGrass;
        if (plan is not null)
        {
            // Off-screen capture of the preview (graphic layer on, centred on the patch) for a visual check.
            if (view.GraphicsToggle is { } gfx) gfx.IsChecked = true;
            view.Canvas.ZoomCentered(2.5); UiPump.Idle();
            for (int i = 0; i < 200 && view.Canvas.PendingGraphics > 0; i++) { Thread.Sleep(50); UiPump.Idle(); }
            var png = Path.Combine(Path.GetDirectoryName(full)!, "vista-previa-hierba.png");
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(1400, 860, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(view);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var f = File.Create(png)) enc.Save(f);
            Console.WriteLine($"Captura de la vista previa: {png}");
        }
        bool saved = view.SaveGrass();
        var mb = new GARC.MemGARC(original).Files; var ma = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        var differing = Enumerable.Range(0, Math.Min(ma.Length, mb.Length)).Where(i => !ma[i].AsSpan().SequenceEqual(mb[i])).ToArray();
        bool onlyBlock = saved && plan is not null && differing.SequenceEqual([plan.TargetBlock]);
        Console.WriteLine((onlyBlock ? "PASS" : "FAIL") + $" Gen7GrassEditorReal_SaveChangesOnlyTheTargetBlock (miembros {string.Join(",", differing)}; {view.Status.Text})");
        var reopened = new Map3dsEditorView(GameProjectSession.Open(full, false));
        var after = (Gen7MapBackend)reopened.Backend;
        var visibleAfter = after.VisibleGrassCells(area).ToHashSet();
        bool reread = plan is not null && plan.Covered.Count(visibleAfter.Contains) >= plan.Covered.Count * 3 / 4 && visibleBefore.IsSubsetOf(visibleAfter);
        bool catchSame = after.PaintableCells(area).ToHashSet().SetEquals(paintBefore) && after.EncounterCells(area).ToHashSet().SetEquals(encBefore);
        Console.WriteLine((reread ? "PASS" : "FAIL") + $" Gen7GrassEditorReal_ReopenedWithTheVisibleGrass ({visibleBefore.Count} → {visibleAfter.Count} casillas con hierba visible)");
        Console.WriteLine((catchSame ? "PASS" : "FAIL") + " Gen7GrassEditorReal_CatchZonesUnchanged (casillas con encuentros y disponibles iguales)");
        reopened.ShowZone(area);
        bool undo = reopened.ShowGrassPanel() && reopened.GrassUndo is not null && reopened.UndoGrass();
        bool exact = File.ReadAllBytes(archive).AsSpan().SequenceEqual(original);
        Console.WriteLine((undo && exact ? "PASS" : "FAIL") + " Gen7GrassEditorReal_UndoFromThePanelRestoresTheArchiveByteForByte");
        var last = new Map3dsEditorView(GameProjectSession.Open(full, false));
        bool gone = ((Gen7MapBackend)last.Backend).VisibleGrassCells(area).ToHashSet().SetEquals(visibleBefore);
        Console.WriteLine((gone ? "PASS" : "FAIL") + " Gen7GrassEditorReal_ReopenedAfterUndoWithoutTheGrass");
        if (!(panel && preview && onlyBlock && reread && catchSame && undo && exact && gone)) throw new InvalidDataException("La prueba real del editor de hierba visible falló.");
        Console.WriteLine("Copia sin cambios netos en el archivo de bloques (guardado y deshecho) · gameplay NOT tested");
    }
}
