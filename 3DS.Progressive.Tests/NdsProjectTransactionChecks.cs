using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Nds.Project;

/// <summary>
/// Persistent DS project, PR 3: Apply, Reset and the event saves go through the transaction. The central check is equivalence: after every operation, the
/// live session, a project reopened from the disk and a session that never had persistence hold the same bytes, and export the same ROM. Synthetic ROMs of the
/// six supported profiles (opened as romhacks, because the original tables are not redistributable); the probe repeats it on private dumps.
/// </summary>
internal static class NdsProjectTransactionChecks
{
    private const string Writer = "comprobaciones";
    private static object? Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Everything a session holds that reaches an exported ROM or its change log, as one digest: tables, overlays, ARM9, history and events.</summary>
    internal static string Fingerprint(NdsProject project)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string label, ReadOnlySpan<byte> data) { sha.AppendData(Encoding.UTF8.GetBytes(label + ":" + data.Length + ";")); sha.AppendData(data); }
        foreach (var (path, data) in ((Dictionary<string, byte[]>)Field(project, "_files")!).OrderBy(p => p.Key, StringComparer.Ordinal)) Add("file " + path, data);
        foreach (var (id, data) in ((Dictionary<int, byte[]>)Field(project, "_overlays")!).OrderBy(p => p.Key)) Add("overlay " + id, data);
        Add("arm9", (byte[]?)Field(project, "_arm9") ?? []);
        Add("recompress " + string.Join(",", ((HashSet<int>)Field(project, "_recompress")!).Order()), []);
        foreach (var operation in project.History)
            Add("operation", Encoding.UTF8.GetBytes(operation.Action + "|" + operation.Seed + "|" + operation.Recipe.ToJsonString() + "|" + string.Join(";", operation.Changes.Select(c => c.Location + "," + c.Field + "," + c.Before + "," + c.After))));
        if (Field(project, "_events") is NdsEventSession events)
        {
            Add("events", events.Data.Bytes()); Add("scripts", events.Data.ScriptArchiveBytes());
            Add("event history", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(events.History()) + string.Join("\n", events.Audit)));
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    /// <summary>Representative operations of every kind. Each one is computed on the project it is given, so two sessions in the same state get the same result.</summary>
    internal static IReadOnlyList<(string Name, Func<NdsProject, NdsPreview> Make)> Scenarios() =>
    [
        ("edición manual de un entrenador", p => { var cell = p.ReadTrainerFields().First(c => c.Max > c.Value && c.Kind == NdsValueKind.Number); return p.PreviewTrainerEdits(new Dictionary<string, int> { [cell.Key] = cell.Value + 1 }); }),
        ("edición manual de una especie", p => { var cell = p.Read(NdsSection.Pokemon).First(c => c.Max > c.Value && c.Kind == NdsValueKind.Number); return p.PreviewEdits(NdsSection.Pokemon, new Dictionary<string, int> { [cell.Key] = cell.Value + 1 }); }),
        ("randomizar entrenadores y salvajes", p => p.PreviewRandom("Especies", new Gen5RandomizerOptions(7) { Trainers = Gen5SpeciesMode.SimilarBst, Wild = Gen5SpeciesMode.SimilarBst })),
        ("entrenadores: equipos y reglas", p => p.PreviewTrainers("Entrenadores", new Gen5RandomizerOptions(8) { Trainers = Gen5SpeciesMode.Random }, new NdsTrainerOptions(8) { MinTeamSize = 3 })),
        ("datos: estadísticas, tipos y habilidades", p => p.PreviewData("Datos", NdsSection.Pokemon, new NdsDataOptions(9) { Stats = NdsStatsMode.Random, Types = true, Abilities = true })),
        ("movimientos por nivel", p => p.PreviewLearnsets("Level Up", new Dictionary<int, IReadOnlyList<NdsLearnsetMove>> { [1] = new NdsLearnsetMove[] { new(33, 1), new(45, 5), new(118, 9) }.Take(Math.Min(3, p.LearnsetLimit)).ToArray() })),
        ("ajustes del juego", p => p.PreviewTweaks(new NdsTweakOptions { UpdateTypeEffectiveness = true, GuaranteedCatch = true })),
        ("segunda edición manual de una especie", p => { var cell = p.Read(NdsSection.Pokemon).Last(c => c.Max > c.Value && c.Kind == NdsValueKind.Number); return p.PreviewEdits(NdsSection.Pokemon, new Dictionary<string, int> { [cell.Key] = cell.Value + 1 }); }),
        ("segunda randomización", p => p.PreviewRandom("Especies otra vez", new Gen5RandomizerOptions(21) { Trainers = Gen5SpeciesMode.Random, Wild = Gen5SpeciesMode.Random })),
    ];

    private static bool NotApplicable(Exception ex) => ex is InvalidOperationException or InvalidDataException or ArgumentException or NotSupportedException or KeyNotFoundException;

    /// <summary>Opens the ROM and attaches the project at the given path: the stored one if there is one, a new one otherwise.</summary>
    internal static NdsProject OpenPersistent(string rom, string projectPath, bool romhack, bool readOnly = false)
    {
        var project = NdsProject.Open(rom, romhack); NdsProjectFileStore? store = null;
        try
        {
            store = readOnly ? NdsProjectFileStore.OpenReadOnly(projectPath) : NdsProjectFileStore.Open(projectPath); var load = store.Load();
            if (load.Status is not (NdsProjectLoadStatus.Empty or NdsProjectLoadStatus.Loaded)) throw new InvalidDataException("Proyecto no legible: " + load.Message);
            project.AttachPersistence(store, NdsProjectRomIdentity.Compute(rom, project.IsRomhack), load.Document, Writer);
            return project;
        }
        catch { if (!project.IsPersistent) store?.Dispose(); project.Dispose(); throw; }
    }

    private static string? Throws(Action action) { try { action(); return null; } catch (Exception ex) { return ex.GetType().Name; } }

    internal static void Run(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-project-tx-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string gen5Base = Path.Combine(temp, "base.nds"); File.WriteAllBytes(gen5Base, NdsGen5RandomizerChecks.SyntheticBlackRomhack());
            var fixtures = Gen4Profile.All.Select(p => (p.GameCode, Bytes: NdsGen4Checks.SyntheticRom(p, true))).Concat(Gen5Profile.All.Select(p => (p.GameCode, Bytes: NdsTrainerParityChecks.SyntheticGen5(p, gen5Base)))).ToList();
            check(fixtures.Count == 6, "NdsProjectTx_SixProfilesAreExercised");
            foreach (var (code, bytes) in fixtures)
            {
                string folder = Directory.CreateDirectory(Path.Combine(temp, code)).FullName, rom = Path.Combine(folder, code + ".nds"); File.WriteAllBytes(rom, bytes);
                Equivalence(rom, rom + NdsProjectPaths.Extension, true, folder, (ok, name) => check(ok, "NdsProjectTx_" + code + "_" + name), minimum: 4);
            }
            string first = Path.Combine(temp, fixtures[0].GameCode, fixtures[0].GameCode + ".nds");
            Failures(first, check); Refusals(first, Path.Combine(temp, fixtures[^1].GameCode, fixtures[^1].GameCode + ".nds"), temp, check); Temporary(first, temp, check); Events(temp, check);
        }
        finally { Directory.Delete(temp, true); }
    }

    /// <summary>Real dumps, read only: the project files are written in the output folder, never next to the ROM.</summary>
    internal static void Probe(string output, IReadOnlyList<string> roms)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output); int checks = 0;
        foreach (string rom in roms)
        {
            string before = Hash(rom), name = Path.GetFileNameWithoutExtension(rom), folder = Directory.CreateDirectory(Path.Combine(output, new string(name.Where(char.IsLetterOrDigit).Take(24).ToArray()))).FullName;
            Console.WriteLine("== " + name);
            Equivalence(rom, Path.Combine(folder, "proyecto.nds.drproj"), false, folder, (ok, test) => { if (!ok) throw new Exception("FAIL " + test); checks++; Console.WriteLine("PASS " + test); }, minimum: 7);
            using (var identify = NdsRom.Open(rom)) if (identify.Header is { GameCode: "CPUE", Version: 1 }) RealEvents(rom, folder, (ok, test) => { if (!ok) throw new Exception("FAIL " + test); checks++; Console.WriteLine("PASS " + test); });
            if (Hash(rom) != before || Directory.GetFiles(Path.GetDirectoryName(rom)!, "*.drproj*").Length > 0) throw new Exception("FAIL la ROM original cambió o apareció un proyecto junto a ella");
            Console.WriteLine("PASS la ROM original no cambió y no se creó nada junto a ella"); checks++;
            foreach (string exported in Directory.GetFiles(folder, "*.nds")) File.Delete(exported);
        }
        Console.WriteLine($"Completed {checks} DS project transaction checks on private dumps (read-only sources).");
    }

    /// <summary>
    /// Real Platinum: a nurse with her own appended script is added on Route 201 in a persistent project, and another module then edits the script archive.
    /// The same script file is refused when the operation is confirmed; a different one combines, survives closing and reopening, and exports identically.
    /// </summary>
    private static void RealEvents(string rom, string folder, Action<bool, string> check)
    {
        byte[] originalScripts; using (var source = NdsRom.Open(rom)) originalScripts = source.ReadFile(PlatinumEvents.ScriptPath);
        NdsPreview Scripts(NdsProject project, int member)
        {
            var narc = Narc.Read(originalScripts); narc.Files[member] = [.. narc.Files[member], 0];
            return new NdsPreview((Guid)Field(project, "_id")!, project.Revision, "Otro módulo · scripts " + member, null, new Gen5RandomizerResult(new Dictionary<string, byte[]> { [PlatinumEvents.ScriptPath] = narc.Write() }, [new Gen5Change("Scripts " + member, "Bytes", 0, 1)], new Dictionary<int, byte[]>()), new System.Text.Json.Nodes.JsonObject { ["Kind"] = "Prueba" });
        }
        NdsEventProposal Nurse(NdsProject project, out int scriptFile)
        {
            var events = project.Events; var data = events.Data; var template = NdsNpcStructurePlanner.NurseTemplate(data, out _)!; var route = data.Maps.First(m => m.InternalName == "R201"); scriptFile = route.ScriptFile;
            var model = new NdsMapCanvasModel(data.Layout(route.Id), data.Events(route.Id), project.LoadTerrain().Terrain, data.Bounds(route.Id)); var anchor = data.Events(route.Id).First(e => e.Kind == PlatinumEventKind.Npc);
            for (int r = 1; r <= 12; r++) for (int dx = -r; dx <= r; dx++) foreach (int dy in new[] { -r, r })
            {
                var draft = NdsNpcStructurePlanner.PasteDraft(data, template, route.Id, anchor.X + dx, anchor.Y + dy); var (script, binding) = NdsNpcStructurePlanner.BindingFor(data, template, route.Id, NdsBehaviourMode.WithBehaviour, draft.Id);
                var plan = NdsNpcStructurePlanner.PlanAdd(data, model, route.Id, template.Source, draft with { Script = script });
                if (plan.Outcome == NdsMoveOutcome.Ready) return events.PreviewAdd(route.Id, plan.Record!, binding);
            }
            throw new InvalidOperationException("No se encontró una casilla libre en la Ruta 201.");
        }
        string project = Path.Combine(folder, "eventos.nds.drproj"), liveRom = Path.Combine(folder, "eventos-viva.nds"), resumedRom = Path.Combine(folder, "eventos-reanudada.nds"); string print;
        using (var live = OpenPersistent(rom, project, false))
        {
            live.SaveEvent(Nurse(live, out int scriptFile));
            check(live.SavedGeneration == 2 && live.SavedDocument is { State.EventScripts: not null, State.EventData: not null, Meta.Events.CheckpointVersion: 3 } && !Directory.GetFiles(folder, "*.ndsevents.json").Any(), "Events_ANurseWithHerOwnScriptIsSavedInTheProjectWithoutACheckpoint");
            string before = Fingerprint(live);
            check(Throws(() => live.Apply(Scripts(live, scriptFile))) == nameof(InvalidOperationException) && live.SavedGeneration == 2 && Fingerprint(live) == before, "Events_AnotherEditOfTheSameScriptFileIsRefusedWhenConfirmedAndNothingIsSaved");
            live.Apply(Scripts(live, scriptFile == 0 ? 1 : 0)); live.Apply(Scenarios()[2].Make(live));
            using (var reader = OpenPersistent(rom, project, false, readOnly: true)) check(Fingerprint(reader) == Fingerprint(live) && live.SavedGeneration == 4, "Events_AnEditOfAnotherScriptFileCombinesAndTheReopenedProjectIsIdentical");
            live.Export(liveRom); print = Fingerprint(live);
        }
        using (var resumed = OpenPersistent(rom, project, false))
        {
            resumed.Export(resumedRom);
            check(Fingerprint(resumed) == print && Hash(resumedRom) == Hash(liveRom), "Events_TheRomExportedAfterResumingHasTheSameEventsScriptsAndTables");
            resumed.SaveEvent(resumed.Events.PreviewUndo());
            check(resumed.SavedDocument is { State.EventScripts: null, State.EventData: null } && resumed.Events.Data.ScriptArchiveBytes().AsSpan().SequenceEqual(originalScripts), "Events_UndoingTheNurseRestoresTheOriginalEventsAndScripts");
        }
        // A checkpoint written by a session without a project (the previous way of working) is imported by the opener into a project; the file is only read.
        string checkpoint = Path.Combine(folder, "antiguo.ndsevents.json"); byte[] eventsOfCheckpoint, scriptsOfCheckpoint;
        using (var plain = NdsProject.Open(rom, false)) { plain.Events.Save(Nurse(plain, out _), checkpoint); eventsOfCheckpoint = plain.Events.Data.Bytes(); scriptsOfCheckpoint = plain.Events.Data.ScriptArchiveBytes(); }
        byte[] checkpointBytes = File.ReadAllBytes(checkpoint); string imported = Path.Combine(folder, "importado.nds.drproj");
        using (var opened = NdsProjectOpener.Open(new(rom, false, imported, checkpoint)))
            check(opened is { IsPersistent: true, SavedGeneration: 2 } && opened.Events.Data.Bytes().AsSpan().SequenceEqual(eventsOfCheckpoint) && opened.Events.Data.ScriptArchiveBytes().AsSpan().SequenceEqual(scriptsOfCheckpoint)
                && opened.SavedDocument!.Meta.Events is { ImportedFrom: 3 } && File.ReadAllBytes(checkpoint).AsSpan().SequenceEqual(checkpointBytes), "Events_AV3CheckpointIsImportedIntoAProjectByTheOpenerAndTheFileIsNotAltered");
        using (var opened = NdsProjectOpener.Open(new(rom, false, imported)))
            check(opened.SavedGeneration == 2 && opened.Events.CanUndo && opened.Events.Data.Bytes().AsSpan().SequenceEqual(eventsOfCheckpoint), "Events_TheImportedProjectResumesWithoutTheCheckpoint");
        // The other order: the script file is edited first, then the nurse that needs it is refused when her save is confirmed.
        string second = Path.Combine(folder, "eventos-2.nds.drproj");
        using (var live = OpenPersistent(rom, second, false))
        {
            var nurse = Nurse(live, out int scriptFile); live.Apply(Scripts(live, scriptFile)); string before = Fingerprint(live);
            check(Throws(() => live.SaveEvent(Nurse(live, out _))) == nameof(InvalidOperationException) && live.SavedGeneration == 2 && Fingerprint(live) == before && !live.Events.CanUndo, "Events_ANurseOnAScriptFileAlreadyEditedIsRefusedWhenConfirmedAndNothingIsSaved");
        }
    }

    private static void Equivalence(string rom, string projectPath, bool romhack, string folder, Action<bool, string> check, int minimum)
    {
        string resumedPrint, twinPrint; int applied = 0; bool durable = true, identical = true; long generation;
        using var twin = NdsProject.Open(rom, romhack);
        using (var live = OpenPersistent(rom, projectPath, romhack))
        {
            check(File.Exists(projectPath) && live is { IsPersistent: true, SavedGeneration: 1, HasChanges: false } && NdsProjectCodec.Read(File.ReadAllBytes(projectPath)) is { Ok: true } created && created.Document!.OperationCount == 0, "OpeningCreatesTheEmptyProjectOnDisk");
            foreach (var (name, make) in Scenarios())
            {
                NdsPreview preview;
                try { preview = make(live); } catch (Exception ex) when (NotApplicable(ex)) { Console.WriteLine($"   - {name}: no aplicable ({ex.Message.Split('\n')[0]})"); continue; }
                generation = live.SavedGeneration; string before = Fingerprint(live);
                live.Apply(preview); twin.Apply(make(twin)); applied++;
                durable &= live.SavedGeneration == generation + 1 && Fingerprint(live) != before;
                using var reader = OpenPersistent(rom, projectPath, romhack, readOnly: true);
                bool same = Fingerprint(reader) == Fingerprint(live) && Fingerprint(live) == Fingerprint(twin) && reader.History.Count == applied; identical &= same;
                if (!same) Console.WriteLine($"   ! {name}: el proyecto reabierto difiere");
            }
            check(applied >= minimum && durable, $"EveryConfirmedOperationIsANewDurableGeneration_{applied}Operations");
            check(identical, "AfterEveryOperationTheReopenedProjectAndAnUnsavedSessionHoldTheSameBytes");
            live.Export(Path.Combine(folder, "viva.nds")); twin.Export(Path.Combine(folder, "sin-proyecto.nds")); twinPrint = Fingerprint(twin); generation = live.SavedGeneration;
        }
        // The session is closed. A new one resumes the project from the disk and must export exactly the same ROM.
        using (var resumed = OpenPersistent(rom, projectPath, romhack))
        {
            resumedPrint = Fingerprint(resumed); resumed.Export(Path.Combine(folder, "reanudada.nds"));
            check(resumedPrint == twinPrint && resumed.SavedGeneration == generation && resumed.HasChanges && resumed.History.Count == applied, "AResumedSessionHasTheSameStateAndHistory");
            check(Hash(Path.Combine(folder, "reanudada.nds")) == Hash(Path.Combine(folder, "viva.nds")) && Hash(Path.Combine(folder, "viva.nds")) == Hash(Path.Combine(folder, "sin-proyecto.nds")), "TheRomExportedAfterResumingIsByteIdentical");
            // Work continues after resuming, and a stale preview is still refused without touching the project.
            var stale = Scenarios()[1].Make(resumed); resumed.Apply(Scenarios()[1].Make(resumed)); twin.Apply(Scenarios()[1].Make(twin));
            check(Throws(() => resumed.Apply(stale)) == nameof(InvalidOperationException) && resumed.SavedGeneration == generation + 1 && Fingerprint(resumed) == Fingerprint(twin), "WorkContinuesAfterResumingAndAStalePreviewChangesNothing");
            // Discarding is a generation too; the one before it stays in the backups.
            resumed.Reset();
            var backup = NdsProjectCodec.Read(File.ReadAllBytes(NdsProjectPaths.BackupOf(projectPath, 1)));
            check(resumed is { HasChanges: false, History.Count: 0 } && resumed.SavedGeneration == generation + 2 && NdsProjectCodec.Read(File.ReadAllBytes(projectPath)).Document is { OperationCount: 0, State.Files.Count: 0 }
                && backup.Ok && backup.Document!.Meta.Operations.Count == applied + 1, "DiscardingIsANewEmptyGenerationAndThePreviousOneStaysInTheBackups");
        }
        using (var after = OpenPersistent(rom, projectPath, romhack, readOnly: true)) check(!after.HasChanges && after.History.Count == 0, "TheDiscardedProjectReopensEmpty");
    }

    /// <summary>What the session does when the store fails: nothing confirmed is lost, nothing unconfirmed is kept.</summary>
    private static void Failures(string rom, Action<bool, string> check)
    {
        var scenarios = Scenarios(); var identity = NdsProjectRomIdentity.Compute(rom, true);
        using var twin = NdsProject.Open(rom, true); using var project = NdsProject.Open(rom, true); var medium = new NdsProjectMedium(); var store = new InMemoryNdsProjectStore(medium);
        project.AttachPersistence(store, identity, null, Writer);
        foreach (var (fault, expected) in new[] { (NdsProjectFault.FailBeforeWrite, NdsProjectStoreError.WriteFailed), (NdsProjectFault.CorruptWrite, NdsProjectStoreError.VerificationFailed) })
        {
            var preview = scenarios[0].Make(project); string before = Fingerprint(project); int revision = project.Revision; long saved = project.SavedGeneration; store.Fault = fault; NdsProjectStoreError? kind = null;
            try { project.Apply(preview); } catch (NdsProjectSaveException ex) { kind = ex.Kind; }
            bool untouched = kind == expected && project.LastSaveError == expected && Fingerprint(project) == before && project.Revision == revision && project.SavedGeneration == saved && project.History.Count == 0 && project.PersistenceFault is null;
            // The same preview is still valid (nothing changed) and can be confirmed again.
            project.Apply(preview); bool retried = project.SavedGeneration == saved + 1 && project.History.Count == 1; project.Reset();
            check(untouched && retried && project.SavedGeneration == saved + 2, $"NdsProjectTx_{fault}_TheOperationIsNotAppliedAndCanBeRetried");
        }
        twin.Apply(scenarios[0].Make(twin)); twin.Apply(scenarios[1].Make(twin));
        project.Apply(scenarios[0].Make(project)); string confirmed = Fingerprint(project);
        store.Fault = NdsProjectFault.FailAfterPublish; bool faulted = false;
        try { project.Apply(scenarios[1].Make(project)); } catch (NdsProjectFaultedException) { faulted = true; }
        bool blocked = faulted && project.PersistenceFault is not null && Fingerprint(project) == confirmed && Throws(() => project.Apply(scenarios[1].Make(project))) == nameof(NdsProjectFaultedException) && Throws(project.Reset) == nameof(NdsProjectFaultedException);
        check(blocked, "NdsProjectTx_UnknownOutcome_TheSessionKeepsTheConfirmedStateAndTakesNoMoreOperations");
        project.ReconcilePersistence();
        check(project.PersistenceFault is null && Fingerprint(project) == Fingerprint(twin) && project.History.Count == 2, "NdsProjectTx_Reconcile_InstallsWhatBecameDurable");
        twin.Apply(scenarios[2].Make(twin)); project.Apply(scenarios[2].Make(project));
        check(Fingerprint(project) == Fingerprint(twin), "NdsProjectTx_WorkContinuesAfterReconciling");
        // The file changes outside the session: the operation is refused, the session keeps its state and does not adopt the other content.
        string mine = Fingerprint(project); long generation = project.SavedGeneration; byte[] theirs = NdsProjectCodec.Serialize(new NdsProjectDocument(project.SavedDocument!.Meta with { Generation = generation + 4 }, new NdsProjectState())); medium.Main = theirs;
        NdsProjectStoreError? conflict = null; try { project.Apply(scenarios[1].Make(project)); } catch (NdsProjectSaveException ex) { conflict = ex.Kind; }
        check(conflict == NdsProjectStoreError.StaleGeneration && Fingerprint(project) == mine && project.SavedGeneration == generation && medium.Main.AsSpan().SequenceEqual(theirs) && project.PersistenceFault is null, "NdsProjectTx_ExternalChange_IsRefusedWithoutLosingTheSessionOrOverwritingTheFile");
    }

    private static void Refusals(string rom, string otherRom, string temp, Action<bool, string> check)
    {
        var identity = NdsProjectRomIdentity.Compute(rom, true); var other = NdsProjectRomIdentity.Compute(otherRom, true);
        NdsProjectDocument stored;
        using (var source = NdsProject.Open(rom, true)) { source.AttachPersistence(new InMemoryNdsProjectStore(new()), identity, null, Writer); source.Apply(Scenarios()[2].Make(source)); stored = source.SavedDocument!; }
        NdsProject Fresh(string path = "") => NdsProject.Open(path.Length == 0 ? rom : path, true);
        string? Attach(NdsProject project, NdsProjectRom rom, NdsProjectDocument? document) { using (project) { string empty = Fingerprint(project); string? error = Throws(() => project.AttachPersistence(new InMemoryNdsProjectStore(new()), rom, document, Writer)); return error is not null && (project.IsPersistent || Fingerprint(project) != empty) ? "dejó la sesión cambiada" : error; } }
        check(Attach(Fresh(), identity, stored) is null, "NdsProjectTx_Attach_ControlCaseIsAccepted");
        check(Attach(Fresh(), other, null) == nameof(InvalidDataException), "NdsProjectTx_Attach_AnIdentityOfAnotherGameIsRefused");
        check(Attach(Fresh(otherRom), other, stored) == nameof(InvalidDataException), "NdsProjectTx_Attach_AProjectOfAnotherGameIsRefused");
        check(Attach(Fresh(), identity with { Sha256 = new string('0', 64) }, stored) == nameof(InvalidDataException), "NdsProjectTx_Attach_AProjectOfAnotherDumpOfTheSameGameIsRefused");
        check(Attach(Fresh(), identity, new(stored.Meta with { Rom = stored.Meta.Rom with { IsRomhack = false } }, stored.State)) == nameof(InvalidDataException), "NdsProjectTx_Attach_ARomhackMismatchIsRefused");
        check(Attach(Fresh(), identity, new(stored.Meta, new NdsProjectState { Files = new(stored.State.Files) { ["a/9/9/9"] = [1, 2, 3] } })) == nameof(InvalidDataException), "NdsProjectTx_Attach_ATableThatIsNotInTheRomIsRefused");
        check(Attach(Fresh(), identity, new(stored.Meta with { RecompressOverlays = [9999] }, stored.State)) == nameof(InvalidDataException), "NdsProjectTx_Attach_AnOverlayThatIsNotInTheRomIsRefused");
        check(Attach(Fresh(), identity, new(stored.Meta with { Events = new([NdsEventOperation.OfMove(new(0, 0, new(1, 1, 0), new(2, 2, 0)))], ["x"]) }, stored.State)) == nameof(InvalidDataException) || identity.GameCode == "CPUE", "NdsProjectTx_Attach_EventsOnAGameWithoutEventSupportAreRefused");
        using (var changed = Fresh()) { changed.Apply(Scenarios()[0].Make(changed)); check(Throws(() => changed.AttachPersistence(new InMemoryNdsProjectStore(new()), identity, null, Writer)) == nameof(InvalidOperationException) && !changed.IsPersistent, "NdsProjectTx_Attach_ASessionWithChangesOfItsOwnIsRefused"); }
        using (var twice = Fresh()) { twice.AttachPersistence(new InMemoryNdsProjectStore(new()), identity, null, Writer); check(Throws(() => twice.AttachPersistence(new InMemoryNdsProjectStore(new()), identity, null, Writer)) == nameof(InvalidOperationException), "NdsProjectTx_Attach_ASecondProjectIsRefused"); }
        // Closing the session releases the project (its lock) so that it can be opened again.
        string folder = Directory.CreateDirectory(Path.Combine(temp, "cierre")).FullName, copy = Path.Combine(folder, "juego.nds"); File.Copy(rom, copy);
        using (var held = OpenPersistent(copy, copy + ".drproj", true)) check(Throws(() => NdsProjectFileStore.Open(copy + ".drproj").Dispose()) == nameof(NdsProjectStoreException), "NdsProjectTx_AnOpenSessionHoldsItsProject");
        using (var again = NdsProjectFileStore.Open(copy + ".drproj")) check(again.Load().Status == NdsProjectLoadStatus.Loaded, "NdsProjectTx_ClosingTheSessionReleasesTheProject");
    }

    /// <summary>Batch, Global Template and plain sessions are temporary: they must never create a project or a lock next to the ROM.</summary>
    private static void Temporary(string rom, string temp, Action<bool, string> check)
    {
        string folder = Directory.CreateDirectory(Path.Combine(temp, "temporales")).FullName, source = Path.Combine(folder, "juego.nds"), output = Directory.CreateDirectory(Path.Combine(temp, "lote")).FullName; File.Copy(rom, source);
        System.Text.Json.Nodes.JsonObject recipe; string expected;
        using (var session = NdsProject.Open(source, true))
        {
            foreach (int i in new[] { 0, 2, 3 }) session.Apply(Scenarios()[i].Make(session));
            recipe = session.CaptureGlobal(); expected = Fingerprint(session); session.Export(Path.Combine(output, "directa.nds"));
        }
        var batch = NdsProject.BuildBatch(source, true, recipe, 2, output, "lote", null, CancellationToken.None);
        check(batch.Count == 2 && Directory.GetFiles(folder).Select(Path.GetFileName).SequenceEqual(["juego.nds"]) && !Directory.GetFiles(output, "*.drproj*").Any(), "NdsProjectTx_BatchAndPlainSessionsCreateNoProjectFiles");
        // The same template applied to a persistent project: one generation per operation, and the same result as without persistence.
        string persistentFolder = Directory.CreateDirectory(Path.Combine(temp, "global")).FullName, copy = Path.Combine(persistentFolder, "juego.nds"); File.Copy(rom, copy);
        using (var project = OpenPersistent(copy, copy + ".drproj", true))
        {
            int count = project.ApplyGlobal(recipe, newSeeds: false); project.Export(Path.Combine(output, "persistente.nds"));
            check(count == 3 && project.SavedGeneration == 4 && Fingerprint(project) == expected && Hash(Path.Combine(output, "persistente.nds")) == Hash(Path.Combine(output, "directa.nds")), "NdsProjectTx_GlobalTemplateOnAPersistentProjectGivesTheSameRomOneGenerationPerOperation");
            check(project.CaptureGlobal().ToJsonString() == recipe.ToJsonString(), "NdsProjectTx_TheTemplateCapturedFromAPersistentProjectIsTheSame");
        }
        using (var resumed = OpenPersistent(copy, copy + ".drproj", true, readOnly: true)) check(Fingerprint(resumed) == expected && resumed.CaptureGlobal().ToJsonString() == recipe.ToJsonString(), "NdsProjectTx_TheTemplateSurvivesClosingAndReopening");
    }

    /// <summary>Event saves in a persistent project (synthetic event tables on a synthetic Platinum ROM; the probe uses the real ones).</summary>
    private static void Events(string temp, Action<bool, string> check)
    {
        string folder = Directory.CreateDirectory(Path.Combine(temp, "eventos")).FullName, rom = Path.Combine(folder, "platino.nds"), eventRom = Path.Combine(folder, "tablas-de-eventos.nds");
        File.WriteAllBytes(rom, NdsGen4Checks.SyntheticRom(Gen4Profile.All.Single(p => p.GameCode == "CPUE"), true)); var fixture = NdsEventChecks.Make(); File.WriteAllBytes(eventRom, fixture.Rom());
        using var tables = NdsRom.Open(eventRom); string project = rom + ".drproj"; var move = new PlatinumNpcValues(12, 13, 3);
        NdsProject Open(bool readOnly = false)
        {
            var opened = NdsProject.Open(rom, true); NdsProjectFileStore? store = null;
            try
            {
                opened.AdoptEventSession(new NdsEventSession(tables, fixture.Read())); store = readOnly ? NdsProjectFileStore.OpenReadOnly(project) : NdsProjectFileStore.Open(project);
                opened.AttachPersistence(store, NdsProjectRomIdentity.Compute(rom, true), store.Load().Document, Writer); return opened;
            }
            catch { if (!opened.IsPersistent) store?.Dispose(); opened.Dispose(); throw; }
        }
        using (var live = Open())
        {
            check(live.SupportsEventProfile, "NdsProjectTx_Events_TheSyntheticPlatinumRomHasTheEventProfile");
            var proposal = live.Events.Preview(0, 0, move); string before = Fingerprint(live);
            check(Fingerprint(live) == before && live.SavedGeneration == 1, "NdsProjectTx_Events_APreviewSavesNothing");
            live.SaveEvent(proposal);
            check(live.SavedGeneration == 2 && live.Events.Data.Npc(0, 0) is { X: 12, Y: 13, Direction: 3 } && live.Events.LastCheckpoint is null && !Directory.GetFiles(folder, "*.ndsevents.json").Any() && live.HasChanges
                && live.SavedDocument is { Meta.Events.Operations.Count: 1, State.EventData: not null, State.EventScripts: null } && live.SavedDocument.OperationCount == 1, "NdsProjectTx_Events_ASaveIsADurableGenerationWithoutACheckpointFile");
            using (var reader = Open(readOnly: true)) check(Fingerprint(reader) == Fingerprint(live) && reader.Events.Data.Bytes().AsSpan().SequenceEqual(live.Events.Data.Bytes()) && reader.Events.CanUndo, "NdsProjectTx_Events_TheReopenedProjectHasTheSameEventsAndHistory");
            // The stored history has the shape of a checkpoint: it exports as the same v1 document a checkpoint save would have written.
            var exported = live.SavedDocument!.Meta.Events!.ToCheckpoint(eventRom, new string('A', 64));
            check(exported is { Version: 1, Edits.Count: 1 } && exported.Edits[0] == new NdsNpcEdit(0, 0, new(2, 3, 0), move), "NdsProjectTx_Events_TheHistoryKeepsTheCheckpointShape");
            // A general operation after an event save keeps the events; an event save keeps the general tables.
            live.Apply(Scenarios()[2].Make(live)); live.SaveEvent(live.Events.Preview(0, 0, new(5, 6, 1)));
            using (var reader = Open(readOnly: true)) check(Fingerprint(reader) == Fingerprint(live) && reader.History.Count == 1 && reader.Events.History().Count == 2 && live.SavedGeneration == 4, "NdsProjectTx_Events_EventsAndGeneralOperationsInterleaveWithoutLosingEitherPart");
            // Undo is a generation; undoing everything stores the original event data again (no table).
            live.SaveEvent(live.Events.PreviewUndo()); live.SaveEvent(live.Events.PreviewUndo());
            check(live.SavedDocument is { State.EventData: null, Meta.Events.Operations.Count: 0 } && live.Events.Data.Bytes().AsSpan().SequenceEqual(fixture.Read().Bytes()) && live.SavedGeneration == 6, "NdsProjectTx_Events_UndoingEverythingStoresTheOriginalEventData");
            using (var reader = Open(readOnly: true)) check(Fingerprint(reader) == Fingerprint(live), "NdsProjectTx_Events_TheUndoneProjectReopensIdentically");
            live.SaveEvent(live.Events.Preview(0, 0, move));
        }
        // A stored history that does not replay to the stored bytes is refused, and nothing is installed.
        var stored = NdsProjectCodec.Read(File.ReadAllBytes(project)).Document!; var identity = NdsProjectRomIdentity.Compute(rom, true);
        foreach (var (label, tampered) in new (string, NdsProjectDocument)[]
        {
            ("EventDataThatTheHistoryDoesNotProduce", new(stored.Meta, new NdsProjectState { Files = stored.State.Files, Overlays = stored.State.Overlays, Arm9 = stored.State.Arm9, EventData = fixture.Read().Bytes() })),
            ("AHistoryThatDoesNotApply", new(stored.Meta with { Events = stored.Meta.Events! with { Operations = [NdsEventOperation.OfMove(new(0, 0, new(9, 9, 0), move))] } }, stored.State)),
            ("AnUnknownEventOperation", new(stored.Meta with { Events = stored.Meta.Events! with { Operations = [new NdsEventOperation("Teletransportar", null, null)] } }, stored.State)),
        })
        {
            using var target = NdsProject.Open(rom, true); target.AdoptEventSession(new NdsEventSession(tables, fixture.Read())); string empty = Fingerprint(target);
            check(Throws(() => target.AttachPersistence(new InMemoryNdsProjectStore(new()), identity, tampered, Writer)) == nameof(InvalidDataException) && !target.IsPersistent && Fingerprint(target) == empty, $"NdsProjectTx_Events_{label}_IsRefusedAndNothingIsInstalled");
        }
        // Two edits of the same script file never combine; of different files they do. The rule is the one the export uses, applied when confirming.
        var session = new NdsEventSession(tables, fixture.Read()); byte[] original = fixture.Scripts;
        byte[] Archive(params (int Index, byte[] Data)[] changes) { var narc = Narc.Read(original); foreach (var (index, data) in changes) narc.Files[index] = data; return narc.Write(); }
        check(Throws(() => session.RequireMergeable(Archive((0, [1, 2, 3])), Archive((0, [4, 5, 6])))) == nameof(InvalidOperationException) && Throws(() => session.RequireMergeable(Archive((0, [1, 2, 3])), Archive())) is null
            && Throws(() => session.RequireMergeable(Archive(), Archive((0, [4, 5, 6])))) is null, "NdsProjectTx_Events_TwoEditsOfTheSameScriptFileAreRefusedWhenConfirming");
    }
}
