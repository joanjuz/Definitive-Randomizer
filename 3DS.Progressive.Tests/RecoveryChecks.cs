using System.Diagnostics;
using System.IO;
using System.Reflection;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

internal static class RecoveryChecks
{
    private static FileChange[] Changes(string root)=>[new(Path.Combine(root,"first.bin"),[1,2],[3,4]),new(Path.Combine(root,"second.bin"),[5,6],[7,8])];
    internal static void InterruptChild(string root)
    {
        var files=Changes(root);using var journal=RecoveryJournal.Begin(Path.Combine(root,"test.pending"),root,Path.Combine(root,"history.json"),files);
        FileTransaction.Commit(files,afterReplace:n=>{if(n==1)Environment.Exit(91);},recovery:journal);
        throw new IOException("La prueba no interrumpió el guardado.");
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var fixture=new Fixture(GameVersion.XY);string root=fixture.Root,dir=Path.Combine(root,"test.pending"),history=Path.Combine(root,"history.json");
        var files=Changes(root);foreach(var f in files)File.WriteAllBytes(f.Path,f.Original());
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--interrupt-save-check");start.ArgumentList.Add(root);
        using(var child=Process.Start(start)??throw new IOException("No se pudo iniciar la prueba de interrupción."))
        {if(!child.WaitForExit(30000)){child.Kill();throw new IOException("La prueba de interrupción no terminó.");}check(child.ExitCode==91,"recovery child exits between file replacements without running cleanup");}
        check(File.ReadAllBytes(files[0].Path).SequenceEqual(files[0].Proposed())&&File.ReadAllBytes(files[1].Path).SequenceEqual(files[1].Original())&&Directory.Exists(dir),"interrupted commit leaves a durable original backup and partial targets");
        RecoveryJournal.Recover(dir,root,history);
        check(files.All(f=>File.ReadAllBytes(f.Path).SequenceEqual(f.Original()))&&!Directory.Exists(dir),"recovery restores every file after actual process interruption");
        check(!Directory.EnumerateFiles(root,"*.studio-*.tmp").Any(),"recovery removes abandoned staged files so rebuild cannot package them");
        RecoveryJournal.Recover(dir,root,history);check(!Directory.Exists(dir),"recovery is idempotent after cleanup");
        using(var journal=RecoveryJournal.Begin(dir,root,history,files))
        {
            File.WriteAllBytes(files[0].Path,files[0].Proposed());File.WriteAllBytes(files[1].Path,[9,9]);
        }
        reject(()=>RecoveryJournal.Recover(dir,root,history),"recovery rejects concurrent third-party target bytes before any restore");
        check(File.ReadAllBytes(files[0].Path).SequenceEqual(files[0].Proposed())&&File.ReadAllBytes(files[1].Path).SequenceEqual(new byte[]{9,9}),"recovery never overwrites third-party edits or partially restores a rejected set");
        File.WriteAllBytes(files[1].Path,files[1].Original());File.WriteAllBytes(Path.Combine(dir,"0.original"),[99]);
        reject(()=>RecoveryJournal.Recover(dir,root,history),"recovery rejects corrupted original backup");
        File.WriteAllBytes(Path.Combine(dir,"0.original"),files[0].Original());RecoveryJournal.Recover(dir,root,history);
        using(var journal=RecoveryJournal.Begin(dir,root,history,files))
            FileTransaction.Commit(files,recovery:journal);
        check(files.All(f=>File.ReadAllBytes(f.Path).SequenceEqual(f.Proposed()))&&!Directory.Exists(dir),"successful commit clears recovery state after marking completion");
        foreach(var f in files)File.WriteAllBytes(f.Path,f.Original());
        using(var journal=RecoveryJournal.Begin(dir,root,history,files))
            reject(()=>FileTransaction.Commit(files,afterReplace:_=>throw new IOException("Injected failure"),recovery:journal),"regular write failure rolls back through the same journal");
        check(files.All(f=>File.ReadAllBytes(f.Path).SequenceEqual(f.Original()))&&!Directory.Exists(dir),"ordinary rollback preserves bytes and removes completed recovery work");
        reject(()=>RecoveryJournal.Begin(dir,Path.Combine(root,"other-root"),history,files),"journal rejects targets outside the selected project");
    }
}
