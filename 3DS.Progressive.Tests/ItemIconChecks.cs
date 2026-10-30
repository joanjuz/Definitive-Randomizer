using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThreeDS.Progressive;
internal static class ItemIconChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var sources=ItemIcons.Keys.Select(ItemIcons.Get).ToArray();
        check(sources.Length==33 && sources.All(x=>x.IsFrozen && x.PixelWidth>20 && x.PixelHeight>20),"ItemIcons_All33Crops_LoadedAndFrozen");
        check(ReferenceEquals(ItemIcons.Get("Dna"),ItemIcons.Get("Dna")),"ItemIcons_Cache_ReusesFrozenBitmap");
        foreach(var pair in new[]{("Pokémon","Pokeball"),("Entrenadores","Trainers"),("Tiendas","Bag"),("Evoluciones","Dna"),("Movimientos","Raid"),("MT y tutores","Scroll"),("Field Items","Incense"),("Global Template","Community"),("Batch ROM Builder","Group"),("Historial de cambios","Research")})
            check(ReferenceEquals(new NavigationIconConverter().Convert(pair.Item1,typeof(BitmapSource),null!,CultureInfo.InvariantCulture),ItemIcons.Get(pair.Item2)),"ItemIcons_Navigation_"+pair.Item2);
        check(sources.All(source=>{var rgba=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var pixels=new byte[source.PixelWidth*source.PixelHeight*4];rgba.CopyPixels(pixels,source.PixelWidth*4,0);bool clear=false,opaque=false;for(int i=3;i<pixels.Length;i+=4){clear|=pixels[i]==0;opaque|=pixels[i]>240;}return clear&&opaque;}),"ItemIcons_AllCrops_RetainTransparentMask");
        var app=(ThreeDS.Progressive.App)System.Windows.Application.Current;
        var button=new System.Windows.Controls.Button{Content="Evoluciones",Style=(Style)app.FindResource("SidebarButton")};
        var host=new System.Windows.Controls.Grid();host.Children.Add(button);var scope=new Window{Content=host};
        using var source=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Section icon checks"){WindowStyle=unchecked((int)0x80000000),Width=240,Height=50,PositionX=-32000,PositionY=-32000}){RootVisual=host};
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);host.Measure(new Size(240,50));host.Arrange(new Rect(0,0,240,50));host.UpdateLayout();
        var mask=Descendants<System.Windows.Shapes.Rectangle>(host).Single(r=>r.OpacityMask is ImageBrush);
        check(ReferenceEquals(((ImageBrush)mask.OpacityMask).ImageSource,ItemIcons.Get("Dna")) && Descendants<System.Windows.Controls.TextBlock>(host).Any(t=>t.Text=="Evoluciones") && System.Windows.Automation.AutomationProperties.GetName(button)=="Evoluciones","ItemIcons_Sidebar_PreservesLabelAndAccessibleName");
        check(System.Windows.Data.BindingOperations.IsDataBound(mask,System.Windows.Shapes.Shape.FillProperty) && ReferenceEquals(mask.Fill,button.Foreground),"ItemIcons_Sidebar_MaskUsesBoundForeground");
        try
        {
            AccessiblePalette.Update(app,true);System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.DataBind);host.UpdateLayout();
            check(ReferenceEquals(mask.Fill,button.Foreground) && ((SolidColorBrush)mask.Fill).Color==((SolidColorBrush)app.FindResource("TextMain")).Color,"ItemIcons_Sidebar_HighContrastTracksTextColor");
            button.IsEnabled=false;System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.DataBind);host.UpdateLayout();
            check(ReferenceEquals(mask.Fill,button.Foreground) && ((SolidColorBrush)mask.Fill).Color==((SolidColorBrush)app.FindResource("DisabledText")).Color,"ItemIcons_Sidebar_DisabledTracksTextColor");
        }
        finally{AccessiblePalette.Update(app,false);source.RootVisual=null;scope.Close();}
        CaptureGallery();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}}
    private static void CaptureGallery()
    {
        const int columns=6,cell=120;int height=((ItemIcons.Keys.Count+columns-1)/columns)*cell;
        var visual=new DrawingVisual();using(var draw=visual.RenderOpen())
        {
            draw.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(23,32,43)),null,new Rect(0,0,columns*cell,height));
            int i=0;foreach(string key in ItemIcons.Keys)
            {
                var icon=ItemIcons.Get(key);double scale=64d/Math.Max(icon.PixelWidth,icon.PixelHeight),w=icon.PixelWidth*scale,h=icon.PixelHeight*scale;int x=i%columns*cell,y=i/columns*cell;
                draw.DrawImage(icon,new Rect(x+(cell-w)/2,y+8+(64-h)/2,w,h));
                var label=new FormattedText(key,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.White,1);
                draw.DrawText(label,new System.Windows.Point(x+(cell-label.Width)/2,y+84));i++;
            }
        }
        var image=new RenderTargetBitmap(columns*cell,height,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));string directory=Path.Combine(AppContext.BaseDirectory,"visual-redesign");Directory.CreateDirectory(directory);using var file=File.Create(Path.Combine(directory,"item-icons.png"));encoder.Save(file);
    }
}
