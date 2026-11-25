using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;
using pk3DS.Core.Modding.Research;
namespace ThreeDS.Progressive.Services;
/// <summary>«Habilidades especiales · Permitir Multitipo en otras especies» (Ultra Sun / Ultra Moon). Writes only code.bin.</summary>
public sealed class GameMultitypeUnlockService:IProjectEditor
{
    internal const string Entry="Permitir Multitipo en otras especies";
    private readonly GameProjectSession _project;private readonly string _codePath;private readonly string? _ncchPath;
    private readonly IReadOnlyList<MultitypeUnlockProfile>? _profiles;private byte[] _hash;private readonly Guid _owner=Guid.NewGuid();
    public string Title=>"Habilidades especiales · Multitipo";
    public IReadOnlyList<RecordEntry> Entries=>[new(1,Entry)];
    public IReadOnlyList<EditorGroup> RandomOptions=>[new("Multitipo",[new("Enable","1 = permitir · 0 = solo Arceus. Un Pokémon con Multitipo toma el tipo de su Tabla (Normal sin Tabla); no asigna la habilidad a nadie",1,0,1)])];
    public GameMultitypeUnlockService(GameProjectSession project):this(project,null){}
    internal GameMultitypeUnlockService(GameProjectSession project,IReadOnlyList<MultitypeUnlockProfile>? profiles)
    {
        if(!project.GameConfiguration.USUM)throw new NotSupportedException("Multitipo para otras especies solo está disponible para Ultra Sol y Ultra Luna.");
        _project=project;_profiles=profiles;
        _codePath=GamePlayerLevelCapService.Find(project.GameConfiguration.ExeFS,"code.bin",false);
        _ncchPath=new[]{"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"}.Select(n=>Path.Combine(project.ProjectRoot,n)).FirstOrDefault(File.Exists);
        if(!File.Exists(_codePath))throw new FileNotFoundException("Falta code.bin.");
        _hash=SHA256.HashData(File.ReadAllBytes(_codePath));
        var report=Inspect(File.ReadAllBytes(_codePath)).Report;
        if(!report.CanApply)throw new InvalidDataException("No se modificó ningún archivo. "+GamePlayerLevelCapService.Describe(report));
    }
    private ulong? Title_()=>RareCandyMultiUsePatch.ReadTitleId(_ncchPath is null?null:File.ReadAllBytes(_ncchPath));
    private MultitypeUnlockInspection Inspect(byte[] code)=>MultitypeUnlockPatch.Inspect(Title_(),code,_profiles);
    public IReadOnlyList<EditorGroup> Read(int id)
    {
        if(id!=1)throw new ArgumentException("Registro inválido.");
        var state=Inspect(GarcTransaction.ReadUnchanged(_codePath,_hash)).State;
        return[new("Estado",[new("Enable","Multitipo permitido en otras especies",state==MultitypeUnlockState.Installed?1:0,0,1)])];
    }
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions,options);
        if(!scope.Contains(1)||scope.Any(i=>i!=1))throw new ArgumentException("Selecciona «"+Entry+"».");
        bool enable=options["Enable"]==1;
        byte[] code=GarcTransaction.ReadUnchanged(_codePath,_hash);
        var parameters=new Dictionary<string,string>{{"ActionId","usum.multitype-unlock"},{"Enable",enable?"1":"0"}};
        string action=enable?"Permitir Multitipo en otras especies":"Restaurar Multitipo solo para Arceus";
        if(Inspect(code).State==(enable?MultitypeUnlockState.Installed:MultitypeUnlockState.Original))return new(_owner,action,0,[],[],parameters);
        byte[] output=MultitypeUnlockPatch.Apply(Title_(),code,enable,_profiles);
        var dependencies=_ncchPath is null?[]:new[]{(_ncchPath,SHA256.HashData(File.ReadAllBytes(_ncchPath)))};
        return new(_owner,action,0,[new FileChange(_codePath,code,output)],
            [new(1,"code.bin · tipo 1 y tipo 2","Condición de Multitipo",enable?"Solo Arceus":"Cualquier especie",enable?"Cualquier especie":"Solo Arceus")],parameters,dependencies);
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values){using var manual=ManualOperationScope.Record(this,id,values);Apply(Preview(values,[id]));}
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("Propuesta de otra sesión.");
        foreach(var (path,fingerprint) in proposal.Dependencies)GarcTransaction.ReadUnchanged(path,fingerprint);
        if(proposal.Files.Count==0)return;
        GarcTransaction.ReadUnchanged(_codePath,_hash);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        _hash=SHA256.HashData(File.ReadAllBytes(_codePath));
        var expected=proposal.Parameters["Enable"]=="1"?MultitypeUnlockState.Installed:MultitypeUnlockState.Original;
        if(Inspect(File.ReadAllBytes(_codePath)).State!=expected)throw new InvalidDataException("La relectura de Multitipo no coincide.");
    }
}
