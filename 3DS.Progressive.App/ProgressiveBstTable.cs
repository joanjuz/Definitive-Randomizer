using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using Brush=System.Windows.Media.Brush;
using Brushes=System.Windows.Media.Brushes;
using MessageBox=System.Windows.MessageBox;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using Control=System.Windows.Controls.Control;
using DataGrid=System.Windows.Controls.DataGrid;
using DataGridRow=System.Windows.Controls.DataGridRow;
using DataGridCell=System.Windows.Controls.DataGridCell;
using DataGridColumnHeader=System.Windows.Controls.Primitives.DataGridColumnHeader;
using UserControl=System.Windows.Controls.UserControl;
using Binding=System.Windows.Data.Binding;

namespace ThreeDS.Progressive;

/// <summary>One editable BST range table shared by Trainer and Wild randomizers.</summary>
internal sealed class ProgressiveBstTable : UserControl
{
    internal sealed class RangeRow : INotifyPropertyChanged
    {
        private int _minLevel,_maxLevel,_minBst,_maxBst;
        private bool _fullRandom;
        public int MinLevel{get=>_minLevel;set{_minLevel=value;Changed();}}
        public int MaxLevel{get=>_maxLevel;set{_maxLevel=value;Changed();}}
        public int MinBST{get=>_minBst;set{_minBst=value;Changed();}}
        public int MaxBST{get=>_maxBst;set{_maxBst=value;Changed();}}
        public bool FullRandom{get=>_fullRandom;set{_fullRandom=value;Changed();}}
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    }
    private readonly ObservableCollection<RangeRow> _rows=[];
    private readonly DataGrid _grid;
    private readonly int _bstLimit;
    internal event Action? Changed;
    internal int Count=>_rows.Count;
    internal ProgressiveBstTable(IEnumerable<EditorField> fields,int maxBst=1530)
    {
        _bstLimit=maxBst;
        Background=(Brush)FindResource("Panel");
        var main=new StackPanel {Margin=new Thickness(0,2,0,10)};
        Content=main;
        var heading=new DockPanel {Margin=new Thickness(0,0,0,10)};
        main.Children.Add(heading);
        var buttons=new WrapPanel {HorizontalAlignment=System.Windows.HorizontalAlignment.Right};
        DockPanel.SetDock(buttons,Dock.Right);heading.Children.Add(buttons);
        var add=new Button {Content="+ Añadir rango",Margin=new Thickness(0,0,8,0)};
        add.Click+=(_,_)=>AddRange();
        var remove=new Button {Content="− Quitar seleccionado"};
        remove.Click+=(_,_)=>RemoveRange();
        buttons.Children.Add(add);buttons.Children.Add(remove);
        heading.Children.Add(new TextBlock
        {
            Text="Rangos BST progresivo · tabla editable",FontSize=15,FontWeight=FontWeights.SemiBold,
            Foreground=(Brush)FindResource("TextMain"),VerticalAlignment=VerticalAlignment.Center
        });
        _grid=new DataGrid
        {
            AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,CanUserSortColumns=false,
            ItemsSource=_rows,RowHeight=37,MinHeight=100,
            MaxHeight=480,SelectionUnit=DataGridSelectionUnit.FullRow,
            Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),
            RowBackground=(Brush)FindResource("Panel"),AlternatingRowBackground=(Brush)FindResource("Surface"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            HorizontalGridLinesBrush=(Brush)FindResource("Stroke"),VerticalGridLinesBrush=(Brush)FindResource("Stroke"),
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto
        };
        var headers=new Style(typeof(DataGridColumnHeader));
        headers.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)FindResource("Surface")));
        headers.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        headers.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));
        _grid.ColumnHeaderStyle=headers;
        var cell=new Style(typeof(DataGridCell));
        cell.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
        cell.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        cell.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(7,2,7,2)));
        _grid.CellStyle=cell;
        var rowStyle=new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        _grid.RowStyle=rowStyle;
        AddColumn("Nivel desde","MinLevel");
        AddColumn("Nivel hasta","MaxLevel");
        AddColumn("BST mínimo","MinBST");
        AddColumn("BST máximo","MaxBST");
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header="Libre",Binding=new Binding("FullRandom")
                {Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=new DataGridLength(85)
        });
        main.Children.Add(_grid);
        main.Children.Add(new TextBlock
        {
            Text="Añadir divide el intervalo seleccionado; quitar lo fusiona con su vecino. Los niveles deben cubrir 1–100 sin huecos ni solapamientos.",
            FontSize=11,Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,10,0,0)
        });
        var groups=fields.Where(f=>f.Key.StartsWith("BST.",StringComparison.Ordinal))
            .GroupBy(f=>int.TryParse(f.Key.Split('.')[1],out int index)?index:-1)
            .Where(g=>g.Key>=0).OrderBy(g=>g.Key);
        foreach(var group in groups)
        {
            int V(string property,int fallback=0)=>group.FirstOrDefault(f=>f.Key.EndsWith("."+property,StringComparison.Ordinal))?.Value??fallback;
            _rows.Add(new RangeRow {MinLevel=V("MinLevel"),MaxLevel=V("MaxLevel"),
                MinBST=V("MinBST"),MaxBST=V("MaxBST"),FullRandom=V("FullRandom")==1});
        }
        foreach(var row in _rows)row.PropertyChanged+=RowChanged;
    }
    private void AddColumn(string header,string property)=>_grid.Columns.Add(new DataGridTextColumn
    {
        Header=header,Binding=new Binding(property)
        {Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true},
        Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=100
    });
    private void RowChanged(object? sender,PropertyChangedEventArgs e)=>Changed?.Invoke();
    private void Commit()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell,true);
        _grid.CommitEdit(DataGridEditingUnit.Row,true);
    }
    private void AddRange()
    {
        Commit();
        if(_rows.Count>=100)return;
        var row=_grid.SelectedItem as RangeRow??_rows.LastOrDefault();
        if(row is null){row=new RangeRow {MinLevel=1,MaxLevel=100,MinBST=180,MaxBST=680};_rows.Add(row);row.PropertyChanged+=RowChanged;}
        else
        {
            if(row.MinLevel>=row.MaxLevel)
            {
                MessageBox.Show("Elegí un intervalo con dos niveles o más para dividirlo.","Rangos BST",
                    MessageBoxButton.OK,MessageBoxImage.Information);return;
            }
            int split=(row.MinLevel+row.MaxLevel)/2;
            var right=new RangeRow {MinLevel=split+1,MaxLevel=row.MaxLevel,
                MinBST=row.MinBST,MaxBST=row.MaxBST,FullRandom=row.FullRandom};
            row.MaxLevel=split;
            _rows.Insert(_rows.IndexOf(row)+1,right);
            right.PropertyChanged+=RowChanged;
            _grid.SelectedItem=right;
        }
        Changed?.Invoke();
    }
    private void RemoveRange()
    {
        Commit();
        if(_rows.Count<=1)return;
        var row=_grid.SelectedItem as RangeRow??_rows[^1];
        int index=_rows.IndexOf(row);
        if(index>0)_rows[index-1].MaxLevel=row.MaxLevel;
        else _rows[1].MinLevel=row.MinLevel;
        row.PropertyChanged-=RowChanged;
        _rows.Remove(row);Changed?.Invoke();
    }
    internal IReadOnlyDictionary<string,int> Capture()
    {
        Commit();
        if(_rows.Count==0)throw new ArgumentException("Debe existir al menos un rango BST.");
        var sorted=_rows.OrderBy(r=>r.MinLevel).ToArray();
        for(int i=0;i<sorted.Length;i++)
        {
            var r=sorted[i];
            if(r.MinLevel<1||r.MaxLevel>100||r.MinLevel>r.MaxLevel||
                (!r.FullRandom&&(r.MinBST<1||r.MaxBST>_bstLimit||r.MinBST>r.MaxBST)))
                throw new ArgumentException($"El rango BST {i+1} tiene valores inválidos.");
            if(i==0&&r.MinLevel!=1 || i>0&&r.MinLevel!=sorted[i-1].MaxLevel+1)
                throw new ArgumentException("La tabla BST debe cubrir niveles consecutivos empezando en 1.");
        }
        if(sorted[^1].MaxLevel!=100)throw new ArgumentException("La tabla BST debe terminar en el nivel 100.");
        var result=new Dictionary<string,int>();
        for(int i=0;i<sorted.Length;i++)
        {
            var r=sorted[i];string k=$"BST.{i}.";
            result[k+"MinLevel"]=r.MinLevel;result[k+"MaxLevel"]=r.MaxLevel;
            result[k+"MinBST"]=r.MinBST;result[k+"MaxBST"]=r.MaxBST;
            result[k+"FullRandom"]=r.FullRandom?1:0;
        }
        return result;
    }
}
