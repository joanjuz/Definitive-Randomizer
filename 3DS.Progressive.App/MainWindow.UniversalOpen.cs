using System.IO;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.ProjectDetection;
using ThreeDS.Progressive.Services.Switch;

namespace ThreeDS.Progressive;

// Universal "Abrir proyecto" (phase B2): one action for every platform. The platform's session is only replaced after the new project was opened,
// verified and shown; any failure restores the previous screen, header and project.
public partial class MainWindow
{
    private OpenProjectDialog? _openDialog;
    /// <summary>Checks only: raised at the riskiest moments of an installation ("host" = after the view swap, "project" = after the project swap).</summary>
    internal Action<string>? InstallFaultForChecks;
    /// <summary>Checks only: answers the confirmations ("replace", "warning", "recovery", "leave") instead of showing them.</summary>
    internal Func<string, string, bool>? OpenPromptForChecks;

    internal GameProjectSession? Session => _session;
    internal object? SwitchHost_VisibleContent() => SwitchHost.Visibility == Visibility.Visible ? SwitchHost.Content : null;
    internal SwitchWorkspaceView? SwitchWorkspace => _switchWorkspace;
    internal OpenProjectDialog? OpenDialog => _openDialog;
    /// <summary>Checks only: what is on screen (hosts, header) as one comparable text.</summary>
    internal string HostDescription() => string.Join("|", HomeHost.Visibility, EditorShell.Visibility, SwitchHost.Visibility, SwitchHost.Content?.GetType().Name, ProjectNameText.Text, GameNameText.Text, RebuildButton.Content, OpenProjectButton.IsEnabled);
    internal ReplacementInfo InspectForChecks(DetectedPlatform platform) => new WpfProjectOpenHost(this).Inspect(platform);

    internal ProjectOpenCoordinator CreateOpenCoordinator(Func<ProjectDetection, OpenOptions, PreparedProject>? prepare = null, Func<string?, ProjectDetection>? detect = null) =>
        new(new WpfProjectOpenHost(this), prepare, detect);

