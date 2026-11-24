using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Adds a wild-encounter zone to an Ultra Sun / Ultra Moon area through the project history: a box region in part 6 of
/// the area event pack (member area·11 of the field archive) and, when the table comes from another area, a copy of it
/// in the area's encounter table pack (member area·11 + 9). Both members are recompressed (checked to round-trip) in one
/// rebuild of the archive, every other member is checked unchanged, and the original members are kept together so
/// the area can be restored exactly (the archive is too large for the history's restore points).
/// </summary>
internal sealed class Map3dsGen7CatchZoneService
{
    internal const string Module = "Editor de mapas";
    internal const string ActionId = "map3ds.gen7.catch-zone.add";
    private readonly GameProjectSession _project;
    private readonly string _path;
    private readonly GARCReference _reference;
    private readonly Guid _owner = Guid.NewGuid();
    private (string Key, byte[] Events, byte[] Tables)? _pending;

    internal Map3dsGen7CatchZoneService(GameProjectSession project)
    {
        var config = project.GameConfiguration;
        if (!config.USUM) throw new NotSupportedException("Las zonas de captura Gen7 solo están verificadas en Ultra Sol / Ultra Luna.");
        _project = project;
        _path = Path.Combine(config.RomFS, config.GetGARCFileName("encdata"));
        _reference = config.GetGARCReference("encdata");
    }

    internal string FieldArchive => _path;
    private string OriginalsFolder => Path.Combine(_project.History.StorageDirectory, "map3ds-originales", "zonas-captura");

    private static int EventsMember(int area) => area * Gen7MapBackend.SlotsPerWorld;
    private static int TablesMember(int area) => (area * Gen7MapBackend.SlotsPerWorld) + Gen7CatchZones.TableSlot;
    private static string Key(byte[] events, byte[] tables) => Convert.ToHexString(SHA256.HashData([.. events, .. tables]));

