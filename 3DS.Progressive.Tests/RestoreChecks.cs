using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ThreeDS.Progressive.Services;

internal static class RestoreChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        string root=Path.Combine(Path.GetTempPath(),"restore-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string project=Path.Combine(root,"source");Directory.CreateDirectory(Path.Combine(project,"RomFS"));File.WriteAllBytes(Path.Combine(project,"RomFS","file.bin"),[1,2,3,4]);
            string backup=Task.Run(()=>ProjectBackup.CreateAsync(project,Path.Combine(root,"backups"),"XY")).GetAwaiter().GetResult();
            var plan=ProjectRestore.InspectAsync(backup).GetAwaiter().GetResult();string target=Path.Combine(root,"restored");
            ProjectRestore.RestoreAsync(plan,target).GetAwaiter().GetResult();
            check(File.ReadAllBytes(Path.Combine(target,"RomFS","file.bin")).SequenceEqual(new byte[]{1,2,3,4}),"Restore reproduces manifest-verified files");
            reject(()=>ProjectRestore.RestoreAsync(plan,target).GetAwaiter().GetResult(),"Restore refuses existing destination");
            using(var zip=ZipFile.Open(backup,ZipArchiveMode.Update)){using var writer=new StreamWriter(zip.CreateEntry("project/../escape.bin").Open());writer.Write("bad");}
            reject(()=>ProjectRestore.RestoreAsync(plan,Path.Combine(root,"changed")).GetAwaiter().GetResult(),"Restore rejects source changed after preview");
            reject(()=>ProjectRestore.InspectAsync(backup).GetAwaiter().GetResult(),"Restore rejects unexpected traversal entry");
            check(!Directory.Exists(Path.Combine(root,"changed"))&&!Directory.GetDirectories(root,".definitive-restore-*").Any(),"Rejected restore publishes no target or staging");
            string corrupted=Path.Combine(root,"corrupt.zip");
            using(var zip=ZipFile.Open(corrupted,ZipArchiveMode.Create))
            {
                using(var stream=zip.CreateEntry("backup-manifest.json").Open())JsonSerializer.Serialize(stream,new BackupManifest(1,"XY",DateTimeOffset.UtcNow,[new("file.bin",4,new string('0',64))]));
                using(var stream=zip.CreateEntry("project/file.bin").Open())stream.Write(new byte[]{1,2,3,4});
            }
            reject(()=>ProjectRestore.InspectAsync(corrupted).GetAwaiter().GetResult(),"Restore rejects corrupt file hash before writing");
            foreach(string unsafePath in new[]{"../escape","/absolute","a\\b","a/../b","CON.txt","file:stream","trailing. "})
            {
                string bad=Path.Combine(root,Guid.NewGuid()+".zip");
                using(var zip=ZipFile.Open(bad,ZipArchiveMode.Create))
                {
                    byte[] bytes=[1,2,3,4];string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                    using(var stream=zip.CreateEntry("backup-manifest.json").Open())JsonSerializer.Serialize(stream,new BackupManifest(1,"XY",DateTimeOffset.UtcNow,[new(unsafePath,4,hash)]));
                    using(var stream=zip.CreateEntry("project/"+unsafePath).Open())stream.Write(bytes);
                }
                reject(()=>ProjectRestore.InspectAsync(bad).GetAwaiter().GetResult(),"Restore rejects unsafe manifest path "+unsafePath);
            }
        }
        finally{Directory.Delete(root,true);}
    }
}
