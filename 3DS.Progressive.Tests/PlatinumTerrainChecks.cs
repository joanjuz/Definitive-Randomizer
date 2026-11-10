using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;

/// <summary>Terrain collision reader, spatial query and overlay model: synthetic checks plus a read-only real-ROM probe.</summary>
internal static class PlatinumTerrainChecks
{
    /// <summary>One land_data member: 4 u32 sizes + 0x800 attribute bytes + props/model/bdhc payloads.</summary>
    internal static byte[] Land(Func<int, int, ushort> attribute, int props = 48, int model = 20, int bdhc = 8)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(0x800u); w.Write((uint)props); w.Write((uint)model); w.Write((uint)bdhc);
        for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++) w.Write(attribute(x, z));
        w.Write(new byte[props + model + bdhc]); return stream.ToArray();
    }
    internal static byte[] Matrix(int width, int height, Func<int, int>? headers, Func<int, int> land)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(new byte[] { (byte)width, (byte)height, (byte)(headers is null ? 0 : 1), 0, 0 });
        if (headers is not null) for (int i = 0; i < width * height; i++) w.Write((ushort)headers(i));
        for (int i = 0; i < width * height; i++) w.Write((ushort)land(i));
        return stream.ToArray();
    }
    private static PlatinumEvent Ev(PlatinumEventKind kind, int index, int x, int y, int type = 0, int rx = 0, int ry = 0) =>
        new(kind, index, index, x, y, 0, 0, type, 0, 0, 0, 0, 0, rx, ry, -1, -1, "00");
    private static void Reject<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); } catch (T) { check(true, name); return; }
        throw new Exception("FAIL expected " + typeof(T).Name + ": " + name);
    }
    private static byte[] Archive(params byte[][] files) => NdsGen5RandomizerChecks.BuildNarc(files);
    /// <summary>Collision only at z=2,x=7; behaviour bytes and the unrelated bits 8-14 must not count as collision.</summary>
    private static ushort Pattern(int x, int z) => x == 7 && z == 2 ? (ushort)0x8000 : x == 1 && z == 1 ? (ushort)0x00FF : x == 3 && z == 3 ? (ushort)0x7F00 : x == 9 && z == 9 ? (ushort)0x8002 : (ushort)0;

    internal static void Run(Action<bool, string> check)
    {
        // ---- reader
        var terrain = PlatinumTerrain.ParseNarc(Archive(Land(Pattern), Land((x, z) => (ushort)(x == 1 && z == 2 ? 0x8000 : 0))));
        check(terrain.Count == 2 && terrain.Attribute(0, 7, 2) == 0x8000 && terrain.Attribute(0, 2, 7) == 0 && terrain.PropBytes(0) == 48 && terrain.ModelBytes(0) == 20 && terrain.BdhcBytes(0) == 8, "Terrain_AttributesAreRowMajorZThenX");
        var one = Matrix(2, 2, null, i => new[] { 0, 1, 0xFFFF, 0 }[i]);
        var layout = PlatinumMapLayout.ParseMatrix(one, 5, 0);
        check(terrain.Sample(layout, 7, 2).Status == PlatinumTerrainStatus.Blocked && terrain.Sample(layout, 2, 7).Status == PlatinumTerrainStatus.ClearOfCollision, "Terrain_NoXZTransposition");
        check(terrain.Sample(layout, 33, 2).Status == PlatinumTerrainStatus.Blocked && terrain.Sample(layout, 33, 2).LandId == 1 && terrain.Sample(layout, 34, 2).Status == PlatinumTerrainStatus.ClearOfCollision, "Terrain_BlockOffsetsUseTheMatrixCellLandId");
        check(!Enum.GetNames<PlatinumTerrainStatus>().Any(n => n.Contains("Walk") || n.Contains("Free") || n.Contains("Safe")), "Terrain_StatusEnumCannotExpressWalkable");
        check(terrain.Sample(layout, 1, 1).Status == PlatinumTerrainStatus.ClearOfCollision && terrain.Sample(layout, 3, 3).Status == PlatinumTerrainStatus.ClearOfCollision, "Terrain_OnlyBit15IsCollision");
        var pattern = PlatinumTerrain.ParseNarc(Archive(Land(Pattern))); var single = PlatinumMapLayout.ParseMatrix(Matrix(1, 1, null, _ => 0), 5, 0);
        check(pattern.Sample(single, 1, 1) is { Status: PlatinumTerrainStatus.ClearOfCollision, Behavior: 0xFF } && pattern.Sample(single, 3, 3).Status == PlatinumTerrainStatus.ClearOfCollision && pattern.Sample(single, 9, 9) is { Status: PlatinumTerrainStatus.Blocked, Behavior: 2 }, "Terrain_BehaviourByteAndOtherBitsDoNotBlock");
        check(terrain.Sample(layout, 0, 40).Status == PlatinumTerrainStatus.Unknown && terrain.Sample(layout, 0, 40).Reason.Contains("0xFFFF") && terrain.Sample(layout, -1, 0).Status == PlatinumTerrainStatus.Unknown && terrain.Sample(layout, 64, 0).Reason.Contains("fuera de la matriz"), "Terrain_EmptyAndOutsideCellsAreUnknownNotFree");
        var badId = PlatinumMapLayout.ParseMatrix(Matrix(1, 1, null, _ => 9), 5, 0);
        check(terrain.Sample(badId, 0, 0).Status == PlatinumTerrainStatus.Unknown && terrain.Sample(badId, 0, 0).Reason.Contains("fuera de la tabla"), "Terrain_LandIdOutsideTheArchiveIsUnknown");
        check(terrain.Status(layout, 7, 2) == terrain.Sample(layout, 7, 2).Status && terrain.Status(layout, 0, 40) == PlatinumTerrainStatus.Unknown && terrain.Status(layout, 33, 2) == PlatinumTerrainStatus.Blocked, "Terrain_FastStatusAgreesWithSample");
        Reject<ArgumentOutOfRangeException>(() => terrain.Attribute(0, 32, 0), check, "Terrain_AttributeRejectsLocalOutOfRange");
        Reject<ArgumentOutOfRangeException>(() => terrain.Attribute(5, 0, 0), check, "Terrain_AttributeRejectsUnknownLandId");

        // ---- strict file validation
        byte[] good = Land(Pattern);
        Reject<InvalidDataException>(() => PlatinumTerrain.ParseNarc(Archive(good[..0x20])), check, "Terrain_TruncatedMemberRejected");
        var wrongSize = (byte[])good.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(wrongSize, 0x400);
        Reject<InvalidDataException>(() => PlatinumTerrain.ParseNarc(Archive(wrongSize)), check, "Terrain_UnexpectedAttributeSizeRejected");
        var longer = good.Concat(new byte[4]).ToArray();
        Reject<InvalidDataException>(() => PlatinumTerrain.ParseNarc(Archive(longer)), check, "Terrain_TrailingBytesRejected");
        var lying = (byte[])good.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(lying.AsSpan(8), 0xFFFFFFF0);
        Reject<InvalidDataException>(() => PlatinumTerrain.ParseNarc(Archive(lying)), check, "Terrain_OverflowingSectionSizeRejected");
        Reject<InvalidDataException>(() => PlatinumTerrain.ParseNarc(new byte[64]), check, "Terrain_NonNarcRejected");
        var mutable = Archive(good); var parsed = PlatinumTerrain.ParseNarc(mutable); Array.Fill(mutable, (byte)0xFF);
        check(parsed.Attribute(0, 7, 2) == 0x8000, "Terrain_InputMutationCannotChangeTheReader");

        // ---- profile
        string folder = Path.Combine(Path.GetTempPath(), "nds-terrain-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            foreach (var (name, code, revision) in new[] { ("UnaccreditedArchive", "CPUE", (byte)1), ("OtherGame", "CPUP", (byte)1), ("OtherRevision", "CPUE", (byte)0) })
            {
                byte[] rom = NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]> { [PlatinumTerrain.LandDataPath] = Archive(good) }, code, revision);
                string path = Path.Combine(folder, name + ".nds"); File.WriteAllBytes(path, rom);
                using (var open = NdsRom.Open(path)) Reject<InvalidDataException>(() => PlatinumTerrain.Open(open), check, "Terrain_OpenRejects" + name);
                check(File.ReadAllBytes(path).SequenceEqual(rom), "Terrain_RejectionLeavesSourceUnchanged_" + name);
            }
        }
        finally { Directory.Delete(folder, true); }

        // ---- shared matrix between headers: terrain is a fact of the cell; ownership is a property of the header
        var shared = Matrix(2, 1, i => i == 0 ? 6 : 7, _ => 0);
        var six = PlatinumMapLayout.ParseMatrix(shared, 6, 4); var seven = PlatinumMapLayout.ParseMatrix(shared, 7, 4);
        var q6 = new NdsSpatialQuery(six, pattern, []); var q7 = new NdsSpatialQuery(seven, pattern, []);
        check(q6.Terrain(7, 2).Status == q7.Terrain(7, 2).Status && q6.Terrain(40, 5).Status == q7.Terrain(40, 5).Status, "TerrainQuery_SharedMatrixHasOneTerrainFact");
        check(q6.Assess(5, 5).Verdict == NdsTileVerdict.Allowed && q7.Assess(5, 5).Verdict == NdsTileVerdict.Indeterminate && q7.Assess(5, 5).Reasons.Any(r => r.Contains("otra cabecera")), "TerrainQuery_ClearTileOfAnotherHeaderIsIndeterminate");
        check(q7.Assess(7, 2).Verdict == NdsTileVerdict.Blocked && q6.Assess(7, 2).Verdict == NdsTileVerdict.Blocked && q6.Assess(39, 2).Verdict == NdsTileVerdict.Blocked, "TerrainQuery_BlockedStaysBlockedEvenInAnotherHeaderCell");

        // ---- spatial query
        var owned = PlatinumMapLayout.ParseMatrix(Matrix(1, 1, i => 5, _ => 0), 5, 0);
        var events = new[] { Ev(PlatinumEventKind.Npc, 0, 10, 10), Ev(PlatinumEventKind.Warp, 0, 12, 10), Ev(PlatinumEventKind.Trigger, 0, 14, 10, 0, 3, 2), Ev(PlatinumEventKind.Object, 0, 16, 10), Ev(PlatinumEventKind.Npc, 1, 18, 10, 3) };
        var query = new NdsSpatialQuery(owned, pattern, events, (32, 32));
        var free = query.Assess(20, 20);
        check(free.Verdict == NdsTileVerdict.Allowed && free.VerdictText == "Permitido según datos conocidos" && free.Provenance.Count >= 2 && free.Limitations.Single().Contains("scripts") && free.ToText().Contains("No evaluado:"), "TerrainQuery_AllowedKeepsProvenanceAndLimitations");
        check(query.Assess(7, 2) is { Verdict: NdsTileVerdict.Blocked } blocked && blocked.VerdictText.StartsWith("Bloqueado") && blocked.Terrain.Attribute == 0x8000, "TerrainQuery_BlockedKeepsTheRawAttribute");
        check(query.Assess(10, 10).Verdict == NdsTileVerdict.Indeterminate && query.Assess(10, 10).Occupants.Single().Kind == PlatinumEventKind.Npc && query.Assess(10, 10).Reasons.Any(r => r.Contains("NPC 0")), "TerrainQuery_TileWithAnNpcIsIndeterminate");
        check(query.Assess(10, 10, (PlatinumEventKind.Npc, 0)).Verdict == NdsTileVerdict.Allowed, "TerrainQuery_OwnTileDoesNotCountAsOccupied");
        check(query.Assess(12, 10).Verdict == NdsTileVerdict.Indeterminate && query.Assess(12, 10).Reasons.Any(r => r.Contains("warp 0")) && query.Assess(16, 10).Reasons.Any(r => r.Contains("objeto de fondo")) && query.Assess(18, 10).Reasons.Any(r => r.Contains("objeto de suelo")), "TerrainQuery_WarpsObjectsAndGroundItemsOccupyTiles");
        var inTrigger = query.Assess(15, 11);
        check(inTrigger.Verdict == NdsTileVerdict.Allowed && inTrigger.Overlapping.Count == 1 && inTrigger.Reasons.Any(r => r.Contains("rango declarado")) && query.Assess(14, 10).Overlapping.Count == 1 && query.Assess(17, 10).Overlapping.Count == 0, "TerrainQuery_TriggerRangeIsReportedNotAssumedSafe");
        check(query.Assess(33, 5) is { EditorAccepts: false, Verdict: NdsTileVerdict.Indeterminate } && query.Assess(-1, 5).EditorAccepts == false && query.Assess(31, 31).EditorAccepts, "TerrainQuery_ReportsWhatTheEditorWouldReject");
        check(query.Assess(1, 1).Reasons.Any(r => r.Contains("0xFF") && r.Contains("sin interpretar")), "TerrainQuery_BehaviourByteIsReportedRawNotInterpreted");
        var contextual = new NdsSpatialQuery(PlatinumMapLayout.ParseMatrix(Matrix(1, 1, null, _ => 0), 5, 0), pattern, []);
        check(contextual.Assess(20, 20).Verdict == NdsTileVerdict.Allowed && contextual.Assess(20, 20).Reasons.Any(r => r.Contains("contexto")), "TerrainQuery_ContextualCellsAreAllowedWithAnExplicitNote");
        var noData = new NdsSpatialQuery(owned, null, events);
        check(!noData.HasTerrain && noData.Assess(20, 20).Verdict == NdsTileVerdict.Indeterminate && noData.Status(20, 20) == PlatinumTerrainStatus.Unknown && noData.Assess(20, 20).Reasons.Any(r => r.Contains("sin datos")), "TerrainQuery_NoTerrainDataNeverMeansFree");
        var unknownCell = new NdsSpatialQuery(PlatinumMapLayout.ParseMatrix(Matrix(2, 1, i => 5, i => i == 0 ? 0 : 0xFFFF), 5, 0), pattern, []);
        check(unknownCell.Assess(40, 5).Verdict == NdsTileVerdict.Indeterminate && unknownCell.Assess(40, 5).Terrain.Status == PlatinumTerrainStatus.Unknown, "TerrainQuery_EmptyLandCellIsIndeterminate");
        check(query.Assess(500, 500).Placement == NdsMarkerPlacement.OutsideMatrix && query.Assess(500, 500).Verdict == NdsTileVerdict.Indeterminate, "TerrainQuery_OutsideTheMatrixIsIndeterminate");

        // ---- overlay model
        var model = new NdsMapCanvasModel(PlatinumMapLayout.ParseMatrix(Matrix(2, 1, i => 5, i => i == 0 ? 0 : 0xFFFF), 5, 0), events, pattern);
        var pixels = model.TerrainPixels()!;
        int At(int x, int y) => (y * 64 + x) * 4;
        check(pixels.Length == 64 * 32 * 4 && pixels[At(7, 2) + 3] == 0xB8 && pixels[At(7, 2) + 2] > pixels[At(7, 2)] && pixels[At(20, 20) + 3] == 0x24, "TerrainOverlay_BlockedSolidClearFaint");
        check(pixels[At(40, 4) + 3] != pixels[At(41, 4) + 3] && pixels[At(40, 4) + 3] >= 0x80, "TerrainOverlay_UnknownIsACheckerboardNotFree");
        check(new NdsMapCanvasModel(PlatinumMapLayout.ParseMatrix(Matrix(1, 1, null, _ => 0), 5, 0), []).TerrainPixels() is null, "TerrainOverlay_NoPixelsWithoutTerrainData");
        check((NdsMapLayers.All & NdsMapLayers.Terrain) != 0 && model.HasTerrain && model.Describe(7, 2).Contains("terreno: bloqueado") && model.Describe(20, 20).Contains("no confirma paso") && model.Describe(40, 4).Contains("sin datos"), "TerrainOverlay_HoverDescriptionsDistinguishTheThreeStates");
        check(!new NdsMapCanvasModel(PlatinumMapLayout.ParseMatrix(Matrix(1, 1, null, _ => 0), 5, 0), []).Describe(3, 3).Contains("terreno"), "TerrainOverlay_NoTerrainTextWithoutData");

        // ---- 30x30 and performance
        var big = PlatinumTerrain.ParseNarc(Archive(Land((x, z) => (ushort)((x + z) % 7 == 0 ? 0x8000 : 0))));
        var biggest = PlatinumMapLayout.ParseMatrix(Matrix(30, 30, i => i == 3 + 27 * 30 ? 411 : 300, _ => 0), 411, 0);
        var clock = System.Diagnostics.Stopwatch.StartNew(); var bigModel = new NdsMapCanvasModel(biggest, [], big); var bigPixels = bigModel.TerrainPixels()!; long build = clock.ElapsedMilliseconds;
        check(bigPixels.Length == 960 * 960 * 4 && build < 3000, "TerrainOverlay_960x960BitmapBuildsQuickly");
        clock.Restart(); int blockedCount = 0; for (int i = 0; i < 20000; i++) if (bigModel.Query.Assess(i % 960, i * 7 % 960).Verdict == NdsTileVerdict.Blocked) blockedCount++;
        check(clock.ElapsedMilliseconds < 3000 && blockedCount > 0, "TerrainQuery_20000AssessmentsStayFast");
        check(bigModel.Query.Assess(119, 878) is { Placement: NdsMarkerPlacement.InsideOwnedBlock } && bigModel.Query.Assess(5, 5).Placement == NdsMarkerPlacement.OutsideOwnedBlocks, "TerrainQuery_Case411KeepsOnlyBlock3_27AsOwned");
    }

    /// <summary>Read-only: alignment of the real land_data with the real events; writes only a new JSON report.</summary>
    internal static void Probe(string source, string reportPath)
    {
        string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        source = Path.GetFullPath(source); reportPath = Path.GetFullPath(reportPath);
        if (File.Exists(reportPath) || Directory.Exists(reportPath) || source.Equals(reportPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("La sonda requiere un archivo de informe nuevo, distinto de la ROM.");
        string before = Hash(source); object report;
        using (var rom = NdsRom.Open(source))
        {
            void Require(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
            var events = PlatinumEvents.Open(rom); var terrain = PlatinumTerrain.Open(rom);
            Require(terrain.Count == 666, "TerrainReal_666LandDataMembersAllStrictlyParsed");
            var matrices = Narc.Read(rom.ReadFile(PlatinumEvents.MatrixPath)); int cells = 0, empty = 0, outOfTable = 0;
            foreach (var (bytes, id) in matrices.Files.Select((b, i) => (b, i)))
                foreach (var cell in PlatinumMapLayout.ParseMatrix(bytes, 0, id).Cells) { cells++; if (cell.LandDataId == PlatinumTerrain.EmptyLandId) empty++; else if (!terrain.Contains(cell.LandDataId)) outOfTable++; }
            Require(outOfTable == 0 && cells == 1604, "TerrainReal_EveryMatrixCellResolvesOrIsExplicitlyEmpty");
            var layout = events.Layout(411); var npc = events.Npc(411, 0); var at = layout.Locate(npc.X, npc.Y);
            Require((at.Cell.BlockX, at.Cell.BlockY, at.LocalX, at.LocalY) == (3, 27, 23, 14) && at.Cell.LandDataId == 0 && layout.OwnedCells.Count == 1, "TerrainReal_Case411Npc0Block3_27Local23_14");
            var sample = terrain.Sample(layout, npc.X, npc.Y);
            Require(sample.Status == PlatinumTerrainStatus.ClearOfCollision && sample.LandId == 0, "TerrainReal_Npc0StandsOnATileWithoutTerrainCollision");
            int blockedHere = 0; for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++) if (terrain.Attribute(0, x, z) >= 0x8000) blockedHere++;
            Require(blockedHere > 100 && blockedHere < 900, "TerrainReal_Block3_27HasBothBlockedAndClearTiles");
            // Alignment statistics: NPCs should rarely stand on collision tiles with the right axis order, far more often if transposed.
            int npcs = 0, onBlocked = 0, onBlockedTransposed = 0, signs = 0, signsBlocked = 0, warps = 0, warpsBlocked = 0;
            foreach (var map in events.Maps)
            {
                var lay = events.Layout(map.Id);
                foreach (var e in events.Events(map.Id))
                {
                    if (e.X < 0 || e.Y < 0 || e.X >= lay.TileWidth || e.Y >= lay.TileHeight) continue;
                    var p = lay.Locate(e.X, e.Y); int id = p.Cell.LandDataId;
                    if (id == PlatinumTerrain.EmptyLandId || lay.HasStoredHeaders && p.Cell.HeaderId != map.Id) continue;
                    bool blocked = terrain.Attribute(id, p.LocalX, p.LocalY) >= 0x8000, transposed = terrain.Attribute(id, p.LocalY, p.LocalX) >= 0x8000;
                    if (e.Kind == PlatinumEventKind.Npc && e.Type != 3) { npcs++; if (blocked) onBlocked++; if (transposed) onBlockedTransposed++; }
                    else if (e.Kind == PlatinumEventKind.Object) { signs++; if (blocked) signsBlocked++; }
                    else if (e.Kind == PlatinumEventKind.Warp) { warps++; if (blocked) warpsBlocked++; }
                }
            }
            Require(npcs > 1000 && onBlocked * 4 < npcs && onBlockedTransposed > onBlocked * 3, "TerrainReal_AxisOrderIsSupportedByNpcPlacement");
            Require(signs > 100 && signsBlocked * 2 > signs, "TerrainReal_BackgroundObjectsMostlySitOnCollisionTiles");
            byte[] landBytes = rom.ReadFile(PlatinumTerrain.LandDataPath);
            report = new { profile = "CPUE/1", sourceSha256 = before, landDataBytes = landBytes.Length, landDataCrc32 = Gen5Data.Crc32(landBytes).ToString("X8"), members = terrain.Count,
                matrixCells = cells, emptyCells = empty, block411 = new { x = 3, y = 27, landId = 0, blockedTiles = blockedHere, totalTiles = 1024 },
                npc0 = new { npc.X, npc.Y, local = new { at.LocalX, at.LocalY }, sample.Status, sample.Attribute },
                alignment = new { npcsOnOwnedCells = npcs, npcsOnCollision = onBlocked, npcsOnCollisionIfTransposed = onBlockedTransposed, backgroundObjects = signs, backgroundObjectsOnCollision = signsBlocked, warps, warpsOnCollision = warpsBlocked },
                interpretation = "Static terrain-collision bit (bit 15 of land_data attributes). Not walkability: heights, tile behaviour, doors, scripts and NPC movement are not evaluated; building models are not read.",
                readOnly = true };
        }
        if (Hash(source) != before) throw new Exception("FAIL Source SHA256 changed");
        using var output = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("PASS TerrainReal_SourceSha256Unchanged; report created; gameplay NOT tested");
    }
}
