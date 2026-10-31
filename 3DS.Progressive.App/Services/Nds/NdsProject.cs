using System.IO;
using System.Text;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>One applied edit or randomization, kept for the change log.</summary>
internal sealed record NdsOperation(string Action, uint? Seed, IReadOnlyList<Gen5Change> Changes);

/// <summary>A computed change waiting to be applied; it belongs to one project revision and is rejected once that changes.</summary>
internal sealed class NdsPreview
{
    internal Guid Owner { get; }
    internal int Revision { get; }
    internal string Action { get; }
    internal uint? Seed { get; }
    internal Gen5RandomizerResult Result { get; }
    internal NdsPreview(Guid owner, int revision, string action, uint? seed, Gen5RandomizerResult result)
    { Owner = owner; Revision = revision; Action = action; Seed = seed; Result = result; }
}

/// <summary>
/// A DS ROM opened for editing (Gen4 or Gen5): manual edits and randomizations stack in memory as changed tables and are
/// written only when exporting, to a new ROM. The source ROM is never modified. Heavy calls belong off the UI thread.
/// </summary>
internal sealed class NdsProject : IDisposable
{
    private readonly Guid _id = Guid.NewGuid();
    private readonly NdsRom _rom;
    private readonly Gen5Data? _gen5;
    private readonly Gen4Data? _gen4;
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<int, byte[]> _overlays = new();
    private readonly HashSet<int> _recompress = new();
    private readonly List<NdsOperation> _history = new();
    private byte[]? _arm9;
    private Gen5Data? _gen5View;
    private Gen4Data? _gen4View;

    internal string SourcePath => _rom.Path;
    internal NdsIdentityResult Identity { get; }
    internal string Game => Identity.Profile.Game;
    internal int Generation => _gen4 is not null ? 4 : 5;
    internal bool IsRomhack => _gen5?.IsRomhack ?? _gen4!.IsRomhack;
    internal IReadOnlyDictionary<string, string> Unavailable => _gen5?.Unavailable ?? _gen4!.Unavailable;
    internal bool Allows(string feature) => !Unavailable.ContainsKey(feature);
    internal int MaxSpecies => _gen4 is not null ? Gen4Data.MaxSpecies : Gen5Randomizer.MaxSpecies;
    /// <summary>Increases with every applied change; previews and editors compare it to detect stale data.</summary>
    internal int Revision { get; private set; }
    internal bool HasChanges => _history.Any(o => o.Changes.Count > 0);
    internal IReadOnlyList<NdsOperation> History => _history.AsReadOnly();
    internal IReadOnlyList<string> SpeciesNames { get; }
    internal IReadOnlyList<string> ItemNames { get; }
    internal IReadOnlyList<string> MoveNames { get; }

    private NdsProject(NdsRom rom, Gen5Data? gen5, Gen4Data? gen4)
    {
        _rom = rom;
        _gen5 = gen5;
        _gen4 = gen4;
        Identity = gen5?.Identity ?? gen4!.Identity;
        SpeciesNames = gen5?.SpeciesNames() ?? gen4!.SpeciesNames();
        ItemNames = gen5 is not null ? gen5.Text(gen5.Profile.ItemNamesText) : gen4!.Text(gen4.Profile.ItemNamesText);
        MoveNames = gen5 is not null ? gen5.Text(gen5.Profile.MoveNamesText) : gen4!.Text(gen4.Profile.MoveNamesText);
    }

    /// <summary>
    /// Opens an original dump; with <paramref name="allowRomhack"/>, a ROM of a supported game whose tables are not the originals
    /// opens as a romhack instead of being rejected. An original always opens as an original.
    /// </summary>
    internal static NdsProject Open(string path, bool allowRomhack)
    {
        var rom = NdsRom.Open(path);
        try
        {
            bool gen4 = NdsIdentity.Identify(rom).Profile.Generation == 4;
            try
            {
                return gen4 ? new NdsProject(rom, null, Gen4Data.Open(rom)) : new NdsProject(rom, Gen5Data.Open(rom), null);
            }
            catch (InvalidDataException) when (allowRomhack)
            {
                return gen4 ? new NdsProject(rom, null, Gen4Data.OpenRomhack(rom)) : new NdsProject(rom, Gen5Data.OpenRomhack(rom), null);
            }
        }
        catch
        {
            rom.Dispose();
            throw;
        }
    }

    // Views of the game with every applied change, rebuilt once per revision.
    private Gen5Data Gen5View => _gen5View ??= _gen5!.WithDraft(new Dictionary<string, byte[]>(_files), new Dictionary<int, byte[]>(_overlays));
    private Gen4Data Gen4View => _gen4View ??= _gen4!.WithDraft(new Dictionary<string, byte[]>(_files), new Dictionary<int, byte[]>(_overlays), _arm9);

    internal IReadOnlyList<NdsCell> Read(NdsSection section) => _gen4 is not null ? NdsCells.Read(Gen4View, section) : NdsCells.Read(Gen5View, section);

    internal NdsPreview PreviewEdits(NdsSection section, IReadOnlyDictionary<string, int> edits) =>
        new(_id, Revision, "Edición manual · " + SectionName(section), null, _gen4 is not null ? NdsCells.Write(Gen4View, section, edits) : NdsCells.Write(Gen5View, section, edits));

    internal NdsPreview PreviewRandom(string action, Gen5RandomizerOptions options) =>
        new(_id, Revision, action, options.Seed, _gen4 is not null ? Gen4Randomizer.Generate(Gen4View, options) : Gen5Randomizer.Generate(Gen5View, options));

