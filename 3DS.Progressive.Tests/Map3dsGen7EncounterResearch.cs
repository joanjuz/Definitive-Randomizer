using pk3DS.Core.Maps.Ctr;
using System.Numerics;
using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive.Services;

/// <summary>
/// Read-only research of USUM wild encounters: per area, the encounter tables (slot 9 of the area, «EA» pack of
/// day/night tables) and the surface attributes of the walkable ground (part 6 of its blocks). Nothing is written.
/// </summary>
internal static class Map3dsGen7EncounterResearch
{
    /// <summary>Cells (105 units each, as in the editor) covered by ground triangles of one attribute in one area, with their height.</summary>
    internal static void Triangles(string root, int area, uint attribute)
    {
        var backend = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        var tris = backend.GroundCollision(area).Where(t => t.Attribute == attribute).ToArray();
        Console.WriteLine($"Área {area}: {tris.Length} triángulos con atributo {attribute:X2} (solo lectura)");
        foreach (var g in tris.GroupBy(t => ((int)Math.Floor(((t.A.X + t.B.X + t.C.X) / 3) / 105), (int)Math.Floor(((t.A.Z + t.B.Z + t.C.Z) / 3) / 105))).OrderBy(g => g.Key.Item2).ThenBy(g => g.Key.Item1))
            Console.WriteLine($"  casilla {g.Key.Item1},{g.Key.Item2}: {g.Count()} triángulos, altura {g.Average(t => (t.A.Y + t.B.Y + t.C.Y) / 3):0.0}, extensión X {g.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))):0}–{g.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X))):0} Z {g.Min(t => Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z))):0}–{g.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z))):0}");
    }

    /// <summary>Checks the encounter-zone readers and writers on every area of a real copy, in memory (nothing is written).</summary>
    internal static void Verify(string root)
    {
        var config = GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration;
        var encdata = config.GetlzGARCData("encdata");
        int areas = encdata.FileCount / Gen7MapBackend.SlotsPerWorld, edOk = 0, eaOk = 0, regions = 0, single = 0, withTables = 0;
        foreach (int a in Enumerable.Range(0, areas))
        {
            byte[] ed = encdata[a * Gen7MapBackend.SlotsPerWorld], ea = encdata[(a * Gen7MapBackend.SlotsPerWorld) + 9];
            int files = Gen7CatchZones.RegionFiles(ed);
            if (files >= 0) edOk++;
            if (files == 1) single++;
            int tables = Gen7CatchZones.TableCount(ea);
            if (tables >= 0) eaOk++;
            if (tables > 0) withTables++;
            if (files >= 0) regions += Gen7CatchZones.Regions(ed).Count;
        }
        Console.WriteLine($"Áreas {areas}: ED reconstruible byte a byte {edOk}, EA reconstruible {eaOk} ({withTables} con tablas), regiones leídas {regions}, áreas con un solo archivo de regiones {single}");
        byte[] lilii = encdata[1 * Gen7MapBackend.SlotsPerWorld], route = encdata[9], liliiEa = encdata[(1 * Gen7MapBackend.SlotsPerWorld) + 9];
        var grass = Gen7CatchZones.Regions(encdata[0]).First(r => r.Mask == Gen7CatchZones.MaskOf(Gen7CatchZones.GrassAttribute));
        var entry = Gen7CatchZones.Table(route, (int)grass.Table)!;
        var (ea2, index) = Gen7CatchZones.AddTable(liliiEa, entry);
        var ed2 = Gen7CatchZones.AddRegion(lilii, 0, (uint)index, Gen7CatchZones.MaskOf(Gen7CatchZones.GrassAttribute), new System.Numerics.Vector3(17600, 8, 15000), new System.Numerics.Vector3(1000, 500, 1000));
        var back = Gen7CatchZones.Regions(ed2);
        Console.WriteLine($"En memoria: Pueblo Lilii pasa de {Gen7CatchZones.Regions(lilii).Count} a {back.Count} regiones (tabla {back[0].Table}, máscara {back[0].Mask:X8}) y de {Gen7CatchZones.TableCount(liliiEa)} a {Gen7CatchZones.TableCount(ea2)} tablas; " +
            $"ED {lilii.Length} → {ed2.Length} B; Mini.UnpackMini lee {Mini.UnpackMini(ea2, "EA")!.Length} tablas; tabla 0 = Ruta 1 tabla {grass.Table}: {Mini.UnpackMini(ea2, "EA")![0].AsSpan().SequenceEqual(entry)}");
    }

