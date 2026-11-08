using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

internal sealed partial class NdsRecordEditor
{
    private IReadOnlyDictionary<int, IReadOnlyList<NdsLearnsetMove>>? _trainerLearnsets;
    private int _learnsetRevision = -1;
    private IReadOnlyList<Gen5Trainer>? _originalTrainers, _currentTrainers;
    private int _trainerRevision = -1;

    // Form-specific learnset resolution is not available in the DS data source. Never substitute a base-form list silently.
    // On hacks this is an estimate from the project's tables; modified battle code may choose something else.
    internal int[]? DerivedMoves(int species, int form, int level)
    {
        if (form != 0 || species < 1 || species > _project.MaxSpecies || level is < 1 or > 100 || !_project.Allows(Gen5Feature.Learnsets)) return null;
        if (_learnsetRevision != _project.Revision)
        {
            _trainerLearnsets = _project.ReadAllLearnsets(); _learnsetRevision = _project.Revision;
        }
        if (!_trainerLearnsets!.TryGetValue(species, out var set)) return null;
        var moves = new List<int>();
        foreach (var entry in set)
        {
            if (entry.Level > level) break;
            if (entry.Move <= 0 || moves.Contains(entry.Move)) continue;
            if (moves.Count == 4) moves.RemoveAt(0);
            moves.Add(entry.Move);
        }
        return moves.Concat(Enumerable.Repeat(0, 4 - moves.Count)).ToArray();
    }

    internal string MoveOrigin => _project.IsRomhack ? "Por nivel · estimación de las tablas del proyecto" : "Por nivel · derivados del proyecto";
    internal string SpeciesName(int value) => value >= 0 && value < _project.SpeciesNames.Count ? _project.SpeciesNames[value] : "#" + value;
    internal string ItemName(int value) => value == 0 ? "sin objeto" : value > 0 && value < _project.ItemNames.Count ? _project.ItemNames[value] : "#" + value;
    internal string MoveName(int value) => value == 0 ? "—" : value > 0 && value < _project.MoveNames.Count ? _project.MoveNames[value] : "#" + value;

    private string? TrainerBeforeLabel(Gen5Change change)
    {
        const string prefix="Entrenador ";
        if (!UsesTrainerLayout || !change.Location.StartsWith(prefix,StringComparison.Ordinal) || !change.Field.StartsWith("Pokémon ",StringComparison.Ordinal)) return null;
        string idText=change.Location[prefix.Length..].Split(' ')[0];
        if (!int.TryParse(idText,out int id)) return null;
        var trainer=TrainerStates(id).Current;
        if (change.Field.Contains("· Movimiento ",StringComparison.Ordinal) && !trainer.HasMoves) return "Por nivel (no almacenado)";
        if (change.Field.EndsWith("· Objeto equipado",StringComparison.Ordinal) && !trainer.HasItems) return "Sin objeto (formato del juego)";
        return null;
    }

    internal (Gen5Trainer Original, Gen5Trainer Current) TrainerStates(int id)
    {
        _originalTrainers ??= _project.OriginalTrainers();
        if (_trainerRevision != _project.Revision) { _currentTrainers = _project.Trainers(); _trainerRevision = _project.Revision; }
        return (_originalTrainers.Single(t => t.Id == id), _currentTrainers!.Single(t => t.Id == id));
    }

    internal string TeamMemberDescription(Gen5Trainer trainer, int slot)
    {
        if (slot >= trainer.Team.Count) return "Fuera del equipo";
        var p = trainer.Team[slot];
        string item = trainer.HasItems ? ItemName(p.HeldItem) + " (explícito)" : "sin objeto (formato del juego)";
        string moves = trainer.HasMoves ? string.Join(" / ", p.Moves.Select(MoveName)) + " (explícitos)" : "por nivel (no almacenados)";
        return $"{SpeciesName(p.Species)} · forma {p.Form} · Nv. {p.Level} · {item} · {moves}";
    }
}
