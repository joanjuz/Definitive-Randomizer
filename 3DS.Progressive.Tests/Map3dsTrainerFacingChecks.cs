using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;

/// <summary>Facing and sight of existing trainers: Gen6 (u16 0x0C/0x0E of the NPC record) and Ultra Sun/Ultra Moon (rotation of the 84-byte trainer record).</summary>
internal static class Map3dsTrainerFacingChecks
{
    /// <summary>Zone 0 of a Gen6 backend with four NPCs: a trainer looking down with sight 3, a plain NPC looking left, an NPC with value 5 and one with an angle.</summary>
    internal static Gen6MapBackend Gen6(out byte[] zone)
    {
        zone = Map3dsGen6Checks.Zone(0, 3,
        [
            new(45, 12, Face: 1, Sight: 3, Angle: 0), new(46, 2, Face: 2, Sight: 0, Angle: 0), new(47, 12, Face: 5, Sight: 2, Angle: 0), new(48, 12, Face: 0, Sight: 2),
        ]);
        var z = zone;
        var matrices = new[] { Map3dsGen6Checks.Matrix(2, 2, 0, 1, -1, 1) };
        var blocks = new[] { Map3dsGen6Checks.Block(0x10u), Map3dsGen6Checks.Block(0x20u) };
        return new Gen6MapBackend(Gen6Title.ORAS, 1, _ => z, matrices.Length, i => matrices[i], blocks.Length, i => blocks[i], ["", "", "", "Pueblo Prueba"]);
    }