    /// <summary>Catch plan of the grass cells of a rectangle of an area, on a real copy (read-only).</summary>
    internal static void Plan(string root, int area, int x1, int y1, int x2, int y2)
    {
        var b = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        Console.WriteLine($"Fuentes: {b.EncounterSources.Count}");
        foreach (var src in b.EncounterSources) Console.WriteLine($"  fuente {src.Id}: {src.Name}");
        Console.WriteLine($"Área {area}: tablas {Gen7CatchZones.TableCount(b.TableBytes(area))}, regiones {b.EncounterRegions(area).Count}, casillas con encuentros {b.EncounterCells(area).Count}, casillas de hierba 0x1F {b.GrassCells(area).Count}");
        var cells = b.PaintableCells(area).Where(c => c.X >= x1 && c.X <= x2 && c.Y >= y1 && c.Y <= y2).ToArray();
        Console.WriteLine($"Casillas disponibles en ({x1},{y1})–({x2},{y2}): {cells.Length} casillas: {string.Join(" ", cells.Select(c => $"{c.X},{c.Y}"))}");
        int source = (b.EncounterSources.FirstOrDefault(s => s.Name.Contains("0x19")) ?? b.EncounterSources.First()).Id;
        Console.WriteLine($"Con la fuente {source}: " + b.ValidateCatchCells(area, cells, source).Reason);
        foreach (var e in b.ZoneEvents(area).Where(e => e.Placed && (e.Kind == pk3DS.Core.Maps.MapEventKind.Warp || e.Kind == pk3DS.Core.Maps.MapEventKind.Npc)))
            Console.WriteLine($"  evento {e.Label} en casilla {e.X:0},{e.Y:0}");
        Console.WriteLine($"Ruta 1 (área 0): casillas con encuentros {b.EncounterCells(0).Count}, hierba 0x1F {b.GrassCells(0).Count}, hierba sin región {b.GrassCells(0).Except(b.EncounterCells(0)).Count()}");
    }

