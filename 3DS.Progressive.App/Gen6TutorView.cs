using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive.Services;
using UserControl=System.Windows.Controls.UserControl;
using Orientation=System.Windows.Controls.Orientation;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;

namespace ThreeDS.Progressive;

internal sealed class Gen6TutorView : UserControl
{
    internal RecordEditorView Editor {get;}
    private CheckBox _sanity=null!,_follow=null!;
    internal TutorCompatibilityState RememberCompatibility()=>new(_sanity.IsChecked==true,_follow.IsChecked==true);
    internal Gen6TutorView(GameProjectSession project,RecordEditorView editor,TutorCompatibilityState? state=null)
    {
        Editor=editor;
        var layout=new DockPanel();var bar=new StackPanel{Margin=new Thickness(8)};
        var flags=new StackPanel{Orientation=Orientation.Horizontal};
        var sanity=new CheckBox{Content="Tutor Sanity ORAS · movimientos aprendidos por nivel",Margin=new Thickness(0,0,18,0)};
        var follow=new CheckBox{Content="Follow Evolutions · heredar compatibilidad"};
        _sanity=sanity;_follow=follow;sanity.IsChecked=state?.Sanity??false;follow.IsChecked=state?.Follow??false;
        flags.Children.Add(sanity);flags.Children.Add(follow);bar.Children.Add(flags);
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};
        var preview=new Button{Content="Vista previa de compatibilidad",Padding=new Thickness(10,6,10,6)};
        var apply=new Button{Content="Aplicar y verificar",IsEnabled=false,Margin=new Thickness(8,0,0,0),Padding=new Thickness(10,6,10,6)};
        var status=new TextBlock{Text="Solo se añaden compatibilidades de tutores BP; se conservan MT/MO y los demás datos de Personal.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};
        controls.Children.Add(preview);controls.Children.Add(apply);bar.Children.Add(controls);bar.Children.Add(status);
        GameTutor6CompatibilityService? service=null;Tutor6CompatibilityPreview? proposal=null;
        void Invalidate(){proposal=null;service=null;apply.IsEnabled=false;}
        sanity.Checked+=(_,_)=>Invalidate();sanity.Unchecked+=(_,_)=>Invalidate();follow.Checked+=(_,_)=>Invalidate();follow.Unchecked+=(_,_)=>Invalidate();
        preview.Click+=(_,_)=>
        {
            Invalidate();try
            {
                service=new GameTutor6CompatibilityService(project);proposal=service.Preview(sanity.IsChecked==true,follow.IsChecked==true);
                status.Text=$"Sanity: +{proposal.AddedSanity} · Evoluciones: +{proposal.AddedInherited} · Pokémon afectados: {proposal.Changes.Count}";
                apply.IsEnabled=proposal.Changes.Count>0;
            }
            catch(Exception ex){status.Text=ex.Message;}
        };
        apply.Click+=(_,_)=>
        {
            if(service is null||proposal is null)return;
            try{service.Apply(proposal);Invalidate();status.Text="Compatibilidad tutor ORAS guardada y verificada en Personal.";}
            catch(Exception ex){Invalidate();status.Text=ex.Message;}
        };
        DockPanel.SetDock(bar,Dock.Top);layout.Children.Add(bar);layout.Children.Add(Editor);Content=layout;
    }
}
