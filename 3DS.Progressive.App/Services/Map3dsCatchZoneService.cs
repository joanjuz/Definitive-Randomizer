using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Turns plain-ground cells of an X/Y zone into cells with wild encounters through the project history:
/// the map blocks (mapGR) get the encounter value on those cells and, when the zone has no encounter table,
/// its part 3 receives a copy of another zone's table. Both archives are rebuilt and re-read and go into one
/// history entry, so they are undone and rebuilt together.
/// </summary>
internal sealed class Map3dsCatchZoneService
{
    internal const string Module = "Editor de mapas";
    internal const string ActionId = "map3ds.catch-zone.paint";
    private readonly GameProjectSession _project;
    private readonly Guid _owner = Guid.NewGuid();

    internal Map3dsCatchZoneService(GameProjectSession project)
    {
        if (!project.GameConfiguration.XY) throw new NotSupportedException("Las zonas de captura solo están verificadas en X/Y.");
        _project = project;
    }

    internal string ZoneArchive => Path.Combine(_project.GameConfiguration.RomFS, _project.GameConfiguration.GetGARCFileName("encdata"));
    internal string BlockArchive => Path.Combine(_project.GameConfiguration.RomFS, _project.GameConfiguration.GetGARCFileName("mapGR"));

    /// <summary>Builds the change without writing anything.</summary>
    internal EditorProposal Preview(Gen6MapBackend backend, int zone, IReadOnlyCollection<(int X, int Y)> cells, int? sourceZone)
    {
        var check = backend.ValidateCatchCells(zone, cells, sourceZone);
        if (!check.Allowed) throw new InvalidOperationException(check.Reason);
        var layout = backend.Layout(zone);
        var byBlock = cells.GroupBy(c => layout.BlockAt(c.X / layout.BlockCells, c.Y / layout.BlockCells))
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<(int X, int Y)>)g.Select(c => (c.X % layout.BlockCells, c.Y % layout.BlockCells)).ToArray());
        var config = _project.GameConfiguration;
        var files = new List<FileChange>
        {
            Rewrite(BlockArchive, config.GetGARCReference("mapGR"), byBlock.Keys, i => backend.BlockBytes(i), (i, current) => Gen6CatchZones.PaintBlock(current, byBlock[i])),
        };
        bool newTable = !backend.HasEncounterTable(zone);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var changes = new List<BatchChange>
        {
            new(zone, $"Zona {zone:000}", "Casillas con encuentros", $"{backend.EncounterCells(zone).Count}", $"+{cells.Count} (bloques {string.Join(", ", byBlock.Keys.Order())})", "catch.cells"),
        };
        if (newTable)
        {
            int source = sourceZone!.Value;
            byte[] table = backend.EncounterTable(source) ?? throw new InvalidOperationException("La zona de origen no tiene tabla.");
            files.Add(Rewrite(ZoneArchive, config.GetGARCReference("encdata"), [zone], i => backend.ZoneBytes(i), (_, current) => Gen6CatchZones.AddTable(current, table)));
            changes.Add(new BatchChange(zone, $"Zona {zone:000}", "Tabla de encuentros", "—", $"copia de la zona {source:000}", "catch.table"));
        }
        var parameters = new Dictionary<string, string>
        {
            { "ActionId", ActionId }, { "Game", "XY" }, { "Zone", zone.ToString(inv) }, { "Cells", cells.Count.ToString(inv) },
            { "Blocks", string.Join(",", byBlock.Keys.Order().Select(b => b.ToString(inv))) },
            { "Value", "0x" + Gen6CatchZones.EncounterGrass.ToString("X8", inv) },
            { "TableSource", newTable ? sourceZone!.Value.ToString(inv) : "existing" }, { "Gameplay", "not tested" },
        };
        return new EditorProposal(_owner, $"Zona de captura en la zona {zone:000} ({cells.Count} casillas)", 0, [.. files], [.. changes], parameters);
    }

    /// <summary>
    /// Reads an archive, checks every member to change is the one the editor read, applies <paramref name="edit"/>
    /// to the decompressed member, recompresses it (checked to round-trip) and rebuilds the archive, checking
    /// that every other member is unchanged.
    /// </summary>
    private static FileChange Rewrite(string path, GARCReference reference, IEnumerable<int> members, Func<int, byte[]?> shown, Func<int, byte[], byte[]> edit)
    {
        byte[] source = File.ReadAllBytes(path);
        byte[][] records = new GARC.MemGARC(source).Files;
        var original = (byte[][])records.Clone();
        var changed = members.ToHashSet();
        foreach (int i in changed)
        {
            if (i < 0 || i >= records.Length) throw new InvalidDataException($"El miembro {i} no existe en {Path.GetFileName(path)}.");
            byte[] raw = records[i];
            bool compressed = raw.Length > 0 && raw[0] == 0x11;
            byte[] current = compressed ? Decompress(raw) : raw;
            if (shown(i) is not { } expected || !current.AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("El archivo no coincide con lo que muestra el editor. Vuelve a abrir el editor.");
            byte[] edited = edit(i, current);
            byte[] encoded = edited;
            if (compressed)
            {
                encoded = GameCatchZoneService.Compress(edited);
                if (!Decompress(encoded).AsSpan().SequenceEqual(edited)) throw new InvalidDataException("La recompresión no es reversible. No se guardó nada.");
            }
            records[i] = encoded;
        }
        byte[] output = GarcTransaction.Build(source, reference, records, preserveRecordSize: false);
        var reread = new GARC.MemGARC(output).Files;
        if (reread.Length != original.Length) throw new InvalidDataException("La reconstrucción cambió el número de miembros. No se guardó nada.");
        for (int i = 0; i < reread.Length; i++)
            if (!changed.Contains(i) && !reread[i].AsSpan().SequenceEqual(original[i])) throw new InvalidDataException("La reconstrucción cambió otro miembro. No se guardó nada.");
        return new FileChange(path, source, output);
    }

    internal void Apply(EditorProposal proposal)
    {
        if (proposal.Owner != _owner) throw new InvalidOperationException("La propuesta pertenece a otro editor.");
        foreach (var file in proposal.Files) GarcTransaction.ReadUnchanged(file.Path, file.Fingerprint);
        _project.History.Commit(proposal.Files, Module, proposal.Action, proposal.Changes, proposal.Parameters);
    }

    private static byte[] Decompress(byte[] raw)
    {
        using var output = new MemoryStream();
        LZSS.Decompress(new MemoryStream(raw), raw.Length, output);
        return output.ToArray();
    }
}
