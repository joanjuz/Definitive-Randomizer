using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Field-level deterministic overrides. Never store complete game archives.</summary>
public static class ManualEditTemplate
{
    internal const string ActionId="manual-edits.apply";
    internal sealed record Edit(string Kind,int Entry,string Field,string Before,string After,int Language=-1);
    private static bool Selected(GlobalTemplateDocument doc)=>doc.Actions.Contains(ActionId,StringComparer.OrdinalIgnoreCase);
    private static Edit[] Read(GlobalTemplateDocument doc)=>doc.Section("ProgressiveApp")?["ManualEdits"]?["Entries"]?.Deserialize<Edit[]>()??[];
    internal static void Capture(GlobalTemplateDocument doc,HistoryDocument history)
    {
        var native=doc.Section("ProgressiveApp") as JsonObject??new();
        var edits=Read(doc).ToList();
        var operations=new List<JsonObject>();
        void AddOperation(JsonObject op)
        {
            if(op["Kind"]?.GetValue<string>()=="resources"&&op["Payload"]?["Files"] is JsonArray resources)
            {
                foreach(var f in resources)
                {
                    var file=f!.AsObject();string path=file["Path"]!.GetValue<string>(),mode=file["Mode"]!.GetValue<string>();
                    var pieces=mode=="blocks"?file["Blocks"]!.AsArray().Select(b=>{var copy=(JsonObject)file.DeepClone();copy["Blocks"]=new JsonArray(b!.DeepClone());return copy;}):new[]{(JsonObject)file.DeepClone()};
                    foreach(var piece in pieces)
                    {
                        string suffix=mode=="blocks"?piece["Blocks"]![0]!["Offset"]!.ToString():mode=="garc"?piece["Index"]!.ToString():"";
                        string key="resource:"+path+":"+mode+":"+suffix;var old=operations.FirstOrDefault(o=>o["Key"]?.GetValue<string>()==key);
                        if(old is not null)
                        {
                            if(mode=="blocks")piece["Blocks"]![0]!["Before"]=old["Payload"]!["Files"]![0]!["Blocks"]![0]!["Before"]!.DeepClone();
                            if(mode=="file")piece["BeforeHash"]=old["Payload"]!["Files"]![0]!["BeforeHash"]!.DeepClone();
                        }
                        operations.RemoveAll(o=>o["Key"]?.GetValue<string>()==key);operations.Add(new JsonObject{["Kind"]="resources",["Key"]=key,["Payload"]=new JsonObject{["Files"]=new JsonArray(piece)}});
                    }
                }
                return;
            }
            if(op["Kind"]?.GetValue<string>()=="exact-overrides")
            {
                foreach(var editNode in op["Payload"]!["Entries"]!.AsArray())
                {
                    var edit=editNode!.Deserialize<Edit>()!;string key=$"final-field:{edit.Kind}:{edit.Entry}:{edit.Field}:{edit.Language}";
                    edits.RemoveAll(e=>e.Kind==edit.Kind&&e.Entry==edit.Entry&&e.Field==edit.Field&&e.Language==edit.Language);
                    operations.RemoveAll(o=>o["Key"]?.GetValue<string>()==key);
                    operations.Add(new JsonObject{["Kind"]="exact-overrides",["Key"]=key,["Language"]=edit.Language,["Payload"]=new JsonObject{["Entries"]=new JsonArray(editNode!.DeepClone())}});
                }
                return;
            }
            string operationKey=op["Key"]!.GetValue<string>();var merged=(JsonObject)op.DeepClone();var previous=operations.FirstOrDefault(o=>o["Key"]?.GetValue<string>()==operationKey);
            if(previous is not null&&op["Kind"]?.GetValue<string>() is "record" or "move"&&op["Payload"]?["Fields"] is JsonArray changed)
            {
                var fields=changed.Select(n=>n!.GetValue<string>()).ToHashSet();var prior=previous["Payload"]!["Fields"] is JsonArray stored?stored.Select(n=>n!.GetValue<string>()):previous["Payload"]!["Values"]!.AsObject().Select(p=>p.Key);
                foreach(string field in prior)if(fields.Add(field))merged["Payload"]!["Values"]![field]=previous["Payload"]!["Values"]![field]!.DeepClone();
                merged["Payload"]!["Fields"]=System.Text.Json.JsonSerializer.SerializeToNode(fields.Order().ToArray());
            }
            operations.RemoveAll(o=>o["Key"]?.GetValue<string>()==operationKey);operations.Add(merged);
        }
        foreach(var saved in ManualOperations.Read(doc))AddOperation(saved);
        void Merge(Edit next)
        {
            int index=edits.FindIndex(e=>e.Kind==next.Kind&&e.Entry==next.Entry&&e.Field==next.Field&&e.Language==next.Language);
            if(index>=0)edits[index]=next with{Before=edits[index].Before};else edits.Add(next);
        }
        foreach(var entry in history.Entries.Where(e=>e.Kind=="Edit"))
        {
            if(entry.Parameters.TryGetValue("ManualOperation",out var operation))
            {
                AddOperation(JsonNode.Parse(operation)!.AsObject());continue;
            }
            if(entry.Parameters.TryGetValue("ManualEdits",out var replay))
            {
                foreach(var value in JsonSerializer.Deserialize<Edit[]>(replay)??[])Merge(value);
                continue;
            }
            string kind=entry.Action switch {"Editar Pokémon"=>"personal","Editar Game Text"=>"gametext","Editar Story Text"=>"storytext",_=>""};
            if(kind.Length==0)continue;
            int language=-1;
            if(kind!="personal"&&(!entry.Parameters.TryGetValue("Language",out var raw)||!int.TryParse(raw,out language)))throw new InvalidDataException("La edición de texto no tiene idioma registrado.");
            foreach(var change in entry.Changes)
            {
                var next=new Edit(kind,change.Id,change.Field,change.Before,change.After,language);
                bool followsTemplate=operations.Any(o=>o["Kind"]?.GetValue<string>()=="authored-template")||operations.Any(o=>o["Kind"]?.GetValue<string>()=="exact-overrides");
                if(followsTemplate)
                {
                    edits.RemoveAll(e=>e.Kind==next.Kind&&e.Entry==next.Entry&&e.Field==next.Field&&e.Language==next.Language);
                    AddOperation(new JsonObject{["Kind"]="exact-overrides",["Key"]=$"final-field:{next.Kind}:{next.Entry}:{next.Field}:{next.Language}",["Language"]=next.Language,["Payload"]=new JsonObject{["Entries"]=JsonSerializer.SerializeToNode(new[]{next})}});
                }
                else Merge(next);
            }
        }
        var missing=history.Entries.Where(e=>!e.Parameters.ContainsKey("ManualOperation")&&!e.Parameters.ContainsKey("ManualEdits")&&ManualResourceReplay.ShouldCapture(e.Action,e.Parameters))
            .Where(e=>!e.Files.All(f=>operations.Any(o=>o["Kind"]?.GetValue<string>()=="resources"&&o["Payload"]?["Files"]?.AsArray().Any(n=>n?["Path"]?.GetValue<string>()==f.Path)==true)))
            .Where(e=>e.Changes.Count==0||!e.Changes.All(c=>operations.Any(o=>
                o["Kind"]?.GetValue<string>()=="record"&&o["Payload"]?["Title"]?.GetValue<string>()==e.Module&&o["Payload"]?["Id"]?.GetValue<int>()==c.Id||
                o["Kind"]?.GetValue<string>()=="move"&&e.Action=="Editar movimiento"&&o["Payload"]?["Id"]?.GetValue<int>()==c.Id||
                o["Kind"]?.GetValue<string>()=="learnset"&&e.Action=="Editar movimientos por nivel"&&o["Payload"]?["Id"]?.GetValue<int>()==c.Id)))
            .Select(e=>e.Module+" · "+e.Action+" ("+e.Id+")").ToArray();
        if(edits.Count==0&&operations.Count==0&&missing.Length==0)return;
        if(doc.Game.Equals("ANY",StringComparison.OrdinalIgnoreCase))doc.SetSection("Game",JsonValue.Create(history.Game));
        if(doc.Game!=history.Game)throw new InvalidDataException("El historial de modificaciones manuales pertenece a otro juego.");
        native["ManualEdits"]=new JsonObject{["Version"]=2,["Game"]=doc.Game,["Generation"]=doc.Generation,["Entries"]=JsonSerializer.SerializeToNode(edits),["Operations"]=new JsonArray(operations.Select(o=>(JsonNode)o.DeepClone()).ToArray()),["MissingHistoryPayloads"]=JsonSerializer.SerializeToNode(missing)};
        doc.SetSection("ProgressiveApp",native);
        var actions=doc.Section("Actions")!.AsArray();
        if(!actions.Any(a=>a?["Id"]?.GetValue<string>()==ActionId))actions.Add(new JsonObject{["Id"]=ActionId,["Parameters"]=new JsonObject()});
        doc.SetSection("Actions",actions);
    }
    internal static void Validate(GlobalTemplateDocument doc)
    {
        if(!Selected(doc))return;
        var node=doc.Section("ProgressiveApp")?["ManualEdits"];
        if(node?["Version"]?.GetValue<int>() is not (1 or 2)||node["Game"]?.GetValue<string>()!=doc.Game||doc.Game=="ANY"||node["Generation"]?.GetValue<int>()!=doc.Generation)
            throw new InvalidDataException("Las modificaciones manuales requieren su juego y generación de origen.");
        if(node["MissingHistoryPayloads"] is JsonArray missing&&missing.Count>0)throw new InvalidDataException("El historial antiguo no conserva los datos completos de estas modificaciones. Volvé a guardar las entradas o recursos afectados para incluirlos: "+string.Join("; ",missing.Take(8).Select(n=>n!.GetValue<string>())));
        var edits=Read(doc);
        ManualOperations.Validate(doc);
        if(edits.Length==0&&ManualOperations.Read(doc).Length==0||edits.Length>100000)throw new InvalidDataException("Lista de modificaciones manuales vacía o demasiado grande.");
        var keys=new HashSet<(string,int,string,int)>();
        foreach(var e in edits)
        {
            if(e is null||e.Entry<0||e.Field is null||e.Before is null||e.After is null||!keys.Add((e.Kind,e.Entry,e.Field,e.Language)))throw new InvalidDataException("Modificación manual inválida o duplicada.");
            if(e.Kind=="personal")
            {
                if(e.Entry==0||e.Language!=-1||typeof(PersonalValues).GetProperty(e.Field) is not {CanWrite:true})throw new InvalidDataException("Campo Pokémon no compatible: "+e.Field);
                _=Value(e);
            }
            else if(e.Kind is not ("gametext" or "storytext")||e.Language is <0 or >8||!e.Field.StartsWith("Línea ",StringComparison.Ordinal)||!int.TryParse(e.Field[6..],out int line)||line<0)
                throw new InvalidDataException("Destino de texto no compatible.");
        }
    }
    private static object Value(Edit e)
    {
        var type=typeof(PersonalValues).GetProperty(e.Field)!.PropertyType;
        if(type==typeof(string))return e.After;
        if(type==typeof(bool)&&bool.TryParse(e.After,out bool b))return b;
        if(type==typeof(int)&&int.TryParse(e.After,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n))return n;
        throw new InvalidDataException("Valor manual inválido: "+e.Field);
    }
    private static PersonalValues Patch(PersonalValues original,IEnumerable<Edit> edits)
    {
        var result=original with{};
        foreach(var edit in edits)typeof(PersonalValues).GetProperty(edit.Field)!.SetValue(result,Value(edit));
        return result;
    }
    internal static void ApplyFinalOverrides(GameProjectSession project,JsonObject payload,bool validateOnly)
    {
        var local=GlobalTemplateDocument.Empty(project.GameName,project.Generation);
        local.SetSection("Actions",new JsonArray(new JsonObject{["Id"]=ActionId,["Parameters"]=new JsonObject()}));
        local.SetSection("ProgressiveApp",new JsonObject{["ManualEdits"]=new JsonObject{["Version"]=2,["Game"]=project.GameName,["Generation"]=project.Generation,["Entries"]=payload["Entries"]!.DeepClone()}});
        using var scope=ManualOperationScope.Begin("exact-overrides","final-fields:"+Guid.NewGuid().ToString("N"),payload);
        Apply(project,local,validateOnly,ignoreTextOrigin:true);
    }
    internal static void ValidateSession(GameProjectSession project,GlobalTemplateDocument doc)=>Apply(project,doc,true);
    public static void Execute(string root,string json)
    {
        var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
        Apply(project,GlobalTemplateDocument.Parse(json,project.GameName,project.Generation,Environment.GetEnvironmentVariable("DEFINITIVE_TEMPLATE_PATH")),false);
    }
    internal static void Apply(GameProjectSession project,GlobalTemplateDocument doc,bool validateOnly=false,bool ignoreTextOrigin=false)
    {
        Validate(doc);if(!Selected(doc))return;
        if(doc.Game!=project.GameName||doc.Generation!=project.Generation)throw new InvalidDataException("Juego incompatible con las modificaciones manuales.");
        var edits=Read(doc);var config=project.GameConfiguration;
        var personal=edits.Where(e=>e.Kind=="personal").GroupBy(e=>e.Entry).Select(g=>(Index:g.Key,Values:Patch(project.ReadPersonal(g.Key),g))).ToArray();
        var files=new List<FileChange>();var changes=new List<BatchChange>();
        if(personal.Length>0)files.Add(project.PreparePersonalEdits(personal.ToDictionary(r=>r.Index,r=>r.Values)));
        foreach(var row in personal)changes.AddRange(ProjectHistory.ValueChanges(row.Index,"Pokémon",project.ReadPersonal(row.Index),row.Values));
        int language=config.Language;
        try
        {
            foreach(var bank in edits.Where(e=>e.Kind!="personal").GroupBy(e=>(e.Kind,e.Language)))
            {
                config.Language=bank.Key.Language;
                string path=Path.Combine(config.RomFS,config.GetGARCFileName(bank.Key.Kind));
                byte[] source=File.ReadAllBytes(path);var records=new GARC.MemGARC(source).Files;
                foreach(var group in bank.GroupBy(e=>e.Entry))
                {
                    if(group.Key>=records.Length)throw new InvalidDataException("Archivo de texto inexistente: "+group.Key);
                    string[] lines=new TextFile(config,records[group.Key],config.RemapCharacters).Lines;
                    foreach(var edit in group)
                    {
                        int line=int.Parse(edit.Field[6..],CultureInfo.InvariantCulture);
                        if(line>=lines.Length||!ignoreTextOrigin&&(lines[line]!=edit.Before&&lines[line]!=edit.After))throw new InvalidDataException($"Texto incompatible: {bank.Key.Kind}, idioma {bank.Key.Language}, archivo {group.Key}, línea {line}. No coincide con el texto de origen ni con el reemplazo.");
                        changes.Add(new(group.Key,bank.Key.Kind,edit.Field,lines[line],edit.After));lines[line]=edit.After;
                    }
                    var encoded=TextFile.GetBytes(config,lines,config.RemapCharacters);
                    if(!new TextFile(config,encoded,config.RemapCharacters).Lines.SequenceEqual(lines))throw new InvalidDataException("El reemplazo de texto no es reversible.");
                    records[group.Key]=encoded;
                }
                var reference=config.GetGARCReference(bank.Key.Kind);if(reference.LanguageVariant)reference=reference.GetRelativeGARC(config.Language);
                files.Add(new(path,source,GarcTransaction.Build(source,reference,records,preserveRecordSize:false)));
            }
        }
        finally{config.Language=language;}
        if(validateOnly){ManualOperations.Apply(project,doc,true);return;}
        project.History.Commit(files,"Modificaciones manuales","Aplicar valores exactos",changes,new Dictionary<string,string>{{"ManualEdits",JsonSerializer.Serialize(edits)}});
        if(personal.Length>0)project.RefreshPersonalAfterExternalEdit();
        ManualOperations.Apply(project,doc,false);
        // Reopen in the caller after replay; never flush a stale localized cache.
    }
}
