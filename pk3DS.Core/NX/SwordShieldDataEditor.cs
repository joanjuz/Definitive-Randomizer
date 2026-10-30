#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Security.Cryptography;
namespace pk3DS.Core.NX;
// Format reference: kwsch/pkNX d191cd0e5c05f2af81d9a41c1f1d82e6621b351a, GPL-3.0.
// Scalar patches preserve unknown bytes. Only Waza (known scalar-only schema) is rebuilt when a default field was omitted.
public enum SwordShieldDataSection { Moves, Learnsets, Evolutions, Machines, Items, Shops, Statics, Gifts, Trades, TrainerTeams }
public sealed record SwordShieldDataCell(string Resource,int Entry,int Slot,string Field,int Value,int Min,int Max,int Offset,int Width,int Mask=0)
{
    public string Key=>Resource+"|"+Entry+"|"+Slot+"|"+Field;
    public string Location=>Entry+" / "+Slot;
}
public sealed record SwordShieldDataEdit(string Key,int Value);
public sealed record SwordShieldDataChange(string Resource,string Location,string Field,int Before,int After);
public sealed record SwordShieldDataOptions(uint Seed,bool RemoveTradeEvolutions=false,int CompatibilityPercent=50,bool RandomTypes=true,bool RandomCategories=true,bool SimilarBst=false,bool ThreeStageStarters=false,bool SpecialShopsOnly=false) {public IReadOnlyList<int>? SpecialShopEntries {get;init;}public IReadOnlyList<int>? StarterEntries {get;init;}}
public sealed class SwordShieldDataResult
{
    private readonly Dictionary<string,byte[]> _files;
    public IReadOnlyList<SwordShieldDataChange> Changes {get;}
    internal SwordShieldDataResult(Dictionary<string,byte[]> files,List<SwordShieldDataChange> changes){_files=files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());Changes=changes.AsReadOnly();}
    public IReadOnlyDictionary<string,byte[]> Files()=>_files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());
}
public static class SwordShieldDataEditor
{
    public const string LearnPath="bin/pml/waza_oboe/wazaoboe_total.bin",ShopPath="bin/appli/shop/bin/shop_data.bin",ItemPath="bin/pml/item/item.dat",PersonalPath="bin/pml/personal/personal_total.bin";
    public static string EventPath(SwordShieldDataSection s)=>s switch{SwordShieldDataSection.Statics=>"bin/script_event_data/event_encount_data.bin",SwordShieldDataSection.Gifts=>"bin/script_event_data/add_poke.bin",SwordShieldDataSection.Trades=>"bin/script_event_data/field_trade.bin",_=>throw new ArgumentException("Sección de eventos inválida.")};
    private static readonly string[] MoveNames={"Tipo","Categoría","Potencia","Precisión","PP","Prioridad"};
    private static readonly int[] MoveFields={3,5,6,7,8,9};
    private static readonly int[] MoveMax={17,2,255,101,255,5};
    private static int Width(int f)=>f is 0 or 1?4:f is 12 or 19?2:1;
    private static int ReadValue(SwitchFlatBuffer fb,int table,int f){int w=Width(f),at=fb.Field(table,f,w);return at<0?0:w==4?fb.I32(at):w==2?fb.U16(at):f is 9 or 17 or 20 or 21 or 26 or 27 or 28?unchecked((sbyte)fb.Data[at]):fb.Data[at];}
    private static void CheckMove(SwitchFlatBuffer fb){int vt=fb.Root-fb.I32(fb.Root);if(fb.U16(vt)>4+51*2)throw new InvalidDataException("Esquema Waza desconocido.");for(int f=0;f<51;f++)_ = ReadValue(fb,fb.Root,f);}
    private static bool CanonicalMove(string path,int id)
    {
        string name=Path.GetFileNameWithoutExtension(path);return name.StartsWith("waza",StringComparison.Ordinal)&&int.TryParse(name.AsSpan(4),out int fileId)&&fileId>0&&fileId==id;
    }
    public static IReadOnlyList<int> UsableMoves(IReadOnlyDictionary<string,byte[]> files)=>files.Where(p=>p.Key.StartsWith("bin/pml/waza/",StringComparison.Ordinal)).Select(p=>{var fb=new SwitchFlatBuffer(p.Value);CheckMove(fb);return (Id:ReadValue(fb,fb.Root,1),Use:ReadValue(fb,fb.Root,2),Canonical:CanonicalMove(p.Key,ReadValue(fb,fb.Root,1)));}).Where(p=>p.Canonical&&p.Id>0&&p.Id<=826&&p.Use==1&&!(p.Id>=622&&p.Id<=658)&&!(p.Id>=695&&p.Id<=703)&&p.Id!=719&&!(p.Id>=757&&p.Id<=774)).Select(p=>p.Id).Distinct().Order().ToArray();
    public static IReadOnlyList<SwordShieldDataCell> Read(SwordShieldDataSection section,IReadOnlyDictionary<string,byte[]> files,byte[] personal,int? onlyEntry=null)
    {
        var cells=new List<SwordShieldDataCell>();var table=new SwordShieldPersonalTable(personal);var entries=table.Entries().Where(e=>e.Species>0&&(onlyEntry is null||e.Index==onlyEntry)).ToArray();
        void Add(string path,int entry,int slot,string field,int value,int min,int max,int offset,int width,int mask=0){if(value<min||value>max)throw new InvalidDataException("Valor original fuera de rango: "+path+" · "+field);cells.Add(new(path,entry,slot,field,value,min,max,offset,width,mask));}
        byte[] Get(string path)=>files.TryGetValue(path,out var b)?b:throw new FileNotFoundException(path);
        switch(section)
        {
            case SwordShieldDataSection.Moves:
                foreach(var p in files.Where(p=>p.Key.StartsWith("bin/pml/waza/",StringComparison.Ordinal)).OrderBy(p=>p.Key,StringComparer.Ordinal))
                {var fb=new SwitchFlatBuffer(p.Value);CheckMove(fb);int id=ReadValue(fb,fb.Root,1);if(!CanonicalMove(p.Key,id)||ReadValue(fb,fb.Root,2)==0)continue;for(int i=0;i<MoveFields.Length;i++){int f=MoveFields[i];Add(p.Key,id,0,MoveNames[i],ReadValue(fb,fb.Root,f),i==5?-7:0,MoveMax[i],f,-1);}}
                break;
            case SwordShieldDataSection.Learnsets:
                var learn=Get(LearnPath);if(learn.Length!=SwordShieldPersonalTable.EntryCount*0x104)throw new InvalidDataException("Tabla de aprendizajes incompatible.");
                foreach(var e in entries)for(int slot=0;slot<65;slot++){int at=e.Index*0x104+slot*4,m=BinaryPrimitives.ReadUInt16LittleEndian(learn.AsSpan(at)),level=BinaryPrimitives.ReadUInt16LittleEndian(learn.AsSpan(at+2));if(m==65535&&level==65535)break;if(m==65535||level==65535)throw new InvalidDataException("Sentinela de aprendizaje incompleto.");Add(LearnPath,e.Index,slot,"Movimiento",m,1,826,at,2);Add(LearnPath,e.Index,slot,"Nivel",level,0,100,at+2,2);}
                break;
            case SwordShieldDataSection.Evolutions:
                foreach(var p in files.Where(p=>p.Key.StartsWith("bin/pml/evolution/",StringComparison.Ordinal)).OrderBy(p=>p.Key,StringComparer.Ordinal))
                {if(!int.TryParse(Path.GetFileNameWithoutExtension(p.Key).Replace("evo_",""),out int id)||id>=1192)throw new InvalidDataException("Nombre de evolución inválido.");if(onlyEntry is not null&&id!=onlyEntry)continue;if(p.Value.Length!=72)throw new InvalidDataException("Tabla de evolución incompatible.");for(int slot=0;slot<9;slot++){int at=slot*8,method=BinaryPrimitives.ReadUInt16LittleEndian(p.Value.AsSpan(at));if(method==0)continue;Add(p.Key,id,slot,"Método",method,0,49,at,2);Add(p.Key,id,slot,"Argumento",BinaryPrimitives.ReadUInt16LittleEndian(p.Value.AsSpan(at+2)),0,65535,at+2,2);Add(p.Key,id,slot,"Especie",BinaryPrimitives.ReadUInt16LittleEndian(p.Value.AsSpan(at+4)),1,898,at+4,2);Add(p.Key,id,slot,"Forma",p.Value[at+6],0,255,at+6,1);Add(p.Key,id,slot,"Nivel",p.Value[at+7],0,100,at+7,1);}}
                break;
            case SwordShieldDataSection.Machines:
                foreach(var e in entries)foreach(var group in new[]{("MT",0x28,100),("TR",0x3C,100),("Tutor",0x38,8),("Tutor Armadura",0xA8,18)})for(int bit=0;bit<group.Item3;bit++){int at=e.Index*176+group.Item2+bit/8,mask=1<<(bit%8);Add(PersonalPath,e.Index,bit,group.Item1,(personal[at]&mask)==0?0:1,0,1,at,1,mask);}
                break;
            case SwordShieldDataSection.TrainerTeams:
                foreach(var p in files.Where(p=>p.Key.StartsWith("bin/trainer/trainer_poke/",StringComparison.Ordinal)).OrderBy(p=>p.Key,StringComparer.Ordinal))
                {
                    if(!int.TryParse(Path.GetFileNameWithoutExtension(p.Key).Replace("trainer_poke_",""),out int id)||p.Value.Length%32!=0)throw new InvalidDataException("Equipo incompatible.");
                    string headerPath=p.Key.Replace("/trainer_poke/","/trainer_data/",StringComparison.Ordinal).Replace("trainer_poke_","trainer_data_",StringComparison.Ordinal);var header=Get(headerPath);if(header.Length!=20||header[3]>6)throw new InvalidDataException("Cabecera de entrenador incompatible.");if(header[3]==0)continue;if(header[3]*32!=p.Value.Length)throw new InvalidDataException("El equipo no coincide con trainer_data.");
                    for(int slot=0;slot<header[3];slot++)
                    {
                        int at=slot*32;void Byte(string name,int off,int min,int max)=>Add(p.Key,id,slot,name,p.Value[at+off],min,max,at+off,1);void Short(string name,int off,int min,int max)=>Add(p.Key,id,slot,name,BinaryPrimitives.ReadUInt16LittleEndian(p.Value.AsSpan(at+off)),min,max,at+off,2);
                        Byte("Naturaleza",1,0,255);for(int stat=0;stat<6;stat++)Byte("EV "+new[]{"PS","Ataque","Defensa","Ataque Especial","Defensa Especial","Velocidad"}[stat],2+stat,0,255);Byte("Dynamax",8,0,10);Byte("Gigantamax",9,0,1);Short("Nivel",10,1,100);Short("Especie",12,1,898);Short("Forma",14,0,255);Short("Objeto equipado",16,0,1607);for(int move=0;move<4;move++)Short("Movimiento "+(move+1),18+move*2,0,826);
                        uint iv=BinaryPrimitives.ReadUInt32LittleEndian(p.Value.AsSpan(at+28));for(int stat=0;stat<6;stat++)Add(p.Key,id,slot,"IV "+new[]{"PS","Ataque","Defensa","Velocidad","Ataque Especial","Defensa Especial"}[stat],(int)((iv>>(5*stat))&31),0,31,at+28,4,31<<(5*stat));
                    }
                }
                break;
            case SwordShieldDataSection.Items:
                var items=Get(ItemPath);if(items.Length<0x44)throw new InvalidDataException("Tabla de objetos truncada.");int count=BinaryPrimitives.ReadUInt16LittleEndian(items),maxEntry=BinaryPrimitives.ReadUInt16LittleEndian(items.AsSpan(4)),start=BinaryPrimitives.ReadInt32LittleEndian(items.AsSpan(0x40));if(count>2000||maxEntry>2000||0x44+count*2>items.Length||start<0x44+count*2||start>items.Length-maxEntry*48)throw new InvalidDataException("Índice de objetos inválido.");for(int id=1;id<count;id++){int index=BinaryPrimitives.ReadUInt16LittleEndian(items.AsSpan(0x44+id*2));if(index>=maxEntry)throw new InvalidDataException("Objeto fuera de la tabla.");int at=start+index*48;foreach(var f in new[]{("Precio",0,4),("Precio Watts",4,2),("Precio BP",8,2)}){long price=f.Item3==4?BinaryPrimitives.ReadUInt32LittleEndian(items.AsSpan(at+f.Item2)):BinaryPrimitives.ReadUInt16LittleEndian(items.AsSpan(at+f.Item2));if(price>int.MaxValue)throw new InvalidDataException("Precio no representable.");Add(ItemPath,id,0,f.Item1,(int)price,0,f.Item3==4?9999999:65535,at+f.Item2,f.Item3);}}
                break;
            case SwordShieldDataSection.Shops:
                var shops=new SwitchFlatBuffer(Get(ShopPath));var visited=new HashSet<int>();
                void Inventory(int inv,int shop,int stage){int slot=0;foreach(int at in shops.Vector(inv,0,4,true)){int itemSlot=slot++;if(itemSlot>=10000)throw new InvalidDataException("Inventario demasiado grande.");if(!visited.Add(at))continue;int id=shops.I32(at);if(id<=0)continue;Add(ShopPath,shop,stage*10000+itemSlot,"Objeto",id,1,1607,at,4);}}
                int shopIndex=0;foreach(int shop in shops.Tables(shops.Root,0,true)){int inv=shops.Table(shop,1);if(inv<0)throw new InvalidDataException("Inventario ausente.");Inventory(inv,shopIndex++,0);}foreach(int shop in shops.Tables(shops.Root,1,true)){int stage=0;foreach(int inv in shops.Tables(shop,1,true))Inventory(inv,shopIndex,stage++);shopIndex++;}
                // Distinguish each item slot, while preserving inventory progression and hashes.
                break;
            case SwordShieldDataSection.Statics:case SwordShieldDataSection.Gifts:case SwordShieldDataSection.Trades:
                string path=EventPath(section);var events=new SwitchFlatBuffer(Get(path));int entry=0;foreach(int ev in events.Tables(events.Root,0,true)){int speciesField=section==SwordShieldDataSection.Statics?17:section==SwordShieldDataSection.Gifts?9:8;int species=events.Field(ev,speciesField,4);if(species<0||events.I32(species)<=0){entry++;continue;}int scenario=section==SwordShieldDataSection.Statics?events.Field(ev,16,4):-1;if(scenario>=0&&events.I32(scenario)!=0){entry++;continue;}Add(path,entry,0,"Especie",events.I32(species),1,898,species,4);int formField=section==SwordShieldDataSection.Statics?8:section==SwordShieldDataSection.Gifts?1:0;int form=events.Field(ev,formField,1);if(form>=0)Add(path,entry,0,"Forma",events.Data[form],0,255,form,1);int gmax=events.Field(ev,section==SwordShieldDataSection.Statics?13:section==SwordShieldDataSection.Gifts?6:5,1);if(gmax>=0)Add(path,entry,0,"Gigantamax",events.Data[gmax],0,1,gmax,1);int level=events.Field(ev,section==SwordShieldDataSection.Statics?15:section==SwordShieldDataSection.Gifts?8:7,1);if(level>=0)Add(path,entry,0,"Nivel",events.Data[level],0,100,level,1);entry++;}break;
            default:throw new ArgumentOutOfRangeException(nameof(section));
        }
        return cells.AsReadOnly();
    }
    public static SwordShieldDataResult Edit(SwordShieldDataSection section,IReadOnlyDictionary<string,byte[]> files,byte[] personal,IReadOnlyList<SwordShieldDataEdit> edits)
    {
        var rows=Read(section,files,personal);var personalTable=new SwordShieldPersonalTable(personal);var map=rows.ToDictionary(c=>c.Key);if(edits.Select(e=>e.Key).Distinct().Count()!=edits.Count)throw new InvalidDataException("Edición duplicada.");var result=new Dictionary<string,byte[]>(StringComparer.Ordinal);var changes=new List<SwordShieldDataChange>();var pendingMoves=new Dictionary<string,Dictionary<int,int>>();
        foreach(var edit in edits){if(!map.TryGetValue(edit.Key,out var cell)||edit.Value<cell.Min||edit.Value>cell.Max)throw new InvalidDataException("Edición desconocida o fuera de rango: "+edit.Key);if(cell.Field=="Naturaleza"&&edit.Value>24&&edit.Value!=255)throw new InvalidDataException("Naturaleza: 0–24 o 255 (aleatoria).");if(edit.Value==cell.Value)continue;
            if(cell.Field=="Especie"&&!personalTable.Read(edit.Value).Present)throw new InvalidDataException("Especie no presente.");
            if(!result.TryGetValue(cell.Resource,out var data)){data=(byte[])(cell.Resource==PersonalPath?personal:files[cell.Resource]).Clone();result.Add(cell.Resource,data);}
            if(cell.Width==-1){if(!pendingMoves.TryGetValue(cell.Resource,out var values))pendingMoves[cell.Resource]=values=new();values[cell.Offset]=edit.Value;}
            else if(cell.Mask!=0)
            {
                uint mask=unchecked((uint)cell.Mask);int shift=System.Numerics.BitOperations.TrailingZeroCount(mask);uint word=cell.Width==4?BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cell.Offset)):data[cell.Offset];word=(word&~mask)|(((uint)edit.Value<<shift)&mask);if(cell.Width==4)BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(cell.Offset),word);else data[cell.Offset]=(byte)word;
            }
            else if(cell.Width==1)data[cell.Offset]=unchecked((byte)edit.Value);else if(cell.Width==2)BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(cell.Offset),(ushort)edit.Value);else BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(cell.Offset),edit.Value);
            changes.Add(new(cell.Resource,cell.Location,cell.Field,cell.Value,edit.Value));
        }
        foreach(var p in pendingMoves)result[p.Key]=PatchMove(result[p.Key],p.Value);
        var combined=files.ToDictionary(p=>p.Key,p=>p.Value);foreach(var p in result)combined[p.Key]=p.Value;var after=Read(section,combined,result.TryGetValue(PersonalPath,out var output)?output:personal).ToDictionary(c=>c.Key);
        foreach(var edit in edits)if(!after.TryGetValue(edit.Key,out var cell)||cell.Value!=edit.Value)throw new InvalidDataException("La edición no superó la reapertura: "+edit.Key);
        var editedKeys=edits.Select(e=>e.Key).ToHashSet();
        foreach(var original in rows)if(!editedKeys.Contains(original.Key)&&(!after.TryGetValue(original.Key,out var preserved)||preserved.Value!=original.Value))throw new InvalidDataException("La edición afectaría un campo no seleccionado: "+original.Key);
        var formRows=after.Values.Where(c=>c.Field=="Forma").ToDictionary(c=>(c.Resource,c.Entry,c.Slot));
        foreach(var species in after.Values.Where(c=>c.Field=="Especie"&&(editedKeys.Contains(c.Key)||formRows.TryGetValue((c.Resource,c.Entry,c.Slot),out var f)&&editedKeys.Contains(f.Key))))
        {
            formRows.TryGetValue((species.Resource,species.Entry,species.Slot),out var form);
            if(form is not null&&form.Value!=255&&form.Value>=Math.Max(1,(int)personal[species.Value*176+0x20]))throw new InvalidDataException("Forma no disponible para la especie elegida.");
            if(form?.Value==255&&species.Value!=map[species.Key].Value)throw new InvalidDataException("Al cambiar de especie, elige una forma explícita en lugar de 255.");
        }

        return new(result,changes);
    }
    private static byte[] PatchMove(byte[] source,Dictionary<int,int> changes)
    {
        var fb=new SwitchFlatBuffer(source);CheckMove(fb);if(changes.Keys.All(f=>fb.Field(fb.Root,f,Width(f))>=0)){foreach(var p in changes){int at=fb.Field(fb.Root,p.Key,Width(p.Key));fb.Data[at]=unchecked((byte)p.Value);}return fb.Data;}
        // Construct a complete scalar table. Preserve every schema value, including flags and effect fields.
        int[] values=Enumerable.Range(0,51).Select(f=>ReadValue(fb,fb.Root,f)).ToArray();foreach(var p in changes)values[p.Key]=p.Value;
        int table=112,offset=4;var offsets=new int[51];for(int f=0;f<51;f++){int w=Width(f);offset=(offset+w-1)&~(w-1);offsets[f]=offset;offset+=w;}var data=new byte[table+offset];BinaryPrimitives.WriteInt32LittleEndian(data,table);BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4),106);BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6),(ushort)offset);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(table),table-4);
        for(int f=0;f<51;f++){BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8+f*2),(ushort)offsets[f]);int at=table+offsets[f];switch(Width(f)){case 4:BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at),values[f]);break;case 2:BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at),(ushort)values[f]);break;default:data[at]=unchecked((byte)values[f]);break;}}
        var reopened=new SwitchFlatBuffer(data);for(int f=0;f<51;f++)if(ReadValue(reopened,reopened.Root,f)!=values[f])throw new InvalidDataException("Reconstrucción Waza inválida.");return data;
    }
    public static SwordShieldDataResult Randomize(SwordShieldDataSection section,IReadOnlyDictionary<string,byte[]> files,byte[] personal,SwordShieldDataOptions options)
    {
        if(options.CompatibilityPercent<0||options.CompatibilityPercent>100)throw new ArgumentOutOfRangeException(nameof(options));var rows=Read(section,files,personal);var edits=new Dictionary<string,int>();var present=new SwordShieldPersonalTable(personal).Entries().Where(e=>e.Form==0&&e.Species>0).Select(e=>e.Species).ToArray();int[] moves=section==SwordShieldDataSection.Learnsets?UsableMoves(files).ToArray():Array.Empty<int>();if(section==SwordShieldDataSection.Learnsets&&moves.Length==0)throw new InvalidDataException("No hay movimientos válidos.");
        int Pick(string key,int n){byte[] h=SHA256.HashData(Encoding.UTF8.GetBytes(options.Seed+"|"+key));return (int)(BinaryPrimitives.ReadUInt64LittleEndian(h)%(ulong)n);}
        var evoSources=rows.Where(c=>c.Field=="Método").Select(c=>c.Entry).ToHashSet();int[] terminals=present.Where(s=>!evoSources.Contains(s)).ToArray();
        var personalEntries=new SwordShieldPersonalTable(personal).Entries().Where(e=>e.Form==0&&e.Species>0).ToDictionary(e=>e.Species);
        var starterPool=Array.Empty<int>();
        if(options.ThreeStageStarters&&section==SwordShieldDataSection.Gifts)
        {
            var evolutionRows=Read(SwordShieldDataSection.Evolutions,files,personal);
            var edges=evolutionRows.Where(c=>c.Field=="Especie").GroupBy(c=>c.Entry).ToDictionary(g=>g.Key,g=>g.Select(c=>c.Value).Distinct().ToArray());
            var incoming=edges.Values.SelectMany(x=>x).ToHashSet();
            bool TwoSteps(int s)=>edges.TryGetValue(s,out var middle)&&middle.Length>0&&middle.All(m=>m!=s&&edges.TryGetValue(m,out var ends)&&ends.Length>0&&ends.All(e=>e!=s&&e!=m&&!edges.ContainsKey(e)&&personalEntries.ContainsKey(e))&&personalEntries.ContainsKey(m));
            starterPool=present.Where(s=>!incoming.Contains(s)&&TwoSteps(s)).ToArray();if(starterPool.Length==0)throw new InvalidDataException("No hay iniciales presentes con exactamente tres etapas evolutivas.");
        }
        int EventSpecies(SwordShieldDataCell c)
        {
            var pool=options.ThreeStageStarters&&section==SwordShieldDataSection.Gifts&&(options.StarterEntries?.Contains(c.Entry)??(c.Value is 810 or 813 or 816))?starterPool:present;
            if(options.SimilarBst){int bst=personalEntries[c.Value].Values.Take(6).Sum();pool=pool.Where(s=>Math.Abs(personalEntries[s].Values.Take(6).Sum()-bst)<=Math.Max(1,bst/5)).ToArray();}
            if(pool.Length==0)throw new InvalidDataException("No hay candidatos para las restricciones de BST/iniciales.");return pool[Pick(c.Key,pool.Length)];
        }
        // Conservative DLC gift protection until their species-dependent story scripts are validated.
        var protectedGifts=section==SwordShieldDataSection.Gifts?rows.Where(c=>c.Field=="Especie"&&c.Value is 891 or 896 or 897).Select(c=>c.Entry).ToHashSet():new HashSet<int>();
        var specialShops=section==SwordShieldDataSection.Shops&&options.SpecialShopsOnly?(options.SpecialShopEntries??SwordShieldShopClassification.SpecialShopIds(files[ShopPath])).ToHashSet():null;
        foreach(var c in rows){if(protectedGifts.Contains(c.Entry)||specialShops is not null&&!specialShops.Contains(c.Entry))continue;int? value=null;switch(section){case SwordShieldDataSection.Moves:if(c.Field=="Tipo"&&options.RandomTypes||c.Field=="Categoría"&&options.RandomCategories)value=Pick(c.Key,c.Max+1);break;case SwordShieldDataSection.Learnsets:if(c.Field=="Movimiento")value=moves[Pick(c.Key,moves.Length)];break;case SwordShieldDataSection.Evolutions:if(options.RemoveTradeEvolutions){var method=rows.First(x=>x.Resource==c.Resource&&x.Slot==c.Slot&&x.Field=="Método");if(method.Value is 5 or 6 or 7)value=c.Field switch{"Método"=>4,"Argumento"=>0,"Nivel"=>37,_=>(int?)null};}else if(c.Field=="Especie"){var candidates=terminals.Where(s=>s!=c.Entry).ToArray();if(candidates.Length==0)throw new InvalidDataException("Sin destinos de evolución seguros.");value=candidates[Pick(c.Key,candidates.Length)];}else if(c.Field=="Forma")value=0;break;case SwordShieldDataSection.Machines:value=Pick(c.Key,100)<options.CompatibilityPercent?1:0;break;case SwordShieldDataSection.Items:if(c.Value>0&&SwordShieldGameplay.IsAllowedItem(c.Entry))value=1+Pick(c.Key,Math.Min(50000,c.Max));break;case SwordShieldDataSection.Shops:if(SwordShieldGameplay.IsAllowedItem(c.Value)){var candidates=Enumerable.Range(1,1607).Where(SwordShieldGameplay.IsAllowedItem).Where(i=>i!=c.Value).ToArray();value=candidates[Pick(c.Key,candidates.Length)];}break;default:if(c.Field=="Especie")value=EventSpecies(c);else if(c.Field is "Forma" or "Gigantamax")value=0;break;}if(value is int v)edits[c.Key]=v;}
        if(section==SwordShieldDataSection.Learnsets)
        {
            var usable=moves.ToHashSet();var damaging=Read(SwordShieldDataSection.Moves,files,personal).GroupBy(c=>c.Entry).Where(g=>usable.Contains(g.Key)&&g.First(c=>c.Field=="Categoría").Value!=0&&g.First(c=>c.Field=="Potencia").Value>0).Select(g=>g.Key).ToArray();
            if(damaging.Length==0)throw new InvalidDataException("No hay movimientos ofensivos utilizables.");
            foreach(var first in rows.Where(c=>c.Field=="Movimiento").GroupBy(c=>c.Entry).Select(g=>g.OrderBy(c=>c.Slot).First()))edits[first.Key]=damaging[Pick(first.Key,damaging.Length)];
        }
        return Edit(section,files,personal,edits.Select(p=>new SwordShieldDataEdit(p.Key,p.Value)).ToArray());
    }
}
