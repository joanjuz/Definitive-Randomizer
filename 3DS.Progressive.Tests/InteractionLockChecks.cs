using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive;
internal static class InteractionLockChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var main=new MainWindow();var open=(Button)main.FindName("OpenProjectButton");var backup=(Button)main.FindName("BackupButton");
        bool initialOpen=open.IsEnabled,initialBackup=backup.IsEnabled;
        var outer=UiProgress.BlockInteraction();
        check(UiProgress.IsInteractionBlocked&&!open.IsEnabled&&!backup.IsEnabled,"global operation disables header and main content without showing a window");
        using(UiProgress.BlockInteraction())check(!open.IsEnabled,"nested operation keeps navigation disabled");
        check(UiProgress.IsInteractionBlocked&&!open.IsEnabled,"inner completion cannot unlock outer operation");
        outer.Dispose();outer.Dispose();
        check(!UiProgress.IsInteractionBlocked&&open.IsEnabled==initialOpen&&backup.IsEnabled==initialBackup,"completion restores each button's previous availability; disposal is idempotent");
        try{using var failed=UiProgress.BlockInteraction();throw new System.IO.IOException("failure probe");}catch(System.IO.IOException){}
        check(!UiProgress.IsInteractionBlocked&&open.IsEnabled==initialOpen,"failure releases application interaction lock");
        main.Close();
    }
}
