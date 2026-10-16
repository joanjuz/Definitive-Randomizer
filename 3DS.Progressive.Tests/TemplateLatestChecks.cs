using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
internal static class TemplateLatestChecks
{
    private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    private static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Private)!.GetValue(o)!;
    private static object? Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Private)!.Invoke(o,args);
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var state=new TestState();
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var p=f.Session;
            string path=Path.Combine(f.Root,"old-evolution.csv");File.WriteAllText(path,"Source,Target,Method,Level,Argument,Form,ItemName,AltItemName\n95,208,19,,233,-1,Metal Coat,Revestimiento metálico\n");
            var evo=AuthoredTemplate.Import(p,"Evolutions",path);check(evo.Rows[0]["Argument"]=="233",version+" old English evolution item names fall back to explicit stable numeric ID");string saved=Path.Combine(f.Root,"evo.json");evo.Export(saved);check(!File.ReadAllText(saved).Contains("Metal Coat")&&!File.ReadAllText(saved).Contains("Revestimiento"),version+" exported evolution rows contain IDs rather than localized item names");
            string[] conflicting=p.ItemNames.ToArray();conflicting[3]="Metal Coat";check(EvolutionTemplates.Load(path,f.Game.GetText(TextName.SpeciesNames),conflicting,p.Generation)[0].Argument==233,version+" explicit item ID takes priority over a misleading localized display name");
            string[] names=p.ItemNames.ToArray();names[233]="Nombre de objeto en otro idioma";check(EvolutionTemplates.Load(path,f.Game.GetText(TextName.SpeciesNames),names,p.Generation)[0].Argument==233,version+" evolution template resolves identically after data language changes");
            File.WriteAllText(path,"Source,Target,Method,Level,Argument,Form,ItemName\n95,208,19,,,-1,Objeto desconocido\n");reject(()=>AuthoredTemplate.Import(p,"Evolutions",path),version+" unknown item without explicit ID is rejected rather than guessed");
            var moves=AuthoredTemplate.Create(p,"Moves");moves.Rows.Add(new(){["Move"]="7",["Type"]="Fire",["Category"]="Physical",["Stat1"]="Special Attack",["InGameDescription"]="Texto propio"});string json=Path.Combine(f.Root,"named-moves.json");File.WriteAllText(json,moves.Serialize());moves=AuthoredTemplate.Import(p,"Moves",json);check(moves.Rows[0]["Type"]=="9"&&moves.Rows[0]["Category"]=="1"&&moves.Rows[0]["Stat1"]=="3",version+" imported named move selectors normalize to stable type category and stat IDs");var payload=AuthoredTemplate.Store(p,moves);string asset=Path.Combine(payload["Asset"]!["Directory"]!.GetValue<string>(),payload["Asset"]!["Name"]!.GetValue<string>());var stored=JsonSerializer.Deserialize<AuthoredTemplate>(File.ReadAllText(asset))!;check(stored.Rows[0]["Type"]=="9"&&stored.Rows[0]["InGameDescription"]=="Texto propio",version+" immutable replay template stores numeric identities and preserves intentional free text");
        }
    }
    internal static void Ui(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var view=new TemplateStudioView(f.Session);var kind=Field<System.Windows.Controls.ComboBox>(view,"_kind");check(!kind.IsEditable&&kind is not SearchableTemplateComboBox,version+" template type choice is fixed and cannot be typed into");
            var combo=new SearchableTemplateComboBox{ItemsSource=new[]{new TemplateChoice("1","Nombre")},ChoiceValue="1"};combo.Measure(new Size(300,40));combo.Arrange(new Rect(0,0,300,40));combo.ApplyTemplate();var editor=combo.Template.FindName("PART_EditableTextBox",combo) as System.Windows.Controls.TextBox;
            check(ReferenceEquals(combo.Style,System.Windows.Application.Current.FindResource(typeof(System.Windows.Controls.ComboBox))),version+" searchable selector uses application dark combo style");check(editor is not null&&editor.Visibility==Visibility.Visible,version+" dark combo template contains the editable search field");check(editor?.Background is SolidColorBrush brush&&brush.Color==((SolidColorBrush)System.Windows.Application.Current.FindResource("Surface")).Color,version+" search field background is dark rather than Windows white");
            var evo=AuthoredTemplate.Create(f.Session,"Evolutions");Call(view,"LoadDocument",evo);check(Field<System.Windows.Controls.DataGrid>(view,"_grid").Columns.Count>=5,version+" new evolution editor opens independently of preset language resolution");Call(view,"AddRow");check(Field<System.Collections.IList>(view,"_rows").Count==1,version+" evolution template can create rows");
            var row=Field<System.Collections.ObjectModel.ObservableCollection<TemplateStudioRow>>(view,"_rows")[0];
            foreach(var pair in new[]{(6,"233"),(8,"84"),(19,"221"),(21,"7"),(22,"25"),(29,"17")})
            {
                row["Argument"]=pair.Item2;row["Method"]=pair.Item1.ToString();
                check(row.HasArgumentChoices&&row.ArgumentChoices.Any(c=>c.Value==pair.Item2),version+" contextual evolution argument selector contains numeric choice "+pair.Item1);
                check(row["Argument"]==pair.Item2,version+" changing method preserves argument ID");
                check(row.ArgumentChoices.Select(c=>c.Value).Distinct().Count()==row.ArgumentChoices.Count,version+" argument choices have unique IDs including type zero");
            }
            row["Method"]="4";check(!row.HasArgumentChoices&&row["Argument"]=="17",version+" numeric evolution method retains editable numeric argument");
            row["Argument"]="65535";row["Method"]="8";check(row.ArgumentChoices.Any(c=>c.Value=="65535"),version+" imported unknown argument remains selectable without data loss");
            var argumentColumn=Field<System.Windows.Controls.DataGrid>(view,"_grid").Columns.OfType<DataGridTemplateColumn>().Single(c=>c.Header.ToString()!.StartsWith("Argumento"));
            argumentColumn.CellTemplate.Seal();var argumentCell=(FrameworkElement)argumentColumn.CellTemplate.LoadContent();argumentCell.DataContext=row;argumentCell.Measure(new Size(260,45));argumentCell.Arrange(new Rect(0,0,260,45));
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.DataBind);var controls=((System.Windows.Controls.Grid)argumentCell).Children;check(controls.OfType<SearchableTemplateComboBox>().Single().Visibility==Visibility.Visible&&controls.OfType<System.Windows.Controls.TextBox>().Single().Visibility==Visibility.Collapsed,version+" argument cell renders searchable dropdown for item methods");
            row["Method"]="4";System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.DataBind);check(controls.OfType<SearchableTemplateComboBox>().Single().Visibility==Visibility.Collapsed&&controls.OfType<System.Windows.Controls.TextBox>().Single().Visibility==Visibility.Visible,version+" argument cell switches to numeric input without clearing value");
            var description=new TemplateDescriptionWindow("Línea uno\nLínea dos");check(description.Description=="Línea uno\nLínea dos"&&description.Width>=700&&description.Height>=350,version+" expanded description editor preserves multiline content");description.Close();
            check(OptionalDescriptionConverter.Instance.Convert("  ",typeof(object),null!,System.Globalization.CultureInfo.InvariantCulture) is null&&OptionalDescriptionConverter.Instance.Convert("Descripción",typeof(object),null!,System.Globalization.CultureInfo.InvariantCulture)?.ToString()=="Descripción",version+" empty descriptions suppress tooltips while real descriptions remain");
            var tip=new System.Windows.Controls.ToolTip{Content="Descripción"};tip.Measure(new Size(520,500));check(tip.Background is SolidColorBrush tipBrush&&tipBrush.Color==((SolidColorBrush)System.Windows.Application.Current.FindResource("Surface")).Color&&tip.MaxWidth==520,version+" description tooltip uses dark application palette and bounded width");
            if(f.Game.Generation==6)TemplateStudioChecks.Field6Fixture(f);var field=AuthoredTemplate.Create(f.Session,"FieldItems");field.AllowedItems=[17,19];field.BannedItems=[18];field.PrepareMetadata(f.Session);Call(view,"LoadDocument",field);check(Field<TabItem>(view,"_tableTab").Visibility==Visibility.Collapsed&&Field<TabControl>(view,"_tabs").SelectedItem==Field<TabItem>(view,"_catalogueTab"),version+" Field Items hides the empty table and selects its catalog");check(kind.Items.Cast<TemplateChoice>().Single(c=>c.Value=="FieldItems").Label=="Field Items",version+" template menu uses Field Items naming");
            var policy=Field<System.Windows.Controls.ComboBox>(view,"_policyFilter");var items=Field<System.Collections.ObjectModel.ObservableCollection<TemplateItemPolicy>>(view,"_items");var filtered=CollectionViewSource.GetDefaultView(items);policy.SelectedValue="Allowed";check(filtered.Cast<TemplateItemPolicy>().Select(i=>i.Id).Order().SequenceEqual(new[]{17,19}),version+" allowed filter displays effective whitelist");policy.SelectedValue="Banned";check(!filtered.Cast<TemplateItemPolicy>().Any(i=>i.Id is 17 or 19)&&filtered.Cast<TemplateItemPolicy>().Any(i=>i.Id==18),version+" banned filter includes effective exclusions");items.Single(i=>i.Id==17).Policy="Banned";items.Single(i=>i.Id==19).Policy="Default";policy.SelectedValue="Allowed";check(filtered.Cast<TemplateItemPolicy>().Count()==items.Count-2,version+" clearing whitelist updates all default items to allowed");policy.SelectedValue="Banned";check(filtered.Cast<TemplateItemPolicy>().Select(i=>i.Id).Order().SequenceEqual(new[]{17,18}),version+" policy changes refresh the current banned set");var search=Field<System.Windows.Controls.TextBox>(view,"_filter");search.Text="18";check(filtered.Cast<TemplateItemPolicy>().All(i=>i.Id==18||i.Name.Contains("18")),version+" item text search combines with policy filter");
        }
    }
}
