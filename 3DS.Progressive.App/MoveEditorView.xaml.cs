using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ThreeDS.Progressive;

public partial class MoveEditorView : System.Windows.Controls.UserControl
{
    private readonly BatchToolsView _tools = new();
    internal MasterBatchState? CaptureGlobalState()=>_tools.CaptureGlobalState();
    internal void RestoreGlobalState(MasterBatchState state)=>_tools.RestoreGlobalState(state);
    private readonly GameMoveService _moves;
    private MoveListEntry? _selected;
    private MoveValues? _saved;
    private bool _loading;
    private bool _restoringSelection;
    private bool _updatingFlags;
    private readonly List<System.Windows.Controls.CheckBox> _flagChecks = [];

    public MoveEditorView(GameProjectSession project)
    {
        InitializeComponent();
        _moves = new GameMoveService(project);
        MoveToolsHost.Content = _tools;
        _tools.BackRequested += () => { MoveToolsPanel.Visibility = Visibility.Collapsed; MoveFieldsPanel.Visibility = Visibility.Visible; };
        _tools.Configure(true, project.Generation, false, false, false,
            () =>
            {
                if (!HasChanges()) return true;
                if (!ConfirmDiscard()) return false;
                if (_selected is not null) ShowMove(_selected);
                return true;
            },
            scope => scope switch
            {
                1 => MoveList.Items.Cast<MoveListEntry>().Select(m => m.Id).ToArray(),
                2 => _selected is not null ? new[] { _selected.Id } : Array.Empty<int>(),
                _ => _moves.Entries.Select(m => m.Id).ToArray(),
            }, _moves.PreviewMoves,
            preview =>
            {
                _moves.ApplyMovePreview(preview);
                if (_selected is not null) ShowMove(_selected);
                StatusLabel.Text = $"{preview.Action}: {preview.ChangedCount} movimientos guardados y verificados.";
            }, _moves.ExportMoveTable,defaultBattlePatches:!project.GameConfiguration.SM,supportsBalance:_moves.SupportsProgressiveBalance);
        BalanceMovesButton.Visibility=_moves.SupportsProgressiveBalance?Visibility.Visible:Visibility.Collapsed;
        ZMoveCard.Visibility = _moves.SupportsZMoves ? Visibility.Visible : Visibility.Collapsed;
        QualityBox.ItemsSource = _moves.Qualities;
        InflictBox.ItemsSource = _moves.Inflictions;
        TargetBox.ItemsSource = _moves.Targets;
        ZMoveBox.ItemsSource = _moves.AllMoveNames;
        ZEffectBox.ItemsSource = _moves.ZEffects;
        StatOneBox.ItemsSource = _moves.Stats;
        StatTwoBox.ItemsSource = _moves.Stats;
        StatThreeBox.ItemsSource = _moves.Stats;
        RefreshTypeBox.ItemsSource = MoveEditorCatalog.IndexedChoices(Enum.GetNames(typeof(RefreshType)));
        CreateFlagCheckBoxes();
        MoveType.ItemsSource = _moves.Types;
        MoveList.ItemsSource = _moves.Entries;
        CountText.Text = _moves.Entries.Count + " movimientos";
        MoveList.SelectedIndex = 0;
        StatusLabel.Text = "Editor de movimientos listo. Guardar requiere confirmación.";
    }

