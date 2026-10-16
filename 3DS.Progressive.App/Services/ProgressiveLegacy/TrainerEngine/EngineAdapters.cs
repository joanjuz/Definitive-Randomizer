#nullable disable
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using pk3DS.Core;
namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

// Original forms are used only as an isolated in-memory logic host, never displayed.
// The WPF inspector owns options, preview and confirmation. No original settings are written.
internal static class WinFormsUtil
{
    internal static readonly List<string> Messages=[];
    public static DialogResult Prompt(MessageBoxButtons buttons,params string[] text)=>DialogResult.Yes;
    public static void Alert(params string[] text)=>Messages.Add(string.Join(" ",text));
    public static int ToInt32(Control text)=>int.TryParse(text.Text,out int value)?value:0;
    public static Bitmap GetSprite(int species,int form,int gender,int item,GameConfig config,bool shiny=false)=>null;
    public static Bitmap ScaleImage(Bitmap image,int scale)=>null;
}
internal static class RandSettings
{
    public static void GetFormSettings(Form form,Control.ControlCollection controls) { }
    public static void SetFormSettings(Form form,Control.ControlCollection controls) { }
}
internal sealed class TrainerRandomizeProgress : IDisposable
{
    public static TrainerRandomizeProgress Show(Form owner,string title,int total)=>new();
    public void Report(int completed,int total,string name) { }
    public void Dispose() { }
}
public sealed class GlobalRandomizationAction
{
    public string Id {get;set;}="";
    public Dictionary<string,string> Parameters {get;set;}=[];
}
internal static class RandomizationSessionState
{
    internal static void MarkAction(string id) { }
    internal static IEnumerable<GlobalRandomizationAction> ExportActions()=>[];
}
internal static class TrainerEngineHooks
{
    internal static Action<Form> Configure = _=>{};
    internal static void Invoke(object target,string method)
    {
        try {target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,[null,EventArgs.Empty]);}
        catch(TargetInvocationException ex) when(ex.InnerException is not null)
        {System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}
    }
}

internal static class ModernUI { internal static Color Background=>Color.Black; internal static Color MutedText=>Color.Gray; internal static void Apply(Form form){} }
