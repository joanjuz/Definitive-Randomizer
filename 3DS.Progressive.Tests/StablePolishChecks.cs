using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;

/// <summary>
/// Interface polish for the stable release (off-screen, synthetic projects): tutor actions versus recipe options, the remembered work mode, the single
/// universal «Abrir proyecto» on Inicio, and «Deshacer» staying in History. No ROM and no gameplay.
/// </summary>
internal static class StablePolishChecks
{
    private const System.Reflection.BindingFlags Private=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;

    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        StudioDialog.AutomatedChecks=true;
        var app=System.Windows.Application.Current??new ThreeDS.Progressive.App();
        if(app is ThreeDS.Progressive.App own&&own.Resources.MergedDictionaries.Count==0)own.InitializeComponent();
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Tutors(check);
        WorkMode(check);
        Start(check);
        OpenDialogContrast(check);
        StartupArgument(check);
        Undo(check);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        // Logical tree too: collapsed cards and controls inside them are not in the visual tree until shown.
        var seen=new HashSet<DependencyObject>();var stack=new Stack<DependencyObject>();stack.Push(root);
        while(stack.Count>0)
        {
            var node=stack.Pop();if(!seen.Add(node))continue;
            if(node is T match)yield return match;
            if(node is Visual or System.Windows.Media.Media3D.Visual3D)
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)stack.Push(VisualTreeHelper.GetChild(node,i));
            foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())stack.Push(child);
        }
    }
    private static T Named<T>(DependencyObject root,string name) where T:FrameworkElement=>Descendants<T>(root).Single(e=>e.Name==name);
    private static bool Inside(DependencyObject root,FrameworkElement element)=>Descendants<FrameworkElement>(root).Contains(element);
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    private static string Sha(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>A USUM Shop.cro with the 67 BP tutors (4 groups) that the full tutor editor requires, same synthetic layout as the mint checks.</summary>
    internal static byte[] UsumShop67()
    {
        byte[] b=new byte[0x8000];void U(int at,uint v)=>BitConverter.GetBytes(v).CopyTo(b,at);
        "CRO0"u8.CopyTo(b.AsSpan(0x80));U(0x90,(uint)b.Length);
        U(0xb0,0x200);U(0xb4,0x3e00);U(0xb8,0x6000);U(0xbc,0x1000);U(0xc0,0x4000);U(0xc4,0x2000);
        U(0xc8,0x7000);U(0xcc,4);U(0x128,0x7100);
        U(0x7000,0x200);U(0x7004,0x3e00);U(0x7008,0);U(0x700c,0x4000);U(0x7010,0x2000);U(0x7014,1);
        U(0x7018,0x6000);U(0x701c,0x1000);U(0x7020,2);U(0x702c,3);
        int relocation=0;
        void Ref(int target){int at=0x7100+12*relocation;U(at,(uint)((0x100+4*relocation)<<4));U(at+4,0x102);U(at+8,(uint)(target-0x4000));relocation++;}
        int counts=0x4100;int[] tutors=[17,17,17,16],bp=[6,7,8,9,10,11,12];
        for(int i=0;i<4;i++)b[counts+i]=(byte)tutors[i];for(int i=0;i<7;i++)b[counts+4+i]=(byte)bp[i];
        b.AsSpan(counts+11,28).Fill(2);b[counts+39]=0xff;
        Ref(counts);Ref(counts+4);Ref(counts+11);
        for(int i=0;i<28;i++){Ref(0x4200+4*i);BitConverter.GetBytes((ushort)48).CopyTo(b,0x4200+4*i);BitConverter.GetBytes((ushort)17).CopyTo(b,0x4202+4*i);}
        int move=1;
        void Group(int start,int[] sizes,bool tutor){int cursor=start;foreach(int size in sizes){Ref(cursor);for(int i=0;i<size;i++){BitConverter.GetBytes((ushort)(tutor?move++:18)).CopyTo(b,cursor);BitConverter.GetBytes((ushort)(tutor?8+i:12)).CopyTo(b,cursor+2);cursor+=4;}}}
        Group(0x4800,bp,false);Group(0x4c00,tutors,true);U(0x12c,(uint)relocation);return CroIntegrity.Rebuild(b);
    }

    private static void TmCode(Fixture f)
    {
        byte[] code=new byte[0x402000];byte[] signature=[0x03,0x40,0x03,0x41,0x03,0x42,0x03,0x43,0x03];signature.CopyTo(code,0x400000);
        int offset=0x400000+signature.Length+(f.Game.USUM?0x22:0);for(int i=0;i<100;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(code,offset+i*2);
        File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"),code);
    }

    private static void Tutors(Action<bool,string> check)
    {
        using(var f=new Fixture(GameVersion.USUM))
        {
            ModuleChecks.CreateArchives(f);TmCode(f);
            string shop=Path.Combine(f.Game.RomFS,"Shop.cro"),code=Path.Combine(f.Game.ExeFS,"code.bin");File.WriteAllBytes(shop,UsumShop67());
            string shopHash=Sha(shop),codeHash=Sha(code);
            var view=new MachineTutorView(f.Session);
            var service=(GameMachineTutorService)typeof(MachineTutorView).GetField("_service",Private)!.GetValue(view)!;
            check(service.CanEditTutorMoves&&service.Tutors.Count==67,"StablePolish_Tutors_Synthetic67SlotTable");
            var actions=Named<Border>(view,"TutorProjectActionsCard");var recipe=Named<Border>(view,"TutorRecipeOptionsCard");
            var randomize=Named<Button>(view,"TutorRandomizeNow");var free=Named<Button>(view,"TutorFreeNow");var preview=Named<Button>(view,"TutorPreview");var apply=Named<Button>(view,"TutorApply");
            var recipeRandom=Named<CheckBox>(view,"RecipeRandomTutors");var recipeFree=Named<CheckBox>(view,"RecipeFreeTutors");
            check(new FrameworkElement[]{randomize,free,preview,apply}.All(c=>Inside(actions,c)&&!Inside(recipe,c))&&Inside(recipe,recipeRandom)&&Inside(recipe,recipeFree)&&!Inside(actions,recipeRandom)&&!Inside(actions,recipeFree),
                "StablePolish_Tutors_ProjectActionsAndRecipeOptionsAreSeparateGroups");
            check(recipe.Visibility==Visibility.Visible&&(string)randomize.Content=="Randomizar movimientos de tutores"&&(string)free.Content=="Poner todos los precios en 0 BP"&&
                  (string)recipeRandom.Content=="Randomizar tutores al aplicar una receta"&&(string)recipeFree.Content=="Hacer gratuitos los tutores al aplicar una receta",
                "StablePolish_Tutors_LabelsSayWhatEachControlDoes");
            int[] moves=service.Tutors.Select(t=>t.MoveId).ToArray(),prices=service.Tutors.Select(t=>t.Price).ToArray();
            // Recipe options: only stored for Global Template / Batch, nothing happens now.
            recipeRandom.IsChecked=true;recipeFree.IsChecked=true;UiPump.Idle();
            check(!view.HasPendingChanges&&!preview.IsEnabled&&!apply.IsEnabled&&service.Tutors.Select(t=>t.MoveId).SequenceEqual(moves)&&service.Tutors.Select(t=>t.Price).SequenceEqual(prices)
                  &&Sha(shop)==shopHash&&Sha(code)==codeHash&&f.Session.History.Read().Entries.Count==0,"StablePolish_Tutors_RecipeOptionsDoNothingImmediately");
            check(view.RememberRecipe() is {RandomTutors:true,FreeTutors:true},"StablePolish_Tutors_RecipeOptionsAreRemembered");
            var document=GlobalTemplateDocument.Empty("USUM",7);NativeGen7MachineBridge.Capture(document,view.RememberRecipe());
            check(NativeGen7MachineBridge.Read(document) is {RandomTutors:true,FreeTutors:true}&&Sha(shop)==shopHash,"StablePolish_Tutors_RecipeOptionsReachTheRecipeWithoutWriting");
            recipeRandom.IsChecked=false;recipeFree.IsChecked=false;
            // «Poner todos los precios en 0 BP»: prices only.
            Click(free);UiPump.Idle();
            check(service.Tutors.All(t=>t.Price==0)&&service.Tutors.Select(t=>t.MoveId).SequenceEqual(moves)&&view.HasPendingChanges&&preview.IsEnabled&&!apply.IsEnabled,
                "StablePolish_Tutors_FreeChangesPricesOnlyAndEnablesPreview");
            check(Sha(shop)==shopHash&&Sha(code)==codeHash,"StablePolish_Tutors_FreeWritesNothingBeforeApply");
            Click(preview);UiPump.Idle();
            var proposal=(TutorEditPreview?)typeof(MachineTutorView).GetField("_tutorPreview",Private)!.GetValue(view);
            check(proposal is {CodeSlotsChanged:0}&&proposal.Changes.Count==prices.Count(p=>p!=0)&&proposal.Changes.All(c=>c.Field=="Precio BP")&&apply.IsEnabled&&Sha(shop)==shopHash,
                "StablePolish_Tutors_PreviewShowsTheRealPriceDifferencesAndEnablesApply");
            Click(apply);UiPump.Idle();
            var reread=new GameMachineTutorService(f.Session);
            check(reread.Tutors.All(t=>t.Price==0)&&reread.Tutors.Select(t=>t.MoveId).SequenceEqual(moves)&&Sha(code)==codeHash&&f.Session.History.Read().Entries.Count==1&&!view.HasPendingChanges&&!apply.IsEnabled,
                "StablePolish_Tutors_ApplySavesPricesKeepsMovesAndCodeAndRecordsHistory");
            Click(free);UiPump.Idle();
            check(!apply.IsEnabled&&f.Session.History.Read().Entries.Count==1,"StablePolish_Tutors_RepeatingFreeIsHarmless");
            // «Randomizar movimientos de tutores»: moves only, in a fresh editor.
            var second=new MachineTutorView(f.Session);var secondService=(GameMachineTutorService)typeof(MachineTutorView).GetField("_service",Private)!.GetValue(second)!;
            int[] savedPrices=secondService.Tutors.Select(t=>t.Price).ToArray();string savedShop=Sha(shop);
            Click(Named<Button>(second,"TutorRandomizeNow"));UiPump.Idle();
            check(!secondService.Tutors.Select(t=>t.MoveId).SequenceEqual(moves)&&secondService.Tutors.Select(t=>t.Price).SequenceEqual(savedPrices)&&second.HasPendingChanges&&Sha(shop)==savedShop,
                "StablePolish_Tutors_RandomizeChangesMovesOnlyWithoutWriting");
        }
        using(var sm=new Fixture(GameVersion.SM))
        {
            ModuleChecks.CreateArchives(sm);TmCode(sm);
            var view=new MachineTutorView(sm.Session);
            check(Named<Border>(view,"TutorRecipeOptionsCard").Visibility==Visibility.Collapsed&&!Named<Button>(view,"TutorRandomizeNow").IsEnabled,"StablePolish_Tutors_SunMoonHasNoUsumRecipeOptions");
        }
    }

    private static void WorkMode(Action<bool,string> check)
    {
        string state=Path.Combine(Path.GetTempPath(),"dr-workmode-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(state);
        string? previous=Environment.GetEnvironmentVariable("PROGRESSIVE_STATE_DIR");Environment.SetEnvironmentVariable("PROGRESSIVE_STATE_DIR",state);
        try
        {
            using var f=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(f);
            bool HomeShown(MainWindow w)=>((ContentControl)w.FindName("HomeHost")).Visibility==Visibility.Visible;
            bool Simplified(MainWindow w)=>(bool)typeof(MainWindow).GetField("_simplified",Private)!.GetValue(w)!;
            check(WorkModePreference.Load() is null,"StablePolish_Mode_FirstUseHasNoPreference");
            var first=new MainWindow();first.LoadProject(f.Session);
            check(HomeShown(first),"StablePolish_Mode_FirstUseShowsTheChoice");
            first.ChooseExperience(false);
            check(!HomeShown(first)&&WorkModePreference.Load()==false&&File.Exists(Path.Combine(state,"work-mode.json")),"StablePolish_Mode_CompleteChoiceIsStoredOutsideTheProject");
            check(!Directory.EnumerateFiles(f.Game.RomFS,"work-mode.json",SearchOption.AllDirectories).Any(),"StablePolish_Mode_NothingStoredInTheRom");
            first.LoadProject(f.Session);
            check(!HomeShown(first)&&!Simplified(first),"StablePolish_Mode_OpeningAnotherProjectGoesStraightToCompleteMode");
            first.Close();
            var restarted=new MainWindow();
            check(!Simplified(restarted)&&((Button)restarted.FindName("ModeButton")).Content as string=="Modo completo · Cambiar","StablePolish_Mode_RestartKeepsCompleteMode");
            restarted.LoadProject(f.Session);
            check(!HomeShown(restarted),"StablePolish_Mode_RestartOpensProjectWithoutAsking");
            // The choice can be changed later from Inicio («Modo completo · Cambiar» opens it).
            typeof(MainWindow).GetMethod("Home_Click",Private)!.Invoke(restarted,[restarted.FindName("ModeButton"),new RoutedEventArgs()]);
            check(HomeShown(restarted),"StablePolish_Mode_ModeButtonReturnsToTheChoice");
            restarted.ChooseExperience(true);restarted.Close();
            var simple=new MainWindow();simple.LoadProject(f.Session);
            check(!HomeShown(simple)&&Simplified(simple)&&((Button)simple.FindName("ItemsButton")).Visibility==Visibility.Collapsed,"StablePolish_Mode_SimplifiedChoiceIsRememberedToo");
            // The guided tour may switch to full mode temporarily; that is not an explicit choice.
            simple.NavigateTo("GlobalTemplate",fullModeForAdvanced:true);UiPump.Idle();
            check(!Simplified(simple)&&WorkModePreference.Load()==true,"StablePolish_Mode_TourSwitchIsNotRemembered");
            simple.Close();
            File.WriteAllText(Path.Combine(state,"work-mode.json"),"{\"Version\":9,\"Mode\":\"x\"}");
            var invalid=new MainWindow();invalid.LoadProject(f.Session);
            check(WorkModePreference.Load() is null&&HomeShown(invalid),"StablePolish_Mode_InvalidPreferenceShowsTheChoice");
            invalid.Close();
        }
        finally{Environment.SetEnvironmentVariable("PROGRESSIVE_STATE_DIR",previous);try{Directory.Delete(state,true);}catch(IOException){}}
    }

    private static void Start(Action<bool,string> check)
    {
        foreach(var project in new bool[]{false,true})
        {
            using var f=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(f);
            var start=new StartView(project?f.Session:null,false,_=>{},()=>{},()=>{},()=>{});
            start.Measure(new Size(900,700));start.Arrange(new Rect(0,0,900,700));start.UpdateLayout();
            var labels=Descendants<Button>(start).Select(b=>b.Content as string).Where(t=>t is not null).ToArray();
            check(labels.Count(l=>l!.StartsWith("Abrir",StringComparison.Ordinal)&&l.Contains("proyecto",StringComparison.Ordinal))>=1&&labels.Where(l=>l!.StartsWith("Abrir",StringComparison.Ordinal)).All(l=>l!.Contains("proyecto",StringComparison.Ordinal))&&!labels.Any(l=>l!.Contains("Switch")||l.StartsWith("Nintendo DS",StringComparison.Ordinal)),
                $"StablePolish_Start_SingleUniversalOpen_{(project?"WithProject":"Empty")}");
        }
        // Inicio as the user sees it, inside the main window (empty and with a project): every visible text reaches WCAG AA against what is behind it.
        foreach(var project in new bool[]{false,true})
        {
            using var f=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(f);
            var main=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false,Width=1280,Height=800};
            main.Show();if(project)main.LoadProject(f.Session);UiPump.Idle();
            var home=(ContentControl)main.FindName("HomeHost");
            string tag=project?"WithProject":"Empty";
            var poor=LowContrast(home).ToArray();
            Capture(main,"stable-polish-start-"+tag);
            check(home.Visibility==Visibility.Visible&&poor.Length==0,$"StablePolish_Start_TextContrast_{tag} ({string.Join(" | ",poor.Take(4))})");
            // The shell around Inicio (sidebar, header, status bar) too.
            var shell=LowContrast((FrameworkElement)main.Content).Where(p=>!p.StartsWith("«",StringComparison.Ordinal)||true).ToArray();
            check(shell.Length==0,$"StablePolish_Shell_TextContrast_{tag} ({string.Join(" | ",shell.Take(4))})");
            main.Close();
        }
    }

    /// <summary>A path passed by Windows (Abrir con…, dropped on the executable) goes to the universal open; anything else starts normally.</summary>
    private static void StartupArgument(Action<bool,string> check)
    {
        string folder=Path.Combine(Path.GetTempPath(),"dr-startup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string file=Path.Combine(folder,"Juego.nds");File.WriteAllBytes(file,[1,2,3]);
        try
        {
            check(MainWindow.StartupPath(["app.exe",folder])==Path.GetFullPath(folder)&&MainWindow.StartupPath(["app.exe","\""+file+"\""])==Path.GetFullPath(file),"StablePolish_StartupPath_ExistingFileOrFolder");
            check(MainWindow.StartupPath(["app.exe"]) is null&&MainWindow.StartupPath(["app.exe",Path.Combine(folder,"missing.nds")]) is null&&MainWindow.StartupPath(["app.exe",file,folder]) is null&&MainWindow.StartupPath(["app.exe","  "]) is null,
                "StablePolish_StartupPath_OtherwiseStartsNormally");
        }
        finally{Directory.Delete(folder,true);}
    }

    /// <summary>«Abrir proyecto» (universal dialog): its captions are WPF Labels, which take the system black unless the theme styles them.</summary>
    private static void OpenDialogContrast(Action<bool,string> check)
    {
        var main=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false};
        main.Show();
        var dialog=new OpenProjectDialog(null,main.CreateOpenCoordinator()){WindowStartupLocation=WindowStartupLocation.Manual,Left=-20000,Top=-20000,ShowActivated=false};
        try
        {
            dialog.Show();UiPump.Idle();
            var labels=Descendants<Label>(dialog).Where(l=>l.IsVisible).ToArray();
            var poor=LowContrast((FrameworkElement)dialog.Content).ToArray();
            Capture((FrameworkElement)dialog.Content,"stable-polish-open-dialog");
            check(labels.Length>=1&&labels.All(l=>l.Foreground is SolidColorBrush {Color:var c}&&c.R+c.G+c.B>300)&&poor.Length==0,$"StablePolish_OpenDialog_TextContrast ({labels.Length} labels: {string.Join(",",labels.Select(l=>l.Foreground))}; {string.Join(" | ",poor.Take(4))})");
        }
        finally{dialog.Close();main.Close();}
    }

    /// <summary>Visible, enabled texts whose colour is below 4.5:1 (3:1 for large text) against the nearest opaque background behind them.</summary>
    internal static IEnumerable<string> LowContrast(FrameworkElement root)
    {
        foreach(var text in Descendants<TextBlock>(root).Where(t=>t.IsVisible&&t.IsEnabled&&t.ActualWidth>0&&!string.IsNullOrWhiteSpace(t.Text)))
        {
            if(text.Foreground is not SolidColorBrush {Color:var fore})continue;
            Color? back=null;
            for(DependencyObject? p=text;p is not null&&back is null;p=VisualTreeHelper.GetParent(p))
            {
                var brush=p switch{Panel panel=>panel.Background,Border border=>border.Background,Control control=>control.Background,_=>null};
                if(brush is SolidColorBrush {Color.A:>200} solid)back=solid.Color;
            }
            if(back is not Color b)continue;
            double ratio=Contrast(fore,b);bool large=text.FontSize>=24||text.FontSize>=18.66&&text.FontWeight.ToOpenTypeWeight()>=600;
            if(ratio<(large?3:4.5))yield return $"«{text.Text[..Math.Min(40,text.Text.Length)]}» {ratio:0.0}:1 ({fore} sobre {b})";
        }
    }
    private static double Contrast(Color a,Color b)
    {
        static double L(Color c){static double F(byte v){double s=v/255.0;return s<=0.03928?s/12.92:Math.Pow((s+0.055)/1.055,2.4);}return 0.2126*F(c.R)+0.7152*F(c.G)+0.0722*F(c.B);}
        double la=L(a),lb=L(b);return(Math.Max(la,lb)+0.05)/(Math.Min(la,lb)+0.05);
    }
    private static void Capture(FrameworkElement element,string name)
    {
        if(Environment.GetEnvironmentVariable("PROGRESSIVE_UI_CAPTURE_DIR") is not { Length: >0 } folder)return;
        Directory.CreateDirectory(folder);UiPump.Idle();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(folder,name+".png"));encoder.Save(stream);
    }

    private static void Undo(Action<bool,string> check)
    {
        // As in the application: continuations of the async reload come back to the UI thread.
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        using var f=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(f);GlobalApplicationChecks.PrepareOpenable(f);
        var main=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false};
        main.Show();main.LoadProject(f.Session);main.ChooseExperience(false);
        try
        {
            main.NavigateTo("History");Wait(()=>((ContentControl)main.FindName("SectionHost")).Content is HistoryView);
            var before=main.Session;
            var task=(Task)typeof(MainWindow).GetMethod("ReloadAfterUndoAsync",Private)!.Invoke(main,null)!;
            Wait(()=>task.IsCompleted);
            var host=(ContentControl)main.FindName("SectionHost");var status=(TextBlock)main.FindName("StatusText");
            Wait(()=>host.Content is HistoryView);
            check(task.IsCompletedSuccessfully&&!ReferenceEquals(before,main.Session)&&host.Content is HistoryView&&((ContentControl)main.FindName("HomeHost")).Visibility==Visibility.Collapsed,
                $"StablePolish_Undo_ReloadsTheProjectAndStaysInHistory ({task.Exception?.GetBaseException().Message}{host.Content?.GetType().Name})");
            // After the History has finished verifying the files (its own progress message must not hide the result).
            var progress=(FrameworkElement)main.FindName("OperationProgress");
            Wait(()=>progress.Visibility!=Visibility.Visible&&!UiProgress.IsInteractionBlocked);UiPump.Idle();
            check(status.Text.Contains("deshecho",StringComparison.Ordinal),$"StablePolish_Undo_ShowsTheResult ({status.Text})");
        }
        finally{main.Close();}
    }

    private static void Wait(Func<bool> done)
    {
        var limit=DateTime.UtcNow.AddSeconds(30);
        while(!done()&&DateTime.UtcNow<limit)System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);
    }

}
