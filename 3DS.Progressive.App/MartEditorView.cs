using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using DataGrid = System.Windows.Controls.DataGrid;
using ListBox = System.Windows.Controls.ListBox;
using Panel = System.Windows.Controls.Panel;
using TabControl = System.Windows.Controls.TabControl;
using Control = System.Windows.Controls.Control;
using Binding = System.Windows.Data.Binding;
using UserControl = System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>Native dark WPF editor for real USUM Shop.cro regular/BP inventories.</summary>
internal sealed class MartEditorView : UserControl
{
    private readonly Button[] _recipeFlags=Enumerable.Range(0,6).Select(_=>new Button()).ToArray();
    private readonly bool[] _recipeSelected=new bool[6];
    private int _previewSeed=System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue);
    internal Gen7MartState RememberRecipe()=>new(_recipeSelected[0],_recipeSelected[1],_recipeSelected[2],_recipeSelected[3],_recipeSelected[4],_recipeSelected[5],_specialOnly.IsChecked==true,_protectX.IsChecked==true);
    internal void RestoreRecipe(Gen7MartState s){bool[] flags=[s.RandomRegular,s.RandomBP,s.Candies,s.Vitamins,s.FreeMega,s.BanCapsule];for(int i=0;i<flags.Length;i++)SetRecipeSelection(i,flags[i]);_specialOnly.IsChecked=s.SpecialsOnly;_protectX.IsChecked=s.ProtectX;}
    private readonly GameProjectSession _project;
    private GameMartService _service;
    private MartShopPreview? _pending;
    private GameMartService? _preparedService;
    private readonly ComboBox _regularPicker = new() { MinWidth = 310, Margin = new Thickness(0, 0, 10, 10) };
    private readonly ComboBox _bpPicker = new() { MinWidth = 310, Margin = new Thickness(0, 0, 10, 10) };
    private readonly DataGrid _regularGrid;
    private readonly DataGrid _bpGrid;
    private readonly ListBox _changes = new() { Height = 126 };
    private readonly TextBlock _status;
    private readonly Button _apply;
    private readonly Button _add, _delete;
    private readonly CheckBox _specialOnly = new() { Content = "Solo tiendas especiales", Margin = new Thickness(0, 5, 16, 10) };
    private readonly CheckBox _protectX = new() { Content = "Conservar objetos X", IsChecked = true, Margin = new Thickness(0, 5, 16, 10) };
    private readonly TabControl _tabs = new();
    private readonly IReadOnlyList<MoveChoice> _items;
    private bool _dirty, _loading;

    internal MartEditorView(GameProjectSession project)
    {
        _project = project;
        _service = new GameMartService(project);
        _items = project.ItemNames.Select((name, id) =>
            new MoveChoice(id, string.IsNullOrWhiteSpace(name) ? $"Objeto #{id}" : name)).ToArray();
        Background = (Brush)FindResource("Bg");
        var foreground = (Brush)FindResource("TextMain");
        var soft = (Brush)FindResource("TextSoft");
        var root = new DockPanel { Margin = new Thickness(20) };
        Content = root;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        heading.Children.Add(new TextBlock
        {
            Text = project.GameConfiguration.USUM?"Tiendas y BP · Ultra Sol / Ultra Luna":"Tiendas y BP · Sol / Luna",
            FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = foreground
        });
        heading.Children.Add(new TextBlock
        {
            Text = project.GameConfiguration.USUM?"28 tiendas normales con ranuras ampliables y 7 tiendas BP con precios editables. " +
                   "Lectura real de Shop.cro mediante relocaciones; cambios aislados hasta confirmar.":"24 tiendas normales y 6 tiendas BP con ranuras fijas y precios editables. Cambios aislados hasta confirmar.",
            Foreground = soft, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10)
        });
        heading.Children.Add(new TextBlock{Text="Opciones de la vista previa y de Global/Batch",Foreground=soft});
        var recipes=new WrapPanel();string[] labels=["Randomizar tiendas","Randomizar BP","Caramelos raros",project.GameConfiguration.USUM?"Vitaminas 252 EV":"Objetos de entrenamiento EV","Megapiedras gratis (en BP)","Prohibir Cápsula Habilidad"];
        for(int i=0;i<_recipeFlags.Length;i++)
        {
            int index=i;var button=_recipeFlags[i];button.Content=labels[i];button.Margin=new Thickness(0,4,8,8);
            button.Click+=(_,_)=>{SetRecipeSelection(index,true);Dirty();Preview();};
            var remove=new MenuItem{Header="Quitar de la vista previa y de Global/Batch"};
            remove.Click+=(_,_)=>{SetRecipeSelection(index,false);Dirty();Preview();};
            button.ContextMenu=new ContextMenu{Items={remove}};
            SetRecipeSelection(i,false);recipes.Children.Add(button);
        }
        heading.Children.Add(recipes);
        Add(recipes,"Restablecer opciones",()=>{for(int i=0;i<_recipeSelected.Length;i++)SetRecipeSelection(i,false);Dirty();Preview();});
        var actions = new WrapPanel();
        heading.Children.Add(actions);
        _add = Add(actions, "+ Añadir ranura", AddSlot);
        _delete = Add(actions, "− Eliminar ranura", DeleteSlot);
        if(!_service.SupportsExpansion)_add.Visibility=_delete.Visibility=Visibility.Collapsed;
        heading.Children.Add(new WrapPanel { Children = { _specialOnly, _protectX } });
        Add(actions, "Generar vista previa", RegeneratePreview);
        _apply = Add(actions, "Aplicar y verificar", Apply);
        _apply.IsEnabled = false;

        _regularGrid = CreateGrid(false);
        _bpGrid = CreateGrid(true);
        _regularGrid.SelectionChanged += (_, _) => UpdateButtons();
        _tabs.Items.Add(new TabItem { Header = "Tiendas normales", Content = Pane(false) });
        _tabs.Items.Add(new TabItem { Header = "Tiendas BP", Content = Pane(true) });
        _tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == _tabs) UpdateButtons();
        };
        root.Children.Add(_tabs);

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Remove(_tabs);
        root.Children.Add(bottom);
        root.Children.Add(_tabs);
        bottom.Children.Add(new TextBlock
        {
            Text = "CAMBIOS PROPUESTOS · Antes → Después",
            Foreground = soft, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        _changes.Background = (Brush)FindResource("Panel");
        _changes.Foreground = foreground;
        bottom.Children.Add(_changes);
        _status = new TextBlock
        {
            Text = "Seleccioná una tienda para editar. No se escribirá nada hasta aplicar.",
            Foreground = soft, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0)
        };
        bottom.Children.Add(_status);
        BindShops();
        _specialOnly.Checked+=(_,_)=>Dirty();_specialOnly.Unchecked+=(_,_)=>Dirty();
        _protectX.Checked+=(_,_)=>Dirty();_protectX.Unchecked+=(_,_)=>Dirty();
    }

    private void SetRecipeSelection(int index,bool selected)
    {
        _recipeSelected[index]=selected;
        var button=_recipeFlags[index];
        button.SetResourceReference(StyleProperty,selected?"PrimaryButton":"SecondaryButton");
        button.ToolTip=selected?"Incluido en la vista previa y en Global/Batch. Botón derecho para quitarlo.":"Añadir a la vista previa sin quitar las otras acciones.";
        System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"Incluido":"Sin incluir");
    }

    private static Button Add(Panel parent, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 8) };
        button.Click += (_, _) => action();
        parent.Children.Add(button);
        return button;
    }

    private DataGrid CreateGrid(bool bp)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            CanUserReorderColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeaderWidth = 0, RowHeight = 35, ColumnHeaderHeight = 36,
            Background = (Brush)FindResource("Panel"),
            RowBackground = (Brush)FindResource("Panel"),
            AlternatingRowBackground = (Brush)FindResource("Surface"),
            Foreground = (Brush)FindResource("TextMain"),
            BorderBrush = (Brush)FindResource("Stroke"),
            BorderThickness = new Thickness(1),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = (Brush)FindResource("Stroke")
        };
        var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty, (Brush)FindResource("Surface")));
        header.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)FindResource("TextMain")));
        header.Setters.Add(new Setter(Control.BorderBrushProperty, (Brush)FindResource("Stroke")));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
        grid.ColumnHeaderStyle = header;
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "#", Width = 60, IsReadOnly = true,
            Binding = new Binding(nameof(MartSlotRow.Number))
        });
        grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Objeto", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ItemsSource = _items, DisplayMemberPath = nameof(MoveChoice.Display),
            SelectedValuePath = nameof(MoveChoice.Id),
            SelectedValueBinding = new Binding(nameof(MartSlotRow.ItemId))
            { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
        });
        if (!bp && _service.SupportsExpansion)
        {
            var removeButton = new FrameworkElementFactory(typeof(Button));
            removeButton.SetValue(Button.ContentProperty, "Quitar");
            removeButton.SetValue(Button.PaddingProperty, new Thickness(9, 3, 9, 3));
            removeButton.SetValue(Button.MarginProperty, new Thickness(3, 2, 3, 2));
            removeButton.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, args) =>
            {
                if (sender is Button { DataContext: MartSlotRow row })
                {
                    DeleteSpecificRow(row);
                    args.Handled = true;
                }
            }));
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Acción", Width = 90,
                CellTemplate = new DataTemplate { VisualTree = removeButton }
            });
        }
        if (bp)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Precio BP", Width = 110,
                Binding = new Binding(nameof(MartSlotRow.Price))
                { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                    ValidatesOnExceptions = true }
            });
        return grid;
    }

    private DockPanel Pane(bool bp)
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        top.Children.Add(new TextBlock
        {
            Text = bp ? "Sede BP" : "Ubicación / progreso en historia",
            Foreground = (Brush)FindResource("TextSoft"),
            Margin = new Thickness(0, 0, 0, 5)
        });
        var picker = bp ? _bpPicker : _regularPicker;
        picker.DisplayMemberPath = nameof(MartShop.Name);
        picker.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        picker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (bp) { CommitGrid(_bpGrid); _bpGrid.ItemsSource = (picker.SelectedItem as MartShop)?.Rows; }
            else { CommitGrid(_regularGrid); _regularGrid.ItemsSource = (picker.SelectedItem as MartShop)?.Rows; }
            UpdateButtons();
        };
        top.Children.Add(picker);
        root.Children.Add(bp ? _bpGrid : _regularGrid);
        return root;
    }

    private void BindShops()
    {
        _loading = true;
        _regularPicker.ItemsSource = _service.RegularShops;
        _bpPicker.ItemsSource = _service.BpShops;
        foreach (MartShop shop in _service.RegularShops.Concat(_service.BpShops))
        {
            shop.Rows.CollectionChanged += RowsChanged;
            foreach (var row in shop.Rows) row.PropertyChanged += SlotChanged;
        }
        _regularPicker.SelectedIndex = 0;
        _bpPicker.SelectedIndex = 0;
        _regularGrid.ItemsSource = _service.RegularShops[0].Rows;
        _bpGrid.ItemsSource = _service.BpShops[0].Rows;
        _loading = false;
        _dirty = false;
        _pending = null;
        _preparedService = null;
        _changes.ItemsSource = null;
        UpdateButtons();
    }

    private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (MartSlotRow row in e.OldItems) row.PropertyChanged -= SlotChanged;
        if (e.NewItems is not null)
            foreach (MartSlotRow row in e.NewItems) row.PropertyChanged += SlotChanged;
        Dirty();
    }
    private void SlotChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Dirty();
    private void Dirty()
    {
        if (_loading) return;
        _dirty = true;
        _pending = null;
        _preparedService = null;
        _apply.IsEnabled = false;
        _changes.ItemsSource = null;
        _status.Text = "Ediciones pendientes. Generá una nueva vista previa antes de guardar.";
    }
    private static void CommitGrid(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }
    private void CommitBoth() { CommitGrid(_regularGrid); CommitGrid(_bpGrid); }
    private void UpdateButtons()
    {
        bool regular = _tabs.SelectedIndex == 0;
        _add.IsEnabled = regular;
        _delete.IsEnabled = regular && _regularPicker.SelectedItem is MartShop shop &&
            shop.Rows.Count > 1 && _regularGrid.SelectedItem is MartSlotRow;
    }
    private void AddSlot()
    {
        CommitBoth();
        if (_regularPicker.SelectedItem is not MartShop shop) return;
        if (shop.Rows.Count >= pk3DS.Core.Modding.Research.Gen7ExpandedMartTable.MaximumSlotsPerMart)
        {
            StudioDialog.Error(Window.GetWindow(this), "Tiendas", "Se alcanzó el máximo de ranuras de la tienda."); return;
        }
        int item = (_regularGrid.SelectedItem as MartSlotRow)?.ItemId ?? shop.Rows[^1].ItemId;
        shop.Rows.Add(new MartSlotRow(shop.Rows.Count + 1, item));
        _regularGrid.SelectedItem = shop.Rows[^1];
        UpdateButtons();
    }
    private void DeleteSlot()
    {
        CommitBoth();
        if (_regularGrid.SelectedItem is MartSlotRow selected) DeleteSpecificRow(selected);
    }
    private void DeleteSpecificRow(MartSlotRow row)
    {
        if (_regularPicker.SelectedItem is not MartShop shop ||
            shop.Rows.Count <= 1 || !shop.Rows.Contains(row)) return;
        CommitBoth();
        if (!GameMartService.TryRemoveRegularSlot(shop, row)) return;
        _regularGrid.Items.Refresh();
        UpdateButtons();
    }
    private void FreeMega()
    {
        CommitBoth();
        if (!StudioDialog.Confirm(Window.GetWindow(this), "Megapiedras BP",
            "¿Poner a 0 BP todas las megapiedras de Battle Tree? Solo cambia los precios, no los objetos.")) return;
        _service.FreeMegaStoneBP();
        Dirty();
    }
    private void Preset(Func<int> action, string name)
    {
        CommitBoth();
        try
        {
            int count = action();
            Dirty();
            _regularGrid.Items.Refresh();
            _bpGrid.Items.Refresh();
            _status.Text = $"{name}: {count} cambios preparados. Revisá las tablas y generá la vista previa antes de guardar.";
        }
        catch (Exception ex) { StudioDialog.Error(Window.GetWindow(this), name, ex.Message); }
    }
    private void RegeneratePreview()
    {
        _previewSeed=System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue);
        Preview();
    }
    private void Preview()
    {
        CommitBoth();
        try
        {
            var draft=new GameMartService(_project);
            for(int i=0;i<_service.RegularShops.Count;i++)
            {draft.RegularShops[i].Rows.Clear();foreach(var row in _service.RegularShops[i].Rows)draft.RegularShops[i].Rows.Add(new(row.Number,row.ItemId,row.Price));}
            for(int i=0;i<_service.BpShops.Count;i++)for(int j=0;j<_service.BpShops[i].Rows.Count;j++)
            {draft.BpShops[i].Rows[j].ItemId=_service.BpShops[i].Rows[j].ItemId;draft.BpShops[i].Rows[j].Price=_service.BpShops[i].Rows[j].Price;}
            _pending = NativeGen7MartBridge.Prepare(draft,RememberRecipe(),_previewSeed);
            _preparedService=draft;
            _dirty = _pending.Changes.Count>0;
            _changes.ItemsSource = _pending.Changes.Take(150)
                .Select(c => $"{c.Name} · {c.Field}: {c.Before} → {c.After}").ToArray();
            _apply.IsEnabled = _pending.Changes.Count > 0;
            _status.Text = $"{_pending.Changes.Count} diferencias; tamaño Shop.cro " +
                $"{_pending.Rebuild.OriginalFileSize:N0} → {_pending.Rebuild.FinalFileSize:N0} bytes. " +
                (_pending.ItemOutput is null ? "" : "Incluye GARC de objetos (precios y flags EV). ") +
                (_pending.Changes.Count > 150 ? "Se muestran las primeras 150. " : "") +
                "La ROM original sigue intacta.";
        }
        catch (Exception ex)
        {
            _pending = null;
            _preparedService = null;
            _apply.IsEnabled = false;
            StudioDialog.Error(Window.GetWindow(this), "Vista previa de tiendas", ex.Message);
        }
    }
    private void Apply()
    {
        CommitBoth();
        if (_pending is null || _preparedService is null) return;
        if (!StudioDialog.Confirm(Window.GetWindow(this), "Guardar tiendas",
            $"¿Aplicar {_pending.Changes.Count} diferencias a Shop.cro? " +
            "El archivo se verificará antes de escribir y se añadirá al historial.")) return;
        try
        {
            _preparedService.Apply(_pending);
            _loading = true;
            _service = new GameMartService(_project);
            BindShops();
            _loading = false;
            _status.Text = "Shop.cro guardado, releído y registrado en el historial.";
        }
        catch (Exception ex) { _loading = false; StudioDialog.Error(Window.GetWindow(this), "Guardar tiendas", ex.Message); }
    }
    internal bool ConfirmLeave() =>
        !_dirty || StudioDialog.Confirm(Window.GetWindow(this), "Tiendas con cambios",
            "Hay modificaciones de tiendas sin guardar. ¿Descartarlas y salir?");
}
