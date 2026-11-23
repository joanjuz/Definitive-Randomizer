#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace pk3DS.Core.Maps.Gen7;

/// <summary>What a new encounter zone of an area would write: a box region over the cells and the table it uses.</summary>
/// <param name="ZoneFile">File of ED part 6 (zone of the area) that receives the region.</param>
/// <param name="SourceArea">Area whose table is used; when it is not <c>Area</c> the table is copied into the area.</param>
/// <param name="Covered">Cells with an allowed attribute inside the box that were not selected (they also get encounters).</param>
public sealed record Gen7CatchPlan(int Area, int ZoneFile, Vector3 Position, Vector3 Size, uint Mask, int SourceArea, int SourceTable, bool NewTable, int Cells, int Covered);

/// <summary>
/// Catch zones of Ultra Sun / Ultra Moon. A cell produces encounters when its ground triangle has an attribute allowed by
/// the mask of an encounter region (ED part 6) whose shape contains it; the region names its table (an entry of the
/// area's EA pack). See <see cref="Gen7CatchZones"/>. For now only the grass attribute of Route 1 (0x1F) is written,
/// only in areas with a single region file, with a table referenced by a vanilla grass region.
/// </summary>
public sealed partial class Gen7MapBackend : IMapCatchZones
{
    /// <summary>Source ids encode the area and the table: area × <see cref="SourceStride"/> + table.</summary>
    public const int SourceStride = 1000;
    public const int MaxCatchCells = 1600, MaxRegions = 64, MaxTables = 64;

    public static (int Area, int Table) SourceOf(int id) => (id / SourceStride, id % SourceStride);

    public byte[] TableBytes(int area) => (byte[])_world((area * SlotsPerWorld) + Gen7CatchZones.TableSlot).Clone();

    public IReadOnlyList<Gen7EncounterRegion> EncounterRegions(int area)
    {
        try { return Gen7CatchZones.Regions(EventBytes(area)); }
        catch (Exception ex) when (ex is MapFormatException or ArgumentException or IndexOutOfRangeException) { return []; }
    }

    private readonly Dictionary<int, Dictionary<(int X, int Y), List<(uint Attribute, float Height)>>> _groundCells = [];

