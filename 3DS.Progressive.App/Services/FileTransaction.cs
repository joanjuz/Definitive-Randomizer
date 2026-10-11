using System.IO;
using System.Security.Cryptography;

namespace ThreeDS.Progressive.Services;

/// <summary>Frozen bytes and a source fingerprint; never holds the live configuration.</summary>
internal sealed class FileChange(string path, byte[] original, byte[] proposed)
{
    internal string Path { get; } = System.IO.Path.GetFullPath(path);
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
    internal static void Commit(IReadOnlyList<FileChange> files, Action<int>? afterReplace = null)
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
                string temp = f.Path + ".studio-" + Guid.NewGuid().ToString("N") + ".tmp";
                temporary.Add(f.Path, temp);
                File.WriteAllBytes(temp, f.Proposed());
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
                    File.WriteAllBytes(temp, f.Original());
                    File.Replace(temp, f.Path, null);
                    if (!File.ReadAllBytes(f.Path).AsSpan().SequenceEqual(f.Original()))
                        throw new IOException("La restauración no coincide.");
                }
                catch (Exception ex) { rollbackFailures.Add(f.Path + ": " + ex.Message); }
            }
            if (rollbackFailures.Count != 0)
                throw new IOException("Falló la aplicación y no se pudieron restablecer todos los archivos. "
                    + string.Join("; ", rollbackFailures), failure);
            throw;
        }
        finally
        {
            foreach (string temp in temporary.Values)
                if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
