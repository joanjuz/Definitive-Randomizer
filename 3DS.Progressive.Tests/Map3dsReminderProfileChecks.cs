using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

internal static class Map3dsReminderProfileChecks
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] Header(ulong title)
        {
            byte[] bytes = new byte[0x800];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x200), title);
            return bytes;
        }
        check(Gen6MoveReminderProfile.IdentityFailure(Header(Gen6MoveReminderProfile.XTitleId)) is null,
            "ReminderProfile_XIdentityAccepted");
        check(Gen6MoveReminderProfile.IdentityFailure(Header(Gen6MoveReminderProfile.YTitleId)) is { } y && y.Contains("Y") && y.Contains("independiente"),
            "ReminderProfile_YRemainsBlockedDespiteSharedScripts");
        check(Gen6MoveReminderProfile.IdentityFailure(Header(0x000400000011C400)) is { } other && other.Contains("Title ID"),
            "ReminderProfile_OrasIdentityRejectedEvenWithXyConfig");
        check(Gen6MoveReminderProfile.IdentityFailure(new byte[0x207]) is { } shortHeader && shortHeader.Contains("truncada"),
            "ReminderProfile_TruncatedIdentityRejected");
        byte[] content = [1, 4, 9, 16, 25];
        byte[] original = (byte[])content.Clone();
        string expected = Convert.ToHexString(SHA256.HashData(content));
        Gen6MoveReminderProfile.RequireHash(content, expected, "synthetic");
        check(content.AsSpan().SequenceEqual(original), "ReminderProfile_MatchingHashDoesNotMutateResource");
        content[2] ^= 1;
        bool mismatch = false;
        try { Gen6MoveReminderProfile.RequireHash(content, expected, "synthetic"); }
        catch (InvalidDataException ex) { mismatch = ex.Message.Contains("synthetic") && ex.Message.Contains("huella"); }
        check(mismatch, "ReminderProfile_SingleByteResourceChangeRejected");
        bool forged = false;
        try { Gen6MoveReminderProfile.RequireOriginalTemplate(Map3dsReminderChecks.Backend(Map3dsReminderChecks.Zones())); }
        catch (InvalidDataException ex) { forged = ex.Message.Contains("Plantilla"); }
        check(forged, "ReminderProfile_SyntheticBackendAdmissionCannotAuthorizeRealTemplate");
        bool noSnapshot = false;
        try { Gen6MoveReminderProfile.Revalidate(Map3dsReminderChecks.Backend(Map3dsReminderChecks.Zones()), new GameConfig(GameVersion.XY)); }
        catch (InvalidOperationException ex) { noSnapshot = ex.Message.Contains("acreditación"); }
        check(noSnapshot, "ReminderProfile_ExplicitCoreAdmissionCannotForgeProjectSnapshot");
        bool duplicate = false;
        byte[] model54 = Map3dsNurseChecks.ModelEntry(54);
        try { Gen6MoveReminderProfile.RequireModelLists([[.. model54, .. model54]], 0); }
        catch (InvalidDataException ex) { duplicate = ex.Message.Contains("duplicada"); }
        check(duplicate, "ReminderProfile_DuplicateModelRejectedEvenWhenEntriesMatch");
        bool originalMissing = false;
        try { Gen6MoveReminderProfile.RequireModelLists([[], model54], 0); }
        catch (InvalidDataException ex) { originalMissing = ex.Message.Contains("área original"); }
        check(originalMissing, "ReminderProfile_OriginalAreaMustStillLoadModel");
        byte[] archive = new byte[0x1C]; "CRAG"u8.CopyTo(archive);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 0x1C);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(16), 0x1C);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(20), 0x1C);
        Gen6MoveReminderProfile.RequireArchiveHeader(archive);
        foreach (var (offset, value, name) in new[] { (0, 0U, "Magic"), (4, 16U, "HeaderSize"), (16, 29U, "DataBounds"), (20, 29U, "FileSize") })
        {
            byte[] invalid = (byte[])archive.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(offset), value);
            bool rejected = false;
            try { Gen6MoveReminderProfile.RequireArchiveHeader(invalid); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "ReminderProfile_InvalidGarcHeaderRejected_" + name);
        }

        // Temporary synthetic headers/code only; no commercial resource fixture or profile override.
        string root = Path.Combine(Path.GetTempPath(), "reminder-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "RomFS"));
        Directory.CreateDirectory(Path.Combine(root, "ExeFS"));
        string header = Path.Combine(root, "exheader.bin"), code = Path.Combine(root, "ExeFS", ".code.bin");
        var config = new GameConfig(GameVersion.XY) { RomFS = Path.Combine(root, "RomFS"), ExeFS = Path.Combine(root, "ExeFS") };
        check(!Gen6MoveReminderProfile.Inspect(config).Allowed, "ReminderProfile_MissingHeaderRejectsBeforeAnyWrite");
        File.WriteAllBytes(header, Header(Gen6MoveReminderProfile.YTitleId));
        var deniedY = Gen6MoveReminderProfile.Inspect(config);
        check(!deniedY.Allowed && deniedY.Dependencies.Count == 0 && deniedY.Reason.Contains("Pokémon Y"),
            "ReminderProfile_YProjectRejectedBeforeReadingExecutable");
        File.WriteAllBytes(header, Header(Gen6MoveReminderProfile.XTitleId));
        check(!Gen6MoveReminderProfile.Inspect(config).Allowed, "ReminderProfile_MissingExecutableRejected");
        File.WriteAllBytes(code, original);
        var filesBefore = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var wrongCode = Gen6MoveReminderProfile.Inspect(config);
        check(!wrongCode.Allowed && wrongCode.Dependencies.Count == 0 && wrongCode.Reason.Contains("code.bin") && wrongCode.Reason.Contains("huella"),
            "ReminderProfile_UnaccreditedExecutableRejected");
        check(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == filesBefore.Count
            && filesBefore.All(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key))) == p.Value),
            "ReminderProfile_InspectionNeverWritesProject");
        File.Copy(code, Path.Combine(root, "ExeFS", "code.bin"));
        check(!Gen6MoveReminderProfile.Inspect(config).Allowed && Gen6MoveReminderProfile.Inspect(config).Reason.Contains("único"),
            "ReminderProfile_AmbiguousExecutableRejected");
        File.WriteAllBytes(Path.Combine(root, "DecryptedExHeader.bin"), Header(Gen6MoveReminderProfile.YTitleId));
        check(!Gen6MoveReminderProfile.Inspect(config).Allowed, "ReminderProfile_ConflictingHeadersRejected");

        string dependency = Path.Combine(root, "dependency"), another = Path.Combine(root, "another");
        File.WriteAllBytes(dependency, original); File.WriteAllBytes(another, original);
        EditorProposal Proposal(params (string Path, byte[] Fingerprint)[] dependencies) =>
            new(Guid.NewGuid(), "synthetic dependency lease", 0, [], [], new Dictionary<string, string>(), dependencies);
        byte[] fingerprint = SHA256.HashData(original);
        using (var lease = new Map3dsDependencyLease(Proposal((dependency, fingerprint))))
        {
            bool writeDenied = false;
            try { using var writer = new FileStream(dependency, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }
            catch (IOException) { writeDenied = true; }
            check(writeDenied && File.ReadAllBytes(dependency).AsSpan().SequenceEqual(original),
                "ReminderProfile_DependencyLeaseBlocksWritesAndAllowsReads");
        }
        using (var writer = new FileStream(dependency, FileMode.Open, FileAccess.Write, FileShare.Read))
            check(writer.CanWrite, "ReminderProfile_DependencyLeaseReleasesAfterCommitScope");
        bool mismatchDenied = false;
        try { using var lease = new Map3dsDependencyLease(Proposal((dependency, fingerprint), (another, new byte[32]))); }
        catch (IOException) { mismatchDenied = true; }
        using (var writer = new FileStream(dependency, FileMode.Open, FileAccess.Write, FileShare.Read))
            check(mismatchDenied && writer.CanWrite, "ReminderProfile_FailedLeaseReleasesEarlierResources");
        var writing = new EditorProposal(Guid.NewGuid(), "synthetic replacement", 0,
            [new FileChange(dependency, original, [2, 3])], [], new Dictionary<string, string>(), [(dependency, fingerprint)]);
        using (var lease = new Map3dsDependencyLease(writing))
        using (var writer = new FileStream(dependency, FileMode.Open, FileAccess.Write, FileShare.Read))
            check(writer.CanWrite, "ReminderProfile_ChangedFileUsesTransactionGuardWithoutReadLock");
    }
}
