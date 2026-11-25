using System.Windows.Media;
using System.Windows.Media.Imaging;
using pk3DS.Core.NDS;
using pk3DS.Core.NDS.Nitro;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>
/// Top-down images of matrix blocks (terrain and props of land_data, with the textures of the header's area). Each block is rendered at the power-of-two
/// size closest above its size on screen (32–512 pixels), by one background worker that serves the most recent requests first, and kept in a cache
/// bounded by memory rather than by count, so everything visible at once always fits (a whole matrix zoomed out uses small images). While a sharper
/// image is being rendered, the best one already available is returned. Nothing is written anywhere: the images exist only in memory.
/// </summary>
internal sealed class NdsMapRenderCache
{
    /// <summary>Largest image: 16 pixels per tile (one map unit per pixel), 512×512 per 32×32 block. Smallest: 1 pixel per tile.</summary>
    internal const int PixelsPerTile = 16, BlockPixels = 32 * PixelsPerTile, MinimumPixels = 32, BudgetBytes = 96 * 1024 * 1024, MaximumQueue = 1024;
    private readonly INdsMapGraphics _assets; private readonly PlatinumLandProps? _props;
    private readonly object _gate = new();
    private readonly Dictionary<(int Land, int Header, int Size), BitmapSource?> _done = new();
    private readonly LinkedList<(int, int, int)> _order = new();
    private readonly List<(int, int, int)> _queue = new();
    private readonly HashSet<(int, int, int)> _pending = new();
    private readonly Dictionary<(int, int), string> _problems = new();
    private long _bytes; private bool _working; private int _renderCount;
    /// <summary>Raised (on the worker thread) when a requested block finished rendering.</summary>
    internal event Action? Rendered;

    internal NdsMapRenderCache(INdsMapGraphics assets, PlatinumLandProps? props) { _assets = assets; _props = props; }

    /// <summary>The image size used for a block drawn <paramref name="screenPixels"/> wide: the next power of two, between 32 and 512.</summary>
    internal static int SizeFor(double screenPixels)
    {
        int size = MinimumPixels; while (size < screenPixels && size < BlockPixels) size *= 2; return size;
    }

