using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

/// <summary>Disk-backed rollback for interrupted project/history commits. Unknown edits are never overwritten.</summary>
internal sealed class RecoveryJournal : IDisposable
{
    private sealed record Entry(string Path,string Before,string After);
    private sealed record Manifest(int Version,bool Complete,string Token,Entry[] Files,bool Ready=false);
    private readonly string _directory;
    private readonly FileStream _lease;
    private Manifest _manifest;
    private RecoveryJournal(string directory,FileStream lease,Manifest manifest){_directory=directory;_lease=lease;_manifest=manifest;}
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static void Write(string path,byte[] bytes)
    {using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);stream.Write(bytes);stream.Flush(true);}
    private void Save()
    {
        string pending=Path.Combine(_directory,"manifest.tmp"),target=Path.Combine(_directory,"manifest.json");
        Write(pending,JsonSerializer.SerializeToUtf8Bytes(_manifest));File.Move(pending,target,true);
    }
    private static void ValidateTargets(Manifest manifest,string root,string history)
    {
        if(manifest.Version!=1||!Guid.TryParseExact(manifest.Token,"N",out _)||manifest.Files is null||manifest.Files.Length==0||manifest.Files.Length>4096||
            manifest.Files.Any(f=>f is null||string.IsNullOrWhiteSpace(f.Path)||f.Before is null||f.After is null)||
            manifest.Files.Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=manifest.Files.Length)
            throw new InvalidDataException("Registro de recuperación incompatible.");
        string prefix=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar;
        foreach(var file in manifest.Files)
        {
            string full=Path.GetFullPath(file.Path);
            if(!string.Equals(file.Path,full,StringComparison.OrdinalIgnoreCase)||
                !full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&!string.Equals(full,Path.GetFullPath(history),StringComparison.OrdinalIgnoreCase)||
                file.Before.Length!=64||file.After.Length!=64)
                throw new InvalidDataException("La recuperación contiene un archivo fuera del proyecto.");
        }
    }
    internal static RecoveryJournal Begin(string directory,string root,string history,IReadOnlyList<FileChange> files)
    {
        var manifest=new Manifest(1,false,Guid.NewGuid().ToString("N"),files.Select(f=>new Entry(f.Path,Hash(f.Original()),Hash(f.Proposed()))).ToArray());
        ValidateTargets(manifest,root,history);
        var lease=new FileStream(directory+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try
        {
            if(Directory.Exists(directory))throw new IOException("Hay un guardado pendiente de recuperación. Reabrí el proyecto.");
            Directory.CreateDirectory(directory);
            var journal=new RecoveryJournal(directory,lease,manifest);
            journal.Save();
            for(int i=0;i<files.Count;i++)Write(Path.Combine(directory,i+".original"),files[i].Original());
            journal._manifest=manifest with {Ready=true};
            journal.Save();return journal;
        }
        catch{lease.Dispose();throw;}
    }
    internal void Finish(bool complete)
    {
        if(complete)
        {
            _manifest=_manifest with {Complete=true};Save();
            // A cleanup failure after the success marker is not a failed commit.
            try{Clean(_directory,_manifest);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        else Clean(_directory,_manifest);
    }
    internal string TemporaryPath(string path)=>path+".studio-"+_manifest.Token+".tmp";
    private static void Clean(string directory,Manifest manifest)
    {
        foreach(var file in manifest.Files){string temp=file.Path+".studio-"+manifest.Token+".tmp";if(File.Exists(temp))File.Delete(temp);}
        for(int i=0;i<manifest.Files.Length;i++){string path=Path.Combine(directory,i+".original");if(File.Exists(path))File.Delete(path);}
        foreach(string name in new[]{"manifest.tmp","manifest.json"}){string path=Path.Combine(directory,name);if(File.Exists(path))File.Delete(path);}
        if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);
    }
    internal static void Recover(string directory,string root,string history)
    {
        if(!Directory.Exists(directory))return;
        using var lease=new FileStream(directory+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        string path=Path.Combine(directory,"manifest.json");
        if(!File.Exists(path))
        {
            // Begin never replaces a target before its manifest is durable.
            // Do not guess about or delete files in an incomplete preparation.
            throw new IOException("La preparación de recuperación está incompleta: "+directory);
        }
        if(new FileInfo(path).Length>10_000_000)throw new InvalidDataException("Registro de recuperación demasiado grande.");
        var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(path))??throw new InvalidDataException("Registro de recuperación vacío.");
        ValidateTargets(manifest,root,history);
        if(manifest.Complete||!manifest.Ready){Clean(directory,manifest);return;}
        // Validate the entire set before recovering any target or trusting a backup.
        var originals=new byte[manifest.Files.Length][];
        for(int i=0;i<manifest.Files.Length;i++)
        {
            var file=manifest.Files[i];
            string current=Hash(File.ReadAllBytes(file.Path));
            if(current!=file.Before&&current!=file.After)throw new IOException("Un archivo cambió fuera del guardado interrumpido. Se conserva para revisión: "+file.Path);
            if(current==file.Before)continue;
            originals[i]=File.ReadAllBytes(Path.Combine(directory,i+".original"));
            if(Hash(originals[i])!=file.Before)throw new InvalidDataException("El respaldo de recuperación está alterado: "+file.Path);
        }
        for(int i=manifest.Files.Length-1;i>=0;i--)
        {
            var file=manifest.Files[i];string current=Hash(File.ReadAllBytes(file.Path));
            if(current==file.Before)continue;
            if(current!=file.After)throw new IOException("El archivo cambió durante la recuperación: "+file.Path);
            string temporary=file.Path+".studio-"+manifest.Token+".tmp";
            try{Write(temporary,originals[i]);File.Replace(temporary,file.Path,null);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            if(Hash(File.ReadAllBytes(file.Path))!=file.Before)throw new IOException("La recuperación no coincide: "+file.Path);
        }
        Clean(directory,manifest);
    }
    public void Dispose()=>_lease.Dispose();
}
