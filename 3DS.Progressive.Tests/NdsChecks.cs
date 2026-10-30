using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.NDS;

// Nintendo DS container and NARC checks: synthetic for CI, plus a probe for real dumps on isolated outputs.
internal static class NdsChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        string temp=Path.Combine(Path.GetTempPath(),"nds-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            string source=Path.Combine(temp,"source.nds");byte[] original=SyntheticRom();File.WriteAllBytes(source,original);
            using var rom=NdsRom.Open(source);
            check(rom.Files.Select(f=>f.Path).SequenceEqual(new[]{"a.bin","b.bin","dat/c.bin","overlay/0003"}),"Nds_ReadsNestedFileNamesAndUnnamedOverlay");
            check(rom.ReadFile("dat/c.bin").SequenceEqual(Enumerable.Repeat((byte)0xC3,8))&&rom.Header.GameCode=="TEST"&&!rom.Header.IsDsiEnhanced,"Nds_ReadsHeaderAndFileContents");
            string same=Path.Combine(temp,"same.nds");rom.Save(same,new Dictionary<int,byte[]>());
            check(File.ReadAllBytes(same).SequenceEqual(original),"Nds_SaveWithoutChangesIsByteIdentical");
            byte[] grown=Enumerable.Repeat((byte)0xA1,0x18).ToArray();string inPlace=Path.Combine(temp,"in-place.nds");rom.Save(inPlace,new Dictionary<int,byte[]>{[0]=grown});
            using(var edited=NdsRom.Open(inPlace))check(edited.Files[0].Start==0x2000&&edited.ReadFile("a.bin").SequenceEqual(grown)&&edited.ReadFile("b.bin").SequenceEqual(rom.ReadFile("b.bin"))&&edited.Header.NtrEnd==rom.Header.NtrEnd,"Nds_WritesInPlaceWhenFileFitsItsSlot");
            string shrink=Path.Combine(temp,"shrink.nds");rom.Save(shrink,new Dictionary<int,byte[]>{[1]=[1,2,3,4]});byte[] shrunk=File.ReadAllBytes(shrink);
            check(shrunk.AsSpan(0x2204,0x1C).ToArray().All(b=>b==0xFF),"Nds_ShrunkFileLeavesPaddingNotStaleData");
            byte[] large=Enumerable.Repeat((byte)0xC9,0x300).ToArray();string moved=Path.Combine(temp,"moved.nds");rom.Save(moved,new Dictionary<int,byte[]>{[2]=large});byte[] movedBytes=File.ReadAllBytes(moved);
            using(var edited=NdsRom.Open(moved))check(edited.Files[2].Start==0x2800&&edited.ReadFile("dat/c.bin").SequenceEqual(large)&&edited.Header.NtrEnd==0x2B00&&movedBytes.AsSpan(0x2400,8).ToArray().All(b=>b==0xFF)&&edited.ReadFile("overlay/0003").SequenceEqual(rom.ReadFile("overlay/0003")),"Nds_RelocatesGrownFileToFreePaddingAndUpdatesHeader");
            string tooLarge=Path.Combine(temp,"too-large.nds");reject(()=>rom.Save(tooLarge,new Dictionary<int,byte[]>{[2]=new byte[0x10000]}),"Nds_RejectsFileWithoutFreeSpace");check(!File.Exists(tooLarge)&&!File.Exists(tooLarge+".partial"),"Nds_RejectedSaveLeavesNoOutput");
            bool overlayRejected=false;try{rom.Save(Path.Combine(temp,"overlay.nds"),new Dictionary<int,byte[]>{[3]=[0]});}catch(NotSupportedException){overlayRejected=true;}check(overlayRejected,"Nds_OverlayFilesOnlyChangeThroughTheOverlayTable");
            check(rom.Overlays.Count==1&&rom.Overlay(0) is {FileId:3,RamSize:0x10,Compressed:false}&&rom.ReadOverlay(0).SequenceEqual(rom.ReadFile("overlay/0003")),"Nds_ReadsOverlayTable");
            byte[] newOverlay=Enumerable.Range(0,16).Select(i=>(byte)i).ToArray();string overlayOut=Path.Combine(temp,"overlay-ok.nds");rom.Save(overlayOut,new Dictionary<int,byte[]>(),new Dictionary<int,byte[]>{[0]=newOverlay});
            using(var edited=NdsRom.Open(overlayOut))check(edited.ReadOverlay(0).SequenceEqual(newOverlay)&&edited.Overlay(0) is {StoredSize:16,Compressed:false}&&edited.ReadFile("a.bin").SequenceEqual(rom.ReadFile("a.bin")),"Nds_ReplacesOverlayUncompressedAndUpdatesItsTable");
            reject(()=>rom.Save(Path.Combine(temp,"overlay-size.nds"),new Dictionary<int,byte[]>(),new Dictionary<int,byte[]>{[0]=new byte[8]}),"Nds_RejectsOverlayWithDifferentSize");
            reject(()=>rom.Save(source,new Dictionary<int,byte[]>()),"Nds_RejectsWritingOverTheSource");reject(()=>rom.Save(same,new Dictionary<int,byte[]>()),"Nds_RejectsExistingOutput");
            check(File.ReadAllBytes(source).SequenceEqual(original),"Nds_SourceRomIsNeverModified");
            byte[] corrupt=(byte[])original.Clone();corrupt[0x20]^=1;string bad=Path.Combine(temp,"bad.nds");File.WriteAllBytes(bad,corrupt);reject(()=>NdsRom.Open(bad).Dispose(),"Nds_RejectsHeaderWithWrongCrc");
            byte[] narcBytes=SyntheticNarc();var narc=Narc.Read(narcBytes);
            check(narc.Files.Count==3&&narc.Files[0].Length==3&&narc.Files[2].Length==0&&narc.Write().SequenceEqual(narcBytes),"Nds_NarcRoundtripIsByteIdentical");
            narc.Files[1]=[9,9,9,9,9,9];var rebuilt=Narc.Read(narc.Write());check(rebuilt.Files[1].SequenceEqual(new byte[]{9,9,9,9,9,9})&&rebuilt.Files[0].SequenceEqual(new byte[]{1,2,3}),"Nds_NarcRebuildsChangedFileAndKeepsOthers");
            byte[] truncated=narcBytes[..0x20];reject(()=>Narc.Read(truncated),"Nds_NarcRejectsTruncatedArchive");
            check(Gen5Data.Crc32("123456789"u8)==0xCBF43926,"Nds_Crc32MatchesStandardCheckValue");
            var lines=Gen5Text.Read(Gen5Message(["Snivy","Línea\nfin",""]));check(lines.Count==3&&lines[0]=="Snivy"&&lines[1]==@"Línea\x000Afin"&&lines[2]=="","Nds_Gen5TextDecryptsLinesAndKeepsControlCodes");
            reject(()=>Gen5Text.Read(new byte[8]),"Nds_Gen5TextRejectsTruncatedFile");
            byte[] message=Gen5Message(["Uno","Planta y más","Tres"]);
            check(Gen5Text.Replace(message,new Dictionary<int,string>()).SequenceEqual(message)&&Gen5Text.Replace(message,new Dictionary<int,string>{[1]="Planta y más"}).SequenceEqual(message),"Nds_Gen5TextRewriteWithoutChangesIsByteIdentical");
            var rewritten=Gen5Text.Read(Gen5Text.Replace(message,new Dictionary<int,string>{[1]="Fuego con \\xFFFE salto"}));
            check(rewritten.Count==3&&rewritten[0]=="Uno"&&rewritten[1]=="Fuego con \\xFFFE salto"&&rewritten[2]=="Tres","Nds_Gen5TextReplacesOneLineAndKeepsTheOthers");
        }
        finally{Directory.Delete(temp,true);}
    }

    // Opens real dumps read-only, writes only to new files under output, and verifies the source hash afterwards.
    internal static void Probe(string output,IEnumerable<string> roms)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");Directory.CreateDirectory(output);
        foreach(string path in roms.Select(Path.GetFullPath))
        {
            string before=Hash(path);string name=Path.GetFileNameWithoutExtension(path);
            using(var rom=NdsRom.Open(path))
            {
                var identity=NdsIdentity.Identify(rom);string game=identity.Profile.GameCode;
                Check(identity.KnownDump,$"Nds_Real_{game}_IsKnownOriginalDump ({identity.Profile.Game}, {rom.Files.Count} archivos)");
                int narcs=0,members=0;
                foreach(var entry in rom.Files)
                {
                    byte[] data=rom.ReadFile(entry);if(!Narc.IsNarc(data))continue;
                    var narc=Narc.Read(data);if(!narc.Write().SequenceEqual(data))throw new Exception("FAIL Nds_Real_"+game+"_NarcRoundtrip "+entry.Path);
                    narcs++;members+=narc.Files.Count;
                }
                Check(narcs>0,$"Nds_Real_{game}_AllNarcsRoundtripByteIdentical ({narcs} NARC, {members} miembros)");
                string same=Path.Combine(output,name+"-same.nds");rom.Save(same,new Dictionary<int,byte[]>());
                Check(Hash(same)==before,$"Nds_Real_{game}_SaveWithoutChangesIsByteIdentical");File.Delete(same);
                var target=rom.Files.Where(f=>!f.Path.StartsWith("overlay/",StringComparison.Ordinal)&&f.Length>=16).OrderBy(f=>f.Length).First();
                byte[] changed=rom.ReadFile(target);changed[0]^=0xFF;string inPlace=Path.Combine(output,name+"-in-place.nds");rom.Save(inPlace,new Dictionary<int,byte[]>{[target.Id]=changed});
                using(var edited=NdsRom.Open(inPlace))Check(edited.ReadFile(edited.Files[target.Id]).SequenceEqual(changed)&&edited.Files.Where(f=>f.Id!=target.Id).All(f=>f.Start==rom.Files[f.Id].Start&&f.End==rom.Files[f.Id].End),$"Nds_Real_{game}_InPlaceChangeKeepsEveryOtherFile ({target.Path})");
                File.Delete(inPlace);
                byte[] grown=rom.ReadFile(target).Concat(new byte[(int)rom.SlotLength(target)]).ToArray();string moved=Path.Combine(output,name+"-moved.nds");rom.Save(moved,new Dictionary<int,byte[]>{[target.Id]=grown});
                using(var edited=NdsRom.Open(moved))Check(edited.Files[target.Id].Start>=rom.Header.NtrEnd&&edited.ReadFile(edited.Files[target.Id]).SequenceEqual(grown)&&edited.Files.Where(f=>f.Id!=target.Id).All(f=>edited.ReadFile(f).SequenceEqual(rom.ReadFile(f))),$"Nds_Real_{game}_GrownFileRelocatesAndOthersStayIdentical");
                if(rom.Header.IsDsiEnhanced)Check(SameTail(path,moved,rom.FreeSpaceEnd),$"Nds_Real_{game}_DsiAreaAfterFreeSpaceIsUnchanged");
                File.Delete(moved);
            }
            Check(Hash(path)==before,$"Nds_Real_{name}_SourceUnchanged");
        }
        Console.WriteLine($"Completed {checks} NDS real-dump checks; outputs written only under {output}.");
    }

    // Black/White read-only checks against values known from the original games; the ROM is only read.
    internal static void ProbeGen5(IEnumerable<string> roms)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        foreach(string path in roms.Select(Path.GetFullPath))
        {
            string before=Hash(path);
            using(var rom=NdsRom.Open(path))
            {
                var data=Gen5Data.Open(rom);string game=data.Profile.GameCode;
                Check(data.Identity.KnownDump,$"Gen5_{game}_OpensWithEveryTableMatchingItsCrc ({data.Identity.Profile.Game})");
                var names=data.SpeciesNames();Check(names.Count>=650&&names[1]=="Bulbasaur"&&names[495]=="Snivy"&&names[649]=="Genesect",$"Gen5_{game}_SpeciesNames ({names.Count})");
                var stats=data.BaseStats();var bulbasaur=stats[1];var snivy=stats[495];
                Check(bulbasaur is {HP:45,Attack:49,Defense:49,Speed:45,SpAttack:65,SpDefense:65,Type1:11,Type2:3}&&snivy.Total==308&&snivy.Type1==11,$"Gen5_{game}_BaseStats ({stats.Count} entradas)");
                Check(data.Starters().SequenceEqual(new[]{495,498,501}),$"Gen5_{game}_StartersAtEveryScriptPosition");
                var trainers=data.Trainers();var team=trainers.SelectMany(t=>t.Team).ToArray();
                Check(trainers.Count>500&&team.All(p=>p.Species is >=1 and <=649&&p.Level is >=1 and <=100)&&trainers.All(t=>t.BattleType is >=0 and <=3),$"Gen5_{game}_Trainers ({trainers.Count} entrenadores, {team.Length} Pokémon, {trainers.Count(t=>t.BattleType==1)} dobles)");
                // Gen5 names the gym leader class just "Leader" (English) or "Líder" (Spanish).
                var leaders=trainers.Where(t=>t.ClassName is "Leader" or "Líder").ToArray();
                // Exact roster catches misaligned name indices, not just undecodable text.
                string[] expected=game=="IRAS"?["Zeo","Millo","Maíz","Aloe","Camus","Camila","Yakón","Gerania","Junco","Iris","Lirio"]:["Chili","Cilan","Cress","Lenora","Burgh","Elesa","Clay","Skyla","Brycen","Iris","Drayden"];
                Check(leaders.Select(t=>t.Name).Distinct().Order().SequenceEqual(expected.Order()),$"Gen5_{game}_GymLeaderClassAndNamesDecode ({leaders.First().ClassName}: {string.Join(", ",leaders.Select(t=>t.Name).Distinct())})");
                var areas=data.Encounters();var slots=areas.SelectMany(a=>a.Slots).ToArray();
                Check(areas.Count>100&&slots.All(s=>s.Species is >=0 and <=649&&s.MinLevel<=s.MaxLevel&&s.MaxLevel<=100)&&slots.Any(s=>s.Species==504),$"Gen5_{game}_Encounters ({areas.Count} áreas, {slots.Length} huecos)");
            }
            Check(Hash(path)==before,$"Gen5_{Path.GetFileNameWithoutExtension(path)}_SourceUnchanged");
        }
        Console.WriteLine($"Completed {checks} Gen5 read-only checks; no files written.");
    }

    // Encrypts lines like the game: XOR with a key rotated left by 3 per character, ended by 0xFFFF.
    // Layout: sections, lines, sizes, section offset (16); at the section: its size, the line table, then the text.
    private static byte[] Gen5Message(string[] lines)
    {
        var body=new List<byte>();var table=new List<byte>();int tableLength=lines.Length*8;
        foreach(string line in lines)
        {
            ushort[] chars=line.Select(c=>(ushort)c).Append((ushort)0xFFFF).ToArray();
            table.AddRange(BitConverter.GetBytes(4+tableLength+body.Count));table.AddRange(BitConverter.GetBytes((ushort)chars.Length));table.AddRange(new byte[2]);
            int key=0x1234+line.Length;
            foreach(ushort c in chars){body.AddRange(BitConverter.GetBytes((ushort)(c^key)));key=((key<<3)|(key>>13))&0xFFFF;}
        }
        int sectionSize=4+tableLength+body.Count;var data=new List<byte>();
        data.AddRange(BitConverter.GetBytes((ushort)1));data.AddRange(BitConverter.GetBytes((ushort)lines.Length));data.AddRange(BitConverter.GetBytes(sectionSize));data.AddRange(new byte[4]);data.AddRange(BitConverter.GetBytes(16));
        data.AddRange(BitConverter.GetBytes(sectionSize));data.AddRange(table);data.AddRange(body);return data.ToArray();
    }

    private static bool SameTail(string a,string b,long from)
    {
        using var x=File.OpenRead(a);using var y=File.OpenRead(b);if(x.Length!=y.Length)return false;
        x.Position=y.Position=Math.Min(from,x.Length);byte[] p=new byte[1<<20],q=new byte[1<<20];
        while(true){int n=x.Read(p);int m=y.ReadAtLeast(q,n,throwOnEndOfStream:false);if(n!=m||!p.AsSpan(0,n).SequenceEqual(q.AsSpan(0,n)))return false;if(n==0)return true;}
    }

    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}

    // 32 KiB image: header, FNT with a subdirectory, FAT with three named files and one unnamed overlay, 0xFF padding.
    private static byte[] SyntheticRom()
    {
        byte[] rom=Enumerable.Repeat((byte)0xFF,0x8000).ToArray();rom.AsSpan(0,0x1000).Clear();
        "TESTROM"u8.CopyTo(rom);"TEST"u8.CopyTo(rom.AsSpan(0x0C));
        byte[] fnt=[..BitConverter.GetBytes(16),0,0,2,0,..BitConverter.GetBytes(35),2,0,0,0xF0,
            5,(byte)'a',(byte)'.',(byte)'b',(byte)'i',(byte)'n',5,(byte)'b',(byte)'.',(byte)'b',(byte)'i',(byte)'n',0x83,(byte)'d',(byte)'a',(byte)'t',1,0xF0,0,
            5,(byte)'c',(byte)'.',(byte)'b',(byte)'i',(byte)'n',0];
        fnt.CopyTo(rom,0x1000);
        (uint start,int length,byte fill)[] files=[(0x2000,0x10,0xA0),(0x2200,0x20,0xB0),(0x2400,8,0xC3),(0x2600,0x10,0xD0)];
        for(int i=0;i<files.Length;i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(0x1100+i*8),files[i].start);BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(0x1104+i*8),files[i].start+(uint)files[i].length);
            rom.AsSpan((int)files[i].start,files[i].length).Fill(files[i].fill);
        }
        void U32(int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(at),value);
        U32(0x40,0x1000);U32(0x44,(uint)fnt.Length);U32(0x48,0x1100);U32(0x4C,(uint)(files.Length*8));U32(0x80,0x2800);U32(0x84,0x200);
        // One uncompressed ARM9 overlay (id 0) stored in the unnamed file 3.
        U32(0x50,0x1200);U32(0x54,32);U32(0x1200,0);U32(0x1204,0x02000000);U32(0x1208,0x10);U32(0x1218,3);U32(0x121C,0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(0x15E),NdsRom.Crc16(rom.AsSpan(0,0x15E)));
        return rom;
    }

    private static byte[] SyntheticNarc()
    {
        byte[][] files=[[1,2,3],[4,5,6,7],[]];int image=8+files.Sum(f=>(f.Length+3)&~3);
        var data=new List<byte>();data.AddRange("NARC"u8.ToArray());data.AddRange(new byte[]{0xFE,0xFF,0,1,0,0,0,0,0x10,0,3,0});
        data.AddRange("BTAF"u8.ToArray());data.AddRange(BitConverter.GetBytes(12+files.Length*8));data.AddRange(BitConverter.GetBytes((ushort)files.Length));data.AddRange(new byte[2]);
        int offset=0;foreach(var f in files){data.AddRange(BitConverter.GetBytes(offset));data.AddRange(BitConverter.GetBytes(offset+f.Length));offset+=(f.Length+3)&~3;}
        data.AddRange("BTNF"u8.ToArray());data.AddRange(BitConverter.GetBytes(16));data.AddRange(BitConverter.GetBytes(4));data.AddRange(BitConverter.GetBytes(0x10000));
        data.AddRange("GMIF"u8.ToArray());data.AddRange(BitConverter.GetBytes(image));
        foreach(var f in files){data.AddRange(f);data.AddRange(Enumerable.Repeat((byte)0xFF,((f.Length+3)&~3)-f.Length));}
        byte[] narc=data.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(narc.AsSpan(8),(uint)narc.Length);return narc;
    }
}
