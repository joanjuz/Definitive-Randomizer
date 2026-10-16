using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
internal static class Gen7FieldReplayChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var editor=new GameFieldItemsService(f.Session);
            var document=GlobalTemplateDocument.Empty(version.ToString(),7);document.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});GlobalEditorTemplateBridge.Capture(document,editor,new(EditorValues.Flatten(editor.RandomOptions),0),7);document.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="field-items.randomize",["Parameters"]=new JsonObject()}));
            foreach(string action in NativeGen7FieldBridge.ActionIds)
            {
                var missing=GlobalTemplateDocument.Empty(version.ToString(),7);missing.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});missing.SetSection("Actions",new JsonArray(new JsonObject{["Id"]=action,["Parameters"]=new JsonObject()}));
                check(!NativeRecipeCompletion.TryComplete(f.Session,missing)&&missing.Section("ProgressiveApp")!["PartialSnapshot"]!.GetValue<bool>(),version+" missing native snapshot remains partial: "+action);
                missing.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=false,["NativeOnly"]=true});reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(missing),version+" forged complete native snapshot rejected: "+action);
            }
            string path=ModuleChecks.PathFor(f,"encdata");byte[] original=File.ReadAllBytes(path),expected=editor.Preview(EditorValues.Flatten(editor.RandomOptions),editor.Entries.Select(row=>row.Id).ToArray(),48).Files[0].Proposed();
            check(NativeRecipeCompletion.TryComplete(f.Session,document)&&File.ReadAllBytes(path).SequenceEqual(original),version+" native Gen7 field snapshot completes without writing");NativeGen7FieldBridge.ExecuteSession(f.Session,document.ToJson(),48,"FieldItems");check(File.ReadAllBytes(path).SequenceEqual(expected),version+" native Gen7 field replay matches exact seed and filters");
            var partial=document.Clone();var native=partial.Section("ProgressiveApp")!;native["FieldItems"]!["Scope"]=1;partial.SetSection("ProgressiveApp",native);reject(()=>NativeGen7FieldBridge.ExecuteSession(f.Session,partial.ToJson(),1,"FieldItems"),version+" native Gen7 field replay rejects partial scope");
            var invalid=document.Clone();native=invalid.Section("ProgressiveApp")!;native["FieldItems"]!["Options"]!.AsObject().Remove("IncludeEB");invalid.SetSection("ProgressiveApp",native);reject(()=>NativeGen7FieldBridge.ExecuteSession(f.Session,invalid.ToJson(),1,"FieldItems"),version+" native Gen7 field replay rejects incomplete flags");
            var combined=NativeRecipeFactory.Create(f.Session,modules:["FieldItems","Items"]);check(NativeRecipeCompletion.TryComplete(f.Session,combined)&&combined.Generation==7&&combined.Actions.Contains("field-items.randomize")&&combined.Actions.Contains("items.fix-economy"),version+" native Gen7 recipe factory creates executable field and economy configuration");
            var items=new GameItemService(f.Session);document=GlobalTemplateDocument.Empty(version.ToString(),7);document.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});GlobalEditorTemplateBridge.Capture(document,items,new(EditorValues.Flatten(items.RandomOptions),0),7);document.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="items.fix-economy",["Parameters"]=new JsonObject()}));expected=items.PreviewEconomy(items.PriceRows()).Files[0].Proposed();
            check(NativeRecipeCompletion.TryComplete(f.Session,document),version+" native Gen7 economy snapshot completes");NativeGen7FieldBridge.ExecuteSession(f.Session,document.ToJson(),1,"Items");check(File.ReadAllBytes(ModuleChecks.PathFor(f,"item")).SequenceEqual(expected),version+" native Gen7 economy replay matches exact preset");
            invalid=document.Clone();native=invalid.Section("ProgressiveApp")!;native["Items"]!["Options"]!["PriceScale"]=200;invalid.SetSection("ProgressiveApp",native);reject(()=>NativeGen7FieldBridge.ExecuteSession(f.Session,invalid.ToJson(),1,"Items"),version+" economy rejects custom scale instead of ignoring it");
        }
    }
}
