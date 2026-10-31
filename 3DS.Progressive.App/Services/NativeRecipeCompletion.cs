using System.IO;
using System.Text.Json.Nodes;

namespace ThreeDS.Progressive.Services;

internal static class NativeRecipeCompletion
{
    internal static readonly string[] Gen7ParameterActions=["shiny-rate.apply","pickup.randomize"];
    internal static void ValidateGen7Parameters(GameProjectSession project,GlobalTemplateDocument doc)
    {
        if(project.Generation!=7)return;
        if(ShinyRateTemplate.Read(doc) is {} shiny)_=new GameShinyRateService(project).Preview(shiny.Rerolls,shiny.Everything);
        if(doc.Actions.Contains("pickup.randomize",StringComparer.OrdinalIgnoreCase)){ValidatePickupParameters(doc);var editor=new GamePickupService(project);_=editor.Preview(editor.Randomize(editor.Read(),1));}
    }
    internal static void ValidatePickupParameters(GlobalTemplateDocument doc)
    {
        if(doc.Generation!=7||!doc.Actions.Contains("pickup.randomize",StringComparer.OrdinalIgnoreCase))return;
        if(doc.Section("ProgressiveApp")?["Pickup"] is not null)throw new InvalidDataException("Pickup nativo reproduce objetos y conserva las probabilidades de la ROM; no admite otro snapshot.");
        var actions=doc.Section("Actions")!.AsArray();var action=actions.Single(row=>row?["Id"]?.GetValue<string>().Equals("pickup.randomize",StringComparison.OrdinalIgnoreCase)==true);
        if(action?["Parameters"] is not JsonObject parameters||parameters.Count!=0)throw new InvalidDataException("Pickup randomize no admite parámetros adicionales.");
    }
    // A history snapshot is complete only for the actions actually selected,
    // and only when the native engine can validate all configuration and sources.
    internal static bool TryComplete(GameProjectSession project,GlobalTemplateDocument document)
    {
        if(document.Section("ProgressiveApp")?["PartialSnapshot"]?.GetValue<bool>()!=true)return false;
        var allowed=(project.Generation==6?NativeBatchGen6Bridge.ActionIds:NativeBatchGen7StaticBridge.ActionIds).Concat(NativeMasterBatchBridge.ActionIds).Append(ManualEditTemplate.ActionId).Concat(project.Generation==7?NativeGen7FieldBridge.ActionIds:[]).Concat(project.Generation==7?NativeGen7MachineBridge.ActionIds:[]).Concat(project.Generation==7?NativeGen7MartBridge.ActionIds:[]).Concat(project.Generation==7?Gen7ParameterActions:[]).Concat(project.Generation==7?NativeGen7WildBridge.ActionIds:[]).Concat(project.Generation==7?Gen7Adjustments.ActionIds(project.GameConfiguration.USUM):[]).ToArray();
        if(project.Generation is not (6 or 7)||document.Actions.Count==0||document.Actions.All(id=>id=="rng.seed")||document.Actions.Any(id=>id!="rng.seed"&&!allowed.Contains(id,StringComparer.OrdinalIgnoreCase))||!HasAllSelectedSnapshots(document))return false;
        ManualEditTemplate.ValidateSession(project,document);
        NativeGen7WildBridge.ValidateSession(project,document);ValidateGen7Parameters(project,document);NativeMasterBatchBridge.ValidateSession(project,document);
        if(project.Generation==6)NativeBatchGen6Bridge.ValidateSession(project,document);else {NativeBatchGen7StaticBridge.ValidateSession(project,document);NativeGen7FieldBridge.ValidateSession(project,document);NativeGen7MachineBridge.ValidateSession(project,document);_=NativeGen7MartBridge.ValidateSession(project,document);}
        if(document.CheckAssets(new[]{AppContext.BaseDirectory,project.ProjectRoot,Path.GetDirectoryName(document.SourcePath)??""}).Count!=0)
            throw new InvalidDataException("La receta contiene assets ausentes o distintos; no se marcó completa.");
        var native=(JsonObject)document.Section("ProgressiveApp")!;
        native["PartialSnapshot"]=false;native["NativeOnly"]=true;document.SetSection("ProgressiveApp",native);return true;
    }
    internal static bool HasAllSelectedSnapshots(GlobalTemplateDocument document)
    {
        ManualEditTemplate.Validate(document);
        if(!NativeMasterBatchBridge.HasAllSelectedSnapshots(document))return false;
        if(!NativeGen7WildBridge.HasAllSelectedSnapshots(document))return false;
        if(!NativeGen7MartBridge.HasAllSelectedSnapshots(document))return false;
        if(!NativeGen7MachineBridge.HasAllSelectedSnapshots(document))return false;
        if(document.Generation==6&&document.Actions.Any(id=>new[]{"trades.accept-any","trades.randomize-offers"}.Contains(id,StringComparer.OrdinalIgnoreCase))&&document.Section("ProgressiveApp")?["Trades"] is null)return false;
        if(document.Generation!=7)return true;
        _=ShinyRateTemplate.Read(document);ValidatePickupParameters(document);
        if(document.Actions.Any(id=>NativeBatchGen7StaticBridge.ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase))&&!NativeBatchGen7StaticBridge.Selected(document))return false;
        foreach(string id in NativeGen7FieldBridge.ActionIds)
            if(document.Actions.Contains(id,StringComparer.OrdinalIgnoreCase)&&!NativeGen7FieldBridge.Selected(document,id=="field-items.randomize"?"FieldItems":"Items"))return false;
        return true;
    }
    internal static void ReconcileSelection(GlobalTemplateDocument document)
    {
        var native=document.Section("ProgressiveApp") as JsonObject;
        if(native is null)return;
        foreach(var (key,sanity,follow) in new[]{("MachineCompatibility","tms.sanity","tms.follow-evolutions"),("TutorCompatibility","move-tutors.sanity","move-tutors.follow-evolutions")})
            if(native[key] is JsonObject compatibility)compatibility["Options"]=new JsonObject{["Sanity"]=document.Actions.Contains(sanity,StringComparer.OrdinalIgnoreCase)?1:0,["Follow"]=document.Actions.Contains(follow,StringComparer.OrdinalIgnoreCase)?1:0};
        if(native["Gen7Machines"]?["Options"] is JsonObject machineOptions)foreach(string id in NativeGen7MachineBridge.ActionIds)machineOptions[id]=document.Actions.Contains(id,StringComparer.OrdinalIgnoreCase)?1:0;
        if(native["Gen7Marts"]?["Options"] is JsonObject martOptions)foreach(string id in NativeGen7MartBridge.ActionIds)martOptions[id]=document.Actions.Contains(id,StringComparer.OrdinalIgnoreCase)?1:0;
        document.SetSection("ProgressiveApp",native);
    }
}
