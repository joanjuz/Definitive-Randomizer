using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public abstract class ArchiveProjectEditor : IProjectEditor
{
    protected readonly GameProjectSession Project;
    protected readonly GameConfig Config;
    protected readonly Guid ProposalOwner=Guid.NewGuid();
    private int _lastSeed=-1;
    private readonly Dictionary<TextName,IReadOnlyList<MoveChoice>> _choices=[];
    private IReadOnlyList<MoveChoice>? _speciesChoices;
    private readonly Dictionary<string,(string Path,GARCReference Ref,byte[] Hash)> _archives=[];
    protected ArchiveProjectEditor(GameProjectSession project,params string[] archives)
    {
        Project=project;Config=project.GameConfiguration;
        foreach(string name in archives)
        {
            var reference=Config.GetGARCReference(name);string path=Path.Combine(Config.RomFS,reference.Reference);
            if(!File.Exists(path))throw new FileNotFoundException($"Falta el archivo {name} requerido por este editor.",path);
            byte[] bytes=File.ReadAllBytes(path);_ = new GARC.MemGARC(bytes);
            _archives.Add(name,(path,reference,SHA256.HashData(bytes)));
        }
    }
    public abstract string Title {get;}
    public IReadOnlyList<RecordEntry> Entries {get;protected set;}=[];
    public abstract IReadOnlyList<EditorGroup> RandomOptions {get;}
    protected Dictionary<string,byte[][]> Records()=>_archives.ToDictionary(p=>p.Key,p=>
    {
        byte[] source=GarcTransaction.ReadUnchanged(p.Value.Path,p.Value.Hash);
        return p.Key=="encdata"?new LazyGARCFile(new GARC.LazyGARC(source),p.Value.Ref,p.Value.Path).Files:
            new GARC.MemGARC(source).Files;
    });
    protected abstract IReadOnlyList<EditorGroup> ReadRecord(int id,Dictionary<string,byte[][]> records);
    protected abstract void WriteRecord(int id,IReadOnlyDictionary<string,int> values,Dictionary<string,byte[][]> records);
    protected abstract Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,
        Dictionary<string,byte[][]> records,Random random);
    public IReadOnlyList<EditorGroup> Read(int id)=>ReadRecord(id,Records());
    public void Save(int id,IReadOnlyDictionary<string,int> values)
    {
        var records=Records();var before=ReadRecord(id,records);EditorValues.Validate(before,values);
        WriteRecord(id,values,records);var after=ReadRecord(id,records);
        var changes=EditorValues.Diff(id,Entries.First(e=>e.Id==id).Name,before,after);
        if(changes.Count==0)return;
        Commit(new(ProposalOwner,"Editar "+Title,0,Build(records),changes,new Dictionary<string,string>()));
    }
    public virtual EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions,options);
        int[] ids=scope.Distinct().Order().ToArray();
        if(ids.Length==0||ids.Any(id=>!Entries.Any(e=>e.Id==id)))throw new ArgumentException("El alcance no contiene entradas válidas.");
        int seed;do{seed=RandomNumberGenerator.GetInt32(int.MaxValue);}while(seed==_lastSeed);_lastSeed=seed;var random=new Random(seed);
        var records=Records();var changes=new List<BatchChange>();
        foreach(int id in ids)
        {
            var before=ReadRecord(id,records);var values=Randomize(id,options,records,random);
            EditorValues.Validate(before,values);WriteRecord(id,values,records);
            changes.AddRange(EditorValues.Diff(id,Entries.First(e=>e.Id==id).Name,before,ReadRecord(id,records)));
        }
        var parameters=options.ToDictionary(p=>p.Key,p=>p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        parameters["Seed"]=seed.ToString();parameters["Scope"]=string.Join(",",ids);
        return new(ProposalOwner,"Randomizar "+Title,seed,Build(records),changes,parameters);
    }
    private protected IReadOnlyList<FileChange> Build(Dictionary<string,byte[][]> records)=>_archives.Select(p=>
    {
        byte[] source=GarcTransaction.ReadUnchanged(p.Value.Path,p.Value.Hash);
        if(p.Key!="encdata")return new FileChange(p.Value.Path,source,GarcTransaction.Build(source,p.Value.Ref,records[p.Key],preserveRecordSize:false));
        var lazy=new GARC.LazyGARC(source);
        for(int i=0;i<records[p.Key].Length;i++)
            if(!lazy[i].AsSpan().SequenceEqual(records[p.Key][i]))lazy[i]=records[p.Key][i];
        byte[] output=lazy.Save();
        var verify=new GARC.LazyGARC(output);
        for(int i=0;i<records[p.Key].Length;i++)
            if(!verify[i].AsSpan().SequenceEqual(records[p.Key][i]))throw new InvalidDataException("Falló la verificación de encuentros comprimidos.");
        return new FileChange(p.Value.Path,source,output);
    }).ToArray();
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=ProposalOwner)throw new InvalidOperationException("La propuesta pertenece a otra sesión.");
        foreach(var dependency in proposal.Dependencies)GarcTransaction.ReadUnchanged(dependency.Path,dependency.Fingerprint);
        Commit(proposal);
    }
    private void Commit(EditorProposal proposal)
    {
        foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        Project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        foreach(string name in _archives.Keys.ToArray())
        {var value=_archives[name];_archives[name]=(value.Path,value.Ref,SHA256.HashData(File.ReadAllBytes(value.Path)));}
    }
    protected IReadOnlyList<MoveChoice> Choices(TextName text)
    {if(!_choices.TryGetValue(text,out var choices))_choices[text]=choices=Config.GetText(text).Select((n,i)=>new MoveChoice(i,n)).ToArray();return choices;}
    protected IReadOnlyList<MoveChoice> SpeciesChoices=>_speciesChoices??=Choices(TextName.SpeciesNames).Take(Config.MaxSpeciesID+1).ToArray();
    protected EditorField Field(string key,string label,int value,int min=0,int max=255,IReadOnlyList<MoveChoice>? choices=null)=>new(key,label,value,min,max,choices);
    protected int PickSpecies(int original,Random random,int tolerance)
    {
        int[] pool=Enumerable.Range(1,Math.Min(Config.MaxSpeciesID,Config.Personal.Table.Length-1)).ToArray();
        if(tolerance>0 && original>0 && original<Config.Personal.Table.Length)
        {
            int bst=Config.Personal[original].BST;
            pool=pool.Where(i=>Math.Abs(Config.Personal[i].BST-bst)*100<=bst*tolerance).ToArray();
        }
        if(pool.Length==0)throw new InvalidDataException("No hay especies en el intervalo de BST elegido.");
        return pool[random.Next(pool.Length)];
    }
}
