using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using Image=System.Windows.Controls.Image;
using Orientation=System.Windows.Controls.Orientation;
using TextBlock=System.Windows.Controls.TextBlock;
namespace ThreeDS.Progressive;
public partial class RecordEditorView
{
    // Header card of the 3DS Pokémon editor (sprite, name, type badges, BST and saved state) and the total in the Inspector,
    // for editors whose record is one species (IRecordSummary).
    private Border? _summaryCard,_summaryTotalCard;
    private TextBlock? _summaryFallback,_summaryTitle,_summarySubtitle,_summaryTotal,_summaryState,_summaryInspectorTotal;
    private WrapPanel? _summaryBadges;
    private Image? _summarySprite;
    private int _summarySpecies=-1;

    private void UpdateSummary(bool reload)
    {
        if(Editor is not IRecordSummary summaries||_id<0)return;
        if(summaries.Summary(_id,Values(_fields)) is not {} summary)return;
        if(reload||_summaryCard is null||!FieldsPanel.Children.Contains(_summaryCard))
        {
            _summaryCard=BuildSummaryCard();FieldsPanel.Children.Insert(0,_summaryCard);_summarySpecies=-1;
            if(_summaryTotalCard is null&&RandomizerButton.Parent is StackPanel inspector){_summaryTotalCard=BuildTotalCard();inspector.Children.Insert(inspector.Children.IndexOf(RandomizerButton)+1,_summaryTotalCard);}
        }
        _summaryTitle!.Text=summary.Title;_summarySubtitle!.Text=summary.Subtitle;
        _summaryBadges!.Children.Clear();
        foreach(string badge in summary.Badges)
        {
            var chip=new Border{CornerRadius=new CornerRadius(6),Padding=new Thickness(10,4,10,4),Margin=new Thickness(0,0,6,6),Child=new TextBlock{Text=badge,FontSize=12,FontWeight=FontWeights.SemiBold}};
            chip.SetResourceReference(Border.BackgroundProperty,"Selected");((TextBlock)chip.Child).SetResourceReference(TextBlock.ForegroundProperty,"SelectionText");
            _summaryBadges.Children.Add(chip);
        }
        _summaryTotal!.Text=summary.Total.ToString(System.Globalization.CultureInfo.InvariantCulture);_summaryInspectorTotal!.Text=_summaryTotal.Text;
        _summaryState!.Text=_dirty?"Cambios sin guardar":"Guardado";
        if(summary.Species!=_summarySpecies)
        {
            _summarySpecies=summary.Species;_summarySprite!.Source=null;_summaryFallback!.Text="#"+summary.Species.ToString("000",System.Globalization.CultureInfo.InvariantCulture);_summaryFallback.Visibility=Visibility.Visible;int requested=summary.Species;
            _=LoadSummarySpriteAsync(requested);
        }
    }

    private async System.Threading.Tasks.Task LoadSummarySpriteAsync(int species)
    {
        try
        {
            var result=await _trainerSpriteService.GetSpriteAsync(species,species,0,default);
            if(_summarySpecies==species&&_summarySprite is not null&&result.Image is not null){_summarySprite.Source=result.Image;_summaryFallback!.Visibility=Visibility.Collapsed;}
        }
        catch(Exception ex) when (ex is System.Net.Http.HttpRequestException or OperationCanceledException or System.IO.IOException or InvalidOperationException){}
    }

    private Border BuildSummaryCard()
    {
        var card=new Border{CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Padding=new Thickness(16),Margin=new Thickness(0,0,0,12)};
        card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});card.Child=grid;
        var frame=new Border{Width=104,Height=104,CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,16,0)};
        frame.SetResourceReference(Border.BackgroundProperty,"Surface");frame.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        _summarySprite=new Image{Width=80,Height=80,Stretch=Stretch.Uniform};RenderOptions.SetBitmapScalingMode(_summarySprite,BitmapScalingMode.NearestNeighbor);
        // Without a sprite (offline or not downloaded yet) the frame shows the species number instead of an empty box.
        _summaryFallback=new TextBlock{FontSize=20,FontWeight=FontWeights.SemiBold,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};_summaryFallback.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        var spriteHost=new Grid();spriteHost.Children.Add(_summaryFallback);spriteHost.Children.Add(_summarySprite);frame.Child=spriteHost;grid.Children.Add(frame);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(text,1);grid.Children.Add(text);
        _summaryTitle=new TextBlock{FontSize=24,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};text.Children.Add(_summaryTitle);
        _summarySubtitle=new TextBlock{FontSize=12,Margin=new Thickness(0,2,0,8)};_summarySubtitle.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");text.Children.Add(_summarySubtitle);
        _summaryBadges=new WrapPanel();text.Children.Add(_summaryBadges);
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,0)};text.Children.Add(row);
        var caption=new TextBlock{Text="BST",FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};caption.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");row.Children.Add(caption);
        _summaryTotal=new TextBlock{FontSize=18,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};_summaryTotal.SetResourceReference(TextBlock.ForegroundProperty,"Accent");row.Children.Add(_summaryTotal);
        var state=new Border{CornerRadius=new CornerRadius(6),Padding=new Thickness(10,3,10,3),VerticalAlignment=VerticalAlignment.Center};state.SetResourceReference(Border.BackgroundProperty,"Surface");
        _summaryState=new TextBlock{FontSize=12,FontWeight=FontWeights.SemiBold};state.Child=_summaryState;row.Children.Add(state);
        System.Windows.Automation.AutomationProperties.SetName(card,"Resumen de la especie");
        return card;
    }

    private Border BuildTotalCard()
    {
        var card=new Border{CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Padding=new Thickness(14,12,14,12),Margin=new Thickness(0,0,0,12)};
        card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        var stack=new StackPanel();card.Child=stack;
        var title=new TextBlock{Text="Total de estadísticas",FontSize=12};title.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");stack.Children.Add(title);
        _summaryInspectorTotal=new TextBlock{FontSize=32,FontWeight=FontWeights.SemiBold};_summaryInspectorTotal.SetResourceReference(TextBlock.ForegroundProperty,"Accent");stack.Children.Add(_summaryInspectorTotal);
        var caption=new TextBlock{Text="BST (Base Stat Total)",FontSize=12};caption.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");stack.Children.Add(caption);
        return card;
    }
}
