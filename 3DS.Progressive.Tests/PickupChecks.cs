using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;

internal static class PickupChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);string tag=version+" Pickup ";bool gen7=f.Game.Generation==7;
            string path;byte[] source;byte[] table=[];byte[] other=[9,4,7,0,11];int at=0x400080;
            if(gen7)
            {
                table=new byte[4+24+3];BitConverter.GetBytes(3).CopyTo(table,0);
                BitConverter.GetBytes((ushort)17).CopyTo(table,4);BitConverter.GetBytes((ushort)18).CopyTo(table,16);
                table.AsSpan(6,10).Fill(25);table.AsSpan(18,10).Fill(75);new byte[]{0xFE,0xED,0xA5}.CopyTo(table,28);
                path=Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("pickup"));f.WriteGarc(path,[table,other],f.Game.GetGARCReference("pickup"));source=File.ReadAllBytes(path);
            }
            else
            {
                path=Path.Combine(f.Game.ExeFS,"code.bin");source=Enumerable.Repeat((byte)0xA5,0x401000).ToArray();
                for(int i=0;i<29;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(source,at+i*2);
                GamePickupService.Signature.CopyTo(source,at+58);File.WriteAllBytes(path,source);
            }
            var service=new GamePickupService(f.Session);var rows=service.Read();
            if(gen7)
            {
                var doc=GlobalTemplateDocument.Empty(f.Session.GameName,7);doc.SetSection("ProgressiveApp",new System.Text.Json.Nodes.JsonObject{["PartialSnapshot"]=true});doc.SetSection("Actions",new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject{["Id"]="pickup.randomize",["Parameters"]=new System.Text.Json.Nodes.JsonObject()}));var expected=service.Preview(service.Randomize(rows,77)).Files[0].Proposed();
                check(NativeRecipeCompletion.TryComplete(f.Session,doc)&&File.ReadAllBytes(path).SequenceEqual(source),tag+"native recipe validates without modifying source");GlobalEditorTemplateBridge.ValidateBatchReplay(doc);NativeBatchRomBridge.ApplyPickupSession(f.Session,77);check(File.ReadAllBytes(path).SequenceEqual(expected),tag+"native replay matches exact seed and preserves probabilities and other members");File.WriteAllBytes(path,source);service=new GamePickupService(f.Session);rows=service.Read();
            }
            check(rows.Count==(gen7?2:29)&&rows[0].Item==(gen7?17:1),tag+"vanilla layout decoded");
            rows[0].Item=35;if(gen7){rows[0].Rates[0]=40;rows[1].Rates[0]=60;}
            var proposal=service.Preview(rows);
            check(File.ReadAllBytes(path).SequenceEqual(source)&&service.Read()[0].Item!=(35),tag+"preview leaves source and live values unchanged");
            reject(()=>new GamePickupService(f.Session).Apply(proposal),tag+"foreign proposal rejected");
            service.Apply(proposal);
            check(service.Read()[0].Item==35&&(!gen7||service.Read()[0].Rates[0]==40),tag+"changes read back");
            check(f.Session.History.Read().Entries.Last().Module=="Pickup"&&f.Session.History.Read().Entries.Last().Files.Count==1,tag+"edit and file fingerprint recorded in persistent history");
            reject(()=>service.Apply(proposal),tag+"stale proposal rejected");
            byte[] changed=File.ReadAllBytes(path);
            if(gen7)
            {
                var lazy=new GARC.LazyGARC(changed);var expected=(byte[])table.Clone();expected[4]=35;expected[6]=40;expected[18]=60;
                check(lazy[0].SequenceEqual(expected)&&lazy[1].SequenceEqual(other),tag+"opaque trailer and other member preserved");
                var expanded=service.Read();expanded.Add(new(){Item=42,Group="Fila 03"});var expand=service.Preview(expanded);service.Apply(expand);
                lazy=new GARC.LazyGARC(File.ReadAllBytes(path));
                check(service.Read().Count==3&&lazy[0].Length==43&&lazy[0].AsSpan(40).SequenceEqual(table.AsSpan(28))&&lazy[1].SequenceEqual(other),tag+"add row preserves trailer and other member");
                expanded=service.Read();expanded.RemoveAt(2);var shrink=service.Preview(expanded);service.Apply(shrink);
                check(new GARC.LazyGARC(File.ReadAllBytes(path))[0].SequenceEqual(expected),tag+"remove row restores exact member size");
                var invalid=service.Read();invalid[0].Rates[3]++;
                reject(()=>service.Preview(invalid),tag+"non-100 column rejected without writes");
                invalid=service.Read();invalid[0].Rates[3]=-1;reject(()=>service.Preview(invalid),tag+"negative rate rejected");
            }
            else
            {
                byte[] expected=(byte[])source.Clone();BitConverter.GetBytes((ushort)35).CopyTo(expected,at);
                check(changed.SequenceEqual(expected),tag+"only two item bytes may change; probabilities preserved");
                var invalid=service.Read();invalid.RemoveAt(28);reject(()=>service.Preview(invalid),tag+"fixed Gen6 row count protected");
            }
            var original=service.Read();var random=service.Randomize(original);
            int[] pool=ThreeDS.Progressive.Services.ProgressiveLegacy.Randomizer.GetRandomItemList(f.Game);
            check(random.All(r=>pool.Contains(r.Item))&&random.Select((r,i)=>r.Rates.SequenceEqual(original[i].Rates)).All(x=>x),tag+"randomization uses original legal pool and preserves probabilities");
            check(random.Select(r=>r.Item).Distinct().Count()==random.Count,tag+"shuffled pool avoids repeats within one pass");
            var bad=service.Read();bad[0].Item=100000;reject(()=>service.Preview(bad),tag+"unknown item rejected");
            var pending=service.Preview(random);var external=File.ReadAllBytes(path);external[^1]^=1;File.WriteAllBytes(path,external);
            reject(()=>service.Apply(pending),tag+"external modification rejects pending apply");
            check(File.ReadAllBytes(path).SequenceEqual(external),tag+"external file preserved");
            if(!gen7)
            {
                source.AsSpan(at+58,GamePickupService.Signature.Length).Clear();File.WriteAllBytes(path,source);
                reject(()=>new GamePickupService(f.Session),tag+"missing signature rejected");
                GamePickupService.Signature.CopyTo(source,at+58);GamePickupService.Signature.CopyTo(source,at+0x100);File.WriteAllBytes(path,source);
                reject(()=>new GamePickupService(f.Session),tag+"duplicate signature rejected");
            }
            else
            {
                BitConverter.GetBytes(int.MaxValue).CopyTo(table,0);f.WriteGarc(path,[table,other],f.Game.GetGARCReference("pickup"));
                reject(()=>new GamePickupService(f.Session),tag+"invalid row count rejected");
            }
        }
    }
}
