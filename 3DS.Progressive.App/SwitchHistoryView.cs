using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ThreeDS.Progressive.Services.Switch;
using Button=System.Windows.Controls.Button;
using ListBox=System.Windows.Controls.ListBox;
using TextBlock=System.Windows.Controls.TextBlock;
using UserControl=System.Windows.Controls.UserControl;
namespace ThreeDS.Progressive;
internal sealed record SwitchHistoryGroup(string Title,int Count,Func<int,string> Line)
{
    public override string ToString()=>Title+" · "+Count+" registros";
}
internal sealed class SwitchHistoryView:UserControl
{
    internal const int PageSize=200;
    private readonly ListBox _groups=new(){MaxHeight=180},_details=new();private readonly TextBlock _position=new();private readonly Button _previous=new(){Content="Anterior"},_next=new(){Content="Siguiente"};private int _page;
    internal IReadOnlyList<SwitchHistoryGroup> Groups {get;}
    internal IReadOnlyList<string> VisibleLines {get;private set;}=Array.Empty<string>();
    internal int Page=>_page;
    internal SwitchHistoryView(SwordShieldProject project):this(BuildGroups(project)){}
    internal SwitchHistoryView(IReadOnlyList<SwitchHistoryGroup> groups)
    {
        Groups=groups;SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"TextMain");var root=new DockPanel{Margin=new Thickness(20)};Content=root;var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);top.Children.Add(new TextBlock{Text="Historial de cambios",FontSize=23,FontWeight=FontWeights.SemiBold});top.Children.Add(new TextBlock{Text="Selecciona una operación. El detalle se muestra por páginas; exportar conserva todos los registros.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)});top.Children.Add(_groups);
        var bottom=new WrapPanel{Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);foreach(var b in new[]{_previous,_next}){EditorPresentation.StyleAction(b,EditorPresentation.ActionRole.Secondary);b.Margin=new Thickness(0,0,8,0);bottom.Children.Add(b);}bottom.Children.Add(_position);var export=new Button{Content="Exportar historial…",Margin=new Thickness(12,0,0,0)};EditorPresentation.StyleAction(export,EditorPresentation.ActionRole.Secondary);bottom.Children.Add(export);export.Click+=async(_,_)=>{var picker=new Microsoft.Win32.SaveFileDialog{Filter="Historial (*.txt)|*.txt",FileName="historial-switch.txt"};if(picker.ShowDialog(Window.GetWindow(this))!=true)return;export.IsEnabled=false;try{string path=picker.FileName;await Task.Run(()=>{using var writer=new StreamWriter(path);Export(writer);});}catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Exportar historial",ex.Message);}finally{export.IsEnabled=true;}};
        foreach(var list in new[]{_groups,_details}){VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(list,true);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Auto);ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto);}var compact=new Style(typeof(ListBoxItem),TryFindResource(typeof(ListBoxItem)) as Style);compact.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty,new Thickness(5,2,5,2)));compact.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,24.0));_details.ItemContainerStyle=compact;AutomationProperties.SetName(_groups,"Operaciones del historial");AutomationProperties.SetName(_details,"Detalle del historial · página actual");root.Children.Add(_details);_groups.ItemsSource=Groups;_groups.SelectionChanged+=(_,_)=>{_page=0;RefreshPage();};_previous.Click+=(_,_)=>SetPage(_page-1);_next.Click+=(_,_)=>SetPage(_page+1);if(Groups.Count>0)_groups.SelectedIndex=0;else RefreshPage();
    }
    internal static IReadOnlyList<SwitchHistoryGroup> BuildGroups(SwordShieldProject project)
    {
        var groups=new List<SwitchHistoryGroup>();int n=0;
        foreach(var operation in project.DataHistory){var h=operation;groups.Add(new(++n+" · "+SwitchDataEditorView.TitleFor(h.Section)+(h.Seed is uint seed?" · semilla "+seed:" · edición manual"),h.Changes.Count,i=>{var c=h.Changes[i];return c.Location+" · "+c.Field+": "+c.Before+" → "+c.After;}));}
        foreach(var operation in project.GameplayHistory){var h=operation;var warnings=h.Warnings??Array.Empty<string>();groups.Add(new(++n+" · Entrenadores/encuentros/Field Items · semilla "+h.Options.Seed,h.Changes.Count+warnings.Count,i=>{if(i>=h.Changes.Count)return "Aviso · "+warnings[i-h.Changes.Count];var c=h.Changes[i];return c.Location+" · "+c.Field+": "+c.Before+" → "+c.After;}));}
        if(project.History.Count>0){var rows=project.History.ToArray();groups.Add(new(++n+" · Pokémon",rows.Length,i=>{var c=rows[i];return "Entrada "+c.Entry+" · "+c.Field+": "+c.Before+" → "+c.After;}));}
        foreach(var operation in project.Randomizations){var h=operation;groups.Add(new(++n+" · Randomización Pokémon · semilla "+h.Options.Seed,1,_=>$"{h.ChangedValues} valores · estadísticas: {h.Options.Stats} · tipos: {h.Options.Types} · habilidades: {h.Options.Abilities}"));}
        foreach(var operation in project.PokeFeurHistory){var h=operation;groups.Add(new(++n+" · Runtime · semilla "+h.Options.Seed,h.Changes.Count,i=>{var c=h.Changes[i];return c.Parameter+": "+c.Before+" → "+c.After;}));}
        foreach(var operation in project.SurpriseHistory){var h=operation;groups.Add(new(++n+" · Combate sorpresa",1,_=>"Semilla "+h.Seed+" · "+h.Pool.Count+" especies"));}return groups.AsReadOnly();
    }
    internal void SelectGroup(int index)=>_groups.SelectedIndex=index;
    internal void SetPage(int page){int count=(_groups.SelectedItem as SwitchHistoryGroup)?.Count??0;_page=Math.Clamp(page,0,Math.Max(0,(count-1)/PageSize));RefreshPage();}
    private void RefreshPage(){var group=_groups.SelectedItem as SwitchHistoryGroup;int count=group?.Count??0;VisibleLines=group is null?Array.Empty<string>():Enumerable.Range(_page*PageSize,Math.Min(PageSize,Math.Max(0,count-_page*PageSize))).Select(group.Line).ToArray();_details.ItemsSource=VisibleLines;_position.Text=count==0?"Sin registros":"Página "+(_page+1)+" de "+((count+PageSize-1)/PageSize)+" · "+count+" registros";_previous.IsEnabled=_page>0;_next.IsEnabled=(_page+1)*PageSize<count;}
    internal void Export(TextWriter writer){foreach(var group in Groups){writer.WriteLine(group.Title);for(int i=0;i<group.Count;i++)writer.WriteLine(group.Line(i));writer.WriteLine();}}
}
