using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;

/// <summary>«Añadir enfermera» in Gen6: a copy of the game's own Pokémon Center nurse (script 2100) appended to a zone.</summary>
internal static class Map3dsNurseChecks
{
    private const int NurseModel = 59;

    private static byte[] SetNpc(byte[] zone, int npc, int offset, int value)
    {
        var copy = (byte[])zone.Clone();
        var r = new Gen6Zone(copy).Entities.Records.First(x => x.Kind == MapEventKind.Npc && x.Index == npc);
        BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(r.Offset + offset), (ushort)value);
        return copy;
    }

    /// <summary>
    /// Zone 0: a Pokémon Center with its nurse. Zone 1: three NPCs, a prop, a warp and a trigger. Zone 2: five NPCs (the
    /// game's largest count). Zone 3: positions that are not cells. Matrix 2×2 with no block in the lower left.
    /// </summary>
    internal static byte[][] Zones(bool withNurse = true)
    {
        var pc = Map3dsGen6Checks.Zone(0, 3, [new(20, 22, Model: NurseModel, Face: 1, Sight: 0, Angle: 0)]);
        if (withNurse) pc = SetNpc(pc, 0, Gen6ZoneEdit.NpcScript, Gen6Nurse.Script);
        return
        [
            pc,
            Map3dsGen6Checks.Zone(0, 3, [new(45, 12, Angle: 0), new(46, 2, Angle: 0), new(47, 12, Angle: 0)], props: [(44, 10, 2, 1)],
                warps: [(50 * 18 + 9, 20 * 18 + 9, 1, 0)], triggers: [(41, 30, 3, 1)]),
            Map3dsGen6Checks.Zone(0, 3, [new(1, 1), new(2, 1), new(3, 1), new(4, 1), new(5, 1)]),
            Map3dsGen6Checks.Zone(0, 3, [new(500, 500), new(600, 3), new(2, 2)]),
        ];
    }

    internal static Gen6MapBackend Backend(byte[][] zones)
    {
        var matrix = Map3dsGen6Checks.Matrix(2, 2, 0, 1, -1, 1);
        var blocks = new[] { Map3dsGen6Checks.Block(0x20u, odd: (15 * 40) + 30), Map3dsGen6Checks.Block(0x20u) };
        return new Gen6MapBackend(Gen6Title.XY, zones.Length, i => zones[i], 1, _ => matrix, blocks.Length, i => blocks[i], ["", "", "", "Pueblo Prueba"]);
    }

    internal static void Run(Action<bool, string> check)
    {
        var zones = Zones();
        var b = Backend(zones);
        check(b.Nurse is { Zone: 0, Index: 0, Model: NurseModel, Copies: 1 } n && n.Record.Length == 0x30 && b.NurseSummary.Contains("zona 000") && b.NurseSummary.Contains("2100"),
            "Nurse_TemplateIsTheGamesOwnNurse");
        var other = SetNpc(SetNpc(zones[1], 0, Gen6ZoneEdit.NpcScript, Gen6Nurse.Script), 0, Gen6ZoneEdit.NpcModel, NurseModel + 1);
        var walking = SetNpc(zones[0], 0, Gen6ZoneEdit.NpcMovement, 2);
        check(Gen6Nurse.Find([(0, new Gen6Zone(zones[0])), (1, new Gen6Zone(other))]) is null && Gen6Nurse.Find([(0, new Gen6Zone(walking))]) is null
            && Gen6Nurse.Find([(1, new Gen6Zone(zones[1]))]) is null, "Nurse_DisagreeingOrMissingNursesAreNotGuessed");
        var none = Backend(Zones(withNurse: false));
        check(none.Nurse is null && none.NurseSummary.StartsWith("No se encontró") && !none.ValidateAddNurse(1, 30, 16, 180).Allowed, "Nurse_GameWithoutNurseCannotAddOne");

        // Insertion: one record after the last NPC, everything else kept.
        var raw = b.ZoneBytes(1);
        var added = Gen6ZoneEdit.AddNpc(raw, b.Nurse!.Record, 30, 15, 2);
        var before = new Gen6Zone(raw); var after = new Gen6Zone(added);
        var npcs = after.Entities.Records.Where(r => r.Kind == MapEventKind.Npc).ToArray();
        var nr = npcs.Last();
        var e = after.Entities;
        check(added.Length == raw.Length + 0x30 && npcs.Length == 4 && nr.Index == 3 && e.U16(nr, 0) == 3 && e.U16(nr, 0x28) == 30 && e.U16(nr, 0x2A) == 15 && e.U16(nr, 0x0C) == 2
            && e.U16(nr, Gen6ZoneEdit.NpcScript) == Gen6Nurse.Script && e.U16(nr, Gen6ZoneEdit.NpcModel) == NurseModel, "AddNpc_AppendsTheNurseAfterTheLastNpc");
        var expected = (byte[])b.Nurse.Record.Clone();
        foreach (int f in new[] { 0x00, 0x0C, 0x28, 0x2A }) BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(f), BinaryPrimitives.ReadUInt16LittleEndian(e.Bytes(nr)[f..]));
        check(e.Bytes(nr).SequenceEqual(expected), "AddNpc_RecordIsTheTemplateExceptIdFacingAndCell");
        bool recordsKept = before.Entities.Records.All(r => before.Entities.Bytes(r).SequenceEqual(e.Bytes(after.Entities.Records.First(x => x.Kind == r.Kind && x.Index == r.Index))));
        int scriptBefore = before.Entities.Records.Max(r => r.Offset + r.Size), scriptAfter = after.Entities.Records.Max(r => r.Offset + r.Size);
        bool scriptKept = raw.AsSpan(scriptBefore, before.Pack.Start(2) - scriptBefore).SequenceEqual(added.AsSpan(scriptAfter, after.Pack.Start(2) - scriptAfter));
        bool partsKept = Enumerable.Range(0, before.Pack.Count).Where(p => p != 1).All(p => before.Pack.Part(p).SequenceEqual(after.Pack.Part(p)))
            && Enumerable.Range(2, before.Pack.Count - 1).All(p => after.Pack.Start(p) == before.Pack.Start(p) + 0x30) && after.Pack.Start(1) == before.Pack.Start(1);
        check(recordsKept && scriptKept && partsKept, "AddNpc_OtherRecordsEntityScriptAndPartsAreKept");
        check(BinaryPrimitives.ReadInt32LittleEndian(added.AsSpan(after.Pack.Start(1))) == BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(before.Pack.Start(1))) + 0x30
            && added[after.Pack.Start(1) + 5] == 4, "AddNpc_LengthAndCountGrowByOneRecord");
        bool badTemplate = false, badFacing = false, badCell = false, full = false;
        try { Gen6ZoneEdit.AddNpc(raw, new byte[0x2C], 1, 1, 1); } catch (ArgumentException) { badTemplate = true; }
        try { Gen6ZoneEdit.AddNpc(raw, b.Nurse.Record, 1, 1, 4); } catch (ArgumentOutOfRangeException) { badFacing = true; }
        try { Gen6ZoneEdit.AddNpc(raw, b.Nurse.Record, 0x10000, 1, 1); } catch (ArgumentOutOfRangeException) { badCell = true; }
        var crowded = Map3dsGen6Checks.Zone(0, 3, Enumerable.Range(0, 255).Select(i => new Map3dsGen6Checks.SyntheticNpc(i % 80, i / 80)).ToArray());
        try { Gen6ZoneEdit.AddNpc(crowded, b.Nurse.Record, 1, 70, 1); } catch (InvalidOperationException) { full = true; }
        check(badTemplate && badFacing && badCell && full, "AddNpc_RefusesBadTemplatesDirectionsCellsAndFullCounts");

        // Validation of the cell.
        var free = b.ValidateAddNurse(1, 31, 15, 180);
        check(free.Allowed && free.Reason.Contains("NPC 3") && free.Reason.Contains("0x00000020") && free.Reason.Contains("3 NPC") && free.Reason.Contains("No se ha probado"),
            "NurseCheck_FreeCellIsAllowedAndExplained");
        check(b.ValidateAddNurse(1, 30, 15, 180) is { Allowed: true } odd && odd.Reason.Contains("0x01000021") && odd.Reason.Contains("ningún NPC"), "NurseCheck_UnusualGroundIsReported");
        check(new[] { (45, 12), (45, 10), (50, 20), (42, 30) }.All(c => b.ValidateAddNurse(1, c.Item1, c.Item2, 180) is { Allowed: false } r && r.Reason.Contains("ocupa")),
            "NurseCheck_CellsOfNpcsPropsWarpsAndTriggersAreRefused");
        check(!b.ValidateAddNurse(1, 80, 5, 180).Allowed && b.ValidateAddNurse(1, 5, 50, 180).Reason.Contains("sin bloque") && !b.ValidateAddNurse(1, 31, 15, 45).Allowed
            && !b.ValidateAddNurse(9, 31, 15, 180).Allowed, "NurseCheck_OutsideNoBlockBadDirectionAndMissingZoneAreRefused");
        check(b.NpcCeiling == 5 && b.ValidateAddNurse(2, 30, 15, 180).Reason.Contains("máximo") && b.ValidateAddNurse(3, 30, 15, 180).Reason.Contains("no son casillas"),
            "NurseCheck_CountCeilingAndUntrustedZonesAreRefused");
        var reread = Backend([zones[0], added, zones[2], zones[3]]);
        var shown = reread.ZoneEvents(1).Last(v => v.Kind == MapEventKind.Npc);
        check(shown is { Index: 3, X: 30, Y: 15, Placed: true } && shown.Properties.First(p => p.Name == "Script").Value == "2100" && reread.View(1, MapEventKind.Npc, 3) is { Facing: 270 },
            "NurseCheck_AddedNurseReadsBackAsANormalNpc");
    }

    internal static void RunUi(Action<bool, string> check)
    {
        var zones = Zones();
        var calls = new List<(int Zone, int X, int Y, double Facing)>();
        int reloads = 0;
        Gen6MapBackend reloaded = Backend(zones);
        var mover = new Map3dsMover((b, z, n, x, y) => "m", () => { reloads++; return reloaded; }, addNurse: (b, z, x, y, f) =>
        {
            calls.Add((z, x, y, f));
            var g = (Gen6MapBackend)b;
            var updated = (byte[][])zones.Clone();
            updated[z] = Gen6ZoneEdit.AddNpc(g.ZoneBytes(z), g.Nurse!.Record, x, y, Gen6MapBackend.FacingValue(f)!.Value);
            reloaded = Backend(updated);
            return $"Añadir enfermera en la zona {z:000} ({x}, {y})";
        });
        var view = new Map3dsEditorView(Backend(zones), mover);
        Layout(view);
        view.ShowZone(1);
        check(view.NurseButton is not null && new Map3dsEditorView(Backend(zones)).NurseButton is null && new Map3dsEditorView(Backend(zones), new Map3dsMover((b, z, n, x, y) => "m", () => Backend(zones))).NurseButton is null,
            "Map3dsNurseUi_ButtonOnlyWithAProjectThatCanAddNurses");
        check(view.ShowNursePanel() && view.Canvas.PickingCell && view.NurseX is { Text: "" } && view.NurseDirection is { SelectedValue: 180.0 } && view.NurseSave is { IsEnabled: false }
            && view.Details.Children.OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text.Contains("zona 000")), "Map3dsNurseUi_PanelExplainsTheCopiedNurse");
        view.Canvas.PickCell(45, 12);
        check(view.NurseX!.Text == "45" && view.PendingNurse is null && view.NurseSave!.IsEnabled == false && view.NurseResult!.Text.Contains("ocupa") && view.Canvas.PendingNurse is null,
            "Map3dsNurseUi_OccupiedCellCannotBeSaved");
        view.Canvas.PickCell(31, 15);
        check(view.PendingNurse == (31, 15, 180.0) && view.Canvas.PendingNurse == (31, 15, 180.0) && view.NurseSave!.IsEnabled && calls.Count == 0,
            "Map3dsNurseUi_ClickedCellIsPreviewedWithoutWriting");
        view.NurseDirection!.SelectedValue = 90.0;
        UiPump.Idle();
        check(view.PendingNurse == (31, 15, 90.0) && view.Canvas.PendingNurse is { Facing: 90 }, "Map3dsNurseUi_DirectionUpdatesThePreview");
        check(view.SaveNurse() && calls.SequenceEqual([(1, 31, 15, 90.0)]) && reloads == 1 && view.Status.Text.Contains("historial") && !view.Canvas.PickingCell
            && view.Canvas.Selected is { Kind: MapEventKind.Npc, Index: 3, X: 31, Y: 15 }, "Map3dsNurseUi_SaveCommitsAndSelectsTheNewNurse");
        view.ShowNursePanel();
        view.EventList.SelectedIndex = 0;
        UiPump.Idle();
        check(!view.Canvas.PickingCell && view.NurseX is null && view.Canvas.PendingNurse is null, "Map3dsNurseUi_SelectingAnEventLeavesThePanel");
    }

    private static void Layout(FrameworkElement content)
    {
        content.Measure(new Size(1400, 860)); content.Arrange(new Rect(0, 0, 1400, 860)); content.UpdateLayout(); UiPump.Idle(); content.UpdateLayout();
    }

    private static void RequireTestFolder(string full)
    {
        if (!full.Contains("Pruebas", StringComparison.OrdinalIgnoreCase)) throw new IOException("Por seguridad, la prueba solo escribe en una copia dentro de una carpeta de pruebas.");
    }

    /// <summary>
    /// Real persistence on an X/Y or OR/AS copy: preview, commit, reopen, only the new record inserted, other zones identical,
    /// undo, external changes; then reapplied for the gameplay test. With x &lt; 0 it only lists the zone.
    /// </summary>
    internal static void Probe(string copyRoot, int zone, int x, int y, double facing)
    {
        string full = Path.GetFullPath(copyRoot);
        RequireTestFolder(full);
        var session = GameProjectSession.Open(full, false);
        var service = new Map3dsNpcMoveService(session);
        string archive = service.ZoneArchive;
        byte[] original = File.ReadAllBytes(archive);
        string originalHash = Convert.ToHexString(SHA256.HashData(original));
        var before = Gen6MapBackend.FromConfig(session.GameConfiguration);
        Console.WriteLine(before.NurseSummary + $" Máximo de NPC por zona: {before.NpcCeiling}.");
        if (x < 0)
        {
            foreach (var z in before.Zones.Where(z => zone < 0 || z.Id == zone))
                Console.WriteLine($"{z.Name} · {before.ZoneEvents(z.Id).Count(v => v.Kind == MapEventKind.Npc)} NPC · " +
                    string.Join(" ", before.ZoneEvents(z.Id).Where(v => v.Kind == MapEventKind.Npc && v.Placed).Select(v => $"({v.X},{v.Y})")));
            Console.WriteLine("Solo lectura: no se escribió nada.");
            return;
        }
        int count = before.ZoneEvents(zone).Count(v => v.Kind == MapEventKind.Npc);
        Console.WriteLine($"Zona {zone} ({before.Zones.First(z => z.Id == zone).Name}): {count} NPC · enfermera nueva en ({x}, {y}) mirando {Gen6MapBackend.DirectionName(facing)}");
        Console.WriteLine("Comprobación: " + before.ValidateAddNurse(zone, x, y, facing).Reason);

        var proposal = service.PreviewAddNurse(before, zone, x, y, facing);
        bool previewSafe = File.ReadAllBytes(archive).AsSpan().SequenceEqual(original);
        Console.WriteLine((previewSafe ? "PASS" : "FAIL") + " NurseReal_PreviewWritesNothing");
        service.Apply(proposal);
        var after = Gen6MapBackend.FromConfig(session.GameConfiguration);
        var added = after.ZoneEvents(zone).Where(v => v.Kind == MapEventKind.Npc).ToArray();
        var nurse = added.Last();
        bool present = added.Length == count + 1 && nurse is { Placed: true } && (int)nurse.X == x && (int)nurse.Y == y
            && nurse.Properties.First(p => p.Name == "Script").Value == Gen6Nurse.Script.ToString() && nurse.Properties.First(p => p.Name == "Modelo").Value == before.Nurse!.Model.ToString()
            && after.View(zone, MapEventKind.Npc, nurse.Index) is { } v && Gen6MapBackend.FacingValue(v.Facing) == Gen6MapBackend.FacingValue(facing);
        Console.WriteLine((present ? "PASS" : "FAIL") + $" NurseReal_ReopenedWithTheNewNurse (NPC {nurse.Index} en ({nurse.X}, {nurse.Y}), script 2100, modelo {before.Nurse!.Model})");
        var zb = before.Zone(zone); var za = after.Zone(zone);
        bool kept = zb.Entities.Records.All(r => zb.Entities.Bytes(r).SequenceEqual(za.Entities.Bytes(za.Entities.Records.First(n => n.Kind == r.Kind && n.Index == r.Index))))
            && Enumerable.Range(0, zb.Pack.Count).Where(p => p != 1).All(p => zb.Pack.Part(p).SequenceEqual(za.Pack.Part(p)))
            && za.Pack.Raw.Length == zb.Pack.Raw.Length + 0x30;
        Console.WriteLine((kept ? "PASS" : "FAIL") + " NurseReal_EveryOtherRecordScriptAndPartIsKept");
        var membersBefore = new GARC.MemGARC(original).Files;
        var membersAfter = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        bool otherZones = membersAfter.Length == membersBefore.Length && Enumerable.Range(0, membersAfter.Length).All(i => i == zone || membersAfter[i].AsSpan().SequenceEqual(membersBefore[i]));
        Console.WriteLine((otherZones ? "PASS" : "FAIL") + " NurseReal_OtherZonesByteIdentical");
        var entry = session.History.Read().Entries.Last();
        bool recorded = entry.Parameters.GetValueOrDefault("ActionId") == Map3dsNpcMoveService.NurseActionId && entry.Parameters.GetValueOrDefault("Gameplay") == "not tested";
        Console.WriteLine((recorded ? "PASS" : "FAIL") + $" NurseReal_HistoryEntry «{entry.Action}»");
        RestorePoints.Undo(session.History, session.ProjectRoot, entry.Id);
        bool restored = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))) == originalHash;
        Console.WriteLine((restored ? "PASS" : "FAIL") + " NurseReal_UndoRestoresTheOriginalArchive");
        bool stale = false;
        try { service.PreviewAddNurse(Gen6MapBackend.FromConfig(session.GameConfiguration), zone, x, y, facing); } catch (IOException) { stale = true; }
        Console.WriteLine((stale ? "PASS" : "FAIL") + " NurseReal_ChangesOutsideTheEditorRequireAReload");
        if (!(previewSafe && present && kept && otherZones && recorded && restored && stale)) throw new InvalidDataException("La prueba real de «Añadir enfermera» falló.");
        service.Refresh();
        service.Apply(service.PreviewAddNurse(Gen6MapBackend.FromConfig(session.GameConfiguration), zone, x, y, facing));
        Console.WriteLine("Enfermera añadida de nuevo en la copia para la prueba jugable · gameplay NOT tested");
    }

    /// <summary>Rebuilds the copy into a 3DS image and checks the nurse is in it (re-extracted, or the edited archive found byte for byte with «-»).</summary>
    internal static void Rebuild(string copyRoot, string image, string reextract, int zone, int x, int y)
    {
        var project = GameProjectSession.Open(copyRoot, false);
        if (reextract == "-" && File.Exists(image)) Console.WriteLine($"Imagen existente: {image} (no se reconstruye)");
        else
        {
            var built = new GameRomRebuildService(project).BuildAsync(RomRebuildKind.Full3DS, image).GetAwaiter().GetResult();
            Console.WriteLine($"Imagen: {built.File}, {built.Bytes} bytes, SHA256 {built.Hash}");
        }
        if (reextract == "-")
        {
            var cfg = project.GameConfiguration;
            long at = Map3dsTrainerFacingChecks.FindInImage(image, Path.Combine(cfg.RomFS, cfg.GetGARCFileName("encdata")));
            Console.WriteLine((at >= 0 ? "PASS" : "FAIL") + $" NurseRebuild_ImageContainsTheEditedZoneArchive (desplazamiento 0x{at:X} en el RomFS); gameplay NOT tested");
            if (at < 0) throw new InvalidDataException("La imagen no contiene el archivo de zonas editado.");
            return;
        }
        RealRebuildChecks.VerifyExisting(copyRoot, image, reextract);
        var backend = Gen6MapBackend.FromConfig(GameProjectSession.Open(reextract, false).GameConfiguration);
        var nurse = backend.ZoneEvents(zone).LastOrDefault(v => v.Kind == MapEventKind.Npc);
        bool ok = nurse is not null && (int)nurse.X == x && (int)nurse.Y == y && nurse.Properties.First(p => p.Name == "Script").Value == Gen6Nurse.Script.ToString();
        Console.WriteLine((ok ? "PASS" : "FAIL") + $" NurseRebuild_ReextractedImageKeepsTheNurse; gameplay NOT tested");
        if (!ok) throw new InvalidDataException("La imagen reconstruida no conserva la enfermera.");
    }
}
