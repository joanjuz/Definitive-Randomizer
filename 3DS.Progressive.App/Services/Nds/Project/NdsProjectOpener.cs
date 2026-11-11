using System.IO;

namespace ThreeDS.Progressive.Services.Nds.Project;

/// <summary>The project of the ROM would have to be created away from the ROM (its folder cannot be written). Opening continues only if the user accepts that place.</summary>
internal sealed class NdsProjectNeedsConsentException(string romFolder, string alternativePath)
    : IOException($"La carpeta de la ROM no admite archivos nuevos ({romFolder}). El proyecto se puede guardar en {alternativePath}.")
{
    internal string RomFolder { get; } = romFolder;
    internal string AlternativePath { get; } = alternativePath;
}

/// <summary>The main file of the project is damaged or missing. It is never replaced silently: the user must accept the copy that is offered.</summary>
internal sealed class NdsProjectDamagedException(string projectPath, NdsProjectRecoveryPlan plan)
    : IOException("El proyecto guardado está dañado: " + plan.DamageMessage)
{
    internal string ProjectPath { get; } = projectPath;
    internal NdsProjectRecoveryPlan Plan { get; } = plan;
    /// <summary>What the user is told before deciding.</summary>
    internal string Describe()
    {
        if (Plan.Recommended is not { } copy) return "No hay ninguna copia anterior válida. El archivo dañado no se modifica; para empezar un proyecto nuevo, mueve o renombra ese archivo.";
        string lost = Plan.LostOperations(copy) switch { null => "No se puede saber cuántas operaciones se perderían (la cabecera del archivo dañado tampoco es fiable).", 0 => "No se pierde ninguna operación registrada.", int n => $"Se perderían las {n} últimas operaciones." };
        return $"Hay una copia anterior válida ({copy.Source}, generación {copy.Generation}, {copy.OperationCount} operaciones, guardada el {copy.SavedUtc.ToLocalTime():g}). {lost} El archivo dañado se conserva con otro nombre.";
    }
}

/// <param name="RomPath">The ROM to open (never modified).</param>
/// <param name="ProjectPath">The project file to use, when it was chosen explicitly (a <c>.drproj</c> was opened); null to find or create the project of the ROM.</param>
/// <param name="CheckpointPath">An event checkpoint to import into the project (the file is only read).</param>
/// <param name="AcceptAlternativeLocation">The user accepted creating the project away from the ROM.</param>
/// <param name="RecoverDamaged">The user accepted installing the offered backup in place of a damaged main file.</param>
/// <param name="AlternativeRoot">Where projects go when the folder of the ROM cannot be written (the default is a private folder of the user).</param>
internal sealed record NdsProjectOpenRequest(string RomPath, bool AllowRomhack, string? ProjectPath = null, string? CheckpointPath = null,
    bool AcceptAlternativeLocation = false, bool RecoverDamaged = false, string? AlternativeRoot = null);

/// <summary>
/// Opens a DS ROM together with its persistent project: verifies the ROM with the engine (unchanged), finds the project that belongs to it, creates one when
/// there is none, and resumes it otherwise. A project is found by the IDENTITY of the ROM it records, never by a name: next to the ROM under its name, next
/// to the ROM under another name (the ROM was renamed), or in the alternative place derived from the SHA-256 of the ROM. Anything doubtful is refused with an
/// explanation and nothing is written: a project of another ROM, two projects of the same ROM, a project made by a newer build, a damaged one without consent.
/// </summary>
internal static class NdsProjectOpener
{
    internal static string Writer => "Definitive Randomizer " + (typeof(NdsProjectOpener).Assembly.GetName().Version?.ToString(3) ?? "");
    private const long MaxSiblingBytes = 256L * 1024 * 1024; private const int MaxSiblings = 64;

