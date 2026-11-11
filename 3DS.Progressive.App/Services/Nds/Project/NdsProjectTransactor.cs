using System.IO;

namespace ThreeDS.Progressive.Services.Nds.Project;

/// <summary>The project cannot take new operations until it is reconciled with what is durable (persistence completed but memory was not updated, or the outcome is unknown).</summary>
internal sealed class NdsProjectFaultedException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>
/// The transaction of one operation (decision B). The order is fixed:
/// <list type="number">
/// <item><c>prepare</c>: builds and validates the RESULTING document from the current one, without mutating the session (any exception leaves everything as it was);</item>
/// <item>the store makes it durable and verified (<see cref="INdsProjectStore.Commit"/>); if that fails with a definite error nothing changed and nothing is published;</item>
/// <item><c>publish</c>: only then is the result installed in memory.</item>
/// </list>
/// Two exceptional cases block further operations (<see cref="Faulted"/>) until <see cref="Reconcile"/> reloads the durable state and installs it: the store
/// reports an unknown outcome, or the commit succeeded but <c>publish</c> threw. The session therefore never keeps a change that exists only in memory, and never
/// reports as saved something that is not.
/// </summary>
internal sealed class NdsProjectTransactor
{
    private readonly INdsProjectStore _store; private readonly Func<DateTime> _clock;
    private NdsProjectDocument _current; private string? _fault;

    internal NdsProjectTransactor(INdsProjectStore store, NdsProjectDocument current, Func<DateTime>? clock = null) { _store = store; _current = current; _clock = clock ?? (() => DateTime.UtcNow); }

    internal NdsProjectDocument Current => _current;
    internal bool Faulted => _fault is not null;
    internal string? FaultReason => _fault;

    internal NdsProjectDocument Run(Func<NdsProjectDocument, NdsProjectDocument> prepare, Action<NdsProjectDocument> publish)
    {
        if (_fault is not null) throw new NdsProjectFaultedException("El proyecto está bloqueado hasta reconciliarlo con lo guardado: " + _fault);
        var prepared = prepare(_current);
        var next = prepared with { Meta = prepared.Meta with { Generation = _current.Generation + 1, SavedUtc = _clock() } };
        if (prepared.Meta.ProjectId != _current.Meta.ProjectId || !prepared.Meta.Rom.SameRomAs(_current.Meta.Rom)) throw new InvalidOperationException("Una operación no puede cambiar el proyecto ni la ROM a la que pertenece.");
        try { _store.Commit(next, _current.Generation); }
        // A definite error: the store guarantees nothing durable changed, so nothing is published and the session stays as it was.
        catch (NdsProjectStoreException ex) when (ex.Kind != NdsProjectStoreError.OutcomeUnknown) { throw; }
        // An unknown outcome, or an exception the contract does not allow (the store cannot say what happened): either way it is not known what is durable.
        catch (Exception ex)
        { _fault = "no se pudo confirmar si el último guardado terminó (" + ex.Message + ")"; throw new NdsProjectFaultedException(_fault, ex); }
        // From here the operation IS durable. Whatever stops the installation in memory (running out of memory included) leaves the session behind what is saved.
        try { publish(next); }
        catch (Exception ex)
        { _fault = "el guardado terminó pero no se pudo instalar en memoria (" + ex.Message + ")"; throw new NdsProjectFaultedException(_fault, ex); }
        _current = next;
        return next;
    }

    /// <summary>Reloads the durable project and installs it (whether or not the interrupted operation became durable). Leaves the fault if that is not possible.</summary>
    internal NdsProjectDocument Reconcile(Action<NdsProjectDocument> publish)
    {
        if (_fault is null) return _current;
        var load = _store.Load();
        if (load.Status != NdsProjectLoadStatus.Loaded) throw new NdsProjectFaultedException("No se puede reconciliar: el proyecto guardado no se puede leer (" + load.Status + "). " + load.Message);
        if (load.Document!.Meta.ProjectId != _current.Meta.ProjectId || !load.Document.Meta.Rom.SameRomAs(_current.Meta.Rom))
            throw new NdsProjectFaultedException("No se puede reconciliar: el archivo guardado es otro proyecto o es de otra ROM.");
        publish(load.Document!);
        _current = load.Document!; _fault = null;
        return _current;
    }
}
