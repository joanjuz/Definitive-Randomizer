using System.Runtime.CompilerServices;
using pk3DS.Core.NDS;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>
/// Adapts one DS section to the editor contract shared with 3DS and Sword/Shield: entries, fields with their ranges and names,
/// manual saves and randomization proposals. Storage stays in <see cref="NdsProject"/>; this view never writes ROM files.
/// </summary>
internal sealed class NdsRecordEditor : IProjectEditor, ITrainerEditorPresentation, IWildEditorPresentation, ITableEditorPresentation
{
    private static readonly string[] SpeciesModes = ["Aleatorio", "BST similar", "BST progresivo por nivel"];
    private readonly NdsProject _project;
    private readonly NdsSection _section;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly ConditionalWeakTable<EditorProposal, NdsPreview> _proposals = new();
    private IReadOnlyList<NdsCell> _cells = [];
    private int _revision = -1;
    private IReadOnlyList<MoveChoice>? _species, _items, _moves, _types, _abilities;

    internal event Action? Changed;
    internal NdsSection Section => _section;
    public string Title => NdsProject.SectionName(_section);
    public bool UsesTrainerLayout => _section == NdsSection.Trainers;
    public bool UsesWildLayout => _section == NdsSection.Encounters;
    // Long lists of similar values read best as a table, as in Sword/Shield.
    public bool UsesRecordTable => _section is NdsSection.Learnsets or NdsSection.Evolutions or NdsSection.Machines or NdsSection.Shops or NdsSection.EggMoves or NdsSection.Tutors;
    private static bool IsData(NdsSection section) => section >= NdsSection.Pokemon;
    public IReadOnlyList<RecordEntry> Entries { get; private set; } = [];

    internal NdsRecordEditor(NdsProject project, NdsSection section)
    {
        _project = project;
        _section = section;
        Refresh();
    }

    private void Refresh()
    {
        if (_revision == _project.Revision)
            return;
        _cells = _project.Read(_section);
        _revision = _project.Revision;
        Entries = _cells.GroupBy(c => c.Entry).Select(g => new RecordEntry(g.Key, g.First().EntryName)).ToArray();
    }

    private IReadOnlyList<MoveChoice>? Choices(NdsCell cell) => cell.Kind switch
    {
        NdsValueKind.Species => _species ??= Names(_project.SpeciesNames, 0, _project.MaxSpecies),
        NdsValueKind.Item => _items ??= Names(_project.ItemNames, 0, _project.ItemNames.Count - 1),
        NdsValueKind.Move => _moves ??= Names(_project.MoveNames, 0, _project.MoveNames.Count - 1),
        NdsValueKind.Type => _types ??= _project.TypeNames.Select((n, i) => new MoveChoice(i, n)).ToArray(),
        NdsValueKind.Ability => _abilities ??= Names(_project.AbilityNames, 0, _project.AbilityNames.Count - 1),
        NdsValueKind.Option => cell.Options?.Select((n, i) => new MoveChoice(i, n)).ToArray(),
        _ => null,
    };

    private static IReadOnlyList<MoveChoice> Names(IReadOnlyList<string> names, int first, int last) =>
        Enumerable.Range(first, Math.Max(0, last - first + 1)).Select(i => new MoveChoice(i, i == 0 ? "—" : i < names.Count ? names[i] : "#" + i)).ToArray();

    public IReadOnlyList<EditorGroup> Read(int id)
    {
        Refresh();
        var rows = _cells.Where(c => c.Entry == id).ToArray();
        if (rows.Length == 0)
            throw new ArgumentException("Entrada desconocida: " + id);
        return rows.GroupBy(c => c.Group).Select(g => new EditorGroup(g.Key, g.Select(c =>
        {
            var choices = Choices(c);
            // Lists only offer what the cell's range allows (for example, no species 0 in a trainer team).
            return new EditorField(c.Local, c.Field, c.Value, c.Min, c.Max, choices?.Where(m => m.Id >= c.Min && m.Id <= c.Max).ToArray());
        }).ToArray())).ToArray();
    }

    public void Save(int id, IReadOnlyDictionary<string, int> values)
    {
        if (_revision != _project.Revision)
            throw new InvalidOperationException("La ROM cambió desde que se abrió esta entrada. Vuelve a seleccionarla antes de guardar.");
        var rows = _cells.Where(c => c.Entry == id).ToArray();
        EditorValues.Validate(Read(id), values);
        var edits = rows.Where(c => values.TryGetValue(c.Local, out int v) && v != c.Value).ToDictionary(c => c.Key, c => values[c.Local]);
        if (edits.Count == 0)
            return;
        _project.Apply(_project.PreviewEdits(_section, edits));
        Refresh();
        Changed?.Invoke();
    }

