using System.IO;
using System.Security.Cryptography;

namespace ThreeDS.Progressive.Services;

/// <summary>Frozen bytes and a source fingerprint; never holds the live configuration.</summary>
internal sealed class FileChange(string path, byte[] original, byte[] proposed)
{
    internal string Path { get; } = ExistingFilePath(path);
    // File.Replace adopts the destination spelling on Windows. RomFS lookup is
    // case sensitive, so retain the existing directory entry even if a caller
    // used a different spelling (for example DLLBattle versus DllBattle).
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

/// <summary>Stage every file, check every source, then replace. Roll back failures from memory.</summary>
internal static class FileTransaction
{
    internal static void Commit(IReadOnlyList<FileChange> files, Action<int>? afterReplace = null,RecoveryJournal? recovery=null)
    {
        if (files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new InvalidDataException("La propuesta contiene un archivo repetido.");
        var temporary = new Dictionary<string, string>();
        var replaced = new List<FileChange>();
        try
        {
            foreach (var f in files)
            {
                GarcTransaction.ReadUnchanged(f.Path, f.Fingerprint);
                string temp = recovery?.TemporaryPath(f.Path)??f.Path + ".studio-" + Guid.NewGuid().ToString("N") + ".tmp";
                temporary.Add(f.Path, temp);
                WriteFlushed(temp, f.Proposed());
                if (!File.ReadAllBytes(temp).AsSpan().SequenceEqual(f.Proposed()))
                    throw new IOException("No coincide la propuesta preparada: " + f.Path);
            }
            foreach (var f in files) GarcTransaction.ReadUnchanged(f.Path, f.Fingerprint);
            foreach (var f in files)
            {
                // Detect a writer between the initial checks and this replacement.
                GarcTransaction.ReadUnchanged(f.Path, f.Fingerprint);
                File.Replace(temporary[f.Path], f.Path, null);
                replaced.Add(f);
                if (!File.ReadAllBytes(f.Path).AsSpan().SequenceEqual(f.Proposed()))
                    throw new IOException("Falló la relectura del archivo guardado: " + f.Path);
                afterReplace?.Invoke(replaced.Count);
            }
            recovery?.Finish(true);
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<string>();
            foreach (var f in replaced.AsEnumerable().Reverse())
            {
                try
                {
                    // Never overwrite a concurrent edit while recovering our own writes.
                    if (!File.ReadAllBytes(f.Path).AsSpan().SequenceEqual(f.Proposed()))
                        throw new IOException("El archivo fue modificado por otro proceso.");
                    string temp = temporary[f.Path];
                    WriteFlushed(temp, f.Original());
                    File.Replace(temp, f.Path, null);
                    if (!File.ReadAllBytes(f.Path).AsSpan().SequenceEqual(f.Original()))
                        throw new IOException("La restauración no coincide.");
                }
                catch (Exception ex) { rollbackFailures.Add(f.Path + ": " + ex.Message); }
            }
            if (rollbackFailures.Count != 0)
                throw new IOException("Falló la aplicación y no se pudieron restablecer todos los archivos. "
                    + string.Join("; ", rollbackFailures), failure);
            recovery?.Finish(false);
            throw;
        }
        finally
        {
            foreach (string temp in temporary.Values)
                if (File.Exists(temp)) File.Delete(temp);
        }
    }
    private static void WriteFlushed(string path,byte[] bytes)
    {using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);stream.Write(bytes);stream.Flush(true);}
}
