#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Ctr;

namespace pk3DS.Core.Maps.Gen6;

public enum Gen6Title { XY, ORAS }

/// <summary>
/// Read-only Gen6 adapter: zones from the zone GARC (XY a/0/1/2, ORAS a/0/1/3),
/// matrices from mapMatrix and blocks from mapGR. Every file is read through
/// the delegates, so the adapter never touches the project on disk.
/// </summary>
public sealed class Gen6MapBackend : IMapEditorBackend, IMapResourceProvider, IMapEventProvider, IMapMutationValidator, IMapTrainerViews, IMapNurseCreation, IMapCatchZones
{
    private readonly Func<int, byte[]> _zone, _matrix, _block;
    private readonly int _matrixCount, _blockCount;
    private readonly Dictionary<int, Gen6Zone> _zones = [];
    private readonly Dictionary<int, Gen6MapMatrix> _matrices = [];
    private readonly Dictionary<int, Gen6MapBlock?> _blocks = [];
    private readonly List<MapZoneEntry> _entries = [];
    private readonly object _gate = new();
    private readonly Func<int, byte[]>? _area;
    private readonly Func<int, byte[]>? _modelList;
    private readonly int _modelListCount;
    private readonly int _areaCount;
    private readonly Dictionary<int, Dictionary<string, H3dTexture>?> _areaTextures = [];
    private readonly Dictionary<(int Area, string Name), DecodedTexture?> _decoded = [];
    private readonly Dictionary<int, H3dModel?> _models = [];
    /// <summary>Why the last block could not be drawn, for diagnostics.</summary>
    public string? LastGraphicError { get; private set; }

    public Gen6Title Game { get; }
    public string Family => Game == Gen6Title.XY ? "gen6-xy" : "gen6-oras";
    public string Title => Game == Gen6Title.XY ? "Pokémon X / Y" : "Pokémon Rubí Omega / Zafiro Alfa";
    public IReadOnlyList<MapZoneEntry> Zones => _entries;
    public IMapResourceProvider Resources => this;
    public IMapEventProvider Events => this;
    public IMapMutationValidator? Mutations => this;
    public MapCapabilities Capabilities { get; }
    /// <summary>Zones that could not be read, with the reason; they are not listed.</summary>
    public IReadOnlyList<(int Zone, string Reason)> Skipped { get; }

    public Gen6MapBackend(Gen6Title game, int zoneCount, Func<int, byte[]> zone, int matrixCount, Func<int, byte[]> matrix,
        int blockCount, Func<int, byte[]> block, IReadOnlyList<string>? locationNames, int areaCount = 0, Func<int, byte[]>? area = null,
        int modelListCount = 0, Func<int, byte[]>? modelList = null)
    {
        Game = game; _zone = zone; _matrix = matrix; _block = block; _matrixCount = matrixCount; _blockCount = blockCount;
        _modelList = modelList; _modelListCount = modelList is null ? 0 : modelListCount;
        _area = area; _areaCount = area is null ? 0 : areaCount;
        Capabilities = new(_area is not null, true, true, (_area is not null
            ? "Modelos y texturas reales de cada bloque (vista cenital aproximada), rejilla de permisos y eventos."
            : "Rejilla de permisos y eventos.") + " Edición: posición, orientación y visión de NPC existentes, añadir enfermera y zonas de captura en X/Y.");
        var skipped = new List<(int, string)>();
        for (int i = 0; i < zoneCount; i++)
        {
            Gen6Zone z;
            try { z = new Gen6Zone(zone(i)); }
            catch (MapFormatException ex) { skipped.Add((i, ex.Message)); continue; }
            if (z.Matrix >= matrixCount) { skipped.Add((i, $"matriz {z.Matrix} inexistente")); continue; }
            _zones[i] = z;
            string place = locationNames is not null && z.ParentMap < locationNames.Count && !string.IsNullOrWhiteSpace(locationNames[z.ParentMap])
                ? locationNames[z.ParentMap] : $"Ubicación {z.ParentMap}";
            int count = z.Entities.Records.Count;
            _entries.Add(new MapZoneEntry(i, $"{i:000} · {place}", z.Matrix, $"matriz {z.Matrix} · {count} eventos"));
        }
        Skipped = skipped;
    }

    /// <summary>Decompressed bytes of a zone as read when the adapter was built.</summary>
    public byte[] ZoneBytes(int id) => (byte[])Zone(id).Pack.Raw.Clone();

    public MapMutationCheck Validate(int zone, MapMutation mutation) =>
        new(false, "Solo se admite mover NPC (posición). Los demás campos no tienen escritura verificada.");

