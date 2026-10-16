using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class GlobalEditorBridgeChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            string eggPath=ModuleChecks.PathFor(f,"eggmove");
            f.WriteGarc(eggPath,Enumerable.Range(0,840).Select(_=>new byte[f.Game.Generation==7?6:2]).ToArray(),f.Game.GetGARCReference("eggmove"));
            foreach(IProjectEditor generic in new IProjectEditor[]{new GameItemService(f.Session),new GameEvolutionService(f.Session),new GameEggMoveService(f.Session)})
            {
                var originalOptions=EditorValues.Flatten(generic.RandomOptions);
                var snapshot=GlobalTemplateDocument.Empty(version.ToString(),f.Game.Generation);
                snapshot.SetSection("FutureModule",new JsonObject{["Preserve"]=17});
                GlobalEditorTemplateBridge.Capture(snapshot,generic,new(originalOptions,2),f.Game.Generation);
                var loaded=GlobalEditorTemplateBridge.Restore(snapshot,generic,f.Game.Generation)!;
                check(loaded.Scope==2&&originalOptions.All(p=>loaded.Options[p.Key]==p.Value)&&snapshot.Section("FutureModule")!["Preserve"]!.GetValue<int>()==17,
                    version+" Global native module options and scope round-trip without removing unknown sections: "+generic.Title);
                check(!GlobalEditorTemplateBridge.HasSettings(snapshot,"FieldItems",f.Game.Generation),version+" Global does not report unrelated editor settings as present");
            }
            if(f.Game.Generation==6)
            {
                var egg=new GameEggMoveService(f.Session);var recipe=GlobalTemplateDocument.Empty(version.ToString(),6);
                recipe.SetSection("RandSettings",new JsonArray("EggMoveEditor6","CHK_Expand\tTrue","NUD_Moves\t18","CHK_STAB\tFalse","NUD_STAB\t47","CHK_HMs\tTrue","FutureControl\t77",""));
                var state6=GlobalEditorTemplateBridge.Restore(recipe,egg,6)!;
                check(state6.Options["Hms"]==1&&state6.Options["STAB"]==0&&state6.Options["Count"]==18,version+" Global imports Gen6 egg controls including HMs");
                GlobalEditorTemplateBridge.Capture(recipe,egg,state6,6);
                check(GlobalEditorTemplateBridge.HasSettings(recipe,"EggMoves",6)&&recipe.Section("RandSettings")!.ToJsonString().Contains("FutureControl"),version+" Global Gen6 egg capture preserves unknown controls");
                recipe.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="egg-moves.randomize",["Parameters"]=new JsonObject()}));
                GlobalEditorTemplateBridge.ValidateBatchReplay(recipe);
                check(true,version+" Batch accepts full-scope original Gen6 egg recipe");
                f.WriteGarc(eggPath,Enumerable.Range(0,6).Select(_=>new byte[2]).ToArray(),f.Game.GetGARCReference("eggmove"));
                foreach(int hms in new[]{0,1})
                {
                    var nativeEgg=new GameEggMoveService(f.Session);var options6=EditorValues.Flatten(nativeEgg.RandomOptions);
                    options6["Hms"]=hms;options6["STAB"]=0;
                    var proposal=nativeEgg.Preview(options6,[1,2,3,4,5]);
                    var expected=Enumerable.Range(0,6).Select(_=>new pk3DS.Core.Structures.EggMoves6(new byte[2])).ToArray();
                    var engine=new pk3DS.Core.Randomizers.EggMoveRandomizer(f.Game,expected)
                    {Expand=true,ExpandTo=18,STAB=false,STABPercent=33,BannedMoves=[165,621,..(hms==0?new[]{15,19,57,70,127,249,291}:Array.Empty<int>())]};
                    Util.ReseedRand(proposal.Seed);engine.Execute();
                    var proposed=new pk3DS.Core.CTR.GARC.MemGARC(proposal.Files.Single().Proposed()).Files;
                    check(Enumerable.Range(1,5).All(i=>proposed[i].SequenceEqual(expected[i].Write())),version+" Gen6 egg preview matches original Core rules for HM option "+hms);
                    check(File.ReadAllBytes(eggPath).SequenceEqual(proposal.Files.Single().Original()),version+" Gen6 egg preview preserves source bytes");
                }
                using var mf=new Fixture(version);var fixture=Machine6Checks.Setup(mf);var machines=new GameMachine6Service(mf.Session);
                var machineRecipe=GlobalTemplateDocument.Empty(version.ToString(),6);
                machineRecipe.SetSection("RandSettings",new JsonArray("TMHMEditor6","CHK_RandomizeField\tTrue","CHK_RandomizeHM\tTrue","FutureControl\t88",""));
                var machineState=GlobalEditorTemplateBridge.Restore(machineRecipe,machines,6)!;
                check(machineState.Options["Field"]==1&&machineState.Options["Hms"]==1&&machineState.Options["Tms"]==1,version+" Global imports original MT/MO controls with native TM default");
                var changed=new Dictionary<string,int>(machineState.Options){["Hms"]=0,["Tms"]=0};
                GlobalEditorTemplateBridge.Capture(machineRecipe,machines,new(changed,0),6);
                var restoredMachine=GlobalEditorTemplateBridge.Restore(machineRecipe,machines,6)!;
                check(restoredMachine.Options["Tms"]==0&&restoredMachine.Options["Hms"]==0&&machineRecipe.Section("RandSettings")!.ToJsonString().Contains("FutureControl"),version+" Global preserves native-only TM toggle and unknown original controls");
                check(File.ReadAllBytes(fixture.Code).SequenceEqual(fixture.CodeBytes),version+" Global MT/MO settings never edit executable");
            }
            if(f.Game.Generation==7)
            {
                var egg=new GameEggMoveService(f.Session);var recipe=GlobalTemplateDocument.Empty(version.ToString(),7);
                recipe.SetSection("RandSettings",new JsonArray("EggMoveEditor7","CHK_Expand\tFalse","NUD_Moves\t12","CHK_STAB\tTrue","NUD_STAB\t47","FutureControl\t77",""));
                var restoredEgg=GlobalEditorTemplateBridge.Restore(recipe,egg,7)!;
                check(restoredEgg.Options["Expand"]==0&&restoredEgg.Options["Count"]==12&&restoredEgg.Options["STABPercent"]==47,version+" Global imports original egg move controls");
                GlobalEditorTemplateBridge.Capture(recipe,egg,new(restoredEgg.Options,0),7);
                recipe.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="egg-moves.randomize",["Parameters"]=new JsonObject()}));
                GlobalEditorTemplateBridge.ValidateBatchReplay(recipe);
                check(recipe.Section("RandSettings")!.ToJsonString().Contains("FutureControl"),version+" Global egg settings retain unknown controls and accept full-scope Batch replay");
                GlobalEditorTemplateBridge.Capture(recipe,egg,new(restoredEgg.Options,1),7);
                reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(recipe),version+" Batch rejects partial egg scope instead of expanding selection");
            }
            var trainer=new GameTrainerService(f.Session);
            string form=f.Game.Generation==7?"SMTE":"TrainerRand";
            string factor=f.Game.Generation==7?"NUD_LevelBoost":"NUD_Level";
            var doc=GlobalTemplateDocument.Empty(version.ToString(),f.Game.Generation);
            doc.SetSection("RandSettings",new JsonArray(form,"CHK_RandomPKM\tFalse",factor+"\t1.25","FutureOption\tkept","","OtherEditor","X\tTrue",""));
            string header=ModuleChecks.PathFor(f,"trdata"),team=ModuleChecks.PathFor(f,"trpoke");
            byte[] beforeHeader=File.ReadAllBytes(header),beforeTeam=File.ReadAllBytes(team);
            var state=GlobalEditorTemplateBridge.Restore(doc,trainer,f.Game.Generation)!;
            check(state.Options["CHK_RandomPKM"]==0&&state.Options[factor]==125,version+" Global imports original trainer booleans and decimal scale");
            var root=JsonNode.Parse(state.Template!)!;root["FutureTrainer"]="preserve";
            doc.SetSection("Trainer",root);
            var options=new Dictionary<string,int>(state.Options){["CHK_RandomPKM"]=1,[factor]=150,["Rule.1.CapUse"]=1,["Rule.1.LevelCap"]=42};
            options["Rule.1.MoveUse"]=1;if(options.ContainsKey("CHK_StrongStat"))options["CHK_StrongStat"]=1;else options["GlobalMoves.StrongStat"]=1;
            if(options.ContainsKey("NUD_StrongStatTolerance"))options["NUD_StrongStatTolerance"]=20;else options["GlobalMoves.Tolerance"]=20;options["Rule.1.Status"]=0;options["Rule.1.EV"]=252;
            options["GlobalCaps.ApplyPrevious"]=1;options["GlobalCaps.PreviousGap"]=5;
            if(options.ContainsKey("NUD_Shiny"))options["NUD_Shiny"]=1250;
            string json=trainer.CaptureOptions(options);
            GlobalEditorTemplateBridge.Capture(doc,trainer,new(options,2,json),f.Game.Generation);
            var futureNative=doc.Section("ProgressiveApp")!;futureNative["Trainers"]!["Options"]!["FutureNativeOption"]=42;
            doc.SetSection("ProgressiveApp",futureNative);GlobalEditorTemplateBridge.Capture(doc,trainer,new(options,2,json),f.Game.Generation);
            var reload=GlobalTemplateDocument.Parse(doc.ToJson(),version.ToString(),f.Game.Generation);
            var restored=GlobalEditorTemplateBridge.Restore(reload,new GameTrainerService(f.Session),f.Game.Generation)!;
            check(restored.Scope==2&&restored.Options[factor]==150&&restored.Options["Rule.1.LevelCap"]==42&&restored.Options["Rule.1.CapUse"]==1,version+" Global trainer options scope and individual caps round-trip");
            foreach(var p in options.Where(p=>!restored.Options.TryGetValue(p.Key,out int v)||v!=p.Value))Console.WriteLine($"OPTION MISMATCH {version}: {p.Key} = {p.Value} -> {restored.Options.GetValueOrDefault(p.Key,-999)}");
            check(options.All(p=>restored.Options.TryGetValue(p.Key,out int value)&&value==p.Value),version+" Global preserves every exposed trainer option including decimal shiny probability");
            var extra=doc.Section("Trainer")!;
            var cap=extra["LevelCaps"]!["Trainers"]!.AsArray().First(r=>r!["TrainerID"]!.GetValue<int>()==1)!;
            cap["FutureRule"]="preserve";doc.SetSection("Trainer",extra);
            GlobalEditorTemplateBridge.Capture(doc,trainer,new(options,2,json),f.Game.Generation);
            check(doc.Section("Trainer")!["LevelCaps"]!["Trainers"]!.AsArray().First(r=>r!["TrainerID"]!.GetValue<int>()==1)!["FutureRule"]!.GetValue<string>()=="preserve",version+" Global preserves unknown properties inside individual trainer rules");
            var metadata=JsonNode.Parse(json)!;
            var disabled=metadata["LevelCaps"]!["Trainers"]!.AsArray().First(r=>r!["TrainerID"]!.GetValue<int>()==1)!;
            disabled["Use"]=false;disabled["CurrentAceLevel"]=57;disabled["NerfTeam"]=true;
            metadata["LevelCaps"]!["ResetUnlistedTrainers"]=false;
            var metadataTrainer=new GameTrainerService(f.Session);metadataTrainer.RestoreOptions(metadata.ToJsonString());
            var kept=JsonNode.Parse(metadataTrainer.CaptureOptions(EditorValues.Flatten(metadataTrainer.RandomOptions)))!;
            var keptRule=kept["LevelCaps"]!["Trainers"]!.AsArray().First(r=>r!["TrainerID"]!.GetValue<int>()==1)!;
            check(!keptRule["Use"]!.GetValue<bool>()&&keptRule["CurrentAceLevel"]!.GetValue<int>()==57&&keptRule["NerfTeam"]!.GetValue<bool>()&&!kept["LevelCaps"]!["ResetUnlistedTrainers"]!.GetValue<bool>(),version+" Trainer host preserves disabled rules ace metadata max-three and reset policy");
            check(reload.Section("Trainer")!["FutureTrainer"]!.GetValue<string>()=="preserve",version+" Global preserves unknown trainer properties");
            string rows=reload.Section("RandSettings")!.ToJsonString();
            check(rows.Contains("FutureOption")&&rows.Contains("OtherEditor")&&rows.Contains("1.5")&&reload.Section("ProgressiveApp")!["Trainers"]!["Options"]!["FutureNativeOption"]!.GetValue<int>()==42,version+" Global export retains unknown controls native options and other legacy modules");
            var bad=doc.Clone();var native=(JsonObject)bad.Section("ProgressiveApp")!;
            native["Trainers"]!["Options"]![factor]=999999;bad.SetSection("ProgressiveApp",native);
            reject(()=>GlobalEditorTemplateBridge.Restore(bad,new GameTrainerService(f.Session),f.Game.Generation),version+" Global rejects invalid options before UI replacement");
            check(File.ReadAllBytes(header).SequenceEqual(beforeHeader)&&File.ReadAllBytes(team).SequenceEqual(beforeTeam),version+" Global trainer import and export never write ROM records");
            var partial=doc.Clone();var partialState=(JsonObject)partial.Section("ProgressiveApp")!;partialState["PartialSnapshot"]=true;partial.SetSection("ProgressiveApp",partialState);
            reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(partial),version+" Batch refuses incomplete history snapshots even after trainer settings were captured");
            if(f.Game.Generation!=7)
            {
                int gift=f.Game.ORAS?0xF906C:0xF805C,size=f.Game.ORAS?36:24,count=f.Game.ORAS?37:19;
                byte[] cro=new byte[gift+size*count+64];"CRO0"u8.CopyTo(cro.AsSpan(0x80));
                string croPath=Path.Combine(f.Game.RomFS,"DllField.cro");File.WriteAllBytes(croPath,cro);
                var static6=new GameStaticEncounter6Service(f.Session);
                var values=EditorValues.Flatten(static6.RandomOptions);values["Final"]=1;values["FinalLevel"]=45;values["PreserveMega"]=0;
                GlobalEditorTemplateBridge.Capture(doc,static6,new(values,1),6);
                var native6=GlobalEditorTemplateBridge.Restore(doc,new GameStaticEncounter6Service(f.Session),6)!;
                check(values.All(p=>native6.Options[p.Key]==p.Value)&&native6.Scope==1,version+" Global Gen6 static native settings round-trip");
                check(File.ReadAllBytes(croPath).SequenceEqual(cro),version+" Global Gen6 static option transfer preserves DllField");
                continue;
            }
            var statics=new GameStaticEncounterService(f.Session);
            var staticDoc=GlobalTemplateDocument.Empty(version.ToString(),7);
            staticDoc.SetSection("RandSettings",new JsonArray("StaticEncounterEditor7","CHK_Item\tTrue","CHK_G7\tFalse","NUD_LevelBoost\t1.75",""));
            string path=ModuleChecks.PathFor(f,"encounterstatic");byte[] before=File.ReadAllBytes(path);
            var staticState=GlobalEditorTemplateBridge.Restore(staticDoc,statics,7)!;
            check(staticState.Options["RandomItems"]==1&&staticState.Options["G7"]==0&&staticState.Options["LevelScale"]==175,version+" Global imports static original controls");
            var staticOptions=new Dictionary<string,int>(staticState.Options){["LevelFlat"]=5,["Trades"]=0};
            GlobalEditorTemplateBridge.Capture(staticDoc,statics,new(staticOptions,1),7);
            var staticReload=GlobalEditorTemplateBridge.Restore(staticDoc,new GameStaticEncounterService(f.Session),7)!;
            check(staticReload.Options["LevelFlat"]==5&&staticReload.Options["Trades"]==0&&staticReload.Scope==1,version+" Global static native options round-trip");
            check(File.ReadAllBytes(path).SequenceEqual(before),version+" Global static transfer never writes ROM records");
            check(new BatchRomExecution(f.Session).BackendAvailable,version+" Batch original executable is included in Debug output");
            staticDoc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="static-encounters.randomize",["Parameters"]=new JsonObject()}));
            reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(staticDoc),version+" Batch rejects partial native static scope");
            var allOptions=new Dictionary<string,int>(staticOptions){["Trades"]=1,["LevelFlat"]=0};
            GlobalEditorTemplateBridge.Capture(staticDoc,statics,new(allOptions,0),7);
            GlobalEditorTemplateBridge.ValidateBatchReplay(staticDoc);
            check(true,version+" Batch accepts transferable static settings with full scope");
            allOptions["LevelFlat"]=5;GlobalEditorTemplateBridge.Capture(staticDoc,statics,new(allOptions,0),7);
            GlobalEditorTemplateBridge.ValidateBatchReplay(staticDoc);check(true,version+" Batch accepts native level additions through verified Gen7 bridge");
            foreach(string action in new[]{"starters.randomize","static-encounters.modify-levels"})
            {
                staticDoc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]=action,["Parameters"]=new JsonObject()}));
                reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(staticDoc),version+" Batch validates actual original action "+action);
            }
        }
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            var records=ModuleChecks.Read(f,"encounterstatic");
            byte[] giftBytes=(byte[])records[0].Clone();
            _=new pk3DS.Core.Structures.EncounterGift7(giftBytes){SpecialMove=44};
            records[0]=Enumerable.Range(0,4).SelectMany(_=>giftBytes).ToArray();records[1][0]=1;
            string path=ModuleChecks.PathFor(f,"encounterstatic");f.WriteGarc(path,records,f.Game.GetGARCReference("encounterstatic"));
            StarterTextChecks.CreateBanks(f,0);
            byte[] before=File.ReadAllBytes(path);var editor=new GameStaticEncounterService(f.Session);
            var options=EditorValues.Flatten(editor.RandomOptions);options["Species"]=0;options["GiftMoves"]=1;options["Metronome"]=1;
            var preview=editor.Preview(options,[1,4,5]);
            check(File.ReadAllBytes(path).SequenceEqual(before),version+" Metronome static preview writes nothing");
            editor.Apply(preview);
            check(EditorValues.Flatten(editor.Read(1))["Move"]==44&&EditorValues.Flatten(editor.Read(4))["Move"]==0,version+" Metronome preserves starter special move and clears non-starter gift special move");
            var result=EditorValues.Flatten(editor.Read(5));
            check(result["Move0"]==118&&Enumerable.Range(1,3).All(i=>result[$"Move{i}"]==0),version+" Metronome sets exact original static relearn moves");
            var doc=GlobalTemplateDocument.Empty(version.ToString(),7);GlobalEditorTemplateBridge.Capture(doc,editor,new(options,0),7);
            check(doc.Section("RandSettings")!.ToJsonString().Contains("CHK_Metronome")&&GlobalEditorTemplateBridge.Restore(doc,new GameStaticEncounterService(f.Session),7)!.Options["Metronome"]==1,version+" Metronome round-trips through original Global control");
            options["Species"]=1;options["BasicStarter"]=1;options["AllowMega"]=1;options["ForceTotem"]=1;
            for(int gen=1;gen<=7;gen++)options[$"G{gen}"]=0;
            editor.Apply(editor.Preview(options,[1]));
            check(Legal.BasicStarters_7.Contains(EditorValues.Flatten(editor.Read(1))["Species"]),version+" explicit basic starter pool overrides disabled generation filters");
            var staticValues=EditorValues.Flatten(editor.Read(5));staticValues["Species"]=20;
            editor.Save(5,staticValues);
            editor.Apply(editor.Preview(options,[5]));
            check(Legal.FinalEvolutions_7.Concat(Legal.Legendary_USUM).Concat(Legal.Mythical_USUM).Contains(EditorValues.Flatten(editor.Read(5))["Species"]),version+" Force Totem uses original final evolution pool including permitted special species");
            GlobalEditorTemplateBridge.Capture(doc,editor,new(options,0),7);
            var restoredOptions=GlobalEditorTemplateBridge.Restore(doc,new GameStaticEncounterService(f.Session),7)!.Options;
            check(new[]{"AllowMega","BasicStarter","ForceTotem"}.All(k=>restoredOptions[k]==1),version+" Mega starter and Totem options round-trip through Global");
            f.Game.Personal.Table[6].FormeCount=3;
            check(Enumerable.Range(0,64).All(seed=>GameStaticEncounterService.PickLegacyForm(6,false,f.Game.Personal.Table,new Random(seed))==0),version+" Mega species keep base form when Mega disabled");
            check(Enumerable.Range(0,64).Any(seed=>GameStaticEncounterService.PickLegacyForm(6,true,f.Game.Personal.Table,new Random(seed))>0),version+" Mega enabled exposes alternative forms");
            f.Game.Personal.Table[666].FormeCount=20;f.Game.Personal.Table[774].FormeCount=14;
            check(GameStaticEncounterService.PickLegacyForm(666,false,f.Game.Personal.Table,new Random(1))==30&&Enumerable.Range(0,64).All(seed=>GameStaticEncounterService.PickLegacyForm(774,false,f.Game.Personal.Table,new Random(seed))<7),version+" Vivillon and Minior original special form rules preserved");
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(f);
        var main=new MainWindow();main.LoadProject(f.Session);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var restore=typeof(MainWindow).GetMethod("RestoreGlobalOptions",flags)!;
        var capture=typeof(MainWindow).GetMethod("CaptureGlobalOptions",flags)!;
        var view=new GlobalTemplateView(f.Session,d=>restore.Invoke(main,[d]),d=>capture.Invoke(main,[d]));
        var doc=GlobalTemplateDocument.Empty("USUM",7);
        doc.SetSection("RandSettings",new JsonArray("SMTE","CHK_RandomPKM\tFalse","NUD_LevelBoost\t1.25","","StaticEncounterEditor7","CHK_Item\tTrue",""));
        string path=Path.Combine(f.Root,"source-global.json");doc.Save(path);byte[] original=File.ReadAllBytes(path);
        view.OpenPath(path);
        var create=typeof(MainWindow).GetMethod("CreateEditor",flags)!;
        var trainer=(RecordEditorView)create.Invoke(main,[new GameTrainerService(f.Session)])!;
        check(trainer.RememberOptions().Options["CHK_RandomPKM"]==0&&trainer.RememberOptions().Options["NUD_LevelBoost"]==125,"WPF Global import restores actual trainer controls through main window state");
        var statics=(RecordEditorView)create.Invoke(main,[new GameStaticEncounterService(f.Session)])!;
        check(statics.RememberOptions().Options["RandomItems"]==1,"WPF Global import restores actual static controls");
        var exported=view.ExportSnapshot();
        check(exported.Section("Trainer") is JsonObject&&exported.Section("ProgressiveApp")?["StaticEncounters"] is JsonObject,"WPF Global export captures remembered trainer rules and static options");
        check(File.ReadAllBytes(path).SequenceEqual(original),"WPF Global source remains byte-identical after import and export snapshot");
        var states=(Dictionary<string,RecordEditorView.UiState>)typeof(MainWindow).GetField("_sectionOptions",flags)!.GetValue(main)!;
        var previous=states["Entrenadores"];
        var bad=doc.Clone();bad.SetSection("RandSettings",new JsonArray("SMTE","NUD_LevelBoost\t1.75","","StaticEncounterEditor7","NUD_LevelBoost\t999",""));
        string invalid=Path.Combine(f.Root,"invalid-global.json");bad.Save(invalid);bool rejected=false;
        try{view.OpenPath(invalid);}catch(TargetInvocationException){rejected=true;}
        check(rejected&&ReferenceEquals(previous,states["Entrenadores"])&&view.LoadedSourcePath==path,"WPF invalid static import leaves previous trainer state and loaded recipe intact");
        var historyView=new GlobalTemplateView(f.Session,null,d=>capture.Invoke(main,[d]));
        typeof(GlobalTemplateView).GetMethod("FromHistory",flags)!.Invoke(historyView,null);
        check(historyView.LoadedTemplate!.Section("ProgressiveApp")!["PartialSnapshot"]!.GetValue<bool>(),"WPF history snapshot remains explicitly partial after capturing remembered options");
        main.Close();
    }
}
