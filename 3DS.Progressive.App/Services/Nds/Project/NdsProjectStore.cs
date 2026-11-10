using System.IO;

namespace ThreeDS.Progressive.Services.Nds.Project;

internal enum NdsProjectStoreError
{
    /// <summary>Another instance holds the project (the lease is taken).</summary>
    AlreadyInUse,
    /// <summary>The durable generation is not the one the caller started from: someone else wrote the file, or it was replaced externally.</summary>
    StaleGeneration,
    /// <summary>The main file is damaged or missing: recover before committing.</summary>
    MainDamaged,
    /// <summary>The write failed and nothing durable changed.</summary>
    WriteFailed,
    /// <summary>The bytes written did not read back equal; the previous file was kept.</summary>
    VerificationFailed,
    /// <summary>The write may or may not have become durable: the caller must reload before doing anything else.</summary>
    OutcomeUnknown,
    /// <summary>The main file was written by a newer build: it is neither written nor replaced by a recovery.</summary>
    NewerFormat,
    /// <summary>The durable file is another project or belongs to another ROM (it was replaced externally): it is never overwritten.</summary>
    DifferentProject,
    /// <summary>The folder or the file cannot be written (no permission), or the project was opened only to be read.</summary>
    ReadOnly,
    /// <summary>The file could not be read at all (an input/output error, not bad content): nothing is known about the project and nothing was changed.</summary>
    Unreadable,
    Disposed,
}

internal sealed class NdsProjectStoreException(NdsProjectStoreError kind, string message, Exception? inner = null) : IOException(message, inner)
{
    internal NdsProjectStoreError Kind { get; } = kind;
}

internal enum NdsProjectLoadStatus
{
    /// <summary>No project yet (a new one is created after the ROM is verified).</summary>
    Empty,
    Loaded,
    /// <summary>The main file is damaged or missing. It is never replaced silently: see <see cref="NdsProjectLoadResult.Plan"/>.</summary>
    Damaged,
    /// <summary>Written by a newer build: neither read nor touched.</summary>
    NewerFormat,
}

/// <summary>A valid earlier generation (a backup) that could replace a damaged main file.</summary>
internal sealed record NdsProjectRecoveryCandidate(string Source, long Generation, int OperationCount, DateTime SavedUtc, NdsProjectDocument Document, byte[] Bytes);

/// <summary>What is known when the main file cannot be used: its header (if still trustworthy), the reason, and the valid backups, newest first.</summary>
internal sealed record NdsProjectRecoveryPlan(NdsProjectHeader? DamagedHeader, string DamageMessage, IReadOnlyList<NdsProjectRecoveryCandidate> Candidates)
{
    internal NdsProjectRecoveryCandidate? Recommended => Candidates.Count > 0 ? Candidates[0] : null;
    /// <summary>Operations the damaged file recorded that the candidate does not have, or null when the header is not trustworthy and it cannot be known.</summary>
    internal int? LostOperations(NdsProjectRecoveryCandidate candidate) => DamagedHeader is null ? null : Math.Max(0, DamagedHeader.OperationCount - candidate.OperationCount);
}

internal sealed record NdsProjectLoadResult(NdsProjectLoadStatus Status, NdsProjectDocument? Document, NdsProjectRecoveryPlan? Plan, string Message);

/// <summary>
/// Persistence contract of a DS project. Everything is synchronous and DURABLE when it returns: there is no queue and no background save, so there is nothing
/// pending to lose when the application closes. Real stores (PR 2) write a temporary file in the same folder, flush it to disk, read it back and verify it, and
/// replace the main file atomically keeping up to <see cref="NdsProjectPaths.MaxBackups"/> earlier generations; this contract is what the in-memory store of the
/// tests and those stores must both satisfy.
/// </summary>
internal interface INdsProjectStore : IDisposable
{
    /// <summary>Reads the project. Never throws for damaged content (it is classified) and never modifies anything.</summary>
    NdsProjectLoadResult Load();
    /// <summary>
    /// Makes <paramref name="next"/> (generation = <paramref name="expectedGeneration"/> + 1) durable if and only if the durable generation is still
    /// <paramref name="expectedGeneration"/> (0 when there is no project yet) AND the durable file is the same project of the same ROM. Verified by reading it back.
    /// Throws only <see cref="NdsProjectStoreException"/>; any error other than <see cref="NdsProjectStoreError.OutcomeUnknown"/> guarantees nothing durable
    /// changed. A store that lets any other exception escape has broken this contract, and the caller must treat the outcome as unknown.
    /// </summary>
    long Commit(NdsProjectDocument next, long expectedGeneration);
    /// <summary>
    /// Preserves the damaged main file under a new name (never deleted) and installs the chosen valid backup as the main file. Returns its generation. Refused when
    /// the main file is valid or was written by a newer build, and when the candidate is no longer one of the backups of this project.
    /// </summary>
    long Recover(NdsProjectRecoveryCandidate candidate);
}