    public MapMutationCheck ValidateMove(int zone, MapEventKind kind, int index, int x, int y)
    {
        if (kind != MapEventKind.Npc) return new(false, "Solo se pueden mover NPC; objetos, warps y activadores siguen en solo lectura.");
        if (!_zones.ContainsKey(zone)) return new(false, "Zona no disponible.");
        var view = ZoneEvents(zone).FirstOrDefault(e => e.Kind == kind && e.Index == index);
        if (view is null) return new(false, $"La zona no tiene NPC {index}.");
        if (!view.Placed) return new(false, "La posición de este NPC no está verificada; no se puede mover.");
        var layout = Layout(zone);
        if (x < 0 || y < 0 || x >= layout.Width * layout.BlockCells || y >= layout.Height * layout.BlockCells) return new(false, "La casilla está fuera de la matriz de la zona.");
        if (layout.BlockAt(x / layout.BlockCells, y / layout.BlockCells) < 0) return new(false, "La casilla está en una celda sin bloque de mapa.");
        if (view.X == x && view.Y == y) return new(false, "El NPC ya está en esa casilla.");
        var other = ZoneEvents(zone).FirstOrDefault(e => e.Kind == MapEventKind.Npc && e.Index != index && e.Placed && (int)e.X == x && (int)e.Y == y);
        if (other is not null) return new(false, $"La casilla ya la ocupa {other.Label}.");
        return new(true, "La posición se puede escribir. El juego no se ha probado con este cambio: comprueba que el NPC es accesible y que su script sigue funcionando.");
    }

    public Gen6Zone Zone(int id) => _zones.TryGetValue(id, out var z) ? z : throw new ArgumentOutOfRangeException(nameof(id), $"La zona {id} no está disponible.");

    public MapLayout Layout(int zone)
    {
        int id = Zone(zone).Matrix;
        lock (_gate)
        {
            if (!_matrices.TryGetValue(id, out var m)) _matrices[id] = m = new Gen6MapMatrix(_matrix(id));
            return m.ToLayout();
        }
    }

    public MapCellGrid? Cells(int block) => Block(block)?.Cells;

    public MapBlockImage? Graphic(int zone, int block, int size)
    {
        if (_area is null || size <= 0) return null;
        int area = Zone(zone).Area;
        H3dModel? model;
        lock (_gate)
        {
            if (!_models.TryGetValue(block, out model))
            {
                var b = Block(block);
                try { model = b is null ? null : new H3dFile(b.Model).Models.FirstOrDefault(); }
                catch (MapFormatException ex) { model = null; LastGraphicError = $"Bloque {block}: {ex.Message}"; }
                _models[block] = model;
            }
        }
        if (model is null) return null;
        var render = Gen6TopDownRenderer.Render(model, name => Texture(area, name), size);
        return new MapBlockImage(render.Size, render.Pixels, render.Triangles, render.Untextured);
    }

    /// <summary>Texture of an area by name, decoded once.</summary>
    public DecodedTexture? Texture(int area, string name)
    {
        lock (_gate)
        {
            if (_decoded.TryGetValue((area, name), out var done)) return done;
            var set = AreaTextures(area);
            DecodedTexture? result = null;
            if (set is not null && set.TryGetValue(name, out var t))
            {
                try { result = new DecodedTexture(t.Width, t.Height, t.Decode()); }
                catch (MapFormatException ex) { LastGraphicError = $"Textura {name}: {ex.Message}"; }
            }
            _decoded[(area, name)] = result;
            return result;
        }
    }

    /// <summary>Every texture of every BCH part of the area pack ("AD"); the first one with a name wins.</summary>
    public IReadOnlyDictionary<string, H3dTexture>? AreaTextures(int area)
    {
        if (_area is null || area < 0 || area >= _areaCount) return null;
        lock (_gate)
        {
            if (_areaTextures.TryGetValue(area, out var cached)) return cached;
            Dictionary<string, H3dTexture>? set = null;
            try
            {
                var pack = Gen6Pack.Read(_area(area), "AD");
                set = new Dictionary<string, H3dTexture>(StringComparer.Ordinal);
                for (int i = 0; i < pack.Count; i++)
                {
                    var part = pack.Part(i);
                    if (part.Length < 4 || part[0] != 'B' || part[1] != 'C' || part[2] != 'H') continue;
                    foreach (var tex in new H3dFile(part).Textures) set.TryAdd(tex.Name, tex);
                }
            }
            catch (MapFormatException ex) { set = null; LastGraphicError = $"Área {area}: {ex.Message}"; }
            _areaTextures[area] = set;
            return set;
        }
    }

