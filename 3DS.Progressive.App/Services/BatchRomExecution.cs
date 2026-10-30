using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

internal sealed record RomBatchOptions(string TemplatePath,string OutputDirectory,string BaseName,
    int Count,bool Trimmed,bool RestoreBackups,bool KeepEditableProjects,bool AllowAssetMismatch=false);

internal sealed record BatchProgressSnapshot
{
    public string Status{get;init;}="";
    public string Stage{get;init;}="";
    public string Detail{get;init;}="";
    public int CurrentRom{get;init;}
    public int TotalRoms{get;init;}
    public int CompletedRoms{get;init;}
    public int Seed{get;init;}
    public string Output{get;init;}="";
    public int ActionsDone{get;init;}
    public int ActionsTotal{get;init;}
    public int? RebuildPercent{get;init;}
    public double CurrentPercent{get;init;}
    public double OverallPercent{get;init;}
    public string ProgressKind{get;init;}="";
}
internal sealed record RomBuildArtifact(int Index,string File,string Sha256,long Bytes,int? Seed);
internal sealed record RomBuildJournal(DateTimeOffset CompletedUtc,string Project,
    string Template,string TemplateSha256,IReadOnlyList<string> ActionIds,
    bool Trimmed,bool RestoredBackups,IReadOnlyList<RomBuildArtifact> Roms,
    string LogFile);

