using System.IO;
using ThreeDS.Progressive.Services;
using pk3DS.Core;

internal static class RebuildStabilityChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.XY);GlobalCompletionChecks.SetupCode(f);
        string outside=Path.Combine(Path.GetTempPath(),"rebuild-stability-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(outside);
        try
        {
            var service=new GameRomRebuildService(f.Session);
            string? sentinel=null;string target=Path.Combine(outside,"first.bin");Exception? failure=null;
            try{Task.Run(()=>service.BuildAsync(RomRebuildKind.ExeFS,target,(phase,_,_)=>
            {
                if(sentinel is not null)return;
                string stage=Directory.GetDirectories(outside,".definitive-rebuild-*").Single();
                sentinel=Path.Combine(stage,"readonly.tmp");File.WriteAllText(sentinel,"cleanup probe");File.SetAttributes(sentinel,FileAttributes.ReadOnly);
            })).GetAwaiter().GetResult();}catch(Exception ex){failure=ex;}
            check(failure is null&&File.Exists(target),"RebuildStability_CleanupFailureDoesNotInvalidatePublishedOutput");
            check(service.GetType().GetProperty("LastCleanupWarning",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(service) is string warning&&warning.Contains(Path.GetDirectoryName(sentinel!)!),"RebuildStability_CleanupWarningIdentifiesTemporaryFolder");
            if(sentinel is not null&&File.Exists(sentinel))File.SetAttributes(sentinel,FileAttributes.Normal);
            Task.Run(()=>service.BuildAsync(RomRebuildKind.ExeFS,Path.Combine(outside,"retry.bin"))).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            check(File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(Path.Combine(outside,"retry.bin"))),"RebuildStability_RetryAfterCleanupFailureReleasesGate");
            check(service.GetType().GetProperty("LastCleanupWarning",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(service) is null,"RebuildStability_RetryClearsOldWarning");
            string collision=Path.Combine(outside,"collision.bin");byte[] existing=[1,2,3];bool rejected=false;
            try{Task.Run(()=>service.BuildAsync(RomRebuildKind.ExeFS,collision,(phase,_,_)=>{if(phase.StartsWith("Ensamblando"))File.WriteAllBytes(collision,existing);})).GetAwaiter().GetResult();}catch(IOException){rejected=true;}
            check(rejected&&File.ReadAllBytes(collision).SequenceEqual(existing),"RebuildStability_ConcurrentDestinationIsNeverOverwritten");
            Task.Run(()=>service.BuildAsync(RomRebuildKind.ExeFS,Path.Combine(outside,"after-collision.bin"))).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            check(File.Exists(Path.Combine(outside,"after-collision.bin")),"RebuildStability_RetryAfterPublishFailure");
            string code=Directory.GetFiles(f.Game.ExeFS).Single(path=>Path.GetFileName(path).Contains("code",StringComparison.OrdinalIgnoreCase));
            string duplicate=Path.Combine(f.Game.ExeFS,Path.GetFileName(code).StartsWith('.')?"code.bin":".code.bin");File.Copy(code,duplicate);
            var previousStages=Directory.GetDirectories(outside,".definitive-rebuild-*").ToHashSet();
            string failed=Path.Combine(outside,"invalid.bin");sentinel=null;failure=null;
            try{Task.Run(()=>service.BuildAsync(RomRebuildKind.ExeFS,failed,(phase,_,_)=>
            {
                if(sentinel is not null)return;
                string stage=Directory.GetDirectories(outside,".definitive-rebuild-*").Single(path=>!previousStages.Contains(path));
                sentinel=Path.Combine(stage,"readonly.tmp");File.WriteAllText(sentinel,"cleanup after failure");File.SetAttributes(sentinel,FileAttributes.ReadOnly);
            })).GetAwaiter().GetResult();}catch(Exception ex){failure=ex;}
            check(failure is InvalidDataException&&!File.Exists(failed),"RebuildStability_CleanupPreservesOriginalBuildError");
            if(sentinel is not null&&File.Exists(sentinel))File.SetAttributes(sentinel,FileAttributes.Normal);
            File.Delete(duplicate);
            Task.Run(()=>service.BuildAsync(RomRebuildKind.ExeFS,failed)).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            check(File.Exists(failed),"RebuildStability_RetryAfterBuildAndCleanupFailure");
        }
        finally
        {
            foreach(string file in Directory.GetFiles(outside,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);
            Directory.Delete(outside,true);
        }
    }
}
