using System.IO;
using System.Security.Cryptography;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Personal balance templates (Eris-Ultranova) are an optional package: they are never a default.
/// New Batch and Template Studio operations require a CSV chosen by the user. Only recipes saved
/// before this change, which recorded the former built-in balance as <c>TemplatePath = null</c>,
/// still resolve to these files so they replay exactly as they were recorded.
/// </summary>
internal static class OptionalTemplates
{
    internal const string RelativeErisFolder = "plantillas-opcionales/eris-ultranova";
    internal static string ErisFolder => Path.Combine(AppContext.BaseDirectory, "plantillas-opcionales", "eris-ultranova");

    // Gen6 recorded the neutral moves_gen6.csv and completed it with the former built-in rows;
    // GameMoveService keeps that fallback only for these legacy recipes.
    internal static string LegacyMoveTemplate(int generation) => generation == 7
        ? Path.Combine(ErisFolder, "moves_gen7.csv")
        : Path.Combine(AppContext.BaseDirectory, "custom_balance_templates", $"moves_gen{generation}.csv");

    internal static string LegacyStatsTemplate(int generation) => generation == 7
        ? Path.Combine(ErisFolder, "pokemon_stats_gen7.csv")
        : Path.Combine(AppContext.BaseDirectory, "custom_balance_templates", $"pokemon_stats_gen{generation}.csv");

    /// <summary>
    /// Finds a recipe asset by name. A copy whose SHA-256 matches the recipe wins; otherwise the first
    /// existing copy is returned (the caller reports the difference). The optional package is only
    /// considered on an exact SHA-256 match, so it can never replace a different file silently.
    /// </summary>
    internal static string? FindAsset(IEnumerable<string> directories, string fileName, string? expectedSha256)
    {
        var candidates = directories.Where(d => d.Length > 0 && Directory.Exists(d)).Select(d => Path.Combine(d, fileName)).Where(File.Exists).ToList();
        if (expectedSha256 is { Length: > 0 })
        {
            string optional = Path.Combine(ErisFolder, fileName);
            foreach (string path in candidates.Append(optional).Where(File.Exists))
                if (Sha256(path).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) return path;
        }
        return candidates.FirstOrDefault();
    }

    internal static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
