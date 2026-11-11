using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Nds.Project;

/// <summary>
/// Persistent DS project, PR 5: endurance and the state shown to the user. Hundreds of operations per profile over many sessions, with saves that fail at
/// random steps; the recovered state must be, byte for byte, the state of a session that applied only the confirmed operations, and export the same ROM.
/// </summary>
internal static class NdsProjectResilienceChecks
{
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Operation number i: mostly manual edits of different cells, with a randomization every tenth and a data randomization every tenth plus five.</summary>
    private static NdsPreview Operation(NdsProject project, int i)
    {
        if (i % 10 == 0) return project.PreviewRandom("Especies " + i, new Gen5RandomizerOptions((uint)(1000 + i)) { Trainers = Gen5SpeciesMode.SimilarBst, Wild = i % 20 == 0 ? Gen5SpeciesMode.Random : Gen5SpeciesMode.Keep });
        if (i % 10 == 5) return project.PreviewData("Datos " + i, NdsSection.Pokemon, new NdsDataOptions((uint)(2000 + i)) { Stats = NdsStatsMode.Random, Types = i % 20 == 5 });
        if (i % 2 == 0)
        {
            var cells = project.ReadTrainerFields().Where(c => c.Kind == NdsValueKind.Number && c.Max > c.Min).ToList(); var cell = cells[i * 7 % cells.Count];
            return project.PreviewTrainerEdits(new Dictionary<string, int> { [cell.Key] = cell.Value < cell.Max ? cell.Value + 1 : cell.Value - 1 });
        }
        else
        {
            var cells = project.Read(NdsSection.Pokemon).Where(c => c.Kind == NdsValueKind.Number && c.Max > c.Min).ToList(); var cell = cells[i * 13 % cells.Count];
            return project.PreviewEdits(NdsSection.Pokemon, new Dictionary<string, int> { [cell.Key] = cell.Value < cell.Max ? cell.Value + 1 : cell.Value - 1 });
        }
    }

