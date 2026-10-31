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
    public bool UsesRecordTable => _section is NdsSection.Learnsets or NdsSection.Evolutions or NdsSection.Machines or NdsSection.Shops;
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
        NdsSection.Trainers or NdsSection.Encounters =>
        [
            new EditorGroup(Title, [new EditorField("Mode", "Especies", 2, 0, 2, SpeciesModes.Select((m, i) => new MoveChoice(i, m)).ToArray())]),
        ],
        NdsSection.Starters => [new EditorGroup(Title, [new EditorField("Random", "Iniciales aleatorios (tres especies distintas)", 1, 1, 1)])],
        NdsSection.Pokemon =>
        [
            new EditorGroup("Estadísticas base", [new EditorField("Stats", "Estadísticas", 0, 0, 2, Choices("Sin cambios", "Barajar (mismo total)", "Aleatorias (mismo total)"))]),
            new EditorGroup("Tipos y habilidades", [Flag("Types", "Tipos aleatorios", 0), Flag("Abilities", "Habilidades aleatorias (sin Superguarda, Multitipo…)", 0), Flag("Follow", "Las evoluciones heredan tipos y habilidades", 1)]),
            new EditorGroup("Captura", [new EditorField("MinCatch", "Ratio de captura mínimo (0 = sin cambios)", 0, 0, 255)]),
        ],
        NdsSection.Moves => [new EditorGroup(Title, [Flag("Power", "Poder aleatorio (20–150)", 1), Flag("Accuracy", "Precisión aleatoria (50–100)", 0), Flag("PP", "PP aleatorios (5–40)", 0), Flag("Types", "Tipos aleatorios", 0)])],
        NdsSection.Learnsets => [new EditorGroup(Title, [new EditorField("Mode", "Movimientos por nivel", 0, 0, 1, Choices("Aleatorios", "Solo Metrónomo")), new EditorField("SameType", "Del mismo tipo que el Pokémon (%)", 50, 0, 100)])],
        NdsSection.Evolutions => [new EditorGroup(Title, [Flag("Trade", "Evoluciones por intercambio → nivel 37", 1), Flag("Random", "Evoluciones aleatorias de fuerza similar", 0)])],
        NdsSection.Machines => [new EditorGroup(Title, [new EditorField("Mode", "Compatibilidad", 0, 0, 2, Choices("Aleatoria", "Por tipo (mismo tipo siempre)", "Completa")), new EditorField("Percent", "Probabilidad (%)", 50, 0, 100), Flag("KeepHms", "Conservar la compatibilidad de las MO", 1)])],
        _ => [new EditorGroup(Title, [new EditorField("Shuffle", "Barajar (solo objetos normales; MT, MO y objetos clave no se mueven)", 1, 1, 1)])],
    };

    private static EditorField Flag(string key, string label, int value) => new(key, label, value, 0, 1);
    private static IReadOnlyList<MoveChoice> Choices(params string[] names) => names.Select((n, i) => new MoveChoice(i, n)).ToArray();

    public EditorProposal Preview(IReadOnlyDictionary<string, int> options, IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions, options);
        Refresh();
        if (scope.Count == 0)
            throw new ArgumentException("No hay entradas en el alcance elegido.");
        uint seed = SwitchSeed.New32();
        if (IsData(_section))
            return Propose(DataPreview(options, scope, seed), seed);
        var species = (Gen5SpeciesMode)(options.GetValueOrDefault("Mode", 2) + 1);
        // The whole section is one scope value: null keeps the randomizers' original behaviour.
        IReadOnlySet<int>? chosen = scope.Count == Entries.Count ? null : scope.ToHashSet();
        var config = _section switch
        {
            NdsSection.Trainers => new Gen5RandomizerOptions(seed) { Trainers = species, Scope = chosen },
            NdsSection.Encounters => new Gen5RandomizerOptions(seed) { Wild = species, Scope = chosen },
            NdsSection.Starters => new Gen5RandomizerOptions(seed) { Starters = Gen5StarterMode.Random },
            NdsSection.FieldItems => new Gen5RandomizerOptions(seed) { FieldItems = Gen5ItemMode.Shuffle, Scope = chosen },
            _ => new Gen5RandomizerOptions(seed) { Shops = Gen5ItemMode.Shuffle, Scope = chosen },
        };
        string action = "Randomizar " + Title.ToLowerInvariant() + (_section is NdsSection.Trainers or NdsSection.Encounters ? " · " + SpeciesModes[(int)species - 1] : "");
        return Propose(_project.PreviewRandom(action, config), seed);
    }

    private NdsPreview DataPreview(IReadOnlyDictionary<string, int> options, IReadOnlyList<int> scope, uint seed)
    {
        int V(string key, int fallback = 0) => options.GetValueOrDefault(key, fallback);
        IReadOnlySet<int>? chosen = scope.Count == Entries.Count ? null : scope.ToHashSet();
        var config = _section switch
        {
            NdsSection.Pokemon => new NdsDataOptions(seed) { Stats = (NdsStatsMode)V("Stats"), Types = V("Types") == 1, Abilities = V("Abilities") == 1, FollowEvolutions = V("Follow", 1) == 1, MinCatchRate = V("MinCatch"), Scope = chosen },
            NdsSection.Moves => new NdsDataOptions(seed) { MovePower = V("Power") == 1, MoveAccuracy = V("Accuracy") == 1, MovePp = V("PP") == 1, MoveTypes = V("Types") == 1, Scope = chosen },
            NdsSection.Learnsets => new NdsDataOptions(seed) { Learnsets = V("Mode") == 1 ? NdsLearnsetMode.Metronome : NdsLearnsetMode.Random, SameTypePercent = V("SameType", 50), Scope = chosen },
            NdsSection.Evolutions => new NdsDataOptions(seed) { RemoveTradeEvolutions = V("Trade") == 1, RandomEvolutions = V("Random") == 1, Scope = chosen },
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
