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
    /// <summary>Tools opened from a hub card, and the hub that contains them.</summary>
    internal static readonly IReadOnlyDictionary<string,string> HubOf=new Dictionary<string,string>
    {
        ["FacilityNormal"]="PokemonTools",["FacilityAlternate"]="PokemonTools",["EggMoves"]="PokemonTools",["MegaEvolutions"]="PokemonTools",
        ["TypeChart"]="PokemonTools",["ShinyRate"]="PokemonTools",["Pickup"]="PokemonTools",["OWSE"]="PokemonTools",
        ["Tutor6"]="Mechanics",["PlayerCaps"]="Mechanics",["UsumRelearner"]="Mechanics",["CroRecipes"]="Mechanics",["Trade6"]="Mechanics",
        ["CatchZones"]="Mechanics",["OPower"]="Mechanics",["PersistentItems"]="Mechanics",
        ["Extraction"]="RomTools",["Rebuild"]="RomTools",["CroIntegrity"]="RomTools",["RestoreSelected"]="RomTools",["TitleScreen6"]="RomTools",
        ["TitleScreen7"]="RomTools",["GarcShuffle"]="RomTools",["Graphics"]="RomTools",["SMDH"]="RomTools"
    };
    private static readonly IReadOnlyDictionary<string,string> HubLabels=new Dictionary<string,string>
    {["PokemonTools"]="Herramientas Pokémon",["Mechanics"]="Mecánicas y CRO",["RomTools"]="Herramientas ROM"};
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
            _breadcrumbHub=hub;SectionHost.Tag=HubLabels[hub];
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
    private void CommandSearch_Click(object sender,RoutedEventArgs e)=>OpenCommandPalette();
    private CommandPalette? _palette;
    internal CommandPalette? Palette=>_palette;
    internal void OpenCommandPalette()
    {
        if(_closed||UiProgress.IsInteractionBlocked||_palette is not null||SwitchHost.Visibility==Visibility.Visible)return;
        _tour?.Finish();
        var focus=Keyboard.FocusedElement as IInputElement;
        _palette=new CommandPalette(PaletteEntries(),()=>
        {
            PaletteHost.Children.Clear();PaletteHost.Visibility=Visibility.Collapsed;_palette=null;
            if(focus is UIElement {IsVisible:true} previous)previous.Focus();
        });
        PaletteHost.Children.Add(_palette);PaletteHost.Visibility=Visibility.Visible;
    }
    private static readonly IReadOnlyDictionary<string,string> SearchKeywords=new Dictionary<string,string>
    {
        ["Pokemon"]="estadisticas stats bst tipos habilidades especies personal base captura ev",
        ["Moves"]="ataques poder precision pp categoria",
        ["LevelUp"]="aprendizaje learnset movimientos por nivel",
        ["MachineTutor"]="mt mo tm hm tutores maquinas compatibilidad",
        ["Evolutions"]="evolucionar evo metodo intercambio",
        ["PokemonTools"]="hub huevo mega tipos shiny pickup instalaciones",
        ["Trainers"]="rivales lideres gimnasio alto mando jefes caps niveles equipos ia dobles",
        ["Encounters"]="salvajes hierba wild sos zonas",
        ["StaticEncounters"]="regalos iniciales starters legendarios totem gift",
        ["FieldItems"]="objetos del mapa suelo ocultos recoger",
        ["Marts"]="tienda comprar bp precios inventario",
        ["Items"]="precios economia dinero fix economy",
        ["GameText"]="textos idioma nombres frases",
        ["StoryText"]="dialogos escenas historia",
        ["Mechanics"]="parches codigo obediencia megas tutores intercambios",
        ["RomTools"]="rom archivos extraer rebuild",
        ["Automation"]="flujos randomizar todo",
        ["Configuration"]="idioma carpetas preferencias about garc",
        ["TemplateStudio"]="plantillas csv json crear",
        ["GlobalTemplate"]="receta definitivo json repetir",
        ["BatchRom"]="lote varias roms semillas",
        ["History"]="registro cambios sha",
        ["Rebuild"]="generar rom exportar reconstruir 3ds romfs exefs",
        ["Extraction"]="extraer descifrar cxi 3ds",
        ["ShinyRate"]="variocolor shiny brillante probabilidad",
        ["TypeChart"]="tipos efectividad tabla debilidades",
        ["EggMoves"]="huevo crianza",
        ["MegaEvolutions"]="mega piedras megaevolucion",
        ["Pickup"]="recogida habilidad",
        ["PlayerCaps"]="limite nivel jugador level cap",
        ["UsumRelearner"]="recordar movimientos cafeteria",
        ["CroRecipes"]="mecanicas recetas battle cro",
        ["Trade6"]="intercambios ofertas",
        ["CatchZones"]="zonas captura",
        ["OPower"]="poderes",
        ["PersistentItems"]="consumibles objetos tras el combate",
        ["CroIntegrity"]="hashes crr integridad",
        ["RestoreSelected"]="restaurar archivos backup",
        ["TitleScreen6"]="logo titulo portada",["TitleScreen7"]="logo titulo portada animacion",
        ["SMDH"]="icono nombre del juego metadatos",["Graphics"]="imagenes bclim bflim png",["GarcShuffle"]="mezclar",
        ["FacilityNormal"]="instalaciones batalla",["FacilityAlternate"]="instalaciones batalla",["OWSE"]="mapas scripts dev"
    };
    private string LocationOf(string route)
    {
        if(HubOf.TryGetValue(route,out var hub))return HubLabels[hub];
        if(route=="Configuration")return "Opciones";
        var button=SidebarButtonFor(route);
        var pair=NavigationGroupPairs.FirstOrDefault(p=>button is not null&&p.Group.Children.Contains(button));
        return pair.Header?.Content as string??"Inicio";
    }
    /// <summary>Destinations for the open game: the same topics the guided tour knows, plus general actions.</summary>
    internal IReadOnlyList<PaletteEntry> PaletteEntries()
    {
        var entries=new List<PaletteEntry>();
        void Action(string title,string description,string keywords,Action run)=>entries.Add(new(title,"Acción",description,keywords,run));
        if(_session is null)
        {
            Action("Abrir proyecto…","Abre la carpeta de un juego extraído (RomFS + ExeFS).","abrir cargar carpeta",()=>OpenProject_Click(OpenProjectButton,new RoutedEventArgs()));
            entries.Add(new("Extraer ROM","Herramientas ROM","Extrae una ROM de 3DS en una carpeta nueva para editarla.",SearchKeywords["Extraction"],()=>ExtractRom_Click(ExtractRomButton,new RoutedEventArgs())));
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
            entries.Add(new(title,LocationOf(route),topic.Steps[0].Text,SearchKeywords.GetValueOrDefault(route,""),()=>NavigateTo(route)));
        }
        Action("Randomizado guiado · paso a paso","Elige secciones, configura opciones y revisa todo antes de generar la ROM.","asistente todo completo randomizar",OpenGuided);
        Action("Inicio","Modos, randomizado guiado y estado del proyecto.","home principal resumen",()=>Home_Click(HomeButton,new RoutedEventArgs()));
        Action("Vista guiada de esta sección","Explica la sección actual sin aplicar cambios (F1).","ayuda tour f1",()=>OpenTour());
        Action("Generar backup","Copia verificada del proyecto y su historial.","respaldo copia seguridad",()=>Backup_Click(BackupButton,new RoutedEventArgs()));
        Action("Restaurar backup…","Restaura un backup verificado en una carpeta nueva.","respaldo copia",()=>RestoreBackup_Click(RestoreBackupButton,new RoutedEventArgs()));
        Action("Abrir otro proyecto…","Cambia de juego extraído.","abrir cargar carpeta",()=>OpenProject_Click(OpenProjectButton,new RoutedEventArgs()));
        Action(_simplified?"Cambiar a modo completo":"Cambiar a modo simplificado",_simplified?"Muestra todos los editores y herramientas.":"Deja a la vista solo las secciones principales.","modo vista",()=>ChooseExperience(!_simplified));
        return entries;
    }
}
