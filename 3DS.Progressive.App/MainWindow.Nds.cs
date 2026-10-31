using System.IO;
using System.Windows;
namespace ThreeDS.Progressive;
public partial class MainWindow
{
    private NdsWorkspaceView? _ndsWorkspace;
    // The DS workspace shares the platform host and header with Sword/Shield; the open 3DS project is never touched.
    private bool NdsVisible=>SwitchHost.Visibility==Visibility.Visible&&SwitchHost.Content is NdsWorkspaceView;
    internal NdsWorkspaceView? NdsWorkspace=>_ndsWorkspace;
    private void OpenNdsRandomizer()
    {
        if(_closed||UiProgress.IsInteractionBlocked||_switchWorkspace?.Busy==true||_ndsWorkspace?.Busy==true)return;
        if(SwitchHost.Visibility==Visibility.Visible&&SwitchHost.Content is SwitchWorkspaceView current&&!current.CanLeave())return;
        if(_ndsWorkspace is null){_ndsWorkspace=new NdsWorkspaceView(this);_ndsWorkspace.ContextChanged+=RefreshNdsHeader;}
        HomeHost.Visibility=Visibility.Collapsed;EditorShell.Visibility=Visibility.Collapsed;
        SwitchHost.Content=_ndsWorkspace;SwitchHost.Visibility=Visibility.Visible;
        if(_switchHeader is null){_switchRebuildLabel=RebuildButton.Content;_switchProjectLabel=ProjectNameText.Text;_switchGameLabel=GameNameText.Text;_switchHeader=new[]{BackupButton,RebuildButton,ExtractRomButton,RestoreBackupButton,OpenProjectButton}.ToDictionary(b=>b,b=>b.IsEnabled);}
        _switchHeaderVisibility??=new[]{BackupButton,ExtractRomButton,RestoreBackupButton}.ToDictionary(b=>b,b=>b.Visibility);
        foreach(var button in _switchHeaderVisibility.Keys)button.Visibility=Visibility.Collapsed;
        BackupButton.IsEnabled=ExtractRomButton.IsEnabled=RestoreBackupButton.IsEnabled=false;RebuildButton.Content="Exportar ROM…";OpenProjectButton.IsEnabled=true;RefreshNdsHeader();
    }
    private void RefreshNdsHeader()
    {
        if(!NdsVisible||_ndsWorkspace is null)return;
        var project=_ndsWorkspace.Project;
        ProjectNameText.Text=project is null?"Nintendo DS":Path.GetFileNameWithoutExtension(project.SourcePath);
        GameNameText.Text=project is null?"DS · abre una ROM para comenzar":project.Game+(project.IsRomhack?" · romhack":"")+" · ROM nueva al exportar";
        RebuildButton.IsEnabled=project?.HasChanges==true;
        StatusText.Text=project is null?"Abre una ROM de Nintendo DS para comenzar.":project.Game+" · ROM original en lectura · "+(project.HasChanges?"Cambios pendientes de exportar":"Sin cambios pendientes");
    }
}
