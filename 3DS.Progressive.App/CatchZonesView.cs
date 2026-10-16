using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using UserControl=System.Windows.Controls.UserControl;
using Brush=System.Windows.Media.Brush;
using DataGrid=System.Windows.Controls.DataGrid;

namespace ThreeDS.Progressive;

internal sealed class CatchZonesView : UserControl
{
    private readonly GameCatchZoneService _service;

    private readonly Button _preview,_apply;
    private readonly DataGrid _changes;
    private readonly TextBlock _status;
    private EditorProposal? _proposal;
    private bool _busy;
    internal bool ConfirmLeave()=>!_busy;
    internal CatchZonesView(GameProjectSession project)
    {
        _service=new(project);Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Enable Catch Zones · Gen6",FontSize=23,Foreground=(Brush)FindResource("TextMain")});
        top.Children.Add(new TextBlock{Text="Habilita zonas de captura en XY/ORAS con los parches Progressive. Verifica encuentros, mapas y archivos adicionales antes de guardar. Si hay modificaciones previas incompatibles, no se aplica el parche.",TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,8,0,12),Foreground=(Brush)FindResource("TextSoft")});
        var buttons=new WrapPanel();top.Children.Add(buttons);
        _preview=new Button{Content="Generar vista previa",Margin=new Thickness(0,0,10,12)};
        _preview.Click+=async(_,_)=>await PreviewAsync();buttons.Children.Add(_preview);
        _apply=new Button{Content="Aplicar y verificar",IsEnabled=false,Margin=new Thickness(0,0,0,12)};
        _apply.Click+=async(_,_)=>await ApplyAsync();buttons.Children.Add(_apply);
        _status=new TextBlock{Text="Sin cambios preparados.",Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
        DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);
        _changes=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false};
        foreach(var column in new[]{("Archivo","Name"),("Cambio","Field"),("Antes","Before"),("Después","After")})
            _changes.Columns.Add(new DataGridTextColumn{MinWidth=180,Header=column.Item1,Binding=new System.Windows.Data.Binding(column.Item2),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        DarkGridTheme.Apply(_changes);root.Children.Add(_changes);
    }
    private void Invalidate(){_proposal=null;_apply.IsEnabled=false;_changes.ItemsSource=null;}
    private void Busy(bool value){_busy=value;_preview.IsEnabled=!value;_apply.IsEnabled=!value&&_proposal?.Changes.Count>0;}
    private async Task PreviewAsync()
    {
        if(_busy)return;Invalidate();Busy(true);UiProgress.Begin("Preparando Catch Zones");
        try{_proposal=await Task.Run(()=>_service.Preview());_changes.ItemsSource=_proposal.Changes;
            _status.Text=_proposal.Changes.Count==0?"Los parches ya están aplicados. No hay archivos que guardar.":$"{_proposal.Changes.Count} archivos preparados. Revisá la vista previa antes de aplicar.";}
        catch(Exception ex){_status.Text="No se pudo preparar: "+ex.Message;StudioDialog.Error(Window.GetWindow(this),"Catch Zones",ex.Message);}
        finally{Busy(false);UiProgress.End();}
    }
    private async Task ApplyAsync()
    {
        if(_busy||_proposal is null)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Habilitar zonas de captura",$"Guardar los {_proposal.Changes.Count} archivos de la vista previa y registrarlos en el historial?"))return;
        var proposal=_proposal;Busy(true);UiProgress.Begin("Guardando Catch Zones");
        try{await Task.Run(()=>_service.Apply(proposal));Invalidate();_status.Text="Zonas guardadas y relectura verificada. El cambio quedó registrado en el historial.";}
        catch(Exception ex){Invalidate();_status.Text="No se pudo aplicar: "+ex.Message;StudioDialog.Error(Window.GetWindow(this),"Catch Zones",ex.Message);}
        finally{Busy(false);UiProgress.End();}
    }
}
