using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

public sealed record RestorePlan(string Backup,string Sha256,string Game,int Files,long Bytes);
public static partial class ProjectRestore
{
    private const string History=".definitive-restored-history.json";
    private static string Safe(string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||relative.Contains('\\')||relative.StartsWith('/')||relative.Split('/').Any(part=>
            part.Length==0||part is "." or ".."||part.EndsWith('.')||part.EndsWith(' ')||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0||
            System.Text.RegularExpressions.Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Ruta no válida en el respaldo: "+relative);
        return relative;
    }
    private static BackupManifest Verify(ZipArchive zip,CancellationToken cancellation)
    {
        var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            if(!entries.TryAdd(entry.FullName,entry)||((entry.ExternalAttributes>>16)&0xF000)==0xA000||(entry.ExternalAttributes&0x400)!=0)
                throw new InvalidDataException("Entrada duplicada o enlace en el respaldo.");
        }
        if(!entries.TryGetValue("backup-manifest.json",out var metadata)||metadata.Length>16*1024*1024)throw new InvalidDataException("Falta un manifiesto compatible.");
        BackupManifest manifest;
        try{using var stream=metadata.Open();manifest=JsonSerializer.Deserialize<BackupManifest>(stream)??throw new InvalidDataException("Manifiesto vacío.");}
        catch(JsonException ex){throw new InvalidDataException("Manifiesto inválido.",ex);}
        if(manifest.Version!=1||manifest.Files is null||manifest.Files.Count==0||manifest.Files.Count>1000000)throw new InvalidDataException("Formato de respaldo incompatible.");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var file in manifest.Files)
        {
            cancellation.ThrowIfCancellationRequested();
            if(file is null||file.Size<0||file.Sha256 is null||file.Sha256.Length!=64||!file.Sha256.All(Uri.IsHexDigit)||!paths.Add(Safe(file.Path)))throw new InvalidDataException("Inventario inválido o repetido.");
            total=checked(total+file.Size);
            if(!entries.TryGetValue("project/"+file.Path,out var entry)||entry.FullName!="project/"+file.Path||entry.Length!=file.Size)throw new InvalidDataException("Archivo ausente o tamaño incorrecto: "+file.Path);
            using var stream=entry.Open();using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] buffer=new byte[1024*1024];long size=0;int read;
            while((read=stream.Read(buffer,0,buffer.Length))>0){cancellation.ThrowIfCancellationRequested();hash.AppendData(buffer,0,read);size+=read;if(size>file.Size)throw new InvalidDataException("Archivo supera su tamaño declarado.");}
            if(size!=file.Size||!Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SHA-256 incorrecto: "+file.Path);
        }
        foreach(string path in paths){int slash=path.IndexOf('/');while(slash>=0){if(paths.Contains(path[..slash]))throw new InvalidDataException("Colisión archivo/carpeta en el respaldo.");slash=path.IndexOf('/',slash+1);}}
        foreach(string name in entries.Keys)if(name!="backup-manifest.json"&&name!="progressive-history.json"&&(!name.StartsWith("project/",StringComparison.Ordinal)||!paths.Contains(name[8..])))throw new InvalidDataException("Entrada fuera del inventario: "+name);
        if(entries.ContainsKey("progressive-history.json")&&paths.Any(p=>p.Equals(History,StringComparison.OrdinalIgnoreCase)||p.StartsWith(History+"/",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("El archivo de historial recuperado entra en conflicto.");
        if(entries.TryGetValue("progressive-history.json",out var history)&&history.Length>64*1024*1024)throw new InvalidDataException("Historial exportado demasiado grande.");
        return manifest;
    }
    public static Task<RestorePlan> InspectAsync(string backup,CancellationToken cancellation=default)=>Task.Run(()=>
    {
        string path=Path.GetFullPath(backup);using var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        string hash=Convert.ToHexString(SHA256.HashData(source));source.Position=0;using var zip=new ZipArchive(source,ZipArchiveMode.Read,true);var manifest=Verify(zip,cancellation);
        return new RestorePlan(path,hash,manifest.Game,manifest.Files.Count,manifest.Files.Sum(f=>f.Size));
    },cancellation);
    public static Task<string> RestoreAsync(RestorePlan plan,string destination,CancellationToken cancellation=default)=>Task.Run(()=>
    {
        string target=Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));string parent=Path.GetDirectoryName(target)??throw new ArgumentException("Destino sin carpeta padre.");
        if(Directory.Exists(target)||File.Exists(target))throw new IOException("La carpeta destino debe ser nueva.");
        if(!Directory.Exists(parent))throw new DirectoryNotFoundException(parent);
        for(var ancestor=new DirectoryInfo(parent);ancestor is not null;ancestor=ancestor.Parent)if((ancestor.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("El destino no admite enlaces a carpetas.");
        using var source=new FileStream(plan.Backup,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(Convert.ToHexString(SHA256.HashData(source))!=plan.Sha256)throw new IOException("El respaldo cambió después de verificarlo.");
        source.Position=0;using var zip=new ZipArchive(source,ZipArchiveMode.Read,true);var manifest=Verify(zip,cancellation);
        string stage=Path.Combine(parent,".definitive-restore-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try
        {
            int done=0;
            foreach(var file in manifest.Files)
            {
                cancellation.ThrowIfCancellationRequested();string path=Path.Combine(stage,file.Path.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using(var input=zip.GetEntry("project/"+file.Path)!.Open())using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {byte[] buffer=new byte[1024*1024];int read;while((read=input.Read(buffer,0,buffer.Length))>0){cancellation.ThrowIfCancellationRequested();output.Write(buffer,0,read);}output.Flush(true);}
                using(var restored=File.OpenRead(path))if(Convert.ToHexString(SHA256.HashData(restored))!=file.Sha256.ToUpperInvariant())throw new IOException("La copia restaurada no coincide.");
                UiProgress.Stage($"Restaurando respaldo · {++done:N0}/{manifest.Files.Count:N0}");
            }
            if(zip.GetEntry("progressive-history.json") is {} journal)using(var input=journal.Open())using(var output=new FileStream(Path.Combine(stage,History),FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
            cancellation.ThrowIfCancellationRequested();Directory.Move(stage,target);return target;
        }
        finally{if(Directory.Exists(stage)&&string.Equals(Path.GetDirectoryName(Path.GetFullPath(stage)),parent,StringComparison.OrdinalIgnoreCase))Directory.Delete(stage,true);}
    },cancellation);
}
