using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using pk3DS.Core.Modding.Research;

namespace ThreeDS.Progressive.Services;

/// <summary>One opt-in for the supported consumable sources. No default RNG draws are added.</summary>
internal sealed class UsumMintItemPolicy(GameProjectSession project,Func<byte[],UsumNatureMintProfile>? identifyCode=null)
{
    internal const string Node="Gen7MintItems",AddGeneralAction="marts.add-nature-mints";
    internal const string Label="Incluir mentas Gen7 (requiere parche instalado)";
    internal static readonly IReadOnlySet<int> Ids=UsumNatureMintCatalog.Mints.Select(m=>m.ItemId).ToHashSet();
    internal bool Include {get;set;}=Remembered(project);
    private static bool Remembered(GameProjectSession project)
    {
        if(!project.GameConfiguration.USUM)return false;
        bool include=false;
        foreach(var entry in EffectiveHistory.Of(project.History.Read()).Entries)
        {
            if(entry.Parameters.TryGetValue("GlobalTemplateSnapshot",out var json))
                include=Read(GlobalTemplateDocument.Parse(json,project.GameName,project.Generation));
            if(entry.Parameters.TryGetValue(Node,out var saved))
                include=bool.TryParse(saved,out bool value)?value:throw new InvalidDataException("Inclusión de mentas no válida en el historial.");
        }
        return include;
    }
    internal (bool Available,string Report) Inspect()
    {
        if(!project.GameConfiguration.USUM)return(false,"Mentas: solo Ultra Sol / Ultra Luna.");
        var functional=new UsumNatureMintService(project,identifyCode).Inspect();
        if(functional.State!=UsumMintState.Installed)return(false,"Mentas no disponibles: "+functional.Report);
        var icons=new UsumMintIconService(project,identifyCode).Inspect();
        return(true,"21 mentas disponibles como consumibles para tiendas, objetos EI/EB y Recogida. "+
            (icons.State==UsumMintState.Installed?"Iconos reconocidos.":"Advertencia visual: iconos ausentes o no reconocidos; las mentas funcionales se permiten."));
    }
    internal MintItemDependency Capture()
    {
        if(!project.GameConfiguration.USUM)throw new InvalidDataException("Las mentas requieren Ultra Sol / Ultra Luna.");
        var config=project.GameConfiguration;
        string header=new[]{"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"}.Select(name=>Path.Combine(project.ProjectRoot,name)).FirstOrDefault(File.Exists)
            ??throw new InvalidDataException("Falta la identidad NCCH para acreditar las mentas.");
        string exheader=new[]{"exheader.bin","DecryptedExHeader.bin"}.Select(name=>Path.Combine(project.ProjectRoot,name)).FirstOrDefault(File.Exists)
            ??throw new InvalidDataException("Falta el mapa de memoria del exheader para acreditar las mentas.");
        var paths=Directory.GetFiles(config.ExeFS).Where(p=>Path.GetFileName(p) is "code.bin" or ".code.bin")
            .Concat(Directory.GetFiles(config.RomFS,"*.cro")).Concat(new[]{header,exheader,
                Path.Combine(config.RomFS,config.GetGARCFileName("item")),Path.Combine(config.RomFS,"a/1/0/3"),Path.Combine(config.RomFS,".crr/static.crr")})
            .Concat(Enumerable.Range(0,10).Select(language=>Path.Combine(config.RomFS,config.GetGARCReference("gametext").GetRelativeGARC(language).Reference)))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(string path in paths)CheckPath(project,path);
        var dependencies=paths.Select(path=>(Path:path,Fingerprint:SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
        var state=new UsumNatureMintService(project,identifyCode).Inspect();
        if(state.State!=UsumMintState.Installed)throw new InvalidDataException("La operación requiere las 21 mentas funcionales instaladas y reconocidas. "+state.Report);
        foreach(var dependency in dependencies)GarcTransaction.ReadUnchanged(dependency.Path,dependency.Fingerprint);
        return new(project,dependencies);
    }
    internal int[] Pool(IEnumerable<int> original,bool include,Func<int,bool>? allows=null)
    {
        int[] old=original.ToArray();
        if(!project.GameConfiguration.USUM){if(include)throw new InvalidDataException("Las mentas solo se permiten en USUM.");return old;}
        if(!include)return old.Where(id=>!Ids.Contains(id)).ToArray();
        _=Capture();
        return old.Concat(UsumNatureMintCatalog.Mints.Select(m=>m.ItemId)).Distinct().Where(id=>allows?.Invoke(id)??true).ToArray();
    }
    internal static bool Read(GlobalTemplateDocument document)
    {
        var node=document.Section("ProgressiveApp")?[Node];if(node is null)return false;
        if(node is not JsonObject obj||obj["Include"] is not JsonValue value||!value.TryGetValue<bool>(out bool include))
            throw new InvalidDataException("Gen7MintItems requiere una opción Include booleana.");
        if(include&&(document.Generation!=7||!document.Game.Equals("USUM",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("La receta de mentas requiere USUM.");
        return include;
    }
    internal static void Capture(GlobalTemplateDocument document,bool include)
    {
        if(!include&&document.Section("ProgressiveApp")?[Node] is null)return;
        var native=document.Section("ProgressiveApp") as JsonObject??new();
        if(native[Node] is not null and not JsonObject)throw new InvalidDataException("Gen7MintItems debe ser un objeto de opciones.");
        var node=native[Node] as JsonObject??new();node["Include"]=include;native[Node]=node;document.SetSection("ProgressiveApp",native);_=Read(document);
    }
    internal static IReadOnlyDictionary<string,string> Parameters(IReadOnlyDictionary<string,string> parameters,bool include)
    {
        var result=new Dictionary<string,string>(parameters){[Node]=include?"true":"false"};return result;
    }
    internal static void ValidateDocument(GlobalTemplateDocument document)
    {
        if(document.Actions.Contains(AddGeneralAction,StringComparer.OrdinalIgnoreCase)&&
            (document.Generation!=7||!document.Game.Equals("USUM",StringComparison.OrdinalIgnoreCase)||NativeGen7MartBridge.Read(document)?.AddMints!=true))
            throw new InvalidDataException("Añadir las 21 mentas requiere USUM y opciones nativas de tiendas completas.");
        if(!Read(document))return;
        if(document.Actions.Contains("field-items.randomize",StringComparer.OrdinalIgnoreCase)&&!NativeGen7FieldBridge.Selected(document,"FieldItems"))
            throw new InvalidDataException("Las mentas en objetos de campo requieren las opciones nativas guardadas; vuelve a guardar el Global Template desde el editor.");
        if(document.Actions.Any(id=>id is "marts.randomize" or "marts.randomize-bp")&&NativeGen7MartBridge.Read(document) is null)
            throw new InvalidDataException("Las mentas en tiendas requieren las opciones nativas guardadas.");
    }
    internal static void ValidateSession(GameProjectSession project,GlobalTemplateDocument document)
    {
        ValidateDocument(document);
        if(Read(document)||document.Actions.Contains(AddGeneralAction,StringComparer.OrdinalIgnoreCase))_=project.MintItems.Capture();
    }
    public static void ValidateBatch(string root,string json)
    {
        var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
        ValidateSession(project,GlobalTemplateDocument.Parse(json,project.GameName,project.Generation));
    }
    internal static void CheckPath(GameProjectSession project,string path)
    {
        string root=Path.GetFullPath(project.ProjectRoot),full=Path.GetFullPath(path);
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Dependencia de mentas fuera del proyecto.");
        for(string? at=full;at is not null&&at.Length>=root.Length;at=Path.GetDirectoryName(at))
            if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("No se permiten enlaces en las dependencias de mentas.");
    }
}

internal sealed class MintItemDependency(GameProjectSession project,IReadOnlyList<(string Path,byte[] Fingerprint)> dependencies)
{
    internal IReadOnlyList<(string Path,byte[] Fingerprint)> Files=>dependencies;
    internal IDisposable Lock(IReadOnlyList<FileChange> changes)=>Lock(project,dependencies,changes);
    internal static IDisposable Lock(GameProjectSession project,IReadOnlyList<(string Path,byte[] Fingerprint)> dependencies,IReadOnlyList<FileChange> changes)
    {
        var scope=new ReadLocks();
        try
        {
            foreach(var dependency in dependencies)
            {
                UsumMintItemPolicy.CheckPath(project,dependency.Path);
                if(changes.Any(change=>change.Changed&&change.Path.Equals(dependency.Path,StringComparison.OrdinalIgnoreCase)))GarcTransaction.ReadUnchanged(dependency.Path,dependency.Fingerprint);
                else
                {
                    var file=new FileStream(dependency.Path,FileMode.Open,FileAccess.Read,FileShare.Read);scope.Files.Add(file);
                    if(!SHA256.HashData(file).AsSpan().SequenceEqual(dependency.Fingerprint))throw new IOException("El parche de mentas cambió desde la vista previa. Prepara de nuevo la operación.");
                }
            }
            return scope;
        }
        catch{scope.Dispose();throw;}
    }
    private sealed class ReadLocks:IDisposable
    {internal readonly List<FileStream> Files=[];public void Dispose(){foreach(var file in Files)file.Dispose();}}
}
