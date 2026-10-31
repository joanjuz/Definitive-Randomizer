using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

/// <summary>Gen7 adjustments: traded obedience, no obtainable Mega forms, Ultra Warp Ride hint and the single purchase white list.</summary>
internal static class Gen7AdjustmentChecks
{
    // Obedience table and the battle setup literal that points to it (code.bin file offsets, loaded at 0x100000).
    static byte[] ObedienceCode(bool usum)
    {
        var (table,literal)=usum?(0x4C4644,0x21BD64):(0x49E390,0x213C0C);
        byte[] code=new byte[table+0x100];
        new byte[]{20,35,50,65,80,100}.CopyTo(code,table);
        BitConverter.GetBytes((uint)(0x100000+table)).CopyTo(code,literal);
        return code;
    }

    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(bool usum in new[]{true,false})
        {
            string game=usum?"USUM":"SM";int table=usum?0x4C4644:0x49E390;
            byte[] code=ObedienceCode(usum),original=(byte[])code.Clone();
            check(Gen7Obedience.Read(code,usum)==Gen7Obedience.State.Stock&&Gen7Obedience.Apply(code,usum)&&code.AsSpan(table,6).ToArray().All(b=>b==100)
                &&Enumerable.Range(0,code.Length).Count(i=>code[i]!=original[i])==5,$"Gen7Adjust_{game}_ObedienceTableBecomes100");
            check(!Gen7Obedience.Apply(code,usum)&&Gen7Obedience.Read(code,usum)==Gen7Obedience.State.Applied,$"Gen7Adjust_{game}_ObedienceAppliesOnce");
            var unknown=ObedienceCode(usum);unknown[table+2]=51;
            reject(()=>Gen7Obedience.Apply(unknown,usum),$"Gen7Adjust_{game}_ObedienceRejectsUnknownTable");
            reject(()=>Gen7Obedience.Apply(ObedienceCode(!usum),usum),$"Gen7Adjust_{game}_ObedienceRejectsOtherGame");
        }

        check(Legal.IsMegaForm(6,1)&&Legal.IsMegaForm(6,2)&&Legal.IsMegaForm(150,2)&&Legal.IsMegaForm(382,1)&&Legal.IsMegaForm(800,3)
            &&!Legal.IsMegaForm(6,0)&&!Legal.IsMegaForm(800,1)&&!Legal.IsMegaForm(800,2)&&!Legal.IsMegaForm(658,2)&&!Legal.IsMegaForm(746,1)&&!Legal.IsMegaForm(555,1)&&!Legal.IsMegaForm(25,1),
            "Gen7Adjust_MegaFormsOnlyMegaPrimalAndUltraNecrozma");

