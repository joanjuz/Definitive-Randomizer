using System.Windows;
namespace ThreeDS.Progressive;
public partial class MainWindow
{
    // The DS randomizer works on its own ROM file and never touches the open 3DS project or the Switch workspace.
    private void OpenNdsRandomizer()
    {
        if(_closed||UiProgress.IsInteractionBlocked||_switchWorkspace?.Busy==true)return;
        var window=new NdsRandomizerWindow();if(IsVisible)window.Owner=this;window.ShowDialog();
    }
}