/// <summary>The shared "disk" of the in-memory store: two stores over the same medium behave like two instances opening the same file.</summary>
internal sealed class NdsProjectMedium
{
    internal readonly object Gate = new();
    internal byte[]? Main;
    internal readonly List<byte[]> Backups = new();
    internal readonly List<(string Name, byte[] Bytes)> Damaged = new();
    internal object? Lease;
    internal int Preserved => Damaged.Count;
}

/// <summary>Faults an in-memory store can be told to produce at the next commit (one-shot).</summary>
internal enum NdsProjectFault { None, FailBeforeWrite, CorruptWrite, FailAfterPublish }

/// <summary>Reference implementation of the contract, used by the tests (and by later layers' tests) so that the behaviour is specified without a disk.</summary>
internal sealed class InMemoryNdsProjectStore : INdsProjectStore
{
    private readonly NdsProjectMedium _medium; private bool _disposed; private readonly Func<DateTime> _clock;
    internal NdsProjectFault Fault { get; set; }
    internal int Commits { get; private set; }

    internal InMemoryNdsProjectStore(NdsProjectMedium medium, Func<DateTime>? clock = null)
    {
        _medium = medium; _clock = clock ?? (() => DateTime.UtcNow);
        lock (_medium.Gate)
        {
            if (_medium.Lease is not null) throw new NdsProjectStoreException(NdsProjectStoreError.AlreadyInUse, "Otra instancia está usando este proyecto.");
            _medium.Lease = this;
        }
    }

    public void Dispose() { lock (_medium.Gate) { if (!_disposed && ReferenceEquals(_medium.Lease, this)) _medium.Lease = null; _disposed = true; } }
    private void Live() { if (_disposed) throw new NdsProjectStoreException(NdsProjectStoreError.Disposed, "El almacén está cerrado."); }

    public NdsProjectLoadResult Load()
    {
        lock (_medium.Gate)
        {
            Live();
            if (_medium.Main is null && _medium.Backups.Count == 0) return new(NdsProjectLoadStatus.Empty, null, null, "No hay proyecto.");
            if (_medium.Main is not null)
            {
                var read = NdsProjectCodec.Read(_medium.Main);
                if (read.Ok) return new(NdsProjectLoadStatus.Loaded, read.Document, null, "");
                if (read.Status == NdsProjectReadStatus.NewerFormat) return new(NdsProjectLoadStatus.NewerFormat, null, null, read.Message);
                return new(NdsProjectLoadStatus.Damaged, null, Plan(read.Header, read.Message), read.Message);
            }
            return new(NdsProjectLoadStatus.Damaged, null, Plan(null, "Falta el archivo principal del proyecto."), "Falta el archivo principal del proyecto.");
        }
    }

    private NdsProjectRecoveryPlan Plan(NdsProjectHeader? header, string message)
    {
        var candidates = new List<NdsProjectRecoveryCandidate>();
        for (int i = 0; i < _medium.Backups.Count; i++)
        {
            var read = NdsProjectCodec.Read(_medium.Backups[i]);
            if (read.Ok) candidates.Add(new("copia " + (i + 1), read.Document!.Generation, read.Document.OperationCount, read.Document.Meta.SavedUtc, read.Document, _medium.Backups[i]));
        }
        candidates.Sort((a, b) => b.Generation.CompareTo(a.Generation));
        return new(header, message, candidates);
    }

