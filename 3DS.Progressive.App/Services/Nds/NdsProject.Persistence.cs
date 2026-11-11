using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds.Project;

namespace ThreeDS.Progressive.Services.Nds;

// Persistence of a DS project (PR 3). A project opened with NdsProject.Open is NOT persistent: Batch, Global Template replays on temporary projects and the
// tests keep working in memory and never create files next to a ROM. Persistence starts only when a store is attached, and from then on every confirmed
// operation (Apply, Reset, an event save) goes through the transaction: the resulting project is made durable and verified first, and only then does the
// session change. Previews never touch the store.
/// <summary>A confirmed operation could not be saved in the persistent project, so it was not applied. The session is exactly as it was.</summary>
internal sealed class NdsProjectSaveException(NdsProjectStoreError kind, string message, Exception inner) : InvalidOperationException(message, inner)
{
    internal NdsProjectStoreError Kind { get; } = kind;
}

/// <summary>What the interface shows about the persistent project of a DS session.</summary>
internal enum NdsProjectPersistenceState
{
    /// <summary>The session has no project: its changes exist only in memory until a ROM is exported.</summary>
    None,
    /// <summary>Every confirmed operation is on disk.</summary>
    Saved,
    /// <summary>An operation is being made durable right now.</summary>
    Saving,
    /// <summary>The last operation could not be saved and was not applied; the project on disk is the previous, valid one.</summary>
    Error,
    /// <summary>The project can be read but not written (the folder does not allow it): operations are refused.</summary>
    ReadOnly,
    /// <summary>The file on disk is not the one this session was working on (it changed outside, is another project, or is damaged): nothing is overwritten.</summary>
    Conflict,
    /// <summary>It is not known whether the last save finished: the session takes no operations until it is reconciled with what is on disk.</summary>
    RecoveryPending,
}

internal sealed partial class NdsProject
{
    private NdsProjectTransactor? _tx;
    private INdsProjectStore? _store;
    private string _writer = "";

    internal bool IsPersistent => _tx is not null;
    /// <summary>Generation of the project on disk (0 when the session is not persistent).</summary>
    internal long SavedGeneration => _tx?.Current.Generation ?? 0;
    /// <summary>Why the project takes no more operations until <see cref="ReconcilePersistence"/> (an unknown save outcome), or null.</summary>
    internal string? PersistenceFault => _tx?.FaultReason;
    /// <summary>The document as it is on disk (the last confirmed operation). Null when the session is not persistent.</summary>
    internal NdsProjectDocument? SavedDocument => _tx?.Current;

    /// <summary>
    /// Makes this session persistent. With <paramref name="existing"/> null a new project is created (its first generation, empty, is written now); otherwise the
    /// stored project is installed, after checking that it belongs to this ROM. The session must have no changes of its own, and nothing is installed unless
    /// everything in the document is valid for this ROM.
    /// </summary>
    internal void AttachPersistence(INdsProjectStore store, NdsProjectRom rom, NdsProjectDocument? existing, string writer, Func<DateTime>? clock = null)
    {
        if (_tx is not null) throw new InvalidOperationException("La sesión ya tiene un proyecto asociado.");
        if (_history.Count > 0 || _events?.HasHistory == true) throw new InvalidOperationException("Solo se asocia un proyecto a una sesión sin cambios propios.");
        if (!string.Equals(rom.GameCode, Identity.Profile.GameCode, StringComparison.Ordinal) || rom.Revision != Identity.Profile.Version)
            throw new InvalidDataException($"La identidad indicada ({rom.ProfileKey}) no es la de la ROM abierta ({Identity.Profile.GameCode}/{Identity.Profile.Version}).");
        _writer = writer;
        if (existing is null)
        {
            var now = (clock ?? (() => DateTime.UtcNow))();
            var seed = new NdsProjectDocument(new NdsProjectMeta
            {
                Writer = writer, ProjectId = Guid.NewGuid(), CreatedUtc = now, SavedUtc = now, Generation = 0,
                Rom = rom with { IsRomhack = IsRomhack, LastKnownPath = SourcePath }, Settings = new JsonObject { ["AllowRomhack"] = IsRomhack },
            }, new NdsProjectState());
            var transactor = new NdsProjectTransactor(store, seed, clock);
            transactor.Run(document => document, _ => { });
            _tx = transactor;
        }
        else
        {
            var association = NdsProjectAssociation.Evaluate(existing.Meta.Rom, rom);
            if (!association.IsMatch) throw new InvalidDataException(association.Message);
            Install(existing);
            _tx = new NdsProjectTransactor(store, existing, clock);
        }
        _store = store;
    }

