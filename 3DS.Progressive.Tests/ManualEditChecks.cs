using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

internal static class ManualEditChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var state=new TestState();
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var source=new Fixture(version);ModuleChecks.CreateArchives(source);
            using var target=new Fixture(version);ModuleChecks.CreateArchives(target);
            var original=source.Session.ReadPersonal(1);
            source.Session.SavePersonal(1,original with{Attack=120});
            source.Session.SavePersonal(1,source.Session.ReadPersonal(1) with{Attack=130,Speed=111});
            var text=new GameTextService(source.Session);var lines=text.Read(0).ToArray();lines[1]=@"Hola [VAR TRNAME(0000)]!\nTexto propio";text.Save(0,lines);
            var story=new GameTextService(source.Session,true);var dialogue=story.Read(1).ToArray();dialogue[0]="Evento personalizado";story.Save(1,dialogue);
            var doc=GlobalTemplateDocument.Empty(version.ToString(),source.Game.Generation);doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});
            doc.MergeHistoryActions(source.Session.History);ManualEditTemplate.Capture(doc,source.Session.History.Read());
            var generic=GlobalTemplateDocument.Empty("ANY",source.Game.Generation);generic.MergeHistoryActions(source.Session.History);check(generic.Game==version.ToString(),version+" generic recipe is pinned to source game when exact modifications are added");
            check(doc.Actions.SequenceEqual(new[]{ManualEditTemplate.ActionId}),version+" specific edits captured without inventing randomization actions");
            check(doc.Section("ProgressiveApp")!["ManualEdits"]!["Entries"]!.AsArray().Count==4,version+" repeated edits consolidated by exact field and line");
            check(NativeRecipeCompletion.TryComplete(source.Session,doc),version+" manual-only history produces a complete batch recipe");
            GlobalEditorTemplateBridge.ValidateBatchReplay(doc);
            string path=Path.Combine(source.Root,"manual.json");doc.Save(path);doc=GlobalTemplateDocument.Load(path,version.ToString(),source.Game.Generation);
            var assembly=Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));var type=assembly.GetType("pk3DS.WinForms.GlobalRandomizationTemplate",true)!;
            string replay=JsonSerializer.Serialize(JsonSerializer.Deserialize(doc.ToJson(),type),type);doc=GlobalTemplateDocument.Parse(replay,version.ToString(),source.Game.Generation);
            check(doc.Section("ProgressiveApp")!["ManualEdits"]!["Entries"]!.AsArray().Count==4,version+" typed legacy engine preserves exact override payload");
            var before=File.ReadAllBytes(target.PersonalPath);ManualEditTemplate.ValidateSession(target.Session,doc);
            check(before.SequenceEqual(File.ReadAllBytes(target.PersonalPath))&&new GameTextService(target.Session).Read(0)[1]=="Primer texto",version+" preflight writes nothing");
            target.Session.SavePersonal(1,target.Session.ReadPersonal(1) with{Attack=32,Defense=99});
            ManualEditTemplate.Apply(target.Session,doc);
            var result=target.Session.ReadPersonal(1);
            check(result.Attack==130&&result.Speed==111&&result.Defense==99,version+" deterministic final overrides retain unrelated randomized fields");
            check(new GameTextService(target.Session).Read(0)[1]==lines[1]&&new GameTextService(target.Session,true).Read(1)[0]==dialogue[0],version+" Game and Story exact text controls survive replay");
            check(new GameTextService(target.Session).Read(1)[0]=="Segunda sección"&&target.Session.ReadPersonal(2).Attack==80,version+" unrelated text and species preserved");
            target.Game.Language=6;ManualEditTemplate.Apply(target.Session,doc);check(target.Game.Language==6,version+" replay targets the recorded language and restores active UI language");target.Game.Language=0;
            var journal=target.Session.History.Read().Entries.Last();
            check(journal.Files.Count==3&&journal.Parameters.ContainsKey("ManualEdits"),version+" all personal and text archives committed together with replayable audit");
            var recaptured=GlobalTemplateDocument.Empty(version.ToString(),source.Game.Generation);recaptured.MergeHistoryActions(target.Session.History);
            check(recaptured.Section("ProgressiveApp")!["ManualEdits"]!["Entries"]!.AsArray().Count==5,version+" generated project can export its exact overrides again");
            var records=target.PersonalRecords;check(records.Take(records.Length-1).SelectMany(b=>b).SequenceEqual(records[^1]),version+" Personal master table matches individual edited records");
            var wrongGame=doc.Clone();wrongGame.SetSection("Game",JsonValue.Create("ANY"));reject(()=>ManualEditTemplate.Validate(wrongGame),version+" manual replacements cannot be generalized silently to ANY");
            var once=File.ReadAllBytes(target.PersonalPath);ManualEditTemplate.Apply(target.Session,doc);check(once.SequenceEqual(File.ReadAllBytes(target.PersonalPath)),version+" repeat replay is idempotent");
            var mixed=doc.Clone();NativeMasterBatchBridge.Capture(mixed,new(BatchAction.PersonalRandomize,new BatchSettings{Stats=true},0),source.Game.Generation);
            var mixedActions=mixed.Section("Actions")!.AsArray();mixedActions.Add(new JsonObject{["Id"]="personal.randomize",["Parameters"]=new JsonObject()});mixed.SetSection("Actions",mixedActions);GlobalEditorTemplateBridge.ValidateBatchReplay(mixed);
            foreach(int seed in new[]{17,9801})
            {
                NativeMasterBatchBridge.ExecuteSession(target.Session,mixed.ToJson(),seed,"Personal");ManualEditTemplate.Apply(target.Session,mixed);
                check(target.Session.ReadPersonal(1).Attack==130&&target.Session.ReadPersonal(1).Speed==111,version+" random seed "+seed+" cannot change explicit manual values");
            }
            once=File.ReadAllBytes(target.PersonalPath);
            var invalid=doc.Clone();var native=invalid.Section("ProgressiveApp")!;native["ManualEdits"]!["Entries"]![0]!["After"]="999";invalid.SetSection("ProgressiveApp",native);
            reject(()=>ManualEditTemplate.Apply(target.Session,invalid),version+" invalid stat rejected before all writes");check(once.SequenceEqual(File.ReadAllBytes(target.PersonalPath)),version+" rejected override leaves personal unchanged");
            invalid=doc.Clone();native=invalid.Section("ProgressiveApp")!;native["ManualEdits"]!["Entries"]![2]!["Entry"]=99;invalid.SetSection("ProgressiveApp",native);
            reject(()=>ManualEditTemplate.Apply(target.Session,invalid),version+" nonexistent text destination rejects entire proposal");
            check(once.SequenceEqual(File.ReadAllBytes(target.PersonalPath)),version+" bad text never partly applies personal overrides");
            var altered=new GameTextService(target.Session);var changed=altered.Read(0).ToArray();changed[1]="Texto de otra revisión";altered.Save(0,changed);
            reject(()=>ManualEditTemplate.ValidateSession(target.Session,doc),version+" incompatible original line rejected with exact destination");
            invalid=doc.Clone();invalid.SetSection("Actions",new JsonArray());ManualEditTemplate.Apply(target.Session,invalid);check(new GameTextService(target.Session).Read(0)[1]==changed[1],version+" deselected manual action never writes");
            string audit=Path.Combine(source.Root,"history-probe");File.WriteAllText(audit,"a");source.Session.History.Commit([new FileChange(audit,new byte[]{97},new byte[]{98})],"Prueba","Acción",[],new Dictionary<string,string>{{"ActionId","moves.metronome"},{"Scope","All"}});
            var combined=GlobalTemplateDocument.Empty(version.ToString(),source.Game.Generation);combined.MergeHistoryActions(source.Session.History);check(combined.Actions.Contains("moves.metronome")&&combined.Actions.Contains(ManualEditTemplate.ActionId),version+" mixed history retains both manual and original randomization action IDs");
            invalid=doc.Clone();native=invalid.Section("ProgressiveApp")!;native["ManualEdits"]!["Version"]=999;invalid.SetSection("ProgressiveApp",native);reject(()=>ManualEditTemplate.Validate(invalid),version+" unsupported override schema rejected");
        }
    }
}
