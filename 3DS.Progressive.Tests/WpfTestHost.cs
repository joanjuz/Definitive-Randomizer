using System.Reflection;
using System.Windows;

internal static class WpfTestHost
{
    internal static void SuppressStartup()
    {
        // The public StartupUri setter rejects null in WPF. Clear the pending
        // resource startup only in the test process, before pumping the dispatcher.
        // Fail explicitly if a future WPF version changes this implementation.
        var startup=typeof(Application).GetField("_startupUri",BindingFlags.Instance|BindingFlags.NonPublic)
            ??throw new InvalidOperationException("WPF test host cannot suppress StartupUri.");
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        startup.SetValue(Application.Current,null);
        Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
    }
}
