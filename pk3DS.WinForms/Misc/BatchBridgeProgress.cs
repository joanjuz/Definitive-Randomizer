using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace pk3DS.WinForms;

/// <summary>
/// Side-channel progress for WPF: completed operations are counted (not timed),
/// and the original CTR rebuild progress is used for its stage.
/// Never lies about an ETA or interprets elapsed time as completed work.
/// </summary>
internal static class BatchBridgeProgress
{
    private static string path;
    private static int totalRoms,currentRom,completedRoms,seed,actionsTotal,actionsDone;
    private static string stage="",details="",output="";
    private static readonly HashSet<string> Counted = new(StringComparer.OrdinalIgnoreCase);
    private static double rebuildFraction;
    private static bool rebuilding;

    internal static void Start(string directory,string baseName,int total,int actions)
    {
        path=Path.Combine(directory,baseName+"_batch-progress.json");
        totalRoms=Math.Max(1,total);
        actionsTotal=Math.Max(0,actions);
        currentRom=0;completedRoms=0;actionsDone=0;
        stage="Preparando lote";details="Leyendo receta y preparando copia de trabajo";
        Write();
    }
    internal static void BeginRom(int index,int newSeed,string filename)
    {
        if(path is null)return;
        currentRom=index;seed=newSeed;output=filename;actionsDone=0;completedPhase=0;
        rebuildFraction=0;rebuilding=false;Counted.Clear();
        Stage("Preparar copia", "Creando copia limpia de la ROM",0);
    }
    internal static void Stage(string name,string detail,int actionOffset=0)
    {
        if(path is null)return;
        stage=name;details=detail;rebuilding=false;Write();
    }
    internal static void Step(string name,string detail)
    {
        if(path is null)return;
        stage=name;details=detail;
        // The 5 fixed phases: copy, reopen, template, rebuild, verify/finish.
        if(name=="Copia completa")completedPhase=1;
        else if(name=="Juego cargado")completedPhase=2;
        else if(name=="Template aplicado")completedPhase=3;
        Write();
    }
    private static int completedPhase;
    internal static void ActionGroup(string title,IEnumerable<string> actionIds,
        Func<string,bool> predicate)
    {
        if(path is null)return;
        foreach(var id in actionIds.Where(predicate))
            if(Counted.Add(id))actionsDone++;
        stage="Randomización · "+title;
        details=$"{actionsDone} de {actionsTotal} acciones de receta finalizadas";
        Write();
    }
    internal static void BeginRebuild(string filename)
    {
        if(path is null)return;
        // Actions have finished only if the batch engine reached ROM assembly.
        actionsDone=actionsTotal;
        rebuilding=true;rebuildFraction=0;
        stage="Reconstruyendo ROM";details=filename;
        Write();
    }
    internal static void RebuildProgress(int value,int max)
    {
        if(path is null||!rebuilding||max<=0)return;
        double next=Math.Clamp((double)value/max,0,1);
        if(next<rebuildFraction||Math.Abs(next-rebuildFraction)<0.01)return;
        rebuildFraction=next;Write();
    }
    internal static void RebuildStage(string name,int value,int max)
    {
        if(path is null||!rebuilding||max<=0)return;
        double next=Math.Clamp((double)value/max,0,1);
        if(name==stage && next-rebuildFraction<0.01)return;
        stage=name;details=output;rebuildFraction=Math.Max(rebuildFraction,next);Write();
    }
    internal static void EndRebuild()
    {
        if(path is null)return;
        rebuilding=false;rebuildFraction=1;completedPhase=4;
        stage="Reconstrucción completa";details="Verificando salida .3ds";Write();
    }
    internal static void CompleteRom(int index)
    {
        if(path is null)return;
        completedRoms=index;completedPhase=5;
        stage="ROM completada";details=output;Write();
    }
    internal static void Failure(string reason)
    {
        if(path is null)return;
        stage="Error";details=reason;Write("failed");
    }
    internal static void End(bool success)
    {
        if(path is null)return;
        stage=success?"Lote terminado":"Lote interrumpido";
        details=success?$"{completedRoms} de {totalRoms} ROMs verificadas":details;
        Write(success?"completed":"stopped");
    }
    private static void Write(string status="running")
    {
        if(path is null)return;
        int totalUnits=actionsTotal+5;
        double units=completedPhase+actionsDone+(rebuilding?rebuildFraction:0);
        // Count only work units genuinely completed. Do not use wall-clock
        // progress; long ROM assembly displays its real CTR progress separately.
        double currentPercent=currentRom<=0?0:
            Math.Clamp(100*units/totalUnits,0,100);
        double overall=Math.Clamp(100*(completedRoms+
            (currentRom>completedRoms?currentPercent/100:0))/totalRoms,0,100);
        var data=new
        {
            Status=status,Stage=stage,Detail=details,
            CurrentRom=currentRom,TotalRoms=totalRoms,CompletedRoms=completedRoms,
            Seed=seed,Output=output,
            ActionsDone=actionsDone,ActionsTotal=actionsTotal,
            RebuildPercent=rebuilding?(int?)Math.Round(rebuildFraction*100):null,
            CurrentPercent=Math.Round(currentPercent,1),
            OverallPercent=Math.Round(overall,1),
            ProgressKind="pasos verificados (no estimación de tiempo)",
            UpdatedUtc=DateTimeOffset.UtcNow
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp=path+".writing";
            File.WriteAllText(tmp,JsonSerializer.Serialize(data));
            File.Move(tmp,path,true);
        }
        catch(IOException) { /* feedback must never break construction */ }
        catch(UnauthorizedAccessException) { /* Windows can deny replacement while a reader holds the snapshot. */ }
    }
}
