using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
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
    private readonly IMapEditorBackend _backend;
    private readonly Map3dsCanvas _canvas = new();
    private readonly ListBox _zones, _events;
    private readonly TextBox _search;
    private readonly StackPanel _details;
    private readonly TextBlock _status, _zoneTitle;
    private readonly Dictionary<string, CheckBox> _layerBoxes = [];
    private IReadOnlyList<MapEventView> _current = [];
    private bool _syncing;

    internal Map3dsCanvas Canvas => _canvas;
    internal ListBox ZoneList => _zones;
    internal ListBox EventList => _events;
    internal StackPanel Details => _details;
    internal TextBlock Status => _status;
    internal IMapEditorBackend Backend => _backend;
    internal int? CurrentZone { get; private set; }

    internal static IMapEditorBackend CreateBackend(GameProjectSession session)
    {
        var config = session.GameConfiguration;
        if (config.XY || config.ORAS) return Gen6MapBackend.FromConfig(config);
        throw new NotSupportedException("El editor de mapas 3DS todavía no admite este juego. Gen7 (Sol/Luna y Ultra Sol/Ultra Luna) está en desarrollo.");
    }

    internal static bool Supports(GameProjectSession session) => session.GameConfiguration.XY || session.GameConfiguration.ORAS;

    internal Map3dsEditorView(GameProjectSession session) : this(CreateBackend(session)) { }

    internal Map3dsEditorView(IMapEditorBackend backend)
    {
        _backend = backend;
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
        foreach (var (key, label) in new[] { ("Cells", "Permisos"), ("Blocks", "Bloques"), ("Npcs", "NPC"), ("Props", "Objetos"), ("Warps", "Warps"), ("Triggers", "Activadores") })
        {
            var box = new CheckBox { Content = label, IsChecked = true, Foreground = Res("TextMain"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            box.Checked += (_, _) => ApplyLayers(); box.Unchecked += (_, _) => ApplyLayers();
            _layerBoxes[key] = box; bar.Children.Add(box);
        }
        bar.Children.Add(Small("−", () => _canvas.ZoomCentered(1 / 1.25), "Alejar"));
        bar.Children.Add(Small("+", () => _canvas.ZoomCentered(1.25), "Acercar"));
        bar.Children.Add(Small("Encajar", () => _canvas.Fit(), "Encajar la zona en la vista"));
        var legend = new TextBlock { Text = Map3dsCanvas.SchematicNotice, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(legend, Dock.Bottom); centre.Children.Add(legend);
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
        _canvas.Hover += text => _status.Text = text;
        ShowDetails(null);
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
        bool On(string k) => _layerBoxes[k].IsChecked == true;
        _canvas.SetLayers(new Map3dsLayers(On("Cells"), On("Blocks"), On("Npcs"), On("Props"), On("Warps"), On("Triggers")));
    }

    internal void ShowZone(int zone)
    {
        try
        {
            var layout = _backend.Resources.Layout(zone);
            _current = _backend.Events.ZoneEvents(zone);
            CurrentZone = zone;
            _canvas.SetZone(_backend.Resources, layout, _current);
            var entry = _backend.Zones.First(z => z.Id == zone);
            _zoneTitle.Text = entry.Name;
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
        if (!_backend.Capabilities.Editing)
            _details.Children.Add(new TextBlock { Text = ReadOnlyNotice, Foreground = Res("TextSoft"), FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
    }
}
