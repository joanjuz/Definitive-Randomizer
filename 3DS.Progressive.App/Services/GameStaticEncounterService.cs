using System.IO;
using pk3DS.Core;
using pk3DS.Core.Structures;
using pk3DS.Core.Randomizers;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Native Gen7 Gifts / Static Encounters / Trades editor.
/// Preserves unknown bytes and all unrelated slots in encounterstatic GARC.
/// Randomization is based on a frozen scoped preview, never a direct ROM write.
/// </summary>
public sealed partial class GameStaticEncounterService : ArchiveProjectEditor
{
    private sealed record Slot(int File,int Index,int Size,string Kind);
    private readonly Dictionary<int,Slot> _slots=[];
    public GameStaticEncounterService(GameProjectSession project):base(project,"encounterstatic")
    {
        if(Config.Generation!=7)
            throw new NotSupportedException("El editor de encuentros estáticos requiere Gen 7. Gen 6 tendrá su propio formato.");
        var files=Records()["encounterstatic"];
        if(files.Length<=4)throw new InvalidDataException("El archivo encounterstatic no tiene las secciones Gifts, Encounters y Trades.");
        Add(0,EncounterGift7.SIZE,"Regalo");
        Add(1,EncounterStatic7.SIZE,"Encuentro estático");
        Add(4,EncounterTrade7.SIZE,"Intercambio");
        void Add(int file,int size,string kind)
        {
            if(files[file].Length%size!=0)throw new InvalidDataException($"{kind}: longitud de datos incompatible.");
            for(int i=0;i<files[file].Length/size;i++)
            {
                byte[] bytes=files[file].AsSpan(i*size,size).ToArray();
                int species=BitConverter.ToUInt16(bytes,0);
                int id=_slots.Count+1;
                _slots.Add(id,new(file,i,size,kind));
                Entries=Entries.Append(new RecordEntry(id,$"{kind} {i+1:000} · {SpeciesLabel(species)}")).ToArray();
            }
        }
        if(_slots.Count==0)throw new InvalidDataException("No se encontraron encuentros estáticos editables.");
        CaptureStarterText();
    }
    public override string Title=>"Encuentros estáticos";
    private string SpeciesLabel(int id)
    {
        var names=Config.GetText(TextName.SpeciesNames);
        return id>=0&&id<names.Length&&!string.IsNullOrWhiteSpace(names[id])?names[id]:$"Especie #{id}";
    }
    private byte[] Entry(int id,Dictionary<string,byte[][]> data)
    {
        var slot=_slots[id];return data["encounterstatic"][slot.File].AsSpan(slot.Index*slot.Size,slot.Size).ToArray();
    }
    public override IReadOnlyList<EditorGroup> RandomOptions=>[
        new("Especies permitidas",[
            Field("Species","Randomizar especies",1,0,1),
            Field("AllowMega","Permitir formas Mega",0,0,1),
            Field("BasicStarter","Iniciales básicos en los primeros tres regalos",0,0,1),
            Field("G1","Generación 1",1,0,1),Field("G2","Generación 2",1,0,1),
            Field("G3","Generación 3",1,0,1),Field("G4","Generación 4",1,0,1),
            Field("G5","Generación 5",1,0,1),Field("G6","Generación 6",1,0,1),
            Field("G7","Generación 7",1,0,1),
            Field("Legends","Permitir Pokémon legendarios",1,0,1),
            Field("Events","Permitir Pokémon de evento",1,0,1),
            Field("SimilarBST","Limitar especies por BST similar",0,0,1),
            Field("BST","Tolerancia BST (%) · 0 = libre",30,0,100),
            Field("LegendToLegend","Reemplazar legendarios por legendarios",0,0,1),
            Field("NoLegendGifts","Bloquear legendarios en regalos e intercambios",1,0,1)
        ]),
        new("Alcance de randomización",[
            Field("Gifts","Incluir regalos e iniciales",1,0,1),
            Field("Static","Incluir encuentros estáticos y Totems",1,0,1),
            Field("Trades","Incluir Pokémon ofrecidos en intercambios",1,0,1),
            Field("ScaleLevels","Modificar niveles",0,0,1),
            Field("LevelScale","Multiplicar niveles (%)",100,10,300),
            Field("LevelFlat","Sumar niveles",0,-100,100)
        ]),
        new("Regalos, habilidades y objetos",[
            Field("RandomItems","Randomizar objetos equipados",0,0,1),
            Field("RandomAbilities","Randomizar slots de habilidad",0,0,1),
            Field("GiftMoves","Randomizar movimiento especial de regalos",0,0,1),
            Field("Metronome","Modo Metrónomo · estáticos; regalos no iniciales sin movimiento especial",0,0,1),
            Field("ShinyLock","Conservar restricciones shiny",1,0,1),
            Field("RemoveLocks","Eliminar shiny locks (regalos/estáticos)",0,0,1)
        ]),
        new("Encuentros Totem y especiales",[
            Field("TotemAura","Randomizar aura de Totem existente",0,0,1),
            Field("ForceTotem","Reemplazar especies Totem por evoluciones finales",0,0,1),
            Field("ForceFinal","Usar evoluciones finales desde un nivel",0,0,1),
            Field("FinalLevel","Nivel para evolución final",36,1,100)
        ],false)
    ];
    internal GameProjectSession ProjectSession=>Project;
    public bool SupportsTotemRules=>Config.USUM;
    private static readonly int[] TotemEntryIds=[4,9,137,249,24,146,39,45,162,160,229,231];
    // USUM Totem battles (and Ultra Necrozma) cannot be caught, so their forms are not obtainable.
    protected override bool ObtainableRecord(int id)=>!(Config.USUM&&_slots[id].File==1&&TotemEntryIds.Contains(_slots[id].Index));
    /// <summary>Record of a 0-based entry of encounterstatic file 0 (gifts), 1 (static) or 4 (trades).</summary>
    internal int RecordId(int file,int index)=>_slots.First(p=>p.Value.File==file&&p.Value.Index==index).Key;

