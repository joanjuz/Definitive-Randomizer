using System.Globalization;
using System.Text.Json;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
namespace ThreeDS.Progressive;

// When a project is opened, every randomizer shows the options that were really applied to it, instead of the defaults:
// the last Global Template applied (its snapshot, plus the history merged by FromHistory) and, when a section was randomized
// again afterwards from its own editor, the options saved with that entry. Only the remembered UI state changes:
// nothing is written to the project, the history or the recipes.
public partial class MainWindow
{
    private readonly Dictionary<string,string> _optionSources=[];
    private int _appliedOptionsRequest;
    private string GameId=>_session is null?"":_session.GameConfiguration.USUM?"USUM":_session.GameConfiguration.SM?"SM":_session.GameConfiguration.ORAS?"ORAS":"XY";

    /// <summary>Options of a randomizer saved in a history entry (flat option values, trainer controls and template).</summary>
    internal static RecordEditorView.UiState? OptionsFromEntry(HistoryEntry entry,IProjectEditor editor)
    {
        var keys=editor.RandomOptions.SelectMany(g=>g.Fields).ToDictionary(f=>f.Key);
        var options=new Dictionary<string,int>();
        void Take(string key,int value){if(keys.TryGetValue(key,out var field)&&value>=field.Min&&value<=field.Max)options[key]=value;}
        foreach(var (key,text) in entry.Parameters)
            if(int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value))Take(key,value);
        if(entry.Parameters.TryGetValue("Controls",out var controls))
        {
            try
            {
                using var json=JsonDocument.Parse(controls);
                if(json.RootElement.ValueKind==JsonValueKind.Object)
                    foreach(var property in json.RootElement.EnumerateObject())
                        if(property.Value.ValueKind==JsonValueKind.Number&&property.Value.TryGetInt32(out int value))Take(property.Name,value);
            }
            catch(JsonException){}
        }
        string? template=editor is IOptionTemplateProjectEditor&&entry.Parameters.TryGetValue("TrainerTemplate",out var saved)?saved:null;
        return options.Count>0||template is not null?new RecordEditorView.UiState(options,0,template):null;
    }
    private static bool IsRandomization(HistoryEntry entry)=>entry.Kind=="Action"||entry.Action.StartsWith("Randomizar",StringComparison.Ordinal);
    private static string When(DateTimeOffset time)=>time.ToLocalTime().ToString("dd/MM HH:mm",CultureInfo.InvariantCulture);

    private async Task RestoreAppliedOptionsAsync(GameProjectSession session)
    {
        int request=++_appliedOptionsRequest;
        try
        {
            string game=GameId;
            (Action? Apply,List<(string Title,RecordEditorView.UiState State,string Source)> States,string? Label) Compute()
            {
                var history=session.History.Read();
                var global=history.Entries.LastOrDefault(e=>e.Parameters.ContainsKey("GlobalTemplateSnapshot"));
                Action? apply=null;string? label=null;
                if(global is not null)
                {
                    try
                    {
                        var document=GlobalTemplateDocument.FromHistory(history,game,session.Generation,null);
                        apply=PrepareGlobalOptions(document);
                        label=$"Global Template «{document.Name}» · {When(global.Time)}";
                    }
                    catch(Exception ex) when(ex is System.IO.InvalidDataException or JsonException or ArgumentException or InvalidOperationException or System.IO.IOException){}
                }
                var rows=GlobalEditorTemplateBridge.Editors(session).Select(r=>(r.Title,r.Create)).ToList();
                if(session.Generation==7)rows.Add(("Encuentros",()=>new GameEncounterService(session)));
                var states=new List<(string Title,RecordEditorView.UiState State,string Source)>();
                foreach(var (title,create) in rows)
                {
                    // Newest first: the last entry of the section that carries readable options (later than the recipe).
                    var candidates=history.Entries.Where(e=>e.Module==title&&IsRandomization(e)&&e.Parameters.Count>0&&(global is null||e.Time>global.Time)).Reverse().ToArray();
                    if(candidates.Length==0)continue;
                    try
                    {
                        var editor=create();
                        foreach(var entry in candidates)
                            if(OptionsFromEntry(entry,editor) is {} state){states.Add((title,state,$"«{entry.Action}» · {When(entry.Time)}"));break;}
                    }
                    catch(Exception ex) when(ex is System.IO.InvalidDataException or System.IO.IOException or ArgumentException or InvalidOperationException){}
                }
                return (apply,states,label);
            }
            // Automated checks load synchronously, like the other section loads.
            var restoreTimer=System.Diagnostics.Stopwatch.StartNew();
            var (applyGlobal,overrides,globalLabel)=StudioDialog.AutomatedChecks?Compute():await Task.Run(Compute);
            TimingLog.Write("Recuperar opciones aplicadas",session.GameName,restoreTimer.Elapsed);
            if(_closed||request!=_appliedOptionsRequest||!ReferenceEquals(_session,session))return;
            var before=new HashSet<string>(_sectionOptions.Keys);
            applyGlobal?.Invoke();
            if(globalLabel is not null)foreach(var title in _sectionOptions.Keys.Where(k=>!before.Contains(k)))_optionSources[title]=globalLabel;
            foreach(var (title,state,source) in overrides){_sectionOptions[title]=state;_optionSources[title]=source;}
        }
        catch(Exception ex) when(ex is System.IO.IOException or JsonException or System.IO.InvalidDataException or UnauthorizedAccessException)
        {
            // Reading the history for display must never block opening the project.
        }
    }
    /// <summary>Waits for the applied options of the open project (used before opening a randomizer and by the checks).</summary>
    internal Task AppliedOptionsReady=>_appliedOptionsTask??Task.CompletedTask;
    private Task? _appliedOptionsTask;
}
