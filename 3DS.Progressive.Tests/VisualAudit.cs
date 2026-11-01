using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using pk3DS.Core;
using ThreeDS.Progressive;

/// <summary>
/// Visual audit of the real app. For XY, ORAS, SM and USUM fixtures, every section, every tool opened from the hubs,
/// the tables opened from a section and every window that appears are shown in a real (off-screen) window with the
/// dispatcher running, at the logical sizes of common screens and Windows scales. Each state is captured to a folder
/// and checked for: overlapping controls, buttons touching on a row, text that does not fit, controls clipped by their
/// container or past the right edge, unreadable text, light system backgrounds left in the dark theme and table editors
/// with the system look. Warnings (text shortened with an ellipsis) are listed but do not fail the audit.
/// </summary>
internal static class VisualAudit
{
    private sealed record Game(GameVersion Version,string Name,(int Width,int Height)[] Sizes);

    // Logical (DIP) sizes: 1280×800 at 100 %, 1920×1080 at 125 % and 150 %, 1366×768 at 125 %.
    private static readonly Game[] Games=[new(GameVersion.USUM,"USUM",[(1280,800),(1536,864),(1280,720),(1093,614)]),
        new(GameVersion.SM,"SM",[(1536,864),(1093,614)]),new(GameVersion.ORAS,"ORAS",[(1536,864),(1093,614)]),new(GameVersion.XY,"XY",[(1536,864),(1093,614)])];

    private static readonly string[] Sections=["HomeButton","PokemonNavButton","TrainersButton","EncountersButton","StaticEncountersButton","MovesButton","LevelUpButton",
        "MachineTutorButton","ItemsButton","MartsButton","FieldItemsButton","EvolutionsButton","GameTextButton","StoryTextButton","MechanicsButton","PokemonToolsButton",
        "RomToolsButton","AutomationButton","ConfigurationButton","TemplateStudioButton","GlobalTemplateButton","BatchRomButton","HistoryButton"];

    // Hubs whose «Abrir …» buttons open a tool in the same area.
    private static readonly string[] Hubs=["MechanicsButton","PokemonToolsButton","RomToolsButton","AutomationButton","ConfigurationButton"];

    // Tables and windows opened from a button inside a section: (section, button text).
    private static readonly (string Section,string Button)[] SubViews=[("StaticEncountersButton","Totem Level Caps"),
        ("StaticEncountersButton","Totem BST"),("TrainersButton","Tabla de Level Caps"),("TrainersButton","Tabla de Trainer Move Rules"),("ItemsButton","Tabla de precios · Fix Economy"),
        ("MartsButton","Lista blanca de compra única…")];

    private sealed class AuditRun(string output,MainWindow main)
    {
        internal readonly List<string> Issues=[],Warnings=[];
        internal readonly HashSet<Window> Seen=[];
        internal string Context="";
        internal int Views;
        internal readonly string Output=output;internal readonly MainWindow Main=main;
    }

    private static AuditRun? _current;

