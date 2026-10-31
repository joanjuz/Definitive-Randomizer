using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;

// DS randomizer window and home entry, rendered off-screen. RunUi is synthetic (CI); Probe drives the window with a real dump.
internal static class NdsUiChecks
{
    internal static void RunUi(Action<bool,string> check)
    {
        bool opened=false;var start=new StartView(null,false,_=>{},()=>{},()=>{},()=>{},null,()=>opened=true);
        var entry=Buttons(start).SingleOrDefault(b=>AutomationProperties.GetName(b).Contains("Nintendo DS"));
        check(entry is not null&&!Buttons(new StartView(null,false,_=>{},()=>{},()=>{},()=>{})).Any(b=>AutomationProperties.GetName(b).Contains("Nintendo DS")),"NdsUi_HomeEntryAppearsOnlyWhenProvided");
        entry!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));check(opened,"NdsUi_HomeEntryOpensDsRandomizer");

        var window=new NdsRandomizerWindow();var combos=Descendants<ComboBox>(window).ToArray();
        check(!window.CanExport&&Buttons(window).Single(b=>AutomationProperties.GetName(b)=="Generar vista previa de cambios").IsEnabled==false,"NdsUi_StartsWithoutPreviewOrExport");
        check(combos.Length==8&&combos.Skip(6).All(c=>c.Items.Count==2&&(string)c.SelectedItem=="Sin cambios")&&combos.Take(2).All(c=>c.Items.Count==4)&&(string)combos[0].SelectedItem=="BST progresivo por nivel"&&(string)combos[1].SelectedItem=="BST similar","NdsUi_DefaultModesAreProgressiveTrainersAndSimilarWild");
        check((string)combos[2].SelectedItem=="Sin cambios"&&combos.Skip(3).Take(3).All(c=>!c.IsEnabled),"NdsUi_StartersDefaultToUnchangedAndPicksDisabled");
        check(Descendants<FrameworkElement>(window).Where(e=>e is Button or ComboBox or System.Windows.Controls.TextBox or System.Windows.Controls.ListBox).All(e=>AutomationProperties.GetName(e).Length>0),"NdsUi_EveryControlHasAnAccessibleName");
        // Same capture folder the CI uploads; renders at the default and at the minimum width.
        if(Environment.GetEnvironmentVariable("PROGRESSIVE_UI_CAPTURE_DIR") is {Length:>0} captures)
            foreach(var (width,height) in new[]{(window.Width,window.Height),(window.MinWidth,window.MinHeight)})
            {
                var root=(FrameworkElement)window.Content;root.Measure(new System.Windows.Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)width,(int)height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                var frame=new System.Windows.Shapes.Rectangle{Width=width,Height=height,Fill=window.Background};frame.Measure(new System.Windows.Size(width,height));frame.Arrange(new Rect(0,0,width,height));bitmap.Render(frame);bitmap.Render(root);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(captures);using var file=File.Create(Path.Combine(captures,$"nds-randomizer-{(int)width}.png"));encoder.Save(file);
            }

        string temp=Path.Combine(Path.GetTempPath(),"nds-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            // Correct game code but tables that do not match the profile: rejected without enabling anything.
            string fake=Path.Combine(temp,"fake.nds");File.WriteAllBytes(fake,NdsGen5RandomizerChecks.BuildRom(new Dictionary<string,byte[]>{["a/0/0/2"]=NdsGen5RandomizerChecks.BuildNarc([])},"IRBO"));
            Pump(window.LoadAsync(fake));
            check(window.Session is null&&window.Status.StartsWith("No se puede usar esta ROM",StringComparison.Ordinal)&&!window.CanExport,"NdsUi_RejectsRomWhoseTablesDoNotMatch");
            // With "Admitir romhacks", a structurally valid ROM opens as a romhack; options whose code is not original stay off.
            var romhacks=Descendants<System.Windows.Controls.CheckBox>(window).Single();romhacks.IsChecked=true;
            string hack=Path.Combine(temp,"romhack.nds");File.WriteAllBytes(hack,NdsGen5RandomizerChecks.SyntheticBlackRomhack());
            Pump(window.LoadAsync(hack));var hackCombos=Descendants<ComboBox>(window).ToArray();
            check(window.Session is {IsRomhack:true}&&!hackCombos[2].IsEnabled&&!hackCombos[7].IsEnabled&&hackCombos[6].IsEnabled&&hackCombos[0].IsEnabled,"NdsUi_RomhackOptionOpensModifiedRomWithCodeOptionsOff");
            // A ROM whose tables do not parse is still rejected; the ROM already open stays loaded.
            var loaded=window.Session;Pump(window.LoadAsync(fake));
            check(ReferenceEquals(window.Session,loaded)&&window.Status.StartsWith("No se puede usar esta ROM",StringComparison.Ordinal),"NdsUi_RomhackOptionStillRejectsTablesThatDoNotParse");
        }
        finally{window.Close();Directory.Delete(temp,true);}
    }

    // Full flow through the window with a real Black/White dump; writes only to a new output folder.
    internal static void Probe(string rom,string output)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");Directory.CreateDirectory(output);
        string before=Hash(rom);var window=new NdsRandomizerWindow();
        try
        {
            Pump(window.LoadAsync(rom));Check(window.Session is {KnownDump:true},$"NdsUi_Real_LoadsVerifiedOriginal ({window.Session?.Game})");
            Pump(window.PreviewAsync());var first=window.CurrentPreview;Check(first is not null&&first.Result.Changes.Count>0&&window.CanExport,$"NdsUi_Real_PreviewEnablesExport ({first?.Result.Changes.Count} cambios)");
            var trainers=Descendants<ComboBox>(window).First();trainers.SelectedIndex=0;
            Check(window.CurrentPreview is null&&!window.CanExport,"NdsUi_Real_ChangingAnOptionDiscardsThePreview");
            Pump(window.PreviewAsync());Check(window.CurrentPreview is not null&&window.CurrentPreview.Result.Changes.All(c=>c.Location.StartsWith("Encuentros",StringComparison.Ordinal)),"NdsUi_Real_KeepTrainersOnlyChangesEncounters");
            var combos=Descendants<ComboBox>(window).ToArray();var session=window.Session!;
            combos[2].SelectedIndex=2;
            Check(combos.Skip(3).Take(3).All(c=>c.IsEnabled)&&combos.Skip(3).Take(3).Select(c=>c.SelectedIndex+1).SequenceEqual(session.OriginalStarters)&&window.CurrentPreview is null,"NdsUi_Real_CustomStartersStartFromTheOriginals");
            combos[3].SelectedIndex=24;combos[4].SelectedIndex=3;combos[5].SelectedIndex=6;Pump(window.PreviewAsync());
            int starterOverlay=session.Data?.Profile.StarterCryOverlay??session.Gen4!.Profile.StarterOverlay;
            Check(window.CurrentPreview is {Options.Starters:Gen5StarterMode.Custom}&&window.CurrentPreview.Options.CustomStarters!.SequenceEqual(new[]{25,4,7})&&window.CurrentPreview.Result.Overlays.ContainsKey(starterOverlay),"NdsUi_Real_CustomStartersReachThePreview");
            combos[6].SelectedIndex=1;combos[7].SelectedIndex=1;Pump(window.PreviewAsync());
            // Gen4 keeps hidden items and shops in the ARM9; Gen5 in a shop overlay (Black/White) or the shop NARC (White 2).
            bool shopsWritten=session.Gen4 is not null?window.CurrentPreview?.Result.Arm9 is not null
                :session.Data!.Profile.ShopOverlay<0?window.CurrentPreview!.Result.Files.ContainsKey(session.Data.PathOf(Gen5Profile.ShopItems)):window.CurrentPreview!.Result.RecompressOverlays.Contains(21);
            Check(window.CurrentPreview is {Options.FieldItems:Gen5ItemMode.Shuffle,Options.Shops:Gen5ItemMode.Shuffle}&&shopsWritten,"NdsUi_Real_ItemAndShopOptionsReachThePreview");
            string target=Path.Combine(output,"random.nds");Pump(window.ExportAsync(target));
            Check(File.Exists(target)&&File.Exists(Path.ChangeExtension(target,".cambios.txt"))&&window.Status.StartsWith("ROM exportada",StringComparison.Ordinal),"NdsUi_Real_ExportWritesRomAndChangeLog");
        }
        finally{window.Close();}
        Check(Hash(rom)==before,"NdsUi_Real_SourceUnchanged");
        foreach(string f in Directory.GetFiles(output))File.Delete(f);
        Console.WriteLine($"Completed {checks} DS randomizer window checks (off-screen; outputs deleted).");
    }

    private static IEnumerable<Button> Buttons(DependencyObject root)=>Descendants<Button>(root);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        foreach(object child in LogicalTreeHelper.GetChildren(root))
            if(child is DependencyObject d){if(d is T t)yield return t;foreach(var nested in Descendants<T>(d))yield return nested;}
    }
    private static void Pump(Task task){var time=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(time.Elapsed.TotalSeconds>120)throw new TimeoutException();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);}task.GetAwaiter().GetResult();}
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
}
