using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard() || (_showingMoves && _movesView is not null && !_movesView.ConfirmLeave()))
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
            _session = candidate;
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
            PokemonList.ItemsSource = candidate.Entries;
            _suppressSelection = false;

            ProjectNameText.Text = System.IO.Path.GetFileName(candidate.ProjectRoot);
            GameNameText.Text = "Juego: " + candidate.GameName + "  ·  Gen " + candidate.Generation;
            InspectorGameText.Text = candidate.GameName;
            EntryCountText.Text = candidate.Entries.Count + " entradas editables";
            EditorContent.IsEnabled = true;
            MovesButton.IsEnabled = true;
            PokemonList.SelectedIndex = 0;
            StatusText.Text = "Proyecto abierto en modo lectura. Guardar requiere confirmación.";
        }
        catch (Exception ex)
        {
            StudioDialog.Error(this, "No se pudo abrir el proyecto", ex.Message);
            StatusText.Text = "Error al abrir proyecto: " + ex.Message;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PokemonList is null || PokemonList.ItemsSource is null)
            return;
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
            HeldItemOne.Text = p.Item1.ToString();
            HeldItemTwo.Text = p.Item2.ToString();
            HeldItemThree.Text = p.Item3.ToString();
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
            EggOneBox.Text = p.EggGroup1.ToString();
            EggTwoBox.Text = p.EggGroup2.ToString();
            GrowthBox.Text = p.ExperienceGrowth.ToString();
            EscapeBox.Text = p.EscapeRate.ToString();
            SaveButton.IsEnabled = true;
        }
        finally
        {
            _loadingEntry = false;
        }

        UpdatePreview();
        _ = UpdateSpriteAsync(next.Index);
    }

    private bool TryDraft(out PersonalValues? draft)
    {
        draft = null;
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
            !ushort.TryParse(HeldItemOne.Text, out ushort item1) ||
            !ushort.TryParse(HeldItemTwo.Text, out ushort item2) ||
            !ushort.TryParse(HeldItemThree.Text, out ushort item3) ||
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
            !byte.TryParse(EggOneBox.Text, out byte egggroup1) ||
            !byte.TryParse(EggTwoBox.Text, out byte egggroup2) ||
            !byte.TryParse(GrowthBox.Text, out byte experiencegrowth) ||
            !byte.TryParse(EscapeBox.Text, out byte escaperate))
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
        if (!_showingMoves)
            return;
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
        _showingMoves = false;
        PokemonListPanel.Visibility = Visibility.Visible;
        PokemonEditorPanel.Visibility = Visibility.Visible;
        PokemonInspectorPanel.Visibility = Visibility.Visible;
        MovesHost.Visibility = Visibility.Collapsed;
        PokemonNavButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B4059")!);
        MovesButton.Background = Brushes.Transparent;
    }

    private void Moves_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _showingMoves)
            return;
        bool discardedPokemon = HasUnsavedChanges();
        if (!ConfirmDiscard())
            return;
        if (discardedPokemon && _previous is not null)
            ShowEntry(_previous);
        try
        {
            _movesView ??= new MoveEditorView(_session);
            MovesHost.Content = _movesView;
            PokemonListPanel.Visibility = Visibility.Collapsed;
            PokemonEditorPanel.Visibility = Visibility.Collapsed;
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
            UpdatePreview();
    }

    private void Type_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingEntry)
            UpdatePreview();
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
        if (!ConfirmDiscard() || (_showingMoves && _movesView is not null && !_movesView.ConfirmLeave()))
            e.Cancel = true;
        if (!e.Cancel)
        {
            _spriteCancellation?.Cancel();
            _spriteCancellation?.Dispose();
        }
        base.OnClosing(e);
    }
}
