using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Fixed-size XY/ORAS code.bin inventories from MartEditor6.</summary>
public sealed partial class GameMart6Service : IProjectEditor
{
    internal static readonly byte[] Vanilla=Convert.FromHexString("00726F6D3A2F446C6C53746172744D656E752E63726F00");
    internal static readonly byte[] Patched=Convert.FromHexString("00726F6D323A2F446C6C53746172744D656E752E63726F00FF");
    internal static readonly int[] CountsXY=[2,11,14,17,18,19,19,19,19,1,4,10,3,9,1,1,3,3,5,5,6,7,5,5,8,3];
    internal static readonly int[] CountsORAS=[3,10,14,17,18,19,19,19,19,1,9,6,4,3,8,8,3,3,4,3,6,8,7,4];
    private static readonly HashSet<int> Protected=[..Enumerable.Range(328,100),618,619,620,690,691,692,693,694,701,737];
    internal static bool IsProtectedItem(int id)=>Protected.Contains(id);
    private readonly GameProjectSession _project;
    private readonly string _codePath,_itemPath;
    private readonly GARCReference _itemRef;
    private byte[] _codeHash;
    private byte[]? _itemHash;
    private readonly int[] _counts,_offsets;
    private readonly IReadOnlyList<MoveChoice> _items;
    private readonly Guid _owner=Guid.NewGuid();
    public string Title=>"Tiendas · Gen6";
    public IReadOnlyList<RecordEntry> Entries {get;}
    public IReadOnlyList<EditorGroup> RandomOptions=>[new("Operación sobre el alcance elegido",[
        new("Operation","Operación",0,0,1,[new(0,"Randomizar inventarios"),new(1,"Caramelos Raros · última ranura regular y precio 10")]),
        new("SpecialOnly","Randomizar solo tiendas especiales",1,0,1),
        new("KeepX","Conservar objetos X al randomizar",1,0,1),
        new("UseChecks","Usar presets combinados",0,0,1),new("Randomize","Randomizar tiendas",0,0,1),
        new("Candies","Caramelos raros",0,0,1),new("EVItems","Objetos EV (efecto original)",0,0,1),new("BanCapsule","Prohibir Cápsula Habilidad",0,0,1)])];
    public GameMart6Service(GameProjectSession project)
    {
        if(project.Generation!=6)throw new ArgumentException("El editor requiere XY u ORAS.");
        _project=project;var config=project.GameConfiguration;_counts=(int[])(config.ORAS?CountsORAS:CountsXY).Clone();
        _items=project.ItemNames.Take(ushort.MaxValue+1).Select((n,i)=>new MoveChoice(i,n)).ToArray();
        _codePath=new[]{".code.bin","code.bin"}.Select(n=>Path.Combine(config.ExeFS,n)).FirstOrDefault(File.Exists)
            ??throw new FileNotFoundException("Falta code.bin descomprimido.");
        byte[] source=File.ReadAllBytes(_codePath);_codeHash=SHA256.HashData(source);
        int offset=Find(source,_counts.Sum()*2);
        try{_structure=new Gen6MartTable(project,source,offset,_counts);_counts=_structure.Counts(source);offset=_structure.TableOffset(source);}
        catch(InvalidDataException ex) when(source.AsSpan().IndexOf("DRM6"u8)<0){ExpansionReason=ex.Message;}
        _offsets=new int[_counts.Length];
        for(int i=0;i<_counts.Length;i++){_offsets[i]=offset;offset+=2*_counts[i];}
        string[] common=["Sin medallas","1 medalla","2 medallas","3 medallas","4 medallas","5 medallas","6 medallas","7 medallas","8 medallas"];
        string[] special=config.ORAS?
            ["Antes de la Pokédex","Portual · inciensos","Portual · vitaminas","Portual · MT","Férrica · Poké Balls","Portual · objetos X","Malvalona · MT","Verdegal · Poké Balls","Pardal · Poké Balls","Lavacalda · hierbas","Calagua 2F · huida","Calagua 3F · vitaminas","Calagua 3F · objetos X","Calagua 4F · MT ofensivas","Calagua 4F · MT defensivas"]:
            ["Sin uso","Luminalia · hierbas","Luminalia · Poké Balls","Luminalia · piedras","Templin · inciensos","Acua · Poké Ball","Acua · Poción","Luminalia norte · Poké Balls","Relieve · Poké Balls","Yantra · MT","Luminalia sur · MT","Romantis · vitaminas","Fractal · Poké Balls","Batik · MT","Fluxus · MT","Novarte · objetos X","Templin · Poké Balls"];
        Entries=common.Concat(special).Select((n,i)=>new RecordEntry(i+1,n)).ToArray();
        for(int id=1;id<=Entries.Count;id++)_=Read(id,source);
        _itemRef=config.GetGARCReference("item");_itemPath=Path.Combine(config.RomFS,config.GetGARCFileName("item"));
        if(File.Exists(_itemPath))_itemHash=SHA256.HashData(File.ReadAllBytes(_itemPath));
    }
    private static int Find(byte[] source,int bytes)
    {
        if(source.Length%512!=0||source.Length<0x400000)throw new InvalidDataException("code.bin comprimido o truncado.");
        var candidates=new List<int>();
        foreach(byte[] sig in new[]{Vanilla,Patched})
        {
            int start=0x400000;
            while(start<=source.Length-sig.Length)
            {int found=source.AsSpan(start).IndexOf(sig);if(found<0)break;int at=start+found;candidates.Add(at+sig.Length);start=at+1;}
        }
        if(candidates.Count!=1||(long)candidates[0]+bytes>source.Length)throw new InvalidDataException("Firma de tiendas ausente, duplicada o tabla incompleta.");
        return candidates[0];
    }
    private byte[] Source(){_structure?.ValidateHeader();return GarcTransaction.ReadUnchanged(_codePath,_codeHash);}
    private byte[] RebuildExisting(byte[] before,byte[] changed)=>_structure is null?changed:_structure.Build(before,Enumerable.Range(0,_counts.Length).Select(i=>Enumerable.Range(0,_counts[i]).Select(n=>(int)BitConverter.ToUInt16(changed,_offsets[i]+n*2)).ToArray()).ToArray());
    private IReadOnlyList<EditorGroup> Read(int id,byte[] bytes)
    {
        if(id<1||id>_counts.Length)throw new ArgumentOutOfRangeException(nameof(id));
        var fields=Enumerable.Range(0,_counts[id-1]).Select(i=>new EditorField($"Item{i}",$"Ranura {i+1:00}",BitConverter.ToUInt16(bytes,_offsets[id-1]+2*i),0,_items.Count-1,_items)).ToArray();
        if(fields.Any(f=>f.Value>=_items.Count))throw new InvalidDataException("La tabla contiene objetos fuera del catálogo.");
        return [new(Entries[id-1].Name+" · ranuras fijas",fields)];
    }
    public IReadOnlyList<EditorGroup> Read(int id)=>Read(id,Source());
    public void Save(int id,IReadOnlyDictionary<string,int> values)
    {
        using var manual=ManualOperationScope.Record(this,id,values);
        byte[] before=Source(),next=(byte[])before.Clone();EditorValues.Validate(Read(id,before),values);
        for(int i=0;i<_counts[id-1];i++)BitConverter.GetBytes((ushort)values[$"Item{i}"]).CopyTo(next,_offsets[id-1]+i*2);
        next=RebuildExisting(before,next);
        var changes=EditorValues.Diff(id,Entries[id-1].Name,Read(id,before),Read(id,next));
        Apply(new(_owner,"Editar tienda",0,[new FileChange(_codePath,before,next)],changes,new Dictionary<string,string>{{"ActionId","marts6.edit"}}));
    }
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope) => Preview(options,scope,RandomNumberGenerator.GetInt32(int.MaxValue));
    internal EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope,int seed)
    {
        EditorValues.Validate(RandomOptions,options);int[] ids=scope.Distinct().Order().ToArray();
        if(ids.Length==0||ids.Any(i=>i<1||i>Entries.Count))throw new ArgumentException("Alcance de tiendas inválido.");
        if(options["UseChecks"]==1)return PrepareDraft(ReadAllItems(),options,scope,seed);
        byte[] source=Source(),next=(byte[])source.Clone();var rng=new Random(seed);
        var files=new List<FileChange>();var changes=new List<BatchChange>();bool candy=options["Operation"]==1;
        if(candy)
        {
            int regular=_project.GameConfiguration.ORAS?10:9;ids=ids.Where(id=>id<=regular).ToArray();
            if(ids.Length==0)throw new ArgumentException("Seleccioná al menos una tienda regular para Caramelos Raros.");
            int item=_items.FirstOrDefault(i=>string.Equals(i.Name,"Rare Candy",StringComparison.OrdinalIgnoreCase)||string.Equals(i.Name,"Caramelo Raro",StringComparison.OrdinalIgnoreCase))?.Id??50;
            if(item<1||item>=_items.Count||_itemHash is null)throw new InvalidDataException("Falta el objeto Caramelo Raro o su GARC.");
            byte[] original=GarcTransaction.ReadUnchanged(_itemPath,_itemHash);byte[][] records=new GARC.MemGARC(original).Files;
            if(item>=records.Length||records[item].Length<System.Runtime.InteropServices.Marshal.SizeOf<Item>())throw new InvalidDataException("Registro de Caramelo Raro incompleto.");
            int price=BitConverter.ToUInt16(records[item],0)*10;
            // BuyPrice uses units of ten. Touch only this ushort, preserving opaque item fields.
            records[item]=(byte[])records[item].Clone();BitConverter.GetBytes((ushort)1).CopyTo(records[item],0);
            byte[] output=price==10?original:GarcTransaction.Build(original,_itemRef,records);
            files.Add(new FileChange(_itemPath,original,output));if(price!=10)changes.Add(new(item,"Caramelo Raro","Precio de compra",price.ToString(),"10"));
            foreach(int id in ids)BitConverter.GetBytes((ushort)item).CopyTo(next,_offsets[id-1]+2*(_counts[id-1]-1));
        }
        else
        {
            if(options["SpecialOnly"]==1)ids=ids.Where(id=>id>=11).ToArray();
            int[] pool=ProgressiveLegacy.Randomizer.GetRandomItemList(_project.GameConfiguration).Where(i=>i>0&&i<_items.Count).Distinct().ToArray();
            if(pool.Length==0)throw new InvalidDataException("No hay objetos legales para las tiendas.");
            int cursor=pool.Length;
            foreach(int id in ids)for(int i=0;i<_counts[id-1];i++)
            {
                int at=_offsets[id-1]+i*2,current=BitConverter.ToUInt16(source,at);
                if(Protected.Contains(current)||(options["KeepX"]==1&&current is >=55 and <=62))continue;
                if(cursor==pool.Length){rng.Shuffle(pool);cursor=0;}
                BitConverter.GetBytes((ushort)pool[cursor++]).CopyTo(next,at);
            }
        }
        next=RebuildExisting(source,next);
        foreach(int id in ids)changes.AddRange(EditorValues.Diff(id,Entries[id-1].Name,Read(id,source),Read(id,next)));
        files.Add(new FileChange(_codePath,source,next));var parameters=options.ToDictionary(p=>p.Key,p=>p.Value.ToString());
        parameters["Scope"]=string.Join(",",ids);
        if(candy)parameters["price"]="10";else {parameters["specialOnly"]=(options["SpecialOnly"]==1).ToString();parameters["keepXItems"]=(options["KeepX"]==1).ToString();}
        parameters["ActionId"]=candy?"marts.add-rare-candies":"marts.randomize";
        return new(_owner,candy?"Añadir Caramelos Raros":"Randomizar tiendas",seed,files,changes,parameters);
    }
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otro editor.");
        _=Source();foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        byte[] saved=File.ReadAllBytes(_codePath);_codeHash=SHA256.HashData(saved);
        if(_structure is not null){int offset=_structure.TableOffset(saved);var counts=_structure.Counts(saved);for(int i=0;i<_counts.Length;i++){_counts[i]=counts[i];_offsets[i]=offset;offset+=2*_counts[i];}}
        if(proposal.Files.Any(f=>f.Path==_itemPath))_itemHash=SHA256.HashData(File.ReadAllBytes(_itemPath));
    }
}
