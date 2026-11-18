using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Moves an existing Ultra Sun/Ultra Moon NPC through the project history. The area event pack
/// (field GARC member area·11) is decompressed, only the NPC position and its two shapes change
/// (Gen7ZoneEdit), the member is recompressed and checked to round-trip, the GARC is rebuilt and
/// re-read, every other member is checked unchanged, and the change is committed.
/// </summary>
internal sealed class Map3dsGen7NpcMoveService
{
    internal const string Module = "Editor de mapas";
    internal const string ActionId = "map3ds.gen7.npc.move";
    private readonly GameProjectSession _project;
    private readonly string _path;
    private readonly GARCReference _reference;
    private byte[] _hash;
    private readonly Guid _owner = Guid.NewGuid();

    internal Map3dsGen7NpcMoveService(GameProjectSession project)
    {
        var config = project.GameConfiguration;
        if (!config.USUM) throw new NotSupportedException("Mover NPC en Gen7 solo está verificado en Ultra Sol / Ultra Luna.");
        _project = project;
        _path = Path.Combine(config.RomFS, config.GetGARCFileName("encdata"));
        _reference = config.GetGARCReference("encdata");
        _hash = SHA256.HashData(File.ReadAllBytes(_path));
    }

    internal string FieldArchive => _path;
    internal void Refresh() => _hash = SHA256.HashData(File.ReadAllBytes(_path));

    internal EditorProposal Preview(Gen7MapBackend backend, int area, int index, int x, int y)
    {
        var check = backend.ValidateMove(area, MapEventKind.Npc, index, x, y);
        if (!check.Allowed) throw new InvalidOperationException(check.Reason);
        var r = backend.EventRef(area, index) ?? throw new InvalidOperationException("Evento desconocido.");
        var target = backend.MoveTarget(area, index, x, y);
        byte[] source = GarcTransaction.ReadUnchanged(_path, _hash);
        byte[][] records = new GARC.MemGARC(source).Files;
        int member = area * Gen7MapBackend.SlotsPerWorld;
        if (member >= records.Length) throw new InvalidDataException("El área no existe en el archivo.");
        byte[] raw = records[member];
        bool compressed = raw.Length > 0 && raw[0] == 0x11;
        byte[] current = compressed ? Decompress(raw) : raw;
        if (!current.AsSpan().SequenceEqual(backend.EventBytes(area)))
            throw new InvalidDataException("El paquete de eventos del archivo no coincide con el que muestra el editor. Vuelve a abrir el editor.");
        byte[] moved = Gen7ZoneEdit.MoveNpc(current, r.Zone, r.Row, target);
        byte[] encoded = moved;
        if (compressed)
        {
            encoded = GameCatchZoneService.Compress(moved);
            if (!Decompress(encoded).AsSpan().SequenceEqual(moved)) throw new InvalidDataException("La recompresión no es reversible. No se guardó nada.");
        }
        records[member] = encoded;
        byte[] output = GarcTransaction.Build(source, _reference, records, preserveRecordSize: false);
        var before = new GARC.MemGARC(source).Files;
        var after = new GARC.MemGARC(output).Files;
        for (int i = 0; i < after.Length; i++)
            if (i != member && !after[i].AsSpan().SequenceEqual(before[i])) throw new InvalidDataException("La reconstrucción cambió otro archivo de campo. No se guardó nada.");
        string from = $"{r.Position.X:0.#}, {r.Position.Z:0.#} (altura {r.Position.Y:0.#})", to = $"{target.X:0.#}, {target.Z:0.#} (altura {target.Y:0.#})";
        var changes = new[] { new BatchChange(area, $"Área {area:000}", $"NPC zona {r.Zone} fila {r.Row} · posición", from, to, "npc.position") };
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var parameters = new Dictionary<string, string>
        {
            { "ActionId", ActionId }, { "Game", "USUM" }, { "Area", area.ToString(inv) }, { "LocalZone", r.Zone.ToString(inv) }, { "Row", r.Row.ToString(inv) },
            { "From", from }, { "To", to }, { "Gameplay", "not tested" },
        };
        string restoreKey = Convert.ToHexString(SHA256.HashData(encoded));
        parameters["RestoreBlob"] = restoreKey;
        _pendingOriginal = (restoreKey, raw);
        return new EditorProposal(_owner, $"Mover NPC (zona {r.Zone}, fila {r.Row}) del área {area:000}", 0, [new FileChange(_path, source, output)], changes, parameters);
    }

