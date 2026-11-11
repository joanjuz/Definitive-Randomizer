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
    internal static NdsProjectRom Rom(string sha = "AA", string code = "CPUE", int rev = 1) => new(sha.PadRight(64, '0'), "BB".PadRight(40, '0'), code, rev, "Pokémon Platino (USA, rev. 1)", 134217728, false, @"D:\Roms\Platino.nds");

    internal static NdsProjectDocument Doc(int ops = 3, int files = 2, int fileSize = 4096, long generation = 1, int seed = 1, bool events = true, bool arm9 = true)
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

    internal static bool SameDoc(NdsProjectDocument a, NdsProjectDocument b) =>
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
        check(read.Ok && SameDoc(doc, read.Document!) && read.Document!.Meta.Blobs!.Count == 7, "NdsProject_RoundTripKeepsEveryField");
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
        // The same inside the ROM identity, an operation record and the event history (a newer build may extend those objects too).
        var relabeled = (JsonArray)JsonNode.Parse(NdsProjectCodec.HistoryJson(doc.Meta.Operations))!; relabeled[0]!["Etiqueta"] = new JsonArray(1, 2);
        var nested = NdsProjectCodec.Read(Remanifest(NdsProjectCodec.Assemble(doc, [System.Text.Encoding.UTF8.GetBytes(relabeled.ToJsonString())]), m => { m["Rom"]!["Region"] = "EUR"; m["Events"]!["Mapa"] = new JsonObject { ["Z"] = 3 }; }));
        var nestedAgain = nested.Ok ? NdsProjectCodec.Read(NdsProjectCodec.Serialize(nested.Document!)).Document! : null;
        check(nestedAgain is not null && nestedAgain.Meta.Rom.Extra!["Region"].GetString() == "EUR" && nestedAgain.Meta.Operations[0].Extra!["Etiqueta"].GetRawText() == "[1,2]" && nestedAgain.Meta.Events!.Extra!["Mapa"].GetRawText() == "{\"Z\":3}"
            && nestedAgain.Meta.Rom.SameRomAs(doc.Meta.Rom) && NdsProjectAssociation.Evaluate(nestedAgain.Meta.Rom, doc.Meta.Rom).IsMatch, "NdsProject_UnknownFieldsInsideTheRomOperationsAndEventsAreKept");
        // Determinism does not depend on the order in which the tables were added to the state.
        var reversed = new NdsProjectState { Arm9 = doc.State.Arm9, EventData = doc.State.EventData, EventScripts = doc.State.EventScripts };
        foreach (var (path, data) in doc.State.Files.Reverse()) reversed.Files[path] = data; foreach (var (id, data) in doc.State.Overlays) reversed.Overlays[id] = data;
        check(NdsProjectCodec.Serialize(new(doc.Meta, reversed)).AsSpan().SequenceEqual(bytes), "NdsProject_SerializationDoesNotDependOnInsertionOrder");
        // The history is not in the manifest: it is a compressed table, so a randomization with thousands of changes costs kilobytes, not megabytes.
        var heavy = Doc(ops: 0); heavy = new(heavy.Meta with { Operations = Enumerable.Range(0, 6).Select(o => new NdsProjectOperationRecord("Randomizar " + o, (uint)o, new JsonObject { ["Kind"] = "Species" },
            Enumerable.Range(0, 3000).Select(i => new Gen5Change("Entrenador " + (i / 3) + " · Pokémon " + (i % 3), "Especie", (i * 7 + o) % 493, (i * 13 + o) % 493)).ToList())).ToList() }, heavy.State);
        byte[] heavyBytes = NdsProjectCodec.Serialize(heavy); int heavyManifest = BitConverter.ToInt32(heavyBytes, 28); var heavyRead = NdsProjectCodec.Read(heavyBytes);
        var historyEntries = heavyRead.Document?.Meta.Blobs!.Where(b => b.Kind == NdsProjectBlobKind.History).ToList() ?? []; long historyRaw = historyEntries.Sum(b => b.RawLength), historyStored = historyEntries.Sum(b => b.StoredLength);
        check(heavyRead.Ok && SameDoc(heavy, heavyRead.Document!) && historyEntries.Count == 2 && historyEntries.All(b => b.Codec == "br") && historyEntries.Select(b => b.Name).SequenceEqual(["history:000000", "history:000001"])
            && historyRaw > 1_000_000 && historyStored < historyRaw / 4 && heavyManifest < 4096
            && !System.Text.Encoding.UTF8.GetString(heavyBytes, NdsProjectCodec.HeaderSize, heavyManifest).Contains("\"Operations\":[{\"Action\"", StringComparison.Ordinal), $"NdsProject_HistoryIsCompressedBlocksNotPartOfTheManifest_{historyRaw / 1024}KiBTo{historyStored / 1024}KiB");
        HistoryBlocks(heavy, check);
        check(NdsProjectCodec.Read(NdsProjectCodec.Serialize(Doc(ops: 0, events: false))) is { Ok: true } none && none.Document!.Meta.Blobs!.All(b => b.Kind != NdsProjectBlobKind.History) && none.Document.Meta.Operations.Count == 0, "NdsProject_AProjectWithoutOperationsHasNoHistoryTable");
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
        // Short content that is not the start of a project is "not a project", not "truncated" (a small text file is not an interrupted save).
        check(NdsProjectCodec.Read("hola"u8).Status == NdsProjectReadStatus.NotAProject && NdsProjectCodec.Read("{\"Format\":\"x\"} y algo más"u8).Status == NdsProjectReadStatus.NotAProject
            && NdsProjectCodec.Read(NdsProjectCodec.Magic.AsSpan(0, 5)).Status == NdsProjectReadStatus.Truncated && NdsProjectCodec.Read(bytes.AsSpan(0, 20)).Status == NdsProjectReadStatus.Truncated, "NdsProject_ShortForeignContentIsNotAProjectAndAShortProjectIsTruncated");
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
        HostileManifest(check, bytes);
    }

    /// <summary>Rewrites the manifest of a valid project and makes every checksum valid again: only the validation of the manifest's CONTENT is left to refuse it.</summary>
    private static byte[] Remanifest(byte[] data, Action<JsonObject> patch, Action<byte[]>? header = null)
    {
        int length = BitConverter.ToInt32(data, 28); var node = (JsonObject)JsonNode.Parse(data.AsSpan(NdsProjectCodec.HeaderSize, length))!; patch(node);
        byte[] manifest = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString()); var output = new byte[data.Length - length + manifest.Length];
        data.AsSpan(0, NdsProjectCodec.HeaderSize).CopyTo(output); manifest.CopyTo(output, NdsProjectCodec.HeaderSize); data.AsSpan(NdsProjectCodec.HeaderSize + length).CopyTo(output.AsSpan(NdsProjectCodec.HeaderSize + manifest.Length));
        BitConverter.TryWriteBytes(output.AsSpan(28), (uint)manifest.Length); BitConverter.TryWriteBytes(output.AsSpan(48), (ulong)output.Length); header?.Invoke(output); Header(output);
        return Resign(output);
    }

    /// <summary>A manifest with valid checksums but incomplete or contradictory content (a hostile or a buggy writer) is refused as Invalid, never with an exception, and never yields a document with missing parts.</summary>
    private static void HostileManifest(Action<bool, string> check, byte[] bytes)
    {
        check(NdsProjectCodec.Read(Remanifest(bytes, _ => { })).Ok, "NdsProject_RewrittenManifestWithValidChecksumsIsTheControlCase");
        static JsonObject Blob(JsonObject m, int i) => (JsonObject)m["Blobs"]![i]!;
        static JsonObject Ev(JsonObject m) => (JsonObject)m["Events"]!;
        var cases = new (string Name, Action<JsonObject> Patch)[]
        {
            ("BlobNameNull", m => Blob(m, 0)["Name"] = null), ("BlobCodecNull", m => Blob(m, 0)["Codec"] = null), ("BlobShaNull", m => Blob(m, 0)["Sha256"] = null), ("BlobEntryNull", m => ((JsonArray)m["Blobs"]!)[0] = null),
            ("BlobsNull", m => m["Blobs"] = null), ("RomNull", m => m["Rom"] = null), ("RomShaNull", m => m["Rom"]!["Sha256"] = null), ("RomSha1Null", m => m["Rom"]!["Sha1"] = null), ("RomCodeNull", m => m["Rom"]!["GameCode"] = null),
            ("RomGameNull", m => m["Rom"]!["Game"] = null), ("RomPathNull", m => m["Rom"]!["LastKnownPath"] = null), ("RomShaNotHex", m => m["Rom"]!["Sha256"] = "zz"), ("RomLengthNegative", m => m["Rom"]!["Length"] = -1),
            ("EventsEmptyObject", m => m["Events"] = new JsonObject()), ("EventOperationsNull", m => Ev(m)["Operations"] = null), ("EventOperationNull", m => ((JsonArray)Ev(m)["Operations"]!)[0] = null), ("EventAuditNull", m => Ev(m)["Audit"] = null),
            ("EventAuditLineNull", m => ((JsonArray)Ev(m)["Audit"]!)[0] = null), ("EventKindNull", m => Ev(m)["Operations"]![0]!["Kind"] = null), ("EventMoveWithoutPayload", m => Ev(m)["Operations"]![0]!["Move"] = null),
            ("RecompressNull", m => m["RecompressOverlays"] = null), ("WriterNull", m => m["Writer"] = null), ("FormatVersionZero", m => m["FormatVersion"] = 0), ("ManifestMinReaderDiffersFromHeader", m => m["MinReaderVersion"] = 0),
            ("ProjectIdEmpty", m => m["ProjectId"] = Guid.Empty.ToString()),
        };
        foreach (var (name, patch) in cases)
        {
            string outcome;
            try { var r = NdsProjectCodec.Read(Remanifest(bytes, patch)); outcome = r.Ok ? "accepted" : r.Status.ToString(); } catch (Exception ex) { outcome = "threw " + ex.GetType().Name; }
            check(outcome == nameof(NdsProjectReadStatus.Invalid), $"NdsProject_HostileManifest_{name}_IsInvalid" + (outcome == nameof(NdsProjectReadStatus.Invalid) ? "" : " [" + outcome + "]"));
        }
        // The same for the history, which is a table of its own: valid checksums, incomplete or malformed content.
        var doc = NdsProjectCodec.Read(bytes).Document!;
        check(NdsProjectCodec.Read(NdsProjectCodec.Assemble(doc, [NdsProjectCodec.HistoryJson(doc.Meta.Operations)])).Ok, "NdsProject_ReassembledHistoryIsTheControlCase");
        byte[] History(Action<JsonArray> patch) { var array = (JsonArray)JsonNode.Parse(NdsProjectCodec.HistoryJson(doc.Meta.Operations))!; patch(array); return System.Text.Encoding.UTF8.GetBytes(array.ToJsonString()); }
        var histories = new (string Name, byte[] Bytes)[]
        {
            ("NullInsteadOfAList", "null"u8.ToArray()), ("AnObjectInsteadOfAList", "{}"u8.ToArray()), ("NotJson", "[{"u8.ToArray()), ("EmptyList", "[]"u8.ToArray()), ("Empty", []),
            ("OperationNull", History(a => a[0] = null)), ("OperationActionNull", History(a => a[0]!["Action"] = null)), ("OperationRecipeNull", History(a => a[0]!["Recipe"] = null)), ("OperationRecipeIsAList", History(a => a[0]!["Recipe"] = new JsonArray())),
            ("OperationChangesNull", History(a => a[0]!["Changes"] = null)), ("OperationChangeNull", History(a => ((JsonArray)a[0]!["Changes"]!)[0] = null)), ("OneOperationMissing", History(a => a.RemoveAt(0))), ("OneOperationTooMany", History(a => a.Add(a[0]!.DeepClone()))),
        };
        foreach (var (name, history) in histories)
        {
            string outcome;
            try { var r = NdsProjectCodec.Read(NdsProjectCodec.Assemble(doc, [history])); outcome = r.Ok ? "accepted" : r.Status.ToString(); } catch (Exception ex) { outcome = "threw " + ex.GetType().Name; }
            check(outcome == nameof(NdsProjectReadStatus.Invalid), $"NdsProject_HostileHistory_{name}_IsInvalid" + (outcome == nameof(NdsProjectReadStatus.Invalid) ? "" : " [" + outcome + "]"));
        }
        // A project that says it has operations but carries no history table.
        check(NdsProjectCodec.Read(NdsProjectCodec.Assemble(doc, null)).Status == NdsProjectReadStatus.Invalid, "NdsProject_OperationsCountedInTheHeaderButMissingAreInvalid");
        // A generation below 1 (0 means "no project yet") is never a stored project, even when the header and the manifest agree on it.
        foreach (long generation in new[] { 0L, -1L })
            check(NdsProjectCodec.Read(Remanifest(bytes, m => m["Generation"] = generation, h => BitConverter.TryWriteBytes(h.AsSpan(16), generation))).Status == NdsProjectReadStatus.Invalid, $"NdsProject_Generation{generation}_IsInvalid");
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

    internal static NdsProjectDocument Next(NdsProjectDocument current, int tweak) =>
        new(current.Meta with { Generation = current.Generation + 1, SavedUtc = current.Meta.SavedUtc.AddMinutes(1), Operations = [.. current.Meta.Operations, new NdsProjectOperationRecord("op " + tweak, null, new JsonObject { ["Kind"] = "Edit" }, [new("l", "f", 0, tweak)])] },
            new NdsProjectState { Files = new(current.State.Files) { ["a/0/0/1"] = BitConverter.GetBytes(tweak) } });
    internal static NdsProjectStoreError? Fails(Action action) { try { action(); return null; } catch (NdsProjectStoreException ex) { return ex.Kind; } }

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
        using (var s = new InMemoryNdsProjectStore(future)) check(s.Load().Status == NdsProjectLoadStatus.NewerFormat && Fails(() => s.Commit(Next(Doc(), 1), 1)) == NdsProjectStoreError.NewerFormat && future.Main.AsSpan().SequenceEqual(before), "NdsProjectStore_NewerFormatIsNeverModified");
        // A newer-format main file is not "damaged": a recovery must not move it aside and install an older backup over it.
        var newerWithBackups = Populated(4, out var newest); byte[] newerMain = NdsProjectCodec.Serialize(new NdsProjectDocument(newest.Meta with { MinReaderVersion = 2, Generation = 5 }, newest.State)); newerWithBackups.Main = newerMain;
        using (var s = new InMemoryNdsProjectStore(newerWithBackups))
        {
            var backup = NdsProjectCodec.Read(newerWithBackups.Backups[0]); var candidate = new NdsProjectRecoveryCandidate("copia 1", backup.Document!.Generation, backup.Document.OperationCount, backup.Document.Meta.SavedUtc, backup.Document, newerWithBackups.Backups[0]);
            check(s.Load() is { Status: NdsProjectLoadStatus.NewerFormat, Plan: null } && Fails(() => s.Recover(candidate)) == NdsProjectStoreError.NewerFormat && newerWithBackups.Main.AsSpan().SequenceEqual(newerMain) && newerWithBackups.Preserved == 0, "NdsProjectRecovery_ANewerFormatMainIsNeverReplacedByAnOlderBackup");
        }
        // A candidate that is not (or no longer) one of the backups of this project is refused, valid or not.
        var foreign = Populated(4, out _); foreign.Main![NdsProjectCodec.HeaderSize + 30] ^= 1; byte[] foreignBefore = foreign.Main.ToArray(); var other = Doc(generation: 2, seed: 77);
        using (var s = new InMemoryNdsProjectStore(foreign))
            check(Fails(() => s.Recover(new("ajena", 2, other.OperationCount, other.Meta.SavedUtc, other, NdsProjectCodec.Serialize(other)))) == NdsProjectStoreError.WriteFailed && foreign.Main.AsSpan().SequenceEqual(foreignBefore) && foreign.Preserved == 0, "NdsProjectRecovery_ACandidateThatIsNotABackupOfThisProjectIsRefused");
        // Same generation, another project or another ROM put in place of the file: never overwritten.
        foreach (var (label, replace) in new (string, Func<NdsProjectDocument, NdsProjectDocument>)[]
            { ("AnotherProject", d => new(d.Meta with { ProjectId = Guid.NewGuid() }, d.State)), ("AnotherRom", d => new(d.Meta with { Rom = Rom(sha: "CD") }, d.State)) })
        {
            var swapped = Populated(3, out var mine); byte[] theirs = NdsProjectCodec.Serialize(replace(mine)); swapped.Main = theirs;
            using var s = new InMemoryNdsProjectStore(swapped);
            check(Fails(() => s.Commit(Next(mine, 8), mine.Generation)) == NdsProjectStoreError.DifferentProject && swapped.Main.AsSpan().SequenceEqual(theirs) && swapped.Backups.Count == 2, $"NdsProjectStore_{label}WithTheSameGenerationIsNeverOverwritten");
        }
        // A document the codec refuses to write is a definite failure of the store's own kind (the contract allows no other exception).
        var unwritable = Populated(2, out var valid);
        using (var s = new InMemoryNdsProjectStore(unwritable))
            check(Fails(() => s.Commit(new(Next(valid, 3).Meta with { ProjectId = Guid.Empty }, valid.State), valid.Generation)) is NdsProjectStoreError.DifferentProject or NdsProjectStoreError.WriteFailed
                && Fails(() => s.Commit(new(Next(valid, 3).Meta with { Operations = [null!] }, valid.State), valid.Generation)) == NdsProjectStoreError.WriteFailed && s.Load().Document!.Generation == 2, "NdsProjectStore_AnUnwritableDocumentIsADefiniteFailure");
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
        // An exception the contract does not allow (the store cannot say what happened) is an unknown outcome, never "nothing changed".
        var m6 = new NdsProjectMedium(); var rogue = new FlakyStore(new InMemoryNdsProjectStore(m6)); var t6 = new NdsProjectTransactor(rogue, Doc(generation: 0)); t6.Run(Prepare, Publish); published.Clear();
        rogue.ThrowAfterCommit = new UnauthorizedAccessException("acceso denegado");
        check(Faults(() => t6.Run(Prepare, Publish)) && t6.Faulted && published.Count == 0 && t6.Current.Generation == 1, "NdsProjectTransactor_AnExceptionOutsideTheContractIsAnUnknownOutcome");
        adopted = null; t6.Reconcile(d => adopted = d);
        check(!t6.Faulted && adopted!.Generation == 2 && t6.Run(Prepare, Publish).Generation == 3, "NdsProjectTransactor_ReconcileAfterAContractBreachAdoptsWhatIsDurable");
        // Out of memory while installing: the operation is already durable, so the session must block instead of drifting behind the file.
        var m7 = new NdsProjectMedium(); var s7 = new InMemoryNdsProjectStore(m7); var t7 = new NdsProjectTransactor(s7, Doc(generation: 0)); t7.Run(Prepare, Publish);
        check(Faults(() => t7.Run(Prepare, _ => throw new OutOfMemoryException())) && t7.Faulted && s7.Load().Document!.Generation == 2 && t7.Current.Generation == 1, "NdsProjectTransactor_OutOfMemoryWhileInstallingBlocksInsteadOfDrifting");
        adopted = null; t7.Reconcile(d => adopted = d);
        check(!t7.Faulted && t7.Current.Generation == 2 && t7.Run(Prepare, Publish).Generation == 3, "NdsProjectTransactor_ReconcileAfterOutOfMemoryResumes");
        // An operation cannot move the project to another identity, and a reconcile never adopts another project.
        int commitsBefore = s7.Commits; bool refusedIdentity = false;
        try { t7.Run(d => new(Prepare(d).Meta with { Rom = Rom(sha: "EF") }, d.State), Publish); } catch (InvalidOperationException) { refusedIdentity = true; }
        check(refusedIdentity && s7.Commits == commitsBefore && !t7.Faulted, "NdsProjectTransactor_AnOperationCannotChangeTheRomOrTheProject");
        try { t7.Run(Prepare, _ => throw new InvalidOperationException("x")); } catch (NdsProjectFaultedException) { }
        byte[] durable7 = m7.Main!; m7.Main = NdsProjectCodec.Serialize(new NdsProjectDocument(s7.Load().Document!.Meta with { ProjectId = Guid.NewGuid() }, new NdsProjectState()));
        check(Faults(() => t7.Reconcile(Publish)) && t7.Faulted, "NdsProjectTransactor_ReconcileNeverAdoptsAnotherProject");
        m7.Main = durable7; t7.Reconcile(Publish); check(!t7.Faulted && t7.Current.Generation == 4, "NdsProjectTransactor_ReconcileWorksOnceTheRightFileIsBack");
        Days(check);
        static bool Faults(Action a) { try { a(); return false; } catch (NdsProjectFaultedException) { return true; } }
    }

    private sealed class FlakyStore(INdsProjectStore inner) : INdsProjectStore
    {
        internal bool UnknownNext; internal Exception? ThrowAfterCommit;
        public NdsProjectLoadResult Load() => inner.Load();
        public long Commit(NdsProjectDocument next, long expected)
        {
            if (UnknownNext) { UnknownNext = false; throw new NdsProjectStoreException(NdsProjectStoreError.OutcomeUnknown, "sin respuesta"); }
            long generation = inner.Commit(next, expected);
            if (ThrowAfterCommit is { } ex) { ThrowAfterCommit = null; throw ex; }
            return generation;
        }
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
        // A long project: 2000 operations of which 40 are full randomizations (5000 changes each). The history is megabytes of JSON; it is compressed, and a save
        // after one more operation does not serialize the earlier ones again.
        var long_ = Doc(ops: 0, files: 4, fileSize: 64 * 1024); var records = new List<NdsProjectOperationRecord>();
        for (int o = 0; o < 2000; o++) records.Add(new("Operación " + o, o % 50 == 0 ? (uint)o : null, new JsonObject { ["Kind"] = o % 50 == 0 ? "Species" : "Edit", ["I"] = o },
            Enumerable.Range(0, o % 50 == 0 ? 5000 : 2).Select(i => new Gen5Change("Entrada " + i, "Especie", (i + o) % 493, (i * 3 + o) % 493)).ToList()));
        long_ = new(long_.Meta with { Operations = records }, long_.State);
        var clock = System.Diagnostics.Stopwatch.StartNew(); byte[] first = NdsProjectCodec.Serialize(long_); long cold = clock.ElapsedMilliseconds;
        var grown = new NdsProjectDocument(long_.Meta with { Generation = 2, Operations = [.. records, new("Una más", null, new JsonObject { ["Kind"] = "Edit" }, [new("x", "y", 1, 2)])] }, long_.State);
        clock.Restart(); byte[] second = NdsProjectCodec.Serialize(grown); long warm = clock.ElapsedMilliseconds; clock.Restart(); var longRead = NdsProjectCodec.Read(second); long longReadMs = clock.ElapsedMilliseconds;
        Console.WriteLine($"   historial largo: 2001 operaciones, {grown.Meta.Operations.Sum(o => o.Changes.Count)} cambios → archivo {second.Length / 1024} KiB · primera serialización {cold} ms · siguiente guardado {warm} ms · leer y verificar {longReadMs} ms");
        // Verifying a save checks the container and every table without rebuilding the history, so it does not cost what opening the project costs.
        clock.Restart(); var verified = NdsProjectCodec.Verify(second); long verifyMs = clock.ElapsedMilliseconds; var broken = (byte[])second.Clone(); broken[NdsProjectCodec.HeaderSize + BitConverter.ToInt32(second, 28) + 400] ^= 1;
        Console.WriteLine($"   verificar ese guardado sin reconstruir el historial: {verifyMs} ms");
        check(verified.Ok && verified.Document!.Meta.Operations.Count == 0 && verified.Header!.OperationCount == grown.OperationCount && verified.Document.State.SameAs(grown.State) && verified.Document.Generation == 2 && !NdsProjectCodec.Verify(broken).Ok
            && !NdsProjectCodec.Verify(second.AsSpan(0, second.Length - 1)).Ok && verifyMs <= Math.Max(100, longReadMs), "NdsProject_VerifyingASaveChecksEveryTableWithoutRebuildingTheHistory");
        check(longRead.Ok && SameDoc(grown, longRead.Document!) && first.Length < 8 * 1024 * 1024 && warm < 4000 && longReadMs < 15000, "NdsProject_ALongHistoryWithFortyRandomizationsStaysSmallAndSavesWithinTheBudget");
        var big = Doc(ops: 5000, files: 12, fileSize: 512 * 1024); var sw = System.Diagnostics.Stopwatch.StartNew(); byte[] bytes = NdsProjectCodec.Serialize(big); long write = sw.ElapsedMilliseconds;
        sw.Restart(); var read = NdsProjectCodec.Read(bytes); long readMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"   escala: {big.State.Files.Values.Sum(f => (long)f.Length) / 1024} KiB brutos → {bytes.Length / 1024} KiB · serializar {write} ms · leer y verificar {readMs} ms");
        check(read.Ok && SameDoc(big, read.Document!) && write < 8000 && readMs < 8000, "NdsProject_LargeProjectRoundTripsWithinTheBudget");
    }

    // ------------------------------------------------------------------------------------------------------------------------------ history blocks

    private static NdsProjectOperationRecord Op(int i, int changes) => new("Operación " + i, (uint)i, new JsonObject { ["Kind"] = changes > 100 ? "Species" : "Edit", ["N"] = i },
        Enumerable.Range(0, changes).Select(c => new Gen5Change("Entrenador " + (c / 3) + " · Pokémon " + (c % 3), "Especie", (c * 7 + i) % 493, (c * 13 + i) % 493)).ToList());

    /// <summary>A copy of the operations with new objects (as a project reopened from disk has): equal content, nothing shared with the cache of the originals.</summary>
    private static List<NdsProjectOperationRecord> Fresh(IEnumerable<NdsProjectOperationRecord> operations) =>
        operations.Select(o => new NdsProjectOperationRecord(o.Action, o.Seed, (JsonObject)o.Recipe.DeepClone(), o.Changes.ToList())).ToList();

    /// <summary>The offset and length of every stored table, in the order of the index.</summary>
    private static List<(string Name, int Offset, int Length)> Layout(byte[] data)
    {
        int manifest = BitConverter.ToInt32(data, 28); var node = (JsonObject)JsonNode.Parse(data.AsSpan(NdsProjectCodec.HeaderSize, manifest))!; int at = NdsProjectCodec.HeaderSize + manifest;
        var layout = new List<(string, int, int)>();
        foreach (var blob in node["Blobs"]!.AsArray()) { int length = BitConverter.ToInt32(data, at); layout.Add((blob!["Name"]!.GetValue<string>(), at + 4, length)); at += 4 + length; }
        return layout;
    }

    private static void HistoryBlocks(NdsProjectDocument heavy, Action<bool, string> check)
    {
        // Many small edits with a randomization every so often, as a real project has: the operation limit and the weight limit both close blocks.
        var operations = Enumerable.Range(0, 900).Select(i => Op(i, i % 97 == 50 && i < 400 ? 6000 : 1 + i % 3)).ToList();
        var full = NdsProjectCodec.HistoryBlocks(operations);
        bool prefixStable = true;
        for (int n = 1; n <= operations.Count; n++)
        {
            var prefix = NdsProjectCodec.HistoryBlocks(operations.Take(n).ToList());
            // Every block of a shorter history that is closed is exactly a block of the longer one; only the last one may still grow.
            for (int b = 0; b < prefix.Count; b++)
                if (prefix[b].Closed ? prefix[b] != full[b] : prefix[b].Start != full[b].Start || prefix[b].Count > full[b].Count) prefixStable = false;
        }
        check(prefixStable && full.Sum(b => b.Count) == operations.Count && full.All(b => b.Count is > 0 and <= NdsProjectCodec.HistoryBlockOperations) && full.Zip(full.Skip(1)).All(p => p.First.Start + p.First.Count == p.Second.Start)
            && full.Any(b => b.Count == NdsProjectCodec.HistoryBlockOperations) && full.Any(b => b.Count < NdsProjectCodec.HistoryBlockOperations && b.Closed), $"NdsProject_HistoryBlockBoundariesNeverMoveWhenOperationsAreAppended_{full.Count}Blocks");

        // A project that grows one operation at a time writes exactly what a fresh serialization of the same content writes, at every step.
        var baseDoc = Doc(ops: 0); bool sameAsFresh = true; byte[] previous = []; bool closedKept = true;
        for (int n = 1; n <= operations.Count; n += n < 40 ? 1 : 37)
        {
            var grown = new NdsProjectDocument(baseDoc.Meta with { Operations = operations.Take(n).ToList() }, baseDoc.State);
            byte[] incremental = NdsProjectCodec.Serialize(grown), fresh = NdsProjectCodec.Serialize(new NdsProjectDocument(baseDoc.Meta with { Operations = Fresh(operations.Take(n)) }, baseDoc.State));
            sameAsFresh &= incremental.AsSpan().SequenceEqual(fresh);
            if (previous.Length > 0)
            {
                // Every block that was already closed is stored with the same bytes in the next save.
                var before = Layout(previous).Where(l => l.Name.StartsWith(NdsProjectCodec.HistoryPrefix, StringComparison.Ordinal)).ToList(); var after = Layout(incremental).ToDictionary(l => l.Name);
                var closed = NdsProjectCodec.HistoryBlocks(operations.Take(n).ToList());
                for (int b = 0; b < before.Count - 1; b++)
                    if (!after.TryGetValue(before[b].Name, out var now) || !previous.AsSpan(before[b].Offset, before[b].Length).SequenceEqual(incremental.AsSpan(now.Offset, now.Length))) closedKept = false;
            }
            previous = incremental;
        }
        check(sameAsFresh, "NdsProject_AGrowingHistoryIsByteIdenticalToAFreshSerializationAtEveryStep");
        check(closedKept, "NdsProject_ClosedHistoryBlocksAreStoredWithTheSameBytesInTheNextSave");

        // Reopened (new objects, blocks reused from the file) and saved again: same bytes; the operations come back in their exact order.
        var doc = new NdsProjectDocument(baseDoc.Meta with { Operations = operations }, baseDoc.State); byte[] bytes = NdsProjectCodec.Serialize(doc); var read = NdsProjectCodec.Read(bytes);
        check(read.Ok && SameDoc(doc, read.Document!) && read.Document!.Meta.Operations.Select(o => o.Action).SequenceEqual(operations.Select(o => o.Action)) && NdsProjectCodec.Serialize(read.Document).AsSpan().SequenceEqual(bytes)
            && read.Document.Meta.Blobs!.Count(b => b.Kind == NdsProjectBlobKind.History) == full.Count, "NdsProject_AReopenedHistoryKeepsTheOrderOfEveryOperationAndSavesTheSameBytes");
        var appended = new NdsProjectDocument(read.Document!.Meta with { Operations = [.. read.Document.Meta.Operations, Op(9999, 2)] }, read.Document.State);
        check(NdsProjectCodec.Serialize(appended).AsSpan().SequenceEqual(NdsProjectCodec.Serialize(new NdsProjectDocument(appended.Meta with { Operations = Fresh(appended.Meta.Operations) }, appended.State))),
            "NdsProject_AnOperationAppendedAfterReopeningSavesWhatAFreshSerializationSaves");

        // Other boundaries (another writer) are read back in order and rewritten with the canonical ones.
        var split = new[] { NdsProjectCodec.HistoryJson(operations.Take(10).ToList()), NdsProjectCodec.HistoryJson(operations.Skip(10).ToList()) };
        var other = NdsProjectCodec.Read(NdsProjectCodec.Assemble(doc, split));
        check(other.Ok && SameDoc(doc, other.Document!) && NdsProjectCodec.Serialize(other.Document!).AsSpan().SequenceEqual(bytes), "NdsProject_HistoryBlocksWithOtherBoundariesAreReadInOrderAndRewrittenCanonically");

        // Hostile or damaged blocks with valid checksums elsewhere.
        var blocks = new List<byte[]> { NdsProjectCodec.HistoryJson(operations.Take(300).ToList()), NdsProjectCodec.HistoryJson(operations.Skip(300).ToList()) };
        byte[] two = NdsProjectCodec.Assemble(doc, blocks);
        var hostile = new (string Name, Func<byte[]> Bytes)[]
        {
            ("AnEmptyBlock", () => NdsProjectCodec.Assemble(doc, [blocks[0], "[]"u8.ToArray(), blocks[1]])),
            ("ABlockThatIsNotAList", () => NdsProjectCodec.Assemble(doc, [blocks[0], "{}"u8.ToArray()])),
            ("ABlockMissing", () => NdsProjectCodec.Assemble(doc, [blocks[1]])),
            ("AGapInTheNumbering", () => Remanifest(two, m => m["Blobs"]!.AsArray().First(b => b!["Name"]!.GetValue<string>() == "history:000001")!["Name"] = "history:000002")),
            ("TheBlocksOutOfOrder", () => Remanifest(two, m => { foreach (var b in m["Blobs"]!.AsArray()) if (b!["Name"]!.GetValue<string>() is var n && n.StartsWith("history:", StringComparison.Ordinal)) b["Name"] = n == "history:000000" ? "history:000001" : "history:000000"; })),
            ("TheOldSingleHistoryName", () => Remanifest(NdsProjectCodec.Assemble(doc, [NdsProjectCodec.HistoryJson(operations)]), m => m["Blobs"]!.AsArray().First(b => b!["Name"]!.GetValue<string>() == "history:000000")!["Name"] = "history")),
            ("AnUnpaddedBlockName", () => Remanifest(NdsProjectCodec.Assemble(doc, [NdsProjectCodec.HistoryJson(operations)]), m => m["Blobs"]!.AsArray().First(b => b!["Name"]!.GetValue<string>() == "history:000000")!["Name"] = "history:0")),
        };
        foreach (var (name, make) in hostile)
        {
            string outcome;
            try { var r = NdsProjectCodec.Read(make()); outcome = r.Ok ? "accepted" : r.Status.ToString(); } catch (Exception ex) { outcome = "threw " + ex.GetType().Name; }
            check(outcome == nameof(NdsProjectReadStatus.Invalid), $"NdsProject_HostileHistoryBlocks_{name}_IsInvalid" + (outcome == nameof(NdsProjectReadStatus.Invalid) ? "" : " [" + outcome + "]"));
        }
        // A stored block damaged after it was written: opening and checking a save both refuse it, even though this process proved the original bytes.
        var layout = Layout(bytes).First(l => l.Name == "history:000001"); var damaged = (byte[])bytes.Clone(); damaged[layout.Offset + layout.Length / 2] ^= 0x41; damaged = Resign(damaged);
        check(NdsProjectCodec.Read(damaged).Status == NdsProjectReadStatus.Corrupt && NdsProjectCodec.Verify(damaged).Status == NdsProjectReadStatus.Corrupt && NdsProjectCodec.Verify(bytes).Ok,
            "NdsProject_ADamagedHistoryBlockIsCorruptWhenOpenedAndWhenASaveIsChecked");

        // The heavy history of the first checks: its two blocks hold three randomizations each, in order.
        var heavyBlocks = NdsProjectCodec.HistoryBlocks(heavy.Meta.Operations);
        check(heavyBlocks.Count == 2 && heavyBlocks[0] == (0, 3, true) && heavyBlocks[1] == (3, 3, true), "NdsProject_RandomizationsCloseTheirBlockByWeight");
    }
}
