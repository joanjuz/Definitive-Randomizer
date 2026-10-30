using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Data;
using DataGridRow=System.Windows.Controls.DataGridRow;
using DataGridCell=System.Windows.Controls.DataGridCell;
using DataGridColumnHeader=System.Windows.Controls.Primitives.DataGridColumnHeader;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using TextBox=System.Windows.Controls.TextBox;
using DataGrid=System.Windows.Controls.DataGrid;
internal static class VisualRedesignChecks
{
    private sealed class BindingErrors:System.Diagnostics.TraceListener
    {
        internal readonly List<string> Errors=new();
        public override void Write(string? message){if(message?.Contains("Error:")==true)Errors.Add(message);}
        public override void WriteLine(string? message)=>Write(message);
    }
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Private)!.GetValue(o)!;
    private static object? Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Private)!.Invoke(o,args);
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind);
    private static void Layout(FrameworkElement content,double w,double h){content.Measure(new Size(w,h));content.Arrange(new Rect(0,0,w,h));content.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);content.UpdateLayout();Pump();}
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var descendant in Descendants<T>(child))yield return descendant;}}
    private static void Capture(FrameworkElement content,int width,int height,double scale,string name)
    {
        Layout(content,width/scale,height/scale);var image=new RenderTargetBitmap(width,height,96*scale,96*scale,PixelFormats.Pbgra32);image.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));string folder=Path.Combine(AppContext.BaseDirectory,"visual-redesign");Directory.CreateDirectory(folder);using var output=File.Create(Path.Combine(folder,name+".png"));png.Save(output);
    }
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    // A real WPF presentation source without WS_VISIBLE: never activates or shows a window.
    private sealed class InvisiblePresentation:IDisposable
    {
        private readonly HwndSource _source;
        private readonly FrameworkElement _content;
        private readonly double _width,_height;
        internal IntPtr Handle=>_source.Handle;
        internal InvisiblePresentation(FrameworkElement content,int width,int height)
        {
            _content=content;_width=content.Width;_height=content.Height;
            var parameters=new HwndSourceParameters("DefinitiveRandomizer offscreen checks")
            {WindowStyle=unchecked((int)0x80000000),Width=width,Height=height,PositionX=-32000,PositionY=-32000};
            _source=new HwndSource(parameters){RootVisual=content};
            Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
            Layout(content,width,height);
        }
        public void Dispose()
        {
            _source.RootVisual=null;_source.Dispose();
            // HwndSource sizes its root explicitly; do not leak that size into DPI render checks.
            _content.Width=_width;_content.Height=_height;
        }
    }
    private static InvisiblePresentation HiddenSource(FrameworkElement content,int width=1000,int height=720)=>new(content,width,height);
    private static System.Windows.Media.Color ResourceColor(string key)=>( (SolidColorBrush)System.Windows.Application.Current.FindResource(key)).Color;
    private static bool UsesColor(System.Windows.Media.Brush brush,string key)=>brush is SolidColorBrush solid&&solid.Color==ResourceColor(key);

    private static void VerifyGridTheme(Action<bool,string> check,ThreeDS.Progressive.App app)
    {
        var rows=new System.Collections.ObjectModel.ObservableCollection<RecordEntry>{new(1,"Primera"),new(2,"Segunda")};
        var valueBinding=new System.Windows.Data.Binding("Name");
        var table=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,ItemsSource=rows,SelectionUnit=DataGridSelectionUnit.Cell};
        var column=new DataGridTextColumn{Header="Texto del juego",Binding=valueBinding,Width=300};table.Columns.Add(column);
        DarkGridTheme.Apply(table);
        var host=new System.Windows.Controls.Grid();host.Children.Add(table);var scope=new Window{Content=host};
        using var source=HiddenSource(host,640,260);
        var header=Descendants<DataGridColumnHeader>(table).Single(h=>h.Column==column);
        var firstRow=(DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
        var secondRow=(DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(1);
        var cell=Descendants<DataGridCell>(firstRow).Single(c=>c.Column==column);
        var otherCell=Descendants<DataGridCell>(secondRow).Single(c=>c.Column==column);
        check(!IsWindowVisible(source.Handle)&&table.IsLoaded,"GridTheme_HiddenSource_LoadedWithoutVisibleWindow");
        check(UsesColor(header.Background,"Surface")&&UsesColor(cell.Foreground,"TextMain"),"GridTheme_Normal_HeaderAndCell");
        check(UsesColor(firstRow.Background,"Panel")&&UsesColor(secondRow.Background,"Surface"),"GridTheme_Normal_AlternatingRows");
        table.SelectedCells.Add(new DataGridCellInfo(rows[0],column));Pump();
        check(cell.IsSelected&&UsesColor(cell.Background,"Selected")&&UsesColor(cell.Foreground,"SelectionText"),"GridTheme_Normal_Selection");
        try
        {
            AccessiblePalette.Update(app,true);Layout(host,640,260);
            check(table.SelectedCells.Count==1&&ReferenceEquals(table.SelectedCells[0].Item,rows[0]),"GridTheme_HighContrast_SelectionModelRetained");
            cell=Descendants<DataGridCell>((DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0)).Single(c=>c.Column==column);
            otherCell=Descendants<DataGridCell>((DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(1)).Single(c=>c.Column==column);
            check(UsesColor(header.Background,"Surface")&&UsesColor(header.Foreground,"TextMain"),"GridTheme_HighContrast_ExistingHeader");
            check(UsesColor(firstRow.Background,"Panel")&&UsesColor(secondRow.Background,"Surface"),"GridTheme_HighContrast_ExistingRows");
            check(UsesColor(otherCell.Foreground,"TextMain")&&UsesColor(cell.Background,"Selected")&&UsesColor(cell.Foreground,"SelectionText"),"GridTheme_HighContrast_ExistingCellsAndSelection");
            table.IsEnabled=false;Pump();
            check(UsesColor(cell.Foreground,"DisabledText")&&UsesColor(header.Foreground,"DisabledText"),"GridTheme_HighContrast_Disabled");
        }
        finally{table.IsEnabled=true;AccessiblePalette.Update(app,false);Layout(host,640,260); cell=Descendants<DataGridCell>((DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0)).Single(c=>c.Column==column); otherCell=Descendants<DataGridCell>((DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(1)).Single(c=>c.Column==column);}
        check(UsesColor(header.Background,"Surface")&&UsesColor(otherCell.Foreground,"TextMain")&&UsesColor(cell.Background,"Selected"),"GridTheme_RestoreDark_ExistingControls");
        check(ReferenceEquals(table.ItemsSource,rows)&&ReferenceEquals(column.Binding,valueBinding)&&rows[0].Name=="Primera"&&cell.IsSelected,"GridTheme_ResourceSwitch_PreservesDataBindingAndSelection");
        table.SelectionUnit=DataGridSelectionUnit.FullRow;table.SelectedItem=rows[1];Layout(host,640,260);
        var selectedRow=(DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(1);
        check(selectedRow.IsSelected&&Descendants<DataGridCell>(selectedRow).Any()&&Descendants<DataGridCell>(selectedRow).All(c=>c.IsSelected&&UsesColor(c.Background,"Selected")&&UsesColor(c.Foreground,"SelectionText")),"GridTheme_FullRow_NormalSelection");
        try{AccessiblePalette.Update(app,true);Layout(host,640,260);selectedRow=(DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(1);check(selectedRow.IsSelected&&Descendants<DataGridCell>(selectedRow).Any()&&Descendants<DataGridCell>(selectedRow).All(c=>c.IsSelected&&UsesColor(c.Background,"Selected")&&UsesColor(c.Foreground,"SelectionText")),"GridTheme_FullRow_HighContrastSelection");}
        finally{AccessiblePalette.Update(app,false);}
        var foregroundSource=new TextBlock{Foreground=System.Windows.Media.Brushes.Coral};
        var foregroundBinding=new System.Windows.Data.Binding("Foreground"){Source=foregroundSource};
        var boundTable=new DataGrid();boundTable.SetBinding(System.Windows.Controls.Control.ForegroundProperty,foregroundBinding);DarkGridTheme.Apply(boundTable);
        check(ReferenceEquals(BindingOperations.GetBinding(boundTable,System.Windows.Controls.Control.ForegroundProperty),foregroundBinding),"GridTheme_Apply_PreservesExistingForegroundBinding");scope.Close();
    }
    private static void VerifyLoadedLifecycle(Action<bool,string> check)
    {
        var window=new MainWindow();var content=(FrameworkElement)window.Content;int loaded=0,unloaded=0;
        content.Loaded+=(_,_)=>loaded++;content.Unloaded+=(_,_)=>unloaded++;
        var inspector=(Expander)window.FindName("AdaptiveInspector");var grid=(System.Windows.Controls.Grid)inspector.Parent;
        using(var source=HiddenSource(content,1100,720))
        {
            check(!IsWindowVisible(source.Handle)&&content.IsLoaded&&grid.IsLoaded,"Lifecycle_Main_LoadedThroughInvisiblePresentationSource");
            check(grid.RowDefinitions.Count==2&&Descendants<System.Windows.Controls.Grid>(content).Single(AdaptiveLayout.GetHeader).RowDefinitions.Count==2,"Lifecycle_Loaded_ConfiguresInspectorAndHeader");
        }
        Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
        check(loaded==1&&unloaded==1&&!content.IsLoaded,"Lifecycle_Main_DetachmentRaisesUnloaded");window.Close();
    }
    private static void VerifyNavigationAndFocus(Action<bool,string> check,Fixture fixture)
    {
        var main=new MainWindow();var root=(FrameworkElement)main.Content;
        string personal=ModuleChecks.PathFor(fixture,"personal");byte[] before=File.ReadAllBytes(personal);
        main.LoadProject(fixture.Session);
        using var source=HiddenSource(root,1100,720);
        var inspector=(Expander)main.FindName("AdaptiveInspector");
        check(((System.Windows.Controls.Grid)inspector.Parent).RowDefinitions.Count==2,"Lifecycle_Reload_DoesNotDuplicateLayoutRows");
        var section=(ContentControl)main.FindName("SectionHost");
        foreach(var route in new[]{("TemplateStudioButton",typeof(TemplateStudioView)),("GameTextButton",typeof(GameTextView)),("StoryTextButton",typeof(GameTextView)),("GlobalTemplateButton",typeof(GlobalTemplateView)),("EvolutionsButton",typeof(RecordEditorView))})
        {
            var button=(Button)main.FindName(route.Item1);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);Layout(root,1100,720);
            check(button.IsEnabled&&section.Visibility==Visibility.Visible&&section.Content?.GetType()==route.Item2,"Navigation_"+route.Item1+"_RoutesThroughOriginalHandler");
            check(section.Content is FrameworkElement {IsLoaded:true}&&!IsWindowVisible(source.Handle),"Navigation_"+route.Item1+"_LoadsWithoutVisibleWindow");
        }
        ((Button)main.FindName("MovesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        var moves=(ContentControl)main.FindName("MovesHost");check(moves.Visibility==Visibility.Visible&&moves.Content is MoveEditorView&&section.Visibility==Visibility.Collapsed,"Navigation_Moves_OriginalHandler");
        ((Button)main.FindName("PokemonNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        check(((FrameworkElement)main.FindName("PokemonEditorPanel")).Visibility==Visibility.Visible&&moves.Visibility==Visibility.Collapsed&&section.Visibility==Visibility.Collapsed,"Navigation_Pokemon_ReturnsToEditor");
        check(File.ReadAllBytes(personal).SequenceEqual(before),"Navigation_ReadOnlyRoutes_PreservePersonalArchive");
        var opener=(TextBox)main.FindName("SearchBox");FocusManager.SetFocusedElement(main,opener);
        check(ReferenceEquals(FocusManager.GetFocusedElement(main),opener),"Focus_LogicalScope_RecordsOpener");
        var dialog=new StudioDialog();int closed=0;dialog.Closed+=(_,_)=>closed++;
        using(var dialogSource=HiddenSource((FrameworkElement)dialog.Content,540,400))
        {
            var confirm=(Button)dialog.FindName("ConfirmButton");FocusManager.SetFocusedElement(dialog,confirm);
            check(ReferenceEquals(FocusManager.GetFocusedElement(dialog),confirm)&&!IsWindowVisible(dialogSource.Handle),"Focus_Dialog_HasSeparateLogicalScope");
            dialog.Close();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
        }
        check(closed==1&&ReferenceEquals(FocusManager.GetFocusedElement(main),opener),"Focus_LogicalScope_OpenerRetainedAfterDialogClosed");
        main.Close();
        Console.WriteLine("NOTE: Hidden-source checks cover Loaded, routed clicks and logical focus; physical keyboard focus and modal activation remain manual.");
    }
    internal static void Run(Action<bool,string> check)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var bindingLog=new BindingErrors();System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(bindingLog);
        var app=new ThreeDS.Progressive.App();app.InitializeComponent();WpfTestHost.SuppressStartup();StudioDialog.AutomatedChecks=true;
        VerifyGridTheme(check,app);
        var main=new MainWindow();var root=(FrameworkElement)main.Content;
        var inspector=(Expander)main.FindName("AdaptiveInspector");var shellGrid=(System.Windows.Controls.Grid)inspector.Parent;
        VerifyLoadedLifecycle(check);
        // Matrix renders remain detached; route synthetic Loaded through the production handler.
        shellGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent,shellGrid));
        var header=Descendants<System.Windows.Controls.Grid>(root).Single(AdaptiveLayout.GetHeader);header.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent,header));
        foreach(var viewport in new[]{(1280,720,1d),(1366,768,1d),(1920,1080,1d),(1366,768,1.25),(1366,768,1.5),(1920,1080,1.5),(1920,1080,2d)})
        {
            Capture(root,viewport.Item1,viewport.Item2,viewport.Item3,$"shell-{viewport.Item1}-{viewport.Item2}-{viewport.Item3:0.##}");
            check(root.ActualWidth<=viewport.Item1/viewport.Item3+.1,"shell fits viewport at scale "+viewport.Item3);
            var open=(Button)main.FindName("OpenProjectButton");var bounds=open.TransformToAncestor(root).TransformBounds(new Rect(open.RenderSize));check(bounds.Right<=root.ActualWidth+.1&&bounds.Top>=0,"project action remains within shell "+viewport.Item3);
        }
        Layout(root,900,600);check(System.Windows.Controls.Grid.GetRow(inspector)==1&&!inspector.IsExpanded&&shellGrid.ColumnDefinitions[3].ActualWidth==0,"compact shell keeps inspector available below editor");
        inspector.IsExpanded=true;Layout(root,900,600);check(inspector.IsExpanded&&inspector.ActualHeight>25,"compact inspector remains expandable");
        Layout(root,1390,800);check(System.Windows.Controls.Grid.GetRow(inspector)==0&&inspector.IsExpanded&&shellGrid.ColumnDefinitions[3].ActualWidth>=185,"wide shell restores inspector beside editor");
        var pinnedSave=(Button)main.FindName("SaveButton");var pinnedBounds=pinnedSave.TransformToAncestor(root).TransformBounds(new Rect(pinnedSave.RenderSize));check(pinnedBounds.Bottom<=root.ActualHeight&&pinnedBounds.Top>=0,"main save action remains in pinned footer");
        check(System.Windows.Automation.AutomationProperties.GetLabeledBy((TextBox)main.FindName("HpBox")) is not null,"static stat field retains an accessible caption");
        var nav=(Button)main.FindName("StoryTextButton");check(nav.Content is string&&nav.ContentTemplate is not null,"navigation preserves command content while using supplied sprite icon presentation");
        check(new NavigationLabelConverter().Convert("▣  Global Template",typeof(string),null!,System.Globalization.CultureInfo.InvariantCulture).ToString()=="Global Template","visible navigation label excludes Unicode glyph without changing content");
        check(new NavigationIconConverter().Convert("≡  Story Text",typeof(System.Windows.Media.Imaging.BitmapSource),null!,System.Globalization.CultureInfo.InvariantCulture) is System.Windows.Media.Imaging.BitmapSource,"navigation uses supplied transparent artwork");
        var grid=new DataGrid();DarkGridTheme.Apply(grid);grid.Columns.Add(new DataGridTextColumn{Header="Valor",Binding=new System.Windows.Data.Binding("Name")});var collection=new System.Collections.ObjectModel.ObservableCollection<RecordEntry>();grid.ItemsSource=collection;var empty=EditorPresentation.EmptyState(grid,"Sin filas","Añadí una fila para empezar.");Layout(empty,600,250);var notice=Descendants<TextBlock>(empty).Single(t=>t.Text=="Sin filas");var noticePanel=(UIElement)VisualTreeHelper.GetParent(notice);check(noticePanel.Visibility==Visibility.Visible,"empty state appears for an empty data collection");collection.Add(new(1,"Texto"));Pump();check(noticePanel.Visibility==Visibility.Collapsed,"empty state follows collection without removing data grid");
        check(grid.IsEnabled&&grid.Items.Count==1&&grid.RowHeight>=44,"presentation preserves table data and reserves space for cell controls");
        var combo=new SearchableTemplateComboBox{Style=(Style)app.FindResource("GridComboBox")};Layout(combo,250,40);check(combo.ActualHeight<=grid.RowHeight-4,"compact selector fits editable row without clipping");
        var apply=new Button{Content="Usar template"};EditorPresentation.StyleAction(apply,EditorPresentation.ActionRole.Primary);check(ReferenceEquals(apply.Style,app.FindResource("PrimaryButton")),"apply action has primary visual hierarchy");var discard=new Button{Content="Eliminar fila"};EditorPresentation.StyleAction(discard,EditorPresentation.ActionRole.Tertiary);check(ReferenceEquals(discard.Style,app.FindResource("TertiaryButton")),"destructive row action has tertiary hierarchy");
        using var f=new Fixture(pk3DS.Core.GameVersion.USUM);ModuleChecks.CreateArchives(f);
        var moves=new MoveEditorView(f.Session);var moveInspector=(Expander)moves.FindName("AdaptiveInspector");using(var lifecycle=HiddenSource(moves,720,620))check(moves.IsLoaded&&System.Windows.Controls.Grid.GetRow(moveInspector)==1&&!IsWindowVisible(lifecycle.Handle),"Lifecycle_Move_LoadedConfiguresCompactInspector");Capture(moves,720,620,1,"move-editor-compact");check(System.Windows.Controls.Grid.GetRow(moveInspector)==1&&!moveInspector.IsExpanded,"move editor adapts without removing inspector controls");
        var record=new RecordEditorView(new GameEvolutionService(f.Session));var recordInspector=(Expander)record.FindName("AdaptiveInspector");using(var lifecycle=HiddenSource(record,720,620))check(record.IsLoaded&&System.Windows.Controls.Grid.GetRow(recordInspector)==1&&!IsWindowVisible(lifecycle.Handle),"Lifecycle_Record_LoadedConfiguresCompactInspector");
        foreach(double scale in new[]{1d,1.25,1.5,2d})Capture(record,1440,1000,scale,$"record-editor-{scale:0.##}");
        Layout(record,720,620);check(System.Windows.Controls.Grid.GetRow(recordInspector)==1,"record editor inspector adapts in narrow modules");check(Descendants<ComboBox>(record).Any(c=>System.Windows.Automation.AutomationProperties.GetLabeledBy(c) is not null),"generated record fields retain explicit label associations");
        var moveSave=(Button)moves.FindName("SaveMoveButton");var moveSaveBounds=moveSave.TransformToAncestor(moves).TransformBounds(new Rect(moveSave.RenderSize));check(moveSaveBounds.Bottom<=moves.ActualHeight&&moveSaveBounds.Top>=0,"move save action remains visible outside fields scroll");
        var recordSave=(Button)record.FindName("SaveButton");var recordSaveBounds=recordSave.TransformToAncestor(record).TransformBounds(new Rect(recordSave.RenderSize));check(recordSaveBounds.Bottom<=record.ActualHeight&&recordSaveBounds.Top>=0,"record save action stays accessible with inspector collapsed");
        var moveFields=Descendants<System.Windows.Controls.Primitives.UniformGrid>(moves).ToArray();Layout(moves,650,620);check(moveFields.All(g=>g.ActualWidth>=460||g.Columns==1),"narrow form groups reflow to one column");
        check(System.Windows.Automation.AutomationProperties.GetLabeledBy((TextBox)moves.FindName("MovePower")) is not null,"move controls expose accessible labels");
        using(var blocked=UiProgress.BlockInteraction())check(!root.IsEnabled,"batch/rebuild interaction lock still disables the full shell");check(root.IsEnabled,"interaction lock restores shell after operation");
        var studio=new TemplateStudioView(f.Session);Capture(studio,1182,620,1,"templates-empty");Call(studio,"AddRow");var rows=Field<System.Collections.ObjectModel.ObservableCollection<TemplateStudioRow>>(studio,"_rows");rows[0]["Move"]="588";rows[0]["InGameDescription"]="Protege al usuario.\nReduce el Ataque al recibir contacto.";Capture(studio,1182,620,1,"templates-populated");check(rows[0]["Move"]=="588"&&rows[0]["BattlePatch"]=="","style changes preserve selected numeric ID and default None patch");
        foreach(bool story in new[]{false,true})
        {
            var text=new GameTextView(f.Session,story);Capture(text,1182,620,1,story?"story-text":"game-text");var files=Field<System.Windows.Controls.ListBox>(text,"_files");check(files.ItemTemplate is not null&&!text.HasPendingChanges,"two-level text bank presentation does not dirty game text "+story);
            var evidence=Field<System.Windows.Controls.ListBox>(text,"_evidence");check(evidence.ItemsSource is not null,"classification evidence remains available "+story);
        }
        var global=new GlobalTemplateView(f.Session);Capture(global,1182,620,1,"global-empty");check(global.LoadedTemplate is null,"global empty state does not create a recipe");
        VerifyNavigationAndFocus(check,f);
        var labelPanel=new StackPanel();var accessibleLabel=new TextBlock{Text="Potencia (0–255)"};var accessibleInput=new TextBox();labelPanel.Children.Add(accessibleLabel);labelPanel.Children.Add(accessibleInput);accessibleInput.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent,accessibleInput));check(ReferenceEquals(System.Windows.Automation.AutomationProperties.GetLabeledBy(accessibleInput),accessibleLabel),"dynamic form associates its preceding caption for assistive technology");
        var disabled=new Button{IsEnabled=false,Style=(Style)app.FindResource(typeof(Button))};Layout(disabled,180,44);disabled.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent,disabled));check(((SolidColorBrush)disabled.Foreground).Color==((SolidColorBrush)app.FindResource("DisabledText")).Color,"palette accessibility hook preserves disabled style triggers actual="+disabled.Foreground+" expected="+app.FindResource("DisabledText")+" local="+disabled.ReadLocalValue(System.Windows.Controls.Control.ForegroundProperty)+" enabled="+disabled.IsEnabled);
        var expander=new Expander{Style=(Style)app.FindResource(typeof(Expander)),Header="Prueba",Content=new TextBlock{Text="Contenido"}};Layout(expander,400,200);var headerToggle=Descendants<System.Windows.Controls.Primitives.ToggleButton>(expander).Single();headerToggle.IsChecked=true;Pump();check(expander.IsExpanded&&headerToggle.Focusable,"expander header is focusable and preserves expansion binding");
        check(System.Windows.Input.KeyboardNavigation.GetTabIndex((Button)main.FindName("OpenProjectButton"))<System.Windows.Input.KeyboardNavigation.GetTabIndex((Button)main.FindName("BackupButton")),"project opening precedes secondary header actions in keyboard order");
        var ruleRows=new[]{new TrainerRuleTableRow()};
        var rules=new TrainerRulesWindow(ruleRows,false);Capture(rules,1100,620,1,"trainer-rules");var ruleGrid=Field<DataGrid>(rules,"_grid");check(ruleGrid.Columns.Count>=7&&ruleGrid.Columns.Single(c=>c.Header?.ToString()=="ID").DisplayIndex==0,"trainer rule consolidation preserves original columns and freezes identifier");
        var message=new StudioDialog();var messageRoot=(FrameworkElement)message.Content;((TextBlock)message.FindName("MessageLabel")).Text=string.Join("\n",Enumerable.Repeat("Mensaje largo para comprobar el desplazamiento.",80));Capture(messageRoot,540,400,1,"dialog-long-message");check(Descendants<ScrollViewer>(messageRoot).Any(v=>v.ScrollableHeight>0),"long dialog message scrolls instead of clipping");var confirm=(Button)message.FindName("ConfirmButton");var confirmBounds=confirm.TransformToAncestor(messageRoot).TransformBounds(new Rect(confirm.RenderSize));check(confirmBounds.Bottom<=messageRoot.ActualHeight+.1,"dialog actions remain outside message scroll area");message.Close();
        var oldBg=((SolidColorBrush)app.FindResource("Bg")).Color;AccessiblePalette.Update(app,true);check(((SolidColorBrush)app.FindResource("Bg")).Color==System.Windows.SystemColors.WindowColor&&((SolidColorBrush)app.FindResource("SelectionText")).Color==System.Windows.SystemColors.HighlightTextColor,"high contrast uses Windows surface and selection text colors");Capture(root,1920,1080,2,"shell-high-contrast-200");AccessiblePalette.Update(app,false);check(((SolidColorBrush)app.FindResource("Bg")).Color==oldBg,"normal palette restores after high contrast without changing Windows settings");
        var renamed=new Button{Content="Etiqueta diferente"};EditorPresentation.StyleAction(renamed,EditorPresentation.ActionRole.Primary);check(ReferenceEquals(renamed.Style,app.FindResource("PrimaryButton")),"explicit action role is independent of terminology");
        var dialog=new TemplateDescriptionWindow("Primera línea\nSegunda línea",f.Session);var button=Field<Button>(dialog,"_previewButton");var previewTask=(Task)Call(dialog,"PreviewAsync")!;while(!previewTask.IsCompleted)Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.Background);previewTask.GetAwaiter().GetResult();Pump();var dialogRoot=(FrameworkElement)dialog.Content;
        foreach(var viewport in new[]{(760,580,1d),(1024,768,1.25),(1366,768,1.5),(1920,1080,2d)})
        {
            Capture(dialogRoot,viewport.Item1,viewport.Item2,viewport.Item3,$"description-{viewport.Item3:0.##}");
            var save=Descendants<Button>(dialogRoot).Single(b=>b.Content?.ToString()=="Guardar descripción");var bounds=save.TransformToAncestor(dialogRoot).TransformBounds(new Rect(save.RenderSize));check(bounds.Bottom<=dialogRoot.ActualHeight+.1&&bounds.Right<=dialogRoot.ActualWidth+.1,"description save remains visible at scale "+viewport.Item3);
        }
        check(dialog.Height<=dialog.MaxHeight&&dialog.Description=="Primera línea\nSegunda línea","preview resizing respects work area and preserves draft");dialog.Close();main.Close();Pump();System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingLog);check(bindingLog.Errors.Count==0,"rendered views have no binding errors: "+string.Join(" | ",bindingLog.Errors.Take(3)));
    }
}
