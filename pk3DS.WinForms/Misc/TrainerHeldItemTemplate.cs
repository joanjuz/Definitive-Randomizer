using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using pk3DS.Core;

namespace pk3DS.WinForms;

public sealed class TrainerHeldItemTemplate
{
    private const string TemplateFolder = "Templates";
    private const string TemplateFileName = "trainer_held_items.txt";

    private readonly TrainerHeldItemRule[] Rules;
    private readonly int[] FallbackPool;
    public IReadOnlyList<string> Warnings { get; }

    private TrainerHeldItemTemplate(IEnumerable<TrainerHeldItemRule> rules, IEnumerable<int> fallbackPool, IReadOnlyList<string> warnings)
    {
        Rules = rules.ToArray();
        FallbackPool = fallbackPool.Where(i => i > 0).Distinct().ToArray();
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
            // If the executable folder is read-only, LoadOrCreateDefault reports the warning.
        }
    }

    public static TrainerHeldItemTemplate LoadOrCreateDefault(string[] itemNames, IEnumerable<int> fallbackPool)
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
            warnings.Add($"Could not create held item template at '{path}': {ex.Message}");
        }

        if (!File.Exists(path))
            return new TrainerHeldItemTemplate([], fallbackPool, warnings);

        return Load(path, itemNames, fallbackPool, warnings);
    }

    private static TrainerHeldItemTemplate Load(string path, string[] itemNames, IEnumerable<int> fallbackPool, List<string> warnings)
    {
        var lookup = BuildItemLookup(itemNames);
        var rules = new List<TrainerHeldItemRule>();
        int[] fallback = fallbackPool.Where(i => i > 0).Distinct().ToArray();

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not read held item template '{path}': {ex.Message}");
            return new TrainerHeldItemTemplate([], fallback, warnings);
        }

        for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            string line = StripComment(lines[lineNumber]).Trim();
            if (line.Length == 0)
                continue;

            string[] parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length < 6)
            {
                warnings.Add($"Line {lineNumber + 1}: expected Scope | MinLevel | MaxLevel | Chance | Mode | Items.");
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

            if (!TryParseMode(parts[4], out TrainerHeldItemMode mode))
            {
                warnings.Add($"Line {lineNumber + 1}: invalid mode '{parts[4]}'. Use Random, Smart, Clear or Keep.");
                continue;
            }

            bool usePool = false;
            var itemIDs = new List<int>();
            foreach (string itemToken in string.Join('|', parts.Skip(5)).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (itemToken.Equals("POOL", StringComparison.OrdinalIgnoreCase) || itemToken.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    usePool = true;
                    continue;
                }

                if (int.TryParse(itemToken, out int itemID))
                {
                    if (itemID > 0)
                        itemIDs.Add(itemID);
                    continue;
                }

                if (lookup.TryGetValue(Normalize(itemToken), out itemID))
                {
                    itemIDs.Add(itemID);
                    continue;
                }

                warnings.Add($"Line {lineNumber + 1}: item '{itemToken}' was not found. Use the exact item name or its numeric ID.");
            }

            if ((mode == TrainerHeldItemMode.Random || mode == TrainerHeldItemMode.Smart) && !usePool && itemIDs.Count == 0)
            {
                warnings.Add($"Line {lineNumber + 1}: no valid items found for mode {mode}.");
                continue;
            }

            if (maxLevel < minLevel)
                (minLevel, maxLevel) = (maxLevel, minLevel);

            rules.Add(new TrainerHeldItemRule(
                parts[0],
                Math.Clamp(minLevel, 1, 100),
                Math.Clamp(maxLevel, 1, 100),
                Math.Clamp(chance, 0, 100),
                mode,
                usePool,
                itemIDs.Distinct().ToArray()
            ));
        }

        return new TrainerHeldItemTemplate(rules, fallback, warnings);
    }

    public int PickItem(
        int trainerID,
        bool isImportantTrainer,
        string trainerGroup,
        int currentItem,
        int species,
        int form,
        int level,
        IEnumerable<int> moves,
        IEnumerable<int> runtimePool,
        int abilitySlot,
        bool isFinalEvolution,
        int smartMode,
        IEnumerable<int> excludedItems = null)
    {
        var rule = Rules.LastOrDefault(r => r.Matches(trainerID, isImportantTrainer, trainerGroup, level));
        if (rule is null)
            return Math.Max(0, currentItem);

        if (rule.Chance <= 0)
            return 0;

        if (rule.Chance < 100 && (Util.Random32() % 100) >= rule.Chance)
            return 0;

        return rule.Mode switch
        {
            TrainerHeldItemMode.Clear => 0,
            TrainerHeldItemMode.Keep => Math.Max(0, currentItem),
            TrainerHeldItemMode.Random => PickRandom(rule, runtimePool, currentItem, excludedItems),
            TrainerHeldItemMode.Smart => PickSmart(rule, runtimePool, currentItem, species, form, level, moves, abilitySlot, isFinalEvolution, smartMode, excludedItems),
            _ => Math.Max(0, currentItem),
        };
    }

    private int PickRandom(TrainerHeldItemRule rule, IEnumerable<int> runtimePool, int currentItem, IEnumerable<int> excludedItems)
    {
        int[] pool = BuildPool(rule, runtimePool, excludedItems);
        if (pool.Length == 0)
            return Math.Max(0, currentItem);

        return pool[Util.Random32() % pool.Length];
    }

    private int PickSmart(
        TrainerHeldItemRule rule,
        IEnumerable<int> runtimePool,
        int currentItem,
        int species,
        int form,
        int level,
        IEnumerable<int> moves,
        int abilitySlot,
        bool isFinalEvolution,
        int smartMode,
        IEnumerable<int> excludedItems = null)
    {
        int[] pool = BuildPool(rule, runtimePool, excludedItems);
        if (pool.Length == 0)
            return Math.Max(0, currentItem);

        int item = SmartTrainerItemPicker.Pick(species, form, level, moves, pool, abilitySlot, isFinalEvolution, smartMode);
        return Math.Max(0, item);
    }

    private int[] BuildPool(TrainerHeldItemRule rule, IEnumerable<int> runtimePool, IEnumerable<int> excludedItems)
    {
        int[] pool;
        if (!rule.UsePool)
        {
            pool = rule.Items;
        }
        else
        {
            pool = runtimePool.Where(i => i > 0).Distinct().ToArray();
            if (pool.Length == 0)
                pool = FallbackPool;
        }

        int[] original = pool.Where(i => i > 0).Distinct().ToArray();
        if (excludedItems is null)
            return original;

        var excluded = excludedItems.Where(i => i > 0).ToHashSet();
        if (excluded.Count == 0)
            return original;

        int[] filtered = original.Where(i => !excluded.Contains(i)).ToArray();
        return filtered.Length > 0 ? filtered : original;
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf('#');
        return idx >= 0 ? line[..idx] : line;
    }

    private static bool TryParseMode(string value, out TrainerHeldItemMode mode)
    {
        return Enum.TryParse(value.Trim(), true, out mode);
    }

    private static Dictionary<string, int> BuildItemLookup(string[] itemNames)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < itemNames.Length; i++)
        {
            string name = itemNames[i];
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string key = Normalize(name);
            if (!lookup.ContainsKey(key))
                lookup[key] = i;
        }

        return lookup;
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static string GetDefaultTemplate() => """
# Trainer held item template
# Path: Templates/trainer_held_items.txt
#
# Format:
# Scope | MinLevel | MaxLevel | Chance | Mode | Items
#
# Scope:
#   Any          = all trainers
#   Regular      = non-important trainers
#   Important    = important trainers
#   Boss         = alias for Important
#   Trainer:123  = specific trainer ID
#   Group:GYM    = Gen6 tags/groups such as GYM, ELITE, CHAMPION, etc.
#
# Mode:
#   Random = chooses a random item from Items
#   Smart  = uses SmartTrainerItemPicker with Items as the allowed pool
#   Clear  = removes held item
#   Keep   = keeps the current held item
#
# Items:
#   Use POOL to use the current legal random item pool.
#   Or write exact item names / numeric item IDs separated by commas.
#
# Examples with names:
# Any       | 1  | 20  | 40  | Random | Oran Berry, Sitrus Berry
# Important | 35 | 100 | 100 | Smart  | Leftovers, Life Orb, Choice Scarf, Choice Band, Choice Specs, Focus Sash
# Trainer:5 | 1 | 100 | 100 | Clear  | POOL

Any       | 1  | 15  | 25  | Random | POOL
Any       | 16 | 30  | 40  | Random | POOL
Any       | 31 | 45  | 60  | Smart  | POOL
Any       | 46 | 100 | 75  | Smart  | POOL
Important | 1  | 100 | 100 | Smart  | POOL
""";

    private sealed record TrainerHeldItemRule(
        string Scope,
        int MinLevel,
        int MaxLevel,
        int Chance,
        TrainerHeldItemMode Mode,
        bool UsePool,
        int[] Items)
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

    private enum TrainerHeldItemMode
    {
        Random,
        Smart,
        Clear,
        Keep,
    }
}
