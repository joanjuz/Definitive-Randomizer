using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.NDS;
using pk3DS.Core.NDS.Nitro;
using ThreeDS.Progressive.Services.Nds;

/// <summary>
/// Render gallery on a private CPUE/1 copy (read-only): cities, interiors, terraces, caves and large maps. For each map: blocks, triangles, untextured
/// triangles, props the area does not load, render time, and an alignment measure — the tile shift (±2) that best overlaps prop pixels with the
/// collision tiles of land_data, which must be (0,0) when models and events share the same coordinates. Writes one PNG per map with the events on top.
/// </summary>
internal static class NdsMapRenderGallery
{
    internal sealed record Result(int Map, string Name, int Blocks, int Triangles, int Untextured, int Skipped, long Milliseconds, (int Dx, int Dz) BestShift, double Overlap, double PropOnClear, int PropTiles, double OverlapAtZero = 0);

    internal static IReadOnlyList<Result> Probe(string output, string source, IReadOnlyList<string>? prefixes = null)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "copia.nds"); File.Copy(source, copy); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        var results = new List<Result>();
        using (var project = NdsProject.Open(copy, false))
        {
            var data = project.Events.Data; var terrain = project.LoadTerrain().Terrain!; var props = project.LoadProps().Props; var assets = project.LoadMapAssets().Assets!;
            prefixes ??= ["T01", "C01", "C03", "C04", "C05", "C07", "C09", "T01R0101", "C01PC0101", "C03GYM0101", "C05R0101", "D05R0101", "D16", "R210A", "R209", "L01"];
            foreach (var prefix in prefixes)
            {
                var map = data.Maps.FirstOrDefault(m => m.InternalName == prefix) ?? data.Maps.FirstOrDefault(m => m.InternalName.StartsWith(prefix, StringComparison.Ordinal));
                if (map is null) { Console.WriteLine($"INFO no map {prefix}"); continue; }
                results.Add(Render(output, data, terrain, props, assets, map));
            }
        }
        File.Delete(copy);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash) throw new Exception("Source changed");
        return results;
    }

    private static Result Render(string output, INdsEventData data, PlatinumTerrain terrain, PlatinumLandProps? props, INdsMapGraphics assets, PlatinumMap map)
    {
        var layout = data.Layout(map.Id);
        var cells = layout.Cells.Where(c => c.HeaderId == map.Id && c.LandDataId != PlatinumTerrain.EmptyLandId).ToList();
        if (cells.Count == 0) cells = layout.Cells.Where(c => c.LandDataId != PlatinumTerrain.EmptyLandId).ToList();
        // A matrix without stored headers (most of Gen 5, whose overworld is 29×27 blocks): the blocks around the map's own events.
        var own = data.Events(map.Id); if (cells.Count > 40 && own.Count > 0)
        {
            int x0 = own.Min(e => e.X) / 32 - 1, x1 = own.Max(e => e.X) / 32 + 1, y0 = own.Min(e => e.Y) / 32 - 1, y1 = own.Max(e => e.Y) / 32 + 1;
            cells = cells.Where(c => c.BlockX >= x0 && c.BlockX <= x1 && c.BlockY >= y0 && c.BlockY <= y1).ToList();
        }
        int bx0 = cells.Min(c => c.BlockX), by0 = cells.Min(c => c.BlockY), bw = cells.Max(c => c.BlockX) - bx0 + 1, bh = cells.Max(c => c.BlockY) - by0 + 1;
        int ppt = bw * bh > 12 ? 4 : 8, block = 32 * ppt, width = bw * block, height = bh * block;
        var full = new uint[width * height]; var propMask = new bool[bw * 32, bh * 32]; var blocked = new bool[bw * 32, bh * 32]; var owned = new bool[bw * 32, bh * 32];
        int triangles = 0, untextured = 0, skipped = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var cell in cells)
        {
            var scene = assets.BlockScene(cell.LandDataId, cell.HeaderId, props is not null && props.Contains(cell.LandDataId) ? props.Props(cell.LandDataId) : [], out var skip); skipped += skip.Count;
            int terrainModels = assets.LandModel(cell.LandDataId).Count;
            var all = new NitroTopDownRenderer(block, block, -256, -256, ppt / 16f, 0xFF202830); var only = new NitroTopDownRenderer(32 * 4, 32 * 4, -256, -256, 4 / 16f, 0);
            foreach (var (placed, i) in scene.Select((s, i) => (s, i))) { all.Draw(placed); if (i >= terrainModels) only.Draw(placed); }
            triangles += all.Drawn; untextured += all.Untextured;
            int ox = (cell.BlockX - bx0) * block, oy = (cell.BlockY - by0) * block;
            for (int y = 0; y < block; y++) Array.Copy(all.Pixels, y * block, full, (oy + y) * width + ox, block);
            for (int tz = 0; tz < 32; tz++)
                for (int tx = 0; tx < 32; tx++)
                {
                    int gx = (cell.BlockX - bx0) * 32 + tx, gz = (cell.BlockY - by0) * 32 + tz, covered = 0;
                    for (int py = 0; py < 4; py++) for (int px = 0; px < 4; px++) if (only.Pixels[(tz * 4 + py) * 128 + tx * 4 + px] != 0) covered++;
                    propMask[gx, gz] = covered >= 8; owned[gx, gz] = true;
                    blocked[gx, gz] = terrain.Contains(cell.LandDataId) && (terrain.Attribute(cell.LandDataId, tx, tz) & PlatinumTerrain.CollisionMask) != 0;
                }
        }
        long ms = clock.ElapsedMilliseconds;
        // Alignment: overlap (intersection over prop tiles) between prop pixels and collision, for every shift up to ±2 tiles.
        int W = bw * 32, H = bh * 32, propTiles = 0; for (int x = 0; x < W; x++) for (int z = 0; z < H; z++) if (propMask[x, z]) propTiles++;
        double Score(int dx, int dz)
        {
            int hit = 0, total = 0;
            for (int x = 0; x < W; x++) for (int z = 0; z < H; z++)
            {
                if (!propMask[x, z]) continue; int sx = x + dx, sz = z + dz; if (sx < 0 || sz < 0 || sx >= W || sz >= H || !owned[sx, sz]) continue;
                total++; if (blocked[sx, sz]) hit++;
            }
            return total == 0 ? 0 : (double)hit / total;
        }
        var shifts = Enumerable.Range(-2, 5).SelectMany(dx => Enumerable.Range(-2, 5).Select(dz => (dx, dz))).Select(s => (s, score: Score(s.dx, s.dz))).OrderByDescending(s => s.score).ThenBy(s => Math.Abs(s.s.dx) + Math.Abs(s.s.dz)).ToList();
        int clearTiles = 0, propOnClear = 0; for (int x = 0; x < W; x++) for (int z = 0; z < H; z++) if (owned[x, z] && !blocked[x, z]) { clearTiles++; if (propMask[x, z]) propOnClear++; }
        // Events on top: NPC yellow, warps orange, background objects violet, triggers cyan outline.
        var overlay = (uint[])full.Clone();
        void Box(int tx, int tz, uint colour, bool filled)
        {
            int px0 = (tx - bx0 * 32) * ppt, pz0 = (tz - by0 * 32) * ppt;
            for (int y = 0; y < ppt; y++) for (int x = 0; x < ppt; x++)
            {
                int X = px0 + x, Y = pz0 + y; if (X < 0 || Y < 0 || X >= width || Y >= height) continue;
                if (filled ? (x > 0 && y > 0 && x < ppt - 1 && y < ppt - 1) : (x == 0 || y == 0 || x == ppt - 1 || y == ppt - 1)) overlay[Y * width + X] = colour;
            }
        }
        foreach (var e in data.Events(map.Id))
            switch (e.Kind)
            {
                case PlatinumEventKind.Npc: Box(e.X, e.Y, 0xFFF4C95D, true); break;
                case PlatinumEventKind.Warp: Box(e.X, e.Y, 0xFFFF5A2A, true); break;
                case PlatinumEventKind.Object: Box(e.X, e.Y, 0xFFC3A6F2, true); break;
                default: for (int dx = 0; dx < Math.Max(1, e.RangeX); dx++) for (int dz = 0; dz < Math.Max(1, e.RangeY); dz++) Box(e.X + dx, e.Y + dz, 0xFF6CD2E8, false); break;
            }
        string file = Path.Combine(output, $"{map.Id:D3}-{map.InternalName}");
        NdsMapRenderChecks.WritePng(file + ".png", width, height, full); NdsMapRenderChecks.WritePng(file + "-eventos.png", width, height, overlay);
        var best = shifts[0];
        return new(map.Id, $"{map.Name} ({map.InternalName})", cells.Count, triangles, untextured, skipped, ms, best.s, best.score, clearTiles == 0 ? 0 : (double)propOnClear / clearTiles, propTiles, Score(0, 0));
    }
}
