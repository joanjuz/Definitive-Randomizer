using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ThreeDS.Progressive.Services.Nds.Project;

/// <summary>Points of a commit at which the tests can inject a failure or stop the process. Nothing durable has changed before <see cref="BeforeReplace"/> returns.</summary>
internal enum NdsProjectFileStep { BeforeTemp, TempWritten, TempVerified, BackupWritten, BackupsRotated, BeforeReplace, AfterReplace }

/// <summary>
/// The store of a project on disk (the contract of <see cref="INdsProjectStore"/>, in real files next to the main file):
/// <code>
/// Juego.nds.drproj                 the project (main file)
/// Juego.nds.drproj.bak1 … .bak3    the three generations before it, newest first
/// Juego.nds.drproj.lock            held open while an instance uses the project (the system releases it if the process dies)
/// Juego.nds.drproj.tmp-…           a save in progress; an abandoned one is removed by the next save
/// Juego.nds.drproj.damaged-…       a damaged main file set aside by a recovery (never deleted)
/// </code>
/// A commit writes the whole project to a temporary file in the same folder, flushes it to the disk, reads it back and compares it, and only then puts it in
/// place of the main file with one rename that replaces it (<c>MoveFileEx</c>, replace existing, write through). The main file is therefore always either the
/// previous generation or the new one, never a mixture, whenever the process or the machine stops. The backup of the previous generation is a copy made before
/// that rename; it is not flushed, because a backup damaged by a power cut is detected by its checksums and simply not offered.
/// </summary>
internal sealed class NdsProjectFileStore : INdsProjectStore
{
    private const int SharingViolation = unchecked((int)0x80070020), LockViolation = unchecked((int)0x80070021);
    private const int Retries = 20, RetryDelayMs = 50;
    private readonly object _gate = new();
    private readonly FileStream? _lock;
    private readonly Func<DateTime> _clock;
    /// <summary>SHA-256 of the main file as this store last read or wrote it: the file must still be exactly that when the next commit replaces it.</summary>
    private byte[]? _seen;
    private bool _disposed;

    internal string ProjectPath { get; }
    /// <summary>Opened without the lock (the folder cannot be written): it can be read, never committed or recovered.</summary>
    internal bool IsReadOnly => _lock is null;
    /// <summary>Test hook, called at each step of a commit. An exception thrown from it is treated as a failure of the disk at that point.</summary>
    internal Action<NdsProjectFileStep>? Probe { get; set; }

    private NdsProjectFileStore(string path, FileStream? @lock, Func<DateTime>? clock) { ProjectPath = path; _lock = @lock; _clock = clock ?? (() => DateTime.UtcNow); }

    // ------------------------------------------------------------------------------------------------------------------------------ opening

