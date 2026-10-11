using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;
using Panel = System.Windows.Controls.Panel;
using Brush = System.Windows.Media.Brush;

namespace ThreeDS.Progressive;

/// <summary>The same inline workflow is reused by both editors; no second editor window.</summary>
public partial class BatchToolsView : UserControl
{
    private readonly Dictionary<string, CheckBox> _checks = [];
    private readonly Dictionary<string, (TextBox Box, decimal Min, decimal Max, string Label)> _numbers = [];
    private Func<bool>? _prepare;
    private Func<int, IReadOnlyList<int>>? _scope;
    private Func<BatchAction, BatchSettings, IReadOnlyList<int>, BatchPreview>? _generate;
    private Action<BatchPreview>? _apply;
    private Func<IReadOnlyList<int>, string>? _export;
    private BatchPreview? _preview;
    private bool _configured;
    private bool _building;
    private bool _moves;
    private int _generation;
    private bool _hms;
    private bool _special;
    private bool _banActive;
    private sealed record ActionChoice(BatchAction Id, string Name);

    public BatchToolsView() => InitializeComponent();

    public void Configure(bool moves, int generation, bool hms, bool special, bool banActive,
        Func<bool> prepare, Func<int, IReadOnlyList<int>> scope,
        Func<BatchAction, BatchSettings, IReadOnlyList<int>, BatchPreview> generate, Action<BatchPreview> apply,
        Func<IReadOnlyList<int>, string> export)
    {
        _moves = moves; _generation = generation; _hms = hms; _special = special; _banActive = banActive;
        _prepare = prepare; _scope = scope; _generate = generate; _apply = apply; _export = export;
        _configured = true;
        ActionBox.ItemsSource = moves
            ? new[] { new ActionChoice(BatchAction.MoveRandomize, "Randomizar tipos y categorías"), new ActionChoice(BatchAction.Metronome, "Modo Metrónomo") }
            : new[] { new ActionChoice(BatchAction.PersonalRandomize, "Randomizar Pokémon"), new ActionChoice(BatchAction.PersonalModify, "Modificar Pokémon"),
                new ActionChoice(BatchAction.BanTrappingAbilities, "Excluir Shadow Tag y Arena Trap") };
        ActionBox.SelectedIndex = 0;
        BuildOptions();
        InvalidatePreview();
    }

    public void InvalidatePreview()
    {
        if (ApplyButton is null) return;
        _preview = null;
        ApplyButton.IsEnabled = false;
        CopyChangesButton.IsEnabled = false;
        ChangesList.ItemsSource = null;
        ChangesList.Visibility = Visibility.Collapsed;
        SummaryText.Text = "Generá una vista previa para revisar el alcance y los cambios.";
    }

