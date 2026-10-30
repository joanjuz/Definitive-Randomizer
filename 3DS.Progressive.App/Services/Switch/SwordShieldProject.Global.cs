using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core.NX;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed record SwitchRecipeAction(string Kind,JsonElement Parameters);
internal sealed record SwitchPersonalEdit(int Entry,string Field,int Value);
internal sealed record SwitchDataReplay(SwordShieldDataSection Section,SwordShieldDataOptions? Options,IReadOnlyList<SwordShieldDataEdit>? Edits,IReadOnlyList<int>? Scope);
internal sealed record SwitchWorldEdit(string Region,string Group,string Field,int Value);
internal sealed record SwitchWorldReplay(bool Items,IReadOnlyList<SwitchWorldEdit> Edits);
internal sealed record SwitchGlobalPreview(Guid Owner,string InputRevision,string ResultRevision,SwordShieldProject Staged,int Actions,int Changes,long InputEpoch,JsonObject AppliedRecipe);
internal sealed partial class SwordShieldProject
{
    private long _mutationEpoch;
    private readonly List<SwitchRecipeAction> _recipeJournal=new();
    private void AppendRecipe(SwitchRecipeAction action){_recipeJournal.Add(action);_mutationEpoch++;}
    private static SwitchRecipeAction RecipeAction<T>(string kind,T value)=>new(kind,JsonSerializer.SerializeToElement(value));
    private void RecordPersonal(SwordShieldEditPreview preview)=>AppendRecipe(preview.RandomOptions is {} random?RecipeAction("personal.random",random):RecipeAction("personal.manual",preview.Changes.Select(c=>new SwitchPersonalEdit(c.Entry,c.Field,c.After)).ToArray()));
    private SwitchRecipeAction CaptureDataAction(SwordShieldDataPreview preview)
    {
        var map=preview.Options is null?ReadData(preview.Section).ToDictionary(c=>(c.Resource,c.Location,c.Field),c=>c.Key):null;var edits=map is null?null:preview.Result.Changes.Select(c=>new SwordShieldDataEdit(map[(c.Resource,c.Location,c.Field)],c.After)).ToArray();
        return RecipeAction(preview.Options is null?"data.manual":"data.random",new SwitchDataReplay(preview.Section,preview.Options,edits,preview.Scope));
    }
    private SwitchRecipeAction CaptureGameplayAction(SwordShieldGameplayPreview preview)
    {
        var o=preview.Options;if(o.Trainers||o.VisibleEncounters||o.HiddenEncounters||o.VisibleItems||o.HiddenItems)return RecipeAction("gameplay.random",o);
        bool items=preview.Result.Changes.All(c=>c.Resource==SwordShieldGameplay.PlacementPath);var rows=ReadWorld(items).ToDictionary(c=>(c.Resource,c.Region+" · "+c.Group,c.Field));var edits=preview.Result.Changes.Select(change=>{var row=rows[(change.Resource,change.Location,change.Field)];return new SwitchWorldEdit(row.Region,row.Group,row.Field,change.After);}).ToArray();return RecipeAction("world.manual",new SwitchWorldReplay(items,edits));
    }
    internal JsonObject CaptureGlobal()
    {
        RequireEditable();VerifySource();if(SurpriseEnabled)throw new NotSupportedException("Retira el prototipo nativo antiguo antes de crear una receta global.");
        // Manual overrides follow randomization; runtime derives caps from the final teams.
        int Phase(SwitchRecipeAction a)=>a.Kind.StartsWith("runtime.")?2:a.Kind.EndsWith(".manual")?1:0;
        int Module(SwitchRecipeAction a)
        {
            if(a.Kind=="personal.random")return 1;
            if(a.Kind=="data.random"){var section=a.Parameters.Deserialize<SwitchDataReplay>()!.Section;return section switch{SwordShieldDataSection.Moves=>0,SwordShieldDataSection.Evolutions=>2,SwordShieldDataSection.Learnsets=>3,SwordShieldDataSection.Machines=>4,SwordShieldDataSection.Statics or SwordShieldDataSection.Gifts or SwordShieldDataSection.Trades=>5,SwordShieldDataSection.Items=>7,SwordShieldDataSection.Shops=>8,_=>9};}
            if(a.Kind=="gameplay.random")return a.Parameters.Deserialize<SwordShieldGameplayOptions>()!.Trainers?9:6;return 0;
        }
        var actions=_recipeJournal.OrderBy(Phase).ThenBy(a=>Phase(a)==0?Module(a):0).Select(a=>(JsonNode?)JsonSerializer.SerializeToNode(a)).ToArray();if(actions.Length==0)throw new InvalidOperationException("El historial no contiene operaciones reproducibles.");
        return new JsonObject { ["Format"]="DefinitiveRandomizer.SwitchGlobal",["Version"]=1,["Name"]="Global Template · Espada/Escudo",["TitleId"]=Identity.TitleId,["BuildId"]=Identity.BuildId,["RuntimeProvider"]=_recipeJournal.Any(a=>a.Kind=="runtime.options")?PokeFeurPackage.Commit:null,["Actions"]=new JsonArray(actions)};
    }
    internal static JsonObject ReadGlobal(string path)
    {if(new FileInfo(path).Length>16_000_000)throw new InvalidDataException("Template demasiado grande.");return JsonNode.Parse(File.ReadAllText(path),documentOptions:new JsonDocumentOptions{MaxDepth=64}) as JsonObject??throw new InvalidDataException("Template inválido.");}
    private IReadOnlyList<SwitchRecipeAction> ValidateGlobal(JsonObject document)
    {
        if(document["Format"]?.GetValue<string>()!="DefinitiveRandomizer.SwitchGlobal"||document["Version"]?.GetValue<int>()!=1)throw new InvalidDataException("Se requiere un Global Template de Switch v1. Las recetas de 3DS no se convierten automáticamente.");
        if(document["TitleId"]?.GetValue<string>()!=Identity.TitleId||document["BuildId"]?.GetValue<string>()!=Identity.BuildId)throw new InvalidDataException("El juego o Build ID del template no coincide.");
        if(document["Actions"] is not JsonArray array||array.Count is <1 or >10000)throw new InvalidDataException("Lista de acciones inválida.");var result=new List<SwitchRecipeAction>();int phase=0;
        foreach(var node in array){var action=node?.Deserialize<SwitchRecipeAction>()??throw new InvalidDataException("Acción vacía.");if(action.Kind is not ("personal.random" or "personal.manual" or "data.random" or "data.manual" or "gameplay.random" or "world.manual" or "runtime.options" or "runtime.remove"))throw new NotSupportedException("Acción no soportada: "+action.Kind);int next=action.Kind.StartsWith("runtime.")?2:action.Kind.EndsWith(".manual")?1:0;if(next<phase)throw new InvalidDataException("Orden inválido: randomización, cambios manuales y finalmente runtime.");if(action.Kind=="runtime.options"){var options=action.Parameters.Deserialize<PokeFeurOptions>()??throw new InvalidDataException("Faltan opciones del runtime.");options.ValidateFeatureAvailability();}phase=next;result.Add(action);}if(result.Any(a=>a.Kind=="runtime.options")&&document["RuntimeProvider"]?.GetValue<string>()!=PokeFeurPackage.Commit)throw new InvalidDataException("La versión del runtime del template no coincide con el módulo integrado.");return result;
    }
    private int ChangeCount=>_history.Count+_dataHistory.Sum(h=>h.Changes.Count)+_gameplayHistory.Sum(h=>h.Changes.Count)+_pokeHistory.Sum(h=>h.Changes.Count);
    private void CopyStateTo(SwordShieldProject target)
    {
        if(!string.Equals(target.Root,Root,StringComparison.OrdinalIgnoreCase)||target.SourceHash!=SourceHash||target.Identity.BuildId!=Identity.BuildId)throw new InvalidOperationException("El origen cambió durante la preparación de la sesión aislada.");
        target._mutationEpoch=_mutationEpoch;target._draft=(byte[])_draft.Clone();
        void Map(Dictionary<string,byte[]> source,Dictionary<string,byte[]> output){output.Clear();foreach(var p in source)output.Add(p.Key,(byte[])p.Value.Clone());}
        Map(_gameplayOriginal,target._gameplayOriginal);Map(_gameplayDraft,target._gameplayDraft);Map(_pokeDraft,target._pokeDraft);Map(_surpriseDraft,target._surpriseDraft);
        target._dependencies.Clear();foreach(var p in _dependencies)target._dependencies.Add(p.Key,p.Value);
        target._history.Clear();target._history.AddRange(_history);target._randomizations.Clear();target._randomizations.AddRange(_randomizations);target._dataHistory.Clear();target._dataHistory.AddRange(_dataHistory);target._gameplayHistory.Clear();target._gameplayHistory.AddRange(_gameplayHistory);target._pokeHistory.Clear();target._pokeHistory.AddRange(_pokeHistory);target._surpriseHistory.Clear();target._surpriseHistory.AddRange(_surpriseHistory);target._recipeJournal.Clear();target._recipeJournal.AddRange(_recipeJournal);
        target.PokeFeurCurrent=PokeFeurCurrent;target._pokeCaps=_pokeCaps;target._pokeProfileChecked=_pokeProfileChecked;target._surpriseSourceChecked=_surpriseSourceChecked;target._trainerPaths=_trainerPaths?.ToArray();target._dataListings.Clear();foreach(var p in _dataListings)target._dataListings.Add(p.Key,p.Value.ToArray());
    }
    private SwitchRecipeAction Replay(SwitchRecipeAction action,CancellationToken token)
    {
        T Read<T>(){try{return action.Parameters.Deserialize<T>(new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})??throw new InvalidDataException("Parámetros incompletos: "+action.Kind);}catch(JsonException ex){throw new InvalidDataException("Parámetros incompatibles en "+action.Kind+": "+ex.Message,ex);}}
        switch(action.Kind)
        {
            case "personal.random":action=RecipeAction(action.Kind,Read<SwordShieldRandomOptions>() with{Seed=SwitchSeed.New32()});break;
            case "data.random":var data=Read<SwitchDataReplay>();if(data.Options is null)throw new InvalidDataException("Faltan opciones de randomización.");action=RecipeAction(action.Kind,data with{Options=data.Options with{Seed=SwitchSeed.New32()}});break;
            case "data.manual":if(Read<SwitchDataReplay>().Options is not null)throw new InvalidDataException("Una acción manual no puede contener randomización.");break;
            case "gameplay.random":action=RecipeAction(action.Kind,Read<SwordShieldGameplayOptions>() with{Seed=SwitchSeed.New32()});break;
            case "runtime.options":action=RecipeAction(action.Kind,Read<PokeFeurOptions>() with{Seed=SwitchSeed.New64()});break;
        }
        switch(action.Kind)
        {
            case "personal.random":var personal=PreviewRandom(Read<SwordShieldRandomOptions>());Apply(personal);action=RecipeAction(action.Kind,personal.RandomOptions!);break;
            case "personal.manual":foreach(var group in Read<SwitchPersonalEdit[]>().GroupBy(e=>e.Entry)){token.ThrowIfCancellationRequested();var values=this.Read(group.Key).Values.ToArray();foreach(var edit in group)values[(int)Enum.Parse<SwordShieldField>(edit.Field)]=edit.Value;Apply(PreviewPersonal(group.Key,values));}break;
            case "data.random":case "data.manual":var data=Read<SwitchDataReplay>();var dataPreview=PreviewData(data.Section,data.Edits,data.Options,data.Scope);ApplyData(dataPreview);action=RecipeAction(action.Kind,data with{Options=dataPreview.Options,Scope=dataPreview.Scope});break;
            case "gameplay.random":ApplyGameplay(PreviewGameplay(Read<SwordShieldGameplayOptions>()));break;
            case "world.manual":var world=Read<SwitchWorldReplay>();var worldRows=ReadWorld(world.Items).ToDictionary(c=>(c.Region,c.Group,c.Field));ApplyGameplay(PreviewWorld(world.Items,world.Edits.Select(e=>new SwordShieldDataEdit(worldRows[(e.Region,e.Group,e.Field)].Key,e.Value)).ToArray()));break;
            case "runtime.options":var runtime=PreviewPokeFeur(PokeFeurPackage.BuiltIn(Identity.TitleId),Read<PokeFeurOptions>());ApplyPokeFeur(runtime);action=RecipeAction(action.Kind,runtime.Options);break;
            case "runtime.remove":RemovePokeFeur();break;
        }
        return action;
    }
    internal SwitchGlobalPreview PreviewGlobal(JsonObject document,CancellationToken token=default)
    {
        RequireEditable();VerifySource();if(SurpriseEnabled)throw new NotSupportedException("Retira el prototipo nativo antiguo antes de aplicar Global Template.");var actions=ValidateGlobal((JsonObject)document.DeepClone());string input=RevisionHash;long epoch=_mutationEpoch;int before=ChangeCount;var staged=Open(Root);CopyStateTo(staged);var applied=(JsonObject)document.DeepClone();var nodes=(JsonArray)applied["Actions"]!;for(int i=0;i<actions.Count;i++){token.ThrowIfCancellationRequested();var executed=staged.Replay(actions[i],token);((JsonObject)nodes[i]!)["Parameters"]=JsonSerializer.SerializeToNode(executed.Parameters);}staged.VerifySource();return new(_id,input,staged.RevisionHash,staged,actions.Count,staged.ChangeCount-before,epoch,applied);
    }
    internal void ApplyGlobal(SwitchGlobalPreview preview)
    {RequireEditable();VerifySource();if(preview.Owner!=_id||preview.InputRevision!=RevisionHash||preview.InputEpoch!=_mutationEpoch||_surpriseHistory.Count!=preview.Staged._surpriseHistory.Count||preview.ResultRevision!=preview.Staged.RevisionHash)throw new InvalidOperationException("La vista previa global quedó desactualizada.");preview.Staged.VerifySource();preview.Staged.CopyStateTo(this);_mutationEpoch++;}
    internal string GlobalHistorySummary(SwordShieldProject? before=null)=>string.Join("\n",_history.Skip(before?._history.Count??0).Select(c=>$"Pokémon #{c.Entry} · {c.Field}: {c.Before} → {c.After}").Concat(_dataHistory.Skip(before?._dataHistory.Count??0).SelectMany(h=>h.Changes.Select(c=>$"{h.Section} · {c.Location} · {c.Field}: {c.Before} → {c.After}"))).Concat(_gameplayHistory.Skip(before?._gameplayHistory.Count??0).SelectMany(h=>h.Changes.Select(c=>$"{c.Location} · {c.Field}: {c.Before} → {c.After}"))).Concat(_pokeHistory.Skip(before?._pokeHistory.Count??0).SelectMany(h=>h.Changes.Select(c=>$"Runtime · {c.Parameter}: {c.Before} → {c.After}"))).Concat(_gameplayHistory.Skip(before?._gameplayHistory.Count??0).SelectMany(h=>(h.Warnings??Array.Empty<string>()).Select(w=>"Aviso · "+w))));
}