    public Gen6MapBlock? Block(int block)
    {
        if (block < 0 || block >= _blockCount) return null;
        lock (_gate)
        {
            if (_blocks.TryGetValue(block, out var b)) return b;
            try { b = new Gen6MapBlock(_block(block)); }
            catch (MapFormatException) { b = null; }
            _blocks[block] = b;
            return b;
        }
    }

    public IReadOnlyList<MapEventView> ZoneEvents(int zone)
    {
        var e = Zone(zone).Entities;
        var layout = Layout(zone);
        var events = e.Records.Select(r => Describe(e, r)).ToArray();
        bool Inside(MapEventView v) => v.X >= 0 && v.Y >= 0 && v.X < layout.Width * layout.BlockCells && v.Y < layout.Height * layout.BlockCells;
        // Some X/Y zones (Lumiose City among them) store props and NPCs with values
        // that are not cells. When most of them fall outside the matrix, none of the
        // zone's positions is trusted.
        var anchored = events.Where(v => v.Kind is MapEventKind.Npc or MapEventKind.Prop).ToArray();
        bool zoneUntrusted = anchored.Length > 0 && anchored.Count(v => !Inside(v)) * 2 > anchored.Length;
        return events.Select(v => zoneUntrusted
            ? v with { PositionNote = "Esta zona guarda posiciones que no son casillas; su formato aún no está verificado y no se dibujan." }
            : Inside(v) ? v : v with { PositionNote = "Fuera de la matriz de bloques de la zona." }).ToArray();
    }

    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Highest sight written; the largest trainer sight seen in Alpha Sapphire is 8.</summary>
    public const int MaxSight = 8;
    internal const string FacingNote = "Editable: 0 arriba, 1 abajo, 2 izquierda, 3 derecha. Correspondencia deducida de los datos de X y Zafiro Alfa (la línea de visión de los entrenadores recorre casillas transitables); pendiente de prueba en el juego.";
    internal const string SightNote = "Editable (1–8 casillas en línea recta hacia donde mira); pendiente de prueba en el juego.";

    /// <summary>Compass angle of an NPC facing value, or null when the value has no verified direction.</summary>
    public static double? FacingDegrees(int value) => value switch { 0 => 0, 1 => 180, 2 => 270, 3 => 90, _ => null };

    /// <summary>Facing value of a compass angle; only the four verified directions.</summary>
    public static int? FacingValue(double degrees)
    {
        double d = ((degrees % 360) + 360) % 360;
        foreach (int v in new[] { 0, 1, 2, 3 })
            if (Math.Abs(d - FacingDegrees(v)!.Value) < 0.01) return v;
        return null;
    }

    public static string DirectionName(double degrees) => ((((int)Math.Round(degrees) % 360) + 360) % 360) switch
    {
        0 => "arriba", 90 => "derecha", 180 => "abajo", 270 => "izquierda", var d => $"{d}°",
    };

    private (Gen6ZoneEntities Entities, Gen6EntityRecord Record)? Npc(int zone, int index)
    {
        if (!_zones.TryGetValue(zone, out var z)) return null;
        var r = z.Entities.Records.FirstOrDefault(x => x.Kind == MapEventKind.Npc && x.Index == index);
        return r.Size == 0 ? null : (z.Entities, r);
    }

    public MapTrainerView? View(int zone, MapEventKind kind, int index)
    {
        if (kind != MapEventKind.Npc || Npc(zone, index) is not var (e, r)) return null;
        int facing = e.U16(r, 0x0C), sight = e.U16(r, 0x0E);
        bool turnable = FacingDegrees(facing) is not null && e.F32(r, 0x2C) == 0 && ZoneEvents(zone).Any(v => v.Kind == kind && v.Index == index && v.Placed);
        return new MapTrainerView(FacingDegrees(facing) ?? double.NaN, sight, turnable, turnable && sight > 0 && sight <= MaxSight, false, MaxSight,
            turnable ? FacingNote : "La orientación de este NPC no se puede escribir (valor sin dirección verificada, ángulo en 0x2C o posición no verificada).");
    }

