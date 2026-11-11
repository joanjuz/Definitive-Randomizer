using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Border=System.Windows.Controls.Border;
using Button=System.Windows.Controls.Button;
using Grid=System.Windows.Controls.Grid;
using ListBox=System.Windows.Controls.ListBox;
using Panel=System.Windows.Controls.Panel;
using Binding=System.Windows.Data.Binding;
using Control=System.Windows.Controls.Control;
using Brushes=System.Windows.Media.Brushes;
using SelectionMode=System.Windows.Controls.SelectionMode;

namespace ThreeDS.Progressive;

/// <summary>
/// Presentation-only layout shared by the Gen6 and Gen7 mart editors: shop list, inventory table and actions in
/// three columns, narrower as the editor shrinks. Only below <see cref="CompactWidth"/> do the shop list and the
/// actions share the side column (both scroll vertically) so the table keeps its width. Controls, bindings and
/// events stay owned by each view.
/// </summary>
internal sealed class MartWorkspace : Grid
{
    internal const double CompactWidth=720,NarrowWidth=900,WideWidth=1180;
    /// <summary>Height under which a mart editor scrolls as a page in the main window instead of crushing its table.</summary>
    internal const double MinimumHeight=420;
    private readonly FrameworkElement _shops,_inventory,_actions;
    /// <summary>"Wide", "Medium", "Narrow" or "Compact": the arrangement applied for the current width.</summary>
    internal string Mode{get;private set;}="";

    internal MartWorkspace(FrameworkElement shops,FrameworkElement inventory,FrameworkElement actions)
    {
        _shops=shops;_inventory=inventory;_actions=actions;
        foreach(double width in new[]{260d,12,0,12,300})
            ColumnDefinitions.Add(new ColumnDefinition{Width=width==0?new GridLength(1,GridUnitType.Star):new GridLength(width)});
        ColumnDefinitions[2].MinWidth=260;
        RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        RowDefinitions.Add(new RowDefinition{Height=new GridLength(0)});
        // Child order is the keyboard order: shops → inventory → actions in every arrangement.
        Children.Add(shops);Children.Add(inventory);Children.Add(actions);
        Grid.SetColumn(inventory,2);
        Arrange(WideWidth);
        SizeChanged+=(_,e)=>{if(e.WidthChanged)Arrange(e.NewSize.Width);};
    }

    private void Arrange(double width)
    {
        string mode=ModeFor(width);
        if(mode==Mode)return;Mode=mode;
        bool compact=mode=="Compact";
        (double shops,double actions)=mode switch{"Wide"=>(260d,300d),"Medium"=>(210d,250d),"Narrow"=>(180d,220d),_=>(240d,0d)};
        ColumnDefinitions[0].Width=new GridLength(shops);
        double gap=mode=="Narrow"?10:12;
        ColumnDefinitions[1].Width=new GridLength(gap);
        ColumnDefinitions[3].Width=new GridLength(compact?0:gap);
        ColumnDefinitions[4].Width=new GridLength(actions);
        RowDefinitions[1].Height=compact?new GridLength(1.6,GridUnitType.Star):new GridLength(0);
        RowDefinitions[1].MinHeight=compact?150:0;
        Grid.SetRow(_shops,0);Grid.SetRowSpan(_shops,compact?1:2);
        Grid.SetRowSpan(_inventory,2);
        Grid.SetColumn(_actions,compact?0:4);Grid.SetRow(_actions,compact?1:0);Grid.SetRowSpan(_actions,compact?1:2);
        _actions.Margin=compact?new Thickness(0,12,0,0):new Thickness(0);
    }

    internal static string ModeFor(double width)=>width<CompactWidth?"Compact":width<NarrowWidth?"Narrow":width<WideWidth?"Medium":"Wide";

    /// <summary>Gives the change list less height when the editor is short, so the table keeps most of it.</summary>
    internal void FitChanges(ListBox changes,double tall)
    {
        void Fit(double height)=>changes.Height=height<340?52:height<440?64:height<600?92:tall;
        SizeChanged+=(_,e)=>{if(e.HeightChanged)Fit(e.NewSize.Height);};
        Fit(double.PositiveInfinity);
    }

