using System.IO;
using System.Threading;

namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

internal static class Main
{
    private static readonly AsyncLocal<string?> Root = new();
    private static readonly AsyncLocal<pk3DS.Core.GameConfig?> Configuration = new();
    internal static pk3DS.Core.GameConfig Config => Configuration.Value ?? throw new InvalidOperationException("Falta la configuración del motor original.");
    internal static pk3DS.Core.Structures.PersonalInfo.PersonalInfo[] SpeciesStat => Config.Personal.Table;
    internal static string Workspace => Root.Value ?? throw new InvalidOperationException("Falta el espacio temporal de los parches.");
    internal static string RomFSPath => Path.Combine(Workspace, "RomFS");
    internal static string ExeFSPath => Path.Combine(Workspace, "ExeFS");
    internal static IDisposable Enter(string workspace,pk3DS.Core.GameConfig? configuration=null)
    {
        string? previous = Root.Value;
        var oldConfig=Configuration.Value;
        Root.Value = workspace;
        Configuration.Value=configuration??oldConfig;
        return new Scope(() => {Root.Value = previous;Configuration.Value=oldConfig;});
    }
    private sealed class Scope(Action release) : IDisposable { public void Dispose() => release(); }
}

internal static class AutomaticBackups { internal static bool Enabled => false; }