    /// <summary>
    /// Takes the project for this instance. Throws <see cref="NdsProjectStoreError.AlreadyInUse"/> when another instance (in this or another process) holds it,
    /// and <see cref="NdsProjectStoreError.ReadOnly"/> when the folder does not allow writing (the caller can then open it read-only or choose another place).
    /// </summary>
    internal static NdsProjectFileStore Open(string projectPath, bool createFolder = false, Func<DateTime>? clock = null)
    {
        string full = Path.GetFullPath(projectPath), lockPath = NdsProjectPaths.LockOf(full);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (createFolder) Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                // Others may read who holds it (share read); nobody else can open it for writing, so a second instance fails here. Deleted with the handle.
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.DeleteOnClose);
                try
                {
                    stream.SetLength(0); stream.Write(Encoding.UTF8.GetBytes($"pid {Environment.ProcessId} · {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}")); stream.Flush();
                    return new(full, stream, clock);
                }
                catch { stream.Dispose(); throw; }
            }
            catch (IOException ex) when (ex.HResult is SharingViolation or LockViolation)
            {
                throw new NdsProjectStoreException(NdsProjectStoreError.AlreadyInUse, "Otra ventana de Definitive Randomizer está usando este proyecto" + Holder(lockPath) + ". Ciérrala o abre el proyecto allí.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                // Either the folder does not allow writing, or an instance that is closing is still deleting its lock (the name stays busy for an instant).
                if (!NdsProjectLocator.CanWrite(Path.GetDirectoryName(full)!))
                    throw new NdsProjectStoreException(NdsProjectStoreError.ReadOnly, "No se puede escribir en la carpeta del proyecto: " + Path.GetDirectoryName(full), ex);
                if (attempt < Retries) { Thread.Sleep(RetryDelayMs); continue; }
                throw new NdsProjectStoreException(NdsProjectStoreError.AlreadyInUse, "No se pudo tomar el proyecto: su archivo de bloqueo sigue ocupado.", ex);
            }
            catch (IOException ex) { throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "No se puede abrir el proyecto: " + ex.Message, ex); }
        }
    }

    /// <summary>Opens the project only to read it: no lock is taken and nothing is ever written.</summary>
    internal static NdsProjectFileStore OpenReadOnly(string projectPath) => new(Path.GetFullPath(projectPath), null, null);

    private static string Holder(string lockPath)
    {
        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[128]; int read = stream.Read(buffer, 0, buffer.Length);
            return read > 0 ? " (" + Encoding.UTF8.GetString(buffer, 0, read) + ")" : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _lock?.Dispose(); } }
    private void Live() { if (_disposed) throw new NdsProjectStoreException(NdsProjectStoreError.Disposed, "El almacén está cerrado."); }
    private void Writable() { if (_lock is null) throw new NdsProjectStoreException(NdsProjectStoreError.ReadOnly, "El proyecto está abierto en modo de solo lectura."); }

    // ------------------------------------------------------------------------------------------------------------------------------ reading

    /// <summary>The bytes of a file, or null when it does not exist. A file larger than any project is not read (it comes back as an empty, hence invalid, content).</summary>
    private static byte[]? ReadFile(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (stream.Length > NdsProjectCodec.MaxTotalBytes) return Oversized;
                var bytes = new byte[stream.Length]; stream.ReadExactly(bytes);
                return bytes;
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            // Another program (an antivirus, a synchronizer) may hold the file for an instant.
            catch (IOException ex) when (ex.HResult is SharingViolation or LockViolation && attempt < Retries) { Thread.Sleep(RetryDelayMs); }
            catch (EndOfStreamException) when (attempt < Retries) { Thread.Sleep(RetryDelayMs); }
        }
    }
    private static readonly byte[] Oversized = new byte[NdsProjectCodec.HeaderSize];

    private static NdsProjectReadResult Parse(byte[] bytes) => ReferenceEquals(bytes, Oversized)
        ? new(NdsProjectReadStatus.Invalid, "El archivo es mayor de lo que puede ocupar un proyecto.", null, null) : NdsProjectCodec.Read(bytes);

    private IEnumerable<(int Number, string Path)> BackupPaths() => Enumerable.Range(1, NdsProjectPaths.MaxBackups).Select(n => (n, NdsProjectPaths.BackupOf(ProjectPath, n)));

    public NdsProjectLoadResult Load()
    {
        lock (_gate)
        {
            Live();
            try
            {
                byte[]? main = ReadFile(ProjectPath);
                if (main is null)
                {
                    _seen = null;
                    if (!BackupPaths().Any(b => File.Exists(b.Path))) return new(NdsProjectLoadStatus.Empty, null, null, "No hay proyecto.");
                    return new(NdsProjectLoadStatus.Damaged, null, Plan(null, "Falta el archivo principal del proyecto."), "Falta el archivo principal del proyecto.");
                }
                var read = Parse(main);
                if (read.Ok) { _seen = SHA256.HashData(main); return new(NdsProjectLoadStatus.Loaded, read.Document, null, ""); }
                if (read.Status == NdsProjectReadStatus.NewerFormat) return new(NdsProjectLoadStatus.NewerFormat, null, null, read.Message);
                return new(NdsProjectLoadStatus.Damaged, null, Plan(read.Header, read.Message), read.Message);
            }
            catch (Exception ex) when (ex is (IOException or UnauthorizedAccessException) and not NdsProjectStoreException)
            { throw new NdsProjectStoreException(NdsProjectStoreError.Unreadable, "No se puede leer el proyecto: " + ex.Message, ex); }
        }
    }

    private NdsProjectRecoveryPlan Plan(NdsProjectHeader? header, string message)
    {
        var candidates = new List<NdsProjectRecoveryCandidate>();
        foreach (var (number, path) in BackupPaths())
        {
            byte[]? bytes = ReadFile(path); if (bytes is null) continue;
            var read = Parse(bytes);
            if (read.Ok) candidates.Add(new("copia " + number, read.Document!.Generation, read.Document.OperationCount, read.Document.Meta.SavedUtc, read.Document, bytes));
        }
        candidates.Sort((a, b) => b.Generation.CompareTo(a.Generation));
        return new(header, message, candidates);
    }

    // ------------------------------------------------------------------------------------------------------------------------------- commit

    public long Commit(NdsProjectDocument next, long expectedGeneration)
    {
        lock (_gate)
        {
            Live(); Writable();
            if (next.Generation != expectedGeneration + 1) throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "La generación nueva debe ser la esperada más uno.");
            string temp = NdsProjectPaths.TempOf(ProjectPath, Guid.NewGuid().ToString("N")), backupTemp = temp + ".bak";
            byte[] bytes; byte[]? old;
            // ---- Phase 1: everything that can fail without having touched the main file. Any failure here leaves the durable project exactly as it was.
            try
            {
                old = ReadFile(ProjectPath);
                if (old is not null)
                {
                    var current = Parse(old);
                    if (current.Status == NdsProjectReadStatus.NewerFormat) throw new NdsProjectStoreException(NdsProjectStoreError.NewerFormat, "El proyecto lo escribió una versión más reciente: no se modifica. " + current.Message);
                    if (!current.Ok) throw new NdsProjectStoreException(NdsProjectStoreError.MainDamaged, "El archivo principal está dañado: recupéralo antes de guardar.");
                    long durable = current.Document!.Generation;
                    if (durable != expectedGeneration) throw new NdsProjectStoreException(NdsProjectStoreError.StaleGeneration, $"El proyecto cambió fuera de esta sesión (generación {durable}, se esperaba {expectedGeneration}).");
                    if (current.Document.Meta.ProjectId != next.Meta.ProjectId || !current.Document.Meta.Rom.SameRomAs(next.Meta.Rom))
                        throw new NdsProjectStoreException(NdsProjectStoreError.DifferentProject, "El archivo guardado es otro proyecto o es de otra ROM: no se sobrescribe.");
                    // Same generation and same project, but not the bytes this instance last saw: something rewrote the file.
                    if (_seen is not null && !SHA256.HashData(old).AsSpan().SequenceEqual(_seen))
                        throw new NdsProjectStoreException(NdsProjectStoreError.StaleGeneration, "El archivo del proyecto se modificó fuera de esta sesión: no se sobrescribe.");
                }
                else
                {
                    if (BackupPaths().Any(b => File.Exists(b.Path))) throw new NdsProjectStoreException(NdsProjectStoreError.MainDamaged, "Falta el archivo principal: recupéralo antes de guardar.");
                    if (expectedGeneration != 0) throw new NdsProjectStoreException(NdsProjectStoreError.StaleGeneration, $"El proyecto cambió fuera de esta sesión (no hay archivo, se esperaba la generación {expectedGeneration}).");
                }
                Probe?.Invoke(NdsProjectFileStep.BeforeTemp);
                try { bytes = NdsProjectCodec.Serialize(next); }
                catch (InvalidDataException ex) { throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "El proyecto no se puede guardar: " + ex.Message, ex); }
                RemoveAbandonedTemps();
                WriteDurable(temp, bytes);
                Probe?.Invoke(NdsProjectFileStep.TempWritten);
                // Read back from the disk what was written, and compare it with what was meant to be written, before the previous file is touched.
                // (The container and every table are checked; the operations are not deserialized again: see NdsProjectCodec.Verify.)
                byte[]? back = ReadFile(temp); var check = back is null ? null : NdsProjectCodec.Verify(back);
                if (back is null || !back.AsSpan().SequenceEqual(bytes) || check is not { Ok: true } || check.Document!.Generation != next.Generation || !check.Document.State.SameAs(next.State))
                    throw new NdsProjectStoreException(NdsProjectStoreError.VerificationFailed, "El proyecto escrito no coincide al releerlo; se conserva el anterior. " + check?.Message);
                Probe?.Invoke(NdsProjectFileStep.TempVerified);
                if (old is not null) Backup(old, backupTemp);
                Probe?.Invoke(NdsProjectFileStep.BeforeReplace);
            }
            catch (Exception ex)
            {
                TryDelete(temp); TryDelete(backupTemp);
                if (ex is NdsProjectStoreException) throw;
                if (ex is UnauthorizedAccessException) throw new NdsProjectStoreException(NdsProjectStoreError.ReadOnly, "No se puede escribir en la carpeta del proyecto: " + ex.Message, ex);
                throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "No se pudo escribir el proyecto; se conserva el anterior. " + ex.Message, ex);
            }
            // ---- Phase 2: the replacement. From here the answer depends on what the main file is afterwards, not on which call failed.
            try
            {
                Replace(temp, ProjectPath);
                Probe?.Invoke(NdsProjectFileStep.AfterReplace);
                byte[]? now = ReadFile(ProjectPath);
                if (now is null || !now.AsSpan().SequenceEqual(bytes)) throw new IOException("El archivo principal no es el que se acaba de guardar.");
                _seen = SHA256.HashData(bytes);
                return next.Generation;
            }
            catch (Exception ex)
            {
                // Is the main file still, provably, the previous one? Then nothing changed and this is an ordinary failure. Anything else is not known.
                bool unchanged;
                try { byte[]? now = ReadFile(ProjectPath); unchanged = old is null ? now is null : now is not null && now.AsSpan().SequenceEqual(old); }
                catch (Exception inner) when (inner is IOException or UnauthorizedAccessException) { unchanged = false; }
                if (unchanged)
                {
                    TryDelete(temp);
                    // "Access denied" is what Windows answers both when writing is not allowed and when another program holds the file open: tell them apart.
                    bool denied = ex is UnauthorizedAccessException && (IsMarkedReadOnly(ProjectPath) || !NdsProjectLocator.CanWrite(Path.GetDirectoryName(ProjectPath)!));
                    throw new NdsProjectStoreException(denied ? NdsProjectStoreError.ReadOnly : NdsProjectStoreError.WriteFailed, (denied ? "No se puede escribir el archivo del proyecto (solo lectura); se conserva el anterior. " : "No se pudo sustituir el archivo del proyecto (puede que otro programa lo tenga abierto); se conserva el anterior. ") + ex.Message, ex);
                }
                throw new NdsProjectStoreException(NdsProjectStoreError.OutcomeUnknown, "No se pudo confirmar si el guardado terminó. " + ex.Message, ex);
            }
        }
    }

    /// <summary>Writes a file that did not exist and does not return until its bytes are on the disk.</summary>
    private static void WriteDurable(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
        stream.Write(bytes); stream.Flush(true);
    }

    /// <summary>
    /// Keeps the generation that is about to be replaced as backup 1 and moves the others down (the fourth is dropped). When backup 1 already is that generation
    /// (an earlier save got this far and then failed) nothing is rotated, so a failed save never costs a backup.
    /// </summary>
    private void Backup(byte[] old, string backupTemp)
    {
        string first = NdsProjectPaths.BackupOf(ProjectPath, 1);
        if (ReadFile(first) is { } existing && existing.AsSpan().SequenceEqual(old)) return;
        File.WriteAllBytes(backupTemp, old);
        Probe?.Invoke(NdsProjectFileStep.BackupWritten);
        for (int n = NdsProjectPaths.MaxBackups; n > 1; n--)
        {
            string from = NdsProjectPaths.BackupOf(ProjectPath, n - 1), to = NdsProjectPaths.BackupOf(ProjectPath, n);
            if (File.Exists(from)) Replace(from, to, false);
        }
        Replace(backupTemp, first, false);
        Probe?.Invoke(NdsProjectFileStep.BackupsRotated);
    }

    /// <summary>Temporary files of saves that never finished (this instance holds the lock, so none of them is in use).</summary>
    private void RemoveAbandonedTemps()
    {
        string folder = Path.GetDirectoryName(ProjectPath)!, prefix = Path.GetFileName(NdsProjectPaths.TempOf(ProjectPath, ""));
        foreach (string stale in Directory.EnumerateFiles(folder, prefix + "*")) if (Path.GetFileName(stale).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) TryDelete(stale);
    }

    private static bool IsMarkedReadOnly(string path)
    {
        try { return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>One rename that replaces the destination, flushed to the disk before it returns. Retried while another program holds either file for an instant.</summary>
    private static void Replace(string from, string to, bool durable = true)
    {
        for (int attempt = 0; ; attempt++)
        {
            if (MoveFileExW(Extended(from), Extended(to), durable ? MoveFileReplaceExisting | MoveFileWriteThrough : MoveFileReplaceExisting)) return;
            int error = Marshal.GetLastWin32Error();
            if (error is 32 or 33 or 5 && attempt < Retries) { Thread.Sleep(RetryDelayMs); continue; }
            var inner = new Win32Exception(error);
            if (error == 5) throw new UnauthorizedAccessException("Acceso denegado al sustituir " + Path.GetFileName(to) + ": " + inner.Message, inner);
            throw new IOException("No se pudo sustituir " + Path.GetFileName(to) + ": " + inner.Message, inner);
        }
    }

    private static string Extended(string path)
    {
        string full = Path.GetFullPath(path);
        return full.StartsWith(@"\\?\", StringComparison.Ordinal) ? full : full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

    private const int MoveFileReplaceExisting = 0x1, MoveFileWriteThrough = 0x8;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string existing, string replacement, int flags);

    // ------------------------------------------------------------------------------------------------------------------------------ recover

    public long Recover(NdsProjectRecoveryCandidate candidate)
    {
        lock (_gate)
        {
            Live(); Writable();
            string temp = NdsProjectPaths.TempOf(ProjectPath, Guid.NewGuid().ToString("N"));
            try
            {
                if (!NdsProjectCodec.Read(candidate.Bytes).Ok) throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "La copia elegida no es válida.");
                if (!BackupPaths().Any(b => ReadFile(b.Path) is { } bytes && bytes.AsSpan().SequenceEqual(candidate.Bytes)))
                    throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "La copia elegida ya no es una de las copias de este proyecto.");
                byte[]? main = ReadFile(ProjectPath);
                if (main is not null)
                {
                    var read = Parse(main);
                    if (read.Ok) throw new NdsProjectStoreException(NdsProjectStoreError.WriteFailed, "El archivo principal es válido: no se recupera.");
                    if (read.Status == NdsProjectReadStatus.NewerFormat) throw new NdsProjectStoreException(NdsProjectStoreError.NewerFormat, "El proyecto lo escribió una versión más reciente: no se sustituye por una copia anterior.");
                }
                // The replacement is made durable first; only then is the damaged file set aside (renamed, never deleted or overwritten) and the copy put in its place.
                // If the process stops in between, the main file is missing and the copies are intact: the same recovery is offered again.
                WriteDurable(temp, candidate.Bytes);
                if (main is not null)
                {
                    string damaged = NdsProjectPaths.DamagedOf(ProjectPath, _clock());
                    for (int n = 2; File.Exists(damaged); n++) damaged = NdsProjectPaths.DamagedOf(ProjectPath, _clock()) + "-" + n;
                    File.Move(ProjectPath, damaged, false);
                }
                Replace(temp, ProjectPath);
                _seen = SHA256.HashData(candidate.Bytes);
                return candidate.Generation;
            }
            catch (Exception ex) when (ex is (IOException or UnauthorizedAccessException) and not NdsProjectStoreException)
            {
                TryDelete(temp);
                throw new NdsProjectStoreException(ex is UnauthorizedAccessException ? NdsProjectStoreError.ReadOnly : NdsProjectStoreError.WriteFailed, "No se pudo recuperar el proyecto: " + ex.Message, ex);
            }
        }
    }
}

