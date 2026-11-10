using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using Point = System.Windows.Point;

/// <summary>Map props (structure positions and model ids) from land_data: reader, placement, canvas layer, and a read-only real-ROM probe.</summary>
internal static class PlatinumPropsChecks
{
    private static int Fx(double value) => (int)Math.Round(value * 4096);
    /// <summary>A land_data member with the given props section (48 bytes each) and extra trailing bytes inside that section.</summary>
    private static byte[] Land((int Model, double X, double Y, double Z)[] props, int extraBytes = 0)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(0x800u); w.Write((uint)(props.Length * 48 + extraBytes)); w.Write(20u); w.Write(8u);
        for (int i = 0; i < 1024; i++) w.Write((ushort)0);
        foreach (var p in props) { w.Write(p.Model); w.Write(Fx(p.X)); w.Write(Fx(p.Y)); w.Write(Fx(p.Z)); w.Write(0); w.Write(0); w.Write(0); w.Write(0x1000); w.Write(0x1000); w.Write(0x1000); w.Write(0L); }
        w.Write(new byte[extraBytes + 20 + 8]); return stream.ToArray();
    }
    private static byte[] Archive(params byte[][] files) => NdsGen5RandomizerChecks.BuildNarc(files);
    private static void Reject<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); } catch (T) { check(true, name); return; }
        throw new Exception("FAIL expected " + typeof(T).Name + ": " + name);
    }
    private static readonly (int, double, double, double)[] Twinleaf = [(23, -88, 16, -96), (67, -94.22, 16.62, -74.98), (236, 88, 16, 64), (22, 80, 16, -88)];

    internal static void Run(Action<bool, string> check)
    {
        // ---- reader and conversion
        var props = PlatinumLandProps.ParseNarc(Archive(Land(Twinleaf), Land([]), Land([(5, 0, 0, 0)])));
        var first = props.Props(0)[0];
        check(props.Count == 3 && props.Props(0).Count == 4 && props.Props(1).Count == 0 && props.Props(2).Count == 1 && props.TrailingBytes(0) == 0, "MapProps_ReadsEntriesPerMember");
        check(first is { ModelId: 23, Index: 0, RawX: -360448, RawY: 65536, RawZ: -393216, HasUnitScale: true, HasRotation: false, Unused: 0 } && first.X == -88 && first.Y == 16 && first.Z == -96, "MapProps_FixedPointValuesAreExact");
        check(first.TileOffsetX == -5.5 && first.TileOffsetZ == -6 && first.LocalTileX == 10.5 && first.LocalTileZ == 10, "MapProps_PositionIsRelativeToTheBlockCentre");
        check(props.Props(2)[0].LocalTileX == 16 && props.Props(2)[0].LocalTileZ == 16, "MapProps_OriginIsTheCentreOfTheBlock");
        var trailing = PlatinumLandProps.ParseNarc(Archive(Land(Twinleaf[..3], 2)));
        check(trailing.Props(0).Count == 3 && trailing.TrailingBytes(0) == 2, "MapProps_PartialEntryIsIgnoredLikeTheEngineDoes");
        Reject<ArgumentOutOfRangeException>(() => props.Props(9), check, "MapProps_UnknownLandIdIsRejected");
        byte[] good = Land(Twinleaf);
        Reject<InvalidDataException>(() => PlatinumLandProps.ParseNarc(Archive(good[..0x20])), check, "MapProps_TruncatedMemberRejected");
        var wrong = (byte[])good.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(wrong, 0x400);
        Reject<InvalidDataException>(() => PlatinumLandProps.ParseNarc(Archive(wrong)), check, "MapProps_UnexpectedTerrainSizeRejected");
        var lying = (byte[])good.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(lying.AsSpan(4), 0xFFFFFF00);
        Reject<InvalidDataException>(() => PlatinumLandProps.ParseNarc(Archive(lying)), check, "MapProps_SizeLargerThanTheMemberRejected");
        Reject<InvalidDataException>(() => PlatinumLandProps.ParseNarc(Archive(good.Concat(new byte[4]).ToArray())), check, "MapProps_TrailingBytesInTheMemberRejected");
        Reject<InvalidDataException>(() => PlatinumLandProps.ParseNarc(new byte[64]), check, "MapProps_NonNarcRejected");
        var mutable = Archive(good); var copy = PlatinumLandProps.ParseNarc(mutable); Array.Fill(mutable, (byte)0xFF);
        check(copy.Props(0)[0].ModelId == 23, "MapProps_InputMutationCannotChangeTheReader");
        string folder = Path.Combine(Path.GetTempPath(), "nds-props-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            foreach (var (name, code, revision) in new[] { ("UnaccreditedArchive", "CPUE", (byte)1), ("OtherGame", "CPUP", (byte)1) })
            {
                byte[] rom = NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]> { [PlatinumTerrain.LandDataPath] = Archive(good) }, code, revision);
                string path = Path.Combine(folder, name + ".nds"); File.WriteAllBytes(path, rom);
                using (var open = NdsRom.Open(path)) Reject<InvalidDataException>(() => PlatinumLandProps.Open(open), check, "MapProps_OpenRejects" + name);
                check(File.ReadAllBytes(path).SequenceEqual(rom), "MapProps_RejectionLeavesSourceUnchanged_" + name);
            }
        }
        finally { Directory.Delete(folder, true); }

        // ---- placement on a matrix
        var layout = PlatinumMapLayout.ParseMatrix(PlatinumTerrainChecks.Matrix(2, 2, i => i == 0 ? 411 : 300, i => new[] { 0, 2, 0xFFFF, 0 }[i]), 411, 0);
        var placed = props.Place(layout);
        check(placed.Count == 4 + 1 + 4 && placed.Count(p => p.BlockX == 0 && p.BlockY == 0) == 4 && placed.Count(p => p.BlockX == 1 && p.BlockY == 0) == 1 && placed.Count(p => p.BlockY == 1 && p.BlockX == 0) == 0, "MapProps_EmptyCellsContributeNothing");
        var p0 = placed.First(p => p.BlockX == 0 && p.BlockY == 0 && p.Prop.ModelId == 23); var p3 = placed.First(p => p.BlockX == 1 && p.BlockY == 1 && p.Prop.ModelId == 23);
        check(p0.TileX == 10.5 && p0.TileZ == 10 && p3.TileX == 32 + 10.5 && p3.TileZ == 32 + 10 && p3.LandDataId == 0, "MapProps_SameLandBlockOnTwoCellsGivesTwoPlacements");
        check(placed.First(p => p.BlockX == 1 && p.BlockY == 0).TileX == 32 + 16, "MapProps_BlockOffsetsAreExact");
        check(p0.InOwnedBlock && !placed.First(p => p.BlockX == 1 && p.BlockY == 0).InOwnedBlock && !p3.InOwnedBlock, "MapProps_OwnershipFollowsTheStoredHeadersOfTheCell");
        var contextual = PlatinumMapLayout.ParseMatrix(PlatinumTerrainChecks.Matrix(1, 1, null, _ => 0), 5, 0);
        check(props.Place(contextual).All(p => p.InOwnedBlock) && props.Place(PlatinumMapLayout.ParseMatrix(PlatinumTerrainChecks.Matrix(1, 1, null, _ => 9), 5, 0)).Count == 0, "MapProps_ContextualCellsAndIdsOutsideTheArchive");
        var cast = (IList<PlatinumPlacedProp>)placed; Reject<NotSupportedException>(() => cast.Clear(), check, "MapProps_PlacementsAreReadOnly");
        // Twinleaf-like case: block (3,27) with the real positions
        var twin = PlatinumMapLayout.ParseMatrix(PlatinumTerrainChecks.Matrix(30, 30, i => i == 3 + 27 * 30 ? 411 : 300, i => i == 3 + 27 * 30 ? 0 : 0xFFFF), 411, 0);
        var twinProps = props.Place(twin);
        check(twinProps.Count == 4 && twinProps.All(p => p.BlockX == 3 && p.BlockY == 27 && p.InOwnedBlock) && twinProps.First(p => p.Prop.ModelId == 23) is { TileX: 106.5, TileZ: 874.0 }, "MapProps_Case411PositionsLandInsideBlock3_27");

        // ---- model: layer, hit test, selection, description
        var events = new[] { new PlatinumEvent(PlatinumEventKind.Npc, 0, 0, 119, 878, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, -1, "00") };
        var model = new NdsMapCanvasModel(twin, events, null, (960, 960), props); model.SetViewport(1000, 700); model.SetZoom(16, 500, 350);
        var target = twinProps.First(p => p.Prop.ModelId == 23); var (px, py) = model.TileToPixel(target.TileX, target.TileZ); model.PanBy(px - 500, py - 350); (px, py) = model.TileToPixel(target.TileX, target.TileZ);
        check(model.HasProps && (NdsMapLayers.All & NdsMapLayers.Structures) != 0 && model.HitProp(px + 3, py - 2) == target && model.HitProp(px + 30, py) is null, "MapPropsModel_HitFindsTheStructureUnderTheCursor");
        model.Layers &= ~NdsMapLayers.Structures; check(model.HitProp(px, py) is null, "MapPropsModel_HiddenLayerIsNotHit"); model.Layers = NdsMapLayers.All;
        model.Select(PlatinumEventKind.Npc, 0); model.SelectProp(target);
        check(model.SelectedProp == target && model.Selected is null, "MapPropsModel_SelectingAStructureLeavesTheEventSelection"); model.SelectProp(null);
        string text = model.DescribeProp(target);
        check(text.Contains("modelo 23") && text.Contains("land_data 0") && text.Contains("bloque (3,27)") && text.Contains("celda asociada explícitamente a la cabecera 411") && text.Contains("X 106.50, Z 874.00") && text.Contains("posición (-360448, 65536, -393216)"), "MapPropsModel_DescriptionNamesTheProvenance");
        check(text.Contains("NO se leen") && text.Contains("no sustituye a la colisión") && !text.Contains("casa"), "MapPropsModel_DescriptionStatesTheLimits");
        var other = twinProps[0] with { InOwnedBlock = false }; check(model.DescribeProp(other).Contains("celda de otra cabecera (411)") || model.DescribeProp(other).Contains("celda de otra cabecera"), "MapPropsModel_OtherHeaderCellsAreSaidSo");
        check(!new NdsMapCanvasModel(twin, events, null, null, null).HasProps && new NdsMapCanvasModel(twin, events).HitProp(5, 5) is null, "MapPropsModel_WorksWithoutPropData");

        // ---- size and speed on the biggest matrix
        var many = Archive(Land(Enumerable.Range(0, 32).Select(i => (i + 1, (double)(i * 5 - 80), 16.0, (double)(80 - i * 5))).ToArray()));
        var big = PlatinumLandProps.ParseNarc(many); var clock = System.Diagnostics.Stopwatch.StartNew();
        var all = PlatinumMapLayout.ParseMatrix(PlatinumTerrainChecks.Matrix(30, 30, _ => 300, _ => 0), 411, 0); var bigModel = new NdsMapCanvasModel(all, [], null, null, big); bigModel.SetViewport(1100, 700); bigModel.FitMatrix();
        for (int i = 0; i < 300; i++) { bigModel.PanBy(3, 2); _ = bigModel.HitProp(500, 300); }
        check(bigModel.Props.Count == 900 * 32 && clock.ElapsedMilliseconds < 3000, "MapPropsModel_960x960WithTwentyEightThousandStructuresStaysFast");
    }

    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject => NdsMapCanvasChecks.Children<T>(root).Single(e => AutomationProperties.GetName(e) == name);

    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-props-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = NdsMapCanvasChecks.Make(); string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, f.Read()); byte[] eventsBefore = session.Data.Bytes();
            var props = PlatinumLandProps.ParseNarc(Archive(Land(Twinleaf[..2])));
            var view = new NdsEventEditorView(session, null, null, props); var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single(); var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single(); var tabs = NdsMapCanvasChecks.Children<TabControl>(view).Single();
            var details = Find<TextBox>(view, "Eventos DS · propiedades y referencias (copiable)"); var hover = Find<TextBlock>(view, "Eventos DS · posición bajo el cursor");
            NdsMapCanvasChecks.Arrange(view, 1100, 760); maps.SelectedItem = maps.Items[1]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var layer = Find<CheckBox>(view, "Eventos DS · capa Estructuras"); var models = Find<CheckBox>(view, "Eventos DS · capa Modelos/texturas");
            bool Untouched() => session.Revision == 0 && session.Data.Bytes().SequenceEqual(eventsBefore) && NdsMapCanvasChecks.Sha(source) == sourceHash && Directory.GetFiles(temp).Length == 1;
            check(layer.IsEnabled && layer.IsChecked == true && canvas.Model!.HasProps && canvas.Model.Props.Count == 900 * 2, "MapPropsUi_StructuresLayerIsOnWhenThereIsData");
            check(!models.IsEnabled && models.IsChecked == false && models.ToolTip is string t && t.Contains("NSBMD") && t.Contains("no están implementados") && AutomationProperties.GetHelpText(models).Contains("NSBTX"), "MapPropsUi_ModelsAndTexturesLayerIsHonestlyUnavailable");
            check(NdsMapCanvasChecks.Children<TextBlock>(view).Any(x => x.Text.Contains("estructura (map prop: posición e ID de modelo, sin forma)")), "MapPropsUi_LegendExplainsTheMarker");

            var target = canvas.Model!.Props.First(p => p.Prop.ModelId == 23 && p.BlockX == 3 && p.BlockY == 27);
            canvas.Model.SetZoom(16, canvas.Model.ViewportWidth / 2, canvas.Model.ViewportHeight / 2); var c0 = canvas.Model.TileToPixel(target.TileX, target.TileZ); canvas.Model.PanBy(c0.X - canvas.Model.ViewportWidth / 2, c0.Y - canvas.Model.ViewportHeight / 2); canvas.Invalidate();
            var at = canvas.Model.TileToPixel(target.TileX, target.TileZ); var point = new Point(at.X, at.Y);
            byte[] withLayer = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            layer.IsChecked = false; byte[] without = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check((canvas.Model.Layers & NdsMapLayers.Structures) == 0 && !withLayer.SequenceEqual(without) && canvas.Model.HitProp(point.X, point.Y) is null, "MapPropsUi_LayerToggleChangesTheDrawingAndTheHits");
            layer.IsChecked = true; check(NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight).SequenceEqual(withLayer), "MapPropsUi_LayerToggleIsReversible");
            tabs.SelectedIndex = 0; var npcTable = NdsMapCanvasChecks.Children<DataGrid>(view).First(); var selectedBefore = npcTable.SelectedItem;
            canvas.ClickAt(point);
            check(canvas.Model.SelectedProp == target && details.Text.Contains("Estructura (map prop) · modelo 23") && details.Text.Contains("Procedencia: land_data 0") && details.Text.Contains("bloque (3,27)") && details.Text.Contains("celda asociada explícitamente a la cabecera 1") && details.Text.Contains("NO se leen"), "MapPropsUi_ClickShowsProvenanceAndLimits");
            check(ReferenceEquals(npcTable.SelectedItem, selectedBefore) && Find<TextBlock>(view, "Eventos DS · estado") is not null && Untouched(), "MapPropsUi_PickingAStructureDoesNotTouchTheEventSelectionOrTheSession");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "props-411like-selected-1100");
            var move = new Point(point.X + 2, point.Y + 2);
            check(canvas.Model.HitProp(move.X, move.Y) == target, "MapPropsUi_HoverRadiusFindsTheStructure");
            npcTable.SelectedItem = npcTable.Items.Cast<PlatinumEvent>().Last(); npcTable.SelectedItem = npcTable.Items.Cast<PlatinumEvent>().First();
            check(canvas.Model.SelectedProp is null && details.Text.Contains("Mapa 1"), "MapPropsUi_SelectingAnEventClearsTheStructureHighlight");

            // Events keep priority over structures at the same spot
            var standalone = new NdsMapCanvasControl(); var layout2 = PlatinumMapLayout.ParseMatrix(PlatinumTerrainChecks.Matrix(30, 30, i => i == 3 + 27 * 30 ? 411 : 300, i => i == 3 + 27 * 30 ? 0 : 0xFFFF), 411, 0);
            var npcAtProp = new PlatinumEvent(PlatinumEventKind.Npc, 0, 0, 106, 874, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, -1, "00"); PlatinumEvent? picked = null; standalone.EventPicked += e => picked = e;
            NdsMapCanvasChecks.Arrange(standalone, 600, 400); standalone.SetMap(layout2, [npcAtProp], props: props); standalone.Model!.SetZoom(16, 300, 200); var sp = standalone.Model.TileToPixel(106.5, 874); standalone.Model.PanBy(sp.X - 300, sp.Y - 200); var sp2 = standalone.Model.TileToPixel(106.5, 874.5);
            standalone.ClickAt(new Point(sp2.X, sp2.Y));
            check(picked is not null && standalone.Model.SelectedProp is null, "MapPropsUi_AnEventUnderTheCursorWinsOverAStructure");

            // No data: the layer is unavailable and the map still opens
            var bare = new NdsEventEditorView(session); var bareMaps = NdsMapCanvasChecks.Children<ListBox>(bare).Single(); NdsMapCanvasChecks.Arrange(bare, 1100, 760); bareMaps.SelectedItem = bareMaps.Items[1]; NdsMapCanvasChecks.Arrange(bare, 1100, 760);
            var bareLayer = Find<CheckBox>(bare, "Eventos DS · capa Estructuras"); var bareCanvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(bare).Single();
            check(!bareLayer.IsEnabled && bareLayer.IsChecked == false && !bareCanvas.Model!.HasProps && bareCanvas.Model.Markers.Count == 7 && !NdsMapCanvasChecks.Children<TextBlock>(bare).Any(x => x.Text.Contains("estructura (map prop")), "MapPropsUi_WithoutDataTheMapStillOpens");

            // Speed with ~1800 structures on the 960x960 matrix, and both layouts
            canvas.FitMatrix(); var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 6; i++) { canvas.Model.PanBy(5 * i, 3 * i); canvas.Invalidate(); NdsMapCanvasChecks.Pixels(canvas, 1100, 700); }
            for (int i = 0; i < 6; i++) { canvas.FitEvents(); canvas.Model.SetZoom(10 + i * 3, 500, 300); canvas.Invalidate(); NdsMapCanvasChecks.Pixels(canvas, 1100, 700); }
            check(clock.ElapsedMilliseconds / 12 < 900, "MapPropsUi_RenderStaysFastWithStructures");
            foreach (int width in new[] { 1100, 760 })
            {
                NdsMapCanvasChecks.Arrange(view, width, 760); canvas.FitMatrix(); NdsMapCanvasChecks.Flush();
                check(canvas.ActualHeight >= 150 && view.DesiredSize.Width <= width, "MapPropsUi_LayoutAt" + width); NdsMapCanvasChecks.Capture(view, width, 760, "props-411like-" + width);
            }
            check(NdsMapCanvasChecks.Children<CheckBox>(view).All(c => AutomationProperties.GetName(c).Length > 0) && Untouched(), "MapPropsUi_AccessibleNamesAndNothingWritten");
        }
        finally { Directory.Delete(temp, true); }
    }

    /// <summary>Read-only: parses every real land_data member and tests the interpretation against the collision footprints of the owner's dump.</summary>
    internal static void Probe(string source, string reportPath)
    {
        string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        source = Path.GetFullPath(source); reportPath = Path.GetFullPath(reportPath);
        if (File.Exists(reportPath) || Directory.Exists(reportPath) || source.Equals(reportPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("La sonda requiere un archivo de informe nuevo, distinto de la ROM.");
        string before = Hash(source); object report;
        using (var rom = NdsRom.Open(source))
        {
            void Require(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
            var props = PlatinumLandProps.Open(rom); var terrain = PlatinumTerrain.Open(rom); var events = PlatinumEvents.Open(rom);
            Require(props.Count == 666, "MapPropsReal_666MembersParsed");
            int total = 0, withProps = 0, maxCount = 0, trailingFiles = 0, nonUnit = 0, rotated = 0; var models = new HashSet<int>();
            for (int id = 0; id < props.Count; id++)
            {
                var list = props.Props(id); total += list.Count; if (list.Count > 0) withProps++; maxCount = Math.Max(maxCount, list.Count); if (props.TrailingBytes(id) != 0) trailingFiles++;
                foreach (var p in list) { models.Add(p.ModelId); if (!p.HasUnitScale) nonUnit++; if (p.HasRotation) rotated++; }
            }
            Require(total == 3476 && withProps == 387 && maxCount == 32 && trailingFiles == 1 && nonUnit == 0 && rotated == 0, "MapPropsReal_CountsMatchTheObservedData");
            // Which axis mapping agrees with the collision data? Count props with a collision tile within 2 tiles, for several candidate mappings.
            (string Name, Func<double, double, (double, double)> F)[] candidates = [("centre", (x, z) => (x / 16 + 16, z / 16 + 16)), ("corner", (x, z) => (x / 16, z / 16)), ("swapped", (x, z) => (z / 16 + 16, x / 16 + 16)), ("zMirrored", (x, z) => (x / 16 + 16, 16 - z / 16)), ("xMirrored", (x, z) => (16 - x / 16, z / 16 + 16)), ("bothMirrored", (x, z) => (16 - x / 16, 16 - z / 16))];
            var near = new int[candidates.Length]; int n = 0;
            for (int id = 0; id < props.Count; id++)
                foreach (var p in props.Props(id))
                {
                    n++;
                    for (int c = 0; c < candidates.Length; c++)
                    {
                        var (tx, tz) = candidates[c].F(p.X, p.Z); int cx = (int)Math.Floor(tx), cz = (int)Math.Floor(tz); bool hit = false;
                        for (int dz = -2; dz <= 2 && !hit; dz++) for (int dx = -2; dx <= 2 && !hit; dx++) { int x = cx + dx, z = cz + dz; if (x >= 0 && x < 32 && z >= 0 && z < 32 && terrain.Attribute(id, x, z) >= 0x8000) hit = true; }
                        if (hit) near[c]++;
                    }
                }
            Require(near[0] * 100.0 / n >= 85 && Enumerable.Range(1, candidates.Length - 1).All(c => near[0] > near[c]), "MapPropsReal_BlockCentreMappingIsTheBestFitToTheCollisionData");
            var matrices = Narc.Read(rom.ReadFile(PlatinumEvents.MatrixPath)); int placedTotal = 0, outsideBlock = 0;
            foreach (var m in events.Maps.Select(x => x.Matrix).Distinct())
            {
                var layout = PlatinumMapLayout.ParseMatrix(matrices.Files[m], 0, m); var placed = props.Place(layout); placedTotal += placed.Count;
                outsideBlock += placed.Count(p => p.Prop.LocalTileX < 0 || p.Prop.LocalTileX >= 32 || p.Prop.LocalTileZ < 0 || p.Prop.LocalTileZ >= 32);
            }
            Require(placedTotal > 2500 && outsideBlock * 100.0 / placedTotal < 2, "MapPropsReal_NearlyEveryPropFallsInsideItsOwnBlock");
            var map = events.Map(411); var l411 = events.Layout(411); var twin = props.Place(l411).Where(p => p.BlockX == 3 && p.BlockY == 27).ToList();
            Require(twin.Count == 8 && twin.Select(p => p.Prop.ModelId).OrderBy(x => x).SequenceEqual([22, 22, 23, 67, 67, 67, 67, 236]) && twin.All(p => p.InOwnedBlock), "MapPropsReal_Block3_27HasTheEightStructuresOfLandData0");
            // The four house-like props must sit on the centre (within 1.5 tiles) of a collision footprint of that block
            var seen = new bool[32, 32]; var centres = new List<(double X, double Z)>();
            for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++)
                if (terrain.Attribute(0, x, z) >= 0x8000 && !seen[x, z])
                {
                    int minX = x, maxX = x, minZ = z, maxZ = z, count = 0; var stack = new Stack<(int, int)>(); stack.Push((x, z)); seen[x, z] = true;
                    while (stack.Count > 0)
                    {
                        var (a, b) = stack.Pop(); count++; minX = Math.Min(minX, a); maxX = Math.Max(maxX, a); minZ = Math.Min(minZ, b); maxZ = Math.Max(maxZ, b);
                        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) { int nx = a + dx, nz = b + dz; if (nx >= 0 && nx < 32 && nz >= 0 && nz < 32 && !seen[nx, nz] && terrain.Attribute(0, nx, nz) >= 0x8000) { seen[nx, nz] = true; stack.Push((nx, nz)); } }
                    }
                    if (count < 200) centres.Add(((minX + maxX + 1) / 2.0, (minZ + maxZ + 1) / 2.0));
                }
            var houses = twin.Where(p => p.Prop.ModelId is 22 or 23 or 236).ToList();
            Require(houses.Count == 4 && centres.Count == 4 && houses.All(h => centres.Any(c => Math.Abs(c.X - h.Prop.LocalTileX) <= 1.5 && Math.Abs(c.Z - h.Prop.LocalTileZ) <= 1.5)), "MapPropsReal_FourHouseModelsSitOnTheFourCollisionFootprints");
            report = new { profile = "CPUE/1", sourceSha256 = before, members = props.Count, totalProps = total, membersWithProps = withProps, maxPropsPerMember = maxCount, membersWithPartialEntry = trailingFiles, distinctModelIds = models.Count,
                allUnitScale = nonUnit == 0, anyRotation = rotated != 0, mappingFit = candidates.Select((c, i) => new { c.Name, nearCollisionPercent = Math.Round(near[i] * 100.0 / n, 1) }),
                block411 = new { x = 3, y = 27, landId = 0, models = twin.Select(p => p.Prop.ModelId).OrderBy(x => x), houseFootprintCentres = centres.Select(c => new { c.X, c.Z }) },
                interpretation = "Positions and model ids of map props (land_data section 2). Block-centre origin, 16 units per tile; NSBMD models, textures, rotation use and BDHC heights are NOT read. Not a rendering of the buildings.", readOnly = true };
        }
        if (Hash(source) != before) throw new Exception("FAIL Source SHA256 changed");
        using var output = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("PASS MapPropsReal_SourceSha256Unchanged; report created; gameplay NOT tested");
    }
}
