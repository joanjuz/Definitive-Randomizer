using System.IO;
using ThreeDS.Progressive.Models;
using System.Text.Json.Nodes;

namespace ThreeDS.Progressive.Services;

/// <summary>Replays the frozen WPF Gen7 static configuration, including native-only options.</summary>
public static class NativeBatchGen7StaticBridge
{
    internal static readonly string[] ActionIds = ["static-encounters.randomize", "static-encounters.modify-levels", "starters.randomize"];
    internal static bool Selected(GlobalTemplateDocument document) => document.Generation == 7 && document.Section("ProgressiveApp")?["StaticEncounters"] is JsonObject && document.Actions.Any(id => ActionIds.Contains(id, StringComparer.OrdinalIgnoreCase));
    internal static void ValidateDocument(GlobalTemplateDocument document)
    {
        if (!Selected(document)) return;
        var snapshot = (JsonObject)document.Section("ProgressiveApp")!["StaticEncounters"]!;
        if (snapshot["Options"] is not JsonObject options || (snapshot["Scope"]?.GetValue<int>() ?? -1) != 0)
            throw new InvalidDataException("Batch de estáticos Gen7 requiere todas las opciones y alcance Todos.");
        var actions = document.Actions.Where(id => id.StartsWith("static-encounters.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("starters.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("trades.", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (actions.Length != 1 || !ActionIds.Contains(actions[0], StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("El snapshot nativo de estáticos Gen7 admite una sola acción; los perfiles Totem e intercambios separados requieren su receta original.");
        string action = actions[0].ToLowerInvariant();
        if (action == "starters.randomize" && ((options["Species"]?.GetValue<int>()??0)!=1||(options["Gifts"]?.GetValue<int>()??0)!=1||(options["Static"]?.GetValue<int>()??1)!=0||(options["Trades"]?.GetValue<int>()??1)!=0)) throw new InvalidDataException("Iniciales Gen7 requiere especies y regalos activos, estáticos e intercambios desactivados.");
        if (action == "static-encounters.modify-levels" && ((options["Species"]?.GetValue<int>() ?? 1) != 0 || (options["ScaleLevels"]?.GetValue<int>() ?? 0) != 1))
            throw new InvalidDataException("Modificar niveles requiere conservar especies y activar niveles.");
    }
    private static Dictionary<string,int> Options(GlobalTemplateDocument document, GameStaticEncounterService editor)
    {
        var json = document.Section("ProgressiveApp")?["StaticEncounters"]?["Options"] as JsonObject ?? throw new InvalidDataException("Faltan opciones de estáticos.");
        var values = json.ToDictionary(pair => pair.Key, pair => pair.Value?.GetValue<int>() ?? throw new InvalidDataException("Opción vacía: " + pair.Key));
        if (!EditorValues.Flatten(editor.RandomOptions).Keys.ToHashSet().SetEquals(values.Keys)) throw new InvalidDataException("Opciones de estáticos Gen7 incompletas o desconocidas.");
        EditorValues.Validate(editor.RandomOptions, values); return values;
    }
    internal static void ValidateSession(GameProjectSession project, GlobalTemplateDocument document)
    {
        ValidateDocument(document); if (!Selected(document)) return;
        if (project.Generation != 7) throw new InvalidDataException("El snapshot requiere Gen7.");
        _ = Options(document, new GameStaticEncounterService(project));
    }
    public static void Execute(string root, string templateJson, int seed) => ExecuteSession(GameProjectSession.Open(root,recoverInterruptedSave:false), templateJson, seed);
    public static void ExecuteSession(GameProjectSession project, string templateJson, int seed)
    {
        if (project.Generation != 7) throw new InvalidDataException("El puente de estáticos requiere Gen7.");
        var document = GlobalTemplateDocument.Parse(templateJson,project.GameName,7); ValidateSession(project,document);
        if (!Selected(document)) throw new InvalidDataException("La receta no selecciona estáticos nativos Gen7.");
        var editor = new GameStaticEncounterService(project);
        int[] scope=editor.Entries.Select(row=>row.Id).ToArray();
        if(document.Actions.Contains("starters.randomize",StringComparer.OrdinalIgnoreCase))scope=scope.Take(3).ToArray();
        editor.Apply(editor.Preview(Options(document,editor),scope,seed));
    }
}
