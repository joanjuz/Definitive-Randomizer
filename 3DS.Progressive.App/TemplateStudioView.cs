using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Binding=System.Windows.Data.Binding;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using CheckBox=System.Windows.Controls.CheckBox;
using DataGrid=System.Windows.Controls.DataGrid;
using TextBox=System.Windows.Controls.TextBox;
using UserControl=System.Windows.Controls.UserControl;
using Brush=System.Windows.Media.Brush;
using Panel=System.Windows.Controls.Panel;
using TabControl=System.Windows.Controls.TabControl;
namespace ThreeDS.Progressive;

internal sealed class TemplateStudioRow : INotifyPropertyChanged
{
    internal readonly Dictionary<string,string> Values;
    private readonly Action<TemplateStudioRow,string>? _changed;
    internal TemplateStudioRow(Dictionary<string,string> values,Action<TemplateStudioRow,string>? changed=null){Values=values;_changed=changed;}
    public string this[string key]{get=>Values.GetValueOrDefault(key,"");set{if(this[key]==value)return;Values[key]=value;_changed?.Invoke(this,key);Changed("Item[]");}}
    public IReadOnlyList<TemplateChoice> ArgumentChoices {get;set;}=[];
    public bool HasArgumentChoices=>ArgumentChoices.Count>0;
    public IReadOnlyList<TemplateChoice> BattleChoices {get;set;}=[];
    public string BattleDescription=>BattleChoices.FirstOrDefault(c=>c.Value==this["BattlePatch"])?.Description??"Parche importado. Revisá su compatibilidad antes de usarlo.";
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Changed(string name)=>PropertyChanged?.Invoke(this,new(name));
}
internal sealed class TemplateItemPolicy : INotifyPropertyChanged
{
    public int Id {get;init;}
    public string Name {get;init;}="";
    private string _policy="Default";
    public string Policy{get=>_policy;set{_policy=value;PropertyChanged?.Invoke(this,new(nameof(Policy)));}}
    private string _effectiveStatus="Permitido";
    public string EffectiveStatus{get=>_effectiveStatus;set{if(_effectiveStatus==value)return;_effectiveStatus=value;PropertyChanged?.Invoke(this,new(nameof(EffectiveStatus)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}
internal sealed class TemplateStudioView : UserControl
{
    private readonly GameProjectSession _project;
    private readonly ComboBox _kind;
    private readonly DataGrid _grid=new(),_catalogue=new(),_preview=new();
    private readonly TextBlock _help=new(),_status=new();
    private readonly StackPanel _options=new();
    private readonly TextBox _filter=new();
    private readonly Button _use,_addRow,_removeRows,_progressive;
    private readonly Dictionary<string,AuthoredTemplate> _drafts=[];
    private readonly TabItem _catalogueTab,_tableTab;
    private readonly TabControl _tabs;
    private readonly ComboBox _policyFilter=new();
    private readonly ObservableCollection<TemplateStudioRow> _rows=[];
    private readonly ObservableCollection<TemplateItemPolicy> _items=[];
    private AuthoredTemplate _document;
    private readonly Dictionary<string,CheckBox> _checks=[];
    private bool _busy,_loading;
    private string? _validated;
    private int _seed;
    internal TemplateStudioView(GameProjectSession project)
    {
        _project=project;_document=AuthoredTemplate.Create(project,"Moves");
        Background=(Brush)FindResource("Bg");var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Templates · crear y personalizar",Style=(Style)FindResource("PageHeading"),Foreground=(Brush)FindResource("TextMain")});
        _help.Foreground=(Brush)FindResource("TextSoft");_help.TextWrapping=TextWrapping.Wrap;_help.Margin=new Thickness(0,8,0,12);top.Children.Add(_help);
        var bar=new WrapPanel();top.Children.Add(bar);
        _kind=new ComboBox{IsEditable=false,ItemsSource=new[]{new TemplateChoice("Moves","Movimientos"),new("Evolutions","Evoluciones"),new("Stats","Estadísticas Pokémon"),new("FieldItems","Field Items"),new("Shops","Tiendas")},DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue="Moves",Width=230,Margin=new Thickness(0,0,8,10)};bar.Children.Add(_kind);
        Add(bar,"Importar template…",Import);
        _progressive=Add(bar,"Importar Progressive",async ()=>await ImportProgressiveAsync());
        Add(bar,"Exportar template…",Export);
        _addRow=Add(bar,"Añadir fila",AddRow);_removeRows=Add(bar,"Eliminar fila",()=>{Commit();if(_grid.SelectedItem is TemplateStudioRow row)_rows.Remove(row);Invalidate();},EditorPresentation.ActionRole.Tertiary);
        top.Children.Add(_options);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var actions=new WrapPanel();bottom.Children.Add(actions);Add(actions,"Comprobar y previsualizar",async ()=>await Execute(false));_use=Add(actions,"Usar template",async ()=>await Execute(true),EditorPresentation.ActionRole.Primary);_use.IsEnabled=false;
        _status.Foreground=(Brush)FindResource("TextSoft");_status.TextWrapping=TextWrapping.Wrap;bottom.Children.Add(_status);
        var tabs=_tabs=new TabControl();root.Children.Add(tabs);
        ConfigureGrid(_grid);_grid.ItemsSource=_rows;_grid.SelectionMode=DataGridSelectionMode.Single;_tableTab=new TabItem{Header="Tabla del template",Content=EditorPresentation.EmptyState(_grid,"Empezá tu template","Añadí una fila o importá un template para editar sus valores.")};tabs.Items.Add(_tableTab);
        ConfigureGrid(_catalogue);_catalogue.Columns.Add(new DataGridTextColumn{Header="ID",Binding=new Binding("Id"),IsReadOnly=true,Width=70});_catalogue.Columns.Add(new DataGridTextColumn{Header="Objeto",Binding=new Binding("Name"),IsReadOnly=true,Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=180});
        _catalogue.Columns.Add(new DataGridTextColumn{Header="Estado actual",Binding=new Binding("EffectiveStatus"),IsReadOnly=true,Width=150});
        _catalogue.Columns.Add(ChoiceColumn("Regla",new Binding("Policy"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},new[]{new TemplateChoice("Default","Predeterminado"),new("Allowed","Permitido"),new("Banned","Prohibido")},220));
        _catalogue.ItemsSource=_items;var cataloguePanel=new DockPanel();_filter.Margin=new Thickness(0,0,0,8);_filter.ToolTip="Buscar objetos por nombre o ID";DockPanel.SetDock(_filter,Dock.Top);cataloguePanel.Children.Add(_filter);
        _policyFilter.ItemsSource=new[]{new TemplateChoice("All","Todos"),new("Allowed","Permitidos"),new("Banned","Prohibidos")};_policyFilter.DisplayMemberPath="Label";_policyFilter.SelectedValuePath="Value";_policyFilter.SelectedValue="All";_policyFilter.Margin=new Thickness(0,0,0,8);_policyFilter.IsEditable=false;_policyFilter.SelectionChanged+=(_,_)=>RefreshItemFilter();DockPanel.SetDock(_policyFilter,Dock.Top);cataloguePanel.Children.Add(_policyFilter);cataloguePanel.Children.Add(_catalogue);
        _filter.TextChanged+=(_,_)=>RefreshItemFilter();CollectionViewSource.GetDefaultView(_items).Filter=MatchesItemFilter;
        _catalogueTab=new TabItem{Header="Catálogo · permitidos y prohibidos",Content=cataloguePanel};tabs.Items.Add(_catalogueTab);
        ConfigureGrid(_preview);_preview.IsReadOnly=true;foreach(string field in new[]{"Name","Field","Before","After"})_preview.Columns.Add(new DataGridTextColumn{Header=field switch{"Name"=>"Entrada","Field"=>"Campo","Before"=>"Antes",_=>"Después"},Binding=new Binding(field),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});tabs.Items.Add(new TabItem{Header="Cambios previstos",Content=EditorPresentation.EmptyState(_preview,"Sin cambios previsualizados","Comprobá el template para revisar aquí los valores antes y después.")});
        _kind.SelectionChanged+=async (_,_)=>
        {
            if(_loading||_kind.SelectedItem is not TemplateChoice)return;string next=CurrentKind;
            try{_drafts[_document.Kind]=Capture();}catch(Exception e){Error(e);_loading=true;_kind.SelectedValue=_document.Kind;_loading=false;return;}
            await LoadAsync(()=>_drafts.TryGetValue(next,out var saved)?saved:AuthoredTemplate.Create(project,next));
        };
        _rows.CollectionChanged+=(_,_)=>Invalidate();_grid.CellEditEnding+=(_,_)=>Invalidate();_catalogue.CellEditEnding+=(_,_)=>Invalidate();LoadDocument(_document);
    }
    private string CurrentKind=>(string?)_kind.SelectedValue??"Moves";
    private void ConfigureGrid(DataGrid grid){grid.AutoGenerateColumns=false;grid.CanUserAddRows=false;grid.CanUserDeleteRows=false;grid.EnableRowVirtualization=true;grid.EnableColumnVirtualization=true;DarkGridTheme.Apply(grid);grid.FrozenColumnCount=1;}
    private bool MatchesItemFilter(object value)
    {
        if(value is not TemplateItemPolicy item)return false;
        string policy=(string?)_policyFilter.SelectedValue??"All";
        return (policy=="All"||item.EffectiveStatus==(policy=="Allowed"?"Permitido":"Prohibido"))&&(item.Name.Contains(_filter.Text,StringComparison.CurrentCultureIgnoreCase)||item.Id.ToString().Contains(_filter.Text));
    }
    private void RefreshItemFilter()
    {
        bool whitelist=_items.Any(item=>item.Policy=="Allowed");
        foreach(var item in _items)item.EffectiveStatus=item.Policy!="Banned"&&(!whitelist||item.Policy=="Allowed")?"Permitido":"Prohibido";
        var view=CollectionViewSource.GetDefaultView(_items);
        if(view is System.ComponentModel.IEditableCollectionView edit&&edit.IsEditingItem){Dispatcher.BeginInvoke(()=>{_catalogue.CommitEdit(DataGridEditingUnit.Cell,true);_catalogue.CommitEdit(DataGridEditingUnit.Row,true);view.Refresh();});}
        else view.Refresh();
    }
    private Button Add(Panel panel,string label,Action action,EditorPresentation.ActionRole role=EditorPresentation.ActionRole.Secondary){var b=new Button{Content=label,Margin=new Thickness(0,0,8,10)};EditorPresentation.StyleAction(b,role);panel.Children.Add(b);b.Click+=(_,_)=>{try{action();}catch(Exception e){Error(e);}};return b;}
    private void Error(Exception e){_status.Text=e.Message;StudioDialog.Error(Window.GetWindow(this),"Templates",e.Message);}
    private void Invalidate(){_validated=null;if(_use is not null)_use.IsEnabled=false;}
    private void Commit(){if(!_grid.CommitEdit(DataGridEditingUnit.Cell,true)||!_grid.CommitEdit(DataGridEditingUnit.Row,true)||!_catalogue.CommitEdit(DataGridEditingUnit.Cell,true)||!_catalogue.CommitEdit(DataGridEditingUnit.Row,true))throw new InvalidDataException("Terminá de corregir la celda antes de continuar.");}
    private AuthoredTemplate Capture()
    {
        Commit();_document.Rows=_rows.Select(r=>new Dictionary<string,string>(r.Values)).ToList();foreach(var pair in _checks)_document.Options[pair.Key]=pair.Value.IsChecked==true?1:0;
        _document.BannedItems=_items.Where(i=>i.Policy=="Banned").Select(i=>i.Id).ToArray();_document.AllowedItems=_items.Where(i=>i.Policy=="Allowed").Select(i=>i.Id).ToArray();_document.CanonicalizeIds(_project);return JsonSerializer.Deserialize<AuthoredTemplate>(_document.Serialize())!;
    }
    private Task ImportProgressiveAsync(){string kind=CurrentKind;return LoadAsync(()=>AuthoredTemplate.Progressive(_project,kind));}
    private async Task LoadAsync(Func<AuthoredTemplate> load)
    {
        if(_busy)return;_busy=true;IsEnabled=false;using var interaction=UiProgress.BlockInteraction();UiProgress.Begin("Preparando editor de templates…");try{var d=await Task.Run(()=>{var prepared=load();prepared.PrepareMetadata(_project);return prepared;});LoadDocument(d);}catch(Exception e){Error(e);_loading=true;_kind.SelectedValue=_document.Kind;_loading=false;}finally{IsEnabled=true;_busy=false;UiProgress.End();}
    }
    private void LoadDocument(AuthoredTemplate d)
    {
        _loading=true;try
        {
            if(d.Kind=="Shops"&&d.ShopMode!="AppendProgression")throw new InvalidDataException("Esta plantilla antigua reemplaza inventarios. El creador de tiendas ahora añade objetos por progreso; las recetas Global anteriores mantienen su comportamiento original.");
            _document=d;_kind.SelectedValue=d.Kind;
            _addRow.Visibility=_removeRows.Visibility=d.Kind=="FieldItems"?Visibility.Collapsed:Visibility.Visible;
            _progressive.Visibility=AuthoredTemplate.CanImportProgressive(_project,d.Kind)?Visibility.Visible:Visibility.Collapsed;
            _progressive.ToolTip="Cargar los valores Progressive para editarlos";
            _rows.Clear();_items.Clear();_checks.Clear();_options.Children.Clear();_grid.Columns.Clear();
            _help.Text=d.Kind switch
            {
                "Moves"=>"Añadí movimientos y elegí sus cambios. Las casillas vacías conservan el valor del juego; el tipo y la categoría se completan al elegir el movimiento. BattlePatch empieza en None: pasá el cursor para ver su función. Las descripciones se aplican al banco de español. Usar guarda una copia y registra el template para Global/Batch, después de randomizar.",
                "Evolutions"=>"Cada fila añade o actualiza una ruta de evolución. Elegí origen, destino, método, nivel y argumento. El argumento puede ser un objeto o un valor del método; las otras rutas se conservan.",
                "Stats"=>"Elegí una especie o forma y escribí solo las estadísticas que querés modificar (1–255). Las casillas vacías conservan sus valores.",
                "FieldItems"=>"Configurá las opciones y el catálogo de objetos en la segunda pestaña. Si hay permitidos, forman el catálogo de reemplazo; los prohibidos nunca se eligen. Importar Progressive muestra el catálogo del motor para personalizarlo. Usar randomiza los recogibles; Batch utiliza la semilla de cada ROM.",
                _=>"Añadí un objeto y elegí desde qué avance de la historia se vende. Se añade una ranura en esa tienda y en las etapas posteriores; se conserva todo el inventario existente, las tiendas especiales y BP. Si el objeto ya está disponible, no se duplica."+(_project.GameConfiguration.SM?" Sol/Luna todavía no admite ampliar estas tablas: se avisará antes de escribir si hace falta una ranura nueva.":"")
            };
            foreach(string column in AuthoredTemplate.Columns(d.Kind,d.Generation))if(column!="Price"||d.Generation==7)AddColumn(column);
            foreach(var row in d.Rows)_rows.Add(Row(row));
            _catalogueTab.Visibility=d.Kind=="FieldItems"?Visibility.Visible:Visibility.Collapsed;
            _tableTab.Visibility=d.Kind=="FieldItems"?Visibility.Collapsed:Visibility.Visible;
            _tabs.SelectedItem=d.Kind=="FieldItems"?_catalogueTab:_tableTab;
            if(d.Kind=="FieldItems")
            {
                for(int i=1;i<_project.ItemNames.Count;i++){var item=new TemplateItemPolicy{Id=i,Name=_project.ItemNames[i],Policy=d.BannedItems.Contains(i)?"Banned":d.AllowedItems.Contains(i)?"Allowed":"Default"};item.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(TemplateItemPolicy.Policy)){Invalidate();if(!_loading)RefreshItemFilter();}};_items.Add(item);}
                var labels=d.OptionLabels;
                var wrap=new WrapPanel();_options.Children.Add(wrap);foreach(var option in d.Options.Where(p=>p.Key is not ("Operation" or "UseChecks"))){var check=new CheckBox{Content=labels.GetValueOrDefault(option.Key,option.Key),IsChecked=option.Value==1,Margin=new Thickness(0,0,18,10)};check.Checked+=(_,_)=>Invalidate();check.Unchecked+=(_,_)=>Invalidate();_checks[option.Key]=check;wrap.Children.Add(check);}
            }
            RefreshItemFilter();
            _preview.ItemsSource=null;_status.Text="El borrador todavía no se ha aplicado. Exportar no modifica la ROM.";Invalidate();
        }
        finally{_loading=false;}
    }
    private TemplateStudioRow Row(Dictionary<string,string> values)
    {
        var row=new TemplateStudioRow(new(values),(r,key)=>
        {
            Invalidate();if(_document.Kind=="Moves"&&key=="Move"&&int.TryParse(r["Move"],out int id)&&id>0)
            {
                try{if(id>=_project.GameConfiguration.Moves.Length)throw new InvalidDataException("Movimiento fuera del catálogo de este juego.");var v=_project.GameConfiguration.Moves[id];r["Type"]=v.Type.ToString();r["Category"]=v.Category.ToString();r["BattlePatch"]="";r.BattleChoices=BattlePatchCatalogue.For(_project,id);r.Changed(nameof(r.BattleChoices));r.Changed(nameof(r.BattleDescription));}catch(Exception e){Error(e);}
            }
            if(_document.Kind=="Evolutions"&&key=="Method")SetArgumentChoices(r);
            if(key=="BattlePatch")r.Changed(nameof(r.BattleDescription));
        });
        if(_document.Kind=="Evolutions")SetArgumentChoices(row);
        row.BattleChoices=BattlePatchCatalogue.For(_project,int.TryParse(row["Move"],out int move)?move:0);return row;
    }
    private void SetArgumentChoices(TemplateStudioRow row)
    {
        int.TryParse(row["Method"],out int method);
        IReadOnlyList<TemplateChoice>? choices=method switch
        {
            6 or 8 or 17 or 18 or 19 or 20 or 42=>Choices("Item"),
            21=>Choices("Move"),22=>Choices("Source"),29=>Choices("Type"),_=>null
        };
        row.ArgumentChoices=choices is null?[]:choices.Select(c=>c.Value==""?c with{Label="Elegir argumento…"}:c).ToArray();
        if(choices is not null&&!choices.Any(c=>c.Value=="0"))row.ArgumentChoices=row.ArgumentChoices.Append(new TemplateChoice("0","000 · Ninguno")).ToArray();
        if(choices is not null&&!string.IsNullOrEmpty(row["Argument"])&&!row.ArgumentChoices.Any(c=>c.Value==row["Argument"]))
            row.ArgumentChoices=row.ArgumentChoices.Append(new TemplateChoice(row["Argument"],row["Argument"]+" · Valor importado")).ToArray();
        row.Changed(nameof(row.ArgumentChoices));row.Changed(nameof(row.HasArgumentChoices));
    }
    private void EditDescription(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement {DataContext:TemplateStudioRow row})return;
        int id=int.TryParse(row["Move"],out int selected)&&selected>0&&selected<_project.GameConfiguration.Moves.Length?selected:1;
        var native=_project.GameConfiguration.Moves[id];int Value(string key,int fallback)=>int.TryParse(row[key],out int value)?value:fallback;
        int type=Value("Type",native.Type);string typeName=type>=0&&type<_project.Types.Count?_project.Types[type]:type.ToString();
        var example=new MovePreviewExample(_project.MoveNames[id],type,typeName,Value("Category",native.Category),Value("Power",native.Power),Value("Accuracy",native.Accuracy),Value("PP",native.PP));
        var dialog=new TemplateDescriptionWindow(row["InGameDescription"],_project,example){Owner=Window.GetWindow(this)};
        if(dialog.ShowDialog()==true)row["InGameDescription"]=dialog.Description;
    }
    private void AddRow(){if(_document.Kind=="FieldItems")return;Commit();var row=Row(AuthoredTemplate.Columns(_document.Kind,_project.Generation).ToDictionary(k=>k,_=>""));if(_document.Kind=="Shops")row["Stage"]="0";_rows.Add(row);_grid.SelectedItem=row;_grid.ScrollIntoView(row);}
    private IReadOnlyList<TemplateChoice>? Choices(string key)
    {
        TemplateChoice[] Named(IEnumerable<string> names)=>new[]{new TemplateChoice("","Conservar")}.Concat(names.Select((n,i)=>new TemplateChoice(i.ToString(),$"{i:000} · {n}"))).ToArray();
        if(key=="Move")return Named(_project.MoveNames.Take(_project.GameConfiguration.Moves.Length)).Where(c=>c.Value!="0").Select(c=>c.Value==""?c with{Label="Elegir movimiento…"}:c).ToArray();
        if(key is "Source" or "Target" or "Entry")return new[]{new TemplateChoice("","Elegir Pokémon…")}.Concat(_project.Entries.Where(e=>key=="Entry"||e.Index<=_project.GameConfiguration.MaxSpeciesID).Select(e=>new TemplateChoice(e.Index.ToString(),$"{e.Index:000} · {e.Name}"))).ToArray();
        return key switch
        {
            "Type"=>Named(_project.Types),"Category"=>Named(new[]{"Estado","Físico","Especial"}),"Quality"=>Named(MoveEditorCatalog.QualityNames),"Targeting"=>Named(MoveEditorCatalog.TargetNames),"Inflict"=>Named(MoveEditorCatalog.InflictionNames),"UserStat" or "TargetStat" or "Stat1" or "Stat2" or "Stat3"=>Named(MoveEditorCatalog.StatNames),"ZEffect"=>Named(MoveEditorCatalog.ZEffectNames),"ClearFlags" or "ClearStatEffects"=>new[]{new TemplateChoice("false","No"),new("true","Sí")},
            "Item"=>Named(_project.ItemNames).Where(c=>c.Value!="0").Select(c=>c.Value==""?c with{Label="Elegir objeto…"}:c).ToArray(),
            "Method"=>EvolutionMethods(),
            "Stage"=>ShopChoices(),_=>null
        };
    }
    private TemplateChoice[] EvolutionMethods()
    {
        var service=new GameEvolutionService(_project);var field=service.Read(service.Entries[0].Id).SelectMany(g=>g.Fields).First(f=>f.Key=="E0.Method");return field.Choices?.Select(c=>new TemplateChoice(c.Id.ToString(),c.Display)).ToArray()??[];
    }
    private TemplateChoice[] ShopChoices()=>_document.ShopLabels;
    private static DataGridTemplateColumn ChoiceColumn(string label,Binding value,IReadOnlyList<TemplateChoice>? choices,double width,Binding? source=null,Binding? tooltip=null)
    {
        var cell=new FrameworkElementFactory(typeof(SearchableTemplateComboBox));cell.SetResourceReference(StyleProperty,"GridComboBox");
        if(source is not null)cell.SetBinding(ItemsControl.ItemsSourceProperty,source);else cell.SetValue(ItemsControl.ItemsSourceProperty,choices);
        cell.SetBinding(SearchableTemplateComboBox.ChoiceValueProperty,value);
        if(tooltip is not null){tooltip.Converter=OptionalDescriptionConverter.Instance;cell.SetBinding(ToolTipProperty,tooltip);}
        var style=new Style(typeof(ComboBoxItem),(Style)System.Windows.Application.Current.FindResource(typeof(ComboBoxItem)));style.Setters.Add(new Setter(ToolTipProperty,new Binding("Description"){Converter=OptionalDescriptionConverter.Instance}));cell.SetValue(ItemsControl.ItemContainerStyleProperty,style);
        var template=new DataTemplate{VisualTree=cell};return new DataGridTemplateColumn{Header=label,CellTemplate=template,CellEditingTemplate=template,Width=width};
    }
    private void AddColumn(string key)
    {
        var binding=new Binding("["+key+"]"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=true};
        string label=key switch{"Move"=>"Movimiento","Entry"=>"Pokémon / forma","Source"=>"Origen","Target"=>"Destino","Method"=>"Método","Argument"=>"Argumento (ID / valor)","Stage"=>"Disponible desde","Slot"=>"Ranura","Item"=>"Objeto","Price"=>"Precio BP","InGameDescription"=>"InGameDescription · español",_=>key};
        if(key=="BattlePatch")
        {
            _grid.Columns.Add(ChoiceColumn(label,binding,null,220,new Binding("BattleChoices"),new Binding("BattleDescription")));return;
        }
        if(key=="Argument")
        {
            var panel=new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
            var numeric=new FrameworkElementFactory(typeof(TextBox));numeric.SetBinding(TextBox.TextProperty,binding);
            var numericStyle=new Style(typeof(TextBox),(Style)FindResource("GridTextBox"));
            var hide=new DataTrigger{Binding=new Binding("HasArgumentChoices"),Value=true};hide.Setters.Add(new Setter(VisibilityProperty,Visibility.Collapsed));numericStyle.Triggers.Add(hide);numeric.SetValue(StyleProperty,numericStyle);panel.AppendChild(numeric);
            var selector=new FrameworkElementFactory(typeof(SearchableTemplateComboBox));selector.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("ArgumentChoices"));selector.SetBinding(SearchableTemplateComboBox.ChoiceValueProperty,binding);
            var selectorStyle=new Style(typeof(SearchableTemplateComboBox),(Style)FindResource("GridComboBox"));var collapse=new DataTrigger{Binding=new Binding("HasArgumentChoices"),Value=false};collapse.Setters.Add(new Setter(VisibilityProperty,Visibility.Collapsed));selectorStyle.Triggers.Add(collapse);selector.SetValue(StyleProperty,selectorStyle);panel.AppendChild(selector);
            var template=new DataTemplate{VisualTree=panel};_grid.Columns.Add(new DataGridTemplateColumn{Header=label,CellTemplate=template,CellEditingTemplate=template,Width=220});return;
        }
        var choices=Choices(key);if(choices is not null)_grid.Columns.Add(ChoiceColumn(label,binding,choices,key is "Move" or "Entry" or "Source" or "Target" or "Item" or "Stage"?220:160));
        else if(key=="InGameDescription")
        {
            var edit=new FrameworkElementFactory(typeof(Button));edit.SetBinding(ContentControl.ContentProperty,new Binding("[InGameDescription]"){Converter=DescriptionPreviewConverter.Instance});edit.SetValue(ToolTipProperty,"Abrir el editor ampliado de descripción");edit.SetResourceReference(StyleProperty,"GridButton");edit.SetValue(HorizontalContentAlignmentProperty,System.Windows.HorizontalAlignment.Left);edit.AddHandler(Button.ClickEvent,new RoutedEventHandler(EditDescription));
            _grid.Columns.Add(new DataGridTemplateColumn{Header=label,CellTemplate=new DataTemplate{VisualTree=edit},Width=300});
        }
        else _grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=binding,Width=key=="Notes"?250:110});
    }
    private void Import(){var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Templates (*.json;*.csv)|*.json;*.csv",Title="Importar "+_kind.Text};if(dialog.ShowDialog(Window.GetWindow(this))==true){string kind=CurrentKind,path=dialog.FileName;_=LoadAsync(()=>AuthoredTemplate.Import(_project,kind,path));}}
    private void Export(){var d=Capture();d.ValidateShape(_project);var dialog=new Microsoft.Win32.SaveFileDialog{FileName="template-"+d.Kind.ToLowerInvariant(),Filter=d.Kind is "Moves" or "Evolutions" or "Stats"?"Template CSV|*.csv|Template JSON|*.json":"Template JSON|*.json",AddExtension=true};if(dialog.ShowDialog(Window.GetWindow(this))==true){d.Export(dialog.FileName);_status.Text="Template exportado: "+dialog.FileName;}}
    private async Task Execute(bool apply)
    {
        if(_busy)return;AuthoredTemplate d;try{d=Capture();}catch(Exception e){Error(e);return;}
        string snapshot=d.Serialize();if(apply&&_validated!=snapshot){Invalidate();_status.Text="Comprobá otra vez el template: el borrador cambió.";return;}
        _busy=true;IsEnabled=false;using var interaction=UiProgress.BlockInteraction();UiProgress.Begin(apply?"Aplicando template y registrando historial…":"Comprobando template…");
        try
        {
            if(!apply)_seed=System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue);
            int count=await Task.Run(()=>
            {
                if(!apply)return d.Run(_project,_seed,true);
                var payload=AuthoredTemplate.Store(_project,d);payload["Seed"]=_seed;int changes=d.Run(_project,_seed,false,payload);_project.History.RecordTemplateUse(payload);return changes;
            });
            _preview.ItemsSource=d.LastChanges;
            _status.Text=apply?$"Template aplicado y registrado · {count} cambios. Global Template incluirá el archivo y Batch lo reproducirá al final.":$"Comprobación correcta · {count} cambios. Revisá la pestaña Cambios previstos y pulsá Usar template para aplicarlos.";
            _validated=apply?null:snapshot;_use.IsEnabled=!apply;
        }
        catch(Exception e){Invalidate();Error(e);}
        finally{_busy=false;IsEnabled=true;UiProgress.End();}
    }
}
