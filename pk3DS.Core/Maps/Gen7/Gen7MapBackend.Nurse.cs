#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using pk3DS.Core.Maps.Gen6;

namespace pk3DS.Core.Maps.Gen7;

/// <summary>What adding a nurse would write: the record's place, its event id and whether the area must load her model.</summary>
/// <param name="Talk">Centre of the talk range (where the player stands to talk to her).</param>
public sealed record Gen7NursePlan(int Area, int ZoneFile, Vector3 Position, double Compass, uint EventId, Vector3 Talk, bool AddsCharacter, int CharacterBytes, int Characters);

/// <summary>Pokémon Center nurse (see <see cref="Gen7Nurse"/>).</summary>
public sealed partial class Gen7MapBackend : IMapNurseCreation
{
    /// <summary>Character pack of an area (slot 3, decompressed).</summary>
    public byte[] CharacterBytes(int area) => (byte[])_world((area * SlotsPerWorld) + CharacterSlot).Clone();
    public const int CharacterSlot = 3;

    private Gen7NurseTemplate? _nurse;
    private string? _nurseReason;

    /// <summary>
    /// The nurse of the game's Pokémon Centers: every NPC with model 66 and script 5030 must be the same record (apart from
    /// its event id and shape offsets) with the same shapes around her, and every area that has one must hold the same
    /// "CP" pack of model 66; otherwise nothing is offered.
    /// </summary>
    public Gen7NurseTemplate? NurseTemplate
    {
        get
        {
            lock (_catchGate)
            {
                if (_nurse is not null || _nurseReason is not null) return _nurse;
                (_nurse, _nurseReason) = FindNurse();
                return _nurse;
            }
        }
    }

