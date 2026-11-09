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
        if(editor is ThreeDS.Progressive.Services.GameTrainerService||editor is ITrainerEditorPresentation {UsesTrainerLayout:true})
            TrainerCapsButton.Visibility=TrainerMovesButton.Visibility=Visibility.Visible;
        if(editor is ThreeDS.Progressive.Services.Switch.SwordShieldRecordEditor)TrainerMovesButton.Visibility=Visibility.Collapsed;
        if(editor is ThreeDS.Progressive.Services.Switch.SwordShieldRecordEditor {HasTradeEvolutionAction:true})AddInspectorAction("Eliminar evoluciones por intercambio · nivel 37",ShowSwitchTradePreview);
        if(editor is ThreeDS.Progressive.Services.GameEncounterService wildClipboard && wildClipboard.SupportsWildTemplate)
            WildCopyButton.Visibility=WildPasteButton.Visibility=WildPasteAreaButton.Visibility=
                WildDumpButton.Visibility=WildAddRangeButton.Visibility=
                WildRemoveRangeButton.Visibility=WildAuditButton.Visibility=Visibility.Visible;
        if(editor is ThreeDS.Progressive.Services.GameFieldItemsService)
            FieldExportButton.Visibility=Visibility.Visible;
        if(editor is ThreeDS.Progressive.Services.GameItemService || editor is ThreeDS.Progressive.Services.Switch.SwordShieldRecordEditor {HasEconomy:true})
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
           (editor is not ThreeDS.Progressive.Services.GameEncounterService wildOptions || wildOptions.SupportsWildTemplate)
           &&editor is not ThreeDS.Progressive.Services.Nds.NdsRecordEditor {SupportsOptionTemplates:false})
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
        BuildRecordSections(editor);
        // Inspector buttons sit in a narrow column: long labels go on two lines instead of being cut.
        WrapInspectorActions(InspectorBody);
        // Same for the record list: long names ("#002 · Área 000 · tabla 1 · Noche") wrap instead of a horizontal scroll bar.
        RecordList.DisplayMemberPath="";RecordList.SetResourceReference(ItemsControl.ItemTemplateProperty,"WrappedRecordContent");ScrollViewer.SetHorizontalScrollBarVisibility(RecordList,ScrollBarVisibility.Disabled);var style=new Style(typeof(ListBoxItem),(Style)FindResource(typeof(ListBoxItem)));style.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty,System.Windows.HorizontalAlignment.Stretch));RecordList.ItemContainerStyle=style;
        UseTrainerListTemplate();

        if(editor is IOptionTemplatePresentation labels)
        {
            ImportOptionsButton.Content=labels.ImportOptionsLabel;ExportOptionsButton.Content=labels.ExportOptionsLabel;WrapInspectorActions(InspectorBody);UpdateInspectorGroups();
        }
        // Cargar las reglas del entrenador que realmente estará seleccionado, no las del ID por defecto del host.
        if(editor is ThreeDS.Progressive.Services.GameTrainerService trainers&&editor.Entries.Count>0)
            trainers.SelectOptionsEntry(editor.Entries[0].Id);
        _loading=true;
        var groups=editor.RandomOptions.Select(g=>g with {Fields=g.Fields.Select(f=>
            state is not null&&state.Options.TryGetValue(f.Key,out int value)&&value>=f.Min&&value<=f.Max
                ?f with {Value=value}:f).ToArray()});
        BuildGroups(OptionsPanel,groups,_options,false);
        if(editor is GameFieldItemsService mintFields)OptionsPanel.Children.Add(new UsumMintPoolControl(mintFields.Project,Invalidate));
        if(state is not null)ScopeBox.SelectedIndex=Math.Clamp(state.Scope,0,2);
        if(_options.TryGetValue("SpecialShopsOnly",out var special)){special.ToolTip="Tiendas con inventario fijo de objetos no básicos. Se conservan los inventarios de suministros básicos y los que cambian con el progreso.";System.Windows.Automation.AutomationProperties.SetHelpText(special,special.ToolTip.ToString());}
        _loading=false;
        RecordList.SelectedIndex=0;
        UpdateInspectorGroups();
        InputHint.SetText(SearchText,"Nombre o número");
        // Step 3 «Aplica los cambios» becomes current as soon as a preview leaves something to apply.
        ApplyButton.IsEnabledChanged+=(_,e)=>{if((bool)e.NewValue)RandomizeSteps.SetStep(2);};
    }
    /// <summary>Says that the options shown are the ones applied last to the project, and where they come from.</summary>
    internal void ShowOptionsSource(string source)
    {
        OptionsSourceText.Text=$"Estas son las opciones de la última randomización aplicada ({source}), incluidos sus rangos. Puedes cambiarlas antes de generar la vista previa.";
        OptionsSourceNote.Visibility=Visibility.Visible;
    }
    /// <summary>An inspector group without any visible action is hidden together with its heading.</summary>
    private void UpdateInspectorGroups()
    {
        foreach(var group in new System.Windows.Controls.Panel[]{RulesGroup,TemplatesGroup,TableGroup})
            group.Visibility=group.Children.OfType<Button>().Any(b=>b.Visibility==Visibility.Visible)?Visibility.Visible:Visibility.Collapsed;
        MoreToolsGroup.Visibility=((System.Windows.Controls.Panel)MoreToolsGroup.Content).Children.OfType<Button>().Any(b=>b.Visibility==Visibility.Visible)?Visibility.Visible:Visibility.Collapsed;
        ScopeGroup.Visibility=RandomizerButton.Visibility;
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
        if(editor){_editorSections.Clear();_pokemonTabs.Clear();_slotBorders.Clear();}
        var entries=groups.ToArray();
        if(!editor&&Editor is ThreeDS.Progressive.Services.GameEncounterService {SupportsWildTemplate:false})
        {
            string[] gen7Only=["G7","AlolanForms","SkipInaccessible","PreservePlaceholders","SlotMode","SOS","Weather","AllCanCallAllies"];
            entries=entries.Select(g=>g with {Fields=g.Fields.Where(f=>!gen7Only.Contains(f.Key)).ToArray()}).ToArray();
        }
        if(!editor&&(Editor is ThreeDS.Progressive.Services.GameTrainerService||Editor is ITrainerEditorPresentation {UsesTrainerLayout:true}))
        {
            BuildTrainerTabs(panel,entries,controls);
            return;
        }
        var wildRanges=!editor&&(Editor is ThreeDS.Progressive.Services.GameEncounterService||Editor is IWildEditorPresentation {UsesWildLayout:true})
            ?entries.SelectMany(g=>g.Fields).Where(f=>f.Key.StartsWith("BST.",StringComparison.Ordinal)).ToArray()
            :[];
        // Wild editor: the per-slot rates of the main table go into its slot table, next to each species.
        var slotTables=editor&&Editor is ThreeDS.Progressive.Services.GameEncounterService;
        var rates=slotTables?entries.SelectMany(g=>g.Fields).Where(f=>f.Key.StartsWith("Rate",StringComparison.Ordinal)&&!f.Key.Contains('.')).ToArray():[];
        bool ratesPlaced=false;
        foreach(var group in entries)
        {
            if(slotTables&&IsEncounterSlotGroup(group))
            {
                panel.Children.Add(BuildEncounterSlotTable(group,controls,ratesPlaced?[]:rates));ratesPlaced=true;continue;
            }
            if(slotTables&&rates.Length>0&&group.Fields.Any(f=>rates.Contains(f)))
            {
                var kept=group with {Fields=group.Fields.Where(f=>!rates.Contains(f)).ToArray()};
                if(kept.Fields.Count>0)panel.Children.Add(BuildGroupCard(kept,controls,editor));
                continue;
            }
            var filtered=wildRanges.Length>0?
                group with {Fields=group.Fields.Where(f=>!f.Key.StartsWith("BST.",StringComparison.Ordinal)
                    &&f.Key!="Progressive").ToArray()}
                :group;
            if(filtered.Fields.Count==0)continue;
            panel.Children.Add(editor&&(Editor is ThreeDS.Progressive.Services.GameTrainerService||Editor is ITrainerEditorPresentation {UsesTrainerLayout:true})
                &&filtered.Name.StartsWith("Pokémon ",StringComparison.Ordinal)
                    ?BuildTrainerPokemonCard(filtered,controls)
                    :BuildGroupCard(filtered,controls,editor));
        }
        if(wildRanges.Length>0)
        {
            var modeField=entries.SelectMany(g=>g.Fields).First(f=>f.Key=="Progressive");
            var layout=new StackPanel();
            layout.Children.Add(BuildFieldsGrid([modeField],controls,false));
            var table=CreateBstTable(wildRanges,Editor is ThreeDS.Progressive.Services.GameEncounterService);
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
        // Trainer: the selected Pokémon goes right below the team cards; the trainer data closes the page.
        if(editor&&(Editor is ThreeDS.Progressive.Services.GameTrainerService or ThreeDS.Progressive.Services.Nds.NdsRecordEditor {UsesTrainerLayout:true})&&panel.Children.Count>1&&panel.Children[0] is UIElement trainerData)
        {panel.Children.RemoveAt(0);panel.Children.Add(trainerData);}
        if(editor && Editor is ITableEditorPresentation {UsesRecordTable:true}) BuildRecordTable(panel,entries,controls);
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
        if(Editor is ThreeDS.Progressive.Services.Nds.NdsRecordEditor {UsesTrainerLayout:true})labels[1]="Dificultad / IV";
        for(int i=0;i<labels.Length;i++)
        {
            int section=i;
            var fields=group.Fields.Where(f=>TrainerPokemonSection(f.Key)==section).ToArray();
            tabs.Items.Add(new TabItem {Header=labels[i],Content=i==1&&fields.Any(f=>f.Key.EndsWith(".IV0",StringComparison.Ordinal))
                ?BuildStatTable(fields,controls):BuildFieldsGrid(PresentTrainerFields(fields),controls,true)});
        }
        tabs.SelectedIndex=0;
        int slot=group.Fields.FirstOrDefault()?.Key is { } key&&key.StartsWith("P",StringComparison.Ordinal)
            &&key.IndexOf('.')>1&&int.TryParse(key[1..key.IndexOf('.')],out int index)?index:-1;
        if(slot>=0)_pokemonTabs[slot]=tabs;
        // Copy, cut, paste and delete live in the context menu of the team cards (right click on the sprite).
        tabs.SetResourceReference(StyleProperty,"UnderlineTabControl");
        FrameworkElement content=tabs;
        var expand=new Expander {Header=group.Name,IsExpanded=group.Expanded,
            Foreground=(Brush)FindResource("TextMain"),Content=content};
        foreach(var field in group.Fields)_editorSections[field.Key]=expand;
        var slotBorder=GroupBorder(expand);
        if(slot>=0)_slotBorders[slot]=slotBorder;
        return slotBorder;
    }
    private static (string Owner,string Label,Dictionary<string,int> Values)? _copiedPokemon;
    // Values are game IDs, so they are pasted only within the same editor of a game with the same records.
    private string PokemonClipboardOwner=>$"{Editor.GetType().FullName}|{Editor.Title}|{Editor.Entries.Count}";
    internal void CopyTrainerPokemon(Dictionary<string,FrameworkElement> controls,string prefix,string label)
    {
        try
        {
            var values=Values(controls.Where(p=>p.Key.StartsWith(prefix,StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value));
            _copiedPokemon=(PokemonClipboardOwner,$"{(RecordList.SelectedItem is RecordEntry entry?entry.Name+" · ":"")}{label}",values.ToDictionary(p=>p.Key[prefix.Length..],p=>p.Value));
            StatusText.Text=$"Copiado: {_copiedPokemon.Value.Label}. Haz clic derecho en una tarjeta del equipo (de este u otro entrenador) y elige «Pegar».";
        }
        catch(Exception ex){StatusText.Text="No se pudo copiar: "+ex.Message;}
    }
    internal void PasteTrainerPokemon(Dictionary<string,FrameworkElement> controls,string prefix,int slot,string label)
    {
        if(!CanPastePokemon||_copiedPokemon is not { } copied){StatusText.Text="Primero copia un Pokémon de este editor.";return;}
        int pasted=0;
        foreach(var (field,value) in copied.Values)
            if(controls.TryGetValue(prefix+field,out var control)){SetControlValue(control,value);pasted++;}
        // A slot outside the team joins it, like raising «Pokémon en el equipo».
        if(controls.TryGetValue("Count",out var count)&&Values(new(){["Count"]=count})["Count"]<=slot)SetControlValue(count,slot+1);
        StatusText.Text=$"Pegado en {label}: {copied.Label} ({pasted} campos). Cambios sin guardar.";
    }
    private static void SetControlValue(FrameworkElement control,int value)
    {
        switch(control)
        {
            case ComboBox combo:
                if(combo.ItemsSource is IEnumerable<MoveChoice> choices&&!choices.Any(c=>c.Id==value))
                    combo.ItemsSource=choices.Append(new MoveChoice(value,$"ID {value} (personalizado)")).ToArray();
                combo.SelectedValue=value;break;
            case CheckBox check:check.IsChecked=value==1;break;
            case TextBox text:text.Text=(value/(decimal)int.Parse(text.Uid,CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);break;
        }
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
        ("GlobalMoves.Better",["GlobalMoves.IncludeTMs","GlobalMoves.BetterNormal","GlobalMoves.BetterImportant","GlobalMoves.BetterBoss"]),
        ("GlobalMoves.StrongStat",["GlobalMoves.Tolerance","GlobalMoves.StrongNormal","GlobalMoves.StrongImportant","GlobalMoves.StrongBoss"]),
        ("GlobalItems.Smart",["GlobalItems.Normal","GlobalItems.Important","GlobalItems.Boss","GlobalItems.QualityNormal","GlobalItems.QualityImportant","GlobalItems.QualityBoss","GlobalItems.Quality"]),
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
        if(!editor&&master.Parent is "CHK_StrongStat" or "CHK_BetterMovesets" or "CHK_SmartHeldItems" or "GlobalMoves.Better" or "GlobalMoves.StrongStat" or "GlobalItems.Smart")
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
        // FormGrid: each row is as tall as its own fields (a UniformGrid stretched every row to the tallest field).
        var grid=new FormGrid {Columns=2,Margin=new Thickness(0,12,0,0)};
        foreach(var field in editor?fields:SortedOptions(fields))
        {
            var cell=new StackPanel {Margin=new Thickness(0,0,12,15)};
            bool isCheck=field.Choices is null&&field.Min==0&&field.Max==1;
            if(!isCheck)
                cell.Children.Add(new TextBlock {Text=field.Label,TextWrapping=TextWrapping.Wrap,
                    Foreground=(Brush)FindResource("TextSoft"),FontSize=12,Margin=new Thickness(0,0,0,6)});
            var input=CreateFieldInput(field,controls,editor,isCheck);
            cell.Children.Add(input);
            if(!isCheck&&cell.Children[0] is TextBlock caption)System.Windows.Automation.AutomationProperties.SetLabeledBy(input,caption);
            if(!isCheck&&field.Choices is null)
                cell.Children.Add(new TextBlock{Text=$"{field.Min/(decimal)field.Scale} – {field.Max/(decimal)field.Scale}",FontSize=12,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,4,0,0)});
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
            if(result.Image is not null){fallback.Visibility=Visibility.Collapsed;fallback.Text="";}
        }
        catch(System.OperationCanceledException) { }
    }
    private void EncounterGroup_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_id<0||Editor is not GameEncounterService wild)return;
        try { UpdateEncounterSprites(_id,wild.Read(_id)); }
        catch(Exception ex) { StatusText.Text="No se pudo mostrar esta fila: "+ex.Message; }
    }

    private static Dictionary<string,int> Values(Dictionary<string,FrameworkElement> controls)=>controls.ToDictionary(p=>p.Key,p=>p.Value switch
    {
        ComboBox {SelectedValue:int v}=>v,CheckBox check=>check.IsChecked==true?1:0,
        TextBox text when decimal.TryParse(text.Text.Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out decimal v)=>checked((int)(v*int.Parse(text.Uid))),
        _=>throw new ArgumentException("Hay un campo que no contiene un número válido.")
    });
    private void Input_Changed(object sender,RoutedEventArgs e)
    {
        if(_loading)return;Invalidate();if(sender is FrameworkElement {Tag:true}){_dirty=true;SaveButton.IsEnabled=true;StatusText.Text="Cambios sin guardar.";UpdateSummary(reload:false);ScheduleCardsRefresh();}
    }
    private void Invalidate(){_proposal=null;if(ApplyButton is not null)ApplyButton.IsEnabled=false;RandomizeSteps?.SetStep(0);}
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
            _id=id;var record=Editor.Read(id);_record=record;BuildGroups(FieldsPanel,record,_fields,true);WireNdsTrainerFormats();UpdateTrainerTeam(id,record);UpdateEncounterSprites(id,record);UpdateFieldItemOverview(id);PresentEvolutionArguments();SelectionText.Text=Editor.Entries.First(e=>e.Id==id).Display;_dirty=false;SaveButton.IsEnabled=false;StatusText.Text="Editor listo.";Invalidate();
            UpdateSummary(reload:true);
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
        Invalidate();ApplyRecordFilter();
    }
    // Search text plus the selected section (editors with IRecordSections).
    private string? _section;
    private StackPanel? _sectionBar;
    private void ApplyRecordFilter()
    {
        if(RecordList?.ItemsSource is null)return;
        string text=SearchText.Text.Trim();var sections=Editor as IRecordSections;
        var view=CollectionViewSource.GetDefaultView(RecordList.ItemsSource);
        view.Filter=o=>o is RecordEntry entry&&(_section is null||sections is null||sections.SectionOf(entry.Id)==_section)&&
            (entry.Name.Contains(text,StringComparison.OrdinalIgnoreCase)||entry.Id.ToString().Contains(text)
             ||ListsTrainers&&text.Length>0&&TrainerSubtitle(entry.Id).Contains(text,StringComparison.OrdinalIgnoreCase));
        CountText.Text=view.Cast<object>().Count()+" entradas";
    }
    private void BuildRecordSections(IProjectEditor editor)
    {
        if(_sectionBar is not null&&_sectionBar.Parent is System.Windows.Controls.Panel old)old.Children.Remove(_sectionBar);
        _sectionBar=null;_section=null;
        if(editor is not IRecordSections sections||SearchText.Parent is not StackPanel host)return;
        // One row in the narrow list column: a section selector ("Regalos (1)", "Estáticos (200)"...).
        var choices=new string?[]{null}.Concat(sections.Sections).Select(section=>(Section:section,Label:$"{section??"Todos"} ({(section is null?editor.Entries.Count:editor.Entries.Count(e=>sections.SectionOf(e.Id)==section))})")).ToArray();
        var selector=new System.Windows.Controls.ComboBox{ItemsSource=choices.Select(c=>c.Label).ToArray(),Margin=new Thickness(0,0,0,10)};
        System.Windows.Automation.AutomationProperties.SetName(selector,"Sección de la lista");
        var caption=new TextBlock{Text="Sección",FontSize=12,Margin=new Thickness(0,0,0,4)};caption.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        _sectionBar=new StackPanel();_sectionBar.Children.Add(caption);_sectionBar.Children.Add(selector);
        int current=1;
        selector.SelectionChanged+=(_,_)=>
        {
            if(_loading||selector.SelectedIndex<0||selector.SelectedIndex==current)return;
            if(_dirty&&!ConfirmLeave()){_loading=true;selector.SelectedIndex=current;_loading=false;return;}
            current=selector.SelectedIndex;_section=choices[current].Section;
            ApplyRecordFilter();
            if(RecordList.Items.Count>0)RecordList.SelectedIndex=0;
        };
        host.Children.Insert(host.Children.IndexOf(SearchText)+1,_sectionBar);
        _section=choices[1].Section;bool loading=_loading;_loading=true;selector.SelectedIndex=1;_loading=loading;
        ApplyRecordFilter();
    }
    internal bool PrepareToLeave()
    {if(!ConfirmLeave())return false;if(_dirty&&_id>=0)Show(_id);return true;}
    internal void RefreshSession()
    {if(!_dirty&&_id>=0)Show(_id);}
    public bool ConfirmLeave()=>TrainerRulesHost.Visibility!=Visibility.Visible&&
        (!_dirty||StudioDialog.Confirm(Window.GetWindow(this),"Cambios pendientes","Hay modificaciones sin guardar.","Si continuás, se descartará la edición actual."));
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        if(_id<0)return;
        try
        {
            var values=Values(_fields);
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Confirmar guardado","Se guardará la entrada seleccionada y su historial."))return;
            Editor.Save(_id,values);InvalidateTrainerSubtitles();Show(_id);StatusText.Text="Guardado y verificado.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo guardar",ex.Message);}
    }
    private void Discard_Click(object sender,RoutedEventArgs e){if(_id>=0)Show(_id);}
    private void EconomyTable_Click(object sender,RoutedEventArgs e)
    {
        if(Editor is not IEconomyEditor items||!ConfirmLeave())return;
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
    private static void WrapInspectorActions(DependencyObject root)
    {
        if(root is Button button&&button.Content is string){button.ToolTip=button.Content;button.SetResourceReference(ContentControl.ContentTemplateProperty,"WrappedActionContent");}
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())WrapInspectorActions(child);
    }
    internal void AddInspectorAction(string label,Action action)
    {
        if(RandomizerButton.Parent is StackPanel panel){var button=new Button{Content=label,Margin=new Thickness(0,0,0,10)};EditorPresentation.StyleAction(button,EditorPresentation.ActionRole.Secondary);button.Click+=(_,_)=>{if(PrepareToLeave())action();};WrapInspectorActions(button);panel.Children.Insert(1,button);}
    }
    private void OpenTrainerRuleTable(bool levelCaps)
    {
        if(Editor is not ITrainerRuleTableEditor trainer){if(Editor is ITrainerEditorPresentation {UsesTrainerLayout:true})OpenSwitchTrainerRules(levelCaps);return;}
        if(!ConfirmLeave())return;
        try
        {
            var options=OptionValues();
            if(Editor is ThreeDS.Progressive.Services.GameTrainerService original)original.RestoreOptions(original.CaptureOptions(options));
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
                    StatusText.Text=$"Tabla de {(levelCaps?"Level Caps":"movimientos")} actualizada. Genera una vista previa antes de guardar.";
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
        SaveButton.IsEnabled=false;ProposalText.Text="Configura las opciones y genera una vista previa.";ChangeList.ItemsSource=null;
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
            ProposalText.Text=$"{_proposal.Changes.Count} cambios"+(_proposal.Seed==0?"":$" · semilla {_proposal.Seed}")+". Revisa la propuesta antes de aplicar.";
            if(_proposal.Parameters.TryGetValue("Warnings",out string? warnings)&&!string.IsNullOrWhiteSpace(warnings))ProposalText.Text+="\nAvisos: "+_proposal.Parameters.GetValueOrDefault("WarningCount")+". "+string.Join("\n",warnings.Split('\n').Take(3))+"\nEl detalle completo se conserva al aplicar en el historial.";
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
            StatusText.Text=add?"Rango BST dividido. Genera vista previa antes de guardar.":
                "Últimos dos rangos BST unidos. Genera vista previa antes de guardar.";
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
                "Guarda o descarta primero los cambios pendientes de esta tabla.");
            return;
        }
        try
        {
            _wildClipboard=wild.CopyWildTable(_id);
            StatusText.Text=$"Tabla #{_id} copiada: {_wildClipboard.OccupiedNormalSlots} ranuras normales ocupadas. " +
                "Elige una tabla de destino y genera vista previa para pegar.";
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
                "Primero copia una tabla de encuentros desde este proyecto.");
            return;
        }
        if(_dirty)
        {
            StudioDialog.Error(Window.GetWindow(this),"Pegar encuentros",
                "Guarda o descarta la edición individual antes de pegar una tabla.");
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
            StatusText.Text="Vista previa lista · confirma antes de aplicar el pegado.";
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
        try{Editor.Apply(proposal);if(Editor is IOptionTemplatePresentation){_loading=true;try{RecordList.ItemsSource=Editor.Entries;ApplyRecordFilter();RecordList.SelectedItem=Editor.Entries.FirstOrDefault(e=>e.Id==_id);}finally{_loading=false;}}Invalidate();InvalidateTrainerSubtitles();RandomizeSteps.SetStep(3);ProposalText.Text="Propuesta guardada y verificada. El historial quedó actualizado.";}
        catch(Exception ex){Invalidate();ProposalText.Text=ex.Message;}
        finally{UiProgress.End();}
    }
}
