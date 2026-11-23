using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>
/// Map and event editor for 3DS games. It only talks to an
/// <see cref="IMapEditorBackend"/>; each family provides its own adapter.
/// While an adapter is read-only, no control writes anything.
/// </summary>
internal sealed class Map3dsEditorView : UserControl
{
    internal const string ReadOnlyNotice = "Solo lectura: este juego todavía no tiene campos de eventos con escritura verificada.";
    internal const string GameplayNotice = "Escritura verificada en el archivo, pero aún no probada en el juego. Guarda en una copia de trabajo y compruébalo en el emulador.";
    private IMapEditorBackend _backend;
    /// <summary>Writes a move through the project (preview text, then commit). Null when the view is read-only.</summary>
    private readonly Map3dsMover? _mover;
    private TextBox? _moveX, _moveY;
    private Button? _moveSave, _moveRestore;
    private TextBlock? _moveResult;
    private (MapEventView Npc, int X, int Y)? _pendingMove;
    private ComboBox? _viewDirection;
    private TextBox? _viewAngle, _viewSight;
    private Button? _viewSave;
    private TextBlock? _viewResult;
    private (MapEventView Npc, double Facing, int? Sight)? _pendingView;
    internal ComboBox? ViewDirection => _viewDirection;
    internal TextBox? ViewAngle => _viewAngle;
    internal TextBox? ViewSight => _viewSight;
    internal Button? ViewSave => _viewSave;
    internal TextBlock? ViewResult => _viewResult;
    internal (MapEventView Npc, double Facing, int? Sight)? PendingView => _pendingView;
    private IMapTrainerViews? Views => _backend as IMapTrainerViews;
    private Button? _nurseButton, _nurseSave;
    private TextBox? _nurseX, _nurseY;
    private ComboBox? _nurseDirection;
    private TextBlock? _nurseResult;
    private (int X, int Y, double Facing)? _pendingNurse;
    internal Button? NurseButton => _nurseButton;
    internal Button? NurseSave => _nurseSave;
    internal TextBox? NurseX => _nurseX;
    internal TextBox? NurseY => _nurseY;
    internal ComboBox? NurseDirection => _nurseDirection;
    internal TextBlock? NurseResult => _nurseResult;
    internal (int X, int Y, double Facing)? PendingNurse => _pendingNurse;
    private readonly Map3dsCanvas _canvas = new();
    private readonly ListBox _zones, _events;
    private readonly TextBox _search;
    private readonly StackPanel _details;
    private readonly TextBlock _status, _zoneTitle, _legend;
    private readonly Dictionary<string, CheckBox> _layerBoxes = [];
    private IReadOnlyList<MapEventView> _current = [];
    private bool _syncing;

    internal Map3dsCanvas Canvas => _canvas;
    internal CheckBox? GraphicsToggle => _layerBoxes.GetValueOrDefault("Graphics");
    internal CheckBox? DiagnosticToggle => _layerBoxes.GetValueOrDefault("Diagnostic");
    internal ListBox ZoneList => _zones;
    internal ListBox EventList => _events;
    internal StackPanel Details => _details;
    internal TextBlock Status => _status;
    internal IMapEditorBackend Backend => _backend;
    internal TextBox? MoveX => _moveX;
    internal TextBox? MoveY => _moveY;
    internal Button? MoveSave => _moveSave;
    internal Button? MoveRestore => _moveRestore;
    internal TextBlock? MoveResult => _moveResult;
    internal (MapEventView Npc, int X, int Y)? PendingMove => _pendingMove;
    internal int? CurrentZone { get; private set; }

    internal static IMapEditorBackend CreateBackend(GameProjectSession session)
    {
        var config = session.GameConfiguration;
        if (config.XY || config.ORAS) return Gen6MapBackend.FromConfig(config);
        if (config.USUM) return pk3DS.Core.Maps.Gen7.Gen7MapBackend.FromConfig(config);
        throw new NotSupportedException("El editor de mapas 3DS todavía no admite este juego. Sol/Luna está en desarrollo.");
    }

