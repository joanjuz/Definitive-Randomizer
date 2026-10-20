using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using Panel=System.Windows.Controls.Panel;
namespace ThreeDS.Progressive;
public partial class MainWindow
{
    private bool _simplified;
    private string _activeSection="Pokemon";
    private Button[] AdvancedNavigation=>[ItemsButton,GameTextButton,StoryTextButton,MechanicsButton,PokemonToolsButton,RomToolsButton,AutomationButton,TemplateStudioButton,GlobalTemplateButton,BatchRomButton];
    private void InitializeGuidance()
    {
        RefreshStart();SetHomeVisible(true);
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.F1&&!UiProgress.IsInteractionBlocked){e.Handled=true;OpenTour();}};
    }
    private void SetHomeVisible(bool visible)
    {
        bool changed=(HomeHost.Visibility==Visibility.Visible)!=visible;
        HomeHost.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        EditorShell.IsEnabled=!visible;
        if(changed&&IsActive)Dispatcher.BeginInvoke(()=>{if(!_closed)(visible?(UIElement)HomeHost:EditorShell).MoveFocus(new TraversalRequest(FocusNavigationDirection.First));});
    }
    private void RefreshStart()=>HomeHost.Content=new StartView(_session,_simplified,ChooseExperience,
        ()=>OpenProject_Click(OpenProjectButton,new RoutedEventArgs()),OpenGuided,()=>OpenTour("Home"));
    private void Home_Click(object sender,RoutedEventArgs e)
    {
        if(_closed||UiProgress.IsInteractionBlocked)return;
        RefreshStart();SetHomeVisible(true);
    }
    internal void ChooseExperience(bool simplified)
    {
        _simplified=simplified;ApplyExperienceNavigation();
        if(_session is not null)SetHomeVisible(false);else RefreshStart();
    }
    private void ApplyExperienceNavigation()
    {
        ModeButton.Content=_simplified?"Modo simplificado · Cambiar":"Modo completo · Cambiar";
        foreach(var button in AdvancedNavigation)button.Visibility=_simplified?Visibility.Collapsed:Visibility.Visible;
        if(_simplified&&_recordView?.FindName("OptionsPanel") is Panel options)
            foreach(var extra in options.Children.OfType<Expander>().Skip(1))extra.SetCurrentValue(Expander.IsExpandedProperty,false);
        // Existing per-game enabled states and all editor objects remain untouched.
    }
    private void Tour_Click(object sender,RoutedEventArgs e)=>OpenTour();
    private SpotlightTour? _tour;
    private int _tourRequest;
    private void OpenTour(string? route=null)
    {
        if(_closed||UiProgress.IsInteractionBlocked)return;
        _tour?.Finish();
        bool home=HomeHost.Visibility==Visibility.Visible;
        var steps=new List<SpotlightStep>();
        string current=route??(home?"Home":_activeSection);
        steps.Add(new(BrandHomeButton,"Volver a Inicio","Pulsa el logo o el nombre de Definitive Randomizer para volver a Inicio. Allí están los modos, el randomizado guiado y la vista guiada."));
        if(home&&HomeHost.Content is FrameworkElement start)
            steps.AddRange(SpotlightTour.ForContent(start,"Inicio","Elige Modo simplificado, Modo completo o Randomizado guiado. La vista guiada es opcional; Saltar todo termina el recorrido.").Select(step=>step with {Prepare=()=>SetHomeVisible(true)}));
        if(_session is not null)
        {
            var available=GuidanceCatalog.Available(_session).ToDictionary(t=>t.Route);
            var buttons=new[]{PokemonNavButton,TrainersButton,EncountersButton,StaticEncountersButton,MovesButton,LevelUpButton,MachineTutorButton,ItemsButton,MartsButton,FieldItemsButton,EvolutionsButton,GameTextButton,StoryTextButton,MechanicsButton,PokemonToolsButton,RomToolsButton,AutomationButton,ConfigurationButton,TemplateStudioButton,GlobalTemplateButton,BatchRomButton,HistoryButton};
            if(!home)
            {
                FrameworkElement? editor=current=="Pokemon"?EditorContent:SectionHost.Content as FrameworkElement;
                if(editor is not null)
                {
                    var topic=GuidanceCatalog.Find(current);
                    steps.AddRange(SpotlightTour.ForContent(editor,topic.Title,topic.Steps[0].Text));
                    if(current=="Pokemon")
                    {
                        steps.Add(new(PokemonListPanel,"Especies y formas",topic.Steps[0].Text));
                        steps.AddRange(SpotlightTour.ForContent(AdaptiveInspector,"Inspector",topic.Steps[1].Text));
                    }
                }
            }
            foreach(var button in buttons.Where(b=>b.Visibility==Visibility.Visible&&b.IsEnabled))
            {
                string key=ReferenceEquals(button,PokemonNavButton)?"Pokemon":ReferenceEquals(button,MovesButton)?"Moves":button.Tag as string??"";
                if(!available.TryGetValue(key,out var topic))continue;
                string target=key;
                steps.Add(new(button,topic.Title,string.Join("\n\n",topic.Steps.Select(s=>s.Text)),()=>ExploreTour(target),()=>SetHomeVisible(false)));
            }
        }
        TourHost.Visibility=Visibility.Visible;
        _tour=new SpotlightTour((FrameworkElement)Content,steps,()=>{_tour=null;TourHost.Children.Clear();TourHost.Visibility=Visibility.Collapsed;if(home)SetHomeVisible(true);});
        TourHost.Children.Add(_tour);
    }
    private async void ExploreTour(string route)
    {
        int request=++_tourRequest;
        GuidanceNavigate(route);
        for(int i=0;i<450&&!_closed&&request==_tourRequest;i++)
        {
            await Task.Delay(80);
            if(_activeSection!=route)continue;
            if(SectionHost.Content is FrameworkElement content)content.UpdateLayout();
            OpenTour(route);return;
        }
    }
    private void GuidanceNavigate(string route)
    {
        if(route=="Home"){Home_Click(HomeButton,new RoutedEventArgs());return;}
        if(_session is null&&route!="Extraction")return;
        if(!GuidanceCatalog.SimpleRoutes.Contains(route))ChooseExperience(false);
        SetHomeVisible(false);
        if(route=="Pokemon"){Pokemon_Click(PokemonNavButton,new RoutedEventArgs());return;}
        if(route=="Moves"){Moves_Click(MovesButton,new RoutedEventArgs());return;}
        if(route=="Extraction"||route=="PlayerCaps"){NavigateSystem(route);return;}
        var buttons=new[]{TrainersButton,EncountersButton,StaticEncountersButton,LevelUpButton,MachineTutorButton,ItemsButton,MartsButton,FieldItemsButton,EvolutionsButton,GameTextButton,StoryTextButton,MechanicsButton,PokemonToolsButton,RomToolsButton,AutomationButton,ConfigurationButton,TemplateStudioButton,GlobalTemplateButton,BatchRomButton,HistoryButton};
        var original=buttons.FirstOrDefault(button=>button.Tag is string tag&&tag==route);
        if(original is not null){if(original.IsEnabled)original.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return;}
        Section_Click(new Button{Tag=route},new RoutedEventArgs());
    }
    private void Guided_Click(object sender,RoutedEventArgs e)=>OpenGuided();
    private GuidedRandomizationWindow? _guidedWindow;
    private void OpenGuided()
    {
        if(_closed||_session is null||UiProgress.IsInteractionBlocked||!ConfirmDiscard()||!ConfirmAuxiliaryLeave())return;
        if(HasUnsavedChanges()&&_previous is not null)ShowEntry(_previous);
        _guidedWindow=new GuidedRandomizationWindow(_session){Owner=this};
        try{_guidedWindow.ShowDialog();}finally{_guidedWindow=null;}
    }
}
