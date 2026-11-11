using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using pk3DS.Core;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.ProjectDetection;
using ThreeDS.Progressive.Services.Switch;
using Button = System.Windows.Controls.Button;

/// <summary>
/// Universal "Abrir proyecto" (B2). Synthetic and off-screen: the coordinator with a fake host, the real engines opening disposable copies (including the
/// typed 3DS recovery), the dialog's races, and the real MainWindow moving between Nintendo DS, Nintendo 3DS and Switch with the three real engines.
/// </summary>
internal static class UniversalOpenChecks
{
    // ------------------------------------------------------------------------------------------------------------------------------------ fakes

    private sealed class FakeHost : IProjectOpenHost
    {
        internal string? Blocked; internal bool Session = true, Pending, AcceptReplace = true, AcceptWarning = true, AcceptRecovery = true, AcceptLeave = true, InstallThrows, InstallReturnsFalse;
        internal readonly List<string> Calls = new(); internal PreparedProject? Installed; internal string? RecoveryPath;
        public string? WhyBlocked(DetectedPlatform target) { Calls.Add("blocked?"); return Blocked; }
        public ReplacementInfo Inspect(DetectedPlatform target) { Calls.Add("inspect"); return new(Session, Pending, "actual"); }
        public bool ConfirmLeaveView(DetectedPlatform target) { Calls.Add("leave"); return AcceptLeave; }
        public bool ConfirmReplace(ProjectDetection detection, ReplacementInfo current) { Calls.Add("replace"); return AcceptReplace; }
        public bool ConfirmWarning(ProjectDetection detection) { Calls.Add("warning"); return AcceptWarning; }
        public bool ConfirmRecovery(ProjectDetection detection, InterruptedSaveException pending) { Calls.Add("recovery"); RecoveryPath = pending.PendingPath; return AcceptRecovery; }
        public Task<bool> InstallAsync(PreparedProject prepared, OpenOptions options)
        {
            Calls.Add("install");
            if (InstallThrows) throw new InvalidOperationException("fallo al instalar");
            if (InstallReturnsFalse) return Task.FromResult(false);
            prepared.Release(); Installed = prepared; return Task.FromResult(true);
        }
    }

    private sealed class Spy
    {
        internal int Prepared, Disposed; internal readonly List<OpenOptions> Options = new(); internal Func<ProjectDetection, OpenOptions, PreparedProject>? Behaviour; internal ManualResetEventSlim? Gate;
        internal PreparedProject Prepare(ProjectDetection d, OpenOptions o)
        {
            Prepared++; Options.Add(o); Gate?.Wait(10000);
            if (Behaviour is not null) return Behaviour(d, o);
            return new PreparedProject(d.Platform, d, new object(), d.Game, () => Disposed++);
        }
        internal PreparedProject Make(ProjectDetection d, string game = "Juego") => new(d.Platform, d, new object(), game, () => Disposed++);
    }

    private static ProjectDetection Det(DetectedPlatform platform, DetectedKind kind, string path = "x", string game = "Pokémon Prueba", string? warning = null, DetectionStatus status = DetectionStatus.Compatible, string message = "ok") =>
        new(status, platform, kind, path, game, message, [], null, warning);