    private (Gen7NurseTemplate?, string?) FindNurse()
    {
        byte[]? record = null, talk = null, body = null, character = null;
        int count = 0;
        foreach (int area in _worlds.Keys.Order())
        {
            byte[] ed;
            try { ed = EventBytes(area); } catch (Exception ex) when (ex is MapFormatException or ArgumentException) { continue; }
            byte[][] files;
            try
            {
                var parts = Gen7CatchZones.Parts(ed, "ED");
                if (parts.Length <= Gen7Events.NpcPack || parts[Gen7Events.NpcPack].Length < 8) continue;
                files = Gen7CatchZones.Parts(parts[Gen7Events.NpcPack], System.Text.Encoding.ASCII.GetString(parts[Gen7Events.NpcPack], 0, 2));
            }
            catch (MapFormatException) { continue; }
            bool here = false;
            foreach (var file in files)
            {
                if (file.Length < 4) continue;
                foreach (var r in Gen7Events.Records(file, Gen7ZoneEdit.NpcSize, Gen7ZoneEdit.NpcType))
                {
                    if (r.U32(Gen7Nurse.ModelField) != Gen7Nurse.Model || r.U32(Gen7Nurse.ScriptField) != Gen7Nurse.Script) continue;
                    // Each nurse in her own frame: position and turn undone (a nurse added by the editor is the same nurse
                    // elsewhere), so the talk range is compared where it would be for a nurse without rotation.
                    var pos = new Vector3(r.F(Gen7Nurse.Position), r.F(Gen7Nurse.Position + 4), r.F(Gen7Nurse.Position + 8));
                    var rot = new Quaternion(r.F(Gen7Nurse.Rotation), r.F(Gen7Nurse.Rotation + 4), r.F(Gen7Nurse.Rotation + 8), r.F(Gen7Nurse.Rotation + 12));
                    if (rot.X != 0 || rot.Z != 0 || Math.Abs(rot.LengthSquared() - 1) > 1e-3f) return (null, $"Una enfermera del área {area:000} no está girada solo sobre el eje vertical; no se ofrece copiarlas.");
                    var talkShapes = Gen7Nurse.ShapeList(file, (int)r.U32(Gen7ZoneEdit.NpcTalkShape));
                    var talkPoint = Gen7Nurse.FirstPoint(talkShapes);
                    var local = Vector3.Transform(talkPoint - pos, Quaternion.Inverse(rot));
                    var t = Gen7Nurse.MoveShapes(talkShapes, local - talkPoint);
                    var b = Gen7Nurse.MoveShapes(Gen7Nurse.ShapeList(file, (int)r.U32(Gen7ZoneEdit.NpcBodyShape)), -pos);
                    var raw = (byte[])r.Bytes.Clone();
                    for (int k = 0; k < 28; k++) raw[Gen7Nurse.Position + k] = 0; // position and rotation are the placement, not the nurse
                    if (record is null) { record = raw; talk = t; body = b; }
                    else
                    {
                        bool same = Enumerable.Range(0, Gen7ZoneEdit.NpcSize).All(i => (i >= Gen7Nurse.EventId && i < Gen7Nurse.EventId + 4) || i >= Gen7ZoneEdit.NpcTalkShape || raw[i] == record[i]);
                        if (!same || !SimilarShapes(t, talk!) || !SimilarShapes(b, body!))
                        {
                            string what = !same ? $"campos (byte {Enumerable.Range(0, Gen7ZoneEdit.NpcTalkShape).First(i => (i < Gen7Nurse.EventId || i >= Gen7Nurse.EventId + 4) && raw[i] != record[i]):X2})" : !t.SequenceEqual(talk!) ? "forma de conversación" : "forma del cuerpo";
                            return (null, $"Las enfermeras del juego no son todas iguales (área {area:000}: {what}); no se ofrece copiarlas.");
                        }
                    }
                    count++; here = true;
                }
            }
            if (!here) continue;
            try
            {
                var cp = Gen7CatchZones.Parts(CharacterBytes(area), "AC").FirstOrDefault(p => Gen7Nurse.CharacterName(p) == Gen7Nurse.Model.ToString(CultureInfo.InvariantCulture));
                if (cp is null) return (null, $"El área {area:000} tiene enfermera pero no carga su modelo; no se ofrece.");
                if (character is null) character = cp;
                else if (!cp.AsSpan().SequenceEqual(character)) return (null, "El modelo de la enfermera no es igual en todos los Centros; no se ofrece.");
            }
            catch (MapFormatException ex) { return (null, "No se pudo leer el modelo de la enfermera: " + ex.Message); }
        }
        if (record is null || character is null) return (null, "El juego no tiene enfermeras de Centro Pokémon reconocibles (modelo 66, script 5030).");
        // The template stands at the origin without rotation (written again by Gen7Nurse.Place).
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(Gen7Nurse.Rotation + 12), 1);
        return (new Gen7NurseTemplate(record, talk!, body!, character, Gen7Nurse.Model, Gen7Nurse.Script, count), null);
    }

    /// <summary>
    /// Same shape list apart from small placement differences: same count, types and every non-point value, and points
    /// within <see cref="ShapeTolerance"/> units (three Centers of Ultra Moon have the talk range 2.3 units to the side).
    /// </summary>
    private static bool SimilarShapes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        HashSet<int> point = [];
        var padded = new byte[a.Length + 4]; a.CopyTo(padded, 4);
        foreach (var (at, bytes) in Gen7ZoneEdit.ShapePoints(padded, 4)) for (int k = 0; k < bytes; k++) point.Add(at - 4 + k);
        for (int i = 0; i < a.Length; i += 4)
        {
            if (!point.Contains(i)) { if (!a.AsSpan(i, 4).SequenceEqual(b.AsSpan(i, 4))) return false; continue; }
            if (Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(a.AsSpan(i)) - BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(i))) > ShapeTolerance) return false;
        }
        return true;
    }

    public const float ShapeTolerance = 5f;

    public string NurseSummary => NurseTemplate is { } n
        ? $"Copia la enfermera de Centro Pokémon del propio juego ({n.Count} enfermeras idénticas, modelo {n.Model}) con la curación de Lylia del juego (rutina común {Gen7Nurse.HealScript}: fundido, sintonía y equipo curado, sin máquina ni mostrador; dice las frases de Lylia). Si el área no carga su modelo, se añade a los modelos de personaje del área."
        : _nurseReason ?? "No hay enfermera que copiar.";

    public MapMutationCheck ValidateAddNurse(int zone, int x, int y, double facing)
    {
        var (plan, reason) = PlanNurse(zone, x, y, facing);
        if (plan is null) return new(false, reason);
        var inv = CultureInfo.InvariantCulture;
        string model = plan.AddsCharacter
            ? $" El área no carga el modelo de la enfermera: se añadirá a sus modelos de personaje ({plan.Characters} modelos, {plan.CharacterBytes / 1024} KB; el juego llega a {Gen7Nurse.MaxCharacters} y {Gen7Nurse.MaxCharacterBytes / 1024} KB)."
            : " El área ya carga el modelo de la enfermera.";
        return new(true, $"Se añadirá una enfermera (evento {plan.EventId}) en la casilla {x}, {y}, a altura {plan.Position.Y.ToString("0.#", inv)}, mirando hacia {TrainerDirection(plan.Compass)}; se le habla desde la casilla {Math.Floor(plan.Talk.X / UnitsPerCell)}, {Math.Floor(plan.Talk.Z / UnitsPerCell)}.{model} " +
            $"Cura con la rutina común {Gen7Nurse.HealScript} del juego (la curación de Lylia, con sus frases).");
    }

    private readonly Dictionary<int, List<(Vector2 A, Vector2 B)>> _walls = [];

    /// <summary>
    /// Walls of the area seen from above: the vertical triangles of block part 7 (a «BG» pack whose sub-meshes have the
    /// collision layout of part 6; in Pueblo Lilii sub-mesh 0 holds the fences, all of its triangles vertical), each
    /// reduced to the segment between its two farthest points on the ground plane.
    /// </summary>
    public IReadOnlyList<(Vector2 A, Vector2 B)> WallSegments(int area)
    {
        lock (_catchGate) if (_walls.TryGetValue(area, out var cached)) return cached;
        var list = new List<(Vector2, Vector2)>();
        if (_worlds.TryGetValue(area, out var w))
            foreach (int id in w.Blocks.Where(b => b >= 0 && b < _chunkCount).Distinct())
            {
                try
                {
                    var data = _chunk(id);
                    if (data.Length < 8 || data[0] != 'B' || data[1] != 'G') continue;
                    var pack = Gen6Pack.Read(data, "BG");
                    if (pack.Count <= 7 || pack.Length(7) < 8) continue;
                    var p7 = pack.Part(7).ToArray();
                    if (p7[0] != 'B' || p7[1] != 'G') continue;
                    var subs = Gen6Pack.Read(p7, "BG");
                    for (int k = 0; k < subs.Count; k++)
                    {
                        if (subs.Length(k) < 8 || BinaryPrimitives.ReadUInt32LittleEndian(subs.Part(k)) != Gen7Collision.Magic) continue;
                        foreach (var t in Gen7Collision.Read(subs.Part(k)))
                        {
                            var n = Vector3.Cross(t.B - t.A, t.C - t.A);
                            if (n.LengthSquared() <= 0 || Math.Abs(n.Y) / n.Length() >= 0.3f) continue;
                            Vector2[] pts = [new(t.A.X, t.A.Z), new(t.B.X, t.B.Z), new(t.C.X, t.C.Z)];
                            var best = (pts[0], pts[1]);
                            foreach (var (i, j) in new[] { (0, 2), (1, 2) })
                                if (Vector2.DistanceSquared(pts[i], pts[j]) > Vector2.DistanceSquared(best.Item1, best.Item2)) best = (pts[i], pts[j]);
                            if (Vector2.DistanceSquared(best.Item1, best.Item2) > 1) list.Add(best);
                        }
                    }
                }
                catch (MapFormatException) { }
            }
        lock (_catchGate) _walls[area] = list;
        return list;
    }

    /// <summary>
    /// Read-only audit of the reachability rule on the game's own NPCs: for every NPC of the area with a talk range, whether
    /// the cell of that range is reachable on foot (null when the area has no warp arrival on ground).
    /// </summary>
    public (int Npcs, int Reachable, List<string> Unreachable)? ReachabilityAudit(int area)
    {
        var ground = GroundCells(area);
        var reach = Reachable(area, ground, WallSegments(area));
        if (reach is null) return null;
        int n = 0, ok = 0; var bad = new List<string>();
        byte[][] files;
        try
        {
            var parts = Gen7CatchZones.Parts(EventBytes(area), "ED");
            files = Gen7CatchZones.Parts(parts[Gen7Events.NpcPack], System.Text.Encoding.ASCII.GetString(parts[Gen7Events.NpcPack], 0, 2));
        }
        catch (Exception ex) when (ex is MapFormatException or IndexOutOfRangeException or ArgumentException) { return null; }
        foreach (var file in files)
        {
            if (file.Length < 4) continue;
            foreach (var r in Gen7Events.Records(file, Gen7ZoneEdit.NpcSize, Gen7ZoneEdit.NpcType))
            {
                int off = (int)r.U32(Gen7ZoneEdit.NpcTalkShape);
                if (off <= 0 || off >= file.Length) continue;
                Vector3 tp;
                try { tp = Gen7Nurse.FirstPoint(Gen7Nurse.ShapeList(file, off)); } catch (Exception ex) when (ex is MapFormatException or ArgumentException or IndexOutOfRangeException) { continue; }
                var cell = ((int)Math.Floor(tp.X / UnitsPerCell), (int)Math.Floor(tp.Z / UnitsPerCell));
                if (!ground.ContainsKey(cell)) continue;
                n++;
                if (reach.Contains(cell)) ok++; else bad.Add($"evento {r.U32(Gen7Nurse.EventId)} modelo {r.U32(Gen7Nurse.ModelField)} en {cell}");
            }
        }
        return (n, ok, bad);
    }

    public static bool Crosses(Vector2 p, Vector2 q, Vector2 a, Vector2 b)
    {
        static float Side(Vector2 o, Vector2 u, Vector2 v) => ((u.X - o.X) * (v.Y - o.Y)) - ((u.Y - o.Y) * (v.X - o.X));
        float d1 = Side(a, b, p), d2 = Side(a, b, q), d3 = Side(p, q, a), d4 = Side(p, q, b);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    /// <summary>
    /// Cells reachable on foot from where the player arrives (the arrival point of every warp of the area), moving between
    /// neighbouring cells with ground without crossing a wall; null when the area has no warp arrival on ground.
    /// </summary>
    private HashSet<(int X, int Y)>? Reachable(int area, Dictionary<(int X, int Y), List<(uint Attribute, float Height)>> ground, IReadOnlyList<(Vector2 A, Vector2 B)> walls)
    {
        var seeds = Warps(area).Select(wp => ((int)Math.Floor(wp.Arrival.X / UnitsPerCell), (int)Math.Floor(wp.Arrival.Z / UnitsPerCell))).Where(ground.ContainsKey).Distinct().ToList();
        if (seeds.Count == 0) return null;
        // Walls indexed by the cells their bounding box touches, to test each step against the nearby ones only.
        var near = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < walls.Count; i++)
        {
            var (a, b) = walls[i];
            for (int x = (int)Math.Floor(Math.Min(a.X, b.X) / UnitsPerCell) - 1; x <= (int)Math.Floor(Math.Max(a.X, b.X) / UnitsPerCell) + 1; x++)
                for (int y = (int)Math.Floor(Math.Min(a.Y, b.Y) / UnitsPerCell) - 1; y <= (int)Math.Floor(Math.Max(a.Y, b.Y) / UnitsPerCell) + 1; y++)
                {
                    if (!near.TryGetValue((x, y), out var l)) near[(x, y)] = l = [];
                    l.Add(i);
                }
        }
        var seen = new HashSet<(int X, int Y)>(seeds);
        var queue = new Queue<(int X, int Y)>(seeds);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            var from = new Vector2((c.X + 0.5f) * UnitsPerCell, (c.Y + 0.5f) * UnitsPerCell);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (c.X + dx, c.Y + dy);
                if (seen.Contains(n) || !ground.ContainsKey(n)) continue;
                var to = new Vector2((n.Item1 + 0.5f) * UnitsPerCell, (n.Item2 + 0.5f) * UnitsPerCell);
                if (near.TryGetValue(c, out var idx) && idx.Any(i => Crosses(from, to, walls[i].A, walls[i].B))) continue;
                seen.Add(n); queue.Enqueue(n);
            }
        }
        return seen;
    }

    /// <summary>The nurse a cell and direction would add, or why not.</summary>
    public (Gen7NursePlan? Plan, string Reason) PlanNurse(int zone, int x, int y, double facing)
    {
        if (NurseTemplate is not { } t) return (null, NurseSummary);
        if (!_worlds.TryGetValue(zone, out var w)) return (null, "Área no disponible.");
        if (!double.IsFinite(facing)) return (null, "Elige una dirección.");
        if (x < 0 || y < 0 || x >= w.Width * 40 || y >= w.Height * 40) return (null, "La casilla está fuera de la matriz del área.");
        byte[] ed = EventBytes(zone);
        byte[][] files;
        try
        {
            var parts = Gen7CatchZones.Parts(ed, "ED");
            files = Gen7CatchZones.Parts(parts[Gen7Events.NpcPack], System.Text.Encoding.ASCII.GetString(parts[Gen7Events.NpcPack], 0, 2));
        }
        catch (Exception ex) when (ex is MapFormatException or IndexOutOfRangeException or ArgumentException) { return (null, "El paquete de eventos del área no tiene la estructura verificada."); }
        if (files.Length != 1) return (null, $"El área tiene {files.Length} zonas de NPC; por ahora la enfermera solo se añade en áreas de una zona.");

        var ground = GroundCells(zone);
        float? Level((int X, int Y) c, string what, out string why)
        {
            why = "";
            if (!ground.TryGetValue(c, out var under) || under.Count == 0) { why = $"La casilla {what} ({c.X}, {c.Y}) no tiene suelo transitable."; return null; }
            float low = under.Min(g => g.Height), high = under.Max(g => g.Height);
            if (high - low > 30) { why = $"La casilla {what} ({c.X}, {c.Y}) tiene varios niveles de suelo (puente, escalón o techo)."; return null; }
            return (low + high) / 2;
        }
        if (Level((x, y), "elegida", out string why) is not float h) return (null, why);
        var (cx, cz) = CellCentre(x, y);
        var position = new Vector3(cx, h, cz);
        var q = Gen7Nurse.Facing(facing);
        var rel = Gen7Nurse.FirstPoint(t.Talk);
        var talk = position + Vector3.Transform(rel, q);
        var talkCell = ((int)Math.Floor(talk.X / UnitsPerCell), (int)Math.Floor(talk.Z / UnitsPerCell));
        if (Level(talkCell, "desde la que se le habla", out why) is not float th) return (null, why);
        if (Math.Abs(th - h) > 30) return (null, $"La casilla desde la que se le habla ({talkCell.Item1}, {talkCell.Item2}) está a otra altura; elige otra dirección o casilla.");

        // Walls and fences (block part 7): she must be talked to across no wall, from a cell the player can walk to.
        var walls = WallSegments(zone);
        if (walls.Any(wl => Crosses(new(cx, cz), new(talk.X, talk.Z), wl.A, wl.B)))
            return (null, "Hay una pared o valla entre la enfermera y la casilla desde la que se le habla; elige otra dirección o casilla.");
        if (Reachable(zone, ground, walls) is { } reach && !reach.Contains(talkCell))
            return (null, $"No se puede llegar andando a la casilla desde la que se le habla ({talkCell.Item1}, {talkCell.Item2}) desde las entradas del área: hay paredes o vallas por medio. Elige otra casilla.");

        ZoneEvents(zone);
        Dictionary<int, (int Pack, int Zone, int Row, Vector3 Position)> refs;
        lock (_gate) refs = _refs.TryGetValue(zone, out var m) ? new(m) : [];
        foreach (var e in refs.Values)
        {
            var p = new Vector2(e.Position.X, e.Position.Z);
            if (e.Pack is Gen7Events.NpcPack or Gen7Events.TrainerPack && (Vector2.Distance(p, new(cx, cz)) < UnitsPerCell * 0.75f || Vector2.Distance(p, new(talk.X, talk.Z)) < UnitsPerCell * 0.75f))
                return (null, "La casilla o la que hay delante la ocupa otro NPC o entrenador.");
            if (e.Pack == Gen7Events.WarpPack && (Vector2.Distance(p, new(cx, cz)) < UnitsPerCell * 1.5f || Vector2.Distance(p, new(talk.X, talk.Z)) < UnitsPerCell * 1.5f))
                return (null, "Hay un warp (puerta o salida) junto a esa casilla; la enfermera podría bloquearlo.");
        }

        // Event id: one more than the largest NPC id of the zone, unused by every event kind of the zone.
        var used = new HashSet<uint>();
        uint maxNpc = 0;
        var edParts = Gen7CatchZones.Parts(ed, "ED");
        foreach (var layout in Gen7Events.Layouts.Where(l => l.Kind != MapEventKind.Trigger))
        {
            if (layout.Pack >= edParts.Length || edParts[layout.Pack].Length < 8) continue;
            var container = Gen7Events.Container(edParts[layout.Pack]);
            if (container is null) continue;
            for (int z = 0; z < container.Count; z++)
            {
                var file = container.Part(z).ToArray();
                if (file.Length < 4) continue;
                foreach (var r in Gen7Events.Records(file, layout.Size, layout.Type))
                {
                    uint id = r.U32(Gen7Nurse.EventId); used.Add(id);
                    if (layout.Pack == Gen7Events.NpcPack) maxNpc = Math.Max(maxNpc, id);
                }
            }
        }
        uint eventId = maxNpc + 1;
        while (used.Contains(eventId)) eventId++;

        byte[] ac;
        try { ac = CharacterBytes(zone); }
        catch (Exception ex) when (ex is MapFormatException or ArgumentException) { return (null, "No se pudieron leer los modelos de personaje del área."); }
        IReadOnlyList<string?> names;
        try { names = Gen7Nurse.Characters(ac); } catch (MapFormatException ex) { return (null, ex.Message); }
        bool adds = !names.Contains(Gen7Nurse.Model.ToString(CultureInfo.InvariantCulture));
        int bytes = ac.Length, characters = names.Count;
        if (adds)
        {
            try { var withNurse = Gen7Nurse.AddCharacter(ac, t.Character); bytes = withNurse.Length; characters++; }
            catch (InvalidOperationException ex) { return (null, ex.Message); }
        }
        return (new Gen7NursePlan(zone, 0, position, facing, eventId, talk, adds, bytes, characters), "");
    }

    /// <summary>The area event pack and, when the model is added, character pack the plan writes (both checked by their writers).</summary>
    public (byte[] Events, byte[]? Characters) NurseEdits(Gen7NursePlan plan)
    {
        var t = NurseTemplate ?? throw new InvalidOperationException(NurseSummary);
        var (record, talk, body) = Gen7Nurse.Place(t, plan.Position, plan.Compass, plan.EventId);
        byte[] ed = Gen7Nurse.AddNpc(EventBytes(plan.Area), plan.ZoneFile, record, talk, body);
        byte[]? ac = plan.AddsCharacter ? Gen7Nurse.AddCharacter(CharacterBytes(plan.Area), t.Character) : null;
        return (ed, ac);
    }
}
