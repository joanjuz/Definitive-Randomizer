using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Nds.Project;
using ThreeDS.Progressive.Services.ProjectDetection;

/// <summary>
/// Persistent DS project, PR 4: opening. A ROM opens with the project that belongs to it (created, or resumed); the project is recognised by the identity of
/// the ROM and never by a name; whatever is doubtful is refused without writing. Synthetic ROMs on the real file system.
/// </summary>
internal static class NdsProjectOpenChecks
{
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string[] Names(string folder) => Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
    private static string? Throws(Action action) { try { action(); return null; } catch (Exception ex) { return ex.GetType().Name; } }
    private static NdsProject Open(string rom, string? project = null, bool consent = false, bool recover = false, string? alternative = null, string? checkpoint = null) =>
        NdsProjectOpener.Open(new(rom, true, project, checkpoint, consent, recover, alternative));
    private static void Work(NdsProject project, params int[] scenarios) { foreach (int i in scenarios) project.Apply(NdsProjectTransactionChecks.Scenarios()[i].Make(project)); }

    internal static void Run(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-project-open-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            byte[] black = NdsGen5RandomizerChecks.SyntheticBlackRomhack(), diamond = NdsGen4Checks.SyntheticRom(Gen4Profile.All.First(p => p.GameCode == "ADAE"), true);
            string Folder(string name) => Directory.CreateDirectory(Path.Combine(temp, name)).FullName;
            string Rom(string folder, string name, byte[]? bytes = null) { string path = Path.Combine(folder, name); File.WriteAllBytes(path, bytes ?? black); return path; }
            Lifecycle(Folder("ciclo"), Rom, check); Relocation(temp, Folder, Rom, check); Refusals(Folder, Rom, diamond, check); Damage(Folder("daño"), Rom, check);
            ReadOnly(Folder, Rom, check); Detector(Folder, Rom, check); Engine(Folder("motor"), Rom, check); Checkpoints(Folder("checkpoints"), check);
        }
        finally { Directory.Delete(temp, true); }
    }

    private static void Lifecycle(string folder, Func<string, string, byte[]?, string> rom, Action<bool, string> check)
    {
        string source = rom(folder, "Negro.nds", null), project = source + ".drproj", before = Hash(source); string print;
        using (var first = Open(source))
        {
            check(first is { IsPersistent: true, SavedGeneration: 1, HasChanges: false } && Names(folder).SequenceEqual(new[] { "Negro.nds", "Negro.nds.drproj", "Negro.nds.drproj.lock" }) && Hash(source) == before, "NdsProjectOpen_ARomOpensWithANewProjectNextToItAndIsNotModified");
            check(first.SavedDocument!.Meta is { Rom.LastKnownPath: var known, Writer: var writer } && known == source && writer.StartsWith("Definitive Randomizer", StringComparison.Ordinal) && first.SavedDocument.Meta.Rom.Sha256 == before, "NdsProjectOpen_TheProjectRecordsTheIdentityOfTheRom");
            Work(first, 0, 2, 4); print = NdsProjectTransactionChecks.Fingerprint(first);
            // The same ROM cannot be opened twice: the second attempt is refused and the open session keeps working.
            string? second = null; try { Open(source).Dispose(); } catch (NdsProjectStoreException ex) { second = ex.Message; }
            Work(first, 1); print = NdsProjectTransactionChecks.Fingerprint(first);
            check(second is not null && second.Contains("ya está abierto en esta ventana", StringComparison.Ordinal) && first.SavedGeneration == 5, "NdsProjectOpen_TheSameProjectIsNotOpenedTwiceAndTheFirstSessionGoesOn");
        }
        check(Names(folder).All(n => !n.EndsWith(".lock", StringComparison.Ordinal)) && Hash(source) == before, "NdsProjectOpen_ClosingReleasesTheProjectAndLeavesTheRomIntact");
        using (var resumed = Open(source)) check(resumed is { SavedGeneration: 5, HasChanges: true } && resumed.History.Count == 4 && NdsProjectTransactionChecks.Fingerprint(resumed) == print, "NdsProjectOpen_OpeningTheRomAgainResumesTheProject");
        using (var direct = Open(source, project)) check(direct.SavedGeneration == 5 && NdsProjectTransactionChecks.Fingerprint(direct) == print, "NdsProjectOpen_OpeningTheProjectFileResumesTheSameWork");
        // The engine's own rule is unchanged: a romhack does not open without the romhack mode, with or without a project.
        check(Throws(() => NdsProjectOpener.Open(new(source, false)).Dispose()) == nameof(InvalidDataException) && Names(folder).All(n => !n.EndsWith(".lock", StringComparison.Ordinal)), "NdsProjectOpen_TheEngineStillDecidesWhetherTheRomOpens");
    }

    private static void Relocation(string temp, Func<string, string> folder, Func<string, string, byte[]?, string> rom, Action<bool, string> check)
    {
        // The ROM and its project are moved together to another folder under another name.
        string from = folder("origen"), to = folder("destino"), source = rom(from, "Juego.nds", null); string print;
        using (var project = Open(source)) { Work(project, 0, 2); print = NdsProjectTransactionChecks.Fingerprint(project); }
        File.Move(source, Path.Combine(to, "Renombrado.nds")); File.Move(source + ".drproj", Path.Combine(to, "Renombrado.nds.drproj")); foreach (string backup in Directory.GetFiles(from)) File.Move(backup, Path.Combine(to, Path.GetFileName(backup).Replace("Juego", "Renombrado")));
        string moved = Path.Combine(to, "Renombrado.nds");
        using (var project = Open(moved))
        {
            check(NdsProjectTransactionChecks.Fingerprint(project) == print && project.SavedGeneration == 3, "NdsProjectOpen_ARomMovedAndRenamedWithItsProjectResumes");
            Work(project, 1); print = NdsProjectTransactionChecks.Fingerprint(project);
            check(project.SavedDocument!.Meta.Rom.LastKnownPath == moved, "NdsProjectOpen_TheNextSaveRecordsWhereTheRomIsNow");
        }
        // Only the ROM is renamed; its project keeps the old name. It is recognised by the ROM it records, and no second project is created.
        string renamed = Path.Combine(to, "Otra vez.nds"); File.Move(moved, renamed);
        using (var project = Open(renamed))
            check(NdsProjectTransactionChecks.Fingerprint(project) == print && project.SavedGeneration == 4 && !File.Exists(renamed + ".drproj"), "NdsProjectOpen_ARenamedRomFindsItsProjectByContentAndNoSecondProjectIsCreated");
        // A copy of the same ROM next to the original, which still has its project: the copy gets its own.
        string twin = folder("gemelas"), original = rom(twin, "Original.nds", null), copy = Path.Combine(twin, "Copia.nds");
        using (var project = Open(original)) Work(project, 0);
        File.Copy(original, copy);
        using (var project = Open(copy)) check(project is { SavedGeneration: 1, HasChanges: false } && File.Exists(copy + ".drproj"), "NdsProjectOpen_ACopyOfARomThatStillHasItsProjectGetsItsOwn");
        using (var project = Open(original)) check(project.SavedGeneration == 2, "NdsProjectOpen_TheOriginalKeepsItsProject");
        // Two orphan projects that both record this ROM: nothing is chosen and nothing is created.
        string doubt = folder("dos-proyectos"), a = rom(doubt, "A.nds", null);
        using (var project = Open(a)) Work(project, 0);
        File.Copy(a + ".drproj", Path.Combine(doubt, "B.nds.drproj")); File.Move(a, Path.Combine(doubt, "C.nds")); var before = Names(doubt);
        check(Throws(() => Open(Path.Combine(doubt, "C.nds")).Dispose()) == nameof(InvalidDataException) && Names(doubt).SequenceEqual(before), "NdsProjectOpen_TwoProjectsForOneRomAreNotChosenBetween");
        using (var project = Open(Path.Combine(doubt, "C.nds"), Path.Combine(doubt, "B.nds.drproj"))) check(project.SavedGeneration == 2, "NdsProjectOpen_TheUserCanStillOpenTheProjectTheyWant");
    }

    private static void Refusals(Func<string, string> folder, Func<string, string, byte[]?, string> rom, byte[] other, Action<bool, string> check)
    {
        // Another ROM takes the name of a ROM that has a project: the project is not used with it and is not touched.
        string place = folder("otra-rom"), source = rom(place, "Juego.nds", null);
        using (var project = Open(source)) Work(project, 0, 2);
        byte[] stored = File.ReadAllBytes(source + ".drproj"); var files = Names(place);
        File.WriteAllBytes(source, other);
        check(Throws(() => Open(source).Dispose()) == nameof(InvalidDataException) && File.ReadAllBytes(source + ".drproj").AsSpan().SequenceEqual(stored) && Names(place).SequenceEqual(files), "NdsProjectOpen_AProjectIsNeverUsedWithAnotherGameAndIsLeftUntouched");
        // The same game with one byte changed (another dump, a patched copy).
        byte[] patched = NdsGen5RandomizerChecks.SyntheticBlackRomhack(); patched[^3] ^= 1; File.WriteAllBytes(source, patched);
        check(Throws(() => Open(source).Dispose()) == nameof(InvalidDataException) && File.ReadAllBytes(source + ".drproj").AsSpan().SequenceEqual(stored) && Names(place).SequenceEqual(files), "NdsProjectOpen_AProjectIsNeverUsedWithAnotherDumpOfTheSameGame");
        // A project written by a newer build.
        string future = folder("futuro"), futureRom = rom(future, "Juego.nds", null);
        using (var project = Open(futureRom)) Work(project, 0);
        var document = NdsProjectCodec.Read(File.ReadAllBytes(futureRom + ".drproj")).Document!; byte[] newer = NdsProjectCodec.Serialize(new(document.Meta with { MinReaderVersion = 2 }, document.State)); File.WriteAllBytes(futureRom + ".drproj", newer);
        check(Throws(() => Open(futureRom).Dispose()) == nameof(InvalidDataException) && File.ReadAllBytes(futureRom + ".drproj").AsSpan().SequenceEqual(newer) && !Names(future).Any(n => n.Contains("damaged", StringComparison.Ordinal)), "NdsProjectOpen_AProjectOfANewerVersionIsNotOpenedNorModified");
    }

    private static void Damage(string folder, Func<string, string, byte[]?, string> rom, Action<bool, string> check)
    {
        string source = rom(folder, "Juego.nds", null), project = source + ".drproj"; string threeOperations;
        using (var session = Open(source)) { Work(session, 0, 2, 4); threeOperations = NdsProjectTransactionChecks.Fingerprint(session); Work(session, 1); }
        byte[] damaged = File.ReadAllBytes(project); damaged[NdsProjectCodec.HeaderSize + 40] ^= 0x55; File.WriteAllBytes(project, damaged); var files = Names(folder);
        NdsProjectDamagedException? offered = null; try { Open(source).Dispose(); } catch (NdsProjectDamagedException ex) { offered = ex; }
        check(offered is { Plan.Recommended.Generation: 4 } && offered.Describe().Contains("Se perderían las 1 últimas operaciones", StringComparison.Ordinal) && File.ReadAllBytes(project).AsSpan().SequenceEqual(damaged) && Names(folder).SequenceEqual(files), "NdsProjectOpen_ADamagedProjectIsNotReplacedWithoutAskingAndSaysWhatWouldBeLost");
        using (var recovered = Open(source, recover: true))
        {
            string[] preserved = Directory.GetFiles(folder, "*.damaged-*");
            check(recovered.SavedGeneration == 4 && recovered.History.Count == 3 && NdsProjectTransactionChecks.Fingerprint(recovered) == threeOperations && preserved.Length == 1 && File.ReadAllBytes(preserved[0]).AsSpan().SequenceEqual(damaged), "NdsProjectOpen_AcceptedRecoveryInstallsTheBackupAndKeepsTheDamagedFile");
            Work(recovered, 1); check(recovered.SavedGeneration == 5, "NdsProjectOpen_WorkContinuesAfterRecovering");
        }
        // No valid copy at all: nothing is offered, nothing is touched, and no new project silently takes its place.
        foreach (string file in Directory.GetFiles(folder, "*.drproj*")) File.WriteAllBytes(file, [1, 2, 3]); files = Names(folder);
        NdsProjectDamagedException? none = null; try { Open(source, recover: true).Dispose(); } catch (NdsProjectDamagedException ex) { none = ex; }
        check(none is { Plan.Recommended: null } && none.Describe().Contains("mueve o renombra", StringComparison.Ordinal) && Names(folder).SequenceEqual(files) && File.ReadAllBytes(project).Length == 3, "NdsProjectOpen_ADamagedProjectWithoutCopiesIsNeverOverwrittenByANewOne");
    }

    private static void WithoutWriteAccess(string folder, Action action)
    {
        var info = new DirectoryInfo(folder); var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, AccessControlType.Deny);
        var security = info.GetAccessControl(); security.AddAccessRule(rule); info.SetAccessControl(security);
        try { action(); }
        finally { security = info.GetAccessControl(); security.RemoveAccessRule(rule); info.SetAccessControl(security); }
    }

    private static void ReadOnly(Func<string, string> folder, Func<string, string, byte[]?, string> rom, Action<bool, string> check)
    {
        string place = folder("sin-permiso"), source = rom(place, "Juego.nds", null), alternative = Path.Combine(folder("alternativa"), "proyectos");
        WithoutWriteAccess(place, () =>
        {
            NdsProjectNeedsConsentException? asked = null; try { Open(source, alternative: alternative).Dispose(); } catch (NdsProjectNeedsConsentException ex) { asked = ex; }
            check(asked is not null && asked.AlternativePath.StartsWith(alternative, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(alternative) && Names(place).SequenceEqual(new[] { "Juego.nds" }), "NdsProjectOpen_AFolderThatCannotBeWrittenNeedsConsentAndNothingIsCreatedBeforeIt");
            string print;
            using (var project = Open(source, consent: true, alternative: alternative))
            {
                Work(project, 0, 2); print = NdsProjectTransactionChecks.Fingerprint(project);
                check(project.SavedGeneration == 3 && File.Exists(asked!.AlternativePath) && Names(place).SequenceEqual(new[] { "Juego.nds" }), "NdsProjectOpen_WithConsentTheProjectLivesInTheAlternativePlaceAndTheRomFolderStaysUntouched");
            }
            using (var project = Open(source, alternative: alternative)) check(NdsProjectTransactionChecks.Fingerprint(project) == print, "NdsProjectOpen_TheAlternativeProjectIsResumedWithoutAskingAgain");
        });
        // The same ROM somewhere writable while a project of it exists in the alternative place: two projects would claim it, so nothing is chosen.
        string elsewhere = folder("con-alternativa"), elsewhereRom = rom(elsewhere, "Juego.nds", null);
        using (var project = Open(elsewhereRom, alternative: alternative)) check(project.SavedGeneration == 3 && !File.Exists(elsewhereRom + ".drproj"), "NdsProjectOpen_AnAlternativeProjectIsUsedWhereverTheRomIsAndNoSecondOneIsCreated");
        // A project that already exists in a folder that later stops being writable opens to be read; operations are refused, not lost silently.
        string locked = folder("solo-lectura"), lockedRom = rom(locked, "Juego.nds", null); string saved;
        using (var project = Open(lockedRom)) { Work(project, 0); saved = NdsProjectTransactionChecks.Fingerprint(project); }
        var lockedFiles = Names(locked); byte[] lockedProject = File.ReadAllBytes(lockedRom + ".drproj");
        WithoutWriteAccess(locked, () =>
        {
            // (Another alternative root: the one above already holds a project of this same synthetic ROM, and two projects for one ROM are refused.)
            using var project = Open(lockedRom, alternative: Path.Combine(folder("alternativa-2"), "proyectos")); NdsProjectStoreError? refused = null;
            try { Work(project, 2); } catch (NdsProjectSaveException ex) { refused = ex.Kind; }
            check(NdsProjectTransactionChecks.Fingerprint(project) == saved && refused == NdsProjectStoreError.ReadOnly && project.History.Count == 1 && Names(locked).SequenceEqual(lockedFiles) && File.ReadAllBytes(lockedRom + ".drproj").AsSpan().SequenceEqual(lockedProject), "NdsProjectOpen_AProjectInAReadOnlyFolderOpensToBeReadAndRefusesOperations");
        });
        // The same folder with the main file damaged: a valid copy exists, but it cannot be installed where nothing can be written. Say so at once (no
        // recovery is offered that would then fail the same way), and touch nothing.
        File.WriteAllBytes(lockedRom + ".drproj", lockedProject[..(lockedProject.Length / 2)]); var damagedFiles = Names(locked);
        WithoutWriteAccess(locked, () =>
        {
            Exception? refusal = null; try { Open(lockedRom, recover: true, alternative: Path.Combine(folder("alternativa-3"), "proyectos")).Dispose(); } catch (Exception ex) { refusal = ex; }
            check(refusal is IOException and not NdsProjectDamagedException && refusal.Message.Contains("no admite escritura", StringComparison.Ordinal) && Names(locked).SequenceEqual(damagedFiles)
                && File.ReadAllBytes(lockedRom + ".drproj").Length == lockedProject.Length / 2, "NdsProjectOpen_ADamagedProjectInAReadOnlyFolderIsNotOfferedARecoveryThatCannotBeWritten");
        });
    }

    private static void Detector(Func<string, string> folder, Func<string, string, byte[]?, string> rom, Action<bool, string> check)
    {
        string place = folder("detector"), source = rom(place, "Juego.nds", null), project = source + ".drproj";
        using (var session = Open(source)) Work(session, 0, 2);
        var found = ProjectDetector.Detect(project);
        check(found is { IsCandidate: true, Platform: DetectedPlatform.NintendoDS, Kind: DetectedKind.DsProject } && found.SourcePath == source && found.Game.Contains("Negro", StringComparison.Ordinal) && found.Evidence.Any(e => e.Contains("generación 3", StringComparison.Ordinal)), "NdsProjectDetector_AProjectIsACandidateWithItsRom");
        string renamed = Path.Combine(place, "sin-extension-conocida.bin"); File.Copy(project, renamed);
        check(ProjectDetector.Detect(renamed) is { IsCandidate: true, Kind: DetectedKind.DsProject }, "NdsProjectDetector_AProjectIsRecognisedByItsContentNotItsName");
        byte[] bytes = File.ReadAllBytes(project); string torn = Path.Combine(place, "cortado.drproj"); File.WriteAllBytes(torn, bytes[..(bytes.Length / 2)]);
        check(ProjectDetector.Detect(torn) is { IsCandidate: false, Status: DetectionStatus.Incomplete, Kind: DetectedKind.DsProject }, "NdsProjectDetector_ATruncatedProjectIsIncomplete");
        var document = NdsProjectCodec.Read(bytes).Document!; string newer = Path.Combine(place, "nuevo.drproj"); File.WriteAllBytes(newer, NdsProjectCodec.Serialize(new(document.Meta with { MinReaderVersion = 2 }, document.State)));
        check(ProjectDetector.Detect(newer) is { IsCandidate: false, Status: DetectionStatus.Unsupported, Kind: DetectedKind.DsProject }, "NdsProjectDetector_AProjectOfANewerVersionIsUnsupported");
        // The ROM is not where it was last used: it is looked for next to the project (by game, revision and size); the engine compares the SHA-256.
        string away = folder("detector-movido"), movedProject = Path.Combine(away, "Trabajo.drproj"); File.Copy(project, movedProject);
        check(ProjectDetector.Detect(movedProject) is { IsCandidate: true } still && still.SourcePath == source, "NdsProjectDetector_TheRecordedRomPathIsTriedFirst");
        File.Move(source, Path.Combine(away, "Cualquier nombre.nds"));
        var relocated = ProjectDetector.Detect(movedProject);
        check(relocated.IsCandidate && relocated.SourcePath == Path.Combine(away, "Cualquier nombre.nds"), "NdsProjectDetector_ARelocatedRomIsFoundNextToTheProject");
        using (var session = NdsProjectOpener.Open(new(relocated.SourcePath!, true, movedProject))) check(session.SavedGeneration == 3, "NdsProjectDetector_TheDetectedProjectOpensWithTheRelocatedRom");
        File.Delete(Path.Combine(away, "Cualquier nombre.nds"));
        check(ProjectDetector.Detect(movedProject) is { IsCandidate: false, Status: DetectionStatus.Incomplete } missing && missing.Message.Contains("Abre directamente la ROM", StringComparison.Ordinal), "NdsProjectDetector_AProjectWithoutItsRomIsIncompleteAndSaysWhatToDo");
        // A ROM of the same game and size that is NOT the recorded one: the detector offers it, the engine refuses it (the full SHA-256 decides).
        byte[] other = NdsGen5RandomizerChecks.SyntheticBlackRomhack(); other[^3] ^= 1; File.WriteAllBytes(Path.Combine(away, "Parecida.nds"), other); var lookalike = ProjectDetector.Detect(movedProject); byte[] untouched = File.ReadAllBytes(movedProject);
        check(lookalike.IsCandidate && Throws(() => NdsProjectOpener.Open(new(lookalike.SourcePath!, true, movedProject)).Dispose()) == nameof(InvalidDataException) && File.ReadAllBytes(movedProject).AsSpan().SequenceEqual(untouched), "NdsProjectDetector_ALookalikeRomIsRefusedByTheEngineAndTheProjectIsUntouched");
    }

    /// <summary>The engine entry of the universal dialog: the three kinds of DS entry end in the same persistent session.</summary>
    private static void Engine(string folder, Func<string, string, byte[]?, string> rom, Action<bool, string> check)
    {
        string source = rom(folder, "Juego.nds", null); var options = new OpenOptions(AllowRomhacks: true); string print;
        using (var prepared = ProjectOpenCoordinator.PrepareWithEngine(ProjectDetector.Detect(source), options))
        { var project = (NdsProject)prepared.Project; Work(project, 0, 2); print = NdsProjectTransactionChecks.Fingerprint(project); check(project.IsPersistent && prepared.Platform == DetectedPlatform.NintendoDS, "NdsProjectOpen_TheUniversalEngineOpensARomWithItsProject"); }
        check(!Directory.GetFiles(folder, "*.lock").Any(), "NdsProjectOpen_APreparedProjectThatIsNotInstalledReleasesItsProject");
        using (var prepared = ProjectOpenCoordinator.PrepareWithEngine(ProjectDetector.Detect(source + ".drproj"), options))
            check(NdsProjectTransactionChecks.Fingerprint((NdsProject)prepared.Project) == print, "NdsProjectOpen_TheUniversalEngineOpensAProjectFileWithTheSameWork");
        // The dialog offers "Admitir romhacks" only for ROMs: a project made on a romhack reopens in that mode by itself, and the ROM alone still needs the option.
        using (var prepared = ProjectOpenCoordinator.PrepareWithEngine(ProjectDetector.Detect(source + ".drproj"), new OpenOptions()))
            check(prepared.Project is NdsProject { IsRomhack: true } reopened && NdsProjectTransactionChecks.Fingerprint(reopened) == print, "NdsProjectOpen_AProjectMadeOnARomhackReopensInRomhackModeByItself");
        check(Throws(() => ProjectOpenCoordinator.PrepareWithEngine(ProjectDetector.Detect(source), new OpenOptions()).Dispose()) == nameof(InvalidDataException), "NdsProjectOpen_TheRomAloneStillNeedsTheRomhackOption");
    }

    /// <summary>Importing a checkpoint into the project of its ROM (synthetic event tables; the real ones are in the probe).</summary>
    private static void Checkpoints(string folder, Action<bool, string> check)
    {
        string rom = Path.Combine(folder, "platino.nds"), eventRom = Path.Combine(folder, "tablas.nds"); File.WriteAllBytes(rom, NdsGen4Checks.SyntheticRom(Gen4Profile.All.Single(p => p.GameCode == "CPUE"), true));
        var fixture = NdsEventChecks.Make(); File.WriteAllBytes(eventRom, fixture.Rom()); using var tables = NdsRom.Open(eventRom); var move = new PlatinumNpcValues(12, 13, 3);
        // An old session, without a project, saved two checkpoints.
        string first = Path.Combine(folder, "uno.ndsevents.json"), second = Path.Combine(folder, "dos.ndsevents.json");
        var old = new NdsEventSession(tables, fixture.Read()); old.Save(old.Preview(0, 0, move), first); old.Save(old.Preview(0, 0, new(5, 6, 1)), second);
        byte[] firstBytes = File.ReadAllBytes(first), secondBytes = File.ReadAllBytes(second);
        NdsProject Persistent()
        {
            var project = NdsProject.Open(rom, true); var store = NdsProjectFileStore.Open(rom + ".drproj");
            try { project.AdoptEventSession(new NdsEventSession(tables, fixture.Read())); project.AttachPersistence(store, NdsProjectRomIdentity.Compute(rom, true), store.Load().Document, "comprobaciones"); return project; }
            catch { if (!project.IsPersistent) store.Dispose(); project.Dispose(); throw; }
        }
        using (var project = Persistent())
        {
            project.ImportEventCheckpoint(NdsEventSession.ReadDocument(second));
            check(project.SavedGeneration == 2 && project.Events.Data.Npc(0, 0) is { X: 5, Y: 6, Direction: 1 } && project.SavedDocument!.Meta.Events is { ImportedFrom: 1, Operations.Count: 2 } && project.Events.CanUndo
                && File.ReadAllBytes(second).AsSpan().SequenceEqual(secondBytes) && File.ReadAllBytes(first).AsSpan().SequenceEqual(firstBytes), "NdsProjectOpen_ACheckpointIsImportedIntoTheProjectAndTheFileIsNotAltered");
            project.ImportEventCheckpoint(NdsEventSession.ReadDocument(second));
            check(project.SavedGeneration == 2, "NdsProjectOpen_ImportingTheSameCheckpointAgainChangesNothing");
            check(Throws(() => project.ImportEventCheckpoint(NdsEventSession.ReadDocument(first))) == nameof(InvalidOperationException) && project.SavedGeneration == 2 && project.Events.Data.Npc(0, 0).X == 5, "NdsProjectOpen_ACheckpointWithAnotherHistoryIsNotMergedIntoTheProject");
            // The imported history exports again as the same checkpoint document.
            var exported = project.SavedDocument!.Meta.Events!.ToCheckpoint(eventRom, NdsEventSession.ReadDocument(second).SourceSha256);
            check(JsonSerializer.Serialize(exported) == JsonSerializer.Serialize(NdsEventSession.ReadDocument(second)), "NdsProjectOpen_TheImportedHistoryExportsAsTheSameCheckpoint");
        }
        using (var resumed = Persistent()) check(resumed.Events.Data.Npc(0, 0) is { X: 5, Y: 6 } && resumed.Events.History().Count == 2, "NdsProjectOpen_TheImportedHistoryIsThereAfterReopening");
    }
}
