using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>One applied edit or randomization, kept for the change log; <see cref="Recipe"/> replays it (Global Template, Batch).</summary>
internal sealed record NdsOperation(string Action, uint? Seed, IReadOnlyList<Gen5Change> Changes, JsonObject Recipe);

/// <summary>A computed change waiting to be applied; it belongs to one project revision and is rejected once that changes.</summary>
internal sealed class NdsPreview
{
    internal Guid Owner { get; }
    internal int Revision { get; }
    internal string Action { get; }
    internal uint? Seed { get; }
    internal Gen5RandomizerResult Result { get; }
    internal JsonObject Recipe { get; }
    internal NdsPreview(Guid owner, int revision, string action, uint? seed, Gen5RandomizerResult result, JsonObject recipe)
    { Owner = owner; Revision = revision; Action = action; Seed = seed; Result = result; Recipe = recipe; }
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
    internal IReadOnlyList<string> TypeNames => Source.TypeNames;
    internal IReadOnlyList<string> AbilityNames => Source.AbilityNames;
    /// <summary>Names, layouts and the TM list of the open game (read once: they do not change with edits).</summary>
    internal NdsDataSource Source => _source ??= _gen4 is not null ? NdsDataSource.Of(_gen4) : NdsDataSource.Of(_gen5!);
    private NdsDataSource? _source;

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
        new(_id, Revision, "Edición manual · " + SectionName(section), null, _gen4 is not null ? NdsCells.Write(Gen4View, section, edits) : NdsCells.Write(Gen5View, section, edits),
            new JsonObject { ["Kind"] = "Edit", ["Section"] = section.ToString(), ["Edits"] = JsonSerializer.SerializeToNode(edits.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value)) });

    internal NdsPreview PreviewRandom(string action, Gen5RandomizerOptions options) =>
        new(_id, Revision, action, options.Seed, _gen4 is not null ? Gen4Randomizer.Generate(Gen4View, options) : Gen5Randomizer.Generate(Gen5View, options),
            new JsonObject { ["Kind"] = "Species", ["Action"] = action, ["Options"] = JsonSerializer.SerializeToNode(options, RecipeJson) });

    /// <summary>Why game tweaks cannot be applied to this ROM, or null when they can (Gen4 only).</summary>
    internal string? TweaksProblem => _gen4 is null
        ? "Negro, Blanco y Blanco 2 guardan estos ajustes en el ARM9, protegido por las firmas DSi, y el del Pokédex Nacional comparte script con los iniciales"
        : NdsTweaks.Problem(_gen4);
    internal bool IsPlatinum => _gen4?.Profile.Kind == Gen4Kind.Platinum;

    internal NdsPreview PreviewTweaks(NdsTweakOptions options) =>
        new(_id, Revision, "Ajustes del juego", null, NdsTweaks.Apply(Gen4View, options),
            new JsonObject { ["Kind"] = "Tweaks", ["Options"] = JsonSerializer.SerializeToNode(options, RecipeJson) });

    internal NdsPreview PreviewData(string action, NdsSection section, NdsDataOptions options) =>
        new(_id, Revision, action, options.Seed, _gen4 is not null ? NdsDataRandomizer.Generate(Gen4View, section, options) : NdsDataRandomizer.Generate(Gen5View, section, options),
            new JsonObject { ["Kind"] = "Data", ["Section"] = section.ToString(), ["Action"] = action, ["Options"] = JsonSerializer.SerializeToNode(options, RecipeJson) });

    // ---- Global Template and Batch ----

    internal const string GlobalFormat = "DefinitiveRandomizer.NdsGlobal";
    private static readonly JsonSerializerOptions RecipeJson = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    /// <summary>The applied operations of this ROM as a replayable recipe, tied to its game code.</summary>
    internal JsonObject CaptureGlobal() => new()
    {
        ["Format"] = GlobalFormat,
        ["Version"] = 1,
        ["Game"] = Identity.Profile.GameCode,
        ["Title"] = Game,
        ["Operations"] = new JsonArray(_history.Select(o => (JsonNode)o.Recipe.DeepClone()).ToArray()),
    };

    internal static JsonObject ReadGlobal(string path)
    {
        var document = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("El archivo no es un Global Template de DS.");
        if (document["Format"]?.GetValue<string>() != GlobalFormat || document["Operations"] is not JsonArray)
            throw new InvalidDataException("El archivo no es un Global Template de Nintendo DS (" + GlobalFormat + ").");
        return document;
    }

    internal static void WriteGlobal(string path, JsonObject document) => File.WriteAllText(path, document.ToJsonString(RecipeJson), new UTF8Encoding(false));

    /// <summary>
    /// Replays a template's operations in order on this ROM, each previewed and applied as if done by hand. With
    /// <paramref name="newSeeds"/>, randomizations draw a new seed (Batch); otherwise they reuse the recorded one.
    /// </summary>
    internal int ApplyGlobal(JsonObject document, bool newSeeds)
    {
        string game = document["Game"]?.GetValue<string>() ?? "";
        if (!string.Equals(game, Identity.Profile.GameCode, StringComparison.Ordinal))
            throw new InvalidOperationException($"El template es de {game} y la ROM abierta es {Identity.Profile.GameCode}: las entradas y claves no coinciden entre juegos.");
        int applied = 0;
        foreach (var node in (JsonArray)document["Operations"]!)
        {
            var op = node as JsonObject ?? throw new InvalidDataException("Operación inválida en el template.");
            Apply(PreviewOperation(op, newSeeds));
            applied++;
        }
        return applied;
    }

    private NdsPreview PreviewOperation(JsonObject op, bool newSeed)
    {
        string kind = op["Kind"]?.GetValue<string>() ?? "";
        NdsSection Section() => Enum.Parse<NdsSection>(op["Section"]!.GetValue<string>());
        // The scope (a set, which System.Text.Json cannot create as IReadOnlySet) is read apart and set back with "with".
        HashSet<int>? scope = null;
        JsonNode Options()
        {
            var options = (JsonObject)op["Options"]!.DeepClone();
            if (newSeed)
                options["Seed"] = pk3DS.Core.NX.SwitchSeed.New32();
            scope = options["Scope"]?.Deserialize<HashSet<int>>();
            options.Remove("Scope");
            return options;
        }
        string action = op["Action"]?.GetValue<string>() ?? kind;
        return kind switch
        {
            "Edit" => PreviewEdits(Section(), op["Edits"]!.Deserialize<Dictionary<string, int>>()!),
            "Species" => PreviewRandom(action, Options().Deserialize<Gen5RandomizerOptions>(RecipeJson)! with { Scope = scope }),
            "Data" => PreviewData(action, Section(), Options().Deserialize<NdsDataOptions>(RecipeJson)! with { Scope = scope }),
            "Tweaks" => PreviewTweaks(op["Options"]!.Deserialize<NdsTweakOptions>(RecipeJson)!),
            _ => throw new InvalidDataException("Tipo de operación desconocido en el template: " + kind),
        };
    }

    /// <summary>
    /// Batch ROM Builder: <paramref name="count"/> new ROMs from the source ROM, each with the template replayed with new seeds,
    /// written as name-N.nds with its change log. Files that exist are never overwritten.
    /// </summary>
    internal static IReadOnlyList<string> BuildBatch(string source, bool allowRomhack, JsonObject document, int count, string folder, string name, IProgress<string>? progress, CancellationToken cancel)
    {
        if (count is < 1 or > 20)
            throw new ArgumentException("Entre 1 y 20 ROMs por lote.");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("La carpeta de salida no existe: " + folder);
        var targets = Enumerable.Range(1, count).Select(i => Path.Combine(folder, $"{name}-{i}.nds")).ToArray();
        if (targets.FirstOrDefault(t => File.Exists(t) || File.Exists(Path.ChangeExtension(t, ".cambios.txt"))) is { } existing)
            throw new IOException("Ya existe " + existing + "; elige otro nombre o carpeta.");
        var written = new List<string>();
        foreach (string target in targets)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report($"Generando {Path.GetFileName(target)} ({written.Count + 1} de {count})…");
            using var project = Open(source, allowRomhack);
            project.ApplyGlobal(document, newSeeds: true);
            project.Export(target);
            written.Add(target);
        }
        return written;
    }

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
        _history.Add(new NdsOperation(preview.Action, preview.Seed, preview.Result.Changes, preview.Recipe));
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
        "Especie entregada" or "Especie pedida" => $"{change.Location} · {change.Field}: {Name(SpeciesNames, change.Before)} → {Name(SpeciesNames, change.After)}",
        "Movimiento" => $"{change.Location} · {change.Field}: {Name(MoveNames, change.Before)} → {Name(MoveNames, change.After)}",
        "Tipo" or "Tipo 1" or "Tipo 2" => $"{change.Location} · {change.Field}: {Name(TypeNames, change.Before)} → {Name(TypeNames, change.After)}",
        "Categoría" => $"{change.Location} · {change.Field}: {Name(Source.Categories, change.Before)} → {Name(Source.Categories, change.After)}",
        "Crecimiento" => $"{change.Location} · {change.Field}: {Name(NdsDataSource.GrowthNames, change.Before)} → {Name(NdsDataSource.GrowthNames, change.After)}",
        "Método" => $"{change.Location} · {change.Field}: {Name(Source.EvolutionMethods, change.Before)} → {Name(Source.EvolutionMethods, change.After)}",
        "Objeto común" or "Objeto raro" or "Objeto (hierba oscura)" => $"{change.Location} · {change.Field}: {Name(ItemNames, change.Before)} → {Name(ItemNames, change.After)}",
        _ when change.Field.StartsWith("Bytes del", StringComparison.Ordinal) => $"{change.Location}: {change.After} {change.Field.ToLowerInvariant()} cambiados",
        _ when change.Field.StartsWith("Habilidad", StringComparison.Ordinal) => $"{change.Location} · {change.Field}: {Name(AbilityNames, change.Before)} → {Name(AbilityNames, change.After)}",
        _ when change.Field.StartsWith("MT", StringComparison.Ordinal) || change.Field.StartsWith("MO", StringComparison.Ordinal) => $"{change.Location} · {change.Field}: {(change.Before == 1 ? "sí" : "no")} → {(change.After == 1 ? "sí" : "no")}",
        "Objeto" or "Objeto equipado" => $"{change.Location}: {change.Field} {Name(ItemNames, change.Before)} → {Name(ItemNames, change.After)}",
        _ when change.Field.StartsWith("Producto", StringComparison.Ordinal) => $"{change.Location} · {change.Field}: {Name(ItemNames, change.Before)} → {Name(ItemNames, change.After)}",
        _ when change.Field.StartsWith("Movimiento ", StringComparison.Ordinal) => $"{change.Location} · {change.Field}: {Name(MoveNames, change.Before)} → {Name(MoveNames, change.After)}",
        _ when change.Field.EndsWith("especie", StringComparison.Ordinal) || change.Field is "Planta" or "Fuego" or "Agua" => $"{change.Location} · {change.Field}: {Name(SpeciesNames, change.Before)} → {Name(SpeciesNames, change.After)}",
        _ => $"{change.Location} · {change.Field}: {change.Before} → {change.After}",
    };

    private static string Name(IReadOnlyList<string> names, int id) => id >= 0 && id < names.Count ? names[id] : "#" + id;

    internal static string SectionName(NdsSection section) => section switch
    {
        NdsSection.Pokemon => "Pokémon",
        NdsSection.Moves => "Movimientos",
        NdsSection.Learnsets => "Level Up",
        NdsSection.Evolutions => "Evoluciones",
        NdsSection.Machines => "MT/MO",
        NdsSection.Statics => "Estáticos y regalos",
        NdsSection.Trades => "Intercambios",
        NdsSection.Starters => "Iniciales",
        NdsSection.Trainers => "Entrenadores",
        NdsSection.Encounters => "Encuentros",
        NdsSection.FieldItems => "Objetos del mapa",
        _ => "Tiendas",
    };

    public void Dispose() => _rom.Dispose();
}
