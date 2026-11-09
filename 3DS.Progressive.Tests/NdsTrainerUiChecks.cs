using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services.Nds;
using pk3DS.Core.NDS;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;

internal static class NdsTrainerUiChecks
{
    internal static void Run(string source, Action<bool,string> check)
    {
        using var project=NdsProject.Open(source,true);
        var editor=new NdsRecordEditor(project,NdsSection.Trainers);
        var view=new RecordEditorView(editor);
        var list=(ListBox)view.FindName("RecordList");
        var cards=(WrapPanel)view.FindName("TrainerTeamCards");
        var controls=view.FieldControls;
        string[] Texts()=>Descendants<TextBlock>(cards).Select(t=>t.Text).ToArray();
        check(Enumerable.Range(1,4).All(i=>Texts().Contains($"{i}. Mov{i}"))&&Texts().Contains("Objeto explícito: Objeto50")&&Texts().Contains("Forma 1 · imagen de especie"),"NdsTrainerUi_CardsShowFourExplicitMovesItemAndForm");
        view.SelectTrainerSlot(1);
        check(view.SelectedTrainerSlot==1&&Texts().Contains("4. Mov8")&&Texts().Any(t=>t.StartsWith("ROM de origen:",StringComparison.Ordinal)&&t.Contains("Especie11",StringComparison.Ordinal)),"NdsTrainerUi_SelectingMemberUpdatesComparison");
        Capture(view,"nds-trainer-explicit",1100,760);
        list.SelectedItem=editor.Entries.Single(e=>e.Id==2);
        controls=view.FieldControls;
        check(!controls["P0.Move0"].IsEnabled&&!controls["P0.Item"].IsEnabled&&Texts().Contains("1. Mov33")&&Texts().Contains("2. Mov45")&&Texts().Contains("3. —")&&Texts().Contains("4. —")
            &&Texts().Any(t=>t.Contains("estimación",StringComparison.Ordinal))&&project.History.Count==0,"NdsTrainerUi_DefaultMovesAreDerivedReadOnlyAndDoNotWrite");
        var defaults=editor.RandomOptions.SelectMany(g=>g.Fields).ToDictionary(f=>f.Key,f=>f.Value);
        defaults["CHK_RandomPKM"]=0;defaults["CHK_RandomItems"]=0;defaults["CB_Moves"]=3;
        var defaultPreview=editor.Preview(defaults,[2]);
        check(defaultPreview.Changes.Any(c=>c.Before=="Por nivel (no almacenado)"&&c.After.Contains("Mov118",StringComparison.Ordinal))&&project.History.Count==0,"NdsTrainerUi_DefaultMovePreviewDoesNotPretendTheRomStoresZero");
        ((TextBox)controls["P0.Level"]).Text="1"; view.RefreshCards();
        check(Texts().Contains("2. —")&&project.History.Count==0,"NdsTrainerUi_DerivedMovesFollowUnsavedLevel");
        ((CheckBox)controls["HasMoves"]).IsChecked=true;
        ((CheckBox)controls["HasItems"]).IsChecked=true; view.RefreshCards();
        check(controls["P0.Move0"].IsEnabled&&controls["P0.Item"].IsEnabled&&((ComboBox)controls["P0.Move0"]).SelectedValue is 33,"NdsTrainerUi_ExplicitConversionEnablesFieldsAndCopiesDerivedMoves");
        ((ComboBox)controls["P0.Move3"]).SelectedValue=55;
        ((ComboBox)controls["P0.Item"]).SelectedValue=234;
        view.RefreshCards();
        check(Texts().Contains("4. Mov55")&&Texts().Contains("Objeto explícito: Objeto234")&&Texts().Any(t=>t.StartsWith("Proyecto guardado:",StringComparison.Ordinal)&&t.Contains("por nivel",StringComparison.Ordinal)),"NdsTrainerUi_UnsavedExplicitValuesStayDistinctFromSavedState");
        ((Button)view.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        check(project.History.Count==1&&project.Trainers().Single(t=>t.Id==2).Team[0].Moves[3]==55&&Texts().Any(t=>t.StartsWith("Proyecto guardado:",StringComparison.Ordinal)&&t.Contains("Mov55",StringComparison.Ordinal)),"NdsTrainerUi_SaveRefreshesCardsAndComparison");
        // A species change resets only its form, not the other members.
        list.SelectedItem=editor.Entries.Single(e=>e.Id==1); controls=view.FieldControls;
        ((ComboBox)controls["P0.Species"]).SelectedValue=25; view.RefreshCards();
        check(((TextBox)controls["P0.Form"]).Text=="0"&&Texts().Any(t=>t.StartsWith("Edición actual:",StringComparison.Ordinal)&&t.Contains("Especie25",StringComparison.Ordinal)),"NdsTrainerUi_SpeciesChangeResetsFormInLiveComparison");
        Descendants<Button>(view).Single(b=>b.Content is string text&&text=="Descartar cambios").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        ((TextBox)view.FindName("SearchText")).Text="Ana";
        check(list.Items.Count==1&&list.Items[0] is RecordEntry {Id:1}&&view.TrainerSubtitle(1).Contains("2 Pokémon",StringComparison.Ordinal),"NdsTrainerUi_SearchAndTrainerSubtitleUseCurrentProject");
        Capture(view,"nds-trainer-compact",760,620);
        ((TextBox)view.FindName("SearchText")).Text="";
        list.SelectedItem=editor.Entries.Single(e=>e.Id==2);
        ((Button)view.FindName("RandomizerButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        ((ComboBox)view.FindName("ScopeBox")).SelectedIndex=2;
        ((CheckBox)view.OptionControls["CHK_RandomPKM"]).IsChecked=false;
        ((CheckBox)view.OptionControls["CHK_RandomItems"]).IsChecked=false;
        ((ComboBox)view.OptionControls["CB_Moves"]).SelectedValue=3;
        ((Button)view.FindName("PreviewButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var changes=((ListBox)view.FindName("ChangeList")).Items.Cast<BatchChange>().ToArray();
        check(changes.Any(c=>c.After.Contains("Mov118",StringComparison.Ordinal)&&c.Before.Contains("Mov33",StringComparison.Ordinal))&&project.Trainers().Single(t=>t.Id==2).Team[0].Moves[0]==33,"NdsTrainerUi_RandomPreviewComparesSavedMovesWithNamedResult");
        ((Button)view.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Descendants<Button>(view).Single(b=>b.Content is string text&&text=="← Volver al editor").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        check(Texts().Contains("1. Mov118")&&Texts().Any(t=>t.StartsWith("ROM de origen:",StringComparison.Ordinal)&&t.Contains("por nivel",StringComparison.Ordinal))
            &&Texts().Any(t=>t.StartsWith("Proyecto guardado:",StringComparison.Ordinal)&&t.Contains("Mov118",StringComparison.Ordinal)),"NdsTrainerUi_AppliedRandomizationRefreshesCardsAndKeepsOriginalComparison");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        foreach(object child in LogicalTreeHelper.GetChildren(root)) if(child is DependencyObject d)
        { if(d is T found)yield return found; foreach(var item in Descendants<T>(d))yield return item; }
    }
    private static void Capture(FrameworkElement view,string name,int width,int height)
    {
        if(Environment.GetEnvironmentVariable("PROGRESSIVE_UI_CAPTURE_DIR") is not {Length:>0} folder)return;
        view.Measure(new System.Windows.Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
        var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(view);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory(folder);
        using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);
    }
}
