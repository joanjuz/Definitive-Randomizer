using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using Panel=System.Windows.Controls.Panel;
using HorizontalAlignment=System.Windows.HorizontalAlignment;
using Orientation=System.Windows.Controls.Orientation;

namespace ThreeDS.Progressive;

/// <summary>
/// Options column next to a table: one card per group (title, optional description, controls), full-width buttons with the
/// same spacing, checkboxes and help text aligned. Existing controls are moved into the cards, so their handlers do not change.
/// </summary>
internal static class OptionCards
{
    internal static StackPanel Column(params UIElement[] cards)
    {
        var column=new StackPanel();
        foreach(var card in cards)column.Children.Add(card);
        return column;
    }

    /// <summary>A card with the given controls. Panels of buttons or checkboxes are laid out vertically (or in <paramref name="buttonColumns"/> columns).</summary>
    internal static Border Card(string title,string? description,IEnumerable<UIElement> content,int buttonColumns=1)
    {
        var body=new StackPanel();
        body.Children.Add(new TextBlock{Text=title,FontSize=14,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,description is null?10:4)});
        ((TextBlock)body.Children[0]).SetResourceReference(TextBlock.ForegroundProperty,"TextMain");
        if(description is not null)body.Children.Add(Help(description));
        foreach(var element in content)body.Children.Add(Normalize(Detach(element),buttonColumns));
        var card=new Border{Child=body,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(14,12,14,6),Margin=new Thickness(0,0,0,12)};
        card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        return card;
    }

    internal static Border Card(string title,string? description,params UIElement[] content)=>Card(title,description,(IEnumerable<UIElement>)content);

    internal static Button Primary(Button button){button.SetResourceReference(FrameworkElement.StyleProperty,"PrimaryButton");return button;}

    private static TextBlock Help(string text)
    {
        var help=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,0,0,10)};
        help.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        return help;
    }

    private static UIElement Detach(UIElement element)
    {
        switch((element as FrameworkElement)?.Parent)
        {
            case Panel panel:panel.Children.Remove(element);break;
            case Decorator decorator:decorator.Child=null;break;
            case ContentControl control:control.Content=null;break;
        }
        return element;
    }

    // Buttons and checkboxes that were in a WrapPanel (horizontal toolbar) go one under another, full width.
    private static UIElement Normalize(UIElement element,int buttonColumns)
    {
        switch(element)
        {
            case Button button:return Fit(button);
            case CheckBox check:check.Margin=new Thickness(0,3,0,3);check.HorizontalAlignment=HorizontalAlignment.Left;return check;
            case TextBlock text when text.TextWrapping==TextWrapping.Wrap&&text.FontSize<=13&&text.Inlines.Count<=1:
                text.FontSize=12;text.Margin=new Thickness(0,2,0,10);text.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");return text;
            case WrapPanel wrap:
            {
                var items=wrap.Children.Cast<UIElement>().ToArray();wrap.Children.Clear();
                bool buttons=items.OfType<Button>().Any();
                Panel target=buttons&&buttonColumns>1?new UniformGrid{Columns=buttonColumns}:new StackPanel();
                target.Margin=new Thickness(0,0,0,4);
                foreach(var item in items)target.Children.Add(Normalize(item,buttonColumns));
                if(target is UniformGrid grid)
                {
                    foreach(var b in target.Children.OfType<Button>())b.Margin=new Thickness(0,0,8,8);
                    // Two columns only while each button keeps about 130 px; a narrow card (small screens) uses one.
                    grid.SizeChanged+=(_,e)=>{int columns=e.NewSize.Width>=buttonColumns*130?buttonColumns:1;if(grid.Columns!=columns)grid.Columns=columns;};
                }
                return target;
            }
            case StackPanel stack when stack.Orientation==Orientation.Vertical:
            {
                var items=stack.Children.Cast<UIElement>().ToArray();stack.Children.Clear();
                foreach(var item in items)stack.Children.Add(Normalize(item,buttonColumns));
                stack.Margin=new Thickness(0);
                return stack;
            }
            default:return element;
        }
    }

    private static Button Fit(Button button)
    {
        button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Center;
        button.Margin=new Thickness(0,0,0,8);button.MinHeight=34;
        // In a narrow column (small screens) a long label goes on two lines instead of being cut.
        if(button.Content is string)button.SetResourceReference(ContentControl.ContentTemplateProperty,"WrappedActionContent");
        return button;
    }
}
