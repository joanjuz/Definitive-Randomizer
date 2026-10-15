using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>XY/ORAS DllField tables from vanilla; starter species stay synchronized
/// with DllPoke3Select. All writes use frozen proposals and the project history.</summary>
public sealed class GameStaticEncounter6Service : IProjectEditor
{
    private sealed record Slot(int Offset,int Size,int Index,bool Gift);
    private readonly GameProjectSession _project;
    private readonly GameConfig _game;
    private readonly string _path;
    private byte[] _hash;
    private readonly Guid _owner=Guid.NewGuid();
    private readonly Dictionary<int,Slot> _slots=[];
    private readonly int[] _starters;
    private readonly IReadOnlyList<MoveChoice> _species;
    public string Title=>"Regalos, iniciales y encuentros estáticos · Gen6";
    public IReadOnlyList<RecordEntry> Entries {get;}
    public GameStaticEncounter6Service(GameProjectSession project)
    {
        _project=project;_game=project.GameConfiguration;
        if(_game.Generation!=6)throw new NotSupportedException("Este editor requiere XY u ORAS.");
        _path=Path.Combine(_game.RomFS,"DllField.cro");
        byte[] source=File.ReadAllBytes(_path);ValidateCro(source);
        _hash=SHA256.HashData(source);
        _species=Choices(TextName.SpeciesNames).Take(_game.MaxSpeciesID+1).ToArray();
        _starters=_game.ORAS?[0,1,2,28,29,30,31,32,33,34,35,36]:[0,1,2,3,4,5];
        Add(_game.ORAS?0xF1B20:0xEE46C,12,_game.ORAS?59:13,false);
        Add(_game.ORAS?0xF906C:0xF805C,_game.ORAS?36:24,_game.ORAS?37:19,true);
        Entries=_slots.Select(p=>new RecordEntry(p.Key,
            $"{(p.Value.Gift?(_starters.Contains(p.Value.Index)?"Inicial":"Regalo"):"Estático")} {p.Value.Index+1:000} · {_species[BitConverter.ToUInt16(source,p.Value.Offset)].Name}")).ToArray();
        void Add(int offset,int size,int count,bool gift)
        {
            if(offset<0||(long)offset+size*count>source.Length)
                throw new InvalidDataException("DllField.cro incompleto para las tablas Gen6.");
            for(int i=0;i<count;i++)
            {
                int at=offset+i*size;
                if(BitConverter.ToUInt16(source,at)>_game.MaxSpeciesID || source[at+(gift?5:3)]>100)
                    throw new InvalidDataException("La disposición de DllField.cro no coincide con XY/ORAS vanilla.");
                _slots.Add(_slots.Count+1,new(at,size,i,gift));
            }
        }
    }
    private static void ValidateCro(byte[] bytes)
    {
        if(bytes.Length<0x180||!bytes.AsSpan(0x80,4).SequenceEqual("CRO0"u8))
            throw new InvalidDataException("No se reconoce la cabecera CRO0.");
    }
    private IReadOnlyList<MoveChoice> Choices(TextName name)=>_game.GetText(name).Select((s,i)=>new MoveChoice(i,s)).ToArray();
    private static EditorField F(string key,string label,int value,int min=0,int max=255,IReadOnlyList<MoveChoice>? choices=null)=>new(key,label,value,min,max,choices);
    private IReadOnlyList<EditorGroup> SharedOptions=>[
        new("Especies y formas",[
            F("Species","Randomizar especies",1,0,1),
            F("BasicStarter","Iniciales básicos",0,0,1),
            F("StarterGeneration","Iniciales de la generación original de cada grupo",0,0,1),
            F("G1","Generación 1",1,0,1),F("G2","Generación 2",1,0,1),F("G3","Generación 3",1,0,1),
            F("G4","Generación 4",1,0,1),F("G5","Generación 5",1,0,1),F("G6","Generación 6",1,0,1),
            F("Legends","Permitir legendarios",1,0,1),F("Events","Permitir míticos",1,0,1),
            F("BST","Tolerancia BST (%) · 0 = libre",0,0,100),
            F("LegendToLegend","Legendarios por legendarios/míticos",0,0,1),
            F("Forms","Randomizar formas",1,0,1),F("Mega","Permitir formas de combate",0,0,1),
            F("Final","Evoluciones finales desde un nivel",0,0,1),F("FinalLevel","Nivel de evolución final",36,1,100)]),
        new("Alcance y atributos",[
            F("Static","Incluir encuentros estáticos",1,0,1),F("Gifts","Incluir regalos",1,0,1),
            F("Starters","Incluir iniciales y sincronizar selector",1,0,1),
            F("ScaleLevels","Modificar niveles",0,0,1),F("LevelScale","Multiplicar niveles (%)",100,10,300),
            F("LevelFlat","Sumar niveles",0,-100,100),F("Abilities","Randomizar habilidad",0,0,1),
            F("Items","Randomizar objetos",0,0,1),F("RemoveLocks","Eliminar shiny locks",0,0,1),F("PreserveMega","Conservar regalos con megapiedras",1,0,1)])];
    internal static readonly string[] ProfileNames=["Static","Gifts","Starters"];
    public IReadOnlyList<EditorGroup> RandomOptions
    {
        get
        {
            var shared=SharedOptions;
            var groups=new List<EditorGroup>{new("Configuración de perfiles",[F("SeparateProfiles","Usar ajustes independientes por grupo",0,0,1)])};
            groups.AddRange(shared);
            foreach(string profile in ProfileNames)
            {
                var fields=shared.SelectMany(g=>g.Fields).Where(f=>!ProfileNames.Contains(f.Key)&&
                    (profile=="Starters"||f.Key is not ("BasicStarter" or "StarterGeneration"))).Select(f=>f with {Key=profile+"."+f.Key}).ToList();
                fields.Insert(0,F(profile+".Enabled","Incluir este grupo",1,0,1));
                groups.Add(new("Perfil independiente · "+(profile=="Static"?"Encuentros estáticos":profile=="Gifts"?"Regalos":"Iniciales"),fields,false));
            }
            return groups;
        }
    }
    internal static bool IsProfileOption(string key)=>key=="SeparateProfiles"||ProfileNames.Any(p=>key.StartsWith(p+".",StringComparison.Ordinal));
    private IReadOnlyDictionary<string,int> EffectiveOptions(IReadOnlyDictionary<string,int> options,string profile)
    {
        if(options.GetValueOrDefault("SeparateProfiles")==0)return options;
        var values=EditorValues.Flatten(SharedOptions);
        foreach(string key in values.Keys.ToArray())if(options.TryGetValue(profile+"."+key,out int value))values[key]=value;
        foreach(string name in ProfileNames)values[name]=name==profile?options[profile+".Enabled"]:0;
        return values;
    }
    private byte[] Source()=>GarcTransaction.ReadUnchanged(_path,_hash);
    public IReadOnlyList<EditorGroup> Read(int id)=>Read(id,Source());
    private IReadOnlyList<EditorGroup> Read(int id,byte[] source)
    {
        var slot=_slots[id];var raw=source.AsSpan(slot.Offset,slot.Size).ToArray();
        var fields=new List<EditorField>{F("Species","Especie",BitConverter.ToUInt16(raw,0),0,_game.MaxSpeciesID,_species)};
        if(slot.Gift)
        {
            var g=new EncounterGift6(raw,_game.ORAS);
            fields.AddRange([F("Form","Forma",g.Form),F("Level","Nivel",g.Level,0,100),
                F("Item","Objeto equipado",Math.Max(0,g.HeldItem),0,65535,Choices(TextName.ItemNames)),
                F("Ability","Habilidad · -1 aleatoria",g.Ability,-1,2),F("Nature","Naturaleza · -1 aleatoria",g.Nature,-1,24),
                F("Gender","Género",g.Gender,-1,2),F("ShinyLock","Bloqueo shiny",g.ShinyLock?1:0,0,1)]);
            for(int i=0;i<6;i++)fields.Add(F($"IV{i}",$"IV {new[]{"PS","Ataque","Defensa","Velocidad","Ataque especial","Defensa especial"}[i]} · -1 aleatorio",g.IVs[i],-1,31));
            if(_game.ORAS)
            {
                fields.Add(F("Move","Movimiento especial",g.Move,0,65535,Choices(TextName.MoveNames)));
                fields.Add(F("Met","Lugar de encuentro",g.MetLocation,short.MinValue,short.MaxValue));
                for(int i=0;i<6;i++)fields.Add(F($"Contest{i}",$"Concurso {i+1}",g.ContestStats[i]));
            }
        }
        else
        {
            var e=new EncounterStatic6(raw);
            fields.AddRange([F("Form","Forma",e.Form),F("Level","Nivel",e.Level,0,100),
                F("Item","Objeto equipado",e.HeldItem,0,32767,Choices(TextName.ItemNames)),
                F("Gender","Género",e.Gender,0,3),F("Ability","Slot de habilidad",e.Ability,0,7),
                F("Shiny","Permitir shiny",(raw[6]&1)!=0?1:0,0,1),F("ShinyLock","Bloqueo shiny",e.ShinyLock?1:0,0,1),
                F("IV3","Tres IV perfectos",e.IV3?1:0,0,1),F("IV3_1","Segundo flag de tres IV",e.IV3_1?1:0,0,1)]);
        }
        return [new(slot.Gift?"Regalo / inicial":"Encuentro estático",fields)];
    }
    private void Write(int id,IReadOnlyDictionary<string,int> v,byte[] source)
    {
        EditorValues.Validate(Read(id,source),v);
        var slot=_slots[id];var raw=source.AsSpan(slot.Offset,slot.Size).ToArray();
        if(slot.Gift)
        {
            var g=new EncounterGift6(raw,_game.ORAS){Species=(ushort)v["Species"],Form=(byte)v["Form"],Level=(byte)v["Level"],
                Ability=(sbyte)v["Ability"],Nature=(sbyte)v["Nature"],Gender=(sbyte)v["Gender"]};
            if(Math.Max(0,g.HeldItem)!=v["Item"])g.HeldItem=v["Item"];
            g.ShinyLock=v["ShinyLock"]==1;
            for(int i=0;i<6;i++)g.IVs[i]=(sbyte)v[$"IV{i}"];
            if(_game.ORAS){g.Move=(ushort)v["Move"];g.MetLocation=(short)v["Met"];for(int i=0;i<6;i++)g.ContestStats[i]=(byte)v[$"Contest{i}"];}
            raw=g.Write();
        }
        else
        {
            var e=new EncounterStatic6(raw){Species=(ushort)v["Species"],Form=(byte)v["Form"],Level=(byte)v["Level"],
                Gender=v["Gender"],Ability=v["Ability"],Shiny=v["Shiny"]==1,ShinyLock=v["ShinyLock"]==1,IV3=v["IV3"]==1,IV3_1=v["IV3_1"]==1};
            if(e.HeldItem!=v["Item"])e.HeldItem=v["Item"];
            raw=e.Write();
        }
        raw.CopyTo(source,slot.Offset);
    }
    private EditorProposal Build(byte[] before,byte[] result,IEnumerable<BatchChange> changes,string action,int seed,IReadOnlyDictionary<string,string> parameters)
    {
        var files=new List<FileChange>{new(_path,before,result)};
        var changedStarters=_slots.Values.Where(s=>s.Gift&&_starters.Contains(s.Index)&&
            BitConverter.ToUInt16(before,s.Offset)!=BitConverter.ToUInt16(result,s.Offset)).ToArray();
        if(changedStarters.Length>0)
        {
            string path=Path.Combine(_game.RomFS,"DllPoke3Select.cro");
            byte[] original=File.ReadAllBytes(path);ValidateCro(original);
            int start=BitConverter.ToInt32(original,0xB8);
            if(!_game.ORAS)start=checked(start+0x10);
            if(start<0x180||(long)start+(_starters.Length-1)*0x54+2>original.Length)
                throw new InvalidDataException("Selector de iniciales Gen6 incompleto o incompatible.");
            var selector=(byte[])original.Clone();
            foreach(var s in changedStarters)
            {
                int at=start+Array.IndexOf(_starters,s.Index)*0x54;
                if(BitConverter.ToUInt16(original,at)>_game.MaxSpeciesID)
                    throw new InvalidDataException("Especie inválida en el selector de iniciales.");
                result.AsSpan(s.Offset,2).CopyTo(selector.AsSpan(at,2));
            }
            files.Add(new(path,original,selector));
        }
        return new(_owner,action,seed,files,changes,parameters);
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values)
    {
        byte[] before=Source(),result=(byte[])before.Clone();var old=Read(id,before);
        Write(id,values,result);
        var changes=EditorValues.Diff(id,Entries.First(e=>e.Id==id).Name,old,Read(id,result));
        if(changes.Count>0)Apply(Build(before,result,changes,"Editar "+Title,0,new Dictionary<string,string>()));
    }
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope) => Preview(options,scope,RandomNumberGenerator.GetInt32(int.MaxValue));
    internal EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope,int seed)
    {
        EditorValues.Validate(RandomOptions,options);
        int[] ids=scope.Distinct().Order().ToArray();
        if(ids.Length==0||ids.Any(id=>!_slots.ContainsKey(id)))throw new ArgumentException("Alcance inválido.");
        var random=new Random(seed);
        byte[] before=Source(),result=(byte[])before.Clone();var changes=new List<BatchChange>();
        var profileOptions=ProfileNames.ToDictionary(p=>p,p=>EffectiveOptions(options,p));
        var pools=profileOptions.ToDictionary(p=>p.Key,p=>new SpeciesRandomizer(_game)
        {G1=p.Value["G1"]==1,G2=p.Value["G2"]==1,G3=p.Value["G3"]==1,G4=p.Value["G4"]==1,G5=p.Value["G5"]==1,G6=p.Value["G6"]==1,G7=false,L=p.Value["Legends"]==1,E=p.Value["Events"]==1}
        .GetAllowedSpeciesPool().Where(s=>s>0&&s<=_game.MaxSpeciesID&&s<_game.Personal.Table.Length).ToArray());
        foreach(int id in ids)
        {
            var slot=_slots[id];string profile=slot.Gift?(_starters.Contains(slot.Index)?"Starters":"Gifts"):"Static";var rowOptions=profileOptions[profile];var pool=pools[profile];var old=Read(id,before);var v=EditorValues.Flatten(old);
            if(v["Species"]==0|| (slot.Gift?rowOptions[_starters.Contains(slot.Index)?"Starters":"Gifts"]:rowOptions["Static"])==0)continue;
            if(slot.Gift&&rowOptions["PreserveMega"]==1&&(v["Item"] is >=656 and <=685 || _game.ORAS&&v["Item"] is >=752 and <=770))continue;
            if(rowOptions["ScaleLevels"]==1&&v["Level"]>0)v["Level"]=Math.Clamp((int)Math.Round(v["Level"]*rowOptions["LevelScale"]/100.0)+rowOptions["LevelFlat"],1,100);
            if(rowOptions["Species"]==1)
            {
                int species=v["Species"];IEnumerable<int> candidates=pool;
                bool starter=slot.Gift&&_starters.Contains(slot.Index);
                int starterGen=_game.ORAS?(slot.Index<3?3:slot.Index<=30?2:slot.Index<=33?4:5):slot.Index<3?6:1;
                if(starter&&rowOptions.GetValueOrDefault("BasicStarter")==1)
                    candidates=rowOptions.GetValueOrDefault("StarterGeneration")==1?Legal.BasicStarters[starterGen-1]:Legal.BasicStarters_6;
                else if(starter&&rowOptions.GetValueOrDefault("StarterGeneration")==1)
                    candidates=new SpeciesRandomizer(_game)
                    {
                        G1=starterGen==1,G2=starterGen==2,G3=starterGen==3,
                        G4=starterGen==4,G5=starterGen==5,G6=starterGen==6,G7=false,
                        L=rowOptions["Legends"]==1,E=rowOptions["Events"]==1,Shedinja=false
                    }.GetAllowedSpeciesPool();
                if(starter)candidates=candidates.Where(s=>s!=292);
                bool explicitStarter=starter&&(rowOptions.GetValueOrDefault("BasicStarter")==1||rowOptions.GetValueOrDefault("StarterGeneration")==1);
                if(!explicitStarter&&!Enumerable.Range(1,6).Any(g=>rowOptions["G"+g]==1))
                    throw new ArgumentException("Seleccioná al menos una generación para regalos o encuentros estáticos.");
                if(!explicitStarter&&rowOptions["LegendToLegend"]==1&&(Legal.Legendary_6.Contains(species)||Legal.Mythical_6.Contains(species)))
                    candidates=candidates.Where(s=>Legal.Legendary_6.Contains(s)||Legal.Mythical_6.Contains(s));
                if(!explicitStarter&&rowOptions["Final"]==1&&v["Level"]>=rowOptions["FinalLevel"])
                    candidates=candidates.Where(s=>Legal.FinalEvolutions_6.Contains(s)||Legal.Legendary_6.Contains(s)||Legal.Mythical_6.Contains(s));
                if(!explicitStarter&&rowOptions["BST"]>0){int bst=_game.Personal[species].BST;candidates=candidates.Where(s=>Math.Abs(_game.Personal[s].BST-bst)*100<=bst*rowOptions["BST"]);}
                int[] allowed=candidates.Where(s=>s>0&&s<=_game.MaxSpeciesID&&s<_game.Personal.Table.Length).ToArray();
                if(allowed.Length==0)throw new InvalidDataException("Los filtros no dejan especies válidas.");
                v["Species"]=WildSpeciesSelection.ChooseReplacement(allowed,species,random);v["Form"]=0;v["Gender"]=0;
            }
            if(rowOptions["Forms"]==1)
            {
                int s=v["Species"],count=_game.Personal[s].FormeCount;
                v["Form"]=s is 664 or 665 or 666?30:count>1&&(!Legal.BattleExclusiveForms.Contains(s)||rowOptions["Mega"]==1)?random.Next(count):0;
            }
            if(rowOptions["Abilities"]==1)v["Ability"]=slot.Gift?random.Next(3):random.Next(1,4);
            if(rowOptions["Items"]==1){int[] items=ProgressiveLegacy.Randomizer.GetRandomItemList(_game).Where(i=>i>0&&i<_game.GetText(TextName.ItemNames).Length).ToArray();if(items.Length==0)throw new InvalidDataException("Sin objetos válidos.");v["Item"]=items[random.Next(items.Length)];}
            if(rowOptions["RemoveLocks"]==1)v["ShinyLock"]=0;
            Write(id,v,result);changes.AddRange(EditorValues.Diff(id,Entries.First(e=>e.Id==id).Name,old,Read(id,result)));
        }
        return Build(before,result,changes,"Randomizar "+Title,seed,options.ToDictionary(p=>p.Key,p=>p.Value.ToString()));
    }
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otra sesión.");
        GarcTransaction.ReadUnchanged(_path,_hash);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        _hash=SHA256.HashData(File.ReadAllBytes(_path));
    }
}
