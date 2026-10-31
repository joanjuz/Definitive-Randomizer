using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.NDS;

// Gen5 randomizer: synthetic Black/White layout for CI, plus a probe that exports from real dumps to isolated outputs.
internal static class NdsGen5RandomizerChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        string temp=Path.Combine(Path.GetTempPath(),"nds-gen5-rand-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            string path=Path.Combine(temp,"synthetic.nds");File.WriteAllBytes(path,SyntheticBlack());
            using var rom=NdsRom.Open(path);var data=Gen5Data.Open(rom,requireOriginalTables:false);
            var keep=Gen5Randomizer.Generate(data,new Gen5RandomizerOptions(7));check(keep.Files.Count==0&&keep.Changes.Count==0,"Gen5Rand_KeepChangesNothing");
            var options=new Gen5RandomizerOptions(7){Trainers=Gen5SpeciesMode.Random,Wild=Gen5SpeciesMode.SimilarBst};
            var a=Gen5Randomizer.Generate(data,options);var b=Gen5Randomizer.Generate(data,options);var c=Gen5Randomizer.Generate(data,options with{Seed=8});
            check(a.Changes.SequenceEqual(b.Changes)&&a.Files.Keys.Order().SequenceEqual(b.Files.Keys.Order())&&a.Files.All(f=>f.Value.SequenceEqual(b.Files[f.Key]))&&!a.Changes.SequenceEqual(c.Changes),"Gen5Rand_DeterministicBySeed");
            var headers=Narc.Read(a.Files["a/0/9/2"]);var teams=Narc.Read(a.Files["a/0/9/3"]);
            byte[] withMoves=teams.Files[1];
            check(headers.Files[1][0]==2&&withMoves.Length==20&&BinaryPrimitives.ReadUInt16LittleEndian(withMoves.AsSpan(8))==50&&BinaryPrimitives.ReadUInt16LittleEndian(withMoves.AsSpan(18))==51,"Gen5Rand_DropsCustomMovesButKeepsItems");
            check(BinaryPrimitives.ReadUInt16LittleEndian(withMoves.AsSpan(2))==12&&BinaryPrimitives.ReadUInt16LittleEndian(withMoves.AsSpan(12))==14&&BinaryPrimitives.ReadUInt16LittleEndian(withMoves.AsSpan(6))==0,"Gen5Rand_KeepsLevelsAndClearsForms");
            check(headers.Files[2][0]==0&&teams.Files[2].Length==8&&a.Changes.Count(x=>x.Field=="Movimientos personalizados")==1,"Gen5Rand_TrainerWithoutMovesKeepsLayout");
            byte[] wild=Narc.Read(a.Files["a/1/2/6"]).Files[0];int[] species=Enumerable.Range(0,12).Select(i=>BinaryPrimitives.ReadUInt16LittleEndian(wild.AsSpan(8+i*4))&0x7FF).ToArray();
            check(species.Take(6).Distinct().Count()==1&&species.Skip(6).Distinct().Count()==1&&wild[0]==20&&wild[10]==3&&wild[11]==4,"Gen5Rand_SameSpeciesInAreaStaysTogetherAndKeepsRatesAndLevels");
            var progressive=Gen5Randomizer.Generate(data,new Gen5RandomizerOptions(3){Trainers=Gen5SpeciesMode.ProgressiveBst});var stats=data.BaseStats();
            check(progressive.Changes.Where(x=>x.Field=="Especie").All(x=>stats[x.After].Total is >=220 and <=380),"Gen5Rand_ProgressiveRespectsLevelRange");
            reject(()=>Gen5Randomizer.Generate(data,options with{BstRanges=[new Gen5BstRange(5,1,0,10)]}),"Gen5Rand_RejectsInvalidRange");
            reject(()=>data.Export(Path.Combine(temp,"out.nds"),a),"Gen5Rand_ExportRequiresVerifiedOriginalTables");
            reject(()=>Gen5Randomizer.Generate(data,new Gen5RandomizerOptions(1){Starters=Gen5StarterMode.Custom,CustomStarters=[1,1,4]}),"Gen5Rand_StartersRejectDuplicates");
            reject(()=>Gen5Randomizer.Generate(data,new Gen5RandomizerOptions(1){Starters=Gen5StarterMode.Custom,CustomStarters=[1,4,700]}),"Gen5Rand_StartersRejectSpeciesOutsideGen5");
            reject(()=>Gen5Randomizer.Generate(data,new Gen5RandomizerOptions(1){Starters=Gen5StarterMode.Custom}),"Gen5Rand_CustomStartersRequireThreeSpecies");
            reject(()=>Gen5Data.OpenRomhack(rom),"Gen5Romhack_RejectsTablesThatDoNotParse");
            string hackPath=Path.Combine(temp,"romhack.nds");File.WriteAllBytes(hackPath,SyntheticBlackRomhack());
            using(var hackRom=NdsRom.Open(hackPath))
            {
                reject(()=>Gen5Data.Open(hackRom),"Gen5Romhack_StrictOpenStillRejectsModifiedTables");
                var hack=Gen5Data.OpenRomhack(hackRom);
                check(hack.IsRomhack&&!hack.RequiresOriginalTables&&hack.Unavailable.ContainsKey(Gen5Feature.Starters)&&hack.Unavailable.ContainsKey(Gen5Feature.Shops)&&!hack.Unavailable.ContainsKey(Gen5Feature.HiddenItems),"Gen5Romhack_OpensAndListsOptionsWhoseCodeIsNotOriginal");
                reject(()=>Gen5Randomizer.Generate(hack,new Gen5RandomizerOptions(2){Starters=Gen5StarterMode.Random}),"Gen5Romhack_RejectsUnavailableStarters");
                reject(()=>Gen5Randomizer.Generate(hack,new Gen5RandomizerOptions(2){Shops=Gen5ItemMode.Shuffle}),"Gen5Romhack_RejectsUnavailableShops");
                var hacked=Gen5Randomizer.Generate(hack,new Gen5RandomizerOptions(2){Trainers=Gen5SpeciesMode.Random,Wild=Gen5SpeciesMode.Random,FieldItems=Gen5ItemMode.Shuffle});
                string hackOut=Path.Combine(temp,"romhack-out.nds");hack.Export(hackOut,hacked);
                using var exported=NdsRom.Open(hackOut);var again=Gen5Data.Open(exported,requireOriginalTables:false);
                check(hacked.Changes.Count>0&&hacked.Overlays.Count==0&&again.Trainers().Count==hack.Trainers().Count&&again.Encounters().Count==hack.Encounters().Count,"Gen5Romhack_ExportsTrainersAndEncounters");
            }
            check(new[]{17,1,213,327,504,537,577,615}.All(Gen5Randomizer.IsMovableItem)&&!new[]{0,120,133,328,419,420,425,428,503,505,536,574,578,579,616,617,618,620,621,626,700}.Any(Gen5Randomizer.IsMovableItem),"Gen5Rand_TmsHmsKeyAndUnknownItemsNeverMove");
            var w2=Gen5Profile.All.Single(p=>p.GameCode=="IRDO");var bw=Gen5Profile.All.Where(p=>!p.Sequel).ToArray();
            check(w2 is {Sequel:true,ShopOverlay:-1,StarterCryOverlay:316,PokedexJump:0x1E,RecompressStarterCryOverlay:true}&&w2.ShopItemSizes.Count==32&&w2.FixedShops.All(s=>s<32)&&w2.Files.ContainsKey(Gen5Profile.ShopItems)
                &&w2.StarterOffsets.All(o=>o.All(x=>x.File==854))&&w2.PokedexPatch.Take(w2.PokedexSignature.Count).SequenceEqual(w2.PokedexSignature)
                &&bw.Length==2&&bw.All(p=>p is {ShopOverlay:21,StarterCryOverlay:223,PokedexJump:4,RecompressStarterCryOverlay:false}&&p.ShopItemOffsets.Count==p.ShopItemSizes.Count&&!p.Files.ContainsKey(Gen5Profile.ShopItems)&&p.PokedexPatch.Take(p.PokedexSignature.Count).SequenceEqual(p.PokedexSignature)),
                "Gen5Rand_White2ProfileUsesShopNarcAndItsOwnStarterData");
        }
        finally{Directory.Delete(temp,true);}
    }

    // Exports a randomized copy of each real dump, re-reads it and checks that only the intended data changed.
    internal static void Probe(string output,IEnumerable<string> roms)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");Directory.CreateDirectory(output);
        foreach(string source in roms.Select(Path.GetFullPath))
        {
            string before=Hash(source);
            using(var rom=NdsRom.Open(source))
            {
                var data=Gen5Data.Open(rom);string game=data.Profile.GameCode;
                var options=new Gen5RandomizerOptions(20261030){Trainers=Gen5SpeciesMode.ProgressiveBst,Wild=Gen5SpeciesMode.SimilarBst};
                var result=Gen5Randomizer.Generate(data,options);
                string target=Path.Combine(output,game+"-random.nds");data.Export(target,result);
                using var exported=NdsRom.Open(target);var check=Gen5Data.Open(exported,requireOriginalTables:false);
                var oldTrainers=data.Trainers();var newTrainers=check.Trainers();
                Check(newTrainers.Count==oldTrainers.Count&&newTrainers.Zip(oldTrainers).All(p=>p.First.Team.Count==p.Second.Team.Count&&p.First.BattleType==p.Second.BattleType&&p.First.Name==p.Second.Name&&p.First.Team.Zip(p.Second.Team).All(q=>q.First.Level==q.Second.Level&&q.First.HeldItem==q.Second.HeldItem)),$"Gen5Rand_Real_{game}_TrainersKeepCountsLevelsItemsAndBattleType");
                var stats=data.BaseStats();var team=newTrainers.SelectMany(t=>t.Team).ToArray();
                Check(team.All(p=>p.Species is >=1 and <=649&&p.Form==0&&options.BstRanges.Any(r=>p.Level>=r.MinLevel&&p.Level<=r.MaxLevel&&stats[p.Species].Total>=r.MinBst&&stats[p.Species].Total<=r.MaxBst))&&newTrainers.All(t=>!t.HasMoves),$"Gen5Rand_Real_{game}_TrainerSpeciesFollowProgressiveBst ({team.Length} Pokémon, {result.Changes.Count(x=>x.Location.StartsWith("Entrenador")&&x.Field=="Especie")} cambios)");
                var oldAreas=data.Encounters();var newAreas=check.Encounters();
                Check(newAreas.Count==oldAreas.Count&&newAreas.Zip(oldAreas).All(p=>p.First.Rate==p.Second.Rate&&p.First.Slots.Zip(p.Second.Slots).All(s=>s.First.MinLevel==s.Second.MinLevel&&s.First.MaxLevel==s.Second.MaxLevel&&(s.Second.Species==0)==(s.First.Species==0))),$"Gen5Rand_Real_{game}_EncountersKeepRatesLevelsAndEmptySlots ({result.Changes.Count(x=>x.Location.StartsWith("Encuentros"))} cambios)");
                Check(check.SpeciesNames().SequenceEqual(data.SpeciesNames())&&check.Starters().SequenceEqual(data.Starters()),$"Gen5Rand_Real_{game}_UntouchedTablesStayIdentical");
                var changed=result.Files.Keys.Select(p=>rom.File(p).Id).ToHashSet();
                Check(exported.Files.Where(f=>!changed.Contains(f.Id)).All(f=>exported.ReadFile(f).SequenceEqual(rom.ReadFile(rom.Files[f.Id]))),$"Gen5Rand_Real_{game}_OnlyRandomizedNarcsDiffer");
                var again=Gen5Randomizer.Generate(data,options);Check(again.Changes.SequenceEqual(result.Changes),$"Gen5Rand_Real_{game}_SameSeedSameResult");
            }
            foreach(string f in Directory.GetFiles(output))File.Delete(f);
            Check(Hash(source)==before,$"Gen5Rand_Real_{Path.GetFileNameWithoutExtension(source)}_SourceUnchanged");
        }
        Console.WriteLine($"Completed {checks} Gen5 randomizer real-dump checks; outputs deleted from {output}.");
    }

    // Starters on real dumps: mapping of the selection sprites, every place a starter lives, and nothing else changed.
    internal static void ProbeStarters(string output,IEnumerable<string> roms)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");Directory.CreateDirectory(output);
        static byte[] Lz11(byte[] data){using var input=new MemoryStream(data);using var result=new MemoryStream();pk3DS.Core.CTR.LZSS.Decompress(input,data.Length,result);return result.ToArray();}
        foreach(string source in roms.Select(Path.GetFullPath))
        {
            string before=Hash(source);
            using(var rom=NdsRom.Open(source))
            {
                var data=Gen5Data.Open(rom);string game=data.Profile.GameCode;
                var graphics=Narc.Read(rom.ReadFile("a/0/0/4"));var selection=Narc.Read(rom.ReadFile("a/2/0/5"));
                // The selection sprites are not byte-identical to the battle sprites (same RGCN size, different data); only the format can be checked.
                Check(new[]{495,498,501}.Select((s,i)=>{byte[] front=Lz11(graphics.Files[s*20]);return front.Length==selection.Files[12+i].Length&&front.AsSpan(0,4).SequenceEqual("RGCN"u8)&&selection.Files[12+i].AsSpan(0,4).SequenceEqual("RGCN"u8);}).All(x=>x),$"Gen5Starters_Real_{game}_BattleSpritesMatchSelectionSlotFormat");
                int[] chosen=[25,4,7];
                var result=Gen5Randomizer.Generate(data,new Gen5RandomizerOptions(1){Starters=Gen5StarterMode.Custom,CustomStarters=chosen});
                Check(result.Changes.Count==3&&result.Files.Count==3&&result.Overlays.Keys.SequenceEqual(new[]{223}),$"Gen5Starters_Real_{game}_TouchesScriptsGraphicsTextAndCryOverlayOnly");
                string target=Path.Combine(output,game+"-starters.nds");data.Export(target,result);
                using var exported=NdsRom.Open(target);var check=Gen5Data.Open(exported,requireOriginalTables:false);
                Check(check.Starters().SequenceEqual(chosen),$"Gen5Starters_Real_{game}_ScriptsGiveTheNewStarters");
                byte[] cries=exported.ReadOverlay(223);int table=cries.AsSpan().IndexOf(new byte[]{0x08,0x0A,0x07,0x00,0x08,0x00,0x00})+7;
                Check(Enumerable.Range(0,3).All(i=>BitConverter.ToUInt16(cries,table+i*2)==chosen[i])&&exported.Overlay(223) is {Compressed:false,StoredSize:13696}&&cries.Length==13696,$"Gen5Starters_Real_{game}_CryTableUpdatedInUncompressedOverlay");
                var newSelection=Narc.Read(exported.ReadFile("a/2/0/5"));
                Check(chosen.Select((s,i)=>newSelection.Files[12+i].SequenceEqual(Lz11(graphics.Files[s*20]))&&newSelection.Files[i*2].SequenceEqual(graphics.Files[s*20+18])).All(x=>x),$"Gen5Starters_Real_{game}_SelectionSpritesAndPalettesAreTheNewStarters");
                var types=data.Text(199);var lines=Gen5Text.Read(Narc.Read(exported.ReadFile("a/0/0/3")).Files[430]);var oldLines=Gen5Text.Read(Narc.Read(rom.ReadFile("a/0/0/3")).Files[430]);
                Check(lines[18].Contains(types[12])&&!lines[18].Contains(types[11])&&lines[17]==oldLines[17]&&lines[16]==oldLines[16]&&lines.Where((l,i)=>i is not 18).SequenceEqual(oldLines.Where((l,i)=>i is not 18)),$"Gen5Starters_Real_{game}_SelectionTextNamesTheNewType ({types[12]})");
                byte[] script=Narc.Read(exported.ReadFile("a/0/5/7")).Files[792];byte[] oldScript=Narc.Read(rom.ReadFile("a/0/5/7")).Files[792];
                Check(script.Length==oldScript.Length+16&&script[0x3A0]==4&&script[0x3A1]==0&&BitConverter.ToInt32(script,0x3A2)+0x3A6==oldScript.Length&&script.AsSpan(oldScript.Length,4).SequenceEqual(new byte[]{0x24,0,0xA7,2}),$"Gen5Starters_Real_{game}_PokedexScriptCallsAppendedBlock");
                var changedPaths=result.Files.Keys.Select(p=>rom.File(p).Id).Append(rom.Overlay(223).FileId).ToHashSet();
                Check(exported.Files.Where(f=>!changedPaths.Contains(f.Id)).All(f=>exported.ReadFile(f).SequenceEqual(rom.ReadFile(rom.Files[f.Id]))),$"Gen5Starters_Real_{game}_EveryOtherFileIdentical");
                var random=Gen5Randomizer.Generate(data,new Gen5RandomizerOptions(77){Starters=Gen5StarterMode.Random});var again=Gen5Randomizer.Generate(data,new Gen5RandomizerOptions(77){Starters=Gen5StarterMode.Random});
                var picked=random.Changes.Select(c=>c.After).ToArray();
                Check(random.Changes.SequenceEqual(again.Changes)&&picked.Distinct().Count()==picked.Length,$"Gen5Starters_Real_{game}_RandomStartersDistinctAndReproducible ({string.Join(", ",picked.Select(s=>data.SpeciesNames()[s]))})");
            }
            foreach(string f in Directory.GetFiles(output))File.Delete(f);
            Check(Hash(source)==before,$"Gen5Starters_Real_{Path.GetFileNameWithoutExtension(source)}_SourceUnchanged");
        }
        Console.WriteLine($"Completed {checks} Gen5 starter real-dump checks; outputs deleted from {output}.");
    }

    // Field items and shops on real dumps: same multiset of items, fixed items and shops untouched, nothing else changed.
    internal static void ProbeItems(string output,IEnumerable<string> roms)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");Directory.CreateDirectory(output);
        static string Bag(IEnumerable<int> items)=>string.Join(",",items.Order());
        foreach(string source in roms.Select(Path.GetFullPath))
        {
            string before=Hash(source);
            using(var rom=NdsRom.Open(source))
            {
                var data=Gen5Data.Open(rom);string game=data.Profile.GameCode;
                var field=data.FieldItems();var shops=data.Shops();
                Check(field.Count==483&&field.Count(f=>f.File==864)==306&&shops.Count==26&&shops.Select(s=>s.Count).SequenceEqual(data.Profile.ShopItemSizes),$"Gen5Items_Real_{game}_Reads306BallsAnd177HiddenItemsAnd26Shops");
                var options=new Gen5RandomizerOptions(5){FieldItems=Gen5ItemMode.Shuffle,Shops=Gen5ItemMode.Shuffle,Starters=Gen5StarterMode.Custom,CustomStarters=[25,4,7]};
                var result=Gen5Randomizer.Generate(data,options);
                Check(result.Overlays.Keys.Order().SequenceEqual(new[]{21,223})&&result.RecompressOverlays.SetEquals(new[]{21}),$"Gen5Items_Real_{game}_ShopsRecompressOverlay21AndStartersKeep223Uncompressed");
                string target=Path.Combine(output,game+"-items.nds");data.Export(target,result);
                using var exported=NdsRom.Open(target);var check=Gen5Data.Open(exported,requireOriginalTables:false);
                var newField=check.FieldItems();
                Check(newField.Count==field.Count&&newField.Zip(field).All(p=>p.First.File==p.Second.File&&p.First.Offset==p.Second.Offset)&&Bag(newField.Select(f=>f.Item))==Bag(field.Select(f=>f.Item)),$"Gen5Items_Real_{game}_FieldItemsKeepPositionsAndTheSameItems");
                Check(newField.Zip(field).Where(p=>!Gen5Randomizer.IsMovableItem(p.Second.Item)).All(p=>p.First.Item==p.Second.Item)&&newField.Zip(field).Count(p=>p.First.Item!=p.Second.Item)>200,$"Gen5Items_Real_{game}_TmsStayAndRegularItemsMove ({newField.Zip(field).Count(p=>p.First.Item!=p.Second.Item)} cambiados)");
                var newShops=check.Shops();
                Check(data.Profile.FixedShops.All(s=>newShops[s].SequenceEqual(shops[s]))&&Bag(newShops.SelectMany(s=>s))==Bag(shops.SelectMany(s=>s))&&newShops.Select(s=>s.Count).SequenceEqual(data.Profile.ShopItemSizes),$"Gen5Items_Real_{game}_ShopsKeepFixedListsAndTheSameItems");
                byte[] plain=exported.ReadOverlay(21);var ovl=exported.Overlay(21);
                Check(ovl.Compressed&&plain.Length==rom.ReadOverlay(21).Length&&exported.Files[ovl.FileId].Start==rom.Files[ovl.FileId].Start,$"Gen5Items_Real_{game}_Overlay21StaysCompressedInItsSlot ({ovl.StoredSize} bytes)");
                Check(check.Starters().SequenceEqual(new[]{25,4,7}),$"Gen5Items_Real_{game}_StartersAndItemsShareTheScriptsTable");
                var changedIds=result.Files.Keys.Select(p=>rom.File(p).Id).Concat(result.Overlays.Keys.Select(o=>rom.Overlay(o).FileId)).ToHashSet();
                Check(exported.Files.Where(f=>!changedIds.Contains(f.Id)).All(f=>exported.ReadFile(f).SequenceEqual(rom.ReadFile(rom.Files[f.Id]))),$"Gen5Items_Real_{game}_EveryOtherFileIdentical");
                var again=Gen5Randomizer.Generate(data,options);Check(again.Changes.SequenceEqual(result.Changes),$"Gen5Items_Real_{game}_SameSeedSameResult");
            }
            foreach(string f in Directory.GetFiles(output))File.Delete(f);
            Check(Hash(source)==before,$"Gen5Items_Real_{Path.GetFileNameWithoutExtension(source)}_SourceUnchanged");
        }
        Console.WriteLine($"Completed {checks} Gen5 item real-dump checks; outputs deleted from {output}.");
    }

    // White 2 on the owner's dump: every option at once, then each table re-read from the exported ROM.
    internal static void ProbeSequel(string output,string source)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        output=Path.GetFullPath(output);source=Path.GetFullPath(source);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");Directory.CreateDirectory(output);
        static byte[] Lz11(byte[] data){using var input=new MemoryStream(data);using var result=new MemoryStream();pk3DS.Core.CTR.LZSS.Decompress(input,data.Length,result);return result.ToArray();}
        static string Bag(IEnumerable<int> items)=>string.Join(",",items.Order());
        string before=Hash(source);
        using(var rom=NdsRom.Open(source))
        {
            var data=Gen5Data.Open(rom);var p=data.Profile;
            Check(p.GameCode=="IRDO"&&p.Sequel&&data.Identity.KnownDump,"Gen5W2_Real_KnownWhite2DumpUsesTheSequelProfile");
            var names=data.SpeciesNames();var trainers=data.Trainers();var areas=data.Encounters();
            Check(names[495]=="Snivy"&&names[649]=="Genesect"&&data.Text(p.ItemNamesText)[1]=="Master Ball"&&data.Text(p.TypeNamesText)[11]=="Grass","Gen5W2_Real_SpeciesItemAndTypeNamesFromTheirOwnTexts");
            var leaders=trainers.Where(t=>t.ClassName=="Leader"&&t.Id<200).OrderBy(t=>t.Team.Max(m=>m.Level)).Select(t=>t.Name).ToArray();
            Check(trainers.Count==813&&leaders.SequenceEqual(new[]{"Cheren","Roxie","Burgh","Elesa","Clay","Skyla","Drayden","Marlon"}),$"Gen5W2_Real_813TrainersAndTheEightGymLeadersInOrder");
            Check(areas.Count>0&&areas.All(a=>a.Slots.All(s=>s.Species<=Gen5Randomizer.MaxSpecies)),$"Gen5W2_Real_EncountersParse ({areas.Count} áreas)");
            Check(data.Starters().SequenceEqual(new[]{495,498,501}),"Gen5W2_Real_StartersAreSnivyTepigOshawott");
            var field=data.FieldItems();var shops=data.Shops();
            Check(field.Count(f=>f.File==p.ItemBallScript)>200&&field.Count(f=>f.File==p.HiddenItemScript)>100&&shops.Count==32&&shops[7].SequenceEqual(new[]{331,335}),$"Gen5W2_Real_ReadsFieldItemsAndThe32Shops ({field.Count(f=>f.File==p.ItemBallScript)} visibles, {field.Count(f=>f.File==p.HiddenItemScript)} ocultos)");

            int[] chosen=[25,4,7];
            var options=new Gen5RandomizerOptions(2026){Trainers=Gen5SpeciesMode.ProgressiveBst,Wild=Gen5SpeciesMode.SimilarBst,Starters=Gen5StarterMode.Custom,CustomStarters=chosen,FieldItems=Gen5ItemMode.Shuffle,Shops=Gen5ItemMode.Shuffle};
            var result=Gen5Randomizer.Generate(data,options);
            Check(result.Overlays.Keys.SequenceEqual(new[]{316})&&result.RecompressOverlays.SetEquals(new[]{316})&&result.Files.ContainsKey("a/2/8/2"),"Gen5W2_Real_ShopsGoToTheirNarcAndOnlyTheCryOverlayIsRecompressed");
            string target=Path.Combine(output,"IRDO-all.nds");data.Export(target,result);
            using var exported=NdsRom.Open(target);var check=Gen5Data.Open(exported,requireOriginalTables:false);
            var newTrainers=check.Trainers();
            Check(newTrainers.Count==trainers.Count&&newTrainers.Zip(trainers).All(t=>t.First.Team.Count==t.Second.Team.Count&&t.First.Team.Zip(t.Second.Team).All(m=>m.First.Level==m.Second.Level&&m.First.HeldItem==m.Second.HeldItem)),"Gen5W2_Real_TrainersKeepTeamSizesLevelsAndItems");
            Check(newTrainers.SelectMany(t=>t.Team).Count(m=>m.Species is <1 or >Gen5Randomizer.MaxSpecies)==0&&result.Changes.Count(c=>c.Location.StartsWith("Entrenador"))>1000,"Gen5W2_Real_TrainerSpeciesChangedAndValid");
            var newAreas=check.Encounters();
            Check(newAreas.Count==areas.Count&&newAreas.Zip(areas).All(a=>a.First.Rate==a.Second.Rate&&a.First.Slots.Zip(a.Second.Slots).All(s=>s.First.MinLevel==s.Second.MinLevel&&s.First.MaxLevel==s.Second.MaxLevel)),"Gen5W2_Real_EncountersKeepRatesAndLevels");
            Check(check.Starters().SequenceEqual(chosen),"Gen5W2_Real_ScriptsGiveTheNewStarters");
            var ovl=exported.Overlay(316);byte[] cries=exported.ReadOverlay(316);int table=cries.AsSpan().IndexOf([.. p.StarterCryPrefix])+p.StarterCryPrefix.Count;
            Check(ovl.Compressed&&cries.Length==rom.ReadOverlay(316).Length&&Enumerable.Range(0,3).All(i=>BitConverter.ToUInt16(cries,table+i*2)==chosen[i])&&exported.Files[ovl.FileId].Start==rom.Files[ovl.FileId].Start,$"Gen5W2_Real_CryTableUpdatedInRecompressedOverlay ({ovl.StoredSize} bytes)");
            var graphics=Narc.Read(rom.ReadFile("a/0/0/4"));var selection=Narc.Read(exported.ReadFile("a/2/0/2"));
            Check(chosen.Select((s,i)=>selection.Files[12+i].SequenceEqual(Lz11(graphics.Files[s*20]))&&selection.Files[i*2].SequenceEqual(graphics.Files[s*20+18])).All(x=>x),"Gen5W2_Real_SelectionSpritesAndPalettesAreTheNewStarters");
            var lines=Gen5Text.Read(Narc.Read(exported.ReadFile("a/0/0/3")).Files[169]);var oldLines=Gen5Text.Read(Narc.Read(rom.ReadFile("a/0/0/3")).Files[169]);
            Check(lines[37].Contains("Electric")&&lines[36]==oldLines[36]&&lines[35]==oldLines[35]&&lines.Where((l,i)=>i!=37).SequenceEqual(oldLines.Where((l,i)=>i!=37)),"Gen5W2_Real_SelectionTextNamesTheNewType");
            byte[] script=Narc.Read(exported.ReadFile("a/0/5/6")).Files[854];byte[] oldScript=Narc.Read(rom.ReadFile("a/0/5/6")).Files[854];
            Check(script.Length==oldScript.Length+14&&script[0x6BE]==0x1E&&script[0x6BF]==0&&BitConverter.ToInt32(script,0x6C0)+0x6C4==oldScript.Length&&script.AsSpan(oldScript.Length,6).SequenceEqual(new byte[]{0x28,0,0xA1,0x40,4,0}),"Gen5W2_Real_PokedexScriptJumpsToAppendedBlock");
            var newField=check.FieldItems();
            Check(newField.Count==field.Count&&newField.Zip(field).All(f=>f.First.Offset==f.Second.Offset&&(Gen5Randomizer.IsMovableItem(f.Second.Item)||f.First.Item==f.Second.Item))&&Bag(newField.Select(f=>f.Item))==Bag(field.Select(f=>f.Item)),"Gen5W2_Real_FieldItemsSameItemsAndFixedOnesStay");
            var newShops=check.Shops();
            Check(p.FixedShops.All(s=>newShops[s].SequenceEqual(shops[s]))&&Bag(newShops.SelectMany(s=>s))==Bag(shops.SelectMany(s=>s))&&!newShops.SequenceEqual(shops),"Gen5W2_Real_ShopsShuffledExceptFixedLists");
            var shopNarc=Narc.Read(exported.ReadFile("a/2/8/2"));var oldShopNarc=Narc.Read(rom.ReadFile("a/2/8/2"));
            Check(shopNarc.Files.Count==80&&shopNarc.Files.Skip(32).Zip(oldShopNarc.Files.Skip(32)).All(f=>f.First.SequenceEqual(f.Second)),"Gen5W2_Real_UnusedShopFilesUntouched");
            var changedIds=result.Files.Keys.Select(k=>rom.File(k).Id).Append(rom.Overlay(316).FileId).ToHashSet();
            Check(exported.Files.Where(f=>!changedIds.Contains(f.Id)).All(f=>exported.ReadFile(f).SequenceEqual(rom.ReadFile(rom.Files[f.Id]))),"Gen5W2_Real_EveryOtherFileIdentical");
            Check(Gen5Randomizer.Generate(data,options).Changes.SequenceEqual(result.Changes),"Gen5W2_Real_SameSeedSameResult");
        }
        foreach(string f in Directory.GetFiles(output))File.Delete(f);
        Check(Hash(source)==before,"Gen5W2_Real_SourceUnchanged");
        Console.WriteLine($"Completed {checks} White 2 real-dump checks; outputs deleted from {output}.");
    }

    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}

    // Black (U) code and paths with small tables: 650 species whose BST equals 6*species, three trainers and one wild area.
    // Every profile table present and parseable but none original, and no overlays: what a romhack looks like to the profile.
    internal static byte[] SyntheticBlackRomhack()
    {
        var scripts=new byte[866][];for(int i=0;i<scripts.Length;i++)scripts[i]=[0x13,0xFD];
        return SyntheticBlack(new Dictionary<string,byte[]>{["a/0/5/7"]=BuildNarc(scripts),["a/0/0/3"]=BuildNarc([]),["a/0/0/4"]=BuildNarc([]),["a/2/0/5"]=BuildNarc([])});
    }

    internal static byte[] SyntheticBlackRom()=>SyntheticBlack();

    private static byte[] SyntheticBlack(IReadOnlyDictionary<string,byte[]>? extra=null)
    {
        var text=new byte[204][];for(int i=0;i<text.Length;i++)text[i]=[];text[203]=Gen5Message(Enumerable.Range(0,560).Select(i=>"Mov"+i).ToArray());
        text[199]=Gen5Message(Enumerable.Range(0,17).Select(i=>"Tipo"+i).ToArray());text[182]=Gen5Message(Enumerable.Range(0,165).Select(i=>"Hab"+i).ToArray());
        text[70]=Gen5Message(Enumerable.Range(0,650).Select(i=>"Especie"+i).ToArray());text[190]=Gen5Message(["","Ana","Beto"]);text[191]=Gen5Message(["Clase"]);text[54]=Gen5Message(Enumerable.Range(0,640).Select(i=>"Objeto"+i).ToArray());
        var stats=Enumerable.Range(0,650).Select(s=>{var p=new byte[60];for(int i=0;i<6;i++)p[i]=(byte)Math.Clamp(s+i*7,10,255);p[6]=11;p[7]=3;p[8]=45;p[24]=65;p[25]=34;return p;}).ToArray();
        byte[] header(int flags,int count){var h=new byte[20];h[0]=(byte)flags;h[3]=(byte)count;return h;}
        byte[] mon(int level,int species,int form){var m=new byte[8];BinaryPrimitives.WriteUInt16LittleEndian(m.AsSpan(2),(ushort)level);BinaryPrimitives.WriteUInt16LittleEndian(m.AsSpan(4),(ushort)species);BinaryPrimitives.WriteUInt16LittleEndian(m.AsSpan(6),(ushort)form);return m;}
        byte[] u16(params int[] v)=>v.SelectMany(x=>BitConverter.GetBytes((ushort)x)).ToArray();
        byte[] team1=[..mon(12,10,1),..u16(50),..u16(1,2,3,4),..mon(14,11,0),..u16(51),..u16(5,6,7,8)];
        var wild=new byte[232];wild[0]=20;for(int i=0;i<12;i++){BinaryPrimitives.WriteUInt16LittleEndian(wild.AsSpan(8+i*4),(ushort)((i<6?504:506)|(1<<11)));wild[10+i*4]=3;wild[11+i*4]=4;}
        var files=new Dictionary<string,byte[]>
        {
            ["a/0/0/2"]=BuildNarc(text),["a/0/1/6"]=BuildNarc(stats),["a/0/5/7"]=BuildNarc([]),
            ["a/0/1/8"]=BuildNarc(Enumerable.Range(0,650).Select(_=>u16(33,1,45,3,0xFFFF,0xFFFF)).ToArray()),
            ["a/0/1/9"]=BuildNarc(Enumerable.Range(0,650).Select(s=>{var e=new byte[42];if(s is 1 or 2){BinaryPrimitives.WriteUInt16LittleEndian(e,4);BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(2),(ushort)(s*16));BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(4),(ushort)(s+1));}if(s==4){BinaryPrimitives.WriteUInt16LittleEndian(e,5);BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(4),5);}return e;}).ToArray()),
            ["a/0/2/1"]=BuildNarc(Enumerable.Range(0,560).Select(m=>{var d=new byte[36];d[0]=(byte)(m%17);d[2]=(byte)(m%3);d[3]=(byte)(m%5==0?0:40);d[4]=100;d[5]=20;return d;}).ToArray()),
            ["a/0/9/2"]=BuildNarc([new byte[20],header(3,2),header(0,1)]),["a/0/9/3"]=BuildNarc([[],team1,mon(15,20,0)]),["a/1/2/6"]=BuildNarc([wild]),
        };
        foreach(var (path,data) in extra??new Dictionary<string,byte[]>())files[path]=data;
        return BuildRom(files,"IRBO");
    }

    private static byte[] Gen5Message(string[] lines)
    {
        var body=new List<byte>();var table=new List<byte>();int tableLength=lines.Length*8;
        foreach(string line in lines)
        {
            ushort[] chars=line.Select(ch=>(ushort)ch).Append((ushort)0xFFFF).ToArray();
            table.AddRange(BitConverter.GetBytes(4+tableLength+body.Count));table.AddRange(BitConverter.GetBytes((ushort)chars.Length));table.AddRange(new byte[2]);
            int key=0x2345;foreach(ushort ch in chars){body.AddRange(BitConverter.GetBytes((ushort)(ch^key)));key=((key<<3)|(key>>13))&0xFFFF;}
        }
        int size=4+tableLength+body.Count;var data=new List<byte>();
        data.AddRange(BitConverter.GetBytes((ushort)1));data.AddRange(BitConverter.GetBytes((ushort)lines.Length));data.AddRange(BitConverter.GetBytes(size));data.AddRange(new byte[4]);data.AddRange(BitConverter.GetBytes(16));
        data.AddRange(BitConverter.GetBytes(size));data.AddRange(table);data.AddRange(body);return data.ToArray();
    }

    internal static byte[] BuildNarc(byte[][] files)
    {
        int image=8+files.Sum(f=>(f.Length+3)&~3);var data=new List<byte>();
        data.AddRange("NARC"u8.ToArray());data.AddRange(new byte[]{0xFE,0xFF,0,1,0,0,0,0,0x10,0,3,0});
        data.AddRange("BTAF"u8.ToArray());data.AddRange(BitConverter.GetBytes(12+files.Length*8));data.AddRange(BitConverter.GetBytes((ushort)files.Length));data.AddRange(new byte[2]);
        int offset=0;foreach(var f in files){data.AddRange(BitConverter.GetBytes(offset));data.AddRange(BitConverter.GetBytes(offset+f.Length));offset+=(f.Length+3)&~3;}
        data.AddRange("BTNF"u8.ToArray());data.AddRange(BitConverter.GetBytes(16));data.AddRange(BitConverter.GetBytes(4));data.AddRange(BitConverter.GetBytes(0x10000));
        data.AddRange("GMIF"u8.ToArray());data.AddRange(BitConverter.GetBytes(image));
        foreach(var f in files){data.AddRange(f);data.AddRange(Enumerable.Repeat((byte)0xFF,((f.Length+3)&~3)-f.Length));}
        byte[] narc=data.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(narc.AsSpan(8),(uint)narc.Length);return narc;
    }

    // Minimal ROM with a nested FNT built from slash paths, files aligned to 0x200 and 64 KiB of free padding at the end.
    internal static byte[] BuildRom(IReadOnlyDictionary<string,byte[]> files,string gameCode,byte version=0)
    {
        var dirs=new List<string>{""};foreach(string p in files.Keys.Order(StringComparer.Ordinal)){var parts=p.Split('/');for(int i=1;i<parts.Length;i++){string d=string.Join('/',parts[..i]);if(!dirs.Contains(d))dirs.Add(d);}}
        string Parent(string d)=>d.Contains('/')?d[..d.LastIndexOf('/')]:"";
        var order=new List<string>();var first=new int[dirs.Count];
        for(int i=0;i<dirs.Count;i++){first[i]=order.Count;order.AddRange(files.Keys.Where(p=>Parent(p)==dirs[i]).Order(StringComparer.Ordinal));}
        var sub=new List<byte>[dirs.Count];
        for(int i=0;i<dirs.Count;i++)
        {
            sub[i]=[];string prefix=dirs[i].Length==0?"":dirs[i]+"/";
            foreach(string f in files.Keys.Where(p=>Parent(p)==dirs[i]).Order(StringComparer.Ordinal)){string n=f[prefix.Length..];sub[i].Add((byte)n.Length);sub[i].AddRange(System.Text.Encoding.ASCII.GetBytes(n));}
            for(int j=0;j<dirs.Count;j++)if(j>0&&Parent(dirs[j])==dirs[i]){string n=dirs[j][prefix.Length..];sub[i].Add((byte)(0x80|n.Length));sub[i].AddRange(System.Text.Encoding.ASCII.GetBytes(n));sub[i].AddRange(BitConverter.GetBytes((ushort)(0xF000|j)));}
            sub[i].Add(0);
        }
        var fnt=new List<byte>();int at=dirs.Count*8;
        for(int i=0;i<dirs.Count;i++){fnt.AddRange(BitConverter.GetBytes(at));fnt.AddRange(BitConverter.GetBytes((ushort)first[i]));fnt.AddRange(BitConverter.GetBytes((ushort)(i==0?dirs.Count:0xF000|dirs.IndexOf(Parent(dirs[i])))));at+=sub[i].Count;}
        foreach(var s in sub)fnt.AddRange(s);
        int fntOffset=0x1000,fatOffset=(fntOffset+fnt.Count+0x1FF)&~0x1FF,dataStart=(fatOffset+order.Count*8+0x1FF)&~0x1FF;
        int end=dataStart;var starts=new int[order.Count];for(int i=0;i<order.Count;i++){starts[i]=end;end=(end+files[order[i]].Length+0x1FF)&~0x1FF;}
        byte[] rom=Enumerable.Repeat((byte)0xFF,end+0x10000).ToArray();rom.AsSpan(0,0x1000).Clear();
        "SYNTHETIC"u8.CopyTo(rom);System.Text.Encoding.ASCII.GetBytes(gameCode).CopyTo(rom,0x0C);rom[0x1E]=version;fnt.ToArray().CopyTo(rom,fntOffset);
        for(int i=0;i<order.Count;i++){byte[] f=files[order[i]];f.CopyTo(rom,starts[i]);BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(fatOffset+i*8),(uint)starts[i]);BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(fatOffset+i*8+4),(uint)(starts[i]+f.Length));}
        void U32(int o,int v)=>BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(o),(uint)v);
        U32(0x40,fntOffset);U32(0x44,fnt.Count);U32(0x48,fatOffset);U32(0x4C,order.Count*8);U32(0x80,end);U32(0x84,0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(0x15E),NdsRom.Crc16(rom.AsSpan(0,0x15E)));
        return rom;
    }
}
