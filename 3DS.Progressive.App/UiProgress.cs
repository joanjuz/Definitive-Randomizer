namespace ThreeDS.Progressive;

/// <summary>Shared progress indicator for lengthy WPF editor operations.</summary>
internal static class UiProgress
{
    internal static event Action<string,bool>? Changed;
    internal static void Begin(string message)=>Changed?.Invoke(message,true);
    internal static void End()=>Changed?.Invoke("",false);
}