    public IReadOnlyList<EditorGroup> RandomOptions => _section switch
    {
        // Keys follow the shared trainer tabs (RecordEditorView.TrainerOptionCategory): levels, moves, items/AI/battles and the BST table.
        NdsSection.Trainers =>
        [
            new EditorGroup("Pokémon y equipos", [new EditorField("Mode", "Especies", 1, 0, 2, Choices(SpeciesChoices)), Flag("Theme", "Un tipo por entrenador", 0), Flag("NoLegend", "Sin legendarios", 0),
                new EditorField("TeamSize", "Pokémon mínimos por equipo (0 = sin cambios)", 0, 0, 6), Flag("MaxIVs", "IV al máximo", 0),
                Flag("Names", "Barajar nombres de entrenador", 0), Flag("Titles", "Barajar clases de entrenador", 0)]),
            new EditorGroup("Niveles de entrenadores", [new EditorField("Levels", "Niveles (+/- %)", 0, -50, 100), new EditorField("EvolvedLevel", "Forzar evolución final desde el nivel (0 = no)", 0, 0, 100)]),
            new EditorGroup("Movimientos", [new EditorField("CB_Moves", "Movimientos de los entrenadores", 0, 0, 4,
                Choices("Sin cambios", "Los de su nivel (Level Up)", "Mejores movesets (más fuertes, STAB, MT)", "Aleatorios", "Solo Metrónomo"))]),
            new EditorGroup("IA, objetos y combates", [new EditorField("HeldItems", "Objetos equipados", 0, 0, 3, Choices("Sin cambios", "Aleatorios", "Inteligentes (según tipo y estadísticas)", "Ninguno")),
                Flag("MaxAI", "IA al máximo", 0),
                new EditorField("DoubleChance", _project.DoublesProblem is { } problem ? "Combates dobles · no disponible: " + problem : _project.Generation == 5
                    ? "Combates dobles (%) · experimental en Gen5" : "Combates dobles (%)", 0, 0, _project.DoublesProblem is null ? 100 : 0)]),
            new EditorGroup("BST progresivo", [Flag("CHK_ProgressiveBST", "Usar rangos BST por nivel", 1)]),
            .. BstRanges(),
        ],
        NdsSection.Encounters =>
        [
            new EditorGroup(Title, [new EditorField("Mode", "Especies", 1, 0, 2, Choices(SpeciesChoices)), Flag("Progressive", "BST progresivo por nivel (tabla)", 0)]),
            new EditorGroup("Al estilo de Universal Randomizer", [Flag("Global", "Mapeo global de especies", 0), Flag("NoLegend", "Sin legendarios", 0), new EditorField("Levels", "Niveles (+/- %)", 0, -50, 100)]),
            .. BstRanges(),
        ],
        NdsSection.Statics => [new EditorGroup(Title, [new EditorField("Mode", "Especies", 2, 0, 2, Choices("Aleatorias", "BST similar", "Legendarios por legendarios"))])],
        NdsSection.Trades => [new EditorGroup(Title, [Flag("Given", "Randomizar el Pokémon que entregan", 1), Flag("Requested", "Randomizar el Pokémon que piden", 1)])],
        NdsSection.Starters => [new EditorGroup(Title, [new EditorField("Random", "Iniciales aleatorios (tres especies distintas)", 1, 1, 1),
            // Diamond and Platinum give the starter with an item; the section then has an "Item" cell.
            .. _cells.Any(c => c.Local == "Item") ? [Flag("Item", "Objeto aleatorio para el inicial", 0)] : Array.Empty<EditorField>()])],
        NdsSection.EggMoves => [new EditorGroup(Title, [new EditorField("Mode", "Movimientos huevo", 0, 0, 1, Choices("Aleatorios", "Solo Metrónomo")),
            new EditorField("SameType", "Del mismo tipo que el Pokémon (%)", 50, 0, 100)])],
        NdsSection.Tutors => [new EditorGroup(Title, [new EditorField("Mode", "Compatibilidad", 0, 0, 2, Choices("Aleatoria", "Por tipo (mismo tipo siempre)", "Completa")),
            new EditorField("Percent", "Probabilidad (%)", 50, 0, 100)])],
        NdsSection.MoveLists => [new EditorGroup(Title,
        [
            .. _cells.Any(c => c.Entry == 0) ? [Flag("TMs", "Movimientos de las MT aleatorios (las MO no cambian)", 1)] : Array.Empty<EditorField>(),
            .. _cells.Any(c => c.Entry == 1) ? [Flag("Tutors", "Movimientos de los tutores aleatorios", 1)] : Array.Empty<EditorField>(),
            Flag("KeepField", "Conservar movimientos de campo (Destello, Golpe Cabeza…)", 1),
        ])],
        NdsSection.Pokemon =>
        [
            new EditorGroup("Estadísticas base", [new EditorField("Stats", "Estadísticas", 0, 0, 2, Choices("Sin cambios", "Barajar (mismo total)", "Aleatorias (mismo total)"))]),
            new EditorGroup("Tipos y habilidades", [Flag("Types", "Tipos aleatorios", 0), Flag("Abilities", "Habilidades aleatorias (sin Superguarda, Multitipo…)", 0), Flag("Follow", "Las evoluciones heredan tipos y habilidades", 1)]),
            new EditorGroup("Captura, experiencia y objetos", [new EditorField("MinCatch", "Ratio de captura mínimo (0 = sin cambios)", 0, 0, 255),
                Flag("Growth", "Crecimiento medio rápido (legendarios lento)", 0), Flag("WildItems", "Objetos salvajes aleatorios", 0)]),
        ],
        NdsSection.Moves => [new EditorGroup(Title, [Flag("Power", "Poder aleatorio (20–150)", 1), Flag("Accuracy", "Precisión aleatoria (50–100)", 0), Flag("PP", "PP aleatorios (5–40)", 0), Flag("Types", "Tipos aleatorios", 0)])],
        NdsSection.Learnsets => [new EditorGroup(Title, [new EditorField("Mode", "Movimientos por nivel", 0, 0, 1, Choices("Aleatorios", "Solo Metrónomo")), new EditorField("SameType", "Del mismo tipo que el Pokémon (%)", 50, 0, 100)])],
        NdsSection.Evolutions => [new EditorGroup(Title, [Flag("Trade", "Evoluciones por intercambio → nivel 37", 1), Flag("Random", "Evoluciones aleatorias de fuerza similar", 0),
            Flag("Time", "Quitar evoluciones por hora del día", 0), new EditorField("MaxLevel", "Nivel máximo de evolución (0 = sin cambios)", 0, 0, 100)])],
        NdsSection.Machines => [new EditorGroup(Title, [new EditorField("Mode", "Compatibilidad", 0, 0, 2, Choices("Aleatoria", "Por tipo (mismo tipo siempre)", "Completa")), new EditorField("Percent", "Probabilidad (%)", 50, 0, 100), Flag("KeepHms", "Conservar la compatibilidad de las MO", 1)])],
        _ => [new EditorGroup(Title, [new EditorField("Shuffle", "Barajar (solo objetos normales; MT, MO y objetos clave no se mueven)", 1, 1, 1)])],
    };

