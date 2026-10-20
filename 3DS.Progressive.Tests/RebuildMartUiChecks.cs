using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using MenuItem=System.Windows.Controls.MenuItem;
using Panel=System.Windows.Controls.Panel;

internal static class RebuildMartUiChecks
{
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    internal static void Run(Action<bool,string> check)
    {
        var main=new MainWindow();
        var rebuild=(Button)main.FindName("RebuildButton");var backup=(Button)main.FindName("BackupButton");
        var actions=(Panel)backup.Parent;
        check(ReferenceEquals(rebuild.Parent,actions)&&actions.Children.IndexOf(rebuild)==actions.Children.IndexOf(backup)+1,"Rebuild_HeaderAdjacentToBackup");
        check(!rebuild.IsEnabled,"Rebuild_DisabledWithoutProject");
        var empty=new HistoryDocument(2,"USUM","Fixture",[]);
        check(RomRebuildView.HistorySummary(empty).Contains("No hay cambios registrados"),"Rebuild_EmptyHistoryExplainsCurrentFiles");
        var entry=new HistoryEntry(Guid.NewGuid(),DateTimeOffset.UtcNow,"Pokémon","Editar PS",[new(1,"Bulbasaur","PS","45","70")],new Dictionary<string,string>(),[new("a/0/1/7","before","after")],ChangedValueCount:1);
        var history=empty with {Entries=[entry,entry with {Action="Editar PS de nuevo",Changes=[new(1,"Bulbasaur","PS","70","80")]}]};
        string summary=RomRebuildView.HistorySummary(history);
        check(summary.Contains("45 → 70")&&summary.Contains("70 → 80")&&summary.IndexOf("Editar PS de nuevo",StringComparison.Ordinal)>summary.IndexOf("Editar PS",StringComparison.Ordinal)&&summary.Contains("1 archivos registrados"),"Rebuild_HistoryPreservesOrderDetailsAndDistinctFiles");
        check(summary.Contains("estado guardado actual")&&summary.Contains("no las opciones pendientes"),"Rebuild_HistoryDistinguishesSavedStateFromOptions");
        main.Close();
    }
    internal static void CheckMart(GameProjectSession project,string path,string itemPath,Action<bool,string> check)
    {
        byte[] source=File.ReadAllBytes(path),items=File.ReadAllBytes(itemPath);
        var main=new MainWindow();main.LoadProject(project);
        check(((Button)main.FindName("RebuildButton")).IsEnabled,"Rebuild_EnabledWithProject");
        ((Button)main.FindName("RebuildButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(((ContentControl)main.FindName("SectionHost")).Content is RomRebuildView,"Rebuild_HeaderUsesExistingNavigation");main.Close();
        var view=new MartEditorView(project);
        var buttons=Field<Button[]>(view,"_recipeFlags");
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var first=Field<MartShopPreview>(view,"_pending");
        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var both=Field<MartShopPreview>(view,"_pending");
        check(view.RememberRecipe() is {RandomRegular:true,RandomBP:true},"Marts_ButtonsAccumulateRegularAndBp");
        check(first.Changes.Where(c=>!c.Field.StartsWith("BP",StringComparison.Ordinal)).All(c=>both.Changes.Contains(c)),"Marts_AddingBpPreservesPreviousRegularPreview");
        buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var combined=Field<MartShopPreview>(view,"_pending");
        var expected=NativeGen7MartBridge.Prepare(new GameMartService(project),view.RememberRecipe(),Field<int>(view,"_previewSeed"));
        check(combined.Output.SequenceEqual(expected.Output)&&combined.ItemOutput!.SequenceEqual(expected.ItemOutput!),"Marts_CumulativePreviewMatchesNativeBridge");
        buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(Field<MartShopPreview>(view,"_pending").Output.SequenceEqual(combined.Output),"Marts_RepeatedActionDoesNotToggleOrReroll");
        ((MenuItem)buttons[1].ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(view.RememberRecipe() is {RandomRegular:true,RandomBP:false,Candies:true},"Marts_RemoveActionPreservesOtherSelections");
        ((Button)((Panel)buttons[0].Parent).Children.OfType<Button>().Single(b=>b.Content?.ToString()=="Restablecer opciones")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(view.RememberRecipe()==new Gen7MartState()&&Field<MartShopPreview>(view,"_pending").Changes.Count==0,"Marts_ResetOptionsClearsPreviewWithoutWriting");
        var snapshot=new Gen7MartState(FreeMega:true,SpecialsOnly:true,ProtectX:false);view.RestoreRecipe(snapshot);
        check(view.RememberRecipe()==snapshot,"Marts_RestoreRecipePreservesGlobalBatchOptions");
        check(File.ReadAllBytes(path).SequenceEqual(source)&&File.ReadAllBytes(itemPath).SequenceEqual(items)&&project.History.Read().Entries.Count==0,"Marts_ButtonPreviewsNeverWriteRomOrHistory");
        var savedView=new MartEditorView(project);var savedButtons=Field<Button[]>(savedView,"_recipeFlags");
        savedButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));savedButtons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var savedPreview=Field<MartShopPreview>(savedView,"_pending");
        Field<Button>(savedView,"_apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(File.ReadAllBytes(path).SequenceEqual(savedPreview.Output)&&File.ReadAllBytes(itemPath).SequenceEqual(savedPreview.ItemOutput!),"Marts_ApplyWritesExactCumulativePreview");
        var saved=project.History.Read().Entries.Last();
        check(saved.Parameters["Acciones"].Contains("marts.randomize")&&saved.Parameters["Acciones"].Contains("marts.add-rare-candies"),"Marts_ApplyRecordsBothActionsForGlobalBatch");
        File.WriteAllBytes(path,source);File.WriteAllBytes(itemPath,items);

    }
}
