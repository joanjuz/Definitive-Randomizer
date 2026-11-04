using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;

// Isolated diagnostic: evaluate every expectation before reporting failure.
// All files and metadata belong to disposable synthetic fixtures.
internal static class UndoTemplateRegressionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var state=new TestState();
        var results=new List<(bool Passed,string Name)>();
        void Expect(bool passed,string name){results.Add((passed,name));Console.WriteLine($"{(passed?"PASS":"FAIL")}: {name}");}
        StudioDialog.AutomatedChecks=true;
        var app=new ThreeDS.Progressive.App();app.InitializeComponent();WpfTestHost.SuppressStartup();
        Manual();UndoChain();EditorOptions();Recipe();ManualOperation();MultipleRecipes();
        int failed=results.Count(r=>!r.Passed);
        Console.WriteLine($"Undo/template expectations: {results.Count-failed} passed, {failed} failed, {results.Count} total.");
        check(failed==0,"UndoTemplateRegression: "+string.Join(", ",results.Where(r=>!r.Passed).Select(r=>r.Name)));

        void Manual()
        {
            using var f=new Fixture(GameVersion.USUM);
            byte[] original=File.ReadAllBytes(f.PersonalPath);var before=f.Session.ReadPersonal(1);
            f.Session.SavePersonal(1,before with {Attack=130});
            var h=f.Session.History;var e1=h.Read().Entries.Single();
            Expect(HasAttack(Export(h.Read())),"Manual_NoUndo_ExportsE1");
            RestorePoints.Undo(h,f.Root,e1.Id);var history=h.Read();var e2=history.Entries.Last();
            Expect(e2.Parameters.GetValueOrDefault("UndoOf")==e1.Id.ToString(),"Manual_E2ReferencesE1");
            Expect(File.ReadAllBytes(f.PersonalPath).SequenceEqual(original),"Manual_UndoRestoresExactBytes");
            Expect(history.Entries.Count==2&&history.Entries[0].Id==e1.Id,"Manual_AppendOnlyAuditRetainsE1AndE2");
            var exported=Export(history);string path=Path.Combine(f.Root,"undone-template.json");exported.Save(path);
            var reloaded=GlobalTemplateDocument.Load(path,"USUM",7);
            Expect(!reloaded.Actions.Contains(ManualEditTemplate.ActionId),"Manual_UndoneE1AbsentFromSavedTemplate");
            Expect(HasAttack(Export(history with {Entries=[e1]})),"Manual_LegacyHistoryWithoutUndoStillExports");
            // ManualEdits is a separate supported history payload reader.
            var legacy=e1 with {Changes=[],Parameters=new Dictionary<string,string>{{"ManualEdits",JsonSerializer.Serialize(new[]{new ManualEditTemplate.Edit("personal",1,"Attack",before.Attack.ToString(),"130")})}}};
            Expect(HasAttack(Export(history with {Entries=[legacy]})),"Manual_LegacyPayloadNoUndoExports");
            Expect(!HasAttack(Export(history with {Entries=[legacy,e2]})),"Manual_LegacyPayloadUndoExcludesE1");
            var grouped=HistoryView.Group(history.Entries.Reverse().ToArray());
            Expect(grouped.Count==2&&grouped.Select(r=>r.Main.Id).ToHashSet().SetEquals(new[]{e1.Id,e2.Id}),"Manual_HistoryViewPreservesAuditRows");
        }

        void UndoChain()
        {
            using var f=new Fixture(GameVersion.USUM);
            byte[] original=File.ReadAllBytes(f.PersonalPath);
            var before=f.Session.ReadPersonal(1);var h=f.Session.History;
            f.Session.SavePersonal(1,before with {Attack=130});
            byte[] edited=File.ReadAllBytes(f.PersonalPath);
            var e1=h.Read().Entries.Single();
            // Undoing the restoration brings the original change back: exclusion by id alone would drop it for good.
            RestorePoints.Undo(h,f.Root,e1.Id);var u1=h.Read().Entries.Last();
            Expect(!HasAttack(Export(h.Read())),"Chain_UndoExcludesOriginal");
            RestorePoints.Undo(h,f.Root,u1.Id);var redone=h.Read();
            Expect(redone.Entries.Count==3&&redone.Entries[^1].Parameters.GetValueOrDefault("UndoOf")==u1.Id.ToString(),"Chain_UndoOfUndoIsAppended");
            Expect(File.ReadAllBytes(f.PersonalPath).SequenceEqual(edited),"Chain_UndoOfUndoRestoresChange");
            Expect(HasAttack(Export(redone)),"Chain_UndoOfUndoReinstatesOriginalInTemplate");
            // The projection keeps the standing entry and drops the reverted restoration.
            var single=EffectiveHistory.Entries(redone.Entries);
            Expect(single.Select(e=>e.Id).Contains(e1.Id)&&!single.Select(e=>e.Id).Contains(u1.Id),"Chain_ProjectionKeepsOnlyStandingEntries");
            Expect(single.Select(e=>e.Id).SequenceEqual(new[]{e1.Id,redone.Entries[^1].Id}),"Chain_ProjectionPreservesJournalOrder");
            Expect(EffectiveHistory.Entries(single).Select(e=>e.Id).SequenceEqual(single.Select(e=>e.Id)),"Chain_ProjectionIsIdempotent");
            RestorePoints.Undo(h,f.Root,redone.Entries[^1].Id);
            Expect(File.ReadAllBytes(f.PersonalPath).SequenceEqual(original)&&!HasAttack(Export(h.Read())),"Chain_ThirdUndoRestoresOriginalBytesAndExcludesEdit");
            var legacy=redone with {Entries=[e1]};
            Expect(ReferenceEquals(EffectiveHistory.Of(legacy),legacy),"Chain_LegacyWithoutUndoIsUnchanged");
        }

        void EditorOptions()
        {
            using var f=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(f);
            string file=Path.Combine(f.Root,"options-synthetic.bin");File.WriteAllBytes(file,[1]);var h=f.Session.History;
            var parameters=new Dictionary<string,string>{{"Scope","All"},{"ActionId","trainers.randomize"},{"Controls","{\"NUD_LevelBoost\":125}"}};
            h.Commit([new FileChange(file,[1],[2])],"Entrenadores","Randomizar entrenadores",[],parameters);
            var e1=h.Read().Entries.Single();
            Expect(HasTrainerOption(f),"Options_NoUndo_RealReaderRestores125");
            Expect(Export(h.Read()).Actions.Contains("trainers.randomize"),"Merge_NoUndo_ExportsEditorAction");
            RestorePoints.Undo(h,f.Root,e1.Id);
            Expect(File.ReadAllBytes(file).SequenceEqual(new byte[]{1}),"Options_UndoRestoresSyntheticFile");
            Expect(!HasTrainerOption(f),"Options_UndoneEditorOptionNotShownAsApplied");
            Expect(!Export(h.Read()).Actions.Contains("trainers.randomize"),"Merge_UndoneEditorActionAbsent");
        }

        void Recipe()
        {
            using var f=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(f);
            string file=Path.Combine(f.Root,"recipe-synthetic.bin");File.WriteAllBytes(file,[3]);var h=f.Session.History;
            var recipe=GlobalTemplateDocument.Empty("USUM",7);recipe.Rename("Synthetic undo recipe");
            recipe.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="trainers.randomize",["Parameters"]=new JsonObject()}));
            recipe.SetSection("RandSettings",new JsonArray("SMTE","NUD_LevelBoost\t1.25",""));
            h.CommitGlobalTemplate([new FileChange(file,[3],[4])],recipe,17,ProjectHistory.Hash(JsonSerializer.SerializeToUtf8Bytes(h.Read())));
            var active=h.Read();var master=active.Entries.Single(e=>e.Parameters.ContainsKey("GlobalTemplateSnapshot"));
            var child=active.Entries.Single(e=>e.Parameters.ContainsKey("ActionId"));
            Expect(master.Parameters["GlobalApplication"]==child.Parameters["GlobalApplication"],"Recipe_MasterAndChildShareGlobalApplication");
            Expect(Export(active).Actions.Contains("trainers.randomize"),"Recipe_NoUndo_ExportsSnapshot");
            Expect(HasTrainerOption(f),"Recipe_NoUndo_RealReaderRestores125");
            RestorePoints.Undo(h,f.Root,master.Id);var undone=h.Read();
            Expect(File.ReadAllBytes(file).SequenceEqual(new byte[]{3}),"Recipe_UndoRestoresSyntheticFile");
            Expect(undone.Entries.Last().Parameters.GetValueOrDefault("UndoOf")==master.Id.ToString()&&!undone.Entries.Last().Parameters.ContainsKey("GlobalApplication"),"Recipe_UndoTargetsMasterOnly");
            Expect(!Export(undone).Actions.Contains("trainers.randomize"),"Recipe_UndoneSnapshotNotExported");
            var merged=GlobalTemplateDocument.Empty("USUM",7);merged.MergeHistoryActions(undone);
            Expect(!merged.Actions.Contains("trainers.randomize"),"Recipe_UndoneChildNotMerged");
            Expect(!HasTrainerOption(f),"Recipe_UndoneGlobalOptionsNotShownAsApplied");
            var grouped=HistoryView.Group(undone.Entries.Reverse().ToArray());
            Expect(grouped.Count==2&&grouped.Single(r=>r.Main.Id==master.Id).Actions.Single().Id==child.Id,"Recipe_HistoryViewPreservesGroupedAudit");
        }

        void ManualOperation()
        {
            using var f=new Fixture(GameVersion.USUM);var h=f.Session.History;
            var before=f.Session.ReadPersonal(1);
            var payload=new JsonObject{["Entries"]=JsonSerializer.SerializeToNode(new[]{new ManualEditTemplate.Edit("personal",1,"Attack",before.Attack.ToString(),"130")})};
            using(var scope=ManualOperationScope.Begin("exact-overrides","synthetic-exact",payload))
                f.Session.SavePersonal(1,before with {Attack=130});
            byte[] edited=File.ReadAllBytes(f.PersonalPath);
            var e1=h.Read().Entries.Single();
            Expect(e1.Parameters.ContainsKey("ManualOperation"),"Operation_RealCommitRecordsPayload");
            var active=Export(h.Read());active.Validate("USUM",7);
            Expect(ManualOperations.Read(active).Single()["Payload"]!["Entries"]![0]!["After"]!.GetValue<string>()=="130","Operation_NoUndoExportsExactValue");
            RestorePoints.Undo(h,f.Root,e1.Id);var restoration=h.Read().Entries.Last();
            Expect(ManualOperations.Read(Export(h.Read())).Length==0,"Operation_UndoExcludesPayload");
            RestorePoints.Undo(h,f.Root,restoration.Id);
            Expect(ManualOperations.Read(Export(h.Read())).Length==1&&File.ReadAllBytes(f.PersonalPath).SequenceEqual(edited),"Operation_UndoOfUndoReactivatesPayload");
        }

        void MultipleRecipes()
        {
            using var f=new Fixture(GameVersion.USUM);ModuleChecks.CreateArchives(f);var h=f.Session.History;
            string file=Path.Combine(f.Root,"multiple-recipes.bin");File.WriteAllBytes(file,[1]);
            HistoryEntry Apply(string name,string boost,byte from,byte to)
            {
                var doc=GlobalTemplateDocument.Empty("USUM",7);doc.Rename(name);
                doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="trainers.randomize",["Parameters"]=new JsonObject{["Marker"]=name}}));
                doc.SetSection("RandSettings",new JsonArray("SMTE","NUD_LevelBoost\t"+boost,""));
                h.CommitGlobalTemplate([new FileChange(file,[from],[to])],doc,17,ProjectHistory.Hash(JsonSerializer.SerializeToUtf8Bytes(h.Read())));
                return h.Read().Entries.Last(e=>e.Parameters.ContainsKey("GlobalTemplateSnapshot"));
            }
            var a=Apply("A","1.25",1,2);var b=Apply("B","1.75",2,3);
            Expect(Export(h.Read()).Name=="B"&&HasTrainerOption(f,175),"Multiple_LatestSnapshotAndOptionsSelected");
            RestorePoints.Undo(h,f.Root,b.Id);var undoB=h.Read().Entries.Last();
            var effective=EffectiveHistory.Of(h.Read());
            Expect(Export(h.Read()).Name=="A"&&HasTrainerOption(f),"Multiple_UndoBSelectsPreviousSnapshotAndOptions");
            Expect(effective.Entries.Any(e=>e.Id==a.Id)&&!effective.Entries.Any(e=>e.Id==b.Id),"Multiple_OnlyUndoneMasterExcluded");
            var child=effective.Entries.Single(e=>e.Parameters.ContainsKey("ActionId"));
            Expect(child.Parameters["GlobalApplication"]==a.Parameters["GlobalApplication"],"Multiple_SameActionTypeInOtherApplicationSurvives");
            var merge=GlobalTemplateDocument.Empty("USUM",7);merge.MergeHistoryActions(h.Read());
            Expect(merge.Section("Actions")![0]!["Parameters"]!["Marker"]!.GetValue<string>()=="A","Multiple_MergeUsesSurvivingApplicationPayload");
            RestorePoints.Undo(h,f.Root,undoB.Id);
            Expect(Export(h.Read()).Name=="B"&&HasTrainerOption(f,175)&&File.ReadAllBytes(file).SequenceEqual(new byte[]{3}),"Multiple_UndoOfUndoReactivatesRecipeAndOptions");
            Expect(EffectiveHistory.Of(h.Read()).Entries.Count(e=>e.Parameters.ContainsKey("ActionId"))==2,"Multiple_UndoOfUndoReactivatesChildren");
            var c=Apply("C","1.5",3,4);RestorePoints.Undo(h,f.Root,c.Id);
            var d=Apply("D","1.6",3,5);
            Expect(Export(h.Read()).Name=="D"&&HasTrainerOption(f,160),"Multiple_NewRecipeAfterIntermediateUndoSelected");
            var ids=EffectiveHistory.Of(h.Read()).Entries.Where(e=>e.Parameters.ContainsKey("ActionId")).Select(e=>e.Parameters["GlobalApplication"]).ToHashSet();
            Expect(ids.SetEquals(new[]{a.Parameters["GlobalApplication"],b.Parameters["GlobalApplication"],d.Parameters["GlobalApplication"]}),"Multiple_IntermediateUndoExcludesOnlyMatchingChildren");
        }
    }

    private static GlobalTemplateDocument Export(HistoryDocument history)=>GlobalTemplateDocument.FromHistory(history,"USUM",7,null);
    private static bool HasAttack(GlobalTemplateDocument doc)=>doc.Section("ProgressiveApp")?["ManualEdits"]?["Entries"] is JsonArray entries&&entries.Any(e=>e?["Field"]?.GetValue<string>()=="Attack"&&e?["After"]?.GetValue<string>()=="130");
    private static bool HasTrainerOption(Fixture f,int expected=125)
    {
        var main=new MainWindow();
        try
        {
            main.LoadProject(f.Session);main.AppliedOptionsReady.GetAwaiter().GetResult();
            var states=(Dictionary<string,RecordEditorView.UiState>)typeof(MainWindow).GetField("_sectionOptions",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;
            return states.TryGetValue("Entrenadores",out var saved)&&saved.Options.GetValueOrDefault("NUD_LevelBoost")==expected;
        }
        finally{main.Close();}
    }
}