    /// <summary>
    /// On a short editor (high Windows scale or a low window) the page description takes a single line, with the
    /// full text as its tooltip, and on a very short one it gives its line to the table. It only describes the
    /// editor: every figure it gives is also in the shop list and the heading of the table.
    /// </summary>
    internal static void FitDescription(FrameworkElement view,TextBlock description)
    {
        description.ToolTip=description.Text;description.TextTrimming=TextTrimming.CharacterEllipsis;
        void Fit(double height)
        {
            description.TextWrapping=height<560?TextWrapping.NoWrap:TextWrapping.Wrap;
            description.Visibility=height<450?Visibility.Collapsed:Visibility.Visible;
        }
        view.SizeChanged+=(_,e)=>{if(e.HeightChanged)Fit(e.NewSize.Height);};
    }

    /// <summary>Name of the selected shop and its details on one line; a shortened text keeps the full one as tooltip.</summary>
    internal static DockPanel ShopHeading(TextBlock title,TextBlock detail)
    {
        foreach(var text in new[]{title,detail})
        {
            text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;
            text.VerticalAlignment=VerticalAlignment.Bottom;
            text.SetBinding(FrameworkElement.ToolTipProperty,new Binding(nameof(TextBlock.Text)){RelativeSource=RelativeSource.Self});
        }
        title.Margin=new Thickness(0,0,14,0);detail.Margin=new Thickness(0,0,0,2);
        DockPanel.SetDock(title,Dock.Left);
        return new DockPanel{Margin=new Thickness(0,0,0,8),LastChildFill=true,Children={title,detail}};
    }

    /// <summary>Vertical-only scrolling inside a card, on the card's own surface.</summary>
    internal static ScrollViewer Scroll(UIElement content,double gutter=0)=>new()
    {
        Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
        Background=Brushes.Transparent,Padding=new Thickness(0,0,gutter,0)
    };

    internal static Border Card(UIElement child)
    {
        var card=new Border{Child=child};card.SetResourceReference(StyleProperty,"EditorPanel");
        return card;
    }

    internal static TextBlock Heading(Panel parent,string text,bool first=false)
    {
        var heading=new TextBlock{Text=text,Margin=new Thickness(0,first?0:14,0,8)};
        heading.SetResourceReference(StyleProperty,"InspectorGroupHeading");
        parent.Children.Add(heading);
        return heading;
    }

    internal static TextBlock Caption(Panel parent,string text)
    {
        var caption=new TextBlock{Text=text};caption.SetResourceReference(StyleProperty,"FieldCaption");
        parent.Children.Add(caption);
        return caption;
    }

    /// <summary>Full-width action with wrapping text; the content stays the plain label (names and automation).</summary>
    internal static Button Action(Panel parent,string label,Action action)
    {
        var button=new Button{Content=label};
        Stack(button);
        button.Click+=(_,_)=>action();
        parent.Children.Add(button);
        return button;
    }

    internal static void Stack(Control control)
    {
        control.HorizontalAlignment=System.Windows.HorizontalAlignment.Stretch;
        control.HorizontalContentAlignment=System.Windows.HorizontalAlignment.Left;
        control.Margin=new Thickness(0,0,0,6);
        if(control is ContentControl content&&content.Content is string)content.SetResourceReference(ContentControl.ContentTemplateProperty,"WrappedOptionContent");
    }

