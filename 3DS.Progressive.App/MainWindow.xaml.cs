using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        _previous = next;
        _currentIndex = next.Index;
        _original = p;
        EntryTitleText.Text = next.Name + "  ·  Entrada " + next.Index.ToString("000");
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
        UpdatePreview();
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

    private void Stat_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();
    private void Type_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (BstText is null || HpBox is null || AtkBox is null || DefBox is null ||
            SpaBox is null || SpdBox is null || SpeBox is null)
            return;
        if (byte.TryParse(HpBox.Text, out var hp) &&
            byte.TryParse(AtkBox.Text, out var atk) &&
            byte.TryParse(DefBox.Text, out var def) &&
            byte.TryParse(SpaBox.Text, out var spa) &&
            byte.TryParse(SpdBox.Text, out var spd) &&
            byte.TryParse(SpeBox.Text, out var spe))
            BstText.Text = (hp + atk + def + spa + spd + spe).ToString();
        else
            BstText.Text = "—";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscard())
            e.Cancel = true;
        base.OnClosing(e);
    }
}
