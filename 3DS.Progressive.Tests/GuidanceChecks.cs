using System.IO;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using TextBox=System.Windows.Controls.TextBox;

internal static class GuidanceChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,Private)!.GetValue(value)!;
    private static void Layout(FrameworkElement root,double width,double height)
    {
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
    }
    private static void Capture(FrameworkElement root,int width,int height,double scale,string name)
    {
        Layout(root,width/scale,height/scale);
        var image=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96*scale,96*scale,System.Windows.Media.PixelFormats.Pbgra32);image.Render(root);
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        string folder=Path.Combine(Path.GetTempPath(),"DefinitiveRandomizer-Guidance-QA");Directory.CreateDirectory(folder);
        using var output=File.Create(Path.Combine(folder,name+".png"));png.Save(output);
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        check(GuidanceCatalog.Topics.Select(topic=>topic.Route).Distinct().Count()==GuidanceCatalog.Topics.Count&&GuidanceCatalog.Topics.All(topic=>topic.Steps.Count==3&&topic.Steps.All(step=>!string.IsNullOrWhiteSpace(step.Text))),"Guidance_CatalogHasUniqueShortCompleteTopics");
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var plan=new GuidedRandomizationPlan(f.Session);
            check(plan.Choices.All(choice=>!choice.Selected&&choice.Groups is null),"Guidance_NoImplicitSelectionOrEagerArchiveLoad_"+version);
            check(plan.Choices.Any(choice=>choice.Module.Key=="Tutors")== (version is GameVersion.ORAS or GameVersion.USUM),"Guidance_TutorsRespectGameCompatibility_"+version);
            check(GuidanceCatalog.Available(f.Session).Any(topic=>topic.Route=="PlayerCaps")== (version==GameVersion.USUM),"Guidance_TourFiltersUnsupportedTopics_"+version);
            reject(()=>plan.BuildDocument(),"Guidance_EmptySelectionRejected_"+version);
            foreach(string key in new[]{"Personal","Moves","LevelUp"}){plan.Get(key).Selected=true;plan.Initialize(key);}
            plan.Get("Personal").Values["Abilities"]=1;plan.Get("Moves").Values["MoveTypes"]=1;plan.Get("LevelUp").Values["CHK_STAB"]=1;plan.Get("LevelUp").Values["NUD_STAB"]=27;
            byte[] personal=File.ReadAllBytes(f.PersonalPath),moves=File.ReadAllBytes(f.MovePath);
            var doc=plan.BuildDocument();
            check(doc.Actions.ToHashSet().SetEquals(new[]{"personal.randomize","moves.randomize","level-up-moves.randomize"}),"Guidance_OnlyChosenActionsAreExported_"+version);
            var master=NativeMasterBatchBridge.Read(doc,"Personal")!;
            check(master.Settings.Abilities&&!master.Settings.Stats&&NativeMasterBatchBridge.Read(doc,"Moves")!.Settings.MoveTypes,"Guidance_PreservesChosenMasterOptions_"+version);
            check(doc.Section("RandSettings")!.ToJsonString().Contains("NUD_STAB\\t27")&&doc.Section("RandSettings")!.ToJsonString().Contains(version is GameVersion.XY or GameVersion.ORAS?"LevelUpEditor6":"LevelUpEditor7"),"Guidance_LevelUpUsesOriginalSettings_"+version);
            check(GlobalTemplateDocument.Parse(doc.ToJson(),f.Session.GameName,f.Game.Generation).Actions.SequenceEqual(doc.Actions),"Guidance_RecipeRoundTrip_"+version);
            string recipe=Path.Combine(f.Root,"guided.json");File.WriteAllText(recipe,doc.ToJson());string output=Path.Combine(Path.GetTempPath(),"guided-preflight-"+Guid.NewGuid().ToString("N"));
            var result=new BatchRomExecution(f.Session).Preflight(new(recipe,output,"guiado",1,true,false,true));
            check(result.Template.Actions.Count==3&&!Directory.Exists(output)&&File.ReadAllBytes(f.PersonalPath).SequenceEqual(personal)&&File.ReadAllBytes(f.MovePath).SequenceEqual(moves)&&f.Session.History.Read().Entries.Count==0,"Guidance_ConfigurationAndPreflightNeverWriteSource_"+version);
            byte[] untouched=File.ReadAllBytes(ModuleChecks.PathFor(f,"encdata"));
            var expectedPersonal=f.Session.PreviewPersonal(BatchAction.PersonalRandomize,master.Settings with{Seed=133},f.Session.Entries.Select(row=>row.Index).ToArray()).CopyRecords();
            NativeMasterBatchBridge.ExecuteSession(f.Session,doc.ToJson(),133,"Personal");
            check(f.PersonalRecords.Zip(expectedPersonal).All(pair=>pair.First.SequenceEqual(pair.Second)),"Guidance_ChosenPersonalRecipeMatchesNativeReplay_"+version);
            var moveService=new GameMoveService(f.Session);var moveState=NativeMasterBatchBridge.Read(doc,"Moves")!;
            var expectedMoves=moveService.PreviewMoves(BatchAction.MoveRandomize,moveState.Settings with{Seed=219},moveService.Entries.Select(row=>row.Id).ToArray()).CopyRecords();
            NativeMasterBatchBridge.ExecuteSession(f.Session,doc.ToJson(),219,"Moves");
            check(f.MoveRecords.Zip(expectedMoves).All(pair=>pair.First.SequenceEqual(pair.Second))&&File.ReadAllBytes(ModuleChecks.PathFor(f,"encdata")).SequenceEqual(untouched),"Guidance_ChosenMoveReplayPreservesUnselectedArchives_"+version);
            plan.Get("Moves").Selected=false;check(!plan.BuildDocument().Actions.Contains("moves.randomize"),"Guidance_DeselectionRemovesAction_"+version);
            plan.Get("Personal").Values["Deviation"]=101;reject(()=>plan.BuildDocument(),"Guidance_InvalidRangeRejected_"+version);plan.Get("Personal").Values["Deviation"]=25;
            plan.Get("Personal").Values["Abilities"]=0;check(!plan.BuildDocument().Actions.Contains("personal.randomize"),"Guidance_EmptySectionSkipped_"+version);
            var wild=new GuidedRandomizationPlan(f.Session);wild.Get("Wild").Selected=true;wild.Initialize("Wild");var wildDoc=wild.BuildDocument();
            check(wildDoc.Actions.Count==1&&wildDoc.Actions[0].StartsWith("wild-encounters."),"Guidance_WildHasExactlyOneConfiguredAction_"+version);
            if(f.Game.Generation==7)check(NativeGen7WildBridge.Restore(new GameEncounterService(f.Session),wildDoc).Count==wild.Get("Wild").Values.Count,"Guidance_WildGen7SnapshotIsComplete_"+version);
        }
    }
    private static void SetupField6(Fixture fixture)
    {
        bool oras=fixture.Game.ORAS;int number=oras?29:31,visible=oras?39:17,start=oras?0xB64:0xB04;
        byte[] instructions=new byte[0x1800];BitConverter.GetBytes((ushort)17).CopyTo(instructions,start);BitConverter.GetBytes((ushort)18).CopyTo(instructions,start+12);
        byte[] encoded=Gen6ScriptCodec.Encode(instructions);byte[] script=new byte[32+encoded.Length];
        BitConverter.GetBytes(script.Length).CopyTo(script,0);BitConverter.GetBytes(0x0A0AF1E0).CopyTo(script,4);BitConverter.GetBytes(32).CopyTo(script,12);BitConverter.GetBytes(32).CopyTo(script,16);BitConverter.GetBytes(32+instructions.Length).CopyTo(script,20);encoded.CopyTo(script,32);
        var files=Enumerable.Range(0,60).Select(_=>new byte[]{0xAA,0xBB}).ToArray();files[visible]=script;files[oras?57:26]=script;
        var reference=fixture.Game.GetGARCReference("personal");string path=Path.Combine(fixture.Game.RomFS,oras?"a/0/2/9":"a/0/3/1");
        fixture.WriteGarc(path,files,reference.GetRelativeGARC(number-reference.FileNumber,"scripts"));
    }
    private static void SetupStatic6(Fixture fixture)
    {
        bool oras=fixture.Game.ORAS;int gift=oras?0xF906C:0xF805C,count=oras?37:19,size=oras?36:24;
        byte[] bytes=new byte[gift+count*size+64];"CRO0"u8.CopyTo(bytes.AsSpan(0x80));
        File.WriteAllBytes(Path.Combine(fixture.Game.RomFS,"DllField.cro"),bytes);
    }
    internal static void RunUi(Action<bool,string> check)
    {
        GuidanceFollowupChecks.RunUi(check);
        WpfTestHost.SuppressStartup();StudioDialog.AutomatedChecks=true;
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var main=new MainWindow();main.LoadProject(f.Session);
            var hp=(TextBox)main.FindName("HpBox");hp.Text="87";object? originalEditor=((ContentControl)main.FindName("SectionHost")).Content;
            var advanced=(Button)main.FindName("TemplateStudioButton");var originalButton=advanced;
            main.ChooseExperience(true);
            check(advanced.Visibility==Visibility.Collapsed&&((Button)main.FindName("EncountersButton")).Visibility==Visibility.Visible,"Guidance_SimplifiedShowsCoreSections_"+version);
            main.ChooseExperience(false);
            check(ReferenceEquals(originalButton,main.FindName("TemplateStudioButton"))&&advanced.Visibility==Visibility.Visible&&hp.Text=="87"&&ReferenceEquals(originalEditor,((ContentControl)main.FindName("SectionHost")).Content)&&f.Session.History.Read().Entries.Count==0,"Guidance_ModeSwitchPreservesControlsDraftAndHistory_"+version);
            ((Button)main.FindName("HomeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((ContentControl)main.FindName("HomeHost")).Visibility==Visibility.Visible&&((ContentControl)main.FindName("HomeHost")).Content is StartView,"Guidance_HomeUsesRealStartView_"+version);
            if(version==GameVersion.USUM)Capture((FrameworkElement)main.Content,1280,800,1,"inicio");
            check(!((System.Windows.Controls.Grid)main.FindName("EditorShell")).IsEnabled&&((ContentControl)main.FindName("HomeHost")).IsEnabled,"Guidance_HomeExcludesCoveredEditorsFromInteraction_"+version);
            main.ChooseExperience(false);check(((System.Windows.Controls.Grid)main.FindName("EditorShell")).IsEnabled,"Guidance_ReturnRestoresEditorInteraction_"+version);main.Close();
            var wizard=new GuidedRandomizationWindow(f.Session);wizard.Plan.Get("Personal").Selected=true;wizard.Plan.Get("Moves").Selected=true;
            wizard.NextAsync().GetAwaiter().GetResult();
            var heading=Field<TextBlock>(wizard,"_heading");
            check(((System.Windows.Media.SolidColorBrush)heading.Foreground).Color==((System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.FindResource("TextMain")).Color,"Guidance_DialogInheritsReadableTheme_"+version);
            if(version==GameVersion.USUM)
            {
                var app=(ThreeDS.Progressive.App)System.Windows.Application.Current;
                try
                {
                    AccessiblePalette.Update(app,true);System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
                    check(((System.Windows.Media.SolidColorBrush)heading.Foreground).Color==((System.Windows.Media.SolidColorBrush)app.FindResource("TextMain")).Color,"Guidance_DialogTracksHighContrastPalette");
                }
                finally{AccessiblePalette.Update(app,false);}
                foreach(var (width,height,scale) in new[]{(860,700,1d),(1075,875,1.25),(1290,1050,1.5),(920,900,2d)})
                {
                    Capture((FrameworkElement)wizard.Content,width,height,scale,"asistente-"+scale.ToString(CultureInfo.InvariantCulture));
                    var next=Field<Button>(wizard,"_next");var bounds=next.TransformToAncestor((FrameworkElement)wizard.Content).TransformBounds(new Rect(next.RenderSize));
                    check(bounds.Top>=0&&bounds.Bottom<=((FrameworkElement)wizard.Content).ActualHeight+1,"Guidance_WizardActionsStayVisible_"+scale.ToString(CultureInfo.InvariantCulture));
                }
            }
            var first=Field<Dictionary<string,FrameworkElement>>(wizard,"_inputs");((CheckBox)first["Abilities"]).IsChecked=true;
            wizard.NextAsync().GetAwaiter().GetResult();var second=Field<Dictionary<string,FrameworkElement>>(wizard,"_inputs");((CheckBox)second["MoveTypes"]).IsChecked=true;
            wizard.BackAsync().GetAwaiter().GetResult();
            check(((CheckBox)Field<Dictionary<string,FrameworkElement>>(wizard,"_inputs")["Abilities"]).IsChecked==true,"Guidance_WizardBackRetainsChoices_"+version);
            wizard.NextAsync().GetAwaiter().GetResult();wizard.NextAsync().GetAwaiter().GetResult();
            var document=Field<GlobalTemplateDocument>(wizard,"_document");
            check(document.Actions.ToHashSet().SetEquals(new[]{"personal.randomize","moves.randomize"})&&f.Session.History.Read().Entries.Count==0,"Guidance_WizardSummaryDoesNotApply_"+version);wizard.Close();
            var invalid=new GuidedRandomizationWindow(f.Session);invalid.Plan.Get("Personal").Selected=true;invalid.NextAsync().GetAwaiter().GetResult();invalid.NextAsync().GetAwaiter().GetResult();
            check(Field<GlobalTemplateDocument>(invalid,"_document").Actions.Count==0,"Guidance_AllEmptySummaryDoesNotGenerate_"+version);invalid.Close();
            check(f.Session.History.Read().Entries.Count==0,"Guidance_CancelNeverApplies_"+version);
            foreach(string key in new[]{"Trainers","EggMoves","Evolutions","StaticEncounters","Marts","Machines","FieldItems"}.Concat(version is GameVersion.ORAS or GameVersion.USUM?new[]{"Tutors"}:Array.Empty<string>()))
            {
                using var module=new Fixture(version);ModuleChecks.CreateArchives(module);
                if(key=="EggMoves")module.WriteGarc(ModuleChecks.PathFor(module,"eggmove"),Enumerable.Range(0,6).Select(_=>new byte[module.Game.Generation==7?4:2]).ToArray(),module.Game.GetGARCReference("eggmove"));
                if(module.Game.Generation==6)
                {
                    if(key=="Marts")Mart6Checks.Setup(module);
                    if(key=="Machines")Machine6Checks.Setup(module);
                    if(key=="Tutors")Tutor6Checks.Setup(module);
                    if(key=="StaticEncounters")SetupStatic6(module);
                    if(key=="FieldItems")SetupField6(module);
                }
                var selected=new GuidedRandomizationPlan(module.Session);selected.Get(key).Selected=true;selected.Initialize(key);
                var recipe=selected.BuildDocument();
                check(recipe.Actions.Count>0&&module.Session.History.Read().Entries.Count==0,"Guidance_SectionOptionsCreateReplayableRecipe_"+version+"_"+key);
                var values=selected.Get(key).Values;var saved=new Dictionary<string,int>(values);
                foreach(var field in selected.Get(key).Groups!.SelectMany(g=>g.Fields).Where(f=>f.Min==0&&f.Max==1&&f.Choices is null))values[field.Key]=0;
                if(key=="Marts"&&module.Game.Generation==6)values["UseChecks"]=1;
                if(key=="Machines"&&module.Game.Generation==6)values["HmRestriction"]=0;
                check(selected.BuildDocument().Actions.Count==0,"Guidance_NoCheckedOptionsSkipsSection_"+version+"_"+key);
                selected.Get(key).Values=saved;
                if(key=="Marts"&&module.Game.Generation==7)
                {
                    selected.Get(key).Values["Candies"]=1;selected.Get(key).Values["RandomBP"]=1;
                    var combined=selected.BuildDocument();check(combined.Actions.Contains("marts.add-rare-candies")&&combined.Actions.Contains("marts.randomize-bp")&&combined.Actions.Contains("marts.randomize"),"Guidance_MartOptionsAccumulate_"+version);
                }
            }
        }
    }
}
