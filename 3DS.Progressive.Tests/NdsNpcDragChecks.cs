using System.Buffers.Binary;
using System.IO;
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
using Border = System.Windows.Controls.Border;
using Point = System.Windows.Point;

/// <summary>Visual NPC move: pure planning/drag math and WPF integration. Dragging never writes; only the existing Preview/checkpoint flow does.</summary>
internal static class NdsNpcDragChecks
{
    private static PlatinumEvent Ev(PlatinumEventKind kind, int index, int x, int y, int type = 0, int rx = 0, int ry = 0) =>
        new(kind, index, index, x, y, 0, 0, type, 0, 0, 0, 0, 0, rx, ry, -1, -1, "00");
    private static ushort Attr(int x, int z) => x >= 28 || x == 2 && z == 21 ? (ushort)0x8000 : x == 23 && z == 14 ? (ushort)0x0002 : (ushort)0;
    private static PlatinumTerrain Terrain() => PlatinumTerrain.ParseNarc(NdsGen5RandomizerChecks.BuildNarc([PlatinumTerrainChecks.Land(Attr)]));
    private static PlatinumMapLayout Twinleaf() => PlatinumMapLayout.ParseMatrix(PlatinumTerrainChecks.Matrix(30, 30, i => i == 3 + 27 * 30 ? 411 : 300, _ => 0), 411, 0);
    private static NdsMapCanvasModel Model(PlatinumTerrain? terrain, (int, int)? bounds, PlatinumEvent[] events)
    {
        var model = new NdsMapCanvasModel(Twinleaf(), events, terrain, bounds); model.SetViewport(1000, 700); return model;
    }
    private static readonly PlatinumEvent Npc0 = Ev(PlatinumEventKind.Npc, 0, 119, 878);
    private static PlatinumEvent[] Events() => [Npc0, Ev(PlatinumEventKind.Npc, 1, 5, 5), Ev(PlatinumEventKind.Npc, 2, 130, 885, 3), Ev(PlatinumEventKind.Npc, 3, 2000, 10), Ev(PlatinumEventKind.Npc, 4, 121, 878),
        Ev(PlatinumEventKind.Warp, 0, 120, 880), Ev(PlatinumEventKind.Trigger, 0, 110, 870, 0, 4, 2), Ev(PlatinumEventKind.Object, 0, 100, 870)];
    private static bool Near(double a, double b, double e = 1e-9) => Math.Abs(a - b) <= e;

