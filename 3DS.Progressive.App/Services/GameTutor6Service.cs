using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;

public sealed class GameTutor6Service : IProjectEditor
{
    private readonly GameProjectSession _project;
    private readonly string _path;
    private byte[] _hash;
    private readonly Gen6TutorLayout _layout;
    private readonly Guid _owner=Guid.NewGuid();
    private readonly IReadOnlyList<MoveChoice> _choices;
    private readonly int _moveCount;
    public string Title=>"Tutores y precios BP · ORAS";
    public IReadOnlyList<RecordEntry> Entries {get;}
    public IReadOnlyList<EditorGroup> RandomOptions=>[new("Opciones tutor ORAS",[
        new("Moves","Randomizar movimientos",1,0,1),new("Hms","Incluir movimientos de campo",0,0,1),
        new("Prices","Precios BP",0,0,2,[new(0,"Conservar precios"),new(1,"Gratis"),new(2,"Precio fijo")]),new("Price","Precio fijo BP",4,0,65535)])];
    public GameTutor6Service(GameProjectSession project)
    {
        if(!project.GameConfiguration.ORAS)throw new ArgumentException("Los tutores BP de Gen6 requieren ORAS.");
        _project=project;_moveCount=Math.Min(project.MoveNames.Count,project.GameConfiguration.Moves.Length);
        _path=new[]{".code.bin","code.bin"}.Select(name=>Path.Combine(project.GameConfiguration.ExeFS,name)).FirstOrDefault(File.Exists)??throw new FileNotFoundException("Falta code.bin.");
        byte[] code=File.ReadAllBytes(_path);_hash=SHA256.HashData(code);_layout=Gen6TutorLayout.Read(code,_moveCount);
        _choices=Enumerable.Range(1,_moveCount-1).Where(id=>id!=622).Select(id=>new MoveChoice(id,project.MoveNames[id])).ToArray();
        Entries=Enumerable.Range(0,60).Select(id=>new RecordEntry(id,$"Tutor {id+1:00} · Sede {Gen6TutorLayout.Compatibility(id).Group+1}")).ToArray();
    }
    private byte[] Code(){byte[] code=GarcTransaction.ReadUnchanged(_path,_hash);_=Gen6TutorLayout.Read(code,_moveCount);return code;}
    private IReadOnlyList<EditorGroup> Read(int id,byte[] code)
    {
        if(id is <0 or >=60)throw new ArgumentOutOfRangeException(nameof(id));
        return [new("Movimiento y coste",[new("Move","Movimiento",BitConverter.ToUInt16(code,_layout.Shop+id*8),1,_moveCount-1,_choices),new("Price","Precio BP",checked((int)BitConverter.ToUInt32(code,_layout.Shop+id*8+4)),0,65535)])];
    }
    public IReadOnlyList<EditorGroup> Read(int id)=>Read(id,Code());
    private EditorProposal Build(IReadOnlyDictionary<int,IReadOnlyDictionary<string,int>> edits,int seed,string action,IReadOnlyDictionary<string,string> parameters)
    {
        byte[] source=Code(),output=(byte[])source.Clone();var changes=new List<BatchChange>();
        foreach(var (id,values) in edits)
        {
            var before=Read(id,source);EditorValues.Validate(before,values);if(values["Move"]==622)throw new ArgumentException("El movimiento 622 está reservado como marcador de sedes.");
            BitConverter.GetBytes((ushort)values["Move"]).CopyTo(output,_layout.Shop+id*8);BitConverter.GetBytes((ushort)values["Move"]).CopyTo(output,_layout.List+2*Gen6TutorLayout.ListSlots[id]);
            BitConverter.GetBytes((uint)values["Price"]).CopyTo(output,_layout.Shop+id*8+4);changes.AddRange(EditorValues.Diff(id,Entries[id].Name,before,Read(id,output)));
        }
        _=Gen6TutorLayout.Read(output,_moveCount);return new(_owner,action,seed,[new FileChange(_path,source,output)],changes,parameters);
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values)=>Apply(Build(new Dictionary<int,IReadOnlyDictionary<string,int>>{{id,values}},0,"Editar tutor ORAS",new Dictionary<string,string>{{"Scope",id.ToString()}}));
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)=>Preview(options,scope,RandomNumberGenerator.GetInt32(int.MaxValue));
    internal EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope,int seed)
    {
        EditorValues.Validate(RandomOptions,options);if(scope.Count==0||scope.Distinct().Count()!=scope.Count||scope.Any(id=>id is <0 or >=60))throw new ArgumentException("Alcance de tutores inválido.");
        int[] hms=[15,19,57,70,127,249,291];int[] banned=[165,621,622];int[] pool=Enumerable.Range(1,_moveCount-1).Where(move=>!banned.Contains(move)&&(options["Hms"]==1||!hms.Contains(move))).ToArray();new Random(seed).Shuffle(pool);
        if(options["Moves"]==1&&pool.Length<scope.Count)throw new InvalidDataException("No hay suficientes movimientos para los tutores seleccionados.");
        var values=new Dictionary<int,IReadOnlyDictionary<string,int>>();byte[] source=Code();
        for(int i=0;i<scope.Count;i++){int id=scope[i];var row=EditorValues.Flatten(Read(id,source));if(options["Moves"]==1)row["Move"]=pool[i];if(options["Prices"]!=0)row["Price"]=options["Prices"]==1?0:options["Price"];values.Add(id,row);}
        var parameters=options.ToDictionary(pair=>pair.Key,pair=>pair.Value.ToString());parameters["ActionId"]="move-tutors.randomize";parameters["Scope"]=scope.Count==60?"All":string.Join(",",scope);
        return Build(values,seed,"Randomizar tutores ORAS",parameters);
    }
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otro editor de tutores.");
        _=Code();foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);_hash=SHA256.HashData(File.ReadAllBytes(_path));
    }
}
