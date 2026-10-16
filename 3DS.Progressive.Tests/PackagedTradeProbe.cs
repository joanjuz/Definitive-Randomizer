using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Services;

internal static class PackagedTradeProbe
{
    internal static void Run(string runner,string root,string recipePath)
    {
        root=Path.GetFullPath(root);
        if(!root.StartsWith(@"D:\Progra\DefinitiveRandomizer-validation\",StringComparison.OrdinalIgnoreCase)||!root.Contains("package-probe",StringComparison.OrdinalIgnoreCase))throw new IOException("El diagnóstico requiere una copia independiente identificada como package-probe.");
        var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
        var service=new GameTrade6Service(project);var options=new Dictionary<string,int>{{"Offers",0}};
        var document=GlobalTemplateDocument.Empty(project.GameName,6);document.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});
        GlobalEditorTemplateBridge.Capture(document,service,new(options,0),6);
        document.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="trades.accept-any",["Parameters"]=new JsonObject()}));
        if(!NativeRecipeCompletion.TryComplete(project,document))throw new IOException("La receta no está completa.");
        var expected=service.Preview(options,[1],15);File.WriteAllText(recipePath,document.ToJson());
        var start=new ProcessStartInfo(Path.GetFullPath(runner)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        start.ArgumentList.Add("--native-gen6-replay");start.ArgumentList.Add(root);start.ArgumentList.Add(recipePath);start.ArgumentList.Add("15");
        using var process=Process.Start(start)??throw new IOException("No se pudo iniciar el ejecutor empaquetado.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(60000)){process.Kill(entireProcessTree:true);throw new IOException("El diagnóstico empaquetado excedió 60 segundos.");}
        if(process.ExitCode!=0)throw new IOException(error.GetAwaiter().GetResult());
        foreach(var file in expected.Files)if(!File.ReadAllBytes(file.Path).SequenceEqual(file.Proposed()))throw new IOException("El paquete no reproduce la propuesta nativa.");
        Console.WriteLine($"PASS PACKAGE: loaded actual packaged bridge; {expected.Changes.Count} trade changes; output identical to native proposal; private copy only.");
    }
}
