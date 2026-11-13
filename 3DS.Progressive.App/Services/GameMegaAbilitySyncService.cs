using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;
using pk3DS.Core.Modding.Research;
namespace ThreeDS.Progressive.Services;
/// <summary>
/// Installs or removes Mega Ability Sync in Ultra Sun / Ultra Moon: in a link battle each game sends, with its party,
/// the Ability its Pokémon get when they Mega Evolve, and the other game uses it for those Pokémon. Both players need
/// it; with only one side installed both games behave as the original. Only Battle.cro is written; static.crs and
/// code.bin are read to resolve addresses.
/// </summary>
public sealed class GameMegaAbilitySyncService:IProjectEditor
{
    internal const string CapabilityId="usum.mega-ability-sync";
    private const string Entry="Usar en combates entre dos juegos la habilidad Mega del dueño de cada Pokémon";
    private readonly GameProjectSession _project;private readonly MegaAbilitySyncProfile _profile;private readonly string _battlePath,_crsPath,_codePath;private byte[] _hash;private readonly Guid _owner=Guid.NewGuid();
    public string Title=>"Mega Ability Sync · USUM";
    public IReadOnlyList<RecordEntry> Entries=>[new(1,Entry)];
    public IReadOnlyList<EditorGroup> RandomOptions=>[new("Mega Ability Sync",[new("Enable","1 = instalar · 0 = retirar. Los dos jugadores deben instalarlo; no cambia los combates sin conexión",1,0,1)])];
    public GameMegaAbilitySyncService(GameProjectSession project):this(project,MegaAbilitySyncProfile.Usum){}
    internal GameMegaAbilitySyncService(GameProjectSession project,MegaAbilitySyncProfile profile)
    {
        if(!project.GameConfiguration.USUM)throw new NotSupportedException("Mega Ability Sync solo está disponible para Ultra Sol/Ultra Luna.");
        _project=project;_profile=profile;var config=project.GameConfiguration;
        _battlePath=GamePlayerLevelCapService.Find(config.RomFS,"Battle.cro",true);
        _crsPath=Path.Combine(config.RomFS,"static.crs");
        _codePath=GamePlayerLevelCapService.Find(config.ExeFS,"code.bin",false);
        if(!File.Exists(_battlePath))throw new FileNotFoundException("Battle.cro ausente.");
        _hash=SHA256.HashData(File.ReadAllBytes(_battlePath));
        var capability=InspectCapability(File.ReadAllBytes(_battlePath),ReadOptional(_crsPath),ReadOptional(_codePath),_profile);
        if(!capability.CanApply)throw new InvalidDataException("No se modificó ningún archivo. "+GamePlayerLevelCapService.Describe(capability));
    }
    private static byte[]? ReadOptional(string path)=>File.Exists(path)?File.ReadAllBytes(path):null;

    /// <summary>
    /// Read-only capability report. The installation is simulated on a copy with the real installer, so the report and
    /// the installer always agree. The input is never modified.
    /// </summary>
    internal static PatchCapabilityReport InspectCapability(byte[]? cro,byte[]? crs,byte[]? code,MegaAbilitySyncProfile? profile=null)
    {
        profile??=MegaAbilitySyncProfile.Usum;
        PatchCapabilityReport Report(PatchCapabilityLevel level,string summary,IReadOnlyList<PatchResourceReport> resources,IReadOnlyList<PatchChangedRange>? changes=null,IReadOnlyList<string>? resized=null)=>
            new(CapabilityId,level,resources,changes??[],resized??[],summary);
        PatchResourceReport Whole(string name,PatchResourceState state,string evidence)=>new(name,"Archivo completo",state,null,evidence);
        if(cro is null)return Report(PatchCapabilityLevel.NotAccredited,"Datos insuficientes: falta Battle.cro.",[Whole("Battle.cro",PatchResourceState.Missing,"No se encontró el archivo.")]);
        if(crs is null||code is null)
            return Report(PatchCapabilityLevel.NotAccredited,"Datos insuficientes: se necesitan static.crs y code.bin descomprimido para resolver las direcciones del juego.",
                [Whole(crs is null?"static.crs":"code.bin",PatchResourceState.Missing,"No se encontró el archivo.")]);
        if(!MegaAbilitySyncPatch.TryResolveCodeAddresses(crs,code,out var addresses,out string addressError))
            return Report(PatchCapabilityLevel.NotAccredited,"No se pudieron acreditar las direcciones del juego: "+addressError,
                [Whole("code.bin",PatchResourceState.ExternalModification,addressError)]);
        var inspection=MegaAbilitySyncPatch.Inspect(cro,addresses,profile);
        switch(inspection.State)
        {
            case MegaAbilitySyncState.Unsupported:
                return Report(PatchCapabilityLevel.NotAccredited,inspection.Detail,[Whole("Battle.cro",PatchResourceState.ExternalModification,inspection.Detail)]);
            case MegaAbilitySyncState.External:
                return Report(PatchCapabilityLevel.Conflict,inspection.Detail,inspection.Sites);
            case MegaAbilitySyncState.Partial:
                return Report(PatchCapabilityLevel.NotAccredited,inspection.Detail+" Restaura Battle.cro desde el historial antes de volver a instalar.",inspection.Sites);
        }
        if(!MegaAbilitySyncPatch.TryInstall(cro,addresses,profile,out byte[] output,out string installError))
            return Report(PatchCapabilityLevel.Conflict,"No se pudo simular la instalación: "+installError,inspection.Sites);
        string state=inspection.State switch
        {
            MegaAbilitySyncState.Installed=>"El parche está instalado.",
            MegaAbilitySyncState.InstalledEarlierFormat=>"El parche está instalado con la rutina anterior, que no sincroniza en los combates por enlace; al instalarlo de nuevo se sustituye en el mismo sitio.",
            _=>"Los cuatro puntos tienen el código original."
        };
        if(output.Length!=cro.Length)
            return Report(PatchCapabilityLevel.CompatibleByAdaptation,state+" La instalación amplía el segmento de código porque no queda espacio libre.",inspection.Sites,[],["Battle.cro"]);
        var changes=new List<PatchChangedRange>();
        for(int i=0;i<cro.Length;i++){if(cro[i]==output[i])continue;int start=i;while(i<cro.Length&&cro[i]!=output[i])i++;changes.Add(new("Battle.cro",(uint)start,i-start));}
        return Report(PatchCapabilityLevel.Verified,state+" Las rutinas de combate de las que depende son las originales.",inspection.Sites,changes);
    }