    /// <summary>Exact 0-based original Static Encounter indexes, not UI IDs.</summary>
    public IReadOnlyList<TotemRuleRow> TotemRules()
    {
        if(!SupportsTotemRules)throw new NotSupportedException("Los Totem especiales requieren USUM.");
        var records=Records();
        var answer=new List<TotemRuleRow>();
        foreach(int index in TotemEntryIds)
        {
            var id=_slots.FirstOrDefault(k=>k.Value.File==1&&k.Value.Index==index).Key;
            if(id==0)continue;
            var encounter=new EncounterStatic7(Entry(id,records));
            int species=encounter.Species,bst=Config.Personal[species].BST;
            try
            {
                int formIndex=Config.Personal.GetFormIndex(species,encounter.Form);
                if(formIndex>=0&&formIndex<Config.Personal.Table.Length)
                    bst=Config.Personal[formIndex].BST;
            }
            catch(ArgumentException){}catch(IndexOutOfRangeException){}
            (int min,int max)=index switch
            {
                4 or 9=>(380,450),137=>(420,500),249=>(400,480),
                24=>(440,520),146=>(420,500),39=>(440,520),
                45=>(540,640),162=>(480,560),160=>(700,800),
                229 or 231=>(520,680),_=>(300,600)
            };
            string group=index switch
            {
                4=>"Primer Totem (US)",9=>"Primer Totem (UM)",160=>"Ultra Necrozma",
                229=>"Postgame US",231=>"Postgame UM",_=>"Totem de prueba"
            };
            answer.Add(new TotemRuleRow
            {
                Id=index,RecordId=id,Group=group,Name=SpeciesLabel(species),
                CurrentPokemon=SpeciesLabel(species),CurrentLevel=encounter.Level,
                CurrentBST=bst,LevelCap=encounter.Level,MinBST=min,MaxBST=max
            });
        }
        return answer.OrderBy(x=>x.CurrentLevel).ThenBy(x=>x.Id).ToArray();
    }

    public EditorProposal PreviewGiftBstBaseline()
    {
        var records=Records();
        var giftSlots=_slots.Where(x=>x.Value.File==0&&x.Value.Index>=3)
            .OrderBy(x=>x.Value.Index).ToArray();
        var changes=new List<BatchChange>();
        foreach(var (id,slot) in giftSlots)
        {
            int desired=slot.Index is >=4 and <=13?7:59;
            var before=ReadRecord(id,records);var values=EditorValues.Flatten(before);
            values["Species"]=desired;values["Form"]=0;
            WriteRecord(id,values,records);
            changes.AddRange(EditorValues.Diff(id,Entries.First(x=>x.Id==id).Name,before,ReadRecord(id,records)));
        }
        return new EditorProposal(ProposalOwner,"Establecer BST base de regalos",0,Build(records),changes,
            new Dictionary<string,string>
            {
                ["Scope"]="Regalos 3+, excluyendo iniciales 0-2",
                ["Rule"]="Regalos 4-13: Squirtle / restantes: Arcanine"
            });
    }

