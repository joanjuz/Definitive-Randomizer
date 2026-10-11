using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed class GameEncounterService : ArchiveProjectEditor
{
    private sealed record Location(int File,int Table,int Offset,int Map,string Name);
    private readonly Dictionary<int,Location> _locations=[];
    private readonly (string Name,int Count)[] _types;
    public GameEncounterService(GameProjectSession project):base(project,"encdata")
    {
        _types=Config.XY?[("Hierba",12),("Hierba alta",12),("Flores rojas",12),("Flores amarillas",12),("Flores moradas",12),("Surf",5),("Golpe Roca",5),("Caña vieja",3),("Caña buena",3),("Supercaña",3),("Horda 60%",5),("Horda 35%",5),("Horda 5%",5)]
            :[("Hierba",12),("Hierba alta",12),("Enjambre",3),("Surf",5),("Golpe Roca",5),("Caña vieja",3),("Caña buena",3),("Supercaña",3),("Horda 60%",5),("Horda 35%",5),("Horda 5%",5)];
        var records=Records()["encdata"];var names=new Dictionary<int,string>();
        if(Config.Generation==7)
        {
            try
            {
                var areas=Area7.GetArray(Config.GetlzGARCData("encdata"),Config.GetlzGARCData("zonedata"),Config.GetlzGARCData("worlddata"),Config.GetText(TextName.metlist_000000));
                foreach(var area in areas)if(!string.IsNullOrWhiteSpace(area.Name))names[area.FileNumber]=area.Name;
            }
            catch (Exception ex) when(ex is IOException or ArgumentException or IndexOutOfRangeException or NullReferenceException or InvalidOperationException or FormatException) { }
            for(int file=9;file<records.Length;file+=11)
            {
                if(records[file].Length==0)continue;
                byte[][] tables=Mini.UnpackMini(records[file],"EA")??throw new InvalidDataException("Tabla de encuentros EA no reconocida.");
                for(int t=0;t<tables.Length;t++)
                {
                    if(tables[t].Length==0)continue;
                    if(tables[t].Length<0x2CC)throw new InvalidDataException("Tabla de día/noche incompleta.");
                    for(int day=0;day<2;day++)
                    {
                        int id=_locations.Count+1;string area=names.GetValueOrDefault(file,$"Área {(file-9)/11:000}");
                        _locations.Add(id,new(file,t,4+day*0x164,(file-9)/11,$"{area} · tabla {t+1} · {(day==0?"Día":"Noche")}"));
                    }
                }
            }
        }
        else
        {
            int start=Config.XY?1:2,size=Config.XY?0x178:0xF6;var zones=records[0];var places=Config.GetText(TextName.metlist_000000);
            for(int file=start;file<records.Length;file++)
            {
                byte[] record=records[file];if(record.Length<20)continue;
                int offset=checked(BitConverter.ToInt32(record,16)+(Config.XY?16:14));
                if(offset<20||offset>record.Length-size)continue;
                int map=file-start,nameOffset=map*56+28,nameId=nameOffset+1<zones.Length?zones[nameOffset]|((zones[nameOffset+1]&1)<<8):-1;
                string name=nameId>=0&&nameId<places.Length?places[nameId]:$"Mapa {map:000}";
                _locations.Add(_locations.Count+1,new(file,0,offset,map,$"{map:000} · {name}"));
            }
        }
        Entries=_locations.Select(p=>new RecordEntry(p.Key,p.Value.Name)).ToArray();
        if(Entries.Count==0)throw new InvalidDataException("No se encontraron tablas de encuentros editables.");
    }
    public override string Title=>"Encuentros";
    public override IReadOnlyList<EditorGroup> RandomOptions=>[new("Randomización",[
        Field("Species","Randomizar especies",1,0,1),Field("BST","Tolerancia BST (%) · 0 = cualquier BST",30,0,100),
        Field("LevelScale","Escala de niveles (%)",100,1,1000),Field("SOS","Incluir aliados SOS y clima",1,0,1)])];
    private byte[] Table(int id,Dictionary<string,byte[][]> records)
    {
        var l=_locations[id];return Config.Generation==7?Mini.UnpackMini(records["encdata"][l.File],"EA")[l.Table]:records["encdata"][l.File];
    }
    protected override IReadOnlyList<EditorGroup> ReadRecord(int id,Dictionary<string,byte[][]> records)
    {
        var location=_locations[id];byte[] table=Table(id,records);int offset=location.Offset;
        var groups=new List<EditorGroup>();
        if(Config.Generation==7)
        {
            var general=new List<EditorField>{Field("Min","Nivel mínimo",table[offset],0,100),Field("Max","Nivel máximo",table[offset+1],0,100)};
            for(int i=0;i<10;i++)general.Add(Field("Rate"+i,$"Probabilidad slot {i+1} (%)",table[offset+2+i],0,100));
            groups.Add(new("Niveles y probabilidades",general));
            for(int group=0;group<9;group++)
            {
                var f=new List<EditorField>();int count=group==8?6:10;
                for(int i=0;i<count;i++)
                {
                    uint raw=BitConverter.ToUInt32(table,offset+(group==8?0x14C+i*4:12+group*40+i*4));string k=$"S{group}.{i}.";
                    f.Add(Field(k+"Species",$"Slot {i+1} · especie",(int)(raw&0x7FF),0,Config.MaxSpeciesID,SpeciesChoices));
                    f.Add(Field(k+"Form",$"Slot {i+1} · forma",(int)((raw>>11)&31),0,31));
                }
                groups.Add(new(group==0?"Encuentros principales":group==8?"SOS por clima":$"Tabla SOS {group}",f,group==0));
            }
        }
        else
        {
            int slot=0;
            foreach(var type in _types)
            {
                var f=new List<EditorField>();
                for(int n=0;n<type.Count;n++,slot++)
                {
                    int at=offset+slot*4,raw=BitConverter.ToUInt16(table,at);string k=$"S{slot}.";
                    f.Add(Field(k+"Species",$"Slot {n+1} · especie",raw&0x7FF,0,Config.MaxSpeciesID,SpeciesChoices));
                    f.Add(Field(k+"Form",$"Slot {n+1} · forma",raw>>11,0,31));
                    f.Add(Field(k+"Min",$"Slot {n+1} · nivel mínimo",table[at+2],0,100));f.Add(Field(k+"Max",$"Slot {n+1} · nivel máximo",table[at+3],0,100));
                }
                groups.Add(new(type.Name,f,groups.Count==0));
            }
        }
        return groups;
    }
    protected override void WriteRecord(int id,IReadOnlyDictionary<string,int> v,Dictionary<string,byte[][]> records)
    {
        var l=_locations[id];byte[] table=Table(id,records);int o=l.Offset;
        if(Config.Generation==7)
        {
            if(v["Min"]>v["Max"])throw new ArgumentException("El nivel mínimo supera al máximo.");
            table[o]=(byte)v["Min"];table[o+1]=(byte)v["Max"];
            for(int i=0;i<10;i++)table[o+2+i]=(byte)v["Rate"+i];
            for(int group=0;group<9;group++)for(int i=0;i<(group==8?6:10);i++)
            {
                string k=$"S{group}.{i}.";int at=o+(group==8?0x14C+i*4:12+group*40+i*4);
                uint preserved=BitConverter.ToUInt32(table,at)&0xFFFF0000u;
                BitConverter.GetBytes(preserved|(uint)v[k+"Species"]|((uint)v[k+"Form"]<<11)).CopyTo(table,at);
            }
            byte[][] tables=Mini.UnpackMini(records["encdata"][l.File],"EA");tables[l.Table]=table;
            records["encdata"][l.File]=Mini.PackMini(tables,"EA");
        }
        else
        {
            for(int i=0;i<_types.Sum(t=>t.Count);i++)
            {
                string k=$"S{i}.";int at=o+i*4;if(v[k+"Min"]>v[k+"Max"])throw new ArgumentException($"Slot {i+1}: el nivel mínimo supera al máximo.");
                BitConverter.GetBytes((ushort)(v[k+"Species"]|(v[k+"Form"]<<11))).CopyTo(table,at);
                table[at+2]=(byte)v[k+"Min"];table[at+3]=(byte)v[k+"Max"];
            }
            if(Config.ORAS)
            {
                byte[] storage=records["encdata"][1];int at=(l.Map+1)*4;
                if(at+4>storage.Length)throw new InvalidDataException("La tabla de encuentros compartida de ORAS está incompleta.");
                int target=checked(BitConverter.ToInt32(storage,at)+14);
                if(target<0||target>storage.Length-0xF4)throw new InvalidDataException("Offset compartido de ORAS fuera de rango.");
                table.AsSpan(o,0xF4).CopyTo(storage.AsSpan(target,0xF4));
            }
        }
    }
    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,Dictionary<string,byte[][]> records,Random random)
    {
        var values=EditorValues.Flatten(ReadRecord(id,records));
        foreach(string key in values.Keys.ToArray())
        {
            if(key.EndsWith("Species")&&values[key]>0&&options["Species"]==1)
            {
                if(Config.Generation==7&&options["SOS"]==0&&!key.StartsWith("S0."))continue;
                values[key]=PickSpecies(values[key],random,options["BST"]);values[key[..^7]+"Form"]=0;
            }
            if((key=="Min"||key=="Max"||key.EndsWith(".Min")||key.EndsWith(".Max"))&&values[key]>0)
                values[key]=Math.Clamp((int)Math.Round(values[key]*options["LevelScale"]/100.0),1,100);
        }
        return values;
    }
}
