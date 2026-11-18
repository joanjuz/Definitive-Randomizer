using System.Buffers.Binary;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Ctr;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive;

/// <summary>H3D reader pieces, PICA texture decoding and the Gen6 top-down rasteriser, with synthetic data.</summary>
internal static class Map3dsRenderChecks
{
    private static H3dMesh Quad(float y, float x0, float z0, float x1, float z1, int material = 0)
    {
        var attrs = new List<PicaAttribute> { new(PicaAttribute.Position, 3, 3, 1), new(PicaAttribute.TexCoord0, 3, 2, 1) };
        var v = new byte[4 * 20];
        void Put(int i, float x, float z, float u, float w)
        {
            var s = v.AsSpan(i * 20);
            BinaryPrimitives.WriteSingleLittleEndian(s, x); BinaryPrimitives.WriteSingleLittleEndian(s[4..], y); BinaryPrimitives.WriteSingleLittleEndian(s[8..], z);
            BinaryPrimitives.WriteSingleLittleEndian(s[12..], u); BinaryPrimitives.WriteSingleLittleEndian(s[16..], w);
        }
        Put(0, x0, z0, 0, 0); Put(1, x1, z0, 1, 0); Put(2, x1, z1, 1, 1); Put(3, x0, z1, 0, 1);
        return new H3dMesh(material, attrs, 20, Vector4.Zero, v, [new H3dSubMesh(0, [0, 1, 2, 0, 2, 3])]);
    }

    private static H3dTextureCoord Identity => new(0, 0, Vector2.One, 0, Vector2.Zero);

    private static DecodedTexture Solid(byte r, byte g, byte b, byte a = 255)
    {
        var px = new byte[8 * 8 * 4];
        for (int i = 0; i < 64; i++) { px[i * 4] = b; px[(i * 4) + 1] = g; px[(i * 4) + 2] = r; px[(i * 4) + 3] = a; }
        return new DecodedTexture(8, 8, px);
    }

