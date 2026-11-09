using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

internal enum NdsMarkerShape { Npc, GroundObject, BackgroundObject, Warp, Trigger }

[Flags]
internal enum NdsMapLayers { None = 0, Npc = 1, Objects = 2, Warps = 4, Triggers = 8, Blocks = 16, Tiles = 32, Ownership = 64, All = 127 }

/// <summary>How a 32×32 block relates to the selected header. Never implies geometry, collisions or walkability.</summary>
internal enum NdsBlockOwnership { Explicit, Contextual, Other }

internal enum NdsMarkerPlacement { InsideOwnedBlock, InsideContextualBlock, OutsideOwnedBlocks, OutsideMatrix }

/// <summary>A drawable view of one parsed event. The event record stays the single source of truth.</summary>
internal sealed record NdsMapMarker(PlatinumEvent Event, NdsMarkerShape Shape, int X, int Y, int Width, int Height, NdsMarkerPlacement Placement)
{
    internal PlatinumEventKind Kind => Event.Kind;
    internal int Index => Event.Index;
    internal NdsMapLayers Layer => Shape switch
    {
        NdsMarkerShape.Npc => NdsMapLayers.Npc,
        NdsMarkerShape.GroundObject or NdsMarkerShape.BackgroundObject => NdsMapLayers.Objects,
        NdsMarkerShape.Warp => NdsMapLayers.Warps,
        _ => NdsMapLayers.Triggers,
    };
    internal double CenterX => X + 0.5;
    internal double CenterY => Y + 0.5;
}

/// <summary>
/// Pure, read-only spatial state for the schematic DS event view (no WPF types). Event X/Y are tile coordinates
/// within the selected matrix (see PlatinumMapLayout); this model neither moves events nor infers geometry.
/// Screen position = tile × Zoom − Offset, in device-independent pixels.
/// </summary>
internal sealed class NdsMapCanvasModel
{
    internal const double MinZoom = 0.1, MaxZoom = 64, Margin = 24, MinMarkerPixels = 14;
    private readonly List<NdsMapMarker> _markers;
    private bool _positioned;

    internal PlatinumMapLayout Layout { get; }
    internal IReadOnlyList<NdsMapMarker> Markers => _markers;
    internal int WorldMinX { get; }
    internal int WorldMinY { get; }
    internal int WorldMaxX { get; }
    internal int WorldMaxY { get; }
    internal double Zoom { get; private set; } = 1;
    internal double OffsetX { get; private set; }
    internal double OffsetY { get; private set; }
    internal double ViewportWidth { get; private set; }
    internal double ViewportHeight { get; private set; }
    internal NdsMapLayers Layers { get; set; } = NdsMapLayers.All;
    internal (PlatinumEventKind Kind, int Index)? Selected { get; private set; }
    internal int OwnedBlockCount => Layout.OwnedCells.Count;
    internal int OutsideMaskCount => _markers.Count(m => m.Placement is NdsMarkerPlacement.OutsideOwnedBlocks or NdsMarkerPlacement.OutsideMatrix);

    internal NdsMapCanvasModel(PlatinumMapLayout layout, IEnumerable<PlatinumEvent> events)
    {
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _markers = events.Select(e => Build(layout, e)).ToList();
        int minX = 0, minY = 0, maxX = layout.TileWidth, maxY = layout.TileHeight;
        foreach (var m in _markers)
        {
            minX = Math.Min(minX, m.X); minY = Math.Min(minY, m.Y);
            maxX = Math.Max(maxX, m.X + m.Width); maxY = Math.Max(maxY, m.Y + m.Height);
        }
        WorldMinX = minX; WorldMinY = minY; WorldMaxX = maxX; WorldMaxY = maxY;
    }

