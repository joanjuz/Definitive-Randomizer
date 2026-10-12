using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using Brush=System.Windows.Media.Brush;
using Color=System.Windows.Media.Color;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using TextBox=System.Windows.Controls.TextBox;
using DataGrid=System.Windows.Controls.DataGrid;
using DataGridRow=System.Windows.Controls.DataGridRow;
using DataGridCell=System.Windows.Controls.DataGridCell;
using DataGridColumnHeader=System.Windows.Controls.Primitives.DataGridColumnHeader;
using Control=System.Windows.Controls.Control;
using Binding=System.Windows.Data.Binding;
using Brushes=System.Windows.Media.Brushes;
using Orientation=System.Windows.Controls.Orientation;
using UserControl=System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>Dark, native WPF replacement for pk3DS's complete Important/Boss rule tables.</summary>
internal sealed class TrainerRulesWindow : UserControl
{
    private readonly ObservableCollection<TrainerRuleTableRow> _rows;
    private readonly ICollectionView _view;
    private readonly DataGrid _grid;
    private readonly CheckBox _important,_bosses;
    private readonly bool _caps;
    private readonly TextBlock _count;
    internal event Action<IReadOnlyList<TrainerRuleTableRow>>? Accepted;
    internal event Action? Cancelled;
    public IReadOnlyList<TrainerRuleTableRow> Rows=>_rows.ToArray();
    private Brush Panel=> (Brush)FindResource("Panel");
    private Brush Surface=> (Brush)FindResource("Surface");
    private Brush TextMain=> (Brush)FindResource("TextMain");
    private Brush TextSoft=> (Brush)FindResource("TextSoft");
    private Brush Stroke=> (Brush)FindResource("Stroke");

