using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

/// <summary>Prepare the ORIGINAL USUM relearner implementation in a separate, clean
/// copy and merge only the two validated binary changes into WPF ProjectHistory.</summary>
internal sealed record RelearnerFileProposal(string Path,byte[] Before,byte[] After);
internal sealed record RelearnerPreview(IReadOnlyList<RelearnerFileProposal> Files,string Report);
internal sealed class GameUsumRelearnerService(GameProjectSession project)
{
    internal bool Supported=>project.GameConfiguration.USUM;
    private static string RunnerPath=>BatchRomExecution.RunnerPath;
    internal async Task<RelearnerPreview> PrepareAsync(Action<string> progress)
    {
        if(!Supported)throw new NotSupportedException("USUM Relearner solo admite Ultra Sun y Ultra Moon.");
        if(!File.Exists(RunnerPath))
            throw new FileNotFoundException("Falta LegacyBatch/pk3DS.WinForms.exe.",RunnerPath);
        string stage=Path.Combine(Path.GetTempPath(),"progressive-relearner-"+Guid.NewGuid().ToString("N"));
        string request=Path.Combine(Path.GetTempPath(),"progressive-relearner-job-"+Guid.NewGuid().ToString("N")+".json");
        string root=Path.GetFullPath(project.ProjectRoot);
        await File.WriteAllTextAsync(request,JsonSerializer.Serialize(new{ProjectRoot=root,StageRoot=stage}));
        try
        {
            progress("Preparando copia limpia RomFS/ExeFS en segundo plano…");
            using var process=new Process{StartInfo=new ProcessStartInfo(RunnerPath)
            {
                UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                ArgumentList={"--relearner-preview",request}
            }};
            if(!process.Start())throw new InvalidOperationException("No se pudo ejecutar el parcheador original.");
            await process.WaitForExitAsync();
            if(process.ExitCode!=0)
            {
                string error=Path.Combine(stage,"relearner-preview-error.txt");
                throw new InvalidDataException(File.Exists(error)?File.ReadAllText(error):
                    "Falló el parcheador original en la copia de trabajo.");
            }
            string reportPath=Path.Combine(stage,"relearner-preview-report.txt");
            if(!File.Exists(reportPath))throw new IOException("El parcheador no confirmó su resultado.");
            string rom=project.GameConfiguration.RomFS;
            string exe=project.GameConfiguration.ExeFS;
            string story=Path.Combine(rom,project.GameConfiguration.GetGARCFileName("storytext"));
            var sourcePaths=new List<string>{story};
            var code=new[]{Path.Combine(exe,".code.bin"),Path.Combine(exe,"code.bin")}
                .FirstOrDefault(File.Exists) ??
                Directory.GetFiles(exe).FirstOrDefault(p=>Path.GetFileName(p)
                    .Contains("code",StringComparison.OrdinalIgnoreCase));
            if(code is null)throw new FileNotFoundException("No se encontró code.bin de USUM.");
            sourcePaths.Add(code);
            var changes=new List<RelearnerFileProposal>();
            foreach(string source in sourcePaths)
            {
                if(!File.Exists(source))throw new FileNotFoundException("Falta archivo original.",source);
                string relative=Path.GetRelativePath(root,source);
                if(relative.StartsWith("..",StringComparison.Ordinal)||
                    Path.IsPathRooted(relative))throw new InvalidDataException("Ruta fuera del proyecto: "+source);
                string staged=Path.Combine(stage,relative);
                if(!File.Exists(staged))throw new FileNotFoundException("El parche no generó "+relative,staged);
                byte[] before=await File.ReadAllBytesAsync(source);
                byte[] after=await File.ReadAllBytesAsync(staged);
                if(!before.AsSpan().SequenceEqual(after))
                    changes.Add(new(source,before,after));
            }
            return new(changes,File.ReadAllText(reportPath));
        }
        finally
        {
            try{File.Delete(request);}catch(IOException){}
            try{if(Directory.Exists(stage))Directory.Delete(stage,true);}catch(IOException){}
        }
    }
    internal void Apply(RelearnerPreview preview)
    {
        if(!Supported)throw new NotSupportedException("USUM Relearner únicamente USUM.");
        foreach(var file in preview.Files)
        {
            if(!File.Exists(file.Path)||!SHA256.HashData(File.ReadAllBytes(file.Path))
                .SequenceEqual(SHA256.HashData(file.Before)))
                throw new IOException("Un archivo cambió después de la vista previa: "+file.Path);
        }
        project.History.Commit(preview.Files.Select(p=>new FileChange(p.Path,p.Before,p.After)).ToArray(),
            "Mecánicas/CRO","Aplicar USUM Relearner",[],
            new Dictionary<string,string>
            {
                ["ActionId"]="usum.move-relearner",
                ["ModifiedFiles"]=string.Join(",",preview.Files.Select(p=>Path.GetFileName(p.Path))),
                ["OriginalEngine"]="USUMMoveRelearnerPatcher"
            });
    }
}