    private static EditorField Flag(string key, string label, int value) => new(key, label, value, 0, 1);

    private static readonly string[] SpeciesChoices = ["Aleatorias", "BST similar", "Sin cambios (solo las demás opciones)"];

    // The editable BST table of 3DS and Switch, read back by key prefix "BST.n.".
    private static IEnumerable<EditorGroup> BstRanges() => Gen5BstRange.Defaults.Select((r, i) => new EditorGroup("Rango " + (i + 1),
    [
        new EditorField($"BST.{i}.MinLevel", "Nivel mínimo", r.MinLevel, 1, 100), new EditorField($"BST.{i}.MaxLevel", "Nivel máximo", r.MaxLevel, 1, 100),
        new EditorField($"BST.{i}.MinBST", "BST mínimo", r.MinBst, 0, 1530), new EditorField($"BST.{i}.MaxBST", "BST máximo", r.MaxBst, 0, 1530), Flag($"BST.{i}.FullRandom", "Full Random", 0),
    ]));

    private static IReadOnlyList<Gen5BstRange> ReadRanges(IReadOnlyDictionary<string, int> options)
    {
        var ranges = options.Where(p => p.Key.StartsWith("BST.", StringComparison.Ordinal)).GroupBy(p => int.Parse(p.Key.Split('.')[1])).OrderBy(g => g.Key)
            .Select(g => { int V(string name) => options[$"BST.{g.Key}.{name}"]; return new Gen5BstRange(V("MinLevel"), V("MaxLevel"), V("MinBST"), V("MaxBST"), V("FullRandom") == 1); }).ToArray();
        return ranges.Length > 0 ? ranges : Gen5BstRange.Defaults;
    }
    private static IReadOnlyList<MoveChoice> Choices(params string[] names) => names.Select((n, i) => new MoveChoice(i, n)).ToArray();

