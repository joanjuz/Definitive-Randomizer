using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed class SelectedRestorePreview
{
    internal GameProjectSession Project {get;}
    internal RestorePlan Plan {get;}
    internal IReadOnlyList<FileChange> Files {get;}
    internal IReadOnlyList<BatchChange> Changes {get;}
    internal SelectedRestorePreview(GameProjectSession project,RestorePlan plan,IReadOnlyList<FileChange> files,IReadOnlyList<BatchChange> changes)
    {Project=project;Plan=plan;Files=files;Changes=changes;}
}
public static partial class ProjectRestore
{
    private static string? GameTarget(GameProjectSession project,string relative)
    {
        Safe(relative);string path=Path.GetFullPath(Path.Combine(project.ProjectRoot,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!new[]{project.GameConfiguration.RomFS,project.GameConfiguration.ExeFS}.Any(folder=>path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))return null;
        if(!File.Exists(path))return null;
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("No se restauran archivos enlazados.");
        for(var directory=new DirectoryInfo(Path.GetDirectoryName(path)!);directory is not null;directory=directory.Parent)
            if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("No se restauran rutas enlazadas.");
        return path;
    }
    private static FileStream OpenSelectedBackup(RestorePlan plan)
    {
        var source=new FileStream(plan.Backup,FileMode.Open,FileAccess.Read,FileShare.Read);
        try{if(Convert.ToHexString(SHA256.HashData(source))!=plan.Sha256)throw new IOException("El respaldo cambió desde la inspección.");source.Position=0;return source;}
        catch{source.Dispose();throw;}
    }
    internal static Task<IReadOnlyList<BackupFile>> SelectableAsync(GameProjectSession project,RestorePlan plan,CancellationToken cancellation=default)=>Task.Run<IReadOnlyList<BackupFile>>(()=>
    {
        using var source=OpenSelectedBackup(plan);using var zip=new ZipArchive(source,ZipArchiveMode.Read,true);var manifest=Verify(zip,cancellation);
        if(!string.Equals(manifest.Game,project.GameName,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("El respaldo pertenece a otra familia de juegos.");
        return manifest.Files.Where(file=>file.Size<=128*1024*1024&&GameTarget(project,file.Path) is string target&&new FileInfo(target).Length<=128*1024*1024).ToArray();
    },cancellation);
    internal static Task<SelectedRestorePreview> PreviewSelectedAsync(GameProjectSession project,RestorePlan plan,IReadOnlyList<string> selected,CancellationToken cancellation=default)=>Task.Run(()=>
    {
        if(selected.Count==0||selected.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=selected.Count)throw new ArgumentException("Seleccioná archivos distintos del respaldo.");
        using var source=OpenSelectedBackup(plan);using var zip=new ZipArchive(source,ZipArchiveMode.Read,true);var manifest=Verify(zip,cancellation);
        if(!string.Equals(manifest.Game,project.GameName,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("El respaldo pertenece a otra familia de juegos.");
        var rows=selected.Select(path=>manifest.Files.SingleOrDefault(file=>file.Path.Equals(path,StringComparison.OrdinalIgnoreCase))??throw new InvalidDataException("El archivo no está en el inventario del respaldo: "+path)).ToArray();
        if(rows.Sum(row=>row.Size)>128*1024*1024)throw new InvalidDataException("La vista previa selectiva admite hasta 128 MiB por operación. Para recuperar todo, usá la restauración completa en una carpeta nueva.");
        if(rows.Sum(row=>new FileInfo(GameTarget(project,row.Path)??throw new InvalidDataException("Archivo fuera de RomFS/ExeFS.")).Length)>128*1024*1024)throw new InvalidDataException("Los archivos actuales superan 128 MiB por operación.");
        var files=new List<FileChange>();var changes=new List<BatchChange>();
        foreach(var row in rows)
        {
            cancellation.ThrowIfCancellationRequested();string path=GameTarget(project,row.Path)??throw new InvalidDataException("Solo se restauran archivos existentes de RomFS/ExeFS: "+row.Path);
            byte[] before=File.ReadAllBytes(path);using var input=zip.GetEntry("project/"+row.Path)!.Open();using var output=new MemoryStream();input.CopyTo(output);byte[] after=output.ToArray();
            if(Convert.ToHexString(SHA256.HashData(after))!=row.Sha256.ToUpperInvariant())throw new IOException("Hash de respaldo distinto.");
            var file=new FileChange(path,before,after);if(!file.Changed)continue;files.Add(file);
            changes.Add(new(changes.Count,row.Path,"SHA-256",Convert.ToHexString(file.Fingerprint),row.Sha256));
        }
        return new SelectedRestorePreview(project,plan,files.AsReadOnly(),changes.AsReadOnly());
    },cancellation);
    internal static void ApplySelected(GameProjectSession project,SelectedRestorePreview preview)
    {
        if(!ReferenceEquals(project,preview.Project)||preview.Files.Count==0)throw new InvalidOperationException("La propuesta no pertenece al proyecto o no tiene cambios.");
        using var backup=OpenSelectedBackup(preview.Plan);
        foreach(var file in preview.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        project.History.Commit(preview.Files,"Restauración","Restaurar archivos seleccionados",preview.Changes,
            new Dictionary<string,string>{{"Scope",string.Join(",",preview.Changes.Select(change=>change.Name))},{"BackupSHA256",preview.Plan.Sha256}});
    }
}