    internal static NdsProject Open(NdsProjectOpenRequest request)
    {
        var project = NdsProject.Open(request.RomPath, request.AllowRomhack); NdsProjectFileStore? store = null;
        try
        {
            var identity = NdsProjectRomIdentity.Compute(project.SourcePath, project.IsRomhack);
            string path = request.ProjectPath is { } chosen ? Path.GetFullPath(chosen) : Locate(project.SourcePath, identity, request, out bool create);
            store = OpenStore(path, createFolder: request.ProjectPath is null);
            var load = store.Load();
            switch (load.Status)
            {
                case NdsProjectLoadStatus.Loaded: break;
                case NdsProjectLoadStatus.Empty:
                    if (store.IsReadOnly) throw new IOException("No hay proyecto y la carpeta no admite crear uno: " + Path.GetDirectoryName(path));
                    break;
                case NdsProjectLoadStatus.NewerFormat:
                    throw new InvalidDataException("El proyecto guardado lo escribió una versión más reciente de Definitive Randomizer y no se modifica: " + load.Message);
                default:
                    var plan = load.Plan!;
                    if (!request.RecoverDamaged || plan.Recommended is null || store.IsReadOnly) throw new NdsProjectDamagedException(path, plan);
                    store.Recover(plan.Recommended); load = store.Load();
                    if (load.Status != NdsProjectLoadStatus.Loaded) throw new InvalidDataException("La copia recuperada no se pudo leer: " + load.Message);
                    break;
            }
            // Attaching checks the association (same game, same revision, same bytes) and installs the stored project, or creates the first generation.
            project.AttachPersistence(store, identity, load.Document, Writer);
            if (request.CheckpointPath is { } checkpoint) project.ImportEventCheckpoint(NdsEventSession.ReadDocument(checkpoint));
            return project;
        }
        catch { if (!project.IsPersistent) store?.Dispose(); project.Dispose(); throw; }
    }

    /// <summary>Takes the project for writing; when the place does not allow it, opens it only to be read (operations are then refused, never lost silently).</summary>
    private static NdsProjectFileStore OpenStore(string path, bool createFolder)
    {
        try { return NdsProjectFileStore.Open(path, createFolder && !Directory.Exists(Path.GetDirectoryName(path))); }
        catch (NdsProjectStoreException ex) when (ex.Kind == NdsProjectStoreError.ReadOnly && File.Exists(path)) { return NdsProjectFileStore.OpenReadOnly(path); }
        catch (NdsProjectStoreException ex) when (ex.Kind == NdsProjectStoreError.AlreadyInUse && ex.Message.Contains("pid " + Environment.ProcessId + " ", StringComparison.Ordinal))
        { throw new NdsProjectStoreException(NdsProjectStoreError.AlreadyInUse, "Este proyecto ya está abierto en esta ventana.", ex); }
    }

    /// <summary>Where the project of this ROM is, or where a new one goes. Refuses instead of choosing when more than one project claims the ROM.</summary>
    private static string Locate(string romPath, NdsProjectRom identity, NdsProjectOpenRequest request, out bool create)
    {
        var resolution = NdsProjectLocator.Resolve(romPath, identity, request.AlternativeRoot); create = false;
        // A project under another name in the folder of the ROM, whose recorded ROM is by content this one and whose own ROM file is gone: the ROM was
        // renamed and its project was not.
        var renamed = resolution.Beside.Exists ? [] : Siblings(resolution.Beside.ProjectPath, identity);
        var found = new List<string>(renamed); if (resolution.Beside.Exists) found.Add(resolution.Beside.ProjectPath); if (resolution.Alternative.Exists) found.Add(resolution.Alternative.ProjectPath);
        if (found.Count > 1)
            throw new InvalidDataException("Hay más de un proyecto para esta ROM y no se elige uno en silencio:\n" + string.Join("\n", found) + "\nAbre directamente el archivo .drproj que quieras usar, o aparta los demás.");
        if (found.Count == 1) return found[0];
        create = true;
        if (resolution.NeedsConsent && !request.AcceptAlternativeLocation) throw new NdsProjectNeedsConsentException(Path.GetDirectoryName(resolution.Beside.ProjectPath)!, resolution.Alternative.ProjectPath);
        return resolution.Preferred.ProjectPath;
    }

    private static List<string> Siblings(string expected, NdsProjectRom identity)
    {
        var matches = new List<string>(); string folder = Path.GetDirectoryName(expected)!;
        try
        {
            foreach (string candidate in Directory.EnumerateFiles(folder, "*" + NdsProjectPaths.Extension).Take(MaxSiblings))
            {
                if (candidate.Equals(expected, StringComparison.OrdinalIgnoreCase) || !candidate.EndsWith(NdsProjectPaths.Extension, StringComparison.OrdinalIgnoreCase)) continue;
                // A project whose own ROM is still next to it belongs to that file (another copy of the same ROM), not to the one being opened.
                if (File.Exists(candidate[..^NdsProjectPaths.Extension.Length])) continue;
                try
                {
                    if (new FileInfo(candidate).Length > MaxSiblingBytes) continue;
                    var read = NdsProjectCodec.Read(File.ReadAllBytes(candidate));
                    if (read.Ok && read.Document!.Meta.Rom.SameRomAs(identity)) matches.Add(candidate);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return matches;
    }
}
