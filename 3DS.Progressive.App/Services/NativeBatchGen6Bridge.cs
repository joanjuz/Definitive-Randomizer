using System.IO;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Headless native services for the original runner's isolated Gen6 staging project.</summary>
public static class NativeBatchGen6Bridge
{
    internal static readonly string[] ActionIds=["trades.accept-any","trades.randomize-offers","static-encounters.randomize","static-encounters.modify-levels","starters.randomize","tms.randomize","field-items.randomize","marts.randomize","marts.add-rare-candies","marts.add-ev-items","marts.ban-ability-capsule","items.fix-economy","pickup.randomize","wild-encounters.randomize","wild-encounters.modify-levels","wild-encounters.scale-levels","tms.sanity","tms.follow-evolutions","catchzones.enable","shiny-rate.apply","move-tutors.randomize","move-tutors.sanity","move-tutors.follow-evolutions"];
    private static string? Key(string action)=>action switch
    {
        "trades.accept-any" or "trades.randomize-offers"=>"Trades",
        "static-encounters.randomize" or "static-encounters.modify-levels" or "starters.randomize"=>"StaticEncounters",
        "move-tutors.randomize"=>"Tutors","move-tutors.sanity" or "move-tutors.follow-evolutions"=>"TutorCompatibility",
        "tms.randomize"=>"Machines","tms.sanity" or "tms.follow-evolutions"=>"MachineCompatibility","field-items.randomize"=>"FieldItems",
        "marts.randomize" or "marts.add-rare-candies" or "marts.add-ev-items" or "marts.ban-ability-capsule"=>"Marts","items.fix-economy"=>"Items","pickup.randomize"=>"Pickup",
        "wild-encounters.randomize" or "wild-encounters.modify-levels" or "wild-encounters.scale-levels"=>"Wild","catchzones.enable"=>"CatchZones","shiny-rate.apply"=>"ShinyRate",_=>null
    };
    private static IEnumerable<string> SelectedActions(GlobalTemplateDocument doc)=>doc.Actions.Where(a=>ActionIds.Contains(a,StringComparer.OrdinalIgnoreCase)&&
        (Key(a.ToLowerInvariant())!="Trades"||doc.Section("ProgressiveApp")?["Trades"] is not null));
    internal static void ValidateDocument(GlobalTemplateDocument doc)
    {
        MachineCompatibilityTemplate.ValidateReplay(doc);TutorCompatibilityTemplate.ValidateReplay(doc);
        foreach(var group in SelectedActions(doc).GroupBy(a=>Key(a.ToLowerInvariant())!))
        {
            if(doc.Generation!=6)throw new InvalidDataException("El puente requiere Gen6.");
            if(group.Key is "MachineCompatibility" or "TutorCompatibility")
            {

                continue;
            }
            if(group.Key is not ("Trades" or "Marts")&&group.Count()!=1)throw new InvalidDataException("Batch Gen6 requiere una sola acción por módulo nativo: "+group.Key);
            if(group.Key=="ShinyRate")
            {
                _=ShinyParameters(doc);
                if(doc.Section("ProgressiveApp")?["ShinyRate"] is not null)throw new InvalidDataException("Shiny Rate Batch requiere parámetros de acción; no admite otro snapshot.");
                continue;
            }
            if(group.Key=="CatchZones")
            {
                if(doc.Section("ProgressiveApp")?["CatchZones"] is not null)throw new InvalidDataException("Catch Zones Batch usa el manifiesto incluido; no admite snapshots personalizados.");
                continue;
            }
            if(group.Key=="Pickup")
            {
                if(doc.Section("ProgressiveApp")?["Pickup"] is not null)throw new InvalidDataException("Pickup Batch reproduce la tabla completa con el pool legal; no admite una tabla personalizada.");
                continue;
            }
            if(group.Key=="Items")
            {
                if(doc.Section("ProgressiveApp")?["Items"] is JsonObject itemSnapshot&&((itemSnapshot["Scope"]?.GetValue<int>()??-1)!=0||itemSnapshot["Options"] is not JsonObject itemOptions||itemOptions.Count!=2||(itemOptions["PriceScale"]?.GetValue<int>()??0)!=100||(itemOptions["OnlyPaid"]?.GetValue<int>()??0)!=1))throw new InvalidDataException("Batch Fix Economy aplica el preset Gen6; no representa una tabla de precios personalizada.");
                continue;
            }
            if(doc.Section("ProgressiveApp")?[group.Key] is not JsonObject snapshot||snapshot["Options"] is not JsonObject options)
                throw new InvalidDataException("Falta la configuración del editor nuevo para Batch Gen6: "+group.Key);
            if((snapshot["Scope"]?.GetValue<int>()??-1)!=0)throw new InvalidDataException("Batch Gen6 requiere alcance Todos: "+group.Key);
            if(group.Key=="Trades")
            {
                if(options.Count!=1||(options["Offers"]?.GetValue<int>()??-1) is <0 or >1||
                    (options["Offers"]!.GetValue<int>()==1)!=group.Contains("trades.randomize-offers",StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Las acciones de intercambios no coinciden con Randomizar ofertas.");
                continue;
            }
            string action=group.First().ToLowerInvariant();
            if(group.Key=="Wild")
            {
                if(action!="wild-encounters.randomize"&&((options["Species"]?.GetValue<int>()??1)!=0||(options["ModifyLevel"]?.GetValue<int>()??0)!=1))throw new InvalidDataException("Modificar niveles Wild requiere conservar especies y activar niveles.");
            }
            if(group.Key=="Marts"&&options["UseChecks"]?.GetValue<int>()==1)
            {
                string[] ids=["marts.randomize","marts.add-rare-candies","marts.add-ev-items","marts.ban-ability-capsule"];string[] flags=["Randomize","Candies","EVItems","BanCapsule"];
                if(!ids.Where((id,i)=>options[flags[i]]?.GetValue<int>()==1).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(group))throw new InvalidDataException("Las acciones de tiendas Gen6 no coinciden con sus checks.");
            }
            if(group.Key=="Marts"&&options["UseChecks"]?.GetValue<int>()!=1&&(options["Operation"]?.GetValue<int>()??-1)!=(action=="marts.add-rare-candies"?1:0))throw new InvalidDataException("La acción de tiendas no coincide con Operation.");
            if(group.Key=="StaticEncounters"&&(options["SeparateProfiles"]?.GetValue<int>()??0)==1&&action!="static-encounters.randomize")throw new InvalidDataException("Los perfiles independientes se reproducen mediante static-encounters.randomize.");
            if(action=="starters.randomize"&&((options["Static"]?.GetValue<int>()??1)!=0||(options["Gifts"]?.GetValue<int>()??1)!=0||(options["Starters"]?.GetValue<int>()??0)!=1||(options["Species"]?.GetValue<int>()??0)!=1))
                throw new InvalidDataException("La acción de iniciales requiere solo iniciales y randomización de especies.");
            if(action=="static-encounters.modify-levels"&&((options["Species"]?.GetValue<int>()??1)!=0||(options["Forms"]?.GetValue<int>()??1)!=0||(options["ScaleLevels"]?.GetValue<int>()??0)!=1))
                throw new InvalidDataException("Modificar niveles requiere especies/formas conservadas y niveles activados.");
        }
    }
    private static (int Rerolls,bool Everything) ShinyParameters(GlobalTemplateDocument doc)
    {
        var action=((JsonArray)doc.Section("Actions")!).Single(a=>string.Equals(a?["Id"]?.GetValue<string>(),"shiny-rate.apply",StringComparison.OrdinalIgnoreCase));
        var parameters=(JsonObject)action!["Parameters"]!;
        if(!parameters.Select(p=>p.Key).ToHashSet().SetEquals(new[]{"rerolls","everythingShiny"})||
           !int.TryParse(parameters["rerolls"]?.GetValue<string>(),System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out int rolls)||rolls is <1 or >9000||
           !bool.TryParse(parameters["everythingShiny"]?.GetValue<string>(),out bool everything))
            throw new InvalidDataException("Shiny Rate requiere rerolls (1–9000) y everythingShiny explícitos y válidos.");
        return(rolls,everything);
    }
    private static IProjectEditor Create(GameProjectSession project,string key)=>key switch
    {"Trades"=>new GameTrade6Service(project),"StaticEncounters"=>new GameStaticEncounter6Service(project),"Machines"=>new GameMachine6Service(project),"Tutors"=>new GameTutor6Service(project),"FieldItems"=>new GameFieldItems6Service(project),"Marts"=>new GameMart6Service(project),"Items"=>new GameItemService(project),"Wild"=>new GameEncounterService(project),_=>throw new InvalidDataException("Módulo no admitido.")};
    private static Dictionary<string,int> Options(GlobalTemplateDocument doc,IProjectEditor editor,string key)
    {
        var saved=(JsonObject)doc.Section("ProgressiveApp")![key]!["Options"]!;
        var values=saved.ToDictionary(p=>p.Key,p=>p.Value?.GetValue<int>()??throw new InvalidDataException("Opción vacía: "+p.Key));
        if(editor is GameEncounterService wild)wild.RestoreRangeOptions(values);
        if(editor is GameFieldItems6Service fieldEditor&&doc.Section("ProgressiveApp")![key]!["Template"] is {} template)fieldEditor.RestoreOptions(template.ToJsonString());
        var fields=EditorValues.Flatten(editor.RandomOptions);
        // Old shared Gen6 snapshots predate independent profiles. Fill only the newly introduced fields.
        if(key=="StaticEncounters"&&values.Keys.All(fields.ContainsKey)&&!values.Keys.Any(GameStaticEncounter6Service.IsProfileOption))
            foreach(var field in fields)if(!values.ContainsKey(field.Key)&&GameStaticEncounter6Service.IsProfileOption(field.Key))values[field.Key]=field.Value;
        if(key=="Wild"&&values.Keys.All(fields.ContainsKey)&&!values.Keys.Any(k=>k.StartsWith("BST.",StringComparison.Ordinal)))
            foreach(var field in fields)if(field.Key.StartsWith("BST.",StringComparison.Ordinal))values[field.Key]=field.Value;
        if(key=="Marts"&&values.Keys.ToHashSet().SetEquals(new[]{"Operation","SpecialOnly","KeepX"}))
            foreach(var field in fields)if(!values.ContainsKey(field.Key))values[field.Key]=field.Value;
        if(!fields.Keys.ToHashSet().SetEquals(values.Keys))throw new InvalidDataException("Configuración incompleta o con opciones desconocidas: "+key);
        EditorValues.Validate(editor.RandomOptions,values);return values;
    }
    internal static void ValidateSession(GameProjectSession project,GlobalTemplateDocument doc,string? catchManifest=null)
    {
        ValidateDocument(doc);
        foreach(string action in SelectedActions(doc))
        {
            string key=Key(action.ToLowerInvariant())!;
            if(key=="ShinyRate"){var state=ShinyParameters(doc);_=new GameShinyRateService(project).Preview(state.Rerolls,state.Everything);continue;}
            if(key=="CatchZones"){_=new GameCatchZoneService(project,catchManifest).Preview();continue;}
            if(key=="TutorCompatibility"){_=new GameTutor6CompatibilityService(project).Preview(true,true);continue;}
            if(key=="MachineCompatibility"){_=new GameTmCompatibilityService(project).Preview(true,true);continue;}
            if(key=="Pickup"){_=new GamePickupService(project).Read();continue;}
            var editor=Create(project,key);
            if(key!="Items"){var options=Options(doc,editor,key);if(editor is GameTrade6Service trades)_=trades.Preview(options,[1],1);}
        }
    }
    public static void Execute(string root,string templateJson,int seed)
    {
        var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
        ExecuteSession(project,templateJson,seed);
    }
    public static void ExecuteSession(GameProjectSession project,string templateJson,int seed)=>ExecuteSession(project,templateJson,seed,null);
    internal static void ExecuteSession(GameProjectSession project,string templateJson,int seed,string? catchManifest)
    {
        if(project.Generation!=6)throw new InvalidDataException("El puente nativo requiere Gen6.");
        var doc=GlobalTemplateDocument.Parse(templateJson,project.GameName,6);ValidateDocument(doc);
        var actions=SelectedActions(doc).OrderBy(a=>a.Equals("catchzones.enable",StringComparison.OrdinalIgnoreCase)?0:Key(a.ToLowerInvariant()) is "MachineCompatibility" or "TutorCompatibility"?2:1).ToArray();
        // Validate every selected module before committing any changes. Recreate editors for
        // execution because machines, field items and marts may share code.bin.
        ValidateSession(project,doc,catchManifest);
        int index=0;
        bool tradesDone=false,martsDone=false;
        foreach(string action in actions)
        {
            string key=Key(action.ToLowerInvariant())!;if(key=="Trades"&&tradesDone)continue;if(key=="Trades")tradesDone=true;if(key=="Marts"&&martsDone)continue;if(key=="Marts")martsDone=true;
            int moduleSeed=unchecked(seed+ ++index*1009);
            if(key=="ShinyRate")
            {
                var state=ShinyParameters(doc);var shiny=new GameShinyRateService(project);var preview=shiny.Preview(state.Rerolls,state.Everything);
                if(preview.Changes.Count>0)shiny.Apply(preview.File,preview.Changes,false);continue;
            }
            if(key=="CatchZones")
            {
                var catchZones=new GameCatchZoneService(project,catchManifest);catchZones.Apply(catchZones.Preview());continue;
            }
            if(key=="TutorCompatibility")
            {
                var compatibility=new GameTutor6CompatibilityService(project);var preview=compatibility.Preview(action.Equals("move-tutors.sanity",StringComparison.OrdinalIgnoreCase),action.Equals("move-tutors.follow-evolutions",StringComparison.OrdinalIgnoreCase));
                if(preview.Changes.Count>0)compatibility.Apply(preview);continue;
            }
            if(key=="MachineCompatibility")
            {
                var compatibility=new GameTmCompatibilityService(project);var preview=compatibility.Preview(action.Equals("tms.sanity",StringComparison.OrdinalIgnoreCase),action.Equals("tms.follow-evolutions",StringComparison.OrdinalIgnoreCase));
                if(preview.Changes.Count>0)compatibility.Apply(preview);continue;
            }
            if(key=="Pickup")
            {
                var pickup=new GamePickupService(project);pickup.Apply(pickup.Preview(pickup.Randomize(pickup.Read(),moduleSeed)));continue;
            }
            var editor=Create(project,key);int[] scope=editor.Entries.Select(e=>e.Id).ToArray();
            EditorProposal proposal;
            if(editor is GameItemService items)proposal=items.PreviewEconomy(items.PriceRows());
            else
            {
                var options=Options(doc,editor,key);
                proposal=editor switch
                {
                    GameTrade6Service trades=>trades.Preview(options,scope,moduleSeed),
                    GameEncounterService wild=>wild.Preview(options,scope,moduleSeed),
                    GameStaticEncounter6Service statics=>statics.Preview(options,scope,moduleSeed),
                    GameTutor6Service tutors=>tutors.Preview(options,scope,moduleSeed),
                    GameMachine6Service machines=>machines.Preview(options,scope,moduleSeed),
                    GameFieldItems6Service field=>field.Preview(options,scope,moduleSeed),
                    GameMart6Service marts=>marts.Preview(options,scope,moduleSeed),_=>throw new InvalidDataException("Editor no admitido.")
                };
            }
            editor.Apply(proposal);
        }
        if(actions.Any(action=>Key(action.ToLowerInvariant())=="StaticEncounters"))
        {
            var integrity=new GameCroIntegrityService(project);integrity.Apply(integrity.Preview(updateCrr:false));
        }
    }
}
