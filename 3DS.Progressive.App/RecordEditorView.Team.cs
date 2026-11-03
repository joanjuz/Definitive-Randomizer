using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using ComboBox=System.Windows.Controls.ComboBox;
using Image=System.Windows.Controls.Image;
using Orientation=System.Windows.Controls.Orientation;
using TextBox=System.Windows.Controls.TextBox;
using MenuItem=System.Windows.Controls.MenuItem;
using ContextMenu=System.Windows.Controls.ContextMenu;
using Separator=System.Windows.Controls.Separator;
namespace ThreeDS.Progressive;

// Presentation of the trainer and wild editors: trainer header, live team cards, one Pokémon editor at a time,
// encounter slot cards with their rate or levels and a compact slot table. Values are read from and written to the
// same field controls (FieldControls), so saving, history and templates are unchanged.
public partial class RecordEditorView
{
    private IReadOnlyList<EditorGroup>? _record;
    private int _selectedSlot,_teamId=-1;
    private readonly Dictionary<int,Border> _slotBorders=[];
    private readonly List<(int Slot,Button Card)> _teamCards=[];
    private System.Windows.Threading.DispatcherTimer? _cardsTimer;
    internal int SelectedTrainerSlot=>_selectedSlot;

    /// <summary>Current value of a field control (also unsaved edits); null when the control is missing or holds an invalid number.</summary>
    private int? LiveValue(string key)
    {
        if(!_fields.TryGetValue(key,out var control))return null;
        try{return Values(new Dictionary<string,FrameworkElement>{[key]=control})[key];}
        catch(Exception ex) when(ex is ArgumentException or OverflowException or FormatException){return null;}
    }
    private string? LiveDisplay(string key)=>_fields.TryGetValue(key,out var c)&&c is ComboBox {SelectedItem:MoveChoice choice}?Clean(choice.Display):null;
    private static readonly Regex NumberPrefix=new(@"^\s*\d+\s*·\s*",RegexOptions.CultureInvariant);
    /// <summary>«025 · Pikachu» → «Pikachu».</summary>
    private static string Clean(string display)=>NumberPrefix.Replace(display,"");
    private bool UsesTrainerLayout=>Editor is GameTrainerService||Editor is ITrainerEditorPresentation {UsesTrainerLayout:true};

