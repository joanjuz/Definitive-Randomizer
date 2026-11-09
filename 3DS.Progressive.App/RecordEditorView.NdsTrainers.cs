using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive.Services.Nds;

namespace ThreeDS.Progressive;

public partial class RecordEditorView
{
    private bool PresentNdsTrainerMember(StackPanel identity, StackPanel moves, int slot, int species, int form, int level)
    {
        if (Editor is not NdsRecordEditor { UsesTrainerLayout: true } nds) return false;
        string prefix = $"P{slot}.";
        bool customMoves = LiveValue("HasMoves") == 1, customItems = LiveValue("HasItems") == 1;
        identity.Children.Add(Small($"Forma {form} · imagen de especie"));
        moves.Children.Add(Small(customItems ? "Objeto explícito: " + nds.ItemName(LiveValue(prefix + "Item") ?? 0) : "Sin objeto · formato del juego"));
        foreach (var (key, control) in _fields.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            if (key.EndsWith(".Item", StringComparison.Ordinal)) control.IsEnabled = customItems;
            if (key[(key.LastIndexOf('.') + 1)..].StartsWith("Move", StringComparison.Ordinal)) control.IsEnabled = customMoves;
        }
        var derived = customMoves ? null : nds.DerivedMoves(species, form, level);
        moves.Children.Add(Small(customMoves ? "Movimientos explícitos" : nds.MoveOrigin));
        for (int j = 0; j < 4; j++)
            moves.Children.Add(Small($"{j + 1}. " + (customMoves ? nds.MoveName(LiveValue(prefix + "Move" + j) ?? 0) : derived is not null ? nds.MoveName(derived[j]) : "Determinado por el juego · no calculado")));
        foreach (var text in identity.Children.OfType<TextBlock>().Concat(moves.Children.OfType<TextBlock>()))
        { text.TextWrapping = TextWrapping.Wrap; text.TextTrimming = TextTrimming.None; }
        return true;
    }

    private void PresentNdsTrainerComparison(int id)
    {
        if (Editor is not NdsRecordEditor { UsesTrainerLayout: true } nds) return;
        var states = nds.TrainerStates(id);
        int slot = _selectedSlot;
        string prefix = $"P{slot}.";
        var comparison = new StackPanel { MaxWidth = 720, Margin = new Thickness(2,8,2,8) };
        comparison.SetBinding(FrameworkElement.MaxWidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = TrainerTeamCards });
        comparison.Children.Add(Small("ORIGEN → ESTADO GUARDADO → EDICIÓN ACTUAL", false, 12));
        ((TextBlock)comparison.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, "TextMain");
        comparison.Children.Add(Small("ROM de origen: " + nds.TeamMemberDescription(states.Original, slot)));
        comparison.Children.Add(Small("Proyecto guardado: " + nds.TeamMemberDescription(states.Current, slot)));
        comparison.Children.Add(Small("Edición actual: " + nds.SpeciesName(LiveValue(prefix + "Species") ?? 0)
            + $" · forma {LiveValue(prefix + "Form")} · Nv. {LiveValue(prefix + "Level")} · "
            + (LiveValue("HasItems") == 1 ? nds.ItemName(LiveValue(prefix + "Item") ?? 0) + " (explícito)" : "sin objeto (formato del juego)")
            + " · " + (LiveValue("HasMoves") == 1 ? string.Join(" / ", Enumerable.Range(0,4).Select(i => nds.MoveName(LiveValue(prefix + "Move" + i) ?? 0))) : "por nivel (no almacenados)")));
        comparison.Children.Add(Small("La propuesta de randomización compara el proyecto guardado con el resultado antes de aplicarlo."));
        foreach (var text in comparison.Children.OfType<TextBlock>()) text.TextWrapping = TextWrapping.Wrap;
        System.Windows.Automation.AutomationProperties.SetName(comparison, "Comparación del equipo DS");
        TrainerTeamCards.Children.Add(comparison);
    }

    private void WireNdsTrainerFormats()
    {
        if (Editor is not NdsRecordEditor { UsesTrainerLayout: true } nds) return;
        foreach (string key in new[] { "HasMoves", "HasItems" })
            if (_fields.TryGetValue(key,out var format)) format.ToolTip = "Al guardar se convierte el formato de todo el equipo. Al desactivarlo se eliminan sus valores explícitos. Los demás datos se conservan.";
        for (int slot = 0; slot < 6; slot++)
        {
            string prefix = $"P{slot}.";
            if (_fields.TryGetValue(prefix + "Species", out var species) && species is System.Windows.Controls.ComboBox picker)
                picker.SelectionChanged += (_,_) => { if (_fields.TryGetValue(prefix + "Form", out var form)) SetControlValue(form, 0); };
        }
        if (_fields.TryGetValue("HasMoves", out var control) && control is System.Windows.Controls.CheckBox flag)
        {
            bool initialized = flag.IsChecked == true || _fields.Keys.Where(k => k.Contains(".Move", StringComparison.Ordinal)).Any(k => LiveValue(k) is > 0);
            flag.Checked += (_,_) =>
            {
                if (initialized) { RefreshCards(); return; }
                initialized = true;
                // Materialize derived values only after the user explicitly selects the custom-moves format.
                for (int slot = 0; slot < (LiveValue("Count") ?? 0); slot++)
                {
                    string prefix = $"P{slot}.";
                    var derived = nds.DerivedMoves(LiveValue(prefix + "Species") ?? 0, LiveValue(prefix + "Form") ?? 0, LiveValue(prefix + "Level") ?? 1);
                    if (derived is null) continue;
                    for (int i = 0; i < 4; i++) if (_fields.TryGetValue(prefix + "Move" + i, out var move)) SetControlValue(move, derived[i]);
                }
                RefreshCards();
            };
        }
    }
}
