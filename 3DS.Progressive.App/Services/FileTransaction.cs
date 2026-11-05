using System.IO;
using System.Security.Cryptography;

namespace ThreeDS.Progressive.Services;

/// <summary>Frozen bytes and a source fingerprint; never holds the live configuration.</summary>
internal sealed class FileChange(string path, byte[] original, byte[] proposed)
{
    internal string Path { get; } = ExistingFilePath(path);
    // The replacement takes this exact spelling. RomFS lookup is case sensitive,
    // so retain the existing directory entry even if a caller used a different
    // spelling (for example DLLBattle versus DllBattle).
    private static string ExistingFilePath(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string? directory = System.IO.Path.GetDirectoryName(full);
        if (directory is null || !Directory.Exists(directory)) return full;
        string name = System.IO.Path.GetFileName(full);
        return Directory.EnumerateFiles(directory)
            .FirstOrDefault(candidate => string.Equals(System.IO.Path.GetFileName(candidate), name, StringComparison.OrdinalIgnoreCase)) ?? full;
    }
    private readonly byte[] _original = (byte[])original.Clone();
    private readonly byte[] _proposed = (byte[])proposed.Clone();
    internal byte[] Fingerprint { get; } = SHA256.HashData(original);
    internal byte[] Original() => (byte[])_original.Clone();
    internal byte[] Proposed() => (byte[])_proposed.Clone();
    internal bool Changed => !_original.AsSpan().SequenceEqual(_proposed);
}

/// <summary>
/// Stage every file, then hold and check every target, and only then replace. A held target
/// cannot change, so no window exists between its last check and its replacement or rollback.
/// A replacement is two renames: the original steps aside and the staged file takes the name
/// without replacing anything. Whoever creates that name in between keeps it.
/// </summary>
internal static class FileTransaction
{
    private sealed class Slot(FileChange file,string staged,string retired):IDisposable
    {
        internal FileChange File{get;}=file;
        internal string Staged{get;}=staged;
        internal string Retired{get;}=retired;
        internal ExclusiveFile? Original,Replacement;
        internal bool Vacated,Installed;
        public void Dispose(){Original?.Dispose();Replacement?.Dispose();}
    }
    internal static void Commit(IReadOnlyList<FileChange> files, Action<int>? afterReplace = null,RecoveryJournal? recovery=null)
    {
        if (files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new InvalidDataException("La propuesta contiene un archivo repetido.");
        if(files.Count==0)return;
        if(recovery is null)
        {
            using var durable=StandaloneTransactions.Begin(files);
            try { Commit(files,afterReplace,durable); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {
                if(Directory.Exists(durable.DirectoryPath))
                    throw new IOException(ex.Message+" "+StandaloneTransactions.Instructions(durable.DirectoryPath),ex);
                throw;
            }
            return;
        }
        string token = recovery?.Token ?? Guid.NewGuid().ToString("N");
        var slots = new List<Slot>();
        bool completed = false;
        try
        {
            foreach (var f in files)
            {
                GarcTransaction.ReadUnchanged(f.Path, f.Fingerprint);
                string temp = f.Path + ".studio-" + token + ".tmp";
                slots.Add(new Slot(f, temp, ExclusiveFile.RetiredPath(f.Path, token)));
                WriteFlushed(temp, f.Proposed());
                if (!File.ReadAllBytes(temp).AsSpan().SequenceEqual(f.Proposed()))
                    throw new IOException("No coincide la propuesta preparada: " + f.Path);
                ExclusiveFile.CopyMetadata(f.Path, temp);
            }
            // Hold the whole set first: a target that cannot be held stops the transaction before any change.
            foreach (var slot in slots)
            {
                slot.Original = ExclusiveFile.Open(slot.File.Path);
                GarcTransaction.RequireUnchanged(slot.Original.ReadAll(), slot.File.Fingerprint);
            }
            int replaced = 0;
            foreach (var slot in slots)
            {
                var f = slot.File;
                slot.Replacement = ExclusiveFile.Open(slot.Staged);
                if (!slot.Replacement.ReadAll().AsSpan().SequenceEqual(f.Proposed()))
                    throw new IOException("No coincide la propuesta preparada: " + f.Path);
                slot.Original!.MoveTo(slot.Retired);
                slot.Vacated = true;
                slot.Replacement.MoveTo(f.Path);
                slot.Installed = true;
                if (!slot.Replacement.ReadAll().AsSpan().SequenceEqual(f.Proposed()))
                    throw new IOException("Falló la relectura del archivo guardado: " + f.Path);
                afterReplace?.Invoke(++replaced);
            }
            recovery?.MarkComplete();
            completed = true;
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<string>();
            foreach (var slot in slots.AsEnumerable().Reverse().Where(s => s.Vacated))
            {
                try { Restore(slot); }
                catch (Exception ex) { rollbackFailures.Add(slot.File.Path + ": " + ex.Message + " Original conservado en " + slot.Retired); }
            }
            if (rollbackFailures.Count != 0)
                throw new IOException("Falló la aplicación y no se pudieron restablecer todos los archivos. "
                    + string.Join("; ", rollbackFailures), failure);
            // Cleanup needs the handles gone. A journal left behind by a failure here is harmless:
            // every target is back, so the next Recover only removes it. Keep the real cause.
            foreach (var slot in slots) slot.Dispose();
            try { recovery?.Finish(false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
        finally
        {
            foreach (var slot in slots)
            {
                if (completed) TryDeleteOnClose(slot.Original);
                slot.Dispose();
                BestEffortDelete(slot.Staged);
                // A retired original that rollback could not put back is the evidence to keep.
                if (completed) BestEffortDelete(slot.Retired);
            }
        }
        recovery?.Finish(true);
    }
    private static void Restore(Slot slot)
    {
        // Both handles stayed open, so nobody else could have written these bytes.
        if (slot.Installed)
        {
            if (!slot.Replacement!.ReadAll().AsSpan().SequenceEqual(slot.File.Proposed()))
                throw new IOException("El archivo fue modificado por otro proceso.");
            slot.Replacement.MoveTo(slot.Staged);
            slot.Installed = false;
        }
        slot.Original!.MoveTo(slot.File.Path);
        slot.Vacated = false;
        if (!slot.Original.ReadAll().AsSpan().SequenceEqual(slot.File.Original()))
            throw new IOException("La restauración no coincide.");
    }
    // The commit already succeeded: a scanner holding a finished file must not turn it into a failure.
    private static void TryDeleteOnClose(ExclusiveFile? file)
    {try{file?.DeleteOnClose();}catch(IOException){}}
    private static void BestEffortDelete(string path)
    {try{if(File.Exists(path))File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
    private static void WriteFlushed(string path,byte[] bytes)
    {using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);stream.Write(bytes);stream.Flush(true);}
}
