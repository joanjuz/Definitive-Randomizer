using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Adds a Pokémon Center nurse to an Ultra Sun / Ultra Moon area through the project history (see <see cref="Gen7Nurse"/>):
/// the copied NPC in part 1 of the area event pack (member area·11 of the field archive), the healing machine her script
/// animates (static object 2 in part 4) when the area has none, her model's "CP" pack in the area character pack (member
/// area·11 + 3) and the machine's "SM" pack in the area object pack (member area·11 + 4) when missing. The members are
/// recompressed (checked to round-trip) in one rebuild of the archive, every other member is checked unchanged, and the
/// three original members are kept together so the area can be restored exactly (the archive is too large for the
/// history's restore points). When the area's zone has no field gimmick, its gimmick byte in the zone records (zonedata
/// member 0) becomes the Pokémon Center's in the same history entry; that member is kept with the others.
/// </summary>
internal sealed class Map3dsGen7NurseService
{
    internal const string Module = "Editor de mapas";
    internal const string ActionId = "map3ds.gen7.nurse.add";
    private readonly GameProjectSession _project;
    private readonly string _path;
    private readonly GARCReference _reference, _zoneReference;
    private readonly string _zonePath;
    private readonly Guid _owner = Guid.NewGuid();
    private (string Key, byte[] Events, byte[] Characters, byte[] Objects, byte[] Zones)? _pending;

    internal Map3dsGen7NurseService(GameProjectSession project)
    {
        var config = project.GameConfiguration;
        if (!config.USUM) throw new NotSupportedException("La enfermera Gen7 solo está verificada en Ultra Sol / Ultra Luna.");
        _project = project;
        _path = Path.Combine(config.RomFS, config.GetGARCFileName("encdata"));
        _reference = config.GetGARCReference("encdata");
        _zonePath = Path.Combine(config.RomFS, config.GetGARCFileName("zonedata"));
        _zoneReference = config.GetGARCReference("zonedata");
    }

    internal string ZoneArchive => _zonePath;

    internal string FieldArchive => _path;
    private string OriginalsFolder => Path.Combine(_project.History.StorageDirectory, "map3ds-originales", "enfermeras");

    private static int EventsMember(int area) => area * Gen7MapBackend.SlotsPerWorld;
    private static int CharactersMember(int area) => (area * Gen7MapBackend.SlotsPerWorld) + Gen7MapBackend.CharacterSlot;
    private static int ObjectsMember(int area) => (area * Gen7MapBackend.SlotsPerWorld) + Gen7MapBackend.ObjectSlot;
    private static string Key(byte[] events, byte[] characters, byte[] objects, byte[] zones) => Convert.ToHexString(SHA256.HashData([.. events, .. characters, .. objects, .. zones]));

