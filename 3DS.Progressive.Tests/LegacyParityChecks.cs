using System.IO;
using System.Reflection;
using ThreeDS.Progressive.Services;
using Legacy=ThreeDS.Progressive.Services.ProgressiveLegacy;

// Parity between the app's ProgressiveLegacy copies and the LegacyBatch (WinForms) originals.
// See docs/3ds-progressive/AUDITORIA_MOTOR_DUPLICADO.md for the differences these checks pin down.
internal static class LegacyParityChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var assembly=Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));
        var main=assembly.GetType("pk3DS.WinForms.Main",true)!;
        var romFS=main.GetField("RomFSPath",BindingFlags.Public|BindingFlags.Static)!;var exeFS=main.GetField("ExeFSPath",BindingFlags.Public|BindingFlags.Static)!;
        object? oldRomFS=romFS.GetValue(null),oldExeFS=exeFS.GetValue(null);
        byte[] relocated=RegressionFixes.FullBattle();
        try
        {
            // The app relocates these patches when Giro Vil V2 already moved the read-only data; LegacyBatch rejects them explicitly.
            foreach(string name in new[]{"Gen7LuckyChantCritPatcher","Gen7FairyLockV11Patcher","Gen7MeditateV73Patcher","Gen7NightmareSleepV76Patcher"})
            {
                var legacy=Apply(name,relocated,useBatch:true);
                check(legacy.Found,"LegacyBatch "+name+" locates the fixture Battle.cro, so its result is not a missing-file rejection");
                check(legacy.Result==-1&&legacy.Bytes.SequenceEqual(relocated),"LegacyBatch "+name+" rejects a Battle.cro relocated by Giro Vil V2 without modifying it");
                var app=Apply(name,relocated,useBatch:false);
                check(app.Result>=0&&Legacy.Gen7BrutalSwingSelfConfusionPatcher.IsInstalled(app.Bytes),"app "+name+" resolves the same relocated Battle.cro and keeps Giro Vil V2");
            }
            // Mist is the same code in both engines and only targets the code segment, which Giro Vil V2 does not move.
            var legacyMist=Apply("Gen7MistOwnerActivePatcher",relocated,useBatch:true);var appMist=Apply("Gen7MistOwnerActivePatcher",relocated,useBatch:false);
            check(legacyMist.Result==appMist.Result&&legacyMist.Bytes.SequenceEqual(appMist.Bytes),"Mist gives the same result and bytes in the app and LegacyBatch on a relocated Battle.cro");
        }
        finally{romFS.SetValue(null,oldRomFS);exeFS.SetValue(null,oldExeFS);}

        (int Result,byte[] Bytes,bool Found) Apply(string name,byte[] battle,bool useBatch)
        {
            string root=Path.Combine(Path.GetTempPath(),"legacy-parity-"+Guid.NewGuid().ToString("N"));string rom=Path.Combine(root,"RomFS"),path=Path.Combine(rom,"Battle.cro");
            Directory.CreateDirectory(rom);Directory.CreateDirectory(Path.Combine(root,"ExeFS"));File.WriteAllBytes(path,battle);
            try
            {
                if(!useBatch)
                    using(Legacy.Main.Enter(root))return (Invoke(typeof(Legacy.Main).Assembly.GetType("ThreeDS.Progressive.Services.ProgressiveLegacy."+name,true)!,"Apply"),File.ReadAllBytes(path),true);
                romFS.SetValue(null,rom);exeFS.SetValue(null,Path.Combine(root,"ExeFS"));
                var type=assembly.GetType("pk3DS.WinForms."+name,true)!;
                bool found=type.GetMethod("FindBattleCro",BindingFlags.Static|BindingFlags.NonPublic,Type.EmptyTypes) is not { } find||
                    string.Equals(Path.GetFullPath((string)find.Invoke(null,null)!),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase);
                return (Invoke(type,"Apply"),File.ReadAllBytes(path),found);
            }
            finally{Directory.Delete(root,true);}
        }
        static int Invoke(Type type,string method)
        {
            try{return (int)type.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic,Type.EmptyTypes)!.Invoke(null,null)!;}
            catch(TargetInvocationException ex)when(ex.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}
        }
    }
}