    public long Commit(NdsProjectDocument next, long expectedGeneration)
    {
        lock (_medium.Gate)
        {
            Live();
            if (next.Generation != expectedGeneration + 1) throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "La generación nueva debe ser la esperada más uno.");
            long durable = 0; bool hasMain = _medium.Main is not null;
            if (hasMain)
            {
                var current = NdsProjectCodec.Read(_medium.Main!);
                if (current.Status == NdsProjectReadStatus.NewerFormat) throw new NdsProjectStoreException(NdsProjectStoreError.NewerFormat, "El proyecto lo escribió una versión más reciente: no se modifica. " + current.Message);
                if (!current.Ok) throw new NdsProjectStoreException(NdsProjectStoreError.MainDamaged, "El archivo principal está dañado: recupéralo antes de guardar.");
                durable = current.Document!.Generation;
                if (durable != expectedGeneration) throw new NdsProjectStoreException(NdsProjectStoreError.StaleGeneration, $"El proyecto cambió fuera de esta sesión (generación {durable}, se esperaba {expectedGeneration}).");
                // Same generation is not enough: the file must still be THIS project of THIS ROM (a copy of another project put in its place is never overwritten).
                if (current.Document.Meta.ProjectId != next.Meta.ProjectId || !current.Document.Meta.Rom.SameRomAs(next.Meta.Rom))
                    throw new NdsProjectStoreException(NdsProjectStoreError.DifferentProject, "El archivo guardado es otro proyecto o es de otra ROM: no se sobrescribe.");
            }
            else if (_medium.Backups.Count > 0) throw new NdsProjectStoreException(NdsProjectStoreError.MainDamaged, "Falta el archivo principal: recupéralo antes de guardar.");
            if (durable != expectedGeneration) throw new NdsProjectStoreException(NdsProjectStoreError.StaleGeneration, $"El proyecto cambió fuera de esta sesión (generación {durable}, se esperaba {expectedGeneration}).");
            var fault = Fault; Fault = NdsProjectFault.None;
            if (fault == NdsProjectFault.FailBeforeWrite) throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "No se pudo escribir el proyecto (fallo simulado).");
            byte[] bytes;
            try { bytes = NdsProjectCodec.Serialize(next); }
            catch (InvalidDataException ex) { throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "El proyecto no se puede guardar: " + ex.Message, ex); }
            if (fault == NdsProjectFault.CorruptWrite) { bytes = (byte[])bytes.Clone(); bytes[bytes.Length / 2] ^= 0x5A; }
            // Read back what was "written" and compare it with what was meant to be written, before the previous file is touched.
            var check = NdsProjectCodec.Read(bytes);
            if (!check.Ok || check.Document!.Generation != next.Generation || check.Document.OperationCount != next.OperationCount || !check.Document.State.SameAs(next.State))
                throw new NdsProjectStoreException(NdsProjectStoreError.VerificationFailed, "El proyecto escrito no coincide al releerlo; se conserva el anterior. " + check.Message);
            if (hasMain) { _medium.Backups.Insert(0, _medium.Main!); while (_medium.Backups.Count > NdsProjectPaths.MaxBackups) _medium.Backups.RemoveAt(_medium.Backups.Count - 1); }
            _medium.Main = bytes; Commits++;
            if (fault == NdsProjectFault.FailAfterPublish) throw new NdsProjectStoreException(NdsProjectStoreError.OutcomeUnknown, "No se pudo confirmar si el guardado terminó (fallo simulado después de escribir).");
            return next.Generation;
        }
    }

    public long Recover(NdsProjectRecoveryCandidate candidate)
    {
        lock (_medium.Gate)
        {
            Live();
            if (!NdsProjectCodec.Read(candidate.Bytes).Ok) throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "La copia elegida no es válida.");
            if (!_medium.Backups.Any(b => b.AsSpan().SequenceEqual(candidate.Bytes))) throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "La copia elegida ya no es una de las copias de este proyecto.");
            if (_medium.Main is not null)
            {
                var main = NdsProjectCodec.Read(_medium.Main);
                if (main.Ok) throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "El archivo principal es válido: no se recupera.");
                if (main.Status == NdsProjectReadStatus.NewerFormat) throw new NdsProjectStoreException(NdsProjectStoreError.NewerFormat, "El proyecto lo escribió una versión más reciente: no se sustituye por una copia anterior.");
            }
            if (_medium.Main is not null) _medium.Damaged.Add((NdsProjectPaths.DamagedOf("proyecto", _clock()), _medium.Main));
            _medium.Main = candidate.Bytes;
            return candidate.Generation;
        }
    }
}
