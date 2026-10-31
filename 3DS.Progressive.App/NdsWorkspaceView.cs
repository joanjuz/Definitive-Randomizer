using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using HorizontalAlignment=System.Windows.HorizontalAlignment;
using ListBox=System.Windows.Controls.ListBox;
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
        [(NdsSection.Trainers,"Trainers"),(NdsSection.Encounters,"Encounters"),(NdsSection.Starters,"Starters"),(NdsSection.FieldItems,"FieldItems"),(NdsSection.Shops,"Shops")];
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

    internal NdsProject? Project {get;private set;}
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
            AutomationProperties.SetName(b,"Sección DS · "+label);b.Click+=async(_,_)=>await NavigateAsync(route);_navigation[route]=b;nav.Children.Add(b);
        }
        void Group(string text){var heading=new TextBlock{Text=text,Margin=new Thickness(10,18,0,6),FontSize=11,FontWeight=FontWeights.SemiBold};heading.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");nav.Children.Add(heading);}
        Add("Home","Inicio");Group("EDICIÓN");foreach(var (section,route) in Sections)Add(route,NdsProject.SectionName(section));
        Group("SISTEMAS");Add("History","Historial de cambios");
        _status.Margin=new Thickness(4,15,0,0);_status.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");nav.Children.Add(_status);
        Grid.SetColumn(_page,1);root.Children.Add(_page);
        AutomationProperties.SetName(_romhacks,"Admitir romhacks");AutomationProperties.SetName(_history,"Cambios pendientes");AutomationProperties.SetName(_discard,"Descartar cambios pendientes");
        _discard.Click+=(_,_)=>Discard();
        EditorPresentation.StyleAction(_discard,EditorPresentation.ActionRole.Tertiary);
        _ = NavigateAsync("Home");
    }

    internal bool CanLeave()=>!_busy&&(_page.Content is not RecordEditorView record||record.PrepareToLeave());

    /// <summary>Asks for a DS ROM and opens it; pending changes of the open ROM must be exported or discarded first.</summary>
    internal async Task OpenAsync()
    {
        if(_busy)return;
        if(Project?.HasChanges==true&&!StudioDialog.AutomatedChecks&&!StudioDialog.Confirm(_owner,"Nintendo DS","Hay cambios sin exportar en "+Project.Game+". ¿Descartarlos y abrir otra ROM?"))return;
        var picker=new Microsoft.Win32.OpenFileDialog{Filter="ROM de Nintendo DS (*.nds)|*.nds",Title="ROM original de Pokémon para Nintendo DS"};
        if(picker.ShowDialog(_owner)==true)await LoadAsync(picker.FileName);
    }

    internal async Task LoadAsync(string path)
    {
        if(_busy)return;SetBusy(true,"Leyendo y verificando la ROM…");
        try
        {
            bool romhacks=_romhacks.IsChecked==true;
            var project=await Task.Run(()=>NdsProject.Open(path,romhacks));
            if(_page.Content is RecordEditorView record&&!record.PrepareToLeave()){project.Dispose();return;}
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
        if(_page.Content is RecordEditorView record&&!record.PrepareToLeave())return;
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
        if(_page.Content is RecordEditorView previous&&!previous.PrepareToLeave())return;
        if(route is not ("Home" or "History")&&Project is null){_status.Text="Abre una ROM de DS para editar.";route="Home";}
        try
        {
            FrameworkElement content;
            if(route=="Home")content=HomePage();
            else if(route=="History")content=HistoryPage();
            else if(_pages.TryGetValue(route,out var cached)){if(cached is RecordEditorView record)record.RefreshSession();content=cached;}
            else
            {
                var section=Sections.First(s=>s.Route==route).Section;
                if(section==NdsSection.Shops&&!Project!.Allows(Gen5Feature.Shops)||section==NdsSection.Starters&&!Project!.Allows(Gen5Feature.Starters))
                {
                    content=Notice(NdsProject.SectionName(section),"No disponible en esta ROM: "+Project!.Unavailable[section==NdsSection.Shops?Gen5Feature.Shops:Gen5Feature.Starters]+".");
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
            $"\n{(Project.HasChanges?Project.History.Count+" operaciones pendientes de exportar.":"Sin cambios pendientes.")}";
        panel.Children.Add(_summary.Parent is Panel s?Detach(s,_summary):_summary);
        _discard.IsEnabled=Project?.HasChanges==true;panel.Children.Add(_discard.Parent is Panel d?Detach(d,_discard):_discard);
        return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    private FrameworkElement HistoryPage()
    {
        var panel=new DockPanel{Margin=new Thickness(24)};
        var title=new TextBlock{Text="Historial de cambios",FontSize=23,FontWeight=FontWeights.SemiBold};DockPanel.SetDock(title,Dock.Top);panel.Children.Add(title);
        var note=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0),Text="Operaciones aplicadas a la ROM abierta, en orden. Se escriben al exportar, junto con este registro."};
        note.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");DockPanel.SetDock(note,Dock.Top);panel.Children.Add(note);
        _history.ItemsSource=Project is null?Array.Empty<string>():Project.History.SelectMany(o=>new[]{$"== {o.Action}{(o.Seed is {} seed?" · semilla "+seed:"")} · {o.Changes.Count} cambios"}.Concat(o.Changes.Take(200).Select(Project.Describe))).ToArray();
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
        project.Reset();_pages.Clear();UpdateStatus();ContextChanged?.Invoke();_ = NavigateAsync("Home",force:true);
    }

    private void UpdateStatus()
    {
        if(Project is null)return;
        _status.Text=Project.Game+(Project.IsRomhack?" · romhack":"")+" · "+(Project.HasChanges?Project.History.Count+" operaciones pendientes":"sin cambios pendientes");
    }

    private void SetBusy(bool busy,string? status)
    {
        _busy=busy;foreach(var b in _navigation.Values)b.IsEnabled=!busy;_romhacks.IsEnabled=!busy;
        if(status is not null)_status.Text=status;
    }
}
