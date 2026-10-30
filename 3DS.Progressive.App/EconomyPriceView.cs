using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using DataGrid=System.Windows.Controls.DataGrid;
using DataGridRow=System.Windows.Controls.DataGridRow;
using DataGridColumnHeader=System.Windows.Controls.Primitives.DataGridColumnHeader;
using Control=System.Windows.Controls.Control;
using UserControl=System.Windows.Controls.UserControl;
using TextBox=System.Windows.Controls.TextBox;
using CheckBox=System.Windows.Controls.CheckBox;
using Binding=System.Windows.Data.Binding;

namespace ThreeDS.Progressive;

/// <summary>Full-width dark Economy Price table, embedded in Items editor.</summary>
internal sealed class EconomyPriceView : UserControl
{
    private readonly IEconomyEditor _items;
    private readonly ObservableCollection<ItemPriceRow> _rows;
    private readonly ICollectionView _view;
    private readonly DataGrid _grid;
    private readonly TextBox _search;
    private readonly Button _apply;
    private readonly TextBlock _status;
    private ThreeDS.Progressive.Models.EditorProposal? _preview;
    internal event Action? Closed;
    internal event Action? Saved;
    internal EconomyPriceView(IEconomyEditor items)
    {
        _items=items;
        _rows=new ObservableCollection<ItemPriceRow>(items.PriceRows());
        _view=CollectionViewSource.GetDefaultView(_rows);
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel {Margin=new Thickness(16),Background=(Brush)FindResource("Bg")};
        Content=root;
        var head=new StackPanel {Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
        head.Children.Add(new TextBlock {Text="Tabla de precios de economía",FontSize=21,
            Foreground=(Brush)FindResource("TextMain"),FontWeight=FontWeights.SemiBold});
        head.Children.Add(new TextBlock {Text=$"Preset {items.EconomyPreset}. {items.EconomyHelp}",
            Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap});
        var controls=new WrapPanel {Margin=new Thickness(0,12,0,0)};
        head.Children.Add(controls);
        _search=new TextBox {Width=270,Margin=new Thickness(0,0,10,6)};
        _search.TextChanged+=(_,_)=>Refresh();
        controls.Children.Add(_search);
        Button Command(string label,Action callback)
        {
            var b=new Button {Content=label,Margin=new Thickness(0,0,8,6)};
            b.Click+=(_,_)=>callback();controls.Children.Add(b);return b;
        }
        Command("Incluir visibles",()=>SetVisible(true));
        Command("Excluir visibles",()=>SetVisible(false));
        Command("Restablecer preset",ResetDefaults);
        var bottom=new DockPanel {Margin=new Thickness(0,12,0,0),LastChildFill=false};
        DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        _status=new TextBlock {Foreground=(Brush)FindResource("TextSoft"),
            Text="Revisá y ajustá las filas incluidas.",VerticalAlignment=VerticalAlignment.Center,
            TextWrapping=TextWrapping.Wrap};
        DockPanel.SetDock(_status,Dock.Left);bottom.Children.Add(_status);
        var close=new Button {Content="Volver",Margin=new Thickness(8,0,0,0)};
        close.Click+=(_,_)=>Closed?.Invoke();
        DockPanel.SetDock(close,Dock.Right);bottom.Children.Add(close);
        _apply=new Button {Content="Aplicar cambios",IsEnabled=false,Margin=new Thickness(8,0,0,0)};
        _apply.Click+=(_,_)=>Apply();
        DockPanel.SetDock(_apply,Dock.Right);bottom.Children.Add(_apply);
        var preview=new Button {Content="Generar vista previa",Margin=new Thickness(8,0,0,0)};
        preview.Click+=(_,_)=>Preview();
        DockPanel.SetDock(preview,Dock.Right);bottom.Children.Add(preview);
        _grid=new DataGrid
        {
            ItemsSource=_view,AutoGenerateColumns=false,CanUserAddRows=false,
            CanUserDeleteRows=false,CanUserSortColumns=true,RowHeight=32,
            Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),
            RowBackground=(Brush)FindResource("Panel"),AlternatingRowBackground=(Brush)FindResource("Surface"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            HorizontalGridLinesBrush=(Brush)FindResource("Stroke"),
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto
        };
        var headers=new Style(typeof(DataGridColumnHeader));
        headers.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)FindResource("Surface")));
        headers.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        headers.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));
        _grid.ColumnHeaderStyle=headers;
        var rowStyle=new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        _grid.RowStyle=rowStyle;
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header="Aplicar",Binding=new Binding("Included"){Mode=BindingMode.TwoWay,
                UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=70
        });
        Col("ID","Id",68,true);
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header="Objeto",Binding=new Binding("Name"),IsReadOnly=true,
            Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=205
        });
        Col("Compra actual","CurrentBuy",135,true);
        Col("Compra objetivo","TargetBuy",155);
        Col("Venta objetivo","TargetSell",135,true);
        Col("Origen","Source",210,true);
        _grid.CellEditEnding+=(_,_)=>Invalidate();
        foreach(var row in _rows)row.PropertyChanged+=(_,_)=>Invalidate();
        root.Children.Add(_grid);
        _view.Filter=Matches;
        Refresh();
    }
    private void Col(string title,string property,double width,bool readOnly=false)
    {
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header=title,Binding=new Binding(property){Mode=readOnly?BindingMode.OneWay:BindingMode.TwoWay,
                UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true},
            IsReadOnly=readOnly,Width=new DataGridLength(width)
        });
    }
    private bool Matches(object obj)=>obj is ItemPriceRow row&&
        (string.IsNullOrWhiteSpace(_search.Text)||row.Name.Contains(_search.Text,StringComparison.OrdinalIgnoreCase)||
         row.Id.ToString().Contains(_search.Text));
    private void Refresh()
    {
        _view.Refresh();
        if(_status is not null)_status.Text=$"{_view.Cast<object>().Count()} objetos visibles · {_rows.Count(r=>r.Included)} incluidos.";
    }
    private void Invalidate(){_preview=null;if(_apply is not null)_apply.IsEnabled=false;}
    private void SetVisible(bool yes)
    {
        foreach(var row in _rows.Where(Matches))row.Included=yes;
        Refresh();
    }
    private void ResetDefaults()
    {
        foreach(var row in _rows)
        {
            int? target=GameEconomyDefaults.TargetPrice(row.Id);
            if(target is null)continue;
            row.TargetBuy=target.Value;row.Included=true;
        }
        Refresh();
    }
    private void Preview()
    {
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell,true);
            _grid.CommitEdit(DataGridEditingUnit.Row,true);
            _preview=_items.PreviewEconomy(_rows.ToArray());
            _apply.IsEnabled=_preview.Changes.Count>0;
            _status.Text=$"{_preview.Changes.Count} precios distintos. Revisá antes de aplicar.";
        }
        catch(Exception ex){Invalidate();StudioDialog.Error(Window.GetWindow(this),"Vista previa de economía",ex.Message);}
    }
    private void Apply()
    {
        if(_preview is null)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar economía",
            $"Se modificarán {_preview.Changes.Count} precios de compra en los datos del juego."))return;
        try
        {
            _items.Apply(_preview);
            _preview=null;_apply.IsEnabled=false;
            Saved?.Invoke();_status.Text="Economía guardada con transacción e historial.";
        }
        catch(Exception ex){Invalidate();StudioDialog.Error(Window.GetWindow(this),"No se pudo guardar la economía",ex.Message);}
    }
}
