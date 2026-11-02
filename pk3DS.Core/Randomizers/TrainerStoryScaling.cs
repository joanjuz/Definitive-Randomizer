#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace pk3DS.Core.Randomizers;

/// <summary>One story window: it ends at its milestone trainers (gym leader, kahuna, Elite Four...), whose level is the window's cap.</summary>
public sealed record TrainerStoryStage(string Name, IReadOnlyList<int> Milestones, int BaseCap);

/// <summary>Where a trainer is fought: story window and position inside it. FromWalkthrough is false when the place came from its level.</summary>
public sealed record TrainerStoryPoint(int Stage, int Order, bool FromWalkthrough);

/// <summary>
/// Story chronology of one game, built from the WikiDex walkthrough (tools/story-chronology): the order of the places and the
/// trainers of each place, matched with the game's trainers by team. It drives "Escalado progresivo de entrenadores sin cap" the
/// way the USUM table does: a trainer moves from the previous milestone's cap towards just below the next one, by its position in
/// the window (curve 1.6). Trainers after the Champion (postgame) and trainers the chronology does not place are not scaled.
/// </summary>
public sealed class TrainerStoryProfile
{
    public const double CurvePower = 1.6;
    public string Game { get; }
    public IReadOnlyList<TrainerStoryStage> Stages { get; }
    public IReadOnlyDictionary<int, TrainerStoryPoint> Points { get; }
    private readonly Dictionary<int, int> _milestoneStage = new();
    private readonly Dictionary<int, int[]> _orders;

    public TrainerStoryProfile(string game, IReadOnlyList<TrainerStoryStage> stages, IReadOnlyDictionary<int, TrainerStoryPoint> points)
    {
        Game = game;
        Stages = stages;
        Points = points;
        for (int s = 0; s < stages.Count; s++)
            foreach (int id in stages[s].Milestones)
                _milestoneStage[id] = s;
        // The denominator of each window: every distinct position of the window (regular, important and boss trainers), as in USUM.
        _orders = points.Values.Where(p => p.Stage < stages.Count).GroupBy(p => p.Stage).ToDictionary(g => g.Key, g => g.Select(p => p.Order).Distinct().Order().ToArray());
    }

    /// <summary>Profiles shipped with the app: XY, ORAS, SM, DP, Pt, HGSS, BW, B2W2 and SWSH (embedded JSON).</summary>
    public static TrainerStoryProfile? Load(string key)
    {
        using var stream = typeof(TrainerStoryProfile).Assembly.GetManifestResourceStream("pk3DS.Core.Story." + key + ".json");
        return stream is null ? null : Parse(stream);
    }

    public static TrainerStoryProfile Parse(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var stages = root.GetProperty("stages").EnumerateArray().Select(s => new TrainerStoryStage(s.GetProperty("name").GetString() ?? "",
            s.GetProperty("milestones").EnumerateArray().Select(m => m.GetInt32()).ToArray(), s.GetProperty("cap").GetInt32())).ToArray();
        var points = new Dictionary<int, TrainerStoryPoint>();
        foreach (var p in root.GetProperty("points").EnumerateObject())
        {
            var v = p.Value.EnumerateArray().Select(x => x.GetInt32()).ToArray();
            if (v.Length != 3 || v[0] < 0 || v[0] > stages.Length)
                throw new InvalidDataException("Cronología de entrenadores inválida: " + p.Name);
            points[int.Parse(p.Name)] = new TrainerStoryPoint(v[0], v[1], v[2] == 1);
        }
        if (stages.Length == 0 || stages.Any(s => s.BaseCap is < 1 or > 100))
            throw new InvalidDataException("Cronología de entrenadores sin etapas válidas.");
        return new TrainerStoryProfile(root.GetProperty("game").GetString() ?? "", stages, points);
    }

    public bool IsMilestone(int trainer) => _milestoneStage.ContainsKey(trainer);

    /// <summary>
    /// The game is the one the chronology was built from: every milestone trainer exists and each window's lowest boss level is the
    /// recorded one. A romhack that changed its bosses fails and keeps the scaling by level.
    /// </summary>
    public bool Matches(Func<int, int?> originalAce) =>
        Stages.All(s => s.Milestones.Select(originalAce).ToArray() is var aces && aces.All(a => a is > 0) && aces.Min() == s.BaseCap);

    /// <summary>
    /// Level the trainer's ace should reach, or null when it is not scaled (postgame or not in the chronology).
    /// <paramref name="configuredCap"/> gives the cap a Level Cap rule sets for a window's milestone trainers (null keeps the
    /// game's own level); <paramref name="originalAce"/> gives any trainer's original ace (the first window starts at its lowest one).
    /// </summary>
    public int? Target(int trainer, Func<IReadOnlyList<int>, int?> configuredCap, Func<int, int?> originalAce, int gap, int minimumLevel = 1, double power = CurvePower)
    {
        if (!Points.TryGetValue(trainer, out var point) || point.Stage >= Stages.Count)
            return null;
        int Cap(int stage) => Math.Clamp(configuredCap(Stages[stage].Milestones) ?? Stages[stage].BaseCap, 1, 100);
        // A boss lands on its window's configured cap; without a Level Cap rule it keeps its own level (the Elite Four differ).
        if (_milestoneStage.TryGetValue(trainer, out int milestone))
            return configuredCap(Stages[milestone].Milestones) is int configured ? Math.Max(minimumLevel, Math.Clamp(configured, 1, 100)) : null;

        int next = Cap(point.Stage), previous = point.Stage > 0 ? Cap(point.Stage - 1) : 0;
        int ceiling = Math.Max(minimumLevel, next - 1);
        int end = Math.Min(Math.Clamp(next - gap, 1, 100), ceiling);
        // Late windows can have a lower cap than an earlier one: an older higher cap never pushes a trainer over the next boss.
        if (previous > 0 && previous < next)
            end = Math.Max(end, Math.Min(previous, ceiling));
        end = Math.Min(end, ceiling);

        int start;
        if (previous > 0)
            start = Math.Min(end, previous);
        else
        {
            // The first window has no previous milestone: it starts at the lowest original ace of its trainers.
            int lowest = Points.Where(p => p.Value.Stage == point.Stage && !IsMilestone(p.Key)).Select(p => originalAce(p.Key)).OfType<int>().DefaultIfEmpty(end).Min();
            start = Math.Max(minimumLevel, Math.Min(Math.Clamp(lowest, 1, 100), end));
        }

        var orders = _orders.TryGetValue(point.Stage, out var o) ? o : [];
        double progress = 1.0;
        if (orders.Length > 1)
        {
            int rank = 0;
            for (int i = 0; i < orders.Length && orders[i] <= point.Order; i++)
                rank = i;
            progress = rank / (double)(orders.Length - 1);
        }
        double curved = Math.Pow(Math.Clamp(progress, 0, 1), power > 0 && !double.IsNaN(power) ? power : CurvePower);
        int target = (int)Math.Round(start + (end - start) * curved, MidpointRounding.AwayFromZero);
        return Math.Max(minimumLevel, Math.Clamp(target, start, end));
    }
}
