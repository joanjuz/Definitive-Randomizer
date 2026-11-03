using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;

/// <summary>
/// Navigation and hierarchy redesign (02/11/2026): grouped sidebar, header emphasis, breadcrumbs of hub tools, search palette,
/// "modified" marks, Inicio status, compact option rows, randomization steps and grouped inspector. Off-screen, not interactive.
/// </summary>
internal static class NavigationRedesignChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
    private static void Layout(FrameworkElement content,double w,double h){content.Measure(new Size(w,h));content.Arrange(new Rect(0,0,w,h));content.UpdateLayout();Pump();content.UpdateLayout();Pump();}
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var d in Descendants<T>(child))yield return d;}}
    private static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Pump();}
    private static void Capture(FrameworkElement content,int width,int height,string name)
    {
        Layout(content,width,height);var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(content);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));string folder=Path.Combine(AppContext.BaseDirectory,"visual-redesign");
        Directory.CreateDirectory(folder);using var output=File.Create(Path.Combine(folder,name+".png"));png.Save(output);
    }
    private static bool Active(Button button)=>button.Background is SolidColorBrush brush&&brush.Color.A>0;
    internal static void Run(Action<bool,string> check)
    {
        using var f=new Fixture(pk3DS.Core.GameVersion.USUM);ModuleChecks.CreateArchives(f);
        var main=new MainWindow();var root=(FrameworkElement)main.Content;
        T Find<T>(string name)=>(T)main.FindName(name);
        main.LoadProject(f.Session);Layout(root,1280,800);
        var section=Find<ContentControl>("SectionHost");

        // Sidebar groups and modes.
        string[] headers=["NavHeaderSpecies","NavHeaderWorld","NavHeaderText","NavHeaderPatches","NavHeaderRecipes"];
        check(headers.All(h=>Find<ToggleButton>(h).Visibility==Visibility.Visible&&Find<ToggleButton>(h).IsChecked==true),"Nav_FullMode_ShowsEveryGroupExpanded");
        check(Find<Button>("AutomationButton").Visibility==Visibility.Collapsed&&Find<Button>("AutomationButton").Tag as string=="Automation","Nav_Automation_HiddenButStillARoute");
        main.ChooseExperience(true);Pump();
        check(Find<ToggleButton>("NavHeaderText").Visibility==Visibility.Collapsed&&Find<ToggleButton>("NavHeaderPatches").Visibility==Visibility.Collapsed
            &&Find<ToggleButton>("NavHeaderSpecies").Visibility==Visibility.Visible,"Nav_SimplifiedMode_HidesEmptyGroupHeadings");
        check(Find<Button>("AutomationButton").Visibility==Visibility.Collapsed,"Nav_SimplifiedMode_KeepsAutomationHidden");
        main.ChooseExperience(false);Pump();
        check(Find<ToggleButton>("NavHeaderText").Visibility==Visibility.Visible&&Find<Button>("AutomationButton").Visibility==Visibility.Collapsed,"Nav_FullMode_RestoresGroupsWithoutAutomation");
        var worldHeader=Find<ToggleButton>("NavHeaderWorld");worldHeader.IsChecked=false;Pump();
        check(Find<StackPanel>("NavGroupWorld").Visibility==Visibility.Collapsed,"Nav_GroupHeader_CollapsesItsSections");

        // Header emphasis with a project open.
        check(ReferenceEquals(Find<Button>("RebuildButton").Style,root.FindResource("PrimaryButton"))&&ReferenceEquals(Find<Button>("OpenProjectButton").Style,root.FindResource("SecondaryButton")),"Header_ProjectOpen_RebuildIsThePrimaryAction");
        check(Find<Button>("ExtractRomButton").Visibility==Visibility.Collapsed,"Header_ProjectOpen_HidesExtraction");

        // Highlight, group auto-expand and breadcrumbs.
        Click(Find<Button>("TrainersButton"));
        check(worldHeader.IsChecked==true&&Active(Find<Button>("TrainersButton"))&&!Active(Find<Button>("PokemonNavButton")),"Nav_OpeningASection_ExpandsItsGroupAndMarksIt");
        check(section.Tag is null,"Breadcrumb_SidebarSection_HasNone");
        main.NavigateTo("Rebuild");Pump();
        var buttons=new[]{"PokemonNavButton","MovesButton","TrainersButton","MechanicsButton","RomToolsButton","HistoryButton"}.Select(Find<Button>).ToArray();
        check(Active(Find<Button>("RomToolsButton"))&&buttons.Where(b=>b.Name!="RomToolsButton").All(b=>!Active(b)),"Nav_HubTool_MarksItsHubOnly");
        check(section.Tag as string=="Herramientas ROM"&&Breadcrumbs.GetCurrent(section)=="Rebuild","Breadcrumb_HubTool_ShowsHubAndTool");
        Layout(root,1280,800);
        var back=section.Template.FindName("BreadcrumbBack",section) as Button;
        check(back is not null&&section.Template.FindName("Breadcrumb",section) is Border {Visibility:Visibility.Visible},"Breadcrumb_HubTool_BackButtonVisible");
        if(back is not null)Click(back);
        check(section.Content is SystemsView&&section.Tag is null&&Active(Find<Button>("RomToolsButton")),"Breadcrumb_Back_ReturnsToTheHub");
        Click(Find<Button>("AutomationButton"));
        check(section.Content is SystemsView,"Nav_Automation_RouteStillOpensItsHub");

        // Search palette.
        main.OpenCommandPalette();Pump();
        var palette=main.Palette;
        check(palette is not null&&Find<Grid>("PaletteHost").Visibility==Visibility.Visible,"Palette_Opens");
        if(palette is not null)
        {
            Layout(root,1280,800);Capture(root,1280,800,"navigation-palette");
            var all=palette.Results.Items.Count;
            palette.Query.Text="shiny";Pump();
            check(palette.Results.Items.Count>0&&palette.Results.Items[0] is PaletteEntry {Title:var t,Location:"Herramientas Pokémon"}&&t.Contains("Shiny",StringComparison.Ordinal),"Palette_FindsHubToolAndSaysWhereItIs");
            palette.Query.Text="estaticos";Pump();
            check(palette.Results.Items.Cast<PaletteEntry>().Any(e=>e.Title=="Encuentros estáticos"),"Palette_IgnoresAccents");
            palette.Query.Text="zzzz-sin-resultado";Pump();
            check(palette.Results.Items.Count==0,"Palette_NoMatchShowsEmptyState");
            palette.Query.Text="";Pump();
            check(palette.Results.Items.Count==all&&all>30,"Palette_EmptyQueryListsEveryDestination");
            palette.Query.Text="rebuild";Pump();palette.RunSelected();Pump();
            check(main.Palette is null&&Find<Grid>("PaletteHost").Visibility==Visibility.Collapsed&&section.Content is RomRebuildView&&section.Tag as string=="Herramientas ROM","Palette_EnterOpensTheToolAndCloses");
        }
        check(CommandPalette.Search([new("Tabla de tipos","Herramientas Pokémon","","efectividad",()=>{}),new("Tipos","Otro","","",()=>{})],"tipos").First().Title=="Tipos","Palette_TitleMatchesRankFirst");

        // "Modified" marks and Inicio status, from the real history.
        string probe=Path.Combine(f.Root,"badge-probe.bin");File.WriteAllBytes(probe,[1]);
        f.Session.History.Commit([new FileChange(probe,[1],[2])],"Entrenadores","Randomizar entrenadores",[new(1,"Uno","Valor","1","2")],new Dictionary<string,string>{["ActionId"]="trainers.randomize"});Pump();Pump();
        check(NavBadge.GetCount(Find<Button>("TrainersButton"))==1&&NavBadge.GetHasChanges(Find<Button>("TrainersButton"))&&NavBadge.GetCount(Find<Button>("EncountersButton"))==0,"Badge_CommitMarksItsSection");
        check((System.Windows.Automation.AutomationProperties.GetHelpText(Find<Button>("TrainersButton"))??"").Contains("1 cambio guardado",StringComparison.Ordinal),"Badge_IsAnnouncedToScreenReaders");
        Click(Find<Button>("BrandHomeButton"));Layout(root,1280,800);
        var home=Find<ContentControl>("HomeHost").Content as FrameworkElement;
        var texts=home is null?[]:Descendants<TextBlock>(home).Select(t=>t.Text).ToArray();
        check(texts.Any(t=>t.StartsWith("1 cambio guardado",StringComparison.Ordinal)),"Home_StatusCountsSavedChanges");
        check(home is not null&&Descendants<Button>(home).Any(b=>b.Content as string=="Entrenadores · 1"),"Home_StatusLinksToModifiedSection");
        if(home is not null)Capture(root,1280,800,"navigation-home-status");
        SessionMilestones.MarkBackup(f.Session);Pump();Pump();Layout(root,1280,800);
        home=Find<ContentControl>("HomeHost").Content as FrameworkElement;
        check(home is not null&&Descendants<TextBlock>(home).Any(t=>t.Inlines.OfType<System.Windows.Documents.Run>().Any(r=>r.Text.Contains("al día",StringComparison.Ordinal))),"Home_StatusShowsBackupUpToDate");

        // Saved changes of the open section, in the section bar.
        Click(Find<Button>("TrainersButton"));Layout(root,1280,800);
        check(SectionChanges.GetSummary(section)=="1 cambio guardado"&&section.Template.FindName("ChangesArea",section) is Grid {Visibility:Visibility.Visible},"Changes_SectionBarSummarizesSavedChanges");
        check(SectionChanges.GetItems(section) is IEnumerable<SavedChange> listed&&listed.Single().Title=="Randomizar entrenadores","Changes_SectionBarListsWhatWasDone");
        check((Find<Button>("TrainersButton").ToolTip as string??"").Contains("Randomizar entrenadores",StringComparison.Ordinal),"Changes_SidebarTooltipSaysWhatChanged");
        VerifyChangeMapping(check);
        VerifyTrainerAndWild(check,f);
        VerifyAppliedOptionsAndEditors(check);

        // Option rows keep their own height (FormGrid) instead of the tallest field of the form.
        var form=new FormGrid{Columns=2};
        form.Children.Add(new Border{Height=200});form.Children.Add(new Border{Height=20});
        var third=new Border{Height=20};form.Children.Add(third);form.Children.Add(new Border{Height=20});
        Layout(form,400,600);
        check(Math.Abs(form.DesiredSize.Height-220)<.5&&Math.Abs(third.TranslatePoint(new Point(),form).Y-200)<.5,"FormGrid_RowsAreAsTallAsTheirContent");
        check(form is UniformGrid,"FormGrid_KeepsTheAdaptiveOneColumnHook");

        // Randomization steps, manual save bar and grouped inspector.
        var trainers=new RecordEditorView(new GameTrainerService(f.Session));Layout(trainers,1100,700);
        var steps=(StepIndicator)trainers.FindName("RandomizeSteps");
        typeof(RecordEditorView).GetMethod("Randomizer_Click",Private)!.Invoke(trainers,[trainers,new RoutedEventArgs()]);Layout(trainers,1100,700);
        check(steps.Current==0&&((FrameworkElement)trainers.FindName("ManualSaveBar")).Visibility==Visibility.Collapsed,"Steps_RandomizerStartsAtConfigureAndHidesManualSave");
        var apply=(Button)trainers.FindName("ApplyButton");apply.IsEnabled=true;check(steps.Current==2,"Steps_PreviewWithChangesMovesToApply");
        typeof(RecordEditorView).GetMethod("Invalidate",Private)!.Invoke(trainers,null);check(steps.Current==0&&!apply.IsEnabled,"Steps_ChangingOptionsReturnsToConfigure");
        typeof(RecordEditorView).GetMethod("Back_Click",Private)!.Invoke(trainers,[trainers,new RoutedEventArgs()]);Layout(trainers,1100,700);
        check(((FrameworkElement)trainers.FindName("ManualSaveBar")).Visibility==Visibility.Visible,"Steps_BackToEditorRestoresManualSave");
        check(((FrameworkElement)trainers.FindName("RulesGroup")).Visibility==Visibility.Visible&&ReferenceEquals(((Button)trainers.FindName("RandomizerButton")).Style,trainers.FindResource("PrimaryButton")),"Inspector_Trainers_PrimaryActionAndRulesGroup");
        var evolutions=new RecordEditorView(new GameEvolutionService(f.Session));Layout(evolutions,1100,700);
        check(((FrameworkElement)evolutions.FindName("RulesGroup")).Visibility==Visibility.Collapsed&&((FrameworkElement)evolutions.FindName("TemplatesGroup")).Visibility==Visibility.Visible
            &&((FrameworkElement)evolutions.FindName("MoreToolsGroup")).Visibility==Visibility.Collapsed,"Inspector_EmptyGroupsAreHidden");
        var search=(TextBox)evolutions.FindName("SearchText");
        check(InputHint.GetText(search)=="Nombre o número"&&search.Template.FindName("HintText",search) is TextBlock {Visibility:Visibility.Visible},"InputHint_ShownWhileEmpty");
        search.Text="1";Layout(evolutions,1100,700);
        check(search.Template.FindName("HintText",search) is TextBlock {Visibility:Visibility.Collapsed},"InputHint_HiddenWithText");

        // Wrapped tabs keep their row when selected (WrapPanel instead of TabPanel).
        typeof(RecordEditorView).GetMethod("Randomizer_Click",Private)!.Invoke(trainers,[trainers,new RoutedEventArgs()]);Layout(trainers,1100,700);
        var tabs=Descendants<TabControl>(trainers).FirstOrDefault(t=>t.Items.Count==5);
        if(tabs is not null)
        {
            var first=(TabItem)tabs.ItemContainerGenerator.ContainerFromIndex(0);double before=first.TranslatePoint(new Point(),tabs).Y;
            tabs.SelectedIndex=4;Layout(trainers,1100,700);
            check(Math.Abs(first.TranslatePoint(new Point(),tabs).Y-before)<.5,"Tabs_SelectingAWrappedTabDoesNotReorderRows");
        }
        else check(false,"Tabs_TrainerOptionsHaveFiveTabs");

        // Hubs: cards in columns, plain description first, technical details last and collapsed.
        var hub=new SystemsView(f.Session,"Mechanics",_=>{},()=>{},()=>{});Layout(hub,1000,700);
        var cards=Descendants<TileGrid>(hub).Single().Children.OfType<Border>().Where(c=>c.Visibility==Visibility.Visible).ToArray();
        check(cards.Length>=2&&Math.Abs(cards[0].TranslatePoint(new Point(),hub).Y-cards[1].TranslatePoint(new Point(),hub).Y)<.5,"Hub_CardsFlowInColumns");
        check(cards.All(c=>c.Child is StackPanel body&&(body.Children.OfType<Expander>().LastOrDefault() is not {} details||ReferenceEquals(body.Children[^1],details)&&!details.IsExpanded)),"Hub_TechnicalDetailsAreLastAndCollapsed");
        Capture(hub,1000,700,"navigation-hub-mechanics");
        main.Close();Pump();
    }
    private static HistoryEntry Entry(string module,string action,string kind="Edit",IReadOnlyDictionary<string,string>? parameters=null,IReadOnlyList<ThreeDS.Progressive.Models.BatchChange>? changes=null,int values=0)=>
        new(Guid.NewGuid(),DateTimeOffset.UtcNow,module,action,changes??[],parameters??new Dictionary<string,string>(),[],kind,values);
    /// <summary>Entries as real projects store them: Global Template action ids, ActionId parameters and damaged accents.</summary>
    private static void VerifyChangeMapping(Action<bool,string> check)
    {
        var document=new HistoryDocument(2,"USUM","p",[
            Entry("Global Template","trainers.randomize","Action",values:120),
            Entry("Global Template","marts.add-rare-candies","Action"),
            Entry("Global Template","Aplicar Global Template · receta","Action"),
            Entry("Encuentros est\uFFFDticos","Editar Encuentros est\uFFFDticos",changes:[new(5,"Tapu Koko","Nivel","60","70")]),
            Entry("Encuentros","Randomizar encuentros","Action",new Dictionary<string,string>{["ActionId"]="wild-encounters.randomize"}),
            Entry("Obediencia","Los Pokémon intercambiados siempre obedecen",parameters:new Dictionary<string,string>{["ActionId"]="player.traded-obedience"})]);
        var changes=SectionProgress.Changes(document);
        check(changes.GetValueOrDefault("Trainers")?.Single() is {Title:"Randomizar entrenadores"} trainers&&trainers.Detail.Contains("Global Template",StringComparison.Ordinal)&&trainers.Detail.Contains("120 valores",StringComparison.Ordinal),"Changes_GlobalTemplateActionMarksItsSection");
        check(changes.GetValueOrDefault("Marts")?.Count==1&&changes.GetValueOrDefault("GlobalTemplate")?.Count==1,"Changes_EveryRecipeActionCounts");
        check(changes.GetValueOrDefault("StaticEncounters")?.Single().Detail.Contains("Tapu Koko",StringComparison.Ordinal)==true&&!changes.ContainsKey("Encounters")==false&&changes["Encounters"].Count==1,"Changes_DamagedAccentsAndActionIdsKeepTheirSection");
        check(changes.GetValueOrDefault("Mechanics")?.Count==1,"Changes_TradedObedienceIsAMechanic");
    }
    /// <summary>Trainer header, one Pokémon editor at a time, live cards, adding a Pokémon, and the wild slot table.</summary>
    private static void VerifyTrainerAndWild(Action<bool,string> check,Fixture f)
    {
        var trainers=new RecordEditorView(new GameTrainerService(f.Session));Layout(trainers,1170,760);
        var slots=(IDictionary<int,Border>)typeof(RecordEditorView).GetField("_slotBorders",Private)!.GetValue(trainers)!;
        var chips=(WrapPanel)trainers.FindName("TrainerHeaderChips");
        check(slots.Count==6&&slots.Values.Count(b=>b.Visibility==Visibility.Visible)==1&&chips.Children.Count>=2,"Trainer_HeaderAndOnePokemonEditorAtATime");
        int count=int.Parse(((TextBox)trainers.FieldControls["Count"]).Text,System.Globalization.CultureInfo.InvariantCulture);
        if(count>=2)
        {
            trainers.SelectTrainerSlot(1);
            check(slots[1].Visibility==Visibility.Visible&&slots[0].Visibility==Visibility.Collapsed&&trainers.SelectedTrainerSlot==1,"Trainer_TeamCardOpensItsSlot");
        }
        ((TextBox)trainers.FieldControls["P0.Level"]).Text="77";trainers.RefreshCards();Layout(trainers,1170,760);
        check(chips.Children.OfType<Border>().Any(c=>c.Child is TextBlock {Text:var t}&&t.Contains("77",StringComparison.Ordinal)),"Trainer_CardsFollowUnsavedEdits");
        ((ScrollViewer)trainers.FindName("FieldsScroll")).ScrollToTop();Capture(trainers,1170,760,"trainers-redesign");
        var cards=(WrapPanel)trainers.FindName("TrainerTeamCards");
        var add=cards.Children.OfType<Button>().FirstOrDefault(b=>b.Content is TextBlock {Text:"+ Añadir Pokémon"});
        check(add is not null,"Trainer_FreeSlotOffersAddPokemon");
        if(add is not null)
        {
            Click(add);
            check(((TextBox)trainers.FieldControls["Count"]).Text==(count+1).ToString(System.Globalization.CultureInfo.InvariantCulture)&&trainers.SelectedTrainerSlot==count&&slots[count].Visibility==Visibility.Visible,"Trainer_AddPokemonRaisesCountAndOpensTheSlot");
        }
        check(slots[count].Child is Expander {Header:string heading}&&heading.StartsWith($"Pokémon {count+1} · ",StringComparison.Ordinal),"Trainer_AddedSlotHeadingFollowsTheTeam");
        Capture(trainers,1170,760,"trainers-redesign-added");
        if(cards.Children.OfType<Button>().FirstOrDefault(b=>b.ContextMenu is not null)?.ContextMenu is System.Windows.Controls.ContextMenu cardMenu)
        {
            check(cardMenu.Items.OfType<System.Windows.Controls.MenuItem>().Select(i=>i.Header as string).SequenceEqual(["Copiar","Cortar","Pegar","Eliminar del equipo"]),"Trainer_CardContextMenuEntries");
            Capture(cardMenu,260,200,"trainer-card-menu");
        }
        // Delete and cut from the card menu: the next Pokémon moves up and the team shrinks by one.
        int before=int.Parse(((TextBox)trainers.FieldControls["Count"]).Text,System.Globalization.CultureInfo.InvariantCulture);
        if(before>=2)
        {
            string secondSpecies=((System.Windows.Controls.ComboBox)trainers.FieldControls["P1.Species"]).SelectedValue?.ToString()??"";
            check(trainers.DeleteTrainerPokemon(0)&&((TextBox)trainers.FieldControls["Count"]).Text==(before-1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                &&((System.Windows.Controls.ComboBox)trainers.FieldControls["P0.Species"]).SelectedValue?.ToString()==secondSpecies,"Trainer_DeleteMovesTheTeamUp");
            trainers.CutTrainerPokemon(0,"Pokémon 1");
            check(trainers.CanPastePokemon,"Trainer_CutKeepsThePokemonToPaste");
        }
        check(!trainers.DeleteTrainerPokemon(9),"Trainer_DeleteOutsideTheTeamDoesNothing");

        var wild=new RecordEditorView(new GameEncounterService(f.Session,System.Threading.CancellationToken.None));Layout(wild,1170,760);
        bool sameRow=wild.FieldControls.TryGetValue("Rate0",out var rate)&&wild.FieldControls.TryGetValue("S0.0.Species",out var species)
            &&ReferenceEquals(rate.Parent,species.Parent)&&Grid.GetRow(rate)==Grid.GetRow(species);
        check(sameRow,"Wild_SlotTablePutsRateNextToSpecies");
        var wildCards=(WrapPanel)wild.FindName("EncounterSpriteCards");
        check(wildCards.Children.OfType<Button>().Any(b=>b.Content is StackPanel p&&p.Children.OfType<TextBlock>().Any(t=>t.Text.EndsWith('%'))),"Wild_CardsShowSlotRate");
        Capture(wild,1170,760,"wild-redesign");
        ((ScrollViewer)wild.FindName("FieldsScroll")).ScrollToVerticalOffset(560);Capture(wild,1170,760,"wild-redesign-table");
    }
    /// <summary>
    /// Applied options are shown again when the project is reopened; trainer list, gender/ability and IV/EV table;
    /// Field Items overview; and editing the text of a move from the move editor.
    /// </summary>
    private static void VerifyAppliedOptionsAndEditors(Action<bool,string> check)
    {
        using var f=new Fixture(pk3DS.Core.GameVersion.USUM);ModuleChecks.CreateArchives(f);
        var trainers=new RecordEditorView(new GameTrainerService(f.Session));Layout(trainers,1170,760);
        check(trainers.FieldControls.TryGetValue("P0.Gender",out var gender)&&gender is System.Windows.Controls.ComboBox&&trainers.FieldControls["P0.Ability"] is System.Windows.Controls.ComboBox,"Trainer_Gen7GenderAndAbilityByName");
        check(trainers.FieldControls.TryGetValue("P0.IV0",out var iv)&&iv.Parent is Grid&&trainers.FieldControls["P0.EV0"].Parent==iv.Parent,"Trainer_IvEvAreOneTablePerStat");
        check(trainers.TrainerSubtitle(trainers.Editor.Entries[0].Id).Contains("Pokémon",StringComparison.Ordinal),"Trainer_ListSaysClassTeamAndLevels");
        // Randomize trainers with a changed option, then reopen the project: the option comes back with its source.
        string key=trainers.OptionControls.Keys.FirstOrDefault(k=>k=="NUD_LevelBoost")??trainers.OptionControls.First(p=>p.Value is TextBox).Key;
        typeof(RecordEditorView).GetMethod("Randomizer_Click",Private)!.Invoke(trainers,[trainers,new RoutedEventArgs()]);
        var box=(TextBox)trainers.OptionControls[key];string changed=box.Text=="137"?"138":"137";box.Text=changed;
        typeof(RecordEditorView).GetMethod("Preview_Click",Private)!.Invoke(trainers,[trainers,new RoutedEventArgs()]);Pump();
        typeof(RecordEditorView).GetMethod("Apply_Click",Private)!.Invoke(trainers,[trainers,new RoutedEventArgs()]);Pump();
        bool applied=f.Session.History.Read().Entries.Any(e=>e.Module=="Entrenadores"&&e.Parameters.ContainsKey("Controls"));
        check(applied,"Applied_TrainerRandomizationSavesItsOptions");
        var main=new MainWindow();
        main.LoadProject(f.Session);Pump();
        ((Button)main.FindName("TrainersButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Pump();
        var reopened=((ContentControl)main.FindName("SectionHost")).Content as RecordEditorView;
        check(reopened is not null&&reopened.OptionControls.TryGetValue(key,out var restored)&&restored is TextBox {Text:var text}&&text==changed,"Applied_ReopenedRandomizerShowsAppliedOption");
        check(reopened?.FindName("OptionsSourceNote") is Border {Visibility:Visibility.Visible},"Applied_RandomizerSaysWhereOptionsComeFrom");
        main.Close();Pump();
        // Field Items: whole game, area and same item.
        var field=new RecordEditorView(new GameFieldItemsService(f.Session));Layout(field,1170,760);
        check(field.FieldOverviewCard is not null&&field.FieldContextCard is not null,"FieldItems_OverviewAndContextCards");
        Capture(field,1170,760,"field-items-redesign");
        // Moves: name and description of the selected move (a text bank with the move name and flavor files of USUM).
        int nameFile=f.Game.GameText.First(t=>t.Name==pk3DS.Core.TextName.MoveNames).Index,flavorFile=f.Game.GameText.First(t=>t.Name==pk3DS.Core.TextName.MoveFlavor).Index;
        var bank=Enumerable.Range(0,Math.Max(nameFile,flavorFile)+1).Select(i=>pk3DS.Core.TextFile.GetBytes(f.Game,
            Enumerable.Range(0,64).Select(n=>i==nameFile?$"Movimiento {n}":i==flavorFile?$"Descripción {n}\\nsegunda línea":$"Texto {i}.{n}").ToArray())).ToArray();
        f.WriteGarc(ModuleChecks.PathFor(f,"gametext"),bank,f.Game.GetGARCReference("gametext"));
        var moves=new MoveEditorView(f.Session);Layout(moves,1170,760);
        check(moves.MoveTextDescription?.Text.Contains('\n')==true,"Moves_DescriptionShowsGameLineBreaks");
        check(moves.MoveTextName is {IsEnabled:true}&&moves.MoveTextDescription is not null,"Moves_TextCardShowsNameAndDescription");
        if(moves.MoveTextDescription is {IsEnabled:true} description)
        {
            description.Text="Una descripción\nnueva";Pump();
            check(moves.HasPendingChanges,"Moves_UnsavedTextCountsAsPendingChange");
            moves.SaveMoveText();Pump();
            var flavor=f.Game.GameText.First(t=>t.Name==pk3DS.Core.TextName.MoveFlavor).Index;
            check(new GameTextService(f.Session).Read(flavor).Contains("Una descripción\\nnueva")&&!moves.HasPendingChanges,"Moves_TextIsSavedInGameText");
        }
        Capture(moves,1170,760,"moves-text-card");
    }
}
