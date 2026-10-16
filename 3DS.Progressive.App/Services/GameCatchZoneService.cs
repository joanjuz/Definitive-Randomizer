using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed class GameCatchZoneService
{
    private readonly GameProjectSession _project;
    private readonly Guid _owner=Guid.NewGuid();
    private readonly string _manifestPath;
    internal GameCatchZoneService(GameProjectSession project, string? manifestPath=null)
    {
        if(project.Generation!=6)throw new InvalidDataException("Catch Zones requiere XY u ORAS.");
        _project=project;
        _manifestPath=manifestPath??Path.Combine(Path.GetDirectoryName(typeof(GameCatchZoneService).Assembly.Location)!,"catch_zone_patches",project.GameConfiguration.ORAS?"ORAS.json":"XY.json");
    }
    private static void CheckPath(string path)
    {
        for(string? current=Path.GetFullPath(path);current is not null;current=Path.GetDirectoryName(current))
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Catch Zones no acepta enlaces a otras carpetas.");
    }
    internal EditorProposal Preview()
    {
        CheckPath(_manifestPath);byte[] json=File.ReadAllBytes(_manifestPath);
        string family=_project.GameConfiguration.ORAS?"ORAS":"XY";
        var manifest=CatchZonePatchEngine.Load(json,family);
        if(manifest.Files!.Any(f=>f.Operation.Equals("delete",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Este flujo requiere parches sin eliminación de archivos.");
        var originals=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var containers=new Dictionary<string,(string Path,byte[][] Records)>(StringComparer.OrdinalIgnoreCase);
        var members=new Dictionary<string,(string Scope,int Index,bool Compressed)>();
        var sources=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        string Key(string scope,string path)=>scope+"/"+path;
        byte[] Read(string path)
        {
            if(!originals.TryGetValue(path,out var bytes)){CheckPath(path);originals[path]=bytes=File.ReadAllBytes(path);}return bytes;
        }
        foreach(var file in manifest.Files!)
        {
            string key=Key(file.Scope,file.Path);
            if(file.Scope.Equals("raw",StringComparison.OrdinalIgnoreCase))
            {
                string path=Path.GetFullPath(Path.Combine(_project.GameConfiguration.RomFS,file.Path));
                string root=Path.GetFullPath(_project.GameConfiguration.RomFS)+Path.DirectorySeparatorChar;
                if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Ruta raw insegura.");
                sources[key]=Read(path);continue;
            }
            if(!containers.TryGetValue(file.Scope,out var container))
            {
                string path=Path.Combine(_project.GameConfiguration.RomFS,_project.GameConfiguration.GetGARCFileName(file.Scope));
                byte[] bytes=Read(path);var header=GARC.UnpackGARC(path);
                if(header.fatb.Entries.Any(e=>e.SubEntries.Count(s=>s.Exists)!=1 || !e.SubEntries[0].Exists))
                    throw new InvalidDataException("GARC con múltiples subarchivos no compatible con Catch Zones.");
                container=(path,new GARC.MemGARC(bytes).Files);containers.Add(file.Scope,container);
            }
            var match=Regex.Match(file.Path,@"^(dec_)?([0-9]+)\.bin$",RegexOptions.CultureInvariant);
            if(!match.Success||!int.TryParse(match.Groups[2].Value,out int index)||index>=container.Records.Length)
                throw new InvalidDataException("Miembro GARC Catch Zones inexistente: "+file.Path);
            bool compressed=match.Groups[1].Success;
            if(members.Values.Any(m=>m.Scope.Equals(file.Scope,StringComparison.OrdinalIgnoreCase)&&m.Index==index))
                throw new InvalidDataException("Miembro GARC repetido mediante nombres alternativos.");
            byte[] raw=container.Records[index];byte[] source=raw;
            if(compressed)
            {
                if(raw.Length==0||raw[0]!=0x11)throw new InvalidDataException("Se esperaba un miembro LZ11: "+file.Path);
                using var output=new MemoryStream();LZSS.Decompress(new MemoryStream(raw),raw.Length,output);source=output.ToArray();
            }
            members.Add(key,(file.Scope,index,compressed));sources[key]=source;
        }
        var incompatible=new List<string>();
        foreach(var file in manifest.Files!)
        {
            byte[] bytes=sources[Key(file.Scope,file.Path)];string hash=CatchZonePatchEngine.Hash(bytes);
            bool clean=CatchZonePatchEngine.MatchesSource(file,bytes.LongLength,hash);
            bool applied=CatchZonePatchEngine.MatchesTarget(file,bytes.LongLength,hash);
            if(!clean&&!applied)incompatible.Add(Key(file.Scope,file.Path));
        }
        if(incompatible.Count>0)
            throw new InvalidDataException($"Catch Zones: {incompatible.Count} archivos no coinciden con el origen compatible ni con el parche aplicado. "+
                string.Join(", ",incompatible.Take(8))+(incompatible.Count>8?"…":"")+". No se guardó ningún cambio.");
        var results=CatchZonePatchEngine.Evaluate(json,family,(scope,path)=>sources[Key(scope,path)]);
        var rawOutputs=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var changes=new List<BatchChange>();int id=0;
        foreach(var result in results)
        {
            if(result.AlreadyApplied)continue;
            string key=Key(result.Scope,result.Path);byte[] next=result.Bytes!;
            changes.Add(new(++id,key,"SHA-256",CatchZonePatchEngine.Hash(sources[key]),CatchZonePatchEngine.Hash(next)));
            if(result.Scope.Equals("raw",StringComparison.OrdinalIgnoreCase))
            {rawOutputs.Add(Path.GetFullPath(Path.Combine(_project.GameConfiguration.RomFS,result.Path)),next);continue;}
            var member=members[key];
            if(member.Compressed)
            {
                byte[] encoded=Compress(next);using var decoded=new MemoryStream();LZSS.Decompress(new MemoryStream(encoded),encoded.Length,decoded);
                if(!decoded.ToArray().AsSpan().SequenceEqual(next))throw new InvalidDataException("Compresión Catch Zones no reversible.");
                next=encoded;
            }
            containers[member.Scope].Records[member.Index]=next;
        }
        var outputs=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in containers)
        {
            var container=item.Value;byte[] original=originals[container.Path];
            if(rawOutputs.ContainsKey(container.Path))throw new InvalidDataException("Parche raw y GARC dirigido al mismo archivo.");
            byte[][] before=new GARC.MemGARC(original).Files;
            outputs[container.Path]=before.Where((b,i)=>!b.AsSpan().SequenceEqual(container.Records[i])).Any()
                ?GarcTransaction.Build(original,_project.GameConfiguration.GetGARCReference(item.Key),container.Records,preserveRecordSize:false):original;
        }
        foreach(var pair in rawOutputs)outputs.Add(pair.Key,pair.Value);
        var files=originals.Select(p=>new FileChange(p.Key,p.Value,outputs.GetValueOrDefault(p.Key,p.Value))).ToArray();
        foreach(var file in files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        GarcTransaction.ReadUnchanged(_manifestPath,SHA256.HashData(json));
        return new(_owner,"Enable Catch Zones · "+family,0,files,changes,new Dictionary<string,string>{{"ActionId","catchzones.enable"},{"ManifestSHA256",CatchZonePatchEngine.Hash(json)}},
            files.Select(f=>(f.Path,f.Fingerprint)).Append((_manifestPath,SHA256.HashData(json))));
    }
    internal void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otro proyecto.");
        foreach(var dependency in proposal.Dependencies){CheckPath(dependency.Path);GarcTransaction.ReadUnchanged(dependency.Path,dependency.Fingerprint);}
        _project.History.Commit(proposal.Files,"Catch Zones",proposal.Action,proposal.Changes,proposal.Parameters);
    }
    internal static byte[] Compress(byte[] source)
    {
        string temp=Path.Combine(Path.GetTempPath(),"progressive-catch-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string input=Path.Combine(temp,"input"),output=Path.Combine(temp,"output");File.WriteAllBytes(input,source);
            LZSS.Compress(input,output);return File.ReadAllBytes(output);
        }
        finally { Directory.Delete(temp,true); }
    }
}
