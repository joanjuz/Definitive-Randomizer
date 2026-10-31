#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.NDS;

public enum NdsStatsMode { Keep, Shuffle, Random }
public enum NdsLearnsetMode { Keep, Random, Metronome }
public enum NdsMachineMode { Keep, Random, TypeBased, Full }
public enum NdsStaticMode { Keep, Random, SimilarBst, SwapLegendaries }

/// <summary>Universal-randomizer style options for species, moves, learnsets, evolutions and TM/HM compatibility.</summary>
public sealed record NdsDataOptions(uint Seed)
{
    /// <summary>Shuffle keeps each species' six values in another order; Random spreads the same total in new proportions.</summary>
    public NdsStatsMode Stats { get; init; }
    public bool Types { get; init; }
    public bool Abilities { get; init; }
    /// <summary>Evolved species take their types and abilities from the species they evolve from.</summary>
    public bool FollowEvolutions { get; init; } = true;
    /// <summary>Raises every catch rate below this value to it (0 keeps them).</summary>
    public int MinCatchRate { get; init; }
    public bool MovePower { get; init; }
    public bool MoveAccuracy { get; init; }
    public bool MovePp { get; init; }
    public bool MoveTypes { get; init; }
    public NdsLearnsetMode Learnsets { get; init; }
    /// <summary>Chance (%) that a random level-up move shares a type with the species.</summary>
    public int SameTypePercent { get; init; } = 50;
    /// <summary>Trade evolutions (with or without item, and Karrablast/Shelmet) become level 37.</summary>
    public bool RemoveTradeEvolutions { get; init; }
    /// <summary>Each evolution leads to a species of similar base stat total.</summary>
    public bool RandomEvolutions { get; init; }
    /// <summary>Evolutions by time of day become time-free: friendship by day or night → friendship, holding an item by day or night → using it.</summary>
    public bool RemoveTimeEvolutions { get; init; }
    /// <summary>Level evolutions above this level happen at it (0 = keep).</summary>
    public int MaxEvolutionLevel { get; init; }
    public NdsMachineMode Machines { get; init; }
    public int MachinePercent { get; init; } = 50;
    /// <summary>HM compatibility stays as it is with Random and TypeBased, so field moves the story needs stay learnable.</summary>
    public bool KeepHms { get; init; } = true;
    /// <summary>SwapLegendaries keeps legendaries legendary and the rest not (UPR-ZX "swap legendaries").</summary>
    public NdsStaticMode Statics { get; init; }
    public bool TradeGiven { get; init; }
    public bool TradeRequested { get; init; }
    public IReadOnlySet<int>? Scope { get; init; }
    internal bool InScope(int entry) => Scope is null || Scope.Contains(entry);
}

/// <summary>
/// Data randomizations in the manner of Universal Pokémon Randomizer ZX (GPLv3): every result is a set of cell edits written by
/// <see cref="NdsCells"/>, so the change log and the checks are the ones manual editing uses. Choices depend only on the seed
/// and the entry, so a result is reproducible.
/// </summary>
public static class NdsDataRandomizer
{
    // Struggle never changes; Metronome is the move of the Metronome-only mode.
    private const int Struggle = 165, Metronome = 118;
    // Abilities that break or do nothing on other species (Wonder Guard, Forecast, Multitype, Flower Gift, Illusion, Zen Mode).
    private static readonly HashSet<int> BannedAbilities = [25, 59, 121, 122, 149, 161];
    // Legendary and mythical species of Gen1-5, by National Dex number.
    private static readonly HashSet<int> Legendaries = [144, 145, 146, 150, 151, 243, 244, 245, 249, 250, 251, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386,
        480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493, 494, 638, 639, 640, 641, 642, 643, 644, 645, 646, 647, 648, 649];
    public static bool IsLegendary(int species) => Legendaries.Contains(species);

