using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed record HistoryFile(string Path, string BeforeSha256, string AfterSha256);
public sealed record HistoryEntry(Guid Id, DateTimeOffset Time, string Module, string Action,
    IReadOnlyList<BatchChange> Changes, IReadOnlyDictionary<string,string> Parameters,
    IReadOnlyList<HistoryFile> Files);
public sealed record HistoryDocument(int Version, string Game, string Project, IReadOnlyList<HistoryEntry> Entries);

/// <summary>The journal participates in the same verified commit as the edited game files.</summary>
public sealed class ProjectHistory
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,object> Locks = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented=true };
    private readonly string _root, _game, _path;
    private readonly object _gate;
    public ProjectHistory(string projectRoot, string game, string? storageRoot=null)
    {
        _root=Path.GetFullPath(projectRoot); _game=game;
        string key=OperatingSystem.IsWindows()?_root.ToUpperInvariant():_root;
        string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        storageRoot ??= Environment.GetEnvironmentVariable("PROGRESSIVE_STATE_DIR");
        // Portable metadata outside RomFS/ExeFS; opening the project never creates it.
        _path=storageRoot is null?Path.Combine(_root,"3ds-progressive-history.json"):
            Path.Combine(storageRoot,"history",id+".json");
        _gate=Locks.GetOrAdd(_path,_=>new object());
    }
    public HistoryDocument Read()
    {
        lock(_gate) return File.Exists(_path)
            ? JsonSerializer.Deserialize<HistoryDocument>(File.ReadAllBytes(_path),Json)
                ?? throw new InvalidDataException("El historial no tiene un formato válido.")
            : new(1,_game,Path.GetFileName(_root),[]);
    }
    internal void Commit(IReadOnlyList<FileChange> files,string module,string action,
        IEnumerable<BatchChange> changes,IReadOnlyDictionary<string,string>? parameters=null)
    {
        lock(_gate)
        {
            var actual=files.Where(f=>f.Changed).ToArray();
            if(actual.Length==0)return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // File.Replace requires a target. Initializing the empty journal changes no game data.
            if(!File.Exists(_path)) File.WriteAllBytes(_path,JsonSerializer.SerializeToUtf8Bytes(Read(),Json));
            byte[] source=File.ReadAllBytes(_path);
            var document=JsonSerializer.Deserialize<HistoryDocument>(source,Json)
                ?? throw new InvalidDataException("No se puede leer el historial.");
            var entry=new HistoryEntry(Guid.NewGuid(),DateTimeOffset.UtcNow,module,action,changes.ToArray(),
                new Dictionary<string,string>(parameters??new Dictionary<string,string>()),
                actual.Select(f=>new HistoryFile(Path.GetRelativePath(_root,f.Path),Hash(f.Original()),Hash(f.Proposed()))).ToArray());
            byte[] proposed=JsonSerializer.SerializeToUtf8Bytes(document with {Entries=document.Entries.Append(entry).ToArray()},Json);
            FileTransaction.Commit([..actual,new FileChange(_path,source,proposed)]);
        }
    }
    public void Export(string path) => File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(Read(),Json));
    internal byte[] CommitGarc(string path,pk3DS.Core.GARCReference reference,byte[] fingerprint,byte[][] records,
        string module,string action,IEnumerable<BatchChange> changes,IReadOnlyDictionary<string,string>? parameters=null,bool mini=false)
    {
        byte[] source=GarcTransaction.ReadUnchanged(path,fingerprint);
        byte[] output=GarcTransaction.Build(source,reference,records,mini);
        Commit([new FileChange(path,source,output)],module,action,changes,parameters);
        return SHA256.HashData(output);
    }
    internal static IEnumerable<BatchChange> ValueChanges<T>(int id,string name,T before,T after)=>
        typeof(T).GetProperties().Where(p=>p.CanRead).Select(p=>new BatchChange(id,name,p.Name,
            Convert.ToString(p.GetValue(before),System.Globalization.CultureInfo.InvariantCulture)??"",
            Convert.ToString(p.GetValue(after),System.Globalization.CultureInfo.InvariantCulture)??""))
        .Where(c=>c.Before!=c.After);
    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    internal static Dictionary<string,string> Settings(BatchSettings settings,IEnumerable<int>? scope=null,
        IEnumerable<(string Path,byte[] Fingerprint)>? dependencies=null)
    {
        var result=new Dictionary<string,string> { ["Settings"]=JsonSerializer.Serialize(settings), ["Seed"]=settings.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if(scope is not null)result["Scope"]=string.Join(",",scope);
        if(dependencies is not null)result["Dependencies"]=JsonSerializer.Serialize(dependencies.Select(d=>new {FileName=Path.GetFileName(d.Path),Sha256=Convert.ToHexString(d.Fingerprint)}));
        return result;
    }
}
