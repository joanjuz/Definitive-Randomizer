using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using DataGrid=System.Windows.Controls.DataGrid;
using DataGridColumnHeader=System.Windows.Controls.Primitives.DataGridColumnHeader;
using Control=System.Windows.Controls.Control;
using TextBox=System.Windows.Controls.TextBox;
using UserControl=System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>In-place USUM Totem caps / BST editor with original 0-based entry IDs.</summary>
internal sealed class TotemRulesView : UserControl
{
    private readonly GameStaticEncounterService _service;
    private readonly bool _bstMode;
    private readonly ObservableCollection<TotemRuleRow> _rows;
    private readonly DataGrid _grid;
    private readonly CheckBox _enabled;
    private readonly Button _apply;
    private readonly TextBlock _status;
    private EditorProposal? _preview;
    internal event Action? Closed;
    internal event Action? Saved;
    internal TotemRulesView(GameStaticEncounterService service,bool bstMode)
    {
        if(!service.SupportsTotemRules)
            throw new NotSupportedException("Totem Level Caps y BST están disponibles únicamente para USUM.");
        _service=service;_bstMode=bstMode;
        _rows=new ObservableCollection<TotemRuleRow>(service.TotemRules());
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel {Margin=new Thickness(18),Background=(Brush)FindResource("Bg")};
        Content=root;
        var top=new StackPanel {Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock
        {
            Text=bstMode?"Totem BST — reglas por encuentro":"Totem Level Caps — reglas por encuentro",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")
        });
        top.Children.Add(new TextBlock
        {
            Text=bstMode
                ?"Intervalos BST de los Totem USUM y Ultra Necrozma. Cambiar especie solo afecta los IDs marcados."
                :"Cap = 0 conserva el nivel actual. La tabla usa los IDs originales de encuentros estáticos USUM.",
            Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)
        });
        _enabled=new CheckBox {Content=bstMode?"Activar Totem BST":"Activar Totem Level Caps",
            IsChecked=false,Margin=new Thickness(0,8,0,6)};
        top.Children.Add(_enabled);
        var actions=new WrapPanel();
        top.Children.Add(actions);
        void Command(string title,Action action)
        {
            var button=new Button {Content=title,Margin=new Thickness(0,2,8,6)};
            button.Click+=(_,_)=>action();actions.Children.Add(button);
        }
        Command("Seleccionar todos",()=>SetVisible(true));
        Command("Deseleccionar todos",()=>SetVisible(false));
        Command("Cargar template…",ImportTemplate);
        Command("Guardar template…",ExportTemplate);
        var bottom=new DockPanel {LastChildFill=false,Margin=new Thickness(0,12,0,0)};
        DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        _status=new TextBlock {Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Text=$"{_rows.Count} encuentros originales identificados.",
            VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};
        DockPanel.SetDock(_status,Dock.Left);bottom.Children.Add(_status);
        var back=new Button {Content="Volver",Margin=new Thickness(8,0,0,0)};
        back.Click+=(_,_)=>Closed?.Invoke();DockPanel.SetDock(back,Dock.Right);bottom.Children.Add(back);
        _apply=new Button {Content="Aplicar propuesta",IsEnabled=false,Margin=new Thickness(8,0,0,0)};
        _apply.Click+=(_,_)=>Apply();DockPanel.SetDock(_apply,Dock.Right);bottom.Children.Add(_apply);
        var preview=new Button {Content="Generar vista previa",Margin=new Thickness(8,0,0,0)};
        preview.Click+=(_,_)=>Prepare();DockPanel.SetDock(preview,Dock.Right);bottom.Children.Add(preview);
        _grid=new DataGrid
        {
            ItemsSource=_rows,AutoGenerateColumns=false,CanUserAddRows=false,
            CanUserDeleteRows=false,CanUserSortColumns=true,RowHeight=34,
            HeadersVisibility=DataGridHeadersVisibility.Column,RowHeaderWidth=0,
            Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),
            RowBackground=(Brush)FindResource("Panel"),AlternatingRowBackground=(Brush)FindResource("Surface"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            HorizontalGridLinesBrush=(Brush)FindResource("Stroke"),
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto
        };
        var header=new Style(typeof(DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)FindResource("Surface")));
        header.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        header.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,8,8,8)));
        _grid.ColumnHeaderStyle=header;
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header="Usar",Width=62,
            Binding=new Binding("Use"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged}
        });
        void Column(string name,string key,int width,bool editable=false)
        {
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header=name,Width=width,IsReadOnly=!editable,
                Binding=new Binding(key)
                {
                    Mode=editable?BindingMode.TwoWay:BindingMode.OneWay,
                    UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,
                    ValidatesOnExceptions=true
                }
            });
        }
        Column("ID",nameof(TotemRuleRow.Id),54);
        Column("Grupo",nameof(TotemRuleRow.Group),140);
        Column("Pokémon actual",nameof(TotemRuleRow.CurrentPokemon),180);
        Column("Nivel actual",nameof(TotemRuleRow.CurrentLevel),95);
        if(bstMode)
        {
            Column("BST actual",nameof(TotemRuleRow.CurrentBST),95);
            Column("BST mínimo",nameof(TotemRuleRow.MinBST),110,true);
            Column("BST máximo",nameof(TotemRuleRow.MaxBST),110,true);
        }
        else Column("Cap (0 = actual)",nameof(TotemRuleRow.LevelCap),155,true);
        _grid.CellEditEnding+=(_,_)=>Invalidate();
        _enabled.Checked+=(_,_)=>Invalidate();_enabled.Unchecked+=(_,_)=>Invalidate();
        foreach(var row in _rows)row.PropertyChanged+=(_,_)=>Invalidate();
        root.Children.Add(_grid);
    }
    private void Invalidate(){_preview=null;if(_apply is not null)_apply.IsEnabled=false;}
    private void SetVisible(bool yes)
    {foreach(var row in _rows)row.Use=yes;Invalidate();}
    private void Prepare()
    {
        if(_enabled.IsChecked!=true)
        {
            _status.Text="Marcá la casilla Activar para generar la propuesta.";
            return;
        }
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell,true);
            _grid.CommitEdit(DataGridEditingUnit.Row,true);
            UiProgress.Begin("Calculando propuesta de Totem…");
            _preview=_service.PreviewTotemRules(_rows.ToArray(),_bstMode);
            _apply.IsEnabled=_preview.Changes.Count>0;
            _status.Text=$"{_preview.Changes.Count} campos modificados. Revisá y confirmá.";
        }
        catch(Exception ex){Invalidate();StudioDialog.Error(Window.GetWindow(this),"Totem",ex.Message);}
        finally{UiProgress.End();}
    }
    private void Apply()
    {
        if(_preview is null)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),
            _bstMode?"Aplicar Totem BST":"Aplicar Totem Level Caps",
            $"Se guardarán {_preview.Changes.Count} cambios en encounterstatic GARC con historial."))return;
        try
        {
            UiProgress.Begin("Guardando encuentros Totem…");
            _service.Apply(_preview);_preview=null;_apply.IsEnabled=false;
            _status.Text="Cambios guardados y registrados en el historial.";
            Saved?.Invoke();
        }
        catch(Exception ex){Invalidate();StudioDialog.Error(Window.GetWindow(this),"Guardar Totem",ex.Message);}
        finally{UiProgress.End();}
    }
    private void ExportTemplate()
    {
        try
        {
            var picker=new Microsoft.Win32.SaveFileDialog
            {
                Filter="Template Totem JSON|*.json",
                FileName=_bstMode?"usum_totem_bst.json":"usum_totem_caps.json"
            };
            if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
            var entries=_rows.Select(r=>new TotemTemplateEntry
            {
                EntryID=r.Id,Use=r.Use,LevelCap=r.LevelCap,MinBST=r.MinBST,MaxBST=r.MaxBST
            }).ToArray();
            var doc=new TotemTemplate {Game="USUM",Enabled=_enabled.IsChecked==true,Entries=entries};
            System.IO.File.WriteAllText(picker.FileName,JsonSerializer.Serialize(doc,
                new JsonSerializerOptions{WriteIndented=true}));
            _status.Text="Template exportado.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Exportar Totem",ex.Message);}
    }
    private void ImportTemplate()
    {
        var picker=new Microsoft.Win32.OpenFileDialog {Filter="Template Totem JSON|*.json"};
        if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        try
        {
            var doc=JsonSerializer.Deserialize<TotemTemplate>(System.IO.File.ReadAllText(picker.FileName),
                new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
                ??throw new InvalidDataException("Template vacío.");
            if(!string.Equals(doc.Game,"USUM",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El template no corresponde a USUM.");
            if(doc.Entries.GroupBy(x=>x.EntryID).Any(g=>g.Count()>1))
                throw new InvalidDataException("El template tiene IDs Totem repetidos.");
            foreach(var entry in doc.Entries)
            {
                var row=_rows.FirstOrDefault(x=>x.Id==entry.EntryID)
                    ??throw new InvalidDataException($"ID Totem desconocido: {entry.EntryID}.");
                if(_bstMode&&(entry.MinBST<1||entry.MaxBST>1530||entry.MinBST>entry.MaxBST)
                    ||!_bstMode&&(entry.LevelCap<0||entry.LevelCap>100))
                    throw new InvalidDataException($"Valor inválido para Totem {entry.EntryID}.");
                row.Use=entry.Use;row.LevelCap=entry.LevelCap;
                row.MinBST=entry.MinBST;row.MaxBST=entry.MaxBST;
            }
            _enabled.IsChecked=doc.Enabled;Invalidate();_status.Text="Template cargado. Generá vista previa.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Importar Totem",ex.Message);}
    }
    public sealed class TotemTemplate
    {
        public int Version {get;set;}=1;
        public string Name {get;set;}="USUM Totem Rules";
        public string Game {get;set;}="USUM";
        public bool Enabled {get;set;}=true;
        public TotemTemplateEntry[] Entries {get;set;}=[];
    }
    public sealed class TotemTemplateEntry
    {
        public int EntryID {get;set;}
        public bool Use {get;set;}=true;
        public int LevelCap {get;set;}
        public int MinBST {get;set;}
        public int MaxBST {get;set;}
    }
}
