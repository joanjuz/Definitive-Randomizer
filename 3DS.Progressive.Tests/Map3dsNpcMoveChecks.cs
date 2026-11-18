using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive.Services;

/// <summary>Real persistence of a Gen6 NPC move on a working copy: commit, reopen, other zones untouched, undo.</summary>
internal static class Map3dsNpcMoveChecks
{
    internal static void Probe(string copyRoot, int zone, int npc, int x, int y)
    {
        string full = Path.GetFullPath(copyRoot);
        if (!full.Contains("PruebasMultiGen", StringComparison.OrdinalIgnoreCase) && !full.Contains("Pruebas", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Por seguridad, la prueba solo escribe en una copia dentro de una carpeta de pruebas.");
        var session = GameProjectSession.Open(full, false);
        var service = new Map3dsNpcMoveService(session);
        string archive = service.ZoneArchive;
        byte[] originalArchive = File.ReadAllBytes(archive);
        string originalHash = Convert.ToHexString(SHA256.HashData(originalArchive));
        var before = Gen6MapBackend.FromConfig(session.GameConfiguration);
        var npcBefore = before.ZoneEvents(zone).First(e => e.Kind == MapEventKind.Npc && e.Index == npc);
        var others = Enumerable.Range(0, new GARC.MemGARC(originalArchive).FileCount).Where(i => i != zone).ToArray();
        Console.WriteLine($"Zona {zone}, NPC {npc}: ({npcBefore.X}, {npcBefore.Y}) → ({x}, {y}) · archivo {Path.GetFileName(Path.GetDirectoryName(archive))}/{Path.GetFileName(archive)} SHA256 {originalHash[..16]}");

        var proposal = service.Preview(before, zone, npc, x, y);
        bool unchangedByPreview = File.ReadAllBytes(archive).AsSpan().SequenceEqual(originalArchive);
        Console.WriteLine((unchangedByPreview ? "PASS" : "FAIL") + " NpcMoveReal_PreviewWritesNothing");
        service.Apply(proposal);

        var after = Gen6MapBackend.FromConfig(session.GameConfiguration);
        var npcAfter = after.ZoneEvents(zone).First(e => e.Kind == MapEventKind.Npc && e.Index == npc);
        bool moved = npcAfter.X == x && npcAfter.Y == y;
        var beforeEvents = before.ZoneEvents(zone).Where(e => !(e.Kind == MapEventKind.Npc && e.Index == npc)).Select(e => (e.Kind, e.Index, e.X, e.Y)).ToArray();
        var afterEvents = after.ZoneEvents(zone).Where(e => !(e.Kind == MapEventKind.Npc && e.Index == npc)).Select(e => (e.Kind, e.Index, e.X, e.Y)).ToArray();
        bool restSame = beforeEvents.SequenceEqual(afterEvents);
        var diff = Enumerable.Range(0, before.ZoneBytes(zone).Length).Where(i => before.ZoneBytes(zone)[i] != after.ZoneBytes(zone)[i]).ToArray();
        bool onlyPosition = diff.Length is > 0 and <= 4;
        bool otherZones = others.All(i => !before.Zones.Any(z => z.Id == i) || before.ZoneBytes(i).AsSpan().SequenceEqual(after.ZoneBytes(i)));
        Console.WriteLine((moved ? "PASS" : "FAIL") + $" NpcMoveReal_ReopenedFromDiskAtTheNewCell ({npcAfter.X}, {npcAfter.Y})");
        Console.WriteLine((restSame && onlyPosition ? "PASS" : "FAIL") + $" NpcMoveReal_OnlyTheNpcPositionChanged ({diff.Length} bytes de la zona)");
        Console.WriteLine((otherZones ? "PASS" : "FAIL") + " NpcMoveReal_OtherZonesByteIdentical");
        var entry = session.History.Read().Entries.Last();
        bool recorded = entry.Module == Map3dsNpcMoveService.Module && entry.Parameters.GetValueOrDefault("ActionId") == Map3dsNpcMoveService.ActionId;
        Console.WriteLine((recorded ? "PASS" : "FAIL") + $" NpcMoveReal_HistoryEntry «{entry.Action}»");

        RestorePoints.Undo(session.History, session.ProjectRoot, entry.Id);
        bool restored = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))) == originalHash;
        Console.WriteLine((restored ? "PASS" : "FAIL") + " NpcMoveReal_UndoRestoresTheOriginalArchive");
        if (!(unchangedByPreview && moved && restSame && onlyPosition && otherZones && recorded && restored)) throw new InvalidDataException("La prueba real de movimiento de NPC falló.");

        // Leave the copy with the move applied so it can be rebuilt and tried in the game.
        bool stale = false;
        try { service.Preview(Gen6MapBackend.FromConfig(session.GameConfiguration), zone, npc, x, y); } catch (IOException) { stale = true; }
        Console.WriteLine((stale ? "PASS" : "FAIL") + " NpcMoveReal_ChangesOutsideTheEditorRequireAReload");
        service.Refresh();
        var again = service.Preview(Gen6MapBackend.FromConfig(session.GameConfiguration), zone, npc, x, y);
        service.Apply(again);
        Console.WriteLine($"Movimiento aplicado de nuevo en la copia para la prueba jugable · gameplay NOT tested");
    }

    /// <summary>Rebuilds the working copy into a 3DS image, re-extracts it and checks the NPC is still on its new cell.</summary>
    internal static void Rebuild(string copyRoot, string image, string reextract, int zone, int npc, int x, int y)
    {
        var project = GameProjectSession.Open(copyRoot, false);
        var built = new GameRomRebuildService(project).BuildAsync(RomRebuildKind.Full3DS, image).GetAwaiter().GetResult();
        Console.WriteLine($"Imagen: {built.File}, {built.Bytes} bytes, SHA256 {built.Hash}");
        RealRebuildChecks.VerifyExisting(copyRoot, image, reextract);
        var reopened = GameProjectSession.Open(reextract, false);
        var npcAfter = Gen6MapBackend.FromConfig(reopened.GameConfiguration).ZoneEvents(zone).First(e => e.Kind == MapEventKind.Npc && e.Index == npc);
        bool ok = npcAfter.X == x && npcAfter.Y == y;
        Console.WriteLine((ok ? "PASS" : "FAIL") + $" NpcMoveRebuild_ReextractedImageKeepsTheMove ({npcAfter.X}, {npcAfter.Y}); gameplay NOT tested");
        if (!ok) throw new InvalidDataException("La imagen reconstruida no conserva el movimiento.");
    }
}
