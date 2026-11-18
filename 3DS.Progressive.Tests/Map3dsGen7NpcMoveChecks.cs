using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive.Services;

/// <summary>Real persistence of an Ultra Sun/Ultra Moon NPC move on a working copy: commit, reopen, other data untouched, undo, rebuild.</summary>
internal static class Map3dsGen7NpcMoveChecks
{
    /// <summary>First neighbouring cell (up to 3 cells away) where the move is allowed.</summary>
    private static (int X, int Y)? FindCell(Gen7MapBackend backend, int area, MapEventView npc)
    {
        for (int r = 1; r <= 3; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = (int)npc.X + dx, y = (int)npc.Y + dy;
                    if (backend.ValidateMove(area, MapEventKind.Npc, npc.Index, x, y).Allowed) return (x, y);
                }
        return null;
    }

    internal static void Probe(string copyRoot, int area, int npcNumber)
    {
        string full = Path.GetFullPath(copyRoot);
        if (!full.Contains("Pruebas", StringComparison.OrdinalIgnoreCase)) throw new IOException("Por seguridad, la prueba solo escribe en una copia dentro de una carpeta de pruebas.");
        var session = GameProjectSession.Open(full, false);
        var service = new Map3dsGen7NpcMoveService(session);
        string archive = service.FieldArchive;
        byte[] original = File.ReadAllBytes(archive);
        string originalHash = Convert.ToHexString(SHA256.HashData(original));
        var before = Gen7MapBackend.FromConfig(session.GameConfiguration);
        var npcs = before.ZoneEvents(area).Where(e => e.Kind == MapEventKind.Npc && before.EventRef(area, e.Index)?.Pack == Gen7Events.NpcPack).ToArray();
        var npc = npcs[npcNumber];
        var cell = FindCell(before, area, npc) ?? throw new InvalidDataException("No hay una casilla vecina válida para ese NPC.");
        var refBefore = before.EventRef(area, npc.Index)!.Value;
        Console.WriteLine($"Área {area} ({before.Zones.First(z => z.Id == area).Name}): {npc.Label} en casilla ({npc.X}, {npc.Y}) = mundo ({refBefore.Position.X:0.#}, {refBefore.Position.Z:0.#}, altura {refBefore.Position.Y:0.#}) → casilla ({cell.X}, {cell.Y})");

        var proposal = service.Preview(before, area, npc.Index, cell.X, cell.Y);
        bool previewSafe = File.ReadAllBytes(archive).AsSpan().SequenceEqual(original);
        Console.WriteLine((previewSafe ? "PASS" : "FAIL") + " Gen7NpcMoveReal_PreviewWritesNothing");
        service.Apply(proposal);

        var after = Gen7MapBackend.FromConfig(session.GameConfiguration);
        var refAfter = after.EventRef(area, npc.Index)!.Value;
        var target = before.MoveTarget(area, npc.Index, cell.X, cell.Y);
        bool moved = refAfter.Position == target && refAfter.Zone == refBefore.Zone && refAfter.Row == refBefore.Row;
        Console.WriteLine((moved ? "PASS" : "FAIL") + $" Gen7NpcMoveReal_ReopenedAtTheNewPosition ({refAfter.Position.X:0.#}, {refAfter.Position.Z:0.#}, altura {refAfter.Position.Y:0.#})");
        var others = before.ZoneEvents(area).Where(e => e.Index != npc.Index).Select(e => (e.Kind, e.X, e.Y, e.Label)).ToArray();
        var othersAfter = after.ZoneEvents(area).Where(e => e.Index != npc.Index).Select(e => (e.Kind, e.X, e.Y, e.Label)).ToArray();
        bool restSame = others.SequenceEqual(othersAfter);
        Console.WriteLine((restSame ? "PASS" : "FAIL") + " Gen7NpcMoveReal_OtherEventsOfTheAreaUnchanged");
        var membersBefore = new GARC.MemGARC(original).Files;
        var membersAfter = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        int member = area * Gen7MapBackend.SlotsPerWorld;
        bool otherMembers = membersAfter.Length == membersBefore.Length && Enumerable.Range(0, membersAfter.Length).All(i => i == member || membersAfter[i].AsSpan().SequenceEqual(membersBefore[i]));
        Console.WriteLine((otherMembers ? "PASS" : "FAIL") + " Gen7NpcMoveReal_OtherFieldMembersByteIdentical");
        var edBefore = before.EventBytes(area); var edAfter = after.EventBytes(area);
        int changedBytes = Enumerable.Range(0, edBefore.Length).Count(i => edBefore[i] != edAfter[i]);
        Console.WriteLine((edBefore.Length == edAfter.Length && changedBytes > 0 ? "PASS" : "FAIL") + $" Gen7NpcMoveReal_SameSizeEventPack ({changedBytes} bytes cambiados: posición y formas)");
        var entry = session.History.Read().Entries.Last();
        bool recorded = entry.Module == Map3dsGen7NpcMoveService.Module && entry.Parameters.GetValueOrDefault("ActionId") == Map3dsGen7NpcMoveService.ActionId;
        Console.WriteLine((recorded ? "PASS" : "FAIL") + $" Gen7NpcMoveReal_HistoryEntry «{entry.Action}»");

        bool canRestore = service.CanRestore(area);
        service.Apply(service.PreviewRestore(area));
        var restoredMembers = new GARC.MemGARC(File.ReadAllBytes(archive)).Files;
        bool restored = canRestore && restoredMembers.Length == membersBefore.Length && Enumerable.Range(0, restoredMembers.Length).All(i => restoredMembers[i].AsSpan().SequenceEqual(membersBefore[i]));
        bool sameFile = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))) == originalHash;
        Console.WriteLine((restored ? "PASS" : "FAIL") + $" Gen7NpcMoveReal_RestorePutsBackEveryMemberByteForByte (archivo completo idéntico: {(sameFile ? "sí" : "no, solo cambia el empaquetado del GARC")})");
        var lastEntry = session.History.Read().Entries.Last();
        bool restoreRecorded = lastEntry.Parameters.GetValueOrDefault("ActionId") == Map3dsGen7NpcMoveService.ActionId + ".restore";
        Console.WriteLine((restoreRecorded ? "PASS" : "FAIL") + $" Gen7NpcMoveReal_RestoreIsAHistoryEntry «{lastEntry.Action}»");
        restored &= restoreRecorded;
        // A change made outside this service (here: another service instance writing) must stop this one.
        var other = new Map3dsGen7NpcMoveService(session);
        other.Apply(other.Preview(Gen7MapBackend.FromConfig(session.GameConfiguration), area, npc.Index, cell.X, cell.Y));
        bool stale = false;
        try { service.Preview(Gen7MapBackend.FromConfig(session.GameConfiguration), area, npc.Index, cell.X, cell.Y); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { stale = true; }
        other.Apply(other.PreviewRestore(area));
        Console.WriteLine((stale ? "PASS" : "FAIL") + " Gen7NpcMoveReal_ChangesOutsideTheEditorRequireAReload");
        if (!(previewSafe && moved && restSame && otherMembers && changedBytes > 0 && recorded && restored && stale)) throw new InvalidDataException("La prueba real de movimiento Gen7 falló.");

        service.Refresh();
        service.Apply(service.Preview(Gen7MapBackend.FromConfig(session.GameConfiguration), area, npc.Index, cell.X, cell.Y));
        Console.WriteLine($"Movimiento aplicado de nuevo en la copia para la prueba jugable (área {area}, {npc.Label}, casilla {cell.X},{cell.Y}) · gameplay NOT tested");
    }

    internal static void Rebuild(string copyRoot, string image, string reextract, int area, int npcNumber, int x, int y)
    {
        var project = GameProjectSession.Open(copyRoot, false);
        var built = new GameRomRebuildService(project).BuildAsync(RomRebuildKind.Full3DS, image).GetAwaiter().GetResult();
        Console.WriteLine($"Imagen: {built.File}, {built.Bytes} bytes, SHA256 {built.Hash}");
        RealRebuildChecks.VerifyExisting(copyRoot, image, reextract);
        var reopened = Gen7MapBackend.FromConfig(GameProjectSession.Open(reextract, false).GameConfiguration);
        var npc = reopened.ZoneEvents(area).Where(e => e.Kind == MapEventKind.Npc && reopened.EventRef(area, e.Index)?.Pack == Gen7Events.NpcPack).ToArray()[npcNumber];
        bool ok = (int)npc.X == x && (int)npc.Y == y;
        Console.WriteLine((ok ? "PASS" : "FAIL") + $" Gen7NpcMoveRebuild_ReextractedImageKeepsTheMove (casilla {npc.X}, {npc.Y}); gameplay NOT tested");
        if (!ok) throw new InvalidDataException("La imagen reconstruida no conserva el movimiento.");
    }
}
