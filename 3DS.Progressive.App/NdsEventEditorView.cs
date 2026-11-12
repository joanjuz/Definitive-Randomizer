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
    // Read-only map props (structures). Never edited here; null when the ROM is not the accredited CPUE/1 land_data.
    private readonly PlatinumLandProps? _props; private readonly string? _propsNote;
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
    private readonly Button _btnAdd = new() { Content = "Añadir" }, _btnDup = new() { Content = "Duplicar" }, _btnCopy = new() { Content = "Copiar" }, _btnCut = new() { Content = "Cortar" }, _btnPaste = new() { Content = "Pegar" }, _btnDelete = new() { Content = "Eliminar" }, _btnNurse = new() { Content = "Añadir enfermera" };
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

    internal NdsEventEditorView(NdsEventSession session, PlatinumTerrain? terrain = null, string? terrainUnavailable = null, PlatinumLandProps? props = null, string? propsUnavailable = null)
    {
        _session = session; _terrain = terrain; _props = props; _propsNote = propsUnavailable;
        _terrainNote.Text = terrain is null
            ? "Terreno: no disponible" + (string.IsNullOrWhiteSpace(terrainUnavailable) ? "" : " (" + terrainUnavailable + ")") + ". Se muestran solo eventos y matriz; las casillas no se consideran libres ni bloqueadas."
            : "Terreno: colisión leída de land_data (bit 15 de cada atributo). «Bloqueado» es un dato del juego; «sin colisión» no confirma que se pueda pasar; sin datos = desconocido. Son bloqueos abstractos, no los gráficos de casas o árboles."; Margin = new Thickness(12);
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "TextMain");
        var root = new DockPanel(); Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(_title); header.Children.Add(_intro);
        header.Children.Add(_saveHelp);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var actions = new WrapPanel(); footer.Children.Add(actions);
        foreach (var button in new[] { _preview, _save, _undo }) { button.Margin = new Thickness(0, 6, 8, 6); actions.Children.Add(button); }
        EditorPresentation.StyleAction(_save, EditorPresentation.ActionRole.Primary);
        EditorPresentation.StyleAction(_preview, EditorPresentation.ActionRole.Secondary);
        EditorPresentation.StyleAction(_undo, EditorPresentation.ActionRole.Tertiary);
        footer.Children.Add(_differences); footer.Children.Add(_status);
        // The working area scrolls as a whole when the window is too short for it, so the map keeps a usable size instead of being pushed out of view; the
        // header and the footer (with the save actions) stay outside the scrolled area.
        _bodyScroll.Content = _body; root.Children.Add(_bodyScroll); _bodyScroll.SizeChanged += (_, _) => Adapt();
        AccessibleName(_bodyScroll, "Eventos DS · área de trabajo (desplazable en ventanas pequeñas)");
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
        inspector.Children.Add(_selection); inspector.Children.Add(_newBox);
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
            // With a persistent project the change is saved in it (with the rest of the work on the ROM); no checkpoint file is asked for.
            if (ProjectSave is not null) { await SaveToProjectAsync(); return; }
            var picker = new Microsoft.Win32.SaveFileDialog { Filter = "Checkpoint DS (*.ndsevents.json)|*.ndsevents.json", DefaultExt = ".ndsevents.json", AddExtension = true, FileName = Path.GetFileNameWithoutExtension(_session.SourcePath) + "-eventos-" + (_session.Revision + 1) + ".ndsevents.json", Title = "Guardar checkpoint nuevo; no sobrescribe versiones anteriores" };
            if (picker.ShowDialog(Window.GetWindow(this)) == true) await SaveToAsync(picker.FileName);
        };
        _undo.Click += (_, _) => { try { ShowProposal(_session.PreviewUndo()); _status.Text = ProjectSave is null ? "Restauración preparada. Guarda en un checkpoint nuevo para aplicarla." : "Restauración preparada. Guarda para aplicarla en el proyecto."; } catch (Exception ex) when (Known(ex)) { Fail(ex); } };
        _warp.Click += (_, _) => { if (_event is { Kind: PlatinumEventKind.Warp } warp && CanNavigate(warp)) { _search.Text = ""; _maps.SelectedItem = _session.Data.Map(warp.Destination); _tabs.SelectedIndex = 2; _tables[2].SelectedItem = _tables[2].Items.Cast<PlatinumEvent>().First(e => e.Index == warp.Anchor); } };
        _script.Click += (_, _) => { var selected = _event; if (selected is null) return; _tabs.SelectedIndex = 3; _tables[3].SelectedItem = _tables[3].Items.Cast<PlatinumEvent>().FirstOrDefault(e => e.Kind == selected.Kind && e.Index == selected.Index); _details.Focus(); };
        SizeChanged += (_, _) => Adapt(); _differences.TextChanged += (_, _) => { if (_brief) Adapt(); }; FilterMaps(); RefreshStatus();
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
        // NPC operations live in the toolbar above the map: always visible, never scrolled away with the inspector.
        bar.Children.Add(BuildStructureBar());
        _center.Children.Add(bar);
        foreach (var (label, layer) in new[] { ("NPC", NdsMapLayers.Npc), ("Objetos", NdsMapLayers.Objects), ("Warps", NdsMapLayers.Warps), ("Activadores", NdsMapLayers.Triggers), ("Bloques", NdsMapLayers.Blocks), ("Tiles", NdsMapLayers.Tiles), ("Máscara", NdsMapLayers.Ownership), ("Terreno", NdsMapLayers.Terrain), ("Estructuras", NdsMapLayers.Structures), ("Modelos/texturas", NdsMapLayers.None) })
        {
            var box = new CheckBox { Content = label, IsChecked = true, Margin = new Thickness(0, 0, 10, 0), Tag = layer, FontSize = 12 }; AccessibleName(box, "Eventos DS · capa " + label + (layer == NdsMapLayers.Blocks ? " de 32×32 tiles" : layer == NdsMapLayers.Ownership ? " de cabecera (celdas de la cabecera, contextuales y ajenas)" : layer == NdsMapLayers.Terrain ? " y obstáculos (colisión de terreno)" : "")); if (layer == NdsMapLayers.Terrain && _terrain is null) { box.IsChecked = false; box.IsEnabled = false; } box.ToolTip = AutomationProperties.GetName(box).Substring("Eventos DS · ".Length);
            if (layer == NdsMapLayers.Structures)
            {
                box.ToolTip = _props is null
                    ? "Estructuras: no disponibles" + (string.IsNullOrWhiteSpace(_propsNote) ? "" : " (" + _propsNote + ")") + "."
                    : "Posición e ID de modelo de los map props de land_data (solo lectura). No dibuja la forma del edificio.";
                if (_props is null) { box.IsChecked = false; box.IsEnabled = false; }
                AutomationProperties.SetHelpText(box, (string)box.ToolTip);
            }
            if (layer == NdsMapLayers.None) { box.IsChecked = false; box.IsEnabled = false; box.ToolTip = "No disponible: exige leer los modelos NSBMD y las texturas NSBTX, que no están implementados."; AutomationProperties.SetHelpText(box, (string)box.ToolTip); }
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
        _mapFooter.Children.Add(_toolHelp); _mapFooter.Children.Add(_moveBox); _mapFooter.Children.Add(notice); _mapFooter.Children.Add(_terrainNote); _mapFooter.Children.Add(_scale); _mapFooter.Children.Add(_hover); _mapFooter.Children.Add(_legend);
        Grid.SetRow(_mapFooter, 3); _center.Children.Add(_mapFooter);
        Grid.SetColumn(_center, 1); _body.Children.Add(_center);
        _modeMap.Checked += (_, _) => ApplyMode(); _modeTables.Checked += (_, _) => ApplyMode();
        _canvas.PropPicked += ShowProp; _canvas.EventPicked += PickFromCanvas; _canvas.ViewChanged += UpdateScale; _canvas.Hover += text => _hover.Text = text;
    }
    private UIElement BuildLegend()
    {
        var legend = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) }; AccessibleName(legend, "Eventos DS · leyenda de marcadores");
        void Item(UIElement glyph, string text) { var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) }; row.Children.Add(glyph); row.Children.Add(new TextBlock { Text = text, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) }); legend.Children.Add(row); }
        foreach (var (shape, text) in new[] { (NdsMarkerShape.Npc, "NPC"), (NdsMarkerShape.GroundObject, "NPC objeto de suelo"), (NdsMarkerShape.BackgroundObject, "objeto de fondo"), (NdsMarkerShape.Warp, "warp"), (NdsMarkerShape.Trigger, "activador (rango declarado)") })
            Item(new System.Windows.Shapes.Path { Width = 20, Height = 16, Data = NdsMapCanvasControl.ShapeGeometry(shape, new Point(10, 8), 5.5), Fill = new System.Windows.Media.SolidColorBrush(NdsMapCanvasControl.ColorOf(shape)), Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 1 }, text);
        foreach (var (kind, text) in new[] { (NdsBlockOwnership.Explicit, "celda asociada a esta cabecera"), (NdsBlockOwnership.Contextual, "cabecera contextual (sin IDs)"), (NdsBlockOwnership.Other, "celda de otra cabecera") })
            Item(new System.Windows.Shapes.Rectangle { Width = 16, Height = 12, Fill = NdsMapCanvasControl.LegendBrush(kind), Stroke = NdsMapCanvasControl.LegendStroke }, text);
        if (_props is not null) Item(new System.Windows.Shapes.Path { Width = 20, Height = 16, Data = System.Windows.Media.Geometry.Parse("M3,14 L3,7 L10,1 L17,7 L17,14 Z"), Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0x8B, 0x5E)), Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 1 }, "estructura (map prop: posición e ID de modelo, sin forma; solo lectura)");
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
        _proposalOpen = compact; ApplyFooterDetail();
        if (_inspector is not null) Adapt();
    }
    private void HideProposal(bool cancelDrag = true)
    {
        _move = null; _moveMap = null; if (_moveBox.Visibility != Visibility.Collapsed) _moveBox.Visibility = Visibility.Collapsed;
        _newBox.Visibility = Visibility.Collapsed; _newTemplate = null; _newPlan = null; _newClip = null; _newDraft = null; _newBinding = null;
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
    // Flow (every path ends in the existing proposal → «Guardar cambios…» → checkpoint; nothing is written by clicking):
    //   Duplicar / Pegar / Añadir → choose the tile on the map (ghost) → a ready tile previews at once, a warning or a copy from another map opens
    //   the form first → the pending NPC is drawn on the map until it is saved or discarded.
    private sealed record TemplateItem(PlatinumEvent Event, string Label) { public override string ToString() => Label; }
    private readonly ComboBox _nTemplate = new() { MinWidth = 200 };
    private (int X, int Y)? _contextTile;
    private NdsNpcClip? _placeClip;

    private UIElement BuildStructureBar()
    {
        var bar = new WrapPanel { Margin = new Thickness(6, 0, 0, 0) }; AccessibleName(bar, "Eventos DS · operaciones de NPC"); bar.Children.Add(new TextBlock { Text = "NPC:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) });
        var menu = new ContextMenu();
        foreach (var (button, tip, action) in new (Button, string, Action)[] {
            (_btnAdd, "añadir NPC", () => DoAdd(null)), (_btnNurse, "añadir enfermera", () => DoAddNurse(null)), (_btnDup, "duplicar NPC", () => DoDuplicate(null)), (_btnCopy, "copiar NPC", DoCopy), (_btnCut, "cortar NPC", DoCut), (_btnPaste, "pegar NPC", () => DoPaste(null)), (_btnDelete, "eliminar NPC", DoDelete) })
        {
            button.Margin = new Thickness(0, 0, 6, 6); button.Padding = new Thickness(8, 2, 8, 2); AccessibleName(button, "Eventos DS · " + tip); EditorPresentation.StyleAction(button, EditorPresentation.ActionRole.Tertiary);
            button.Click += (_, _) => action(); bar.Children.Add(button);
            var item = new MenuItem { Header = button.Content }; AutomationProperties.SetName(item, "Eventos DS · menú · " + tip); item.Click += (_, _) => action(); menu.Items.Add(item); _menuItems.Add((item, button));
        }
        // Context-menu actions at the right-clicked tile: no extra placement step.
        menu.Items.Add(new Separator());
        foreach (var (header, tip, action) in new (string, string, Action)[] { ("Pegar aquí", "pegar NPC aquí", () => DoPaste(_contextTile)), ("Duplicar aquí", "duplicar NPC aquí", () => DoDuplicate(_contextTile)), ("Añadir NPC aquí", "añadir NPC aquí", () => DoAdd(_contextTile)), ("Añadir enfermera aquí", "añadir enfermera aquí", () => DoAddNurse(_contextTile)) })
        {
            var item = new MenuItem { Header = header }; AutomationProperties.SetName(item, "Eventos DS · menú · " + tip); item.Click += (_, _) => action(); menu.Items.Add(item);
            _menuItems.Add((item, tip.StartsWith("pegar") ? _btnPaste : tip.StartsWith("duplicar") ? _btnDup : tip.Contains("enfermera") ? _btnNurse : _btnAdd));
        }
        _btnAdd.ToolTip = "Crea un NPC nuevo: elige la casilla en el mapa y su apariencia. No se guarda nada hasta confirmar."; _btnDup.ToolTip = "Duplica el NPC seleccionado: elige la casilla en el mapa (Ctrl+D).";
        _btnCopy.ToolTip = "Copia el NPC seleccionado (Ctrl+C). Se puede pegar en este mapa o en otro."; _btnCut.ToolTip = "Marca el NPC para trasladarlo dentro de su archivo de eventos; no se elimina nada (Ctrl+X).";
        _btnNurse.ToolTip = "Añade una Enfermera Joy que cura al equipo (Ctrl+E): elige la casilla en el mapa. Usa una enfermera real de la ROM como plantilla y añade su script al mapa; fuera de un Centro Pokémon no hay animación de la máquina.";
        _btnDelete.ToolTip = "Elimina un NPC añadido por el editor (Supr). Los NPC del juego original no se eliminan: se explica por qué.";
        _canvas.ContextMenu = menu; _canvas.ContextMenuOpening += (_, _) => UpdateStructureButtons();
        _canvas.ContextTarget += (tile, marker) =>
        {
            _contextTile = tile;
            if (marker is { Kind: PlatinumEventKind.Npc } m && (_event is null || _event.Kind != m.Kind || _event.Index != m.Index)) PickFromCanvas(m.Event);
            UpdateStructureButtons();
        };
        _canvas.PreviewKeyDown += (_, e) =>
        {
            bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0; Action? run = null;
            if (ctrl && e.Key == System.Windows.Input.Key.C) run = DoCopy; else if (ctrl && e.Key == System.Windows.Input.Key.X) run = DoCut; else if (ctrl && e.Key == System.Windows.Input.Key.V) run = () => DoPaste(null);
            else if (ctrl && e.Key == System.Windows.Input.Key.D) run = () => DoDuplicate(null); else if (ctrl && e.Key == System.Windows.Input.Key.E) run = () => DoAddNurse(null); else if (e.Key == System.Windows.Input.Key.Delete) run = DoDelete;
            if (run is null) return; e.Handled = true; run();
        };
        UpdateStructureButtons(); return bar;
    }
    private PlatinumEvent? SelectedTemplate => _event is { } e && NdsNpcStructurePlanner.IsTemplate(e) ? e : null;
    /// <summary>Template for «Añadir»: the selected NPC, else the copied one, else the first ordinary NPC of the map.</summary>
    private PlatinumEvent? DefaultTemplate => SelectedTemplate ?? _clip?.Source ?? (_map is null ? null : _session.Data.Events(_map.Id).FirstOrDefault(NdsNpcStructurePlanner.IsTemplate));
    private void UpdateStructureButtons()
    {
        bool ok = !_busy && _map is not null && _canvas.Model is not null; var t = SelectedTemplate;
        _btnDup.IsEnabled = _btnCopy.IsEnabled = _btnCut.IsEnabled = ok && t is not null;
        _btnAdd.IsEnabled = ok && DefaultTemplate is not null; _btnNurse.IsEnabled = ok && NurseClip is not null;
        if (NurseClip is null) _btnNurse.ToolTip = "La ROM no contiene ninguna enfermera con el script reconocido: no hay plantilla acreditada.";
        _btnPaste.IsEnabled = ok && _clip is not null; _btnDelete.IsEnabled = ok && _event?.Kind == PlatinumEventKind.Npc;
        _btnPaste.Content = _clip is { Cut: true } ? "Pegar (trasladar)" : "Pegar copia";
        _btnPaste.ToolTip = _clip is null ? "No hay nada copiado." : _clip.Cut ? "Traslada el NPC cortado (Ctrl+V) · " + _clip.Origin : "Crea una copia nueva (Ctrl+V) · " + _clip.Origin;
        _btnDelete.Content = _map is not null && _event is { Kind: PlatinumEventKind.Npc } n && _session.IsAddedNpc(_map.Id, n.Index) ? "Eliminar (añadido)" : "Eliminar";
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
        _clip = new NdsNpcClip(t, false, _map.Id, _map.EventFile); _status.Text = $"Copiado: {_clip.Origin}. Pega en este mapa o en otro; nada se ha modificado."; UpdateStructureButtons();
    }
    private void DoCut()
    {
        if (SelectedTemplate is not { } t || _map is null) return;
        _clip = new NdsNpcClip(t, true, _map.Id, _map.EventFile); _status.Text = $"Marcado para trasladar: {_clip.Origin}. No se ha eliminado nada; el traslado solo se aplica al pegar y tras confirmar."; UpdateStructureButtons();
    }
    private void DoDuplicate((int X, int Y)? at)
    {
        if (SelectedTemplate is not { } t || _map is null || !StructureGuard()) return;
        var clip = new NdsNpcClip(t, false, _map.Id, _map.EventFile);
        if (at is { } tile) OnTileChosen(clip, tile.X, tile.Y, openForm: false); else StartPlacement(clip, false);
    }
    private void DoPaste((int X, int Y)? at)
    {
        if (_clip is not { } clip || _map is null || !StructureGuard()) return;
        var problems = NdsNpcStructurePlanner.PasteProblems(_session.Data, clip, _map.Id);
        if (problems.Count > 0) { ShowInfo("No se puede pegar este NPC aquí:\n" + string.Join("\n", problems.Select(p => "• " + p)) + "\nNo se modificó nada ni se perdió el NPC original."); return; }
        if (at is { } tile) OnTileChosen(clip, tile.X, tile.Y, openForm: false); else StartPlacement(clip, false);
    }
    private void DoAdd((int X, int Y)? at)
    {
        if (DefaultTemplate is not { } t || _map is null || !StructureGuard()) return;
        var clip = _clip is { Cut: false } c && ReferenceEquals(c.Source, t) ? c : new NdsNpcClip(t, false, _map.Id, _map.EventFile);
        if (at is { } tile) OnTileChosen(clip, tile.X, tile.Y, openForm: true); else StartPlacement(clip, true);
    }
    private (PlatinumEvents Data, NdsNpcClip? Clip, int Count)? _nurseCache;
    /// <summary>The accredited nurse template of the current data (see <see cref="NdsNpcStructurePlanner.NurseTemplate"/>), computed once per data instance.</summary>
    private NdsNpcClip? NurseClip
    {
        get
        {
            var data = _session.Data;
            if (_nurseCache is not { } cache || !ReferenceEquals(cache.Data, data)) { var clip = NdsNpcStructurePlanner.NurseTemplate(data, out int n); _nurseCache = cache = (data, clip, n); }
            return cache.Clip;
        }
    }
    /// <summary>«Añadir enfermera»: the paste flow of a real nurse with her behaviour, without visiting a Pokémon Center first. The user's clipboard is untouched.</summary>
    private void DoAddNurse((int X, int Y)? at)
    {
        if (NurseClip is not { } clip || _map is null || !StructureGuard()) return;
        var option = BehaviourFor(clip).Single(o => o.Mode == NdsBehaviourMode.WithBehaviour);
        if (!option.Available) { ShowInfo($"No se puede añadir una enfermera funcional en el mapa {_map.Id}:\n• {option.Reason}\nNo se modificó nada."); return; }
        if (at is { } tile) { OnTileChosen(clip, tile.X, tile.Y, openForm: true); return; }
        StartPlacement(clip, true);
        _hover.Text = _status.Text = "Haz clic en la casilla de la enfermera nueva; Esc cancela. No se guarda nada hasta guardar el checkpoint.";
    }
    private void DoDelete()
    {
        if (_event is not { Kind: PlatinumEventKind.Npc } npc || _map is null || _busy) return;
        bool added = _session.IsAddedNpc(_map.Id, npc.Index);
        var report = NdsNpcStructurePlanner.AssessDelete(_session.Data, _map.Id, npc, added);
        if (!report.Allowed) { HideProposal(); ShowInfo($"Eliminar NPC {npc.Index} (ID {npc.Id}) — Mapa {_map.Id}: NO permitido.\n" + string.Join("\n", report.Reasons.Select(r => "• " + r)) + "\nNo se modificó nada."); return; }
        if (!StructureGuard()) return;
        try { ShowProposal(_session.PreviewRemoveAdded(_map.Id, npc.Index)); _status.Text = "Propuesta de eliminación del NPC añadido: revisa las diferencias y guarda un checkpoint nuevo. Aún no se ha guardado nada."; }
        catch (Exception ex) when (Known(ex)) { Fail(ex); }
    }
    private NdsAddPlan PlanFor(NdsNpcClip clip, PlatinumEvent template, NdsMapCanvasModel model, int mapId, int x, int y) =>
        NdsNpcStructurePlanner.PlanAdd(_session.Data, model, mapId, template, NdsNpcStructurePlanner.PasteDraft(_session.Data, clip with { Source = template }, mapId, x, y));
    private void StartPlacement(NdsNpcClip clip, bool openForm)
    {
        if (_canvas.Model is not { } model || _map is null) return;
        int mapId = _map.Id; _placeClip = clip; _placeTemplate = clip.Source; _placeCut = clip.Cut; _placeForm = openForm;
        _canvas.BeginPlacement(clip.Source, (x, y) =>
        {
            if (clip.Cut) { var move = NdsNpcMovePlanner.Plan(model, clip.Source, x, y); return new(move.Outcome, move.Live); }
            var plan = PlanFor(clip, clip.Source, model, mapId, x, y);
            return new(plan.Outcome, $"NPC nuevo en X {x}, Y {y} · {plan.Summary}" + (plan.Reasons.Count > 0 ? " · " + plan.Reasons[0] : ""));
        });
        _hover.Text = clip.Cut ? "Haz clic en la casilla destino del traslado; Esc cancela. No se ha eliminado nada." : "Haz clic en la casilla del NPC nuevo; Esc cancela. No se guarda nada.";
        _status.Text = _hover.Text; _canvas.Focus();
    }
    private bool _placeForm;
    private void OnPlacementChosen(int x, int y) { if (_placeClip is { } clip) OnTileChosen(clip, x, y, _placeForm); }
    /// <summary>A tile was chosen for a clip: a cut becomes the move proposal; a ready copy previews at once; warnings, copies from another map and «Añadir» open the form.</summary>
    private void OnTileChosen(NdsNpcClip clip, int x, int y, bool openForm)
    {
        if (_map is null || _canvas.Model is not { } model) return;
        if (clip.Cut) { OnMoveDropped(NdsNpcMovePlanner.Plan(model, clip.Source, x, y)); return; }
        var draft = NdsNpcStructurePlanner.PasteDraft(_session.Data, clip, _map.Id, x, y);
        var plan = NdsNpcStructurePlanner.PlanAdd(_session.Data, model, _map.Id, clip.Source, draft);
        bool crossMap = NdsNpcStructurePlanner.IsCrossMap(_session.Data, clip, _map.Id);
        // A recognised behaviour (nurse, shared message) is a choice the user must see: open the form with it preselected.
        bool behaviour = BehaviourFor(clip).Any(o => o.Mode != NdsBehaviourMode.Appearance && o.Available);
        if (openForm || crossMap || behaviour || plan.Outcome != NdsMoveOutcome.Ready) { OpenNewForm(clip, draft); return; }
        _newTemplate = clip.Source; _newClip = clip; _newDraft = draft; _newPlan = plan; _newBinding = null; PreviewNewNpc();
    }
    private NdsNpcClip? _newClip;
    private NdsAddDraft? _newDraft;
    private NdsScriptBinding? _newBinding;
    private readonly ComboBox _nMode = new() { MinWidth = 220 };
    private IReadOnlyList<NdsBehaviourOption> BehaviourFor(NdsNpcClip clip) => _map is null ? [] : NdsNpcStructurePlanner.BehaviourOptions(_session.Data, clip, _map.Id);
    private void BuildNewBox()
    {
        _newBox.SetResourceReference(Border.BorderBrushProperty, "Accent"); _newBox.SetResourceReference(Border.BackgroundProperty, "Panel");
        var stack = new StackPanel(); var title = new TextBlock { Text = "NPC nuevo", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var templateRow = new StackPanel { Margin = new Thickness(0, 4, 0, 0) }; templateRow.Children.Add(new TextBlock { Text = "Plantilla (bytes desconocidos, rangos y visión)", FontSize = 11.5 }); templateRow.Children.Add(_nTemplate);
        AccessibleName(_nTemplate, "Eventos DS · nuevo NPC · plantilla");
        var modeRow = new StackPanel { Margin = new Thickness(0, 4, 0, 0) }; modeRow.Children.Add(new TextBlock { Text = "Comportamiento", FontSize = 11.5 }); modeRow.Children.Add(_nMode);
        AccessibleName(_nMode, "Eventos DS · nuevo NPC · comportamiento"); _nMode.SelectionChanged += (_, _) => RevalidateNew();
        var fields = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (label, control, name) in new (string, FrameworkElement, string)[] { ("ID", _nId, "ID"), ("Gráfico", _nGfx, "gráfico"), ("Movimiento", _nMov, "movimiento"), ("Dirección", _nDir, "dirección"), ("X", _nX, "X"), ("Y", _nY, "Y") })
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 8, 4) }; cell.Children.Add(new TextBlock { Text = label, FontSize = 11.5 }); cell.Children.Add(control); fields.Children.Add(cell);
            AccessibleName(control, "Eventos DS · nuevo NPC · " + name);
        }
        var buttons = new WrapPanel(); buttons.Children.Add(_newPreview); buttons.Children.Add(_newCancel);
        AccessibleName(_newBox, "Eventos DS · NPC nuevo"); AccessibleName(_newInfo, "Eventos DS · validación del NPC nuevo"); AccessibleName(_newPreview, "Eventos DS · previsualizar propuesta de NPC nuevo"); AccessibleName(_newCancel, "Eventos DS · cancelar NPC nuevo");
        EditorPresentation.StyleAction(_newPreview, EditorPresentation.ActionRole.Primary); EditorPresentation.StyleAction(_newCancel, EditorPresentation.ActionRole.Tertiary);
        foreach (var box in new[] { _nId, _nX, _nY, _nDir }) box.TextChanged += (_, _) => RevalidateNew();
        foreach (var combo in new[] { _nGfx, _nMov }) combo.SelectionChanged += (_, _) => RevalidateNew();
        _nTemplate.SelectionChanged += (_, _) => { if (!_newLoading && _nTemplate.SelectedItem is TemplateItem item) { _newTemplate = item.Event; RevalidateNew(); } };
        _newCancel.Click += (_, _) => HideProposal();
        _newPreview.Click += (_, _) => PreviewNewNpc();
        // Buttons first: on small windows the explanation can be long, and the action must stay reachable without scrolling.
        var infoScroll = new ScrollViewer { Content = _newInfo, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        stack.Children.Add(title); stack.Children.Add(buttons); stack.Children.Add(modeRow); stack.Children.Add(templateRow); stack.Children.Add(fields); stack.Children.Add(infoScroll); _newBox.Child = stack;
    }
    private void OpenNewForm(NdsNpcClip clip, NdsAddDraft draft)
    {
        if (_map is null) return;
        _newTemplate = clip.Source; _newClip = clip; _newDraft = draft; _newLoading = true;
        var templates = _session.Data.Events(_map.Id).Where(NdsNpcStructurePlanner.IsTemplate).Select(e => new TemplateItem(e, $"NPC {e.Index} de este mapa · ID {e.Id} · gráfico {e.Graphics}")).ToList();
        bool nurse = _nurseCache is { Clip: { } n } && ReferenceEquals(n, clip);
        if (nurse) { templates.RemoveAll(t => ReferenceEquals(t.Event, clip.Source)); templates.Insert(0, new TemplateItem(clip.Source, $"Enfermera Joy · plantilla de la ROM ({clip.Origin})")); }
        else if (NdsNpcStructurePlanner.IsCrossMap(_session.Data, clip, _map.Id)) templates.Insert(0, new TemplateItem(clip.Source, $"Copiado de otro mapa: {clip.Origin}"));
        _nTemplate.ItemsSource = templates; _nTemplate.SelectedItem = templates.FirstOrDefault(t => ReferenceEquals(t.Event, clip.Source)) ?? templates.FirstOrDefault();
        _nGfx.ItemsSource = _session.Data.AccreditedGraphics.ToArray(); _nMov.ItemsSource = _session.Data.AccreditedMovements.ToArray();
        _nId.Text = draft.Id.ToString(CultureInfo.InvariantCulture); _nX.Text = draft.X.ToString(CultureInfo.InvariantCulture); _nY.Text = draft.Y.ToString(CultureInfo.InvariantCulture); _nDir.Text = draft.Direction.ToString(CultureInfo.InvariantCulture);
        _nGfx.SelectedItem = draft.Graphics; _nMov.SelectedItem = draft.Movement;
        var options = BehaviourFor(clip); var defaultMode = NdsNpcStructurePlanner.DefaultMode(options);
        _nMode.Items.Clear();
        foreach (var option in options)
        {
            var item = new ComboBoxItem { Content = option.Label + (option.Available ? "" : " — no disponible"), Tag = option, IsEnabled = option.Available, ToolTip = option.Reason };
            AutomationProperties.SetName(item, "Eventos DS · comportamiento · " + option.Label); AutomationProperties.SetHelpText(item, option.Reason); _nMode.Items.Add(item);
        }
        _nMode.SelectedItem = _nMode.Items.Cast<ComboBoxItem>().First(i => ((NdsBehaviourOption)i.Tag).Mode == defaultMode);
        _newLoading = false;
        _moveBox.Visibility = Visibility.Collapsed; _newBox.Visibility = Visibility.Visible; _inspector.ScrollToTop(); RevalidateNew(); _newBox.BringIntoView();
    }
    private void RevalidateNew()
    {
        if (_newLoading || _newTemplate is not { } template || _map is null || _canvas.Model is not { } model) return;
        static bool Int(TextBox box, out int value) => int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        if (!Int(_nId, out int id) || !Int(_nX, out int x) || !Int(_nY, out int y) || !Int(_nDir, out int dir) || _nGfx.SelectedItem is not int gfx || _nMov.SelectedItem is not int mov)
        {
            _newPlan = null; _newPreview.IsEnabled = false; _newInfo.Text = "Introduce enteros en ID, dirección, X e Y y elige un gráfico y un movimiento acreditados."; return;
        }
        bool crossMap = _newClip is { } c && NdsNpcStructurePlanner.IsCrossMap(_session.Data, c, _map.Id) && ReferenceEquals(template, c.Source);
        int? height = crossMap ? NdsNpcStructurePlanner.TargetHeight(_session.Data, _map.Id, template, x, y).RawHeight : null;
        var option = (_nMode.SelectedItem as ComboBoxItem)?.Tag as NdsBehaviourOption ?? new NdsBehaviourOption(NdsBehaviourMode.Appearance, true, "Solo apariencia", "");
        var (script, binding) = _newClip is { } bc ? NdsNpcStructurePlanner.BindingFor(_session.Data, bc, _map.Id, option.Mode, id) : (0, null);
        var draft = new NdsAddDraft(id, gfx, mov, dir, x, y, height, script); _newDraft = draft; _newBinding = binding;
        var plan = NdsNpcStructurePlanner.PlanAdd(_session.Data, model, _map.Id, template, draft); _newPlan = plan;
        var lines = new List<string> { "Estado: " + plan.Summary, $"Comportamiento: {option.Label} — {option.Reason}" };
        if (option.Mode == NdsBehaviourMode.WithBehaviour)
            lines.AddRange(["Dependencias que se trasladan o reasignan:", $"  · script común {PlatinumScriptFile.PokecenterNurseCommonScript} (global): se conserva", $"  · variable 0x8007 = ID local del NPC nuevo ({id}): se reasigna", "  · textos: los del banco común (ninguno local)", $"  · archivo de scripts {_map.ScriptFile}: se añade el script local {script} al final; los demás no cambian",
                "  · la animación de la máquina de curación solo existe en los Centros Pokémon; fuera de ellos el juego no la reproduce y cura igualmente (comprobado por el propietario en el emulador, en Pueblo Hojaverde)"]);
        lines.AddRange(plan.Reasons.Select(r => "• " + r));
        if (crossMap) { lines.Add("Copia entre mapas (apariencia y configuración básica):"); lines.AddRange(NdsNpcStructurePlanner.CrossMapNotes(_newClip!).Select(r => "  · " + r)); lines.Add("  · " + NdsNpcStructurePlanner.TargetHeight(_session.Data, _map.Id, template, x, y).Source); }
        lines.Add($"Cantidad de NPC en el mapa: {_session.Data.NpcCount(_map.Id)} → {_session.Data.NpcCount(_map.Id) + 1} · script {script}, flag 0, tipo 0.");
        if (plan.CanPropose) lines.Add("No evaluado: alturas y BDHC, comportamiento del tile, puertas, scripts y estado dinámico. Previsualizar no guarda nada.");
        var sharing = _session.Data.Maps.Where(m => m.EventFile == _map.EventFile).Select(m => m.Id).ToList();
        if (sharing.Count > 1) lines.Add($"Archivo de eventos compartido por {sharing.Count} cabeceras ({string.Join(", ", sharing.Take(8))}): el NPC nuevo aparecerá en todas.");
        _newInfo.Text = string.Join("\n", lines); _newPreview.IsEnabled = plan.CanPropose;
        _newPreview.Content = plan.Outcome == NdsMoveOutcome.NeedsWarning ? "Previsualizar propuesta con advertencia" : "Previsualizar propuesta";
    }
    private void PreviewNewNpc()
    {
        if (_newPlan is not { CanPropose: true } plan || _map is null || _newTemplate is not { } template || _newDraft is not { } draft) return;
        try
        {
            var proposal = _session.PreviewAdd(_map.Id, plan.Record!, _newBinding);
            HideProposal(); ShowProposal(proposal); _canvas.SetPendingAddition(template, draft.X, draft.Y);
            _status.Text = $"NPC nuevo preparado en X {draft.X}, Y {draft.Y} (se ve en el mapa como «pendiente»). Pulsa «Guardar cambios…» para crear el checkpoint; aún no se ha guardado nada.";
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
        try { _canvas.SetMap(_session.Data.Layout(map.Id), events, _terrain, _session.Data.Bounds(map.Id), props: _props); }
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
    /// <summary>Smallest height of the working area that still gives the map a useful size; below it the area scrolls instead of shrinking the map.</summary>
    internal const double UsefulMapHeight = 240, MinimumMapHeight = 180, ShortWindowHeight = 620, CompactWidth = 850;
    private bool _brief, _adapting;
    private readonly ScrollViewer _bodyScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly TextBlock _title = new() { Text = "Eventos · Platino CPUE/1", FontSize = 23, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _intro = new() { Text = "Mueve, añade, duplica y copia NPC (también entre mapas, como copia básica). Objetos de fondo, warps y scripts: consulta. Los NPC del juego original no se eliminan.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) };
    internal ScrollViewer WorkingArea => _bodyScroll;
    private void Adapt()
    {
        // Short window: the two explanatory paragraphs give their room to the map and stay available as the tooltip of the title.
        if (_adapting) return;
        bool brief = _brief = ActualHeight > 0 && ActualHeight < ShortWindowHeight;
        _intro.Visibility = _saveHelp.Visibility = brief ? Visibility.Collapsed : Visibility.Visible;
        _title.FontSize = brief ? 17 : 23; _title.ToolTip = brief ? _intro.Text + "\n\n" + _saveHelp.Text : null;
        AutomationProperties.SetHelpText(_title, brief ? _intro.Text + " " + _saveHelp.Text : "");
        bool compact = ActualWidth < CompactWidth;
        _body.ColumnDefinitions[0].Width = new GridLength(compact ? 165 : 205);
        _body.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(280);
        Grid.SetRow(_inspector, compact ? 1 : 0); Grid.SetColumn(_inspector, compact ? 0 : 2); Grid.SetColumnSpan(_inspector, compact ? 2 : 1);
        _inspector.Visibility = compact && _moveBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;   // compact layout: give the canvas the room while a proposal is open
        _inspector.MaxHeight = compact ? (brief ? 120 : 170) : double.PositiveInfinity;
        // A short window keeps only the essential lines under the map (the legend and the help come back when there is room).
        ApplyFooterDetail();
        // In a short window the empty box of differences gives its room to the map; it comes back as soon as there is something to show.
        _differences.Visibility = brief && _differences.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        // The working area is as tall as its viewport, but never shorter than the map column needs with the map at a useful height: then it scrolls,
        // instead of pushing the map out of view. The useful height never exceeds the viewport, so the whole map can always be brought into view.
        double viewport = _bodyScroll.ActualHeight, width = _bodyScroll.ActualWidth;
        if (viewport <= 0 || width <= 0) return;
        double map = brief ? Math.Max(MinimumMapHeight, Math.Min(UsefulMapHeight, viewport - 8)) : MinimumMapHeight;
        if (_canvasFrame is not null)
        {
            _canvasFrame.MinHeight = _canvas.MinHeight = map;
            // In a short window the map is never taller than the viewport either, so that scrolling can show all of it at once.
            _canvasFrame.MaxHeight = brief ? Math.Max(map, viewport - 8) : double.PositiveInfinity; _canvasFrame.VerticalAlignment = brief ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        }
        _adapting = true;
        try
        {
            double column = Math.Max(0, width - SystemParameters.VerticalScrollBarWidth - _body.ColumnDefinitions[0].Width.Value - _body.ColumnDefinitions[2].Width.Value);
            _center.Measure(new System.Windows.Size(column, double.PositiveInfinity));
            double needed = Math.Ceiling(_center.DesiredSize.Height) + (compact && _inspector.Visibility == Visibility.Visible ? _inspector.MaxHeight : 0);
            _body.Height = Math.Max(viewport, needed); _center.InvalidateMeasure();
        }
        finally { _adapting = false; }
    }
    private bool _proposalOpen;
    private void ApplyFooterDetail()
    {
        var visibility = _proposalOpen || _brief ? Visibility.Collapsed : Visibility.Visible;
        _terrainNote.Visibility = visibility; _toolHelp.Visibility = visibility; if (_legend is not null) _legend.Visibility = visibility;
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
        if (item is { Kind: PlatinumEventKind.Npc } && _map is not null)
        {
            string behaviour;
            try { behaviour = item.Script is >= 1 and < 2000 ? _session.Data.ClassifyScript(_map.Id, item.Script)?.Describe() ?? $"script local {item.Script} inexistente" : item.Script == 0 ? "sin script" : $"script {item.Script} (común, de combate o de objeto; no se copia)"; }
            catch (InvalidDataException) { behaviour = "archivo de scripts no legible"; }
            var options = NdsNpcStructurePlanner.BehaviourOptions(_session.Data, new NdsNpcClip(item, false, _map.Id, _map.EventFile), _map.Id);
            _details.Text += $"\nComportamiento: {behaviour}\nCopiar con comportamiento: " + (options.Single(o => o.Mode == NdsBehaviourMode.WithBehaviour) is { Available: true } ? "disponible" : "no — " + options.Single(o => o.Mode == NdsBehaviourMode.WithBehaviour).Reason) + "\n";
        }
        if (item is not null && _canvas.Model?.Find(item.Kind, item.Index) is { } marker)
        {
            _details.Text += $"\nPosición en el mapa esquemático: {_canvas.Model.Describe(item.X, item.Y)}\n{NdsMapCanvasModel.Describe(marker.Placement)}.";
            _details.Text += "\n\nConsulta espacial (solo lectura, no es una validación de juego):\n" + _canvas.Model.Query.Assess(item.X, item.Y, (item.Kind, item.Index)).ToText();
        }
        _canvas.ClearPropSelection(); _canvas.SelectProgrammatic(item, true);
        UpdateStructureButtons();
        _updating = false;
    }
    /// <summary>
    /// A structure was clicked on the canvas. It becomes the only subject of the inspector: the event is deselected first, with the same
    /// confirmation as any other change of selection, so copy, cut, duplicate, delete and the position fields can never act on an event
    /// that is no longer shown. Structures are read-only.
    /// </summary>
    private void ShowProp(PlatinumPlacedProp prop)
    {
        if (_canvas.Model is not { } model || _map is null || _busy) { _canvas.ClearPropSelection(); return; }
        if (_move is not null || _canvas.Drag is not null || _canvas.Placing || _newBox.Visibility == Visibility.Visible) HideProposal();
        _updating = true; CurrentTable.SelectedItem = null; _updating = false;
        SelectEvent(null);
        if (_event is not null) { _canvas.ClearPropSelection(); return; }   // pending fields kept: the user declined
        _canvas.SelectPropProgrammatic(prop);
        _selection.Text = $"Estructura · modelo {prop.Prop.ModelId} · bloque ({prop.BlockX},{prop.BlockY})";
        _details.Text = $"Mapa {_map.Id}: {_map.Name}\n\n" + model.DescribeProp(prop);
        UpdateStructureButtons();
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
    private const string CheckpointHelp = "Guardar crea un checkpoint nuevo .ndsevents.json y actualiza el borrador de la ROM. Reabre ese checkpoint para continuar; Exportar ROM reconstruye una copia nueva. Otras ediciones DS no se guardan en el checkpoint.";
    private const string ProjectHelp = "Guardar aplica el cambio y lo guarda en el proyecto de la ROM, junto con el resto de tu trabajo; al reabrir la ROM se continúa donde lo dejaste. Exportar ROM reconstruye una copia nueva. La ROM de origen no se modifica.";
    private readonly TextBlock _saveHelp = new() { Text = CheckpointHelp, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private Action<NdsEventProposal>? _projectSave;
    /// <summary>
    /// Set when the ROM has a persistent project: saving a proposal goes through it (durable before the session changes) instead of writing a checkpoint.
    /// It runs off the UI thread, like the checkpoint save.
    /// </summary>
    internal Action<NdsEventProposal>? ProjectSave { get => _projectSave; set { _projectSave = value; _saveHelp.Text = value is null ? CheckpointHelp : ProjectHelp; RefreshStatus(); } }
    internal Task SaveToProjectAsync() => SaveAsync(proposal => ProjectSave!(proposal));
    internal Task SaveToAsync(string path) => SaveAsync(proposal => _session.Save(proposal, path));
    private async Task SaveAsync(Action<NdsEventProposal> save)
    {
        if (_busy || _pending is null || _pending.Changes.Count == 0) return;
        _busy = true; _body.IsEnabled = false;
        using var interaction = UiProgress.BlockInteraction();
        try
        {
            var proposal = _pending; await Task.Run(() => save(proposal));
            _dirty = false; InvalidatePreview(); SelectMap(_map is null ? null : _session.Data.Map(_map.Id), confirm: false); RefreshStatus(); Changed?.Invoke();
        }
        catch (Exception ex) when (Known(ex)) { Fail(ex); }
        finally { _busy = false; _body.IsEnabled = true; }
    }
    private void RefreshStatus()
    {
        _undo.IsEnabled = _session.CanUndo;
        _status.Text = ProjectSave is not null ? (_session.CanUndo ? "Cambios de eventos guardados en el proyecto · Exportar ROM para generar la copia jugable." : "Sin cambios de eventos.")
            : _session.LastCheckpoint is null ? "Sin checkpoint guardado." : "Checkpoint verificado: " + _session.LastCheckpoint + " · Exportar ROM para generar la copia jugable.";
    }
    private void Fail(Exception ex) { _status.Text = "No se guardó: " + ex.Message; InvalidatePreview(); }
    private static bool Known(Exception ex) => ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.Text.Json.JsonException;
}