    private const int TradeHookOffset=0x2843F4;
    private const int TradeWrapperOffset=0x4B9C00;
    private static readonly byte[] TradeCleanHook=[0xEA,0x68,0xFD,0xEB];
    private static readonly byte[] TradePatchedHook=[0x01,0xD6,0x08,0xEB];
    private static readonly byte[] TradePatchedWrapper=[
        0x00,0x00,0x51,0xE3,0x1E,0xFF,0x2F,0x01,0xE5,0x92,0xF4,0xEA
    ];
    private string TradeCodePath()
    {
        string dir=Config.ExeFS;
        if(!Directory.Exists(dir))throw new DirectoryNotFoundException("ExeFS no disponible.");
        string[] candidates=[Path.Combine(dir,".code.bin"),Path.Combine(dir,"code.bin")];
        return candidates.FirstOrDefault(File.Exists)
            ??Directory.EnumerateFiles(dir,"*code*.bin").FirstOrDefault()
            ??throw new FileNotFoundException("Se requiere el code.bin descomprimido de USUM.");
    }
    private static bool HasBytes(byte[] bytes,int offset,byte[] expected)=>
        offset>=0&&offset+expected.Length<=bytes.Length
            &&bytes.AsSpan(offset,expected.Length).SequenceEqual(expected);
    private bool HasTradePatch()
    {
        if(!Config.USUM)return false;
        var data=File.ReadAllBytes(TradeCodePath());
        return HasBytes(data,TradeHookOffset,TradePatchedHook)
            &&HasBytes(data,TradeWrapperOffset,TradePatchedWrapper);
    }
    public EditorProposal PreviewTradeAcceptAny()
    {
        if(!Config.USUM)throw new NotSupportedException("Accept Any Pokémon solo admite Ultra Sun/Ultra Moon.");
        string codePath=TradeCodePath();
        byte[] source=File.ReadAllBytes(codePath);
        if(source.Length<TradeWrapperOffset+TradePatchedWrapper.Length)
            throw new InvalidDataException("code.bin incompleto para el parche de intercambios.");
        bool cleanHook=HasBytes(source,TradeHookOffset,TradeCleanHook);
        bool patchedHook=HasBytes(source,TradeHookOffset,TradePatchedHook);
        bool cleanWrapper=HasBytes(source,TradeWrapperOffset,new byte[12]);
        bool patchedWrapper=HasBytes(source,TradeWrapperOffset,TradePatchedWrapper);
        if((!cleanHook&&!patchedHook)||(!cleanWrapper&&!patchedWrapper))
            throw new InvalidDataException("Firma del hook USUM incompatible o zona code-cave ya utilizada. Ningún byte fue escrito.");
        byte[] patched=(byte[])source.Clone();
        TradePatchedHook.CopyTo(patched,TradeHookOffset);
        TradePatchedWrapper.CopyTo(patched,TradeWrapperOffset);
        var data=Records();var changes=new List<BatchChange>();
        foreach(var pair in _slots.Where(k=>k.Value.File==4))
        {
            int id=pair.Key;var before=ReadRecord(id,data);
            var slot=pair.Value;
            var raw=Entry(id,data);
            var trade=new EncounterTrade7(raw);
            if(trade.TradeRequestSpecies==0)continue;
            trade.TradeRequestSpecies=0;
            raw.CopyTo(data["encounterstatic"][slot.File],slot.Index*slot.Size);
            changes.AddRange(EditorValues.Diff(id,Entries.First(x=>x.Id==id).Name,before,ReadRecord(id,data)));
        }
        return new EditorProposal(ProposalOwner,"Trades Accept Any Pokémon",0,
            [..Build(data),new FileChange(codePath,source,patched)],changes,
            new Dictionary<string,string>
            {
                ["Scope"]="Todos los intercambios USUM",
                ["Patch"]="TradeHook 2843F4 / Wrapper 4B9C00",
                ["Integrity"]="Hook original y code-cave comprobados; escritura atómica de los dos archivos"
            });
    }
    public EditorProposal PreviewTotemRules(IReadOnlyList<TotemRuleRow> rules,bool bstMode)
    {
        if(!SupportsTotemRules)throw new NotSupportedException("Solo USUM.");
        if(rules.GroupBy(x=>x.Id).Any(x=>x.Count()>1))
            throw new ArgumentException("La tabla de Totem contiene índices duplicados.");
        var records=Records();
        var baseline=TotemRules().ToDictionary(r=>r.Id);
        var random=new Random(System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue));
        var changes=new List<BatchChange>();
        foreach(var rule in rules.Where(x=>x.Use))
        {
            if(!baseline.TryGetValue(rule.Id,out var expected)||expected.RecordId!=rule.RecordId)
                throw new ArgumentException($"ID Totem {rule.Id} no pertenece a esta ROM.");
            int id=expected.RecordId;
            var before=ReadRecord(id,records);
            var values=EditorValues.Flatten(before);
            if(bstMode)
            {
                if(rule.MinBST is <1 or >1530||rule.MaxBST is <1 or >1530||rule.MaxBST<rule.MinBST)
                    throw new ArgumentException($"Totem {rule.Id}: intervalo BST inválido.");
                var choices=Enumerable.Range(1,Math.Min(Config.MaxSpeciesID,Config.Personal.Table.Length-1))
                    .Where(species=>Config.Personal[species].BST>=rule.MinBST
                        &&Config.Personal[species].BST<=rule.MaxBST)
                    .ToArray();
                if(choices.Length==0)
                    throw new InvalidDataException($"Totem {rule.Id}: no hay especies en el intervalo BST seleccionado.");
                int[] alternatives=choices.Where(x=>x!=values["Species"]).ToArray();
                values["Species"]=(alternatives.Length>0?alternatives:choices)[random.Next(
                    alternatives.Length>0?alternatives.Length:choices.Length)];
                values["Form"]=0;
            }
            else
            {
                if(rule.LevelCap is <0 or >100)
                    throw new ArgumentException($"Totem {rule.Id}: cap fuera de 0–100.");
                if(rule.LevelCap>0)values["Level"]=rule.LevelCap;
            }
            if(EditorValues.Flatten(before)["Species"]!=values["Species"]||EditorValues.Flatten(before)["Level"]!=values["Level"])
            {
                // Refresh learnable moves to the candidate's current level like the
                // original Totem editor, but keep all unrelated static flags intact.
                int species=values["Species"],level=values["Level"];
                int formIndex=Config.Personal.GetFormIndex(species,values["Form"]);
                if(formIndex>=0&&formIndex<Config.Learnsets.Length)
                {
                    int[] moves=Config.Learnsets[formIndex].GetEncounterMoves(level);
                    for(int m=0;m<4;m++)values[$"Move{m}"]=m<moves.Length?moves[m]:0;
                }
            }
            WriteRecord(id,values,records);
            changes.AddRange(EditorValues.Diff(id,Entries.First(e=>e.Id==id).Name,before,ReadRecord(id,records)));
        }
        return new EditorProposal(ProposalOwner,bstMode?"Randomizar Totem BST":"Aplicar Totem Level Caps",0,
            Build(records),changes,new Dictionary<string,string>
            {
                ["Scope"]=string.Join(",",rules.Where(x=>x.Use).Select(x=>x.Id)),
                ["RuleType"]=bstMode?"TotemBST":"TotemLevelCaps"
            });
    }
    protected override IReadOnlyList<EditorGroup> ReadRecord(int id,Dictionary<string,byte[][]> records)
    {
        var slot=_slots[id];byte[] raw=Entry(id,records);
        var fields=new List<EditorField>();
        fields.Add(Field("Species","Especie",BitConverter.ToUInt16(raw,0),0,Config.MaxSpeciesID,SpeciesChoices));
        fields.Add(Field("Form","Forma",slot.File==4?raw[4]:raw[2],0,255));
        fields.Add(Field("Level","Nivel",slot.File==4?raw[5]:raw[3],1,100));
        if(slot.File==0)
        {
            var gift=new EncounterGift7(raw);
            fields.Add(Field("Gender","Género",gift.Gender,0,1));
            fields.Add(Field("Ability","Habilidad / -1 = normal",gift.Ability,-1,2));
            fields.Add(Field("Nature","Naturaleza / -1 = aleatoria",gift.Nature,-1,24));
            fields.Add(Field("Item","Objeto equipado",gift.HeldItem,0,65535,Choices(TextName.ItemNames)));
            fields.Add(Field("Egg","Huevo",gift.IsEgg?1:0,0,1));
            fields.Add(Field("ShinyLock","Bloqueo shiny",gift.ShinyLock?1:0,0,1));
            fields.Add(Field("Move","Movimiento especial",gift.SpecialMove,0,65535,Choices(TextName.MoveNames)));
        }
        else if(slot.File==1)
        {
            var enc=new EncounterStatic7(raw);
            fields.Add(Field("Item","Objeto equipado",enc.HeldItem,0,32767,Choices(TextName.ItemNames)));
            fields.Add(Field("Gender","Género",enc.Gender,0,3));
            fields.Add(Field("Ability","Slot de habilidad",enc.Ability,0,7));
            fields.Add(Field("Nature","Naturaleza",enc.Nature,0,255,Choices(TextName.Natures)));
            fields.Add(Field("Shiny","Permitir shiny",((raw[6]&1)!=0)?1:0,0,1));
            fields.Add(Field("ShinyLock","Bloqueo shiny",enc.ShinyLock?1:0,0,1));
            fields.Add(Field("Aura","Aura de Totem",enc.Aura,0,255));
            fields.Add(Field("Allies","Cantidad / tipo de aliados",enc.Allies,0,255));
            fields.Add(Field("Ally1","Primer aliado",enc.Ally1,0,255));
            fields.Add(Field("Ally2","Segundo aliado",enc.Ally2,0,255));
            fields.Add(Field("Map","Mapa",enc.Map,-32769,32766));
            AddStats(enc.IVs,enc.EVs);
            for(int i=0;i<4;i++)fields.Add(Field($"Move{i}","Movimiento recordable "+(i+1),enc.RelearnMoves[i],0,65535,Choices(TextName.MoveNames)));
        }
        else
        {
            var trade=new EncounterTrade7(raw);
            fields.Add(Field("Request","Pokémon solicitado",trade.TradeRequestSpecies,0,Config.MaxSpeciesID,SpeciesChoices));
            fields.Add(Field("Item","Objeto equipado",trade.HeldItem,0,32767,Choices(TextName.ItemNames)));
            fields.Add(Field("Ability","Habilidad",trade.Ability,0,255));
            fields.Add(Field("Nature","Naturaleza",trade.Nature,0,255,Choices(TextName.Natures)));
            fields.Add(Field("Gender","Género",trade.Gender,0,255));
            fields.Add(Field("TID","ID entrenador",trade.TID,0,65535));
            fields.Add(Field("SID","ID secreto",trade.SID,0,65535));
            AddStats(trade.IVs,null);
        }
        return [new(slot.Kind+" "+(slot.Index+1),fields)];
        void AddStats(int[] ivs,int[]? evs)
        {
            string[] names=["PS","Ataque","Defensa","Velocidad","Ataque especial","Defensa especial"];
            for(int i=0;i<6;i++)
            {
                fields.Add(Field($"IV{i}",$"IV {names[i]} · -1 aleatorio; -4 en PS = tres perfectos",ivs[i],sbyte.MinValue,sbyte.MaxValue));
                if(evs!=null)fields.Add(Field($"EV{i}",$"EV {names[i]}",evs[i],0,255));
            }
        }
    }
    protected override void WriteRecord(int id,IReadOnlyDictionary<string,int> v,Dictionary<string,byte[][]> records)
    {
        var slot=_slots[id];byte[] bytes=Entry(id,records);
        if(slot.File==0)
        {
            var gift=new EncounterGift7(bytes);
            gift.Species=v["Species"];gift.Form=v["Form"];gift.Level=v["Level"];
            // Preserve the original unknown gender/lock bits.
            bytes[4]=(byte)((bytes[4]&~1)|(v["Gender"]&1));
            gift.Ability=(sbyte)v["Ability"];gift.Nature=(sbyte)v["Nature"];
            gift.HeldItem=v["Item"];gift.IsEgg=v["Egg"]==1;
            gift.ShinyLock=v["ShinyLock"]==1;gift.SpecialMove=v["Move"];
        }
        else if(slot.File==1)
        {
            var enc=new EncounterStatic7(bytes);
            enc.Species=v["Species"];enc.Form=v["Form"];enc.Level=v["Level"];
            if(v["Item"]!=enc.HeldItem)enc.HeldItem=v["Item"];enc.Gender=v["Gender"];enc.Ability=v["Ability"];enc.Nature=v["Nature"];
            bytes[6]=(byte)((bytes[6]&~3)|(v["Shiny"]==1?1:0)|(v["ShinyLock"]==1?2:0));
            enc.Aura=v["Aura"];enc.Allies=v["Allies"];
            enc.Ally1=v["Ally1"];enc.Ally2=v["Ally2"];enc.Map=v["Map"];
            enc.IVs=Enumerable.Range(0,6).Select(i=>v[$"IV{i}"]).ToArray();
            enc.EVs=Enumerable.Range(0,6).Select(i=>v[$"EV{i}"]).ToArray();
            enc.RelearnMoves=Enumerable.Range(0,4).Select(i=>v[$"Move{i}"]).ToArray();
        }
        else
        {
            var trade=new EncounterTrade7(bytes);
            trade.Species=v["Species"];trade.Form=v["Form"];trade.Level=v["Level"];
            if(v["Request"]==0&&trade.TradeRequestSpecies!=0&&!HasTradePatch())
                throw new NotSupportedException("Para aceptar cualquier Pokémon, aplicá primero el parche del selector de intercambios USUM.");
            trade.TradeRequestSpecies=v["Request"];
            if(v["Item"]!=trade.HeldItem)trade.HeldItem=v["Item"];
            trade.Ability=v["Ability"];trade.Nature=v["Nature"];trade.Gender=v["Gender"];
            trade.TID=v["TID"];trade.SID=v["SID"];
            trade.IVs=Enumerable.Range(0,6).Select(i=>v[$"IV{i}"]).ToArray();
        }
        bytes.CopyTo(records["encounterstatic"][slot.File],slot.Index*slot.Size);
    }
    private int PickFilteredSpecies(int original,int level,bool giftOrTrade,
        IReadOnlyDictionary<string,int> options,Random random)
    {
        var filter=new SpeciesRandomizer(Config)
        {
            G1=options["G1"]==1,G2=options["G2"]==1,G3=options["G3"]==1,
            G4=options["G4"]==1,G5=options["G5"]==1,G6=options["G6"]==1,
            G7=options["G7"]==1,L=options["Legends"]==1,E=options["Events"]==1,rBST=false
        };
        var legendary=new HashSet<int>(Config.USUM?Legal.Legendary_USUM:Legal.Legendary_SM);
        var pool=filter.GetAllowedSpeciesPool().Where(species=>species>0&&
            species<=Config.MaxSpeciesID&&species<Config.Personal.Table.Length).ToArray();
        if(options["NoLegendGifts"]==1&&giftOrTrade)
            pool=pool.Where(id=>!legendary.Contains(id)&&!Legal.Mythical_USUM.Contains(id)
                &&id is not (772 or 789 or 790 or 803)).ToArray();
        if(!giftOrTrade&&options["LegendToLegend"]==1&&
            (legendary.Contains(original)||Legal.Mythical_USUM.Contains(original)))
            pool=legendary.Concat(Legal.Mythical_USUM).Distinct().Where(s=>s>0&&s<=Config.MaxSpeciesID).ToArray();
        int tolerance=options["SimilarBST"]==1?options["BST"]:0;
        if(tolerance>0&&original>0&&original<Config.Personal.Table.Length)
        {
            int bst=Config.Personal[original].BST;
            pool=pool.Where(id=>Math.Abs(Config.Personal[id].BST-bst)*100<=bst*tolerance).ToArray();
        }
        if(pool.Length==0)throw new InvalidDataException("Static Encounters: el filtro de generación, BST o legendarios no deja reemplazos válidos.");
        return pool[random.Next(pool.Length)];
    }
    // Match the original species-based Force Totem rule, rather than the
    // separate USUM table of encounters used by individual Totem profiles.
    internal static readonly int[] TotemSpecies=[20,105,735,738,743,746,752,754,758,777,778,784];
    internal static int PickLegacyForm(int species,bool mega,pk3DS.Core.Structures.PersonalInfo.PersonalInfo[] stats,Random random)
    {
        if(stats[species].FormeCount<=1)return 0;
        if(species is 664 or 665 or 666)return 30;
        if(species==774)return random.Next(7);
        if(Legal.EvolveToAlolanForms.Contains(species))return random.Next(2);
        return !Legal.Mega_ORAS.Contains((ushort)species)||mega?random.Next(stats[species].FormeCount):0;
    }
    // A status SpecialMove can displace the starter's only natural attack.
    // Follow the original Progressive rule using the current ROM's move table.
    internal static int StarterSpecialMove(GameConfig config,int species,int form,int level,int specialMove)
    {
        bool Damaging(int move)=>move>0&&move<config.Moves.Length&&config.Moves[move].Power>0;
        if(Damaging(specialMove))return specialMove;
        int index=config.Personal.GetFormIndex(species,form);
        var sets=config.Learnsets;
        var set=sets!=null&&(uint)index<(uint)sets.Length?sets[index]:null;
        int[] current=set?.GetEncounterMoves(level)??[];
        if(specialMove==0&&current.Any(Damaging))return 0;
        int natural=current.FirstOrDefault(Damaging);
        if(natural==0)
            natural=(set?.GetMoves(Math.Clamp(level,1,100))??[])
                .Where(Damaging).Distinct().OrderBy(move=>config.Moves[move].Power).FirstOrDefault();
        if(natural!=0)return natural;
        if(Damaging(33))return 33;
        int fallback=Enumerable.Range(1,Math.Max(0,config.Moves.Length-1)).FirstOrDefault(Damaging);
        return fallback!=0?fallback:throw new InvalidDataException("No existe un movimiento dañino para garantizar un inicial jugable.");
    }
    internal static int[] GiftMovePool(GameConfig config)=>Enumerable.Range(1,Math.Max(0,config.Moves.Length-1))
        .Where(move=>!Legal.Z_Moves.Contains(move)&&move is not (165 or 464 or 621)).ToArray();
    internal static int RandomTotemAura(bool usum,int encounterIndex,int aura,Random random)
    {
        if(usum&&!TotemEntryIds.Contains(encounterIndex)||aura is <1 or >15)return aura;
        int boost=(aura-1)%3;
        int currentBase=aura-boost;
        int[] alternatives=new[]{1,4,7,10,13}.Where(stat=>stat!=currentBase).ToArray();
        return alternatives[random.Next(alternatives.Length)]+boost;
    }
    private int[] FinalPool(bool playerReceived,IReadOnlyDictionary<string,int> options)
    {
        IEnumerable<int> pool=Legal.FinalEvolutions_7;
        if(options["Legends"]==1)pool=pool.Concat(Config.USUM?Legal.Legendary_USUM:Legal.Legendary_SM);
        if(options["Events"]==1)pool=pool.Concat(Legal.Mythical_USUM);
        if(playerReceived&&options["NoLegendGifts"]==1)
            pool=pool.Where(s=>!Legal.Legendary_USUM.Contains(s)&&!Legal.Mythical_USUM.Contains(s)&&s is not (772 or 789 or 790 or 803));
        return pool.Distinct().Where(s=>s>0&&s<=Config.MaxSpeciesID&&s<Config.Personal.Table.Length).ToArray();
    }
    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,
        Dictionary<string,byte[][]> records,Random random)
    {
        var slot=_slots[id];var values=EditorValues.Flatten(ReadRecord(id,records));
        bool enabled=slot.File switch{0=>options["Gifts"]==1,1=>options["Static"]==1,_=>options["Trades"]==1};
        if(!enabled||values["Species"]<=0)return values;
        if(options["Species"]==1&&values["Species"]>0)
        {
            int original=values["Species"];
            bool basic=slot.File==0&&slot.Index<3&&options.GetValueOrDefault("BasicStarter")==1;
            bool forceTotem=slot.File==1&&TotemSpecies.Contains(original)&&options.GetValueOrDefault("ForceTotem")==1;
            // Original explicit starter/Totem pools override generation and BST
            // filters. Legend-to-legend has precedence over Force Totem.
            bool legendOverride=options["LegendToLegend"]==1&&
                (Legal.Legendary_USUM.Contains(original)||Legal.Mythical_USUM.Contains(original));
            int[] special=(basic?Legal.BasicStarters_7:forceTotem&&!legendOverride?FinalPool(false,options):[])
                .Where(s=>s>0&&s<=Config.MaxSpeciesID&&s<Config.Personal.Table.Length).ToArray();
            values["Species"]=basic||forceTotem&&!legendOverride
                ?special.Length>0?special[random.Next(special.Length)]:throw new InvalidDataException("No hay especies compatibles en el perfil de iniciales/Totem.")
                :PickFilteredSpecies(original,values["Level"],slot.File!=1,options,random);
            values["Form"]=PickLegacyForm(values["Species"],options.GetValueOrDefault("AllowMega")==1,Config.Personal.Table,random);
        }
        if(values["Level"]>0&&options["ScaleLevels"]==1)
            values["Level"]=Math.Clamp((int)Math.Round(values["Level"]*options["LevelScale"]/100.0)
                +options["LevelFlat"],1,100);
        if(options["Species"]==1)
        {
            if(options["ForceFinal"]==1&&!(slot.File==0&&slot.Index<3)&&values["Level"]>=options["FinalLevel"])
            {
                var finals=FinalPool(slot.File!=1,options);
                if(finals.Length==0)throw new InvalidDataException("El filtro no deja evoluciones finales válidas.");
                if(!finals.Contains(values["Species"]))values["Species"]=finals[random.Next(finals.Length)];
                values["Form"]=PickLegacyForm(values["Species"],options.GetValueOrDefault("AllowMega")==1,Config.Personal.Table,random);
            }
            values["Nature"]=slot.File switch{0=>-1,1=>0,_=>random.Next(25)};
            if(slot.File==1)
            {
                values["Gender"]=0;
                bool threePerfect=values["IV0"]==-4;
                for(int i=0;i<6;i++){values[$"IV{i}"]=i==0&&threePerfect?-4:-1;values[$"EV{i}"]=0;}
                int index=Config.Personal.GetFormIndex(values["Species"],values["Form"]);
                var sets=Config.Learnsets;
                int[] moves=sets!=null&&(uint)index<(uint)sets.Length?sets[index].GetEncounterMoves(values["Level"]):new int[4];
                for(int i=0;i<4;i++)values[$"Move{i}"]=moves[i];
            }
            else if(slot.File==4)
            {
                // Keep an already installed Accept Any request intact.
                if(values["Request"]>0)values["Request"]=PickFilteredSpecies(values["Request"],values["Level"],true,options,random);
                for(int i=0;i<6;i++)values[$"IV{i}"]=-1;
            }
        }
        if(options["RandomItems"]==1&&values.ContainsKey("Item"))
        {
            int[] heldPool=ProgressiveLegacy.Randomizer.GetRandomItemList(Config);
            var valid=heldPool.Where(item=>item<Config.GetText(TextName.ItemNames).Length).ToArray();
            if(valid.Length>0)values["Item"]=valid[random.Next(valid.Length)];
        }
        if(options["RandomAbilities"]==1&&values.ContainsKey("Ability"))
            values["Ability"]=slot.File==1?random.Next(1,4):random.Next(0,3);
        if(options["GiftMoves"]==1&&slot.File==0)
        {
            if(options.GetValueOrDefault("Metronome")==1)
            {
                // Original starters keep their special move in this mode;
                // Randomize All clears it only for gifts after the first three.
                if(slot.Index>=3)values["Move"]=0;
            }
            else
            {
            int[] moves=GiftMovePool(Config);
            if(moves.Length>0)values["Move"]=moves[random.Next(moves.Length)];
            }
        }
        if(slot.File==1&&options.GetValueOrDefault("Metronome")==1)
            for(int i=0;i<4;i++)values[$"Move{i}"]=i==0?118:0;
        if(options["TotemAura"]==1&&slot.File==1)
            values["Aura"]=RandomTotemAura(Config.USUM,slot.Index,values["Aura"],random);
        if(slot.File!=4&&options["RemoveLocks"]==1)
        {
            values["ShinyLock"]=0;
            if(slot.File==1)values["Shiny"]=1;
        }
        else if(slot.File!=4&&options["ShinyLock"]==1)
            values["ShinyLock"]=EditorValues.Flatten(ReadRecord(id,records))["ShinyLock"];
        if(slot.File==0&&slot.Index<3&&(options["Species"]==1||
            options["GiftMoves"]==1&&options.GetValueOrDefault("Metronome")==0))
            values["Move"]=StarterSpecialMove(Config,values["Species"],values["Form"],values["Level"],values["Move"]);
        return values;
    }
}

public sealed class TotemRuleRow : System.ComponentModel.INotifyPropertyChanged
{
    public int Id {get;init;}
    public int RecordId {get;init;}
    public string Group {get;init;}="";
    public string Name {get;init;}="";
    public string CurrentPokemon {get;init;}="";
    public int CurrentLevel {get;init;}
    public int CurrentBST {get;init;}
    private bool _use=true;
    public bool Use {get=>_use;set{_use=value;OnChanged(nameof(Use));}}
    private int _cap;
    public int LevelCap {get=>_cap;set{_cap=value;OnChanged(nameof(LevelCap));}}
    private int _minBst;
    public int MinBST {get=>_minBst;set{_minBst=value;OnChanged(nameof(MinBST));}}
    private int _maxBst;
    public int MaxBST {get=>_maxBst;set{_maxBst=value;OnChanged(nameof(MaxBST));}}
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string p)=>PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(p));
}
