using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds.Project;

internal enum NdsProjectAssociationStatus
{
    /// <summary>Same game, same revision, same bytes: the project belongs to this ROM.</summary>
    Match,
    /// <summary>Another game or another revision (code or revision differ).</summary>
    DifferentGame,
    /// <summary>Same game and revision but not the same bytes (SHA-256 or length differ: another dump, a patched copy or a romhack).</summary>
    DifferentRom,
}

internal sealed record NdsProjectAssociation(NdsProjectAssociationStatus Status, string Message)
{
    internal bool IsMatch => Status == NdsProjectAssociationStatus.Match;

    /// <summary>
    /// A project is only ever used with the ROM whose full SHA-256 it records. The file name, the folder and the last known path are hints for finding the ROM
    /// again, never evidence. Nothing here binds a project to a different ROM: a mismatch is reported and the caller must refuse.
    /// </summary>
    internal static NdsProjectAssociation Evaluate(NdsProjectRom stored, NdsProjectRom actual)
    {
        if (!string.Equals(stored.GameCode, actual.GameCode, StringComparison.Ordinal) || stored.Revision != actual.Revision)
            return new(NdsProjectAssociationStatus.DifferentGame, $"El proyecto es de {stored.Game} ({stored.ProfileKey}) y la ROM es {actual.Game} ({actual.ProfileKey}).");
        if (!string.Equals(stored.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase) || stored.Length != actual.Length)
            return new(NdsProjectAssociationStatus.DifferentRom, "La ROM es del mismo juego pero no es la misma que usó el proyecto (SHA-256 distinto): puede ser otro volcado, una copia modificada o un romhack.");
        return new(NdsProjectAssociationStatus.Match, "La ROM coincide con la del proyecto.");
    }
}

/// <summary>Where the files of a project live, derived from the ROM path. Pure functions; nothing is created here.</summary>
internal static class NdsProjectPaths
{
    internal const string Extension = ".drproj";
    internal const int MaxBackups = 3;
    internal static string ProjectOf(string romPath) => romPath + Extension;
    internal static string BackupOf(string projectPath, int number) => number is >= 1 and <= MaxBackups ? projectPath + ".bak" + number : throw new ArgumentOutOfRangeException(nameof(number));
    internal static string LockOf(string projectPath) => projectPath + ".lock";
    /// <summary>Name under which a damaged main file is preserved for diagnosis (never deleted or overwritten).</summary>
    internal static string DamagedOf(string projectPath, DateTime utc) => projectPath + ".damaged-" + utc.ToString("yyyyMMddTHHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}

internal static class NdsProjectRomIdentity
{
    /// <summary>
    /// Reads the identity of a ROM: the full SHA-256 and SHA-1 of the file, its game code and revision from the header (checksummed by <see cref="NdsRom"/>) and the
    /// supported profile it matches. A ROM whose game or revision is not a supported profile is refused; it is the engine, not the project, that judges its tables.
    /// </summary>
    internal static NdsProjectRom Compute(string path, bool isRomhack)
    {
        string full = Path.GetFullPath(path);
        using var rom = NdsRom.Open(full);
        var profile = NdsIdentity.Profiles.FirstOrDefault(p => p.GameCode == rom.Header.GameCode && p.Version == rom.Header.Version)
            ?? throw new InvalidDataException($"ROM no admitida: {rom.Header.GameCode} revisión {rom.Header.Version}.");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[1 << 20]; int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { sha256.AppendData(buffer, 0, read); sha1.AppendData(buffer, 0, read); }
        return new(Convert.ToHexString(sha256.GetHashAndReset()), Convert.ToHexString(sha1.GetHashAndReset()), profile.GameCode, profile.Version, profile.Game, stream.Length, isRomhack, full);
    }
}
