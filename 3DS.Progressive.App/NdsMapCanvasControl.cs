using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;
using FontFamily = System.Windows.Media.FontFamily;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace ThreeDS.Progressive;

/// <summary>
/// Schematic, read-only view of DS events over the matrix grid. Draws with a single DrawingContext pass (no visual per tile),
/// culls to the viewport and never edits data. It does NOT render the game's graphics, collisions or heights.
/// </summary>
internal sealed class NdsMapCanvasControl : FrameworkElement
{
    internal const string SchematicNotice = "Vista esquemática de eventos. Muestra colisiones estáticas de terreno cuando hay datos, pero no representa los gráficos, las alturas ni todas las reglas de movimiento del juego.";
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Brush ExplicitFill, ContextualFill, OtherFill, Surface, Inspect;
    private static readonly Pen GlyphPen = new(new SolidColorBrush(Color.FromArgb(0xC0, 0x40, 0x10, 0x10)), 1.5);
    private static readonly Pen BlockPen, TilePen, MatrixPen, ExplicitPen, SelectionPen, HaloPen, WarnPen;
    private NdsMapCanvasModel? _model;
    private Point _down, _last; private bool _pressed, _panning;
    private System.Windows.Media.Imaging.BitmapSource? _terrainBitmap;
    private NdsNpcDragSession? _drag;
    private (PlatinumEvent Npc, int X, int Y)? _pendingMove;
    private NdsMapTool _tool;
    private bool _middlePan;

