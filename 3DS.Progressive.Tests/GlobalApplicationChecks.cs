using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using pk3DS.Core;
using pk3DS.Core.Structures;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;
using Button=System.Windows.Controls.Button;
internal static class GlobalApplicationChecks
{
    internal static void PrepareOpenable(Fixture f)
    {
        File.WriteAllBytes(Path.Combine(f.Game.RomFS,"preview-fixture.bin"),new byte[32]);
        byte[] text=TextFile.GetBytes(f.Game,Enumerable.Range(0,1000).Select(index=>"Entry"+index).ToArray());
        f.WriteGarc(ModuleChecks.PathFor(f,"gametext"),Enumerable.Range(0,f.Game.GameText.Max(row=>row.Index)+1).Select(_=>text).ToArray(),f.Game.GetGARCReference("gametext"));
        foreach(string name in new[]{"gametext","storytext"})
        {
            byte[] bytes=File.ReadAllBytes(ModuleChecks.PathFor(f,name));var reference=f.Game.GetGARCReference(name);
            for(int language=0;language<(f.Game.Generation==6?8:10);language++)
            {string path=Path.Combine(f.Game.RomFS,reference.GetRelativeGARC(language,name).Reference);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);}
        }
        int desired=f.Game.XY?271:f.Game.ORAS?299:f.Game.SM?311:333;
        int count=Directory.EnumerateFiles(Path.Combine(f.Game.RomFS,"a"),"*",SearchOption.AllDirectories).Count(path=>Path.GetFileName(path).Length==1);
        for(int i=0;i<desired-count;i++){string folder=Path.Combine(f.Game.RomFS,"a","padding",i.ToString("000"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"0"),[0]);}
        byte[] code=new byte[f.Game.Generation==6?0x401000:1024];
        if(f.Game.Generation==6){GameMachine6Service.Signature.CopyTo(code,0x400100);for(int i=0;i<107;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(code,0x400108+i*2);}
        File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"),code);File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"banner.bin"),new byte[512]);File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"header.bin"),new byte[512]);File.WriteAllBytes(Path.Combine(f.Root,"ExHeader.bin"),new byte[0x800]);
    }
    internal static async Task Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var state=new TestState();
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);PrepareOpenable(f);
            var doc=GlobalTemplateDocument.Empty(version.ToString(),f.Game.Generation);
            NativeMasterBatchBridge.Capture(doc,new MasterBatchState(BatchAction.PersonalRandomize,new(){Abilities=true},0),f.Game.Generation);
            NativeMasterBatchBridge.Capture(doc,new MasterBatchState(BatchAction.MoveRandomize,new(){MoveTypes=true},0),f.Game.Generation);
            doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="personal.randomize",["Parameters"]=new JsonObject()},new JsonObject{["Id"]="moves.randomize",["Parameters"]=new JsonObject()}));
            var application=new GlobalTemplateApplication(f.Session);var before=GlobalTemplateApplication.Snapshot(f.Session);
            using(var cancelled=await application.PreviewAsync(doc))
                check(cancelled.Files.Count>0&&GlobalTemplateApplication.Snapshot(f.Session).All(pair=>before[pair.Key]==pair.Value)&&f.Session.History.Read().Entries.Count==0&&!Directory.EnumerateFiles(cancelled.Workspace,"*.3ds",SearchOption.AllDirectories).Any(),"GlobalApply_PreviewRunsOriginalEngineWithoutSourceWritesOrRebuild_"+version);
            check(f.Session.History.Read().Entries.Count==0,"GlobalApply_DiscardPreviewLeavesHistoryUntouched_"+version);
            using var preview=await application.PreviewAsync(doc);
            var expected=preview.Files.ToDictionary(file=>file.RelativePath,file=>file.AfterSha256);
            application.Apply(preview);
            check(expected.All(pair=>ProjectHistory.Hash(File.ReadAllBytes(Path.Combine(f.Root,pair.Key)))==pair.Value)&&File.Exists(Path.Combine(f.Game.ExeFS,"code.bin"))&&!File.Exists(Path.Combine(f.Game.ExeFS,".code.bin")),"GlobalApply_AppliesFrozenBytesAndPreservesCodeFilename_"+version);
            var history=f.Session.History.Read();
            check(doc.Actions.All(id=>history.Entries.Count(entry=>entry.Parameters.GetValueOrDefault("ActionId")==id)==1)&&history.Entries.Count==doc.Actions.Count+1,"GlobalApply_EachActionRecordedOnceWithSharedTransaction_"+version);
            check(history.Entries.Any(entry=>entry.Parameters.GetValueOrDefault("GlobalTemplateSnapshot")==doc.ToJson()),"GlobalApply_HistoryRetainsCompleteRecipeOptions_"+version);
            check(f.Session.History.InspectCurrentState().Entries.Values.All(value=>value==HistoryEntryState.Verified),"GlobalApply_HistoryReferencesRealFinalHashes_"+version);
            reject(()=>application.Apply(preview),"GlobalApply_RejectsSecondApply_"+version);
            application=new GlobalTemplateApplication(GameProjectSession.Open(f.Root,recoverInterruptedSave:false));
            using(var stale=await application.PreviewAsync(doc))
            {
                string path=Path.Combine(f.Game.ExeFS,"banner.bin");byte[] original=File.ReadAllBytes(path);File.WriteAllBytes(path,[1,2,3]);
                reject(()=>application.Apply(stale),"GlobalApply_RejectsChangedDependency_"+version);File.WriteAllBytes(path,original);
            }
            if(version==GameVersion.USUM)
            {
                using(var historyChanged=await application.PreviewAsync(doc))
                {
                    var hashes=GlobalTemplateApplication.Snapshot(f.Session);
                    f.Session.History.Commit([],"Tests","External",[],new Dictionary<string,string>{{"RecordTemplateUse","true"}});
                    reject(()=>application.Apply(historyChanged),"GlobalApply_RejectsChangedHistory");
                    check(GlobalTemplateApplication.Snapshot(f.Session).All(pair=>hashes[pair.Key]==pair.Value),"GlobalApply_StaleHistoryRejectionPreservesGameFiles");
                }
                using(var failing=await application.PreviewAsync(doc))
                {
                    var hashes=GlobalTemplateApplication.Snapshot(f.Session);int historyCount=f.Session.History.Read().Entries.Count;
                    string locked=failing.Files[^1].SourcePath;var attributes=File.GetAttributes(locked);File.SetAttributes(locked,attributes|FileAttributes.ReadOnly);
                    try{bool blocked=false;try{application.Apply(failing);}catch(UnauthorizedAccessException){blocked=true;}check(blocked,"GlobalApply_ReadOnlyCommitRejected");}
                    finally{File.SetAttributes(locked,attributes);}
                    check(GlobalTemplateApplication.Snapshot(f.Session).All(pair=>hashes[pair.Key]==pair.Value)&&f.Session.History.Read().Entries.Count==historyCount,"GlobalApply_FailedCommitRollsBackFilesAndHistory");
                }
            }
            if(version==GameVersion.USUM)
            {
                string dependency=Path.Combine(f.Root,"moves_gen7.csv");File.WriteAllText(dependency,"local dependency fixture");
                var local=doc.Clone();local.SetSection("Assets",new JsonArray(new JsonObject{["Role"]="move-balance",["FileName"]="moves_gen7.csv",["Sha256"]=ProjectHistory.Hash(File.ReadAllBytes(dependency))}));
                string path=Path.Combine(f.Root,"local.json");local.Save(path);local=GlobalTemplateDocument.Load(path,"USUM",7);
                using var proposal=await application.PreviewAsync(local);
                check(File.ReadAllText(Path.Combine(proposal.Workspace,"custom_balance_templates","moves_gen7.csv"))=="local dependency fixture","GlobalApply_LocalVerifiedDependenciesOverrideBundledCopies");
            }
            using(var damaged=await application.PreviewAsync(doc))
            {
                if(damaged.Files.Count>0){File.WriteAllBytes(damaged.Files[0].ProposedPath,[7,8,9]);reject(()=>application.Apply(damaged),"GlobalApply_RejectsTamperedPreview_"+version);}
            }
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(f);var main=new MainWindow();main.LoadProject(f.Session);
        check(((ContentControl)main.FindName("HomeHost")).Visibility==Visibility.Visible&&!((Grid)main.FindName("EditorShell")).IsEnabled,"GlobalApply_OpeningProjectRequiresChoiceAtHome");main.Close();
        var view=new GlobalTemplateView(f.Session);var doc=GlobalTemplateDocument.Empty("USUM",7);NativeMasterBatchBridge.Capture(doc,new MasterBatchState(BatchAction.MoveRandomize,new(){MoveTypes=true},0),7);
        doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="moves.randomize",["Parameters"]=new JsonObject()}));
        string path=Path.Combine(f.Root,"preview.json");doc.Save(path);view.OpenPath(path);
        var fields=BindingFlags.Instance|BindingFlags.NonPublic;
        check(((Button)typeof(GlobalTemplateView).GetField("_previewButton",fields)!.GetValue(view)!).IsEnabled&&!((Button)typeof(GlobalTemplateView).GetField("_applyButton",fields)!.GetValue(view)!).IsEnabled&&view.ExecutionSnapshot().Actions.SequenceEqual(doc.Actions),"GlobalApply_LoadEnablesPreviewButRequiresPreviewBeforeApply");
        string historyHash=ProjectHistory.Hash(JsonSerializer.SerializeToUtf8Bytes(f.Session.History.Read()));
        f.Session.History.CommitGlobalTemplate([],doc,1,historyHash);
        var restored=new GlobalTemplateView(f.Session);typeof(GlobalTemplateView).GetMethod("FromHistory",fields)!.Invoke(restored,null);
        check(restored.LoadedTemplate!.Actions.SequenceEqual(doc.Actions)&&NativeMasterBatchBridge.Read(restored.LoadedTemplate!,"Moves")!.Settings.MoveTypes,"GlobalApply_FromHistoryRestoresAppliedRecipeWithoutInventingSettings");
        var systems=new SystemsView(f.Session,"Configuration",_=>{},()=>{},()=>{});
        IEnumerable<Button> Buttons(DependencyObject parent){for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);if(child is Button button)yield return button;foreach(var nested in Buttons(child))yield return nested;}}
        systems.Measure(new Size(900,700));systems.Arrange(new Rect(0,0,900,700));systems.UpdateLayout();
        var dialog=systems.CreateGarcDialog();
        string listing=string.Join(Environment.NewLine,f.Game.Files.Select(file=>file.Name+" → "+f.Game.GetGARCFileName(file.Name)));
        check(dialog.Title=="About GARCs"&&dialog.DetailsLabel.Text==listing&&dialog.DetailsLabel.Visibility==Visibility.Visible&&dialog.MaxHeight<=SystemParameters.WorkArea.Height&&dialog.CancelButton.Visibility==Visibility.Collapsed,"GlobalApply_AboutGarcsDialogContainsActualScrollableListing");dialog.Close();
        check(Buttons(systems).Any(button=>button.Content as string=="About GARCs")&&!Buttons(systems).Any(button=>button.Content as string=="About GARCs · Copiar listado"),"GlobalApply_AboutGarcsOpensListingInsteadOfCopyOnly");
    }
}
