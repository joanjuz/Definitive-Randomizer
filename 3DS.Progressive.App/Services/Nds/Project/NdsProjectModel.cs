using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds.Project;

// Persistent Nintendo DS project (file «Juego.nds.drproj»), format version 1. This file only DEFINES the contract and the container: nothing here is connected to the
// editors yet (PR 1). The modified binary tables are the source of truth; recipes, seeds and change lists are metadata kept for the history and for Global Template.

/// <summary>Identity of the ROM a project belongs to. A project is never associated with another ROM, whatever its name or folder.</summary>
internal sealed record NdsProjectRom(string Sha256, string Sha1, string GameCode, int Revision, string Game, long Length, bool IsRomhack, string LastKnownPath)
{
    internal string ProfileKey => GameCode + "/" + Revision;
}

/// <summary>One confirmed operation (same fields as <see cref="NdsOperation"/>): metadata only, the resulting tables live in the state.</summary>
internal sealed record NdsProjectOperationRecord(string Action, uint? Seed, JsonObject Recipe, IReadOnlyList<Gen5Change> Changes)
{
    internal static NdsProjectOperationRecord Of(NdsOperation operation) => new(operation.Action, operation.Seed, (JsonObject)operation.Recipe.DeepClone(), operation.Changes);
    internal NdsOperation ToOperation() => new(Action, Seed, Changes, (JsonObject)Recipe.DeepClone());
}

/// <summary>
/// Event history of Pokémon Platinum (CPUE/1), in the shape of the checkpoints v1, v2 and v3 so that those files import into a project and can be exported again.
/// <c>Operations</c> is always the ordered list (a v1 checkpoint's moves are converted); <c>ImportedFrom</c> records the checkpoint version it came from, if any.
/// </summary>
internal sealed record NdsProjectEvents(IReadOnlyList<NdsEventOperation> Operations, IReadOnlyList<string> Audit, int? ImportedFrom = null)
{
    /// <summary>Same rule as <c>NdsEventSession.Build</c>: v1 when there are only moves, v3 when a new NPC has a script, v2 otherwise.</summary>
    internal int CheckpointVersion => Operations.All(o => o.Kind == NdsEventOperation.MoveKind) ? 1 : Operations.Any(o => o.Add?.Script is not null) ? 3 : 2;

    internal static NdsProjectEvents FromCheckpoint(NdsEventDocument document)
    {
        if (document.Version is < 1 or > 3) throw new InvalidDataException("Solo se importan checkpoints de eventos v1, v2 y v3.");
        var operations = document.Version >= 2 ? document.Operations ?? throw new InvalidDataException("Checkpoint v2/v3 sin operaciones.") : document.Edits.Select(NdsEventOperation.OfMove).ToList();
        return new(operations.ToList().AsReadOnly(), document.Audit.ToList().AsReadOnly(), document.Version);
    }

    internal NdsEventDocument ToCheckpoint(string sourcePath, string sourceSha256) => CheckpointVersion == 1
        ? new(NdsEventSession.Format, 1, "CPUE", 1, sourcePath, sourceSha256, Operations.Select(o => o.Move!).ToList().AsReadOnly(), Audit)
        : new(NdsEventSession.Format, CheckpointVersion, "CPUE", 1, sourcePath, sourceSha256, Array.Empty<NdsNpcEdit>(), Audit, Operations);
}

/// <summary>Where a stored table comes from. The container names blobs by kind and key.</summary>
internal enum NdsProjectBlobKind { File, Overlay, Arm9, EventData, EventScripts }

internal sealed record NdsProjectBlobEntry(string Name, NdsProjectBlobKind Kind, long RawLength, long StoredLength, string Sha256, string Codec);

/// <summary>
/// The persistent metadata (manifest). Unknown properties written by a newer build are kept in <see cref="Extra"/> and written back, so an older build that is
/// allowed to read the file (same major version) never drops them. <see cref="Blobs"/> is the index of the stored tables; it is filled by the codec.
/// </summary>
internal sealed record NdsProjectMeta
{
    internal const string FormatName = "DefinitiveRandomizer.NdsProject";
    internal const int CurrentVersion = 1;
    public string Format { get; init; } = FormatName;
    public int FormatVersion { get; init; } = CurrentVersion;
    /// <summary>Oldest reader that understands this file; a reader with a lower version refuses it instead of guessing.</summary>
    public int MinReaderVersion { get; init; } = 1;
    public string Writer { get; init; } = "";
    public Guid ProjectId { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime SavedUtc { get; init; }
    /// <summary>Increases by one with every durable commit; it is the compare-and-swap token of the store.</summary>
    public long Generation { get; init; }
    public required NdsProjectRom Rom { get; init; }
    public IReadOnlyList<NdsProjectOperationRecord> Operations { get; init; } = [];
    public NdsProjectEvents? Events { get; init; }
    /// <summary>Overlays that must be recompressed on export (state, not metadata).</summary>
    public IReadOnlyList<int> RecompressOverlays { get; init; } = [];
    /// <summary>Project configuration that is not a table (for example whether romhack tables are accepted).</summary>
    public JsonObject? Settings { get; init; }
    public IReadOnlyList<NdsProjectBlobEntry>? Blobs { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>The modified tables, exactly as the project would export them (files by their ROM path, overlays by id, the ARM9, and the Platinum event data).</summary>
internal sealed class NdsProjectState
{
    internal Dictionary<string, byte[]> Files { get; init; } = new(StringComparer.Ordinal);
    internal Dictionary<int, byte[]> Overlays { get; init; } = new();
    internal byte[]? Arm9 { get; init; }
    internal byte[]? EventData { get; init; }
    internal byte[]? EventScripts { get; init; }

    internal bool SameAs(NdsProjectState other) =>
        Files.Count == other.Files.Count && Files.All(p => other.Files.TryGetValue(p.Key, out var b) && p.Value.AsSpan().SequenceEqual(b)) &&
        Overlays.Count == other.Overlays.Count && Overlays.All(p => other.Overlays.TryGetValue(p.Key, out var b) && p.Value.AsSpan().SequenceEqual(b)) &&
        Same(Arm9, other.Arm9) && Same(EventData, other.EventData) && Same(EventScripts, other.EventScripts);
    private static bool Same(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
}

/// <summary>A complete project at one generation: what is written to, and read from, the store in a single durable step.</summary>
internal sealed record NdsProjectDocument(NdsProjectMeta Meta, NdsProjectState State)
{
    internal long Generation => Meta.Generation;
    internal int OperationCount => Meta.Operations.Count + (Meta.Events?.Operations.Count ?? 0);
}
