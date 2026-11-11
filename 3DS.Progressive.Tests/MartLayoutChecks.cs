using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using ListBox=System.Windows.Controls.ListBox;
using Panel=System.Windows.Controls.Panel;
using TabControl=System.Windows.Controls.TabControl;

/// <summary>
/// Three-column layout of the 3DS mart editors (Gen6 and Gen7) inside the real main window, off-screen. Checks the
/// arrangement for each logical screen size, that nothing leaves the page horizontally, that every action stays
/// reachable and that choosing a shop from the lists shows the right table. Rendering off-screen is not an
/// interactive check of focus, contrast or DPI.
/// </summary>
internal static class MartLayoutChecks
{
    // Logical (DIP) sizes of 1920×1080, 1366×768 and 1024×640 screens at their scales, and the window minimum.
    private static readonly (string Label,int Width,int Height,double Scale)[] Sizes=
        [("1920x1080-100",1920,1080,1),("1920x1080-125",1536,864,1.25),("1920x1080-150",1280,720,1.5),
         ("1366x768-100",1366,768,1),("1366x768-125",1093,614,1.25),("1024x640-100",1024,640,1),("minimo-900x480",900,480,1)];

    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;

    internal static void Run(Action<bool,string> check,string? captures=null,string? usumSource=null)
    {
        StudioDialog.AutomatedChecks=true;
        var games=new List<GameVersion>{GameVersion.SM,GameVersion.XY,GameVersion.ORAS};
        if(usumSource is not null)games.Insert(0,GameVersion.USUM);
        foreach(var version in games)
        {
            using var fixture=new Fixture(version);
            ModuleChecks.CreateArchives(fixture);GlobalApplicationChecks.PrepareOpenable(fixture);
            if(version==GameVersion.SM)File.WriteAllBytes(Path.Combine(fixture.Game.RomFS,"Shop.cro"),MartSmChecks.SyntheticShop());
            else if(version==GameVersion.USUM)
            {
                string source=new[]{"RomFS/Shop.cro","ExtractedRomFS/Shop.cro"}.Select(p=>Path.Combine(usumSource!,p)).FirstOrDefault(File.Exists)
                    ??throw new FileNotFoundException("La extracción USUM no contiene Shop.cro.",usumSource);
                File.Copy(source,Path.Combine(fixture.Game.RomFS,"Shop.cro"),true);
            }
            // XY with the expanded mart table (add/remove enabled); ORAS with the fixed vanilla one.
            else Mart6Checks.Setup(fixture,patched:version==GameVersion.XY);
            var main=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false};
            main.Show();main.LoadProject(fixture.Session);main.ChooseExperience(false);
            try
            {
                bool interacted=false;
                foreach(var size in Sizes)
                {
                    main.Width=size.Width;main.Height=size.Height;Pump(main);
                    typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,["Marts"]);Pump(main);
                    var host=(ContentControl)main.FindName("SectionHost");
                    var view=(FrameworkElement)host.Content;
                    string tag=$"{version} {size.Label}";
                    check(version.ToString() is "XY" or "ORAS"?view is Gen6MartView:view is MartEditorView,tag+": Tiendas abre el editor de su generación");
                    if(captures is not null)Capture(main,Path.Combine(captures,$"{version}-{size.Label}.png"),size.Scale);
                    Layout(check,tag,view,host);
                    if(!interacted){if(view is MartEditorView gen7)Gen7(check,version.ToString(),gen7,main);else Gen6(check,version.ToString(),(Gen6MartView)view,main);interacted=true;}
                }
            }
            finally{main.Close();}
        }
    }

    private static void Layout(Action<bool,string> check,string tag,FrameworkElement view,FrameworkElement host)
    {
        var workspace=Descendants(view).OfType<MartWorkspace>().Single();
        double width=workspace.ActualWidth;
        string expected=width<MartWorkspace.CompactWidth?"Compact":width<MartWorkspace.NarrowWidth?"Narrow":width<MartWorkspace.WideWidth?"Medium":"Wide";
        check(workspace.Mode==expected,$"{tag}: disposición {workspace.Mode} para {width:0} DIP (esperada {expected})");
        var panes=workspace.Children.OfType<FrameworkElement>().ToArray();
        check(panes.Length==3&&panes.All(p=>p.ActualWidth>=150&&p.ActualHeight>=100),$"{tag}: lista de tiendas, inventario y acciones visibles");
        // Keyboard order follows the children: shops → inventory → actions in every arrangement.
        bool hasList=Descendants(panes[0]).OfType<ListBox>().Any(),hasGrid=Descendants(panes[1]).OfType<DataGrid>().Any(),hasApply=Descendants(panes[2]).OfType<Button>().Any(b=>b.Content as string=="Aplicar y verificar");
        check(hasList&&hasGrid&&hasApply,$"{tag}: orden de tabulación lista → tabla → acciones (lista {hasList}, tabla {hasGrid}, acciones {hasApply})");
        bool compact=workspace.Mode=="Compact";
        check(compact?Grid.GetColumn(panes[2])==0&&Grid.GetRow(panes[2])==1:Grid.GetColumn(panes[2])==4,$"{tag}: acciones a la derecha o, en ventana estrecha, bajo la lista");

        // Nothing leaves the page horizontally; the view fits the host and needs no page scroll.
        // Only a window lower than the editor's minimum height scrolls the page, and only vertically.
        bool pageScroll=view.ActualHeight>host.ActualHeight+1;
        check(view.ActualWidth<=host.ActualWidth+1&&(!pageScroll||view.ActualHeight<=view.MinHeight+1),$"{tag}: la vista cabe a lo ancho{(pageScroll?$"; se desplaza en vertical hasta su alto mínimo {view.MinHeight:0}":"")} ({view.ActualWidth:0}×{view.ActualHeight:0} en {host.ActualWidth:0}×{host.ActualHeight:0})");
        var outside=Descendants(view).OfType<FrameworkElement>()
            .Where(e=>e.IsVisible&&e.ActualWidth>0&&!Ancestors(e).Any(a=>a is DataGrid))
            .FirstOrDefault(e=>{var r=Bounds(e,view);return r.Left<-1||r.Right>view.ActualWidth+1;});
        check(outside is null,$"{tag}: ningún control sale por la derecha{(outside is null?"":$" ({outside.GetType().Name} {AutomationProperties.GetName(outside)})")}");

        // Every action and option is inside its column (wrapped, not cut) and reachable by vertical scrolling.
        var actionCard=panes[2];var cardBounds=Bounds(actionCard,view);
        var controls=Descendants(actionCard).OfType<ButtonBase>().Where(c=>c.IsVisible).ToArray();
        var cut=controls.FirstOrDefault(c=>{var r=Bounds(c,view);return r.Left<cardBounds.Left-0.5||r.Right>cardBounds.Right+0.5||c.DesiredSize.Width-c.Margin.Left-c.Margin.Right>c.ActualWidth+1;});
        check(controls.Length>=4&&cut is null,$"{tag}: {controls.Length} acciones dentro de su columna sin recortes{(cut is null?"":$" ({AutomationProperties.GetName(cut)})")}");
        var orphan=controls.FirstOrDefault(c=>!Within(c,actionCard).Any(a=>a is ScrollViewer)&&!Inside(Bounds(c,view),cardBounds));
        check(orphan is null,$"{tag}: cada acción está a la vista o en una columna desplazable");
        foreach(string label in new[]{"Generar vista previa","Aplicar y verificar"})
        {
            var button=controls.OfType<Button>().Single(b=>b.Content as string==label);
            check(!Within(button,actionCard).Any(a=>a is ScrollViewer)&&Inside(Bounds(button,view),cardBounds)&&Bounds(button,view).Bottom<=view.ActualHeight+1,$"{tag}: «{label}» fijo y visible al pie de las acciones");
        }
        var lists=Descendants(panes[0]).OfType<ListBox>().ToArray();
        check(lists.All(l=>Within(l,panes[0]).Any(a=>a is ScrollViewer s&&s.VerticalScrollBarVisibility==ScrollBarVisibility.Auto&&s.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled)),
            $"{tag}: la lista de tiendas se desplaza solo en vertical");

        // The visible table keeps most of the height and needs no horizontal scrolling.
        var grid=Descendants(panes[1]).OfType<DataGrid>().Single(g=>g.IsVisible);
        var scroller=Descendants(grid).OfType<ScrollViewer>().First();
        // At least the header and three rows on a short editor (1366×768 at 125 %, minimum window); more otherwise.
        double minimum=view.ActualHeight<330?100:view.ActualHeight<450?140:200;
        check(grid.ActualHeight>=minimum&&grid.ActualWidth>=300,$"{tag}: tabla de {grid.ActualWidth:0}×{grid.ActualHeight:0} (mínimo 300×{minimum})");
        check(scroller.ComputedHorizontalScrollBarVisibility!=Visibility.Visible,$"{tag}: la tabla no necesita desplazamiento horizontal");
    }

    private static void Gen7(Action<bool,string> check,string game,MartEditorView view,Window main)
    {
        var regular=Field<ListBox>(view,"_regularPicker");var bp=Field<ListBox>(view,"_bpPicker");var tabs=Field<TabControl>(view,"_tabs");
        var service=Field<GameMartService>(view,"_service");
        check(regular.Items.Count==service.RegularShops.Count&&bp.Items.Count==service.BpShops.Count,$"{game}: las listas muestran {regular.Items.Count} tiendas normales y {bp.Items.Count} BP");
        check(regular.ItemContainerGenerator.ContainerFromIndex(regular.Items.Count-1) is ListBoxItem&&bp.ItemContainerGenerator.ContainerFromIndex(bp.Items.Count-1) is ListBoxItem,$"{game}: todas las tiendas tienen fila en la lista (sin desplegable)");
        check(AutomationProperties.GetName(regular)=="Tiendas normales"&&AutomationProperties.GetName(bp)=="Tiendas BP"&&TextSearch.GetTextPath(regular)=="Name",$"{game}: listas con nombre accesible y búsqueda por teclado");
        bp.SelectedIndex=Math.Min(1,bp.Items.Count-1);Pump(main);
        var bpGrid=Field<DataGrid>(view,"_bpGrid");var regularGrid=Field<DataGrid>(view,"_regularGrid");
        var title=Field<TextBlock>(view,"_shopTitle");
        check(tabs.SelectedIndex==1&&bpGrid.IsVisible&&!regularGrid.IsVisible&&ReferenceEquals(bpGrid.ItemsSource,((MartShop)bp.SelectedItem).Rows)&&title.Text==((MartShop)bp.SelectedItem).Name,
            $"{game}: elegir una tienda BP muestra su tabla y su nombre");
        check(!ReferenceEquals(regular.ItemContainerStyle,bp.ItemContainerStyle),$"{game}: solo la lista de la tabla visible rellena su selección");
        check(!Field<Button>(view,"_add").IsEnabled,$"{game}: añadir ranura sigue desactivado en tiendas BP");
        regular.Focus();Pump(main);
        check(tabs.SelectedIndex==0&&regularGrid.IsVisible,$"{game}: volver a la lista normal muestra su tabla aunque no cambie la tienda");
        regular.SelectedIndex=Math.Min(2,regular.Items.Count-1);Pump(main);
        var shop=(MartShop)regular.SelectedItem;
        check(ReferenceEquals(regularGrid.ItemsSource,shop.Rows)&&title.Text==shop.Name&&Field<TextBlock>(view,"_shopDetail").Text.Contains($"{shop.Rows.Count} ranuras"),$"{game}: cambiar de tienda actualiza tabla, título y número de ranuras");
        if(service.SupportsExpansion)
        {
            int before=shop.Rows.Count;regularGrid.SelectedItem=shop.Rows[0];
            Field<Button>(view,"_add").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(main);
            var item=(ListBoxItem)regular.ItemContainerGenerator.ContainerFromItem(shop);
            check(shop.Rows.Count==before+1&&Descendants(item).OfType<TextBlock>().Any(t=>t.Text==(before+1).ToString())&&Field<TextBlock>(view,"_shopDetail").Text.Contains($"{before+1} ranuras"),$"{game}: añadir ranura actualiza el recuento de la lista y del título");
            Field<Button>(view,"_delete").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(main);
            check(shop.Rows.Count==before,$"{game}: eliminar ranura sigue disponible desde la columna de acciones");
        }
        var flags=Field<Button[]>(view,"_recipeFlags");
        view.RestoreRecipe(new Gen7MartState(Candies:true,ProtectX:true));Pump(main);
        var wrapped=System.Windows.Application.Current.FindResource("WrappedOptionContent");
        check(AutomationProperties.GetItemStatus(flags[2])=="Incluido"&&!ReferenceEquals(flags[2].ContentTemplate,wrapped)&&ReferenceEquals(flags[0].ContentTemplate,wrapped)&&AutomationProperties.GetItemStatus(flags[0])=="Sin incluir",
            $"{game}: las opciones incluidas muestran una marca además del color");
        check(view.RememberRecipe()==new Gen7MartState(Candies:true,ProtectX:true),$"{game}: restaurar la receta conserva el estado de las opciones");
        var recipes=(Panel)flags[0].Parent;
        check(flags.All(f=>ReferenceEquals(f.Parent,recipes))&&recipes.Children.OfType<Button>().Any(b=>b.Content as string=="Restablecer opciones"),$"{game}: opciones de receta y «Restablecer opciones» en el mismo grupo");
        bool usum=game=="USUM";
        string[] labels=["Randomizar tiendas","Randomizar BP","Caramelos raros",usum?"Vitaminas 252 EV":"Objetos de entrenamiento EV","Megapiedras gratis (en BP)","Prohibir Cápsula Habilidad","Generar vista previa","Aplicar y verificar","Restablecer opciones"];
        var visible=Descendants(view).OfType<ButtonBase>().Where(b=>b.IsVisible).Select(b=>b.Content as string).ToHashSet();
        check(labels.All(visible.Contains)&&visible.Contains("Solo tiendas especiales")&&visible.Contains("Conservar objetos X"),$"{game}: todas las acciones conservan su texto");
        check(!usum||new[]{"Solo un objeto en tiendas especiales","21 mentas en stocks generales (0–7)","Lista blanca de compra única…","+ Añadir ranura","− Eliminar ranura"}.All(visible.Contains),$"{game}: opciones exclusivas de USUM visibles");
        check(usum||!new[]{"Solo un objeto en tiendas especiales","21 mentas en stocks generales (0–7)","Lista blanca de compra única…","+ Añadir ranura"}.Any(visible.Contains),$"{game}: Sol/Luna no muestra opciones de USUM ni ranuras ampliables");
        view.RestoreRecipe(new Gen7MartState());
    }

    private static void Gen6(Action<bool,string> check,string game,Gen6MartView view,Window main)
    {
        var picker=Field<ListBox>(view,"_picker");var grid=Field<DataGrid>(view,"_grid");var title=Field<TextBlock>(view,"_shopTitle");
        var shops=Field<List<MartShop>>(view,"_shops");
        check(picker.Items.Count==shops.Count&&picker.ItemContainerGenerator.ContainerFromIndex(shops.Count-1) is ListBoxItem&&AutomationProperties.GetName(picker)=="Tiendas",$"{game}: la lista muestra las {shops.Count} tiendas");
        picker.SelectedIndex=Math.Min(3,shops.Count-1);Pump(main);
        var shop=(MartShop)picker.SelectedItem;
        check(ReferenceEquals(grid.ItemsSource,shop.Rows)&&title.Text==shop.Name,$"{game}: elegir una tienda muestra su tabla y su nombre");
        var checks=Field<Dictionary<string,CheckBox>>(view,"_checks");
        check(checks.Count==6&&checks.Values.All(c=>c.IsVisible&&!string.IsNullOrEmpty(AutomationProperties.GetName(c))),$"{game}: las seis opciones están visibles y con nombre accesible");
        var buttons=Descendants(view).OfType<Button>().Where(b=>b.IsVisible).ToArray();
        var add=buttons.Single(b=>b.Content as string=="+ Añadir objeto");var remove=buttons.Single(b=>b.Content as string=="− Eliminar objeto");
        check(add.IsEnabled==view.Editor.SupportsExpansion&&remove.IsEnabled==view.Editor.SupportsExpansion,$"{game}: añadir/eliminar objeto según la tabla ampliable ({view.Editor.SupportsExpansion})");
        if(view.Editor.SupportsExpansion)
        {
            int before=shop.Rows.Count;grid.SelectedItem=shop.Rows[0];
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(main);
            var item=(ListBoxItem)picker.ItemContainerGenerator.ContainerFromItem(shop);
            check(shop.Rows.Count==before+1&&Descendants(item).OfType<TextBlock>().Any(t=>t.Text==(before+1).ToString())&&Field<TextBlock>(view,"_shopDetail").Text.StartsWith($"{before+1} objetos",StringComparison.Ordinal),$"{game}: añadir objeto actualiza la lista y el título");
            grid.SelectedItem=shop.Rows[^1];remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(main);
            check(shop.Rows.Count==before,$"{game}: eliminar objeto desde la columna de acciones");
        }
        check(view.RememberOptions().Options["UseChecks"]==1,$"{game}: las opciones siguen guardándose para Global/Batch");
    }

    private static bool Inside(Rect r,Rect area)=>r.Left>=area.Left-0.5&&r.Right<=area.Right+0.5&&r.Top>=area.Top-0.5&&r.Bottom<=area.Bottom+0.5;
    private static Rect Bounds(FrameworkElement e,Visual root)=>e.TransformToAncestor(root).TransformBounds(new Rect(0,0,e.ActualWidth,e.ActualHeight));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);yield return child;
            foreach(var nested in Descendants(child))yield return nested;
        }
    }
    /// <summary>Ancestors up to (not including) <paramref name="stop"/>: the page itself may scroll inside the main window.</summary>
    private static IEnumerable<DependencyObject> Within(DependencyObject child,DependencyObject stop)=>Ancestors(child).TakeWhile(a=>!ReferenceEquals(a,stop));
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        for(var current=VisualTreeHelper.GetParent(child);current is not null;current=VisualTreeHelper.GetParent(current))yield return current;
    }
    private static void Pump(Window window)
    {
        for(int i=0;i<4;i++){window.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);window.UpdateLayout();}
    }
    /// <summary>Renders the window content at the screen scale (125 % or 150 %) so the capture has its real pixel density.</summary>
    private static void Capture(Window window,string path,double scale)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*scale),(int)Math.Ceiling(content.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(path);encoder.Save(stream);
    }
}
