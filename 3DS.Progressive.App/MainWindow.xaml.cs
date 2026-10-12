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
    private RecordEditorView? _recordView;
    private readonly Dictionary<string,RecordEditorView.UiState> _sectionOptions=[];
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

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard() || !ConfirmAuxiliaryLeave())
            return;

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Selecciona la carpeta RAIZ del juego extraido (RomFS + ExeFS).",
            ShowNewFolderButton = false,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        try
        {
            var candidate = GameProjectSession.Open(dialog.SelectedPath);
            LoadProject(candidate);
        }
        catch (Exception ex)
        {
            StudioDialog.Error(this, "No se pudo abrir el proyecto", ex.Message);
            StatusText.Text = "Error al abrir proyecto: " + ex.Message;
        }
    }

    private bool ConfirmAuxiliaryLeave()
    {
        if(_recordView is not null&&!_recordView.ConfirmLeave())return false;
        if(_recordView is not null)_sectionOptions[_recordView.Editor.Title]=_recordView.RememberOptions();
        if(_showingMoves&&_movesView is not null)
        {
            bool pending=_movesView.HasPendingChanges;
            if(!_movesView.ConfirmLeave())return false;
            if(pending){_movesView=null;MovesHost.Content=null;}
        }
        return true;
    }

    private void Section_Click(object sender,RoutedEventArgs e)
    {
        if(_session is null||sender is not Button {Tag:string section})return;
        if(!ConfirmDiscard()||!ConfirmAuxiliaryLeave())return;
        if(HasUnsavedChanges()&&_previous is not null)ShowEntry(_previous);
        try
        {
            UserControl view=section switch
            {
                "Trainers"=>CreateEditor(new GameTrainerService(_session)),
                "Encounters"=>CreateEditor(new GameEncounterService(_session)),
                "StaticEncounters"=>CreateEditor(new GameStaticEncounterService(_session)),
                "Evolutions"=>CreateEditor(new GameEvolutionService(_session)),
                "History"=>new HistoryView(_session.History),
                _=>throw new ArgumentException("Sección desconocida.")
            };
            _recordView=view as RecordEditorView;SectionHost.Content=view;
            PokemonListPanel.Visibility=PokemonEditorPanel.Visibility=PersonalToolsPanel.Visibility=PokemonInspectorPanel.Visibility=MovesHost.Visibility=Visibility.Collapsed;
            _personalTools.InvalidatePreview();SectionHost.Visibility=Visibility.Visible;_showingMoves=false;
            PokemonNavButton.Background=MovesButton.Background=TrainersButton.Background=EncountersButton.Background=StaticEncountersButton.Background=EvolutionsButton.Background=HistoryButton.Background=Brushes.Transparent;
            ((Button)sender).Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B4059")!);
            StatusText.Text=section=="History"?"Historial persistente de cambios confirmados.":"Editor integrado · guardar registra los cambios en el historial.";
        }
        catch(Exception ex){StudioDialog.Error(this,"No se pudo cargar la sección",ex.Message);}
    }

    private RecordEditorView CreateEditor(IProjectEditor editor)=>new(editor,_sectionOptions.GetValueOrDefault(editor.Title));

    private async void Backup_Click(object sender,RoutedEventArgs e)
    {
        if(_session is null||_backupRunning)return;
        using var folder=new System.Windows.Forms.FolderBrowserDialog {Description="Elegí la carpeta de destino del backup completo del proyecto.",ShowNewFolderButton=true};
        if(folder.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
        var project=_session;_backupRunning=true;IsEnabled=false;
        try
        {
            StatusText.Text="Generando backup de los archivos guardados…";
            var progress=new Progress<BackupProgress>(p=>StatusText.Text=$"Backup: {p.Completed}/{p.Total} · {p.File}");
            string path=await ProjectBackup.CreateAsync(project.ProjectRoot,folder.SelectedPath,project.GameName,progress,history:project.History.Read());
            StatusText.Text="Backup completo y verificado: "+path;
            IsEnabled=true;StudioDialog.Notice(this,"Backup generado","Se guardó y verificó una copia del proyecto y su historial.",path);
        }
        catch(Exception ex){IsEnabled=true;StudioDialog.Error(this,"No se pudo generar el backup",ex.Message);StatusText.Text=ex.Message;}
        finally{_backupRunning=false;IsEnabled=true;}
    }

    internal void LoadProject(GameProjectSession candidate)
    {
        _session = candidate;
        _sectionOptions.Clear();
        _recordView=null;SectionHost.Content=null;
        _movesView = null;
        MovesHost.Content = null;
        DisplayPokemon();
        _previous = null;
        _original = null;
        _currentIndex = -1;
        _spriteCancellation?.Cancel();
        PokemonSprite.Source = null;
        SpriteFallback.Visibility = Visibility.Visible;
        SpriteStatusText.Text = "Sin sprite";
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
        TrainersButton.IsEnabled = EncountersButton.IsEnabled = EvolutionsButton.IsEnabled = HistoryButton.IsEnabled = BackupButton.IsEnabled = true;
        StaticEncountersButton.IsEnabled=candidate.Generation==7;
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
        _recordView=null;SectionHost.Visibility=Visibility.Collapsed;SectionHost.Content=null;
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
        bool discardedPokemon = HasUnsavedChanges();
        if (!ConfirmDiscard())
            return;
        if (discardedPokemon && _previous is not null)
            ShowEntry(_previous);
        try
        {
            _movesView ??= new MoveEditorView(_session);
            _recordView=null;SectionHost.Visibility=Visibility.Collapsed;SectionHost.Content=null;
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
            StatusText.Text = "Movimientos · edición integrada en 3DS Progressive.";
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
        SpriteFallback.Visibility = Visibility.Visible;
        SpriteStatusText.Text = "Cargando sprite...";
        int speciesId = _session.GetSpriteSpeciesId(entryIndex);

        try
        {
            var result = await _sprites.GetSpriteAsync(entryIndex, speciesId, cancellation.Token);
            if (cancellation.IsCancellationRequested || _currentIndex != entryIndex)
                return;

            PokemonSprite.Source = result.Image;
            SpriteFallback.Visibility = result.Image is null
                ? Visibility.Visible : Visibility.Collapsed;
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
