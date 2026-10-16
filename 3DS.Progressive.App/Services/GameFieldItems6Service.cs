using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Progressive Gen6 pickup locations; frozen scripts/code proposals committed together.</summary>
public sealed partial class GameFieldItems6Service : IProjectEditor,IOptionTemplateProjectEditor
{
    private sealed record Slot(int File,int Offset,string Kind);
    internal static readonly byte[] HiddenSignature=Convert.FromHexString("A100A200A300A400A5001400010053004A0084000900");
    private readonly GameProjectSession _project;
    private readonly string _scriptsPath;
    private readonly string? _codePath;
    private readonly GARCReference _reference;
    private readonly Dictionary<int,Slot> _slots=[];
    private byte[] _scriptsHash;
    private byte[]? _codeHash;
    private readonly Guid _owner=Guid.NewGuid();
    private readonly byte[][] _items;
    private readonly string _itemPath;
    private readonly byte[] _itemHash;
    public string Title=>"Objetos de campo · Gen6";
    public IReadOnlyList<RecordEntry> Entries {get;}
    private IReadOnlyList<EditorGroup> BaseRandomOptions=>[new("Objetos recogibles",[
        new("Visible","Incluir visibles",1,0,1),new("Hidden","Incluir ocultos",1,0,1),
        new("Mega","Incluir megapiedras",0,0,1),new("Safe","Conservar cola sospechosa ORAS",1,0,1),
        new("ExcludeMachines","Excluir MT y MO",1,0,1),new("ExcludeKey","Excluir objetos clave",1,0,1),
        new("RandomPool","Usar catálogo de objetos Progressive en lugar de mezclar",0,0,1)])];
    public GameFieldItems6Service(GameProjectSession project)
    {
        _project=project;var game=project.GameConfiguration;
        if(game.Generation!=6)throw new ArgumentException("Se requiere XY u ORAS.");
        _itemPath=Path.Combine(game.RomFS,game.GetGARCFileName("item"));
        byte[] itemSource=File.ReadAllBytes(_itemPath);_itemHash=SHA256.HashData(itemSource);_items=new GARC.MemGARC(itemSource).Files;
        int number=game.ORAS?29:31;var personal=game.GetGARCReference("personal");
        _reference=personal.GetRelativeGARC(number-personal.FileNumber,"scripts");
        _scriptsPath=Path.Combine(game.RomFS,_reference.Reference);
        byte[] source=File.ReadAllBytes(_scriptsPath);_scriptsHash=SHA256.HashData(source);
        var records=new GARC.MemGARC(source).Files;
        AddScript(game.ORAS?39:17,game.ORAS?0xB64:0xB04,12,null,"Visible");
        if(!game.ORAS)AddScript(26,0xB18,12,null,"Hidden");
        else
        {
            AddScript(57,2746,32,27,"Mega");
            _codePath=new[]{Path.Combine(game.ExeFS,".code.bin"),Path.Combine(game.ExeFS,"code.bin")}.FirstOrDefault(File.Exists);
            if(_codePath!=null)
            {
                byte[] code=File.ReadAllBytes(_codePath);_codeHash=SHA256.HashData(code);
                var matches=Enumerable.Range(0,Math.Max(0,code.Length-HiddenSignature.Length+1))
                    .Where(i=>code.AsSpan(i,HiddenSignature.Length).SequenceEqual(HiddenSignature)).Take(2).ToArray();
                if(matches.Length>1)throw new InvalidDataException("Firma de objetos ocultos ORAS duplicada.");
                if(matches.Length==1)
                {
                    int start=matches[0]+HiddenSignature.Length;
                    if((long)start+170*14>code.Length)throw new InvalidDataException("Tabla de objetos ocultos ORAS truncada.");
                    for(int i=0;i<170;i++)Add(-1,start+i*14+2,"Hidden",code);
                }
            }
        }
        byte[]? codeSource=_codePath==null?null:File.ReadAllBytes(_codePath);
        Entries=_slots.Select(p=>new RecordEntry(p.Key,$"{p.Value.Kind} · {(p.Value.File<0?"código":"script "+p.Value.File)} · {Name(Value(p.Value,records,codeSource))}")).ToArray();
        void AddScript(int file,int offset,int stride,int? count,string kind)
        {
            if(file>=records.Length)throw new InvalidDataException("El archivo de scripts no contiene las tablas Gen6 esperadas.");
            byte[] data=Gen6ScriptCodec.Decode(records[file]).Instructions;
            if(offset+2>data.Length)throw new InvalidDataException("Tabla de objetos de script truncada.");
            if(count.HasValue&&(long)offset+(count.Value-1)*stride+2>data.Length)throw new InvalidDataException("Tabla de megapiedras truncada.");
            for(int at=offset,n=0;at+2<=data.Length&&(!count.HasValue||n<count.Value);at+=stride,n++)Add(file,at,kind,data);
        }
        void Add(int file,int offset,string kind,byte[] data)
        {if(BitConverter.ToUInt16(data,offset)>0)_slots.Add(_slots.Count+1,new(file,offset,kind));}
    }
    private string Name(int item)=>item<_project.ItemNames.Count?_project.ItemNames[item]:$"Objeto #{item}";
    private static int Value(Slot slot,byte[][] records,byte[]? code)=>BitConverter.ToUInt16(
        slot.File<0?code??throw new InvalidDataException("Código ausente."):Gen6ScriptCodec.Decode(records[slot.File]).Instructions,slot.Offset);
    private (byte[] Source,byte[][] Records,byte[]? Code) ReadSource()
    {
        _=GarcTransaction.ReadUnchanged(_itemPath,_itemHash);
        byte[] source=GarcTransaction.ReadUnchanged(_scriptsPath,_scriptsHash);
        return(source,new GARC.MemGARC(source).Files,_codePath==null?null:GarcTransaction.ReadUnchanged(_codePath,_codeHash!));
    }
    public IReadOnlyList<EditorGroup> Read(int id)
    {
        var data=ReadSource();int item=Value(_slots[id],data.Records,data.Code);
        var choices=_project.ItemNames.Select((n,i)=>new MoveChoice(i,n)).ToList();
        if(item>=choices.Count)choices.Add(new(item,$"Objeto personalizado #{item}"));
        return[new("Objeto recogible",[new("Item","Objeto",item,0,65535,choices)])];
    }
    private bool Allowed(Slot slot,int item,IReadOnlyDictionary<string,int> options)
    {
        if(options[slot.Kind]==0||options["Safe"]==1&&_project.GameConfiguration.ORAS&&slot.Kind=="Visible"&&slot.Offset>=0x16A4)return false;
        if(options["Safe"]==1&&item>=_project.ItemNames.Count)return false;
        if(options["ExcludeMachines"]==1&&item is >=328 and <=434)return false;
        string name=Name(item).ToLowerInvariant();
        if(options["ExcludeKey"]==1)
        {
            if(new[]{"key","clave","llave","bike","bicycle","bicicleta","ticket","pass","pase","card","tarjeta","letter","carta","devon","scanner","scope","fossil","fósil","fosil","mega bracelet","mega ring","z ring","z-ring"}.Any(name.Contains))return false;
            if(item<_items.Length&&_items[item].Length>0&&new Item(_items[item]).PocketField>=7&&!name.Contains("ite")&&!name.Contains("mega"))return false;
        }
        return item>0;
    }
    private EditorProposal Prepare(Dictionary<int,int> edits,string action,int seed)
    {
        var data=ReadSource();var changes=new List<BatchChange>();var modified=new Dictionary<int,byte[]>();
        byte[]? code=data.Code==null?null:(byte[])data.Code.Clone();
        foreach(var (id,item) in edits)
        {
            if(item is <0 or >65535||!_slots.TryGetValue(id,out var slot))throw new ArgumentException("Objeto o selección inválidos.");
            int old=Value(slot,data.Records,data.Code);if(old==item)continue;
            byte[] target;
            if(slot.File<0)target=code!;
            else if(!modified.TryGetValue(slot.File,out target!))modified[slot.File]=target=Gen6ScriptCodec.Decode(data.Records[slot.File]).Instructions;
            BitConverter.GetBytes((ushort)item).CopyTo(target,slot.Offset);
            changes.Add(new(id,Entries[id-1].Name,"Item",Name(old),Name(item)));
        }
        foreach(var (file,instructions) in modified)data.Records[file]=Gen6ScriptCodec.Rebuild(data.Records[file],instructions);
        var files=new List<FileChange>();
        if(modified.Count>0)files.Add(new(_scriptsPath,data.Source,GarcTransaction.Build(data.Source,_reference,data.Records,preserveRecordSize:false)));
        if(code!=null)files.Add(new(_codePath!,data.Code!,code));
        var verification=modified.Count>0?new GARC.MemGARC(files[0].Proposed()).Files:data.Records;
        foreach(var (id,item) in edits)if(Value(_slots[id],verification,code)!=item)throw new InvalidDataException("No coincide la relectura del objeto.");
        // Freeze both dependencies, even if the selected operation only changes one.
        if(modified.Count==0)files.Add(new(_scriptsPath,data.Source,data.Source));
        byte[] itemSource=GarcTransaction.ReadUnchanged(_itemPath,_itemHash);files.Add(new(_itemPath,itemSource,itemSource));
        return new(_owner,action,seed,files,changes,new Dictionary<string,string>{{"Scope",string.Join(",",edits.Keys)}});
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values){using var manual=ManualOperationScope.Record(this,id,values);EditorValues.Validate(Read(id),values);Apply(Prepare(new(){{id,values["Item"]}},"Editar objeto de campo",0));}
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope) => Preview(options,scope,RandomNumberGenerator.GetInt32(int.MaxValue));
    internal EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope,int seed)
    {
        EditorValues.Validate(RandomOptions,options);var source=ReadSource();
        if(scope.Any(id=>!_slots.ContainsKey(id)))throw new ArgumentException("Selección inválida.");
        var ids=scope.Distinct().Where(id=>Allowed(_slots[id],Value(_slots[id],source.Records,source.Code),options)).ToArray();
        if(ids.Length<2)throw new ArgumentException("Seleccioná al menos dos objetos elegibles.");
        var random=new Random(seed);
        int[] values=ids.Select(id=>Value(_slots[id],source.Records,source.Code)).ToArray();
        if(_itemTemplate.AllowedItems.Length>0)
        {
            var poolOptions=options.ToDictionary(p=>p.Key,p=>p.Value);poolOptions["Visible"]=1;
            var pool=_itemTemplate.AllowedItems.Where(item=>Allowed(new(0,0,"Visible"),item,poolOptions)).ToArray();
            if(pool.Length==0)throw new InvalidDataException("La plantilla y los filtros dejan el catálogo vacío.");
            for(int i=0;i<values.Length;i++)values[i]=pool[random.Next(pool.Length)];
        }
        else if(options["RandomPool"]==1)
        {
            var poolOptions=options.ToDictionary(p=>p.Key,p=>p.Value);poolOptions["Visible"]=1;
            var pool=ProgressiveLegacy.Randomizer.GetRandomItemList(_project.GameConfiguration)
                .Where(item=>_itemTemplate.Allows(item)&&item<_project.ItemNames.Count&&Allowed(new(0,0,"Visible"),item,poolOptions)).ToArray();
            if(pool.Length==0)throw new InvalidDataException("Catálogo vacío.");
            for(int i=0;i<values.Length;i++)values[i]=pool[random.Next(pool.Length)];
        }
        else
        {
            var eligible=values.Where(_itemTemplate.Allows).ToArray();
            if(eligible.Length==0)throw new InvalidDataException("Todos los objetos elegibles están prohibidos por la plantilla.");
            for(int i=0;i<values.Length;i++)if(!_itemTemplate.Allows(values[i]))values[i]=eligible[random.Next(eligible.Length)];
            for(int i=values.Length-1;i>0;i--){int j=random.Next(i+1);(values[i],values[j])=(values[j],values[i]);}
        }
        return Prepare(ids.Select((id,i)=>(id,values[i])).ToDictionary(p=>p.id,p=>p.Item2),"Randomizar objetos de campo",seed);
    }
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otra sesión.");
        _=ReadSource();foreach(var f in proposal.Files)GarcTransaction.ReadUnchanged(f.Path,f.Fingerprint);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        _scriptsHash=SHA256.HashData(File.ReadAllBytes(_scriptsPath));if(_codePath!=null)_codeHash=SHA256.HashData(File.ReadAllBytes(_codePath));
    }
    public string ExportCsv()
    {
        var data=ReadSource();var rows=new List<string>{"ID,Source,Script,Offset,ItemID,Item"};
        foreach(var (id,slot) in _slots)
        {
            int item=Value(slot,data.Records,data.Code);
            rows.Add($"{id},{slot.Kind},{slot.File},{slot.Offset},{item},\"{Name(item).Replace("\"","\"\"")}\"");
        }
        return string.Join(Environment.NewLine,rows);
    }
}
