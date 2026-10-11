using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Threading;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
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

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard())
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
            PokemonList.ItemsSource = candidate.Entries;
            _suppressSelection = false;

            ProjectNameText.Text = System.IO.Path.GetFileName(candidate.ProjectRoot);
            GameNameText.Text = "Juego: " + candidate.GameName + "  ·  Gen " + candidate.Generation;
            InspectorGameText.Text = candidate.GameName;
            EntryCountText.Text = candidate.Entries.Count + " entradas editables";
            EditorContent.IsEnabled = true;
            PokemonList.SelectedIndex = 0;
            StatusText.Text = "Proyecto abierto en modo lectura. Guardar requiere confirmación.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "No se pudo abrir el proyecto",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
        draft = new PersonalValues(hp, atk, def, spa, spd, spe, catchRate,
            TypeOne.SelectedIndex, TypeTwo.SelectedIndex);
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
        return MessageBox.Show(this,
            "Hay cambios sin guardar. ¿Querés descartarlos?",
            "Cambios pendientes", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _currentIndex < 0)
            return;
        if (!TryDraft(out PersonalValues? draft) || draft is null)
        {
            MessageBox.Show(this, "Todos los campos numéricos deben contener valores de 0 a 255, y ambos tipos deben estar seleccionados.",
                "Datos inválidos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (draft == _original)
        {
            StatusText.Text = "No hay cambios para guardar.";
            return;
        }

        if (MessageBox.Show(this,
            "Se modificará la tabla personal del proyecto extraído seleccionado. " +
            "Asegurate de trabajar sobre una COPIA de tus archivos. " +
            "Studio NO crea backups automáticos. ¿Continuar?",
            "Confirmar guardado del GARC", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            _session.SavePersonal(_currentIndex, draft);
            _original = _session.ReadPersonal(_currentIndex);
            UpdatePreview();
            StatusText.Text = "Cambios guardados y GARC verificado: entrada " + _currentIndex + ".";
            MessageBox.Show(this, "Datos personales guardados correctamente.",
                "3DS Progressive", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudo guardar: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Error al guardar",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (!ConfirmDiscard())
            e.Cancel = true;
        if (!e.Cancel)
        {
            _spriteCancellation?.Cancel();
            _spriteCancellation?.Dispose();
        }
        base.OnClosing(e);
    }
}
