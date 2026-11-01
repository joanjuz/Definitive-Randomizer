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
/// Visual audit of the real app: every section is shown in a real (off-screen) window with the dispatcher running,
/// at two sizes, with table cells in edit mode. Captures go to a folder for review, and the layout is checked for
/// buttons that overlap each other, buttons whose text is cut, and controls that go past the right edge.
/// </summary>
internal static class VisualAudit
{
    private static readonly string[] Sections=["PokemonNavButton","TrainersButton","EncountersButton","StaticEncountersButton","MovesButton","LevelUpButton",
        "MachineTutorButton","ItemsButton","FieldItemsButton","EvolutionsButton","GameTextButton","StoryTextButton","MechanicsButton","PokemonToolsButton",
        "RomToolsButton","AutomationButton","ConfigurationButton","TemplateStudioButton","GlobalTemplateButton","BatchRomButton","HistoryButton"];

    // Tables opened from a button inside a section: (section, button text).
    private static readonly (string Section,string Button)[] SubViews=[("MechanicsButton","Abrir Player Level Caps"),("StaticEncountersButton","Totem Level Caps"),
        ("StaticEncountersButton","Totem BST"),("TrainersButton","Tabla de Level Caps"),("TrainersButton","Tabla de Trainer Move Rules"),("ItemsButton","Tabla de precios · Fix Economy")];

    internal static void Run(string output,Action<bool,string> check)
    {
        Directory.CreateDirectory(output);
        StudioDialog.AutomatedChecks=true;
        var app=new ThreeDS.Progressive.App();app.InitializeComponent();
        using var fixture=new Fixture(GameVersion.USUM);
        ModuleChecks.CreateArchives(fixture);GlobalApplicationChecks.PrepareOpenable(fixture);
        // A code.bin with the TM table signature, so the MT editor loads real assignments.
        byte[] code=new byte[0x402000];byte[] signature=[0x03,0x40,0x03,0x41,0x03,0x42,0x03,0x43,0x03];
        signature.CopyTo(code,0x400000);int tmOffset=0x400000+signature.Length+0x22;
        for(int i=0;i<100;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(code,tmOffset+i*2);
        File.WriteAllBytes(Path.Combine(fixture.Game.ExeFS,"code.bin"),code);

        var main=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false};
        main.Show();main.LoadProject(fixture.Session);main.ChooseExperience(false);
        var host=(ContentControl)main.FindName("SectionHost");
        var issues=new List<string>();
        foreach(var (width,height) in new[]{(1280,800),(1600,900)})
        {
            main.Width=width;main.Height=height;Pump(main);
            foreach(string name in Sections)
            {
                if(main.FindName(name) is not Button nav||!nav.IsVisible||!nav.IsEnabled)continue;
                try{nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}catch(Exception ex){issues.Add($"{name}: no abre ({ex.Message})");continue;}
                Pump(main);
                string label=name.Replace("Button","");
                Capture((FrameworkElement)main.Content,Path.Combine(output,$"{width}x{height}-{label}.png"));
                if(host.Content is FrameworkElement view)
                {
                    foreach(var issue in Inspect(view))issues.Add($"{width}x{height} {label}: {issue}");
                    EditFirstCells(main,view,Path.Combine(output,$"{width}x{height}-{label}"),issues,$"{width}x{height} {label}");
                    // Other tabs of the section (MT / tutors, trainer option tabs...).
                    foreach(var tabs in Visual(view).OfType<TabControl>().Where(t=>t.IsVisible).ToArray())
                        for(int tab=1;tab<tabs.Items.Count;tab++)
                        {
                            tabs.SelectedIndex=tab;Pump(main);
                            string tabLabel=$"{label}-tab{tab}";
                            Capture((FrameworkElement)main.Content,Path.Combine(output,$"{width}x{height}-{tabLabel}.png"));
                            foreach(var issue in Inspect(view))issues.Add($"{width}x{height} {tabLabel}: {issue}");
                            EditFirstCells(main,view,Path.Combine(output,$"{width}x{height}-{tabLabel}"),issues,$"{width}x{height} {tabLabel}");
                        }
                }
            }
            foreach(var (section,buttonText) in SubViews)
            {
                if(main.FindName(section) is not Button nav||!nav.IsVisible||!nav.IsEnabled)continue;
                nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(main);
                if(host.Content is not FrameworkElement owner)continue;
                var opener=Visual(owner).OfType<Button>().FirstOrDefault(b=>b.IsVisible&&Equals(b.Content,buttonText));
                string label=(section.Replace("Button","")+"-"+buttonText).Replace(' ','_').Replace('·','-');
                if(opener is null){issues.Add($"{width}x{height}: no se encontró el botón «{buttonText}»");continue;}
                try{opener.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}catch(Exception ex){issues.Add($"{width}x{height} {label}: no abre ({ex.Message})");continue;}
                Pump(main);
                // Tables opened inside a record editor cover it (TrainerRulesHost): inspect only the table, then go back.
                var view=owner.FindName("TrainerRulesHost") is ContentControl{IsVisible:true,Content:FrameworkElement table}?table:host.Content as FrameworkElement??owner;
                Capture((FrameworkElement)main.Content,Path.Combine(output,$"{width}x{height}-{label}.png"));
                foreach(var issue in Inspect(view))issues.Add($"{width}x{height} {label}: {issue}");
                EditFirstCells(main,view,Path.Combine(output,$"{width}x{height}-{label}"),issues,$"{width}x{height} {label}");
                if(Visual(view).OfType<Button>().FirstOrDefault(b=>b.IsVisible&&b.IsEnabled&&b.Content is "Volver" or "Cerrar" or "Cancelar" or "Volver al randomizador")is Button back)
                {
                    foreach(var grid in Visual(view).OfType<DataGrid>())grid.CancelEdit();
                    back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(main);
                }
            }
        }
        main.Close();
        File.WriteAllLines(Path.Combine(output,"issues.txt"),issues);
        foreach(var issue in issues)Console.WriteLine("VISUAL: "+issue);
        check(issues.Count==0,$"Visual audit: no overlapping buttons, cut button text, controls past the edge or unreadable cell editors ({issues.Count} problem(s); captures in {output})");
    }

