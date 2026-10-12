using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using UserControl=System.Windows.Controls.UserControl;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using CheckBox=System.Windows.Controls.CheckBox;
using Brush=System.Windows.Media.Brush;

namespace ThreeDS.Progressive;

public partial class RecordEditorView : UserControl
{
    internal sealed record UiState(IReadOnlyDictionary<string,int> Options,int Scope,string? Template=null);
    internal IProjectEditor Editor {get;}
    private readonly Dictionary<string,FrameworkElement> _fields=[],_options=[];
    private bool _loading,_dirty;
    private int _id=-1;
    private EditorProposal? _proposal;
    private bool _templateMode;
    internal IReadOnlyDictionary<string,FrameworkElement> FieldControls=>_fields;
    internal IReadOnlyDictionary<string,FrameworkElement> OptionControls=>_options;
    public RecordEditorView(IProjectEditor editor) : this(editor,null) { }
    internal RecordEditorView(IProjectEditor editor,UiState? state)
    {
        Editor=editor;InitializeComponent();TitleText.Text=editor.Title;RandomizerButton.Content="Randomizar "+editor.Title.ToLowerInvariant();
        if(editor is ITemplateProjectEditor template){TemplateButton.Visibility=Visibility.Visible;TemplateButton.Content=template.TemplateButton;TemplatePathText.Text=template.DefaultTemplatePath;}
        if(editor is IOptionTemplateProjectEditor optionTemplate)
        {ImportOptionsButton.Visibility=ExportOptionsButton.Visibility=Visibility.Visible;if(state?.Template is { } saved)optionTemplate.RestoreOptions(saved);}
        RecordList.ItemsSource=editor.Entries;CountText.Text=editor.Entries.Count+" entradas";
        _loading=true;
        var groups=editor.RandomOptions.Select(g=>g with {Fields=g.Fields.Select(f=>
            state is not null&&state.Options.TryGetValue(f.Key,out int value)&&value>=f.Min&&value<=f.Max
                ?f with {Value=value}:f).ToArray()});
        BuildGroups(OptionsPanel,groups,_options,false);
        if(state is not null)ScopeBox.SelectedIndex=Math.Clamp(state.Scope,0,2);
        _loading=false;
        RecordList.SelectedIndex=0;
    }
    internal UiState RememberOptions()
    {
        Dictionary<string,int> options;
        try {options=Values(_options);}catch(ArgumentException){return new(new Dictionary<string,int>(),ScopeBox.SelectedIndex);}
        string? template=null;
        if(Editor is IOptionTemplateProjectEditor editor)
        {try{template=editor.CaptureOptions(options);}catch(ArgumentException){ }}
        return new(options,ScopeBox.SelectedIndex,template);
    }
    private void BuildGroups(StackPanel panel,IEnumerable<EditorGroup> groups,Dictionary<string,FrameworkElement> controls,bool editor)
    {
        panel.Children.Clear();controls.Clear();
        foreach(var group in groups)
        {
            var grid=new UniformGrid {Columns=2,Margin=new Thickness(0,12,0,0)};
            foreach(var field in group.Fields)
            {
                var cell=new StackPanel {Margin=new Thickness(0,0,12,15)};
                cell.Children.Add(new TextBlock {Text=field.Label,TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),FontSize=12,Margin=new Thickness(0,0,0,6)});
                FrameworkElement input;
                if(field.Choices is { } choices)
                {
                    if(!choices.Any(c=>c.Id==field.Value))choices=choices.Append(new MoveChoice(field.Value,$"ID {field.Value} (personalizado)")).ToArray();
                    var combo=new ComboBox {ItemsSource=choices,DisplayMemberPath="Display",SelectedValuePath="Id",SelectedValue=field.Value,MaxDropDownHeight=300};
                    combo.SelectionChanged+=Input_Changed;input=combo;
                }
                else if(field.Min==0&&field.Max==1)
                {
                    var check=new CheckBox {IsChecked=field.Value==1,Content="Activado",Margin=new Thickness(0,7,0,7)};
                    check.Checked+=Input_Changed;check.Unchecked+=Input_Changed;input=check;
                }
                else
                {
                    var text=new TextBox {Text=(field.Value/(decimal)field.Scale).ToString(CultureInfo.InvariantCulture)};text.TextChanged+=Input_Changed;input=text;
                }
                input.Tag=editor;input.Uid=field.Scale.ToString(CultureInfo.InvariantCulture);controls.Add(field.Key,input);cell.Children.Add(input);grid.Children.Add(cell);
            }
            var expand=new Expander {Header=group.Name,IsExpanded=group.Expanded,Foreground=(Brush)FindResource("TextMain"),Content=grid};
            panel.Children.Add(new Border {Background=(Brush)FindResource("Panel"),BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(16),Margin=new Thickness(0,0,0,14),Child=expand});
        }
    }
    private static Dictionary<string,int> Values(Dictionary<string,FrameworkElement> controls)=>controls.ToDictionary(p=>p.Key,p=>p.Value switch
    {
        ComboBox {SelectedValue:int v}=>v,CheckBox check=>check.IsChecked==true?1:0,
        TextBox text when decimal.TryParse(text.Text.Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out decimal v)=>checked((int)(v*int.Parse(text.Uid))),
        _=>throw new ArgumentException("Hay un campo que no contiene un número válido.")
    });
    private void Input_Changed(object sender,RoutedEventArgs e)
    {
        if(_loading)return;Invalidate();if(sender is FrameworkElement {Tag:true}){_dirty=true;SaveButton.IsEnabled=true;StatusText.Text="Cambios sin guardar.";}
    }
    private void Invalidate(){_proposal=null;if(ApplyButton is not null)ApplyButton.IsEnabled=false;}
    private void Scope_Changed(object sender,SelectionChangedEventArgs e)=>Invalidate();
    private void Show(int id)
    {
        _loading=true;
        try
        {
            if(Editor is ThreeDS.Progressive.Services.GameTrainerService trainer)
            {
                var state=_options.Count>0?RememberOptions():null;
                trainer.SelectOptionsEntry(id);
                var groups=Editor.RandomOptions.Select(g=>g with {Fields=g.Fields.Select(f=>state is not null&&state.Options.TryGetValue(f.Key,out int value)?f with {Value=value}:f).ToArray()});
                BuildGroups(OptionsPanel,groups,_options,false);
            }
            _id=id;BuildGroups(FieldsPanel,Editor.Read(id),_fields,true);SelectionText.Text=Editor.Entries.First(e=>e.Id==id).Display;_dirty=false;SaveButton.IsEnabled=false;StatusText.Text="Editor listo.";Invalidate();
        }
        finally{_loading=false;}
    }
    private void Selection_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||RecordList.SelectedItem is not RecordEntry entry)return;
        if(!ConfirmLeave())
        {_loading=true;RecordList.SelectedItem=Editor.Entries.FirstOrDefault(x=>x.Id==_id);_loading=false;return;}
        try{Show(entry.Id);}
        catch(Exception ex){StatusText.Text=ex.Message;}
    }
    private void Search_Changed(object sender,TextChangedEventArgs e)
    {
        if(_loading||RecordList?.ItemsSource is null)return;
        if(_dirty&&!ConfirmLeave()){_loading=true;SearchText.Text="";_loading=false;return;}
        Invalidate();string text=SearchText.Text.Trim();
        CollectionViewSource.GetDefaultView(RecordList.ItemsSource).Filter=o=>o is RecordEntry entry&&
            (entry.Name.Contains(text,StringComparison.OrdinalIgnoreCase)||entry.Id.ToString().Contains(text));
    }
    public bool ConfirmLeave()=>!_dirty||StudioDialog.Confirm(Window.GetWindow(this),"Cambios pendientes","Hay modificaciones sin guardar.","Si continuás, se descartará la edición actual.");
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        if(_id<0)return;
        try
        {
            var values=Values(_fields);
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Confirmar guardado","Se guardará la entrada seleccionada y su historial."))return;
            Editor.Save(_id,values);Show(_id);StatusText.Text="Guardado y verificado.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo guardar",ex.Message);}
    }
    private void Discard_Click(object sender,RoutedEventArgs e){if(_id>=0)Show(_id);}
    private void Randomizer_Click(object sender,RoutedEventArgs e)
    {
        if(!ConfirmLeave())return;if(_dirty&&_id>=0)Show(_id);Invalidate();
        _templateMode=false;ToolTitle.Text="Randomizar";TemplatePanel.Visibility=Visibility.Collapsed;OptionsPanel.Visibility=Visibility.Visible;
        FieldsScroll.Visibility=Visibility.Collapsed;ToolsPanel.Visibility=Visibility.Visible;
        SaveButton.IsEnabled=false;ProposalText.Text="Configurá las opciones y generá una vista previa.";ChangeList.ItemsSource=null;
    }
    private void Back_Click(object sender,RoutedEventArgs e)
    {Invalidate();ToolsPanel.Visibility=Visibility.Collapsed;FieldsScroll.Visibility=Visibility.Visible;if(_id>=0)Show(_id);}
    private void Preview_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            int[] scope=ScopeBox.SelectedIndex switch {1=>RecordList.Items.Cast<RecordEntry>().Select(e=>e.Id).ToArray(),2=>_id>=0?[_id]:[],_=>Editor.Entries.Select(e=>e.Id).ToArray()};
            _proposal=_templateMode&&Editor is ITemplateProjectEditor template?template.PreviewTemplate(TemplatePathText.Text,scope):Editor.Preview(Values(_options),scope);ChangeList.ItemsSource=_proposal.Changes;
            ProposalText.Text=$"{_proposal.Changes.Count} cambios"+(_proposal.Seed==0?"":$" · semilla {_proposal.Seed}")+". Revisá la propuesta antes de aplicar.";
            ApplyButton.IsEnabled=_proposal.Changes.Count>0;
        }
        catch(Exception ex){Invalidate();ProposalText.Text=ex.Message;}
    }
    private void Template_Click(object sender,RoutedEventArgs e)
    {
        if(!ConfirmLeave())return;if(_dirty&&_id>=0)Show(_id);Invalidate();_templateMode=true;
        ToolTitle.Text="Template de evoluciones";TemplatePanel.Visibility=Visibility.Visible;OptionsPanel.Visibility=Visibility.Collapsed;
        FieldsScroll.Visibility=Visibility.Collapsed;ToolsPanel.Visibility=Visibility.Visible;SaveButton.IsEnabled=false;ChangeList.ItemsSource=null;
        ProposalText.Text="Se conservarán las evoluciones originales y se añadirán o actualizarán las rutas del CSV, como en pk3DS Progressive.";
    }
    private void ChooseTemplate_Click(object sender,RoutedEventArgs e)
    {var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Template de evoluciones|*.csv"};if(dialog.ShowDialog(Window.GetWindow(this))==true){TemplatePathText.Text=dialog.FileName;Invalidate();}}
    private void DefaultTemplate_Click(object sender,RoutedEventArgs e)
    {if(Editor is ITemplateProjectEditor template){TemplatePathText.Text=template.DefaultTemplatePath;Invalidate();}}
    private void ImportOptions_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not IOptionTemplateProjectEditor editor)return;
        var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Template de entrenadores Progressive|*.json"};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{editor.ImportOptions(dialog.FileName);_loading=true;BuildGroups(OptionsPanel,Editor.RandomOptions,_options,false);_loading=false;Randomizer_Click(sender,e);}
        catch(Exception ex){_loading=false;StudioDialog.Error(Window.GetWindow(this),"No se pudo cargar el template",ex.Message);}
    }
    private void ExportOptions_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not IOptionTemplateProjectEditor editor)return;
        var dialog=new Microsoft.Win32.SaveFileDialog {Filter="Template de entrenadores Progressive|*.json",FileName="trainer-template.json"};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{editor.ExportOptions(dialog.FileName,Values(_options));StudioDialog.Notice(Window.GetWindow(this),"Template exportado","Se conservaron las opciones, rangos y reglas de entrenadores.");}
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo exportar",ex.Message);}
    }
    private void Apply_Click(object sender,RoutedEventArgs e)
    {
        var proposal=_proposal;if(proposal is null)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Aplicar propuesta",$"Se guardarán {proposal.Changes.Count} cambios en {Editor.Title}."))return;
        try{Editor.Apply(proposal);Invalidate();ProposalText.Text="Propuesta guardada y verificada. El historial quedó actualizado.";}
        catch(Exception ex){Invalidate();ProposalText.Text=ex.Message;}
    }
}