    /// <summary>Builds the change without writing anything.</summary>
    /// <param name="experimentalSoil">Only for in-game experiments (test runner); see <see cref="Gen7MapBackend.PlanCatch"/>.</param>
    internal EditorProposal Preview(Gen7MapBackend backend, int area, IReadOnlyCollection<(int X, int Y)> cells, int? source, uint? experimentalSoil = null)
    {
        var (plan, reason) = backend.PlanCatch(area, cells, source, experimentalSoil);
        if (plan is null) throw new InvalidOperationException(reason);
        byte[] entry = Gen7CatchZones.Table(backend.TableBytes(plan.SourceArea), plan.SourceTable) ?? throw new InvalidOperationException("La tabla de origen no existe.");
        byte[] source0 = File.ReadAllBytes(_path);
        byte[][] records = new GARC.MemGARC(source0).Files;
        var original = (byte[][])records.Clone();
        int ev = EventsMember(area), tb = TablesMember(area);
        if (tb >= records.Length) throw new InvalidDataException("El área no existe en el archivo.");
        byte[] events = Decode(records[ev]), tables = Decode(records[tb]);
        if (!events.AsSpan().SequenceEqual(backend.EventBytes(area)) || !tables.AsSpan().SequenceEqual(backend.TableBytes(area)))
            throw new InvalidDataException("El archivo no coincide con lo que muestra el editor. Vuelve a abrir el editor.");
        int table = plan.SourceTable;
        byte[] newTables = tables;
        if (plan.NewTable) (newTables, table) = Gen7CatchZones.AddTable(tables, entry);
        byte[] newEvents = Gen7CatchZones.AddRegion(events, plan.ZoneFile, (uint)table, plan.Mask, plan.Position, plan.Size);
        records[ev] = Encode(records[ev], newEvents);
        if (plan.NewTable) records[tb] = Encode(records[tb], newTables);
        byte[] output = GarcTransaction.Build(source0, _reference, records, preserveRecordSize: false);
        var after = new GARC.MemGARC(output).Files;
        if (after.Length != original.Length) throw new InvalidDataException("La reconstrucción cambió el número de miembros. No se guardó nada.");
        for (int i = 0; i < after.Length; i++)
            if (i != ev && !(plan.NewTable && i == tb) && !after[i].AsSpan().SequenceEqual(original[i])) throw new InvalidDataException("La reconstrucción cambió otro archivo de campo. No se guardó nada.");
        if (!Decode(after[ev]).AsSpan().SequenceEqual(newEvents) || !Decode(after[tb]).AsSpan().SequenceEqual(newTables)) throw new InvalidDataException("La relectura no coincide con la propuesta. No se guardó nada.");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string key = Key(after[ev], after[tb]);
        _pending = (key, original[ev], original[tb]);
        var changes = new List<BatchChange>
        {
            new(area, $"Área {area:000}", "Regiones de encuentro", $"{backend.EncounterRegions(area).Count}", $"+1 (caja sobre {plan.Cells} casillas, tabla {table})", "catch.region"),
        };
        if (plan.NewTable) changes.Add(new BatchChange(area, $"Área {area:000}", "Tablas de encuentros", $"{Gen7CatchZones.TableCount(tables)}", $"+1 (copia de la tabla {plan.SourceTable} del área {plan.SourceArea:000})", "catch.table"));
        var parameters = new Dictionary<string, string>
        {
            { "ActionId", ActionId }, { "Game", "USUM" }, { "Area", area.ToString(inv) }, { "Cells", plan.Cells.ToString(inv) }, { "Table", table.ToString(inv) },
            { "SourceArea", plan.SourceArea.ToString(inv) }, { "SourceTable", plan.SourceTable.ToString(inv) }, { "Mask", "0x" + plan.Mask.ToString("X8", inv) },
            { "Box", $"{plan.Position.X.ToString("0.##", inv)},{plan.Position.Y.ToString("0.##", inv)},{plan.Position.Z.ToString("0.##", inv)} {plan.Size.X.ToString("0", inv)}x{plan.Size.Y.ToString("0", inv)}x{plan.Size.Z.ToString("0", inv)}" },
            { "RestoreBlob", key }, { "Gameplay", "not tested" },
            { "Experimental", experimentalSoil is uint e ? $"suelo 0x{e:X2} sin precedente; regla de terreno compartido omitida" : "no" },
        };
        return new EditorProposal(_owner, $"Zona de captura en el área {area:000} ({plan.Cells} casillas)", 0, [new FileChange(_path, source0, output)], [.. changes], parameters);
    }

    private static byte[] Decode(byte[] raw) => raw.Length > 0 && raw[0] == 0x11 ? Decompress(raw) : raw;

    private static byte[] Encode(byte[] originalRaw, byte[] edited)
    {
        if (originalRaw.Length == 0 || originalRaw[0] != 0x11) return edited;
        byte[] encoded = GameCatchZoneService.Compress(edited);
        if (!Decompress(encoded).AsSpan().SequenceEqual(edited)) throw new InvalidDataException("La recompresión no es reversible. No se guardó nada.");
        return encoded;
    }

    private (byte[] Source, byte[][] Records, string Key) Current(int area)
    {
        byte[] source = File.ReadAllBytes(_path);
        byte[][] records = new GARC.MemGARC(source).Files;
        if (TablesMember(area) >= records.Length) throw new InvalidDataException("El área no existe en el archivo.");
        return (source, records, Key(records[EventsMember(area)], records[TablesMember(area)]));
    }

    /// <summary>Whether the area's current events and tables come from a zone added here and can be put back exactly.</summary>
    internal bool CanRestore(int area) => Directory.Exists(Path.Combine(OriginalsFolder, Current(area).Key));

    /// <summary>Puts back, byte for byte, the event and table members the area had before its last zone (a new history entry).</summary>
    internal EditorProposal PreviewRestore(int area)
    {
        var (source, records, key) = Current(area);
        string folder = Path.Combine(OriginalsFolder, key);
        if (!Directory.Exists(folder)) throw new InvalidOperationException("No hay estado anterior guardado para las regiones actuales de esta área.");
        byte[] events = File.ReadAllBytes(Path.Combine(folder, "eventos.bin")), tables = File.ReadAllBytes(Path.Combine(folder, "tablas.bin"));
        _ = Gen7CatchZones.Regions(Decode(events));
        int ev = EventsMember(area), tb = TablesMember(area);
        var original = (byte[][])records.Clone();
        records[ev] = events; records[tb] = tables;
        byte[] output = GarcTransaction.Build(source, _reference, records, preserveRecordSize: false);
        var after = new GARC.MemGARC(output).Files;
        for (int i = 0; i < after.Length; i++)
            if (i != ev && i != tb && !after[i].AsSpan().SequenceEqual(original[i])) throw new InvalidDataException("La reconstrucción cambió otro archivo de campo. No se restauró nada.");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return new EditorProposal(_owner, $"Restaurar regiones y tablas del área {area:000} antes de la última zona de captura", 0, [new FileChange(_path, source, output)],
            [new BatchChange(area, $"Área {area:000}", "Regiones y tablas de encuentro", key[..12], "estado anterior", "catch.restore")],
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
            if (!File.Exists(Path.Combine(folder, "tablas.bin"))) File.WriteAllBytes(Path.Combine(folder, "tablas.bin"), keep.Tables);
        }
        _project.History.Commit(proposal.Files, Module, proposal.Action, proposal.Changes, proposal.Parameters);
    }

    private static byte[] Decompress(byte[] raw)
    {
        using var output = new MemoryStream();
        LZSS.Decompress(new MemoryStream(raw), raw.Length, output);
        return output.ToArray();
    }
}
