using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;

internal static class SinglePurchaseChecks
{
    internal static void Schema(Action<bool,string> check,Action<Action,string> reject)
    {
        var doc=GlobalTemplateDocument.Empty("USUM",7);NativeGen7MartBridge.Capture(doc,new(SinglePurchase:true));
        check(NativeGen7MartBridge.Read(doc) is {SinglePurchase:true}&&doc.Actions.SequenceEqual(new[]{GameMartService.SinglePurchaseAction}),"SinglePurchase_GlobalRoundtripStoresPolicyWithoutItemIds");
        var legacy=GlobalTemplateDocument.Empty("USUM",7);NativeGen7MartBridge.Capture(legacy,new(RandomRegular:true));var native=legacy.Section("ProgressiveApp")!;native["Gen7Marts"]!["Options"]!.AsObject().Remove(GameMartService.SinglePurchaseAction);legacy.SetSection("ProgressiveApp",native);
        check(NativeGen7MartBridge.Read(legacy) is {SinglePurchase:false,RandomRegular:true},"SinglePurchase_LegacyEightOptionSnapshotRemainsCompatible");
        doc.SelectActions(new HashSet<string>());NativeRecipeCompletion.ReconcileSelection(doc);NativeGen7MartBridge.ValidateDocument(doc);
        check(NativeGen7MartBridge.Read(doc) is {SinglePurchase:false},"SinglePurchase_ActionDeselectionReconcilesSnapshot");
        foreach(var game in new[]{"SM","XY","ORAS"})reject(()=>NativeGen7MartBridge.Capture(GlobalTemplateDocument.Empty(game,game=="SM"?7:6),new(SinglePurchase:true)),"SinglePurchase_RejectsUnsupportedFamily_"+game);
    }
    internal static async Task Backend(string root,Action<bool,string> check)
    {
        var source=Path.Combine(root,"backend-source");var project=GameProjectSession.Open(source,recoverInterruptedSave:false);
        var paths=Directory.GetFiles(project.GameConfiguration.RomFS,"*",SearchOption.AllDirectories).Concat(Directory.GetFiles(project.GameConfiguration.ExeFS,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,p=>ProjectHistory.Hash(File.ReadAllBytes(p)));
        var app=new GlobalTemplateApplication(project);
        var only=GlobalTemplateDocument.Empty("USUM",7);only.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});NativeGen7MartBridge.Capture(only,new(SinglePurchase:true));NativeRecipeCompletion.TryComplete(project,only);
        using(var preview=await app.PreviewAsync(only))
        {
            string shop=Path.Combine(preview.Workspace,"preview-project","RomFS","Shop.cro");
            if(!File.Exists(shop))shop=Directory.GetFiles(preview.Workspace,"Shop.cro",SearchOption.AllDirectories).Single();
            check(Gen7SinglePurchaseStock.Read(File.ReadAllBytes(shop)) is {} stock&&Gen7SinglePurchaseStock.CurrentOffers(File.ReadAllBytes(shop)).All(stock.Offers.Contains),"SinglePurchase_OriginalBackendAppliesPolicyOnlyRecipe");
        }
        var mixed=GlobalTemplateDocument.Empty("USUM",7);mixed.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});NativeGen7MartBridge.Capture(mixed,new(RandomRegular:true,SpecialsOnly:true,ProtectX:false,SinglePurchase:true));
        var manual=JsonNode.Parse("""{"Version":2,"Game":"USUM","Generation":7,"Entries":[],"Operations":[{"Kind":"shops7","Key":"shops7@test","Language":2,"Payload":{"Shops":[{"Index":8,"IsBP":false,"Rows":[{"ItemId":17,"Price":0}]}],"CandyPrice":false,"KeyPrice":false,"Vitamin252":false,"VitaminIds":[]}}]}""")!;
        var native=mixed.Section("ProgressiveApp")!.AsObject();native["ManualEdits"]=manual;mixed.SetSection("ProgressiveApp",native);var actions=mixed.Section("Actions")!.AsArray();actions.Add(new JsonObject{["Id"]="manual-edits.apply",["Parameters"]=new JsonObject()});mixed.SetSection("Actions",actions);NativeRecipeCompletion.TryComplete(project,mixed);
        using(var preview=await app.PreviewAsync(mixed))
        {
            string shop=Directory.GetFiles(preview.Workspace,"Shop.cro",SearchOption.AllDirectories).Single();byte[] bytes=File.ReadAllBytes(shop);var stock=Gen7SinglePurchaseStock.Read(bytes)!;Gen7ShopCroLayout.TryRead(bytes,out var layout,out _);
            check(layout.RegularMarts.Inventories[8].SequenceEqual(new ushort[]{17})&&Gen7SinglePurchaseStock.CurrentOffers(bytes).All(stock.Offers.Contains),"SinglePurchase_OriginalBackendCoversFinalManualOverridesAfterRandomization");
            string stagedRoot=Directory.GetParent(Path.GetDirectoryName(shop)!)!.FullName;
            var history=new ProjectHistory(stagedRoot,"USUM").Read();
            check(history.Entries.Any(e=>e.Parameters.TryGetValue("ManualOperation",out var op)&&JsonNode.Parse(op)?["Payload"]?["Shops"] is JsonArray rows&&rows.Count>0&&JsonNode.Parse(op)?["Payload"]?["SinglePurchase"]?.GetValue<bool>()==true),"SinglePurchase_BackendEditableHistoryPreservesManualRowsAndPolicyTogether");
        }
        check(paths.All(p=>ProjectHistory.Hash(File.ReadAllBytes(p.Key))==p.Value),"SinglePurchase_OriginalBackendPreviewNeverModifiesSource");
    }
    static byte[] Part(byte[] bytes,Gen7ShopGroupedTableSnapshot table)=>bytes.AsSpan((int)table.DataStart,table.DataLength).ToArray();
    internal static void CopyModules(Fixture f,string rom)
    {
        foreach(string file in Directory.GetFiles(rom,"*.cro"))File.Copy(file,Path.Combine(f.Game.RomFS,Path.GetFileName(file)),true);
        Directory.CreateDirectory(Path.Combine(f.Game.RomFS,".crr"));File.Copy(Path.Combine(rom,".crr","static.crr"),Path.Combine(f.Game.RomFS,".crr","static.crr"),true);
        var reference=f.Game.GetGARCReference("gametext");
        for(int i=0;i<10;i++){string relative=reference.GetRelativeGARC(i).Reference;string path=Path.Combine(f.Game.RomFS,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(Path.Combine(rom,relative),path,true);}
    }
    internal static void RealCopies(string rom,string output,Action<bool,string> check,Action<Action,string> reject)
    {
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Output must be new");Directory.CreateDirectory(output);
        var sourceFiles=Directory.GetFiles(rom,"*.cro").Concat(new[]{Path.Combine(rom,".crr","static.crr")}).Concat(Enumerable.Range(0,10).Select(i=>Path.Combine(rom,GARCReference.GARCReference_US.First(r=>r.Name=="gametext").GetRelativeGARC(i).Reference))).ToDictionary(p=>p,p=>ProjectHistory.Hash(File.ReadAllBytes(p)));
        byte[] source=File.ReadAllBytes(Path.Combine(rom,"Shop.cro")),original=(byte[])source.Clone();
        byte[] patched=Gen7SinglePurchaseStock.Apply(source,167);var stock=Gen7SinglePurchaseStock.Read(patched)!;
        Gen7ShopCroLayout.TryRead(source,out var before,out _);Gen7ShopCroLayout.TryRead(patched,out var after,out _);
        var expected=before.RegularMarts.Inventories.SelectMany((items,shop)=>shop>=8?items.Where(x=>x!=0).Select(x=>new SinglePurchaseOffer((ushort)shop,x)):[]).Distinct().OrderBy(x=>x.Shop).ThenBy(x=>x.Item).ToArray();
        check(stock.Offers.SequenceEqual(expected.Concat(new[]{new SinglePurchaseOffer(10,101),new SinglePurchaseOffer(11,101),new SinglePurchaseOffer(10,102)}).Distinct())&&stock.Offers.All(o=>o.Shop is >=8 and <28),"SinglePurchase_CoversEverySpecialShopItemAndExcludesProgression");
        check(Part(source,before.BPItems).SequenceEqual(Part(patched,after.BPItems))&&Part(source,before.BPTutors).SequenceEqual(Part(patched,after.BPTutors)),"SinglePurchase_PreservesEveryBpItemPriceAndTutor");
        check(before.RegularMarts.Inventories.Select((items,i)=>items.SequenceEqual(after.RegularMarts.Inventories[i])).All(x=>x),"SinglePurchase_PatchPreservesAllExistingInventories");
        check(Gen7SinglePurchaseStock.Apply(patched,167).SequenceEqual(patched)&&source.SequenceEqual(original),"SinglePurchase_InstallIsIdempotentAndSourceIsImmutable");
        check(CroIntegrity.Rebuild(patched).SequenceEqual(patched)&&stock.Location+stock.AllocationBytes<=BitConverter.ToUInt32(patched,(int)BitConverter.ToUInt32(patched,0xc8))+BitConverter.ToUInt32(patched,(int)BitConverter.ToUInt32(patched,0xc8)+4),"SinglePurchase_HashesAndDeclaredExecutableAllocationValid");
        var inventories=after.RegularMarts.Inventories.Select(x=>x.ToArray()).ToArray();ushort added=Enumerable.Range(1,900).Select(x=>(ushort)x).First(x=>!stock.Offers.Contains(new SinglePurchaseOffer(10,x)));inventories[10]=inventories[10].Append(added).ToArray();
        check(Gen7ExpandedMartTable.TryRebuild(patched,inventories,out var extended,out _,out _),"SinglePurchase_ExistingMartBuilderCanExtendPatchedShop");var appended=Gen7SinglePurchaseStock.Read(extended)!;
        check(appended.Identity==stock.Identity&&appended.Offers.Take(stock.Offers.Count).SequenceEqual(stock.Offers)&&appended.Offers.Last()==new SinglePurchaseOffer(10,added),"SinglePurchase_NewItemAppendsWithoutReassigningPurchasedBits");
        inventories[10]=inventories[10].Reverse().Concat(new[]{inventories[10][0]}).ToArray();
        check(Gen7ExpandedMartTable.TryRebuild(extended,inventories,out var reordered,out _,out _)&&Gen7SinglePurchaseStock.Read(reordered)!.Offers.SequenceEqual(appended.Offers),"SinglePurchase_ReorderAndDuplicateRowsKeepSameStock");
        var tampered=(byte[])patched.Clone();tampered[stock.Location+0x820]^=1;reject(()=>Gen7SinglePurchaseStock.Read(tampered),"SinglePurchase_RejectsCorruptOwnedManifest");
        tampered=(byte[])source.Clone();tampered[0x200]^=1;reject(()=>Gen7SinglePurchaseStock.Apply(tampered,167),"SinglePurchase_RejectsUnknownExecutableWithoutPatching");
        var crowded=before.RegularMarts.Inventories.Select((x,i)=>i<8?x:Enumerable.Range(1,127).Select(n=>(ushort)n).ToArray()).ToArray();
        check(Gen7ExpandedMartTable.TryRebuild(source,crowded,out var full,out _,out _),"SinglePurchase_CapacityFixtureHasAll127SlotsInEverySpecialShop");reject(()=>Gen7SinglePurchaseStock.Apply(full,167),"SinglePurchase_CapacityFailureNeverSilentlyOmitsItems");
        File.WriteAllBytes(Path.Combine(output,"original-Shop.cro"),source);File.WriteAllBytes(Path.Combine(output,"integrated-Shop.cro"),patched);
        File.WriteAllText(Path.Combine(output,"integrated-registry.json"),JsonSerializer.Serialize(stock));

        using var fixture=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(fixture);GlobalApplicationChecks.PrepareOpenable(fixture);CopyModules(fixture,rom);
        var project=fixture.Session;string shopPath=Path.Combine(fixture.Game.RomFS,"Shop.cro");
        var editor=new GameMartService(project);editor.SinglePurchaseSpecials();var preview=editor.Prepare();
        check(File.ReadAllBytes(shopPath).SequenceEqual(source)&&project.History.Read().Entries.Count==0&&preview.ExtraFiles.Any(f=>f.Changed),"SinglePurchase_PreviewWritesNeitherRomNorHistory");
        var textChanges=preview.ExtraFiles.Where(f=>f.Changed&&f.Path.Contains(Path.DirectorySeparatorChar+"a"+Path.DirectorySeparatorChar)).ToArray();
        bool preserved=true;int message=Gen7SinglePurchaseStock.Read(preview.Output)!.MessageId;
        foreach(var file in textChanges)
        {
            var a=new GARC.MemGARC(file.Original()).Files;var b=new GARC.MemGARC(file.Proposed()).Files;
            for(int i=0;i<a.Length;i++)if(i!=95&&!a[i].SequenceEqual(b[i]))preserved=false;
            var oldText=new TextFile(fixture.Game,a[95],fixture.Game.RemapCharacters).Lines;var newText=new TextFile(fixture.Game,b[95],fixture.Game.RemapCharacters).Lines;
            if(!newText.Take(oldText.Length).SequenceEqual(oldText)||newText.Length!=message+1)preserved=false;
        }
        check(textChanges.Length==10&&preserved,"SinglePurchase_AppendsOneMessageInTenLanguagesPreservingEveryOriginalLineAndOtherBank");
        editor.Apply(preview);var history=project.History.Read();var entry=history.Entries.Last();var operation=JsonNode.Parse(entry.Parameters["ManualOperation"])!;
        check(operation["Payload"]!["SinglePurchase"]!.GetValue<bool>()&&operation["Payload"]!["Shops"]!.AsArray().Count==0,"SinglePurchase_HistoryStoresPolicyWithoutFreezingRandomizedItems");
        check(entry.Files.Count==12&&entry.Files.Any(f=>f.Path.EndsWith("static.crr")),"SinglePurchase_ShopTenLanguagesCrrAndHistoryCommitTogether");
        var doc=GlobalTemplateDocument.Empty("USUM",7);doc.MergeHistoryActions(project.History);
        check(doc.Actions.Contains("manual-edits.apply")&&ManualOperations.Read(doc).Single()["Payload"]!["SinglePurchase"]!.GetValue<bool>(),"SinglePurchase_GlobalFromHistoryContainsReplayablePolicy");
        foreach(var file in new[]{new FileChange(shopPath,source,source)}.Concat(preview.ExtraFiles)){File.WriteAllBytes(file.Path,file.Original());}
        ManualEditTemplate.Apply(project,doc);
        check(File.ReadAllBytes(shopPath).SequenceEqual(preview.Output),"SinglePurchase_ManualTemplateReplayReconstructsExactPolicyPatch");
        var installedService=new GameMartService(project);var noOp=installedService.Prepare();
        check(noOp.Changes.Count==0&&noOp.ExtraFiles.All(f=>!f.Changed),"SinglePurchase_ReopenedEditorDoesNotAppendMessageAgain");
        var alteredService=new GameMartService(project);alteredService.RegularShops[11].Rows.Add(new(alteredService.RegularShops[11].Rows.Count+1,added));var altered=alteredService.Prepare();alteredService.Apply(altered);
        check(Gen7SinglePurchaseStock.Read(altered.Output)!.Offers.Contains(new SinglePurchaseOffer(11,added)),"SinglePurchase_ManualShopEditRefreshesInstalledPolicy");
        // Frozen previews must reject any modified dependency without partially replacing Shop.cro.
        var staleService=new GameMartService(project);staleService.RegularShops[10].Rows.Add(new(staleService.RegularShops[10].Rows.Count+1,added));var stale=staleService.Prepare();var textDependency=stale.ExtraFiles.First(f=>f.Path.Contains(Path.DirectorySeparatorChar+"a"+Path.DirectorySeparatorChar));byte[] beforeText=File.ReadAllBytes(textDependency.Path),beforeShop=File.ReadAllBytes(shopPath);var changed=(byte[])beforeText.Clone();changed[^1]^=1;File.WriteAllBytes(textDependency.Path,changed);
        reject(()=>staleService.Apply(stale),"SinglePurchase_StaleLanguageDependencyRejectsWholeApply");
        check(File.ReadAllBytes(shopPath).SequenceEqual(beforeShop)&&File.ReadAllBytes(textDependency.Path).SequenceEqual(changed),"SinglePurchase_StaleApplyPreservesRomAndExternalEdit");File.WriteAllBytes(textDependency.Path,beforeText);
        Exception? uiFailure=null;
        var ui=new Thread(()=>
        {
            try
            {
                var app=new ThreeDS.Progressive.App();app.InitializeComponent();WpfTestHost.SuppressStartup();StudioDialog.AutomatedChecks=true;
            var view=new MartEditorView(project);var flags=(System.Windows.Controls.Button[])typeof(MartEditorView).GetField("_recipeFlags",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
            check(flags[6].Visibility==System.Windows.Visibility.Visible&&view.RememberRecipe().SinglePurchase,"SinglePurchase_WpfButtonRestoresInstalledPolicy");
            var guided=new GuidedRandomizationPlan(project);guided.Initialize("Marts");check(guided.Get("Marts").Values.ContainsKey("SinglePurchase"),"SinglePurchase_GuidedUsumExposesPolicy");
                app.Shutdown();
            }
            catch(Exception ex){uiFailure=ex;}
        });ui.SetApartmentState(ApartmentState.STA);ui.Start();ui.Join();if(uiFailure is not null)throw new Exception("Single purchase WPF component checks failed",uiFailure);
        var state=new Gen7MartState(SinglePurchase:true);var template=GlobalTemplateDocument.Empty("USUM",7);NativeGen7MartBridge.Capture(template,state);File.WriteAllText(Path.Combine(output,"policy-template.json"),template.ToJson());
        // Retain a minimal synthetic project for the actual original backend integration probe.
        var resetFiles=new[]{new FileChange(shopPath,source,source)}.Concat(preview.ExtraFiles);foreach(var file in resetFiles)File.WriteAllBytes(file.Path,file.Original());
        string backendSource=Path.Combine(output,"backend-source");Directory.CreateDirectory(backendSource);
        foreach(string file in Directory.GetFiles(fixture.Root,"*",SearchOption.AllDirectories)){string path=Path.Combine(backendSource,Path.GetRelativePath(fixture.Root,file));Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(file,path);}
        foreach(var (file,hash) in sourceFiles)check(ProjectHistory.Hash(File.ReadAllBytes(file))==hash,"SinglePurchase_OriginalSourceUnchanged_"+Path.GetRelativePath(rom,file));
    }
}
