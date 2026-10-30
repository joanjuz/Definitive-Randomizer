using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using Binding=System.Windows.Data.Binding;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
namespace ThreeDS.Progressive;
internal static class EditorPresentation
{
    internal enum ActionRole { Primary, Secondary, Tertiary }
    internal static void StyleAction(Button button,ActionRole role=ActionRole.Secondary)
    {
        button.SetResourceReference(FrameworkElement.StyleProperty,role switch {ActionRole.Primary=>"PrimaryButton",ActionRole.Tertiary=>"TertiaryButton",_=>"SecondaryButton"});
    }
    internal static FrameworkElement EmptyState(ItemsControl items,string title,string explanation)
    {
        var root=new System.Windows.Controls.Grid();root.Children.Add(items);
        var message=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new Thickness(20),MaxWidth=420,IsHitTestVisible=false};
        message.Children.Add(new TextBlock{Text=title,FontSize=17,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Foreground=(Brush)System.Windows.Application.Current.FindResource("TextMain")});
        message.Children.Add(new TextBlock{Text=explanation,Margin=new Thickness(0,8,0,0),TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Foreground=(Brush)System.Windows.Application.Current.FindResource("TextSoft")});
        var style=new Style(typeof(StackPanel));style.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed));var trigger=new DataTrigger{Binding=new Binding("HasItems"){Source=items},Value=false};trigger.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Visible));style.Triggers.Add(trigger);message.Style=style;root.Children.Add(message);return root;
    }
}
public sealed class NavigationLabelConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {string s=value?.ToString()??"";int split=s.IndexOf("  ",StringComparison.Ordinal);return split>=0?s[(split+2)..]:s;}
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
public sealed class NavigationIconConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        return ItemIcons.ForLabel(value?.ToString()??"");
    }
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
internal sealed class BankEntryCaptionConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        if(value is not RecordEntry entry)return "";int split=entry.Name.IndexOf(" · [",StringComparison.Ordinal);
        return parameter?.ToString()=="detail"?(split>=0?entry.Name[(split+3)..]:"Archivo de texto"):$"#{entry.Id:000} · "+(split>=0?entry.Name[..split]:entry.Name);
    }
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}

public sealed class NavigationActiveConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is SolidColorBrush brush&&brush.Color.A>0;
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