    /// <summary>Installs or removes on detached copies. Throws without output when the files are not accredited.</summary>
    internal static (byte[] Bytes,bool Changed,bool Installed) Patch(byte[] cro,byte[]? crs,byte[]? code,bool enable,MegaAbilitySyncProfile? profile=null)
    {
        profile??=MegaAbilitySyncProfile.Usum;
        var capability=InspectCapability(cro,crs,code,profile);
        if(!capability.CanApply||crs is null||code is null||!MegaAbilitySyncPatch.TryResolveCodeAddresses(crs,code,out var addresses,out _))
            throw new InvalidDataException("No se modificó ningún archivo. "+GamePlayerLevelCapService.Describe(capability));
        string error;byte[] output;
        if(!(enable?MegaAbilitySyncPatch.TryInstall(cro,addresses,profile,out output,out error):MegaAbilitySyncPatch.TryUninstall(cro,addresses,profile,out output,out error)))
            throw new InvalidDataException("No se modificó ningún archivo. "+error);
        return(output,!output.AsSpan().SequenceEqual(cro),MegaAbilitySyncPatch.Inspect(output,addresses,profile).State==MegaAbilitySyncState.Installed);
    }
    private MegaAbilitySyncState? State(byte[] cro)=>ReadOptional(_crsPath) is { } crs&&ReadOptional(_codePath) is { } code&&
        MegaAbilitySyncPatch.TryResolveCodeAddresses(crs,code,out var addresses,out _)?MegaAbilitySyncPatch.Inspect(cro,addresses,_profile).State:null;
    private bool Installed(byte[] cro)=>State(cro)==MegaAbilitySyncState.Installed;
    private static string Label(MegaAbilitySyncState? state)=>state switch
    {
        MegaAbilitySyncState.Installed=>"instalado",
        MegaAbilitySyncState.InstalledEarlierFormat=>"instalado con la rutina anterior",
        _=>"sin instalar"
    };
    public IReadOnlyList<EditorGroup> Read(int id)
    {
        if(id!=1)throw new ArgumentException("Registro inválido.");
        var data=GarcTransaction.ReadUnchanged(_battlePath,_hash);
        var state=State(data);
        string help=state==MegaAbilitySyncState.InstalledEarlierFormat
            ?"Instalado con la rutina anterior, que no sincroniza en los combates por enlace: aplica 1 para sustituirla"
            :"Mega Ability Sync instalado (1) o no (0)";
        return[new("Estado",[new("Enable",help,state is MegaAbilitySyncState.Installed or MegaAbilitySyncState.InstalledEarlierFormat?1:0,0,1)])];
    }
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions,options);
        if(!scope.Contains(1)||scope.Any(i=>i!=1))throw new ArgumentException("Selecciona Mega Ability Sync.");
        bool enable=options["Enable"]==1;
        var source=GarcTransaction.ReadUnchanged(_battlePath,_hash);
        var patch=Patch(source,ReadOptional(_crsPath),ReadOptional(_codePath),enable,_profile);
        var before=State(source);
        string action=!enable?"Retirar Mega Ability Sync":before==MegaAbilitySyncState.InstalledEarlierFormat?"Actualizar Mega Ability Sync":"Instalar Mega Ability Sync";
        return new(_owner,action,0,[new(_battlePath,source,patch.Bytes)],
            patch.Changed?[new(1,Entries[0].Name,"Battle.cro",Label(before),enable?"instalado":"sin instalar")]:[],
            new Dictionary<string,string>{{"Game","USUM"},{"Protocol",MegaAbilitySyncPatch.ProtocolVersion.ToString()},{"Enable",enable?"1":"0"}});
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values){using var manual=ManualOperationScope.Record(this,id,values);Apply(Preview(values,[id]));}
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("Propuesta de otra sesión.");
        _=GarcTransaction.ReadUnchanged(_battlePath,_hash);
        if(proposal.Changes.Count==0)return;
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        byte[] saved=File.ReadAllBytes(_battlePath);_hash=SHA256.HashData(saved);
        bool expected=proposal.Parameters.TryGetValue("Enable",out string? enable)&&enable=="1";
        if(Installed(saved)!=expected)throw new InvalidDataException("No coincide la relectura de Mega Ability Sync.");
    }
}
