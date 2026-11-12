using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using HorizontalAlignment=System.Windows.HorizontalAlignment;
using ListBox=System.Windows.Controls.ListBox;
using TextBox=System.Windows.Controls.TextBox;
using Panel=System.Windows.Controls.Panel;
using UserControl=System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>
/// Nintendo DS workspace (Gen4 and Gen5), built like the Sword/Shield one: a sidebar of sections that open in the shared record
/// editor, with changes stacking in an <see cref="NdsProject"/> until "Exportar ROM…" writes a new ROM. The source is never modified.
/// </summary>
internal sealed class NdsWorkspaceView:UserControl
{
    private static readonly (NdsSection Section,string Route)[] Sections=
        [(NdsSection.Pokemon,"Pokemon"),(NdsSection.Moves,"Moves"),(NdsSection.Learnsets,"Learnsets"),(NdsSection.Evolutions,"Evolutions"),(NdsSection.MoveLists,"MoveLists"),(NdsSection.Machines,"Machines"),(NdsSection.Tutors,"Tutors"),(NdsSection.EggMoves,"EggMoves"),(NdsSection.Trainers,"Trainers"),(NdsSection.Encounters,"Encounters"),(NdsSection.Starters,"Starters"),(NdsSection.Statics,"Statics"),(NdsSection.Trades,"Trades"),(NdsSection.FieldItems,"FieldItems"),(NdsSection.Shops,"Shops")];
    private readonly Window _owner;
    private readonly ContentControl _page=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly Dictionary<string,Button> _navigation=new();
    private readonly Dictionary<string,FrameworkElement> _pages=new();
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly CheckBox _romhacks=new(){Content="Admitir romhacks (tablas modificadas; beta)",Margin=new Thickness(0,10,0,0)};
    private readonly TextBlock _summary=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    private readonly ListBox _history=new(){MinHeight=160,Margin=new Thickness(0,8,0,0)};
    private readonly Button _discard=new(){Content="Descartar cambios pendientes",IsEnabled=false,Margin=new Thickness(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Left};
    private bool _busy;

    /// <summary>Destinations of this workspace for the search palette (Ctrl+K): its own sidebar entries.</summary>
    internal IReadOnlyList<PaletteEntry> PaletteEntries(string location)=>_navigation
        .Select(p=>new PaletteEntry(System.Windows.Automation.AutomationProperties.GetName(p.Value) is string name&&name.Length>0?StripSuffix(name):p.Value.Content as string??p.Key,location,"Sección de "+location+".",p.Key,()=>_=NavigateAsync(p.Key)))
        .ToArray();
    private static string StripSuffix(string name)=>name.Replace("Sección DS · ","").Replace(" · Espada/Escudo","");
    private NdsProject? _project;
    internal NdsProject? Project
    {
        get=>_project;
        private set
        {
            if(_project is not null)_project.PersistenceChanged-=OnPersistenceChanged;
            _project=value;
            if(value is not null)value.PersistenceChanged+=OnPersistenceChanged;
            RefreshProjectState();
        }
    }
    // State of the persistent project, always visible in the sidebar; the action appears only when the user has something to decide.
    private readonly TextBlock _projectState=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,10,0,0),FontSize=12};
    private readonly Button _projectAction=new(){Margin=new Thickness(4,6,0,0),HorizontalAlignment=HorizontalAlignment.Left,Visibility=Visibility.Collapsed};
    internal string ProjectStateText=>_projectState.Text;
    internal Button ProjectActionButton=>_projectAction;
    private void OnPersistenceChanged(){if(Dispatcher.CheckAccess())RefreshProjectState();else Dispatcher.BeginInvoke(RefreshProjectState);}
    private void RefreshProjectState()
    {
        var state=_project?.PersistenceState;
        _projectState.Text=_project is null?"":_project.PersistenceSummary;
        _projectState.Visibility=_project is null?Visibility.Collapsed:Visibility.Visible;
        _projectState.SetResourceReference(TextBlock.ForegroundProperty,state is NdsProjectPersistenceState.Error or NdsProjectPersistenceState.Conflict or NdsProjectPersistenceState.RecoveryPending?"TextMain":"TextSoft");
        _projectState.FontWeight=state is NdsProjectPersistenceState.Error or NdsProjectPersistenceState.Conflict or NdsProjectPersistenceState.RecoveryPending or NdsProjectPersistenceState.ReadOnly?FontWeights.SemiBold:FontWeights.Normal;
        _projectAction.Content=state==NdsProjectPersistenceState.RecoveryPending?"Reconciliar con lo guardado":"Recargar el proyecto guardado";
        _projectAction.Visibility=state is NdsProjectPersistenceState.RecoveryPending or NdsProjectPersistenceState.Conflict?Visibility.Visible:Visibility.Collapsed;
        AutomationProperties.SetName(_projectAction,"Proyecto DS · "+_projectAction.Content);
    }
    private void ResolveProjectState()
    {
        if(_busy||_project is not {} project)return;
        if(project.PersistenceState==NdsProjectPersistenceState.Conflict&&!StudioDialog.AutomatedChecks&&!StudioDialog.Confirm(_owner,"Proyecto de Nintendo DS","El archivo del proyecto cambió fuera de esta sesión. ¿Cargar lo que hay guardado en el disco?","Esta sesión solo contiene operaciones que ya se habían guardado. Se mostrará el contenido actual del archivo; no se sobrescribe nada."))return;
        try{project.ReloadPersistence();}
        catch(InvalidOperationException ex){_status.Text="No se pudo cargar el proyecto guardado: "+ex.Message;if(!StudioDialog.AutomatedChecks)StudioDialog.Error(_owner,"Proyecto de Nintendo DS",ex.Message);RefreshProjectState();return;}
        _pages.Clear();UpdateStatus();ContextChanged?.Invoke();_ = NavigateAsync("Home",force:true);
    }
    internal bool Busy=>_busy;
    internal event Action? ContextChanged;
    internal string? CurrentRoute {get;private set;}

    internal NdsWorkspaceView(Window owner)
    {
        _owner=owner;
        SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"TextMain");
        var root=new Grid{ClipToBounds=true};root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty,"Bg");Content=root;
        root.ColumnDefinitions.Add(new(){Width=new GridLength(232)});root.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var nav=new StackPanel{Margin=new Thickness(12)};var navScroll=new ScrollViewer{Content=nav,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};root.Children.Add(navScroll);
        nav.Children.Add(new TextBlock{Text="Nintendo DS",FontWeight=FontWeights.SemiBold,Margin=new Thickness(4,8,0,12)});
        void Add(string route,string label)
        {
            var b=new Button{Content=label,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,2,0,2)};b.SetResourceReference(StyleProperty,"SidebarButton");
            // As the Switch sidebar: long names wrap to a second line instead of being cut.
            b.SetResourceReference(ContentTemplateProperty,"SwitchNavigationContent");b.Padding=new Thickness(10,8,10,8);
            AutomationProperties.SetName(b,"Sección DS · "+label);b.Click+=async(_,_)=>await NavigateAsync(route);_navigation[route]=b;nav.Children.Add(b);
        }
        void Group(string text){var heading=new TextBlock{Text=text,Margin=new Thickness(10,18,0,6),FontSize=11,FontWeight=FontWeights.SemiBold};heading.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");nav.Children.Add(heading);}
        Add("Home","Inicio");Group("EDICIÓN");foreach(var (section,route) in Sections)Add(route,NdsProject.SectionName(section));
        Add("Events","Eventos");
        Group("SISTEMAS");Add("Tweaks","Ajustes del juego");Add("Template","Global Template y Batch");Add("History","Historial de cambios");
        _status.Margin=new Thickness(4,15,0,0);_status.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");nav.Children.Add(_status);
        AutomationProperties.SetName(_projectState,"Estado del proyecto DS");AutomationProperties.SetLiveSetting(_projectState,AutomationLiveSetting.Polite);
        nav.Children.Add(_projectState);EditorPresentation.StyleAction(_projectAction,EditorPresentation.ActionRole.Secondary);_projectAction.Click+=(_,_)=>ResolveProjectState();nav.Children.Add(_projectAction);RefreshProjectState();
        Grid.SetColumn(_page,1);root.Children.Add(_page);
        AutomationProperties.SetName(_romhacks,"Admitir romhacks");AutomationProperties.SetName(_history,"Cambios pendientes");AutomationProperties.SetName(_discard,"Descartar cambios pendientes");
        _discard.Click+=(_,_)=>Discard();
        EditorPresentation.StyleAction(_discard,EditorPresentation.ActionRole.Tertiary);
        _ = NavigateAsync("Home");
    }

    internal bool CanLeave()=>!_busy&&PageCanLeave();
    // The shared record editor and the 3DS views used by DS (Level Up) ask before discarding unsaved changes.
    private bool PageCanLeave()=>_page.Content switch{RecordEditorView record=>record.PrepareToLeave(),LevelUpView levelUp=>levelUp.ConfirmLeave(),NdsEventEditorView events=>events.ConfirmLeave(),_=>true};

    /// <summary>Asks for a DS ROM and opens it; pending changes of the open ROM must be exported or discarded first.</summary>
    internal async Task OpenAsync()
    {
        if(_busy)return;
        // The window opens it through the universal flow (detector, engine, persistent project); that flow asks what has to be asked.
        if(OpenRequested is {} open)
        {
            var chosen=new Microsoft.Win32.OpenFileDialog{Filter="ROM, proyecto o checkpoint de Nintendo DS|*.nds;*.drproj;*.ndsevents.json|ROM de Nintendo DS (*.nds)|*.nds|Proyecto DS (*.drproj)|*.drproj|Checkpoint de eventos DS|*.ndsevents.json",Title="Abrir ROM, proyecto o checkpoint de eventos de Nintendo DS"};
            if(chosen.ShowDialog(_owner)==true)await open(chosen.FileName);
            return;
        }
        if(Project is {HasChanges:true,IsPersistent:false}&&!StudioDialog.AutomatedChecks&&!StudioDialog.Confirm(_owner,"Nintendo DS","Hay cambios sin exportar en "+Project.Game+". ¿Descartarlos y abrir otra ROM?"))return;
        var picker=new Microsoft.Win32.OpenFileDialog{Filter="ROM o checkpoint de eventos DS|*.nds;*.ndsevents.json|ROM de Nintendo DS (*.nds)|*.nds|Checkpoint de eventos DS|*.ndsevents.json",Title="Abrir ROM o checkpoint de eventos de Nintendo DS"};
        if(picker.ShowDialog(_owner)==true)await LoadAsync(picker.FileName);
    }

    /// <summary>Set by the window: opens a chosen file through the universal "Abrir proyecto" flow instead of the direct, in-memory load of this view.</summary>
    internal Func<string,Task>? OpenRequested{get;set;}

    /// <summary>The "Admitir romhacks" choice (shared with the universal "Abrir proyecto" dialog).</summary>
    internal bool AllowRomhacks{get=>_romhacks.IsChecked==true;set=>_romhacks.IsChecked=value;}

    /// <summary>
    /// Installs a project already opened (and verified) by the engine, for the universal "Abrir proyecto". Returns the project it replaced, which the caller
    /// disposes only after everything succeeded. If anything fails here the previous project, pages, route and status are restored and the error is thrown.
    /// </summary>
    internal async Task<NdsProject?> InstallAsync(NdsProject project,bool allowRomhacks,Action? fault=null)
    {
        if(_busy)throw new InvalidOperationException("El editor de Nintendo DS está ocupado.");
        var previous=Project;var pages=new Dictionary<string,FrameworkElement>(_pages);var content=_page.Content;var route=CurrentRoute;var hacks=AllowRomhacks;var status=_status.Text;
        try
        {
            Project=project;_pages.Clear();AllowRomhacks=allowRomhacks;_status.Text=project.Game+(project.IsRomhack?" · romhack":"");
            fault?.Invoke();
            await NavigateAsync("Home",force:true);
            if(!ReferenceEquals(Project,project)||ReferenceEquals(_page.Content,content)||CurrentRoute!="Home")throw new InvalidOperationException("No se pudo mostrar el proyecto de Nintendo DS.");
            return previous;
        }
        catch
        {
            Project=previous;_pages.Clear();foreach(var p in pages)_pages[p.Key]=p.Value;_page.Content=content;CurrentRoute=route;AllowRomhacks=hacks;_status.Text=status;
            ContextChanged?.Invoke();
            throw;
        }
    }

    internal async Task LoadAsync(string path)
    {
        if(_busy)return;SetBusy(true,"Leyendo y verificando la ROM…");
        try
        {
            bool romhacks=_romhacks.IsChecked==true;
            var project=await Task.Run(()=>NdsProject.Open(path,romhacks));
            if(!PageCanLeave()){project.Dispose();return;}
            Project?.Dispose();Project=project;_pages.Clear();
            _status.Text=project.Game+(project.IsRomhack?" · romhack":"");
        }
        catch(Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            _status.Text="No se puede usar esta ROM: "+ex.Message;
            if(!StudioDialog.AutomatedChecks)StudioDialog.Error(_owner,"Nintendo DS",ex.Message);
        }
        finally{SetBusy(false,null);}
        await NavigateAsync("Home",force:true);
    }

    /// <summary>Asks for a new file name and writes the ROM with every pending change, plus its change log.</summary>
    internal async Task ExportAsync()
    {
        if(_busy||Project is not {HasChanges:true} project)return;
        if(!PageCanLeave())return;
        var picker=new Microsoft.Win32.SaveFileDialog{Filter="ROM de Nintendo DS (*.nds)|*.nds",FileName=Path.GetFileNameWithoutExtension(project.SourcePath)+"-random.nds",Title="Guardar ROM nueva"};
        if(picker.ShowDialog(_owner)!=true)return;
        await ExportToAsync(picker.FileName);
    }

    internal async Task ExportToAsync(string output)
    {
        if(_busy||Project is not {HasChanges:true} project)return;
        using var interaction=UiProgress.BlockInteraction();
        SetBusy(true,"Exportando y verificando la ROM nueva…");UiProgress.Begin("Nintendo DS · exportando ROM…");
        try
        {
            string log=await Task.Run(()=>project.Export(output));
            _status.Text="ROM exportada: "+Path.GetFileName(output)+". Registro: "+Path.GetFileName(log)+".";
            if(!StudioDialog.AutomatedChecks)StudioDialog.Notice(_owner,"Nintendo DS","ROM exportada y verificada.",output);
        }
        catch(Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            _status.Text="No se pudo exportar: "+ex.Message;
            if(!StudioDialog.AutomatedChecks)StudioDialog.Error(_owner,"Nintendo DS",ex.Message);
        }
        finally{UiProgress.End();SetBusy(false,null);}
    }

    internal async Task NavigateAsync(string route,bool force=false)
    {
        if(_busy||!force&&route==CurrentRoute&&route!="Home"&&route!="History")return;
        if(!PageCanLeave())return;
        if(route is not ("Home" or "History" or "Tweaks" or "Template")&&Project is null){_status.Text="Abre una ROM de DS para editar.";route="Home";}
        try
        {
            FrameworkElement content;
            if(route=="Home")content=HomePage();
            else if(route=="Tweaks")content=TweaksPage();
            else if(route=="Template")content=TemplatePage();
            else if(route=="History")content=HistoryPage();
            else if(route=="Events")
            {
                if(!Project!.SupportsEventProfile)content=Notice("Eventos","Fase 1: solo Pokémon Platino CPUE/1, con tablas acreditadas. Otros títulos y revisiones no están implementados.");
                else
                {
                    SetBusy(true,"Leyendo mapas y eventos de Platino…");
                    using var interaction=UiProgress.BlockInteraction();
                    try
                    {
                        var project=Project;var (session,terrain,props)=await Task.Run(()=>(project.Events,project.LoadTerrain(),project.LoadProps()));
                        var view=new NdsEventEditorView(session,terrain.Terrain,terrain.Note,props.Props,props.Note);if(project.IsPersistent)view.ProjectSave=project.SaveEvent;view.Changed+=()=>{project.EventChanged();UpdateStatus();ContextChanged?.Invoke();};content=view;
                    }
                    finally{SetBusy(false,null);}
                }
            }
            else if(_pages.TryGetValue(route,out var cached)){if(cached is RecordEditorView record)record.RefreshSession();content=cached;}
            else
            {
                var section=Sections.First(s=>s.Route==route).Section;
                if(section!=NdsSection.MoveLists&&Project!.SectionProblem(section) is {} problem)
                {
                    content=Notice(NdsProject.SectionName(section),problem);
                }
                else if(section==NdsSection.MoveLists)
                {
                    // The 3DS «MT y tutores» layout; TM/Tutor Sanity work even where the TM list itself cannot change (Black/White).
                    // Not cached: it is rebuilt on each visit, so it always shows the history as it is now (undo, templates).
                    var view=new NdsMachineTutorView(Project!);view.Changed+=()=>{UpdateStatus();ContextChanged?.Invoke();};content=view;
                }
                else if(section==NdsSection.Learnsets)
                {
                    // The 3DS Level Up view: one vertical table per species and the option cards on the right.
                    var source=new NdsLevelUpSource(Project!);source.Changed+=()=>{UpdateStatus();ContextChanged?.Invoke();};
                    var view=new LevelUpView(source);content=view;
                }
                else
                {
                    SetBusy(true,"Cargando "+NdsProject.SectionName(section).ToLowerInvariant()+"…");
                    try
                    {
                        var project=Project!;
                        var editor=await Task.Run(()=>new NdsRecordEditor(project,section));
                        editor.Changed+=()=>{UpdateStatus();ContextChanged?.Invoke();};
                        var view=new RecordEditorView(editor);_pages[route]=view;content=view;
                    }
                    finally{SetBusy(false,null);}
                }
            }
            _page.Content=content;CurrentRoute=route;
            foreach(var p in _navigation){p.Value.FontWeight=p.Key==route?FontWeights.Bold:FontWeights.Normal;if(p.Key==route)p.Value.SetResourceReference(BackgroundProperty,"Selected");else p.Value.ClearValue(BackgroundProperty);}
            UpdateStatus();ContextChanged?.Invoke();
        }
        catch(Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            StudioDialog.Error(_owner,"Nintendo DS",e.Message);
        }
    }

    private FrameworkElement HomePage()
    {
        var panel=new StackPanel{Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock{Text="Nintendo DS · Gen4 y Gen5 (beta)",FontSize=23,FontWeight=FontWeights.SemiBold});
        var intro=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0),
            Text="Abre un volcado original de Pokémon Diamante, Platino o HeartGold (USA), Negro (USA/EUR), Blanco (España) o Blanco 2 (USA/EUR) con «Abrir proyecto». "+
                 "Cada sección se edita a mano o se randomiza con vista previa; los cambios se acumulan y «Exportar ROM…» escribe una ROM nueva con su registro. La ROM original no se modifica. "+
                 "Negro, Blanco y Blanco 2 son DSi-enhanced: la ROM exportada funciona en modo DS y en emuladores, no en modo DSi."};
        intro.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");panel.Children.Add(intro);
        panel.Children.Add(_romhacks.Parent is Panel old?Detach(old,_romhacks):_romhacks);
        var hint=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(22,2,0,0),Text="Acepta ROM modificadas de esos juegos si conservan la estructura esperada; las opciones que tocan código del juego solo se activan si ese código es el original."};
        hint.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");panel.Children.Add(hint);
        _summary.Text=Project is null?"Ninguna ROM abierta.":
            $"{Project.Game} · {(Project.IsRomhack?"romhack (tablas modificadas)":Project.Identity.KnownDump?"volcado original verificado":"código reconocido, volcado no verificado")}\n{Project.SourcePath}"+
            string.Concat(Project.Unavailable.Select(u=>$"\nNo disponible · {u.Key}: {u.Value}."))+
            $"\n{(Project.HasChanges?Project.OperationCount+" operaciones pendientes de exportar.":"Sin cambios pendientes.")}"+
            "\n"+Project.PersistenceSummary;
        panel.Children.Add(_summary.Parent is Panel s?Detach(s,_summary):_summary);
        _discard.IsEnabled=Project?.HasChanges==true;panel.Children.Add(_discard.Parent is Panel d?Detach(d,_discard):_discard);
        return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    // Tweaks from Universal Pokémon Randomizer ZX, applied as one more stacked operation.
    private FrameworkElement TweaksPage()
    {
        var panel=new StackPanel{Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock{Text="Ajustes del juego",FontSize=23,FontWeight=FontWeights.SemiBold});
        var intro=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,10),
            Text="Ajustes de Universal Pokémon Randomizer ZX. Solo se aplican sobre el código original del juego (ARM9, overlays y scripts) y se exportan con el resto de cambios."};
        intro.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");panel.Children.Add(intro);
        if(Project is null){panel.Children.Add(new TextBlock{Text="Abre una ROM para ver sus ajustes."});return panel;}
        CheckBox Box(string text,string name){var box=new CheckBox{Content=text,Margin=new Thickness(0,6,0,0)};AutomationProperties.SetName(box,name);panel.Children.Add(box);return box;}
        bool gen4=Project.Generation==4;string? arm9=Project.Arm9TweaksProblem;
        var fast=gen4&&arm9 is null?Box("Texto instantáneo","Texto instantáneo"):null;var dex=gen4?Box("Pokédex Nacional desde el inicio","Pokédex Nacional desde el inicio"):null;
        var shoes=!gen4||arm9 is null?Box("Correr sin las Deportivas","Correr sin las Deportivas"):null;
        var distortion=Project.IsPlatinum?Box("Mundo Distorsión más rápido","Mundo Distorsión más rápido"):null;
        var types=Box("Tabla de tipos de Gen6 (Acero ya no resiste Fantasma ni Siniestro)","Tabla de tipos de Gen6");
        var catching=Box("Captura garantizada (todas las Poké Ball atrapan)","Captura garantizada");
        var music=gen4?null:Box("Sin música de PS bajos","Sin música de PS bajos");
        var pickup=Box("Objetos de Recogida aleatorios","Objetos de Recogida aleatorios");
        if(arm9 is not null){var off=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0),Text="No disponible: "+arm9+"."};off.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");panel.Children.Add(off);}
        var note=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0),Text="Cada ajuste se aplica una sola vez: los de código y script comprueban que siguen siendo los originales. La Recogida aleatoria usa una semilla nueva en cada lote."};
        note.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");panel.Children.Add(note);
        var apply=new Button{Content="Aplicar ajustes",Margin=new Thickness(0,12,0,0),HorizontalAlignment=HorizontalAlignment.Left};AutomationProperties.SetName(apply,"Aplicar ajustes del juego");
        EditorPresentation.StyleAction(apply,EditorPresentation.ActionRole.Primary);panel.Children.Add(apply);
        var result=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};panel.Children.Add(result);
        apply.Click+=(_,_)=>
        {
            var options=new NdsTweakOptions{FastestText=fast?.IsChecked==true,NationalDexAtStart=dex?.IsChecked==true,RunWithoutRunningShoes=shoes?.IsChecked==true,FastDistortionWorld=distortion?.IsChecked==true,
                UpdateTypeEffectiveness=types.IsChecked==true,GuaranteedCatch=catching.IsChecked==true,DisableLowHpMusic=music?.IsChecked==true,RandomPickupItems=pickup.IsChecked==true};
            if(options.RandomPickupItems)options=options with{Seed=pk3DS.Core.NX.SwitchSeed.New32()};
            if(options==new NdsTweakOptions()){result.Text="Elige al menos un ajuste.";return;}
            try{Project.Apply(Project.PreviewTweaks(options));result.Text="Ajustes aplicados; se escribirán al exportar.";UpdateStatus();ContextChanged?.Invoke();}
            catch(Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException){result.Text="No se pudo aplicar: "+ex.Message;}
        };
        return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    private JsonObject? _template;

    // Global Template: the history as a replayable recipe for the same game; Batch: several ROMs from it with new seeds.
    private FrameworkElement TemplatePage()
    {
        var panel=new StackPanel{Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock{Text="Global Template y Batch",FontSize=23,FontWeight=FontWeights.SemiBold});
        var intro=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,10),
            Text="Un Global Template guarda las operaciones del historial (ediciones, randomizaciones con su semilla y ajustes) para repetirlas sobre otra copia del mismo juego. Batch genera varias ROM nuevas desde la ROM abierta, cada una con semillas nuevas. Los templates de DS son por juego y no se mezclan con los de 3DS o Switch."};
        intro.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");panel.Children.Add(intro);
        if(Project is null){panel.Children.Add(new TextBlock{Text="Abre una ROM para crear o aplicar un template."});return panel;}
        var summary=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};panel.Children.Add(summary);
        var result=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
        void Show()
        {
            summary.Text=_template is null?"Ningún template cargado.":
                $"Template de {_template["Title"]} ({_template["Game"]}) · {((JsonArray)_template["Operations"]!).Count} operaciones:\n"+
                string.Join("\n",((JsonArray)_template["Operations"]!).Select((o,i)=>$"{i+1}. {o?["Action"]?.GetValue<string>()??(o?["Kind"]?.GetValue<string>()=="Edit"?"Edición manual · "+o["Section"]:o?["Kind"]?.GetValue<string>())}"));
        }
        var buttons=new WrapPanel();panel.Children.Add(buttons);
        void Action(string label,Action action,EditorPresentation.ActionRole role=EditorPresentation.ActionRole.Secondary)
        {
            var b=new Button{Content=label,Margin=new Thickness(0,0,8,8)};AutomationProperties.SetName(b,label);EditorPresentation.StyleAction(b,role);buttons.Children.Add(b);
            b.Click+=(_,_)=>{try{action();}catch(Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or UnauthorizedAccessException){result.Text=ex.Message;}};
        }
        Action("Crear desde historial",()=>{_template=Project.CaptureGlobal();Show();result.Text=_template["Operations"]!.AsArray().Count==0?"El historial está vacío: aplica operaciones primero.":"Template creado desde el historial.";});
        Action("Cargar template…",()=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Global Template de DS (*.json)|*.json"};if(picker.ShowDialog(_owner)!=true)return;_template=NdsProject.ReadGlobal(picker.FileName);Show();result.Text="Template cargado.";});
        Action("Guardar template…",()=>{if(_template is null)throw new InvalidOperationException("Crea o carga un template.");var picker=new Microsoft.Win32.SaveFileDialog{Filter="Global Template de DS (*.json)|*.json",FileName="global-ds-"+_template["Game"]+".json"};if(picker.ShowDialog(_owner)!=true)return;NdsProject.WriteGlobal(picker.FileName,_template);result.Text="Template guardado.";});
        Action("Aplicar a esta ROM",()=>
        {
            if(_template is null)throw new InvalidOperationException("Crea o carga un template.");
            int count=Project.ApplyGlobal(_template,newSeeds:false);_pages.Clear();UpdateStatus();ContextChanged?.Invoke();
            result.Text=$"{count} operaciones aplicadas con sus semillas; se escribirán al exportar.";
        },EditorPresentation.ActionRole.Primary);
        panel.Children.Add(new TextBlock{Text="Batch ROM Builder",FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,16,0,6)});
        var fields=new WrapPanel();panel.Children.Add(fields);
        var count=new TextBox{Text="3",Width=70};var name=new TextBox{Text="Definitive-"+Project.Identity.Profile.GameCode,MinWidth=200};
        AutomationProperties.SetName(count,"Cantidad de ROMs");AutomationProperties.SetName(name,"Nombre base");
        void Field(string label,FrameworkElement control){var p=new StackPanel{Margin=new Thickness(0,0,12,8)};p.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,0,0,4)});p.Children.Add(control);fields.Children.Add(p);}
        Field("ROMs (1-20)",count);Field("Nombre base",name);
        var build=new Button{Content="Generar lote…",HorizontalAlignment=HorizontalAlignment.Left};AutomationProperties.SetName(build,"Generar lote");EditorPresentation.StyleAction(build,EditorPresentation.ActionRole.Primary);panel.Children.Add(build);
        build.Click+=async(_,_)=>
        {
            if(_busy)return;
            if(_template is null){result.Text="Crea o carga un template.";return;}
            if(!int.TryParse(count.Text,out int n)||n is <1 or >20){result.Text="Cantidad entre 1 y 20.";return;}
            var folder=new Microsoft.Win32.OpenFolderDialog{Title="Carpeta de salida del lote"};if(folder.ShowDialog(_owner)!=true)return;
            var template=(JsonObject)_template.DeepClone();string source=Project.SourcePath,baseName=name.Text.Trim();bool romhack=Project.IsRomhack;
            using var interaction=UiProgress.BlockInteraction();SetBusy(true,"Generando lote…");UiProgress.Begin("Nintendo DS · Batch ROM Builder…");
            try
            {
                var progress=new Progress<string>(s=>_status.Text=s);
                var written=await Task.Run(()=>NdsProject.BuildBatch(source,romhack,template,n,folder.FolderName,baseName,progress,CancellationToken.None));
                result.Text=$"{written.Count} ROM generadas y verificadas en {folder.FolderName}.";
            }
            catch(Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException){result.Text="No se pudo generar el lote: "+ex.Message;}
            finally{UiProgress.End();SetBusy(false,null);UpdateStatus();}
        };
        panel.Children.Add(result);Show();
        return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    private FrameworkElement HistoryPage()
    {
        var panel=new DockPanel{Margin=new Thickness(24)};
        var title=new TextBlock{Text="Historial de cambios",FontSize=23,FontWeight=FontWeights.SemiBold};DockPanel.SetDock(title,Dock.Top);panel.Children.Add(title);
        var note=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0),Text="Operaciones aplicadas a la ROM abierta, en orden. Se escriben al exportar, junto con este registro."};
        note.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");DockPanel.SetDock(note,Dock.Top);panel.Children.Add(note);
        _history.ItemsSource=Project is null?Array.Empty<string>():Project.History.SelectMany(o=>new[]{$"== {o.Action}{(o.Seed is {} seed?" · semilla "+seed:"")} · {o.Changes.Count} cambios"}.Concat(o.Changes.Take(200).Select(Project.Describe))).Concat(Project.EventHistory.Select(e=>"Eventos · "+e)).ToArray();
        if(_history.Parent is Panel old)old.Children.Remove(_history);panel.Children.Add(_history);
        return panel;
    }

    private static FrameworkElement Notice(string title,string text)
    {
        var panel=new StackPanel{Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock{Text=title,FontSize=23,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        return panel;
    }

    private static UIElement Detach(Panel parent,UIElement child){parent.Children.Remove(child);return child;}

    private void Discard()
    {
        if(Project is not {HasChanges:true} project||_busy)return;
        if(!StudioDialog.AutomatedChecks&&!StudioDialog.Confirm(_owner,"Nintendo DS","¿Descartar todos los cambios pendientes de "+project.Game+"?"))return;
        // On a persistent project discarding is saved too (the previous generation stays in its backups); if that save fails nothing is discarded.
        try{project.Reset();}
        catch(InvalidOperationException ex){_status.Text="No se descartó: "+ex.Message;if(!StudioDialog.AutomatedChecks)StudioDialog.Error(_owner,"Nintendo DS",ex.Message);return;}
        _pages.Clear();UpdateStatus();ContextChanged?.Invoke();_ = NavigateAsync("Home",force:true);
    }

    private void UpdateStatus()
    {
        RefreshProjectState();
        if(Project is null)return;
        _status.Text=Project.Game+(Project.IsRomhack?" · romhack":"")+" · "+(Project.HasChanges?Project.OperationCount+" operaciones pendientes":"sin cambios pendientes");
    }

    private void SetBusy(bool busy,string? status)
    {
        _busy=busy;foreach(var b in _navigation.Values)b.IsEnabled=!busy;_romhacks.IsEnabled=!busy;
        if(status is not null)_status.Text=status;
    }
}
