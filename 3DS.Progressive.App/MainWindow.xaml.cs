using System.IO;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using System.Threading;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

namespace ThreeDS.Progressive;

public partial class MainWindow : Window
{
    private readonly BatchToolsView _personalTools = new();
    private GameProjectSession? _session;
    private PokemonListEntry? _previous;
    private PersonalValues? _original;
    private bool _suppressSelection;
    private int _currentIndex = -1;
    private bool _loadingEntry;
    private readonly SpriteService _sprites = new();
    private CancellationTokenSource? _spriteCancellation;
    private MoveEditorView? _movesView;
    private bool _showingMoves;
    private MachineCompatibilityState? _gen6CompatibilityState;
    private TutorCompatibilityState? _gen6TutorCompatibilityState;
    private RecordEditorView? _recordView;
    private Gen7MartState? _gen7MartState;
    private MartEditorView CreateMartView(){var view=new MartEditorView(_session!);if(_gen7MartState is not null)view.RestoreRecipe(_gen7MartState);return view;}
    private Gen7MachineState? _gen7MachineState;
    private MachineTutorView? _machineTutorView;
    private MachineTutorView CreateMachineTutorView(){var view=new MachineTutorView(_session!);if(_gen7MachineState is not null)view.RestoreRecipe(_gen7MachineState);return view;}
    private MartEditorView? _martView;
    private Gen6MartView? _mart6View;
    private LevelUpView? _levelUpView;
    private PlayerLevelCapsView? _playerCapsView;
    private CroRecipesView? _croRecipesView;
    private UsumRelearnerView? _relearnerView;
    private TypeChartView? _typeChartView;
    private ShinyRateView? _shinyRateView;
    private ShinyRateOptions? _shinyOptions;
    private RomRebuildView? _romRebuildView;
    private CroIntegrityView? _croIntegrityView;
    private CatchZonesView? _catchZonesView;
    private PickupView? _pickupView;
    private RomExtractionView? _romExtractionView;
    private TemplateStudioView? _templateStudioView;
    private GlobalTemplateView? _globalTemplateView;
    private BatchRomBuilderView? _batchRomView;
    private GameTextView? _gameTextView;
    private GameTextView? _storyTextView;
    private readonly Dictionary<string,RecordEditorView.UiState> _sectionOptions=[];
    private readonly System.Windows.Threading.DispatcherTimer _operationTimer=new()
        {Interval=TimeSpan.FromSeconds(1)};
    private bool _globalOperationActive;
    private bool _backupRunning;
    private IReadOnlyList<MoveChoice> _itemOptions = [];
    private IReadOnlyList<MoveChoice> _moveOptions = [];
    private IReadOnlyList<MoveChoice> _eggOptions = [];
    private IReadOnlyList<MoveChoice> _growthOptions = [];
    private readonly ObservableCollection<FlagChoice> _tmChoices = [];
    private readonly ObservableCollection<FlagChoice> _tutorChoices = [];
    private readonly ObservableCollection<FlagChoice> _specialChoices = [];

    public MainWindow()
    {
        InitializeComponent();
        Closed+=(_,_)=>_sectionLoadCancellation?.Cancel();
        UiProgress.InteractionLockChanged+=blocked=>
        {
            void Apply(){if(Content is UIElement shell)shell.IsEnabled=!blocked;}
            if(Dispatcher.CheckAccess())Apply();else Dispatcher.Invoke(Apply);
        };
        UiProgress.Changed+=(message,busy)=>
        {
            if(!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(()=>
                {
                    OperationProgress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
                    OperationProgress.IsIndeterminate=true;
                    OperationPercent.Visibility=Visibility.Collapsed;
                    if(busy)StatusText.Text=message;
                });
                return;
            }
            OperationProgress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
            OperationProgress.IsIndeterminate=true;
            OperationPercent.Visibility=Visibility.Collapsed;
            if(busy)StatusText.Text=message;
        };
        UiProgress.Reported+=(message,percentage)=>
        {
            void Update()
            {
                OperationProgress.Visibility=Visibility.Visible;
                OperationProgress.IsIndeterminate=false;
                OperationProgress.Value=percentage;
                OperationPercent.Visibility=Visibility.Visible;
                OperationPercent.Text=$"{percentage:0.0}%";
                StatusText.Text=message;
            }
            if(Dispatcher.CheckAccess())Update();
            else Dispatcher.BeginInvoke((Action)Update);
        };
        UiProgress.Telemetry+=snapshot=>
        {
            void Update()
            {
                _globalOperationActive=snapshot.Busy;
                OperationStageText.Visibility=snapshot.Busy?Visibility.Visible:Visibility.Collapsed;
                OperationElapsedText.Visibility=snapshot.Busy?Visibility.Visible:Visibility.Collapsed;
                if(snapshot.Busy)
                {
                    OperationStageText.Text=snapshot.Stage+
                        (snapshot.Completed is int count&&snapshot.Total is int total
                            ?$" · {count:N0}/{total:N0}":"");
                    OperationElapsedText.Text=snapshot.Elapsed.ToString(@"mm\:ss");
                    if(snapshot.Percent is double percent)
                    {
                        OperationProgress.IsIndeterminate=false;
                        OperationProgress.Value=percent;
                        OperationPercent.Visibility=Visibility.Visible;
                        OperationPercent.Text=$"{percent:0.#}%";
                    }
                    else
                    {
                        OperationProgress.IsIndeterminate=true;
                        OperationPercent.Visibility=Visibility.Collapsed;
                    }
                    if(!_operationTimer.IsEnabled)_operationTimer.Start();
                }
                else _operationTimer.Stop();
            }
            if(Dispatcher.CheckAccess())Update();
            else Dispatcher.BeginInvoke((Action)Update);
        };
        _operationTimer.Tick+=(_,_) =>
        {
            if(_globalOperationActive)
                OperationElapsedText.Text=UiProgress.Snapshot.Elapsed.ToString(@"mm\:ss");
        };
        PersonalToolsHost.Content = _personalTools;
        _personalTools.BackRequested += ClosePersonalTools;
        TmFlagsList.ItemsSource = _tmChoices;
        TutorFlagsList.ItemsSource = _tutorChoices;
        SpecialTutorFlagsList.ItemsSource = _specialChoices;
    }

    private void ConfigurePersonalTools(GameProjectSession project)
    {
        _personalTools.Configure(false, project.Generation, project.SupportsHms,
            project.SupportsSpecialTutors, project.TrappingAbilitiesBanned,
            () =>
            {
                if (!HasUnsavedChanges()) return true;
                if (!ConfirmDiscard()) return false;
                if (_previous is not null) ShowEntry(_previous);
                return true;
            },
            scope => scope switch
            {
                1 => PokemonList.Items.Cast<PokemonListEntry>().Select(p => p.Index).ToArray(),
                2 => _currentIndex > 0 ? new[] { _currentIndex } : Array.Empty<int>(),
                _ => project.Entries.Select(p => p.Index).ToArray(),
            }, project.PreviewPersonal,
            preview =>
            {
                project.ApplyPersonalPreview(preview);
                if (_previous is not null) ShowEntry(_previous);
                StatusText.Text = $"{preview.Action}: {preview.ChangedCount} entradas guardadas y verificadas.";
            }, project.ExportPersonalTable);
    }

