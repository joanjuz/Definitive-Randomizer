using System.Diagnostics;
using System.IO;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Local timings of the slow steps (opening a project, reading the applied options, loading a section), appended to
/// %LOCALAPPDATA%\3DS-Progressive\logs\timings.log so a regression can be measured on real projects. Only names,
/// durations and the game are written (no paths or data). Disabled in automated checks; failures are ignored.
/// </summary>
internal static class TimingLog
{
    private static readonly string FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"3DS-Progressive","logs","timings.log");
    private static readonly object Gate=new();
    internal static bool Enabled=>!ThreeDS.Progressive.StudioDialog.AutomatedChecks;
    internal sealed class Scope(string step,string game):IDisposable
    {
        private readonly Stopwatch _watch=Stopwatch.StartNew();
        public void Dispose()=>Write(step,game,_watch.Elapsed);
    }
    internal static Scope Measure(string step,string game)=>new(step,game);
    internal static void Write(string step,string game,TimeSpan elapsed)
    {
        if(!Enabled)return;
        try
        {
            lock(Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if(File.Exists(FilePath)&&new FileInfo(FilePath).Length>2*1024*1024)File.Move(FilePath,FilePath+".old",true);
                File.AppendAllText(FilePath,$"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{game}\t{step}\t{elapsed.TotalMilliseconds:0} ms{Environment.NewLine}");
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
    }
}
