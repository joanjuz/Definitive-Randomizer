using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;

internal sealed partial class AuthoredTemplate
{
    public string Format {get;set;}="DefinitiveRandomizer.TemplateStudio.v1";
    public string Kind {get;set;}="Moves";
    public string ShopMode {get;set;}="Replace"; // Absent in old files: retain their original replay semantics.
    public int Generation {get;set;}
    public string Game {get;set;}="";
    public List<Dictionary<string,string>> Rows {get;set;}=[];
    public Dictionary<string,int> Options {get;set;}=[];
    public int[] BannedItems {get;set;}=[];
    public int[] AllowedItems {get;set;}=[];
    [System.Text.Json.Serialization.JsonIgnore] internal Dictionary<string,string> OptionLabels {get;set;}=[];
    [System.Text.Json.Serialization.JsonIgnore] internal TemplateChoice[] ShopLabels {get;set;}=[];
    internal void PrepareMetadata(GameProjectSession p)
    {
        if(Kind=="FieldItems")OptionLabels=FieldEditor(p).RandomOptions.SelectMany(g=>g.Fields).ToDictionary(f=>f.Key,f=>f.Label);
        if(Kind=="Shops")ShopLabels=Enumerable.Range(0,ProgressionStageCount(p)).Select(i=>new TemplateChoice(i.ToString(),StageLabel(p,i))).ToArray();
    }
    internal static readonly string[] Kinds=["Moves","Evolutions","Stats","FieldItems","Shops"];
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    internal static string[] Columns(string kind,int generation)=>kind switch
    {
        "Moves"=>["Move","Type","Category","Quality","Power","Accuracy","PP","Priority","HitMin","HitMax","CriticalStage","Flinch","Effect","Param0x0B","Heal","Recoil","TurnMin","TurnMax","Targeting","Inflict","InflictChance","ClearStatEffects","UserStat","UserStatChange","UserStatChance","TargetStat","TargetStatChange","TargetStatChance","Stat1","Stat1Change","Stat1Chance","Stat2","Stat2Change","Stat2Chance","Stat3","Stat3Change","Stat3Chance","ClearFlags","SetFlags","UnsetFlags",..(generation==7?new[]{"ZEffect"}:Array.Empty<string>()),"BattlePatch","InGameDescription"],
        "Evolutions"=>["Source","Target","Method","Level","Argument",..(generation==7?new[]{"Form"}:Array.Empty<string>())],
        "Stats"=>["Entry","HP","ATK","DEF","SPA","SPD","SPE","Notes"],
        "FieldItems"=>[],"Shops"=>["Stage","Item"],_=>throw new InvalidDataException("Tipo de template desconocido.")
    };
    internal static AuthoredTemplate Create(GameProjectSession p,string kind)
    {
        var d=new AuthoredTemplate{Kind=kind,Generation=p.Generation,Game=p.GameName};
        if(kind=="FieldItems")d.Options=EditorValues.Flatten(FieldEditor(p).RandomOptions);
        if(kind=="Shops")d.ShopMode="AppendProgression";
        return d;
    }
    internal static IProjectEditor FieldEditor(GameProjectSession p)=>p.Generation==6?new GameFieldItems6Service(p):new GameFieldItemsService(p);
    internal IReadOnlyList<BatchChange> LastChanges {get;private set;}=[];
    private int Count(IReadOnlyList<BatchChange> changes){LastChanges=changes;return changes.Count;}
    internal string Serialize()=>JsonSerializer.Serialize(this,Json);
    internal void ValidateShape(GameProjectSession p)
    {
        if(Rows is null||Options is null||BannedItems is null||AllowedItems is null)throw new InvalidDataException("Template incompleto.");
        if(Format!="DefinitiveRandomizer.TemplateStudio.v1"||!Kinds.Contains(Kind)||Generation!=p.Generation||Game!=p.GameName)throw new InvalidDataException("Template incompatible con el juego abierto.");
        if(Rows.Count>10000||Options.Count>100)throw new InvalidDataException("Template demasiado grande.");
        var fields=(Kind=="Shops"&&ShopMode=="Replace"?new[]{"Shop","Slot","Item","Price"}:Columns(Kind,Generation)).ToHashSet();
        if(Kind=="Shops")ValidateShopMode(p);
        foreach(var row in Rows)if(row.Keys.Any(k=>!fields.Contains(k))||row.Values.Any(v=>v is null||v.Length>16000))throw new InvalidDataException("Campo desconocido o texto demasiado largo.");
        if(BannedItems.Concat(AllowedItems).Any(id=>id<1||id>=p.ItemNames.Count)||BannedItems.Intersect(AllowedItems).Any()||BannedItems.Distinct().Count()!=BannedItems.Length||AllowedItems.Distinct().Count()!=AllowedItems.Length)throw new InvalidDataException("Catálogo de objetos inválido, repetido o contradictorio.");
        if(Kind is "Moves" or "Evolutions" or "Stats"&&Rows.Count==0)throw new InvalidDataException("Añadí al menos una fila.");
        if(Kind=="Evolutions"&&Rows.Any(row=>Number(row,"Method")>(p.Generation==6?33:p.GameConfiguration.USUM?42:39)))throw new InvalidDataException("Método de evolución no disponible en este juego.");
        if(Kind=="FieldItems"&&Rows.Count!=0)throw new InvalidDataException("Field Items usa el catálogo y sus opciones.");
        if(Kind=="Moves")foreach(var row in Rows)
        {
            int id=Number(row,"Move");string patch=row.GetValueOrDefault("BattlePatch","");
            if(patch.Length>0&&!BattlePatchCatalogue.For(p,id).Any(c=>c.Value==patch))throw new InvalidDataException($"BattlePatch incompatible con el movimiento {id}: {patch}");
            if(Generation==6&&row.GetValueOrDefault("ZEffect","").Length>0)throw new InvalidDataException("Los efectos Z requieren Gen7.");
        }
    }
    internal static int Number(IReadOnlyDictionary<string,string> row,string field,int fallback=0)=>!row.TryGetValue(field,out string? s)||string.IsNullOrWhiteSpace(s)?fallback:int.TryParse(s,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out int n)?n:throw new InvalidDataException("Número inválido en "+field+": "+s);
    internal string Csv()
    {
        string Quote(string s)=>"\""+s.Replace("\r\n","\\n").Replace("\n","\\n").Replace("\r","\\n").Replace("\"","\"\"")+"\"";
        string[] columns=Columns(Kind,Generation);
        return string.Join(",",columns)+"\n"+string.Join("\n",Rows.Select(row=>string.Join(",",columns.Select(k=>Quote(row.GetValueOrDefault(k,""))))))+"\n";
    }
    internal static AuthoredTemplate Import(GameProjectSession p,string kind,string path)
    {
        if(new FileInfo(path).Length>8*1024*1024)throw new InvalidDataException("El template supera 8 MiB.");
        string text=File.ReadAllText(path);var d=Create(p,kind);
        if(text.TrimStart().StartsWith('{'))
        {
            var node=JsonNode.Parse(text)!.AsObject();
            if(node["Format"]?.GetValue<string>()=="DefinitiveRandomizer.FieldItems.v1"&&kind=="FieldItems")
            {
                ((IOptionTemplateProjectEditor)FieldEditor(p)).RestoreOptions(text);
                d.BannedItems=node["BannedItems"]?.Deserialize<int[]>()??[];d.AllowedItems=node["AllowedItems"]?.Deserialize<int[]>()??[];
                foreach(var pair in node["Options"]?.AsObject()??new JsonObject())d.Options[pair.Key]=pair.Value!.GetValue<int>();return d;
            }
            d=JsonSerializer.Deserialize<AuthoredTemplate>(text)??throw new InvalidDataException("Template vacío.");if(d.Kind!=kind)throw new InvalidDataException("Elegí el mismo tipo que el archivo a importar.");d.CanonicalizeIds(p);d.ValidateShape(p);return d;
        }
        if(kind=="Moves")d.SetMoveRows(p,MoveTemplates.LoadMovePatches(path,p.MoveNames.ToArray()));
        else if(kind=="Evolutions")d.Rows=EvolutionTemplates.Load(path,p.GameConfiguration.GetText(pk3DS.Core.TextName.SpeciesNames),p.ItemNames.ToArray(),p.Generation).Select(e=>new Dictionary<string,string>{["Source"]=e.Source.ToString(),["Target"]=e.Target.ToString(),["Method"]=e.Method.ToString(),["Level"]=e.Level.ToString(),["Argument"]=e.Argument.ToString(),["Form"]=e.Form.ToString()}).ToList();
        else if(kind=="Stats")
        {
            var names=Names(p);foreach(var patch in PokemonStatTemplates.Load(path,names))d.Rows.Add(Columns(kind,p.Generation).ToDictionary(k=>k,k=>Convert.ToString(typeof(PokemonStatTemplates.PokemonStatPatchRow).GetProperty(k)?.GetValue(patch),System.Globalization.CultureInfo.InvariantCulture)??""));
        }
        else throw new InvalidDataException("Este tipo de template se importa como JSON.");
        if(p.Generation==6&&kind=="Evolutions")foreach(var row in d.Rows)row.Remove("Form");
        d.ValidateShape(p);return d;
    }
    private void SetMoveRows(GameProjectSession p,IEnumerable<MoveTemplates.MovePatchRow> patches)
    {
            foreach(var patch in patches)
            {
                var row=new Dictionary<string,string>();foreach(string col in Columns("Moves",p.Generation))
                {
                    object? value=typeof(MoveTemplates.MovePatchRow).GetProperty(col)?.GetValue(patch);
                    if(col=="Inflict")value=patch.Inflict.HasValue?patch.Inflict.Value.ToString():patch.InflictToken;
                    if(col=="InGameDescription"&&value is string description)value=description.Replace("\\n","\n");
                    row[col]=value is bool b?b?"true":"false":Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??"";
                }
                var tokens=new MoveTemplateApplier(p.Generation,p.Types.ToArray());
                foreach(string key in new[]{"Type","Category","Quality","Targeting","Inflict","UserStat","TargetStat","Stat1","Stat2","Stat3"})if(!string.IsNullOrWhiteSpace(row[key]))row[key]=tokens.ResolveTemplateChoice(key,row[key]).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if(patch.KingShieldAttackMinusOne)row["BattlePatch"]=p.Generation==6?"Gen6KingShieldMinusOne":"Gen7KingShieldMinusOne";
                Rows.Add(row);
            }
    }
    internal static string[] Names(GameProjectSession p)=>Enumerable.Range(0,p.EntryCount).Select(i=>p.Entries.FirstOrDefault(e=>e.Index==i)?.Name??"—").ToArray();
    internal void Export(string path)
    {
        RequireNumericIds();
        if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))
        {if(Kind is not ("Moves" or "Evolutions" or "Stats"))throw new InvalidDataException("Este template requiere JSON.");File.WriteAllText(path,Csv(),new UTF8Encoding(false));}
        else if(Kind=="FieldItems")File.WriteAllText(path,JsonSerializer.Serialize(new{Format="DefinitiveRandomizer.FieldItems.v1",Generation,BannedItems,AllowedItems,Options},Json));
        else File.WriteAllText(path,Serialize());
    }
    private static IEnumerable<int> ProgressivePool(GameProjectSession p,string kind)=>kind=="Shops"&&p.Generation==7?GameMartService.ProgressiveCatalog:ProgressiveLegacy.Randomizer.GetRandomItemList(p.GameConfiguration);
    internal static AuthoredTemplate Progressive(GameProjectSession p,string kind)
    {
        if(kind=="Moves"&&p.Generation==7&&!p.GameConfiguration.USUM)throw new InvalidDataException("El template de movimientos Progressive corresponde a USUM. En Sol/Luna podés crear tu propio template de datos de movimientos, sin los parches exclusivos de USUM.");
        if(kind is "Moves" or "Evolutions" or "Stats")
        {
            string file=(kind=="Moves"?"moves":kind=="Stats"?"pokemon_stats":"evolutions")+$"_gen{p.Generation}.csv";
            string path=Path.Combine(AppContext.BaseDirectory,"custom_balance_templates",file);
            if(kind=="Moves"&&(!File.Exists(path)||MoveTemplates.LoadMovePatches(path,p.MoveNames.ToArray()).Length==0)){var built=Create(p,kind);built.SetMoveRows(p,BuiltInMoveBalance.ForGeneration(p.Generation));built.ValidateShape(p);return built;}
            return Import(p,kind,path);
        }
        var d=Create(p,kind);if(kind=="Shops"){d.ShopMode="Replace";d.Options=LegacyShopOptions(p);}
        int[] allowed=ProgressivePool(p,kind).Where(id=>id>0&&id<p.ItemNames.Count).Distinct().Order().ToArray();
        d.AllowedItems=allowed;d.BannedItems=Enumerable.Range(1,p.ItemNames.Count-1).Except(allowed).Where(id=>kind!="Shops"||!(p.Generation==6?GameMart6Service.IsProtectedItem(id):GameMartService.IsProtectedItem(id))).ToArray();
        if(kind=="FieldItems"&&p.Generation==6)d.Options["RandomPool"]=1;
        if(kind=="Shops")d.Options[p.Generation==6?"Randomize":"RandomRegular"]=1;
        return d;
    }
    internal static JsonObject Store(GameProjectSession p,AuthoredTemplate d)
    {
        d.CanonicalizeIds(p);d.ValidateShape(p);byte[] bytes=Encoding.UTF8.GetBytes(d.Serialize());string hash=Convert.ToHexString(SHA256.HashData(bytes));string directory=Path.Combine(p.History.StorageDirectory,"authored-templates");Directory.CreateDirectory(directory);string name=hash+".bin",path=Path.Combine(directory,name);
        if(!File.Exists(path))File.WriteAllBytes(path,bytes);if(!File.ReadAllBytes(path).SequenceEqual(bytes))throw new IOException("La copia de template existente no coincide.");
        return new(){["Asset"]=new JsonObject{["Name"]=name,["Sha256"]=hash,["Directory"]=directory},["TemplateKind"]=d.Kind};
    }
    internal static int Replay(GameProjectSession p,GlobalTemplateDocument doc,JsonObject payload,bool validateOnly)
    {
        byte[] bytes=ManualResourceReplay.ReadAsset(payload["Asset"]!.AsObject(),doc);var d=JsonSerializer.Deserialize<AuthoredTemplate>(bytes)??throw new InvalidDataException("Template vacío.");
        d.ValidateShape(p);int seed=int.TryParse(Environment.GetEnvironmentVariable("DEFINITIVE_TEMPLATE_SEED"),out int value)?value:payload["Seed"]?.GetValue<int>()??12345;
        var recorded=(JsonObject)payload.DeepClone();var asset=recorded["Asset"]!.AsObject();string directory=asset["Directory"]!.GetValue<string>();if(!Path.IsPathRooted(directory))asset["Directory"]=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc.SourcePath)!,directory));
        if(!validateOnly){recorded=Store(p,d);if(payload["Seed"] is {} savedSeed)recorded["Seed"]=savedSeed.DeepClone();}
        int count=d.Run(p,seed,validateOnly,recorded);if(!validateOnly)p.History.RecordTemplateUse(recorded);return count;
    }
    internal int Run(GameProjectSession p,int seed,bool validateOnly,JsonObject? payload=null)
    {
        ValidateShape(p);string? temp=null;using var scope=payload is null?null:ManualOperationScope.Begin("authored-template","template:"+payload["Asset"]!["Sha256"]!.GetValue<string>(),payload);
        try
        {
            if(Kind is "Moves" or "Evolutions" or "Stats")
            {
                temp=Path.Combine(Path.GetTempPath(),"definitive-template-"+Guid.NewGuid().ToString("N")+".csv");File.WriteAllText(temp,Csv(),new UTF8Encoding(false));
                if(Kind=="Moves")
                {
                    var service=new GameMoveService(p);bool patches=Rows.Any(r=>!string.IsNullOrWhiteSpace(r.GetValueOrDefault("BattlePatch")));
                    var preview=service.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{Seed=seed,TemplatePath=temp,BattlePatches=patches,ExplicitBattlePatchSelection=true,MoveDescriptions=true},Rows.Select(r=>Number(r,"Move")).Distinct().ToArray());
                    if(!validateOnly)service.ApplyMovePreview(preview);return Count(preview.Changes);
                }
                if(Kind=="Evolutions")
                {var service=new GameEvolutionService(p);var preview=service.PreviewTemplate(temp,Rows.Select(r=>Number(r,"Source")).Distinct().ToArray());if(!validateOnly)service.Apply(preview);return Count(preview.Changes);}
                var stats=p.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings{Seed=seed,PersonalTemplate=true,TemplatePath=temp},Rows.Select(r=>Number(r,"Entry")).Distinct().ToArray());if(!validateOnly)p.ApplyPersonalPreview(stats);return Count(stats.Changes);
            }
            if(Kind=="FieldItems")
            {
                var editor=FieldEditor(p);string json=JsonSerializer.Serialize(new{Format="DefinitiveRandomizer.FieldItems.v1",Generation,BannedItems,AllowedItems,Options});((IOptionTemplateProjectEditor)editor).RestoreOptions(json);
                var proposal=editor is GameFieldItems6Service six?six.Preview(Options,editor.Entries.Select(e=>e.Id).ToArray(),seed):((GameFieldItemsService)editor).Preview(Options,editor.Entries.Select(e=>e.Id).ToArray(),seed);
                if(!validateOnly)editor.Apply(proposal);return Count(proposal.Changes);
            }
            return ShopMode=="AppendProgression"?RunShopAdditions(p,seed,validateOnly):RunShops(p,seed,validateOnly);
        }
        finally{if(temp is not null&&File.Exists(temp))File.Delete(temp);}
    }
    private int RunShops(GameProjectSession p,int seed,bool validateOnly)
    {
        bool On(string k)=>Options.GetValueOrDefault(k)==1;
        if(p.Generation==6&&(Options.GetValueOrDefault("UseChecks")!=1||Options.GetValueOrDefault("Operation")!=0))throw new InvalidDataException("El template de tiendas Gen6 requiere presets combinados (UseChecks=1, Operation=0).");
        var expected=LegacyShopOptions(p);if(!Options.Keys.ToHashSet().SetEquals(expected.Keys)||Options.Values.Any(v=>v is <0 or >1))throw new InvalidDataException("Opciones de tiendas incompletas o inválidas.");
        var rng=new Random(seed);int[] pool=(AllowedItems.Length>0?AllowedItems:ProgressivePool(p,"Shops")).Where(i=>i>0&&i<p.ItemNames.Count&&!BannedItems.Contains(i)&&(!On("BanCapsule")||i!=645)).Distinct().ToArray();
        int Filter(int item)=>item>0&&BannedItems.Contains(item)?pool.Length>0?pool[rng.Next(pool.Length)]:throw new InvalidDataException("Los filtros dejan vacío el catálogo de reemplazo."):item;
        var keys=new HashSet<(int,int)>();foreach(var row in Rows)if(!keys.Add((Number(row,"Shop"),Number(row,"Slot")))||Number(row,"Slot")<1||Number(row,"Item")<0||Number(row,"Item")>=p.ItemNames.Count||Number(row,"Price") is <0 or >65535||BannedItems.Contains(Number(row,"Item")))throw new InvalidDataException("Fila de tienda inválida, duplicada u objeto prohibido.");
        if(p.Generation==6)
        {
            if(Rows.Any(r=>Number(r,"Price")!=0))throw new InvalidDataException("Gen6 no tiene precios BP por ranura; dejá Precio en 0.");
            var service=new GameMart6Service(p);var inventory=service.ReadAllItems().Select(a=>a.ToList()).ToArray();
            foreach(var group in Rows.GroupBy(r=>Number(r,"Shop")))
            {
                if(group.Key<1||group.Key>inventory.Length)throw new InvalidDataException("Tienda inexistente.");var rows=group.OrderBy(r=>Number(r,"Slot")).ToArray();if(!rows.Select(r=>Number(r,"Slot")).SequenceEqual(Enumerable.Range(1,rows.Length)))throw new InvalidDataException("Las ranuras de cada tienda deben empezar en 1 y ser consecutivas.");inventory[group.Key-1]=rows.Select(r=>Number(r,"Item")).ToList();
            }
            foreach(var list in inventory)for(int i=0;i<list.Count;i++)list[i]=Filter(list[i]);
            var proposal=service.PrepareDraft(inventory.Select(a=>a.ToArray()).ToArray(),Options,service.Entries.Select(e=>e.Id).ToArray(),seed,customPool:AllowedItems.Length>0?AllowedItems:null,banned:BannedItems.ToHashSet(),exactShops:Rows.GroupBy(row=>Number(row,"Shop")).ToDictionary(g=>g.Key,g=>g.OrderBy(row=>Number(row,"Slot")).Select(row=>Number(row,"Item")).ToArray()));
            if(!validateOnly)service.Apply(proposal);return Count(proposal.Changes);
        }
        var marts=new GameMartService(p);var state=new Gen7MartState(On("RandomRegular"),On("RandomBP"),On("Candies"),On("Vitamins"),On("FreeMega"),On("BanCapsule"),On("SpecialsOnly"),On("ProtectX"));
        if(state.RandomRegular)marts.RandomizeShops(false,state.SpecialsOnly,state.ProtectX,seed,AllowedItems.Length>0?AllowedItems:null,BannedItems.ToHashSet());
        if(state.RandomBP)marts.RandomizeShops(true,seed:seed,customPool:AllowedItems.Length>0?AllowedItems:null,banned:BannedItems.ToHashSet());
        if(state.Candies)marts.AddRareCandies();if(state.Vitamins)marts.AddVitamins252();if(state.FreeMega)marts.FreeMegaStoneBP();if(state.BanCapsule)marts.BanAbilityCapsule(seed,AllowedItems.Length>0?AllowedItems:null,BannedItems.ToHashSet());
        var shops=marts.RegularShops.Concat(marts.BpShops).ToDictionary(s=>s.IsBP?1000+s.Index+1:s.Index+1);
        foreach(var group in Rows.GroupBy(r=>Number(r,"Shop")))
        {
            if(!shops.TryGetValue(group.Key,out var shop))throw new InvalidDataException("Tienda inexistente.");var rows=group.OrderBy(r=>Number(r,"Slot")).ToArray();if(!rows.Select(r=>Number(r,"Slot")).SequenceEqual(Enumerable.Range(1,rows.Length)))throw new InvalidDataException("Las ranuras de cada tienda deben empezar en 1 y ser consecutivas.");
            shop.Rows.Clear();foreach(var row in rows)shop.Rows.Add(new MartSlotRow(shop.Rows.Count+1,Number(row,"Item"),Number(row,"Price")));
        }
        foreach(var shop in shops.Values)foreach(var row in shop.Rows)
        {
            row.ItemId=Filter(row.ItemId);
        }
        var preview=marts.Prepare();if(!validateOnly)marts.Apply(preview);return Count(preview.Changes);
    }
}
