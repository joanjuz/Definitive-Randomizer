using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Services;

internal static class RealRebuildChecks
{
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    internal static void VerifyExisting(string root,string image,string output)
    {
        var session=GameProjectSession.Open(root,recoverInterruptedSave:false);var config=session.GameConfiguration;
        var files=Directory.GetFiles(config.RomFS,"*",SearchOption.AllDirectories).Concat(Directory.GetFiles(config.ExeFS,"*",SearchOption.AllDirectories)).ToArray();
        var hashes=files.ToDictionary(p=>p,Hash,StringComparer.OrdinalIgnoreCase);string imageHash=Hash(image);
        new GameRomExtractionService(root).ExtractAsync(image,output).GetAwaiter().GetResult();
        foreach(var (file,hash) in hashes)
        {
            bool rom=file.StartsWith(config.RomFS+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
            string relative=Path.GetRelativePath(rom?config.RomFS:config.ExeFS,file);
            if(!rom&&relative is "code.bin" or ".code.bin")relative=".code.bin";
            if(Hash(Path.Combine(output,rom?"RomFS":"ExeFS",relative))!=hash||Hash(file)!=hash)throw new IOException("Roundtrip u original distinto: "+file);
        }
        if(Hash(image)!=imageHash)throw new IOException("Contenedor cambiado.");
        Console.WriteLine($"PASS: {Path.GetFileName(root)} existing {Path.GetFileName(image)} extraction, all {files.Length} files match source byte-for-byte; source files and container intact.");
    }
    internal static void Run(string output,IEnumerable<string> roots)
    {
        output=Path.GetFullPath(output);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");
        var sources=roots.Select(Path.GetFullPath).ToArray();
        if(sources.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=sources.Length)throw new IOException("Raíces repetidas.");
        foreach(string root in sources)
            if(output.Equals(root,StringComparison.OrdinalIgnoreCase)||output.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||root.StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("La salida debe estar separada de los originales.");
        Directory.CreateDirectory(output);
        foreach(string root in sources)
        {
            var session=GameProjectSession.Open(root,recoverInterruptedSave:false);var config=session.GameConfiguration;
            var files=Directory.GetFiles(config.RomFS,"*",SearchOption.AllDirectories).Concat(Directory.GetFiles(config.ExeFS,"*",SearchOption.AllDirectories)).ToArray();
            var originals=files.ToDictionary(p=>p,Hash,StringComparer.OrdinalIgnoreCase);
            string game=Path.Combine(output,Path.GetFileName(root));Directory.CreateDirectory(game);
            var rebuild=new GameRomRebuildService(session);
            foreach(var kind in Enum.GetValues<RomRebuildKind>())
            {
                string path=Path.Combine(game,kind+(kind is RomRebuildKind.Full3DS or RomRebuildKind.Trimmed3DS?".3ds":".bin"));
                var built=rebuild.BuildAsync(kind,path).GetAwaiter().GetResult();
                if(Hash(path)!=built.Hash)throw new IOException("SHA de salida distinto.");
                if(kind is RomRebuildKind.Full3DS or RomRebuildKind.Trimmed3DS)
                {
                    string extracted=Path.Combine(game,kind+"-extracted");
                    new GameRomExtractionService(root).ExtractAsync(path,extracted).GetAwaiter().GetResult();
                    foreach(string file in files)
                    {
                        bool rom=file.StartsWith(config.RomFS+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
                        string relative=Path.GetRelativePath(rom?config.RomFS:config.ExeFS,file);
                        if(!rom&&relative is "code.bin" or ".code.bin")relative=".code.bin";
                        string recovered=Path.Combine(extracted,rom?"RomFS":"ExeFS",relative);
                        if(!File.Exists(recovered)||Hash(recovered)!=originals[file])throw new IOException("Roundtrip distinto: "+file+" -> "+recovered);
                    }
                    Console.WriteLine($"PASS: {Path.GetFileName(root)} {kind} rebuild/extract byte-exact roundtrip of {files.Length} source files.");
                }
                foreach(var (file,hash) in originals)if(Hash(file)!=hash)throw new IOException("Original cambiado: "+file);
                Console.WriteLine($"PASS: {Path.GetFileName(root)} {kind}, bytes={built.Bytes}, SHA={built.Hash}, all source hashes intact.");
            }
        }
    }
}
