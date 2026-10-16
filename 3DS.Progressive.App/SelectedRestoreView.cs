using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using DataGrid=System.Windows.Controls.DataGrid;
using UserControl=System.Windows.Controls.UserControl;
using Brush=System.Windows.Media.Brush;
using Binding=System.Windows.Data.Binding;

namespace ThreeDS.Progressive;

internal sealed class RestoreFileRow : INotifyPropertyChanged
{
    internal BackupFile File {get;}
    public string Path=>File.Path;
    public long Bytes=>File.Size;
    private bool _selected;
    public bool Selected{get=>_selected;set{if(value==_selected)return;_selected=value;PropertyChanged?.Invoke(this,new(nameof(Selected)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
    internal RestoreFileRow(BackupFile file)=>File=file;
}
internal sealed class SelectedRestoreView : UserControl
{
    private readonly GameProjectSession _project;
    private readonly Func<Task> _reload;
    private readonly DataGrid _grid;
    private readonly Button _load,_previewButton,_apply;
    private readonly TextBlock _status;
    private readonly ObservableCollection<RestoreFileRow> _rows=[];
    private RestorePlan? _plan;
    private SelectedRestorePreview? _proposal;
    private bool _busy;
    internal bool ConfirmLeave()=>!_busy&&(_proposal is null||StudioDialog.Confirm(Window.GetWindow(this),"Restauración pendiente","Hay una vista previa sin aplicar. ¿Descartarla?"));
    internal SelectedRestoreView(GameProjectSession project,Func<Task> reload)
    {
        _project=project;_reload=reload;Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text="Restaurar archivos seleccionados",FontSize=23,Foreground=(Brush)FindResource("TextMain")});
        header.Children.Add(new TextBlock{Text="Elegí un backup verificado del mismo juego y los archivos existentes de RomFS/ExeFS que querés recuperar. La vista previa admite hasta 128 MiB por operación; la restauración completa permite recuperar todo en una carpeta nueva.",Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});
        var buttons=new WrapPanel();header.Children.Add(buttons);
        _load=new Button{Content="Abrir backup…",Margin=new Thickness(0,0,8,12)};_load.Click+=async(_,_)=>await LoadBackup();buttons.Children.Add(_load);
        _previewButton=new Button{Content="Generar vista previa",IsEnabled=false,Margin=new Thickness(0,0,8,12)};_previewButton.Click+=async(_,_)=>await Preview();buttons.Children.Add(_previewButton);
        _apply=new Button{Content="Aplicar restauración",IsEnabled=false,Margin=new Thickness(0,0,8,12)};_apply.Click+=async(_,_)=>await Apply();buttons.Children.Add(_apply);
        _status=new TextBlock{Text="No hay un backup cargado.",Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
        DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);
        _grid=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,ItemsSource=_rows};DarkGridTheme.Apply(_grid);
        _grid.Columns.Add(new DataGridCheckBoxColumn{Header="Restaurar",Binding=new Binding(nameof(RestoreFileRow.Selected)){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=85});
        _grid.Columns.Add(new DataGridTextColumn{Header="Archivo",Binding=new Binding(nameof(RestoreFileRow.Path)),IsReadOnly=true,MinWidth=350,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        _grid.Columns.Add(new DataGridTextColumn{Header="Bytes backup",Binding=new Binding(nameof(RestoreFileRow.Bytes)),IsReadOnly=true,Width=130});root.Children.Add(_grid);
    }
    private void Busy(bool value)
    {_busy=value;_grid.IsEnabled=!value;_load.IsEnabled=!value;_previewButton.IsEnabled=!value&&_plan is not null;_apply.IsEnabled=!value&&_proposal?.Files.Count>0;}
    private void Invalidate(){_proposal=null;_apply.IsEnabled=false;}
    private async Task LoadBackup()
    {
        var picker=new Microsoft.Win32.OpenFileDialog{Filter="Backup Definitive Randomizer|*.zip"};if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        Busy(true);Invalidate();UiProgress.Begin("Verificando backup e inventario…");
        try
        {
            var plan=await ProjectRestore.InspectAsync(picker.FileName);var files=await ProjectRestore.SelectableAsync(_project,plan);_plan=plan;_rows.Clear();
            foreach(var file in files){var row=new RestoreFileRow(file);row.PropertyChanged+=(_,_)=>Invalidate();_rows.Add(row);}
            _status.Text=$"{files.Count} archivos disponibles · {_plan.Game}. Seleccioná los que querés recuperar.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Backup no compatible",ex.Message);}
        finally{Busy(false);UiProgress.End();}
    }
    private async Task Preview()
    {
        if(_plan is null)return;Busy(true);Invalidate();UiProgress.Begin("Preparando restauración selectiva…");
        try
        {
            _proposal=await ProjectRestore.PreviewSelectedAsync(_project,_plan,_rows.Where(row=>row.Selected).Select(row=>row.Path).ToArray());
            _status.Text=$"Vista previa: {_proposal.Files.Count} archivos diferentes. Todavía no se guardó ningún cambio.";
            if(_proposal.Files.Count>0)StudioDialog.Notice(Window.GetWindow(this),"Vista previa de restauración",_status.Text,string.Join(Environment.NewLine,_proposal.Changes.Select(change=>change.Name)));
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Restauración selectiva",ex.Message);}
        finally{Busy(false);UiProgress.End();}
    }
    private async Task Apply()
    {
        if(_proposal is not {} proposal||!StudioDialog.Confirm(Window.GetWindow(this),"Aplicar restauración",$"¿Recuperar {proposal.Files.Count} archivos del backup y registrar los cambios en el historial?"))return;
        Busy(true);UiProgress.Begin("Restaurando y verificando archivos seleccionados…");
        try{await Task.Run(()=>ProjectRestore.ApplySelected(_project,proposal));Invalidate();await _reload();}
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo restaurar",ex.Message);}
        finally{Busy(false);UiProgress.End();}
    }
}
