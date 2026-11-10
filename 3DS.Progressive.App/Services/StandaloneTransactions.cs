using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ThreeDS.Progressive.Services;

/// <summary>Durable recovery for callers without project history. Never stores metadata in RomFS/ExeFS.</summary>
internal static class StandaloneTransactions
{
    private static string Storage => Path.Combine(Environment.GetEnvironmentVariable("PROGRESSIVE_STATE_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DefinitiveRandomizer"),"file-transactions");

    internal static RecoveryJournal Begin(IReadOnlyList<FileChange> files)
    {
        string root=Path.GetDirectoryName(files[0].Path)!;
        foreach(var file in files)
            while(!Within(file.Path,root))
                root=Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root))
                    ?? throw new IOException("Los destinos del guardado deben estar en el mismo volumen.");
        RecoverPending(root);
        string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
        Directory.CreateDirectory(Storage);
        return RecoveryJournal.Begin(Path.Combine(Storage,id+".pending"),root,Path.Combine(root,"unused-history.json"),files);
    }

    private static bool Within(string path,string root) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);

    /// <summary>Called before project data is loaded. Diagnostic/rebuild/backup callers only reject.</summary>
    internal static void RecoverPending(string root,bool recover=true)
    {
        if(!Directory.Exists(Storage))return;
        foreach(string directory in Directory.GetDirectories(Storage,"*.pending"))
        {
            string[] targets=RecoveryJournal.Targets(directory);
            if(!targets.Any(p=>Within(p,root)))continue;
            if(!recover)throw new InterruptedSaveException(Instructions(directory),directory);
            if(targets.Any(p=>!Within(p,root)))
                throw new IOException(Instructions(directory));
            try { RecoveryJournal.Recover(directory,root,Path.Combine(root,"unused-history.json")); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            { throw new IOException(ex.Message+" "+Instructions(directory),ex); }
        }
    }

    internal static string Instructions(string directory) =>
        "Hay un guardado individual pendiente de recuperación: "+directory+
        ". Cierra los otros programas. Conserva cualquier archivo externo en una copia separada y libera su nombre " +
        "solo después de verificar esa copia; vuelve a abrir el proyecto para recuperar. " +
        "No borres los archivos .old.tmp ni este registro. Si no hay conflicto, reabrir recupera automáticamente.";
}
