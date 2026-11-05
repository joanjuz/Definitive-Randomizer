using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;
using UserControl=System.Windows.Controls.UserControl;
using Button=System.Windows.Controls.Button;
using ListBox=System.Windows.Controls.ListBox;
using ComboBox=System.Windows.Controls.ComboBox;
using Brush=System.Windows.Media.Brush;
using HorizontalAlignment=System.Windows.HorizontalAlignment;
using Orientation=System.Windows.Controls.Orientation;

namespace ThreeDS.Progressive;

/// <summary>
/// One row of the history list: a single saved operation, or a Global Template application with all its actions
/// grouped under the entry that applied it (they share «GlobalApplication»).
/// </summary>
internal sealed record HistoryItem(HistoryEntry Main,IReadOnlyList<HistoryEntry> Actions)
{
    internal bool IsRecipe=>Actions.Count>0;
    internal IEnumerable<HistoryEntry> All=>Actions.Prepend(Main);
}

public sealed class HistoryView : UserControl
{
    internal static IReadOnlyList<HistoryItem> Group(IReadOnlyList<HistoryEntry> newestFirst)
    {
        var applications=newestFirst.Where(e=>e.Parameters.ContainsKey("GlobalTemplateSnapshot")&&e.Parameters.ContainsKey("GlobalApplication"))
            .ToDictionary(e=>e.Parameters["GlobalApplication"],e=>e);
        var grouped=newestFirst.Where(e=>!e.Parameters.ContainsKey("GlobalTemplateSnapshot")&&e.Parameters.TryGetValue("GlobalApplication",out var id)&&applications.ContainsKey(id))
            .GroupBy(e=>e.Parameters["GlobalApplication"]).ToDictionary(g=>g.Key,g=>(IReadOnlyList<HistoryEntry>)g.Reverse().ToArray());
        var items=new List<HistoryItem>();
        foreach(var entry in newestFirst)
        {
            if(entry.Parameters.TryGetValue("GlobalApplication",out var id)&&grouped.ContainsKey(id)&&!entry.Parameters.ContainsKey("GlobalTemplateSnapshot"))continue;
            items.Add(new HistoryItem(entry,entry.Parameters.TryGetValue("GlobalApplication",out var app)&&grouped.TryGetValue(app,out var actions)&&entry.Parameters.ContainsKey("GlobalTemplateSnapshot")?actions:[]));
        }
        return items;
    }
    private static readonly string[] Kinds=["Todo","Ediciones manuales","Randomizaciones y acciones","Global Template"];
    internal ListBox List {get;}
    internal ComboBox SectionFilter {get;}
    internal ComboBox KindFilter {get;}
    internal System.Windows.Controls.TextBox SearchFilter {get;}