    public MapMutationCheck ValidateView(int zone, MapEventKind kind, int index, double facing, int? sight)
    {
        if (View(zone, kind, index) is not { } view) return new(false, "Solo los NPC tienen orientación y visión.");
        if (!view.FacingWritable) return new(false, view.Note);
        if (FacingValue(facing) is null) return new(false, "Solo hay cuatro direcciones: arriba, abajo, izquierda y derecha.");
        if (sight is int s && s != view.Sight)
        {
            if (!view.SightWritable) return new(false, view.Sight == 0 ? "Este NPC no tiene visión; no se le añade." : "El alcance de este NPC no se puede escribir.");
            if (s < 1 || s > MaxSight) return new(false, $"El alcance debe estar entre 1 y {MaxSight} casillas.");
        }
        if (FacingValue(facing) == FacingValue(view.Facing) && (sight is null || sight == view.Sight)) return new(false, "No hay cambios respecto al valor guardado.");
        return new(true, "Se puede escribir. El juego no se ha probado con este cambio: comprueba que el entrenador mira hacia esa dirección y te ve a esa distancia.");
    }

    private Gen6NurseTemplate? _nurse;
    private bool _nurseSearched;
    private int _npcCeiling = -1;

    /// <summary>Nurse copied by «Añadir enfermera»: the first NPC with the nurse script, see <see cref="Gen6Nurse"/>.</summary>
    public Gen6NurseTemplate? Nurse
    {
        get
        {
            lock (_gate)
            {
                if (!_nurseSearched) { _nurse = Gen6Nurse.Find(_zones.OrderBy(z => z.Key).Select(z => (z.Key, z.Value))); _nurseSearched = true; }
                return _nurse;
            }
        }
    }

    /// <summary>Largest NPC count of any zone of this game: a new NPC never makes a zone go beyond what the game already has.</summary>
    public int NpcCeiling
    {
        get
        {
            lock (_gate)
            {
                if (_npcCeiling < 0) _npcCeiling = _zones.Values.Select(z => z.Entities.Records.Count(r => r.Kind == MapEventKind.Npc)).DefaultIfEmpty(0).Max();
                return _npcCeiling;
            }
        }
    }

    /// <summary>Model list of an area (see <see cref="Gen6AreaModels"/>), or null when the game files were not given or the area has none.</summary>
    public byte[]? ModelList(int area) => _modelList is not null && area >= 0 && area < _modelListCount ? (byte[])_modelList(area).Clone() : null;

    private byte[]? _nurseEntry;
    private int _modelCeiling = -1;

    /// <summary>The nurse's entry in the model lists (identical in every list that has it), or null.</summary>
    public byte[]? NurseModelEntry
    {
        get
        {
            if (Nurse is not { } n || _modelList is null) return null;
            lock (_gate)
            {
                if (_modelCeiling < 0)
                {
                    var lists = Enumerable.Range(0, _modelListCount).Select(_modelList).ToArray();
                    _nurseEntry = Gen6AreaModels.Entry(lists, n.Model);
                    _modelCeiling = lists.Select(l => l.Length / Gen6AreaModels.EntrySize).DefaultIfEmpty(0).Max();
                }
                return _nurseEntry is null ? null : (byte[])_nurseEntry.Clone();
            }
        }
    }

    /// <summary>Largest model list of the game; a list never grows beyond it.</summary>
    public int ModelListCeiling { get { _ = NurseModelEntry; return _modelCeiling; } }

    /// <summary>Whether the area of <paramref name="zone"/> already loads the nurse's model (null when it cannot be read).</summary>
    public bool? AreaLoadsNurse(int zone)
    {
        if (Nurse is not { } n || ModelList(Zone(zone).Area) is not { } list) return null;
        return Gen6AreaModels.Models(list).Contains(n.Model);
    }

    public string NurseSummary => Nurse is { } n
        ? $"Copia la enfermera del Centro Pokémon de la zona {n.Zone:000} (NPC {n.Index}, modelo {n.Model}, script {Gen6Nurse.Script}; {n.Copies} enfermeras con ese script en el juego). " +
          "El script es el del Centro Pokémon: saludo, pregunta, curación y despedida."
        : $"No se encontró ninguna enfermera (script {Gen6Nurse.Script}) con un modelo único y quieta; no se puede añadir.";

