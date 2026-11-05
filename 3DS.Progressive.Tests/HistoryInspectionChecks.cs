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
            int Subscribers()=>((Delegate?)typeof(ThreeDS.Progressive.UiProgress).GetField("InteractionLockChanged",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length??0;
            int beforeSubscribers=Subscribers();
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
            System.Windows.Controls.Button UndoButton()=>Descendants<System.Windows.Controls.Button>(view).Single(button=>button.Content?.ToString()=="Deshacer este cambio");
            var entry=history.Read().Entries.Single();
            check(RestorePoints.WhyNot(history.StorageDirectory,root,entry.Id) is null,"HistoryUndoUi_ControlHasValidRestorePoint");
            check(UndoButton().IsEnabled,"HistoryUndoUi_AutomaticVerificationEnablesValidUndo");
            check(Subscribers()==beforeSubscribers+1,"HistoryUndoUi_LoadSubscribesOnce");
            var verify=Descendants<System.Windows.Controls.Button>(view).Single(button=>button.Content?.ToString()=="Comprobar archivos actuales");
            var gate=typeof(ProjectHistory).GetField("_gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(history)!;
            using(var verificationLocked=new ManualResetEventSlim())using(var verificationRelease=new ManualResetEventSlim())
            using(var outer=ThreeDS.Progressive.UiProgress.BlockInteraction())
            {
                var verificationHolding=Task.Run(()=>{lock(gate){verificationLocked.Set();if(!verificationRelease.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException("History verification gate watchdog.");}});
                try
                {
                    if(!verificationLocked.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("History verification gate did not start.");
                    verify.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    check(ThreeDS.Progressive.UiProgress.IsInteractionBlocked&&!verify.IsEnabled&&!UndoButton().IsEnabled,"HistoryUndoUi_DisabledWhileVerificationIsActive");
                    UndoButton().RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    check(File.ReadAllBytes(file).SequenceEqual(new byte[]{1}),"HistoryUndoUi_BlockedClickDoesNotWrite");
                }
                finally{verificationRelease.Set();verificationHolding.GetAwaiter().GetResult();}
                var watch=System.Diagnostics.Stopwatch.StartNew();
                do{Pump();}while(!verify.IsEnabled&&watch.Elapsed<TimeSpan.FromSeconds(10));
                if(!verify.IsEnabled)throw new TimeoutException("Nested history verification did not finish.");
                check(ThreeDS.Progressive.UiProgress.IsInteractionBlocked&&!UndoButton().IsEnabled,"HistoryUndoUi_InnerCompletionKeepsOuterOperationDisabled");
            }
            Complete();check(UndoButton().IsEnabled,"HistoryUndoUi_LastUnlockEnablesValidUndo");
            using(ThreeDS.Progressive.UiProgress.BlockInteraction())File.WriteAllBytes(file,[0]);
            Complete();check(!UndoButton().IsEnabled,"HistoryUndoUi_UnlockRechecksExternalChanges");
            File.WriteAllBytes(file,[1]);verify.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Complete();
            check(UndoButton().IsEnabled,"HistoryUndoUi_ManualVerificationEnablesValidUndo");
            var retainedUndo=UndoButton();
            var technical=Descendants<System.Windows.Controls.Expander>(view).Single(expander=>expander.Header?.ToString()=="Detalles técnicos");
            technical.IsExpanded=true;
            using(new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                using(ThreeDS.Progressive.UiProgress.BlockInteraction()){}
                Pump();check(!UndoButton().IsEnabled,"HistoryUndoUi_UnreadableTargetStaysDisabledOnUnlock");
            }
            using(ThreeDS.Progressive.UiProgress.BlockInteraction()){}
            Complete();check(UndoButton().IsEnabled,"HistoryUndoUi_ReadableTargetCanEnableAgain");
            check(ReferenceEquals(UndoButton(),retainedUndo)&&technical.IsExpanded,"HistoryUndoUi_UnlockPreservesControlsAndExpandedDetails");
            var workerLock=Task.Run(()=>ThreeDS.Progressive.UiProgress.BlockInteraction()).GetAwaiter().GetResult();
            try{Pump();check(!UndoButton().IsEnabled,"HistoryUndoUi_WorkerLockUpdatesOnDispatcher");}
            finally{Task.Run(workerLock.Dispose).GetAwaiter().GetResult();}
            Complete();check(UndoButton().IsEnabled,"HistoryUndoUi_WorkerUnlockEnablesOnDispatcher");
            File.WriteAllBytes(file,[0]);
            verify.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Complete();
            check(Descendants<System.Windows.Controls.TextBlock>(view).Any(text=>text.Text.Contains("0 registros coherentes"))&&Descendants<System.Windows.Controls.TextBlock>(view).Any(text=>text.Text.Contains("Desfasado: no acredita")),"HistoryInspectionUi_RecheckShowsResetWithoutDeletingLog");
            check(!UndoButton().IsEnabled,"HistoryUndoUi_ExternalChangeRemainsDisabledAfterVerification");
            File.WriteAllBytes(file,[1]);verify.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Complete();
            string log=Directory.GetFiles(Path.Combine(root,"metadata","history"),"*.json").Single();byte[] saved=File.ReadAllBytes(log);
            try
            {
                File.WriteAllText(log,"{invalid");verify.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Complete();
                check(Descendants<System.Windows.Controls.TextBlock>(view).Any(text=>text.Text.StartsWith("No se pudo comprobar"))&&!Descendants<System.Windows.Controls.TextBlock>(view).Any(text=>text.Text.Contains("Cadena coherente")),"HistoryInspectionUi_FailureClearsPreviousVerifiedLabels");
            }
            finally{File.WriteAllBytes(log,saved);}
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
            check(Subscribers()==beforeSubscribers,"HistoryUndoUi_UnloadRemovesStaticHandler");
            source.RootVisual=view;Complete();
            check(UndoButton().IsEnabled&&Subscribers()==beforeSubscribers+1,"HistoryUndoUi_ReloadEnablesWithoutDuplicateSubscription");
            UndoButton().RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Complete();
            check(File.ReadAllBytes(file).SequenceEqual(new byte[]{0})&&history.Read().Entries[^1].Parameters.GetValueOrDefault("UndoOf")==entry.Id.ToString(),"HistoryUndoUi_EnabledButtonRestoresAndRecordsUndo");
            source.RootVisual=null;Pump();
            check(Subscribers()==beforeSubscribers,"HistoryUndoUi_FinalUnloadReleasesStaticHandler");
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
            // «Exportar historial (JSON)» can be loaded as a Global Template: the last applied recipe plus the actions recorded after it.
            {
                // Same shape as the export: an applied recipe, then an editor action recorded afterwards.
                var applied=GlobalTemplateDocument.Empty("USUM",7);applied.Rename("Receta aplicada");
                var exportedHistory=new HistoryDocument(2,"USUM","export-project",[
                    new(Guid.NewGuid(),DateTimeOffset.UtcNow,"Global Template","Aplicar Global Template",[],new Dictionary<string,string>{{"GlobalTemplateSnapshot",applied.ToJson()},{"GlobalTemplateSource",""}},[],"Action"),
                    new(Guid.NewGuid(),DateTimeOffset.UtcNow,"Entrenadores","Randomizar entrenadores",[],new Dictionary<string,string>{{"ActionId","history.export-check"},{"ActionParameters","{\"Seed\":1}"}},[],"Action")]);
                string exported=Path.Combine(root,"historial-progressive.json");File.WriteAllBytes(exported,JsonSerializer.SerializeToUtf8Bytes(exportedHistory,new JsonSerializerOptions{WriteIndented=true}));
                var template=GlobalTemplateDocument.Load(exported,"USUM",7);
                check(template.Name=="Receta aplicada"&&template.Actions.Contains("history.export-check")&&template.SourcePath==exported,"HistoryInspection_ExportedHistoryLoadsAsGlobalTemplate");
                bool otherGame;try{GlobalTemplateDocument.Load(exported,"SM",7);otherGame=false;}catch(InvalidDataException){otherGame=true;}
                check(otherGame&&ProjectHistory.TryParseExport(applied.ToJson()) is null,"HistoryInspection_ExportedHistoryOfAnotherGameIsRejectedAndTemplatesStayTemplates");
            }
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
