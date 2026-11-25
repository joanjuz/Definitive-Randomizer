using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;
using pk3DS.Core.Modding.Research;
namespace ThreeDS.Progressive.Services;
/// <summary>
/// Installs or removes «Caramelos Raros personalizados»: using a Rare Candy from the Bag asks how many to use.
/// Omega Ruby writes code.bin, DllBag.cro and static.crr; Ultra Sun / Ultra Moon write Bag.cro and static.crr. The files
/// of each game are committed together through the project history; the NCCH header (and in USUM FieldBag.cro, in OR the
/// ExHeader) are only read.
/// </summary>
public sealed class GameRareCandyMultiUseService:IProjectEditor
{
    internal const string Entry="Usar varios Caramelos Raros a la vez desde la Bolsa";
    internal const string HowItWorks="En el juego: Bolsa → Caramelo Raro → Usar. Si tienes más de uno, el juego pregunta cuántos quieres usar (como máximo, los que tengas). "+
        "Se aplican uno a uno; al llegar al nivel 100, o cuando el juego no permita usar más, se detiene y no gasta los restantes. También aprende los movimientos de los niveles que se salta.";
    internal const string HowItWorksUsum="En el juego: Bolsa → Caramelo Raro → Usar. Si tienes más de uno, aparece la ventana de cantidad (la misma de la tienda). "+
        "Cada caramelo sigue el proceso normal del juego: ventana de nivel, movimientos nuevos (se pueden rechazar) y evolución. Se detiene al evolucionar, al llegar al nivel 100, si el nivel no sube o si no quedan caramelos, sin gastar los restantes.";
    private readonly GameProjectSession _project;
    private readonly bool _usum;
    private readonly string[] _written;
    private readonly string _bagPath,_crrPath;
    private readonly string? _codePath,_fieldBagPath,_ncchPath,_exheaderPath;
    private readonly Dictionary<string,byte[]> _hashes=new(StringComparer.OrdinalIgnoreCase);
    private readonly Guid _owner=Guid.NewGuid();
    private readonly RareCandyMultiUseProfile _profile;
    public string Title=>_usum?"Caramelos Raros personalizados · Ultra Sol / Ultra Luna":"Caramelos Raros personalizados · Rubí Omega";
    public IReadOnlyList<RecordEntry> Entries=>[new(1,Entry)];
    public IReadOnlyList<EditorGroup> RandomOptions=>[new("Caramelos Raros personalizados",[new("Enable","1 = instalar · 0 = retirar. La cantidad se elige en el juego al usar el Caramelo Raro",1,0,1)])];

    public GameRareCandyMultiUseService(GameProjectSession project):this(project,RareCandyMultiUsePatch.OmegaRuby){}
    internal GameRareCandyMultiUseService(GameProjectSession project,RareCandyMultiUseProfile profile)
    {
        _profile=profile;
        var config=project.GameConfiguration;
        if(!config.ORAS&&!config.USUM)throw new NotSupportedException("Caramelos Raros personalizados solo está disponible para Rubí Omega, Ultra Sol y Ultra Luna.");
        _project=project;_usum=config.USUM;
        _crrPath=Path.Combine(config.RomFS,".crr","static.crr");
        _ncchPath=Ncch(project);
        if(_usum)
        {
            _bagPath=GamePlayerLevelCapService.Find(config.RomFS,"Bag.cro",true);
            _fieldBagPath=GamePlayerLevelCapService.Find(config.RomFS,"FieldBag.cro",true);
            _written=[_bagPath,_crrPath];
        }
        else
        {
            _codePath=GamePlayerLevelCapService.Find(config.ExeFS,"code.bin",false);
            _bagPath=GamePlayerLevelCapService.Find(config.RomFS,"DllBag.cro",true);
            _exheaderPath=FirstExisting(project.ProjectRoot,"exheader.bin","DecryptedExHeader.bin");
            _written=[_codePath,_bagPath,_crrPath];
        }
        foreach(var path in _written)if(!File.Exists(path))throw new FileNotFoundException("Falta "+Path.GetFileName(path)+".");
        Remember();
        var inspection=Inspect();
        if(!inspection.Report.CanApply)throw new InvalidDataException("No se modificó ningún archivo. "+GamePlayerLevelCapService.Describe(inspection.Report));
    }