    private static DataTemplate? _included;
    /// <summary>Recipe toggles show a check mark when included, so the state does not rely on colour alone.</summary>
    internal static void ShowIncluded(Button button,bool included)
    {
        if(!included){button.SetResourceReference(ContentControl.ContentTemplateProperty,"WrappedOptionContent");return;}
        if(_included is null)
        {
            var row=new FrameworkElementFactory(typeof(DockPanel));
            var mark=new FrameworkElementFactory(typeof(TextBlock));
            mark.SetValue(TextBlock.TextProperty,"✓");mark.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,8,0));
            mark.SetValue(DockPanel.DockProperty,Dock.Left);
            var text=new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty,new Binding());text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
            row.AppendChild(mark);row.AppendChild(text);
            _included=new DataTemplate{VisualTree=row};_included.Seal();
        }
        button.ContentTemplate=_included;
    }

    /// <summary>
    /// A shop list inside the scrolling side column: it shows every shop (no inner scrolling), forwards the mouse
    /// wheel to that column, supports type-ahead by name and shows the number of objects of each shop.
    /// </summary>
    internal static void ConfigureShopList(ListBox list,string name)
    {
        list.HorizontalContentAlignment=System.Windows.HorizontalAlignment.Stretch;
        ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        TextSearch.SetTextPath(list,nameof(Services.MartShop.Name));
        System.Windows.Automation.AutomationProperties.SetName(list,name);
        list.SelectionMode=SelectionMode.Single;
        // The list's own (disabled) ScrollViewer would take the page background from the application style.
        var scroller=new Style(typeof(ScrollViewer));scroller.Setters.Add(new Setter(BackgroundProperty,Brushes.Transparent));
        list.Resources[typeof(ScrollViewer)]=scroller;
        var row=new FrameworkElementFactory(typeof(DockPanel));
        var count=new FrameworkElementFactory(typeof(TextBlock));
        count.SetValue(DockPanel.DockProperty,Dock.Right);count.SetValue(FrameworkElement.MarginProperty,new Thickness(8,0,0,0));
        count.SetValue(TextBlock.FontSizeProperty,12d);
        count.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        count.SetValue(FrameworkElement.ToolTipProperty,"Objetos en la tienda");
        count.SetBinding(TextBlock.TextProperty,new Binding("Rows.Count"));
        var label=new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty,new Binding(nameof(Services.MartShop.Name)));
        label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        row.AppendChild(count);row.AppendChild(label);
        list.ItemTemplate=new DataTemplate{VisualTree=row};
        list.PreviewMouseWheel+=(sender,e)=>
        {
            if(e.Handled||((FrameworkElement)sender).Parent is not UIElement parent)return;
            e.Handled=true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice,e.Timestamp,e.Delta){RoutedEvent=MouseWheelEvent,Source=sender});
        };
        SetListActive(list,true);
    }

    private static Style? _activeItem,_inactiveItem;
    /// <summary>
    /// Gen7 keeps a selected shop in both lists (normal and BP). Only the list whose table is shown fills its
    /// selection; the other one outlines it, so a single shop reads as the one being edited.
    /// </summary>
    internal static void SetListActive(ListBox list,bool active)
    {
        if(_activeItem is null)
        {
            var baseline=(Style)System.Windows.Application.Current.FindResource(typeof(ListBoxItem));
            _activeItem=new Style(typeof(ListBoxItem),baseline);
            _activeItem.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,7,9,7)));
            _activeItem.Seal();
            var surface=new FrameworkElementFactory(typeof(Border)){Name="Surface"};
            surface.SetValue(Border.CornerRadiusProperty,new CornerRadius(6));
            surface.SetValue(Border.BorderThicknessProperty,new Thickness(1));
            surface.SetValue(Border.BackgroundProperty,Brushes.Transparent);
            surface.SetValue(Border.BorderBrushProperty,Brushes.Transparent);
            surface.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
            surface.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            var template=new ControlTemplate(typeof(ListBoxItem)){VisualTree=surface};
            var selected=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};
            selected.Setters.Add(new Setter(Border.BorderBrushProperty,new DynamicResourceExtension("InputStroke"),"Surface"));
            var hover=new Trigger{Property=IsMouseOverProperty,Value=true};
            hover.Setters.Add(new Setter(Border.BackgroundProperty,new DynamicResourceExtension("Hover"),"Surface"));
            hover.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("SelectionText")));
            var focus=new Trigger{Property=IsKeyboardFocusWithinProperty,Value=true};
            focus.Setters.Add(new Setter(Border.BorderBrushProperty,new DynamicResourceExtension("Accent"),"Surface"));
            template.Triggers.Add(selected);template.Triggers.Add(hover);template.Triggers.Add(focus);
            _inactiveItem=new Style(typeof(ListBoxItem),_activeItem);
            _inactiveItem.Setters.Add(new Setter(Control.TemplateProperty,template));
            _inactiveItem.Seal();
        }
        list.ItemContainerStyle=active?_activeItem:_inactiveItem;
        System.Windows.Automation.AutomationProperties.SetItemStatus(list,active?"Tabla visible":"");
    }
}
