using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

public sealed record BackupProgress(int Completed,int Total,string File);
public sealed record BackupFile(string Path,long Size,string Sha256);
public sealed record BackupManifest(int Version,string Game,DateTimeOffset Created,IReadOnlyList<BackupFile> Files);
public static class ProjectBackup
{
    public static async Task<string> CreateAsync(string projectRoot,string folder,string game,
        IProgress<BackupProgress>? progress=null,CancellationToken cancellation=default,HistoryDocument? history=null)
    {
        string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        string destination=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        StringComparison comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        if(destination.Equals(root,comparison)||destination.StartsWith(root+Path.DirectorySeparatorChar,comparison))
            throw new ArgumentException("Elegí una carpeta fuera del proyecto para guardar el backup.");
        if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
        Directory.CreateDirectory(destination);
        return await Task.Run(()=>
        {
            var files=new List<string>();
            void Collect(string dir)
            {
                cancellation.ThrowIfCancellationRequested();
                if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("El backup no admite enlaces a otras carpetas: "+dir);
                foreach(string file in Directory.EnumerateFiles(dir))
                {
                    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                        throw new IOException("El backup no admite enlaces a otros archivos: "+file);
                    files.Add(file);
                }
                foreach(string child in Directory.EnumerateDirectories(dir))Collect(child);
            }
            Collect(root);files.Sort(StringComparer.Ordinal);
            string name=Path.GetFileName(root)+"-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+".zip";
            string path=Path.Combine(destination,name),partial=path+".partial";
            var manifest=new List<BackupFile>();
            try
            {
                using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                using(var zip=new ZipArchive(output,ZipArchiveMode.Create))
                {
                    byte[] buffer=new byte[1024*1024];int done=0;
                    foreach(string file in files)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string relative=Path.GetRelativePath(root,file).Replace('\\','/');
                        var info=new FileInfo(file);long originalSize=info.Length;DateTime originalTime=info.LastWriteTimeUtc;
                        var entry=zip.CreateEntry("project/"+relative,CompressionLevel.Fastest);
                        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        long size=0;
                        using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
                        using(var target=entry.Open())
                        {
                            int read;while((read=input.Read(buffer,0,buffer.Length))>0)
                            { cancellation.ThrowIfCancellationRequested();target.Write(buffer,0,read);hash.AppendData(buffer,0,read);size+=read; }
                        }
                        string digest=Convert.ToHexString(hash.GetHashAndReset());
                        info.Refresh();
                        using(var verify=File.OpenRead(file))
                            if(size!=originalSize||info.Length!=originalSize||info.LastWriteTimeUtc!=originalTime||
                                digest!=Convert.ToHexString(SHA256.HashData(verify)))
                                throw new IOException("El proyecto cambió durante el backup: "+relative);
                        manifest.Add(new(relative,size,digest));progress?.Report(new(++done,files.Count,relative));
                    }
                    using(var metadata=zip.CreateEntry("backup-manifest.json").Open())
                        JsonSerializer.Serialize(metadata,new BackupManifest(1,game,DateTimeOffset.UtcNow,manifest),new JsonSerializerOptions {WriteIndented=true});
                    if(history is not null)
                        using(var journal=zip.CreateEntry("progressive-history.json").Open())JsonSerializer.Serialize(journal,history);
                }
                using(var zip=ZipFile.OpenRead(partial))
                    foreach(var file in manifest)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var entry=zip.GetEntry("project/"+file.Path)??throw new IOException("Falta un archivo en el backup.");
                        using var stream=entry.Open();
                        if(entry.Length!=file.Size||Convert.ToHexString(SHA256.HashData(stream))!=file.Sha256)
                            throw new IOException("El backup no superó la verificación: "+file.Path);
                        using var current=File.OpenRead(Path.Combine(root,file.Path.Replace('/',Path.DirectorySeparatorChar)));
                        if(Convert.ToHexString(SHA256.HashData(current))!=file.Sha256)
                            throw new IOException("El proyecto cambió durante el backup: "+file.Path);
                    }
                File.Move(partial,path);return path;
            }
            finally {if(File.Exists(partial))File.Delete(partial);}
        },cancellation);
    }
}