    public MapMutationCheck ValidateAddNurse(int zone, int x, int y, double facing)
    {
        if (Nurse is not { } nurse) return new(false, NurseSummary);
        if (!_zones.ContainsKey(zone)) return new(false, "Zona no disponible.");
        var events = ZoneEvents(zone);
        if (events.Any(e => e.PositionNote?.Contains("no son casillas", StringComparison.Ordinal) == true))
            return new(false, "Esta zona guarda posiciones que no son casillas; no se añaden NPC hasta verificar su formato.");
        if (FacingValue(facing) is null) return new(false, "Solo hay cuatro direcciones: arriba, abajo, izquierda y derecha.");
        var layout = Layout(zone);
        if (x < 0 || y < 0 || x >= layout.Width * layout.BlockCells || y >= layout.Height * layout.BlockCells) return new(false, "La casilla está fuera de la matriz de la zona.");
        int block = layout.BlockAt(x / layout.BlockCells, y / layout.BlockCells);
        if (block < 0) return new(false, "La casilla está en una celda sin bloque de mapa.");
        var taken = events.FirstOrDefault(e => e.Placed && x >= e.X && x < e.X + e.Width && y >= e.Y && y < e.Y + e.Height);
        if (taken is not null) return new(false, $"La casilla ya la ocupa {taken.Label}.");
        int count = events.Count(e => e.Kind == MapEventKind.Npc);
        if (count + 1 > NpcCeiling) return new(false, $"La zona ya tiene {count} NPC; con uno más superaría el máximo que usa el juego ({NpcCeiling}).");
        int area = Zone(zone).Area;
        if (ModelList(area) is not { } models) return new(false, $"No se puede leer la lista de modelos del área {area}; sin el modelo de la enfermera en ella el juego se congela al entrar en la zona.");
        if (NurseModelEntry is null) return new(false, $"No se encontró una entrada única del modelo {nurse.Model} en las listas de modelos; no se añade.");
        string modelNote = "";
        if (AreaLoadsNurse(zone) == false)
        {
            int entries = models.Length / Gen6AreaModels.EntrySize;
            if (entries + 1 > ModelListCeiling) return new(false, $"La lista de modelos del área {area} ya tiene {entries} modelos; con uno más superaría el máximo del juego ({ModelListCeiling}).");
            int sharing = _zones.Values.Count(z => z.Area == area);
            modelNote = $" Se añadirá el modelo {nurse.Model} a la lista de modelos del área {area} ({sharing} zona{(sharing == 1 ? "" : "s")} la usa{(sharing == 1 ? "" : "n")}).";
        }
        string ground = "";
        if (Cells(block) is { } grid)
        {
            uint Value(int cx, int cy) => grid.Cells[((cy % layout.BlockCells) * grid.Width) + (cx % layout.BlockCells)];
            uint value = Value(x, y);
            int same = events.Count(e => e.Kind == MapEventKind.Npc && e.Placed && layout.BlockAt((int)e.X / layout.BlockCells, (int)e.Y / layout.BlockCells) is int b && b >= 0
                && Cells(b) is { } g && g.Cells[(((int)e.Y % layout.BlockCells) * g.Width) + ((int)e.X % layout.BlockCells)] == value);
            ground = $" Permiso de la casilla 0x{value.ToString("X8", CultureInfo.InvariantCulture)}: " + (same > 0 ? $"{same} NPC de esta zona están sobre casillas con el mismo valor." : "ningún NPC de esta zona está sobre una casilla con ese valor; comprueba que se puede llegar a hablar con ella.");
        }
        return new(true, $"Se puede añadir como NPC {count} (copia de la enfermera de la zona {nurse.Zone:000}).{modelNote}{ground} No se ha probado en el juego fuera de un Centro Pokémon: comprueba que aparece, que se puede hablar con ella y que cura al equipo.");
    }

    private Gen6MapMatrix MatrixOf(int zone)
    {
        int id = Zone(zone).Matrix;
        lock (_gate)
        {
            if (!_matrices.TryGetValue(id, out var m)) _matrices[id] = m = new Gen6MapMatrix(_matrix(id));
            return m;
        }
    }

    /// <summary>Decompressed bytes of a map block as read when the adapter was built, or null when it cannot be read.</summary>
    public byte[]? BlockBytes(int block) => Block(block) is { } b ? (byte[])b.Pack.Raw.Clone() : null;

    private Dictionary<int, int>? _blockUses;

    /// <summary>Positions where a block appears in all the matrices of the game (a painted block changes every one of them).</summary>
    public int BlockUses(int block)
    {
        lock (_gate)
        {
            if (_blockUses is null)
            {
                var uses = new Dictionary<int, int>();
                for (int m = 0; m < _matrixCount; m++)
                {
                    Gen6MapMatrix matrix;
                    try { matrix = new Gen6MapMatrix(_matrix(m)); } catch (MapFormatException) { continue; }
                    foreach (int b in matrix.Blocks.Where(b => b >= 0)) uses[b] = uses.GetValueOrDefault(b) + 1;
                }
                _blockUses = uses;
            }
            return _blockUses.GetValueOrDefault(block);
        }
    }

    /// <summary>
    /// Zone whose encounter table applies on cell (x, y): the zone map of an exterior matrix, or the zone itself
    /// when it is the only one using a matrix without zone map. Null when it cannot be decided.
    /// </summary>
    public int? CellOwner(int zone, int x, int y)
    {
        var matrix = MatrixOf(zone);
        if (matrix.ZoneAt(x, y) is int owner) return owner;
        int matrixId = Zone(zone).Matrix;
        return _zones.Values.Count(z => z.Matrix == matrixId) == 1 ? zone : null;
    }

