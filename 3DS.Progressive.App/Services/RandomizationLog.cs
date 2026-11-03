using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// «Registro del randomizado» (spoiler log): what the project looks like now, read from the same editors the user sees.
/// Read-only. Each part is independent: a part that cannot be read says so and the others are still written.
/// </summary>
internal static class RandomizationLog
{
    private static readonly Regex NumberPrefix=new(@"^\s*\d+\s*·\s*",RegexOptions.CultureInvariant);
    private static string Clean(string text)=>NumberPrefix.Replace(text,"");
    private static string Name(EditorField? field)=>field is null?"?":Clean(field.Choices?.FirstOrDefault(c=>c.Id==field.Value)?.Display??$"#{field.Value}");

    internal static string Build(GameProjectSession project,IProgress<string>? progress=null,CancellationToken cancellation=default)
    {
        var text=new StringBuilder();
        string version=typeof(RandomizationLog).Assembly.GetName().Version?.ToString(3)??"";
        text.AppendLine("REGISTRO DEL RANDOMIZADO · DEFINITIVE RANDOMIZER");
        text.AppendLine($"Juego: {project.GameName} · Gen {project.Generation}");
        text.AppendLine($"Proyecto: {Path.GetFileName(project.ProjectRoot)}");
        text.AppendLine($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm} · aplicación {version}");
        text.AppendLine("Contiene spoilers: describe el estado actual del proyecto, no solo lo randomizado.");
        void Part(string title,Action<StringBuilder> body)
        {
            cancellation.ThrowIfCancellationRequested();progress?.Report(title);
            text.AppendLine().AppendLine(new string('=',72)).AppendLine(title.ToUpperInvariant()).AppendLine(new string('=',72));
            var part=new StringBuilder();
            try{body(part);text.Append(part);}
            catch(Exception ex) when(ex is not OperationCanceledException){text.AppendLine("No disponible en este proyecto: "+ex.Message);}
        }
        Part("Cambios guardados",part=>
        {
            var changes=SectionProgress.Changes(project.History.Read());
            if(changes.Count==0){part.AppendLine("Todavía no hay cambios guardados.");return;}
            foreach(var (route,items) in changes.OrderBy(p=>ThreeDS.Progressive.SectionCatalog.Label(p.Key)))
            {
                part.AppendLine().AppendLine($"{ThreeDS.Progressive.SectionCatalog.Label(route)} ({items.Count})");
                foreach(var change in items.Reverse())part.AppendLine($"  {change.When}  {change.Title}"+(change.Detail.Length>0?$" · {change.Detail}":""));
            }
        });
        Part("Regalos, iniciales y encuentros estáticos",part=>
        {
            IProjectEditor statics=project.Generation==6?new GameStaticEncounter6Service(project):new GameStaticEncounterService(project);
            foreach(var entry in statics.Entries)
            {
                var fields=statics.Read(entry.Id).SelectMany(g=>g.Fields).GroupBy(f=>f.Key).ToDictionary(g=>g.Key,g=>g.First());
                var species=fields.Values.FirstOrDefault(f=>f.Key=="Species"||f.Key.EndsWith(".Species",StringComparison.Ordinal));
                var level=fields.Values.FirstOrDefault(f=>f.Key=="Level"||f.Key.EndsWith(".Level",StringComparison.Ordinal));
                part.AppendLine($"{entry.Display}: {Name(species)}"+(level is null?"":$" Nv. {level.Value}"));
            }
        });
        Part("Entrenadores",part=>
        {
            var trainers=new GameTrainerService(project);
            foreach(var entry in trainers.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                var fields=trainers.Read(entry.Id).SelectMany(g=>g.Fields).GroupBy(f=>f.Key).ToDictionary(g=>g.Key,g=>g.First());
                int count=fields.TryGetValue("Count",out var size)?size.Value:0;if(count==0)continue;
                string trainerClass=fields.TryGetValue("Class",out var c)?Name(c):"";
                var team=Enumerable.Range(0,count).Select(s=>$"{Name(fields.GetValueOrDefault($"P{s}.Species"))} Nv. {fields.GetValueOrDefault($"P{s}.Level")?.Value}"
                    +(fields.TryGetValue($"P{s}.Item",out var item)&&item.Value>0?$" @ {Name(item)}":""));
                part.AppendLine($"{entry.Display}"+(trainerClass.Length>0?$" ({trainerClass})":"")+": "+string.Join(", ",team));
            }
        });
        Part("Encuentros salvajes",part=>part.Append(new GameEncounterService(project,cancellation).ExportAllEncounterTables()));
        return text.ToString();
    }
}
