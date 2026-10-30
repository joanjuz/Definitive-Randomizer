using System.IO;
using System.Text.Json;
using ThreeDS.Progressive.Services;

internal static class HistoryInspectionChecks
{
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root)where T:System.Windows.DependencyObject
    {
        for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);if(child is T found)yield return found;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"history-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string file=Path.Combine(root,"data.bin");File.WriteAllBytes(file,[0]);var history=new ProjectHistory(root,"USUM",Path.Combine(root,"metadata"));
            history.Commit([new FileChange(file,[0],[1])],"Test","Randomizar historial",[],new Dictionary<string,string>{{"Scope","All"}});
            var view=new ThreeDS.Progressive.HistoryView(history);
            var parameters=new System.Windows.Interop.HwndSourceParameters("history offscreen checks")
                {WindowStyle=unchecked((int)0x80000000),Width=900,Height=650,PositionX=-32000,PositionY=-32000};
            using var source=new System.Windows.Interop.HwndSource(parameters){RootVisual=view};
            void Pump()=>System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
            void Complete()
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();
                do{Pump();}while(ThreeDS.Progressive.UiProgress.IsInteractionBlocked&&watch.Elapsed<TimeSpan.FromSeconds(10));
                if(ThreeDS.Progressive.UiProgress.IsInteractionBlocked)throw new TimeoutException("History UI verification did not finish.");
                view.Measure(new System.Windows.Size(900,650));view.Arrange(new System.Windows.Rect(0,0,900,650));view.UpdateLayout();
            }
            Complete();
            check(view.IsLoaded&&Descendants<System.Windows.Controls.TextBlock>(view).Any(text=>text.Text.Contains("1 registros coherentes")),"HistoryInspectionUi_LoadedChecksActualFiles");
            File.WriteAllBytes(file,[0]);
            var verify=Descendants<System.Windows.Controls.Button>(view).Single(button=>button.Content?.ToString()=="Comprobar archivos actuales");
            verify.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Complete();
            check(Descendants<System.Windows.Controls.TextBlock>(view).Any(text=>text.Text.Contains("0 registros coherentes"))&&Descendants<System.Windows.Controls.TextBlock>(view).Any(text=>text.Text.Contains("Desfasado: no acredita")),"HistoryInspectionUi_RecheckShowsResetWithoutDeletingLog");
            File.WriteAllBytes(file,[1]);verify.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Complete();
            string log=Directory.GetFiles(Path.Combine(root,"metadata","history"),"*.json").Single();byte[] saved=File.ReadAllBytes(log);
            try
            {
                File.WriteAllText(log,"{invalid");verify.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Complete();
                check(Descendants<System.Windows.Controls.TextBlock>(view).Any(text=>text.Text.StartsWith("No se pudo comprobar"))&&!Descendants<System.Windows.Controls.TextBlock>(view).Any(text=>text.Text.Contains("Cadena coherente")),"HistoryInspectionUi_FailureClearsPreviousVerifiedLabels");
            }
            finally{File.WriteAllBytes(log,saved);}
            var gate=typeof(ProjectHistory).GetField("_gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(history)!;
            using var locked=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            var holding=Task.Run(()=>{lock(gate){locked.Set();release.Wait(TimeSpan.FromSeconds(10));}});
            if(!locked.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("History gate probe did not start.");
            try
            {
                verify.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                source.RootVisual=null;Pump();
            }
            finally{release.Set();holding.GetAwaiter().GetResult();}
            Complete();
            check(!view.IsLoaded&&!ThreeDS.Progressive.UiProgress.IsInteractionBlocked,"HistoryInspectionUi_UnloadCancelsAndUnlocks");
        }
        finally{Directory.Delete(root,true);}
    }
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"history-inspection-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string data=Path.Combine(root,"data.bin"),storage=Path.Combine(root,"metadata");
            File.WriteAllBytes(data,[0]);var history=new ProjectHistory(root,"USUM",storage);
            check(history.InspectCurrentState().Document.Entries.Count==0&&!Directory.Exists(storage),"HistoryInspection_EmptyDoesNotCreateMetadata");
            void Commit(byte before,byte after,string action)=>history.Commit([new FileChange(data,[before],[after])],"Test",action,[],new Dictionary<string,string>{{"Scope","All"}});
            Commit(0,1,"Randomizar primero");Commit(1,2,"Randomizar segundo");
            var inspection=history.InspectCurrentState();
            check(inspection.Files.Count==1&&inspection.Files[0].State==HistoryFileState.Matches&&inspection.Entries.Values.All(value=>value==HistoryEntryState.Verified),"HistoryInspection_ChecksTerminalHashNotIntermediateEdits");
            string log=Directory.GetFiles(Path.Combine(storage,"history"),"*.json").Single();byte[] logBefore=File.ReadAllBytes(log);
            DateTime timestamp=File.GetLastWriteTimeUtc(data);File.WriteAllBytes(data,[0]);File.SetLastWriteTimeUtc(data,timestamp);
            var reopened=new ProjectHistory(root,"USUM",storage);inspection=reopened.InspectCurrentState();
            check(inspection.Files.Single().State==HistoryFileState.Changed&&inspection.Entries.Values.All(value=>value==HistoryEntryState.OutOfDate),"HistoryInspection_ReopenedCleanRomWithSameSizeAndDateIsOutOfDate");
            check(File.ReadAllBytes(log).SequenceEqual(logBefore)&&File.ReadAllBytes(data).SequenceEqual(new byte[]{0}),"HistoryInspection_NeverClearsHistoryOrChangesRom");
            Commit(0,3,"Randomizar tras limpiar");inspection=history.InspectCurrentState();
            check(inspection.Entries.Values.Count(value=>value==HistoryEntryState.OutOfDate)==2&&inspection.Entries.Values.Count(value=>value==HistoryEntryState.Verified)==1,"HistoryInspection_NewEditAfterResetDoesNotValidateOldSegment");
            File.Delete(data);inspection=history.InspectCurrentState();
            check(inspection.Files.Single().State==HistoryFileState.Missing&&inspection.Entries.Values.All(value=>value==HistoryEntryState.OutOfDate),"HistoryInspection_MissingFileIsNotApplied");
            File.WriteAllBytes(data,[3]);
            using(var locked=new FileStream(data,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                check(history.InspectCurrentState().Files.Single().State==HistoryFileState.Unreadable,"HistoryInspection_LockedFileIsNotReportedVerified");
            var document=history.Read();var last=document.Entries.Last();
            void Store(HistoryDocument value)=>File.WriteAllBytes(log,JsonSerializer.SerializeToUtf8Bytes(value));
            Store(document with {Entries=[last with {Id=Guid.NewGuid(),Files=[]}]});
            check(history.InspectCurrentState().Entries.Values.Single()==HistoryEntryState.Unverified,"HistoryInspection_NoFileRecordDoesNotProveRomModification");
            Store(document with {Game="XY"});
            check(!history.InspectCurrentState().GameMatches&&history.InspectCurrentState().Entries.Values.All(value=>value==HistoryEntryState.OutOfDate),"HistoryInspection_WrongGameIsNotVerified");
            foreach(string path in new[]{"../outside.bin",data})
            {
                Store(document with {Entries=[last with {Files=[last.Files[0] with {Path=path}]}]});inspection=history.InspectCurrentState();
                check(inspection.Entries.Values.Single()==HistoryEntryState.OutOfDate&&inspection.Files.Count==0,"HistoryInspection_UnsafePathRejected_"+(Path.IsPathRooted(path)?"Absolute":"Parent"));
            }
            Store(document with {Entries=[last with {Files=[last.Files[0] with {AfterSha256="invalid"}]}]});
            check(history.InspectCurrentState().Entries.Values.Single()==HistoryEntryState.OutOfDate,"HistoryInspection_InvalidHashIsNotVerified");
            Store(document with {Version=1});
            check(history.InspectCurrentState().Entries.Values.Last()==HistoryEntryState.Verified,"HistoryInspection_LegacyJournalRemainsReadable");
            Store(document);using var cancellation=new CancellationTokenSource();cancellation.Cancel();bool canceled=false;
            try{history.InspectCurrentState(cancellation.Token);}catch(OperationCanceledException){canceled=true;}
            check(canceled,"HistoryInspection_CancellationIsObserved");
            Store(document);string second=Path.Combine(root,"second.bin");File.WriteAllBytes(second,[4]);
            history.Commit([new FileChange(data,[3],[5]),new FileChange(second,[4],[6])],"Test","Randomizar dos archivos",[],new Dictionary<string,string>{{"Scope","All"}});
            File.WriteAllBytes(second,[4]);var partial=history.InspectCurrentState();
            check(partial.Entries.Values.Last()==HistoryEntryState.OutOfDate&&partial.Files.Count(file=>file.State==HistoryFileState.Matches)==1,"HistoryInspection_PartialResetInvalidatesWholeAction");
            check(File.ReadAllBytes(data).SequenceEqual(new byte[]{5})&&File.ReadAllBytes(second).SequenceEqual(new byte[]{4}),"HistoryInspection_PartialResetCheckNeverRestoresFiles");
            Store(document);File.WriteAllBytes(data,[0]);inspection=history.InspectCurrentState();
            string summary=ThreeDS.Progressive.RomRebuildView.HistorySummary(inspection);
            check(summary.Contains("0 registros coherentes")&&summary.Contains("Desfasado")&&!summary.Contains("Registros cuya cadena de archivos coincide"),"HistoryInspection_RebuildDoesNotListStaleActionsAsApplied");
            check(summary.Contains("SHA-256")&&summary.Contains("se conserva"),"HistoryInspection_ExplainsContentCheckAndPreservedJournal");
        }
        finally{Directory.Delete(root,true);}
    }
}
