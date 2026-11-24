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
/// <param name="Machine">Where the healing machine goes, or null when the area already has the Center statics (objects 2 and 5).</param>
/// <param name="Counter">Where the Pokémon Center counter goes (object 5, under her), or null with <paramref name="Machine"/>.</param>
/// <param name="Zone">Global zone of the area (zonedata) whose field gimmick becomes the Pokémon Center's, or -1 when it already is.</param>
public sealed record Gen7NursePlan(int Area, int ZoneFile, Vector3 Position, double Compass, uint EventId, Vector3 Talk, bool AddsCharacter, int CharacterBytes, int Characters,
    Vector3? Machine = null, uint MachineEventId = 0, bool AddsObject = false, int ObjectBytes = 0, int Objects = 0, int Zone = -1,
    Vector3? Counter = null, uint CounterEventId = 0, bool AddsCounterModel = false);

/// <summary>Pokémon Center nurse (see <see cref="Gen7Nurse"/>).</summary>
public sealed partial class Gen7MapBackend : IMapNurseCreation
{
    /// <summary>Character pack of an area (slot 3, decompressed).</summary>
    public byte[] CharacterBytes(int area) => (byte[])_world((area * SlotsPerWorld) + CharacterSlot).Clone();
    public const int CharacterSlot = 3, ObjectSlot = 4;

    /// <summary>Zone records (zonedata member 0, 84 bytes per zone), read for the field gimmick; null when not available.</summary>
    public byte[]? ZoneData { get; set; }

    /// <summary>Object pack of an area (slot 4, decompressed).</summary>
    public byte[] ObjectBytes(int area) => (byte[])_world((area * SlotsPerWorld) + ObjectSlot).Clone();