    /// <summary>
    /// Read-only: whether two areas share terrain. Prints the matrix of both, the blocks they share, the grass clusters
    /// of <paramref name="a"/> and, for each, the encounter cells of <paramref name="b"/> at the same coordinates.
    /// </summary>
    internal static void Overlap(string root, int a, int b)
    {
        var be = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        var wa = be.World(a); var wb = be.World(b);
        string M(pk3DS.Core.Maps.Gen7.Gen7World w) => $"{w.Width}×{w.Height}, bloque {w.BlockSize}: " + string.Join(" ", Enumerable.Range(0, w.Width * w.Height).Where(i => w.Blocks[i] >= 0).Select(i => $"({i % w.Width},{i / w.Width})={w.Blocks[i]}"));
        Console.WriteLine($"Área {a}: {M(wa)}");
        Console.WriteLine($"Área {b}: {M(wb)}");
        var shared = wa.Blocks.Where(x => x >= 0).Intersect(wb.Blocks.Where(x => x >= 0)).ToArray();
        Console.WriteLine($"Bloques compartidos: {string.Join(" ", shared)}");
        var grassA = be.GrassCells(a).ToHashSet(); var encB = be.EncounterCells(b).ToHashSet(); var grassB = be.GrassCells(b).ToHashSet(); var encA = be.EncounterCells(a).ToHashSet();
        var seen = new HashSet<(int, int)>();
        foreach (var start in grassA.OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            if (!seen.Add(start)) continue;
            var comp = new List<(int X, int Y)> { start }; var q = new Queue<(int X, int Y)>(); q.Enqueue(start);
            while (q.Count > 0) { var (x, y) = q.Dequeue(); foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) }) if (grassA.Contains(n) && seen.Add(n)) { comp.Add(n); q.Enqueue(n); } }
            Console.WriteLine($"Hierba de {a}: {comp.Count} casillas en X {comp.Min(c => c.X)}–{comp.Max(c => c.X)}, Y {comp.Min(c => c.Y)}–{comp.Max(c => c.Y)}; con encuentros en {a}: {comp.Count(encA.Contains)}; en {b}: hierba {comp.Count(grassB.Contains)}, con encuentros {comp.Count(encB.Contains)}");
        }
        foreach (var r in be.EncounterRegions(b).Take(40))
            Console.WriteLine($"  región de {b}: tabla {r.Table} máscara 0x{r.Mask:X8} centro casilla {r.Position.X / 105:0},{r.Position.Z / 105:0} {(r.Box ? $"caja {r.Size.X:0}×{r.Size.Z:0}" : $"cilindro r {r.Radius:0}")}");
    }

    /// <summary>Read-only: grass clusters where a catch zone can be created (own terrain, no encounters), per area, with the area's own regions.</summary>
    internal static void Candidates(string root)
    {
        var be = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        foreach (var z in be.Zones)
        {
            IReadOnlyList<(int X, int Y)> paint;
            try { paint = be.PaintableCells(z.Id); } catch (Exception ex) { Console.WriteLine($"{z.Name}: error {ex.Message}"); continue; }
            if (paint.Count == 0) continue;
            var set = paint.ToHashSet(); var seen = new HashSet<(int, int)>();
            int files = Gen7CatchZones.RegionFiles(be.EventBytes(z.Id));
            foreach (var start in paint)
            {
                if (!seen.Add(start)) continue;
                var comp = new List<(int X, int Y)> { start }; var q = new Queue<(int X, int Y)>(); q.Enqueue(start);
                while (q.Count > 0) { var (x, y) = q.Dequeue(); foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) }) if (set.Contains(n) && seen.Add(n)) { comp.Add(n); q.Enqueue(n); } }
                if (comp.Count < 12) continue;
                Console.WriteLine($"{z.Name}	regiones {be.EncounterRegions(z.Id).Count}	archivos {files}	tablas {Gen7CatchZones.TableCount(be.TableBytes(z.Id))}	{comp.Count} casillas X {comp.Min(c => c.X)}–{comp.Max(c => c.X)} Y {comp.Min(c => c.Y)}–{comp.Max(c => c.Y)}");
            }
        }
    }

    /// <summary>
    /// Read-only: how much of each grass patch the grass boxes (mask 0x80000000) cover when their size is read as the full
    /// width (factor 0.5) or as a half-extent (factor 1). Per box: grass cells of the touched patches, covered cells and the
    /// grass cells of other patches it would reach.
    /// </summary>
    internal static void BoxScale(string root)
    {
        var be = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        uint grassMask = Gen7CatchZones.MaskOf(Gen7CatchZones.GrassAttribute);
        double[] factors = [0.5, 1.0];
        var tot = new double[2]; var spill = new int[2]; int boxes = 0, patchCells = 0;
        foreach (var z in be.Zones)
        {
            var regions = be.EncounterRegions(z.Id).Where(x => x.Box && x.Mask == grassMask).ToArray();
            if (regions.Length == 0) continue;
            var grass = be.GrassCells(z.Id).ToHashSet();
            var comp = new Dictionary<(int, int), int>(); int next = 0;
            foreach (var st in grass) { if (comp.ContainsKey(st)) continue; var q = new Queue<(int X, int Y)>(); q.Enqueue(st); comp[st] = next; while (q.Count > 0) { var (x, y) = q.Dequeue(); foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) }) if (grass.Contains(n) && !comp.ContainsKey(n)) { comp[n] = next; q.Enqueue(n); } } next++; }
            foreach (var rg in regions)
            {
                var inv = Quaternion.Inverse(Quaternion.Normalize(rg.Rotation));
                bool In((int X, int Y) c, double f) { var l = Vector3.Transform(new Vector3((c.X + 0.5f) * 105, rg.Position.Y, (c.Y + 0.5f) * 105) - rg.Position, inv); return Math.Abs(l.X) <= rg.Size.X * f && Math.Abs(l.Z) <= rg.Size.Z * f; }
                var core = grass.Where(c => In(c, 0.5)).Select(c => comp[c]).ToHashSet();
                if (core.Count == 0) { Console.WriteLine($"{z.Name} tabla {rg.Table}: sin hierba en la caja"); continue; }
                var patch = grass.Where(c => core.Contains(comp[c])).ToArray();
                boxes++; patchCells += patch.Length;
                var line = $"{z.Name} tabla {rg.Table} caja {rg.Size.X:0}×{rg.Size.Z:0}: hierba de la mancha {patch.Length}";
                for (int k = 0; k < 2; k++)
                {
                    int cov = patch.Count(c => In(c, factors[k])); int sp = grass.Count(c => !core.Contains(comp[c]) && In(c, factors[k]));
                    tot[k] += cov; spill[k] += sp; line += $" | f{factors[k]}: cubre {cov} ({100.0 * cov / patch.Length:0}%), otras manchas {sp}";
                }
                Console.WriteLine(line);
            }
        }
        Console.WriteLine($"TOTAL {boxes} cajas, {patchCells} casillas de hierba en sus manchas: ancho completo cubre {100 * tot[0] / patchCells:0.0}% (otras manchas {spill[0]}), semieje cubre {100 * tot[1] / patchCells:0.0}% (otras manchas {spill[1]})");
    }

    /// <summary>Read-only: attribute bits used by vanilla region masks (regions per bit) and ground attributes of an area's own terrain.</summary>
    internal static void MaskBits(string root, int area)
    {
        var be = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        var bits = new SortedDictionary<int, int>();
        foreach (var z in be.Zones) foreach (var rg in be.EncounterRegions(z.Id)) for (int k = 0; k < 32; k++) if ((rg.Mask >> k & 1) != 0) bits[k] = bits.GetValueOrDefault(k) + 1;
        Console.WriteLine("Bits de máscara (atributo: regiones): " + string.Join(" ", bits.Select(kv => $"0x{kv.Key:X2}:{kv.Value}")));
        var w = be.World(area);
        var own = new Dictionary<uint, int>();
        foreach (var t in be.GroundCollision(area))
        {
            float cx = (t.A.X + t.B.X + t.C.X) / 3, cz = (t.A.Z + t.B.Z + t.C.Z) / 3;
            int col = (int)Math.Floor(cx / 4200), row = (int)Math.Floor(cz / 4200);
            if (be.AreasSharingBlock(area, col, row).Count > 0) continue;
            own[t.Attribute] = own.GetValueOrDefault(t.Attribute) + 1;
        }
        Console.WriteLine($"Área {area}, triángulos de bloques propios por atributo: " + string.Join(" ", own.OrderBy(k => k.Key).Select(kv => $"0x{kv.Key:X2}:{kv.Value}")));
    }

    /// <summary>
    /// Read-only, per ground attribute used by vanilla masks: regions whose mask has only that bit or combined; then, per
    /// area, patches of that ground on the area's own terrain without encounters (not another area's terrain), 12+ cells.
    /// </summary>
    internal static void AttributeCandidates(string root, string attributes)
    {
        var be = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        foreach (uint attr in attributes.Split(',').Select(x => Convert.ToUInt32(x, 16)))
        {
            uint bit = Gen7CatchZones.MaskOf(attr);
            int only = 0, combined = 0; var maskAreas = new SortedSet<string>();
            foreach (var z in be.Zones) foreach (var rg in be.EncounterRegions(z.Id)) if ((rg.Mask & bit) != 0) { if (rg.Mask == bit) only++; else combined++; maskAreas.Add(z.Name); }
            Console.WriteLine($"== Atributo 0x{attr:X2}: regiones solo con este bit {only}, combinado {combined}; áreas: {string.Join(", ", maskAreas.Take(12))}{(maskAreas.Count > 12 ? " …" : "")}");
            foreach (var z in be.Zones)
            {
                IReadOnlyList<(int X, int Y)> cells;
                try { cells = be.CellsWithAttribute(z.Id, attr); } catch (Exception) { continue; }
                if (cells.Count == 0) continue;
                var enc = be.EncounterCells(z.Id).ToHashSet();
                var free = cells.Where(c => !enc.Contains(c) && be.OtherOwner(z.Id, c) is null).ToHashSet();
                int withEnc = cells.Count(enc.Contains);
                var seen = new HashSet<(int, int)>(); var comps = new List<List<(int X, int Y)>>();
                foreach (var st in free.OrderBy(c => c.Y).ThenBy(c => c.X))
                {
                    if (!seen.Add(st)) continue;
                    var comp = new List<(int X, int Y)> { st }; var q = new Queue<(int X, int Y)>(); q.Enqueue(st);
                    while (q.Count > 0) { var (x, y) = q.Dequeue(); foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) }) if (free.Contains(n) && seen.Add(n)) { comp.Add(n); q.Enqueue(n); } }
                    if (comp.Count >= 12) comps.Add(comp);
                }
                if (comps.Count == 0 && withEnc == 0) continue;
                Console.WriteLine($"{z.Name}\tregiones {be.EncounterRegions(z.Id).Count}\tarchivos {Gen7CatchZones.RegionFiles(be.EventBytes(z.Id))}\tcasillas 0x{attr:X2} {cells.Count}, con encuentros {withEnc}, libres propias {free.Count}"
                    + string.Concat(comps.OrderByDescending(c => c.Count).Take(4).Select(c => $" | {c.Count} en X {c.Min(p => p.X)}–{c.Max(p => p.X)} Y {c.Min(p => p.Y)}–{c.Max(p => p.Y)}")));
            }
        }
    }

    /// <summary>
    /// Read-only description of an area: matrix, placed events (kind and cell), regions (table, mask, cells it covers) and
    /// the free patches of an attribute, to judge whether the player can reach a patch.
    /// </summary>
    internal static void AreaInfo(string root, int area, uint attr)
    {
        var be = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        var w = be.World(area);
        Console.WriteLine($"{be.Zones.First(z => z.Id == area).Name}: matriz {w.Width}×{w.Height}: " + string.Join(" ", Enumerable.Range(0, w.Width * w.Height).Where(i => w.Blocks[i] >= 0).Select(i => $"({i % w.Width},{i / w.Width})={w.Blocks[i]}")));
        foreach (var g in be.ZoneEvents(area).Where(e => e.Placed).GroupBy(e => e.Kind))
            Console.WriteLine($"  {g.Key}: {g.Count()} en X {g.Min(e => e.X):0}–{g.Max(e => e.X):0} Y {g.Min(e => e.Y):0}–{g.Max(e => e.Y):0}: " + string.Join(" ", g.Take(30).Select(e => $"{e.X:0},{e.Y:0}")));
        var ground = be.CellsWithAttribute(area, attr).ToHashSet();
        foreach (var rg in be.EncounterRegions(area))
        {
            var cov = ground.Where(c => rg.Allows(attr) && rg.Contains(new Vector3((c.X + 0.5f) * 105, rg.Position.Y, (c.Y + 0.5f) * 105))).ToArray();
            Console.WriteLine($"  región archivo {rg.ZoneFile} tabla {rg.Table} máscara 0x{rg.Mask:X8} centro {rg.Position.X / 105:0},{rg.Position.Z / 105:0} y {rg.Position.Y:0} " +
                (rg.Box ? $"semiejes {rg.Size.X:0}×{rg.Size.Y:0}×{rg.Size.Z:0}" : $"cilindro r {rg.Radius:0} h {rg.Height:0}") + (cov.Length > 0 ? $"; casillas 0x{attr:X2} {cov.Length} en X {cov.Min(c => c.X)}–{cov.Max(c => c.X)} Y {cov.Min(c => c.Y)}–{cov.Max(c => c.Y)}" : ""));
        }
        var enc = be.EncounterCells(area).ToHashSet();
        var free = ground.Where(c => !enc.Contains(c)).ToArray();
        foreach (var grp in free.GroupBy(c => be.OtherOwner(area, c) ?? "(propias)"))
            Console.WriteLine($"  libres 0x{attr:X2} {grp.Key}: {grp.Count()} en X {grp.Min(c => c.X)}–{grp.Max(c => c.X)} Y {grp.Min(c => c.Y)}–{grp.Max(c => c.Y)}");
    }

    /// <summary>
    /// Read-only reachability hint: ground triangles connected by shared vertices (rounded to 1 unit) to the triangles under
    /// the area's warps, without a step over <paramref name="step"/> units between neighbours. Prints, for each region and for
    /// the free patches of <paramref name="attr"/>, how many of their cells are on connected ground. Connected ground does not
    /// prove the player can walk there (walls, scripts); disconnected ground suggests scenery.
    /// </summary>
    internal static void Reach(string root, int area, uint attr, float step)
    {
        var be = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        var tris = be.GroundCollision(area);
        (int, int, int) K(Vector3 v) => ((int)MathF.Round(v.X), (int)MathF.Round(v.Y), (int)MathF.Round(v.Z));
        var byVertex = new Dictionary<(int, int, int), List<int>>();
        for (int i = 0; i < tris.Count; i++)
            foreach (var v in new[] { tris[i].A, tris[i].B, tris[i].C })
            { var k = K(v); if (!byVertex.TryGetValue(k, out var l)) byVertex[k] = l = []; l.Add(i); }
        Vector3 Centre(int i) => (tris[i].A + tris[i].B + tris[i].C) / 3;
        bool Under(int i, float x, float z)
        {
            var t = tris[i];
            float d = ((t.B.Z - t.C.Z) * (t.A.X - t.C.X)) + ((t.C.X - t.B.X) * (t.A.Z - t.C.Z));
            if (Math.Abs(d) < 1e-6f) return false;
            float u = (((t.B.Z - t.C.Z) * (x - t.C.X)) + ((t.C.X - t.B.X) * (z - t.C.Z))) / d, v = (((t.C.Z - t.A.Z) * (x - t.C.X)) + ((t.A.X - t.C.X) * (z - t.C.Z))) / d;
            return u >= -1e-4f && v >= -1e-4f && 1 - u - v >= -1e-4f;
        }
        var reached = new bool[tris.Count]; var queue = new Queue<int>();
        foreach (var e in be.ZoneEvents(area).Where(e => e.Placed && e.Kind == pk3DS.Core.Maps.MapEventKind.Warp))
        {
            float x = ((float)e.X + 0.5f) * 105, z = ((float)e.Y + 0.5f) * 105;
            for (int i = 0; i < tris.Count; i++) if (!reached[i] && Under(i, x, z)) { reached[i] = true; queue.Enqueue(i); }
        }
        int starts = queue.Count;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(); var ci = Centre(i);
            foreach (var v in new[] { tris[i].A, tris[i].B, tris[i].C })
                foreach (int j in byVertex[K(v)])
                    if (!reached[j] && Math.Abs(Centre(j).Y - ci.Y) <= step) { reached[j] = true; queue.Enqueue(j); }
        }
        Console.WriteLine($"Triángulos {tris.Count}, de inicio (bajo warps) {starts}, conectados {reached.Count(x => x)}");
        bool CellReached((int X, int Y) c)
        {
            float x = (c.X + 0.5f) * 105, z = (c.Y + 0.5f) * 105;
            for (int i = 0; i < tris.Count; i++) if (reached[i] && tris[i].Attribute == attr && Under(i, x, z)) return true;
            return false;
        }
        var ground = be.CellsWithAttribute(area, attr);
        foreach (var rg in be.EncounterRegions(area).Where(rg => rg.Allows(attr)))
        {
            var cov = ground.Where(c => rg.Contains(new Vector3((c.X + 0.5f) * 105, rg.Position.Y, (c.Y + 0.5f) * 105))).ToArray();
            Console.WriteLine($"  región tabla {rg.Table}: {cov.Length} casillas, conectadas {cov.Count(CellReached)}");
        }
        var enc = be.EncounterCells(area).ToHashSet();
        var free = ground.Where(c => !enc.Contains(c) && be.OtherOwner(area, c) is null).ToArray();
        Console.WriteLine($"  libres propias 0x{attr:X2}: {free.Length} casillas, conectadas {free.Count(CellReached)}");
    }

    /// <summary>
    /// Read-only: the encounter-grass model of map blocks (part 1 of the block pack, "Map07_encgrass01"). For one block
    /// (or every block when <paramref name="block"/> is -1, summarised): meshes, vertex count, attribute formats and the
    /// world bounding box of each mesh, with the cells of the block whose ground is a writable soil under it.
    /// </summary>
    internal static void GrassModels(string root, int block)
    {
        var config = GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration;
        var map = new GARC.LazyGARC(File.ReadAllBytes(Path.Combine(config.RomFS, "a/0/8/6")));
        int count = map.FileCount;
        var formats = new Dictionary<string, int>(); var meshCounts = new Dictionary<int, int>(); int models = 0;
        for (int id = block < 0 ? 0 : block; id < (block < 0 ? count : block + 1); id++)
        {
            byte[] data = map[id];
            if (data.Length < 8 || data[0] != (byte)'B' || data[1] != (byte)'G') continue;
            var pack = pk3DS.Core.Maps.Gen6.Gen6Pack.Read(data, "BG");
            if (pack.Count < 2 || pack.Length(1) < 4 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(pack.Part(1)) != Gfl2File.ModelMagic) continue;
            if (block >= 0) Console.WriteLine($"Bloque {id}: parte 1 de {pack.Length(1)} B");
            var ms = new[] { Gfl2File.ReadModel(pack.Part(1)) };
            foreach (var m in ms)
            {
                models++;
                meshCounts[m.Meshes.Count] = meshCounts.GetValueOrDefault(m.Meshes.Count) + 1;
                for (int k = 0; k < m.Meshes.Count; k++)
                {
                    var mesh = m.Meshes[k];
                    string f = string.Join(",", mesh.Attributes.Select(x => $"{x.Name}:{x.Format}x{x.Elements}*{x.Scale:0.###}")) + $" stride {mesh.Stride}";
                    formats[f] = formats.GetValueOrDefault(f) + 1;
                    if (block < 0) continue;
                    var pos = mesh.Read(PicaAttribute.Position)!;
                    float minX = pos.Min(v => v.X), maxX = pos.Max(v => v.X), minY = pos.Min(v => v.Y), maxY = pos.Max(v => v.Y), minZ = pos.Min(v => v.Z), maxZ = pos.Max(v => v.Z);
                    Console.WriteLine($"  malla {k}: material {m.Materials[mesh.Material].Name} texturas {string.Join("/", m.Materials[mesh.Material].TextureNames.Where(t => t.Length > 0))}; vértices {mesh.VertexCount}, triángulos {mesh.Triangles().Count()}; X {minX:0}–{maxX:0} ({minX / 105:0}–{maxX / 105:0}) Y {minY:0}–{maxY:0} Z {minZ:0}–{maxZ:0} ({minZ / 105:0}–{maxZ / 105:0})");
                }
            }
        }
        Console.WriteLine($"Modelos de hierba: {models}; mallas por modelo: {string.Join(" ", meshCounts.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"))}");
        foreach (var f in formats.OrderByDescending(k => k.Value)) Console.WriteLine($"  formato {f.Key}: {f.Value} mallas");
    }

    /// <summary>
    /// Read-only ground map of a rectangle of cells: one character per cell for the attribute of its ground (0-9, A-Z for
    /// 0x0A-0x23, '.' without ground, '*' several attributes), with NPCs (n) and warps (w) over it, and the height range.
    /// </summary>
    internal static void GroundMap(string root, int area, int x1, int y1, int x2, int y2)
    {
        var be = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        var attr = new Dictionary<(int, int), HashSet<uint>>();
        for (uint a = 0; a < 0x27; a++) foreach (var c in be.CellsWithAttribute(area, a)) { if (!attr.TryGetValue(c, out var set)) attr[c] = set = []; set.Add(a); }
        var ev = new Dictionary<(int, int), char>();
        foreach (var e in be.ZoneEvents(area).Where(e => e.Placed)) ev[((int)Math.Floor(e.X), (int)Math.Floor(e.Y))] = e.Kind == pk3DS.Core.Maps.MapEventKind.Warp ? 'w' : e.Kind == pk3DS.Core.Maps.MapEventKind.Npc ? 'n' : ev.GetValueOrDefault(((int)Math.Floor(e.X), (int)Math.Floor(e.Y)), 'p');
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Console.WriteLine("     " + string.Concat(Enumerable.Range(x1, x2 - x1 + 1).Select(x => x % 10 == 0 ? (char)('0' + (x / 10 % 10)) : ' ')));
        for (int y = y1; y <= y2; y++)
        {
            var line = new System.Text.StringBuilder($"{y,4} ");
            for (int x = x1; x <= x2; x++)
                line.Append(ev.TryGetValue((x, y), out var k) ? k : !attr.TryGetValue((x, y), out var set) ? '.' : set.Count > 1 ? '*' : digits[(int)set.First()]);
            Console.WriteLine(line);
        }
        foreach (var e in be.ZoneEvents(area).Where(e => e.Placed && e.X >= x1 && e.X <= x2 + 1 && e.Y >= y1 && e.Y <= y2 + 1)) Console.WriteLine($"  {e.Label} en {e.X:0.#},{e.Y:0.#}");
        for (int bx = x1 / 40; bx <= x2 / 40; bx++) for (int by = y1 / 40; by <= y2 / 40; by++)
            Console.WriteLine($"  bloque ({bx},{by}) = {be.World(area).Blocks[(by * be.World(area).Width) + bx]}, lo cargan también: {string.Join(", ", be.AreasSharingBlock(area, bx, by))}");
    }

    /// <summary>
    /// Read-only check of <see cref="Gen7VisibleGrass"/> on every block of a real copy: block packs rebuild byte for byte,
    /// every grass model is recognised, moved by an offset (positions read back moved, every other byte equal) and a zero
    /// offset gives the same bytes.
    /// </summary>
    internal static void GrassVerify(string root)
    {
        var config = GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration;
        var map = new GARC.LazyGARC(File.ReadAllBytes(Path.Combine(config.RomFS, "a/0/8/6")));
        int blocks = 0, rebuilt = 0, grass = 0, moved = 0, zero = 0, single = 0; var failures = new List<string>();
        var delta = new System.Numerics.Vector3(1234.5f, 7.25f, -321f);
        for (int id = 0; id < map.FileCount; id++)
        {
            byte[] data = map[id];
            if (data.Length < 8 || data[0] != (byte)'B' || data[1] != (byte)'G') continue;
            blocks++;
            byte[][] parts;
            try { parts = Gen7VisibleGrass.BlockParts(data); rebuilt++; } catch (Exception ex) { failures.Add($"bloque {id}: {ex.Message}"); continue; }
            if (parts[1].Length == 0) continue;
            if (!Gen7VisibleGrass.IsGrassModel(parts[1])) { failures.Add($"bloque {id}: parte 1 no reconocida"); continue; }
            grass++;
            if (Gen7VisibleGrass.MeshCount(parts[1]) == 1) single++;
            try
            {
                var (min, max) = Gen7VisibleGrass.Bounds(parts[1]);
                var t = Gen7VisibleGrass.Translate(parts[1], delta);
                var (min2, max2) = Gen7VisibleGrass.Bounds(t);
                if (System.Numerics.Vector3.Distance(min2, min + delta) < 0.05f && System.Numerics.Vector3.Distance(max2, max + delta) < 0.05f) moved++;
                else failures.Add($"bloque {id}: caja movida incorrecta");
                if (Gen7VisibleGrass.Translate(parts[1], System.Numerics.Vector3.Zero).AsSpan().SequenceEqual(parts[1])) zero++;
            }
            catch (Exception ex) { failures.Add($"bloque {id}: {ex.Message}"); }
        }
        Console.WriteLine($"Bloques {blocks}: reconstruidos byte a byte {rebuilt}; modelos de hierba {grass} (de una malla {single}); movidos y releídos {moved}; desplazamiento cero idéntico {zero}");
        foreach (var f in failures.Take(20)) Console.WriteLine("  " + f);
    }

    /// <summary>Read-only: the plan of a visible grass patch (template centred on a cell) and the templates the area can use.</summary>
    internal static void GrassPlan(string root, int area, int x, int y, int template)
    {
        var be = Gen7MapBackend.FromConfig(GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration);
        Console.WriteLine("Plantillas con texturas del área: " + string.Join("; ", be.GrassTemplatesFor(area).Select(t => $"{t.Block} ({t.Size.X:0}×{t.Size.Z:0}, {string.Join("/", t.Textures)})")));
        var (plan, reason) = be.PlanGrass(area, (x, y), template);
        if (plan is null) { Console.WriteLine("No: " + reason); return; }
        Console.WriteLine($"Bloque {plan.TargetBlock} ({plan.Col},{plan.Row}); desplazamiento {plan.Delta.X:0.#},{plan.Delta.Y:0.#},{plan.Delta.Z:0.#}; {plan.Covered.Count} casillas X {plan.Covered.Min(c => c.X)}–{plan.Covered.Max(c => c.X)} Y {plan.Covered.Min(c => c.Y)}–{plan.Covered.Max(c => c.Y)}; desnivel {plan.GroundSpread:0.#}; también lo cargan {string.Join(", ", plan.Sharing)}");
        var soils = new Dictionary<uint, int>();
        foreach (var c in plan.Covered) for (uint a = 0; a < 0x27; a++) if (be.CellsWithAttribute(area, a).Contains(c)) soils[a] = soils.GetValueOrDefault(a) + 1;
        Console.WriteLine("Suelos bajo la hierba: " + string.Join(" ", soils.Select(k => $"0x{k.Key:X2}:{k.Value}")));
        var ev = be.ZoneEvents(area).Where(e => e.Placed && plan.Covered.Contains(((int)Math.Floor(e.X), (int)Math.Floor(e.Y)))).ToArray();
        Console.WriteLine($"Eventos bajo la hierba: {(ev.Length == 0 ? "ninguno" : string.Join(", ", ev.Select(e => e.Label)))}");
    }

    /// <summary>Day and night summary of one encounter table of an area, with the game's species names (read-only).</summary>
    internal static void Species(string root, int area, int table)
    {
        var config = GameProjectSession.Open(Path.GetFullPath(root), false).GameConfiguration;
        var b = Gen7MapBackend.FromConfig(config);
        var entry = Gen7CatchZones.Table(b.TableBytes(area), table) ?? throw new InvalidDataException("No existe esa tabla.");
        string[] names = config.GetText(TextName.SpeciesNames);
        Console.WriteLine("Día: " + new EncounterTable(entry.Skip(4).Take(0x164).ToArray()).GetSummary(names));
        Console.WriteLine("Noche: " + new EncounterTable(entry.Skip(0x168).ToArray()).GetSummary(names));
    }

    internal static void Run(string root, string output)
    {
        var session = GameProjectSession.Open(Path.GetFullPath(root), false);
        var config = session.GameConfiguration;
        var backend = Gen7MapBackend.FromConfig(config);
        var encdata = config.GetlzGARCData("encdata");
        using var w = new StreamWriter(output);
        int areas = encdata.FileCount / Gen7MapBackend.SlotsPerWorld;
        w.WriteLine($"areas\t{areas}");
        for (int a = 0; a < areas; a++)
        {
            byte[] slot = encdata[(a * Gen7MapBackend.SlotsPerWorld) + 9];
            int tables = 0, nonEmpty = 0;
            string tableSizes = "";
            if (slot.Length >= 4 && slot[0] == 'E' && slot[1] == 'A')
            {
                var parts = Mini.UnpackMini(slot, "EA") ?? [];
                tables = parts.Length; nonEmpty = parts.Count(p => p.Length > 0);
                tableSizes = string.Join(",", parts.Select(p => p.Length));
            }
            IReadOnlyList<Gen7CollisionTriangle> tris;
            try { tris = backend.GroundCollision(a); } catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidDataException) { tris = []; }
            var hist = tris.GroupBy(t => t.Attribute).OrderByDescending(g => g.Count()).Select(g => $"{g.Key:X8}:{g.Count()}");
            string name = backend.Zones.FirstOrDefault(z => z.Id == a)?.Name ?? "";
            // ED parts: per zone file, u32 count and the first u32 (type) of the first record, plus the file size.
            string ed = "";
            if (backend.EventPack(a) is { } pack)
                ed = string.Join(" ", Enumerable.Range(0, pack.Count).Select(p =>
                {
                    var part = pack.Part(p).ToArray();
                    var inner = Gen7Events.Container(part);
                    if (inner is null) return $"p{p}:raw{part.Length}";
                    var files = Enumerable.Range(0, inner.Count).Select(z => inner.Part(z).ToArray()).ToArray();
                    string Desc(byte[] f) => f.Length < 4 ? $"{f.Length}b" : $"{BitConverter.ToInt32(f, 0)}x" + (f.Length >= 8 ? $"t{BitConverter.ToUInt32(f, 4)}" : "") + $"/{f.Length}";
                    return $"p{p}:[{string.Join(",", files.Select(Desc))}]";
                }));
            w.WriteLine($"{a}\t{name}\tslot9={slot.Length}\ttables={tables}\tnonempty={nonEmpty}\tsizes={tableSizes}\ttris={tris.Count}\t{string.Join(" ", hist)}\t{ed}");
        }
        Console.WriteLine($"Investigación escrita en {output} (solo lectura)");
        string regionsFile = Path.ChangeExtension(output, null) + "-regiones.tsv";
        using var rw = new StreamWriter(regionsFile);
        rw.WriteLine("area\tzona\tid\tmascara\tforma\tdentro\tdentro_con_atributo\tdentro_sin_atributo\tfuera_con_atributo\tatributos_dentro");
        for (int a = 0; a < areas; a++)
        {
            if (backend.EventPack(a) is not { } pack || pack.Count <= 6) continue;
            var inner = Gen7Events.Container(pack.Part(6).ToArray());
            if (inner is null) continue;
            IReadOnlyList<Gen7CollisionTriangle> tris;
            try { tris = backend.GroundCollision(a); } catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidDataException) { continue; }
            for (int z = 0; z < inner.Count; z++)
            {
                byte[] f = inner.Part(z).ToArray();
                if (f.Length < 4) continue;
                int n = BitConverter.ToInt32(f, 0);
                for (int i = 0; i < n; i++)
                {
                    int h = 4 + (i * 28);
                    uint id = BitConverter.ToUInt32(f, h + 4), mask = BitConverter.ToUInt32(f, h + 8);
                    int off = BitConverter.ToInt32(f, h + 24);
                    uint kind = BitConverter.ToUInt32(f, off + 4);
                    float F(int k) => BitConverter.ToSingle(f, off + k);
                    var pos = new System.Numerics.Vector3(F(8), F(12), F(16));
                    Func<System.Numerics.Vector3, bool> inside;
                    string shape;
                    if (kind == 1)
                    {
                        var q = new System.Numerics.Quaternion(F(20), F(24), F(28), F(32));
                        var size = new System.Numerics.Vector3(F(36), F(40), F(44));
                        var inv = System.Numerics.Quaternion.Inverse(System.Numerics.Quaternion.Normalize(q));
                        inside = c =>
                        {
                            var l = System.Numerics.Vector3.Transform(c - pos, inv);
                            return Math.Abs(l.X) <= size.X / 2 && Math.Abs(l.Z) <= size.Z / 2 && Math.Abs(l.Y) <= Math.Max(size.Y, 1) ;
                        };
                        shape = $"caja {size.X:0}x{size.Y:0}x{size.Z:0}";
                    }
                    else
                    {
                        float radius = F(20), height = F(24);
                        inside = c => Math.Pow(c.X - pos.X, 2) + Math.Pow(c.Z - pos.Z, 2) <= radius * radius && Math.Abs(c.Y - pos.Y) <= Math.Max(height, 1);
                        shape = $"cilindro r{radius:0} h{height:0}";
                    }
                    int ins = 0, insAttr = 0, outAttr = 0;
                    var attrsIn = new Dictionary<uint, int>();
                    foreach (var t in tris)
                    {
                        var c = (t.A + t.B + t.C) / 3;
                        bool allowed = t.Attribute < 32 && ((mask >> (int)t.Attribute) & 1) != 0;
                        if (inside(c)) { ins++; if (allowed) insAttr++; attrsIn[t.Attribute] = attrsIn.GetValueOrDefault(t.Attribute) + 1; }
                        else if (allowed) outAttr++;
                    }
                    rw.WriteLine($"{a}\t{z}\t{id}\t{mask:X8}\t{shape}\t{ins}\t{insAttr}\t{ins - insAttr}\t{outAttr}\t{string.Join(" ", attrsIn.OrderByDescending(p => p.Value).Select(p => $"{p.Key:X2}:{p.Value}"))}");
                }
            }
        }
        Console.WriteLine($"Regiones escritas en {regionsFile} (solo lectura)");
    }
}
