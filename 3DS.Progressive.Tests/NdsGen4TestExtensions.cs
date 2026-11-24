using pk3DS.Core.NDS;

/// <summary>
/// The Gen 4 operations the existing checks use on a session's data (fingerprints, new NPC, scripts). The editor holds the data as
/// <see cref="INdsEventData"/>; these checks run on Gen 4 ROMs only and say so explicitly.
/// </summary>
internal static class NdsGen4TestExtensions
{
    internal static PlatinumEvents Gen4(this INdsEventData data) => data as PlatinumEvents ?? throw new InvalidOperationException("These checks need Gen 4 event data.");
    internal static string NormalizedHash(this INdsEventData data) => data.Gen4().NormalizedHash();
    internal static string EditableHash(this INdsEventData data) => data.Gen4().EditableHash();
    internal static string EventsHash(this INdsEventData data) => data.Gen4().EventsHash();
    internal static IReadOnlyList<string> ValidateNewNpc(this INdsEventData data, int mapId, byte[] record, int? allowedScript = null) => data.Gen4().ValidateNewNpc(mapId, record, allowedScript);
    internal static int ScriptEntryCount(this INdsEventData data, int mapId) => data.Gen4().ScriptEntryCount(mapId);
}