    internal static NdsMapMarker Build(PlatinumMapLayout layout, PlatinumEvent e)
    {
        var shape = e.Kind switch
        {
            PlatinumEventKind.Npc when e.Type == 3 => NdsMarkerShape.GroundObject,
            PlatinumEventKind.Npc => NdsMarkerShape.Npc,
            PlatinumEventKind.Object => NdsMarkerShape.BackgroundObject,
            PlatinumEventKind.Warp => NdsMarkerShape.Warp,
            _ => NdsMarkerShape.Trigger,
        };
        // Trigger extent is the declared range fields (+6/+8); shown as such, not as validated geometry.
        int w = shape == NdsMarkerShape.Trigger ? Math.Max(1, e.RangeX) : 1, h = shape == NdsMarkerShape.Trigger ? Math.Max(1, e.RangeY) : 1;
        return new(e, shape, e.X, e.Y, w, h, Place(layout, e.X, e.Y));
    }

    internal static NdsMarkerPlacement Place(PlatinumMapLayout layout, int x, int y)
    {
        if (x < 0 || y < 0 || x >= layout.TileWidth || y >= layout.TileHeight) return NdsMarkerPlacement.OutsideMatrix;
        if (!layout.HasStoredHeaders) return NdsMarkerPlacement.InsideContextualBlock;
        return layout.Cell(x / PlatinumMapLayout.BlockSize, y / PlatinumMapLayout.BlockSize).HeaderId == layout.MapId
            ? NdsMarkerPlacement.InsideOwnedBlock : NdsMarkerPlacement.OutsideOwnedBlocks;
    }

    internal NdsBlockOwnership Ownership(int blockX, int blockY)
    {
        var cell = Layout.Cell(blockX, blockY);
        return !Layout.HasStoredHeaders ? NdsBlockOwnership.Contextual : cell.HeaderId == Layout.MapId ? NdsBlockOwnership.Explicit : NdsBlockOwnership.Other;
    }

    internal static string Describe(NdsMarkerPlacement placement) => placement switch
    {
        NdsMarkerPlacement.InsideOwnedBlock => "dentro de una celda asociada explícitamente a esta cabecera",
        NdsMarkerPlacement.InsideContextualBlock => "dentro de la matriz; sin cabeceras almacenadas (cabecera contextual)",
        NdsMarkerPlacement.OutsideOwnedBlocks => "fuera de la máscara de la cabecera: cae en una celda asignada a otra cabecera. Se muestra sin corregir",
        _ => "fuera de la matriz. Se muestra sin recortar ni corregir",
    };

