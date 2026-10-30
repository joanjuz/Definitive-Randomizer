using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed class PickupRow
{
    public int Item {get;set;}
    public int[] Rates {get;set;}=new int[10];
    public string Group {get;set;}="";
    public PickupRow Copy()=>new(){Item=Item,Rates=(int[])Rates.Clone(),Group=Group};
}

/// <summary>Vanilla Gen6 code table and Gen7 member zero, preserving all other bytes.</summary>
public sealed class GamePickupService
{
    internal static readonly byte[] Signature=[0x1E,0x28,0x32,0x3C,0x46,0x50,0x5A,0x5E,0x62,0x05,0x0A,0x0F,0x14,0x19,0x1E,0x23,0x28,0x2D,0x32];
    private readonly GameProjectSession _project;
    private readonly string _path;
    private byte[] _hash;
    private readonly Guid _owner=Guid.NewGuid();
    public bool Gen7=>_project.Generation==7;
    public IReadOnlyList<MoveChoice> Items {get;}
    public GamePickupService(GameProjectSession project)
    {
        _project=project;Items=project.ItemNames.Select((n,i)=>new MoveChoice(i,n)).ToArray();
        _path=Gen7?Path.Combine(project.GameConfiguration.RomFS,project.GameConfiguration.GetGARCFileName("pickup")):
            new[]{".code.bin","code.bin"}.Select(n=>Path.Combine(project.GameConfiguration.ExeFS,n)).FirstOrDefault(File.Exists)
            ??throw new FileNotFoundException("Falta code.bin descomprimido.");
        byte[] source=File.ReadAllBytes(_path);_hash=SHA256.HashData(source);_=Decode(source);
    }
    private int Offset(byte[] source)
    {
        if(source.Length%512!=0||source.Length<0x400000+58+Signature.Length)
            throw new InvalidDataException("code.bin incompleto o comprimido para Pickup Gen6.");
        int index=source.AsSpan(0x400000).IndexOf(Signature);
        if(index<58||source.AsSpan(0x400000+index+1).IndexOf(Signature)>=0)
            throw new InvalidDataException("No se encontró una firma Pickup Gen6 única y compatible.");
        return 0x400000+index-58;
    }
    private byte[] Member(byte[] source)=>Gen7?new GARC.LazyGARC(source)[0]:source;
    private List<PickupRow> Decode(byte[] source)
    {
        byte[] table=Member(source);var rows=new List<PickupRow>();
        if(Gen7)
        {
            if(table.Length<4)throw new InvalidDataException("Tabla Pickup truncada.");
            int count=BitConverter.ToInt32(table,0)-1;
            if(count<1||count>4096||4L+12L*count>table.Length)throw new InvalidDataException("Cantidad de filas Pickup incompatible.");
            for(int i=0;i<count;i++)rows.Add(new(){Item=BitConverter.ToUInt16(table,4+i*12),
                Rates=table.AsSpan(6+i*12,10).ToArray().Select(x=>(int)x).ToArray(),Group=$"Fila {i+1:00}"});
        }
        else
        {
            int at=Offset(table);
            for(int i=0;i<29;i++)rows.Add(new(){Item=BitConverter.ToUInt16(table,at+2*i),Group=i<18?$"Común {i+1:00}":$"Raro {i-17:00}"});
        }
        if(rows.Any(r=>r.Item<0||r.Item>=Items.Count||r.Rates.Any(v=>v>100)))
            throw new InvalidDataException("Pickup contiene objetos o probabilidades incompatibles.");
        return rows;
    }
    private byte[] Source()=>GarcTransaction.ReadUnchanged(_path,_hash);
    public List<PickupRow> Read()=>Decode(Source());
    public List<PickupRow> Randomize(IReadOnlyList<PickupRow> draft)=>Randomize(draft,RandomNumberGenerator.GetInt32(int.MaxValue));
    internal List<PickupRow> Randomize(IReadOnlyList<PickupRow> draft,int seed)
    {
        _=Source();var output=draft.Select(r=>r.Copy()).ToList();
        int[] pool=ProgressiveLegacy.Randomizer.GetRandomItemList(_project.GameConfiguration)
            .Where(i=>i>0&&i<Items.Count).Distinct().ToArray();
        if(pool.Length==0)throw new InvalidDataException("No hay objetos legales para Pickup.");
        var random=new Random(seed);int cursor=pool.Length;
        foreach(var row in output)
        {
            if(cursor==pool.Length){random.Shuffle(pool);cursor=0;}
            row.Item=pool[cursor++];
        }
        return output;
    }
    public EditorProposal Preview(IReadOnlyList<PickupRow> draft)
    {
        // Freeze UI values before validation and serialization.
        var rows=draft.Select(r=>r.Copy()).ToArray();byte[] source=Source();var before=Decode(source);
        if((!Gen7&&rows.Length!=29)||rows.Length<1||rows.Length>4096||
            rows.Any(r=>r.Item<0||r.Item>=Items.Count||r.Rates.Length!=10||r.Rates.Any(v=>v<0||v>100)))
            throw new ArgumentException("Filas, objetos o probabilidades Pickup inválidos.");
        if(Gen7)for(int c=0;c<10;c++)if(rows.Sum(r=>r.Rates[c])!=100)
            throw new ArgumentException($"La columna de niveles {c*10+1}–{(c+1)*10} debe sumar 100 %.");
        byte[] table=Member(source),output;
        if(Gen7)
        {
            int oldEnd=4+12*before.Count,newEnd=4+12*rows.Length;
            byte[] next=new byte[newEnd+table.Length-oldEnd];BitConverter.GetBytes(rows.Length+1).CopyTo(next,0);
            table.AsSpan(oldEnd).CopyTo(next.AsSpan(newEnd));
            for(int i=0;i<rows.Length;i++)
            {BitConverter.GetBytes((ushort)rows[i].Item).CopyTo(next,4+i*12);for(int c=0;c<10;c++)next[6+i*12+c]=(byte)rows[i].Rates[c];}
            var lazy=new GARC.LazyGARC(source);
            // Read the original entry before replacing it so LazyGARC retains
            // its LZ11 storage state instead of deriving it from the new bytes.
            _=lazy[0];
            lazy[0]=next;output=lazy.Save();
            if(!new GARC.LazyGARC(output)[0].AsSpan().SequenceEqual(next))throw new IOException("Falló la relectura Pickup.");
        }
        else
        {
            output=(byte[])source.Clone();int at=Offset(source);
            for(int i=0;i<29;i++)BitConverter.GetBytes((ushort)rows[i].Item).CopyTo(output,at+i*2);
        }
        var changes=new List<BatchChange>();
        for(int i=0;i<Math.Max(before.Count,rows.Length);i++)
        {
            if(i>=rows.Length||i>=before.Count){changes.Add(new(i,"Pickup","Fila",i<before.Count?before[i].Item.ToString():"—",i<rows.Length?rows[i].Item.ToString():"—"));continue;}
            if(before[i].Item!=rows[i].Item)changes.Add(new(i,"Pickup","Objeto",Items[before[i].Item].Display,Items[rows[i].Item].Display));
            if(Gen7)for(int c=0;c<10;c++)if(before[i].Rates[c]!=rows[i].Rates[c])changes.Add(new(i,"Pickup",$"Niveles {c*10+1}–{(c+1)*10}",before[i].Rates[c]+" %",rows[i].Rates[c]+" %"));
        }
        return new(_owner,"Editar Pickup",0,[new FileChange(_path,source,output)],changes,new Dictionary<string,string>{{"ActionId","pickup.edit"}});
    }
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otro editor.");
        foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit(proposal.Files,"Pickup",proposal.Action,proposal.Changes,proposal.Parameters);
        _hash=SHA256.HashData(File.ReadAllBytes(_path));
    }
}
