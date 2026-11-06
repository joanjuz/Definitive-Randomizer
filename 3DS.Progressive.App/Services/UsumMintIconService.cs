using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed record UsumMintIconPreview(UsumMintState State,string Report,EditorProposal? Proposal=null);

/// <summary>Phase 1.1 changes only the executable's item-image references and the image GARC.</summary>
internal sealed class UsumMintIconService(GameProjectSession project,
    Func<byte[],UsumNatureMintProfile>? identifyCode=null,IReadOnlyList<UsumMintIconSource>? sources=null)
{
    private readonly Guid _owner=Guid.NewGuid();
    private IReadOnlyList<UsumMintIconSource> Sources=>sources??UsumMintIconSources.All;
    private sealed record Snapshot(string CodePath,byte[] Code,string IconPath,byte[] Archive,
        IReadOnlyList<(string Path,byte[] Fingerprint)> Dependencies);

    internal UsumMintIconPreview Inspect()
    {
        try
        {
            var snapshot=Read();
            if(UsumMintIconLayout.HasMapping(snapshot.Code,true))
            {
                var records=UsumMintIconGraphics.ReadArchive(snapshot.Archive,UsumMintIconLayout.InstalledImageCount);
                for(int i=0;i<Sources.Count;i++)
                    if(Hash(UsumMintIconGraphics.Decompress(records[UsumMintIconLayout.OriginalImageCount+i]))!=Sources[i].TextureSha256)
                        return new(UsumMintState.Modified,"Un icono de menta cambió externamente; no se sobrescribe.");
                return new(UsumMintState.Installed,"21 iconos reconocidos. Las tiendas y los demás iconos se conservan.");
            }
            UsumMintIconGraphics.ReadArchive(snapshot.Archive,UsumMintIconLayout.OriginalImageCount);
            return new(UsumMintState.NotInstalled,"Iconos no instalados. Se pueden descargar y preparar los 21 iconos de PokéAPI.");
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or NotSupportedException)
        {return new(UsumMintState.Incompatible,ex.Message);}
    }
    internal async Task<UsumMintIconPreview> PrepareAsync(CancellationToken cancellation=default)
    {
        var state=Inspect();
        if(state.State!=UsumMintState.NotInstalled)return state;
        var textures=await UsumMintIconDownload.LoadAsync(UsumMintIconDownload.DefaultCache,cancellation:cancellation);
        return Prepare(textures);
    }
    internal UsumMintIconPreview Prepare(IReadOnlyList<byte[]> textures)
    {
        var snapshot=Read();
        if(!UsumMintIconLayout.HasMapping(snapshot.Code,false))throw new InvalidDataException("Los iconos ya están instalados o su tabla cambió. Comprueba el estado.");
        if(textures.Count!=21||textures.Select((texture,i)=>Hash(texture)==Sources[i].TextureSha256).Any(valid=>!valid))
            throw new InvalidDataException("Los 21 iconos no coinciden con el perfil de conversión v1.");
        byte[] archive=UsumMintIconGraphics.Append(snapshot.Archive,textures);
        byte[] code=UsumMintIconLayout.InstallMapping(snapshot.Code);
        var files=new[]{new FileChange(snapshot.CodePath,snapshot.Code,code),new FileChange(snapshot.IconPath,snapshot.Archive,archive)};
        var proposal=new EditorProposal(_owner,"Instalar iconos de mentas Gen7",0,files,
            Sources.Select((source,i)=>new BatchChange(source.ItemId,"Menta "+source.Slug,"Icono",UsumMintIconLayout.OriginalImage(source.ItemId).ToString(),(769+i).ToString())),
            new Dictionary<string,string>{{"ActionId","usum.nature-mints.icons.install"},{"Phase","1.1"},
                {"Source","PokeAPI/sprites"},{"SourceCommit",UsumMintIconSources.Commit},{"Conversion","RGB5A1-32-area-v1"}},snapshot.Dependencies);
        return new(UsumMintState.NotInstalled,"21 iconos preparados: 32×32 RGB5A1. Se añaden las imágenes 769–789 sin sustituir imágenes existentes.\n"+
            string.Join("\n",files.Select(file=>Path.GetRelativePath(project.ProjectRoot,file.Path)))+"\nLas tiendas, precios, textos y el comportamiento de las mentas no cambian.",proposal);
    }
    private Snapshot Read()
    {
        if(!project.GameConfiguration.USUM)throw new NotSupportedException("Los iconos de mentas requieren Ultra Sol o Ultra Luna.");
        if(Sources.Count!=21||!Sources.Select(s=>s.ItemId).SequenceEqual(UsumNatureMintCatalog.Mints.Select(m=>m.ItemId)))
            throw new InvalidDataException("El catálogo de iconos no coincide con las 21 mentas.");
        var config=project.GameConfiguration;
        var codes=Directory.GetFiles(config.ExeFS).Where(p=>Path.GetFileName(p) is "code.bin" or ".code.bin").ToArray();
        if(codes.Length!=1)throw new InvalidDataException("Se requiere un único code.bin descomprimido.");
        string header=new[]{"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"}.Select(name=>Path.Combine(project.ProjectRoot,name)).FirstOrDefault(File.Exists)
            ??throw new InvalidDataException("Falta la identidad NCCH del proyecto.");
        string icons=Path.GetFullPath(Path.Combine(config.RomFS,UsumMintIconLayout.ArchivePath));
        var paths=new List<string>{codes[0],icons,header,Path.Combine(config.RomFS,"Bag.cro"),
            Path.Combine(config.RomFS,config.GetGARCFileName("item")),Path.Combine(config.RomFS,"a/1/0/3"),Path.Combine(config.RomFS,".crr/static.crr")};
        for(int language=0;language<10;language++)paths.Add(Path.Combine(config.RomFS,config.GetGARCReference("gametext").GetRelativeGARC(language).Reference));
        var contents=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach(string candidate in paths)
        {
            string path=Path.GetFullPath(candidate);ValidatePath(path);
            if(path.Equals(icons,StringComparison.OrdinalIgnoreCase)&&new FileInfo(path).Length>16*1024*1024)
                throw new InvalidDataException("El archivo gráfico excede el tamaño admitido para iconos USUM.");
            contents.Add(path,File.ReadAllBytes(path));
        }
        var phase1=new UsumNatureMintService(project,identifyCode).Inspect();
        if(phase1.State!=UsumMintState.Installed)throw new InvalidDataException("Se requiere la fase 1 instalada y reconocida. "+phase1.Report);
        byte[] code=contents[codes[0]];
        (identifyCode??UsumNatureMintCatalog.IdentifyCode)(code);
        if(!UsumMintIconLayout.HasMapping(code,false)&&!UsumMintIconLayout.HasMapping(code,true))
            throw new InvalidDataException("Las referencias de iconos no coinciden con un estado reconocido.");
        var dependencies=contents.Select(pair=>(Path:pair.Key,Fingerprint:SHA256.HashData(pair.Value))).ToArray();
        foreach(var dependency in dependencies)GarcTransaction.ReadUnchanged(dependency.Path,dependency.Fingerprint);
        return new(codes[0],code,icons,contents[icons],dependencies);
    }
    internal void Apply(UsumMintIconPreview preview)
    {
        var proposal=preview.Proposal??throw new InvalidOperationException("No hay iconos preparados para instalar.");
        if(proposal.Owner!=_owner||preview.State!=UsumMintState.NotInstalled)throw new InvalidOperationException("Propuesta ajena o no instalable.");
        var locks=new List<FileStream>();
        try
        {
            foreach(var dependency in proposal.Dependencies)
            {
                ValidatePath(dependency.Path);
                if(proposal.Files.Any(file=>file.Path.Equals(dependency.Path,StringComparison.OrdinalIgnoreCase)))
                    GarcTransaction.ReadUnchanged(dependency.Path,dependency.Fingerprint);
                else
                {
                    var stream=new FileStream(dependency.Path,FileMode.Open,FileAccess.Read,FileShare.Read);locks.Add(stream);
                    if(!SHA256.HashData(stream).AsSpan().SequenceEqual(dependency.Fingerprint))throw new IOException("Una dependencia de las mentas cambió; prepara de nuevo los iconos.");
                }
            }
            project.History.Commit(proposal.Files,"Mentas Gen7","Instalar iconos de mentas Gen7",proposal.Changes,proposal.Parameters,section:"UsumNatureMintIcons");
        }
        finally{foreach(var stream in locks)stream.Dispose();}
    }
    private void ValidatePath(string path)
    {
        string root=Path.GetFullPath(project.ProjectRoot),full=Path.GetFullPath(path);
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Archivo de iconos fuera del proyecto.");
        for(string? at=full;at is not null&&at.Length>=root.Length;at=Path.GetDirectoryName(at))
            if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("No se permiten enlaces en los archivos del parche.");
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
}