    internal static bool Supports(GameProjectSession session) => session.GameConfiguration.XY || session.GameConfiguration.ORAS || session.GameConfiguration.USUM;

    internal Map3dsEditorView(GameProjectSession session) : this(CreateBackend(session), Map3dsMover.For(session)) { }

    internal Map3dsEditorView(IMapEditorBackend backend, Map3dsMover? mover = null)
    {
        _backend = backend;
        _mover = backend.Mutations is not null ? mover : null;
        Background = Res("Bg");
        var root = new DockPanel { Margin = new Thickness(18) };
        Content = root;

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Editor de mapas · " + backend.Title, FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = Res("TextMain") });
        string note = backend.Capabilities.Note + (backend.Capabilities.Editing ? "" : " " + ReadOnlyNotice);
        top.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), Margin = new Thickness(0, 6, 0, 0) });

        _status = new TextBlock { Foreground = Res("TextSoft"), Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        root.Children.Add(grid);

        // Zones
        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        _search = new TextBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Buscar zona por número o nombre" };
        System.Windows.Automation.AutomationProperties.SetName(_search, "Buscar zona");
        DockPanel.SetDock(_search, Dock.Top); left.Children.Add(_search);
        _zones = List();
        System.Windows.Automation.AutomationProperties.SetName(_zones, "Zonas");
        left.Children.Add(_zones);
        grid.Children.Add(left);

        // Canvas + layers
        var centre = new DockPanel();
        Grid.SetColumn(centre, 1); grid.Children.Add(centre);
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(bar, Dock.Top); centre.Children.Add(bar);
        _zoneTitle = new TextBlock { Foreground = Res("TextMain"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        bar.Children.Add(_zoneTitle);
        if (backend.Capabilities.Graphics)
        {
            var gfx = new CheckBox { Content = "Modelos/texturas", IsChecked = backend.Capabilities.GraphicsByDefault, Foreground = Res("TextMain"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Map3dsCanvas.GraphicNotice };
            gfx.Checked += (_, _) => ApplyLayers(); gfx.Unchecked += (_, _) => ApplyLayers();
            _layerBoxes["Graphics"] = gfx; bar.Children.Add(gfx);
        }
        if (backend.Capabilities.DiagnosticLayer is { } diagnosticName)
        {
            var diag = new CheckBox { Content = diagnosticName, IsChecked = false, Foreground = Res("TextMain"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Map3dsCanvas.DiagnosticNotice };
            diag.Checked += (_, _) => ApplyLayers(); diag.Unchecked += (_, _) => ApplyLayers();
            _layerBoxes["Diagnostic"] = diag; bar.Children.Add(diag);
        }
        foreach (var (key, label) in new[] { ("Cells", "Permisos"), ("Blocks", "Bloques"), ("Npcs", "NPC"), ("Props", "Objetos"), ("Warps", "Warps"), ("Triggers", "Activadores") })
        {
            var box = new CheckBox { Content = label, IsChecked = true, Foreground = Res("TextMain"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            box.Checked += (_, _) => ApplyLayers(); box.Unchecked += (_, _) => ApplyLayers();
            _layerBoxes[key] = box; bar.Children.Add(box);
        }
        bar.Children.Add(Small("−", () => _canvas.ZoomCentered(1 / 1.25), "Alejar"));
        bar.Children.Add(Small("+", () => _canvas.ZoomCentered(1.25), "Acercar"));
        bar.Children.Add(Small("Encajar", () => _canvas.Fit(), "Encajar la zona en la vista"));
        if (_mover is { CanAddNurse: true } && backend is IMapNurseCreation)
        {
            _nurseButton = Small("Añadir enfermera", () => ShowNursePanel(), "Añadir una enfermera de Centro Pokémon que cura al equipo");
            _nurseButton.Margin = new Thickness(12, 0, 6, 0);
            bar.Children.Add(_nurseButton);
        }
        _legend = new TextBlock { Text = Map3dsCanvas.SchematicNotice, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(_legend, Dock.Bottom); centre.Children.Add(_legend);
        var frame = new Border { BorderBrush = Res("Stroke"), BorderThickness = new Thickness(1), Child = _canvas };
        System.Windows.Automation.AutomationProperties.SetName(_canvas, "Mapa de la zona");
        centre.Children.Add(frame);

        // Details + events
        var right = new Grid { Margin = new Thickness(12, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        _details = new StackPanel { Margin = new Thickness(10) };
        right.Children.Add(new Border { Background = Res("Panel"), BorderBrush = Res("Stroke"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _details } });
        _events = List();
        System.Windows.Automation.AutomationProperties.SetName(_events, "Eventos de la zona");
        Grid.SetRow(_events, 1); right.Children.Add(_events);

        _zones.ItemsSource = backend.Zones;
        _zones.DisplayMemberPath = nameof(MapZoneEntry.Name);
        _zones.SelectionChanged += (_, _) => { if (_zones.SelectedItem is MapZoneEntry z) ShowZone(z.Id); };
        _search.TextChanged += (_, _) => Filter();
        _events.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            var item = (_events.SelectedItem as ListBoxItem)?.Tag as MapEventView;
            _canvas.Select(item, true); ShowDetails(item);
        };
        _canvas.EventPicked += item => { ShowDetails(item); SyncList(item); };
        _canvas.CellPicked += (x, y) =>
        {
            if (_nurseX is null || _nurseY is null) return;
            _nurseX.Text = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _nurseY.Text = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
            PreviewNurse();
        };
        _canvas.Hover += text => _status.Text = text;
        ShowDetails(null);
        ApplyLayers();
        if (backend.Zones.Count > 0) _zones.SelectedIndex = 0;
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private ListBox List() => new()
    {
        Background = Res("Panel"), Foreground = Res("TextMain"), BorderBrush = Res("Stroke"), BorderThickness = new Thickness(1),
    };

    private Button Small(string text, Action click, string name)
    {
        var b = new Button { Content = text, MinWidth = 34, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 2, 8, 2), ToolTip = name };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => click();
        return b;
    }

    private void Filter()
    {
        string q = _search.Text.Trim();
        _zones.ItemsSource = q.Length == 0 ? _backend.Zones
            : _backend.Zones.Where(z => z.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }

    private void ApplyLayers()
    {
        bool On(string k) => _layerBoxes.TryGetValue(k, out var box) && box.IsChecked == true;
        bool graphics = On("Graphics");
        bool diagnostic = On("Diagnostic");
        _canvas.SetLayers(new Map3dsLayers(On("Cells"), On("Blocks"), On("Npcs"), On("Props"), On("Warps"), On("Triggers"), graphics, diagnostic));
        _legend.Text = (graphics ? Map3dsCanvas.GraphicNotice : Map3dsCanvas.SchematicNotice) + (diagnostic ? " " + Map3dsCanvas.DiagnosticNotice : "");
    }

    internal void ShowZone(int zone)
    {
        try
        {
            var layout = _backend.Resources.Layout(zone);
            _current = _backend.Events.ZoneEvents(zone);
            CurrentZone = zone;
            _canvas.SetZone(_backend.Resources, layout, _current, zone);
            var entry = _backend.Zones.First(z => z.Id == zone);
            _zoneTitle.Text = entry.Name;
            if (!Equals(_zones.SelectedItem, entry) && _zones.Items.Contains(entry)) { _zones.SelectedItem = entry; _zones.ScrollIntoView(entry); }
            int unplaced = _current.Count(e => !e.Placed);
            _status.Text = $"{_current.Count} eventos · matriz {layout.Width}×{layout.Height} bloques" + (unplaced > 0 ? $" · {unplaced} sin dibujar (ver lista)" : "");
            _syncing = true;
            _events.Items.Clear();
            foreach (var e in _current)
                _events.Items.Add(new ListBoxItem
                {
                    Tag = e, Content = (e.Placed ? "" : "⚠ ") + e.Label,
                    Foreground = new SolidColorBrush(Map3dsCanvas.KindColor(e.Kind)),
                    ToolTip = e.PositionNote,
                });
            _syncing = false;
            ShowDetails(null);
        }
        catch (MapFormatException ex)
        {
            CurrentZone = null; _canvas.Clear(); _events.Items.Clear();
            _status.Text = "No se pudo leer la zona: " + ex.Message;
        }
    }

    private void SyncList(MapEventView? item)
    {
        _syncing = true;
        _events.SelectedItem = item is null ? null : _events.Items.OfType<ListBoxItem>().FirstOrDefault(i => Equals(i.Tag, item));
        if (_events.SelectedItem is not null) _events.ScrollIntoView(_events.SelectedItem);
        _syncing = false;
    }

    private void ShowDetails(MapEventView? item)
    {
        _details.Children.Clear();
        _canvas.PickingCell = false; _canvas.SetPendingNurse(null);
        _nurseX = _nurseY = null; _nurseDirection = null; _nurseSave = null; _nurseResult = null; _pendingNurse = null;
        if (item is null)
        {
            _details.Children.Add(new TextBlock { Text = "Selecciona un evento en el mapa o en la lista.", TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft") });
            return;
        }
        _details.Children.Add(new TextBlock { Text = item.Label, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = Res("TextMain"), TextWrapping = TextWrapping.Wrap });
        if (item.PositionNote is { } why)
            _details.Children.Add(new TextBlock { Text = why, Foreground = Res("TextSoft"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        foreach (var p in item.Properties)
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(new TextBlock { Text = p.Name, Foreground = Res("TextSoft"), FontSize = 11.5 });
            row.Children.Add(new TextBlock { Text = p.Value, Foreground = Res("TextMain"), TextWrapping = TextWrapping.Wrap });
            if (p.Note is { } n) row.Children.Add(new TextBlock { Text = n, Foreground = Res("TextSoft"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
            _details.Children.Add(row);
        }
        _moveX = _moveY = null; _moveSave = null; _moveResult = null; _moveRestore = null;
        _viewDirection = null; _viewAngle = _viewSight = null; _viewSave = null; _viewResult = null; _pendingView = null;
        var view = CurrentZone is int vz && item.Placed ? Views?.View(vz, item.Kind, item.Index) : null;
        ShowViewOverlay(item, view?.Facing, view?.Sight, pending: false);
        bool movable = _mover is not null && item.Kind == MapEventKind.Npc && item.Placed && item.Properties.Any(IsPositionField);
        bool turnable = _mover is { CanSetView: true } && view is { FacingWritable: true };
        if (!movable && !turnable)
        {
            _details.Children.Add(new TextBlock { Text = ReadOnlyNotice, Foreground = Res("TextSoft"), FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            return;
        }
        if (turnable) _details.Children.Add(ViewPanel(item, view!));
        if (!movable)
        {
            AddRestore(_details);
            return;
        }
        var box = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        box.Children.Add(new TextBlock { Text = "Mover NPC", FontWeight = FontWeights.SemiBold, Foreground = Res("TextMain") });
        var coords = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        TextBox Number(string name, double value)
        {
            var t = new TextBox { Text = ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 64, Margin = new Thickness(4, 0, 10, 0) };
            System.Windows.Automation.AutomationProperties.SetName(t, name);
            return t;
        }
        coords.Children.Add(new TextBlock { Text = "X", Foreground = Res("TextSoft"), VerticalAlignment = VerticalAlignment.Center });
        coords.Children.Add(_moveX = Number("Casilla X de destino", item.X));
        coords.Children.Add(new TextBlock { Text = "Y", Foreground = Res("TextSoft"), VerticalAlignment = VerticalAlignment.Center });
        coords.Children.Add(_moveY = Number("Casilla Y de destino", item.Y));
        box.Children.Add(coords);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Small("Previsualizar", () => PreviewMove(item), "Previsualizar el movimiento en el mapa"));
        _moveSave = Small("Guardar", () => SaveMove(), "Guardar el movimiento en el proyecto");
        _moveSave.IsEnabled = false;
        buttons.Children.Add(_moveSave);
        buttons.Children.Add(Small("Cancelar", () => { _pendingMove = null; _canvas.SetPendingMove(null); if (_moveSave is not null) _moveSave.IsEnabled = false; if (_moveResult is not null) _moveResult.Text = ""; }, "Cancelar el movimiento"));
        box.Children.Add(buttons);
        _moveResult = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), Margin = new Thickness(0, 6, 0, 0) };
        box.Children.Add(_moveResult);
        box.Children.Add(new TextBlock { Text = GameplayNotice, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        AddRestore(box);
        _details.Children.Add(box);
    }

    private static bool IsPositionField(MapEventProperty p) => p.Writable && (p.Name == "Casilla" || p.Name.StartsWith("Posición", StringComparison.Ordinal));

    private void AddRestore(System.Windows.Controls.Panel box)
    {
        if (CurrentZone is int restoreZone && _mover is not null && _mover.CanRestore(restoreZone))
        {
            _moveRestore = Small("Restaurar estado anterior del área", () => RestoreArea(), "Volver, byte a byte, al estado del área antes del último cambio");
            _moveRestore.Margin = new Thickness(0, 8, 0, 0);
            box.Children.Add(_moveRestore);
        }
    }

    private static readonly (string Name, string Arrow, double Degrees)[] Cardinal = [("Arriba", "↑", 0), ("Derecha", "→", 90), ("Abajo", "↓", 180), ("Izquierda", "←", 270)];

    /// <summary>Facing (four directions or a free angle) and, where the game has one, sight range of the selected character.</summary>
    private StackPanel ViewPanel(MapEventView item, MapTrainerView view)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var box = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        box.Children.Add(new TextBlock { Text = view.Sight is null ? "Orientación del entrenador" : "Orientación y visión", FontWeight = FontWeights.SemiBold, Foreground = Res("TextMain") });
        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (view.FreeAngle)
        {
            row.Children.Add(new TextBlock { Text = "Grados", Foreground = Res("TextSoft"), VerticalAlignment = VerticalAlignment.Center });
            _viewAngle = new TextBox { Text = double.IsFinite(view.Facing) ? view.Facing.ToString("0.#", inv) : "", Width = 64, Margin = new Thickness(4, 0, 10, 0) };
            System.Windows.Automation.AutomationProperties.SetName(_viewAngle, "Orientación en grados (0 arriba, 90 derecha, 180 abajo, 270 izquierda)");
            row.Children.Add(_viewAngle);
            foreach (var (name, arrow, deg) in Cardinal)
                row.Children.Add(Small(arrow, () => { _viewAngle!.Text = deg.ToString(inv); PreviewView(item); }, "Mirar hacia " + name.ToLowerInvariant()));
        }
        else
        {
            row.Children.Add(new TextBlock { Text = "Mira hacia", Foreground = Res("TextSoft"), VerticalAlignment = VerticalAlignment.Center });
            _viewDirection = new ComboBox { Width = 110, Margin = new Thickness(4, 0, 10, 0), DisplayMemberPath = "Name", SelectedValuePath = "Degrees",
                ItemsSource = Cardinal.Select(c => new { c.Name, c.Degrees }).ToArray() };
            _viewDirection.SelectedValue = view.Facing;
            System.Windows.Automation.AutomationProperties.SetName(_viewDirection, "Dirección hacia la que mira");
            row.Children.Add(_viewDirection);
        }
        box.Children.Add(row);
        if (view.Sight is int sight)
        {
            var sightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            sightRow.Children.Add(new TextBlock { Text = "Alcance (casillas)", Foreground = Res("TextSoft"), VerticalAlignment = VerticalAlignment.Center });
            _viewSight = new TextBox { Text = sight.ToString(inv), Width = 48, Margin = new Thickness(4, 0, 0, 0), IsEnabled = view.SightWritable,
                ToolTip = view.SightWritable ? $"1–{view.MaxSight}" : "Este NPC no tiene visión; no se le añade." };
            System.Windows.Automation.AutomationProperties.SetName(_viewSight, "Alcance de visión en casillas");
            sightRow.Children.Add(_viewSight);
            box.Children.Add(sightRow);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Small("Previsualizar", () => PreviewView(item), "Previsualizar la orientación en el mapa"));
        _viewSave = Small("Guardar", () => SaveView(), "Guardar la orientación en el proyecto");
        _viewSave.IsEnabled = false;
        buttons.Children.Add(_viewSave);
        buttons.Children.Add(Small("Cancelar", () => CancelView(item), "Cancelar el cambio de orientación"));
        box.Children.Add(buttons);
        _viewResult = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), Margin = new Thickness(0, 6, 0, 0) };
        box.Children.Add(_viewResult);
        box.Children.Add(new TextBlock { Text = view.Note, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        return box;
    }

    private void ShowViewOverlay(MapEventView item, double? facing, int? sight, bool pending)
    {
        if (facing is not double f || CurrentZone is not int zone || Views is not { } views) { _canvas.SetView(null); return; }
        var cells = sight is int s && s > 0 ? views.SightCells(zone, item.Kind, item.Index, f, s) : [];
        _canvas.SetView((item, f, cells, pending));
    }

    private void CancelView(MapEventView item)
    {
        _pendingView = null;
        if (_viewSave is not null) _viewSave.IsEnabled = false;
        if (_viewResult is not null) _viewResult.Text = "";
        var view = CurrentZone is int zone ? Views?.View(zone, item.Kind, item.Index) : null;
        ShowViewOverlay(item, view?.Facing, view?.Sight, pending: false);
    }

    /// <summary>Checks the new facing/sight and draws it on the map; nothing is written.</summary>
    internal bool PreviewView(MapEventView npc)
    {
        if (_viewResult is null || _viewSave is null || CurrentZone is not int zone || Views is not { } views || views.View(zone, npc.Kind, npc.Index) is not { } view) return false;
        _pendingView = null; _viewSave.IsEnabled = false;
        double facing;
        if (_viewAngle is not null)
        {
            if (!double.TryParse(_viewAngle.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out facing) || !double.IsFinite(facing))
            { _viewResult.Text = "Escribe un ángulo en grados."; return false; }
            facing = ((facing % 360) + 360) % 360;
        }
        else if (_viewDirection?.SelectedValue is double d) facing = d;
        else { _viewResult.Text = "Elige una dirección."; return false; }
        int? sight = null;
        if (_viewSight is { IsEnabled: true })
        {
            if (!int.TryParse(_viewSight.Text, out int s)) { _viewResult.Text = "Escribe un número entero de casillas."; return false; }
            sight = s;
        }
        var check = views.ValidateView(zone, npc.Kind, npc.Index, facing, sight);
        _viewResult.Text = check.Reason;
        if (!check.Allowed) { ShowViewOverlay(npc, view.Facing, view.Sight, pending: false); return false; }
        _pendingView = (npc, facing, sight);
        ShowViewOverlay(npc, facing, sight ?? view.Sight, pending: true);
        _viewSave.IsEnabled = true;
        return true;
    }

    /// <summary>Commits the previewed facing/sight through the project history and reloads the zone from disk.</summary>
    internal bool SaveView()
    {
        if (_mover is null || _pendingView is not { } change || CurrentZone is not int zone || _viewResult is null) return false;
        try
        {
            string summary = _mover.SetView(_backend, zone, change.Npc.Index, change.Facing, change.Sight);
            _backend = _mover.Reload();
            _pendingView = null;
            ShowZone(zone);
            var turned = _current.FirstOrDefault(e => e.Kind == change.Npc.Kind && e.Index == change.Npc.Index);
            _canvas.Select(turned, true); ShowDetails(turned); SyncList(turned);
            _status.Text = summary + " · Puedes deshacerlo desde el historial.";
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or ArgumentException)
        {
            _viewResult.Text = "No se guardó: " + ex.Message;
            return false;
        }
    }

    /// <summary>Puts the area back to its state before the last move made here (a new history entry) and reloads it.</summary>
    internal bool RestoreArea()
    {
        if (_mover is null || CurrentZone is not int zone) return false;
        try
        {
            string summary = _mover.Restore(zone);
            _backend = _mover.Reload();
            _pendingMove = null; _canvas.SetPendingMove(null); _pendingView = null;
            ShowZone(zone);
            _status.Text = summary + " · Registrado en el historial.";
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException)
        {
            _status.Text = "No se restauró: " + ex.Message;
            return false;
        }
    }

    /// <summary>Checks the target cell and shows a ghost marker; nothing is written.</summary>
    internal bool PreviewMove(MapEventView npc)
    {
        if (_moveX is null || _moveY is null || _moveResult is null || _moveSave is null || CurrentZone is not int zone || _backend.Mutations is null) return false;
        _pendingMove = null; _canvas.SetPendingMove(null); _moveSave.IsEnabled = false;
        if (!int.TryParse(_moveX.Text, out int x) || !int.TryParse(_moveY.Text, out int y)) { _moveResult.Text = "Escribe números enteros para X e Y."; return false; }
        var check = _backend.Mutations.ValidateMove(zone, npc.Kind, npc.Index, x, y);
        _moveResult.Text = check.Reason;
        if (!check.Allowed) return false;
        _pendingMove = (npc, x, y);
        _canvas.SetPendingMove(_pendingMove);
        _moveSave.IsEnabled = true;
        return true;
    }

    /// <summary>Commits the previewed move through the project history and reloads the zone from disk.</summary>
    internal bool SaveMove()
    {
        if (_mover is null || _pendingMove is not { } move || CurrentZone is not int zone || _moveResult is null) return false;
        try
        {
            string summary = _mover.Move(_backend, zone, move.Npc.Index, move.X, move.Y);
            _backend = _mover.Reload();
            _pendingMove = null; _canvas.SetPendingMove(null);
            ShowZone(zone);
            var moved = _current.FirstOrDefault(e => e.Kind == move.Npc.Kind && e.Index == move.Npc.Index);
            _canvas.Select(moved, true); ShowDetails(moved); SyncList(moved);
            _status.Text = summary + " · Puedes deshacerlo desde el historial.";
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or ArgumentException)
        {
            _moveResult.Text = "No se guardó: " + ex.Message;
            return false;
        }
    }
    /// <summary>Panel to place a new nurse: cell (typed or clicked on the map), direction, preview and save.</summary>
    internal bool ShowNursePanel()
    {
        if (_mover is not { CanAddNurse: true } || _backend is not IMapNurseCreation nurses || CurrentZone is not int zone) return false;
        _canvas.Select(null, false); SyncList(null);
        ShowDetails(null);
        _details.Children.Clear();
        _details.Children.Add(new TextBlock { Text = "Añadir enfermera", FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = Res("TextMain") });
        _details.Children.Add(new TextBlock { Text = nurses.NurseSummary, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), Margin = new Thickness(0, 4, 0, 0) });
        _details.Children.Add(new TextBlock { Text = "Haz clic en una casilla del mapa o escribe sus coordenadas.", TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), Margin = new Thickness(0, 8, 0, 0) });
        var coords = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        TextBox Number(string name)
        {
            var t = new TextBox { Width = 64, Margin = new Thickness(4, 0, 10, 0) };
            System.Windows.Automation.AutomationProperties.SetName(t, name);
            return t;
        }
        coords.Children.Add(new TextBlock { Text = "X", Foreground = Res("TextSoft"), VerticalAlignment = VerticalAlignment.Center });
        coords.Children.Add(_nurseX = Number("Casilla X de la enfermera"));
        coords.Children.Add(new TextBlock { Text = "Y", Foreground = Res("TextSoft"), VerticalAlignment = VerticalAlignment.Center });
        coords.Children.Add(_nurseY = Number("Casilla Y de la enfermera"));
        _details.Children.Add(coords);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = "Mira hacia", Foreground = Res("TextSoft"), VerticalAlignment = VerticalAlignment.Center });
        _nurseDirection = new ComboBox { Width = 110, Margin = new Thickness(4, 0, 0, 0), DisplayMemberPath = "Name", SelectedValuePath = "Degrees",
            ItemsSource = Cardinal.Select(c => new { c.Name, c.Degrees }).ToArray(), SelectedValue = 180.0 };
        System.Windows.Automation.AutomationProperties.SetName(_nurseDirection, "Dirección hacia la que mira la enfermera");
        _nurseDirection.SelectionChanged += (_, _) => { if (_nurseX is { Text.Length: > 0 }) PreviewNurse(); };
        row.Children.Add(_nurseDirection);
        _details.Children.Add(row);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Small("Previsualizar", () => PreviewNurse(), "Previsualizar la enfermera en el mapa"));
        _nurseSave = Small("Guardar", () => SaveNurse(), "Guardar la enfermera en el proyecto");
        _nurseSave.IsEnabled = false;
        buttons.Children.Add(_nurseSave);
        buttons.Children.Add(Small("Cancelar", () => ShowDetails(null), "Cancelar la enfermera nueva"));
        _details.Children.Add(buttons);
        _nurseResult = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), Margin = new Thickness(0, 6, 0, 0) };
        _details.Children.Add(_nurseResult);
        _details.Children.Add(new TextBlock { Text = GameplayNotice, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        _canvas.PickingCell = true;
        _status.Text = $"Zona {zone:000}: elige la casilla de la enfermera.";
        return true;
    }

    /// <summary>Checks the cell and direction and draws the nurse on the map; nothing is written.</summary>
    internal bool PreviewNurse()
    {
        if (_nurseX is null || _nurseY is null || _nurseResult is null || _nurseSave is null || CurrentZone is not int zone || _backend is not IMapNurseCreation nurses) return false;
        _pendingNurse = null; _canvas.SetPendingNurse(null); _nurseSave.IsEnabled = false;
        if (!int.TryParse(_nurseX.Text, out int x) || !int.TryParse(_nurseY.Text, out int y)) { _nurseResult.Text = "Escribe números enteros para X e Y."; return false; }
        if (_nurseDirection?.SelectedValue is not double facing) { _nurseResult.Text = "Elige una dirección."; return false; }
        var check = nurses.ValidateAddNurse(zone, x, y, facing);
        _nurseResult.Text = check.Reason;
        if (!check.Allowed) return false;
        _pendingNurse = (x, y, facing);
        _canvas.SetPendingNurse(_pendingNurse);
        _nurseSave.IsEnabled = true;
        return true;
    }

    /// <summary>Writes the previewed nurse through the project history and reloads the zone from disk.</summary>
    internal bool SaveNurse()
    {
        if (_mover is null || _pendingNurse is not { } nurse || CurrentZone is not int zone || _nurseResult is null) return false;
        try
        {
            string summary = _mover.AddNurse(_backend, zone, nurse.X, nurse.Y, nurse.Facing);
            _backend = _mover.Reload();
            ShowZone(zone);
            var added = _current.LastOrDefault(e => e.Kind == MapEventKind.Npc && e.Placed && (int)e.X == nurse.X && (int)e.Y == nurse.Y);
            _canvas.Select(added, true); ShowDetails(added); SyncList(added);
            _status.Text = summary + " · Puedes deshacerlo desde el historial.";
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or ArgumentException)
        {
            _nurseResult.Text = "No se guardó: " + ex.Message;
            return false;
        }
    }
}