        // Mega forms in a wild slot, a gift, a static, a trade and an evolution; a Totem battle and other battle forms keep theirs.
        using(var f=new Fixture(GameVersion.USUM))
        {
            ModuleChecks.CreateArchives(f);
            var statics=new GameStaticEncounterService(f.Session);
            int Static(int file,int index)=>statics.RecordId(file,index);
            void Set(IProjectEditor editor,int id,params (string Key,int Value)[] values)
            {
                var v=EditorValues.Flatten(editor.Read(id));foreach(var (key,value) in values)v[key]=value;editor.Save(id,v);
            }
            int gift=Static(0,0),megaStatic=Static(1,0),totem=Static(1,4),ash=Static(1,1),ultra=Static(1,2),dusk=Static(1,3),trade=Static(4,0);
            Set(statics,gift,("Species",6),("Form",2));Set(statics,megaStatic,("Species",3),("Form",1));Set(statics,totem,("Species",6),("Form",1));
            Set(statics,ash,("Species",658),("Form",2));Set(statics,ultra,("Species",800),("Form",3));Set(statics,dusk,("Species",800),("Form",1));Set(statics,trade,("Species",9),("Form",1));
            var evolutions=new GameEvolutionService(f.Session);
            Set(evolutions,1,("E0.Species",3),("E0.Form",1));Set(evolutions,2,("E0.Species",351),("E0.Form",1));
            var wild=new GameEncounterService(f.Session);int area=wild.Entries[0].Id;
            Set(wild,area,("S0.0.Species",6),("S0.0.Form",1),("S1.0.Species",746),("S1.0.Form",1),("S8.0.Species",382),("S8.0.Form",1));
            int entriesBefore=f.Session.History.Read().Entries.Count;
            var files=new[]{"encounterstatic","evolution","encdata"}.ToDictionary(n=>n,n=>File.ReadAllBytes(ModuleChecks.PathFor(f,n)));

            var proposals=Gen7Adjustments.PreviewNoMegas(f.Session);
            var changes=proposals.SelectMany(p=>p.Proposal.Changes).ToArray();
            check(changes.Length==7&&changes.All(c=>c.Field.EndsWith("Forma",StringComparison.Ordinal)||c.Field.Contains("forma",StringComparison.Ordinal)||c.Key!.EndsWith("Form",StringComparison.Ordinal))
                &&files.All(p=>File.ReadAllBytes(ModuleChecks.PathFor(f,p.Key)).SequenceEqual(p.Value))&&f.Session.History.Read().Entries.Count==entriesBefore,
                $"Gen7Adjust_NoMegasPreviewFindsEveryObtainableMegaWithoutWriting ({changes.Length})");
            Gen7Adjustments.ApplyNoMegas(proposals);
            int Form(IProjectEditor editor,int id,string key="Form")=>EditorValues.Flatten(editor.Read(id))[key];
            statics=new GameStaticEncounterService(f.Session);evolutions=new GameEvolutionService(f.Session);wild=new GameEncounterService(f.Session);
            check(Form(statics,gift)==0&&Form(statics,megaStatic)==0&&Form(statics,ultra)==0&&Form(statics,trade)==0&&Form(evolutions,1,"E0.Form")==0
                &&Form(wild,area,"S0.0.Form")==0&&Form(wild,area,"S8.0.Form")==0,"Gen7Adjust_NoMegasSetsBaseForms");
            check(Form(statics,totem)==1&&Form(statics,ash)==2&&Form(statics,dusk)==1&&Form(evolutions,2,"E0.Form")==1&&Form(wild,area,"S1.0.Form")==1
                &&EditorValues.Flatten(statics.Read(gift))["Species"]==6,"Gen7Adjust_NoMegasKeepsTotemsSpeciesAndOtherBattleForms");
            var history=f.Session.History.Read().Entries.Skip(entriesBefore).ToArray();
            check(history.Length==3&&history.All(e=>e.Parameters.GetValueOrDefault("ActionId")==Gen7Adjustments.NoMegasActionId)&&Gen7Adjustments.PreviewNoMegas(f.Session).Count==0,
                "Gen7Adjust_NoMegasRecordsHistoryAndAppliesOnce");
            var doc=GlobalTemplateDocument.Empty("USUM",7);doc.MergeHistoryActions(f.Session.History);
            check(doc.Actions.Contains(Gen7Adjustments.NoMegasActionId),"Gen7Adjust_NoMegasGoesToGlobalTemplate");

            // The fixture's story text has no file 366: the hint refuses instead of writing anything.
            GlobalApplicationChecks.PrepareOpenable(f);
            reject(()=>Gen7Adjustments.PreviewUltraWarpHint(f.Session),"Gen7Adjust_UltraWarpHintRejectsUnknownText");
        }
        using(var sm=new Fixture(GameVersion.SM))
        {
            bool refused=false;try{Gen7Adjustments.PreviewUltraWarpHint(sm.Session);}catch(NotSupportedException){refused=true;}
            check(refused,"Gen7Adjust_UltraWarpHintIsUsumOnly");
        }

