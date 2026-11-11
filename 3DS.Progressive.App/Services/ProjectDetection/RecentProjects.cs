using System.IO;
using System.Text.Json;

namespace ThreeDS.Progressive.Services.ProjectDetection;

/// <summary>A project that was opened: where it is and what the detector said it was. Nothing of its content.</summary>
internal sealed record RecentProject(string Path, string Game, string Platform, DateTime OpenedUtc)
{
    /// <summary>Whether the file or folder is still at that path. An entry that is not can only be removed from the list.</summary>
    internal bool Available => File.Exists(Path) || Directory.Exists(Path);
    internal string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
}

/// <summary>
/// The list of recently opened projects (phase A4). It lives in a file of its own in the user's local application data, next to the preferences: it is never
/// written to a log, a report, a project or a ROM, and never leaves the machine. It is a convenience: any problem reading or writing it gives an empty list
/// or is ignored, and removing an entry only edits this list (no file of a project is ever deleted from here). Opening a recent project goes through the
/// same detector and engines as any other path, so a stale or tampered entry cannot open anything by itself.
/// </summary>
internal static class RecentProjects
{
    internal const int Max = 12;
    private static readonly object Gate = new();
    /// <summary>
    /// The file of the list, or null when there is none to use: the automated checks only ever use a state folder given to them (PROGRESSIVE_STATE_DIR) and
    /// never the list of the person running them.
    /// </summary>
    private static string? FilePath => Environment.GetEnvironmentVariable("PROGRESSIVE_STATE_DIR") is { Length: > 0 } state ? System.IO.Path.Combine(state, "recent-projects.json")
        : StudioDialog.AutomatedChecks ? null
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3DS-Progressive", "recent-projects.json");

    /// <summary>Newest first. Entries that are not usable as entries (no path, a relative path) are dropped; missing files are kept so that the user sees them.</summary>
    internal static IReadOnlyList<RecentProject> Load()
    {
        lock (Gate)
        {
            try
            {
                if (FilePath is not { } file || !File.Exists(file) || new FileInfo(file).Length > 1024 * 1024) return [];
                var entries = JsonSerializer.Deserialize<List<RecentProject?>>(File.ReadAllText(file)) ?? [];
                return entries.Where(e => e is { Path.Length: > 0, Game: not null, Platform: not null } && System.IO.Path.IsPathFullyQualified(e.Path)).Select(e => e!)
                    .GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(Max).ToList().AsReadOnly();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return []; }
        }
    }

    /// <summary>Puts a project that was just opened at the top of the list.</summary>
    internal static void Record(string path, string game, string platform)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string full; try { full = System.IO.Path.GetFullPath(path); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        Write([new RecentProject(full, game, platform, DateTime.UtcNow), .. Load().Where(e => !e.Path.Equals(full, StringComparison.OrdinalIgnoreCase))]);
    }

    /// <summary>Removes one entry from the list. The project itself is not touched.</summary>
    internal static void Remove(string path) => Write(Load().Where(e => !e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));

    private static void Write(IEnumerable<RecentProject> entries)
    {
        lock (Gate)
        {
            try
            {
                if (FilePath is not { } file) return;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!); string temporary = file + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(entries.Take(Max).ToList(), new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, file, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}