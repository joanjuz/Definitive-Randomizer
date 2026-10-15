using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ThreeDS.Progressive.Services;

/// <summary>Lossless v1/coverage8 pk3DS Progressive compatibility: unknown fields
/// stay in the JSON; no implicit migration, auto-randomization or ROM writes.</summary>
internal sealed class GlobalTemplateDocument
{
    private readonly JsonObject _root;
    internal string? SourcePath {get;private set;}
    internal string Name => GetString("Name");
    internal string Game => GetString("Game");
    internal int Generation=>GetInt("Generation");
    internal int Coverage=>GetInt("ActionCoverageVersion");
    internal int Version=>GetInt("Version");
    internal IReadOnlyList<string> Actions=>_root["Actions"] is JsonArray actions
        ?actions.Select(a=>a?["Id"]?.GetValue<string>()??"").ToArray():[];
    internal int SettingsCount=>_root["RandSettings"] is JsonArray rows?rows.Count:0;
    internal int TrainerCapCount=>_root["Trainer"]?["LevelCaps"]?["Trainers"] is JsonArray a?a.Count:0;
    internal int TrainerMoveRuleCount=>_root["Trainer"]?["TrainerMoveRules"]?["Trainers"] is JsonArray b?b.Count:0;
    internal int PlayerCapCount=>_root["LevelCaps"]?["Entries"] is JsonArray c?c.Count:0;
    internal int AssetCount=>_root["Assets"] is JsonArray d?d.Count:0;
    internal string Sha256=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToJson())));
    internal string ToJson()=>_root.ToJsonString(new JsonSerializerOptions{WriteIndented=true});
    private string GetString(string key)=>_root[key]?.GetValue<string>()??"";
    private int GetInt(string key)=>_root[key]?.GetValue<int>()??0;
    private GlobalTemplateDocument(JsonObject root,string? source=null){_root=root;SourcePath=source;}
    internal static GlobalTemplateDocument Load(string path,string expectedGame,int expectedGeneration)
    {
        if(!File.Exists(path))throw new FileNotFoundException("No existe el template.",path);
        var info=new FileInfo(path);
        if(info.Length>15_000_000)throw new InvalidDataException("Template supera el límite seguro de 15 MB.");
        var json=File.ReadAllText(path,Encoding.UTF8);
        return Parse(json,expectedGame,expectedGeneration,path);
    }
    internal static GlobalTemplateDocument Parse(string json,string expectedGame,int expectedGeneration,
        string? source=null)
    {
        var root=JsonNode.Parse(json) as JsonObject
            ??throw new InvalidDataException("El template global debe tener un objeto JSON raíz.");
        var doc=new GlobalTemplateDocument(root,source);
        doc.Validate(expectedGame,expectedGeneration);
        return doc;
    }
    internal static GlobalTemplateDocument Empty(string game,int generation)=>
        new(new JsonObject
        {
            ["Version"]=1,["ActionCoverageVersion"]=8,
            ["Name"]="Definitive Randomizer · Configuración actual",
            ["Game"]=game,["Generation"]=generation,
            ["SavedUtc"]=DateTime.UtcNow.ToString("O"),
            ["RandSettings"]=new JsonArray(),
            ["Trainer"]=null,["Wild"]=null,["LevelCaps"]=null,
            ["Actions"]=new JsonArray(),["Assets"]=new JsonArray()
        });
    internal void Validate(string game,int generation)
    {
        if(Version!=1)throw new InvalidDataException($"Formato global {Version} no admitido. Se requiere Version=1.");
        if(Generation!=generation)
            throw new InvalidDataException($"Template de generación {Generation}; el proyecto es generación {generation}.");
        if(!string.Equals(Game,"ANY",StringComparison.OrdinalIgnoreCase)
            &&!string.Equals(Game,game,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Template {Game}; el proyecto cargado es {game}.");
        if(_root["RandSettings"] is not JsonArray || _root["Actions"] is not JsonArray ||
            _root["Assets"] is not JsonArray)
            throw new InvalidDataException("Faltan RandSettings, Actions o Assets como listas JSON.");
        var actions=(JsonArray)_root["Actions"]!;
        var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var action in actions)
        {
            if(action is not JsonObject obj ||
                obj["Id"] is null||string.IsNullOrWhiteSpace(obj["Id"]!.GetValue<string>()))
                throw new InvalidDataException("Hay acciones sin ID válido.");
            string id=obj["Id"]!.GetValue<string>();
            if(!ids.Add(id))throw new InvalidDataException("Acción repetida en template: "+id);
            if(obj["Parameters"] is not JsonObject)
                throw new InvalidDataException(id+" no contiene Parameters en formato JSON.");
        }
        if(_root["LevelCaps"] is JsonObject caps && caps["Entries"] is JsonArray capRows
            &&capRows.Count>128)
            throw new InvalidDataException("Player Level Caps supera el máximo de 128 checkpoints.");
        foreach(JsonNode? node in (JsonArray)_root["Assets"]!)
            if(node is JsonObject asset &&
               (asset["FileName"]?.GetValue<string>()??"") is string name && name.Length>0 &&
               (Path.GetFileName(name)!=name||name.Contains("..",StringComparison.Ordinal)))
                throw new InvalidDataException("Asset con ruta no segura: "+name);
    }
    internal void Rename(string name)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("El nombre no puede quedar vacío.");
        _root["Name"]=name.Trim();
    }
    internal void SelectActions(IReadOnlySet<string> selected)
    {
        if(_root["Actions"] is not JsonArray actions)return;
        var filtered=actions.Where(a=>a is JsonObject obj&&selected.Contains(
            obj["Id"]?.GetValue<string>()??"")).Select(a=>a!.DeepClone()).ToArray();
        _root["Actions"]=new JsonArray(filtered);
    }
    internal void MergeHistoryActions(ProjectHistory history)
    {
        var actions=(JsonArray)_root["Actions"]!;
        var current=Actions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in history.Read().Entries)
        {
            if(!entry.Parameters.TryGetValue("ActionId",out string? id)||
                string.IsNullOrWhiteSpace(id)||!current.Add(id))continue;
            // History records demonstrate that a module ran, but do not invent missing
            // legacy RandSettings or detailed action parameter payloads.
            actions.Add(new JsonObject{["Id"]=id,["Parameters"]=new JsonObject()});
        }
    }
    internal GlobalTemplateDocument Clone()=>new((JsonObject)_root.DeepClone(),SourcePath);
    internal JsonNode? Section(string key)=>_root[key]?.DeepClone();
    internal void SetSection(string key,JsonNode? value)=>_root[key]=value?.DeepClone();
    internal void Save(string path)
    {
        if(SourcePath is not null &&
            string.Equals(Path.GetFullPath(SourcePath),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Guardar en una ruta nueva para preservar el template fuente.");
        string? folder=Path.GetDirectoryName(Path.GetFullPath(path));
        if(folder is not null)Directory.CreateDirectory(folder);
        File.WriteAllText(path,ToJson(),new UTF8Encoding(false));
    }
    internal IReadOnlyList<string> CheckAssets(IEnumerable<string> directories)
    {
        var messages=new List<string>();
        foreach(var entry in (JsonArray)_root["Assets"]!)
        {
            if(entry is not JsonObject asset)continue;
            string file=asset["FileName"]?.GetValue<string>()??"";
            string expected=asset["Sha256"]?.GetValue<string>()??"";
            if(file.Length==0)continue;
            string? found=directories.Where(Directory.Exists).Select(d=>Path.Combine(d,file))
                .FirstOrDefault(File.Exists);
            if(found is null){messages.Add($"No se encontró el asset: {file}");continue;}
            string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(found)));
            if(!hash.Equals(expected,StringComparison.OrdinalIgnoreCase))
                messages.Add($"SHA-256 distinto en {file}: no aplicar automáticamente.");
        }
        return messages;
    }
}
