#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>
/// What the map and event editor needs to know about a game, whatever its generation. Gen 4 (<see cref="Gen4EventProfile"/>) and Gen 5
/// (<see cref="Gen5MapProfile"/>) implement it with their own measured values; nothing binary is shared through it.
/// </summary>
public interface INdsEventProfile
{
    string GameCode { get; }
    byte Revision { get; }
    string GameName { get; }
    string Key { get; }
    string Label { get; }
    /// <summary>The event archive the editor writes, and the script archive it exports (written only where scripts can be appended).</summary>
    string EventPath { get; }
    string ScriptPath { get; }
    bool SupportsNpcCreation { get; }
    int MaxObjectFacing { get; }
    /// <summary>Whether the interaction direction of background objects has an accredited meaning in this game and may be changed.</summary>
    bool EditsObjectFacing { get; }
    int MaxTrainerSight { get; }
    /// <summary>Why the sight of <paramref name="npc"/> cannot be shown or edited, or null when it can.</summary>
    string? SightRefusal(PlatinumEvent npc);
}

/// <summary>
/// The event data of one ROM as the editor presents and edits it: maps, events as <see cref="PlatinumEvent"/> records, the matrix as
/// <see cref="PlatinumMapLayout"/>, and the edits every generation accepts (move and turn NPC, edit background objects, warps and triggers). Each
/// implementation enforces its own rules and fingerprints. Adding NPC and scripts is Platinum's alone (<see cref="PlatinumEvents"/>).
/// </summary>
public interface INdsEventData
{
    INdsEventProfile Profile { get; }
    IReadOnlyList<PlatinumMap> Maps { get; }
    PlatinumMap Map(int mapId);
    IReadOnlyList<PlatinumEvent> Events(int mapId);
    PlatinumEvent Npc(int mapId, int index);
    PlatinumEvent Event(int mapId, PlatinumEventKind kind, int index);
    int NpcCount(int mapId);
    (int Width, int Height) Bounds(int mapId);
    PlatinumMapLayout Layout(int mapId);
    byte[] ReplaceNpc(int mapId, int index, PlatinumNpcValues expected, PlatinumNpcValues values);
    byte[] ReplaceEvent(int mapId, PlatinumEventKind kind, int index, PlatinumEventValues expected, PlatinumEventValues values);
    bool WarpTargetExists(int header, int anchor);
    string WarpStatus(PlatinumEvent warp);
    PlatinumScriptLink Script(int mapId, int reference);
    PlatinumScriptInfo? ClassifyScript(int mapId, int number);
    byte[] Bytes();
    byte[] ScriptArchiveBytes();
    /// <summary>A new instance with edited event bytes; refused when anything beyond the editable fields changed.</summary>
    INdsEventData WithEvents(byte[] events);
}

/// <summary>Every measured map and event profile, of any generation.</summary>
public static class NdsEventProfiles
{
    public static IReadOnlyList<INdsEventProfile> All { get; } = [.. Gen4EventProfile.All, .. Gen5MapProfile.All];
    public static INdsEventProfile? For(string gameCode, int revision) => All.FirstOrDefault(p => p.GameCode == gameCode && p.Revision == revision);
    public static string SupportedLabels => string.Join(", ", All.Select(p => p.Label));
}
