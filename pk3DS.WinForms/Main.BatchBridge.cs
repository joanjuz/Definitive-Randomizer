using pk3DS.Core;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace pk3DS.WinForms;

// Headless adapter for the WPF Batch ROM Builder. Reuses the complete original
// Gen7 engine; never adds a second, reduced randomization implementation.
internal sealed class BatchBridgeRequest
{
    public string ProjectRoot {get;set;}=string.Empty;
    public string TemplatePath {get;set;}=string.Empty;
    public string OutputDirectory {get;set;}=string.Empty;
    public string BaseName {get;set;}="Randomized";
    public int Count {get;set;}=4;
    public bool Trimmed {get;set;}
    public bool RestoreBackups {get;set;}=true;
    public bool KeepEditableProjects {get;set;}
    public bool AllowAssetMismatch {get;set;}
    public bool PreviewOnly {get;set;}
    public int Language {get;set;}=2;
}


internal sealed class RelearnerBridgeRequest
{
    public string ProjectRoot{get;set;}=string.Empty;
    public string StageRoot{get;set;}=string.Empty;
}

public partial class Main
{
    internal async Task RunBatchBridge(string requestFile)
    {
        if(!File.Exists(requestFile))throw new FileNotFoundException("Batch request missing.",requestFile);
        var job=JsonSerializer.Deserialize<BatchBridgeRequest>(File.ReadAllText(requestFile))
            ??throw new InvalidDataException("Batch request is empty.");
        if(job.Count is <1 or >50)throw new InvalidDataException("Batch count must be 1–50.");
        if(!File.Exists(job.TemplatePath)||!Directory.Exists(job.ProjectRoot))
            throw new InvalidDataException("Project or global template is missing.");
        if(string.IsNullOrWhiteSpace(job.BaseName)||
            job.BaseName.Any(c=>Path.GetInvalidFileNameChars().Contains(c))||
            job.BaseName.Contains(".."))
            throw new InvalidDataException("Unsafe output base name.");
        string source=Path.GetFullPath(job.ProjectRoot);
        string output=Path.GetFullPath(job.OutputDirectory);
        if(output.Equals(source,StringComparison.OrdinalIgnoreCase)||
           output.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,
               StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Batch output cannot be inside the source project.");
        if(Enumerable.Range(1,job.Count).Any(i=>File.Exists(Path.Combine(output,
            $"{job.BaseName}_{i:00}.3ds"))))
            throw new IOException("Target .3ds already exists; refusing silent overwrite.");
        if(job.PreviewOnly)
        {
            Directory.CreateDirectory(output);
            using var previewRuntime=BatchRuntime.Begin(message=>File.AppendAllText(Path.Combine(output,"preview-log.txt"),message+Environment.NewLine));
            if(job.Language<0||job.Language>9)throw new InvalidDataException("Idioma no válido.");
            CB_Lang.SelectedIndex=job.Language;
            OpenQuick(source);
            if(Config is null||Config.Generation is not (6 or 7))throw new InvalidDataException("Proyecto incompatible.");
            RunGlobalTemplatePreview(job);return;
        }
        OpenQuick(source);
        if(Config is null||Config.Generation is not (6 or 7))
            throw new InvalidDataException("Batch source must be a fully extracted XY/ORAS/SM/USUM game.");
        // In the original builder the loading UI and ROM replay share the exact
        // same backend path. Only the dialog is replaced by WPF configuration.
        await ShowBatchRomBuilder(new BatchRomBuildOptions
        {
            TemplatePath=Path.GetFullPath(job.TemplatePath),
            OutputDirectory=output,
            BaseName=job.BaseName,
            Count=job.Count,
            Trimmed=job.Trimmed,
            RestoreBackups=job.RestoreBackups,
            KeepEditableProjects=job.KeepEditableProjects,
            AllowAssetMismatch=job.AllowAssetMismatch
        });
        string report=Path.Combine(output,job.BaseName+"_batch.txt");
        if(!File.Exists(report)||!File.ReadAllText(report).Contains("Completed:"))
            throw new IOException("Original batch executor did not report successful completion.");
    }
    private void RunGlobalTemplatePreview(BatchBridgeRequest job)
    {
        string source=Path.GetFullPath(job.ProjectRoot),output=Path.GetFullPath(job.OutputDirectory);
        string stage=Path.Combine(output,"preview-project");
        if(Directory.Exists(stage)||File.Exists(stage))throw new IOException("La copia de vista previa ya existe.");
        var template=GlobalRandomizationTemplateFile.Load(job.TemplatePath,CurrentGlobalTemplateGame);
        int generation=Config.Generation;string game=CurrentGlobalTemplateGame;
        if(template.Generation!=generation)throw new InvalidDataException("Generación incompatible.");
        var unsupported=generation==6?BatchGen6ActionExecutor.GetUnsupported(template.Actions):BatchGen7ActionExecutor.GetUnsupported(template.Actions);
        if(unsupported.Length>0)throw new NotSupportedException(string.Join(", ",unsupported));
        BatchWorkspace.PrepareCleanCopy(source,stage,Config,ExHeaderPath,false);
        {
            OpenQuick(stage);
            if(Config.Generation!=generation||CurrentGlobalTemplateGame!=game)throw new InvalidDataException("La copia no corresponde al juego original.");
            int seed=System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue);
            Util.ReseedRand(seed);
            string previous=Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY"),previousTemplate=Environment.GetEnvironmentVariable("DEFINITIVE_TEMPLATE_PATH");
            Environment.SetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY","1");Environment.SetEnvironmentVariable("DEFINITIVE_TEMPLATE_PATH",job.TemplatePath);
            try{var warnings=GlobalRandomizationTemplateFile.Apply(template,game);if(warnings.Count>0)throw new InvalidDataException(string.Join(" | ",warnings));if(generation==6)BatchGen6ActionExecutor.Execute(template);else BatchGen7ActionExecutor.Execute(template);}
            finally{Environment.SetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY",previous);Environment.SetEnvironmentVariable("DEFINITIVE_TEMPLATE_PATH",previousTemplate);}
            // This bridge deliberately stops before ROM assembly. The exact staged bytes are the proposal.
            File.WriteAllText(Path.Combine(output,"preview-result.json"),JsonSerializer.Serialize(new{Seed=seed,Actions=template.Actions.Select(a=>a.Id).ToArray()}));
        }
    }
    internal void RunRelearnerPreviewBridge(string requestFile)
    {
        var req=JsonSerializer.Deserialize<RelearnerBridgeRequest>(File.ReadAllText(requestFile))
            ??throw new InvalidDataException("Solicitud Relearner vacía.");
        string source=Path.GetFullPath(req.ProjectRoot);
        string stage=Path.GetFullPath(req.StageRoot);
        if(!Directory.Exists(source)||
            stage.Equals(source,StringComparison.OrdinalIgnoreCase)||
            stage.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Carpeta de staging insegura.");
        OpenQuick(source);
        if(Config?.USUM!=true||string.IsNullOrWhiteSpace(ExHeaderPath))
            throw new InvalidDataException("Se necesita una extracción USUM con RomFS, ExeFS y ExHeader.");
        var config=Config;
        string exHeader=ExHeaderPath;
        BatchWorkspace.PrepareCleanCopy(source,stage,config,exHeader,false);
        OpenQuick(stage);
        if(Config?.USUM!=true)throw new InvalidDataException("La copia no se identificó como USUM.");
        string report=USUMMoveRelearnerPatcher.Apply(ExeFSPath,Config);
        File.WriteAllText(Path.Combine(stage,"relearner-preview-report.txt"),report);
    }

}