    internal static void Run(Action<bool, string> check)
    {
        var terrain = Terrain(); var model = Model(terrain, (960, 960), Events());
        NdsNpcMovePlan Plan(int x, int y) => NdsNpcMovePlanner.Plan(model, Npc0, x, y);

        // ---- which events can be dragged
        check(NdsNpcMovePlanner.IsDraggable(Npc0) && !NdsNpcMovePlanner.IsDraggable(Ev(PlatinumEventKind.Npc, 2, 1, 1, 3)) && !NdsNpcMovePlanner.IsDraggable(Ev(PlatinumEventKind.Warp, 0, 1, 1))
            && !NdsNpcMovePlanner.IsDraggable(Ev(PlatinumEventKind.Object, 0, 1, 1)) && !NdsNpcMovePlanner.IsDraggable(Ev(PlatinumEventKind.Trigger, 0, 1, 1)), "NpcDrag_OnlyExistingNpcRecordsAreDraggable");
        check(NdsNpcMovePlanner.Plan(model, Ev(PlatinumEventKind.Warp, 0, 130, 880), 131, 880).Outcome == NdsMoveOutcome.Rejected, "NpcDrag_PlannerRefusesNonNpcEvents");

        // ---- planning policy
        var ready = Plan(120, 878);
        check(ready.Outcome == NdsMoveOutcome.Ready && ready.CanPropose && ready.Assessment!.Verdict == NdsTileVerdict.Allowed && ready.Summary == "Sin conflictos estáticos conocidos" && !ready.Summary.Contains("segur"), "NpcDrag_ClearTileIsReadyButNeverCalledSafe");
        var blocked = Plan(124, 878);
        check(blocked.Outcome == NdsMoveOutcome.Rejected && !blocked.CanPropose && blocked.Reasons.Any(r => r.Contains("colisión de terreno")), "NpcDrag_TerrainCollisionIsRejected");
        check(Plan(130, 885).Outcome == NdsMoveOutcome.Rejected, "NpcDrag_CollisionTileOfAGroundObjectIsRejected");
        check(Plan(121, 878) is { Outcome: NdsMoveOutcome.NeedsWarning } npcOn && npcOn.Reasons.Any(r => r.Contains("NPC 4")) && npcOn.CanPropose, "NpcDrag_TileWithAnotherNpcNeedsAWarning");
        check(Plan(120, 880) is { Outcome: NdsMoveOutcome.NeedsWarning } warpOn && warpOn.Reasons.Any(r => r.Contains("warp 0")), "NpcDrag_WarpTileNeedsAWarning");
        check(Plan(100, 870) is { Outcome: NdsMoveOutcome.NeedsWarning } objOn && objOn.Reasons.Any(r => r.Contains("objeto de fondo")), "NpcDrag_BackgroundObjectTileNeedsAWarning");
        check(Plan(111, 871) is { Outcome: NdsMoveOutcome.NeedsWarning } trig && trig.Reasons.Any(r => r.Contains("rango declarado")), "NpcDrag_TriggerRangeNeedsAWarning");
        check(Plan(6, 5) is { Outcome: NdsMoveOutcome.NeedsWarning } other && other.Reasons.Any(r => r.Contains("otra cabecera")), "NpcDrag_CellOfAnotherHeaderNeedsAWarning");
        check(Plan(950, 959).Outcome == NdsMoveOutcome.NeedsWarning && Plan(960, 878).Outcome == NdsMoveOutcome.Rejected && Plan(878, 960).Outcome == NdsMoveOutcome.Rejected, "NpcDrag_MatrixEdgeAcceptedOutsideRejected");
        check(Plan(-1, 5).Outcome == NdsMoveOutcome.Rejected && Plan(5, -300).Outcome == NdsMoveOutcome.Rejected && Plan(-1, 5).Reasons.Any(r => r.Contains("matriz")), "NpcDrag_NegativeCoordinatesAreRejected");
        var own = Plan(119, 878);
        check(own.Outcome == NdsMoveOutcome.NoChange && !own.CanPropose && own.Assessment is null, "NpcDrag_StayingOnItsOwnTileIsNoChange");
        var snapshot = NdsNpcMovePlanner.Plan(model, Npc0, 120, 878);
        check(snapshot.Assessment!.Occupants.All(o => o.Index != 0 || o.Kind != PlatinumEventKind.Npc), "NpcDrag_OwnTileDoesNotCountAsOccupied");
        var narrow = Model(terrain, (64, 96), Events());
        check(NdsNpcMovePlanner.Plan(narrow, Npc0, 100, 50) is { Outcome: NdsMoveOutcome.Rejected } cons && cons.Reasons.Any(r => r.Contains("límites que acepta el editor")) && NdsNpcMovePlanner.Plan(narrow, Npc0, 100, 50).Assessment!.Terrain.Status == PlatinumTerrainStatus.ClearOfCollision, "NpcDrag_SharedFileConservativeBoundsRejectEvenWithoutCollision");
        check(NdsNpcMovePlanner.Plan(narrow, Npc0, 10, 10).Outcome == NdsMoveOutcome.NeedsWarning, "NpcDrag_InsideConservativeBoundsStillOnlyAWarningInAnotherHeaderCell");
        var bare = Model(null, (960, 960), Events());
        check(NdsNpcMovePlanner.Plan(bare, Npc0, 120, 878) is { Outcome: NdsMoveOutcome.NeedsWarning } noData && noData.Reasons.Any(r => r.Contains("sin datos de terreno")), "NpcDrag_WithoutTerrainDataNothingIsReady");
        check(new[] { Plan(120, 878), Plan(121, 878), Plan(124, 878), Plan(960, 878) }.All(p => p.Outcome == NdsMoveOutcome.Rejected == !p.CanPropose || p.CanPropose), "NpcDrag_OnlyReadyOrWarnedPlansCanBeProposed");

        // ---- pixel -> tile math with the canvas model (zoom, pan, negatives)
        foreach (double zoom in new[] { 0.5, 1, 3.7, 16, 64 })
        {
            var m = Model(terrain, (960, 960), Events()); m.SetZoom(zoom, 500, 350); m.PanBy(zoom * 3, -zoom * 2);
            bool exact = true;
            foreach (var (tx, ty) in new[] { (119, 878), (120, 878), (0, 0), (959, 959), (-3, 4), (500, 12) })
            {
                var c = m.TileToPixel(tx + 0.5, ty + 0.5); var session = new NdsNpcDragSession(m, Npc0, c.X + 40, c.Y); session.Update(c.X + 40 + 5, c.Y); session.Update(c.X, c.Y);
                if (session.TargetX != tx || session.TargetY != ty) exact = false;
                var left = m.TileToPixel(tx, ty); var nearLeft = new NdsNpcDragSession(m, Npc0, left.X + 100, left.Y); nearLeft.Update(left.X + 0.02 * Math.Min(1, zoom), left.Y + 0.02 * Math.Min(1, zoom));
                var nearRight = new NdsNpcDragSession(m, Npc0, left.X + 100, left.Y); nearRight.Update(left.X + zoom - 0.02 * Math.Min(1, zoom), left.Y + zoom - 0.02 * Math.Min(1, zoom));
                if (nearLeft.TargetX != tx || nearLeft.TargetY != ty || nearRight.TargetX != tx || nearRight.TargetY != ty) exact = false;
            }
            check(exact, "NpcDrag_PixelToTileIsExactAtZoom" + zoom.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var s = Model(terrain, (960, 960), Events()); var origin = s.TileToPixel(119.5, 878.5);
        s.SetZoom(16, 500, 350); var fitOrigin = s.TileToPixel(119.5, 878.5);
        var drag = new NdsNpcDragSession(s, Npc0, fitOrigin.X, fitOrigin.Y);
        check(!drag.Update(fitOrigin.X + 2, fitOrigin.Y + 3) && !drag.Active && drag.Complete() is null, "NpcDrag_MovesUnderTheThresholdAreClicks");
        var step = s.TileToPixel(121.5, 878.5);
        check(drag.Update(step.X, step.Y) && drag.Active && drag.TargetX == 121 && !drag.Update(step.X + 0.1, step.Y) && drag.Complete() is { } done && done.ToX == 121, "NpcDrag_ActivatesAfterTheThresholdAndReportsOnlyTileChanges");
        drag.Update(fitOrigin.X + 0.2, fitOrigin.Y);
        check(drag.Active && drag.Plan.Outcome == NdsMoveOutcome.NoChange && drag.Complete() is null, "NpcDrag_ReturningToTheOwnTileCancelsTheProposal");
        check(s.Markers.First(k => k.Index == 0 && k.Kind == PlatinumEventKind.Npc).Event == Npc0 && Npc0.X == 119 && Npc0.Y == 878, "NpcDrag_DraggingDoesNotMutateEvents");
        var panned = Model(terrain, (960, 960), Events()); panned.PanBy(-5000, -5000); var farLeft = new NdsNpcDragSession(panned, Npc0, 100, 100); farLeft.Update(10, 10);
        check(farLeft.Active && farLeft.Plan.Outcome is NdsMoveOutcome.Rejected or NdsMoveOutcome.NeedsWarning, "NpcDrag_ClampedPanStillMapsToIntegerTiles");

        var clock = System.Diagnostics.Stopwatch.StartNew(); var perf = Model(terrain, (960, 960), Enumerable.Range(0, 4000).Select(i => Ev(PlatinumEventKind.Npc, i, i % 960, i / 5 % 960)).ToArray());
        perf.FitMatrix(); var ps = new NdsNpcDragSession(perf, perf.Markers[0].Event, 100, 100); for (int i = 0; i < 600; i++) ps.Update(100 + i % 400, 100 + i % 300);
        check(clock.ElapsedMilliseconds < 4000, "NpcDrag_PlanningStaysFastWithManyEvents");
    }

    // ------------------------------------------------------------------------------------------ WPF integration
    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject => NdsMapCanvasChecks.Children<T>(root).Single(e => AutomationProperties.GetName(e) == name);

    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-npc-drag-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var f = NdsMapCanvasChecks.Make(); string source = Path.Combine(temp, "source.nds"); File.WriteAllBytes(source, f.Rom()); string sourceHash = NdsMapCanvasChecks.Sha(source);
            string outputDir = Path.Combine(temp, "out"); Directory.CreateDirectory(outputDir);
            using var rom = NdsRom.Open(source); var data = f.Read(); var session = new NdsEventSession(rom, data); byte[] eventsBefore = session.Data.Bytes(); string normalizedBefore = session.Data.NormalizedHash();
            var view = new NdsEventEditorView(session, Terrain(), null); var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single();
            var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single(); var tabs = NdsMapCanvasChecks.Children<TabControl>(view).Single(); var npcTable = NdsMapCanvasChecks.Children<DataGrid>(view).First();
            TextBox Box(string name) => Find<TextBox>(view, "Eventos DS · " + name); Button Btn(string label) => Find<Button>(view, "Eventos DS · " + label);
            var toolSelect = Find<RadioButton>(view, "Eventos DS · herramienta seleccionar"); var toolMove = Find<RadioButton>(view, "Eventos DS · herramienta mover NPC");
            var box = Find<Border>(view, "Eventos DS · propuesta de movimiento"); var detail = Find<TextBlock>(view, "Eventos DS · detalle de la propuesta"); var hover = Find<TextBlock>(view, "Eventos DS · posición bajo el cursor");
            var accept = Btn("aceptar propuesta"); var cancel = Btn("cancelar propuesta"); var help = Find<TextBlock>(view, "Eventos DS · ayuda de la herramienta");
            NdsMapCanvasChecks.Arrange(view, 1100, 760); maps.SelectedItem = maps.Items[1]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            Point Center(PlatinumEventKind kind, int index, double zoom = 16) { NdsMapCanvasChecks.CenterOn(canvas, kind, index, zoom); var mk = canvas.Model!.Find(kind, index)!; var c = canvas.Model.TileToPixel(mk.CenterX, mk.CenterY); return new Point(c.X, c.Y); }
            Point TileCentre(int x, int y) { var c = canvas.Model!.TileToPixel(x + 0.5, y + 0.5); return new Point(c.X, c.Y); }
            bool Untouched() => session.Revision == 0 && session.Data.Bytes().SequenceEqual(eventsBefore) && NdsMapCanvasChecks.Sha(source) == sourceHash && Directory.GetFiles(temp).Length == 1 && Directory.GetFiles(outputDir).Length == 0;

            // Tools: Select keeps the old behaviour, Move enables dragging
            check(toolSelect.IsChecked == true && canvas.Tool == NdsMapTool.Select && (box.Visibility != Visibility.Visible) && help.Text.Contains("Seleccionar"), "NpcDragUi_SelectToolIsTheDefault");
            var at = Center(PlatinumEventKind.Npc, 0);
            check(!canvas.BeginDragAt(at) && canvas.Drag is null, "NpcDragUi_SelectToolNeverStartsADrag");
            toolMove.IsChecked = true;
            check(canvas.Tool == NdsMapTool.MoveNpc && help.Text.Contains("Mover NPC") && help.Text.Contains("Esc cancela") && help.Text.Contains("no se guarda nada") && help.Text.Contains("botón central"), "NpcDragUi_MoveToolExplainsCancelAndPanning");
            check(!canvas.BeginDragAt(new Point(at.X + 150, at.Y + 150)) && !canvas.DragDeclined && canvas.Drag is null, "NpcDragUi_EmptySpaceStillPansInMoveTool");
            foreach (var (kind, index) in new[] { (PlatinumEventKind.Warp, 0), (PlatinumEventKind.Object, 0), (PlatinumEventKind.Trigger, 0), (PlatinumEventKind.Npc, 2) })
                check(!canvas.BeginDragAt(Center(kind, index)) && canvas.Drag is null, $"NpcDragUi_{kind}{index}IsNotDraggable");
            at = Center(PlatinumEventKind.Npc, 0);

            // A drag shows a ghost and live feedback, and changes no data
            byte[] idle = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check(canvas.BeginDragAt(at) && canvas.Drag is { Active: false }, "NpcDragUi_MoveToolStartsTheDragOnAnNpc");
            canvas.DragTo(TileCentre(120, 878));
            check(canvas.Drag is { Active: true, TargetX: 120, TargetY: 878 } && hover.Text.Contains("X 120, Y 878") && hover.Text.Contains("Sin conflictos estáticos conocidos") && !hover.Text.Contains("seguro"), "NpcDragUi_LiveFeedbackNamesTheTargetAndState");
            canvas.Invalidate(); byte[] ghost = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check(!idle.SequenceEqual(ghost), "NpcDragUi_DraggingDrawsTheGhostAndTargetTile");
            canvas.DragTo(TileCentre(124, 878)); canvas.Invalidate(); byte[] red = NdsMapCanvasChecks.Pixels(canvas, (int)canvas.ActualWidth, (int)canvas.ActualHeight);
            check(hover.Text.Contains("Rechazado") && !red.SequenceEqual(ghost) && Untouched(), "NpcDragUi_BlockedTargetShowsARejectedShapeAndWritesNothing");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-drag-rejected-1100");
            canvas.DragTo(TileCentre(111, 871)); canvas.Invalidate(); NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-drag-warning-1100");
            check(hover.Text.Contains("Indeterminado") && Untouched(), "NpcDragUi_TriggerRangeTargetShowsAnIndeterminateShape");
            canvas.DragTo(TileCentre(120, 878)); canvas.Invalidate(); NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-drag-ready-1100");
            var dpi = new System.Windows.Media.Imaging.RenderTargetBitmap((int)canvas.ActualWidth * 2, (int)canvas.ActualHeight * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32); dpi.Render(canvas);
            check(canvas.Drag!.TargetX == 120 && canvas.Drag.TargetY == 878 && dpi.PixelWidth == (int)canvas.ActualWidth * 2, "NpcDragUi_HighDpiRenderingDoesNotChangeTheTargetTile");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 6; i++) { canvas.DragTo(TileCentre(120 + i % 3, 878)); canvas.Invalidate(); NdsMapCanvasChecks.Pixels(canvas, 1100, 700); }
            check(clock.ElapsedMilliseconds / 6 < 900, "NpcDragUi_RenderingWithAnActiveDragStaysFast");

            // Escape / cancel semantics
            canvas.CancelDrag();
            check(canvas.Drag is null && (box.Visibility != Visibility.Visible) && Untouched() && Box("Coordenada X").Text == "119", "NpcDragUi_CancelRestoresTheEditorExactly");
            check(canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)) && canvas.Drag is not null, "NpcDragUi_CanDragAgainAfterCancel");
            canvas.DragTo(TileCentre(120, 878)); toolSelect.IsChecked = true;
            check(canvas.Drag is null && canvas.Tool == NdsMapTool.Select && (box.Visibility != Visibility.Visible) && Untouched(), "NpcDragUi_SwitchingToolCancelsTheDrag"); toolMove.IsChecked = true;
            canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)); canvas.DragTo(TileCentre(120, 878)); maps.SelectedItem = maps.Items[2];
            check(canvas.Drag is null && (box.Visibility != Visibility.Visible) && canvas.Model!.Layout.MapId == 2 && Untouched(), "NpcDragUi_ChangingMapCancelsTheDrag");
            maps.SelectedItem = maps.Items[1]; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)); canvas.DragTo(TileCentre(120, 878)); canvas.DropAt(TileCentre(120, 878));
            var modeTables = Find<RadioButton>(view, "Eventos DS · modo tablas"); var modeMap = Find<RadioButton>(view, "Eventos DS · modo mapa esquemático");
            check((box.Visibility == Visibility.Visible), "NpcDragUi_DropOpensAProposalWithoutSaving");
            modeTables.IsChecked = true; check((box.Visibility != Visibility.Visible) && canvas.Drag is null && Untouched(), "NpcDragUi_ChangingModeClosesTheProposal"); modeMap.IsChecked = true; NdsMapCanvasChecks.Arrange(view, 1100, 760);
            check(canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)) && canvas.Drag is not null, "NpcDragUi_BeginningADragNeverNeedsTheGuard"); canvas.CancelDrag();

            // Click without dragging is a normal selection
            canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 1)); canvas.DropAt(Center(PlatinumEventKind.Npc, 1));
            check((box.Visibility != Visibility.Visible) && canvas.Model!.Selected == (PlatinumEventKind.Npc, 1) && Box("Coordenada X").Text == "5" && Untouched(), "NpcDragUi_ClickInMoveToolStillSelects");

            // Proposal -> cancel keeps the editor as it was
            tabs.SelectedIndex = 0; npcTable.SelectedItem = npcTable.Items.Cast<PlatinumEvent>().First(e => e.Index == 1);
            canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)); canvas.DragTo(TileCentre(120, 878)); canvas.DropAt(TileCentre(120, 878));
            check((box.Visibility == Visibility.Visible) && (accept.Visibility == Visibility.Visible) && accept.Content.ToString() == "Aceptar propuesta" && detail.Text.Contains("NPC 0 (ID 20) — Mapa 1") && detail.Text.Contains("Original:  X=119, Y=878 · bloque (3,27), local (23,14)") && detail.Text.Contains("Propuesto: X=120, Y=878 · bloque (3,27), local (24,14)") && detail.Text.Contains("Sin conflictos estáticos conocidos"), "NpcDragUi_ProposalComparesBeforeAndAfter");
            check(detail.Text.Contains("No evaluado:") && detail.Text.Contains("alturas") && detail.Text.Contains("scripts") && detail.Text.Contains("no es una confirmación de que el movimiento sea seguro") && detail.Text.Contains("Aceptar NO guarda nada"), "NpcDragUi_ProposalStatesWhatIsNotEvaluated");
            check(Box("diferencias previstas").Text == "" && !Btn("Guardar cambios…").IsEnabled && Box("Coordenada X").Text == "5" && canvas.Model!.Selected == (PlatinumEventKind.Npc, 1) && Untouched(), "NpcDragUi_ProposalDoesNotTouchFieldsOrPreview");
            cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check((box.Visibility != Visibility.Visible) && Box("Coordenada X").Text == "5" && Box("diferencias previstas").Text == "" && !Btn("Guardar cambios…").IsEnabled && canvas.PendingMove is null && Untouched(), "NpcDragUi_CancellingTheProposalChangesNothing");

            // Rejections: collision, matrix edge, nothing to accept
            canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)); canvas.DragTo(TileCentre(124, 878)); canvas.DropAt(TileCentre(124, 878));
            check((box.Visibility == Visibility.Visible) && (accept.Visibility != Visibility.Visible) && cancel.Content.ToString() == "Cerrar" && detail.Text.Contains("Estado: Rechazado") && detail.Text.Contains("colisión de terreno") && detail.Text.Contains("no se puede aceptar") && Untouched(), "NpcDragUi_CollisionIsRejectedWithItsReason");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-proposal-rejected-1100"); cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)); canvas.DragTo(TileCentre(965, 878)); canvas.DropAt(TileCentre(965, 878));
            check((box.Visibility == Visibility.Visible) && (accept.Visibility != Visibility.Visible) && detail.Text.Contains("fuera de la matriz") && detail.Text.Contains("fuera de los límites que acepta el editor") && Untouched(), "NpcDragUi_OutOfBoundsIsAlwaysRejected"); cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)); canvas.DragTo(TileCentre(119, 878)); canvas.DropAt(TileCentre(119, 878));
            check((box.Visibility != Visibility.Visible) && Untouched(), "NpcDragUi_DroppingOnItsOwnTileOpensNothing");

            // Indeterminate: explicit warning, still goes through Preview only
            canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)); canvas.DragTo(TileCentre(111, 871)); canvas.DropAt(TileCentre(111, 871));
            check((box.Visibility == Visibility.Visible) && (accept.Visibility == Visibility.Visible) && accept.Content.ToString() == "Aceptar propuesta con advertencia" && detail.Text.Contains("Estado: Indeterminado") && detail.Text.Contains("rango declarado") && Untouched(), "NpcDragUi_IndeterminateRequiresAnExplicitWarningAcceptance");
            NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-proposal-warning-1100"); cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            // Pending fields are respected (ConfirmLeave is auto-approved by the test host, so we assert what it protects)
            Box("Coordenada X").Text = "7";
            check(Btn("Previsualizar diferencias").IsEnabled, "NpcDragUi_FieldsCanBeEdited");
            check(canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)) && Box("Coordenada X").Text == "7", "NpcDragUi_MouseDownAloneKeepsPendingFields"); canvas.CancelDrag();
            check(Box("Coordenada X").Text == "7", "NpcDragUi_CancelBeforeActivationKeepsPendingFields");
            tabs.SelectedIndex = 0; npcTable.SelectedItem = npcTable.Items.Cast<PlatinumEvent>().First(e => e.Index == 0); Box("Coordenada X").Text = "125"; view.PreviewSelection();
            string previewText = Box("diferencias previstas").Text;
            check(previewText.Contains("119 → 125") && Btn("Guardar cambios…").IsEnabled, "NpcDragUi_AnExistingPreviewIsPending");
            var realGuard = canvas.DragGuard; int calls = 0; canvas.DragGuard = e => { calls++; return realGuard!(e); };
            var origin = Center(PlatinumEventKind.Npc, 0);
            // A plain click on the selected NPC: no guard, nothing discarded, no re-selection
            canvas.BeginDragAt(origin); canvas.DropAt(origin);
            check(calls == 0 && Box("Coordenada X").Text == "125" && Box("diferencias previstas").Text == previewText && Btn("Guardar cambios…").IsEnabled && (box.Visibility != Visibility.Visible) && Untouched(), "NpcDragUi_ClickWithoutDraggingPreservesFieldsAndPreview");
            // Movement under the threshold, then cancelled
            canvas.BeginDragAt(origin); canvas.DragTo(new Point(origin.X + 2, origin.Y + 2)); canvas.CancelDrag();
            check(calls == 0 && canvas.Drag is null && Box("Coordenada X").Text == "125" && Box("diferencias previstas").Text == previewText && Btn("Guardar cambios…").IsEnabled, "NpcDragUi_CancelledBeforeTheThresholdPreservesFieldsAndPreview");
            // Movement under the threshold, released: still a click
            canvas.BeginDragAt(origin); canvas.DragTo(new Point(origin.X + 3, origin.Y)); canvas.DropAt(new Point(origin.X + 3, origin.Y));
            check(calls == 0 && Box("Coordenada X").Text == "125" && Box("diferencias previstas").Text == previewText && (box.Visibility != Visibility.Visible), "NpcDragUi_ReleaseUnderTheThresholdIsOnlyAClick");
            // The guard is asked exactly when the drag activates; a refusal keeps everything and ends the drag
            canvas.DragGuard = e => { calls++; return false; };
            canvas.BeginDragAt(origin); int before = calls; canvas.DragTo(new Point(origin.X + 40, origin.Y));
            check(before == 0 && calls == 1 && canvas.Drag is null && canvas.DragDeclined && Box("Coordenada X").Text == "125" && Box("diferencias previstas").Text == previewText && Btn("Guardar cambios…").IsEnabled && (box.Visibility != Visibility.Visible) && Untouched(), "NpcDragUi_RefusedConfirmationPreservesFieldsAndPreview");
            canvas.DragTo(new Point(origin.X + 80, origin.Y)); canvas.DropAt(new Point(origin.X + 80, origin.Y));
            check(calls == 1 && (box.Visibility != Visibility.Visible) && Box("diferencias previstas").Text == previewText && Untouched(), "NpcDragUi_AfterARefusalTheRestOfTheGestureDoesNothing");
            canvas.BeginDragAt(origin); canvas.DragTo(new Point(origin.X + 40, origin.Y)); canvas.DropAt(new Point(origin.X + 40, origin.Y));
            check(calls == 2 && (box.Visibility != Visibility.Visible) && Box("diferencias previstas").Text == previewText && Untouched(), "NpcDragUi_ARefusedDropOpensNoProposal");
            // Activating at the drop itself (no intermediate move) also asks first
            canvas.BeginDragAt(origin); canvas.DropAt(new Point(origin.X + 60, origin.Y));
            check(calls == 3 && (box.Visibility != Visibility.Visible) && Box("diferencias previstas").Text == previewText && Untouched(), "NpcDragUi_ActivationAtTheDropAlsoAsksFirst");
            // An accepted confirmation: asked once, then the user's authorization discards the old preview
            canvas.DragGuard = e => { calls++; return realGuard!(e); }; calls = 0;
            canvas.BeginDragAt(origin); canvas.DragTo(new Point(origin.X + 40, origin.Y)); canvas.DragTo(new Point(origin.X + 56, origin.Y));
            check(calls == 1 && canvas.Drag is { Active: true } && !Btn("Guardar cambios…").IsEnabled && Box("diferencias previstas").Text == "", "NpcDragUi_AcceptedConfirmationIsAskedOnceAndThenDiscardsTheOldPreview"); canvas.CancelDrag(); canvas.DragGuard = realGuard;

            // Accept: fills X/Y and runs the existing Preview; nothing saved yet
            tabs.SelectedIndex = 0; npcTable.SelectedItem = npcTable.Items.Cast<PlatinumEvent>().First(e => e.Index == 0);
            canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)); canvas.DragTo(TileCentre(120, 878)); canvas.DropAt(TileCentre(120, 878));
            NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-proposal-ready-1100");
            accept.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check((box.Visibility != Visibility.Visible) && Box("Coordenada X").Text == "120" && Box("Coordenada Y").Text == "878" && Box("diferencias previstas").Text.Contains("Coordenada X: 119 → 120") && !Box("diferencias previstas").Text.Contains("Coordenada Y") && Btn("Guardar cambios…").IsEnabled, "NpcDragUi_AcceptFillsTheFieldsAndRunsTheExistingPreview");
            check(canvas.PendingMove is { X: 120, Y: 878 } && canvas.Model!.Selected == (PlatinumEventKind.Npc, 0) && (npcTable.SelectedItem as PlatinumEvent)?.Index == 0 && Untouched(), "NpcDragUi_AcceptSyncsCanvasTableAndInspectorWithoutSaving");
            canvas.Invalidate(); NdsMapCanvasChecks.Capture(view, 1100, 760, "npc-pending-1100");

            // Save -> immutable checkpoint, then restore
            string first = Path.Combine(outputDir, "move.ndsevents.json");
            NdsMapCanvasChecks.Pump(view.SaveToAsync(first));
            var doc = NdsEventSession.ReadDocument(first);
            check(session.Revision == 1 && session.Data.Npc(1, 0) is { X: 120, Y: 878 } && File.Exists(first) && doc.Edits.Count == 1 && doc.Edits[0].Before.X == 119 && doc.Edits[0].After.X == 120 && doc.Edits[0].After.Y == 878 && NdsMapCanvasChecks.Sha(source) == sourceHash, "NpcDragUi_CheckpointStoresTheDraggedPosition");
            check(session.Data.NormalizedHash() == normalizedBefore && !session.Data.Bytes().SequenceEqual(eventsBefore), "NpcDragUi_OnlyDirectionAndXYBytesChanged");
            check(canvas.Model!.Find(PlatinumEventKind.Npc, 0)!.X == 120 && canvas.PendingMove is null && (npcTable.Items.Cast<PlatinumEvent>().First(e => e.Index == 0)).X == 120 && Box("Coordenada X").Text == "120", "NpcDragUi_CanvasTableAndInspectorRefreshFromTheRealSession");
            check(canvas.Model.Layout.OwnedCells.Count == 1 && canvas.Model.Find(PlatinumEventKind.Npc, 0)!.Placement == NdsMarkerPlacement.InsideOwnedBlock, "NpcDragUi_Case411KeepsOnlyBlock3_27Owned");
            Btn("Restaurar último cambio…").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            string second = Path.Combine(outputDir, "restore.ndsevents.json"); NdsMapCanvasChecks.Pump(view.SaveToAsync(second));
            check(session.Revision == 2 && session.Data.Npc(1, 0) is { X: 119, Y: 878 } && session.Data.Bytes().SequenceEqual(eventsBefore) && canvas.Model!.Find(PlatinumEventKind.Npc, 0)!.X == 119 && NdsEventSession.ReadDocument(first).Edits.Count == 1 && File.Exists(first), "NpcDragUi_RestoreReturnsToTheOriginalBytesAndKeepsTheFirstCheckpoint");
            check(NdsMapCanvasChecks.Sha(source) == sourceHash && Directory.GetFiles(outputDir).Length == 2, "NpcDragUi_SourceIntactAndCheckpointsNeverOverwritten");

            // Swapping two NPC like the owner's manual test, entirely through the canvas
            var swap = new NdsEventEditorView(session, Terrain(), null); var swapCanvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(swap).Single(); var swapMaps = NdsMapCanvasChecks.Children<ListBox>(swap).Single();
            NdsMapCanvasChecks.Arrange(swap, 1100, 760); swapMaps.SelectedItem = swapMaps.Items[1]; NdsMapCanvasChecks.Arrange(swap, 1100, 760);
            Find<RadioButton>(swap, "Eventos DS · herramienta mover NPC").IsChecked = true;
            NdsMapCanvasChecks.CenterOn(swapCanvas, PlatinumEventKind.Npc, 0, 16); var p0 = swapCanvas.Model!.TileToPixel(119.5, 878.5);
            check(swapCanvas.BeginDragAt(new Point(p0.X, p0.Y)) && swapCanvas.Drag is not null, "NpcDragUi_AnEquivalentMoveCanBePreparedWithoutTypingCoordinates"); swapCanvas.CancelDrag();

            // Shared event files: warning and conservative bounds
            var shared = f with { Headers = (byte[])f.Headers.Clone() }; BinaryPrimitives.WriteUInt16LittleEndian(shared.Headers.AsSpan(2 * 24 + 16), 1);
            string sharedSource = Path.Combine(temp, "shared.nds"); File.WriteAllBytes(sharedSource, shared.Rom()); using var sharedRom = NdsRom.Open(sharedSource); var sharedSession = new NdsEventSession(sharedRom, shared.Read());
            var sharedView = new NdsEventEditorView(sharedSession, Terrain(), null); var sharedCanvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(sharedView).Single(); var sharedMaps = NdsMapCanvasChecks.Children<ListBox>(sharedView).Single();
            NdsMapCanvasChecks.Arrange(sharedView, 1100, 760); sharedMaps.SelectedItem = sharedMaps.Items[1]; NdsMapCanvasChecks.Arrange(sharedView, 1100, 760); Find<RadioButton>(sharedView, "Eventos DS · herramienta mover NPC").IsChecked = true;
            var sharedDetail = Find<TextBlock>(sharedView, "Eventos DS · detalle de la propuesta"); var sharedBox = Find<Border>(sharedView, "Eventos DS · propuesta de movimiento");
            NdsMapCanvasChecks.CenterOn(sharedCanvas, PlatinumEventKind.Npc, 0, 16); var sp = sharedCanvas.Model!.TileToPixel(119.5, 878.5); byte[] sharedBefore = sharedSession.Data.Bytes();
            check(sharedCanvas.BeginDragAt(new Point(sp.X, sp.Y)), "NpcDragUi_SharedFileNpcCanBeDragged");
            var target = sharedCanvas.Model.TileToPixel(120.5, 878.5); sharedCanvas.DragTo(new Point(target.X, target.Y)); sharedCanvas.DropAt(new Point(target.X, target.Y));
            check(sharedBox.Visibility == Visibility.Visible && sharedDetail.Text.Contains("compartido por 2 cabeceras (1, 2)") && sharedDetail.Text.Contains("Rechazado") && sharedDetail.Text.Contains("límites que acepta el editor") && Find<Button>(sharedView, "Eventos DS · aceptar propuesta").Visibility != Visibility.Visible, "NpcDragUi_SharedFileWarnsAndAppliesTheConservativeBounds");
            check(sharedSession.Data.Bytes().SequenceEqual(sharedBefore) && sharedSession.Revision == 0, "NpcDragUi_SharedFileProposalChangesNothing");

            // Layout at both widths
            foreach (int width in new[] { 1100, 760 })
            {
                NdsMapCanvasChecks.Arrange(view, width, 760); canvas.FitEvents(); toolMove.IsChecked = true;
                canvas.BeginDragAt(Center(PlatinumEventKind.Npc, 0)); canvas.DragTo(TileCentre(121, 878)); canvas.DropAt(TileCentre(121, 878));
                check((box.Visibility == Visibility.Visible) && canvas.ActualHeight >= 120 && view.DesiredSize.Width <= width && box.ActualHeight > 0 && Btn("cancelar propuesta").ActualWidth > 0, "NpcDragUi_LayoutKeepsTheCanvasAndProposalAt" + width);
                NdsMapCanvasChecks.Capture(view, width, 760, "npc-proposal-layout-" + width); cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            check(NdsMapCanvasChecks.Children<RadioButton>(view).Concat<Control>(NdsMapCanvasChecks.Children<Button>(view)).All(c => AutomationProperties.GetName(c).Length > 0), "NpcDragUi_NewControlsHaveAccessibleNames");
        }
        finally { Directory.Delete(temp, true); }
    }
}
