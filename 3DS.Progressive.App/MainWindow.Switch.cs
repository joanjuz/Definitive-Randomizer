using System.Windows;
namespace ThreeDS.Progressive;
public partial class MainWindow
{
    private SwitchWorkspaceView? _switchWorkspace;
    private Dictionary<System.Windows.Controls.Button,bool>? _switchHeader;
    private Dictionary<System.Windows.Controls.Button,Visibility>? _switchHeaderVisibility;
    private object? _switchRebuildLabel;
    private string? _switchProjectLabel,_switchGameLabel;
    private void HideSwitchWorkspace()
    {
        SwitchHost.Visibility=Visibility.Collapsed;EditorShell.Visibility=Visibility.Visible;
        if(_switchHeaderVisibility is not null){foreach(var p in _switchHeaderVisibility)p.Key.Visibility=p.Value;_switchHeaderVisibility=null;}
        if(_switchHeader is null)return;foreach(var p in _switchHeader)p.Key.IsEnabled=p.Value;RebuildButton.Content=_switchRebuildLabel;ProjectNameText.Text=_switchProjectLabel;GameNameText.Text=_switchGameLabel;_switchHeader=null;
    }
    private void RefreshSwitchHeader()
    {
        if(SwitchHost.Visibility!=Visibility.Visible||_switchWorkspace is null)return;
        var project=_switchWorkspace.Project;string? name=project is null?null:System.IO.Path.GetFileName(project.Root);ProjectNameText.Text=project is null?"Espada/Escudo":string.Equals(name,project.Identity.TitleId,StringComparison.OrdinalIgnoreCase)?project.Identity.Game:name;
        GameNameText.Text=project is null?"Switch · abre una extracción para comenzar":project.Identity.Game+(project.Identity.CanEdit?" · perfil 1.3.2 · mod":" · solo lectura");
        RebuildButton.IsEnabled=project?.HasChanges==true;
        StatusText.Text=project is null?"Abre una extracción de Espada/Escudo para comenzar.":project.Identity.Game+" · extracción en lectura · "+(project.HasChanges?"Cambios pendientes de exportar":"Sin cambios pendientes");
    }
    private void OpenSwitchPrototype()
    {
        if(_closed||UiProgress.IsInteractionBlocked)return;
        if(_switchWorkspace is null){_switchWorkspace=new SwitchWorkspaceView(this,()=>Home_Click(BrandHomeButton,new RoutedEventArgs()));_switchWorkspace.ContextChanged+=RefreshSwitchHeader;}
        HomeHost.Visibility=Visibility.Collapsed;EditorShell.Visibility=Visibility.Collapsed;
        SwitchHost.Content=_switchWorkspace;SwitchHost.Visibility=Visibility.Visible;
        if(_switchHeader is null){_switchRebuildLabel=RebuildButton.Content;_switchProjectLabel=ProjectNameText.Text;_switchGameLabel=GameNameText.Text;_switchHeader=new[]{BackupButton,RebuildButton,ExtractRomButton,RestoreBackupButton,OpenProjectButton}.ToDictionary(b=>b,b=>b.IsEnabled);}
        _switchHeaderVisibility??=new[]{BackupButton,ExtractRomButton,RestoreBackupButton}.ToDictionary(b=>b,b=>b.Visibility);
        foreach(var button in _switchHeaderVisibility.Keys)button.Visibility=Visibility.Collapsed;
        ProjectNameText.Text="Espada/Escudo";GameNameText.Text="Switch · exportación de mods";
        BackupButton.IsEnabled=ExtractRomButton.IsEnabled=RestoreBackupButton.IsEnabled=false;RebuildButton.Content="Exportar mod…";OpenProjectButton.IsEnabled=true;RefreshSwitchHeader();
    }
}
