using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation;
using ThreeDS.Progressive.Models;
using Binding=System.Windows.Data.Binding;
using DataGrid=System.Windows.Controls.DataGrid;
using Panel=System.Windows.Controls.Panel;
using TextBlock=System.Windows.Controls.TextBlock;
namespace ThreeDS.Progressive;
public partial class RecordEditorView
{
    internal DataGrid? ActiveRecordTable {get;private set;}
    // Reparent the actual validated editor controls: no duplicate values, handlers or save path.
    private void BuildRecordTable(StackPanel host,IReadOnlyList<EditorGroup> groups,Dictionary<string,FrameworkElement> controls)
    {
        var grid=new DataGrid {Name="RecordTable",AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,FrozenColumnCount=1,EnableRowVirtualization=true,EnableColumnVirtualization=true,MinHeight=220,MaxHeight=560,RowHeight=double.NaN,HeadersVisibility=DataGridHeadersVisibility.Column};
        ActiveRecordTable=grid;DarkGridTheme.Apply(grid);AutomationProperties.SetName(grid,Editor.Title+" · tabla editable");
        var rows=new List<Dictionary<string,object>>();
        var labels=groups.SelectMany(g=>g.Fields).Select(f=>f.Label).Distinct().ToArray();
        grid.Columns.Add(new DataGridTextColumn {Header="Ranura",Binding=new Binding("[Ranura]"),IsReadOnly=true,Width=80,MinWidth=80});
        for(int i=0;i<labels.Length;i++)
        {
            var factory=new FrameworkElementFactory(typeof(ContentControl));factory.SetBinding(ContentControl.ContentProperty,new Binding("[C"+i+"]"));
            grid.Columns.Add(new DataGridTemplateColumn {Header=labels[i],CellTemplate=new DataTemplate {VisualTree=factory},Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=110});
        }
        for(int n=0;n<groups.Count;n++)
        {
            var row=new Dictionary<string,object>{{"Ranura",groups[n].Name}};for(int i=0;i<labels.Length;i++)row["C"+i]=null!;
            foreach(var field in groups[n].Fields)
            {
                var control=controls[field.Key];if(control.Parent is Panel parent)parent.Children.Remove(control);
                if(control is System.Windows.Controls.CheckBox checkbox)checkbox.Content=field.Label switch {"MT"=>"MT"+n.ToString("00"),"TR"=>"TR"+n.ToString("00"),"Tutor"=>"Tutor "+(n+1).ToString("00"),"Tutor Armadura"=>"Armadura "+(n+1).ToString("00"),_=>null};AutomationProperties.SetName(control,field.Label);control.Margin=new Thickness(3);control.MinWidth=65;control.ToolTip=field.Label+" · "+field.Min+"–"+field.Max;
                if(field.Label=="Método"&&control is System.Windows.Controls.ComboBox method)method.SelectionChanged+=(_,_)=>RefreshContextualRecord();row["C"+Array.IndexOf(labels,field.Label)]=control;
            }
            rows.Add(row);
        }
        grid.FrozenColumnCount=1;grid.ItemsSource=rows;host.Children.Clear();host.Children.Add(new TextBlock {Text="Edita las celdas y pulsa Guardar en el proyecto. Los cambios no se aplican al escribir.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});host.Children.Add(grid);
    }
    private void RefreshContextualRecord()
    {
        if(_loading||_id<0||Editor is not IContextualRecordEditor context)return;
        try {var values=Values(_fields);_loading=true;BuildGroups(FieldsPanel,context.ReadContext(_id,values),_fields,true);_dirty=true;SaveButton.IsEnabled=true;Invalidate();StatusText.Text="Cambios sin guardar.";}
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Argumento evolutivo",ex.Message);}finally{_loading=false;}
    }
    internal DataGrid? ActiveTrainerRuleGrid {get;private set;}
    private void OpenSwitchTrainerRules(bool caps)
    {
        if(!caps)return;
        if(!ConfirmLeave())return;
        if(ToolsPanel.Visibility!=Visibility.Visible)Randomizer_Click(this,new RoutedEventArgs());
        var groups=Editor.RandomOptions.Select(g=>g with {Fields=g.Fields.Where(f=>f.Key.StartsWith("Rule.")&&(caps?f.Key.EndsWith(".CapUse")||f.Key.EndsWith(".LevelCap"):!f.Key.EndsWith(".CapUse")&&!f.Key.EndsWith(".LevelCap"))).ToArray()}).Where(g=>g.Fields.Count>0).ToArray();
        var initial=OptionValues().Where(p=>p.Key.StartsWith("Rule.")).ToDictionary(p=>p.Key,p=>p.Value);
        var rows=new List<Dictionary<string,object>>();
        var grid=new DataGrid {AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,FrozenColumnCount=1,MinHeight=250,MaxHeight=540,EnableRowVirtualization=true};ActiveTrainerRuleGrid=grid;DarkGridTheme.Apply(grid);
        grid.Columns.Add(new DataGridTextColumn {Header="ID",Binding=new Binding("[Id]"),IsReadOnly=true,Width=70,MinWidth=60});
        grid.Columns.Add(new DataGridTextColumn {Header="Grupo",Binding=new Binding("[Group]"),IsReadOnly=true,Width=230,MinWidth=100});
        grid.Columns.Add(new DataGridTextColumn {Header="Entrenador",Binding=new Binding("[Name]"),IsReadOnly=true,Width=150,MinWidth=100});
        grid.Columns.Add(new DataGridTextColumn {Header="Ace actual",Binding=new Binding("[Ace]"),IsReadOnly=true,Width=90,MinWidth=75});
        var labels=groups.SelectMany(g=>g.Fields).Select(f=>f.Label).Distinct().ToArray();
        for(int i=0;i<labels.Length;i++){var factory=new FrameworkElementFactory(typeof(ContentControl));factory.SetBinding(ContentControl.ContentProperty,new Binding("[C"+i+"]"));grid.Columns.Add(new DataGridTemplateColumn {Header=labels[i],CellTemplate=new DataTemplate {VisualTree=factory},Width=140});}
        // Use separate controls bound to the originals so opening a table never reparents option panels.
        foreach(var group in groups){int id=int.Parse(group.Fields[0].Key.Split('.')[1],System.Globalization.CultureInfo.InvariantCulture);var identity=((ITrainerTableMetadata)Editor).TrainerIdentity(id);var row=new Dictionary<string,object>{{"Id",id},{"Group",identity.Group},{"Name",identity.Name},{"Ace",identity.Ace}};foreach(var field in group.Fields){if(!_options.TryGetValue(field.Key,out var original))continue;FrameworkElement control;
            if(original is System.Windows.Controls.CheckBox check){var copy=new System.Windows.Controls.CheckBox();copy.SetBinding(System.Windows.Controls.CheckBox.IsCheckedProperty,new Binding("IsChecked"){Source=check,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});control=copy;}
            else {var copy=new System.Windows.Controls.TextBox();copy.SetBinding(System.Windows.Controls.TextBox.TextProperty,new Binding("Text"){Source=original,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});control=copy;}
            System.Windows.Automation.AutomationProperties.SetName(control,group.Name+" · "+field.Label);if(field.Label=="Método"&&control is System.Windows.Controls.ComboBox method)method.SelectionChanged+=(_,_)=>RefreshContextualRecord();row["C"+Array.IndexOf(labels,field.Label)]=control;}rows.Add(row);}
        grid.FrozenColumnCount=2;grid.ItemsSource=rows;
        var root=new DockPanel {Margin=new Thickness(16)};var info=new TextBlock {Text="Los cambios configuran la siguiente vista previa; todavía no modifican los equipos. Líderes y otros entrenadores importantes aparecen por separado. Los niveles proceden de tu extracción.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(info,Dock.Top);root.Children.Add(info);
        var actions=new WrapPanel {Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var close=new System.Windows.Controls.Button {Content="Aceptar"};actions.Children.Add(close);
        var cancel=new System.Windows.Controls.Button {Content="Cancelar",Margin=new Thickness(8,0,0,0)};actions.Children.Add(cancel);
        if(Editor is ThreeDS.Progressive.Services.Switch.SwordShieldRecordEditor sw&&sw.CapPreset.Count>0)
        {
            var preset=new System.Windows.Controls.Button{Name="DivineSwordPresetButton",Content="DivineSword",Margin=new Thickness(8,0,0,0)};EditorPresentation.StyleAction(preset,EditorPresentation.ActionRole.Secondary);AutomationProperties.SetName(preset,"DivineSword · cargar los level caps seleccionados");actions.Children.Add(preset);
            preset.Click+=(_,_)=>{foreach(var cap in sw.CapPreset){if(_options.TryGetValue("Rule."+cap.Key+".CapUse",out var enabled)&&enabled is System.Windows.Controls.CheckBox flag)flag.IsChecked=true;if(_options.TryGetValue("Rule."+cap.Key+".LevelCap",out var level)&&level is System.Windows.Controls.TextBox text)text.Text=cap.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);}Invalidate();};
        }
        close.Click+=(_,_)=>{try {EditorValues.Validate(Editor.RandomOptions.Select(g=>g with{Fields=g.Fields.Where(f=>!f.Key.StartsWith("BST.")).ToArray()}),OptionValues());CloseTrainerRules();(caps?TrainerCapsButton:TrainerMovesButton).Focus();}catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Reglas inválidas",ex.Message);}};
        cancel.Click+=(_,_)=>{foreach(var value in initial){if(_options[value.Key] is System.Windows.Controls.CheckBox check)check.IsChecked=value.Value==1;else if(_options[value.Key] is System.Windows.Controls.TextBox text)text.Text=value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);}CloseTrainerRules();(caps?TrainerCapsButton:TrainerMovesButton).Focus();};
        var filters=new WrapPanel {Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(filters,Dock.Top);root.Children.Add(filters);
        var important=new System.Windows.Controls.CheckBox {Content="Entrenadores importantes",IsChecked=true,Margin=new Thickness(0,0,16,0)};var bosses=new System.Windows.Controls.CheckBox {Content="Líderes de gimnasio",IsChecked=true};filters.Children.Add(important);filters.Children.Add(bosses);
        var view=CollectionViewSource.GetDefaultView(rows);view.Filter=o=>o is Dictionary<string,object> row&&(((string)row["Group"]).StartsWith("Jefes")?bosses.IsChecked==true:important.IsChecked==true);grid.ItemsSource=view;
        void SelectVisible(bool enabled){foreach(var row in view.Cast<Dictionary<string,object>>()){string key="Rule."+row["Id"]+(caps?".CapUse":".MoveUse");if(_options.TryGetValue(key,out var control)&&control is System.Windows.Controls.CheckBox check)check.IsChecked=enabled;}}
        var select=new System.Windows.Controls.Button {Content="Seleccionar visibles",Margin=new Thickness(12,0,0,0)};var deselect=new System.Windows.Controls.Button {Content="Deseleccionar visibles",Margin=new Thickness(8,0,0,0)};select.Click+=(_,_)=>SelectVisible(true);deselect.Click+=(_,_)=>SelectVisible(false);filters.Children.Add(select);filters.Children.Add(deselect);
        important.Checked+=(_,_)=>view.Refresh();important.Unchecked+=(_,_)=>view.Refresh();bosses.Checked+=(_,_)=>view.Refresh();bosses.Unchecked+=(_,_)=>view.Refresh();root.Children.Add(grid);
        var heading=new TextBlock {Text=caps?"Tabla de Level Caps":"Tabla de Trainer Move Rules",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(heading,Dock.Top);root.Children.Insert(0,heading);
        var surface=new Border {Child=root};surface.SetResourceReference(Border.BackgroundProperty,"Bg");TrainerRulesHost.Content=surface;TrainerRulesHost.Visibility=Visibility.Visible;
    }
}