    /// <summary>The kind of the last save that failed with a definite error (nothing was applied), or null after a save that succeeded.</summary>
    internal NdsProjectStoreError? LastSaveError { get; private set; }

    /// <summary>
    /// Runs one operation through the transaction. A definite failure of the store reaches the editors as <see cref="NdsProjectSaveException"/>, an
    /// <see cref="InvalidOperationException"/> like the other reasons an operation is refused, so every view that applies a change already reports it.
    /// </summary>
    private void Commit(Func<NdsProjectDocument, NdsProjectDocument> prepare, Action<NdsProjectDocument> publish)
    {
        _saving = true; PersistenceChanged?.Invoke();
        try { _tx!.Run(prepare, publish); LastSaveError = null; }
        catch (NdsProjectStoreException ex)
        {
            LastSaveError = ex.Kind;
            throw new NdsProjectSaveException(ex.Kind, "El cambio no se aplicó porque no se pudo guardar en el proyecto. " + ex.Message, ex);
        }
        finally { _saving = false; PersistenceChanged?.Invoke(); }
    }

    private bool _saving;
    /// <summary>Raised when <see cref="PersistenceState"/> may have changed, on the thread that is saving (the interface marshals it).</summary>
    internal event Action? PersistenceChanged;

    internal NdsProjectPersistenceState PersistenceState =>
        _tx is null ? NdsProjectPersistenceState.None
        : _tx.Faulted ? NdsProjectPersistenceState.RecoveryPending
        : _saving ? NdsProjectPersistenceState.Saving
        : _store is NdsProjectFileStore { IsReadOnly: true } || LastSaveError == NdsProjectStoreError.ReadOnly ? NdsProjectPersistenceState.ReadOnly
        : LastSaveError is NdsProjectStoreError.StaleGeneration or NdsProjectStoreError.DifferentProject or NdsProjectStoreError.MainDamaged or NdsProjectStoreError.NewerFormat or NdsProjectStoreError.AlreadyInUse ? NdsProjectPersistenceState.Conflict
        : LastSaveError is not null ? NdsProjectPersistenceState.Error
        : NdsProjectPersistenceState.Saved;

    /// <summary>One line for the interface: the state and what it means for the work.</summary>
    internal string PersistenceSummary => PersistenceState switch
    {
        NdsProjectPersistenceState.None => "Sin proyecto: los cambios solo están en memoria hasta exportar.",
        NdsProjectPersistenceState.Saved => $"Proyecto guardado · generación {SavedGeneration}",
        NdsProjectPersistenceState.Saving => "Guardando el proyecto…",
        NdsProjectPersistenceState.Error => "Error al guardar: el último cambio no se aplicó. El proyecto guardado sigue siendo el anterior; vuelve a intentarlo.",
        NdsProjectPersistenceState.ReadOnly => "Proyecto de solo lectura: la carpeta no admite escritura y los cambios se rechazan.",
        NdsProjectPersistenceState.Conflict => "Conflicto de escritura: el archivo del proyecto cambió fuera de esta sesión y no se sobrescribe. Puedes recargar lo guardado.",
        _ => "Recuperación pendiente: no se sabe si el último guardado terminó. Reconcilia con lo guardado para continuar.",
    };

