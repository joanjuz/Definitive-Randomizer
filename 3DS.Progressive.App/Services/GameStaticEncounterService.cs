using System.IO;
using pk3DS.Core;
using pk3DS.Core.Structures;
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
        new("Randomización de especies",[
            Field("Species","Randomizar especies",1,0,1),
            Field("BST","Tolerancia BST (%) · 0 = cualquier especie",0,0,100),
            Field("LevelScale","Multiplicar niveles (%)",100,10,300),
            Field("Gifts","Incluir regalos e iniciales",1,0,1),
            Field("Static","Incluir encuentros estáticos y Totems",1,0,1),
            Field("Trades","Incluir Pokémon ofrecidos en intercambios",1,0,1),
            Field("ShinyLock","Conservar restricciones shiny",1,0,1)
        ])
    ];
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
            enc.HeldItem=v["Item"];enc.Gender=v["Gender"];enc.Ability=v["Ability"];enc.Nature=v["Nature"];
            bytes[6]=(byte)((bytes[6]&~3)|(v["Shiny"]==1?1:0)|(v["ShinyLock"]==1?2:0));
            enc.Aura=v["Aura"];enc.Allies=v["Allies"];
            enc.RelearnMoves=Enumerable.Range(0,4).Select(i=>v[$"Move{i}"]).ToArray();
        }
        else
        {
            var trade=new EncounterTrade7(bytes);
            trade.Species=v["Species"];trade.Form=v["Form"];trade.Level=v["Level"];
            trade.TradeRequestSpecies=v["Request"];trade.HeldItem=v["Item"];
            trade.Ability=v["Ability"];trade.Nature=v["Nature"];trade.Gender=v["Gender"];
            trade.TID=v["TID"];trade.SID=v["SID"];
        }
        bytes.CopyTo(records["encounterstatic"][slot.File],slot.Index*slot.Size);
    }
    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,
        Dictionary<string,byte[][]> records,Random random)
    {
        var slot=_slots[id];var values=EditorValues.Flatten(ReadRecord(id,records));
        bool enabled=slot.File switch{0=>options["Gifts"]==1,1=>options["Static"]==1,_=>options["Trades"]==1};
        if(!enabled)return values;
        if(options["Species"]==1&&values["Species"]>0)
        {
            values["Species"]=PickSpecies(values["Species"],random,options["BST"]);
            values["Form"]=0;
        }
        values["Level"]=Math.Clamp((int)Math.Round(values["Level"]*options["LevelScale"]/100.0),1,100);
        if(slot.File!=4&&options["ShinyLock"]==1)
            values["ShinyLock"]=EditorValues.Flatten(ReadRecord(id,records))["ShinyLock"];
        return values;
    }
}