    /// <param name="usumSource">Optional USUM extraction: only its Shop.cro is copied into the fixture, so Tiendas y BP
    /// (and the single-purchase white list) can be audited. The source is only read.</param>
    internal static void Run(string output,string? usumSource,Action<bool,string> check)
    {
        Directory.CreateDirectory(output);
        StudioDialog.AutomatedChecks=true;
        var app=new ThreeDS.Progressive.App();app.InitializeComponent();
        // Closing a window the audit opened must never end the application.
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        // Section loading is async void: with this context its exceptions reach the dispatcher, where they are recorded
        // as problems of the current view instead of ending the process.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Dispatcher.CurrentDispatcher.UnhandledException+=(_,e)=>
        {
            if(_current is null)return;
            string message=e.Exception.GetBaseException().Message;
            // The app reports missing or unrecognised data with its own message (the fixture lacks many files); any
            // other exception is a problem of the view.
            bool handled=e.Exception is InvalidOperationException&&(message.StartsWith("No se pudo",StringComparison.Ordinal)||message.Contains(": ",StringComparison.Ordinal)&&e.Exception.StackTrace?.Contains("StudioDialog.Error",StringComparison.Ordinal)==true);
            (handled?_current.Warnings:_current.Issues).Add($"{_current.Context}: error al abrir ({message})");e.Handled=true;
        };
        var allIssues=new List<string>();var allWarnings=new List<string>();int views=0;
        foreach(var game in Games)
        {
            using var fixture=new Fixture(game.Version);
            ModuleChecks.CreateArchives(fixture);GlobalApplicationChecks.PrepareOpenable(fixture);
            bool shop=PrepareShop(fixture,game.Version,usumSource);Enrich(fixture);
            if(game.Version is GameVersion.SM or GameVersion.USUM)
            {
                // A code.bin with the TM table signature, so the MT editor loads real assignments.
                byte[] code=new byte[0x402000];byte[] signature=[0x03,0x40,0x03,0x41,0x03,0x42,0x03,0x43,0x03];
                signature.CopyTo(code,0x400000);int tmOffset=0x400000+signature.Length+0x22;
                for(int i=0;i<100;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(code,tmOffset+i*2);
                File.WriteAllBytes(Path.Combine(fixture.Game.ExeFS,"code.bin"),code);
            }
            var main=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false};
            main.Show();main.LoadProject(fixture.Session);main.ChooseExperience(false);
            var run=new AuditRun(output,main);_current=run;
            if(!shop)run.Warnings.Add($"{game.Name}: Tiendas y BP no se audita (la fixture no tiene Shop.cro{(game.Version==GameVersion.USUM?"; pasar una extracción USUM como segundo argumento":"")})");
            // Modal windows (dialogs, ShowDialog) are handled from a timer: it runs inside their own message loop.
            var watcher=new DispatcherTimer(TimeSpan.FromMilliseconds(120),DispatcherPriority.Background,(_,_)=>Windows(run),main.Dispatcher);watcher.Start();
            try
            {
                foreach(var (width,height) in game.Sizes)
                {
                    main.Width=width;main.Height=height;Pump(main);
                    string size=$"{game.Name}-{width}x{height}";
                    foreach(string name in Sections.Where(s=>shop||s!="MartsButton"))Section(run,size,name);
                    foreach(string hub in Hubs)HubTools(run,size,hub);
                    foreach(var (section,button) in SubViews.Where(s=>shop||s.Section!="MartsButton"))SubView(run,size,section,button);
                }
            }
            finally{watcher.Stop();Windows(run);main.Close();_current=null;}
            allIssues.AddRange(run.Issues);allWarnings.AddRange(run.Warnings);views+=run.Views;
        }
        allIssues=allIssues.Distinct().ToList();allWarnings=allWarnings.Distinct().ToList();
        File.WriteAllLines(Path.Combine(output,"issues.txt"),allIssues);
        File.WriteAllLines(Path.Combine(output,"warnings.txt"),allWarnings);
        foreach(var issue in allIssues)Console.WriteLine("VISUAL: "+issue);
        Console.WriteLine($"Visual audit: {views} views, {allIssues.Count} problem(s), {allWarnings.Count} warning(s); captures in {output}");
        check(allIssues.Count==0,$"Visual audit: no overlapping or clipped controls, cut or unreadable text, light system backgrounds or system-look cell editors ({allIssues.Count} problem(s))");
    }

    /// <summary>Shop.cro for the fixture: the synthetic SM layout of MartSmChecks, a copy of a real USUM one, none for Gen6.</summary>
    private static bool PrepareShop(Fixture fixture,GameVersion version,string? usumSource)
    {
        string target=Path.Combine(fixture.Game.RomFS,"Shop.cro");
        if(version==GameVersion.SM){File.WriteAllBytes(target,MartSmChecks.SyntheticShop());return true;}
        if(version!=GameVersion.USUM||usumSource is null)return version is GameVersion.XY or GameVersion.ORAS;
        string? source=new[]{"RomFS/Shop.cro","ExtractedRomFS/Shop.cro"}.Select(p=>Path.Combine(usumSource,p)).FirstOrDefault(File.Exists);
        if(source is null)return false;
        File.Copy(source,target,true);return true;
    }