    /// <summary>
    /// After a conflict: makes what is on disk the session (the session only held confirmed operations, and those were already on disk before the file
    /// changed outside). Refused when the file is not readable as this project of this ROM; nothing is installed then.
    /// </summary>
    internal void ReloadPersistence()
    {
        if (_tx is null || _store is null) throw new InvalidOperationException("La sesión no tiene un proyecto asociado.");
        if (_tx.Faulted) { ReconcilePersistence(); PersistenceChanged?.Invoke(); return; }
        NdsProjectLoadResult load;
        try { load = _store.Load(); } catch (NdsProjectStoreException ex) { throw new InvalidOperationException("No se pudo leer el proyecto guardado: " + ex.Message, ex); }
        if (load.Status != NdsProjectLoadStatus.Loaded) throw new InvalidOperationException("El proyecto guardado no se puede leer (" + load.Message + "). Cierra y vuelve a abrir la ROM para recuperarlo.");
        var current = _tx.Current.Meta;
        if (load.Document!.Meta.ProjectId != current.ProjectId || !load.Document.Meta.Rom.SameRomAs(current.Rom)) throw new InvalidOperationException("El archivo guardado es otro proyecto o es de otra ROM: no se carga en esta sesión.");
        try { Install(load.Document); } catch (InvalidDataException ex) { throw new InvalidOperationException(ex.Message, ex); }
        _tx = new NdsProjectTransactor(_store, load.Document); LastSaveError = null; PersistenceChanged?.Invoke();
    }

    /// <summary>After an unknown save outcome: reads what is on disk and makes it the session, whether or not the interrupted operation got there.</summary>
    internal void ReconcilePersistence()
    {
        if (_tx is null) throw new InvalidOperationException("La sesión no tiene un proyecto asociado.");
        _tx.Reconcile(Install); LastSaveError = null; PersistenceChanged?.Invoke();
    }

    /// <summary>
    /// Replaces the session with a stored document. Everything that can be refused is checked before anything changes: the tables must exist in this ROM, the
    /// romhack mode must be the one the project was made with, and the event history must replay to the stored bytes.
    /// </summary>
    private void Install(NdsProjectDocument document)
    {
        if (document.Meta.Rom.IsRomhack != IsRomhack)
            throw new InvalidDataException(document.Meta.Rom.IsRomhack ? "El proyecto se creó sobre un romhack: ábrelo con el modo romhack activado." : "El proyecto se creó sobre la ROM original y esta se abrió como romhack.");
        try
        {
            foreach (string path in document.State.Files.Keys) _ = _rom.File(path);
            foreach (int id in document.State.Overlays.Keys.Concat(document.Meta.RecompressOverlays)) _ = _rom.Overlay(id);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new InvalidDataException("El proyecto contiene una tabla que no existe en esta ROM: " + ex.Message, ex); }
        if (document.State.Arm9 is not null && _gen4 is null) throw new InvalidDataException("El proyecto contiene un ARM9 modificado y en este juego el ARM9 no se modifica.");
        var events = document.Meta.Events; bool hasEvents = events is { Operations.Count: > 0 } or { Audit.Count: > 0 } || document.State.EventData is not null || document.State.EventScripts is not null;
        if (hasEvents && !SupportsEventProfile) throw new InvalidDataException("El proyecto contiene eventos y esta ROM no es Pokémon Platino CPUE/1.");
        if (hasEvents)
        {
            // Both the events and another module changed the script archive: they must still combine, exactly as they will when exporting.
            if (document.State.EventScripts is { } mine && document.State.Files.TryGetValue(PlatinumEvents.ScriptPath, out var others))
                try { Events.RequireMergeable(mine, others); }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { throw new InvalidDataException("El proyecto contiene dos ediciones del mismo archivo de scripts: " + ex.Message, ex); }
            // The last check that can refuse: the history is replayed and must give the stored bytes. The event session changes only if it does.
            Events.Restore(events?.Operations ?? [], events?.Audit ?? [], document.State.EventData, document.State.EventScripts);
        }
        else _events?.Reset();
        // From here nothing can be refused.
        _files.Clear(); foreach (var (path, data) in document.State.Files) _files[path] = data;
        _overlays.Clear(); foreach (var (id, data) in document.State.Overlays) _overlays[id] = data;
        _recompress.Clear(); _recompress.UnionWith(document.Meta.RecompressOverlays);
        _arm9 = document.State.Arm9;
        _history.Clear(); _history.AddRange(document.Meta.Operations.Select(o => o.ToOperation()));
        Revision++; _gen5View = null; _gen4View = null;
    }

