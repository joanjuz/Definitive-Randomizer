using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Models;
using pk3DS.Core.Modding.Research;
namespace ThreeDS.Progressive.Services;

internal static class ManualOperations
{
    internal static readonly string[] RecordEditors=[nameof(GameTrainerService),nameof(GameEncounterService),nameof(GameEvolutionService),nameof(GameEggMoveService),nameof(GameItemService),nameof(GameMegaEvolutionService),nameof(GameStaticEncounterService),nameof(GameStaticEncounter6Service),nameof(GameMachine6Service),nameof(GameMart6Service),nameof(GameFieldItemsService),nameof(GameFieldItems6Service),nameof(GameTrade6Service),nameof(GameTutor6Service),nameof(GameOPowerService),nameof(GamePersistentItemsService),nameof(GameBattleFacilityService)];
    internal static IProjectEditor Editor(GameProjectSession project,string kind,string title)=>kind switch
    {
        nameof(GameTrainerService)=>new GameTrainerService(project),nameof(GameEncounterService)=>new GameEncounterService(project),nameof(GameEvolutionService)=>new GameEvolutionService(project),nameof(GameEggMoveService)=>new GameEggMoveService(project),nameof(GameItemService)=>new GameItemService(project),nameof(GameMegaEvolutionService)=>new GameMegaEvolutionService(project),nameof(GameStaticEncounterService)=>new GameStaticEncounterService(project),nameof(GameStaticEncounter6Service)=>new GameStaticEncounter6Service(project),nameof(GameMachine6Service)=>new GameMachine6Service(project),nameof(GameMart6Service)=>new GameMart6Service(project),nameof(GameFieldItemsService)=>new GameFieldItemsService(project),nameof(GameFieldItems6Service)=>new GameFieldItems6Service(project),nameof(GameTrade6Service)=>new GameTrade6Service(project),nameof(GameTutor6Service)=>new GameTutor6Service(project),nameof(GameOPowerService)=>new GameOPowerService(project),nameof(GamePersistentItemsService)=>new GamePersistentItemsService(project),nameof(GameBattleFacilityService)=>new GameBattleFacilityService(project,title.StartsWith("Super Maison",StringComparison.Ordinal)||title.StartsWith("Battle Royal",StringComparison.Ordinal)),_=>throw new InvalidDataException("Editor manual no compatible: "+kind)
    };
    internal static JsonObject[] Read(GlobalTemplateDocument doc)=>doc.Section("ProgressiveApp")?["ManualEdits"]?["Operations"] is JsonArray ops?ops.Select(o=>o as JsonObject??throw new InvalidDataException("Operación manual inválida.")).ToArray():[];
    internal static void Validate(GlobalTemplateDocument doc)
    {
        var keys=new HashSet<string>();var ops=Read(doc);if(ops.Length>100000)throw new InvalidDataException("Demasiadas operaciones manuales.");
        foreach(var op in ops)
        {
            string key=op["Key"]?.GetValue<string>()??"",kind=op["Kind"]?.GetValue<string>()??"";
            if(key.Length==0||!keys.Add(key)||op["Payload"] is not JsonObject p)throw new InvalidDataException("Operación manual duplicada o incompleta.");
            if(op["Language"] is {} language&&language.GetValue<int>() is <-1 or >8)throw new InvalidDataException("Idioma manual inválido.");
            switch(kind)
            {
                case "exact-overrides":if(p["Entries"] is not JsonArray exact||exact.Count==0)throw new InvalidDataException("Faltan los valores manuales finales.");break;
                case "authored-template":if(p["Asset"] is not JsonObject)throw new InvalidDataException("Falta el archivo del template creado.");break;
                case "record":
                    if(!RecordEditors.Contains(p["Editor"]?.GetValue<string>())||p["Id"]?.GetValue<int>()<0||p["Values"] is not JsonObject fields||fields.Count==0)throw new InvalidDataException("Entrada manual no válida.");
                    foreach(var field in fields)_=field.Value!.GetValue<int>();
                    if(p["Fields"] is JsonArray selected&&selected.Any(n=>n is null||!fields.ContainsKey(n.GetValue<string>())))throw new InvalidDataException("Campo manual no incluido en el registro.");break;
                case "move":_=p["Values"]!.Deserialize<MoveValues>()??throw new InvalidDataException("Movimiento vacío.");break;
                case "learnset":_=p["Rows"]!.Deserialize<LevelUpMoveEntry[]>()??throw new InvalidDataException("Aprendizaje vacío.");break;
                case "personal-flags":if(p["Bits"] is not JsonArray)throw new InvalidDataException("Faltan compatibilidades manuales.");break;
                case "resources":if(p["Files"] is not JsonArray)throw new InvalidDataException("Recursos manuales incompletos.");break;
                case "shops6":case "crointegrity":case "shiny":case "playercaps":case "typechart":break;
                case "shops7":if(p["Shops"] is not JsonArray)throw new InvalidDataException("Tiendas incompletas.");break;
                case "pickup":_=p["Rows"]!.Deserialize<PickupRow[]>()??throw new InvalidDataException("Pickup vacío.");break;
                case "machines7":case "tutors7":if(p["Rows"] is not JsonArray)throw new InvalidDataException("Tabla MT/tutores vacía.");break;
                default:throw new InvalidDataException("Operación manual no reconocida: "+kind);
            }
        }
    }
    private static Dictionary<string,int> RecordValues(IProjectEditor editor,int id,JsonObject payload)
    {
        var saved=payload["Values"]!.Deserialize<Dictionary<string,int>>()!;var current=EditorValues.Flatten(editor.Read(id));
        var fields=payload["Fields"] is JsonArray selected?selected.Select(n=>n!.GetValue<string>()).ToHashSet():saved.Keys.ToHashSet();
        if(!saved.Keys.ToHashSet().SetEquals(current.Keys)||fields.Any(k=>!saved.ContainsKey(k)))throw new InvalidDataException("La estructura del registro manual cambió: "+editor.Title);
        int previousCount=current.GetValueOrDefault("Count");foreach(string field in fields)current[field]=saved[field];
        if(editor is GameTrainerService)
        {
            int needed=0;foreach(string field in fields){int dot=field.IndexOf('.');if(dot>1&&field[0]=='P'&&int.TryParse(field.AsSpan(1,dot-1),out int slot))needed=Math.Max(needed,slot+1);}
            current["Count"]=Math.Max(current["Count"],needed);
            for(int slot=previousCount;slot<current["Count"];slot++)foreach(var value in saved.Where(p=>p.Key.StartsWith("P"+slot+".",StringComparison.Ordinal)))current[value.Key]=value.Value;
            if(current.ContainsKey("HasMoves")&&fields.Any(k=>k.StartsWith("P",StringComparison.Ordinal)&&k.Contains(".Move",StringComparison.Ordinal)))current["HasMoves"]=saved["HasMoves"];
            if(current.ContainsKey("HasItems")&&fields.Any(k=>k.StartsWith("P",StringComparison.Ordinal)&&k.EndsWith(".Item",StringComparison.Ordinal)))current["HasItems"]=saved["HasItems"];
        }
        return current;
    }
    internal static void Apply(GameProjectSession project,GlobalTemplateDocument doc,bool validateOnly)
    {
        var ordered=new List<JsonObject>();
        foreach(var operation in Read(doc).OrderBy(o=>o["Kind"]?.GetValue<string>()=="crointegrity"?1:0))
        {
            if(operation["Kind"]?.GetValue<string>()=="resources"&&ordered.LastOrDefault()?["Kind"]?.GetValue<string>()=="resources")
            {foreach(var file in operation["Payload"]!["Files"]!.AsArray())ordered[^1]["Payload"]!["Files"]!.AsArray().Add(file!.DeepClone());}
            else ordered.Add((JsonObject)operation.DeepClone());
        }
        foreach(var op in ordered)
        {
            int language=project.GameConfiguration.Language;
            try
            {
                if(op["Language"] is {} lang&&lang.GetValue<int>()>=0)project.GameConfiguration.Language=lang.GetValue<int>();
                var p=op["Payload"]!.AsObject();string kind=op["Kind"]!.GetValue<string>();
                switch(kind)
                {
                    case "exact-overrides":ManualEditTemplate.ApplyFinalOverrides(project,p,validateOnly);break;
                    case "authored-template":AuthoredTemplate.Replay(project,doc,p,validateOnly);break;
                    case "record":
                        var editor=Editor(project,p["Editor"]!.GetValue<string>(),p["Title"]!.GetValue<string>());int id=p["Id"]!.GetValue<int>();
                        if(!editor.Entries.Any(e=>e.Id==id))throw new InvalidDataException("No existe la entrada manual: "+id);
                        var fields=RecordValues(editor,id,p);var schema=editor.Read(id);
                        EditorValues.Validate(schema,fields);if(!validateOnly)editor.Save(id,fields);break;
                    case "move":
                        var moves=new GameMoveService(project);int moveId=p["Id"]!.GetValue<int>();var moveValues=JsonSerializer.SerializeToNode(moves.Read(moveId))!.AsObject();
                        foreach(string field in p["Fields"] is JsonArray changes?changes.Select(n=>n!.GetValue<string>()):p["Values"]!.AsObject().Select(v=>v.Key))
                        {if(!moveValues.ContainsKey(field)||p["Values"]?[field] is null)throw new InvalidDataException("Campo de movimiento desconocido.");moveValues[field]=p["Values"]![field]!.DeepClone();}
                        if(!validateOnly)moves.Save(moveId,moveValues.Deserialize<MoveValues>()!);break;
                    case "learnset":if(!validateOnly)new GameLevelUpService(project).Save(p["Id"]!.GetValue<int>(),p["Rows"]!.Deserialize<LevelUpMoveEntry[]>()!);break;
                    case "personal-flags":
                        string personalPath=Path.Combine(project.GameConfiguration.RomFS,project.GameConfiguration.GetGARCFileName("personal"));byte[] raw=File.ReadAllBytes(personalPath);var records=new pk3DS.Core.CTR.GARC.MemGARC(raw).Files;
                        foreach(var bit in p["Bits"]!.AsArray())
                        {
                            int entry=bit!["Id"]!.GetValue<int>(),size=bit["Size"]!.GetValue<int>(),offset=bit["Offset"]!.GetValue<int>(),mask=bit["Mask"]!.GetValue<int>(),value=bit["Bits"]!.GetValue<int>();
                            if(entry<1||entry>=records.Length-1||size!=records[entry].Length||offset<0||offset>=size||mask is <1 or >255||value<0||(value&~mask)!=0)throw new InvalidDataException("Compatibilidad manual fuera de límites.");
                            records[entry][offset]=(byte)((records[entry][offset]&~mask)|value);
                        }
                        GarcTransaction.RebuildPersonalMaster(records);var output=GarcTransaction.Build(raw,project.GameConfiguration.GetGARCReference("personal"),records);
                        if(!validateOnly){using var scope=ManualOperationScope.Begin(kind,op["Key"]!.GetValue<string>(),p);project.History.Commit([new FileChange(personalPath,raw,output)],"MT y tutores","Reproducir compatibilidades manuales",[]);project.RefreshPersonalAfterExternalEdit();}break;
                    case "resources":ManualResourceReplay.Apply(project,doc,p,validateOnly);break;
                    case "typechart":var chart=new GameTypeChartService(project);var chartPreview=chart.Preview(p["Values"]!.Deserialize<byte[]>()!,project.Generation==6);if(!validateOnly&&chartPreview.Changes.Count>0)chart.Apply(chartPreview.File,chartPreview.Changes);break;
                    case "shiny":var shiny=new GameShinyRateService(project);var shinyPreview=shiny.Preview(p["Rerolls"]!.GetValue<int>(),p["Everything"]!.GetValue<bool>(),p["Restore"]!.GetValue<bool>());if(!validateOnly&&shinyPreview.Changes.Count>0)shiny.Apply(shinyPreview.File,shinyPreview.Changes,p["Restore"]!.GetValue<bool>());break;
                    case "playercaps":var caps=new GamePlayerLevelCapService(project);var capPreview=caps.Preview(p["Table"]!.Deserialize<LevelCapTable>()!);if(!validateOnly)caps.Apply(capPreview);break;
                    case "crointegrity":var integrity=new GameCroIntegrityService(project);var hashes=integrity.Preview(p["UpdateCRR"]!.GetValue<bool>());if(!validateOnly&&hashes.Changes.Count>0){using var scope=ManualOperationScope.Begin(kind,op["Key"]!.GetValue<string>(),p);integrity.Apply(hashes);}break;
                    case "shops6":
                        var mart6=new GameMart6Service(project);var inventory=mart6.ReadAllItems();foreach(var shop in p["Shops"]!.AsArray()){int shopId=shop!["Id"]!.GetValue<int>();if(shopId<1||shopId>inventory.Length)throw new InvalidDataException("Tienda Gen6 manual inexistente.");inventory[shopId-1]=shop["Items"]!.Deserialize<int[]>()!;}
                        var options=EditorValues.Flatten(mart6.RandomOptions);options["UseChecks"]=1;
                        var draft=mart6.PrepareDraft(inventory,options,mart6.Entries.Select(e=>e.Id).ToArray(),0,p["Prices"]!.Deserialize<int[]>()!);if(!validateOnly&&draft.Changes.Count>0){using var scope=ManualOperationScope.Begin(kind,op["Key"]!.GetValue<string>(),p);mart6.Apply(draft);}break;
                    case "shops7":
                        var marts=new GameMartService(project);
                        foreach(var shop in p["Shops"]!.AsArray())
                        {
                            var rows=shop!["Rows"]!.AsArray();bool bp=shop["IsBP"]!.GetValue<bool>();int index=shop["Index"]!.GetValue<int>();
                            var list=bp?marts.BpShops:marts.RegularShops;if(index<0||index>=list.Count)throw new InvalidDataException("Tienda manual inexistente.");
                            var target=list[index];target.Rows.Clear();foreach(var row in rows)target.Rows.Add(new MartSlotRow(target.Rows.Count+1,row!["ItemId"]!.GetValue<int>(),row["Price"]!.GetValue<int>()));
                        }
                        marts.RestoreManualItemEffects(p);var preview=marts.Prepare();if(!validateOnly&&preview.Changes.Count>0){using var scope=ManualOperationScope.Begin(kind,op["Key"]!.GetValue<string>(),p);marts.Apply(preview);}break;
                    case "pickup":var pickup=new GamePickupService(project);var proposal=pickup.Preview(p["Rows"]!.Deserialize<PickupRow[]>()!);if(!validateOnly&&proposal.Changes.Count>0){using var scope=ManualOperationScope.Begin(kind,op["Key"]!.GetValue<string>(),p);pickup.Apply(proposal);}break;
                    case "machines7":case "tutors7":
                        var machines=new GameMachineTutorService(project);
                        foreach(var row in p["Rows"]!.AsArray())
                        {
                            int n=row!["Number"]!.GetValue<int>();
                            if(kind=="machines7"){var target=machines.Machines.Single(t=>t.Number==n);target.MoveId=row["MoveId"]!.GetValue<int>();}
                            else {var target=machines.Tutors.Single(t=>t.Number==n);if(row["MoveChanged"]?.GetValue<bool>()??true)target.MoveId=row["MoveId"]!.GetValue<int>();if(row["PriceChanged"]?.GetValue<bool>()??true)target.Price=row["Price"]!.GetValue<int>();}
                        }
                        if(kind=="machines7"){bool synchronize=p["SynchronizeDescriptions"]?.GetValue<bool>()??false;var change=machines.PrepareMachines(synchronize);if(!validateOnly&&change.Changes.Count>0){using var scope=ManualOperationScope.Begin(kind,op["Key"]!.GetValue<string>(),p);machines.SaveMachines(synchronize);}}
                        else {var tutorPreview=machines.CanEditTutorMoves?machines.PreviewTutorEdits():null;if(!validateOnly){using var scope=ManualOperationScope.Begin(kind,op["Key"]!.GetValue<string>(),p);if(tutorPreview is not null)machines.ApplyTutorEdits(tutorPreview);else machines.SaveTutorPrices();}}
                        break;
                }
            }
            finally{project.GameConfiguration.Language=language;}
        }
        if(!validateOnly){project.RefreshPersonalAfterExternalEdit();project.GameConfiguration.InitializeMoves();}
    }
}
