using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
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
    private WildTableClipboard? _wildClipboard;
    private ProgressiveBstTable? _bstTable;
    internal IReadOnlyDictionary<string,FrameworkElement> FieldControls=>_fields;
    internal IReadOnlyDictionary<string,FrameworkElement> OptionControls=>_options;
    public RecordEditorView(IProjectEditor editor) : this(editor,null) { }
    internal RecordEditorView(IProjectEditor editor,UiState? state)
    {
        Editor=editor;
        if(editor is GameEncounterService wildRanges6&&state is not null)wildRanges6.RestoreRangeOptions(state.Options);InitializeComponent();TitleText.Text=editor.Title;RandomizerButton.Content="Randomizar "+editor.Title.ToLowerInvariant();
        if(editor is ThreeDS.Progressive.Services.GameMegaEvolutionService mega)
        {
            if(mega.GameIsUsum)MegaUnlockButton.Visibility=Visibility.Visible;
            RandomizerButton.Visibility=Visibility.Collapsed;
            // Vanilla Mega Editor only offers manual slots, not a randomizer.
        }
        if(editor is ThreeDS.Progressive.Services.GameTrainerService)
            TrainerCapsButton.Visibility=TrainerMovesButton.Visibility=Visibility.Visible;
        if(editor is ThreeDS.Progressive.Services.GameEncounterService wildClipboard && wildClipboard.SupportsWildTemplate)
            WildCopyButton.Visibility=WildPasteButton.Visibility=WildPasteAreaButton.Visibility=
                WildDumpButton.Visibility=WildAddRangeButton.Visibility=
                WildRemoveRangeButton.Visibility=WildAuditButton.Visibility=Visibility.Visible;
        if(editor is ThreeDS.Progressive.Services.GameFieldItemsService)
            FieldExportButton.Visibility=Visibility.Visible;
        if(editor is ThreeDS.Progressive.Services.GameItemService)
            EconomyTableButton.Visibility=Visibility.Visible;
        if(editor is ThreeDS.Progressive.Services.GameStaticEncounterService staticEditor)
        {
            GiftBstButton.Visibility=Visibility.Visible;
            if(staticEditor.SupportsTotemRules)
                TotemCapsButton.Visibility=TotemBstButton.Visibility=TradesAnyButton.Visibility=TradesNamesButton.Visibility=Visibility.Visible;
        }
        if(editor is ITemplateProjectEditor template){TemplateButton.Visibility=Visibility.Visible;TemplateButton.Content=template.TemplateButton;TemplatePathText.Text=template.DefaultTemplatePath;}
        if(editor is ThreeDS.Progressive.Services.GameEncounterService wild&&state is not null)
            wild.RestoreRangeOptions(state.Options);
        if(editor is IOptionTemplateProjectEditor optionTemplate &&
           (editor is not ThreeDS.Progressive.Services.GameEncounterService wildOptions || wildOptions.SupportsWildTemplate))
        {ImportOptionsButton.Visibility=ExportOptionsButton.Visibility=Visibility.Visible;
            if(editor is ThreeDS.Progressive.Services.GameEncounterService)
            {
                ImportOptionsButton.Content="Cargar template Wild";
                ExportOptionsButton.Content="Exportar template Wild";
            }
            if(editor is GameFieldItems6Service or GameFieldItemsService)
            {ImportOptionsButton.Content="Importar plantilla de objetos";ExportOptionsButton.Content="Exportar plantilla de objetos";}
            if(state?.Template is { } saved)optionTemplate.RestoreOptions(saved);}
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
        try {options=OptionValues();}catch(ArgumentException){return new(new Dictionary<string,int>(),ScopeBox.SelectedIndex);}
        string? template=null;
        if(Editor is IOptionTemplateProjectEditor editor &&
           (Editor is not ThreeDS.Progressive.Services.GameEncounterService wildGen6 || wildGen6.SupportsWildTemplate))
        {try{template=editor.CaptureOptions(options);}catch(Exception ex) when(ex is ArgumentException or System.IO.InvalidDataException){ }}
        return new(options,ScopeBox.SelectedIndex,template);
    }
    private Dictionary<string,int> OptionValues()
    {
        var values=Values(_options);
        if(Editor is ThreeDS.Progressive.Services.GameEncounterService {SupportsWildTemplate:false})
            foreach(var pair in EditorValues.Flatten(Editor.RandomOptions))values.TryAdd(pair.Key,pair.Value);
        if(_bstTable is not null)
        {
            foreach(var pair in _bstTable.Capture())values[pair.Key]=pair.Value;
            if(Editor is ThreeDS.Progressive.Services.GameEncounterService wild)
                wild.RestoreRangeOptions(values);
        }
        return values;
    }
    private ProgressiveBstTable CreateBstTable(IEnumerable<EditorField> fields,bool wild=false)
    {
        var grid=new ProgressiveBstTable(fields,wild?999:1530);
        grid.Changed+=Invalidate;
        _bstTable=grid;
        return grid;
    }
    // El editor individual conserva su estructura. El randomizador de entrenadores
    // organiza cada control vanilla/Progressive por función y no por el orden
    // accidental del formulario WinForms original.
    private void BuildGroups(StackPanel panel,IEnumerable<EditorGroup> groups,Dictionary<string,FrameworkElement> controls,bool editor)
    {
        panel.Children.Clear();controls.Clear();
        if(!editor)_bstTable=null;
        if(editor){_editorSections.Clear();_pokemonTabs.Clear();}
        var entries=groups.ToArray();
        if(!editor&&Editor is ThreeDS.Progressive.Services.GameEncounterService {SupportsWildTemplate:false})
        {
            string[] gen7Only=["G7","AlolanForms","SkipInaccessible","PreservePlaceholders","SlotMode","SOS","Weather","AllCanCallAllies"];
            entries=entries.Select(g=>g with {Fields=g.Fields.Where(f=>!gen7Only.Contains(f.Key)).ToArray()}).ToArray();
        }
        if(!editor&&Editor is ThreeDS.Progressive.Services.GameTrainerService)
        {
            BuildTrainerTabs(panel,entries,controls);
            return;
        }
        var wildRanges=!editor&&Editor is ThreeDS.Progressive.Services.GameEncounterService
            ?entries.SelectMany(g=>g.Fields).Where(f=>f.Key.StartsWith("BST.",StringComparison.Ordinal)).ToArray()
            :[];
        foreach(var group in entries)
        {
            var filtered=wildRanges.Length>0?
                group with {Fields=group.Fields.Where(f=>!f.Key.StartsWith("BST.",StringComparison.Ordinal)
                    &&f.Key!="Progressive").ToArray()}
                :group;
            if(filtered.Fields.Count==0)continue;
            panel.Children.Add(editor&&Editor is ThreeDS.Progressive.Services.GameTrainerService
                &&filtered.Name.StartsWith("Pokémon ",StringComparison.Ordinal)
                    ?BuildTrainerPokemonCard(filtered,controls)
                    :BuildGroupCard(filtered,controls,editor));
        }
        if(wildRanges.Length>0)
        {
            var modeField=entries.SelectMany(g=>g.Fields).First(f=>f.Key=="Progressive");
            var layout=new StackPanel();
            layout.Children.Add(BuildFieldsGrid([modeField],controls,false));
            var table=CreateBstTable(wildRanges,true);
            layout.Children.Add(table);
            panel.Children.Add(GroupBorder(new Expander {Header="BST progresivo por nivel",
                IsExpanded=true,Foreground=(Brush)FindResource("TextMain"),Content=layout}));
            if(controls.TryGetValue("Progressive",out var control)&&control is CheckBox check)
            {
                void Toggle(object sender,RoutedEventArgs e)=>table.Visibility=
                    check.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
                check.Checked+=Toggle;check.Unchecked+=Toggle;
                table.Visibility=check.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
            }
        }
        WireExclusive(controls,"SimilarBST","Progressive");
        if(!editor&&Editor is ThreeDS.Progressive.Services.GameStaticEncounterService)
            WireExclusive(controls,"ShinyLock","RemoveLocks");
    }
    private static void WireExclusive(Dictionary<string,FrameworkElement> fields,string a,string b)
    {
        if(!fields.TryGetValue(a,out var first)||first is not CheckBox one||
           !fields.TryGetValue(b,out var second)||second is not CheckBox two)return;
        one.Checked+=(_,_)=>{if(two.IsChecked==true)two.IsChecked=false;};
        two.Checked+=(_,_)=>{if(one.IsChecked==true)one.IsChecked=false;};
        if(one.IsChecked==true&&two.IsChecked==true)two.IsChecked=false;
    }
    private readonly Dictionary<string,Expander> _editorSections=[];
    private int _trainerTabIndex;
    private static readonly string[] TrainerTabNames=
        ["Pokémon y equipos","Niveles de entrenadores","Movimientos y reglas","IA, objetos y combates","BST progresivo"];

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
                selected=selected.Where(f=>!f.Key.StartsWith("BST.",StringComparison.Ordinal)).ToArray();
                if(selected.Length==0)continue;
                string label=group.Name is "Main" or "Opciones generales"
                    ?TrainerTabNames[category]:group.Name;
                container.Children.Add(BuildGroupCard(group with
                {Name=label,Fields=selected,Expanded=category==4||group.Expanded},controls,false));
            }
            if(category==1)
                container.Children.Insert(0,new TextBlock
                {
                    Text="Estos límites afectan a los ENTRENADORES rivales. Los Player Level Caps son un parche distinto del código del jugador y se configuran por separado.",
                    Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
                    Margin=new Thickness(10,9,10,15)
                });
            if(category==4)
            {
                var ranges=groups.SelectMany(g=>g.Fields)
                    .Where(f=>f.Key.StartsWith("BST.",StringComparison.Ordinal)).ToArray();
                if(ranges.Length>0)
                {
                    var bstPanel=CreateBstTable(ranges);
                    container.Children.Add(bstPanel);
                    if(controls.TryGetValue("CHK_ProgressiveBST",out var control)&&control is CheckBox check)
                    {
                        void Toggle(object sender,RoutedEventArgs e)=>bstPanel.Visibility=
                            check.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
                        check.Checked+=Toggle;check.Unchecked+=Toggle;
                        bstPanel.Visibility=check.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
                    }
                }
            }
            if(container.Children.Count==0)
                container.Children.Add(new TextBlock {Text="No hay opciones adicionales para esta versión del juego.",
                    Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8,16,8,16)});
            tabs.Items.Add(new TabItem {Header=TrainerTabNames[category],Content=container});
        }
        WireExclusive(controls,"CHK_BST","CHK_ProgressiveBST");
        tabs.SelectionChanged+=(_,e)=>
        {
            if(ReferenceEquals(e.Source,tabs))_trainerTabIndex=tabs.SelectedIndex;
        };
        tabs.SelectedIndex=Math.Clamp(_trainerTabIndex,0,tabs.Items.Count-1);
        host.Children.Add(tabs);
    }

    internal static int TrainerOptionCategory(string key)
    {
        if(key.StartsWith("GlobalItems.",StringComparison.Ordinal))return 3;
        if(key.StartsWith("GlobalMoves.",StringComparison.Ordinal))return 2;
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
    private static readonly (string Parent,string[] Related)[] OptionDependencies=
    [
        ("CHK_ForceFullyEvolved",["NUD_ForceFullyEvolved"]),
        ("CHK_Damage",["NUD_Damage"]),
        ("CHK_STAB",["NUD_STAB"]),
        ("CHK_RandomDoubleBattles",["NUD_DoubleBattleChance"]),
        ("CHK_Level",["NUD_LevelBoost","NUD_Level"]),
        ("SimilarBST",["BST"]),
        ("ScaleLevels",["LevelScale","LevelFlat"]),
        ("ForceFinal",["FinalLevel"]),
        ("GlobalMoves.StrongStat",["GlobalMoves.Tolerance","GlobalMoves.StrongNormal","GlobalMoves.StrongImportant","GlobalMoves.StrongBoss"]),
        ("GlobalItems.Smart",["GlobalItems.Normal","GlobalItems.Important","GlobalItems.Boss","GlobalItems.QualityNormal","GlobalItems.QualityImportant","GlobalItems.QualityBoss"]),
        ("CHK_StrongStat",["NUD_StrongStatTolerance","CHK_StrongStatNormalTrainers",
            "CHK_StrongStatImportantTrainers","CHK_StrongStatBosses"]),
        ("CHK_BetterMovesets",["CHK_BetterMovesetsIncludeTMs",
            "CHK_BetterMovesetsNormalTrainers","CHK_BetterMovesetsImportantTrainers","CHK_BetterMovesetsBosses","GlobalMoves.BetterNormal","GlobalMoves.BetterImportant","GlobalMoves.BetterBoss","GlobalMoves.IncludeTMs"]),
        ("CHK_SmartHeldItems",["CHK_SmartItemsNormalTrainers","CHK_SmartItemsImportantTrainers",
            "CHK_SmartItemsBosses","CB_SmartHeldItemMode","CB_SmartHeldItemModeNormal",
            "CB_SmartHeldItemModeImportant","CB_SmartHeldItemModeBoss"])
    ];
    private static IReadOnlyList<EditorField> SortedOptions(IReadOnlyList<EditorField> fields)
    {
        var ordered=new List<EditorField>();
        var consumed=new HashSet<string>();
        foreach(var field in fields)
        {
            if(consumed.Contains(field.Key))continue;
            var relation=OptionDependencies.FirstOrDefault(pair=>pair.Related.Contains(field.Key));
            if(relation.Parent is not null&&fields.Any(f=>f.Key==relation.Parent))continue;
            ordered.Add(field);consumed.Add(field.Key);
            var children=OptionDependencies.FirstOrDefault(pair=>pair.Parent==field.Key).Related;
            if(children is not null)
                foreach(var child in fields.Where(f=>children.Contains(f.Key)))
                    if(consumed.Add(child.Key))ordered.Add(child);
        }
        foreach(var field in fields)if(consumed.Add(field.Key))ordered.Add(field);
        return ordered;
    }
    private Border BuildGroupCard(EditorGroup group,Dictionary<string,FrameworkElement> controls,bool editor)
    {
        var fields=group.Fields;
        var master=OptionDependencies.FirstOrDefault(p=>fields.Any(f=>f.Key==p.Parent)
            &&p.Related.Any(k=>fields.Any(f=>f.Key==k)));
        FrameworkElement content;
        if(!editor&&master.Parent is "CHK_StrongStat" or "CHK_BetterMovesets" or "CHK_SmartHeldItems" or "GlobalMoves.StrongStat" or "GlobalItems.Smart")
        {
            var first=fields.First(f=>f.Key==master.Parent);
            var dependents=fields.Where(f=>master.Related.Contains(f.Key)).ToArray();
            bool smart=master.Parent is "CHK_SmartHeldItems" or "GlobalItems.Smart";
            var random=smart?fields.FirstOrDefault(f=>f.Key=="CHK_RandomItems"):null;
            var unrelated=fields.Where(f=>f.Key!=master.Parent&&!master.Related.Contains(f.Key)&&f.Key!=random?.Key).ToArray();
            var stack=new StackPanel();
            stack.Children.Add(BuildFieldsGrid(random is null?[first]:[random,first],controls,false));
            var sub=BuildFieldsGrid(dependents,controls,false);
            stack.Children.Add(sub);
            if(unrelated.Length>0)stack.Children.Add(BuildFieldsGrid(unrelated,controls,false));
            if(controls[master.Parent] is CheckBox check)
            {
                var randomCheck=random is not null?controls[random.Key] as CheckBox:null;
                void Toggle(object sender,RoutedEventArgs e)
                {
                    check.IsEnabled=randomCheck?.IsChecked!=false;
                    sub.Visibility=check.IsChecked==true&&check.IsEnabled?Visibility.Visible:Visibility.Collapsed;
                }
                check.Checked+=Toggle;check.Unchecked+=Toggle;
                if(randomCheck is not null){randomCheck.Checked+=Toggle;randomCheck.Unchecked+=Toggle;}
                Toggle(check,new RoutedEventArgs());
            }
            content=stack;
        }
        else content=BuildFieldsGrid(fields,controls,editor);
        var expand=new Expander {Header=group.Name,IsExpanded=group.Expanded,
            Foreground=(Brush)FindResource("TextMain"),Content=content};
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
        foreach(var field in editor?fields:SortedOptions(fields))
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
            System.Windows.Automation.AutomationProperties.SetName(input,field.Label);
            if(!isCheck&&cell.Children[0] is TextBlock caption)System.Windows.Automation.AutomationProperties.SetLabeledBy(input,caption);
            if(!isCheck&&field.Choices is null)
            {
                string range=$"{field.Min/(decimal)field.Scale} – {field.Max/(decimal)field.Scale}";
                System.Windows.Automation.AutomationProperties.SetHelpText(input,range);
                cell.Children.Add(new TextBlock{Text=range,FontSize=12,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,4,0,0)});
            }
            if(editor&&Editor is ThreeDS.Progressive.Services.GameTrainerService
                &&field.Key=="AI"&&input is TextBox ai)
                AddTrainerAiBits(cell,ai);
            grid.Children.Add(cell);
        }
        if(!editor)
            foreach(var (parent,related) in OptionDependencies)
                if(controls.TryGetValue(parent,out var toggle)&&toggle is CheckBox check)
                    foreach(var child in related)
                        if(controls.TryGetValue(child,out var dependent))
                        {
                            dependent.IsEnabled=check.IsChecked==true;
                            check.Checked+=(_,_)=>dependent.IsEnabled=true;
                            check.Unchecked+=(_,_)=>dependent.IsEnabled=false;
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
            fallback.Visibility=Visibility.Collapsed;
            fallback.Text="";
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
            var fallback=new TextBlock {Text="",FontSize=24,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,
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
    private void EncounterGroup_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_id<0||Editor is not GameEncounterService wild)return;
        try { UpdateEncounterSprites(_id,wild.Read(_id)); }
        catch(Exception ex) { StatusText.Text="No se pudo mostrar esta fila: "+ex.Message; }
    }

    private void UpdateEncounterSprites(int id,IReadOnlyList<EditorGroup> groups)
    {
        EncounterSpriteCards.Children.Clear();
        EncounterPairCards.Children.Clear();
        bool wild=Editor is GameEncounterService {SupportsWildTemplate:true};
        WildPreviewControls.Visibility=wild?Visibility.Visible:Visibility.Collapsed;
        EncounterPairPreview.Visibility=Visibility.Collapsed;
        if(Editor is not GameEncounterService && Editor is not GameStaticEncounterService)
        {
            EncounterSpritePreview.Visibility=Visibility.Collapsed;
            return;
        }
        EditorGroup? section;
        if(wild)
        {
            if(EncounterGroupPicker.SelectedIndex<0)
                EncounterGroupPicker.SelectedIndex=0;
            int selected=Math.Clamp(EncounterGroupPicker.SelectedIndex,0,8);
            section=groups.Skip(1).ElementAtOrDefault(selected);
            var main=groups.FirstOrDefault()?.Fields.ToDictionary(f=>f.Key);
            int min=main?.GetValueOrDefault("Min")?.Value??0;
            int max=main?.GetValueOrDefault("Max")?.Value??0;
            int active=section?.Fields.Count(f=>f.Key.EndsWith(".Species",StringComparison.Ordinal)
                &&f.Value>0)??0;
            string summary=$"Niveles {min}–{max} · {active} especies en la fila seleccionada";
            if(selected==0)
            {
                int rate=Enumerable.Range(0,10).Sum(slot=>
                    main?.GetValueOrDefault("Rate"+slot)?.Value??0);
                summary+=$" · Probabilidades: {rate}%";
                if(rate is not (0 or 100))summary+=" (debe sumar 0% o 100%)";
            }
            EncounterGroupSummary.Text=summary;
        }
        else
            section=groups.FirstOrDefault(g=>g.Fields.Any(f=>f.Key=="Species"||
                f.Key.EndsWith(".Species",StringComparison.Ordinal)));
        if(section is null)
        {
            EncounterSpritePreview.Visibility=Visibility.Collapsed;return;
        }
        EncounterSpritePreview.Visibility=Visibility.Visible;
        EncounterSpriteTitle.Text=wild&&Editor is GameEncounterService w
            ?$"{w.TimeOfDay(id).ToUpperInvariant()} · {section.Name.ToUpperInvariant()}"
            :section.Name.ToUpperInvariant()+" · SPRITES";
        RenderEncounterRow(EncounterSpriteCards,section,id,null);
        if(wild&&Editor is GameEncounterService editor&&
            editor.PairedDayNightId(id) is int paired)
        {
            int selected=Math.Clamp(EncounterGroupPicker.SelectedIndex,0,8);
            var alternate=editor.Read(paired).Skip(1).ElementAtOrDefault(selected);
            if(alternate is not null)
            {
                EncounterPairPreview.Visibility=Visibility.Visible;
                EncounterPairTitle.Text=$"{editor.TimeOfDay(paired).ToUpperInvariant()} · {alternate.Name} · tabla #{paired}";
                RenderEncounterRow(EncounterPairCards,alternate,id,paired);
            }
        }
    }

    private void RenderEncounterRow(WrapPanel host,EditorGroup section,int activeId,int? pairedId)
    {
        var allFields=section.Fields;
        var entries=allFields.Where(f=>f.Key=="Species"||
            f.Key.EndsWith(".Species",StringComparison.Ordinal)).Take(12).ToArray();
        for(int position=0;position<entries.Length;position++)
        {
            var field=entries[position];
            string prefix=field.Key.EndsWith(".Species",StringComparison.Ordinal)
                ?field.Key[..^7]:string.Empty;
            int form=allFields.FirstOrDefault(f=>f.Key==prefix+"Form")?.Value??0;
            int species=field.Value;
            var picture=new Image {Width=52,Height=52,Stretch=Stretch.Uniform,
                HorizontalAlignment=System.Windows.HorizontalAlignment.Center};
            RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.NearestNeighbor);
            var fallback=new TextBlock {Text=species==0?"—":"",FontSize=17,
                Foreground=(Brush)FindResource("TextSoft"),
                VerticalAlignment=System.Windows.VerticalAlignment.Center,
                HorizontalAlignment=System.Windows.HorizontalAlignment.Center};
            var art=new Grid {Height=56};
            art.Children.Add(picture);art.Children.Add(fallback);
            var content=new StackPanel();
            content.Children.Add(new TextBlock {Text=$"#{position+1:00}",
                FontSize=10,Foreground=(Brush)FindResource("TextSoft"),
                TextAlignment=TextAlignment.Center});
            content.Children.Add(art);
            string label=field.Choices?.FirstOrDefault(choice=>choice.Id==species)?.Display??$"#{species}";
            content.Children.Add(new TextBlock {Text=label,Width=95,
                TextTrimming=TextTrimming.CharacterEllipsis,
                TextAlignment=TextAlignment.Center,FontSize=11});
            if(form>0)
                content.Children.Add(new TextBlock {Text=$"Forma {form}",FontSize=10,
                    Foreground=(Brush)FindResource("TextSoft"),TextAlignment=TextAlignment.Center});
            var card=new Button {Content=content,Width=104,Margin=new Thickness(0,0,8,8),
                Padding=new Thickness(3),
                ToolTip=pairedId.HasValue?"Abrir esta ranura en la tabla del otro horario":
                    "Ir a la especie y forma de esta ranura"};
            string key=field.Key;
            card.Click+=(_,_) =>
            {
                if(pairedId.HasValue)
                {
                    RecordList.SelectedItem=Editor.Entries.FirstOrDefault(x=>x.Id==pairedId.Value);
                    if(_id!=pairedId.Value)return;
                }
                if(_editorSections.TryGetValue(key,out var expand))expand.IsExpanded=true;
                if(_fields.TryGetValue(key,out var control))control.BringIntoView();
            };
            host.Children.Add(card);
            if(species>0&&_trainerSpriteCancel is not null)
                _=LoadTeamSpriteAsync(picture,fallback,activeId,species,form,_trainerSpriteCancel.Token);
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
            _id=id;var record=Editor.Read(id);BuildGroups(FieldsPanel,record,_fields,true);UpdateTrainerTeam(id,record);UpdateEncounterSprites(id,record);SelectionText.Text=Editor.Entries.First(e=>e.Id==id).Display;_dirty=false;SaveButton.IsEnabled=false;StatusText.Text="Editor listo.";Invalidate();
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
    public bool ConfirmLeave()=>TrainerRulesHost.Visibility!=Visibility.Visible&&
        (!_dirty||StudioDialog.Confirm(Window.GetWindow(this),"Cambios pendientes","Hay modificaciones sin guardar.","Si continuás, se descartará la edición actual."));
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
    private void EconomyTable_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameItemService items||!ConfirmLeave())return;
        try
        {
            var table=new EconomyPriceView(items);
            table.Closed+=CloseTrainerRules;
            table.Saved+=()=>
            {
                CloseTrainerRules();
                if(_id>=0)Show(_id);
                StatusText.Text="Economía actualizada, guardada y verificada.";
            };
            TrainerRulesHost.Content=table;
            TrainerRulesHost.Visibility=Visibility.Visible;
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo abrir Fix Economy",ex.Message);}
    }
    private void FieldExport_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameFieldItemsService field)return;
        try
        {
            var picker=new Microsoft.Win32.SaveFileDialog {Filter="CSV|*.csv",FileName="field-items.csv"};
            if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
            System.IO.File.WriteAllText(picker.FileName,field.ExportCsv(),System.Text.Encoding.UTF8);
            StatusText.Text="Field Items exportados a CSV.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Error al exportar Field Items",ex.Message);}
    }
    private void TradesNames_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameStaticEncounterService service
            ||!service.SupportsTotemRules||!ConfirmLeave())return;
        try
        {
            UiProgress.Begin("Preparando los diálogos originales de intercambio USUM…");
            var dialogue=new ThreeDS.Progressive.Services.GameTradeDialogueService(
                service.ProjectSession);
            ThreeDS.Progressive.Models.EditorProposal preview;
            try{preview=dialogue.Preview();}
            finally{UiProgress.End();}
            if(preview.Changes.Count==0)
            {
                StudioDialog.Notice(Window.GetWindow(this),"Diálogos de intercambios",
                    "El banco español ya coincide con los diálogos genéricos previstos.");
                return;
            }
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Ocultar nombres en intercambios",
                $"Se sustituirán {preview.Changes.Count} líneas del banco storytext español (archivo 14), dejando intactas las demás líneas e idiomas."))return;
            UiProgress.Begin("Guardando diálogos de intercambios…");
            try{dialogue.Apply(preview);}
            finally{UiProgress.End();}
            StatusText.Text="Diálogos españoles guardados y registrados en el historial.";
        }
        catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(this),"Diálogos de intercambios",ex.Message);}
    }
    private void TradesAny_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameStaticEncounterService service||
           !service.SupportsTotemRules||!ConfirmLeave())return;
        try
        {
            UiProgress.Begin("Verificando el parche de intercambios USUM…");
            ThreeDS.Progressive.Models.EditorProposal proposal;
            try{proposal=service.PreviewTradeAcceptAny();}
            finally{UiProgress.End();}
            if(proposal.Files.All(file=>!file.Changed))
            {
                StudioDialog.Notice(Window.GetWindow(this),"Trades Accept Any",
                    "El selector y los intercambios ya están configurados. No hay cambios.");
                return;
            }
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Trades Accept Any",
                "Se aplicarán conjuntamente las solicitudes de intercambio (especie 0) y el hook verificado de code.bin. Si hay firmas desconocidas, la operación se rechaza."))return;
            UiProgress.Begin("Aplicando parche de intercambios y verificando archivos…");
            try{service.Apply(proposal);}
            finally{UiProgress.End();}
            if(_id>=0)Show(_id);
            StatusText.Text="Trades Accept Any aplicado en code.bin y encuentro estático con historial.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Trades Accept Any",ex.Message);}
    }
    private void MegaUnlock_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameMegaEvolutionService mega||!ConfirmLeave())return;
        try
        {
            var engine=new ThreeDS.Progressive.Services.GameMegaUnlockService(mega.Session);
            var proposal=engine.Preview();
            if(!proposal.File.Changed)
            {
                StudioDialog.Notice(Window.GetWindow(this),"Mega desde el inicio",
                    "Este Battle.cro ya contiene el parche de desbloqueo Mega.");
                return;
            }
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Desbloquear Mega Evolution",
                "Se sustituirá la instrucción ARM verificada en Battle.cro y se recalcularán los hashes CRO. ¿Continuar?"))return;
            UiProgress.Begin("Parcheando Battle.cro · Mega Evolution desde el inicio…");
            try{engine.Apply(proposal.File,proposal.Changes);}
            finally{UiProgress.End();}
            StatusText.Text="Desbloqueo Mega instalado, verificado y registrado en historial.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Mega Evolution",ex.Message);}
    }
    private void GiftBst_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameStaticEncounterService service
            ||!ConfirmLeave())return;
        try
        {
            var preview=service.PreviewGiftBstBaseline();
            if(preview.Changes.Count==0)
            {
                StudioDialog.Notice(Window.GetWindow(this),"Gift BST Baseline",
                    "Los regalos ya tienen especies con los BST base indicados.");
                return;
            }
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Gift BST Baseline",
                $"Se cambiarán {preview.Changes.Count} campos de regalos: índices 4–13 Squirtle (BST 314), otros no iniciales Arcanine (BST 555). Los iniciales 0–2 no se modifican."))return;
            UiProgress.Begin("Guardando BST base de regalos…");
            try{service.Apply(preview);}
            finally{UiProgress.End();}
            if(_id>=0)Show(_id);
            StatusText.Text="Gift BST Baseline aplicada, verificada y registrada en el historial.";
        }
        catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(this),"Error en Gift BST Baseline",ex.Message);}
    }
    private void TotemCaps_Click(object sender,RoutedEventArgs e)=>OpenTotemRules(false);
    private void TotemBst_Click(object sender,RoutedEventArgs e)=>OpenTotemRules(true);
    private void OpenTotemRules(bool bstMode)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameStaticEncounterService service
            ||!service.SupportsTotemRules||!ConfirmLeave())return;
        try
        {
            var table=new TotemRulesView(service,bstMode);
            table.Closed+=CloseTrainerRules;
            table.Saved+=()=>
            {
                CloseTrainerRules();
                if(_id>=0)Show(_id);
                StatusText.Text="Reglas Totem guardadas en el GARC con historial.";
            };
            TrainerRulesHost.Content=table;
            TrainerRulesHost.Visibility=Visibility.Visible;
        }
        catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(this),"No se pudo abrir la tabla Totem",ex.Message);}
    }
    private void TrainerCaps_Click(object sender,RoutedEventArgs e)=>OpenTrainerRuleTable(true);
    private void TrainerMoves_Click(object sender,RoutedEventArgs e)=>OpenTrainerRuleTable(false);
    private void OpenTrainerRuleTable(bool levelCaps)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameTrainerService trainer)return;
        if(!ConfirmLeave())return;
        try
        {
            var options=OptionValues();
            trainer.RestoreOptions(trainer.CaptureOptions(options));
            var rows=trainer.GetRuleRows();
            if(rows.Count==0)
            {
                StudioDialog.Notice(Window.GetWindow(this),"Sin candidatos",
                    "No se encontraron entrenadores importantes ni jefes en los datos de este proyecto.");
                return;
            }
            var table=new TrainerRulesWindow(rows,levelCaps);
            table.Cancelled+=CloseTrainerRules;
            table.Accepted+=edited=>
            {
                try
                {
                    trainer.UpdateRuleRows(edited,levelCaps);
                    _loading=true;
                    try{BuildGroups(OptionsPanel,Editor.RandomOptions,_options,false);}
                    finally{_loading=false;}
                    Invalidate();
                    CloseTrainerRules();
                    StatusText.Text=$"Tabla de {(levelCaps?"Level Caps":"movimientos")} actualizada. Generá una vista previa antes de guardar.";
                }
                catch(Exception ex)
                {StudioDialog.Error(Window.GetWindow(this),"No se pudieron guardar las reglas",ex.Message);}
            };
            TrainerRulesHost.Content=table;
            TrainerRulesHost.Visibility=Visibility.Visible;
            StatusText.Text="Editando tabla de entrenadores dentro de Progressive.";
        }
        catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(this),"No se pudo abrir la tabla",ex.Message);}
    }
    private void CloseTrainerRules()
    {
        TrainerRulesHost.Visibility=Visibility.Collapsed;
        TrainerRulesHost.Content=null;
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
    private async void Preview_Click(object sender,RoutedEventArgs e)
    {
        UiProgress.Begin("Generando vista previa de "+Editor.Title+"…");
        if(!StudioDialog.AutomatedChecks)
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        try
        {
            int[] scope=ScopeBox.SelectedIndex switch {1=>RecordList.Items.Cast<RecordEntry>().Select(e=>e.Id).ToArray(),2=>_id>=0?[_id]:[],_=>Editor.Entries.Select(e=>e.Id).ToArray()};
            _proposal=_templateMode&&Editor is ITemplateProjectEditor template?template.PreviewTemplate(TemplatePathText.Text,scope):Editor.Preview(OptionValues(),scope);ChangeList.ItemsSource=_proposal.Changes;
            ProposalText.Text=$"{_proposal.Changes.Count} cambios"+(_proposal.Seed==0?"":$" · semilla {_proposal.Seed}")+". Revisá la propuesta antes de aplicar.";
            ApplyButton.IsEnabled=_proposal.Changes.Count>0;
        }
        catch(Exception ex){Invalidate();ProposalText.Text=ex.Message;}
        finally{UiProgress.End();}
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
    private void WildAddRange_Click(object sender,RoutedEventArgs e)=>ChangeWildRanges(true);
    private void WildRemoveRange_Click(object sender,RoutedEventArgs e)=>ChangeWildRanges(false);
    private void ChangeWildRanges(bool add)
    {
        if(Editor is not GameEncounterService wild)return;
        try
        {
            var values=OptionValues();
            bool changed=add?wild.SplitLongestWildRange():wild.MergeLastWildRange();
            if(!changed)
            {
                StudioDialog.Notice(Window.GetWindow(this),"Rangos BST",
                    add?"No hay un intervalo BST divisible.":"Solo queda un intervalo BST.");
                return;
            }
            _loading=true;
            try
            {
                // Keep every other option while rebinding the changed BST rows.
                var groups=wild.RandomOptions.Select(g=>g with
                {
                    Fields=g.Fields.Select(f=>!f.Key.StartsWith("BST.",StringComparison.Ordinal)&&
                        values.TryGetValue(f.Key,out int v)&&v>=f.Min&&v<=f.Max
                        ?f with {Value=v}:f).ToArray()
                });
                BuildGroups(OptionsPanel,groups,_options,false);
            }
            finally {_loading=false;}
            Invalidate();
            StatusText.Text=add?"Rango BST dividido. Generá vista previa antes de guardar.":
                "Últimos dos rangos BST unidos. Generá vista previa antes de guardar.";
        }
        catch(Exception ex){_loading=false;StudioDialog.Error(Window.GetWindow(this),
            "Editar rangos BST",ex.Message);}
    }
    private void WildAudit_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not GameEncounterService wild)return;
        try
        {
            string result=wild.DiagnoseWild(OptionValues());
            StudioDialog.Notice(Window.GetWindow(this),"Diagnóstico Progressive Wild",
                "Análisis de especies, niveles y rangos sin modificar la ROM.",result);
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Diagnóstico Wild",ex.Message);}
    }
    private void WildDump_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not GameEncounterService wild)return;
        try
        {
            var picker=new Microsoft.Win32.SaveFileDialog
            {Filter="Reporte de encuentros (*.txt)|*.txt",FileName="encounter-tables.txt"};
            if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
            System.IO.File.WriteAllText(picker.FileName,wild.ExportAllEncounterTables(),
                System.Text.Encoding.UTF8);
            StatusText.Text="Reporte de encuentros exportado. No se modificó la ROM.";
        }
        catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(this),"Exportar encuentros",ex.Message);}
    }
    private void WildCopy_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameEncounterService wild||_id<0)return;
        if(_dirty)
        {
            StudioDialog.Error(Window.GetWindow(this),"Copiar tabla",
                "Guardá o descartá primero los cambios pendientes de esta tabla.");
            return;
        }
        try
        {
            _wildClipboard=wild.CopyWildTable(_id);
            StatusText.Text=$"Tabla #{_id} copiada: {_wildClipboard.OccupiedNormalSlots} ranuras normales ocupadas. " +
                "Elegí una tabla de destino y generá vista previa para pegar.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Copiar encuentros",ex.Message);}
    }
    private void WildPaste_Click(object sender,RoutedEventArgs e)=>PreviewWildPaste(false);
    private void WildPasteArea_Click(object sender,RoutedEventArgs e)=>PreviewWildPaste(true);
    private void PreviewWildPaste(bool allCompatible)
    {
        if(Editor is not ThreeDS.Progressive.Services.GameEncounterService wild||_id<0)return;
        if(_wildClipboard is null)
        {
            StudioDialog.Notice(Window.GetWindow(this),"Pegar encuentros",
                "Primero copiá una tabla de encuentros desde este proyecto.");
            return;
        }
        if(_dirty)
        {
            StudioDialog.Error(Window.GetWindow(this),"Pegar encuentros",
                "Guardá o descartá la edición individual antes de pegar una tabla.");
            return;
        }
        try
        {
            int[] ids=allCompatible?wild.CompatibleWildTables(_wildClipboard,_id).ToArray():[_id];
            var proposal=wild.PreviewPasteWild(_wildClipboard,ids);
            _proposal=proposal;
            _templateMode=false;
            ToolTitle.Text=allCompatible?"Pegar tablas en área compatible":"Pegar tabla de encuentros";
            TemplatePanel.Visibility=Visibility.Collapsed;
            OptionsPanel.Visibility=Visibility.Collapsed;
            FieldsScroll.Visibility=Visibility.Collapsed;
            ToolsPanel.Visibility=Visibility.Visible;
            SaveButton.IsEnabled=false;
            ChangeList.ItemsSource=proposal.Changes;
            ApplyButton.IsEnabled=proposal.Changes.Count>0;
            ProposalText.Text=$"{proposal.Changes.Count} diferencias en {ids.Length} tablas. " +
                "Las tablas incompatibles y otras áreas permanecen intactas. " +
                "Ningún archivo ha sido modificado todavía.";
            StatusText.Text="Vista previa lista · confirmá antes de aplicar el pegado.";
        }
        catch(Exception ex)
        {
            Invalidate();
            StudioDialog.Error(Window.GetWindow(this),"Pegar encuentros",ex.Message);
        }
    }
    private void ImportOptions_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not IOptionTemplateProjectEditor editor)return;
        bool wild=Editor is ThreeDS.Progressive.Services.GameEncounterService;
        bool field=Editor is GameFieldItems6Service or GameFieldItemsService;
        var dialog=new Microsoft.Win32.OpenFileDialog
        {Filter=field?"Template Field Items v1|*.json":wild?"Template Wild Progressive v1|*.json":"Template de entrenadores Progressive|*.json"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{editor.ImportOptions(dialog.FileName);_loading=true;BuildGroups(OptionsPanel,Editor.RandomOptions,_options,false);_loading=false;Randomizer_Click(sender,e);}
        catch(Exception ex){_loading=false;StudioDialog.Error(Window.GetWindow(this),"No se pudo cargar el template",ex.Message);}
    }
    private void ExportOptions_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not IOptionTemplateProjectEditor editor)return;
        bool wild=Editor is ThreeDS.Progressive.Services.GameEncounterService;
        bool field=Editor is GameFieldItems6Service or GameFieldItemsService;
        var dialog=new Microsoft.Win32.SaveFileDialog
        {Filter=field?"Template Field Items v1|*.json":wild?"Template Wild Progressive v1|*.json":"Template de entrenadores Progressive|*.json",
        FileName=field?"field-items-template.json":wild?"wild-template.json":"trainer-template.json"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{editor.ExportOptions(dialog.FileName,OptionValues());StudioDialog.Notice(Window.GetWindow(this),"Template exportado",
            field?"Se conservaron los IDs prohibidos, el catálogo permitido y los filtros de Field Items.":wild?"Se conservaron los filtros y rangos Progressive Wild v1.":
            "Se conservaron las opciones, rangos y reglas de entrenadores.");}
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo exportar",ex.Message);}
    }
    private async void Apply_Click(object sender,RoutedEventArgs e)
    {
        var proposal=_proposal;if(proposal is null)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Aplicar propuesta",$"Se guardarán {proposal.Changes.Count} cambios en {Editor.Title}."))return;
        UiProgress.Begin("Guardando y verificando "+Editor.Title+"…");
        if(!StudioDialog.AutomatedChecks)
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        try{Editor.Apply(proposal);Invalidate();ProposalText.Text="Propuesta guardada y verificada. El historial quedó actualizado.";}
        catch(Exception ex){Invalidate();ProposalText.Text=ex.Message;}
        finally{UiProgress.End();}
    }
}
