using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;

internal static class CatchZoneReferenceAudit
{
    internal static void Run(string romRoot,string templatesRoot)
    {
        var session=GameProjectSession.Open(romRoot);var game=session.GameConfiguration;
        if(session.Generation!=6)throw new InvalidDataException("Esta auditoría requiere Gen6.");
        string family=game.ORAS?"ORAS":"XY";
        var manifest=CatchZonePatchEngine.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"catch_zone_patches",family+".json")),family);
        var archives=new Dictionary<string,GARC.MemGARC>(StringComparer.OrdinalIgnoreCase);
        int match=0,different=0,missing=0,sourceMatch=0,already=0,incompatible=0;
        Console.WriteLine("AUDIT FAMILY: "+family);
        Console.WriteLine("ROM REFERENCE: "+romRoot);
        Console.WriteLine("OLD TEMPLATES: "+templatesRoot);
        foreach(var file in manifest.Files!)
        {
            string oldPath=Path.Combine(templatesRoot,family,file.Scope,file.Path);
            if(!File.Exists(oldPath)){missing++;Console.WriteLine("OLD TARGET MISSING: "+file.Scope+"/"+file.Path);}
            else
            {
                byte[] old=File.ReadAllBytes(oldPath);
                if(old.LongLength==file.TargetLength&&CatchZonePatchEngine.Hash(old).Equals(file.TargetSha256,StringComparison.OrdinalIgnoreCase))match++;
                else{different++;Console.WriteLine("OLD TARGET DIFFERENT: "+file.Scope+"/"+file.Path+"; length "+old.Length+"; SHA256 "+CatchZonePatchEngine.Hash(old));}
            }
            byte[] source;
            if(file.Scope.Equals("raw",StringComparison.OrdinalIgnoreCase))source=File.ReadAllBytes(Path.Combine(game.RomFS,file.Path));
            else
            {
                if(!archives.TryGetValue(file.Scope,out var archive))
                {archive=new GARC.MemGARC(File.ReadAllBytes(Path.Combine(game.RomFS,game.GetGARCFileName(file.Scope))));archives.Add(file.Scope,archive);}
                var name=Regex.Match(file.Path,@"^(dec_)?([0-9]+)\.bin$");
                if(!name.Success)throw new InvalidDataException("Nombre de miembro no compatible.");
                source=archive.GetFile(int.Parse(name.Groups[2].Value));
                if(name.Groups[1].Success){using var stream=new MemoryStream();LZSS.Decompress(new MemoryStream(source),source.Length,stream);source=stream.ToArray();}
            }
            string hash=CatchZonePatchEngine.Hash(source);
            if(CatchZonePatchEngine.MatchesSource(file,source.LongLength,hash))
            {
                sourceMatch++;
                var single=new CatchZoneManifest{Format=2,Game=family,Files=[file]};
                _=CatchZonePatchEngine.Evaluate(JsonSerializer.SerializeToUtf8Bytes(single),family,(_,_)=>source);
            }
            else if(CatchZonePatchEngine.MatchesTarget(file,source.LongLength,hash))already++;
            else{incompatible++;Console.WriteLine("ROM SOURCE INCOMPATIBLE: "+file.Scope+"/"+file.Path+"; length "+source.Length+"; actual "+hash+"; expected "+file.SourceSha256);}
        }
        Console.WriteLine($"OLD TARGET SUMMARY: matching {match}; different {different}; missing {missing}; manifest files {manifest.Files.Count}");
        Console.WriteLine($"ROM SOURCE SUMMARY: verified applicable {sourceMatch}; already applied {already}; incompatible {incompatible}");
        Console.WriteLine("AUDIT MODE: no writes, no Apply, no ROM binary output.");
    }
}