    /// <summary>Ground triangles under each cell centre (105-unit cells), with their attribute and height there.</summary>
    private Dictionary<(int X, int Y), List<(uint Attribute, float Height)>> GroundCells(int area)
    {
        lock (_catchGate)
        {
            if (_groundCells.TryGetValue(area, out var cached)) return cached;
            var cells = new Dictionary<(int, int), List<(uint, float)>>();
            foreach (var t in GroundCollision(area))
            {
                float minX = Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), maxX = Math.Max(t.A.X, Math.Max(t.B.X, t.C.X));
                float minZ = Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z)), maxZ = Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z));
                for (int cx = (int)Math.Floor(minX / UnitsPerCell); cx <= (int)Math.Floor(maxX / UnitsPerCell); cx++)
                    for (int cy = (int)Math.Floor(minZ / UnitsPerCell); cy <= (int)Math.Floor(maxZ / UnitsPerCell); cy++)
                    {
                        float px = (cx + 0.5f) * UnitsPerCell, pz = (cy + 0.5f) * UnitsPerCell;
                        if (HeightAt(t, px, pz) is not float h) continue;
                        if (!cells.TryGetValue((cx, cy), out var list)) cells[(cx, cy)] = list = [];
                        list.Add((t.Attribute, h));
                    }
            }
            _groundCells[area] = cells;
            return cells;
        }
    }

    private readonly object _catchGate = new();

    /// <summary>Height of the triangle at (x, z) when the point is inside it seen from above.</summary>
    private static float? HeightAt(Gen7CollisionTriangle t, float x, float z)
    {
        float d = ((t.B.Z - t.C.Z) * (t.A.X - t.C.X)) + ((t.C.X - t.B.X) * (t.A.Z - t.C.Z));
        if (Math.Abs(d) < 1e-6f) return null;
        float a = (((t.B.Z - t.C.Z) * (x - t.C.X)) + ((t.C.X - t.B.X) * (z - t.C.Z))) / d;
        float b = (((t.C.Z - t.A.Z) * (x - t.C.X)) + ((t.A.X - t.C.X) * (z - t.C.Z))) / d;
        float c = 1 - a - b;
        const float eps = -1e-4f;
        return a < eps || b < eps || c < eps ? null : (a * t.A.Y) + (b * t.B.Y) + (c * t.C.Y);
    }

    /// <summary>Cells whose ground has the grass attribute that can be written (0x1F), with or without encounters.</summary>
    public IReadOnlyList<(int X, int Y)> GrassCells(int zone) =>
        GroundCells(zone).Where(c => c.Value.Any(g => g.Attribute == Gen7CatchZones.GrassAttribute)).Select(c => c.Key).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();

    public IReadOnlyList<(int X, int Y)> PaintableCells(int zone)
    {
        if (!_worlds.ContainsKey(zone)) return [];
        var encounters = EncounterCells(zone).ToHashSet();
        return GrassCells(zone).Where(c => !encounters.Contains(c) && OtherOwner(zone, c) is null).ToArray();
    }

    /// <summary>
    /// Areas whose matrix has the same block at the same position: the terrain a world loads around its own (an area also
    /// contains the ground of its neighbours, e.g. Iki Town holds Route 1's grass).
    /// </summary>
    public IReadOnlyList<int> AreasSharingBlock(int zone, int col, int row)
    {
        var w = World(zone);
        if (col < 0 || row < 0 || col >= w.Width || row >= w.Height || w.Blocks[(row * w.Width) + col] is not (>= 0 and var block)) return [];
        return _worlds.Where(o => o.Key != zone && col < o.Value.Width && row < o.Value.Height && o.Value.Blocks[(row * o.Value.Width) + col] == block).Select(o => o.Key).ToArray();
    }

    private readonly Dictionary<(int Zone, int Col, int Row), bool> _ownsBlock = [];
    private Dictionary<(int X, int Y), List<(int Area, float Height)>>? _grassEncounters;

    /// <summary>Every grass cell (0x1F) with encounters in any area, with the area and the ground height.</summary>
    private Dictionary<(int X, int Y), List<(int Area, float Height)>> GrassEncounterCells()
    {
        lock (_catchGate)
        {
            if (_grassEncounters is not null) return _grassEncounters;
            var map = new Dictionary<(int, int), List<(int, float)>>();
            foreach (int area in _worlds.Keys)
            {
                if (!EncounterRegions(area).Any(r => r.Allows(Gen7CatchZones.GrassAttribute))) continue;
                var ground = GroundCells(area);
                foreach (var c in EncounterCells(area))
                    foreach (var g in ground[c].Where(g => g.Attribute == Gen7CatchZones.GrassAttribute))
                    {
                        if (!map.TryGetValue(c, out var list)) map[c] = list = [];
                        list.Add((area, g.Height));
                    }
            }
            return _grassEncounters = map;
        }
    }

    /// <summary>
    /// The area the ground of a cell is known to belong to when it is not <paramref name="zone"/>, or null when the cell can be
    /// written in the zone. A block that no other area loads belongs to the zone; a shared block only when the zone already
    /// has encounters in it. Otherwise it is not known which area the player is in there, so it is not written.
    /// </summary>
    public string? OtherOwner(int zone, (int X, int Y) cell)
    {
        // The same ground in another area (a copy of the terrain with other block ids) that already has encounters there.
        if (GroundCells(zone).TryGetValue(cell, out var mine))
        {
            foreach (var (area, height) in GrassEncounterCells().GetValueOrDefault(cell) ?? [])
                if (area != zone && mine.Any(g => g.Attribute == Gen7CatchZones.GrassAttribute && Math.Abs(g.Height - height) < 50))
                    return _entries.FirstOrDefault(e => e.Id == area)?.Name ?? $"{area:000}";
        }
        int col = (int)Math.Floor(cell.X / 40.0), row = (int)Math.Floor(cell.Y / 40.0);
        var others = AreasSharingBlock(zone, col, row);
        if (others.Count == 0) return null;
        bool owns;
        lock (_catchGate)
        {
            if (!_ownsBlock.TryGetValue((zone, col, row), out owns))
                _ownsBlock[(zone, col, row)] = owns = EncounterCells(zone).Any(c => (int)Math.Floor(c.X / 40.0) == col && (int)Math.Floor(c.Y / 40.0) == row);
        }
        if (owns) return null;
        return string.Join(", ", others.Select(o => _entries.FirstOrDefault(e => e.Id == o)?.Name ?? $"{o:000}"));
    }

    public bool HasEncounterTable(int zone) => Gen7CatchZones.TableCount(TableBytes(zone)) > 0;

    /// <summary>In USUM a region always names its table, so the editor always asks which one.</summary>
    public bool RequiresTableSource(int zone) => true;

    private IReadOnlyList<MapZoneEntry>? _sources;

    /// <summary>Tables used by vanilla grass regions (mask of attribute 0x1F only), the only ones offered for new zones.</summary>
    public IReadOnlyList<MapZoneEntry> EncounterSources
    {
        get
        {
            lock (_catchGate)
            {
                if (_sources is not null) return _sources;
                uint grass = Gen7CatchZones.MaskOf(Gen7CatchZones.GrassAttribute);
                var list = new List<MapZoneEntry>();
                foreach (var e in _entries)
                {
                    byte[] ea = TableBytes(e.Id);
                    foreach (uint t in EncounterRegions(e.Id).Where(r => r.Mask == grass && Gen7CatchZones.Table(ea, (int)r.Table) is not null).Select(r => r.Table).Distinct().Order())
                        list.Add(new MapZoneEntry((e.Id * SourceStride) + (int)t, $"{e.Name} · tabla {t} (hierba)", e.Layout, "tabla de una región de hierba del juego"));
                }
                return _sources = list;
            }
        }
    }

    public IReadOnlyList<(int X, int Y)> EncounterCells(int zone)
    {
        var regions = EncounterRegions(zone);
        if (regions.Count == 0) return [];
        return GroundCells(zone).Where(c => c.Value.Any(g => regions.Any(r => r.Allows(g.Attribute)
                && r.Contains(new Vector3((c.Key.X + 0.5f) * UnitsPerCell, g.Height, (c.Key.Y + 0.5f) * UnitsPerCell)))))
            .Select(c => c.Key).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
    }

    /// <summary>The write a set of cells would need, or the reason it cannot be done.</summary>
    public (Gen7CatchPlan? Plan, string Reason) PlanCatch(int zone, IReadOnlyCollection<(int X, int Y)> cells, int? sourceZone)
    {
        if (!_worlds.ContainsKey(zone)) return (null, "Área no disponible.");
        if (cells.Count == 0) return (null, "Elige al menos una casilla.");
        if (cells.Distinct().Count() != cells.Count) return (null, "Hay casillas repetidas.");
        if (cells.Count > MaxCatchCells) return (null, $"Como máximo {MaxCatchCells} casillas por operación.");
        byte[] ed = EventBytes(zone), ea = TableBytes(zone);
        int files = Gen7CatchZones.RegionFiles(ed);
        if (files < 0) return (null, "El paquete de eventos del área no tiene la estructura verificada; no se escribe.");
        if (files != 1) return (null, $"El área tiene {files} zonas en sus regiones de encuentro; por ahora solo se escriben áreas de una zona.");
        int tables = Gen7CatchZones.TableCount(ea);
        if (tables < 0) return (null, "El paquete de tablas de encuentros del área no tiene la estructura verificada; no se escribe.");
        var regions = EncounterRegions(zone);
        if (regions.Count >= MaxRegions) return (null, $"El área ya tiene {regions.Count} regiones de encuentro.");
        var ground = GroundCells(zone);
        uint grass = Gen7CatchZones.GrassAttribute;
        foreach (var (x, y) in cells)
        {
            if (!ground.TryGetValue((x, y), out var under) || under.Count == 0) return (null, $"La casilla {x}, {y} no tiene suelo.");
            if (!under.Any(g => g.Attribute == grass))
                return (null, $"La casilla {x}, {y} no es suelo de hierba de encuentros (atributo 0x{grass:X2}); su suelo es {string.Join(", ", under.Select(g => $"0x{g.Attribute:X2}").Distinct())}. Otros suelos no están acreditados.");            if (OtherOwner(zone, (x, y)) is { } owner)
                return (null, $"La casilla {x}, {y} es terreno de {owner}: el área lo carga como escenario (o es una copia del mismo terreno) y allí el jugador está en otra área, así que no se escribe.");
        }
        if (sourceZone is not int source) return (null, "Elige la tabla de encuentros que usará la zona.");
        if (!EncounterSources.Any(e => e.Id == source)) return (null, "Esa tabla no es de una región de hierba del juego.");
        var (sourceArea, sourceTable) = SourceOf(source);
        if (Gen7CatchZones.Table(TableBytes(sourceArea), sourceTable) is not { Length: Gen7CatchZones.TableSize }) return (null, "La tabla de origen no tiene el tamaño esperado.");
        bool newTable = sourceArea != zone;
        if (newTable && tables >= MaxTables) return (null, $"El área ya tiene {tables} tablas.");
        int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X), minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);
        var heights = cells.SelectMany(c => ground[c].Where(g => g.Attribute == grass).Select(g => g.Height)).ToArray();
        var position = new Vector3((minX + maxX + 1) * UnitsPerCell / 2, heights.Average(), (minY + maxY + 1) * UnitsPerCell / 2);
        // Half-extents (see Gen7EncounterRegion.Contains); the height is the vanilla one.
        var size = new Vector3((maxX - minX + 1) * UnitsPerCell / 2, 500, (maxY - minY + 1) * UnitsPerCell / 2);
        var selected = cells.ToHashSet();
        int covered = ground.Count(c => c.Key.X >= minX && c.Key.X <= maxX && c.Key.Y >= minY && c.Key.Y <= maxY && !selected.Contains(c.Key) && c.Value.Any(g => g.Attribute == grass));
        return (new Gen7CatchPlan(zone, 0, position, size, Gen7CatchZones.MaskOf(grass), sourceArea, sourceTable, newTable, cells.Count, covered), "");
    }

    public MapMutationCheck ValidateCatchCells(int zone, IReadOnlyCollection<(int X, int Y)> cells, int? sourceZone)
    {
        var (plan, reason) = PlanCatch(zone, cells, sourceZone);
        if (plan is null) return new(false, reason);
        var inv = CultureInfo.InvariantCulture;
        string table = plan.NewTable
            ? $"Se copiará al área la tabla {plan.SourceTable} del área {plan.SourceArea:000} tal como está ahora (también si está randomizada); después se puede editar en «Encuentros»."
            : $"Usará la tabla {plan.SourceTable} que ya tiene el área.";
        string extra = plan.Covered > 0 ? $" La región es un rectángulo: también tendrán encuentros {plan.Covered} casillas de hierba dentro de él que no están marcadas." : "";
        return new(true, $"Se añadirá una región de encuentros (caja de {(plan.Size.X * 2).ToString("0", inv)} × {(plan.Size.Z * 2).ToString("0", inv)} unidades) sobre {plan.Cells} casillas de hierba 0x{Gen7CatchZones.GrassAttribute:X2}. {table}{extra} " +
            "El suelo y los modelos no cambian. No se ha probado en el juego.");
    }
}
