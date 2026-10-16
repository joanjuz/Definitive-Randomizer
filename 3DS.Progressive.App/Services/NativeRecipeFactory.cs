using System.Text.Json.Nodes;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal static class NativeRecipeFactory
{
    internal static GlobalTemplateDocument Create(GameProjectSession project,Action<GlobalTemplateDocument>? capture=null,IEnumerable<string>? modules=null)
    {
        if(project.Generation is not (6 or 7))throw new System.IO.InvalidDataException("La receta nativa requiere XY/ORAS/SM/USUM.");
        var document=GlobalTemplateDocument.Empty(project.GameName,project.Generation);
        document.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});
        var selected=(modules??(project.Generation==6?new[]{"StaticEncounters","Wild","Machines","FieldItems","Marts","Items"}:new[]{"StaticEncounters","FieldItems","Items","Machines","Wild"})).ToHashSet();
        if(modules is null&&project.GameConfiguration.ORAS)selected.Add("Tutors");
        if(modules is null&&project.Generation==7)selected.Add("Marts");
        if(modules is null&&project.GameConfiguration.USUM)selected.Add("Tutors");
        if(project.Generation==7)
        {
            if(selected.Contains("Machines")||selected.Contains("Tutors"))NativeGen7MachineBridge.Capture(document,new(RandomMachines:selected.Contains("Machines"),RandomTutors:selected.Contains("Tutors")));
            if(selected.Contains("Marts"))NativeGen7MartBridge.Capture(document,new(RandomRegular:true));
        }
        foreach(var row in GlobalEditorTemplateBridge.Editors(project).Where(row=>selected.Contains(row.Key)))
        {
            var editor=row.Create();GlobalEditorTemplateBridge.Capture(document,editor,new RecordEditorView.UiState(EditorValues.Flatten(editor.RandomOptions),0),project.Generation);
        }
        if(modules is null)
        {
            var shiny=new GameShinyRateService(project).Read();ShinyRateTemplate.Capture(document,new(shiny.Rerolls,shiny.AlwaysShiny));
        }
        // Remembered user settings take precedence over freshly initialized defaults.
        capture?.Invoke(document);
        if(project.Generation==6&&document.Section("ProgressiveApp")?["Trades"] is not null)selected.Add("Trades");
        var actions=(JsonArray)document.Section("Actions")!;
        foreach(string key in selected)
        {
            if(project.Generation==7&&key is ("Machines" or "Tutors" or "Marts"))continue;
            var options=document.Section("ProgressiveApp")?[key]?["Options"];
            if(key=="Marts"&&project.Generation==6&&options?["UseChecks"]?.GetValue<int>()==1)
            {
                string[] ids=["marts.randomize","marts.add-rare-candies","marts.add-ev-items","marts.ban-ability-capsule"];string[] flags=["Randomize","Candies","EVItems","BanCapsule"];
                foreach(var row in actions.Where(row=>ids.Contains(row?["Id"]?.GetValue<string>())).ToArray())actions.Remove(row);
                for(int i=0;i<ids.Length;i++)if(options[flags[i]]?.GetValue<int>()==1)actions.Add(new JsonObject{["Id"]=ids[i],["Parameters"]=new JsonObject()});
                continue;
            }
            string id=key switch
            {
                "StaticEncounters" when project.Generation==6&&options?["SeparateProfiles"]?.GetValue<int>()==1=>"static-encounters.randomize",
                "StaticEncounters"=>options?["Species"]?.GetValue<int>()==0&&(project.Generation==7||options?["Forms"]?.GetValue<int>()==0)&&options?["ScaleLevels"]?.GetValue<int>()==1?"static-encounters.modify-levels":"static-encounters.randomize",
                "Wild" when project.Generation==7=>NativeGen7WildBridge.ActionId(options!.AsObject().ToDictionary(p=>p.Key,p=>p.Value!.GetValue<int>())),
                "Wild"=>options?["Species"]?.GetValue<int>()==0&&options?["ModifyLevel"]?.GetValue<int>()==1?"wild-encounters.modify-levels":"wild-encounters.randomize",
                "Trades"=>options?["Offers"]?.GetValue<int>()==1?"trades.randomize-offers":"trades.accept-any",
                "Tutors"=>"move-tutors.randomize","Machines"=>"tms.randomize","FieldItems"=>"field-items.randomize",
                "Marts"=>options?["Operation"]?.GetValue<int>()==1?"marts.add-rare-candies":"marts.randomize",
                "Items"=>"items.fix-economy",_=>throw new System.IO.InvalidDataException("Módulo de receta no admitido: "+key)
            };
            if(!actions.Any(row=>row?["Id"]?.GetValue<string>()==id))actions.Add(new JsonObject{["Id"]=id,["Parameters"]=new JsonObject()});
        }
        if(modules is null&&project.Generation==6)foreach(string id in new[]{"pickup.randomize","catchzones.enable"})actions.Add(new JsonObject{["Id"]=id,["Parameters"]=new JsonObject()});
        if(modules is null&&project.Generation==7)actions.Add(new JsonObject{["Id"]="pickup.randomize",["Parameters"]=new JsonObject()});
        foreach(string module in new[]{"Moves","Personal"})
            if(NativeMasterBatchBridge.Read(document,module) is {} state&&!actions.Any(row=>row?["Id"]?.GetValue<string>()==NativeMasterBatchBridge.ActionId(state)))actions.Add(new JsonObject{["Id"]=NativeMasterBatchBridge.ActionId(state),["Parameters"]=new JsonObject()});
        document.SetSection("Actions",actions);
        return document;
    }
}
