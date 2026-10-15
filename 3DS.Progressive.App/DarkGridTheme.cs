using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DataGrid=System.Windows.Controls.DataGrid;
using DataGridCell=System.Windows.Controls.DataGridCell;
using Brushes=System.Windows.Media.Brushes;
using Control=System.Windows.Controls.Control;
using Brush=System.Windows.Media.Brush;

namespace ThreeDS.Progressive;

internal static class DarkGridTheme
{
    internal static void Apply(DataGrid grid)
    {
        Brush Color(string key)=>(Brush)System.Windows.Application.Current.FindResource(key);
        grid.Background=grid.RowBackground=Color("Panel");grid.AlternatingRowBackground=Color("Surface");
        grid.Foreground=Color("TextMain");grid.BorderBrush=grid.HorizontalGridLinesBrush=grid.VerticalGridLinesBrush=Color("Stroke");
        grid.HeadersVisibility=DataGridHeadersVisibility.Column;grid.RowHeaderWidth=0;
        grid.GridLinesVisibility=DataGridGridLinesVisibility.Horizontal;grid.RowHeight=34;grid.ColumnHeaderHeight=38;
        var header=new Style(typeof(DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty,Color("Surface")));
        header.Setters.Add(new Setter(Control.ForegroundProperty,Color("TextMain")));
        header.Setters.Add(new Setter(Control.BorderBrushProperty,Color("Stroke")));
        header.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0,0,1,1)));
        header.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,6,9,6)));
        grid.ColumnHeaderStyle=header;
        var cells=new Style(typeof(DataGridCell));
        cells.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
        cells.Setters.Add(new Setter(Control.ForegroundProperty,Color("TextMain")));
        cells.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));
        cells.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(6,2,6,2)));
        var selected=new Trigger{Property=DataGridCell.IsSelectedProperty,Value=true};
        selected.Setters.Add(new Setter(Control.BackgroundProperty,Color("Selected")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty,Color("TextMain")));cells.Triggers.Add(selected);
        grid.CellStyle=cells;
    }
}
