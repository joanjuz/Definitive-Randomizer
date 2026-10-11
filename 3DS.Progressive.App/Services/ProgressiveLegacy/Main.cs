using System.IO;
using System.Threading;

namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

internal static class Main
{
    private static readonly AsyncLocal<string?> Root = new();
    internal static string Workspace => Root.Value ?? throw new InvalidOperationException("Falta el espacio temporal de los parches.");
    internal static string RomFSPath => Path.Combine(Workspace, "RomFS");
    internal static string ExeFSPath => Path.Combine(Workspace, "ExeFS");
    internal static IDisposable Enter(string workspace)
    {
        string? previous = Root.Value;
        Root.Value = workspace;
        return new Scope(() => Root.Value = previous);
    }
    private sealed class Scope(Action release) : IDisposable { public void Dispose() => release(); }
}

internal static class AutomaticBackups { internal static bool Enabled => false; }
