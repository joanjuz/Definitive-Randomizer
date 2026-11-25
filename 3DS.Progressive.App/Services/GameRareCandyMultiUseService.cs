using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;
using pk3DS.Core.Modding.Research;
namespace ThreeDS.Progressive.Services;
/// <summary>
/// Installs or removes «Caramelos Raros personalizados» in Omega Ruby: using a Rare Candy from the Bag asks how many to
/// use. code.bin, DllBag.cro and static.crr are written together through the project history; the NCCH header and the
/// ExHeader are only read to identify the title and its memory layout.
/// </summary>
public sealed class GameRareCandyMultiUseService:IProjectEditor
{
    internal const string Entry="Usar varios Caramelos Raros a la vez desde la Bolsa";
    internal const string HowItWorks="En el juego: Bolsa → Caramelo Raro → Usar. Si tienes más de uno, el juego pregunta cuántos quieres usar (como máximo, los que tengas). "+
        "Se aplican uno a uno; al llegar al nivel 100, o cuando el juego no permita usar más, se detiene y no gasta los restantes. También aprende los movimientos de los niveles que se salta.";
    private readonly GameProjectSession _project;
    private readonly string _codePath,_bagPath,_crrPath;
    private readonly string? _ncchPath,_exheaderPath;
    private byte[] _codeHash,_bagHash,_crrHash;
    private readonly Guid _owner=Guid.NewGuid();
    private readonly RareCandyMultiUseProfile _profile;
    public string Title=>"Caramelos Raros personalizados · Rubí Omega";
    public IReadOnlyList<RecordEntry> Entries=>[new(1,Entry)];
    public IReadOnlyList<EditorGroup> RandomOptions=>[new("Caramelos Raros personalizados",[new("Enable","1 = instalar · 0 = retirar. La cantidad se elige en el juego al usar el Caramelo Raro",1,0,1)])];

    public GameRareCandyMultiUseService(GameProjectSession project):this(project,RareCandyMultiUsePatch.OmegaRuby){}
    internal GameRareCandyMultiUseService(GameProjectSession project,RareCandyMultiUseProfile profile)
    {
        _profile=profile;
        if(!project.GameConfiguration.ORAS)throw new NotSupportedException("Caramelos Raros personalizados solo está disponible para Rubí Omega.");
        _project=project;var config=project.GameConfiguration;
        _codePath=GamePlayerLevelCapService.Find(config.ExeFS,"code.bin",false);
        _bagPath=GamePlayerLevelCapService.Find(config.RomFS,"DllBag.cro",true);
        _crrPath=Path.Combine(config.RomFS,".crr","static.crr");
        _ncchPath=FirstExisting(project.ProjectRoot,"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin");
        _exheaderPath=FirstExisting(project.ProjectRoot,"exheader.bin","DecryptedExHeader.bin");
        foreach(var path in new[]{_codePath,_bagPath,_crrPath})if(!File.Exists(path))throw new FileNotFoundException("Falta "+Path.GetFileName(path)+".");
        _codeHash=SHA256.HashData(File.ReadAllBytes(_codePath));_bagHash=SHA256.HashData(File.ReadAllBytes(_bagPath));_crrHash=SHA256.HashData(File.ReadAllBytes(_crrPath));
        var inspection=Inspect();
        if(!inspection.Report.CanApply)throw new InvalidDataException("No se modificó ningún archivo. "+GamePlayerLevelCapService.Describe(inspection.Report));
    }

    private static string? FirstExisting(string root,params string[] names)=>names.Select(name=>Path.Combine(root,name)).FirstOrDefault(File.Exists);
    private static byte[]? ReadOptional(string? path)=>path is not null&&File.Exists(path)?File.ReadAllBytes(path):null;

    /// <summary>Supported only for an Omega Ruby project; the title comes from the NCCH header, which OR and AS do not share.</summary>
    internal static bool Supports(GameProjectSession project)
    {
        if(!project.GameConfiguration.ORAS)return false;
        try{return RareCandyMultiUsePatch.ReadTitleId(ReadOptional(FirstExisting(project.ProjectRoot,"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin")))==RareCandyMultiUsePatch.OmegaRubyTitleId;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){return false;}
    }