    private (string Key, byte[] Original)? _pendingOriginal;

    /// <summary>
    /// Folder (next to the project history) with the original member of each area before a move,
    /// named by the SHA-256 of the moved member. Needed because restore points are not kept for
    /// files over RestorePoints.MaxBytes, and this field archive is larger.
    /// </summary>
    private string OriginalsFolder => Path.Combine(_project.History.StorageDirectory, "map3ds-originales");

    private (byte[] Source, byte[][] Records, int Member, string Key) Current(int area)
    {
        byte[] source = GarcTransaction.ReadUnchanged(_path, _hash);
        byte[][] records = new GARC.MemGARC(source).Files;
        int member = area * Gen7MapBackend.SlotsPerWorld;
        if (member >= records.Length) throw new InvalidDataException("El área no existe en el archivo.");
        return (source, records, member, Convert.ToHexString(SHA256.HashData(records[member])));
    }

    /// <summary>Whether the area's current event pack comes from a move made here and can be restored exactly.</summary>
    internal bool CanRestore(int area) => File.Exists(Path.Combine(OriginalsFolder, Current(area).Key + ".bin"));

    /// <summary>Puts back, byte for byte, the event pack the area had before its last move (a new history entry).</summary>
    internal EditorProposal PreviewRestore(int area)
    {
        var (source, records, member, key) = Current(area);
        string blob = Path.Combine(OriginalsFolder, key + ".bin");
        if (!File.Exists(blob)) throw new InvalidOperationException("No hay estado anterior guardado para el paquete de eventos actual de esta área.");
        byte[] original = File.ReadAllBytes(blob);
        byte[] decoded = original.Length > 0 && original[0] == 0x11 ? Decompress(original) : original;
        _ = pk3DS.Core.Maps.Gen6.Gen6Pack.Read(decoded, "ED");
        records[member] = original;
        byte[] output = GarcTransaction.Build(source, _reference, records, preserveRecordSize: false);
        var before = new GARC.MemGARC(source).Files;
        var after = new GARC.MemGARC(output).Files;
        for (int i = 0; i < after.Length; i++)
            if (i != member && !after[i].AsSpan().SequenceEqual(before[i])) throw new InvalidDataException("La reconstrucción cambió otro archivo de campo. No se restauró nada.");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return new EditorProposal(_owner, $"Restaurar eventos del área {area:000} antes del último movimiento", 0, [new FileChange(_path, source, output)],
            [new BatchChange(area, $"Área {area:000}", "Paquete de eventos", key[..12], Convert.ToHexString(SHA256.HashData(original))[..12], "npc.position.restore")],
            new Dictionary<string, string> { { "ActionId", ActionId + ".restore" }, { "Game", "USUM" }, { "Area", area.ToString(inv) } });
    }

    internal void Apply(EditorProposal proposal)
    {
        if (proposal.Owner != _owner) throw new InvalidOperationException("La propuesta pertenece a otro editor.");
        foreach (var file in proposal.Files) GarcTransaction.ReadUnchanged(file.Path, file.Fingerprint);
        if (_pendingOriginal is { } keep && proposal.Parameters.TryGetValue("RestoreBlob", out var key) && key == keep.Key)
        {
            Directory.CreateDirectory(OriginalsFolder);
            string blob = Path.Combine(OriginalsFolder, key + ".bin");
            if (!File.Exists(blob)) File.WriteAllBytes(blob, keep.Original);
        }
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
