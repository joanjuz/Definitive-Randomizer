using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using ComboBox=System.Windows.Controls.ComboBox;
using DataGrid=System.Windows.Controls.DataGrid;
using UserControl=System.Windows.Controls.UserControl;
using ListBox=System.Windows.Controls.ListBox;
using Panel=System.Windows.Controls.Panel;
using Brush=System.Windows.Media.Brush;
using Binding=System.Windows.Data.Binding;
namespace ThreeDS.Progressive;
internal sealed class Gen6MartView:UserControl
{
    internal GameMart6Service Editor {get;private set;}
    private readonly GameProjectSession _project;
    private readonly ComboBox _picker=new(){DisplayMemberPath="Name",MinWidth=320};
    private readonly DataGrid _grid=new(){AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false};
    private readonly Dictionary<string,CheckBox> _checks=[];
    private readonly ListBox _changes=new(){Height=140};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button _apply;
    private List<MartShop> _shops=[];
    private EditorProposal? _pending;
    private bool _dirty,_loading;
    internal Gen6MartView(GameProjectSession project,RecordEditorView.UiState? saved=null)
    {
        _project=project;Editor=new(project);Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Tiendas · XY / ORAS",FontSize=23,Foreground=(Brush)FindResource("TextMain")});
        top.Children.Add(new TextBlock{Text=Editor.SupportsExpansion?$"Añadí o eliminá objetos: hasta 64 por tienda y {Editor.Capacity} ranuras en total. Las plumas conservan su efecto EV Gen6.":"Inventarios fijos. Ampliar requiere una extracción con ExHeader compatible.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,4,0,8)});
        var options=new WrapPanel();top.Children.Add(options);
        string[] keys=["Randomize","Candies","EVItems","BanCapsule","SpecialOnly","KeepX"];
        string[] labels=["Randomizar tiendas","Caramelos raros","Plumas EV","Prohibir Cápsula Habilidad","Solo tiendas especiales","Conservar objetos X"];
        var values=EditorValues.Flatten(Editor.RandomOptions);if(saved is not null)foreach(var p in saved.Options)if(values.ContainsKey(p.Key))values[p.Key]=p.Value;
        if(saved is not null&&values["UseChecks"]==0){values["Randomize"]=values["Operation"]==0?1:0;values["Candies"]=values["Operation"]==1?1:0;}
        for(int i=0;i<keys.Length;i++){var check=new CheckBox{Content=labels[i],IsChecked=values[keys[i]]==1,Foreground=(Brush)FindResource("TextMain"),Margin=new Thickness(0,4,12,8)};_checks[keys[i]]=check;options.Children.Add(check);}
        var actions=new WrapPanel();top.Children.Add(actions);
        Add(actions,"+ Añadir objeto",()=>{if(_picker.SelectedItem is not MartShop shop)return;if(shop.Rows.Count>=64||_shops.Sum(s=>s.Rows.Count)>=Editor.Capacity){_status.Text="Capacidad de ranuras alcanzada.";return;}shop.Rows.Add(new(shop.Rows.Count+1,(_grid.SelectedItem as MartSlotRow)?.ItemId??4));}).IsEnabled=Editor.SupportsExpansion;
        Add(actions,"− Eliminar objeto",()=>{if(_picker.SelectedItem is MartShop shop&&_grid.SelectedItem is MartSlotRow row&&shop.Rows.Count>1){shop.Rows.Remove(row);for(int i=0;i<shop.Rows.Count;i++)shop.Rows[i].Number=i+1;_grid.Items.Refresh();}}).IsEnabled=Editor.SupportsExpansion;
        Add(actions,"Generar vista previa",Preview);_apply=Add(actions,"Aplicar y verificar",Apply);_apply.IsEnabled=false;
        top.Children.Add(_picker);_picker.SelectionChanged+=(_,_)=>{_grid.CommitEdit();_grid.ItemsSource=(_picker.SelectedItem as MartShop)?.Rows;};
        _grid.Columns.Add(new DataGridTextColumn{Header="Ranura",Width=90,Binding=new Binding("Number"),IsReadOnly=true});
        _grid.Columns.Add(new DataGridComboBoxColumn{Header="Objeto",MinWidth=400,ItemsSource=project.ItemNames.Select((n,i)=>new MoveChoice(i,n)).ToArray(),SelectedValuePath="Id",DisplayMemberPath="Display",SelectedValueBinding=new Binding("ItemId"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        _grid.Background=(Brush)FindResource("Panel");_grid.Foreground=(Brush)FindResource("TextMain");_grid.RowBackground=(Brush)FindResource("Panel");_grid.AlternatingRowBackground=(Brush)FindResource("Surface");
        _grid.HeadersVisibility=DataGridHeadersVisibility.Column;_grid.RowHeaderWidth=0;_grid.RowHeight=35;_grid.ColumnHeaderHeight=36;
        _grid.BorderBrush=(Brush)FindResource("Stroke");_grid.GridLinesVisibility=DataGridGridLinesVisibility.Horizontal;
        _grid.HorizontalGridLinesBrush=(Brush)FindResource("Stroke");
        var header=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        header.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty,(Brush)FindResource("Surface")));
        header.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        header.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty,new Thickness(10,6,10,6)));
        _grid.ColumnHeaderStyle=header;
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);bottom.Children.Add(_changes);bottom.Children.Add(_status);_changes.Background=(Brush)FindResource("Panel");_changes.Foreground=_status.Foreground=(Brush)FindResource("TextSoft");root.Children.Add(_grid);
        Bind();foreach(var check in _checks.Values){check.Checked+=(_,_)=>Dirty();check.Unchecked+=(_,_)=>Dirty();}
    }
    private static Button Add(Panel p,string text,Action action){var b=new Button{Content=text,Margin=new Thickness(0,4,8,8)};b.Click+=(_,_)=>action();p.Children.Add(b);return b;}
    private void Bind()
    {
        _loading=true;var items=Editor.ReadAllItems();_shops=Editor.Entries.Select((e,i)=>new MartShop(i,false,e.Name,items[i].Select((id,n)=>new MartSlotRow(n+1,id)))).ToList();
        foreach(var shop in _shops){foreach(var row in shop.Rows)row.PropertyChanged+=(_,_)=>Dirty();shop.Rows.CollectionChanged+=(_,e)=>{if(e.NewItems is not null)foreach(MartSlotRow row in e.NewItems)row.PropertyChanged+=(_,_)=>Dirty();Dirty();};}
        _picker.ItemsSource=_shops;_picker.SelectedIndex=0;_loading=false;_dirty=false;_pending=null;_apply.IsEnabled=false;
    }
    private void Dirty(){if(_loading)return;_dirty=true;_pending=null;_apply.IsEnabled=false;_changes.ItemsSource=null;_status.Text="Cambios pendientes: generá una nueva vista previa.";}
    internal RecordEditorView.UiState RememberOptions(){var values=EditorValues.Flatten(Editor.RandomOptions);values["UseChecks"]=1;foreach(var pair in _checks)values[pair.Key]=pair.Value.IsChecked==true?1:0;return new(values,0);}
    internal void Preview()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell,true);_grid.CommitEdit(DataGridEditingUnit.Row,true);
        try{_pending=Editor.PrepareDraft(_shops.Select(s=>s.Rows.Select(r=>r.ItemId).ToArray()).ToArray(),RememberOptions().Options,Editor.Entries.Select(e=>e.Id).ToArray(),System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue));_changes.ItemsSource=_pending.Changes.Take(150).Select(c=>$"{c.Name} · {c.Field}: {c.Before} → {c.After}").ToArray();_apply.IsEnabled=_pending.Changes.Count>0;_status.Text=$"{_pending.Changes.Count} diferencias preparadas. No se ha guardado.";}
        catch(Exception ex){_pending=null;_apply.IsEnabled=false;StudioDialog.Error(Window.GetWindow(this),"Tiendas Gen6",ex.Message);}
    }
    private void Apply(){if(_pending is null||!StudioDialog.Confirm(Window.GetWindow(this),"Guardar tiendas Gen6",$"¿Aplicar {_pending.Changes.Count} diferencias y registrarlas en el historial?"))return;try{Editor.Apply(_pending);Editor=new(_project);Bind();_status.Text="Tiendas guardadas, releídas y verificadas.";}catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Tiendas Gen6",ex.Message);}}
    internal bool ConfirmLeave()=>!_dirty||StudioDialog.Confirm(Window.GetWindow(this),"Tiendas pendientes","¿Descartar los cambios sin guardar?");
}