    private void BuildOptions()
    {
        _building = true;
        try
        {
            OptionsPanel.Children.Clear(); _checks.Clear(); _numbers.Clear();
            if (ActionBox.SelectedItem is not ActionChoice choice) return;
            SeedBox.IsEnabled = choice.Id is BatchAction.PersonalRandomize or BatchAction.MoveRandomize;
            switch (choice.Id)
            {
                case BatchAction.MoveRandomize:
                    Check("MoveTypes", "Tipos de movimientos");
                    Check("MoveCategories", "Categoría física/especial (conservar movimientos de estado)");
                    ActionHelp.Text = "Forcejeo (165) y Maldición (174) quedan protegidos, como en el editor original.";
                    break;
                case BatchAction.Metronome:
                    ActionHelp.Text = "En el alcance elegido: Metrónomo (118) tendrá 40 PP; Placaje (33), 1 PP; los demás, 0 PP. Solo modifica PP.";
                    break;
                case BatchAction.BanTrappingAbilities:
                    ActionHelp.Text = "Reemplaza Shadow Tag (23) y Arena Trap (71) por otra habilidad existente o una alternativa determinista. La exclusión seguirá activa en las próximas acciones masivas de esta sesión.";
                    break;
                case BatchAction.PersonalRandomize:
                    Check("Stats", "Variar estadísticas base"); Number("Deviation", "Variación de estadísticas (%)", 25, 0, 100);
                    var stats = new WrapPanel(); OptionsPanel.Children.Add(stats);
                    foreach (var slot in new[] { ("HP", "PS"), ("ATK", "Ataque"), ("DEF", "Defensa"), ("SPE", "Velocidad"), ("SPA", "At. esp."), ("SPD", "Def. esp.") })
                        Check(slot.Item1, slot.Item2, true, stats);
                    Check("Shuffle", "Barajar estadísticas (preservar BST)");
                    Check("Types", "Randomizar tipos"); Number("SingleTypeChance", "Probabilidad de un solo tipo (%)", 50, 0, 100);
                    Check("Abilities", "Randomizar habilidades"); Check("WonderGuard", "Permitir Wonder Guard");
                    Check("BanTraps", "Excluir Shadow Tag y Arena Trap", _banActive);
                    if (_banActive) _checks["BanTraps"].IsEnabled = false;
                    Check("Items", "Randomizar objetos equipados"); Check("Catch", "Randomizar ratio de captura");
                    Check("Eggs", "Randomizar grupos huevo"); Number("SameEggChance", "Probabilidad de repetir grupo huevo (%)", 50, 0, 100);
                    Check("Tms", "Randomizar compatibilidad MT");
                    if (_hms) Check("Hms", "Randomizar compatibilidad MO");
                    Check("TypeTutors", "Randomizar tutores por tipo");
                    if (_special) Check("SpecialTutors", _generation == 7 ? "Randomizar tutores de playa" : "Randomizar tutores ORAS");
                    ActionHelp.Text = "Usa el motor de pk3DS. La variación y el barajado protegen a los Pokémon con 1 PS. Los parámetros y la semilla se aplican a los registros guardados.";
                    break;
                case BatchAction.PersonalModify:
                    Check("RemoveEvs", "Quitar EV otorgados"); Check("SlowGrowth", "Curva de experiencia lenta");
                    Check("ScaleExperience", "Escalar experiencia base"); Number("ExperiencePercent", "Experiencia base (%)", 100, 0, 10000);
                    Check("ClearCompatibility", "Quitar MT y tutores (conservar MO de Gen 6)");
                    Check("FullTms", "Compatibilidad completa con MT");
                    if (_hms) Check("FullHms", "Compatibilidad completa con MO");
                    Check("FullTypeTutors", "Compatibilidad completa con tutores por tipo");
                    if (_special) Check("FullSpecialTutors", _generation == 7 ? "Compatibilidad completa con tutores de playa" : "Compatibilidad completa con tutores ORAS");
                    Check("QuickHatch", "Eclosión rápida (1 ciclo)");
                    Check("SetCatch", "Fijar ratio de captura"); Number("CatchValue", "Ratio de captura (0–255)", 255, 0, 255);
                    if (_generation == 7) { Check("SetCall", "Fijar ratio de llamada SOS"); Number("CallValue", "Ratio SOS (0–255)", 15, 0, 255); }
                    ActionHelp.Text = "Solo modifica las opciones marcadas y conserva los bits reservados. Quitar compatibilidades y hacerlas completas son opciones incompatibles.";
                    break;
            }
        }
        finally { _building = false; }
    }