    private static void Pump(Window window)
    {
        for(int i=0;i<3;i++)
        {
            window.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
            window.UpdateLayout();
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

    /// <summary>Buttons that overlap, buttons whose text does not fit, and controls past the right edge of the view.</summary>
    internal static IEnumerable<string> Inspect(FrameworkElement view)
    {
        var buttons=Visual(view).OfType<ButtonBase>().Where(b=>b.IsVisible&&b.ActualWidth>0&&b.ActualHeight>0&&b is not DataGridColumnHeader&&b.TemplatedParent is null).ToArray();
        var rects=new List<(ButtonBase Button,Rect Rect)>();
        foreach(var b in buttons)
        {
            Rect r;try{r=Visible(b,view);}catch(InvalidOperationException){continue;}
            if(!r.IsEmpty&&r.Width>1&&r.Height>1)rects.Add((b,r));
        }
        string Name(ButtonBase b)=>b.Content is string s?$"«{s}»":b.Name.Length>0?b.Name:b.GetType().Name;
        for(int i=0;i<rects.Count;i++)
        {
            var (a,ra)=rects[i];
            if(ra.Right>view.ActualWidth+1&&ra.Left<view.ActualWidth)yield return $"{Name(a)} se sale por la derecha ({ra.Right:0} > {view.ActualWidth:0})";
            for(int j=i+1;j<rects.Count;j++)
            {
                var (b,rb)=rects[j];
                var overlap=Rect.Intersect(ra,rb);
                if(!overlap.IsEmpty&&overlap.Width>2&&overlap.Height>2&&!IsAncestor(a,b)&&!IsAncestor(b,a))yield return $"{Name(a)} se solapa con {Name(b)}";
                // Touching buttons on the same row with no gap.
                else if(Math.Abs(ra.Top-rb.Top)<4&&(Math.Abs(ra.Right-rb.Left)<3||Math.Abs(rb.Right-ra.Left)<3))yield return $"{Name(a)} y {Name(b)} están pegados sin separación";
            }
            // Text that does not fit: the text measured without limits is wider than the space inside the button
            // (button width minus padding and border), or than the text block itself.
            foreach(var text in Visual(a).OfType<TextBlock>().Where(t=>t.IsVisible&&t.TextWrapping==TextWrapping.NoWrap&&t.Text.Length>0))
            {
                var probe=new TextBlock{Text=text.Text,FontFamily=text.FontFamily,FontSize=text.FontSize,FontWeight=text.FontWeight,FontStyle=text.FontStyle};
                probe.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                double room=Math.Min(text.ActualWidth,a.ActualWidth-a.Padding.Left-a.Padding.Right-a.BorderThickness.Left-a.BorderThickness.Right);
                if(probe.DesiredSize.Width>room+1.5)yield return $"{Name(a)}: texto cortado ({probe.DesiredSize.Width:0} px en {room:0} px)";
            }
        }
    }

    private static bool IsAncestor(DependencyObject parent,DependencyObject child)
    {
        for(var node=VisualTreeHelper.GetParent(child);node is not null;node=VisualTreeHelper.GetParent(node))if(node==parent)return true;
        return false;
    }

    /// <summary>Puts the first editable text cell and the first combo cell of each visible table into edit mode, captures it and checks the editor is readable.</summary>
    private static void EditFirstCells(Window window,FrameworkElement view,string prefix,List<string> issues,string label)
    {
        int n=0;
        foreach(var grid in Visual(view).OfType<DataGrid>().Where(g=>g.IsVisible&&!g.IsReadOnly&&g.Items.Count>0).Take(2).ToArray())
        {
            foreach(var column in new DataGridColumn?[]{grid.Columns.FirstOrDefault(c=>c is DataGridTextColumn&&!c.IsReadOnly),grid.Columns.FirstOrDefault(c=>c is DataGridComboBoxColumn&&!c.IsReadOnly)})
            {
                if(column is null)continue;
                grid.Focus();grid.SelectedItem=grid.Items[0];
                grid.CurrentCell=new DataGridCellInfo(grid.Items[0],column);
                grid.BeginEdit();Pump(window);
                Capture((FrameworkElement)window.Content,$"{prefix}-edit{n++}-{(column is DataGridComboBoxColumn?"lista":"texto")}.png");
                var cell=Visual(grid).OfType<DataGridCell>().FirstOrDefault(c=>c.IsEditing);
                var editor=cell is null?null:Visual(cell).OfType<Control>().FirstOrDefault(c=>c is TextBox or ComboBox);
                if(editor is null)issues.Add($"{label}: la celda {column.Header} no entró en edición");
                else
                {
                    // The system look (black text, light combo box) comes back whenever the default column style survives.
                    bool systemLook=ReferenceEquals(editor.Style,DataGridTextColumn.DefaultEditingElementStyle)||ReferenceEquals(editor.Style,DataGridComboBoxColumn.DefaultEditingElementStyle);
                    if(systemLook)issues.Add($"{label}: el editor de «{column.Header}» usa el aspecto del sistema (texto negro)");
                    var fore=Brush(editor.Foreground);var back=Brush(editor.Background);
                    if(back.A>200&&Contrast(fore,back)<4.5)issues.Add($"{label}: editor de «{column.Header}» poco legible (texto {fore} sobre {back})");
                }
                grid.CancelEdit();Pump(window);
            }
        }
    }

    private static Color Brush(Brush? brush)=>brush is SolidColorBrush s?s.Color:Colors.Transparent;
    private static double Contrast(Color a,Color b)
    {
        static double L(Color c){double F(byte v){double s=v/255.0;return s<=0.03928?s/12.92:Math.Pow((s+0.055)/1.055,2.4);}return 0.2126*F(c.R)+0.7152*F(c.G)+0.0722*F(c.B);}
        double la=L(a),lb=L(b);return (Math.Max(la,lb)+0.05)/(Math.Min(la,lb)+0.05);
    }
}
