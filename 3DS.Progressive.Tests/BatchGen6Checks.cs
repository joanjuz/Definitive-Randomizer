using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

internal static class BatchGen6Checks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var assembly=Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));
        var executor=assembly.GetType("pk3DS.WinForms.BatchGen6ActionExecutor",true)!;
        var actions=((IEnumerable<string>)executor.GetField("Supported",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        check(actions.SetEquals(BatchRomExecution.Gen6ActionIds),"Gen6 WPF preflight and packaged runner share the exact supported action set");
        foreach(var (form,methods) in new[]{("MoveEditor6",new[]{"B_RandAll_Click","B_Metronome_Click","B_BalanceMoves_Click","SetEntry"}),
            ("PersonalEditor6",new[]{"B_Randomize_Click","B_ModifyAll","B_PokemonStatsTemplate_Click","SaveEntry"}),
            ("EvolutionEditor6",new[]{"B_RandAll_Click","B_EveryLevel_Click","B_Trade_Click","B_NormalizeEvolutions_Click","SetList"}),
            ("LevelUpEditor6",new[]{"B_RandAll_Click","B_Metronome_Click","SetList"}),
            ("EggMoveEditor6",new[]{"B_RandAll_Click","Form_Closing"})})
            check(methods.All(name=>assembly.GetType("pk3DS.WinForms."+form,true)!.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null),"Gen6 packaged original replay methods exist: "+form);
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using(var native=new Fixture(version))
            using(var replay=new Fixture(version))
            {
                var first=Trade6Checks.Setup(native);var second=Trade6Checks.Setup(replay);
                var editorTrade=new GameTrade6Service(native.Session);
                var optionsTrade=new Dictionary<string,int>{{"Offers",0}};
                editorTrade.Apply(editorTrade.Preview(optionsTrade,[1]));
                var patcher=assembly.GetType("pk3DS.WinForms.Gen6TradePatcher",true)!;
                var apply=patcher.GetMethod("ApplyGen6TradePatch",BindingFlags.Public|BindingFlags.Static)!;
                apply.Invoke(null,new object[]{replay.Game.ExeFS,replay.Game,false});
                check(File.ReadAllBytes(first.Path).SequenceEqual(File.ReadAllBytes(second.Path)),version+" Batch original trade patch matches native editor byte for byte");
                var recipe=GlobalTemplateDocument.Empty(version.ToString(),6);
                GlobalEditorTemplateBridge.Capture(recipe,editorTrade,new(optionsTrade,0),6);
                recipe.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="trades.accept-any",["Parameters"]=new JsonObject()}));
                GlobalEditorTemplateBridge.ValidateBatchReplay(recipe);
                check(true,version+" Gen6 trade snapshot supports accept-any Batch replay");
                optionsTrade["Offers"]=1;GlobalEditorTemplateBridge.Capture(recipe,editorTrade,new(optionsTrade,0),6);
                reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(recipe),version+" Batch blocks trade snapshot/action mismatch");
                recipe.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="trades.randomize-offers",["Parameters"]=new JsonObject()}));
                GlobalEditorTemplateBridge.ValidateBatchReplay(recipe);
                check(true,version+" Gen6 random offer snapshot supports Batch replay");
                apply.Invoke(null,new object[]{replay.Game.ExeFS,replay.Game,true});
                var after=File.ReadAllBytes(second.Path);var requests=version==GameVersion.ORAS?GameTrade6Service.ORASRequests:GameTrade6Service.XYRequests;
                var offers=version==GameVersion.ORAS?GameTrade6Service.ORASOffers:GameTrade6Service.XYOffers;
                check(requests.All(at=>BitConverter.ToUInt16(after,at)==0)&&offers.All(at=>BitConverter.ToUInt16(after,at)>0&&BitConverter.ToUInt16(after,at)<=replay.Game.MaxSpeciesID),version+" original Batch random offers retain accept-any and legal species");
            }
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            string egg=ModuleChecks.PathFor(f,"eggmove");f.WriteGarc(egg,Enumerable.Range(0,6).Select(_=>new byte[2]).ToArray(),f.Game.GetGARCReference("eggmove"));
            var editor=new GameEggMoveService(f.Session);var doc=GlobalTemplateDocument.Empty(version.ToString(),6);
            GlobalEditorTemplateBridge.Capture(doc,editor,new(ThreeDS.Progressive.Models.EditorValues.Flatten(editor.RandomOptions),0),6);
            doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="egg-moves.randomize",["Parameters"]=new JsonObject()}));
            string path=Path.Combine(f.Root,"batch.json");File.WriteAllText(path,doc.ToJson());var engine=new BatchRomExecution(f.Session);
            var options=new RomBatchOptions(path,Path.Combine(Path.GetTempPath(),"gen6-batch-check-"+Guid.NewGuid().ToString("N")),"test",1,false,false,false);
            byte[] before=File.ReadAllBytes(egg);var result=engine.Preflight(options);
            check(result.Template.Generation==6&&File.ReadAllBytes(egg).SequenceEqual(before)&&!Directory.Exists(options.OutputDirectory),version+" Batch preflight accepts Gen6 recipe without mutating or creating output");
            doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="static-encounters.totem-bst",["Parameters"]=new JsonObject()}));File.WriteAllText(path,doc.ToJson());
            bool blocked=false;try{engine.Preflight(options);}catch(NotSupportedException){blocked=true;}
            check(blocked,version+" Gen6 Batch rejects Gen7-only actions before staging");
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        var assembly=Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
        var main=assembly.GetType("pk3DS.WinForms.Main",true)!;var runtime=assembly.GetType("pk3DS.WinForms.BatchRuntime",true)!;
        var settings=assembly.GetType("pk3DS.WinForms.RandSettings",true)!;var templates=assembly.GetType("pk3DS.WinForms.TrainerRandomizerTemplateFile",true)!;
        object? oldConfig=main.GetField("Config",flags)!.GetValue(null);string oldDirectory=Environment.CurrentDirectory;
        var oldSettings=settings.GetMethod("Save",flags)!.Invoke(null,null);
        try
        {
            foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
            {
                using var f=new Fixture(version);ModuleChecks.CreateArchives(f);f.Game.InitializeLearnset();f.Game.InitializeEvos();
                try {
                Environment.CurrentDirectory=f.Root;main.GetField("Config",flags)!.SetValue(null,f.Game);
                templates.GetMethod("ClearCurrent",flags)!.Invoke(null,new object?[]{null});
                settings.GetMethod("Load",flags)!.Invoke(null,new object[]{new[]{"TrainerRand","CHK_RandomPKM\tFalse","CHK_Level\tTrue","NUD_Level\t2","CB_Moves\t0","CHK_StoryMEvos\tFalse","CHK_BetterMovesets\tFalse",""}});
                var before=ModuleChecks.Read(f,"trpoke");byte[] personal=File.ReadAllBytes(f.PersonalPath);
                using var context=(IDisposable)runtime.GetMethod("Begin",flags)!.Invoke(null,new object[]{new Action<string>(_=>{})})!;
                assembly.GetType("pk3DS.WinForms.BatchGen6ActionExecutor",true)!.GetMethod("RunTrainers",flags)!.Invoke(null,null);
                var after=ModuleChecks.Read(f,"trpoke");
                check(after.Skip(1).All(team=>BitConverter.ToUInt16(team,2)==20&&BitConverter.ToUInt16(team,20)==22),version+" packaged Gen6 Batch actually replays trainer level multiplier without dialogs");
                check(after.Skip(1).Zip(before.Skip(1)).All(pair=>BitConverter.ToUInt16(pair.First,4)==BitConverter.ToUInt16(pair.Second,4)&&BitConverter.ToUInt16(pair.First,22)==BitConverter.ToUInt16(pair.Second,22)),version+" Batch trainer option disables species randomization");
                check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(personal),version+" Batch trainer replay preserves personal archive");
                } finally {Environment.CurrentDirectory=oldDirectory;}
            }
        }
        finally
        {
            settings.GetMethod("Load",flags)!.Invoke(null,new[]{oldSettings});templates.GetMethod("ClearCurrent",flags)!.Invoke(null,new object?[]{null});
            main.GetField("Config",flags)!.SetValue(null,oldConfig);Environment.CurrentDirectory=oldDirectory;
        }
    }}
