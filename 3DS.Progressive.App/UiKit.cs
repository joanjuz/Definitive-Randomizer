using System.Windows;
using System.Windows.Controls;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
namespace ThreeDS.Progressive;

/// <summary>One-way converter from a lambda, for bindings built in code.</summary>
internal sealed class LambdaConverter(Func<object?,object?> convert):System.Windows.Data.IValueConverter
{
    public object? Convert(object? value,Type targetType,object? parameter,System.Globalization.CultureInfo culture)=>convert(value);
    public object ConvertBack(object? value,Type targetType,object? parameter,System.Globalization.CultureInfo culture)=>System.Windows.Data.Binding.DoNothing;
}
/// <summary>
/// Small presentation pieces shared by the views built in code (cards, chips, captions, link chips, filter boxes).
/// Every colour is a theme resource reference, so high contrast and theme changes apply to them too.
/// </summary>
internal static class UiKit
{
    /// <summary>Card with an upper-case caption, the same look as the hub and overview cards.</summary>
    internal static Border Card(string title,out StackPanel body,Thickness? margin=null)
    {
        body=new StackPanel();
        if(title.Length>0)
        {
            var heading=new TextBlock{Text=title,FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10),TextWrapping=TextWrapping.Wrap};
            heading.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");body.Children.Add(heading);
        }
        var card=new Border{CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Padding=new Thickness(16,14,16,10),Margin=margin??new Thickness(0,0,0,14),Child=body};
        card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        return card;
    }
    /// <summary>Read-only tag («3 Pokémon», «Nv. 12–15»).</summary>
    internal static Border Chip(string text)
    {
        var label=new TextBlock{Text=text,FontSize=12,FontWeight=FontWeights.SemiBold};
        label.SetResourceReference(TextBlock.ForegroundProperty,"TextMain");
        var chip=new Border{CornerRadius=new CornerRadius(6),Padding=new Thickness(10,3,10,4),Margin=new Thickness(0,0,6,6),BorderThickness=new Thickness(1),Child=label};
        chip.SetResourceReference(Border.BackgroundProperty,"Surface");chip.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        return chip;
    }
    /// <summary>Secondary text line.</summary>
    internal static TextBlock Small(string text,bool soft=true,double size=11,bool wrap=false)
    {
        var block=new TextBlock{Text=text,FontSize=size,TextTrimming=wrap?TextTrimming.None:TextTrimming.CharacterEllipsis,TextWrapping=wrap?TextWrapping.Wrap:TextWrapping.NoWrap};
        if(soft)block.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        return block;
    }
    /// <summary>Clickable chip that navigates or filters («Poción ×12», «Área 003 · oculto»).</summary>
    internal static Button LinkChip(string text,Action run,bool current=false)
    {
        var chip=new Button{Content=text,MinHeight=28,Padding=new Thickness(10,3,10,3),Margin=new Thickness(0,0,6,6)};
        EditorPresentation.StyleAction(chip,current?EditorPresentation.ActionRole.Primary:EditorPresentation.ActionRole.Secondary);
        chip.Click+=(_,_)=>run();return chip;
    }
    /// <summary>Bold line used as the heading of a block inside a card or a details panel.</summary>
    internal static TextBlock Heading(string text,double size=14,Thickness? margin=null)
    {
        var block=new TextBlock{Text=text,FontSize=size,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=margin??new Thickness(0,12,0,6)};
        block.SetResourceReference(TextBlock.ForegroundProperty,"TextMain");return block;
    }
    /// <summary>Search box with placeholder.</summary>
    internal static TextBox SearchBox(string hint,string accessibleName)
    {
        var box=new TextBox{MinWidth=220};InputHint.SetText(box,hint);
        System.Windows.Automation.AutomationProperties.SetName(box,accessibleName);return box;
    }
}
