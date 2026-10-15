using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

internal static class MasterBatchChecks
{
    internal static void RunUi(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var fixture=new Fixture(version);ModuleChecks.CreateArchives(fixture);var main=new ThreeDS.Progressive.MainWindow();main.LoadProject(fixture.Session);
            try
            {
                if(fixture.Game.Generation==7)
                {
                    byte[] code=new byte[0x402000];byte[] signature=[0x03,0x40,0x03,0x41,0x03,0x42,0x03,0x43,0x03];signature.CopyTo(code,0x400000);int offset=0x400000+signature.Length+(version==GameVersion.USUM?0x22:0);for(int i=0;i<100;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(code,offset+i*2);File.WriteAllBytes(Path.Combine(fixture.Game.ExeFS,"code.bin"),code);
                    var view=new ThreeDS.Progressive.MachineTutorView(fixture.Session);var remembered=new Gen7MachineState(RandomMachines:true,MachineSanity:true,MachineFollow:true);view.RestoreRecipe(remembered);check(view.RememberRecipe()==remembered,version+" WPF machine recipe options restore exactly");
                    var state=new Gen7MachineState();var global=new ThreeDS.Progressive.GlobalTemplateView(fixture.Session,captureOptions:doc=>NativeGen7MachineBridge.Capture(doc,state));var loaded=GlobalTemplateDocument.Empty(version.ToString(),7);typeof(ThreeDS.Progressive.GlobalTemplateView).GetMethod("Use",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(global,[loaded,null]);state=new(MachineSanity:true);var newlyEnabled=global.ExportSnapshot();check(newlyEnabled.Actions.Contains("tms.sanity")&&NativeGen7MachineBridge.Read(newlyEnabled)!.MachineSanity,version+" Global export includes newly enabled editor action absent from old action list");
                    var recipe=GlobalTemplateDocument.Empty(version.ToString(),7);NativeGen7MachineBridge.Capture(recipe,view.RememberRecipe());check(NativeGen7MachineBridge.Read(recipe)==remembered,version+" WPF machine recipe capture preserves all settings");
                }
                var input=GlobalTemplateDocument.Empty(version.ToString(),fixture.Game.Generation);NativeMasterBatchBridge.Capture(input,new(BatchAction.PersonalRandomize,new BatchSettings{Stats=false,Deviation=17,SingleTypeChance=67},1),fixture.Game.Generation);NativeMasterBatchBridge.Capture(input,new(BatchAction.MoveRandomize,new BatchSettings{MoveTypes=true,MoveCategories=false},0),fixture.Game.Generation);
                var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;typeof(ThreeDS.Progressive.MainWindow).GetMethod("RestoreGlobalOptions",flags)!.Invoke(main,[input]);var output=GlobalTemplateDocument.Empty(version.ToString(),fixture.Game.Generation);typeof(ThreeDS.Progressive.MainWindow).GetMethod("CaptureGlobalOptions",flags)!.Invoke(main,[output]);
                var personal=NativeMasterBatchBridge.Read(output,"Personal")!;var moves=NativeMasterBatchBridge.Read(output,"Moves")!;
                check(personal.Scope==1&&!personal.Settings.Stats&&personal.Settings.Deviation==17&&personal.Settings.SingleTypeChance==67,version+" WPF Global preserves scope and inactive numeric personal options");
                check(moves.Action==BatchAction.MoveRandomize&&moves.Settings.MoveTypes&&!moves.Settings.MoveCategories,version+" WPF Global restores and captures move options");
                var tool=(ThreeDS.Progressive.BatchToolsView)typeof(ThreeDS.Progressive.MainWindow).GetField("_personalTools",flags)!.GetValue(main)!;tool.OpenAction(BatchAction.PersonalModify);
                var checks=(System.Collections.IDictionary)typeof(ThreeDS.Progressive.BatchToolsView).GetField("_checks",flags)!.GetValue(tool)!;
                check(checks.Contains("PersonalTemplate")&&checks.Contains("SetCall")== (fixture.Game.Generation==7)&&checks.Contains("FullHms")== (fixture.Game.Generation==6),version+" WPF personal templates work in both generations while SOS and HM remain generation-specific");
                var native=output.Section("ProgressiveApp")!;native["MasterBatches"]!["Personal"]!["FutureSnapshotMetadata"]="keep";output.SetSection("ProgressiveApp",native);NativeMasterBatchBridge.Capture(output,personal,fixture.Game.Generation);check(output.Section("ProgressiveApp")!["MasterBatches"]!["Personal"]!["FutureSnapshotMetadata"]!.GetValue<string>()=="keep",version+" native Master capture preserves unknown snapshot metadata");
            }
            finally{main.Close();}
        }
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var fixture=new Fixture(version);ModuleChecks.CreateArchives(fixture);
            var document=GlobalTemplateDocument.Empty(version.ToString(),fixture.Game.Generation);document.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});
            var settings=new BatchSettings{Stats=true,StatSlots=[true,false,true,false,true,false],Deviation=13,Types=true,SingleTypeChance=81,Tms=true,Hms=fixture.Game.Generation==6};
            var state=new MasterBatchState(BatchAction.PersonalRandomize,settings,0);NativeMasterBatchBridge.Capture(document,state,fixture.Game.Generation);
            document.SetSection("Actions",new JsonArray(new JsonObject{["Id"]=NativeMasterBatchBridge.ActionId(state),["Parameters"]=new JsonObject()}));
            byte[] source=File.ReadAllBytes(fixture.PersonalPath);var expected=fixture.Session.PreviewPersonal(state.Action,settings with{Seed=321},fixture.Session.Entries.Select(row=>row.Index).ToArray()).CopyRecords();
            check(NativeRecipeCompletion.TryComplete(fixture.Session,document)&&File.ReadAllBytes(fixture.PersonalPath).SequenceEqual(source),version+" complete native personal recipe validates without writing");
            var assembly=System.Reflection.Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));var type=assembly.GetType("pk3DS.WinForms.GlobalRandomizationTemplate",true)!;string replay=JsonSerializer.Serialize(JsonSerializer.Deserialize(document.ToJson(),type),type);
            NativeMasterBatchBridge.ExecuteSession(fixture.Session,replay,321,"Personal");check(fixture.PersonalRecords.Zip(expected).All(pair=>pair.First.SequenceEqual(pair.Second)),version+" typed original Global replays exact selected stat slots deviation types HM flags and seed");
            var moveService=new GameMoveService(fixture.Session);settings=new BatchSettings{MoveTypes=true,MoveCategories=true};state=new(BatchAction.MoveRandomize,settings,0);NativeMasterBatchBridge.Capture(document,state,fixture.Game.Generation);document.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="moves.randomize",["Parameters"]=new JsonObject()}));
            byte[] personalBefore=File.ReadAllBytes(fixture.PersonalPath);var expectedMoves=moveService.PreviewMoves(state.Action,settings with{Seed=819},moveService.Entries.Select(row=>row.Id).ToArray()).CopyRecords();NativeMasterBatchBridge.ExecuteSession(fixture.Session,document.ToJson(),819,"Moves");
            check(fixture.MoveRecords.Zip(expectedMoves).All(pair=>pair.First.SequenceEqual(pair.Second)),version+" native move replay preserves exact type category options and seed");check(File.ReadAllBytes(fixture.PersonalPath).SequenceEqual(personalBefore),version+" native move replay preserves unrelated personal archive");
            var incomplete=document.Clone();var native=incomplete.Section("ProgressiveApp")!;native["MasterBatches"]!["Moves"]!["Settings"]!.AsObject().Remove("MoveTypes");incomplete.SetSection("ProgressiveApp",native);reject(()=>NativeMasterBatchBridge.ExecuteSession(fixture.Session,incomplete.ToJson(),1,"Moves"),version+" incomplete move snapshot rejected");
            var partial=document.Clone();native=partial.Section("ProgressiveApp")!;native["MasterBatches"]!["Moves"]!["Scope"]=1;partial.SetSection("ProgressiveApp",native);reject(()=>NativeMasterBatchBridge.ExecuteSession(fixture.Session,partial.ToJson(),1,"Moves"),version+" partial move scope rejected for ROM replay");NativeMasterBatchBridge.ValidateSession(fixture.Session,partial,forReplay:false);check(NativeMasterBatchBridge.Read(partial,"Moves")!.Scope==1,version+" partial move scope retained for UI restoration");
            string csv=Path.Combine(fixture.Root,"custom.csv");File.WriteAllText(csv,"Move,Power,Accuracy,PP\n1,99,88,12\n");state=new(BatchAction.MoveBalance,new BatchSettings{TemplatePath=csv,BattlePatches=false,MoveDescriptions=false},0);NativeMasterBatchBridge.Capture(document,state,fixture.Game.Generation);document.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="moves.balance",["Parameters"]=new JsonObject()}));
            byte[] beforeBalance=File.ReadAllBytes(fixture.MovePath);
            if(version==GameVersion.SM)reject(()=>NativeMasterBatchBridge.ExecuteSession(fixture.Session,document.ToJson(),1,"Moves"),"SM native replay rejects custom Progressive balance");
            else NativeMasterBatchBridge.ExecuteSession(fixture.Session,document.ToJson(),1,"Moves");
            check(version==GameVersion.SM?File.ReadAllBytes(fixture.MovePath).SequenceEqual(beforeBalance):new GameMoveService(fixture.Session).Read(1).Power==99,version+" balance replay respects game scope and preserves rejected input");File.AppendAllText(csv,"2,123,88,12\n");reject(()=>{if(version==GameVersion.SM)_=NativeMasterBatchBridge.Read(document,"Moves");else NativeMasterBatchBridge.ExecuteSession(fixture.Session,document.ToJson(),1,"Moves");},version+" modified custom CSV rejected by fingerprint");
            string statsCsv=Path.Combine(fixture.Root,"custom-stats.csv");File.WriteAllText(statsCsv,"Entry,Pokemon,HP,ATK,DEF,SPA,SPD,SPE,Notes\n1,,120,110,100,90,80,70,Known stats\n");
            var statState=new MasterBatchState(BatchAction.PersonalModify,new BatchSettings{PersonalTemplate=true,TemplatePath=statsCsv},0);NativeMasterBatchBridge.Capture(document,statState,fixture.Game.Generation);document.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="pokemon-stats.apply",["Parameters"]=new JsonObject()}));var beforeStats=fixture.PersonalRecords;NativeMasterBatchBridge.ExecuteSession(fixture.Session,document.ToJson(),1,"Personal");var afterStats=fixture.PersonalRecords;
            check(afterStats[1].Take(6).SequenceEqual(new byte[]{120,110,100,70,90,80}),version+" native stats CSV updates HP ATK DEF SPE SPA SPD in correct order");
            check(beforeStats[1].Skip(6).SequenceEqual(afterStats[1].Skip(6))&&beforeStats.Skip(2).Take(beforeStats.Length-3).Zip(afterStats.Skip(2)).All(pair=>pair.First.SequenceEqual(pair.Second)),version+" native stats CSV preserves opaque bytes and other species");
        }
    }
}
