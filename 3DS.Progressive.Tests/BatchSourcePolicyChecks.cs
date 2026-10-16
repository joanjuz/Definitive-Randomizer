using System.IO;
using System.Reflection;
using ThreeDS.Progressive.Services;
internal static class BatchSourcePolicyChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var assembly=Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));var workspace=assembly.GetType("pk3DS.WinForms.BatchWorkspace",true)!;
        var normalize=workspace.GetMethod("NormalizeStageCode",BindingFlags.Static|BindingFlags.NonPublic)!;
        void Run(string root){try{normalize.Invoke(null,[root]);}catch(TargetInvocationException ex)when(ex.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}}
        foreach(string name in new[]{"code.bin","CODE.BIN",".CODE.BIN",".code.bin"})
        {
            string root=Path.Combine(Path.GetTempPath(),"code-stage-policy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                byte[] code=[1,2,3,4],banner=[7,8,9];File.WriteAllBytes(Path.Combine(root,"banner.bin"),banner);File.WriteAllBytes(Path.Combine(root,name),code);File.WriteAllBytes(Path.Combine(root,"icon.bin"),[5,6]);
                Run(root);check(Directory.GetFiles(root).Select(Path.GetFileName).Contains(".code.bin",StringComparer.Ordinal)&&File.ReadAllBytes(Path.Combine(root,".code.bin")).SequenceEqual(code),"Batch staging normalizes only its own "+name+" filename without changing code bytes");
                check(File.ReadAllBytes(Path.Combine(root,"banner.bin")).SequenceEqual(banner)&&!Directory.GetFiles(root).Any(path=>Path.GetFileName(path).StartsWith(".code-normalize-")),"Batch staging preserves banner and consumes unique temporary for "+name);
                Run(root);check(Directory.GetFiles(root).Length==3,"Batch staging code normalization is idempotent for "+name);
                File.WriteAllBytes(Path.Combine(root,"code.bin"),[9]);reject(()=>Run(root),"Batch staging rejects ambiguous code names for "+name);
            }
            finally{Directory.Delete(root,true);}
        }
    }
}