    /// <summary>Text for a tile under the cursor; meaning of headers is taken only from stored matrix data.</summary>
    internal string Describe(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Layout.TileWidth || y >= Layout.TileHeight) return $"X {x} · Y {y} · fuera de la matriz";
        var at = Layout.Locate(x, y);
        string header = !Layout.HasStoredHeaders ? "cabecera contextual " + Layout.MapId + " (sin cabeceras almacenadas)"
            : at.Cell.HeaderId == Layout.MapId ? "cabecera " + at.Cell.HeaderId + " (explícita)" : "otra cabecera: " + at.Cell.HeaderId;
        return $"X {x} · Y {y} · bloque ({at.Cell.BlockX},{at.Cell.BlockY}) · local ({at.LocalX},{at.LocalY}) · {header}";
    }

    // ---- viewport -------------------------------------------------------------------------------------------------
    internal void SetViewport(double width, double height)
    {
        if (!(width > 0) || !(height > 0)) return;
        ViewportWidth = width; ViewportHeight = height;
        if (!_positioned) { FitEvents(); return; }
        ClampOffset();
    }

    internal (double X, double Y) TileToPixel(double tileX, double tileY) => (tileX * Zoom - OffsetX, tileY * Zoom - OffsetY);
    internal (double X, double Y) PixelToTile(double pixelX, double pixelY) => ((pixelX + OffsetX) / Zoom, (pixelY + OffsetY) / Zoom);
    internal (int X, int Y) TileAt(double pixelX, double pixelY)
    {
        var (x, y) = PixelToTile(pixelX, pixelY);
        return ((int)Math.Floor(x), (int)Math.Floor(y));
    }

    internal void SetZoom(double zoom, double anchorX, double anchorY)
    {
        double next = Math.Clamp(zoom, MinZoom, MaxZoom);
        var (tx, ty) = PixelToTile(anchorX, anchorY);
        Zoom = next; OffsetX = tx * next - anchorX; OffsetY = ty * next - anchorY; _positioned = true; ClampOffset();
    }
    internal void ZoomBy(double factor, double anchorX, double anchorY) => SetZoom(Zoom * factor, anchorX, anchorY);
    internal void ZoomCentered(double factor) => ZoomBy(factor, ViewportWidth / 2, ViewportHeight / 2);
    internal void PanBy(double dx, double dy) { OffsetX += dx; OffsetY += dy; _positioned = true; ClampOffset(); }

    private void ClampOffset()
    {
        OffsetX = ClampAxis(OffsetX, WorldMinX, WorldMaxX, ViewportWidth);
        OffsetY = ClampAxis(OffsetY, WorldMinY, WorldMaxY, ViewportHeight);
    }
    private double ClampAxis(double offset, int min, int max, double viewport)
    {
        double low = min * Zoom - Margin, high = max * Zoom - viewport + Margin;
        return high < low ? (min * Zoom + max * Zoom - viewport) / 2 : Math.Clamp(offset, low, high);
    }

    private void FitBox(double minX, double minY, double maxX, double maxY, double maxZoom)
    {
        double w = Math.Max(1, maxX - minX), h = Math.Max(1, maxY - minY);
        double zoom = Math.Min(Math.Max(1, ViewportWidth - 2 * Margin) / w, Math.Max(1, ViewportHeight - 2 * Margin) / h);
        Zoom = Math.Clamp(zoom, MinZoom, maxZoom);
        OffsetX = (minX + maxX) / 2 * Zoom - ViewportWidth / 2; OffsetY = (minY + maxY) / 2 * Zoom - ViewportHeight / 2;
        _positioned = true; ClampOffset();
    }
    /// <summary>Shows the whole matrix plus any event that falls outside of it.</summary>
    internal void FitMatrix() => FitBox(WorldMinX, WorldMinY, WorldMaxX, WorldMaxY, MaxZoom);
    /// <summary>Frames every event (padded); falls back to the whole matrix when the map has none.</summary>
    internal void FitEvents()
    {
        if (_markers.Count == 0) { FitMatrix(); return; }
        int minX = _markers.Min(m => m.X) - 3, minY = _markers.Min(m => m.Y) - 3, maxX = _markers.Max(m => m.X + m.Width) + 3, maxY = _markers.Max(m => m.Y + m.Height) + 3;
        FitBox(minX, minY, maxX, maxY, 24);
    }
    /// <summary>Scrolls the minimum amount needed to bring a marker into view; does not change zoom.</summary>
    internal void EnsureVisible(NdsMapMarker marker)
    {
        var (px, py) = TileToPixel(marker.CenterX, marker.CenterY); double pad = 40;
        double dx = px < pad ? px - pad : px > ViewportWidth - pad ? px - (ViewportWidth - pad) : 0;
        double dy = py < pad ? py - pad : py > ViewportHeight - pad ? py - (ViewportHeight - pad) : 0;
        if (dx != 0 || dy != 0) PanBy(dx, dy);
    }
    /// <summary>Inclusive tile range intersecting the viewport, clamped to the world rectangle (for culling).</summary>
    internal (int X0, int Y0, int X1, int Y1) VisibleTiles()
    {
        var a = PixelToTile(0, 0); var b = PixelToTile(ViewportWidth, ViewportHeight);
        return (Math.Max(WorldMinX, (int)Math.Floor(a.X)), Math.Max(WorldMinY, (int)Math.Floor(a.Y)),
                Math.Min(WorldMaxX - 1, (int)Math.Floor(b.X)), Math.Min(WorldMaxY - 1, (int)Math.Floor(b.Y)));
    }

    // ---- markers / selection --------------------------------------------------------------------------------------
    internal bool IsLayerVisible(NdsMapMarker marker) => (Layers & marker.Layer) != 0;
    internal IEnumerable<NdsMapMarker> VisibleMarkers => _markers.Where(IsLayerVisible);
    internal NdsMapMarker? Find(PlatinumEventKind kind, int index) => _markers.FirstOrDefault(m => m.Kind == kind && m.Index == index);
    internal NdsMapMarker? SelectedMarker => Selected is { } key ? Find(key.Kind, key.Index) : null;

    internal bool Select(PlatinumEventKind kind, int index)
    {
        if (Find(kind, index) is null) return false;
        Selected = (kind, index); return true;
    }
    internal void ClearSelection() => Selected = null;

    private double HalfSize(NdsMapMarker marker) => Math.Max(Zoom * 0.5, MinMarkerPixels / 2);

    /// <summary>Marker under a pixel. Point markers win (nearest centre); triggers are hit inside their declared rectangle.</summary>
    internal NdsMapMarker? HitTest(double pixelX, double pixelY)
    {
        NdsMapMarker? best = null; double bestDistance = double.MaxValue;
        foreach (var m in VisibleMarkers.Where(m => m.Shape != NdsMarkerShape.Trigger))
        {
            var (cx, cy) = TileToPixel(m.CenterX, m.CenterY); double half = HalfSize(m);
            if (Math.Abs(pixelX - cx) > half || Math.Abs(pixelY - cy) > half) continue;
            double d = (pixelX - cx) * (pixelX - cx) + (pixelY - cy) * (pixelY - cy);
            if (d < bestDistance - 1e-9 || Math.Abs(d - bestDistance) <= 1e-9 && best is not null && Priority(m) < Priority(best)) { best = m; bestDistance = d; }
        }
        if (best is not null) return best;
        double area = double.MaxValue;
        foreach (var m in VisibleMarkers.Where(m => m.Shape == NdsMarkerShape.Trigger))
        {
            var (x0, y0) = TileToPixel(m.X, m.Y); double w = Math.Max(m.Width * Zoom, MinMarkerPixels), h = Math.Max(m.Height * Zoom, MinMarkerPixels);
            if (pixelX < x0 || pixelX > x0 + w || pixelY < y0 || pixelY > y0 + h) continue;
            if (w * h < area) { best = m; area = w * h; }
        }
        return best;
    }
    private static int Priority(NdsMapMarker m) => ((int)m.Shape) * 100000 + m.Index;

    /// <summary>Spatial keyboard navigation: the nearest visible marker in a direction (dx, dy ∈ {−1,0,1}); never moves an event.</summary>
    internal NdsMapMarker? Neighbor(int dx, int dy)
    {
        var current = SelectedMarker;
        if (current is null) return Ordered().FirstOrDefault();
        NdsMapMarker? best = null; double bestScore = double.MaxValue;
        foreach (var m in VisibleMarkers.Where(m => m != current))
        {
            double rx = m.CenterX - current.CenterX, ry = m.CenterY - current.CenterY;
            double along = rx * dx + ry * dy, across = Math.Abs(rx * dy - ry * dx);
            if (along <= 0) continue;
            double score = along + 2 * across;
            if (score < bestScore - 1e-9 || Math.Abs(score - bestScore) <= 1e-9 && best is not null && Priority(m) < Priority(best)) { best = m; bestScore = score; }
        }
        return best;
    }
    private IEnumerable<NdsMapMarker> Ordered() => VisibleMarkers.OrderBy(Priority);
    /// <summary>Next (+1) or previous (−1) visible marker in a stable order (kind, index); wraps.</summary>
    internal NdsMapMarker? Cycle(int step)
    {
        var list = Ordered().ToList();
        if (list.Count == 0) return null;
        int at = list.FindIndex(m => Selected is { } s && m.Kind == s.Kind && m.Index == s.Index);
        return list[at < 0 ? (step > 0 ? 0 : list.Count - 1) : ((at + step) % list.Count + list.Count) % list.Count];
    }
}