    private uint? CellValue(MapLayout layout, int x, int y)
    {
        if (x < 0 || y < 0 || x >= layout.Width * layout.BlockCells || y >= layout.Height * layout.BlockCells) return null;
        int block = layout.BlockAt(x / layout.BlockCells, y / layout.BlockCells);
        if (block < 0 || Cells(block) is not { } g) return null;
        return g.Cells[((y % layout.BlockCells) * g.Width) + (x % layout.BlockCells)];
    }

    public bool HasEncounterTable(int zone)
    {
        var pack = Zone(zone).Pack;
        return pack.Count > Gen6CatchZones.TablePart && pack.Length(Gen6CatchZones.TablePart) > 0;
    }

    /// <summary>Encounter table of a zone (part 3 of its pack), or null.</summary>
    public byte[]? EncounterTable(int zone) => Gen6CatchZones.Table(ZoneBytes(zone));

    public IReadOnlyList<MapZoneEntry> EncounterSources =>
        _entries.Where(e => Zone(e.Id).Pack is var p && p.Count > Gen6CatchZones.TablePart && p.Length(Gen6CatchZones.TablePart) == Gen6CatchZones.TableSize).ToArray();

    public IReadOnlyList<(int X, int Y)> EncounterCells(int zone)
    {
        var layout = Layout(zone);
        var cells = new List<(int, int)>();
        for (int by = 0; by < layout.Height; by++)
            for (int bx = 0; bx < layout.Width; bx++)
            {
                int block = layout.BlockAt(bx, by);
                if (block < 0 || Cells(block) is not { } g) continue;
                for (int cy = 0; cy < g.Height; cy++)
                    for (int cx = 0; cx < g.Width; cx++)
                    {
                        if (g.Cells[(cy * g.Width) + cx] != Gen6CatchZones.EncounterGrass) continue;
                        int x = (bx * layout.BlockCells) + cx, y = (by * layout.BlockCells) + cy;
                        if (CellOwner(zone, x, y) == zone) cells.Add((x, y));
                    }
            }
        return cells;
    }

    /// <summary>Most cells written in one operation.</summary>
    public const int MaxCatchCells = 1600;

    public MapMutationCheck ValidateCatchCells(int zone, IReadOnlyCollection<(int X, int Y)> cells, int? sourceZone)
    {
        if (Game != Gen6Title.XY) return new(false, "Las zonas de captura solo están verificadas en X/Y. Rubí Omega/Zafiro Alfa usa otro formato de tabla de encuentros.");
        if (!_zones.ContainsKey(zone)) return new(false, "Zona no disponible.");
        if (cells.Count == 0) return new(false, "Elige al menos una casilla.");
        if (cells.Distinct().Count() != cells.Count) return new(false, "Hay casillas repetidas.");
        if (cells.Count > MaxCatchCells) return new(false, $"Como máximo {MaxCatchCells} casillas por operación.");
        var events = ZoneEvents(zone);
        if (events.Any(e => e.PositionNote?.Contains("no son casillas", StringComparison.Ordinal) == true))
            return new(false, "Esta zona guarda posiciones que no son casillas; su formato aún no está verificado.");
        var layout = Layout(zone);
        var blocks = new HashSet<int>();
        foreach (var (x, y) in cells)
        {
            if (CellValue(layout, x, y) is not uint value) return new(false, $"La casilla {x}, {y} está fuera de la matriz o en una celda sin bloque.");
            if (value != Gen6CatchZones.Ground)
                return new(false, $"La casilla {x}, {y} no es suelo normal (permiso 0x{value.ToString("X8", CultureInfo.InvariantCulture)}); solo se convierte el suelo 0x{Gen6CatchZones.Ground:X8}.");
            int? owner = CellOwner(zone, x, y);
            if (owner is null) return new(false, "No se puede saber qué zona usa estas casillas: la matriz no tiene mapa de zonas y la comparten varias zonas.");
            if (owner != zone) return new(false, $"La casilla {x}, {y} pertenece a la zona {owner} en el mapa de zonas; sus encuentros serían los de esa zona.");
            var taken = events.FirstOrDefault(e => e.Placed && x >= e.X && x < e.X + e.Width && y >= e.Y && y < e.Y + e.Height);
            if (taken is not null) return new(false, $"La casilla {x}, {y} la ocupa {taken.Label}.");
            int block = layout.BlockAt(x / layout.BlockCells, y / layout.BlockCells);
            if (blocks.Add(block) && BlockUses(block) != 1) return new(false, $"El bloque {block} aparece en {BlockUses(block)} posiciones de las matrices del juego; cambiarlo afectaría a todas.");
        }
        string table;
        if (HasEncounterTable(zone))
        {
            if (Zone(zone).Pack.Length(Gen6CatchZones.TablePart) != Gen6CatchZones.TableSize) return new(false, "La tabla de encuentros de la zona tiene un tamaño inesperado.");
            table = "Usará la tabla de encuentros actual de la zona.";
        }
        else
        {
            if (sourceZone is not int source) return new(false, "La zona no tiene tabla de encuentros: elige la zona de la que se copiará.");
            if (source == zone || !_zones.ContainsKey(source) || !EncounterSources.Any(e => e.Id == source)) return new(false, $"La zona {source} no tiene una tabla de encuentros que se pueda copiar.");
            table = $"La zona no tiene encuentros: se creará su tabla copiando la de la zona {source:000} tal como está ahora (también si está randomizada); después se puede editar en «Encuentros».";
        }
        int already = EncounterCells(zone).Count;
        return new(true, $"Se pueden convertir {cells.Count} casillas de suelo normal en suelo con encuentros (0x{Gen6CatchZones.EncounterGrass:X8}) en {blocks.Count} bloque{(blocks.Count == 1 ? "" : "s")}; la zona ya tenía {already}. {table} " +
            "La hierba no se verá: el modelo del mapa no cambia. No se ha probado en el juego.");
    }

