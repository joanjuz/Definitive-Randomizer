using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using pk3DS.Core;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

/// <summary>Synthetic XY patch, installer, coexistence and case-preserving transaction checks.</summary>
internal static class Gen6PersistentItemsChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        RunInfrastructure(check,reject);
        RunPatcher(check,reject);
        RunService(check,reject);
        RunCoexistence(check,reject);
    }

    internal static byte[] Battle()
    {
        byte[] data=Enumerable.Repeat((byte)0xA5,0xD1800).ToArray();
        "CRO0"u8.CopyTo(data.AsSpan(0x80));
        U(0x90,data.Length);U(0xB0,0x180);U(0xB4,0xD0000);U(0xB8,0xD1600);U(0xBC,0x200);U(0xC0,0xD1000);
        U(0xC8,0xD1000);U(0xCC,4);
        int[] segments=[0x180,0xD0000,0,0xD1000,0x600,1,0xD1600,0x200,2,0,0x40,3];
        for(int i=0;i<segments.Length;i++)U(0xD1000+i*4,segments[i]);
        Convert.FromHexString("100099E50000D0E5010050E32000001A000054E30050A0E31D00009A").CopyTo(data,0x14848);
        Convert.FromHexString("0600A0E186AFFFEB0060A0E10010A0E15C0097E51120A0E3C1AEFFEB000050E300F020E32500000A").CopyTo(data,0x14888);
        Gen6KingShieldSignature.Original.CopyTo(data,Gen6PersistentItemsPatcher.KingShield);
        data.AsSpan(Gen6PersistentItemsPatcher.TypeChart,324).Fill(4);
        return CroIntegrity.Rebuild(data);
        void U(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at),value);
    }

    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    internal static string PathFor(Fixture fixture,string key)=>key.StartsWith("ExtractedRomFS/",StringComparison.Ordinal)
        ?Path.Combine(fixture.Game.RomFS,key[15..]):key.StartsWith("ExtractedExeFS/",StringComparison.Ordinal)
        ?Path.Combine(fixture.Game.ExeFS,key[15..]):Path.Combine(fixture.Root,key);
    internal static Gen6PersistentItemsProfile Setup(Fixture fixture,bool y)
    {
        ulong title=y?0x0004000000055E00UL:0x0004000000055D00UL;
        byte[] header=new byte[0x200],exheader=new byte[0x800];"NCCH"u8.CopyTo(header.AsSpan(0x100));
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x118),title);
        BinaryPrimitives.WriteUInt64LittleEndian(exheader.AsSpan(0x200),title);
        byte[] battle=Battle();
        // Reopening a real session also reads the native TM table. Keep that
        // unrelated contract valid instead of bypassing session construction.
        byte[] code=new byte[0x400200];code[0]=(byte)(y?2:1);
        GameMachine6Service.Signature.CopyTo(code,0x400000);
        for(int i=0;i<105;i++)BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(0x400008+i*2),1);
        var bytes=new Dictionary<string,byte[]>
        {
            ["HeaderNCCH0.bin"]=header,["DecryptedExHeader.bin"]=exheader,
            ["ExtractedExeFS/.code.bin"]=code,
            ["ExtractedRomFS/DllBattle.cro"]=battle,["ExtractedRomFS/DllField.cro"]=[0xB2,(byte)(y?2:1)],
            ["ExtractedRomFS/static.crs"]=[0xC3,(byte)(y?2:1)],
            ["ExtractedRomFS/.crr/static.crr"]=[0xD4,(byte)(y?2:1)],
            ["ExtractedRomFS/a/2/2/0"]=[0xE5,0x17]
        };
        foreach(var (key,value) in bytes){string path=PathFor(fixture,key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,value);}
        return new(title,y?"Synthetic Y":"Synthetic X",bytes.ToDictionary(pair=>pair.Key,pair=>Hash(pair.Value)),Gen6PersistentItemsPatcher.Shape(battle));
    }
    private static Dictionary<string,int> Enable(bool enabled)=>new(){{"Enable",enabled?1:0}};
    private static int State(GameGen6PersistentItemsService service)=>EditorValues.Flatten(service.Read(1))["Enable"];
    private static bool ExactName(Fixture fixture)=>Directory.GetFiles(fixture.Game.RomFS,"*.cro")
        .Count(p=>Path.GetFileName(p).Equals("DllBattle.cro",StringComparison.Ordinal))==1
        &&!Directory.GetFiles(fixture.Game.RomFS,"*.cro").Any(p=>Path.GetFileName(p)=="DLLBattle.cro");

    private static void RunInfrastructure(Action<bool,string> check,Action<Action,string> reject)
    {
        using var fixture=new Fixture(GameVersion.XY);
        string actual=Path.Combine(fixture.Game.RomFS,"DllBattle.cro");
        string requested=Path.Combine(fixture.Game.RomFS,"DLLBattle.cro");
        bool ExactName()=>Directory.GetFiles(fixture.Game.RomFS,"*.cro")
            .Select(Path.GetFileName).SequenceEqual(new[]{"DllBattle.cro"});

        // This fixture represents the XY family, not separately accredited X and Y title profiles.
        check(ProjectFeatureAvailability.Supports(fixture.Session,"PersistentItems"),
            "Gen6PersistentItems_XyDiagnosticSectionVisible");
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

    private static void RunPatcher(Action<bool,string> check,Action<Action,string> reject)
    {
        byte[] source=Battle();string shape=Gen6PersistentItemsPatcher.Shape(source),frozen=Hash(source);
        check(Gen6PersistentItemsPatcher.Inspect(source,shape)==Gen6PersistentItemsPatcher.State.Original,
            "Gen6PersistentItems_OriginalSyntheticShapeRecognized");
        reject(()=>Gen6PersistentItemsPatcher.Inspect(source),"Gen6PersistentItems_SyntheticShapeNeverAccreditedByProductionProfile");
        byte[] installed=Gen6PersistentItemsPatcher.Patch(source,true,shape);
        check(Hash(source)==frozen&&Gen6PersistentItemsPatcher.Inspect(installed,shape)==Gen6PersistentItemsPatcher.State.Installed,
            "Gen6PersistentItems_InstallLeavesSourceImmutableAndReadsBackInstalled");
        check(BinaryPrimitives.ReadUInt32LittleEndian(installed.AsSpan(0x14854))==0x8A000020
            &&BinaryPrimitives.ReadUInt32LittleEndian(installed.AsSpan(0x148AC))==0xEA000025
            &&Enumerable.Range(0x80,source.Length-0x80).Where(i=>source[i]!=installed[i]).SequenceEqual(new[]{0x14857,0x148AF})
            &&installed.Length==source.Length&&CroIntegrity.Rebuild(installed).SequenceEqual(installed),
            "Gen6PersistentItems_OnlyTwoConditionBytesAndValidHashesChange");
        check(Gen6PersistentItemsPatcher.Patch(installed,true,shape).SequenceEqual(installed)
            &&Gen6PersistentItemsPatcher.Patch(source,false,shape).SequenceEqual(source),"Gen6PersistentItems_BothDirectionsIdempotent");
        check(Gen6PersistentItemsPatcher.Patch(installed,false,shape).SequenceEqual(source),"Gen6PersistentItems_DisableRestoresEveryOriginalByte");
        foreach(int at in new[]{0x14854,0x148AC})
        {
            byte[] partial=(byte[])source.Clone();BinaryPrimitives.WriteUInt32LittleEndian(partial.AsSpan(at),at==0x14854?0x8A000020u:0xEA000025u);partial=CroIntegrity.Rebuild(partial);
            check(Gen6PersistentItemsPatcher.Inspect(partial,shape)==Gen6PersistentItemsPatcher.State.Partial,"Gen6PersistentItems_PartialRecognized_"+at.ToString("X"));
            reject(()=>Gen6PersistentItemsPatcher.Patch(partial,true,shape),"Gen6PersistentItems_PartialInstallNotRepaired_"+at.ToString("X"));
            reject(()=>Gen6PersistentItemsPatcher.Patch(partial,false,shape),"Gen6PersistentItems_PartialNotBlindlyRemoved_"+at.ToString("X"));
        }
        foreach(var (label,mutate,rehash) in new (string,Action<byte[]>,bool)[]
        {
            ("UnknownGate",b=>b[0x14854]^=1,true),("UnknownRestore",b=>b[0x148AC]^=1,true),
            ("MissingGate",b=>b[0x14848]^=1,true),("MissingRestore",b=>b[0x14888]^=1,true),
            ("DuplicateGate",b=>source.AsSpan(0x14848,28).CopyTo(b.AsSpan(0x400)),true),
            ("DuplicateRestore",b=>source.AsSpan(0x14888,40).CopyTo(b.AsSpan(0x500)),true),
            ("UnknownBody",b=>b[0x600]^=1,true),("InvalidHash",b=>b[0x700]^=1,false),
            ("InvalidTypeCell",b=>b[Gen6PersistentItemsPatcher.TypeChart+323]=3,true),
            ("MissingKingShield",b=>b[Gen6PersistentItemsPatcher.KingShield]^=1,true),
            ("DuplicateKingShield",b=>Gen6KingShieldSignature.Original.CopyTo(b,0x800),true)
        })
        {
            byte[] bad=(byte[])source.Clone();mutate(bad);if(rehash)bad=CroIntegrity.Rebuild(bad);string before=Hash(bad);
            reject(()=>Gen6PersistentItemsPatcher.Patch(bad,true,shape),"Gen6PersistentItems_Reject_"+label);
            check(Hash(bad)==before,"Gen6PersistentItems_RejectionNeverMutates_"+label);
        }
        foreach(int length in new[]{0x7F,0x180,0x14850,Gen6PersistentItemsPatcher.TypeChart+323})
            reject(()=>Gen6PersistentItemsPatcher.Inspect(source[..length],shape),"Gen6PersistentItems_TruncationRejected_"+length.ToString("X"));
        foreach(byte value in new byte[]{0,2,4,8})
        {
            byte[] compatible=(byte[])source.Clone();compatible[Gen6PersistentItemsPatcher.TypeChart]=value;
            Gen6KingShieldSignature.Patched.CopyTo(compatible,Gen6PersistentItemsPatcher.KingShield);compatible=CroIntegrity.Rebuild(compatible);
            byte[] result=Gen6PersistentItemsPatcher.Patch(compatible,true,shape);
            check(result[Gen6PersistentItemsPatcher.TypeChart]==value&&result[Gen6PersistentItemsPatcher.KingShield+4]==0xFF
                &&Gen6PersistentItemsPatcher.Patch(result,false,shape).SequenceEqual(compatible),"Gen6PersistentItems_AllowedCompanionEditsSurviveToggle_"+value);
        }
    }

    private static void RunService(Action<bool,string> check,Action<Action,string> reject)
    {
        using(var x=new Fixture(GameVersion.XY))using(var y=new Fixture(GameVersion.XY))
        {
            var px=Setup(x,false);var py=Setup(y,true);
            check(px.TitleId!=py.TitleId&&px.Pins["ExtractedExeFS/.code.bin"]!=py.Pins["ExtractedExeFS/.code.bin"]
                &&GameGen6PersistentItemsService.Inspect(x.Session,px).Allowed&&GameGen6PersistentItemsService.Inspect(y.Session,py).Allowed,
                "Gen6PersistentItems_IndependentSyntheticTitleProfilesRecognized");
            check(!GameGen6PersistentItemsService.Inspect(x.Session,py).Allowed&&!GameGen6PersistentItemsService.Inspect(y.Session,px).Allowed,
                "Gen6PersistentItems_CrossedTitleProfilesRejected");
            check(!GameGen6PersistentItemsService.Inspect(x.Session).Allowed&&!GameGen6PersistentItemsService.Inspect(y.Session).Allowed,
                "Gen6PersistentItems_ProductionProfilesRefuseAllSyntheticResources");
        }
        foreach(bool y in new[]{false,true})
        {
            using var f=new Fixture(GameVersion.XY);var profile=Setup(f,y);string path=PathFor(f,"ExtractedRomFS/DllBattle.cro"),tag=y?"Y":"X";
            byte[] source=File.ReadAllBytes(path);var service=new GameGen6PersistentItemsService(f.Session,profile);
            var beforeFiles=profile.Pins.Keys.ToDictionary(k=>k,k=>Hash(File.ReadAllBytes(PathFor(f,k))));
            var preview=service.Preview(Enable(true),[1]);
            check(State(service)==0&&preview.Changes.Count==2&&preview.Files.Count==1&&preview.Dependencies.Count==8
                &&File.ReadAllBytes(path).SequenceEqual(source)&&f.Session.History.Read().Entries.Count==0,
                "Gen6PersistentItems_ReadOnlyPreviewIncludesTwoWordsAndAllDependencies_"+tag);
            reject(()=>new GameGen6PersistentItemsService(f.Session,profile).Apply(preview),"Gen6PersistentItems_ForeignProposalRejected_"+tag);
            byte[] modified=preview.Files[0].Proposed();modified[Gen6PersistentItemsPatcher.TypeChart]=8;modified=CroIntegrity.Rebuild(modified);
            var tampered=new EditorProposal(preview.Owner,preview.Action,preview.Seed,[new FileChange(path,source,modified)],preview.Changes,preview.Parameters,preview.Dependencies);
            reject(()=>service.Apply(tampered),"Gen6PersistentItems_ModifiedFrozenBytesRejected_"+tag);
            var parameters=new Dictionary<string,string>(preview.Parameters){{"Tampered","true"}};parameters["TitleId"]="0004000000000000";
            var wrongMetadata=new EditorProposal(preview.Owner,preview.Action,preview.Seed,preview.Files,preview.Changes,parameters,preview.Dependencies);
            reject(()=>service.Apply(wrongMetadata),"Gen6PersistentItems_ModifiedProposalMetadataRejected_"+tag);
            var alteredDependencies=preview.Dependencies.Select(d=>(d.Path,Fingerprint:(byte[])d.Fingerprint.Clone())).ToArray();alteredDependencies[0].Fingerprint[0]^=1;
            var wrongDependencies=new EditorProposal(preview.Owner,preview.Action,preview.Seed,preview.Files,preview.Changes,preview.Parameters,alteredDependencies);
            reject(()=>service.Apply(wrongDependencies),"Gen6PersistentItems_ModifiedDependencySnapshotRejected_"+tag);
            check(File.ReadAllBytes(path).SequenceEqual(source)&&f.Session.History.Read().Entries.Count==0,"Gen6PersistentItems_AllProposalRejectionsWriteNothing_"+tag);
            reject(()=>service.Preview(Enable(true),[1,1]),"Gen6PersistentItems_DuplicateScopeRejected_"+tag);
            reject(()=>service.Preview(new Dictionary<string,int>{{"Enable",2}},[1]),"Gen6PersistentItems_InvalidEnableRejected_"+tag);
            service.Apply(preview);byte[] installed=File.ReadAllBytes(path);var entry=f.Session.History.Read().Entries.Last();
            check(State(service)==1&&entry.Files.Count==1&&entry.Parameters["TitleId"]==profile.TitleId.ToString("X16")
                &&entry.Parameters["ActionId"]==GameGen6PersistentItemsService.ActionId&&ExactName(f),
                "Gen6PersistentItems_InstalledAndJournaledWithExactTitleAndPhysicalName_"+tag);
            service.Apply(service.Preview(Enable(true),[1]));
            check(f.Session.History.Read().Entries.Count==1&&File.ReadAllBytes(path).SequenceEqual(installed),"Gen6PersistentItems_IdempotentServiceDoesNotCreateHistory_"+tag);
            reject(()=>service.Apply(preview),"Gen6PersistentItems_StaleAlreadyAppliedPreviewRejected_"+tag);
            var reopened=Fixture.CreateSession(f.Game,f.Root,f.PersonalPath);
            check(State(new GameGen6PersistentItemsService(reopened,profile))==1,"Gen6PersistentItems_ReopenedSessionRecognizesInstalledBytes_"+tag);
            RestorePoints.Undo(f.Session.History,f.Session.ProjectRoot,entry.Id);
            check(File.ReadAllBytes(path).SequenceEqual(source)&&State(new GameGen6PersistentItemsService(f.Session,profile))==0&&ExactName(f),
                "Gen6PersistentItems_HistoryUndoRestoresExactOriginal_"+tag);
            var fresh=new GameGen6PersistentItemsService(f.Session,profile);fresh.Save(1,Enable(true));fresh.Save(1,Enable(false));
            check(File.ReadAllBytes(path).SequenceEqual(source)&&beforeFiles.All(p=>Hash(File.ReadAllBytes(PathFor(f,p.Key)))==p.Value),
                "Gen6PersistentItems_ExplicitRemovalPreservesAllOtherDependencies_"+tag);
        }
        using(var f=new Fixture(GameVersion.XY))
        {
            var profile=Setup(f,false);string path=PathFor(f,"ExtractedRomFS/DllBattle.cro");byte[] source=File.ReadAllBytes(path);
            foreach(string key in profile.Pins.Keys)
            {
                var service=new GameGen6PersistentItemsService(f.Session,profile);var preview=service.Preview(Enable(true),[1]);
                string changedPath=PathFor(f,key);byte[] before=File.ReadAllBytes(changedPath),external=(byte[])before.Clone();external[^1]^=1;File.WriteAllBytes(changedPath,external);
                reject(()=>service.Apply(preview),"Gen6PersistentItems_DependencyRaceRejected_"+key.Replace('/','_'));
                check(File.ReadAllBytes(changedPath).SequenceEqual(external)&&f.Session.History.Read().Entries.Count==0
                    &&(changedPath==path||File.ReadAllBytes(path).SequenceEqual(source)),"Gen6PersistentItems_RacePreservesExternalEdit_"+key.Replace('/','_'));
                File.WriteAllBytes(changedPath,before);
            }
            File.Move(path,Path.Combine(f.Game.RomFS,"DLLBattle.cro"));
            check(!GameGen6PersistentItemsService.Inspect(f.Session,profile).Allowed,"Gen6PersistentItems_WrongPhysicalDllBattleSpellingRejected");
        }
    }

    private static void RunCoexistence(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(string companion in new[]{"TypeChart","KingShield"})foreach(bool itemsFirst in new[]{false,true})
        {
            using var f=new Fixture(GameVersion.XY);var profile=Setup(f,false);string path=PathFor(f,"ExtractedRomFS/DllBattle.cro");
            byte[] source=File.ReadAllBytes(path);string name=companion+"_"+(itemsFirst?"ItemsFirst":"CompanionFirst");
            void Items()=>new GameGen6PersistentItemsService(f.Session,profile).Save(1,Enable(true));
            void Companion()
            {
                if(companion=="TypeChart")
                {
                    var chart=new GameTypeChartService(f.Session);var values=chart.Read();values[17]=8;
                    var p=chart.Preview(values,repairCro:!itemsFirst);chart.Apply(p.File,p.Changes);
                }
                else
                {
                    string csv=Path.Combine(f.Root,"persistent-shield.csv");File.WriteAllText(csv,"Move,PP,KingShieldAttackMinusOne\n588,7,true\n");
                    var moves=new GameMoveService(f.Session);var p=moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{TemplatePath=csv,MoveDescriptions=false},[588]);moves.ApplyMovePreview(p);
                }
            }
            if(itemsFirst){Items();Companion();}else{Companion();Items();}
            byte[] both=File.ReadAllBytes(path);var operations=f.Session.History.Read().Entries.ToArray();
            check(Gen6PersistentItemsPatcher.Inspect(both,profile.ShapeHash)==Gen6PersistentItemsPatcher.State.Installed
                &&(companion=="TypeChart"?both[Gen6PersistentItemsPatcher.TypeChart+17]==8:both[Gen6PersistentItemsPatcher.KingShield+4]==0xFF)
                &&CroIntegrity.Rebuild(both).SequenceEqual(both)&&ExactName(f),"Gen6PersistentItems_BothOrdersKeepCompanionAndValidCro_"+name);
            var service=new GameGen6PersistentItemsService(f.Session,profile);var remove=service.Preview(Enable(false),[1]);service.Apply(remove);
            byte[] removed=File.ReadAllBytes(path);
            check(Gen6PersistentItemsPatcher.Inspect(removed,profile.ShapeHash)==Gen6PersistentItemsPatcher.State.Original
                &&(companion=="TypeChart"?removed[Gen6PersistentItemsPatcher.TypeChart+17]==8:removed[Gen6PersistentItemsPatcher.KingShield+4]==0xFF),
                "Gen6PersistentItems_RemovalKeepsCompanionEdits_"+name);
            var removal=f.Session.History.Read().Entries.Last();RestorePoints.Undo(f.Session.History,f.Session.ProjectRoot,removal.Id);
            check(File.ReadAllBytes(path).SequenceEqual(both),"Gen6PersistentItems_UndoRemovalRestoresBothPatches_"+name);
            foreach(var entry in operations.Reverse())RestorePoints.Undo(f.Session.History,f.Session.ProjectRoot,entry.Id);
            check(File.ReadAllBytes(path).SequenceEqual(source)&&ExactName(f),"Gen6PersistentItems_LifoUndoRestoresExactSource_"+name);
        }
    }

    internal static void RunUi(Action<bool,string> check)
    {
        ThreeDS.Progressive.StudioDialog.AutomatedChecks=true;
        foreach(bool y in new[]{false,true})
        {
            using var f=new Fixture(GameVersion.XY);var profile=Setup(f,y);string path=PathFor(f,"ExtractedRomFS/DllBattle.cro"),tag=y?"Y":"X";
            byte[] source=File.ReadAllBytes(path);
            var incompatible=new ThreeDS.Progressive.Gen6PersistentItemsView(f.Session);
            check(incompatible.Status.Text.Contains("Incompatible",StringComparison.Ordinal)&&!incompatible.PreviewInstall.IsEnabled
                &&!incompatible.PreviewRemove.IsEnabled&&!incompatible.ApplyButton.IsEnabled,
                "Gen6PersistentItemsUi_UnknownSyntheticProfileExplainedAndDisabled_"+tag);
            var view=new ThreeDS.Progressive.Gen6PersistentItemsView(f.Session,profile);
            var textPanel=(System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)view.Content).Content;
            check(textPanel.Children.OfType<System.Windows.Controls.TextBlock>().All(t=>Equals(t.Foreground,view.FindResource("TextMain")))
                &&Equals(view.Background,view.FindResource("Bg")),"Gen6PersistentItemsUi_UsesThemeContrastResources_"+tag);
            view.Measure(new System.Windows.Size(640,520));view.Arrange(new System.Windows.Rect(0,0,640,520));view.UpdateLayout();
            check(view.Status.Text.Contains(profile.Name,StringComparison.Ordinal)&&view.Status.Text.Contains("Original compatible",StringComparison.Ordinal)
                &&view.PreviewInstall.IsEnabled&&!view.ApplyButton.IsEnabled&&view.ActualWidth==640,
                "Gen6PersistentItemsUi_RecognizedProfileAndNarrowLayout_"+tag);
            Click(view.PreviewRemove);
            check(!view.ApplyButton.IsEnabled&&File.ReadAllBytes(path).SequenceEqual(source),"Gen6PersistentItemsUi_OriginalRemovalIsReadOnlyNoOp_"+tag);
            Click(view.PreviewInstall);
            check(view.ApplyButton.IsEnabled&&File.ReadAllBytes(path).SequenceEqual(source)&&f.Session.History.Read().Entries.Count==0,
                "Gen6PersistentItemsUi_PreviewRequiredBeforeAnyWrite_"+tag);
            Click(view.ApplyButton);
            check(view.Status.Text.Contains("Instalado",StringComparison.Ordinal)&&!view.ApplyButton.IsEnabled
                &&f.Session.History.Read().Entries.Count==1&&ExactName(f),"Gen6PersistentItemsUi_ConfirmedApplyWritesAndRefreshesStatus_"+tag);
            Click(view.PreviewInstall);
            check(!view.ApplyButton.IsEnabled,"Gen6PersistentItemsUi_IdempotentPreviewDoesNotEnableApply_"+tag);
            Click(view.PreviewRemove);check(view.ApplyButton.IsEnabled,"Gen6PersistentItemsUi_RemovalHasExplicitPreview_"+tag);
            Click(view.ApplyButton);
            check(view.Status.Text.Contains("Original compatible",StringComparison.Ordinal)&&File.ReadAllBytes(path).SequenceEqual(source)
                &&f.Session.History.Read().Entries.Count==2,"Gen6PersistentItemsUi_RemovalRestoresExactBytesAndRecordsHistory_"+tag);
            Click(view.PreviewInstall);
            string code=PathFor(f,"ExtractedExeFS/.code.bin");byte[] external=File.ReadAllBytes(code);external[^1]^=1;File.WriteAllBytes(code,external);
            Click(view.ApplyButton);
            check(!view.ApplyButton.IsEnabled&&!view.PreviewInstall.IsEnabled&&view.Status.Text.Contains("Incompatible",StringComparison.Ordinal)
                &&File.ReadAllBytes(path).SequenceEqual(source)&&File.ReadAllBytes(code).SequenceEqual(external)
                &&f.Session.History.Read().Entries.Count==2,"Gen6PersistentItemsUi_StaleDependencyBlocksApplyAndExplainsReason_"+tag);
        }
        static void Click(System.Windows.Controls.Button button)=>button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    }
}
