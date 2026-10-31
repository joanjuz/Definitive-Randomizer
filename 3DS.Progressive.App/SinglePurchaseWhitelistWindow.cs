using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Orientation=System.Windows.Controls.Orientation;
using Window=System.Windows.Window;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ListBox=System.Windows.Controls.ListBox;
using CheckBox=System.Windows.Controls.CheckBox;

namespace ThreeDS.Progressive;

/// <summary>White list of the USUM single purchase: items that special shops keep selling without limit.</summary>
internal sealed class SinglePurchaseWhitelistWindow : Window
{
    private readonly HashSet<ushort> _selected;
    internal IReadOnlyList<ushort> Selected=>_selected.Order().ToArray();

    internal SinglePurchaseWhitelistWindow(IReadOnlyList<MoveChoice> items,IReadOnlyList<ushort> selected)
    {
        _selected=[..selected];
        Title="Lista blanca de compra única";MaxWidth=SystemParameters.WorkArea.Width-24;MaxHeight=SystemParameters.WorkArea.Height-24;Width=Math.Min(560,MaxWidth);Height=Math.Min(640,MaxHeight);MinWidth=Math.Min(380,Width);MinHeight=Math.Min(320,Height);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResizeWithGrip;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Background=(Brush)FindResource("Bg");Foreground=(Brush)FindResource("TextMain");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var title=new TextBlock{Text=Title,FontSize=22,FontWeight=FontWeights.SemiBold};
        title.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==1)DragMove();};heading.Children.Add(title);
        heading.Children.Add(new TextBlock{Text="Los objetos marcados se venden sin límite en las tiendas especiales aunque esté activa la compra única. Un objeto que ya estaba registrado conserva su posición en la partida.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,8,0,8)});
        var search=new TextBox{ToolTip="Buscar objeto por nombre o número",Margin=new Thickness(0,0,0,8)};heading.Children.Add(search);
        var onlySelected=new CheckBox{Content="Mostrar solo los marcados",Margin=new Thickness(0,0,0,10)};heading.Children.Add(onlySelected);
        var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var count=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),VerticalAlignment=VerticalAlignment.Center};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(buttons,Dock.Right);footer.Children.Add(buttons);footer.Children.Add(count);
        var accept=new Button{Content="Aceptar",IsDefault=true,Margin=new Thickness(0,0,8,0)};accept.Click+=(_,_)=>{DialogResult=true;Close();};buttons.Children.Add(accept);
        var cancel=new Button{Content="Cancelar",IsCancel=true};buttons.Children.Add(cancel);
        EditorPresentation.StyleAction(accept,EditorPresentation.ActionRole.Primary);EditorPresentation.StyleAction(cancel,EditorPresentation.ActionRole.Tertiary);
        var list=new ListBox{Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain")};root.Children.Add(list);
        System.Windows.Automation.AutomationProperties.SetName(search,"Buscar objeto");
        System.Windows.Automation.AutomationProperties.SetName(list,"Objetos de la lista blanca");
        var choices=items.Where(i=>i.Id>0&&!string.IsNullOrWhiteSpace(i.Name)).ToArray();
        void Count()=>count.Text=_selected.Count==0?"Ningún objeto en la lista blanca.":$"{_selected.Count} objeto(s) sin límite.";
        void Filter()
        {
            string term=TextContentClassifier.Normalize(search.Text);
            list.Items.Clear();
            foreach(var item in choices.Where(i=>(onlySelected.IsChecked!=true||_selected.Contains((ushort)i.Id))&&(term.Length==0||TextContentClassifier.Normalize(i.Name).Contains(term,StringComparison.Ordinal)||i.Id.ToString()==search.Text.Trim())))
            {
                var id=(ushort)item.Id;
                var box=new CheckBox{Content=$"{item.Name} · #{item.Id}",IsChecked=_selected.Contains(id),Foreground=(Brush)FindResource("TextMain")};
                box.Checked+=(_,_)=>{_selected.Add(id);Count();};box.Unchecked+=(_,_)=>{_selected.Remove(id);Count();};
                list.Items.Add(box);
            }
        }
        search.TextChanged+=(_,_)=>Filter();onlySelected.Checked+=(_,_)=>Filter();onlySelected.Unchecked+=(_,_)=>Filter();
        Filter();Count();
    }
}
