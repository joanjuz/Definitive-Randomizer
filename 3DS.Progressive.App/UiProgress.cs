using System.Diagnostics;

namespace ThreeDS.Progressive;

/// <summary>General operations report measured work, stage changes and elapsed time.
/// Unknown totals stay indeterminate: never fabricate an estimated percentage.</summary>
internal sealed record OperationTelemetry(string Title,string Stage,double? Percent,
    int? Completed,int? Total,TimeSpan Elapsed,bool Busy);

internal static class UiProgress
{
    private static readonly object Gate=new();
    private static readonly Stopwatch Clock=new();
    private static string _title="",_stage="";
    private static double? _percentage;
    private static int? _completed,_total;
    internal static event Action<string,bool>? Changed;
    internal static event Action<string,double>? Reported;
    internal static event Action<OperationTelemetry>? Telemetry;
    internal static OperationTelemetry Snapshot
    {
        get {lock(Gate)return Current(true);}
    }
    private static OperationTelemetry Current(bool busy)=>new(_title,_stage,_percentage,
        _completed,_total,Clock.Elapsed,busy);
    internal static void Begin(string message)
    {
        OperationTelemetry snapshot;
        lock(Gate)
        {
            _title=message;_stage="Iniciando…";_percentage=null;_completed=_total=null;
            Clock.Restart();snapshot=Current(true);
        }
        Changed?.Invoke(message,true);Telemetry?.Invoke(snapshot);
    }
    internal static void Stage(string message)
    {
        OperationTelemetry snapshot;
        lock(Gate)
        {
            if(!Clock.IsRunning)return;
            _stage=message;snapshot=Current(true);
        }
        Telemetry?.Invoke(snapshot);
    }
    internal static void Report(string message,double percentage)
    {
        OperationTelemetry snapshot;
        lock(Gate)
        {
            _stage=message;_percentage=Math.Clamp(percentage,0,100);
            snapshot=Current(true);
        }
        Reported?.Invoke(message,snapshot.Percent!.Value);Telemetry?.Invoke(snapshot);
    }
    internal static void Count(string stage,int done,int total)
    {
        if(total<1||done<0||done>total)throw new ArgumentOutOfRangeException(nameof(done));
        OperationTelemetry snapshot;
        lock(Gate)
        {
            _stage=stage;_completed=done;_total=total;
            _percentage=100d*done/total;snapshot=Current(true);
        }
        Reported?.Invoke(stage,snapshot.Percent!.Value);Telemetry?.Invoke(snapshot);
    }
    internal static void End()
    {
        OperationTelemetry snapshot;
        lock(Gate)
        {
            Clock.Stop();snapshot=Current(false);_percentage=null;_completed=null;_total=null;
        }
        Telemetry?.Invoke(snapshot);Changed?.Invoke("",false);
    }
}
