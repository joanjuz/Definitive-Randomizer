using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
using UserControl=System.Windows.Controls.UserControl;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using CheckBox=System.Windows.Controls.CheckBox;
using DataGrid=System.Windows.Controls.DataGrid;
using Binding=System.Windows.Data.Binding;
namespace ThreeDS.Progressive;
internal sealed class SwitchDataDraft
{
    internal SwordShieldDataCell Cell {get;}
    public string Entry {get;}
    public string Field=>Cell.Field;
    public string Range=>Cell.Min+"–"+Cell.Max;
    public int Original=>Cell.Value;
    public string Value {get;set;}
    internal SwitchDataDraft(SwordShieldDataCell cell,string label=""){Cell=cell;Entry=cell.Location+(label.Length==0?"":" · "+label);Value=cell.Value.ToString(CultureInfo.InvariantCulture);}
}
internal sealed class SwitchDataEditorView:UserControl
{
    private readonly SwordShieldProject _project;
    private readonly SwordShieldDataSection _section;
    private readonly DataGrid _grid=new(){AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,FrozenColumnCount=1,EnableRowVirtualization=true,EnableColumnVirtualization=true};
    private readonly TextBox _filter=new(),_seed=new(){IsReadOnly=true,Text=SwitchSeed.New32().ToString(CultureInfo.InvariantCulture)},_percent=new(){Text="50"};
    private readonly CheckBox _trade=new(){Content="Convertir evoluciones por intercambio a nivel 37"};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button _apply=new(){Content="Añadir al mod",IsEnabled=false};
    private readonly List<Button> _buttons=new();
    private List<SwitchDataDraft> _rows=new();
    private SwordShieldDataPreview? _pending;
    private bool _busy;
    private string? _loadedRevision;
    internal bool Busy=>_busy;
    internal event Action? Changed;
    internal static string TitleFor(SwordShieldDataSection section)=>section switch{SwordShieldDataSection.TrainerTeams=>"Entrenadores · equipos",SwordShieldDataSection.Moves=>"Movimientos",SwordShieldDataSection.Learnsets=>"Level Up Moves",SwordShieldDataSection.Evolutions=>"Evoluciones",SwordShieldDataSection.Machines=>"MT y tutores",SwordShieldDataSection.Items=>"Objetos y economía",SwordShieldDataSection.Shops=>"Tiendas",SwordShieldDataSection.Statics=>"Encuentros estáticos",SwordShieldDataSection.Gifts=>"Regalos",_=>"Intercambios"};
    internal SwitchDataEditorView(SwordShieldProject project,SwordShieldDataSection section)
    {
        _project=project;_section=section;NameScope.SetNameScope(this,new NameScope());var root=new Grid{Margin=new Thickness(16)};Content=root;root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new StackPanel();root.Children.Add(new ScrollViewer{Content=header,MaxHeight=245,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});header.Children.Add(new TextBlock{Text=TitleFor(section),FontSize=23});header.Children.Add(new TextBlock{Text="Edición manual y randomización con vista previa. Los cambios se añaden a la sesión y al historial; exportar crea un mod sin escribir en tu extracción.",TextWrapping=TextWrapping.Wrap});
        void Field(string text,TextBox box){var label=new System.Windows.Controls.Label{Content=text,Target=box};AutomationProperties.SetLabeledBy(box,label);header.Children.Add(label);header.Children.Add(box);}
        Field("Filtrar por entrada o campo",_filter);Field("Semilla aleatoria",_seed);_filter.TextChanged+=(_,_)=>{if(_grid.ItemsSource is not null)CollectionViewSource.GetDefaultView(_grid.ItemsSource).Refresh();};
        _trade.Visibility=section==SwordShieldDataSection.Evolutions?Visibility.Visible:Visibility.Collapsed;header.Children.Add(_trade);
        if(section==SwordShieldDataSection.Machines)Field("Probabilidad de compatibilidad (0–100 %)",_percent);
        header.Children.Add(new TextBlock{Text=section switch{SwordShieldDataSection.Gifts=>"Randomizar conserva los regalos de Kubfu, Glastrier y Spectrier hasta validar sus guiones de DLC. La edición manual sigue disponible. Los modelos de los regalos mostrados en el mapa no se modifican.",SwordShieldDataSection.TrainerTeams=>"Especie, forma, nivel, objeto, cuatro movimientos, naturaleza (0–24; 255 = aleatoria), EV e IV. Randomizar cambia especies y conserva los demás parámetros. Los bits de shiny y Dynamax no se modifican al editar IV.",SwordShieldDataSection.Moves=>"Randomizar cambia tipos y categorías; los demás campos se editan manualmente. Se conservan efectos, banderas y movimientos no utilizables.",SwordShieldDataSection.Learnsets=>"Randomizar conserva niveles y cantidad de aprendizajes, excluye movimientos Z/Max y garantiza un movimiento ofensivo inicial. No cambia movimientos huevo.",SwordShieldDataSection.Evolutions=>"Randomizar dirige las rutas existentes a especies terminales para evitar ciclos. La opción de intercambio conserva destinos y usa nivel 37. Se muestran las rutas existentes.",SwordShieldDataSection.Machines=>"Compatibilidad MT00–99, TR00–99 y tutores. 0 = no; 1 = sí. Conserva bits reservados. No cambia qué movimiento contiene cada MT/TR.",SwordShieldDataSection.Shops=>"Conserva tiendas, etapas de desbloqueo y objetos clave/MT/TR. Cada objeto común cambia por otro permitido; no implementa compras únicas de 3DS.",SwordShieldDataSection.Items=>"Precios en dinero, Watts y BP. La randomización conserva los precios gratuitos y excluye objetos clave y MT/TR.",_=>"Conserva identificadores, requisitos, guiones, movimientos y parámetros no editados. Los estáticos con escenarios especiales de historia se protegen y no se muestran."},TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
        foreach(var c in new[]{("Entrada / ranura","Entry",2,true),("Campo","Field",2,true),("Rango","Range",1,true),("Actual","Original",1,true),("Nuevo valor","Value",1,false)})_grid.Columns.Add(new DataGridTextColumn{Header=c.Item1,Binding=new Binding(c.Item2){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},IsReadOnly=c.Item4,Width=new DataGridLength(c.Item3,DataGridLengthUnitType.Star)});
        DarkGridTheme.Apply(_grid);_grid.FrozenColumnCount=1;AutomationProperties.SetName(_grid,"Tabla editable de "+TitleFor(section));RegisterName("SwitchDataGrid",_grid);_grid.CellEditEnding+=(_,_)=>Invalidate();Grid.SetRow(_grid,1);root.Children.Add(_grid);
        var footer=new StackPanel();Grid.SetRow(footer,2);root.Children.Add(footer);var actions=new WrapPanel();footer.Children.Add(actions);
        Button Add(string text,Action action){var b=new Button{Content=text,Margin=new Thickness(0,6,8,6)};EditorPresentation.StyleAction(b,EditorPresentation.ActionRole.Secondary);b.Click+=(_,_)=>action();actions.Children.Add(b);_buttons.Add(b);return b;}
        Add("Recargar tabla",()=>_ = LoadAsync());Add("Nueva semilla aleatoria",()=>{_seed.Text=SwitchSeed.New32().ToString(CultureInfo.InvariantCulture);Invalidate();});Add("Previsualizar edición",()=>_ = PreviewAsync(false));Add("Previsualizar randomización",()=>_ = PreviewAsync(true));actions.Children.Add(_apply);EditorPresentation.StyleAction(_apply,EditorPresentation.ActionRole.Primary);_apply.Click+=async(_,_)=>{if(_pending is null)return;try{_project.ApplyData(_pending);Changed?.Invoke();await LoadAsync();}catch(Exception e){Error(e);}};RegisterName("SwitchDataApply",_apply);RegisterName("SwitchDataSeed",_seed);AutomationProperties.SetLiveSetting(_status,AutomationLiveSetting.Polite);footer.Children.Add(new ScrollViewer{Content=_status,MaxHeight=90,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});_percent.TextChanged+=(_,_)=>Invalidate();_trade.Checked+=(_,_)=>Invalidate();_trade.Unchecked+=(_,_)=>Invalidate();
    }
    private void Error(Exception e)=>StudioDialog.Error(Window.GetWindow(this),"Editor Switch",e.Message);
    private void Invalidate(){_pending=null;_apply.IsEnabled=false;}
    private void SetBusy(bool busy){_busy=busy;_grid.IsEnabled=!busy;_filter.IsEnabled=_percent.IsEnabled=_trade.IsEnabled=!busy;foreach(var b in _buttons)b.IsEnabled=!busy;_apply.IsEnabled=!busy&&_pending?.Result.Changes.Count>0;}
    internal async Task LoadAsync()
    {
        if(_busy)return;Invalidate();SetBusy(true);try{var cells=await Task.Run(()=>_project.ReadData(_section));_loadedRevision=_project.RevisionHash;var names=_project.Entries.ToDictionary(e=>e.Index,e=>e.Name);string Label(SwordShieldDataCell c)=>_section switch{SwordShieldDataSection.TrainerTeams=>_project.TrainerName(c.Entry),SwordShieldDataSection.Moves=>_project.MoveName(c.Entry),SwordShieldDataSection.Items=>_project.ItemName(c.Entry),SwordShieldDataSection.Machines or SwordShieldDataSection.Learnsets or SwordShieldDataSection.Evolutions=>names.GetValueOrDefault(c.Entry,""),_=>""};_rows=cells.Select(c=>new SwitchDataDraft(c,Label(c))).ToList();_grid.ItemsSource=_rows;CollectionViewSource.GetDefaultView(_rows).Filter=item=>item is SwitchDataDraft row&&(row.Entry+" "+row.Field).Contains(_filter.Text,StringComparison.CurrentCultureIgnoreCase);_status.Text=$"{_rows.Count:N0} campos. Edita Nuevo valor y genera una vista previa.";}catch(Exception e){Error(e);}finally{SetBusy(false);}
    }
    internal async Task PreviewAsync(bool random)
    {
        if(_busy)return;try{if(_loadedRevision!=_project.RevisionHash)throw new InvalidOperationException("La sesión cambió. Recarga la tabla antes de editar para conservar los cambios de las otras secciones.");_grid.CommitEdit(DataGridEditingUnit.Cell,true);_grid.CommitEdit(DataGridEditingUnit.Row,true);var edits=_rows.Where(r=>r.Value!=r.Original.ToString(CultureInfo.InvariantCulture)).Select(r=>new SwordShieldDataEdit(r.Cell.Key,int.Parse(r.Value,CultureInfo.InvariantCulture))).ToArray();if(random&&edits.Length>0)throw new InvalidOperationException("Hay campos editados. Previsualiza y añade esa edición antes de randomizar.");if(!int.TryParse(_percent.Text,out int percent))throw new ArgumentException("Porcentaje inválido.");if(random&&_project.DataHistory.LastOrDefault()?.Seed==uint.Parse(_seed.Text,CultureInfo.InvariantCulture))_seed.Text=SwitchSeed.New32().ToString(CultureInfo.InvariantCulture);var options=random?new SwordShieldDataOptions(uint.Parse(_seed.Text,CultureInfo.InvariantCulture),_trade.IsChecked==true,percent):null;SetBusy(true);_pending=await Task.Run(()=>_project.PreviewData(_section,edits,options));_status.Text=$"{_pending.Result.Changes.Count:N0} cambios pendientes. "+string.Join(" · ",_pending.Result.Changes.Take(10).Select(c=>$"{c.Location} {c.Field}: {c.Before} → {c.After}"));}catch(Exception e){Invalidate();Error(e);}finally{SetBusy(false);}
    }
}
