#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Text;

namespace pk3DS.Core.NX;

public sealed record SwordShieldGameplayOptions(uint Seed, bool Trainers=false, bool VisibleEncounters=false, bool HiddenEncounters=false, bool VisibleItems=false, bool HiddenItems=false, bool SimilarBst=true)
{
    public IReadOnlyList<string>? WorldScope {get;init;}
    public IReadOnlyList<int>? TrainerScope { get; init; }
    public bool FillEmptyEncounters {get;init;}
    public int EmptyMinLevel {get;init;}=1;
    public int EmptyMaxLevel {get;init;}=5;
    public bool ProgressiveBst { get; init; }
    public IReadOnlyList<SwordShieldBstRange> BstRanges { get; init; } = SwordShieldBstRange.Defaults;
    public bool RandomizeTrainerSpecies { get; init; } = true;
    public SwordShieldTrainerOptions TrainerRules { get; init; } = new();
}
public sealed record SwordShieldGameplayChange(string Resource,string Location,string Field,int Before,int After);
public sealed class SwordShieldGameplayResult
{
    private readonly Dictionary<string,byte[]> _files;
    public IReadOnlyList<SwordShieldGameplayChange> Changes { get; }
    public IReadOnlyList<string> Warnings {get;}
    public int EligibleSlots { get; }
    public int SkippedItems { get; }
    public SwordShieldGameplayResult(Dictionary<string,byte[]> files,List<SwordShieldGameplayChange> changes,int slots,int skipped,IReadOnlyList<string>? warnings=null)
    { Warnings=Array.AsReadOnly((warnings??Array.Empty<string>()).ToArray());_files=files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());Changes=Array.AsReadOnly(changes.ToArray());EligibleSlots=slots;SkippedItems=skipped; }
    public Dictionary<string,byte[]> Files() => _files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());
}

