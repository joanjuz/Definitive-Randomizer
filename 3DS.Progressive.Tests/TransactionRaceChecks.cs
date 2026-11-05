using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ThreeDS.Progressive.Services;

/// <summary>
/// Security invariants of FileTransaction and RecoveryJournal under synthetic concurrent writers.
/// Every held target refuses writers, so the two kinds of case answer different questions:
/// a pause on the held stretch proves the refusal; a pause on the vacated name, the one moment a
/// writer can still succeed, proves that its acknowledged edit survives.
/// </summary>
internal static class TransactionRaceChecks
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private static readonly byte[] External = [0xE1, 0xE2, 0xE3, 0xE4];
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(20);
    private static FileChange[] Changes(string root) =>
        [new(Path.Combine(root,"first.bin"),[1,2,3,4],[11,12,13,14]),
         new(Path.Combine(root,"second.bin"),[5,6,7,8],[15,16,17,18]),
         new(Path.Combine(root,"third.bin"),[21,22,23,24],[31,32,33,34]),
         new(Path.Combine(root,"fourth.bin"),[41,42,43,44],[51,52,53,54])];
    private static string Journal(string root) => Path.Combine(root,"test.pending");
    private static string History(string root) => Path.Combine(root,"history.json");
    private static byte[] ReadShared(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var memory=new MemoryStream();stream.CopyTo(memory);return memory.ToArray();
    }
    private static void WriteExternal(string path)
    {
        // OpenOrCreate: a save that lands in the vacated name creates the file, as any editor would.
        using var stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);
        stream.Write(External);stream.SetLength(External.Length);stream.Flush(true);
        Console.WriteLine($"TRACE external write flushed: {Path.GetFileName(path)} bytes={Bytes(External)}");
    }
    private static bool Refused(Action attempt)
    {
        try { attempt(); return false; }
        catch(IOException ex) when(ex.HResult==SharingViolation) { return true; }
        catch(UnauthorizedAccessException) { return true; }
    }
    /// <summary>Every way another program can change the bytes behind a name.</summary>
    private static bool AllRefused(string path,string name)
    {
        string side=path+".actor";File.WriteAllBytes(side,External);
        byte[] before=ReadShared(path);
        var actors=new (string Name,Action Attempt)[]
        {
            ("in-place write",()=>WriteExternal(path)),
            ("truncate",()=>new FileStream(path,FileMode.Truncate,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete).Dispose()),
            ("delete",()=>File.Delete(path)),
            ("rename away",()=>File.Move(path,path+".moved")),
            ("rename over",()=>File.Move(side,path,true)),
            ("File.Replace over",()=>File.Replace(side,path,null)),
        };
        bool all=true;
        foreach(var actor in actors)
        {
            bool refused=Refused(actor.Attempt);all&=refused;
            Console.WriteLine($"TRACE {name} actor={actor.Name}: {(refused?"refused":"ACCEPTED")}");
        }
        all&=File.Exists(path)&&ReadShared(path).SequenceEqual(before)&&!File.Exists(path+".moved");
        File.Delete(side);
        return all;
    }
    private static bool Is(FileChange file, byte[] bytes) => ReadShared(file.Path).SequenceEqual(bytes);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string message)
    { if(!value)throw new IOException("Harness/control failure: "+message); }
    private static Exception? Capture(Action action)
    { try { action(); return null; } catch(Exception ex) { return ex; } }
    private static string Bytes(byte[] bytes) => $"{Convert.ToHexString(bytes)} sha256={Convert.ToHexString(SHA256.HashData(bytes))}";
    private static string[] Residual(string root) =>
        [..Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>p.Contains(".studio-",StringComparison.Ordinal)).Select(p=>Path.GetRelativePath(root,p)).Order()];
    private static void Observe(string name,FileChange file,Exception? failure,string root)
    {
        Console.WriteLine($"TRACE {name}: before={Bytes(file.Original())}; proposed={Bytes(file.Proposed())}; external={Bytes(External)}; observed={Bytes(ReadShared(file.Path))}; exception={failure?.GetType().Name??"none"}; journal={Directory.Exists(Journal(root))}");
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

    private static EventWaitHandle Signal(string root,string name) =>
        new(false,EventResetMode.ManualReset,"Local\\h2h3-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..24]+"-"+name);

    // Invoked only on a parent-created synthetic root, with an explicit ownership marker.
    internal static void InterruptChild(string root,string mode)
    {
        root=Path.GetFullPath(root);
        Require(root.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            &&Path.GetFileName(Path.GetDirectoryName(root)!).StartsWith("transaction-races-",StringComparison.Ordinal)
            &&File.ReadAllText(Path.Combine(root,"synthetic.owner"))=="H2H3 synthetic only", "child ownership marker");
        var files=Changes(root);
        if(mode=="recover")
        {
            // The parent kills this process while it is paused inside the restoration.
            RecoveryJournal.Recover(Journal(root),root,History(root));
            throw new IOException("Child recovery was not interrupted.");
        }
        Require(files.All(f=>Is(f,f.Original())),"child accepts only the expected synthetic bytes");
        using var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files);
        if(mode=="commit-vacated")
        {
            using var ready=Signal(root,"ready");using var armed=Signal(root,"armed");
            FileTransaction.Commit(files,afterReplace:n=>{if(n!=1)return;ready.Set();Require(armed.WaitOne(Watchdog),"child arm watchdog");},recovery:journal);
            throw new IOException("Child commit was not interrupted.");
        }
        FileTransaction.Commit(files,afterReplace:n=>{if(n==3)Environment.Exit(91);},recovery:journal);
        throw new IOException("Child did not interrupt the transaction.");
    }
    private static Process StartChild(string root,string mode)
    {
        File.WriteAllText(Path.Combine(root,"synthetic.owner"),"H2H3 synthetic only");
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--interrupt-transaction-races");start.ArgumentList.Add(root);start.ArgumentList.Add(mode);
        return Process.Start(start)??throw new IOException("Cannot start synthetic child.");
    }
    private static FileChange[] Interrupted(string root)
    {
        using var child=StartChild(root,"commit");
        if(!child.WaitForExit((int)Watchdog.TotalMilliseconds))
        {child.Kill(entireProcessTree:true);child.WaitForExit();throw new TimeoutException("Interrupted child watchdog.");}
        var files=Changes(root);
        Require(child.ExitCode==91&&Is(files[0],files[0].Proposed())&&Is(files[1],files[1].Proposed())&&Is(files[2],files[2].Proposed())
            &&Is(files[3],files[3].Original())&&File.Exists(Path.Combine(Journal(root),"manifest.json")),"durable real interrupted commit");
        Console.WriteLine("TRACE child exit=91; first=proposed; second=proposed; third=proposed; fourth=before; durable journal present");
        Console.WriteLine($"TRACE interrupted manifest={Bytes(File.ReadAllBytes(Path.Combine(Journal(root),"manifest.json")))}");
        return files;
    }
    private static string Token(string root)
    {
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Journal(root),"manifest.json")));
        return doc.RootElement.GetProperty("Token").GetString()!;
    }
    private static string Temporary(string root,FileChange file) => file.Path+".studio-"+Token(root)+".tmp";
    private static void Paused(Task worker,TransactionOpenBarrier barrier,Action edit)
    {
        try
        {
            barrier.WaitForOpen();
            Require(!worker.IsCompleted,"worker must remain blocked at the barrier");
            Console.WriteLine("TRACE oplock break acknowledged by event; worker blocked at the instrumented operation");
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
    /// <summary>
    /// Recover stages from the last target to the first, then holds them all, then swaps in the same
    /// order. The staging of files[0] is therefore a known point at which the other staged files
    /// exist and no target is held yet. It both is a pause and lets a second barrier be armed.
    /// </summary>
    private static Exception? RecoverPaused(string root,FileChange[] files,Action edit,string? second=null,bool rename=false)
    {
        string first=Temporary(root,files[0]);File.WriteAllBytes(first,[0xAA]);
        Exception? failure=null;TransactionOpenBarrier? next=null;
        var staging=new TransactionOpenBarrier(first);
        var worker=Task.Run(()=>failure=Capture(()=>RecoveryJournal.Recover(Journal(root),root,History(root))));
        if(second is null)
        {
            using(staging)Paused(worker,staging,edit);
            return failure;
        }
        using(staging)
        {
            staging.WaitForOpen();Require(!worker.IsCompleted,"recovery paused while staging");
            next=new TransactionOpenBarrier(second,rename);
        }
        using(next)Paused(worker,next,edit);
        return failure;
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
            CommitHeld(Create(parent,"commit-held"),Result);
            CommitBeforeHold(Create(parent,"commit-before-hold"),Result);
            ExistingWriters(parent,Result);
            RollbackHeld(Create(parent,"rollback-held"),Result);
            RecoveryHeld(Create(parent,"recover-held"),Result);
            RecoveryBeforeHold(Create(parent,"recover-before-hold"),Result);
            CrashedCommit(Create(parent,"crash-commit"),Result);
            CrashedRecovery(Create(parent,"crash-recovery"),Result);
            RecoveryEvidence(parent,Result);
            Compatibility(parent,Result);
        }
        finally {Directory.Delete(parent,true);}
        Console.WriteLine($"Transaction race results: Passed: {results.Count(r=>r.Pass)}, Failed: {results.Count(r=>!r.Pass)}, Skipped: 0");
        // Report every case first, because the repository Check callback throws on its first failure.
        foreach(var result in results.Where(r=>r.Pass))check(true,result.Name);
        check(results.All(r=>r.Pass),"H2H3_AllExternalEditsPreserved (see individual invariant failures above)");
    }

    // The only moment a writer can still succeed: the name is empty between the two renames.
    private static void CommitRace(string root,Action<bool,string> result,bool edit=true)
    {
        var files=Changes(root);var victim=files[1];Exception? failure=null;
        using var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files);
        string retired=ExclusiveFile.RetiredPath(victim.Path,journal.Token);
        TransactionOpenBarrier? barrier=null;
        using var armed=new ManualResetEventSlim();
        var worker=Task.Run(()=>{try
        {
            FileTransaction.Commit(files,afterReplace:n=>
            {
                if(n!=1)return;
                // All sources are staged and held. The staged file's rename installs the victim.
                barrier=new TransactionOpenBarrier(journal.TemporaryPath(victim.Path),pauseAtRename:true);armed.Set();
            },recovery:journal);
        }catch(Exception ex){failure=ex;}finally{armed.Set();}});
        if(!armed.Wait(Watchdog))AbortHarness("commit arm timeout");
        Require(barrier is not null,"commit barrier armed: "+failure);
        using(barrier)
            Paused(worker,barrier,()=>
            {
                Require(!File.Exists(victim.Path)&&ReadShared(retired).SequenceEqual(victim.Original()),"name vacated after the final hash, original retired");
                if(!edit)return;
                WriteExternal(victim.Path);
                Require(Is(victim,External),"external write completed before releasing the staged rename");
                Console.WriteLine("TRACE commit external write complete; releasing the staged rename");
            });
        Observe(edit?"H2 commit":"calibration commit",victim,failure,root);
        if(!edit)
        {
            Require(failure is null&&files.All(f=>Is(f,f.Proposed()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"commit barrier must permit normal success");
            result(true,"Harness_CommitBarrierAllowsSuccessWithoutWriter");return;
        }
        result(Is(victim,External),"H2_CommitPreservesExternalEditAfterFinalHash");
        result(failure is not null||Is(victim,External),"H2_CommitDoesNotSilentlySucceedAfterExternalLoss");
        result(failure is IOException&&Directory.Exists(Journal(root))&&ReadShared(retired).SequenceEqual(victim.Original())&&Is(files[0],files[0].Original())
            &&Is(files[2],files[2].Original())&&Is(files[3],files[3].Original()),"H2_CommitLostNameKeepsOriginalJournalAndRestoresTheRest");
    }

    // A pause on the staged file's open: the set is held, checked, and about to be replaced.
    private static void CommitHeld(string root,Action<bool,string> result)
    {
        var files=Changes(root);var victim=files[1];Exception? failure=null;bool refused=false;
        using var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files);
        TransactionOpenBarrier? barrier=null;using var armed=new ManualResetEventSlim();
        var worker=Task.Run(()=>{try
        {
            FileTransaction.Commit(files,afterReplace:n=>{if(n==1){barrier=new TransactionOpenBarrier(journal.TemporaryPath(victim.Path));armed.Set();}},recovery:journal);
        }catch(Exception ex){failure=ex;}finally{armed.Set();}});
        if(!armed.Wait(Watchdog))AbortHarness("held commit arm timeout");
        Require(barrier is not null,"held commit barrier armed: "+failure);
        using(barrier)Paused(worker,barrier,()=>
        {
            Require(Is(victim,victim.Original()),"readers still see the held target");
            refused=AllRefused(victim.Path,"held commit")&&AllRefused(files[3].Path,"held commit, later target")&&AllRefused(files[0].Path,"held commit, installed target");
        });
        Observe("held commit",victim,failure,root);
        result(refused,"H2_HeldTargetsRefuseEveryWriterAfterFinalHash");
        result(failure is null&&files.All(f=>Is(f,f.Proposed()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"H2_CommitCompletesAfterRefusedWriters");
    }

    // A real edit before the hold must be seen by the check made through the held handle.
    private static void CommitBeforeHold(string root,Action<bool,string> result)
    {
        var files=Changes(root);var victim=files[1];Exception? failure=null;
        using var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files);
        string temp=journal.TemporaryPath(victim.Path);File.WriteAllBytes(temp,[0xAA]);
        using(var barrier=new TransactionOpenBarrier(temp))
        {
            var worker=Task.Run(()=>failure=Capture(()=>FileTransaction.Commit(files,recovery:journal)));
            Paused(worker,barrier,()=>{WriteExternal(files[0].Path);Require(Is(files[0],External),"edit before the hold");});
        }
        Observe("commit before hold",files[0],failure,root);
        result(failure is IOException&&Is(files[0],External)&&Is(files[1],files[1].Original())&&Is(files[2],files[2].Original())&&Is(files[3],files[3].Original())
            &&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"H2_EditBeforeHoldRejectedWithoutWrites");
    }

    private static void ExistingWriters(string parent,Action<bool,string> result)
    {
        string root=Create(parent,"existing-writers");var files=Changes(root);
        Exception? failure;
        using(new FileStream(files[2].Path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))
            failure=Capture(()=>FileTransaction.Commit(files));
        result(failure is IOException&&files.All(f=>Is(f,f.Original()))&&Residual(root).Length==0,"H2_OpenWriterHandleStopsCommitBeforeAnyChange");
        var mapping=MemoryMappedFile.CreateFromFile(files[2].Path,FileMode.Open,null,0,MemoryMappedFileAccess.ReadWrite);
        var view=mapping.CreateViewAccessor();mapping.Dispose();
        failure=Capture(()=>FileTransaction.Commit(files));
        view.Dispose();
        result(failure is IOException&&files.All(f=>Is(f,f.Original()))&&Residual(root).Length==0,"H2_WritableMappingWithoutHandleStopsCommitBeforeAnyChange");
    }

    private static void RollbackRace(string root,Action<bool,string> result,bool edit=true)
    {
        var files=Changes(root);var victim=files[0];Exception? failure=null;
        using var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files);
        string retired=ExclusiveFile.RetiredPath(victim.Path,journal.Token);
        TransactionOpenBarrier? barrier=null;using var armed=new ManualResetEventSlim();
        var worker=Task.Run(()=>{try
        {
            FileTransaction.Commit(files,afterReplace:n=>
            {
                Require(n==1,"rollback injection after first replacement");
                // Rollback moves the replacement away, then renames the original back.
                barrier=new TransactionOpenBarrier(retired,pauseAtRename:true);armed.Set();
                throw new IOException("Synthetic rollback trigger");
            },recovery:journal);
        }catch(Exception ex){failure=ex;}finally{armed.Set();}});
        if(!armed.Wait(Watchdog))AbortHarness("rollback arm timeout");
        Require(barrier is not null,"rollback barrier armed: "+failure);
        using(barrier)Paused(worker,barrier,()=>
        {
            Require(!File.Exists(victim.Path)&&ReadShared(retired).SequenceEqual(victim.Original()),"rollback verified the proposed bytes and vacated the name");
            if(!edit)return;
            WriteExternal(victim.Path);Require(Is(victim,External),"rollback external write completed");
        });
        Observe(edit?"H2 rollback":"calibration rollback",victim,failure,root);
        if(!edit)
        {
            Require(failure is IOException&&failure.Message=="Synthetic rollback trigger"
                &&files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"rollback barrier must allow normal rollback");
            result(true,"Harness_RollbackBarrierAllowsRestoreWithoutWriter");return;
        }
        result(failure is IOException,"H2_RollbackReportsOriginalFailure");
        result(failure?.InnerException?.Message=="Synthetic rollback trigger","H2_RollbackKeepsTheCauseAfterLosingTheName");
        result(Is(victim,External),"H2_RollbackPreservesExternalEditAfterComparison");
        result(Is(files[1],files[1].Original())&&Is(files[2],files[2].Original())&&Is(files[3],files[3].Original()),"H2_RollbackLeavesUnreplacedTargetsUntouched");
        result(Is(victim,External)||Directory.Exists(Journal(root)),"H2_RollbackRetainsEvidenceIfExternalEditLost");
        result(Directory.Exists(Journal(root))&&ReadShared(retired).SequenceEqual(victim.Original()),"H2_RollbackLostNameKeepsOriginalAndJournal");
    }

    // Rollback of two replacements: while the second is restored, the first is still held.
    private static void RollbackHeld(string root,Action<bool,string> result)
    {
        var files=Changes(root);Exception? failure=null;bool refused=false;
        using var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files);
        TransactionOpenBarrier? barrier=null;using var armed=new ManualResetEventSlim();
        var worker=Task.Run(()=>{try
        {
            FileTransaction.Commit(files,afterReplace:n=>
            {
                if(n!=2)return;
                barrier=new TransactionOpenBarrier(ExclusiveFile.RetiredPath(files[1].Path,journal.Token),pauseAtRename:true);armed.Set();
                throw new IOException("Synthetic rollback trigger");
            },recovery:journal);
        }catch(Exception ex){failure=ex;}finally{armed.Set();}});
        if(!armed.Wait(Watchdog))AbortHarness("held rollback arm timeout");
        Require(barrier is not null,"held rollback barrier armed: "+failure);
        using(barrier)Paused(worker,barrier,()=>
        {
            Require(Is(files[0],files[0].Proposed()),"first target awaits its restoration");
            refused=AllRefused(files[0].Path,"held rollback");
        });
        Observe("held rollback",files[0],failure,root);
        result(refused,"H2_TargetAwaitingRollbackRefusesEveryWriter");
        result(failure?.Message=="Synthetic rollback trigger"&&files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"H2_RollbackCompletesAfterRefusedWriters");
    }

    private static void RecoveryRace(string root,bool multiple,Action<bool,string> result,bool edit=true)
    {
        var files=Interrupted(root);var victim=files[multiple?1:2];string retired=ExclusiveFile.RetiredPath(victim.Path,Token(root));
        var failure=RecoverPaused(root,files,()=>
        {
            Require(!File.Exists(victim.Path),"recovery verified the set and vacated the name");
            if(multiple)Require(Is(files[2],files[2].Original()),"reverse first restoration already completed");
            if(!edit)return;
            WriteExternal(victim.Path);Require(Is(victim,External),"recovery external write completed");
        },Temporary(root,victim),rename:true);
        string label=multiple?"H3_Multiple":"H3_Single";
        Observe(edit?label:"calibration recovery",victim,failure,root);
        if(!edit)
        {
            Require(failure is null&&files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"recovery barrier must allow normal restore");
            result(true,"Harness_RecoveryBarrierAllowsRestoreWithoutWriter");return;
        }
        result(Is(victim,External),label+"PreservesExternalEditAfterFinalHash");
        result(Is(victim,External)||Directory.Exists(Journal(root)),label+"RetainsJournalIfExternalEditLost");
        result(Is(files[3],files[3].Original()),label+"LeavesNeverCommittedTargetUntouched");
        result(failure is IOException&&Directory.Exists(Journal(root))&&File.Exists(retired),label+"LostNameIsReportedWithJournalAndRetiredFile");
    }

    private static void RecoveryPartial(string root,Action<bool,string> result)
    {
        var files=Interrupted(root);
        var failure=RecoverPaused(root,files,()=>{WriteExternal(files[1].Path);Require(Is(files[1],External),"later target edited");},Temporary(root,files[1]),rename:true);
        Observe("H3 partial later target",files[1],failure,root);
        result(failure is IOException&&Is(files[1],External)&&Is(files[2],files[2].Original())
            &&Is(files[3],files[3].Original())&&Is(files[0],files[0].Proposed())&&Directory.Exists(Journal(root)),"H3_PartialRecoveryRejectsLaterConflictAndRetainsJournal");
        byte[] manifest=File.ReadAllBytes(Path.Combine(Journal(root),"manifest.json"));
        var retry=Capture(()=>RecoveryJournal.Recover(Journal(root),root,History(root)));
        result(retry is IOException&&Is(files[1],External)&&Is(files[2],files[2].Original())
            &&File.ReadAllBytes(Path.Combine(Journal(root),"manifest.json")).SequenceEqual(manifest),"H3_PartialRetryPreservesConflictAndManifest");
        // Explicit reconciliation of synthetic bytes only; verify retained backups really permit retry.
        File.WriteAllBytes(files[1].Path,files[1].Proposed());
        RecoveryJournal.Recover(Journal(root),root,History(root));
        result(files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"H3_ReconciledPartialRecoveryCanFinish");
    }

    // Pause on the staged file's open, inside the swap: the set is held and checked.
    private static void RecoveryHeld(string root,Action<bool,string> result)
    {
        var files=Interrupted(root);bool refused=false;
        var failure=RecoverPaused(root,files,()=>
        {
            Require(Is(files[2],files[2].Proposed()),"nothing restored yet");
            refused=AllRefused(files[2].Path,"held recovery")&&AllRefused(files[0].Path,"held recovery, later target")&&AllRefused(files[3].Path,"held recovery, unaffected target");
        },Temporary(root,files[2]));
        Observe("held recovery",files[2],failure,root);
        result(refused,"H3_HeldSetRefusesEveryWriterAfterFinalHash");
        result(failure is null&&files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"H3_RecoveryCompletesAfterRefusedWriters");
    }

    // An edit that lands before the hold now stops the whole set: a conflict no longer leaves a partial restore.
    private static void RecoveryBeforeHold(string root,Action<bool,string> result)
    {
        var files=Interrupted(root);byte[] manifest=File.ReadAllBytes(Path.Combine(Journal(root),"manifest.json"));
        var failure=RecoverPaused(root,files,()=>{WriteExternal(files[0].Path);Require(Is(files[0],External),"edit before the hold");});
        Observe("recovery before hold",files[0],failure,root);
        result(failure is IOException&&Is(files[0],External)&&Is(files[1],files[1].Proposed())&&Is(files[2],files[2].Proposed())&&Is(files[3],files[3].Original())
            &&Directory.Exists(Journal(root))&&File.ReadAllBytes(Path.Combine(Journal(root),"manifest.json")).SequenceEqual(manifest),"H3_EditBeforeHoldRejectsTheWholeSet");
        File.WriteAllBytes(files[0].Path,files[0].Proposed());
        RecoveryJournal.Recover(Journal(root),root,History(root));
        result(files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"H3_ReconciledSetCanFinish");
    }

    /// <summary>Kills the child while it is paused and proves the kill landed before the next rename.</summary>
    private static void Kill(Process child,TransactionOpenBarrier barrier,Func<bool> interruptedState,string name)
    {
        child.Kill(entireProcessTree:true);
        if(!child.WaitForExit((int)Watchdog.TotalMilliseconds))AbortHarness(name+": child did not die while paused");
        barrier.Release();
        Require(interruptedState(),name+": kill must land before the paused rename");
        Console.WriteLine($"TRACE {name}: killed with the target name vacated");
    }

    private static void CrashedCommit(string root,Action<bool,string> result)
    {
        var files=Changes(root);var victim=files[1];
        using var ready=Signal(root,"ready");using var armed=Signal(root,"armed");
        using var child=StartChild(root,"commit-vacated");
        try
        {
            if(WaitHandle.WaitAny([ready,new ManualResetEvent(false){SafeWaitHandle=new Microsoft.Win32.SafeHandles.SafeWaitHandle(child.Handle,false)}],Watchdog)!=0)
                throw new IOException("Harness/control failure: child never reached the first replacement.");
            string retired=ExclusiveFile.RetiredPath(victim.Path,Token(root));
            using(var barrier=new TransactionOpenBarrier(Temporary(root,victim),pauseAtRename:true))
            {
                armed.Set();barrier.WaitForOpen();
                Kill(child,barrier,()=>!File.Exists(victim.Path)&&File.Exists(retired),"crash commit");
            }
            Require(Is(files[0],files[0].Proposed())&&Is(files[2],files[2].Original()),"crash left a real partial commit");
        }
        finally{if(!child.HasExited){child.Kill(entireProcessTree:true);child.WaitForExit();}}
        RecoveryJournal.Recover(Journal(root),root,History(root));
        result(files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"H3_ProcessKilledBetweenRenamesRecovers");
    }

    private static void CrashedRecovery(string root,Action<bool,string> result)
    {
        var files=Interrupted(root);var victim=files[2];
        string first=Temporary(root,files[0]),temp=Temporary(root,victim),retired=ExclusiveFile.RetiredPath(victim.Path,Token(root));
        File.WriteAllBytes(first,[0xAA]);
        var staging=new TransactionOpenBarrier(first);TransactionOpenBarrier? swap=null;
        using var child=StartChild(root,"recover");
        try
        {
            staging.WaitForOpen();
            swap=new TransactionOpenBarrier(temp,pauseAtRename:true);
            staging.Dispose();
            swap.WaitForOpen();
            Kill(child,swap,()=>!File.Exists(victim.Path)&&File.Exists(retired),"crash recovery");
        }
        finally{staging.Dispose();swap?.Dispose();if(!child.HasExited){child.Kill(entireProcessTree:true);child.WaitForExit();}}
        RecoveryJournal.Recover(Journal(root),root,History(root));
        result(files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"H3_ProcessKilledDuringRecoveryRecoversOnRetry");
    }

    private static void RecoveryEvidence(string parent,Action<bool,string> result)
    {
        // Journals written by earlier versions have no retired files: replacement used File.Replace.
        string root=Create(parent,"previous-version");var files=Interrupted(root);
        foreach(var file in files)File.Delete(ExclusiveFile.RetiredPath(file.Path,Token(root)));
        RecoveryJournal.Recover(Journal(root),root,History(root));
        result(files.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root)),"H3_PreviousVersionJournalRecovers");

        root=Create(parent,"deleted-target");files=Interrupted(root);
        string retired=ExclusiveFile.RetiredPath(files[1].Path,Token(root));
        File.Delete(retired);File.Delete(files[1].Path);
        var failure=Capture(()=>RecoveryJournal.Recover(Journal(root),root,History(root)));
        result(failure is IOException&&!File.Exists(files[1].Path)&&Is(files[0],files[0].Proposed())&&Is(files[2],files[2].Proposed())&&Directory.Exists(Journal(root)),"H3_TargetDeletedOutsideRejectedWithoutWrites");

        root=Create(parent,"altered-retired");files=Interrupted(root);
        retired=ExclusiveFile.RetiredPath(files[1].Path,Token(root));
        File.WriteAllBytes(retired,External);
        failure=Capture(()=>RecoveryJournal.Recover(Journal(root),root,History(root)));
        result(failure is IOException&&File.ReadAllBytes(retired).SequenceEqual(External)&&Is(files[0],files[0].Proposed())&&Is(files[1],files[1].Proposed())&&Directory.Exists(Journal(root)),"H3_AlteredRetiredFileRejectedAndKept");
    }

    // What File.Replace gave the editing flows and a rename would not give by itself.
    private static void Compatibility(string parent,Action<bool,string> result)
    {
        Exception? failure;
        string root=Create(parent,"metadata");var files=Changes(root);var created=new DateTime(2001,2,3,4,5,6,DateTimeKind.Utc);
        File.SetAttributes(files[0].Path,FileAttributes.Hidden);File.SetCreationTimeUtc(files[0].Path,created);
        var info=new FileInfo(files[1].Path);var security=info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadData,AccessControlType.Allow));
        info.SetAccessControl(security);
        string acl=new FileInfo(files[1].Path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        using(var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files))FileTransaction.Commit(files,recovery:journal);
        string aclAfter=new FileInfo(files[1].Path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        Console.WriteLine($"TRACE metadata: proposed={files.All(f=>Is(f,f.Proposed()))} attributes={File.GetAttributes(files[0].Path)} created={File.GetCreationTimeUtc(files[0].Path):O} acl before={acl} after={aclAfter}");
        result(files.All(f=>Is(f,f.Proposed()))&&File.GetAttributes(files[0].Path).HasFlag(FileAttributes.Hidden)&&File.GetCreationTimeUtc(files[0].Path)==created
            &&new FileInfo(files[1].Path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access)==acl,"Compat_AttributesCreationTimeAndAclSurvive");

        root=Create(parent,"metadata-recovery");var interrupted=Interrupted(root);
        File.SetCreationTimeUtc(interrupted[2].Path,created);
        RecoveryJournal.Recover(Journal(root),root,History(root));
        result(interrupted.All(f=>Is(f,f.Original()))&&File.GetCreationTimeUtc(interrupted[2].Path)==created,"Compat_RecoveryKeepsTargetMetadata");

        // A staged file carries the target's attributes, and creating over a hidden file is refused.
        root=Create(parent,"hidden-staged");interrupted=Interrupted(root);
        string stale=Temporary(root,interrupted[1]);File.WriteAllBytes(stale,[0xAA]);File.SetAttributes(stale,FileAttributes.Hidden);
        failure=Capture(()=>RecoveryJournal.Recover(Journal(root),root,History(root)));
        result(failure is null&&interrupted.All(f=>Is(f,f.Original()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"Compat_HiddenStagedLeftoverDoesNotBlockRecovery");

        root=Create(parent,"read-only");files=Changes(root);File.SetAttributes(files[2].Path,FileAttributes.ReadOnly);
        failure=Capture(()=>FileTransaction.Commit(files));
        File.SetAttributes(files[2].Path,FileAttributes.Normal);
        result(failure is UnauthorizedAccessException&&files.All(f=>Is(f,f.Original()))&&Residual(root).Length==0,"Compat_ReadOnlyTargetStillRefused");

        root=Create(parent,"symlink");files=Changes(root);string real=Path.Combine(root,"real.bin");File.WriteAllBytes(real,files[2].Original());
        File.Delete(files[2].Path);
        if(Capture(()=>File.CreateSymbolicLink(files[2].Path,real)) is null)
        {
            failure=Capture(()=>FileTransaction.Commit(files));
            result(failure is IOException&&new FileInfo(files[2].Path).LinkTarget is not null&&File.ReadAllBytes(real).SequenceEqual(files[2].Original())
                &&Is(files[0],files[0].Original())&&Residual(root).Length==0,"Compat_SymbolicLinkTargetStillRefused");
        }
        else Console.WriteLine("TRACE symbolic links unavailable to this account: case not exercised");

        root=Create(parent,"spelling");
        var spelled=new FileChange(Path.Combine(root,"FIRST.BIN"),[1,2,3,4],[11,12,13,14]);
        FileTransaction.Commit([spelled]);
        result(Directory.GetFiles(root,"first.bin").Select(Path.GetFileName).SequenceEqual(["first.bin"]),"Compat_ExistingSpellingSurvives");

        root=parent;while(root.Length<300)root=Path.Combine(root,new string('d',40));root=Create(root,"long");files=Changes(root);
        Exception? rollback;
        using(var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files))
            rollback=Capture(()=>FileTransaction.Commit(files,afterReplace:n=>{if(n==2)throw new IOException("Synthetic failure");},recovery:journal));
        bool restored=rollback is IOException&&files.All(f=>Is(f,f.Original()));
        using(var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files))FileTransaction.Commit(files,recovery:journal);
        result(restored&&files.All(f=>Is(f,f.Proposed()))&&!Directory.Exists(Journal(root)),"Compat_LongPathsCommitAndRollBack");

        root=Create(parent,"device-prefix");
        var prefixed=new FileChange("\\\\?\\"+Path.Combine(root,"first.bin"),[1,2,3,4],[11,12,13,14]);
        FileTransaction.Commit([prefixed]);
        result(File.ReadAllBytes(Path.Combine(root,"first.bin")).SequenceEqual(prefixed.Proposed())&&Residual(root).Length==0,"Compat_DevicePrefixedPathCommits");

        // Documented limit, kept visible: a reader without FileShare.Delete is refused during the hold.
        root=Create(parent,"readers");files=Changes(root);bool strict=false,shared=false;
        FileTransaction.Commit(files,afterReplace:n=>{if(n!=1)return;strict=Refused(()=>File.ReadAllBytes(files[3].Path));shared=Is(files[3],files[3].Original());});
        result(strict&&shared,"Limit_ReadersNeedFileShareDeleteDuringTheHold");
    }

    private static void Controls(string parent,Action<bool,string> result)
    {
        string root=Create(parent,"controls");var files=Changes(root);
        using(var journal=RecoveryJournal.Begin(Journal(root),root,History(root),files))FileTransaction.Commit(files,recovery:journal);
        result(files.All(f=>Is(f,f.Proposed()))&&!Directory.Exists(Journal(root))&&Residual(root).Length==0,"H2_ControlNormalCommit");
        foreach(var f in files)File.WriteAllBytes(f.Path,f.Original());
        WriteExternal(files[1].Path);
        var failure=Capture(()=>FileTransaction.Commit(files));
        result(failure is IOException&&Is(files[1],External)&&Is(files[0],files[0].Original())&&Is(files[2],files[2].Original()),"H2_ControlExternalBeforePreflight");
        File.WriteAllBytes(files[1].Path,files[1].Original());
        failure=Capture(()=>FileTransaction.Commit(files,afterReplace:_=>throw new IOException("Synthetic failure")));
        result(failure is IOException&&files.All(f=>Is(f,f.Original()))&&Residual(root).Length==0,"H2_ControlNormalRollback");
        bool refused=false;
        failure=Capture(()=>FileTransaction.Commit(files,afterReplace:_=>{refused=AllRefused(files[0].Path,"control");throw new IOException("Synthetic failure");}));
        result(refused&&failure is IOException&&files.All(f=>Is(f,f.Original())),"H2_ControlWriterRefusedBeforeRollbackComparison");
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
