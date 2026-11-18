using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThreeDS.Progressive;
using pk3DS.Core;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;

/// <summary>Gen6 (X/Y, Omega Ruby/Alpha Sapphire) map and event readers.</summary>
internal static class Map3dsGen6Checks
{
    internal static byte[] Pack(string magic, params byte[][] parts)
    {
        int head = 4 + ((parts.Length + 1) * 4);
        var offsets = new List<int>();
        int at = head;
        foreach (var p in parts) { offsets.Add(at); at += (p.Length + 3) & ~3; }
        offsets.Add(at);
        var data = new byte[at];
        data[0] = (byte)magic[0]; data[1] = (byte)magic[1];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)parts.Length);
        for (int i = 0; i < offsets.Count; i++) BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4 + (i * 4)), offsets[i]);
        for (int i = 0; i < parts.Length; i++) parts[i].CopyTo(data, offsets[i]);
        return data;
    }

    internal sealed record SyntheticNpc(int X, int Y, int Model = 7, int Face = 1, int Sight = 3);

    internal static byte[] Zone(int matrix, int parent, SyntheticNpc[] npcs, (int X, int Y, int W, int H)[]? props = null,
        (int RawX, int RawY, int Dest, int Type)[]? warps = null, (int X, int Y, int W, int H)[]? triggers = null, int? badLength = null)
    {
        props ??= []; warps ??= []; triggers ??= [];
        var head = new byte[Gen6Zone.HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(4), (ushort)matrix);
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(0x1C), (ushort)(parent | (5 << 10)));
        int body = (props.Length * 0x14) + (npcs.Length * 0x30) + (warps.Length * 0x18) + (triggers.Length * 0x18);
        var script = new byte[] { 8, 0, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD };
        var e = new byte[Gen6ZoneEntities.HeaderSize + body + script.Length];
        BinaryPrimitives.WriteInt32LittleEndian(e, badLength ?? (8 + body));
        e[4] = (byte)props.Length; e[5] = (byte)npcs.Length; e[6] = (byte)warps.Length; e[7] = (byte)triggers.Length;
        int o = Gen6ZoneEntities.HeaderSize;
        foreach (var p in props)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o), 40);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o + 8), (ushort)p.X); BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o + 10), (ushort)p.Y);
            BinaryPrimitives.WriteInt16LittleEndian(e.AsSpan(o + 12), (short)p.W); BinaryPrimitives.WriteInt16LittleEndian(e.AsSpan(o + 14), (short)p.H);
            o += 0x14;
        }
        foreach (var n in npcs)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o + 2), (ushort)n.Model);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o + 0x0C), (ushort)n.Face);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o + 0x0E), (ushort)n.Sight);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o + 0x28), (ushort)n.X); BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o + 0x2A), (ushort)n.Y);
            BinaryPrimitives.WriteSingleLittleEndian(e.AsSpan(o + 0x2C), 18f);
            o += 0x30;
        }
        foreach (var w in warps)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o), (ushort)w.Dest);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o + 6), (ushort)w.Type);
            BinaryPrimitives.WriteInt16LittleEndian(e.AsSpan(o + 8), (short)w.RawX); BinaryPrimitives.WriteInt16LittleEndian(e.AsSpan(o + 12), (short)w.RawY);
            BinaryPrimitives.WriteInt16LittleEndian(e.AsSpan(o + 14), 3); BinaryPrimitives.WriteInt16LittleEndian(e.AsSpan(o + 16), 1);
            o += 0x18;
        }
        foreach (var t in triggers)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o), 500);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o + 12), (ushort)t.X); BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(o + 14), (ushort)t.Y);
            BinaryPrimitives.WriteInt16LittleEndian(e.AsSpan(o + 16), (short)t.W); BinaryPrimitives.WriteInt16LittleEndian(e.AsSpan(o + 18), (short)t.H);
            o += 0x18;
        }
        script.CopyTo(e, o);
        return Pack("ZO", head, e, new byte[4], new byte[16]);
    }

    internal static byte[] Matrix(int width, int height, params int[] blocks)
    {
        var head = new byte[8 + (width * height * 2) + 34];
        head[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(4), (ushort)width); BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(6), (ushort)height);
        for (int i = 0; i < width * height; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(8 + (i * 2)), (ushort)(i < blocks.Length && blocks[i] >= 0 ? blocks[i] : 0xFFFF));
        return Pack("MM", head, new byte[324]);
    }

    internal static byte[] Block(uint fill, int odd = -1)
    {
        var grid = new byte[4 + (40 * 40 * 4)];
        BinaryPrimitives.WriteUInt16LittleEndian(grid, 40); BinaryPrimitives.WriteUInt16LittleEndian(grid.AsSpan(2), 40);
        for (int i = 0; i < 1600; i++) BinaryPrimitives.WriteUInt32LittleEndian(grid.AsSpan(4 + (i * 4)), i == odd ? 0x01000021u : fill);
        return Pack("GR", grid, "BCH\0"u8.ToArray(), "coll"u8.ToArray(), new byte[128], new byte[128], new byte[128]);
    }

    internal static Gen6MapBackend Synthetic(out byte[][] zones)
    {
        zones =
        [
            Zone(0, 3, [new(45, 12), new(5, 70, 9, 0, 0)], props: [(44, 10, 2, 1)], warps: [(46 * 18 + 9, 20 * 18 + 9, 1, 0), (100, 200, 1, 1)],
                triggers: [(41, 30, 3, 1)]),
            Zone(1, 4, [new(500, 500), new(600, 3), new(2, 2)]),
            Zone(0, 3, [new(1, 1)], badLength: 999),
            [0x5A, 0x4F, 0, 0],
            Zone(9, 4, []),
        ];
        var z = zones;
        var matrices = new[] { Matrix(2, 2, 0, 1, -1, 1), Matrix(1, 1, 1) };
        var blocks = new[] { Block(0x10u, odd: 0), Block(0x20u), [1, 2, 3, 4] };
        return new Gen6MapBackend(Gen6Title.ORAS, z.Length, i => z[i], matrices.Length, i => matrices[i], blocks.Length, i => blocks[i],
            ["", "", "", "Pueblo Prueba", "Ruta Prueba"]);
    }

    internal static void Run(Action<bool, string> check)
    {
        var backend = Synthetic(out var zones);
        check(backend.Zones.Count == 2 && backend.Zones[0].Name == "000 · Pueblo Prueba" && backend.Zones[1].Id == 1, "Gen6Map_ListsReadableZonesWithLocationNames");
        check(backend.Skipped.Count == 3 && backend.Skipped.Any(s => s.Zone == 2 && s.Reason.Contains("longitud")) && backend.Skipped.Any(s => s.Zone == 4 && s.Reason.Contains("matriz")),
            "Gen6Map_SkipsZonesWithInconsistentLengthsOrMissingMatrices");
        check(backend.Family == "gen6-oras" && backend.Capabilities.Editing && backend.Mutations is not null
            && !backend.Validate(0, new MapMutation(MapEventKind.Npc, 0, "Modelo", 3)).Allowed, "Gen6Map_OnlyNpcPositionIsWritable");

        var layout = backend.Resources.Layout(0);
        check(layout.Width == 2 && layout.Height == 2 && layout.BlockCells == 40 && layout.BlockAt(0, 0) == 0 && layout.BlockAt(0, 1) == -1 && layout.BlockAt(1, 1) == 1 && layout.BlockAt(5, 0) == -1,
            "Gen6Map_MatrixGivesBlocksRowMajorWithEmptyCells");
        var cells = backend.Resources.Cells(0);
        check(cells is { Width: 40, Height: 40 } && cells.Cells[0] == 0x01000021u && cells.Cells[1] == 0x10u, "Gen6Map_BlockPermissionGridIs40x40");
        check(backend.Resources.Cells(2) is null && backend.Resources.Cells(-1) is null && backend.Resources.Cells(77) is null, "Gen6Map_UnreadableOrMissingBlocksHaveNoCells");

        var events = backend.Events.ZoneEvents(0);
        check(events.Count(e => e.Kind == MapEventKind.Npc) == 2 && events.Count(e => e.Kind == MapEventKind.Prop) == 1
            && events.Count(e => e.Kind == MapEventKind.Warp) == 2 && events.Count(e => e.Kind == MapEventKind.Trigger) == 1, "Gen6Map_ReadsEveryEntityKind");
        var npc = events.First(e => e.Kind == MapEventKind.Npc);
        check(npc.X == 45 && npc.Y == 12 && npc.Facing is null && npc.Properties.Any(p => p.Name == "Modelo" && p.Value == "7")
            && npc.Properties.Any(p => p.Name == "Alcance de visión" && p.Value == "3") && npc.Properties.Any(p => p.Name == "Ángulo" && p.Value == "18°"),
            "Gen6Map_NpcCellModelSightAndAngle");
        check(npc.Properties.First(p => p.Name.StartsWith("Orientación")).Note?.Contains("no está verificada") == true,
            "Gen6Map_NpcFacingIsShownRawUntilVerified");
        var warp = events.First(e => e.Kind == MapEventKind.Warp);
        check(warp.X == 46 && warp.Y == 20 && warp.Width == 3 && warp.Properties.First(p => p.Name == "Tipo de coordenadas").Note is null, "Gen6Map_WarpCentreConvertsToItsCell");
        var loose = events.Where(e => e.Kind == MapEventKind.Warp).ElementAt(1);
        check(loose.Properties.First(p => p.Name == "Tipo de coordenadas").Note?.Contains("aproximada") == true, "Gen6Map_OffGridWarpsAreFlaggedApproximate");
        var prop = events.First(e => e.Kind == MapEventKind.Prop);
        var trigger = events.First(e => e.Kind == MapEventKind.Trigger);
        check(prop.X == 44 && prop.Width == 2 && trigger.X == 41 && trigger.Y == 30 && trigger.Width == 3, "Gen6Map_PropAndTriggerAreas");
        check(events.Where(e => e.Kind != MapEventKind.Npc).All(e => e.Properties.All(p => !p.Writable))
            && events.Where(e => e.Kind == MapEventKind.Npc).All(e => e.Properties.Count(p => p.Writable) == 1 && e.Properties.Single(p => p.Writable).Name == "Casilla"),
            "Gen6Map_OnlyTheNpcCellIsMarkedWritable");

        // Writing an NPC position touches exactly the two u16 fields.
        var raw = backend.ZoneBytes(0);
        var moved = Gen6ZoneEdit.MoveNpc(raw, 1, 33, 21);
        var diff = Enumerable.Range(0, raw.Length).Where(i => raw[i] != moved[i]).ToArray();
        var after = new Gen6Zone(moved).Entities;
        var npc1 = after.Records.First(r => r.Kind == MapEventKind.Npc && r.Index == 1);
        check(diff.Length > 0 && diff.All(i => i >= npc1.Offset + 0x28 && i < npc1.Offset + 0x2C) && after.U16(npc1, 0x28) == 33 && after.U16(npc1, 0x2A) == 21
            && moved.Length == raw.Length, "Gen6Edit_MoveNpcWritesOnlyItsPosition");
        bool missing = false, range = false;
        try { Gen6ZoneEdit.MoveNpc(raw, 9, 1, 1); } catch (ArgumentOutOfRangeException) { missing = true; }
        try { Gen6ZoneEdit.MoveNpc(raw, 0, 70000, 1); } catch (ArgumentOutOfRangeException) { range = true; }
        check(missing && range, "Gen6Edit_RejectsMissingNpcsAndOutOfRangeCells");
        check(backend.ValidateMove(0, MapEventKind.Npc, 0, 46, 12).Allowed, "Gen6Move_AllowsAFreeCellWithABlock");
        check(!backend.ValidateMove(0, MapEventKind.Npc, 0, 90, 12).Allowed && !backend.ValidateMove(0, MapEventKind.Npc, 0, 10, 50).Reason.Contains("fuera")
            && backend.ValidateMove(0, MapEventKind.Npc, 0, 10, 50).Reason.Contains("sin bloque"), "Gen6Move_RejectsOutsideTheMatrixAndEmptyCells");
        check(backend.ValidateMove(0, MapEventKind.Npc, 1, 45, 12).Reason.Contains("ocupa") && backend.ValidateMove(0, MapEventKind.Npc, 0, 45, 12).Reason.Contains("ya está"),
            "Gen6Move_RejectsOccupiedAndUnchangedCells");
        check(!backend.ValidateMove(0, MapEventKind.Warp, 0, 46, 12).Allowed && !backend.ValidateMove(1, MapEventKind.Npc, 0, 2, 2).Allowed,
            "Gen6Move_RejectsOtherKindsAndUnverifiedZones");

        check(events.All(e => e.Placed), "Gen6Map_EventsInsideTheMatrixArePlaced");
        var untrusted = backend.Events.ZoneEvents(1);
        check(untrusted.Count == 3 && untrusted.All(e => !e.Placed && e.PositionNote!.Contains("no son casillas")),
            "Gen6Map_ZoneWithMostPositionsOffTheMatrixIsNotDrawn");

        var zone = backend.Zone(0);
        int scriptAt = zone.Entities.Records[^1].Offset + zone.Entities.Records[^1].Size;
        check(zone.Pack.Raw.AsSpan(scriptAt + 4, 4).SequenceEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }) && ReferenceEquals(zone.Pack.Raw, zones[0]),
            "Gen6Map_ScriptAfterEntitiesIsUntouched");
        bool threw = false;
        try { backend.Zone(2); } catch (ArgumentOutOfRangeException) { threw = true; }
        check(threw, "Gen6Map_UnavailableZoneThrows");
    }

    /// <summary>Opens an extracted 3DS game folder without a project session or history.</summary>
    internal static GameConfig OpenConfig(string root)
    {
        string romfs = Directory.GetDirectories(root).First(x => Path.GetFileName(x).Contains("rom", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(x, "a")));
        string exefs = Directory.GetDirectories(root).First(x => Path.GetFileName(x).Contains("exe", StringComparison.OrdinalIgnoreCase));
        int count = Directory.EnumerateFiles(Path.Combine(romfs, "a"), "*", SearchOption.AllDirectories).Count(f => Path.GetFileName(f).Length == 1);
        var config = new GameConfig(count);
        if (config.Version == GameVersion.Invalid) throw new InvalidDataException($"RomFS no reconocido ({count} GARC).");
        config.Initialize(romfs, exefs, 2);
        return config;
    }

    /// <summary>Read-only pass over every zone of a real copy.</summary>
    internal static void Probe(string output, string root)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Usa una carpeta nueva de salida.");
        Directory.CreateDirectory(output);
        var hashes = SnapshotHashes(root);
        var config = OpenConfig(root);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var backend = Gen6MapBackend.FromConfig(config);
        var log = new List<string> { $"{backend.Title} ({config.Version}) · zonas legibles {backend.Zones.Count} · omitidas {backend.Skipped.Count} · {sw.ElapsedMilliseconds} ms" };
        log.AddRange(backend.Skipped.Select(s => $"  omitida {s.Zone}: {s.Reason}"));
        var kinds = new Dictionary<MapEventKind, int>();
        int outside = 0, onEmpty = 0, onBlock = 0, approx = 0, unplaced = 0, untrustedZones = 0;
        var blocksSeen = new HashSet<int>(); int unreadable = 0;
        foreach (var z in backend.Zones)
        {
            var layout = backend.Resources.Layout(z.Id);
            foreach (int b in layout.Blocks.Where(b => b >= 0).Distinct())
                if (blocksSeen.Add(b) && backend.Resources.Cells(b) is null) unreadable++;
            var zoneEvents = backend.Events.ZoneEvents(z.Id);
            if (zoneEvents.Any(e => e.PositionNote?.Contains("no son casillas") == true)) untrustedZones++;
            foreach (var e in zoneEvents)
            {
                if (!e.Placed) unplaced++;
                kinds[e.Kind] = kinds.GetValueOrDefault(e.Kind) + 1;
                if (e.Properties.Any(p => p.Note?.Contains("aproximada") == true)) approx++;
                int col = (int)Math.Floor(e.X / 40), row = (int)Math.Floor(e.Y / 40);
                if (col < 0 || row < 0 || col >= layout.Width || row >= layout.Height) outside++;
                else if (layout.BlockAt(col, row) < 0) onEmpty++;
                else onBlock++;
            }
        }
        log.Add("Eventos: " + string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
        log.Add($"Sobre un bloque {onBlock} · sobre celda vacía {onEmpty} · fuera de la matriz {outside} · warps con posición aproximada {approx}");
        log.Add($"Sin dibujar {unplaced} · zonas con posiciones no verificadas {untrustedZones}");
        log.Add($"Bloques GR distintos usados {blocksSeen.Count} · ilegibles {unreadable} · total {sw.ElapsedMilliseconds} ms");
        File.WriteAllLines(Path.Combine(output, "gen6-map-probe.txt"), log);
        foreach (var l in log) Console.WriteLine(l);
        bool same = SnapshotHashes(root).SequenceEqual(hashes);
        Console.WriteLine((same ? "PASS" : "FAIL") + " Gen6MapReal_SourceUnchanged: map GARC hashes identical after reading");
        if (!same) throw new InvalidDataException("La copia de origen cambió durante la lectura.");
        if (backend.Zones.Count == 0 || unreadable > 0) throw new InvalidDataException("Lectura real incompleta.");
        Console.WriteLine("PASS Gen6MapReal_AllListedZonesAndBlocksRead; gameplay NOT tested");
    }

    private static void Layout(FrameworkElement content, double w, double h)
    {
        content.Measure(new System.Windows.Size(w, h)); content.Arrange(new Rect(0, 0, w, h)); content.UpdateLayout(); UiPump.Idle(); content.UpdateLayout(); UiPump.Idle();
    }

    private static void SavePng(FrameworkElement content, int w, int h, string path)
    {
        Layout(content, w, h);
        var image = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); image.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); png.Save(file);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    /// <summary>Off-screen checks of the 3DS map editor view with the synthetic adapter.</summary>
    internal static void RunUi(Action<bool, string> check)
    {
        var backend = Synthetic(out _);
        var view = new Map3dsEditorView(backend);
        Layout(view, 1400, 860);
        check(view.ZoneList.Items.Count == 2 && view.CurrentZone == 0, "Map3dsUi_ListsZonesAndOpensTheFirst");
        check(view.Canvas.RenderedMarkers == 6 && view.EventList.Items.Count == 6, "Map3dsUi_DrawsEveryPlacedEvent");
        var npc = backend.Events.ZoneEvents(0).First(e => e.Kind == MapEventKind.Npc);
        var centre = view.Canvas.ToScreen(new System.Windows.Point(npc.X + 0.5, npc.Y + 0.5));
        var hit = view.Canvas.HitTest(centre);
        check(hit is not null && hit.Kind == MapEventKind.Npc && hit.Index == 0, "Map3dsUi_HitTestFindsTheNpcUnderTheCursor");
        var trigger = backend.Events.ZoneEvents(0).First(e => e.Kind == MapEventKind.Trigger);
        var onTrigger = view.Canvas.HitTest(view.Canvas.ToScreen(new System.Windows.Point(trigger.X + 1.5, trigger.Y + 0.5)));
        check(onTrigger?.Kind == MapEventKind.Trigger, "Map3dsUi_HitTestFindsAreas");
        view.EventList.SelectedIndex = view.EventList.Items.Cast<ListBoxItem>().ToList().FindIndex(i => i.Tag is MapEventView { Kind: MapEventKind.Npc, Index: 0 });
        UiPump.Idle();
        var texts = Descendants<TextBlock>(view.Details).Select(t => t.Text).ToArray();
        check(view.Canvas.Selected?.Kind == MapEventKind.Npc && texts.Contains("Modelo") && texts.Contains("7") && texts.Contains(Map3dsEditorView.ReadOnlyNotice),
            "Map3dsUi_SelectingFromTheListShowsPropertiesAndReadOnlyNotice");
        check(!Descendants<System.Windows.Controls.Primitives.TextBoxBase>(view.Details).Any(), "Map3dsUi_NoEditableControlsWhileReadOnly");
        var layers = Descendants<CheckBox>(view).ToArray();
        layers.First(c => (string)c.Content == "NPC").IsChecked = false; UiPump.Idle();
        Layout(view, 1400, 860);
        check(view.Canvas.RenderedMarkers == 4 && view.Canvas.HitTest(centre)?.Kind != MapEventKind.Npc, "Map3dsUi_HidingALayerRemovesItsMarkersAndHits");
        layers.First(c => (string)c.Content == "NPC").IsChecked = true; UiPump.Idle();
        view.ShowZone(1); Layout(view, 1400, 860);
        check(view.Canvas.RenderedMarkers == 0 && view.EventList.Items.Cast<ListBoxItem>().All(i => ((string)i.Content).StartsWith("⚠")) && view.Status.Text.Contains("3 sin dibujar"),
            "Map3dsUi_UnplacedEventsAreListedWithAWarningButNotDrawn");
        double before = view.Canvas.Scale; view.Canvas.ZoomCentered(2);
        check(Math.Abs(view.Canvas.Scale - (before * 2)) < 1e-6, "Map3dsUi_ZoomChangesScale");
        // Editing panel with a test mover: nothing is written until "Guardar".
        var calls = new List<(int Zone, int Npc, int X, int Y)>();
        int reloads = 0;
        var mover = new Map3dsMover((b, z, n, x, y) => { calls.Add((z, n, x, y)); return $"Mover NPC {n}"; }, () => { reloads++; return Synthetic(out _); });
        var edit = new Map3dsEditorView(Synthetic(out _), mover);
        Layout(edit, 1400, 860);
        var first = edit.Backend.Events.ZoneEvents(0).First(e => e.Kind == MapEventKind.Npc);
        edit.EventList.SelectedIndex = edit.EventList.Items.Cast<ListBoxItem>().ToList().FindIndex(i => i.Tag is MapEventView { Kind: MapEventKind.Npc, Index: 0 });
        UiPump.Idle();
        check(edit.MoveX is { Text: "45" } && edit.MoveY is { Text: "12" } && edit.MoveSave is { IsEnabled: false }, "Map3dsMoveUi_NpcShowsTheMovePanelWithItsCell");
        edit.MoveX!.Text = "90";
        check(!edit.PreviewMove(first) && edit.MoveSave!.IsEnabled == false && edit.PendingMove is null && edit.MoveResult!.Text.Contains("fuera") && calls.Count == 0,
            "Map3dsMoveUi_InvalidCellsCannotBeSaved");
        edit.MoveX!.Text = "46";
        check(edit.PreviewMove(first) && edit.MoveSave!.IsEnabled && edit.PendingMove is { X: 46, Y: 12 } && calls.Count == 0, "Map3dsMoveUi_PreviewShowsTheTargetWithoutWriting");
        check(edit.SaveMove() && calls.SequenceEqual([(0, 0, 46, 12)]) && reloads == 1 && edit.PendingMove is null && edit.Status.Text.Contains("historial"),
            "Map3dsMoveUi_SaveCommitsThroughTheProjectAndReloads");
        int restores = 0;
        var restoring = new Map3dsMover((b, z, n, x, y) => "m", () => Synthetic(out _), z => true, z => { restores++; return "Restaurado"; });
        var rv = new Map3dsEditorView(Synthetic(out _), restoring);
        Layout(rv, 1400, 860);
        rv.EventList.SelectedIndex = rv.EventList.Items.Cast<ListBoxItem>().ToList().FindIndex(i => i.Tag is MapEventView { Kind: MapEventKind.Npc, Index: 0 });
        UiPump.Idle();
        check(rv.MoveRestore is not null && edit.MoveRestore is null && rv.RestoreArea() && restores == 1 && rv.Status.Text.Contains("historial"), "Map3dsMoveUi_RestoreButtonOnlyWhenAStateIsSaved");
        var readOnlyNpc = new Map3dsEditorView(Synthetic(out _));
        Layout(readOnlyNpc, 1400, 860);
        readOnlyNpc.EventList.SelectedIndex = readOnlyNpc.EventList.Items.Cast<ListBoxItem>().ToList().FindIndex(i => i.Tag is MapEventView { Kind: MapEventKind.Npc });
        UiPump.Idle();
        check(readOnlyNpc.MoveX is null, "Map3dsMoveUi_NoMovePanelWithoutAProject");
        check(Map3dsCanvas.CellColor(0x01000021u) == 0xFF0E1218u && Map3dsCanvas.CellColor(5) == Map3dsCanvas.CellColor(5) && Map3dsCanvas.CellColor(5) != Map3dsCanvas.CellColor(6),
            "Map3dsUi_PermissionColoursAreStable");
    }

    /// <summary>Opens the editor on a real copy, off-screen, and saves a capture of one zone.</summary>
    internal static void ProbeUi(string output, string root, int zone, bool graphics = false)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Usa una carpeta nueva de salida.");
        Directory.CreateDirectory(output);
        var hashes = SnapshotHashes(root);
        var backend = Gen6MapBackend.FromConfig(OpenConfig(root));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var view = new Map3dsEditorView(backend);
        view.ShowZone(zone);
        if (graphics)
        {
            view.GraphicsToggle!.IsChecked = true;
            var until = DateTime.UtcNow.AddSeconds(60);
            do { Layout(view, 1600, 1000); UiPump.Idle(); Thread.Sleep(25); } while ((view.Canvas.PendingGraphics > 0 || view.Canvas.GraphicsDrawn == 0) && DateTime.UtcNow < until);
            Console.WriteLine($"Capa gráfica: {view.Canvas.GraphicsDrawn} bloques dibujados, pendientes {view.Canvas.PendingGraphics}");
        }
        string png = Path.Combine(output, $"gen6-zone-{zone:000}{(graphics ? "-graficos" : "")}.png");
        SavePng(view, 1600, 1000, png);
        Console.WriteLine($"Zona {zone}: {view.Canvas.RenderedMarkers} marcadores dibujados, {view.EventList.Items.Count} eventos, {sw.ElapsedMilliseconds} ms · {png}");
        bool same = SnapshotHashes(root).SequenceEqual(hashes);
        Console.WriteLine((same ? "PASS" : "FAIL") + " Map3dsUiReal_SourceUnchanged; gameplay NOT tested");
        if (!same) throw new InvalidDataException("La copia de origen cambió.");
    }

    /// <summary>Read-only: renders every block of a zone from its models and textures, and parses every model and area texture of the copy.</summary>
    internal static void RenderProbe(string output, string root, int zone, int pixels)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Usa una carpeta nueva de salida.");
        Directory.CreateDirectory(output);
        var hashes = SnapshotHashes(root);
        var config = OpenConfig(root);
        var backend = Gen6MapBackend.FromConfig(config);
        var layout = backend.Resources.Layout(zone);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int minC = int.MaxValue, minR = int.MaxValue, maxC = -1, maxR = -1;
        for (int r = 0; r < layout.Height; r++) for (int c = 0; c < layout.Width; c++)
            if (layout.BlockAt(c, r) >= 0) { minC = Math.Min(minC, c); minR = Math.Min(minR, r); maxC = Math.Max(maxC, c); maxR = Math.Max(maxR, r); }
        int w = (maxC - minC + 1) * pixels, h = (maxR - minR + 1) * pixels, triangles = 0, untextured = 0, drawn = 0, failed = 0;
        var canvas = new byte[w * h * 4];
        for (int r = minR; r <= maxR; r++) for (int c = minC; c <= maxC; c++)
        {
            int block = layout.BlockAt(c, r);
            if (block < 0) continue;
            var img = backend.Resources.Graphic(zone, block, pixels);
            if (img is null) { failed++; continue; }
            drawn++; triangles += img.Triangles; untextured += img.Untextured;
            for (int y = 0; y < pixels; y++)
                Buffer.BlockCopy(img.Pixels, y * pixels * 4, canvas, ((((r - minR) * pixels) + y) * w + ((c - minC) * pixels)) * 4, pixels * 4);
        }
        long zoneMs = sw.ElapsedMilliseconds;
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, canvas, w * 4);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
        string file = Path.Combine(output, $"gen6-render-zone-{zone:000}.png");
        using (var f = File.Create(file)) png.Save(f);
        var zoneArea = backend.Zone(zone).Area;
        Console.WriteLine($"Zona {zone} (área {zoneArea}): {drawn} bloques dibujados, {failed} sin modelo legible, {triangles} triángulos, {untextured} sin textura, {zoneMs} ms · {file}");
        sw.Restart();
        int models = 0, modelErrors = 0, areas = 0, areaErrors = 0, textures = 0, textureErrors = 0;
        var usedBlocks = backend.Zones.SelectMany(z => backend.Resources.Layout(z.Id).Blocks).Where(b => b >= 0).Distinct().ToArray();
        foreach (int b in usedBlocks)
        {
            var gr = backend.Block(b);
            if (gr is null) continue;
            try { _ = new pk3DS.Core.Maps.Ctr.H3dFile(gr.Model).Models.Count; models++; }
            catch (MapFormatException ex) { modelErrors++; if (modelErrors <= 5) Console.WriteLine($"  modelo {b}: {ex.Message}"); }
        }
        foreach (int a in backend.Zones.Select(z => backend.Zone(z.Id).Area).Distinct())
        {
            var set = backend.AreaTextures(a);
            if (set is null) { areaErrors++; if (areaErrors <= 5) Console.WriteLine($"  área {a}: {backend.LastGraphicError}"); continue; }
            areas++;
            foreach (var t in set.Values)
            {
                try { _ = t.Decode(); textures++; }
                catch (MapFormatException) { textureErrors++; }
            }
        }
        Console.WriteLine($"Modelos GR usados leídos {models} · con error {modelErrors} · áreas {areas} · con error {areaErrors} · texturas decodificadas {textures} · con error {textureErrors} · {sw.ElapsedMilliseconds} ms");
        bool same = SnapshotHashes(root).SequenceEqual(hashes);
        Console.WriteLine((same ? "PASS" : "FAIL") + " Gen6RenderReal_SourceUnchanged; gameplay NOT tested");
        if (!same) throw new InvalidDataException("La copia de origen cambió.");
    }

    private static string[] SnapshotHashes(string root)
    {
        var romfs = Directory.GetDirectories(root).First(x => Path.GetFileName(x).Contains("rom", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(x, "a")));
        return new[] { "a/0/1/2", "a/0/1/3", "a/0/1/4", "a/0/4/1", "a/0/4/2", "a/0/3/9", "a/0/4/0" }
            .Select(p => Path.Combine(romfs, p)).Where(File.Exists)
            .Select(p => p + "=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    }
}
