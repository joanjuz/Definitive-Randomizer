using System.Windows.Threading;

/// <summary>
/// Shared dispatcher wait for the off-screen UI checks. «Idle» waits until bindings, style triggers and layout have
/// settled (ContextIdle); waiting only to DataBind can observe a style trigger before it applies.
/// </summary>
internal static class UiPump
{
    internal static void Idle()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
}
