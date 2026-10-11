using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ThreeDS.Progressive;

public partial class MoveEditorWindow : Window
{
    private readonly GameMoveService _moves;
    private MoveListEntry? _selected;
    private MoveValues? _saved;
    private bool _loading;
    private bool _restoringSelection;

    public MoveEditorWindow(GameProjectSession project)
    {
        InitializeComponent();
        _moves = new GameMoveService(project);
        MoveType.ItemsSource = _moves.Types;
        MoveList.ItemsSource = _moves.Entries;
        CountText.Text = _moves.Entries.Count + " movimientos";
        MoveList.SelectedIndex = 0;
        StatusLabel.Text = "Editor de movimientos listo. Guardar requiere confirmación.";
    }

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (MoveList is null || MoveList.ItemsSource is null)
            return;
        var view = CollectionViewSource.GetDefaultView(MoveList.ItemsSource);
        string q = (SearchInput.Text ?? string.Empty).Trim();
        view.Filter = item => item is MoveListEntry m &&
            (q.Length == 0 || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
             || m.Id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
    }

    private void MoveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringSelection || MoveList.SelectedItem is not MoveListEntry selected)
            return;
        if (_selected is not null && selected.Id != _selected.Id && !ConfirmDiscard())
        {
            _restoringSelection = true;
            MoveList.SelectedItem = _selected;
            _restoringSelection = false;
            return;
        }
        ShowMove(selected);
    }

    private void ShowMove(MoveListEntry entry)
    {
        var p = _moves.Read(entry.Id);
        _loading = true;
        try
        {
            _selected = entry;
            _saved = p;
            MoveNameText.Text = entry.Name;
            MoveNumberText.Text = "Movimiento #" + entry.Id.ToString("000");
            MoveType.SelectedIndex = p.Type;
            MoveCategory.SelectedIndex = p.Category;
            MovePower.Text = p.Power.ToString();
            MoveAccuracy.Text = p.Accuracy.ToString();
            MovePP.Text = p.PP.ToString();
            MovePriority.Text = p.Priority.ToString();
        }
        finally { _loading = false; }
        UpdatePreview();
    }

    private bool TryDraft(out MoveValues? result)
    {
        result = null;
        if (_selected is null ||
            !byte.TryParse(MovePower.Text, out byte power) ||
            !byte.TryParse(MoveAccuracy.Text, out byte acc) ||
            !byte.TryParse(MovePP.Text, out byte pp) ||
            !int.TryParse(MovePriority.Text, out int priority) ||
            priority < -8 || priority > 8 ||
            MoveCategory.SelectedIndex < 0 || MoveCategory.SelectedIndex > 2 ||
            MoveType.SelectedIndex < 0 || MoveType.SelectedIndex >= _moves.Types.Count)
            return false;
        result = new MoveValues(MoveType.SelectedIndex, MoveCategory.SelectedIndex,
            power, acc, pp, priority);
        return true;
    }

    private bool HasChanges()
    {
        if (_saved is null)
            return false;
        return !TryDraft(out var current) || current != _saved;
    }

    private bool ConfirmDiscard() =>
        !HasChanges() || StudioDialog.Confirm(this, "Cambios pendientes",
            "Hay modificaciones sin guardar.",
            "Si continuás, los cambios del movimiento actual se perderán.");

    private void UpdatePreview()
    {
        if (_loading || PreviewPower is null || MovePower is null || ChangeStateBadge is null)
            return;
        PreviewType.Text = MoveType.SelectedItem as string ?? "—";
        PreviewCategory.Text = (MoveCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "—";
        PreviewPower.Text = MovePower.Text;

        bool valid = TryDraft(out MoveValues? next);
        bool modified = _saved is not null && (!valid || next != _saved);
        string bg = !valid ? "#6B4434" : modified ? "#75522E" : "#31544A";
        string fg = !valid ? "#F6C1A7" : modified ? "#F8D49A" : "#B6E8CC";
        ChangeStateText.Text = !valid ? "Revisar campos" : modified ? "Cambios sin guardar" : "Guardado";
        ChangeStateBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)!);
        ChangeStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg)!);
    }

    private void Value_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();
    private void Text_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;
        ShowMove(_selected);
        StatusLabel.Text = "Datos restablecidos.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (!TryDraft(out var next) || next is null)
        {
            StudioDialog.Notice(this, "Datos inválidos",
                "Revisá los campos del movimiento: tipo, categoría, potencia, precisión, PP y prioridad.");
            return;
        }
        if (next == _saved)
        {
            StatusLabel.Text = "No hay modificaciones pendientes.";
            return;
        }
        if (!StudioDialog.Confirm(this, "Confirmar guardado",
            "Se actualizará el GARC de movimientos del proyecto extraído.",
            "Studio no realiza copias de seguridad automáticas. Asegurate de trabajar con una copia del juego."))
            return;
        try
        {
            _moves.Save(_selected.Id, next);
            _saved = _moves.Read(_selected.Id);
            UpdatePreview();
            StatusLabel.Text = $"Movimiento #{_selected.Id:000} guardado y verificado.";
            StudioDialog.Notice(this, "Movimiento guardado",
                "Los cambios se guardaron y se comprobó la integridad de la tabla.");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Error al guardar movimiento.";
            StudioDialog.Error(this, "No se pudo guardar", ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscard())
            e.Cancel = true;
        base.OnClosing(e);
    }
}