/// <summary>Where the project of a ROM is, or where it can be created.</summary>
internal sealed record NdsProjectLocation(string ProjectPath, bool BesideRom, bool Exists, bool FolderWritable);

/// <summary>
/// Decides where the project of a ROM lives. Next to the ROM whenever that folder can be written; otherwise in a private folder of the user, in a subfolder
/// named after the SHA-256 of the ROM (so it is found again wherever the ROM is moved, and never by its name). Nothing is created here: using the alternative
/// place for a NEW project is the user's decision (<see cref="NeedsConsent"/>).
/// </summary>
internal sealed record NdsProjectResolution(NdsProjectLocation Beside, NdsProjectLocation Alternative)
{
    /// <summary>An existing project wins (next to the ROM first); otherwise next to the ROM when it can be written; otherwise the alternative place.</summary>
    internal NdsProjectLocation Preferred => Beside.Exists ? Beside : Alternative.Exists ? Alternative : Beside.FolderWritable ? Beside : Alternative;
    /// <summary>A new project would have to be created away from the ROM: ask before doing it.</summary>
    internal bool NeedsConsent => !Preferred.Exists && !Preferred.BesideRom;
    /// <summary>There is a project in both places (for example the ROM folder became writable later): the caller must not pick one silently.</summary>
    internal bool Ambiguous => Beside.Exists && Alternative.Exists;
    /// <summary>The project exists where it cannot be written: it can only be opened read-only.</summary>
    internal bool ReadOnly => Preferred.Exists && !Preferred.FolderWritable;
}

