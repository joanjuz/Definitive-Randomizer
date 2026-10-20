using System.IO;
using System.Security.Cryptography;

namespace ThreeDS.Progressive.Services;

internal enum HistoryEntryState { Verified, OutOfDate, Unverified }
internal enum HistoryFileState { Matches, Changed, Missing, Invalid, Unreadable }
internal sealed record HistoryFileCheck(string Path,HistoryFileState State,string? ActualSha256=null);
internal sealed record HistoryInspection(HistoryDocument Document,IReadOnlyList<HistoryFileCheck> Files,
    IReadOnlyDictionary<Guid,HistoryEntryState> Entries,bool GameMatches)
{
    internal string Summary
    {
        get
        {
            int verified=Entries.Values.Count(value=>value==HistoryEntryState.Verified);
            int stale=Entries.Values.Count(value=>value==HistoryEntryState.OutOfDate);
            int unknown=Entries.Values.Count(value=>value==HistoryEntryState.Unverified);
            return $"{verified} registros coherentes con los archivos actuales · {stale} desfasados · {unknown} sin comprobación binaria."+
                (GameMatches?"":" El historial corresponde a otro juego.");
        }
    }
}

public sealed partial class ProjectHistory
{
    // Verify the terminal fingerprint once per file, retaining only the continuous
    // history segment that leads to it. A new edit after an external reset must
    // never make edits from the previous ROM appear valid again.
    internal HistoryInspection InspectCurrentState(CancellationToken cancellationToken=default)
    {
        lock(_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document=Read();
            bool sameGame=string.Equals(document.Game,_game,StringComparison.OrdinalIgnoreCase);
            var comparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
            var tracked=new Dictionary<string,(HistoryFile File,int Segment)>(comparer);
            var requirements=new Dictionary<Guid,List<(string Path,int Segment)>>();
            var invalid=new HashSet<Guid>();
            foreach(var entry in document.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var paths=new List<(string,int)>();requirements[entry.Id]=paths;
                foreach(var file in entry.Files)
                {
                    string? full=SafeHistoryPath(file.Path);
                    if(full is null||!ValidHash(file.BeforeSha256)||!ValidHash(file.AfterSha256)){invalid.Add(entry.Id);continue;}
                    int segment=tracked.TryGetValue(full,out var previous)?previous.Segment+
                        (string.Equals(previous.File.AfterSha256,file.BeforeSha256,StringComparison.OrdinalIgnoreCase)?0:1):0;
                    tracked[full]=(file,segment);paths.Add((full,segment));
                }
            }
            byte[] buffer=new byte[128*1024];
            var checks=new Dictionary<string,HistoryFileCheck>(comparer);
            foreach(var (full,expected) in tracked)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HistoryFileState state;string? actual=null;
                try
                {
                    if(!sameGame||HasLinkBelowRoot(full))state=HistoryFileState.Invalid;
                    else if(!File.Exists(full))state=HistoryFileState.Missing;
                    else
                    {
                        using var input=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read,buffer.Length,FileOptions.SequentialScan);
                        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        int read;
                        while((read=input.Read(buffer,0,buffer.Length))>0){cancellationToken.ThrowIfCancellationRequested();hash.AppendData(buffer,0,read);}
                        cancellationToken.ThrowIfCancellationRequested();actual=Convert.ToHexString(hash.GetHashAndReset());
                        state=actual.Equals(expected.File.AfterSha256,StringComparison.OrdinalIgnoreCase)?HistoryFileState.Matches:HistoryFileState.Changed;
                    }
                }
                catch(FileNotFoundException){state=HistoryFileState.Missing;}
                catch(DirectoryNotFoundException){state=HistoryFileState.Missing;}
                catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){state=HistoryFileState.Unreadable;}
                checks[full]=new(expected.File.Path,state,actual);
            }
            var states=new Dictionary<Guid,HistoryEntryState>();
            foreach(var entry in document.Entries)
            {
                var paths=requirements[entry.Id];
                states[entry.Id]=!sameGame||invalid.Contains(entry.Id)||paths.Any(path=>tracked[path.Path].Segment!=path.Segment||checks[path.Path].State!=HistoryFileState.Matches)
                    ?HistoryEntryState.OutOfDate:paths.Count==0?HistoryEntryState.Unverified:HistoryEntryState.Verified;
            }
            return new(document,checks.Values.ToArray(),states,sameGame);
        }
    }
    private static bool ValidHash(string? hash)=>hash is {Length:64}&&hash.All(Uri.IsHexDigit);
    private string? SafeHistoryPath(string path)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(path)||path.Split('/','\\').Contains(".."))return null;
            string relative=path.Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar);
            if(Path.IsPathRooted(relative))return null;
            string full=Path.GetFullPath(Path.Combine(_root,relative));
            return full.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)?full:null;
        }
        catch(Exception ex)when(ex is ArgumentException or NotSupportedException or PathTooLongException){return null;}
    }
    private bool HasLinkBelowRoot(string path)
    {
        for(string? current=path;current is not null&&!string.Equals(current,_root,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return true;
        return false;
    }
}
