using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Adds a Pokémon Center nurse to an Ultra Sun / Ultra Moon area through the project history (see <see cref="Gen7Nurse"/>):
/// the copied NPC in part 1 of the area event pack (member area·11 of the field archive) and, when the area does not load
/// her model, its "CP" pack in the area character pack (member area·11 + 3). Both members are recompressed (checked to
/// round-trip) in one rebuild of the archive, every other member is checked unchanged, and the original members are kept
/// together so the area can be restored exactly (the archive is too large for the history's restore points).
/// </summary>
internal sealed class Map3dsGen7NurseService
{
    internal const string Module = "Editor de mapas";
    internal const string ActionId = "map3ds.gen7.nurse.add";
    private readonly GameProjectSession _project;
    private readonly string _path;
    private readonly GARCReference _reference;
    private readonly Guid _owner = Guid.NewGuid();
    private (string Key, byte[] Events, byte[] Characters)? _pending;

    internal Map3dsGen7NurseService(GameProjectSession project)
    {
        var config = project.GameConfiguration;
        if (!config.USUM) throw new NotSupportedException("La enfermera Gen7 solo está verificada en Ultra Sol / Ultra Luna.");
        _project = project;
        _path = Path.Combine(config.RomFS, config.GetGARCFileName("encdata"));
        _reference = config.GetGARCReference("encdata");
    }

    internal string FieldArchive => _path;
    private string OriginalsFolder => Path.Combine(_project.History.StorageDirectory, "map3ds-originales", "enfermeras");

    private static int EventsMember(int area) => area * Gen7MapBackend.SlotsPerWorld;
    private static int CharactersMember(int area) => (area * Gen7MapBackend.SlotsPerWorld) + Gen7MapBackend.CharacterSlot;
    private static string Key(byte[] events, byte[] characters) => Convert.ToHexString(SHA256.HashData([.. events, .. characters]));

    /// <summary>Builds the change without writing anything.</summary>
    internal EditorProposal Preview(Gen7MapBackend backend, int area, int x, int y, double facing)
    {
        var (plan, reason) = backend.PlanNurse(area, x, y, facing);
        if (plan is null) throw new InvalidOperationException(reason);
        byte[] source = File.ReadAllBytes(_path);
        byte[][] records = new GARC.MemGARC(source).Files;
        var original = (byte[][])records.Clone();
        int ev = EventsMember(area), ch = CharactersMember(area);
        if (ch >= records.Length) throw new InvalidDataException("El área no existe en el archivo.");
        if (!Decode(records[ev]).AsSpan().SequenceEqual(backend.EventBytes(area)) || !Decode(records[ch]).AsSpan().SequenceEqual(backend.CharacterBytes(area)))
            throw new InvalidDataException("El archivo no coincide con lo que muestra el editor. Vuelve a abrir el editor.");
        var (events, characters) = backend.NurseEdits(plan);
        records[ev] = Encode(records[ev], events);
        if (characters is not null) records[ch] = Encode(records[ch], characters);
        byte[] output = GarcTransaction.Build(source, _reference, records, preserveRecordSize: false);
        var after = new GARC.MemGARC(output).Files;
        if (after.Length != original.Length) throw new InvalidDataException("La reconstrucción cambió el número de miembros. No se guardó nada.");
        for (int i = 0; i < after.Length; i++)
            if (i != ev && !(characters is not null && i == ch) && !after[i].AsSpan().SequenceEqual(original[i])) throw new InvalidDataException("La reconstrucción cambió otro archivo de campo. No se guardó nada.");
        if (!Decode(after[ev]).AsSpan().SequenceEqual(events) || (characters is not null && !Decode(after[ch]).AsSpan().SequenceEqual(characters)))
            throw new InvalidDataException("La relectura no coincide con la propuesta. No se guardó nada.");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string key = Key(after[ev], after[ch]);
        _pending = (key, original[ev], original[ch]);
        string where = $"{Math.Floor(plan.Position.X / Gen7MapBackend.UnitsPerCell)}, {Math.Floor(plan.Position.Z / Gen7MapBackend.UnitsPerCell)}";
        var changes = new List<BatchChange>
        {
            new(area, $"Área {area:000}", "NPC", "—", $"enfermera (evento {plan.EventId}) en la casilla {where}, mirando hacia {Gen7MapBackend.TrainerDirection(plan.Compass)}", "nurse.npc"),
        };
        if (characters is not null) changes.Add(new BatchChange(area, $"Área {area:000}", "Modelos de personaje", "sin el modelo 66", $"+ modelo 66 ({plan.Characters} modelos)", "nurse.model"));
        var parameters = new Dictionary<string, string>
        {
            { "ActionId", ActionId }, { "Game", "USUM" }, { "Area", area.ToString(inv) }, { "Cell", where }, { "Facing", plan.Compass.ToString("0.##", inv) },
            { "EventId", plan.EventId.ToString(inv) }, { "Model", Gen7Nurse.Model.ToString(inv) }, { "Script", Gen7Nurse.Script.ToString(inv) },
            { "AddsModel", characters is not null ? "yes" : "no" }, { "RestoreBlob", key }, { "Gameplay", "not tested" },
        };
        return new EditorProposal(_owner, $"Enfermera en el área {area:000} (casilla {where})", 0, [new FileChange(_path, source, output)], [.. changes], parameters);
    }

