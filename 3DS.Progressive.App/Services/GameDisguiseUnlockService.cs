using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;
using pk3DS.Core.Modding.Research;
namespace ThreeDS.Progressive.Services;
/// <summary>«Habilidades especiales · Disfraz en otras especies» (Ultra Sun / Ultra Moon). Writes Battle.cro and static.crr.</summary>
public sealed class GameDisguiseUnlockService:IProjectEditor
{
    internal const string Entry="Permitir Disfraz en otras especies";
    private readonly GameProjectSession _project;private readonly string _battlePath,_crrPath;private readonly string? _ncchPath;
    private byte[] _battleHash,_crrHash;private readonly Guid _owner=Guid.NewGuid();
    public string Title=>"Habilidades especiales · Disfraz";
    public IReadOnlyList<RecordEntry> Entries=>[new(1,Entry)];
    public IReadOnlyList<EditorGroup> RandomOptions=>[new("Disfraz",[new("Enable","1 = permitir · 0 = solo Mimikyu. Un Pokémon con Disfraz anula el primer golpe una vez por combate, sin cambiar de forma; no asigna la habilidad a nadie",1,0,1)])];
    public GameDisguiseUnlockService(GameProjectSession project)
    {
        if(!project.GameConfiguration.USUM)throw new NotSupportedException("Disfraz en otras especies solo está disponible para Ultra Sol y Ultra Luna.");
        _project=project;var config=project.GameConfiguration;
        _battlePath=GamePlayerLevelCapService.Find(config.RomFS,"Battle.cro",true);
        _crrPath=Path.Combine(config.RomFS,".crr","static.crr");
        _ncchPath=new[]{"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"}.Select(n=>Path.Combine(project.ProjectRoot,n)).FirstOrDefault(File.Exists);
        foreach(var path in new[]{_battlePath,_crrPath})if(!File.Exists(path))throw new FileNotFoundException("Falta "+Path.GetFileName(path)+".");
        _battleHash=SHA256.HashData(File.ReadAllBytes(_battlePath));_crrHash=SHA256.HashData(File.ReadAllBytes(_crrPath));
        var report=Inspect(File.ReadAllBytes(_battlePath),File.ReadAllBytes(_crrPath)).Report;
        if(!report.CanApply)throw new InvalidDataException("No se modificó ningún archivo. "+GamePlayerLevelCapService.Describe(report));
    }
    private ulong? TitleId()=>RareCandyMultiUsePatch.ReadTitleId(_ncchPath is null?null:File.ReadAllBytes(_ncchPath));
    private MultitypeUnlockInspection Inspect(byte[] battle,byte[] crr)=>DisguiseUnlockPatch.Inspect(TitleId(),battle,crr);
    public IReadOnlyList<EditorGroup> Read(int id)
    {
        if(id!=1)throw new ArgumentException("Registro inválido.");
        var state=Inspect(GarcTransaction.ReadUnchanged(_battlePath,_battleHash),GarcTransaction.ReadUnchanged(_crrPath,_crrHash)).State;
        return[new("Estado",[new("Enable","Disfraz permitido en otras especies",state==MultitypeUnlockState.Installed?1:0,0,1)])];
    }
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions,options);
        if(!scope.Contains(1)||scope.Any(i=>i!=1))throw new ArgumentException("Selecciona «"+Entry+"».");
        bool enable=options["Enable"]==1;
        byte[] battle=GarcTransaction.ReadUnchanged(_battlePath,_battleHash),crr=GarcTransaction.ReadUnchanged(_crrPath,_crrHash);
        var parameters=new Dictionary<string,string>{{"ActionId","usum.disguise-unlock"},{"Enable",enable?"1":"0"}};
        string action=enable?"Permitir Disfraz en otras especies":"Restaurar Disfraz solo para Mimikyu";
        if(Inspect(battle,crr).State==(enable?MultitypeUnlockState.Installed:MultitypeUnlockState.Original))return new(_owner,action,0,[],[],parameters);
        var output=enable?DisguiseUnlockPatch.Install(TitleId(),battle,crr):DisguiseUnlockPatch.Remove(TitleId(),battle,crr);
        var dependencies=_ncchPath is null?[]:new[]{(_ncchPath,SHA256.HashData(File.ReadAllBytes(_ncchPath)))};
        return new(_owner,action,0,[new FileChange(_battlePath,battle,output.Battle),new FileChange(_crrPath,crr,output.Crr)],
            [new(1,"Battle.cro · 4 comprobaciones de Disfraz","Especies con Disfraz",enable?"Solo Mimikyu":"Cualquier especie",enable?"Cualquier especie":"Solo Mimikyu"),
             new(2,"static.crr","Entrada de Battle.cro","Hash anterior","Hash nuevo (lista ordenada)")],parameters,dependencies);
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values){using var manual=ManualOperationScope.Record(this,id,values);Apply(Preview(values,[id]));}
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("Propuesta de otra sesión.");
        foreach(var (path,fingerprint) in proposal.Dependencies)GarcTransaction.ReadUnchanged(path,fingerprint);
        if(proposal.Files.Count==0)return;
        foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        _battleHash=SHA256.HashData(File.ReadAllBytes(_battlePath));_crrHash=SHA256.HashData(File.ReadAllBytes(_crrPath));
        var expected=proposal.Parameters["Enable"]=="1"?MultitypeUnlockState.Installed:MultitypeUnlockState.Original;
        if(Inspect(File.ReadAllBytes(_battlePath),File.ReadAllBytes(_crrPath)).State!=expected)throw new InvalidDataException("La relectura de Disfraz no coincide.");
    }
}
