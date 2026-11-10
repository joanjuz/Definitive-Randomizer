using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Services.ProjectDetection;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBox = System.Windows.Controls.TextBox;
using Label = System.Windows.Controls.Label;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ThreeDS.Progressive;

/// <summary>
/// Universal "Abrir proyecto" (phase B2): one dialog for every platform. The user picks a file or a folder, never a console; the detector (B1) says what it
/// is and the coordinator opens it with the existing engine. The dialog stays open while the project is opened and installed, so cancelling or closing it
/// during that time discards the result and keeps the previous project. A detection that is no longer the current path can never update the dialog.
/// </summary>
internal sealed class OpenProjectDialog : Window
{
    private readonly ProjectOpenCoordinator _coordinator;
    private readonly Func<string?, ProjectDetection> _detect;
    private readonly TextBox _path = new() { MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _file = new() { Content = "Elegir archivo…", Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _folder = new() { Content = "Elegir carpeta…" };
    private readonly Button _open = new() { Content = "Abrir", MinWidth = 115, IsDefault = true, IsEnabled = false };
    private readonly Button _cancel = new() { Content = "Cancelar", MinWidth = 107, IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
    private readonly CheckBox _romhacks = new() { Content = "Admitir romhacks (tablas modificadas; beta)", Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _game = new() { TextWrapping = TextWrapping.Wrap }, _platform = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _warning = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontWeight = FontWeights.SemiBold, Visibility = Visibility.Collapsed };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontWeight = FontWeights.SemiBold, Visibility = Visibility.Collapsed };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    private int _version;
    private CancellationTokenSource? _detectCancel, _openCancel;
    private ProjectDetection? _current;
    private bool _busy, _closing, _closed;

    /// <summary>Wait before analysing typed text (not before a picked path). Checks set it to zero.</summary>
    internal TimeSpan TypingDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    /// <summary>System pickers; replaced by checks, which cannot answer a native dialog.</summary>
    internal Func<string?>? FilePicker { get; set; }
    internal Func<string?>? FolderPicker { get; set; }
    internal bool Succeeded { get; private set; }
    internal OpenResult? LastResult { get; private set; }
    internal Task DetectionTask { get; private set; } = Task.CompletedTask;
    internal Task OpeningTask { get; private set; } = Task.CompletedTask;
    internal ProjectDetection? Current => _current;
    internal bool Busy => _busy;
    internal bool IsOpenEnabled => _open.IsEnabled;
    internal string PathText { get => _path.Text; set => _path.Text = value; }
    internal bool AllowRomhacks { get => _romhacks.IsChecked == true; set => _romhacks.IsChecked = value; }
    internal string GameText => _game.Text;
    internal string PlatformText => _platform.Text;
    internal string MessageText => _message.Text;
    internal string WarningText => _warning.Visibility == Visibility.Visible ? _warning.Text : "";
    internal string ResultText => _result.Visibility == Visibility.Visible ? _result.Text : "";

    internal OpenProjectDialog(Window? owner, ProjectOpenCoordinator coordinator, bool allowRomhacks = false, Func<string?, ProjectDetection>? detect = null)
    {
        _coordinator = coordinator; _detect = detect ?? ProjectDetector.Detect; AllowRomhacks = allowRomhacks;
        Title = "Abrir proyecto"; Width = 620; SizeToContent = SizeToContent.Height; MinHeight = 380; MaxHeight = Math.Max(380, SystemParameters.WorkArea.Height - 32);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false; WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        if (owner is not null && owner.IsVisible) Owner = owner;
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "TextMain");
        Build();
        _path.TextChanged += (_, _) => StartDetection(TypingDelay);
        _file.Click += (_, _) => { if (!_busy && (FilePicker is null ? PickFile() : FilePicker()) is { } p) SetPicked(p); };
        _folder.Click += (_, _) => { if (!_busy && (FolderPicker is null ? PickFolder() : FolderPicker()) is { } p) SetPicked(p); };
        _open.Click += (_, _) => OpeningTask = OpenCurrentAsync();
        _cancel.Click += (_, _) => RequestClose();
        Closing += (_, _) => { _closing = true; _openCancel?.Cancel(); _detectCancel?.Cancel(); };
        Closed += (_, _) => _closed = true;
        Loaded += (_, _) => _path.Focus();
        Reset();
    }

    private void Build()
    {
        var border = new Border { CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "Panel"); border.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = new GridLength(47) }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        border.Child = grid; Content = border;

        var header = new Border { CornerRadius = new CornerRadius(12, 12, 0, 0) }; header.SetResourceReference(Border.BackgroundProperty, "Surface");
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var title = new TextBlock { Text = "Abrir proyecto", FontWeight = FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(19, 0, 0, 0) };
        var close = new Button { Content = "×", Width = 30, Height = 29, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 19, 0), ToolTip = "Cerrar" };
        AutomationProperties.SetName(close, "Cerrar diálogo"); AutomationProperties.SetAutomationId(close, "OpenProjectClose"); close.Click += (_, _) => RequestClose();
        var headerGrid = new Grid(); headerGrid.Children.Add(title); headerGrid.Children.Add(close); header.Child = headerGrid; grid.Children.Add(header);