    public IReadOnlyList<(int X, int Y)> SightCells(int zone, MapEventKind kind, int index, double facing, int sight)
    {
        var view = ZoneEvents(zone).FirstOrDefault(v => v.Kind == kind && v.Index == index);
        if (view is not { Placed: true } || FacingValue(facing) is not int f || sight <= 0) return [];
        var layout = Layout(zone);
        (int dx, int dy) = f switch { 0 => (0, -1), 1 => (0, 1), 2 => (-1, 0), _ => (1, 0) };
        var cells = new List<(int, int)>();
        for (int k = 1; k <= Math.Min(sight, 64); k++)
        {
            int x = (int)view.X + (dx * k), y = (int)view.Y + (dy * k);
            if (x < 0 || y < 0 || x >= layout.Width * layout.BlockCells || y >= layout.Height * layout.BlockCells) break;
            cells.Add((x, y));
        }
        return cells;
    }
    private static string Hex(int v) => "0x" + v.ToString("X4", CultureInfo.InvariantCulture);

    private MapEventView Describe(Gen6ZoneEntities e, Gen6EntityRecord r)
    {
        var p = new List<MapEventProperty>();
        switch (r.Kind)
        {
            case MapEventKind.Npc:
            {
                int x = e.U16(r, 0x28), y = e.U16(r, 0x2A);
                p.Add(new("ID", N(e.U16(r, 0x00))));
                p.Add(new("Modelo", N(e.U16(r, 0x02))));
                p.Add(new("Movimiento", N(e.U16(r, 0x04))));
                p.Add(new("Bandera de aparición", Hex(e.U16(r, 0x08))));
                p.Add(new("Script", N(e.U16(r, 0x0A))));
                int facing = e.U16(r, 0x0C), sight = e.U16(r, 0x0E);
                bool turnable = FacingDegrees(facing) is not null && e.F32(r, 0x2C) == 0;
                p.Add(new("Orientación", FacingDegrees(facing) is double deg ? $"{DirectionName(deg)} (valor {N(facing)})" : $"valor {N(facing)} (sin verificar)", Writable: turnable,
                    Note: turnable ? FacingNote : FacingDegrees(facing) is null ? "Solo los valores 0–3 tienen dirección verificada; los demás quedan en solo lectura." : "Este NPC tiene un ángulo en 0x2C; no se cambia su orientación hasta verificar cómo se combinan."));
                p.Add(new("Alcance de visión", sight == 0 ? "0 (no busca al jugador)" : $"{N(sight)} casillas", Writable: turnable && sight > 0 && sight <= MaxSight,
                    Note: sight == 0 ? "Sin visión: no se le añade porque no se ha verificado qué script de combate usaría." : SightNote));
                p.Add(new("Casilla", $"{N(x)}, {N(y)}", Writable: true, Note: "Editable: se puede mover a otra casilla con bloque de la zona."));
                p.Add(new("Ángulo", e.F32(r, 0x2C).ToString("0.##", CultureInfo.InvariantCulture) + "°"));
                return new(r.Kind, r.Index, x, y, 1, 1, null, $"NPC {r.Index} · modelo {e.U16(r, 0x02)}", p);
            }
            case MapEventKind.Prop:
            {
                int x = e.U16(r, 0x08), y = e.U16(r, 0x0A), w = e.S16(r, 0x0C), h = e.S16(r, 0x0E);
                p.Add(new("Script", N(e.U16(r, 0x00))));
                p.Add(new("Casilla", $"{N(x)}, {N(y)}"));
                p.Add(new("Tamaño", $"{N(w)} × {N(h)}"));
                return new(r.Kind, r.Index, x, y, Math.Max(1, w), Math.Max(1, h), null, $"Objeto {r.Index} · script {e.U16(r, 0x00)}", p);
            }
            case MapEventKind.Warp:
            {
                int rawX = e.S16(r, 0x08), rawZ = e.S16(r, 0x0A), rawY = e.S16(r, 0x0C), w = e.S16(r, 0x0E), h = e.S16(r, 0x10);
                int type = e.U16(r, 0x06);
                bool cell = type == 0 && rawX % 18 == 9 && rawY % 18 == 9;
                p.Add(new("Zona de destino", N(e.U16(r, 0x00))));
                p.Add(new("Warp de destino", N(e.U16(r, 0x02))));
                p.Add(new("Tipo de coordenadas", N(type), Note: cell ? null : "Coordenadas fuera de la rejilla de casillas: la posición mostrada es aproximada."));
                p.Add(new("Posición (1/18 casilla)", $"{N(rawX)}, {N(rawY)} · altura {N(rawZ)}"));
                p.Add(new("Tamaño", $"{N(w)} × {N(h)}"));
                return new(r.Kind, r.Index, Math.Floor(rawX / 18.0), Math.Floor(rawY / 18.0), Math.Max(1, w), Math.Max(1, h), null,
                    $"Warp {r.Index} → zona {e.U16(r, 0x00)}", p);
            }
            default:
            {
                int x = e.U16(r, 0x0C), y = e.U16(r, 0x0E), w = e.S16(r, 0x10), h = e.S16(r, 0x12);
                p.Add(new("Script", N(e.U16(r, 0x00))));
                p.Add(new("Constante", N(e.U16(r, 0x04))));
                p.Add(new("Casilla", $"{N(x)}, {N(y)}"));
                p.Add(new("Tamaño", $"{N(w)} × {N(h)}"));
                string what = r.Kind == MapEventKind.Trigger ? "Activador" : "Activador de zona";
                return new(r.Kind, r.Index, x, y, Math.Max(1, w), Math.Max(1, h), null, $"{what} {r.Index} · script {e.U16(r, 0x00)}", p);
            }
        }
    }

