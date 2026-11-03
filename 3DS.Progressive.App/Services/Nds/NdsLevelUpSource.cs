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
    private const int Metronome = 118, Struggle = 165;
    private readonly NdsProject _project;
    private readonly ConditionalWeakTable<LevelUpBatchPreview, NdsPreview> _previews = new();
    internal event Action? Changed;

    public IReadOnlyList<PokemonListEntry> Entries { get; }
    public IReadOnlyList<MoveChoice> Moves { get; }
    public int Generation => _project.Generation;
    public int MaxMoves { get; }
    public IReadOnlyList<int> RandomMovePool => Moves.Where(m => m.Id > 0 && m.Id != Struggle).Select(m => m.Id).ToArray();

    internal NdsLevelUpSource(NdsProject project)
    {
        _project = project;
        Entries = Enumerable.Range(1, project.MaxSpecies).Select(s => new PokemonListEntry(s, s < project.SpeciesNames.Count ? project.SpeciesNames[s] : $"#{s:000}")).ToArray();
        MaxMoves = project.LearnsetLimit;
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
        if (settings.SpreadTo is < 1 or > 100 || settings.StabPercent is < 0 or > 100 || settings.Expand && (settings.ExpandTo < 1 || settings.ExpandTo > MaxMoves))
            throw new ArgumentException("Revisa el nivel final, el porcentaje STAB y la cantidad de movimientos.");
        var current = _project.ReadAllLearnsets();
        NdsPreview preview;
        string action = metronome ? "Modo Metrónomo (todos)" : "Randomizar todos";
        if (metronome)
            preview = _project.PreviewLearnsets(action + " · Level Up", Entries.ToDictionary(e => e.Index, e => (IReadOnlyList<NdsLearnsetMove>)[new NdsLearnsetMove(Metronome, 1)]));
        else
        {
            uint seed = SwitchSeed.New32();
            var options = new NdsDataOptions(seed)
            {
                Learnsets = NdsLearnsetMode.Random, SameTypePercent = settings.BiasByType ? settings.StabPercent : 0, AllowHmMoves = settings.AllowHms,
                StartWithFour = settings.StartWithFour, SpreadLevelsTo = settings.Spread ? settings.SpreadTo : 0, ExcludeFixedDamage = settings.ExcludeFixedDamage,
            };
            preview = _project.PreviewData(action + " · Level Up", NdsSection.Learnsets, options);
            // «Expandir cantidad»: the DS randomizer keeps each list's length, so the lists grow here and are saved whole (the recipe keeps them).
            if (settings.Expand)
            {
                int target = Math.Min(settings.ExpandTo, MaxMoves);
                action = $"Randomizar todos · {target} movimientos";
                preview = _project.PreviewLearnsets(action + " · Level Up", Expand(_project.LearnsetsOf(preview)!, target, settings, new Random(unchecked((int)seed))));
            }
        }
        // One summary row per species whose list changes, as the 3DS preview shows.
        var sets = _project.LearnsetsOf(preview);
        var changes = Entries.Where(e => sets is not null && sets.TryGetValue(e.Index, out var after) && current.TryGetValue(e.Index, out var before) && !after.SequenceEqual(before))
            .Select(e => new BatchChange(e.Index, e.Name, "Aprendizaje por nivel", $"{current[e.Index].Count} movimientos",
                metronome ? "Metrónomo al nivel 1" : $"{sets![e.Index].Count} movimientos randomizados")).ToArray();
        var batch = new LevelUpBatchPreview([], [], changes.Length > 0 ? changes : [new BatchChange(0, "Level Up", "Aprendizaje por nivel", "", $"{preview.Result.Changes.Count} cambios")],
            action, metronome ? null : settings) { Token = preview };
        _previews.Add(batch, preview);
        return batch;
    }

    // Lists grown to the target with moves the options allow (no Struggle, HMs only if allowed, no fixed damage if asked, the species'
    // types as often as the STAB percentage), no repeats; then the levels are spread again as the randomizer does.
    private Dictionary<int, IReadOnlyList<NdsLearnsetMove>> Expand(IReadOnlyDictionary<int, IReadOnlyList<NdsLearnsetMove>> sets, int target, LevelUpRandomSettings settings, Random random)
    {
        var moves = _project.Read(NdsSection.Moves).GroupBy(c => c.Entry).ToDictionary(g => g.Key, g => (Type: g.First(c => c.Local == "Type").Value, Power: g.First(c => c.Local == "Power").Value));
        var pokemon = _project.Read(NdsSection.Pokemon).Where(c => c.Local is "Type1" or "Type2").GroupBy(c => c.Entry).ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToHashSet());
        var excluded = new HashSet<int> { Struggle };
        if (!settings.AllowHms)
            excluded.UnionWith(_project.Source.MachineMoves.Skip(_project.Source.TmCount));
        if (settings.ExcludeFixedDamage)
            excluded.UnionWith([49, 82]);
        int[] pool = moves.Keys.Where(m => m > 0 && !excluded.Contains(m)).Order().ToArray();
        var result = new Dictionary<int, IReadOnlyList<NdsLearnsetMove>>();
        foreach (var (species, list) in sets)
        {
            if (list.Count >= target || list.All(m => m.Move == Metronome))
            {
                result[species] = list;
                continue;
            }
            var own = pokemon.GetValueOrDefault(species) ?? [];
            int[] stab = pool.Where(m => moves[m].Power > 0 && own.Contains(moves[m].Type)).ToArray();
            var chosen = list.Select(m => m.Move).ToList();
            for (int attempt = 0; chosen.Count < target && attempt < target * 20; attempt++)
            {
                int[] from = settings.BiasByType && stab.Length > 0 && random.Next(100) < settings.StabPercent ? stab : pool;
                int move = from[random.Next(from.Length)];
                if (!chosen.Contains(move))
                    chosen.Add(move);
            }
            int top = settings.Spread ? settings.SpreadTo : Math.Max(list.Count > 0 ? list.Max(m => m.Level) : 1, 50);
            result[species] = chosen.Select((move, i) => new NdsLearnsetMove(move, settings.StartWithFour && i < 4 ? 1 : Math.Max(1, i * top / chosen.Count))).ToArray();
        }
        return result;
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