    internal static void Run(Action<bool, string> check, int operations = 300)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-project-resilience-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string gen5Base = Path.Combine(temp, "base.nds"); File.WriteAllBytes(gen5Base, NdsGen5RandomizerChecks.SyntheticBlackRomhack());
            var fixtures = Gen4Profile.All.Select(p => (p.GameCode, Bytes: NdsGen4Checks.SyntheticRom(p, true))).Concat(Gen5Profile.All.Select(p => (p.GameCode, Bytes: NdsTrainerParityChecks.SyntheticGen5(p, gen5Base)))).ToList();
            foreach (var (code, bytes) in fixtures)
            {
                string folder = Directory.CreateDirectory(Path.Combine(temp, code)).FullName, rom = Path.Combine(folder, code + ".nds"); File.WriteAllBytes(rom, bytes);
                Endurance(rom, rom + NdsProjectPaths.Extension, true, folder, operations, (ok, name) => check(ok, "NdsProjectResilience_" + code + "_" + name));
            }
            States(Path.Combine(temp, fixtures[0].GameCode, fixtures[0].GameCode + ".nds"), temp, check);
        }
        finally { Directory.Delete(temp, true); }
    }

    /// <summary>Real dumps, read only; the projects are written in the output folder.</summary>
    internal static void Probe(string output, int operations, IReadOnlyList<string> roms)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output); int checks = 0;
        foreach (string rom in roms)
        {
            string before = Hash(rom), name = Path.GetFileNameWithoutExtension(rom), folder = Directory.CreateDirectory(Path.Combine(output, new string(name.Where(char.IsLetterOrDigit).Take(24).ToArray()))).FullName; Console.WriteLine("== " + name);
            Endurance(rom, Path.Combine(folder, "proyecto.nds.drproj"), false, folder, operations, (ok, test) => { if (!ok) throw new Exception("FAIL " + test); checks++; Console.WriteLine("PASS " + test); });
            if (Hash(rom) != before || Directory.GetFiles(Path.GetDirectoryName(rom)!, "*.drproj*").Length > 0) throw new Exception("FAIL la ROM original cambió o apareció un proyecto junto a ella");
            Console.WriteLine("PASS la ROM original no cambió y no se creó nada junto a ella"); checks++;
            foreach (string exported in Directory.GetFiles(folder, "*.nds")) File.Delete(exported);
        }
        Console.WriteLine($"Completed {checks} DS project resilience checks on private dumps (read-only sources).");
    }

    private static void Endurance(string rom, string projectPath, bool romhack, string folder, int operations, Action<bool, string> check)
    {
        var rng = new Random(2026); var steps = Enum.GetValues<NdsProjectFileStep>(); int confirmed = 0, refused = 0, unknown = 0, sessions = 1, mismatches = 0, skipped = 0; string why = ""; bool states = true; var times = new List<double>();
        using var twin = NdsProject.Open(rom, romhack);
        NdsProjectFileStore store = NdsProjectFileStore.Open(projectPath); var live = NdsProject.Open(rom, romhack);
        try
        {
            var identity = NdsProjectRomIdentity.Compute(rom, live.IsRomhack); live.AttachPersistence(store, identity, null, "resistencia");
            for (int i = 1; i <= operations; i++)
            {
                NdsPreview preview;
                try { preview = Operation(live, i); } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or NotSupportedException) { skipped++; why = ex.Message.Split('\n')[0]; continue; }
                bool fail = rng.Next(9) == 0; if (fail) { var at = steps[rng.Next(steps.Length)]; var target = store; target.Probe = s => { if (s == at) { target.Probe = null; throw new IOException("fallo simulado en " + at); } }; }
                long generation = live.SavedGeneration; var clock = Stopwatch.StartNew();
                try { live.Apply(preview); times.Add(clock.Elapsed.TotalMilliseconds); twin.Apply(Operation(twin, i)); confirmed++; states &= live.PersistenceState == NdsProjectPersistenceState.Saved && live.SavedGeneration == generation + 1; }
                catch (NdsProjectSaveException) { refused++; states &= live.PersistenceState == NdsProjectPersistenceState.Error && live.SavedGeneration == generation; }
                catch (NdsProjectFaultedException)
                {
                    // The outcome is unknown: the session is blocked, and reconciling installs whatever is on disk.
                    unknown++; states &= live.PersistenceState == NdsProjectPersistenceState.RecoveryPending;
                    live.ReconcilePersistence(); states &= live.PersistenceState == NdsProjectPersistenceState.Saved;
                    if (live.SavedGeneration == generation + 1) { twin.Apply(Operation(twin, i)); confirmed++; }
                }
                store.Probe = null;
                if (i % 20 == 0)
                {
                    // Close everything and resume from the disk, as on another day.
                    live.Dispose(); store = NdsProjectFileStore.Open(projectPath); live = NdsProject.Open(rom, romhack); live.AttachPersistence(store, identity, store.Load().Document, "resistencia"); sessions++;
                    if (NdsProjectTransactionChecks.Fingerprint(live) != NdsProjectTransactionChecks.Fingerprint(twin)) mismatches++;
                }
            }
            times.Sort(); long size = new FileInfo(projectPath).Length; if (skipped > 0) Console.WriteLine($"   ({skipped} operaciones no aplicables a esta ROM; la última: {why})");
            Console.WriteLine($"   {live.Game}: {confirmed} confirmadas · {refused} rechazadas · {unknown} de resultado desconocido · {sessions} sesiones · proyecto {size / 1024} KiB · {live.History.Sum(o => o.Changes.Count)} cambios · guardar una operación: mediana {times[times.Count / 2]:0.0} ms, máximo {times[^1]:0.0} ms");
            check(confirmed >= operations * 2 / 3 && refused > 5 && sessions > operations / 25 && live.SavedGeneration == confirmed + 1 && live.History.Count == confirmed, $"{confirmed}OperationsOver{sessions}SessionsWith{refused + unknown}FailedSaves_OnlyTheConfirmedOnesAreThere");
            check(mismatches == 0 && NdsProjectTransactionChecks.Fingerprint(live) == NdsProjectTransactionChecks.Fingerprint(twin), "EveryResumedSessionIsByteIdenticalToASessionThatNeverClosed");
            check(states, "TheStateShownToTheUserFollowsEverySaveFailureAndRecovery");
            check(times[times.Count / 2] < 2000, "SavingAnOperationStaysWithinTheInteractiveBudgetAsTheProjectGrows");
            live.Export(Path.Combine(folder, "resistencia-viva.nds")); twin.Export(Path.Combine(folder, "resistencia-sin-proyecto.nds"));
        }
        finally { live.Dispose(); }
        using (var resumed = NdsProjectTransactionChecks.OpenPersistent(rom, projectPath, romhack))
        {
            resumed.Export(Path.Combine(folder, "resistencia-reanudada.nds"));
            check(Hash(Path.Combine(folder, "resistencia-reanudada.nds")) == Hash(Path.Combine(folder, "resistencia-viva.nds")) && Hash(Path.Combine(folder, "resistencia-viva.nds")) == Hash(Path.Combine(folder, "resistencia-sin-proyecto.nds")), "TheRomExportedAfterTheLastReopeningIsByteIdentical");
            var backups = Enumerable.Range(1, 3).Select(n => NdsProjectCodec.Read(File.ReadAllBytes(NdsProjectPaths.BackupOf(projectPath, n)))).ToList();
            // After a save that failed once the backup was made, backup 1 is the current generation until the next save (which then does not rotate again):
            // the backups are always whole, distinct, consecutive generations, the newest being the current one or the one before it.
            var generations = backups.Where(b => b.Ok).Select(b => b.Document!.Generation).ToArray(); long newest = generations.Length > 0 ? generations[0] : -1;
            check(generations.Length == 3 && newest >= resumed.SavedGeneration - 1 && newest <= resumed.SavedGeneration && generations.SequenceEqual([newest, newest - 1, newest - 2]) && !Directory.GetFiles(folder, "*.tmp-*").Any(), "TheThreeBackupsAreWholeConsecutiveGenerationsAndNoTemporaryFileRemains");
        }
    }

    /// <summary>The six states, as the project reports them and as the DS workspace shows them (off-screen).</summary>
    private static void States(string rom, string temp, Action<bool, string> check)
    {
        var scenarios = NdsProjectTransactionChecks.Scenarios(); var identity = NdsProjectRomIdentity.Compute(rom, true); var view = new NdsWorkspaceView(new Window());
        void Pump(Task task) { var timer = Stopwatch.StartNew(); while (!task.IsCompleted) { if (timer.Elapsed.TotalSeconds > 90) throw new TimeoutException(); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); } task.GetAwaiter().GetResult(); }
        void Click() => view.ProjectActionButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        bool Action(string? label) => label is null ? view.ProjectActionButton.Visibility != Visibility.Visible : view.ProjectActionButton.Visibility == Visibility.Visible && (string)view.ProjectActionButton.Content == label;

        check(view.ProjectStateText == "" && Action(null), "NdsProjectState_NothingIsShownWithoutARom");
        var plain = NdsProject.Open(rom, true); Pump(view.InstallAsync(plain, true));
        check(plain.PersistenceState == NdsProjectPersistenceState.None && view.ProjectStateText.StartsWith("Sin proyecto", StringComparison.Ordinal) && Action(null), "NdsProjectState_ASessionWithoutAProjectSaysItsChangesAreOnlyInMemory");

        var medium = new NdsProjectMedium(); var store = new InMemoryNdsProjectStore(medium); var project = NdsProject.Open(rom, true); project.AttachPersistence(store, identity, null, "estados");
        Pump(view.InstallAsync(project, true)); plain.Dispose();
        check(project.PersistenceState == NdsProjectPersistenceState.Saved && view.ProjectStateText == "Proyecto guardado · generación 1" && Action(null), "NdsProjectState_Saved_IsShownWithItsGeneration");
        // "Saving" exists only while the store is working: it is observed from inside the save.
        var during = new List<(NdsProjectPersistenceState State, string Text)>(); project.PersistenceChanged += () => during.Add((project.PersistenceState, project.PersistenceSummary));
        project.Apply(scenarios[0].Make(project));
        check(during.Count == 2 && during[0] is { State: NdsProjectPersistenceState.Saving, Text: "Guardando el proyecto…" } && during[1].State == NdsProjectPersistenceState.Saved && view.ProjectStateText == "Proyecto guardado · generación 2", "NdsProjectState_Saving_IsReportedWhileTheStoreWorksAndThenSaved");

        store.Fault = NdsProjectFault.FailBeforeWrite; try { project.Apply(scenarios[1].Make(project)); } catch (NdsProjectSaveException) { }
        check(project.PersistenceState == NdsProjectPersistenceState.Error && view.ProjectStateText.StartsWith("Error al guardar", StringComparison.Ordinal) && Action(null), "NdsProjectState_Error_SaysTheChangeWasNotAppliedAndOffersNothingToDecide");
        project.Apply(scenarios[1].Make(project));
        check(project.PersistenceState == NdsProjectPersistenceState.Saved && view.ProjectStateText == "Proyecto guardado · generación 3", "NdsProjectState_TheErrorClearsWithTheNextSuccessfulSave");

        store.Fault = NdsProjectFault.FailAfterPublish; try { project.Apply(scenarios[2].Make(project)); } catch (NdsProjectFaultedException) { }
        check(project.PersistenceState == NdsProjectPersistenceState.RecoveryPending && view.ProjectStateText.StartsWith("Recuperación pendiente", StringComparison.Ordinal) && Action("Reconciliar con lo guardado"), "NdsProjectState_RecoveryPending_OffersToReconcile");
        Click();
        check(project.PersistenceState == NdsProjectPersistenceState.Saved && project.History.Count == 3 && view.ProjectStateText == "Proyecto guardado · generación 4" && Action(null), "NdsProjectState_Reconciling_InstallsWhatWasSavedAndReturnsToSaved");

        // The file is replaced outside the session by a later generation of the same project.
        string mine = NdsProjectTransactionChecks.Fingerprint(project); var theirs = new NdsProjectDocument(project.SavedDocument!.Meta with { Generation = 9, Operations = [] }, new NdsProjectState()); medium.Main = NdsProjectCodec.Serialize(theirs);
        try { project.Apply(scenarios[0].Make(project)); } catch (NdsProjectSaveException) { }
        check(project.PersistenceState == NdsProjectPersistenceState.Conflict && view.ProjectStateText.StartsWith("Conflicto de escritura", StringComparison.Ordinal) && Action("Recargar el proyecto guardado") && NdsProjectTransactionChecks.Fingerprint(project) == mine, "NdsProjectState_Conflict_KeepsTheSessionAndOffersToReload");
        Click();
        check(project.PersistenceState == NdsProjectPersistenceState.Saved && project.SavedGeneration == 9 && project.History.Count == 0 && view.ProjectStateText == "Proyecto guardado · generación 9" && Action(null), "NdsProjectState_Reloading_ShowsWhatIsOnDiskWithoutOverwritingIt");
        project.Apply(scenarios[0].Make(project));
        check(project.SavedGeneration == 10 && project.PersistenceState == NdsProjectPersistenceState.Saved, "NdsProjectState_WorkContinuesAfterReloading");
        // Another project put in its place is never loaded into this session.
        byte[] other = NdsProjectCodec.Serialize(new NdsProjectDocument(project.SavedDocument!.Meta with { ProjectId = Guid.NewGuid() }, new NdsProjectState())); medium.Main = other; string held = NdsProjectTransactionChecks.Fingerprint(project);
        try { project.Apply(scenarios[1].Make(project)); } catch (NdsProjectSaveException) { }
        Click();
        check(project.PersistenceState == NdsProjectPersistenceState.Conflict && NdsProjectTransactionChecks.Fingerprint(project) == held && medium.Main.AsSpan().SequenceEqual(other) && Action("Recargar el proyecto guardado"), "NdsProjectState_AnotherProjectInItsPlaceIsNotLoadedAndTheConflictStays");

        // Read-only: a project on disk opened where it cannot be written.
        string folder = Directory.CreateDirectory(Path.Combine(temp, "estado-solo-lectura")).FullName, copy = Path.Combine(folder, "juego.nds"); File.Copy(rom, copy);
        using (var writer = NdsProjectTransactionChecks.OpenPersistent(copy, copy + ".drproj", true)) writer.Apply(scenarios[0].Make(writer));
        var reader = NdsProjectTransactionChecks.OpenPersistent(copy, copy + ".drproj", true, readOnly: true); Pump(view.InstallAsync(reader, true)); project.Dispose();
        bool refused = false; try { reader.Apply(scenarios[1].Make(reader)); } catch (NdsProjectSaveException ex) { refused = ex.Kind == NdsProjectStoreError.ReadOnly; }
        check(reader.PersistenceState == NdsProjectPersistenceState.ReadOnly && refused && view.ProjectStateText.StartsWith("Proyecto de solo lectura", StringComparison.Ordinal) && Action(null) && reader.History.Count == 1, "NdsProjectState_ReadOnly_IsShownFromTheStartAndOperationsAreRefused");
        reader.Dispose();
        check(System.Windows.Automation.AutomationProperties.GetName(view.ProjectActionButton).StartsWith("Proyecto DS · ", StringComparison.Ordinal), "NdsProjectState_TheActionHasAnAccessibleName");
    }
}