        var body = new StackPanel { Margin = new Thickness(23, 20, 23, 16) };
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Elige o pega la ruta de una ROM de Nintendo DS, un checkpoint de eventos DS, la carpeta de una extracción de Nintendo 3DS o la de una extracción de Espada/Escudo. La aplicación detecta el juego y abre su editor." };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "TextSoft"); body.Children.Add(intro);
        var pathLabel = new Label { Content = "_Ruta del archivo o carpeta", Target = _path, Padding = new Thickness(0, 14, 0, 4) }; body.Children.Add(pathLabel);
        Describe(_path, "OpenProjectPath", "Ruta del archivo o carpeta del proyecto"); body.Children.Add(_path);
        var pickers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        Describe(_file, "OpenProjectChooseFile", "Elegir archivo"); Describe(_folder, "OpenProjectChooseFolder", "Elegir carpeta");
        EditorPresentation.StyleAction(_file); EditorPresentation.StyleAction(_folder); pickers.Children.Add(_file); pickers.Children.Add(_folder); body.Children.Add(pickers);

        var info = new Border { Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(14, 10, 14, 12), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        info.SetResourceReference(Border.BorderBrushProperty, "Stroke"); info.SetResourceReference(Border.BackgroundProperty, "Surface");
        var rows = new Grid(); rows.ColumnDefinitions.Add(new() { Width = new GridLength(96) }); rows.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        rows.RowDefinitions.Add(new()); rows.RowDefinitions.Add(new());
        void Row(int r, string label, TextBlock value, string id)
        {
            var l = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) }; l.SetResourceReference(TextBlock.ForegroundProperty, "TextSoft");
            Grid.SetRow(l, r); Grid.SetRow(value, r); Grid.SetColumn(value, 1); value.FontWeight = FontWeights.SemiBold; value.Margin = new Thickness(0, 0, 0, 4);
            AutomationProperties.SetAutomationId(value, id); AutomationProperties.SetName(value, label); rows.Children.Add(l); rows.Children.Add(value);
        }
        Row(0, "Juego", _game, "OpenProjectGame"); Row(1, "Plataforma", _platform, "OpenProjectPlatform");
        var infoStack = new StackPanel(); infoStack.Children.Add(rows);
        AutomationProperties.SetAutomationId(_message, "OpenProjectMessage"); AutomationProperties.SetName(_message, "Compatibilidad");
        AutomationProperties.SetAutomationId(_warning, "OpenProjectWarning"); AutomationProperties.SetName(_warning, "Advertencia");
        AutomationProperties.SetAutomationId(_result, "OpenProjectResult"); AutomationProperties.SetName(_result, "Resultado de la apertura");
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite); AutomationProperties.SetLiveSetting(_result, AutomationLiveSetting.Assertive);
        infoStack.Children.Add(_message); infoStack.Children.Add(_warning); infoStack.Children.Add(_result); info.Child = infoStack; body.Children.Add(info);
        Describe(_romhacks, "OpenProjectRomhacks", "Admitir romhacks de Nintendo DS"); body.Children.Add(_romhacks);
        body.Children.Add(_progress); AutomationProperties.SetName(_progress, "Abriendo el proyecto");
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);

        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 15, 20, 15) }; footer.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Describe(_cancel, "OpenProjectCancel", "Cancelar"); Describe(_open, "OpenProjectOpen", "Abrir proyecto");
        EditorPresentation.StyleAction(_cancel, EditorPresentation.ActionRole.Tertiary); EditorPresentation.StyleAction(_open, EditorPresentation.ActionRole.Primary);
        buttons.Children.Add(_cancel); buttons.Children.Add(_open); footer.Child = buttons; Grid.SetRow(footer, 2); grid.Children.Add(footer);
        _path.TabIndex = 0; _file.TabIndex = 1; _folder.TabIndex = 2; _romhacks.TabIndex = 3; _cancel.TabIndex = 4; _open.TabIndex = 5;
    }

    private static void Describe(FrameworkElement element, string id, string name) { AutomationProperties.SetAutomationId(element, id); AutomationProperties.SetName(element, name); }

    private string? PickFile()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "ROM o checkpoint de Nintendo DS|*.nds;*.ndsevents.json|Todos los archivos|*.*", Title = "Elegir el archivo del proyecto" };
        return picker.ShowDialog(this) == true ? picker.FileName : null;
    }

    private string? PickFolder()
    {
        var preferences = AppPreferences.Load();
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(preferences.DefaultProjectFolder) ? preferences.DefaultProjectFolder : string.Empty,
            Description = "Selecciona la carpeta raíz de una extracción de Nintendo 3DS (RomFS + ExeFS) o de Espada/Escudo (exefs + romfs).", ShowNewFolderButton = false,
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    /// <summary>A picked path is analysed at once, even when it equals the text already there.</summary>
    internal void SetPicked(string path)
    {
        if (_path.Text == path) StartDetection(TimeSpan.Zero); else _path.Text = path;
    }

    private void Reset()
    {
        _current = null; _game.Text = "—"; _platform.Text = "—"; _message.Text = "Pega una ruta o elige un archivo o una carpeta."; _warning.Visibility = Visibility.Collapsed; _romhacks.Visibility = Visibility.Collapsed; RefreshButtons();
    }

    private void StartDetection(TimeSpan delay)
    {
        if (_closing || _busy) return;
        int version = ++_version; _detectCancel?.Cancel(); var cancel = _detectCancel = new CancellationTokenSource();
        _current = null; _result.Visibility = Visibility.Collapsed; _warning.Visibility = Visibility.Collapsed; _romhacks.Visibility = Visibility.Collapsed;
        string text = _path.Text;
        if (string.IsNullOrWhiteSpace(text)) { Reset(); DetectionTask = Task.CompletedTask; return; }
        _game.Text = "…"; _platform.Text = "…"; _message.Text = "Analizando la ruta…"; RefreshButtons();
        DetectionTask = Analyse(version, text, delay, cancel.Token);
    }

    private async Task Analyse(int version, string text, TimeSpan delay, CancellationToken cancel)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancel);
            var result = await Task.Run(() => _detect(text));
            // Only the result of the path that is current now may update the dialog.
            if (version != _version || cancel.IsCancellationRequested || _closing || _closed) return;
            Show(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (version == _version && !_closing && !_closed)
        { Show(new ProjectDetection(DetectionStatus.Unknown, DetectedPlatform.None, DetectedKind.None, text, "", "No se pudo analizar la ruta: " + ex.Message, [])); }
    }

    private void Show(ProjectDetection result)
    {
        _current = result;
        _game.Text = result.Game.Length > 0 ? result.Game : "—";
        _platform.Text = result.PlatformName.Length > 0 ? result.PlatformName : "—";
        string prefix = result.Status switch { DetectionStatus.Compatible => "✔ Compatible. ", DetectionStatus.Unsupported => "✖ No admitido. ", DetectionStatus.Incomplete => "✖ Incompleto. ", DetectionStatus.Ambiguous => "✖ Ambiguo. ", DetectionStatus.Inaccessible => "✖ No accesible. ", _ => "✖ No reconocido. " };
        _message.Text = prefix + result.Message;
        _warning.Text = result.Warning is null ? "" : "⚠ " + result.Warning; _warning.Visibility = result.Warning is null ? Visibility.Collapsed : Visibility.Visible;
        _romhacks.Visibility = result.IsCandidate && result.Kind == DetectedKind.DsRom ? Visibility.Visible : Visibility.Collapsed;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        _open.IsEnabled = !_busy && _current is { IsCandidate: true };
        _path.IsEnabled = _file.IsEnabled = _folder.IsEnabled = _romhacks.IsEnabled = !_busy;
        _progress.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
    }

    internal async Task OpenCurrentAsync()
    {
        if (_busy || _closing || _current is not { IsCandidate: true } candidate) return;
        _busy = true; _openCancel = new CancellationTokenSource(); _result.Visibility = Visibility.Collapsed; _message.Text = "Abriendo «" + candidate.Game + "»… el proyecto actual no se toca hasta que el nuevo esté verificado."; RefreshButtons();
        OpenResult result;
        try { result = await _coordinator.OpenAsync(candidate, new OpenOptions(AllowRomhacks: AllowRomhacks), _openCancel.Token); }
        catch (Exception ex) { result = new(OpenOutcome.Rejected, "No se pudo abrir el proyecto: " + ex.Message, candidate.Platform); }
        _busy = false;
        if (_closing || _closed) { LastResult = result; return; }
        LastResult = result;
        if (result.Opened) { Succeeded = true; Close(); return; }
        _message.Text = "✔ Compatible. " + candidate.Message; RefreshButtons();
        _result.Text = (result.Outcome == OpenOutcome.Cancelled ? "ℹ " : "✖ ") + result.Message; _result.Visibility = Visibility.Visible;
        _path.Focus();
    }

    private void RequestClose() { _closing = true; _openCancel?.Cancel(); _detectCancel?.Cancel(); Close(); }
}
