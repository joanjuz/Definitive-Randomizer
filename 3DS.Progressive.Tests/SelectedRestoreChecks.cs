using System.IO;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

internal static class SelectedRestoreChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var f=new Fixture(GameVersion.XY);
        string first=Path.Combine(f.Game.RomFS,"restore-a.bin"),second=Path.Combine(f.Game.ExeFS,"restore-b.bin"),personal=Path.Combine(f.Root,"personal-note.txt");
        File.WriteAllBytes(first,[1,2,3]);File.WriteAllBytes(second,[4,5,6]);File.WriteAllText(personal,"personal");
        string backupRoot=Path.Combine(Path.GetTempPath(),"selected-restore-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            string backup=Task.Run(()=>ProjectBackup.CreateAsync(f.Root,backupRoot,f.Session.GameName)).GetAwaiter().GetResult();
            var plan=Task.Run(()=>ProjectRestore.InspectAsync(backup)).GetAwaiter().GetResult();
            var available=Task.Run(()=>ProjectRestore.SelectableAsync(f.Session,plan)).GetAwaiter().GetResult();
            check(available.Any(file=>file.Path=="RomFS/restore-a.bin")&&available.Any(file=>file.Path=="ExeFS/restore-b.bin")&&!available.Any(file=>file.Path=="personal-note.txt"),"Selected restore lists only game files and excludes personal files");
            File.WriteAllBytes(first,[8,8,8]);File.WriteAllBytes(second,[9,9,9]);File.WriteAllText(personal,"my new note");
            var preview=Task.Run(()=>ProjectRestore.PreviewSelectedAsync(f.Session,plan,["RomFS/restore-a.bin"])).GetAwaiter().GetResult();
            check(preview.Files.Count==1&&File.ReadAllBytes(first).SequenceEqual(new byte[]{8,8,8}),"Selected restore preview is read-only");
            ProjectRestore.ApplySelected(f.Session,preview);
            check(File.ReadAllBytes(first).SequenceEqual(new byte[]{1,2,3})&&File.ReadAllBytes(second).SequenceEqual(new byte[]{9,9,9})&&File.ReadAllText(personal)=="my new note","Selected restore recovers only selected file and preserves unrelated changes");
            reject(()=>ProjectRestore.ApplySelected(f.Session,preview),"Selected restore rejects stale proposal");
            var unchanged=Task.Run(()=>ProjectRestore.PreviewSelectedAsync(f.Session,plan,["RomFS/restore-a.bin"])).GetAwaiter().GetResult();
            check(unchanged.Files.Count==0,"Selected restore excludes identical files");
            reject(()=>ProjectRestore.ApplySelected(f.Session,unchanged),"Selected restore rejects empty apply");
            File.WriteAllBytes(first,[8,8,8]);
            var both=Task.Run(()=>ProjectRestore.PreviewSelectedAsync(f.Session,plan,["RomFS/restore-a.bin","ExeFS/restore-b.bin"])).GetAwaiter().GetResult();
            File.WriteAllBytes(second,[7,7,7]);
            reject(()=>ProjectRestore.ApplySelected(f.Session,both),"Selected restore verifies all fingerprints before any write");
            check(File.ReadAllBytes(first).SequenceEqual(new byte[]{8,8,8})&&File.ReadAllBytes(second).SequenceEqual(new byte[]{7,7,7}),"Selected restore conflict preserves all current files");
            reject(()=>Task.Run(()=>ProjectRestore.PreviewSelectedAsync(f.Session,plan,["personal-note.txt"])).GetAwaiter().GetResult(),"Selected restore refuses personal file selection");
            reject(()=>Task.Run(()=>ProjectRestore.PreviewSelectedAsync(f.Session,plan,["RomFS/restore-a.bin","RomFS/restore-a.bin"])).GetAwaiter().GetResult(),"Selected restore rejects duplicate selections");
            using var other=new Fixture(GameVersion.ORAS);
            reject(()=>Task.Run(()=>ProjectRestore.SelectableAsync(other.Session,plan)).GetAwaiter().GetResult(),"Selected restore rejects backup from another game family");
            reject(()=>ProjectRestore.ApplySelected(other.Session,both),"Selected restore rejects proposal from another session");
            File.AppendAllText(backup,"changed");
            reject(()=>ProjectRestore.ApplySelected(f.Session,both),"Selected restore rejects backup changed after preview");
        }
        finally{if(Directory.Exists(backupRoot))Directory.Delete(backupRoot,true);}
    }
}