    private void PersonalAction_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || sender is not Button { Tag: string action }) return;
        bool pending = HasUnsavedChanges();
        if (!ConfirmDiscard()) return;
        if (pending && _previous is not null) ShowEntry(_previous);
        _personalTools.OpenAction(Enum.Parse<BatchAction>(action));
        PokemonEditorPanel.Visibility = Visibility.Collapsed;
        PersonalToolsPanel.Visibility = Visibility.Visible;
    }

    private void ClosePersonalTools()
    {
        PersonalToolsPanel.Visibility = Visibility.Collapsed;
        PokemonEditorPanel.Visibility = Visibility.Visible;
    }

    private void ExtractRom_Click(object sender,RoutedEventArgs e)
    {
        if(!ConfirmDiscard()||!ConfirmAuxiliaryLeave())return;
        _romExtractionView=new RomExtractionView(_session?.ProjectRoot);
        SectionHost.Content=_romExtractionView;
        _recordView=null;_machineTutorView=null;_martView=null;_mart6View=null;_levelUpView=null;_gameTextView=null;_storyTextView=null;
        _playerCapsView=null;_croRecipesView=null;_relearnerView=null;_typeChartView=null;_shinyRateView=null;
        _romRebuildView=null;_croIntegrityView=null;_catchZonesView=null;_pickupView=null;_globalTemplateView=null;_templateStudioView=null;_batchRomView=null;
        PokemonListPanel.Visibility=PokemonEditorPanel.Visibility=PersonalToolsPanel.Visibility=
            PokemonInspectorPanel.Visibility=MovesHost.Visibility=Visibility.Collapsed;
        SectionHost.Visibility=Visibility.Visible;_showingMoves=false;
        StatusText.Text="Extracción de ROM descifrada en una carpeta nueva.";
    }
    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard() || !ConfirmAuxiliaryLeave())
            return;

        var preferences=AppPreferences.Load();
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath=Directory.Exists(preferences.DefaultProjectFolder)
                ?preferences.DefaultProjectFolder:string.Empty,
            Description = "Selecciona la carpeta RAIZ del juego extraido (RomFS + ExeFS).",
            ShowNewFolderButton = false,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        UiProgress.Begin("Leyendo los archivos del proyecto y construyendo catálogos…");
        try
        {
            var candidate = await Task.Run(()=>GameProjectSession.Open(dialog.SelectedPath));
            LoadProject(candidate);
            preferences.DefaultProjectFolder=dialog.SelectedPath;
            preferences.Save();
        }
        catch (Exception ex)
        {
            StudioDialog.Error(this, "No se pudo abrir el proyecto", ex.Message);
            StatusText.Text = "Error al abrir proyecto: " + ex.Message;
        }
        finally{UiProgress.End();}
    }

    private bool ConfirmAuxiliaryLeave()
    {
        if(SectionHost.Content is TitleScreenView titleScreen&&!titleScreen.ConfirmLeave())return false;
        if(SectionHost.Content is TitleResourcesView titleResources&&!titleResources.ConfirmLeave())return false;
        if(SectionHost.Content is GarcShuffleView shuffle&&!shuffle.ConfirmLeave())return false;
        if(SectionHost.Content is IconView icon&&!icon.ConfirmLeave())return false;
        if(SectionHost.Content is SelectedRestoreView selectedRestore&&!selectedRestore.ConfirmLeave())return false;
        if(_recordView is not null&&!_recordView.ConfirmLeave())return false;
        if(_machineTutorView is not null&&!_machineTutorView.ConfirmLeave())return false;
        if(_martView is not null&&!_martView.ConfirmLeave())return false;
        if(_mart6View is not null&&!_mart6View.ConfirmLeave())return false;
        if(_levelUpView is not null&&!_levelUpView.ConfirmLeave())return false;
        if(_gameTextView is not null&&!_gameTextView.ConfirmLeave())return false;
        if(_storyTextView is not null&&!_storyTextView.ConfirmLeave())return false;
        if(_playerCapsView is not null&&!_playerCapsView.ConfirmLeave())return false;
        if(_croRecipesView is not null&&!_croRecipesView.ConfirmLeave())return false;
        if(_relearnerView is not null&&!_relearnerView.ConfirmLeave())return false;
        if(_typeChartView is not null&&!_typeChartView.ConfirmLeave())return false;
        if(_shinyRateView is not null&&!_shinyRateView.ConfirmLeave())return false;
        if(_romRebuildView is not null&&!_romRebuildView.ConfirmLeave())return false;
        if(_pickupView is not null&&!_pickupView.ConfirmLeave())return false;
        if(_croIntegrityView is not null&&!_croIntegrityView.ConfirmLeave())return false;
        if(_catchZonesView is not null&&!_catchZonesView.ConfirmLeave())return false;
        if(_romExtractionView is not null&&!_romExtractionView.ConfirmLeave())return false;
        if(_martView is not null)_gen7MartState=_martView.RememberRecipe();
        if(_machineTutorView is not null)_gen7MachineState=_machineTutorView.RememberRecipe();
        if(_shinyRateView is not null)_shinyOptions=_shinyRateView.RememberOptions();
        if(SectionHost.Content is Gen6TutorView tutors)_gen6TutorCompatibilityState=tutors.RememberCompatibility();
        if(SectionHost.Content is Gen6MachineView machines)_gen6CompatibilityState=machines.RememberCompatibility();
        if(_recordView is not null)_sectionOptions[_recordView.Editor.Title]=_recordView.RememberOptions();
        if(_mart6View is not null)_sectionOptions[_mart6View.Editor.Title]=_mart6View.RememberOptions();
        if(_showingMoves&&_movesView is not null)
        {
            bool pending=_movesView.HasPendingChanges;
            if(!_movesView.ConfirmLeave())return false;
            if(pending){_movesView=null;MovesHost.Content=null;}
        }
        return true;
    }

    private int _sectionLoadVersion;
    private CancellationTokenSource? _sectionLoadCancellation;
    private async void Section_Click(object sender,RoutedEventArgs e)
    {
        if(_session is null||sender is not Button {Tag:string section})return;
        if(!ProjectFeatureAvailability.Supports(_session,section))return;
        if(!ConfirmDiscard()||!ConfirmAuxiliaryLeave())return;
        if(HasUnsavedChanges()&&_previous is not null)ShowEntry(_previous);
        if(section=="Rebuild")
        {
            try{StudioDialog.Notice(this,"Rebuild · Historial de cambios",
                "Se empaquetarán los archivos actuales del proyecto. Este historial muestra cambios ya guardados; no se vuelven a ejecutar ni randomizar.",
                RomRebuildView.HistorySummary(_session.History.Read()));}
            catch(Exception ex){StudioDialog.Error(this,"No se pudo leer el historial",ex.Message);return;}
        }
        using var interaction=section is "Encounters" or "FieldItems"?UiProgress.BlockInteraction():null;
        _sectionLoadCancellation?.Cancel();
        var cancellation=new CancellationTokenSource();_sectionLoadCancellation=cancellation;
        int loadVersion=++_sectionLoadVersion;
        UiProgress.Begin("Cargando "+section+"…");
        if(!StudioDialog.AutomatedChecks)
            await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Background);
        try
        {
            GameEncounterService? encounterEditor=null;
            IProjectEditor? fieldEditor=null;
            if(section=="Encounters")
            {
                var session=_session;
                encounterEditor=StudioDialog.AutomatedChecks?new GameEncounterService(session,cancellation.Token):
                    await Task.Run(()=>new GameEncounterService(session,cancellation.Token),cancellation.Token);
                if(loadVersion!=_sectionLoadVersion||!ReferenceEquals(_session,session))return;
            }
            if(section=="FieldItems")
            {
                var session=_session;
                IProjectEditor Load()=>session.Generation==6?new GameFieldItems6Service(session,cancellation.Token):new GameFieldItemsService(session,cancellation.Token);
                fieldEditor=StudioDialog.AutomatedChecks?Load():await Task.Run(Load,cancellation.Token);
                if(loadVersion!=_sectionLoadVersion||!ReferenceEquals(_session,session))return;
            }
            UserControl view=section switch
            {
                "Trainers"=>CreateEditor(new GameTrainerService(_session)),
                "Encounters"=>CreateEditor(encounterEditor!),
                "StaticEncounters"=>CreateEditor(_session.Generation==6 ? new GameStaticEncounter6Service(_session) : new GameStaticEncounterService(_session)),
                "Tutor6" when _session.GameConfiguration.ORAS=>new Gen6TutorView(_session,CreateEditor(new GameTutor6Service(_session)),_gen6TutorCompatibilityState),
                "MachineTutor"=>_session.Generation==6 ? new Gen6MachineView(_session,CreateEditor(new GameMachine6Service(_session)),_gen6CompatibilityState) : _machineTutorView=CreateMachineTutorView(),
                "LevelUp"=>_levelUpView=new LevelUpView(_session),
                "Items"=>CreateEditor(new GameItemService(_session)),
                "PersistentItems" when _session.GameConfiguration.USUM=>CreateEditor(new GamePersistentItemsService(_session)),
                "Marts"=>_session.Generation==6 ? _mart6View=new Gen6MartView(_session,_sectionOptions.GetValueOrDefault("Tiendas · Gen6")) : _martView=CreateMartView(),
                "FieldItems"=>CreateEditor(fieldEditor!),
                "GameText"=>_gameTextView=new GameTextView(_session),
                "StoryText"=>_storyTextView=new GameTextView(_session,story:true),
                "Evolutions"=>CreateEditor(new GameEvolutionService(_session)),
                "EggMoves"=>CreateEditor(new GameEggMoveService(_session)),
                "MegaEvolutions"=>CreateEditor(new GameMegaEvolutionService(_session)),
                "FacilityNormal"=>CreateEditor(new GameBattleFacilityService(_session,false)),
                "FacilityAlternate"=>CreateEditor(new GameBattleFacilityService(_session,true)),
                "TypeChart"=>_typeChartView=new TypeChartView(_session),
                "ShinyRate"=>_shinyRateView=new ShinyRateView(_session,_shinyOptions),
                "PlayerCaps"=>new PlayerLevelCapsView(_session),
                "CroRecipes"=>_croRecipesView=new CroRecipesView(_session),
                "UsumRelearner"=>_relearnerView=new UsumRelearnerView(_session),
                "TemplateStudio"=>_templateStudioView??=new TemplateStudioView(_session),
                "GlobalTemplate"=>_globalTemplateView??=new GlobalTemplateView(_session,RestoreGlobalOptions,CaptureGlobalOptions,RestoreGlobalOptionsAsync),
                "BatchRom"=>_batchRomView??=new BatchRomBuilderView(_session,
                    _globalTemplateView?.LoadedSourcePath),

                "OWSE"=>new OwseDevView(_session),
                "Rebuild"=>_romRebuildView=new RomRebuildView(_session),
                "CroIntegrity"=>_croIntegrityView=new CroIntegrityView(_session),
                "GarcShuffle" when _session.Generation==6=>new GarcShuffleView(_session),
                "TitleScreen6" when _session.Generation==6=>new TitleResourcesView(_session),
                "TitleScreen7" when _session.Generation==7=>new TitleResourcesView(_session),
                "Graphics"=>new GraphicsView(),
                "SMDH"=>new IconView(_session),
                "RestoreSelected"=>new SelectedRestoreView(_session,ReloadAfterSelectedRestore),
                "CatchZones"=>_catchZonesView=new CatchZonesView(_session),
                "Pickup"=>_pickupView=new PickupView(_session),
                "OPower"=>CreateEditor(new GameOPowerService(_session)),
                "Trade6"=>CreateEditor(new GameTrade6Service(_session)),
                "PokemonTools"=>new SystemsView(_session,"PokemonTools",NavigateSystem,
                    ()=>Backup_Click(BackupButton,new RoutedEventArgs()),OpenAutomatedPokemon),
                "Mechanics"=>new SystemsView(_session,"Mechanics",NavigateSystem,
                    ()=>Backup_Click(BackupButton,new RoutedEventArgs()),OpenAutomatedPokemon),
                "RomTools"=>new SystemsView(_session,"RomTools",NavigateSystem,
                    ()=>Backup_Click(BackupButton,new RoutedEventArgs()),OpenAutomatedPokemon),
                "Automation"=>new SystemsView(_session,"Automation",NavigateSystem,
                    ()=>Backup_Click(BackupButton,new RoutedEventArgs()),OpenAutomatedPokemon),
                "Configuration"=>new SystemsView(_session,"Configuration",NavigateSystem,
                    ()=>Backup_Click(BackupButton,new RoutedEventArgs()),OpenAutomatedPokemon),
                "History"=>new HistoryView(_session.History),
                _=>throw new ArgumentException("Sección desconocida.")
            };
            _recordView=view is Gen6MachineView gen6Machines?gen6Machines.Editor:view is Gen6TutorView gen6Tutors?gen6Tutors.Editor:view as RecordEditorView;_playerCapsView=null;if(section!="CroRecipes")_croRecipesView=null;if(section!="UsumRelearner")_relearnerView=null;if(section!="TypeChart")_typeChartView=null;if(section!="ShinyRate")_shinyRateView=null;if(section!="Rebuild")_romRebuildView=null;if(section!="Pickup")_pickupView=null;if(section!="CroIntegrity")_croIntegrityView=null;if(section!="CatchZones")_catchZonesView=null;_romExtractionView=null; if(section!="MachineTutor")_machineTutorView=null; if(section!="Marts"||_session.Generation==6)_martView=null;if(section!="Marts"||_session.Generation==7)_mart6View=null; if(section!="LevelUp")_levelUpView=null; if(section!="GameText")_gameTextView=null;if(section!="StoryText")_storyTextView=null;SectionHost.Content=view;
            PokemonListPanel.Visibility=PokemonEditorPanel.Visibility=PersonalToolsPanel.Visibility=PokemonInspectorPanel.Visibility=MovesHost.Visibility=Visibility.Collapsed;
            _personalTools.InvalidatePreview();SectionHost.Visibility=Visibility.Visible;_showingMoves=false;
            PokemonNavButton.Background=MovesButton.Background=TrainersButton.Background=EncountersButton.Background=StaticEncountersButton.Background=MachineTutorButton.Background=LevelUpButton.Background=ItemsButton.Background=MartsButton.Background=FieldItemsButton.Background=GameTextButton.Background=StoryTextButton.Background=EvolutionsButton.Background=HistoryButton.Background=TemplateStudioButton.Background=GlobalTemplateButton.Background=BatchRomButton.Background=PokemonToolsButton.Background=MechanicsButton.Background=RomToolsButton.Background=AutomationButton.Background=ConfigurationButton.Background=Brushes.Transparent;
            ((Button)sender).Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B4059")!);
            StatusText.Text=section=="History"?"Historial persistente de cambios confirmados.":"Editor integrado · guardar registra los cambios en el historial.";
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
        catch(Exception ex){StudioDialog.Error(this,"No se pudo cargar la sección",ex.Message);}
        finally{if(ReferenceEquals(_sectionLoadCancellation,cancellation))_sectionLoadCancellation=null;cancellation.Dispose();UiProgress.End();}
    }

    private void NavigateSystem(string section)
    {
        if(section=="Extraction"){ExtractRom_Click(ExtractRomButton,new RoutedEventArgs());return;}
        if(_session is null)return;
        if(section=="ReloadLanguage")
        {
            _=ReloadProjectLanguageAsync();
            return;
        }
        var target=section switch
        {
            "Trainers"=>TrainersButton,"Encounters"=>EncountersButton,
            "StaticEncounters"=>StaticEncountersButton,"MachineTutor"=>MachineTutorButton,
            "LevelUp"=>LevelUpButton,"Items"=>ItemsButton,"Marts"=>MartsButton,"GameText"=>GameTextButton,"StoryText"=>StoryTextButton,
            "History"=>HistoryButton,"PlayerCaps"=>MechanicsButton,
            "GlobalTemplate"=>GlobalTemplateButton,"BatchRom"=>BatchRomButton,
            _=>null
        };
        if(_session is null||!ProjectFeatureAvailability.Supports(_session,section))return;
        if(section=="PlayerCaps")
        {
            // The Player Caps editor is hosted as its own full-width view.
            if(!ConfirmDiscard()||!ConfirmAuxiliaryLeave())return;
            var view=_playerCapsView=new PlayerLevelCapsView(_session);
            _recordView=null;_machineTutorView=null;_martView=null;_mart6View=null;_levelUpView=null;_gameTextView=null;_storyTextView=null;
            SectionHost.Content=view;
            PokemonListPanel.Visibility=PokemonEditorPanel.Visibility=PersonalToolsPanel.Visibility=
                PokemonInspectorPanel.Visibility=MovesHost.Visibility=Visibility.Collapsed;
            SectionHost.Visibility=Visibility.Visible;
            return;
        }
        if(section is "TitleScreen6" or "TitleScreen7" or "Tutor6" or "RestoreSelected" or "Graphics" or "SMDH" or "GarcShuffle")
        {Section_Click(new Button{Tag=section},new RoutedEventArgs());return;}
        if(section is "FacilityNormal" or "FacilityAlternate")
        {Section_Click(new Button{Tag=section},new RoutedEventArgs());return;}
        if(section is "CroRecipes" or "UsumRelearner" or "EggMoves" or "MegaEvolutions" or "TypeChart" or "ShinyRate" or "OWSE" or "Rebuild" or "CroIntegrity" or "Pickup" or "OPower" or "CatchZones" or "Trade6")
        {
            if(section is "EggMoves" or "MegaEvolutions" or "TypeChart" or "ShinyRate" or "OWSE" or "Rebuild" or "CroIntegrity" or "Pickup" or "OPower" or "CatchZones" or "Trade6"||_session.GameConfiguration.USUM)
                Section_Click(new Button{Tag=section},new RoutedEventArgs());
            return;
        }
        target?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private async Task ReloadProjectLanguageAsync()
    {
        if(_session is null||!ConfirmDiscard()||!ConfirmAuxiliaryLeave())return;
        string root=_session.ProjectRoot;
        UiProgress.Begin("Options · Cargando textos del idioma seleccionado…");
        UiProgress.Stage("Releyendo catálogos originales del juego…");
        try
        {
            var candidate=await Task.Run(()=>GameProjectSession.Open(root));
            LoadProject(candidate);
            StatusText.Text="Idioma de datos aplicado: "+
                AppPreferences.Load().GameTextLanguageIndex+". No se editó la ROM.";
        }
        catch(Exception ex)
        {
            StudioDialog.Error(this,"Idioma del juego",ex.Message);
        }
        finally{UiProgress.End();}
    }
    private void OpenAutomatedPokemon()
    {
        if(_session is null||!ConfirmAuxiliaryLeave())return;
        DisplayPokemon();
        PersonalAction_Click(new Button{Tag="PersonalRandomize"},new RoutedEventArgs());
    }
    private RecordEditorView CreateEditor(IProjectEditor editor)
    {
        var view=new RecordEditorView(editor,_sectionOptions.GetValueOrDefault(editor.Title));
        if(editor is GameFieldItems6Service fieldItems)
        {
            var export=(Button)view.FindName("FieldExportButton");export.Visibility=Visibility.Visible;
            export.Click+=(_,_)=>
            {
                try
                {
                    var picker=new Microsoft.Win32.SaveFileDialog{Filter="CSV|*.csv",FileName="field-items-gen6.csv"};
                    if(picker.ShowDialog(this)!=true)return;
                    File.WriteAllText(picker.FileName,fieldItems.ExportCsv(),System.Text.Encoding.UTF8);
                    ((TextBlock)view.FindName("StatusText")).Text="Objetos de campo Gen6 exportados a CSV.";
                }
                catch(Exception ex){StudioDialog.Error(this,"Error al exportar objetos de campo",ex.Message);}
            };
        }
        if(editor is GameBattleFacilityService facility)
        {
            var export=(Button)view.FindName("FieldExportButton");export.Visibility=Visibility.Visible;export.Content="Exportar instalación…";
            export.Click+=(_,_)=>
            {
                try{var picker=new Microsoft.Win32.SaveFileDialog{Filter="Texto|*.txt",FileName="battle-facility.txt"};if(picker.ShowDialog(this)==true)File.WriteAllText(picker.FileName,facility.ExportText(),System.Text.Encoding.UTF8);}
                catch(Exception ex){StudioDialog.Error(this,"Error al exportar instalación",ex.Message);}
            };
        }
        if(editor is GameOPowerService or GameBattleFacilityService)
        {
            ((FrameworkElement)view.FindName("RandomizerButton")).Visibility=Visibility.Collapsed;
            var scope=(FrameworkElement)view.FindName("ScopeBox");scope.Visibility=Visibility.Collapsed;
            if(scope.Parent is System.Windows.Controls.Panel host)
            {
                int index=host.Children.IndexOf(scope);if(index>0)host.Children[index-1].Visibility=Visibility.Collapsed;
                if(index+1<host.Children.Count&&host.Children[index+1] is TextBlock hint)hint.Text="Los cambios confirmados se verifican y quedan registrados en el historial.";
            }
        }
        return view;
    }

    private void RestoreGlobalOptions(GlobalTemplateDocument document)=>PrepareGlobalOptions(document)();
    private async Task RestoreGlobalOptionsAsync(GlobalTemplateDocument document)
    {
        var apply=await Task.Run(()=>PrepareGlobalOptions(document));
        apply();
    }
    private Action PrepareGlobalOptions(GlobalTemplateDocument document)
    {
        if(_session is null)return ()=>{};
        NativeMasterBatchBridge.ValidateSession(_session,document,forReplay:false);
        NativeGen7MachineBridge.ValidateSession(_session,document,forReplay:false);
        var machineRecipe=NativeGen7MachineBridge.Read(document);
        _=NativeGen7MartBridge.ValidateSession(_session,document,forReplay:false);var martRecipe=NativeGen7MartBridge.Read(document);
        var personalMaster=NativeMasterBatchBridge.Read(document,"Personal");var movesMaster=NativeMasterBatchBridge.Read(document,"Moves");
        // Validate on isolated services before replacing any remembered UI state.
        var editors=GlobalEditorTemplateBridge.Editors(_session)
            .Where(row=>GlobalEditorTemplateBridge.HasSettings(document,row.Key,_session.Generation)).Select(row=>row.Create()).ToArray();
        var shiny=ShinyRateTemplate.Read(document);
        if(shiny is not null)_=new GameShinyRateService(_session).Preview(shiny.Rerolls,shiny.Everything);
        var compatibility=_session.Generation==6?MachineCompatibilityTemplate.Read(document):null;
        var tutorCompatibility=_session.GameConfiguration.ORAS?TutorCompatibilityTemplate.Read(document):null;
        var states=editors.Select(editor=>(editor.Title,State:GlobalEditorTemplateBridge.Restore(document,editor,_session.Generation))).ToArray();
        return ()=>
        {
        if(martRecipe is not null){_gen7MartState=martRecipe;_martView?.RestoreRecipe(martRecipe);}
        if(machineRecipe is not null){_gen7MachineState=machineRecipe;_machineTutorView?.RestoreRecipe(machineRecipe);}
        if(personalMaster is not null)_personalTools.RestoreGlobalState(personalMaster);
        if(movesMaster is not null){_movesView??=new MoveEditorView(_session);_movesView.RestoreGlobalState(movesMaster);}
        if(shiny is not null)_shinyOptions=shiny;
        if(compatibility is not null)_gen6CompatibilityState=compatibility;
        if(tutorCompatibility is not null)_gen6TutorCompatibilityState=tutorCompatibility;
        foreach(var (title,state) in states)if(state is not null)_sectionOptions[title]=state;
        };
    }
    private void CaptureGlobalOptions(GlobalTemplateDocument document)
    {
        if(_session is null)return;
        if(_mart6View is not null)_sectionOptions[_mart6View.Editor.Title]=_mart6View.RememberOptions();
        if(_personalTools.CaptureGlobalState() is {} personalMaster)NativeMasterBatchBridge.Capture(document,personalMaster,_session.Generation);
        if(_movesView?.CaptureGlobalState() is {} movesMaster)NativeMasterBatchBridge.Capture(document,movesMaster,_session.Generation);
        if(_shinyOptions is not null)ShinyRateTemplate.Capture(document,_shinyOptions);
        if(_session.Generation==7){if(_martView is not null)_gen7MartState=_martView.RememberRecipe();if(_gen7MartState is not null)NativeGen7MartBridge.Capture(document,_gen7MartState);}
        if(_session.Generation==7)
        {
            // New exports randomize MT by default, including before opening the editor.
            // Keep the reader/replay of older recipes faithful to their saved actions.
            var machines=_machineTutorView?.RememberRecipe()??_gen7MachineState??NativeGen7MachineBridge.Read(document)??new Gen7MachineState();
            _gen7MachineState=machines with{RandomMachines=true};
            NativeGen7MachineBridge.Capture(document,_gen7MachineState);
        }
        if(_session.Generation==6&&_gen6CompatibilityState is not null)MachineCompatibilityTemplate.Capture(document,_gen6CompatibilityState);
        if(_session.GameConfiguration.ORAS&&_gen6TutorCompatibilityState is not null)TutorCompatibilityTemplate.Capture(document,_gen6TutorCompatibilityState);
        foreach(var row in GlobalEditorTemplateBridge.Editors(_session))
        {
            if(_sectionOptions.TryGetValue(row.Title,out var state))
                GlobalEditorTemplateBridge.Capture(document,row.Create(),state,_session.Generation);
        }
    }

    private async Task ReloadAfterSelectedRestore()
    {
        if(_session is null)return;string root=_session.ProjectRoot;
        var restored=await Task.Run(()=>GameProjectSession.Open(root,recoverInterruptedSave:false));LoadProject(restored);
        StatusText.Text="Archivos seleccionados restaurados, verificados y registrados en el historial.";
    }
    private void Rebuild_Click(object sender,RoutedEventArgs e)=>NavigateSystem("Rebuild");

    private async void Backup_Click(object sender,RoutedEventArgs e)
    {
        if(_session is null||_backupRunning)return;
        var prefs=AppPreferences.Load();
        using var folder=new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath=Directory.Exists(prefs.DefaultBackupFolder)?prefs.DefaultBackupFolder:string.Empty,
            Description="Elegí la carpeta de destino del backup completo del proyecto.",
            ShowNewFolderButton=true
        };
        if(folder.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
        var project=_session;_backupRunning=true;IsEnabled=false;
        UiProgress.Begin("Generando y verificando backup del proyecto…");
        try
        {
            StatusText.Text="Generando backup de los archivos guardados…";
            var progress=new Progress<BackupProgress>(p=>
            UiProgress.Report($"Backup: {p.Completed}/{p.Total} · {p.File}",
                p.Total>0?100d*p.Completed/p.Total:0));
            string path=await ProjectBackup.CreateAsync(project.ProjectRoot,folder.SelectedPath,project.GameName,progress,history:project.History.Read());
            StatusText.Text="Backup completo y verificado: "+path;
            prefs.DefaultBackupFolder=folder.SelectedPath;prefs.Save();
            IsEnabled=true;StudioDialog.Notice(this,"Backup generado","Se guardó y verificó una copia del proyecto y su historial.",path);
        }
        catch(Exception ex){IsEnabled=true;StudioDialog.Error(this,"No se pudo generar el backup",ex.Message);StatusText.Text=ex.Message;}
        finally{_backupRunning=false;IsEnabled=true;UiProgress.End();}
    }
    private async void RestoreBackup_Click(object sender,RoutedEventArgs e)
    {
        if(_backupRunning||_globalOperationActive)return;
        var input=new Microsoft.Win32.OpenFileDialog{Filter="Backup Definitive Randomizer|*.zip",Title="Respaldo que se verificará antes de restaurar"};
        if(input.ShowDialog(this)!=true)return;
        _backupRunning=true;IsEnabled=false;UiProgress.Begin("Verificando respaldo completo…");
        try
        {
            var plan=await ProjectRestore.InspectAsync(input.FileName);
            IsEnabled=true;
            if(!StudioDialog.Confirm(this,"Respaldo verificado",$"{plan.Game} · {plan.Files:N0} archivos · {plan.Bytes:N0} bytes", "Se creará una carpeta nueva. El historial exportado se conserva como referencia y no sustituye el historial activo."))return;
            var folder=new Microsoft.Win32.OpenFolderDialog{Title="Carpeta donde se creará el proyecto restaurado"};if(folder.ShowDialog(this)!=true)return;
            string destination=Path.Combine(folder.FolderName,"Definitive_restaurado_"+DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            IsEnabled=false;var restored=await ProjectRestore.RestoreAsync(plan,destination);
            StatusText.Text="Respaldo restaurado y verificado: "+restored;
            IsEnabled=true;StudioDialog.Notice(this,"Restauración completada","Abrí el proyecto desde la carpeta restaurada.",restored);
        }
        catch(Exception ex){IsEnabled=true;StudioDialog.Error(this,"Restauración de respaldo",ex.Message);}
        finally{_backupRunning=false;IsEnabled=true;UiProgress.End();}
    }

    internal void LoadProject(GameProjectSession candidate)
    {
        _sectionLoadCancellation?.Cancel();++_sectionLoadVersion;
        _session = candidate;
        _sectionOptions.Clear();
        _recordView=null;_machineTutorView=null;_martView=null;_mart6View=null;_levelUpView=null;_gameTextView=null;_playerCapsView=null;_croRecipesView=null;_relearnerView=null;_typeChartView=null;_shinyRateView=null;_romRebuildView=null;_croIntegrityView=null;_catchZonesView=null;_pickupView=null;_romExtractionView=null;_globalTemplateView=null;_templateStudioView=null;_batchRomView=null;_gen7MartState=null;_gen7MachineState=null;_gen6CompatibilityState=null;_gen6TutorCompatibilityState=null;_shinyOptions=null;SectionHost.Content=null;
        _movesView = null;
        MovesHost.Content = null;
        DisplayPokemon();
        _previous = null;
        _original = null;
        _currentIndex = -1;
        _spriteCancellation?.Cancel();
        PokemonSprite.Source = null;
        SpriteFallback.Visibility = Visibility.Collapsed;
        SpriteStatusText.Text = "";
        _suppressSelection = true;
        SearchBox.Text = string.Empty;
        TypeOne.ItemsSource = candidate.Types;
        TypeTwo.ItemsSource = candidate.Types;
        AbilityOne.ItemsSource = candidate.AbilityNames;
        AbilityTwo.ItemsSource = candidate.AbilityNames;
        AbilityHidden.ItemsSource = candidate.AbilityNames;
        _itemOptions = candidate.ItemNames.Select((name, index) =>
            new MoveChoice(index, string.IsNullOrWhiteSpace(name)
                ? (index == 0 ? "Sin objeto" : $"Objeto ID {index}") : name)).ToArray();
        string[] eggs = ["—", "Monstruo", "Agua 1", "Bicho", "Volador", "Campo",
            "Hada", "Planta", "Humanoide", "Agua 3", "Mineral", "Amorfo",
            "Agua 2", "Ditto", "Dragón", "No descubierto"];
        _eggOptions = eggs.Select((name, index) => new MoveChoice(index, name)).ToArray();
        string[] growth = ["Medio rápido", "Errático", "Fluctuante",
            "Medio lento", "Rápido", "Lento"];
        _growthOptions = growth.Select((name, index) => new MoveChoice(index, name)).ToArray();
        foreach (var control in new[] { HeldItemOne, HeldItemTwo, HeldItemThree })
            control.ItemsSource = _itemOptions;
        EggOneBox.ItemsSource = _eggOptions;
        EggTwoBox.ItemsSource = _eggOptions;
        GrowthBox.ItemsSource = _growthOptions;
        _moveOptions = candidate.MoveNames.Select((name, index) => new MoveChoice(index,
            string.IsNullOrWhiteSpace(name) ? (index == 0 ? "Ninguno" : $"Movimiento ID {index}") : name)).ToArray();
        ZItemBox.ItemsSource = _itemOptions;
        ZBaseMoveBox.ItemsSource = _moveOptions;
        ZSpecialMoveBox.ItemsSource = _moveOptions;
        Gen7PersonalCard.Visibility = candidate.SupportsGen7Personal
            ? Visibility.Visible : Visibility.Collapsed;
        PokemonList.ItemsSource = candidate.Entries;
        _suppressSelection = false;

        ProjectNameText.Text = System.IO.Path.GetFileName(candidate.ProjectRoot);
        GameNameText.Text = "Juego: " + candidate.GameName + "  ·  Gen " + candidate.Generation;
        InspectorGameText.Text = candidate.GameName;
        EntryCountText.Text = candidate.Entries.Count + " entradas editables";
        EditorContent.IsEnabled = true;
        PersonalActions.IsEnabled = true;
        MovesButton.IsEnabled = true;
        TrainersButton.IsEnabled = EncountersButton.IsEnabled = EvolutionsButton.IsEnabled = HistoryButton.IsEnabled = BackupButton.IsEnabled = RebuildButton.IsEnabled = true;
        StaticEncountersButton.IsEnabled=candidate.Generation is 6 or 7;
        MachineTutorButton.IsEnabled=candidate.Generation is 6 or 7;
        LevelUpButton.IsEnabled=true;
        ItemsButton.IsEnabled=true;
        MartsButton.IsEnabled=ProjectFeatureAvailability.Supports(candidate,"Marts");
        MartsButton.Visibility=MartsButton.IsEnabled?Visibility.Visible:Visibility.Collapsed;
        MartsButton.Content=candidate.Generation==6?"▦  Tiendas · Gen6":candidate.GameConfiguration.USUM?"▦  Tiendas y BP · USUM":"▦  Tiendas y BP · SM";
        MachineTutorButton.Content=candidate.Generation==6?"▣  MT y MO · Gen6":"▣  MT y tutores";
        FieldItemsButton.IsEnabled=true;
        GameTextButton.IsEnabled=true;
        try
        {
            string storyPath=Path.Combine(candidate.GameConfiguration.RomFS,
                candidate.GameConfiguration.GetGARCFileName("storytext"));
            StoryTextButton.IsEnabled=File.Exists(storyPath);
        }
        catch { StoryTextButton.IsEnabled=false; }
        MechanicsButton.IsEnabled=PokemonToolsButton.IsEnabled=RomToolsButton.IsEnabled=AutomationButton.IsEnabled=ConfigurationButton.IsEnabled=true;
        TemplateStudioButton.IsEnabled=GlobalTemplateButton.IsEnabled=BatchRomButton.IsEnabled=true;
        ConfigurePersonalTools(candidate);
        PokemonList.SelectedIndex = 0;
        StatusText.Text = "Proyecto abierto en modo lectura. Guardar requiere confirmación.";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PokemonList is null || PokemonList.ItemsSource is null)
            return;
        _personalTools.InvalidatePreview();
        ICollectionView view = CollectionViewSource.GetDefaultView(PokemonList.ItemsSource);
        string query = (SearchBox.Text ?? string.Empty).Trim();
        view.Filter = item => item is PokemonListEntry p &&
            (query.Length == 0 ||
             p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             p.Index.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
    }

    private void PokemonList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || _session is null || PokemonList.SelectedItem is not PokemonListEntry next)
            return;

        if (_previous is not null && next.Index != _previous.Index && !ConfirmDiscard())
        {
            _suppressSelection = true;
            PokemonList.SelectedItem = _previous;
            _suppressSelection = false;
            return;
        }

        ShowEntry(next);
    }

    private void ShowEntry(PokemonListEntry next)
    {
        if (_session is null)
            return;

        _personalTools.InvalidatePreview();
        var p = _session.ReadPersonal(next.Index);
        _loadingEntry = true;
        try
        {
            _previous = next;
            _currentIndex = next.Index;
            _original = p;
            EntryTitleText.Text = next.Name + "  ·  Entrada " + next.Index.ToString("000");
            HeaderNameText.Text = next.Name;
            HeaderNumberText.Text = "Entrada #" + next.Index.ToString("000");
            InspectorEntryText.Text = next.Name;
            HpBox.Text = p.HP.ToString();
            AtkBox.Text = p.Attack.ToString();
            DefBox.Text = p.Defense.ToString();
            SpaBox.Text = p.SpecialAttack.ToString();
            SpdBox.Text = p.SpecialDefense.ToString();
            SpeBox.Text = p.Speed.ToString();
            CatchBox.Text = p.CatchRate.ToString();
            TypeOne.SelectedIndex = p.FirstType;
            TypeTwo.SelectedIndex = p.SecondType;
            AbilityOne.SelectedIndex = p.Ability1;
            AbilityTwo.SelectedIndex = p.Ability2;
            AbilityHidden.SelectedIndex = p.AbilityHidden;
            AssignNamedValue(HeldItemOne, _itemOptions, p.Item1);
            AssignNamedValue(HeldItemTwo, _itemOptions, p.Item2);
            AssignNamedValue(HeldItemThree, _itemOptions, p.Item3);
            EvHpBox.Text = p.EvHp.ToString();
            EvAtkBox.Text = p.EvAttack.ToString();
            EvDefBox.Text = p.EvDefense.ToString();
            EvSpaBox.Text = p.EvSpecialAttack.ToString();
            EvSpdBox.Text = p.EvSpecialDefense.ToString();
            EvSpeBox.Text = p.EvSpeed.ToString();
            BaseExpBox.Text = p.BaseExperience.ToString();
            FriendshipBox.Text = p.BaseFriendship.ToString();
            HatchBox.Text = p.HatchCycles.ToString();
            GenderBox.Text = p.GenderRatio.ToString();
            AssignNamedValue(EggOneBox, _eggOptions, p.EggGroup1);
            AssignNamedValue(EggTwoBox, _eggOptions, p.EggGroup2);
            AssignNamedValue(GrowthBox, _growthOptions, p.ExperienceGrowth);
            EscapeBox.Text = p.EscapeRate.ToString();
            EvoStageBox.Text = p.EvoStage.ToString();
            FormCountBox.Text = p.FormCount.ToString();
            FormSpriteBox.Text = p.FormSprite.ToString();
            ColorBox.Text = p.Color.ToString();
            HeightBox.Text = p.Height.ToString();
            WeightBox.Text = p.Weight.ToString();
            AssignNamedValue(ZItemBox, _itemOptions, p.ZItem);
            AssignNamedValue(ZBaseMoveBox, _moveOptions, p.ZBaseMove);
            AssignNamedValue(ZSpecialMoveBox, _moveOptions, p.ZSpecialMove);
            RegionalVariantCheck.IsChecked = p.RegionalVariant;
            LoadCompatibilityLists(p);
            SaveButton.IsEnabled = true;
        }
        finally
        {
            _loadingEntry = false;
        }

        UpdatePreview();
        _ = UpdateSpriteAsync(next.Index);
    }

    private static void AssignNamedValue(System.Windows.Controls.ComboBox box,
        IReadOnlyList<MoveChoice> options, int id)
    {
        // La ROM personalizada podría contener IDs por encima del archivo de nombres.
        // Mantenerlos visibles y guardables sin normalizarlos a cero.
        box.ItemsSource = options.Any(x => x.Id == id)
            ? options : options.Concat([new MoveChoice(id, "ID propio, sin nombre registrado")]).ToArray();
        box.SelectedValue = id;
    }

    private static bool TryNamedValue(System.Windows.Controls.ComboBox box, int max, out int id)
    {
        id = box.SelectedValue is int value ? value : -1;
        return id >= 0 && id <= max;
    }

    private void LoadCompatibilityLists(PersonalValues p)
    {
        if (_session is null)
            return;

        _tmChoices.Clear();
        for (int i = 0; i < Math.Min(100, p.TmFlags.Length); i++)
        {
            string name = i < _session.TmMoveIds.Count
                ? _session.GetMoveLabel(_session.TmMoveIds[i])
                : "Nombre no disponible (code.bin)";
            AddChoice(_tmChoices, $"MT {i + 1:000} · {name}", i, -1, p.TmFlags[i] == '1');
        }

        _tutorChoices.Clear();
        // Exactamente los 8 movimientos TypeTutors de PersonalEditor7 (no inventar un orden).
        int[] fixedTutors = [520, 519, 518, 338, 307, 308, 434, 620];
        for (int i = 0; i < Math.Min(p.TutorFlags.Length, fixedTutors.Length); i++)
            AddChoice(_tutorChoices,
                $"{i + 1:00} · {_session.GetMoveLabel(fixedTutors[i])}",
                i, -1, p.TutorFlags[i] == '1');

        _specialChoices.Clear();
        string[] groups = p.SpecialTutorFlags.Length == 0 ? [] : p.SpecialTutorFlags.Split('|');
        int slot = 0;
        for (int group = 0; group < groups.Length; group++)
        {
            for (int i = 0; i < groups[group].Length; i++, slot++)
            {
                string moveName = slot < _session.BeachTutorMoveIds.Count
                    ? _session.GetMoveLabel(_session.BeachTutorMoveIds[slot])
                    : "Nombre no disponible (Shop.cro)";
                AddChoice(_specialChoices,
                    $"BT {slot + 1:00} · {moveName}",
                    i, group, groups[group][i] == '1');
            }
        }
    }

    private void AddChoice(ObservableCollection<FlagChoice> target, string name,
        int index, int group, bool enabled)
    {
        var flag = new FlagChoice(name, index, group, enabled);
        flag.Changed += () => { if (!_loadingEntry) { _personalTools.InvalidatePreview(); UpdatePreview(); } };
        target.Add(flag);
    }

    private static string MergeFlags(string original, ObservableCollection<FlagChoice> flags)
    {
        char[] data = original.ToCharArray();
        foreach (FlagChoice flag in flags)
            data[flag.Index] = flag.IsChecked ? '1' : '0';
        return new string(data);
    }

    private string MergeSpecialFlags(string original)
    {
        string[] groups = original.Length == 0 ? [] : original.Split('|');
        char[][] data = groups.Select(g => g.ToCharArray()).ToArray();
        foreach (FlagChoice flag in _specialChoices)
            data[flag.Group][flag.Index] = flag.IsChecked ? '1' : '0';
        return string.Join("|", data.Select(g => new string(g)));
    }

    private void CheckAllTm_Click(object sender, RoutedEventArgs e)
    {
        foreach (FlagChoice flag in _tmChoices) flag.IsChecked = true;
    }

    private void UncheckAllTm_Click(object sender, RoutedEventArgs e)
    {
        foreach (FlagChoice flag in _tmChoices) flag.IsChecked = false;
    }

    private void FlagCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loadingEntry) UpdatePreview();
    }

    private bool TryDraft(out PersonalValues? draft)
    {
        draft = null;
        int zItem = 0, zBase = 0, zSpecial = 0;
        if (_currentIndex < 0 || _session is null)
            return false;
        if (!byte.TryParse(HpBox.Text, out byte hp) ||
            !byte.TryParse(AtkBox.Text, out byte atk) ||
            !byte.TryParse(DefBox.Text, out byte def) ||
            !byte.TryParse(SpaBox.Text, out byte spa) ||
            !byte.TryParse(SpdBox.Text, out byte spd) ||
            !byte.TryParse(SpeBox.Text, out byte spe) ||
            !byte.TryParse(CatchBox.Text, out byte catchRate) ||
            TypeOne.SelectedIndex < 0 || TypeTwo.SelectedIndex < 0)
            return false;
        if (AbilityOne.SelectedIndex < 0 || AbilityTwo.SelectedIndex < 0 ||
            AbilityHidden.SelectedIndex < 0 ||
            !TryNamedValue(HeldItemOne, 65535, out int item1) ||
            !TryNamedValue(HeldItemTwo, 65535, out int item2) ||
            !TryNamedValue(HeldItemThree, 65535, out int item3) ||
            !byte.TryParse(EvHpBox.Text, out byte evhp) || evhp > 3 ||
            !byte.TryParse(EvAtkBox.Text, out byte evattack) || evattack > 3 ||
            !byte.TryParse(EvDefBox.Text, out byte evdefense) || evdefense > 3 ||
            !byte.TryParse(EvSpaBox.Text, out byte evspecialattack) || evspecialattack > 3 ||
            !byte.TryParse(EvSpdBox.Text, out byte evspecialdefense) || evspecialdefense > 3 ||
            !byte.TryParse(EvSpeBox.Text, out byte evspeed) || evspeed > 3 ||
            !ushort.TryParse(BaseExpBox.Text, out ushort baseexperience) ||
            !byte.TryParse(FriendshipBox.Text, out byte basefriendship) ||
            !byte.TryParse(HatchBox.Text, out byte hatchcycles) ||
            !byte.TryParse(GenderBox.Text, out byte genderratio) ||
            !TryNamedValue(EggOneBox, 255, out int egggroup1) ||
            !TryNamedValue(EggTwoBox, 255, out int egggroup2) ||
            !TryNamedValue(GrowthBox, 255, out int experiencegrowth) ||
            !byte.TryParse(EscapeBox.Text, out byte escaperate) ||
            !byte.TryParse(EvoStageBox.Text, out byte evostage) ||
            !byte.TryParse(FormCountBox.Text, out byte formcount) ||
            !ushort.TryParse(FormSpriteBox.Text, out ushort formsprite) ||
            !byte.TryParse(ColorBox.Text, out byte color) || color > 63 ||
            !ushort.TryParse(HeightBox.Text, out ushort height) ||
            !ushort.TryParse(WeightBox.Text, out ushort weight) ||
            (_session.SupportsGen7Personal &&
                (!TryNamedValue(ZItemBox, 65535, out zItem) ||
                 !TryNamedValue(ZBaseMoveBox, 65535, out zBase) ||
                 !TryNamedValue(ZSpecialMoveBox, 65535, out zSpecial))))
            return false;
        draft = new PersonalValues(hp, atk, def, spa, spd, spe, catchRate,
            TypeOne.SelectedIndex, TypeTwo.SelectedIndex)
        {
            Ability1 = AbilityOne.SelectedIndex,
            Ability2 = AbilityTwo.SelectedIndex,
            AbilityHidden = AbilityHidden.SelectedIndex,
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            EvHp = evhp,
            EvAttack = evattack,
            EvDefense = evdefense,
            EvSpecialAttack = evspecialattack,
            EvSpecialDefense = evspecialdefense,
            EvSpeed = evspeed,
            BaseExperience = baseexperience,
            BaseFriendship = basefriendship,
            HatchCycles = hatchcycles,
            GenderRatio = genderratio,
            EggGroup1 = egggroup1,
            EggGroup2 = egggroup2,
            ExperienceGrowth = experiencegrowth,
            EscapeRate = escaperate,
            EvoStage = evostage,
            FormCount = formcount,
            FormSprite = formsprite,
            Color = color,
            Height = height,
            Weight = weight,
            ZItem = zItem,
            ZBaseMove = zBase,
            ZSpecialMove = zSpecial,
            RegionalVariant = _session.SupportsGen7Personal && RegionalVariantCheck.IsChecked == true,
            TmFlags = MergeFlags(_original!.TmFlags, _tmChoices),
            TutorFlags = MergeFlags(_original.TutorFlags, _tutorChoices),
            SpecialTutorFlags = MergeSpecialFlags(_original.SpecialTutorFlags),
        };
        return true;
    }

    private bool HasUnsavedChanges()
    {
        if (_original is null)
            return false;
        return !TryDraft(out PersonalValues? current) || current != _original;
    }

    private bool ConfirmDiscard()
    {
        if (!HasUnsavedChanges())
            return true;
        return StudioDialog.Confirm(this, "Cambios pendientes",
            "Hay cambios sin guardar. ¿Querés descartarlos?",
            "Si continuás, los cambios de la especie actual no se conservarán.");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _currentIndex < 0)
            return;
        if (!TryDraft(out PersonalValues? draft) || draft is null)
        {
            StudioDialog.Notice(this, "Datos inválidos",
                "Los valores deben estar entre 0 y 255 y ambos tipos deben estar seleccionados.");
            return;
        }
        if (draft == _original)
        {
            StatusText.Text = "No hay cambios para guardar.";
            return;
        }

        if (!StudioDialog.Confirm(this, "Confirmar guardado del GARC",
            "Se modificará la tabla personal del proyecto extraído seleccionado.",
            "Asegurate de trabajar sobre una COPIA de tus archivos. Studio no crea backups automáticos."))
            return;

        try
        {
            _personalTools.InvalidatePreview();
            _session.SavePersonal(_currentIndex, draft);
            _original = _session.ReadPersonal(_currentIndex);
            UpdatePreview();
            StatusText.Text = "Cambios guardados y GARC verificado: entrada " + _currentIndex + ".";
            StudioDialog.Notice(this, "Guardado completado", "Datos personales guardados correctamente.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudo guardar: " + ex.Message;
            StudioDialog.Error(this, "Error al guardar", ex.Message);
        }
    }

    private void Pokemon_Click(object sender, RoutedEventArgs e)
    {
        if (!_showingMoves && SectionHost.Visibility!=Visibility.Visible)
            return;
        if(_recordView is not null&&!_recordView.ConfirmLeave())return;
        if(_martView is not null&&!_martView.ConfirmLeave())return;
        bool discarded = _movesView?.HasPendingChanges == true;
        if (_movesView is not null && !_movesView.ConfirmLeave())
            return;
        if (discarded)
        {
            MovesHost.Content = null;
            _movesView = null;
        }
        DisplayPokemon();
    }

    private void DisplayPokemon()
    {
        _recordView=null;_martView=null;_mart6View=null;SectionHost.Visibility=Visibility.Collapsed;SectionHost.Content=null;
        TrainersButton.Background=EncountersButton.Background=EvolutionsButton.Background=HistoryButton.Background=Brushes.Transparent;
        _showingMoves = false;
        PokemonListPanel.Visibility = Visibility.Visible;
        PokemonEditorPanel.Visibility = Visibility.Visible;
        PersonalToolsPanel.Visibility = Visibility.Collapsed;
        _personalTools.InvalidatePreview();
        PokemonInspectorPanel.Visibility = Visibility.Visible;
        MovesHost.Visibility = Visibility.Collapsed;
        PokemonNavButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B4059")!);
        MovesButton.Background = Brushes.Transparent;
    }

    private void Moves_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _showingMoves)
            return;
        if(_recordView is not null&&!_recordView.ConfirmLeave())return;
        if(_martView is not null&&!_martView.ConfirmLeave())return;
        bool discardedPokemon = HasUnsavedChanges();
        if (!ConfirmDiscard())
            return;
        if (discardedPokemon && _previous is not null)
            ShowEntry(_previous);
        try
        {
            _movesView ??= new MoveEditorView(_session);
            _recordView=null;_martView=null;_mart6View=null;SectionHost.Visibility=Visibility.Collapsed;SectionHost.Content=null;
            TrainersButton.Background=EncountersButton.Background=EvolutionsButton.Background=HistoryButton.Background=Brushes.Transparent;
            MovesHost.Content = _movesView;
            PokemonListPanel.Visibility = Visibility.Collapsed;
            PokemonEditorPanel.Visibility = Visibility.Collapsed;
            PersonalToolsPanel.Visibility = Visibility.Collapsed;
            _personalTools.InvalidatePreview();
            PokemonInspectorPanel.Visibility = Visibility.Collapsed;
            MovesHost.Visibility = Visibility.Visible;
            _showingMoves = true;
            PokemonNavButton.Background = Brushes.Transparent;
            MovesButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B4059")!);
            StatusText.Text = "Movimientos · edición integrada en Definitive Randomizer.";
        }
        catch (Exception ex)
        {
            StudioDialog.Error(this, "No se pudo cargar Movimientos", ex.Message);
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _previous is null)
            return;
        ShowEntry(_previous);
        StatusText.Text = "Campos restablecidos al último estado guardado.";
    }

    private void Stat_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingEntry)
        {
            _personalTools.InvalidatePreview();
            UpdatePreview();
        }
    }

    private void Type_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingEntry)
        {
            _personalTools.InvalidatePreview();
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        if (BstText is null || HeaderBstText is null || HpBox is null ||
            AtkBox is null || DefBox is null || SpaBox is null ||
            SpdBox is null || SpeBox is null || DirtyBadge is null)
            return;

        if (byte.TryParse(HpBox.Text, out var hp) &&
            byte.TryParse(AtkBox.Text, out var atk) &&
            byte.TryParse(DefBox.Text, out var def) &&
            byte.TryParse(SpaBox.Text, out var spa) &&
            byte.TryParse(SpdBox.Text, out var spd) &&
            byte.TryParse(SpeBox.Text, out var spe))
            BstText.Text = HeaderBstText.Text = (hp + atk + def + spa + spd + spe).ToString();
        else
            BstText.Text = HeaderBstText.Text = "—";

        UpdateBadge(TypeBadgeOne, TypeBadgeOneText, TypeOne.SelectedItem as string);
        UpdateBadge(TypeBadgeTwo, TypeBadgeTwoText, TypeTwo.SelectedItem as string);

        if (_original is null)
            SetDirtyState("Sin cambios", "#35485D", "#C5D7E9");
        else if (!TryDraft(out PersonalValues? draft))
            SetDirtyState("Revisar campos", "#75522E", "#F8D49A");
        else if (draft != _original)
            SetDirtyState("Cambios sin guardar", "#75522E", "#F8D49A");
        else
            SetDirtyState("Guardado", "#31544A", "#B6E8CC");
    }

    private void SetDirtyState(string message, string background, string foreground)
    {
        DirtyBadgeText.Text = message;
        DirtyBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background)!);
        DirtyBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground)!);
    }

    private static void UpdateBadge(Border badge, TextBlock label, string? name)
    {
        string type = string.IsNullOrWhiteSpace(name) ? "—" : name;
        label.Text = type;
        string color = type.ToLowerInvariant() switch
        {
            "normal" => "#6D798A",
            "fire" => "#A85D45",
            "water" => "#416FA4",
            "electric" => "#A48D44",
            "grass" => "#4B8157",
            "ice" => "#528992",
            "fighting" => "#8E554E",
            "poison" => "#74539A",
            "ground" => "#846E4C",
            "flying" => "#5979A6",
            "psychic" => "#A35B80",
            "bug" => "#687E46",
            "rock" => "#837252",
            "ghost" => "#605A8A",
            "dragon" => "#516AAB",
            "dark" => "#50576B",
            "steel" => "#5C788C",
            "fairy" => "#9C7297",
            _ => "#395469"
        };
        badge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
    }

    private async System.Threading.Tasks.Task UpdateSpriteAsync(int entryIndex)
    {
        if (_session is null)
            return;

        _spriteCancellation?.Cancel();
        _spriteCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _spriteCancellation = cancellation;

        PokemonSprite.Source = null;
        SpriteFallback.Visibility = Visibility.Collapsed;
        SpriteStatusText.Text = "";
        int speciesId = _session.GetSpriteSpeciesId(entryIndex);

        try
        {
            var result = await _sprites.GetSpriteAsync(entryIndex, speciesId, cancellation.Token);
            if (cancellation.IsCancellationRequested || _currentIndex != entryIndex)
                return;

            PokemonSprite.Source = result.Image;
            SpriteFallback.Visibility = Visibility.Collapsed;
            SpriteStatusText.Text = result.Description;
        }
        catch (OperationCanceledException)
        {
            // Cambió la selección mientras se cargaba la imagen.
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if(_backupRunning){e.Cancel=true;base.OnClosing(e);return;}
        if (!ConfirmDiscard() || !ConfirmAuxiliaryLeave())
            e.Cancel = true;
        if (!e.Cancel)
        {
            _spriteCancellation?.Cancel();
            _spriteCancellation?.Dispose();
        }
        base.OnClosing(e);
    }
}
