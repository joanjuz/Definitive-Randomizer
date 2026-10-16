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
        string label=value?.ToString()??"";string data=label.Contains("Batch ROM")?"M1,2 H6 V7 H1 Z M10,2 H15 V7 H10 Z M1,10 H6 V15 H1 Z M10,10 H15 V15 H10 Z":label.Contains("Global Template")?"M3,1 H10 L14,5 V15 H3 Z M10,1 V5 H14 M6,8 H11 M6,11 H11":label.Contains("Templates")?"M3,1 H11 V13 H3 Z M6,4 H9 M6,7 H9 M6,10 H9 M12,4 H15 V16 H6":label.Contains("estáticos")?"M8,1 A7,7 0 1 1 1,8 A7,7 0 0 1 8,1 M8,4 V9 M8,12 V13":label.Contains("Story Text")?"M2,2 H14 V11 H7 L3,15 V11 H2 Z M5,5 H11 M5,8 H10":label.Contains("Texto")||label.Contains("Text")?"M2,3 H14 M2,7 H14 M2,11 H10":label.Contains("Historial")?"M8,1 A7,7 0 1 1 1,8 A7,7 0 0 1 8,1 M8,4 V8 L11,10":label.Contains("Pokémon")&&!label.Contains("Herramientas")?"M8,1 A7,7 0 1 1 1,8 A7,7 0 0 1 8,1 M1,8 H5 M11,8 H15 M8,5 A3,3 0 1 1 5,8 A3,3 0 0 1 8,5":label.Contains("Encuentros")?"M2,2 L6,4 L10,2 L14,4 V14 L10,12 L6,14 L2,12 Z M6,4 V14 M10,2 V12":label.Contains("Entrenadores")?"M6,2 A3,3 0 1 1 3,5 A3,3 0 0 1 6,2 M1,15 V12 Q6,7 11,12 V15 M12,2 Q16,5 12,8 M13,10 Q16,11 15,15":label.Contains("Movimientos")||label.Contains("Level Up")?"M9,1 L3,9 H7 L6,15 L13,6 H9 Z":label.Contains("Opciones")?"M2,4 H14 M2,8 H14 M2,12 H14 M5,2 V6 M11,6 V10 M6,10 V14":label.Contains("Herramientas")||label.Contains("Mecánicas")?"M9,2 Q13,0 15,3 L12,5 L11,4 L9,6 L10,8 L3,15 L1,13 L8,6 Q6,3 9,2":label.Contains("Evoluciones")?"M2,8 H13 M9,3 L14,8 L9,13": "M2,2 H14 V14 H2 Z M2,6 H14 M6,6 V14";
        return Geometry.Parse(data);
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
