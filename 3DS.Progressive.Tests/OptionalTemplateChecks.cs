using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

/// <summary>
/// The Eris-Ultranova balance is an optional package: no default, generic randomizer, new recipe or
/// Template Studio shortcut may apply it (or its battle mechanics) without an explicit choice.
/// Recipes recorded before the change keep replaying it. Synthetic data only; no game is played.
/// </summary>
internal static class OptionalTemplateChecks
{
    private static string Bundled(string file)=>Path.Combine(AppContext.BaseDirectory,"custom_balance_templates",file);
    private static string Eris(string file)=>Path.Combine(OptionalTemplates.ErisFolder,file);

    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        Package(check);
        Assets(check);
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})Project(check,reject,version);
    }

    private static void Package(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.USUM);string[] moves=f.Session.MoveNames.ToArray();
        string[] entries=Enumerable.Range(0,f.Session.EntryCount).Select(i=>f.Session.Entries.FirstOrDefault(e=>e.Index==i)?.Name??"—").ToArray();
        foreach(int gen in new[]{6,7})
        {
            check(File.Exists(Bundled($"moves_gen{gen}.csv"))&&MoveTemplates.LoadMovePatches(Bundled($"moves_gen{gen}.csv"),moves).Length==0,$"Optional_BundledMoveTemplateGen{gen}HasNoActiveRows");
            check(File.Exists(Bundled($"pokemon_stats_gen{gen}.csv"))&&PokemonStatTemplates.Load(Bundled($"pokemon_stats_gen{gen}.csv"),entries).Length==0,$"Optional_BundledStatsTemplateGen{gen}HasNoActiveRows");
        }
        var eris7=MoveTemplates.LoadMovePatches(Eris("moves_gen7.csv"),moves);
        check(eris7.Length==57&&eris7.Any(r=>r.Move==587&&r.BattlePatch=="Gen7FairyLockAllFairyV11"),"Optional_ErisGen7PackageKeepsTheFormerBalanceIncludingFairyLock");
        check(PokemonStatTemplates.Load(Eris("pokemon_stats_gen7.csv"),entries).Length==1,"Optional_ErisStatsPackageKeepsTheSlakingRow");
        check(File.Exists(Eris("LEEME.md"))&&File.ReadAllText(Eris("LEEME.md")).Contains("Gen7FairyLockAllFairyV11")&&File.ReadAllText(Eris("LEEME.md")).Contains("nunca lo aplica por defecto"),"Optional_ErisPackageDocumentsEveryMechanic");
        // The Gen6 file is the exact former built-in balance, now importable by choice.
        using var xy=new Fixture(GameVersion.XY);
        var csv=MoveTemplates.LoadMovePatches(Eris("moves_gen6.csv"),xy.Session.MoveNames.ToArray());var built=BuiltInMoveBalance.ForGeneration(6);
        var compared=typeof(MoveTemplates.MovePatchRow).GetProperties().Where(p=>p.Name is not ("Notes" or "InGameDescription")).ToArray();
        check(csv.Length==built.Length&&csv.Zip(built).All(pair=>compared.All(p=>Equals(p.GetValue(pair.First),p.GetValue(pair.Second)))),"Optional_ErisGen6CsvEqualsTheFormerBuiltInRows");
    }

    private static void Assets(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"dr-optional-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,"moves_gen7.csv"),"neutral");
            string eris=OptionalTemplates.Sha256(Eris("moves_gen7.csv")),other=OptionalTemplates.Sha256(Path.Combine(root,"moves_gen7.csv"));
            check(OptionalTemplates.FindAsset([root],"moves_gen7.csv",eris)==Eris("moves_gen7.csv"),"Optional_OldRecipeAssetResolvesToThePackageOnlyByExactSha256");
            check(OptionalTemplates.FindAsset([root],"moves_gen7.csv",other)==Path.Combine(root,"moves_gen7.csv"),"Optional_MatchingLocalAssetWins");
            check(OptionalTemplates.FindAsset([root],"moves_gen7.csv","00")==Path.Combine(root,"moves_gen7.csv"),"Optional_PackageNeverReplacesADifferentAsset");
            check(OptionalTemplates.FindAsset([Path.Combine(root,"missing")],"moves_gen7.csv","00") is null,"Optional_PackageIsNotAFallbackForMissingAssets");
        }
        finally{Directory.Delete(root,true);}
    }

    private static void Project(Action<bool,string> check,Action<Action,string> reject,GameVersion version)
    {
        using var f=new Fixture(version);var p=f.Session;
        foreach(string kind in new[]{"Moves","Stats"})
        {
            check(!AuthoredTemplate.CanImportProgressive(p,kind),$"{version} Template Studio offers no built-in {kind} balance");
            reject(()=>AuthoredTemplate.Progressive(p,kind),$"{version} {kind} has no built-in values to import");
        }
        check(AuthoredTemplate.Create(p,"Moves") is var none&&TemplateStudioView.BattleMechanicsSummary(none) is null,$"{version} a template without battle patches asks nothing");
        var fairy=AuthoredTemplate.Create(p,"Moves");fairy.Rows.Add(new(){["Move"]="587",["BattlePatch"]="Gen7FairyLockAllFairyV11"});fairy.Rows.Add(new(){["Move"]="15",["BattlePatch"]="None"});
        check(TemplateStudioView.BattleMechanicsSummary(fairy) is {} listed&&listed.Contains("587")&&!listed.Contains("15:"),$"{version} Template Studio lists each battle mechanic before applying");
        // Generic randomizers never touch battle code nor read a balance file.
        var moves=new GameMoveService(p);int[] all=moves.Entries.Select(m=>m.Id).ToArray();
        foreach(var action in new[]{BatchAction.MoveRandomize,BatchAction.Metronome})
        {
            var preview=moves.PreviewMoves(action,new BatchSettings{Seed=7,MoveTypes=true,MoveCategories=true},all);
            check(!preview.AdditionalFiles.Any(c=>c.Path.EndsWith(".cro",StringComparison.OrdinalIgnoreCase))&&!preview.Dependencies.Any(d=>d.Path.Contains("plantillas-opcionales")||d.Path.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)),$"{version} {action} applies no balance and no battle mechanic");
        }
        var personal=p.PreviewPersonal(BatchAction.PersonalRandomize,new BatchSettings{Seed=7,Stats=true,Types=true,Abilities=true,Tms=true},p.Entries.Select(e=>e.Index).ToArray());
        check(personal.AdditionalFiles.Count==0&&!personal.Dependencies.Any(d=>d.Path.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)),$"{version} Pokémon randomizer applies no stats template and no battle mechanic");
        if(version is GameVersion.USUM)
        {
            var dataOnly=moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{TemplatePath=Eris("moves_gen7.csv"),BattlePatches=false,MoveDescriptions=false},all);
            check(dataOnly.ChangedCount>0&&!dataOnly.AdditionalFiles.Any(c=>c.Path.EndsWith(".cro",StringComparison.OrdinalIgnoreCase)),$"{version} chosen Eris CSV without the mechanics box changes data only");
            bool cro=File.Exists(Path.Combine(f.Game.RomFS,"Battle.cro"));
            if(cro)check(moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{TemplatePath=Eris("moves_gen7.csv"),BattlePatches=true,MoveDescriptions=false},all).AdditionalFiles.Any(c=>c.Path.EndsWith("Battle.cro",StringComparison.OrdinalIgnoreCase)),$"{version} mechanics install only when ticked");
            else reject(()=>moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{TemplatePath=Eris("moves_gen7.csv"),BattlePatches=true,MoveDescriptions=false},all),$"{version} mechanics are only processed when ticked");
        }
    }

    // STA thread with application resources.
    internal static void Ui(Action<bool,string> check)
    {
        int generated=0;
        var view=new BatchToolsView();
        view.Configure(true,7,false,true,false,()=>true,_=>[1],(_,_,_)=>{generated++;throw new InvalidOperationException("not expected");},_=>{},_=>"",defaultBattlePatches:true,supportsBalance:true);
        var window=new Window{Content=view,Width=1100,Height=800,ShowInTaskbar=false,WindowStyle=WindowStyle.None,Left=-20000,Top=-20000};window.Show();
        try
        {
            check(view.TemplateName.Text==BatchToolsView.NoTemplateText,"OptionalUi_BalanceStartsWithoutAnyTemplate");
            check(!Descendants<Button>(view).Any(b=>Text(b).Contains("Progressive")),"OptionalUi_NoButtonLoadsTheFormerBuiltInBalance");
            view.OpenAction(BatchAction.MoveBalance);window.UpdateLayout();
            var boxes=Descendants<CheckBox>(view).ToArray();
            var mechanics=boxes.Single(b=>Text(b).StartsWith("Aplicar también las mecánicas de combate"));
            check(mechanics.IsChecked==false&&boxes.All(b=>b.IsChecked!=true),"OptionalUi_BattleMechanicsAreUncheckedByDefault");
            check(view.ActionHelp.Text.Contains("no hay balance por defecto")&&view.ActionHelp.Text.Contains("Battle.cro"),"OptionalUi_HelpExplainsMechanicsNeedAnExplicitChoice");
            typeof(BatchToolsView).GetMethod("Preview_Click",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(view,[null,new RoutedEventArgs()]);
            check(generated==0&&view.ValidationText.Text.Contains("Carga el CSV"),"OptionalUi_PreviewWithoutACsvAsksForOne");
            check(view.CaptureGlobalState() is null,"OptionalUi_RecipeNeverRecordsAnImplicitBalance");
            view.RestoreGlobalState(new MasterBatchState(BatchAction.MoveBalance,new BatchSettings{BattlePatches=true},0));
            check(view.TemplateName.Text==BatchToolsView.LegacyTemplateText&&view.CaptureGlobalState() is {Settings.TemplatePath:null},"OptionalUi_OldRecipeShowsAndKeepsItsRecordedBalance");
            view.Configure(false,7,false,true,false,()=>true,_=>[1],(_,_,_)=>{generated++;throw new InvalidOperationException("not expected");},_=>{},_=>"");
            view.OpenAction(BatchAction.PersonalModify);window.UpdateLayout();
            var stats=Descendants<CheckBox>(view).Single(b=>Text(b)=="Aplicar estadísticas desde un CSV que elijas");
            check(stats.IsChecked==false,"OptionalUi_StatsTemplateIsOff");
            stats.IsChecked=true;
            typeof(BatchToolsView).GetMethod("Preview_Click",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(view,[null,new RoutedEventArgs()]);
            check(generated==0&&view.ValidationText.Text.Contains("CSV de estadísticas"),"OptionalUi_StatsTemplateWithoutACsvAsksForOne");
        }
        finally{window.Close();}
    }

    private static string Text(ContentControl c)=>c.Content as string??(c.Content as TextBlock)?.Text??"";
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        var seen=new HashSet<DependencyObject>();var stack=new Stack<DependencyObject>();stack.Push(root);
        while(stack.Count>0)
        {
            var node=stack.Pop();if(!seen.Add(node))continue;
            if(node is T match)yield return match;
            if(node is Visual)for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)stack.Push(VisualTreeHelper.GetChild(node,i));
            foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())stack.Push(child);
        }
    }
}
