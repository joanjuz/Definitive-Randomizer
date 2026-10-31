using System.Runtime.CompilerServices;
using pk3DS.Core.NDS;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>
/// Adapts one DS section to the editor contract shared with 3DS and Sword/Shield: entries, fields with their ranges and names,
/// manual saves and randomization proposals. Storage stays in <see cref="NdsProject"/>; this view never writes ROM files.
/// </summary>
internal sealed class NdsRecordEditor : IProjectEditor, ITrainerEditorPresentation, IWildEditorPresentation
{
    private static readonly string[] SpeciesModes = ["Aleatorio", "BST similar", "BST progresivo por nivel"];
    private readonly NdsProject _project;
    private readonly NdsSection _section;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly ConditionalWeakTable<EditorProposal, NdsPreview> _proposals = new();
    private IReadOnlyList<NdsCell> _cells = [];
    private int _revision = -1;
    private IReadOnlyList<MoveChoice>? _species, _items, _moves;

    internal event Action? Changed;
    internal NdsSection Section => _section;
    public string Title => NdsProject.SectionName(_section);
    public bool UsesTrainerLayout => _section == NdsSection.Trainers;
    public bool UsesWildLayout => _section == NdsSection.Encounters;
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
        _ => [new EditorGroup(Title, [new EditorField("Shuffle", "Barajar (solo objetos normales; MT, MO y objetos clave no se mueven)", 1, 1, 1)])],
    };

    public EditorProposal Preview(IReadOnlyDictionary<string, int> options, IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions, options);
        Refresh();
        if (scope.Count == 0)
            throw new ArgumentException("No hay entradas en el alcance elegido.");
        uint seed = SwitchSeed.New32();
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
        var preview = _project.PreviewRandom(action, config);
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