    private void Check(string key, string label, bool initial = false, Panel? parent = null)
    {
        var check = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = initial, Margin = new Thickness(0, 6, 10, 6) };
        check.Checked += Input_Changed; check.Unchecked += Input_Changed;
        _checks.Add(key, check); (parent ?? OptionsPanel).Children.Add(check);
    }

    private void Number(string key, string label, decimal value, decimal min, decimal max)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSoft"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) });
        var box = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture) };
        Grid.SetColumn(box, 1); grid.Children.Add(box); box.TextChanged += Input_Changed;
        _numbers.Add(key, (box, min, max, label)); OptionsPanel.Children.Add(grid);
    }

    private bool Checked(string key) => _checks.TryGetValue(key, out var c) && c.IsChecked == true;
    private decimal Value(string key, decimal fallback = 0) => _numbers.TryGetValue(key, out var n)
        ? decimal.Parse(n.Box.Text, CultureInfo.CurrentCulture) : fallback;

    private BatchSettings ReadSettings()
    {
        var errors = new List<string>();
        bool random = SeedBox.IsEnabled;
        SeedBox.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        if (!int.TryParse(SeedBox.Text, out int seed) && random)
        { SeedBox.BorderBrush = ErrorBrush(); errors.Add("Semilla: usá un entero entre -2147483648 y 2147483647."); }
        foreach (var pair in _numbers)
        {
            var n = pair.Value;
            n.Box.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
            bool active = pair.Key switch
            {
                "Deviation" => Checked("Stats"), "SingleTypeChance" => Checked("Types"),
                "SameEggChance" => Checked("Eggs"), "ExperiencePercent" => Checked("ScaleExperience"),
                "CatchValue" => Checked("SetCatch"), "CallValue" => Checked("SetCall"), _ => true,
            };
            if (!active) continue;
            if (!decimal.TryParse(n.Box.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ||
                value < n.Min || value > n.Max || (pair.Key is "CatchValue" or "CallValue" && value != decimal.Truncate(value)))
            { n.Box.BorderBrush = ErrorBrush(); errors.Add($"{n.Label}: valor permitido de {n.Min} a {n.Max}."); }
        }
        if (Checked("ClearCompatibility") && new[] { "FullTms", "FullHms", "FullTypeTutors", "FullSpecialTutors" }.Any(Checked))
            errors.Add("No combines quitar compatibilidad con compatibilidad completa.");
        if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
        // Disabled numeric options never parse an irrelevant invalid field.
        decimal Active(string check, string number, decimal fallback) => Checked(check) ? Value(number) : fallback;
        return new BatchSettings
        {
            Seed = random ? seed : 0, Stats = Checked("Stats"), Shuffle = Checked("Shuffle"),
            StatSlots = new[] { "HP", "ATK", "DEF", "SPE", "SPA", "SPD" }.Select(Checked).ToArray(),
            Deviation = Active("Stats", "Deviation", 25), Types = Checked("Types"), SingleTypeChance = Active("Types", "SingleTypeChance", 50),
            Abilities = Checked("Abilities"), WonderGuard = Checked("WonderGuard"), BanTraps = Checked("BanTraps"),
            Items = Checked("Items"), Catch = Checked("Catch"), Eggs = Checked("Eggs"), SameEggChance = Active("Eggs", "SameEggChance", 50),
            Tms = Checked("Tms"), Hms = Checked("Hms"), TypeTutors = Checked("TypeTutors"), SpecialTutors = Checked("SpecialTutors"),
            RemoveEvs = Checked("RemoveEvs"), SlowGrowth = Checked("SlowGrowth"), ScaleExperience = Checked("ScaleExperience"),
            ExperiencePercent = Active("ScaleExperience", "ExperiencePercent", 100), ClearCompatibility = Checked("ClearCompatibility"),
            FullTms = Checked("FullTms"), FullHms = Checked("FullHms"), FullTypeTutors = Checked("FullTypeTutors"), FullSpecialTutors = Checked("FullSpecialTutors"),
            QuickHatch = Checked("QuickHatch"), SetCatch = Checked("SetCatch"), CatchValue = (int)Active("SetCatch", "CatchValue", 255),
            SetCall = Checked("SetCall"), CallValue = (int)Active("SetCall", "CallValue", 15),
            MoveTypes = Checked("MoveTypes"), MoveCategories = Checked("MoveCategories"),
        };
    }

    private static Brush ErrorBrush() => new SolidColorBrush(Color.FromRgb(255, 98, 109));
    private void Input_Changed(object sender, RoutedEventArgs e) { if (!_building) InvalidatePreview(); }
    private void Action_Changed(object sender, SelectionChangedEventArgs e)
    { if (_configured) { BuildOptions(); InvalidatePreview(); ValidationText.Text = ""; } }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (!_configured || ActionBox.SelectedItem is not ActionChoice choice) return;
        InvalidatePreview(); ValidationText.Text = "";
        try
        {
            BatchSettings settings = ReadSettings();
            if (_prepare?.Invoke() != true) return;
            _preview = _generate!(choice.Id, settings, _scope!(ScopeBox.SelectedIndex));
            ChangesList.ItemsSource = _preview.Changes;
            ChangesList.Visibility = _preview.Changes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            SummaryText.Text = $"{_preview.Action} · {_preview.ScopeCount} entradas revisadas · {_preview.ChangedCount} modificadas · {_preview.Changes.Count} campos."
                + (SeedBox.IsEnabled ? $" Semilla: {_preview.Seed}." : "")
                + (_preview.ChangedCount == 0 ? " No hay cambios para aplicar." : " Revisá la lista antes de aplicar.");
            CopyChangesButton.IsEnabled = _preview.Changes.Count > 0;
            ApplyButton.IsEnabled = _preview.ChangedCount > 0 || choice.Id == BatchAction.BanTrappingAbilities;
        }
        catch (Exception ex) { ValidationText.Text = ex.Message; }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_preview is null || _apply is null) return;
        // The editor may have been edited after previewing; never discard it silently.
        var proposal = _preview;
        if (_prepare?.Invoke() != true) return;
        if (_preview != proposal)
        {
            ValidationText.Text = "El editor cambió. Generá una vista previa nueva.";
            return;
        }
        if (!StudioDialog.Confirm(Window.GetWindow(this), "Aplicar al proyecto", proposal.Action,
            $"Se guardarán {proposal.ChangedCount} entradas de {proposal.ScopeCount} revisadas. El GARC se volverá a leer y verificar.")) return;
        try
        {
            _apply(proposal);
            if (ActionBox.SelectedItem is ActionChoice a && (a.Id == BatchAction.BanTrappingAbilities || Checked("BanTraps")))
            {
                _banActive = true;
                if (_checks.TryGetValue("BanTraps", out var ban)) { ban.IsChecked = true; ban.IsEnabled = false; }
            }
            InvalidatePreview();
            SummaryText.Text = $"Aplicación completada: {proposal.ChangedCount} entradas guardadas; GARC verificado.";
        }
        catch (Exception ex)
        {
            InvalidatePreview(); ValidationText.Text = ex.Message;
            StudioDialog.Error(Window.GetWindow(this), "No se pudo aplicar la propuesta", ex.Message);
        }
    }

    private void CopyTable_Click(object sender, RoutedEventArgs e)
    {
        if (_export is null || _scope is null) return;
        try
        {
            System.Windows.Clipboard.SetText(_export(_scope(ScopeBox.SelectedIndex)));
            ValidationText.Text = "";
            SummaryText.Text = "Tabla de datos guardados copiada. Podés pegarla en Excel o un editor de texto.";
        }
        catch (Exception ex) { ValidationText.Text = ex.Message; }
    }

    private void CopyChanges_Click(object sender, RoutedEventArgs e)
    {
        if (_preview is null) return;
        try
        {
            string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            var rows = new[] { "ID\tNombre\tCampo\tAntes\tDespués" }.Concat(_preview.Changes.Select(c =>
                string.Join("\t", new[] { c.Id.ToString(), c.Name, c.Field, c.Before, c.After }.Select(Clean))));
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, rows));
            ValidationText.Text = "";
        }
        catch (Exception ex) { ValidationText.Text = ex.Message; }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) { InvalidatePreview(); ValidationText.Text = ""; }
}
