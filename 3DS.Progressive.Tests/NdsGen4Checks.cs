using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.NDS;

// Gen4 (Diamond, Platinum, HeartGold): synthetic layouts for CI, plus a probe that exports from real dumps to isolated outputs.
internal static class NdsGen4Checks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var lines=Gen4Text.Read(Gen4Message(["","TURTWIG","Ana Beto","Cafe é"],compressed:2));
        check(lines.SequenceEqual(new[]{"","TURTWIG","Ana Beto","Cafe é"}),"Gen4Text_DecryptsPlainAndCompressedLines");
        check(Gen4Text.Read(Gen4Message(["￾"]))[0].StartsWith(@"\v",StringComparison.Ordinal)&&Gen4Text.Read(Gen4Message([]))is{Count:0},"Gen4Text_KeepsVariablesAndReadsEmptyFiles");
        reject(()=>Gen4Text.Read([1,0,0,0]),"Gen4Text_RejectsTruncatedLineTable");

        string temp=Path.Combine(Path.GetTempPath(),"nds-gen4-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            foreach(var kind in new[]{Gen4Kind.DiamondPearl,Gen4Kind.HeartGoldSoulSilver})
            {
                var profile=Gen4Profile.All.Single(p=>p.Kind==kind);string name=kind==Gen4Kind.DiamondPearl?"Dp":"Hgss";
                string path=Path.Combine(temp,name+".nds");File.WriteAllBytes(path,Synthetic(profile));
                using var rom=NdsRom.Open(path);var data=Gen4Data.Open(rom,requireOriginalTables:false);
                var trainers=data.Trainers();var slots=data.Encounters();
                check(trainers.Count==2&&trainers[0].Name=="ANA"&&trainers[0].ClassName=="Leader"&&trainers[0].Team.Select(m=>(m.Species,m.Level,m.HeldItem)).SequenceEqual(new[]{(387,12,0),(388,14,0)})
                    &&trainers[1].Team.Single() is {Species:300,Form:1,HeldItem:17,Moves.Count:4},$"Gen4_{name}_ReadsTrainersWithTheirEntrySize");
                int expected=kind==Gen4Kind.DiamondPearl?12+1+5:36+1+5+1;
                check(slots.Count==expected&&slots.All(s=>s.Species is >=1 and <=Gen4Data.MaxSpecies),$"Gen4_{name}_ReadsGrassAlternateAndWaterSlots ({slots.Count})");
                var keep=Gen4Randomizer.Generate(data,new Gen5RandomizerOptions(3));check(keep.Files.Count==0&&keep.Changes.Count==0,$"Gen4_{name}_KeepChangesNothing");
                reject(()=>Gen4Randomizer.Generate(data,new Gen5RandomizerOptions(3){Starters=Gen5StarterMode.Custom,CustomStarters=[1,1,4]}),$"Gen4_{name}_RejectsRepeatedCustomStarters");
                reject(()=>Gen4Randomizer.Generate(data,new Gen5RandomizerOptions(3){Starters=Gen5StarterMode.Custom,CustomStarters=[1,4,494]}),$"Gen4_{name}_RejectsStartersOutsideGen4");
                var options=new Gen5RandomizerOptions(3){Trainers=Gen5SpeciesMode.Random,Wild=Gen5SpeciesMode.Random};
                var a=Gen4Randomizer.Generate(data,options);var b=Gen4Randomizer.Generate(data,options);var c=Gen4Randomizer.Generate(data,options with{Seed=4});
                check(a.Changes.SequenceEqual(b.Changes)&&!a.Changes.SequenceEqual(c.Changes)&&a.Overlays.Count==0,$"Gen4_{name}_DeterministicBySeed");
                string target=Path.Combine(temp,name+"-out.nds");reject(()=>data.Export(target,a),$"Gen4_{name}_ExportRequiresVerifiedOriginalTables");
                // Synthetic tables cannot match the profile CRC32, so the tables are written the way Export writes them.
                rom.Save(target,a.Files.ToDictionary(f=>rom.File(f.Key).Id,f=>f.Value));
                using var exported=NdsRom.Open(target);var again=Gen4Data.Open(exported,requireOriginalTables:false);
                var newTrainers=again.Trainers();var moved=newTrainers[1].Team.Single();
                check(newTrainers.Zip(trainers).All(t=>t.First.Team.Select(m=>(m.Level,m.HeldItem,m.Difficulty)).SequenceEqual(t.Second.Team.Select(m=>(m.Level,m.HeldItem,m.Difficulty))))
                    &&moved is {Form:0,Moves.Count:0}&&!newTrainers[1].HasMoves&&newTrainers[1].HasItems,$"Gen4_{name}_TrainersKeepLevelsAndItemsAndDropCustomMoves");
                byte[] team=Narc.Read(exported.ReadFile(data.PathOf(Gen4Profile.TrainerPokemon))).Files[2];
                check(team.Length==data.TeamEntrySize(2)+3&&(kind==Gen4Kind.DiamondPearl||team[data.TeamEntrySize(2)-2]==0xAB)&&team[^3..].SequenceEqual(new byte[]{9,9,9}),$"Gen4_{name}_TeamEntriesKeepSealAndTrailingBytes");
                var newSlots=again.Encounters();
                check(newSlots.Count==slots.Count&&newSlots.Zip(slots).All(s=>s.First.Offset==s.Second.Offset&&s.First.MinLevel==s.Second.MinLevel&&s.First.MaxLevel==s.Second.MaxLevel)
                    &&newSlots.Zip(slots).Where(s=>s.Second.Group==slots[0].Group).GroupBy(s=>s.Second.Species).All(g=>g.Select(s=>s.First.Species).Distinct().Count()==1),$"Gen4_{name}_EncountersKeepLevelsAndMapEachSpeciesOnce");
                byte[] wild=Narc.Read(exported.ReadFile(data.PathOf(Gen4Profile.WildPokemon))).Files[0],oldWild=Narc.Read(rom.ReadFile(data.PathOf(Gen4Profile.WildPokemon))).Files[0];
                check(Enumerable.Range(0,wild.Length).Where(i=>wild[i]!=oldWild[i]).All(i=>newSlots.Any(s=>i>=s.Offset&&i<s.Offset+s.Width)),$"Gen4_{name}_OnlySpeciesBytesChangeInEncounterFiles");
            }
            string bad=Path.Combine(temp,"bad.nds");var dp=Gen4Profile.All.Single(p=>p.Kind==Gen4Kind.DiamondPearl);File.WriteAllBytes(bad,Synthetic(dp));
            using(var rom=NdsRom.Open(bad))reject(()=>Gen4Data.Open(rom),"Gen4_StrictOpenRejectsTablesThatAreNotTheOriginals");
            byte[] message=Gen4Message(["","TURTWIG la hoja","Ana Beto"],compressed:2);
            var edited=Gen4Text.Read(Gen4Text.Replace(message,new Dictionary<int,string>{[1]=@"\vFF00\z0003PIKACHU\vFF00\z0000!\nWill you?"}));
            check(edited.Count==3&&edited[0]==""&&edited[1]==@"\vFF00\z0003PIKACHU\vFF00\z0000!\nWill you?"&&edited[2]=="Ana Beto",$"Gen4Text_ReplaceRewritesOneLineAndKeepsTheOthers");
            reject(()=>Gen4Text.Replace(message,new Dictionary<int,string>{[1]="\u4E00\u4E01"}),"Gen4Text_ReplaceRejectsCharactersOutsideTheTable");
            check(new[]{1,17,111,135,213,327}.All(Gen4Randomizer.IsMovableItem)&&!new[]{0,112,134,328,419,420,427,428,464,536,537}.Any(Gen4Randomizer.IsMovableItem),"Gen4_TmsHmsKeyAndExclusiveItemsNeverMove");
            check(Gen4Profile.All.All(p=>p.ShopPrefix.Count==16&&p.FixedShops.All(s=>s<p.ShopCount)&&p.HiddenItemCount>200&&p.Files.ContainsKey(Gen4Profile.Scripts)),"Gen4_ProfilesDescribeItemsAndShops");
            Arm9Checks(check,reject,temp);
        }
        finally{try{Directory.Delete(temp,true);}catch(IOException){}}
    }

    // ARM9 writer: an uncompressed binary is replaced in place; a BLZ-compressed one is compressed again with its first 0x4000
    // bytes raw and the module parameters' compressed end updated; DSi-enhanced games and size changes are rejected.
    private static void Arm9Checks(Action<bool,string> check,Action<Action,string> reject,string temp)
    {
        var random=new Random(5);
        const int moduleParams=0xBA0;
        byte[] plain=new byte[0x6000];for(int i=0;i<plain.Length;i++)plain[i]=(byte)(i%97<40?0:random.Next(4));
        // An uncompressed binary has no compressed end in its module parameters.
        plain.AsSpan(moduleParams+0x14,4).Clear();
        foreach(bool compressed in new[]{false,true})
        {
            string name=compressed?"Compressed":"Plain";
            byte[] stored=plain;
            if(compressed){stored=pk3DS.Core.CTR.BLZCoder.CompressArm9(plain);BinaryPrimitives.WriteUInt32LittleEndian(stored.AsSpan(moduleParams+0x14),0x2000000u+(uint)stored.Length);}
            string path=Path.Combine(temp,"arm9-"+name+".nds");File.WriteAllBytes(path,RomWithArm9(stored,moduleParams,dsi:false));
            using var rom=NdsRom.Open(path);
            byte[] read=rom.ReadArm9();
            check(read.Length==plain.Length&&(compressed?read.AsSpan(0,moduleParams+0x14).SequenceEqual(plain.AsSpan(0,moduleParams+0x14)):read.SequenceEqual(plain)),$"NdsArm9_{name}_ReadsTheRunningBinary");
            byte[] changed=(byte[])read.Clone();changed[0x5000]^=0x5A;changed[0x10]^=0x11;
            string output=Path.Combine(temp,"arm9-"+name+"-out.nds");rom.Save(output,new Dictionary<int,byte[]>(),null,null,changed);
            using var saved=NdsRom.Open(output);byte[] again=saved.ReadArm9();
            byte[] header=File.ReadAllBytes(output)[..0x200];uint size=BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0x2C));byte[] raw=File.ReadAllBytes(output);
            check(again.AsSpan(0x5000).SequenceEqual(changed.AsSpan(0x5000))&&again[0x10]==changed[0x10]&&BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x4000+(int)size))==0xDEC00621
                &&(!compressed||BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x4000+moduleParams+0x14))==0x2000000u+size),$"NdsArm9_{name}_SavesInPlaceWithFooterAndModuleParams");
            reject(()=>rom.Save(Path.Combine(temp,"arm9-"+name+"-short.nds"),new Dictionary<int,byte[]>(),null,null,changed[..^4]),$"NdsArm9_{name}_RejectsASizeChange");
        }
        string dsi=Path.Combine(temp,"arm9-dsi.nds");File.WriteAllBytes(dsi,RomWithArm9(plain,moduleParams,dsi:true));
        using(var rom=NdsRom.Open(dsi))reject(()=>rom.Save(Path.Combine(temp,"arm9-dsi-out.nds"),new Dictionary<int,byte[]>(),null,null,rom.ReadArm9()),"NdsArm9_RejectsDsiEnhancedGames");
    }

    // Minimal ROM: header, ARM9 at 0x4000 followed by its nitrocode footer, then an empty file table.
    private static byte[] RomWithArm9(byte[] arm9,int moduleParams,bool dsi)
    {
        int fnt=(0x4000+arm9.Length+12+0x1FF)&~0x1FF,fat=fnt+0x200,end=fat+0x200;
        byte[] rom=Enumerable.Repeat((byte)0xFF,end+0x1000).ToArray();rom.AsSpan(0,0x4000).Clear();
        "SYNTHETIC"u8.CopyTo(rom);"TEST"u8.CopyTo(rom.AsSpan(0x0C));if(dsi)rom[0x12]=2;
        void U32(int o,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(o),v);
        U32(0x20,0x4000);U32(0x24,0x2000800);U32(0x28,0x2000000);U32(0x2C,(uint)arm9.Length);arm9.CopyTo(rom,0x4000);
        U32(0x4000+arm9.Length,0xDEC00621);U32(0x4000+arm9.Length+4,(uint)moduleParams);U32(0x4000+arm9.Length+8,0);
        rom.AsSpan(fnt,8).Clear();U32(fnt,8);BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(fnt+6),1);rom[fnt+8]=0;
        U32(0x40,(uint)fnt);U32(0x44,9);U32(0x48,(uint)fat);U32(0x4C,0);U32(0x80,(uint)end);U32(0x84,0x4000);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(0x15E),NdsRom.Crc16(rom.AsSpan(0,0x15E)));
        return rom;
    }

    // Synthetic Gen4 message file: count, key, encrypted pointer table, then encrypted lines (one line optionally 9-bit compressed).
    internal static byte[] Gen4Message(string[] lines,int compressed=-1)
    {
        static int Code(char c)=>c switch{>='A' and <='Z'=>0x12B+c-'A',>='a' and <='z'=>0x145+c-'a',' '=>0x1DE,'é'=>0x188,'￾'=>0xFFFE,_=>throw new ArgumentException(c.ToString())};
        const int fileKey=0x1234;var body=new List<ushort[]>();
        for(int i=0;i<lines.Length;i++)
        {
            var codes=lines[i].Select(Code).ToList();
            if(lines[i]=="￾")codes=[0xFFFE,0x0100,1,0x0002];
            if(i==compressed)
            {
                codes.Add(0x1FF);var units=new List<int>{0xF100};int buffer=0,bits=0;
                foreach(int code in codes){buffer|=code<<bits;bits+=9;while(bits>=15){units.Add(buffer&0x7FFF);buffer>>=15;bits-=15;}}
                if(bits>0)units.Add(buffer&0x7FFF);codes=units;
            }
            codes.Add(0xFFFF);body.Add(codes.Select(c=>(ushort)c).ToArray());
        }
        var data=new byte[4+lines.Length*8+body.Sum(b=>b.Length*2)];
        BinaryPrimitives.WriteUInt16LittleEndian(data,(ushort)lines.Length);BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2),fileKey);
        int key=(fileKey*0x2FD)&0xFFFF,offset=4+lines.Length*8;
        for(int i=0;i<lines.Length;i++)
        {
            int lineKey=(key*(i+1))&0xFFFF;uint mask=(uint)(lineKey|(lineKey<<16));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4+i*8),(uint)offset^mask);BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8+i*8),(uint)body[i].Length^mask);
            int charKey=(0x91BD3*(i+1))&0xFFFF;
            foreach(ushort c in body[i]){BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset),(ushort)(c^charKey));charKey=(charKey+0x493D)&0xFFFF;offset+=2;}
        }
        return data;
    }

    // Two trainers (one without items or moves, one with both) and one encounter file in the layout of the profile's game.
    private static byte[] Synthetic(Gen4Profile profile)
    {
        bool hgss=profile.Kind==Gen4Kind.HeartGoldSoulSilver;
        var text=new byte[Math.Max(profile.TrainerClassesText,profile.SpeciesNamesText)+1][];for(int i=0;i<text.Length;i++)text[i]=Gen4Message([]);
        text[profile.SpeciesNamesText]=Gen4Message(Enumerable.Range(0,494).Select(i=>"S"+new string((char)('a'+i%26),1+i/26)).ToArray());
        text[profile.TrainerNamesText]=Gen4Message(["","ANA","BETO"]);text[profile.TrainerClassesText]=Gen4Message(["Leader"]);
        var stats=Enumerable.Range(0,494).Select(s=>Enumerable.Repeat((byte)Math.Min(255,s/2),6).Concat(new byte[]{1,2}).ToArray()).ToArray();
        int seal=profile.Kind==Gen4Kind.DiamondPearl?0:2;
        byte[] Mon(int level,int species,int form,byte[] extra){var m=new byte[6];m[0]=50;m[1]=0x10;BinaryPrimitives.WriteUInt16LittleEndian(m.AsSpan(2),(ushort)level);BinaryPrimitives.WriteUInt16LittleEndian(m.AsSpan(4),(ushort)(species|form<<10));return [..m,..extra,..Enumerable.Repeat((byte)0xAB,seal)];}
        byte[] Header(int flags,int count){var h=new byte[20];h[0]=(byte)flags;h[3]=(byte)count;return h;}
        byte[] moves=new byte[8];for(int i=0;i<4;i++)BinaryPrimitives.WriteUInt16LittleEndian(moves.AsSpan(i*2),(ushort)(33+i));
        var headers=new[]{new byte[20],Header(0,2),Header(3,1)};
        var teams=new[]{new byte[8],[..Mon(12,387,0,[]),..Mon(14,388,0,[])],[..Mon(30,300,1,[17,0,..moves]),9,9,9]};
        byte[] wild;
        if(hgss)
        {
            wild=new byte[196];wild[0]=30;wild[1]=10;for(int i=0;i<12;i++)wild[8+i]=(byte)(3+i);
            for(int t=0;t<3;t++)for(int i=0;i<12;i++)BinaryPrimitives.WriteUInt16LittleEndian(wild.AsSpan(20+t*24+i*2),(ushort)(i<6?16:19));
            BinaryPrimitives.WriteUInt16LittleEndian(wild.AsSpan(92),(ushort)252);
            for(int i=0;i<5;i++){wild[100+i*4]=20;wild[101+i*4]=30;BinaryPrimitives.WriteUInt16LittleEndian(wild.AsSpan(102+i*4),(ushort)72);}
            BinaryPrimitives.WriteUInt16LittleEndian(wild.AsSpan(188),(ushort)206);
        }
        else
        {
            wild=new byte[424];wild[0]=10;for(int i=0;i<12;i++){wild[4+i*8]=(byte)(2+i);BinaryPrimitives.WriteUInt32LittleEndian(wild.AsSpan(8+i*8),(uint)(i<6?396:399));}
            BinaryPrimitives.WriteUInt32LittleEndian(wild.AsSpan(108),(uint)163);
            wild[204]=20;for(int i=0;i<5;i++){BinaryPrimitives.WriteUInt32LittleEndian(wild.AsSpan(208+i*8),(uint)(20<<8|40));BinaryPrimitives.WriteUInt32LittleEndian(wild.AsSpan(212+i*8),(uint)129);}
        }
        var files=new Dictionary<string,byte[]>
        {
            [profile.Files[Gen4Profile.Text].Path]=NdsGen5RandomizerChecks.BuildNarc(text),
            [profile.Files[Gen4Profile.PokemonStats].Path]=NdsGen5RandomizerChecks.BuildNarc(stats),
            [profile.Files[Gen4Profile.TrainerData].Path]=NdsGen5RandomizerChecks.BuildNarc(headers),
            [profile.Files[Gen4Profile.TrainerPokemon].Path]=NdsGen5RandomizerChecks.BuildNarc(teams),
            [profile.Files[Gen4Profile.WildPokemon].Path]=NdsGen5RandomizerChecks.BuildNarc([wild]),
        };
        return NdsGen5RandomizerChecks.BuildRom(files,profile.GameCode,profile.Version);
    }

    // Real dumps: trainers and encounters randomized, exported to an isolated folder and read back; the source must not change.
    internal static void Probe(string output,IEnumerable<string> roms)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");Directory.CreateDirectory(output);
        foreach(string source in roms.Select(Path.GetFullPath))
        {
            string before=Hash(source);string game;
            using(var rom=NdsRom.Open(source))
            {
                var data=Gen4Data.Open(rom);game=data.Profile.GameCode;var names=data.SpeciesNames();
                var trainers=data.Trainers();var slots=data.Encounters();
                Check(data.Identity.KnownDump&&names[387]=="TURTWIG"&&names[493]=="ARCEUS"&&data.Text(data.Profile.TypeNamesText)[12]=="GRASS",$"Gen4_Real_{game}_KnownDumpAndOwnTexts");
                string[] leaders=data.Profile.Kind==Gen4Kind.HeartGoldSoulSilver?["Falkner","Bugsy","Whitney","Morty","Chuck","Jasmine","Pryce","Clair"]:["Roark","Gardenia","Maylene","Wake","Fantina","Byron","Candice","Volkner"];
                Check(leaders.All(l=>trainers.Any(t=>t.ClassName=="Leader"&&t.Name==l)),$"Gen4_Real_{game}_ReadsAllEightGymLeaders ({trainers.Count} entrenadores)");
                Check(slots.Count>5000&&slots.All(s=>s.Species is >=1 and <=Gen4Data.MaxSpecies),$"Gen4_Real_{game}_ReadsEncounterSlots ({slots.Count})");
                var field=data.FieldItems();var shops=data.Shops();
                Check(field.Count(f=>!f.Hidden)>250&&field.Count(f=>f.Hidden)==data.Profile.HiddenItemCount&&shops.Select(s=>s.Shop).Distinct().Count()==data.Profile.ShopCount,$"Gen4_Real_{game}_ReadsFieldItemsAndShops ({field.Count(f=>!f.Hidden)} visibles, {field.Count(f=>f.Hidden)} ocultos, {shops.Count} productos)");
                var options=new Gen5RandomizerOptions(2026){Trainers=Gen5SpeciesMode.ProgressiveBst,Wild=Gen5SpeciesMode.SimilarBst,FieldItems=Gen5ItemMode.Shuffle,Shops=Gen5ItemMode.Shuffle};
                var result=Gen4Randomizer.Generate(data,options);
                Check(result.Files.Count==4&&result.Arm9 is not null&&result.Changes.Count>5000,$"Gen4_Real_{game}_ChangesTrainersEncountersItemsAndShops ({result.Changes.Count} cambios)");
                ProbeStarters(data,rom,output,game,Check);
                string target=Path.Combine(output,game+".nds");data.Export(target,result);
                using var exported=NdsRom.Open(target);var check=Gen4Data.Open(exported,requireOriginalTables:false);
                var newTrainers=check.Trainers();
                Check(newTrainers.Count==trainers.Count&&newTrainers.Zip(trainers).All(t=>t.First.Team.Count==t.Second.Team.Count&&t.First.Team.Zip(t.Second.Team).All(m=>m.First.Level==m.Second.Level&&m.First.HeldItem==m.Second.HeldItem&&m.First.Difficulty==m.Second.Difficulty&&m.First.Species is >=1 and <=Gen4Data.MaxSpecies)),$"Gen4_Real_{game}_TrainersKeepSizesLevelsAndItems");
                var newSlots=check.Encounters();
                Check(newSlots.Count==slots.Count&&newSlots.Zip(slots).All(s=>s.First.Offset==s.Second.Offset&&s.First.MinLevel==s.Second.MinLevel&&s.First.MaxLevel==s.Second.MaxLevel),$"Gen4_Real_{game}_EncountersKeepSlotsAndLevels");
                static string Bag(IEnumerable<int> items)=>string.Join(",",items.Order());
                var newField=check.FieldItems();
                Check(newField.Count==field.Count&&newField.Zip(field).All(f=>f.First.Offset==f.Second.Offset&&(Gen4Randomizer.IsMovableItem(f.Second.Item)||f.First.Item==f.Second.Item))&&Bag(newField.Select(f=>f.Item))==Bag(field.Select(f=>f.Item))
                    &&newField.Zip(field).Count(f=>f.First.Item!=f.Second.Item)>200,$"Gen4_Real_{game}_FieldItemsSameItemsAndFixedOnesStay");
                var newShops=check.Shops();
                Check(newShops.Count==shops.Count&&newShops.Zip(shops).All(s=>s.First.Offset==s.Second.Offset&&(data.Profile.FixedShops.Contains(s.Second.Shop)||!Gen4Randomizer.IsMovableItem(s.Second.Item)?s.First.Item==s.Second.Item:Gen4Randomizer.IsMovableItem(s.First.Item)))
                    &&Bag(newShops.Select(s=>s.Item))==Bag(shops.Select(s=>s.Item)),$"Gen4_Real_{game}_ShopsShuffledExceptFixedLists");
                byte[] arm9=exported.ReadArm9();
                // A recompressed ARM9 differs only in the 4-byte "compressed end" of its module parameters.
                var arm9Diff=Enumerable.Range(0,arm9.Length).Where(i=>arm9[i]!=result.Arm9![i]).ToArray();
                Check(arm9.Length==result.Arm9!.Length&&(arm9Diff.Length==0||arm9Diff[^1]-arm9Diff[0]<4)&&exported.Overlays.All(o=>exported.ReadFile(exported.Files[o.FileId]).SequenceEqual(rom.ReadFile(rom.Files[o.FileId]))),$"Gen4_Real_{game}_Arm9WrittenAndOverlaysUntouched ({(arm9Diff.Length==0?"sin comprimir":"recomprimido")}, {exported.Header.NtrEnd==rom.Header.NtrEnd})");
                var changedIds=result.Files.Keys.Select(p=>rom.File(p).Id).ToHashSet();
                Check(exported.Files.Where(f=>!changedIds.Contains(f.Id)).All(f=>exported.ReadFile(f).SequenceEqual(rom.ReadFile(rom.Files[f.Id]))),$"Gen4_Real_{game}_EveryOtherFileIdentical");
                Check(Gen4Randomizer.Generate(data,options).Changes.SequenceEqual(result.Changes),$"Gen4_Real_{game}_SameSeedSameResult");
            }
            foreach(string f in Directory.GetFiles(output))File.Delete(f);
            Check(Hash(source)==before,$"Gen4_Real_{game}_SourceUnchanged");
        }
        Console.WriteLine($"Completed {checks} Gen4 real-dump checks; outputs deleted from {output}.");
    }

    // Custom starters: table, cries or selection graphics, rival and tag-battle scripts and the selection text, then re-read.
    private static void ProbeStarters(Gen4Data data,NdsRom rom,string output,string game,Action<bool,string> Check)
    {
        bool hgss=data.Profile.Kind==Gen4Kind.HeartGoldSoulSilver;int[] original=hgss?[152,155,158]:[387,390,393],chosen=[25,4,7];
        Check(data.Starters().SequenceEqual(original),$"Gen4Starters_Real_{game}_ReadsTheOriginalStarters");
        var result=Gen4Randomizer.Generate(data,new Gen5RandomizerOptions(9){Starters=Gen5StarterMode.Custom,CustomStarters=chosen});
        Check(result.Changes.Count==3&&result.Overlays.Keys.SequenceEqual(new[]{data.Profile.StarterOverlay})&&result.Files.Keys.Order().SequenceEqual(new[]{data.PathOf(Gen4Profile.Scripts),data.PathOf(Gen4Profile.Text)}.Order())&&(result.Arm9 is not null)==hgss,
            $"Gen4Starters_Real_{game}_TouchesTableScriptsTextAndStarterOverlayOnly");
        string target=Path.Combine(output,game+"-starters.nds");data.Export(target,result);
        using var exported=NdsRom.Open(target);var check=Gen4Data.Open(exported,requireOriginalTables:false);
        Check(check.Starters().SequenceEqual(chosen),$"Gen4Starters_Real_{game}_TableHasTheNewStarters");
        byte[] overlay=exported.ReadOverlay(data.Profile.StarterOverlay),oldOverlay=rom.ReadOverlay(data.Profile.StarterOverlay);
        var changedBytes=Enumerable.Range(0,overlay.Length).Where(i=>overlay[i]!=oldOverlay[i]).ToArray();
        Check(overlay.Length==oldOverlay.Length&&(hgss?changedBytes.Length<=6&&exported.Overlay(61).Compressed&&exported.Files[exported.Overlay(61).FileId].Start==rom.Files[rom.Overlay(61).FileId].Start
            :changedBytes.All(i=>i<data.Profile.StarterOffset+12&&(i>=data.Profile.StarterOffset||i<0x800))),$"Gen4Starters_Real_{game}_OverlayChangesOnlyTheStarterData ({changedBytes.Length} bytes)");
        var scripts=Narc.Read(exported.ReadFile(data.PathOf(Gen4Profile.Scripts)));var oldScripts=Narc.Read(rom.ReadFile(data.PathOf(Gen4Profile.Scripts)));
        var changedScripts=Enumerable.Range(0,scripts.Files.Count).Where(i=>!scripts.Files[i].SequenceEqual(oldScripts.Files[i])).ToArray();
        Check(changedScripts.All(i=>data.Profile.RivalScripts.Contains(i)||data.Profile.TagScripts.Contains(i))&&changedScripts.Length>=data.Profile.RivalScripts.Count,$"Gen4Starters_Real_{game}_OnlyRivalAndTagScriptsChange ({changedScripts.Length})");
        var lines=Gen4Text.Read(Narc.Read(exported.ReadFile(data.PathOf(Gen4Profile.Text))).Files[data.Profile.StarterScreenText]);
        var oldLines=Gen4Text.Read(Narc.Read(rom.ReadFile(data.PathOf(Gen4Profile.Text))).Files[data.Profile.StarterScreenText]);
        int[] named=hgss?[1,2,3,4,5,6]:[1,2,3];
        Check(lines.Count==oldLines.Count&&lines[1].Contains("PIKACHU")&&lines[2].Contains("CHARMANDER")&&lines[3].Contains("SQUIRTLE")&&(!hgss||lines[4].Contains("Electric-type")&&lines[6].Contains("Water-type"))
            &&(hgss||lines[1].Contains("Mouse Pokémon"))&&lines.Where((l,i)=>!named.Contains(i)).SequenceEqual(oldLines.Where((l,i)=>!named.Contains(i))),$"Gen4Starters_Real_{game}_SelectionTextNamesTheNewStarters");
        var random=Gen4Randomizer.Generate(data,new Gen5RandomizerOptions(77){Starters=Gen5StarterMode.Random});
        Check(random.Changes.Select(c=>c.After).Distinct().Count()==random.Changes.Count&&random.Changes.SequenceEqual(Gen4Randomizer.Generate(data,new Gen5RandomizerOptions(77){Starters=Gen5StarterMode.Random}).Changes),$"Gen4Starters_Real_{game}_RandomStartersDistinctAndReproducible");
    }

    private static string Hash(string path)
    {
        using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));
    }
}
