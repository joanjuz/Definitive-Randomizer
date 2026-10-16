using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Original OPower 65x22-byte table; entry zero and unknown fields preserved.</summary>
public sealed class GameOPowerService : IProjectEditor
{
    internal static readonly byte[] Signature="49461845\0"u8.ToArray();
    private readonly GameProjectSession _project;
    private readonly string _path;
    private byte[] _hash;
    private readonly int _offset;
    private readonly string[] _flavor;
    private readonly Guid _owner=Guid.NewGuid();
    public string Title=>"O-Powers · Gen6";
    public IReadOnlyList<RecordEntry> Entries {get;}
    public IReadOnlyList<EditorGroup> RandomOptions=>[];
    public GameOPowerService(GameProjectSession project)
    {
        if(project.Generation!=6)throw new ArgumentException("O-Powers requiere XY u ORAS.");
        _project=project;_flavor=project.GameConfiguration.GetText(TextName.OPowerFlavor);
        _path=new[]{".code.bin","code.bin"}.Select(n=>Path.Combine(project.GameConfiguration.ExeFS,n)).FirstOrDefault(File.Exists)
            ??throw new FileNotFoundException("Falta code.bin descomprimido.");
        byte[] data=File.ReadAllBytes(_path);_hash=SHA256.HashData(data);_offset=Find(data);
        for(int i=1;i<65;i++)ValidateText(data,i);
        Entries=Enumerable.Range(1,64).Select(i=>new RecordEntry(i,
            _flavor[BitConverter.ToUInt16(data,_offset+22*i+12)]+" · "+_flavor[1+data[_offset+22*i]])).ToArray();
    }
    private static int Find(byte[] data)
    {
        if(data.Length%512!=0||data.Length<0x400000+Signature.Length)throw new InvalidDataException("code.bin comprimido o incompleto.");
        int found=data.AsSpan(0x400000).IndexOf(Signature);
        if(found<0||data.AsSpan(0x400000+found+1).IndexOf(Signature)>=0)throw new InvalidDataException("Firma O-Powers ausente o duplicada.");
        int offset=0x400000+found+Signature.Length;
        while(offset<data.Length&&data[offset]==255)offset++;
        if((long)offset+65*22>data.Length)throw new InvalidDataException("Tabla O-Powers truncada.");
        return offset;
    }
    private void ValidateText(byte[] data,int id)
    {
        int at=_offset+22*id;
        if(1+data[at]>=_flavor.Length||new[]{6,8,10,12}.Any(i=>BitConverter.ToUInt16(data,at+i)>=_flavor.Length))
            throw new InvalidDataException("Referencias de texto O-Powers fuera del catálogo.");
    }
    private byte[] Source()=>GarcTransaction.ReadUnchanged(_path,_hash);
    private int At(int id){if(id<1||id>64)throw new ArgumentOutOfRangeException(nameof(id));return _offset+id*22;}
    public IReadOnlyList<EditorGroup> Read(int id)
    {
        byte[] source=Source();int at=At(id);int usage=source[at+1];
        var choices=new List<MoveChoice>{new(0,"0"),new(2,"2"),new(254,"254")};
        if(!choices.Any(c=>c.Id==usage))choices.Add(new(usage,$"Original desconocido · {usage}"));
        return [new("Costes y progresión",[
            new("PlayerCost","Coste para el jugador",source[at+3],0,255),
            new("OtherCost","Coste para otro jugador",source[at+4],0,255),
            new("Stage","Etapa",source[at+14],0,255),new("LevelUp","Requisito de subida",source[at+15],0,255),
            new("Efficacy","Eficacia",BitConverter.ToUInt16(source,at+18),0,65535),
            new("Duration","Duración · 255 conserva el valor especial vanilla",source[at+20],0,255),
            new("Usability","Usabilidad · valores vanilla",usage,0,255,choices)]),
            new("Referencias originales · se conservan",new[]{6,8,10,12}.Select((n,i)=>{
                int value=BitConverter.ToUInt16(source,at+n);return new EditorField("Text"+n,new[]{"Tipo","Texto corto","Descripción","Nombre"}[i],value,value,value,[new(value,_flavor[value])]);
            }).ToArray(),false)];
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values)
    {
        using var manual=ManualOperationScope.Record(this,id,values);
        var before=Read(id);EditorValues.Validate(before,values);byte[] original=Source(),output=(byte[])original.Clone();int at=At(id);
        int usage=values["Usability"];
        if(usage!=original[at+1]&&usage is not (0 or 2 or 254))throw new ArgumentException("Usabilidad permitida: 0, 2 o 254; un valor desconocido solo puede conservarse.");
        foreach(var pair in new[]{("PlayerCost",3),("OtherCost",4),("Stage",14),("LevelUp",15),("Duration",20),("Usability",1)})output[at+pair.Item2]=(byte)values[pair.Item1];
        BitConverter.GetBytes((ushort)values["Efficacy"]).CopyTo(output,at+18);
        var old=EditorValues.Flatten(before);var changes=values.Where(p=>old[p.Key]!=p.Value)
            .Select(p=>new BatchChange(id,Entries[id-1].Name,before.SelectMany(g=>g.Fields).First(f=>f.Key==p.Key).Label,old[p.Key].ToString(),p.Value.ToString())).ToArray();
        Apply(new(_owner,"Editar O-Power",0,[new FileChange(_path,original,output)],changes,new Dictionary<string,string>{{"ActionId","opower.edit"}}));
    }
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)=>
        throw new NotSupportedException("El editor O-Powers vanilla no tiene randomizador.");
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otro editor.");
        _=Source();foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        _hash=SHA256.HashData(File.ReadAllBytes(_path));
    }
}
