using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

internal static class Gen7RecipeChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);
            foreach(string id in NativeGen7MachineBridge.ActionIds.Concat(NativeGen7MartBridge.ActionIds))
            {
                var doc=GlobalTemplateDocument.Empty(version.ToString(),7);doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]=id,["Parameters"]=new JsonObject()}));
                check(!NativeRecipeCompletion.TryComplete(f.Session,doc),version+" incomplete native machine/shop snapshot remains partial: "+id);
                doc.SetSection("ProgressiveApp",new JsonObject());reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(doc),version+" action-only recipe cannot bypass native snapshot validation: "+id);
                doc.SetSection("ProgressiveApp",new JsonObject{["NativeOnly"]=true});reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(doc),version+" forged complete machine/shop recipe rejected: "+id);
            }
            var template=GlobalTemplateDocument.Empty(version.ToString(),7);var state=new Gen7MartState(true,true,true,true,true,true,true,false);NativeGen7MartBridge.Capture(template,state);check(NativeGen7MartBridge.Read(template)==state,version+" shop recipe preserves all preset and filter options");NativeGen7MartBridge.ValidateDocument(template);
            var metadata=template.Section("ProgressiveApp")!;metadata["Gen7Marts"]!["FutureSetting"]="preserved";template.SetSection("ProgressiveApp",metadata);NativeGen7MartBridge.Capture(template,state);check(template.Section("ProgressiveApp")!["Gen7Marts"]!["FutureSetting"]!.GetValue<string>()=="preserved",version+" recapturing shop options preserves snapshot metadata");
            var machines=GlobalTemplateDocument.Empty(version.ToString(),7);NativeGen7MachineBridge.Capture(machines,new(RandomMachines:true));metadata=machines.Section("ProgressiveApp")!;metadata["Gen7Machines"]!["FutureSetting"]="preserved";machines.SetSection("ProgressiveApp",metadata);NativeGen7MachineBridge.Capture(machines,new(MachineSanity:true));check(machines.Section("ProgressiveApp")!["Gen7Machines"]!["FutureSetting"]!.GetValue<string>()=="preserved",version+" recapturing machine options preserves snapshot metadata");
            template.SelectActions(new HashSet<string>{"marts.randomize","marts.add-rare-candies"});NativeRecipeCompletion.ReconcileSelection(template);NativeGen7MartBridge.ValidateDocument(template);check(NativeGen7MartBridge.Read(template)==new Gen7MartState(RandomRegular:true,Candies:true,SpecialsOnly:true,ProtectX:false),version+" shop action deselection preserves filters and reconciles presets");
            var invalid=template.Clone();var native=invalid.Section("ProgressiveApp")!;native["Gen7Marts"]!["Options"]!.AsObject().Remove("ProtectX");invalid.SetSection("ProgressiveApp",native);reject(()=>NativeGen7MartBridge.Read(invalid),version+" missing shop filter rejected rather than defaulted");
            invalid=template.Clone();var actions=invalid.Section("Actions")!.AsArray();actions.Add(new JsonObject{["Id"]="marts.expanded-layout",["Parameters"]=new JsonObject()});invalid.SetSection("Actions",actions);reject(()=>NativeGen7MartBridge.ValidateDocument(invalid),version+" original expanded profile cannot be silently mixed with native shop options");
            if(version==GameVersion.SM)reject(()=>NativeGen7MartBridge.ValidateSession(f.Session,template),"SM rejects shop recipe when source CRO is absent");
        }
        var gen6=GlobalTemplateDocument.Empty("XY",6);reject(()=>NativeGen7MartBridge.Capture(gen6,new()),"Gen6 rejects USUM shop recipe");reject(()=>NativeGen7MachineBridge.Capture(gen6,new()),"Gen6 rejects Gen7 machine recipe");
    }
}