/// <summary>Lossless scalar patches, schema references: kwsch/pkNX SWSH (GPL-3.0).</summary>
public static class SwordShieldGameplay
{
    public const string Algorithm="swsh-gameplay-fnv-splitmix-v3";
    public const string EncountersPath="bin/archive/field/resident/data_table.gfpak";
    public const string PlacementPath="bin/archive/field/resident/placement.gfpak";
    public const string ItemHashesPath="bin/pml/item/item_hash_to_index.dat";
    // Safe SWSH regular, battle, ball, medicine, berry, ingredient and treasure pouches from pkNX Tables8. Excludes key items, TMs/TRs and Dynamax crystals.
    private static readonly HashSet<int> AllowedItems=new() { 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,76,77,79,80,81,82,83,84,85,86,87,88,89,90,91,92,94,106,107,108,109,110,112,116,117,118,119,134,135,136,149,150,151,152,153,154,155,156,157,158,159,160,161,162,163,169,170,171,172,173,174,184,185,186,187,188,189,190,191,192,193,194,195,196,197,198,199,200,201,202,203,204,205,206,207,208,209,210,211,212,213,214,215,217,218,219,220,221,222,223,224,225,228,229,230,231,232,233,234,235,236,237,238,239,240,241,242,243,244,245,246,247,248,249,250,251,252,253,254,255,257,258,259,265,266,267,268,269,270,271,272,273,274,275,276,277,278,279,280,281,282,283,284,285,286,287,288,289,290,291,292,293,294,295,296,297,298,299,300,301,302,303,304,305,306,307,308,309,310,311,312,313,314,315,316,317,318,319,320,321,322,323,324,325,326,485,486,487,488,489,490,491,492,493,494,495,496,497,498,499,500,504,537,538,539,540,541,542,543,544,545,546,547,564,565,566,567,568,569,570,571,576,580,581,582,583,591,639,640,644,645,646,647,648,649,650,686,687,688,708,709,795,796,846,849,851,852,879,880,881,882,883,884,903,904,905,906,907,908,909,910,911,912,913,914,915,916,917,918,919,920,1084,1085,1086,1087,1088,1089,1090,1091,1092,1093,1094,1095,1096,1097,1098,1099,1103,1104,1105,1106,1107,1108,1109,1110,1111,1112,1113,1114,1115,1116,1117,1118,1119,1120,1121,1122,1123,1124,1125,1126,1127,1128,1129,1231,1232,1233,1234,1235,1236,1237,1238,1239,1240,1241,1242,1243,1244,1245,1246,1247,1248,1249,1250,1251,1252,1253,1254,1256,1257,1258,1259,1260,1261,1262,1263,1264,1579,1580,1581,1582,1588,1592,1604,1606 };
    public static bool IsAllowedItem(int id)=>AllowedItems.Contains(id);
    public static SwordShieldGameplayResult Generate(IReadOnlyDictionary<string,byte[]> source,byte[] personal,bool shield,SwordShieldGameplayOptions options)
    {
        SwordShieldBattlePolicy.Validate(options);
        var personalTable=new SwordShieldPersonalTable(personal);
        var pool=personalTable.Entries().Where(e=>e.Form==0&&e.Species>0).OrderBy(e=>e.Species).ToArray();
        if(pool.Length==0)throw new InvalidDataException("No hay especies base presentes.");
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);var changes=new List<SwordShieldGameplayChange>();var warnings=new List<string>();int slots=0,skipped=0;
        byte[] Get(string path)=>source.TryGetValue(path,out var data)?data:throw new FileNotFoundException("Falta el recurso de esta sección: "+path);
        int PickSpecies(int before,string key,int level)
        {
            var original=before>0?personalTable.Read(before):pool[0];var candidates=pool;
            if(options.ProgressiveBst)
            {
                var range=options.BstRanges.Single(r=>level>=r.MinLevel&&level<=r.MaxLevel);
                if(!range.FullRandom)candidates=pool.Where(e=>e.BST>=range.MinBst&&e.BST<=range.MaxBst).ToArray();
                if(candidates.Length==0)throw new InvalidDataException($"Sin especies presentes para nivel {level}, BST {range.MinBst}–{range.MaxBst}.");
            }
            else if(options.SimilarBst && before>0)
            {
                int distance=pool.Min(e=>Math.Abs(e.BST-original.BST));int tolerance=Math.Max(distance,Math.Max(1,original.BST/5));
                candidates=pool.Where(e=>Math.Abs(e.BST-original.BST)<=tolerance).ToArray();
            }
            return candidates[Pick(options.Seed,key,candidates.Length)].Species;
        }
        void Change(string path,string location,string field,int before,int after){if(before!=after)changes.Add(new(path,location,field,before,after));}
        if(options.Trainers)
        {
            var trainerContext=options.TrainerRules.NeedsMoves||options.TrainerRules.SmartItems?SwordShieldBattlePolicy.Prepare(personal,source,options.TrainerRules):null;
            var paths=source.Keys.Where(p=>p.StartsWith("bin/trainer/trainer_poke/",StringComparison.Ordinal)&&p.EndsWith(".bin",StringComparison.Ordinal)).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            if(paths.Length==0)throw new InvalidDataException("No se encontraron equipos de entrenadores.");
            foreach(string path in paths)
            {
                int id=int.Parse(Path.GetFileNameWithoutExtension(path).Split('_').Last());if(options.TrainerScope is not null&&!options.TrainerScope.Contains(id))continue;
                var data=Get(path);var header=Get(path.Replace("/trainer_poke/","/trainer_data/",StringComparison.Ordinal).Replace("trainer_poke_","trainer_data_",StringComparison.Ordinal));
                if(header.Length!=20||data.Length%32!=0||header[3]>6)throw new InvalidDataException("Equipo de entrenador inválido: "+path);
                int count=header[3];if(count==0)continue;
                if(data.Length!=count*32)throw new InvalidDataException("El tamaño del equipo no coincide con trainer_data: "+path);
                // Mandatory early Hop variants can start with only the player starter; their scripts do not guard double battles.
                if(id is not (>=4 and <=9)&&options.TrainerRules.DoubleBattleChance>0&&header[2]==0&&count>=2&&Pick(options.Seed,path+"/double-battle",100)<options.TrainerRules.DoubleBattleChance)
                {
                    string headerPath=path.Replace("/trainer_poke/","/trainer_data/",StringComparison.Ordinal).Replace("trainer_poke_","trainer_data_",StringComparison.Ordinal);var doubleHeader=(byte[])header.Clone();doubleHeader[2]=1;files.Add(headerPath,doubleHeader);Change(headerPath,"Entrenador #"+id,"Combate doble",0,1);
                }
                var output=(byte[])data.Clone();
                for(int i=0;i<count;i++)
                {
                    int at=i*32,before=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at+12)),form=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at+14)),level=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at+10));
                    if(before<1||before>898||level<1||level>100)throw new InvalidDataException("Especie o nivel de entrenador inválido.");
                    int trainerId=int.Parse(Path.GetFileNameWithoutExtension(path).Split('_').Last());
                    level=SwordShieldBattlePolicy.TrainerLevel(trainerId,level,options.TrainerRules);
                    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at+10),(ushort)level);
                    int after=options.RandomizeTrainerSpecies?PickSpecies(before,path+"/"+i,level):before;slots++;string location=$"Equipo {Path.GetFileNameWithoutExtension(path)} · Pokémon {i+1} · nivel {level}";
                    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at+12),(ushort)after);BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at+14),(ushort)(options.RandomizeTrainerSpecies?0:form));
                    Change(path,location,"Especie",before,after);Change(path,location,"Forma",form,options.RandomizeTrainerSpecies?0:form);
                    Change(path,location,"Nivel",BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at+10)),level);
                    if(options.RandomizeTrainerSpecies&&(before!=after||form!=0)){Change(path,location,"Gigantamax",data[at+9],0);output[at+9]=0;}
                }
                SwordShieldBattlePolicy.ApplyTrainer(output,data,path,trainerContext,options,(field,slot,before,after)=>Change(path,$"Equipo {Path.GetFileNameWithoutExtension(path)} · Pokémon {slot+1}",field,before,after),warnings.Add);
                if(!output.AsSpan().SequenceEqual(data))files.Add(path,output);
            }
        }
        if(options.VisibleEncounters||options.HiddenEncounters)
        {
            byte[] archive=Get(EncountersPath);var pack=new SwitchGfPack(archive);var members=new Dictionary<int,byte[]>();
            foreach(bool visible in new[]{true,false})
            {
                if(visible?!options.VisibleEncounters:!options.HiddenEncounters)continue;
                string member=(visible?"encount_symbol_":"encount_")+(shield?"t":"k")+".bin";int index=pack.Find(member);
                if(index<0)throw new InvalidDataException("No se encontró "+member);
                var memberData=pack.Read(index);var fb=new SwitchFlatBuffer(options.FillEmptyEncounters?SwordShieldEncounterExpansion.Materialize(memberData,member,options.WorldScope):memberData);var visited=new HashSet<int>();int zoneIndex=0;
                foreach(int zone in fb.Tables(fb.Root,1,true))
                {
                    int weather=0;foreach(int sub in fb.Tables(zone,1,true))
                    {
                        if(options.WorldScope is not null&&!options.WorldScope.Contains($"{member}/{zoneIndex}/{weather}")){weather++;continue;}
                        int slot=0;bool filled=false;var encounterRows=fb.Tables(sub,2,true).ToArray();foreach(int entry in encounterRows)
                        {
                            int at=fb.Field(entry,1);if(at>=0&&!visited.Add(at)){slot++;continue;}if(at<0){slot++;continue;}int before=fb.I32(at);if(before==0&&!options.FillEmptyEncounters){slot++;continue;}if(before==0&&(fb.Field(entry,0,1)<0||fb.Field(sub,0,1)<0||fb.Field(sub,1,1)<0)){slot++;continue;}
                            if(before<0||before>898)throw new InvalidDataException("Especie de encuentro fuera de rango.");
                            int minAt=fb.Field(sub,0,1),maxAt=fb.Field(sub,1,1);
                            if(before==0){filled=true;if(fb.Data[minAt]==0&&fb.Data[maxAt]==0){Change(EncountersPath,$"{member} · zona {zoneIndex} · clima {weather}","Nivel mínimo",0,options.EmptyMinLevel);Change(EncountersPath,$"{member} · zona {zoneIndex} · clima {weather}","Nivel máximo",0,options.EmptyMaxLevel);fb.Data[minAt]=(byte)options.EmptyMinLevel;fb.Data[maxAt]=(byte)options.EmptyMaxLevel;}}
                            int level=minAt<0?1:fb.Data[minAt];
                            if(options.ProgressiveBst&&(level<1||level>100||(maxAt>=0&&(fb.Data[maxAt]<level||fb.Data[maxAt]>100))))throw new InvalidDataException("Niveles de encuentro incompatibles con Progressive BST.");
                            int after=PickSpecies(before,$"{member}/{zoneIndex}/{weather}/{slot}",level);slots++;
                            string location=$"{member} · zona {zoneIndex} · clima {weather} · ranura {slot}";
                            Change(EncountersPath,location,"Especie",before,after);BinaryPrimitives.WriteInt32LittleEndian(fb.Data.AsSpan(at),after);
                            int formAt=fb.Field(entry,2,1);if(formAt>=0){Change(EncountersPath,location,"Forma",fb.Data[formAt],0);fb.Data[formAt]=0;}slot++;
                        }
                        if(filled)
                        {
                            var active=encounterRows.Where(e=>fb.Field(e,1,4)>=0&&fb.I32(fb.Field(e,1,4))>0&&fb.Field(e,0,1)>=0).ToArray();
                            if(active.Length>100)throw new InvalidDataException("Demasiadas ranuras para repartir probabilidades.");
                            for(int n=0;n<active.Length;n++){int probability=fb.Field(active[n],0,1),value=100/active.Length+(n<100%active.Length?1:0);Change(EncountersPath,$"{member} · zona {zoneIndex} · clima {weather} · ranura {n}","Probabilidad",fb.Data[probability],value);fb.Data[probability]=(byte)value;}
                        }
                        weather++;
                    }
                    zoneIndex++;
                }
                members[index]=fb.Data;
            }
            var output=pack.Rewrite(members);if(!output.AsSpan().SequenceEqual(archive))files.Add(EncountersPath,output);
        }
        if(options.VisibleItems||options.HiddenItems)
        {
            byte[] hashes=Get(ItemHashesPath);if(hashes.Length<4)throw new InvalidDataException("Tabla de objetos truncada.");
            int count=BinaryPrimitives.ReadInt32LittleEndian(hashes);if(count<0||count>100000||hashes.Length!=4+count*16)throw new InvalidDataException("Tabla de hashes de objetos inválida.");
            var map=new Dictionary<ulong,int>();var reverse=new Dictionary<int,ulong>();
            for(int i=0;i<count;i++){int at=4+i*16;ulong hash=BinaryPrimitives.ReadUInt64LittleEndian(hashes.AsSpan(at));int id=BinaryPrimitives.ReadInt32LittleEndian(hashes.AsSpan(at+8));if(id<=0)continue;if(map.TryGetValue(hash,out int previous)&&previous!=id)throw new InvalidDataException("Hash ambiguo de objeto.");map[hash]=id;if(!reverse.ContainsKey(id))reverse[id]=hash;}
            int[] candidates=reverse.Keys.Where(IsAllowedItem).OrderBy(i=>i).ToArray();if(candidates.Length<2)throw new InvalidDataException("Se necesitan al menos dos objetos comunes seguros para evitar conservar el original.");
            var archive=Get(PlacementPath);var pack=new SwitchGfPack(archive);int names=pack.Find("AreaNameHashTable.tbl");if(names<0)throw new InvalidDataException("No se encontró la tabla de áreas.");
            var members=new Dictionary<int,byte[]>();
            foreach(string area in SwitchGfPack.AreaNames(pack.Read(names)))
            {
                if(options.WorldScope is not null&&!options.WorldScope.Contains(area))continue;
                int index=pack.Find(area+".bin");if(index<0)continue;var fb=new SwitchFlatBuffer(pack.Read(index));bool changed=false;var visited=new HashSet<int>();
                void Replace(int at,string key)
                {
                    if(at<0){skipped++;return;}if(!visited.Add(at))return;ulong hash=fb.U64(at);if(!map.TryGetValue(hash,out int before)||!IsAllowedItem(before)){skipped++;return;}
                    int originalIndex=Array.BinarySearch(candidates,before);
                    int choice=Pick(options.Seed,key,candidates.Length-1);if(choice>=originalIndex)choice++;
                    int after=candidates[choice];slots++;Change(PlacementPath,key,"Objeto",before,after);
                    if(before!=after){BinaryPrimitives.WriteUInt64LittleEndian(fb.Data.AsSpan(at),reverse[after]);changed=true;}
                }
                int zoneIndex=0;
                foreach(int zone in fb.Tables(fb.Root,0,true))
                {
                    if(options.VisibleItems){int n=0;foreach(int holder in fb.Tables(zone,6))
                    {
                        int item=fb.Table(holder,0);if(item<0)throw new InvalidDataException("Field Item sin datos.");
                        string model=fb.String(item,2);if(model=="bin/field/model/unit_obj/unit_obj_itemred01/unit_obj_itemred01")
                        {var values=fb.Vector(item,6,8);if(values.Length>0)Replace(values[0],$"{area}/zona {zoneIndex}/visible {n}/variante 0");else skipped++;}else skipped++;n++;
                    }}
                    if(options.HiddenItems){int n=0;foreach(int holder in fb.Tables(zone,19))
                    {
                        int item=fb.Table(holder,0);if(item<0)throw new InvalidDataException("Objeto oculto sin datos.");int j=0;
                        foreach(int chance in fb.Tables(item,2))Replace(fb.Field(chance,0,8),$"{area}/zona {zoneIndex}/oculto {n}/variante {j++}");n++;
                    }}
                    zoneIndex++;
                }
                if(changed)members[index]=fb.Data;
            }
            var output=pack.Rewrite(members);if(!output.AsSpan().SequenceEqual(archive))files.Add(PlacementPath,output);
        }
        return new(files,changes,slots,skipped,warnings);
    }
    private static int Pick(uint seed,string key,int count)
    {
        ulong state=SwitchGfPack.NameHash(key)^((ulong)seed<<32);ulong z=unchecked(state+0x9E3779B97F4A7C15);
        z=unchecked((z^(z>>30))*0xBF58476D1CE4E5B9);z=unchecked((z^(z>>27))*0x94D049BB133111EB);z^=z>>31;
        return (int)(z%(uint)count);
    }
}