    internal static void Run(Action<bool, string> check)
    {
        // RGBA8: the first texel of the first tile is the bottom-left pixel.
        var rgba = new byte[8 * 8 * 4];
        rgba[0] = 0xFF; rgba[1] = 0x30; rgba[2] = 0x20; rgba[3] = 0x10; // A B G R
        var decoded = PicaTexture.Decode(rgba, 8, 8, PicaTextureFormat.RGBA8);
        int bottomLeft = 7 * 8 * 4;
        check(decoded[bottomLeft] == 0x30 && decoded[bottomLeft + 1] == 0x20 && decoded[bottomLeft + 2] == 0x10 && decoded[bottomLeft + 3] == 0xFF && decoded[3] == 0,
            "Pica_Rgba8FirstTexelIsBottomLeftInBgra");
        check(PicaTexture.Length(16, 8, PicaTextureFormat.ETC1) == 64 && PicaTexture.Length(16, 8, PicaTextureFormat.ETC1A4) == 128 && PicaTexture.Length(12, 8, PicaTextureFormat.L8) == -1,
            "Pica_LengthsPerFormatAndTileAlignment");
        // ETC1 individual mode, colours 1 and 2 = (15,0,0) → 255,0,0; table 0, index 0 → +2.
        var etc = new byte[8 * 8 / 2];
        for (int b = 0; b < 4; b++) BinaryPrimitives.WriteUInt64LittleEndian(etc.AsSpan(b * 8), (ulong)0xFF000000u << 32);
        var e = PicaTexture.Decode(etc, 8, 8, PicaTextureFormat.ETC1);
        check(e[2] == 255 && e[1] == 2 && e[0] == 2 && e[3] == 255 && e.Where((_, i) => i % 4 == 2).All(x => x == 255), "Pica_Etc1IndividualModeBaseColourAndModifier");
        bool shortThrows = false;
        try { PicaTexture.Decode(new byte[10], 8, 8, PicaTextureFormat.RGBA8); } catch (MapFormatException) { shortThrows = true; }
        check(shortThrows, "Pica_IncompleteTextureIsRejected");

        check(Identity.Apply(new Vector2(0.25f, 0.75f)) is var uv && Math.Abs(uv.X - 0.25f) < 1e-5 && Math.Abs(uv.Y - 0.75f) < 1e-5, "H3d_IdentityMayaTransformKeepsUv");
        var scaled = new H3dTextureCoord(0, 0, new Vector2(2, 2), 0, Vector2.Zero).Apply(new Vector2(0.5f, 0.5f));
        check(Math.Abs(scaled.X - 1f) < 1e-5 && Math.Abs(scaled.Y - 1f) < 1e-5, "H3d_MayaScaleWithoutRotationScalesFromTheOrigin");

        bool notBch = false, version = false;
        try { _ = new H3dFile(new byte[64]); } catch (MapFormatException) { notBch = true; }
        var fake = new byte[0x80]; "BCH\0"u8.CopyTo(fake); fake[4] = 0x20;
        try { _ = new H3dFile(fake); } catch (MapFormatException ex) { version = ex.Message.Contains("0x20"); }
        check(notBch && version && H3dFile.SupportedVersions.SetEquals([7, 0x21]), "H3d_OnlyVerifiedVersionsAreAccepted");

        var quad = Quad(0, -360, -360, 360, 360);
        var corners = quad.Read(PicaAttribute.Position)!;
        check(corners.Length == 4 && corners[2] == new Vector4(360, 0, 360, 0) && quad.Triangles().Count() == 2, "H3d_MeshReadsFloatPositionsAndTriangles");
        var strip = new H3dMesh(0, quad.Attributes, 20, Vector4.Zero, quad.Vertices, [new H3dSubMesh(1, [0, 1, 3, 2])]);
        check(strip.Triangles().SequenceEqual([(0, 1, 3), (3, 1, 2)]), "H3d_StripsAlternateWinding");

        var mats = new List<H3dMaterial>
        {
            new("ground", ["projection_dummy", "ground", ""], [new H3dTextureCoord(0, 3, Vector2.One, 0, Vector2.Zero), Identity, Identity]),
            new("roof", ["roof", "", ""], [Identity, Identity, Identity]),
            new("glass", ["glass", "", ""], [Identity, Identity, Identity]),
            new("lost", ["lost", "", ""], [Identity, Identity, Identity]),
        };
        var model = new H3dModel("test", Matrix4x4.Identity, mats,
        [
            Quad(0, -360, -360, 360, 360, 0),
            Quad(50, -90, -90, 90, 90, 1),
            Quad(80, -360, -360, -270, -270, 2),
            Quad(10, 270, 270, 360, 360, 3),
        ]);
        var textures = new Dictionary<string, DecodedTexture> { ["ground"] = Solid(200, 0, 0), ["roof"] = Solid(0, 200, 0), ["glass"] = Solid(0, 0, 200, 0), ["projection_dummy"] = Solid(9, 9, 9) };
        var render = Gen6TopDownRenderer.Render(model, n => textures.GetValueOrDefault(n), 80);
        byte[] P(int x, int y) => render.Pixels.AsSpan(((y * 80) + x) * 4, 4).ToArray();
        check(P(40, 40)[1] == 200 && P(40, 40)[2] == 0, "Render_HighestSurfaceWins");
        check(P(5, 60)[2] == 200, "Render_UvMappedTextureMatchingTheMaterialIsUsed");
        check(P(2, 2)[2] == 200 && P(2, 2)[0] == 0, "Render_TransparentTexelsAreDiscarded");
        check(render.Triangles == 8 && render.Untextured == 2 && render.MissingTextures.SequenceEqual(["lost"]) && P(78, 78)[3] == 255,
            "Render_CountsTrianglesAndMissingTextures");
        check(P(40, 40)[3] == 255 && render.Size == 80, "Render_BlockSpansTheWholeImage");
    }

