using System.IO;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed record GuidedModule(string Key,string Title,string Description);
internal sealed class GuidedChoice(GuidedModule module)
{
    internal GuidedModule Module {get;}=module;
    internal bool Selected {get;set;}
    internal IReadOnlyList<EditorGroup>? Groups {get;set;}
    internal Dictionary<string,int> Values {get;set;}=[];
    internal IProjectEditor? Editor {get;set;}
}

/// <summary>Configuration only. Execution is delegated to the existing isolated Batch builder.</summary>
internal sealed class GuidedRandomizationPlan
{
    private readonly GameProjectSession _project;
    internal IReadOnlyList<GuidedChoice> Choices {get;}
    internal GuidedRandomizationPlan(GameProjectSession project)
    {
        _project=project;
        var modules=new List<GuidedModule>
        {
            new("Personal","Pokémon","Estadísticas, tipos, habilidades y compatibilidad."),
            new("Moves","Movimientos","Propiedades de los movimientos; los aprendizajes se eligen por separado."),
            new("Trainers","Entrenadores","Equipos y opciones del randomizador de entrenadores."),
            new("Wild","Encuentros","Pokémon salvajes, niveles y opciones por generación."),
            new("StaticEncounters","Encuentros estáticos e iniciales","Regalos, iniciales y encuentros especiales del editor actual."),
            new("LevelUp","Level Up Moves","Movimientos aprendidos al subir de nivel."),
            new("EggMoves","Egg Moves","Movimientos heredados por crianza."),
            new("Evolutions","Evoluciones","Randomizar, evolucionar cada nivel o eliminar intercambios con el motor original."),
            new("Machines",project.Generation==6?"MT y MO":"MT","Randomizar los movimientos de las máquinas."),
            new("FieldItems","Field Items","Objetos recogidos en el mapa y su catálogo permitido."),
            new("Marts","Tiendas","Inventarios y opciones de las tiendas compatibles."),
            new("Pickup","Pickup","Randomizar objetos de Recogida conservando el motor y formato del juego.")
        };
        if(project.GameConfiguration.ORAS||project.GameConfiguration.USUM)
            modules.Insert(9,new("Tutors","Tutores","Movimientos de tutor compatibles con el juego abierto."));
        Choices=modules.Select(module=>new GuidedChoice(module)).ToArray();
    }
    internal GuidedChoice Get(string key)=>Choices.Single(choice=>choice.Module.Key==key);
    internal IProjectEditor? CreateEditor(string key,CancellationToken cancellationToken=default)=>key switch
    {
        "Trainers"=>new GameTrainerService(_project),"Wild"=>new GameEncounterService(_project,cancellationToken),
        "StaticEncounters"=>_project.Generation==6?new GameStaticEncounter6Service(_project):new GameStaticEncounterService(_project),
        "EggMoves"=>new GameEggMoveService(_project),"Evolutions"=>new GameEvolutionService(_project),
        "FieldItems"=>_project.Generation==6?new GameFieldItems6Service(_project,cancellationToken):new GameFieldItemsService(_project,cancellationToken),
        "Machines" when _project.Generation==6=>new GameMachine6Service(_project),
        "Tutors" when _project.Generation==6=>new GameTutor6Service(_project),
        "Marts" when _project.Generation==6=>new GameMart6Service(_project),_=>null
    };
    internal void Initialize(string key,IProjectEditor? editor=null)
    {
        var choice=Get(key);if(choice.Groups is not null)return;
        choice.Editor=editor??CreateEditor(key);
        if(choice.Editor is GameTrainerService trainers&&trainers.Entries.Count>0)trainers.SelectOptionsEntry(trainers.Entries[0].Id);
        choice.Groups=choice.Editor?.RandomOptions??SpecialGroups(key,_project.GameConfiguration.USUM);
        choice.Values=EditorValues.Flatten(choice.Groups);
        if(key=="Evolutions")choice.Values["Operation"]=1;
        if(key=="Marts"&&_project.Generation==6)choice.Values["KeepX"]=0;
    }
    private static EditorField Flag(string key,string title,bool value=false)=>new(key,title,value?1:0,0,1);
    private static IReadOnlyList<EditorGroup> SpecialGroups(string key,bool usum)=>key switch
    {
        "Personal"=>[new("Qué randomizar",[Flag("Stats","Estadísticas"),Flag("Types","Tipos"),Flag("Abilities","Habilidades"),Flag("Items","Objetos equipados"),Flag("Catch","Ratio de captura"),Flag("Eggs","Grupos huevo"),Flag("Tms","Compatibilidad MT")]),
            new("Estadísticas y tipos",[Flag("Shuffle","Reordenar estadísticas"),new("Deviation","Variación de estadísticas (%)",25,0,100),new("SingleTypeChance","Probabilidad de un solo tipo (%)",50,0,100)]),
            new("Habilidades",[Flag("WonderGuard","Permitir Superguarda"),Flag("BanTraps","Prohibir habilidades que impiden escapar")])],
        "Moves"=>[new("Movimientos",[Flag("MoveTypes","Randomizar tipos"),Flag("MoveCategories","Randomizar categorías")])],
        "LevelUp"=>[new("Aprendizajes",[Flag("CHK_HMs","Permitir MO / HM"),Flag("CHK_STAB","Sesgo por tipo (STAB)"),new("NUD_STAB","STAB (%)",15,0,100),Flag("CHK_NoFixedDamage","Excluir movimientos de daño fijo",true)]),
            new("Cantidad y niveles",[Flag("CHK_Expand","Expandir cantidad de movimientos"),new("NUD_Moves","Cantidad de movimientos",25,25,75),Flag("CHK_4MovesLvl1","Comenzar con 4 movimientos"),Flag("CHK_Spread","Distribuir niveles uniformemente"),new("NUD_Level","Nivel final",79,1,100)])],
        "Machines"=>[new("Compatibilidad MT",[Flag("Randomize","Randomizar MT",true),Flag("Sanity","TM Sanity · movimientos aprendidos por nivel"),Flag("Follow","TM Follow Evolutions · heredar compatibilidad")])],
        "Tutors"=>[new("Tutores",[Flag("Randomize","Randomizar tutores",true),Flag("Free","Tutores gratuitos"),Flag("Sanity","Sanity · movimientos aprendidos por nivel"),Flag("Follow","Follow Evolutions · heredar compatibilidad")])],
        "Marts"=>[new("Inventario",[Flag("RandomRegular","Randomizar tiendas",true),Flag("RandomBP","Randomizar BP"),Flag("Candies","Caramelos raros"),Flag("Vitamins","Vitaminas 252 EV"),Flag("FreeMega","Megapiedras gratis (en BP)"),Flag("BanCapsule","Prohibir Cápsula Habilidad"),..(usum?new[]{Flag("SinglePurchase","Solo un objeto en tiendas especiales")}:[])]),
            new("Alcance",[Flag("SpecialsOnly","Solo tiendas especiales",true),Flag("ProtectX","Proteger objetos X")])],
        "Pickup"=>[new("Recogida",[Flag("Randomize","Randomizar Pickup",true)])],_=>throw new InvalidDataException("Sección guiada no admitida: "+key)
    };
    internal static bool HasActions(GuidedChoice choice)
    {
        bool Any(params string[] keys)=>keys.Any(key=>choice.Values.GetValueOrDefault(key)==1);
        return choice.Module.Key switch
        {
            "Personal"=>Any("Stats","Types","Abilities","Items","Catch","Eggs","Tms"),
            "Machines" or "Tutors" when choice.Values.ContainsKey("Randomize")=>Any("Randomize","Free","Sanity","Follow"),
            "FieldItems"=>Any("Visible","Hidden","Mega","IncludeEI","IncludeEB"),
            "Moves"=>Any("MoveTypes","MoveCategories"),
            "Marts" when choice.Values.ContainsKey("RandomRegular")=>Any("RandomRegular","RandomBP","Candies","Vitamins","FreeMega","BanCapsule","SinglePurchase"),
            "Marts" when choice.Values.GetValueOrDefault("UseChecks")==1=>Any("Randomize","Candies","EVItems","BanCapsule"),
            "Wild"=>Any("Species","ModifyLevel","AllCanCallAllies"),
            "Machines" when choice.Values.ContainsKey("Tms")=>Any("Tms","Hms","Sanity","Follow")||choice.Values.GetValueOrDefault("HmRestriction")!=0,
            _=>choice.Groups is not null && (!choice.Groups.SelectMany(g=>g.Fields).Any(f=>f.Min==0&&f.Max==1&&f.Choices is null)||choice.Groups.SelectMany(g=>g.Fields).Any(f=>f.Min==0&&f.Max==1&&f.Choices is null&&choice.Values.GetValueOrDefault(f.Key)==1))
        };
    }
    internal GlobalTemplateDocument BuildDocument()
    {
        var selected=Choices.Where(choice=>choice.Selected).ToArray();
        if(selected.Length==0)throw new InvalidDataException("Elegí al menos una sección para randomizar.");
        var doc=GlobalTemplateDocument.Empty(_project.GameName,_project.Generation);doc.Rename("Randomizado guiado");
        doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=false});
        void Add(string id)
        {
            var actions=doc.Section("Actions")!.AsArray();
            if(!actions.Any(row=>row?["Id"]?.GetValue<string>()==id))actions.Add(new JsonObject{["Id"]=id,["Parameters"]=new JsonObject()});
            doc.SetSection("Actions",actions);
        }
        int V(GuidedChoice c,string key)=>c.Values[key];bool B(GuidedChoice c,string key)=>V(c,key)==1;
        foreach(var choice in selected)
        {
            if(choice.Groups is null)throw new InvalidDataException("Falta revisar las opciones de "+choice.Module.Title+".");
            if(!HasActions(choice))continue;
            EditorValues.Validate(choice.Groups,choice.Values);
            string key=choice.Module.Key;
            if(key=="Personal")
            {
                var settings=new BatchSettings{Stats=B(choice,"Stats"),Types=B(choice,"Types"),Abilities=B(choice,"Abilities"),Items=B(choice,"Items"),Catch=B(choice,"Catch"),Eggs=B(choice,"Eggs"),Tms=B(choice,"Tms"),Shuffle=B(choice,"Shuffle"),Deviation=V(choice,"Deviation"),SingleTypeChance=V(choice,"SingleTypeChance"),WonderGuard=B(choice,"WonderGuard"),BanTraps=B(choice,"BanTraps")};

                var state=new MasterBatchState(BatchAction.PersonalRandomize,settings,0);NativeMasterBatchBridge.Capture(doc,state,_project.Generation);Add(NativeMasterBatchBridge.ActionId(state));continue;
            }
            if(key=="Moves")
            {

                var state=new MasterBatchState(BatchAction.MoveRandomize,new BatchSettings{MoveTypes=B(choice,"MoveTypes"),MoveCategories=B(choice,"MoveCategories")},0);
                NativeMasterBatchBridge.Capture(doc,state,_project.Generation);Add(NativeMasterBatchBridge.ActionId(state));continue;
            }
            if(key=="LevelUp")
            {
                var rows=doc.Section("RandSettings")!.AsArray();if(rows.Count>0)rows.Add("");rows.Add(_project.Generation==6?"LevelUpEditor6":"LevelUpEditor7");
                foreach(var pair in choice.Values)rows.Add(pair.Key+"\t"+(pair.Key.StartsWith("CHK_")?(pair.Value==1).ToString():pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                rows.Add("");doc.SetSection("RandSettings",rows);Add("level-up-moves.randomize");continue;
            }
            if(_project.Generation==7&&key is "Machines" or "Tutors")
            {
                var old=NativeGen7MachineBridge.Read(doc)??new();
                var state=key=="Machines"?old with{RandomMachines=B(choice,"Randomize"),MachineSanity=B(choice,"Sanity"),MachineFollow=B(choice,"Follow")}:old with{RandomTutors=B(choice,"Randomize"),FreeTutors=B(choice,"Free"),TutorSanity=B(choice,"Sanity"),TutorFollow=B(choice,"Follow")};
                NativeGen7MachineBridge.Capture(doc,state);continue;
            }
            if(_project.Generation==7&&key=="Marts")
            {
                var state=new Gen7MartState(B(choice,"RandomRegular"),B(choice,"RandomBP"),B(choice,"Candies"),B(choice,"Vitamins"),B(choice,"FreeMega"),B(choice,"BanCapsule"),B(choice,"SpecialsOnly"),B(choice,"ProtectX"),_project.GameConfiguration.USUM&&B(choice,"SinglePurchase"));
                if(!new[]{state.RandomRegular,state.RandomBP,state.Candies,state.Vitamins,state.FreeMega,state.BanCapsule,state.SinglePurchase}.Any(value=>value))throw new InvalidDataException("Tiendas: elegí al menos una acción.");
                NativeGen7MartBridge.Capture(doc,state);continue;
            }
            if(key=="Pickup"){Add("pickup.randomize");continue;}
            var editor=choice.Editor??throw new InvalidDataException("Editor no preparado: "+key);
            string? template=editor is GameEncounterService {SupportsWildTemplate:false}?null:editor is IOptionTemplateProjectEditor options?options.CaptureOptions(choice.Values):null;
            GlobalEditorTemplateBridge.Capture(doc,editor,new RecordEditorView.UiState(choice.Values,0,template),_project.Generation);
            if(key=="Marts"&&_project.Generation==6&&choice.Values.GetValueOrDefault("UseChecks")==1)
            {
                string[] flags=["Randomize","Candies","EVItems","BanCapsule"];string[] ids=["marts.randomize","marts.add-rare-candies","marts.add-ev-items","marts.ban-ability-capsule"];
                if(!flags.Any(flag=>choice.Values.GetValueOrDefault(flag)==1))throw new InvalidDataException("Tiendas: elegí al menos una acción.");
                for(int i=0;i<flags.Length;i++)if(choice.Values.GetValueOrDefault(flags[i])==1)Add(ids[i]);
                continue;
            }
            Add(key switch
            {
                "Trainers"=>"trainers.randomize","StaticEncounters"=>choice.Values.GetValueOrDefault("Species")==0&&choice.Values.GetValueOrDefault("ScaleLevels")==1?"static-encounters.modify-levels":"static-encounters.randomize","EggMoves"=>"egg-moves.randomize",
                "Evolutions"=>V(choice,"Operation") switch{1=>"evolutions.randomize",2=>"evolutions.every-level",3=>"evolutions.remove-trade",_=>throw new InvalidDataException("El asistente usa un motor original de evoluciones.")},
                "Wild"=>_project.Generation==7?NativeGen7WildBridge.ActionId(choice.Values):choice.Values.GetValueOrDefault("Species")==0&&choice.Values.GetValueOrDefault("ModifyLevel")==1?"wild-encounters.modify-levels":"wild-encounters.randomize",
                "Machines"=>"tms.randomize","Tutors"=>"move-tutors.randomize","FieldItems"=>"field-items.randomize","Marts"=>choice.Values.GetValueOrDefault("Operation")==1?"marts.add-rare-candies":"marts.randomize",_=>throw new InvalidDataException("Sección sin replay: "+key)
            });
        }
        if(doc.Actions.Count==0)return doc;
        ProjectFeatureAvailability.ValidateActions(_project,doc.Actions);GlobalEditorTemplateBridge.ValidateBatchReplay(doc);
        return doc;
    }
}
