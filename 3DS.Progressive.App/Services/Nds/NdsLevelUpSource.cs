using System.Runtime.CompilerServices;
using pk3DS.Core.NDS;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>
/// The 3DS Level Up view over a DS project: lists of any length per species, saved as one stacked operation each; the bulk
/// randomizer is the DS learnset randomizer with the 3DS options, and "Metrónomo · todos" leaves Metronome at level 1, as in 3DS.
/// </summary>
internal sealed class NdsLevelUpSource : ILevelUpSource
{
    private const int Metronome = 118;
    private readonly NdsProject _project;
    private readonly ConditionalWeakTable<LevelUpBatchPreview, NdsPreview> _previews = new();
    internal event Action? Changed;

    public IReadOnlyList<PokemonListEntry> Entries { get; }
    public IReadOnlyList<MoveChoice> Moves { get; }
    public int Generation => _project.Generation;
    public bool SupportsExpand => false;

    internal NdsLevelUpSource(NdsProject project)
    {
        _project = project;
        Entries = Enumerable.Range(1, project.MaxSpecies).Select(s => new PokemonListEntry(s, s < project.SpeciesNames.Count ? project.SpeciesNames[s] : $"#{s:000}")).ToArray();
        Moves = project.MoveNames.Select((name, id) => new MoveChoice(id, string.IsNullOrWhiteSpace(name) ? $"Movimiento {id:000}" : name)).ToArray();
    }

    public IReadOnlyList<LevelUpMoveEntry> Read(int species) =>
        _project.ReadLearnset(species).Select(m => new LevelUpMoveEntry { Move = m.Move, Level = m.Level }).ToArray();

    public void Save(int species, IReadOnlyList<LevelUpMoveEntry> rows)
    {
        if (!Entries.Any(e => e.Index == species))
            throw new ArgumentOutOfRangeException(nameof(species));
        var moves = rows.Select(r => new NdsLearnsetMove(r.Move, r.Level)).ToArray();
        _project.Apply(_project.PreviewLearnsets("Editar movimientos por nivel · " + Entries.First(e => e.Index == species).Name,
            new Dictionary<int, IReadOnlyList<NdsLearnsetMove>> { [species] = moves }));
        Changed?.Invoke();
    }

    public LevelUpBatchPreview PrepareRandomization(LevelUpRandomSettings settings, bool metronome = false)
    {
        if (settings.SpreadTo is < 1 or > 100 || settings.StabPercent is < 0 or > 100)
            throw new ArgumentException("Revisá el nivel final y el porcentaje STAB.");
        NdsPreview preview;
        if (metronome)
            preview = _project.PreviewLearnsets("Modo Metrónomo (todos)", Entries.ToDictionary(e => e.Index, e => (IReadOnlyList<NdsLearnsetMove>)[new NdsLearnsetMove(Metronome, 1)]));
        else
            preview = _project.PreviewData("Randomizar todos · Level Up", NdsSection.Learnsets, new NdsDataOptions(SwitchSeed.New32())
            {
                Learnsets = NdsLearnsetMode.Random, SameTypePercent = settings.BiasByType ? settings.StabPercent : 0, AllowHmMoves = settings.AllowHms,
                StartWithFour = settings.StartWithFour, SpreadLevelsTo = settings.Spread ? settings.SpreadTo : 0, ExcludeFixedDamage = settings.ExcludeFixedDamage,
            });
        // One summary row per species whose list changes, as the 3DS preview shows.
        var sets = _project.LearnsetsOf(preview);
        var changes = Entries.Where(e => sets is not null && sets.TryGetValue(e.Index, out var after) && !after.SequenceEqual(_project.ReadLearnset(e.Index)))
            .Select(e => new BatchChange(e.Index, e.Name, "Aprendizaje por nivel", "Learnset original", metronome ? "Metrónomo al nivel 1" : "Learnset randomizado")).ToArray();
        var batch = new LevelUpBatchPreview([], [], changes.Length > 0 ? changes : [new BatchChange(0, "Level Up", "Aprendizaje por nivel", "", $"{preview.Result.Changes.Count} cambios")],
            metronome ? "Modo Metrónomo (todos)" : "Randomizar todos", metronome ? null : settings) { Token = preview };
        _previews.Add(batch, preview);
        return batch;
    }

    public IReadOnlyDictionary<int, IReadOnlyList<LevelUpMoveEntry>> PreviewLearnsets(LevelUpBatchPreview preview)
    {
        if (!_previews.TryGetValue(preview, out var nds))
            throw new ArgumentException("La vista previa no es de este proyecto.");
        var sets = _project.LearnsetsOf(nds);
        return Entries.ToDictionary(e => e.Index, e => (IReadOnlyList<LevelUpMoveEntry>)(sets is not null && sets.TryGetValue(e.Index, out var set) ? set : _project.ReadLearnset(e.Index))
            .Select(m => new LevelUpMoveEntry { Move = m.Move, Level = m.Level }).ToArray());
    }

    public void ApplyRandomization(LevelUpBatchPreview preview)
    {
        if (!_previews.TryGetValue(preview, out var nds))
            throw new ArgumentException("La vista previa no es de este proyecto.");
        _project.Apply(nds);
        Changed?.Invoke();
    }

    public string ExportCsv()
    {
        var lines = new List<string> { "PokemonID,Pokemon,Level,MoveID,Move" };
        foreach (var entry in Entries)
            foreach (var row in _project.ReadLearnset(entry.Index))
                lines.Add($"{entry.Index},\"{entry.Name.Replace("\"", "\"\"")}\",{row.Level},{row.Move},\"{Moves[row.Move].Name.Replace("\"", "\"\"")}\"");
        return string.Join(Environment.NewLine, lines);
    }
}
