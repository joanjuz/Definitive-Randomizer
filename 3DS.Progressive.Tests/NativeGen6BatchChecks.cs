using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class NativeGen6BatchChecks
{
    private static GlobalTemplateDocument Recipe(Fixture f,IProjectEditor editor,string action,Dictionary<string,int>? options=null)
    {
        var doc=GlobalTemplateDocument.Empty(f.Game.Version.ToString(),6);
        GlobalEditorTemplateBridge.Capture(doc,editor,new(options??EditorValues.Flatten(editor.RandomOptions),0),6);
        doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]=action,["Parameters"]=new JsonObject()}));return doc;
    }
    private static string Replay(GlobalTemplateDocument doc)
    {
        var assembly=System.Reflection.Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));
        var type=assembly.GetType("pk3DS.WinForms.GlobalRandomizationTemplate",true)!;
        var template=System.Text.Json.JsonSerializer.Deserialize(doc.ToJson(),type)!;
        return System.Text.Json.JsonSerializer.Serialize(template,type);
    }
    private static string Field(Fixture f)
    {
        ModuleChecks.CreateArchives(f);bool oras=f.Game.ORAS;int at=oras?0xB64:0xB04;
        byte[] instructions=new byte[0x1800];for(int i=0;i<3;i++)BitConverter.GetBytes((ushort)(155+i)).CopyTo(instructions,at+i*12);
        byte[] compressed=Gen6ScriptCodec.Encode(instructions),script=new byte[32+compressed.Length];
        BitConverter.GetBytes(script.Length).CopyTo(script,0);BitConverter.GetBytes(0x0A0AF1E0).CopyTo(script,4);
        BitConverter.GetBytes(32).CopyTo(script,12);BitConverter.GetBytes(32).CopyTo(script,16);BitConverter.GetBytes(32+instructions.Length).CopyTo(script,20);compressed.CopyTo(script,32);
        var files=Enumerable.Range(0,60).Select(_=>new byte[]{0xAA,0xBB}).ToArray();files[oras?39:17]=script;files[oras?57:26]=(byte[])script.Clone();
        string path=Path.Combine(f.Game.RomFS,oras?"a/0/2/9":"a/0/3/1");var reference=f.Game.GetGARCReference("personal");
        f.WriteGarc(path,files,reference.GetRelativeGARC((oras?29:31)-reference.FileNumber,"scripts"));return path;
    }
    private static void Statics(Fixture f)
    {
        bool oras=f.Game.ORAS;int stat=oras?0xF1B20:0xEE46C,gift=oras?0xF906C:0xF805C,size=oras?36:24,count=oras?37:19;
        byte[] code=new byte[gift+size*count+64];"CRO0"u8.CopyTo(code.AsSpan(0x80));
        for(int i=0;i<(oras?59:13);i++){BitConverter.GetBytes((ushort)1).CopyTo(code,stat+i*12);code[stat+i*12+3]=5;}
        for(int i=0;i<count;i++){BitConverter.GetBytes((ushort)1).CopyTo(code,gift+i*size);code[gift+i*size+5]=5;}
        File.WriteAllBytes(Path.Combine(f.Game.RomFS,"DllField.cro"),code);
        byte[] selector=new byte[0x1000];"CRO0"u8.CopyTo(selector.AsSpan(0x80));BitConverter.GetBytes(0x200).CopyTo(selector,0xB8);BitConverter.GetBytes(0xE00).CopyTo(selector,0xBC);BitConverter.GetBytes(0x200).CopyTo(selector,0xC0);
        for(int i=0;i<(oras?12:6);i++)BitConverter.GetBytes((ushort)1).CopyTo(selector,0x200+(oras?0:16)+i*0x54);
        File.WriteAllBytes(Path.Combine(f.Game.RomFS,"DllPoke3Select.cro"),selector);
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using(var firstWild=new Fixture(version))
            using(var secondWild=new Fixture(version))
            {
                ModuleChecks.CreateArchives(firstWild);ModuleChecks.CreateArchives(secondWild);
                var editor=new GameEncounterService(firstWild.Session);var doc=Recipe(firstWild,editor,"wild-encounters.randomize");
                GlobalEditorTemplateBridge.ValidateBatchReplay(doc);
                NativeBatchGen6Bridge.ExecuteSession(firstWild.Session,Replay(doc),93);NativeBatchGen6Bridge.ExecuteSession(secondWild.Session,Replay(doc),93);
                string a=ModuleChecks.PathFor(firstWild,"encdata"),b=ModuleChecks.PathFor(secondWild,"encdata");
                check(File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)),version+" Batch Wild reproduces full snapshot with fixed seed");
                editor=new GameEncounterService(firstWild.Session);var options=EditorValues.Flatten(editor.RandomOptions);
                options["Species"]=0;options["ModifyLevel"]=1;options["LevelScale"]=200;options["LevelFlat"]=3;
                var before=editor.Entries.ToDictionary(e=>e.Id,e=>EditorValues.Flatten(editor.Read(e.Id)));
                doc=Recipe(firstWild,editor,"wild-encounters.modify-levels",options);NativeBatchGen6Bridge.ExecuteSession(firstWild.Session,Replay(doc),93);
                editor=new GameEncounterService(firstWild.Session);
                check(editor.Entries.All(e=>EditorValues.Flatten(editor.Read(e.Id)).Where(p=>p.Key.EndsWith("Species")||p.Key.EndsWith("Form")).All(p=>p.Value==before[e.Id][p.Key])),version+" Batch Wild level action preserves species and forms");
                check(editor.Entries.All(e=>EditorValues.Flatten(editor.Read(e.Id)).Where(p=>p.Key.EndsWith("Min")).All(p=>p.Value==23)),version+" Batch Wild applies scale and flat level adjustment");
                var snapshot=doc.Section("ProgressiveApp")!;snapshot["Wild"]!["Scope"]=1;doc.SetSection("ProgressiveApp",snapshot);byte[] stable=File.ReadAllBytes(a);
                reject(()=>NativeBatchGen6Bridge.ExecuteSession(firstWild.Session,Replay(doc),93),version+" Batch Wild rejects partial scope");
                check(stable.SequenceEqual(File.ReadAllBytes(a)),version+" rejected Wild scope leaves encounter archive unchanged");
            }
            using(var first=new Fixture(version))
            using(var second=new Fixture(version))
            {
                const int at=0x400080;byte[] source=Enumerable.Repeat((byte)0xA5,0x401000).ToArray();
                for(int i=0;i<29;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(source,at+i*2);
                GamePickupService.Signature.CopyTo(source,at+58);
                string a=Path.Combine(first.Game.ExeFS,"code.bin"),b=Path.Combine(second.Game.ExeFS,"code.bin");
                File.WriteAllBytes(a,source);File.WriteAllBytes(b,source);
                var doc=GlobalTemplateDocument.Empty(first.Game.Version.ToString(),6);
                doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="pickup.randomize",["Parameters"]=new JsonObject()}));
                GlobalEditorTemplateBridge.ValidateBatchReplay(doc);
                NativeBatchGen6Bridge.ExecuteSession(first.Session,Replay(doc),87);NativeBatchGen6Bridge.ExecuteSession(second.Session,Replay(doc),87);
                byte[] changed=File.ReadAllBytes(a);
                check(changed.SequenceEqual(File.ReadAllBytes(b))&&!changed.SequenceEqual(source),version+" Batch Pickup same seed reproduces changed bytes through original template");
                check(changed.AsSpan(0,at).SequenceEqual(source.AsSpan(0,at))&&changed.AsSpan(at+58).SequenceEqual(source.AsSpan(at+58)),version+" Batch Pickup preserves all bytes outside the 29 item IDs");
                var pool=ThreeDS.Progressive.Services.ProgressiveLegacy.Randomizer.GetRandomItemList(first.Game).Where(id=>id>0&&id<first.Session.ItemNames.Count).ToHashSet();
                check(new GamePickupService(first.Session).Read().All(row=>pool.Contains(row.Item)),version+" Batch Pickup selects only legal items");
                var native=new JsonObject{["Pickup"]=new JsonObject{["Scope"]=1}};doc.SetSection("ProgressiveApp",native);
                reject(()=>NativeBatchGen6Bridge.ExecuteSession(first.Session,Replay(doc),87),version+" Batch Pickup refuses custom snapshots");
                check(changed.SequenceEqual(File.ReadAllBytes(a)),version+" rejected Pickup snapshot leaves code unchanged");
            }
            using(var f=new Fixture(version))
            {
                Statics(f);var editor=new GameStaticEncounter6Service(f.Session);var options=EditorValues.Flatten(editor.RandomOptions);
                options["Static"]=0;options["Gifts"]=0;options["Starters"]=1;options["BasicStarter"]=1;
                var doc=Recipe(f,editor,"starters.randomize",options);GlobalEditorTemplateBridge.ValidateBatchReplay(doc);
                string path=Path.Combine(f.Game.RomFS,"DllField.cro");byte[] before=File.ReadAllBytes(path);
                NativeBatchGen6Bridge.ExecuteSession(f.Session,Replay(doc),42);var reread=new GameStaticEncounter6Service(f.Session);
                var starters=reread.Entries.Where(e=>e.Name.StartsWith("Inicial")).ToArray();
                check(starters.All(e=>pk3DS.Core.Legal.BasicStarters_6.Contains(EditorValues.Flatten(reread.Read(e.Id))["Species"])),version+" Batch native starter recipe applies full basic-starter options");
                int gift=f.Game.ORAS?0xF906C:0xF805C,size=f.Game.ORAS?36:24;int[] indices=f.Game.ORAS?[0,1,2,28,29,30,31,32,33,34,35,36]:[0,1,2,3,4,5];
                byte[] field=File.ReadAllBytes(path),selector=File.ReadAllBytes(Path.Combine(f.Game.RomFS,"DllPoke3Select.cro"));
                check(CroIntegrity.Rebuild(field).SequenceEqual(field)&&CroIntegrity.Rebuild(selector).SequenceEqual(selector),version+" Batch native CRO hashes are rebuilt after static changes");
                check(indices.Select((id,i)=>BitConverter.ToUInt16(field,gift+id*size)==BitConverter.ToUInt16(selector,0x200+(f.Game.ORAS?0:16)+i*0x54)).All(x=>x),version+" Batch native starters synchronize selector and gift tables");
                check(reread.Entries.Where(e=>!e.Name.StartsWith("Inicial")).All(e=>EditorValues.Flatten(reread.Read(e.Id))["Species"]==1),version+" Batch starter action preserves other gifts and statics");
                options=EditorValues.Flatten(reread.RandomOptions);options["Species"]=0;options["Forms"]=0;options["ScaleLevels"]=1;options["LevelScale"]=200;options["LevelFlat"]=3;
                doc=Recipe(f,reread,"static-encounters.modify-levels",options);NativeBatchGen6Bridge.ExecuteSession(f.Session,Replay(doc),42);
                reread=new GameStaticEncounter6Service(f.Session);check(reread.Entries.All(e=>EditorValues.Flatten(reread.Read(e.Id))["Level"]==13),version+" Batch native levels include static gift and starter tables plus flat offset");
                var partial=doc.Section("ProgressiveApp")!;partial["StaticEncounters"]!["Scope"]=1;doc.SetSection("ProgressiveApp",partial);byte[] stable=File.ReadAllBytes(path);reject(()=>NativeBatchGen6Bridge.ExecuteSession(f.Session,Replay(doc),42),version+" native Batch rejects partial scope");
                check(File.ReadAllBytes(path).SequenceEqual(stable),version+" partial scope rejection leaves CRO unchanged");
            }
            using(var first=new Fixture(version))using(var second=new Fixture(version))
            {
                var a=Machine6Checks.Setup(first);var b=Machine6Checks.Setup(second);var editor=new GameMachine6Service(first.Session);var options=EditorValues.Flatten(editor.RandomOptions);options["HmRestriction"]=1;
                var doc=Recipe(first,editor,"tms.randomize",options);
                NativeBatchGen6Bridge.ExecuteSession(first.Session,Replay(doc),77);NativeBatchGen6Bridge.ExecuteSession(second.Session,Replay(doc),77);
                check(File.ReadAllBytes(a.Code).SequenceEqual(File.ReadAllBytes(b.Code))&&File.ReadAllBytes(a.Text).SequenceEqual(File.ReadAllBytes(b.Text)),version+" native Batch machines and descriptions reproduce the same seed");
                check(Gen6HmRestriction.Locate(File.ReadAllBytes(a.Code),first.Game.ORAS).Patched,version+" native Batch carries forgettable HM option");
                var invalid=doc.Section("ProgressiveApp")!;invalid["Machines"]!["Options"]!["FutureUnknown"]=1;doc.SetSection("ProgressiveApp",invalid);byte[] stable=File.ReadAllBytes(a.Code);
                reject(()=>NativeBatchGen6Bridge.ExecuteSession(first.Session,Replay(doc),77),version+" native Batch rejects unrepresented machine options");check(File.ReadAllBytes(a.Code).SequenceEqual(stable),version+" invalid option rejection preserves code");
            }
            using(var first=new Fixture(version))using(var second=new Fixture(version))
            {
                string a=Field(first),b=Field(second);var editor=new GameFieldItems6Service(first.Session);var doc=Recipe(first,editor,"field-items.randomize");
                NativeBatchGen6Bridge.ExecuteSession(first.Session,Replay(doc),123);NativeBatchGen6Bridge.ExecuteSession(second.Session,Replay(doc),123);
                check(File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)),version+" native Batch field items round-trip through original template and reproduce seed");
                editor=new GameFieldItems6Service(first.Session);
                check(editor.Entries.Select(e=>EditorValues.Flatten(editor.Read(e.Id))["Item"]).Order().SequenceEqual(new[]{155,156,157}),version+" native Batch field shuffle preserves item multiset");
                var items=new GameItemService(first.Session);doc=Recipe(first,items,"items.fix-economy");NativeBatchGen6Bridge.ExecuteSession(first.Session,Replay(doc),123);
                check(new pk3DS.Core.Structures.Item(ModuleChecks.Read(first,"item")[2]).BuyPrice==200,version+" native Batch economy applies historical Gen6 preset");
            }
            using(var first=new Fixture(version))using(var second=new Fixture(version))
            {
                var a=Mart6Checks.Setup(first);var b=Mart6Checks.Setup(second);var editor=new GameMart6Service(first.Session);var doc=Recipe(first,editor,"marts.randomize");
                NativeBatchGen6Bridge.ExecuteSession(first.Session,Replay(doc),99);NativeBatchGen6Bridge.ExecuteSession(second.Session,Replay(doc),99);
                check(File.ReadAllBytes(a.Code).SequenceEqual(File.ReadAllBytes(b.Code)),version+" native Batch marts reproduce full snapshot with fixed seed");
                editor=new GameMart6Service(first.Session);var options=EditorValues.Flatten(editor.RandomOptions);options["Operation"]=1;doc=Recipe(first,editor,"marts.add-rare-candies",options);
                NativeBatchGen6Bridge.ExecuteSession(first.Session,Replay(doc),99);check(new pk3DS.Core.Structures.Item(ModuleChecks.Read(first,"item")[50]).BuyPrice==10,version+" native Batch rare candy includes price and stock changes");
                options["Operation"]=0;doc=Recipe(first,editor,"marts.add-rare-candies",options);reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(doc),version+" Batch rejects mismatched mart operation");
            }
        }
    }
}