    /// <summary>RomFS path of the model lists per area.</summary>
    public static string ModelListArchive(bool xy) => xy ? "a/1/4/9" : "a/1/3/7";

    /// <summary>Builds the adapter from a 3DS project configuration.</summary>
    public static Gen6MapBackend FromConfig(GameConfig config)
    {
        if (!config.XY && !config.ORAS) throw new NotSupportedException("El adaptador de mapas Gen6 solo admite X/Y y Rubí Omega/Zafiro Alfa.");
        var zones = config.GetlzGARCData("encdata");
        var matrices = config.GetlzGARCData("mapMatrix");
        var blocks = config.GetlzGARCData("mapGR");
        // Area data packs ("AD"): XY a/0/1/3, ORAS a/0/1/4, right after the zone GARC. Checked on real copies.
        var areas = new GARC.LazyGARC(System.IO.File.ReadAllBytes(System.IO.Path.Combine(config.RomFS, config.XY ? "a/0/1/3" : "a/0/1/4")));
        string[]? names = null;
        try { names = config.GetText(TextName.metlist_000000); } catch (Exception) { }
        // Model lists per area: XY a/1/4/9, ORAS a/1/3/7. Members are raw 24-byte entries and are read as stored (a list
        // whose first model is 17 starts with 0x11 and must not be taken for LZ11). Checked on X and Alpha Sapphire.
        var models = new GARC.MemGARC(System.IO.File.ReadAllBytes(System.IO.Path.Combine(config.RomFS, ModelListArchive(config.XY)))).Files;
        return new Gen6MapBackend(config.XY ? Gen6Title.XY : Gen6Title.ORAS,
            zones.FileCount, i => zones[i], matrices.FileCount, i => matrices[i], blocks.FileCount, i => blocks[i], names, areas.FileCount, i => areas[i],
            models.Length, i => models[i]);
    }
}
