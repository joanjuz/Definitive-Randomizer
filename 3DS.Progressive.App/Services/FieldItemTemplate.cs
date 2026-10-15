using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;
internal sealed class FieldItemTemplate
{
    private HashSet<int> _banned=[];internal int[] AllowedItems {get;private set;}=[];
    private Dictionary<string,int> _options=[];
    internal bool Allows(int item)=>!_banned.Contains(item)&&(AllowedItems.Length==0||AllowedItems.Contains(item));
    internal IReadOnlyList<EditorGroup> Apply(IReadOnlyList<EditorGroup> groups)=>groups.Select(g=>g with {Fields=g.Fields.Select(f=>f with {Value=_options.GetValueOrDefault(f.Key,f.Value)}).ToArray()}).ToArray();
    internal void Restore(string json,int generation,int itemCount,IReadOnlyList<EditorGroup> groups)
    {
        if(json.Length>1024*1024)throw new InvalidDataException("La plantilla supera 1 MiB.");
        var node=JsonNode.Parse(json)?.AsObject()??throw new InvalidDataException("Plantilla vacía.");
        if(node["Format"]?.GetValue<string>()!="DefinitiveRandomizer.FieldItems.v1"||node["Generation"]?.GetValue<int>()!=generation)throw new InvalidDataException("Formato o generación de plantilla incompatible.");
        if(node.Any(p=>p.Key is not ("Format" or "Generation" or "BannedItems" or "AllowedItems" or "Options")))throw new InvalidDataException("Campo de plantilla desconocido.");
        int[] List(string name){var values=node[name]?.AsArray().Select(v=>v?.GetValue<int>()??throw new InvalidDataException("ID vacío.")).ToArray()??[];if(values.Any(i=>i<=0||i>=itemCount)||values.Distinct().Count()!=values.Length)throw new InvalidDataException("IDs fuera del catálogo o duplicados: "+name);return values;}
        int[] banned=List("BannedItems"),allowed=List("AllowedItems");if(banned.Intersect(allowed).Any())throw new InvalidDataException("Un objeto no puede estar permitido y prohibido a la vez.");
        var options=EditorValues.Flatten(groups);
        if(node["Options"] is JsonObject saved)foreach(var p in saved){if(!options.ContainsKey(p.Key))throw new InvalidDataException("Opción desconocida: "+p.Key);options[p.Key]=p.Value?.GetValue<int>()??throw new InvalidDataException("Opción vacía.");}
        EditorValues.Validate(groups,options);_banned=banned.ToHashSet();AllowedItems=allowed;_options=options;
    }
    internal string Capture(int generation,IReadOnlyDictionary<string,int> options)=>JsonSerializer.Serialize(new {Format="DefinitiveRandomizer.FieldItems.v1",Generation=generation,BannedItems=_banned.Order().ToArray(),AllowedItems,Options=options},new JsonSerializerOptions{WriteIndented=true});
}
