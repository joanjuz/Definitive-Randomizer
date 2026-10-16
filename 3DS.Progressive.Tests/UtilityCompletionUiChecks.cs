using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pk3DS.Core;
using ThreeDS.Progressive;

internal static class UtilityCompletionUiChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.XY);ModuleChecks.CreateArchives(f);
        byte[] icon=new byte[0x36C0];"SMDH"u8.CopyTo(icon);File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"icon.bin"),icon);
        var view=new IconView(f.Session);var window=new Window{Width=1150,Height=820,Content=view};window.Show();
        try
        {
            check(view.ConfirmLeave(),"WPF SMDH clean editor permits navigation");
            check(Equals(view.Background,System.Windows.Application.Current.FindResource("Bg")),"WPF SMDH uses application dark background");
            Capture(view,"smdh-editor.png");
            var restore=new SelectedRestoreView(f.Session,()=>Task.CompletedTask);window.Content=restore;
            var grid=(DataGrid)typeof(SelectedRestoreView).GetField("_grid",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(restore)!;
            check(Equals(grid.Background,System.Windows.Application.Current.FindResource("Panel"))&&grid.ColumnHeaderStyle is not null,"WPF selective restore uses dark grid headers and background");
            check(restore.ConfirmLeave(),"WPF selective restore clean view permits navigation");
            typeof(SelectedRestoreView).GetField("_busy",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(restore,true);
            check(!restore.ConfirmLeave(),"WPF selective restore blocks navigation during active work");
            typeof(SelectedRestoreView).GetField("_busy",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(restore,false);
            grid.ItemsSource=new[]{new RestoreFileRow(new ThreeDS.Progressive.Services.BackupFile("RomFS/a/1/6/5",1024,new string('0',64)))};
            Capture(restore,"restauracion-selectiva.png");
            check(grid.Columns[1].ActualWidth>=350,"WPF selective restore keeps file paths readable with empty or populated grid");
        }
        finally{window.Close();}
        var main=new MainWindow();main.Show();
        try
        {
            main.LoadProject(f.Session);
            foreach(var (section,type) in new[]{("SMDH",typeof(IconView)),("RestoreSelected",typeof(SelectedRestoreView)),("Graphics",typeof(GraphicsView)),("GarcShuffle",typeof(GarcShuffleView))})
            {
                typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,[section]);
                check(((ContentControl)main.FindName("SectionHost")).Content?.GetType()==type,"WPF tools button routing opens "+section);
            }
        }
        finally{main.Close();}
    }
    private static void Capture(FrameworkElement view,string name)
    {
        string? dir=Environment.GetEnvironmentVariable("PROGRESSIVE_UI_CAPTURE_DIR");if(string.IsNullOrWhiteSpace(dir))return;
        view.Measure(new Size(1100,740));view.Arrange(new Rect(0,0,1100,740));view.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),(int)Math.Ceiling(view.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(view);
        Directory.CreateDirectory(dir);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(dir,name));encoder.Save(output);
    }
}
