using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// USUM/SM Field Items based on the original Progressive EI / EB detection.
/// Only touched area files are serialized; a frozen preview always precedes bulk edits.
/// </summary>
public sealed class GameFieldItemsService : IProjectEditor
{
    private sealed record FieldSlot(int Area,int SubFile,int Offset,string Source,int OriginalItem);
    private readonly GameProjectSession _project;
    private readonly GameConfig _config;
    private readonly string _path;
    private readonly Guid _owner=Guid.NewGuid();
    private readonly Dictionary<int,FieldSlot> _locations=[];
    private byte[] _sha;
    public string Title=>"Field Items";
    public IReadOnlyList<RecordEntry> Entries {get;}
    public IReadOnlyList<EditorGroup> RandomOptions=>[
        new("Shuffle seguro de objetos recogibles",[
            new("IncludeEI","Incluir pickups EI",1,0,1),
            new("IncludeEB","Incluir pickups ocultos EB",1,0,1),
            new("ExcludeMachines","Excluir MT y MO",1,0,1),
            new("ExcludeKey","Excluir objetos clave",1,0,1)
        ])
    ];
    public GameFieldItemsService(GameProjectSession project)
    {
        _project=project;_config=project.GameConfiguration;
        if(_config.Generation!=7)throw new NotSupportedException("Field Items de Gen6 requiere parsear los scripts y code.bin.");
        _path=Path.Combine(_config.RomFS,_config.GetGARCFileName("encdata"));
        _sha=SHA256.HashData(File.ReadAllBytes(_path));
        var lazy=new GARC.LazyGARC(File.ReadAllBytes(_path));
        var entries=new List<RecordEntry>();
        for(int area=0;area<lazy.FileCount/11;area++)
        {
            int file=area*11;
            byte[][] ed;
            try {ed=Mini.UnpackMini(lazy[file],"ED");}
            catch {continue;}
            if(ed is null||ed.Length<=11)continue;
            for(int kind=0;kind<2;kind++)
            {
                string type=kind==0?"EI":"EB";
                byte[][] nested;
                try {nested=Mini.UnpackMini(ed[kind+10],type);}
                catch {continue;}
                if(nested is null)continue;
                for(int sub=0;sub<nested.Length;sub++)
                {
                    byte[] data=nested[sub];if(data.Length==0)continue;
                    int count=data[0];
                    for(int i=0;i<count;i++)
                        for(int slot=0;slot<(kind==0?1:7);slot++)
                        {
                            int offset=kind==0?(i*64)+52:4+i*68+54+slot*2;
                            if(offset+2>data.Length)continue;
                            int item=BitConverter.ToUInt16(data,offset);
                            if(item<=0)continue;
                            int id=_locations.Count+1;
                            _locations.Add(id,new(area,sub,offset,type,item));
                            string name=GetItemName(item);
                            entries.Add(new RecordEntry(id,$"Área {area:000} · {type} · {name}"));
                        }
                }
            }
        }
        Entries=entries;
    }
    private string GetItemName(int item)=>
        item>=0&&item<_project.ItemNames.Count&&!string.IsNullOrWhiteSpace(_project.ItemNames[item])
            ?_project.ItemNames[item]:$"Objeto #{item}";
    private byte[] Original()=>GarcTransaction.ReadUnchanged(_path,_sha);
    private int ReadValue(int id,GARC.LazyGARC lazy)
    {
        var slot=_locations[id];
        var ed=Mini.UnpackMini(lazy[slot.Area*11],"ED");
        var nested=Mini.UnpackMini(ed[slot.Source=="EI"?10:11],slot.Source);
        return BitConverter.ToUInt16(nested[slot.SubFile],slot.Offset);
    }
    public IReadOnlyList<EditorGroup> Read(int id)
    {
        if(!_locations.ContainsKey(id))throw new ArgumentOutOfRangeException(nameof(id));
        int value=ReadValue(id,new GARC.LazyGARC(Original()));
        var options=_project.ItemNames.Select((name,index)=>new MoveChoice(index,
            string.IsNullOrWhiteSpace(name)?$"Objeto {index:000}":name)).ToArray();
        return [new("Recogible de la zona",[
            new EditorField("Item","Objeto",value,0,Math.Min(65535,options.Length-1),options)
        ])];
    }
    private void Change(GARC.LazyGARC lazy,int id,int item)
    {
        var slot=_locations[id];
        int file=slot.Area*11;
        var ed=Mini.UnpackMini(lazy[file],"ED");
        int part=slot.Source=="EI"?10:11;
        var nested=Mini.UnpackMini(ed[part],slot.Source);
        BitConverter.GetBytes((ushort)item).CopyTo(nested[slot.SubFile],slot.Offset);
        ed[part]=Mini.PackMini(nested,slot.Source);
        lazy[file]=Mini.PackMini(ed,"ED");
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values)
    {
        EditorValues.Validate(Read(id),values);
        byte[] source=Original();var lazy=new GARC.LazyGARC(source);
        int before=ReadValue(id,lazy),after=values["Item"];
        if(before==after)return;
        Change(lazy,id,after);
        byte[] proposed=lazy.Save();
        var verify=new GARC.LazyGARC(proposed);
        if(ReadValue(id,verify)!=after)throw new InvalidDataException("La relectura del recogible no coincide.");
        _project.History.Commit([new FileChange(_path,source,proposed)],Title,"Editar Field Item",
            [new BatchChange(id,Entries[id-1].Name,"Objeto",GetItemName(before),GetItemName(after))]);
        _sha=SHA256.HashData(proposed);
    }
    private bool Eligible(int id,IReadOnlyDictionary<string,int> settings)
    {
        var s=_locations[id];
        if(s.Source=="EI"&&settings["IncludeEI"]==0||s.Source=="EB"&&settings["IncludeEB"]==0)return false;
        int item=s.OriginalItem;string name=GetItemName(item).ToLowerInvariant();
        if(settings["ExcludeMachines"]==1&&(item is >=328 and <=434||
            name.StartsWith("tm")||name.StartsWith("mt")||name.StartsWith("mo")||name.StartsWith("hm")))return false;
        if(settings["ExcludeKey"]==1)
        {
            string[] words=["ticket","key","llave","pase","tarjeta","fósil","fosil","bicicleta","scanner","card"];
            if(words.Any(name.Contains))return false;
        }
        return true;
    }
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions,options);
        if(scope.Any(id=>!_locations.ContainsKey(id)))throw new ArgumentException("La selección contiene Field Items inválidos.");
        var ids=scope.Distinct().Where(id=>Eligible(id,options)).ToArray();
        if(ids.Length<2)throw new ArgumentException("Seleccioná al menos dos Field Items seguros para mezclar.");
        byte[] source=Original();var lazy=new GARC.LazyGARC(source);
        int[] original=ids.Select(id=>ReadValue(id,lazy)).ToArray();
        int[] shuffled=(int[])original.Clone();
        int seed=RandomNumberGenerator.GetInt32(int.MaxValue);var random=new Random(seed);
        for(int i=shuffled.Length-1;i>0;i--){int j=random.Next(i+1);(shuffled[i],shuffled[j])=(shuffled[j],shuffled[i]);}
        var changes=new List<BatchChange>();
        for(int i=0;i<ids.Length;i++)
        {
            if(original[i]==shuffled[i])continue;
            Change(lazy,ids[i],shuffled[i]);
            changes.Add(new BatchChange(ids[i],Entries[ids[i]-1].Name,"Objeto",
                GetItemName(original[i]),GetItemName(shuffled[i])));
        }
        byte[] output=lazy.Save();
        return new EditorProposal(_owner,"Randomizar Field Items",seed,
            [new FileChange(_path,source,output)],changes,
            new Dictionary<string,string> {["Scope"]=string.Join(",",ids),["Seed"]=seed.ToString()});
    }
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otra sesión.");
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        _sha=SHA256.HashData(File.ReadAllBytes(_path));
    }
    public string ExportCsv()
    {
        var lines=new List<string>{"ID,Area,Source,SubFile,Offset,ItemID,Item"};
        foreach(var (id,slot) in _locations)
        {
            string item="\"" + GetItemName(slot.OriginalItem).Replace("\"","\"\"") + "\"";
            lines.Add($"{id},{slot.Area},{slot.Source},{slot.SubFile},{slot.Offset},{slot.OriginalItem},{item}");
        }
        return string.Join(Environment.NewLine,lines);
    }
}
