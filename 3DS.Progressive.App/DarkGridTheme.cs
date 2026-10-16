using System.Windows;
using System.Windows.Documents;
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
    private static DataTemplate WrappedHeader()
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding());text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);return new DataTemplate{VisualTree=text};
    }
    internal static void Apply(DataGrid grid)
    {
        Brush Color(string key)=>(Brush)System.Windows.Application.Current.FindResource(key);
        grid.Background=grid.RowBackground=Color("Panel");grid.AlternatingRowBackground=Color("Surface");
        grid.Foreground=Color("TextMain");grid.BorderBrush=grid.HorizontalGridLinesBrush=grid.VerticalGridLinesBrush=Color("Stroke");
        grid.HeadersVisibility=DataGridHeadersVisibility.Column;grid.RowHeaderWidth=0;
        grid.GridLinesVisibility=DataGridGridLinesVisibility.Horizontal;grid.RowHeight=44;grid.MinRowHeight=44;grid.ColumnHeaderHeight=double.NaN;
        grid.SetValue(Typography.NumeralAlignmentProperty,FontNumeralAlignment.Tabular);
        grid.Resources[typeof(System.Windows.Controls.TextBox)]=System.Windows.Application.Current.FindResource("GridTextBox");
        grid.Resources[typeof(System.Windows.Controls.ComboBox)]=System.Windows.Application.Current.FindResource("GridComboBox");
        grid.Resources[typeof(System.Windows.Controls.Button)]=System.Windows.Application.Current.FindResource("GridButton");
        var header=new Style(typeof(DataGridColumnHeader));
        header.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,40d));
        header.Setters.Add(new Setter(Control.BackgroundProperty,Color("Surface")));
        header.Setters.Add(new Setter(Control.ForegroundProperty,Color("TextMain")));
        header.Setters.Add(new Setter(Control.BorderBrushProperty,Color("Stroke")));
        header.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0,0,1,1)));
        header.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center));
        header.Setters.Add(new Setter(ContentControl.ContentTemplateProperty,WrappedHeader()));
        header.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,6,9,6)));
        grid.ColumnHeaderStyle=header;
        var cells=new Style(typeof(DataGridCell));
        cells.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
        cells.Setters.Add(new Setter(Control.ForegroundProperty,Color("TextMain")));
        cells.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));
        cells.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(3,2,3,2)));
        var selected=new Trigger{Property=DataGridCell.IsSelectedProperty,Value=true};
        selected.Setters.Add(new Setter(Control.BackgroundProperty,Color("Selected")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty,Color("SelectionText")));cells.Triggers.Add(selected);
        cells.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.Transparent));
        var focus=new Trigger{Property=UIElement.IsKeyboardFocusWithinProperty,Value=true};focus.Setters.Add(new Setter(Control.BorderBrushProperty,Color("Accent")));cells.Triggers.Add(focus);
        grid.CellStyle=cells;
    }
}
