using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ComboBox=System.Windows.Controls.ComboBox;
using TextBox=System.Windows.Controls.TextBox;
using ListBox=System.Windows.Controls.ListBox;
using KeyEventArgs=System.Windows.Input.KeyEventArgs;
using KeyEventHandler=System.Windows.Input.KeyEventHandler;

namespace ThreeDS.Progressive;

/// <summary>
/// Type-to-search for every long drop-down of the app (items, species, moves, natures…). Clicking the box or typing on it opens a
/// list with a search field instead of the plain drop-down; what is typed filters the list (any part of the name or its number,
/// without accents) and choosing a result selects that same item in the ComboBox. The ComboBox, its items and its bindings are not
/// changed, so a value is only written when a result is chosen. Editable boxes keep their own behavior.
/// </summary>
internal static class ComboSearch
{
    internal const int MinimumItems=10;
    private static bool _installed;

    internal static void Install()
    {
        if(_installed)return;
        _installed=true;
        EventManager.RegisterClassHandler(typeof(ComboBox),UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler(OnMouseDown));
        EventManager.RegisterClassHandler(typeof(ComboBox),UIElement.PreviewKeyDownEvent,new KeyEventHandler(OnKeyDown));
        EventManager.RegisterClassHandler(typeof(ComboBox),UIElement.PreviewTextInputEvent,new TextCompositionEventHandler(OnTextInput));
    }

    internal static bool IsSearchable(ComboBox combo)=>!combo.IsEditable&&combo.IsEnabled&&!combo.IsDropDownOpen&&combo.Items.Count>=MinimumItems;

