using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive.Models;
using Button=System.Windows.Controls.Button;
namespace ThreeDS.Progressive;

// Field Items overview: the whole game at a glance (visible / hidden pickups, TMs, key items, most repeated items),
// the other pickups of the same area and the other places where the selected item is. Read-only and navigable:
// the item itself is still edited with the original field and saved with «Guardar cambios».
public partial class RecordEditorView
{
    internal Border? FieldOverviewCard {get;private set;}
    internal Border? FieldContextCard {get;private set;}
    private Border Card(string title,out StackPanel body)
    {
        body=new StackPanel();
        var heading=new TextBlock{Text=title,FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)};
        heading.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");body.Children.Add(heading);
        var card=new Border{CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Padding=new Thickness(16,14,16,10),Margin=new Thickness(0,0,0,14),Child=body};
        card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        return card;
    }
    private Button LinkChip(string text,Action run,bool current=false)
    {
        var chip=new Button{Content=text,MinHeight=28,Padding=new Thickness(10,3,10,3),Margin=new Thickness(0,0,6,6)};
        EditorPresentation.StyleAction(chip,current?EditorPresentation.ActionRole.Primary:EditorPresentation.ActionRole.Secondary);
        chip.Click+=(_,_)=>run();return chip;
    }
    private void SelectRecord(int id)
    {
        if(Editor.Entries.FirstOrDefault(e=>e.Id==id) is not RecordEntry entry)return;
        if(!RecordList.Items.Contains(entry))SearchText.Text="";
        RecordList.SelectedItem=entry;RecordList.ScrollIntoView(entry);
    }
    private static string Kind(FieldItemInfo info)=>info.Hidden?"oculto":"visible";

    private void UpdateFieldItemOverview(int id)
    {
        FieldOverviewCard=FieldContextCard=null;
        if(Editor is not IFieldItemPresentation presentation)return;
        var all=Editor.Entries.Select(e=>presentation.Info(e.Id)).ToArray();
        var current=all.FirstOrDefault(i=>i.Id==id);
        if(current is null)return;
        // Whole game.
        FieldOverviewCard=Card("FIELD ITEMS DEL JUEGO",out var overview);
        var chips=new WrapPanel();overview.Children.Add(chips);
        chips.Children.Add(Chip($"{all.Length} ubicaciones"));
        chips.Children.Add(Chip($"{all.Count(i=>!i.Hidden)} visibles"));
        chips.Children.Add(Chip($"{all.Count(i=>i.Hidden)} ocultos"));
        chips.Children.Add(Chip($"{all.Count(i=>i.Machine)} MT"));
        chips.Children.Add(Chip($"{all.Count(i=>i.Key)} objetos clave"));
        chips.Children.Add(Chip($"{all.Select(i=>i.Item).Distinct().Count()} objetos distintos"));
        var top=all.GroupBy(i=>i.Item).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key).Take(8).ToArray();
        if(top.Length>0)
        {
            overview.Children.Add(Small("Más repetidos (pulsa uno para filtrar la lista):",true,12));
            var repeated=new WrapPanel{Margin=new Thickness(0,6,0,0)};overview.Children.Add(repeated);
            foreach(var group in top){string name=group.First().ItemName;repeated.Children.Add(LinkChip($"{name} ×{group.Count()}",()=>SearchText.Text=name));}
        }
        FieldsPanel.Children.Insert(0,FieldOverviewCard);
        // The selected pickup in context.
        FieldContextCard=Card($"ÁREA {current.Area:000} · OBJETO {Kind(current).ToUpperInvariant()}",out var context);
        var area=all.Where(i=>i.Area==current.Area).ToArray();
        context.Children.Add(Small(area.Length==1?"Es el único objeto recogible de esta área.":$"En esta área hay {area.Length} objetos recogibles ({area.Count(i=>!i.Hidden)} visibles, {area.Count(i=>i.Hidden)} ocultos):",true,12));
        var siblings=new WrapPanel{Margin=new Thickness(0,6,0,6)};context.Children.Add(siblings);
        foreach(var info in area.Take(40)){var target=info.Id;siblings.Children.Add(LinkChip($"{info.ItemName} · {Kind(info)}",()=>SelectRecord(target),info.Id==id));}
        var same=all.Where(i=>i.Item==current.Item).ToArray();
        context.Children.Add(Small(same.Length==1?$"{current.ItemName} solo aparece aquí.":$"{current.ItemName} aparece en {same.Length} ubicaciones ({same.Count(i=>!i.Hidden)} visibles, {same.Count(i=>i.Hidden)} ocultas):",true,12));
        var others=new WrapPanel{Margin=new Thickness(0,6,0,0)};context.Children.Add(others);
        foreach(var info in same.Where(i=>i.Id!=id).Take(24)){var target=info.Id;others.Children.Add(LinkChip($"Área {info.Area:000} · {Kind(info)}",()=>SelectRecord(target)));}
        if(same.Length>25)others.Children.Add(Small($"… y {same.Length-25} más"));
        if(current.Machine||current.Key)
            context.Children.Add(Small(current.Machine?"Es una MT: «Excluir MT» la deja en su sitio al randomizar.":"Es un objeto clave: «Excluir objetos clave» lo deja en su sitio al randomizar.",true,12));
        FieldsPanel.Children.Add(FieldContextCard);
    }
}
