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
using ComboBox = System.Windows.Controls.ComboBox;
using DataGrid = System.Windows.Controls.DataGrid;
using ListBox = System.Windows.Controls.ListBox;
using Panel = System.Windows.Controls.Panel;
using TabControl = System.Windows.Controls.TabControl;
using Control = System.Windows.Controls.Control;
using UserControl = System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>Native dark WPF editor for real USUM Shop.cro regular/BP inventories.</summary>
internal sealed class MartEditorView : UserControl
{
    private readonly GameProjectSession _project;
    private GameMartService _service;
    private MartShopPreview? _pending;
    private readonly ComboBox _regularPicker = new() { MinWidth = 310, Margin = new Thickness(0, 0, 10, 10) };
    private readonly ComboBox _bpPicker = new() { MinWidth = 310, Margin = new Thickness(0, 0, 10, 10) };
    private readonly DataGrid _regularGrid;
    private readonly DataGrid _bpGrid;
    private readonly ListBox _changes = new() { Height = 126 };
    private readonly TextBlock _status;
    private readonly Button _apply;
    private readonly Button _add, _delete, _freeMega;
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
            Text = "Tiendas y BP · Ultra Sol / Ultra Luna",
            FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = foreground
        });
        heading.Children.Add(new TextBlock
        {
            Text = "28 tiendas normales con ranuras ampliables y 7 tiendas BP con precios editables. " +
                   "Lectura real de Shop.cro mediante relocaciones; cambios aislados hasta confirmar.",
            Foreground = soft, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10)
        });
        var actions = new WrapPanel();
        heading.Children.Add(actions);
        _add = Add(actions, "+ Añadir ranura", AddSlot);
        _delete = Add(actions, "− Eliminar ranura", DeleteSlot);
        _freeMega = Add(actions, "Megapiedras gratis (0 BP)", FreeMega);
        Add(actions, "Generar vista previa", Preview);
        _apply = Add(actions, "Aplicar y verificar", Apply);
        _apply.IsEnabled = false;

        _regularGrid = CreateGrid(false);
        _bpGrid = CreateGrid(true);
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
        _freeMega.IsEnabled = _service.BpShops.Count > 5;
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
        if (_regularPicker.SelectedItem is not MartShop shop ||
            _regularGrid.SelectedItem is not MartSlotRow row || shop.Rows.Count <= 1) return;
        shop.Rows.Remove(row);
        for (int i = 0; i < shop.Rows.Count; i++) shop.Rows[i].Number = i + 1;
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
    private void Preview()
    {
        CommitBoth();
        try
        {
            _pending = _service.Prepare();
            _changes.ItemsSource = _pending.Changes.Take(150)
                .Select(c => $"{c.Name} · {c.Field}: {c.Before} → {c.After}").ToArray();
            _apply.IsEnabled = _pending.Changes.Count > 0;
            _status.Text = $"{_pending.Changes.Count} diferencias; tamaño Shop.cro " +
                $"{_pending.Rebuild.OriginalFileSize:N0} → {_pending.Rebuild.FinalFileSize:N0} bytes. " +
                (_pending.Changes.Count > 150 ? "Se muestran las primeras 150. " : "") +
                "La ROM original sigue intacta.";
        }
        catch (Exception ex)
        {
            _pending = null;
            _apply.IsEnabled = false;
            StudioDialog.Error(Window.GetWindow(this), "Vista previa de tiendas", ex.Message);
        }
    }
    private void Apply()
    {
        CommitBoth();
        if (_pending is null) return;
        if (!StudioDialog.Confirm(Window.GetWindow(this), "Guardar tiendas",
            $"¿Aplicar {_pending.Changes.Count} diferencias a Shop.cro? " +
            "El archivo se verificará antes de escribir y se añadirá al historial.")) return;
        try
        {
            _service.Apply(_pending);
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
