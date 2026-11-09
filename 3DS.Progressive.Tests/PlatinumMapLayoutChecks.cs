using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using pk3DS.Core.NDS;

internal static class PlatinumMapLayoutChecks
{
    private static byte[] Matrix(int w, int h, bool headers, bool altitudes, string prefix = "map")
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { (byte)w, (byte)h, (byte)(headers ? 1 : 0), (byte)(altitudes ? 1 : 0), (byte)prefix.Length });
        writer.Write(Encoding.ASCII.GetBytes(prefix));
        if (headers) for (int i = 0; i < w * h; i++) writer.Write((ushort)(i % 2 == 0 ? 411 : 300));
        if (altitudes) for (int i = 0; i < w * h; i++) writer.Write((byte)(200 + i));
        for (int i = 0; i < w * h; i++) writer.Write((ushort)(i == 0 ? 0 : i == 1 ? 65535 : 512 + i));
        return stream.ToArray();
    }

    private static void Reject<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); } catch (T) { check(true, name); return; }
        throw new Exception("FAIL expected " + typeof(T).Name + ": " + name);
    }

    internal static void Run(Action<bool, string> check)
    {
        foreach (bool headers in new[] { false, true })
        foreach (bool altitudes in new[] { false, true })
        {
            var layout = PlatinumMapLayout.ParseMatrix(Matrix(3, 2, headers, altitudes), 411, 7);
            string suffix = $"_Headers{headers}_Altitudes{altitudes}";
            check(layout is { Width: 3, Height: 2, TileWidth: 96, TileHeight: 64, MapId: 411, MatrixId: 7, ModelPrefix: "map" }
                && layout.HasStoredHeaders == headers && layout.HasStoredAltitudes == altitudes, "MapLayout_Sections" + suffix);
            check(layout.Cell(2, 1) is { BlockX: 2, BlockY: 1, LandDataId: 517 }
                && layout.Cell(0, 0).LandDataId == 0 && layout.Cell(1, 0).LandDataId == 65535, "MapLayout_RowMajorLittleEndianAndRawLandIds" + suffix);
            check(layout.Cell(2, 1).StoredHeaderId == (headers ? (ushort?)300 : null)
                && layout.Cell(2, 1).HeaderId == (headers ? 300 : 411)
                && layout.Cell(2, 1).RawAltitude == (altitudes ? (byte?)205 : null), "MapLayout_StoredVersusContextualValues" + suffix);
            check(layout.OwnedCells.Select(c => (c.BlockX, c.BlockY)).SequenceEqual(headers
                ? new[] { (0, 0), (2, 0), (1, 1) } : new[] { (0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1) }), "MapLayout_DisconnectedOwnership" + suffix);
        }
        var bytes = Matrix(3, 2, true, true); var before = (byte[])bytes.Clone();
        var data = PlatinumMapLayout.ParseMatrix(bytes, 411, 0);
        check(bytes.SequenceEqual(before), "MapLayout_ParseLeavesInputBytesUnchanged");
        Array.Fill(bytes, (byte)0);
        check(data.Cell(2, 1).LandDataId == 517 && data.ModelPrefix == "map", "MapLayout_InputMutationCannotChangeLayout");
        Reject<NotSupportedException>(() => ((IList<PlatinumMapCell>)data.Cells)[0] = data.Cell(2, 1), check, "MapLayout_CellsAreReadOnly");
        Reject<NotSupportedException>(() => ((IList<PlatinumMapCell>)data.OwnedCells).Clear(), check, "MapLayout_OwnershipIsReadOnly");
        check(!PlatinumMapLayout.ParseMatrix(Matrix(1, 1, true, false), 300, 0).OwnedCells.Any(), "MapLayout_NoOwnedCellsIsNotInvented");
        var contextual = Matrix(1, 1, false, false);
        check(PlatinumMapLayout.ParseMatrix(contextual, 411, 7).Cell(0, 0).HeaderId == 411
            && PlatinumMapLayout.ParseMatrix(contextual, 592, 7).Cell(0, 0).HeaderId == 592,
            "MapLayout_MissingHeadersFollowSelectedMapContextNotMatrixId");
        var unknownHeader = Matrix(1, 1, true, false);
        BinaryPrimitives.WriteUInt16LittleEndian(unknownHeader.AsSpan(8), ushort.MaxValue);
        var unknownLayout = PlatinumMapLayout.ParseMatrix(unknownHeader, 411, 0);
        check(unknownLayout.Cell(0, 0).StoredHeaderId == ushort.MaxValue && unknownLayout.OwnedCells.Count == 0,
            "MapLayout_UnknownRawHeaderIsPreservedWithoutInventingOwnership");
        check(data.Locate(32, 0).Cell.HeaderId == 300, "MapLayout_OffHeaderPositionRemainsInspectable");
        foreach (var (x, y, bx, by, lx, ly) in new[] { (0, 0, 0, 0, 0, 0), (31, 31, 0, 0, 31, 31), (32, 32, 1, 1, 0, 0), (95, 63, 2, 1, 31, 31) })
        {
            var position = data.Locate(x, y);
            check((position.Cell.BlockX, position.Cell.BlockY, position.LocalX, position.LocalY) == (bx, by, lx, ly)
                && data.ToTiles(bx, by, lx, ly) == (x, y), $"MapLayout_TileBoundaryRoundtrip_{x}_{y}");
        }
        check(Enumerable.Range(0, 96).All(x => Enumerable.Range(0, 64).All(y =>
        {
            var p = data.Locate(x, y); return data.ToTiles(p.Cell.BlockX, p.Cell.BlockY, p.LocalX, p.LocalY) == (x, y);
        })), "MapLayout_AllNonSquareTilesRoundtrip");
        var large = PlatinumMapLayout.ParseMatrix(Matrix(30, 30, false, false, ""), 411, 0);
        var npc = large.Locate(119, 878);
        check((npc.Cell.BlockX, npc.Cell.BlockY, npc.LocalX, npc.LocalY) == (3, 27, 23, 14)
            && large.ToTiles(3, 27, 23, 14) == (119, 878), "MapLayout_TwinleafCoordinateHypothesisArithmeticOnly");
        check(large.Cell(29, 29).BlockY == 29 && large.Locate(959, 959).LocalY == 31, "MapLayout_MaximumMatrixAccepted");
        foreach (var (x, y) in new[] { (-1, 0), (0, -1), (96, 0), (0, 64), (int.MaxValue, 0), (int.MinValue, 0) })
            Reject<ArgumentOutOfRangeException>(() => data.Locate(x, y), check, $"MapLayout_RejectTile_{x}_{y}");
        foreach (var (x, y) in new[] { (-1, 0), (0, -1), (3, 0), (0, 2) })
            Reject<ArgumentOutOfRangeException>(() => data.Cell(x, y), check, $"MapLayout_RejectBlock_{x}_{y}");
        foreach (var (x, y) in new[] { (-1, 0), (0, -1), (32, 0), (0, 32) })
            Reject<ArgumentOutOfRangeException>(() => data.ToTiles(0, 0, x, y), check, $"MapLayout_RejectLocal_{x}_{y}");
        Reject<ArgumentOutOfRangeException>(() => data.ToTiles(3, 0, 0, 0), check, "MapLayout_InverseRejectsBlockOutsideMatrix");
        foreach (var (offset, value, name) in new[] { (0, 0, "ZeroWidth"), (1, 0, "ZeroHeight"), (0, 31, "Width31"), (1, 31, "Height31"),
            (2, 2, "HeaderFlag"), (3, 2, "AltitudeFlag"), (4, 17, "Prefix17"), (5, 0, "NulPrefix"), (5, 128, "NonAsciiPrefix") })
        {
            var bad = (byte[])before.Clone(); bad[offset] = (byte)value;
            Reject<InvalidDataException>(() => PlatinumMapLayout.ParseMatrix(bad, 411, 0), check, "MapLayout_Reject" + name);
        }
        foreach (int length in new[] { 0, 4, 7, 19, 25, before.Length - 1 })
            Reject<InvalidDataException>(() => PlatinumMapLayout.ParseMatrix(before[..length], 411, 0), check, "MapLayout_RejectTruncated_" + length);
        Reject<InvalidDataException>(() => PlatinumMapLayout.ParseMatrix([.. before, 0], 411, 0), check, "MapLayout_RejectTrailingBytes");
        check(PlatinumMapLayout.ParseMatrix(Matrix(1, 1, false, false, "abcdefghijklmnop"), 0, 0).ModelPrefix.Length == 16, "MapLayout_MaximumPrefixAccepted");
        foreach (int id in new[] { -1, 593 })
            Reject<ArgumentOutOfRangeException>(() => PlatinumMapLayout.ParseMatrix(before, id, 0), check, "MapLayout_RejectMap_" + id);
        Reject<ArgumentOutOfRangeException>(() => PlatinumMapLayout.ParseMatrix(before, 411, -1), check, "MapLayout_RejectNegativeMatrix");
        string folder = Path.Combine(Path.GetTempPath(), "map-layout-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var (code, version, unit, name) in new[] { ("CPUE", (byte)0, (byte)0, "Revision"), ("IPKE", (byte)1, (byte)0, "Game"),
                ("CPUE", (byte)1, (byte)2, "Dsi"), ("CPUE", (byte)1, (byte)0, "UnaccreditedTables") })
            {
                byte[] romBytes = NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]>
                { [PlatinumEvents.NamesPath] = [0], [PlatinumEvents.MatrixPath] = [0], [PlatinumEvents.ScriptPath] = [0] }, code, version);
                romBytes[0x12] = unit;
                BinaryPrimitives.WriteUInt16LittleEndian(romBytes.AsSpan(0x15E), NdsRom.Crc16(romBytes.AsSpan(0, 0x15E)));
                string path = Path.Combine(folder, name + ".nds"); File.WriteAllBytes(path, romBytes);
                using var rom = NdsRom.Open(path);
                Reject<InvalidDataException>(() => PlatinumMapLayout.Open(rom, 411), check, "MapLayout_ProfileRejects" + name);
                check(File.ReadAllBytes(path).SequenceEqual(romBytes), "MapLayout_ProfileRejectionLeavesSourceUnchanged_" + name);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    internal static void Probe(string source, string reportPath)
    {
        // No copying, editing, exporting, session creation or checkpoint access. Only the new JSON report is written.
        string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        source = Path.GetFullPath(source); reportPath = Path.GetFullPath(reportPath);
        if (File.Exists(reportPath) || Directory.Exists(reportPath) || source.Equals(reportPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("La sonda requiere un archivo de informe nuevo, distinto de la ROM.");
        string before = Hash(source);
        object report;
        using (var rom = NdsRom.Open(source))
        {
            var events = PlatinumEvents.Open(rom); byte[] eventBytes = rom.ReadFile(PlatinumEvents.EventPath);
            var matrices = Narc.Read(rom.ReadFile(PlatinumEvents.MatrixPath));
            var layout = PlatinumMapLayout.Open(rom, 411); var map = events.Map(411); var npc = events.Npc(411, 0);
            void Require(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
            Require(map.InternalName == "T01" && map.Matrix == 0 && layout.Width == 30 && layout.Height == 30, "MapLayoutReal_T01Shared30x30");
            Require(npc.X == 119 && npc.Y == 878, "MapLayoutReal_Npc0StoredCoordinates");
            var position = layout.Locate(npc.X, npc.Y);
            Require((position.Cell.BlockX, position.Cell.BlockY, position.LocalX, position.LocalY) == (3, 27, 23, 14)
                && layout.ToTiles(3, 27, 23, 14) == (119, 878), "MapLayoutReal_Npc0TileRoundtrip");
            Require(position.Cell.StoredHeaderId == 411 && layout.OwnedCells.Count == 1 && layout.OwnedCells[0] == position.Cell,
                "MapLayoutReal_Npc0ExplicitUniqueHeader411Cell");
            Require(position.Cell.LandDataId == 0 && position.Cell.RawAltitude == 0, "MapLayoutReal_ZeroIsValidRawLandAndAltitude");
            var allMatrices = matrices.Files.Select((bytes, id) => PlatinumMapLayout.ParseMatrix(bytes, 411, id)).ToArray();
            Require(allMatrices.Length == 289, "MapLayoutReal_All289MatrixMembersParsed");
            var contexts = events.Maps.Select(m => PlatinumMapLayout.ParseMatrix(matrices.Files[m.Matrix], m.Id, m.Matrix)).ToArray();
            Require(contexts.Length == 593 && contexts.All(l => l.TileWidth == events.Map(l.MapId).Width && l.TileHeight == events.Map(l.MapId).Height),
                "MapLayoutReal_All593HeaderDimensionsAgreeWithEventReader");
            Require(eventBytes.SequenceEqual(events.Bytes()) && eventBytes.SequenceEqual(rom.ReadFile(PlatinumEvents.EventPath)),
                "MapLayoutReal_EventArchiveByteExactAfterReads");
            report = new { profile = "CPUE/1", sourceSha256 = before, sourceBytes = rom.Length,
                map = new { map.Id, map.InternalName, map.EventFile, map.Matrix }, npc = new { npc.Index, npc.X, npc.Y, rawZ = npc.Z },
                layout.Width, layout.Height, layout.ModelPrefix, layout.HasStoredHeaders, layout.HasStoredAltitudes,
                position, ownedCells = layout.OwnedCells, matrixMembers = allMatrices.Length, headerContexts = contexts.Length,
                storedHeaderMatrices = allMatrices.Count(l => l.HasStoredHeaders), storedAltitudeMatrices = allMatrices.Count(l => l.HasStoredAltitudes),
                eventArchiveSha256 = Convert.ToHexString(SHA256.HashData(eventBytes)),
                interpretation = "Confirmed static matrix/header association; no geometry, collisions, runtime state or gameplay validation.",
                readOnly = true };
        }
        if (Hash(source) != before) throw new Exception("FAIL Source SHA256 changed");
        using var output = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("PASS MapLayoutReal_SourceSha256Unchanged; report created; gameplay NOT tested");
    }
}
