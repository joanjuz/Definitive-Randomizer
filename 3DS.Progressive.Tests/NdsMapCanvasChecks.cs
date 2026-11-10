using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Point = System.Windows.Point;

/// <summary>Spatial model (pure) and WPF integration checks for the read-only schematic DS event canvas.</summary>
internal static class NdsMapCanvasChecks
{
    private static PlatinumEvent Ev(PlatinumEventKind kind, int index, int x, int y, int type = 0, int rx = 0, int ry = 0) =>
        new(kind, index, index, x, y, 0, 0, type, 0, 0, 0, 0, 0, rx, ry, -1, -1, "00");

    private static byte[] Matrix(int w, int h, Func<int, int>? headers)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { (byte)w, (byte)h, (byte)(headers is null ? 0 : 1), 0, 0 });
        if (headers is not null) for (int i = 0; i < w * h; i++) writer.Write((ushort)headers(i));
        for (int i = 0; i < w * h; i++) writer.Write((ushort)0);
        return stream.ToArray();
    }
    /// <summary>The 411 case: a 30×30 matrix of which only block (3,27) is stored with this map's header.</summary>
    private static PlatinumMapLayout Twinleaf() => PlatinumMapLayout.ParseMatrix(Matrix(30, 30, i => i == 3 + 27 * 30 ? 411 : 300), 411, 0);
    private static PlatinumMapLayout Contextual(int w, int h) => PlatinumMapLayout.ParseMatrix(Matrix(w, h, null), 5, 0);
    private static bool Near(double a, double b, double e = 1e-9) => Math.Abs(a - b) <= e;

    internal static void Run(Action<bool, string> check)
    {
        var twin = Twinleaf();
        var npc0 = Ev(PlatinumEventKind.Npc, 0, 119, 878);
        var model = new NdsMapCanvasModel(twin, [npc0, Ev(PlatinumEventKind.Npc, 1, 5, 5), Ev(PlatinumEventKind.Warp, 0, 130, 880), Ev(PlatinumEventKind.Trigger, 0, 110, 870, 0, 4, 2),
            Ev(PlatinumEventKind.Object, 0, 100, 860), Ev(PlatinumEventKind.Npc, 2, 130, 885, 3)]);
        model.SetViewport(1000, 700);

        // Coordinates, zoom and pan
        model.SetZoom(3.7, 10, 20); model.PanBy(137.5, -42.25);
        var (px, py) = model.TileToPixel(119, 878); var (tx, ty) = model.PixelToTile(px, py);
        check(Near(tx, 119) && Near(ty, 878) && model.TileAt(px + 1, py + 1) == (119, 878), "MapCanvas_TileAndPixelAreExactInverses");
        model.FitMatrix(); model.SetZoom(10, 500, 350); var anchor = model.PixelToTile(500, 350); model.ZoomBy(1.5, 500, 350);
        check(Near(model.PixelToTile(500, 350).X, anchor.X, 1e-6) && Near(model.PixelToTile(500, 350).Y, anchor.Y, 1e-6) && Near(model.Zoom, 15), "MapCanvas_ZoomKeepsTheTileUnderTheCursor");
        model.SetZoom(1e9, 0, 0); bool maxOk = model.Zoom == NdsMapCanvasModel.MaxZoom; model.SetZoom(-5, 0, 0);
        check(maxOk && model.Zoom == NdsMapCanvasModel.MinZoom, "MapCanvas_ZoomIsClampedToLimits");
        model.SetZoom(20, 0, 0); model.PanBy(-1e7, -1e7);
        check(model.OffsetX >= model.WorldMinX * model.Zoom - NdsMapCanvasModel.Margin - 1e-9 && model.OffsetY >= model.WorldMinY * model.Zoom - NdsMapCanvasModel.Margin - 1e-9, "MapCanvas_PanCannotLeaveTheWorldRectangle");
        model.PanBy(1e7, 1e7);
        check(model.OffsetX <= model.WorldMaxX * model.Zoom - model.ViewportWidth + NdsMapCanvasModel.Margin + 1e-9, "MapCanvas_PanIsClampedAtTheFarEdge");
        model.FitMatrix(); var (ox, oy) = model.TileToPixel(0, 0); var (ex, ey) = model.TileToPixel(960, 960);
        check(twin.TileWidth == 960 && twin.TileHeight == 960 && ox >= 0 && oy >= 0 && ex <= 1000 && ey <= 700, "MapCanvas_FitMatrixShows960x960");
        model.FitEvents();
        bool allInside = model.Markers.All(m => { var (a, b) = model.TileToPixel(m.X, m.Y); var (c, d) = model.TileToPixel(m.X + m.Width, m.Y + m.Height); return a >= 0 && b >= 0 && c <= 1000 && d <= 700; });
        check(allInside && model.Zoom > 0.5 && model.Zoom <= 24, "MapCanvas_FitEventsFramesEveryEventWithoutExceedingZoomLimit");
        var empty = new NdsMapCanvasModel(twin, []); empty.SetViewport(1000, 700); double emptyZoom = empty.Zoom; empty.FitMatrix();
        check(Near(emptyZoom, empty.Zoom), "MapCanvas_EmptyMapFitsTheMatrix");
        model.FitEvents(); var (vx0, vy0, vx1, vy1) = model.VisibleTiles();
        model.SetZoom(16, 500, 350); var (zx0, zy0, zx1, zy1) = model.VisibleTiles();
        check(zx1 - zx0 < 100 && zy1 - zy0 < 80 && (vx1 - vx0) > (zx1 - zx0), "MapCanvas_VisibleRangeCullsToTheViewport");

        // Ownership on a shared 30×30 matrix
        int explicitCount = 0, otherCount = 0;
        for (int by = 0; by < 30; by++) for (int bx = 0; bx < 30; bx++)
            if (model.Ownership(bx, by) == NdsBlockOwnership.Explicit) explicitCount++; else if (model.Ownership(bx, by) == NdsBlockOwnership.Other) otherCount++;
        check(explicitCount == 1 && otherCount == 899 && model.Ownership(3, 27) == NdsBlockOwnership.Explicit && model.OwnedBlockCount == 1, "MapCanvas_OnlyBlock3_27BelongsToHeader411");
        var contextual = new NdsMapCanvasModel(Contextual(2, 3), []);
        check(Enumerable.Range(0, 3).SelectMany(by => Enumerable.Range(0, 2).Select(bx => contextual.Ownership(bx, by))).All(o => o == NdsBlockOwnership.Contextual), "MapCanvas_ContextualHeadersAreNotPresentedAsExplicit");
        check(contextual.Layout is { Width: 2, Height: 3, TileWidth: 64, TileHeight: 96 } && contextual.WorldMaxX == 64 && contextual.WorldMaxY == 96, "MapCanvas_NonSquareMatrixHasExactWorldRectangle");
        string description = model.Describe(119, 878);
        check(description.Contains("bloque (3,27)") && description.Contains("local (23,14)") && description.Contains("cabecera 411 (explícita)"), "MapCanvas_DescribesTheValidated411Position");
        check(model.Describe(5, 5).Contains("otra cabecera: 300") && model.Describe(-1, 5).Contains("fuera de la matriz") && contextual.Describe(1, 1).Contains("contextual"), "MapCanvas_DescribesOtherContextualAndOutsidePositions");

        // Events outside the header mask or the matrix are kept, never clipped
        var far = Ev(PlatinumEventKind.Npc, 3, 2000, -7); var all = new NdsMapCanvasModel(twin, [npc0, Ev(PlatinumEventKind.Npc, 1, 5, 5), far]);
        check(all.Find(PlatinumEventKind.Npc, 0)!.Placement == NdsMarkerPlacement.InsideOwnedBlock && all.Find(PlatinumEventKind.Npc, 1)!.Placement == NdsMarkerPlacement.OutsideOwnedBlocks
            && all.Find(PlatinumEventKind.Npc, 3)!.Placement == NdsMarkerPlacement.OutsideMatrix && all.Markers.Count == 3 && all.OutsideMaskCount == 2, "MapCanvas_OutOfMaskEventsAreKeptAndClassified");
        check(all.WorldMaxX >= 2001 && all.WorldMinY <= -7, "MapCanvas_WorldRectangleGrowsToIncludeOutOfMatrixEvents");
        all.SetViewport(800, 600); all.FitEvents(); var (fx, fy) = all.TileToPixel(far.X + 0.5, far.Y + 0.5);
        check(all.HitTest(fx, fy)?.Index == 3, "MapCanvas_OutOfMatrixEventIsSelectableAtItsOwnPosition");
        check(new NdsMapCanvasModel(Contextual(1, 1), [Ev(PlatinumEventKind.Npc, 0, 3, 3)]).Markers[0].Placement == NdsMarkerPlacement.InsideContextualBlock, "MapCanvas_ContextualPlacement");

        // Marker kinds, shapes and layers
        var kinds = model.Markers.ToDictionary(m => (m.Kind, m.Index), m => m.Shape);
        check(kinds[(PlatinumEventKind.Npc, 0)] == NdsMarkerShape.Npc && kinds[(PlatinumEventKind.Npc, 2)] == NdsMarkerShape.GroundObject && kinds[(PlatinumEventKind.Object, 0)] == NdsMarkerShape.BackgroundObject
            && kinds[(PlatinumEventKind.Warp, 0)] == NdsMarkerShape.Warp && kinds[(PlatinumEventKind.Trigger, 0)] == NdsMarkerShape.Trigger, "MapCanvas_EveryKindHasItsOwnShape");
        check(model.Markers.Single(m => m.Shape == NdsMarkerShape.Trigger) is { Width: 4, Height: 2 }, "MapCanvas_TriggerUsesItsDeclaredRange");
        check(new NdsMapCanvasModel(twin, [Ev(PlatinumEventKind.Trigger, 0, 1, 1)]).Markers[0] is { Width: 1, Height: 1 }, "MapCanvas_ZeroRangeTriggerStaysSelectable");
        check(Enum.GetValues<NdsMarkerShape>().Select(s => NdsMapCanvasControl.ShapeGeometry(s, new Point(10, 10), 6).Bounds.ToString()).Distinct().Count() == 5, "MapCanvas_ShapesAreDistinguishableWithoutColour");

        // Hit testing and selection
        model.FitEvents(); model.SetZoom(16, 500, 350); var npcMarker = model.Find(PlatinumEventKind.Npc, 0)!; var (hx, hy) = model.TileToPixel(npcMarker.CenterX, npcMarker.CenterY);
        model.PanBy(hx - 500, hy - 350); (hx, hy) = model.TileToPixel(npcMarker.CenterX, npcMarker.CenterY);
        check(model.HitTest(hx, hy)?.Index == 0 && model.HitTest(hx, hy)?.Kind == PlatinumEventKind.Npc && model.HitTest(hx + 400, hy + 300) is null, "MapCanvas_HitTestFindsTheMarkerUnderTheCursor");
        var warpMarker = model.Find(PlatinumEventKind.Warp, 0)!; var (wx, wy) = model.TileToPixel(warpMarker.CenterX, warpMarker.CenterY);
        check(model.HitTest(wx, wy)?.Kind == PlatinumEventKind.Warp, "MapCanvas_HitTestFindsWarps");
        var trig = model.Find(PlatinumEventKind.Trigger, 0)!; var (trx, try_) = model.TileToPixel(trig.X + 2.5, trig.Y + 1.2);
        check(model.HitTest(trx, try_)?.Kind == PlatinumEventKind.Trigger, "MapCanvas_HitTestFindsInsideTheTriggerRectangle");
        model.Layers &= ~NdsMapLayers.Npc; check(model.HitTest(hx, hy) is null, "MapCanvas_HiddenLayerIsNotHit");
        model.Layers = NdsMapLayers.All;
        model.FitMatrix(); var small = model.Find(PlatinumEventKind.Npc, 0)!; var (sx, sy) = model.TileToPixel(small.CenterX, small.CenterY);
        check(model.Zoom < 2 && model.HitTest(sx + 3, sy + 3)?.Index == 0, "MapCanvas_MarkersKeepAMinimumHitSizeAtLowZoom");
        check(model.Select(PlatinumEventKind.Warp, 0) && model.Selected == (PlatinumEventKind.Warp, 0) && !model.Select(PlatinumEventKind.Warp, 9) && model.Selected == (PlatinumEventKind.Warp, 0), "MapCanvas_SelectionOnlyAcceptsExistingEvents");
        model.Select(PlatinumEventKind.Npc, 0); var east = model.Neighbor(1, 0); var north = model.Neighbor(0, -1); var west = model.Neighbor(-1, 0);
        check(east is not null && north is not null && west is not null && east.CenterX > 119.5 && north!.CenterY < 878.5 && west!.CenterX < 119.5, "MapCanvas_KeyboardNeighboursFollowTheDirection");
        model.Select(PlatinumEventKind.Npc, 0); var seen = new List<(PlatinumEventKind, int)>(); for (int i = 0; i < model.Markers.Count; i++) { var next = model.Cycle(1)!; model.Select(next.Kind, next.Index); seen.Add((next.Kind, next.Index)); }
        check(seen.Distinct().Count() == model.Markers.Count && model.Selected == (PlatinumEventKind.Npc, 0), "MapCanvas_CycleVisitsEveryMarkerAndWraps");
        model.ClearSelection(); check(model.Selected is null && model.SelectedMarker is null, "MapCanvas_ClearSelection");

        // Performance: the biggest matrix, many events
        var many = Enumerable.Range(0, 4000).Select(i => Ev(PlatinumEventKind.Npc, i, i % 960, i / 5 % 960)).ToList();
        var stress = new NdsMapCanvasModel(twin, many); stress.SetViewport(1100, 700); var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) { stress.PanBy(13, 7); stress.ZoomBy(i % 2 == 0 ? 1.1 : 1 / 1.1, 500, 300); _ = stress.HitTest(500, 300); }
        check(clock.ElapsedMilliseconds < 2000, "MapCanvas_PanZoomAndHitTestStayFastWith4000Events");
    }

    // ------------------------------------------------------------------ WPF integration (off-screen)
    internal sealed record Fixture(byte[] Headers, byte[] Names, byte[] Events, byte[] Matrices, byte[] Scripts)
    {
        internal PlatinumEvents Read() => PlatinumEvents.Parse(Headers, Names, Events, Matrices, Scripts, ["Zona A"]);
        internal byte[] Rom() => NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]>
        { [PlatinumEvents.EventPath] = Events, [PlatinumEvents.NamesPath] = Names, [PlatinumEvents.MatrixPath] = Matrices, [PlatinumEvents.ScriptPath] = Scripts, ["untouched.bin"] = [7, 1, 99, 18] }, "CPUE", 1);
    }
    private static void Put(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), checked((ushort)value));
    internal static Fixture Make()
    {
        byte[] Npc(int id, int x, int y, int type = 0, int script = 0) { var b = new byte[32]; Put(b, 0, id); Put(b, 2, 33); Put(b, 6, type); Put(b, 10, script); Put(b, 24, x); Put(b, 26, y); return b; }
        byte[] Warp(int x, int y, int dest) { var b = new byte[12]; Put(b, 0, x); Put(b, 2, y); Put(b, 4, dest); return b; }
        byte[] Trigger(int x, int y, int rx, int ry) { var b = new byte[16]; Put(b, 0, 5); Put(b, 2, x); Put(b, 4, y); Put(b, 6, rx); Put(b, 8, ry); return b; }
        byte[] Background(int x, int y) { var b = new byte[20]; Put(b, 0, 1); Put(b, 4, x); Put(b, 8, y); return b; }
        byte[] File(byte[][] bg, byte[][] npcs, byte[][] warps, byte[][] triggers)
        {
            using var output = new MemoryStream(); using var w = new BinaryWriter(output);
            foreach (var block in new[] { bg, npcs, warps, triggers }) { w.Write(block.Length); foreach (var r in block) w.Write(r); }
            return output.ToArray();
        }
        byte[] headers = new byte[72], names = new byte[48];
        int[] matrixOf = [2, 0, 1];
        for (int i = 0; i < 3; i++) { Encoding.ASCII.GetBytes("MAP_" + (char)('A' + i)).CopyTo(names, i * 16); Put(headers, i * 24 + 2, matrixOf[i]); Put(headers, i * 24 + 16, i); }
        var events = NdsGen5RandomizerChecks.BuildNarc([
            File([], [Npc(10, 2, 3)], [Warp(1, 1, 1)], []),
            File([Background(100, 860)], [Npc(20, 119, 878, 0, 1), Npc(21, 5, 5), Npc(22, 130, 885, 3, 7000), Npc(23, 2000, 10)], [Warp(130, 880, 0)], [Trigger(110, 870, 4, 2)]),
            File([], [Npc(30, 3, 4)], [Warp(5, 5, 1)], [])]);
        var matrices = NdsGen5RandomizerChecks.BuildNarc([Matrix(30, 30, i => i == 3 + 27 * 30 ? 1 : 2), Matrix(2, 3, null), Matrix(1, 1, null)]);
        return new(headers, names, events, matrices, NdsGen5RandomizerChecks.BuildNarc([[2, 0, 0, 0, 0x13, 0xFD, 2, 0, 0xAB, 0xCD]]));
    }
    internal static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (object child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject item) foreach (var descendant in Children<T>(item)) yield return descendant;
    }
    internal static void Pump(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted) { if (timer.Elapsed.TotalSeconds > 90) throw new TimeoutException(); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); }
        task.GetAwaiter().GetResult();
    }
    internal static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    internal static void Arrange(FrameworkElement view, int width, int height)
    {
        view.Margin = new Thickness(0); view.Width = width; view.Height = height; view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
    }
    internal static void Flush() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
    internal static void Capture(FrameworkElement view, int width, int height, string name)
    {
        Flush();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(view);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        string captures = Path.Combine(AppContext.BaseDirectory, "nds-map-canvas"); Directory.CreateDirectory(captures);
        using var stream = File.Create(Path.Combine(captures, name + ".png")); png.Save(stream);
    }
    internal static void CenterOn(NdsMapCanvasControl canvas, PlatinumEventKind kind, int index, double zoom)
    {
        var m = canvas.Model!; var marker = m.Find(kind, index)!; m.SetZoom(zoom, m.ViewportWidth / 2, m.ViewportHeight / 2);
        var (px, py) = m.TileToPixel(marker.CenterX, marker.CenterY); m.PanBy(px - m.ViewportWidth / 2, py - m.ViewportHeight / 2); canvas.Invalidate();
    }

    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-map-canvas-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = Make(); string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = Sha(source);
            string outputDir = Path.Combine(temp, "out"); Directory.CreateDirectory(outputDir);
            using var rom = NdsRom.Open(source); var data = f.Read(); var session = new NdsEventSession(rom, data); var view = new NdsEventEditorView(session);
            byte[] eventsBefore = session.Data.Bytes();
            var canvas = Children<NdsMapCanvasControl>(view).Single(); var maps = Children<ListBox>(view).Single(); var tabs = Children<TabControl>(view).Single();
            Arrange(view, 1100, 760);
            TextBox Box(string name) => Children<TextBox>(view).Single(e => AutomationProperties.GetName(e) == "Eventos DS · " + name);
            Button Action(string label) => Children<Button>(view).Single(b => AutomationProperties.GetName(b) == "Eventos DS · " + label);
            maps.SelectedItem = maps.Items[1]; Arrange(view, 1100, 760);
            var model = canvas.Model!;
            check(model.Layout is { MapId: 1, Width: 30, Height: 30 } && model.Markers.Count == 7 && model.Layout.OwnedCells.Count == 1, "MapCanvasUi_ShowsTheSelectedMapMatrixAndEvents");
            check(System.Windows.Media.VisualTreeHelper.GetChildrenCount(canvas) == 0 && canvas.IsFocusable(), "MapCanvasUi_DrawsWithoutOneElementPerTile");
            check(Children<TextBlock>(view).Any(t => t.Text == NdsMapCanvasControl.SchematicNotice) && Children<TextBlock>(view).Any(t => t.Text.Contains("matriz 30×30 bloques (960×960 tiles)") && t.Text.Contains(": 1 ·")), "MapCanvasUi_ShowsSchematicNoticeDimensionsAndOwnedCellCount");
            check(Children<TextBlock>(view).Any(t => t.Text.Contains("fuera de la máscara de la cabecera")) && Children<TextBlock>(view).Any(t => t.Text == "warp") && Children<TextBlock>(view).Any(t => t.Text == "activador (rango declarado)"), "MapCanvasUi_LegendExplainsMarkersAndMask");

            // Table -> canvas
            tabs.SelectedIndex = 0; var npcTable = Children<DataGrid>(view).First(); npcTable.SelectedItem = npcTable.Items.Cast<PlatinumEvent>().First(e => e.Index == 1);
            check(canvas.Model!.Selected == (PlatinumEventKind.Npc, 1), "MapCanvasUi_TableSelectionHighlightsTheMarker");
            tabs.SelectedIndex = 2; var warpTable = Children<DataGrid>(view).ElementAt(2); warpTable.SelectedItem = warpTable.Items.Cast<PlatinumEvent>().First();
            check(canvas.Model.Selected == (PlatinumEventKind.Warp, 0), "MapCanvasUi_WarpTableSelectionHighlightsTheMarker");

            // Canvas -> table and inspector, via the same code path as a click
            tabs.SelectedIndex = 0; CenterOn(canvas, PlatinumEventKind.Npc, 0, 16); var npc = canvas.Model.Find(PlatinumEventKind.Npc, 0)!; var (cx, cy) = canvas.Model.TileToPixel(npc.CenterX, npc.CenterY);
            canvas.ClickAt(new Point(cx, cy));
            check(canvas.Model.Selected == (PlatinumEventKind.Npc, 0) && (npcTable.SelectedItem as PlatinumEvent)?.Index == 0 && Box("Coordenada X").Text == "119" && Box("Coordenada Y").Text == "878", "MapCanvasUi_ClickSelectsInTableAndInspector");
            var details = Children<TextBox>(view).Single(t => AutomationProperties.GetName(t).Contains("propiedades"));
            check(details.Text.Contains("bloque (3,27)") && details.Text.Contains("local (23,14)") && details.Text.Contains("cabecera 1 (explícita)") && details.Text.Contains("asociada explícitamente"), "MapCanvasUi_InspectorExplainsTheValidatedPosition");
            CenterOn(canvas, PlatinumEventKind.Warp, 0, 16); var wm = canvas.Model.Find(PlatinumEventKind.Warp, 0)!; (cx, cy) = canvas.Model.TileToPixel(wm.CenterX, wm.CenterY); canvas.ClickAt(new Point(cx, cy));
            check(tabs.SelectedIndex == 2 && (warpTable.SelectedItem as PlatinumEvent)?.Kind == PlatinumEventKind.Warp && details.Text.Contains("Destino 0") && Action("Ir al destino existente").IsEnabled, "MapCanvasUi_WarpClickSwitchesTableAndKeepsNavigation");
            CenterOn(canvas, PlatinumEventKind.Npc, 1, 16); var off = canvas.Model.Find(PlatinumEventKind.Npc, 1)!; (cx, cy) = canvas.Model.TileToPixel(off.CenterX, off.CenterY); canvas.ClickAt(new Point(cx, cy));
            check(details.Text.Contains("fuera de la máscara") && details.Text.Contains("otra cabecera: 2"), "MapCanvasUi_EventOutsideTheMaskIsReportedNotHidden");
            CenterOn(canvas, PlatinumEventKind.Npc, 3, 16); var outside = canvas.Model.Find(PlatinumEventKind.Npc, 3)!; (cx, cy) = canvas.Model.TileToPixel(outside.CenterX, outside.CenterY); canvas.ClickAt(new Point(cx, cy));
            check(canvas.Model.Selected == (PlatinumEventKind.Npc, 3) && details.Text.Contains("fuera de la matriz"), "MapCanvasUi_EventOutsideTheMatrixRemainsInspectable");
            canvas.Navigate(-1, 0, false); check(canvas.Model.Selected is { } && canvas.Model.Selected != (PlatinumEventKind.Npc, 3), "MapCanvasUi_KeyboardNavigationSelectsNeighbour");
            canvas.Navigate(1, 0, true); check(true, "MapCanvasUi_KeyboardPanDoesNotThrow");

            // Map change, mode and layers
            maps.SelectedItem = maps.Items[2]; Arrange(view, 1100, 760);
            check(canvas.Model!.Layout is { MapId: 2, Width: 2, Height: 3, TileWidth: 64, TileHeight: 96 } && canvas.Model.Markers.Count == 2, "MapCanvasUi_MapChangeRebuildsTheCanvasForNonSquareMatrix");
            maps.SelectedItem = maps.Items[0]; check(canvas.Model!.Layout is { MapId: 0, Width: 1 } && canvas.Model.Ownership(0, 0) == NdsBlockOwnership.Contextual, "MapCanvasUi_ContextualMapIsReported");
            maps.SelectedItem = maps.Items[1]; Arrange(view, 1100, 760);
            var modeTables = Children<RadioButton>(view).Single(r => AutomationProperties.GetName(r) == "Eventos DS · modo tablas"); var modeMap = Children<RadioButton>(view).Single(r => AutomationProperties.GetName(r) == "Eventos DS · modo mapa esquemático");
            modeTables.IsChecked = true;
            check(tabs.Visibility == Visibility.Visible && ((FrameworkElement)canvas.Parent).Visibility == Visibility.Collapsed, "MapCanvasUi_TablesModeShowsTheExistingTables");
            modeMap.IsChecked = true; check(tabs.Visibility == Visibility.Collapsed && ((FrameworkElement)canvas.Parent).Visibility == Visibility.Visible, "MapCanvasUi_MapModeShowsTheCanvas");
            var layerNpc = Children<CheckBox>(view).Single(c => AutomationProperties.GetName(c) == "Eventos DS · capa NPC"); layerNpc.IsChecked = false;
            check((canvas.Model.Layers & NdsMapLayers.Npc) == 0 && (canvas.Model.Layers & NdsMapLayers.Warps) != 0, "MapCanvasUi_LayerCheckboxesHideKinds"); layerNpc.IsChecked = true;
            maps.SelectedItem = maps.Items[0]; maps.SelectedItem = maps.Items[1]; Arrange(view, 1100, 760);
            check((canvas.Model!.Layers & NdsMapLayers.Npc) != 0, "MapCanvasUi_LayersSurviveMapChanges");

            // Accessibility
            check(Children<CheckBox>(view).Concat<Control>(Children<RadioButton>(view)).Concat(Children<Button>(view)).All(c => AutomationProperties.GetName(c).Length > 0) && AutomationProperties.GetName(canvas).Length > 0 && AutomationProperties.GetHelpText(canvas).Contains("Flechas"), "MapCanvasUi_ControlsHaveAccessibleNamesAndKeyboardHelp");
            check(Children<Control>(view).Where(c => c is TextBox or ListBox or TabControl).All(c => AutomationProperties.GetName(c).Length > 0), "MapCanvasUi_ExistingInputsKeepTheirNames");

            // Rendering time and captures at both layouts
            foreach (int width in new[] { 1100, 760 })
            {
                Arrange(view, width, 760); canvas.FitEvents(); Flush();
                check(view.DesiredSize.Width <= width && canvas.ActualWidth > 200 && canvas.ActualHeight >= 150 && Action("Guardar cambios…").ActualWidth > 0, "MapCanvasUi_AdaptiveLayout" + width);
                check(Children<TextBlock>(view).Single(t => AutomationProperties.GetName(t) == "Eventos DS · aviso de vista esquemática").ActualHeight > 0, "MapCanvasUi_NoticeVisibleAt" + width);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 6; i++) { canvas.Model!.PanBy(25, 10); canvas.Invalidate(); Flush(); var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 760, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bmp.Render(canvas); }
                check(clock.ElapsedMilliseconds / 6 < 750, "MapCanvasUi_RenderIsFastOn960x960At" + width);
                Capture(view, width, 760, "canvas-411like-" + width);
            }
            Arrange(view, 1100, 760); CenterOn(canvas, PlatinumEventKind.Npc, 0, 16); canvas.ClickAt(new Point(canvas.Model!.ViewportWidth / 2, canvas.Model.ViewportHeight / 2)); Capture(view, 1100, 760, "canvas-411like-zoom-selected");
            check(canvas.RenderedMarkers > 0 && canvas.RenderedMarkers < canvas.Model.Markers.Count + 1, "MapCanvasUi_OnlyVisibleMarkersAreDrawn");

            // Read-only: nothing changed on the session, the source or disk
            check(session.Revision == 0 && session.Data.Bytes().SequenceEqual(eventsBefore) && !session.CanUndo && session.LastCheckpoint is null, "MapCanvasUi_BrowsingDoesNotChangeTheEventSession");
            check(Sha(source) == sourceHash && Directory.GetFiles(temp).Length == 1 && Directory.GetFiles(outputDir).Length == 0, "MapCanvasUi_BrowsingDoesNotTouchTheSourceOrCreateFiles");

            // The existing preview/save flow still works from a canvas selection and the canvas shows the saved value
            tabs.SelectedIndex = 0; CenterOn(canvas, PlatinumEventKind.Npc, 0, 16); var target = canvas.Model.Find(PlatinumEventKind.Npc, 0)!; (cx, cy) = canvas.Model.TileToPixel(target.CenterX, target.CenterY); canvas.ClickAt(new Point(cx, cy));
            Box("Coordenada X").Text = "120"; view.PreviewSelection();
            check(Box("diferencias previstas").Text.Contains("Coordenada X: 119 → 120") && Action("Guardar cambios…").IsEnabled, "MapCanvasUi_PreviewFromCanvasSelectionStillShowsExactDifferences");
            Pump(view.SaveToAsync(Path.Combine(outputDir, "saved.ndsevents.json")));
            check(session.Revision == 1 && session.Data.Npc(1, 0).X == 120 && canvas.Model!.Find(PlatinumEventKind.Npc, 0)!.X == 120 && File.Exists(Path.Combine(outputDir, "saved.ndsevents.json")) && Sha(source) == sourceHash, "MapCanvasUi_SaveStillCreatesCheckpointAndCanvasRefreshes");
            check(session.CanUndo && Action("Restaurar último cambio…").IsEnabled, "MapCanvasUi_RestoreStillAvailable");
        }
        finally { Directory.Delete(temp, true); }
    }
    internal static byte[] Pixels(FrameworkElement element, int width, int height)
    {
        Flush(); var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(element);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0); return pixels;
    }

    internal static void RunTerrainUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-terrain-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = Make(); string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = Sha(source);
            string outputDir = Path.Combine(temp, "out"); Directory.CreateDirectory(outputDir);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read()); byte[] eventsBefore = session.Data.Bytes();
            var terrain = PlatinumTerrain.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([PlatinumTerrainChecks.Land((x, z) => x == 2 && z == 21 || x >= 28 ? (ushort)0x8000 : x == 23 && z == 14 ? (ushort)0x0002 : (ushort)0)]));
            var view = new NdsEventEditorView(session, terrain, null); var canvas = Children<NdsMapCanvasControl>(view).Single(); var maps = Children<ListBox>(view).Single(); var tabs = Children<TabControl>(view).Single();
            TextBox Box(string name) => Children<TextBox>(view).Single(e => AutomationProperties.GetName(e) == "Eventos DS · " + name);
            Button Action(string label) => Children<Button>(view).Single(b => AutomationProperties.GetName(b) == "Eventos DS · " + label);
            Arrange(view, 1100, 760); maps.SelectedItem = maps.Items[1]; Arrange(view, 1100, 760);
            var details = Children<TextBox>(view).Single(t => AutomationProperties.GetName(t).Contains("propiedades"));
            var layer = Children<CheckBox>(view).Single(c => AutomationProperties.GetName(c).StartsWith("Eventos DS · capa Terreno"));
            check(layer.IsEnabled && layer.IsChecked == true && canvas.Model!.HasTerrain && (canvas.Model.Layers & NdsMapLayers.Terrain) != 0, "TerrainUi_LayerCheckboxIsEnabledAndOnWithData");
            var texts = Children<TextBlock>(view).Select(t => t.Text).ToList();
            check(texts.Any(t => t.Contains("terreno bloqueado")) && texts.Any(t => t.Contains("sin colisión (no confirma paso)")) && texts.Any(t => t.Contains("sin datos (damero)")), "TerrainUi_LegendShowsTheThreeStates");
            check(texts.Any(t => t.Contains("bit 15") && t.Contains("no confirma que se pueda pasar") && t.Contains("no los gráficos")) && texts.Contains(NdsMapCanvasControl.SchematicNotice), "TerrainUi_StatesTheLimitsOfTheTerrainLayer");
            check(NdsMapCanvasControl.SchematicNotice.Contains("colisiones estáticas") && NdsMapCanvasControl.SchematicNotice.Contains("gráficos") && NdsMapCanvasControl.SchematicNotice.Contains("alturas") && NdsMapCanvasControl.SchematicNotice.Contains("reglas de movimiento") && !NdsMapCanvasControl.SchematicNotice.Contains("No representa los gráficos ni las colisiones"), "TerrainUi_NoticeAcknowledgesStaticCollisionsAndKeepsTheLimits");

            void Click(PlatinumEventKind kind, int index) { CenterOn(canvas, kind, index, 16); var mk = canvas.Model!.Find(kind, index)!; var (cx, cy) = canvas.Model.TileToPixel(mk.CenterX, mk.CenterY); canvas.ClickAt(new Point(cx, cy)); }
            tabs.SelectedIndex = 0; Click(PlatinumEventKind.Npc, 0);
            check(details.Text.Contains("Consulta espacial") && details.Text.Contains("Permitido según datos conocidos") && details.Text.Contains("comportamiento de tile 0x02") && details.Text.Contains("No evaluado:") && details.Text.Contains("Procedencia:"), "TerrainUi_Npc0Case411ShowsAssessmentProvenanceAndLimits");
            Click(PlatinumEventKind.Npc, 2);
            check(details.Text.Contains("Bloqueado según datos conocidos") && details.Text.Contains("colisión de terreno"), "TerrainUi_GroundObjectOnACollisionTileIsReportedBlocked");
            Click(PlatinumEventKind.Npc, 1);
            check(details.Text.Contains("Indeterminado") && details.Text.Contains("otra cabecera"), "TerrainUi_EventOutsideTheMaskIsIndeterminate");
            check(canvas.Model!.Describe(130, 885).Contains("terreno: bloqueado") && canvas.Model.Describe(119, 878).Contains("no confirma paso"), "TerrainUi_HoverTextDistinguishesBlockedAndClear");

            // Layer on/off really changes what is drawn; both states keep every other layer intact
            CenterOn(canvas, PlatinumEventKind.Npc, 0, 16); canvas.Invalidate(); var withTerrain = Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            layer.IsChecked = false; var without = Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check((canvas.Model.Layers & NdsMapLayers.Terrain) == 0 && (canvas.Model.Layers & NdsMapLayers.Npc) != 0 && !withTerrain.SequenceEqual(without), "TerrainUi_LayerToggleChangesTheDrawing");
            layer.IsChecked = true; check(Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight).SequenceEqual(withTerrain), "TerrainUi_LayerToggleIsReversibleAndDeterministic");
            Capture(view, 1100, 760, "terrain-411like-zoom-1100");
            canvas.FitEvents(); Flush(); Capture(view, 1100, 760, "terrain-411like-fit-1100");
            Arrange(view, 760, 760); canvas.FitEvents(); Flush(); Capture(view, 760, 760, "terrain-411like-760");
            check(canvas.ActualHeight >= 150 && view.DesiredSize.Width <= 760, "TerrainUi_CompactLayoutKeepsTheCanvas");
            Arrange(view, 1100, 760);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 6; i++) { canvas.FitMatrix(); canvas.Model!.PanBy(7 * i, 3 * i); canvas.Invalidate(); Pixels(canvas, 1100, 700); }
            for (int i = 0; i < 6; i++) { CenterOn(canvas, PlatinumEventKind.Npc, 0, 12 + i); Pixels(canvas, 1100, 700); }
            check(clock.ElapsedMilliseconds / 12 < 900, "TerrainUi_960x960OverlayRendersFastAtFitAndZoom");

            // A map or project without terrain data keeps working and never shows free tiles
            var bare = new NdsEventEditorView(session, null, "land_data no acreditado"); var bareCanvas = Children<NdsMapCanvasControl>(bare).Single(); var bareMaps = Children<ListBox>(bare).Single();
            Arrange(bare, 1100, 760); bareMaps.SelectedItem = bareMaps.Items[1]; Arrange(bare, 1100, 760);
            var bareLayer = Children<CheckBox>(bare).Single(c => AutomationProperties.GetName(c).Contains("capa Terreno"));
            var bareDetails = Children<TextBox>(bare).Single(t => AutomationProperties.GetName(t).Contains("propiedades"));
            check(!bareLayer.IsEnabled && bareLayer.IsChecked == false && !bareCanvas.Model!.HasTerrain && Children<TextBlock>(bare).Any(t => t.Text.Contains("Terreno: no disponible") && t.Text.Contains("land_data no acreditado") && t.Text.Contains("libres ni bloqueadas")), "TerrainUi_WithoutDataTheLayerIsUnavailableAndSaysWhy");
            check(bareCanvas.Model!.Markers.Count == 7 && bareCanvas.Model.Layout.Width == 30 && bareDetails.Text.Contains("Indeterminado") && bareDetails.Text.Contains("sin datos de terreno") && !Children<TextBlock>(bare).Any(t => t.Text.Contains("terreno bloqueado")), "TerrainUi_WithoutDataTheMapStillOpensWithEvents");
            Capture(bare, 1100, 760, "terrain-unavailable-1100");

            // Read-only and integrity
            check(session.Revision == 0 && session.Data.Bytes().SequenceEqual(eventsBefore) && Sha(source) == sourceHash && Directory.GetFiles(temp).Length == 1 && Directory.GetFiles(outputDir).Length == 0, "TerrainUi_BrowsingTerrainDoesNotTouchSessionSourceOrDisk");
            // Existing preview/save flow still works from a canvas selection, with terrain enabled
            tabs.SelectedIndex = 0; Click(PlatinumEventKind.Npc, 0); Box("Coordenada X").Text = "120"; view.PreviewSelection();
            check(Box("diferencias previstas").Text.Contains("Coordenada X: 119 → 120") && Action("Guardar cambios…").IsEnabled, "TerrainUi_PreviewStillShowsExactDifferences");
            Pump(view.SaveToAsync(Path.Combine(outputDir, "saved.ndsevents.json")));
            check(session.Revision == 1 && session.Data.Npc(1, 0).X == 120 && canvas.Model!.Find(PlatinumEventKind.Npc, 0)!.X == 120 && Sha(source) == sourceHash && canvas.Model.HasTerrain, "TerrainUi_SaveStillCreatesCheckpointAndKeepsTheTerrainLayer");
        }
        finally { Directory.Delete(temp, true); }
    }
    private static bool IsFocusable(this UIElement element) => element.Focusable;
}
