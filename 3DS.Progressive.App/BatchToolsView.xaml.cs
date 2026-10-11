using System.Globalization;
using System.Security.Cryptography;
using System.IO;
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
    private BatchAction _action;
    private int _lastSeed;
    private string? _templatePath;
    private readonly Dictionary<BatchAction, (Dictionary<string, bool> Checks, Dictionary<string, string> Numbers)> _options = [];
    public event Action? BackRequested;
    internal BatchAction ActiveAction => _action;
    internal int LastSeed => _lastSeed;

    public BatchToolsView() => InitializeComponent();

    public void Configure(bool moves, int generation, bool hms, bool special, bool banActive,
        Func<bool> prepare, Func<int, IReadOnlyList<int>> scope,
        Func<BatchAction, BatchSettings, IReadOnlyList<int>, BatchPreview> generate, Action<BatchPreview> apply,
        Func<IReadOnlyList<int>, string> export)
    {
        _moves = moves; _generation = generation; _hms = hms; _special = special; _banActive = banActive;
        _prepare = prepare; _scope = scope; _generate = generate; _apply = apply; _export = export;
        _configured = true;
        _options.Clear();
        _checks.Clear(); _numbers.Clear();
        _templatePath = null;
        TemplateName.Text = _moves ? "Balance Progressive incluido" : "Estadísticas Progressive incluidas";
        OpenAction(moves ? BatchAction.MoveRandomize : BatchAction.PersonalRandomize);
    }

    public void OpenAction(BatchAction action)
    {
        bool valid = _moves ? action is BatchAction.MoveRandomize or BatchAction.Metronome or BatchAction.MoveBalance
            : action is BatchAction.PersonalRandomize or BatchAction.PersonalModify;
        if (!valid) throw new ArgumentException("Esta acción no pertenece al editor actual.");
        RememberOptions();
        _action = action;
        ActionTitle.Text = action switch
        {
            BatchAction.PersonalRandomize => "Randomizar Pokémon", BatchAction.PersonalModify => "Modificar Pokémon",
            BatchAction.MoveRandomize => "Randomizar movimientos", BatchAction.Metronome => "Modo Metrónomo",
            _ => "Balance y templates",
        };
        BuildOptions();
        InvalidatePreview();
        OptionsScroll.ScrollToTop();
        ValidationText.Text = "";
    }

    private void RememberOptions()
    {
        if (_checks.Count + _numbers.Count == 0) return;
        _options[_action] = (_checks.ToDictionary(p => p.Key, p => p.Value.IsChecked == true),
            _numbers.ToDictionary(p => p.Key, p => p.Value.Box.Text));
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
            SeedPanel.Visibility = IsRandom ? Visibility.Visible : Visibility.Collapsed;
            TemplatePanel.Visibility = _action == BatchAction.MoveBalance || (_action == BatchAction.PersonalModify && _generation == 7) ? Visibility.Visible : Visibility.Collapsed;
            TemplateCaption.Text = _moves ? "Template de movimientos" : "Template de estadísticas";
            switch (_action)
            {
                case BatchAction.MoveRandomize:
                    Check("MoveTypes", "Tipos de movimientos", true);
                    Check("MoveCategories", "Categoría física/especial (conservar movimientos de estado)", true);
                    ActionHelp.Text = "Forcejeo (165) y Maldición (174) quedan protegidos, como en el editor original.";
                    break;
                case BatchAction.Metronome:
                    ActionHelp.Text = "En el alcance elegido: Metrónomo (118) tendrá 40 PP; Placaje (33), 1 PP; los demás, 0 PP. Solo modifica PP.";
                    break;
                case BatchAction.MoveBalance:
                    Check("BattlePatches", "Aplicar mecánicas de combate del template", true);
                    Check("MoveDescriptions", "Actualizar descripciones en español", true);
                    ActionHelp.Text = "Usa los templates de pk3DS Progressive. Revisá los movimientos y archivos de combate antes de aplicar.";
                    break;
                case BatchAction.PersonalRandomize:
                    Check("Stats", "Variar estadísticas base"); Number("Deviation", "Variación de estadísticas (%)", 25, 0, 100);
                    var stats = new WrapPanel(); OptionsPanel.Children.Add(stats);
                    foreach (var slot in new[] { ("HP", "PS"), ("ATK", "Ataque"), ("DEF", "Defensa"), ("SPE", "Velocidad"), ("SPA", "At. esp."), ("SPD", "Def. esp.") })
                        Check(slot.Item1, slot.Item2, false, stats);
                    Check("Shuffle", "Barajar estadísticas (preservar BST)");
                    Check("Types", "Randomizar tipos"); Number("SingleTypeChance", "Probabilidad de un solo tipo (%)", 50, 0, 100);
                    Check("Abilities", "Randomizar habilidades", true); Check("WonderGuard", "Permitir Wonder Guard");
                    Check("BanTraps", "Excluir Shadow Tag y Arena Trap", true);
                    if (_banActive) _checks["BanTraps"].IsEnabled = false;
                    Check("Items", "Randomizar objetos equipados", true); Check("Catch", "Randomizar ratio de captura");
                    Check("Eggs", "Randomizar grupos huevo"); Number("SameEggChance", "Probabilidad de repetir grupo huevo (%)", 50, 0, 100);
                    Check("Tms", "Randomizar compatibilidad MT", true);
                    if (_hms) Check("Hms", "Randomizar compatibilidad MO", true);
                    Check("TypeTutors", "Randomizar tutores por tipo", true);
                    if (_special) Check("SpecialTutors", _generation == 7 ? "Randomizar tutores de playa" : "Randomizar tutores ORAS", true);
                    ActionHelp.Text = "Usa el motor de pk3DS. La variación y el barajado protegen a los Pokémon con 1 PS. Los parámetros y la semilla se aplican a los registros guardados.";
                    break;
                case BatchAction.PersonalModify:
                    if (_generation == 7) Check("PersonalTemplate", "Aplicar estadísticas desde un template Progressive");
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
            if (_options.TryGetValue(_action, out var remembered))
            {
                foreach (var pair in remembered.Checks)
                    if (_checks.TryGetValue(pair.Key, out var check)) check.IsChecked = pair.Value;
                foreach (var pair in remembered.Numbers)
                    if (_numbers.TryGetValue(pair.Key, out var number)) number.Box.Text = pair.Value;
            }
            if (_banActive && _checks.TryGetValue("BanTraps", out var ban)) { ban.IsChecked = true; ban.IsEnabled = false; }
            UpdateDependencies();
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
        if (Checked("Stats") && !new[] { "HP", "ATK", "DEF", "SPE", "SPA", "SPD" }.Any(Checked))
            errors.Add("Variación de estadísticas: elegí al menos una estadística.");
        if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
        // Disabled numeric options never parse an irrelevant invalid field.
        decimal Active(string check, string number, decimal fallback) => Checked(check) ? Value(number) : fallback;
        return new BatchSettings
        {
            Seed = IsRandom ? _lastSeed : 0, Stats = Checked("Stats"), Shuffle = Checked("Shuffle"),
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
            PersonalTemplate = Checked("PersonalTemplate"),
            TemplatePath = _templatePath, BattlePatches = Checked("BattlePatches"), MoveDescriptions = Checked("MoveDescriptions") && Checked("BattlePatches"),
        };
    }

    private static Brush ErrorBrush() => new SolidColorBrush(Color.FromRgb(255, 98, 109));
    private bool IsRandom => _action is BatchAction.PersonalRandomize or BatchAction.MoveRandomize;

    private void UpdateDependencies()
    {
        foreach (var pair in _numbers)
            pair.Value.Box.IsEnabled = pair.Key switch
            {
                "Deviation" => Checked("Stats"), "SingleTypeChance" => Checked("Types"),
                "SameEggChance" => Checked("Eggs"), "ExperiencePercent" => Checked("ScaleExperience"),
                "CatchValue" => Checked("SetCatch"), "CallValue" => Checked("SetCall"), _ => true,
            };
        foreach (string slot in new[] { "HP", "ATK", "DEF", "SPE", "SPA", "SPD" })
            if (_checks.TryGetValue(slot, out var check)) check.IsEnabled = Checked("Stats");
        if (_checks.TryGetValue("WonderGuard", out var guard)) guard.IsEnabled = Checked("Abilities");
        TemplatePanel.IsEnabled = _moves || Checked("PersonalTemplate");
        if (_checks.TryGetValue("MoveDescriptions", out var descriptions)) descriptions.IsEnabled = Checked("BattlePatches");
    }

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (_building) return;
        UpdateDependencies();
        InvalidatePreview();
        ValidationText.Text = "";
    }

    private void Back_Click(object sender, RoutedEventArgs e) { RememberOptions(); InvalidatePreview(); BackRequested?.Invoke(); }
    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        _options.Remove(_action); BuildOptions(); InvalidatePreview(); ValidationText.Text = "";
    }
    private void LoadTemplate_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Templates CSV (*.csv)|*.csv", Title = _moves ? "Template de movimientos" : "Template de estadísticas de Pokémon" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        _templatePath = picker.FileName;
        TemplateName.Text = Path.GetFileName(_templatePath);
        TemplateName.ToolTip = _templatePath;
        InvalidatePreview(); ValidationText.Text = "";
    }
    private void DefaultTemplate_Click(object sender, RoutedEventArgs e)
    {
        _templatePath = null; TemplateName.Text = _moves ? "Balance Progressive incluido" : "Estadísticas Progressive incluidas"; TemplateName.ToolTip = null;
        InvalidatePreview(); ValidationText.Text = "";
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (!_configured) return;
        InvalidatePreview(); ValidationText.Text = "";
        try
        {
            ReadSettings();
            if (_prepare?.Invoke() != true) return;
            if (IsRandom)
            {
                int next;
                do { next = RandomNumberGenerator.GetInt32(int.MaxValue); } while (next == _lastSeed);
                _lastSeed = next;
                SeedText.Text = next.ToString(CultureInfo.InvariantCulture);
            }
            BatchSettings settings = ReadSettings();
            _preview = _generate!(_action, settings, _scope!(ScopeBox.SelectedIndex));
            ChangesList.ItemsSource = _preview.Changes;
            ChangesList.Visibility = _preview.Changes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            SummaryText.Text = $"{_preview.Action} · {_preview.ScopeCount} entradas revisadas · {_preview.ChangedCount} modificadas · {_preview.Changes.Count} campos · {_preview.AdditionalFileCount} archivos adicionales."
                + (IsRandom ? $" Semilla: {_preview.Seed}." : "")
                + (_preview.ChangedCount == 0 ? " No hay cambios para aplicar." : " Revisá la lista antes de aplicar.");
            CopyChangesButton.IsEnabled = _preview.Changes.Count > 0;
            ApplyButton.IsEnabled = _preview.Changes.Count > 0;
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
            $"Se guardarán {proposal.ChangedCount} entradas de {proposal.ScopeCount} revisadas y {proposal.AdditionalFileCount} archivos adicionales. Se volverán a leer y verificar los archivos propuestos.")) return;
        try
        {
            _apply(proposal);
            if (Checked("BanTraps"))
            {
                _banActive = true;
                if (_checks.TryGetValue("BanTraps", out var ban)) { ban.IsChecked = true; ban.IsEnabled = false; }
            }
            InvalidatePreview();
            SummaryText.Text = $"Aplicación completada: {proposal.ChangedCount} entradas guardadas; archivos verificados.";
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
