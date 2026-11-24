using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;
using System.Windows;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;

/// <summary>
/// «Añadir enfermera» in Ultra Sun / Ultra Moon: the copied Center nurse (ED part 1 record and shapes, model 66 in the
/// area character pack) on synthetic areas, the panel, and on real copies through the service and the history.
/// </summary>
internal static class Map3dsGen7NurseChecks
{
    /// <summary>A shape list with one cylinder (type 0): centre, radius, height.</summary>
    internal static byte[] Cylinder(Vector3 c, float radius, float height)
    {
        var b = new byte[28];
        BinaryPrimitives.WriteInt32LittleEndian(b, 1); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 0);
        float[] v = [c.X, c.Y, c.Z, radius, height];
        for (int k = 0; k < v.Length; k++) BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8 + (k * 4)), v[k]);
        return b;
    }

    /// <summary>An NPC record (type 1) without its shape offsets.</summary>
    internal static byte[] Npc(uint id, uint model, uint script, Vector3 pos, uint flags = 0)
    {
        var r = new byte[120];
        BinaryPrimitives.WriteUInt32LittleEndian(r, 1);
        BinaryPrimitives.WriteSingleLittleEndian(r.AsSpan(4), pos.X); BinaryPrimitives.WriteSingleLittleEndian(r.AsSpan(8), pos.Y); BinaryPrimitives.WriteSingleLittleEndian(r.AsSpan(12), pos.Z);
        BinaryPrimitives.WriteSingleLittleEndian(r.AsSpan(0x1C), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x2C), id); BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x30), 9);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x34), model); BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x38), script);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x3C), flags);
        return r;
    }

    /// <summary>A zone file of ED part 1: count, records and each record's talk and body shapes after them.</summary>
    internal static byte[] NpcFile(params (byte[] Record, byte[] Talk, byte[] Body)[] npcs)
    {
        int at = 4 + (npcs.Length * 120);
        var shapes = new List<byte>(); var records = new List<byte>();
        foreach (var (record, talk, body) in npcs)
        {
            var r = (byte[])record.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(0x70), at); at += talk.Length;
            BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(0x74), at); at += body.Length;
            records.AddRange(r); shapes.AddRange(talk); shapes.AddRange(body);
        }
        return [.. BitConverter.GetBytes(npcs.Length), .. records, .. shapes];
    }

    internal static (byte[] Record, byte[] Talk, byte[] Body) Standing(uint id, uint model, uint script, Vector3 pos, uint flags = 0) =>
        (Npc(id, model, script, pos, flags), Cylinder(pos + new Vector3(0, 0, 130), 80, 160), Cylinder(pos, 50, 160));

    /// <summary>An event pack: 17 parts, part 1 the NPC files, part 2 warps (88-byte records at the given points), part 6 one empty region file.</summary>
    /// <summary>A static object record (ED part 4, 56 bytes, no shapes): object id, event id, position and rotation.</summary>
    internal static byte[] Static(uint id, uint obj, Vector3 pos, Quaternion rot)
    {
        var r = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(r, 4);
        float[] v = [pos.X, pos.Y, pos.Z, rot.X, rot.Y, rot.Z, rot.W];
        for (int k = 0; k < v.Length; k++) BinaryPrimitives.WriteSingleLittleEndian(r.AsSpan(4 + (k * 4)), v[k]);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x2C), id); BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x30), obj);
        return r;
    }

    /// <summary>The Center machine as in Ultra Moon: object 2 on the counter to the side of a nurse at 2100,0,2100.</summary>
    internal static byte[] Machine(uint id = 701) => Static(id, 2, new Vector3(2264.7f, 80, 2120.7f), new Quaternion(0, -0.366f, 0, -0.9306f));

    /// <summary>An object "SM" pack: id and a model part, 0x100 bytes in all.</summary>
    internal static byte[] ObjectModel(ushort id, byte fill = 0x3C)
    {
        var head = new byte[0x80]; BinaryPrimitives.WriteUInt16LittleEndian(head, id);
        var sm = Gen7CatchZones.Pack("SM", [head, Enumerable.Repeat(fill, 0x80 - 16).ToArray()], 0x10);
        return [.. sm, .. new byte[(0x100 - sm.Length % 0x100) % 0x100]];
    }

    internal static byte[] ObjectPack(params ushort[] ids)
    {
        var parts = ids.Select(i => ObjectModel(i)).ToArray();
        return Gen7CatchZones.Pack("AS", parts, Gen7Nurse.DataStart(parts.Length));
    }

    internal static byte[] Ed(byte[][] npcFiles, params Vector3[] warps) => Ed(npcFiles, [], warps);

    internal static byte[] Ed(byte[][] npcFiles, byte[][] statics, params Vector3[] warps)
    {
        var warpFile = new List<byte>(BitConverter.GetBytes(warps.Length));
        for (int i = 0; i < warps.Length; i++)
        {
            var w = new byte[88];
            BinaryPrimitives.WriteUInt32LittleEndian(w, 3);
            BinaryPrimitives.WriteSingleLittleEndian(w.AsSpan(4), warps[i].X); BinaryPrimitives.WriteSingleLittleEndian(w.AsSpan(8), warps[i].Y); BinaryPrimitives.WriteSingleLittleEndian(w.AsSpan(12), warps[i].Z);
            BinaryPrimitives.WriteUInt32LittleEndian(w.AsSpan(0x2C), (uint)(100 + i));
            warpFile.AddRange(w);
        }
        return Gen7CatchZones.Pack("ED", Enumerable.Range(0, 17).Select(p => p switch
        {
            1 => Gen7CatchZones.Pack("EN", npcFiles),
            2 => Gen7CatchZones.Pack("EW", [[.. warpFile]]),
            4 => Gen7CatchZones.Pack("ES", [[.. BitConverter.GetBytes(statics.Length), .. statics.SelectMany(x => x)]]),
            6 => Gen7CatchZones.Pack("EA", [Map3dsGen7CatchZoneChecks.RegionFile()]),
            _ => Gen7CatchZones.Pack("EP", [BitConverter.GetBytes(0)]),
        }).ToArray());
    }

    /// <summary>A character "CP" pack named <paramref name="model"/>, 0x100 bytes in all (two parts of 0x80).</summary>
    internal static byte[] Character(string model, byte fill = 0x5A)
    {
        var name = new byte[0x80]; Encoding.ASCII.GetBytes(model).CopyTo(name, 0);
        var body = Enumerable.Repeat(fill, 0x80 - 16).ToArray();
        var cp = Gen7CatchZones.Pack("CP", [name, body], 0x10);
        return [.. cp, .. new byte[(0x100 - cp.Length % 0x100) % 0x100]];
    }

    internal static byte[] Characters(params string[] models)
    {
        var parts = models.Select(m => Character(m)).ToArray();
        return Gen7CatchZones.Pack("AC", parts, Gen7Nurse.DataStart(parts.Length));
    }

    /// <summary>
    /// Areas: 0 a Pokémon Center (nurse model 66 / script 5030 and a clerk); 1 the target (one NPC, model 5, ground
    /// 0–2100 × 0–1050, a warp at 1522,0,577); 2 two NPC zones; 3 a second Center; 4 a target with 37 models.
    /// </summary>
    internal static Dictionary<int, byte[]> Areas()
    {
        var tr = Map3dsGen6Checks.Pack("TR", Map3dsGen7Checks.Tr(1, 1, 4200, 0));
        var c = new Vector3(2100, 0, 2100);
        var areas = new Dictionary<int, byte[]>();
        void Area(int a, byte[] ed, byte[] ac, byte[] objects)
        { areas[a * 11] = ed; areas[(a * 11) + 2] = tr; areas[(a * 11) + 3] = ac; areas[(a * 11) + 4] = objects; areas[(a * 11) + 9] = Gen7CatchZones.Pack("EA", []); }
        Area(0, Ed([NpcFile(Standing(1, 66, 5030, c, 111), Standing(2, 64, 5400, c + new Vector3(300, 0, 0)))], [Machine()]), Characters("66", "64", "135"), ObjectPack(2, 5));
        Area(1, Ed([NpcFile(Standing(1, 5, 0, new Vector3(157.5f, 0, 157.5f)))], [Static(303, 77, new Vector3(1800, 0, 900), Quaternion.Identity)], new Vector3(1522.5f, 0, 577.5f)),
            Characters("5", "135"), ObjectPack(77));
        Area(2, Ed([NpcFile(), NpcFile()]), Characters("135"), ObjectPack(77));
        Area(3, Ed([NpcFile(Standing(3, 66, 5030, c, 111))], [Machine(702)]), Characters("66", "135"), ObjectPack(2, 5));
        Area(4, Ed([NpcFile()]), Characters([.. Enumerable.Range(200, 37).Select(i => i.ToString())]), ObjectPack(77));
        return areas;
    }

    internal static Gen7MapBackend Backend(Dictionary<int, byte[]> areas)
    {
        var block = Map3dsGen7GrassEditorChecks.MapBlock([], Map3dsGen7GrassEditorChecks.Ground());
        return new Gen7MapBackend("Prueba", 55, i => areas.GetValueOrDefault(i, []), 1, _ => block);
    }

    internal static void Run(Action<bool, string> check)
    {
        var areas = Areas(); var b = Backend(areas);
        var t = b.NurseTemplate;
        check(t is { Count: 2, Model: 66, Script: 5030 } && Gen7Nurse.CharacterName(t.Character) == "66" && Gen7Nurse.FirstPoint(t.Talk) == new Vector3(0, 0, 130)
            && b.NurseSummary.Contains("2 enfermeras") && b.NurseSummary.Contains("máquina de curación"), "Gen7Nurse_TemplateIsTheGameNurseWithItsModel");
        check(Vector3.Distance(t!.MachineOffset, new Vector3(164.7f, 0, 20.7f)) < 0.01f && Gen7Nurse.ObjectId(t.MachineModel) == 2
            && Gen7Events.Records([.. BitConverter.GetBytes(1), .. t.Machine], 56, 4).Single().U32(0x30) == 2, "Gen7Nurse_TemplateHasTheCenterHealingMachine");
        check(Gen7Nurse.DataStart(30) == 0x80 && Gen7Nurse.DataStart(31) == 0x100 && Gen7Nurse.DataStart(37) == 0x100, "Gen7Nurse_CharacterPackDataStartFollowsTheGame");

        var (plan, reason) = b.PlanNurse(1, 5, 5, 90);
        check(plan is { EventId: 2, AddsCharacter: true, Characters: 3 } && plan.Position == new Vector3(577.5f, 0, 577.5f) && Vector3.Distance(plan.Talk, new Vector3(707.5f, 0, 577.5f)) < 0.01f,
            "Gen7Nurse_PlanPlacesHerOnTheCellWithTheTalkRangeInFront " + reason);
        var (ed, ac, objs) = b.NurseEdits(plan!);
        var file = Gen7CatchZones.Parts(Gen7CatchZones.Parts(ed, "ED")[1], "EN")[0];
        var recs = Gen7Events.Records(file, 120, 1).ToList();
        var nurse = recs[^1];
        var q = Gen7Nurse.Facing(90);
        check(recs.Count == 2 && nurse.U32(0x34) == 66 && nurse.U32(0x38) == 5030 && nurse.U32(0x3C) == 111 && nurse.U32(0x2C) == 2 && nurse.Position == plan!.Position
            && Math.Abs(nurse.F(0x14) - q.Y) < 1e-6 && Math.Abs(nurse.F(0x1C) - q.W) < 1e-6
            && Vector3.Distance(Gen7Nurse.FirstPoint(Gen7Nurse.ShapeList(file, (int)nurse.U32(0x70))), plan.Talk) < 0.01f
            && Gen7Nurse.FirstPoint(Gen7Nurse.ShapeList(file, (int)nurse.U32(0x74))) == plan.Position, "Gen7Nurse_NewRecordIsTheTemplateMovedAndTurned");
        var oldFile = Gen7CatchZones.Parts(Gen7CatchZones.Parts(areas[11], "ED")[1], "EN")[0];
        var old = Gen7Events.Records(oldFile, 120, 1).First();
        check(recs[0].Bytes.AsSpan(0, 0x70).SequenceEqual(old.Bytes.AsSpan(0, 0x70)) && recs[0].U32(0x70) == old.U32(0x70) + 120
            && Gen7Nurse.ShapeList(file, (int)recs[0].U32(0x70)).SequenceEqual(Gen7Nurse.ShapeList(oldFile, (int)old.U32(0x70)))
            && Enumerable.Range(0, 17).Where(p => p is not 1 and not 4).All(p => Gen7CatchZones.Parts(ed, "ED")[p].AsSpan().SequenceEqual(Gen7CatchZones.Parts(areas[11], "ED")[p])),
            "Gen7Nurse_OtherNpcsAndPartsKeepTheirBytes");
        check(ac is not null && Gen7Nurse.Characters(ac).SequenceEqual(["5", "135", "66"]) && Gen7CatchZones.Parts(ac, "AC")[^1].AsSpan().SequenceEqual(t!.Character),
            "Gen7Nurse_AreaLoadsTheNurseModel");
        // Machine: object 2 next to her, turned with her, on the ground, with its model added to the area objects.
        var statics = Gen7Events.Records(Gen7CatchZones.Parts(Gen7CatchZones.Parts(ed, "ED")[4], "ES")[0], 56, 4).ToList();
        var machine = statics[^1];
        var expected = plan!.Position + Vector3.Transform(new Vector3(164.7f, 0, 20.7f), q);
        check(plan.Machine is { } mpos && Vector3.Distance(mpos, new Vector3(expected.X, 0, expected.Z)) < 0.01f && plan.MachineEventId == 701 && plan.AddsObject
            && statics.Count == 2 && statics[0].Bytes.AsSpan().SequenceEqual(Gen7Events.Records(Gen7CatchZones.Parts(Gen7CatchZones.Parts(areas[11], "ED")[4], "ES")[0], 56, 4).First().Bytes)
            && machine.U32(0x30) == 2 && machine.U32(0x2C) == 701 && Vector3.Distance(machine.Position, mpos) < 0.01f
            && objs is not null && Gen7Nurse.Objects(objs).SequenceEqual([77, 2]) && Gen7CatchZones.Parts(objs, "AS")[^1].AsSpan().SequenceEqual(t.MachineModel),
            "Gen7Nurse_HealingMachineIsPlacedNextToHerWithItsModel");
        var (inCenter, _) = b.PlanNurse(3, 5, 5, 180);
        check(inCenter is { AddsCharacter: false, Machine: null } && b.NurseEdits(inCenter).Characters is null && b.NurseEdits(inCenter).Objects is null,
            "Gen7Nurse_AreaThatAlreadyLoadsTheModelAndHasAMachineKeepsThem");
        var (machineOff, r9) = b.PlanNurse(1, 5, 8, 270);
        check(machineOff is null && r9.Contains("máquina de curación"), "Gen7Nurse_MachineNeedsGroundBesideHer " + r9);
        var (occupied, r1) = b.PlanNurse(1, 1, 1, 180);
        var (front, r2) = b.PlanNurse(1, 1, 0, 180);
        var (warp, r3) = b.PlanNurse(1, 13, 5, 0);
        var (noGround, r4) = b.PlanNurse(1, 30, 5, 180);
        var (talkOff, r5) = b.PlanNurse(1, 5, 9, 180);
        var (zones, r6) = b.PlanNurse(2, 5, 5, 180);
        var (full, r7) = b.PlanNurse(4, 5, 5, 180);
        check(occupied is null && r1.Contains("ocupa") && front is null && r2.Contains("ocupa") && warp is null && r3.Contains("warp") && noGround is null && r4.Contains("no tiene suelo")
            && talkOff is null && r5.Contains("desde la que se le habla") && zones is null && r6.Contains("2 zonas") && full is null && r7.Contains("37"), "Gen7Nurse_InvalidPlacesAreRefused | " + string.Join(" | ", r1, r2, r3, r4, r5, r6, r7));
        var added = new Dictionary<int, byte[]>(areas) { [11] = ed, [14] = ac!, [15] = objs! };
        var b2 = Backend(added);
        var (second, r8) = b2.PlanNurse(1, 9, 5, 270);
        check(b2.NurseTemplate is { Count: 3 } && second is { EventId: 3, AddsCharacter: false, Machine: null } && b2.NurseEdits(second).Characters is null && b2.NurseEdits(second).Objects is null,
            "Gen7Nurse_AnAddedNurseIsRecognisedAndASecondOneCanBeAdded " + r8);
        var lone = Areas(); lone[3 * 11] = Ed([NpcFile(Standing(3, 66, 5030, new Vector3(2100, 0, 2100), 112))]);
        check(Backend(lone).NurseTemplate is null && Backend(lone).NurseSummary.Contains("no son todas iguales"), "Gen7Nurse_DifferentNursesAreNotCopied");
    }

    internal static void RunUi(Action<bool, string> check)
    {
        var areas = Areas();
        var calls = new List<(int Zone, int X, int Y, double Facing)>();
        var mover = new Map3dsMover((b, z, n, x, y) => "m", () => Backend(areas), addNurse: (b, z, x, y, f) => { calls.Add((z, x, y, f)); return $"Enfermera en el área {z:000}"; });
        var view = new Map3dsEditorView(Backend(areas), mover);
        view.Measure(new Size(1400, 860)); view.Arrange(new Rect(0, 0, 1400, 860)); view.UpdateLayout(); UiPump.Idle();
        var noProject = new Map3dsEditorView(Backend(areas));
        check(view.NurseButton is not null && noProject.NurseButton is null, "Map3dsGen7NurseUi_ButtonOnlyWithAProjectThatCanWrite");
        view.ShowZone(1);
        check(view.ShowNursePanel() && view.Canvas.PickingCell && view.NurseSave is { IsEnabled: false }, "Map3dsGen7NurseUi_PanelOpens");
        view.Canvas.PickCell(5, 5); UiPump.Idle();
        check(view.NurseResult!.Text.StartsWith("Se añadirá una enfermera") && view.NurseResult.Text.Contains("modelos de personaje") && view.NurseResult.Text.Contains("máquina de curación") && view.NurseSave!.IsEnabled && view.Canvas.PendingNurse is { X: 5, Y: 5 },
            "Map3dsGen7NurseUi_PreviewExplainsTheModelAndDoesNotWrite");
        view.Canvas.PickCell(1, 1); UiPump.Idle();
        check(!view.NurseSave!.IsEnabled && view.NurseResult!.Text.Contains("ocupa"), "Map3dsGen7NurseUi_OccupiedCellCannotBeSaved");
        view.Canvas.PickCell(5, 5); UiPump.Idle();
        check(view.SaveNurse() && calls.Count == 1 && calls[0].Zone == 1 && calls[0].X == 5 && calls[0].Y == 5, "Map3dsGen7NurseUi_SaveGoesThroughTheProject");
    }

    /// <summary>
    /// Real copy in a test folder: preview, commit, reopen (exactly one more NPC, the nurse, and her model loaded), only
    /// the area's event and character members changed, history, exact restore, stale editor; then reapplied for the
    /// gameplay test.
    /// </summary>
    internal static void Probe(string copyRoot, int area, int x, int y, double facing)
    {
        string full = Path.GetFullPath(copyRoot);
        if (!full.Contains("Pruebas", StringComparison.OrdinalIgnoreCase)) throw new IOException("Por seguridad, la prueba solo escribe en una copia dentro de una carpeta de pruebas.");
        var session = GameProjectSession.Open(full, false);
        var service = new Map3dsGen7NurseService(session);
        string archive = service.FieldArchive;
        byte[] original = File.ReadAllBytes(archive);
        var before = Gen7MapBackend.FromConfig(session.GameConfiguration);
        Console.WriteLine(before.NurseSummary);
        var check0 = before.ValidateAddNurse(area, x, y, facing);
        Console.WriteLine("Comprobación: " + check0.Reason);
        if (!check0.Allowed) throw new InvalidOperationException(check0.Reason);
        var npcsBefore = before.ZoneEvents(area).Where(e => e.Kind == MapEventKind.Npc).ToList();
        var proposal = service.Preview(before, area, x, y, facing);
        bool previewSafe = File.ReadAllBytes(archive).AsSpan().SequenceEqual(original);
        Console.WriteLine((previewSafe ? "PASS" : "FAIL") + " Gen7NurseReal_PreviewWritesNothing");
        service.Apply(proposal);
        var after = Gen7MapBackend.FromConfig(session.GameConfiguration);
        var npcsAfter = after.ZoneEvents(area).Where(e => e.Kind == MapEventKind.Npc).ToList();
        var ed = after.EventBytes(area);
        var file = Gen7CatchZones.Parts(Gen7CatchZones.Parts(ed, "ED")[1], Encoding.ASCII.GetString(Gen7CatchZones.Parts(ed, "ED")[1], 0, 2))[0];
        var recs = Gen7Events.Records(file, 120, 1).ToList();
        var nurse = recs[^1];
        bool one = npcsAfter.Count == npcsBefore.Count + 1 && nurse.U32(0x34) == Gen7Nurse.Model && nurse.U32(0x38) == Gen7Nurse.Script
            && Math.Floor(nurse.Position.X / Gen7MapBackend.UnitsPerCell) == x && Math.Floor(nurse.Position.Z / Gen7MapBackend.UnitsPerCell) == y;
        Console.WriteLine((one ? "PASS" : "FAIL") + $" Gen7NurseReal_ExactlyOneMoreNpcTheNurse ({npcsBefore.Count} → {npcsAfter.Count}; evento {nurse.U32(0x2C)}, modelo {nurse.U32(0x34)}, script {nurse.U32(0x38)}, posición {nurse.Position})");
        bool others = npcsBefore.All(e => npcsAfter.Any(a => a.Label == e.Label && a.X == e.X && a.Y == e.Y));
        Console.WriteLine((others ? "PASS" : "FAIL") + " Gen7NurseReal_OtherNpcsUnchanged");
        bool model = Gen7Nurse.Characters(after.CharacterBytes(area)).Contains("66")
            && Gen7CatchZones.Parts(after.CharacterBytes(area), "AC").Any(p => p.AsSpan().SequenceEqual(after.NurseTemplate!.Character));
        Console.WriteLine((model ? "PASS" : "FAIL") + $" Gen7NurseReal_AreaLoadsTheNurseModel ({Gen7Nurse.Characters(before.CharacterBytes(area)).Count} → {Gen7Nurse.Characters(after.CharacterBytes(area)).Count} modelos, {after.CharacterBytes(area).Length} B)");
        var staticsAfter = Gen7Events.Records(Gen7CatchZones.Parts(Gen7CatchZones.Parts(ed, "ED")[4], Encoding.ASCII.GetString(Gen7CatchZones.Parts(ed, "ED")[4], 0, 2))[0], 56, 4).ToList();
        int machinesBefore = Gen7Events.Records(Gen7CatchZones.Parts(Gen7CatchZones.Parts(before.EventBytes(area), "ED")[4], Encoding.ASCII.GetString(Gen7CatchZones.Parts(before.EventBytes(area), "ED")[4], 0, 2))[0], 56, 4).Count(r => r.U32(0x30) == 2);
        var machine = staticsAfter.LastOrDefault(r => r.U32(0x30) == Gen7Nurse.MachineObject);
        bool machineOk = machine is not null && staticsAfter.Count(r => r.U32(0x30) == 2) == Math.Max(machinesBefore, 1) && Gen7Nurse.Objects(after.ObjectBytes(area)).Contains((int)Gen7Nurse.MachineObject)
            && Gen7CatchZones.Parts(after.ObjectBytes(area), "AS").Any(p => p.AsSpan().SequenceEqual(after.NurseTemplate!.MachineModel));
        Console.WriteLine((machineOk ? "PASS" : "FAIL") + $" Gen7NurseReal_AreaHasTheHealingMachineAndItsModel (estático {machine?.U32(0x2C)} en {machine?.Position}; objetos {Gen7Nurse.Objects(before.ObjectBytes(area)).Count} → {Gen7Nurse.Objects(after.ObjectBytes(area)).Count})");
        var mb = new GARC.MemGARC(original).Files; var ma = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        int ev = area * Gen7MapBackend.SlotsPerWorld, ch = ev + Gen7MapBackend.CharacterSlot, ob = ev + Gen7MapBackend.ObjectSlot;
        var differing = Enumerable.Range(0, Math.Min(ma.Length, mb.Length)).Where(i => !ma[i].AsSpan().SequenceEqual(mb[i])).ToArray();
        bool membersOk = ma.Length == mb.Length && differing.All(i => i == ev || i == ch || i == ob) && differing.Contains(ev);
        Console.WriteLine((membersOk ? "PASS" : "FAIL") + $" Gen7NurseReal_OnlyTheAreaEventsCharactersAndObjectsChanged (miembros {string.Join(",", differing)})");
        var entry = session.History.Read().Entries.Last();
        bool recorded = entry.Parameters.GetValueOrDefault("ActionId") == Map3dsGen7NurseService.ActionId;
        Console.WriteLine((recorded ? "PASS" : "FAIL") + $" Gen7NurseReal_HistoryEntry «{entry.Action}»");
        bool canRestore = service.CanRestore(area);
        service.Apply(service.PreviewRestore(area));
        var restoredMembers = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        bool restored = restoredMembers.Length == mb.Length && Enumerable.Range(0, mb.Length).All(i => restoredMembers[i].AsSpan().SequenceEqual(mb[i]));
        Console.WriteLine((canRestore && restored ? "PASS" : "FAIL") + " Gen7NurseReal_RestorePutsBackEveryOriginalMember");
        service.Apply(service.Preview(Gen7MapBackend.FromConfig(session.GameConfiguration), area, x, y, facing));
        bool stale = false;
        try { service.Preview(before, area, x, y, facing); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { stale = true; }
        Console.WriteLine((stale ? "PASS" : "FAIL") + " Gen7NurseReal_AnEditorThatReadOldFilesCannotWrite");
        if (!(previewSafe && one && others && model && machineOk && membersOk && recorded && canRestore && restored && stale)) throw new InvalidDataException("La prueba real de la enfermera de USUM falló.");
        Console.WriteLine("Enfermera aplicada en la copia para la prueba jugable · gameplay NOT tested");
    }
}
