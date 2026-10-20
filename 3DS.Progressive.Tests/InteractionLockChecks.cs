using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive;
internal static class InteractionLockChecks
{
    private static int[] Subscribers()=>new[]{"InteractionLockChanged","Changed","Reported","Telemetry"}.Select(name=>
        ((Delegate?)typeof(UiProgress).GetField(name,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length??0).ToArray();
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ClosedWindow()
    {
        var window=new MainWindow();window.Close();return new WeakReference(window);
    }
    internal static void Run(Action<bool,string> check)
    {
        WpfTestHost.SuppressStartup();
        var shutdown=Application.Current.ShutdownMode;
        Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try
        {
            int[] before=Subscribers();
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
            UiProgress.Begin("lifecycle probe");
            var timer=(System.Windows.Threading.DispatcherTimer)typeof(MainWindow).GetField("_operationTimer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
            check(timer.IsEnabled,"WindowLifecycle_ProgressStartsTimer");
            // Queue worker updates but close before the dispatcher processes them.
            Task.Run(()=>UiProgress.Report("queued before close",42)).GetAwaiter().GetResult();
            main.Close();
            check(Subscribers().SequenceEqual(before),"WindowLifecycle_CloseRemovesAllStaticProgressHandlers");
            check(!timer.IsEnabled,"WindowLifecycle_CloseStopsTimer");
            var status=(TextBlock)main.FindName("StatusText");string previous=status.Text;
            UiProgress.Begin("late operation");UiProgress.Report("late result",50);UiProgress.End();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
            check(status.Text==previous&&!timer.IsEnabled,"WindowLifecycle_ClosedWindowIgnoresProgress");
            using var fixture=new Fixture(pk3DS.Core.GameVersion.USUM);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(MainWindow).GetField("_session",flags)!.SetValue(main,fixture.Session);
            var host=(ContentControl)main.FindName("SectionHost");object? original=host.Content;
            typeof(MainWindow).GetMethod("Section_Click",flags)!.Invoke(main,[new Button{Tag="Encounters"},new RoutedEventArgs()]);
            check(ReferenceEquals(host.Content,original),"WindowLifecycle_ClosedWindowRejectsNavigation");
            typeof(MainWindow).GetField("_session",flags)!.SetValue(main,null);
            main.LoadProject(fixture.Session);
            check(typeof(MainWindow).GetField("_session",flags)!.GetValue(main) is null&&ReferenceEquals(host.Content,original),"WindowLifecycle_ClosedWindowRejectsProjectPublication");
            foreach(string section in new[]{"Encounters","FieldItems"})
            {
                var loading=new MainWindow();
                typeof(MainWindow).GetField("_session",flags)!.SetValue(loading,fixture.Session);
                var loadingHost=(ContentControl)loading.FindName("SectionHost");
                object? previousContent=loadingHost.Content;
                try
                {
                    StudioDialog.AutomatedChecks=false;
                    typeof(MainWindow).GetMethod("Section_Click",flags)!.Invoke(loading,[new Button{Tag=section},new RoutedEventArgs()]);
                    var source=(CancellationTokenSource)typeof(MainWindow).GetField("_sectionLoadCancellation",flags)!.GetValue(loading)!;
                    var token=source.Token;
                    check(UiProgress.IsInteractionBlocked,"WindowLifecycle_AsyncLoadHoldsInteraction_"+section);
                    loading.Close();
                    var watch=System.Diagnostics.Stopwatch.StartNew();
                    while(typeof(MainWindow).GetField("_sectionLoadCancellation",flags)!.GetValue(loading) is not null&&watch.Elapsed<TimeSpan.FromSeconds(5))
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
                    check(token.IsCancellationRequested&&typeof(MainWindow).GetField("_sectionLoadCancellation",flags)!.GetValue(loading) is null,
                        "WindowLifecycle_CloseCancelsAsyncLoad_"+section);
                    check(ReferenceEquals(previousContent,loadingHost.Content)&&!UiProgress.IsInteractionBlocked,
                        "WindowLifecycle_CanceledLoadCannotPublishAndUnlocks_"+section);
                }
                finally{StudioDialog.AutomatedChecks=true;loading.Close();}
            }
            var closed=Enumerable.Range(0,5).Select(_=>ClosedWindow()).ToArray();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            check(closed.All(reference=>!reference.IsAlive),"WindowLifecycle_RepeatedClosedWindowsAreCollectible");
            check(Subscribers().SequenceEqual(before),"WindowLifecycle_RepeatedCloseDoesNotAccumulateSubscriptions");
        }
        finally{Application.Current.ShutdownMode=shutdown;}
    }
}