    public EditorProposal Preview(IReadOnlyDictionary<string, int> options, IReadOnlyList<int> scope)
    {
        // The BST table may have more or fewer rows than the defaults; ProgressiveBstTable checks it.
        EditorValues.Validate(RandomOptions.Select(g => g with { Fields = g.Fields.Where(f => !f.Key.StartsWith("BST.", StringComparison.Ordinal)).ToArray() }), options);
        Refresh();
        if (scope.Count == 0)
            throw new ArgumentException("No hay entradas en el alcance elegido.");
        uint seed = SwitchSeed.New32();
        if (IsData(_section))
            return Propose(DataPreview(options, scope, seed), seed);
        int mode = options.GetValueOrDefault("Mode", 1);
        bool progressive = options.GetValueOrDefault(_section == NdsSection.Trainers ? "CHK_ProgressiveBST" : "Progressive") == 1;
        // "Sin cambios" keeps species and only applies the other options; the BST table replaces "BST similar".
        var species = _section is not (NdsSection.Trainers or NdsSection.Encounters) ? Gen5SpeciesMode.Random
            : mode == 2 ? Gen5SpeciesMode.Keep : progressive ? Gen5SpeciesMode.ProgressiveBst : mode == 0 ? Gen5SpeciesMode.Random : Gen5SpeciesMode.SimilarBst;
        var ranges = ReadRanges(options);
        // The whole section is one scope value: null keeps the randomizers' original behaviour.
        IReadOnlySet<int>? chosen = scope.Count == Entries.Count ? null : scope.ToHashSet();
        var config = _section switch
        {
            NdsSection.Trainers => new Gen5RandomizerOptions(seed) { Trainers = species, Scope = chosen, TypeThemedTrainers = options.GetValueOrDefault("Theme") == 1,
                ForceFullyEvolvedLevel = options.GetValueOrDefault("EvolvedLevel"), TrainerLevelPercent = options.GetValueOrDefault("Levels"), BstRanges = ranges, BanLegendaries = options.GetValueOrDefault("NoLegend") == 1 },
            NdsSection.Encounters => new Gen5RandomizerOptions(seed) { Wild = species, Scope = chosen, WildGlobalMapping = options.GetValueOrDefault("Global") == 1,
                WildLevelPercent = options.GetValueOrDefault("Levels"), BstRanges = ranges, BanLegendaries = options.GetValueOrDefault("NoLegend") == 1 },
            NdsSection.Starters => new Gen5RandomizerOptions(seed) { Starters = Gen5StarterMode.Random, RandomStarterItem = options.GetValueOrDefault("Item") == 1 },
            NdsSection.FieldItems => new Gen5RandomizerOptions(seed) { FieldItems = Gen5ItemMode.Shuffle, Scope = chosen },
            _ => new Gen5RandomizerOptions(seed) { Shops = Gen5ItemMode.Shuffle, Scope = chosen },
        };
        string action = "Randomizar " + Title.ToLowerInvariant() + (_section is NdsSection.Trainers or NdsSection.Encounters && species != Gen5SpeciesMode.Keep ? " · " + SpeciesModes[(int)species - 1] : "");
        if (_section == NdsSection.Trainers)
        {
            var tools = new NdsTrainerOptions(seed ^ 0x5EED)
            {
                MinTeamSize = options.GetValueOrDefault("TeamSize"), Moves = (NdsTrainerMoveMode)options.GetValueOrDefault("CB_Moves"), Items = (NdsTrainerItemMode)options.GetValueOrDefault("HeldItems"),
                MaxIvs = options.GetValueOrDefault("MaxIVs") == 1, MaxAi = options.GetValueOrDefault("MaxAI") == 1, DoubleChance = options.GetValueOrDefault("DoubleChance"), Scope = chosen,
                ShuffleNames = options.GetValueOrDefault("Names") == 1, ShuffleClassNames = options.GetValueOrDefault("Titles") == 1,
            };
            return Propose(_project.PreviewTrainers(action, (Gen5RandomizerOptions)config, tools), seed);
        }
        return Propose(_project.PreviewRandom(action, config), seed);
    }

