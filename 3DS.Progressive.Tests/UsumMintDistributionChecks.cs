using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class UsumMintDistributionChecks
{
    private static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    // Entirely synthetic CRO: real segment/relocation structure, invented inventories.
    private static byte[] Shop()
    {
        byte[] b=new byte[0x8000];void U(int at,uint v)=>BitConverter.GetBytes(v).CopyTo(b,at);
        "CRO0"u8.CopyTo(b.AsSpan(0x80));U(0x90,(uint)b.Length);
        U(0xb0,0x200);U(0xb4,0x3e00);U(0xb8,0x6000);U(0xbc,0x1000);U(0xc0,0x4000);U(0xc4,0x2000);
        U(0xc8,0x7000);U(0xcc,4);U(0x128,0x7100);
        U(0x7000,0x200);U(0x7004,0x3e00);U(0x7008,0);U(0x700c,0x4000);U(0x7010,0x2000);U(0x7014,1);
        U(0x7018,0x6000);U(0x701c,0x1000);U(0x7020,2);U(0x702c,3);
        int relocation=0;
        void Ref(int target){int at=0x7100+12*relocation;U(at,(uint)((0x100+4*relocation)<<4));U(at+4,0x102);U(at+8,(uint)(target-0x4000));relocation++;}
        int counts=0x4100;int[] tutors=[2,3,4,5],bp=[6,7,8,9,10,11,12];
        for(int i=0;i<4;i++)b[counts+i]=(byte)tutors[i];for(int i=0;i<7;i++)b[counts+4+i]=(byte)bp[i];
        b.AsSpan(counts+11,28).Fill(2);b[counts+39]=0xff;
        Ref(counts);Ref(counts+4);Ref(counts+11);
        for(int i=0;i<28;i++){Ref(0x4200+4*i);BitConverter.GetBytes((ushort)48).CopyTo(b,0x4200+4*i);BitConverter.GetBytes((ushort)17).CopyTo(b,0x4202+4*i);}
        void Group(int start,int[] sizes){int cursor=start;foreach(int size in sizes){Ref(cursor);for(int i=0;i<size;i++){BitConverter.GetBytes((ushort)18).CopyTo(b,cursor);BitConverter.GetBytes((ushort)12).CopyTo(b,cursor+2);cursor+=4;}}}
        Group(0x4800,bp);Group(0x4c00,tutors);U(0x12c,(uint)relocation);return CroIntegrity.Rebuild(b);
    }
    private static void Pickup(Fixture f)
    {
        byte[] table=new byte[4+12*64];BitConverter.GetBytes(65).CopyTo(table,0);
        for(int i=0;i<64;i++){BitConverter.GetBytes((ushort)17).CopyTo(table,4+12*i);table.AsSpan(6+12*i,10).Fill((byte)(i==0?100:0));}
        f.WriteGarc(ModuleChecks.PathFor(f,"pickup"),[table,new byte[]{9,8,7}],f.Game.GetGARCReference("pickup"));
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        int[] ids=[113,114,115,123,124,125,126,127,128,129,130,131,132,133,426,427,622,837,838,839,840];
        check(UsumMintItemPolicy.Ids.SetEquals(ids),"MintDistributionExact21Ids");
        using(var sm=new Fixture(GameVersion.SM))
        {
            check(!sm.Session.MintItems.Inspect().Available,"MintDistributionRejectSM");
            reject(()=>sm.Session.MintItems.Pool([17],true),"MintDistributionSMOptInRejected");
        }
        foreach(bool moon in new[]{false,true})Lifecycle(check,reject,moon,ids);
        CodeProfile(check,reject);
    }
    private static void Lifecycle(Action<bool,string> check,Action<Action,string> reject,bool moon,int[] ids)
    {
        string tag=moon?"UM":"US";
        using var f=new Fixture(GameVersion.USUM);var profile=UsumNatureMintChecks.Setup(f,moon);Pickup(f);
        UsumNatureMintProfile Identity(byte[] code)=>Hash(code)==profile.CodeSha256?profile:throw new InvalidDataException("Unknown synthetic executable");
        f.Session.MintItems=new(f.Session,Identity);
        string shopPath=Path.Combine(f.Game.RomFS,"Shop.cro"),bagPath=Path.Combine(f.Game.RomFS,"Bag.cro"),crrPath=Path.Combine(f.Game.RomFS,".crr/static.crr");
        byte[] shop=Shop();check(Gen7ShopCroLayout.TryRead(shop,out _,out _),"MintDistributionSyntheticShopLayout"+tag);File.WriteAllBytes(shopPath,shop);
        byte[] crr=File.ReadAllBytes(crrPath);BitConverter.GetBytes(2).CopyTo(crr,0x354);File.WriteAllBytes(crrPath,CroIntegrity.RebuildCrr(crr,[File.ReadAllBytes(bagPath),shop]));
        check(!f.Session.MintItems.Include&&f.Session.MintItems.Pool([17,17,113,48],false).SequenceEqual(new[]{17,17,48}),"MintDistributionOffRetainsOrderWeightsExcludesMints"+tag);
        check(f.Session.MintItems.Pool(GameMartService.ProgressiveCatalog,false).SequenceEqual(GameMartService.ProgressiveCatalog),"MintDistributionLegacyMartPoolUnchanged"+tag);
        reject(()=>f.Session.MintItems.Pool([17],true),"MintDistributionMissingPatchBlocked"+tag);
        var phase1=new UsumNatureMintService(f.Session,Identity);phase1.Apply(phase1.Inspect());
        check(f.Session.MintItems.Inspect().Available&&f.Session.MintItems.Inspect().Report.Contains("Advertencia visual"),"MintDistributionIconsOptional"+tag);
        int[] pool=f.Session.MintItems.Pool([17,17,113],true);
        check(pool.Distinct().Count()==22&&ids.All(pool.Contains),"MintDistributionPoolUniqueAndComplete"+tag);
        check(!f.Session.MintItems.Pool([17],true,id=>id!=113).Contains(113),"MintDistributionBanAppliesToMints"+tag);
        var legacy=new GamePickupService(f.Session);var rows=legacy.Read();
        int[] oldPool=ThreeDS.Progressive.Services.ProgressiveLegacy.Randomizer.GetRandomItemList(f.Game).Where(i=>i>0&&i<legacy.Items.Count).Distinct().ToArray();
        new Random(971).Shuffle(oldPool);
        check(legacy.Randomize(rows,971).Select(r=>r.Item).SequenceEqual(oldPool.Take(rows.Count)),"MintDistributionLegacyPickupSeedUnchanged"+tag);
        f.Session.MintItems.Include=true;
        var pickup=new GamePickupService(f.Session);var a=pickup.Randomize(rows,33);var b=pickup.Randomize(rows,33);
        check(a.Select(r=>r.Item).SequenceEqual(b.Select(r=>r.Item))&&a.Where((r,i)=>!r.Rates.SequenceEqual(rows[i].Rates)).Count()==0,"MintDistributionPickupDeterministicRates"+tag);
        var injected=rows.Select(r=>r.Copy()).ToList();for(int i=0;i<21;i++)injected[i].Item=ids[i];
        var pending=pickup.Preview(injected);var bag=File.ReadAllBytes(bagPath);byte[] foreign=(byte[])bag.Clone();foreign[^1]^=1;File.WriteAllBytes(bagPath,foreign);
        byte[] pickupBefore=File.ReadAllBytes(ModuleChecks.PathFor(f,"pickup"));
        reject(()=>pickup.Apply(pending),"MintDistributionChangedDependencyBlocksWrite"+tag);
        check(File.ReadAllBytes(ModuleChecks.PathFor(f,"pickup")).SequenceEqual(pickupBefore),"MintDistributionNoPartialWrite"+tag);File.WriteAllBytes(bagPath,bag);
        string memoryPath=Path.Combine(f.Root,"exheader.bin");byte[] memory=File.ReadAllBytes(memoryPath),changedMemory=(byte[])memory.Clone();changedMemory[0x20]^=1;File.WriteAllBytes(memoryPath,changedMemory);
        reject(()=>pickup.Apply(pending),"MintDistributionChangedMemoryMapBlocksWrite"+tag);
        check(File.ReadAllBytes(ModuleChecks.PathFor(f,"pickup")).SequenceEqual(pickupBefore),"MintDistributionMemoryConflictNoPartialWrite"+tag);File.WriteAllBytes(memoryPath,memory);
        using(var writer=new FileStream(bagPath,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete))reject(()=>pickup.Apply(pending),"MintDistributionConcurrentDependencyWriterRejected"+tag);
        pickup.Apply(pending);check(new GamePickupService(f.Session).Read().Take(21).Select(r=>r.Item).SequenceEqual(ids),"MintDistributionPickupPersists"+tag);
        RestorePoints.Undo(f.Session.History,f.Root,f.Session.History.Read().Entries.Last().Id);
        check(File.ReadAllBytes(ModuleChecks.PathFor(f,"pickup")).SequenceEqual(pickupBefore),"MintDistributionPickupUndoExact"+tag);
        var marts=new GameMartService(f.Session);var bpBefore=marts.BpShops.SelectMany(s=>s.Rows).Select(r=>(r.ItemId,r.Price)).ToArray();
        check(marts.AddNatureMints()==168&&marts.AddNatureMints()==0,"MintDistributionAllEightStocksIdempotent"+tag);
        var preview=marts.Prepare();
        FileChange[] pendingShop=[new(shopPath,shop,preview.Output),..(preview.ExtraFiles??[]).Where(file=>file.Changed)];
        reject(()=>FileTransaction.Commit(pendingShop,i=>{if(i==1)throw new IOException("Injected distribution transaction failure");}),"MintDistributionRollbackInjectedFailure"+tag);
        check(pendingShop.All(file=>File.ReadAllBytes(file.Path).SequenceEqual(file.Original())),"MintDistributionRollbackRestoresShopAndCRR"+tag);
        marts.Apply(preview);var reopened=new GameMartService(f.Session);
        var reopenedSession=Fixture.CreateSession(f.Game,f.Root,f.PersonalPath);
        check(reopenedSession.MintItems.Include,"MintDistributionHistoryRemembersExplicitInclusion"+tag);
        var recovered=GlobalTemplateDocument.FromHistory(f.Session.History.Read(),"USUM",7,null);
        check(recovered.Actions.Contains(UsumMintItemPolicy.AddGeneralAction)&&!recovered.Actions.Contains("usum.nature-mints.install")&&UsumMintItemPolicy.Read(recovered)&&NativeRecipeCompletion.TryComplete(f.Session,recovered),"MintDistributionHistoryRecoversExecutableShopRecipe"+tag);
        check(reopened.RegularShops.Take(8).All(s=>s.Rows.Take(2).Select(r=>r.ItemId).SequenceEqual(new[]{48,17})&&ids.All(id=>s.Rows.Count(r=>r.ItemId==id)==1)),"MintDistributionStoresPreserveExistingAndAll21"+tag);
        check(reopened.BpShops.SelectMany(s=>s.Rows).Select(r=>(r.ItemId,r.Price)).SequenceEqual(bpBefore),"MintDistributionBPPricesUntouched"+tag);
        RestorePoints.Undo(f.Session.History,f.Root,f.Session.History.Read().Entries.Last().Id);
        check(File.ReadAllBytes(shopPath).SequenceEqual(shop),"MintDistributionShopUndoExact"+tag);
        var crowded=new GameMartService(f.Session);
        while(crowded.RegularShops[0].Rows.Count<107)crowded.RegularShops[0].Rows.Add(new(crowded.RegularShops[0].Rows.Count+1,17));
        reject(()=>crowded.AddNatureMints(),"MintDistributionCapacityRejectsBeforeAnyStockChanged"+tag);
        check(crowded.RegularShops[0].Rows.Count==107&&crowded.RegularShops.Skip(1).All(s=>s.Rows.Count==2)&&File.ReadAllBytes(shopPath).SequenceEqual(shop),"MintDistributionCapacityFailureIsAtomic"+tag);
        // The real Batch workspace copier must carry identity, before stage preflight.
        string stage=Path.Combine(f.Root,"stage-test"),exheader=Path.Combine(f.Root,"exheader.bin");
        // Setup already wrote the accredited US/UM memory map. Keep it valid for
        // the distribution checks which continue using this same project.
        byte[] sourceExheader=File.ReadAllBytes(exheader);
        var assembly=System.Reflection.Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));
        assembly.GetType("pk3DS.WinForms.BatchWorkspace",true)!.GetMethod("PrepareCleanCopy",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[f.Root,stage,f.Game,exheader,false]);
        check(File.ReadAllBytes(Path.Combine(stage,"HeaderNCCH0.bin")).SequenceEqual(File.ReadAllBytes(Path.Combine(f.Root,"HeaderNCCH0.bin"))),"MintDistributionBatchStagePreservesNCCH"+tag);
        check(File.ReadAllBytes(exheader).SequenceEqual(sourceExheader)&&File.ReadAllBytes(Path.Combine(stage,"exheader.bin")).SequenceEqual(sourceExheader),"MintDistributionBatchStagePreservesMemoryMap"+tag);
        var field=new GameFieldItemsService(f.Session);int[] scope=field.Entries.Select(e=>e.Id).ToArray();var opts=EditorValues.Flatten(field.RandomOptions);
        var fieldPreview=field.Preview(opts,scope,23);check(fieldPreview.Files[0].Proposed().SequenceEqual(field.Preview(opts,scope,23).Files[0].Proposed()),"MintDistributionFieldDeterministic"+tag);
        byte[] fieldBefore=File.ReadAllBytes(ModuleChecks.PathFor(f,"encdata"));
        foreach(int mint in ids)
        {
            var optionNode=JsonNode.Parse(field.CaptureOptions(opts))!;optionNode["AllowedItems"]=new JsonArray(mint);field.RestoreOptions(optionNode.ToJsonString());
            var onlyMint=field.Preview(opts,scope,41);
            check(onlyMint.Changes.Count==scope.Length&&onlyMint.Changes.All(c=>c.After==f.Session.ItemNames[mint]),"MintDistributionFieldWhitelistMint"+mint+tag);
        }
        field.Apply(field.Preview(opts,scope,41));
        check(new GameFieldItemsService(f.Session).Entries.All(e=>new GameFieldItemsService(f.Session).Read(e.Id).SelectMany(g=>g.Fields).Single(v=>v.Key=="Item").Value==840),"MintDistributionFieldSaveReopen"+tag);
        RestorePoints.Undo(f.Session.History,f.Root,f.Session.History.Read().Entries.Last().Id);
        check(File.ReadAllBytes(ModuleChecks.PathFor(f,"encdata")).SequenceEqual(fieldBefore),"MintDistributionFieldUndoExact"+tag);
        var document=NativeRecipeFactory.Create(f.Session,modules:["FieldItems","Marts"]);
        NativeGen7MartBridge.Capture(document,new(RandomRegular:true,AddMints:true));
        var native=document.Section("ProgressiveApp")!;native[UsumMintItemPolicy.Node]!["Future"]="preserved";document.SetSection("ProgressiveApp",native);
        string recipe=Path.Combine(f.Root,"mint-recipe.json");document.Save(recipe);document=GlobalTemplateDocument.Load(recipe,"USUM",7);
        check(UsumMintItemPolicy.Read(document)&&NativeGen7MartBridge.Read(document)!.AddMints&&document.Section("ProgressiveApp")![UsumMintItemPolicy.Node]!["Future"]!.GetValue<string>()=="preserved","MintDistributionTemplateRoundtripUnknownNode"+tag);
        check(NativeRecipeCompletion.TryComplete(f.Session,document)&&BatchRomExecution.Supports(UsumMintItemPolicy.AddGeneralAction),"MintDistributionBatchNativeRecipeComplete"+tag);
        var old=GlobalTemplateDocument.Empty("USUM",7);NativeGen7MartBridge.Capture(old,new(RandomRegular:true));
        check(!UsumMintItemPolicy.Read(old)&&NativeGen7MartBridge.Read(old) is {AddMints:false},"MintDistributionLegacyRecipeDefaultFalse"+tag);
        var forged=GlobalTemplateDocument.Empty("USUM",7);forged.SetSection("Actions",new JsonArray(new JsonObject{["Id"]=UsumMintItemPolicy.AddGeneralAction,["Parameters"]=new JsonObject()}));
        reject(()=>UsumMintItemPolicy.ValidateDocument(forged),"MintDistributionNoLegacyFallbackForMintDistribution"+tag);
        var expected=new GameMartService(f.Session){MintInclusion=true};var expectedShop=NativeGen7MartBridge.Prepare(expected,NativeGen7MartBridge.Read(document)!,91).Output;
        NativeGen7MartBridge.ExecuteSession(f.Session,document.ToJson(),91);
        check(File.ReadAllBytes(shopPath).SequenceEqual(expectedShop),"MintDistributionBatchShopReplayExact"+tag);
        var manualShop=new GameMartService(f.Session);manualShop.RegularShops[0].Rows.Clear();manualShop.RegularShops[0].Rows.Add(new(1,30));manualShop.Apply(manualShop.Prepare());
        NativeGen7MartBridge.FinalizeMintsSession(f.Session,document);var finalShop=new GameMartService(f.Session);
        check(finalShop.RegularShops[0].Rows[0].ItemId==30&&finalShop.RegularShops.Take(8).All(s=>ids.All(id=>s.Rows.Any(r=>r.ItemId==id))),"MintDistributionManualOverridesKeepRowsAndAll21"+tag);
        byte[] finalBytes=File.ReadAllBytes(shopPath);int historyCount=f.Session.History.Read().Entries.Count;
        NativeGen7MartBridge.FinalizeMintsSession(f.Session,document);
        check(File.ReadAllBytes(shopPath).SequenceEqual(finalBytes)&&f.Session.History.Read().Entries.Count==historyCount,"MintDistributionFinalizationIdempotent"+tag);
        // A dependency must fail before any source gets written; no fallback to vanilla.
        File.WriteAllBytes(bagPath,foreign);var snapshot=Directory.GetFiles(f.Game.RomFS,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Hash(File.ReadAllBytes(p)));
        reject(()=>UsumMintItemPolicy.ValidateSession(f.Session,document),"MintDistributionPartialPatchPreflightRejected"+tag);
        check(snapshot.All(p=>Hash(File.ReadAllBytes(p.Key))==p.Value),"MintDistributionPreflightReadOnly"+tag);
    }
    private static void CodeProfile(Action<bool,string> check,Action<Action,string> reject)
    {
        byte[] code=new byte[5914624];for(int i=0;i<100;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(code,UsumMintCodeDataProfile.TmOffset+i*2);
        new byte[]{0x23,0,0xd4,0xe5}.CopyTo(code,UsumMintCodeDataProfile.PidOffset);code[UsumMintCodeDataProfile.AlwaysShinyOffset]=0x0a;
        new byte[]{20,35,50,65,80,100}.CopyTo(code,UsumMintCodeDataProfile.ObedienceOffset);
        BitConverter.GetBytes(0x5c4644u).CopyTo(code,0x21bd64);
        byte[] normalized=UsumMintCodeDataProfile.Normalize(code);code[UsumMintCodeDataProfile.TmOffset]=99;code[UsumMintCodeDataProfile.AlwaysShinyOffset]=0xea;
        check(UsumMintCodeDataProfile.Normalize(code).SequenceEqual(normalized),"MintDistributionCodeOnlyKnownDataNormalized");
        code[0x100]^=1;reject(()=>UsumNatureMintCatalog.IdentifyCode(code),"MintDistributionUnknownExecutableStillRejected");
        code[UsumMintCodeDataProfile.TmOffset]=0;code[UsumMintCodeDataProfile.TmOffset+1]=0;reject(()=>UsumMintCodeDataProfile.Normalize(code),"MintDistributionInvalidTMRejected");
    }
}
