using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using DataGrid=System.Windows.Controls.DataGrid;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;

namespace ThreeDS.Progressive;

/// <summary>
/// Editing look for every table of the app. DataGridTextColumn and DataGridComboBoxColumn create their editors with an
/// explicit default style, so the app's dark input styles never applied and the edited value showed in the system colors.
/// Each DataGrid, when loaded, swaps those defaults for GridTextBox and GridComboBox; a style set by a view is kept.
/// </summary>
internal static class GridEditing
{
    private static readonly ConditionalWeakTable<DataGrid,object> Attached=[];

    internal static void Install()=>EventManager.RegisterClassHandler(typeof(DataGrid),FrameworkElement.LoadedEvent,new RoutedEventHandler(Attach));

    private static void Attach(object? sender,RoutedEventArgs e)
    {
        if(sender is not DataGrid grid||Attached.TryGetValue(grid,out _))return;
        Attached.Add(grid,new object());
        grid.PreparingCellForEdit+=Prepare;
    }

    internal static void Prepare(object? sender,DataGridPreparingCellForEditEventArgs e)
    {
        var app=System.Windows.Application.Current;
        if(app is null)return;
        switch(e.EditingElement)
        {
            case TextBox text when text.Style is null||ReferenceEquals(text.Style,DataGridTextColumn.DefaultEditingElementStyle):
                if(app.TryFindResource("GridTextBox") is Style textStyle)text.Style=textStyle;
                text.SelectAll();
                break;
            case ComboBox combo when combo.Style is null||ReferenceEquals(combo.Style,DataGridComboBoxColumn.DefaultEditingElementStyle):
                if(app.TryFindResource("GridComboBox") is Style comboStyle)combo.Style=comboStyle;
                break;
        }
    }
}
