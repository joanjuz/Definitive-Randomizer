using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class NativeGen7WildChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            // An unrelated compressed map member must remain opaque to the
            // encounter editor, even when its codec payload is not understood.
            string archivePath=ModuleChecks.PathFor(f,"encdata");var mapRecords=ModuleChecks.Read(f,"encdata");byte[] opaqueMap=[0x11,0xFF,0xFF,0xFF];mapRecords[0]=opaqueMap;f.WriteGarc(archivePath,mapRecords,f.Game.GetGARCReference("encdata"));
            var editor=new GameEncounterService(f.Session);editor.SplitLongestWildRange();var options=EditorValues.Flatten(editor.RandomOptions);options["UniquePerTable"]=1;options["RelaxBST"]=1;options["SlotMode"]=3;options["AllCanCallAllies"]=1;options["LevelScale"]=140;options["LevelFlat"]=3;
            var template=JsonNode.Parse(editor.CaptureOptions(options))!;template["FutureWildSetting"]="preserved";editor.RestoreOptions(template.ToJsonString());string captured=editor.CaptureOptions(options);
            var doc=GlobalTemplateDocument.Empty(f.Session.GameName,7);doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});GlobalEditorTemplateBridge.Capture(doc,editor,new(options,0,captured),7);doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]=NativeGen7WildBridge.ActionId(options),["Parameters"]=new JsonObject()}));
            byte[] original=File.ReadAllBytes(ModuleChecks.PathFor(f,"encdata"));var expected=editor.Preview(options,editor.Entries.Select(row=>row.Id).ToArray(),432).Files.Single().Proposed();check(NativeRecipeCompletion.TryComplete(f.Session,doc)&&File.ReadAllBytes(ModuleChecks.PathFor(f,"encdata")).SequenceEqual(original),version+" complete native Wild recipe validates without writing");
            var restored=GlobalEditorTemplateBridge.Restore(doc,new GameEncounterService(f.Session),7)!;check(restored.Options.Count==options.Count&&restored.Options.All(p=>options[p.Key]==p.Value)&&restored.Template!.Contains("FutureWildSetting"),version+" Global preserves dynamic BST ranges SOS levels and unknown Wild metadata");
            var assembly=System.Reflection.Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));var type=assembly.GetType("pk3DS.WinForms.GlobalRandomizationTemplate",true)!;string replay=JsonSerializer.Serialize(JsonSerializer.Deserialize(doc.ToJson(),type),type);NativeGen7WildBridge.ExecuteSession(f.Session,replay,432);check(File.ReadAllBytes(ModuleChecks.PathFor(f,"encdata")).SequenceEqual(expected),version+" original typed template replays exact native Wild seed and copy SOS options");check(new GARC.MemGARC(File.ReadAllBytes(archivePath)).GetFile(0).SequenceEqual(opaqueMap),version+" encounter replay leaves unrelated compressed map members byte-identical without decoding them");
            var invalid=doc.Clone();var native=invalid.Section("ProgressiveApp")!;native["Wild"]!["Options"]!.AsObject().Remove("Weather");invalid.SetSection("ProgressiveApp",native);reject(()=>NativeGen7WildBridge.ExecuteSession(f.Session,invalid.ToJson(),1),version+" incomplete Wild options rejected");
            invalid=doc.Clone();native=invalid.Section("ProgressiveApp")!;native["Wild"]!["Scope"]=1;invalid.SetSection("ProgressiveApp",native);reject(()=>NativeGen7WildBridge.ExecuteSession(f.Session,invalid.ToJson(),1),version+" partial Wild scope rejected for Batch");check(GlobalEditorTemplateBridge.Restore(invalid,new GameEncounterService(f.Session),7)!.Scope==1,version+" partial Wild scope remains available for UI restoration");
            invalid=doc.Clone();native=invalid.Section("ProgressiveApp")!;native["Wild"]!.AsObject().Remove("Template");invalid.SetSection("ProgressiveApp",native);reject(()=>NativeGen7WildBridge.ExecuteSession(f.Session,invalid.ToJson(),1),version+" missing Wild template rejected");
            editor=new GameEncounterService(f.Session);options=EditorValues.Flatten(editor.RandomOptions);options["LevelScale"]=400;GlobalEditorTemplateBridge.Capture(doc,editor,new(options,0),7);restored=GlobalEditorTemplateBridge.Restore(doc,new GameEncounterService(f.Session),7)!;check(restored.Options["LevelScale"]==400,version+" native Global preserves extended level scale beyond v1 template limits");
        }
    }
}
