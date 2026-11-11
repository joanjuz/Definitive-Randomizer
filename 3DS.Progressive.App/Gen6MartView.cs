using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using DataGrid=System.Windows.Controls.DataGrid;
using UserControl=System.Windows.Controls.UserControl;
using ListBox=System.Windows.Controls.ListBox;
using Brush=System.Windows.Media.Brush;
using Binding=System.Windows.Data.Binding;
namespace ThreeDS.Progressive;
internal sealed class Gen6MartView:UserControl
{
    internal GameMart6Service Editor {get;private set;}
    private readonly GameProjectSession _project;
    // Shop selector: a vertical list in the left column of the shared mart layout.
    private readonly ListBox _picker=new();
    private readonly DataGrid _grid=new(){AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false};
    private readonly Dictionary<string,CheckBox> _checks=[];
    private readonly ListBox _changes=new(){Height=140};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _shopTitle=new(){FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _shopDetail=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,8)};
    private readonly TextBlock _shopsHeading;
    private readonly Button _apply;
    private List<MartShop> _shops=[];
    private EditorProposal? _pending;
    private bool _dirty,_loading;
    internal Gen6MartView(GameProjectSession project,RecordEditorView.UiState? saved=null)
    {
        _project=project;Editor=new(project);Background=(Brush)FindResource("Bg");MinHeight=MartWorkspace.MinimumHeight;
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Tiendas · XY / ORAS",FontSize=23,Foreground=(Brush)FindResource("TextMain")});
        TextBlock description;top.Children.Add(description=new TextBlock{Text=Editor.SupportsExpansion?$"Añade o elimina objetos: hasta 64 por tienda y {Editor.Capacity} ranuras en total. Las plumas conservan su efecto EV Gen6.":"Inventarios fijos. Ampliar requiere una extracción con ExHeader compatible.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,4,0,0)});
        MartWorkspace.FitDescription(this,description);
        string[] keys=["Randomize","Candies","EVItems","BanCapsule","SpecialOnly","KeepX"];
        string[] labels=["Randomizar tiendas","Caramelos raros","Plumas EV","Prohibir Cápsula Habilidad","Solo tiendas especiales","Conservar objetos X"];
        var values=EditorValues.Flatten(Editor.RandomOptions);if(saved is not null)foreach(var p in saved.Options)if(values.ContainsKey(p.Key))values[p.Key]=p.Value;
        if(saved is not null&&values["UseChecks"]==0){values["Randomize"]=values["Operation"]==0?1:0;values["Candies"]=values["Operation"]==1?1:0;}
        for(int i=0;i<keys.Length;i++)
        {
            var check=new CheckBox{Content=new TextBlock{Text=labels[i],TextWrapping=TextWrapping.Wrap},IsChecked=values[keys[i]]==1,Foreground=(Brush)FindResource("TextMain"),Margin=new Thickness(0,2,0,8)};
            System.Windows.Automation.AutomationProperties.SetName(check,labels[i]);_checks[keys[i]]=check;
        }

        // Right column: actions on the selected shop, options grouped by purpose and preview/apply pinned at the bottom.
        var options=new StackPanel();
        MartWorkspace.Heading(options,"Inventario de la tienda",first:true);
        MartWorkspace.Action(options,"+ Añadir objeto",()=>{if(_picker.SelectedItem is not MartShop shop)return;if(shop.Rows.Count>=64||_shops.Sum(s=>s.Rows.Count)>=Editor.Capacity){_status.Text="Capacidad de ranuras alcanzada.";return;}shop.Rows.Add(new(shop.Rows.Count+1,(_grid.SelectedItem as MartSlotRow)?.ItemId??4));}).IsEnabled=Editor.SupportsExpansion;
        MartWorkspace.Action(options,"− Eliminar objeto",()=>{if(_picker.SelectedItem is MartShop shop&&_grid.SelectedItem is MartSlotRow row&&shop.Rows.Count>1){shop.Rows.Remove(row);for(int i=0;i<shop.Rows.Count;i++)shop.Rows[i].Number=i+1;_grid.Items.Refresh();}}).IsEnabled=Editor.SupportsExpansion;
        MartWorkspace.Heading(options,"Opciones de la vista previa y de Global/Batch");
        MartWorkspace.Heading(options,"Randomización");
        foreach(string key in new[]{"Randomize","SpecialOnly","KeepX"})options.Children.Add(_checks[key]);
        MartWorkspace.Heading(options,"Objetos especiales");
        foreach(string key in new[]{"Candies","EVItems"})options.Children.Add(_checks[key]);
        MartWorkspace.Heading(options,"Restricciones");
        options.Children.Add(_checks["BanCapsule"]);
        var commit=new StackPanel{Margin=new Thickness(0,10,0,0)};
        MartWorkspace.Heading(commit,"Vista previa y guardado",first:true);
        MartWorkspace.Action(commit,"Generar vista previa",Preview);_apply=MartWorkspace.Action(commit,"Aplicar y verificar",Apply);_apply.IsEnabled=false;
        _apply.SetResourceReference(StyleProperty,"PrimaryButton");_apply.HorizontalContentAlignment=System.Windows.HorizontalAlignment.Center;
        var actionsPane=new DockPanel();
        var commitFrame=new Border{BorderThickness=new Thickness(0,1,0,0),Child=commit};commitFrame.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        DockPanel.SetDock(commitFrame,Dock.Bottom);actionsPane.Children.Add(commitFrame);
        actionsPane.Children.Add(MartWorkspace.Scroll(options,gutter:4));

        // Left column: every shop in one vertical list with its number of objects.
        var shopsContent=new StackPanel();_shopsHeading=MartWorkspace.Heading(shopsContent,"Tiendas",first:true);shopsContent.Children.Add(_picker);
        MartWorkspace.ConfigureShopList(_picker,"Tiendas");
        _picker.SelectionChanged+=(_,e)=>{if(e.Source!=_picker)return;_grid.CommitEdit();_grid.ItemsSource=(_picker.SelectedItem as MartShop)?.Rows;UpdateShopHeading();};
        var shopsPane=MartWorkspace.Card(MartWorkspace.Scroll(shopsContent));

        _grid.Columns.Add(new DataGridTextColumn{Header="Ranura",Width=90,Binding=new Binding("Number"),IsReadOnly=true});
        _grid.Columns.Add(new DataGridComboBoxColumn{Header="Objeto",MinWidth=220,ItemsSource=project.ItemNames.Select((n,i)=>new MoveChoice(i,n)).ToArray(),SelectedValuePath="Id",DisplayMemberPath="Display",SelectedValueBinding=new Binding("ItemId"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        _grid.Background=(Brush)FindResource("Panel");_grid.Foreground=(Brush)FindResource("TextMain");_grid.RowBackground=(Brush)FindResource("Panel");_grid.AlternatingRowBackground=(Brush)FindResource("Surface");
        _grid.HeadersVisibility=DataGridHeadersVisibility.Column;_grid.RowHeaderWidth=0;_grid.RowHeight=35;_grid.ColumnHeaderHeight=36;
        _grid.BorderBrush=(Brush)FindResource("Stroke");_grid.GridLinesVisibility=DataGridGridLinesVisibility.Horizontal;
        _grid.HorizontalGridLinesBrush=(Brush)FindResource("Stroke");
        var header=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        header.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty,(Brush)FindResource("Surface")));
        header.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        header.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty,new Thickness(10,6,10,6)));
        _grid.ColumnHeaderStyle=header;

        // Centre column: the selected shop and its table, then the proposed changes and the status.
        var center=new DockPanel();
        _shopTitle.Foreground=(Brush)FindResource("TextMain");_shopDetail.Foreground=(Brush)FindResource("TextSoft");
        var shopHeading=MartWorkspace.ShopHeading(_shopTitle,_shopDetail);DockPanel.SetDock(shopHeading,Dock.Top);center.Children.Add(shopHeading);
        var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);center.Children.Add(bottom);
        bottom.Children.Add(new TextBlock{Text="CAMBIOS PROPUESTOS · Antes → Después",Foreground=(Brush)FindResource("TextSoft"),FontSize=11,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
        bottom.Children.Add(_changes);bottom.Children.Add(_status);_status.Margin=new Thickness(0,7,0,0);
        _changes.Background=(Brush)FindResource("Panel");_changes.Foreground=_status.Foreground=(Brush)FindResource("TextSoft");center.Children.Add(_grid);
        var workspace=new MartWorkspace(shopsPane,center,MartWorkspace.Card(actionsPane)){Margin=new Thickness(0,14,0,0)};
        workspace.FitChanges(_changes,140);root.Children.Add(workspace);
        Bind();foreach(var check in _checks.Values){check.Checked+=(_,_)=>Dirty();check.Unchecked+=(_,_)=>Dirty();}
    }
    private void UpdateShopHeading()
    {
        var shop=_picker.SelectedItem as MartShop;
        _shopsHeading.Text=$"Tiendas · {_shops.Count}";
        _shopTitle.Text=shop?.Name??"Tiendas";
        _shopDetail.Text=shop is null?"":$"{shop.Rows.Count} objetos"+(Editor.SupportsExpansion?$" (máximo 64) · {_shops.Sum(s=>s.Rows.Count)} de {Editor.Capacity} ranuras en total":"");
    }
    private void Bind()
    {
        _loading=true;var items=Editor.ReadAllItems();_shops=Editor.Entries.Select((e,i)=>new MartShop(i,false,e.Name,items[i].Select((id,n)=>new MartSlotRow(n+1,id)))).ToList();
        foreach(var shop in _shops){foreach(var row in shop.Rows)row.PropertyChanged+=(_,_)=>Dirty();shop.Rows.CollectionChanged+=(_,e)=>{if(e.NewItems is not null)foreach(MartSlotRow row in e.NewItems)row.PropertyChanged+=(_,_)=>Dirty();Dirty();};}
        _picker.ItemsSource=_shops;_picker.SelectedIndex=0;_loading=false;_dirty=false;_pending=null;_apply.IsEnabled=false;UpdateShopHeading();
    }
    private void Dirty(){if(_loading)return;_dirty=true;_pending=null;_apply.IsEnabled=false;_changes.ItemsSource=null;_status.Text="Cambios pendientes: genera una nueva vista previa.";UpdateShopHeading();}
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
