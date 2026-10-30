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

    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}

    // Black (U) code and paths with small tables: 650 species whose BST equals 6*species, three trainers and one wild area.
    private static byte[] SyntheticBlack()
    {
        var text=new byte[192][];for(int i=0;i<text.Length;i++)text[i]=[];
        text[70]=Gen5Message(Enumerable.Range(0,650).Select(i=>"Especie"+i).ToArray());text[190]=Gen5Message(["","Ana","Beto"]);text[191]=Gen5Message(["Clase"]);
        var stats=Enumerable.Range(0,650).Select(s=>Enumerable.Repeat((byte)Math.Min(255,s),6).Concat(new byte[]{11,3}).ToArray()).ToArray();
        byte[] header(int flags,int count){var h=new byte[20];h[0]=(byte)flags;h[3]=(byte)count;return h;}
        byte[] mon(int level,int species,int form){var m=new byte[8];BinaryPrimitives.WriteUInt16LittleEndian(m.AsSpan(2),(ushort)level);BinaryPrimitives.WriteUInt16LittleEndian(m.AsSpan(4),(ushort)species);BinaryPrimitives.WriteUInt16LittleEndian(m.AsSpan(6),(ushort)form);return m;}
        byte[] u16(params int[] v)=>v.SelectMany(x=>BitConverter.GetBytes((ushort)x)).ToArray();
        byte[] team1=[..mon(12,10,1),..u16(50),..u16(1,2,3,4),..mon(14,11,0),..u16(51),..u16(5,6,7,8)];
        var wild=new byte[232];wild[0]=20;for(int i=0;i<12;i++){BinaryPrimitives.WriteUInt16LittleEndian(wild.AsSpan(8+i*4),(ushort)((i<6?504:506)|(1<<11)));wild[10+i*4]=3;wild[11+i*4]=4;}
        var files=new Dictionary<string,byte[]>
        {
            ["a/0/0/2"]=BuildNarc(text),["a/0/1/6"]=BuildNarc(stats),["a/0/5/7"]=BuildNarc([]),
            ["a/0/9/2"]=BuildNarc([new byte[20],header(3,2),header(0,1)]),["a/0/9/3"]=BuildNarc([[],team1,mon(15,20,0)]),["a/1/2/6"]=BuildNarc([wild]),
        };
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
    internal static byte[] BuildRom(IReadOnlyDictionary<string,byte[]> files,string gameCode)
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
        "SYNTHETIC"u8.CopyTo(rom);System.Text.Encoding.ASCII.GetBytes(gameCode).CopyTo(rom,0x0C);fnt.ToArray().CopyTo(rom,fntOffset);
        for(int i=0;i<order.Count;i++){byte[] f=files[order[i]];f.CopyTo(rom,starts[i]);BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(fatOffset+i*8),(uint)starts[i]);BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(fatOffset+i*8+4),(uint)(starts[i]+f.Length));}
        void U32(int o,int v)=>BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(o),(uint)v);
        U32(0x40,fntOffset);U32(0x44,fnt.Count);U32(0x48,fatOffset);U32(0x4C,order.Count*8);U32(0x80,end);U32(0x84,0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(0x15E),NdsRom.Crc16(rom.AsSpan(0,0x15E)));
        return rom;
    }
}
