using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.NX;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed class PokeFeurPackage
{
    internal const string Commit="e69afd5b9d4e5f892237bb7fb30aed10101d9c0a";
    internal const string ModuleHash="AB13EC3B8693EF475E5FD81221095D6862D3C34E179399E6B25F351FD7563331";
    internal const string NpdmHash="8FB43885745A0CB0B54D7B631F769A260ADAE13FCB0574E2C97B598C4900A8D2";
    internal const string ShieldModuleHash="C3969FC0E35339295AE46E88BE915372019A60C12B674B61A21D67AFC08B8B14";
    internal const string ShieldNpdmHash="29FAFC4E38C3B1E352F8D4ACBD19F80E8BD944F3FE32380DEF5C0E273A55EB57";
    internal string TitleId {get;}
    internal const string LicenseHash="8CEB4B9EE5ADEDDE47B31E975C1D90C73AD27B6B165A1DCD80C7C545EB65B903";
    internal const string Repository="https://github.com/JonatanUHC/PokeFeurRandomSWSH";
    internal static readonly IReadOnlyDictionary<string,string> ScriptHashes=new Dictionary<string,string>(StringComparer.Ordinal){
        ["bin/script/amx/early_bike.amx"]="05780D081D2F3552E224B1651B302AD5A2C3C079D84EB47583F546BFF625C920",
        ["bin/script/amx/randomizer_pc_menu.amx"]="191B5FFC054A22980CD34CCDED3213A2EF031189C7B7995CA95E167F70CDDE38",
        ["bin/script/amx/dex_animations.amx"]="B7524162082AF68114592362B85F6D137E4647BD6552C8373F22EDA604CF7B44"};
    private readonly Dictionary<string,byte[]> _files;
    internal string Folder {get;}
    internal bool Has(string path)=>_files.ContainsKey(path);
    internal byte[] File(string path)=>_files.TryGetValue(path,out var bytes)?(byte[])bytes.Clone():throw new FileNotFoundException("El paquete no contiene "+path);
    private PokeFeurPackage(string folder,Dictionary<string,byte[]> files,string titleId="0100ABF008968000"){Folder=folder;_files=files;TitleId=titleId;}
    // All resources are pinned to the upstream commit; no cache, filesystem or network is consulted.
    internal static PokeFeurPackage BuiltIn(string titleId="0100ABF008968000")
    {
        if(titleId is not ("0100ABF008968000" or "01008DB008C2C000"))throw new NotSupportedException("Título de runtime no compatible.");
        bool shield=titleId=="01008DB008C2C000";
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        var hashes=new Dictionary<string,string>(StringComparer.Ordinal){["exefs/subsdk9"]=shield?ShieldModuleHash:ModuleHash,["exefs/main.npdm"]=shield?ShieldNpdmHash:NpdmHash,["LICENSE-PokeFeur.txt"]=LicenseHash};
        foreach(var script in ScriptHashes)hashes.Add("romfs/"+script.Key,script.Value);
        foreach(var resource in hashes)
        {
            using var stream=typeof(PokeFeurPackage).Assembly.GetManifestResourceStream("ThreeDS.Progressive.PokeFeur."+(shield&&resource.Key.StartsWith("exefs/",StringComparison.Ordinal)?"shield/":"")+resource.Key)??throw new InvalidDataException("Falta un recurso integrado del runtime: "+resource.Key);
            if(stream.Length>8*1024*1024)throw new InvalidDataException("Recurso integrado demasiado grande: "+resource.Key);
            using var buffer=new MemoryStream();stream.CopyTo(buffer);byte[] bytes=buffer.ToArray();
            if(Convert.ToHexString(SHA256.HashData(bytes))!=resource.Value)throw new InvalidDataException("Integridad incorrecta del runtime integrado: "+resource.Key);
            files.Add(resource.Key,bytes);
        }
        for(int i=0;i<3;i++)_ = SwordSurpriseMod.ReadSegment(files["exefs/subsdk9"],i);
        return new("Integrado en la aplicación · "+Commit[..12],files,titleId);
    }
    internal static string DefaultFolder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DefinitiveRandomizer","runtimes","PokeFeur",Commit);
    internal static PokeFeurPackage Read(string folder)
    {
        folder=Path.GetFullPath(folder);SwordShieldProject.CheckNoLinks(folder);if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Selecciona el paquete local PokeFeur o la raíz de su repositorio descargado.");
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        byte[] Read(string relative,string expected){string p=Path.Combine(folder,relative);SwordShieldProject.CheckNoLinks(p);if(!System.IO.File.Exists(p)||new FileInfo(p).Length>8*1024*1024)throw new IOException("Recurso ausente o demasiado grande: "+relative);byte[] b=System.IO.File.ReadAllBytes(p);if(Convert.ToHexString(SHA256.HashData(b))!=expected)throw new InvalidDataException("Hash incompatible: "+relative+". Se admite el paquete fijado en "+Commit);return b;}
        bool repo=Directory.Exists(Path.Combine(folder,"assets/exlaunch/0100ABF008968000"));string prefix=repo?"assets/exlaunch/0100ABF008968000/":"exefs/";
        files["exefs/subsdk9"]=Read(prefix+(repo?"subsdk9_ryujinx":"subsdk9"),ModuleHash);
        files["exefs/main.npdm"]=Read(prefix+(repo?"main.npdm_ryujinx":"main.npdm"),NpdmHash);
        for(int i=0;i<3;i++)_ = SwordSurpriseMod.ReadSegment(files["exefs/subsdk9"],i);
        foreach(var p in ScriptHashes){string path=(repo?"assets/exlaunch/runtime/":"romfs/")+p.Key;if(System.IO.File.Exists(Path.Combine(folder,path)))files["romfs/"+p.Key]=Read(path,p.Value);}
        string license=Path.Combine(folder,repo?"LICENSE":"LICENSE-PokeFeur.txt");SwordShieldProject.CheckNoLinks(license);if(!System.IO.File.Exists(license))throw new FileNotFoundException("El paquete debe incluir su licencia GPL-3.0.");
        if(new FileInfo(license).Length>100000)throw new InvalidDataException("Licencia demasiado grande.");
        byte[] licenseBytes=System.IO.File.ReadAllBytes(license);if(Convert.ToHexString(SHA256.HashData(licenseBytes))!=LicenseHash)throw new InvalidDataException("Licencia del paquete inválida.");files["LICENSE-PokeFeur.txt"]=licenseBytes;return new(folder,files);
    }
}
