using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;

internal enum RomRebuildKind { RomFS,ExeFS,Full3DS,Trimmed3DS }

/// <summary>Single-project rebuild using original CTR/RomFS/ExeFS algorithms.
/// Every intermediate file lives outside the source project; no writes in ROM folder.
/// </summary>
internal sealed class ExeFsBuildCache
{
    internal byte[]? Bytes;
    internal string? Signature;
    internal int CompressionCount;
}

internal sealed class GameRomRebuildService(GameProjectSession project, ExeFsBuildCache? sharedCache=null)
{
    private static readonly SemaphoreSlim BuildGate=new(1,1);
    private readonly ExeFsBuildCache _cache=sharedCache??project.RebuildExeFsCache;
    internal string? LastCleanupWarning { get; private set; }
    internal int ExeFsCompressionCount=>_cache.CompressionCount;
    private readonly Dictionary<string,double> _lastTimings=[];
    internal IReadOnlyDictionary<string,double> LastTimings=>new Dictionary<string,double>(_lastTimings);

    private static string ExeSignature(string folder)
    {
        return string.Join("\n",Directory.GetFiles(folder,"*",SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName,StringComparer.Ordinal).Select(path=>
        {using var stream=File.OpenRead(path);return Path.GetFileName(path)+":"+Convert.ToHexString(SHA256.HashData(stream));}));
    }
    internal async Task<(string File,string Hash,long Bytes)> BuildAsync(RomRebuildKind kind,string target,Action<string,int,int>? progress=null)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        string? lastPhase=null;double lastAt=0;
        void Report(string text,int value)
        {
            double now=watch.Elapsed.TotalMilliseconds;
            if(lastPhase is not null)_lastTimings[lastPhase]=now-lastAt;
            lastPhase=text;lastAt=now;UiProgress.Stage(text);progress?.Invoke(text,value,100);
        }
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
        _lastTimings.Clear();LastCleanupWarning=null;watch.Restart();
        string stage=Path.Combine(Path.GetDirectoryName(target)!,".definitive-rebuild-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            return await Task.Run(()=>
            {
                string romfs=project.GameConfiguration.RomFS;
                string exefs=project.GameConfiguration.ExeFS;
                string romfile=Path.Combine(stage,"romfs.bin");
                string exefile=Path.Combine(stage,"exefs.bin");
                string? exeSignature=null;
                if(!Directory.Exists(romfs))
                    throw new DirectoryNotFoundException("No se encontró la carpeta RomFS del proyecto: "+romfs);
                if(kind is not RomRebuildKind.RomFS && !Directory.Exists(exefs))
                    throw new DirectoryNotFoundException("No se encontró la carpeta ExeFS del proyecto: "+exefs);
                // Fail early rather than spending minutes rebuilding a large RomFS
                // before discovering that the .3ds input is not extractable.
                if(kind is RomRebuildKind.Full3DS or RomRebuildKind.Trimmed3DS)
                    _=LocateExheader(root);
                if(kind is RomRebuildKind.RomFS or RomRebuildKind.Full3DS or RomRebuildKind.Trimmed3DS)
                {
                    Report("RomFS · empaquetando archivos y generando hashes IVFC…",0);
                    // The original RomFS builder dereferences PB_Show.InvokeRequired
                    // unconditionally. In this WPF worker there is no WinForms progress
                    // bar unless we provide a lightweight, private instance. Omitting it
                    // produces NullReferenceException during the first metadata/data pass.
                    // Do not show a legacy window: only our dark WPF progress is visible.
                    using(var legacyProgress=CreateLegacyProgressBar())
                        RomFS.BuildRomFS(romfs,romfile,null,legacyProgress,(phase,elapsed)=>_lastTimings[phase]=elapsed.TotalMilliseconds);
                    if(!File.Exists(romfile)||new FileInfo(romfile).Length==0)
                        throw new IOException("RomFS no produjo una salida válida.");
                    UiProgress.Count("RomFS compilado",1,kind==RomRebuildKind.RomFS?2:4);
                }
                if(kind is RomRebuildKind.ExeFS or RomRebuildKind.Full3DS or RomRebuildKind.Trimmed3DS)
                {
                    Report("ExeFS · comprobando si el ejecutable cambió…",25);
                    string[] source=Directory.GetFiles(exefs, "*",SearchOption.TopDirectoryOnly);
                    if(source.Length is <1 or >10)
                        throw new InvalidDataException("ExeFS requiere entre 1 y 10 archivos.");
                    exeSignature=ExeSignature(exefs);
                    if(_cache.Bytes is not null&&_cache.Signature==exeSignature)
                    {
                        Report("ExeFS · reutilizando ejecutable idéntico ya comprimido…",25);
                        File.WriteAllBytes(exefile,_cache.Bytes);
                    }
                    else
                    {
                        string stageExe=Path.Combine(stage,"ExeFS-input");
                        Directory.CreateDirectory(stageExe);
                        var prepared=new List<string>(source.Length);
                        foreach(string path in source)
                        {
                            // Toolkit extractions use code.bin; ExeFS requires the entry name .code.
                            // Normalize only the staged copy, never the extracted project.
                            string name=Path.GetFileName(path);
                            if(new[]{"code.bin",".code.bin","code",".code"}.Contains(name,StringComparer.OrdinalIgnoreCase))name=".code.bin";
                            string copied=Path.Combine(stageExe,name);
                            if(File.Exists(copied))throw new InvalidDataException("ExeFS contiene dos archivos que se resuelven al mismo nombre: "+name);
                            File.Copy(path,copied);
                            prepared.Add(copied);
                        }
                        Report("ExeFS · comprimiendo code.bin (BLZ)…",25);
                        string? code=prepared.FirstOrDefault(path=>Path.GetFileName(path)
                            .Contains("code",StringComparison.OrdinalIgnoreCase));
                        if(code is null)throw new InvalidDataException("ExeFS necesita code.bin.");
                        using(var progressBar=CreateLegacyProgressBar())
                            _=new BLZCoder(["-en",code],progressBar,(value,max)=>progress?.Invoke("ExeFS · comprimiendo code.bin (BLZ)…",25+(int)(25L*value/Math.Max(1,max)),100));
                        if(!ExeFS.PackExeFS(prepared.ToArray(),exefile))
                            throw new IOException("El motor ExeFS rechazó los archivos temporales.");
                        if(!File.Exists(exefile)||new FileInfo(exefile).Length<=0x200)
                            throw new IOException("ExeFS no produjo una salida válida.");
                        if(ExeSignature(exefs)!=exeSignature)throw new IOException("ExeFS cambió durante la compilación; no se publicó la salida.");
                        _cache.Bytes=File.ReadAllBytes(exefile);_cache.Signature=exeSignature;_cache.CompressionCount++;
                    }
                    UiProgress.Count("ExeFS compilado",
                        kind==RomRebuildKind.ExeFS?1:2,kind==RomRebuildKind.ExeFS?2:4);
                }
                Report("Ensamblando contenedor de salida…",50);
                string produced=kind switch
                {
                    RomRebuildKind.RomFS=>romfile,
                    RomRebuildKind.ExeFS=>exefile,
                    _=>Build3ds(stage,root,exefile,romfile,kind==RomRebuildKind.Trimmed3DS)
                };
                Report("Verificando SHA-256 de la salida…",75);
                using var input=File.OpenRead(produced);
                string hash=Convert.ToHexString(SHA256.HashData(input));
                long size=input.Length;
                if(size<512)throw new InvalidDataException("El archivo reconstruido está incompleto.");
                input.Close();
                UiProgress.Count("Integridad SHA-256 verificada",kind is RomRebuildKind.ExeFS or RomRebuildKind.RomFS?2:4,
                    kind is RomRebuildKind.ExeFS or RomRebuildKind.RomFS?2:4);
                if(exeSignature is not null&&ExeSignature(exefs)!=exeSignature)throw new IOException("ExeFS cambió antes de publicar la salida.");
                File.Move(produced,target);
                Report("Reconstrucción e integridad completadas",100);
                _lastTimings["Total"]=watch.Elapsed.TotalMilliseconds;
                return(target,hash,size);
            });
        }
        finally
        {
            try
            {
                try{if(Directory.Exists(stage))Directory.Delete(stage,true);}
                catch(Exception ex)when(ex is IOException or UnauthorizedAccessException)
                {
                    // Cleanup must neither turn a published ROM into a failure nor
                    // mask the original build exception. Never hold the gate on cleanup failure.
                    LastCleanupWarning="No se pudo eliminar la carpeta temporal: "+stage+"\n"+ex.Message;
                }
            }
            finally{BuildGate.Release();}
        }
    }
    // CTRUtil.WriteROM calls ProgressBar.Invoke directly, even when the
    // .3DS build runs without a WinForms window. A newly constructed control
    // has no native handle and throws before the ROM data is written.
    // Create its handle on this same rebuild worker thread: Invoke then runs
    // synchronously and no legacy window or WinForms message loop is needed.
    internal static System.Windows.Forms.ProgressBar CreateLegacyProgressBar()
    {
        var progressBar=new System.Windows.Forms.ProgressBar();
        _=progressBar.Handle;
        return progressBar;
    }
    private static string LocateExheader(string root)
    {
        if(!Directory.Exists(root))
            throw new DirectoryNotFoundException("La carpeta raíz de la ROM no existe: "+root);
        string? path=Directory.EnumerateFiles(root,"*",SearchOption.TopDirectoryOnly)
            .FirstOrDefault(x=>Path.GetFileName(x).Contains("exheader",StringComparison.OrdinalIgnoreCase)
                ||Path.GetExtension(x).Equals(".exh",StringComparison.OrdinalIgnoreCase));
        if(path is null)throw new FileNotFoundException(
            "Para reconstruir un .3DS se requiere el ExHeader extraído en la carpeta raíz.");
        if(new FileInfo(path).Length<0x800)
            throw new InvalidDataException("El ExHeader está incompleto; se requieren al menos 0x800 bytes.");
        return path;
    }
    internal static string PrepareCompressedExheader(string source,string target)
    {
        byte[] header=File.ReadAllBytes(source);
        if(header.Length<0x800)throw new InvalidDataException("El ExHeader está incompleto.");
        // ExeFS is always BLZ-encoded above, including when reusing the cache.
        // Extraction tools can clear this bit after decompressing code.bin.
        header[0xD]|=1;
        using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        output.Write(header);
        return target;
    }
    private static string Build3ds(string stage,string root,string exefs,string romfs,bool trimmed)
    {
        string exheader=PrepareCompressedExheader(LocateExheader(root),Path.Combine(stage,"exheader-compressed.bin"));
        UiProgress.Stage(trimmed?"Compilando Trimmed .3DS…":"Compilando .3DS completo…");
        var exh=new Exheader(exheader);
        string path=Path.Combine(stage,"result.3ds");
        using var legacyProgress=CreateLegacyProgressBar();
        if(!CTRUtil.BuildROM(true,"Nintendo",exefs,romfs,exheader,exh.GetSerial(),path,trimmed,
                PB_Show:legacyProgress))
            throw new IOException("El motor original CTRUtil no completó la ROM.");
        UiProgress.Count("ROM .3DS empaquetada",3,4);
        return path;
    }
}
