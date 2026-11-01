using System.Runtime.CompilerServices;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services.Switch;

/// <summary>
/// The 3DS Level Up view over a Sword/Shield project: whole lists per personal entry (up to 64 moves, level 0 = on evolution),
/// saved as "learnset.manual" operations; "Randomizar todos" is the Switch learnset randomizer with the 3DS options (data.random,
/// re-seeded by Global Template as every Switch randomizer), and "Metrónomo · todos" leaves Metronome at level 1.
/// </summary>
internal sealed class SwordShieldLevelUpSource : ILevelUpSource
{
    private const int Metronome = 118;
    private readonly SwordShieldProject _project;
    private readonly ConditionalWeakTable<LevelUpBatchPreview, SwordShieldDataPreview> _previews = new();
    internal event Action? Changed;

    public IReadOnlyList<PokemonListEntry> Entries { get; }
    public IReadOnlyList<MoveChoice> Moves { get; }
    public int Generation => 8;
    public int MaxMoves => SwordShieldLearnsets.Capacity;
    // Moves Sword/Shield can use (many older moves are cut), never Struggle.
    public IReadOnlyList<int> RandomMovePool { get; }

    internal SwordShieldLevelUpSource(SwordShieldProject project)
    {
        _project = project;
        // Every present species and form that has a list (the Level Up record of an absent entry is unused).
        var lists = project.ReadAllLearnsets();
        Entries = project.Entries.Where(e => e.Present && e.Species > 0 && lists.TryGetValue(e.Index, out var l) && l.Count > 0)
            .Select(e => new PokemonListEntry(e.Index, e.Form > 0 ? $"{e.Name} · forma {e.Form}" : e.Name)).ToArray();
        RandomMovePool = project.UsableMoves().Where(m => m is > 0 and not 165).Order().ToArray();
        Moves = Enumerable.Range(0, 827).Select(id => new MoveChoice(id, id == 0 ? "—" : project.MoveName(id) is { Length: > 0 } name ? name : $"Movimiento {id:000}")).ToArray();
    }

    public IReadOnlyList<LevelUpMoveEntry> Read(int entry) =>
        _project.ReadLearnset(entry).Select(m => new LevelUpMoveEntry { Move = m.Move, Level = m.Level }).ToArray();

    public void Save(int entry, IReadOnlyList<LevelUpMoveEntry> rows)
    {
        if (!Entries.Any(e => e.Index == entry))
            throw new ArgumentOutOfRangeException(nameof(entry));
        _project.ApplyData(_project.PreviewLearnsets(new Dictionary<int, IReadOnlyList<SwordShieldLearnMove>> { [entry] = rows.Select(r => new SwordShieldLearnMove(r.Move, r.Level)).ToArray() }));
        Changed?.Invoke();
    }

    public LevelUpBatchPreview PrepareRandomization(LevelUpRandomSettings settings, bool metronome = false)
    {
        if (settings.SpreadTo is < 1 or > 100 || settings.StabPercent is < 0 or > 100 || settings.Expand && (settings.ExpandTo < 1 || settings.ExpandTo > MaxMoves))
            throw new ArgumentException("Revisá el nivel final, el porcentaje STAB y la cantidad de movimientos.");
        var current = _project.ReadAllLearnsets();
        var preview = metronome
            ? _project.PreviewLearnsets(Entries.ToDictionary(e => e.Index, e => (IReadOnlyList<SwordShieldLearnMove>)[new SwordShieldLearnMove(Metronome, 1)]))
            : _project.PreviewData(SwordShieldDataSection.Learnsets, null, new SwordShieldDataOptions(SwitchSeed.New32())
            {
                LearnStabPercent = settings.BiasByType ? settings.StabPercent : 0, LearnExpandTo = settings.Expand ? settings.ExpandTo : 0,
                LearnStartWithFour = settings.StartWithFour, LearnSpreadTo = settings.Spread ? settings.SpreadTo : 0, LearnExcludeFixedDamage = settings.ExcludeFixedDamage,
            });
        var sets = SwordShieldProject.LearnsetsOf(preview);
        var changes = Entries.Where(e => sets.TryGetValue(e.Index, out var after) && current.TryGetValue(e.Index, out var before) && !after.SequenceEqual(before))
            .Select(e => new BatchChange(e.Index, e.Name, "Aprendizaje por nivel", $"{current[e.Index].Count} movimientos",
                metronome ? "Metrónomo al nivel 1" : $"{sets[e.Index].Count} movimientos randomizados")).ToArray();
        string action = metronome ? "Modo Metrónomo (todos)" : "Randomizar todos";
        var batch = new LevelUpBatchPreview([], [], changes.Length > 0 ? changes : [new BatchChange(0, "Level Up", "Aprendizaje por nivel", "", $"{preview.Result.Changes.Count} cambios")],
            action, metronome ? null : settings) { Token = preview };
        _previews.Add(batch, preview);
        return batch;
    }

    public IReadOnlyDictionary<int, IReadOnlyList<LevelUpMoveEntry>> PreviewLearnsets(LevelUpBatchPreview preview)
    {
        if (!_previews.TryGetValue(preview, out var data))
            throw new ArgumentException("La vista previa no es de este proyecto.");
        var sets = SwordShieldProject.LearnsetsOf(data);
        return Entries.ToDictionary(e => e.Index, e => (IReadOnlyList<LevelUpMoveEntry>)(sets.TryGetValue(e.Index, out var set) ? set : _project.ReadLearnset(e.Index))
            .Select(m => new LevelUpMoveEntry { Move = m.Move, Level = m.Level }).ToArray());
    }

    public void ApplyRandomization(LevelUpBatchPreview preview)
    {
        if (!_previews.TryGetValue(preview, out var data))
            throw new ArgumentException("La vista previa no es de este proyecto.");
        _project.ApplyData(data);
        Changed?.Invoke();
    }

    public string ExportCsv()
    {
        var lines = new List<string> { "Entry,Pokemon,Level,MoveID,Move" };
        var all = _project.ReadAllLearnsets();
        foreach (var entry in Entries)
            foreach (var row in all[entry.Index])
                lines.Add($"{entry.Index},\"{entry.Name.Replace("\"", "\"\"")}\",{row.Level},{row.Move},\"{Moves[Math.Min(row.Move, Moves.Count - 1)].Name.Replace("\"", "\"\"")}\"");
        return string.Join(Environment.NewLine, lines);
    }
}
