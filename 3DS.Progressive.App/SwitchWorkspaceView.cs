using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
using UserControl=System.Windows.Controls.UserControl;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using HorizontalAlignment=System.Windows.HorizontalAlignment;
namespace ThreeDS.Progressive;
internal sealed class SwitchWorkspaceView:UserControl
{
    private readonly SwordShieldPrototypeWindow _personal;
    private readonly FrameworkElement _personalPage;
    private readonly ContentControl _page=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly Dictionary<string,FrameworkElement> _pages=new();
    private readonly List<Window> _controllers=new();
    private readonly Dictionary<string,Button> _navigation=new();
    private readonly Window _owner;
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Action _home;
    private SwordPokeFeurWindow? _runtime;
    private bool _loadingPage;
    internal event Action? ContextChanged;
    internal SwordShieldProject? Project=>_personal.Project;
    internal void PresentProject(SwordShieldProject project)=>_personal.PresentProject(project);
    internal bool Busy=>_pages.Values.OfType<SwitchAutomationView>().Any(v=>v.Busy)||_loadingPage||_personal.IsWorking||_controllers.Any(w=>w is SwordPokeFeurWindow p?p.IsWorking:w is SwordShieldGameplayWindow g&&g.IsWorking)||_pages.Values.OfType<SwitchDataEditorView>().Any(p=>p.Busy);
    internal Task OpenAsync()=>Busy?Task.CompletedTask:_personal.OpenEmbeddedAsync();
    internal Task ExportAsync()=>Busy?Task.CompletedTask:_personal.ExportEmbeddedAsync();
    internal bool CanLeave()=>!Busy&&(_page.Content is not RecordEditorView record||record.PrepareToLeave())&&_personal.CanLeave();
    internal SwitchWorkspaceView(Window owner,Action home)
    {
        SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"TextMain");
        var labelStyle=new Style(typeof(System.Windows.Controls.Label));labelStyle.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,new DynamicResourceExtension("TextSoft")));Resources.Add(typeof(System.Windows.Controls.Label),labelStyle);
        NameScope.SetNameScope(this,new NameScope());RegisterName("SwitchWorkspacePage",_page);_owner=owner;_home=home;_personal=new SwordShieldPrototypeWindow{EmbeddedClose=home};if(owner.IsVisible)_personal.Owner=owner;_personal.EmbeddedNavigate=route=>_ = NavigateAsync(route);_personalPage=_personal.TakePage();_personal.SessionChanged+=()=>ContextChanged?.Invoke();_personal.ProjectChanged+=()=>{foreach(var controller in _controllers)controller.Close();_controllers.Clear();_pages.Clear();_runtime=null;Dispatcher.BeginInvoke(()=>_ = NavigateAsync("Pokemon"));};
        var root=new Grid{ClipToBounds=true};root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty,"Bg");Content=root;root.ColumnDefinitions.Add(new(){Width=new GridLength(232)});root.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});var nav=new StackPanel{Margin=new Thickness(12)};var scroll=new ScrollViewer{Content=nav,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};var border=new Border{Child=scroll,BorderThickness=new Thickness(0,0,1,0)};border.SetResourceReference(BackgroundProperty,"Navigation");border.SetResourceReference(BorderBrushProperty,"Stroke");root.Children.Add(border);Grid.SetColumn(_page,1);root.Children.Add(_page);
        nav.Children.Add(new TextBlock{Text="Espada/Escudo",FontWeight=FontWeights.SemiBold,Margin=new Thickness(4,8,0,12)});
        void Add(string route,string label){var b=new Button{Content=label,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,2,0,2)};b.SetResourceReference(StyleProperty,"SidebarButton");b.SetResourceReference(System.Windows.Controls.ContentControl.ContentTemplateProperty,"SwitchNavigationContent");b.Padding=new Thickness(10,8,10,8);AutomationProperties.SetName(b,label+" · Espada/Escudo");b.Click+=async(_,_)=>await NavigateAsync(route);nav.Children.Add(b);_navigation.Add(route,b);RegisterName("SwitchRoute"+route,b);}
        void Group(string text){var heading=new TextBlock{Text=text,Margin=new Thickness(10,18,0,6),FontSize=11,FontWeight=FontWeights.SemiBold};heading.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");nav.Children.Add(heading);}
        Add("Home","Inicio");Group("EDICIÓN");Add("Pokemon","Pokémon");Add("Trainers","Entrenadores");Add("Encounters","Encuentros");Add("FieldItems","Field Items");Add("Runtime","Encuentros · combate sorpresa");
        foreach(var section in Enum.GetValues<SwordShieldDataSection>().Where(s=>s!=SwordShieldDataSection.TrainerTeams))Add(section.ToString(),SwitchDataEditorView.TitleFor(section));
        Group("SISTEMAS");Add("Caps","Level caps y EXP");Add("Tools","Mecánicas y herramientas");Add("GlobalTemplate","Global Template");Add("BatchRomBuilder","Batch ROM Builder");Add("History","Historial de cambios");_status.Margin=new Thickness(4,15,0,0);nav.Children.Add(_status);_ = NavigateAsync("Pokemon");
    }
    internal async Task NavigateAsync(string route)
    {
        if(route=="Gameplay")route="Trainers";
        if(Busy)return;if(route is not ("Runtime" or "Caps" or "Tools" or "TrainerCaps")&&_pages.TryGetValue(route,out var current)&&ReferenceEquals(_page.Content,current))return;if(_page.Content is RecordEditorView previous&&!previous.PrepareToLeave())return;if(route=="Pokemon"&&ReferenceEquals(_page.Content,_personalPage))return;if(ReferenceEquals(_page.Content,_personalPage)&&route!="Pokemon"&&!_personal.CanChangeSection())return;if(route=="Home"){_home();return;}var project=_personal.Project;if(project is null&&route!="Pokemon"){_status.Text="Abre una extracción desde Pokémon.";return;}
        if(project is not null&&!project.Identity.CanEdit&&route is not "Pokemon" and not "History"){_status.Text="Build ID desconocido: solo lectura.";return;}
        _loadingPage=true;_status.Text="Cargando "+route+"…";
        try
        {
            FrameworkElement content;
            if(route=="Pokemon"){_personal.RefreshSession();content=_personalPage;}
            else if(route=="History"){content=new SwitchHistoryView(project!);}

            else if(route is "Runtime" or "Caps" or "Tools" or "TrainerCaps")
            {

                if(_runtime is null){_runtime=new SwordPokeFeurWindow(project!){EmbeddedClose=()=>_ = NavigateAsync("Pokemon")};if(_owner.IsVisible)_runtime.Owner=_owner;_runtime.Changed+=()=>ContextChanged?.Invoke();_controllers.Add(_runtime);_pages["Runtime"]=_runtime.TakePage();}_runtime.SelectTab(route is "Caps" or "TrainerCaps"?2:route=="Tools"?3:1);if(route=="TrainerCaps")_runtime.UseTrainerCaps();content=_pages["Runtime"];
            }
            else if(route is "GlobalTemplate" or "BatchRomBuilder"){if(!_pages.TryGetValue(route,out var automation)){automation=new SwitchAutomationView(project!,route=="BatchRomBuilder",()=>ContextChanged?.Invoke());_pages.Add(route,automation);}content=automation;}
            else if(_pages.TryGetValue(route,out var cached)){if(cached is RecordEditorView record)record.RefreshSession();content=cached;}
            else if(route is "Encounters" or "FieldItems")
            {
                var adapter=await Task.Run(()=>new SwordShieldWorldEditor(project!,route=="FieldItems"));adapter.Changed+=()=>ContextChanged?.Invoke();var view=new RecordEditorView(adapter);_pages.Add(route,view);content=view;
            }
            else if(Enum.TryParse<SwordShieldDataSection>(route=="Trainers"?"TrainerTeams":route,out var section))
            {
                var adapter=await Task.Run(()=>new SwordShieldRecordEditor(project!,section));adapter.Changed+=()=>ContextChanged?.Invoke();var view=new RecordEditorView(adapter);if(adapter.UsesTrainerLayout)view.AddInspectorAction("Player Level Caps · por entrenadores",()=>_ = NavigateAsync("TrainerCaps"));_pages.Add(route,view);content=view;
            }
            else return;
            _page.Content=content;foreach(var p in _navigation){p.Value.FontWeight=p.Key==route?FontWeights.Bold:FontWeights.Normal;if(p.Key==route)p.Value.SetResourceReference(BackgroundProperty,"Selected");else p.Value.ClearValue(BackgroundProperty);}
            ContextChanged?.Invoke();_status.Text=project is null?"Abre tu extracción para editar.":project.Identity.Game+" · "+project.Entries.Count+" entradas · original en lectura";
        }
        catch(Exception e){StudioDialog.Error(_owner,"Navegación Switch",e.Message);}
        finally{_loadingPage=false;}
    }
}
