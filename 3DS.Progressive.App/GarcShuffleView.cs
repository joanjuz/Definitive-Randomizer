using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using DataGrid=System.Windows.Controls.DataGrid;
using UserControl=System.Windows.Controls.UserControl;
namespace ThreeDS.Progressive;
internal sealed class GarcShuffleView : UserControl
{
    private readonly GameGarcShuffleService _service;
    private readonly ComboBox _files;
    private readonly DataGrid _changes;
    private readonly Button _preview,_apply=new();
    private readonly TextBlock _status=new();
    private GarcShufflePreview? _proposal;
    private bool _busy;
    internal bool ConfirmLeave()=>!_busy&&(_proposal is null||StudioDialog.Confirm(Window.GetWindow(this),"GARC pendiente","¿Descartar la propuesta sin aplicar?"));
    internal GarcShuffleView(GameProjectSession project)
    {
        _service=new(project);Background=(Brush)FindResource("Bg");var root=new DockPanel{Margin=new Thickness(22)};Content=root;
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text="GARC Shuffler · Gen6",FontSize=24,Foreground=(Brush)FindResource("TextMain")});
        header.Children.Add(new TextBlock{Text="Mezcla referencias a archivos simples sin alterar el contenido binario. Conserva carpetas y la lista de archivos protegidos de vanilla. Requiere guardar un backup antes de aplicar.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,8,0,12)});
        _files=new ComboBox{ItemsSource=_service.Files,SelectedIndex=0,Margin=new Thickness(0,0,0,12)};header.Children.Add(_files);_files.SelectionChanged+=(_,_)=>{_proposal=null;_apply.IsEnabled=false;};
        var buttons=new WrapPanel();header.Children.Add(buttons);_preview=new Button{Content="Generar vista previa",Margin=new Thickness(0,0,8,12)};_apply=new Button{Content="Backup y aplicar",IsEnabled=false,Margin=new Thickness(0,0,8,12)};buttons.Children.Add(_preview);buttons.Children.Add(_apply);
        _preview.Click+=async(_,_)=>await Preview();_apply.Click+=async(_,_)=>await Apply();
        _status.Foreground=(Brush)FindResource("TextSoft");_status.Margin=new Thickness(0,10,0,0);DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);
        _changes=new DataGrid{AutoGenerateColumns=true,CanUserAddRows=false,IsReadOnly=true};DarkGridTheme.Apply(_changes);root.Children.Add(_changes);
    }
    private void Busy(bool value){_busy=value;_files.IsEnabled=!value;_preview.IsEnabled=!value;_apply.IsEnabled=!value&&_proposal?.Changes.Count>0;}
    private async Task Preview()
    {
        if(_files.SelectedItem is not string file)return;Busy(true);UiProgress.Begin("Analizando referencias GARC…");
        try{_proposal=await Task.Run(()=>_service.Preview(file,System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue)));_changes.ItemsSource=_proposal.Changes;_status.Text=$"{_proposal.Changes.Count} referencias diferentes; no se guardaron cambios.";}
        catch(Exception ex){_proposal=null;StudioDialog.Error(Window.GetWindow(this),"GARC Shuffler",ex.Message);}
        finally{Busy(false);UiProgress.End();}
    }
    private async Task Apply()
    {
        if(_proposal is not {} proposal||proposal.Changes.Count==0)return;
        var picker=new Microsoft.Win32.SaveFileDialog{Filter="Backup ZIP|*.zip",FileName="pre-shuffle-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"};if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Aplicar Shuffler",$"¿Mezclar {proposal.Changes.Count} referencias de {proposal.Relative}? El resultado requiere una prueba en juego; los recursos mezclados pueden cambiar su comportamiento."))return;
        Busy(true);UiProgress.Begin("Guardando backup y referencias GARC…");
        try{await Task.Run(()=>_service.Apply(proposal,picker.FileName));_proposal=null;_status.Text="GARC verificado y backup ZIP guardado; se puede recuperar con restauración selectiva.";}
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"GARC Shuffler",ex.Message);}
        finally{Busy(false);UiProgress.End();}
    }
}