    public static Gen5RandomizerResult Generate(Gen5Data data, NdsSection section, NdsDataOptions options) =>
        NdsCells.Write(data, section, Edits(NdsDataSource.Of(data), section, s => NdsCells.Read(data, s), options));

    public static Gen5RandomizerResult Generate(Gen4Data data, NdsSection section, NdsDataOptions options) =>
        NdsCells.Write(data, section, Edits(NdsDataSource.Of(data), section, s => NdsCells.Read(data, s), options));

    private static Dictionary<string, int> Edits(NdsDataSource source, NdsSection section, Func<NdsSection, IReadOnlyList<NdsCell>> read, NdsDataOptions options)
    {
        var edits = new Dictionary<string, int>();
        int Pick(string key, int count) => Gen5Randomizer.Index(options.Seed, key, count);
        IReadOnlyDictionary<int, Dictionary<string, int>> Table(NdsSection s) =>
            read(s).GroupBy(c => c.Entry).ToDictionary(g => g.Key, g => g.ToDictionary(c => c.Local, c => c.Value));
        void Set(int entry, string local, int value, IReadOnlyDictionary<int, Dictionary<string, int>> table)
        {
            if (table[entry][local] != value)
                edits[entry + ":" + local] = value;
        }
        int[] types = source.UsableTypes.ToArray();
        switch (section)
        {
            case NdsSection.Pokemon:
            {
                var pokemon = Table(NdsSection.Pokemon);
                var parents = Parents(Table(NdsSection.Evolutions));
                int abilityMax = source.AbilityNames.Count - 1;
                int[] abilities = Enumerable.Range(1, Math.Max(0, abilityMax)).Where(a => !BannedAbilities.Contains(a)).ToArray();
                var newTypes = new Dictionary<int, (int, int)>();
                var newAbilities = new Dictionary<int, int[]>();
                // Families in evolution order, so evolved species can copy what their pre-evolution got.
                foreach (int species in EvolutionOrder(pokemon.Keys, parents))
                {
                    var row = pokemon[species];
                    if (!options.InScope(species))
                        continue;
                    if (options.Stats != NdsStatsMode.Keep)
                    {
                        int[] stats = Enumerable.Range(0, 6).Select(i => row["Stat" + i]).ToArray();
                        int[] result = options.Stats == NdsStatsMode.Shuffle ? ShuffleStats(stats, species, Pick) : RandomStats(stats, species, Pick);
                        for (int i = 0; i < 6; i++)
                            Set(species, "Stat" + i, result[i], pokemon);
                    }
                    int? parent = options.FollowEvolutions && parents.TryGetValue(species, out int p) && pokemon.ContainsKey(p) ? p : null;
                    if (options.Types)
                    {
                        (int t1, int t2) = parent is { } from && newTypes.TryGetValue(from, out var inherited) ? inherited
                            : parent is { } kept ? (pokemon[kept]["Type1"], pokemon[kept]["Type2"]) : RandomTypes(species, types, Pick);
                        newTypes[species] = (t1, t2);
                        Set(species, "Type1", t1, pokemon);
                        Set(species, "Type2", t2, pokemon);
                    }
                    if (options.Abilities && abilities.Length > 0)
                    {
                        int slots = row.Keys.Count(k => k.StartsWith("Ability", StringComparison.Ordinal));
                        int[] chosen = parent is { } from && newAbilities.TryGetValue(from, out var inherited) ? inherited
                            : Enumerable.Range(1, slots).Select(i => abilities[Pick($"ability/{species}/{i}", abilities.Length)]).ToArray();
                        newAbilities[species] = chosen;
                        for (int i = 1; i <= slots; i++)
                            // Empty slots stay empty; a second slot repeats the first when it would match it anyway.
                            if (row["Ability" + i] != 0)
                                Set(species, "Ability" + i, i == 2 && chosen[1] == chosen[0] ? chosen[0] : chosen[i - 1], pokemon);
                    }
                    if (options.MinCatchRate > 0 && row["CatchRate"] < options.MinCatchRate)
                        Set(species, "CatchRate", Math.Min(255, options.MinCatchRate), pokemon);
                }
                break;
            }
            case NdsSection.Moves:
            {
                var moves = Table(NdsSection.Moves);
                foreach (var (move, row) in moves)
                {
                    if (move == Struggle || !options.InScope(move))
                        continue;
                    if (options.MovePower && row["Power"] > 1)
                        Set(move, "Power", 20 + 5 * Pick($"power/{move}", 27), moves);
                    if (options.MoveAccuracy && row["Accuracy"] is > 0 and <= 100)
                        Set(move, "Accuracy", 50 + 5 * Pick($"accuracy/{move}", 11), moves);
                    if (options.MovePp)
                        Set(move, "PP", 5 * (1 + Pick($"pp/{move}", 8)), moves);
                    // Gen4's "???" type (Curse) stays.
                    if (options.MoveTypes && types.Contains(row["Type"]))
                        Set(move, "Type", types[Pick($"type/{move}", types.Length)], moves);
                }
                break;
            }
            case NdsSection.Learnsets when options.Learnsets != NdsLearnsetMode.Keep:
            {
                var sets = Table(NdsSection.Learnsets);
                var pokemon = Table(NdsSection.Pokemon);
                var moves = Table(NdsSection.Moves);
                int[] pool = moves.Keys.Where(m => m != Struggle).Order().ToArray();
                int[] damaging = pool.Where(m => moves[m]["Power"] > 0).ToArray();
                foreach (var (species, row) in sets)
                {
                    if (!options.InScope(species))
                        continue;
                    int count = row.Keys.Count(k => k.EndsWith(".Move", StringComparison.Ordinal));
                    if (options.Learnsets == NdsLearnsetMode.Metronome)
                    {
                        for (int i = 0; i < count; i++)
                            Set(species, "M" + i + ".Move", Metronome, sets);
                        continue;
                    }
                    var own = pokemon.TryGetValue(species, out var p) ? new[] { p["Type1"], p["Type2"] } : [];
                    int[] stab = damaging.Where(m => own.Contains(moves[m]["Type"])).ToArray();
                    var used = new HashSet<int>();
                    for (int i = 0; i < count; i++)
                    {
                        int move = 0;
                        // The first move always deals damage; several tries avoid repeating a move in one learnset.
                        for (int attempt = 0; attempt < 8 && (move == 0 || used.Contains(move)); attempt++)
                        {
                            string key = $"learn/{species}/{i}/{attempt}";
                            bool sameType = stab.Length > 0 && Pick(key + "/stab", 100) < options.SameTypePercent;
                            int[] from = sameType ? stab : i == 0 ? damaging : pool;
                            move = from[Pick(key, from.Length)];
                        }
                        used.Add(move);
                        Set(species, "M" + i + ".Move", move, sets);
                    }
                }
                break;
            }
            case NdsSection.Evolutions:
            {
                var evolutions = Table(NdsSection.Evolutions);
                var pokemon = Table(NdsSection.Pokemon);
                int Bst(int species) => pokemon.TryGetValue(species, out var row) ? Enumerable.Range(0, 6).Sum(i => row["Stat" + i]) : 0;
                var all = pokemon.Keys.Order().ToArray();
                foreach (var (species, row) in evolutions)
                {
                    if (!options.InScope(species))
                        continue;
                    for (int i = 0; row.ContainsKey("E" + i + ".Method"); i++)
                    {
                        int method = row["E" + i + ".Method"], target = row["E" + i + ".Species"];
                        if (method == 0 || target == 0)
                            continue;
                        string name = method < source.EvolutionMethods.Count ? source.EvolutionMethods[method] : "";
                        if (options.RemoveTimeEvolutions && name is "Amistad de día" or "Amistad de noche")
                            Set(species, "E" + i + ".Method", source.EvolutionMethods.ToList().IndexOf("Amistad"), evolutions);
                        if (options.RemoveTimeEvolutions && name is "Nivel con objeto de día" or "Nivel con objeto de noche")
                            Set(species, "E" + i + ".Method", source.EvolutionMethods.ToList().IndexOf("Usar objeto"), evolutions);
                        // Every "Nivel…" method uses its parameter as the level.
                        if (options.MaxEvolutionLevel > 0 && name.StartsWith("Nivel", StringComparison.Ordinal) && !name.StartsWith("Nivel con objeto", StringComparison.Ordinal)
                            && !name.StartsWith("Nivel en", StringComparison.Ordinal) && !name.StartsWith("Nivel junto", StringComparison.Ordinal) && row["E" + i + ".Param"] > options.MaxEvolutionLevel)
                            Set(species, "E" + i + ".Param", options.MaxEvolutionLevel, evolutions);
                        if (options.RemoveTradeEvolutions && source.IsTradeMethod(method))
                        {
                            Set(species, "E" + i + ".Method", source.LevelMethod, evolutions);
                            Set(species, "E" + i + ".Param", 37, evolutions);
                        }
                        if (options.RandomEvolutions)
                        {
                            int bst = Bst(target), tolerance = Math.Max(10, bst / 10);
                            int[] candidates;
                            while ((candidates = all.Where(s => s != species && s != target && Math.Abs(Bst(s) - bst) <= tolerance).ToArray()).Length < 5 && tolerance < 800)
                                tolerance += Math.Max(10, bst / 20);
                            if (candidates.Length > 0)
                                Set(species, "E" + i + ".Species", candidates[Pick($"evo/{species}/{i}", candidates.Length)], evolutions);
                        }
                    }
                }
                break;
            }
            case NdsSection.Statics when options.Statics != NdsStaticMode.Keep:
            {
                var statics = Table(NdsSection.Statics);
                var pokemon = Table(NdsSection.Pokemon);
                int Bst(int species) => pokemon.TryGetValue(species, out var row) ? Enumerable.Range(0, 6).Sum(i => row["Stat" + i]) : 0;
                int[] all = pokemon.Keys.Order().ToArray();
                foreach (var (entry, row) in statics)
                {
                    if (!options.InScope(entry))
                        continue;
                    int species = row["Species"];
                    int[] candidates = options.Statics switch
                    {
                        NdsStaticMode.SwapLegendaries => all.Where(s => IsLegendary(s) == IsLegendary(species)).ToArray(),
                        NdsStaticMode.SimilarBst => all.Where(s => Math.Abs(Bst(s) - Bst(species)) <= Math.Max(30, Bst(species) / 10)).ToArray(),
                        _ => all,
                    };
                    if (candidates.Length > 0)
                        Set(entry, "Species", candidates[Pick($"static/{entry}", candidates.Length)], statics);
                }
                break;
            }
            case NdsSection.Trades:
            {
                var trades = Table(NdsSection.Trades);
                int max = source.MaxSpecies;
                foreach (var (entry, row) in trades)
                {
                    if (!options.InScope(entry))
                        continue;
                    if (options.TradeGiven)
                        Set(entry, "Given", 1 + Pick($"trade/{entry}/given", max), trades);
                    if (options.TradeRequested && row["Requested"] > 0)
                        Set(entry, "Requested", 1 + Pick($"trade/{entry}/requested", max), trades);
                }
                break;
            }
            case NdsSection.Machines when options.Machines != NdsMachineMode.Keep:
            {
                var machines = Table(NdsSection.Machines);
                var pokemon = Table(NdsSection.Pokemon);
                var moves = Table(NdsSection.Moves);
                int MoveType(int bit) => bit < source.MachineMoves.Count && moves.TryGetValue(source.MachineMoves[bit], out var m) ? m["Type"] : -1;
                foreach (var (species, row) in machines)
                {
                    if (!options.InScope(species))
                        continue;
                    var own = pokemon.TryGetValue(species, out var p) ? new[] { p["Type1"], p["Type2"] } : [];
                    foreach (string local in row.Keys)
                    {
                        int bit = int.Parse(local[1..]);
                        bool hm = bit >= source.TmCount;
                        int value = options.Machines switch
                        {
                            NdsMachineMode.Full => 1,
                            _ when hm && options.KeepHms => row[local],
                            NdsMachineMode.TypeBased when own.Contains(MoveType(bit)) => 1,
                            // Normal moves are learnable by most species.
                            NdsMachineMode.TypeBased when MoveType(bit) == 0 => Pick($"tm/{species}/{bit}", 100) < 75 ? 1 : 0,
                            _ => Pick($"tm/{species}/{bit}", 100) < options.MachinePercent ? 1 : 0,
                        };
                        Set(species, local, value, machines);
                    }
                }
                break;
            }
        }
        return edits;
    }

