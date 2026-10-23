using System.IO;
using System.Text.Json;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed class SwordShieldOptionsStore
{
    private readonly string _section;
    private Dictionary<string,int> _saved=new(StringComparer.Ordinal);
    internal SwordShieldOptionsStore(string section)=>_section=section;
    internal IReadOnlyList<EditorGroup> Apply(IReadOnlyList<EditorGroup> groups)
    {
        var result=groups.Select(g=>g with{Fields=g.Fields.Where(f=>!f.Key.StartsWith("BST.",StringComparison.Ordinal)||!_saved.Keys.Any(k=>k.StartsWith("BST.",StringComparison.Ordinal))).Select(f=>_saved.TryGetValue(f.Key,out int value)?f with{Value=value}:f).ToArray()}).Where(g=>g.Fields.Count>0).ToList();
        foreach(var range in _saved.Where(p=>p.Key.StartsWith("BST.",StringComparison.Ordinal)).GroupBy(p=>p.Key.Split('.')[1]).OrderBy(g=>int.Parse(g.Key)))
        {
            var fields=range.Select(p=>new EditorField(p.Key,p.Key.Split('.').Last(),p.Value,p.Key.EndsWith("Level",StringComparison.Ordinal)?1:0,p.Key.EndsWith("Level",StringComparison.Ordinal)?100:p.Key.EndsWith("FullRandom",StringComparison.Ordinal)?1:1530)).ToArray();result.Add(new("Rango "+(int.Parse(range.Key)+1),fields));
        }
        return result;
    }
    private sealed record Document(string Format,int Version,string Section,Dictionary<string,int> Options);
    internal string Capture(IReadOnlyDictionary<string,int> options,IReadOnlyList<EditorGroup> groups)
    {Validate(options,groups);return JsonSerializer.Serialize(new Document("DefinitiveRandomizer.SwitchOptions",1,_section,new(options)),new JsonSerializerOptions{WriteIndented=true});}
    internal void Restore(string json,IReadOnlyList<EditorGroup> groups)
    {
        if(json.Length>2_000_000)throw new InvalidDataException("Template demasiado grande.");var doc=JsonSerializer.Deserialize<Document>(json)??throw new InvalidDataException("Template vacío.");if(doc.Format!="DefinitiveRandomizer.SwitchOptions"||doc.Version!=1||doc.Section!=_section||doc.Options is null)throw new InvalidDataException("El template no corresponde a esta sección de Switch. Las recetas de 3DS requieren una adaptación explícita.");Validate(doc.Options,groups);_saved=new(doc.Options,StringComparer.Ordinal);if(_saved.TryGetValue("GlobalItems.Quality",out int quality))foreach(string category in new[]{"Normal","Important","Boss"})_saved.TryAdd("GlobalItems.Quality"+category,quality);
    }
    private static void Validate(IReadOnlyDictionary<string,int> values,IReadOnlyList<EditorGroup> groups)
    {
        var known=groups.SelectMany(g=>g.Fields).Where(f=>!f.Key.StartsWith("BST.",StringComparison.Ordinal)).ToDictionary(f=>f.Key);
        foreach(var pair in values)
        {
            if(known.TryGetValue(pair.Key,out var field)){if(pair.Value<field.Min||pair.Value>field.Max)throw new InvalidDataException("Opción fuera de rango: "+pair.Key);continue;}
            // Legacy per-trainer Move Rules and unselected caps remain importable but are no longer exposed/applied by the Switch editor.
            if(pair.Key=="GlobalItems.Quality"){if(pair.Value is <0 or >2)throw new InvalidDataException("Calidad fuera de rango.");continue;}
            var old=pair.Key.Split('.');if(old.Length==3&&old[0]=="Rule"&&int.TryParse(old[1],out int oldId)&&oldId>=0&&old[2] is "CapUse" or "LevelCap" or "MoveUse" or "Better" or "Strong" or "Power" or "Tolerance" or "Status")
            {int legacyMax=old[2]=="LevelCap"?100:old[2] is "Power" or "Tolerance"?255:1;if(pair.Value<(old[2]=="LevelCap"?1:0)||pair.Value>legacyMax)throw new InvalidDataException("Regla heredada fuera de rango.");continue;}
            var parts=pair.Key.Split('.');if(parts.Length!=3||parts[0]!="BST"||!int.TryParse(parts[1],out int range)||range is <0 or >99||parts[2] is not ("MinLevel" or "MaxLevel" or "MinBST" or "MaxBST" or "FullRandom"))throw new InvalidDataException("Opción desconocida: "+pair.Key);
            int max=parts[2].EndsWith("Level",StringComparison.Ordinal)?100:parts[2]=="FullRandom"?1:1530;if(pair.Value<0||pair.Value>max)throw new InvalidDataException("Opción BST fuera de rango.");
        }
        foreach(var field in known.Values)if(!values.ContainsKey(field.Key)&&!field.Key.StartsWith("Rule.")&&field.Key is not ("DoubleBattleChance" or "GlobalMoves.IncludeTMs" or "GlobalItems.QualityNormal" or "GlobalItems.QualityImportant" or "GlobalItems.QualityBoss" or "GlobalMoves.BetterNormal" or "GlobalMoves.BetterImportant" or "GlobalMoves.BetterBoss" or "GlobalMoves.StrongNormal" or "GlobalMoves.StrongImportant" or "GlobalMoves.StrongBoss" or "GlobalMoves.PowerNormal" or "GlobalMoves.PowerImportant" or "GlobalMoves.PowerBoss" or "GlobalItems.Normal" or "GlobalItems.Important" or "GlobalItems.Boss")&&field.Key is not ("SpecialShopsOnly" or "SimilarBST" or "ThreeStage" or "FillEmpty" or "EmptyMin" or "EmptyMax"))throw new InvalidDataException("Falta la opción: "+field.Key);
        foreach(var range in values.Where(p=>p.Key.StartsWith("BST.",StringComparison.Ordinal)).GroupBy(p=>p.Key.Split('.')[1]))if(range.Count()!=5)throw new InvalidDataException("Rango BST incompleto.");
    }
    internal void Import(string path,IReadOnlyList<EditorGroup> groups)=>Restore(File.ReadAllText(path),groups);
    internal void Export(string path,IReadOnlyDictionary<string,int> options,IReadOnlyList<EditorGroup> groups)=>File.WriteAllText(path,Capture(options,groups));
}
