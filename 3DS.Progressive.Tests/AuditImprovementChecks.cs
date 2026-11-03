using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using TextBox=System.Windows.Controls.TextBox;

/// <summary>
/// Audit follow-up (03/11/2026): single section catalog, section stored in the history, readable history with filters
/// and grouped recipes, restore points and undo, randomization log, evolution argument by catalog, level-up move details,
/// static encounter values by name, gender ratio in words, first steps and the search palette of the DS/Switch workspaces.
/// </summary>
internal static class AuditImprovementChecks
{
    private static void Layout(FrameworkElement content,double w,double h){content.Measure(new Size(w,h));content.Arrange(new Rect(0,0,w,h));content.UpdateLayout();UiPump.Idle();content.UpdateLayout();UiPump.Idle();}
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var d in Descendants<T>(child))yield return d;}}
    private static void Capture(FrameworkElement content,int width,int height,string name)
    {
        Layout(content,width,height);var image=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(content);
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        string folder=Path.Combine(AppContext.BaseDirectory,"visual-redesign");Directory.CreateDirectory(folder);using var output=File.Create(Path.Combine(folder,name+".png"));png.Save(output);
    }
    private static HistoryEntry Entry(string module,string action,string kind="Edit",Dictionary<string,string>? parameters=null)=>
        new(Guid.NewGuid(),DateTimeOffset.UtcNow,module,action,[],parameters??[],[],kind);
    internal static void Run(Action<bool,string> check)
    {
        // Catalog: every hub tool points to a hub of the catalog, and every guided topic is in the catalog.
        check(SectionCatalog.HubOf.Values.All(h=>SectionCatalog.Find(h) is {Hub:null}),"Catalog_HubToolsBelongToHubs");
        check(GuidanceCatalog.Topics.Where(t=>t.Route!="Home").All(t=>SectionCatalog.Find(t.Route) is not null),"Catalog_CoversEveryGuidedTopic");
        check(GuidanceCatalog.SimpleRoutes.SetEquals(["Pokemon","Trainers","Encounters","StaticEncounters","Moves","LevelUp","MachineTutor","FieldItems","Marts","Evolutions","History","Configuration"]),"Catalog_SimpleModeKeepsItsSections");
        // History groups a recipe under the entry that applied it.
        var recipe=new Dictionary<string,string>{["GlobalTemplateSnapshot"]="{}",["GlobalApplication"]="g1"};
        var newestFirst=new[]{Entry("Global Template","trainers.randomize","Action",new(){["ActionId"]="trainers.randomize",["GlobalApplication"]="g1"}),
            Entry("Global Template","marts.randomize","Action",new(){["ActionId"]="marts.randomize",["GlobalApplication"]="g1"}),
            Entry("Global Template","Aplicar Global Template · receta","Action",recipe),Entry("Entrenadores","Editar Entrenadores")};
        var items=HistoryView.Group(newestFirst);
        check(items.Count==2&&items[0].IsRecipe&&items[0].Actions.Count==2&&!items[1].IsRecipe,"History_RecipeIsOneBlock");
        check(GenderRatio("0")=="Siempre macho"&&GenderRatio("255")=="Sin género"&&GenderRatio("127").Contains("50 % macho",StringComparison.Ordinal),"Pokemon_GenderRatioInWords");

        using var f=new Fixture(pk3DS.Core.GameVersion.USUM);ModuleChecks.CreateArchives(f);
        // Restore point and undo on a real commit of the fixture.
        string probe=Path.Combine(f.Root,"undo-probe.bin");File.WriteAllBytes(probe,[1,2,3]);
        f.Session.History.Commit([new FileChange(probe,[1,2,3],[9,9,9])],"Entrenadores","Randomizar entrenadores",[],new Dictionary<string,string>{["ActionId"]="trainers.randomize"});
        var last=f.Session.History.Read().Entries[^1];
        check(last.Section=="Trainers","History_StoresSectionOfNewEntries");
        check(RestorePoints.Find(f.Session.History.StorageDirectory,last.Id) is not null&&RestorePoints.WhyNot(f.Session.History.StorageDirectory,f.Root,last.Id) is null,"Undo_RestorePointAfterCommit");
        check(RestorePoints.LatestUndoable(f.Session.History,f.Root)?.Id==last.Id,"Undo_LatestUndoableIsTheNewestChange");
        RestorePoints.Undo(f.Session.History,f.Root,last.Id);
        var undo=f.Session.History.Read().Entries[^1];
        check(File.ReadAllBytes(probe).SequenceEqual(new byte[]{1,2,3})&&undo.Action.StartsWith("Restaurar archivos · deshacer",StringComparison.Ordinal)&&undo.Section=="Trainers","Undo_WritesBackOriginalsAndRecordsIt");
        check(RestorePoints.Find(f.Session.History.StorageDirectory,last.Id) is null,"Undo_RestorePointIsConsumed");
        f.Session.History.Commit([new FileChange(probe,[1,2,3],[4,4,4])],"Entrenadores","Editar Entrenadores",[]);
        var edited=f.Session.History.Read().Entries[^1];File.WriteAllBytes(probe,[7,7,7]);
        check(RestorePoints.WhyNot(f.Session.History.StorageDirectory,f.Root,edited.Id) is string reason&&reason.Contains("posterior",StringComparison.Ordinal),"Undo_RefusesWhenFilesChangedAfterwards");
        check(!new DirectoryInfo(f.Root).EnumerateFiles("*",SearchOption.AllDirectories).Any(file=>file.FullName.Contains(Path.Combine("RomFS",RestorePoints.FolderName),StringComparison.OrdinalIgnoreCase)),"Undo_RestorePointsOutsideRomFS");
        // History view: readable rows, filters, technical details collapsed.
        var history=new HistoryView(f.Session.History);Layout(history,1100,760);
        check(history.SectionFilter.Items.Count>=2&&history.KindFilter.Items.Count==4&&history.List.Items.Count>=3,"History_FiltersAndRows");
        history.SearchFilter.Text="deshacer";UiPump.Idle();
        check(history.List.Items.Count==1,"History_SearchFindsTheUndo");
        history.SearchFilter.Text="";UiPump.Idle();
        check(Descendants<Expander>(history).Any(e=>Equals(e.Header,"Detalles técnicos")&&!e.IsExpanded),"History_TechnicalDetailsCollapsed");
        Capture(history,1100,760,"audit-history");
        // Randomization log: every part is written, even when one is not available in the fixture.
        string log=RandomizationLog.Build(f.Session);
        check(log.Contains("REGISTRO DEL RANDOMIZADO",StringComparison.Ordinal)&&log.Contains("ENTRENADORES",StringComparison.Ordinal)&&log.Contains("CAMBIOS GUARDADOS",StringComparison.Ordinal)&&log.Contains("Randomizar entrenadores",StringComparison.Ordinal),"Log_HasEveryPartAndTheSavedChanges");
        // Evolutions: the argument is an item list for «Usar objeto» and a number for «Subir de nivel».
        var evolutions=new RecordEditorView(new GameEvolutionService(f.Session));Layout(evolutions,1170,760);
        var method=(ComboBox)evolutions.FieldControls["E0.Method"];
        method.SelectedValue=8;UiPump.Idle();
        check(evolutions.FieldControls["E0.Argument"] is ComboBox,"Evolution_ItemMethodUsesItemList");
        method.SelectedValue=4;UiPump.Idle();
        check(evolutions.FieldControls["E0.Argument"] is TextBox&&evolutions.EvolutionOverviewCard is not null,"Evolution_LevelMethodUsesNumberAndShowsRoutes");
        method.SelectedValue=8;UiPump.Idle();Capture(evolutions,1170,760,"audit-evolutions");
        // Level Up: type, category, power and accuracy next to each move.
        var levelUp=new LevelUpView(f.Session);Layout(levelUp,1170,760);
        check(levelUp.MoveTables[0].Columns.Select(c=>c.Header as string).SequenceEqual(["Nivel","Movimiento","Tipo","Cat.","Pot.","Prec."]),"LevelUp_ShowsMoveDetails");
        Capture(levelUp,1170,760,"audit-levelup");
        // Static encounters (Gen7): nature 0 means random, so the list is shifted by one.
        var statics=new GameStaticEncounterService(f.Session);
        var natureField=statics.Entries.Select(e=>statics.Read(e.Id).SelectMany(g=>g.Fields).FirstOrDefault(x=>x.Key=="Nature"&&x.Max==255)).FirstOrDefault(x=>x is not null);
        check(natureField is null||natureField.Choices!.First() is {Id:0,Name:"Aleatoria"}&&natureField.Choices!.Skip(1).First().Id==1,"Static_NatureListMatchesStoredValue");
        // First steps on Inicio without a project; the DS workspace has its sections in the palette.
        var main=new MainWindow();Layout((FrameworkElement)main.Content,1280,800);
        var start=((ContentControl)main.FindName("HomeHost")).Content as FrameworkElement;
        check(start is not null&&Descendants<TextBlock>(start).Any(t=>t.Text=="PRIMEROS PASOS"),"Home_FirstStepsWithoutProject");
        Capture((FrameworkElement)main.Content,1280,800,"audit-first-steps");
        main.Close();UiPump.Idle();
        var nds=new NdsWorkspaceView(new Window());
        check(nds.PaletteEntries("Nintendo DS").Count>=10,"Palette_DsWorkspaceListsItsSections");
    }
    private static string GenderRatio(string value)=>(string)typeof(MainWindow).GetMethod("GenderRatioMeaning",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,[value])!;
}
