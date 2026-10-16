using System.IO;
using System.Runtime.InteropServices;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Services;

internal static class Economy6Checks
{
    internal static void Setup(Fixture f)
    {
        byte[][] records=Enumerable.Range(0,1000).Select(i=>{var item=new Item(Enumerable.Repeat((byte)0xA5,Marshal.SizeOf<Item>()).ToArray());item.BuyPrice=12340;return item.Write();}).ToArray();
        f.WriteGarc(Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("item")),records,f.Game.GetGARCReference("item"));
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var expected=Enumerable.Range(328,100).Concat(new[]{618,619,620,690,691,692,693,694,701,737}).ToDictionary(id=>id,_=>1000);
        foreach(var pair in new (int Id,int Price)[]{(2,200),(3,150),(4,100),(76,50),(77,50),(79,50),(93,7500),(565,10),(566,10),(567,10),(568,10),(569,10),(570,10)})expected[pair.Id]=pair.Price;
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);Setup(f);string tag=version+" Fix Economy ";string path=Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("item"));
            byte[] original=File.ReadAllBytes(path);var before=new GARC.MemGARC(original).Files;var service=new GameItemService(f.Session);var rows=service.PriceRows();
            check(rows.Count(r=>r.Included)==123&&rows.Where(r=>r.Included).All(r=>expected.TryGetValue(r.Id,out int price)&&price==r.TargetBuy),tag+"all 123 historical targets match original preset");
            check(rows.Where(r=>!r.Included).All(r=>r.TargetBuy==12340&&r.Source=="Actual"),tag+"items outside preset unchanged and excluded");
            check(rows.Where(r=>r.Included).All(r=>r.Source=="Progressive Gen6 histórico")&&service.EconomyPreset.Contains("Gen6"),tag+"Gen6 provenance distinct from UPR-ZX Gen7");
            var proposal=service.PreviewEconomy(rows);check(proposal.Changes.Count==123&&File.ReadAllBytes(path).SequenceEqual(original),tag+"preview covers historical targets without writing");service.Apply(proposal);
            var after=new GARC.MemGARC(File.ReadAllBytes(path)).Files;
            check(after.Select((bytes,id)=>new Item(bytes).BuyPrice==(expected.GetValueOrDefault(id,12340))).All(x=>x),tag+"every output price verified");
            check(after.Where((bytes,id)=>!bytes.AsSpan(2).SequenceEqual(before[id].AsSpan(2))).Count()==0,tag+"all non-price bytes preserved exactly");
            check(f.Session.History.Read().Entries.Last().Parameters["Preset"]=="Progressive Gen6 histórico",tag+"history records correct preset");
            int count=f.Session.History.Read().Entries.Count;service.Apply(service.PreviewEconomy(service.PriceRows()));check(f.Session.History.Read().Entries.Count==count,tag+"repeat preset does not add history");
            rows=service.PriceRows();var first=rows.First(r=>r.Id==2);first.TargetBuy=210;first.Included=false;var third=rows.First(r=>r.Id==3);third.TargetBuy=160;service.Apply(service.PreviewEconomy(rows));
            after=new GARC.MemGARC(File.ReadAllBytes(path)).Files;check(new Item(after[2]).BuyPrice==200&&new Item(after[3]).BuyPrice==160,tag+"custom target and excluded rows respected");
            rows=service.PriceRows();rows.First(r=>r.Id==2).TargetBuy=201;reject(()=>service.PreviewEconomy(rows),tag+"nonrepresentable price rejected");
            reject(()=>service.PreviewEconomy([rows[0],rows[0]]),tag+"duplicate rows rejected");
            proposal=service.PreviewEconomy(service.PriceRows());byte[] external=File.ReadAllBytes(path);external[^1]^=1;File.WriteAllBytes(path,external);reject(()=>service.Apply(proposal),tag+"external change blocks save");check(File.ReadAllBytes(path).SequenceEqual(external),tag+"external bytes preserved");
        }
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);Setup(f);var service=new GameItemService(f.Session);var rows=service.PriceRows();
            check(rows.First(r=>r.Id==2).TargetBuy==800&&rows.First(r=>r.Id==4).TargetBuy==200&&service.EconomyPreset=="UPR-ZX Gen7 + Progressive",version+" economy retains original Gen7 defaults");
        }
    }
}
