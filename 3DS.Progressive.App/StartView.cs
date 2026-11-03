using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using UserControl=System.Windows.Controls.UserControl;
namespace ThreeDS.Progressive;
internal sealed class StartView:UserControl
{
    internal StartView(GameProjectSession? project,bool simplified,Action<bool> chooseMode,Action open,Action guided,Action tour,Action? openSwitch=null,Action? openNds=null,
        Action<string>? navigate=null,Action? backup=null,Action? extract=null)
    {
        SetResourceReference(BackgroundProperty,"Bg");
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Content=scroll;
        var body=new StackPanel{Margin=new Thickness(30),MaxWidth=950,HorizontalAlignment=System.Windows.HorizontalAlignment.Center};scroll.Content=body;
        body.Children.Add(new TextBlock{Text="¿Cómo quieres trabajar?",FontSize=28,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        var intro=new TextBlock{Text=project is null?"Abre un proyecto extraído para editar o generar tu randomizado.":"Proyecto: "+System.IO.Path.GetFileName(project.ProjectRoot)+" · "+project.GameName,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,20)};intro.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");body.Children.Add(intro);
        if(project is not null)body.Children.Add(ProjectStatus(project,navigate,backup));
        else body.Children.Add(FirstSteps(open,extract));
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
            var nds=new Button{Content="Nintendo DS · Gen4/Gen5 (beta)",Margin=new Thickness(0,0,8,8)};
            System.Windows.Automation.AutomationProperties.SetName(nds,"Abrir el espacio de trabajo de Nintendo DS para Pokémon Diamante, Platino, HeartGold, Negro, Blanco y Blanco 2");
            nds.Click+=(_,_)=>openNds();actions.Children.Add(nds);
        }
        body.Children.Add(new TextBlock{Text="Ayuda breve y opcional · F1 abre la guía de la sección actual · Ctrl+K busca cualquier sección o herramienta.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});
        SizeChanged+=(_,_)=>cards.Columns=ActualWidth<670?1:2;
    }
    /// <summary>
    /// «Estado del proyecto»: what has been saved (read from the existing history), which sections it touched, and whether a backup
    /// or a Rebuild is still pending in this session. Read-only; each button only navigates or starts the existing action.
    /// </summary>
    private FrameworkElement ProjectStatus(GameProjectSession project,Action<string>? navigate,Action? backup)
    {
        int total=0;IReadOnlyDictionary<string,int> sections=new Dictionary<string,int>();bool readable=true;
        IReadOnlyDictionary<string,IReadOnlyList<SavedChange>> changes=new Dictionary<string,IReadOnlyList<SavedChange>>();
        try{var document=project.History.Read();total=document.Entries.Count;changes=SectionProgress.Changes(document);sections=changes.ToDictionary(p=>p.Key,p=>p.Value.Count);}
        catch(Exception ex) when(ex is System.IO.IOException or System.Text.Json.JsonException or System.IO.InvalidDataException or UnauthorizedAccessException){readable=false;}
        var milestones=SessionMilestones.For(project,total);
        var card=new Border{BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(18,16,18,14),Margin=new Thickness(0,0,12,20)};
        card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        System.Windows.Automation.AutomationProperties.SetName(card,"Estado del proyecto");
        var stack=new StackPanel();card.Child=stack;
        stack.Children.Add(new TextBlock{Text="Estado del proyecto",FontSize=17,FontWeight=FontWeights.SemiBold});
        string summary=!readable?"No se pudo leer el historial del proyecto; ábrelo desde Historial de cambios para revisarlo."
            :total==0?"Todavía no hay cambios guardados. Elige un modo o usa el Randomizado guiado para empezar."
            :$"{total} {(total==1?"cambio guardado":"cambios guardados")} en el historial"+(sections.Count>0?$", en {sections.Count} {(sections.Count==1?"sección":"secciones")}.":".");
        stack.Children.Add(Soft(summary,new Thickness(0,6,0,10)));
        if(sections.Count>0&&navigate is not null)
        {
            var chips=new WrapPanel{Margin=new Thickness(0,0,0,8)};
            foreach(var (route,count) in sections.OrderByDescending(p=>p.Value).ThenBy(p=>p.Key,StringComparer.Ordinal))
            {
                string label=GuidanceCatalog.Find(route) is {Route:var found} topic&&found==route?topic.Title:route;
                var chip=new Button{Content=$"{label} · {count}",Margin=new Thickness(0,0,8,8),MinHeight=30,Padding=new Thickness(10,4,10,4)};
                chip.SetResourceReference(StyleProperty,"SecondaryButton");
                System.Windows.Automation.AutomationProperties.SetName(chip,$"Ir a {label}: {count} {(count==1?"cambio guardado":"cambios guardados")}");
                chip.Click+=(_,_)=>navigate(route);chips.Children.Add(chip);
            }
            stack.Children.Add(chips);
            stack.Children.Add(ChangeDetails(changes));
        }
        var rows=new System.Windows.Controls.Grid{Margin=new Thickness(0,4,0,0)};
        rows.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});rows.ColumnDefinitions.Add(new ColumnDefinition());rows.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        void Row(string name,string state,bool pending,string? action,Action? run)
        {
            int row=rows.RowDefinitions.Count;rows.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var mark=new TextBlock{Text=pending?"●":"✓",Margin=new Thickness(0,0,10,6),VerticalAlignment=VerticalAlignment.Center};
            mark.SetResourceReference(TextBlock.ForegroundProperty,pending?"Accent":"TextSoft");
            var text=new TextBlock{TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,6)};
            text.Inlines.Add(new System.Windows.Documents.Run(name+": "){FontWeight=FontWeights.SemiBold});text.Inlines.Add(new System.Windows.Documents.Run(state));
            System.Windows.Controls.Grid.SetRow(mark,row);System.Windows.Controls.Grid.SetRow(text,row);System.Windows.Controls.Grid.SetColumn(text,1);
            rows.Children.Add(mark);rows.Children.Add(text);
            if(action is not null&&run is not null)
            {
                var button=new Button{Content=action,MinHeight=30,Padding=new Thickness(10,4,10,4),Margin=new Thickness(0,0,0,6)};
                button.SetResourceReference(StyleProperty,pending?"PrimaryButton":"SecondaryButton");
                button.Click+=(_,_)=>run();System.Windows.Controls.Grid.SetRow(button,row);System.Windows.Controls.Grid.SetColumn(button,2);rows.Children.Add(button);
            }
        }
        string Ago(DateTimeOffset time){var span=DateTimeOffset.Now-time;return span.TotalMinutes<1?"hace un momento":span.TotalHours<1?$"hace {(int)span.TotalMinutes} min":$"hace {(int)span.TotalHours} h";}
        string Changes(int n)=>n==1?"1 cambio":$"{n} cambios";
        Row("Backup",milestones.Backup is DateTimeOffset b?Ago(b)+(milestones.SinceBackup>0?$" · {Changes(milestones.SinceBackup)} después":" · al día"):"ninguno en esta sesión",
            total>0&&(milestones.Backup is null||milestones.SinceBackup>0),"Generar backup",backup);
        Row("ROM",milestones.Rebuild is DateTimeOffset r?"generada "+Ago(r)+(milestones.SinceRebuild>0?$" · {Changes(milestones.SinceRebuild)} sin incluir":" · al día")
            :total>0?"aún no se generó en esta sesión":"sin cambios que generar",
            total>0&&(milestones.Rebuild is null||milestones.SinceRebuild>0),"Rebuild",navigate is null?null:()=>navigate("Rebuild"));
        stack.Children.Add(rows);
        stack.Children.Add(Soft("El backup y el Rebuild se recuerdan solo durante esta sesión de la aplicación.",new Thickness(0,4,0,0),12));
        return card;
    }
    /// <summary>«Ver qué se cambió»: per section, the saved operations with what they did and when (newest first).</summary>
    private static Expander ChangeDetails(IReadOnlyDictionary<string,IReadOnlyList<SavedChange>> changes)
    {
        var list=new StackPanel{Margin=new Thickness(24,6,0,4)};
        foreach(var (route,items) in changes.OrderByDescending(p=>p.Value.Count).ThenBy(p=>p.Key,StringComparer.Ordinal))
        {
            string label=GuidanceCatalog.Find(route) is {Route:var found} topic&&found==route?topic.Title:route;
            list.Children.Add(new TextBlock{Text=$"{label} · {items.Count}",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,4)});
            foreach(var change in items.Take(6))
            {
                var row=new DockPanel{Margin=new Thickness(10,0,0,4)};
                var when=Soft(change.When,new Thickness(12,0,0,0),12);DockPanel.SetDock(when,Dock.Right);row.Children.Add(when);
                var text=new TextBlock{TextWrapping=TextWrapping.Wrap};
                text.Inlines.Add(new System.Windows.Documents.Run("• "+change.Title));
                if(change.Detail.Length>0){var detail=new System.Windows.Documents.Run(" · "+change.Detail){FontSize=12};detail.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"TextSoft");text.Inlines.Add(detail);}
                row.Children.Add(text);list.Children.Add(row);
            }
            if(items.Count>6)list.Children.Add(Soft($"… y {items.Count-6} más en el historial",new Thickness(10,0,0,4),12));
        }
        return new Expander{Header="Ver qué se cambió",IsExpanded=false,Content=list,Margin=new Thickness(-6,0,0,8)};
    }
    /// <summary>«Primeros pasos»: the whole route from a ROM to a randomized ROM, for the first time the app is opened.</summary>
    private FrameworkElement FirstSteps(Action open,Action? extract)
    {
        var card=UiKit.Card("PRIMEROS PASOS",out var body,new Thickness(0,0,12,20));
        System.Windows.Automation.AutomationProperties.SetName(card,"Primeros pasos");
        (string Title,string Text,string? Action,Action? Run)[] steps=
        [
            ("Extrae tu ROM","Convierte el .3ds o .cxi en una carpeta de proyecto. El archivo original no cambia.","Extraer ROM…",extract),
            ("Abre el proyecto","Elige la carpeta extraída (la que contiene RomFS y ExeFS).","Abrir proyecto…",open),
            ("Randomiza","Con el Randomizado guiado eliges las secciones y revisas todo antes de aplicar; o entra en cada sección.",null,null),
            ("Genera la ROM","Rebuild crea la ROM jugable con todos los cambios guardados. Antes, haz un backup.",null,null),
        ];
        for(int i=0;i<steps.Length;i++)
        {
            var row=new System.Windows.Controls.Grid{Margin=new Thickness(0,0,0,10)};
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(34)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var number=new Border{Width=24,Height=24,CornerRadius=new CornerRadius(12),VerticalAlignment=VerticalAlignment.Top,
                Child=new TextBlock{Text=(i+1).ToString(),FontWeight=FontWeights.Bold,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            number.SetResourceReference(Border.BackgroundProperty,i<2?"Accent":"Surface");((TextBlock)number.Child).SetResourceReference(TextBlock.ForegroundProperty,i<2?"Bg":"TextSoft");
            var text=new StackPanel();text.Children.Add(new TextBlock{Text=steps[i].Title,FontWeight=FontWeights.SemiBold});text.Children.Add(UiKit.Small(steps[i].Text,true,12,wrap:true));
            System.Windows.Controls.Grid.SetColumn(text,1);row.Children.Add(number);row.Children.Add(text);
            if(steps[i].Action is string label&&steps[i].Run is Action run)
            {
                var button=new Button{Content=label,MinHeight=30,Padding=new Thickness(10,4,10,4),Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Top};
                button.SetResourceReference(StyleProperty,i==0?"SecondaryButton":"PrimaryButton");button.Click+=(_,_)=>run();
                System.Windows.Controls.Grid.SetColumn(button,2);row.Children.Add(button);
            }
            body.Children.Add(row);
        }
        body.Children.Add(Soft("¿Ya tienes una carpeta extraída? Empieza por el paso 2. Ctrl+K busca cualquier sección cuando el proyecto esté abierto.",new Thickness(0,2,0,0),12));
        return card;
    }
    private static TextBlock Soft(string text,Thickness margin,double size=13)
    {
        var block=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=margin,FontSize=size};
        block.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");return block;
    }
}
