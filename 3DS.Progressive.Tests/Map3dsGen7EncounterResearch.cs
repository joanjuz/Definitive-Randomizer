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
        Console.WriteLine($"Fuentes de hierba: {b.EncounterSources.Count} (p. ej. {string.Join("; ", b.EncounterSources.Take(3).Select(s => $"{s.Id} {s.Name}"))})");
        Console.WriteLine($"Área {area}: tablas {Gen7CatchZones.TableCount(b.TableBytes(area))}, regiones {b.EncounterRegions(area).Count}, casillas con encuentros {b.EncounterCells(area).Count}, casillas de hierba 0x1F {b.GrassCells(area).Count}");
        var cells = b.GrassCells(area).Where(c => c.X >= x1 && c.X <= x2 && c.Y >= y1 && c.Y <= y2).ToArray();
        Console.WriteLine($"Hierba en ({x1},{y1})–({x2},{y2}): {cells.Length} casillas: {string.Join(" ", cells.Select(c => $"{c.X},{c.Y}"))}");
        int source = b.EncounterSources.First(s => s.Id / Gen7MapBackend.SourceStride == 0).Id;
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
