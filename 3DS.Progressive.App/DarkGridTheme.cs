using System.Windows;
using System.Windows.Documents;
using System.Windows.Data;
using DataGridRow=System.Windows.Controls.DataGridRow;
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
    // Setter values must resolve again when AccessiblePalette replaces a resource.
    private static Setter ResourceSetter(DependencyProperty property,string key)=>new(property,new DynamicResourceExtension(key));
    private static DataTemplate WrappedHeader()
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding());text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);return new DataTemplate{VisualTree=text};
    }
    internal static void Apply(DataGrid grid)
    {
        void Resource(DependencyProperty property,string key)
        {
            if(!BindingOperations.IsDataBound(grid,property))grid.SetResourceReference(property,key);
        }
        Resource(Control.BackgroundProperty,"Panel");Resource(DataGrid.RowBackgroundProperty,"Panel");
        Resource(DataGrid.AlternatingRowBackgroundProperty,"Surface");Resource(Control.ForegroundProperty,"TextMain");
        Resource(Control.BorderBrushProperty,"Stroke");Resource(DataGrid.HorizontalGridLinesBrushProperty,"Stroke");
        Resource(DataGrid.VerticalGridLinesBrushProperty,"Stroke");
        grid.HeadersVisibility=DataGridHeadersVisibility.Column;grid.RowHeaderWidth=0;
        grid.GridLinesVisibility=DataGridGridLinesVisibility.Horizontal;grid.RowHeight=44;grid.MinRowHeight=44;grid.ColumnHeaderHeight=double.NaN;
        grid.SetValue(Typography.NumeralAlignmentProperty,FontNumeralAlignment.Tabular);
        grid.Resources[typeof(System.Windows.Controls.TextBox)]=System.Windows.Application.Current.FindResource("GridTextBox");
        grid.Resources[typeof(System.Windows.Controls.ComboBox)]=System.Windows.Application.Current.FindResource("GridComboBox");
        grid.Resources[typeof(System.Windows.Controls.Button)]=System.Windows.Application.Current.FindResource("GridButton");
        var header=new Style(typeof(DataGridColumnHeader));
        header.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,40d));
        header.Setters.Add(ResourceSetter(Control.BackgroundProperty,"Surface"));
        header.Setters.Add(ResourceSetter(Control.ForegroundProperty,"TextMain"));
        header.Setters.Add(ResourceSetter(Control.BorderBrushProperty,"Stroke"));
        header.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0,0,1,1)));
        header.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center));
        header.Setters.Add(new Setter(ContentControl.ContentTemplateProperty,WrappedHeader()));
        header.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,6,9,6)));
        var disabledHeader=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};
        disabledHeader.Setters.Add(ResourceSetter(Control.ForegroundProperty,"DisabledText"));header.Triggers.Add(disabledHeader);
        grid.ColumnHeaderStyle=header;
        // Do not set normal Background here: DataGrid owns the alternating row brushes.
        var rows=new Style(typeof(DataGridRow),grid.RowStyle);
        var selectedRow=new Trigger{Property=DataGridRow.IsSelectedProperty,Value=true};
        selectedRow.Setters.Add(ResourceSetter(Control.ForegroundProperty,"SelectionText"));rows.Triggers.Add(selectedRow);
        var disabledRow=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};
        disabledRow.Setters.Add(ResourceSetter(Control.ForegroundProperty,"DisabledText"));rows.Triggers.Add(disabledRow);
        grid.RowStyle=rows;
        var cells=new Style(typeof(DataGridCell));
        cells.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
        cells.Setters.Add(ResourceSetter(Control.ForegroundProperty,"TextMain"));
        cells.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));
        cells.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(3,2,3,2)));
        var selected=new Trigger{Property=DataGridCell.IsSelectedProperty,Value=true};
        selected.Setters.Add(ResourceSetter(Control.BackgroundProperty,"Selected"));
        selected.Setters.Add(ResourceSetter(Control.ForegroundProperty,"SelectionText"));cells.Triggers.Add(selected);
        cells.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.Transparent));
        var focus=new Trigger{Property=UIElement.IsKeyboardFocusWithinProperty,Value=true};focus.Setters.Add(ResourceSetter(Control.BorderBrushProperty,"Accent"));cells.Triggers.Add(focus);
        var disabledCell=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};
        disabledCell.Setters.Add(ResourceSetter(Control.ForegroundProperty,"DisabledText"));
        disabledCell.Setters.Add(ResourceSetter(Control.BackgroundProperty,"DisabledSurface"));
        disabledCell.Setters.Add(ResourceSetter(Control.BorderBrushProperty,"DisabledStroke"));cells.Triggers.Add(disabledCell);
        grid.CellStyle=cells;
    }
}