    /// <summary>
    /// The sharpest image available for the block (at least the requested size when ready); starts rendering the requested size once when it is missing.
    /// Null while nothing is available yet. Empty cells (land 0xFFFF) and unknown headers have no image.
    /// </summary>
    /// <summary>The image of a block drawn by the first candidate header whose area provides all its terrain textures (see <see cref="ResolveHeader"/>).</summary>
    internal BitmapSource? Get(int land, IReadOnlyList<int> candidates, double screenPixels = BlockPixels) =>
        candidates.Count == 0 ? null : Get(land, candidates.Count == 1 ? candidates[0] : ResolveHeader(land, candidates), screenPixels);
    private readonly Dictionary<(int, int), int> _resolved = new();
    /// <summary>Memoized choice among tied headers; checking texture bindings parses the block's model once (it is reused by the render).</summary>
    internal int ResolveHeader(int land, IReadOnlyList<int> candidates)
    {
        if (land == 0xFFFF || land >= _assets.LandCount) return candidates[0];
        lock (_gate) if (_resolved.TryGetValue((land, candidates[0]), out int known)) return known;
        int header;
        try { lock (_assets) header = _assets.FirstBindingHeader(land, candidates.Where(h => h >= 0 && h < _assets.HeaderCount).ToList()); }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or ArgumentException) { header = candidates[0]; }
        lock (_gate) _resolved[(land, candidates[0])] = header;
        return header;
    }
    internal BitmapSource? Get(int land, int header, double screenPixels = BlockPixels)
    {
        if (land == 0xFFFF || land >= _assets.LandCount || header < 0 || header >= _assets.HeaderCount) return null;
        int size = SizeFor(screenPixels); var key = (land, header, size); BitmapSource? fallback = null; bool start = false;
        lock (_gate)
        {
            for (int s = BlockPixels; s >= MinimumPixels; s /= 2)
            {
                if (!_done.TryGetValue((land, header, s), out var image)) continue;
                Touch((land, header, s));
                if (s >= size) return image;
                fallback ??= image;
            }
            if (_pending.Add(key)) { _queue.Add(key); if (_queue.Count > MaximumQueue) { _pending.Remove(_queue[0]); _queue.RemoveAt(0); } }
            else { _queue.Remove(key); _queue.Add(key); }       // asked again: the most recent requests go first
            if (!_working) { _working = true; start = true; }
        }
        if (start) Task.Run(Work);
        return fallback;
    }
    private void Touch((int, int, int) key) { _order.Remove(key); _order.AddFirst(key); }

    private void Work()
    {
        while (true)
        {
            (int Land, int Header, int Size) key;
            lock (_gate)
            {
                if (_queue.Count == 0) { _working = false; return; }
                key = _queue[^1]; _queue.RemoveAt(_queue.Count - 1);
            }
            BitmapSource? result = null; string? problem = null;
            try { result = Render(key.Land, key.Header, out problem, key.Size); }
            catch (Exception ex) when (ex is System.IO.InvalidDataException or ArgumentException) { problem = ex.Message; }
            lock (_gate)
            {
                _pending.Remove(key); _done[key] = result; Touch(key); _bytes += (long)key.Size * key.Size * 4;
                if (problem is not null) _problems[(key.Land, key.Header)] = problem;
                while (_bytes > BudgetBytes && _order.Count > 1)
                {
                    var last = _order.Last!.Value; _order.RemoveLast(); _done.Remove(last); _bytes -= (long)last.Item3 * last.Item3 * 4;
                }
            }
            Interlocked.Increment(ref _renderCount); Rendered?.Invoke();
        }
    }

    /// <summary>Renders synchronously (also used by the checks). The assets instance caches parsed files and is not thread-safe, so every use is serialized.</summary>
    internal BitmapSource? Render(int land, int header, out string? problem, int size = BlockPixels)
    {
        NitroTopDownRenderer renderer; IReadOnlyList<string> skipped;
        // Small images (far away) are rendered larger and box-filtered — 4× up to 64 px, 2× at 128 px — so textures average out instead of aliasing into noise.
        int factor = size <= 64 ? 4 : size <= 128 ? 2 : 1, work = size * factor;
        lock (_assets)
        {
            var scene = _assets.BlockScene(land, header, _props is not null && _props.Contains(land) ? _props.Props(land) : [], out skipped);
            // Gen 5 only: translucent texels (tree shadows) are mixed after the opaque ones; Gen 4 images stay exactly as they were.
            renderer = new NitroTopDownRenderer(work, work, -BlockPixels / 2f, -BlockPixels / 2f, work / (float)BlockPixels, 0) { BlendTranslucentTexels = _assets is Gen5MapGraphics };
            renderer.DrawScene(scene);
        }
        problem = skipped.Count > 0 || renderer.Untextured > 0 ? string.Join("; ", skipped.Append(renderer.Untextured > 0 ? $"{renderer.Untextured} triángulos sin textura" : "").Where(s => s.Length > 0)) : null;
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, NitroTopDownRenderer.Downsample(renderer.Pixels, work, factor), size * 4); bitmap.Freeze();
        return bitmap;
    }
    internal string? Problem(int land, int header) { lock (_gate) return _problems.GetValueOrDefault((land, header)); }
    /// <summary>Blocks rendered since creation (a block rendered again after being evicted counts again).</summary>
    internal int RenderCount => Volatile.Read(ref _renderCount);
    internal int Cached { get { lock (_gate) return _done.Count; } }
    internal long CachedBytes { get { lock (_gate) return _bytes; } }
    internal bool Busy { get { lock (_gate) return _working || _queue.Count > 0; } }
}
