using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class NativeGen7StaticChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var fixture=new Fixture(version);ModuleChecks.CreateArchives(fixture);var editor=new GameStaticEncounterService(fixture.Session);
            foreach(string action in NativeBatchGen7StaticBridge.ActionIds)
            {
                var absentSnapshot=GlobalTemplateDocument.Empty(version.ToString(),7);absentSnapshot.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});absentSnapshot.SetSection("Actions",new JsonArray(new JsonObject{["Id"]=action,["Parameters"]=new JsonObject()}));
                check(!NativeRecipeCompletion.TryComplete(fixture.Session,absentSnapshot),version+" absentSnapshot static snapshot never completes: "+action);
                absentSnapshot.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=false,["NativeOnly"]=true});reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(absentSnapshot),version+" forged native static recipe rejected: "+action);
            }
            var values=EditorValues.Flatten(editor.RandomOptions);values["Species"]=0;values["Gifts"]=0;values["Static"]=1;values["Trades"]=0;values["ScaleLevels"]=1;values["LevelScale"]=150;values["LevelFlat"]=7;
            var document=GlobalTemplateDocument.Empty(version.ToString(),7);document.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});
            GlobalEditorTemplateBridge.Capture(document,editor,new(values,0),7);document.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="static-encounters.modify-levels",["Parameters"]=new JsonObject()}));
            check(NativeRecipeCompletion.TryComplete(fixture.Session,document)&&document.Section("ProgressiveApp")!["PartialSnapshot"]!.GetValue<bool>()==false,version+" native Gen7 static snapshot becomes complete only after source and option validation");
            var incompleteLegacy=document.Clone();var extraActions=incompleteLegacy.Section("Actions")!.AsArray();extraActions.Add(new JsonObject{["Id"]="moves.randomize",["Parameters"]=new JsonObject()});incompleteLegacy.SetSection("Actions",extraActions);reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(incompleteLegacy),version+" completed native recipe rejects later legacy action with absent full settings");            GlobalEditorTemplateBridge.ValidateBatchReplay(document);string path=ModuleChecks.PathFor(fixture,"encounterstatic");byte[] source=File.ReadAllBytes(path);
            byte[] expected=editor.Preview(values,editor.Entries.Select(row=>row.Id).ToArray(),1234).Files.Single().Proposed();
            var assembly=System.Reflection.Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));var type=assembly.GetType("pk3DS.WinForms.GlobalRandomizationTemplate",true)!;
            string replay=JsonSerializer.Serialize(JsonSerializer.Deserialize(document.ToJson(),type),type);
            NativeBatchGen7StaticBridge.ExecuteSession(fixture.Session,replay,1234);
            check(File.ReadAllBytes(path).SequenceEqual(expected),version+" typed original template replays exact native static scale flat offsets and filters");
            var before=new pk3DS.Core.CTR.GARC.MemGARC(source);var after=new pk3DS.Core.CTR.GARC.MemGARC(File.ReadAllBytes(path));
            check(before.GetFile(0).SequenceEqual(after.GetFile(0))&&before.GetFile(4).SequenceEqual(after.GetFile(4)),version+" Gen7 static filters preserve excluded gifts and trades byte for byte");
            foreach(var (key,value) in new[]{("Scope",1),("Scope",2)})
            {
                var bad=document.Clone();var native=bad.Section("ProgressiveApp")!;native["StaticEncounters"]![key]=value;bad.SetSection("ProgressiveApp",native);
                reject(()=>NativeBatchGen7StaticBridge.ExecuteSession(fixture.Session,bad.ToJson(),1),version+" Gen7 static partial scope "+value+" rejected");
            }
            var missing=document.Clone();var snapshot=missing.Section("ProgressiveApp")!;snapshot["StaticEncounters"]!["Options"]!.AsObject().Remove("LevelFlat");missing.SetSection("ProgressiveApp",snapshot);reject(()=>NativeBatchGen7StaticBridge.ExecuteSession(fixture.Session,missing.ToJson(),1),version+" Gen7 static incomplete options rejected");
            var mixed=document.Clone();var actions=mixed.Section("Actions")!.AsArray();actions.Add(new JsonObject{["Id"]="trades.accept-any",["Parameters"]=new JsonObject()});mixed.SetSection("Actions",actions);reject(()=>NativeBatchGen7StaticBridge.ExecuteSession(fixture.Session,mixed.ToJson(),1),version+" Gen7 static snapshot rejects mixed legacy action rather than dropping it");
            check(File.ReadAllBytes(path).SequenceEqual(expected),version+" rejected Gen7 static recipes leave source unchanged");
            var records=new pk3DS.Core.CTR.GARC.MemGARC(File.ReadAllBytes(path)).Files;byte[] gift=records[0];records[0]=Enumerable.Range(0,4).SelectMany(_=>gift).ToArray();fixture.WriteGarc(path,records,fixture.Game.GetGARCReference("encounterstatic"));
            StarterTextChecks.CreateBanks(fixture,0);
            var starterEditor=new GameStaticEncounterService(fixture.Session);var starterOptions=EditorValues.Flatten(starterEditor.RandomOptions);starterOptions["Gifts"]=1;starterOptions["Static"]=0;starterOptions["Trades"]=0;starterOptions["Species"]=1;starterOptions["BasicStarter"]=1;
            var starters=GlobalTemplateDocument.Empty(version.ToString(),7);GlobalEditorTemplateBridge.Capture(starters,starterEditor,new(starterOptions,0),7);starters.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="starters.randomize",["Parameters"]=new JsonObject()}));
            byte[] expectedStarter=starterEditor.Preview(starterOptions,[1,2,3],987).Files.Single(file=>file.Path==path).Proposed();NativeBatchGen7StaticBridge.ExecuteSession(fixture.Session,starters.ToJson(),987);
            check(File.ReadAllBytes(path).SequenceEqual(expectedStarter),version+" native Gen7 starter action reproduces exact first-three scope and seed");
            check(new pk3DS.Core.CTR.GARC.MemGARC(File.ReadAllBytes(path)).GetFile(0).AsSpan(gift.Length*3,gift.Length).SequenceEqual(gift),version+" native Gen7 starter action preserves fourth gift");            var nativeRecipe=document.Clone();var restored=GlobalEditorTemplateBridge.Restore(nativeRecipe,new GameStaticEncounterService(fixture.Session),7)!;
            check(restored.Options["LevelFlat"]==7&&restored.Options["Gifts"]==0&&restored.Options["Trades"]==0,version+" Global Gen7 static import restores native-only values");
        }
    }
}