    /// <summary>Adapter that draws a solid image per block, to check the graphics layer of the view.</summary>
    private sealed class Painted(IMapEditorBackend inner) : IMapEditorBackend, IMapResourceProvider
    {
        public int Calls;
        public string Family => inner.Family;
        public string Title => inner.Title;
        public IReadOnlyList<MapZoneEntry> Zones => inner.Zones;
        public IMapResourceProvider Resources => this;
        public IMapEventProvider Events => inner.Events;
        public IMapMutationValidator? Mutations => null;
        public MapCapabilities Capabilities => inner.Capabilities with { Graphics = true, DiagnosticLayer = "Diagnóstico de prueba" };
        public MapBlockImage? Diagnostic(int zone, int block, int size)
        {
            var px = new byte[size * size * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = 0xC0; px[i + 3] = 0xFF; }
            return new MapBlockImage(size, px, 2, 2);
        }
        public MapLayout Layout(int zone) => inner.Resources.Layout(zone);
        public MapCellGrid? Cells(int block) => inner.Resources.Cells(block);
        public MapBlockImage? Graphic(int zone, int block, int size)
        {
            Interlocked.Increment(ref Calls);
            var px = new byte[size * size * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i + 1] = 0xC0; px[i + 3] = 0xFF; }
            return new MapBlockImage(size, px, 2, 0);
        }
    }

    private static void Layout(FrameworkElement content, double w, double h)
    {
        content.Measure(new System.Windows.Size(w, h)); content.Arrange(new Rect(0, 0, w, h)); content.UpdateLayout(); UiPump.Idle(); content.UpdateLayout(); UiPump.Idle();
    }

    internal static void RunUi(Action<bool, string> check)
    {
        var readOnly = new Map3dsEditorView(Map3dsGen6Checks.Synthetic(out _));
        check(readOnly.GraphicsToggle is null, "Map3dsGfxUi_NoGraphicsToggleWithoutGraphics");
        var painted = new Painted(Map3dsGen6Checks.Synthetic(out _));
        var view = new Map3dsEditorView(painted);
        Layout(view, 1400, 860);
        check(view.GraphicsToggle is { IsChecked: false } && painted.Calls == 0 && view.Canvas.GraphicsDrawn == 0, "Map3dsGfxUi_GraphicsAreOffByDefault");
        view.GraphicsToggle!.IsChecked = true;
        Layout(view, 1400, 860);
        var until = DateTime.UtcNow.AddSeconds(10);
        while ((view.Canvas.PendingGraphics > 0 || view.Canvas.GraphicsDrawn == 0) && DateTime.UtcNow < until) { UiPump.Idle(); Layout(view, 1400, 860); Thread.Sleep(20); }
        check(painted.Calls >= 2 && view.Canvas.GraphicsDrawn == 3 && view.Canvas.RenderedMarkers == 6, "Map3dsGfxUi_BlocksAreDrawnInTheBackgroundWithEventsOnTop");
        var texts = new List<string>();
        void Collect(DependencyObject d) { for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++) { var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i); if (c is TextBlock t) texts.Add(t.Text); Collect(c); } }
        Collect(view);
        check(texts.Contains(Map3dsCanvas.GraphicNotice), "Map3dsGfxUi_LegendExplainsTheApproximation");
        int calls = painted.Calls;
        Layout(view, 1400, 860);
        check(painted.Calls == calls, "Map3dsGfxUi_RenderedBlocksAreCached");
        view.GraphicsToggle.IsChecked = false; Layout(view, 1400, 860);
        check(view.Canvas.GraphicsDrawn == 0 && texts.Count > 0, "Map3dsGfxUi_TurningOffReturnsToTheSchematicView");
        check(view.DiagnosticToggle is { IsChecked: false } && view.Canvas.DiagnosticsDrawn == 0, "Map3dsGfxUi_DiagnosticLayerIsOffByDefault");
        view.DiagnosticToggle!.IsChecked = true;
        var untilDiag = DateTime.UtcNow.AddSeconds(10);
        do { Layout(view, 1400, 860); UiPump.Idle(); Thread.Sleep(20); } while ((view.Canvas.PendingGraphics > 0 || view.Canvas.DiagnosticsDrawn == 0) && DateTime.UtcNow < untilDiag);
        var legendTexts = new List<string>();
        void CollectLegend(DependencyObject d) { for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++) { var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i); if (c is TextBlock tb) legendTexts.Add(tb.Text); CollectLegend(c); } }
        CollectLegend(view);
        check(view.Canvas.DiagnosticsDrawn == 3 && legendTexts.Any(x => x.Contains(Map3dsCanvas.DiagnosticNotice)), "Map3dsGfxUi_DiagnosticLayerIsDrawnAndExplained");
        view.DiagnosticToggle.IsChecked = false; Layout(view, 1400, 860);
        check(view.Canvas.DiagnosticsDrawn == 0, "Map3dsGfxUi_DiagnosticLayerTurnsOff");
        check(Map3dsCanvas.DetailFor(10) == 64 && Map3dsCanvas.DetailFor(130) == 256 && Map3dsCanvas.DetailFor(5000) == 512, "Map3dsGfxUi_LevelOfDetail");
    }
}