    private static byte[] Decode(byte[] raw)
    {
        if (raw.Length == 0 || raw[0] != 0x11) return raw;
        using var output = new MemoryStream();
        LZSS.Decompress(new MemoryStream(raw), raw.Length, output);
        return output.ToArray();
    }

    private static byte[] Encode(byte[] originalRaw, byte[] edited)
    {
        if (originalRaw.Length == 0 || originalRaw[0] != 0x11) return edited;
        byte[] encoded = GameCatchZoneService.Compress(edited);
        if (!Decode(encoded).AsSpan().SequenceEqual(edited)) throw new InvalidDataException("La recompresión no es reversible. No se guardó nada.");
        return encoded;
    }

    private (byte[] Source, byte[][] Records, string Key) Current(int area)
    {
        byte[] source = File.ReadAllBytes(_path);
        byte[][] records = new GARC.MemGARC(source).Files;
        if (CharactersMember(area) >= records.Length) throw new InvalidDataException("El área no existe en el archivo.");
        return (source, records, Key(records[EventsMember(area)], records[CharactersMember(area)]));
    }

    /// <summary>Whether the area's current events and characters come from a nurse added here and can be put back exactly.</summary>
    internal bool CanRestore(int area) => Directory.Exists(Path.Combine(OriginalsFolder, Current(area).Key));

    /// <summary>Puts back, byte for byte, the event and character members the area had before its last nurse (a new history entry).</summary>
    internal EditorProposal PreviewRestore(int area)
    {
        var (source, records, key) = Current(area);
        string folder = Path.Combine(OriginalsFolder, key);
        if (!Directory.Exists(folder)) throw new InvalidOperationException("No hay estado anterior guardado para la enfermera actual de esta área.");
        byte[] events = File.ReadAllBytes(Path.Combine(folder, "eventos.bin")), characters = File.ReadAllBytes(Path.Combine(folder, "personajes.bin"));
        _ = Gen7CatchZones.Parts(Decode(events), "ED"); _ = Gen7Nurse.Characters(Decode(characters));
        int ev = EventsMember(area), ch = CharactersMember(area);
        var original = (byte[][])records.Clone();
        records[ev] = events; records[ch] = characters;
        byte[] output = GarcTransaction.Build(source, _reference, records, preserveRecordSize: false);
        var after = new GARC.MemGARC(output).Files;
        for (int i = 0; i < after.Length; i++)
            if (i != ev && i != ch && !after[i].AsSpan().SequenceEqual(original[i])) throw new InvalidDataException("La reconstrucción cambió otro archivo de campo. No se restauró nada.");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return new EditorProposal(_owner, $"Restaurar NPC y modelos del área {area:000} antes de la última enfermera", 0, [new FileChange(_path, source, output)],
            [new BatchChange(area, $"Área {area:000}", "NPC y modelos de personaje", key[..12], "estado anterior", "nurse.restore")],
            new Dictionary<string, string> { { "ActionId", ActionId + ".restore" }, { "Game", "USUM" }, { "Area", area.ToString(inv) } });
    }

    internal void Apply(EditorProposal proposal)
    {
        if (proposal.Owner != _owner) throw new InvalidOperationException("La propuesta pertenece a otro editor.");
        foreach (var file in proposal.Files) GarcTransaction.ReadUnchanged(file.Path, file.Fingerprint);
        if (_pending is { } keep && proposal.Parameters.TryGetValue("RestoreBlob", out var key) && key == keep.Key)
        {
            string folder = Path.Combine(OriginalsFolder, key);
            Directory.CreateDirectory(folder);
            if (!File.Exists(Path.Combine(folder, "eventos.bin"))) File.WriteAllBytes(Path.Combine(folder, "eventos.bin"), keep.Events);
            if (!File.Exists(Path.Combine(folder, "personajes.bin"))) File.WriteAllBytes(Path.Combine(folder, "personajes.bin"), keep.Characters);
        }
        _project.History.Commit(proposal.Files, Module, proposal.Action, proposal.Changes, proposal.Parameters);
    }
}