    private static T Wait<T>(Task<T> task) { var time = System.Diagnostics.Stopwatch.StartNew(); while (!task.IsCompleted) { if (time.Elapsed.TotalSeconds > 120) throw new TimeoutException(); Spin(); } return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task) { var time = System.Diagnostics.Stopwatch.StartNew(); while (!task.IsCompleted) { if (time.Elapsed.TotalSeconds > 120) throw new TimeoutException(); Spin(); } task.GetAwaiter().GetResult(); }
    private static void Spin() { System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); Thread.Sleep(1); }
    private static void Until(Func<bool> condition, string what) { var time = System.Diagnostics.Stopwatch.StartNew(); while (!condition()) { if (time.Elapsed.TotalSeconds > 60) throw new TimeoutException("Esperando: " + what); Spin(); } }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject d) { if (d is T t) yield return t; foreach (var nested in Descendants<T>(d)) yield return nested; }
    }
    private static FrameworkElement ById(DependencyObject root, string id) => Descendants<FrameworkElement>(root).First(e => System.Windows.Automation.AutomationProperties.GetAutomationId(e) == id);
    private static OpenProjectDialog Show(OpenProjectDialog dialog) { dialog.TypingDelay = TimeSpan.Zero; dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = dialog.Top = -20000; dialog.ShowActivated = false; dialog.Show(); return dialog; }

    // ------------------------------------------------------------------------------------------------------------------------------- entry points

    /// <summary>No WPF: the coordinator, the typed 3DS recovery and the engines on disposable copies.</summary>
    internal static void Run(Action<bool, string> check)
    {
        Coordinator(check);
        string temp = Path.Combine(Path.GetTempPath(), "universal-open-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try { Recovery(temp, check); }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }

    /// <summary>Real MainWindow and real dialog, off-screen. Needs an STA thread with the application resources.</summary>
    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "universal-open-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try { DialogRaces(check); Window(temp, check); }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }

    // ----------------------------------------------------------------------------------------------------------------------------- coordinator

    private static void Coordinator(Action<bool, string> check)
    {
        var token = CancellationToken.None;
        OpenResult Go(FakeHost host, Spy spy, ProjectDetection d, OpenOptions? o = null, CancellationToken? c = null, Func<string?, ProjectDetection>? detect = null) =>
            Wait(new ProjectOpenCoordinator(host, spy.Prepare, detect ?? (_ => d)).OpenAsync(d, o ?? new OpenOptions(), c ?? token));
        var ds = Det(DetectedPlatform.NintendoDS, DetectedKind.DsRom); var g3 = Det(DetectedPlatform.Nintendo3DS, DetectedKind.Extracted3dsFolder); var sw = Det(DetectedPlatform.NintendoSwitch, DetectedKind.SwitchExtraction);

        // Every platform reaches its own engine through the same path and installs once.
        foreach (var d in new[] { ds, g3, sw })
        {
            var host = new FakeHost { Session = false }; var spy = new Spy(); var r = Go(host, spy, d);
            check(r.Opened && spy.Prepared == 1 && host.Installed is { Released: true } && spy.Disposed == 0 && !host.Calls.Contains("replace"), $"UniversalOpen_{d.Platform}_OpensAndInstallsOnceWithoutAsking");
        }
        // Detection that is not a candidate never reaches an engine.
        foreach (var status in new[] { DetectionStatus.Unsupported, DetectionStatus.Incomplete, DetectionStatus.Ambiguous, DetectionStatus.Unknown, DetectionStatus.Inaccessible })
        {
            var host = new FakeHost(); var spy = new Spy(); var r = Go(host, spy, Det(DetectedPlatform.NintendoDS, DetectedKind.DsRom, status: status, message: "no"));
            check(r.Outcome == OpenOutcome.Rejected && spy.Prepared == 0 && host.Calls.Count == 0, "UniversalOpen_NotACandidate_" + status + "_NeverReachesAnEngine");
        }
        // The same platform with pending changes asks; cancelling keeps everything and opens nothing.
        { var host = new FakeHost { Session = true, Pending = true, AcceptReplace = false }; var spy = new Spy(); var r = Go(host, spy, ds);
          check(r.Outcome == OpenOutcome.Cancelled && spy.Prepared == 0 && host.Installed is null && !host.Calls.Contains("install") && host.Calls.Contains("replace"), "UniversalOpen_CancelWithPendingChanges_OpensNothingAndInstallsNothing"); }
        { var host = new FakeHost { AcceptLeave = false }; var spy = new Spy(); var r = Go(host, spy, sw);
          check(r.Outcome == OpenOutcome.Cancelled && spy.Prepared == 0 && !host.Calls.Contains("replace"), "UniversalOpen_UnsettledPageOfTheVisibleWorkspace_CancelsBeforeOpening"); }
        { var host = new FakeHost { Session = false, Pending = true }; var spy = new Spy(); var r = Go(host, spy, g3);
          check(r.Opened && !host.Calls.Contains("replace"), "UniversalOpen_NoSessionOfTheTargetPlatform_DoesNotAsk"); }
        { var host = new FakeHost { Session = true, Pending = false }; var spy = new Spy(); var r = Go(host, spy, g3);
          check(r.Opened && host.Calls.Contains("replace"), "UniversalOpen_ExistingSessionIsAlwaysPassedToTheHostBeforeReplacing"); }
        // Busy application.
        { var host = new FakeHost { Blocked = "ocupado" }; var spy = new Spy(); var r = Go(host, spy, ds);
          check(r.Outcome == OpenOutcome.Blocked && r.Message == "ocupado" && spy.Prepared == 0, "UniversalOpen_BusyApplicationOpensNothing"); }
        // The engine rejects: nothing is installed.
        { var host = new FakeHost(); var spy = new Spy { Behaviour = (_, _) => throw new InvalidDataException("tablas no reconocidas") }; var r = Go(host, spy, ds);
          check(r.Outcome == OpenOutcome.Rejected && r.Message.Contains("tablas no reconocidas") && !host.Calls.Contains("install"), "UniversalOpen_EngineRejection_KeepsThePreviousProject"); }
        // The engine opens something else than detected.
        { var host = new FakeHost(); var spy = new Spy { Behaviour = (d, _) => new PreparedProject(DetectedPlatform.NintendoSwitch, d, new object(), "Otro", () => { }) }; var r = Go(host, spy, ds);
          check(r.Outcome == OpenOutcome.Rejected && !host.Calls.Contains("install"), "UniversalOpen_PlatformMismatchFromTheEngine_IsNotInstalled"); }
        { var host = new FakeHost(); var spy = new Spy(); var r = Go(host, spy, ds, detect: _ => Det(DetectedPlatform.Nintendo3DS, DetectedKind.Extracted3dsFolder));
          check(r.Outcome == OpenOutcome.Rejected && spy.Prepared == 0, "UniversalOpen_EntryThatChangedAfterTheDialogAnalysedIt_IsRejected"); }
        { var host = new FakeHost(); var spy = new Spy(); var r = Go(host, spy, ds, detect: _ => Det(DetectedPlatform.NintendoDS, DetectedKind.DsRom, status: DetectionStatus.Incomplete));
          check(r.Outcome == OpenOutcome.Rejected && spy.Prepared == 0, "UniversalOpen_EntryThatBecameIncomplete_IsRejected"); }
        // The installation throws or declines: the prepared project is disposed, the host keeps the previous state.
        { var host = new FakeHost { InstallThrows = true }; var spy = new Spy(); var r = Go(host, spy, ds);
          check(r.Outcome == OpenOutcome.Rejected && r.Message.Contains("se conserva el anterior") && spy.Disposed == 1 && host.Installed is null, "UniversalOpen_InstallException_DisposesTheNewProjectAndKeepsTheOld"); }
        { var host = new FakeHost { InstallReturnsFalse = true }; var spy = new Spy(); var r = Go(host, spy, ds);
          check(r.Outcome == OpenOutcome.Cancelled && spy.Disposed == 1, "UniversalOpen_InstallDeclined_DisposesTheNewProject"); }
        { var host = new FakeHost(); var spy = new Spy(); Go(host, spy, ds);
          check(spy.Disposed == 0 && host.Installed!.Released, "UniversalOpen_InstalledProjectIsOwnedByTheHostNotDisposed"); }
        // Cancelled while the engine opens: the result is discarded.
        { var host = new FakeHost(); var gate = new ManualResetEventSlim(false); var spy = new Spy { Gate = gate }; using var cts = new CancellationTokenSource();
          var task = new ProjectOpenCoordinator(host, spy.Prepare, _ => ds).OpenAsync(ds, new OpenOptions(), cts.Token);
          Until(() => spy.Prepared == 1, "engine started"); cts.Cancel(); gate.Set(); var r = Wait(task);
          check(r.Outcome == OpenOutcome.Cancelled && spy.Disposed == 1 && !host.Calls.Contains("install"), "UniversalOpen_CancelledDuringTheOpening_DiscardsTheResult"); }
        // Demos and any recognised entry with a warning: the user must accept it first.
        var demo = Det(DetectedPlatform.Nintendo3DS, DetectedKind.Extracted3dsFolder, game: "Pokémon Sol/Luna (demo)", warning: "Es una DEMO.");
        { var host = new FakeHost { AcceptWarning = false }; var spy = new Spy(); var r = Go(host, spy, demo);
          check(r.Outcome == OpenOutcome.Cancelled && spy.Prepared == 0 && host.Calls.Contains("warning") && !host.Calls.Contains("install"), "UniversalOpen_DemoWithoutAcceptingTheWarning_OpensNothing"); }
        { var host = new FakeHost(); var spy = new Spy(); var r = Go(host, spy, demo);
          check(r.Opened && host.Calls.IndexOf("warning") < host.Calls.IndexOf("install") && spy.Prepared == 1, "UniversalOpen_DemoAcceptedByTheUser_IsTriedWithTheExistingEngine"); }
        { var host = new FakeHost(); var spy = new Spy { Behaviour = (_, _) => throw new InvalidDataException("code.bin comprimido o incompleto") }; var r = Go(host, spy, demo);
          check(r.Outcome == OpenOutcome.Rejected && r.Message.Contains("code.bin") && r.Message.Contains("demo") && !host.Calls.Contains("install"), "UniversalOpen_DemoRejectedByTheEngine_ShowsTheErrorAndKeepsThePrevious"); }
        { var host = new FakeHost(); var spy = new Spy(); Go(host, spy, g3);
          check(!host.Calls.Contains("warning"), "UniversalOpen_FullGameDoesNotShowAWarning"); }
        // Typed 3DS recovery: first without recovering; recovery only after the user accepted; the same type is not a text comparison.
        { var host = new FakeHost { AcceptRecovery = false }; var spy = new Spy { Behaviour = (_, o) => o.RecoverInterrupted3ds ? throw new InvalidOperationException("no debe recuperar") : throw new InterruptedSaveException("texto cualquiera", @"C:\x\pending") };
          var r = Go(host, spy, g3);
          check(r.Outcome == OpenOutcome.Cancelled && spy.Prepared == 1 && !spy.Options[0].RecoverInterrupted3ds && host.Calls.Contains("recovery") && host.RecoveryPath == @"C:\x\pending" && !host.Calls.Contains("install"), "UniversalOpen_3dsInterruptedSave_AsksAndDoesNotRecoverWhenDeclined"); }
        { var host = new FakeHost(); int calls = 0; var spy = new Spy { Behaviour = (d, o) => { calls++; if (!o.RecoverInterrupted3ds) throw new InterruptedSaveException("otro texto", "p"); return new PreparedProject(d.Platform, d, new object(), "XY", () => { }); } };
          var r = Go(host, spy, g3);
          check(r.Opened && spy.Prepared == 2 && !spy.Options[0].RecoverInterrupted3ds && spy.Options[1].RecoverInterrupted3ds && host.Calls.IndexOf("recovery") < host.Calls.IndexOf("install"), "UniversalOpen_3dsInterruptedSave_RecoversOnlyAfterTheUserAccepted"); }
        { var host = new FakeHost(); var spy = new Spy { Behaviour = (_, _) => throw new IOException("El proyecto tiene un guardado interrumpido. La lectura de diagnóstico no lo modifica; ábrelo en la aplicación para recuperarlo.") };
          var r = Go(host, spy, g3);
          check(r.Outcome == OpenOutcome.Rejected && spy.Prepared == 1 && !host.Calls.Contains("recovery"), "UniversalOpen_RecoveryIsRecognisedByTypeNotByTheMessageText"); }
        { var host = new FakeHost(); var spy = new Spy { Behaviour = (_, _) => throw new InterruptedSaveException("x", "p") }; var r = Go(host, spy, ds);
          check(r.Outcome == OpenOutcome.Rejected && !host.Calls.Contains("recovery"), "UniversalOpen_OnlyThe3dsEngineCanOfferRecovery"); }
        { var host = new FakeHost(); var spy = new Spy { Behaviour = (d, o) => o.RecoverInterrupted3ds ? throw new IOException("conflicto: hay archivos de otro programa") : throw new InterruptedSaveException("x", "p") };
          var r = Go(host, spy, g3);
          check(r.Outcome == OpenOutcome.Rejected && r.Message.Contains("conflicto") && spy.Prepared == 2 && !host.Calls.Contains("install"), "UniversalOpen_FailedRecovery_IsReportedAndNothingIsInstalled"); }
        // Options reach the engine untouched.
        { var host = new FakeHost(); var spy = new Spy(); Go(host, spy, ds, new OpenOptions(AllowRomhacks: true));
          check(spy.Options.Single().AllowRomhacks && !spy.Options.Single().RecoverInterrupted3ds, "UniversalOpen_RomhackOptionIsPassedToTheDsEngineOnly"); }
        // Checkpoints v1, v2 and v3 are Nintendo DS candidates opened with the DS engine, by their own path.
        string temp = Path.Combine(Path.GetTempPath(), "universal-open-cp-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string source = ProjectDetectorChecks.Write(temp, "Platino.nds", ProjectDetectorChecks.Rom("CPUE", 1));
            foreach (int v in new[] { 1, 2, 3 })
            {
                string path = ProjectDetectorChecks.Checkpoint(temp, $"v{v}.ndsevents.json", source, v);
                var detected = ProjectDetector.Detect(path); var host = new FakeHost(); var spy = new Spy(); var r = Wait(new ProjectOpenCoordinator(host, spy.Prepare).OpenAsync(detected, new OpenOptions(), token));
                check(detected.Kind == DetectedKind.DsEventCheckpoint && r.Opened && host.Installed!.Detection.Path == path && host.Installed.Platform == DetectedPlatform.NintendoDS, $"UniversalOpen_Checkpoint_V{v}_OpensWithTheDsEngineByItsOwnPath");
            }
        }
        finally { Directory.Delete(temp, true); }
    }

    // ------------------------------------------------------------------------------------------------- 3DS recovery on disposable copies

    private static string HistoryPending(string root)
    {
        var history = new ProjectHistory(root, "");
        return (string)typeof(ProjectHistory).GetField("_path", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(history)! + ".pending";
    }

    /// <summary>An interrupted save left behind by a process that died between two replacements: the first file already has the proposed bytes.</summary>
    private static (string A, string B, Action Dispose) Interrupt(string root, bool standalone)
    {
        string a = Path.Combine(root, "first.bin"), b = Path.Combine(root, "second.bin"); File.WriteAllBytes(a, [1, 2]); File.WriteAllBytes(b, [5, 6]);
        var files = new[] { new FileChange(a, [1, 2], [3, 4]), new FileChange(b, [5, 6], [7, 8]) };
        if (!standalone) Directory.CreateDirectory(Path.GetDirectoryName(HistoryPending(root))!);
        var journal = standalone ? StandaloneTransactions.Begin(files) : RecoveryJournal.Begin(HistoryPending(root), root, Path.Combine(root, "unused-history.json"), files);
        File.WriteAllBytes(a, [3, 4]); journal.Dispose();
        return (a, b, () => { });
    }

    private static void Recovery(string temp, Action<bool, string> check)
    {
        string state = Path.Combine(temp, "state"); string? previous = Environment.GetEnvironmentVariable("PROGRESSIVE_STATE_DIR");
        Environment.SetEnvironmentVariable("PROGRESSIVE_STATE_DIR", state);
        try
        {
            foreach (bool standalone in new[] { false, true })
            {
                string label = standalone ? "Standalone" : "History";
                string root = ProjectDetectorChecks.MakeDemoProject(temp, "recover-" + label, GameVersion.ORAS, 299), other = ProjectDetectorChecks.MakeDemoProject(temp, "bystander-" + label, GameVersion.ORAS, 299);
                var (a, b, _) = Interrupt(root, standalone); var (oa, ob, _) = Interrupt(other, standalone);
                var detected = ProjectDetector.Detect(root);
                check(detected is { Status: DetectionStatus.Compatible, Platform: DetectedPlatform.Nintendo3DS }, $"UniversalOpen_Recovery_{label}_ProjectWithInterruptedSaveIsStillACandidate");
                // The first try must not recover: the typed exception comes from the unmodified engine.
                var typed = Assert.Throws<InterruptedSaveException>(() => GameProjectSession.Open(root, recoverInterruptedSave: false));
                check(typed is not null && File.ReadAllBytes(a).SequenceEqual(new byte[] { 3, 4 }), $"UniversalOpen_Recovery_{label}_OpenWithoutRecoveryRaisesTheTypedExceptionAndWritesNothing");
                string snapshot = string.Join("|", Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x).Select(f => f + File.GetLastWriteTimeUtc(f).Ticks + new FileInfo(f).Length));
                // Declined: nothing changes.
                var declined = new FakeHost { Session = false, AcceptRecovery = false };
                var r = Wait(new ProjectOpenCoordinator(declined).OpenAsync(detected, new OpenOptions(), CancellationToken.None));
                string after = string.Join("|", Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x).Select(f => f + File.GetLastWriteTimeUtc(f).Ticks + new FileInfo(f).Length));
                check(r.Outcome == OpenOutcome.Cancelled && declined.Installed is null && snapshot == after && File.ReadAllBytes(a).SequenceEqual(new byte[] { 3, 4 }), $"UniversalOpen_Recovery_{label}_DecliningLeavesTheFolderExactlyAsItWas");
                // Accepted: only this project is recovered, and only after the confirmation.
                var accepted = new FakeHost { Session = false }; PreparedProject? opened = null;
                var coordinator = new ProjectOpenCoordinator(accepted);
                r = Wait(coordinator.OpenAsync(detected, new OpenOptions(), CancellationToken.None)); opened = accepted.Installed;
                check(r.Opened && opened?.Project is GameProjectSession && accepted.Calls.IndexOf("recovery") >= 0 && accepted.Calls.IndexOf("recovery") < accepted.Calls.IndexOf("install") && accepted.RecoveryPath is { Length: > 0 }, $"UniversalOpen_Recovery_{label}_AcceptedRecoveryOpensTheProjectWithTheRealEngine");
                check(File.ReadAllBytes(a).SequenceEqual(new byte[] { 1, 2 }) && File.ReadAllBytes(b).SequenceEqual(new byte[] { 5, 6 }), $"UniversalOpen_Recovery_{label}_RecoveryRestoresTheFilesOfTheNewProject");
                check(File.ReadAllBytes(oa).SequenceEqual(new byte[] { 3, 4 }) && File.ReadAllBytes(ob).SequenceEqual(new byte[] { 5, 6 }), $"UniversalOpen_Recovery_{label}_AnotherProjectsInterruptedSaveIsNotTouched");
                var again = Wait(new ProjectOpenCoordinator(new FakeHost { Session = false }).OpenAsync(ProjectDetector.Detect(root), new OpenOptions(), CancellationToken.None));
                check(again.Opened, $"UniversalOpen_Recovery_{label}_NoSecondConfirmationOnceRecovered");
            }
            // A clean 3DS project opens without any recovery question, and the real engine opens a demo on a disposable copy.
            string clean = ProjectDetectorChecks.MakeDemoProject(temp, "clean", GameVersion.ORAS, 299); var host = new FakeHost { Session = false };
            check(Wait(new ProjectOpenCoordinator(host).OpenAsync(ProjectDetector.Detect(clean), new OpenOptions(), CancellationToken.None)).Opened && !host.Calls.Contains("recovery"), "UniversalOpen_Recovery_CleanProjectOpensWithoutQuestions");
        }
        finally { Environment.SetEnvironmentVariable("PROGRESSIVE_STATE_DIR", previous); }
    }

    private static class Assert
    {
        internal static T? Throws<T>(Action action) where T : Exception { try { action(); } catch (T e) { return e; } return null; }
    }

    // ------------------------------------------------------------------------------------------------------------------------- dialog races

    private static void DialogRaces(Action<bool, string> check)
    {
        var ds = Det(DetectedPlatform.NintendoDS, DetectedKind.DsRom, "A", "Pokémon A"); var other = Det(DetectedPlatform.Nintendo3DS, DetectedKind.Extracted3dsFolder, "B", "Pokémon B");
        // A slow detection of the old path never overwrites the result of the newer one.
        var gate = new ManualResetEventSlim(false); var calls = new List<string>();
        ProjectDetection Detect(string? path) { lock (calls) calls.Add(path ?? ""); if (path == "A") gate.Wait(10000); return path == "A" ? ds : other; }
        var spy = new Spy(); var host = new FakeHost(); var dialog = Show(new OpenProjectDialog(null, new ProjectOpenCoordinator(host, spy.Prepare, Detect), detect: Detect));
        dialog.PathText = "A"; dialog.PathText = "B"; Until(() => dialog.Current is not null, "detection of B");
        check(dialog.Current!.Path == "B" && dialog.GameText == "Pokémon B" && dialog.PlatformText == "Nintendo 3DS" && dialog.IsOpenEnabled, "UniversalOpenDialog_NewestPathWinsWhileTheOldDetectionIsStillRunning");
        gate.Set(); Wait(dialog.DetectionTask); Thread.Sleep(150); for (int i = 0; i < 30; i++) Spin();
        check(dialog.Current!.Path == "B" && dialog.GameText == "Pokémon B" && dialog.IsOpenEnabled, "UniversalOpenDialog_LateResultOfTheOldPathIsIgnored");
        // A candidate that becomes stale: editing the text disables "Abrir" at once.
        dialog.PathText = "C"; check(!dialog.IsOpenEnabled && dialog.Current is null, "UniversalOpenDialog_EditingThePathDisablesOpenImmediately");
        Until(() => dialog.Current is not null, "detection of C");
        // Several fast edits: only the last one counts.
        dialog.PathText = "A1"; dialog.PathText = "A2"; dialog.PathText = "B"; Until(() => dialog.Current?.Path == "B" && dialog.IsOpenEnabled, "final detection");
        lock (calls) check(calls.Count(c => c == "B") >= 1 && dialog.Current!.Path == "B", "UniversalOpenDialog_RapidEditsEndOnTheLastPath");
        // An incompatible, ambiguous or missing path never enables "Abrir".
        foreach (var status in new[] { DetectionStatus.Incomplete, DetectionStatus.Ambiguous, DetectionStatus.Unsupported, DetectionStatus.Inaccessible, DetectionStatus.Unknown })
        {
            var d = new OpenProjectDialog(null, new ProjectOpenCoordinator(host, spy.Prepare), detect: _ => Det(DetectedPlatform.None, DetectedKind.None, "z", "", status: status, message: "no sirve"));
            Show(d); d.PathText = "z"; Wait(d.DetectionTask);
            check(!d.IsOpenEnabled && d.MessageText.Contains("no sirve"), "UniversalOpenDialog_" + status + "_DisablesOpenAndExplains"); d.Close();
        }
        dialog.Close();

        // Cancelling and closing during the opening discard the project and keep the previous one.
        foreach (string how in new[] { "Cancelar", "CerrarVentana", "BotonCerrar" })
        {
            var holdGate = new ManualResetEventSlim(false); var s = new Spy { Gate = holdGate }; var h = new FakeHost();
            var d = Show(new OpenProjectDialog(null, new ProjectOpenCoordinator(h, s.Prepare, _ => ds), detect: _ => ds)); d.PathText = "A"; Wait(d.DetectionTask);
            Click((Button)ById(d, "OpenProjectOpen")); Until(() => s.Prepared == 1 && d.Busy, "opening started");
            check(!d.IsOpenEnabled, "UniversalOpenDialog_" + how + "_OpenIsDisabledWhileOpening");
            if (how == "Cancelar") Click((Button)ById(d, "OpenProjectCancel")); else if (how == "BotonCerrar") Click((Button)ById(d, "OpenProjectClose")); else d.Close();
            holdGate.Set(); Wait(d.OpeningTask); for (int i = 0; i < 20; i++) Spin();
            check(!d.Succeeded && s.Disposed == 1 && h.Installed is null && !h.Calls.Contains("install"), "UniversalOpenDialog_" + how + "_DuringTheOpening_DiscardsTheProjectAndInstallsNothing");
        }
        // A failed opening keeps the dialog open with the error and lets the user try again.
        { var h = new FakeHost(); var s = new Spy { Behaviour = (_, _) => throw new InvalidDataException("la ROM no coincide") };
          var d = Show(new OpenProjectDialog(null, new ProjectOpenCoordinator(h, s.Prepare, _ => ds), detect: _ => ds)); d.PathText = "A"; Wait(d.DetectionTask);
          Click((Button)ById(d, "OpenProjectOpen")); Wait(d.OpeningTask);
          check(!d.Succeeded && d.IsOpenEnabled && d.ResultText.Contains("la ROM no coincide") && !d.Busy, "UniversalOpenDialog_RejectedOpening_StaysOpenWithTheErrorAndCanRetry");
          s.Behaviour = null; Click((Button)ById(d, "OpenProjectOpen")); Wait(d.OpeningTask);
          check(d.Succeeded && h.Installed is not null, "UniversalOpenDialog_RetryAfterARejectedOpeningInstalls"); }
        // Keyboard: Enter opens only a candidate; the demo warning is on screen before opening.
        { var h = new FakeHost(); var s = new Spy(); var demo = Det(DetectedPlatform.Nintendo3DS, DetectedKind.Extracted3dsFolder, "D", "Pokémon Sol/Luna (demo)", "Es una DEMO. Trabaja sobre una copia.");
          var d = Show(new OpenProjectDialog(null, new ProjectOpenCoordinator(h, s.Prepare, _ => demo), detect: _ => demo)); d.PathText = "D"; Wait(d.DetectionTask);
          check(d.WarningText.Contains("DEMO") && d.IsOpenEnabled && d.GameText.Contains("demo"), "UniversalOpenDialog_DemoShowsItsWarningBeforeOpening");
          Click((Button)ById(d, "OpenProjectOpen")); Wait(d.OpeningTask);
          check(h.Calls.Contains("warning") && d.Succeeded, "UniversalOpenDialog_DemoOpeningAsksForConfirmation"); }
    }

    // ---------------------------------------------------------------------------------------------------- real window, real engines

    private static string RomFile(string folder, string name) { string path = Path.Combine(folder, name); File.WriteAllBytes(path, NdsGen5RandomizerChecks.SyntheticBlackRomhack()); return path; }

    private static OpenResult OpenWith(MainWindow main, string path, bool romhacks = false)
    {
        var dialog = Show(new OpenProjectDialog(main, main.CreateOpenCoordinator()));
        try
        {
            dialog.AllowRomhacks = romhacks; dialog.PathText = path; Wait(dialog.DetectionTask);
            if (!dialog.IsOpenEnabled) return new(OpenOutcome.Rejected, "El diálogo no habilita Abrir: " + dialog.MessageText);
            Click((Button)ById(dialog, "OpenProjectOpen")); Wait(dialog.OpeningTask);
            return dialog.LastResult ?? new(OpenOutcome.Rejected, "sin resultado");
        }
        finally { dialog.Close(); }
    }

    private static void Window(string temp, Action<bool, string> check)
    {
        string state = Path.Combine(temp, "state"); string? previousState = Environment.GetEnvironmentVariable("PROGRESSIVE_STATE_DIR"); Environment.SetEnvironmentVariable("PROGRESSIVE_STATE_DIR", state);
        string? answerFor = null; var asked = new List<string>(); bool answer = true;
        var main = new MainWindow();
        main.OpenPromptForChecks = (kind, text) => { asked.Add(kind + ": " + text); return answer; };
        try
        {
            string dsA = RomFile(temp, "A.nds"), dsB = RomFile(temp, "B.nds");
            string sword = SwordShieldChecks.Fixture(temp, "sword"), shield = SwordShieldChecks.Fixture(temp, "shield", true);
            string g1 = ProjectDetectorChecks.MakeDemoProject(temp, "oras-a", GameVersion.ORAS, 299), g2 = ProjectDetectorChecks.MakeDemoProject(temp, "oras-b", GameVersion.ORAS, 299);
            bool NdsOn() => main.SwitchHost_VisibleContent() is NdsWorkspaceView;
            NdsProject Ds() => main.NdsWorkspace!.Project!;

            // The first open does not need any platform to be chosen before.
            var r = OpenWith(main, dsA, romhacks: true);
            check(r.Opened && NdsOn() && Ds() is { IsRomhack: true }, "UniversalOpenWindow_OpensDsFromAnEmptyApplicationWithoutChoosingAPlatform");
            var trainers = new NdsRecordEditor(Ds(), NdsSection.Trainers); var values = trainers.Read(1).SelectMany(g => g.Fields).ToDictionary(f => f.Key, f => f.Value); values["P0.Species"] = 25; values["P0.Level"] = 33; trainers.Save(1, values);
            var dsFirst = Ds(); int ops = dsFirst.OperationCount;
            // The universal flow opens a DS ROM with its persistent project: the work is already saved, so there is nothing pending to lose.
            check(dsFirst is { HasChanges: true, IsPersistent: true, SavedGeneration: 2 } && File.Exists(dsA + ".drproj") && main.InspectForChecks(DetectedPlatform.NintendoDS) is { HasSession: true, HasPendingChanges: false }, "UniversalOpenWindow_ADsRomOpensWithItsSavedProjectAndHasNothingPendingToLose");

            // DS → 3DS: the DS project and its changes stay.
            r = OpenWith(main, g1);
            var session1 = main.Session;
            check(r.Opened && session1 is not null && !NdsOn() && Ds() == dsFirst && dsFirst.HasChanges && dsFirst.OperationCount == ops, "UniversalOpenWindow_DsTo3ds_KeepsTheDsProjectAndItsChanges");
            // 3DS → Switch.
            r = OpenWith(main, sword);
            var swordProject = main.SwitchWorkspace?.Project;
            check(r.Opened && swordProject is not null && swordProject.Identity.Game.Contains("Espada") && main.Session == session1 && Ds() == dsFirst, "UniversalOpenWindow_3dsToSwitch_KeepsThe3dsAndDsProjects");
            // Switch → DS: the DS project on screen is saved, so replacing it asks nothing (answering "no" to anything would cancel) and loses nothing.
            string before = main.HostDescription(); asked.Clear(); answer = false;
            r = OpenWith(main, dsB, romhacks: true);
            check(r.Opened && Ds() != dsFirst && Ds().SourcePath.EndsWith("B.nds") && Ds().IsPersistent && File.Exists(dsB + ".drproj") && NdsOn() && !asked.Any(a => a.StartsWith("replace")) && main.SwitchWorkspace!.Project == swordProject && main.Session == session1, "UniversalOpenWindow_SwitchToDs_ASavedDsProjectIsReplacedWithoutAskingAndTheOthersStay");
            // A DS session WITHOUT a project (the direct, in-memory load) still has changes that would be lost: cancelling keeps everything exactly.
            Wait(main.NdsWorkspace!.LoadAsync(RomFile(temp, "memoria.nds")));
            var unsaved = Ds(); var memory = new NdsRecordEditor(unsaved, NdsSection.Trainers); var memoryValues = memory.Read(1).SelectMany(g => g.Fields).ToDictionary(f => f.Key, f => f.Value); memoryValues["P0.Level"] = 44; memory.Save(1, memoryValues);
            before = main.HostDescription(); asked.Clear(); answer = false; r = OpenWith(main, dsB, romhacks: true);
            check(!unsaved.IsPersistent && main.InspectForChecks(DetectedPlatform.NintendoDS) is { HasPendingChanges: true } && r.Outcome == OpenOutcome.Cancelled && Ds() == unsaved && unsaved.HasChanges && main.HostDescription() == before && asked.Any(a => a.StartsWith("replace")), "UniversalOpenWindow_ADsSessionWithoutAProject_CancelWithPendingChangesKeepsEverything");
            answer = true; r = OpenWith(main, dsB, romhacks: true);
            check(r.Opened && Ds() != unsaved && Ds().SourcePath.EndsWith("B.nds") && NdsOn() && main.SwitchWorkspace!.Project == swordProject && main.Session == session1, "UniversalOpenWindow_SwitchToDs_ConfirmedReplacementInstallsTheNewDsAndKeepsTheOthers");
            // Same platform: Switch → Switch (no pending changes: no question).
            asked.Clear(); r = OpenWith(main, shield);
            check(r.Opened && main.SwitchWorkspace!.Project!.Identity.Game.Contains("Escudo") && main.SwitchWorkspace.Project != swordProject && !asked.Any(a => a.StartsWith("replace")), "UniversalOpenWindow_SwitchReplacedBySwitch_NoQuestionWithoutPendingChanges");
            // Switch → 3DS with an unsaved form: declined keeps the session and the form; accepted replaces.
            ((TextBox)main.FindName("HpBox")).Text = "77";
            check(main.InspectForChecks(DetectedPlatform.Nintendo3DS) is { HasSession: true, HasPendingChanges: true }, "UniversalOpenWindow_HostReportsUnsaved3dsForm");
            answer = false; before = main.HostDescription(); r = OpenWith(main, g2);
            check(r.Outcome == OpenOutcome.Cancelled && main.Session == session1 && ((TextBox)main.FindName("HpBox")).Text == "77" && main.HostDescription() == before, "UniversalOpenWindow_SwitchTo3ds_CancelKeepsTheSessionAndTheUnsavedForm");
            answer = true; r = OpenWith(main, g2);
            var s2 = main.Session;
            check(r.Opened && s2 is not null && s2 != session1 && main.SwitchHost_VisibleContent() is null, "UniversalOpenWindow_SwitchTo3ds_ConfirmedReplacementShowsThe3dsEditor");
            // 3DS → DS.
            r = OpenWith(main, dsA, romhacks: true);
            check(r.Opened && NdsOn() && Ds().SourcePath.EndsWith("A.nds") && main.Session == s2 && main.SwitchWorkspace!.Project!.Identity.Game.Contains("Escudo"), "UniversalOpenWindow_3dsToDs_KeepsThe3dsAndSwitchProjects");
            // The ROM opened first comes back with the work that was done on it: it was resumed from its project, not opened empty.
            check(Ds() != dsFirst && Ds() is { IsPersistent: true, HasChanges: true, SavedGeneration: 2 } && Ds().OperationCount == ops && new NdsRecordEditor(Ds(), NdsSection.Trainers).Read(1).SelectMany(g => g.Fields).ToDictionary(f => f.Key, f => f.Value) is var resumed && resumed["P0.Species"] == 25 && resumed["P0.Level"] == 33, "UniversalOpenWindow_ReopeningADsRomResumesItsSavedWork");
            // DS → DS with no pending changes, and 3DS → 3DS, Switch → Switch were covered above.
            asked.Clear(); r = OpenWith(main, dsB, romhacks: true);
            check(r.Opened && !asked.Any(a => a.StartsWith("replace")), "UniversalOpenWindow_DsReplacedByDs_NoQuestionWithoutPendingChanges");

            // Drag and drop (B3): what is dropped goes through the same detector, engines and confirmations as the dialog.
            OpenResult Drop(params string[] paths) { var task = main.HandleDropAsync(paths); Wait(task); return task.Result; }
            var dsBefore = Ds(); string dropRom = RomFile(temp, "soltada.nds"); main.NdsWorkspace!.AllowRomhacks = true;
            r = Drop(dropRom);
            check(r.Opened && NdsOn() && Ds() != dsBefore && Ds().SourcePath == dropRom && Ds().IsPersistent && File.Exists(dropRom + ".drproj") && main.AllowDrop, "UniversalOpenDrop_ADsRomDroppedOnTheWindowOpensWithItsProject");
            var edited = new NdsRecordEditor(Ds(), NdsSection.Trainers); var editedValues = edited.Read(1).SelectMany(g => g.Fields).ToDictionary(f => f.Key, f => f.Value); editedValues["P0.Level"] = 41; edited.Save(1, editedValues); int dropOps = Ds().OperationCount;
            r = Drop(dsB);
            check(r.Opened && Ds().SourcePath.EndsWith("B.nds"), "UniversalOpenDrop_AnotherRomReplacesTheSavedProjectWithoutAsking");
            r = Drop(dropRom + ".drproj");
            check(r.Opened && Ds().SourcePath == dropRom && Ds().OperationCount == dropOps && Ds().IsRomhack, "UniversalOpenDrop_AProjectFileDroppedResumesItsWork");
            r = Drop(g1);
            check(r.Opened && main.Session is not null && main.Session != s2 && !NdsOn(), "UniversalOpenDrop_A3dsFolderDroppedOpensThe3dsProject");
            var s3 = main.Session; r = Drop(shield);
            check(r.Opened && main.SwitchWorkspace!.Project!.Identity.Game.Contains("Escudo") && main.Session == s3, "UniversalOpenDrop_ASwitchExtractionDroppedOpensIt");
            // What cannot be opened changes nothing and says why.
            string screen = main.HostDescription(); var dsKept = Ds(); var swKept = main.SwitchWorkspace!.Project; string note = Path.Combine(temp, "notas.txt"); File.WriteAllText(note, "no es un proyecto");
            r = Drop(dsA, dsB);
            check(r.Outcome == OpenOutcome.Rejected && r.Message.Contains("un solo archivo") && main.HostDescription().Replace(r.Message, "") == screen.Replace(r.Message, "") && Ds() == dsKept, "UniversalOpenDrop_SeveralItemsAreRefusedAndNothingChanges");
            r = Drop(note);
            check(r.Outcome == OpenOutcome.Rejected && Ds() == dsKept && main.SwitchWorkspace!.Project == swKept && main.Session == s3, "UniversalOpenDrop_AnUnrecognisedFileIsRefusedAndNothingChanges");
            r = Drop(Path.Combine(temp, "no-existe.nds"));
            check(r.Outcome == OpenOutcome.Rejected && Ds() == dsKept, "UniversalOpenDrop_AMissingPathIsRefused");
            r = Drop();
            check(r.Outcome == OpenOutcome.Rejected && Ds() == dsKept, "UniversalOpenDrop_AnEmptyDropIsRefused");
            using (UiProgress.BlockInteraction()) { r = Drop(dsA); check(r.Outcome == OpenOutcome.Blocked && Ds() == dsKept && main.DropProblem([dsA]) is not null, "UniversalOpenDrop_NothingIsOpenedWhileAnOperationIsRunning"); }
            check(main.DropProblem([dsA]) is null && main.DropProblem([dsA, dsB]) is not null && main.DropProblem(null) is not null, "UniversalOpenDrop_TheCursorShowsWhetherTheDropWillBeAccepted");

            // Rejections never disturb the previous projects or the screen.
            var dsNow = Ds(); var sessionNow = main.Session; var switchNow = main.SwitchWorkspace!.Project; before = main.HostDescription();
            string fakeRom = Path.Combine(temp, "fake.nds"); File.WriteAllBytes(fakeRom, NdsGen5RandomizerChecks.BuildRom(new Dictionary<string, byte[]> { ["a/0/0/2"] = NdsGen5RandomizerChecks.BuildNarc([]) }, "IRBO"));
            r = OpenWith(main, fakeRom);
            check(r.Outcome == OpenOutcome.Rejected && Ds() == dsNow && main.HostDescription() == before, "UniversalOpenWindow_DsRomWhoseTablesDoNotMatch_IsRejectedAndTheProjectStays");
            string badDemo = ProjectDetectorChecks.MakeDemoProject(temp, "demo-sin-tabla", GameVersion.ORASDEMO, 301, machineTable: false); asked.Clear();
            r = OpenWith(main, badDemo);
            check(r.Outcome == OpenOutcome.Rejected && asked.Any(a => a.StartsWith("warning")) && r.Message.Contains("code.bin") && main.Session == sessionNow && main.HostDescription() == before, "UniversalOpenWindow_DemoRejectedByTheEngine_ShowsTheErrorAndKeepsTheProject");
            string brokenSwitch = SwordShieldChecks.Fixture(temp, "roto"); File.WriteAllBytes(Path.Combine(brokenSwitch, "romfs", SwordShieldProject.PersonalRelativePath), [1, 2, 3]);
            r = OpenWith(main, brokenSwitch);
            check(r.Outcome == OpenOutcome.Rejected && main.SwitchWorkspace!.Project == switchNow && main.HostDescription() == before, "UniversalOpenWindow_SwitchExtractionWithBrokenData_IsRejectedAndTheProjectStays");

            // Not candidates: the dialog explains and the button stays disabled.
            string incomplete = ProjectDetectorChecks.Make3ds(temp, "sin-exefs", 299, exe: false), both = ProjectDetectorChecks.Make3ds(temp, "ambigua", 299); ProjectDetectorChecks.MakeSwitchInto(both);
            foreach (var (label, path, text) in new[] { ("IncompleteFolder", incomplete, "Incompleto"), ("AmbiguousFolder", both, "Ambiguo"), ("MissingPath", Path.Combine(temp, "no-existe"), "No accesible") })
            {
                var d = Show(new OpenProjectDialog(main, main.CreateOpenCoordinator())); d.PathText = path; Wait(d.DetectionTask);
                check(!d.IsOpenEnabled && d.MessageText.Contains(text), "UniversalOpenWindow_" + label + "_DisablesOpen"); d.Close();
            }
            check(main.HostDescription() == before, "UniversalOpenWindow_NonCandidatesChangeNothing");

            // Demos: declining the warning opens nothing; accepting tries the engine and installs.
            string demo = ProjectDetectorChecks.MakeDemoProject(temp, "demo-oras", GameVersion.ORASDEMO, 301);
            answer = false; asked.Clear(); r = OpenWith(main, demo);
            check(r.Outcome == OpenOutcome.Cancelled && asked.Count(a => a.StartsWith("warning")) == 1 && main.Session == sessionNow, "UniversalOpenWindow_DemoWarningDeclined_OpensNothing");
            answer = true; r = OpenWith(main, demo);
            check(r.Opened && main.Session != sessionNow && main.Session!.GameName.Length > 0, "UniversalOpenWindow_DemoWarningAccepted_OpensWithTheExistingEngine");

            // Failures while installing, at both risky moments, for every platform: the previous screen and projects come back.
            InstallFaults(main, temp, dsB, sword, g1, check, () => answer = true);

            // The old per-platform accesses are still there.
            check(typeof(MainWindow).GetMethod("OpenProjectByPlatform_Click", BindingFlags.NonPublic | BindingFlags.Instance) is not null && typeof(NdsWorkspaceView).GetMethod("OpenAsync", BindingFlags.NonPublic | BindingFlags.Instance) is not null && typeof(SwitchWorkspaceView).GetMethod("OpenAsync", BindingFlags.NonPublic | BindingFlags.Instance) is not null, "UniversalOpenWindow_PerPlatformAccessesRemainAsAlternatives");
            var fresh = new MainWindow(); var titles = fresh.PaletteEntries().Select(e => e.Title).ToArray();
            check(titles.Contains("Abrir proyecto…") && titles.Contains("Abrir por plataforma…") && titles.Contains("Espada/Escudo · editor Switch") && titles.Any(t => t.StartsWith("Nintendo DS")), "UniversalOpenWindow_PaletteOffersTheUniversalActionAndThePlatformEntries");
            // The header button opens the same dialog whatever platform is on screen.
            main.Dispatcher.BeginInvoke(() => { var open = main.OpenDialog; check(open is not null && open.Title == "Abrir proyecto", "UniversalOpenWindow_HeaderButtonOpensTheUniversalDialogWhateverIsOnScreen"); open?.Close(); }, System.Windows.Threading.DispatcherPriority.Background);
            Click((Button)main.FindName("OpenProjectButton"));
            check(main.OpenDialog is null, "UniversalOpenWindow_DialogIsReleasedWhenItCloses");
            fresh.Close();
        }
        finally { Environment.SetEnvironmentVariable("PROGRESSIVE_STATE_DIR", previousState); main.Close(); _ = answerFor; }
    }

    private static void InstallFaults(MainWindow main, string temp, string dsPath, string switchPath, string g3Path, Action<bool, string> check, Action allow)
    {
        // Put every platform in a known state, then fail the next installation of each.
        allow(); OpenWith(main, switchPath); OpenWith(main, g3Path); OpenWith(main, dsPath, romhacks: true);
        foreach (var (platform, path, stages) in new[] { ("Ds", RomFile(temp, "fault.nds"), new[] { "host", "project" }), ("Switch", SwordShieldChecks.Fixture(temp, "fault-switch", true), new[] { "host", "project" }), ("3ds", ProjectDetectorChecks.MakeDemoProject(temp, "fault-oras", GameVersion.ORAS, 299), new[] { "project" }) })
            foreach (string stage in stages)
            {
                var ds = main.NdsWorkspace!.Project; var sw = main.SwitchWorkspace!.Project; var s3 = main.Session; string screen = main.HostDescription();
                main.InstallFaultForChecks = s => { if (s == stage) throw new IOException("fallo inyectado en " + s); };
                var r = OpenWith(main, path, romhacks: true);
                main.InstallFaultForChecks = null;
                bool screenBack = platform == "3ds" || main.HostDescription() == screen;
                if (!(r.Outcome == OpenOutcome.Rejected && main.NdsWorkspace!.Project == ds && main.SwitchWorkspace!.Project == sw && main.Session == s3 && screenBack))
                { Console.WriteLine($"DIAG {platform}/{stage}: {r.Outcome} '{r.Message}' ds={main.NdsWorkspace!.Project == ds} sw={main.SwitchWorkspace!.Project == sw} s3={main.Session == s3}"); Console.WriteLine("  before=" + screen); Console.WriteLine("  after =" + main.HostDescription()); }
                check(r.Outcome == OpenOutcome.Rejected && r.Message.Contains("fallo inyectado") && main.NdsWorkspace!.Project == ds && main.SwitchWorkspace!.Project == sw && main.Session == s3 && screenBack, $"UniversalOpenWindow_{platform}_InstallFailureAt_{stage}_RestoresThePreviousScreenAndProjects");
                if (platform == "3ds") check(main.HostDescription().Split('|')[..4].SequenceEqual(screen.Split('|')[..4]), $"UniversalOpenWindow_{platform}_InstallFailureAt_{stage}_ShowsTheSameWorkspaceAsBefore");
            }
        var again = OpenWith(main, RomFile(temp, "after.nds"), romhacks: true);
        check(again.Opened, "UniversalOpenWindow_AfterInstallFailuresTheApplicationOpensNormally");
    }
}
