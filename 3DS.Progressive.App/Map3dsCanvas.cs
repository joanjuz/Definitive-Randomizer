using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pk3DS.Core.Maps;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Size = System.Windows.Size;

namespace ThreeDS.Progressive;

/// <summary>
/// Map canvas of the 3DS editor. It draws what a <see cref="IMapEditorBackend"/>
/// provides: per-block cell grids and the events of a zone. Coordinates are in
/// zone cells; the view transform is scale + offset.
/// </summary>
internal sealed class Map3dsCanvas : FrameworkElement
{
    internal const string SchematicNotice = "Vista esquemática: cada color es un valor distinto de la rejilla de permisos del bloque. No representa todavía los modelos, las texturas ni las alturas del juego.";
    private IMapResourceProvider? _resources;
    private MapLayout? _layout;
    private IReadOnlyList<MapEventView> _events = [];
    private readonly Dictionary<int, ImageSource?> _blockImages = [];
    private readonly Dictionary<(int Zone, int Block, int Size), ImageSource?> _graphics = [];
    private readonly HashSet<(int Zone, int Block, int Size)> _pending = [];
    private int _zone = -1;
    private int _generation;
    internal const string GraphicNotice = "Modelos y texturas reales vistos desde arriba. Vista aproximada: sin iluminación, transparencias ni animaciones; los eventos se dibujan encima.";
    /// <summary>Blocks whose graphic is being drawn; tests wait for it to reach zero.</summary>
    internal int PendingGraphics { get { lock (_pending) return _pending.Count; } }
    internal int GraphicsDrawn { get; private set; }
    internal int DiagnosticsDrawn { get; private set; }
    internal const string DiagnosticNotice = "Capa de diagnóstico: superficies de colisión transitables del juego, en colores planos por atributo. No son los gráficos del suelo, que todavía no se han localizado.";
    private double _scale = 4;
    private Vector _offset;
    private Point? _panFrom;
    private Vector _panStart;
    private bool _panned;

