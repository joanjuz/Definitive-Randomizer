using System.IO;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

/// <summary>Production refusal and shared transaction infrastructure; no Gen6 item-restoration algorithm is accredited.</summary>
internal static class Gen6PersistentItemsChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var fixture=new Fixture(GameVersion.XY);
        string actual=Path.Combine(fixture.Game.RomFS,"DllBattle.cro");
        string requested=Path.Combine(fixture.Game.RomFS,"DLLBattle.cro");
        bool ExactName()=>Directory.GetFiles(fixture.Game.RomFS,"*.cro")
            .Select(Path.GetFileName).SequenceEqual(new[]{"DllBattle.cro"});

        // This fixture represents the XY family, not separately accredited X and Y title profiles.
        check(!ProjectFeatureAvailability.Supports(fixture.Session,"PersistentItems"),
            "Gen6PersistentItems_XyFeatureRemainsUnavailable");
        foreach(var (name,bytes) in new[]{("Cro",CroIntegrity.Rebuild(CroIntegrityChecks.Cro(6))),
                     ("UsumSignatures",CroIntegrity.Rebuild(Review73Checks.Cro())),("Truncated",new byte[0x83])})
        {
            File.WriteAllBytes(actual,bytes);
            int historyCount=fixture.Session.History.Read().Entries.Count;
            bool refused=false;
            try{_=new GamePersistentItemsService(fixture.Session);}
            catch(NotSupportedException e){refused=e.Message.Contains("Ultra Sol/Ultra Luna",StringComparison.Ordinal);}
            check(refused,"Gen6PersistentItems_XyServiceRefuses_"+name);
            check(File.ReadAllBytes(actual).SequenceEqual(bytes)&&ExactName()&&
                fixture.Session.History.Read().Entries.Count==historyCount,
                "Gen6PersistentItems_RefusalPreservesBytesNameAndHistory_"+name);
        }

        // Exercise the shared infrastructure using a harmless synthetic data-byte edit, not a battle hook.
        byte[] original=CroIntegrity.Rebuild(CroIntegrityChecks.Cro(6));
        byte[] changed=(byte[])original.Clone();changed[0x320]^=0x40;changed=CroIntegrity.Rebuild(changed);
        File.WriteAllBytes(actual,original);
        var save=new FileChange(requested,original,changed);
        check(Path.GetFileName(save.Path)=="DllBattle.cro"&&File.ReadAllBytes(actual).SequenceEqual(original),
            "Gen6PersistentItems_FrozenChangePreservesPhysicalNameWithoutWriting");
        FileTransaction.Commit([save]);
        check(ExactName()&&File.ReadAllBytes(actual).SequenceEqual(changed)&&changed.Length==original.Length,
            "Gen6PersistentItems_TransactionPreservesDllBattleNameSizeAndBytes");
        check(CroIntegrity.Rebuild(File.ReadAllBytes(actual)).SequenceEqual(changed)&&
            original.AsSpan(0x80,0x2A0).SequenceEqual(changed.AsSpan(0x80,0x2A0))&&
            original.AsSpan(0x321).SequenceEqual(changed.AsSpan(0x321)),
            "Gen6PersistentItems_SyntheticEditPreservesOtherSegmentsAndValidHashes");

        reject(()=>FileTransaction.Commit([new FileChange(requested,changed,original)],
            _=>throw new IOException("Injected XY rollback")),"Gen6PersistentItems_InjectedTransactionFailureRejected");
        check(ExactName()&&File.ReadAllBytes(actual).SequenceEqual(changed),
            "Gen6PersistentItems_RollbackPreservesDllBattleNameAndPreviousBytes");

        var stale=new FileChange(requested,changed,original);
        byte[] external=(byte[])changed.Clone();external[0x330]^=1;external=CroIntegrity.Rebuild(external);
        File.WriteAllBytes(actual,external);
        reject(()=>FileTransaction.Commit([stale]),"Gen6PersistentItems_StaleTransactionRejected");
        check(ExactName()&&File.ReadAllBytes(actual).SequenceEqual(external),
            "Gen6PersistentItems_StaleRejectionPreservesConcurrentEdit");

        File.WriteAllBytes(actual,original);
        string pending=Path.Combine(fixture.Root,"xy-items.pending"),history=Path.Combine(fixture.Root,"xy-items-history.json");
        using(var journal=RecoveryJournal.Begin(pending,fixture.Root,history,[save]))
            File.WriteAllBytes(actual,changed); // Simulate replacement before the durable completion marker.
        RecoveryJournal.Recover(pending,fixture.Root,history);
        check(ExactName()&&File.ReadAllBytes(actual).SequenceEqual(original)&&!Directory.Exists(pending),
            "Gen6PersistentItems_RecoveryRestoresExactBytesAndPhysicalName");
        check(!Directory.EnumerateFiles(fixture.Game.RomFS).Any(p=>p.Contains(".studio-",StringComparison.Ordinal)),
            "Gen6PersistentItems_TransactionLeavesNoStagingFilesForRebuild");
    }
}
