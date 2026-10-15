using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pk3DS.Core;
using ThreeDS.Progressive;

internal static class RomToolUiChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var main=new MainWindow();main.Show();
        try
        {
            var button=(Button)main.FindName("ExtractRomButton");
            check(button.IsEnabled,"WPF extraction is available before opening a project");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var view=((ContentControl)main.FindName("SectionHost")).Content as RomExtractionView;
            check(view is not null,"WPF extraction opens in the main window without a project");
            var field=typeof(MainWindow).GetField("_romExtractionView",BindingFlags.Instance|BindingFlags.NonPublic)!;
            check(ReferenceEquals(field.GetValue(main),view),"WPF retains active extraction view for navigation and close checks");
            if(view is not null)
            {
                Capture(view,"extraccion-rom.png");
                var cancelField=typeof(RomExtractionView).GetField("_cancellation",BindingFlags.Instance|BindingFlags.NonPublic)!;
                using var cancel=new CancellationTokenSource();cancelField.SetValue(view,cancel);
                check(!view.ConfirmLeave()&&!(bool)typeof(MainWindow).GetMethod("ConfirmAuxiliaryLeave",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!,
                    "WPF active extraction blocks navigation and closing");
                cancelField.SetValue(view,null);
                check(view.ConfirmLeave(),"WPF completed extraction allows navigation");
            }
            using var f=new Fixture(GameVersion.USUM);
            var cro=new CroIntegrityView(f.Session);
            var cr=(CheckBox)typeof(CroIntegrityView).GetField("_crr",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(cro)!;
            var apply=(Button)typeof(CroIntegrityView).GetField("_apply",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(cro)!;
            check(cr.IsChecked==false&&!apply.IsEnabled,"WPF CRO/CRR defaults to vanilla CRO-only and no apply before preview");
            cr.IsChecked=true;check(!apply.IsEnabled,"WPF CRR option changes invalidate proposals");
            var grid=(DataGrid)typeof(CroIntegrityView).GetField("_changes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(cro)!;
            check(Equals(grid.Background,System.Windows.Application.Current.FindResource("Panel"))&&grid.ColumnHeaderStyle is not null&&grid.CellStyle is not null,"WPF CRO grid applies dark background headers and cells");
            Capture(cro,"cro-crr-vacio.png");
            grid.ItemsSource=new[]{new ThreeDS.Progressive.Models.BatchChange(0,"DllBattle.cro","SHA-256","Original","Reconstruido"),new ThreeDS.Progressive.Models.BatchChange(1,"DllField.cro","SHA-256","Original","Reconstruido")};grid.SelectedIndex=0;
            Capture(cro,"cro-crr.png");
            byte[] table=new byte[28];BitConverter.GetBytes(3).CopyTo(table,0);table[4]=17;table[16]=18;table.AsSpan(6,10).Fill(25);table.AsSpan(18,10).Fill(75);
            string pickupPath=Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("pickup"));f.WriteGarc(pickupPath,[table],f.Game.GetGARCReference("pickup"));
            typeof(MainWindow).GetField("_session",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,f.Session);
            typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,new object[]{"Pickup"});
            check(((ContentControl)main.FindName("SectionHost")).Content is PickupView&&ReferenceEquals(typeof(MainWindow).GetField("_pickupView",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main),((ContentControl)main.FindName("SectionHost")).Content),"WPF Pickup routing retains view for unsaved navigation guard");
            var pickup=new PickupView(f.Session);check(pickup.ConfirmLeave(),"WPF Pickup clean editor permits navigation");Capture(pickup,"pickup-gen7.png");
            using var gen6=new Fixture(GameVersion.XY);byte[] code=new byte[0x401000];ThreeDS.Progressive.Services.GamePickupService.Signature.CopyTo(code,0x400100);File.WriteAllBytes(Path.Combine(gen6.Game.ExeFS,"code.bin"),code);
            var pickup6=new PickupView(gen6.Session);check(pickup6.ConfirmLeave(),"WPF Gen6 Pickup opens fixed common and rare table");Capture(pickup6,"pickup-gen6.png");
            Machine6Checks.Setup(gen6);main.LoadProject(gen6.Session);
            check(((Button)main.FindName("MachineTutorButton")).IsEnabled,"WPF Gen6 MT/MO navigation enabled");
            typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,new object[]{"MachineTutor"});
            var machinePanel=((ContentControl)main.FindName("SectionHost")).Content as Gen6MachineView;
            var machine=machinePanel?.Editor;
            check(machine?.Editor is ThreeDS.Progressive.Services.GameMachine6Service&&ReferenceEquals(typeof(MainWindow).GetField("_recordView",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main),machine),"WPF Gen6 MT/MO uses integrated editor and unsaved navigation guard");
            if(machinePanel is not null)Capture(machinePanel,"mt-mo-gen6.png");
            Mart6Checks.Setup(gen6);main.LoadProject(gen6.Session);
            check(((Button)main.FindName("MartsButton")).IsEnabled,"WPF Gen6 stores navigation enabled");
            typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,new object[]{"Marts"});
            var mart=((ContentControl)main.FindName("SectionHost")).Content as Gen6MartView;
            check(mart?.Editor is ThreeDS.Progressive.Services.GameMart6Service&&ReferenceEquals(typeof(MainWindow).GetField("_mart6View",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main),mart)&&typeof(MainWindow).GetField("_martView",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main) is null,"WPF Gen6 stores retain integrated editor without stale USUM view");
            if(mart is not null)Capture(mart,"tiendas-gen6.png");
            main.LoadProject(gen6.Session);
            typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,new object[]{"CatchZones"});
            var catchView=((ContentControl)main.FindName("SectionHost")).Content as CatchZonesView;
            check(catchView is not null && ReferenceEquals(typeof(MainWindow).GetField("_catchZonesView",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main),catchView),"WPF Catch Zones navigation retains view for busy guard");
            if(catchView is not null)
            {
                var catchGrid=(DataGrid)typeof(CatchZonesView).GetField("_changes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(catchView)!;
                var catchApply=(Button)typeof(CatchZonesView).GetField("_apply",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(catchView)!;
                check(!catchApply.IsEnabled && Equals(catchGrid.Background,System.Windows.Application.Current.FindResource("Panel")),"WPF Catch Zones requires preview and uses dark grid");
                typeof(CatchZonesView).GetField("_busy",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(catchView,true);
                check(!catchView.ConfirmLeave(),"WPF Catch Zones busy operation prevents navigation");
                typeof(CatchZonesView).GetField("_busy",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(catchView,false);
                Capture(catchView,"catch-zones-gen6.png");
            }
            Economy6Checks.Setup(gen6);
            var economy6=new EconomyPriceView(new ThreeDS.Progressive.Services.GameItemService(gen6.Session));
            var economyRows=(System.Collections.ObjectModel.ObservableCollection<ThreeDS.Progressive.Services.ItemPriceRow>)typeof(EconomyPriceView).GetField("_rows",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(economy6)!;
            check(economyRows.Count(r=>r.Included)==123 && economyRows.First(r=>r.Id==3).TargetBuy==150,"WPF Gen6 economy loads historical preset in editable table");
            Capture(economy6,"economia-gen6.png");
            Trade6Checks.Setup(gen6);main.LoadProject(gen6.Session);
            typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,new object[]{"Trade6"});
            var trade=((ContentControl)main.FindName("SectionHost")).Content as RecordEditorView;
            check(trade?.Editor is ThreeDS.Progressive.Services.GameTrade6Service && ReferenceEquals(typeof(MainWindow).GetField("_recordView",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main),trade),"WPF Gen6 trades integrated editor and unsaved navigation guard");
            if(trade is not null)Capture(trade,"intercambios-gen6.png");
            OPowerChecks.Setup(gen6);main.LoadProject(gen6.Session);
            typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,new object[]{"OPower"});
            var opower=((ContentControl)main.FindName("SectionHost")).Content as RecordEditorView;
            check(opower?.Editor is ThreeDS.Progressive.Services.GameOPowerService&&ReferenceEquals(typeof(MainWindow).GetField("_recordView",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main),opower),"WPF O-Powers uses integrated editor and unsaved guard");
            if(opower is not null){check(((FrameworkElement)opower.FindName("RandomizerButton")).Visibility==Visibility.Collapsed&&((FrameworkElement)opower.FindName("ScopeBox")).Visibility==Visibility.Collapsed,"WPF O-Powers hides nonexistent randomizer and scope");Capture(opower,"o-powers-gen6.png");}
        }
        finally{main.Close();}
    }
    private static void Capture(FrameworkElement view,string name)
    {
        view.Measure(new Size(1100,690));view.Arrange(new Rect(0,0,1100,690));view.UpdateLayout();
        string? target=Environment.GetEnvironmentVariable("PROGRESSIVE_UI_CAPTURE_DIR");if(string.IsNullOrEmpty(target))return;
        Directory.CreateDirectory(target);var bitmap=new RenderTargetBitmap(1100,690,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(target,name));encoder.Save(stream);
    }
}
