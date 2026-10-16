using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive.Services;
using UserControl=System.Windows.Controls.UserControl;
using Orientation=System.Windows.Controls.Orientation;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;

namespace ThreeDS.Progressive;

internal sealed class Gen6MachineView : UserControl
{
    internal RecordEditorView Editor {get;}
    private CheckBox _sanity=null!,_follow=null!;
    internal MachineCompatibilityState RememberCompatibility()=>new(_sanity.IsChecked==true,_follow.IsChecked==true);
    internal Gen6MachineView(GameProjectSession project,RecordEditorView editor,MachineCompatibilityState? state=null)
    {
        Editor=editor;
        var layout=new DockPanel();var bar=new StackPanel{Margin=new Thickness(8)};
        var flags=new StackPanel{Orientation=Orientation.Horizontal};
        var sanity=new CheckBox{Content="MT/MO Sanity · movimientos aprendidos por nivel",Margin=new Thickness(0,0,18,0)};
        var follow=new CheckBox{Content="Follow Evolutions · heredar compatibilidad"};
        _sanity=sanity;_follow=follow;sanity.IsChecked=state?.Sanity??false;follow.IsChecked=state?.Follow??false;
        flags.Children.Add(sanity);flags.Children.Add(follow);bar.Children.Add(flags);
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};
        var preview=new Button{Content="Vista previa de compatibilidad",Padding=new Thickness(10,6,10,6)};
        var apply=new Button{Content="Aplicar y verificar",IsEnabled=false,Margin=new Thickness(8,0,0,0),Padding=new Thickness(10,6,10,6)};
        var status=new TextBlock{Text="Solo se añaden compatibilidades de MT/MO; se conservan los tutores y los demás datos de Personal.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};
        controls.Children.Add(preview);controls.Children.Add(apply);bar.Children.Add(controls);bar.Children.Add(status);
        GameTmCompatibilityService? service=null;TmCompatibilityPreview? proposal=null;
        void Invalidate(){proposal=null;service=null;apply.IsEnabled=false;}
        sanity.Checked+=(_,_)=>Invalidate();sanity.Unchecked+=(_,_)=>Invalidate();follow.Checked+=(_,_)=>Invalidate();follow.Unchecked+=(_,_)=>Invalidate();
        preview.Click+=(_,_)=>
        {
            Invalidate();try
            {
                service=new GameTmCompatibilityService(project);proposal=service.Preview(sanity.IsChecked==true,follow.IsChecked==true);
                status.Text=$"Sanity: +{proposal.AddedSanityFlags} · Evoluciones: +{proposal.AddedInheritedFlags} · Pokémon afectados: {proposal.Changes.Count}";
                apply.IsEnabled=proposal.Changes.Count>0;
            }
            catch(Exception ex){status.Text=ex.Message;}
        };
        apply.Click+=(_,_)=>
        {
            if(service is null||proposal is null)return;
            try{service.Apply(proposal);Invalidate();status.Text="Compatibilidad MT/MO guardada y verificada en Personal.";}
            catch(Exception ex){Invalidate();status.Text=ex.Message;}
        };
        var editorRoot=(Grid)Editor.Content;
        var heading=editorRoot.Children.OfType<Border>().First(b=>Grid.GetRow(b)==0);
        var title=heading.Child;heading.Child=null;var header=new StackPanel();header.Children.Add(title);header.Children.Add(bar);heading.Child=header;
        Content=Editor;
    }
}