    /// <summary>
    /// Synthetic egg moves, Mega evolutions, battle facility archives and SMDH icon (same layouts as the module checks),
    /// so the tools that need them open and are audited instead of stopping at a missing-file message.
    /// </summary>
    private static void Enrich(Fixture f)
    {
        int species=f.PersonalRecords.Length;bool gen7=f.Game.Generation==7;
        void Write(string name,byte[][] files){try{f.WriteGarc(ModuleChecks.PathFor(f,name),files,f.Game.GetGARCReference(name));}catch(Exception ex)when(ex is ArgumentException or KeyNotFoundException or InvalidOperationException){}}
        Write("eggmove",Enumerable.Range(0,species).Select(i=>gen7?new pk3DS.Core.Structures.EggMoves7([0,0,0,0]){Moves=i==0?[]:[1,2,3],FormTableIndex=0}.Write():i==0?new byte[]{0,0}:new byte[]{3,0,1,0,2,0,3,0}).ToArray());
        Write("megaevo",Enumerable.Range(0,species).Select(i=>{byte[] b=new byte[16];if(i==1){BitConverter.GetBytes((ushort)1).CopyTo(b,2);BitConverter.GetBytes((ushort)150).CopyTo(b,4);}return b;}).ToArray());
        foreach(string suffix in new[]{"N","S"})
        {
            Write("maisontr"+suffix,[[1,0,2,0,2,0,1,0,0xAA,0xBB],[2,0,0,0]]);
            Write("maisonpk"+suffix,Enumerable.Range(0,3).Select(i=>{byte[] b=new byte[18];b[0]=(byte)(i+1);b[2]=1;b[4]=2;b[6]=3;b[8]=4;b[10]=0xC1;b[11]=3;b[12]=5;b[14]=7;return b;}).ToArray());
        }
        byte[] icon=new byte[0x36C0];"SMDH"u8.CopyTo(icon);icon[6]=71;File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"icon.bin"),icon);
    }

    private static ContentControl Host(MainWindow main)=>(ContentControl)main.FindName("SectionHost");

    private static bool Click(AuditRun run,ButtonBase button,string label)
    {
        try{button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));}
        catch(Exception ex){run.Issues.Add($"{label}: no abre ({ex.Message})");return false;}
        Pump(run.Main);Windows(run);return true;
    }

    private static void Section(AuditRun run,string size,string name)
    {
        if(run.Main.FindName(name) is not Button nav||!nav.IsVisible||!nav.IsEnabled)return;
        string label=$"{size} {name.Replace("Button","")}";run.Context=label;
        if(!Click(run,nav,label))return;
        // The home and the Pokémon editor are not hosted in SectionHost: inspect the whole content then.
        var view=Host(run.Main) is {IsVisible:true,Content:FrameworkElement hosted}?hosted:(FrameworkElement)run.Main.Content;
        Check(run,view,label);
        // Other tabs of the section (MT / tutors, trainer option tabs, template tabs...).
        foreach(var tabs in Visual(view).OfType<TabControl>().Where(t=>t.IsVisible).ToArray())
            for(int tab=1;tab<tabs.Items.Count;tab++)
            {
                tabs.SelectedIndex=tab;Pump(run.Main);
                Check(run,view,$"{label}-tab{tab}");
            }
    }

    private static void HubTools(AuditRun run,string size,string hub)
    {
        if(run.Main.FindName(hub) is not Button nav||!nav.IsVisible||!nav.IsEnabled)return;
        if(!Click(run,nav,$"{size} {hub}"))return;
        if(Host(run.Main).Content is not FrameworkElement hubView)return;
        var labels=Visual(hubView).OfType<Button>().Where(b=>b.IsVisible&&b.IsEnabled&&b.Content is string s&&s.StartsWith("Abrir ",StringComparison.Ordinal)&&!s.EndsWith('…')&&s!="Abrir extracción")
            .Select(b=>(string)b.Content).Distinct().ToArray();
        foreach(string text in labels)
        {
            // The hub is rebuilt after each tool: open it again and find the button by its text.
            Click(run,nav,$"{size} {hub}");
            if(Host(run.Main).Content is not FrameworkElement current)continue;
            var opener=Visual(current).OfType<Button>().FirstOrDefault(b=>b.IsVisible&&b.IsEnabled&&Equals(b.Content,text));
            if(opener is null)continue;
            string label=$"{size} {hub.Replace("Button","")}-{Slug(text)}";run.Context=label;
            if(!Click(run,opener,label))continue;
            if(Host(run.Main).Content is FrameworkElement view&&!ReferenceEquals(view,current))
            {
                Check(run,view,label);
                foreach(var tabs in Visual(view).OfType<TabControl>().Where(t=>t.IsVisible).ToArray())
                    for(int tab=1;tab<tabs.Items.Count;tab++){tabs.SelectedIndex=tab;Pump(run.Main);Check(run,view,$"{label}-tab{tab}");}
            }
        }
    }

    private static void SubView(AuditRun run,string size,string section,string buttonText)
    {
        if(run.Main.FindName(section) is not Button nav||!nav.IsVisible||!nav.IsEnabled)return;
        Click(run,nav,$"{size} {section}");
        if(Host(run.Main).Content is not FrameworkElement owner)return;
        var opener=Visual(owner).OfType<Button>().FirstOrDefault(b=>b.IsVisible&&Equals(b.Content,buttonText));
        if(opener is null)return;   // not available for this game
        string label=$"{size} {section.Replace("Button","")}-{Slug(buttonText)}";run.Context=label;
        if(!opener.IsEnabled){run.Warnings.Add($"{label}: el botón está desactivado en la fixture");return;}
        if(!Click(run,opener,label))return;
        // Tables opened inside a record editor cover it (TrainerRulesHost): inspect only the table, then go back.
        var view=owner.FindName("TrainerRulesHost") is ContentControl{IsVisible:true,Content:FrameworkElement table}?table:Host(run.Main).Content as FrameworkElement??owner;
        if(ReferenceEquals(view,owner))return;   // it opened a window: the watcher already checked it
        Check(run,view,label);
        if(Visual(view).OfType<Button>().FirstOrDefault(b=>b.IsVisible&&b.IsEnabled&&b.Content is "Volver" or "Cerrar" or "Cancelar" or "Volver al randomizador")is Button back)
        {
            foreach(var grid in Visual(view).OfType<DataGrid>())grid.CancelEdit();
            back.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Pump(run.Main);
        }
    }

    /// <summary>Captures and checks every new window (dialogs included), then closes it. Error dialogs are problems.</summary>
    private static void Windows(AuditRun run)
    {
        if(Application.Current is null)return;
        foreach(var window in Application.Current.Windows.OfType<Window>().ToArray())
        {
            if(window is MainWindow||!window.IsLoaded||!run.Seen.Add(window))continue;
            window.UpdateLayout();
            string title=string.IsNullOrWhiteSpace(window.Title)?window.GetType().Name:window.Title;
            string label=$"{run.Context} [ventana {Slug(title)}]";
            Console.WriteLine($"AUDIT window {window.GetType().Name} «{title}» during {run.Context}");
            if(window.Content is FrameworkElement content)
            {
                Capture(window,Path.Combine(run.Output,Slug(label)+".png"));
                var found=Inspect(content,out var warnings);
                foreach(var issue in found)run.Issues.Add($"{label}: {issue}");
                foreach(var warning in warnings)run.Warnings.Add($"{label}: {warning}");
                string text=string.Join(" ",Visual(content).OfType<TextBlock>().Select(t=>t.Text).Where(t=>t.Length>0)).Trim();
                if(window.GetType().Name=="StudioDialog"&&(title.Contains("No se pudo",StringComparison.OrdinalIgnoreCase)||title.Contains("Error",StringComparison.OrdinalIgnoreCase)))
                    run.Issues.Add($"{run.Context}: diálogo de error «{title}»: {(text.Length>160?text[..160]+"…":text)}");
            }
            run.Views++;
            try{window.Close();}catch(InvalidOperationException){}
        }
    }

    private static string Slug(string text)=>new string(text.Select(c=>char.IsLetterOrDigit(c)||c is '-' or '_'?c:'_').ToArray()).Trim('_');

    private static void Check(AuditRun run,FrameworkElement view,string label)
    {
        run.Views++;
        Capture((FrameworkElement)run.Main.Content,Path.Combine(run.Output,Slug(label)+".png"));
        var found=Inspect(view,out var warnings);
        foreach(var issue in found)run.Issues.Add($"{label}: {issue}");
        foreach(var warning in warnings)run.Warnings.Add($"{label}: {warning}");
        EditFirstCells(run,view,label);
    }

    /// <summary>Lets the dispatcher run (sections load asynchronously) until the content stops changing.</summary>
    private static void Pump(Window window)
    {
        int stable=0;object? last=null;int lastCount=-1;
        for(int i=0;i<60&&stable<3;i++)
        {
            window.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            object? content=(window.FindName("SectionHost") as ContentControl)?.Content;int count=content is DependencyObject d?Visual(d).Count():0;
            if(ReferenceEquals(content,last)&&count==lastCount)stable++;else stable=0;
            last=content;lastCount=count;
            if(stable<3)Thread.Sleep(15);
        }
    }

    private static void Capture(FrameworkElement element,string file)
    {
        if(element.ActualWidth<1||element.ActualHeight<1)return;
        var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(file);encoder.Save(stream);
    }

    private static IEnumerable<DependencyObject> Visual(DependencyObject root)
    {
        yield return root;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Visual(VisualTreeHelper.GetChild(root,i)))yield return child;
    }

    private static Rect Bounds(FrameworkElement element,Visual root)=>element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    // The part of the element that is actually shown: clipped by every scroll viewer above it.
    private static Rect Visible(FrameworkElement element,FrameworkElement root)
    {
        var rect=Bounds(element,root);
        for(var node=VisualTreeHelper.GetParent(element);node is not null&&node!=root;node=VisualTreeHelper.GetParent(node))
            if(node is ScrollContentPresenter presenter)rect.Intersect(Bounds(presenter,root));
        return rect;
    }

    private static bool Within<T>(DependencyObject element,FrameworkElement root) where T:DependencyObject
    {
        for(var node=VisualTreeHelper.GetParent(element);node is not null&&node!=root;node=VisualTreeHelper.GetParent(node))if(node is T)return true;
        return false;
    }

    private static bool Shown(FrameworkElement element,FrameworkElement root)
    {
        if(!element.IsVisible||element.ActualWidth<1||element.ActualHeight<1)return false;
        try{var r=Visible(element,root);return !r.IsEmpty&&r.Width>1&&r.Height>1;}catch(InvalidOperationException){return false;}
    }

    /// <summary>Problems in a view; warnings are listed apart and do not fail the audit.</summary>
    internal static IEnumerable<string> Inspect(FrameworkElement view)=>Inspect(view,out _);

    internal static List<string> Inspect(FrameworkElement view,out List<string> warnings)
    {
        var issues=new List<string>();warnings=[];
        var all=Visual(view).OfType<FrameworkElement>().ToArray();
        // Interactive controls drawn by the view itself (not parts of another control's template, not table cells).
        var controls=all.Where(e=>e is ButtonBase or TextBox or ComboBox or Slider&&e is not DataGridColumnHeader&&e.TemplatedParent is null
            &&!Within<DataGridCell>(e,view)&&Shown(e,view)).ToArray();
        var rects=controls.Select(c=>(Control:c,Rect:Visible(c,view))).ToArray();
        string Name(FrameworkElement e)=>e switch
        {
            ContentControl{Content:string s}=>$"«{s}»",
            TextBlock{Text.Length:>0} block=>$"texto «{Short(block.Text)}»",
            _ when System.Windows.Automation.AutomationProperties.GetName(e) is {Length:>0} n=>$"«{n}»",
            _ when e.Name.Length>0=>e.Name,
            _=>e.GetType().Name
        };
        for(int i=0;i<rects.Length;i++)
        {
            var (a,ra)=rects[i];
            if(ra.Right>view.ActualWidth+1&&ra.Left<view.ActualWidth)issues.Add($"{Name(a)} se sale por la derecha ({ra.Right:0} > {view.ActualWidth:0})");
            for(int j=i+1;j<rects.Length;j++)
            {
                var (b,rb)=rects[j];
                if(IsAncestor(a,b)||IsAncestor(b,a))continue;
                var overlap=Rect.Intersect(ra,rb);
                if(!overlap.IsEmpty&&overlap.Width>2&&overlap.Height>2)issues.Add($"{Name(a)} se solapa con {Name(b)}");
                // Touching buttons on the same row with no gap.
                else if(a is Button&&b is Button&&Math.Abs(ra.Top-rb.Top)<4&&(Math.Abs(ra.Right-rb.Left)<3||Math.Abs(rb.Right-ra.Left)<3))issues.Add($"{Name(a)} y {Name(b)} están pegados sin separación");
            }
        }
        foreach(var element in all)
        {
            if(!element.IsVisible||element.ActualWidth<1||element.ActualHeight<1||Within<DataGridCell>(element,view)||Within<DataGridColumnHeader>(element,view))continue;
            // Clipped by its layout slot: the element was given less room than it needs (a cut button, a field cut at the bottom).
            if((element is Control&&element.TemplatedParent is null||element is TextBlock{TemplatedParent:null})&&LayoutInformation.GetLayoutClip(element) is {} clip&&Shown(element,view))
            {
                // TabPanel and others arrange with the margin included: only a clip smaller than the element minus its margin counts.
                var box=clip.Bounds;var margin=element.Margin;
                if(box.Width<element.ActualWidth-margin.Left-margin.Right-1.5||box.Height<element.ActualHeight-margin.Top-margin.Bottom-1.5)
                    issues.Add($"{Name(element)} recortado por su contenedor ({box.Width:0}×{box.Height:0} de {element.ActualWidth:0}×{element.ActualHeight:0})");
            }
            if(element is TextBlock text&&text.Text.Trim().Length>0&&Shown(text,view))
            {
                var probe=new TextBlock{Text=text.Text,FontFamily=text.FontFamily,FontSize=text.FontSize,FontWeight=text.FontWeight,FontStyle=text.FontStyle,FontStretch=text.FontStretch};
                // Text that does not fit on its line. Inside a button the room is the button minus padding and border.
                if(text.TextWrapping==TextWrapping.NoWrap)
                {
                    probe.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                    double room=text.ActualWidth;
                    if(Owner<ButtonBase>(text,view) is Control button&&button is not ComboBoxItem)
                        room=Math.Min(room,button.ActualWidth-button.Padding.Left-button.Padding.Right-button.BorderThickness.Left-button.BorderThickness.Right);
                    if(probe.DesiredSize.Width>room+1.5)
                    {
                        string message=$"texto «{Short(text.Text)}» no cabe ({probe.DesiredSize.Width:0} px en {room:0} px)";
                        if(text.TextTrimming!=TextTrimming.None&&Owner<ButtonBase>(text,view) is null)warnings.Add(message+" · se acorta con «…»");else issues.Add(message);
                    }
                }
                else
                {
                    // Wrapped text cut at the bottom.
                    probe.TextWrapping=text.TextWrapping;probe.Measure(new Size(text.ActualWidth,double.PositiveInfinity));
                    if(probe.DesiredSize.Height>text.ActualHeight+2)issues.Add($"texto «{Short(text.Text)}» cortado por abajo ({probe.DesiredSize.Height:0} px en {text.ActualHeight:0} px)");
                }
                // Readable text: WCAG AA (4.5:1, or 3:1 for large text). Disabled controls are exempt.
                if(text.IsEnabled&&Opacity(text)>0.05&&text.Foreground is SolidColorBrush fore&&EffectiveBackground(text) is Color back)
                {
                    // The text's own opacity (and its ancestors') fades it towards the background.
                    var faded=fore.Color;faded.A=(byte)(faded.A*Math.Min(1,Opacity(text)));
                    double ratio=Contrast(Blend(faded,back),back);bool large=text.FontSize>=24||text.FontSize>=18.66&&text.FontWeight.ToOpenTypeWeight()>=600;
                    if(ratio<(large?3:4.5)&&!Within<DataGridCell>(text,view))issues.Add($"texto «{Short(text.Text)}» con poco contraste ({ratio:0.0}:1, {fore.Color} sobre {back})");
                }
            }
            // A scroll thumb too short to see or grab (long lists).
            if(element is Thumb thumb&&VisualTreeHelper.GetParent(thumb) is Track track&&Shown(thumb,view)&&Opacity(thumb)>0.05)
            {
                double length=track.Orientation==Orientation.Vertical?thumb.ActualHeight:thumb.ActualWidth;
                if(length<20)issues.Add($"barra de desplazamiento con un pulgar de {length:0} px");
            }
            // A large light background left by a control without the dark theme (system look).
            if(element is Panel or Border&&Background(element) is SolidColorBrush{Color.A:>200} brush&&Luminance(brush.Color)>0.6&&element.ActualWidth*element.ActualHeight>=4000&&Shown(element,view)&&Opacity(element)>0.05)
                issues.Add($"{Name(element)} con fondo claro {brush.Color} ({element.ActualWidth:0}×{element.ActualHeight:0})");
        }
        return issues;
    }

    private static string Short(string text){text=text.Replace('\n',' ').Trim();return text.Length>48?text[..48]+"…":text;}

    private static T? Owner<T>(DependencyObject element,FrameworkElement root) where T:DependencyObject
    {
        for(var node=VisualTreeHelper.GetParent(element);node is not null&&node!=root;node=VisualTreeHelper.GetParent(node))if(node is T t)return t;
        return null;
    }

    // Only panels and borders are sure to paint their background; a control's Background is whatever its template does with it.
    private static Brush? Background(FrameworkElement element)=>element switch{Panel p=>p.Background,Border b=>b.Background,_=>null};

    private static double Opacity(DependencyObject element)
    {
        double opacity=1;
        for(var node=element;node is not null;node=VisualTreeHelper.GetParent(node))if(node is UIElement u)opacity*=u.Opacity;
        return opacity;
    }

    // The first opaque background behind the element.
    private static Color? EffectiveBackground(DependencyObject element)
    {
        for(var node=VisualTreeHelper.GetParent(element);node is not null;node=VisualTreeHelper.GetParent(node))
            if(node is FrameworkElement e&&Background(e) is SolidColorBrush s&&s.Color.A>200&&e.Opacity>0.99)return s.Color;
        return null;
    }

    private static Color Blend(Color fore,Color back)
    {
        double a=fore.A/255.0;
        return Color.FromRgb((byte)(fore.R*a+back.R*(1-a)),(byte)(fore.G*a+back.G*(1-a)),(byte)(fore.B*a+back.B*(1-a)));
    }

    private static bool IsAncestor(DependencyObject parent,DependencyObject child)
    {
        for(var node=VisualTreeHelper.GetParent(child);node is not null;node=VisualTreeHelper.GetParent(node))if(node==parent)return true;
        return false;
    }

    /// <summary>Puts the first editable text cell and the first combo cell of each visible table into edit mode, captures it and checks the editor is readable.</summary>
    private static void EditFirstCells(AuditRun run,FrameworkElement view,string label)
    {
        int n=0;
        foreach(var grid in Visual(view).OfType<DataGrid>().Where(g=>g.IsVisible&&!g.IsReadOnly&&g.Items.Count>0).Take(2).ToArray())
        {
            foreach(var column in new DataGridColumn?[]{grid.Columns.FirstOrDefault(c=>c is DataGridTextColumn&&!c.IsReadOnly),grid.Columns.FirstOrDefault(c=>c is DataGridComboBoxColumn&&!c.IsReadOnly)})
            {
                if(column is null)continue;
                grid.Focus();grid.SelectedItem=grid.Items[0];
                grid.CurrentCell=new DataGridCellInfo(grid.Items[0],column);
                grid.BeginEdit();Pump(run.Main);
                Capture((FrameworkElement)run.Main.Content,Path.Combine(run.Output,$"{Slug(label)}-edit{n++}-{(column is DataGridComboBoxColumn?"lista":"texto")}.png"));
                var cell=Visual(grid).OfType<DataGridCell>().FirstOrDefault(c=>c.IsEditing);
                var editor=cell is null?null:Visual(cell).OfType<Control>().FirstOrDefault(c=>c is TextBox or ComboBox);
                if(editor is null)run.Issues.Add($"{label}: la celda {column.Header} no entró en edición");
                else
                {
                    // The system look (black text, light combo box) comes back whenever the default column style survives.
                    bool systemLook=ReferenceEquals(editor.Style,DataGridTextColumn.DefaultEditingElementStyle)||ReferenceEquals(editor.Style,DataGridComboBoxColumn.DefaultEditingElementStyle);
                    if(systemLook)run.Issues.Add($"{label}: el editor de «{column.Header}» usa el aspecto del sistema (texto negro)");
                    var fore=Brush(editor.Foreground);var back=Brush(editor.Background);
                    if(back.A>200&&Contrast(fore,back)<4.5)run.Issues.Add($"{label}: editor de «{column.Header}» poco legible (texto {fore} sobre {back})");
                }
                grid.CancelEdit();Pump(run.Main);
            }
        }
    }

    private static Color Brush(Brush? brush)=>brush is SolidColorBrush s?s.Color:Colors.Transparent;
    private static double Luminance(Color c){static double F(byte v){double s=v/255.0;return s<=0.03928?s/12.92:Math.Pow((s+0.055)/1.055,2.4);}return 0.2126*F(c.R)+0.7152*F(c.G)+0.0722*F(c.B);}
    private static double Contrast(Color a,Color b)
    {
        double la=Luminance(a),lb=Luminance(b);return (Math.Max(la,lb)+0.05)/(Math.Min(la,lb)+0.05);
    }
}
