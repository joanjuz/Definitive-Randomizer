using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush=System.Windows.Media.Brush;
using Color=System.Windows.Media.Color;
using KeyEventArgs=System.Windows.Input.KeyEventArgs;
using ListBox=System.Windows.Controls.ListBox;
using TextBox=System.Windows.Controls.TextBox;
using Orientation=System.Windows.Controls.Orientation;
namespace ThreeDS.Progressive;

/// <summary>One destination of the palette: a section, a hub tool or a general action. Run only navigates or opens what the user could already open.</summary>
internal sealed record PaletteEntry(string Title,string Location,string Description,string Keywords,Action Run);

/// <summary>
/// «Buscar sección o herramienta» (Ctrl+K): overlay over the main window. It lists every destination available for the open game,
/// including the tools that live inside the hubs, and says where each one is so the user learns the layout.
/// </summary>
internal sealed class CommandPalette : Grid
{
    private readonly IReadOnlyList<PaletteEntry> _entries;
    private readonly Action _close;
    private readonly TextBox _query=new(){FontSize=15,Padding=new Thickness(12,9,12,9)};
    private readonly ListBox _results=new(){MaxHeight=430};
    private readonly TextBlock _empty=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8,14,8,6),Visibility=Visibility.Collapsed};
    private readonly TextBlock _count=new(){FontSize=12,Margin=new Thickness(2,8,2,8)};
    private bool _done;
    internal TextBox Query=>_query;
    internal ListBox Results=>_results;
    internal CommandPalette(IReadOnlyList<PaletteEntry> entries,Action close)
    {
        _entries=entries;_close=close;
        // A dimmed layer: clicking outside the card closes the palette without running anything.
        var shade=new Border{Background=new SolidColorBrush(Color.FromArgb(150,0,0,0))};
        shade.MouseLeftButtonDown+=(_,_)=>Close();
        Children.Add(shade);
        var card=new Border{MaxWidth=640,Margin=new Thickness(24,64,24,24),VerticalAlignment=VerticalAlignment.Top,
            BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(14)};
        card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Accent");
        var stack=new StackPanel();card.Child=stack;
        var title=new TextBlock{Text="¿Qué quieres cambiar?",FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(2,0,2,10)};
        title.SetResourceReference(TextBlock.ForegroundProperty,"TextMain");
        stack.Children.Add(title);
        InputHint.SetText(_query,"Escribe una sección o herramienta: shiny, niveles, tiendas, MT, Rebuild…");
        AutomationProperties.SetName(_query,"Buscar sección o herramienta");
        stack.Children.Add(_query);
        _count.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        AutomationProperties.SetLiveSetting(_count,AutomationLiveSetting.Polite);
        stack.Children.Add(_count);
        _results.ItemTemplate=BuildTemplate();
        ScrollViewer.SetHorizontalScrollBarVisibility(_results,System.Windows.Controls.ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_results,"Resultados");
        stack.Children.Add(_results);
        _empty.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        stack.Children.Add(_empty);
        var keys=new TextBlock{Text="↑ ↓ para moverte · Intro para abrir · Esc para cerrar",FontSize=11,Margin=new Thickness(2,10,2,0)};
        keys.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        stack.Children.Add(keys);
        Children.Add(card);
        _query.TextChanged+=(_,_)=>Filter();
        _query.PreviewKeyDown+=QueryKey;
        _results.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;RunSelected();}else if(e.Key==Key.Escape){e.Handled=true;Close();}};
        _results.PreviewMouseLeftButtonUp+=(_,e)=>{if(ItemsControl.ContainerFromElement(_results,(DependencyObject)e.OriginalSource) is ListBoxItem){RunSelected();}};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}};
        Filter();
        Loaded+=(_,_)=>{_query.Focus();Keyboard.Focus(_query);};
    }
    private static DataTemplate BuildTemplate()
    {
        var root=new FrameworkElementFactory(typeof(StackPanel));root.SetValue(MarginProperty,new Thickness(2,3,2,3));
        var top=new FrameworkElementFactory(typeof(DockPanel));
        var location=new FrameworkElementFactory(typeof(TextBlock));
        location.SetValue(DockPanel.DockProperty,Dock.Right);location.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(PaletteEntry.Location)));
        location.SetValue(TextBlock.FontSizeProperty,11d);location.SetValue(TextBlock.MarginProperty,new Thickness(10,0,0,0));location.SetValue(TextBlock.OpacityProperty,.8);
        location.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);
        var name=new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(PaletteEntry.Title)));name.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);
        name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        top.AppendChild(location);top.AppendChild(name);
        var description=new FrameworkElementFactory(typeof(TextBlock));
        description.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(PaletteEntry.Description)));
        description.SetValue(TextBlock.FontSizeProperty,12d);description.SetValue(TextBlock.OpacityProperty,.85);
        description.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);description.SetValue(TextBlock.MarginProperty,new Thickness(0,2,0,0));
        root.AppendChild(top);root.AppendChild(description);
        return new DataTemplate(typeof(PaletteEntry)){VisualTree=root};
    }
    private void QueryKey(object sender,KeyEventArgs e)
    {
        int count=_results.Items.Count;
        switch(e.Key)
        {
            case Key.Down when count>0:_results.SelectedIndex=Math.Min(count-1,_results.SelectedIndex+1);_results.ScrollIntoView(_results.SelectedItem);e.Handled=true;break;
            case Key.Up when count>0:_results.SelectedIndex=Math.Max(0,_results.SelectedIndex-1);_results.ScrollIntoView(_results.SelectedItem);e.Handled=true;break;
            case Key.Enter:RunSelected();e.Handled=true;break;
            case Key.Escape:Close();e.Handled=true;break;
        }
    }
    internal static string Normalize(string text)
    {
        var decomposed=text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder=new StringBuilder(decomposed.Length);
        foreach(char c in decomposed)if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)builder.Append(c);
        return builder.ToString();
    }
    /// <summary>Every word must appear somewhere; titles weigh more than keywords and descriptions. Empty query: catalog order.</summary>
    internal static IReadOnlyList<PaletteEntry> Search(IReadOnlyList<PaletteEntry> entries,string query)
    {
        var words=Normalize(query).Split([' ','·',',','/'],StringSplitOptions.RemoveEmptyEntries);
        if(words.Length==0)return entries;
        var scored=new List<(PaletteEntry Entry,int Score,int Order)>();
        for(int i=0;i<entries.Count;i++)
        {
            var entry=entries[i];
            string title=Normalize(entry.Title),rest=Normalize(entry.Location+" "+entry.Keywords+" "+entry.Description);
            int score=0;bool all=true;
            foreach(var word in words)
            {
                if(title.StartsWith(word,StringComparison.Ordinal))score+=6;
                else if(title.Split(' ').Any(w=>w.StartsWith(word,StringComparison.Ordinal)))score+=4;
                else if(title.Contains(word,StringComparison.Ordinal))score+=3;
                else if(Normalize(entry.Keywords).Contains(word,StringComparison.Ordinal))score+=2;
                else if(rest.Contains(word,StringComparison.Ordinal))score+=1;
                else{all=false;break;}
            }
            if(all)scored.Add((entry,score,i));
        }
        return scored.OrderByDescending(s=>s.Score).ThenBy(s=>s.Order).Select(s=>s.Entry).ToArray();
    }
    private void Filter()
    {
        var found=Search(_entries,_query.Text);
        _results.ItemsSource=found;
        _results.SelectedIndex=found.Count>0?0:-1;
        _empty.Visibility=found.Count==0?Visibility.Visible:Visibility.Collapsed;
        _empty.Text="No hay coincidencias para «"+_query.Text.Trim()+"». Prueba con otra palabra o abre la vista guiada (F1).";
        _count.Text=found.Count==_entries.Count?$"{found.Count} destinos disponibles para este juego":$"{found.Count} de {_entries.Count} destinos";
    }
    internal void RunSelected()
    {
        if(_done||_results.SelectedItem is not PaletteEntry entry)return;
        Close();
        entry.Run();
    }
    internal void Close(){if(_done)return;_done=true;_close();}
}
