using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

internal static class ShinyBatchChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            byte[] code=new byte[0x4000];
            new byte[]{0x23,0x00,0xD4,0xE5,0x01,0x50,0x85,0xE2,0x05,0x00,0x50,0xE1,0xDE,0xFF,0xFF,0xCA}.CopyTo(code,0x1000);
            new byte[]{0x00,0x20,0x22,0xE0,0x02,0x30,0x21,0xE2,0x03,0x20,0x92,0xE1,0x1C,0x00,0x00,0x0A}.CopyTo(code,0x2000);
            string path=Path.Combine(f.Game.ExeFS,"code.bin");File.WriteAllBytes(path,code);
            var doc=GlobalTemplateDocument.Empty(f.Session.GameName,6);
            JsonObject Parameters()=>new(){["rerolls"]="127",["everythingShiny"]="true"};
            void Recipe(JsonObject parameters)=>doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="items.fix-economy",["Parameters"]=new JsonObject()},new JsonObject{["Id"]="shiny-rate.apply",["Parameters"]=parameters}));
            Recipe(Parameters());GlobalEditorTemplateBridge.ValidateBatchReplay(doc);
            var expected=new GameShinyRateService(f.Session).Preview(127,true);
            NativeBatchGen6Bridge.ValidateSession(f.Session,doc);
            check(File.ReadAllBytes(path).SequenceEqual(code),version+" Shiny Batch preflight is read-only");
            NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),91);
            check(File.ReadAllBytes(path).SequenceEqual(expected.File.Proposed()),version+" Shiny Batch exactly reproduces native ARM proposal including rounded rerolls");
            var state=new GameShinyRateService(f.Session).Read();
            check(state.AlwaysShiny&&state.Rerolls==expected.AppliedRerolls,version+" Shiny Batch reads back both requested settings");
            NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),92);
            check(File.ReadAllBytes(path).SequenceEqual(expected.File.Proposed()),version+" Shiny Batch is idempotent and independent of random seed");
            foreach(var invalid in new[]{new JsonObject(),new JsonObject{["rerolls"]="0",["everythingShiny"]="true"},new JsonObject{["rerolls"]="9001",["everythingShiny"]="false"},new JsonObject{["rerolls"]="125.5",["everythingShiny"]="false"},new JsonObject{["rerolls"]="125",["everythingShiny"]="maybe"},new JsonObject{["rerolls"]="125",["everythingShiny"]="false",["extra"]="1"}})
            {Recipe(invalid);reject(()=>NativeBatchGen6Bridge.ValidateDocument(doc),version+" Shiny Batch rejects missing/invalid/unknown parameters");}
            File.WriteAllBytes(path,new byte[code.Length]);Recipe(Parameters());
            string items=ModuleChecks.PathFor(f,"item");byte[] before=File.ReadAllBytes(items);
            reject(()=>NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),91),version+" Unknown PID signature blocks all selected native actions");
            check(File.ReadAllBytes(items).SequenceEqual(before)&&File.ReadAllBytes(path).All(b=>b==0),version+" Shiny preflight conflict preserves code and economy");
        }
    }
}
