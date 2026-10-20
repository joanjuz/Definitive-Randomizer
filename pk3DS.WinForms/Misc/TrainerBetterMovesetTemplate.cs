using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using pk3DS.Core;

namespace pk3DS.WinForms;

public sealed class TrainerBetterMovesetTemplate
{
    private const string TemplateFolder = "Templates";
    private const string TemplateFileName = "trainer_better_movesets.txt";

    private readonly TrainerBetterMovesetRule[] Rules;
    public IReadOnlyList<string> Warnings { get; }

    private TrainerBetterMovesetTemplate(IEnumerable<TrainerBetterMovesetRule> rules, IReadOnlyList<string> warnings)
    {
        Rules = rules.ToArray();
        Warnings = warnings;
    }

    public static string DefaultPath => Path.Combine(Environment.GetEnvironmentVariable("DEFINITIVE_GLOBAL_PREVIEW_ROOT")??AppDomain.CurrentDomain.BaseDirectory, TemplateFolder, TemplateFileName);

    public static void EnsureDefaultFile()
    {
        try
        {
            string path = DefaultPath;
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            if (!File.Exists(path))
                File.WriteAllText(path, GetDefaultTemplate());
        }
        catch
        {
            // The template is also created when loading for randomization.
        }
    }

    public static TrainerBetterMovesetTemplate LoadOrCreateDefault()
    {
        var warnings = new List<string>();
        string path = DefaultPath;

        try
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            if (!File.Exists(path))
                File.WriteAllText(path, GetDefaultTemplate());
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not create better moveset template at '{path}': {ex.Message}");
        }

        if (!File.Exists(path))
            return new TrainerBetterMovesetTemplate([], warnings);

        return Load(path, warnings);
    }

    private static TrainerBetterMovesetTemplate Load(string path, List<string> warnings)
    {
        var rules = new List<TrainerBetterMovesetRule>();

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not read better moveset template '{path}': {ex.Message}");
            return new TrainerBetterMovesetTemplate([], warnings);
        }

        for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            string line = StripComment(lines[lineNumber]).Trim();
            if (line.Length == 0)
                continue;

            string[] parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length < 5)
            {
                warnings.Add($"Line {lineNumber + 1}: expected Scope | MinLevel | MaxLevel | Chance | Mode.");
                continue;
            }

            if (parts[0].Equals("Scope", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!int.TryParse(parts[1], out int minLevel) || !int.TryParse(parts[2], out int maxLevel))
            {
                warnings.Add($"Line {lineNumber + 1}: invalid level range.");
                continue;
            }

            if (!int.TryParse(parts[3], out int chance))
            {
                warnings.Add($"Line {lineNumber + 1}: invalid chance value.");
                continue;
            }

            if (!TryParseMode(parts[4], out TrainerBetterMovesetMode mode))
            {
                warnings.Add($"Line {lineNumber + 1}: invalid mode '{parts[4]}'. Use Better, Keep, Off or Clear.");
                continue;
            }

            if (maxLevel < minLevel)
                (minLevel, maxLevel) = (maxLevel, minLevel);

            rules.Add(new TrainerBetterMovesetRule(
                parts[0],
                Math.Clamp(minLevel, 1, 100),
                Math.Clamp(maxLevel, 1, 100),
                Math.Clamp(chance, 0, 100),
                mode
            ));
        }

        return new TrainerBetterMovesetTemplate(rules, warnings);
    }

    public bool ShouldApply(int trainerID, bool isImportantTrainer, string trainerGroup, int level)
    {
        var rule = Rules.LastOrDefault(r => r.Matches(trainerID, isImportantTrainer, trainerGroup, level));
        if (rule is null)
            return false;

        if (rule.Mode != TrainerBetterMovesetMode.Better)
            return false;

        if (rule.Chance <= 0)
            return false;

        if (rule.Chance >= 100)
            return true;

        return (Util.Random32() % 100) < rule.Chance;
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf('#');
        return idx >= 0 ? line[..idx] : line;
    }

    private static bool TryParseMode(string value, out TrainerBetterMovesetMode mode)
    {
        string normalized = value.Trim();
        if (normalized.Equals("On", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Apply", StringComparison.OrdinalIgnoreCase))
            normalized = nameof(TrainerBetterMovesetMode.Better);
        if (normalized.Equals("None", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Skip", StringComparison.OrdinalIgnoreCase))
            normalized = nameof(TrainerBetterMovesetMode.Keep);

        return Enum.TryParse(normalized, true, out mode);
    }

    private static string GetDefaultTemplate() => """
# Trainer better moveset template
# Path: Templates/trainer_better_movesets.txt
#
# This file is used only when the global Better Movesets checkbox is enabled.
# Per-trainer Move Rules with Better Movesets checked still force Better Movesets.
#
# Format:
# Scope | MinLevel | MaxLevel | Chance | Mode
#
# Scope:
#   Any          = all trainers
#   Regular      = non-important trainers
#   Important    = important trainers
#   Boss         = alias for Important
#   Trainer:123  = specific trainer ID
#   Group:GYM    = Gen6 tags/groups such as GYM, ELITE, CHAMPION, RIVAL, etc.
#
# Mode:
#   Better = roll Chance and apply Better Movesets if it succeeds
#   Keep   = do not apply Better Movesets for that matching rule
#   Off    = alias for Keep
#   Clear  = alias for Keep
#
# Last matching rule wins. Put broad rules first and exceptions later.
#
# Examples:
# Any       | 1  | 20  | 25  | Better
# Regular   | 41 | 100 | 65  | Better
# Important | 1  | 100 | 100 | Better
# Group:RIVAL | 1 | 100 | 100 | Better

Any       | 1  | 15  | 20  | Better
Any       | 16 | 30  | 35  | Better
Any       | 31 | 45  | 55  | Better
Any       | 46 | 100 | 70  | Better
Important | 1  | 30  | 80  | Better
Important | 31 | 100 | 100 | Better
""";

    private sealed record TrainerBetterMovesetRule(
        string Scope,
        int MinLevel,
        int MaxLevel,
        int Chance,
        TrainerBetterMovesetMode Mode)
    {
        public bool Matches(int trainerID, bool isImportantTrainer, string trainerGroup, int level)
        {
            if (level < MinLevel || level > MaxLevel)
                return false;

            string scope = Scope.Trim();
            if (scope.Equals("Any", StringComparison.OrdinalIgnoreCase) || scope.Equals("All", StringComparison.OrdinalIgnoreCase))
                return true;
            if (scope.Equals("Regular", StringComparison.OrdinalIgnoreCase))
                return !isImportantTrainer;
            if (scope.Equals("Important", StringComparison.OrdinalIgnoreCase) || scope.Equals("Boss", StringComparison.OrdinalIgnoreCase))
                return isImportantTrainer;
            if (scope.StartsWith("Trainer:", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(scope[8..], out int id) && id == trainerID;
            if (scope.StartsWith("Group:", StringComparison.OrdinalIgnoreCase))
                return GroupMatches(trainerGroup, scope[6..]);

            return GroupMatches(trainerGroup, scope);
        }

        private static bool GroupMatches(string trainerGroup, string expected)
        {
            if (string.IsNullOrWhiteSpace(trainerGroup) || string.IsNullOrWhiteSpace(expected))
                return false;

            return trainerGroup.Contains(expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private enum TrainerBetterMovesetMode
    {
        Better,
        Keep,
        Off,
        Clear,
    }
}
