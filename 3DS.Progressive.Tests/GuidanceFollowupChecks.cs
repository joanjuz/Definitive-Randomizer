using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
using pk3DS.Core;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using DataGrid=System.Windows.Controls.DataGrid;
internal static class GuidanceFollowupChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,Private)!.GetValue(value)!;
    private static void Layout(FrameworkElement root){root.Measure(new Size(1200,800));root.Arrange(new Rect(0,0,1200,800));root.UpdateLayout();}
    internal static void RunUi(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var fixture=new Fixture(version);ModuleChecks.CreateArchives(fixture);
            foreach(string key in new[]{"Wild","Trainers"})
            {
                var wizard=new GuidedRandomizationWindow(fixture.Session);wizard.Plan.Get(key).Selected=true;wizard.NextAsync().GetAwaiter().GetResult();
                var choice=wizard.Plan.Get(key);string gate=key=="Wild"?"Progressive":"CHK_ProgressiveBST";
                var inputs=Field<Dictionary<string,FrameworkElement>>(wizard,"_inputs");
                check(!inputs.Keys.Any(k=>k.StartsWith("BST."))&&!Field<bool>(wizard,"_rangePage"),"Guidance_BstFieldsExcludedFromOptions_"+version+"_"+key);
                ((CheckBox)inputs[gate]).IsChecked=true;wizard.NextAsync().GetAwaiter().GetResult();
                var table=Field<ProgressiveBstTable>(wizard,"_bstTable");var grid=Field<DataGrid>(table,"_grid");
                var first=(ProgressiveBstTable.RangeRow)grid.Items[0];int min=first.MinBST;first.MinBST=first.MaxBST+1;
                wizard.NextAsync().GetAwaiter().GetResult();
                check(Field<bool>(wizard,"_rangePage")&&Field<GlobalTemplateDocument?>(wizard,"_document") is null&&!string.IsNullOrWhiteSpace(Field<TextBlock>(wizard,"_status").Text),"Guidance_InvalidBstRangeStaysOnTable_"+version+"_"+key);
                first.MinBST=min+1;
                check(Field<bool>(wizard,"_rangePage")&&table.Count>0,"Guidance_BstEnabledOpensNextTableStep_"+version+"_"+key);
                wizard.BackAsync().GetAwaiter().GetResult();
                ((CheckBox)Field<Dictionary<string,FrameworkElement>>(wizard,"_inputs")[gate]).IsChecked=false;wizard.NextAsync().GetAwaiter().GetResult();
                check(!Field<bool>(wizard,"_rangePage")&&Field<GlobalTemplateDocument>(wizard,"_document") is not null,"Guidance_BstDisabledSkipsTable_"+version+"_"+key);
                wizard.BackAsync().GetAwaiter().GetResult();((CheckBox)Field<Dictionary<string,FrameworkElement>>(wizard,"_inputs")[gate]).IsChecked=true;wizard.NextAsync().GetAwaiter().GetResult();
                table=Field<ProgressiveBstTable>(wizard,"_bstTable");check(table.Capture()["BST.0.MinBST"]==min+1,"Guidance_BstBackRetainsEditedRanges_"+version+"_"+key);
                typeof(ProgressiveBstTable).GetMethod("AddRange",Private)!.Invoke(table,null);int count=table.Count;
                if(version==GameVersion.USUM&&key=="Trainers")
                {
                    var tableRoot=(FrameworkElement)wizard.Content;tableRoot.Measure(new Size(860,700));tableRoot.Arrange(new Rect(0,0,860,700));tableRoot.UpdateLayout();
                    var image=new System.Windows.Media.Imaging.RenderTargetBitmap(860,700,96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(tableRoot);var tablePng=new System.Windows.Media.Imaging.PngBitmapEncoder();tablePng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    string folder=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"DefinitiveRandomizer-Guidance-QA");System.IO.Directory.CreateDirectory(folder);using var output=System.IO.File.Create(System.IO.Path.Combine(folder,"bst-step.png"));tablePng.Save(output);
                }
                wizard.NextAsync().GetAwaiter().GetResult();
                check(choice.Values.Keys.Count(k=>k.StartsWith("BST."))==count*5&&Field<GlobalTemplateDocument>(wizard,"_document").Actions.Count>0,"Guidance_BstAddedRangeIsCapturedInRecipe_"+version+"_"+key);wizard.Close();
            }
            var plan=new GuidedRandomizationPlan(fixture.Session);plan.Get("Marts").Selected=true;
            if(fixture.Game.Generation==6)Mart6Checks.Setup(fixture);
            plan.Initialize("Marts");var marts=plan.Get("Marts").Values;
            check(marts.GetValueOrDefault(fixture.Game.Generation==7?"SpecialsOnly":"SpecialOnly")==1&&marts.GetValueOrDefault(fixture.Game.Generation==7?"ProtectX":"KeepX")==0,"Guidance_MartScopeDefaultsSpecialWithoutXProtection_"+version);
            foreach(string key in new[]{"Personal","Moves"}){plan.Get(key).Selected=true;plan.Initialize(key);}
            plan.Get("Personal").Values["Abilities"]=1;
            foreach(string flag in new[]{"RandomRegular","RandomBP","Candies","Vitamins","FreeMega","BanCapsule","SpecialOnly","KeepX","Randomize","EVItems"})if(marts.ContainsKey(flag))marts[flag]=0;
            if(marts.ContainsKey("UseChecks"))marts["UseChecks"]=1;
            check(plan.BuildDocument().Actions.SequenceEqual(new[]{"personal.randomize"}),"Guidance_EmptyMovesAndMartsDoNotBlockOtherSections_"+version);
        }
        using var sample=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(sample);var main=new MainWindow();main.LoadProject(sample.Session);
        check(new[]{"HomeButton","ModeButton","GuidedButton","TourButton"}.All(name=>((Button)main.FindName(name)).Visibility==Visibility.Collapsed),"Guidance_DuplicateSidebarEntriesAreHidden");
        ((Button)main.FindName("BrandHomeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(((ContentControl)main.FindName("HomeHost")).Visibility==Visibility.Visible,"Guidance_BrandLogoAndNameReturnHome");
        typeof(MainWindow).GetMethod("OpenTour",Private)!.Invoke(main,new object?[]{null});
        var tour=Field<SpotlightTour>(main,"_tour");var root=(FrameworkElement)main.Content;Layout(root);
        check(ReferenceEquals(tour.Current.Target,main.FindName("BrandHomeButton"))&&!tour.HighlightBounds.IsEmpty,"Guidance_TourHighlightsActualControlBounds");
        tour.Move(1);Layout(root);
        root.Measure(new Size(640,480));root.Arrange(new Rect(0,0,640,480));root.UpdateLayout();
        var card=Field<Border>(tour,"_card");var cardBounds=card.TransformToAncestor(tour).TransformBounds(new Rect(card.RenderSize));
        check(cardBounds.Left>=0&&cardBounds.Top>=0&&cardBounds.Right<=tour.ActualWidth+1&&cardBounds.Bottom<=tour.ActualHeight+1,"Guidance_TourCardRemainsInsideSmallWindow");
        Layout(root);
        check(tour.Current.Target is StartView,"Guidance_TourExplainsRealHomeView");
        tour.Finish();
        check(((Grid)main.FindName("TourHost")).Children.Count==0&&((Grid)main.FindName("TourHost")).Visibility==Visibility.Collapsed&&sample.Session.History.Read().Entries.Count==0,"Guidance_SkipTourRemovesOverlayWithoutApplying");
        main.ChooseExperience(false);typeof(MainWindow).GetMethod("OpenTour",Private)!.Invoke(main,new object?[]{"Pokemon"});tour=Field<SpotlightTour>(main,"_tour");
        var steps=Field<IReadOnlyList<SpotlightStep>>(tour,"_steps");
        check(steps.Any(step=>step.Target is Expander)&&steps.Any(step=>ReferenceEquals(step.Target,main.FindName("EncountersButton")))&&steps.All(step=>!string.IsNullOrWhiteSpace(step.Text)),"Guidance_TourCoversRealSectionsAndSubsections");
        Layout(root);var png=new System.Windows.Media.Imaging.RenderTargetBitmap(1200,800,96,96,System.Windows.Media.PixelFormats.Pbgra32);png.Render(root);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(png));using(var output=System.IO.File.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"DefinitiveRandomizer-Guidance-QA","spotlight.png")))encoder.Save(output);
        tour.Finish();main.Close();
    }
}
