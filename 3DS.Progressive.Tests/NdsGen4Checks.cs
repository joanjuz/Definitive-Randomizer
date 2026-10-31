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
                reject(()=>Gen4Randomizer.Generate(data,new Gen5RandomizerOptions(3){Starters=Gen5StarterMode.Random}),$"Gen4_{name}_RejectsOptionsItDoesNotSupport");
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
        }
        finally{try{Directory.Delete(temp,true);}catch(IOException){}}
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
                var options=new Gen5RandomizerOptions(2026){Trainers=Gen5SpeciesMode.ProgressiveBst,Wild=Gen5SpeciesMode.SimilarBst};
                var result=Gen4Randomizer.Generate(data,options);
                Check(result.Files.Count==3&&result.Changes.Count>5000,$"Gen4_Real_{game}_ChangesTrainersAndEncounters ({result.Changes.Count} cambios)");
                string target=Path.Combine(output,game+".nds");data.Export(target,result);
                using var exported=NdsRom.Open(target);var check=Gen4Data.Open(exported,requireOriginalTables:false);
                var newTrainers=check.Trainers();
                Check(newTrainers.Count==trainers.Count&&newTrainers.Zip(trainers).All(t=>t.First.Team.Count==t.Second.Team.Count&&t.First.Team.Zip(t.Second.Team).All(m=>m.First.Level==m.Second.Level&&m.First.HeldItem==m.Second.HeldItem&&m.First.Difficulty==m.Second.Difficulty&&m.First.Species is >=1 and <=Gen4Data.MaxSpecies)),$"Gen4_Real_{game}_TrainersKeepSizesLevelsAndItems");
                var newSlots=check.Encounters();
                Check(newSlots.Count==slots.Count&&newSlots.Zip(slots).All(s=>s.First.Offset==s.Second.Offset&&s.First.MinLevel==s.Second.MinLevel&&s.First.MaxLevel==s.Second.MaxLevel),$"Gen4_Real_{game}_EncountersKeepSlotsAndLevels");
                var changedIds=result.Files.Keys.Select(p=>rom.File(p).Id).ToHashSet();
                Check(exported.Files.Where(f=>!changedIds.Contains(f.Id)).All(f=>exported.ReadFile(f).SequenceEqual(rom.ReadFile(rom.Files[f.Id]))),$"Gen4_Real_{game}_EveryOtherFileIdentical");
                Check(Gen4Randomizer.Generate(data,options).Changes.SequenceEqual(result.Changes),$"Gen4_Real_{game}_SameSeedSameResult");
            }
            foreach(string f in Directory.GetFiles(output))File.Delete(f);
            Check(Hash(source)==before,$"Gen4_Real_{game}_SourceUnchanged");
        }
        Console.WriteLine($"Completed {checks} Gen4 real-dump checks; outputs deleted from {output}.");
    }

    private static string Hash(string path)
    {
        using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));
    }
}
