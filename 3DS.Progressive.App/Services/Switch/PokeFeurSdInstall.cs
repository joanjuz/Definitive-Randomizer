using System.IO;
using System.Text;
using System.Security.Cryptography;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed record PokeFeurSdPlan(string Destination,string Config,string NewHash,string? ExistingHash,string? Backup);
internal static class PokeFeurSdInstall
{
    private static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    internal static PokeFeurSdPlan Preview(string sdRoot,string config,string sourceRoot)
    {
        string root=Path.GetFullPath(sdRoot),source=Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));SwordShieldProject.CheckNoLinks(root);string destination=Path.GetFullPath(Path.Combine(root,"config/swsh-mods-exl/config.toml"));SwordShieldProject.CheckNoLinks(destination);
        if(destination.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||source.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||root.Equals(source,StringComparison.OrdinalIgnoreCase))throw new IOException("La configuración SD debe quedar fuera de la extracción y de sus carpetas padre.");
        if(!Directory.Exists(root))throw new DirectoryNotFoundException("Selecciona la raíz SD existente del emulador.");if(config.Length>200000)throw new InvalidDataException("Configuración demasiado grande.");
        string hash=Hash(Encoding.UTF8.GetBytes(config)),old=System.IO.File.Exists(destination)?Hash(System.IO.File.ReadAllBytes(destination)):"";
        return new(destination,config,hash,old.Length==0?null:old,old.Length>0&&old!=hash?destination+".backup-"+Guid.NewGuid().ToString("N"):null);
    }
    internal static void Apply(PokeFeurSdPlan plan,bool replace)
    {
        string target=Path.GetFullPath(plan.Destination);SwordShieldProject.CheckNoLinks(target);if(Path.GetFileName(target)!="config.toml"||Path.GetFileName(Path.GetDirectoryName(target))!="swsh-mods-exl")throw new IOException("Destino de configuración inválido.");
        byte[] bytes=Encoding.UTF8.GetBytes(plan.Config);if(Hash(bytes)!=plan.NewHash)throw new InvalidDataException("La propuesta de configuración cambió.");string? old=System.IO.File.Exists(target)?Hash(System.IO.File.ReadAllBytes(target)):null;
        if(old!=plan.ExistingHash)throw new IOException("La configuración de SD cambió después de previsualizar.");if(old==plan.NewHash)return;
        if(old is not null&&(!replace||plan.Backup is null))throw new IOException("La configuración existente es distinta; se requiere sustitución explícita con copia de seguridad.");
        if(plan.Backup is {} backup){backup=Path.GetFullPath(backup);if(!string.Equals(Path.GetDirectoryName(backup),Path.GetDirectoryName(target),StringComparison.OrdinalIgnoreCase)||!backup.StartsWith(target+".backup-",StringComparison.OrdinalIgnoreCase)||System.IO.File.Exists(backup))throw new IOException("Copia de seguridad inválida.");}
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);SwordShieldProject.CheckNoLinks(target);string temp=target+".tmp-"+Guid.NewGuid().ToString("N");
        try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes);stream.Flush(true);}if(!System.IO.File.ReadAllBytes(temp).AsSpan().SequenceEqual(bytes))throw new IOException("No se pudo verificar la configuración preparada.");string? now=System.IO.File.Exists(target)?Hash(System.IO.File.ReadAllBytes(target)):null;if(now!=old)throw new IOException("La configuración cambió durante la preparación.");if(old is not null)System.IO.File.Copy(target,plan.Backup!,false);System.IO.File.Move(temp,target,old is not null);}
        finally{if(System.IO.File.Exists(temp))System.IO.File.Delete(temp);}
    }
}
