using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using UserControl = System.Windows.Controls.UserControl;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TabControl = System.Windows.Controls.TabControl;
using DataGrid = System.Windows.Controls.DataGrid;
using Binding = System.Windows.Data.Binding;

namespace ThreeDS.Progressive;

/// <summary>Native DS event editor. Saving creates an immutable checkpoint and updates the pending ROM rebuild.</summary>
internal sealed class NdsEventEditorView : UserControl
{
    private readonly NdsEventSession _session;
    private readonly TextBox _search = new(), _x = new(), _y = new(), _direction = new();
    private readonly TextBox _details = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _differences = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 44, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ListBox _maps = new() { DisplayMemberPath = nameof(PlatinumMap.Display) };
    private readonly TabControl _tabs = new();
    private readonly DataGrid[] _tables = new DataGrid[4];
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap }, _selection = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly Button _preview = new() { Content = "Previsualizar diferencias" }, _save = new() { Content = "Guardar cambios…", IsEnabled = false };
    private readonly Button _undo = new() { Content = "Restaurar último cambio…" }, _warp = new() { Content = "Ir al destino existente", IsEnabled = false }, _script = new() { Content = "Ver referencia de script", IsEnabled = false };
    private readonly Grid _body = new();
    private readonly ScrollViewer _inspector;
    private NdsEventProposal? _pending;
    private PlatinumMap? _map;
    private PlatinumEvent? _event;
    private bool _updating, _busy, _dirty;
    private int _activeTab;
    private DataGrid CurrentTable => _tables[Math.Clamp(_tabs.SelectedIndex, 0, 3)];
    internal event Action? Changed;

    internal NdsEventEditorView(NdsEventSession session)
    {
        _session = session; Margin = new Thickness(12);
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "TextMain");
        var root = new DockPanel(); Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Eventos · Platino CPUE/1", FontSize = 23, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Edita X/Y y dirección de NPC existentes. Objetos de fondo, warps y scripts: consulta. No añade ni elimina registros.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) });
        header.Children.Add(new TextBlock { Text = "Guardar crea un checkpoint nuevo .ndsevents.json y actualiza el borrador de la ROM. Reabre ese checkpoint para continuar; Exportar ROM reconstruye una copia nueva. Otras ediciones DS no se guardan en el checkpoint.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var actions = new WrapPanel(); footer.Children.Add(actions);
        foreach (var button in new[] { _preview, _save, _undo }) { button.Margin = new Thickness(0, 6, 8, 6); actions.Children.Add(button); }
        EditorPresentation.StyleAction(_save, EditorPresentation.ActionRole.Primary);
        EditorPresentation.StyleAction(_preview, EditorPresentation.ActionRole.Secondary);
        EditorPresentation.StyleAction(_undo, EditorPresentation.ActionRole.Tertiary);
        footer.Children.Add(_differences); footer.Children.Add(_status);
        root.Children.Add(_body);
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(205) });
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(280) });
        _body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        var searchLabel = new TextBlock { Text = "Buscar mapa (nombre o ID)", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(searchLabel, Dock.Top); left.Children.Add(searchLabel);
        DockPanel.SetDock(_search, Dock.Top); left.Children.Add(_search); left.Children.Add(_maps); _body.Children.Add(left);
        Grid.SetColumn(_tabs, 1); _body.Children.Add(_tabs);
        string[] titles = ["NPC", "Objetos", "Warps", "Scripts"];
        for (int i = 0; i < 4; i++)
        {
            var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader, EnableRowVirtualization = true, EnableColumnVirtualization = true, MinHeight = 130 };
            DarkGridTheme.Apply(table); table.RowHeight = 32; table.MinRowHeight = 32;
            foreach (var (label, property) in new[] { ("Índice", "Index"), ("ID", "Id"), ("Evento", "Description"), ("X", "X"), ("Y", "Y"), ("Script", "Script") })
                table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property), Width = property == "Description" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto, MinWidth = property switch { "Description" => 160, "Script" => 64, "Index" => 60, _ => 48 } });
            if (i == 2) { table.Columns.Add(new DataGridTextColumn { Header = "Destino", Binding = new Binding("Destination") }); table.Columns.Add(new DataGridTextColumn { Header = "Warp", Binding = new Binding("Anchor") }); }
            table.SelectionChanged += (_, _) => { if (!_updating && ReferenceEquals(CurrentTable, table)) SelectEvent(table.SelectedItem as PlatinumEvent); };
            _tables[i] = table; _tabs.Items.Add(new TabItem { Header = titles[i], Content = table }); AccessibleName(table, "Eventos DS · tabla " + titles[i]);
        }
        var inspector = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        inspector.Children.Add(_selection);
        foreach (var (label, box) in new[] { ("Coordenada X", _x), ("Coordenada Y", _y), ("Dirección (0–3)", _direction) })
        {
            inspector.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) }); inspector.Children.Add(box); AccessibleName(box, "Eventos DS · " + label);
            box.TextChanged += (_, _) => { if (!_updating) { _dirty = true; InvalidatePreview(); } };
        }
        foreach (var button in new[] { _warp, _script }) { button.Margin = new Thickness(0, 10, 0, 4); inspector.Children.Add(button); }
        _details.MinHeight = 150; _details.MaxHeight = 330; inspector.Children.Add(_details);
        _inspector = new ScrollViewer { Content = inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(_inspector, 2); _body.Children.Add(_inspector);
        AccessibleName(_search, "Eventos DS · buscar mapa por nombre o ID"); AccessibleName(_maps, "Eventos DS · mapas"); AccessibleName(_tabs, "Eventos DS · pestañas");
        AccessibleName(_details, "Eventos DS · propiedades y referencias (copiable)"); AccessibleName(_differences, "Eventos DS · diferencias previstas"); AccessibleName(_status, "Eventos DS · estado");
        foreach (var button in new[] { _preview, _save, _undo, _warp, _script }) AccessibleName(button, "Eventos DS · " + button.Content);
        _search.TextChanged += (_, _) => FilterMaps();
        _maps.SelectionChanged += (_, _) => { if (!_updating) SelectMap(_maps.SelectedItem as PlatinumMap); };
        _tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, _tabs) && !_updating) SelectEvent(CurrentTable.SelectedItem as PlatinumEvent); };
        _preview.Click += (_, _) => PreviewSelection();
        _save.Click += async (_, _) =>
        {
            if (_pending is null) return;
            var picker = new Microsoft.Win32.SaveFileDialog { Filter = "Checkpoint DS (*.ndsevents.json)|*.ndsevents.json", DefaultExt = ".ndsevents.json", AddExtension = true, FileName = Path.GetFileNameWithoutExtension(_session.SourcePath) + "-eventos-" + (_session.Revision + 1) + ".ndsevents.json", Title = "Guardar checkpoint nuevo; no sobrescribe versiones anteriores" };
            if (picker.ShowDialog(Window.GetWindow(this)) == true) await SaveToAsync(picker.FileName);
        };
        _undo.Click += (_, _) => { try { ShowProposal(_session.PreviewUndo()); _status.Text = "Restauración preparada. Guarda en un checkpoint nuevo para aplicarla."; } catch (Exception ex) when (Known(ex)) { Fail(ex); } };
        _warp.Click += (_, _) => { if (_event is { Kind: PlatinumEventKind.Warp } warp && CanNavigate(warp)) { _search.Text = ""; _maps.SelectedItem = _session.Data.Map(warp.Destination); _tabs.SelectedIndex = 2; _tables[2].SelectedItem = _tables[2].Items.Cast<PlatinumEvent>().First(e => e.Index == warp.Anchor); } };
        _script.Click += (_, _) => { var selected = _event; if (selected is null) return; _tabs.SelectedIndex = 3; _tables[3].SelectedItem = _tables[3].Items.Cast<PlatinumEvent>().FirstOrDefault(e => e.Kind == selected.Kind && e.Index == selected.Index); _details.Focus(); };
        SizeChanged += (_, _) => Adapt(); FilterMaps(); RefreshStatus();
    }
    private static void AccessibleName(DependencyObject control, string name) => AutomationProperties.SetName(control, name);
    private void Adapt()
    {
        bool compact = ActualWidth < 850;
        _body.ColumnDefinitions[0].Width = new GridLength(compact ? 165 : 205);
        _body.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(280);
        Grid.SetRow(_inspector, compact ? 1 : 0); Grid.SetColumn(_inspector, compact ? 0 : 2); Grid.SetColumnSpan(_inspector, compact ? 2 : 1);
        _inspector.MaxHeight = compact ? 230 : double.PositiveInfinity;
    }
    private void FilterMaps()
    {
        if (!ConfirmLeave()) return;
        _dirty = false; InvalidatePreview(); int? id = _map?.Id; _updating = true;
        _maps.ItemsSource = _session.Data.Maps.Where(m => m.Display.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        _maps.SelectedItem = _maps.Items.Cast<PlatinumMap>().FirstOrDefault(m => m.Id == id) ?? _maps.Items.Cast<PlatinumMap>().FirstOrDefault();
        _updating = false; SelectMap(_maps.SelectedItem as PlatinumMap);
    }
    private void SelectMap(PlatinumMap? map, bool confirm = true)
    {
        if (confirm && !ConfirmLeave()) { _updating = true; _maps.SelectedItem = _map; _updating = false; return; }
        _dirty = false; InvalidatePreview(); _map = map; _updating = true;
        var events = map is null ? Array.Empty<PlatinumEvent>() : _session.Data.Events(map.Id).ToArray();
        _tables[0].ItemsSource = events.Where(e => e.Kind == PlatinumEventKind.Npc).ToArray();
        _tables[1].ItemsSource = events.Where(e => e.Kind == PlatinumEventKind.Object || e.Kind == PlatinumEventKind.Npc && e.Type == 3).ToArray();
        _tables[2].ItemsSource = events.Where(e => e.Kind == PlatinumEventKind.Warp).ToArray();
        _tables[3].ItemsSource = events.Where(e => e.Kind == PlatinumEventKind.Trigger || e.Script != 0).ToArray();
        foreach (var table in _tables) table.SelectedItem = table.Items.Cast<PlatinumEvent>().FirstOrDefault();
        _updating = false; SelectEvent(CurrentTable.SelectedItem as PlatinumEvent);
    }
    private void SelectEvent(PlatinumEvent? item)
    {
        if ((_dirty || _pending is not null) && !ConfirmLeave())
        {
            _updating = true; _tabs.SelectedIndex = _activeTab; CurrentTable.SelectedItem = _event; _updating = false; return;
        }
        _activeTab = Math.Max(0, _tabs.SelectedIndex);
        _event = item; _updating = true; _dirty = false; InvalidatePreview();
        bool editable = item?.Kind == PlatinumEventKind.Npc;
        _x.IsReadOnly = _y.IsReadOnly = _direction.IsReadOnly = !editable; _preview.IsEnabled = editable;
        _x.Text = item?.X.ToString(CultureInfo.InvariantCulture) ?? ""; _y.Text = item?.Y.ToString(CultureInfo.InvariantCulture) ?? ""; _direction.Text = item?.Direction.ToString(CultureInfo.InvariantCulture) ?? "";
        _selection.Text = item is null ? "Selecciona un evento" : $"{item.Description} · índice {item.Index} · ID {item.Id}";
        _script.IsEnabled = item?.Script > 0; _warp.IsEnabled = item is { Kind: PlatinumEventKind.Warp } w && CanNavigate(w);
        _details.Text = _map is null ? "Sin mapas." : $"Mapa {_map.Id}: {_map.Name}\nInterno: {_map.InternalName}\nEvento NARC: {_map.EventFile} · scripts: {_map.ScriptFile} · matriz: {_map.Matrix}\nDimensiones: {_map.Width} × {_map.Height} baldosas. No se validan colisiones ni jugabilidad.\n";
        if (item is not null && _map is not null)
        {
            _details.Text += $"\nTipo {item.Type} · gráfico {item.Graphics} · movimiento {item.Movement}\nFlag {item.Flag} · vista {item.Sight} · rango X/Y {item.RangeX}/{item.RangeY}\nZ bruto {item.Z} (no editable)\n";
            if (item.Kind == PlatinumEventKind.Warp) _details.Text += $"Destino {item.Destination} · ancla {item.Anchor}\n{_session.Data.WarpStatus(item)}\n";
            if (item.Script > 0)
            {
                var link = _session.Data.Script(_map.Id, item.Script);
                _details.Text += $"Script #{link.Reference} → archivo {link.Archive} · entrada {link.Entry?.ToString() ?? "?"} · offset {link.Offset?.ToString() ?? "?"}\n{link.Status}\n{link.Bytes}\n";
            }
            _details.Text += "\nBytes completos (campos desconocidos preservados):\n" + item.Raw;
            if (!editable) _details.Text += "\nSolo consulta en fase 1.";
            if (_session.Data.Maps.Count(m => m.EventFile == _map.EventFile) > 1) _details.Text += "\nArchivo compartido por varias cabeceras: el cambio afecta a todas sus referencias.";
        }
        _updating = false;
    }
    private bool CanNavigate(PlatinumEvent warp) => warp.Destination >= 0 && warp.Destination < _session.Data.Maps.Count &&
        _session.Data.Events(warp.Destination).Any(e => e.Kind == PlatinumEventKind.Warp && e.Index == warp.Anchor);
    internal bool ConfirmLeave() => !_busy && ((!_dirty && _pending is null) || StudioDialog.Confirm(Window.GetWindow(this), "Eventos DS", "Hay campos o una propuesta sin guardar. ¿Descartarlos y continuar?"));
    private void InvalidatePreview() { _pending = null; _save.IsEnabled = false; _differences.Text = ""; }
    internal void PreviewSelection()
    {
        try
        {
            if (_map is null || _event?.Kind != PlatinumEventKind.Npc) return;
            int Parse(TextBox box) => int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : throw new InvalidDataException("Introduce coordenadas y dirección como enteros.");
            ShowProposal(_session.Preview(_map.Id, _event.Index, new(Parse(_x), Parse(_y), Parse(_direction))));
            _status.Text = _pending!.Changes.Count == 0 ? "Sin diferencias." : "Revisa las diferencias y guarda un checkpoint nuevo. La ROM fuente no se modifica.";
        }
        catch (Exception ex) when (Known(ex)) { Fail(ex); }
    }
    private void ShowProposal(NdsEventProposal proposal)
    {
        _pending = proposal; _save.IsEnabled = proposal.Changes.Count > 0;
        _differences.Text = string.Join("\n", proposal.Changes.Select(c => $"{c.Location} · {c.Field}: {c.Before} → {c.After}"));
    }
    internal async Task SaveToAsync(string path)
    {
        if (_busy || _pending is null || _pending.Changes.Count == 0) return;
        _busy = true; _body.IsEnabled = false;
        using var interaction = UiProgress.BlockInteraction();
        try
        {
            var proposal = _pending; await Task.Run(() => _session.Save(proposal, path));
            _dirty = false; InvalidatePreview(); SelectMap(_map is null ? null : _session.Data.Map(_map.Id), confirm: false); RefreshStatus(); Changed?.Invoke();
        }
        catch (Exception ex) when (Known(ex)) { Fail(ex); }
        finally { _busy = false; _body.IsEnabled = true; }
    }
    private void RefreshStatus() { _undo.IsEnabled = _session.CanUndo; _status.Text = _session.LastCheckpoint is null ? "Sin checkpoint guardado." : "Checkpoint verificado: " + _session.LastCheckpoint + " · Exportar ROM para generar la copia jugable."; }
    private void Fail(Exception ex) { _status.Text = "No se guardó: " + ex.Message; InvalidatePreview(); }
    private static bool Known(Exception ex) => ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.Text.Json.JsonException;
}
