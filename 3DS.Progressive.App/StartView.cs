using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using UserControl=System.Windows.Controls.UserControl;
namespace ThreeDS.Progressive;
internal sealed class StartView:UserControl
{
    internal StartView(GameProjectSession? project,bool simplified,Action<bool> chooseMode,Action open,Action guided,Action tour,Action? openSwitch=null,Action? openNds=null)
    {
        SetResourceReference(BackgroundProperty,"Bg");
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Content=scroll;
        var body=new StackPanel{Margin=new Thickness(30),MaxWidth=950,HorizontalAlignment=System.Windows.HorizontalAlignment.Center};scroll.Content=body;
        body.Children.Add(new TextBlock{Text="¿Cómo quieres trabajar?",FontSize=28,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        var intro=new TextBlock{Text=project is null?"Abrí un proyecto extraído para editar o generar tu randomizado.":"Proyecto: "+System.IO.Path.GetFileName(project.ProjectRoot)+" · "+project.GameName,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,20)};intro.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");body.Children.Add(intro);
        var cards=new UniformGrid{Columns=2};body.Children.Add(cards);
        Button Card(string title,string description,bool simple)
        {
            var text=new StackPanel();text.Children.Add(new TextBlock{Text=title,FontSize=21,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
            text.Children.Add(new TextBlock{Text=description,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
            var button=new Button{Content=text,MinHeight=145,Padding=new Thickness(22),Margin=new Thickness(0,0,12,12),HorizontalContentAlignment=System.Windows.HorizontalAlignment.Stretch,Style=(Style)FindResource(simple==simplified?"PrimaryButton":"SecondaryButton")};
            System.Windows.Automation.AutomationProperties.SetName(button,title);button.Click+=(_,_)=>chooseMode(simple);cards.Children.Add(button);return button;
        }
        Card("Modo simplificado","Las secciones principales del randomizer, con menos herramientas a la vista. Puedes cambiar de modo cuando quieras.",true);
        Card("Modo completo","Todos los editores, templates, herramientas de ROM y opciones avanzadas disponibles para tu juego.",false);
        var wizard=new Button{Content="Randomizado guiado · paso a paso",MinHeight=42,Margin=new Thickness(0,3,12,16),IsEnabled=project is not null};wizard.Click+=(_,_)=>guided();body.Children.Add(wizard);
        body.Children.Add(new TextBlock{Text="Elige las secciones, configura sus opciones y revisa todo antes de generar la ROM. Avanzar o volver no aplica cambios.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,12,18)});
        var actions=new WrapPanel();body.Children.Add(actions);
        var load=new Button{Content=project is null?"Abrir proyecto…":"Abrir otro proyecto…",Margin=new Thickness(0,0,8,8)};load.Click+=(_,_)=>open();actions.Children.Add(load);
        var help=new Button{Content="Vista guiada · conocer la aplicación",Margin=new Thickness(0,0,8,8)};help.Click+=(_,_)=>tour();actions.Children.Add(help);
        if(openSwitch is not null)
        {
            var swsh=new Button{Content="Espada/Escudo · editor Switch",Margin=new Thickness(0,0,8,8)};
            System.Windows.Automation.AutomationProperties.SetName(swsh,"Abrir editor de Espada/Escudo para exportar mods");
            swsh.Click+=(_,_)=>openSwitch();actions.Children.Add(swsh);
        }
        if(openNds is not null)
        {
            var nds=new Button{Content="Negro/Blanco · randomizer DS (beta)",Margin=new Thickness(0,0,8,8)};
            System.Windows.Automation.AutomationProperties.SetName(nds,"Abrir el randomizer de Nintendo DS para Pokémon Negro y Blanco");
            nds.Click+=(_,_)=>openNds();actions.Children.Add(nds);
        }
        body.Children.Add(new TextBlock{Text="Ayuda breve y opcional · F1 abre la guía de la sección actual.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});
        SizeChanged+=(_,_)=>cards.Columns=ActualWidth<670?1:2;
    }
}
