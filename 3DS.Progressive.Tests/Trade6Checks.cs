using System.IO;
using pk3DS.Core;
using pk3DS.Core.Randomizers;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class Trade6Checks
{
    internal static (string Path,byte[] Bytes) Setup(Fixture f)
    {
        bool oras=f.Game.ORAS;byte[] code=Enumerable.Repeat((byte)0xA5,0x487000).ToArray();
        int[] requests=oras?GameTrade6Service.ORASRequests:GameTrade6Service.XYRequests,offers=oras?GameTrade6Service.ORASOffers:GameTrade6Service.XYOffers;
        ushort[] expected=oras?GameTrade6Service.ORASExpected:GameTrade6Service.XYExpected;
        for(int i=0;i<requests.Length;i++)BitConverter.GetBytes(expected[i]).CopyTo(code,requests[i]);
        for(int i=0;i<offers.Length;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(code,offers[i]);
        if(oras)SetSelectorLayout(code,GameTrade6Service.SelectorOffsets);
        string path=Path.Combine(f.Game.ExeFS,"code.bin");File.WriteAllBytes(path,code);return(path,code);
    }
    internal static void SetSelectorLayout(byte[] code,int[] offsets)
    {
        byte[][] before=[Convert.FromHexString("0100A0E31080BDE80000A0E31080BDE8F0412DE9"),Convert.FromHexString("485080E59C4040E24C50C0E5EC009FE5")];
        byte[][] after=[Convert.FromHexString("0070A0E10260A0E10150A0E3BC2EC0E1"),Convert.FromHexString("BC7EC4E1")];
        for(int i=0;i<2;i++){before[i].CopyTo(code,offsets[i]-before[i].Length);GameTrade6Service.SelectorClean[i].CopyTo(code,offsets[i]);after[i].CopyTo(code,offsets[i]+4);}
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);var data=Setup(f);bool oras=f.Game.ORAS;string tag=version+" Progressive trades ";
            int[] requests=oras?GameTrade6Service.ORASRequests:GameTrade6Service.XYRequests,offers=oras?GameTrade6Service.ORASOffers:GameTrade6Service.XYOffers;
            var service=new GameTrade6Service(f.Session);var options=new Dictionary<string,int>{{"Offers",0}};var preview=service.Preview(options,[1]);
            check(File.ReadAllBytes(data.Path).SequenceEqual(data.Bytes),tag+"preview writes nothing");
            byte[] expected=(byte[])data.Bytes.Clone();foreach(int at in requests)expected.AsSpan(at,2).Clear();
            if(oras)for(int i=0;i<2;i++)GameTrade6Service.SelectorPatched[i].CopyTo(expected,GameTrade6Service.SelectorOffsets[i]);
            service.Apply(preview);check(File.ReadAllBytes(data.Path).SequenceEqual(expected),tag+"only requests and required selectors changed");
            check(f.Session.History.Read().Entries.Last().Files.Count==1,tag+"patch and selector committed in one history entry");
            int count=f.Session.History.Read().Entries.Count;service.Apply(service.Preview(options,[1]));
            check(File.ReadAllBytes(data.Path).SequenceEqual(expected)&&f.Session.History.Read().Entries.Count==count,tag+"idempotent enable preserves bytes and history");
            var values=EditorValues.Flatten(service.Read(1));values["Offer0"]=25;service.Save(1,values);BitConverter.GetBytes((ushort)25).CopyTo(expected,offers[0]);
            check(File.ReadAllBytes(data.Path).SequenceEqual(expected),tag+"manual offer changes exactly one ushort");
            values=EditorValues.Flatten(service.Read(1));values["Request0"]=1;reject(()=>service.Save(1,values),tag+"read-only requests protected");
            reject(()=>service.Preview(options,[]),tag+"empty scope rejected");reject(()=>service.Preview(options,[2]),tag+"foreign scope rejected");
            options["Offers"]=2;reject(()=>service.Preview(options,[1]),tag+"invalid options rejected");options["Offers"]=1;
            var random=service.Preview(options,[1]);var filter=new SpeciesRandomizer(f.Game){rBST=false,rEXP=false,rType=false,L=false,E=false,Shedinja=false};var pool=filter.GetAllowedSpeciesPool();
            service.Apply(random);var after=File.ReadAllBytes(data.Path);
            check(offers.All(at=>pool.Contains(BitConverter.ToUInt16(after,at))),tag+"random offers use original Progressive legal pool");
            check(offers.All(at=>BitConverter.ToUInt16(after,at)!=BitConverter.ToUInt16(expected,at)),tag+"random offers differ from original species");
            foreach(int at in offers)expected.AsSpan(at,2).CopyTo(after.AsSpan(at,2));
            check(after.SequenceEqual(expected),tag+"randomization preserves all unrelated code bytes");
            byte[] unknown=(byte[])expected.Clone();BitConverter.GetBytes(ushort.MaxValue).CopyTo(unknown,offers[0]);unknown.AsSpan(offers[1],2).Clear();File.WriteAllBytes(data.Path,unknown);
            service=new GameTrade6Service(f.Session);service.Apply(service.Preview(options,[1]));after=File.ReadAllBytes(data.Path);
            check(BitConverter.ToUInt16(after,offers[0])==ushort.MaxValue&&BitConverter.ToUInt16(after,offers[1])==0,tag+"unknown and empty original offers are preserved");
            values=EditorValues.Flatten(service.Read(1));values["Offer0"]=ushort.MaxValue-1;reject(()=>service.Save(1,values),tag+"new unknown offer rejected");
            preview=service.Preview(new Dictionary<string,int>{{"Offers",0}},[1]);after[^1]^=1;File.WriteAllBytes(data.Path,after);
            reject(()=>service.Apply(preview),tag+"external modification blocks save");check(File.ReadAllBytes(data.Path).SequenceEqual(after),tag+"external bytes survive rejection");
            reject(()=>service.Apply(new EditorProposal(Guid.NewGuid(),"foreign",0,[],[],new Dictionary<string,string>())),tag+"foreign proposal rejected");
            byte[] bad=(byte[])data.Bytes.Clone();BitConverter.GetBytes((ushort)1).CopyTo(bad,requests[0]);File.WriteAllBytes(data.Path,bad);
            reject(()=>new GameTrade6Service(f.Session),tag+"unknown request signature rejected");
            foreach(int randomOffers in new[]{0,1})
            {
                File.WriteAllBytes(data.Path,data.Bytes);service=new GameTrade6Service(f.Session);
                var nativeOptions=new Dictionary<string,int>{{"Offers",randomOffers}};
                var doc=GlobalTemplateDocument.Empty(version.ToString(),6);doc.SetSection("ProgressiveApp",new System.Text.Json.Nodes.JsonObject{["PartialSnapshot"]=true});
                GlobalEditorTemplateBridge.Capture(doc,service,new(nativeOptions,0),6);
                var actions=new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject{["Id"]=randomOffers==1?"trades.randomize-offers":"trades.accept-any",["Parameters"]=new System.Text.Json.Nodes.JsonObject()});
                if(randomOffers==1)actions.Add(new System.Text.Json.Nodes.JsonObject{["Id"]="trades.accept-any",["Parameters"]=new System.Text.Json.Nodes.JsonObject()});doc.SetSection("Actions",actions);
                check(NativeRecipeCompletion.TryComplete(f.Session,doc)&&File.ReadAllBytes(data.Path).SequenceEqual(data.Bytes),tag+"native recipe completes without RandSettings and preserves source");
                var proposal=service.Preview(nativeOptions,[1],86+1009);NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),86);
                check(File.ReadAllBytes(data.Path).SequenceEqual(proposal.Files.Single().Proposed()),tag+"native replay matches seeded proposal and executes combined trade actions once");
                var missing=doc.Clone();missing.SetSection("ProgressiveApp",new System.Text.Json.Nodes.JsonObject{["PartialSnapshot"]=true});
                check(!NativeRecipeCompletion.TryComplete(f.Session,missing),tag+"action-only trade recipe cannot be marked natively complete");
                var mismatch=doc.Clone();var snapshot=mismatch.Section("ProgressiveApp")!;snapshot["Trades"]!["Options"]!["Offers"]=1-randomOffers;mismatch.SetSection("ProgressiveApp",snapshot);
                reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(mismatch),tag+"native trade flags must match selected actions");
            }

            if(oras){bad=(byte[])data.Bytes.Clone();bad[GameTrade6Service.SelectorOffsets[1]]^=1;File.WriteAllBytes(data.Path,bad);reject(()=>new GameTrade6Service(f.Session),tag+"second selector incompatibility blocks complete operation");}
            File.WriteAllBytes(data.Path,new byte[512]);reject(()=>new GameTrade6Service(f.Session),tag+"truncated binary rejected");
        }
        using(var sapphire=new Fixture(GameVersion.ORAS))
        {
            var data=Setup(sapphire);byte[] code=(byte[])data.Bytes.Clone();code.AsSpan(0x3196A0,0x40).Fill(0xA5);code.AsSpan(0x319F10,0x40).Fill(0xA5);
            int[] offsets=[0x3196C0,0x319F30];SetSelectorLayout(code,offsets);File.WriteAllBytes(data.Path,code);
            check(Gen6TradeSelectorProfile.Resolve(code).SequenceEqual(offsets),"AS shifted selectors recognized by paired context");
            var service=new GameTrade6Service(sapphire.Session);service.Apply(service.Preview(new Dictionary<string,int>{{"Offers",0}},[1]));
            byte[] expected=(byte[])code.Clone();foreach(int at in GameTrade6Service.ORASRequests)expected.AsSpan(at,2).Clear();for(int i=0;i<2;i++)GameTrade6Service.SelectorPatched[i].CopyTo(expected,offsets[i]);
            check(File.ReadAllBytes(data.Path).SequenceEqual(expected),"AS patch changes only requests and resolved selectors");
            service=new GameTrade6Service(sapphire.Session);service.Apply(service.Preview(new Dictionary<string,int>{{"Offers",0}},[1]));
            check(File.ReadAllBytes(data.Path).SequenceEqual(expected),"AS patched layout remains idempotent");
            var bad=(byte[])code.Clone();bad[offsets[0]-4]^=1;reject(()=>Gen6TradeSelectorProfile.Resolve(bad),"AS unknown surrounding instruction rejected");
            bad=(byte[])code.Clone();bad.AsSpan(0x319F10,0x40).Fill(0xA5);SetSelectorLayout(bad,[0x3196C0,0x319F38]);reject(()=>Gen6TradeSelectorProfile.Resolve(bad),"OR AS mixed overlapping layout rejected");
        }
        var originalAssembly=System.Reflection.Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));
        var apply=originalAssembly.GetType("pk3DS.WinForms.Gen6TradePatcher",true)!.GetMethod("ApplyGen6TradePatch")!;
        foreach(string name in new[]{"PokemonX","PokemonY","PokemonOR","PokemonAS"})
        {
            string source=Path.Combine(@"D:\Nintendo\3DS\Juegos",name,"ExtractedExeFS","code.bin");if(!File.Exists(source))continue;
            byte[] sourceBytes=File.ReadAllBytes(source);using var native=new Fixture(name is "PokemonOR" or "PokemonAS"?GameVersion.ORAS:GameVersion.XY);using var legacy=new Fixture(native.Game.Version);
            string nativePath=Path.Combine(native.Game.ExeFS,"code.bin"),legacyPath=Path.Combine(legacy.Game.ExeFS,"code.bin");File.WriteAllBytes(nativePath,sourceBytes);File.WriteAllBytes(legacyPath,sourceBytes);
            var service=new GameTrade6Service(native.Session);var proposal=service.Preview(new Dictionary<string,int>{{"Offers",0}},[1]);
            check(File.ReadAllBytes(nativePath).SequenceEqual(sourceBytes),name+" real trade preview writes nothing on private copy");service.Apply(proposal);
            byte[] exact=(byte[])sourceBytes.Clone();foreach(int at in native.Game.ORAS?GameTrade6Service.ORASRequests:GameTrade6Service.XYRequests)exact.AsSpan(at,2).Clear();
            if(native.Game.ORAS){int[] selector=name=="PokemonAS"?[0x3196C0,0x319F30]:[0x3196C8,0x319F38];for(int i=0;i<2;i++)GameTrade6Service.SelectorPatched[i].CopyTo(exact,selector[i]);}
            check(File.ReadAllBytes(nativePath).SequenceEqual(exact),name+" real patch changes only independently expected request and selector bytes");
            apply.Invoke(null,new object[]{legacy.Game.ExeFS,legacy.Game,false});
            check(File.ReadAllBytes(nativePath).SequenceEqual(File.ReadAllBytes(legacyPath)),name+" native and Batch trade patches match on private copies of real code");
            service=new GameTrade6Service(native.Session);service.Apply(service.Preview(new Dictionary<string,int>{{"Offers",0}},[1]));
            check(File.ReadAllBytes(source).SequenceEqual(sourceBytes),name+" original ROM code preserved");
        }
        using var gen7=new Fixture(GameVersion.USUM);reject(()=>new GameTrade6Service(gen7.Session),"Progressive Gen6 trades rejects Gen7");
    }
}
