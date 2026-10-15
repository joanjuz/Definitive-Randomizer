using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Services;

internal static class CatchBatchChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using(var wild=new Fixture(version))
            {
                ModuleChecks.CreateArchives(wild);var records=ModuleChecks.Read(wild,"encdata");int index=wild.Game.ORAS?2:1;
                BitConverter.GetBytes((ushort)2047).CopyTo(records[index],32);
                wild.WriteGarc(ModuleChecks.PathFor(wild,"encdata"),records,wild.Game.GetGARCReference("encdata"));
                var editor=new GameEncounterService(wild.Session);var options=ThreeDS.Progressive.Models.EditorValues.Flatten(editor.RandomOptions);
                options["Species"]=0;options["ModifyLevel"]=1;options["LevelFlat"]=1;
                var proposal=editor.Preview(options,editor.Entries.Select(entry=>entry.Id).ToArray(),71);editor.Apply(proposal);
                var entry=editor.Entries.First();var values=ThreeDS.Progressive.Models.EditorValues.Flatten(editor.Read(entry.Id));
                check(values["S0.Species"]==2047&&values["S0.Min"]==10&&values["S1.Min"]==11,version+" Wild level preview preserves entire unknown slot and updates valid slots");
                values["S0.Species"]=2046;reject(()=>editor.Save(entry.Id,values),version+" Wild still rejects introducing a new unknown species");
            }
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            byte[] source=[1,2,3,4],target=[1,9,3,4];string path=Path.Combine(f.Game.RomFS,"catch-fixture.bin");File.WriteAllBytes(path,source);
            string manifest=Path.Combine(f.Root,"catch-fixture.json");
            File.WriteAllBytes(manifest,JsonSerializer.SerializeToUtf8Bytes(new CatchZoneManifest{Format=2,Game=f.Game.ORAS?"ORAS":"XY",Files=[new(){Scope="raw",Path="catch-fixture.bin",Length=4,TargetLength=4,SourceSha256=CatchZonePatchEngine.Hash(source),TargetSha256=CatchZonePatchEngine.Hash(target),PatchMode="hunks",Hunks=[new(){Offset=1,Data="CQ=="}]}]}));
            var doc=GlobalTemplateDocument.Empty(f.Session.GameName,6);
            doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="items.fix-economy",["Parameters"]=new JsonObject()},new JsonObject{["Id"]="catchzones.enable",["Parameters"]=new JsonObject()}));
            GlobalEditorTemplateBridge.ValidateBatchReplay(doc);string items=ModuleChecks.PathFor(f,"item");byte[] before=File.ReadAllBytes(items);
            File.WriteAllBytes(path,[1,8,3,4]);
            reject(()=>NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),71,manifest),version+" Batch Catch source mismatch blocks every selected native module");
            check(File.ReadAllBytes(items).SequenceEqual(before)&&File.ReadAllBytes(path).SequenceEqual(new byte[]{1,8,3,4}),version+" Catch preflight failure preserves economy and conflicting source");
            File.WriteAllBytes(path,source);NativeBatchGen6Bridge.ValidateSession(f.Session,doc,manifest);
            check(File.ReadAllBytes(path).SequenceEqual(source),version+" Catch Batch preflight does not write source");
            NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),71,manifest);
            check(File.ReadAllBytes(path).SequenceEqual(target)&&new Item(ModuleChecks.Read(f,"item")[2]).BuyPrice==200,version+" Batch Catch and economy both apply their verified outputs");
            check(f.Session.History.Read().Entries.First().Module=="Catch Zones",version+" Catch executes first even when recipe lists it last");
            byte[] stable=File.ReadAllBytes(items);NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),71,manifest);
            check(File.ReadAllBytes(path).SequenceEqual(target)&&File.ReadAllBytes(items).SequenceEqual(stable),version+" Catch Batch repeated preset is idempotent");
            doc.SetSection("ProgressiveApp",new JsonObject{["CatchZones"]=new JsonObject{["Scope"]=1}});
            reject(()=>NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),71,manifest),version+" Catch Batch rejects unsupported custom snapshot");
        }
    }
}
