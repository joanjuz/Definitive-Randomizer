using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;

internal static partial class NdsMapRenderChecks
{
    /// <summary>Synthetic assets for the trainer fixture: one area with a green-textured ground covering a whole block (±256 units).</summary>
    internal static PlatinumMapAssets SyntheticAssets(byte[] headers)
    {
        ushort red = 0x001F, green = 0x03E0, blue = 0x7C00, white = 0x7FFF;
        var btx = Btx([new("t", pk3DS.Core.NDS.Nitro.NitroTextureFormat.Palette4, 8, 8, Enumerable.Repeat((byte)0x55, 16).ToArray(), Repeat: true)], [("p", [red, green, blue, white])]);
        var ground = DisplayList([(0x40, [1u]), .. Vtx16((-4, 0, -4), (4, 0, -4), (4, 0, 4), (-4, 0, 4)), (0x41, [])]);
        return new PlatinumMapAssets(headers, NdsGen5RandomizerChecks.BuildNarc([new byte[8]]), NdsGen5RandomizerChecks.BuildNarc([new byte[] { 0, 0 }]), NdsGen5RandomizerChecks.BuildNarc([btx]),
            NdsGen5RandomizerChecks.BuildNarc([btx]), NdsGen5RandomizerChecks.BuildNarc([Bmd(1, [ground])]), NdsGen5RandomizerChecks.BuildNarc([LandData(Bmd(64, [ground]))]));
    }