    private void Remember(){foreach(var path in _written)_hashes[path]=SHA256.HashData(File.ReadAllBytes(path));}
    private byte[] ReadUnchanged(string path)=>GarcTransaction.ReadUnchanged(path,_hashes[path]);
    private static string? FirstExisting(string root,params string[] names)=>names.Select(name=>Path.Combine(root,name)).FirstOrDefault(File.Exists);
    private static string? Ncch(GameProjectSession project)=>FirstExisting(project.ProjectRoot,"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin");
    private static byte[]? ReadOptional(string? path)=>path is not null&&File.Exists(path)?File.ReadAllBytes(path):null;

    /// <summary>Omega Ruby (NCCH title, which OR and AS do not share) or Ultra Sun / Ultra Moon.</summary>
    internal static bool Supports(GameProjectSession project)
    {
        var config=project.GameConfiguration;
        if(!config.ORAS&&!config.USUM)return false;
        try
        {
            ulong? title=RareCandyMultiUsePatch.ReadTitleId(ReadOptional(Ncch(project)));
            return config.USUM?title is RareCandyUsumPatch.UltraSunTitleId or RareCandyUsumPatch.UltraMoonTitleId:title==RareCandyMultiUsePatch.OmegaRubyTitleId;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){return false;}
    }

    internal enum CardState{Unavailable,NotInstalled,Installed}
    /// <summary>Read-only state for the «Mecánicas y CRO» card. Nothing is written.</summary>
    internal static (CardState State,string Text) InspectProject(GameProjectSession project)
    {
        var config=project.GameConfiguration;
        if(!config.ORAS&&!config.USUM)return(CardState.Unavailable,"Solo para Rubí Omega, Ultra Sol y Ultra Luna.");
        try
        {
            ulong? title=RareCandyMultiUsePatch.ReadTitleId(ReadOptional(Ncch(project)));
            var result=config.USUM
                ?RareCandyUsumPatch.Inspect(title,ReadOptional(GamePlayerLevelCapService.Find(config.RomFS,"Bag.cro",true)),
                    ReadOptional(GamePlayerLevelCapService.Find(config.RomFS,"FieldBag.cro",true)),ReadOptional(Path.Combine(config.RomFS,".crr","static.crr")))
                :RareCandyMultiUsePatch.Inspect(title,ReadOptional(FirstExisting(project.ProjectRoot,"exheader.bin","DecryptedExHeader.bin")),
                    ReadOptional(GamePlayerLevelCapService.Find(config.ExeFS,"code.bin",false)),
                    ReadOptional(GamePlayerLevelCapService.Find(config.RomFS,"DllBag.cro",true)),ReadOptional(Path.Combine(config.RomFS,".crr","static.crr")));
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

    private RareCandyMultiUseInspection Inspect()
    {
        ulong? title=RareCandyMultiUsePatch.ReadTitleId(ReadOptional(_ncchPath));
        return _usum?RareCandyUsumPatch.Inspect(title,ReadOptional(_bagPath),ReadOptional(_fieldBagPath),ReadOptional(_crrPath))
            :RareCandyMultiUsePatch.Inspect(title,ReadOptional(_exheaderPath),ReadOptional(_codePath),ReadOptional(_bagPath),ReadOptional(_crrPath),_profile);
    }

    public IReadOnlyList<EditorGroup> Read(int id)
    {
        if(id!=1)throw new ArgumentException("Registro inválido.");
        foreach(var path in _written)ReadUnchanged(path);
        return[new("Estado",[new("Enable","Caramelos Raros personalizados instalados",Inspect().State==RareCandyMultiUseState.Installed?1:0,0,1)])];
    }

    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions,options);
        if(!scope.Contains(1)||scope.Any(i=>i!=1))throw new ArgumentException("Selecciona «"+Entry+"».");
        bool enable=options["Enable"]==1;
        var current=_written.ToDictionary(p=>p,ReadUnchanged,StringComparer.OrdinalIgnoreCase);
        ulong? title=RareCandyMultiUsePatch.ReadTitleId(ReadOptional(_ncchPath));
        string action=enable?"Instalar Caramelos Raros personalizados":"Retirar Caramelos Raros personalizados";
        RareCandyMultiUseInspection inspection;
        Func<Dictionary<string,byte[]>> build;
        string bagName=Path.GetFileName(_bagPath);
        var dependencies=new List<(string Path,byte[] Fingerprint)>();
        if(_usum)
        {
            byte[] fieldBag=ReadOptional(_fieldBagPath)??throw new FileNotFoundException("Falta FieldBag.cro.");
            dependencies.Add((_fieldBagPath!,SHA256.HashData(fieldBag)));
            inspection=RareCandyUsumPatch.Inspect(title,current[_bagPath],fieldBag,current[_crrPath]);
            build=()=>{var o=enable?RareCandyUsumPatch.Install(title,current[_bagPath],fieldBag,current[_crrPath]):RareCandyUsumPatch.Remove(title,current[_bagPath],fieldBag,current[_crrPath]);
                return new(StringComparer.OrdinalIgnoreCase){[_bagPath]=o.Bag,[_crrPath]=o.Crr};};
        }
        else
        {
            byte[] exheader=ReadOptional(_exheaderPath)??throw new FileNotFoundException("Falta el ExHeader del proyecto.");
            dependencies.Add((_exheaderPath!,SHA256.HashData(exheader)));
            inspection=RareCandyMultiUsePatch.Inspect(title,exheader,current[_codePath!],current[_bagPath],current[_crrPath],_profile);
            build=()=>{var o=enable?RareCandyMultiUsePatch.Install(title,exheader,current[_codePath!],current[_bagPath],current[_crrPath],_profile)
                :RareCandyMultiUsePatch.Remove(title,exheader,current[_codePath!],current[_bagPath],current[_crrPath],_profile);
                return new(StringComparer.OrdinalIgnoreCase){[_codePath!]=o.Code,[_bagPath]=o.Bag,[_crrPath]=o.Crr};};
        }
        if(_ncchPath is not null)dependencies.Add((_ncchPath,SHA256.HashData(File.ReadAllBytes(_ncchPath))));
        var parameters=new Dictionary<string,string>{{"ActionId",_usum?"usum.rare-candy-multiuse":"oras.rare-candy-multiuse"},{"Profile",_usum?RareCandyUsumPatch.ProfileId:_profile.Id},{"Enable",enable?"1":"0"}};
        if(inspection.State==(enable?RareCandyMultiUseState.Installed:RareCandyMultiUseState.Original))
            return new(_owner,action,0,[],[],parameters);
        var output=build();
        var changes=inspection.Report.Resources.Where(r=>r.Offset is not null&&r.Resource!="memoria · estado de 12 bytes")
            .Select((r,i)=>new BatchChange(i+1,r.Resource,r.Role,enable?"Original":"Instalado",enable?"Instalado":"Original")).ToList();
        changes.Add(new(changes.Count+1,bagName+" · hashes","SHA-256 del segmento de código","Recalculado","Recalculado"));
        changes.Add(new(changes.Count+1,"static.crr","Entrada de "+bagName,"Hash anterior","Hash nuevo (lista ordenada)"));
        return new(_owner,action,0,_written.Select(p=>new FileChange(p,current[p],output[p])),changes,parameters,dependencies);
    }

    public void Save(int id,IReadOnlyDictionary<string,int> values){using var manual=ManualOperationScope.Record(this,id,values);Apply(Preview(values,[id]));}

    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("Propuesta de otra sesión.");
        foreach(var (path,fingerprint) in proposal.Dependencies)GarcTransaction.ReadUnchanged(path,fingerprint);
        if(proposal.Files.Count==0)return;
        foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        Remember();
        var expected=proposal.Parameters["Enable"]=="1"?RareCandyMultiUseState.Installed:RareCandyMultiUseState.Original;
        var state=Inspect().State;
        if(state!=expected)throw new InvalidDataException("La relectura de Caramelos Raros personalizados no coincide ("+state+").");
    }
}