    /// <summary>Static object records (ED part 4) of an area, every zone file.</summary>
    private static List<Gen7Record> Statics(byte[] ed)
    {
        var list = new List<Gen7Record>();
        var parts = Gen7CatchZones.Parts(ed, "ED");
        if (parts.Length <= Gen7Events.StaticPack || parts[Gen7Events.StaticPack].Length < 8) return list;
        var container = Gen7Events.Container(parts[Gen7Events.StaticPack]);
        if (container is null) return list;
        for (int z = 0; z < container.Count; z++)
        {
            var file = container.Part(z).ToArray();
            if (file.Length >= 4) list.AddRange(Gen7Events.Records(file, Gen7Nurse.StaticSize, Gen7Nurse.StaticType));
        }
        return list;
    }

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
        // Center statics, in each nurse's frame: [0] the healing machine (object 2), [1] the counter (object 5).
        uint[] staticObjects = [Gen7Nurse.MachineObject, Gen7Nurse.CounterObject];
        string[] staticNames = ["máquina de curación", "mostrador del Centro"];
        var sRecord = new byte[]?[2]; var sModel = new byte[]?[2]; var sOffset = new Vector3[2]; var sTurn = new Quaternion[2];
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
                    // The machine and the counter of this nurse's area, in her frame (offset and relative turn).
                    for (int si = 0; si < 2; si++)
                    {
                        var m = Statics(ed).FirstOrDefault(st => st.U32(Gen7Nurse.StaticObject) == staticObjects[si]);
                        if (m is null) continue;
                        var mpos = new Vector3(m.F(Gen7Nurse.Position), m.F(Gen7Nurse.Position + 4), m.F(Gen7Nurse.Position + 8));
                        var mrot = new Quaternion(m.F(Gen7Nurse.Rotation), m.F(Gen7Nurse.Rotation + 4), m.F(Gen7Nurse.Rotation + 8), m.F(Gen7Nurse.Rotation + 12));
                        var inv = Quaternion.Inverse(rot);
                        var off = Vector3.Transform(mpos - pos, inv);
                        var turn = Quaternion.Normalize(inv * mrot);
                        var mraw = (byte[])m.Bytes.Clone();
                        for (int k = 0; k < 28; k++) mraw[Gen7Nurse.Position + k] = 0;
                        for (int k = 0; k < 4; k++) mraw[Gen7Nurse.EventId + k] = 0;
                        if (sRecord[si] is null) { sRecord[si] = mraw; sOffset[si] = off; sTurn[si] = turn; }
                        else if (!mraw.AsSpan().SequenceEqual(sRecord[si]) || Vector3.Distance(off, sOffset[si]) > ShapeTolerance || Math.Abs(Quaternion.Dot(turn, sTurn[si])) < 0.999f)
                            return (null, $"La {staticNames[si]} del área {area:000} no está como en los demás Centros; no se ofrece la enfermera.");
                    }
                }
            }
            if (!here) continue;
            try
            {
                var packs = Gen7CatchZones.Parts(ObjectBytes(area), "AS");
                for (int si = 0; si < 2; si++)
                {
                    var sm = packs.FirstOrDefault(pk => Gen7Nurse.ObjectId(pk) == (int)staticObjects[si]);
                    if (sm is null) continue;
                    if (sModel[si] is null) sModel[si] = sm;
                    else if (!sm.AsSpan().SequenceEqual(sModel[si])) return (null, $"El modelo de la {staticNames[si]} no es igual en todos los Centros; no se ofrece.");
                }
            }
            catch (MapFormatException ex) { return (null, "No se pudieron leer los modelos de objetos del Centro: " + ex.Message); }
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
        for (int si = 0; si < 2; si++)
        {
            if (sRecord[si] is null || sModel[si] is null) return (null, $"Los Centros del juego no tienen una {staticNames[si]} reconocible (objeto {staticObjects[si]}); sin ella la curación se congela, así que no se ofrece.");
            BinaryPrimitives.WriteSingleLittleEndian(sRecord[si]!.AsSpan(Gen7Nurse.Rotation + 12), 1);
        }
        // The template stands at the origin without rotation (written again by Gen7Nurse.Place).
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(Gen7Nurse.Rotation + 12), 1);
        return (new Gen7NurseTemplate(record, talk!, body!, character, Gen7Nurse.Model, Gen7Nurse.Script, count, sRecord[0]!, sOffset[0], sTurn[0], sModel[0]!,
            sRecord[1]!, sOffset[1], sTurn[1], sModel[1]!), null);
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
        ? $"Copia la enfermera de Centro Pokémon del propio juego ({n.Count} enfermeras idénticas: modelo {n.Model} y rutina común de curación {n.Script}) y, si el área no tiene una, la máquina de curación que usa esa rutina (objeto {Gen7Nurse.MachineObject}). Si el área no carga sus modelos, se añaden."
        : _nurseReason ?? "No hay enfermera que copiar.";

    public MapMutationCheck ValidateAddNurse(int zone, int x, int y, double facing)
    {
        var (plan, reason) = PlanNurse(zone, x, y, facing);
        if (plan is null) return new(false, reason);
        var inv = CultureInfo.InvariantCulture;
        string model = plan.AddsCharacter
            ? $" El área no carga el modelo de la enfermera: se añadirá a sus modelos de personaje ({plan.Characters} modelos, {plan.CharacterBytes / 1024} KB; el juego llega a {Gen7Nurse.MaxCharacters} y {Gen7Nurse.MaxCharacterBytes / 1024} KB)."
            : " El área ya carga el modelo de la enfermera.";
        string machine = plan.Machine is { } mp
            ? $" Con ella se colocan, como en los Centros, el mostrador del Centro (estático {plan.CounterEventId}, bajo ella) y la máquina de curación encima (estático {plan.MachineEventId}, casilla {Math.Floor(mp.X / UnitsPerCell)}, {Math.Floor(mp.Z / UnitsPerCell)}), necesarios para curar" +
              (plan.AddsObject || plan.AddsCounterModel ? $"; sus modelos se añadirán a los objetos del área ({plan.Objects} objetos, {plan.ObjectBytes / 1024} KB; el juego llega a {Gen7Nurse.MaxObjects} y {Gen7Nurse.MaxObjectBytes / 1024} KB)." : ".")
            : " El área ya tiene el mostrador y la máquina de curación.";
        model += machine;
        model += plan.Zone >= 0
            ? $" La zona {plan.Zone} pasará a cargar el módulo de campo del Centro Pokémon (gimmick {Gen7Nurse.PokecenGimmick}), que usa la curación."
            : " La zona ya carga el módulo del Centro Pokémon.";
        return new(true, $"Se añadirá una enfermera (evento {plan.EventId}) en la casilla {x}, {y}, a altura {plan.Position.Y.ToString("0.#", inv)}, mirando hacia {TrainerDirection(plan.Compass)}; se le habla desde la casilla {Math.Floor(plan.Talk.X / UnitsPerCell)}, {Math.Floor(plan.Talk.Z / UnitsPerCell)}.{model} " +
            "Usa la rutina común de curación del juego. No se ha probado en el juego.");
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

        // Healing machine: added next to her when the area has none (the healing branch looks it up and animates it).
        Vector3? machinePos = null, counterPos = null; uint machineId = 0, counterId = 0;
        List<Gen7Record> statics;
        try { statics = Statics(ed); } catch (MapFormatException) { return (null, "Los objetos estáticos del área no tienen la estructura verificada."); }
        bool hasMachine = statics.Any(st => st.U32(Gen7Nurse.StaticObject) == Gen7Nurse.MachineObject);
        bool hasCounter = statics.Any(st => st.U32(Gen7Nurse.StaticObject) == Gen7Nurse.CounterObject);
        if (hasMachine != hasCounter)
            return (null, "El área tiene solo una de las dos piezas del Centro (máquina de curación u mostrador); no se añade la enfermera ahí.");
        if (!hasMachine)
        {
            var staticFiles = Gen7Events.Container(edParts[Gen7Events.StaticPack]);
            if (staticFiles is null || staticFiles.Count != 1) return (null, "El área tiene varias zonas de objetos estáticos; por ahora no se añade la máquina de curación.");
            var (_, mp) = Gen7Nurse.PlaceMachine(t, position, facing, 0);
            var mcell = ((int)Math.Floor(mp.X / UnitsPerCell), (int)Math.Floor(mp.Z / UnitsPerCell));
            if (Level(mcell, "de la máquina de curación", out why) is not float mh) return (null, why + " Elige otra dirección o casilla (la máquina va al lado de la enfermera).");
            if (Math.Abs(mh - h) > 30) return (null, $"La casilla de la máquina de curación ({mcell.Item1}, {mcell.Item2}) está a otra altura; elige otra dirección o casilla.");
            _ = mh;
            foreach (var e in refs.Values)
            {
                var p = new Vector2(e.Position.X, e.Position.Z);
                if (e.Pack is Gen7Events.NpcPack or Gen7Events.TrainerPack or Gen7Events.WarpPack && Vector2.Distance(p, new(mp.X, mp.Z)) < UnitsPerCell * 0.75f)
                    return (null, "La casilla de la máquina de curación la ocupa otro evento; elige otra dirección o casilla.");
            }
            if (Vector2.Distance(new(mp.X, mp.Z), new(talk.X, talk.Z)) < UnitsPerCell * 0.75f) return (null, "La máquina de curación quedaría donde se habla con la enfermera.");
            foreach (var st in statics) used.Add(st.U32(Gen7Nurse.EventId));
            machineId = 701;
            while (used.Contains(machineId) || machineId == eventId) machineId++;
            counterId = machineId + 1;
            while (used.Contains(counterId) || counterId == eventId) counterId++;
            machinePos = mp;
            counterPos = Gen7Nurse.PlaceCounter(t, position, facing, 0).Position;
        }

        // Field gimmick: the healing branch needs the Pokémon Center module, loaded by zones with gimmick 3.
        if (ZoneData is not { } zd) return (null, "No se pudo leer la tabla de zonas (campo de gimmick).");
        var areaZones = _zoneTable.Values.Where(zi => zi.Area == zone).Select(zi => zi.Zone).ToArray();
        if (areaZones.Length != 1) return (null, $"El área tiene {areaZones.Length} zonas del juego; por ahora la enfermera solo se añade en áreas de una zona.");
        int gz = areaZones[0];
        if ((gz + 1) * Gen7Nurse.ZoneSize > zd.Length) return (null, "La zona no está en la tabla de zonas.");
        byte gimmick = zd[(gz * Gen7Nurse.ZoneSize) + Gen7Nurse.GimmickOffset];
        if (gimmick is not (Gen7Nurse.NoGimmick or Gen7Nurse.PokecenGimmick))
            return (null, $"La zona {gz} ya usa otro módulo de campo (gimmick {gimmick}: ascensor, instalación de combate, Festival...); la curación de la enfermera necesita el del Centro Pokémon y no pueden convivir.");
        int setZone = gimmick == Gen7Nurse.NoGimmick ? gz : -1;

        int objectBytes = 0, objects = 0; bool addsObject = false, addsCounter = false;
        if (machinePos is not null)
        {
            byte[] objs;
            try { objs = ObjectBytes(zone); } catch (Exception ex) when (ex is MapFormatException or ArgumentException) { return (null, "No se pudieron leer los objetos del área."); }
            IReadOnlyList<int?> ids;
            try { ids = Gen7Nurse.Objects(objs); } catch (MapFormatException ex) { return (null, ex.Message); }
            addsObject = !ids.Contains((int)Gen7Nurse.MachineObject);
            addsCounter = !ids.Contains((int)Gen7Nurse.CounterObject);
            objectBytes = objs.Length; objects = ids.Count;
            try
            {
                var with = objs;
                if (addsObject) { with = Gen7Nurse.AddObject(with, t.MachineModel); objects++; }
                if (addsCounter) { with = Gen7Nurse.AddObject(with, t.CounterModel); objects++; }
                objectBytes = with.Length;
            }
            catch (InvalidOperationException ex) { return (null, ex.Message + " (el mostrador del Centro ocupa 430 KB)"); }
        }

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
        return (new Gen7NursePlan(zone, 0, position, facing, eventId, talk, adds, bytes, characters, machinePos, machineId, addsObject, objectBytes, objects, setZone,
            counterPos, counterId, addsCounter), "");
    }

    /// <summary>
    /// The area event pack (nurse and, when needed, healing machine) and, when they are added, the character and object
    /// packs the plan writes (each checked by its writer).
    /// </summary>
    /// <summary>The zone records with the plan's zone set to the Pokémon Center gimmick, or null when nothing changes.</summary>
    public byte[]? NurseZoneEdit(Gen7NursePlan plan) =>
        plan.Zone < 0 ? null : Gen7Nurse.SetGimmick(ZoneData ?? throw new InvalidOperationException("Sin tabla de zonas."), plan.Zone, Gen7Nurse.PokecenGimmick);

    public (byte[] Events, byte[]? Characters, byte[]? Objects) NurseEdits(Gen7NursePlan plan)
    {
        var t = NurseTemplate ?? throw new InvalidOperationException(NurseSummary);
        var (record, talk, body) = Gen7Nurse.Place(t, plan.Position, plan.Compass, plan.EventId);
        byte[] ed = Gen7Nurse.AddNpc(EventBytes(plan.Area), plan.ZoneFile, record, talk, body);
        byte[]? objects = null;
        if (plan.Machine is not null)
        {
            ed = Gen7Nurse.AddStatic(ed, 0, Gen7Nurse.PlaceMachine(t, plan.Position, plan.Compass, plan.MachineEventId).Record);
            ed = Gen7Nurse.AddStatic(ed, 0, Gen7Nurse.PlaceCounter(t, plan.Position, plan.Compass, plan.CounterEventId).Record);
            var objs = ObjectBytes(plan.Area);
            if (plan.AddsObject) objs = Gen7Nurse.AddObject(objs, t.MachineModel);
            if (plan.AddsCounterModel) objs = Gen7Nurse.AddObject(objs, t.CounterModel);
            if (plan.AddsObject || plan.AddsCounterModel) objects = objs;
        }
        byte[]? ac = plan.AddsCharacter ? Gen7Nurse.AddCharacter(CharacterBytes(plan.Area), t.Character) : null;
        return (ed, ac, objects);
    }
}