    /// <summary>Real CPUE/1 copy in the editor, off-screen: Twinleaf (header 411) with the graphic layer on, captured with its NPC on top. Read-only.</summary>
    internal static void ProbeUi(string output, string source)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "Platino-copia.nds"); File.Copy(source, copy); string hash = NdsMapCanvasChecks.Sha(source);
        using (var project = NdsProject.Open(copy, false))
        {
            var (assets, note) = project.LoadMapAssets(); if (assets is null) throw new Exception("Graphics not available: " + note);
            var view = new NdsEventEditorView(project.Events, project.LoadTerrain().Terrain, null, project.LoadProps().Props, null, assets, null);
            var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single(); var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single();
            NdsMapCanvasChecks.Arrange(view, 1280, 860); maps.SelectedItem = maps.Items.Cast<PlatinumMap>().Single(m => m.Id == 411); NdsMapCanvasChecks.Arrange(view, 1280, 860);
            foreach (var name in new[] { "Máscara", "Bloques", "Terreno" }) NdsMapCanvasChecks.Children<CheckBox>(view).Single(b => AutomationProperties.GetName(b)?.StartsWith("Eventos DS · capa " + name) == true).IsChecked = false;
            NdsMapCanvasChecks.Children<CheckBox>(view).Single(b => AutomationProperties.GetName(b) == "Eventos DS · capa Modelos/texturas").IsChecked = true;
            canvas.FitEvents(); canvas.Invalidate(); NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while ((canvas.Renders!.Busy || canvas.Renders.Cached == 0) && clock.Elapsed.TotalSeconds < 60) System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            canvas.Invalidate(); NdsMapCanvasChecks.Capture(view, 1280, 860, "twinleaf-editor-graphic");
            Console.WriteLine($"PASS MapRenderUiReal_TwinleafInTheEditor: {canvas.Renders.Cached} blocks in {clock.ElapsedMilliseconds} ms; problems: {canvas.Renders.Problem(0, 411) ?? "none"}");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "nds-map-canvas", "twinleaf-editor-graphic.png"), Path.Combine(output, "twinleaf-editor-graphic.png"));
            // Whole matrix (the outdoor world): redraw as the window would whenever blocks finish, and count renders. Every block should render once.
            canvas.FitMatrix(); var model = canvas.Model!; int cellsWithLand = model.Layout.Cells.Select(c => (c.LandDataId, canvas.Renders.ResolveHeader(c.LandDataId, model.GraphicsCandidates(c)))).Where(k => k.LandDataId != 0xFFFF).Distinct().Count(); var whole = System.Diagnostics.Stopwatch.StartNew(); int before = canvas.Renders.RenderCount;
            while (whole.Elapsed.TotalSeconds < 40) { canvas.Invalidate(); NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); if (!canvas.Renders.Busy && canvas.Renders.Cached >= cellsWithLand) break; }
            int renders = canvas.Renders.RenderCount - before; NdsMapCanvasChecks.Capture(view, 1280, 860, "overworld-editor-graphic");
            Console.WriteLine($"{(!canvas.Renders.Busy && canvas.Renders.Cached >= cellsWithLand && renders <= 3 * cellsWithLand ? "PASS" : "FAIL")} MapRenderUiReal_WholeMatrixRendersEveryBlockWithoutLooping: {cellsWithLand} distinct blocks, {renders} renders in {whole.ElapsedMilliseconds} ms, busy {canvas.Renders.Busy}, cached {canvas.Renders.Cached} ({canvas.Renders.CachedBytes / 1048576} MB), working set {Environment.WorkingSet / 1048576} MB");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "nds-map-canvas", "overworld-editor-graphic.png"), Path.Combine(output, "overworld-editor-graphic.png"));
            int unowned = 0, untextured = 0;
            foreach (var cell in model.Layout.Cells.Where(c => c.HeaderId == 0 && c.LandDataId != 0xFFFF))
            {
                unowned++; var r = new pk3DS.Core.NDS.Nitro.NitroTopDownRenderer(32, 32, -256, -256, 32 / 512f);
                foreach (var p in assets.BlockScene(cell.LandDataId, canvas.Renders.ResolveHeader(cell.LandDataId, model.GraphicsCandidates(cell)), project.LoadProps().Props!.Props(cell.LandDataId), out _).ToList()) r.Draw(p);
                untextured += r.Untextured;
            }
            Console.WriteLine($"{(untextured == 0 ? "PASS" : "FAIL")} MapRenderUiReal_UnownedCellsUseTheNearestOwnersTextures: {unowned} cells, {untextured} untextured triangles");
        }
        File.Delete(copy);
        if (NdsMapCanvasChecks.Sha(source) != hash) throw new Exception("Source changed");
        Console.WriteLine("PASS MapRenderUiReal_SourceUnchanged; output " + output);
    }

    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-map-render-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = NdsTrainerVisionChecks.Make(); string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); string hash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read());
            var assets = SyntheticAssets(f.Headers);
            var view = new NdsEventEditorView(session, NdsTrainerVisionChecks.Terrain(), null, null, null, assets, null);
            var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single(); var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single();
            CheckBox Layer(FrameworkElement v, string name) => NdsMapCanvasChecks.Children<CheckBox>(v).Single(b => AutomationProperties.GetName(b) == "Eventos DS · capa " + name);
            NdsMapCanvasChecks.Arrange(view, 1100, 760); maps.SelectedItem = maps.Items[NdsTrainerVisionChecks.Map]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            NdsMapCanvasChecks.CenterOn(canvas, pk3DS.Core.NDS.PlatinumEventKind.Npc, NdsTrainerVisionChecks.Trainer, 2);
            var models = Layer(view, "Modelos/texturas");
            check(models.IsEnabled && models.IsChecked == false && (canvas.Model!.Layers & NdsMapLayers.Models) == 0 && ((string)models.ToolTip).Contains("desde arriba"), "MapRenderUi_TheGraphicLayerIsAvailableButOffByDefault");
            byte[] schematic = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check(canvas.Renders!.Cached == 0, "MapRenderUi_NothingIsRenderedWhileTheLayerIsOff");

            models.IsChecked = true; canvas.Invalidate(); NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while ((canvas.Renders.Busy || canvas.Renders.Cached == 0) && clock.Elapsed.TotalSeconds < 30) System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            canvas.Invalidate(); byte[] graphic = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check((canvas.Model!.Layers & NdsMapLayers.Models) != 0 && canvas.Renders!.Cached > 0 && !graphic.SequenceEqual(schematic), "MapRenderUi_TurningTheLayerOnDrawsTheBlocksInTheBackground");
            var notice = NdsMapCanvasChecks.Children<TextBlock>(view).Single(t => AutomationProperties.GetName(t) == "Eventos DS · aviso de vista esquemática");
            check(notice.Text == NdsEventEditorView.GraphicNotice && notice.Text.Contains("sin la cámara inclinada"), "MapRenderUi_TheNoticeSaysWhatTheGraphicViewIsNot");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "map-render-on-1100");
            var image = canvas.Renders.Render(0, NdsTrainerVisionChecks.Map, out string? problem);
            var pixels = new uint[512 * 512]; image!.CopyPixels(pixels, 512 * 4, 0);
            check(image.PixelWidth == NdsMapRenderCache.BlockPixels && pixels[0] == 0xFF00FF00 && pixels[^1] == 0xFF00FF00 && problem is null, "MapRenderUi_ABlockImageIsTheTexturedGroundAtSixteenPixelsPerTile");
            check(canvas.Renders.Get(0xFFFF, 1) is null && canvas.Renders.Get(0, 99) is null, "MapRenderUi_EmptyCellsAndUnknownHeadersHaveNoImage");
            // Level of detail: a small request renders a small image; a larger one returns that image at once and the sharper one when ready.
            var lod = new NdsMapRenderCache(assets, null); void Settle() { var t = System.Diagnostics.Stopwatch.StartNew(); while (lod.Busy && t.Elapsed.TotalSeconds < 30) System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); }
            check(lod.Get(0, NdsTrainerVisionChecks.Map, 20) is null, "MapRenderUi_NothingIsShownBeforeTheFirstRender"); Settle();
            var small = lod.Get(0, NdsTrainerVisionChecks.Map, 20); var interim = lod.Get(0, NdsTrainerVisionChecks.Map, 400); Settle(); var sharp = lod.Get(0, NdsTrainerVisionChecks.Map, 400);
            check(small?.PixelWidth == 32 && ReferenceEquals(interim, small) && sharp?.PixelWidth == 512 && lod.CachedBytes == (32 * 32 + 512 * 512) * 4, "MapRenderUi_ASharperImageReplacesTheSmallOneWhenReady");
            var smallPixels = new uint[32 * 32]; small!.CopyPixels(smallPixels, 32 * 4, 0);
            check(smallPixels.All(p => p == 0xFF00FF00), "MapRenderUi_SmallImagesAreFilteredNotNoisy");

            // The notice changes with the layer, so the column is laid out again before comparing with the schematic capture.
            models.IsChecked = false; NdsMapCanvasChecks.Arrange(view, 1100, 760); canvas.Invalidate();
            check(NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight).SequenceEqual(schematic) && notice.Text == NdsMapCanvasControl.SchematicNotice, "MapRenderUi_TurningTheLayerOffRestoresTheSchematicView");

            // Editing keeps working with the graphic layer on.
            models.IsChecked = true; view.Rotate(3);
            check(canvas.Facing is { Direction: 3, Changed: true } && session.Revision == 0 && NdsMapCanvasChecks.Sha(source) == hash, "MapRenderUi_TrainersCanStillBeTurnedOverTheGraphicLayer");

            var bare = new NdsEventEditorView(session, null, null, null, null, null, "archivo no acreditado");
            var off = Layer(bare, "Modelos/texturas");
            check(!off.IsEnabled && off.IsChecked == false && ((string)off.ToolTip).StartsWith("No disponible") && ((string)off.ToolTip).Contains("archivo no acreditado"), "MapRenderUi_WithoutAccreditedArchivesTheLayerExplainsWhy");
        }
        finally { Directory.Delete(temp, true); }
    }
}
