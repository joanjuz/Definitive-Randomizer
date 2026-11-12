using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// How far a patch is accredited for one concrete set of game files. Only <see cref="Verified"/> and
/// <see cref="CompatibleByAdaptation"/> allow writing; there is no way to force the other two.
/// </summary>
public enum PatchCapabilityLevel
{
    /// <summary>Every resource is in a known state and the installation was simulated without moving anything.</summary>
    Verified,

    /// <summary>The structure is recognised and the simulated installation succeeded by relocating into verified space.</summary>
    CompatibleByAdaptation,

    /// <summary>Another modification occupies a resource the patch needs.</summary>
    Conflict,

    /// <summary>Not enough evidence: missing or truncated data, a partial or earlier installation, a moved signature.</summary>
    NotAccredited,
}

/// <summary>What one resource (a hook site in one file) looks like.</summary>
public enum PatchResourceState
{
    Original,
    OwnInstall,
    OwnInstallEarlierFormat,
    PartialInstall,
    ExternalModification,
    Displaced,
    Truncated,
    Missing,
}

public sealed record PatchResourceReport(
    string Resource,
    string Role,
    PatchResourceState State,
    uint? Offset,
    string Evidence);

/// <summary>A run of bytes the simulated installation changes in a file that keeps its size.</summary>
public sealed record PatchChangedRange(
    string Resource,
    uint Offset,
    int Length);

/// <summary>
/// Read-only report of one patch against the files it would touch. Producing it never writes: the installation is
/// simulated on detached copies.
/// </summary>
public sealed record PatchCapabilityReport(
    string PatchId,
    PatchCapabilityLevel Level,
    IReadOnlyList<PatchResourceReport> Resources,
    IReadOnlyList<PatchChangedRange> Changes,
    IReadOnlyList<string> Resized,
    string Summary)
{
    public bool CanApply =>
        Level is PatchCapabilityLevel.Verified or PatchCapabilityLevel.CompatibleByAdaptation;

    public PatchResourceReport Resource(string name) =>
        Resources.First(r => r.Resource == name);
}