    public HistoryView(ProjectHistory history):this(history,null){}
    internal HistoryView(ProjectHistory history,Func<Task>? afterUndo)
    {
        Background=(Brush)FindResource("Bg");var grid=new Grid {Margin=new Thickness(22)};
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new());
        var header=new StackPanel {Margin=new Thickness(0,0,0,12)};
        header.Children.Add(new TextBlock {Text="Historial del proyecto",FontSize=23,FontWeight=FontWeights.SemiBold});
        header.Children.Add(new TextBlock {Text="Cada cambio guardado, en orden, con lo que se hizo y los archivos que tocó. Las recetas aplicadas se agrupan en un solo bloque.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,6,0,12)});
        var actions=new WrapPanel();header.Children.Add(actions);
        var export=new Button {Content="Exportar historial (JSON)",Margin=new Thickness(0,0,8,8)};
        export.Click+=(_,_)=>
        {
            var dialog=new Microsoft.Win32.SaveFileDialog {FileName="historial-progressive.json",Filter="Historial JSON|*.json"};
            if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
            try{history.Export(dialog.FileName);StudioDialog.Notice(Window.GetWindow(this),"Historial exportado","El archivo contiene las acciones confirmadas y sus parámetros.");}
            catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo exportar",ex.Message);}
        };
        var verify=new Button{Content="Comprobar archivos actuales",Margin=new Thickness(0,0,8,8)};
        actions.Children.Add(verify);actions.Children.Add(export);
        var validation=new TextBlock{Text="Estado actual sin comprobar. El historial registra operaciones pasadas.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,0)};
        header.Children.Add(validation);
        grid.Children.Add(header);
        // Filters: section, kind and free text.
        var filters=new WrapPanel{Margin=new Thickness(0,0,0,12)};Grid.SetRow(filters,1);grid.Children.Add(filters);
        SearchFilter=UiKit.SearchBox("Buscar: entrenador, objeto, acción…","Buscar en el historial");SearchFilter.Margin=new Thickness(0,0,8,6);SearchFilter.Width=260;
        SectionFilter=new ComboBox{MinWidth=220,Margin=new Thickness(0,0,8,6)};System.Windows.Automation.AutomationProperties.SetName(SectionFilter,"Sección");
        KindFilter=new ComboBox{MinWidth=200,Margin=new Thickness(0,0,8,6),ItemsSource=Kinds,SelectedIndex=0};System.Windows.Automation.AutomationProperties.SetName(KindFilter,"Tipo de cambio");
        var count=UiKit.Small("",true,12);count.VerticalAlignment=VerticalAlignment.Center;
        filters.Children.Add(SearchFilter);filters.Children.Add(SectionFilter);filters.Children.Add(KindFilter);filters.Children.Add(count);
        HistoryInspection? inspection=null;
        CancellationTokenSource? pending=null;
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(340)});body.ColumnDefinitions.Add(new());Grid.SetRow(body,2);grid.Children.Add(body);
        var details=new StackPanel {Margin=new Thickness(20,0,0,0)};
        var scroll=new ScrollViewer {Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetColumn(scroll,1);body.Children.Add(scroll);
        IReadOnlyList<HistoryEntry> entries=history.Read().Entries.Reverse().ToArray();
        List=new ListBox();ScrollViewer.SetHorizontalScrollBarVisibility(List,ScrollBarVisibility.Disabled);body.Children.Add(List);
        string State(HistoryEntry entry)=>inspection is null||!inspection.Entries.TryGetValue(entry.Id,out var state)?"Sin comprobar":state switch
            {HistoryEntryState.Verified=>"Cadena coherente con archivos actuales",HistoryEntryState.OutOfDate=>"Desfasado: no acredita el estado actual",_=>"Sin comprobación binaria"};
        string SectionOf(HistoryEntry entry)=>SectionProgress.RouteFor(entry) is string route?SectionCatalog.Label(route):entry.Module;
        string When(HistoryEntry entry)=>entry.Time.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        string Title(HistoryItem item)=>item.IsRecipe?item.Main.Action:SectionProgress.Describe(item.Main).Title;
        bool IsManual(HistoryEntry entry)=>entry.Kind!="Action"&&entry.Module!="Global Template";
        void RefreshSections()
        {
            var routes=entries.Select(SectionProgress.RouteFor).OfType<string>().GroupBy(r=>r).OrderByDescending(g=>g.Count()).ToArray();
            object? previous=SectionFilter.SelectedItem;
            SectionFilter.ItemsSource=new[]{"Todas las secciones"}.Concat(routes.Select(g=>$"{SectionCatalog.Label(g.Key)} ({g.Count()})")).ToArray();
            SectionFilter.Tag=new string?[]{null}.Concat(routes.Select(g=>g.Key)).ToArray();
            SectionFilter.SelectedIndex=previous is string text&&((string[])SectionFilter.ItemsSource).Contains(text)?Array.IndexOf((string[])SectionFilter.ItemsSource,text):0;
        }
        bool Matches(HistoryItem item)
        {
            string? section=SectionFilter.Tag is string?[] routes&&SectionFilter.SelectedIndex>0?routes[SectionFilter.SelectedIndex]:null;
            if(section is not null&&!item.All.Any(e=>SectionProgress.RouteFor(e)==section))return false;
            bool kind=KindFilter.SelectedIndex switch{1=>IsManual(item.Main),2=>!IsManual(item.Main)&&!item.IsRecipe&&item.Main.Module!="Global Template",3=>item.IsRecipe||item.Main.Module=="Global Template",_=>true};
            if(!kind)return false;
            string text=SearchFilter.Text.Trim();
            if(text.Length==0)return true;
            return item.All.Any(e=>SectionProgress.Describe(e) is var d&&(d.Title+" "+d.Detail+" "+e.Module+" "+e.Action).Contains(text,StringComparison.CurrentCultureIgnoreCase)
                ||e.Changes.Any(c=>(c.Name+" "+c.Field+" "+c.After).Contains(text,StringComparison.CurrentCultureIgnoreCase)));
        }
        FrameworkElement Row(HistoryItem item)
        {
            var stack=new StackPanel{Margin=new Thickness(2,2,2,2)};
            stack.Children.Add(new TextBlock{Text=Title(item),FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
            var described=SectionProgress.Describe(item.Main);
            string line=item.IsRecipe?$"Global Template · {item.Actions.Count} acciones":SectionOf(item.Main)+(described.Detail.Length>0?" · "+described.Detail:"");
            stack.Children.Add(UiKit.Small(line,true,12,wrap:true));
            stack.Children.Add(UiKit.Small(When(item.Main)+" · "+State(item.Main),true,11));
            return stack;
        }
        HistoryItem[] shown=[];
        Action? refreshUndoAvailability=null;
        string? UndoReason(Guid entry)
        {
            try{return RestorePoints.WhyNot(history.StorageDirectory,history.ProjectRoot,entry);}
            catch(Exception ex)when(ex is System.IO.IOException or UnauthorizedAccessException){return ex.Message;}
        }
        void ShowDetails()
        {
            refreshUndoAvailability=null;
            details.Children.Clear();
            if(List.SelectedItem is not ListBoxItem {Tag:HistoryItem item})
            {
                if(entries.Count==0)Add("Todavía no hay cambios guardados en este proyecto.");
                else if(shown.Length==0)Add("Ningún cambio coincide con los filtros.");
                return;
            }
            var main=item.Main;
            details.Children.Add(new TextBlock{Text=Title(item),FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
            Add((item.IsRecipe?"Global Template":SectionOf(main))+" · "+main.Time.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
            Add(State(main),true);
            if(item.IsRecipe)
            {
                details.Children.Add(UiKit.Heading($"Acciones aplicadas ({item.Actions.Count})"));
                foreach(var group in item.Actions.GroupBy(a=>SectionProgress.RouteFor(a)??"").OrderBy(g=>SectionCatalog.Label(g.Key)))
                {
                    Add(group.Key.Length>0?SectionCatalog.Label(group.Key):"Otras",true);
                    foreach(var action in group)Add("• "+SectionProgress.Describe(action).Title);
                }
                Add("Las acciones de una receta se ejecutan juntas: sus archivos son el resultado conjunto de toda la receta.");
            }
            else
            {
                if(main.Kind=="Action")Add("Se conserva la acción y sus opciones. Las randomizaciones usarán una semilla nueva en cada ROM.");
                var described=SectionProgress.Describe(main);
                if(described.Detail.Length>0)Add(described.Detail);
                if(main.Changes.Count>0)
                {
                    details.Children.Add(UiKit.Heading($"Qué cambió ({main.Changes.Count})"));
                    foreach(var change in main.Changes.Take(300))
                    {
                        Add(change.Name+" · "+change.Field,true);
                        var values=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6)};
                        values.Inlines.Add(new System.Windows.Documents.Run(change.Before));
                        values.Inlines.Add(new System.Windows.Documents.Run("  →  "){FontWeight=FontWeights.SemiBold});
                        var after=new System.Windows.Documents.Run(change.After);after.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"Accent");values.Inlines.Add(after);
                        details.Children.Add(values);
                    }
                    if(main.Changes.Count>300)Add($"… y {main.Changes.Count-300} cambios más (el JSON exportado los contiene todos).");
                }
            }
            // «Deshacer»: writes back the files as they were before this change (only while nothing changed them afterwards).
            if(RestorePoints.Find(history.StorageDirectory,main.Id) is not null)
            {
                string? reason=UndoReason(main.Id);
                var undo=new Button{Content=item.IsRecipe?"Deshacer toda la receta":"Deshacer este cambio",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,4),IsEnabled=reason is null&&!UiProgress.IsInteractionBlocked};
                EditorPresentation.StyleAction(undo,EditorPresentation.ActionRole.Primary);
                undo.Click+=async(_,_)=>
                {
                    if(UiProgress.IsInteractionBlocked)return;
                    if(!StudioDialog.Confirm(Window.GetWindow(this),"Deshacer cambio",$"Los archivos volverán a como estaban antes de «{Title(item)}».",
                        "Queda registrado en el historial como una restauración. Las recetas no lo reproducen."))return;
                    try
                    {
                        RestorePoints.Undo(history,history.ProjectRoot,main.Id);
                        validation.Text="Cambio deshecho y verificado.";
                        if(afterUndo is not null)await afterUndo();
                        else{entries=history.Read().Entries.Reverse().ToArray();RefreshSections();Fill();}
                    }
                    catch(Exception ex) when(ex is InvalidOperationException or System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
                    {StudioDialog.Error(Window.GetWindow(this),"No se pudo deshacer",ex.Message);}
                };
                details.Children.Add(undo);
                const string available="Se guardó una copia de los archivos anteriores a este cambio.";
                Add(reason??available);
                var undoHint=(TextBlock)details.Children[^1];
                refreshUndoAvailability=()=>
                {
                    // An operation may hold targets exclusively; only recheck once it releases them.
                    if(UiProgress.IsInteractionBlocked){undo.IsEnabled=false;return;}
                    string? latest=UndoReason(main.Id);
                    undo.IsEnabled=latest is null&&!UiProgress.IsInteractionBlocked;
                    undoHint.Text=latest??available;
                };
            }
            details.Children.Add(UiKit.Heading($"Archivos ({main.Files.Count})"));
            foreach(var file in main.Files)Add(file.Path+"  ·  "+Short(file.BeforeSha256)+" → "+Short(file.AfterSha256));
            // Raw module, action and parameters (a recipe snapshot can be thousands of characters): one click away.
            var technical=new StackPanel{Margin=new Thickness(20,4,0,0)};
            void Tech(string text,bool strong=false)=>technical.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=12,FontWeight=strong?FontWeights.SemiBold:FontWeights.Normal,Foreground=(Brush)FindResource(strong?"TextMain":"TextSoft")});
            Tech("Módulo: "+main.Module);Tech("Acción: "+main.Action);Tech("Tipo: "+main.Kind);
            if(main.Parameters.Count>0){Tech("Parámetros",true);foreach(var parameter in main.Parameters)Tech(parameter.Key+": "+(parameter.Value.Length>1500?parameter.Value[..1500]+$"… ({parameter.Value.Length:N0} caracteres; completo en el JSON exportado)":parameter.Value));}
            foreach(var file in main.Files)Tech(file.Path+"\n"+file.BeforeSha256+" → "+file.AfterSha256);
            details.Children.Add(new Expander{Header="Detalles técnicos",IsExpanded=false,Content=technical,Margin=new Thickness(-6,14,0,0)});
        }
        void RefreshUndoForInteraction()
        {
            if(IsLoaded)refreshUndoAvailability?.Invoke();
        }
        void InteractionChanged(bool blocked)
        {
            if(Dispatcher.CheckAccess())RefreshUndoForInteraction();
            else Dispatcher.BeginInvoke((Action)RefreshUndoForInteraction);
        }
        static string Short(string hash)=>hash.Length>12?hash[..12]:hash;
        void Fill()
        {
            Guid? selected=(List.SelectedItem as ListBoxItem)?.Tag is HistoryItem chosen?chosen.Main.Id:null;
            List.Items.Clear();
            shown=Group(entries).Where(Matches).ToArray();
            foreach(var item in shown)List.Items.Add(new ListBoxItem {Tag=item,Content=Row(item)});
            count.Text=shown.Length==Group(entries).Count?$"{shown.Length} registros":$"{shown.Length} de {Group(entries).Count} registros";
            if(shown.Length>0)List.SelectedIndex=Array.FindIndex(shown,item=>item.Main.Id==selected) is int index&&index>=0?index:0;
            ShowDetails();
        }
        List.SelectionChanged+=(_,_)=>ShowDetails();
        SearchFilter.TextChanged+=(_,_)=>Fill();SectionFilter.SelectionChanged+=(_,_)=>Fill();KindFilter.SelectionChanged+=(_,_)=>Fill();
        RefreshSections();Fill();
        async Task VerifyAsync()
        {
            if(pending is not null)return;
            var cancellation=new CancellationTokenSource();pending=cancellation;verify.IsEnabled=false;
            using var interaction=UiProgress.BlockInteraction();
            UiProgress.Begin("Comprobando historial y archivos actuales…");
            validation.Text="Comprobando SHA-256 de los archivos registrados…";
            inspection=null;Fill();
            try
            {
                var result=await Task.Run(()=>history.InspectCurrentState(cancellation.Token),cancellation.Token);
                if(cancellation.IsCancellationRequested||!IsLoaded)return;
                inspection=result;entries=result.Document.Entries.Reverse().ToArray();validation.Text=result.Summary;RefreshSections();Fill();
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
            catch(Exception ex){if(IsLoaded)validation.Text="No se pudo comprobar el historial: "+ex.Message;}
            finally{pending=null;cancellation.Dispose();verify.IsEnabled=true;UiProgress.End();}
        }
        verify.Click+=async(_,_)=>await VerifyAsync();
        Loaded+=async(_,_)=>
        {
            UiProgress.InteractionLockChanged-=InteractionChanged;
            UiProgress.InteractionLockChanged+=InteractionChanged;
            await VerifyAsync();
        };
        Unloaded+=(_,_)=>{UiProgress.InteractionLockChanged-=InteractionChanged;pending?.Cancel();};
        void Add(string text,bool strong=false)=>details.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,FontWeight=strong?FontWeights.SemiBold:FontWeights.Normal,Margin=new Thickness(0,0,0,4),Foreground=(Brush)FindResource(strong?"TextMain":"TextSoft")});
        Content=grid;
    }
}
