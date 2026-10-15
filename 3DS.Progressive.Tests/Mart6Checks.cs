using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class Mart6Checks
{
    internal static (string Code,string Item,byte[] Bytes,byte[][] Items,int Offset,int[] Counts) Setup(Fixture f,bool patched=false)
    {
        byte[] code=Enumerable.Repeat((byte)0xA5,0x401000).ToArray();byte[] signature=patched?GameMart6Service.Patched:GameMart6Service.Vanilla;
        signature.CopyTo(code,0x400100);int at=0x400100+signature.Length;int[] counts=f.Game.ORAS?GameMart6Service.CountsORAS:GameMart6Service.CountsXY;
        for(int i=0;i<counts.Sum();i++)BitConverter.GetBytes((ushort)(10+i%80)).CopyTo(code,at+2*i);
        int special=at+2*counts.Take(10).Sum();BitConverter.GetBytes((ushort)55).CopyTo(code,special);BitConverter.GetBytes((ushort)328).CopyTo(code,special+2);
        string path=Path.Combine(f.Game.ExeFS,"code.bin");File.WriteAllBytes(path,code);
        byte[][] items=Enumerable.Range(0,80).Select(i=>{var item=new Item{BuyPrice=i*10};byte[] bytes=item.Write();bytes[^1]=0xAB;return bytes;}).ToArray();
        string itemPath=Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("item"));f.WriteGarc(itemPath,items,f.Game.GetGARCReference("item"));
        return(path,itemPath,code,items,at,counts);
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})foreach(bool patched in new[]{false,true})
        {
            using var f=new Fixture(version);var fixture=Setup(f,patched);string tag=$"{version} tiendas {(patched?"rom2":"rom")} ";
            var service=new GameMart6Service(f.Session);int last=fixture.Counts.Length;
            check(service.Entries.Count==last&&EditorValues.Flatten(service.Read(last)).Count==fixture.Counts[^1],tag+"vanilla count and last store decoded");
            var values=EditorValues.Flatten(service.Read(last));values["Item0"]=23;service.Save(last,values);
            byte[] expected=(byte[])fixture.Bytes.Clone();int lastAt=fixture.Offset+fixture.Counts.Take(last-1).Sum()*2;BitConverter.GetBytes((ushort)23).CopyTo(expected,lastAt);
            check(File.ReadAllBytes(fixture.Code).SequenceEqual(expected),tag+"manual edit changes one item ushort only");
            check(f.Session.History.Read().Entries.Last().Module==service.Title,tag+"manual edit recorded in history");
            values["Item0"]=100000;reject(()=>service.Save(last,values),tag+"invalid item rejected");
            var options=EditorValues.Flatten(service.RandomOptions);int[] all=service.Entries.Select(e=>e.Id).ToArray();var proposal=service.Preview(options,all);
            check(File.ReadAllBytes(fixture.Code).SequenceEqual(expected),tag+"random preview is read-only");
            byte[] proposed=proposal.Files.First(p=>p.Path==fixture.Code).Proposed();int specialAt=fixture.Offset+fixture.Counts.Take(10).Sum()*2;
            check(proposed.AsSpan(fixture.Offset,specialAt-fixture.Offset).SequenceEqual(expected.AsSpan(fixture.Offset,specialAt-fixture.Offset)),tag+"special-only preserves regular and unused/before-Pokedex stores");
            check(BitConverter.ToUInt16(proposed,specialAt)==55&&BitConverter.ToUInt16(proposed,specialAt+2)==328,tag+"X items and TM/HM slots protected");
            int[] pool=ThreeDS.Progressive.Services.ProgressiveLegacy.Randomizer.GetRandomItemList(f.Game);
            check(Enumerable.Range(2,(fixture.Counts.Sum()-fixture.Counts.Take(10).Sum())-2).All(i=>pool.Contains(BitConverter.ToUInt16(proposed,specialAt+2*i))||Enumerable.Range(328,100).Contains(BitConverter.ToUInt16(expected,specialAt+2*i))),tag+"randomized slots use original legal pool");
            reject(()=>new GameMart6Service(f.Session).Apply(proposal),tag+"foreign proposal rejected");
            options["Operation"]=1;byte[] itemBefore=File.ReadAllBytes(fixture.Item);var candy=service.Preview(options,all);
            check(File.ReadAllBytes(fixture.Item).SequenceEqual(itemBefore)&&File.ReadAllBytes(fixture.Code).SequenceEqual(expected),tag+"candy preview changes neither file");
            service.Apply(candy);byte[] candyCode=(byte[])expected.Clone();int regular=f.Game.ORAS?10:9,offset=fixture.Offset;
            for(int i=0;i<regular;i++){BitConverter.GetBytes((ushort)50).CopyTo(candyCode,offset+2*(fixture.Counts[i]-1));offset+=2*fixture.Counts[i];}
            check(File.ReadAllBytes(fixture.Code).SequenceEqual(candyCode),tag+"candy modifies last slot of regular stores only");
            var itemRecords=new GARC.MemGARC(File.ReadAllBytes(fixture.Item)).Files;byte[] priceExpected=(byte[])fixture.Items[50].Clone();BitConverter.GetBytes((ushort)1).CopyTo(priceExpected,0);
            check(itemRecords[50].SequenceEqual(priceExpected)&&new Item(itemRecords[50]).BuyPrice==10&&itemRecords.Length==80&&Enumerable.Range(0,80).Where(i=>i!=50).All(i=>itemRecords[i].SequenceEqual(fixture.Items[i])),tag+"price uses vanilla units and preserves opaque bytes and other items");
            check(f.Session.History.Read().Entries.Last().Files.Count==2,tag+"candy inventory and price share one history commit");
            reject(()=>service.Apply(candy),tag+"stale candy proposal rejected");
            candy=service.Preview(options,all);check(candy.Changes.Count==0&&candy.Files.All(p=>!p.Changed),tag+"repeated candy action is byte-idempotent");
            reject(()=>service.Preview(options,[last]),tag+"candy requires selected regular store");
            options["Operation"]=0;options["SpecialOnly"]=0;options["KeepX"]=0;proposal=service.Preview(options,[11]);proposed=proposal.Files.First(p=>p.Path==fixture.Code).Proposed();
            check(BitConverter.ToUInt16(proposed,specialAt+2)==328&&proposed.AsSpan(fixture.Offset,specialAt-fixture.Offset).SequenceEqual(candyCode.AsSpan(fixture.Offset,specialAt-fixture.Offset)),tag+"explicit scope leaves other stores unchanged; TM remains protected");
            options["Operation"]=1;candy=service.Preview(options,[1]);byte[] external=File.ReadAllBytes(fixture.Item);external[^1]^=1;File.WriteAllBytes(fixture.Item,external);
            reject(()=>service.Apply(candy),tag+"external item change blocks inventory apply even if price unchanged");check(File.ReadAllBytes(fixture.Code).SequenceEqual(candyCode)&&File.ReadAllBytes(fixture.Item).SequenceEqual(external),tag+"both external and code bytes preserved");
            File.WriteAllBytes(fixture.Item,itemBefore);service=new GameMart6Service(f.Session);candy=service.Preview(options,[1]);candyCode[^1]^=1;File.WriteAllBytes(fixture.Code,candyCode);
            reject(()=>service.Apply(candy),tag+"external code change blocks price apply");check(File.ReadAllBytes(fixture.Item).SequenceEqual(itemBefore),tag+"price preserved after code conflict");
            var bad=(byte[])fixture.Bytes.Clone();GameMart6Service.Vanilla.CopyTo(bad,0x400500);File.WriteAllBytes(fixture.Code,bad);reject(()=>new GameMart6Service(f.Session),tag+"duplicate marker rejected");
            File.WriteAllBytes(fixture.Code,new byte[512]);reject(()=>new GameMart6Service(f.Session),tag+"truncated code rejected");
            byte[] missing=(byte[])fixture.Bytes.Clone();missing.AsSpan(0x400100,(patched?GameMart6Service.Patched:GameMart6Service.Vanilla).Length).Clear();File.WriteAllBytes(fixture.Code,missing);reject(()=>new GameMart6Service(f.Session),tag+"missing marker rejected");
            File.WriteAllBytes(fixture.Code,fixture.Bytes.Concat(new byte[]{0}).ToArray());reject(()=>new GameMart6Service(f.Session),tag+"non-decompressed alignment rejected");
            byte[] unknown=(byte[])fixture.Bytes.Clone();BitConverter.GetBytes(ushort.MaxValue).CopyTo(unknown,fixture.Offset);File.WriteAllBytes(fixture.Code,unknown);reject(()=>new GameMart6Service(f.Session),tag+"unknown source item rejected");
            File.WriteAllBytes(fixture.Code,fixture.Bytes);fixture.Items[50]=new byte[2];f.WriteGarc(fixture.Item,fixture.Items,f.Game.GetGARCReference("item"));service=new GameMart6Service(f.Session);byte[] truncatedItem=File.ReadAllBytes(fixture.Item);
            reject(()=>service.Preview(options,[1]),tag+"truncated candy record rejected");check(File.ReadAllBytes(fixture.Code).SequenceEqual(fixture.Bytes)&&File.ReadAllBytes(fixture.Item).SequenceEqual(truncatedItem),tag+"truncated candy rejection writes neither file");
        }
    }
}
