using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Brushes=System.Windows.Media.Brushes;
using Button=System.Windows.Controls.Button;
using Color=System.Windows.Media.Color;
namespace ThreeDS.Progressive;

// Presentation of the navigation only: which sidebar entry is active, the breadcrumb of hub tools, the groups,
// the "modified" marks and the search palette. Every destination still opens through the original handlers.
public partial class MainWindow
{
    private static readonly Brush ActiveNavigationFill=Freeze(new SolidColorBrush(Color.FromRgb(0x2B,0x40,0x59)));
    private static Brush Freeze(Brush brush){brush.Freeze();return brush;}
    private Button[] SidebarButtons=>[PokemonNavButton,MovesButton,LevelUpButton,MachineTutorButton,EvolutionsButton,PokemonToolsButton,
        TrainersButton,EncountersButton,StaticEncountersButton,FieldItemsButton,MartsButton,ItemsButton,GameTextButton,StoryTextButton,
        MechanicsButton,RomToolsButton,TemplateStudioButton,GlobalTemplateButton,BatchRomButton,HistoryButton,AutomationButton,ConfigurationButton];
    private (ToggleButton Header,System.Windows.Controls.Panel Group)[] NavigationGroupPairs=>
        [(NavHeaderSpecies,NavGroupSpecies),(NavHeaderWorld,NavGroupWorld),(NavHeaderText,NavGroupText),(NavHeaderPatches,NavGroupPatches),(NavHeaderRecipes,NavGroupRecipes)];
    /// <summary>Tools opened from a hub card, and the hub that contains them (from the single SectionCatalog).</summary>
    internal static IReadOnlyDictionary<string,string> HubOf=>SectionCatalog.HubOf;
    private string? _breadcrumbHub;
    private Button? SidebarButtonFor(string route)
    {
        if(route=="Pokemon")return PokemonNavButton;
        if(route=="Moves")return MovesButton;
        var direct=SidebarButtons.FirstOrDefault(b=>b.Tag as string==route);
        if(direct is not null)return direct;
        return HubOf.TryGetValue(route,out var hub)?SidebarButtons.FirstOrDefault(b=>b.Tag as string==hub):null;
    }
    /// <summary>Marks the sidebar entry of the open section (or of the hub that contains the tool) and sets the breadcrumb.</summary>
    private void HighlightNavigation(string route)
    {
        var active=SidebarButtonFor(route);
        foreach(var button in SidebarButtons)button.Background=ReferenceEquals(button,active)?ActiveNavigationFill:Brushes.Transparent;
        foreach(var (header,group) in NavigationGroupPairs)
            if(active is not null&&group.Children.Contains(active)&&header.IsChecked!=true)header.IsChecked=true;
        if(_session is not null&&HubOf.TryGetValue(route,out var hub))
        {
            _breadcrumbHub=hub;SectionHost.Tag=SectionCatalog.Label(hub);
            Breadcrumbs.SetCurrent(SectionHost,GuidanceCatalog.Find(route) is {Route:var found} topic&&found==route?ToolTitle(route,topic.Title):route);
        }
        else{_breadcrumbHub=null;SectionHost.Tag=null;Breadcrumbs.SetCurrent(SectionHost,null);}
        ShowSectionChanges(route);
    }
    private string ToolTitle(string route,string fallback)=>route switch
    {
        "FacilityNormal"=>_session?.Generation==6?"Battle Maison":"Battle Tree",
        "FacilityAlternate"=>_session?.Generation==6?"Super Maison":"Battle Royal",
        _=>fallback
    };
    private void Breadcrumb_Click(object sender,RoutedEventArgs e)
    {
        if(_breadcrumbHub is not string hub||_session is null||UiProgress.IsInteractionBlocked)return;
        SidebarButtons.FirstOrDefault(b=>b.Tag as string==hub)?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    }
    /// <summary>Hides a group heading when the current mode shows none of its entries.</summary>
    private void UpdateNavigationGroups()
    {
        foreach(var (header,group) in NavigationGroupPairs)
            header.Visibility=group.Children.OfType<UIElement>().Any(c=>c.Visibility==Visibility.Visible)?Visibility.Visible:Visibility.Collapsed;
    }
    /// <summary>Without a project, open or extract is the main action; with one, building the ROM is.</summary>
    private void RefreshHeaderEmphasis()
    {
        bool project=_session is not null;
        OpenProjectButton.SetResourceReference(StyleProperty,project?"SecondaryButton":"PrimaryButton");
        RebuildButton.SetResourceReference(StyleProperty,project?"PrimaryButton":"SecondaryButton");
        // The extraction stays in Herramientas ROM and in the search; the header keeps only what applies to the open project.
        if(SwitchHost.Visibility!=Visibility.Visible)ExtractRomButton.Visibility=project?Visibility.Collapsed:Visibility.Visible;
    }
    private GameProjectSession? _badgeSession;
    private void WatchHistory(GameProjectSession session)
    {
        if(_badgeSession is not null)_badgeSession.History.Committed-=OnHistoryCommitted;
        _badgeSession=session;session.History.Committed+=OnHistoryCommitted;
        RefreshNavigationBadges();
    }
    private void OnHistoryCommitted()
    {
        if(_closed||Dispatcher.HasShutdownStarted)return;
        Dispatcher.BeginInvoke(()=>{if(_closed)return;RefreshNavigationBadges();if(HomeHost.Visibility==Visibility.Visible&&_session is not null)RefreshStart();});
    }
    private IReadOnlyDictionary<string,IReadOnlyList<SavedChange>> _savedChanges=new Dictionary<string,IReadOnlyList<SavedChange>>();
    private string RouteOf(Button button)=>ReferenceEquals(button,PokemonNavButton)?"Pokemon":ReferenceEquals(button,MovesButton)?"Moves":button.Tag as string??"";
    private void RefreshNavigationBadges()
    {
        _savedChanges=SectionProgress.TryChanges(_session);
        foreach(var button in SidebarButtons)
        {
            var changes=_savedChanges.GetValueOrDefault(RouteOf(button))??[];
            NavBadge.SetCount(button,changes.Count);
            // The tooltip says what was changed, not only how many times.
            if(changes.Count>0)
            {
                string label=new NavigationLabelConverter().Convert(button.Content,typeof(string),null!,System.Globalization.CultureInfo.CurrentCulture).ToString()!;
                button.ToolTip=$"{label} · {changes.Count} {(changes.Count==1?"cambio guardado":"cambios guardados")}\n"+
                    string.Join("\n",changes.Take(5).Select(c=>$"• {c.Title}"+(c.Detail.Length>0?$" ({c.Detail})":"")))+(changes.Count>5?$"\n… y {changes.Count-5} más":"");
            }
        }
        if(_activeSection is string active)ShowSectionChanges(active);
    }
    /// <summary>«3 cambios guardados ▾» in the section bar, with the list of what was saved for this section.</summary>
    private void ShowSectionChanges(string route)
    {
        string owner=HubOf.TryGetValue(route,out var hub)?hub:route;
        var changes=_savedChanges.GetValueOrDefault(owner)??[];
        SectionChanges.SetItems(SectionHost,changes.Count>0?changes:null);
        SectionChanges.SetSummary(SectionHost,changes.Count==0?null:changes.Count==1?"1 cambio guardado":$"{changes.Count} cambios guardados");
    }
    private void SectionChangesHistory_Click(object sender,RoutedEventArgs e)
    {
        if(sender is FrameworkElement {TemplatedParent:ContentControl host}&&host.Template.FindName("ChangesToggle",host) is ToggleButton toggle)toggle.IsChecked=false;
        if(_session is not null&&!UiProgress.IsInteractionBlocked)HistoryButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    }
    private void OnMilestonesChanged()
    {
        if(_closed||Dispatcher.HasShutdownStarted)return;
        Dispatcher.BeginInvoke(()=>{if(!_closed&&HomeHost.Visibility==Visibility.Visible&&_session is not null)RefreshStart();});
    }
    private static readonly string[] StatBarLabels=["PS","Ataque","Defensa","At. Esp.","Def. Esp.","Velocidad"];
    /// <summary>Six bars on a 255 scale; the longest bar uses the accent colour.</summary>
    private void RenderStatBars(IReadOnlyList<byte> stats)
    {
        StatBarsPanel.Children.Clear();
        int best=stats.Max(v=>(int)v);
        for(int i=0;i<stats.Count;i++)
        {
            var row=new Grid{Margin=new Thickness(0,0,0,3)};
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(62)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(30)});row.ColumnDefinitions.Add(new ColumnDefinition());
            var name=UiKit.Small(StatBarLabels[i],true,11);var value=UiKit.Small(stats[i].ToString(),false,11);Grid.SetColumn(value,1);
            var track=new Border{Height=6,CornerRadius=new CornerRadius(3),VerticalAlignment=VerticalAlignment.Center};track.SetResourceReference(Border.BackgroundProperty,"Surface");
            var fill=new Border{Height=6,CornerRadius=new CornerRadius(3),HorizontalAlignment=System.Windows.HorizontalAlignment.Left};
            fill.SetResourceReference(Border.BackgroundProperty,stats[i]==best?"Accent":"TextSoft");
            var host=new Grid();host.Children.Add(track);host.Children.Add(fill);Grid.SetColumn(host,2);
            byte stat=stats[i];// copy: the handler runs after the loop has finished
            host.SizeChanged+=(_,e)=>fill.Width=Math.Max(2,e.NewSize.Width*stat/255.0);
            row.Children.Add(name);row.Children.Add(value);row.Children.Add(host);
            System.Windows.Automation.AutomationProperties.SetName(row,$"{StatBarLabels[i]}: {stats[i]}");
            StatBarsPanel.Children.Add(row);
        }
    }
    /// <summary>«Ratio de género» in words: 0 always male, 254 always female, 255 genderless, otherwise the share of females.</summary>
    internal static string GenderRatioMeaning(string text)
    {
        if(!int.TryParse(text,out int value)||value is <0 or >255)return "";
        return value switch
        {
            0=>"Siempre macho",254=>"Siempre hembra",255=>"Sin género",
            _=>$"{100-(value+1)*100.0/256:0.#} % macho · {(value+1)*100.0/256:0.#} % hembra"
        };
    }
    /// <summary>After «Deshacer» the project is read again, so every editor shows the restored files.</summary>
    private async Task ReloadAfterUndoAsync()
    {
        if(_session is null)return;string root=_session.ProjectRoot;
        var restored=await Task.Run(()=>GameProjectSession.Open(root,recoverInterruptedSave:false));LoadProject(restored);
        StatusText.Text="Cambio deshecho, verificado y registrado en el historial.";
    }
    /// <summary>«Deshacer el último cambio» (search palette): the newest change that still can be undone.</summary>
    internal async Task UndoLatestAsync()
    {
        if(_session is null||UiProgress.IsInteractionBlocked||!ConfirmDiscard()||!ConfirmAuxiliaryLeave())return;
        var latest=RestorePoints.LatestUndoable(_session.History,_session.History.ProjectRoot);
        if(latest is null){StudioDialog.Notice(this,"Deshacer","No hay ningún cambio reciente que se pueda deshacer.","Solo se guardan los "+RestorePoints.Keep+" últimos, y se pueden deshacer mientras ningún cambio posterior haya modificado los mismos archivos.");return;}
        string title=SectionProgress.Describe(latest).Title;
        if(!StudioDialog.Confirm(this,"Deshacer el último cambio",$"Los archivos volverán a como estaban antes de «{title}».","Queda registrado en el historial como una restauración."))return;
        try{RestorePoints.Undo(_session.History,_session.History.ProjectRoot,latest.Id);await ReloadAfterUndoAsync();}
        catch(Exception ex) when(ex is InvalidOperationException or System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
        {StudioDialog.Error(this,"No se pudo deshacer",ex.Message);}
    }
    private void CommandSearch_Click(object sender,RoutedEventArgs e)=>OpenCommandPalette();
    private CommandPalette? _palette;
    internal CommandPalette? Palette=>_palette;
    internal void OpenCommandPalette()
    {
        if(_closed||UiProgress.IsInteractionBlocked||_palette is not null)return;
        if(SwitchHost.Visibility==Visibility.Visible&&WorkspacePaletteEntries() is not {Count:>0})return;
        _tour?.Finish();
        var focus=Keyboard.FocusedElement as IInputElement;
        _palette=new CommandPalette(SwitchHost.Visibility==Visibility.Visible?WorkspacePaletteEntries()!:PaletteEntries(),()=>
        {
            PaletteHost.Children.Clear();PaletteHost.Visibility=Visibility.Collapsed;_palette=null;
            if(focus is UIElement {IsVisible:true} previous)previous.Focus();
        });
        PaletteHost.Children.Add(_palette);PaletteHost.Visibility=Visibility.Visible;
    }
    private string LocationOf(string route)
    {
        if(HubOf.TryGetValue(route,out var hub))return SectionCatalog.Label(hub);
        if(route=="Configuration")return "Opciones";
        var button=SidebarButtonFor(route);
        var pair=NavigationGroupPairs.FirstOrDefault(p=>button is not null&&p.Group.Children.Contains(button));
        return pair.Header?.Content as string??"Inicio";
    }
    /// <summary>Nintendo DS and Sword/Shield use their own sidebar, so the palette lists their sections.</summary>
    private IReadOnlyList<PaletteEntry>? WorkspacePaletteEntries()=>SwitchHost.Content switch
    {
        NdsWorkspaceView nds=>nds.PaletteEntries("Nintendo DS"),
        SwitchWorkspaceView swsh=>swsh.PaletteEntries("Espada/Escudo"),
        _=>null
    };
    /// <summary>Destinations for the open game: the same topics the guided tour knows, plus general actions.</summary>
    internal IReadOnlyList<PaletteEntry> PaletteEntries()
    {
        var entries=new List<PaletteEntry>();
        void Action(string title,string description,string keywords,Action run)=>entries.Add(new(title,"Acción",description,keywords,run));
        if(_session is null)
        {
            Action("Abrir proyecto…","Abre la carpeta de un juego extraído (RomFS + ExeFS).","abrir cargar carpeta",()=>OpenProject_Click(OpenProjectButton,new RoutedEventArgs()));
            entries.Add(new("Extraer ROM","Herramientas ROM","Extrae una ROM de 3DS en una carpeta nueva para editarla.",SectionCatalog.Keywords("Extraction"),()=>ExtractRom_Click(ExtractRomButton,new RoutedEventArgs())));
            Action("Restaurar backup…","Restaura un backup verificado en una carpeta nueva.","respaldo copia",()=>RestoreBackup_Click(RestoreBackupButton,new RoutedEventArgs()));
            Action("Espada/Escudo · editor Switch","Abre el editor de Espada/Escudo para exportar mods.","switch sword shield mod",OpenSwitchPrototype);
            Action("Nintendo DS · Gen4/Gen5 (beta)","Diamante, Platino, HeartGold, Negro, Blanco y Blanco 2.","ds nds negro blanco platino heartgold diamante",OpenNdsRandomizer);
            Action("Vista guiada","Recorrido por la aplicación; no aplica cambios.","ayuda tour f1",()=>OpenTour());
            return entries;
        }
        foreach(var topic in GuidanceCatalog.Available(_session))
        {
            if(topic.Route is "Home")continue;
            string route=topic.Route;
            string title=ToolTitle(route,topic.Title);
            entries.Add(new(title,LocationOf(route),topic.Steps[0].Text,SectionCatalog.Keywords(route),()=>NavigateTo(route)));
        }
        Action("Randomizado guiado · paso a paso","Elige secciones, configura opciones y revisa todo antes de generar la ROM.","asistente todo completo randomizar",OpenGuided);
        Action("Inicio","Modos, randomizado guiado y estado del proyecto.","home principal resumen",()=>Home_Click(HomeButton,new RoutedEventArgs()));
        Action("Vista guiada de esta sección","Explica la sección actual sin aplicar cambios (F1).","ayuda tour f1",()=>OpenTour());
        Action("Exportar registro del randomizado","Archivo de texto con regalos, entrenadores y encuentros actuales (spoiler).","spoiler log registro resumen exportar",()=>_=SystemsView.ExportLogAsync(this,_session,text=>StatusText.Text=text));
        Action("Deshacer el último cambio","Devuelve los archivos a como estaban antes del último cambio guardado.","deshacer revertir undo volver atras",()=>_=UndoLatestAsync());
        Action("Generar backup","Copia verificada del proyecto y su historial.","respaldo copia seguridad",()=>Backup_Click(BackupButton,new RoutedEventArgs()));
        Action("Restaurar backup…","Restaura un backup verificado en una carpeta nueva.","respaldo copia",()=>RestoreBackup_Click(RestoreBackupButton,new RoutedEventArgs()));
        Action("Abrir otro proyecto…","Cambia de juego extraído.","abrir cargar carpeta",()=>OpenProject_Click(OpenProjectButton,new RoutedEventArgs()));
        Action(_simplified?"Cambiar a modo completo":"Cambiar a modo simplificado",_simplified?"Muestra todos los editores y herramientas.":"Deja a la vista solo las secciones principales.","modo vista",()=>ChooseExperience(!_simplified));
        return entries;
    }
}