        // White list in the Global Template recipe.
        var template=GlobalTemplateDocument.Empty("USUM",7);NativeGen7MartBridge.Capture(template,new(SinglePurchase:true,Exempt:[234,50]));
        check(NativeGen7MartBridge.Read(template) is {SinglePurchase:true,Exempt:[234,50]}&&template.Actions.SequenceEqual(new[]{GameMartService.SinglePurchaseAction}),"Gen7Adjust_WhitelistRoundTripsInGlobalTemplate");
        NativeGen7MartBridge.Capture(template,new(SinglePurchase:true));
        check(NativeGen7MartBridge.Read(template) is {Exempt:null}&&template.Section("ProgressiveApp")!["Gen7Marts"]!["SinglePurchaseExempt"] is null,"Gen7Adjust_EmptyWhitelistLeavesNoKey");
        var invalid=template.Section("ProgressiveApp")!;invalid["Gen7Marts"]!["SinglePurchaseExempt"]=new JsonArray(0);template.SetSection("ProgressiveApp",invalid);
        reject(()=>NativeGen7MartBridge.Read(template),"Gen7Adjust_WhitelistRejectsInvalidItem");
        foreach(string id in new[]{Gen7Adjustments.ObedienceActionId,Gen7Adjustments.NoMegasActionId,Gen7Adjustments.UltraWarpHintActionId})
            check(BatchRomExecution.Supports(id),"Gen7Adjust_BatchSupports_"+id);
    }

    /// <summary>Copies of the real files: <paramref name="usum"/> and <paramref name="sm"/> are extracted roots (ExtractedRomFS/ExtractedExeFS). Nothing is written there.</summary>
    internal static void RealCopies(string usum,string sm,Action<bool,string> check,Action<Action,string> reject)
    {
        var sources=new[]{usum,sm}.SelectMany(root=>Directory.GetFiles(Path.Combine(root,"ExtractedExeFS"),"code.bin").Append(Path.Combine(root,"ExtractedRomFS","Shop.cro"))).Where(File.Exists).ToDictionary(p=>p,p=>ProjectHistory.Hash(File.ReadAllBytes(p)));
        foreach(var (root,isUsum) in new[]{(usum,true),(sm,false)})
        {
            byte[] code=File.ReadAllBytes(Path.Combine(root,"ExtractedExeFS","code.bin")),original=(byte[])code.Clone();
            check(Gen7Obedience.Read(code,isUsum)==Gen7Obedience.State.Stock&&Gen7Obedience.Apply(code,isUsum)&&Enumerable.Range(0,code.Length).Count(i=>code[i]!=original[i])==5,
                $"Gen7Adjust_Real_{(isUsum?"USUM":"SM")}_ObedienceChangesFiveBytes");
            reject(()=>Gen7Obedience.Read(original,!isUsum),$"Gen7Adjust_Real_{(isUsum?"USUM":"SM")}_ObedienceRejectsOtherFamilyOffsets");
        }

        using(var f=new Fixture(GameVersion.USUM))
        {
            ModuleChecks.CreateArchives(f);GlobalApplicationChecks.PrepareOpenable(f);
            var reference=f.Game.GetGARCReference("storytext");
            foreach(int language in new[]{2,6})
            {
                var relative=reference.GetRelativeGARC(language);
                File.Copy(Path.Combine(usum,"ExtractedRomFS",relative.Reference),Path.Combine(f.Game.RomFS,relative.Reference),true);
            }
            File.Copy(Path.Combine(usum,"ExtractedExeFS","code.bin"),Path.Combine(f.Game.ExeFS,"code.bin"),true);
            var obedience=Gen7Adjustments.PreviewObedience(f.Session)!;Gen7Adjustments.ApplyObedience(f.Session,obedience);
            check(Gen7Adjustments.PreviewObedience(f.Session) is null&&f.Session.History.Read().Entries[^1].Parameters["ActionId"]==Gen7Adjustments.ObedienceActionId,"Gen7Adjust_Real_ObedienceServiceAppliesOnceWithHistory");

            var (files,changes)=Gen7Adjustments.PreviewUltraWarpHint(f.Session);
            check(files.Count==2&&changes.Count==4&&changes.All(c=>c.After.Contains("GAME FREAK",StringComparison.Ordinal)&&c.After.StartsWith(c.Before[..c.Before.LastIndexOf("[VAR 0114(",StringComparison.Ordinal)],StringComparison.Ordinal)
                &&c.After.EndsWith(c.Before[c.Before.LastIndexOf("[VAR 0114(",StringComparison.Ordinal)..],StringComparison.Ordinal)),"Gen7Adjust_Real_UltraWarpHintAddsOnePageBeforeTheClosingCommand");
            Gen7Adjustments.ApplyUltraWarpHint(f.Session,files,changes);
            bool othersKept=true;
            foreach(var file in files)
            {
                var a=new GARC.MemGARC(file.Original()).Files;var b=new GARC.MemGARC(File.ReadAllBytes(file.Path)).Files;
                for(int i=0;i<a.Length;i++)if(i!=366&&!a[i].SequenceEqual(b[i]))othersKept=false;
                var before=new TextFile(f.Game,a[366],f.Game.RemapCharacters).Lines;var after=new TextFile(f.Game,b[366],f.Game.RemapCharacters).Lines;
                if(before.Length!=after.Length||Enumerable.Range(0,before.Length).Any(i=>i is not (6 or 16)&&before[i]!=after[i]))othersKept=false;
            }
            check(othersKept&&Gen7Adjustments.PreviewUltraWarpHint(f.Session).Changes.Count==0,"Gen7Adjust_Real_UltraWarpHintOnlyTouchesTwoLinesPerLanguageAndAppliesOnce");
        }

        // Global Template through the original LegacyBatch backend: the three actions, on a copy, after everything else.
        using(var b=new Fixture(GameVersion.USUM))
        {
            ModuleChecks.CreateArchives(b);GlobalApplicationChecks.PrepareOpenable(b);
            var reference=b.Game.GetGARCReference("storytext");
            foreach(int language in new[]{2,6})
            {
                var relative=reference.GetRelativeGARC(language);
                File.Copy(Path.Combine(usum,"ExtractedRomFS",relative.Reference),Path.Combine(b.Game.RomFS,relative.Reference),true);
            }
            File.Copy(Path.Combine(usum,"ExtractedExeFS","code.bin"),Path.Combine(b.Game.ExeFS,"code.bin"),true);
            SinglePurchaseChecks.CopyModules(b,Path.Combine(usum,"ExtractedRomFS"));
            ushort exemptItem=Gen7SinglePurchaseStock.CurrentOffers(File.ReadAllBytes(Path.Combine(usum,"ExtractedRomFS","Shop.cro")))[0].Item;
            var wild=new GameEncounterService(b.Session);int area=wild.Entries[0].Id;
            var values=EditorValues.Flatten(wild.Read(area));values["S0.0.Species"]=6;values["S0.0.Form"]=1;wild.Save(area,values);
            var doc=GlobalTemplateDocument.Empty("USUM",7);doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});
            doc.SetSection("Actions",new JsonArray(new[]{Gen7Adjustments.ObedienceActionId,Gen7Adjustments.UltraWarpHintActionId,Gen7Adjustments.NoMegasActionId}.Select(id=>(JsonNode)new JsonObject{["Id"]=id,["Parameters"]=new JsonObject()}).ToArray()));
            NativeGen7MartBridge.Capture(doc,new(SinglePurchase:true,Exempt:[exemptItem]));
            check(NativeRecipeCompletion.TryComplete(b.Session,doc),"Gen7Adjust_Backend_RecipeFromHistoryIsCompleteWithTheNewActions");
            using var preview=new GlobalTemplateApplication(b.Session).PreviewAsync(doc).GetAwaiter().GetResult();
            byte[] Proposed(string name)=>File.ReadAllBytes(preview.Files.Single(f=>f.RelativePath.Replace('\\','/').EndsWith(name,StringComparison.Ordinal)).ProposedPath);
            byte[] code=Proposed("code.bin");
            var spanish=new GARC.MemGARC(Proposed(reference.GetRelativeGARC(6).Reference.Replace('\\','/'))).Files[366];
            check(Gen7Obedience.Read(code,true)==Gen7Obedience.State.Applied,"Gen7Adjust_Backend_ObedienceApplied");
            check(new TextFile(b.Game,spanish,b.Game.RemapCharacters).Lines[6].Contains("GAME FREAK",StringComparison.Ordinal),"Gen7Adjust_Backend_UltraWarpHintApplied");
            var encounters=preview.Files.Single(f=>f.RelativePath.Replace('\\','/').EndsWith(b.Game.GetGARCFileName("encdata").Replace('\\','/'),StringComparison.Ordinal));
            var stagedWild=new GameEncounterService(GameProjectSession.Open(Path.Combine(preview.Workspace,"preview-project"),recoverInterruptedSave:false));
            var after=EditorValues.Flatten(stagedWild.Read(area));
            check(encounters.AfterSha256!=encounters.BeforeSha256&&after["S0.0.Species"]==6&&after["S0.0.Form"]==0,$"Gen7Adjust_Backend_NoMegasRemovesTheWildMega ({preview.Files.Count} archivos)");
            byte[] stagedShop=Proposed("Shop.cro");var stagedStock=Gen7SinglePurchaseStock.Read(stagedShop)!;
            check(stagedStock.Offers.Count>100&&stagedStock.ExemptItems.SequenceEqual(new[]{exemptItem})&&Gen7SinglePurchaseStock.CurrentOffers(stagedShop).Any(o=>o.Item==exemptItem),"Gen7Adjust_Backend_WhitelistedItemStaysUnlimited");
        }

        // White list on the real Shop.cro: listed offers are registered but flagged; one registered before keeps its row and save bit.
        byte[] shop=File.ReadAllBytes(Path.Combine(usum,"ExtractedRomFS","Shop.cro"));
        var offers=Gen7SinglePurchaseStock.CurrentOffers(shop);ushort listed=offers[0].Item;
        byte[] fresh=Gen7SinglePurchaseStock.Apply(shop,167,[listed]);var freshStock=Gen7SinglePurchaseStock.Read(fresh)!;
        check(offers.All(freshStock.Offers.Contains)&&freshStock.ExemptItems.SequenceEqual(new[]{listed})&&freshStock.Exempt.SetEquals(offers.Where(o=>o.Item==listed))
            &&Gen7SinglePurchaseStock.Apply(fresh,167).SequenceEqual(fresh),"Gen7Adjust_Real_WhitelistedOffersAreRegisteredFlaggedAndRemembered");
        byte[] installed=Gen7SinglePurchaseStock.Apply(shop,167);var stock=Gen7SinglePurchaseStock.Read(installed)!;
        byte[] exempt=Gen7SinglePurchaseStock.Apply(installed,167,[listed]);var exemptStock=Gen7SinglePurchaseStock.Read(exempt)!;
        int row=stock.Offers.ToList().FindIndex(o=>o.Item==listed);
        check(exemptStock.Offers.SequenceEqual(stock.Offers)&&exemptStock.Identity==stock.Identity&&exemptStock.ExemptItems.SequenceEqual(new[]{listed})&&exemptStock.Exempt.All(o=>o.Item==listed)
            &&BitConverter.ToUInt16(exempt,(int)stock.Location+0x900+4+row*8)>=0x8000&&BitConverter.ToUInt16(exempt,(int)stock.Location+0x900+4+row*8+4)==row+1,
            "Gen7Adjust_Real_RegisteredItemKeepsItsRowAndBitButIsNeverFound");
        check(Gen7SinglePurchaseStock.Apply(exempt,167).SequenceEqual(exempt)&&Gen7SinglePurchaseStock.Apply(exempt,167,[]).SequenceEqual(installed)&&CroIntegrity.Rebuild(exempt).SequenceEqual(exempt),
            "Gen7Adjust_Real_WhitelistIsKeptRemovableAndHashed");
        reject(()=>Gen7SinglePurchaseStock.Apply(installed,167,[0]),"Gen7Adjust_Real_WhitelistRejectsEmptyItem");
        foreach(var (path,hash) in sources)check(ProjectHistory.Hash(File.ReadAllBytes(path))==hash,"Gen7Adjust_Real_SourceUnchanged_"+Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))+"_"+Path.GetFileName(path));
    }
}