    /// <summary>Applies a preview made at the current revision: its tables replace the current ones and it joins the change log.</summary>
    internal void Apply(NdsPreview preview)
    {
        if (preview.Owner != _id || preview.Revision != Revision)
            throw new InvalidOperationException("La vista previa pertenece a otra ROM o quedó desactualizada. Genérala de nuevo.");
        foreach (var (path, data) in preview.Result.Files)
            _files[path] = data;
        foreach (var (id, data) in preview.Result.Overlays)
            _overlays[id] = data;
        _recompress.UnionWith(preview.Result.RecompressOverlays);
        if (preview.Result.Arm9 is not null)
            _arm9 = preview.Result.Arm9;
        _history.Add(new NdsOperation(preview.Action, preview.Seed, preview.Result.Changes));
        Revision++;
        _gen5View = null;
        _gen4View = null;
    }

    /// <summary>Discards every pending change.</summary>
    internal void Reset()
    {
        _files.Clear();
        _overlays.Clear();
        _recompress.Clear();
        _history.Clear();
        _arm9 = null;
        Revision++;
        _gen5View = null;
        _gen4View = null;
    }

    /// <summary>Writes the new ROM and its change log, then re-reads the ROM to confirm the tables still parse.</summary>
    internal string Export(string output)
    {
        if (!HasChanges)
            throw new InvalidOperationException("No hay cambios que exportar.");
        string log = Path.ChangeExtension(output, ".cambios.txt");
        if (File.Exists(log))
            throw new IOException("Ya existe el registro de cambios: " + log);
        var all = _history.SelectMany(o => o.Changes).ToArray();
        var result = new Gen5RandomizerResult(new Dictionary<string, byte[]>(_files), all, new Dictionary<int, byte[]>(_overlays))
        {
            RecompressOverlays = new HashSet<int>(_recompress),
            Arm9 = _arm9,
        };
        int trainers, areas;
        if (_gen4 is not null)
        {
            _gen4.Export(output, result);
            using var exported = NdsRom.Open(output);
            var check = Gen4Data.Open(exported, requireOriginalTables: false);
            (trainers, areas) = (check.Trainers().Count, check.Encounters().Count);
            if (trainers != _gen4.Trainers().Count || areas != _gen4.Encounters().Count)
                throw new InvalidDataException("La ROM exportada no conserva el número de entrenadores o de huecos de encuentro.");
        }
        else
        {
            _gen5!.Export(output, result);
            using var exported = NdsRom.Open(output);
            var check = Gen5Data.Open(exported, requireOriginalTables: false);
            (trainers, areas) = (check.Trainers().Count, check.Encounters().Count);
            if (trainers != _gen5.Trainers().Count || areas != _gen5.Encounters().Count)
                throw new InvalidDataException("La ROM exportada no conserva el número de entrenadores o de áreas.");
        }
        var text = new StringBuilder();
        text.AppendLine($"Juego: {Game} ({Identity.Profile.GameCode}); volcado original verificado: {(Identity.KnownDump ? "sí" : "no")}{(IsRomhack ? "; romhack (tablas modificadas)" : "")}");
        text.AppendLine($"Origen: {SourcePath}");
        text.AppendLine($"Operaciones: {_history.Count}; cambios: {all.Length}");
        foreach (var operation in _history)
        {
            text.AppendLine();
            text.AppendLine($"== {operation.Action}{(operation.Seed is { } seed ? " · semilla " + seed : "")} · {operation.Changes.Count} cambios");
            foreach (var change in operation.Changes)
                text.AppendLine(Describe(change));
        }
        File.WriteAllText(log, text.ToString(), new UTF8Encoding(true));
        return log;
    }

    internal string Describe(Gen5Change change) => change.Field switch
    {
        "Especie" => $"{change.Location}: {Name(SpeciesNames, change.Before)} → {Name(SpeciesNames, change.After)}",
        "Objeto" or "Objeto equipado" => $"{change.Location}: {change.Field} {Name(ItemNames, change.Before)} → {Name(ItemNames, change.After)}",
        _ when change.Field.StartsWith("Producto", StringComparison.Ordinal) => $"{change.Location} · {change.Field}: {Name(ItemNames, change.Before)} → {Name(ItemNames, change.After)}",
        _ when change.Field.StartsWith("Movimiento ", StringComparison.Ordinal) => $"{change.Location} · {change.Field}: {Name(MoveNames, change.Before)} → {Name(MoveNames, change.After)}",
        _ when change.Field.EndsWith("especie", StringComparison.Ordinal) || change.Field is "Planta" or "Fuego" or "Agua" => $"{change.Location} · {change.Field}: {Name(SpeciesNames, change.Before)} → {Name(SpeciesNames, change.After)}",
        _ => $"{change.Location} · {change.Field}: {change.Before} → {change.After}",
    };

    private static string Name(IReadOnlyList<string> names, int id) => id >= 0 && id < names.Count ? names[id] : "#" + id;

    internal static string SectionName(NdsSection section) => section switch
    {
        NdsSection.Starters => "Iniciales",
        NdsSection.Trainers => "Entrenadores",
        NdsSection.Encounters => "Encuentros",
        NdsSection.FieldItems => "Objetos del mapa",
        _ => "Tiendas",
    };

    public void Dispose() => _rom.Dispose();
}
