using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

/// <summary>Files an entry replaced, as they were before it (relative path, hashes and stored copy).</summary>
internal sealed record RestorePointFile(string Path,string BeforeSha256,string AfterSha256,string Blob);
internal sealed record RestorePointManifest(Guid Entry,DateTimeOffset Time,string Title,string? Section,IReadOnlyList<RestorePointFile> Files);

/// <summary>
/// Restore points for «Deshacer»: after a verified commit, the original bytes of the game files it replaced are copied
/// next to the history («.progressive-restore», excluded from backups). Best effort: a restore point can never make a save
/// fail. Only the newest <see cref="Keep"/> points are kept and a commit larger than <see cref="MaxBytes"/> gets none.
/// Undoing checks that the files are still exactly as the entry left them, writes the originals back in one verified
/// transaction and records it as «Restaurar archivos · deshacer …», which recipes do not replay.
/// </summary>
internal static class RestorePoints
{
    internal const string FolderName=".progressive-restore";
    internal const int Keep=8;
    internal const long MaxBytes=256L*1024*1024;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private static string Root(string storage)=>Path.Combine(storage,FolderName);
    private static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));

    internal static void Save(string storage,string projectRoot,HistoryEntry entry,IReadOnlyList<FileChange> files)
    {
        try
        {
            var game=files.Where(f=>f.Changed).ToArray();
            if(game.Length==0||game.Sum(f=>(long)f.Original().Length)>MaxBytes)return;
            string folder=Path.Combine(Root(storage),entry.Id.ToString("N"));
            Directory.CreateDirectory(folder);
            var rows=new List<RestorePointFile>();
            for(int i=0;i<game.Length;i++)
            {
                byte[] original=game[i].Original();string blob=$"{i:000}.bin";
                File.WriteAllBytes(Path.Combine(folder,blob),original);
                rows.Add(new(Path.GetRelativePath(projectRoot,game[i].Path),Hash(original),Hash(game[i].Proposed()),blob));
            }
            var title=ThreeDS.Progressive.SectionProgress.Describe(entry).Title;
            File.WriteAllText(Path.Combine(folder,"manifest.json"),JsonSerializer.Serialize(new RestorePointManifest(entry.Id,entry.Time,title,entry.Section,rows),Json));
            Prune(storage);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){}
    }
    private static void Prune(string storage)
    {
        var points=Directory.Exists(Root(storage))?new DirectoryInfo(Root(storage)).GetDirectories().OrderByDescending(d=>d.CreationTimeUtc).ToArray():[];
        foreach(var old in points.Skip(Keep))try{old.Delete(true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    internal static RestorePointManifest? Find(string storage,Guid entry)
    {
        string path=Path.Combine(Root(storage),entry.ToString("N"),"manifest.json");
        try{return File.Exists(path)?JsonSerializer.Deserialize<RestorePointManifest>(File.ReadAllText(path)):null;}
        catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException){return null;}
    }
    /// <summary>Null when the entry can be undone; otherwise why not.</summary>
    internal static string? WhyNot(string storage,string projectRoot,Guid entry)
    {
        var manifest=Find(storage,entry);
        if(manifest is null)return "No hay punto de restauración para este cambio (solo se guardan los últimos "+Keep+").";
        foreach(var file in manifest.Files)
        {
            string path=Path.Combine(projectRoot,file.Path);
            if(!File.Exists(path))return "Falta el archivo "+file.Path+".";
            if(!string.Equals(Hash(File.ReadAllBytes(path)),file.AfterSha256,StringComparison.OrdinalIgnoreCase))
                return "Un cambio posterior modificó "+file.Path+". Deshaz antes los cambios más recientes.";
            if(!File.Exists(Path.Combine(Root(storage),entry.ToString("N"),file.Blob)))return "El punto de restauración está incompleto.";
        }
        return null;
    }
    /// <summary>Writes back the files as they were before the entry and records it in the history.</summary>
    internal static void Undo(ProjectHistory history,string projectRoot,Guid entry)
    {
        string storage=history.StorageDirectory;
        if(WhyNot(storage,projectRoot,entry) is string reason)throw new InvalidOperationException(reason);
        var manifest=Find(storage,entry)!;
        string folder=Path.Combine(Root(storage),entry.ToString("N"));
        var changes=new List<FileChange>();
        foreach(var file in manifest.Files)
        {
            string path=Path.Combine(projectRoot,file.Path);byte[] original=File.ReadAllBytes(Path.Combine(folder,file.Blob));
            if(!string.Equals(Hash(original),file.BeforeSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("La copia de "+file.Path+" no coincide con su huella.");
            // Validate the same captured bytes that FileChange will fingerprint; WhyNot is only a preflight.
            byte[] current=File.ReadAllBytes(path);
            if(!string.Equals(Hash(current),file.AfterSha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Un cambio posterior modificó "+file.Path+". Deshaz antes los cambios más recientes.");
            changes.Add(new FileChange(path,current,original));
        }
        history.Commit(changes,"Restauración","Restaurar archivos · deshacer «"+manifest.Title+"»",[],
            new Dictionary<string,string>{{"UndoOf",entry.ToString()}},section:manifest.Section);
        try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    /// <summary>The newest entry that still can be undone, for «Deshacer último cambio».</summary>
    internal static HistoryEntry? LatestUndoable(ProjectHistory history,string projectRoot)
    {
        foreach(var entry in history.Read().Entries.Reverse())
        {
            if(entry.Parameters.ContainsKey("UndoOf"))continue;
            if(Find(history.StorageDirectory,entry.Id) is null)continue;
            return WhyNot(history.StorageDirectory,projectRoot,entry.Id) is null?entry:null;
        }
        return null;
    }
}
