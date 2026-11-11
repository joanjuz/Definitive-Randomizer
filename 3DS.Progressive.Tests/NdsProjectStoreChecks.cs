using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Services.Nds.Project;

/// <summary>
/// Persistent DS project, PR 2: the store on disk. Every check here uses the REAL file system of the folder it is given (a new temporary folder by default):
/// real files, real renames, real locks, real access rules, and real child processes that are terminated in the middle of a save. What it cannot do is cut
/// the power: that a flushed file survives a power failure is the operating system's guarantee, not something measured here.
/// </summary>
internal static class NdsProjectStoreChecks
{
    private static readonly DateTime T0 = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
    private const string Name = "Juego.nds.drproj";

    /// <summary>The project every scenario builds: generation g is always the same document, in this process and in the child processes.</summary>
    internal static NdsProjectDocument Chain(int generation)
    {
        var doc = NdsProjectFormatChecks.Doc(generation: 1);
        for (int i = 2; i <= generation; i++) doc = NdsProjectFormatChecks.Next(doc, i);
        return doc;
    }

    private static NdsProjectStoreError? Fails(Action action) => NdsProjectFormatChecks.Fails(action);
    private static string Folder(string root, string name) => Directory.CreateDirectory(Path.Combine(root, name)).FullName;
    private static string[] Names(string folder) => Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
    private static bool NoTemps(string folder) => !Names(folder).Any(n => n.Contains(".tmp-", StringComparison.Ordinal));
    private static long[] BackupGenerations(string project) => Enumerable.Range(1, 3).Select(n => NdsProjectPaths.BackupOf(project, n)).Where(File.Exists)
        .Select(p => NdsProjectCodec.Read(File.ReadAllBytes(p)) is { Ok: true } r ? r.Document!.Generation : -1).ToArray();

    /// <summary>A folder with a project at the given generation (committed one by one, like a session would).</summary>
    private static string Populated(string root, string name, int generation)
    {
        string project = Path.Combine(Folder(root, name), Name);
        using var store = NdsProjectFileStore.Open(project);
        for (int g = 1; g <= generation; g++) store.Commit(Chain(g), g - 1);
        return project;
    }