    /// <summary>Shows the universal dialog (modal). The platform currently on screen does not matter.</summary>
    internal void OpenUniversalProject()
    {
        if (_closed || UiProgress.IsInteractionBlocked) return;
        if (_openDialog is not null) { _openDialog.Activate(); return; }
        var host = new WpfProjectOpenHost(this);
        var dialog = _openDialog = new OpenProjectDialog(this, new ProjectOpenCoordinator(host), _ndsWorkspace?.AllowRomhacks == true);
        host.PromptOwner = dialog;
        dialog.Closed += (_, _) =>
        {
            _openDialog = null;
            if (dialog.Succeeded && dialog.LastResult is { Platform: DetectedPlatform.Nintendo3DS } && dialog.Current is { } opened && Directory.Exists(opened.Path))
            { var preferences = AppPreferences.Load(); preferences.DefaultProjectFolder = opened.Path; preferences.Save(); }
        };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Opens one path through the universal flow without showing the dialog: the same detector, the same engines and the same confirmations. Used by the
    /// per-platform "open" of the DS workspace, so that a ROM opened from there gets its persistent project exactly like one opened from the dialog.
    /// </summary>
    internal async Task<OpenResult> OpenPathAsync(string path)
    {
        if (_closed) return new(OpenOutcome.Blocked, "La aplicación se está cerrando.");
        var detection = await Task.Run(() => ProjectDetector.Detect(path));
        var result = detection.IsCandidate
            ? await new ProjectOpenCoordinator(new WpfProjectOpenHost(this)).OpenAsync(detection, new OpenOptions(_ndsWorkspace?.AllowRomhacks == true), CancellationToken.None)
            : new OpenResult(OpenOutcome.Rejected, detection.Message, detection.Platform);
        if (result.Opened) RecentProjects.Record(detection.Path, detection.Game, detection.PlatformName);
        if (!result.Opened)
        {
            StatusText.Text = result.Message;
            if (result.Outcome != OpenOutcome.Cancelled && !StudioDialog.AutomatedChecks) StudioDialog.Error(this, "Abrir proyecto", result.Message);
        }
        return result;
    }

    // Drag and drop (phase B3): one file or folder dropped on the window is opened exactly like a path chosen in "Abrir proyecto". Nothing is decided here:
    // the detector says what it is, the engines open it, and the same confirmations are asked.
    private void InitializeDrop()
    {
        AllowDrop = true;
        PreviewDragOver += (_, e) => { if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return; e.Effects = DropProblem(e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[]) is null ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None; e.Handled = true; };
        PreviewDrop += async (_, e) => { if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return; e.Handled = true; await HandleDropAsync(e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[]); };
    }

    /// <summary>Why what is being dragged cannot be dropped now, or null. Only what can be known without reading anything: the detector judges the content after the drop.</summary>
    internal string? DropProblem(string[]? paths)
    {
        if (_closed) return "La aplicación se está cerrando.";
        if (UiProgress.IsInteractionBlocked) return "Hay una operación en curso. Espera a que termine para abrir otro proyecto.";
        if (_openDialog is not null) return "Ya hay un diálogo «Abrir proyecto» abierto: usa ese diálogo.";
        if (paths is null || paths.Length == 0) return "No se soltó ningún archivo ni carpeta.";
        if (paths.Length > 1) return "Suelta un solo archivo o una sola carpeta: se abre un proyecto cada vez.";
        return null;
    }

    /// <summary>Opens what was dropped through the universal flow. A drop that cannot be opened changes nothing and says why.</summary>
    internal async Task<OpenResult> HandleDropAsync(string[]? paths)
    {
        if (DropProblem(paths) is { } problem)
        {
            StatusText.Text = problem;
            return new(UiProgress.IsInteractionBlocked || _closed || _openDialog is not null ? OpenOutcome.Blocked : OpenOutcome.Rejected, problem);
        }
        return await OpenPathAsync(paths![0]);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e) => OpenUniversalProject();

    private bool Ask(Window? owner, string kind, string title, string message, string? details = null) =>
        OpenPromptForChecks is { } hook ? hook(kind, message + (details is null ? "" : "\n" + details)) : StudioDialog.Confirm(owner ?? this, title, message, details);

    private sealed record HostState(Visibility Home, Visibility Editor, Visibility Switch, object? SwitchContent, NdsWorkspaceView? Nds, SwitchWorkspaceView? SwitchView,
        Dictionary<Button, bool>? Header, Dictionary<Button, Visibility>? HeaderVisibility, object? RebuildLabel, string? ProjectLabel, string? GameLabel,
        string ProjectName, string GameName, string Status, object? RebuildContent, (Button Button, Visibility Visibility, bool Enabled)[] Buttons);

    private HostState CaptureHostState() => new(HomeHost.Visibility, EditorShell.Visibility, SwitchHost.Visibility, SwitchHost.Content, _ndsWorkspace, _switchWorkspace,
        _switchHeader is null ? null : new(_switchHeader), _switchHeaderVisibility is null ? null : new(_switchHeaderVisibility), _switchRebuildLabel, _switchProjectLabel, _switchGameLabel,
        ProjectNameText.Text, GameNameText.Text, StatusText.Text, RebuildButton.Content,
        new[] { BackupButton, RebuildButton, ExtractRomButton, RestoreBackupButton, OpenProjectButton }.Select(b => (b, b.Visibility, b.IsEnabled)).ToArray());

    private void RestoreHostState(HostState s)
    {
        HomeHost.Visibility = s.Home; EditorShell.Visibility = s.Editor; SwitchHost.Content = s.SwitchContent; SwitchHost.Visibility = s.Switch;
        _ndsWorkspace = s.Nds; _switchWorkspace = s.SwitchView; _switchHeader = s.Header; _switchHeaderVisibility = s.HeaderVisibility;
        _switchRebuildLabel = s.RebuildLabel; _switchProjectLabel = s.ProjectLabel; _switchGameLabel = s.GameLabel;
        foreach (var (button, visibility, enabled) in s.Buttons) { button.Visibility = visibility; button.IsEnabled = enabled; }
        RebuildButton.Content = s.RebuildContent; ProjectNameText.Text = s.ProjectName; GameNameText.Text = s.GameName; StatusText.Text = s.Status;
    }

    private async Task InstallNdsAsync(PreparedProject prepared, OpenOptions options)
    {
        var state = CaptureHostState(); var project = (NdsProject)prepared.Project; NdsProject? replaced;
        try
        {
            ShowNdsWorkspace(leaveConfirmed: true);
            if (!NdsVisible || _ndsWorkspace is null) throw new InvalidOperationException("No se pudo mostrar el editor de Nintendo DS.");
            InstallFaultForChecks?.Invoke("host");
            replaced = await _ndsWorkspace.InstallAsync(project, options.AllowRomhacks, () => InstallFaultForChecks?.Invoke("project"));
            RefreshNdsHeader();
        }
        catch { RestoreHostState(state); throw; }
        prepared.Release(); replaced?.Dispose();
    }

    private void InstallSwitch(PreparedProject prepared)
    {
        var state = CaptureHostState(); var project = (SwordShieldProject)prepared.Project; var previous = _switchWorkspace?.Project;
        try
        {
            OpenSwitchPrototype();
            if (SwitchHost.Visibility != Visibility.Visible || _switchWorkspace is null || !ReferenceEquals(SwitchHost.Content, _switchWorkspace)) throw new InvalidOperationException("No se pudo mostrar el editor de Espada/Escudo.");
            InstallFaultForChecks?.Invoke("host");
            _switchWorkspace.PresentProject(project);
            InstallFaultForChecks?.Invoke("project");
            if (!ReferenceEquals(_switchWorkspace.Project, project)) throw new InvalidOperationException("No se pudo instalar el proyecto de Espada/Escudo.");
            RefreshSwitchHeader();
        }
        catch
        {
            RestoreHostState(state);
            // PresentProject replaced the project of the workspace (and closed its sub-windows): present the previous one again.
            if (state.SwitchView is { } view && previous is not null && !ReferenceEquals(view.Project, previous)) view.PresentProject(previous);
            throw;
        }
        prepared.Release();
    }

    private void Install3ds(PreparedProject prepared)
    {
        var state = CaptureHostState(); var session = (GameProjectSession)prepared.Project; var previous = _session;
        try
        {
            if (SwitchHost.Visibility == Visibility.Visible) HideSwitchWorkspace();
            LoadProject(session);
            InstallFaultForChecks?.Invoke("project");
            if (!ReferenceEquals(_session, session)) throw new InvalidOperationException("No se pudo instalar el proyecto de 3DS.");
        }
        catch
        {
            RestoreHostState(state);
            // LoadProject assigns the new session first and rebuilds the whole 3DS interface: rebuild it for the previous one.
            if (previous is not null && !ReferenceEquals(_session, previous)) { try { LoadProject(previous); } catch (Exception) { _session = previous; } RestoreHostState(state); }
            else _session = previous;
            throw;
        }
        prepared.Release();
    }

    /// <summary>The bridge between the coordinator and the real window.</summary>
    private sealed class WpfProjectOpenHost(MainWindow window) : IProjectOpenHost
    {
        internal Window? PromptOwner { get; set; }

        public string? WhyBlocked(DetectedPlatform target)
        {
            if (window._closed || window.Dispatcher.HasShutdownStarted) return "La aplicación se está cerrando.";
            if (UiProgress.IsInteractionBlocked) return "Hay una operación en curso. Espera a que termine para abrir otro proyecto.";
            if (window._switchWorkspace?.Busy == true) return "El editor de Espada/Escudo está ocupado. Espera a que termine para abrir otro proyecto.";
            if (window._ndsWorkspace?.Busy == true) return "El editor de Nintendo DS está ocupado. Espera a que termine para abrir otro proyecto.";
            if (target == DetectedPlatform.Nintendo3DS && (window._backupRunning || window._globalOperationActive)) return "Hay una copia de seguridad o una operación global en curso en el proyecto de 3DS.";
            return null;
        }

        public ReplacementInfo Inspect(DetectedPlatform target) => target switch
        {
            DetectedPlatform.NintendoDS => window._ndsWorkspace?.Project is { } nds
                // A persistent project is already saved: replacing it loses nothing, so there is nothing to confirm.
                ? new(true, nds.HasChanges && !nds.IsPersistent, $"{nds.Game}" + (nds.HasChanges ? $" ({nds.OperationCount} operaciones sin exportar)" : "")) : new(false, false, ""),
            DetectedPlatform.NintendoSwitch => window._switchWorkspace?.Project is { } sw
                ? new(true, window._switchWorkspace.HasUnsavedWork, sw.Identity.Game) : new(false, false, ""),
            DetectedPlatform.Nintendo3DS => window._session is { } session ? new(true, window.HasUnsavedChanges(), session.GameName) : new(false, false, ""),
            _ => new(false, false, ""),
        };

        public bool ConfirmLeaveView(DetectedPlatform target)
        {
            if (window.SwitchHost.Visibility != Visibility.Visible) return true;
            return window.SwitchHost.Content switch
            {
                SwitchWorkspaceView sw when target != DetectedPlatform.NintendoSwitch => sw.CanReplaceProject(),
                NdsWorkspaceView nds when target != DetectedPlatform.NintendoDS => nds.CanLeave(),
                _ => true,
            };
        }

        public bool ConfirmReplace(ProjectDetection detection, ReplacementInfo current)
        {
            string name = current.Description;
            switch (detection.Platform)
            {
                case DetectedPlatform.NintendoDS:
                    if (current.HasPendingChanges && !window.Ask(PromptOwner, "replace", "Nintendo DS", $"Hay cambios sin exportar en {name}. Si abres «{detection.Game}» se descartarán. ¿Descartarlos y abrir el proyecto nuevo?")) return false;
                    return window._ndsWorkspace?.CanLeave() ?? true;
                case DetectedPlatform.NintendoSwitch:
                    if (current.HasPendingChanges && !window.Ask(PromptOwner, "replace", "Espada/Escudo", $"Hay cambios sin exportar en {name}. Si abres «{detection.Game}» se descartarán. ¿Descartarlos y abrir el proyecto nuevo?")) return false;
                    return window._switchWorkspace?.CanReplaceProject() ?? true;
                default:
                    if (current.HasPendingChanges && !window.Ask(PromptOwner, "replace", "Nintendo 3DS", $"Hay cambios sin guardar en {name}. Si abres «{detection.Game}» se descartarán. ¿Descartarlos y abrir el proyecto nuevo?")) return false;
                    return window.ConfirmAuxiliaryLeave();
            }
        }

        public bool ConfirmWarning(ProjectDetection detection) =>
            window.Ask(PromptOwner, "warning", "Compatibilidad limitada", $"«{detection.Game}»: {detection.Warning}", "Aceptar solo permite intentar abrirla con el motor existente; no garantiza que todas las funciones estén disponibles. ¿Intentar abrirla?");

        public bool ConfirmRecovery(ProjectDetection detection, InterruptedSaveException pending) =>
            window.Ask(PromptOwner, "recovery", "Guardado interrumpido", $"El proyecto «{Path.GetFileName(Path.TrimEndingDirectorySeparator(detection.Path))}» tiene un guardado interrumpido (registro: {pending.PendingPath}). Para abrirlo hay que recuperarlo primero.",
                "La recuperación restablece los archivos de ese proyecto que quedaron a medio escribir a su estado anterior al guardado interrumpido y limpia el registro (si el guardado ya había terminado, solo limpia el registro). Solo toca esa carpeta; los demás proyectos abiertos no se modifican. Haz antes una copia si el proyecto es importante. ¿Recuperar y abrir?");

        public bool ConfirmDsProject(ProjectDetection detection, string kind, string message, string details) =>
            window.Ask(PromptOwner, kind, kind == "ds-recover" ? "Proyecto dañado" : "Proyecto de Nintendo DS", $"«{detection.Game}»: {message}", details);

        public async Task<bool> InstallAsync(PreparedProject prepared, OpenOptions options)
        {
            switch (prepared.Platform)
            {
                case DetectedPlatform.NintendoDS: await window.InstallNdsAsync(prepared, options); return true;
                case DetectedPlatform.NintendoSwitch: window.InstallSwitch(prepared); return true;
                case DetectedPlatform.Nintendo3DS: window.Install3ds(prepared); return true;
                default: return false;
            }
        }
    }
}
