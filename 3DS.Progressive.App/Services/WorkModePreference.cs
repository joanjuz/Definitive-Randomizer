using System.IO;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// The work mode chosen on Inicio («Modo simplificado» / «Modo completo»), remembered per Windows user so that opening a project goes straight
/// to that mode. Local-only: never stored in a project, a ROM or a recipe. The automated checks only use a state folder given to them
/// (PROGRESSIVE_STATE_DIR) and never the preference of the person running them.
/// </summary>
internal static class WorkModePreference
{
    private const string Simplified="simplified",Complete="complete";
    private static readonly object Gate=new();

    private static string? FilePath=>Environment.GetEnvironmentVariable("PROGRESSIVE_STATE_DIR") is { Length: >0 } state?Path.Combine(state,"work-mode.json")
        :StudioDialog.AutomatedChecks?null
        :Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"3DS-Progressive","work-mode.json");

    private sealed record Stored(int Version,string Mode);

    /// <summary>true = simplified, false = complete, null = nothing valid remembered (first use, unreadable or unknown value).</summary>
    internal static bool? Load()
    {
        lock(Gate)
        {
            try
            {
                if(FilePath is not { } path||!File.Exists(path))return null;
                var stored=JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
                return stored is not { Version: 1 }?null:stored.Mode==Simplified?true:stored.Mode==Complete?false:null;
            }
            catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException){return null;}
        }
    }

    /// <summary>Remembers the explicit choice. Best effort: a preference that cannot be written never blocks the choice itself.</summary>
    internal static bool Save(bool simplified)
    {
        lock(Gate)
        {
            try
            {
                if(FilePath is not { } path)return false;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string tmp=path+".tmp";
                File.WriteAllText(tmp,JsonSerializer.Serialize(new Stored(1,simplified?Simplified:Complete)));
                File.Move(tmp,path,true);
                return true;
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or NotSupportedException){return false;}
        }
    }
}
