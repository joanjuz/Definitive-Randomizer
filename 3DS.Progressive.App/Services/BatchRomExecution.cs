using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

internal sealed record RomBatchOptions(string TemplatePath,string OutputDirectory,string BaseName,
    int Count,bool Trimmed,bool RestoreBackups,bool KeepEditableProjects);

internal sealed record RomBuildArtifact(int Index,string File,string Sha256,long Bytes,int? Seed);
internal sealed record RomBuildJournal(DateTimeOffset CompletedUtc,string Project,
    string Template,string TemplateSha256,IReadOnlyList<string> ActionIds,
    bool Trimmed,bool RestoredBackups,IReadOnlyList<RomBuildArtifact> Roms,
    string LogFile);

/// <summary>WPF facade for the original Gen7 WinForms batch replay/build engine.
/// Every ROM is staged separately: source is never randomized directly.</summary>
internal sealed class BatchRomExecution
{
    private readonly GameProjectSession _project;
    private readonly string _game;
    internal static string RunnerPath=>Path.Combine(AppContext.BaseDirectory,
        "LegacyBatch","pk3DS.WinForms.exe");
    internal bool BackendAvailable=>File.Exists(RunnerPath);
    internal BatchRomExecution(GameProjectSession project)
    {
        _project=project;
        var config=project.GameConfiguration;
        _game=config.USUM?"USUM":config.SM?"SM":config.ORAS?"ORAS":"XY";
    }
    internal (GlobalTemplateDocument Template,IReadOnlyList<string> Warnings) Preflight(RomBatchOptions options)
    {
        if(_project.Generation!=7)
            throw new NotSupportedException("Batch ROM Builder original actualmente solo permite generación 7.");
        if(options.Count is <1 or >50)throw new ArgumentOutOfRangeException(nameof(options.Count),
            "Se admiten de 1 a 50 ROMs por lote.");
        if(options.BaseName.Length==0||options.BaseName.Contains("..")||
            options.BaseName.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
            throw new ArgumentException("Nombre de salida no válido.");
        string target=Path.GetFullPath(options.OutputDirectory);
        string root=Path.GetFullPath(_project.ProjectRoot);
        if(target.Equals(root,StringComparison.OrdinalIgnoreCase)||
            target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El destino no puede estar dentro de la ROM de origen.");
        var template=GlobalTemplateDocument.Load(options.TemplatePath,_game,7);
        if(template.Actions.Count==0)throw new InvalidDataException("El template no tiene acciones de batch.");
        if(template.Coverage<2)
            throw new InvalidDataException("ActionCoverageVersion insuficiente para replay automatizado.");
        foreach(int index in Enumerable.Range(1,options.Count))
        {
            if(File.Exists(Path.Combine(target,$"{options.BaseName}_{index:00}.3ds")))
                throw new IOException("Ya existe el ROM destino: "+options.BaseName+"_"+index.ToString("00")+".3ds");
        }
        var dirs=new[]{AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory,"custom_balance_templates"),
            Path.Combine(Path.GetDirectoryName(RunnerPath)??"","custom_balance_templates"),
            Path.GetDirectoryName(options.TemplatePath)??""};
        var warnings=template.CheckAssets(dirs).ToList();
        var exe=_project.GameConfiguration.ExeFS;
        var rom=_project.GameConfiguration.RomFS;
        if(!Directory.Exists(exe)||!Directory.Exists(rom))
            warnings.Add("La extracción debe incluir RomFS y ExeFS.");
        // The actual ExHeader is validated by the legacy engine on execution.
        if(!BackendAvailable)warnings.Add("El ejecutor original pk3DS.WinForms no se incluyó en este paquete.");
        return(template,warnings);
    }
    internal async Task<RomBuildJournal> RunAsync(RomBatchOptions options,Action<string> progress)
    {
        var (template,warnings)=Preflight(options);
        if(warnings.Count>0)throw new InvalidDataException("Hay requisitos sin resolver: "+
            string.Join(" | ",warnings));
        Directory.CreateDirectory(options.OutputDirectory);
        string requestPath=Path.Combine(Path.GetTempPath(),"progressive-batch-"+
            Guid.NewGuid().ToString("N")+".json");
        string log=Path.Combine(options.OutputDirectory,options.BaseName+"_batch.txt");
        if(File.Exists(log))throw new IOException("Ya existe el log de este lote. Elegí otro nombre de salida.");
        var request=new
        {
            ProjectRoot=_project.ProjectRoot,
            TemplatePath=Path.GetFullPath(options.TemplatePath),
            OutputDirectory=Path.GetFullPath(options.OutputDirectory),
            options.BaseName,options.Count,options.Trimmed,options.RestoreBackups,
            options.KeepEditableProjects
        };
        await File.WriteAllTextAsync(requestPath,JsonSerializer.Serialize(request));
        try
        {
            using var process=new Process
            {
                StartInfo=new ProcessStartInfo(RunnerPath)
                {
                    UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                    Arguments="--batch-bridge "+Quote(requestPath)
                }
            };
            if(!process.Start())throw new InvalidOperationException("No se pudo iniciar el constructor original.");
            string last="";
            while(!process.HasExited)
            {
                await Task.Delay(950);
                if(!File.Exists(log))continue;
                string[] lines=await File.ReadAllLinesAsync(log);
                var current=lines.LastOrDefault(line=>line.StartsWith("ROM ",StringComparison.Ordinal))
                    ??"Preparando y randomizando lotes…";
                if(current!=last){progress(current);last=current;}
            }
            await process.WaitForExitAsync();
            if(process.ExitCode!=0)
            {
                string failure=Path.Combine(options.OutputDirectory,"3ds-progressive-batch-error.txt");
                throw new IOException("Batch abortado por el motor original. "+
                    (File.Exists(failure)?File.ReadAllText(failure):"Revisá el log "+log));
            }
            if(!File.Exists(log))throw new IOException("No se generó el informe del batch.");
            var lines=File.ReadAllLines(log);
            var seeds=lines.Where(l=>l.StartsWith("Seed:",StringComparison.Ordinal))
                .Select(l=>int.TryParse(l["Seed:".Length..].Trim(),out var n)?(int?)n:null).ToArray();
            var outputs=new List<RomBuildArtifact>();
            for(int i=1;i<=options.Count;i++)
            {
                string path=Path.Combine(options.OutputDirectory,$"{options.BaseName}_{i:00}.3ds");
                if(!File.Exists(path)||new FileInfo(path).Length<512)
                    throw new IOException("Falta un ROM final o su tamaño es inválido: "+path);
                outputs.Add(new(i,path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                    new FileInfo(path).Length,i-1<seeds.Length?seeds[i-1]:null));
            }
            var document=new RomBuildJournal(DateTimeOffset.UtcNow,_project.ProjectRoot,
                Path.GetFullPath(options.TemplatePath),template.Sha256,template.Actions,
                options.Trimmed,options.RestoreBackups,outputs,log);
            // Append-only manifest for batch history, separate from source RomFS.
            string manifest=Path.Combine(options.OutputDirectory,options.BaseName+"_batch-manifest.json");
            File.WriteAllText(manifest,JsonSerializer.Serialize(document,new JsonSerializerOptions{WriteIndented=true}));
            return document;
        }
        finally
        {
            try{File.Delete(requestPath);}catch(IOException){}
        }
    }
    private static string Quote(string value)=>"\""+value.Replace("\"","\\\"")+"\"";
}