    // Species → the species it evolves from (first one found), from the evolution table.
    private static Dictionary<int, int> Parents(IReadOnlyDictionary<int, Dictionary<string, int>> evolutions)
    {
        var parents = new Dictionary<int, int>();
        foreach (var (species, row) in evolutions.OrderBy(p => p.Key))
            for (int i = 0; row.ContainsKey("E" + i + ".Method"); i++)
                if (row["E" + i + ".Method"] != 0 && row["E" + i + ".Species"] is int target and > 0 && target != species)
                    parents.TryAdd(target, species);
        return parents;
    }

    // Pre-evolutions before their evolutions; species in a cycle (only possible after manual edits) come last in id order.
    private static IEnumerable<int> EvolutionOrder(IEnumerable<int> species, IReadOnlyDictionary<int, int> parents)
    {
        int Depth(int s)
        {
            int depth = 0;
            for (int current = s; parents.TryGetValue(current, out int parent) && depth < 8; current = parent)
                depth++;
            return depth;
        }
        return species.OrderBy(Depth).ThenBy(s => s);
    }

    private static int[] ShuffleStats(int[] stats, int species, Func<string, int, int> pick)
    {
        var result = (int[])stats.Clone();
        // Shedinja-style 1 HP stays on HP.
        int first = stats[0] == 1 ? 1 : 0;
        for (int i = 5; i > first; i--)
        {
            int j = first + pick($"shuffle/{species}/{i}", i - first + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

    private static int[] RandomStats(int[] stats, int species, Func<string, int, int> pick)
    {
        int first = stats[0] == 1 ? 1 : 0, total = stats.Skip(first).Sum();
        var weights = Enumerable.Range(0, 6).Select(i => i < first ? 0 : 50 + pick($"stat/{species}/{i}", 101)).ToArray();
        int sum = weights.Sum();
        var result = weights.Select((w, i) => i < first ? stats[i] : Math.Clamp((int)Math.Round((double)total * w / sum), 10, 255)).ToArray();
        // Rounding and clamping can move the total a little; give the difference back to stats that have room.
        for (int diff = total - result.Skip(first).Sum(), i = first, guard = 0; diff != 0 && guard < 6 * 256 * 2; i = i == 5 ? first : i + 1, guard++)
        {
            int step = Math.Sign(diff);
            if (result[i] + step is >= 10 and <= 255)
            {
                result[i] += step;
                diff -= step;
            }
        }
        return result;
    }

    private static (int, int) RandomTypes(int species, int[] types, Func<string, int, int> pick)
    {
        int first = types[pick($"type/{species}/1", types.Length)];
        // Half of the species keep a single type, as the originals roughly do.
        if (pick($"type/{species}/dual", 2) == 0)
            return (first, first);
        var others = types.Where(t => t != first).ToArray();
        return (first, others[pick($"type/{species}/2", others.Length)]);
    }
}
