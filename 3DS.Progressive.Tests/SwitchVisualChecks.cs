using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Switch;
internal static class SwitchVisualChecks
{
    private static void Pump(Task task){var time=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(time.Elapsed.TotalSeconds>60)throw new TimeoutException();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);}task.GetAwaiter().GetResult();}
    private static MainWindow Open(out SwitchWorkspaceView workspace)
    {
        var window=new MainWindow();typeof(MainWindow).GetMethod("OpenSwitchPrototype",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);workspace=(SwitchWorkspaceView)((ContentControl)window.FindName("SwitchHost")).Content;return window;
    }
    internal static void RunUi(Action<bool,string> check)
    {
        var window=Open(out var workspace);try
        {
            check(((ContentControl)window.FindName("HomeHost")).Visibility==Visibility.Collapsed&&((Grid)window.FindName("EditorShell")).Visibility==Visibility.Collapsed,"Switch_Visual_HomeAnd3dsShellAreNotPaintedBehindWorkspace");
            check(workspace.Background is SolidColorBrush b&&b.Color.A==255,"Switch_Visual_WorkspaceHasOpaqueThemeSurface");
            check(new[]{"BackupButton","ExtractRomButton","RestoreBackupButton"}.All(name=>((System.Windows.Controls.Button)window.FindName(name)).Visibility==Visibility.Collapsed),"Switch_Visual_HeaderContainsOnlyRelevantActions");
            ((System.Windows.Controls.Button)window.FindName("BrandHomeButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));check(((ContentControl)window.FindName("HomeHost")).Visibility==Visibility.Visible&&new[]{"BackupButton","ExtractRomButton","RestoreBackupButton"}.All(name=>((System.Windows.Controls.Button)window.FindName(name)).Visibility==Visibility.Visible),"Switch_Visual_HomeRestoresHeaderAndExclusiveSurface");
        }finally{window.Close();}
    }
    internal static void Preview(string root,string output)
    {
        Directory.CreateDirectory(output);Exception? failure=null;var thread=new Thread(()=>{try
        {
            using var bindingLog=new StringWriter();var listener=new System.Diagnostics.TextWriterTraceListener(bindingLog);var bindingSource=System.Diagnostics.PresentationTraceSources.DataBindingSource;var previousLevel=bindingSource.Switch.Level;bindingSource.Switch.Level=System.Diagnostics.SourceLevels.Error;bindingSource.Listeners.Add(listener);
            var app=new App();app.InitializeComponent();WpfTestHost.SuppressStartup();StudioDialog.AutomatedChecks=true;var window=Open(out var workspace);try
            {
                workspace.PresentProject(SwordShieldProject.Open(root));System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);
                void Capture(string label,int width,int height)
                {
                    var surface=(FrameworkElement)window.Content;surface.Measure(new Size(width,height));surface.Arrange(new Rect(0,0,width,height));surface.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);surface.UpdateLayout();var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));string destination=Path.Combine(output,label+"-"+width+".png");using var stream=new FileStream(destination,FileMode.CreateNew);encoder.Save(stream);Console.WriteLine("Rendered "+destination);
                }
                foreach(var route in new[]{"Pokemon","Trainers","Encounters","Moves","Learnsets","Machines","Evolutions","Shops","Items","FieldItems","Runtime","Caps","Tools","GlobalTemplate","BatchRomBuilder"})
                {
                    Pump(workspace.NavigateAsync(route));foreach(var size in route=="Pokemon"?new[]{(900,620),(1280,800),(1600,900)}:new[]{(1280,800)})
                    {
                        var surface=(FrameworkElement)window.Content;surface.Measure(new Size(size.Item1,size.Item2));surface.Arrange(new Rect(0,0,size.Item1,size.Item2));surface.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);surface.UpdateLayout();
                        var image=new RenderTargetBitmap(size.Item1,size.Item2,96,96,PixelFormats.Pbgra32);image.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));string path=Path.Combine(output,route+"-"+size.Item1+".png");using var file=new FileStream(path,FileMode.CreateNew);png.Save(file);Console.WriteLine("Rendered "+path);
                    }
                }
                Pump(workspace.NavigateAsync("Trainers"));var trainer=(RecordEditorView)((ContentControl)workspace.FindName("SwitchWorkspacePage")).Content;((System.Windows.Controls.Button)trainer.FindName("RandomizerButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Capture("Trainers-options",1280,800);foreach(var rule in new[]{("TrainerCapsButton","Trainers-caps-table")}){((System.Windows.Controls.Button)trainer.FindName(rule.Item1)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Capture(rule.Item2,1280,800);((ContentControl)trainer.FindName("TrainerRulesHost")).Visibility=Visibility.Collapsed;((ContentControl)trainer.FindName("TrainerRulesHost")).Content=null;}var optionTabs=((StackPanel)trainer.FindName("OptionsPanel")).Children.OfType<System.Windows.Controls.TabControl>().Single();optionTabs.SelectedIndex=2;((System.Windows.Controls.CheckBox)trainer.OptionControls["GlobalMoves.Better"]).IsChecked=true;((System.Windows.Controls.CheckBox)trainer.OptionControls["GlobalMoves.StrongStat"]).IsChecked=true;Capture("Trainers-moves-options",1280,800);optionTabs.SelectedIndex=3;((System.Windows.Controls.CheckBox)trainer.OptionControls["GlobalItems.Smart"]).IsChecked=true;Capture("Trainers-items-options",1280,800);((System.Windows.Controls.CheckBox)trainer.OptionControls["CHK_ProgressiveBST"]).IsChecked=true;((StackPanel)trainer.FindName("OptionsPanel")).Children.OfType<System.Windows.Controls.TabControl>().Single().SelectedIndex=4;Capture("Trainers-bst",1280,800);
                var session=workspace.Project!;session.ApplyData(session.PreviewData(pk3DS.Core.NX.SwordShieldDataSection.Learnsets,null,new pk3DS.Core.NX.SwordShieldDataOptions(12345)));Pump(workspace.NavigateAsync("History"));Capture("History-paged",1280,800);Pump(workspace.NavigateAsync("Evolutions"));Capture("Evolutions-trade-action",1280,800);
            }finally{window.Close();bindingSource.Listeners.Remove(listener);bindingSource.Switch.Level=previousLevel;}
            if(bindingLog.GetStringBuilder().Length>0)throw new InvalidDataException("Binding errors in Switch render: "+bindingLog);Console.WriteLine("PASS Switch_Visual_RenderedSharedViewsHaveNoBindingErrors");
        }catch(Exception e){failure=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure is not null)throw failure;
    }
}