    /// <summary>Cards follow the edits (species, level, item, moves, rates…) without waiting for «Guardar».</summary>
    private void ScheduleCardsRefresh()
    {
        if(_id<0||_record is null||!(UsesTrainerLayout||EncounterSpritePreview.Visibility==Visibility.Visible))return;
        if(_cardsTimer is null)
        {
            _cardsTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
            _cardsTimer.Tick+=(_,_)=>{_cardsTimer.Stop();RefreshCards();};
        }
        _cardsTimer.Stop();_cardsTimer.Start();
    }
    internal void RefreshCards()
    {
        if(_id<0||_record is null)return;
        try{UpdateTrainerTeam(_id,_record);UpdateEncounterSprites(_id,_record);}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.IO.InvalidDataException){StatusText.Text="No se pudo actualizar la vista previa: "+ex.Message;}
    }
    private Border Chip(string text)
    {
        var label=new TextBlock{Text=text,FontSize=12,FontWeight=FontWeights.SemiBold};
        label.SetResourceReference(TextBlock.ForegroundProperty,"TextMain");
        var chip=new Border{CornerRadius=new CornerRadius(6),Padding=new Thickness(10,3,10,4),Margin=new Thickness(0,0,6,6),BorderThickness=new Thickness(1),Child=label};
        chip.SetResourceReference(Border.BackgroundProperty,"Surface");chip.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        return chip;
    }
    private TextBlock Small(string text,bool soft=true,double size=11)
    {
        var block=new TextBlock{Text=text,FontSize=size,TextTrimming=TextTrimming.CharacterEllipsis};
        if(soft)block.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        return block;
    }
    private int TeamCount(IReadOnlyList<EditorGroup> groups)=>Math.Clamp(LiveValue("Count")
        ??groups.FirstOrDefault()?.Fields.FirstOrDefault(f=>f.Key=="Count")?.Value
        ??groups.Count(g=>g.Name.StartsWith("Pokémon ",StringComparison.Ordinal)),0,6);

    private void UpdateTrainerTeam(int id,IReadOnlyList<EditorGroup> groups)
    {
        _trainerSpriteCancel?.Cancel();
        _trainerSpriteCancel?.Dispose();
        _trainerSpriteCancel=new System.Threading.CancellationTokenSource();
        TrainerTeamCards.Children.Clear();_teamCards.Clear();TrainerHeaderChips.Children.Clear();
        if(!UsesTrainerLayout){TrainerTeamPreview.Visibility=Visibility.Collapsed;return;}
        TrainerTeamPreview.Visibility=Visibility.Visible;
        if(id!=_teamId){_teamId=id;_selectedSlot=0;}
        int count=TeamCount(groups);
        // Header: who the trainer is and what kind of battle it is.
        TrainerHeaderTitle.Text=Editor.Entries.FirstOrDefault(e=>e.Id==id)?.Name??"";
        TrainerHeaderSubtitle.Text=LiveDisplay("Class") is string trainerClass?"Clase: "+trainerClass:"";
        TrainerHeaderSubtitle.Visibility=TrainerHeaderSubtitle.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        if(LiveDisplay("Mode") is string mode)TrainerHeaderChips.Children.Add(Chip("Combate "+mode.ToLowerInvariant()));
        TrainerHeaderChips.Children.Add(Chip(count==1?"1 Pokémon":$"{count} Pokémon"));
        var levels=Enumerable.Range(0,count).Select(s=>LiveValue($"P{s}.Level")).OfType<int>().ToArray();
        if(levels.Length>0)TrainerHeaderChips.Children.Add(Chip(levels.Min()==levels.Max()?$"Nv. {levels.Min()}":$"Nv. {levels.Min()}–{levels.Max()}"));
        int items=Enumerable.Range(0,4).Count(j=>LiveValue("Item"+j) is >0);
        if(items>0)TrainerHeaderChips.Children.Add(Chip(items==1?"1 objeto de combate":$"{items} objetos de combate"));
        if(LiveValue("Healer")==1)TrainerHeaderChips.Children.Add(Chip("Cura tras el combate"));
        for(int slot=0;slot<count;slot++)
        {
            string prefix=$"P{slot}.";
            if(LiveValue(prefix+"Species") is not int species)continue;
            int form=LiveValue(prefix+"Form")??0,level=LiveValue(prefix+"Level")??1;
            string name=LiveDisplay(prefix+"Species")??$"Pokémon #{species}";
            var picture=new Image{Width=56,Height=56,Stretch=Stretch.Uniform};
            RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.NearestNeighbor);
            var fallback=new TextBlock{Text="#"+species.ToString("000",CultureInfo.InvariantCulture),FontSize=13,FontWeight=FontWeights.SemiBold,
                HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            fallback.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
            var art=new Grid{Width=58,Height=58,Margin=new Thickness(0,0,8,0)};art.Children.Add(fallback);art.Children.Add(picture);
            var identity=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            identity.Children.Add(new TextBlock{Text=name,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis});
            identity.Children.Add(Small($"Nv. {level}"+(form>0?$" · Forma {form}":""),true,12));
            if(LiveValue(prefix+"Item") is >0&&LiveDisplay(prefix+"Item") is string item)identity.Children.Add(Small("◆ "+item));
            var top=new DockPanel();DockPanel.SetDock(art,Dock.Left);top.Children.Add(art);top.Children.Add(identity);
            var moves=new StackPanel{Margin=new Thickness(2,6,0,0)};
            for(int j=0;j<4;j++)
                if(LiveValue(prefix+"Move"+j) is >0&&LiveDisplay(prefix+"Move"+j) is string move)moves.Children.Add(Small("· "+move));
            if(moves.Children.Count==0)moves.Children.Add(Small("Movimientos por nivel"));
            var content=new StackPanel();content.Children.Add(top);content.Children.Add(moves);
            var card=new Button{Content=content,Width=184,Margin=new Thickness(0,4,8,8),Padding=new Thickness(8),
                HorizontalContentAlignment=System.Windows.HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
            System.Windows.Automation.AutomationProperties.SetName(card,$"Pokémon {slot+1}: {name}, nivel {level}");
            // The editor heading follows the slot too («Pokémon 3 · Pikachu»), also for a slot just added to the team.
            if(_editorSections.TryGetValue(prefix+"Species",out var slotEditor))slotEditor.Header=$"Pokémon {slot+1} · {name} · Nv. {level}";
            int current=slot;
            card.Click+=(_,_)=>SelectTrainerSlot(current);
            AttachPokemonMenu(card,current,count,name);
            TrainerTeamCards.Children.Add(card);_teamCards.Add((slot,card));
            _=LoadTeamSpriteAsync(picture,fallback,id,species,form,_trainerSpriteCancel.Token);
        }
        // A trainer can have up to six Pokémon: the free slots are added from here instead of showing six editors.
        if(count<6&&_fields.ContainsKey("Count")&&_fields.ContainsKey($"P{count}.Species"))
        {
            var add=new Button{Width=184,MinHeight=96,Margin=new Thickness(0,4,8,8),
                Content=new TextBlock{Text="+ Añadir Pokémon",FontWeight=FontWeights.SemiBold,HorizontalAlignment=System.Windows.HorizontalAlignment.Center},
                ToolTip="Sube «Pokémon en el equipo» y abre la ranura nueva. Se guarda con «Guardar cambios»."};
            EditorPresentation.StyleAction(add,EditorPresentation.ActionRole.Tertiary);
            add.BorderThickness=new Thickness(1);add.SetResourceReference(BorderBrushProperty,"Stroke");
            System.Windows.Automation.AutomationProperties.SetName(add,"Añadir un Pokémon al equipo");
            int next=count;
            add.Click+=(_,_)=>
            {
                SetControlValue(_fields["Count"],next+1);_selectedSlot=next;
                RefreshCards();SelectTrainerSlot(next);
            };
            // Right click on the free slot: paste the copied Pokémon as a new team member.
            var pasteNew=MenuEntry("Pegar como nuevo Pokémon","Ctrl+V",()=>PasteAsNewPokemon(next));
            add.ContextMenu=new ContextMenu{Items={pasteNew}};
            add.ContextMenuOpening+=(_,_)=>pasteNew.IsEnabled=CanPastePokemon;
            add.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.V&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control&&CanPastePokemon){e.Handled=true;PasteAsNewPokemon(next);}};
            TrainerTeamCards.Children.Add(add);
        }
        if(count==0&&TrainerTeamCards.Children.Count==0)
            TrainerTeamCards.Children.Add(new TextBlock{Text="Este entrenador no tiene Pokémon en el equipo.",
                Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(6,12,6,12)});
        ApplyTrainerSlots(count);
    }
    internal bool CanPastePokemon=>_copiedPokemon?.Owner==PokemonClipboardOwner;
    private static MenuItem MenuEntry(string header,string? gesture,Action run)
    {
        var item=new MenuItem{Header=header,InputGestureText=gesture??""};
        item.Click+=(_,_)=>run();return item;
    }
    /// <summary>Copiar · Cortar · Pegar · Eliminar on a team card, also with Ctrl+C / Ctrl+X / Ctrl+V / Supr while it has focus.</summary>
    private void AttachPokemonMenu(Button card,int slot,int count,string name)
    {
        string prefix=$"P{slot}.",label=$"Pokémon {slot+1} · {name}";
        var copy=MenuEntry("Copiar","Ctrl+C",()=>CopyTrainerPokemon(_fields,prefix,label));
        var cut=MenuEntry("Cortar","Ctrl+X",()=>CutTrainerPokemon(slot,label));
        var paste=MenuEntry("Pegar","Ctrl+V",()=>{PasteTrainerPokemon(_fields,prefix,slot,label);RefreshCards();SelectTrainerSlot(slot);});
        var delete=MenuEntry("Eliminar del equipo","Supr",()=>DeleteTrainerPokemon(slot));
        var menu=new ContextMenu{Items={copy,cut,paste,new Separator(),delete}};
        System.Windows.Automation.AutomationProperties.SetName(menu,"Acciones de "+label);
        card.ContextMenu=menu;
        // The paste entry follows the clipboard; a trainer keeps at least one Pokémon.
        card.ContextMenuOpening+=(_,_)=>{paste.IsEnabled=CanPastePokemon;delete.IsEnabled=cut.IsEnabled=count>1;};
        card.PreviewKeyDown+=(_,e)=>
        {
            bool control=System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control;
            if(control&&e.Key==System.Windows.Input.Key.C){e.Handled=true;CopyTrainerPokemon(_fields,prefix,label);}
            else if(control&&e.Key==System.Windows.Input.Key.X&&count>1){e.Handled=true;CutTrainerPokemon(slot,label);}
            else if(control&&e.Key==System.Windows.Input.Key.V&&CanPastePokemon){e.Handled=true;paste.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));}
            else if(e.Key==System.Windows.Input.Key.Delete&&count>1){e.Handled=true;DeleteTrainerPokemon(slot);}
        };
        card.ToolTip=$"{label} · clic derecho: copiar, cortar, pegar o eliminar";
    }
    internal void CutTrainerPokemon(int slot,string label)
    {
        CopyTrainerPokemon(_fields,$"P{slot}.",label);
        if(DeleteTrainerPokemon(slot))StatusText.Text=$"Cortado: {label}. Pégalo con clic derecho en otra tarjeta. Cambios sin guardar.";
    }
    internal void PasteAsNewPokemon(int slot)
    {
        if(!CanPastePokemon||!_fields.ContainsKey("Count"))return;
        SetControlValue(_fields["Count"],slot+1);
        PasteTrainerPokemon(_fields,$"P{slot}.",slot,$"Pokémon {slot+1}");
        _selectedSlot=slot;RefreshCards();SelectTrainerSlot(slot);
    }
    /// <summary>Removes a team member: the following Pokémon move up one slot and «Pokémon en el equipo» goes down by one.</summary>
    internal bool DeleteTrainerPokemon(int slot)
    {
        if(_record is null||!_fields.ContainsKey("Count"))return false;
        int count=TeamCount(_record);
        if(slot>=count||count<=1){StatusText.Text="Un entrenador necesita al menos un Pokémon.";return false;}
        try
        {
            for(int s=slot;s<count-1;s++)
            {
                string from=$"P{s+1}.",to=$"P{s}.";
                var values=Values(_fields.Where(p=>p.Key.StartsWith(from,StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value));
                foreach(var (key,value) in values)
                    if(_fields.TryGetValue(to+key[from.Length..],out var target))SetControlValue(target,value);
            }
        }
        catch(ArgumentException ex){StatusText.Text="No se pudo eliminar: "+ex.Message;return false;}
        SetControlValue(_fields["Count"],count-1);
        _selectedSlot=Math.Min(slot,count-2);RefreshCards();SelectTrainerSlot(_selectedSlot);
        StatusText.Text=$"Pokémon {slot+1} eliminado del equipo. Cambios sin guardar.";
        return true;
    }
    /// <summary>Opens the editor of one team slot; the other slot editors stay hidden (their values are kept).</summary>
    internal void SelectTrainerSlot(int slot)
    {
        _selectedSlot=slot;
        ApplyTrainerSlots(_record is null?6:TeamCount(_record));
        string key=$"P{slot}.Species";
        if(_editorSections.TryGetValue(key,out var section))section.IsExpanded=true;
        if(_pokemonTabs.TryGetValue(slot,out var tabs))tabs.SelectedIndex=0;
        if(_slotBorders.TryGetValue(slot,out var border))border.BringIntoView();
    }
    private void ApplyTrainerSlots(int count)
    {
        if(_slotBorders.Count==0)return;
        if(_selectedSlot>=count)_selectedSlot=Math.Max(0,count-1);
        foreach(var (slot,border) in _slotBorders)
            border.Visibility=count>0&&slot==_selectedSlot?Visibility.Visible:Visibility.Collapsed;
        foreach(var (slot,card) in _teamCards)
        {
            bool selected=slot==_selectedSlot;
            card.BorderThickness=new Thickness(selected?2:1);
            card.SetResourceReference(BorderBrushProperty,selected?"Accent":"InputStroke");
            System.Windows.Automation.AutomationProperties.SetItemStatus(card,selected?"Seleccionado":"");
        }
    }

    private void UpdateEncounterSprites(int id,IReadOnlyList<EditorGroup> groups)
    {
        EncounterSpriteCards.Children.Clear();
        EncounterPairCards.Children.Clear();
        bool wild=Editor is GameEncounterService {SupportsWildTemplate:true};
        WildPreviewControls.Visibility=wild?Visibility.Visible:Visibility.Collapsed;
        EncounterPairPreview.Visibility=Visibility.Collapsed;
        if(Editor is not GameEncounterService && Editor is not GameStaticEncounterService && Editor is not IWildEditorPresentation {UsesWildLayout:true})
        {
            EncounterSpritePreview.Visibility=Visibility.Collapsed;
            return;
        }
        _trainerSpriteCancel??=new System.Threading.CancellationTokenSource();
        var saved=groups.SelectMany(g=>g.Fields).GroupBy(f=>f.Key).ToDictionary(g=>g.Key,g=>g.First().Value);
        int? Current(string key)=>LiveValue(key)??(saved.TryGetValue(key,out int v)?v:null);
        EditorGroup? section;int selected=0;
        if(wild)
        {
            if(EncounterGroupPicker.SelectedIndex<0)EncounterGroupPicker.SelectedIndex=0;
            selected=Math.Clamp(EncounterGroupPicker.SelectedIndex,0,8);
            section=groups.Skip(1).ElementAtOrDefault(selected);
            int min=Current("Min")??0,max=Current("Max")??0;
            int active=section?.Fields.Count(f=>f.Key.EndsWith(".Species",StringComparison.Ordinal)&&(Current(f.Key)??0)>0)??0;
            string summary=$"Niveles {min}–{max} · {active} especies en la fila seleccionada";
            if(selected==0)
            {
                int rate=Enumerable.Range(0,10).Sum(slot=>Current("Rate"+slot)??0);
                summary+=$" · Probabilidades: {rate}%";
                if(rate is not (0 or 100))summary+=" (debe sumar 0% o 100%)";
            }
            EncounterGroupSummary.Text=summary;
        }
        else
            section=groups.FirstOrDefault(g=>g.Fields.Any(f=>f.Key=="Species"||f.Key.EndsWith(".Species",StringComparison.Ordinal)));
        if(section is null){EncounterSpritePreview.Visibility=Visibility.Collapsed;return;}
        EncounterSpritePreview.Visibility=Visibility.Visible;
        EncounterSpriteTitle.Text=wild&&Editor is GameEncounterService w
            ?$"{w.TimeOfDay(id).ToUpperInvariant()} · {section.Name.ToUpperInvariant()}"
            :section.Name.ToUpperInvariant()+" · SPRITES";
        RenderEncounterRow(EncounterSpriteCards,section,id,null,Current,wild&&selected==0);
        if(wild&&Editor is GameEncounterService editor&&editor.PairedDayNightId(id) is int paired)
        {
            var other=editor.Read(paired);
            var alternate=other.Skip(1).ElementAtOrDefault(selected);
            if(alternate is not null)
            {
                var values=other.SelectMany(g=>g.Fields).GroupBy(f=>f.Key).ToDictionary(g=>g.Key,g=>g.First().Value);
                EncounterPairPreview.Visibility=Visibility.Visible;
                EncounterPairTitle.Text=$"{editor.TimeOfDay(paired).ToUpperInvariant()} · {alternate.Name} · tabla #{paired}";
                RenderEncounterRow(EncounterPairCards,alternate,id,paired,key=>values.TryGetValue(key,out int v)?v:null,selected==0);
            }
        }
    }

    /// <summary>One card per slot (also empty ones, so the positions match the table): sprite, species and its rate or levels.</summary>
    private void RenderEncounterRow(WrapPanel host,EditorGroup section,int activeId,int? pairedId,Func<string,int?> value,bool showRates)
    {
        var allFields=section.Fields;
        var entries=allFields.Where(f=>f.Key=="Species"||f.Key.EndsWith(".Species",StringComparison.Ordinal)).Take(12).ToArray();
        for(int position=0;position<entries.Length;position++)
        {
            var field=entries[position];
            string prefix=field.Key.EndsWith(".Species",StringComparison.Ordinal)?field.Key[..^7]:string.Empty;
            int species=value(field.Key)??field.Value;
            int form=value(prefix+"Form")??0;
            var picture=new Image{Width=52,Height=52,Stretch=Stretch.Uniform,HorizontalAlignment=System.Windows.HorizontalAlignment.Center};
            RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.NearestNeighbor);
            var fallback=new TextBlock{Text=species==0?"—":"#"+species.ToString("000",CultureInfo.InvariantCulture),FontSize=species==0?17:12,
                Foreground=(Brush)FindResource("TextSoft"),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=System.Windows.HorizontalAlignment.Center};
            var art=new Grid{Height=56};
            art.Children.Add(fallback);art.Children.Add(picture);
            var content=new StackPanel();
            content.Children.Add(new TextBlock{Text=$"#{position+1:00}",FontSize=10,Foreground=(Brush)FindResource("TextSoft"),TextAlignment=TextAlignment.Center});
            content.Children.Add(art);
            string label=species==0?"Vacío":pairedId is null&&LiveDisplay(field.Key) is string live?live
                :Clean(field.Choices?.FirstOrDefault(choice=>choice.Id==species)?.Display??$"#{species}");
            content.Children.Add(new TextBlock{Text=label,Width=95,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,FontSize=11,
                FontWeight=species==0?FontWeights.Normal:FontWeights.SemiBold});
            // Rate of the main table (Gen7) or the level range of the slot (Gen6), the two things a player notices in game.
            string? detail=showRates&&value("Rate"+position) is int rate?$"{rate}%"
                :value(prefix+"Min") is int min&&value(prefix+"Max") is int max?(min==max?$"Nv. {min}":$"Nv. {min}–{max}"):null;
            if(form>0)detail=(detail is null?"":detail+" · ")+$"Forma {form}";
            if(detail is not null)
                content.Children.Add(new TextBlock{Text=detail,FontSize=10,Foreground=(Brush)FindResource("TextSoft"),TextAlignment=TextAlignment.Center});
            var card=new Button{Content=content,Width=104,Margin=new Thickness(0,0,8,8),Padding=new Thickness(3),Opacity=species==0?.6:1,
                ToolTip=pairedId.HasValue?"Abrir esta ranura en la tabla del otro horario":"Ir a la especie y forma de esta ranura"};
            System.Windows.Automation.AutomationProperties.SetName(card,$"Ranura {position+1}: {label}"+(detail is null?"":", "+detail));
            string key=field.Key;
            card.Click+=(_,_)=>
            {
                if(pairedId.HasValue)
                {
                    RecordList.SelectedItem=Editor.Entries.FirstOrDefault(x=>x.Id==pairedId.Value);
                    if(_id!=pairedId.Value)return;
                }
                if(_editorSections.TryGetValue(key,out var expand))expand.IsExpanded=true;
                if(_fields.TryGetValue(key,out var control)){control.BringIntoView();control.Focus();}
            };
            host.Children.Add(card);
            if(species>0&&_trainerSpriteCancel is not null)
                _=LoadTeamSpriteAsync(picture,fallback,activeId,species,form,_trainerSpriteCancel.Token);
        }
    }

    /// <summary>Creates the input of one field and registers it (same control types, events and accessibility as before).</summary>
    private FrameworkElement CreateFieldInput(EditorField field,Dictionary<string,FrameworkElement> controls,bool editor,bool isCheck)
    {
        FrameworkElement input;
        if(field.Choices is { } choices)
        {
            if(!choices.Any(c=>c.Id==field.Value))
                choices=choices.Append(new MoveChoice(field.Value,$"ID {field.Value} (personalizado)")).ToArray();
            var combo=new ComboBox{ItemsSource=choices,DisplayMemberPath="Display",SelectedValuePath="Id",SelectedValue=field.Value,MaxDropDownHeight=300};
            combo.SelectionChanged+=Input_Changed;input=combo;
        }
        else if(isCheck)
        {
            // Long labels go on several lines in the narrow options column instead of being cut.
            var check=new CheckBox{IsChecked=field.Value==1,Content=field.Label,Margin=new Thickness(0,7,0,7)};
            check.SetResourceReference(ContentControl.ContentTemplateProperty,"WrappedOptionContent");
            check.Checked+=Input_Changed;check.Unchecked+=Input_Changed;input=check;
        }
        else
        {
            var edit=new TextBox{Text=(field.Value/(decimal)field.Scale).ToString(CultureInfo.InvariantCulture)};
            edit.TextChanged+=Input_Changed;input=edit;
        }
        input.Tag=editor;input.Uid=field.Scale.ToString(CultureInfo.InvariantCulture);
        controls.Add(field.Key,input);
        System.Windows.Automation.AutomationProperties.SetName(input,field.Label);
        if(!isCheck&&field.Choices is null)
            System.Windows.Automation.AutomationProperties.SetHelpText(input,$"{field.Min/(decimal)field.Scale} – {field.Max/(decimal)field.Scale}");
        return input;
    }

    private static readonly Regex SlotKey=new(@"^S\d+\.(\d+\.)?Species$",RegexOptions.CultureInvariant);
    private static bool IsEncounterSlotGroup(EditorGroup group)=>group.Fields.Count>0&&group.Fields.Any(f=>SlotKey.IsMatch(f.Key))
        &&group.Fields.All(f=>f.Key.StartsWith('S')&&f.Key.Contains('.'));
    /// <summary>
    /// Wild slots as a table: one row per slot with species, form and its rate (Gen7 main table) or levels (Gen6),
    /// instead of two loose fields per slot in a two-column form.
    /// </summary>
    private Border BuildEncounterSlotTable(EditorGroup group,Dictionary<string,FrameworkElement> controls,IReadOnlyList<EditorField> rates)
    {
        var slots=group.Fields.Where(f=>f.Key.EndsWith(".Species",StringComparison.Ordinal)).Select(f=>f.Key[..^7]).ToArray();
        var byKey=group.Fields.ToDictionary(f=>f.Key);
        bool levels=slots.Any(s=>byKey.ContainsKey(s+"Min"));
        var grid=new Grid{Margin=new Thickness(0,10,0,0)};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(34)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=150});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(74)});
        string[] extra=levels?["Nv. mín","Nv. máx"]:rates.Count>0?["Prob. %"]:[];
        foreach(var _ in extra)grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(80)});
        grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        string[] headers=["#","Especie","Forma",..extra];
        for(int c=0;c<headers.Length;c++)
        {
            var header=new TextBlock{Text=headers[c],FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,8,6)};
            header.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");Grid.SetColumn(header,c);grid.Children.Add(header);
        }
        var expand=new Expander{Header=group.Name,IsExpanded=group.Expanded,Foreground=(Brush)FindResource("TextMain"),Content=grid};
        void Add(EditorField field,int row,int column)
        {
            var input=CreateFieldInput(field,controls,true,false);
            input.Margin=new Thickness(0,0,8,6);
            if(input is ComboBox combo)combo.SetResourceReference(StyleProperty,"GridComboBox");
            if(input is TextBox box)box.SetResourceReference(StyleProperty,"GridTextBox");
            Grid.SetRow(input,row);Grid.SetColumn(input,column);grid.Children.Add(input);_editorSections[field.Key]=expand;
        }
        for(int i=0;i<slots.Length;i++)
        {
            int row=i+1;grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var number=new TextBlock{Text=(i+1).ToString(CultureInfo.InvariantCulture),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,0,8,6)};
            number.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");Grid.SetRow(number,row);grid.Children.Add(number);
            Add(byKey[slots[i]+"Species"],row,1);
            if(byKey.TryGetValue(slots[i]+"Form",out var form))Add(form,row,2);
            if(levels)
            {
                if(byKey.TryGetValue(slots[i]+"Min",out var min))Add(min,row,3);
                if(byKey.TryGetValue(slots[i]+"Max",out var max))Add(max,row,4);
            }
            else if(i<rates.Count)Add(rates[i],row,3);
        }
        // Fields outside the slot pattern keep the generic form, so nothing the service exposes is lost.
        var rest=group.Fields.Where(f=>!controls.ContainsKey(f.Key)).ToArray();
        if(rest.Length>0){var stack=new StackPanel();expand.Content=stack;stack.Children.Add(grid);stack.Children.Add(BuildFieldsGrid(rest,controls,true));foreach(var f in rest)_editorSections[f.Key]=expand;}
        return GroupBorder(expand);
    }
}
