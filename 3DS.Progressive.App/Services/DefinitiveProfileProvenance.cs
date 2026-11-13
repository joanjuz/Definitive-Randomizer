using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ThreeDS.Progressive.Services;

/// <summary>Historical declarations from the latest effective committed recipe; never a current-ROM attestation.</summary>
internal static class DefinitiveProfileProvenance
{
    private static string Text(JsonNode? node, int max)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text) ||
            text.Length is 0 || text.Length > max || text.Trim() != text ||
            text.Any(c => char.IsControl(c) || c is '/' or '\\' or ':')) throw new InvalidDataException("Metadato no portable.");
        return text;
    }

    internal static JsonObject? Build(GameProjectSession project, string gameId)
    {
        try
        {
            var entries = EffectiveHistory.Entries(project.History.ReadForDataExport().Entries);
            var entry = entries.LastOrDefault(e => e.Parameters.ContainsKey("GlobalTemplateSnapshot"));
            if (entry is null || entry.Kind != "Action" || !entry.Parameters.TryGetValue("GlobalApplication", out var batch) ||
                !Guid.TryParse(batch, out _) || !entry.Parameters.TryGetValue("TemplateSha256", out var expected)) return null;
            string snapshot = entry.Parameters["GlobalTemplateSnapshot"];
            if (Encoding.UTF8.GetByteCount(snapshot) > ShareableProfile.MaxBytes) return null;
            var recipe = GlobalTemplateDocument.Parse(snapshot, "USUM", 7);
            if (recipe.Sha256 != expected || recipe.Section("ProgressiveApp")?["Profile"] is not JsonObject profile) return null;
            string[] keys = ["Format", "Version", "Name", "Author", "RecipeSha256", "Sections"];
            if (profile.Any(p => !keys.Contains(p.Key)) || profile["Format"]?.GetValue<string>() != ShareableProfile.Format ||
                profile["Version"]?.GetValue<int>() != 1) return null;
            string name = Text(profile["Name"], 120);
            string hash = Text(profile["RecipeSha256"], 64);
            if (!Regex.IsMatch(hash, "\\A[0-9A-F]{64}\\z")) return null;
            string? author = null;
            if (profile["Author"] is JsonValue authorValue && authorValue.TryGetValue<string>(out var authorText) && authorText == "") { }
            else if (profile.ContainsKey("Author")) author = Text(profile["Author"], 80);
            if (profile["Sections"] is not JsonArray sections || sections.Count is < 1 or > 64) return null;
            var declared = sections.Select(s => s?.GetValue<string>() ?? "").ToArray();
            if (declared.Distinct(StringComparer.Ordinal).Count() != declared.Length ||
                declared.Any(s => s.Length > 90 || !Regex.IsMatch(s, "\\A(actions|manual|options)/[a-z0-9-]{1,80}\\z"))) return null;
            var present = ShareableProfile.AppliedSectionIds(recipe).ToHashSet(StringComparer.Ordinal);
            var selected = declared.Where(present.Contains).Order(StringComparer.Ordinal).ToArray();
            if (selected.Length == 0) return null;
            var metadata = new JsonObject { ["format"] = ShareableProfile.Format, ["version"] = 1, ["name"] = name, ["recipeSha256"] = hash };
            if (author is not null) metadata["author"] = author;
            var subsequent = entries.SkipWhile(e => e.Id != entry.Id).Skip(1)
                .Any(e => e.Parameters.GetValueOrDefault("GlobalApplication") != batch);
            var result = new JsonObject { ["version"] = 1, ["status"] = "historical", ["currentMatch"] = "not-verified",
                ["subsequentChanges"] = subsequent ? "recorded" : "none-recorded", ["gameId"] = gameId,
                ["profile"] = metadata, ["sections"] = new JsonArray(selected.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()) };
            return Encoding.UTF8.GetByteCount(result.ToJsonString()) <= 16 * 1024 ? result : null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or NotSupportedException or NullReferenceException)
        { return null; }
    }
}
