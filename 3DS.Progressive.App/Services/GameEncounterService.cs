using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Randomizers;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed partial class GameEncounterService : ArchiveProjectEditor, IOptionTemplateProjectEditor
{
    private sealed record Location(int File,int Table,int Offset,int Map,string Name);
    private readonly Dictionary<int,Location> _locations=[];
    private readonly (string Name,int Count)[] _types;
    private List<(int MinLevel,int MaxLevel,int MinBST,int MaxBST,bool FullRandom)> _bstRanges=
    [
        (1,10,180,320,false),(11,20,220,380,false),(21,30,280,450,false),
        (31,40,340,520,false),(41,50,400,580,false),(51,100,480,680,false)
    ];
    internal void RestoreRangeOptions(IReadOnlyDictionary<string,int> values)
    {
        var groups=values.Where(p=>p.Key.StartsWith("BST.",StringComparison.Ordinal))
            .GroupBy(p=>int.Parse(p.Key.Split('.')[1])).OrderBy(p=>p.Key).ToArray();
        if(groups.Length==0)return;
        _bstRanges=groups.Select(g=>
        {
            int V(string name)=>g.First(p=>p.Key.EndsWith("."+name,StringComparison.Ordinal)).Value;
            return (V("MinLevel"),V("MaxLevel"),V("MinBST"),V("MaxBST"),V("FullRandom")==1);
        }).ToList();
    }

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
    public override IReadOnlyList<EditorGroup> RandomOptions
    {
        get
        {
            var groups=new List<EditorGroup>
            {
                new("Especies permitidas",[
                    Field("Species","Randomizar especies",1,0,1),
                    Field("G1","Generación 1",1,0,1),Field("G2","Generación 2",1,0,1),
                    Field("G3","Generación 3",1,0,1),Field("G4","Generación 4",1,0,1),
                    Field("G5","Generación 5",1,0,1),Field("G6","Generación 6",1,0,1),
                    Field("G7","Generación 7",Config.Generation==7?1:0,0,Config.Generation==7?1:0),
                    Field("Legends","Incluir legendarios",0,0,1),
                    Field("Events","Incluir legendarios de evento",0,0,1),
                    Field("SimilarBST","Mantener BST similar",0,0,1),
                    Field("MegaForms","Permitir formas Mega/batalla durante la randomización",0,0,1),
                    Field("BST","Tolerancia BST (%) · 0 = libre",30,0,100),
                    Field("Progressive","BST progresivo por nivel",0,0,1)
                ]),
                new("Slots y SOS",[
                    Field("SlotMode","Aleatorización: 0 Todo / 1 Normal / 2 SOS / 3 Copiar Normal a SOS",
                        0,0,Config.Generation==7?3:0,
                        [new(0,"Todas las tablas"),new(1,"Solo encuentros normales"),
                         new(2,"Solo SOS"),new(3,"Normal + copiar a SOS")]),
                    Field("SOS","Incluir aliados SOS y clima",1,0,1),
                    Field("Weather","Randomizar fila SOS meteorológica",1,0,1),
                    Field("AllCanCallAllies","Cualquier Pokémon puede pedir ayuda SOS (rellenar slots vacíos)",0,0,1)
                ]),
                new("Niveles",[
                    Field("LevelScale","Multiplicador de niveles (%)",100,1,500),
                    Field("LevelFlat","Ajuste plano (-100 a 100)",0,-100,100),
                    Field("KeepRange","Mantener mínimo y máximo diferenciados",1,0,1)
                ])
            };
            if(Config.Generation==7)
            {
                for(int i=0;i<_bstRanges.Count;i++)
                {
                    var range=_bstRanges[i];
                    string prefix=$"BST.{i}.";
                    groups.Add(new($"Rango BST {i+1}",[
                        Field(prefix+"MinLevel","Nivel inicial",range.MinLevel,1,100),
                        Field(prefix+"MaxLevel","Nivel final",range.MaxLevel,1,100),
                        Field(prefix+"MinBST","BST mínimo",range.MinBST,1,999),
                        Field(prefix+"MaxBST","BST máximo",range.MaxBST,1,999),
                        Field(prefix+"FullRandom","Randomización libre en este rango",range.FullRandom?1:0,0,1)
                    ],false));
                }
            }
            return _wildTemplateDefaults is null ? groups : groups.Select(g=>g with
            {Fields=g.Fields.Select(f=>f with {Value=WildDefault(f.Key,f.Value)}).ToArray()}).ToArray();
        }
    }
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
        if(options["SimilarBST"]==1&&options["Progressive"]==1)
            throw new ArgumentException("Elegí solo un modo BST: similar o progresivo.");
        if(!Enumerable.Range(1,Config.Generation).Any(i=>options[$"G{i}"]==1))
            throw new ArgumentException("Seleccioná al menos una generación.");
        var picker=new SpeciesRandomizer(Config)
        {
            G1=options["G1"]==1,G2=options["G2"]==1,G3=options["G3"]==1,
            G4=options["G4"]==1,G5=options["G5"]==1,G6=options["G6"]==1,
            G7=Config.Generation==7&&options["G7"]==1,
            L=options["Legends"]==1,E=options["Events"]==1,rBST=false
        };
        int[] allowed=picker.GetAllowedSpeciesPool().Where(species=>species>=1&&
            species<=Config.MaxSpeciesID&&species<Config.Personal.Table.Length).ToArray();
        if(allowed.Length==0)throw new InvalidDataException("El filtro no permite ninguna especie.");
        var ranges=new List<(int MinLevel,int MaxLevel,int MinBST,int MaxBST,bool FullRandom)>();
        if(options["Progressive"]==1)
        {
            if(Config.Generation!=7)throw new NotSupportedException("El modo Progressive Wild de esta vista solo admite Gen7.");
            for(int i=0;i<_bstRanges.Count;i++)
            {
                string k=$"BST.{i}.";
                int lo=options[k+"MinLevel"],hi=options[k+"MaxLevel"];
                int min=options[k+"MinBST"],max=options[k+"MaxBST"];
                if(lo>hi||min>max)throw new ArgumentException($"Rango BST {i+1}: valores invertidos.");
                ranges.Add((lo,hi,min,max,options[k+"FullRandom"]==1));
            }
            ranges.Sort((a,b)=>a.MinLevel.CompareTo(b.MinLevel));
            if(ranges[0].MinLevel!=1||ranges[^1].MaxLevel!=100||
               Enumerable.Range(1,ranges.Count-1).Any(i=>ranges[i].MinLevel!=ranges[i-1].MaxLevel+1))
                throw new ArgumentException("Los rangos Progressive BST deben cubrir niveles 1–100 sin huecos ni solapamientos.");
        }
        int level=Config.Generation==7?Math.Max(1,values["Max"]):Math.Max(1,values.GetValueOrDefault("S0.Max",1));
        int[] pool=allowed;
        if(options["Progressive"]==1)
        {
            var range=ranges.First(r=>level>=r.MinLevel&&level<=r.MaxLevel);
            if(!range.FullRandom)pool=allowed.Where(species=>
                Config.Personal[species].BST>=range.MinBST&&Config.Personal[species].BST<=range.MaxBST).ToArray();
        }
        // Similar BST is slot-specific, never based on the first species of the area.
        if(pool.Length==0)throw new InvalidDataException("No hay especies válidas con los filtros BST seleccionados.");
        int slotMode=options["SlotMode"];
        foreach(string key in values.Keys.Where(k=>k.EndsWith("Species")).ToArray())
        {
            int group=-1;
            if(Config.Generation==7&&key.StartsWith("S")&&key.Contains('.'))
                int.TryParse(key[1..key.IndexOf('.')],out group);
            bool weather=Config.Generation==7&&group==8;
            bool isSos=Config.Generation==7&&group is >=1 and <=7;
            bool allow=Config.Generation!=7||(
                (weather?options["Weather"]==1&&options["SOS"]==1:
                 isSos?options["SOS"]==1&&slotMode is 0 or 2:
                 slotMode is not 2));
            if(!allow||options["Species"]!=1||values[key]<=0)continue;
            int[] slotPool=pool;
            if(options["SimilarBST"]==1&&values[key]<Config.Personal.Table.Length)
            {
                int bst=Config.Personal[values[key]].BST;
                slotPool=allowed.Where(species=>Math.Abs(Config.Personal[species].BST-bst)*100<=bst*options["BST"]).ToArray();
                if(slotPool.Length==0)
                    throw new InvalidDataException($"No hay reemplazos dentro de la tolerancia BST del slot {key}.");
            }
            int chosen=slotPool[random.Next(slotPool.Length)];
            values[key]=chosen;
            values[key[..^7]+"Form"]=Config.Generation==7
                ?PickWildForm(chosen,random,options["MegaForms"]==1):0;
        }
        if(Config.Generation==7&&slotMode==3&&options["SOS"]==1)
        {
            for(int group=1;group<=7;group++)for(int i=0;i<10;i++)
            {
                string target=$"S{group}.{i}.",source=$"S0.{i}.";
                if(values[target+"Species"]==0)continue;
                values[target+"Species"]=values[source+"Species"];
                values[target+"Form"]=values[source+"Form"];
            }
        }
        if(Config.Generation==7&&options["AllCanCallAllies"]==1)
        {
            // Vanilla Wild7Randomizer semantics: fill only missing ordinary SOS
            // species from the matching normal slot, never overwrite existing
            // partners or meteorological SOS encounters.
            for(int group=1;group<=7;group++)
                for(int i=0;i<10;i++)
                {
                    string original=$"S0.{i}.",ally=$"S{group}.{i}.";
                    if(values[original+"Species"]==0||values[ally+"Species"]!=0)continue;
                    values[ally+"Species"]=values[original+"Species"];
                    values[ally+"Form"]=values[original+"Form"];
                }
        }
        int Scale(int x)=>x==0?0:Math.Clamp((int)Math.Round(x*options["LevelScale"]/100.0)+options["LevelFlat"],1,100);
        if(Config.Generation==7)
        {
            int min=values["Min"],max=values["Max"];
            values["Min"]=Scale(min);values["Max"]=Scale(max);
            if(options["KeepRange"]==1&&min<max&&values["Max"]<=values["Min"]&&values["Min"]<100)
                values["Max"]=values["Min"]+1;
            if(values["Min"]>values["Max"])values["Max"]=values["Min"];
        }
        else
        {
            foreach(string key in values.Keys.Where(k=>k.EndsWith(".Min")).ToArray())
            {
                string maxKey=key[..^3]+"Max";if(!values.ContainsKey(maxKey))continue;
                int lo=values[key],hi=values[maxKey];
                values[key]=Scale(lo);values[maxKey]=Scale(hi);
                if(options["KeepRange"]==1&&lo<hi&&values[maxKey]<=values[key]&&values[key]<100)
                    values[maxKey]=values[key]+1;
                if(values[key]>values[maxKey])values[maxKey]=values[key];
            }
        }
        return values;
    }
}
