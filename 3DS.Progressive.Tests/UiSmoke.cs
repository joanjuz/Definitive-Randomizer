using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;

internal static class UiSmoke
{
    internal static void Run(Action<bool,string> check)
    {
        // Run on an STA Windows thread. No ROM or visible window is needed.
        var app = new ThreeDS.Progressive.App();
        app.InitializeComponent();
        var main = new MainWindow();
        main.Content.As<FrameworkElement>().Measure(new Size(1390,845));
        main.Content.As<FrameworkElement>().Arrange(new Rect(0,0,1390,845));
        check(main.FindName("PersonalToolsHost") is ContentControl { Content: BatchToolsView },"WPF main shell initializes batch view");
        foreach (var generation in new[] {6,7})
        {
            var tools = new BatchToolsView();
            tools.Configure(false,generation,generation==6,true,false,()=>true,_=>[1],
                (_,s,scope)=>new BatchPreview(Guid.NewGuid(),[1],[[3]],"Smoke",s.Seed,scope.Count,[new BatchChange(1,"Demo","Tipo","A","B")]),_=>{},_=>"Table");
            var checks=(IDictionary)typeof(BatchToolsView).GetField("_checks",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(tools)!;
            check(checks.Contains("Hms")== (generation==6),"WPF HM options match generation "+generation);
            var apply=(Button)tools.FindName("ApplyButton");
            check(!apply.IsEnabled,"WPF Apply disabled before preview Gen "+generation);
            var seed=(TextBox)tools.FindName("SeedBox");
            var preview=(Button)tools.FindName("PreviewButton");
            seed.Text="invalid";preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!apply.IsEnabled && ((TextBlock)tools.FindName("ValidationText")).Text.Contains("Semilla"),"WPF exact invalid seed reported Gen "+generation);
            seed.Text="123";preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(apply.IsEnabled,"WPF frozen preview enables Apply Gen "+generation);
            seed.Text="124";
            check(!apply.IsEnabled,"WPF seed change invalidates proposal Gen "+generation);
            ((ComboBox)tools.FindName("ActionBox")).SelectedIndex=1;
            checks=(IDictionary)typeof(BatchToolsView).GetField("_checks",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(tools)!;
            check(checks.Contains("SetCall")==(generation==7),"WPF SOS modifier matches generation "+generation);
        }
        using(var fixture = new Fixture(GameVersion.USUM))
        {
            var view = new MoveEditorView(fixture.Session);
            view.Measure(new Size(1170,720));view.Arrange(new Rect(0,0,1170,720));
            check(!Walk(view).OfType<Button>().Any(b => Equals(b.Content,"Editor integrado · 3DS Progressive")),"WPF obsolete integrated editor badge removed");
            check(view.FindName("MoveToolsHost") is ContentControl { Content: BatchToolsView },"WPF real move editor initializes inline tools");
            var tools=(BatchToolsView)((ContentControl)view.FindName("MoveToolsHost")).Content;
            ((ComboBox)tools.FindName("ActionBox")).SelectedIndex=1;
            check(!((TextBox)tools.FindName("SeedBox")).IsEnabled,"WPF Metronome has no irrelevant seed input");
            ((Button)tools.FindName("PreviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(((Button)tools.FindName("ApplyButton")).IsEnabled,"WPF real move preview enables Apply");
            ((TextBox)view.FindName("MovePower")).Text="81";
            check(!((Button)tools.FindName("ApplyButton")).IsEnabled,"WPF editing move invalidates proposal");
        }
        main.Close();app.Shutdown();
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Walk(System.Windows.Media.VisualTreeHelper.GetChild(root,i))) yield return child;
    }

    private static T As<T>(this object value) => (T)value;
}
