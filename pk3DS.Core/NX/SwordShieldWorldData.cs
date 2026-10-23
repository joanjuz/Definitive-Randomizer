#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Buffers.Binary;
namespace pk3DS.Core.NX;
public sealed record SwordShieldWorldCell(string Resource,int Member,string Region,string Group,string Field,int Offset,int Width,int Value,int Min,int Max)
{
    public string Key=>Resource+"|"+Member+"|"+Offset;
}
/// <summary>Lossless world-table adapter for the same list/form workflow as the CTR editors.</summary>
public static class SwordShieldWorldData
{
    private static (Dictionary<ulong,int> Forward,Dictionary<int,ulong> Reverse) ItemMap(byte[] data)
    {
        if(data.Length<4)throw new InvalidDataException("Tabla de objetos truncada.");int count=BinaryPrimitives.ReadInt32LittleEndian(data);if(count<0||count>100000||data.Length!=4+count*16)throw new InvalidDataException("Tabla de objetos inválida.");var forward=new Dictionary<ulong,int>();var reverse=new Dictionary<int,ulong>();
        for(int i=0;i<count;i++){int at=4+i*16;ulong hash=BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at));int id=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at+8));if(id<=0)continue;if(forward.TryGetValue(hash,out int previous)&&previous!=id)throw new InvalidDataException("Hash de objeto ambiguo.");forward[hash]=id;reverse.TryAdd(id,hash);}return(forward,reverse);
    }
    public static IReadOnlyList<int> AvailableItems(IReadOnlyDictionary<string,byte[]> files)=>ItemMap(files[SwordShieldGameplay.ItemHashesPath]).Reverse.Keys.Where(SwordShieldGameplay.IsAllowedItem).Order().ToArray();
    public static IReadOnlyList<SwordShieldWorldCell> Read(IReadOnlyDictionary<string,byte[]> files,bool items,bool shield)
    {
        var cells=new List<SwordShieldWorldCell>();string path=items?SwordShieldGameplay.PlacementPath:SwordShieldGameplay.EncountersPath;var pack=new SwitchGfPack(files[path]);
        if(!items)
        {
            foreach(bool visible in new[]{true,false})
            {
                string name=(visible?"encount_symbol_":"encount_")+(shield?"t":"k")+".bin";int index=pack.Find(name);if(index<0)throw new InvalidDataException("No se encontró "+name);var fb=new SwitchFlatBuffer(pack.Read(index));int zoneIndex=0;var visited=new HashSet<int>();
                foreach(int zone in fb.Tables(fb.Root,1,true))
                {
                    int weather=0;foreach(int sub in fb.Tables(zone,1,true))
                    {
                        string region=$"{name}/{zoneIndex}/{weather}";int slot=0;
                        foreach(var pair in new[]{(0,"Nivel mínimo"),(1,"Nivel máximo")}){int at=fb.Field(sub,pair.Item1,1);if(at>=0&&visited.Add(at))cells.Add(new(path,index,region,"Niveles",pair.Item2,at,1,fb.Data[at],1,100));}
                        foreach(int entry in fb.Tables(sub,2,true))
                        {
                            int species=fb.Field(entry,1,4);if(species<0||fb.I32(species)==0){slot++;continue;}
                            foreach(var pair in new[]{(1,"Especie",4,1,898),(2,"Forma",1,0,255),(0,"Probabilidad",1,0,100)}){int at=fb.Field(entry,pair.Item1,pair.Item3);if(at>=0&&visited.Add(at)){int value=pair.Item3==4?fb.I32(at):fb.Data[at];if(value<pair.Item4||value>pair.Item5)throw new InvalidDataException("Campo de encuentro fuera de rango: "+pair.Item2);cells.Add(new(path,index,region,"Pokémon "+(slot+1),pair.Item2,at,pair.Item3,value,pair.Item4,pair.Item5));}}slot++;
                        }weather++;
                    }zoneIndex++;
                }
            }
        }
        else
        {
            var map=ItemMap(files[SwordShieldGameplay.ItemHashesPath]);int names=pack.Find("AreaNameHashTable.tbl");if(names<0)throw new InvalidDataException("Falta la tabla de áreas.");
            foreach(string area in SwitchGfPack.AreaNames(pack.Read(names)))
            {
                int index=pack.Find(area+".bin");if(index<0)continue;var fb=new SwitchFlatBuffer(pack.Read(index));var visited=new HashSet<int>();int zoneIndex=0;
                void Add(int at,string group){if(at<0||!visited.Add(at)||!map.Forward.TryGetValue(fb.U64(at),out int id)||!SwordShieldGameplay.IsAllowedItem(id))return;cells.Add(new(path,index,area,group,"Objeto",at,8,id,1,1607));}
                foreach(int zone in fb.Tables(fb.Root,0,true))
                {
                    int n=0;foreach(int holder in fb.Tables(zone,6)){int item=fb.Table(holder,0);if(item<0)throw new InvalidDataException("Field Item sin datos.");if(fb.String(item,2)=="bin/field/model/unit_obj/unit_obj_itemred01/unit_obj_itemred01"){var values=fb.Vector(item,6,8);if(values.Length>0)Add(values[0],$"Zona {zoneIndex} · visible {n}");}n++;}
                    n=0;foreach(int holder in fb.Tables(zone,19)){int item=fb.Table(holder,0);if(item<0)throw new InvalidDataException("Objeto oculto sin datos.");int chance=0;foreach(int value in fb.Tables(item,2))Add(fb.Field(value,0,8),$"Zona {zoneIndex} · oculto {n} · variante {chance++}");n++;}zoneIndex++;
                }
            }
        }
        return cells.AsReadOnly();
    }
    public static SwordShieldGameplayResult Edit(IReadOnlyDictionary<string,byte[]> source,byte[] personal,bool items,bool shield,IReadOnlyList<SwordShieldDataEdit> edits)
    {
        var cells=Read(source,items,shield);var map=cells.ToDictionary(c=>c.Key);if(edits.Select(e=>e.Key).Distinct().Count()!=edits.Count)throw new InvalidDataException("Edición duplicada.");var table=new SwordShieldPersonalTable(personal);var reverse=items?ItemMap(source[SwordShieldGameplay.ItemHashesPath]).Reverse:new Dictionary<int,ulong>();string path=items?SwordShieldGameplay.PlacementPath:SwordShieldGameplay.EncountersPath;var pack=new SwitchGfPack(source[path]);var members=new Dictionary<int,byte[]>();var changes=new List<SwordShieldGameplayChange>();
        foreach(var edit in edits)
        {
            if(!map.TryGetValue(edit.Key,out var cell)||edit.Value<cell.Min||edit.Value>cell.Max)throw new InvalidDataException("Campo desconocido o fuera de rango.");if(cell.Value==edit.Value)continue;
            if(cell.Field=="Especie"&&!table.Read(edit.Value).Present)throw new InvalidDataException("Especie no presente.");if(cell.Width==8&&(!reverse.ContainsKey(edit.Value)||!SwordShieldGameplay.IsAllowedItem(edit.Value)))throw new InvalidDataException("Objeto no admitido en esta tabla.");
            if(!members.TryGetValue(cell.Member,out var data))members[cell.Member]=data=pack.Read(cell.Member);
            if(cell.Width==8)BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(cell.Offset),reverse[edit.Value]);else if(cell.Width==4)BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(cell.Offset),edit.Value);else data[cell.Offset]=(byte)edit.Value;
            changes.Add(new(path,cell.Region+" · "+cell.Group,cell.Field,cell.Value,edit.Value));
        }
        var output=members.Count==0?source[path]:pack.Rewrite(members);var files=source.ToDictionary(p=>p.Key,p=>p.Value);files[path]=output;var after=Read(files,items,shield).ToDictionary(c=>c.Key);
        foreach(var edit in edits)if(!after.TryGetValue(edit.Key,out var c)||c.Value!=edit.Value)throw new InvalidDataException("Falló la reapertura del campo editado.");
        foreach(var region in after.Values.GroupBy(c=>c.Region))if(region.Any(c=>c.Field=="Probabilidad"&&edits.Any(e=>e.Key==c.Key))&&region.Where(c=>c.Field=="Probabilidad").Sum(c=>c.Value) is not (0 or 100))throw new InvalidDataException("Las probabilidades deben sumar 0 % o 100 %.");
        foreach(var group in after.Values.GroupBy(c=>(c.Region,c.Group)))
        {
            var min=group.FirstOrDefault(c=>c.Field=="Nivel mínimo");var max=group.FirstOrDefault(c=>c.Field=="Nivel máximo");if(min is not null&&max is not null&&min.Value>max.Value)throw new InvalidDataException("El nivel mínimo supera al máximo.");
            var species=group.FirstOrDefault(c=>c.Field=="Especie");var form=group.FirstOrDefault(c=>c.Field=="Forma");if(species is not null&&form is not null&&edits.Any(e=>e.Key==species.Key||e.Key==form.Key)&&!table.Entries().Any(e=>e.Species==species.Value&&e.Form==form.Value))throw new InvalidDataException("Forma no presente para esa especie.");
        }
        return new(members.Count==0?new():new(){[path]=output},changes,changes.Count,0);
    }
}
