using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Moves an existing Gen6 NPC to another cell through the project history:
/// the zone record is decompressed, only the NPC position changes, the record
/// is recompressed (checked to round-trip), the GARC is rebuilt and re-read,
/// and the change is committed so it can be undone and rebuilt like any other.
/// </summary>
internal sealed class Map3dsNpcMoveService
{
    internal const string Module = "Editor de mapas";
    internal const string ActionId = "map3ds.npc.move";
    private readonly GameProjectSession _project;
    private readonly string _path;
    private readonly GARCReference _reference;
    private byte[] _hash;
    private readonly Guid _owner = Guid.NewGuid();

    internal Map3dsNpcMoveService(GameProjectSession project)
    {
        var config = project.GameConfiguration;
        if (!config.XY && !config.ORAS) throw new NotSupportedException("Mover NPC solo está verificado en X/Y y Rubí Omega/Zafiro Alfa.");
        _project = project;
        _path = Path.Combine(config.RomFS, config.GetGARCFileName("encdata"));
        _reference = config.GetGARCReference("encdata");
        _hash = SHA256.HashData(File.ReadAllBytes(_path));
    }

    internal string ZoneArchive => _path;

    /// <summary>
    /// Takes the archive on disk as the new base, when the view reloads its zones
    /// (for example after an undo). <see cref="Preview"/> still checks that the
    /// zone in the file matches the one the editor shows.
    /// </summary>
    internal void Refresh() => _hash = SHA256.HashData(File.ReadAllBytes(_path));

    /// <summary>Builds the change without writing anything.</summary>
    internal EditorProposal Preview(Gen6MapBackend backend, int zone, int npc, int x, int y)
    {
        var check = backend.ValidateMove(zone, MapEventKind.Npc, npc, x, y);
        if (!check.Allowed) throw new InvalidOperationException(check.Reason);
        byte[] source = GarcTransaction.ReadUnchanged(_path, _hash);
        byte[][] records = new GARC.MemGARC(source).Files;
        if (zone < 0 || zone >= records.Length) throw new InvalidDataException("La zona no existe en el archivo.");
        byte[] raw = records[zone];
        bool compressed = raw.Length > 0 && raw[0] == 0x11;
        byte[] current = compressed ? Decompress(raw) : raw;
        if (!current.AsSpan().SequenceEqual(backend.ZoneBytes(zone)))
            throw new InvalidDataException("La zona del archivo no coincide con la que muestra el editor. Vuelve a abrir el editor.");
        var before = backend.ZoneEvents(zone).First(e => e.Kind == MapEventKind.Npc && e.Index == npc);
        byte[] moved = Gen6ZoneEdit.MoveNpc(current, npc, x, y);
        byte[] encoded = moved;
        if (compressed)
        {
            encoded = GameCatchZoneService.Compress(moved);
            if (!Decompress(encoded).AsSpan().SequenceEqual(moved)) throw new InvalidDataException("La recompresión de la zona no es reversible. No se guardó nada.");
        }
        records[zone] = encoded;
        byte[] output = GarcTransaction.Build(source, _reference, records, preserveRecordSize: false);
        var reread = new GARC.MemGARC(output).Files;
        for (int i = 0; i < reread.Length; i++)
            if (i != zone && !reread[i].AsSpan().SequenceEqual(new GARC.MemGARC(source).Files[i]))
                throw new InvalidDataException("La reconstrucción cambió otra zona. No se guardó nada.");
        string from = $"{before.X:0}, {before.Y:0}", to = $"{x}, {y}";
        var changes = new[] { new BatchChange(zone, $"Zona {zone:000}", $"NPC {npc} · casilla", from, to, "npc.position") };
        var parameters = new Dictionary<string, string>
        {
            { "ActionId", ActionId }, { "Game", _project.GameConfiguration.XY ? "XY" : "ORAS" },
            { "Zone", zone.ToString(System.Globalization.CultureInfo.InvariantCulture) }, { "Npc", npc.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "From", from }, { "To", to }, { "Gameplay", "not tested" },
        };
        return new EditorProposal(_owner, $"Mover NPC {npc} de la zona {zone:000} a {to}", 0, [new FileChange(_path, source, output)], changes, parameters);
    }

    internal void Apply(EditorProposal proposal)
    {
        if (proposal.Owner != _owner) throw new InvalidOperationException("La propuesta pertenece a otro editor.");
        foreach (var file in proposal.Files) GarcTransaction.ReadUnchanged(file.Path, file.Fingerprint);
        _project.History.Commit(proposal.Files, Module, proposal.Action, proposal.Changes, proposal.Parameters);
        _hash = SHA256.HashData(File.ReadAllBytes(_path));
    }

    private static byte[] Decompress(byte[] raw)
    {
        using var output = new MemoryStream();
        LZSS.Decompress(new MemoryStream(raw), raw.Length, output);
        return output.ToArray();
    }
}
