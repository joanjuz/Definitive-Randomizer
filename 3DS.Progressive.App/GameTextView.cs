using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using UserControl=System.Windows.Controls.UserControl;
using DataGrid=System.Windows.Controls.DataGrid;
using DataGridRow=System.Windows.Controls.DataGridRow;
using DataGridColumnHeader=System.Windows.Controls.Primitives.DataGridColumnHeader;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ListBox=System.Windows.Controls.ListBox;
using Control=System.Windows.Controls.Control;
using Binding=System.Windows.Data.Binding;
using Brush=System.Windows.Media.Brush;

namespace ThreeDS.Progressive;

internal sealed class GameTextRow : INotifyPropertyChanged
{
    public int Index {get;init;}
    private string _value="";
    public string Value{get=>_value;set{if(_value==value)return;_value=value;PropertyChanged?.Invoke(this,new(nameof(Value)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>In-app searchable text files; no extra windows and no WinForms defaults.</summary>
internal sealed class GameTextView : UserControl
{
    private readonly GameTextService _service;
    private ObservableCollection<GameTextRow> _rows=[];
    private readonly DataGrid _grid;
    private readonly ListBox _files;
    private readonly TextBox _filter;
    private readonly TextBlock _status;
    private readonly Button _save;
    private bool _dirty,_loading;
    private int _id=-1;
    internal bool HasPendingChanges=>_dirty;
    internal bool ConfirmLeave()=>!_dirty||StudioDialog.Confirm(Window.GetWindow(this),
        "Cambios de texto pendientes","Hay líneas editadas sin guardar.","Si continuás, se descartarán.");
    internal GameTextView(GameProjectSession project,bool story=false)
    {
        _service=new(project,story);Background=(Brush)FindResource("Bg");
        var root=new DockPanel {Margin=new Thickness(20)};
        Content=root;
        var heading=new StackPanel {Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        heading.Children.Add(new TextBlock {Text=story?"Story Text · diálogos e historia":"Game Text · textos generales",FontSize=22,
            FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")});
        heading.Children.Add(new TextBlock {Text=story?
            "Bancos originales de diálogos y eventos. Conservá los códigos [VAR ...], cambios de línea y el orden de cada mensaje para evitar scripts dañados.":
            "Archivos de texto de la ROM. Se conserva el número y orden de líneas para no romper referencias de scripts.",
            Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap});
        var footer=new DockPanel {Margin=new Thickness(0,12,0,0)};
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        _status=new TextBlock {Text="Elegí un archivo de texto.",Foreground=(Brush)FindResource("TextSoft"),
            VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
        footer.Children.Add(_status);
        _save=new Button {Content="Guardar texto",IsEnabled=false,Margin=new Thickness(10,0,0,0)};
        DockPanel.SetDock(_save,Dock.Right);footer.Children.Add(_save);
        _save.Click+=(_,_)=>Save();
        var export=new Button {Content="Exportar TXT"};
        DockPanel.SetDock(export,Dock.Right);footer.Children.Add(export);
        export.Click+=(_,_)=>Export();
        if(story)
        {
            var exportAll=new Button
            {
                Content="Exportar banco completo",Margin=new Thickness(0,0,8,0)
            };
            DockPanel.SetDock(exportAll,Dock.Right);footer.Children.Add(exportAll);
            exportAll.Click+=(_,_)=>TryExportAll();
            var importAll=new Button
            {
                Content="Importar banco completo",Margin=new Thickness(0,0,8,0)
            };
            DockPanel.SetDock(importAll,Dock.Right);footer.Children.Add(importAll);
            importAll.Click+=(_,_)=>TryImportAll();
        }
        var content=new Grid();content.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(230)});
        content.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        root.Children.Add(content);
        var left=new DockPanel {Margin=new Thickness(0,0,12,0)};
        Grid.SetColumn(left,0);content.Children.Add(left);
        _filter=new TextBox {Margin=new Thickness(0,0,0,8)};
        _filter.TextChanged+=(_,_)=>Filter();
        DockPanel.SetDock(_filter,Dock.Top);left.Children.Add(_filter);
        _files=new ListBox {ItemsSource=_service.Entries,DisplayMemberPath="Display"};
        _files.SelectionChanged+=(_,_)=>SelectFile();
        left.Children.Add(_files);
        _grid=new DataGrid
        {
            AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,
            CanUserSortColumns=false,RowHeight=44,
            Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),
            RowBackground=(Brush)FindResource("Panel"),AlternatingRowBackground=(Brush)FindResource("Surface"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            HorizontalGridLinesBrush=(Brush)FindResource("Stroke")
        };
        var headers=new Style(typeof(DataGridColumnHeader));
        headers.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)FindResource("Surface")));
        headers.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        headers.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,8,9,8)));
        _grid.ColumnHeaderStyle=headers;
        var rowStyle=new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        _grid.RowStyle=rowStyle;
        _grid.Columns.Add(new DataGridTextColumn {Header="#",Binding=new Binding("Index"),IsReadOnly=true,Width=64});
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header="Texto del juego",Width=new DataGridLength(1,DataGridLengthUnitType.Star),
            Binding=new Binding("Value"){Mode=BindingMode.TwoWay,
                UpdateSourceTrigger=UpdateSourceTrigger.LostFocus}
        });
        _grid.CellEditEnding+=(_,_)=>MarkDirty();
        Grid.SetColumn(_grid,1);content.Children.Add(_grid);
        _loading=true;_files.SelectedIndex=0;_loading=false;
        if(_files.SelectedItem is RecordEntry entry)Load(entry.Id);
    }
    private void Filter()
    {
        if(_files.ItemsSource is null)return;
        string needle=_filter.Text.Trim();
        var view=CollectionViewSource.GetDefaultView(_files.ItemsSource);
        view.Filter=x=>x is RecordEntry r&&(r.Id.ToString().Contains(needle)||
            r.Name.Contains(needle,StringComparison.OrdinalIgnoreCase));
        view.Refresh();
    }
    private void SelectFile()
    {
        if(_loading||_files.SelectedItem is not RecordEntry r||r.Id==_id)return;
        if(!ConfirmLeave())
        {
            _loading=true;_files.SelectedItem=_service.Entries.FirstOrDefault(x=>x.Id==_id);
            _loading=false;return;
        }
        Load(r.Id);
    }
    private void Load(int id)
    {
        try
        {
            var strings=_service.Read(id);
            _rows=new ObservableCollection<GameTextRow>(strings.Select((value,index)=>
                new GameTextRow{Index=index,Value=value}));
            _grid.ItemsSource=_rows;
            _id=id;_dirty=false;_save.IsEnabled=false;
            _status.Text=$"Archivo {id:000} · {_rows.Count} líneas.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Texto no compatible",ex.Message);}
    }
    private void MarkDirty(){_dirty=true;_save.IsEnabled=true;_status.Text="Texto modificado. Guardá antes de salir.";}
    private void Save()
    {
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell,true);
            _grid.CommitEdit(DataGridEditingUnit.Row,true);
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Confirmar texto",
                $"Guardar {_rows.Count} líneas del archivo {_id:000} en {_service.BankName} del idioma activo."))return;
            _service.Save(_id,_rows.Select(x=>x.Value).ToArray());
            Load(_id);_status.Text="Texto guardado, verificado y registrado en el historial.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo guardar el texto",ex.Message);}
    }
    private void TryExportAll()
    {
        try
        {
            var picker=new Microsoft.Win32.SaveFileDialog
            {
                Filter="Texto Unicode original|*.txt",
                FileName="storytext-banco-completo.txt"
            };
            if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
            File.WriteAllText(picker.FileName,_service.ExportAll(),
                System.Text.Encoding.Unicode);
            _status.Text="Banco completo exportado en UTF-16, con códigos de control intactos.";
        }
        catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(this),"Exportar Story Text",ex.Message);}
    }
    private void TryImportAll()
    {
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell,true);
            _grid.CommitEdit(DataGridEditingUnit.Row,true);
            if(_dirty)
                throw new InvalidOperationException(
                    "Guardá o descartá primero las líneas editadas de este archivo.");
            var picker=new Microsoft.Win32.OpenFileDialog
            {
                Filter="Texto Unicode original|*.txt"
            };
            if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
            string dump=File.ReadAllText(picker.FileName,System.Text.Encoding.Unicode);
            TextBankPreview preview=_service.PreviewImportAll(dump);
            if(preview.Changes.Count==0)
            {
                _status.Text="El banco importado es idéntico; no hay cambios.";
                return;
            }
            string examples=string.Join(Environment.NewLine,preview.Changes.Take(12)
                .Select(d=>$"{d.Name}, {d.Field}: {d.Before} → {d.After}"));
            StudioDialog.Notice(Window.GetWindow(this),"Vista previa Story Text",
                $"{preview.Changes.Count} líneas diferentes. Ninguna referencia cambia de posición.",
                examples+(preview.Changes.Count>12?Environment.NewLine+"…":""));
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Aplicar banco Story Text",
                $"¿Aplicar {preview.Changes.Count} cambios al GARC Story Text del idioma activo? " +
                "El guardado se registrará en el historial y no modificará Game Text."))
                return;
            _service.ApplyImportAll(preview);
            Load(_id);
            _status.Text=$"Importación verificada: {preview.Changes.Count} líneas modificadas.";
        }
        catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(this),"Importar Story Text",ex.Message);}
    }
    private void Export()
    {
        try
        {
            var picker=new Microsoft.Win32.SaveFileDialog {Filter="Texto|*.txt",FileName=$"{(_service.BankName=="Story Text"?"story":"game")}-text-{_id:000}.txt"};
            if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
            File.WriteAllText(picker.FileName,_service.Export(_id),System.Text.Encoding.UTF8);
            _status.Text="Archivo de texto exportado.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Exportación fallida",ex.Message);}
    }
}
