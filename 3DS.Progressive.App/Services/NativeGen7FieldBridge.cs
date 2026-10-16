using System.IO;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;
public static class NativeGen7FieldBridge
{
    internal static readonly string[] ActionIds=["field-items.randomize","items.fix-economy"];
    private static string Key(string id)=>id.Equals("field-items.randomize",StringComparison.OrdinalIgnoreCase)?"FieldItems":"Items";
    internal static bool Selected(GlobalTemplateDocument document,string key)=>document.Generation==7&&document.Section("ProgressiveApp")?[key] is JsonObject&&document.Actions.Any(id=>ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase)&&Key(id)==key);
    internal static void ValidateDocument(GlobalTemplateDocument document)
    {
        foreach(string key in new[]{"FieldItems","Items"})if(Selected(document,key))
        {
            var node=document.Section("ProgressiveApp")![key]!;
            if(node["Scope"]?.GetValue<int>()!=0||node["Options"] is not JsonObject options)throw new InvalidDataException("Replay nativo Gen7 requiere opciones completas y alcance Todos: "+key);
            if(key=="Items"&&(options.Count!=2||options["PriceScale"]?.GetValue<int>()!=100||options["OnlyPaid"]?.GetValue<int>()!=1))throw new InvalidDataException("Fix Economy aplica el preset; no representa un porcentaje de precios personalizado.");
        }
    }
    private static Dictionary<string,int> Options(GlobalTemplateDocument document,IProjectEditor editor,string key)
    {
        if(editor is GameFieldItemsService field&&document.Section("ProgressiveApp")![key]!["Template"] is {} template)field.RestoreOptions(template.ToJsonString());
        var json=document.Section("ProgressiveApp")![key]!["Options"]!.AsObject();var values=json.ToDictionary(pair=>pair.Key,pair=>pair.Value?.GetValue<int>()??throw new InvalidDataException("Opción vacía."));
        if(!values.Keys.ToHashSet().SetEquals(EditorValues.Flatten(editor.RandomOptions).Keys))throw new InvalidDataException("Opciones Gen7 incompletas o desconocidas: "+key);
        EditorValues.Validate(editor.RandomOptions,values);return values;
    }
    internal static void ValidateSession(GameProjectSession project,GlobalTemplateDocument document)
    {
        ValidateDocument(document);
        if(Selected(document,"FieldItems")){var editor=new GameFieldItemsService(project);_=editor.Preview(Options(document,editor,"FieldItems"),editor.Entries.Select(row=>row.Id).ToArray(),1);}
        if(Selected(document,"Items")){var editor=new GameItemService(project);_=editor.PreviewEconomy(editor.PriceRows());}
    }
    public static void Execute(string root,string json,int seed,string key)=>ExecuteSession(GameProjectSession.Open(root,recoverInterruptedSave:false),json,seed,key);
    public static void ExecuteSession(GameProjectSession project,string json,int seed,string key)
    {
        if(project.Generation!=7)throw new InvalidDataException("El puente requiere Gen7.");var document=GlobalTemplateDocument.Parse(json,project.GameName,7);ValidateDocument(document);
        if(!Selected(document,key))throw new InvalidDataException("No hay configuración nativa seleccionada: "+key);
        if(key=="FieldItems"){var editor=new GameFieldItemsService(project);editor.Apply(editor.Preview(Options(document,editor,key),editor.Entries.Select(row=>row.Id).ToArray(),seed));}
        else{var editor=new GameItemService(project);editor.Apply(editor.PreviewEconomy(editor.PriceRows()));}
    }
}