    internal NdsMapCanvasModel? Model => _model;
    /// <summary>Raised only for user clicks/keys (not for programmatic selection) so tables stay the source of truth.</summary>
    internal event Action<PlatinumEvent?>? EventPicked;
    internal event Action? ViewChanged;
    /// <summary>Asked before a drag starts (the view applies ConfirmLeave). Returning false keeps everything as it was.</summary>
    internal Func<PlatinumEvent, bool>? DragGuard { get; set; }
    /// <summary>A completed drop with a different tile; nothing has been written. The view decides what to do with the plan.</summary>
    internal event Action<NdsNpcMovePlan>? MoveDropped;
    /// <summary>Live drag feedback; null when the drag ended or was cancelled.</summary>
    internal event Action<NdsNpcDragSession?>? DragChanged;
    internal NdsNpcDragSession? Drag => _drag;
    internal bool DragDeclined { get; private set; }
    internal (PlatinumEvent Npc, int X, int Y)? PendingMove => _pendingMove;
    internal NdsMapTool Tool
    {
        get => _tool;
        set { if (_tool == value) return; CancelDrag(); _tool = value; InvalidateVisual(); }
    }
    internal event Action<string>? Hover;
    internal int RenderedMarkers { get; private set; }
    internal static Brush LegendTerrain(PlatinumTerrainStatus status)
    {
        if (status == PlatinumTerrainStatus.Blocked) return new SolidColorBrush(Color.FromArgb(0xB8, 0xE0, 0x3A, 0x3A));
        if (status == PlatinumTerrainStatus.ClearOfCollision) return new SolidColorBrush(Color.FromArgb(0x60, 0x4F, 0xC9, 0xB5));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x80, 0x20, 0x24, 0x2C)), null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0xA0, 0xA0, 0xA8, 0xB4)), null, new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 2, 2)), new RectangleGeometry(new Rect(2, 2, 2, 2)) } }));
        return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 4), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 4, 4), ViewboxUnits = BrushMappingMode.Absolute };
    }
    internal static Brush LegendBrush(NdsBlockOwnership kind) => kind switch { NdsBlockOwnership.Explicit => ExplicitFill, NdsBlockOwnership.Contextual => ContextualFill, _ => OtherFill };
    internal static Brush LegendStroke => BlockPen.Brush;

    static NdsMapCanvasControl()
    {
        static Brush Hatch(Color color, bool forward)
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            var pen = new Pen(new SolidColorBrush(color), 1.4);
            group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(forward ? new Point(0, 8) : new Point(0, 0), forward ? new Point(8, 0) : new Point(8, 8))));
            var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 8, 8), ViewboxUnits = BrushMappingMode.Absolute };
            brush.Freeze(); return brush;
        }
        static SolidColorBrush Solid(string hex) { var b = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        static Pen MakePen(string hex, double width, DashStyle? dash = null) { var p = new Pen(Solid(hex), width); if (dash is not null) p.DashStyle = dash; p.Freeze(); return p; }
        ExplicitFill = Solid("#5586B7F3"); ContextualFill = Hatch(Color.FromArgb(0xAA, 0x86, 0xB7, 0xF3), true); OtherFill = Hatch(Color.FromArgb(0x88, 0x8A, 0x94, 0xA3), false);
        Surface = Solid("#1B2430"); Inspect = Solid("#FFFFFF");
        BlockPen = MakePen("#7A8CA5", 1); TilePen = MakePen("#2E3A4A", 1); MatrixPen = MakePen("#86B7F3", 2); ExplicitPen = MakePen("#86B7F3", 2.5);
        SelectionPen = MakePen("#FFFFFF", 2.5); HaloPen = MakePen("#86B7F3", 5); WarnPen = MakePen("#FFFFFF", 1.5, DashStyles.Dash);
    }

    internal NdsMapCanvasControl()
    {
        Focusable = true; ClipToBounds = true; SnapsToDevicePixels = true; FocusVisualStyle = null;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased); RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor); Cursor = Cursors.Arrow;
        AutomationProperties.SetName(this, "Eventos DS · mapa esquemático de eventos");
        AutomationProperties.SetHelpText(this, SchematicNotice + " Flechas: elegir el evento vecino. Ctrl+flechas: desplazar. + y −: zoom. F: ajustar a eventos. Re Pág y Av Pág: siguiente o anterior.");
        Unloaded += (_, _) => { if (IsMouseCaptured) ReleaseMouseCapture(); _pressed = _panning = false; };
    }

    internal void SetMap(PlatinumMapLayout layout, IEnumerable<PlatinumEvent> events, PlatinumTerrain? terrain = null, (int Width, int Height)? editorBounds = null, (PlatinumEventKind Kind, int Index)? selected = null, NdsMapLayers? layers = null)
    {
        CancelDrag(); _pendingMove = null;
        var previousLayers = layers ?? _model?.Layers ?? NdsMapLayers.All;
        _model = new NdsMapCanvasModel(layout, events, terrain, editorBounds) { Layers = previousLayers };
        _terrainBitmap = null;
        if (_model.TerrainPixels() is { } pixels)
        {
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(layout.TileWidth, layout.TileHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, layout.TileWidth * 4); bitmap.Freeze(); _terrainBitmap = bitmap;
        }
        if (selected is { } key) _model.Select(key.Kind, key.Index);
        if (ActualWidth > 0 && ActualHeight > 0) _model.SetViewport(ActualWidth, ActualHeight);
        Refresh();
    }
    internal void Clear() { CancelDrag(); _pendingMove = null; _model = null; Refresh(); }
    internal void SetLayers(NdsMapLayers layers) { if (_model is not null) _model.Layers = layers; Refresh(); }
    internal void SelectProgrammatic(PlatinumEvent? item, bool reveal)
    {
        if (_model is null) return;
        if (item is null) _model.ClearSelection(); else if (_model.Select(item.Kind, item.Index) && reveal && _model.SelectedMarker is { } marker) _model.EnsureVisible(marker);
        Refresh();
    }
    internal void ZoomCentered(double factor) { _model?.ZoomCentered(factor); Refresh(); }
    internal void FitEvents() { _model?.FitEvents(); Refresh(); }
    internal void FitMatrix() { _model?.FitMatrix(); Refresh(); }
    /// <summary>Redraws after the model was changed externally (used by tests and callers that move the view).</summary>
    internal void Invalidate() => Refresh();
    private void Refresh()
    {
        var selected = _model?.SelectedMarker;
        AutomationProperties.SetItemStatus(this, _model is null ? "Sin mapa" : $"{_model.Markers.Count} eventos; " + (selected is null ? "sin selección" : $"seleccionado {selected.Shape} {selected.Index} en X {selected.X} Y {selected.Y}"));
        InvalidateVisual(); ViewChanged?.Invoke();
    }

    protected override Size MeasureOverride(Size available) => new(double.IsInfinity(available.Width) ? 320 : available.Width, double.IsInfinity(available.Height) ? 240 : available.Height);
    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); _model?.SetViewport(info.NewSize.Width, info.NewSize.Height); Refresh(); }

    // ---- input ------------------------------------------------------------------------------------------------------
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_model is null) return; var p = e.GetPosition(this);
        _model.ZoomBy(Math.Pow(1.2, e.Delta / 120.0), p.X, p.Y); Refresh(); e.Handled = true;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); if (_model is null) return;
        var start = e.GetPosition(this);
        if (BeginDragAt(start)) { CaptureMouse(); e.Handled = true; return; }
        if (DragDeclined) { e.Handled = true; return; }
        _down = _last = start; _pressed = true; _panning = false; CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        // Middle button pans with any tool, so the map can always be moved while dragging NPCs is enabled.
        if (e.ChangedButton != MouseButton.Middle || _model is null || _drag is not null) return;
        Focus(); _middlePan = true; _last = e.GetPosition(this); Cursor = Cursors.SizeAll; CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton != MouseButton.Middle || !_middlePan) return;
        _middlePan = false; Cursor = Cursors.Arrow; if (IsMouseCaptured) ReleaseMouseCapture(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); _middlePan = false; if (_drag is not null) CancelDrag(); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_model is null) return; var p = e.GetPosition(this);
        if (_drag is not null) { DragTo(p); return; }
        if (_middlePan) { _model.PanBy(_last.X - p.X, _last.Y - p.Y); _last = p; Refresh(); return; }
        if (_pressed)
        {
            if (!_panning && (Math.Abs(p.X - _down.X) > 4 || Math.Abs(p.Y - _down.Y) > 4)) { _panning = true; Cursor = Cursors.SizeAll; }
            if (_panning) { _model.PanBy(_last.X - p.X, _last.Y - p.Y); _last = p; Refresh(); }
            return;
        }
        var tile = _model.TileAt(p.X, p.Y); var hit = _model.HitTest(p.X, p.Y);
        Cursor = hit is null ? Cursors.Arrow : Cursors.Hand;
        Hover?.Invoke(_model.Describe(tile.X, tile.Y) + (hit is null ? "" : $" · {hit.Event.Description} {hit.Index}"));
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag is not null) { var at = e.GetPosition(this); e.Handled = true; DropAt(at); if (IsMouseCaptured) ReleaseMouseCapture(); return; }
        if (!_pressed) return;
        bool click = !_panning; _pressed = _panning = false; if (IsMouseCaptured) ReleaseMouseCapture(); Cursor = Cursors.Arrow; e.Handled = true;
        if (click) ClickAt(e.GetPosition(this));
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drag is not null) { CancelDrag(); e.Handled = true; return; }
        if (_model is null) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0; bool handled = true;
        (int dx, int dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0) };
        if (dx != 0 || dy != 0) Navigate(dx, dy, ctrl);
        else if (e.Key is Key.PageDown) Pick(_model.Cycle(1));
        else if (e.Key is Key.PageUp) Pick(_model.Cycle(-1));
        else if (e.Key is Key.Add or Key.OemPlus) ZoomCentered(1.25);
        else if (e.Key is Key.Subtract or Key.OemMinus) ZoomCentered(1 / 1.25);
        else if (e.Key is Key.F) FitEvents();
        else if (e.Key is Key.Home) FitMatrix();
        else handled = false;
        e.Handled = handled;
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    // ---- NPC drag (no data is touched; a drop only reports a plan) -----------------------------------------------------
    /// <summary>Starts dragging the editable NPC under the point (Move tool only). Empty space or other events return false so the map can pan.</summary>
    internal bool BeginDragAt(Point point)
    {
        DragDeclined = false;
        if (_model is null || _tool != NdsMapTool.MoveNpc || _drag is not null) return false;
        var hit = _model.HitTest(point.X, point.Y);
        if (hit is null || !NdsNpcMovePlanner.IsDraggable(hit.Event)) return false;
        _drag = new NdsNpcDragSession(_model, hit.Event, point.X, point.Y); Cursor = Cursors.SizeAll; DragChanged?.Invoke(_drag); InvalidateVisual(); return true;
    }
    /// <summary>The guard (ConfirmLeave) runs once, when the drag really activates after the movement threshold; a click or an early cancel never reaches it.</summary>
    private bool ActivationAllowed(NdsNpcDragSession drag, bool wasActive)
    {
        if (wasActive || !drag.Active || DragGuard is null || DragGuard(drag.Npc)) return true;
        DragDeclined = true; CancelDrag(); return false;
    }
    internal void DragTo(Point point)
    {
        if (_drag is null) return;
        bool wasActive = _drag.Active;
        if (!_drag.Update(point.X, point.Y)) return;
        if (!ActivationAllowed(_drag, wasActive)) return;
        DragChanged?.Invoke(_drag); InvalidateVisual();
    }
    internal void DropAt(Point point)
    {
        var drag = _drag; if (drag is null || _model is null) return;
        bool wasActive = drag.Active; drag.Update(point.X, point.Y);
        if (!ActivationAllowed(drag, wasActive)) return;
        _drag = null; Cursor = Cursors.Arrow; DragChanged?.Invoke(null);
        // A click without moving keeps pending fields and previews: only a NPC that is not already selected is picked (normal selection rules).
        if (!drag.Active) { if (_model.Selected != (drag.Npc.Kind, drag.Npc.Index)) Pick(_model.Find(drag.Npc.Kind, drag.Npc.Index)); }
        else if (drag.Complete() is { } plan) MoveDropped?.Invoke(plan);
        InvalidateVisual();
    }
    internal void CancelDrag()
    {
        if (_drag is null) return;
        _drag = null; Cursor = Cursors.Arrow; DragChanged?.Invoke(null); if (IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual();
    }
    internal void SetPendingMove(PlatinumEvent npc, int x, int y) { _pendingMove = (npc, x, y); InvalidateVisual(); }
    internal void ClearPendingMove() { if (_pendingMove is null) return; _pendingMove = null; InvalidateVisual(); }

    /// <summary>Selects the marker under a point (what a click does). Empty space keeps the current selection.</summary>
    internal void ClickAt(Point point) { if (_model is not null) Pick(_model.HitTest(point.X, point.Y)); }
    /// <summary>Keyboard navigation: neighbour selection, or panning when <paramref name="pan"/> is set. Never edits events.</summary>
    internal void Navigate(int dx, int dy, bool pan)
    {
        if (_model is null) return;
        if (pan) { _model.PanBy(dx * 48, dy * 48); Refresh(); } else if (_model.Neighbor(dx, dy) is { } next) Pick(next);
    }
    private void Pick(NdsMapMarker? marker)
    {
        if (_model is null) return;
        if (marker is null) return;   // clicking empty space keeps the current selection: no accidental loss of the inspector
        _model.Select(marker.Kind, marker.Index); _model.EnsureVisible(marker); Refresh(); EventPicked?.Invoke(marker.Event);
    }

    // ---- drawing ----------------------------------------------------------------------------------------------------
    internal static Color ColorOf(NdsMarkerShape shape) => shape switch
    {
        NdsMarkerShape.Npc => Color.FromRgb(0xF4, 0xC9, 0x5D),
        NdsMarkerShape.GroundObject => Color.FromRgb(0x7F, 0xD6, 0xA4),
        NdsMarkerShape.BackgroundObject => Color.FromRgb(0xC3, 0xA6, 0xF2),
        NdsMarkerShape.Warp => Color.FromRgb(0xFF, 0x8F, 0x70),
        _ => Color.FromRgb(0x6C, 0xD2, 0xE8),
    };
    internal static Geometry ShapeGeometry(NdsMarkerShape shape, Point c, double r)
    {
        Geometry g = shape switch
        {
            NdsMarkerShape.Npc => new EllipseGeometry(c, r, r),
            NdsMarkerShape.BackgroundObject => new RectangleGeometry(new Rect(c.X - r * 0.85, c.Y - r * 0.85, r * 1.7, r * 1.7)),
            NdsMarkerShape.Warp => Polygon(new Point(c.X, c.Y - r * 1.15), new Point(c.X + r * 1.15, c.Y), new Point(c.X, c.Y + r * 1.15), new Point(c.X - r * 1.15, c.Y)),
            NdsMarkerShape.GroundObject => Polygon(new Point(c.X, c.Y - r), new Point(c.X + r * 1.05, c.Y + r * 0.85), new Point(c.X - r * 1.05, c.Y + r * 0.85)),
            _ => new RectangleGeometry(new Rect(c.X - r * 1.2, c.Y - r * 0.8, r * 2.4, r * 1.6)),
        };
        if (g.CanFreeze) g.Freeze(); return g;
    }
    private static Geometry Polygon(params Point[] points)
    {
        var figure = new PathFigure(points[0], points.Skip(1).Select(p => (PathSegment)new LineSegment(p, true)), true);
        return new PathGeometry([figure]);
    }
    private static Brush Themed(FrameworkElement owner, string key, Brush fallback) => owner.TryFindResource(key) as Brush ?? fallback;

    private FormattedText Text(string text, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, Face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; RenderedMarkers = 0;
        dc.DrawRectangle(Themed(this, "Bg", Surface), null, new Rect(0, 0, w, h));
        if (_model is null) { dc.DrawText(Text("Selecciona un mapa", 14, Themed(this, "TextSoft", Brushes.Gray)), new Point(12, 12)); return; }
        var m = _model; var textBrush = Themed(this, "TextMain", Inspect); var soft = Themed(this, "TextSoft", Brushes.LightGray);
        double z = m.Zoom, blockPx = 32 * z;
        var (x0, y0, x1, y1) = m.VisibleTiles();
        // Matrix surface
        var (mx, my) = m.TileToPixel(0, 0);
        dc.DrawRectangle(Themed(this, "ListSurface", Surface), null, new Rect(mx, my, m.Layout.TileWidth * z, m.Layout.TileHeight * z));
        int bx0 = Math.Max(0, (int)Math.Floor(x0 / 32.0)), by0 = Math.Max(0, (int)Math.Floor(y0 / 32.0));
        int bx1 = Math.Min(m.Layout.Width - 1, (int)Math.Floor(x1 / 32.0)), by1 = Math.Min(m.Layout.Height - 1, (int)Math.Floor(y1 / 32.0));
        bool ownership = (m.Layers & NdsMapLayers.Ownership) != 0, blocks = (m.Layers & NdsMapLayers.Blocks) != 0;
        for (int by = by0; by <= by1; by++)
            for (int bx = bx0; bx <= bx1; bx++)
            {
                var (px, py) = m.TileToPixel(bx * 32, by * 32); var rect = new Rect(px, py, blockPx, blockPx);
                var kind = m.Ownership(bx, by);
                if (ownership) dc.DrawRectangle(kind switch { NdsBlockOwnership.Explicit => ExplicitFill, NdsBlockOwnership.Contextual => ContextualFill, _ => OtherFill }, kind == NdsBlockOwnership.Explicit ? ExplicitPen : null, rect);
                if (blockPx >= 96 && blocks)
                {
                    string label = kind switch { NdsBlockOwnership.Explicit => $"({bx},{by}) · cabecera {m.Layout.MapId}", NdsBlockOwnership.Contextual => $"({bx},{by}) · contextual", _ => $"({bx},{by}) · otra: {m.Layout.Cell(bx, by).HeaderId}" };
                    dc.DrawText(Text(label, 11, soft), new Point(px + 4, py + 3));
                }
            }
        if ((m.Layers & NdsMapLayers.Terrain) != 0 && _terrainBitmap is not null && x1 >= x0 && y1 >= y0)
            DrawTerrain(dc, m, Math.Max(0, x0), Math.Max(0, y0), Math.Min(m.Layout.TileWidth - 1, x1), Math.Min(m.Layout.TileHeight - 1, y1));
        if ((m.Layers & NdsMapLayers.Tiles) != 0 && z >= 8)
            for (int tx = Math.Max(0, x0); tx <= Math.Min(m.Layout.TileWidth, x1 + 1); tx++)
            {
                var (lx, ly0) = m.TileToPixel(tx, 0); var (_, ly1) = m.TileToPixel(tx, m.Layout.TileHeight); dc.DrawLine(TilePen, new Point(lx, ly0), new Point(lx, ly1));
                if (tx > x1 + 1) break;
            }
        if ((m.Layers & NdsMapLayers.Tiles) != 0 && z >= 8)
            for (int ty = Math.Max(0, y0); ty <= Math.Min(m.Layout.TileHeight, y1 + 1); ty++)
            {
                var (lx0, ly) = m.TileToPixel(0, ty); var (lx1, _) = m.TileToPixel(m.Layout.TileWidth, ty); dc.DrawLine(TilePen, new Point(lx0, ly), new Point(lx1, ly));
            }
        if (blocks)
        {
            int stride = Math.Max(1, (int)Math.Ceiling(56 / Math.Max(1, blockPx)));
            for (int bx = bx0; bx <= Math.Min(m.Layout.Width, bx1 + 1); bx++)
            {
                var (lx, ly0) = m.TileToPixel(bx * 32, 0); var (_, ly1) = m.TileToPixel(bx * 32, m.Layout.TileHeight); dc.DrawLine(BlockPen, new Point(lx, ly0), new Point(lx, ly1));
                if (bx % stride == 0 && lx >= 0 && lx < w - 20) dc.DrawText(Text((bx * 32).ToString(CultureInfo.InvariantCulture), 10, soft), new Point(lx + 2, 0));
            }
            for (int by = by0; by <= Math.Min(m.Layout.Height, by1 + 1); by++)
            {
                var (lx0, ly) = m.TileToPixel(0, by * 32); var (lx1, _) = m.TileToPixel(m.Layout.TileWidth, by * 32); dc.DrawLine(BlockPen, new Point(lx0, ly), new Point(lx1, ly));
                if (by % stride == 0 && ly >= 12 && ly < h - 14) dc.DrawText(Text((by * 32).ToString(CultureInfo.InvariantCulture), 10, soft), new Point(2, ly + 1));
            }
        }
        dc.DrawRectangle(null, MatrixPen, new Rect(mx, my, m.Layout.TileWidth * z, m.Layout.TileHeight * z));
        // Markers: triggers first (underneath), then points. Off-screen markers are skipped.
        var drawn = new List<NdsMapMarker>();
        foreach (var marker in m.VisibleMarkers.OrderByDescending(k => k.Shape == NdsMarkerShape.Trigger).ThenBy(k => k.Shape))
        {
            var (px, py) = m.TileToPixel(marker.X, marker.Y);
            double ex = Math.Max(marker.Width * z, NdsMapCanvasModel.MinMarkerPixels), ey = Math.Max(marker.Height * z, NdsMapCanvasModel.MinMarkerPixels);
            if (px + ex < -20 || py + ey < -20 || px > w + 20 || py > h + 20) continue;
            DrawMarker(dc, m, marker, px, py); drawn.Add(marker); RenderedMarkers++;
            if (z >= 14 && marker.Shape != NdsMarkerShape.Trigger) { var c = m.TileToPixel(marker.CenterX, marker.CenterY); dc.DrawText(Text("#" + marker.Index, 10, textBrush), new Point(c.X + 9, c.Y - 18)); }
        }
        if (m.SelectedMarker is { } sel && m.IsLayerVisible(sel)) DrawSelection(dc, m, sel);
        if (_drag is { Active: true } drag) DrawGhost(dc, m, drag.Npc, drag.Plan.ToX, drag.Plan.ToY, drag.Plan.Outcome, "");
        else if (_pendingMove is { } pending) DrawGhost(dc, m, pending.Npc, pending.X, pending.Y, null, "propuesta pendiente");
        if (IsKeyboardFocused) dc.DrawRectangle(null, new Pen(Themed(this, "Accent", Inspect), 2), new Rect(1, 1, Math.Max(0, w - 2), Math.Max(0, h - 2)));
    }

    /// <summary>Terrain collision overlay: one image brush over the visible tile rectangle, plus x glyphs on blocked tiles when zoomed in.</summary>
    private void DrawTerrain(DrawingContext dc, NdsMapCanvasModel m, int tx0, int ty0, int tx1, int ty1)
    {
        if (tx1 < tx0 || ty1 < ty0) return;
        var (px, py) = m.TileToPixel(tx0, ty0); var (qx, qy) = m.TileToPixel(tx1 + 1, ty1 + 1);
        var brush = new ImageBrush(_terrainBitmap) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(tx0, ty0, tx1 - tx0 + 1, ty1 - ty0 + 1), Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        dc.DrawRectangle(brush, null, new Rect(px, py, qx - px, qy - py));
        if (m.Zoom < 10) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
            for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                    if (m.TerrainStatus(tx, ty) == PlatinumTerrainStatus.Blocked)
                    {
                        var (a, b) = m.TileToPixel(tx, ty); double i = m.Zoom * 0.22, s = m.Zoom;
                        ctx.BeginFigure(new Point(a + i, b + i), false, false); ctx.LineTo(new Point(a + s - i, b + s - i), true, false);
                        ctx.BeginFigure(new Point(a + s - i, b + i), false, false); ctx.LineTo(new Point(a + i, b + s - i), true, false);
                    }
        geometry.Freeze(); dc.DrawGeometry(null, GlyphPen, geometry);
    }

    private void DrawMarker(DrawingContext dc, NdsMapCanvasModel model, NdsMapMarker marker, double px, double py)
    {
        var color = ColorOf(marker.Shape); var fill = new SolidColorBrush(color); var edge = new Pen(Brushes.Black, 1.2);
        if (marker.Shape == NdsMarkerShape.Trigger)
        {
            double ex = Math.Max(marker.Width * model.Zoom, NdsMapCanvasModel.MinMarkerPixels), ey = Math.Max(marker.Height * model.Zoom, NdsMapCanvasModel.MinMarkerPixels);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)), new Pen(fill, 2) { DashStyle = DashStyles.Dash }, new Rect(px, py, ex, ey));
            return;
        }
        var c = model.TileToPixel(marker.CenterX, marker.CenterY); var centre = new Point(c.X, c.Y);
        double r = Math.Clamp(model.Zoom * 0.45, 6, 11);
        dc.DrawGeometry(fill, edge, ShapeGeometry(marker.Shape, centre, r));
        if (marker.Placement == NdsMarkerPlacement.OutsideOwnedBlocks) dc.DrawEllipse(null, WarnPen, centre, r + 5, r + 5);
        else if (marker.Placement == NdsMarkerPlacement.OutsideMatrix) { dc.DrawLine(WarnPen, new Point(c.X - r - 4, c.Y - r - 4), new Point(c.X + r + 4, c.Y + r + 4)); dc.DrawLine(WarnPen, new Point(c.X - r - 4, c.Y + r + 4), new Point(c.X + r + 4, c.Y - r - 4)); }
    }

    /// <summary>Original position (dashed ring) and ghost at the target tile. State = shape + colour: check, ?, cross; never "safe".</summary>
    private void DrawGhost(DrawingContext dc, NdsMapCanvasModel m, PlatinumEvent npc, int tx, int ty, NdsMoveOutcome? outcome, string note)
    {
        var accent = Themed(this, "Accent", Inspect);
        Color color = outcome switch { NdsMoveOutcome.Ready => Color.FromRgb(0x4F, 0xC9, 0xB5), NdsMoveOutcome.NeedsWarning => Color.FromRgb(0xF2, 0xB8, 0x4B), NdsMoveOutcome.Rejected => Color.FromRgb(0xFF, 0x5A, 0x5A), NdsMoveOutcome.NoChange => Color.FromRgb(0xA6, 0xB5, 0xC9), _ => Color.FromRgb(0x86, 0xB7, 0xF3) };
        var stroke = new SolidColorBrush(color); var dashed = outcome is NdsMoveOutcome.NeedsWarning or null;
        var pen = new Pen(stroke, outcome == NdsMoveOutcome.Rejected ? 3 : 2.5) { DashStyle = dashed ? DashStyles.Dash : DashStyles.Solid };
        var o = m.TileToPixel(npc.X + 0.5, npc.Y + 0.5); var t = m.TileToPixel(tx + 0.5, ty + 0.5); var origin = new Point(o.X, o.Y); var target = new Point(t.X, t.Y);
        double r = Math.Clamp(m.Zoom * 0.45, 6, 11);
        dc.DrawEllipse(null, new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dot }, origin, r + 6, r + 6);
        dc.DrawLine(new Pen(stroke, 1.5) { DashStyle = DashStyles.Dash }, origin, target);
        double side = Math.Max(m.Zoom, 18);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)), pen, new Rect(target.X - side / 2, target.Y - side / 2, side, side));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xA0, 0xF4, 0xC9, 0x5D)), new Pen(Brushes.Black, 1.2), target, r, r);
        var glyph = new Pen(stroke, 2.5); double g = r * 0.55;
        if (outcome == NdsMoveOutcome.Rejected) { dc.DrawLine(glyph, new Point(target.X - g, target.Y - g), new Point(target.X + g, target.Y + g)); dc.DrawLine(glyph, new Point(target.X + g, target.Y - g), new Point(target.X - g, target.Y + g)); }
        else if (outcome == NdsMoveOutcome.Ready) { var figure = new PathFigure(new Point(target.X - g, target.Y), [new LineSegment(new Point(target.X - g * 0.2, target.Y + g * 0.8), true), new LineSegment(new Point(target.X + g, target.Y - g * 0.7), true)], false); dc.DrawGeometry(null, glyph, new PathGeometry([figure])); }
        else if (outcome == NdsMoveOutcome.NeedsWarning) { var q = Text("?", 14, Brushes.Black); dc.DrawText(q, new Point(target.X - q.Width / 2, target.Y - q.Height / 2)); }
        string label = $"{npc.X},{npc.Y} → {tx},{ty}" + (note.Length > 0 ? " · " + note : "");
        var text = Text(label, 11.5, Brushes.White); var spot = new Point(Math.Clamp(target.X - text.Width / 2, 2, Math.Max(2, ActualWidth - text.Width - 6)), Math.Max(2, target.Y - side / 2 - text.Height - 4));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0x16, 0x1E)), null, new Rect(spot.X - 3, spot.Y - 1, text.Width + 6, text.Height + 2)); dc.DrawText(text, spot);
    }

    private static void DrawSelection(DrawingContext dc, NdsMapCanvasModel model, NdsMapMarker marker)
    {
        if (marker.Shape == NdsMarkerShape.Trigger)
        {
            var (px, py) = model.TileToPixel(marker.X, marker.Y);
            double ex = Math.Max(marker.Width * model.Zoom, NdsMapCanvasModel.MinMarkerPixels), ey = Math.Max(marker.Height * model.Zoom, NdsMapCanvasModel.MinMarkerPixels);
            dc.DrawRectangle(null, HaloPen, new Rect(px - 2, py - 2, ex + 4, ey + 4)); dc.DrawRectangle(null, SelectionPen, new Rect(px - 2, py - 2, ex + 4, ey + 4)); return;
        }
        var c = model.TileToPixel(marker.CenterX, marker.CenterY); double r = Math.Clamp(model.Zoom * 0.45, 6, 11) + 6;
        dc.DrawEllipse(null, HaloPen, new Point(c.X, c.Y), r, r); dc.DrawEllipse(null, SelectionPen, new Point(c.X, c.Y), r, r);
    }
}
