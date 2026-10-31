using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;

namespace ThreeDS.Progressive;

/// <summary>
/// Editing look for every table of the app. DataGridTextColumn and DataGridComboBoxColumn create their editors with an
/// explicit default style, so the app's dark input styles never applied and the edited value showed in the system colors
/// (black text, light combo box). When such an editor is loaded it gets GridCellEditor or GridComboBox; a style set by a view is kept.
/// </summary>
internal static class GridEditing
{
    private static bool _installed;

    internal static void Install()
    {
        if(_installed)return;
        _installed=true;
        EventManager.RegisterClassHandler(typeof(TextBox),FrameworkElement.LoadedEvent,new RoutedEventHandler(Restyle));
        EventManager.RegisterClassHandler(typeof(ComboBox),FrameworkElement.LoadedEvent,new RoutedEventHandler(Restyle),true);
        // The editing ComboBox of a DataGridComboBoxColumn may already be loaded when it enters the cell; the DataGrid always focuses it.
        EventManager.RegisterClassHandler(typeof(ComboBox),UIElement.GotKeyboardFocusEvent,new KeyboardFocusChangedEventHandler((s,e)=>Restyle(s,e)),true);
    }

    internal static void Restyle(object? sender,RoutedEventArgs e)
    {
        var app=System.Windows.Application.Current;
        if(app is null)return;
        if(sender is TextBox text&&ReferenceEquals(text.Style,DataGridTextColumn.DefaultEditingElementStyle)&&app.TryFindResource("GridCellEditor") is Style textStyle)
            text.Style=textStyle;
        else if(sender is ComboBox combo&&ReferenceEquals(combo.Style,DataGridComboBoxColumn.DefaultEditingElementStyle)&&app.TryFindResource("GridComboBox") is Style comboStyle)
            combo.Style=comboStyle;
    }
}
