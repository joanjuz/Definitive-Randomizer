using System.IO;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Switch;

namespace ThreeDS.Progressive.Services.ProjectDetection;

internal enum OpenOutcome
{
    /// <summary>The project is installed in the interface.</summary>
    Opened,
    /// <summary>The user cancelled (a confirmation, or the dialog while opening). Nothing changed.</summary>
    Cancelled,
    /// <summary>The detector no longer accepts the entry, or the engine rejected it. Nothing changed.</summary>
    Rejected,
    /// <summary>The application is busy (an operation is running). Nothing changed.</summary>
    Blocked,
}

internal sealed record OpenResult(OpenOutcome Outcome, string Message, DetectedPlatform Platform = DetectedPlatform.None)
{
    internal bool Opened => Outcome == OpenOutcome.Opened;
}

/// <param name="AllowRomhacks">Nintendo DS only: accept modified tables (the engine's own "Admitir romhacks" mode; its checks are unchanged).</param>
/// <param name="RecoverInterrupted3ds">Nintendo 3DS only: recover an interrupted save of the NEW project. Only set after the user confirmed it.</param>
/// <param name="AcceptAlternativeDsProject">Nintendo DS only: create the persistent project away from the ROM. Only set after the user accepted it.</param>
/// <param name="RecoverDamagedDsProject">Nintendo DS only: install the offered backup of a damaged project. Only set after the user accepted it.</param>
internal sealed record OpenOptions(bool AllowRomhacks = false, bool RecoverInterrupted3ds = false, bool AcceptAlternativeDsProject = false, bool RecoverDamagedDsProject = false);

/// <summary>
/// A project opened by its engine into a temporary variable, not installed yet. It owns the project until <see cref="Release"/> transfers it to the
/// interface; if it is never released (cancelled, validation or installation failed) <see cref="Dispose"/> frees it.
/// </summary>
internal sealed class PreparedProject : IDisposable
{
    private readonly Action? _dispose;
    private bool _released, _disposed;
    internal PreparedProject(DetectedPlatform platform, ProjectDetection detection, object project, string game, Action? dispose = null)
    { Platform = platform; Detection = detection; Project = project; Game = game; _dispose = dispose; }
    internal DetectedPlatform Platform { get; }
    internal ProjectDetection Detection { get; }
    internal object Project { get; }
    internal string Game { get; }
    internal bool Released => _released;
    internal bool Disposed => _disposed;
    /// <summary>The installer takes ownership: the project must not be disposed by this object any more.</summary>
    internal object Release() { _released = true; return Project; }
    public void Dispose() { if (_released || _disposed) return; _disposed = true; _dispose?.Invoke(); }
}

internal sealed record ReplacementInfo(bool HasSession, bool HasPendingChanges, string Description);

/// <summary>What the interface must provide to open a project. Every method runs on the UI thread except where noted.</summary>
internal interface IProjectOpenHost
{
    /// <summary>Why a project cannot be opened right now (an operation is running, the window is closing), or null.</summary>
    string? WhyBlocked(DetectedPlatform target);
    /// <summary>The session of the TARGET platform (the only one that is replaced) and whether it has pending changes.</summary>
    ReplacementInfo Inspect(DetectedPlatform target);
    /// <summary>Asks whatever is needed before the target platform's session is replaced (pending changes, unsaved forms). False keeps everything.</summary>
    bool ConfirmReplace(ProjectDetection detection, ReplacementInfo current);
    /// <summary>Another platform's workspace is on screen: its unsaved page edits must be settled before it is hidden (its project is NOT replaced). False keeps everything.</summary>
    bool ConfirmLeaveView(DetectedPlatform target);
    /// <summary>The detector gave a warning (a 3DS demo): the user must accept it before the engine is tried.</summary>
    bool ConfirmWarning(ProjectDetection detection);
    /// <summary>A NEW 3DS project has an interrupted save: explain what will be recovered and ask. False opens nothing.</summary>
    bool ConfirmRecovery(ProjectDetection detection, InterruptedSaveException pending);
    /// <summary>
    /// A decision about the persistent project of a DS ROM that only the user can take: <paramref name="kind"/> is "ds-alternative" (create the project away
    /// from the ROM, whose folder cannot be written) or "ds-recover" (install the offered backup of a damaged project). False opens nothing and changes nothing.
    /// </summary>
    bool ConfirmDsProject(ProjectDetection detection, string kind, string message, string details) => false;
    /// <summary>
    /// Installs the prepared project. It must either complete or leave the previous state exactly as it was (and throw or return false). On success it
    /// calls <see cref="PreparedProject.Release"/> and disposes whatever it replaced.
    /// </summary>
    Task<bool> InstallAsync(PreparedProject prepared, OpenOptions options);
}