    internal TrainerRulesWindow(IEnumerable<TrainerRuleTableRow> rows,bool caps)
    {
        _caps=caps;
        Background=(Brush)FindResource("Bg");
        _rows=new ObservableCollection<TrainerRuleTableRow>(rows);
        _view=CollectionViewSource.GetDefaultView(_rows);
        var root=new DockPanel {Margin=new Thickness(18),Background=(Brush)FindResource("Bg")};
        Content=root;

        var top=new StackPanel {Margin=new Thickness(0,0,0,14)};
        DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock {Text=caps?"Level Caps":"Trainer Move Rules",
            FontSize=22,FontWeight=FontWeights.SemiBold,Foreground=TextMain});
        top.Children.Add(new TextBlock
        {
            Text="Datos originales de pk3DS Progressive. Filtrá importantes y jefes sin perder las filas ocultas.",
            FontSize=12,Foreground=TextSoft,Margin=new Thickness(0,5,0,12),TextWrapping=TextWrapping.Wrap
        });
        var filters=new WrapPanel {Orientation=Orientation.Horizontal};
        top.Children.Add(filters);
        _important=new CheckBox {Content="Entrenadores importantes",IsChecked=true,Margin=new Thickness(0,5,20,5)};
        _bosses=new CheckBox {Content="Jefes",IsChecked=true,Margin=new Thickness(0,5,20,5)};
        filters.Children.Add(_important);filters.Children.Add(_bosses);
        _important.Checked+=FilterChanged;_important.Unchecked+=FilterChanged;
        _bosses.Checked+=FilterChanged;_bosses.Unchecked+=FilterChanged;
        _count=new TextBlock {Foreground=TextSoft,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,3,6,3)};
        filters.Children.Add(_count);

        var bottom=new StackPanel {Margin=new Thickness(0,12,0,0)};
        DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        bottom.Children.Add(new TextBlock
        {
            Text=caps
                ?"Cap 0 = nivel del Ace dinámico. «Máx. 3» conserva al Pokémon de mayor nivel. Mega y movimiento Z se aplican únicamente donde el juego lo permite."
                :"Potencia 0 = sin mínimo; EV -1 = conservar. Los filtros solo afectan a qué filas se muestran, nunca borran reglas de otros entrenadores.",
            Foreground=TextSoft,FontSize=11,TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,0,0,12)
        });
        var actions=new WrapPanel {HorizontalAlignment=System.Windows.HorizontalAlignment.Right};
        bottom.Children.Add(actions);
        var selectAll=ActionButton("Seleccionar visibles",()=>SetVisible(true));
        var selectNone=ActionButton("Deseleccionar visibles",()=>SetVisible(false));
        var cancel=ActionButton("Volver al randomizador",()=>Cancelled?.Invoke());
        var apply=ActionButton("Aceptar",Accept);
        apply.Background=(Brush)FindResource("Selected");
        foreach(var button in new[]{selectAll,selectNone,cancel,apply})
        {button.Margin=new Thickness(0,0,8,0);actions.Children.Add(button);}

        _grid=new DataGrid
        {
            ItemsSource=_view,AutoGenerateColumns=false,CanUserAddRows=false,
            CanUserDeleteRows=false,CanUserReorderColumns=true,IsReadOnly=false,
            HeadersVisibility=DataGridHeadersVisibility.Column,SelectionMode=DataGridSelectionMode.Extended,
            GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,
            RowBackground=Panel,AlternatingRowBackground=Surface,
            HorizontalGridLinesBrush=Stroke,VerticalGridLinesBrush=Stroke,
            Background=Panel,Foreground=TextMain,BorderBrush=Stroke,BorderThickness=new Thickness(1),
            ColumnWidth=DataGridLength.SizeToHeader,
            RowHeight=33,CanUserResizeColumns=true,CanUserSortColumns=true,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto
        };
        var headerStyle=new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty,Surface));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty,TextMain));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty,Stroke));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,9,10,9)));
        _grid.ColumnHeaderStyle=headerStyle;
        var cellStyle=new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty,TextMain));
        cellStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(Control.BorderBrushProperty,Stroke));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5,1,5,1)));
        _grid.CellStyle=cellStyle;
        var rowStyle=new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,TextMain));
        rowStyle.Setters.Add(new Setter(Control.BorderBrushProperty,Stroke));
        var selected=new Trigger {Property=DataGridRow.IsSelectedProperty,Value=true};
        selected.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)FindResource("Selected")));
        rowStyle.Triggers.Add(selected);
        _grid.RowStyle=rowStyle;
        AddCheck(_caps?"Aplicar cap":"Usar regla",_caps?"CapUse":"MoveUse");
        if(_caps)
        {
            AddCheck("Máx. 3","Max3");AddCheck("Mega","Mega");AddCheck("Z-Move","ZMove");
        }
        else
        {
            AddNumber("Potencia mínima","MinPower");
            AddCheck("Mejor estadística","StrongStat");
            AddNumber("Tolerancia","Tolerance");
            AddCheck("Mov. estado","AllowStatus");
            AddCheck("Better Movesets","Better");
            AddCheck("Smart Items","SmartItems");
            AddNumber("EVs","EVs");
        }
        AddNumber("ID","Id",true);
        AddText("Grupo","Group");AddText("Entrenador","Trainer",true);
        AddNumber("Ace actual","Ace",true);
        if(_caps)AddNumber("Cap (0 = Ace)","Cap");
        root.Children.Add(_grid);
        _view.Filter=MatchesFilters;
        RefreshFilter();
    }
    private Button ActionButton(string label,Action action)
    {
        var button=new Button {Content=label,MinWidth=110,Padding=new Thickness(14,8,14,8)};
        button.Click+=(_,_)=>action();return button;
    }
    private void AddCheck(string header,string property)
    {
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header=header,Binding=new Binding(property){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},
            Width=new DataGridLength(105)
        });
    }
    private void AddNumber(string header,string property,bool readOnly=false)
    {
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header=header,Binding=new Binding(property){Mode=readOnly?BindingMode.OneWay:BindingMode.TwoWay,
                UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true},
            IsReadOnly=readOnly,Width=new DataGridLength(readOnly?83:116)
        });
    }
    private void AddText(string header,string property,bool wide=false)
    {
        _grid.Columns.Add(new DataGridTextColumn
        {Header=header,Binding=new Binding(property){Mode=BindingMode.OneWay},
            IsReadOnly=true,Width=new DataGridLength(wide?280:105)});
    }
    private bool MatchesFilters(object item)=>item is TrainerRuleTableRow row &&
        (string.Equals(row.Group,"Boss",StringComparison.OrdinalIgnoreCase)
            ? _bosses.IsChecked==true:_important.IsChecked==true);
    private void FilterChanged(object sender,RoutedEventArgs e)=>RefreshFilter();
    private void RefreshFilter()
    {
        if(_view is null||_count is null)return;
        _view.Refresh();
        _count.Text=$"{_view.Cast<object>().Count()} visibles de {_rows.Count}";
    }
    private void SetVisible(bool selected)
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell,true);
        _grid.CommitEdit(DataGridEditingUnit.Row,true);
        foreach(var row in _rows.Where(r=>MatchesFilters(r)))
        {
            if(_caps)row.CapUse=selected;
            else row.MoveUse=selected;
        }
        _view.Refresh();
    }
    private void Accept()
    {
        if(!_grid.CommitEdit(DataGridEditingUnit.Cell,true)||
            !_grid.CommitEdit(DataGridEditingUnit.Row,true))return;
        var invalid=_rows.FirstOrDefault(r=>_caps
            ? r.Cap is <0 or >100
            : r.MinPower is <0 or >250 || r.Tolerance is <0 or >255 || r.EVs is <-1 or >252);
        if(invalid is not null)
        {
            StudioDialog.Error(Window.GetWindow(this),"Regla inválida",$"Entrenador {invalid.Id}: comprobá el límite o los valores de movimientos (potencia 0–250, tolerancia 0–255, EV -1–252).");
            return;
        }
        Accepted?.Invoke(Rows);
    }
}
