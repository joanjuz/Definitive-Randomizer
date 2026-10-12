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
using Image=System.Windows.Controls.Image;
using Button=System.Windows.Controls.Button;
using TabControl=System.Windows.Controls.TabControl;
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
        if(editor is ThreeDS.Progressive.Services.GameTrainerService)
            TrainerCapsButton.Visibility=TrainerMovesButton.Visibility=Visibility.Visible;
        if(editor is ITemplateProjectEditor template){TemplateButton.Visibility=Visibility.Visible;TemplateButton.Content=template.TemplateButton;TemplatePathText.Text=template.DefaultTemplatePath;}
        if(editor is IOptionTemplateProjectEditor optionTemplate)
        {ImportOptionsButton.Visibility=ExportOptionsButton.Visibility=Visibility.Visible;if(state?.Template is { } saved)optionTemplate.RestoreOptions(saved);}
        RecordList.ItemsSource=editor.Entries;CountText.Text=editor.Entries.Count+" entradas";
        // Cargar las reglas del entrenador que realmente estará seleccionado, no las del ID por defecto del host.
        if(editor is ThreeDS.Progressive.Services.GameTrainerService trainers&&editor.Entries.Count>0)
            trainers.SelectOptionsEntry(editor.Entries[0].Id);
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
    // El editor individual conserva su estructura. El randomizador de entrenadores
    // organiza cada control vanilla/Progressive por función y no por el orden
    // accidental del formulario WinForms original.
    private void BuildGroups(StackPanel panel,IEnumerable<EditorGroup> groups,Dictionary<string,FrameworkElement> controls,bool editor)
    {
        panel.Children.Clear();controls.Clear();
        if(editor){_editorSections.Clear();_pokemonTabs.Clear();}
        var entries=groups.ToArray();
        if(!editor&&Editor is ThreeDS.Progressive.Services.GameTrainerService)
        {
            BuildTrainerTabs(panel,entries,controls);
            return;
        }
        foreach(var group in entries)
            panel.Children.Add(editor&&Editor is ThreeDS.Progressive.Services.GameTrainerService
                &&group.Name.StartsWith("Pokémon ",StringComparison.Ordinal)
                    ?BuildTrainerPokemonCard(group,controls)
                    :BuildGroupCard(group,controls,editor));
    }
    private readonly Dictionary<string,Expander> _editorSections=[];
    private int _trainerTabIndex;
    private static readonly string[] TrainerTabNames=
        ["Pokémon y equipos","Niveles y Level Caps","Movimientos y reglas","IA, objetos y combates","BST progresivo"];

    private void BuildTrainerTabs(StackPanel host,IReadOnlyList<EditorGroup> groups,Dictionary<string,FrameworkElement> controls)
    {
        var tabs=new TabControl {Margin=new Thickness(0,0,0,12)};
        for(int category=0;category<TrainerTabNames.Length;category++)
        {
            var container=new StackPanel();
            foreach(var group in groups)
            {
                var selected=group.Fields.Where(f=>TrainerOptionCategory(f.Key)==category).ToArray();
                if(selected.Length==0)continue;
                string label=group.Name is "Main" or "Opciones generales"
                    ?TrainerTabNames[category]:group.Name;
                container.Children.Add(BuildGroupCard(group with
                {Name=label,Fields=selected,Expanded=category==4||group.Expanded},controls,false));
            }
            if(container.Children.Count==0)
                container.Children.Add(new TextBlock {Text="No hay opciones adicionales para esta versión del juego.",
                    Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8,16,8,16)});
            tabs.Items.Add(new TabItem {Header=TrainerTabNames[category],Content=container});
        }
        tabs.SelectionChanged+=(_,e)=>
        {
            if(ReferenceEquals(e.Source,tabs))_trainerTabIndex=tabs.SelectedIndex;
        };
        tabs.SelectedIndex=Math.Clamp(_trainerTabIndex,0,tabs.Items.Count-1);
        host.Children.Add(tabs);
    }

    internal static int TrainerOptionCategory(string key)
    {
        if(key.StartsWith("BST.",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("ProgressiveBST",StringComparison.OrdinalIgnoreCase)
            ||key=="CHK_BST")return 4;
        if(key.StartsWith("Rule.",StringComparison.OrdinalIgnoreCase))
        {
            var name=key[(key.LastIndexOf('.')+1)..];
            if(name is "CapUse" or "LevelCap")return 1;
            if(name is "MoveUse" or "Power" or "Strong" or "Tolerance" or "Status" or "Better" or "EV")return 2;
            if(name is "Mega" or "ZMove" or "Smart")return 3;
            return 0;
        }
        if(key.Contains("Level",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Cap",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Curve",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("PreviousTrainer",StringComparison.OrdinalIgnoreCase)
            ||key=="CHK_L")return 1;
        if(key.Contains("Move",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("STAB",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Damage",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("StrongStat",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Fixed",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Power",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Metronome",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Tolerance",StringComparison.OrdinalIgnoreCase)
            ||key=="CB_Moves")return 2;
        if(key.Contains("Item",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Class",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("AI",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Double",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Mega",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Shiny",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Ability",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Money",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Gift",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Gym",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("ZMove",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("Battle",StringComparison.OrdinalIgnoreCase)
            ||key.Contains("TypeTheme",StringComparison.OrdinalIgnoreCase))return 3;
        return 0;
    }
    private readonly Dictionary<int,TabControl> _pokemonTabs=[];

    private Border BuildTrainerPokemonCard(EditorGroup group,Dictionary<string,FrameworkElement> controls)
    {
        var tabs=new TabControl {Margin=new Thickness(0,8,0,0)};
        string[] labels=["Principal","IV y EV","Movimientos"];
        for(int i=0;i<labels.Length;i++)
        {
            int section=i;
            var fields=group.Fields.Where(f=>TrainerPokemonSection(f.Key)==section).ToArray();
            tabs.Items.Add(new TabItem {Header=labels[i],Content=BuildFieldsGrid(fields,controls,true)});
        }
        tabs.SelectedIndex=0;
        int slot=group.Fields.FirstOrDefault()?.Key is { } key&&key.StartsWith("P",StringComparison.Ordinal)
            &&key.IndexOf('.')>1&&int.TryParse(key[1..key.IndexOf('.')],out int index)?index:-1;
        if(slot>=0)_pokemonTabs[slot]=tabs;
        var expand=new Expander {Header=group.Name,IsExpanded=group.Expanded,
            Foreground=(Brush)FindResource("TextMain"),Content=tabs};
        foreach(var field in group.Fields)_editorSections[field.Key]=expand;
        return GroupBorder(expand);
    }
    internal static int TrainerPokemonSection(string key)
    {
        string name=key[(key.LastIndexOf('.')+1)..];
        if(name.StartsWith("Move",StringComparison.OrdinalIgnoreCase))return 2;
        if(name.StartsWith("IV",StringComparison.OrdinalIgnoreCase)
            ||name.StartsWith("EV",StringComparison.OrdinalIgnoreCase))return 1;
        return 0;
    }
    private Border BuildGroupCard(EditorGroup group,Dictionary<string,FrameworkElement> controls,bool editor)
    {
        var grid=BuildFieldsGrid(group.Fields,controls,editor);
        var expand=new Expander {Header=group.Name,IsExpanded=group.Expanded,
            Foreground=(Brush)FindResource("TextMain"),Content=grid};
        if(editor)foreach(var field in group.Fields)_editorSections[field.Key]=expand;
        return GroupBorder(expand);
    }
    private Border GroupBorder(Expander expand)=>new()
    {
        Background=(Brush)FindResource("Panel"),BorderBrush=(Brush)FindResource("Stroke"),
        BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),
        Padding=new Thickness(16),Margin=new Thickness(0,0,0,14),Child=expand
    };
    private UniformGrid BuildFieldsGrid(IReadOnlyList<EditorField> fields,
        Dictionary<string,FrameworkElement> controls,bool editor)
    {
        var grid=new UniformGrid {Columns=2,Margin=new Thickness(0,12,0,0)};
        foreach(var field in fields)
        {
            var cell=new StackPanel {Margin=new Thickness(0,0,12,15)};
            bool isCheck=field.Choices is null&&field.Min==0&&field.Max==1;
            if(!isCheck)
                cell.Children.Add(new TextBlock {Text=field.Label,TextWrapping=TextWrapping.Wrap,
                    Foreground=(Brush)FindResource("TextSoft"),FontSize=12,Margin=new Thickness(0,0,0,6)});
            FrameworkElement input;
            if(field.Choices is { } choices)
            {
                if(!choices.Any(c=>c.Id==field.Value))
                    choices=choices.Append(new MoveChoice(field.Value,$"ID {field.Value} (personalizado)")).ToArray();
                var combo=new ComboBox {ItemsSource=choices,DisplayMemberPath="Display",SelectedValuePath="Id",
                    SelectedValue=field.Value,MaxDropDownHeight=300};
                combo.SelectionChanged+=Input_Changed;input=combo;
            }
            else if(isCheck)
            {
                var check=new CheckBox {IsChecked=field.Value==1,Content=field.Label,Margin=new Thickness(0,7,0,7)};
                check.Checked+=Input_Changed;check.Unchecked+=Input_Changed;input=check;
            }
            else
            {
                var edit=new TextBox {Text=(field.Value/(decimal)field.Scale).ToString(CultureInfo.InvariantCulture)};
                edit.TextChanged+=Input_Changed;input=edit;
            }
            input.Tag=editor;input.Uid=field.Scale.ToString(CultureInfo.InvariantCulture);
            controls.Add(field.Key,input);cell.Children.Add(input);
            if(editor&&Editor is ThreeDS.Progressive.Services.GameTrainerService
                &&field.Key=="AI"&&input is TextBox ai)
                AddTrainerAiBits(cell,ai);
            grid.Children.Add(cell);
        }
        return grid;
    }
    private void AddTrainerAiBits(StackPanel cell,TextBox value)
    {
        var flags=new WrapPanel {Margin=new Thickness(0,9,0,0)};
        string[] names=["Básica","Fuerte","Experta","Dobles","Sin derrota","Battle Royale","Cambio Pokémon","Objetos"];
        var inputs=new List<CheckBox>();
        bool updating=false;
        void FromNumber()
        {
            if(updating||!int.TryParse(value.Text,out int number)||number is <0 or >255)return;
            updating=true;
            for(int bit=0;bit<inputs.Count;bit++)inputs[bit].IsChecked=(number&(1<<bit))!=0;
            updating=false;
        }
        void ToNumber()
        {
            if(updating)return;
            updating=true;
            int number=0;
            for(int bit=0;bit<inputs.Count;bit++)if(inputs[bit].IsChecked==true)number|=1<<bit;
            value.Text=number.ToString(CultureInfo.InvariantCulture);
            updating=false;
        }
        for(int bit=0;bit<names.Length;bit++)
        {
            var check=new CheckBox {Content=names[bit],Margin=new Thickness(0,3,12,3)};
            inputs.Add(check);flags.Children.Add(check);
            check.Checked+=(_,_)=>ToNumber();check.Unchecked+=(_,_)=>ToNumber();
        }
        value.TextChanged+=(_,_)=>FromNumber();
        FromNumber();
        cell.Children.Add(new TextBlock {Text="Habilidades de IA",Foreground=(Brush)FindResource("TextSoft"),
            FontSize=11,Margin=new Thickness(0,8,0,0)});
        cell.Children.Add(flags);
    }

    private readonly ThreeDS.Progressive.Services.SpriteService _trainerSpriteService=new();
    private System.Threading.CancellationTokenSource? _trainerSpriteCancel;
    private async System.Threading.Tasks.Task LoadTeamSpriteAsync(Image picture,TextBlock fallback,
        int trainerId,int speciesId,int form,System.Threading.CancellationToken token)
    {
        try
        {
            var result=await _trainerSpriteService.GetSpriteAsync(speciesId,speciesId,form,token);
            if(token.IsCancellationRequested||_id!=trainerId)return;
            picture.Source=result.Image;
            fallback.Visibility=result.Image is null?Visibility.Visible:Visibility.Collapsed;
            if(result.Image is null)fallback.Text="?";
        }
        catch(System.OperationCanceledException) { }
    }
    private void UpdateTrainerTeam(int id,IReadOnlyList<EditorGroup> groups)
    {
        _trainerSpriteCancel?.Cancel();
        _trainerSpriteCancel?.Dispose();
        _trainerSpriteCancel=new System.Threading.CancellationTokenSource();
        TrainerTeamCards.Children.Clear();
        if(Editor is not ThreeDS.Progressive.Services.GameTrainerService)
        {
            TrainerTeamPreview.Visibility=Visibility.Collapsed;
            return;
        }
        TrainerTeamPreview.Visibility=Visibility.Visible;
        int count=groups.FirstOrDefault()?.Fields.FirstOrDefault(f=>f.Key=="Count")?.Value??0;
        for(int slot=0;slot<Math.Min(6,count);slot++)
        {
            string prefix=$"P{slot}.";
            var fields=groups.SelectMany(g=>g.Fields).Where(f=>f.Key.StartsWith(prefix)).ToDictionary(f=>f.Key);
            if(!fields.TryGetValue(prefix+"Species",out var species))continue;
            int form=fields.TryGetValue(prefix+"Form",out var f)?f.Value:0;
            int level=fields.TryGetValue(prefix+"Level",out var l)?l.Value:1;
            string speciesName=species.Choices?.FirstOrDefault(c=>c.Id==species.Value)?.Display??$"Pokémon #{species.Value}";
            var picture=new Image {Width=58,Height=58,Stretch=Stretch.Uniform,HorizontalAlignment=System.Windows.HorizontalAlignment.Center};
            RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.NearestNeighbor);
            var fallback=new TextBlock {Text="…",FontSize=24,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,
                VerticalAlignment=System.Windows.VerticalAlignment.Center,Foreground=(Brush)FindResource("TextSoft")};
            var spriteHost=new Grid {Height=65};
            spriteHost.Children.Add(picture);spriteHost.Children.Add(fallback);
            var cardContent=new StackPanel {HorizontalAlignment=System.Windows.HorizontalAlignment.Center};
            cardContent.Children.Add(spriteHost);
            cardContent.Children.Add(new TextBlock {Text=speciesName,TextTrimming=TextTrimming.CharacterEllipsis,
                Width=118,TextAlignment=TextAlignment.Center,FontSize=11,FontWeight=FontWeights.SemiBold});
            cardContent.Children.Add(new TextBlock {Text=$"Nv. {level}"+(form>0?$" · Forma {form}":""),
                TextAlignment=TextAlignment.Center,FontSize=11,Foreground=(Brush)FindResource("TextSoft")});
            var card=new Button {Content=cardContent,Width=134,Margin=new Thickness(0,4,8,8),Padding=new Thickness(4)};
            int current=slot;
            card.Click+=(_,_)=>
            {
                string key=$"P{current}.Species";
                if(_editorSections.TryGetValue(key,out var section))section.IsExpanded=true;
                if(_pokemonTabs.TryGetValue(current,out var tabs))tabs.SelectedIndex=0;
                if(_fields.TryGetValue(key,out var control))control.BringIntoView();
            };
            TrainerTeamCards.Children.Add(card);
            _=LoadTeamSpriteAsync(picture,fallback,id,species.Value,form,_trainerSpriteCancel.Token);
        }
        if(count==0)
            TrainerTeamCards.Children.Add(new TextBlock {Text="Este entrenador no tiene Pokémon en el equipo.",
                Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(6,12,6,12)});
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
            _id=id;var record=Editor.Read(id);BuildGroups(FieldsPanel,record,_fields,true);UpdateTrainerTeam(id,record);SelectionText.Text=Editor.Entries.First(e=>e.Id==id).Display;_dirty=false;SaveButton.IsEnabled=false;StatusText.Text="Editor listo.";Invalidate();
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
    private void TrainerCaps_Click(object sender,RoutedEventArgs e)=>OpenTrainerRuleTable(true);
    private void TrainerMoves_Click(object sender,RoutedEventArgs e)=>OpenTrainerRuleTable(false);
    private void OpenTrainerRuleTable(bool levelCaps)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameTrainerService trainer)return;
        if(!ConfirmLeave())return;
        try
        {
            // Commit pending randomization controls before opening. Only change the
            // template in memory; the ROM is untouched until Preview + Apply.
            var options=Values(_options);
            string captured=trainer.CaptureOptions(options);
            trainer.RestoreOptions(captured);
            var rows=trainer.GetRuleRows();
            if(rows.Count==0)
            {
                StudioDialog.Notice(Window.GetWindow(this),"Sin candidatos",
                    "Esta ROM no contiene en los archivos abiertos entrenadores importantes o jefes reconocidos por Progressive.");
                return;
            }
            var dialog=new TrainerRulesWindow(rows,levelCaps){Owner=Window.GetWindow(this)};
            if(dialog.ShowDialog()!=true)return;
            trainer.UpdateRuleRows(dialog.Rows,levelCaps);
            _loading=true;
            try {BuildGroups(OptionsPanel,Editor.RandomOptions,_options,false);}
            finally {_loading=false;}
            Invalidate();
            StatusText.Text=$"Reglas de {(levelCaps?"Level Caps":"movimientos")} actualizadas en memoria. Generá una vista previa para aplicar.";
        }
        catch(Exception ex)
        {
            StudioDialog.Error(Window.GetWindow(this),"No se pudieron editar las reglas",ex.Message);
        }
    }
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
