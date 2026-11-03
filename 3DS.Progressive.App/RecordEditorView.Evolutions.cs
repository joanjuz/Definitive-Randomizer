using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ComboBox=System.Windows.Controls.ComboBox;
using Image=System.Windows.Controls.Image;
using Orientation=System.Windows.Controls.Orientation;
using TextBox=System.Windows.Controls.TextBox;
namespace ThreeDS.Progressive;

// Evolutions: the method argument is chosen from the catalog it refers to (item, move, species or type) instead of a
// raw 0–65535 number, and a summary card shows every route of the species with origin and destination sprites.
// The argument value and its key are the same; only the input control changes with the method.
public partial class RecordEditorView
{
    internal Border? EvolutionOverviewCard {get;private set;}
    private void PresentEvolutionArguments()
    {
        if(Editor is not IEvolutionArgumentCatalog catalog)return;
        for(int i=0;_fields.ContainsKey($"E{i}.Method");i++)
        {
            string prefix=$"E{i}.";
            if(_fields[prefix+"Method"] is ComboBox method)
            {
                method.SelectionChanged+=(_,_)=>{if(!_loading){ApplyArgumentKind(catalog,prefix);UpdateEvolutionOverview();}};
                ApplyArgumentKind(catalog,prefix);
            }
        }
        UpdateEvolutionOverview();
    }
    /// <summary>Swaps the argument input between a catalog dropdown and a number, keeping its value, key and handlers.</summary>
    private void ApplyArgumentKind(IEvolutionArgumentCatalog catalog,string prefix)
    {
        string key=prefix+"Argument";
        if(!_fields.TryGetValue(key,out var current)||current.Parent is not System.Windows.Controls.Panel cell)return;
        int value=LiveValue(key)??0;int method=LiveValue(prefix+"Method")??0;
        var choices=catalog.ArgumentChoices(method);
        FrameworkElement replacement;
        if(choices is not null)
        {
            string kind=catalog.ArgumentLabel(method)??"catálogo";
            if(current is ComboBox existing&&existing.Resources["ArgumentKind"] as string==kind)return;
            if(!choices.Any(c=>c.Id==value))choices=choices.Prepend(new MoveChoice(value,value==0?"000 · Ninguno":$"ID {value} (personalizado)")).ToArray();
            var combo=new ComboBox{ItemsSource=choices,DisplayMemberPath="Display",SelectedValuePath="Id",SelectedValue=value,MaxDropDownHeight=300};
            combo.Resources["ArgumentKind"]=kind;
            combo.SelectionChanged+=Input_Changed;replacement=combo;
        }
        else
        {
            if(current is TextBox)return;
            var box=new TextBox{Text=value.ToString(CultureInfo.InvariantCulture)};box.TextChanged+=Input_Changed;replacement=box;
        }
        replacement.Tag=true;replacement.Uid="1";
        System.Windows.Automation.AutomationProperties.SetName(replacement,catalog.ArgumentLabel(method)??"Parámetro del método");
        int index=cell.Children.IndexOf(current);
        bool loading=_loading;_loading=true;
        try{cell.Children.RemoveAt(index);cell.Children.Insert(index,replacement);_fields[key]=replacement;}
        finally{_loading=loading;}
        if(index>0&&cell.Children[index-1] is TextBlock caption)
        {
            caption.Text=choices is not null&&catalog.ArgumentLabel(method) is string label?label:"Parámetro del método · nivel / objeto / movimiento";
            System.Windows.Automation.AutomationProperties.SetLabeledBy(replacement,caption);
        }
        // The «0 – 65535» hint only makes sense for the number.
        if(index+1<cell.Children.Count&&cell.Children[index+1] is TextBlock range)range.Visibility=choices is null?Visibility.Visible:Visibility.Collapsed;
    }
    /// <summary>«Bulbasaur → Ivysaur · Subir de nivel · 16», one row per route of the species.</summary>
    private void UpdateEvolutionOverview()
    {
        if(EvolutionOverviewCard is not null)FieldsPanel.Children.Remove(EvolutionOverviewCard);
        EvolutionOverviewCard=null;
        if(Editor is not IEvolutionArgumentCatalog catalog||_id<0)return;
        string origin=Editor.Entries.FirstOrDefault(e=>e.Id==_id)?.Name??"";
        EvolutionOverviewCard=UiKit.Card("RUTAS DE EVOLUCIÓN",out var body);
        int routes=0;
        _trainerSpriteCancel??=new System.Threading.CancellationTokenSource();
        for(int i=0;_fields.ContainsKey($"E{i}.Method");i++)
        {
            string prefix=$"E{i}.";
            int method=LiveValue(prefix+"Method")??0;if(method==0)continue;routes++;
            int species=LiveValue(prefix+"Species")??0;
            var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,8)};
            row.Children.Add(Sprite(_id));
            row.Children.Add(new TextBlock{Text="→",FontSize=18,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,6,0)});
            row.Children.Add(Sprite(species));
            var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0)};
            text.Children.Add(new TextBlock{Text=$"{origin} → {LiveDisplay(prefix+"Species")??$"#{species}"}",FontWeight=FontWeights.SemiBold});
            string how=LiveDisplay(prefix+"Method")??$"Método {method}";
            string argument=catalog.ArgumentChoices(method) is not null?LiveDisplay(prefix+"Argument")??"":LiveValue(prefix+"Argument") is int value&&value>0?value.ToString(CultureInfo.InvariantCulture):"";
            if(LiveValue(prefix+"Level") is int level&&level>0)argument=(argument.Length>0?argument+" · ":"")+$"nivel {level}";
            text.Children.Add(UiKit.Small(argument.Length>0?$"{how} · {argument}":how,true,12));
            row.Children.Add(text);
            int target=i;
            var open=new System.Windows.Controls.Button{Content=row,HorizontalContentAlignment=System.Windows.HorizontalAlignment.Left,Padding=new Thickness(6),Margin=new Thickness(0,0,0,6)};
            EditorPresentation.StyleAction(open,EditorPresentation.ActionRole.Tertiary);
            open.Click+=(_,_)=>{if(_editorSections.TryGetValue($"E{target}.Method",out var section)){section.IsExpanded=true;section.BringIntoView();}};
            System.Windows.Automation.AutomationProperties.SetName(open,$"Evolución {i+1}: {origin} a {LiveDisplay(prefix+"Species")}");
            body.Children.Add(open);
        }
        if(routes==0)body.Children.Add(UiKit.Small($"{origin} no evoluciona.",true,12));
        FieldsPanel.Children.Insert(0,EvolutionOverviewCard);
    }
    private FrameworkElement Sprite(int species)
    {
        var picture=new Image{Width=40,Height=40,Stretch=Stretch.Uniform};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.NearestNeighbor);
        var fallback=new TextBlock{Text="#"+species.ToString("000",CultureInfo.InvariantCulture),FontSize=11,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        fallback.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        var host=new Grid{Width=44,Height=44};host.Children.Add(fallback);host.Children.Add(picture);
        if(species>0&&_trainerSpriteCancel is not null)_=LoadTeamSpriteAsync(picture,fallback,_id,species,0,_trainerSpriteCancel.Token);
        return host;
    }
}
