using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;

internal static class TemplateFeedbackChecks
{
    private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Private)!.GetValue(target)!;
    private static object? Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private)!.Invoke(target,args);
    internal static void Ui(Action<bool,string> check)
    {
        using var state=new TestState();
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var p=f.Session;var view=new TemplateStudioView(p);
            Call(view,"AddRow");var rows=Field<System.Collections.IList>(view,"_rows");var row=(TemplateStudioRow)rows[0]!;row["Move"]="7";
            var choices=new[]{new TemplateChoice("7","007 · Puño Fuego"),new TemplateChoice("14","014 · Impactrueno"),new TemplateChoice("15","015 · Trueno")};
            var combo=new SearchableTemplateComboBox{ItemsSource=choices};var other=new SearchableTemplateComboBox{ItemsSource=choices,ChoiceValue="7"};BindingOperations.SetBinding(combo,SearchableTemplateComboBox.ChoiceValueProperty,new Binding("[Move]"){Source=row,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
            combo.Filter("TRUENO");check(combo.Items.Count==2&&combo.Items.Cast<TemplateChoice>().All(c=>c.Label.Contains("trueno",StringComparison.OrdinalIgnoreCase)),version+" searchable selector filters by a middle substring ignoring case");
            check(combo.ChoiceValue=="7"&&row["Move"]=="7",version+" typing does not erase or change the stored move");check(other.Items.Count==3,version+" selector filters are private to each control");
            combo.Filter("puno");check(combo.Items.Count==1&&((TemplateChoice)combo.Items[0]).Value=="7",version+" selector search ignores accents including ñ");combo.Filter("014");check(combo.Items.Count==1,version+" selector accepts numeric ID search");combo.SelectedItem=choices[1];check(row["Move"]=="14",version+" choosing a filtered result commits its original numeric ID");
            combo.Filter("no existe");check(combo.Items.Count==0&&row["Move"]=="14",version+" unmatched searches preserve confirmed data");
            view.Measure(new Size(1400,850));view.Arrange(new Rect(0,0,1400,850));view.UpdateLayout();var buttons=Walk(view).OfType<System.Windows.Controls.Button>().ToArray();check(!buttons.Any(b=>Equals(b.Content,"Nuevo"))&&buttons.Any(b=>Equals(b.Content,"Usar template")),version+" no ambiguous New button and explicit Use template remains");
            check(buttons.Any(b=>Equals(b.Content,"Eliminar fila"))&&!buttons.Any(b=>Equals(b.Content,"Eliminar filas")),version+" delete is labelled in singular");check(Field<System.Windows.Controls.DataGrid>(view,"_grid").SelectionMode==DataGridSelectionMode.Single,version+" row deletion uses single selection");
            var shop=AuthoredTemplate.Create(p,"Shops");shop.PrepareMetadata(p);Call(view,"LoadDocument",shop);check(Field<System.Windows.Controls.DataGrid>(view,"_grid").Columns.Count==2&&Field<System.Collections.IList>(view,"_rows").Count==0,version+" shop editor starts empty with only progression and item columns");
            check(Field<System.Windows.Controls.Button>(view,"_progressive").Visibility==Visibility.Collapsed,version+" shop editor hides Progressive import");check(shop.ShopLabels.Count()==(p.Generation==6?9:8)&&!shop.ShopLabels.Any(c=>c.Label.Contains("BP")),version+" only normal progression stages are selectable");
            Call(view,"AddRow");Call(view,"AddRow");buttons.First(b=>Equals(b.Content,"Eliminar fila")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));check(Field<System.Collections.IList>(view,"_rows").Count==1,version+" delete removes only the selected row");
            var moves=AuthoredTemplate.Create(p,"Moves");Call(view,"LoadDocument",moves);check((Field<System.Windows.Controls.Button>(view,"_progressive").Visibility==Visibility.Visible)==(version!=GameVersion.SM),version+" move Progressive import is visible only for supported games");
            if(version!=GameVersion.SM)
            {
                var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                try{var task=(Task)Call(view,"ImportProgressiveAsync")!;var frame=new DispatcherFrame();task.ContinueWith(_=>view.Dispatcher.BeginInvoke(()=>frame.Continue=false));Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();check(Field<System.Collections.IList>(view,"_rows").Count>10&&!Field<System.Windows.Controls.TextBlock>(view,"_status").Text.Contains("subproceso"),version+" real Progressive import completes with UI-thread-safe metadata and selection");}
                finally{SynchronizationContext.SetSynchronizationContext(old);}
            }
        }
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var node in Walk(child))yield return node;}
    internal static void Logic(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);var d=AuthoredTemplate.Create(f.Session,"Shops");d.Rows.Add(new(){["Stage"]="0",["Item"]="200"});d.Rows.Add(new(){["Stage"]="2",["Item"]="201"});d.ValidateShape(f.Session);
            int count=AuthoredTemplate.ProgressionStageCount(f.Session);var rows=Enumerable.Range(0,count+3).Select(i=>new List<int>{17,i+30}).ToArray();d.AppendToStages(rows,count);
            check(rows.Take(count).All(row=>row.Contains(200)),version+" zero-stage additions propagate to all later normal stores");check(rows.Take(2).All(row=>!row.Contains(201))&&rows.Skip(2).Take(count-2).All(row=>row.Contains(201)),version+" stage two additions never leak into earlier stores");check(rows.Skip(count).All(row=>row.Count==2),version+" special stores remain untouched");check(rows.Select((row,i)=>row.Take(2).SequenceEqual(new[]{17,i+30})).All(v=>v),version+" every original inventory slot remains unchanged");
            int[] sizes=rows.Select(row=>row.Count).ToArray();d.AppendToStages(rows,count);check(sizes.SequenceEqual(rows.Select(row=>row.Count)),version+" reapplying a template never adds duplicate items");
            string path=Path.Combine(f.Root,"additions.json");d.Export(path);var loaded=AuthoredTemplate.Import(f.Session,"Shops",path);check(loaded.ShopMode=="AppendProgression"&&loaded.Rows.Count==2,version+" addition semantics survive export and import");
            d.Rows.Add(new(){["Stage"]=count.ToString(),["Item"]="202"});reject(()=>d.ValidateShape(f.Session),version+" out-of-range progress is rejected");check(!AuthoredTemplate.CanImportProgressive(f.Session,"Shops"),version+" no misleading Progressive preset is offered for additions");
        }
    }
    internal static void Copies(Action<bool,string> check,Action<Action,string> reject,string[] roots)
    {
        using var state=new TestState();
        foreach(string root in roots)
        {
            var version=root.Contains("Ultra",StringComparison.OrdinalIgnoreCase)?GameVersion.USUM:root.Contains("PokemonSol",StringComparison.OrdinalIgnoreCase)?GameVersion.SM:root.EndsWith("PokemonX",StringComparison.OrdinalIgnoreCase)||root.EndsWith("PokemonY",StringComparison.OrdinalIgnoreCase)?GameVersion.XY:GameVersion.ORAS;
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var originals=new List<(string Path,byte[] Data)>();
            void Copy(string from,string to){byte[] bytes=File.ReadAllBytes(from);originals.Add((from,bytes));File.WriteAllBytes(to,bytes);}
            if(f.Game.Generation==7)Copy(Path.Combine(root,"ExtractedRomFS","Shop.cro"),Path.Combine(f.Game.RomFS,"Shop.cro"));
            else{Copy(Directory.GetFiles(Path.Combine(root,"ExtractedExeFS")).Single(path=>Path.GetFileName(path) is "code.bin" or ".code.bin"),Path.Combine(f.Game.ExeFS,"code.bin"));string header=Directory.GetFiles(root).First(path=>Path.GetFileName(path).Contains("ExHeader",StringComparison.OrdinalIgnoreCase));Copy(header,Path.Combine(f.Root,"ExHeader.bin"));}
            var d=AuthoredTemplate.Create(f.Session,"Shops");d.Rows.Add(new(){["Stage"]="0",["Item"]="200"});d.Rows.Add(new(){["Stage"]="2",["Item"]="201"});
            int[][] Read()=>f.Game.Generation==6?new GameMart6Service(f.Session).ReadAllItems():new GameMartService(f.Session).RegularShops.Concat(new GameMartService(f.Session).BpShops).Select(shop=>shop.Rows.Select(row=>row.ItemId).ToArray()).ToArray();
            int[][] before=Read();if(version==GameVersion.SM){reject(()=>d.Run(f.Session,1,false),"SM refuses an unsupported expansion before saving");check(before.SelectMany(row=>row).SequenceEqual(Read().SelectMany(row=>row)),"SM unsupported expansion preserves every inventory");continue;}
            d.Run(f.Session,1,true);check(before.SelectMany(row=>row).SequenceEqual(Read().SelectMany(row=>row)),version+" real-copy addition preview is read-only");var payload=AuthoredTemplate.Store(f.Session,d);d.Run(f.Session,1,false,payload);f.Session.History.RecordTemplateUse(payload);var after=Read();int count=AuthoredTemplate.ProgressionStageCount(f.Session);
            check(after.Take(count).All(row=>row.Contains(200))&&after.Skip(2).Take(count-2).All(row=>row.Contains(201)),version+" actual binary adds new slots at requested and later stages");check(before.Select((row,i)=>row.SequenceEqual(after[i].Take(row.Length))).All(v=>v),version+" actual binary retains original inventory prefixes");check(before.Skip(count).SelectMany(row=>row).SequenceEqual(after.Skip(count).SelectMany(row=>row)),version+" actual binary preserves special and BP inventories");
            d.Run(f.Session,1,false,payload);check(after.SelectMany(row=>row).SequenceEqual(Read().SelectMany(row=>row)),version+" binary template replay is idempotent");
            var doc=GlobalTemplateDocument.Empty(f.Session.GameName,f.Session.Generation);doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});doc.MergeHistoryActions(f.Session.History);string global=Path.Combine(f.Root,"shops-global.json");doc.Save(global);doc=GlobalTemplateDocument.Load(global,f.Session.GameName,f.Session.Generation);ManualEditTemplate.Apply(f.Session,doc);check(after.SelectMany(row=>row).SequenceEqual(Read().SelectMany(row=>row)),version+" Global replay preserves addition semantics");check(originals.All(file=>File.ReadAllBytes(file.Path).SequenceEqual(file.Data)),version+" original game files stay byte-identical");
        }
    }
}