    private static byte[] Shapes(int type, params float[] values)
    {
        var b = new byte[8 + (values.Length * 4)];
        BinaryPrimitives.WriteInt32LittleEndian(b, 1); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), type);
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8 + (i * 4)), values[i]);
        return b;
    }

    /// <summary>
    /// Trainer zone file: row 0 stands still with two cylinders centred on it; row 1 walks a route; row 2 has a box
    /// body; row 3 stands but its cylinder is off-centre. All start without rotation (facing down on the map).
    /// </summary>
    internal static byte[] TrainerZone()
    {
        const int rows = 4, size = Gen7ZoneEdit.TrainerSize;
        var tail = new List<byte>();
        int head = 4 + (rows * size);
        int Add(byte[] b) { int at = head + tail.Count; tail.AddRange(b); return at; }
        var d = new byte[head];
        BinaryPrimitives.WriteInt32LittleEndian(d, rows);
        for (int n = 0; n < rows; n++)
        {
            int o = 4 + (n * size);
            float x = 1000 + (n * 500), z = 2000;
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o), 7);
            BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(o + 4), x); BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(o + 8), 10); BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(o + 12), z);
            BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(o + 0x1C), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o + 44), (uint)(101 + n));
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o + 48), 68); BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o + 52), (uint)(1001 + n));
            var movement = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(movement, n == 1 ? 8u : 4u); BinaryPrimitives.WriteUInt32LittleEndian(movement.AsSpan(4), 97);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o + 0x3C), (uint)Add(movement));
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o + 0x40), (uint)Add(n == 3 ? Shapes(0, x + 50, 10, z, 80, 150) : Shapes(0, x, 10, z, 80, 150)));
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o + 0x44), (uint)Add(n == 2 ? Shapes(1, x, 10, z, 0, 0, 0, 1, 40, 150, 40) : Shapes(0, x, 10, z, 40, 150)));
            if (n == 1)
            {
                var route = new byte[8 + 24];
                BinaryPrimitives.WriteUInt16LittleEndian(route, 2);
                BinaryPrimitives.WriteSingleLittleEndian(route.AsSpan(8), x); BinaryPrimitives.WriteSingleLittleEndian(route.AsSpan(16), z);
                BinaryPrimitives.WriteSingleLittleEndian(route.AsSpan(20), x + 300); BinaryPrimitives.WriteSingleLittleEndian(route.AsSpan(28), z);
                BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o + 0x4C), (uint)Add(route));
            }
        }
        return [.. d, .. tail];
    }

    internal static byte[] TrainerEd(byte[] zoneFile)
    {
        var ps = Enumerable.Range(0, 17).Select(_ => Array.Empty<byte>()).ToArray();
        ps[Gen7Events.TrainerPack] = Map3dsGen6Checks.Pack("EM", zoneFile);
        return Map3dsGen6Checks.Pack("ED", ps);
    }

    internal static Gen7MapBackend Gen7(out byte[] ed)
    {
        var e = ed = TrainerEd(TrainerZone());
        var files = new Dictionary<int, byte[]> { [0] = e, [2] = Map3dsGen6Checks.Pack("TR", Map3dsGen7Checks.Tr(1, 1, 4200, 5)) };
        return new Gen7MapBackend("Prueba", 22, i => files.GetValueOrDefault(i, []), 10, _ => []);
    }

    internal static void Run(Action<bool, string> check)
    {
        // Gen6: value ↔ direction (0 up, 1 down, 2 left, 3 right), measured on X and Alpha Sapphire data.
        check(Gen6MapBackend.FacingDegrees(0) == 0 && Gen6MapBackend.FacingDegrees(1) == 180 && Gen6MapBackend.FacingDegrees(2) == 270 && Gen6MapBackend.FacingDegrees(3) == 90
            && Gen6MapBackend.FacingDegrees(4) is null && Gen6MapBackend.FacingValue(-90) == 2 && Gen6MapBackend.FacingValue(45) is null,
            "Gen6Facing_FourVerifiedDirectionsOnly");
        var g6 = Gen6(out var zone);
        var raw = g6.ZoneBytes(0);
        var turned = Gen6ZoneEdit.SetNpcView(raw, 0, 0, 5);
        var entities = new Gen6Zone(turned).Entities;
        var r0 = entities.Records.First(r => r.Kind == MapEventKind.Npc && r.Index == 0);
        var diff = Enumerable.Range(0, raw.Length).Where(i => raw[i] != turned[i]).ToArray();
        check(entities.U16(r0, 0x0C) == 0 && entities.U16(r0, 0x0E) == 5 && diff.Length > 0 && diff.All(i => i >= r0.Offset + 0x0C && i < r0.Offset + 0x10) && turned.Length == raw.Length,
            "Gen6Facing_EditWritesOnlyFacingAndSight");
        var facingOnly = Gen6ZoneEdit.SetNpcView(raw, 0, 3, null);
        check(new Gen6Zone(facingOnly).Entities.U16(r0, 0x0E) == 3 && Enumerable.Range(0, raw.Length).Count(i => raw[i] != facingOnly[i]) == 1, "Gen6Facing_SightIsKeptWhenNotGiven");
        bool badValue = false, badNpc = false;
        try { Gen6ZoneEdit.SetNpcView(raw, 0, 4, null); } catch (ArgumentOutOfRangeException) { badValue = true; }
        try { Gen6ZoneEdit.SetNpcView(raw, 9, 0, null); } catch (ArgumentOutOfRangeException) { badNpc = true; }
        check(badValue && badNpc, "Gen6Facing_EditRefusesUnverifiedValuesAndMissingNpcs");
        var view = g6.View(0, MapEventKind.Npc, 0);
        check(view is { Facing: 180, Sight: 3, FacingWritable: true, SightWritable: true, FreeAngle: false, MaxSight: Gen6MapBackend.MaxSight }, "Gen6Facing_TrainerViewIsReadable");
        check(g6.View(0, MapEventKind.Npc, 1) is { Facing: 270, Sight: 0, FacingWritable: true, SightWritable: false }
            && g6.View(0, MapEventKind.Npc, 2) is { FacingWritable: false } v2 && double.IsNaN(v2.Facing)
            && g6.View(0, MapEventKind.Npc, 3) is { FacingWritable: false } && g6.View(0, MapEventKind.Warp, 0) is null,
            "Gen6Facing_ValuesAbove3AndAnglesStayReadOnly");
        check(g6.ValidateView(0, MapEventKind.Npc, 0, 0, 5).Allowed && g6.ValidateView(0, MapEventKind.Npc, 0, 90, null).Allowed
            && !g6.ValidateView(0, MapEventKind.Npc, 0, 45, null).Allowed && !g6.ValidateView(0, MapEventKind.Npc, 0, 180, 3).Allowed
            && g6.ValidateView(0, MapEventKind.Npc, 0, 180, 3).Reason.Contains("No hay cambios"), "Gen6Facing_ValidatesDirectionAndRejectsNoChange");
        check(!g6.ValidateView(0, MapEventKind.Npc, 0, 0, 9).Allowed && !g6.ValidateView(0, MapEventKind.Npc, 0, 0, 0).Allowed
            && !g6.ValidateView(0, MapEventKind.Npc, 1, 0, 2).Allowed && g6.ValidateView(0, MapEventKind.Npc, 1, 0, null).Allowed
            && !g6.ValidateView(0, MapEventKind.Npc, 2, 0, null).Allowed, "Gen6Facing_SightOnlyChangesForTrainersWithinItsRange");
        check(g6.SightCells(0, MapEventKind.Npc, 0, 0, 3).SequenceEqual([(45, 11), (45, 10), (45, 9)]) && g6.SightCells(0, MapEventKind.Npc, 1, 0, 5).Count == 2
            && g6.SightCells(0, MapEventKind.Npc, 0, 90, 2).SequenceEqual([(46, 12), (47, 12)]), "Gen6Facing_SightCellsFollowTheDirectionAndStopAtTheMatrix");
        var props = g6.ZoneEvents(0).First(e => e.Kind == MapEventKind.Npc && e.Index == 0).Properties;
        check(props.First(p => p.Name == "Orientación") is { Writable: true, Value: "abajo (valor 1)" } && props.First(p => p.Name == "Alcance de visión") is { Writable: true, Value: "3 casillas" },
            "Gen6Facing_PropertiesShowDirectionAndSight");

        // Gen7: trainer rotation is a quaternion about the vertical axis; no rotation looks down the map (+Z).
        check(Gen7ZoneEdit.CompassOfYaw(0) == 180 && Gen7ZoneEdit.CompassOfYaw(-90) == 270 && Gen7ZoneEdit.CompassOfYaw(90) == 90 && Gen7ZoneEdit.YawOfCompass(0) == 180,
            "Gen7Facing_YawToMapDirection");
        var g7 = Gen7(out var ed);
        var events = g7.ZoneEvents(0).Where(e => e.Kind == MapEventKind.Npc).ToArray();
        check(events.Length == 4 && g7.View(0, MapEventKind.Npc, events[0].Index) is { Facing: 180, Sight: null, FacingWritable: true, FreeAngle: true },
            "Gen7Facing_StandingTrainerIsWritable");
        check(g7.View(0, MapEventKind.Npc, events[1].Index) is { FacingWritable: false } a && a.Note.Contains("quieto")
            && g7.View(0, MapEventKind.Npc, events[2].Index) is { FacingWritable: false } b && b.Note.Contains("cilindro")
            && g7.View(0, MapEventKind.Npc, events[3].Index) is { FacingWritable: false } c && c.Note.Contains("centrada"), "Gen7Facing_RoutesBoxesAndOffCentreShapesStayReadOnly");
        var turnedEd = Gen7ZoneEdit.TurnTrainer(ed, 0, 0, 90);
        var (zs, _) = Gen7ZoneEdit.ZoneFile(turnedEd, Gen7Events.TrainerPack, 0);
        int rec = zs + 4;
        var changed = Enumerable.Range(0, ed.Length).Where(i => ed[i] != turnedEd[i]).ToArray();
        float qy = BinaryPrimitives.ReadSingleLittleEndian(turnedEd.AsSpan(rec + 0x14)), qw = BinaryPrimitives.ReadSingleLittleEndian(turnedEd.AsSpan(rec + 0x1C));
        check(turnedEd.Length == ed.Length && changed.Length > 0 && changed.All(i => (i >= rec + 0x14 && i < rec + 0x18) || (i >= rec + 0x1C && i < rec + 0x20))
            && Math.Abs(qy - Math.Sin(Math.PI / 4)) < 1e-6 && Math.Abs(qw - Math.Cos(Math.PI / 4)) < 1e-6, "Gen7Facing_TurnWritesOnlyTheRotationYAndW");
        var reread = Gen7Events.Records(turnedEd.AsSpan(zs, Gen7ZoneEdit.ZoneFile(turnedEd, Gen7Events.TrainerPack, 0).Length).ToArray(), 84, 7).First();
        check(Gen7ZoneEdit.Yaw(reread) is double y && Math.Abs(Gen7ZoneEdit.CompassOfYaw(y) - 90) < 1e-3, "Gen7Facing_TurnedTrainerReadsBackItsDirection");
        bool route = false, box = false, row = false;
        try { Gen7ZoneEdit.TurnTrainer(ed, 0, 1, 0); } catch (InvalidOperationException) { route = true; }
        try { Gen7ZoneEdit.TurnTrainer(ed, 0, 2, 0); } catch (InvalidOperationException) { box = true; }
        try { Gen7ZoneEdit.TurnTrainer(ed, 0, 9, 0); } catch (ArgumentOutOfRangeException) { row = true; }
        check(route && box && row, "Gen7Facing_TurnRefusesRoutesBoxesAndMissingRows");
        check(g7.ValidateView(0, MapEventKind.Npc, events[0].Index, 270, null).Allowed && !g7.ValidateView(0, MapEventKind.Npc, events[0].Index, 180.2, null).Allowed
            && !g7.ValidateView(0, MapEventKind.Npc, events[0].Index, 0, 4).Allowed && g7.SightCells(0, MapEventKind.Npc, events[0].Index, 0, 4).Count == 0,
            "Gen7Facing_SightIsNotWrittenOrDrawn");
        var tp = events[0].Properties;
        check(tp.First(p => p.Name == "Orientación") is { Writable: true } o && o.Value.StartsWith("180° (abajo)") && tp.Any(p => p.Name == "Alcance de visión" && p.Value == "no localizado" && !p.Writable)
            && tp.Count(p => p.Writable) == 1, "Gen7Facing_PropertiesShowFacingAndUnlocatedSight");
        check(g7.ValidateMove(0, MapEventKind.Npc, events[0].Index, 5, 5).Reason.Contains("entrenadores"), "Gen7Facing_TrainersStillCannotBeMoved");
    }

    private static void Layout(FrameworkElement content, double w, double h)
    {
        content.Measure(new Size(w, h)); content.Arrange(new Rect(0, 0, w, h)); content.UpdateLayout(); UiPump.Idle(); content.UpdateLayout();
    }

    private static void Pick(Map3dsEditorView view, Func<MapEventView, bool> which)
    {
        view.EventList.SelectedIndex = view.EventList.Items.Cast<ListBoxItem>().ToList().FindIndex(i => i.Tag is MapEventView e && which(e));
        UiPump.Idle();
    }

    internal static void RunUi(Action<bool, string> check)
    {
        var calls = new List<(int Zone, int Npc, double Facing, int? Sight)>();
        int reloads = 0;
        var mover = new Map3dsMover((b, z, n, x, y) => "m", () => { reloads++; return Gen6(out _); }, view: (b, z, n, f, s) => { calls.Add((z, n, f, s)); return $"Cambiar orientación del NPC {n}"; });
        var g6 = new Map3dsEditorView(Gen6(out _), mover);
        Layout(g6, 1400, 860);
        Pick(g6, e => e.Kind == MapEventKind.Npc && e.Index == 0);
        check(g6.ViewDirection is { SelectedValue: 180.0 } && g6.ViewSight is { Text: "3", IsEnabled: true } && g6.ViewSave is { IsEnabled: false } && g6.MoveX is not null,
            "Map3dsFacingUi_Gen6TrainerShowsDirectionSightAndMove");
        check(g6.Canvas.ViewOverlay is { Pending: false, Facing: 180 } ov && ov.Cells.SequenceEqual([(45, 13), (45, 14), (45, 15)]), "Map3dsFacingUi_SelectedTrainerDrawsItsSight");
        g6.ViewSight!.Text = "12";
        check(!g6.PreviewView(g6.Canvas.Selected!) && g6.ViewSave!.IsEnabled == false && g6.ViewResult!.Text.Contains("entre 1 y") && calls.Count == 0, "Map3dsFacingUi_SightOutOfRangeCannotBeSaved");
        g6.ViewSight.Text = "4"; g6.ViewDirection!.SelectedValue = 0.0;
        check(g6.PreviewView(g6.Canvas.Selected!) && g6.ViewSave!.IsEnabled && g6.PendingView is { Facing: 0, Sight: 4 } && calls.Count == 0
            && g6.Canvas.ViewOverlay is { Pending: true, Facing: 0 } p && p.Cells.Count == 4 && p.Cells[0] == (45, 11), "Map3dsFacingUi_PreviewDrawsTheNewSightWithoutWriting");
        check(g6.SaveView() && calls.Count == 1 && calls[0] == (0, 0, 0.0, 4) && reloads == 1 && g6.PendingView is null && g6.Status.Text.Contains("historial"),
            "Map3dsFacingUi_SaveCommitsThroughTheProject");
        Pick(g6, e => e.Kind == MapEventKind.Npc && e.Index == 2);
        check(g6.ViewDirection is null && g6.MoveX is not null, "Map3dsFacingUi_UnverifiedFacingHasNoPanel");
        var readOnly = new Map3dsEditorView(Gen6(out _));
        Layout(readOnly, 1400, 860);
        Pick(readOnly, e => e.Kind == MapEventKind.Npc && e.Index == 0);
        check(readOnly.ViewDirection is null && readOnly.Canvas.ViewOverlay is { Pending: false }, "Map3dsFacingUi_WithoutAProjectTheFacingIsShownButNotEditable");

        var calls7 = new List<double>();
        var mover7 = new Map3dsMover((b, z, n, x, y) => "m", () => Gen7(out _), z => false, z => "r", (b, z, n, f, s) => { calls7.Add(f); return "Girar entrenador"; });
        var g7 = new Map3dsEditorView(Gen7(out _), mover7);
        Layout(g7, 1400, 860);
        g7.ShowZone(0);
        Pick(g7, e => e.Label.StartsWith("Entrenador 0"));
        check(g7.ViewAngle is { Text: "180" } && g7.ViewSight is null && g7.MoveX is null && g7.Canvas.ViewOverlay is { Cells.Count: 0, Facing: 180 },
            "Map3dsFacingUi_Gen7TrainerShowsAFreeAngleWithoutSightOrMove");
        g7.ViewAngle!.Text = "abc";
        check(!g7.PreviewView(g7.Canvas.Selected!) && g7.ViewResult!.Text.Contains("grados"), "Map3dsFacingUi_Gen7RejectsNonNumericAngles");
        g7.ViewAngle.Text = "-90";
        check(g7.PreviewView(g7.Canvas.Selected!) && g7.PendingView is { Facing: 270 } && g7.SaveView() && calls7.SequenceEqual([270.0]), "Map3dsFacingUi_Gen7AnglesAreNormalisedAndSaved");
        Pick(g7, e => e.Label.StartsWith("Entrenador 1"));
        check(g7.ViewAngle is null && g7.Details.Children.OfType<TextBlock>().Any(t => t.Text == Map3dsEditorView.ReadOnlyNotice), "Map3dsFacingUi_Gen7PatrolTrainerIsReadOnly");
    }

    private static void RequireTestFolder(string full)
    {
        if (!full.Contains("Pruebas", StringComparison.OrdinalIgnoreCase)) throw new IOException("Por seguridad, la prueba solo escribe en una copia dentro de una carpeta de pruebas.");
    }

    /// <summary>Real persistence on a Gen6 copy: preview, commit, reopen, only 0x0C/0x0E changed, other zones identical, undo, external changes; then reapplied for the game test.</summary>
    internal static void ProbeGen6(string copyRoot, int zone, int npc, double facing, int? sight)
    {
        string full = Path.GetFullPath(copyRoot);
        RequireTestFolder(full);
        var session = GameProjectSession.Open(full, false);
        var service = new Map3dsNpcMoveService(session);
        string archive = service.ZoneArchive;
        byte[] original = File.ReadAllBytes(archive);
        string originalHash = Convert.ToHexString(SHA256.HashData(original));
        var before = Gen6MapBackend.FromConfig(session.GameConfiguration);
        if (facing < 0)
        {
            // Read-only listing: trainers (sight > 0) whose facing can be written, with the walkable cells ahead in each direction.
            foreach (var z in before.Zones.Where(z => zone < 0 || z.Id == zone))
                foreach (var e in before.ZoneEvents(z.Id).Where(e => e.Kind == MapEventKind.Npc && e.Placed))
                    if (before.View(z.Id, e.Kind, e.Index) is { FacingWritable: true, Sight: > 0 } v)
                    {
                        var layout = before.Layout(z.Id);
                        uint? Cell(int x, int y)
                        {
                            int b = x < 0 || y < 0 ? -1 : layout.BlockAt(x / layout.BlockCells, y / layout.BlockCells);
                            return b < 0 || before.Cells(b) is not { } g ? null : g.Cells[((y % layout.BlockCells) * g.Width) + (x % layout.BlockCells)];
                        }
                        uint? own = Cell((int)e.X, (int)e.Y);
                        string free = string.Join(" ", new[] { 0.0, 90, 180, 270 }.Select(d => Gen6MapBackend.DirectionName(d) + " " + before.SightCells(z.Id, e.Kind, e.Index, d, v.Sight!.Value).Count(c => Cell(c.X, c.Y) == own) + "/" + v.Sight));
                        Console.WriteLine($"{z.Name} · NPC {e.Index} en ({e.X}, {e.Y}) · mira {Gen6MapBackend.DirectionName(v.Facing)} · visión {v.Sight} · script {e.Properties.First(p => p.Name == "Script").Value} · casillas como la suya: {free}");
                    }
            Console.WriteLine("Solo lectura: no se escribió nada.");
            return;
        }
        var viewBefore = before.View(zone, MapEventKind.Npc, npc) ?? throw new InvalidDataException("Ese NPC no tiene orientación legible.");
        var npcView = before.ZoneEvents(zone).First(e => e.Kind == MapEventKind.Npc && e.Index == npc);
        Console.WriteLine($"Zona {zone} ({before.Zones.First(z => z.Id == zone).Name}), NPC {npc} en ({npcView.X}, {npcView.Y}): mira {Gen6MapBackend.DirectionName(viewBefore.Facing)}, visión {viewBefore.Sight} → {Gen6MapBackend.DirectionName(facing)}, visión {sight?.ToString() ?? "sin cambio"}");

        var proposal = service.PreviewView(before, zone, npc, facing, sight);
        bool previewSafe = File.ReadAllBytes(archive).AsSpan().SequenceEqual(original);
        Console.WriteLine((previewSafe ? "PASS" : "FAIL") + " Gen6FacingReal_PreviewWritesNothing");
        service.Apply(proposal);
        var after = Gen6MapBackend.FromConfig(session.GameConfiguration);
        var viewAfter = after.View(zone, MapEventKind.Npc, npc)!;
        bool written = Gen6MapBackend.FacingValue(viewAfter.Facing) == Gen6MapBackend.FacingValue(facing) && viewAfter.Sight == (sight ?? viewBefore.Sight);
        Console.WriteLine((written ? "PASS" : "FAIL") + $" Gen6FacingReal_ReopenedWithTheNewValues ({Gen6MapBackend.DirectionName(viewAfter.Facing)}, visión {viewAfter.Sight})");
        var zb = before.ZoneBytes(zone); var za = after.ZoneBytes(zone);
        var rec = before.Zone(zone).Entities.Records.First(r => r.Kind == MapEventKind.Npc && r.Index == npc);
        var diff = Enumerable.Range(0, zb.Length).Where(i => zb[i] != za[i]).ToArray();
        bool onlyFields = za.Length == zb.Length && diff.Length > 0 && diff.All(i => i >= rec.Offset + 0x0C && i < rec.Offset + 0x10);
        Console.WriteLine((onlyFields ? "PASS" : "FAIL") + $" Gen6FacingReal_OnlyFacingAndSightBytesChanged ({diff.Length} bytes)");
        var membersBefore = new GARC.MemGARC(original).Files;
        var membersAfter = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        bool otherZones = Enumerable.Range(0, membersAfter.Length).All(i => i == zone || membersAfter[i].AsSpan().SequenceEqual(membersBefore[i]));
        Console.WriteLine((otherZones ? "PASS" : "FAIL") + " Gen6FacingReal_OtherZonesByteIdentical");
        var entry = session.History.Read().Entries.Last();
        bool recorded = entry.Parameters.GetValueOrDefault("ActionId") == Map3dsNpcMoveService.ViewActionId;
        Console.WriteLine((recorded ? "PASS" : "FAIL") + $" Gen6FacingReal_HistoryEntry «{entry.Action}»");
        RestorePoints.Undo(session.History, session.ProjectRoot, entry.Id);
        bool restored = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))) == originalHash;
        Console.WriteLine((restored ? "PASS" : "FAIL") + " Gen6FacingReal_UndoRestoresTheOriginalArchive");
        bool stale = false;
        try { service.PreviewView(Gen6MapBackend.FromConfig(session.GameConfiguration), zone, npc, facing, sight); } catch (IOException) { stale = true; }
        Console.WriteLine((stale ? "PASS" : "FAIL") + " Gen6FacingReal_ChangesOutsideTheEditorRequireAReload");
        if (!(previewSafe && written && onlyFields && otherZones && recorded && restored && stale)) throw new InvalidDataException("La prueba real de orientación Gen6 falló.");
        service.Refresh();
        service.Apply(service.PreviewView(Gen6MapBackend.FromConfig(session.GameConfiguration), zone, npc, facing, sight));
        Console.WriteLine("Cambio aplicado de nuevo en la copia para la prueba jugable · gameplay NOT tested");
    }

    /// <summary>Real persistence on an Ultra Sun/Ultra Moon copy: preview, commit, reopen, 8 bytes, other members identical, exact restore, external changes; then reapplied.</summary>
    internal static void ProbeGen7(string copyRoot, int area, int trainerNumber, double facing)
    {
        string full = Path.GetFullPath(copyRoot);
        RequireTestFolder(full);
        var session = GameProjectSession.Open(full, false);
        var service = new Map3dsGen7NpcMoveService(session);
        string archive = service.FieldArchive;
        byte[] original = File.ReadAllBytes(archive);
        var before = Gen7MapBackend.FromConfig(session.GameConfiguration);
        var trainers = before.ZoneEvents(area).Where(e => before.EventRef(area, e.Index)?.Pack == Gen7Events.TrainerPack).ToArray();
        var trainer = trainers[trainerNumber];
        var viewBefore = before.View(area, MapEventKind.Npc, trainer.Index) ?? throw new InvalidDataException("Ese entrenador no tiene orientación legible.");
        Console.WriteLine($"Área {area} ({before.Zones.First(z => z.Id == area).Name}): {trainer.Label} en casilla ({trainer.X}, {trainer.Y}) mira {Gen7MapBackend.TrainerDirection(viewBefore.Facing)} → {Gen7MapBackend.TrainerDirection(facing)}");
        foreach (var p in trainer.Properties) Console.WriteLine($"  {p.Name}: {p.Value}");
        var at = before.EventRef(area, trainer.Index)!.Value.Position;
        foreach (double d in new[] { 0.0, 90, 180, 270 })
        {
            int ground = 0;
            for (int k = 1; k <= 5; k++)
            {
                float x = at.X + (float)(Math.Sin(d * Math.PI / 180) * k * Gen7MapBackend.UnitsPerCell), z = at.Z - (float)(Math.Cos(d * Math.PI / 180) * k * Gen7MapBackend.UnitsPerCell);
                if (before.GroundHeight(area, x, z, at.Y) is float h && Math.Abs(h - at.Y) < 60) ground++;
            }
            Console.WriteLine($"  Suelo transitable a su altura en las 5 casillas hacia {Gen7MapBackend.TrainerDirection(d)}: {ground}/5");
        }
        if (facing < 0) { Console.WriteLine("Solo lectura: no se escribió nada."); return; }

        var proposal = service.PreviewView(before, area, trainer.Index, facing);
        bool previewSafe = File.ReadAllBytes(archive).AsSpan().SequenceEqual(original);
        Console.WriteLine((previewSafe ? "PASS" : "FAIL") + " Gen7FacingReal_PreviewWritesNothing");
        service.Apply(proposal);
        var after = Gen7MapBackend.FromConfig(session.GameConfiguration);
        var viewAfter = after.View(area, MapEventKind.Npc, trainer.Index)!;
        bool written = Math.Abs(viewAfter.Facing - facing) < 0.01;
        Console.WriteLine((written ? "PASS" : "FAIL") + $" Gen7FacingReal_ReopenedWithTheNewDirection ({Gen7MapBackend.TrainerDirection(viewAfter.Facing)})");
        var edBefore = before.EventBytes(area); var edAfter = after.EventBytes(area);
        int changedBytes = Enumerable.Range(0, edBefore.Length).Count(i => edBefore[i] != edAfter[i]);
        bool small = edBefore.Length == edAfter.Length && changedBytes is > 0 and <= 8;
        Console.WriteLine((small ? "PASS" : "FAIL") + $" Gen7FacingReal_OnlyTheRotationChanged ({changedBytes} bytes)");
        var others = before.ZoneEvents(area).Where(e => e.Index != trainer.Index).Select(e => (e.Kind, e.X, e.Y, e.Label)).ToArray();
        bool restSame = others.SequenceEqual(after.ZoneEvents(area).Where(e => e.Index != trainer.Index).Select(e => (e.Kind, e.X, e.Y, e.Label)));
        Console.WriteLine((restSame ? "PASS" : "FAIL") + " Gen7FacingReal_OtherEventsOfTheAreaUnchanged");
        var membersBefore = new GARC.MemGARC(original).Files;
        var membersAfter = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        int member = area * Gen7MapBackend.SlotsPerWorld;
        bool otherMembers = membersAfter.Length == membersBefore.Length && Enumerable.Range(0, membersAfter.Length).All(i => i == member || membersAfter[i].AsSpan().SequenceEqual(membersBefore[i]));
        Console.WriteLine((otherMembers ? "PASS" : "FAIL") + " Gen7FacingReal_OtherFieldMembersByteIdentical");
        var entry = session.History.Read().Entries.Last();
        bool recorded = entry.Parameters.GetValueOrDefault("ActionId") == Map3dsGen7NpcMoveService.ViewActionId;
        Console.WriteLine((recorded ? "PASS" : "FAIL") + $" Gen7FacingReal_HistoryEntry «{entry.Action}»");
        bool canRestore = service.CanRestore(area);
        service.Apply(service.PreviewRestore(area));
        var restoredMembers = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        bool restored = canRestore && Enumerable.Range(0, restoredMembers.Length).All(i => restoredMembers[i].AsSpan().SequenceEqual(membersBefore[i]));
        Console.WriteLine((restored ? "PASS" : "FAIL") + " Gen7FacingReal_RestorePutsBackEveryMemberByteForByte");
        var other = new Map3dsGen7NpcMoveService(session);
        other.Apply(other.PreviewView(Gen7MapBackend.FromConfig(session.GameConfiguration), area, trainer.Index, facing));
        bool stale = false;
        try { service.PreviewView(Gen7MapBackend.FromConfig(session.GameConfiguration), area, trainer.Index, facing); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { stale = true; }
        other.Apply(other.PreviewRestore(area));
        Console.WriteLine((stale ? "PASS" : "FAIL") + " Gen7FacingReal_ChangesOutsideTheEditorRequireAReload");
        if (!(previewSafe && written && small && restSame && otherMembers && recorded && restored && stale)) throw new InvalidDataException("La prueba real de orientación Gen7 falló.");
        service.Refresh();
        service.Apply(service.PreviewView(Gen7MapBackend.FromConfig(session.GameConfiguration), area, trainer.Index, facing));
        Console.WriteLine("Cambio aplicado de nuevo en la copia para la prueba jugable · gameplay NOT tested");
    }

    /// <summary>
    /// Offset in the RomFS of the image where the whole content of <paramref name="file"/> is stored, or -1. The RomFS of
    /// a pk3DS rebuild uses the fixed zero key (as read by GameRomExtractionService); it is decrypted in memory, nothing is written.
    /// </summary>
    internal static long FindInImage(string image, string file)
    {
        byte[] content = File.ReadAllBytes(file);
        using var stream = File.OpenRead(image);
        var top = new byte[0x200]; stream.ReadExactly(top);
        long start = top.AsSpan(0x100, 4).SequenceEqual("NCSD"u8) ? BitConverter.ToUInt32(top, 0x120) * 0x200L : 0;
        var header = new byte[0x200]; stream.Position = start; stream.ReadExactly(header);
        if (!header.AsSpan(0x100, 4).SequenceEqual("NCCH"u8)) return -1;
        bool encrypted = (header[0x18F] & 4) == 0;
        if (encrypted && (header[0x18F] & 1) == 0) throw new InvalidDataException("La imagen no usa la clave fija de las reconstrucciones.");
        ulong program = BitConverter.ToUInt64(header, 0x118);
        long romfs = start + (BitConverter.ToUInt32(header, 0x1B0) * 0x200L), length = BitConverter.ToUInt32(header, 0x1B4) * 0x200L;
        const int chunk = 0x400000, head = 64;
        Func<long, pk3DS.Core.CTR.AesCtr?> cipherAt = at => encrypted ? new pk3DS.Core.CTR.AesCtr(new byte[16], program, (3UL << 56) + (ulong)(at / 16)) : null;
        var buffer = new byte[chunk]; var plain = new byte[chunk + head];
        var candidates = new List<long>();
        var cipher = cipherAt(0);
        int carry = 0;
        for (long done = 0; done < length;)
        {
            int count = (int)Math.Min(chunk, length - done);
            stream.Position = romfs + done; stream.ReadExactly(buffer.AsSpan(0, count));
            if (cipher is null) Buffer.BlockCopy(buffer, 0, plain, carry, count); else cipher.TransformBlock(buffer, 0, count, plain, carry);
            int total = carry + count;
            for (int k = 0; k + head <= total; k += 16)
                if (plain[k] == content[0] && plain.AsSpan(k, head).SequenceEqual(content.AsSpan(0, head))) candidates.Add(done - carry + k);
            carry = Math.Min(head, total) / 16 * 16;
            Buffer.BlockCopy(plain, total - carry, plain, 0, carry);
            done += count;
        }
        foreach (long at in candidates.Distinct())
        {
            if (at + content.Length > length) continue;
            var c = cipherAt(at);
            bool same = true;
            for (long k = 0; k < content.Length && same; k += chunk)
            {
                int count = (int)Math.Min(chunk, content.Length - k);
                int padded = (count + 15) / 16 * 16;
                stream.Position = romfs + at + k; stream.ReadExactly(buffer.AsSpan(0, padded));
                if (c is null) Buffer.BlockCopy(buffer, 0, plain, 0, padded); else c.TransformBlock(buffer, 0, padded, plain, 0);
                same = plain.AsSpan(0, count).SequenceEqual(content.AsSpan((int)k, count));
            }
            if (same) return at;
        }
        return -1;
    }

    /// <summary>Rebuilds the copy into a 3DS image, re-extracts it and checks the trainer keeps its new facing (and sight in Gen6).</summary>
    internal static void Rebuild(string copyRoot, string image, string reextract, int zone, int index, double facing, int? sight)
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
            // Without room for a full re-extraction: the edited field archive must be inside the image byte for byte.
            var cfg = project.GameConfiguration;
            string garc = Path.Combine(cfg.RomFS, cfg.GetGARCFileName("encdata"));
            long at = FindInImage(image, garc);
            Console.WriteLine((at >= 0 ? "PASS" : "FAIL") + $" TrainerFacingRebuild_ImageContainsTheEditedFieldArchive (desplazamiento 0x{at:X} en el RomFS); reextracción completa pendiente por espacio en disco; gameplay NOT tested");
            if (at < 0) throw new InvalidDataException("La imagen no contiene el archivo de campo editado.");
            return;
        }
        RealRebuildChecks.VerifyExisting(copyRoot, image, reextract);
        var config = GameProjectSession.Open(reextract, false).GameConfiguration;
        IMapTrainerViews views = config.USUM ? Gen7MapBackend.FromConfig(config) : Gen6MapBackend.FromConfig(config);
        int eventIndex = index;
        if (views is Gen7MapBackend g7) eventIndex = g7.ZoneEvents(zone).Where(e => g7.EventRef(zone, e.Index)?.Pack == Gen7Events.TrainerPack).ElementAt(index).Index;
        var view = views.View(zone, MapEventKind.Npc, eventIndex);
        bool ok = view is not null && Math.Abs((((view.Facing - facing) % 360) + 540) % 360 - 180) < 0.01 && (sight is null || view.Sight == sight);
        Console.WriteLine((ok ? "PASS" : "FAIL") + $" TrainerFacingRebuild_ReextractedImageKeepsTheChange ({view?.Facing}°, visión {view?.Sight?.ToString() ?? "—"}); gameplay NOT tested");
        if (!ok) throw new InvalidDataException("La imagen reconstruida no conserva la orientación.");
    }
}
