using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Ctr;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive.Services;

/// <summary>
/// Visible encounter grass in Ultra Sun / Ultra Moon: the "Map07_encgrass01" model of part 1 of a map block. The moving
/// of real grass models is checked on every block of a real copy (<c>--map3ds-gen7-grass-verify</c>); these checks
/// cover the block pack, the section reader and the refusals without game data.
/// </summary>
internal static class Map3dsGen7VisibleGrassChecks
{
    /// <summary>A block pack with 8 parts, data from 0x80; part 1 is <paramref name="grass"/>.</summary>
    internal static byte[] Block(byte[] grass, int partZero = 0x100) =>
        Gen7VisibleGrass.PackBlock([new byte[partZero], grass, [], [], [], [], new byte[0x80], new byte[0x80]]);

    /// <summary>A model blob with named sections (header, then {name[8], u32 length, u32 -1, data}) padded to 0x80.</summary>
    internal static byte[] Model(params (string Name, byte[] Data)[] sections)
    {
        var body = new List<byte>();
        foreach (var (name, data) in sections)
        {
            var head = new byte[16];
            Encoding.ASCII.GetBytes(name).CopyTo(head, 0);
            BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(8), data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(12), -1);
            body.AddRange(head); body.AddRange(data);
        }
        int length = 16 + body.Count;
        var output = new byte[(length + 0x7F) / 0x80 * 0x80];
        BinaryPrimitives.WriteUInt32LittleEndian(output, Gfl2File.ModelMagic);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), sections.Length);
        body.ToArray().CopyTo(output, 16);
        return output;
    }

    internal static void Run(Action<bool, string> check)
    {
        var name = Encoding.ASCII.GetBytes(Gen7VisibleGrass.ModelName + "GREF");
        var grassLike = Model(("gfmodel", [.. name, 0, 0, 0, 0]), ("material", new byte[8]), ("mesh", new byte[8]));
        var block = Block([]);
        check(Gen7VisibleGrass.BlockParts(block).Length == 8 && Gen7VisibleGrass.BlockParts(block)[1].Length == 0 && BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(4)) == 0x80,
            "Gen7Grass_BlockPacksRebuildByteForByteFromTheirParts");
        bool misaligned = false, wrongCount = false, notExact = false;
        try { Gen7VisibleGrass.PackBlock([new byte[0x40], [], [], [], [], [], [], []]); } catch (ArgumentException) { misaligned = true; }
        try { Gen7VisibleGrass.BlockParts(Gen7CatchZones.Pack("BG", [new byte[0x80], []], 0x80)); } catch (MapFormatException) { wrongCount = true; }
        var shifted = Gen7CatchZones.Pack("BG", [new byte[0x80], [], [], [], [], [], [], []], 0x40);
        try { Gen7VisibleGrass.BlockParts(shifted); } catch (MapFormatException) { notExact = true; }
        check(misaligned && wrongCount && notExact, "Gen7Grass_OtherBlockLayoutsAreRefused");
        var sections = Gen7VisibleGrass.Sections(grassLike);
        check(sections.Select(s => s.Name).SequenceEqual(["gfmodel", "material", "mesh"]) && sections[0].Offset == 16 && Gen7VisibleGrass.MeshCount(grassLike) == 1
            && Gen7VisibleGrass.IsGrassModel(grassLike) && !Gen7VisibleGrass.IsGrassModel(Model(("gfmodel", new byte[8]))) && !Gen7VisibleGrass.IsGrassModel(new byte[0x80]),
            "Gen7Grass_SectionsAndTheGrassModelAreRecognised");
        var trailing = (byte[])grassLike.Clone(); trailing[^1] = 1;
        bool trailingRefused = false, truncatedRefused = false;
        try { Gen7VisibleGrass.Sections(trailing); } catch (MapFormatException) { trailingRefused = true; }
        var truncated = (byte[])grassLike.Clone(); BinaryPrimitives.WriteInt32LittleEndian(truncated.AsSpan(16 + 8), 0x10000);
        try { Gen7VisibleGrass.Sections(truncated); } catch (MapFormatException) { truncatedRefused = true; }
        check(trailingRefused && truncatedRefused, "Gen7Grass_ModelsWithDataAfterTheirSectionsOrOutOfBoundsAreRefused");
        bool hasGrass = false, multi = false;
        try { Gen7VisibleGrass.AddGrass(Block(grassLike), Block(grassLike), Vector3.One); } catch (InvalidOperationException) { hasGrass = true; }
        var twoPatches = Model(("gfmodel", [.. name, 0, 0, 0, 0]), ("material", new byte[8]), ("mesh", new byte[8]), ("mesh", new byte[8]));
        try { Gen7VisibleGrass.AddGrass(Block([]), Block(twoPatches), Vector3.One); } catch (InvalidOperationException) { multi = true; }
        check(hasGrass && multi, "Gen7Grass_OnlyBlocksWithoutGrassAndSinglePatchTemplates");
    }

    /// <summary>
    /// Real persistence on an Ultra Sun / Ultra Moon copy in a test folder: the template's grass patch centred on a cell
    /// (preview, commit, reopen, only the target block member changed, history undo byte for byte, stale editor), then
    /// reapplied for the gameplay test. With <paramref name="soil"/> (hex, or "-"), also a catch zone over the cells under
    /// the grass with the table <paramref name="source"/>; a soil that no vanilla region uses alone is written only as
    /// an experiment (marked in the history).
    /// </summary>
    internal static void Probe(string copyRoot, int area, int x, int y, int template, string soil, int source)
    {
        string full = Path.GetFullPath(copyRoot);
        if (!full.Contains("Pruebas", StringComparison.OrdinalIgnoreCase)) throw new IOException("Por seguridad, la prueba solo escribe en una copia dentro de una carpeta de pruebas.");
        var session = GameProjectSession.Open(full, false);
        var service = new Map3dsGen7VisibleGrassService(session);
        string archive = service.BlockArchive;
        byte[] original = File.ReadAllBytes(archive);
        var before = Gen7MapBackend.FromConfig(session.GameConfiguration);
        var (plan, reason) = before.PlanGrass(area, (x, y), template);
        if (plan is null) throw new InvalidOperationException(reason);
        var t = before.GrassTemplates.First(g => g.Block == template);
        Console.WriteLine($"Área {area} ({before.Zones.First(z => z.Id == area).Name}): bloque {plan.TargetBlock} en ({plan.Col},{plan.Row}); plantilla {template} ({t.Size.X:0}×{t.Size.Z:0}, {string.Join("/", t.Textures)}); desplazamiento {plan.Delta.X:0.#},{plan.Delta.Y:0.#},{plan.Delta.Z:0.#}; {plan.Covered.Count} casillas en X {plan.Covered.Min(c => c.X)}–{plan.Covered.Max(c => c.X)} Y {plan.Covered.Min(c => c.Y)}–{plan.Covered.Max(c => c.Y)}; desnivel {plan.GroundSpread:0.#}; también lo cargan: {string.Join(", ", plan.Sharing)}");
        var proposal = service.Preview(before, area, (x, y), template);
        bool previewSafe = File.ReadAllBytes(archive).AsSpan().SequenceEqual(original);
        Console.WriteLine((previewSafe ? "PASS" : "FAIL") + " Gen7GrassReal_PreviewWritesNothing");
        service.Apply(proposal);
        var after = Gen7MapBackend.FromConfig(session.GameConfiguration);
        var parts = Gen7VisibleGrass.BlockParts(after.BlockBytes(plan.TargetBlock));
        var oldParts = Gen7VisibleGrass.BlockParts(before.BlockBytes(plan.TargetBlock));
        var (min, max) = Gen7VisibleGrass.Bounds(parts[1]);
        bool placed = Gen7VisibleGrass.IsGrassModel(parts[1]) && Gen7VisibleGrass.MeshCount(parts[1]) == 1 && Vector3.Distance(min, t.Min + plan.Delta) < 0.05f && Vector3.Distance(max, t.Max + plan.Delta) < 0.05f;
        Console.WriteLine((placed ? "PASS" : "FAIL") + $" Gen7GrassReal_BlockHasTheMovedPatch (X {min.X:0}–{max.X:0}, Y {min.Y:0}–{max.Y:0}, Z {min.Z:0}–{max.Z:0})");
        bool restOfBlock = Enumerable.Range(0, 8).Where(i => i != 1).All(i => parts[i].AsSpan().SequenceEqual(oldParts[i]));
        Console.WriteLine((restOfBlock ? "PASS" : "FAIL") + " Gen7GrassReal_OtherPartsOfTheBlockUnchanged");
        var mb = new GARC.MemGARC(original).Files; var ma = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        var differing = Enumerable.Range(0, Math.Min(ma.Length, mb.Length)).Where(i => !ma[i].AsSpan().SequenceEqual(mb[i])).ToArray();
        bool others = ma.Length == mb.Length && differing.SequenceEqual([plan.TargetBlock]);
        Console.WriteLine((others ? "PASS" : "FAIL") + $" Gen7GrassReal_OnlyTheTargetBlockChanged (miembros {string.Join(",", differing)})");
        var entry = session.History.Read().Entries.Last();
        bool recorded = entry.Parameters.GetValueOrDefault("ActionId") == Map3dsGen7VisibleGrassService.ActionId && RestorePoints.LatestUndoable(session.History, full)?.Id == entry.Id;
        Console.WriteLine((recorded ? "PASS" : "FAIL") + $" Gen7GrassReal_HistoryEntryWithRestorePoint «{entry.Action}»");
        RestorePoints.Undo(session.History, full, entry.Id);
        bool undone = File.ReadAllBytes(archive).AsSpan().SequenceEqual(original);
        Console.WriteLine((undone ? "PASS" : "FAIL") + " Gen7GrassReal_HistoryUndoRestoresTheArchiveByteForByte");
        service.Apply(service.Preview(Gen7MapBackend.FromConfig(session.GameConfiguration), area, (x, y), template));
        bool stale = false;
        try { service.Preview(after, area, (x, y), template); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { stale = true; }
        Console.WriteLine((stale ? "PASS" : "FAIL") + " Gen7GrassReal_AnEditorThatReadOldFilesCannotWrite");
        bool zone = true;
        if (soil != "-")
        {
            uint attribute = Convert.ToUInt32(soil, 16);
            var catchService = new Map3dsGen7CatchZoneService(session);
            var b = Gen7MapBackend.FromConfig(session.GameConfiguration);
            uint? experimental = Gen7CatchZones.IsWritable(attribute) ? null : attribute;
            var cells = plan.Covered.Where(c => b.CellsWithAttribute(area, attribute).Contains(c)).ToArray();
            Console.WriteLine($"Zona de captura sobre {cells.Length} de {plan.Covered.Count} casillas con suelo 0x{attribute:X2}{(experimental is null ? "" : " (EXPERIMENTAL: ninguna región original usa este suelo solo)")}: " + catchService.Preview(b, area, cells, source, experimental).Changes.Count + " cambios");
            catchService.Apply(catchService.Preview(b, area, cells, source, experimental));
            var z = Gen7MapBackend.FromConfig(session.GameConfiguration);
            var enc = z.EncounterCells(area).ToHashSet();
            zone = cells.All(enc.Contains) && catchService.CanRestore(area);
            Console.WriteLine((zone ? "PASS" : "FAIL") + $" Gen7GrassReal_CatchZoneUnderTheGrass ({cells.Count(enc.Contains)} casillas con encuentros; restaurable {catchService.CanRestore(area)})");
        }
        if (!(previewSafe && placed && restOfBlock && others && recorded && undone && stale && zone)) throw new InvalidDataException("La prueba real de hierba visible de USUM falló.");
        Console.WriteLine("Hierba visible aplicada en la copia para la prueba jugable · gameplay NOT tested");
    }
}