    private NdsPreview DataPreview(IReadOnlyDictionary<string, int> options, IReadOnlyList<int> scope, uint seed)
    {
        int V(string key, int fallback = 0) => options.GetValueOrDefault(key, fallback);
        IReadOnlySet<int>? chosen = scope.Count == Entries.Count ? null : scope.ToHashSet();
        var config = _section switch
        {
            NdsSection.Pokemon => new NdsDataOptions(seed) { Stats = (NdsStatsMode)V("Stats"), Types = V("Types") == 1, Abilities = V("Abilities") == 1, FollowEvolutions = V("Follow", 1) == 1, MinCatchRate = V("MinCatch"),
                StandardizeGrowth = V("Growth") == 1, WildHeldItems = V("WildItems") == 1, Scope = chosen },
            NdsSection.Moves => new NdsDataOptions(seed) { MovePower = V("Power") == 1, MoveAccuracy = V("Accuracy") == 1, MovePp = V("PP") == 1, MoveTypes = V("Types") == 1, Scope = chosen },
            NdsSection.Learnsets => new NdsDataOptions(seed) { Learnsets = V("Mode") == 1 ? NdsLearnsetMode.Metronome : NdsLearnsetMode.Random, SameTypePercent = V("SameType", 50), Scope = chosen },
            NdsSection.Evolutions => new NdsDataOptions(seed) { RemoveTradeEvolutions = V("Trade") == 1, RandomEvolutions = V("Random") == 1, RemoveTimeEvolutions = V("Time") == 1,
                MaxEvolutionLevel = V("MaxLevel"), Scope = chosen },
            NdsSection.Statics => new NdsDataOptions(seed) { Statics = (NdsStaticMode)(V("Mode", 2) + 1), Scope = chosen },
            NdsSection.Trades => new NdsDataOptions(seed) { TradeGiven = V("Given") == 1, TradeRequested = V("Requested") == 1, Scope = chosen },
            NdsSection.EggMoves => new NdsDataOptions(seed) { EggMoves = V("Mode") == 1 ? NdsLearnsetMode.Metronome : NdsLearnsetMode.Random, SameTypePercent = V("SameType", 50), Scope = chosen },
            NdsSection.Tutors => new NdsDataOptions(seed) { Tutors = (NdsMachineMode)(V("Mode") + 1), TutorPercent = V("Percent", 50), Scope = chosen },
            NdsSection.MoveLists => new NdsDataOptions(seed) { RandomTmMoves = V("TMs") == 1, RandomTutorMoves = V("Tutors") == 1, KeepFieldMoves = V("KeepField", 1) == 1, Scope = chosen },
            _ => new NdsDataOptions(seed) { Machines = (NdsMachineMode)(V("Mode") + 1), MachinePercent = V("Percent", 50), KeepHms = V("KeepHms", 1) == 1, Scope = chosen },
        };
        return _project.PreviewData("Randomizar " + Title, _section, config);
    }

    private EditorProposal Propose(NdsPreview preview, uint seed)
    {
        string action = preview.Action;
        var changes = preview.Result.Changes.Select(c => new BatchChange(0, c.Location, c.Field, Label(c, c.Before), Label(c, c.After))).ToArray();
        var proposal = new EditorProposal(_owner, action, unchecked((int)seed), [], changes,
            new Dictionary<string, string> { ["Platform"] = "NDS", ["Game"] = _project.Identity.Profile.GameCode, ["Seed"] = seed.ToString() });
        _proposals.Add(proposal, preview);
        return proposal;
    }

    // Change values shown with their names, as in the export log.
    private string Label(Gen5Change change, int value)
    {
        string text = _project.Describe(change with { Before = value, After = value });
        int arrow = text.LastIndexOf(" → ", StringComparison.Ordinal);
        return arrow >= 0 ? text[(arrow + 3)..] : value.ToString();
    }

    public void Apply(EditorProposal proposal)
    {
        if (proposal.Owner != _owner || !_proposals.TryGetValue(proposal, out var preview))
            throw new InvalidOperationException("Propuesta ajena a este editor.");
        _project.Apply(preview);
        _proposals.Remove(proposal);
        Refresh();
        Changed?.Invoke();
    }
}