    private NdsProjectDocument Compose(NdsProjectDocument current, Dictionary<string, byte[]> files, Dictionary<int, byte[]> overlays, IEnumerable<int> recompress, byte[]? arm9,
        IReadOnlyList<NdsProjectOperationRecord> operations, NdsProjectEvents? events, byte[]? eventData, byte[]? eventScripts)
    {
        var settings = current.Meta.Settings is { } existing ? (JsonObject)existing.DeepClone() : new JsonObject(); settings["AllowRomhack"] = IsRomhack;
        return new(current.Meta with { Writer = _writer, Rom = current.Meta.Rom with { LastKnownPath = SourcePath }, Operations = operations, Events = events, RecompressOverlays = recompress.Order().ToArray(), Settings = settings },
            new NdsProjectState { Files = files, Overlays = overlays, Arm9 = arm9, EventData = eventData, EventScripts = eventScripts });
    }

    /// <summary>
    /// Brings the history of an event checkpoint (v1, v2 or v3) into the persistent project. The checkpoint file is only read. It is refused when the project
    /// already has a different event history (two histories are never merged); importing the history the project already has changes nothing.
    /// </summary>
    internal void ImportEventCheckpoint(NdsEventDocument document)
    {
        if (_tx is null) throw new InvalidOperationException("La sesión no tiene un proyecto asociado.");
        var session = Events; var stage = session.Stage(document);
        if (session.HasHistory)
        {
            if (System.Text.Json.JsonSerializer.Serialize(session.History()) == System.Text.Json.JsonSerializer.Serialize(stage.Operations) && session.Data.Bytes().AsSpan().SequenceEqual(stage.Next.Bytes())) return;
            throw new InvalidOperationException("El proyecto de esta ROM ya tiene su propio historial de eventos y el checkpoint tiene otro: no se mezclan. Abre la ROM (o su proyecto) para continuar con el historial guardado.");
        }
        RequireCompatibleScripts(_files, stage.Next);
        byte[]? data = session.EventBytesIfChanged(stage.Next), scripts = session.ScriptBytesIfChanged(stage.Next); var imported = NdsProjectEvents.FromCheckpoint(document);
        Commit(current => Compose(current, new(_files, StringComparer.Ordinal), new(_overlays), _recompress, _arm9, current.Meta.Operations,
                current.Meta.Events is { } existing ? existing with { Operations = imported.Operations, Audit = imported.Audit, ImportedFrom = imported.ImportedFrom } : imported, data, scripts),
            _ => { session.Install(stage); EventChanged(); });
    }

    /// <summary>
    /// A script archive written by another DS module must combine with the scripts the event history appended. Checked when the operation is confirmed, not
    /// only when exporting: a conflict found days later, at export time, could not be undone.
    /// </summary>
    private void RequireCompatibleScripts(IReadOnlyDictionary<string, byte[]> files, PlatinumEvents? eventState = null)
    {
        if (_events is not { } session || !files.TryGetValue(PlatinumEvents.ScriptPath, out var theirs)) return;
        if (session.ScriptBytesIfChanged(eventState ?? session.Data) is null) return;
        _ = session.MergeScriptArchive(theirs, eventState);
    }

    /// <summary>
    /// Confirms an event proposal in the persistent project (no checkpoint file is written): the history, the event data and the scripts become durable, and
    /// then the session changes. A session that is not persistent keeps using <see cref="NdsEventSession.Save"/> with a checkpoint.
    /// </summary>
    internal void SaveEvent(NdsEventProposal proposal)
    {
        if (_tx is null) throw new InvalidOperationException("La sesión no tiene un proyecto asociado: guarda los eventos en un checkpoint.");
        var session = Events; var stage = session.Stage(proposal);
        RequireCompatibleScripts(_files, stage.Next);
        byte[]? data = session.EventBytesIfChanged(stage.Next), scripts = session.ScriptBytesIfChanged(stage.Next);
        Commit(current => Compose(current, new(_files, StringComparer.Ordinal), new(_overlays), _recompress, _arm9, current.Meta.Operations,
                (current.Meta.Events ?? new NdsProjectEvents([], [])) with { Operations = stage.Operations.AsReadOnly(), Audit = stage.Audit.AsReadOnly() }, data, scripts),
            _ => { session.Install(stage); EventChanged(); });
    }
}
