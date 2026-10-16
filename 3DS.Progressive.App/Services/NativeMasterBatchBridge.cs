using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;
internal sealed record MasterBatchState(BatchAction Action,BatchSettings Settings,int Scope);
public static class NativeMasterBatchBridge
{
    internal static readonly string[] ActionIds=["moves.randomize","moves.metronome","moves.balance","personal.randomize","personal.modify-all","pokemon-stats.apply"];
    internal static string ActionId(MasterBatchState state)=>state.Action switch
    {BatchAction.MoveRandomize=>"moves.randomize",BatchAction.Metronome=>"moves.metronome",BatchAction.MoveBalance=>"moves.balance",BatchAction.PersonalRandomize=>"personal.randomize",BatchAction.PersonalModify=>state.Settings.PersonalTemplate?"pokemon-stats.apply":"personal.modify-all",_=>throw new InvalidDataException("Acción nativa no admitida.")};
    internal static bool Selected(GlobalTemplateDocument document,string module)=>document.Section("ProgressiveApp")?["MasterBatches"]?[module] is JsonObject&&document.Actions.Any(id=>Group(id)==module);
    private static string? Group(string id)=>id.ToLowerInvariant() switch
    {"moves.randomize" or "moves.metronome" or "moves.balance"=>"Moves","personal.randomize" or "personal.modify-all" or "pokemon-stats.apply"=>"Personal",_=>null};
    internal static void Capture(GlobalTemplateDocument document,MasterBatchState state,int generation)
    {
        string module=Group(ActionId(state))!;
        var settings=state.Settings with{Seed=0};string? hash=null;
        if(state.Action==BatchAction.MoveBalance||settings.PersonalTemplate)
        {
            string path=Path.GetFullPath(settings.TemplatePath??Path.Combine(AppContext.BaseDirectory,"custom_balance_templates",state.Action==BatchAction.MoveBalance?$"moves_gen{generation}.csv":$"pokemon_stats_gen{generation}.csv"));
            using var stream=File.OpenRead(path);hash=Convert.ToHexString(SHA256.HashData(stream));
        }
        var native=document.Section("ProgressiveApp") as JsonObject??new();var batches=native["MasterBatches"] as JsonObject??new();
        var snapshot=batches[module] as JsonObject??new();var options=snapshot["Settings"] as JsonObject??new();foreach(var pair in JsonSerializer.SerializeToNode(settings)!.AsObject())options[pair.Key]=pair.Value?.DeepClone();snapshot["ActionId"]=ActionId(state);snapshot["Scope"]=state.Scope;snapshot["Settings"]=options;snapshot["TemplateSha256"]=hash;batches[module]=snapshot;native["MasterBatches"]=batches;document.SetSection("ProgressiveApp",native);
    }
    internal static MasterBatchState? Read(GlobalTemplateDocument document,string module)
    {
        if(document.Section("ProgressiveApp")?["MasterBatches"]?[module] is not JsonObject node)return null;
        string id=node["ActionId"]?.GetValue<string>()??"";if(Group(id)!=module)throw new InvalidDataException("Acción de snapshot incompatible: "+module);
        if(node["Settings"] is not JsonObject settings||!settings.Select(pair=>pair.Key).ToHashSet().SetEquals(typeof(BatchSettings).GetProperties().Select(property=>property.Name)))throw new InvalidDataException("Configuración nativa incompleta o desconocida: "+module);
        var value=settings.Deserialize<BatchSettings>(new JsonSerializerOptions{UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow})??throw new InvalidDataException("Opciones vacías.");if(value.StatSlots is null)throw new InvalidDataException("Faltan slots de estadísticas.");BatchAlgorithms.Validate(value);
        BatchAction action=id switch{"moves.randomize"=>BatchAction.MoveRandomize,"moves.metronome"=>BatchAction.Metronome,"moves.balance"=>BatchAction.MoveBalance,"personal.randomize"=>BatchAction.PersonalRandomize,_=>BatchAction.PersonalModify};
        if((id=="pokemon-stats.apply")!=value.PersonalTemplate&&module=="Personal")throw new InvalidDataException("La acción no coincide con el template Personal.");
        if(action==BatchAction.MoveBalance||value.PersonalTemplate)
        {
            if(node["TemplateSha256"]?.GetValue<string>() is not {} expected)throw new InvalidDataException("Falta el CSV o su SHA-256.");
            string path=Path.GetFullPath(value.TemplatePath??Path.Combine(AppContext.BaseDirectory,"custom_balance_templates",action==BatchAction.MoveBalance?$"moves_gen{document.Generation}.csv":$"pokemon_stats_gen{document.Generation}.csv"));
            using var stream=File.OpenRead(path);if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("El CSV cambió desde la captura nativa.");
        }
        int scope=node["Scope"]?.GetValue<int>()??-1;if(scope is <0 or >2)throw new InvalidDataException("Alcance nativo inválido.");return new(action,value,scope);
    }
    internal static bool HasAllSelectedSnapshots(GlobalTemplateDocument document)=>document.Actions.Where(id=>ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase)).All(id=>Selected(document,Group(id)!));
    internal static void ValidateDocument(GlobalTemplateDocument document)
    {
        foreach(string module in new[]{"Moves","Personal"})if(Selected(document,module))
        {
            var state=Read(document,module)!;var actions=document.Actions.Where(id=>Group(id)==module).ToArray();
            if(state.Scope!=0||actions.Length!=1||!actions[0].Equals(ActionId(state),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Batch nativo requiere alcance Todos y una acción coincidente por módulo: "+module);
        }
    }
    internal static void ValidateSession(GameProjectSession project,GlobalTemplateDocument document,bool forReplay=true)
    {
        ProjectFeatureAvailability.ValidateActions(project,document.Actions);
        if(forReplay)ValidateDocument(document);
        foreach(string module in new[]{"Moves","Personal"})if(forReplay?Selected(document,module):Read(document,module) is not null)
        {
            var state=Read(document,module)!;
            if(forReplay)_=Preview(project,module,state,1);
            else ValidateGeneration(project,state.Settings);
        }
    }
    private static void ValidateGeneration(GameProjectSession project,BatchSettings settings)
    {
        if(project.Generation==6&&settings.SetCall||project.Generation==7&&(settings.Hms||settings.FullHms)||!project.SupportsSpecialTutors&&(settings.SpecialTutors||settings.FullSpecialTutors))throw new InvalidDataException("Opciones de una generación incompatible.");
    }
    private static (BatchPreview Proposal,GameMoveService? Moves) Preview(GameProjectSession project,string module,MasterBatchState state,int seed)
    {
        var settings=state.Settings with{Seed=seed};
        ValidateGeneration(project,settings);
        if(module=="Personal")return(project.PreviewPersonal(state.Action,settings,project.Entries.Select(row=>row.Index).ToArray()),null);
        var moves=new GameMoveService(project);return(moves.PreviewMoves(state.Action,settings,moves.Entries.Select(row=>row.Id).ToArray()),moves);
    }
    public static void Execute(string root,string templateJson,int seed,string module)=>ExecuteSession(GameProjectSession.Open(root,recoverInterruptedSave:false),templateJson,seed,module);
    public static void ExecuteSession(GameProjectSession project,string templateJson,int seed,string module)
    {
        var document=GlobalTemplateDocument.Parse(templateJson,project.GameName,project.Generation);ProjectFeatureAvailability.ValidateActions(project,document.Actions);ValidateDocument(document);
        if(!Selected(document,module))throw new InvalidDataException("No hay configuración nativa seleccionada: "+module);
        var result=Preview(project,module,Read(document,module)!,seed);
        if(result.Moves is {} moves)moves.ApplyMovePreview(result.Proposal);else project.ApplyPersonalPreview(result.Proposal);
    }
}