    internal static void Run(Action<bool, string> check, string? folder = null)
    {
        string root = folder ?? Path.Combine(Path.GetTempPath(), "nds-project-store-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Usa una carpeta nueva.");
        Directory.CreateDirectory(root);
        try
        {
            Contract(check, root); Faults(check, root); RealFailures(check, root); External(check, root); Recovery(check, root); ReadOnlyFolder(check, root); Locator(check, root);
            Interrupted(check, root); Instances(check, root); Race(check, root); Transactions(check, root); Timing(check, root);
        }
        finally { try { Directory.Delete(root, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine("   (no se pudo borrar la carpeta de pruebas: " + ex.Message + ")"); } }
    }

    // ----------------------------------------------------------------------------------------------------------------------------- contract

    private static void Contract(Action<bool, string> check, string root)
    {
        string folder = Folder(root, "contrato"), project = Path.Combine(folder, Name);
        using (var store = NdsProjectFileStore.Open(project))
        {
            check(store.Load().Status == NdsProjectLoadStatus.Empty && Names(folder).SequenceEqual([Name + ".lock"]), "NdsFileStore_NewProjectIsEmptyAndOnlyTheLockExists");
            var first = Chain(1);
            check(store.Commit(first, 0) == 1 && store.Load() is { Status: NdsProjectLoadStatus.Loaded } l && NdsProjectFormatChecks.SameDoc(first, l.Document!) && File.ReadAllBytes(project).AsSpan().SequenceEqual(NdsProjectCodec.Serialize(first)), "NdsFileStore_CommitWritesExactlyTheSerializedProject");
            check(Fails(() => store.Commit(first, 0)) == NdsProjectStoreError.StaleGeneration && Fails(() => store.Commit(first, 1)) == NdsProjectStoreError.WriteFailed, "NdsFileStore_CommitIsCompareAndSwapOnTheGeneration");
            for (int g = 2; g <= 7; g++) store.Commit(Chain(g), g - 1);
            check(BackupGenerations(project).SequenceEqual([6L, 5, 4]) && store.Load().Document!.Generation == 7, "NdsFileStore_KeepsTheThreeGenerationsBeforeTheCurrentOne");
            check(Names(folder).SequenceEqual(new[] { Name, Name + ".bak1", Name + ".bak2", Name + ".bak3", Name + ".lock" }), "NdsFileStore_LeavesNoOtherFileInTheFolder");
        }
        check(Names(folder).SequenceEqual(new[] { Name, Name + ".bak1", Name + ".bak2", Name + ".bak3" }), "NdsFileStore_TheLockDisappearsWhenTheProjectIsClosed");
        var closed = NdsProjectFileStore.Open(project); closed.Dispose();
        check(Fails(() => closed.Load()) == NdsProjectStoreError.Disposed && Fails(() => closed.Commit(Chain(8), 7)) == NdsProjectStoreError.Disposed, "NdsFileStore_AClosedStoreRefusesEverything");
        using (var again = NdsProjectFileStore.Open(project))
            check(again.Load() is { Status: NdsProjectLoadStatus.Loaded } l && NdsProjectFormatChecks.SameDoc(Chain(7), l.Document!) && again.Commit(Chain(8), 7) == 8, "NdsFileStore_AReopenedProjectIsTheSameAndContinues");
        // A store that never loaded (the session already has the document) still commits by generation.
        using (var blind = NdsProjectFileStore.Open(project)) check(blind.Commit(Chain(9), 8) == 9 && Fails(() => blind.Commit(Chain(9), 8)) == NdsProjectStoreError.StaleGeneration, "NdsFileStore_CommitWithoutLoadingStillComparesTheGeneration");
        check(Fails(() => NdsProjectFileStore.Open(Path.Combine(root, "no-existe", Name)).Dispose()) == NdsProjectStoreError.WriteFailed, "NdsFileStore_AMissingFolderIsReportedNotCreated");
        check(!Directory.Exists(Path.Combine(root, "no-existe")), "NdsFileStore_OpeningNeverCreatesAFolderUnlessAsked");
    }

    // ------------------------------------------------------------------------------------------------------------------------ injected faults

    private static void Faults(Action<bool, string> check, string root)
    {
        string project = Populated(root, "fallos", 3), folder = Path.GetDirectoryName(project)!; byte[] before = File.ReadAllBytes(project);
        using var store = NdsProjectFileStore.Open(project); store.Load();
        foreach (var step in new[] { NdsProjectFileStep.BeforeTemp, NdsProjectFileStep.TempWritten, NdsProjectFileStep.TempVerified, NdsProjectFileStep.BackupWritten, NdsProjectFileStep.BackupsRotated, NdsProjectFileStep.BeforeReplace })
        {
            store.Probe = s => { if (s == step) throw new IOException("fallo de disco simulado en " + step); };
            var error = Fails(() => store.Commit(Chain(4), 3)); store.Probe = null;
            check(error == NdsProjectStoreError.WriteFailed && File.ReadAllBytes(project).AsSpan().SequenceEqual(before) && NoTemps(folder) && store.Load().Document!.Generation == 3, $"NdsFileStore_FailureAt{step}_LeavesThePreviousProjectAndNoTemporaryFile");
        }
        // The failed saves got as far as making the backup: they must not have cost a generation of backups.
        check(BackupGenerations(project).Distinct().Count() == 3 && BackupGenerations(project).All(g => g is >= 1 and <= 3), "NdsFileStore_FailedSavesDoNotConsumeBackups");
        string Temp() => Directory.GetFiles(folder).Single(n => n.Contains(".tmp-", StringComparison.Ordinal) && !n.EndsWith(".bak", StringComparison.Ordinal));
        var torn = new (string Name, Action Damage)[]
        {
            ("AFlippedByte", () => { var b = File.ReadAllBytes(Temp()); b[b.Length / 2] ^= 0x5A; File.WriteAllBytes(Temp(), b); }),
            ("ATruncatedFile", () => { var b = File.ReadAllBytes(Temp()); File.WriteAllBytes(Temp(), b[..(b.Length / 2)]); }),
            ("AnEmptyFile", () => File.WriteAllBytes(Temp(), [])),
            ("AMissingFile", () => File.Delete(Temp())),
        };
        foreach (var (name, damage) in torn)
        {
            store.Probe = s => { if (s == NdsProjectFileStep.TempWritten) damage(); };
            var error = Fails(() => store.Commit(Chain(4), 3)); store.Probe = null;
            check(error == NdsProjectStoreError.VerificationFailed && File.ReadAllBytes(project).AsSpan().SequenceEqual(before) && NoTemps(folder), $"NdsFileStore_{name}WhereTheSaveWasWrittenIsCaughtByReadingItBack");
        }
        store.Probe = s => { if (s == NdsProjectFileStep.BeforeReplace) File.Delete(Temp()); };
        var lost = Fails(() => store.Commit(Chain(4), 3)); store.Probe = null;
        check(lost == NdsProjectStoreError.WriteFailed && File.ReadAllBytes(project).AsSpan().SequenceEqual(before), "NdsFileStore_AReplacementThatCannotHappenIsADefiniteFailure");
        check(store.Commit(Chain(4), 3) == 4 && BackupGenerations(project).SequenceEqual([3L, 2, 1]), "NdsFileStore_ASaveAfterTheFailuresSucceedsWithDistinctBackups");
        // After the replacement the answer is no longer "nothing changed": the store says it does not know, and the project is in fact the new one.
        store.Probe = s => { if (s == NdsProjectFileStep.AfterReplace) throw new IOException("sin respuesta"); };
        var unknown = Fails(() => store.Commit(Chain(5), 4)); store.Probe = null;
        check(unknown == NdsProjectStoreError.OutcomeUnknown && store.Load().Document!.Generation == 5 && store.Commit(Chain(6), 5) == 6, "NdsFileStore_AFailureAfterTheReplacementIsAnUnknownOutcome");
        // An abandoned temporary file (a process that died) is removed by the next save.
        File.WriteAllBytes(NdsProjectPaths.TempOf(project, "abandonado"), [1, 2, 3]); File.WriteAllBytes(Path.Combine(folder, "Otro.nds.drproj.tmp-ajeno"), [4]);
        store.Commit(Chain(7), 6);
        check(NoTemps(folder) == false && Names(folder).Count(n => n.Contains(".tmp-", StringComparison.Ordinal)) == 1 && File.Exists(Path.Combine(folder, "Otro.nds.drproj.tmp-ajeno")), "NdsFileStore_AbandonedTemporariesOfThisProjectAreRemovedAndOthersAreLeftAlone");
    }

    // -------------------------------------------------------------------------------------------------------------- real file system failures

    private static void RealFailures(Action<bool, string> check, string root)
    {
        string project = Populated(root, "reales", 3); byte[] before = File.ReadAllBytes(project);
        using var store = NdsProjectFileStore.Open(project); store.Load();
        // Another program keeps the project open for reading and does not allow it to be renamed or replaced (what a backup or synchronization tool can do).
        using (var reader = new FileStream(project, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Fails(() => store.Commit(Chain(4), 3));
            check(error == NdsProjectStoreError.WriteFailed && File.ReadAllBytes(project).AsSpan().SequenceEqual(before) && store.Load().Document!.Generation == 3 && NoTemps(Path.GetDirectoryName(project)!), "NdsFileStore_AProjectHeldByAnotherProgramIsNotReplacedAndStaysIntact");
        }
        check(store.Commit(Chain(4), 3) == 4, "NdsFileStore_TheSaveSucceedsOnceTheOtherProgramLetsGo");
        // Another program holds it exclusively: it cannot even be read. That is an input/output failure, not a damaged project.
        using (var exclusive = new FileStream(project, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(Fails(() => store.Load()) == NdsProjectStoreError.Unreadable && Fails(() => store.Commit(Chain(5), 4)) == NdsProjectStoreError.WriteFailed, "NdsFileStore_AnUnreadableProjectIsNotCalledDamaged");
        check(store.Load().Document!.Generation == 4, "NdsFileStore_ReadableAgainAfterwards");
        // The file is marked read-only.
        File.SetAttributes(project, File.GetAttributes(project) | FileAttributes.ReadOnly);
        try
        {
            var error = Fails(() => store.Commit(Chain(5), 4));
            check(error == NdsProjectStoreError.ReadOnly && store.Load().Document!.Generation == 4, "NdsFileStore_AReadOnlyProjectFileIsNotReplacedAndIsReportedAsReadOnly");
        }
        finally { File.SetAttributes(project, File.GetAttributes(project) & ~FileAttributes.ReadOnly); }
        check(store.Commit(Chain(5), 4) == 5 && BackupGenerations(project).SequenceEqual([4L, 3, 2]), "NdsFileStore_ContinuesAfterTheAttributeIsCleared");
        // A file larger than any project is refused by its size, without being read into memory.
        string huge = Path.Combine(Folder(root, "enorme"), Name);
        try
        {
            using (var big = new FileStream(huge, FileMode.CreateNew)) big.SetLength(NdsProjectCodec.MaxTotalBytes + 1);
            long memory = GC.GetTotalMemory(true);
            using var bigStore = NdsProjectFileStore.Open(huge); var load = bigStore.Load();
            check(load.Status == NdsProjectLoadStatus.Damaged && GC.GetTotalMemory(false) - memory < 64 * 1024 * 1024 && Fails(() => bigStore.Commit(Chain(1), 0)) == NdsProjectStoreError.MainDamaged, "NdsFileStore_AnOversizedFileIsRefusedWithoutReadingIt");
        }
        catch (IOException ex) when (!File.Exists(huge) || new FileInfo(huge).Length == 0) { Console.WriteLine("   (sin espacio para la prueba de archivo enorme: " + ex.Message + ")"); check(true, "NdsFileStore_AnOversizedFileIsRefusedWithoutReadingIt [omitida: sin espacio en disco]"); }
        finally { File.Delete(huge); }
    }

    // ------------------------------------------------------------------------------------------------------------------ external modification

    private static void External(Action<bool, string> check, string root)
    {
        static NdsProjectDocument Other(NdsProjectDocument d) => new(d.Meta, new NdsProjectState { Files = { ["a/0/9/9"] = [7, 7, 7] } });
        // Same project, same generation, other content: only the bytes tell.
        string project = Populated(root, "externo-contenido", 4);
        using (var store = NdsProjectFileStore.Open(project))
        {
            store.Load(); byte[] theirs = NdsProjectCodec.Serialize(Other(Chain(4))); File.WriteAllBytes(project, theirs);
            check(Fails(() => store.Commit(Chain(5), 4)) == NdsProjectStoreError.StaleGeneration && File.ReadAllBytes(project).AsSpan().SequenceEqual(theirs), "NdsFileStore_ARewriteWithTheSameGenerationIsDetectedByItsContent");
            check(store.Load().Document!.State.Files.ContainsKey("a/0/9/9") && store.Commit(NdsProjectFormatChecks.Next(Other(Chain(4)), 5), 4) == 5, "NdsFileStore_AfterReloadingTheSessionCanContinueFromWhatIsThere");
        }
        project = Populated(root, "externo-generacion", 4);
        using (var store = NdsProjectFileStore.Open(project))
        {
            store.Load(); byte[] theirs = NdsProjectCodec.Serialize(Chain(6)); File.WriteAllBytes(project, theirs);
            check(Fails(() => store.Commit(Chain(5), 4)) == NdsProjectStoreError.StaleGeneration && File.ReadAllBytes(project).AsSpan().SequenceEqual(theirs), "NdsFileStore_ANewerGenerationWrittenElsewhereIsNotOverwritten");
        }
        foreach (var (label, replace) in new (string, Func<NdsProjectDocument, NdsProjectDocument>)[]
            { ("AnotherProject", d => new(d.Meta with { ProjectId = Guid.NewGuid() }, d.State)), ("AnotherRom", d => new(d.Meta with { Rom = NdsProjectFormatChecks.Rom(sha: "CD") }, d.State)) })
        {
            project = Populated(root, "externo-" + label, 3);
            using var store = NdsProjectFileStore.Open(project); byte[] theirs = NdsProjectCodec.Serialize(replace(Chain(3))); File.WriteAllBytes(project, theirs);
            check(Fails(() => store.Commit(Chain(4), 3)) == NdsProjectStoreError.DifferentProject && File.ReadAllBytes(project).AsSpan().SequenceEqual(theirs) && BackupGenerations(project).SequenceEqual([2L, 1]), $"NdsFileStore_{label}InItsPlaceIsNeverOverwritten");
        }
        project = Populated(root, "externo-borrado", 3);
        using (var store = NdsProjectFileStore.Open(project))
        {
            store.Load(); File.Delete(project);
            check(Fails(() => store.Commit(Chain(4), 3)) == NdsProjectStoreError.MainDamaged && !File.Exists(project) && store.Load().Status == NdsProjectLoadStatus.Damaged, "NdsFileStore_ADeletedMainFileWithBackupsIsRecoveredNotRecreated");
            foreach (int n in new[] { 1, 2 }) File.Delete(NdsProjectPaths.BackupOf(project, n));
            check(Fails(() => store.Commit(Chain(4), 3)) == NdsProjectStoreError.StaleGeneration && store.Load().Status == NdsProjectLoadStatus.Empty && !File.Exists(project), "NdsFileStore_AProjectDeletedCompletelyIsNotRecreatedFromAnOldSession");
        }
        project = Populated(root, "externo-nuevo", 3);
        using (var store = NdsProjectFileStore.Open(project))
        {
            var plan = Damage(project, store, b => b[NdsProjectCodec.HeaderSize + 30] ^= 1); File.WriteAllBytes(project, NdsProjectCodec.Serialize(new NdsProjectDocument(Chain(4).Meta with { MinReaderVersion = 2 }, Chain(4).State))); byte[] newer = File.ReadAllBytes(project);
            check(store.Load().Status == NdsProjectLoadStatus.NewerFormat && Fails(() => store.Commit(Chain(5), 4)) == NdsProjectStoreError.NewerFormat && Fails(() => store.Recover(plan.Recommended!)) == NdsProjectStoreError.NewerFormat
                && File.ReadAllBytes(project).AsSpan().SequenceEqual(newer) && !Names(Path.GetDirectoryName(project)!).Any(n => n.Contains("damaged", StringComparison.Ordinal)), "NdsFileStore_ANewerFormatProjectIsNeitherWrittenNorReplacedByABackup");
        }
    }

    /// <summary>Damages the main file and returns the recovery plan the store offers.</summary>
    private static NdsProjectRecoveryPlan Damage(string project, NdsProjectFileStore store, Action<byte[]> damage)
    {
        byte[] bytes = File.ReadAllBytes(project); damage(bytes); File.WriteAllBytes(project, bytes);
        return store.Load().Plan ?? throw new InvalidOperationException("El archivo dañado no se detectó.");
    }

    // ----------------------------------------------------------------------------------------------------------------------------- recovery

    private static void Recovery(Action<bool, string> check, string root)
    {
        int length = NdsProjectCodec.Serialize(Chain(5)).Length;
        foreach (int keep in new[] { 0, 10, NdsProjectCodec.HeaderSize, length / 2, length - 1 })
        {
            string project = Populated(root, "truncado-" + keep, 5), folder = Path.GetDirectoryName(project)!; byte[] truncated = File.ReadAllBytes(project)[..keep]; File.WriteAllBytes(project, truncated);
            using var store = NdsProjectFileStore.Open(project); var load = store.Load(); var plan = load.Plan;
            bool offered = load.Status == NdsProjectLoadStatus.Damaged && plan is not null && plan.Candidates.Select(c => c.Generation).SequenceEqual([4L, 3, 2]);
            bool untouched = Fails(() => store.Commit(Chain(6), 5)) == NdsProjectStoreError.MainDamaged && File.ReadAllBytes(project).AsSpan().SequenceEqual(truncated) && !Names(folder).Any(n => n.Contains("damaged", StringComparison.Ordinal));
            long generation = offered ? store.Recover(plan!.Recommended!) : -1; string[] preserved = Directory.GetFiles(folder, Name + ".damaged-*");
            check(offered && untouched && generation == 4 && preserved.Length == 1 && File.ReadAllBytes(preserved[0]).AsSpan().SequenceEqual(truncated) && store.Load() is { Status: NdsProjectLoadStatus.Loaded } l && NdsProjectFormatChecks.SameDoc(Chain(4), l.Document!)
                && store.Commit(Chain(5), 4) == 5 && Fails(() => store.Recover(plan!.Candidates[1])) == NdsProjectStoreError.WriteFailed && NoTemps(folder), $"NdsFileRecovery_MainTruncatedTo{keep}Bytes_IsOfferedPreservedRecoveredAndContinued");
        }
        string body = Populated(root, "cuerpo", 5);
        using (var store = NdsProjectFileStore.Open(body))
        {
            var plan = Damage(body, store, b => b[NdsProjectCodec.HeaderSize + 30] ^= 0x7F);
            check(plan.DamagedHeader is { Generation: 5 } && plan.LostOperations(plan.Recommended!) == 1 && plan.LostOperations(plan.Candidates[2]) == 3, "NdsFileRecovery_SaysHowManyOperationsMayBeLost");
            var foreign = NdsProjectFormatChecks.Doc(generation: 2, seed: 77);
            check(Fails(() => store.Recover(new("ajena", 2, foreign.OperationCount, foreign.Meta.SavedUtc, foreign, NdsProjectCodec.Serialize(foreign)))) == NdsProjectStoreError.WriteFailed, "NdsFileRecovery_ACandidateThatIsNotABackupOfThisProjectIsRefused");
            // The backup offered was replaced before the user chose: what is installed must be what was offered.
            byte[] bak = File.ReadAllBytes(NdsProjectPaths.BackupOf(body, 1)); File.WriteAllBytes(NdsProjectPaths.BackupOf(body, 1), NdsProjectCodec.Serialize(foreign));
            check(Fails(() => store.Recover(plan.Recommended!)) == NdsProjectStoreError.WriteFailed, "NdsFileRecovery_ABackupThatChangedAfterBeingOfferedIsNotInstalled");
            File.WriteAllBytes(NdsProjectPaths.BackupOf(body, 1), bak);
        }
        string partial = Populated(root, "copias-dañadas", 5);
        using (var store = NdsProjectFileStore.Open(partial))
        {
            var b1 = File.ReadAllBytes(NdsProjectPaths.BackupOf(partial, 1)); b1[b1.Length / 2] ^= 1; File.WriteAllBytes(NdsProjectPaths.BackupOf(partial, 1), b1);
            var plan = Damage(partial, store, b => b[30] ^= 1);
            check(plan.DamagedHeader is null && plan.Candidates.Select(c => c.Generation).SequenceEqual([3L, 2]) && plan.LostOperations(plan.Recommended!) is null, "NdsFileRecovery_DamagedBackupsAreNotOfferedAndAnUnreadableHeaderMeansUnknownLoss");
            foreach (int n in new[] { 2, 3 }) File.WriteAllBytes(NdsProjectPaths.BackupOf(partial, n), [1, 2, 3]);
            check(store.Load() is { Status: NdsProjectLoadStatus.Damaged, Plan.Recommended: null }, "NdsFileRecovery_NoValidBackupMeansNoOffer");
        }
        string missing = Populated(root, "sin-principal", 4); File.Delete(missing);
        using (var store = NdsProjectFileStore.Open(missing))
        {
            var load = store.Load();
            check(load.Status == NdsProjectLoadStatus.Damaged && load.Message.Contains("Falta") && store.Recover(load.Plan!.Recommended!) == 3 && store.Load().Document!.Generation == 3 && !Names(Path.GetDirectoryName(missing)!).Any(n => n.Contains("damaged", StringComparison.Ordinal)), "NdsFileRecovery_AMissingMainFileIsRecoveredFromItsBackups");
        }
        string garbage = Populated(root, "basura", 3); File.WriteAllText(garbage, "esto no es un proyecto");
        using (var store = NdsProjectFileStore.Open(garbage)) check(store.Load() is { Status: NdsProjectLoadStatus.Damaged, Plan.Candidates.Count: 2 }, "NdsFileRecovery_ForeignContentInTheMainFileIsDamagedNotEmpty");
        // Two recoveries within the same instant of the clock must not overwrite the first preserved file.
        string twice = Populated(root, "dos-veces", 4);
        using (var store = NdsProjectFileStore.Open(twice, clock: () => T0))
        {
            store.Recover(Damage(twice, store, b => b[100] ^= 1).Recommended!); store.Recover(Damage(twice, store, b => b[101] ^= 1).Recommended!);
            check(Directory.GetFiles(Path.GetDirectoryName(twice)!, Name + ".damaged-*").Length == 2, "NdsFileRecovery_EveryDamagedFileIsPreservedUnderItsOwnName");
        }
    }

    // ----------------------------------------------------------------------------------------------------------------------- read-only folder

    /// <summary>Denies the current user the right to create files in a folder (a real access rule), runs the action, and removes the rule.</summary>
    private static void WithoutWriteAccess(string folder, Action action)
    {
        var info = new DirectoryInfo(folder); var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, AccessControlType.Deny);
        var security = info.GetAccessControl(); security.AddAccessRule(rule); info.SetAccessControl(security);
        try { action(); }
        finally { security = info.GetAccessControl(); security.RemoveAccessRule(rule); info.SetAccessControl(security); }
    }

    private static void ReadOnlyFolder(Action<bool, string> check, string root)
    {
        string project = Populated(root, "solo-lectura", 3), folder = Path.GetDirectoryName(project)!, alternative = Folder(root, "alternativa-rl"); byte[] before = File.ReadAllBytes(project); var rom = Chain(1).Meta.Rom;
        string emptyFolder = Folder(root, "solo-lectura-vacia");
        WithoutWriteAccess(folder, () =>
        {
            check(!NdsProjectLocator.CanWrite(folder) && Fails(() => NdsProjectFileStore.Open(project).Dispose()) == NdsProjectStoreError.ReadOnly, "NdsFileStore_AFolderThatCannotBeWrittenIsReportedAsReadOnly");
            using var reader = NdsProjectFileStore.OpenReadOnly(project);
            check(reader.IsReadOnly && reader.Load() is { Status: NdsProjectLoadStatus.Loaded } l && NdsProjectFormatChecks.SameDoc(Chain(3), l.Document!), "NdsFileStore_AReadOnlyProjectCanStillBeRead");
            check(Fails(() => reader.Commit(Chain(4), 3)) == NdsProjectStoreError.ReadOnly && File.ReadAllBytes(project).AsSpan().SequenceEqual(before) && Names(folder).Length == 3, "NdsFileStore_AReadOnlyProjectIsNeverWritten");
            var resolution = NdsProjectLocator.Resolve(Path.Combine(folder, "Juego.nds"), rom, alternative);
            check(resolution.Preferred.BesideRom && resolution.ReadOnly && !resolution.NeedsConsent, "NdsProjectLocator_AnExistingProjectInAReadOnlyFolderIsOpenedReadOnlyNotDuplicated");
        });
        WithoutWriteAccess(emptyFolder, () =>
        {
            var resolution = NdsProjectLocator.Resolve(Path.Combine(emptyFolder, "Juego.nds"), rom, alternative);
            check(!resolution.Beside.FolderWritable && !resolution.Preferred.BesideRom && resolution.NeedsConsent && resolution.Preferred.ProjectPath == NdsProjectLocator.AlternativeOf(alternative, rom) && Names(alternative).Length == 0, "NdsProjectLocator_ANewProjectForAReadOnlyFolderGoesElsewhereOnlyWithConsent");
            using var store = NdsProjectFileStore.Open(resolution.Preferred.ProjectPath, createFolder: true);
            check(store.Commit(Chain(1), 0) == 1 && Names(emptyFolder).Length == 0, "NdsFileStore_TheAlternativePlaceWorksAndTheRomFolderStaysUntouched");
            var again = NdsProjectLocator.Resolve(Path.Combine(emptyFolder, "Juego.nds"), rom, alternative);
            check(again.Preferred.Exists && !again.Preferred.BesideRom && !again.NeedsConsent && !again.ReadOnly, "NdsProjectLocator_TheAlternativeProjectIsFoundAgain");
        });
        using (var store = NdsProjectFileStore.Open(project)) check(store.Commit(Chain(4), 3) == 4, "NdsFileStore_WritableAgainOnceTheAccessRuleIsRemoved");
    }

    private static void Locator(Action<bool, string> check, string root)
    {
        string folder = Folder(root, "ubicación"), alternative = Path.Combine(root, "alternativa", "no-creada"); var rom = Chain(1).Meta.Rom; string romPath = Path.Combine(folder, "Mi Platino.nds");
        var fresh = NdsProjectLocator.Resolve(romPath, rom, alternative);
        check(fresh.Preferred.BesideRom && fresh.Preferred.ProjectPath == romPath + ".drproj" && !fresh.Preferred.Exists && !fresh.NeedsConsent && !fresh.Ambiguous && Names(folder).Length == 0 && !Directory.Exists(alternative), "NdsProjectLocator_ANewProjectGoesNextToTheRomAndResolvingCreatesNothing");
        check(NdsProjectLocator.AlternativeOf(alternative, rom) == NdsProjectLocator.AlternativeOf(alternative, rom with { LastKnownPath = @"Z:\otra\Renombrada.nds" }) && NdsProjectLocator.AlternativeOf(alternative, rom) != NdsProjectLocator.AlternativeOf(alternative, NdsProjectFormatChecks.Rom(sha: "CD"))
            && NdsProjectLocator.AlternativeOf(alternative, rom).EndsWith(@"\CPUE-rev1.nds.drproj", StringComparison.Ordinal), "NdsProjectLocator_TheAlternativePathDependsOnlyOnTheRomIdentity");
        using (var store = NdsProjectFileStore.Open(NdsProjectLocator.AlternativeOf(alternative, rom), createFolder: true)) store.Commit(Chain(1), 0);
        var moved = NdsProjectLocator.Resolve(Path.Combine(Folder(root, "ubicación-2"), "Renombrada.nds"), rom, alternative);
        check(moved.Preferred.Exists && !moved.Preferred.BesideRom && !moved.Ambiguous, "NdsProjectLocator_AnAlternativeProjectIsFoundWhereverTheRomIsMoved");
        using (var store = NdsProjectFileStore.Open(romPath + ".drproj")) { store.Commit(Chain(1), 0); store.Commit(Chain(2), 1); }
        var both = NdsProjectLocator.Resolve(romPath, rom, alternative);
        check(both.Ambiguous && both.Preferred.BesideRom, "NdsProjectLocator_AProjectInBothPlacesIsReportedAsAmbiguous");
        File.Delete(romPath + ".drproj");
        check(NdsProjectLocator.Resolve(romPath, rom, alternative).Beside.Exists, "NdsProjectLocator_AProjectWithOnlyItsBackupsStillExists");
    }

    // --------------------------------------------------------------------------------------------------------------------- child processes

    private static Process Child(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add("--nds-project-store-child");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("No se pudo iniciar el proceso hijo.");
    }

    /// <summary>Entry point of the child processes (see <see cref="Interrupted"/>, <see cref="Instances"/> and <see cref="Race"/>).</summary>
    internal static int ChildMain(string[] arguments)
    {
        string mode = arguments[0], project = arguments[1];
        switch (mode)
        {
            case "kill":
            {
                // Saves generations up to the one given and terminates the process, without any cleanup, at the given step of the last save.
                var step = Enum.Parse<NdsProjectFileStep>(arguments[2]); int last = int.Parse(arguments[3]);
                var store = NdsProjectFileStore.Open(project);
                for (int g = 1; g < last; g++) store.Commit(Chain(g), g - 1);
                store.Probe = s => { if (s == step) { Process.GetCurrentProcess().Kill(); Thread.Sleep(Timeout.Infinite); } };
                store.Commit(Chain(last), last - 1);
                return 3;
            }
            case "hold":
            {
                using var store = NdsProjectFileStore.Open(project); Console.WriteLine("held"); Console.Out.Flush(); Console.In.ReadLine(); return 0;
            }
            case "race":
            {
                // Takes the project whenever it is free, saves one generation and lets it go, for the given time. Prints how many saves it made.
                var deadline = Stopwatch.StartNew(); int seconds = int.Parse(arguments[2]), saved = 0;
                while (deadline.Elapsed.TotalSeconds < seconds)
                {
                    NdsProjectFileStore store;
                    try { store = NdsProjectFileStore.Open(project); } catch (NdsProjectStoreException ex) when (ex.Kind == NdsProjectStoreError.AlreadyInUse) { Thread.Sleep(1); continue; }
                    using (store)
                    {
                        var load = store.Load(); if (load.Status is not (NdsProjectLoadStatus.Empty or NdsProjectLoadStatus.Loaded)) return 4;
                        long generation = load.Document?.Generation ?? 0;
                        store.Commit(generation == 0 ? Chain(1) : NdsProjectFormatChecks.Next(load.Document!, (int)generation + 1), generation); saved++;
                    }
                }
                Console.WriteLine("saved=" + saved); return 0;
            }
            default: return 2;
        }
    }

    /// <summary>A process is terminated (no finalizers, no cleanup) at every step of a save. Whatever the step, the project found afterwards is a complete generation.</summary>
    private static void Interrupted(Action<bool, string> check, string root)
    {
        foreach (var step in Enum.GetValues<NdsProjectFileStep>())
        {
            string folder = Folder(root, "corte-" + step), project = Path.Combine(folder, Name);
            using (var child = Child("kill", project, step.ToString(), "5")) { if (!child.WaitForExit(60000)) { child.Kill(); throw new IOException("El proceso hijo no terminó."); } if (child.ExitCode == 3) throw new IOException("El proceso hijo no se interrumpió en " + step + "."); }
            bool replaced = step == NdsProjectFileStep.AfterReplace, rotated = step >= NdsProjectFileStep.BackupsRotated; int expected = replaced ? 5 : 4;
            using var store = NdsProjectFileStore.Open(project); var load = store.Load();
            bool whole = load.Status == NdsProjectLoadStatus.Loaded && NdsProjectFormatChecks.SameDoc(Chain(expected), load.Document!);
            bool backups = BackupGenerations(project).SequenceEqual(rotated ? [4L, 3, 2] : [3L, 2, 1]);
            bool resumes = whole && store.Commit(Chain(expected + 1), expected) == expected + 1 && NoTemps(folder) && BackupGenerations(project).SequenceEqual(replaced ? [5L, 4, 3] : [4L, 3, 2]);
            check(whole && backups && resumes, $"NdsFileStore_ProcessKilledAt{step}_LeavesGeneration{expected}CompleteAndTheNextSaveContinues");
        }
    }

    private static void Instances(Action<bool, string> check, string root)
    {
        string project = Populated(root, "instancias", 2);
        using (var first = NdsProjectFileStore.Open(project))
        {
            string message = ""; NdsProjectStoreError? kind = null;
            try { NdsProjectFileStore.Open(project).Dispose(); } catch (NdsProjectStoreException ex) { kind = ex.Kind; message = ex.Message; }
            check(kind == NdsProjectStoreError.AlreadyInUse && message.Contains("pid " + Environment.ProcessId, StringComparison.Ordinal), "NdsFileStore_ASecondInstanceIsRefusedAndToldWhoHoldsTheProject");
            using var reader = NdsProjectFileStore.OpenReadOnly(project);
            check(reader.Load().Document!.Generation == 2, "NdsFileStore_AReadOnlyOpenDoesNotNeedTheLock");
        }
        int won = 0, refused = 0, other = 0; var stores = new System.Collections.Concurrent.ConcurrentBag<NdsProjectFileStore>(); using var gate = new ManualResetEventSlim();
        var threads = Enumerable.Range(0, 16).Select(_ => new Thread(() =>
        {
            gate.Wait();
            try { stores.Add(NdsProjectFileStore.Open(project)); Interlocked.Increment(ref won); }
            catch (NdsProjectStoreException ex) when (ex.Kind == NdsProjectStoreError.AlreadyInUse) { Interlocked.Increment(ref refused); }
            catch (Exception) { Interlocked.Increment(ref other); }
        })).ToList();
        threads.ForEach(t => t.Start()); gate.Set(); threads.ForEach(t => t.Join()); foreach (var s in stores) s.Dispose();
        check(won == 1 && refused == 15 && other == 0, $"NdsFileStore_OfSixteenSimultaneousOpensExactlyOneGetsTheProject_{won}Won_{refused}Refused");
        int reopened = 0; for (int i = 0; i < 200; i++) { using var s = NdsProjectFileStore.Open(project); reopened++; }
        check(reopened == 200, "NdsFileStore_ClosingAndReopeningImmediatelyAlwaysWorks_200Times");
        // Another PROCESS holds the project; when it is terminated the system releases the lock.
        using var child = Child("hold", project);
        string? line = child.StandardOutput.ReadLine();
        var whileHeld = Fails(() => NdsProjectFileStore.Open(project).Dispose());
        child.Kill(); child.WaitForExit(30000);
        using var after = NdsProjectFileStore.Open(project);
        check(line == "held" && whileHeld == NdsProjectStoreError.AlreadyInUse && after.Load().Document!.Generation == 2 && after.Commit(Chain(3), 2) == 3, "NdsFileStore_AnotherProcessHoldsTheProjectUntilItDiesAndThenItIsFree");
    }

    /// <summary>Four processes fight for one project for a few seconds. Every save any of them reports must be there: the final generation is the sum.</summary>
    private static void Race(Action<bool, string> check, string root)
    {
        string project = Path.Combine(Folder(root, "carrera"), Name);
        var children = Enumerable.Range(0, 4).Select(_ => Child("race", project, "4")).ToList(); long total = 0; bool clean = true;
        foreach (var child in children)
        {
            string output = child.StandardOutput.ReadToEnd(); if (!child.WaitForExit(120000)) { child.Kill(); clean = false; }
            clean &= child.ExitCode == 0; var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("saved=", StringComparison.Ordinal));
            if (line is null) { clean = false; Console.WriteLine("   hijo: " + output + child.StandardError.ReadToEnd()); } else total += long.Parse(line[6..]);
            child.Dispose();
        }
        using var store = NdsProjectFileStore.Open(project); var load = store.Load();
        check(clean && total > 0 && load.Status == NdsProjectLoadStatus.Loaded && load.Document!.Generation == total && load.Document.Meta.Operations.Count == 3 + total - 1 && BackupGenerations(project).All(g => g > 0) && NoTemps(Path.GetDirectoryName(project)!),
            $"NdsFileStore_FourProcessesRacingForOneProject_EverySaveIsThere_{total}Saves");
    }

    // ------------------------------------------------------------------------------------------------------------------------- transactions

    /// <summary>The transaction of PR 1 over the real store: hundreds of operations, reopened sessions and failures at random steps; only the confirmed ones remain.</summary>
    private static void Transactions(Action<bool, string> check, string root)
    {
        string project = Path.Combine(Folder(root, "transacciones"), Name); var rng = new Random(2026); var model = new Dictionary<string, byte[]>(); int confirmed = 0, refused = 0, unknown = 0, sessions = 1;
        var steps = Enum.GetValues<NdsProjectFileStep>(); var store = NdsProjectFileStore.Open(project);
        var tx = new NdsProjectTransactor(store, NdsProjectFormatChecks.Doc(generation: 0, ops: 0, files: 0, events: false, arm9: false));
        for (int i = 1; i <= 300; i++)
        {
            string key = "a/0/" + rng.Next(6) + "/1"; var data = new byte[rng.Next(1, 3000)]; rng.NextBytes(data); int index = i;
            if (rng.Next(8) == 0) { var at = steps[rng.Next(steps.Length)]; store.Probe = s => { if (s == at) { store.Probe = null; throw new IOException("fallo simulado en " + at); } }; }
            try
            {
                tx.Run(d => new(d.Meta with { Operations = [.. d.Meta.Operations, new NdsProjectOperationRecord("Edición " + index, null, new JsonObject { ["Kind"] = "Edit", ["I"] = index }, [new("k", "f", 0, index)])] },
                    new NdsProjectState { Files = new(d.State.Files) { [key] = data } }), _ => { });
                model[key] = data; confirmed++;
            }
            catch (NdsProjectStoreException) { refused++; }
            catch (NdsProjectFaultedException)
            {
                // Unknown outcome: the session is blocked until it adopts what is on the disk, whichever it is.
                long before = tx.Current.Generation; tx.Reconcile(_ => { }); unknown++;
                if (tx.Current.Generation == before + 1) { model[key] = data; confirmed++; }
            }
            store.Probe = null;
            if (i % 37 == 0) { store.Dispose(); store = NdsProjectFileStore.Open(project); tx = new NdsProjectTransactor(store, store.Load().Document!); sessions++; }
        }
        store.Dispose();
        using var final = NdsProjectFileStore.Open(project); var doc = final.Load().Document!;
        check(doc.Meta.Operations.Count == confirmed && doc.Generation == confirmed && refused > 5 && unknown > 0 && doc.State.Files.Count == model.Count && model.All(p => doc.State.Files[p.Key].AsSpan().SequenceEqual(p.Value))
            && BackupGenerations(project).SequenceEqual([confirmed - 1L, confirmed - 2, confirmed - 3]) && NoTemps(Path.GetDirectoryName(project)!) && sessions > 8,
            $"NdsFileStore_300OperationsAcrossReopenedSessionsWithFailures_OnlyConfirmedOnesRemain_{confirmed}Confirmed_{refused}Refused_{unknown}Unknown");
    }

    private static void Timing(Action<bool, string> check, string root)
    {
        // A state the size of the measured real projects (about 400 KiB raw). Informative: the figure depends on the disk the folder is on.
        string project = Path.Combine(Folder(root, "tiempos"), Name); using var store = NdsProjectFileStore.Open(project); var doc = NdsProjectFormatChecks.Doc(files: 5, fileSize: 80 * 1024, generation: 1); store.Commit(doc, 0);
        var times = new List<double>();
        for (int i = 2; i <= 21; i++) { doc = new(doc.Meta with { Generation = i }, doc.State); var sw = Stopwatch.StartNew(); store.Commit(doc, i - 1); times.Add(sw.Elapsed.TotalMilliseconds); }
        times.Sort(); Console.WriteLine($"   guardado duradero de {new FileInfo(project).Length / 1024} KiB en {Path.GetPathRoot(project)}: mediana {times[times.Count / 2]:0.0} ms · máximo {times[^1]:0.0} ms");
        check(times[times.Count / 2] < 5000, "NdsFileStore_ADurableSaveOfARealisticProjectFitsTheInteractiveBudget");
    }
}
