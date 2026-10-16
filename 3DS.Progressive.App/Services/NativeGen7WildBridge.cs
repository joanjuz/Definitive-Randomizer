using System.IO;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;

/// <summary>Uses the validated Wild engine unchanged; freezes all UI options and its v1 template.</summary>
public static class NativeGen7WildBridge
{
    internal static readonly string[] ActionIds=["wild-encounters.randomize","wild-encounters.progressive","wild-encounters.modify-levels","wild-encounters.scale-levels","wild-encounters.copy-sos"];
    internal static bool Selected(GlobalTemplateDocument doc)=>doc.Generation==7&&doc.Section("ProgressiveApp")?["Wild"] is JsonObject&&doc.Actions.Any(id=>ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase));
    internal static bool HasAllSelectedSnapshots(GlobalTemplateDocument doc)=>doc.Generation!=7||!doc.Actions.Any(id=>ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase))||doc.Section("ProgressiveApp")?["Wild"] is JsonObject;
    internal static string ActionId(IReadOnlyDictionary<string,int> options)=>options["Species"]==0&&options["ModifyLevel"]==1?"wild-encounters.modify-levels":options["Progressive"]==1?"wild-encounters.progressive":options["SlotMode"]==3?"wild-encounters.copy-sos":"wild-encounters.randomize";
    internal static void ValidateDocument(GlobalTemplateDocument doc)
    {
        if(!Selected(doc))return;var snapshot=doc.Section("ProgressiveApp")!["Wild"]!;
        if(snapshot["Scope"]?.GetValue<int>()!=0||snapshot["Options"] is not JsonObject options||snapshot["Template"] is not JsonObject)throw new InvalidDataException("Wild Gen7 requiere alcance Todos, opciones completas y su template v1.");
        string[] actions=doc.Actions.Where(id=>id.StartsWith("wild-encounters.",StringComparison.OrdinalIgnoreCase)).ToArray();if(actions.Length!=1||!ActionIds.Contains(actions[0],StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("La receta Wild nativa admite una acción con todas sus opciones congeladas.");
        var values=options.ToDictionary(p=>p.Key,p=>p.Value?.GetValue<int>()??throw new InvalidDataException("Opción Wild vacía."));if(new[]{"Species","ModifyLevel","Progressive","SlotMode"}.Any(key=>!values.ContainsKey(key)))throw new InvalidDataException("Faltan opciones principales Wild.");string expected=ActionId(values);
        if(!actions[0].Equals(expected,StringComparison.OrdinalIgnoreCase)&&!(actions[0].Equals("wild-encounters.scale-levels",StringComparison.OrdinalIgnoreCase)&&expected=="wild-encounters.modify-levels"))throw new InvalidDataException("La acción Wild no coincide con las opciones guardadas.");
    }
    internal static Dictionary<string,int> Restore(GameEncounterService editor,GlobalTemplateDocument doc,bool forReplay=true)
    {
        if(forReplay)ValidateDocument(doc);var snapshot=doc.Section("ProgressiveApp")?["Wild"]??throw new InvalidDataException("Falta el snapshot Wild.");
        if(snapshot["Template"] is not JsonObject template||snapshot["Options"] is not JsonObject options)throw new InvalidDataException("Falta el template o las opciones Wild.");editor.RestoreOptions(template.ToJsonString());
        var values=options.ToDictionary(p=>p.Key,p=>p.Value?.GetValue<int>()??throw new InvalidDataException("Opción Wild vacía."));
        if(!EditorValues.Flatten(editor.RandomOptions).Keys.ToHashSet().SetEquals(values.Keys))throw new InvalidDataException("Opciones Wild incompletas o desconocidas.");EditorValues.Validate(editor.RandomOptions,values);return values;
    }
    internal static void ValidateSession(GameProjectSession project,GlobalTemplateDocument doc,bool forReplay=true)
    {
        if(doc.Generation!=7||doc.Section("ProgressiveApp")?["Wild"] is null)return;
        var editor=new GameEncounterService(project);var values=Restore(editor,doc,forReplay);
        if(forReplay&&Selected(doc))_=editor.Preview(values,editor.Entries.Select(row=>row.Id).ToArray(),1);
    }
    public static void Execute(string root,string json,int seed)=>ExecuteSession(GameProjectSession.Open(root,recoverInterruptedSave:false),json,seed);
    public static void ExecuteSession(GameProjectSession project,string json,int seed)
    {
        if(project.Generation!=7)throw new InvalidDataException("El puente Wild requiere Gen7.");var doc=GlobalTemplateDocument.Parse(json,project.GameName,7);ValidateDocument(doc);
        if(!Selected(doc))throw new InvalidDataException("No hay acción Wild nativa seleccionada.");var editor=new GameEncounterService(project);var options=Restore(editor,doc);editor.Apply(editor.Preview(options,editor.Entries.Select(row=>row.Id).ToArray(),seed));
    }
}
