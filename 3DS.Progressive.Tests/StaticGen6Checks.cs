using System.IO;
using pk3DS.Core;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class StaticGen6Checks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);
            bool oras=f.Game.ORAS;string tag=version+" Static Gen6 ";
            int stat=oras?0xF1B20:0xEE46C,gift=oras?0xF906C:0xF805C,size=oras?36:24;
            int staticCount=oras?59:13,giftCount=oras?37:19;
            byte[] field=Enumerable.Repeat((byte)0xA5,gift+giftCount*size+64).ToArray();
            "CRO0"u8.CopyTo(field.AsSpan(0x80));
            for(int i=0;i<staticCount;i++)
            {
                int at=stat+i*12;BitConverter.GetBytes((ushort)1).CopyTo(field,at);
                field[at+2]=0;field[at+3]=15;field[at+4]=field[at+5]=255;
                field[at+6]=0x83;field[at+7]=0xA3;
            }
            for(int i=0;i<giftCount;i++)
            {
                int at=gift+i*size;BitConverter.GetBytes((ushort)1).CopyTo(field,at);
                field[at+4]=0;field[at+5]=5;field[at+6]=0xA2;
                field[at+7]=field[at+8]=255;BitConverter.GetBytes(-1).CopyTo(field,at+12);field[at+16]=0;
                if(oras){field[at+18]=field[at+19]=0;field[at+20]=field[at+21]=0;}
                for(int n=0;n<6;n++)field[at+(oras?22:17)+n]=255;
            }
            string path=Path.Combine(f.Game.RomFS,"DllField.cro");File.WriteAllBytes(path,field);
            byte[] selector=Enumerable.Repeat((byte)0x5A,0x1000).ToArray();
            "CRO0"u8.CopyTo(selector.AsSpan(0x80));BitConverter.GetBytes(0x200).CopyTo(selector,0xB8);
            int start=oras?0x200:0x210;
            for(int i=0;i<(oras?12:6);i++)BitConverter.GetBytes((ushort)1).CopyTo(selector,start+i*0x54);
            string selectPath=Path.Combine(f.Game.RomFS,"DllPoke3Select.cro");File.WriteAllBytes(selectPath,selector);
            var editor=new GameStaticEncounter6Service(f.Session);
            check(editor.Entries.Count==staticCount+giftCount,tag+"table counts match vanilla");
            var values=EditorValues.Flatten(editor.Read(1));
            check(values["Shiny"]==1&&values["ShinyLock"]==1&&values["IV3"]==1&&values["IV3_1"]==1,tag+"independent packed flags decode correctly");
            editor.Save(1,values);
            check(File.ReadAllBytes(path).SequenceEqual(field),tag+"no-op preserves negative item sentinel and opaque bytes");
            values["Level"]=77;editor.Save(1,values);
            byte[] saved=File.ReadAllBytes(path);
            check(saved[stat+3]==77&&saved.Where((b,i)=>i!=stat+3).SequenceEqual(field.Where((b,i)=>i!=stat+3)),tag+"individual save changes only requested byte");
            int giftId=staticCount+1;values=EditorValues.Flatten(editor.Read(giftId));
            editor.Save(giftId,values);
            check(File.ReadAllBytes(path).SequenceEqual(saved),tag+"gift no-op preserves signed fields and item sentinel");
            values["Species"]=4;values["IV0"]=31;editor.Save(giftId,values);
            var expected=(byte[])saved.Clone();BitConverter.GetBytes((ushort)4).CopyTo(expected,gift);expected[gift+(oras?22:17)]=31;
            var expectedSelector=(byte[])selector.Clone();BitConverter.GetBytes((ushort)4).CopyTo(expectedSelector,start);
            check(File.ReadAllBytes(path).SequenceEqual(expected)&&File.ReadAllBytes(selectPath).SequenceEqual(expectedSelector),tag+"starter and selector synchronize atomically preserving other bytes");
            check(f.Session.History.Read().Entries.Last().Files.Count==2,tag+"starter history tracks both CROs");
            var options=EditorValues.Flatten(editor.RandomOptions);options["Species"]=options["Forms"]=0;options["ScaleLevels"]=1;options["LevelFlat"]=5;
            var preview=editor.Preview(options,[1]);
            check(File.ReadAllBytes(path).SequenceEqual(expected),tag+"preview writes nothing");
            editor.Apply(preview);check(EditorValues.Flatten(editor.Read(1))["Level"]==82,tag+"level-only preview commits and reads back");
            reject(()=>editor.Apply(preview),tag+"repeated proposal rejected");
            using var other=new Fixture(version);
            File.WriteAllBytes(Path.Combine(other.Game.RomFS,"DllField.cro"),expected);
            var otherEditor=new GameStaticEncounter6Service(other.Session);
            reject(()=>otherEditor.Apply(editor.Preview(options,[1])),tag+"cross-session preview rejected");
            var pending=editor.Preview(options,[1]);
            byte[] external=File.ReadAllBytes(path);external[^1]^=1;File.WriteAllBytes(path,external);
            reject(()=>editor.Apply(pending),tag+"external CRO write rejected");
            check(File.ReadAllBytes(path).SequenceEqual(external),tag+"external data preserved");
            editor=new GameStaticEncounter6Service(f.Session);
            options=EditorValues.Flatten(editor.RandomOptions);options["Forms"]=0;
            for(int g=1;g<=6;g++)options["G"+g]=0;options["Legends"]=options["Events"]=0;
            reject(()=>editor.Preview(options,[1]),tag+"empty species pool rejected");
            options["BasicStarter"]=1;options["StarterGeneration"]=1;
            int[] starterIndexes=oras?[0,1,2,28,29,30,31,32,33,34,35,36]:[0,1,2,3,4,5];
            int[] starterIds=starterIndexes.Select(index=>staticCount+index+1).ToArray();
            byte[] starterBefore=File.ReadAllBytes(path),selectorBefore=File.ReadAllBytes(selectPath);
            var starters=editor.Preview(options,starterIds);
            check(File.ReadAllBytes(path).SequenceEqual(starterBefore)&&File.ReadAllBytes(selectPath).SequenceEqual(selectorBefore),tag+"generation starter profile previews both CROs without writing");
            editor.Apply(starters);
            check(starterIndexes.All(index=>
            {
                int gen=oras?(index<3?3:index<=30?2:index<=33?4:5):index<3?6:1;
                return Legal.BasicStarters[gen-1].Contains(EditorValues.Flatten(editor.Read(staticCount+index+1))["Species"]);
            }),tag+"basic starters follow original group generations with normal generation filters disabled");
            byte[] selectorAfter=File.ReadAllBytes(selectPath);
            check(starterIndexes.Select((index,group)=>BitConverter.ToUInt16(selectorAfter,start+group*0x54)==
                EditorValues.Flatten(editor.Read(staticCount+index+1))["Species"]).All(x=>x),tag+"every generation starter group synchronizes selector and gifts");
            check(File.ReadAllBytes(path).AsSpan(stat,staticCount*12).SequenceEqual(starterBefore.AsSpan(stat,staticCount*12)),tag+"starter profile leaves static encounter table untouched");
            var global=GlobalTemplateDocument.Empty(version.ToString(),6);
            GlobalEditorTemplateBridge.Capture(global,editor,new(options,1),6);
            var restored=GlobalEditorTemplateBridge.Restore(global,new GameStaticEncounter6Service(f.Session),6)!;
            check(restored.Options["BasicStarter"]==1&&restored.Options["StarterGeneration"]==1,tag+"new starter profiles survive native Global Template round-trip");
            // Represent valid CRO regions so the native runner can also regenerate integrity hashes.
            foreach(string croPath in new[]{path,selectPath})
            {
                byte[] cro=File.ReadAllBytes(croPath);BitConverter.GetBytes(0x200).CopyTo(cro,0xB0);BitConverter.GetBytes(cro.Length-0x200).CopyTo(cro,0xB4);
                BitConverter.GetBytes(cro.Length).CopyTo(cro,0xB8);BitConverter.GetBytes(0).CopyTo(cro,0xBC);BitConverter.GetBytes(cro.Length).CopyTo(cro,0xC0);File.WriteAllBytes(croPath,cro);
            }
            // Selector table lookup uses the data offset, so preserve its original data segment.
            byte[] selectorRegions=File.ReadAllBytes(selectPath);BitConverter.GetBytes(0x200).CopyTo(selectorRegions,0xB8);BitConverter.GetBytes(selectorRegions.Length-0x200).CopyTo(selectorRegions,0xBC);BitConverter.GetBytes(0x200).CopyTo(selectorRegions,0xC0);File.WriteAllBytes(selectPath,selectorRegions);
            selectorAfter=selectorRegions;editor=new GameStaticEncounter6Service(f.Session);
            var independent=EditorValues.Flatten(editor.RandomOptions);independent["SeparateProfiles"]=1;
            foreach(string profile in GameStaticEncounter6Service.ProfileNames){independent[profile+".Species"]=0;independent[profile+".Forms"]=0;independent[profile+".ScaleLevels"]=1;}
            independent["Static.LevelScale"]=100;independent["Static.LevelFlat"]=3;
            independent["Gifts.LevelScale"]=200;independent["Gifts.LevelFlat"]=0;
            independent["Starters.Enabled"]=0;
            var all=editor.Entries.Select(e=>e.Id).ToArray();byte[] profileBefore=File.ReadAllBytes(path);
            var combined=editor.Preview(independent,all,55);
            check(File.ReadAllBytes(path).SequenceEqual(profileBefore),tag+"independent combined preview remains read only");
            editor.Apply(combined);
            check(Enumerable.Range(1,staticCount).All(id=>EditorValues.Flatten(editor.Read(id))["Level"]==Math.Min(100,profileBefore[stat+(id-1)*12+3]+3)),tag+"static profile uses independent flat levels");
            check(Enumerable.Range(0,giftCount).Where(i=>!starterIndexes.Contains(i)).All(i=>EditorValues.Flatten(editor.Read(staticCount+i+1))["Level"]==profileBefore[gift+i*size+5]*2),tag+"gift profile uses independent multiplier");
            check(starterIndexes.All(i=>File.ReadAllBytes(path).AsSpan(gift+i*size,size).SequenceEqual(profileBefore.AsSpan(gift+i*size,size)))&&File.ReadAllBytes(selectPath).SequenceEqual(selectorAfter),tag+"disabled starter profile preserves every starter and selector");
            global=GlobalTemplateDocument.Empty(version.ToString(),6);GlobalEditorTemplateBridge.Capture(global,editor,new(independent,0),6);
            global.SetSection("Actions",new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject{["Id"]="static-encounters.randomize",["Parameters"]=new System.Text.Json.Nodes.JsonObject()}));
            restored=GlobalEditorTemplateBridge.Restore(global,new GameStaticEncounter6Service(f.Session),6)!;
            check(restored.Options.OrderBy(p=>p.Key).SequenceEqual(independent.OrderBy(p=>p.Key)),tag+"all independent settings survive Global roundtrip");
            File.WriteAllBytes(path,profileBefore);NativeBatchGen6Bridge.ExecuteSession(f.Session,global.ToJson(),123);
            check(File.ReadAllBytes(path).SequenceEqual(CroIntegrity.Rebuild(combined.Files.Single(p=>p.Path==path).Proposed())),tag+"native replay matches joint profile proposal");
            var legacy=global.Section("ProgressiveApp")!.AsObject();var legacyOptions=legacy["StaticEncounters"]!["Options"]!.AsObject();
            foreach(string key in legacyOptions.Select(p=>p.Key).Where(GameStaticEncounter6Service.IsProfileOption).ToArray())legacyOptions.Remove(key);
            legacyOptions["Species"]=0;legacyOptions["Forms"]=0;legacyOptions["ScaleLevels"]=0;
            global.SetSection("ProgressiveApp",legacy);NativeBatchGen6Bridge.ValidateSession(f.Session,global);
            check(true,tag+"old shared snapshots migrate new profile defaults during replay");
            expectedSelector=File.ReadAllBytes(selectPath);
            File.WriteAllBytes(path,external[..0x180]);
            reject(()=>new GameStaticEncounter6Service(f.Session),tag+"truncated tables rejected");
            check(File.ReadAllBytes(selectPath).SequenceEqual(expectedSelector),tag+"failed operations preserve starter selector");
        }
    }
}