    private void MoveAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        bool pending = HasChanges();
        if (!ConfirmDiscard()) return;
        if (pending && _selected is not null) ShowMove(_selected);
        _tools.OpenAction(Enum.Parse<BatchAction>(action));
        MoveFieldsPanel.Visibility = Visibility.Collapsed;
        MoveToolsPanel.Visibility = Visibility.Visible;
    }

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (MoveList is null || MoveList.ItemsSource is null)
            return;
        _tools.InvalidatePreview();
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
        _tools.InvalidatePreview();
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
            AssignCatalog(QualityBox, _moves.Qualities, p.Quality);
            MinHitsBox.Text = p.HitMin.ToString();
            MaxHitsBox.Text = p.HitMax.ToString();
            // Valores no catalogados se conservan en lugar de convertirlos a None.
            if (!_moves.Inflictions.Any(c => c.Id == p.Inflict))
                InflictBox.ItemsSource = _moves.Inflictions.Concat([new MoveChoice(p.Inflict, "ID no catalogado")]).ToArray();
            else
                InflictBox.ItemsSource = _moves.Inflictions;
            InflictBox.SelectedValue = p.Inflict;
            InflictRateBox.Text = p.InflictPercent.ToString();
            CritBox.Text = p.CritStage.ToString();
            FlinchBox.Text = p.Flinch.ToString();
            EffectBox.Text = p.EffectSequence.ToString();
            AssignCatalog(TargetBox, _moves.Targets, p.Target);
            if (!_moves.AllMoveNames.Any(c => c.Id == p.ZMove))
                ZMoveBox.ItemsSource = _moves.AllMoveNames.Concat([new MoveChoice(p.ZMove, "Movimiento no catalogado")]).ToArray();
            else
                ZMoveBox.ItemsSource = _moves.AllMoveNames;
            ZMoveBox.SelectedValue = p.ZMove;
            ZPowerBox.Text = p.ZPower.ToString();
            AssignCatalog(ZEffectBox, _moves.ZEffects, p.ZEffect);
            TurnsMinBox.Text = p.TurnsMin.ToString();
            TurnsMaxBox.Text = p.TurnsMax.ToString();
            RecoilBox.Text = p.Recoil.ToString();
            HealingBox.Text = p.Healing.ToString();
            AssignCatalog(StatOneBox, _moves.Stats, p.Stat1);
            AssignCatalog(StatTwoBox, _moves.Stats, p.Stat2);
            AssignCatalog(StatThreeBox, _moves.Stats, p.Stat3);
            StageOneBox.Text = p.Stat1Stage.ToString();
            StageTwoBox.Text = p.Stat2Stage.ToString();
            StageThreeBox.Text = p.Stat3Stage.ToString();
            StageRateOneBox.Text = p.Stat1Rate.ToString();
            StageRateTwoBox.Text = p.Stat2Rate.ToString();
            StageRateThreeBox.Text = p.Stat3Rate.ToString();
            InflictCountBox.Text = p.InflictCount.ToString();
            AssignCatalog(RefreshTypeBox, MoveEditorCatalog.IndexedChoices(
                Enum.GetNames(typeof(RefreshType)), 5), p.RefreshType);
            RefreshRateBox.Text = p.RefreshRate.ToString();
            FlagsBox.Text = p.Flags.ToString("X8");
            SyncFlagCheckboxes(p.Flags);
        }
        finally { _loading = false; }
        UpdatePreview();
    }

    private bool TryDraft(out MoveValues? result)
    {
        result = null;
        string flagsText = (FlagsBox.Text ?? string.Empty).Trim();
        if (flagsText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            flagsText = flagsText[2..];
        if (_selected is null ||
            !byte.TryParse(TurnsMinBox.Text, out byte turnsmin) ||
            !byte.TryParse(TurnsMaxBox.Text, out byte turnsmax) ||
            !sbyte.TryParse(RecoilBox.Text, out sbyte recoil) ||
            !byte.TryParse(HealingBox.Text, out byte healing) ||
            !TryChoice(StatOneBox, out int stat1) ||
            !TryChoice(StatTwoBox, out int stat2) ||
            !TryChoice(StatThreeBox, out int stat3) ||
            !sbyte.TryParse(StageOneBox.Text, out sbyte stat1stage) ||
            !sbyte.TryParse(StageTwoBox.Text, out sbyte stat2stage) ||
            !sbyte.TryParse(StageThreeBox.Text, out sbyte stat3stage) ||
            !byte.TryParse(StageRateOneBox.Text, out byte stat1rate) ||
            !byte.TryParse(StageRateTwoBox.Text, out byte stat2rate) ||
            !byte.TryParse(StageRateThreeBox.Text, out byte stat3rate) ||
            !byte.TryParse(InflictCountBox.Text, out byte inflictcount) ||
            !TryChoice(RefreshTypeBox, out int refreshtype) ||
            !byte.TryParse(RefreshRateBox.Text, out byte refreshrate) ||
            !byte.TryParse(MovePower.Text, out byte power) ||
            !byte.TryParse(MoveAccuracy.Text, out byte acc) ||
            !byte.TryParse(MovePP.Text, out byte pp) ||
            !int.TryParse(MovePriority.Text, out int priority) ||
            priority < -8 || priority > 8 ||
            MoveCategory.SelectedIndex < 0 || MoveCategory.SelectedIndex > 2 ||
            MoveType.SelectedIndex < 0 || MoveType.SelectedIndex >= _moves.Types.Count ||
            !uint.TryParse(flagsText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint flags) ||
            !TryChoice(QualityBox, out int quality) ||
            !byte.TryParse(MinHitsBox.Text, out byte hitmin) || hitmin > 15 ||
            !byte.TryParse(MaxHitsBox.Text, out byte hitmax) || hitmax > 15 ||
            !TryChoice(InflictBox, out int inflict) ||
            !byte.TryParse(InflictRateBox.Text, out byte inflictpercent) ||
            !byte.TryParse(CritBox.Text, out byte critstage) ||
            !byte.TryParse(FlinchBox.Text, out byte flinch) ||
            !ushort.TryParse(EffectBox.Text, out ushort effectsequence) ||
            !TryChoice(TargetBox, out int target) ||
            !TryChoice(ZMoveBox, out int zmove) ||
            !byte.TryParse(ZPowerBox.Text, out byte zpower) ||
            !TryChoice(ZEffectBox, out int zeffect))
            return false;
        result = new MoveValues(MoveType.SelectedIndex, MoveCategory.SelectedIndex,
            power, acc, pp, priority)
        {
            Quality = quality,
            HitMin = hitmin,
            HitMax = hitmax,
            Inflict = inflict,
            InflictPercent = inflictpercent,
            CritStage = critstage,
            Flinch = flinch,
            EffectSequence = effectsequence,
            Target = target,
            ZMove = zmove,
            ZPower = zpower,
            ZEffect = zeffect,
            Flags = flags,
            TurnsMin = turnsmin,
            TurnsMax = turnsmax,
            Recoil = recoil,
            Healing = healing,
            Stat1 = stat1,
            Stat2 = stat2,
            Stat3 = stat3,
            Stat1Stage = stat1stage,
            Stat2Stage = stat2stage,
            Stat3Stage = stat3stage,
            Stat1Rate = stat1rate,
            Stat2Rate = stat2rate,
            Stat3Rate = stat3rate,
            InflictCount = inflictcount,
            RefreshType = refreshtype,
            RefreshRate = refreshrate,
        };
        return true;
    }

    private readonly List<System.Windows.Controls.Control> _highlightedErrors = [];

    private List<string> ValidateFields()
    {
        foreach (var field in _highlightedErrors)
        {
            field.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
            field.ClearValue(System.Windows.Controls.Control.BorderThicknessProperty);
            field.ClearValue(FrameworkElement.ToolTipProperty);
        }
        _highlightedErrors.Clear();
        var errors = new List<string>();

        void Error(System.Windows.Controls.Control field, string message)
        {
            errors.Add(message);
            field.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF626D")!);
            field.BorderThickness = new Thickness(2);
            field.ToolTip = message;
            _highlightedErrors.Add(field);
        }
        void Number(System.Windows.Controls.TextBox field, string label, int min, int max)
        {
            if (!int.TryParse(field.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int value) || value < min || value > max)
                Error(field, $"{label}: número entre {min} y {max}.");
        }
        void Choice(System.Windows.Controls.ComboBox field, string label)
        {
            if (field.SelectedValue is not int)
                Error(field, $"{label}: seleccioná una opción de la lista.");
        }

        if (_selected is not null)
        {
            Number(TurnsMinBox, "Turnos mínimos", 0, 255);
            Number(TurnsMaxBox, "Turnos máximos", 0, 255);
            Number(RecoilBox, "Retroceso", -128, 127);
            Number(HealingBox, "Curación", 0, 255);
            Number(StageOneBox, "Nivel de stat 1", -128, 127);
            Number(StageTwoBox, "Nivel de stat 2", -128, 127);
            Number(StageThreeBox, "Nivel de stat 3", -128, 127);
            Number(StageRateOneBox, "Probabilidad stat 1", 0, 255);
            Number(StageRateTwoBox, "Probabilidad stat 2", 0, 255);
            Number(StageRateThreeBox, "Probabilidad stat 3", 0, 255);
            Number(InflictCountBox, "Duración del estado", 0, 255);
            Number(RefreshRateBox, "Probabilidad Poké Relax", 0, 255);
            Number(MovePower, "Potencia", 0, 255);
            Number(MoveAccuracy, "Precisión", 0, 255);
            Number(MovePP, "PP", 0, 255);
            Number(MovePriority, "Prioridad", -8, 8);
            Number(MinHitsBox, "Golpes mínimos", 0, 15);
            Number(MaxHitsBox, "Golpes máximos", 0, 15);
            Number(InflictRateBox, "Probabilidad del estado", 0, 255);
            Number(CritBox, "Críticos", 0, 255);
            Number(FlinchBox, "Retroceso del objetivo", 0, 255);
            Number(EffectBox, "Secuencia del efecto", 0, 65535);
            Number(ZPowerBox, "Potencia Z", 0, 255);
            Choice(StatOneBox, "Stat 1");
            Choice(StatTwoBox, "Stat 2");
            Choice(StatThreeBox, "Stat 3");
            Choice(RefreshTypeBox, "Poké Relax");
            Choice(QualityBox, "Clasificación del efecto");
            Choice(InflictBox, "Estado infligido");
            Choice(TargetBox, "Objetivo");
            Choice(ZMoveBox, "Movimiento Z asociado");
            Choice(ZEffectBox, "Efecto Z");
            if (MoveType.SelectedIndex < 0)
                Error(MoveType, "Tipo del movimiento: elegí un tipo.");
            if (MoveCategory.SelectedIndex is < 0 or > 2)
                Error(MoveCategory, "Categoría del movimiento: elegí un valor.");
            string flags = (FlagsBox.Text ?? "").Trim();
            if (flags.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                flags = flags[2..];
            if (!uint.TryParse(flags, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out _))
                Error(FlagsBox, "Flags: código hexadecimal de 32 bits inválido.");
        }
        return errors;
    }

    private bool HasChanges()
    {
        if (_saved is null)
            return false;
        return !TryDraft(out var current) || current != _saved;
    }

    private bool ConfirmDiscard() =>
        !HasChanges() || StudioDialog.Confirm(Window.GetWindow(this), "Cambios pendientes",
            "Hay modificaciones sin guardar.",
            "Si continuás, los cambios del movimiento actual se perderán.");

    private void UpdatePreview()
    {
        if (_loading || PreviewPower is null || MovePower is null || ChangeStateBadge is null)
            return;
        PreviewType.Text = MoveType.SelectedItem as string ?? "—";
        PreviewCategory.Text = (MoveCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "—";
        PreviewPower.Text = MovePower.Text;

        var errors = ValidateFields();
        MoveValues? next = null;
        bool valid = errors.Count == 0 && TryDraft(out next);
        if (!valid && errors.Count == 0 && _selected is not null)
            errors.Add("Un selector no pudo interpretarse. Restablecé el movimiento y volvé a intentarlo.");
        if (ValidationSummary is not null)
        {
            ValidationSummary.Text = errors.Count == 0 ? "" :
                "Revisar: " + string.Join("  ·  ", errors.Take(4)) +
                (errors.Count > 4 ? $"  (+{errors.Count - 4} más)" : "");
            ValidationSummary.Visibility = errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        bool modified = _saved is not null && (!valid || next != _saved);
        string bg = !valid ? "#6B4434" : modified ? "#75522E" : "#31544A";
        string fg = !valid ? "#F6C1A7" : modified ? "#F8D49A" : "#B6E8CC";
        ChangeStateText.Text = !valid ? "Revisar campos" : modified ? "Cambios sin guardar" : "Guardado";
        ChangeStateBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)!);
        ChangeStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg)!);
    }


    private static void AssignCatalog(System.Windows.Controls.ComboBox box,
        IReadOnlyList<MoveChoice> normal, int selectedId)
    {
        box.ItemsSource = normal.Any(c => c.Id == selectedId)
            ? normal
            : normal.Concat([new MoveChoice(selectedId, "ID original no catalogado")]).ToArray();
        box.SelectedValue = selectedId;
    }

    private static bool TryChoice(System.Windows.Controls.ComboBox box, out int id)
    {
        id = 0;
        if (box.SelectedValue is int selected)
        {
            id = selected;
            return true;
        }
        return false;
    }

    private void CreateFlagCheckBoxes()
    {
        var flagNames = Enum.GetNames(_moves.SupportsZMoves ? typeof(MoveFlag7) : typeof(MoveFlag6));
        for (int i = 0; i < 32; i++)
        {
            int bit = i;
            var check = new System.Windows.Controls.CheckBox
            {
                Content = i + 1 < flagNames.Length ? flagNames[i + 1] : $"Flag {i + 1}",
                Foreground = System.Windows.Media.Brushes.White,
                Width = 175,
                Padding = new Thickness(2, 6, 4, 6),
                Margin = new Thickness(0, 1, 0, 1),
                ToolTip = $"Bit {i} del campo Flags"
            };
            check.Checked += (_, _) => FlagCheckChanged(bit, true);
            check.Unchecked += (_, _) => FlagCheckChanged(bit, false);
            _flagChecks.Add(check);
            FlagsPanel.Children.Add(check);
        }
    }

    private void FlagCheckChanged(int bit, bool enabled)
    {
        if (_updatingFlags || _loading || FlagsBox is null)
            return;
        string txt = (FlagsBox.Text ?? "").Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (!uint.TryParse(txt, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint flags))
            return;
        flags = enabled ? flags | (1u << bit) : flags & ~(1u << bit);
        FlagsBox.Text = flags.ToString("X8");
    }

    private void SyncFlagCheckboxes(uint flags)
    {
        _updatingFlags = true;
        try
        {
            for (int i = 0; i < _flagChecks.Count; i++)
                _flagChecks[i].IsChecked = (flags & (1u << i)) != 0;
        }
        finally
        {
            _updatingFlags = false;
        }
    }

    private void FlagsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) _tools.InvalidatePreview();
        if (!_loading && FlagsBox is not null && !_updatingFlags)
        {
            string txt = (FlagsBox.Text ?? "").Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            if (uint.TryParse(txt, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint flags))
                SyncFlagCheckboxes(flags);
        }
        UpdatePreview();
    }

    private void Value_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) _tools.InvalidatePreview();
        UpdatePreview();
    }
    private void Text_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loading) _tools.InvalidatePreview();
        UpdatePreview();
    }

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
        var errors = ValidateFields();
        if (errors.Count != 0)
        {
            UpdatePreview();
            _highlightedErrors.FirstOrDefault()?.Focus();
            StudioDialog.Notice(Window.GetWindow(this), "Revisá los campos marcados",
                string.Join(Environment.NewLine, errors.Take(8)),
                errors.Count > 8 ? $"Hay {errors.Count - 8} errores adicionales resaltados en rojo." :
                                  "Cada campo inválido tiene un borde rojo y una descripción al pasar el cursor.");
            return;
        }
        if (!TryDraft(out var next) || next is null)
        {
            StudioDialog.Error(Window.GetWindow(this), "No se pudo validar",
                "No fue posible convertir uno de los valores. Restablecé el movimiento y volvé a intentarlo.");
            return;
        }
        if (next == _saved)
        {
            StatusLabel.Text = "No hay modificaciones pendientes.";
            return;
        }
        if (!StudioDialog.Confirm(Window.GetWindow(this), "Confirmar guardado",
            "Se actualizará el GARC de movimientos del proyecto extraído.",
            "Studio no realiza copias de seguridad automáticas. Asegurate de trabajar con una copia del juego."))
            return;
        try
        {
            _tools.InvalidatePreview();
            _moves.Save(_selected.Id, next);
            _saved = _moves.Read(_selected.Id);
            UpdatePreview();
            StatusLabel.Text = $"Movimiento #{_selected.Id:000} guardado y verificado.";
            StudioDialog.Notice(Window.GetWindow(this), "Movimiento guardado",
                "Los cambios se guardaron y se comprobó la integridad de la tabla.");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Error al guardar movimiento.";
            StudioDialog.Error(Window.GetWindow(this), "No se pudo guardar", ex.Message);
        }
    }

    

    public bool ConfirmLeave() => ConfirmDiscard();

    public bool HasPendingChanges => HasChanges();
}