/// <summary>WPF facade for the original Gen7 WinForms batch replay/build engine.
/// Every ROM is staged separately: source is never randomized directly.</summary>
internal sealed class BatchRomExecution
{
    // Parity with BatchGen7ActionExecutor.Supported from the original executable.
    private static readonly HashSet<string> SupportedActionIds=new(StringComparer.OrdinalIgnoreCase)
    {
        "manual-edits.apply","rng.seed","moves.balance","moves.randomize","moves.metronome",
        "personal.randomize","personal.modify-all","pokemon-stats.apply",
        "personal.ban-trapping-abilities","evolutions.normalize","evolutions.randomize",
        "evolutions.remove-trade","evolutions.every-level","level-up-moves.randomize",
        "level-up-moves.metronome","egg-moves.randomize",
        "tms.randomize","tms.sanity","tms.follow-evolutions","trainers.randomize",
        "wild-encounters.randomize","wild-encounters.progressive",
        "wild-encounters.scale-levels","wild-encounters.copy-sos",
        "wild-encounters.modify-levels","starters.randomize",
        "static-encounters.randomize","static-encounters.modify-levels",
        "static-encounters.totem-level-caps","static-encounters.totem-bst",
        "trades.accept-any","trades.randomize-offers","trades.hide-species-names",
        "pickup.randomize","items.fix-economy","marts.randomize","marts.randomize-bp",
        "marts.expanded-layout","marts.add-rare-candies","marts.add-ev-items",
        "marts.free-mega-stones","marts.ban-ability-capsule","marts.single-purchase-specials",
        "move-tutors.randomize","move-tutors.sanity","move-tutors.follow-evolutions",
        "move-tutors.free","field-items.randomize","shiny-rate.apply",
        "mega-evolution.unlock-from-start","battle.persistent-consumables",
        "usum.move-relearner","player.level-caps"
    };
    private readonly GameProjectSession _project;
    internal static readonly HashSet<string> Gen6ActionIds=new(StringComparer.OrdinalIgnoreCase)
    {"manual-edits.apply","rng.seed","moves.balance","moves.randomize","moves.metronome","personal.randomize","personal.modify-all","pokemon-stats.apply",
     "evolutions.randomize","evolutions.every-level","evolutions.remove-trade","evolutions.normalize","level-up-moves.randomize","level-up-moves.metronome","egg-moves.randomize","trades.accept-any","trades.randomize-offers","trainers.randomize","static-encounters.randomize","static-encounters.modify-levels","starters.randomize","tms.randomize","field-items.randomize","marts.randomize","marts.add-rare-candies","marts.add-ev-items","marts.ban-ability-capsule","items.fix-economy","pickup.randomize","wild-encounters.randomize","wild-encounters.modify-levels","wild-encounters.scale-levels","tms.sanity","tms.follow-evolutions","catchzones.enable","shiny-rate.apply","move-tutors.randomize","move-tutors.sanity","move-tutors.follow-evolutions"};
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
        if(_project.Generation is not (6 or 7))throw new NotSupportedException("Batch requiere Gen6 o Gen7.");
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
        var template=GlobalTemplateDocument.Load(options.TemplatePath,_game,_project.Generation);
        ProjectFeatureAvailability.ValidateActions(_project,template.Actions);
        GlobalEditorTemplateBridge.ValidateBatchReplay(template);
        if(_project.Generation==6)NativeBatchGen6Bridge.ValidateSession(_project,template);
        else NativeBatchGen7StaticBridge.ValidateSession(_project,template);
        NativeGen7WildBridge.ValidateSession(_project,template);NativeRecipeCompletion.ValidateGen7Parameters(_project,template);_=NativeGen7MartBridge.ValidateSession(_project,template);NativeGen7MachineBridge.ValidateSession(_project,template);NativeMasterBatchBridge.ValidateSession(_project,template);if(_project.Generation==7)NativeGen7FieldBridge.ValidateSession(_project,template);
        ManualEditTemplate.ValidateSession(_project,template);
        if(template.Actions.Count==0)throw new InvalidDataException("El template no tiene acciones de batch.");
        if(template.SettingsCount==0&&template.Actions.Any(id=>id!="rng.seed"&&id!=ManualEditTemplate.ActionId&&!NativeMasterBatchBridge.ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase)&&!(_project.Generation==7&&NativeGen7FieldBridge.ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase))&&!(_project.Generation==7&&NativeGen7MachineBridge.ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase))&&!(_project.Generation==7&&NativeGen7MartBridge.ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase))&&!(_project.Generation==7&&NativeRecipeCompletion.Gen7ParameterActions.Contains(id,StringComparer.OrdinalIgnoreCase))&&!(_project.Generation==7&&NativeGen7WildBridge.ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase))&&!(_project.Generation==6?NativeBatchGen6Bridge.ActionIds:NativeBatchGen7StaticBridge.ActionIds).Contains(id,StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Este template se creó solo desde historial: no contiene RandSettings. Importá y guardá una receta completa antes de construir.");
        var supported=_project.Generation==6?Gen6ActionIds:SupportedActionIds;
        var unsupported=template.Actions.Where(id=>!supported.Contains(id)&&id!=ManualEditTemplate.ActionId).ToArray();
        if(unsupported.Length>0)
            throw new NotSupportedException("El ejecutor original no admite estas acciones para este juego: "+
                string.Join(", ",unsupported));
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
    internal Task<(GlobalTemplateDocument Template,IReadOnlyList<string> Warnings)> PreflightAsync(RomBatchOptions options)=>Task.Run(()=>
    {
        var result=Preflight(options);
        // Large frozen archive proposals are only needed for validation. Reclaim them
        // before an idle UI and a separate builder otherwise retain both heaps at once.
        GC.Collect(GC.MaxGeneration,GCCollectionMode.Aggressive,blocking:true,compacting:true);
        return result;
    });
    internal static bool IsHashMismatch(string warning)=>
        warning.StartsWith("SHA-256 distinto en ",StringComparison.Ordinal);
    internal async Task<RomBuildJournal> RunAsync(RomBatchOptions options,Action<BatchProgressSnapshot> progress)
    {
        var (template,warnings)=await PreflightAsync(options);
        var blocking=warnings.Where(w=>!IsHashMismatch(w)).ToArray();
        if(blocking.Length>0||warnings.Any(IsHashMismatch)&&!options.AllowAssetMismatch)
            throw new InvalidDataException("Hay requisitos sin resolver: "+
                string.Join(" | ",blocking.Length>0?blocking:warnings));
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
            options.KeepEditableProjects,options.AllowAssetMismatch
        };
        await File.WriteAllTextAsync(requestPath,JsonSerializer.Serialize(request));
        string? scratchToClean=null;
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
            // Keep large GARC/codec scratch files on the selected output drive, not the system drive.
            string scratch=Path.Combine(options.OutputDirectory,".definitive-temp-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);scratchToClean=Path.GetFullPath(scratch);
            process.StartInfo.Environment["TEMP"]=scratch;process.StartInfo.Environment["TMP"]=scratch;
            if(!process.Start())throw new InvalidOperationException("No se pudo iniciar el constructor original.");
            string progressFile=Path.Combine(options.OutputDirectory,
                options.BaseName+"_batch-progress.json");
            string last="";
            async Task Poll()
            {
                if(!File.Exists(progressFile))return;
                try
                {
                    using var snapshotStream=new FileStream(progressFile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,4096,true);
                    using var reader=new StreamReader(snapshotStream);
                    string json=await reader.ReadToEndAsync();
                    if(json==last)return;
                    var snapshot=JsonSerializer.Deserialize<BatchProgressSnapshot>(json);
                    if(snapshot is not null){last=json;progress(snapshot);}
                }
                catch(IOException){} // atomic replace during write
                catch(JsonException){} // ignore incomplete old snapshot
            }
            while(!process.HasExited)
            {
                await Task.Delay(400);
                await Poll();
            }
            await process.WaitForExitAsync();
            await Poll();
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
                using var outputStream=File.OpenRead(path);
                string hash=Convert.ToHexString(SHA256.HashData(outputStream));
                outputs.Add(new(i,path,hash,new FileInfo(path).Length,
                    i-1<seeds.Length?seeds[i-1]:null));
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
            if(scratchToClean is not null&&Path.GetDirectoryName(scratchToClean)==Path.GetFullPath(options.OutputDirectory)&&Path.GetFileName(scratchToClean).StartsWith(".definitive-temp-",StringComparison.Ordinal))
                try{if(Directory.Exists(scratchToClean))Directory.Delete(scratchToClean,true);}catch(IOException){}catch(UnauthorizedAccessException){}

        }
    }
    private static string Quote(string value)=>"\""+value.Replace("\"","\\\"")+"\"";
}
