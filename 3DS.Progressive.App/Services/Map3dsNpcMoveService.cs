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
        var before = backend.ZoneEvents(zone).First(e => e.Kind == MapEventKind.Npc && e.Index == npc);
        var (source, output) = Rewrite(backend, zone, current => Gen6ZoneEdit.MoveNpc(current, npc, x, y));
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

    /// <summary>
    /// Changes the facing and, when given, the sight range of an existing NPC (u16 0x0C and 0x0E of its record)
    /// through the same checks as a move. Nothing is written until <see cref="Apply"/>.
    /// </summary>
    internal EditorProposal PreviewView(Gen6MapBackend backend, int zone, int npc, double facing, int? sight)
    {
        var check = backend.ValidateView(zone, MapEventKind.Npc, npc, facing, sight);
        if (!check.Allowed) throw new InvalidOperationException(check.Reason);
        var view = backend.View(zone, MapEventKind.Npc, npc) ?? throw new InvalidOperationException("El NPC no tiene orientación legible.");
        int value = Gen6MapBackend.FacingValue(facing) ?? throw new InvalidOperationException("Dirección no verificada.");
        int? newSight = sight is int s && s != view.Sight ? s : null;
        var (source, output) = Rewrite(backend, zone, current => Gen6ZoneEdit.SetNpcView(current, npc, value, newSight));
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var changes = new List<BatchChange>();
        if (Gen6MapBackend.FacingValue(view.Facing) != value)
            changes.Add(new BatchChange(zone, $"Zona {zone:000}", $"NPC {npc} · orientación", Gen6MapBackend.DirectionName(view.Facing), Gen6MapBackend.DirectionName(facing), "npc.facing"));
        if (newSight is int ns)
            changes.Add(new BatchChange(zone, $"Zona {zone:000}", $"NPC {npc} · alcance de visión", (view.Sight ?? 0).ToString(inv), ns.ToString(inv), "npc.sight"));
        var parameters = new Dictionary<string, string>
        {
            { "ActionId", ViewActionId }, { "Game", _project.GameConfiguration.XY ? "XY" : "ORAS" },
            { "Zone", zone.ToString(inv) }, { "Npc", npc.ToString(inv) }, { "Facing", value.ToString(inv) },
            { "Sight", (newSight ?? view.Sight ?? 0).ToString(inv) }, { "Gameplay", "not tested" },
        };
        string what = string.Join(" y ", changes.Select(c => c.Field.Split(" · ")[1]));
        return new EditorProposal(_owner, $"Cambiar {what} del NPC {npc} de la zona {zone:000}", 0, [new FileChange(_path, source, output)], changes, parameters);
    }

    internal const string ViewActionId = "map3ds.npc.view";
    internal const string NurseActionId = "map3ds.npc.add-nurse";

    /// <summary>
    /// Adds a Pokémon Center nurse on cell (x, y): a copy of the game's own nurse record (model and
    /// script 2100) with a new ID, the cell and the facing. Same checks and history as a move.
    /// </summary>
    internal EditorProposal PreviewAddNurse(Gen6MapBackend backend, int zone, int x, int y, double facing)
    {
        var check = backend.ValidateAddNurse(zone, x, y, facing);
        if (!check.Allowed) throw new InvalidOperationException(check.Reason);
        var nurse = backend.Nurse ?? throw new InvalidOperationException(backend.NurseSummary);
        int value = Gen6MapBackend.FacingValue(facing) ?? throw new InvalidOperationException("Dirección no verificada.");
        int index = backend.ZoneEvents(zone).Count(e => e.Kind == MapEventKind.Npc);
        var (source, output) = Rewrite(backend, zone, current => Gen6ZoneEdit.AddNpc(current, nurse.Record, x, y, value));
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string to = $"{x}, {y} · mira {Gen6MapBackend.DirectionName(facing)}";
        var changes = new[] { new BatchChange(zone, $"Zona {zone:000}", $"NPC {index} · enfermera nueva", "—", to, "npc.add-nurse") };
        var parameters = new Dictionary<string, string>
        {
            { "ActionId", NurseActionId }, { "Game", _project.GameConfiguration.XY ? "XY" : "ORAS" },
            { "Zone", zone.ToString(inv) }, { "Npc", index.ToString(inv) }, { "X", x.ToString(inv) }, { "Y", y.ToString(inv) }, { "Facing", value.ToString(inv) },
            { "Script", Gen6Nurse.Script.ToString(inv) }, { "Model", nurse.Model.ToString(inv) },
            { "TemplateZone", nurse.Zone.ToString(inv) }, { "TemplateNpc", nurse.Index.ToString(inv) }, { "Gameplay", "not tested" },
        };
        return new EditorProposal(_owner, $"Añadir enfermera en la zona {zone:000} ({x}, {y})", 0, [new FileChange(_path, source, output)], changes, parameters);
    }

    /// <summary>
    /// Reads the zone archive by fingerprint, checks the zone is the one the editor shows, applies
    /// <paramref name="edit"/> to the decompressed zone, recompresses it (checked to round-trip) and
    /// rebuilds the archive, checking that every other zone is unchanged.
    /// </summary>
    private (byte[] Source, byte[] Output) Rewrite(Gen6MapBackend backend, int zone, Func<byte[], byte[]> edit)
    {
        byte[] source = GarcTransaction.ReadUnchanged(_path, _hash);
        byte[][] records = new GARC.MemGARC(source).Files;
        if (zone < 0 || zone >= records.Length) throw new InvalidDataException("La zona no existe en el archivo.");
        byte[] raw = records[zone];
        bool compressed = raw.Length > 0 && raw[0] == 0x11;
        byte[] current = compressed ? Decompress(raw) : raw;
        if (!current.AsSpan().SequenceEqual(backend.ZoneBytes(zone)))
            throw new InvalidDataException("La zona del archivo no coincide con la que muestra el editor. Vuelve a abrir el editor.");
        byte[] edited = edit(current);
        byte[] encoded = edited;
        if (compressed)
        {
            encoded = GameCatchZoneService.Compress(edited);
            if (!Decompress(encoded).AsSpan().SequenceEqual(edited)) throw new InvalidDataException("La recompresión de la zona no es reversible. No se guardó nada.");
        }
        records[zone] = encoded;
        byte[] output = GarcTransaction.Build(source, _reference, records, preserveRecordSize: false);
        var reread = new GARC.MemGARC(output).Files;
        var original = new GARC.MemGARC(source).Files;
        for (int i = 0; i < reread.Length; i++)
            if (i != zone && !reread[i].AsSpan().SequenceEqual(original[i]))
                throw new InvalidDataException("La reconstrucción cambió otra zona. No se guardó nada.");
        return (source, output);
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