    /// <summary>Builds the change without writing anything.</summary>
    internal EditorProposal Preview(Gen7MapBackend backend, int area, int x, int y, double facing)
    {
        var (plan, reason) = backend.PlanNurse(area, x, y, facing);
        if (plan is null) throw new InvalidOperationException(reason);
        byte[] source = File.ReadAllBytes(_path);
        byte[][] records = new GARC.MemGARC(source).Files;
        var original = (byte[][])records.Clone();
        int ev = EventsMember(area), ch = CharactersMember(area), ob = ObjectsMember(area);
        if (ob >= records.Length) throw new InvalidDataException("El área no existe en el archivo.");
        if (!Decode(records[ev]).AsSpan().SequenceEqual(backend.EventBytes(area)) || !Decode(records[ch]).AsSpan().SequenceEqual(backend.CharacterBytes(area))
            || !Decode(records[ob]).AsSpan().SequenceEqual(backend.ObjectBytes(area)))
            throw new InvalidDataException("El archivo no coincide con lo que muestra el editor. Vuelve a abrir el editor.");
        var (events, characters, objects) = backend.NurseEdits(plan);
        records[ev] = Encode(records[ev], events);
        if (characters is not null) records[ch] = Encode(records[ch], characters);
        if (objects is not null) records[ob] = Encode(records[ob], objects);
        byte[] output = GarcTransaction.Build(source, _reference, records, preserveRecordSize: false);
        var after = new GARC.MemGARC(output).Files;
        if (after.Length != original.Length) throw new InvalidDataException("La reconstrucción cambió el número de miembros. No se guardó nada.");
        for (int i = 0; i < after.Length; i++)
            if (i != ev && !(characters is not null && i == ch) && !(objects is not null && i == ob) && !after[i].AsSpan().SequenceEqual(original[i])) throw new InvalidDataException("La reconstrucción cambió otro archivo de campo. No se guardó nada.");
        if (!Decode(after[ev]).AsSpan().SequenceEqual(events) || (characters is not null && !Decode(after[ch]).AsSpan().SequenceEqual(characters))
            || (objects is not null && !Decode(after[ob]).AsSpan().SequenceEqual(objects)))
            throw new InvalidDataException("La relectura no coincide con la propuesta. No se guardó nada.");

        // Zone records: the Pokémon Center field gimmick for the area's zone when it has none.
        byte[] zoneSource = File.ReadAllBytes(_zonePath);
        byte[][] zoneRecords = new GARC.MemGARC(zoneSource).Files;
        if (zoneRecords.Length == 0 || backend.ZoneData is null || !Decode(zoneRecords[0]).AsSpan().SequenceEqual(backend.ZoneData))
            throw new InvalidDataException("La tabla de zonas no coincide con la que leyó el editor. Vuelve a abrir el editor.");
        byte[] zoneOriginal = zoneRecords[0];
        var files = new List<FileChange> { new(_path, source, output) };
        byte[] zoneAfter = zoneOriginal;
        if (backend.NurseZoneEdit(plan) is { } zones)
        {
            var zoneBefore = (byte[][])zoneRecords.Clone();
            zoneRecords[0] = Encode(zoneRecords[0], zones);
            byte[] zoneOutput = GarcTransaction.Build(zoneSource, _zoneReference, zoneRecords, preserveRecordSize: false);
            var zoneCheck = new GARC.MemGARC(zoneOutput).Files;
            if (zoneCheck.Length != zoneBefore.Length || !Decode(zoneCheck[0]).AsSpan().SequenceEqual(zones)
                || Enumerable.Range(1, zoneCheck.Length - 1).Any(i => !zoneCheck[i].AsSpan().SequenceEqual(zoneBefore[i])))
                throw new InvalidDataException("La reconstrucción de la tabla de zonas no coincide. No se guardó nada.");
            zoneAfter = zoneCheck[0];
            files.Add(new FileChange(_zonePath, zoneSource, zoneOutput));
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string key = Key(after[ev], after[ch], after[ob], zoneAfter);
        _pending = (key, original[ev], original[ch], original[ob], zoneOriginal);
        string where = $"{Math.Floor(plan.Position.X / Gen7MapBackend.UnitsPerCell)}, {Math.Floor(plan.Position.Z / Gen7MapBackend.UnitsPerCell)}";
        var changes = new List<BatchChange>
        {
            new(area, $"Área {area:000}", "NPC", "—", $"enfermera (evento {plan.EventId}) en la casilla {where}, mirando hacia {Gen7MapBackend.TrainerDirection(plan.Compass)}", "nurse.npc"),
        };
        if (characters is not null) changes.Add(new BatchChange(area, $"Área {area:000}", "Modelos de personaje", "sin el modelo 66", $"+ modelo 66 ({plan.Characters} modelos)", "nurse.model"));
        if (plan.Machine is { } mp)
            changes.Add(new BatchChange(area, $"Área {area:000}", "Objetos estáticos", "sin máquina de curación",
                $"máquina de curación (estático {plan.MachineEventId}) en la casilla {Math.Floor(mp.X / Gen7MapBackend.UnitsPerCell)}, {Math.Floor(mp.Z / Gen7MapBackend.UnitsPerCell)}", "nurse.machine"));
        if (objects is not null) changes.Add(new BatchChange(area, $"Área {area:000}", "Modelos de objetos", "sin el objeto 2", $"+ objeto 2 ({plan.Objects} objetos)", "nurse.machine-model"));
        if (plan.Zone >= 0) changes.Add(new BatchChange(area, $"Zona {plan.Zone}", "Módulo de campo (gimmick, +0x0E)", Gen7Nurse.NoGimmick.ToString(inv), $"{Gen7Nurse.PokecenGimmick} (Centro Pokémon)", "nurse.gimmick"));
        var parameters = new Dictionary<string, string>
        {
            { "ActionId", ActionId }, { "Game", "USUM" }, { "Area", area.ToString(inv) }, { "Cell", where }, { "Facing", plan.Compass.ToString("0.##", inv) },
            { "EventId", plan.EventId.ToString(inv) }, { "Model", Gen7Nurse.Model.ToString(inv) }, { "Script", Gen7Nurse.Script.ToString(inv) },
            { "AddsModel", characters is not null ? "yes" : "no" }, { "Machine", plan.Machine is null ? "existing" : plan.MachineEventId.ToString(inv) },
            { "AddsMachineModel", objects is not null ? "yes" : "no" }, { "Gimmick", plan.Zone >= 0 ? $"zona {plan.Zone}: 0 → 3" : "sin cambios" },
            { "RestoreBlob", key }, { "Gameplay", "not tested" },
        };
        return new EditorProposal(_owner, $"Enfermera en el área {area:000} (casilla {where})", 0, [.. files], [.. changes], parameters);
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

    private (byte[] Source, byte[][] Records, byte[] ZoneSource, byte[][] ZoneRecords, string Key) Current(int area)
    {
        byte[] source = File.ReadAllBytes(_path);
        byte[][] records = new GARC.MemGARC(source).Files;
        if (ObjectsMember(area) >= records.Length) throw new InvalidDataException("El área no existe en el archivo.");
        byte[] zoneSource = File.ReadAllBytes(_zonePath);
        byte[][] zoneRecords = new GARC.MemGARC(zoneSource).Files;
        if (zoneRecords.Length == 0) throw new InvalidDataException("La tabla de zonas está vacía.");
        return (source, records, zoneSource, zoneRecords, Key(records[EventsMember(area)], records[CharactersMember(area)], records[ObjectsMember(area)], zoneRecords[0]));
    }

    /// <summary>Whether the area's current events and characters come from a nurse added here and can be put back exactly.</summary>
    internal bool CanRestore(int area) => Directory.Exists(Path.Combine(OriginalsFolder, Current(area).Key));

    /// <summary>Puts back, byte for byte, the event and character members the area had before its last nurse (a new history entry).</summary>
    internal EditorProposal PreviewRestore(int area)
    {
        var (source, records, zoneSource, zoneRecords, key) = Current(area);
        string folder = Path.Combine(OriginalsFolder, key);
        if (!Directory.Exists(folder)) throw new InvalidOperationException("No hay estado anterior guardado para la enfermera actual de esta área.");
        byte[] events = File.ReadAllBytes(Path.Combine(folder, "eventos.bin")), characters = File.ReadAllBytes(Path.Combine(folder, "personajes.bin")),
            objects = File.ReadAllBytes(Path.Combine(folder, "objetos.bin")), zones = File.ReadAllBytes(Path.Combine(folder, "zonas.bin"));
        _ = Gen7CatchZones.Parts(Decode(events), "ED"); _ = Gen7Nurse.Characters(Decode(characters)); _ = Gen7Nurse.Objects(Decode(objects));
        int ev = EventsMember(area), ch = CharactersMember(area), ob = ObjectsMember(area);
        var original = (byte[][])records.Clone();
        records[ev] = events; records[ch] = characters; records[ob] = objects;
        byte[] output = GarcTransaction.Build(source, _reference, records, preserveRecordSize: false);
        var after = new GARC.MemGARC(output).Files;
        for (int i = 0; i < after.Length; i++)
            if (i != ev && i != ch && i != ob && !after[i].AsSpan().SequenceEqual(original[i])) throw new InvalidDataException("La reconstrucción cambió otro archivo de campo. No se restauró nada.");
        var files = new List<FileChange> { new(_path, source, output) };
        if (!zoneRecords[0].AsSpan().SequenceEqual(zones))
        {
            var zoneBefore = (byte[][])zoneRecords.Clone();
            zoneRecords[0] = zones;
            byte[] zoneOutput = GarcTransaction.Build(zoneSource, _zoneReference, zoneRecords, preserveRecordSize: false);
            var zoneCheck = new GARC.MemGARC(zoneOutput).Files;
            if (zoneCheck.Length != zoneBefore.Length || Enumerable.Range(1, zoneCheck.Length - 1).Any(i => !zoneCheck[i].AsSpan().SequenceEqual(zoneBefore[i])))
                throw new InvalidDataException("La reconstrucción de la tabla de zonas cambió otro miembro. No se restauró nada.");
            files.Add(new FileChange(_zonePath, zoneSource, zoneOutput));
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return new EditorProposal(_owner, $"Restaurar NPC, objetos, modelos y zona del área {area:000} antes de la última enfermera", 0, [.. files],
            [new BatchChange(area, $"Área {area:000}", "NPC, objetos estáticos y modelos", key[..12], "estado anterior", "nurse.restore")],
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
            if (!File.Exists(Path.Combine(folder, "objetos.bin"))) File.WriteAllBytes(Path.Combine(folder, "objetos.bin"), keep.Objects);
            if (!File.Exists(Path.Combine(folder, "zonas.bin"))) File.WriteAllBytes(Path.Combine(folder, "zonas.bin"), keep.Zones);
        }
        _project.History.Commit(proposal.Files, Module, proposal.Action, proposal.Changes, proposal.Parameters);
    }
}
