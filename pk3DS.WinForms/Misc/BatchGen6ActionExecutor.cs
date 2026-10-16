using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.WinForms;

internal static class BatchGen6ActionExecutor
{
    internal static readonly HashSet<string> Supported=new(StringComparer.OrdinalIgnoreCase)
    {"manual-edits.apply","rng.seed","moves.balance","moves.randomize","moves.metronome","personal.randomize","personal.modify-all","pokemon-stats.apply",
     "evolutions.randomize","evolutions.every-level","evolutions.remove-trade","evolutions.normalize","level-up-moves.randomize","level-up-moves.metronome","egg-moves.randomize","trades.accept-any","trades.randomize-offers","trainers.randomize","static-encounters.randomize","static-encounters.modify-levels","starters.randomize","tms.randomize","field-items.randomize","marts.randomize","marts.add-rare-candies","marts.add-ev-items","marts.ban-ability-capsule","items.fix-economy","pickup.randomize","wild-encounters.randomize","wild-encounters.modify-levels","wild-encounters.scale-levels","tms.sanity","tms.follow-evolutions","catchzones.enable","shiny-rate.apply","move-tutors.randomize","move-tutors.sanity","move-tutors.follow-evolutions"};
    internal static string[] GetUnsupported(IEnumerable<GlobalRandomizationAction> actions)=>(actions??Enumerable.Empty<GlobalRandomizationAction>()).Where(a=>a!=null&&!string.IsNullOrWhiteSpace(a.Id)&&!Supported.Contains(a.Id)).Select(a=>a.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    internal static void Execute(GlobalRandomizationTemplate template)
    {
        if(Main.Config?.Generation!=6)throw new NotSupportedException("El ejecutor Gen6 requiere XY/ORAS.");
        var unsupported=GetUnsupported(template.Actions);if(unsupported.Length>0)throw new NotSupportedException("Acciones Gen6 no integradas: "+string.Join(", ",unsupported));
        var actions=new HashSet<string>(template.Actions.Select(a=>a.Id),StringComparer.OrdinalIgnoreCase);
        bool Has(string id)=>actions.Contains(id);
        if((Has("trades.accept-any")||Has("trades.randomize-offers"))&&!HasNativeTrades(template))
            BatchRuntime.Log(Gen6TradePatcher.ApplyGen6TradePatch(Main.ExeFSPath,Main.Config,Has("trades.randomize-offers")));
        BatchBridgeProgress.ActionGroup("Trades",actions,id=>id.StartsWith("trades.",StringComparison.OrdinalIgnoreCase));
        if(actions.Any(id=>id.StartsWith("moves.",StringComparison.OrdinalIgnoreCase))&&!TryMasterNative(template,"Moves"))
        {
            BatchRuntime.Log("Gen6 moves...");var g=Main.Config.GARCMoves;bool mini=Main.Config.ORAS;byte[][] data=mini?Mini.UnpackMini(g.GetFile(0),"WD"):g.Files;
            using var form=new MoveEditor6(data);
            if(Has("moves.randomize"))Event(form,"B_RandAll_Click");if(Has("moves.metronome"))Event(form,"B_Metronome_Click");if(Has("moves.balance"))Event(form,"B_BalanceMoves_Click");
            Invoke(form,"SetEntry");g.Files=mini?new[]{Mini.PackMini(data,"WD")}:data;g.Save();Main.Config.InitializeMoves();
        }
        BatchBridgeProgress.ActionGroup("Moves",actions,id=>id.StartsWith("moves.",StringComparison.OrdinalIgnoreCase));
        if(actions.Any(id=>id.StartsWith("personal.",StringComparison.OrdinalIgnoreCase)||id=="pokemon-stats.apply")&&!TryMasterNative(template,"Personal"))
        {
            BatchRuntime.Log("Gen6 personal...");byte[][] data=Main.Config.GARCPersonal.Files;using var form=new PersonalEditor6(data);
            if(Has("personal.randomize"))Event(form,"B_Randomize_Click");if(Has("personal.modify-all"))Event(form,"B_ModifyAll");if(Has("pokemon-stats.apply"))Event(form,"B_PokemonStatsTemplate_Click");
            Invoke(form,"SaveEntry");for(int i=0;i<data.Length-1;i++)data[i].CopyTo(data[^1],i*data[i].Length);Main.Config.GARCPersonal.Files=data;Main.Config.GARCPersonal.Save();Main.Config.InitializePersonal();
        }
        BatchBridgeProgress.ActionGroup("Personal",actions,id=>id.StartsWith("personal.",StringComparison.OrdinalIgnoreCase)||id=="pokemon-stats.apply");
        if(actions.Any(id=>id.StartsWith("evolutions.",StringComparison.OrdinalIgnoreCase)))
        {
            BatchRuntime.Log("Gen6 evolutions...");var g=Main.Config.GetGARCData("evolution");byte[][] data=g.Files;using var form=new EvolutionEditor6(data);
            if(Has("evolutions.randomize"))Event(form,"B_RandAll_Click");if(Has("evolutions.every-level"))Event(form,"B_EveryLevel_Click");if(Has("evolutions.remove-trade"))Event(form,"B_Trade_Click");if(Has("evolutions.normalize"))Event(form,"B_NormalizeEvolutions_Click");
            Invoke(form,"SetList");g.Files=data;g.Save();Main.Config.InitializeEvos();
        }
        BatchBridgeProgress.ActionGroup("Evolutions",actions,id=>id.StartsWith("evolutions.",StringComparison.OrdinalIgnoreCase));
        if(actions.Any(id=>id.StartsWith("level-up-moves.",StringComparison.OrdinalIgnoreCase)))
        {
            BatchRuntime.Log("Gen6 level-up moves...");byte[][] data=Main.Config.GARCLearnsets.Files;using var form=new LevelUpEditor6(data);
            if(Has("level-up-moves.randomize"))Event(form,"B_RandAll_Click");if(Has("level-up-moves.metronome"))Event(form,"B_Metronome_Click");
            Invoke(form,"SetList");Main.Config.GARCLearnsets.Files=data;Main.Config.GARCLearnsets.Save();Main.Config.InitializeLearnset();
        }
        BatchBridgeProgress.ActionGroup("LevelUpMoves",actions,id=>id.StartsWith("level-up-moves.",StringComparison.OrdinalIgnoreCase));
        if(Has("egg-moves.randomize"))
        {
            BatchRuntime.Log("Gen6 egg moves...");var g=Main.Config.GetGARCData("eggmove");byte[][] data=g.Files;using var form=new EggMoveEditor6(data);
            Event(form,"B_RandAll_Click");Invoke(form,"Form_Closing",form,new FormClosingEventArgs(CloseReason.None,false));g.Files=data;g.Save();
        }
        BatchBridgeProgress.ActionGroup("EggMoves",actions,id=>id.StartsWith("egg-moves.",StringComparison.OrdinalIgnoreCase));Main.SaveGameText();
        string[] nativeIds=["trades.accept-any","trades.randomize-offers","static-encounters.randomize","static-encounters.modify-levels","starters.randomize","tms.randomize","field-items.randomize","marts.randomize","marts.add-rare-candies","marts.add-ev-items","marts.ban-ability-capsule","items.fix-economy","pickup.randomize","wild-encounters.randomize","wild-encounters.modify-levels","wild-encounters.scale-levels","tms.sanity","tms.follow-evolutions","catchzones.enable","shiny-rate.apply","move-tutors.randomize","move-tutors.sanity","move-tutors.follow-evolutions"];
        if(nativeIds.Any(id=>Has(id)&&(!id.StartsWith("trades.",StringComparison.OrdinalIgnoreCase)||HasNativeTrades(template))))
        {
            BatchRuntime.Log("Gen6 native editors...");
            ExecuteNative(System.IO.Directory.GetParent(Main.RomFSPath).FullName,System.Text.Json.JsonSerializer.Serialize(template),unchecked((int)Util.Random32()));
            Main.Config.InitializeAll();
            BatchBridgeProgress.ActionGroup("Gen6 native editors",actions,id=>nativeIds.Contains(id,StringComparer.OrdinalIgnoreCase));
        }
        if(Has("trainers.randomize"))RunTrainers();
        BatchBridgeProgress.ActionGroup("Trainers",actions,id=>id.StartsWith("trainers.",StringComparison.OrdinalIgnoreCase));
        ApplyManual(template);
    }
    internal static void ApplyManual(GlobalRandomizationTemplate template)
    {
        if(!template.Actions.Any(a=>a.Id.Equals("manual-edits.apply",StringComparison.OrdinalIgnoreCase)))return;
        Main.SaveGameText();
        string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));
        var bridge=Assembly.LoadFrom(path).GetType("ThreeDS.Progressive.Services.ManualEditTemplate",true);
        string previousSeed=Environment.GetEnvironmentVariable("DEFINITIVE_TEMPLATE_SEED");
        Environment.SetEnvironmentVariable("DEFINITIVE_TEMPLATE_SEED",unchecked((int)Util.Random32()).ToString(System.Globalization.CultureInfo.InvariantCulture));
        try{bridge.GetMethod("Execute",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{System.IO.Directory.GetParent(Main.RomFSPath).FullName,System.Text.Json.JsonSerializer.Serialize(template)});}
        catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
        finally{Environment.SetEnvironmentVariable("DEFINITIVE_TEMPLATE_SEED",previousSeed);}
        Main.Config.InitializePersonal();Main.Config.InitializeMoves();Main.Config.InitializeGameText();
        BatchBridgeProgress.ActionGroup("Modificaciones manuales",template.Actions.Select(a=>a.Id),id=>id=="manual-edits.apply");
    }
    private static bool HasNativeTrades(GlobalRandomizationTemplate template)
    {
        using var document=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(template));
        return document.RootElement.TryGetProperty("ProgressiveApp",out var native)&&native.TryGetProperty("Trades",out var trades)&&trades.ValueKind==System.Text.Json.JsonValueKind.Object;
    }
    internal static bool TryWild7Native(GlobalRandomizationTemplate template)
    {
        string json=System.Text.Json.JsonSerializer.Serialize(template);using(var doc=System.Text.Json.JsonDocument.Parse(json))if(!doc.RootElement.TryGetProperty("ProgressiveApp",out var native)||!native.TryGetProperty("Wild",out _))return false;
        string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));var bridge=Assembly.LoadFrom(path).GetType("ThreeDS.Progressive.Services.NativeGen7WildBridge",true);
        try{bridge.GetMethod("Execute",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{System.IO.Directory.GetParent(Main.RomFSPath).FullName,json,unchecked((int)Util.Random32())});}catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}return true;
    }
    internal static bool TryMartNative(GlobalRandomizationTemplate template)
    {
        string json=System.Text.Json.JsonSerializer.Serialize(template);using(var document=System.Text.Json.JsonDocument.Parse(json))if(!document.RootElement.TryGetProperty("ProgressiveApp",out var native)||!native.TryGetProperty("Gen7Marts",out _))return false;
        Main.SaveGameText();string assemblyPath=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));var bridge=Assembly.LoadFrom(assemblyPath).GetType("ThreeDS.Progressive.Services.NativeGen7MartBridge",true);
        try{bridge.GetMethod("Execute",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{System.IO.Directory.GetParent(Main.RomFSPath).FullName,json,unchecked((int)Util.Random32())});}catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
        Main.Config.InitializeGameText();return true;
    }
    internal static bool TryMachineNative(GlobalRandomizationTemplate template,string key)
    {
        string json=System.Text.Json.JsonSerializer.Serialize(template);using(var document=System.Text.Json.JsonDocument.Parse(json))if(!document.RootElement.TryGetProperty("ProgressiveApp",out var native)||!native.TryGetProperty("Gen7Machines",out _))return false;
        Main.SaveGameText();string assemblyPath=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));var bridge=Assembly.LoadFrom(assemblyPath).GetType("ThreeDS.Progressive.Services.NativeGen7MachineBridge",true);
        try{bridge.GetMethod("Execute",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{System.IO.Directory.GetParent(Main.RomFSPath).FullName,json,unchecked((int)Util.Random32()),key});}catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
        Main.Config.InitializePersonal();Main.Config.InitializeGameText();return true;
    }
    internal static bool TryFieldNative(GlobalRandomizationTemplate template,string key)
    {
        string id=key=="FieldItems"?"field-items.randomize":"items.fix-economy";if(!template.Actions.Any(action=>action.Id.Equals(id,StringComparison.OrdinalIgnoreCase)))return false;
        string json=System.Text.Json.JsonSerializer.Serialize(template);using(var document=System.Text.Json.JsonDocument.Parse(json))if(!document.RootElement.TryGetProperty("ProgressiveApp",out var native)||!native.TryGetProperty(key,out _))return false;
        Main.SaveGameText();string assemblyPath=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));var bridge=Assembly.LoadFrom(assemblyPath).GetType("ThreeDS.Progressive.Services.NativeGen7FieldBridge",true);
        try{bridge.GetMethod("Execute",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{System.IO.Directory.GetParent(Main.RomFSPath).FullName,json,unchecked((int)Util.Random32()),key});}catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
        Main.Config.InitializeGameText();return true;
    }
    internal static bool TryMasterNative(GlobalRandomizationTemplate template,string module)
    {
        if(!template.Actions.Any(action=>module=="Moves"?action.Id.StartsWith("moves.",StringComparison.OrdinalIgnoreCase):action.Id.StartsWith("personal.",StringComparison.OrdinalIgnoreCase)||action.Id=="pokemon-stats.apply"))return false;
        string json=System.Text.Json.JsonSerializer.Serialize(template);
        using(var document=System.Text.Json.JsonDocument.Parse(json))
            if(!document.RootElement.TryGetProperty("ProgressiveApp",out var native)||!native.TryGetProperty("MasterBatches",out var batches)||!batches.TryGetProperty(module,out _))return false;
        string assemblyPath=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));
        Main.SaveGameText();
        var bridge=Assembly.LoadFrom(assemblyPath).GetType("ThreeDS.Progressive.Services.NativeMasterBatchBridge",true);
        try{bridge.GetMethod("Execute",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{System.IO.Directory.GetParent(Main.RomFSPath).FullName,json,unchecked((int)Util.Random32()),module});}
        catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
        if(module=="Moves")Main.Config.InitializeMoves();else Main.Config.InitializePersonal();
        Main.Config.InitializeGameText();return true;
    }
    internal static void ExecuteNative(string root,string templateJson,int seed)
    {
            string assemblyPath=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));
            if(!System.IO.File.Exists(assemblyPath))throw new System.IO.FileNotFoundException("Falta el motor nativo de Definitive Randomizer.",assemblyPath);
            var bridge=Assembly.LoadFrom(assemblyPath).GetType("ThreeDS.Progressive.Services.NativeBatchGen6Bridge",true);
            var method=bridge.GetMethod("Execute",BindingFlags.Static|BindingFlags.Public);
            try { method.Invoke(null,new object[]{root,templateJson,seed}); }
            catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
    }
    internal static void ExecuteStaticGen7Native(string root,string templateJson,int seed)
    {
        string assemblyPath=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));
        var bridge=Assembly.LoadFrom(assemblyPath).GetType("ThreeDS.Progressive.Services.NativeBatchGen7StaticBridge",true);
        try{bridge.GetMethod("Execute",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{root,templateJson,seed});}
        catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
    }
    internal static void RebuildNative(string root,string target,bool trimmed)
    {
        string assemblyPath=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));
        var bridge=Assembly.LoadFrom(assemblyPath).GetType("ThreeDS.Progressive.Services.NativeBatchRomBridge",true);
        string lastStage="";var clock=System.Diagnostics.Stopwatch.StartNew();
        Action<string,int,int> progress=(stage,value,max)=>{BatchBridgeProgress.RebuildStage(stage,value,max);if(stage!=lastStage){BatchRuntime.Log($"Rebuild [{clock.Elapsed.TotalSeconds:F1}s] {stage}");lastStage=stage;}};
        try{bridge.GetMethod("RebuildWithProgress",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{root,target,trimmed,progress});}
        catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
    }
    internal static void ApplyPickup7Native(string root,int seed)
    {
        string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));var bridge=Assembly.LoadFrom(path).GetType("ThreeDS.Progressive.Services.NativeBatchRomBridge",true);
        try{bridge.GetMethod("ApplyPickup",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{root,seed});}catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
    }
    internal static void ApplyShinyNative(string root,int rerolls,bool everything)
    {
        string assemblyPath=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(BatchGen6ActionExecutor).Assembly.Location),"..","3DS.Progressive.dll"));
        var bridge=Assembly.LoadFrom(assemblyPath).GetType("ThreeDS.Progressive.Services.NativeBatchRomBridge",true);
        try{bridge.GetMethod("ApplyShiny",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{root,rerolls,everything});}
        catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
    }
    internal static void RunTrainers()
    {
        BatchRuntime.Log("Gen6 trainers...");var headers=Main.Config.GetGARCData("trdata");var teams=Main.Config.GetGARCData("trpoke");
        byte[][] trd=headers.Files,trp=teams.Files;using var form=new RSTE(trd,trp);
        Event(form,"B_Randomize_Click");Invoke(form,"WriteFile");headers.Files=trd;teams.Files=trp;headers.Save();teams.Save();
    }
    private static void Event(object form,string name)=>Invoke(form,name,form,EventArgs.Empty);
    private static void Invoke(object form,string name,params object[] args)
    {
        var method=form.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)??throw new MissingMethodException(form.GetType().FullName,name);
        try{method.Invoke(form,args);}catch(TargetInvocationException ex)when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
    }
}