    private static void OnMouseDown(object sender,MouseButtonEventArgs e)
    {
        if(sender is not ComboBox combo||!IsSearchable(combo))return;
        e.Handled=true;combo.Focus();Open(combo,"");
    }
    private static void OnKeyDown(object sender,KeyEventArgs e)
    {
        if(sender is not ComboBox combo||!IsSearchable(combo))return;
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key==Key.F4||key==Key.Space||(key is Key.Down or Key.Up&&Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))){e.Handled=true;Open(combo,"");}
    }
    private static void OnTextInput(object sender,TextCompositionEventArgs e)
    {
        if(sender is not ComboBox combo||!IsSearchable(combo)||string.IsNullOrWhiteSpace(e.Text)||e.Text.Any(char.IsControl))return;
        e.Handled=true;Open(combo,e.Text);
    }

    private sealed record Row(object Item,string Label,string Key);

    internal sealed record SearchList(Popup Popup,TextBox Search,ListBox List);

    /// <summary>Opens the search list of <paramref name="combo"/> with <paramref name="query"/> already typed.</summary>
    internal static SearchList Open(ComboBox combo,string query)
    {
        var search=Build(combo,query);
        var popup=search.Popup;
        // StaysOpen=false closed the list as soon as the search field took the keyboard (the popup loses its mouse capture).
        // The list stays open and closes on a choice, Esc or Tab, a click elsewhere in the window, or when the window
        // is deactivated, moved or resized. A click on the same box only closes it.
        var window=Window.GetWindow(combo);
        void OutsideClick(object sender,MouseButtonEventArgs e){popup.IsOpen=false;if(combo.IsMouseOver)e.Handled=true;}
        void WindowChanged(object? sender,EventArgs e)=>popup.IsOpen=false;
        void SizeChanged(object sender,SizeChangedEventArgs e)=>popup.IsOpen=false;
        if(window is not null){window.PreviewMouseDown+=OutsideClick;window.Deactivated+=WindowChanged;window.LocationChanged+=WindowChanged;window.SizeChanged+=SizeChanged;}
        popup.Closed+=(_,_)=>
        {
            if(window is not null){window.PreviewMouseDown-=OutsideClick;window.Deactivated-=WindowChanged;window.LocationChanged-=WindowChanged;window.SizeChanged-=SizeChanged;}
            if(search.Search.IsKeyboardFocusWithin||Keyboard.FocusedElement is null)combo.Focus();
        };
        popup.IsOpen=true;
        return search;
    }

    /// <summary>The search list of <paramref name="combo"/>, closed (the checks drive it without showing a window).</summary>
    internal static SearchList Build(ComboBox combo,string query)
    {
        var rows=combo.Items.Cast<object>().Select(item=>{string label=Label(combo,item);return new Row(item,label,Normalize(label));}).ToList();
        var view=new ListCollectionView(rows);
        var search=new TextBox {Text=query,Margin=new Thickness(0,0,0,6),MinHeight=30};
        System.Windows.Automation.AutomationProperties.SetName(search,"Buscar en "+(System.Windows.Automation.AutomationProperties.GetName(combo) is { Length: >0 } name?name:"la lista"));
        var list=new ListBox {ItemsSource=view,DisplayMemberPath=nameof(Row.Label),MaxHeight=Math.Clamp(combo.MaxDropDownHeight,200,360),
            BorderThickness=new Thickness(0),Focusable=false};
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(list,true);
        var empty=new TextBlock {Text="Sin resultados. Prueba con otra parte del nombre o con su número.",TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(6,4,6,6),FontSize=12,Visibility=Visibility.Collapsed};
        empty.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        var layout=new DockPanel();
        DockPanel.SetDock(search,Dock.Top);layout.Children.Add(search);
        DockPanel.SetDock(empty,Dock.Top);layout.Children.Add(empty);
        layout.Children.Add(list);
        var border=new Border {Child=layout,Padding=new Thickness(6),Margin=new Thickness(0,3,0,0),CornerRadius=new CornerRadius(7),
            BorderThickness=new Thickness(1),MinWidth=Math.Max(combo.ActualWidth,280),MaxWidth=Math.Max(combo.ActualWidth,560)};
        border.SetResourceReference(Border.BackgroundProperty,"Surface");border.SetResourceReference(Border.BorderBrushProperty,"Accent");
        var popup=new Popup {PlacementTarget=combo,Placement=PlacementMode.Bottom,StaysOpen=true,AllowsTransparency=true,Child=border,PopupAnimation=PopupAnimation.None};
        TextElement.SetFontSize(border,combo.FontSize);TextElement.SetFontFamily(border,combo.FontFamily);

        void Apply()
        {
            string[] words=Normalize(search.Text).Split(' ',StringSplitOptions.RemoveEmptyEntries);
            view.Filter=words.Length==0?null:o=>words.All(((Row)o).Key.Contains);
            empty.Visibility=view.Count==0?Visibility.Visible:Visibility.Collapsed;
            var current=words.Length==0?rows.FirstOrDefault(r=>Equals(r.Item,combo.SelectedItem)):null;
            list.SelectedItem=current is not null&&view.Contains(current)?current:view.Count>0?view.GetItemAt(0):null;
            if(list.SelectedItem is not null)list.ScrollIntoView(list.SelectedItem);
        }
        void Choose(object? chosen)
        {
            if(chosen is not Row row)return;
            popup.IsOpen=false;
            if(!Equals(combo.SelectedItem,row.Item))combo.SelectedItem=row.Item;
            combo.Focus();
        }
        void Move(int delta)
        {
            if(view.Count==0)return;
            int index=Math.Clamp((list.SelectedItem is null?-1:view.IndexOf(list.SelectedItem))+delta,0,view.Count-1);
            list.SelectedItem=view.GetItemAt(index);list.ScrollIntoView(list.SelectedItem);
        }
        search.TextChanged+=(_,_)=>Apply();
        search.PreviewKeyDown+=(_,e)=>
        {
            switch(e.Key)
            {
                case Key.Down:Move(1);e.Handled=true;break;
                case Key.Up:Move(-1);e.Handled=true;break;
                case Key.PageDown:Move(10);e.Handled=true;break;
                case Key.PageUp:Move(-10);e.Handled=true;break;
                case Key.Enter:Choose(list.SelectedItem);e.Handled=true;break;
                case Key.Escape:popup.IsOpen=false;combo.Focus();e.Handled=true;break;
                case Key.Tab:popup.IsOpen=false;combo.Focus();break;
            }
        };
        list.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            for(var node=e.OriginalSource as DependencyObject;node is not null&&!ReferenceEquals(node,list);node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
                if(node is ListBoxItem item){Choose(item.DataContext);e.Handled=true;return;}
        };
        popup.Opened+=(_,_)=>{search.Focus();Keyboard.Focus(search);search.CaretIndex=search.Text.Length;};
        Apply();
        return new SearchList(popup,search,list);
    }

    private static string Label(ComboBox combo,object item)
    {
        object? value=item switch
        {
            ComboBoxItem box=>box.Content,
            _ when !string.IsNullOrEmpty(combo.DisplayMemberPath)=>item.GetType().GetProperty(combo.DisplayMemberPath)?.GetValue(item),
            _=>item
        };
        return value?.ToString()??"";
    }

    internal static string Normalize(string text)=>new string(text.Normalize(NormalizationForm.FormD)
        .Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).Select(char.ToLowerInvariant).ToArray());
}