    internal enum CardState{Unavailable,NotInstalled,Installed}
    /// <summary>Read-only state for the «Mecánicas y CRO» card. Nothing is written.</summary>
    internal static (CardState State,string Text) InspectProject(GameProjectSession project)
    {
        if(!project.GameConfiguration.ORAS)return(CardState.Unavailable,"Solo para Rubí Omega.");
        try
        {
            var config=project.GameConfiguration;
            var result=RareCandyMultiUsePatch.Inspect(
                RareCandyMultiUsePatch.ReadTitleId(ReadOptional(FirstExisting(project.ProjectRoot,"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"))),
                ReadOptional(FirstExisting(project.ProjectRoot,"exheader.bin","DecryptedExHeader.bin")),
                ReadOptional(GamePlayerLevelCapService.Find(config.ExeFS,"code.bin",false)),
                ReadOptional(GamePlayerLevelCapService.Find(config.RomFS,"DllBag.cro",true)),
                ReadOptional(Path.Combine(config.RomFS,".crr","static.crr")));
            return result.State switch
            {
                RareCandyMultiUseState.Installed=>(CardState.Installed,"Estado: instalado."),
                RareCandyMultiUseState.Original=>(CardState.NotInstalled,"Estado: sin instalar."),
                _=>(CardState.Unavailable,"No disponible en este proyecto: "+result.Report.Summary),
            };
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {return(CardState.Unavailable,"No se pudo leer el estado: "+e.Message);}
    }

    private RareCandyMultiUseInspection Inspect()=>RareCandyMultiUsePatch.Inspect(
        RareCandyMultiUsePatch.ReadTitleId(ReadOptional(_ncchPath)),ReadOptional(_exheaderPath),
        ReadOptional(_codePath),ReadOptional(_bagPath),ReadOptional(_crrPath),_profile);

    public IReadOnlyList<EditorGroup> Read(int id)
    {
        if(id!=1)throw new ArgumentException("Registro inválido.");
        GarcTransaction.ReadUnchanged(_codePath,_codeHash);GarcTransaction.ReadUnchanged(_bagPath,_bagHash);GarcTransaction.ReadUnchanged(_crrPath,_crrHash);
        return[new("Estado",[new("Enable","Caramelos Raros personalizados instalados",Inspect().State==RareCandyMultiUseState.Installed?1:0,0,1)])];
    }

    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions,options);
        if(!scope.Contains(1)||scope.Any(i=>i!=1))throw new ArgumentException("Selecciona «"+Entry+"».");
        bool enable=options["Enable"]==1;
        byte[] code=GarcTransaction.ReadUnchanged(_codePath,_codeHash),bag=GarcTransaction.ReadUnchanged(_bagPath,_bagHash),crr=GarcTransaction.ReadUnchanged(_crrPath,_crrHash);
        ulong? title=RareCandyMultiUsePatch.ReadTitleId(ReadOptional(_ncchPath));
        byte[] exheader=ReadOptional(_exheaderPath)??throw new FileNotFoundException("Falta el ExHeader del proyecto.");
        var inspection=RareCandyMultiUsePatch.Inspect(title,exheader,code,bag,crr,_profile);
        var parameters=new Dictionary<string,string>{{"ActionId","oras.rare-candy-multiuse"},{"Profile",_profile.Id},{"Enable",enable?"1":"0"}};
        string action=enable?"Instalar Caramelos Raros personalizados":"Retirar Caramelos Raros personalizados";
        if(inspection.State==(enable?RareCandyMultiUseState.Installed:RareCandyMultiUseState.Original))
            return new(_owner,action,0,[],[],parameters);
        var output=enable?RareCandyMultiUsePatch.Install(title,exheader,code,bag,crr,_profile):RareCandyMultiUsePatch.Remove(title,exheader,code,bag,crr,_profile);
        var changes=inspection.Report.Resources.Where(r=>r.Offset is not null&&r.Resource!="memoria · estado de 12 bytes")
            .Select((r,i)=>new BatchChange(i+1,r.Resource,r.Role,enable?"Original":"Instalado",enable?"Instalado":"Original")).ToList();
        changes.Add(new(changes.Count+1,"DllBag.cro · hashes","SHA-256 del segmento de código","Recalculado","Recalculado"));
        changes.Add(new(changes.Count+1,"static.crr","Entrada de DllBag.cro","Hash anterior","Hash nuevo (lista ordenada)"));
        var dependencies=new[]{_ncchPath,_exheaderPath}.OfType<string>().Select(p=>(p,SHA256.HashData(File.ReadAllBytes(p))));
        return new(_owner,action,0,[new FileChange(_codePath,code,output.Code),new FileChange(_bagPath,bag,output.Bag),new FileChange(_crrPath,crr,output.Crr)],
            changes,parameters,dependencies);
    }

    public void Save(int id,IReadOnlyDictionary<string,int> values){using var manual=ManualOperationScope.Record(this,id,values);Apply(Preview(values,[id]));}

    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("Propuesta de otra sesión.");
        foreach(var (path,fingerprint) in proposal.Dependencies)GarcTransaction.ReadUnchanged(path,fingerprint);
        if(proposal.Files.Count==0)return;
        foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        _codeHash=SHA256.HashData(File.ReadAllBytes(_codePath));_bagHash=SHA256.HashData(File.ReadAllBytes(_bagPath));_crrHash=SHA256.HashData(File.ReadAllBytes(_crrPath));
        var expected=proposal.Parameters["Enable"]=="1"?RareCandyMultiUseState.Installed:RareCandyMultiUseState.Original;
        var state=Inspect().State;
        if(state!=expected)throw new InvalidDataException("La relectura de Caramelos Raros personalizados no coincide ("+state+").");
    }
}
