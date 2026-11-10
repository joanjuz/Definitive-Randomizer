using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds.Project;

namespace ThreeDS.Progressive.Services.Nds;

// Persistence of a DS project (PR 3). A project opened with NdsProject.Open is NOT persistent: Batch, Global Template replays on temporary projects and the
// tests keep working in memory and never create files next to a ROM. Persistence starts only when a store is attached, and from then on every confirmed
// operation (Apply, Reset, an event save) goes through the transaction: the resulting project is made durable and verified first, and only then does the
// session change. Previews never touch the store.
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

    /// <summary>After an unknown save outcome: reads what is on disk and makes it the session, whether or not the interrupted operation got there.</summary>
    internal void ReconcilePersistence()
    {
        if (_tx is null) throw new InvalidOperationException("La sesión no tiene un proyecto asociado.");
        _tx.Reconcile(Install);
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
        _tx.Run(current => Compose(current, new(_files, StringComparer.Ordinal), new(_overlays), _recompress, _arm9, current.Meta.Operations,
                (current.Meta.Events ?? new NdsProjectEvents([], [])) with { Operations = stage.Operations.AsReadOnly(), Audit = stage.Audit.AsReadOnly() }, data, scripts),
            _ => { session.Install(stage); EventChanged(); });
    }
}
