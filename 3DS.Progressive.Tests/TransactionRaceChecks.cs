using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ThreeDS.Progressive.Services;

/// <summary>Isolated failing security invariants, not assertions that bless today's data loss.</summary>
internal static class TransactionRaceChecks
{
    private static readonly byte[] External = [0xE1, 0xE2, 0xE3, 0xE4];
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(20);
    private static FileChange[] Changes(string root) =>
        [new(Path.Combine(root,"first.bin"),[1,2,3,4],[11,12,13,14]),
         new(Path.Combine(root,"second.bin"),[5,6,7,8],[15,16,17,18]),
         new(Path.Combine(root,"third.bin"),[21,22,23,24],[31,32,33,34])];
    private static string Journal(string root) => Path.Combine(root,"test.pending");
    private static string History(string root) => Path.Combine(root,"history.json");
    private static byte[] ReadShared(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var memory=new MemoryStream();stream.CopyTo(memory);return memory.ToArray();
    }
    private static void WriteExternal(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);
        stream.Write(External);stream.SetLength(External.Length);stream.Flush(true);
        Console.WriteLine($"TRACE external write flushed: {Path.GetFileName(path)} bytes={Bytes(External)}");
    }
    private static bool Is(FileChange file, byte[] bytes) => ReadShared(file.Path).SequenceEqual(bytes);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string message)
    { if(!value)throw new IOException("Harness/control failure: "+message); }
    private static Exception? Capture(Action action)
    { try { action(); return null; } catch(Exception ex) { return ex; } }
    private static string Bytes(byte[] bytes) => $"{Convert.ToHexString(bytes)} sha256={Convert.ToHexString(SHA256.HashData(bytes))}";
    private static void Observe(string name,FileChange file,Exception? failure,string root)
    {
        Console.WriteLine($"TRACE {name}: before={Bytes(file.Original())}; proposed={Bytes(file.Proposed())}; external={Bytes(External)}; observed={Bytes(File.ReadAllBytes(file.Path))}; exception={failure?.GetType().Name??"none"}; journal={Directory.Exists(Journal(root))}");
        if(failure is not null)Console.WriteLine($"TRACE {name} exception: {failure.Message}");
        Console.WriteLine($"TRACE {name} residual={string.Join(",",Directory.GetFiles(root,"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(root,p)).Order())}");
        foreach(var target in Changes(root))
            Console.WriteLine($"TRACE {name} target={Path.GetFileName(target.Path)} observed={Bytes(ReadShared(target.Path))}");
        string manifest=Path.Combine(Journal(root),"manifest.json");
        Console.WriteLine($"TRACE {name} manifest={(File.Exists(manifest)?Bytes(File.ReadAllBytes(manifest)):"absent")}; historyExists={File.Exists(History(root))}");
    }
    private static string Create(string parent,string name)
    {
        string root=Path.Combine(parent,name);Directory.CreateDirectory(root);
        foreach(var file in Changes(root))File.WriteAllBytes(file.Path,file.Original());
        return root;
    }

    // Invoked only on a parent-created synthetic root, with an explicit ownership marker.
    internal static void InterruptChild(string root)
    {
        root=Path.GetFullPath(root);
        Require(root.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            &&Path.GetFileName(Path.GetDirectoryName(root)!).StartsWith("transaction-races-",StringComparison.Ordinal)
            &&File.ReadAllText(Path.Combine(root,"synthetic.owner"))=="H2H3 synthetic only", "child ownership marker");
        var files=Changes(root);
        Require(files.All(f=>Is(f,f.Original())),"child accepts only the expected synthetic bytes");
        using var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files);
        FileTransaction.Commit(files,afterReplace:n=>{if(n==2)Environment.Exit(91);},recovery:journal);
        throw new IOException("Child did not interrupt the transaction.");
    }
    private static FileChange[] Interrupted(string root)
    {
        File.WriteAllText(Path.Combine(root,"synthetic.owner"),"H2H3 synthetic only");
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--interrupt-transaction-races");start.ArgumentList.Add(root);
        using var child=Process.Start(start)??throw new IOException("Cannot start synthetic child.");
        if(!child.WaitForExit((int)Watchdog.TotalMilliseconds))
        {child.Kill(entireProcessTree:true);child.WaitForExit();throw new TimeoutException("Interrupted child watchdog.");}
        var files=Changes(root);
        Require(child.ExitCode==91&&Is(files[0],files[0].Proposed())&&Is(files[1],files[1].Proposed())
            &&Is(files[2],files[2].Original())&&File.Exists(Path.Combine(Journal(root),"manifest.json")),"durable real interrupted commit");
        Console.WriteLine("TRACE child exit=91; first=proposed; second=proposed; third=before; durable journal present");
        Console.WriteLine($"TRACE interrupted manifest={Bytes(File.ReadAllBytes(Path.Combine(Journal(root),"manifest.json")))}");
        return files;
    }
    private static string Temporary(string root,FileChange file)
    {
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Journal(root),"manifest.json")));
        return file.Path+".studio-"+doc.RootElement.GetProperty("Token").GetString()+".tmp";
    }
    private static void Paused(Task worker,TransactionOpenBarrier barrier,Action edit)
    {
        try
        {
            barrier.WaitForOpen();
            Require(!worker.IsCompleted,"worker must remain blocked at temporary open");
            Console.WriteLine("TRACE oplock break acknowledged by event; worker blocked after target comparison, before temporary open completes");
            edit();
        }
        finally
        {
            try { barrier.Release(); }
            catch(Exception ex) { AbortHarness("oplock release: "+ex.Message); }
            if(!worker.Wait(Watchdog))
            {
                // Do not delete temporary files while a worker can still touch them.
                AbortHarness("worker stuck after release");
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void AbortHarness(string reason)
    {
        Console.Error.WriteLine("HARNESS WATCHDOG: "+reason+"; retaining synthetic evidence; not a security result.");
        Environment.Exit(3);throw new InvalidOperationException("Unreachable watchdog exit.");
    }

    internal static void Run(Action<bool,string> check)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows/NTFS oplocks required.");
        string parent=Path.Combine(Path.GetTempPath(),"transaction-races-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        Require(new DriveInfo(Path.GetPathRoot(parent)!).DriveFormat=="NTFS","local NTFS required");
        var results=new List<(bool Pass,string Name)>();
        void Result(bool pass,string name)
        {results.Add((pass,name));Console.WriteLine($"{(pass?"VERIFIED":"INVARIANT FAILED")}: {name}");}
        try
        {
            Controls(parent,Result);
            CommitRace(Create(parent,"calibrate-commit"),Result,edit:false);
            RollbackRace(Create(parent,"calibrate-rollback"),Result,edit:false);
            RecoveryRace(Create(parent,"calibrate-recovery"),false,Result,edit:false);
            CommitRace(Create(parent,"commit"),Result);
            RollbackRace(Create(parent,"rollback"),Result);
            RecoveryRace(Create(parent,"recover-single"),false,Result);
            RecoveryRace(Create(parent,"recover-multiple"),true,Result);
            RecoveryPartial(Create(parent,"recover-partial"),Result);
        }
        finally {Directory.Delete(parent,true);}
        Console.WriteLine($"Transaction race results: Passed: {results.Count(r=>r.Pass)}, Failed: {results.Count(r=>!r.Pass)}, Skipped: 0");
        // Report every case first, because the repository Check callback throws on its first failure.
        foreach(var result in results.Where(r=>r.Pass))check(true,result.Name);
        check(results.All(r=>r.Pass),"H2H3_AllExternalEditsPreserved (see individual invariant failures above)");
    }

    private static void CommitRace(string root,Action<bool,string> result,bool edit=true)
    {
        var files=Changes(root);var victim=files[1];Exception? failure=null;
        using var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files);
        TransactionOpenBarrier? barrier=null;
        using var armed=new ManualResetEventSlim();
        var worker=Task.Run(()=>{try
        {
            FileTransaction.Commit(files,afterReplace:n=>
            {
                if(n!=1)return;
                // All sources are staged. The next target hash precedes File.Replace's source open.
                barrier=new TransactionOpenBarrier(journal.TemporaryPath(victim.Path));armed.Set();
            },recovery:journal);
        }catch(Exception ex){failure=ex;}finally{armed.Set();}});
        if(!armed.Wait(Watchdog))AbortHarness("commit arm timeout");
        Require(barrier is not null,"commit barrier armed: "+failure);
        using(barrier)
            Paused(worker,barrier,()=>
            {
                Require(Is(victim,victim.Original()),"victim unchanged before external write");
                if(!edit)return;
                WriteExternal(victim.Path);
                Require(Is(victim,External),"external write completed before releasing source open");
                Console.WriteLine("TRACE commit external write complete; releasing File.Replace source");
            });
        Observe(edit?"H2 commit":"calibration commit",victim,failure,root);
        if(!edit)
        {
            Require(failure is null&&files.All(f=>Is(f,f.Proposed()))&&!Directory.Exists(Journal(root)),"commit barrier must permit normal success");
            result(true,"Harness_CommitBarrierAllowsSuccessWithoutWriter");return;
        }
        result(Is(victim,External),"H2_CommitPreservesExternalEditAfterFinalHash");
        result(failure is not null||Is(victim,External),"H2_CommitDoesNotSilentlySucceedAfterExternalLoss");
    }

    private static void RollbackRace(string root,Action<bool,string> result,bool edit=true)
    {
        var files=Changes(root);var victim=files[0];Exception? failure=null;
        using var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files);
        TransactionOpenBarrier? barrier=null;using var armed=new ManualResetEventSlim();
        var worker=Task.Run(()=>{try
        {
            FileTransaction.Commit(files,afterReplace:n=>
            {
                Require(n==1,"rollback injection after first replacement");
                string temp=journal.TemporaryPath(victim.Path);
                File.WriteAllBytes(temp,[0xAA]);
                barrier=new TransactionOpenBarrier(temp);armed.Set();
                throw new IOException("Synthetic rollback trigger");
            },recovery:journal);
        }catch(Exception ex){failure=ex;}finally{armed.Set();}});
        if(!armed.Wait(Watchdog))AbortHarness("rollback arm timeout");
        Require(barrier is not null,"rollback barrier armed: "+failure);
        using(barrier)Paused(worker,barrier,()=>
        {
            Require(Is(victim,victim.Proposed()),"rollback comparison accepted proposed bytes");
            if(!edit)return;
            WriteExternal(victim.Path);Require(Is(victim,External),"rollback external write completed");
        });
        Observe(edit?"H2 rollback":"calibration rollback",victim,failure,root);
        if(!edit)
        {
            Require(failure is IOException&&failure.Message=="Synthetic rollback trigger"
                &&files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root)),"rollback barrier must allow normal rollback");
            result(true,"Harness_RollbackBarrierAllowsRestoreWithoutWriter");return;
        }
        result(failure is IOException,"H2_RollbackReportsOriginalFailure");
        result(Is(victim,External),"H2_RollbackPreservesExternalEditAfterComparison");
        result(Is(files[1],files[1].Original())&&Is(files[2],files[2].Original()),"H2_RollbackLeavesUnreplacedTargetsUntouched");
        result(Is(victim,External)||Directory.Exists(Journal(root)),"H2_RollbackRetainsEvidenceIfExternalEditLost");
    }

    private static void RecoveryRace(string root,bool multiple,Action<bool,string> result,bool edit=true)
    {
        var files=Interrupted(root);var victim=files[multiple?0:1];
        string temp=Temporary(root,victim);File.WriteAllBytes(temp,[0xAA]);
        Exception? failure=null;
        using(var barrier=new TransactionOpenBarrier(temp))
        {
            var worker=Task.Run(()=>failure=Capture(()=>RecoveryJournal.Recover(Journal(root),root,History(root))));
            Paused(worker,barrier,()=>
            {
                Require(Is(victim,victim.Proposed()),"recovery last comparison accepted proposed bytes");
                if(multiple)Require(Is(files[1],files[1].Original()),"reverse first restoration already completed");
                if(!edit)return;
                WriteExternal(victim.Path);Require(Is(victim,External),"recovery external write completed");
            });
        }
        string label=multiple?"H3_Multiple":"H3_Single";
        Observe(edit?label:"calibration recovery",victim,failure,root);
        if(!edit)
        {
            Require(failure is null&&files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root)),"recovery barrier must allow normal restore");
            result(true,"Harness_RecoveryBarrierAllowsRestoreWithoutWriter");return;
        }
        result(Is(victim,External),label+"PreservesExternalEditAfterFinalHash");
        result(Is(victim,External)||Directory.Exists(Journal(root)),label+"RetainsJournalIfExternalEditLost");
        result(Is(files[2],files[2].Original()),label+"LeavesNeverCommittedTargetUntouched");
    }

    private static void RecoveryPartial(string root,Action<bool,string> result)
    {
        var files=Interrupted(root);string temp=Temporary(root,files[1]);File.WriteAllBytes(temp,[0xAA]);
        Exception? failure=null;
        using(var barrier=new TransactionOpenBarrier(temp))
        {
            var worker=Task.Run(()=>failure=Capture(()=>RecoveryJournal.Recover(Journal(root),root,History(root))));
            Paused(worker,barrier,()=>{WriteExternal(files[0].Path);Require(Is(files[0],External),"later target edited");});
        }
        Observe("H3 partial later target",files[0],failure,root);
        result(failure is IOException&&Is(files[0],External)&&Is(files[1],files[1].Original())
            &&Is(files[2],files[2].Original())&&Directory.Exists(Journal(root)),"H3_PartialRecoveryRejectsLaterConflictAndRetainsJournal");
        byte[] manifest=File.ReadAllBytes(Path.Combine(Journal(root),"manifest.json"));
        var retry=Capture(()=>RecoveryJournal.Recover(Journal(root),root,History(root)));
        result(retry is IOException&&Is(files[0],External)&&Is(files[1],files[1].Original())
            &&File.ReadAllBytes(Path.Combine(Journal(root),"manifest.json")).SequenceEqual(manifest),"H3_PartialRetryPreservesConflictAndManifest");
        // Explicit reconciliation of synthetic bytes only; verify retained backups really permit retry.
        File.WriteAllBytes(files[0].Path,files[0].Proposed());
        RecoveryJournal.Recover(Journal(root),root,History(root));
        result(files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root)),"H3_ReconciledPartialRecoveryCanFinish");
    }

    private static void Controls(string parent,Action<bool,string> result)
    {
        string root=Create(parent,"controls");var files=Changes(root);
        using(var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files))FileTransaction.Commit(files,recovery:journal);
        result(files.All(f=>Is(f,f.Proposed()))&&!Directory.Exists(Journal(root)),"H2_ControlNormalCommit");
        foreach(var f in files)File.WriteAllBytes(f.Path,f.Original());
        WriteExternal(files[1].Path);
        var failure=Capture(()=>FileTransaction.Commit(files));
        result(failure is IOException&&Is(files[1],External)&&Is(files[0],files[0].Original())&&Is(files[2],files[2].Original()),"H2_ControlExternalBeforePreflight");
        File.WriteAllBytes(files[1].Path,files[1].Original());
        failure=Capture(()=>FileTransaction.Commit(files,afterReplace:_=>throw new IOException("Synthetic failure")));
        result(failure is IOException&&files.All(f=>Is(f,f.Original())),"H2_ControlNormalRollback");
        failure=Capture(()=>FileTransaction.Commit(files,afterReplace:_=>{WriteExternal(files[0].Path);throw new IOException("Synthetic failure");}));
        result(failure is IOException&&Is(files[0],External)&&Is(files[1],files[1].Original())&&Is(files[2],files[2].Original()),"H2_ControlExternalBeforeRollbackComparison");
        root=Create(parent,"recovery-controls");files=Interrupted(root);
        WriteExternal(files[0].Path);
        failure=Capture(()=>RecoveryJournal.Recover(Journal(root),root,History(root)));
        result(failure is IOException&&Is(files[0],External)&&Is(files[1],files[1].Proposed())&&Directory.Exists(Journal(root)),"H3_ControlExternalBeforePreflight");
        File.WriteAllBytes(files[0].Path,files[0].Proposed());
        File.WriteAllBytes(Path.Combine(Journal(root),"0.original"),[0xFF]);
        failure=Capture(()=>RecoveryJournal.Recover(Journal(root),root,History(root)));
        result(failure is InvalidDataException&&Is(files[0],files[0].Proposed())&&Is(files[1],files[1].Proposed())&&Directory.Exists(Journal(root)),"H3_ControlCorruptBackupRejectedBeforeWrites");
        File.WriteAllBytes(Path.Combine(Journal(root),"0.original"),files[0].Original());
        RecoveryJournal.Recover(Journal(root),root,History(root));
        result(files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root))&&!Directory.GetFiles(root,"*.studio-*.tmp").Any(),"H3_ControlNormalInterruptedRecovery");
        RecoveryJournal.Recover(Journal(root),root,History(root));
        result(files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root)),"H3_ControlRecoveryIdempotent");
    }
}
