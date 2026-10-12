using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;

internal static class UiSmoke
{
    internal static void Run(Action<bool,string> check)
    {
        StudioDialog.AutomatedChecks=true;
        var app = new ThreeDS.Progressive.App();
        app.InitializeComponent();
        var main = new MainWindow();
        main.Show();
        Layout((FrameworkElement)main.Content,1390,845);
        check(main.Foreground is SolidColorBrush color && color.Color==Color.FromRgb(239,243,248),"WPF main titles inherit the dark theme foreground");
        check(main.FindName("PersonalToolsHost") is ContentControl { Content: BatchToolsView },"WPF main shell initializes inspector tools");
        check(((FrameworkElement)main.FindName("PersonalToolsPanel")).Visibility==Visibility.Collapsed,"WPF tools are initially outside the editor");
        check(!Walk(main.Content.As<DependencyObject>()).OfType<TextBlock>().Any(t=>t.Text.Contains("CAMPOS TODAVÍA PENDIENTES")),"WPF obsolete pending-fields card removed");
        foreach (var generation in new[] {6,7})
        {
            var tools = new BatchToolsView();
            tools.Configure(false,generation,generation==6,true,false,()=>true,_=>[1],
                (_,s,scope)=>new BatchPreview(Guid.NewGuid(),[1],[[3]],"Smoke",s.Seed,scope.Count,[new BatchChange(1,"Demo","Tipo","A","B")]),_=>{},_=>"Table");
            IDictionary Checks()=>(IDictionary)typeof(BatchToolsView).GetField("_checks",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(tools)!;
            var checks=Checks();
            check(checks.Contains("Hms")== (generation==6),"WPF HM options match generation "+generation);
            foreach(string key in new[]{"Abilities","BanTraps","Items","Tms","TypeTutors","SpecialTutors"})
                check(((CheckBox)checks[key]!).IsChecked==true,"WPF screenshot default enabled: "+key+" Gen "+generation);
            foreach(string key in new[]{"Stats","HP","ATK","DEF","SPE","SPA","SPD","Shuffle","Types","WonderGuard","Catch","Eggs"})
                check(((CheckBox)checks[key]!).IsChecked==false,"WPF screenshot default unchecked: "+key+" Gen "+generation);
            check(tools.FindName("ActionBox")==null && tools.FindName("SeedBox")==null,"WPF action selector and manual seed removed Gen "+generation);
            var apply=(Button)tools.FindName("ApplyButton");
            check(!apply.IsEnabled,"WPF Apply disabled before preview Gen "+generation);
            var preview=(Button)tools.FindName("PreviewButton");
            preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            int first=tools.LastSeed;
            check(apply.IsEnabled && first>=0,"WPF automatic random seed enables frozen proposal Gen "+generation);
            preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(tools.LastSeed!=first,"WPF each preview uses a new random seed Gen "+generation);
            ((CheckBox)checks["Items"]!).IsChecked=false;
            check(!apply.IsEnabled,"WPF option change invalidates proposal Gen "+generation);
            tools.OpenAction(BatchAction.PersonalModify);
            check(Checks().Contains("SetCall")== (generation==7),"WPF SOS modifier matches generation "+generation);
            tools.OpenAction(BatchAction.PersonalRandomize);
            check(((CheckBox)Checks()["Items"]!).IsChecked==false,"WPF inspector options are remembered Gen "+generation);
            Layout(tools,600,900);
            apply.ApplyTemplate();
            Border border=Walk(apply).OfType<Border>().First(b=>b.Name=="ButtonBorder");
            check(((SolidColorBrush)border.Background).Color==Color.FromRgb(38,55,74),"WPF disabled action has subdued logo-blue background Gen "+generation);
        }
        // BST intervals are dynamically split/merged, not hardcoded to six.
        var bstFields=new[]{
            new EditorField("BST.0.MinLevel","Min",1,1,100),
            new EditorField("BST.0.MaxLevel","Max",100,1,100),
            new EditorField("BST.0.MinBST","MinBST",180,1,1530),
            new EditorField("BST.0.MaxBST","MaxBST",680,1,1530),
            new EditorField("BST.0.FullRandom","Libre",0,0,1)
        };
        var rangeTable=new ProgressiveBstTable(bstFields);
        Layout(rangeTable,920,440);
        check(rangeTable.Count==1,"WPF BST editor starts with its actual configured rows");
        Walk(rangeTable).OfType<Button>().First(b=>Equals(b.Content,"+ Añadir rango"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(rangeTable.Count==2&&rangeTable.Capture().ContainsKey("BST.1.MaxLevel"),
            "WPF BST add splits a range and serializes an extra row");
        Walk(rangeTable).OfType<Button>().First(b=>Equals(b.Content,"− Quitar seleccionado"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(rangeTable.Count==1&&rangeTable.Capture()["BST.0.MaxLevel"]==100,
            "WPF BST remove restores adjacent coverage");
        // Unlike the original generic inspector, native WPF rule tables keep
        // an independent Important/Boss filter without losing hidden edits.
        var candidates=new[]{
            new TrainerRuleTableRow{Id=9,Group="Important",Trainer="Entrenador importante",Ace=15,CapUse=true},
            new TrainerRuleTableRow{Id=23,Group="Boss",Trainer="Jefe",Ace=20,CapUse=true}
        };
        foreach(bool caps in new[]{true,false})
        {
            var rulesWindow=new TrainerRulesWindow(candidates,caps);
            Layout(rulesWindow,1160,650);
            var important=(CheckBox)typeof(TrainerRulesWindow)
                .GetField("_important",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(rulesWindow)!;
            var bosses=(CheckBox)typeof(TrainerRulesWindow)
                .GetField("_bosses",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(rulesWindow)!;
            var view=(System.ComponentModel.ICollectionView)typeof(TrainerRulesWindow)
                .GetField("_view",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(rulesWindow)!;
            check(((System.Collections.IEnumerable)view).Cast<object>().Count()==2,
                "WPF trainer table initially shows important trainers and bosses");
            bosses.IsChecked=false;
            check(((System.Collections.IEnumerable)view).Cast<object>().Count()==1,
                "WPF Boss filter hides bosses without discarding their records");
            important.IsChecked=false;
            check(((System.Collections.IEnumerable)view).Cast<object>().Count()==0&&rulesWindow.Rows.Count==2,
                "WPF trainer table filters preserve both hidden important and boss candidates");
            // Inline table stays hosted in the main editor; no modal window to close.
        }
        using(var fixture = new Fixture(GameVersion.USUM))
        {
            ModuleChecks.CreateArchives(fixture);
            main.LoadProject(fixture.Session);
            Layout((FrameworkElement)main.Content,1390,845);
            check(((TextBlock)main.FindName("DirtyBadgeText")).Text=="Guardado","WPF preserves original custom Z IDs while loading Pokemon");
            var personal=(BatchToolsView)((ContentControl)main.FindName("PersonalToolsHost")).Content;
            ((Button)main.FindName("RandomizePokemonButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((FrameworkElement)main.FindName("PokemonEditorPanel")).Visibility==Visibility.Collapsed &&
                ((FrameworkElement)main.FindName("PersonalToolsPanel")).Visibility==Visibility.Visible,"WPF inspector action temporarily replaces the centre");
            check(((FrameworkElement)main.FindName("PokemonListPanel")).Visibility==Visibility.Visible &&
                ((FrameworkElement)main.FindName("PokemonInspectorPanel")).Visibility==Visibility.Visible,"WPF catalogue and inspector remain available during actions");
            Layout((FrameworkElement)main.Content,1390,845);Capture((FrameworkElement)main.Content,"pokemon-randomizar.png");
            Walk(personal).OfType<Button>().First(b=>Equals(b.Content,"← Volver al editor")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((FrameworkElement)main.FindName("PokemonEditorPanel")).Visibility==Visibility.Visible &&
                ((FrameworkElement)main.FindName("PersonalToolsPanel")).Visibility==Visibility.Collapsed,"WPF return restores the centre editor");
            ((Button)main.FindName("ModifyPokemonButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(personal.ActiveAction==BatchAction.PersonalModify,"WPF modifier is a separate named inspector button");
            Walk(personal).OfType<Button>().First(b=>Equals(b.Content,"← Volver al editor")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout((FrameworkElement)main.Content,1390,845);Capture((FrameworkElement)main.Content,"pokemon-editor.png");
            ((Button)main.FindName("MovesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var view = (MoveEditorView)((ContentControl)main.FindName("MovesHost")).Content;
            Layout(view,1170,720);
            check(!Walk(view).OfType<Button>().Any(b => Equals(b.Content,"Editor integrado · 3DS Progressive")),"WPF obsolete integrated editor badge removed");
            check(!Walk(view).OfType<TextBlock>().Any(t=>t.Text.Contains("Faltan estados secundarios")),"WPF obsolete move pending notice removed");
            var tools=(BatchToolsView)((ContentControl)view.FindName("MoveToolsHost")).Content;
            ((Button)view.FindName("MetronomeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(tools.ActiveAction==BatchAction.Metronome && ((FrameworkElement)tools.FindName("SeedPanel")).Visibility==Visibility.Collapsed,"WPF inspector Metronome has no irrelevant seed");
            ((Button)tools.FindName("PreviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((Button)tools.FindName("ApplyButton")).IsEnabled,"WPF real move preview enables Apply");
            ((TextBox)view.FindName("MovePower")).Text="81";
            check(!((Button)tools.FindName("ApplyButton")).IsEnabled,"WPF editing move invalidates proposal");
            ((TextBox)view.FindName("MovePower")).Text="75";
            ((Button)view.FindName("BalanceMovesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(tools.ActiveAction==BatchAction.MoveBalance && ((FrameworkElement)tools.FindName("TemplatePanel")).Visibility==Visibility.Visible,"WPF templates open in the temporary centre");
            Layout(view,1170,720);Capture(view,"movimientos-templates.png");
            Walk(tools).OfType<Button>().First(b=>Equals(b.Content,"← Volver al editor")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var expander in Walk(view).OfType<Expander>().ToArray())
                expander.IsExpanded=true;
            Layout(view,1170,720);
            ComboBox choice=(ComboBox)view.FindName("TargetBox");
            choice.BringIntoView();view.UpdateLayout();choice.ApplyTemplate();
            Capture(view,"movimientos-editor.png");
            check(Walk(choice).OfType<TextBlock>().Any(t=>t.Text.Contains("Single Adjacent Ally/Foe")) &&
                !Walk(choice).OfType<TextBlock>().Any(t=>t.Text.Contains("MoveChoice {")),"WPF selected dropdown shows its name rather than record code");
            check(((Button)main.FindName("BackupButton")).IsEnabled,"WPF manual backup becomes available with a project");
            ((Button)main.FindName("TrainersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var trainer=(RecordEditorView)((ContentControl)main.FindName("SectionHost")).Content;
            check(trainer.Editor is ThreeDS.Progressive.Services.GameTrainerService,"WPF Trainers opens a real archive editor");
            var pokemonTabs=(IDictionary)typeof(RecordEditorView)
                .GetField("_pokemonTabs",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(trainer)!;
            check(pokemonTabs.Count==6 && pokemonTabs[0] is TabControl {Items.Count:3},
                "WPF trainer team has six Pokémon editors with Main, IV/EV and Moves tabs");
            var aiInput=(TextBox)trainer.FieldControls["AI"];
            var aiParent=(StackPanel)aiInput.Parent;
            check(aiInput.Text=="165"&&aiParent.Children.OfType<WrapPanel>().Any(p=>p.Children.Count==8),
                "WPF Trainer AI mask has eight visual flags without losing the original byte value");

            check(((FrameworkElement)trainer.FindName("TrainerTeamPreview")).Visibility==Visibility.Visible
                && ((StackPanel)trainer.FindName("TrainerTeamCards")).Children.Count>0,
                "WPF trainer editor shows its Pokémon team cards");
            check(File.Exists(Path.Combine(AppContext.BaseDirectory,"Sprites","1.png")),
                "Original Pokémon sprites are bundled for offline views");
            check(RecordEditorView.TrainerOptionCategory("NUD_LevelBoost")==1
                && RecordEditorView.TrainerOptionCategory("CB_Moves")==2
                && RecordEditorView.TrainerOptionCategory("BST.0.MinBST")==4,
                "Trainer UI assigns level caps, moves and progressive BST to separate panels");
            Layout(trainer,1170,720);Capture(trainer,"entrenadores-editor.png");
            ((TextBox)trainer.FieldControls["P0.Level"]).Text="44";
            check(((Button)trainer.FindName("SaveButton")).IsEnabled,"WPF trainer editing marks unsaved changes");
            Walk(trainer).OfType<Button>().First(b=>Equals(b.Content,"Descartar cambios")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((TextBox)trainer.FieldControls["P0.Level"]).Text=="10","WPF trainer discard restores the saved value");
            ((Button)trainer.FindName("RandomizerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var trainerTabs=((StackPanel)trainer.FindName("OptionsPanel")).Children.OfType<TabControl>().Single();
            trainerTabs.ApplyTemplate();
            var firstTrainerTab=(TabItem)trainerTabs.Items[0];
            firstTrainerTab.ApplyTemplate();
            check(trainerTabs.Background is SolidColorBrush tabsBg && tabsBg.Color==Color.FromRgb(21,26,34)
                && firstTrainerTab.Background is SolidColorBrush tabBg && tabBg.Color==Color.FromRgb(37,47,61),
                "WPF trainer tab strip and panel use dark graphite brushes (no Windows white defaults)");
            trainerTabs.SelectedIndex=4;
            Layout(trainer,1170,720);Capture(trainer,"entrenadores-bst-dark.png");
            check(trainerTabs.SelectedIndex==4,"WPF trainer BST tab remains selectable after dark template");
            trainerTabs.SelectedIndex=0;

            check(trainer.OptionControls.ContainsKey("GlobalCaps.ApplyPrevious")
                && trainer.OptionControls.ContainsKey("GlobalCaps.PreviousGap")
                && trainer.OptionControls.ContainsKey("GlobalMoves.ResetUnlisted"),
                "WPF exposes imported global Level Caps and Move Rules template policies");
            check(trainer.OptionControls.Keys.Any(k=>k.EndsWith(".CapUse",StringComparison.Ordinal))
                && trainer.OptionControls.Keys.Any(k=>k.EndsWith(".MoveUse",StringComparison.Ordinal)),
                "Trainer options expose level cap and move rules even without a saved template");
            check(((StackPanel)trainer.FindName("OptionsPanel")).Children.OfType<TabControl>().Any(t=>t.Items.Count==5
                && ((TabItem)t.Items[1]).Header?.ToString()=="Niveles de entrenadores"),
                "Trainer randomizer shows five task-based panels");
            ((ComboBox)trainer.FindName("ScopeBox")).SelectedIndex=2;
            ((TextBox)trainer.OptionControls["NUD_LevelBoost"]).Text="1.6";
            ((Button)trainer.FindName("PreviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((Button)trainer.FindName("ApplyButton")).IsEnabled,"WPF trainer randomizer produces a scoped proposal");
            Layout(trainer,1170,720);Capture(trainer,"entrenadores-randomizar.png");
            ((Button)main.FindName("EncountersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var encounters=(RecordEditorView)((ContentControl)main.FindName("SectionHost")).Content;
            check(encounters.Editor is ThreeDS.Progressive.Services.GameEncounterService && encounters.FieldControls.ContainsKey("S8.0.Species"),"WPF Encounters includes SOS/weather controls");
            check(((FrameworkElement)encounters.FindName("EncounterSpritePreview")).Visibility==Visibility.Visible
                && ((WrapPanel)encounters.FindName("EncounterSpriteCards")).Children.Count==10,
                "WPF encounter editor displays ten clickable original Pokémon sprite slots");
            Layout(encounters,1170,720);Capture(encounters,"encuentros-editor.png");
            ((Button)main.FindName("TrainersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var remembered=(RecordEditorView)((ContentControl)main.FindName("SectionHost")).Content;
            check(((TextBox)remembered.OptionControls["NUD_LevelBoost"]).Text=="1.6"&&((ComboBox)remembered.FindName("ScopeBox")).SelectedIndex==2,"WPF module options and scope survive navigation");
            ((Button)main.FindName("StaticEncountersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var staticView=(RecordEditorView)((ContentControl)main.FindName("SectionHost")).Content;
            check(staticView.Editor is ThreeDS.Progressive.Services.GameStaticEncounterService
                &&staticView.FieldControls.ContainsKey("ShinyLock"),
                "WPF Static Encounters opens real Gen7 gift fields");
            check(((FrameworkElement)staticView.FindName("EncounterSpritePreview")).Visibility==Visibility.Visible
                && ((WrapPanel)staticView.FindName("EncounterSpriteCards")).Children.Count==1,
                "WPF Static Encounters displays the Pokémon sprite for the selected gift");
            Layout(staticView,1170,720);Capture(staticView,"static-encounters-editor.png");
            if(fixture.Game.USUM)
            {
                check(((Button)staticView.FindName("TotemCapsButton")).Visibility==Visibility.Visible
                    &&((Button)staticView.FindName("TotemBstButton")).Visibility==Visibility.Visible,
                    "WPF USUM static encounter exposes Totem Caps and Totem BST tools");
                ((Button)staticView.FindName("TotemCapsButton")).RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                var host=(ContentControl)staticView.FindName("TrainerRulesHost");
                check(host.Visibility==Visibility.Visible&&host.Content is TotemRulesView,
                    "WPF Totem Caps opens embedded dark table without extra windows");
                if(host.Content is TotemRulesView cap)
                {Layout(cap,1150,680);Capture(cap,"totem-caps-dark.png");}
                ((Button)Walk(host).OfType<Button>().First(x=>Equals(x.Content,"Volver")))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Button)staticView.FindName("TotemBstButton")).RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                check(host.Content is TotemRulesView,"WPF Totem BST opens its independent table");
                if(host.Content is TotemRulesView bst)
                {Layout(bst,1150,680);Capture(bst,"totem-bst-dark.png");}
                ((Button)Walk(host).OfType<Button>().First(x=>Equals(x.Content,"Volver")))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            // Supply a valid code.bin fixture for the native TM editor; unlike a dummy
            // navigation card this exercises loading actual Gen7 TM assignments.
            byte[] code=new byte[0x402000];
            byte[] signature=[0x03,0x40,0x03,0x41,0x03,0x42,0x03,0x43,0x03];
            signature.CopyTo(code,0x400000);
            int tmOffset=0x400000+signature.Length+0x22;
            for(int i=0;i<100;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(code,tmOffset+i*2);
            File.WriteAllBytes(Path.Combine(fixture.Game.ExeFS,"code.bin"),code);
            ((Button)main.FindName("MachineTutorButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var machineView=((ContentControl)main.FindName("SectionHost")).Content as MachineTutorView;
            check(machineView is not null,"WPF MT/Tutors navigation creates a native editor");
            Layout(machineView!,1170,720);
            check(((DockPanel)machineView!.Content).Children.OfType<TabControl>().Any(t=>t.Items.Count==2),
                "WPF MT/Tutors navigation opens a real dual-tab editor");
            Capture(machineView,"mt-tutores-editor.png");
            ((Button)main.FindName("LevelUpButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var levelUp=((ContentControl)main.FindName("SectionHost")).Content as LevelUpView;
            check(levelUp is not null,"WPF Level Up Moves navigation opens editable learned move table");
            if(levelUp is not null)
            {
                Layout(levelUp,1170,720);
                check(levelUp.MoveTables.Count==3&&levelUp.MoveTables.All(g=>
                    g.Columns[0].Width.Value>=90&&g.RowHeaderWidth==0),
                    "WPF Level Up tables have full 90px level columns and no white row-header strips");
                var footerButton=Walk(levelUp).OfType<Button>().First(x=>Equals(x.Content,"Guardar movimientos"));
                check(footerButton.IsEnabled==false,"WPF Save is disabled before editing");
                Capture(levelUp,"level-up-moves.png");
                var before=levelUp.VisibleLearnset.Select(x=>(x.Level,x.Move)).ToArray();
                Walk(levelUp).OfType<Button>().First(x=>Equals(x.Content,"Metrónomo · Todos"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(levelUp.HasGlobalPreview&&!footerButton.IsEnabled
                    &&levelUp.MoveTables.All(g=>g.IsReadOnly)
                    &&levelUp.VisibleLearnset.Count==1
                    &&levelUp.VisibleLearnset[0].Move==118,
                    "WPF global randomizer shows actual proposed Metronome learnset in the existing tables without writing");
                Capture(levelUp,"level-up-preview.png");
                Walk(levelUp).OfType<Button>().First(x=>Equals(x.Content,"Descartar vista previa"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!levelUp.HasGlobalPreview&&
                    levelUp.VisibleLearnset.Select(x=>(x.Level,x.Move)).SequenceEqual(before),
                    "WPF Discard preview restores the original species learnset");
            }
            ((Button)main.FindName("GlobalTemplateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((ContentControl)main.FindName("SectionHost")).Content is GlobalTemplateView,
                "WPF Global Template menu opens lossless compatible import/export UI");
            var globalView=((ContentControl)main.FindName("SectionHost")).Content as GlobalTemplateView;
            if(globalView is not null)
            {
                Layout(globalView,1100,720);
                var actionGrid=Walk(globalView).OfType<System.Windows.Controls.DataGrid>().First();
                check(actionGrid.RowHeaderWidth==0&&actionGrid.ColumnHeaderStyle is not null &&
                    actionGrid.ColumnHeaderStyle.Setters.OfType<Setter>().Any(x=>
                        x.Property==System.Windows.Controls.Control.BackgroundProperty),
                    "Global Template action table uses explicit dark column header instead of white default");
                Capture(globalView,"global-template-dark.png");
            }
            ((Button)main.FindName("BatchRomButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((ContentControl)main.FindName("SectionHost")).Content is BatchRomBuilderView,
                "WPF Batch ROM Builder is native dark UI and no longer an inaccessible section");
            if(((ContentControl)main.FindName("SectionHost")).Content is BatchRomBuilderView batchView)
            {
                Layout(batchView,1160,760);
                check(Walk(batchView).OfType<System.Windows.Controls.ProgressBar>().Count()>=2,
                    "WPF Batch Builder has separate measured overall and current-ROM percentage bars");
                Capture(batchView,"batch-rom-progress.png");
            }
            ((Button)main.FindName("PokemonToolsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pokemonTools=((ContentControl)main.FindName("SectionHost")).Content as SystemsView;
            check(pokemonTools is not null,"WPF separates original editors into Pokémon Tools");
            if(pokemonTools is not null)
            {
                Layout(pokemonTools,1130,690);
                var labels=Walk(pokemonTools).OfType<TextBlock>().Select(t=>t.Text).ToArray();
                check(labels.Any(t=>t?.Contains("Egg Moves")==true)&&
                    labels.Any(t=>t?.Contains("Mega Evolutions")==true)&&
                    labels.Any(t=>t?.Contains("Type Chart")==true)&&
                    labels.Any(t=>t?.Contains("Shiny Rate")==true)&&
                    labels.Any(t=>t?.Contains("OWSE")==true),
                    "Pokémon Tools hosts all original editors and read-only OWSE Dev entry");
                Capture(pokemonTools,"pokemon-tools-dark.png");
            }
            ((Button)main.FindName("MechanicsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((ContentControl)main.FindName("SectionHost")).Content is SystemsView,
                "WPF mechanics and CRO menu is active and opens real systems hub");
            ((Button)main.FindName("RomToolsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((ContentControl)main.FindName("SectionHost")).Content is SystemsView,
                "WPF ROM tools menu opens checksum and backup workflows");
            ((Button)main.FindName("AutomationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((ContentControl)main.FindName("SectionHost")).Content is SystemsView,
                "WPF automation menu navigates existing safe randomizer engines");
            ((Button)main.FindName("ConfigurationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((ContentControl)main.FindName("SectionHost")).Content is SystemsView,
                "WPF configuration menu opens local-only folder defaults");
            ((Button)main.FindName("MechanicsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var hub=((ContentControl)main.FindName("SectionHost")).Content as SystemsView;
            if(fixture.Game.USUM&&hub is not null)
            {
                Layout(hub,1150,720);
                hub.OpenCroRecipesForChecks();
                check(((ContentControl)main.FindName("SectionHost")).Content is CroRecipesView,
                    "CRO Recipes opens native dark editor and preview within main WPF window");
                ((Button)main.FindName("MechanicsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (((ContentControl)main.FindName("SectionHost")).Content as SystemsView)?.OpenRelearnerForChecks();
                check(((ContentControl)main.FindName("SectionHost")).Content is UsumRelearnerView,
                    "USUM Relearner opens original Sun/Moon patch workflow without WinForms dialog");
                ((Button)main.FindName("MechanicsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (((ContentControl)main.FindName("SectionHost")).Content as SystemsView)?.OpenPlayerCapsForChecks();
                check(((ContentControl)main.FindName("SectionHost")).Content is PlayerLevelCapsView,
                    "WPF Player Level Caps opens researched USUM flags and CRO installer in the main application");
            }
            ((Button)main.FindName("ItemsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var items=((ContentControl)main.FindName("SectionHost")).Content as RecordEditorView;
            check(items?.Editor is ThreeDS.Progressive.Services.GameItemService&&
                items.FieldControls.ContainsKey("Price"),"WPF native Item editor exposes prices and attributes");
            if(items is not null)
            {
                Layout(items,1170,720);Capture(items,"objetos-editor.png");
                ((Button)items.FindName("EconomyTableButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var economy=((ContentControl)items.FindName("TrainerRulesHost"));
                check(economy.Visibility==Visibility.Visible&&economy.Content is EconomyPriceView,
                    "WPF Fix Economy uses full-width in-app grid, not a new window");
                if(economy.Content is EconomyPriceView ev){Layout(ev,1160,720);Capture(ev,"economy-price-table.png");}
                ((Button)Walk(economy).OfType<Button>().First(x=>Equals(x.Content,"Volver")))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            ((Button)main.FindName("FieldItemsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var fieldItems=((ContentControl)main.FindName("SectionHost")).Content as RecordEditorView;
            check(fieldItems?.Editor is ThreeDS.Progressive.Services.GameFieldItemsService
                &&fieldItems.FieldControls.ContainsKey("Item"),
                "WPF Field Items navigates to real EI/EB pickup editor");
            if(fieldItems is not null){Layout(fieldItems,1170,720);Capture(fieldItems,"field-items-editor.png");}
            ((Button)main.FindName("GameTextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var gameTexts=((ContentControl)main.FindName("SectionHost")).Content as GameTextView;
            check(gameTexts is not null,"WPF game story/text editor navigates without a separate window");
            if(gameTexts is not null){Layout(gameTexts,1170,720);Capture(gameTexts,"historia-textos.png");}
            ((Button)main.FindName("EvolutionsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var evolutions=(RecordEditorView)((ContentControl)main.FindName("SectionHost")).Content;
            check(evolutions.Editor is ThreeDS.Progressive.Services.GameEvolutionService&&evolutions.FieldControls.ContainsKey("E7.Method"),"WPF Evolutions exposes all eight routes");
            Layout(evolutions,1170,720);Capture(evolutions,"evoluciones-editor.png");
            ((Button)main.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((ContentControl)main.FindName("SectionHost")).Content is HistoryView,"WPF persistent history opens in the same window");
            ((Button)main.FindName("PokemonNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((FrameworkElement)main.FindName("PokemonEditorPanel")).Visibility==Visibility.Visible && ((FrameworkElement)main.FindName("SectionHost")).Visibility==Visibility.Collapsed,"WPF returns from new modules to Pokemon");
        }
        main.Close();app.Shutdown();
    }

    private static void Layout(FrameworkElement element,double width,double height)
    {element.Measure(new Size(width,height));element.Arrange(new Rect(0,0,width,height));element.UpdateLayout();}
    private static void Capture(FrameworkElement element,string file)
    {
        string? target=Environment.GetEnvironmentVariable("PROGRESSIVE_UI_CAPTURE_DIR");
        if(string.IsNullOrWhiteSpace(target))return;
        Directory.CreateDirectory(target);
        var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(target,file));encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Walk(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    private static T As<T>(this object value) => (T)value;
}
