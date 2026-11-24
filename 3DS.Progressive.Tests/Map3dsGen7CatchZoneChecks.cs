using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Windows;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;

/// <summary>Catch zones in Ultra Sun / Ultra Moon: an encounter region (ED part 6) over grass 0x1F and its table (EA).</summary>
internal static class Map3dsGen7CatchZoneChecks
{
    internal static byte[] RegionFile(params (uint Table, uint Mask, Vector3 Pos, Vector3 Size)[] regions)
    {
        if (regions.Length == 0) return [];
        var f = new byte[4 + (regions.Length * 28) + (regions.Length * 48)];
        BinaryPrimitives.WriteInt32LittleEndian(f, regions.Length);
        for (int i = 0; i < regions.Length; i++)
        {
            int h = 4 + (i * 28), s = 4 + (regions.Length * 28) + (i * 48);
            BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(h), 6); BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(h + 4), regions[i].Table);
            BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(h + 8), regions[i].Mask); BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(h + 24), s);
            BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(s), 1); BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(s + 4), 1);
            float[] v = [regions[i].Pos.X, regions[i].Pos.Y, regions[i].Pos.Z, 0, 0, 0, 1, regions[i].Size.X, regions[i].Size.Y, regions[i].Size.Z];
            for (int k = 0; k < v.Length; k++) BinaryPrimitives.WriteSingleLittleEndian(f.AsSpan(s + 8 + (k * 4)), v[k]);
        }
        return f;
    }

    /// <summary>ED with 17 parts like USUM (part 6 = one file per zone, magic EA); the other parts are small opaque packs.</summary>
    internal static byte[] Ed(params byte[][] regionFiles) =>
        Gen7CatchZones.Pack("ED", Enumerable.Range(0, 17).Select(p => p == Gen7CatchZones.RegionPack ? Gen7CatchZones.Pack("EA", regionFiles)
            : Gen7CatchZones.Pack("EP", [BitConverter.GetBytes(p)])).ToArray());

    internal static byte[] Entry(byte marker)
    {
        var e = new byte[Gen7CatchZones.TableSize];
        e[0] = 1; e[4] = marker; e[5] = 2; e[0x168] = marker;
        return e;
    }

    /// <summary>Collision block: grass 0x1F on X/Z 0–1050 and plain ground 0x01 on X 1050–2100, Z 0–1050, at height <paramref name="y"/>.</summary>
    internal static byte[] Block(float y = 0, uint plain = 0x01)
    {
        (Vector3, Vector3, Vector3, uint)[] tris =
        [
            (new(0, 0, 0), new(1050, 0, 0), new(0, 0, 1050), 0x1F), (new(1050, 0, 0), new(1050, 0, 1050), new(0, 0, 1050), 0x1F),
            (new(1050, 0, 0), new(2100, 0, 0), new(1050, 0, 1050), plain), (new(2100, 0, 0), new(2100, 0, 1050), new(1050, 0, 1050), plain),
        ];
        var up = new Vector3(0, y, 0);
        tris = [.. tris.Select(t => (t.Item1 + up, t.Item2 + up, t.Item3 + up, t.Item4))];
        var col = new byte[8 + (tris.Length * 72)];
        BinaryPrimitives.WriteUInt32LittleEndian(col, Gen7Collision.Magic); BinaryPrimitives.WriteInt32LittleEndian(col.AsSpan(4), tris.Length);
        for (int i = 0; i < tris.Length; i++)
        {
            int at = 8 + (i * 72);
            foreach (var (k, p) in new[] { (0, tris[i].Item1), (16, tris[i].Item2), (32, tris[i].Item3) })
            { BinaryPrimitives.WriteSingleLittleEndian(col.AsSpan(at + k), p.X); BinaryPrimitives.WriteSingleLittleEndian(col.AsSpan(at + k + 4), p.Y); BinaryPrimitives.WriteSingleLittleEndian(col.AsSpan(at + k + 8), p.Z); }
            BinaryPrimitives.WriteUInt32LittleEndian(col.AsSpan(at + 64), tris[i].Item4);
        }
        return Map3dsGen6Checks.Pack("BG", [.. Enumerable.Range(0, 6).Select(_ => new byte[4]), col]);
    }

    /// <summary>
    /// Area 0: no regions, empty EA (the target), on its own copy of the collision block (block 1). Area 1: one grass region
    /// (table 0, mask bit 31) over the grass and an EA with one table (the source). Area 2: two region files (refused).
    /// Area 3: no regions, on block 0 like areas 1 and 2 (terrain of another area, not offered).
    /// </summary>
    internal static Dictionary<int, byte[]> Files()
    {
        var tr = Map3dsGen6Checks.Pack("TR", Map3dsGen7Checks.Tr(1, 1, 4200, 0));
        var own = Map3dsGen6Checks.Pack("TR", Map3dsGen7Checks.Tr(1, 1, 4200, 1));
        var grass = (0u, Gen7CatchZones.MaskOf(0x1F), new Vector3(525, 0, 525), new Vector3(525, 500, 525));
        return new Dictionary<int, byte[]>
        {
            [0] = Ed(RegionFile()), [2] = own, [9] = Gen7CatchZones.Pack("EA", []),
            [11] = Ed(RegionFile(grass)), [13] = tr, [20] = Gen7CatchZones.Pack("EA", [Entry(7)]),
            [22] = Ed(RegionFile(), RegionFile()), [24] = tr, [31] = Gen7CatchZones.Pack("EA", []),
            [33] = Ed(RegionFile()), [35] = tr, [42] = Gen7CatchZones.Pack("EA", []),
        };
    }

    internal static Gen7MapBackend Backend(Dictionary<int, byte[]> files)
    {
        // Block 1 (area 0's own terrain) is higher: it is not the same ground as block 0 of the other areas.
        var block = Block(); var own = Block(1000);
        return new Gen7MapBackend("Prueba", 44, i => files.GetValueOrDefault(i, []), 2, id => id == 1 ? own : block);
    }

    internal static void Run(Action<bool, string> check)
    {
        var f = Files();
        check(Gen7CatchZones.RoundTrips(f[0], "ED") && Gen7CatchZones.TableCount(f[9]) == 0 && f[9].Length == 8 && Gen7CatchZones.Parts(f[9], "EA").Length == 0
            && Gen7CatchZones.TableCount(f[20]) == 1 && Gen7CatchZones.RegionFiles(f[0]) == 1 && Gen7CatchZones.RegionFiles(f[22]) == 2, "Gen7Catch_PacksRebuildByteForByteIncludingTheEmptyTablePack");
        var src = Gen7CatchZones.Regions(f[11]);
        check(src.Count == 1 && src[0] is { Box: true, Table: 0, Mask: 0x80000000 } r && r.Allows(0x1F) && !r.Allows(0x01) && r.Contains(new Vector3(500, 0, 500)) && !r.Contains(new Vector3(1600, 0, 500)),
            "Gen7Catch_RegionRecordAndBoxAreRead");
        var one = Gen7CatchZones.AddRegion(f[0], 0, 3, 0x80000000, new Vector3(100, 2, 300), new Vector3(210, 500, 105));
        var two = Gen7CatchZones.AddRegion(f[11], 0, 1, 0x80000000, new Vector3(900, 0, 900), new Vector3(105, 500, 105));
        var oneR = Gen7CatchZones.Regions(one); var twoR = Gen7CatchZones.Regions(two);
        var oldParts = Gen7CatchZones.Parts(f[11], "ED"); var newParts = Gen7CatchZones.Parts(two, "ED");
        check(oneR.Count == 1 && oneR[0] is { Table: 3, Box: true } && oneR[0].Size == new Vector3(210, 500, 105) && twoR.Count == 2 && twoR[0] == src[0] && twoR[1].Table == 1
            && Enumerable.Range(0, 17).Where(p => p != 6).All(p => oldParts[p].AsSpan().SequenceEqual(newParts[p])) && Gen7CatchZones.RoundTrips(two, "ED"),
            "Gen7Catch_AddRegionKeepsTheOtherRegionsAndParts");
        bool badSize = false, badMask = false, badFile = false;
        try { Gen7CatchZones.AddRegion(f[0], 0, 0, 1, Vector3.Zero, new Vector3(0, 500, 10)); } catch (ArgumentOutOfRangeException) { badSize = true; }
        try { Gen7CatchZones.AddRegion(f[0], 0, 0, 0, Vector3.Zero, new Vector3(10, 500, 10)); } catch (ArgumentOutOfRangeException) { badMask = true; }
        try { Gen7CatchZones.AddRegion(f[0], 3, 0, 1, Vector3.Zero, new Vector3(10, 500, 10)); } catch (ArgumentOutOfRangeException) { badFile = true; }
        check(badSize && badMask && badFile, "Gen7Catch_AddRegionRefusesEmptyBoxesMasksAndFiles");
        var (ea1, i1) = Gen7CatchZones.AddTable(f[9], Entry(5));
        var (ea2, i2) = Gen7CatchZones.AddTable(f[20], Entry(5));
        bool size = false;
        try { Gen7CatchZones.AddTable(f[9], new byte[100]); } catch (ArgumentException) { size = true; }
        check(i1 == 0 && i2 == 1 && Mini.UnpackMini(ea1, "EA")!.Length == 1 && Mini.UnpackMini(ea2, "EA")![0].AsSpan().SequenceEqual(Entry(7)) && Gen7CatchZones.Table(ea2, 1)!.AsSpan().SequenceEqual(Entry(5)) && size,
            "Gen7Catch_TablesAreAppendedAndReadByTheEncounterEditorUnpacker");

        // Vanilla EA: data at 0x80; an area without encounters has one empty entry, which the new table fills.
        var vanillaEmpty = Gen7CatchZones.Pack("EA", [Array.Empty<byte>()], 0x80);
        var vanillaFull = Gen7CatchZones.Pack("EA", [Entry(1), Entry(2)], 0x80);
        var (ve, vi) = Gen7CatchZones.AddTable(vanillaEmpty, Entry(5));
        var (vf, vj) = Gen7CatchZones.AddTable(vanillaFull, Entry(5));
        check(vanillaEmpty.Length == 0x80 && Gen7CatchZones.RoundTrips(vanillaEmpty, "EA") && Gen7CatchZones.TableCount(vanillaEmpty) == 0 && Gen7CatchZones.Table(vanillaEmpty, 0) is null
            && vi == 0 && Gen7CatchZones.DataStart(ve) == 0x80 && Gen7CatchZones.Parts(ve, "EA").Length == 1 && Gen7CatchZones.TableCount(ve) == 1
            && vj == 2 && Gen7CatchZones.DataStart(vf) == 0x80 && Gen7CatchZones.Parts(vf, "EA")[1].AsSpan().SequenceEqual(Entry(2)) && Mini.UnpackMini(vf, "EA")!.Length == 3,
            "Gen7Catch_VanillaTablePacksKeepTheirHeaderAndFillTheEmptyEntry");
        bool full = false;
        try { Gen7CatchZones.AddTable(Gen7CatchZones.Pack("EA", Enumerable.Range(0, 30).Select(i => Entry((byte)i)).ToArray(), 0x80), Entry(9)); } catch (InvalidOperationException) { full = true; }
        check(full, "Gen7Catch_AVanillaHeaderThatWouldOverflowIsRefused");

        var b = Backend(f);
        check(b.GrassCells(0).Count == 100 && b.HasEncounterTable(1) && !b.HasEncounterTable(0) && b.RequiresTableSource(0) && b.RequiresTableSource(1)
            && b.EncounterSources.Select(s => s.Id).SequenceEqual([1 * Gen7MapBackend.SourceStride]) && b.EncounterCells(1).Count == 100 && b.EncounterCells(0).Count == 0,
            "Gen7Catch_GrassCellsSourcesAndExistingEncounterCells");
        int source = Gen7MapBackend.SourceStride;
        var ok = b.ValidateCatchCells(0, [(2, 2), (3, 2)], source);
        var (plan, _) = b.PlanCatch(0, [(2, 2), (3, 2)], source);
        check(ok.Allowed && ok.Reason.Contains("Se copiará al área la tabla 0 del área 001") && plan is { NewTable: true, SourceArea: 1, Covered: 0, Mask: 0x80000000 }
            && plan.Position == new Vector3(315, 1000, 262.5f) && plan.Size == new Vector3(105, 500, 52.5f), "Gen7Catch_PlanIsABoxOverTheCellsWithACopiedTable");
        check(b.PlanCatch(0, [(2, 2), (4, 2)], source).Plan is { Covered: 1 } && b.ValidateCatchCells(0, [(2, 2), (4, 2)], source).Reason.Contains("1 casillas del mismo suelo"),
            "Gen7Catch_GrassInsideTheBoxThatWasNotMarkedIsReported");
        check(b.ValidateCatchCells(0, [(12, 2)], source).Reason.Contains("no tiene un suelo de encuentros admitido") && b.ValidateCatchCells(0, [(50, 50)], source).Reason.Contains("no tiene suelo")
            && b.ValidateCatchCells(0, [(2, 2)], null).Reason.Contains("Elige la tabla") && !b.ValidateCatchCells(0, [(2, 2)], 5).Allowed
            && b.ValidateCatchCells(2, [(2, 2)], source).Reason.Contains("una zona") && !b.ValidateCatchCells(0, [], source).Allowed,
            "Gen7Catch_OtherGroundMissingSourcesAndMultiZoneAreasAreRefused");
        check(b.PaintableCells(0).Count == 100 && b.PaintableCells(1).Count == 0 && b.PaintableCells(3).Count == 0 && b.OtherOwner(1, (2, 2)) is null
            && b.ValidateCatchCells(3, [(2, 2)], source).Reason.Contains("terreno de 001"),
            "Gen7Catch_TerrainSharedWithAnotherAreaIsOnlyWrittenByTheAreaWithEncountersOnIt");
        check(Gen7CatchZones.Regions(f[11])[0] is var v && v.Contains(new Vector3(1049, 0, 1049)) && !v.Contains(new Vector3(1060, 0, 525)) && !v.Contains(new Vector3(525, 0, -10)),
            "Gen7Catch_BoxSizeIsAHalfExtent");
        check(b.PlanCatch(1, [(2, 2)], source).Plan is { NewTable: false, SourceTable: 0 } && b.ValidateCatchCells(1, [(2, 2)], source).Reason.Contains("Usará la tabla 0 que ya tiene el área"),
            "Gen7Catch_AnAreaCanReuseItsOwnTable");
        var g = new Dictionary<int, byte[]>(f) { [0] = Gen7CatchZones.AddRegion(f[0], 0, 0, plan!.Mask, plan.Position, plan.Size), [9] = ea1 };
        var after = Backend(g);
        check(after.EncounterCells(0).OrderBy(c => c).SequenceEqual([(2, 2), (3, 2)]) && after.HasEncounterTable(0), "Gen7Catch_WrittenZoneReadsBackAsEncounterCells");
        // Sand (0x19, the only bit of the Haina Desert masks) next to the grass: written with its own mask; mixed soils refused.
        var sandBlock = Block(1000, 0x19); var plainBlock = Block();
        var sand = new Gen7MapBackend("Prueba", 44, i => f.GetValueOrDefault(i, []), 2, id => id == 1 ? sandBlock : plainBlock);
        var (sandPlan, _) = sand.PlanCatch(0, [(12, 2), (13, 2)], source);
        check(sandPlan is { Mask: 0x02000000 } && sand.ValidateCatchCells(0, [(12, 2)], source).Reason.Contains("suelo 0x19")
            && sand.ValidateCatchCells(0, [(2, 2), (12, 2)], source).Reason.Contains("suelos distintos") && sand.PaintableCells(0).Count == 200
            && Gen7CatchZones.AttributeOf(0x80000000) == 0x1F && Gen7CatchZones.AttributeOf(0x80000001) is null && !Gen7CatchZones.IsWritable(0x01) && !Gen7CatchZones.IsWritable(0x12),
            "Gen7Catch_OtherVanillaSingleBitSoilsAreWrittenWithTheirOwnMask");
    }

    internal static void RunUi(Action<bool, string> check)
    {
        var f = Files();
        var calls = new List<(int Area, int Cells, int? Source)>();
        var view = new Map3dsEditorView(Backend(f), new Map3dsMover((b, z, n, x, y) => "m", () => Backend(f),
            paintCatch: (b, z, cells, source) => { calls.Add((z, cells.Count, source)); return "Zona de captura"; }));
        view.Measure(new Size(1400, 860)); view.Arrange(new Rect(0, 0, 1400, 860)); view.UpdateLayout(); UiPump.Idle();
        view.ShowZone(1);
        bool shown = view.EncounterLayer is { IsChecked: true } && view.Canvas.CatchOverlay is { Existing.Count: > 0, Pending.Count: 0 };
        view.EncounterLayer!.IsChecked = false;
        bool hidden = view.Canvas.CatchOverlay is null;
        view.EncounterLayer.IsChecked = true;
        check(shown && hidden && view.Canvas.CatchOverlay is { Existing.Count: > 0 }, "Map3dsGen7CatchUi_EncounterLayerShowsEncounterCellsOutsideThePanel");
        view.ShowZone(0);
        check(view.CatchButton is not null && view.ShowCatchPanel() && view.CatchSource is { Items.Count: 1 }, "Map3dsGen7CatchUi_PanelOffersTheGameGrassTables");
        check(view.CatchPaintable is { Count: > 0 } paint && paint.All(c => c.X < 10) && view.Canvas.CatchOverlay is { Paintable.Count: > 0 } o && o.Paintable.Count == paint.Count,
            "Map3dsGen7CatchUi_PanelMarksTheCellsWhereAZoneCanBeCreated");
        view.Canvas.PickCell(2, 2); view.Canvas.PickCell(3, 2);
        view.CatchSource!.SelectedValue = Gen7MapBackend.SourceStride;
        UiPump.Idle();
        check(view.PreviewCatch() && view.CatchResult!.Text.Contains("región de encuentros") && view.SaveCatch() && calls.SequenceEqual([(0, 2, (int?)Gen7MapBackend.SourceStride)]),
            "Map3dsGen7CatchUi_PreviewAndSaveThroughTheProject");
        view.ShowCatchPanel(); view.Canvas.PickCell(12, 2);
        view.CatchSource!.SelectedValue = Gen7MapBackend.SourceStride; UiPump.Idle();
        check(!view.PreviewCatch() && view.CatchResult!.Text.Contains("no tiene un suelo de encuentros admitido") && calls.Count == 1, "Map3dsGen7CatchUi_OtherGroundCannotBeSaved");
    }

    /// <summary>
    /// Real persistence on an Ultra Sun / Ultra Moon copy in a test folder: every grass cell of the rectangle gets encounters with
    /// table <paramref name="source"/> (area × 1000 + table); preview, commit, reopen, only the area's two members changed,
    /// encounter editor, exact restore of both members, external changes; then reapplied for the gameplay test.
    /// </summary>
    internal static void Probe(string copyRoot, int area, int x1, int y1, int x2, int y2, int source)
    {
        string full = Path.GetFullPath(copyRoot);
        if (!full.Contains("Pruebas", StringComparison.OrdinalIgnoreCase)) throw new IOException("Por seguridad, la prueba solo escribe en una copia dentro de una carpeta de pruebas.");
        var session = GameProjectSession.Open(full, false);
        var service = new Map3dsGen7CatchZoneService(session);
        string archive = service.FieldArchive;
        byte[] original = File.ReadAllBytes(archive);
        string hash = Convert.ToHexString(SHA256.HashData(original));
        var before = Gen7MapBackend.FromConfig(session.GameConfiguration);
        var cells = before.PaintableCells(area).Where(c => c.X >= x1 && c.X <= x2 && c.Y >= y1 && c.Y <= y2).ToArray();
        int editorBefore = new GameEncounterService(session).Entries.Count;
        Console.WriteLine($"Área {area} ({before.Zones.First(z => z.Id == area).Name}): {Gen7CatchZones.TableCount(before.TableBytes(area))} tablas, {before.EncounterRegions(area).Count} regiones; {cells.Length} casillas de hierba en el rectángulo; fuente {source}");
        Console.WriteLine("Comprobación: " + before.ValidateCatchCells(area, cells, source).Reason);
        var proposal = service.Preview(before, area, cells, source);
        bool previewSafe = File.ReadAllBytes(archive).AsSpan().SequenceEqual(original);
        Console.WriteLine((previewSafe ? "PASS" : "FAIL") + " Gen7CatchReal_PreviewWritesNothing");
        service.Apply(proposal);
        var after = Gen7MapBackend.FromConfig(session.GameConfiguration);
        var now = after.EncounterCells(area).ToHashSet();
        bool painted = cells.All(now.Contains) && after.EncounterRegions(area).Count == before.EncounterRegions(area).Count + 1;
        Console.WriteLine((painted ? "PASS" : "FAIL") + $" Gen7CatchReal_ReopenedWithTheRegionAndItsCells ({now.Count} casillas con encuentros)");
        var (sa, st) = Gen7MapBackend.SourceOf(source);
        var newRegion = after.EncounterRegions(area).Last();
        bool table = Gen7CatchZones.Table(after.TableBytes(area), (int)newRegion.Table) is { } t && t.AsSpan().SequenceEqual(Gen7CatchZones.Table(before.TableBytes(sa), st));
        Console.WriteLine((table ? "PASS" : "FAIL") + $" Gen7CatchReal_RegionTableIsTheSourceTable (tabla {newRegion.Table}, máscara {newRegion.Mask:X8})");
        var mb = new GARC.MemGARC(original).Files; var ma = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        int ev = area * Gen7MapBackend.SlotsPerWorld, tb = ev + Gen7CatchZones.TableSlot;
        var differing = Enumerable.Range(0, Math.Min(ma.Length, mb.Length)).Where(i => !ma[i].AsSpan().SequenceEqual(mb[i])).ToArray();
        bool others = ma.Length == mb.Length && differing.All(i => i == ev || i == tb);
        Console.WriteLine((others ? "PASS" : "FAIL") + $" Gen7CatchReal_OnlyTheAreaEventsAndTablesChanged (miembros {string.Join(",", differing)})");
        var encounters = new GameEncounterService(session);
        foreach (var e in encounters.Entries) _ = encounters.Read(e.Id);
        bool editor = encounters.Entries.Count == editorBefore + (sa == area ? 0 : 2);
        Console.WriteLine((editor ? "PASS" : "FAIL") + $" Gen7CatchReal_EncounterEditorListsTheTable ({editorBefore} → {encounters.Entries.Count} ubicaciones: día y noche)");
        var entry = session.History.Read().Entries.Last();
        bool recorded = entry.Parameters.GetValueOrDefault("ActionId") == Map3dsGen7CatchZoneService.ActionId;
        Console.WriteLine((recorded ? "PASS" : "FAIL") + $" Gen7CatchReal_HistoryEntry «{entry.Action}»");
        bool canRestore = service.CanRestore(area);
        service.Apply(service.PreviewRestore(area));
        // Every member goes back byte for byte. The archive bytes can still differ from a vanilla dump: vanilla USUM
        // archives align members to 0x80 and the project's GARC writer (version 6) writes them aligned to 4, as on any save.
        var restoredMembers = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        bool restored = restoredMembers.Length == mb.Length && Enumerable.Range(0, mb.Length).All(i => restoredMembers[i].AsSpan().SequenceEqual(mb[i]));
        bool sameFile = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))) == hash;
        Console.WriteLine((canRestore && restored ? "PASS" : "FAIL") + $" Gen7CatchReal_RestorePutsBackEveryOriginalMember (archivo idéntico: {(sameFile ? "sí" : "no, alineación de miembros 0x80 → 4 del escritor GARC")})");
        service.Apply(service.Preview(Gen7MapBackend.FromConfig(session.GameConfiguration), area, cells, source));
        bool stale = false;
        try { service.Preview(before, area, cells, source); } catch (InvalidDataException) { stale = true; }
        Console.WriteLine((stale ? "PASS" : "FAIL") + " Gen7CatchReal_AnEditorThatReadOldFilesCannotWrite");
        if (!(previewSafe && painted && table && others && editor && recorded && canRestore && restored && stale)) throw new InvalidDataException("La prueba real de zonas de captura de USUM falló.");
        Console.WriteLine("Zona de captura aplicada en la copia para la prueba jugable · gameplay NOT tested");
    }

    /// <summary>
    /// Read-only: opens a real project in the map editor as the app does (project session, backend and mover) and walks
    /// the catch-zone panel of every area named like <paramref name="name"/>, without saving anything.
    /// </summary>
    internal static void UiProbe(string copyRoot, string name)
    {
        var session = GameProjectSession.Open(Path.GetFullPath(copyRoot), false);
        var view = new Map3dsEditorView(session);
        view.Measure(new Size(1400, 860)); view.Arrange(new Rect(0, 0, 1400, 860)); view.UpdateLayout(); UiPump.Idle();
        Console.WriteLine($"Botón «Zona de captura»: {(view.CatchButton is null ? "NO" : "sí")}; zonas en la lista: {view.Backend.Zones.Count}");
        foreach (var z in view.Backend.Zones.Where(z => z.Name.Contains(name, StringComparison.CurrentCultureIgnoreCase)))
        {
            view.ShowZone(z.Id);
            bool panel = view.ShowCatchPanel();
            var overlay = view.Canvas.CatchOverlay;
            var catchZones = (pk3DS.Core.Maps.IMapCatchZones)view.Backend;
            var gen7 = view.Backend as Gen7MapBackend;
            Console.WriteLine($"{z.Name} (id {z.Id}): panel {panel}, disponibles {view.CatchPaintable?.Count}, casillas con encuentros en el overlay {overlay?.Existing.Count}, fuentes {view.CatchSource?.Items.Count}, " +
                $"hierba 0x1F {gen7?.GrassCells(z.Id).Count}, regiones {gen7?.EncounterRegions(z.Id).Count}, archivos de regiones {Gen7CatchZones.RegionFiles(gen7!.EventBytes(z.Id))}, tablas {Gen7CatchZones.TableCount(gen7.TableBytes(z.Id))}");
            var grass = gen7.GrassCells(z.Id).Except(catchZones.EncounterCells(z.Id)).Take(2).ToArray();
            if (grass.Length == 0) grass = [(5, 5)];
            foreach (var (x, y) in grass) view.Canvas.PickCell(x, y);
            if (view.CatchSource is { Items.Count: > 0 }) view.CatchSource.SelectedIndex = 0;
            UiPump.Idle();
            view.PreviewCatch();
            Console.WriteLine($"   vista previa con {string.Join(" ", grass.Select(c => $"{c.X},{c.Y}"))}: {view.CatchResult?.Text}");
        }
        Console.WriteLine("Solo lectura: no se guardó nada.");
    }

    /// <summary>Rebuilds the copy into a 3DS image and checks the field archive is in it byte for byte.</summary>
    internal static void Rebuild(string copyRoot, string image)
    {
        var project = GameProjectSession.Open(copyRoot, false);
        var built = new GameRomRebuildService(project).BuildAsync(RomRebuildKind.Full3DS, image).GetAwaiter().GetResult();
        Console.WriteLine($"Imagen: {built.File}, {built.Bytes} bytes, SHA256 {built.Hash}");
        var cfg = project.GameConfiguration;
        long at = Map3dsTrainerFacingChecks.FindInImage(image, Path.Combine(cfg.RomFS, cfg.GetGARCFileName("encdata")));
        Console.WriteLine((at >= 0 ? "PASS" : "FAIL") + $" Gen7CatchRebuild_ImageContainsTheFieldArchive (0x{at:X}); gameplay NOT tested");
        if (at < 0) throw new InvalidDataException("La imagen no contiene el archivo de campo editado.");
        long zones = Map3dsTrainerFacingChecks.FindInImage(image, Path.Combine(cfg.RomFS, cfg.GetGARCFileName("zonedata")));
        Console.WriteLine((zones >= 0 ? "PASS" : "FAIL") + $" Gen7CatchRebuild_ImageContainsTheZoneTable (0x{zones:X})");
        if (zones < 0) throw new InvalidDataException("La imagen no contiene la tabla de zonas de la copia.");
        ImageContainsBlocks(copyRoot, image);
    }

    /// <summary>Checks the map block archive (a/0/8/6, visible grass) is in the image byte for byte, without rebuilding.</summary>
    internal static void ImageContainsBlocks(string copyRoot, string image)
    {
        var cfg = GameProjectSession.Open(copyRoot, false).GameConfiguration;
        long blocks = Map3dsTrainerFacingChecks.FindInImage(image, Path.Combine(cfg.RomFS, "a", "0", "8", "6"));
        Console.WriteLine((blocks >= 0 ? "PASS" : "FAIL") + $" Gen7CatchRebuild_ImageContainsTheBlockArchive (0x{blocks:X}); gameplay NOT tested");
        if (blocks < 0) throw new InvalidDataException("La imagen no contiene el archivo de bloques editado.");
    }
}
