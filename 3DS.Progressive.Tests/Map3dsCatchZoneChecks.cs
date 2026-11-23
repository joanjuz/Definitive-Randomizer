using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;

/// <summary>Catch zones in X/Y: plain-ground cells turned into encounter cells, with the zone's encounter table copied when it has none.</summary>
internal static class Map3dsCatchZoneChecks
{
    private static byte[] Table(byte marker)
    {
        var t = new byte[Gen6CatchZones.TableSize];
        t[0] = 1; t[0x10] = marker; t[0x11] = 2; t[0x12] = 3; t[0x13] = 4;
        return t;
    }

    /// <summary>ZO pack with the parts of the shared builder and part 3 replaced by <paramref name="table"/> (empty when null).</summary>
    private static byte[] Zone(int matrix, Map3dsGen6Checks.SyntheticNpc[] npcs, byte[]? table)
    {
        var pack = Gen6Pack.Read(Map3dsGen6Checks.Zone(matrix, 3, npcs), "ZO");
        return Map3dsGen6Checks.Pack("ZO", pack.Part(0).ToArray(), pack.Part(1).ToArray(), pack.Part(2).ToArray(), table ?? []);
    }

    /// <summary>MM pack; with <paramref name="zoneMap"/> it has the exterior layout (blocks, 4×4 zones per block, one more u16 per block).</summary>
    private static byte[] Matrix(int width, int height, int[] blocks, int[]? zoneMap)
    {
        int area = width * height;
        var head = new byte[zoneMap is null ? 8 + (area * 2) + 2 : 8 + (area * 36)];
        head[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(4), (ushort)width); BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(6), (ushort)height);
        for (int i = 0; i < area; i++) BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(8 + (i * 2)), (ushort)(blocks[i] < 0 ? 0xFFFF : blocks[i]));
        if (zoneMap is not null)
            for (int i = 0; i < zoneMap.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(8 + (area * 2) + (i * 2)), (ushort)(zoneMap[i] < 0 ? 0xFFFF : zoneMap[i]));
        return Map3dsGen6Checks.Pack("MM", head, new byte[324]);
    }

    private static byte[] Block(params (int X, int Y, uint Value)[] cells)
    {
        var block = Map3dsGen6Checks.Block(Gen6CatchZones.Ground);
        int grid = Gen6Pack.Read(block, "GR").Start(0);
        foreach (var (x, y, v) in cells) BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(grid + 4 + (((y * 40) + x) * 4)), v);
        return block;
    }

    /// <summary>
    /// Matrix 0 (2×1, exterior): block 0 left, block 1 right; the zone map gives zone 0 to the left block except
    /// cells 30–39 × 0–9, and zone 1 to the rest. Matrix 1 (interior, block 2) is only used by zone 2. Matrix 2 is
    /// used by no zone but repeats block 1. Zone 0 has a table and an NPC on (12, 12); zones 1 and 2 have none.
    /// Block 0: wall on (5, 5), encounter grass on (6, 5).
    /// </summary>
    internal static (byte[][] Zones, byte[][] Matrices, byte[][] Blocks) Data()
    {
        var map = new int[8 * 4];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++) map[(y * 8) + x] = x < 4 && !(x == 3 && y == 0) ? 0 : 1;
        byte[][] matrices = [Matrix(2, 1, [0, 1], map), Matrix(1, 1, [2], null), Matrix(1, 1, [1], null)];
        byte[][] blocks = [Block((5, 5, 0x21), (6, 5, Gen6CatchZones.EncounterGrass)), Block(), Block()];
        byte[][] zones = [Zone(0, [new(12, 12)], Table(7)), Zone(0, [new(50, 20)], null), Zone(1, [new(1, 1)], null)];
        return (zones, matrices, blocks);
    }

    /// <summary>OR/AS ZO pack: five parts, the table (0x104 bytes or empty) in part 3 and a 12-byte part 4.</summary>
    private static byte[] ZoneOras(int matrix, Map3dsGen6Checks.SyntheticNpc[] npcs, byte[]? table)
    {
        var pack = Gen6Pack.Read(Map3dsGen6Checks.Zone(matrix, 3, npcs), "ZO");
        return Map3dsGen6Checks.Pack("ZO", pack.Part(0).ToArray(), pack.Part(1).ToArray(), pack.Part(2).ToArray(), table ?? [], new byte[12]);
    }

    private static byte[] TableOras(byte marker)
    {
        var t = new byte[Gen6CatchFormat.ORAS.TableSize];
        t[4] = 1; t[0x0E] = marker; t[0x0F] = 1; t[0x10] = 2; t[0x11] = 3;
        return t;
    }

    /// <summary>«EN» pack mirroring <paramref name="zones"/>' part 3, as in the last member of the OR/AS zone archive.</summary>
    internal static byte[] Shared(byte[][] zones)
    {
        var tables = zones.Select(z => Gen6CatchZones.Table(z) ?? []).ToArray();
        int header = 4 + ((tables.Length + 1) * 4);
        var data = new byte[header + tables.Sum(t => t.Length)];
        data[0] = (byte)'E'; data[1] = (byte)'N';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)tables.Length);
        int at = header;
        for (int i = 0; i <= tables.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4 + (i * 4)), at);
            if (i < tables.Length) { tables[i].CopyTo(data, at); at += tables[i].Length; }
        }
        return data;
    }

    /// <summary>The X/Y layout of <see cref="Data"/> with OR/AS packs: zones 0–2, an opaque member and the «EN» member, grass 0x20004004 on (6, 5).</summary>
    internal static (byte[][] Zones, byte[][] Matrices, byte[][] Blocks) DataOras()
    {
        var d = Data();
        byte[][] zones = [ZoneOras(0, [new(12, 12)], TableOras(7)), ZoneOras(0, [new(50, 20)], null), ZoneOras(1, [new(1, 1)], null)];
        byte[][] members = [.. zones, [0, 0, 0, 0], Shared(zones)];
        byte[][] blocks = [Block((5, 5, 0x21), (6, 5, Gen6CatchFormat.ORAS.EncounterGrass)), d.Blocks[1], d.Blocks[2]];
        return (members, d.Matrices, blocks);
    }

    internal static Gen6MapBackend Backend((byte[][] Zones, byte[][] Matrices, byte[][] Blocks) d, Gen6Title game = Gen6Title.XY) =>
        new(game, d.Zones.Length, i => d.Zones[i], d.Matrices.Length, i => d.Matrices[i], d.Blocks.Length, i => d.Blocks[i], ["", "", "", "Pueblo Prueba"]);

    internal static void Run(Action<bool, string> check)
    {
        var d = Data();
        var b = Backend(d);
        var m0 = new Gen6MapMatrix(d.Matrices[0]);
        check(m0.ZoneAt(5, 5) == 0 && m0.ZoneAt(35, 5) == 1 && m0.ZoneAt(35, 15) == 0 && m0.ZoneAt(45, 5) == 1 && m0.ZoneAt(80, 5) == -1 && new Gen6MapMatrix(d.Matrices[1]).ZoneAt(1, 1) is null,
            "CatchZone_MatrixZoneMapGivesTheZoneOfEachTenByTenCells");
        check(b.CellOwner(0, 5, 5) == 0 && b.CellOwner(0, 35, 5) == 1 && b.CellOwner(2, 3, 3) == 2 && b.BlockUses(0) == 1 && b.BlockUses(1) == 2,
            "CatchZone_CellOwnerAndBlockUses");
        check(b.HasEncounterTable(0) && !b.HasEncounterTable(1) && b.EncounterSources.Select(e => e.Id).SequenceEqual([0]) && b.EncounterCells(0).SequenceEqual([(6, 5)]) && b.EncounterCells(1).Count == 0,
            "CatchZone_TablesSourcesAndExistingEncounterCells");

        // Writers.
        var painted = Gen6CatchZones.PaintBlock(d.Blocks[0], [(10, 10), (11, 10)]);
        int grid = Gen6Pack.Read(d.Blocks[0], "GR").Start(0);
        var changed = Enumerable.Range(0, painted.Length).Where(i => painted[i] != d.Blocks[0][i]).ToArray();
        int c1 = grid + 4 + (((10 * 40) + 10) * 4), c2 = grid + 4 + (((10 * 40) + 11) * 4);
        check(changed.Length > 0 && changed.All(i => (i >= c1 && i < c1 + 4) || (i >= c2 && i < c2 + 4)) && new Gen6MapBlock(painted).Cells.Cells[(10 * 40) + 10] == Gen6CatchZones.EncounterGrass,
            "PaintBlock_WritesOnlyTheChosenCells");
        bool wall = false, grass = false, outside = false;
        try { Gen6CatchZones.PaintBlock(d.Blocks[0], [(5, 5)]); } catch (InvalidOperationException) { wall = true; }
        try { Gen6CatchZones.PaintBlock(d.Blocks[0], [(6, 5)]); } catch (InvalidOperationException) { grass = true; }
        try { Gen6CatchZones.PaintBlock(d.Blocks[0], [(40, 0)]); } catch (ArgumentOutOfRangeException) { outside = true; }
        check(wall && grass && outside, "PaintBlock_OnlyPaintsPlainGroundInsideTheBlock");
        var withTable = Gen6CatchZones.AddTable(d.Zones[1], Table(7));
        var diff = Enumerable.Range(0, d.Zones[1].Length).Where(i => withTable[i] != d.Zones[1][i]).ToArray();
        check(withTable.Length == d.Zones[1].Length + Gen6CatchZones.TableSize && diff.All(i => i >= 20 && i < 24) && Gen6CatchZones.Table(withTable)!.SequenceEqual(Table(7))
            && new Gen6Zone(withTable).Entities.Records.Count == new Gen6Zone(d.Zones[1]).Entities.Records.Count, "AddTable_AppendsPartThreeAndOnlyMovesTheEndOffset");
        bool twice = false, size = false;
        try { Gen6CatchZones.AddTable(d.Zones[0], Table(7)); } catch (InvalidOperationException) { twice = true; }
        try { Gen6CatchZones.AddTable(d.Zones[1], new byte[0x178]); } catch (ArgumentException) { size = true; }
        check(twice && size && Gen6CatchZones.Table(d.Zones[1]) is null, "AddTable_RefusesZonesWithATableAndWrongSizes");

        // Validation.
        var own = b.ValidateCatchCells(0, [(10, 10), (11, 10)], null);
        check(own.Allowed && own.Reason.Contains("tabla de encuentros actual") && own.Reason.Contains("2 casillas") && own.Reason.Contains("no se verá"), "CatchCheck_ZoneWithTableUsesItsTable");
        check(!b.ValidateCatchCells(1, [(32, 2)], null).Allowed && b.ValidateCatchCells(1, [(32, 2)], 0) is { Allowed: true } copy && copy.Reason.Contains("copiando la de la zona 000")
            && !b.ValidateCatchCells(1, [(32, 2)], 2).Allowed && !b.ValidateCatchCells(1, [(32, 2)], 1).Allowed, "CatchCheck_ZoneWithoutTableNeedsAValidSource");
        check(b.ValidateCatchCells(0, [(35, 5)], null).Reason.Contains("pertenece a la zona 1") && b.ValidateCatchCells(0, [(5, 5)], null).Reason.Contains("no es suelo normal")
            && b.ValidateCatchCells(0, [(6, 5)], null).Reason.Contains("no es suelo normal") && b.ValidateCatchCells(0, [(12, 12)], null).Reason.Contains("ocupa"),
            "CatchCheck_OtherZonesWallsGrassAndEventsAreRefused");
        check(b.ValidateCatchCells(1, [(45, 5)], 0).Reason.Contains("aparece en 2 posiciones") && !b.ValidateCatchCells(0, [], null).Allowed && !b.ValidateCatchCells(0, [(90, 5)], null).Allowed
            && !b.ValidateCatchCells(0, Enumerable.Range(0, 1601).Select(i => (i % 40, i / 40)).ToArray(), null).Allowed, "CatchCheck_SharedBlocksEmptyOutsideAndTooManyAreRefused");
        check(b.ValidateCatchCells(2, [(3, 3)], 0).Allowed && Backend(d, Gen6Title.ORAS).ValidateCatchCells(0, [(10, 10)], null).Reason.Contains("en Rubí Omega/Zafiro Alfa tienen 5"),
            "CatchCheck_InteriorOfASingleZoneIsAllowedAndXyPacksAreRefusedInOras");

        // OR/AS: grass 0x20004004, five-part packs, and the «EN» copy of every table in the last member.
        check(Gen6CatchFormat.Of(Gen6Title.XY) is { EncounterGrass: 0x20002004, TableSize: 0x188, PartCount: 4 }
            && Gen6CatchFormat.Of(Gen6Title.ORAS) is { EncounterGrass: 0x20004004, TableSize: 0x104, PartCount: 5 }, "CatchFormat_ValuesAreReadPerFamily");
        var o = DataOras();
        var ob = Backend(o, Gen6Title.ORAS);
        var orasPainted = new Gen6MapBlock(Gen6CatchZones.PaintBlock(o.Blocks[0], [(10, 10)], Gen6CatchFormat.ORAS));
        check(orasPainted.Cells.Cells[(10 * 40) + 10] == 0x20004004 && ob.EncounterCells(0).SequenceEqual([(6, 5)]) && ob.HasEncounterTable(0) && !ob.HasEncounterTable(1)
            && ob.EncounterSources.Select(e => e.Id).SequenceEqual([0]), "CatchOras_GrassValueTablesAndSources");
        var orasTable = Gen6CatchZones.AddTable(o.Zones[1], TableOras(7), Gen6CatchFormat.ORAS);
        var pb = Gen6Pack.Read(o.Zones[1], "ZO"); var pa = Gen6Pack.Read(orasTable, "ZO");
        check(pa.Count == 5 && pa.Part(3).SequenceEqual(TableOras(7)) && pa.Part(4).SequenceEqual(pb.Part(4)) && pa.Start(4) == pb.Start(4) + 0x104 && pa.Start(3) == pb.Start(3)
            && Enumerable.Range(0, 3).All(k => pa.Part(k).SequenceEqual(pb.Part(k))), "CatchOras_TableIsInsertedBeforePartFourWhichOnlyMoves");
        bool xyOnOras = false, orasSize = false;
        try { Gen6CatchZones.AddTable(o.Zones[1], Table(7), Gen6CatchFormat.XY); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { xyOnOras = true; }
        try { Gen6CatchZones.AddTable(o.Zones[1], Table(7), Gen6CatchFormat.ORAS); } catch (ArgumentException) { orasSize = true; }
        check(xyOnOras && orasSize, "CatchOras_XyFormatAndSizesAreRefused");
        var slices = Gen6CatchZones.SharedSlices(o.Zones[4], 3)!;
        var en = Gen6CatchZones.AddSharedTable(o.Zones[4], 3, 1, TableOras(7));
        var after2 = Gen6CatchZones.SharedSlices(en, 3)!;
        check(slices.Select(x => x.End - x.Start).SequenceEqual([0x104, 0, 0]) && after2.Select(x => x.End - x.Start).SequenceEqual([0x104, 0x104, 0])
            && en.AsSpan(after2[1].Start, 0x104).SequenceEqual(TableOras(7)) && en.AsSpan(after2[0].Start, 0x104).SequenceEqual(TableOras(7)) && Gen6CatchZones.SharedSlices(o.Zones[4], 4) is null,
            "CatchOras_SharedCopyGetsTheNewSliceAndKeepsTheOthers");
        bool twiceShared = false;
        try { Gen6CatchZones.AddSharedTable(o.Zones[4], 3, 0, TableOras(7)); } catch (InvalidOperationException) { twiceShared = true; }
        check(twiceShared && ob.SharedTables is not null && ob.SharedZoneCount == 3 && ob.SharedTablesMember == 4, "CatchOras_SharedCopyIsFoundAndNotWrittenTwice");
        check(ob.ValidateCatchCells(1, [(32, 2)], 0) is { Allowed: true } orasOk && orasOk.Reason.Contains("0x20004004") && orasOk.Reason.Contains("(EN)") && orasOk.Reason.Contains("No se ha probado"),
            "CatchOras_ZoneWithoutTableIsAllowedWithTheSharedCopy");
        var broken = (byte[])o.Zones[4].Clone(); broken[slices[0].Start + 0x20] ^= 0xFF;
        byte[][] brokenMembers = [.. o.Zones[..4], broken];
        byte[][] noShared = [.. o.Zones[..4], [0, 0, 0, 0]];
        check(Backend((brokenMembers, o.Matrices, o.Blocks), Gen6Title.ORAS).ValidateCatchCells(1, [(32, 2)], 0).Reason.Contains("no coincide")
            && Backend((brokenMembers, o.Matrices, o.Blocks), Gen6Title.ORAS).ValidateCatchCells(0, [(10, 10)], null).Reason.Contains("no coincide")
            && Backend((noShared, o.Matrices, o.Blocks), Gen6Title.ORAS).ValidateCatchCells(1, [(32, 2)], 0).Reason.Contains("No se encuentra la copia compartida"),
            "CatchOras_MismatchedOrMissingSharedCopyIsRefused");

        // Reading back the written data.
        byte[][] blocksAfter = [Gen6CatchZones.PaintBlock(d.Blocks[0], [(32, 2), (33, 2)]), d.Blocks[1], d.Blocks[2]];
        byte[][] zonesAfter = [d.Zones[0], withTable, d.Zones[2]];
        var after = Backend((zonesAfter, d.Matrices, blocksAfter));
        check(after.HasEncounterTable(1) && after.EncounterCells(1).OrderBy(c => c).SequenceEqual([(32, 2), (33, 2)]) && after.EncounterCells(0).SequenceEqual([(6, 5)])
            && after.EncounterSources.Select(e => e.Id).SequenceEqual([0, 1]), "CatchZone_WrittenCellsAndTableReadBack");
    }

    internal static void RunUi(Action<bool, string> check)
    {
        var d = Data();
        var calls = new List<(int Zone, (int X, int Y)[] Cells, int? Source)>();
        int reloads = 0;
        var mover = new Map3dsMover((b, z, n, x, y) => "m", () => { reloads++; return Backend(d); },
            paintCatch: (b, z, cells, source) => { calls.Add((z, cells.OrderBy(c => c).ToArray(), source)); return $"Zona de captura en la zona {z:000} ({cells.Count} casillas)"; });
        var view = new Map3dsEditorView(Backend(d), mover);
        Layout(view);
        view.ShowZone(1);
        check(view.CatchButton is not null && new Map3dsEditorView(Backend(d)).CatchButton is null, "Map3dsCatchUi_ButtonOnlyWithAProjectThatCanPaint");
        check(view.ShowCatchPanel() && view.Canvas.PickingCell && view.CatchSource is { Items.Count: 1 } && view.CatchSave is { IsEnabled: false }
            && view.Canvas.CatchOverlay is { Existing.Count: 0, Pending.Count: 0 }, "Map3dsCatchUi_PanelAsksForTheTableSourceOfAZoneWithoutTable");
        view.Canvas.PickCell(32, 2); view.Canvas.PickCell(33, 2); view.Canvas.PickCell(34, 2); view.Canvas.PickCell(34, 2);
        check(view.CatchCells!.Count == 2 && view.Canvas.CatchOverlay is { Pending.Count: 2 }, "Map3dsCatchUi_ClicksToggleCells");
        check(!view.PreviewCatch() && view.CatchResult!.Text.Contains("elige la zona") && view.CatchSave!.IsEnabled == false, "Map3dsCatchUi_NoSourceNoSave");
        view.CatchSource!.SelectedValue = 0;
        UiPump.Idle();
        check(view.PreviewCatch() && view.CatchSave!.IsEnabled && calls.Count == 0 && view.CatchResult!.Text.Contains("copiando"), "Map3dsCatchUi_PreviewChecksWithoutWriting");
        view.Canvas.PickCell(35, 2);
        check(!view.CatchSave!.IsEnabled && !view.SaveCatch() && calls.Count == 0, "Map3dsCatchUi_ChangingTheCellsNeedsANewPreview");
        var (x1, y1, x2, y2) = view.CatchRectangle!.Value;
        x1.Text = "32"; y1.Text = "3"; x2.Text = "33"; y2.Text = "4";
        check(view.AddCatchRectangle() && view.CatchCells!.Count == 7, "Map3dsCatchUi_RectangleAddsItsCells");
        check(view.PreviewCatch() && view.SaveCatch() && calls.Count == 1 && calls[0].Zone == 1 && calls[0].Source == 0 && calls[0].Cells.Length == 7 && reloads == 1
            && view.Status.Text.Contains("historial") && view.CatchCells is { Count: 0 }, "Map3dsCatchUi_SaveCommitsThroughTheProjectAndReopensThePanel");
        view.EventList.SelectedIndex = 0;
        UiPump.Idle();
        check(!view.Canvas.PickingCell && view.CatchCells is null && view.Canvas.CatchOverlay is null, "Map3dsCatchUi_SelectingAnEventLeavesThePanel");

        // OR/AS: same panel; the preview names the family's grass value and the «EN» copy.
        var o = DataOras();
        var orasCalls = new List<int?>();
        var oras = new Map3dsEditorView(Backend(o, Gen6Title.ORAS), new Map3dsMover((b, z, n, x, y) => "m", () => Backend(o, Gen6Title.ORAS),
            paintCatch: (b, z, cells, source) => { orasCalls.Add(source); return "Zona de captura"; }));
        Layout(oras);
        oras.ShowZone(1);
        oras.ShowCatchPanel();
        oras.Canvas.PickCell(32, 2);
        oras.CatchSource!.SelectedValue = 0;
        UiPump.Idle();
        check(oras.Canvas.CatchOverlay is { Pending.Count: 1 } && oras.PreviewCatch() && oras.CatchResult!.Text.Contains("0x20004004") && oras.CatchResult.Text.Contains("(EN)")
            && oras.SaveCatch() && orasCalls.SequenceEqual([(int?)0]), "Map3dsCatchUi_OrasPreviewNamesItsGrassAndSharedCopy");
    }

    private static void Layout(FrameworkElement content)
    {
        content.Measure(new Size(1400, 860)); content.Arrange(new Rect(0, 0, 1400, 860)); content.UpdateLayout(); UiPump.Idle(); content.UpdateLayout();
    }

    /// <summary>
    /// Real persistence on an X/Y copy in a test folder: every cell of the rectangle becomes an encounter cell of
    /// <paramref name="zone"/> (copying the table of <paramref name="source"/> if it has none); preview, commit, reopen,
    /// only those cells and that zone changed, undo of both archives, external changes; then reapplied.
    /// </summary>
    internal static void Probe(string copyRoot, int zone, int x1, int y1, int x2, int y2, int source, bool reapply = true)
    {
        string full = Path.GetFullPath(copyRoot);
        if (!full.Contains("Pruebas", StringComparison.OrdinalIgnoreCase)) throw new IOException("Por seguridad, la prueba solo escribe en una copia dentro de una carpeta de pruebas.");
        var session = GameProjectSession.Open(full, false);
        var service = new Map3dsCatchZoneService(session);
        string zones = service.ZoneArchive, blocks = service.BlockArchive;
        byte[] zonesBefore = File.ReadAllBytes(zones), blocksBefore = File.ReadAllBytes(blocks);
        string zonesHash = Convert.ToHexString(SHA256.HashData(zonesBefore)), blocksHash = Convert.ToHexString(SHA256.HashData(blocksBefore));
        var before = Gen6MapBackend.FromConfig(session.GameConfiguration);
        int editorBefore = new GameEncounterService(session).Entries.Count;
        var cells = new List<(int X, int Y)>();
        for (int y = y1; y <= y2; y++) for (int x = x1; x <= x2; x++) cells.Add((x, y));
        bool hadTable = before.HasEncounterTable(zone);
        Console.WriteLine($"Zona {zone} ({before.Zones.First(z => z.Id == zone).Name}): tabla {(hadTable ? "sí" : "no")}, {before.EncounterCells(zone).Count} casillas con encuentros; rectángulo ({x1},{y1})–({x2},{y2}) = {cells.Count} casillas; origen de la tabla {source}");
        Console.WriteLine("Comprobación: " + before.ValidateCatchCells(zone, cells, source).Reason);
        var proposal = service.Preview(before, zone, cells, source);
        bool previewSafe = File.ReadAllBytes(zones).AsSpan().SequenceEqual(zonesBefore) && File.ReadAllBytes(blocks).AsSpan().SequenceEqual(blocksBefore);
        Console.WriteLine((previewSafe ? "PASS" : "FAIL") + " CatchReal_PreviewWritesNothing");
        service.Apply(proposal);
        var after = Gen6MapBackend.FromConfig(session.GameConfiguration);
        var now = after.EncounterCells(zone).ToHashSet();
        bool painted = cells.All(now.Contains) && now.Count == before.EncounterCells(zone).Count + cells.Count;
        Console.WriteLine((painted ? "PASS" : "FAIL") + $" CatchReal_ReopenedWithTheNewEncounterCells ({now.Count})");
        bool table = after.HasEncounterTable(zone) && (hadTable ? after.EncounterTable(zone)!.SequenceEqual(before.EncounterTable(zone)!) : after.EncounterTable(zone)!.SequenceEqual(before.EncounterTable(source)!));
        Console.WriteLine((table ? "PASS" : "FAIL") + " CatchReal_ZoneTableIsTheSourceCopyOrUnchanged");
        var layout = before.Layout(zone);
        var touched = cells.Select(c => layout.BlockAt(c.X / layout.BlockCells, c.Y / layout.BlockCells)).ToHashSet();
        var gb = new GARC.MemGARC(blocksBefore).Files; var ga = new GARC.MemGARC(File.ReadAllBytes(blocks)).Files;
        bool otherBlocks = ga.Length == gb.Length && Enumerable.Range(0, ga.Length).All(i => touched.Contains(i) || ga[i].AsSpan().SequenceEqual(gb[i]));
        bool onlyCells = touched.All(bi =>
        {
            var o = before.Block(bi)!.Cells.Cells; var n = after.Block(bi)!.Cells.Cells;
            var raw = before.BlockBytes(bi)!; var rawAfter = after.BlockBytes(bi)!;
            int grid = before.Block(bi)!.Pack.Start(0);
            var mine = cells.Where(c => layout.BlockAt(c.X / layout.BlockCells, c.Y / layout.BlockCells) == bi).Select(c => ((c.Y % 40) * 40) + (c.X % 40)).ToHashSet();
            return raw.Length == rawAfter.Length && Enumerable.Range(0, raw.Length).All(i => raw[i] == rawAfter[i] || (i >= grid + 4 && mine.Contains((i - grid - 4) / 4)))
                && mine.All(k => o[k] == Gen6CatchZones.Ground && n[k] == before.CatchFormat.EncounterGrass);
        });
        Console.WriteLine((otherBlocks && onlyCells ? "PASS" : "FAIL") + $" CatchReal_OnlyTheChosenCellsOfBlocks {string.Join(",", touched.Order())}Changed"
            + (otherBlocks ? "" : $" · otros bloques distintos: {string.Join(",", Enumerable.Range(0, Math.Min(ga.Length, gb.Length)).Where(i => !touched.Contains(i) && !ga[i].AsSpan().SequenceEqual(gb[i])).Take(8))} ({gb.Length} → {ga.Length})")
            + (onlyCells ? "" : " · el bloque cambió fuera de las casillas"));
        var zb = new GARC.MemGARC(zonesBefore).Files; var za = new GARC.MemGARC(File.ReadAllBytes(zones)).Files;
        // In OR/AS the «EN» copy of the tables (last member) gets the new table too.
        var allowed = new HashSet<int>(hadTable ? [] : session.GameConfiguration.ORAS ? [zone, before.SharedTablesMember] : [zone]);
        var differing = Enumerable.Range(0, Math.Min(za.Length, zb.Length)).Where(i => !za[i].AsSpan().SequenceEqual(zb[i])).ToArray();
        bool otherZones = za.Length == zb.Length && differing.All(allowed.Contains);
        Console.WriteLine((otherZones ? "PASS" : "FAIL") + $" CatchReal_OtherZonesByteIdentical (miembros cambiados: {string.Join(",", differing)})");
        // The existing encounter editor sees the new table (one more location) and can read every table.
        var encounters = new GameEncounterService(session);
        foreach (var e in encounters.Entries) _ = encounters.Read(e.Id);
        bool editor = encounters.Entries.Count == editorBefore + (hadTable ? 0 : 1);
        Console.WriteLine((editor ? "PASS" : "FAIL") + $" CatchReal_EncounterEditorListsTheTable ({editorBefore} → {encounters.Entries.Count} ubicaciones)");
        bool shared = true;
        if (session.GameConfiguration.ORAS)
        {
            try { Console.WriteLine($"Copia compartida EN: {CatchZoneRealCopyChecks.VerifyShared(session.GameConfiguration)} tablas idénticas a sus zonas"); }
            catch (IOException ex) { shared = false; Console.WriteLine("Copia compartida EN: " + ex.Message); }
            Console.WriteLine((shared ? "PASS" : "FAIL") + " CatchReal_SharedTablesMatchEveryZone");
        }
        var entry = session.History.Read().Entries.Last();
        bool recorded = entry.Parameters.GetValueOrDefault("ActionId") == Map3dsCatchZoneService.ActionId && entry.Parameters.GetValueOrDefault("Gameplay") == "not tested";
        Console.WriteLine((recorded ? "PASS" : "FAIL") + $" CatchReal_HistoryEntry «{entry.Action}»");
        RestorePoints.Undo(session.History, session.ProjectRoot, entry.Id);
        bool restored = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zones))) == zonesHash && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(blocks))) == blocksHash;
        Console.WriteLine((restored ? "PASS" : "FAIL") + " CatchReal_UndoRestoresBothOriginalArchives");
        service.Apply(service.Preview(Gen6MapBackend.FromConfig(session.GameConfiguration), zone, cells, source));
        bool stale = false;
        try { service.Preview(before, zone, cells, source); } catch (InvalidDataException) { stale = true; }
        RestorePoints.Undo(session.History, session.ProjectRoot, session.History.Read().Entries.Last().Id);
        Console.WriteLine((stale ? "PASS" : "FAIL") + " CatchReal_AnEditorThatReadOldFilesCannotWrite");
        bool restoredAgain = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zones))) == zonesHash && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(blocks))) == blocksHash;
        if (!(previewSafe && painted && table && otherBlocks && onlyCells && otherZones && editor && shared && recorded && restored && stale && restoredAgain)) throw new InvalidDataException("La prueba real de zonas de captura falló.");
        if (!reapply) { Console.WriteLine("La copia queda como estaba (sin el cambio)."); return; }
        service.Apply(service.Preview(Gen6MapBackend.FromConfig(session.GameConfiguration), zone, cells, source));
        Console.WriteLine("Zona de captura aplicada de nuevo en la copia para la prueba jugable · gameplay NOT tested");
    }

    /// <summary>Rebuilds the copy into a 3DS image and checks both edited archives are in it byte for byte.</summary>
    internal static void Rebuild(string copyRoot, string image)
    {
        var project = GameProjectSession.Open(copyRoot, false);
        var built = new GameRomRebuildService(project).BuildAsync(RomRebuildKind.Full3DS, image).GetAwaiter().GetResult();
        Console.WriteLine($"Imagen: {built.File}, {built.Bytes} bytes, SHA256 {built.Hash}");
        var cfg = project.GameConfiguration;
        long zones = Map3dsTrainerFacingChecks.FindInImage(image, Path.Combine(cfg.RomFS, cfg.GetGARCFileName("encdata")));
        long blocks = Map3dsTrainerFacingChecks.FindInImage(image, Path.Combine(cfg.RomFS, cfg.GetGARCFileName("mapGR")));
        Console.WriteLine((zones >= 0 ? "PASS" : "FAIL") + $" CatchRebuild_ImageContainsTheZoneArchive (0x{zones:X})");
        Console.WriteLine((blocks >= 0 ? "PASS" : "FAIL") + $" CatchRebuild_ImageContainsTheBlockArchive (0x{blocks:X}); gameplay NOT tested");
        if (zones < 0 || blocks < 0) throw new InvalidDataException("La imagen no contiene los archivos editados.");
    }
}
