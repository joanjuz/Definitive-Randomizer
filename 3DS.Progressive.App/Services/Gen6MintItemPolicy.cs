using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core.Modding.Research;

namespace ThreeDS.Progressive.Services;

/// <summary>Injected installation evidence. A provider must capture dependencies and recheck them;
/// a syntactically valid map or hashes alone do not establish native compatibility.</summary>
internal sealed class Gen6MintInstallation
{
    internal ulong TitleId {get;}
    internal Gen6MintItemMap Map {get;}
    internal IReadOnlyDictionary<string,string> Dependencies {get;}
    private readonly Action _verifyUnchanged;
    internal Gen6MintInstallation(ulong titleId,Gen6MintItemMap map,IReadOnlyDictionary<string,string> dependencies,Action verifyUnchanged)
    {
        ArgumentNullException.ThrowIfNull(map);ArgumentNullException.ThrowIfNull(dependencies);ArgumentNullException.ThrowIfNull(verifyUnchanged);
        if(titleId is not (0x0004000000055D00UL or 0x0004000000055E00UL) || dependencies.Count==0 ||
            dependencies.Any(d=>string.IsNullOrWhiteSpace(d.Key)||d.Value is null||d.Value.Length!=64||d.Value.Any(c=>!Uri.IsHexDigit(c))))
            throw new InvalidDataException("La acreditación Gen6 requiere un título X/Y y dependencias SHA-256 capturadas.");
        TitleId=titleId;Map=map;
        Dependencies=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>(dependencies));
        _verifyUnchanged=verifyUnchanged;
    }
    internal void VerifyUnchanged()=>_verifyUnchanged();
}

/// <summary>Independent, default-off XY distribution policy. No native installer or RNG is invoked.</summary>
internal sealed class Gen6MintItemPolicy
{
    internal const string Node=Gen6MintExecutionContract.Node;
    internal const string Label="Incluir mentas Gen6 (requiere parche instalado)";
    internal const string BlockedReason=Gen6MintExecutionContract.BlockedReason;
    private readonly string _game;
    private readonly int _generation;
    private readonly Func<Gen6MintInstallation> _captureInstalled;
    internal bool Include {get;set;}
    internal Gen6MintItemPolicy(GameProjectSession project,Func<Gen6MintInstallation>? captureInstalled=null)
        :this(project.GameName,project.Generation,captureInstalled??(()=>Gen6MintRuntimeDependencies.Capture(project))) { }
    internal Gen6MintItemPolicy(string game,int generation,Func<Gen6MintInstallation>? captureInstalled=null)
    {
        _game=game;_generation=generation;
        _captureInstalled=captureInstalled??(()=>throw new InvalidDataException(BlockedReason));
    }
    private static bool IsXY(string game,int generation)=>generation==6&&game.Equals("XY",StringComparison.OrdinalIgnoreCase);
    internal Gen6MintInstallation Capture()
    {
        if(!IsXY(_game,_generation))throw new InvalidDataException("Las mentas Gen6 requieren Pokémon X/Y.");
        var evidence=_captureInstalled()??throw new InvalidDataException(BlockedReason);
        evidence.VerifyUnchanged();return evidence;
    }
    internal static (int[] Pool,Gen6MintInstallation? Evidence) AdmitPool(GameProjectSession project,IEnumerable<int> original,bool include,IReadOnlySet<int>? allowed=null,IReadOnlySet<int>? banned=null)
    {
        Gen6MintInstallation? evidence=null;
        var policy=new Gen6MintItemPolicy(project,()=>evidence??=Gen6MintRuntimeDependencies.Capture(project));
        return(policy.Pool(original,include,allowed,banned),evidence);
    }
    internal (bool Available,string Report) Inspect()
    {
        try { _=Capture();return(true,"21 mentas Gen6 con instalación y dependencias acreditadas."); }
        catch(InvalidDataException ex){return(false,ex.Message);}
    }
    internal int[] Pool(IEnumerable<int> original,bool include,IReadOnlySet<int>? allowed=null,IReadOnlySet<int>? banned=null)
    {
        var old=original.ToArray();
        if(!include)return old; // Preserve historical order and weighting, without probing installation or filters.
        var evidence=Capture();
        var present=old.ToHashSet();
        var pool=old.Concat(evidence.Map.Items.Select(m=>m.ItemId).Where(present.Add))
            .Where(id=>(allowed is null||allowed.Contains(id)) && !(banned?.Contains(id)??false)).ToArray();
        evidence.VerifyUnchanged();return pool;
    }
    internal static bool Read(GlobalTemplateDocument document)
        =>Gen6MintExecutionContract.Read(document.ToJson());
    internal static void Capture(GlobalTemplateDocument document,bool include)
    {
        if(include&&!IsXY(document.Game,document.Generation))throw new InvalidDataException("La receta de mentas Gen6 requiere Pokémon X/Y.");
        if(!include&&document.Section("ProgressiveApp")?[Node] is null)return;
        var native=document.Section("ProgressiveApp") as JsonObject;
        if(document.Section("ProgressiveApp") is not null&&native is null)throw new InvalidDataException("ProgressiveApp debe ser un objeto.");
        if(native?[Node] is not null and not JsonObject)throw new InvalidDataException("Gen6MintItems debe ser un objeto de opciones.");
        var copy=native?.DeepClone() as JsonObject??new();
        var node=copy[Node] as JsonObject??new();node["Include"]=include;copy[Node]=node;document.SetSection("ProgressiveApp",copy);
    }
    internal static void Capture(GlobalTemplateDocument document,bool includeMarts,bool includeFieldItems)
    {
        if(!includeMarts&&!includeFieldItems&&document.Section("ProgressiveApp")?[Node] is null)return;
        if((includeMarts||includeFieldItems)&&!IsXY(document.Game,document.Generation))throw new InvalidDataException("La receta de mentas Gen6 requiere Pokémon X/Y.");
        if(document.Section("ProgressiveApp") is not null and not JsonObject)throw new InvalidDataException("ProgressiveApp debe ser un objeto.");
        var copy=document.Section("ProgressiveApp")?.DeepClone() as JsonObject??new();
        if(copy[Node] is not null and not JsonObject)throw new InvalidDataException("Gen6MintItems debe ser un objeto de opciones.");
        var node=copy[Node] as JsonObject??new();node.Remove("Include");node["IncludeMarts"]=includeMarts;node["IncludeFieldItems"]=includeFieldItems;
        copy[Node]=node;document.SetSection("ProgressiveApp",copy);
    }
}
