using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Nds.Project;
using ThreeDS.Progressive.Services.ProjectDetection;

/// <summary>Persistent DS project, PR 1: the container, its integrity, the store contract (in memory), the operation transaction and the ROM association. Synthetic.</summary>
internal static class NdsProjectFormatChecks
{
    private static readonly DateTime T0 = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
    private static NdsProjectRom Rom(string sha = "AA", string code = "CPUE", int rev = 1) => new(sha.PadRight(64, '0'), "BB".PadRight(40, '0'), code, rev, "Pokémon Platino (USA, rev. 1)", 134217728, false, @"D:\Roms\Platino.nds");

    private static NdsProjectDocument Doc(int ops = 3, int files = 2, int fileSize = 4096, long generation = 1, int seed = 1, bool events = true, bool arm9 = true)
    {
        var rng = new Random(seed); byte[] Bytes(int n, bool noise) { var b = new byte[n]; if (noise) rng.NextBytes(b); else for (int i = 0; i < n; i++) b[i] = (byte)(i / 64); return b; }
        var operations = Enumerable.Range(0, ops).Select(i => new NdsProjectOperationRecord("Operación " + i, i % 2 == 0 ? (uint)(1000 + i) : null,
            new JsonObject { ["Kind"] = "Edit", ["Section"] = "Trainers", ["Edits"] = new JsonObject { ["1:Level"] = 10 + i } }, [new Gen5Change("Entrenador " + i, "Nivel", i, i + 1)])).ToList();
        var state = new NdsProjectState { Arm9 = arm9 ? Bytes(2048, true) : null, EventData = events ? Bytes(3000, false) : null, EventScripts = events ? Bytes(1500, false) : null };
        for (int i = 0; i < files; i++) state.Files["a/0/" + i + "/1"] = Bytes(fileSize, i % 2 == 1);
        state.Overlays[7] = Bytes(512, true);
        var meta = new NdsProjectMeta
        {
            Writer = "test 1.0", ProjectId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), CreatedUtc = T0, SavedUtc = T0.AddMinutes(generation), Generation = generation, Rom = Rom(),
            Operations = operations, RecompressOverlays = [7, 9], Settings = new JsonObject { ["AllowRomhack"] = false, ["Nota"] = "ñandú" },
            Events = events ? new NdsProjectEvents([NdsEventOperation.OfMove(new NdsNpcEdit(0, 1, new(2, 3, 0), new(4, 5, 1)))], ["Guardar mapa 0 NPC 1: X 2 → 4"]) : null,
        };
        return new(meta, state);
    }

    private static bool SameDoc(NdsProjectDocument a, NdsProjectDocument b) =>
        a.State.SameAs(b.State) && a.Generation == b.Generation && a.Meta.Rom == b.Meta.Rom && a.Meta.ProjectId == b.Meta.ProjectId && a.Meta.SavedUtc == b.Meta.SavedUtc &&
        a.Meta.Operations.Count == b.Meta.Operations.Count && a.Meta.Operations.Zip(b.Meta.Operations).All(p => p.First.Action == p.Second.Action && p.First.Seed == p.Second.Seed && p.First.Recipe.ToJsonString() == p.Second.Recipe.ToJsonString() && p.First.Changes.SequenceEqual(p.Second.Changes)) &&
        a.Meta.RecompressOverlays.SequenceEqual(b.Meta.RecompressOverlays) && a.Meta.Settings?.ToJsonString() == b.Meta.Settings?.ToJsonString() &&
        JsonSerializer.Serialize(a.Meta.Events) == JsonSerializer.Serialize(b.Meta.Events);

    /// <summary>Replaces the footer digest after a deliberate patch, so that only the structural checks (not the whole-file digest) are exercised.</summary>
    private static byte[] Resign(byte[] data) { var copy = (byte[])data.Clone(); int end = copy.Length - NdsProjectCodec.FooterSize; SHA256.HashData(copy.AsSpan(0, end), copy.AsSpan(end + 8)); return copy; }
    private static void Header(byte[] data) { BitConverter.TryWriteBytes(data.AsSpan(56), NdsProjectCodec.Crc32(data.AsSpan(0, 56))); }

    internal static void Run(Action<bool, string> check)
    {
        Container(check); Hostile(check); Events(check); Store(check); Transactor(check); Identity(check); Scale(check);
    }

    // ------------------------------------------------------------------------------------------------------------------------------ container

    private static void Container(Action<bool, string> check)
    {
        var doc = Doc(); byte[] bytes = NdsProjectCodec.Serialize(doc); var read = NdsProjectCodec.Read(bytes);
        check(read.Ok && SameDoc(doc, read.Document!) && read.Document!.Meta.Blobs!.Count == 6, "NdsProject_RoundTripKeepsEveryField");
        check(read.Header is { ContainerVersion: 1, Generation: 1, OperationCount: 4 } && read.Header.SavedUtc == doc.Meta.SavedUtc && read.Header.TotalLength == bytes.Length, "NdsProject_HeaderCarriesGenerationOperationsAndTime");
        check(NdsProjectCodec.Serialize(doc).AsSpan().SequenceEqual(bytes) && NdsProjectCodec.Serialize(read.Document!).AsSpan().SequenceEqual(bytes), "NdsProject_SerializationIsDeterministicAndStableAcrossReload");
        var empty = new NdsProjectDocument(doc.Meta with { Operations = [], Events = null, RecompressOverlays = [], Settings = null }, new NdsProjectState());
        var emptyRead = NdsProjectCodec.Read(NdsProjectCodec.Serialize(empty));
        check(emptyRead.Ok && emptyRead.Document!.State.Files.Count == 0 && emptyRead.Document.OperationCount == 0 && emptyRead.Document.State.Arm9 is null, "NdsProject_EmptyProjectRoundTrips");
        var blobs = read.Document!.Meta.Blobs!;
        check(blobs.Any(b => b.Codec == "br") && blobs.Any(b => b.Codec == "raw") && blobs.All(b => b.Sha256.Length == 64), "NdsProject_CompressibleTablesAreCompressedAndNoiseIsStoredRaw");
        // Forward compatibility: fields written by a newer build of the same major version survive a read and write.
        var future = JsonNode.Parse("{\"x\":[1,2,{\"y\":\"z\"}],\"n\":5}")!;
        var extended = new NdsProjectDocument(doc.Meta with { Extra = new() { ["Futuro"] = JsonSerializer.SerializeToElement(future) } }, doc.State);
        var again = NdsProjectCodec.Read(NdsProjectCodec.Serialize(NdsProjectCodec.Read(NdsProjectCodec.Serialize(extended)).Document!)).Document!;
        check(again.Meta.Extra is { Count: 1 } && again.Meta.Extra["Futuro"].GetRawText() == future.ToJsonString(), "NdsProject_UnknownFieldsOfANewerMinorVersionAreKept");
        var ninth = NdsProjectCodec.Read(NdsProjectCodec.Serialize(Doc(files: 40, fileSize: 300)));
        check(ninth.Ok && ninth.Document!.State.Files.Count == 40, "NdsProject_ManyTablesRoundTrip");
        var op = new NdsOperation("Manual", 5, [new Gen5Change("x", "y", 1, 2)], new JsonObject { ["Kind"] = "Edit" });
        var rec = NdsProjectOperationRecord.Of(op); var back = rec.ToOperation();
        check(back.Action == op.Action && back.Seed == 5 && back.Changes.SequenceEqual(op.Changes) && back.Recipe.ToJsonString() == op.Recipe.ToJsonString() && !ReferenceEquals(back.Recipe, op.Recipe), "NdsProject_OperationRecordConvertsBothWaysWithoutAliasing");
        var newer = NdsProjectCodec.Read(NdsProjectCodec.Serialize(new NdsProjectDocument(doc.Meta with { MinReaderVersion = 2 }, doc.State)));
        check(newer.Status == NdsProjectReadStatus.NewerFormat && newer.Header is { Generation: 1 }, "NdsProject_NewerFormatIsRefusedNotGuessed");
    }

    // ----------------------------------------------------------------------------------------------------------------- truncation and corruption

    private static void Hostile(Action<bool, string> check)
    {
        var doc = Doc(ops: 2, files: 2, fileSize: 600, arm9: false); byte[] bytes = NdsProjectCodec.Serialize(doc); int bad = 0, thrown = 0;
        for (int length = 0; length < bytes.Length; length++)
        {
            try { var r = NdsProjectCodec.Read(bytes.AsSpan(0, length)); if (r.Ok || r.Status is not (NdsProjectReadStatus.Truncated or NdsProjectReadStatus.NotAProject)) bad++; }
            catch (Exception) { thrown++; }
        }
        check(bad == 0 && thrown == 0, $"NdsProject_EveryTruncatedPrefixIsRejectedAsTruncated_{bytes.Length}Lengths");
        bad = thrown = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            var copy = (byte[])bytes.Clone(); copy[i] ^= 0xFF;
            try { if (NdsProjectCodec.Read(copy).Ok) bad++; } catch (Exception) { thrown++; }
        }
        check(bad == 0 && thrown == 0, $"NdsProject_EveryFlippedByteIsDetected_{bytes.Length}Bytes");
        var rng = new Random(11); bad = thrown = 0; var large = NdsProjectCodec.Serialize(Doc(files: 6, fileSize: 20000));
        for (int i = 0; i < 1500; i++)
        {
            var copy = (byte[])large.Clone(); for (int k = rng.Next(1, 4); k > 0; k--) copy[rng.Next(copy.Length)] ^= (byte)(1 << rng.Next(8));
            try { if (NdsProjectCodec.Read(copy).Ok) bad++; } catch (Exception) { thrown++; }
        }
        check(bad == 0 && thrown == 0, "NdsProject_RandomBitFlipsAreNeverAccepted_1500Cases");
        thrown = 0; bad = 0;
        for (int i = 0; i < 300; i++) { var junk = new byte[rng.Next(0, 4000)]; rng.NextBytes(junk); if (i % 3 == 0 && junk.Length > 8) NdsProjectCodec.Magic.CopyTo(junk, 0); try { if (NdsProjectCodec.Read(junk).Ok) bad++; } catch (Exception) { thrown++; } }
        check(bad == 0 && thrown == 0, "NdsProject_RandomGarbageNeverThrowsAndIsNeverAccepted");
        check(NdsProjectCodec.Read(new byte[200]).Status == NdsProjectReadStatus.NotAProject && NdsProjectCodec.Read([]).Status == NdsProjectReadStatus.Truncated, "NdsProject_ZerosAndEmptyAreClassified");
        check(NdsProjectCodec.Read([.. bytes, 0]).Status == NdsProjectReadStatus.Corrupt, "NdsProject_TrailingBytesAreCorrupt");
        // A damaged body keeps a trustworthy header: recovery can say how many operations may be lost.
        var damaged = (byte[])bytes.Clone(); damaged[NdsProjectCodec.HeaderSize + 40] ^= 1; var dr = NdsProjectCodec.Read(damaged);
        check(!dr.Ok && dr.Header is { Generation: 1, OperationCount: 3 }, "NdsProject_DamagedBodyKeepsTheCheckedHeader");
        var damagedHeader = (byte[])bytes.Clone(); damagedHeader[20] ^= 1;
        check(NdsProjectCodec.Read(damagedHeader) is { Ok: false, Header: null, Status: NdsProjectReadStatus.Corrupt }, "NdsProject_DamagedHeaderYieldsNoHeader");
        // Structural limits are enforced even when the digests are valid (re-signed patches).
        var many = (byte[])bytes.Clone(); BitConverter.TryWriteBytes(many.AsSpan(32), (uint)NdsProjectCodec.MaxBlobs + 1); Header(many);
        check(NdsProjectCodec.Read(Resign(many)).Status == NdsProjectReadStatus.Invalid, "NdsProject_TooManyDeclaredTablesIsInvalid");
        var hugeManifest = (byte[])bytes.Clone(); BitConverter.TryWriteBytes(hugeManifest.AsSpan(28), (uint)NdsProjectCodec.MaxManifestBytes + 1); Header(hugeManifest);
        check(NdsProjectCodec.Read(Resign(hugeManifest)).Status == NdsProjectReadStatus.Invalid, "NdsProject_OversizedManifestIsInvalid");
        var badTotal = (byte[])bytes.Clone(); BitConverter.TryWriteBytes(badTotal.AsSpan(48), (ulong)long.MaxValue); Header(badTotal);
        check(NdsProjectCodec.Read(Resign(badTotal)).Status == NdsProjectReadStatus.Invalid, "NdsProject_AbsurdDeclaredLengthIsInvalid");
        var wrongGeneration = (byte[])bytes.Clone(); BitConverter.TryWriteBytes(wrongGeneration.AsSpan(16), 99UL); Header(wrongGeneration);
        check(NdsProjectCodec.Read(Resign(wrongGeneration)).Status == NdsProjectReadStatus.Invalid, "NdsProject_HeaderAndManifestMustAgree");
        // A stored table that does not decompress to its declared length (decompression-bomb guard) is rejected.
        byte[] bomb = NdsProjectCodec.Serialize(new NdsProjectDocument(doc.Meta with { Events = null }, new NdsProjectState { Files = { ["a/zeros"] = new byte[1048576] } }));
        string manifestText = System.Text.Encoding.UTF8.GetString(bomb, NdsProjectCodec.HeaderSize, BitConverter.ToInt32(bomb, 28)); int at = manifestText.IndexOf("\"RawLength\":1048576", StringComparison.Ordinal);
        var shrunk = (byte[])bomb.Clone(); shrunk[NdsProjectCodec.HeaderSize + at + "\"RawLength\":104857".Length] = (byte)'5';
        check(at > 0 && NdsProjectCodec.Read(Resign(shrunk)) is { Ok: false, Status: NdsProjectReadStatus.Corrupt }, "NdsProject_TableLongerThanDeclaredIsRejectedWithoutExpandingIt");
        var wrongSha = (byte[])bomb.Clone(); int shaAt = manifestText.LastIndexOf("\"Sha256\":\"", StringComparison.Ordinal) + 10; wrongSha[NdsProjectCodec.HeaderSize + shaAt] = (byte)(wrongSha[NdsProjectCodec.HeaderSize + shaAt] == '0' ? '1' : '0');
        check(NdsProjectCodec.Read(Resign(wrongSha)) is { Ok: false, Status: NdsProjectReadStatus.Corrupt }, "NdsProject_TableWithWrongSha256IsCorruptEvenWithAValidFileDigest");
    }

    // ----------------------------------------------------------------------------------------------------------------- B2 and checkpoint compatibility

    private static void Events(Action<bool, string> check)
    {
        var move = new NdsNpcEdit(3, 1, new(2, 3, 0), new(4, 5, 1)); var add = new NdsNpcAddition(3, 6, new string('A', 64)); var scripted = new NdsNpcAddition(3, 7, new string('B', 64), new(NdsScriptBinding.Append, 2, 5, "0200AB"));
        var v1 = new NdsEventDocument(NdsEventSession.Format, 1, "CPUE", 1, @"D:\P.nds", new string('C', 64), [move, move with { Index = 2 }], ["a", "b"]);
        var v2 = new NdsEventDocument(NdsEventSession.Format, 2, "CPUE", 1, @"D:\P.nds", new string('C', 64), [], ["a"], [NdsEventOperation.OfMove(move), NdsEventOperation.OfAdd(add)]);
        var v3 = new NdsEventDocument(NdsEventSession.Format, 3, "CPUE", 1, @"D:\P.nds", new string('C', 64), [], ["a"], [NdsEventOperation.OfAdd(scripted), NdsEventOperation.OfRemove(new NdsNpcRemoval(3, 7, new string('B', 64)))]);
        foreach (var (version, document) in new[] { (1, v1), (2, v2), (3, v3) })
        {
            var events = NdsProjectEvents.FromCheckpoint(document); var doc = Doc(events: false); doc = new(doc.Meta with { Events = events }, doc.State);
            var stored = NdsProjectCodec.Read(NdsProjectCodec.Serialize(doc)).Document!.Meta.Events!;
            var exported = stored.ToCheckpoint(document.SourcePath, document.SourceSha256);
            check(events.ImportedFrom == version && events.CheckpointVersion == version && JsonSerializer.Serialize(exported) == JsonSerializer.Serialize(document), $"NdsProject_CheckpointV{version}_ImportsStoresAndExportsIdentically");
        }
        check(throws(() => NdsProjectEvents.FromCheckpoint(v1 with { Version = 4 })) && throws(() => NdsProjectEvents.FromCheckpoint(v2 with { Operations = null })), "NdsProject_CheckpointOutsideV1ToV3OrIncompleteIsRefused");
        static bool throws(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------ store

    private static NdsProjectDocument Next(NdsProjectDocument current, int tweak) =>
        new(current.Meta with { Generation = current.Generation + 1, SavedUtc = current.Meta.SavedUtc.AddMinutes(1), Operations = [.. current.Meta.Operations, new NdsProjectOperationRecord("op " + tweak, null, new JsonObject { ["Kind"] = "Edit" }, [new("l", "f", 0, tweak)])] },
            new NdsProjectState { Files = new(current.State.Files) { ["a/0/0/1"] = BitConverter.GetBytes(tweak) } });
    private static NdsProjectStoreError? Fails(Action action) { try { action(); return null; } catch (NdsProjectStoreException ex) { return ex.Kind; } }

    private static void Store(Action<bool, string> check)
    {
        var medium = new NdsProjectMedium(); var first = Doc(generation: 1);
        using (var store = new InMemoryNdsProjectStore(medium))
        {
            check(store.Load().Status == NdsProjectLoadStatus.Empty, "NdsProjectStore_NewProjectIsEmpty");
            check(store.Commit(first, 0) == 1 && store.Load() is { Status: NdsProjectLoadStatus.Loaded } l && SameDoc(first, l.Document!), "NdsProjectStore_CommitThenLoadRoundTrips");
            check(Fails(() => store.Commit(first, 0)) == NdsProjectStoreError.StaleGeneration && Fails(() => store.Commit(first, 1)) == NdsProjectStoreError.WriteFailed, "NdsProjectStore_CommitIsCompareAndSwapOnTheGeneration");
            var doc = first; for (int i = 1; i <= 6; i++) { doc = Next(doc, i); store.Commit(doc, doc.Generation - 1); }
            check(medium.Backups.Count == NdsProjectPaths.MaxBackups && NdsProjectCodec.Read(medium.Backups[0]).Document!.Generation == 6 && NdsProjectCodec.Read(medium.Backups[2]).Document!.Generation == 4 && store.Load().Document!.Generation == 7, "NdsProjectStore_KeepsOnlyThreeNewestBackups");
            // One-shot faults.
            var gen = store.Load().Document!;
            store.Fault = NdsProjectFault.FailBeforeWrite; var e1 = Fails(() => store.Commit(Next(gen, 100), gen.Generation));
            store.Fault = NdsProjectFault.CorruptWrite; var e2 = Fails(() => store.Commit(Next(gen, 101), gen.Generation));
            check(e1 == NdsProjectStoreError.WriteFailed && e2 == NdsProjectStoreError.VerificationFailed && SameDoc(gen, store.Load().Document!) && medium.Backups.Count == 3, "NdsProjectStore_WriteAndVerificationFailuresLeaveTheDurableProjectUntouched");
            store.Fault = NdsProjectFault.FailAfterPublish; var e3 = Fails(() => store.Commit(Next(gen, 102), gen.Generation));
            check(e3 == NdsProjectStoreError.OutcomeUnknown && store.Load().Document!.Generation == gen.Generation + 1, "NdsProjectStore_UnknownOutcomeMayHaveBeenDurable");
            // External replacement: the next commit from the old generation is refused, never overwritten.
            var durable = store.Load().Document!; medium.Main = NdsProjectCodec.Serialize(Doc(generation: durable.Generation + 5, seed: 9));
            check(Fails(() => store.Commit(Next(durable, 103), durable.Generation)) == NdsProjectStoreError.StaleGeneration, "NdsProjectStore_ExternalModificationIsDetectedAndNotOverwritten");
        }
        // Two instances.
        var shared = new NdsProjectMedium(); var a = new InMemoryNdsProjectStore(shared); a.Commit(first, 0);
        check(Fails(() => new InMemoryNdsProjectStore(shared).Dispose()) == NdsProjectStoreError.AlreadyInUse, "NdsProjectStore_SecondInstanceIsRefusedWhileTheFirstHoldsTheProject");
        a.Dispose(); using (var b = new InMemoryNdsProjectStore(shared)) check(b.Load().Document!.Generation == 1 && Fails(() => a.Commit(Next(first, 2), 1)) == NdsProjectStoreError.Disposed, "NdsProjectStore_ProjectCanBeReopenedAfterRelease");
        Recovery(check);
    }

    private static NdsProjectMedium Populated(int commits, out NdsProjectDocument last)
    {
        var medium = new NdsProjectMedium(); using var store = new InMemoryNdsProjectStore(medium); last = Doc(generation: 1); store.Commit(last, 0);
        for (int i = 2; i <= commits; i++) { last = Next(last, i); store.Commit(last, last.Generation - 1); }
        return medium;
    }

    private static void Recovery(Action<bool, string> check)
    {
        var medium = Populated(5, out var last); byte[] good = medium.Main!;
        var damagedBytes = (byte[])good.Clone(); damagedBytes[NdsProjectCodec.HeaderSize + 30] ^= 0x7F; medium.Main = damagedBytes;
        using (var store = new InMemoryNdsProjectStore(medium))
        {
            var load = store.Load(); var plan = load.Plan!;
            check(load.Status == NdsProjectLoadStatus.Damaged && plan.Candidates.Select(c => c.Generation).SequenceEqual([4L, 3, 2]) && plan.Recommended!.Generation == 4, "NdsProjectRecovery_DamagedMainOffersTheValidBackupsNewestFirst");
            check(plan.LostOperations(plan.Recommended!) == 1 && plan.LostOperations(plan.Candidates[2]) == 3, "NdsProjectRecovery_SaysHowManyOperationsMayBeLost");
            check(Fails(() => store.Commit(Next(last, 9), last.Generation)) == NdsProjectStoreError.MainDamaged && medium.Main!.AsSpan().SequenceEqual(damagedBytes) && medium.Preserved == 0, "NdsProjectRecovery_NothingIsReplacedSilently");
            long gen = store.Recover(plan.Recommended!);
            check(gen == 4 && medium.Preserved == 1 && medium.Damaged[0].Bytes.AsSpan().SequenceEqual(damagedBytes) && store.Load().Document!.Generation == 4, "NdsProjectRecovery_InstallsTheChosenBackupAndPreservesTheDamagedFile");
            var recovered = store.Load().Document!; check(store.Commit(Next(recovered, 50), 4) == 5, "NdsProjectRecovery_CommitsContinueAfterRecovering");
            check(Fails(() => store.Recover(plan.Candidates[1])) == NdsProjectStoreError.WriteFailed, "NdsProjectRecovery_ARecoveryNeverReplacesAValidMainFile");
        }
        // Truncated main (a torn write), a damaged header (operations unknown), a missing main, and a damaged backup are skipped.
        var torn = Populated(4, out _); torn.Main = torn.Main![..(torn.Main.Length / 2)];
        using (var s = new InMemoryNdsProjectStore(torn)) check(s.Load() is { Status: NdsProjectLoadStatus.Damaged, Plan.DamagedHeader: not null } && s.Load().Plan!.DamageMessage.Contains("truncado"), "NdsProjectRecovery_TruncatedMainIsDamagedWithItsHeaderStillKnown");
        var noHeader = Populated(4, out _); noHeader.Main![20] ^= 1;
        using (var s = new InMemoryNdsProjectStore(noHeader)) { var p = s.Load().Plan!; check(p.DamagedHeader is null && p.LostOperations(p.Recommended!) is null, "NdsProjectRecovery_UnknownHeaderMeansUnknownLoss"); }
        var missing = Populated(4, out _); missing.Main = null;
        using (var s = new InMemoryNdsProjectStore(missing)) check(s.Load() is { Status: NdsProjectLoadStatus.Damaged } && s.Load().Message.Contains("Falta") && Fails(() => s.Commit(Next(Doc(generation: 3), 7), 3)) == NdsProjectStoreError.MainDamaged, "NdsProjectRecovery_MissingMainFileIsRecoverableFromBackups");
        var partial = Populated(5, out _); partial.Main![30] ^= 1; partial.Backups[0][partial.Backups[0].Length / 2] ^= 1;
        using (var s = new InMemoryNdsProjectStore(partial)) check(s.Load().Plan!.Candidates.Select(c => c.Generation).SequenceEqual([3L, 2]), "NdsProjectRecovery_DamagedBackupsAreNotOffered");
        var allBad = Populated(3, out _); allBad.Main![30] ^= 1; foreach (var bkp in allBad.Backups) bkp[40] ^= 1;
        using (var s = new InMemoryNdsProjectStore(allBad)) check(s.Load() is { Status: NdsProjectLoadStatus.Damaged, Plan.Recommended: null }, "NdsProjectRecovery_NoValidBackupMeansNoOfferAndNothingIsChanged");
        var future = new NdsProjectMedium { Main = NdsProjectCodec.Serialize(new NdsProjectDocument(Doc().Meta with { MinReaderVersion = 2 }, Doc().State)) }; byte[] before = future.Main.ToArray();
        using (var s = new InMemoryNdsProjectStore(future)) check(s.Load().Status == NdsProjectLoadStatus.NewerFormat && Fails(() => s.Commit(Next(Doc(), 1), 1)) is not null && future.Main.AsSpan().SequenceEqual(before), "NdsProjectStore_NewerFormatIsNeverModified");
    }

    // ------------------------------------------------------------------------------------------------------------------------------- transaction

    private static void Transactor(Action<bool, string> check)
    {
        var medium = new NdsProjectMedium(); var store = new InMemoryNdsProjectStore(medium); var clock = T0; var seed = Doc(generation: 0); var published = new List<long>(); var log = new List<string>();
        var tx = new NdsProjectTransactor(store, seed, () => clock = clock.AddSeconds(1));
        NdsProjectDocument Prepare(NdsProjectDocument d) { log.Add("prepare"); return Next(d, (int)d.Generation + 1) with { }; }
        void Publish(NdsProjectDocument d) { log.Add("publish"); published.Add(d.Generation); }
        var one = tx.Run(Prepare, Publish);
        check(one.Generation == 1 && one.Meta.SavedUtc == T0.AddSeconds(1) && string.Join(",", log) == "prepare,publish" && store.Load().Document!.Generation == 1 && tx.Current.Generation == 1, "NdsProjectTransactor_PreparesThenPersistsThenPublishes");
        log.Clear(); int before = store.Commits;
        try { tx.Run(_ => throw new InvalidOperationException("no válido"), Publish); } catch (InvalidOperationException) { }
        check(store.Commits == before && published.Count == 1 && tx.Current.Generation == 1 && !tx.Faulted, "NdsProjectTransactor_APrepareFailureChangesNothing");
        foreach (var (fault, kind) in new[] { (NdsProjectFault.FailBeforeWrite, NdsProjectStoreError.WriteFailed), (NdsProjectFault.CorruptWrite, NdsProjectStoreError.VerificationFailed) })
        {
            store.Fault = fault; published.Clear();
            var error = Fails(() => tx.Run(Prepare, Publish));
            check(error == kind && published.Count == 0 && tx.Current.Generation == 1 && store.Load().Document!.Generation == 1 && !tx.Faulted, $"NdsProjectTransactor_{fault}_IsNotPublishedNorReportedSaved");
        }
        check(tx.Run(Prepare, Publish).Generation == 2, "NdsProjectTransactor_ARetryAfterAFailureSucceeds");
        medium.Main = NdsProjectCodec.Serialize(Doc(generation: 9, seed: 4));
        check(Fails(() => tx.Run(Prepare, Publish)) == NdsProjectStoreError.StaleGeneration && !tx.Faulted && tx.Current.Generation == 2, "NdsProjectTransactor_ExternalModificationRefusesTheOperationWithoutFaulting");
        // Unknown outcome that DID become durable: blocked, then reconciled to the persisted state.
        var m2 = new NdsProjectMedium(); var s2 = new InMemoryNdsProjectStore(m2); var t2 = new NdsProjectTransactor(s2, Doc(generation: 0)); t2.Run(Prepare, Publish); published.Clear();
        s2.Fault = NdsProjectFault.FailAfterPublish;
        try { t2.Run(Prepare, Publish); } catch (NdsProjectFaultedException) { }
        check(t2.Faulted && published.Count == 0 && t2.Current.Generation == 1 && Faults(() => t2.Run(Prepare, Publish)), "NdsProjectTransactor_UnknownOutcomeBlocksNewOperations");
        NdsProjectDocument? adopted = null; t2.Reconcile(d => adopted = d);
        check(!t2.Faulted && adopted!.Generation == 2 && t2.Current.Generation == 2 && t2.Run(Prepare, Publish).Generation == 3, "NdsProjectTransactor_ReconcileAdoptsWhatWasPersisted");
        // Unknown outcome that did NOT become durable.
        var m3 = new NdsProjectMedium(); var inner = new InMemoryNdsProjectStore(m3); var lying = new FlakyStore(inner); var t3 = new NdsProjectTransactor(lying, Doc(generation: 0)); t3.Run(Prepare, Publish);
        lying.UnknownNext = true; try { t3.Run(Prepare, Publish); } catch (NdsProjectFaultedException) { }
        adopted = null; t3.Reconcile(d => adopted = d);
        check(adopted!.Generation == 1 && t3.Current.Generation == 1 && !t3.Faulted && t3.Run(Prepare, Publish).Generation == 2, "NdsProjectTransactor_ReconcileKeepsThePreviousStateWhenNothingWasPersisted");
        // Persistence done, installation in memory failed: blocked until the persisted state is installed.
        var m4 = new NdsProjectMedium(); var s4 = new InMemoryNdsProjectStore(m4); var t4 = new NdsProjectTransactor(s4, Doc(generation: 0)); t4.Run(Prepare, Publish);
        try { t4.Run(Prepare, _ => throw new InvalidOperationException("la vista falló")); } catch (NdsProjectFaultedException) { }
        check(t4.Faulted && s4.Load().Document!.Generation == 2 && t4.Current.Generation == 1 && Faults(() => t4.Run(Prepare, Publish)), "NdsProjectTransactor_PersistedButNotInstalledBlocksFurtherOperations");
        try { t4.Reconcile(_ => throw new InvalidOperationException("sigue fallando")); } catch (InvalidOperationException) { }
        check(t4.Faulted, "NdsProjectTransactor_FailedReconcileKeepsTheBlock");
        adopted = null; t4.Reconcile(d => adopted = d);
        check(!t4.Faulted && adopted!.Generation == 2 && t4.Current.State.SameAs(s4.Load().Document!.State) && t4.Run(Prepare, Publish).Generation == 3, "NdsProjectTransactor_ReconcileInstallsThePersistedStateAndResumes");
        var m5 = new NdsProjectMedium(); var s5 = new InMemoryNdsProjectStore(m5); var t5 = new NdsProjectTransactor(s5, Doc(generation: 0)); t5.Run(Prepare, Publish); m5.Main![30] ^= 1;
        s5.Fault = NdsProjectFault.FailAfterPublish; try { t5.Run(Prepare, Publish); } catch (NdsProjectStoreException) { } catch (NdsProjectFaultedException) { }
        check(Fails(() => s5.Commit(Next(t5.Current, 1), 1)) == NdsProjectStoreError.MainDamaged, "NdsProjectTransactor_CannotCommitOverADamagedMain");
        Days(check);
        static bool Faults(Action a) { try { a(); return false; } catch (NdsProjectFaultedException) { return true; } }
    }

    private sealed class FlakyStore(INdsProjectStore inner) : INdsProjectStore
    {
        internal bool UnknownNext;
        public NdsProjectLoadResult Load() => inner.Load();
        public long Commit(NdsProjectDocument next, long expected) { if (UnknownNext) { UnknownNext = false; throw new NdsProjectStoreException(NdsProjectStoreError.OutcomeUnknown, "sin respuesta"); } return inner.Commit(next, expected); }
        public long Recover(NdsProjectRecoveryCandidate candidate) => inner.Recover(candidate);
        public void Dispose() => inner.Dispose();
    }

    /// <summary>Many operations over many "sessions": close and reopen repeatedly, fail some writes, and compare with a model that only counts the confirmed ones.</summary>
    private static void Days(Action<bool, string> check)
    {
        var medium = new NdsProjectMedium(); var rng = new Random(2026); var model = new Dictionary<string, byte[]>(); int confirmed = 0, refused = 0, sessions = 0;
        NdsProjectDocument start = Doc(generation: 0, ops: 0, files: 0, events: false, arm9: false);
        var store = new InMemoryNdsProjectStore(medium); var tx = new NdsProjectTransactor(store, start); sessions++;
        for (int i = 1; i <= 400; i++)
        {
            string key = "a/0/" + rng.Next(6) + "/1"; var data = new byte[rng.Next(1, 3000)]; rng.NextBytes(data);
            if (rng.Next(12) == 0) store.Fault = rng.Next(2) == 0 ? NdsProjectFault.FailBeforeWrite : NdsProjectFault.CorruptWrite;
            try
            {
                tx.Run(d => new(d.Meta with { Operations = [.. d.Meta.Operations, new NdsProjectOperationRecord("Edición " + i, null, new JsonObject { ["Kind"] = "Edit", ["I"] = i }, [new("k", "f", 0, i)])] },
                    new NdsProjectState { Files = new(d.State.Files) { [key] = data } }), _ => { });
                model[key] = data; confirmed++;
            }
            catch (NdsProjectStoreException) { refused++; }
            if (i % 37 == 0) { store.Dispose(); store = new InMemoryNdsProjectStore(medium); var load = store.Load(); tx = new NdsProjectTransactor(store, load.Document!); sessions++; }
        }
        var final = new InMemoryNdsProjectStore(medium.Lease is null ? medium : Release(medium, store)).Load().Document!;
        check(final.Meta.Operations.Count == confirmed && final.Generation == confirmed && refused > 5 && final.State.Files.Count == model.Count && model.All(p => final.State.Files[p.Key].AsSpan().SequenceEqual(p.Value)) && medium.Backups.Count == 3 && sessions > 8,
            $"NdsProjectTransactor_400OperationsAcrossReopenedSessions_OnlyConfirmedOnesRemain_{confirmed}Confirmed_{refused}Refused");
        static NdsProjectMedium Release(NdsProjectMedium m, InMemoryNdsProjectStore s) { s.Dispose(); return m; }
    }

    // ------------------------------------------------------------------------------------------------------------------------------- identity

    private static void Identity(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-project-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string rom = ProjectDetectorChecks.Write(temp, "Platino.nds", ProjectDetectorChecks.Rom("CPUE", 1)); var identity = NdsProjectRomIdentity.Compute(rom, false);
            check(identity.GameCode == "CPUE" && identity.Revision == 1 && identity.Length == new FileInfo(rom).Length && identity.Sha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rom))) && identity.Sha1 == Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(rom))) && identity.ProfileKey == "CPUE/1", "NdsProjectIdentity_RecordsFullHashesAndProfile");
            var detected = ProjectDetector.Detect(rom);
            check(detected.IsCandidate && detected.Game == identity.Game, "NdsProjectIdentity_AgreesWithTheUniversalDetector");
            string moved = Path.Combine(Directory.CreateDirectory(Path.Combine(temp, "otra carpeta")).FullName, "renombrada.nds"); File.Copy(rom, moved);
            check(NdsProjectAssociation.Evaluate(identity, NdsProjectRomIdentity.Compute(moved, false)).IsMatch, "NdsProjectIdentity_ARelocatedOrRenamedCopyOfTheSameRomMatches");
            byte[] altered = File.ReadAllBytes(rom); altered[^5] ^= 1; string patched = ProjectDetectorChecks.Write(temp, "Patched.nds", altered);
            check(NdsProjectAssociation.Evaluate(identity, NdsProjectRomIdentity.Compute(patched, false)).Status == NdsProjectAssociationStatus.DifferentRom, "NdsProjectIdentity_OneChangedByteIsADifferentRom");
            string heart = ProjectDetectorChecks.Write(temp, "HG.nds", ProjectDetectorChecks.Rom("IPKE", 0));
            check(NdsProjectAssociation.Evaluate(identity, NdsProjectRomIdentity.Compute(heart, false)).Status == NdsProjectAssociationStatus.DifferentGame && NdsProjectAssociation.Evaluate(identity, identity with { Revision = 0 }).Status == NdsProjectAssociationStatus.DifferentGame, "NdsProjectIdentity_OtherGameOrRevisionIsNeverAssociated");
            check(NdsProjectAssociation.Evaluate(identity with { Length = 1 }, identity).Status == NdsProjectAssociationStatus.DifferentRom && NdsProjectAssociation.Evaluate(identity with { Sha256 = identity.Sha256.ToLowerInvariant() }, identity).IsMatch, "NdsProjectIdentity_LengthMustMatchAndHexCaseDoesNot");
            string unsupported = ProjectDetectorChecks.Write(temp, "Raro.nds", ProjectDetectorChecks.Rom("ADAE", 4));
            check(throws(() => NdsProjectRomIdentity.Compute(unsupported, false)) && throws(() => NdsProjectRomIdentity.Compute(ProjectDetectorChecks.Write(temp, "x.nds", [1, 2, 3]), false)), "NdsProjectIdentity_UnsupportedOrUnreadableRomIsRefused");
            string projectPath = NdsProjectPaths.ProjectOf(@"D:\Roms\Platino.nds");
            check(projectPath == @"D:\Roms\Platino.nds.drproj" && NdsProjectPaths.BackupOf(projectPath, 3).EndsWith(".drproj.bak3") && NdsProjectPaths.LockOf(projectPath).EndsWith(".drproj.lock") && NdsProjectPaths.DamagedOf(projectPath, T0).EndsWith(".drproj.damaged-20261112T080000000Z") && throws2(() => NdsProjectPaths.BackupOf(projectPath, 4)), "NdsProjectPaths_NamesFollowTheApprovedConvention");
            // The checkpoint's source hash is the same SHA-256 the project records, so a checkpoint imported into a project is bound to the same ROM.
            check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rom))) == identity.Sha256, "NdsProjectIdentity_UsesTheSameSha256AsTheEventCheckpoints");
        }
        finally { Directory.Delete(temp, true); }
        static bool throws(Action a) { try { a(); return false; } catch (Exception ex) when (ex is InvalidDataException or IOException) { return true; } }
        static bool throws2(Action a) { try { a(); return false; } catch (ArgumentOutOfRangeException) { return true; } }
    }

    // ----------------------------------------------------------------------------------------------------------------------------------- scale

    private static void Scale(Action<bool, string> check)
    {
        // A project several times larger than the measured real ones (≈ 400 KiB raw): 6 MiB of tables and 5000 operations still serialize and read in well under the CI budget.
        var big = Doc(ops: 5000, files: 12, fileSize: 512 * 1024); var sw = System.Diagnostics.Stopwatch.StartNew(); byte[] bytes = NdsProjectCodec.Serialize(big); long write = sw.ElapsedMilliseconds;
        sw.Restart(); var read = NdsProjectCodec.Read(bytes); long readMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"   escala: {big.State.Files.Values.Sum(f => (long)f.Length) / 1024} KiB brutos → {bytes.Length / 1024} KiB · serializar {write} ms · leer y verificar {readMs} ms");
        check(read.Ok && SameDoc(big, read.Document!) && write < 8000 && readMs < 8000, "NdsProject_LargeProjectRoundTripsWithinTheBudget");
    }
}
