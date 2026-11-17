using System.Windows.Media;
using System.Windows.Media.Imaging;
using pk3DS.Core.NDS;
using pk3DS.Core.NDS.Nitro;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>
/// Top-down images of matrix blocks (terrain and props of land_data, with the textures of the header's area), rendered off the UI thread on request and
/// kept in a small least-recently-used cache. Nothing is written anywhere: the images exist only in memory, built from the user's ROM.
/// </summary>
internal sealed class NdsMapRenderCache
{
    /// <summary>Image resolution: 16 pixels per tile (one map unit per pixel), 512×512 per 32×32 block.</summary>
    internal const int PixelsPerTile = 16, BlockPixels = 32 * PixelsPerTile, Capacity = 48;
    private readonly PlatinumMapAssets _assets; private readonly PlatinumLandProps? _props;
    private readonly object _gate = new();
    private readonly Dictionary<(int Land, int Header), BitmapSource?> _done = new();
    private readonly LinkedList<(int, int)> _order = new();
    private readonly HashSet<(int, int)> _pending = new();
    private readonly Dictionary<(int, int), string> _problems = new();
    /// <summary>Raised (on a worker thread) when a requested block finished rendering.</summary>
    internal event Action? Rendered;

    internal NdsMapRenderCache(PlatinumMapAssets assets, PlatinumLandProps? props) { _assets = assets; _props = props; }

    /// <summary>The block image when ready; otherwise starts rendering it (once) and returns null. Empty cells (land 0xFFFF) have no image.</summary>
    internal BitmapSource? Get(int land, int header)
    {
        if (land == 0xFFFF || land >= _assets.LandCount || header < 0 || header >= _assets.HeaderCount) return null;
        var key = (land, header);
        lock (_gate)
        {
            if (_done.TryGetValue(key, out var image)) { _order.Remove(key); _order.AddFirst(key); return image; }
            if (!_pending.Add(key)) return null;
        }
        Task.Run(() =>
        {
            BitmapSource? result = null; string? problem = null;
            try { result = Render(land, header, out problem); }
            catch (Exception ex) when (ex is System.IO.InvalidDataException or ArgumentException) { problem = ex.Message; }
            lock (_gate)
            {
                _pending.Remove(key); _done[key] = result; _order.AddFirst(key); if (problem is not null) _problems[key] = problem;
                while (_order.Count > Capacity) { var last = _order.Last!.Value; _order.RemoveLast(); _done.Remove(last); }
            }
            Rendered?.Invoke();
        });
        return null;
    }
    /// <summary>Renders synchronously (used by the checks). The assets instance caches parsed files and is not thread-safe, so every use is serialized.</summary>
    internal BitmapSource? Render(int land, int header, out string? problem)
    {
        NitroTopDownRenderer renderer; IReadOnlyList<string> skipped;
        lock (_assets)
        {
            var scene = _assets.BlockScene(land, header, _props is not null && _props.Contains(land) ? _props.Props(land) : [], out skipped);
            renderer = new NitroTopDownRenderer(BlockPixels, BlockPixels, -BlockPixels / 2f, -BlockPixels / 2f, 1, 0);
            foreach (var placed in scene) renderer.Draw(placed);
        }
        problem = skipped.Count > 0 || renderer.Untextured > 0 ? string.Join("; ", skipped.Append(renderer.Untextured > 0 ? $"{renderer.Untextured} triángulos sin textura" : "").Where(s => s.Length > 0)) : null;
        var bitmap = BitmapSource.Create(BlockPixels, BlockPixels, 96, 96, PixelFormats.Bgra32, null, renderer.Pixels, BlockPixels * 4); bitmap.Freeze();
        return bitmap;
    }
    internal string? Problem(int land, int header) { lock (_gate) return _problems.GetValueOrDefault((land, header)); }
    internal int Cached { get { lock (_gate) return _done.Count; } }
    internal bool Busy { get { lock (_gate) return _pending.Count > 0; } }
}