/// <summary>
/// Universal "Abrir proyecto" (phase B2). The detector (B1) only identifies a candidate; this class then uses the existing engine of the platform
/// (<c>NdsProject.Open</c>, <c>GameProjectSession.Open</c>, <c>SwordShieldProject.Open</c>) and never weakens any of its checks. The order is fixed so that
/// a failed opening can never lose the previous project: confirm → open into a temporary variable (off the UI thread) → validate → install → free the old one.
/// </summary>
internal sealed class ProjectOpenCoordinator
{
    private readonly IProjectOpenHost _host;
    private readonly Func<ProjectDetection, OpenOptions, PreparedProject> _prepare;
    private readonly Func<string?, ProjectDetection> _detect;

    internal ProjectOpenCoordinator(IProjectOpenHost host, Func<ProjectDetection, OpenOptions, PreparedProject>? prepare = null, Func<string?, ProjectDetection>? detect = null)
    { _host = host; _prepare = prepare ?? PrepareWithEngine; _detect = detect ?? ProjectDetector.Detect; }

    /// <summary>Opens the detected candidate. Must be called on the UI thread; the engines run on the thread pool.</summary>
    internal async Task<OpenResult> OpenAsync(ProjectDetection detection, OpenOptions options, CancellationToken cancel)
    {
        if (!detection.IsCandidate) return new(OpenOutcome.Rejected, detection.Message, detection.Platform);
        var platform = detection.Platform;
        if (_host.WhyBlocked(platform) is { } blocked) return new(OpenOutcome.Blocked, blocked, platform);
        if (cancel.IsCancellationRequested) return new(OpenOutcome.Cancelled, "Apertura cancelada.", platform);

        // The file may have changed since the dialog analysed it: the same detector must still accept the same thing.
        var again = await Task.Run(() => _detect(detection.Path));
        if (cancel.IsCancellationRequested) return new(OpenOutcome.Cancelled, "Apertura cancelada.", platform);
        if (!again.IsCandidate || again.Platform != detection.Platform || again.Kind != detection.Kind || again.Game != detection.Game)
            return new(OpenOutcome.Rejected, "La entrada cambió mientras se preparaba la apertura: " + again.Message, platform);
        detection = again;

        if (detection.HasWarning && !_host.ConfirmWarning(detection)) return new(OpenOutcome.Cancelled, "Apertura cancelada: no se aceptó la advertencia.", platform);
        if (!_host.ConfirmLeaveView(platform)) return new(OpenOutcome.Cancelled, "Apertura cancelada: la vista actual se conserva.", platform);
        var current = _host.Inspect(platform);
        if (current.HasSession && !_host.ConfirmReplace(detection, current)) return new(OpenOutcome.Cancelled, "Apertura cancelada: el proyecto actual se conserva con sus cambios.", platform);
        if (cancel.IsCancellationRequested) return new(OpenOutcome.Cancelled, "Apertura cancelada.", platform);

        PreparedProject? prepared = null;
        try
        {
            try { prepared = await Task.Run(() => _prepare(detection, options)); }
            catch (Nds.Project.NdsProjectNeedsConsentException consent) when (!options.AcceptAlternativeDsProject)
            {
                if (cancel.IsCancellationRequested) return new(OpenOutcome.Cancelled, "Apertura cancelada.", platform);
                if (!_host.ConfirmDsProject(detection, "ds-alternative", "La carpeta de la ROM no admite archivos nuevos, así que el proyecto con tu trabajo no se puede guardar junto a ella.",
                    $"Carpeta de la ROM: {consent.RomFolder}\nSi aceptas, el proyecto se guardará en tu carpeta de usuario:\n{consent.AlternativePath}\nLa ROM no se modifica. Si no aceptas, no se abre nada.")) return new(OpenOutcome.Cancelled, "Apertura cancelada: no se aceptó guardar el proyecto fuera de la carpeta de la ROM.", platform);
                options = options with { AcceptAlternativeDsProject = true };
                prepared = await Task.Run(() => _prepare(detection, options));
            }
            catch (Nds.Project.NdsProjectDamagedException damaged) when (!options.RecoverDamagedDsProject && damaged.Plan.Recommended is not null)
            {
                if (cancel.IsCancellationRequested) return new(OpenOutcome.Cancelled, "Apertura cancelada.", platform);
                if (!_host.ConfirmDsProject(detection, "ds-recover", damaged.Message, damaged.Describe() + "\nProyecto: " + damaged.ProjectPath + "\n¿Recuperar esa copia y abrir?")) return new(OpenOutcome.Cancelled, "Apertura cancelada: el proyecto dañado no se modificó.", platform);
                options = options with { RecoverDamagedDsProject = true };
                prepared = await Task.Run(() => _prepare(detection, options));
            }
            catch (InterruptedSaveException pending) when (platform == DetectedPlatform.Nintendo3DS && !options.RecoverInterrupted3ds)
            {
                // The typed state of the engine: ask, and only then recover the NEW project.
                if (cancel.IsCancellationRequested) return new(OpenOutcome.Cancelled, "Apertura cancelada.", platform);
                if (!_host.ConfirmRecovery(detection, pending)) return new(OpenOutcome.Cancelled, "Apertura cancelada: no se recuperó el guardado interrumpido y el proyecto no se abrió.", platform);
                options = options with { RecoverInterrupted3ds = true };
                prepared = await Task.Run(() => _prepare(detection, options));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return new(OpenOutcome.Rejected, Explain(detection, ex), platform); }

        try
        {
            if (cancel.IsCancellationRequested) return new(OpenOutcome.Cancelled, "Apertura cancelada: el proyecto abierto se descartó.", platform);
            if (!Validate(prepared, detection, out string invalid)) return new(OpenOutcome.Rejected, invalid, platform);
            if (_host.WhyBlocked(platform) is { } nowBlocked) return new(OpenOutcome.Blocked, nowBlocked, platform);
            bool installed;
            try { installed = await _host.InstallAsync(prepared, options); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { return new(OpenOutcome.Rejected, "No se pudo mostrar el proyecto nuevo; se conserva el anterior. " + ex.Message, platform); }
            return installed
                ? new(OpenOutcome.Opened, prepared.Game + " abierto.", platform)
                : new(OpenOutcome.Cancelled, "No se instaló el proyecto nuevo; se conserva el anterior.", platform);
        }
        finally { prepared!.Dispose(); }
    }

    private static bool Validate(PreparedProject prepared, ProjectDetection detection, out string message)
    {
        message = "";
        if (prepared.Platform != detection.Platform) { message = "El motor abrió una plataforma distinta de la detectada; no se instala."; return false; }
        if (string.IsNullOrWhiteSpace(prepared.Game)) { message = "El motor no identificó el juego; no se instala."; return false; }
        return true;
    }

    private static string Explain(ProjectDetection detection, Exception ex)
    {
        string text = ex is Nds.Project.NdsProjectDamagedException damaged ? damaged.Message + " " + damaged.Describe() : ex.Message;
        if (detection.Platform == DetectedPlatform.Nintendo3DS && ex is InvalidDataException or IOException && detection.HasWarning)
            text += " (Es una demo: el motor existente puede rechazar sus datos; no se fuerza la apertura.)";
        return text;
    }

    /// <summary>The existing engines. Nothing here relaxes their checks; 3DS first opens WITHOUT recovering.</summary>
    internal static PreparedProject PrepareWithEngine(ProjectDetection detection, OpenOptions options)
    {
        switch (detection.Platform)
        {
            case DetectedPlatform.NintendoDS:
            {
                // The ROM is opened by the engine as always, and with it the persistent project that belongs to it (created, or resumed).
                var request = detection.Kind switch
                {
                    DetectedKind.DsProject => new Nds.Project.NdsProjectOpenRequest(detection.SourcePath!, options.AllowRomhacks, ProjectPath: detection.Path),
                    DetectedKind.DsEventCheckpoint => new Nds.Project.NdsProjectOpenRequest(detection.SourcePath!, options.AllowRomhacks, CheckpointPath: detection.Path),
                    _ => new Nds.Project.NdsProjectOpenRequest(detection.Path, options.AllowRomhacks),
                };
                var project = Nds.Project.NdsProjectOpener.Open(request with { AcceptAlternativeLocation = options.AcceptAlternativeDsProject, RecoverDamaged = options.RecoverDamagedDsProject });
                return new(DetectedPlatform.NintendoDS, detection, project, project.Game, project.Dispose);
            }
            case DetectedPlatform.Nintendo3DS:
            {
                var session = GameProjectSession.Open(detection.Path, options.RecoverInterrupted3ds);
                return new(DetectedPlatform.Nintendo3DS, detection, session, session.GameName);
            }
            case DetectedPlatform.NintendoSwitch:
            {
                var project = SwordShieldProject.Open(detection.Path);
                return new(DetectedPlatform.NintendoSwitch, detection, project, project.Identity.Game);
            }
            default: throw new InvalidOperationException("La plataforma detectada no es abrible.");
        }
    }
}
