using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Option transfer only. Never executes a recipe or writes game data.</summary>
internal static class GlobalEditorTemplateBridge
{
    private static string? Key(IProjectEditor editor)=>editor switch
    {GameTrainerService=>"Trainers",GameStaticEncounterService or GameStaticEncounter6Service=>"StaticEncounters",
        GameItemService=>"Items",GameEvolutionService=>"Evolutions",GameEggMoveService=>"EggMoves",
        GameFieldItemsService or GameFieldItems6Service=>"FieldItems",GameMachine6Service=>"Machines",GameTutor6Service=>"Tutors",
        GameMart6Service=>"Marts",GameTrade6Service=>"Trades",GameEncounterService=>"Wild",_=>null};
    internal static IReadOnlyList<(string Key,string Title,Func<IProjectEditor> Create)> Editors(GameProjectSession project)
    {
        var rows=new List<(string,string,Func<IProjectEditor>)>
        {
            ("Trainers","Entrenadores",()=>new GameTrainerService(project)),
            ("StaticEncounters",project.Generation==6?"Regalos, iniciales y encuentros estáticos · Gen6":"Encuentros estáticos",()=>project.Generation==6?new GameStaticEncounter6Service(project):new GameStaticEncounterService(project)),
            ("Items","Objetos",()=>new GameItemService(project)),
            ("Evolutions","Evoluciones",()=>new GameEvolutionService(project)),
            ("EggMoves","Egg Moves · Movimientos huevo",()=>new GameEggMoveService(project)),
            ("FieldItems",project.Generation==6?"Objetos de campo · Gen6":"Field Items",()=>project.Generation==6?new GameFieldItems6Service(project):new GameFieldItemsService(project))
        };
        if(project.Generation==6)rows.AddRange([
            ("Wild","Encuentros",()=>new GameEncounterService(project)),
            ("Machines","MT y MO · Gen6",()=>new GameMachine6Service(project)),
            ("Marts","Tiendas · Gen6",()=>new GameMart6Service(project)),
            ("Trades","Intercambios Progressive · Gen6",()=>new GameTrade6Service(project))]);
        if(project.Generation==7)rows.Add(("Wild","Encuentros",()=>new GameEncounterService(project)));
        if(project.GameConfiguration.ORAS)rows.Add(("Tutors","Tutores y precios BP · ORAS",()=>new GameTutor6Service(project)));
        return rows;
    }
    private static string? Form(IProjectEditor editor,int generation)=>editor switch
    {GameTrainerService=>generation==7?"SMTE":"TrainerRand",GameStaticEncounterService=>"StaticEncounterEditor7",
        GameEggMoveService=>generation==7?"EggMoveEditor7":"EggMoveEditor6",
        GameMachine6Service=>"TMHMEditor6",GameEvolutionService=>generation==7?"EvolutionEditor7":"EvolutionEditor6",_=>null};
    private static readonly Dictionary<string,string> EvolutionNames=new()
    {{"OriginalBST","CHK_BST"},{"Experience","CHK_Exp"},{"Type","CHK_Type"},{"Legends","CHK_L"},{"Events","CHK_E"}};
    private static readonly Dictionary<string,string> EggNames=new()
    {{"Expand","CHK_Expand"},{"Count","NUD_Moves"},{"STAB","CHK_STAB"},{"STABPercent","NUD_STAB"},{"Hms","CHK_HMs"}};
    private static readonly Dictionary<string,string> MachineNames=new()
    {{"Field","CHK_RandomizeField"},{"Hms","CHK_RandomizeHM"}};
    private static string? Control(IProjectEditor editor,string name)=>editor switch
    {GameTrainerService when name.StartsWith("CHK_")||name.StartsWith("NUD_")||name.StartsWith("CB_")=>name,GameStaticEncounterService=>StaticNames.GetValueOrDefault(name),
        GameEggMoveService=>EggNames.GetValueOrDefault(name),GameMachine6Service=>MachineNames.GetValueOrDefault(name),GameEvolutionService=>EvolutionNames.GetValueOrDefault(name),_=>null};
    private static readonly Dictionary<string,string> StaticNames=new()
    {
        ["Legends"]="CHK_L",["Events"]="CHK_E",["SimilarBST"]="CHK_BST",
        ["LegendToLegend"]="CHK_ReplaceLegend",["NoLegendGifts"]="CHK_BlockSpecialPlayerReceived",
        ["ScaleLevels"]="CHK_Level",["LevelScale"]="NUD_LevelBoost",["RandomItems"]="CHK_Item",
        ["RandomAbilities"]="CHK_RandomAbility",["GiftMoves"]="CHK_SpecialMove",
        ["Metronome"]="CHK_Metronome",
        ["AllowMega"]="CHK_AllowMega",["BasicStarter"]="CHK_BasicStarter",["ForceTotem"]="CHK_ForceTotem",
        ["RemoveLocks"]="CHK_RemoveShinyLock",["TotemAura"]="CHK_RandomAura",
        ["ForceFinal"]="CHK_ForceFullyEvolved",["FinalLevel"]="NUD_ForceFullyEvolved",
        ["G1"]="CHK_G1",["G2"]="CHK_G2",["G3"]="CHK_G3",["G4"]="CHK_G4",
        ["G5"]="CHK_G5",["G6"]="CHK_G6",["G7"]="CHK_G7"
    };
    internal static bool HasSettings(GlobalTemplateDocument document,string key,int generation)=>
        document.Section("ProgressiveApp")?[key] is not null ||
        key=="Trainers"&&document.Section("Trainer") is not null ||
        ReadForm(document,key=="Trainers"?(generation==7?"SMTE":"TrainerRand"):
            key=="StaticEncounters"&&generation==7?"StaticEncounterEditor7":key=="EggMoves"?(generation==7?"EggMoveEditor7":"EggMoveEditor6"):
            key=="Machines"&&generation==6?"TMHMEditor6":key=="Evolutions"?(generation==7?"EvolutionEditor7":"EvolutionEditor6"):null).Count>0;
    internal static RecordEditorView.UiState? Restore(GlobalTemplateDocument document,IProjectEditor editor,int generation)
    {
        string? key=Key(editor);if(key is null)return null;
        var native=document.Section("ProgressiveApp")?[key] as JsonObject;
        string? trainer=editor is GameTrainerService?document.Section("Trainer")?.ToJsonString():null;
        var legacy=ReadForm(document,Form(editor,generation));
        if(native is null&&trainer is null&&legacy.Count==0)return null;
        if(trainer is not null)((GameTrainerService)editor).RestoreOptions(trainer);
        string? wildTemplate=null;
        if(editor is GameEncounterService wild&&wild.SupportsWildTemplate&&native is not null){_=NativeGen7WildBridge.Restore(wild,document,forReplay:false);wildTemplate=native["Template"]!.ToJsonString();}
        if(editor is GameFieldItems6Service or GameFieldItemsService&&native?["Template"] is {} fieldTemplate)((IOptionTemplateProjectEditor)editor).RestoreOptions(fieldTemplate.ToJsonString());
        var fields=editor.RandomOptions.SelectMany(g=>g.Fields).ToDictionary(f=>f.Key);
        var values=fields.ToDictionary(p=>p.Key,p=>p.Value.Value);
        bool found=trainer is not null;
        foreach(var (name,field) in fields)
        {
            string? control=Control(editor,name);
            if(control is null||!legacy.TryGetValue(control,out string? raw))continue;
            int scale=editor is GameTrainerService&&name is "NUD_Level" or "NUD_LevelBoost"?100:name=="LevelScale"&&editor is GameStaticEncounterService?100:field.Scale;
            int value=Decode(raw,scale,control);Validate(field,value);values[name]=value;found=true;
        }
        if(editor is GameEvolutionService&&legacy.Count>0)
            values["Operation"]=document.Actions.Contains("evolutions.every-level",StringComparer.OrdinalIgnoreCase)?2:
                document.Actions.Contains("evolutions.remove-trade",StringComparer.OrdinalIgnoreCase)&&!document.Actions.Contains("evolutions.randomize",StringComparer.OrdinalIgnoreCase)?3:1;
        int scope=0;
        if(native is not null)
        {
            if(native["Options"] is not JsonObject options)throw new InvalidDataException("Snapshot de opciones incompleto: "+key);
            foreach(var (name,node) in options)
                if(fields.TryGetValue(name,out var field))
                {int value=node?.GetValue<int>()??throw new InvalidDataException("Opción vacía: "+name);Validate(field,value);values[name]=value;}
            scope=native["Scope"]?.GetValue<int>()??0;
            if(scope is <0 or >2)throw new InvalidDataException("Alcance inválido en "+key);
            found=true;
        }
        if(!found)return null;
        if(editor is GameTrainerService trainers)trainer=trainers.CaptureOptions(values);
        return new(values,scope,trainer??wildTemplate??(editor is GameFieldItems6Service or GameFieldItemsService?native?["Template"]?.ToJsonString():null));
    }
    internal static void Capture(GlobalTemplateDocument document,IProjectEditor editor,RecordEditorView.UiState state,int generation)
    {
        string? key=Key(editor);
        if(key is null)return;
        if(editor is GameTrainerService trainer&&state.Template is not null)trainer.RestoreOptions(state.Template);
        if(editor is GameEncounterService wild&&wild.SupportsWildTemplate){if(state.Template is not null)wild.RestoreOptions(state.Template);else {if(document.Section("ProgressiveApp")?["Wild"]?["Template"] is {} priorTemplate)wild.RestoreOptions(priorTemplate.ToJsonString());wild.RestoreRangeOptions(state.Options);}}
        var groups=editor.RandomOptions;EditorValues.Validate(groups,state.Options);
        var fields=groups.SelectMany(g=>g.Fields).ToDictionary(f=>f.Key);
        var extension=document.Section("ProgressiveApp") as JsonObject??new();
        var snapshot=extension[key] as JsonObject??new();
        var nativeOptions=editor is GameEncounterService wildShape&&wildShape.SupportsWildTemplate?new JsonObject():snapshot["Options"] as JsonObject??new();
        foreach(var (name,value) in state.Options)nativeOptions[name]=value;
        if(editor is GameEncounterService wildCapture&&wildCapture.SupportsWildTemplate){var templateOptions=state.Options.ToDictionary(p=>p.Key,p=>p.Value);templateOptions["LevelScale"]=Math.Clamp(templateOptions["LevelScale"],10,300);snapshot["Template"]=JsonNode.Parse(wildCapture.CaptureOptions(templateOptions));}
        if(editor is GameFieldItems6Service or GameFieldItemsService)snapshot["Template"]=JsonNode.Parse(state.Template??((IOptionTemplateProjectEditor)editor).CaptureOptions(state.Options));
        snapshot["Options"]=nativeOptions.DeepClone();snapshot["Scope"]=state.Scope;
        extension[key]=snapshot.DeepClone();document.SetSection("ProgressiveApp",extension);
        if(key=="Trainers"&&state.Template is not null)
            document.SetSection("Trainer",Merge(document.Section("Trainer"),JsonNode.Parse(state.Template)));
        if(editor is GameEncounterService wildAction&&wildAction.SupportsWildTemplate&&document.Actions.Any(id=>NativeGen7WildBridge.ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase)))
        {
            var actions=document.Section("Actions")!.AsArray();var kept=actions.Where(row=>!NativeGen7WildBridge.ActionIds.Contains(row?["Id"]?.GetValue<string>()??"",StringComparer.OrdinalIgnoreCase)).Select(row=>row?.DeepClone()).ToArray();actions=new JsonArray(kept);actions.Add(new JsonObject{["Id"]=NativeGen7WildBridge.ActionId(state.Options),["Parameters"]=new JsonObject()});document.SetSection("Actions",actions);
        }
        string? form=Form(editor,generation);
        if(form is null)return;
        var edits=new Dictionary<string,string>();
        foreach(var (name,value) in state.Options)
        {
            string? control=Control(editor,name);
            if(control is null)continue;
            if(control.StartsWith("CHK_"))edits[control]=(value==1).ToString();
            else
            {
                int scale=key=="Trainers"?(name is "NUD_Level" or "NUD_LevelBoost"?100:fields[name].Scale):name=="LevelScale"?100:1;
                edits[control]=(value/(decimal)scale).ToString(CultureInfo.InvariantCulture);
            }
        }
        WriteForm(document,form,edits);
    }
    internal static void ValidateBatchReplay(GlobalTemplateDocument document)
    {
        if(document.SettingsCount==0&&!NativeRecipeCompletion.HasAllSelectedSnapshots(document))throw new InvalidDataException("La receta sin RandSettings requiere la configuración nativa de todas sus acciones.");
        if(document.Generation==7){_=ShinyRateTemplate.Read(document);NativeRecipeCompletion.ValidatePickupParameters(document);}
        NativeGen7WildBridge.ValidateDocument(document);NativeGen7MartBridge.ValidateDocument(document);NativeGen7MachineBridge.ValidateDocument(document);NativeMasterBatchBridge.ValidateDocument(document);NativeGen7FieldBridge.ValidateDocument(document);
        if(document.Generation==6)NativeBatchGen6Bridge.ValidateDocument(document);
        else NativeBatchGen7StaticBridge.ValidateDocument(document);
        var native=document.Section("ProgressiveApp");
        if(native is null)return;
        if(native["NativeOnly"]?.GetValue<bool>()==true)
        {
            var allowed=(document.Generation==6?NativeBatchGen6Bridge.ActionIds:NativeBatchGen7StaticBridge.ActionIds).Concat(NativeMasterBatchBridge.ActionIds).Append(ManualEditTemplate.ActionId).Concat(document.Generation==7?NativeGen7FieldBridge.ActionIds:[]).Concat(document.Generation==7?NativeGen7MachineBridge.ActionIds:[]).Concat(document.Generation==7?NativeGen7MartBridge.ActionIds:[]).Concat(document.Generation==7?NativeRecipeCompletion.Gen7ParameterActions:[]).Concat(document.Generation==7?NativeGen7WildBridge.ActionIds:[]).ToArray();
            if(!NativeRecipeCompletion.HasAllSelectedSnapshots(document))throw new InvalidDataException("Falta la configuración nativa de una acción seleccionada.");
            if(document.Actions.Any(id=>id!="rng.seed"&&!allowed.Contains(id,StringComparer.OrdinalIgnoreCase)))throw new InvalidDataException("La receta nativa no contiene la configuración completa de las acciones originales añadidas. Importá una receta original completa para combinarlas.");
        }
        if(native["MachineCompatibility"] is not null)MachineCompatibilityTemplate.ValidateReplay(document);
        if(native["PartialSnapshot"]?.GetValue<bool>()==true)
            throw new InvalidDataException("Este snapshot se creó desde el historial y todavía no configura todos los módulos. Cargá un Global Template original completo y guardá sus opciones actualizadas antes de usar Batch.");
        if(native["EggMoves"] is JsonObject egg&&document.Actions.Contains("egg-moves.randomize",StringComparer.OrdinalIgnoreCase)&&
            (egg["Scope"]?.GetValue<int>()??0)!=0)
            throw new InvalidDataException("Batch requiere movimientos huevo con alcance Todos.");
        if(native["Evolutions"] is JsonObject evolution&&document.Actions.Any(id=>id.StartsWith("evolutions.",StringComparison.OrdinalIgnoreCase)))
        {
            int operation=evolution["Options"]?["Operation"]?.GetValue<int>()??0;
            string expected=operation switch{1=>"evolutions.randomize",2=>"evolutions.every-level",3=>"evolutions.remove-trade",_=>""};
            var actions=document.Actions.Where(id=>id.StartsWith("evolutions.",StringComparison.OrdinalIgnoreCase)).ToArray();
            if((evolution["Scope"]?.GetValue<int>()??0)!=0||expected.Length==0||actions.Length!=1||!string.Equals(actions[0],expected,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Batch requiere evoluciones con alcance Todos y una acción original que coincida con el motor seleccionado.");
        }
        if(native["Trades"] is JsonObject trades&&document.Actions.Any(id=>id.StartsWith("trades.",StringComparison.OrdinalIgnoreCase)))
        {
            int offers=trades["Options"]?["Offers"]?.GetValue<int>()??-1;
            bool random=document.Actions.Contains("trades.randomize-offers",StringComparer.OrdinalIgnoreCase);
            if(document.Generation!=6||(trades["Scope"]?.GetValue<int>()??0)!=0||offers is <0 or >1||(offers==1)!=random||
                document.Actions.Any(id=>id.StartsWith("trades.",StringComparison.OrdinalIgnoreCase)&&id!="trades.accept-any"&&id!="trades.randomize-offers"))
                throw new InvalidDataException("Batch requiere intercambios Gen6 con alcance Todos y acciones que coincidan con Randomizar ofertas.");
        }
        foreach(var (key,prefix) in new[]{("Items","items."),
            ("FieldItems","field-items."),("Machines","tms."),("Marts","marts."),("Tutors","move-tutors.")})
            if(document.Generation!=6&&!NativeGen7FieldBridge.Selected(document,key)&&!NativeGen7MachineBridge.Selected(document,key)&&!(key=="Marts"&&NativeGen7MartBridge.Selected(document))&&native[key] is JsonObject&&document.Actions.Any(id=>id.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Las opciones nativas de "+key+" se pueden guardar y cargar; Batch todavía requiere su configuración original completa. No se generó ninguna ROM.");
        foreach(string key in new[]{"Trainers","StaticEncounters"})
        {
            string[] actions=key=="Trainers"?["trainers.randomize"]:
                ["static-encounters.randomize","static-encounters.modify-levels","starters.randomize"];
            if(!document.Actions.Any(id=>actions.Contains(id,StringComparer.OrdinalIgnoreCase))||native[key] is not JsonObject snapshot)continue;
            if((snapshot["Scope"]?.GetValue<int>()??0)!=0)
                throw new InvalidDataException("Batch requiere alcance Todos en "+key+"; el motor original no reproduce una selección parcial del editor nuevo.");
            if(key!="StaticEncounters"||document.Generation==6||NativeBatchGen7StaticBridge.Selected(document)||snapshot["Options"] is not JsonObject options)continue;
            foreach(string flag in new[]{"Species","Gifts","Static","Trades"})
                if((options[flag]?.GetValue<int>()??1)!=1)
                    throw new InvalidDataException("Batch todavía no reproduce la opción de estáticos "+flag+" desactivada.");
            if((options["LevelFlat"]?.GetValue<int>()??0)!=0 || (options["SimilarBST"]?.GetValue<int>()??0)!=0)
                throw new InvalidDataException("Batch todavía no reproduce los ajustes propios de estáticos: suma de niveles o tolerancia BST. No se generó ninguna ROM.");
        }
    }
    private static void Validate(EditorField field,int value)
    {if(value<field.Min||value>field.Max)throw new InvalidDataException($"Opción fuera de rango: {field.Key} ({value}).");}
    private static int Decode(string raw,int scale,string control)
    {
        if(control.StartsWith("CHK_"))return bool.TryParse(raw,out bool value)?(value?1:0):throw new InvalidDataException("Booleano inválido: "+control);
        if(!decimal.TryParse(raw,NumberStyles.Number,CultureInfo.InvariantCulture,out decimal number)||number*scale!=decimal.Truncate(number*scale)||number*scale<int.MinValue||number*scale>int.MaxValue)
            throw new InvalidDataException("Número inválido: "+control);
        return (int)(number*scale);
    }
    private static Dictionary<string,string> ReadForm(GlobalTemplateDocument document,string? form)
    {
        var result=new Dictionary<string,string>();if(form is null)return result;
        bool active=false,seen=false;
        foreach(var node in (JsonArray)document.Section("RandSettings")!)
        {
            string line=node?.GetValue<string>()??"";
            if(string.IsNullOrWhiteSpace(line)){active=false;continue;}
            int separator=line.IndexOf('\t');
            if(separator<0)
            {active=line==form;if(active&&seen)throw new InvalidDataException("Módulo repetido: "+form);if(active)seen=true;continue;}
            if(active&&!result.TryAdd(line[..separator],line[(separator+1)..]))throw new InvalidDataException("Control duplicado: "+line[..separator]);
        }
        return result;
    }
    private static void WriteForm(GlobalTemplateDocument document,string form,Dictionary<string,string> edits)
    {
        var rows=((JsonArray)document.Section("RandSettings")!).Select(n=>n?.GetValue<string>()??"").ToList();
        int start=rows.IndexOf(form);
        if(start<0){if(rows.Count>0&&!string.IsNullOrWhiteSpace(rows[^1]))rows.Add("");start=rows.Count;rows.Add(form);rows.Add("");}
        int end=start+1;while(end<rows.Count&&!string.IsNullOrWhiteSpace(rows[end]))end++;
        if(end==rows.Count)rows.Add("");
        for(int i=start+1;i<end;i++)
        {int at=rows[i].IndexOf('\t');if(at>=0&&edits.Remove(rows[i][..at],out string? value))rows[i]=rows[i][..at]+"\t"+value;}
        rows.InsertRange(end,edits.Select(p=>p.Key+"\t"+p.Value));
        document.SetSection("RandSettings",new JsonArray(rows.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray()));
    }
    // Keep unknown properties, including those nested in rules, when the original
    // typed trainer template captures its known state again.
    private static JsonNode? Merge(JsonNode? previous,JsonNode? current)
    {
        if(previous is JsonObject old&&current is JsonObject update)
        {var result=(JsonObject)old.DeepClone();foreach(var (key,value) in update)result[key]=Merge(old[key],value);return result;}
        if(previous is JsonArray oldRows&&current is JsonArray newRows)
        {
            return new JsonArray(newRows.Select((row,i)=>
            {var id=row?["TrainerID"];var old=id is null?(i<oldRows.Count?oldRows[i]:null):oldRows.FirstOrDefault(r=>JsonNode.DeepEquals(r?["TrainerID"],id));return Merge(old,row);}).ToArray());
        }
        return current?.DeepClone();
    }
}
