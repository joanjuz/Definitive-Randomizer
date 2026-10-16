using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using ThreeDS.Progressive.Services;

internal static class ShinyRunnerChecks
{
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    internal static void Run(string destination,IEnumerable<string> roots)
    {
        destination=Path.GetFullPath(destination);
        if(Directory.Exists(destination)||File.Exists(destination))throw new IOException("El destino debe ser nuevo.");
        var sources=roots.Select(Path.GetFullPath).ToArray();
        if(sources.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=sources.Length)throw new IOException("Raíces repetidas.");
        foreach(string root in sources)
            if(destination.Equals(root,StringComparison.OrdinalIgnoreCase)||destination.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||root.StartsWith(destination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("La copia debe estar separada de los originales.");
        Directory.CreateDirectory(destination);
        foreach(string root in sources)
        {
            var original=GameProjectSession.Open(root,recoverInterruptedSave:false);
            if(original.Generation!=6)throw new InvalidDataException("Esta comprobación requiere XY/ORAS.");
            var config=original.GameConfiguration;
            var files=Directory.GetFiles(config.RomFS,"*",SearchOption.AllDirectories).Concat(Directory.GetFiles(config.ExeFS,"*",SearchOption.AllDirectories)).ToArray();
            var hashes=files.ToDictionary(p=>p,Hash,StringComparer.OrdinalIgnoreCase);
            string copy=Path.Combine(destination,Path.GetFileName(root));
            foreach(string path in files)
            {
                string target=Path.Combine(copy,Path.GetRelativePath(root,path));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(path,target,false);
            }
            var session=GameProjectSession.Open(copy,recoverInterruptedSave:false);
            var expected=new GameShinyRateService(session).Preview(333,true);
            var doc=GlobalTemplateDocument.Empty(session.GameName,6);ShinyRateTemplate.Capture(doc,new(333,true));
            string recipe=Path.Combine(destination,Path.GetFileName(root)+"-recipe.json");doc.Save(recipe);
            var start=new ProcessStartInfo(BatchRomExecution.RunnerPath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
            foreach(string arg in new[]{"--native-gen6-replay",copy,recipe,"91"})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new IOException("No se inició el ejecutor original.");
            var error=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();
            int waits=0;while(!process.WaitForExit(10000)&&++waits<60){}
            if(!process.HasExited){process.Kill(entireProcessTree:true);throw new IOException("El ejecutor independiente excedió el tiempo de diagnóstico.");}
            File.WriteAllText(Path.Combine(destination,Path.GetFileName(root)+"-runner.log"),output.GetAwaiter().GetResult()+error.GetAwaiter().GetResult());
            if(process.ExitCode!=0)throw new IOException("Falló el ejecutor independiente: "+error.Result);
            if(!File.ReadAllBytes(expected.File.Path).SequenceEqual(expected.File.Proposed()))throw new IOException("Resultado ARM distinto de la propuesta manual.");
            var state=new GameShinyRateService(session).Read();if(!state.AlwaysShiny||state.Rerolls!=expected.AppliedRerolls)throw new IOException("Lectura shiny distinta.");
            foreach(var (path,hash) in hashes)
            {
                if(Hash(path)!=hash)throw new IOException("Original cambiado: "+path);
                string copied=Path.Combine(copy,Path.GetRelativePath(root,path));
                if(!copied.Equals(expected.File.Path,StringComparison.OrdinalIgnoreCase)&&Hash(copied)!=hash)throw new IOException("Archivo ajeno al parche cambiado: "+copied);
            }
            if(!files.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(Directory.GetFiles(config.RomFS,"*",SearchOption.AllDirectories).Concat(Directory.GetFiles(config.ExeFS,"*",SearchOption.AllDirectories))))throw new IOException("Inventario original cambiado.");
            Console.WriteLine($"PASS: {Path.GetFileName(root)} independent original runner; exact manual ARM bytes; rerolls={state.Rerolls}; Everything Shiny=true; {files.Length} source hashes and unrelated copy files preserved.");
        }
    }
}