    internal MapEventView? Selected { get; private set; }
    private (MapEventView Npc, int X, int Y)? _pendingMove;
    internal void SetPendingMove((MapEventView Npc, int X, int Y)? move) { _pendingMove = move; InvalidateVisual(); }
    private static readonly Pen GhostPen = Freeze(new Pen(Brushes.White, 2) { DashStyle = DashStyles.Dash });
    private (MapEventView Event, double Facing, IReadOnlyList<(int X, int Y)> Cells, bool Pending)? _view;
    /// <summary>Facing arrow and sight cells of one character; <c>Pending</c> draws them as a preview.</summary>
    internal void SetView((MapEventView Event, double Facing, IReadOnlyList<(int X, int Y)> Cells, bool Pending)? view) { _view = view; InvalidateVisual(); }
    internal (MapEventView Event, double Facing, IReadOnlyList<(int X, int Y)> Cells, bool Pending)? ViewOverlay => _view;
    private static readonly Brush SightFill = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xF4, 0xC9, 0x5D)));
    private static readonly Pen SightEdge = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xF4, 0xC9, 0x5D)), 1));
    private static readonly Pen ArrowPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xF4, 0xC9, 0x5D)), 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    private (int X, int Y, double Facing)? _pendingNurse;
    /// <summary>Ghost of a nurse that would be added on a cell, with the direction it would face.</summary>
    internal void SetPendingNurse((int X, int Y, double Facing)? nurse) { _pendingNurse = nurse; InvalidateVisual(); }
    internal (int X, int Y, double Facing)? PendingNurse => _pendingNurse;
    /// <summary>While true, a click picks a cell (<see cref="CellPicked"/>) instead of selecting an event.</summary>
    internal bool PickingCell { get; set; }
    internal event Action<int, int>? CellPicked;
    private (IReadOnlyList<(int X, int Y)> Existing, IReadOnlyCollection<(int X, int Y)> Pending)? _catch;
    /// <summary>Cells that already produce encounters and cells chosen to get them.</summary>
    internal void SetCatch((IReadOnlyList<(int X, int Y)> Existing, IReadOnlyCollection<(int X, int Y)> Pending)? cells) { _catch = cells; InvalidateVisual(); }
    internal (IReadOnlyList<(int X, int Y)> Existing, IReadOnlyCollection<(int X, int Y)> Pending)? CatchOverlay => _catch;
    private static readonly Brush CatchExisting = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0x3F, 0xB9, 0x50)));
    private static readonly Pen CatchExistingEdge = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x2E, 0x8B, 0x3C)), 1));
    private static readonly Brush CatchPending = Freeze(new SolidColorBrush(Color.FromArgb(0xAA, 0xB8, 0xF2, 0x4A)));
    /// <summary>Picks cell (x, y) as a click would while <see cref="PickingCell"/> is on.</summary>
    internal void PickCell(int x, int y) { if (PickingCell) CellPicked?.Invoke(x, y); }
    internal event Action<MapEventView?>? EventPicked;
    internal event Action<string>? Hover;
    internal Map3dsLayers Layers { get; private set; } = Map3dsLayers.All;
    internal int RenderedMarkers { get; private set; }
    internal double Scale => _scale;

    private static readonly Pen BlockPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x7A, 0x8C, 0xA5)), 1));
    private static readonly Pen SelectionPen = Freeze(new Pen(Brushes.White, 2.5));
    private static readonly Pen HaloPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x86, 0xB7, 0xF3)), 5));
    private static readonly Pen MarkerEdge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x10, 0x16, 0x1E)), 1.2));
    private static readonly Brush Surface = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30)));
    private static readonly Brush EmptyBlock = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x1B, 0x24)));

    internal Map3dsCanvas()
    {
        Focusable = true; ClipToBounds = true; SnapsToDevicePixels = true; FocusVisualStyle = null;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    internal static Color KindColor(MapEventKind kind) => kind switch
    {
        MapEventKind.Npc => Color.FromRgb(0xF4, 0xC9, 0x5D),
        MapEventKind.Prop => Color.FromRgb(0xC3, 0xA6, 0xF2),
        MapEventKind.Warp => Color.FromRgb(0xFF, 0x8F, 0x70),
        MapEventKind.Trigger => Color.FromRgb(0x6C, 0xD2, 0xE8),
        _ => Color.FromRgb(0x4F, 0xC9, 0xB5),
    };

    internal void SetZone(IMapResourceProvider resources, MapLayout layout, IReadOnlyList<MapEventView> events, int zone = -1)
    {
        if (!ReferenceEquals(resources, _resources)) { _blockImages.Clear(); lock (_pending) _graphics.Clear(); }
        if (zone != _zone) _generation++;
        _resources = resources; _layout = layout; _events = events; Selected = null; _zone = zone; _view = null; _pendingNurse = null; _catch = null;
        Fit();
    }

    internal void Clear() { _layout = null; _events = []; Selected = null; _view = null; InvalidateVisual(); }

    internal void SetLayers(Map3dsLayers layers) { Layers = layers; InvalidateVisual(); }

    internal void Select(MapEventView? item, bool reveal)
    {
        Selected = item;
        if (reveal && item is { Placed: true } && ActualWidth > 0)
        {
            var c = new Point(item.X + (item.Width / 2), item.Y + (item.Height / 2));
            var s = ToScreen(c);
            if (s.X < 0 || s.Y < 0 || s.X > ActualWidth || s.Y > ActualHeight)
                _offset = new Vector((ActualWidth / 2) - (c.X * _scale), (ActualHeight / 2) - (c.Y * _scale));
        }
        InvalidateVisual();
    }

    /// <summary>Fits the blocks that exist (not the empty matrix cells) in the view.</summary>
    internal void Fit()
    {
        if (_layout is null) { InvalidateVisual(); return; }
        int minC = int.MaxValue, minR = int.MaxValue, maxC = -1, maxR = -1;
        for (int r = 0; r < _layout.Height; r++)
            for (int c = 0; c < _layout.Width; c++)
                if (_layout.BlockAt(c, r) >= 0) { minC = Math.Min(minC, c); minR = Math.Min(minR, r); maxC = Math.Max(maxC, c); maxR = Math.Max(maxR, r); }
        if (maxC < 0) { minC = minR = 0; maxC = _layout.Width - 1; maxR = _layout.Height - 1; }
        double w = (maxC - minC + 1) * _layout.BlockCells, h = (maxR - minR + 1) * _layout.BlockCells;
        double vw = ActualWidth > 0 ? ActualWidth : 800, vh = ActualHeight > 0 ? ActualHeight : 600;
        _scale = Math.Clamp(Math.Min(vw / w, vh / h) * 0.95, 0.25, 32);
        _offset = new Vector(((vw - (w * _scale)) / 2) - (minC * _layout.BlockCells * _scale), ((vh - (h * _scale)) / 2) - (minR * _layout.BlockCells * _scale));
        InvalidateVisual();
    }

    internal void ZoomAt(Point screen, double factor)
    {
        double next = Math.Clamp(_scale * factor, 0.25, 48);
        var cell = ToCell(screen);
        _scale = next;
        _offset = new Vector(screen.X - (cell.X * _scale), screen.Y - (cell.Y * _scale));
        InvalidateVisual();
    }

    internal void ZoomCentered(double factor) => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor);

    internal Point ToScreen(Point cell) => new((cell.X * _scale) + _offset.X, (cell.Y * _scale) + _offset.Y);
    internal Point ToCell(Point screen) => new((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);

    private bool Visible(MapEventView e) => e.Placed && Layers.Shows(e.Kind);

    internal MapEventView? HitTest(Point screen)
    {
        var cell = ToCell(screen);
        // Smallest area first, so a point event wins over the trigger under it.
        return _events.Where(Visible)
            .Where(e => cell.X >= e.X && cell.X < e.X + e.Width && cell.Y >= e.Y && cell.Y < e.Y + e.Height)
            .OrderBy(e => e.Width * e.Height).ThenBy(e => e.Kind).FirstOrDefault();
    }

    private ImageSource? BlockImage(int block)
    {
        if (_resources is null || block < 0) return null;
        if (_blockImages.TryGetValue(block, out var img)) return img;
        var grid = _resources.Cells(block);
        if (grid is null) return _blockImages[block] = null;
        var px = new uint[grid.Width * grid.Height];
        for (int i = 0; i < px.Length; i++) px[i] = CellColor(grid.Cells[i]);
        var bmp = BitmapSource.Create(grid.Width, grid.Height, 96, 96, PixelFormats.Bgra32, null, px, grid.Width * 4);
        bmp.Freeze();
        return _blockImages[block] = bmp;
    }

    /// <summary>Stable muted colour per permission value; 0x01000021 (the most common blocking value) is dark.</summary>
    internal static uint CellColor(uint value)
    {
        if (value == 0x01000021u) return 0xFF0E1218u;
        uint h = value * 2654435761u;
        h ^= h >> 15;
        byte r = (byte)(0x40 + ((h >> 0) & 0x5F)), g = (byte)(0x50 + ((h >> 8) & 0x5F)), b = (byte)(0x48 + ((h >> 16) & 0x5F));
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    /// <summary>Level of detail: block side in pixels, a power of two between 64 and 512.</summary>
    internal static int DetailFor(double onScreen)
    {
        int size = 64;
        while (size < 512 && size < onScreen) size *= 2;
        return size;
    }

    /// <summary>
    /// Image of a block from the game models. Returns the best cached size and,
    /// when the wanted size is missing, draws it on a worker thread.
    /// </summary>
    private ImageSource? Graphic(int block, double onScreen, bool diagnostic = false)
    {
        if (_resources is null || _zone < 0) return null;
        int want = DetailFor(onScreen);
        int slot = diagnostic ? -1 - block : block; // diagnostic images share the cache under negative keys
        var key = (_zone, slot, want);
        ImageSource? best = null;
        lock (_pending)
        {
            if (_graphics.TryGetValue(key, out var exact)) return exact;
            foreach (int s in new[] { 512, 256, 128, 64 })
                if (_graphics.TryGetValue((_zone, slot, s), out var img) && img is not null) { best = img; break; }
            if (!_pending.Add(key)) return best;
        }
        var resources = _resources; int zone = _zone, generation = _generation;
        Task.Run(() =>
        {
            ImageSource? result = null;
            try
            {
                var g = diagnostic ? resources.Diagnostic(zone, block, want) : resources.Graphic(zone, block, want);
                if (g is not null)
                {
                    var bmp = BitmapSource.Create(g.Size, g.Size, 96, 96, PixelFormats.Bgra32, null, g.Pixels, g.Size * 4);
                    bmp.Freeze(); result = bmp;
                }
            }
            catch (Exception ex) when (ex is pk3DS.Core.Maps.MapFormatException or InvalidOperationException) { result = null; }
            Dispatcher.BeginInvoke(() =>
            {
                lock (_pending)
                {
                    _pending.Remove(key);
                    if (generation != _generation) return;
                    _graphics[key] = result;
                    if (_graphics.Count > 400) foreach (var old in _graphics.Keys.Where(k => k.Zone != zone).Take(200).ToList()) _graphics.Remove(old);
                }
                InvalidateVisual();
            });
        });
        return best;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Surface, null, new Rect(0, 0, ActualWidth, ActualHeight));
        RenderedMarkers = 0; GraphicsDrawn = 0; DiagnosticsDrawn = 0;
        if (_layout is null) return;
        double size = _layout.BlockCells * _scale;
        for (int r = 0; r < _layout.Height; r++)
            for (int c = 0; c < _layout.Width; c++)
            {
                int block = _layout.BlockAt(c, r);
                var rect = new Rect(ToScreen(new Point(c * _layout.BlockCells, r * _layout.BlockCells)), new Size(size, size));
                if (rect.Right < 0 || rect.Bottom < 0 || rect.Left > ActualWidth || rect.Top > ActualHeight) continue;
                if (block < 0) { dc.DrawRectangle(EmptyBlock, null, rect); continue; }
                bool drawn = false;
                if (Layers.Diagnostic && Graphic(block, rect.Width, diagnostic: true) is { } diag) { dc.DrawRectangle(EmptyBlock, null, rect); dc.DrawImage(diag, rect); DiagnosticsDrawn++; drawn = true; }
                if (Layers.Graphics && Graphic(block, rect.Width) is { } gfx) { if (!drawn) dc.DrawRectangle(EmptyBlock, null, rect); dc.DrawImage(gfx, rect); GraphicsDrawn++; }
                else if (!drawn && Layers.Cells && BlockImage(block) is { } img) dc.DrawImage(img, rect);
                else if (!drawn) dc.DrawRectangle(EmptyBlock, null, rect);
                if (Layers.Blocks) dc.DrawRectangle(null, BlockPen, rect);
            }
        foreach (var e in _events.Where(Visible).OrderByDescending(e => e.Width * e.Height))
        {
            var rect = new Rect(ToScreen(new Point(e.X, e.Y)), new Size(e.Width * _scale, e.Height * _scale));
            if (rect.Right < 0 || rect.Bottom < 0 || rect.Left > ActualWidth || rect.Top > ActualHeight) continue;
            var color = KindColor(e.Kind);
            bool area = e.Width * e.Height > 1 || e.Kind is MapEventKind.Trigger or MapEventKind.ZoneTrigger;
            var fill = new SolidColorBrush(area ? Color.FromArgb(0x66, color.R, color.G, color.B) : color);
            if (e.Kind == MapEventKind.Npc)
            {
                double rad = Math.Max(3, _scale * 0.45);
                dc.DrawEllipse(fill, MarkerEdge, new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)), rad, rad);
            }
            else
            {
                var inset = area || _scale < 6 ? rect : Rect.Inflate(rect, -_scale * 0.1, -_scale * 0.1);
                dc.DrawRectangle(fill, new Pen(new SolidColorBrush(color), area ? 1.5 : 1), inset);
            }
            RenderedMarkers++;
        }
        if (_pendingMove is { } mv)
        {
            var from = ToScreen(new Point(mv.Npc.X + 0.5, mv.Npc.Y + 0.5));
            var to = ToScreen(new Point(mv.X + 0.5, mv.Y + 0.5));
            dc.DrawLine(GhostPen, from, to);
            double rad = Math.Max(4, _scale * 0.5);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x99, 0xF4, 0xC9, 0x5D)), GhostPen, to, rad, rad);
        }
        if (_catch is { } zoneCells)
        {
            var catchSize = new Size(_scale, _scale);
            foreach (var (cx, cy) in zoneCells.Existing) dc.DrawRectangle(CatchExisting, CatchExistingEdge, new Rect(ToScreen(new Point(cx, cy)), catchSize));
            foreach (var (cx, cy) in zoneCells.Pending) dc.DrawRectangle(CatchPending, GhostPen, new Rect(ToScreen(new Point(cx, cy)), catchSize));
        }
        if (_pendingNurse is { } nurse)
        {
            var c = ToScreen(new Point(nurse.X + 0.5, nurse.Y + 0.5));
            double rad = Math.Max(5, _scale * 0.55), arm = rad * 0.55, len = Math.Max(14, _scale * 1.4), a = nurse.Facing * Math.PI / 180;
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xCC, 0xF2, 0x8C, 0xB8)), GhostPen, c, rad, rad);
            var cross = Freeze(new Pen(Brushes.White, Math.Max(1.5, rad * 0.3)));
            dc.DrawLine(cross, new Point(c.X - arm, c.Y), new Point(c.X + arm, c.Y));
            dc.DrawLine(cross, new Point(c.X, c.Y - arm), new Point(c.X, c.Y + arm));
            dc.DrawLine(GhostPen, new Point(c.X + (Math.Sin(a) * rad), c.Y - (Math.Cos(a) * rad)), new Point(c.X + (Math.Sin(a) * len), c.Y - (Math.Cos(a) * len)));
        }
        if (_view is { } v && v.Event.Placed && Layers.Shows(v.Event.Kind))
        {
            foreach (var (cx, cy) in v.Cells)
                dc.DrawRectangle(SightFill, SightEdge, new Rect(ToScreen(new Point(cx, cy)), new Size(_scale, _scale)));
            if (double.IsFinite(v.Facing))
            {
                double rad = v.Facing * Math.PI / 180, len = Math.Max(14, _scale * 1.4);
                var c = ToScreen(new Point(v.Event.X + 0.5, v.Event.Y + 0.5));
                var tip = new Point(c.X + (Math.Sin(rad) * len), c.Y - (Math.Cos(rad) * len));
                var pen = v.Pending ? GhostPen : ArrowPen;
                dc.DrawLine(pen, c, tip);
                foreach (double side in new[] { -2.6, 2.6 })
                    dc.DrawLine(pen, tip, new Point(tip.X - (Math.Sin(rad + side) * len * -0.35), tip.Y + (Math.Cos(rad + side) * len * -0.35)));
            }
        }
        if (Selected is { Placed: true } s && Layers.Shows(s.Kind))
        {
            var rect = new Rect(ToScreen(new Point(s.X, s.Y)), new Size(s.Width * _scale, s.Height * _scale));
            rect.Inflate(3, 3);
            dc.DrawRectangle(null, HaloPen, rect);
            dc.DrawRectangle(null, SelectionPen, rect);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.PreviousSize.Width <= 0 && _layout is not null) Fit();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.2 : 1 / 1.2);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        _panFrom = e.GetPosition(this); _panStart = _offset; _panned = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_panFrom is { } from && e.LeftButton == MouseButtonState.Pressed)
        {
            var d = p - from;
            if (_panned || d.Length > 4) { _panned = true; _offset = _panStart + d; InvalidateVisual(); }
            return;
        }
        var cell = ToCell(p);
        var hit = HitTest(p);
        Hover?.Invoke($"Casilla {Math.Floor(cell.X):0}, {Math.Floor(cell.Y):0}" + (hit is null ? "" : " · " + hit.Label));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (_panFrom is not null && !_panned && PickingCell)
        {
            var cell = ToCell(e.GetPosition(this));
            PickCell((int)Math.Floor(cell.X), (int)Math.Floor(cell.Y));
        }
        else if (_panFrom is not null && !_panned)
        {
            var hit = HitTest(e.GetPosition(this));
            Select(hit, false);
            EventPicked?.Invoke(hit);
        }
        _panFrom = null;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        const double step = 40;
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus: ZoomCentered(1.25); break;
            case Key.Subtract or Key.OemMinus: ZoomCentered(1 / 1.25); break;
            case Key.Left: _offset.X += step; InvalidateVisual(); break;
            case Key.Right: _offset.X -= step; InvalidateVisual(); break;
            case Key.Up: _offset.Y += step; InvalidateVisual(); break;
            case Key.Down: _offset.Y -= step; InvalidateVisual(); break;
            case Key.Home: Fit(); break;
            default: return;
        }
        e.Handled = true;
    }
}

internal sealed record Map3dsLayers(bool Cells, bool Blocks, bool Npcs, bool Props, bool Warps, bool Triggers, bool Graphics = false, bool Diagnostic = false)
{
    internal static readonly Map3dsLayers All = new(true, true, true, true, true, true);
    internal bool Shows(MapEventKind kind) => kind switch
    {
        MapEventKind.Npc => Npcs,
        MapEventKind.Prop => Props,
        MapEventKind.Warp => Warps,
        _ => Triggers,
    };
}