internal static class NdsProjectLocator
{
    internal static string DefaultAlternativeRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DefinitiveRandomizer", "Projects");

    /// <summary>The alternative path depends only on the identity of the ROM, never on its name or folder.</summary>
    internal static string AlternativeOf(string root, NdsProjectRom rom) =>
        Path.Combine(root, rom.Sha256[..32].ToUpperInvariant(), rom.GameCode + "-rev" + rom.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".nds" + NdsProjectPaths.Extension);

    internal static NdsProjectResolution Resolve(string romPath, NdsProjectRom rom, string? alternativeRoot = null)
    {
        string beside = NdsProjectPaths.ProjectOf(Path.GetFullPath(romPath)), alternative = AlternativeOf(alternativeRoot ?? DefaultAlternativeRoot, rom);
        return new(new(beside, true, HasProject(beside), CanWrite(Path.GetDirectoryName(beside)!)), new(alternative, false, HasProject(alternative), CanWrite(Path.GetDirectoryName(alternative)!)));
    }

    /// <summary>A main file or any of its backups: a project whose main file is missing still exists (it is recoverable) and must not be shadowed by a new one.</summary>
    private static bool HasProject(string projectPath) => File.Exists(projectPath) || Enumerable.Range(1, NdsProjectPaths.MaxBackups).Any(n => File.Exists(NdsProjectPaths.BackupOf(projectPath, n)));

    /// <summary>
    /// Whether files can be created in a folder, found out the only reliable way: by creating one (removed with its handle). A folder that does not exist yet is
    /// judged by its nearest existing ancestor.
    /// </summary>
    internal static bool CanWrite(string folder)
    {
        string? existing = folder;
        while (existing is not null && !Directory.Exists(existing)) existing = Path.GetDirectoryName(existing);
        if (existing is null) return false;
        try
        {
            using var probe = new FileStream(Path.Combine(existing, ".drproj-" + Guid.NewGuid().ToString("N") + ".tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
