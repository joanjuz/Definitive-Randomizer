using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class MartSmChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var f=new Fixture(GameVersion.SM);
        byte[] source=Enumerable.Repeat((byte)0xA5,0x6200).ToArray();"CRO0"u8.CopyTo(source.AsSpan(0x80));
        GameMartService.SmRegularSignature.CopyTo(source,0x5100);GameMartService.SmBpSignature.CopyTo(source,0x5800);
        check(GameMartService.SmRegularSignature.SequenceEqual(Convert.FromHexString("2D0000003B0000002F0000003D0000000000000000000000FFFFFFFF00000000100000000E0000000000000000000100")),"SM signature matches independently recorded vanilla leading table");
        BitConverter.GetBytes(0x200).CopyTo(source,0xB0);BitConverter.GetBytes(source.Length-0x200).CopyTo(source,0xB4);BitConverter.GetBytes(source.Length).CopyTo(source,0xB8);BitConverter.GetBytes(0).CopyTo(source,0xBC);BitConverter.GetBytes(source.Length).CopyTo(source,0xC0);
        int regular=0x5100+GameMartService.SmRegularSignature.Length,bp=0x5800+GameMartService.SmBpSignature.Length;
        string[] names=f.Game.GetText(TextName.ItemNames);
        string[] aliases=["Health Wing","Muscle Wing","Resist Wing","Genius Wing","Clever Wing","Swift Wing","Potion","Antidote","Paralyze Heal","Awakening","Burn Heal","Ice Heal","Super Potion","Revive","Hyper Potion","Rare Candy","Heart Scale","Mega Ring"];
        for(int i=0;i<aliases.Length;i++)names[i+1]=aliases[i];
        f.Game.GameTextStrings[f.Game.GameText.First(t=>t.Name==TextName.ItemNames).Index]=names;
        var project=Fixture.CreateSession(f.Game,f.Root,f.PersonalPath);
        int cursor=regular;
        for(int shop=0;shop<24;shop++)for(int slot=0;slot<GameMartService.SmRegularCounts[shop];slot++,cursor+=2)BitConverter.GetBytes((ushort)(slot<6?slot+7:slot==6?13:slot==7?14:slot==8?15:40)).CopyTo(source,cursor);
        cursor=bp;for(int i=0;i<GameMartService.SmBpCounts.Sum();i++,cursor+=4){BitConverter.GetBytes((ushort)(i==0?645:40)).CopyTo(source,cursor);BitConverter.GetBytes((ushort)48).CopyTo(source,cursor+2);}
        string path=Path.Combine(f.Game.RomFS,"Shop.cro");File.WriteAllBytes(path,source);
        byte[][] items=Enumerable.Range(0,1000).Select(_=>{var item=new Item(new byte[0x30]);item.BuyPrice=500;return item.Write();}).ToArray();
        string itemPath=Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("item"));f.WriteGarc(itemPath,items,f.Game.GetGARCReference("item"));
        byte[] itemBefore=File.ReadAllBytes(itemPath);
        var editor=new GameMartService(project);
        check(editor.RegularShops.Count==24&&editor.BpShops.Count==6&&!editor.SupportsExpansion,"SM exposes original 24 normal and six BP shops with fixed sizes");
        check(editor.Prepare().Output.SequenceEqual(source),"SM no-op preserves entire Shop CRO");
        editor.RegularShops[0].Rows.Add(new(99,1));reject(()=>editor.Prepare(),"SM rejects structural expansion before saving");editor.RegularShops[0].Rows.RemoveAt(editor.RegularShops[0].Rows.Count-1);
        editor.RegularShops[0].Rows[0].ItemId=41;editor.BpShops[0].Rows[0].Price=123;
        var manual=editor.Prepare();var expected=(byte[])source.Clone();BitConverter.GetBytes((ushort)41).CopyTo(expected,regular);BitConverter.GetBytes((ushort)123).CopyTo(expected,bp+2);
        check(manual.Output.SequenceEqual(CroIntegrity.Rebuild(expected))&&File.ReadAllBytes(path).SequenceEqual(source),"SM manual preview edits requested item and BP bytes with integrity hashes");
        editor=new GameMartService(project);editor.AddRareCandies();var candy=editor.Prepare();
        check(editor.RegularShops.Take(8).All(s=>s.Rows[^1].ItemId==16)&&editor.RegularShops.Select(s=>s.Rows.Count).SequenceEqual(GameMartService.SmRegularCounts),"SM rare candies replace original final slots without expansion");
        check(candy.ItemOutput is not null&&new Item(new GARC.MemGARC(candy.ItemOutput).Files[16]).BuyPrice==10,"SM candy preset includes atomic item price patch");
        for(int seed=0;seed<16;seed++)
        {
            var combined=new GameMartService(project);var combinedPreview=NativeGen7MartBridge.Prepare(combined,new(RandomRegular:true,RandomBP:true,Candies:true,Vitamins:true,BanCapsule:true),seed);
            check(combined.RegularShops.Take(8).All(shop=>shop.Rows.Take(6).Select(r=>r.ItemId).SequenceEqual(Enumerable.Range(1,6))&&shop.Rows[^1].ItemId==16),"SM combined random/candy/EV preserves deterministic preset slots seed "+seed);
            check(combinedPreview.Output.Length==source.Length&&combined.RegularShops.Select(shop=>shop.Rows.Count).SequenceEqual(GameMartService.SmRegularCounts),"SM combined presets preserve fixed shop structure seed "+seed);
        }
        editor=new GameMartService(project);editor.AddVitamins252();var training=editor.Prepare();
        check(editor.RegularShops.Take(8).All(s=>s.Rows.Take(6).Select(r=>r.ItemId).SequenceEqual(Enumerable.Range(1,6))),"SM training preset installs six wings from own localized item names");
        check(editor.RegularShops.Skip(5).Take(3).All(s=>s.Rows[8].ItemId==18)&&editor.RegularShops.Skip(3).Take(5).All(s=>s.Rows[6].ItemId==17),"SM training preserves trial gates for Mega Ring and Heart Scale");
        check(editor.RegularShops.Take(3).All(s=>s.Rows[6].ItemId==13)&&editor.RegularShops.Skip(8).SelectMany(s=>s.Rows).All(r=>r.ItemId is not 1 and not 2 and not 18),"SM early Super Potion and special shops remain unchanged");
        check(training.ItemOutput is not null&&new Item(new GARC.MemGARC(training.ItemOutput).Files[18]).BuyPrice==10,"SM training patches Mega Ring price without USUM vitamin flags");
        var a=new GameMartService(project);var b=new GameMartService(project);a.RandomizeShops(false,seed:13);b.RandomizeShops(false,seed:13);
        check(a.Prepare().Output.SequenceEqual(b.Prepare().Output),"SM native shop randomization reproduces seed");
        var doc=GlobalTemplateDocument.Empty("SM",7);doc.SetSection("ProgressiveApp",new System.Text.Json.Nodes.JsonObject{["PartialSnapshot"]=true});
        var state=new Gen7MartState(Candies:true,Vitamins:true,FreeMega:true,BanCapsule:true);NativeGen7MartBridge.Capture(doc,state);
        check(NativeRecipeCompletion.TryComplete(project,doc),"SM full native shop recipe completes after real source preflight");
        check(File.ReadAllBytes(path).SequenceEqual(source)&&File.ReadAllBytes(itemPath).SequenceEqual(itemBefore),"SM preflight preserves shop and item source files");
        var proposal=NativeGen7MartBridge.Prepare(new GameMartService(project),state,73);NativeGen7MartBridge.ExecuteSession(project,doc.ToJson(),73);
        check(File.ReadAllBytes(path).SequenceEqual(proposal.Output)&&File.ReadAllBytes(itemPath).SequenceEqual(proposal.ItemOutput!),"SM native Global replay matches joint shop/item proposal");
        byte[] ambiguous=(byte[])source.Clone();GameMartService.SmRegularSignature.CopyTo(ambiguous,0x6000);File.WriteAllBytes(path,ambiguous);reject(()=>new GameMartService(project),"SM rejects duplicate shop signature");
    }
}
