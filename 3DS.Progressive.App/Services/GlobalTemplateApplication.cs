using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace ThreeDS.Progressive.Services;

internal sealed record GlobalPreviewFile(string RelativePath,string SourcePath,string ProposedPath,string BeforeSha256,string AfterSha256,long Bytes);
internal sealed class GlobalTemplatePreview:IDisposable
{
    internal string Workspace{get;}
    internal GlobalTemplateDocument Template{get;}
    internal IReadOnlyDictionary<string,string> SourceHashes{get;}
    internal string HistoryHash{get;}
    internal IReadOnlyList<GlobalPreviewFile> Files{get;}
    internal int Seed{get;}
    internal bool Applied{get;set;}
    internal GlobalTemplatePreview(string workspace,GlobalTemplateDocument template,IReadOnlyDictionary<string,string> sourceHashes,string historyHash,IReadOnlyList<GlobalPreviewFile> files,int seed)
        {Workspace=workspace;Template=template.Clone();SourceHashes=sourceHashes;HistoryHash=historyHash;Files=files;Seed=seed;}
    public void Dispose()
    {
        // Only our uniquely owned temporary workspace is removed; never the source project.
        string full=Path.GetFullPath(Workspace),prefix=Path.Combine(Path.GetTempPath(),"DR-global-preview-");
        if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return;
        try{if(Directory.Exists(full))Directory.Delete(full,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}

/// <summary>Preview is original Batch replay without assembly, applied once using frozen staged bytes.</summary>
internal sealed class GlobalTemplateApplication(GameProjectSession project)
{
    private static string HashFile(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    internal static Dictionary<string,string> Snapshot(GameProjectSession session)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string folder in new[]{session.GameConfiguration.RomFS,session.GameConfiguration.ExeFS})
        {
            if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Se necesitan RomFS y ExeFS.");
            void Scan(string directory)
            {
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("El proyecto contiene un enlace de directorio: "+directory);
                foreach(string file in Directory.EnumerateFiles(directory))
                {
                    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("El proyecto contiene un enlace de archivo.");
                    result.Add(Path.GetRelativePath(session.ProjectRoot,file),HashFile(file));
                }
                foreach(string child in Directory.EnumerateDirectories(directory))Scan(child);
            }
            Scan(folder);
        }
        foreach(string file in Directory.EnumerateFiles(session.ProjectRoot).Where(path=>Path.GetFileName(path).StartsWith("exheader",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path).StartsWith("decryptedexh",StringComparison.OrdinalIgnoreCase)))
        {
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("ExHeader no puede ser un enlace.");
            result.Add(Path.GetRelativePath(session.ProjectRoot,file),HashFile(file));
        }
        return result;
    }
    private string HistoryHash()=>ProjectHistory.Hash(JsonSerializer.SerializeToUtf8Bytes(project.History.Read()));
    internal async Task<GlobalTemplatePreview> PreviewAsync(GlobalTemplateDocument document,Action<string>? report=null)
    {
        string workspace=Path.Combine(Path.GetTempPath(),"DR-global-preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(workspace);
        try
        {
            var frozen=document.Clone();
            string recipe=Path.Combine(workspace,"recipe.json");
            // Save carries manual .assets while keeping their original verified hashes.
            frozen.Save(recipe);
            var roots=new[]{Path.GetDirectoryName(document.SourcePath)??"",AppContext.BaseDirectory,Path.GetDirectoryName(BatchRomExecution.RunnerPath)!,project.ProjectRoot};
            var folders=roots.SelectMany(root=>new[]{root,Path.Combine(root,"custom_balance_templates"),Path.Combine(root,"Templates")}).ToArray();
            foreach(string folder in new[]{"custom_balance_templates","Templates"})
            {
                string target=Path.Combine(workspace,folder);Directory.CreateDirectory(target);
                foreach(string sourceFolder in new[]{Path.Combine(Path.GetDirectoryName(BatchRomExecution.RunnerPath)!,folder),Path.Combine(AppContext.BaseDirectory,folder)}.Where(Directory.Exists))
                    foreach(string file in Directory.EnumerateFiles(sourceFolder))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
            }
            if(frozen.Section("Assets") is JsonArray assets)
                foreach(var entry in assets.OfType<JsonObject>())
                {
                    string name=entry["FileName"]?.GetValue<string>()??"";if(name.Length==0)continue;
                    string target=Path.GetFullPath(Path.Combine(workspace,name));
                    if(!target.StartsWith(workspace+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Asset fuera de la carpeta de receta.");
                    string? source=folders.Select(folder=>Path.Combine(folder,name)).FirstOrDefault(File.Exists);
                    if(source is null)throw new FileNotFoundException("No se encontró el asset: "+name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(source,target,true);
                    string? canonical=entry["Role"]?.GetValue<string>() switch
                    {
                        "move-balance"=>$"custom_balance_templates/moves_gen{project.Generation}.csv",
                        "evolution-balance"=>$"custom_balance_templates/evolutions_gen{project.Generation}.csv",
                        "pokemon-stats"=>$"custom_balance_templates/pokemon_stats_gen{project.Generation}.csv",
                        "trainer-held-items"=>"Templates/trainer_held_items.txt",
                        "trainer-better-movesets"=>"Templates/trainer_better_movesets.txt",
                        "field-items"=>"custom_balance_templates/field_items.txt",_=>null
                    };
                    if(canonical is not null&&!Path.GetFullPath(target).Equals(Path.GetFullPath(Path.Combine(workspace,canonical)),StringComparison.OrdinalIgnoreCase))File.Copy(target,Path.Combine(workspace,canonical),true);
                }
            var preflight=await new BatchRomExecution(project).PreflightAsync(new(recipe,workspace,"preview",1,false,false,true));
            var localWarnings=frozen.CheckAssets([workspace]);
            if(localWarnings.Count>0)throw new InvalidDataException(string.Join(" | ",localWarnings));
            // The generic Batch preflight prefers installed CSVs. This execution has a
            // dedicated frozen dependency root, verified above, so only those hash warnings differ.
            var blocking=preflight.Warnings.Where(warning=>!BatchRomExecution.IsHashMismatch(warning)).ToArray();
            if(blocking.Length>0)throw new InvalidDataException(string.Join(" | ",blocking));
            var hashes=await Task.Run(()=>Snapshot(project));string history=HistoryHash();
            await File.WriteAllTextAsync(Path.Combine(workspace,"request.json"),JsonSerializer.Serialize(new{ProjectRoot=project.ProjectRoot,TemplatePath=recipe,OutputDirectory=workspace,BaseName="preview",Count=1,RestoreBackups=false,KeepEditableProjects=true,PreviewOnly=true,Language=project.GameConfiguration.Language}));
            report?.Invoke("Ejecutando la receta sobre una copia temporal…");
            using var process=new Process{StartInfo=new ProcessStartInfo(BatchRomExecution.RunnerPath){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=workspace}};
            process.StartInfo.ArgumentList.Add("--batch-bridge");process.StartInfo.ArgumentList.Add(Path.Combine(workspace,"request.json"));
            process.StartInfo.Environment["DEFINITIVE_GLOBAL_PREVIEW_ROOT"]=workspace;
            process.StartInfo.Environment["TEMP"]=workspace;process.StartInfo.Environment["TMP"]=workspace;
            if(!process.Start())throw new InvalidOperationException("No se pudo iniciar el ejecutor de templates.");
            await process.WaitForExitAsync();
            string result=Path.Combine(workspace,"preview-result.json"),error=Path.Combine(workspace,"3ds-progressive-batch-error.txt");
            if(process.ExitCode!=0||!File.Exists(result))throw new InvalidDataException(File.Exists(error)?await File.ReadAllTextAsync(error):"El ejecutor no completó la vista previa.");
            using var data=JsonDocument.Parse(await File.ReadAllTextAsync(result));
            var actions=data.RootElement.GetProperty("Actions").EnumerateArray().Select(node=>node.GetString()!).ToArray();
            if(!actions.SequenceEqual(frozen.Actions))throw new InvalidDataException("Las acciones ejecutadas difieren de la receta.");
            return await Task.Run(()=>InspectStage(workspace,document,hashes,history,data.RootElement.GetProperty("Seed").GetInt32()));
        }
        catch{new GlobalTemplatePreview(workspace,document,new Dictionary<string,string>(),"",[],0).Dispose();throw;}
    }
    internal GlobalTemplatePreview InspectStage(string workspace,GlobalTemplateDocument document,IReadOnlyDictionary<string,string> hashes,string historyHash,int seed)
    {
        string stage=Path.Combine(workspace,"preview-project");var changes=new List<GlobalPreviewFile>();var known=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var (relative,hash) in hashes)
        {
            string target=Path.Combine(stage,relative);
            if(!File.Exists(target)&&Path.GetFileName(target).Equals("code.bin",StringComparison.OrdinalIgnoreCase))target=Path.Combine(Path.GetDirectoryName(target)!,".code.bin");
            if(!File.Exists(target))throw new InvalidDataException("La receta eliminó un archivo: "+relative);
            known.Add(Path.GetFullPath(target));string after=HashFile(target);
            if(after!=hash)changes.Add(new(relative,Path.Combine(project.ProjectRoot,relative),target,hash,after,new FileInfo(target).Length));
        }
        foreach(string directory in new[]{project.GameConfiguration.RomFS,project.GameConfiguration.ExeFS})
        {
            string copy=Path.Combine(stage,Path.GetRelativePath(project.ProjectRoot,directory));
            foreach(string file in Directory.EnumerateFiles(copy,"*",SearchOption.AllDirectories))
                if(!known.Contains(Path.GetFullPath(file)))throw new InvalidDataException("La receta añadió un recurso que necesita un instalador específico: "+Path.GetRelativePath(stage,file));
        }
        EnsureSourceUnchanged(hashes,historyHash);
        return new(workspace,document,hashes,historyHash,changes,seed);
    }
    private void EnsureSourceUnchanged(IReadOnlyDictionary<string,string> expected,string history)
    {
        var actual=Snapshot(project);
        if(actual.Count!=expected.Count||expected.Any(pair=>actual.GetValueOrDefault(pair.Key)!=pair.Value)||HistoryHash()!=history)
            throw new IOException("El proyecto o su historial cambiaron desde la vista previa. Genera una vista previa nueva.");
    }
    internal void Apply(GlobalTemplatePreview preview)
    {
        if(preview.Applied)throw new InvalidOperationException("Esta propuesta ya fue aplicada.");
        EnsureSourceUnchanged(preview.SourceHashes,preview.HistoryHash);
        var files=new List<FileChange>();
        foreach(var file in preview.Files)
        {
            if(HashFile(file.ProposedPath)!=file.AfterSha256)throw new IOException("La copia de vista previa cambió. Vuelve a previsualizar.");
            byte[] source=File.ReadAllBytes(file.SourcePath);
            if(ProjectHistory.Hash(source)!=file.BeforeSha256)throw new IOException("El archivo de origen cambió: "+file.RelativePath);
            files.Add(new(file.SourcePath,source,File.ReadAllBytes(file.ProposedPath)));
        }
        project.History.CommitGlobalTemplate(files,preview.Template,preview.Seed,preview.HistoryHash);preview.Applied=true;
    }
}
