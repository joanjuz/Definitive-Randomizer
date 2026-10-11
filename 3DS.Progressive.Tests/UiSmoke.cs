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
            check(((SolidColorBrush)border.Background).Color==Color.FromRgb(50,35,45),"WPF disabled action has subdued wine background Gen "+generation);
        }
        using(var fixture = new Fixture(GameVersion.USUM))
        {
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
