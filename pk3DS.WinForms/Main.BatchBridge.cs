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
        OpenQuick(source);
        if(Config is null||Config.Generation!=7)
            throw new InvalidDataException("Batch source must be a fully extracted Gen7 game.");
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
            KeepEditableProjects=job.KeepEditableProjects
        });
        string report=Path.Combine(output,job.BaseName+"_batch.txt");
        if(!File.Exists(report)||!File.ReadAllText(report).Contains("Completed:"))
            throw new IOException("Original batch executor did not report successful completion.");
    }
}
