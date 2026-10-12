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
public sealed class GameStaticEncounterService : ArchiveProjectEditor
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
            Field("G1","Generación 1",1,0,1),Field("G2","Generación 2",1,0,1),
            Field("G3","Generación 3",1,0,1),Field("G4","Generación 4",1,0,1),
            Field("G5","Generación 5",1,0,1),Field("G6","Generación 6",1,0,1),
            Field("G7","Generación 7",1,0,1),
            Field("Legends","Permitir Pokémon legendarios",1,0,1),
            Field("Events","Permitir Pokémon de evento",1,0,1),
            Field("SimilarBST","Limitar especies por BST similar",0,0,1),
            Field("BST","Tolerancia BST (%) · 0 = libre",30,0,100),
            Field("LegendToLegend","Reemplazar legendarios por legendarios",0,0,1),
            Field("NoLegendGifts","Bloquear legendarios en regalos e intercambios",0,0,1)
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
            Field("RandomAbilities","Randomizar slots de habilidad (regalos/estáticos)",0,0,1),
            Field("GiftMoves","Randomizar movimiento especial de regalos",0,0,1),
            Field("ShinyLock","Conservar restricciones shiny",1,0,1),
            Field("RemoveLocks","Eliminar shiny locks (regalos/estáticos)",0,0,1)
        ]),
        new("Encuentros Totem y especiales",[
            Field("TotemAura","Randomizar aura de Totem existente",0,0,1),
            Field("ForceFinal","Usar evoluciones finales desde un nivel",0,0,1),
            Field("FinalLevel","Nivel para evolución final",36,1,100)
        ],false)
    ];
    public bool SupportsTotemRules=>Config.USUM;
    private static readonly int[] TotemEntryIds=[4,9,137,249,24,146,39,45,162,160,229,231];

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
            if(before[0].Value!=values["Species"]||before[0].Fields.Any(f=>f.Key=="Level"&&f.Value!=values["Level"]))
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
        }
        return [new(slot.Kind+" "+(slot.Index+1),fields)];
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
            enc.RelearnMoves=Enumerable.Range(0,4).Select(i=>v[$"Move{i}"]).ToArray();
        }
        else
        {
            var trade=new EncounterTrade7(bytes);
            trade.Species=v["Species"];trade.Form=v["Form"];trade.Level=v["Level"];
            if(v["Request"]==0&&trade.TradeRequestSpecies!=0)
                throw new NotSupportedException("Accept Any Pokémon requiere el parche del selector de intercambios USUM; no basta con poner 0 en el registro.");
            trade.TradeRequestSpecies=v["Request"];
            if(v["Item"]!=trade.HeldItem)trade.HeldItem=v["Item"];
            trade.Ability=v["Ability"];trade.Nature=v["Nature"];trade.Gender=v["Gender"];
            trade.TID=v["TID"];trade.SID=v["SID"];
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
            pool=pool.Where(id=>!legendary.Contains(id)&&!Legal.Mythical_USUM.Contains(id)).ToArray();
        if(options["LegendToLegend"]==1&&legendary.Contains(original))
            pool=pool.Where(legendary.Contains).ToArray();
        if(options["ForceFinal"]==1&&level>=options["FinalLevel"])
            pool=pool.Where(id=>Legal.FinalEvolutions_7.Contains(id)).ToArray();
        int tolerance=options["SimilarBST"]==1?options["BST"]:0;
        if(tolerance>0&&original>0&&original<Config.Personal.Table.Length)
        {
            int bst=Config.Personal[original].BST;
            pool=pool.Where(id=>Math.Abs(Config.Personal[id].BST-bst)*100<=bst*tolerance).ToArray();
        }
        if(pool.Length==0)throw new InvalidDataException("Static Encounters: el filtro de generación, BST o legendarios no deja reemplazos válidos.");
        return pool[random.Next(pool.Length)];
    }
    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,
        Dictionary<string,byte[][]> records,Random random)
    {
        var slot=_slots[id];var values=EditorValues.Flatten(ReadRecord(id,records));
        bool enabled=slot.File switch{0=>options["Gifts"]==1,1=>options["Static"]==1,_=>options["Trades"]==1};
        if(!enabled||values["Species"]<=0)return values;
        if(options["Species"]==1&&values["Species"]>0)
        {
            values["Species"]=PickFilteredSpecies(values["Species"],values["Level"],
                slot.File!=1,options,random);
            values["Form"]=0;
        }
        if(values["Level"]>0&&options["ScaleLevels"]==1)
            values["Level"]=Math.Clamp((int)Math.Round(values["Level"]*options["LevelScale"]/100.0)
                +options["LevelFlat"],1,100);
        if(options["RandomItems"]==1&&values.ContainsKey("Item"))
        {
            int[] heldPool=[155,156,157,158,159,160,220,234,270,275,287,297,538];
            var valid=heldPool.Where(item=>item<Config.GetText(TextName.ItemNames).Length).ToArray();
            if(valid.Length>0)values["Item"]=valid[random.Next(valid.Length)];
        }
        if(options["RandomAbilities"]==1&&slot.File!=4&&values.ContainsKey("Ability"))
            values["Ability"]=random.Next(0,3);
        if(options["GiftMoves"]==1&&slot.File==0)
        {
            int[] moves=Enumerable.Range(1,Math.Min(Config.Moves.Length,621)-1)
                .Where(move=>!Legal.Z_Moves.Contains(move)&&move is not (165 or 166 or 226))
                .ToArray();
            if(moves.Length>0)values["Move"]=moves[random.Next(moves.Length)];
        }
        if(options["TotemAura"]==1&&slot.File==1&&values["Aura"]>0)
            values["Aura"]=random.Next(1,8);
        if(slot.File!=4&&options["RemoveLocks"]==1)
        {
            values["ShinyLock"]=0;
            if(slot.File==1)values["Shiny"]=1;
        }
        else if(slot.File!=4&&options["ShinyLock"]==1)
            values["ShinyLock"]=EditorValues.Flatten(ReadRecord(id,records))["ShinyLock"];
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
