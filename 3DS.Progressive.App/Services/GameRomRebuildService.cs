using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;

internal enum RomRebuildKind { RomFS,ExeFS,Full3DS,Trimmed3DS }

/// <summary>Single-project rebuild using original CTR/RomFS/ExeFS algorithms.
/// Every intermediate file lives outside the source project; no writes in ROM folder.
/// </summary>
internal sealed class GameRomRebuildService(GameProjectSession project)
{
    private static readonly SemaphoreSlim BuildGate=new(1,1);
    internal async Task<(string File,string Hash,long Bytes)> BuildAsync(RomRebuildKind kind,string target)
    {
        target=Path.GetFullPath(target);
        string root=Path.GetFullPath(project.ProjectRoot);
        if(target.Equals(root,StringComparison.OrdinalIgnoreCase)||
            target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La salida no puede estar dentro de la ROM original.");
        if(File.Exists(target))throw new IOException("El archivo de salida ya existe. No se reemplazará.");
        if(!Directory.Exists(Path.GetDirectoryName(target)))
            throw new DirectoryNotFoundException("La carpeta de destino no existe.");
        await BuildGate.WaitAsync();
        string stage=Path.Combine(Path.GetTempPath(),"progressive-rebuild-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            return await Task.Run(()=>
            {
                string romfs=project.GameConfiguration.RomFS;
                string exefs=project.GameConfiguration.ExeFS;
                string romfile=Path.Combine(stage,"romfs.bin");
                string exefile=Path.Combine(stage,"exefs.bin");
                if(kind is RomRebuildKind.RomFS or RomRebuildKind.Full3DS or RomRebuildKind.Trimmed3DS)
                {
                    UiProgress.Stage("RomFS · calculando metadatos y empaquetando archivos…");
                    RomFS.BuildRomFS(romfs,romfile);
                    if(!File.Exists(romfile)||new FileInfo(romfile).Length==0)
                        throw new IOException("RomFS no produjo una salida válida.");
                    UiProgress.Count("RomFS compilado",1,kind==RomRebuildKind.RomFS?2:4);
                }
                if(kind is RomRebuildKind.ExeFS or RomRebuildKind.Full3DS or RomRebuildKind.Trimmed3DS)
                {
                    UiProgress.Stage("ExeFS · generando tabla y hashes de archivos originales…");
                    string[] source=Directory.GetFiles(exefs, "*",SearchOption.TopDirectoryOnly);
                    if(source.Length is <1 or >10)
                        throw new InvalidDataException("ExeFS requiere entre 1 y 10 archivos.");
                    string stageExe=Path.Combine(stage,"ExeFS-input");
                    Directory.CreateDirectory(stageExe);
                    var prepared=new List<string>(source.Length);
                    foreach(string path in source)
                    {
                        string copied=Path.Combine(stageExe,Path.GetFileName(path));
                        File.Copy(path,copied);
                        prepared.Add(copied);
                    }
                    UiProgress.Stage("ExeFS · BLZ comprimiendo code.bin en copia temporal…");
                    string? code=prepared.FirstOrDefault(path=>Path.GetFileName(path)
                        .Contains("code",StringComparison.OrdinalIgnoreCase));
                    if(code is null)throw new InvalidDataException("ExeFS necesita code.bin.");
                    using(var progressBar=new System.Windows.Forms.ProgressBar())
                        _=new BLZCoder(["-en",code],progressBar);
                    if(!ExeFS.PackExeFS(prepared.ToArray(),exefile))
                        throw new IOException("El motor ExeFS rechazó los archivos temporales.");
                    if(!File.Exists(exefile)||new FileInfo(exefile).Length<=0x200)
                        throw new IOException("ExeFS no produjo una salida válida.");
                    UiProgress.Count("ExeFS compilado",
                        kind==RomRebuildKind.ExeFS?1:2,kind==RomRebuildKind.ExeFS?2:4);
                }
                string produced=kind switch
                {
                    RomRebuildKind.RomFS=>romfile,
                    RomRebuildKind.ExeFS=>exefile,
                    _=>Build3ds(stage,root,exefile,romfile,kind==RomRebuildKind.Trimmed3DS)
                };
                UiProgress.Stage("Verificando integridad y preparando salida…");
                using var input=File.OpenRead(produced);
                string hash=Convert.ToHexString(SHA256.HashData(input));
                long size=input.Length;
                if(size<512)throw new InvalidDataException("El archivo reconstruido está incompleto.");
                input.Close();
                UiProgress.Count("Integridad SHA-256 verificada",kind is RomRebuildKind.ExeFS or RomRebuildKind.RomFS?2:4,
                    kind is RomRebuildKind.ExeFS or RomRebuildKind.RomFS?2:4);
                File.Move(produced,target);
                return(target,hash,size);
            });
        }
        finally
        {
            try{if(Directory.Exists(stage))Directory.Delete(stage,true);}catch(IOException){}
            BuildGate.Release();
        }
    }
    private static string Build3ds(string stage,string root,string exefs,string romfs,bool trimmed)
    {
        string? exheader=Directory.EnumerateFiles(root,"*",SearchOption.TopDirectoryOnly)
            .FirstOrDefault(x=>Path.GetFileName(x).Contains("exheader",StringComparison.OrdinalIgnoreCase)
                ||Path.GetExtension(x).Equals(".exh",StringComparison.OrdinalIgnoreCase));
        if(exheader is null)throw new FileNotFoundException(
            "Para Rebuild .3DS se requiere ExHeader extraído en la carpeta raíz.");
        UiProgress.Stage(trimmed?"Compilando Trimmed .3DS…":"Compilando .3DS completo…");
        var exh=new Exheader(exheader);
        string path=Path.Combine(stage,"result.3ds");
        if(!CTRUtil.BuildROM(true,"Nintendo",exefs,romfs,exheader,exh.GetSerial(),path,trimmed))
            throw new IOException("El motor original CTRUtil no completó la ROM.");
        UiProgress.Count("ROM .3DS empaquetada",3,4);
        return path;
    }
}
