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
using RadioButton = System.Windows.Controls.RadioButton;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Point = System.Windows.Point;

namespace ThreeDS.Progressive;

/// <summary>Native DS event editor. Saving creates an immutable checkpoint and updates the pending ROM rebuild.</summary>
internal sealed class NdsEventEditorView : UserControl
{
    private readonly NdsEventSession _session;
    private readonly PlatinumTerrain? _terrain;
    private readonly TextBlock _terrainNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
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
    private readonly NdsMapCanvasControl _canvas = new() { MinHeight = 180 };
    private readonly Grid _center = new();
    private readonly RadioButton _modeMap = new() { Content = "Mapa", IsChecked = true, GroupName = "NdsEventMode", Margin = new Thickness(0, 0, 10, 0) }, _modeTables = new() { Content = "Tablas", GroupName = "NdsEventMode", Margin = new Thickness(0, 0, 14, 0) };
    private readonly WrapPanel _layerBar = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly StackPanel _mapFooter = new();
    private readonly TextBlock _hover = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) }, _scale = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
    private Border? _canvasFrame;
    private readonly RadioButton _toolSelect = new() { Content = "Seleccionar", IsChecked = true, GroupName = "NdsEventTool", Margin = new Thickness(8, 0, 10, 0) }, _toolMove = new() { Content = "Mover NPC", GroupName = "NdsEventTool", Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _toolHelp = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 3) };
    private readonly Border _moveBox = new() { BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _moveText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _moveAccept = new() { Content = "Aceptar propuesta", Margin = new Thickness(0, 6, 8, 0) }, _moveCancel = new() { Content = "Cancelar", Margin = new Thickness(0, 6, 8, 0) };
    private UIElement? _legend;
    private NdsNpcMovePlan? _move;
    private PlatinumMap? _moveMap;
    // ---- structural NPC operations: every one ends in a reviewable proposal; nothing is written by clicking
    private readonly Button _btnAdd = new() { Content = "Añadir" }, _btnDup = new() { Content = "Duplicar" }, _btnCopy = new() { Content = "Copiar" }, _btnCut = new() { Content = "Cortar" }, _btnPaste = new() { Content = "Pegar" }, _btnDelete = new() { Content = "Eliminar" };
    private readonly List<(MenuItem Item, Button Source)> _menuItems = [];
    private readonly Border _newBox = new() { BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBox _nId = new() { Width = 64 }, _nX = new() { Width = 64 }, _nY = new() { Width = 64 }, _nDir = new() { Width = 56 };
    private readonly ComboBox _nGfx = new() { MinWidth = 70 }, _nMov = new() { MinWidth = 70 };
    private readonly TextBlock _newInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _newPreview = new() { Content = "Previsualizar propuesta", Margin = new Thickness(0, 6, 8, 0) }, _newCancel = new() { Content = "Cancelar", Margin = new Thickness(0, 6, 8, 0) };
    private NdsNpcClip? _clip;
    private PlatinumEvent? _newTemplate, _placeTemplate;
    private bool _placeCut, _newLoading;
    private NdsAddPlan? _newPlan;
    private readonly ScrollViewer _inspector;
    private NdsEventProposal? _pending;
    private PlatinumMap? _map;
    private PlatinumEvent? _event;
    private bool _updating, _busy, _dirty;
    private int _activeTab;
    private DataGrid CurrentTable => _tables[Math.Clamp(_tabs.SelectedIndex, 0, 3)];
    internal event Action? Changed;

    internal NdsEventEditorView(NdsEventSession session, PlatinumTerrain? terrain = null, string? terrainUnavailable = null)
    {
        _session = session; _terrain = terrain;
        _terrainNote.Text = terrain is null
            ? "Terreno: no disponible" + (string.IsNullOrWhiteSpace(terrainUnavailable) ? "" : " (" + terrainUnavailable + ")") + ". Se muestran solo eventos y matriz; las casillas no se consideran libres ni bloqueadas."
            : "Terreno: colisión leída de land_data (bit 15 de cada atributo). «Bloqueado» es un dato del juego; «sin colisión» no confirma que se pueda pasar; sin datos = desconocido. Son bloqueos abstractos, no los gráficos de casas o árboles."; Margin = new Thickness(12);
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
        BuildCenter();
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
        inspector.Children.Add(_selection); inspector.Children.Add(BuildStructureBar());
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
    private void BuildCenter()
    {
        _center.RowDefinitions.Add(new() { Height = GridLength.Auto }); _center.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _center.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); _center.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) }; bar.Children.Add(_modeMap); bar.Children.Add(_modeTables);
        foreach (var (label, tip, action) in new (string, string, Action)[] { ("−", "alejar", () => _canvas.ZoomCentered(1 / 1.25)), ("+", "acercar", () => _canvas.ZoomCentered(1.25)),
            ("Ajustar a eventos", "ajustar a eventos", () => _canvas.FitEvents()), ("Ver matriz completa", "ver matriz completa", () => _canvas.FitMatrix()) })
        {
            var button = new Button { Content = label, MinWidth = 36, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 2, 8, 2) };
            AccessibleName(button, "Eventos DS · mapa · " + tip); button.Click += (_, _) => action(); EditorPresentation.StyleAction(button, EditorPresentation.ActionRole.Tertiary); bar.Children.Add(button);
        }
        foreach (var radio in new[] { _modeMap, _modeTables }) radio.SetResourceReference(ForegroundProperty, "TextMain");
        var tools = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        tools.Children.Add(new TextBlock { Text = "Herramienta:", VerticalAlignment = VerticalAlignment.Center }); tools.Children.Add(_toolSelect); tools.Children.Add(_toolMove); bar.Children.Add(tools);
        foreach (var radio in new[] { _toolSelect, _toolMove }) radio.SetResourceReference(ForegroundProperty, "TextMain");
        AccessibleName(_toolSelect, "Eventos DS · herramienta seleccionar"); AccessibleName(_toolMove, "Eventos DS · herramienta mover NPC");
        _toolSelect.Checked += (_, _) => ApplyTool(); _toolMove.Checked += (_, _) => ApplyTool();
        AccessibleName(_modeMap, "Eventos DS · modo mapa esquemático"); AccessibleName(_modeTables, "Eventos DS · modo tablas");
        _center.Children.Add(bar);
        foreach (var (label, layer) in new[] { ("NPC", NdsMapLayers.Npc), ("Objetos", NdsMapLayers.Objects), ("Warps", NdsMapLayers.Warps), ("Activadores", NdsMapLayers.Triggers), ("Bloques", NdsMapLayers.Blocks), ("Tiles", NdsMapLayers.Tiles), ("Máscara", NdsMapLayers.Ownership), ("Terreno", NdsMapLayers.Terrain) })
        {
            var box = new CheckBox { Content = label, IsChecked = true, Margin = new Thickness(0, 0, 10, 0), Tag = layer, FontSize = 12 }; AccessibleName(box, "Eventos DS · capa " + label + (layer == NdsMapLayers.Blocks ? " de 32×32 tiles" : layer == NdsMapLayers.Ownership ? " de cabecera (celdas de la cabecera, contextuales y ajenas)" : layer == NdsMapLayers.Terrain ? " y obstáculos (colisión de terreno)" : "")); if (layer == NdsMapLayers.Terrain && _terrain is null) { box.IsChecked = false; box.IsEnabled = false; } box.ToolTip = (string)AutomationProperties.GetName(box).Substring(13);
            box.Checked += (_, _) => ApplyLayers(); box.Unchecked += (_, _) => ApplyLayers(); _layerBar.Children.Add(box);
        }
        Grid.SetRow(_layerBar, 1); _center.Children.Add(_layerBar);
        _canvasFrame = new Border { BorderThickness = new Thickness(1), Child = _canvas, MinHeight = 180 }; _canvasFrame.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        Grid.SetRow(_canvasFrame, 2); _center.Children.Add(_canvasFrame); Grid.SetRow(_tabs, 2); _center.Children.Add(_tabs); _tabs.Visibility = Visibility.Collapsed;
        var notice = new TextBlock { Text = NdsMapCanvasControl.SchematicNotice, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        AccessibleName(notice, "Eventos DS · aviso de vista esquemática"); AccessibleName(_hover, "Eventos DS · posición bajo el cursor"); AccessibleName(_scale, "Eventos DS · escala y dimensiones");
        BuildMoveBox(); AccessibleName(_toolHelp, "Eventos DS · ayuda de la herramienta");
        _legend = BuildLegend(); ApplyTool(); _canvas.DragGuard = BeginMoveGuard; _canvas.MoveDropped += OnMoveDropped; _canvas.DragChanged += OnDragChanged;
        BuildNewBox(); _canvas.PlacementChosen += OnPlacementChosen; _canvas.PlacementCancelled += () => { if (_move is null) _hover.Text = ""; };
        _mapFooter.Children.Add(_toolHelp); _mapFooter.Children.Add(_moveBox); _mapFooter.Children.Add(_newBox); _mapFooter.Children.Add(notice); _mapFooter.Children.Add(_terrainNote); _mapFooter.Children.Add(_scale); _mapFooter.Children.Add(_hover); _mapFooter.Children.Add(_legend);
        Grid.SetRow(_mapFooter, 3); _center.Children.Add(_mapFooter);
        Grid.SetColumn(_center, 1); _body.Children.Add(_center);
        _modeMap.Checked += (_, _) => ApplyMode(); _modeTables.Checked += (_, _) => ApplyMode();
        _canvas.EventPicked += PickFromCanvas; _canvas.ViewChanged += UpdateScale; _canvas.Hover += text => _hover.Text = text;
    }
    private UIElement BuildLegend()
    {
        var legend = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) }; AccessibleName(legend, "Eventos DS · leyenda de marcadores");
        void Item(UIElement glyph, string text) { var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) }; row.Children.Add(glyph); row.Children.Add(new TextBlock { Text = text, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) }); legend.Children.Add(row); }
        foreach (var (shape, text) in new[] { (NdsMarkerShape.Npc, "NPC"), (NdsMarkerShape.GroundObject, "NPC objeto de suelo"), (NdsMarkerShape.BackgroundObject, "objeto de fondo"), (NdsMarkerShape.Warp, "warp"), (NdsMarkerShape.Trigger, "activador (rango declarado)") })
            Item(new System.Windows.Shapes.Path { Width = 20, Height = 16, Data = NdsMapCanvasControl.ShapeGeometry(shape, new Point(10, 8), 5.5), Fill = new System.Windows.Media.SolidColorBrush(NdsMapCanvasControl.ColorOf(shape)), Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 1 }, text);
        foreach (var (kind, text) in new[] { (NdsBlockOwnership.Explicit, "celda asociada a esta cabecera"), (NdsBlockOwnership.Contextual, "cabecera contextual (sin IDs)"), (NdsBlockOwnership.Other, "celda de otra cabecera") })
            Item(new System.Windows.Shapes.Rectangle { Width = 16, Height = 12, Fill = NdsMapCanvasControl.LegendBrush(kind), Stroke = NdsMapCanvasControl.LegendStroke }, text);
        if (_terrain is not null)
            foreach (var (status, text) in new[] { (PlatinumTerrainStatus.Blocked, "terreno bloqueado (×)"), (PlatinumTerrainStatus.ClearOfCollision, "sin colisión (no confirma paso)"), (PlatinumTerrainStatus.Unknown, "sin datos (damero)") })
                Item(new System.Windows.Shapes.Rectangle { Width = 16, Height = 12, Fill = NdsMapCanvasControl.LegendTerrain(status), Stroke = NdsMapCanvasControl.LegendStroke }, text);
        legend.Children.Add(new TextBlock { Text = "Anillo discontinuo: evento fuera de la máscara de la cabecera · Aspa: fuera de la matriz", FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        return legend;
    }
    // ---- NPC move tool: a drop only creates a proposal; accepting fills X/Y and runs the existing Preview -------------------
    private void BuildMoveBox()
    {
        _moveBox.SetResourceReference(Border.BorderBrushProperty, "Accent"); _moveBox.SetResourceReference(Border.BackgroundProperty, "Panel");
        var stack = new StackPanel(); var buttons = new WrapPanel();
        AccessibleName(_moveBox, "Eventos DS · propuesta de movimiento"); AccessibleName(_moveText, "Eventos DS · detalle de la propuesta"); AccessibleName(_moveAccept, "Eventos DS · aceptar propuesta"); AccessibleName(_moveCancel, "Eventos DS · cancelar propuesta");
        EditorPresentation.StyleAction(_moveAccept, EditorPresentation.ActionRole.Primary); EditorPresentation.StyleAction(_moveCancel, EditorPresentation.ActionRole.Tertiary);
        buttons.Children.Add(_moveAccept); buttons.Children.Add(_moveCancel); stack.Children.Add(_moveText); stack.Children.Add(buttons); _moveBox.Child = stack;
        _moveAccept.Click += (_, _) => AcceptMove(); _moveCancel.Click += (_, _) => HideProposal();
    }
    private void ApplyTool()
    {
        bool move = _toolMove.IsChecked == true; HideProposal(); _canvas.Tool = move ? NdsMapTool.MoveNpc : NdsMapTool.Select;
        _toolHelp.Text = move
            ? "Mover NPC: arrastra un NPC existente a otra casilla; Esc cancela. Para desplazar el mapa, arrastra el fondo o usa el botón central. Soltar solo crea una propuesta: no se guarda nada."
            : "Seleccionar: clic para elegir un evento; arrastra el fondo para desplazar el mapa.";
    }
    /// <summary>Called when a drag actually activates (after the movement threshold): pending fields or a pending preview need the same authorization as changing the selection.</summary>
    private bool BeginMoveGuard(PlatinumEvent npc)
    {
        if (_busy) return false;
        if (_dirty || _pending is not null)
        {
            if (!ConfirmLeave()) return false;
            _dirty = false; InvalidatePreview();
        }
        HideProposal(cancelDrag: false); return true;   // the drag that is activating must survive
    }
    private void OnDragChanged(NdsNpcDragSession? drag) { if (drag is { Active: true }) _hover.Text = drag.Plan.Live; else if (drag is null && _move is null) _hover.Text = ""; }
    private string PositionText(int x, int y) => _canvas.Model is { } m && x >= 0 && y >= 0 && x < m.Layout.TileWidth && y < m.Layout.TileHeight
        ? $"X={x}, Y={y} · bloque ({x / 32},{y / 32}), local ({x % 32},{y % 32})" : $"X={x}, Y={y} · fuera de la matriz";
    private void OnMoveDropped(NdsNpcMovePlan plan)
    {
        if (_map is null) return;
        _move = plan; _moveMap = _map;
        var lines = new List<string> { $"Propuesta de movimiento · NPC {plan.Npc.Index} (ID {plan.Npc.Id}) — Mapa {_map.Id}: {_map.Name}", "Original:  " + PositionText(plan.FromX, plan.FromY), "Propuesto: " + PositionText(plan.ToX, plan.ToY), "Estado: " + plan.Summary };
        lines.AddRange(plan.Reasons.Select(r => "• " + r));
        if (plan.CanPropose) lines.Add("No evaluado: alturas y BDHC, comportamiento del tile, puertas, scripts, movimiento del NPC y estado dinámico. Esto no es una confirmación de que el movimiento sea seguro en el juego.");
        var sharing = _session.Data.Maps.Where(m => m.EventFile == _map.EventFile).Select(m => m.Id).ToList();
        if (sharing.Count > 1) lines.Add($"Archivo de eventos compartido por {sharing.Count} cabeceras ({string.Join(", ", sharing.Take(8))}{(sharing.Count > 8 ? "…" : "")}): el cambio afecta a todas y se aplican los límites más conservadores.");
        lines.Add(plan.CanPropose ? "Aceptar NO guarda nada: pasa X/Y al editor y genera la previsualización; después guarda un checkpoint nuevo." : "Este destino no se puede aceptar; no se modificó nada.");
        _moveText.Text = string.Join("\n", lines);
        _moveAccept.Visibility = plan.CanPropose ? Visibility.Visible : Visibility.Collapsed; _moveAccept.Content = plan.Outcome == NdsMoveOutcome.NeedsWarning ? "Aceptar propuesta con advertencia" : "Aceptar propuesta";
        _moveCancel.Content = plan.CanPropose ? "Cancelar" : "Cerrar";
        _moveBox.Visibility = Visibility.Visible; _hover.Text = plan.Live; SetFooterCompact(true);
    }
    private void SetFooterCompact(bool compact)
    {
        var visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _terrainNote.Visibility = visibility; _toolHelp.Visibility = visibility; if (_legend is not null) _legend.Visibility = visibility;
        if (_inspector is not null) Adapt();
    }
    private void HideProposal(bool cancelDrag = true)
    {
        _move = null; _moveMap = null; if (_moveBox.Visibility != Visibility.Collapsed) _moveBox.Visibility = Visibility.Collapsed;
        _newBox.Visibility = Visibility.Collapsed; _newTemplate = null; _newPlan = null;
        SetFooterCompact(false); if (cancelDrag) { _canvas.CancelDrag(); _canvas.CancelPlacement(); }
    }
    private void AcceptMove()
    {
        var plan = _move; var map = _moveMap; if (plan is null || map is null || !plan.CanPropose || !ReferenceEquals(map, _map) && map.Id != _map?.Id) { HideProposal(); return; }
        HideProposal();
        var npc = _tables[0].Items.Cast<PlatinumEvent>().FirstOrDefault(e => e.Kind == plan.Npc.Kind && e.Index == plan.Npc.Index);
        if (npc is null) return;
        _updating = true; _tabs.SelectedIndex = 0; _tables[0].SelectedItem = npc; _updating = false;
        SelectEvent(npc);
        if (!ReferenceEquals(_event, npc)) return;      // the user declined to leave pending fields: keep everything as it was
        _x.Text = plan.ToX.ToString(CultureInfo.InvariantCulture); _y.Text = plan.ToY.ToString(CultureInfo.InvariantCulture);
        PreviewSelection();
        if (_pending is { Changes.Count: > 0 }) { _canvas.SetPendingMove(npc, plan.ToX, plan.ToY); _status.Text = "Propuesta aceptada: revisa las diferencias y guarda un checkpoint nuevo. Aún no se ha guardado nada."; }
    }

    // ---- structural NPC operations --------------------------------------------------------------------------------------
    private UIElement BuildStructureBar()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; AccessibleName(bar, "Eventos DS · operaciones de NPC");
        var menu = new ContextMenu();
        foreach (var (button, tip, action) in new (Button, string, Action)[] {
            (_btnAdd, "añadir NPC", DoAdd), (_btnDup, "duplicar NPC", DoDuplicate), (_btnCopy, "copiar NPC", DoCopy), (_btnCut, "cortar NPC", DoCut), (_btnPaste, "pegar NPC", DoPaste), (_btnDelete, "eliminar NPC", DoDelete) })
        {
            button.Margin = new Thickness(0, 0, 6, 6); button.Padding = new Thickness(8, 2, 8, 2); AccessibleName(button, "Eventos DS · " + tip); EditorPresentation.StyleAction(button, EditorPresentation.ActionRole.Tertiary);
            button.Click += (_, _) => action(); bar.Children.Add(button);
            var item = new MenuItem { Header = button.Content }; AutomationProperties.SetName(item, "Eventos DS · menú · " + tip); item.Click += (_, _) => action(); menu.Items.Add(item); _menuItems.Add((item, button));
        }
        _btnAdd.ToolTip = "Crea un NPC nuevo a partir del NPC seleccionado. No se guarda nada hasta confirmar."; _btnDup.ToolTip = "Duplica el NPC seleccionado: elige la casilla en el mapa (Ctrl+D)."; _btnCopy.ToolTip = "Copia el NPC seleccionado (Ctrl+C).";
        _btnCut.ToolTip = "Marca el NPC para trasladarlo; no se elimina nada (Ctrl+X)."; _btnPaste.ToolTip = "Pega el NPC copiado o traslada el cortado (Ctrl+V)."; _btnDelete.ToolTip = "Eliminar NPC: muestra por qué no se permite (Supr).";
        _canvas.ContextMenu = menu; _canvas.ContextMenuOpening += (_, _) => UpdateStructureButtons();
        _canvas.PreviewKeyDown += (_, e) =>
        {
            bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0; Action? run = null;
            if (ctrl && e.Key == System.Windows.Input.Key.C) run = DoCopy; else if (ctrl && e.Key == System.Windows.Input.Key.X) run = DoCut; else if (ctrl && e.Key == System.Windows.Input.Key.V) run = DoPaste;
            else if (ctrl && e.Key == System.Windows.Input.Key.D) run = DoDuplicate; else if (e.Key == System.Windows.Input.Key.Delete) run = DoDelete;
            if (run is null) return; e.Handled = true; run();
        };
        UpdateStructureButtons(); return bar;
    }
    private PlatinumEvent? SelectedTemplate => _event is { } e && NdsNpcStructurePlanner.IsTemplate(e) ? e : null;
    private void UpdateStructureButtons()
    {
        bool ok = !_busy && _map is not null && _canvas.Model is not null; var t = SelectedTemplate;
        _btnAdd.IsEnabled = _btnDup.IsEnabled = _btnCopy.IsEnabled = _btnCut.IsEnabled = ok && t is not null;
        _btnPaste.IsEnabled = ok && _clip is not null; _btnDelete.IsEnabled = ok && _event?.Kind == PlatinumEventKind.Npc;
        _btnPaste.ToolTip = _clip is null ? "No hay nada copiado." : (_clip.Cut ? "Trasladar" : "Pegar") + " · " + _clip.Origin;
        foreach (var (item, source) in _menuItems) item.IsEnabled = source.IsEnabled;
    }
    /// <summary>Starting a structural operation needs the same authorization as leaving pending fields or a pending preview.</summary>
    private bool StructureGuard()
    {
        if (_busy || _map is null) return false;
        if (_dirty || _pending is not null)
        {
            if (!ConfirmLeave()) return false;
            _dirty = false; InvalidatePreview();
        }
        HideProposal(); return true;
    }
    private void ShowInfo(string text)
    {
        _move = null; _moveText.Text = text; _moveAccept.Visibility = Visibility.Collapsed; _moveCancel.Content = "Cerrar"; _moveBox.Visibility = Visibility.Visible; SetFooterCompact(true);
    }
    private void DoCopy()
    {
        if (SelectedTemplate is not { } t || _map is null) return;
        _clip = new NdsNpcClip(t, false, _map.Id, _map.EventFile); _status.Text = $"Copiado: {_clip.Origin}. Nada se ha modificado."; UpdateStructureButtons();
    }
    private void DoCut()
    {
        if (SelectedTemplate is not { } t || _map is null) return;
        _clip = new NdsNpcClip(t, true, _map.Id, _map.EventFile); _status.Text = $"Marcado para trasladar: {_clip.Origin}. No se ha eliminado nada; el traslado solo se aplica al pegar y tras confirmar."; UpdateStructureButtons();
    }
    private void DoDuplicate() { if (SelectedTemplate is { } t && StructureGuard()) StartPlacement(t, false); }
    private void DoPaste()
    {
        if (_clip is not { } clip || _map is null || !StructureGuard()) return;
        var problems = NdsNpcStructurePlanner.PasteProblems(_session.Data, clip, _map.Id);
        if (problems.Count > 0) { ShowInfo("No se puede pegar este NPC aquí:\n" + string.Join("\n", problems.Select(p => "• " + p)) + "\nNo se modificó nada ni se perdió el NPC original."); return; }
        StartPlacement(clip.Source, clip.Cut);
    }
    private void DoAdd()
    {
        if (SelectedTemplate is not { } t || _map is null || !StructureGuard()) return;
        OpenNewForm(t, NdsNpcStructurePlanner.DraftFrom(_session.Data, _map.Id, t, t.X + 1, t.Y));
    }
    private void DoDelete()
    {
        if (_event is not { Kind: PlatinumEventKind.Npc } npc || _map is null || _busy) return;
        HideProposal();
        bool lastAdded = _session.LastAddition is { } add && add.Map == _map.Id && add.Index == npc.Index;
        var report = NdsNpcStructurePlanner.AssessDelete(_session.Data, _map.Id, npc, lastAdded);
        ShowInfo($"Eliminar NPC {npc.Index} (ID {npc.Id}) — Mapa {_map.Id}: NO permitido.\n" + string.Join("\n", report.Reasons.Select(r => "• " + r)) + "\nNo se modificó nada.");
    }
    private void StartPlacement(PlatinumEvent template, bool cut)
    {
        if (_canvas.Model is not { } model || _map is null) return;
        int mapId = _map.Id; _placeTemplate = template; _placeCut = cut;
        _canvas.BeginPlacement(template, (x, y) =>
        {
            if (cut) { var move = NdsNpcMovePlanner.Plan(model, template, x, y); return new(move.Outcome, move.Live); }
            var plan = NdsNpcStructurePlanner.PlanAdd(_session.Data, model, mapId, template, NdsNpcStructurePlanner.DraftFrom(_session.Data, mapId, template, x, y));
            return new(plan.Outcome, $"Nuevo NPC en X {x}, Y {y} · {plan.Summary}" + (plan.Reasons.Count > 0 ? " · " + plan.Reasons[0] : ""));
        });
        _hover.Text = cut ? "Elige la casilla destino del traslado; Esc cancela. No se ha eliminado nada." : "Elige la casilla del NPC nuevo; Esc cancela. No se guarda nada.";
        _canvas.Focus();
    }
    private void OnPlacementChosen(int x, int y)
    {
        if (_placeTemplate is not { } template || _map is null || _canvas.Model is not { } model) return;
        if (_placeCut) { OnMoveDropped(NdsNpcMovePlanner.Plan(model, template, x, y)); return; }
        OpenNewForm(template, NdsNpcStructurePlanner.DraftFrom(_session.Data, _map.Id, template, x, y));
    }
    private void BuildNewBox()
    {
        _newBox.SetResourceReference(Border.BorderBrushProperty, "Accent"); _newBox.SetResourceReference(Border.BackgroundProperty, "Panel");
        var stack = new StackPanel(); var title = new TextBlock { Text = "NPC nuevo — se añade al final del bloque de NPC del archivo de eventos", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var fields = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (label, control, name) in new (string, FrameworkElement, string)[] { ("ID", _nId, "ID"), ("Gráfico", _nGfx, "gráfico"), ("Movimiento", _nMov, "movimiento"), ("Dirección", _nDir, "dirección"), ("X", _nX, "X"), ("Y", _nY, "Y") })
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; cell.Children.Add(new TextBlock { Text = label, FontSize = 11.5 }); cell.Children.Add(control); fields.Children.Add(cell);
            AccessibleName(control, "Eventos DS · nuevo NPC · " + name);
        }
        var buttons = new WrapPanel(); buttons.Children.Add(_newPreview); buttons.Children.Add(_newCancel);
        AccessibleName(_newBox, "Eventos DS · NPC nuevo"); AccessibleName(_newInfo, "Eventos DS · validación del NPC nuevo"); AccessibleName(_newPreview, "Eventos DS · previsualizar propuesta de NPC nuevo"); AccessibleName(_newCancel, "Eventos DS · cancelar NPC nuevo");
        EditorPresentation.StyleAction(_newPreview, EditorPresentation.ActionRole.Primary); EditorPresentation.StyleAction(_newCancel, EditorPresentation.ActionRole.Tertiary);
        foreach (var box in new[] { _nId, _nX, _nY, _nDir }) box.TextChanged += (_, _) => RevalidateNew();
        foreach (var combo in new[] { _nGfx, _nMov }) combo.SelectionChanged += (_, _) => RevalidateNew();
        _newCancel.Click += (_, _) => HideProposal();
        _newPreview.Click += (_, _) => PreviewNewNpc();
        stack.Children.Add(title); stack.Children.Add(fields); stack.Children.Add(_newInfo); stack.Children.Add(buttons); _newBox.Child = stack;
    }
    private void OpenNewForm(PlatinumEvent template, NdsAddDraft draft)
    {
        _newTemplate = template; _newLoading = true;
        _nGfx.ItemsSource = _session.Data.AccreditedGraphics.ToArray(); _nMov.ItemsSource = _session.Data.AccreditedMovements.ToArray();
        _nId.Text = draft.Id.ToString(CultureInfo.InvariantCulture); _nX.Text = draft.X.ToString(CultureInfo.InvariantCulture); _nY.Text = draft.Y.ToString(CultureInfo.InvariantCulture); _nDir.Text = draft.Direction.ToString(CultureInfo.InvariantCulture);
        _nGfx.SelectedItem = draft.Graphics; _nMov.SelectedItem = draft.Movement; _newLoading = false;
        _moveBox.Visibility = Visibility.Collapsed; _newBox.Visibility = Visibility.Visible; SetFooterCompact(true); RevalidateNew();
    }
    private void RevalidateNew()
    {
        if (_newLoading || _newTemplate is not { } template || _map is null || _canvas.Model is not { } model) return;
        static bool Int(TextBox box, out int value) => int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        if (!Int(_nId, out int id) || !Int(_nX, out int x) || !Int(_nY, out int y) || !Int(_nDir, out int dir) || _nGfx.SelectedItem is not int gfx || _nMov.SelectedItem is not int mov)
        {
            _newPlan = null; _newPreview.IsEnabled = false; _newInfo.Text = "Introduce enteros en ID, dirección, X e Y y elige un gráfico y un movimiento acreditados."; return;
        }
        var plan = NdsNpcStructurePlanner.PlanAdd(_session.Data, model, _map.Id, template, new(id, gfx, mov, dir, x, y)); _newPlan = plan;
        var lines = new List<string> { "Estado: " + plan.Summary };
        lines.AddRange(plan.Reasons.Select(r => "• " + r));
        lines.Add($"Cantidad de NPC en el mapa: {_session.Data.NpcCount(_map.Id)} → {_session.Data.NpcCount(_map.Id) + 1} · script 0, flag 0, tipo 0 (no se copian scripts ni flags) · altura y bytes desconocidos copiados de la plantilla.");
        if (plan.CanPropose) lines.Add("No evaluado: alturas y BDHC, comportamiento del tile, puertas, scripts, movimiento del NPC y estado dinámico. Previsualizar no guarda nada.");
        var sharing = _session.Data.Maps.Where(m => m.EventFile == _map.EventFile).Select(m => m.Id).ToList();
        if (sharing.Count > 1) lines.Add($"Archivo de eventos compartido por {sharing.Count} cabeceras ({string.Join(", ", sharing.Take(8))}): el NPC nuevo aparecerá en todas.");
        _newInfo.Text = string.Join("\n", lines); _newPreview.IsEnabled = plan.CanPropose;
        _newPreview.Content = plan.Outcome == NdsMoveOutcome.NeedsWarning ? "Previsualizar propuesta con advertencia" : "Previsualizar propuesta";
    }
    private void PreviewNewNpc()
    {
        if (_newPlan is not { CanPropose: true } plan || _map is null) return;
        try
        {
            var proposal = _session.PreviewAdd(_map.Id, plan.Record!);
            HideProposal(); ShowProposal(proposal); _status.Text = "Propuesta de NPC nuevo preparada: revisa las diferencias y guarda un checkpoint nuevo. Aún no se ha guardado nada.";
        }
        catch (Exception ex) when (Known(ex)) { Fail(ex); }
    }

    private void ApplyMode()
    {
        bool map = _modeMap.IsChecked == true; if (!map) HideProposal();
        if (_canvasFrame is not null) _canvasFrame.Visibility = map ? Visibility.Visible : Visibility.Collapsed;
        _tabs.Visibility = map ? Visibility.Collapsed : Visibility.Visible; _layerBar.Visibility = _mapFooter.Visibility = map ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ApplyLayers()
    {
        var layers = NdsMapLayers.None;
        foreach (var box in _layerBar.Children.OfType<CheckBox>()) if (box.IsChecked == true) layers |= (NdsMapLayers)box.Tag;
        _canvas.SetLayers(layers);
    }
    private void LoadCanvas(PlatinumMap? map, IReadOnlyList<PlatinumEvent> events)
    {
        if (map is null) { _canvas.Clear(); _scale.Text = ""; return; }
        try { _canvas.SetMap(_session.Data.Layout(map.Id), events, _terrain, _session.Data.Bounds(map.Id)); }
        catch (InvalidDataException ex) { _canvas.Clear(); _scale.Text = "No se pudo leer la matriz de este mapa: " + ex.Message; }
    }
    private void UpdateScale()
    {
        if (_canvas.Model is not { } m) return;
        _scale.Text = $"Escala: 1 tile = {m.Zoom:0.##} px · bloque = 32×32 tiles · matriz {m.Layout.Width}×{m.Layout.Height} bloques ({m.Layout.TileWidth}×{m.Layout.TileHeight} tiles) · celdas asociadas explícitamente a la cabecera {m.Layout.MapId}: {(m.Layout.HasStoredHeaders ? m.OwnedBlockCount.ToString(CultureInfo.InvariantCulture) : "sin cabeceras almacenadas")} · eventos fuera de la máscara: {m.OutsideMaskCount}"; _scale.ToolTip = _scale.Text;
    }
    private void PickFromCanvas(PlatinumEvent? item)
    {
        if (item is null || _busy) return;
        if (_dirty || _pending is not null)
        {
            if (!ConfirmLeave()) { _canvas.SelectProgrammatic(_event, false); return; }
            _dirty = false; InvalidatePreview();
        }
        bool InTable(int t) => _tables[t].Items.Cast<PlatinumEvent>().Any(e => e.Kind == item.Kind && e.Index == item.Index);
        int tab = _tabs.SelectedIndex >= 0 && InTable(_tabs.SelectedIndex) ? _tabs.SelectedIndex : item.Kind switch { PlatinumEventKind.Npc => 0, PlatinumEventKind.Object => 1, PlatinumEventKind.Warp => 2, _ => 3 };
        _updating = true; _tabs.SelectedIndex = tab;
        _tables[tab].SelectedItem = _tables[tab].Items.Cast<PlatinumEvent>().FirstOrDefault(e => e.Kind == item.Kind && e.Index == item.Index); _updating = false;
        SelectEvent(item);
    }
    private static void AccessibleName(DependencyObject control, string name) => AutomationProperties.SetName(control, name);
    private void Adapt()
    {
        bool compact = ActualWidth < 850;
        _body.ColumnDefinitions[0].Width = new GridLength(compact ? 165 : 205);
        _body.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(280);
        Grid.SetRow(_inspector, compact ? 1 : 0); Grid.SetColumn(_inspector, compact ? 0 : 2); Grid.SetColumnSpan(_inspector, compact ? 2 : 1);
        _inspector.Visibility = compact && (_moveBox.Visibility == Visibility.Visible || _newBox.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;   // compact layout: give the canvas the room while a proposal is open
        _inspector.MaxHeight = compact ? 170 : double.PositiveInfinity;
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
        if (_move is not null || _canvas.Drag is not null || _canvas.Placing || _newBox.Visibility == Visibility.Visible) HideProposal();
        if (confirm && !ConfirmLeave()) { _updating = true; _maps.SelectedItem = _map; _updating = false; return; }
        _dirty = false; InvalidatePreview(); _map = map; _updating = true;
        var events = map is null ? Array.Empty<PlatinumEvent>() : _session.Data.Events(map.Id).ToArray();
        _tables[0].ItemsSource = events.Where(e => e.Kind == PlatinumEventKind.Npc).ToArray();
        _tables[1].ItemsSource = events.Where(e => e.Kind == PlatinumEventKind.Object || e.Kind == PlatinumEventKind.Npc && e.Type == 3).ToArray();
        _tables[2].ItemsSource = events.Where(e => e.Kind == PlatinumEventKind.Warp).ToArray();
        _tables[3].ItemsSource = events.Where(e => e.Kind == PlatinumEventKind.Trigger || e.Script != 0).ToArray();
        foreach (var table in _tables) table.SelectedItem = table.Items.Cast<PlatinumEvent>().FirstOrDefault();
        LoadCanvas(map, events);
        _updating = false; SelectEvent(CurrentTable.SelectedItem as PlatinumEvent);
    }
    private void SelectEvent(PlatinumEvent? item)
    {
        if ((_dirty || _pending is not null) && !ConfirmLeave())
        {
            _updating = true; _tabs.SelectedIndex = _activeTab; CurrentTable.SelectedItem = _event; _updating = false; _canvas.SelectProgrammatic(_event, false); return;
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
        if (item is not null && _canvas.Model?.Find(item.Kind, item.Index) is { } marker)
        {
            _details.Text += $"\nPosición en el mapa esquemático: {_canvas.Model.Describe(item.X, item.Y)}\n{NdsMapCanvasModel.Describe(marker.Placement)}.";
            _details.Text += "\n\nConsulta espacial (solo lectura, no es una validación de juego):\n" + _canvas.Model.Query.Assess(item.X, item.Y, (item.Kind, item.Index)).ToText();
        }
        _canvas.SelectProgrammatic(item, true);
        UpdateStructureButtons();
        _updating = false;
    }
    private bool CanNavigate(PlatinumEvent warp) => warp.Destination >= 0 && warp.Destination < _session.Data.Maps.Count &&
        _session.Data.Events(warp.Destination).Any(e => e.Kind == PlatinumEventKind.Warp && e.Index == warp.Anchor);
    internal bool ConfirmLeave() => !_busy && ((!_dirty && _pending is null) || StudioDialog.Confirm(Window.GetWindow(this), "Eventos DS", "Hay campos o una propuesta sin guardar. ¿Descartarlos y continuar?"));
    private void InvalidatePreview() { _pending = null; _save.IsEnabled = false; _differences.Text = ""; _canvas.ClearPendingMove(); }
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
        _differences.Text = proposal.Lines is { Count: > 0 } lines ? string.Join("\n", lines) : string.Join("\n", proposal.Changes.Select(c => $"{c.Location} · {c.Field}: {c.Before} → {c.After}"));
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
