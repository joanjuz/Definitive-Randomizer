using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Legacy=ThreeDS.Progressive.Services.ProgressiveLegacy;

internal sealed class TestState : IDisposable
{
    private readonly string? _previous=Environment.GetEnvironmentVariable("PROGRESSIVE_STATE_DIR");
    private readonly string _path=Path.Combine(Path.GetTempPath(),"progressive-test-state-"+Guid.NewGuid().ToString("N"));
    internal TestState(){Environment.SetEnvironmentVariable("PROGRESSIVE_STATE_DIR",_path);}
    public void Dispose(){Environment.SetEnvironmentVariable("PROGRESSIVE_STATE_DIR",_previous);if(Directory.Exists(_path))Directory.Delete(_path,true);}
}

internal static class ModuleChecks
{
    internal static string PathFor(Fixture f,string name)=>Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName(name));
    internal static byte[][] Read(Fixture f,string name)=>new GARC.MemGARC(File.ReadAllBytes(PathFor(f,name))).Files;
    internal static void CreateArchives(Fixture f)
    {
        int gen=f.Game.Generation,o=f.Game.ORAS?4:0;
        byte[][] headers=new byte[4][],teams=new byte[4][];
        for(int id=0;id<headers.Length;id++)
        {
            byte[] h=new byte[20+o];int count=id==0?0:2;h[3+o]=(byte)count;h[12+o]=0xA5;h[17+o]=10;
            if(gen==7){Set16(h,0,20);h[15]=0x9A;}
            else {if(f.Game.ORAS){Set16(h,0,3);Set16(h,2,20);Set16(h,4,0x1234);}else{h[0]=3;h[1]=20;}h[13+o]=0xC3;h[16+o]=0xFF;}
            byte[] team=new byte[count*(gen==7?32:18)];
            for(int i=0;i<count;i++)
            {
                if(gen==7)
                {
                    byte[] raw=new byte[32];raw[15]=0x9B;raw[11]=0x80;
                    var p=new TrainerPoke7(raw){Species=i+1,Level=10+i,Item=2,Nature=4,Ability=2,Gender=1};p.IVs=[1,2,3,4,5,6];p.EVs=[10,20,30,40,50,60];p.Moves=[1,2,3,4];p.Write().CopyTo(team,i*32);
                }
                else
                {
                    int at=i*18;team[at]=128;team[at+1]=0x2D;Set16(team,at+2,10+i);Set16(team,at+4,i+1);Set16(team,at+8,2);
                    for(int n=0;n<4;n++)Set16(team,at+10+n*2,n+1);
                }
            }
            headers[id]=h;teams[id]=team;
        }
        Write(f,"trdata",headers);Write(f,"trpoke",teams);
        var items=Enumerable.Range(0,60).Select(i=>
        {
            var obj=new Item {BuyPrice=i==0?0:i*10};
            return obj.Write();
        }).ToArray();
        Write(f,"item",items);
        Write(f,"gametext",[
            TextFile.GetBytes(f.Game,["Hola","Primer texto"]),
            TextFile.GetBytes(f.Game,["Segunda sección","Con variables de ejemplo"])
        ]);
        var learn=new Learnset6(new byte[4]) {Moves=[1,2,3,4,5,6],Levels=[1,1,1,1,5,10]};
        Write(f,"levelup",Enumerable.Range(0,840).Select(_=>learn.Write()).ToArray());
        if(gen==7)
        {
            byte[] statics=new byte[0x38*200];
            for(int j=0;j<200;j++){statics[j*0x38+4]=20;statics[j*0x38+3]=10;}
            byte[] gifts=new byte[0x14];gifts[0]=1;gifts[3]=5;gifts[4]=0xA2;
            byte[] trades=new byte[0x34];trades[0]=1;trades[5]=10;trades[0x2C]=25;trades[0x30]=0xAA;
            Write(f,"encounterstatic",[gifts,statics,[],[],trades]);
        }
        var evolutions=Enumerable.Range(0,840).Select(i=>
        {
            byte[] data=new byte[gen==7?64:48];Set16(data,0,4);Set16(data,2,16);Set16(data,4,2);
            if(gen==7){for(int at=0;at<64;at+=8)data[at+6]=255;data[7]=16;}
            return data;
        }).ToArray();Write(f,"evolution",evolutions);
        byte[][] encounters;
        if(gen==7)
        {
            encounters=Enumerable.Range(0,22).Select(_=>Array.Empty<byte>()).ToArray();
            // Field Items EI (visible) and EB (hidden) stored in the ED area node.
            byte[] ei=new byte[120];ei[0]=1;Set16(ei,52,17);
            byte[] eb=new byte[120];eb[0]=1;Set16(eb,58,18);
            byte[][] ed=Enumerable.Range(0,12).Select(_=>Array.Empty<byte>()).ToArray();
            ed[10]=Mini.PackMini([ei],"EI");ed[11]=Mini.PackMini([eb],"EB");
            encounters[0]=Mini.PackMini(ed,"ED");
            foreach(int file in new[]{9,20})
            {
                byte[] pair=new byte[0x2CC];pair[0]=0x7A;
                for(int day=0;day<2;day++)
                {
                    int offset=4+day*0x164;pair[offset]=(byte)(10+day);pair[offset+1]=(byte)(15+day);
                    for(int i=0;i<10;i++)pair[offset+2+i]=10;
                    for(int i=0;i<86;i++)Set32(pair,offset+12+i*4,0xABCD0000u|(uint)(i%6+1));
                }
                encounters[file]=Mini.PackMini([pair],"EA");
            }
        }
        else
        {
            int size=f.Game.XY?0x178:0xF6,start=f.Game.XY?1:2;encounters=new byte[start+2][];
            encounters[0]=new byte[112];
            if(f.Game.ORAS)
            {encounters[1]=new byte[32+0xF4*2+8];Set32(encounters[1],4,18);Set32(encounters[1],8,18+0xF4);}
            for(int map=0;map<2;map++)
            {
                byte[] data=new byte[32+size+7];data[0]=0x7C;data[^1]=0x9A;Set32(data,16,(uint)(32-(f.Game.XY?16:14)));
                for(int slot=0;slot<size/4;slot++){Set16(data,32+slot*4,slot%6+1);data[34+slot*4]=10;data[35+slot*4]=15;}
                encounters[start+map]=data;if(f.Game.ORAS)data.AsSpan(32,0xF4).CopyTo(encounters[1].AsSpan(32+map*0xF4));
            }
        }
        Write(f,"encdata",encounters);
    }
    private static void Write(Fixture f,string name,byte[][] records)=>f.WriteGarc(PathFor(f,name),records,f.Game.GetGARCReference(name));
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);CreateArchives(f);
            var trainers=new GameTrainerService(f.Session);var originalHeader=Read(f,"trdata");var originalTeam=Read(f,"trpoke");
            check(trainers.Entries.Count==3,version+" trainers have named browsable records");
            var values=EditorValues.Flatten(trainers.Read(1));
            check(values["Count"]==2&&values["P0.Species"]==1&&values["P1.Level"]==11,version+" trainer structure reads complete teams");
            if(f.Game.Generation==7)check(values["P0.IV5"]==6&&values["P0.EV3"]==40,version+" trainer individual IV/EV order is preserved");
            trainers.Save(1,values);check(Read(f,"trdata")[1].SequenceEqual(originalHeader[1])&&Read(f,"trpoke")[1].SequenceEqual(originalTeam[1]),version+" trainer no-op preserves opaque bits and header bytes");
            check(f.Session.History.Read().Entries.Count==0,version+" no-op does not create a history action");
            values["Class"]=23;values["P0.Level"]=45;values["P0.Item"]=71;values["P0.Move0"]=118;
            trainers.Save(1,values);var reloaded=EditorValues.Flatten(new GameTrainerService(f.Session).Read(1));
            check(reloaded["Class"]==23&&reloaded["P0.Level"]==45&&reloaded["P0.Item"]==71&&reloaded["P0.Move0"]==118,version+" trainer save reloads edited header and team fields");
            check(Read(f,"trdata")[2].SequenceEqual(originalHeader[2])&&Read(f,"trpoke")[2].SequenceEqual(originalTeam[2]),version+" trainer save preserves other records");
            if(f.Game.ORAS)check(Read(f,"trdata")[1][4]==0x34&&Read(f,"trdata")[1][5]==0x12,version+" trainer preserves ORAS uORAS instead of zeroing it");
            if(f.Game.Generation==7)check(Read(f,"trpoke")[1][15]==0x9B&&(Read(f,"trpoke")[1][11]&0x80)!=0,version+" trainer preserves reserved team and packed IV bits");
            else check((Read(f,"trpoke")[1][1]&12)==12,version+" trainer preserves PID bits outside gender and ability");
            values["Count"]=3;values["P2.Species"]=4;values["P2.Level"]=30;trainers.Save(1,values);
            check(EditorValues.Flatten(trainers.Read(1))["P2.Species"]==4&&Read(f,"trpoke")[1].Length==(f.Game.Generation==7?96:54),version+" trainer team resize serializes its actual format");
            var invalid=new Dictionary<string,int>(values){["P0.Level"]=101};byte[] saved=File.ReadAllBytes(PathFor(f,"trpoke"));
            reject(()=>trainers.Save(1,invalid),version+" trainer rejects out-of-range levels");check(File.ReadAllBytes(PathFor(f,"trpoke")).SequenceEqual(saved),version+" invalid trainer save is read-only");
            var options=EditorValues.Flatten(trainers.RandomOptions);options["LevelScale"]=120;
            if(OperatingSystem.IsWindows()&&f.Game.Generation==6)options["CHK_StoryMEvos"]=0; // uniform synthetic mega types; story Mega guarantees are tested separately.
            var trainerOptions=new Dictionary<string,int>(options);
            var preview=trainers.Preview(options,[1]);var second=trainers.Preview(options,[1]);
            check(preview.Changes.Count>0&&preview.Seed!=second.Seed,version+" trainer randomization uses a fresh seed and real changes");
            check(File.ReadAllBytes(PathFor(f,"trpoke")).SequenceEqual(saved),version+" trainer preview is read-only");
            reject(()=>new GameTrainerService(f.Session).Apply(preview),version+" trainer cross-session proposal is rejected");
            trainers.Apply(preview);check(EditorValues.Flatten(trainers.Read(1))["P0.Level"]==54,version+" trainer level scaling applies to the frozen scope");
            var history=f.Session.History.Read();check(history.Entries.Count==3&&history.Entries[^1].Kind=="Action"&&history.Entries[^1].Changes.Count==0&&!history.Entries[^1].Parameters.ContainsKey("Seed")&&history.Entries[^1].Parameters["SeedPolicy"]=="FreshPerRun",version+" trainer history records all successful actions and parameters");
            check(history.Entries[0].Changes.Any(c=>c.Key=="P0.Level"&&c.Field=="Pokémon 1 · Nivel"&&c.Before=="10"&&c.After=="45"),version+" history identifies the team slot and includes before/after values");
            check(new ProjectHistory(f.Root,f.Game.Version.ToString()).Read().Entries.Count==history.Entries.Count,version+" project history survives reopening");
            var encounter=new GameEncounterService(f.Session);var encBefore=Read(f,"encdata");
            check(encounter.Entries.Count==(f.Game.Generation==7?4:2),version+" encounters expose maps and day/night separately");
            var ev=EditorValues.Flatten(encounter.Read(1));string prefix=f.Game.Generation==7?"S0.0.":"S0.";
            ev[prefix+"Species"]=25;ev[prefix+"Form"]=1;
            if(f.Game.Generation==7){ev["Min"]=20;ev["Max"]=25;ev["S8.0.Species"]=26;}
            else{ev[prefix+"Min"]=20;ev[prefix+"Max"]=25;}
            encounter.Save(1,ev);var er=EditorValues.Flatten(new GameEncounterService(f.Session).Read(1));
            check(er[prefix+"Species"]==25&&er[prefix+"Form"]==1,version+" encounter species/forms save and reload");
            if(f.Game.Generation==7)
            {
                var before=Mini.UnpackMini(encBefore[9],"EA")[0];var after=Mini.UnpackMini(Read(f,"encdata")[9],"EA")[0];
                check(er["S8.0.Species"]==26&&er["Min"]==20,version+" SOS weather and table levels are editable");
                check(after.AsSpan(0x168).SequenceEqual(before.AsSpan(0x168))&&after[0]==before[0],version+" night table and EA prefix remain unchanged");
                check((BitConverter.ToUInt32(after,16)&0xFFFF0000u)==0xABCD0000u,version+" encounter packed high bits remain intact");
                check(Read(f,"encdata")[20].SequenceEqual(encBefore[20]),version+" unrelated encounter map remains byte-identical");
            }
            else
            {
                int index=f.Game.XY?1:2;var after=Read(f,"encdata")[index];check(after[0]==0x7C&&after[^1]==0x9A,version+" encounter preserves map header/trailer");
                check(Read(f,"encdata")[index+1].SequenceEqual(encBefore[index+1]),version+" unrelated encounter map remains byte-identical");
                if(f.Game.ORAS)check(Read(f,"encdata")[1].AsSpan(32,0xF4).SequenceEqual(after.AsSpan(32,0xF4)),version+" ORAS map and shared EN mirror are synchronized");
            }
            options=EditorValues.Flatten(encounter.RandomOptions);options["LevelScale"]=150;
            var ep=encounter.Preview(options,[1]);encounter.Apply(ep);
            check(EditorValues.Flatten(encounter.Read(1))[f.Game.Generation==7?"Min":"S0.Min"]==30,version+" encounter scaled levels use the selected scope");
            if(f.Game.Generation==7)
            {
                var statics=new GameStaticEncounterService(f.Session);
                check(statics.Entries.Count==202,version+" static encounters expose real Gifts, Totems and Trades");
                var giftBefore=Read(f,"encounterstatic")[0];
                var gift=EditorValues.Flatten(statics.Read(1));
                check(gift["Species"]==1&&gift["Level"]==5&&gift["ShinyLock"]==1,
                    version+" static gift fields read exact binary structure");
                gift["Species"]=25;statics.Save(1,gift);
                byte[] giftAfter=Read(f,"encounterstatic")[0];
                check(BitConverter.ToUInt16(giftAfter,0)==25&&giftAfter[4]==giftBefore[4],
                    version+" static gift edit preserves unknown gender/lock bits");
                var trade=EditorValues.Flatten(statics.Read(statics.Entries.Count));
                check(trade["Request"]==25,version+" static trades expose requested Pokémon separately");
                trade["Request"]=133;statics.Save(statics.Entries.Count,trade);
                check(BitConverter.ToUInt16(Read(f,"encounterstatic")[4],0x2C)==133
                    &&Read(f,"encounterstatic")[4][0x30]==0xAA,
                    version+" static trades preserve unrelated event flags when saving");
                if(f.Game.USUM)
                {
                    var totems=statics.TotemRules();
                    check(totems.Count==9&&totems.Any(r=>r.Id==160)&&totems.Any(r=>r.Id==4),
                        version+" USUM Totem tables retain all original 0-based Static Encounter IDs");
                    foreach(var r in totems)r.Use=false;
                    var first=totems.First(r=>r.Id==4);
                    first.Use=true;first.LevelCap=33;
                    byte[] untouched=Read(f,"encounterstatic")[1].ToArray();
                    var proposal=statics.PreviewTotemRules(totems,false);
                    check(proposal.Changes.Count>0&&Read(f,"encounterstatic")[1].SequenceEqual(untouched),
                        version+" Totem Level Caps preview never writes to the GARC");
                    statics.Apply(proposal);
                    check(EditorValues.Flatten(statics.Read(first.RecordId))["Level"]==33
                        &&EditorValues.Flatten(statics.Read(totems.First(r=>r.Id==9).RecordId))["Level"]==10,
                        version+" Totem Level Caps apply only the selected ID");
                }
                var staticOptions=EditorValues.Flatten(statics.RandomOptions);
                check(staticOptions.ContainsKey("G7")&&staticOptions.ContainsKey("RandomItems")
                    &&staticOptions.ContainsKey("RemoveLocks")&&staticOptions.ContainsKey("TotemAura"),
                    version+" static randomizer exposes vanilla-like pool and safe item/aura/lock toggles");
                staticOptions["Species"]=0;staticOptions["ScaleLevels"]=1;staticOptions["LevelScale"]=120;
                var staticPreview=statics.Preview(staticOptions,[1]);
                check(staticPreview.Changes.Count>0
                    &&EditorValues.Flatten(statics.Read(1))["Level"]==5,
                    version+" static randomizer preview is frozen and leaves ROM unchanged");
            }
            var levelup=new GameLevelUpService(f.Session);
            check(levelup.Read(1).Count==6,version+" original level-up learnsets are loaded");
            var rows=levelup.Read(1).ToArray();rows[0].Move=12;rows[0].Level=2;
            levelup.Save(1,rows);
            check(levelup.Read(1).Any(r=>r.Move==12&&r.Level==2)&&levelup.Read(1)[0].Level==1,
                version+" level-up moves are preserved and sorted when saving");
            check(new Learnset6(Read(f,"levelup")[2]).Moves[0]==1,
                version+" level-up editing does not touch other Pokémon");
            var bulkOriginal=File.ReadAllBytes(PathFor(f,"levelup"));
            var bulkPreview=levelup.PrepareRandomization(
                new LevelUpRandomSettings(f.Game.Generation==6,true,15,false,25,false,false,79,true),
                metronome:true);
            check(bulkPreview.Changes.Count>10&&
                File.ReadAllBytes(PathFor(f,"levelup")).SequenceEqual(bulkOriginal),
                version+" all-Pokémon Metronome preview is read-only and produces a frozen patch");
            levelup.ApplyRandomization(bulkPreview);
            check(levelup.Read(1).Count==1&&levelup.Read(1)[0].Move==118
                &&levelup.Read(2)[0].Level==1,
                version+" all-Pokémon Metronome applies safely through GARC history");

            var item=new GameItemService(f.Session);
            var iv=EditorValues.Flatten(item.Read(1));
            check(iv["Price"]==10,version+" item editor reads base 10-unit price");
            iv["Price"]=200;item.Save(1,iv);
            check(EditorValues.Flatten(item.Read(1))["Price"]==200,
                version+" item price changes are saved in item GARC");
            check(EditorValues.Flatten(item.Read(2))["Price"]==20,
                version+" other items stay intact");
            if(f.Game.Generation==7)
            {
                var prices=item.PriceRows().ToArray();
                check(prices[0].TargetBuy==3000&&prices[1].TargetBuy==800,
                    version+" economy preset is based on original UPR-ZX targets");
                foreach(var row in prices)row.Included=false;
                prices[0].Included=true;prices[0].TargetBuy=3400;
                var economy=item.PreviewEconomy(prices);
                check(economy.Changes.Count==1&&EditorValues.Flatten(item.Read(1))["Price"]==200,
                    version+" economy grid provides frozen one-item price proposal");
                item.Apply(economy);
                check(EditorValues.Flatten(item.Read(1))["Price"]==3400&&
                    EditorValues.Flatten(item.Read(2))["Price"]==20,
                    version+" economy apply writes only included items");
            }
            var texts=new GameTextService(f.Session);
            check(texts.Read(0)[0]=="Hola",version+" game text original codec parses file");
            texts.Save(0,["Hola editada","Primer texto"]);
            check(texts.Read(0)[0]=="Hola editada"&&texts.Read(1)[0]=="Segunda sección",
                version+" text editing persists and preserves unrelated files");
            if(f.Game.Generation==7)
            {
                var fieldItems=new GameFieldItemsService(f.Session);
                check(fieldItems.Entries.Count==2,version+" EI and EB Field Item locations are detected");
                var one=EditorValues.Flatten(fieldItems.Read(1));
                check(one["Item"]==17,version+" EI item reads real ED/EI area layout");
                one["Item"]=19;fieldItems.Save(1,one);
                check(EditorValues.Flatten(fieldItems.Read(1))["Item"]==19
                    &&EditorValues.Flatten(fieldItems.Read(2))["Item"]==18,
                    version+" Field Item edit preserves adjacent EB");
                var fieldOptions=EditorValues.Flatten(fieldItems.RandomOptions);
                var fieldPreview=fieldItems.Preview(fieldOptions,[1,2]);
                check(fieldPreview.Seed>=0&&fieldPreview.Files.Count==1,
                    version+" Field Items produce a frozen scoped preview");
            }
            var evolution=new GameEvolutionService(f.Session);var e=EditorValues.Flatten(evolution.Read(1));e["E0.Method"]=5;evolution.Save(1,e);
            options=EditorValues.Flatten(evolution.RandomOptions);options["Species"]=0;options["Trade"]=1;options["TradeLevel"]=40;
            var evolve=evolution.Preview(options,[1]);evolution.Apply(evolve);e=EditorValues.Flatten(evolution.Read(1));
            check(e["E0.Method"]==4&&e[f.Game.Generation==7?"E0.Level":"E0.Argument"]==40,version+" evolution trade conversion uses each generation's level field");
            check(EditorValues.Flatten(evolution.Read(2))["E0.Argument"]==16,version+" evolution changes respect scope");
            string export=Path.Combine(f.Root,"history.json");f.Session.History.Export(export);
            check(JsonSerializer.Deserialize<HistoryDocument>(File.ReadAllBytes(export))?.Entries.Count==f.Session.History.Read().Entries.Count,version+" history JSON exports the entire ordered journal");
            var stale=trainers.Preview(trainerOptions,[1]);byte[] source=File.ReadAllBytes(PathFor(f,"trdata"));source[^1]^=1;File.WriteAllBytes(PathFor(f,"trdata"),source);
            int countBefore=f.Session.History.Read().Entries.Count;
            reject(()=>trainers.Apply(stale),version+" trainer external edits reject a stale proposal");
            check(f.Session.History.Read().Entries.Count==countBefore,version+" failed writes never enter history");
        }
        MachineTutorChecks(check,reject);
        PortableHistoryChecks(check);BackupChecks(check,reject);BattleChecks(check,reject);RegressionFixes.Run(check,reject);
    }
    private static void MachineTutorChecks(Action<bool,string> check,Action<Action,string> reject)
    {
        using var f=new Fixture(GameVersion.USUM);
        string path=Path.Combine(f.Game.ExeFS,"code.bin");
        byte[] code=new byte[0x402000];
        byte[] signature=[0x03,0x40,0x03,0x41,0x03,0x42,0x03,0x43,0x03];
        signature.CopyTo(code,0x400000);
        int offset=0x400000+signature.Length+0x22;
        for(int i=0;i<100;i++)Set16(code,offset+i*2,i+1);
        code[0x401800]=0xAC;
        File.WriteAllBytes(path,code);
        var machine=new GameMachineTutorService(f.Session);
        check(machine.CanEditTms&&machine.Machines.Count==100&&machine.Machines[0].MoveId==1,
            "USUM MT editor locates actual code.bin signature and 100 moves");
        machine.Machines[0].MoveId=15;
        machine.SaveMachines();
        byte[] saved=File.ReadAllBytes(path);
        check(BitConverter.ToUInt16(saved,offset)==15&&saved[0x401800]==0xAC
            &&saved.Length==code.Length&&saved.AsSpan(offset+2).SequenceEqual(code.AsSpan(offset+2)),
            "USUM MT edit preserves every code.bin byte outside the edited assignment");
        check(f.Session.TmMoveIds.Count==100&&f.Session.TmMoveIds[0]==15,
            "TM save refreshes Pokémon inspector compatibility labels");
        check(f.Session.History.Read().Entries.Last().Module=="MT y tutores",
            "MT changes are recorded by portable project history");
        var stale=new GameMachineTutorService(f.Session);
        stale.Machines[0].MoveId=34;
        byte[] external=File.ReadAllBytes(path);external[0x401800]^=1;File.WriteAllBytes(path,external);
        int count=f.Session.History.Read().Entries.Count;
        reject(()=>stale.SaveMachines(),"MT stale code.bin cannot be overwritten by another editor");
        check(f.Session.History.Read().Entries.Count==count,"Failed MT edit does not pollute history");
    }

    private static void PortableHistoryChecks(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.USUM);
        string? previous=Environment.GetEnvironmentVariable("PROGRESSIVE_STATE_DIR");
        Environment.SetEnvironmentVariable("PROGRESSIVE_STATE_DIR",null);
        try
        {
            var history=new ProjectHistory(f.Root,"USUM");
            string path=Path.Combine(f.Root,"3ds-progressive-history.json");
            check(history.Read().Entries.Count==0&&!File.Exists(path),"opening the portable history creates no file");
            string source=Path.Combine(f.Root,"test-metadata.bin");File.WriteAllBytes(source,[1]);
            history.Commit([new FileChange(source,[1],[2])],"Prueba","Editar",[new(1,"Uno","Valor","1","2")]);
            check(File.Exists(path)&&new ProjectHistory(f.Root,"USUM").Read().Entries.Count==1,"history travels with the project and survives reopening");
        }
        finally {Environment.SetEnvironmentVariable("PROGRESSIVE_STATE_DIR",previous);}
    }
    private static void BackupChecks(Action<bool,string> check,Action<Action,string> reject)
    {
        using var f=new Fixture(GameVersion.USUM);CreateArchives(f);
        string destination=Path.Combine(Path.GetTempPath(),"progressive-backup-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(destination);
        try
        {
            string zip=ProjectBackup.CreateAsync(f.Root,destination,"USUM",history:f.Session.History.Read()).GetAwaiter().GetResult();
            using(var archive=ZipFile.OpenRead(zip))
            {
                var metadata=archive.GetEntry("backup-manifest.json")!;using var stream=metadata.Open();var manifest=JsonSerializer.Deserialize<BackupManifest>(stream)!;
                check(manifest.Files.Count==Directory.EnumerateFiles(f.Root,"*",SearchOption.AllDirectories).Count(),"backup includes every project file");
                check(archive.GetEntry("progressive-history.json")!=null,"backup includes the persistent history snapshot");
                foreach(var file in manifest.Files)
                {using var saved=archive.GetEntry("project/"+file.Path)!.Open();check(Convert.ToHexString(SHA256.HashData(saved))==file.Sha256,"backup verifies "+file.Path);}
            }
            reject(()=>ProjectBackup.CreateAsync(f.Root,Path.Combine(f.Root,"backups"),"USUM").GetAwaiter().GetResult(),"backup rejects destination inside project");
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();bool wasCancelled=false;
            try{ProjectBackup.CreateAsync(f.Root,destination,"USUM",cancellation:cancelled.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){wasCancelled=true;}
            check(wasCancelled,"backup cancellation is reported");
            check(!Directory.EnumerateFiles(destination,"*.partial").Any(),"backup leaves no partial files");
        }
        finally{Directory.Delete(destination,true);}
    }
    private static void BattleChecks(Action<bool,string> check,Action<Action,string> reject)
    {
        using var f=new Fixture(GameVersion.USUM);byte[] expanded=ExpandedBattle();
        check(Legacy.Gen7BrutalSwingSelfConfusionPatcher.IsInstalled(expanded),"synthetic expanded CRO contains the exact validated Giro Vil V2 payload");
        check(Legacy.LegacyCroLayout.Compatible(expanded),"verified Giro Vil V2 expansion is an accepted legacy layout");
        check(Legacy.LegacyCroLayout.StockOffset(expanded,0x105EF0)==0xF9100,"stock registries follow the inserted code page");
        string path=Path.Combine(f.Game.RomFS,"Battle.cro");File.WriteAllBytes(path,expanded);
        string csv=Path.Combine(f.Root,"combined.csv");File.WriteAllText(csv,"Move,BattlePatch\n171,Gen7NightmareSleepV76\n96,Gen7MeditateV73\n693,Gen7BrutalSwingSelfConfusionV1\n");
        var service=new GameMoveService(f.Session);var settings=new BatchSettings {TemplatePath=csv,MoveDescriptions=false};
        var proposal=service.PreviewMoves(BatchAction.MoveBalance,settings,[171,96,693]);
        check(File.ReadAllBytes(path).SequenceEqual(expanded),"combined expanded-CRO template preview leaves the game untouched");
        check(proposal.AdditionalFileCount==1,"Nightmare works beside Giro Vil V2 with relocated Meditate registries");
        service.ApplyMovePreview(proposal);byte[] installed=File.ReadAllBytes(path);
        check(Legacy.Gen7BrutalSwingSelfConfusionPatcher.IsInstalled(installed),"combined template preserves the validated V2 instructions");
        var again=service.PreviewMoves(BatchAction.MoveBalance,settings,[171,96,693]);
        check(again.Changes.Count==0,"combined expanded-CRO template is byte-idempotent on reapplication");
        byte[] foreign=(byte[])expanded.Clone();foreign[0x1FDE4]^=1;
        check(!Legacy.LegacyCroLayout.Compatible(foreign),"expanded foreign CROs are rejected rather than accepted by size alone");
    }
    internal static byte[] ExpandedBattle()
    {
        byte[] data=new byte[0x13D060];const uint code=0x180,rodata=0xFE000,sectionData=0x12F000,relocations=0x130000;
        Set32(data,0x80,0x304F5243);Set32(data,0x90,(uint)data.Length);
        Set32(data,0xB0,code);Set32(data,0xB4,rodata-code);Set32(data,0xB8,sectionData);Set32(data,0xBC,0x8000);
        Set32(data,0xC8,0x140);Set32(data,0xCC,4);Set32(data,0x128,relocations);Set32(data,0x12C,759);
        Set32(data,0x140,code);Set32(data,0x144,rodata-code);Set32(data,0x14C,rodata);Set32(data,0x150,sectionData-rodata);Set32(data,0x158,sectionData);Set32(data,0x15C,0x8000);
        for(int i=0;i<759;i++)Set32(data,(int)relocations+i*12,3);
        const uint table=0xF9000,handler=0xF8000,timing=0xF8100,function=0xF8200;
        Set32(data,0x873EC,344);Set32(data,(int)table,693);
        Reloc(755,sectionData,table);Reloc(756,table+4,handler);Reloc(757,handler+16,timing);Reloc(758,timing+4,function);
        Set32(data,(int)handler,0xE3A01001);Set32(data,(int)handler+4,0xE5801000);Set32(data,(int)handler+8,0xE59F0000);Set32(data,(int)handler+12,0xE12FFF1E);data[timing]=4;
        var type=typeof(Legacy.Gen7BrutalSwingSelfConfusionPatcher);
        Copy(type,"EventGuardSignature",0x60740);Copy(type,"StatusBuilderSignature",0x60C6C);Copy(type,"GenericConfusionSignature",0x60F28);Copy(type,"SpreadMultiHitSelectorPatched",0x1FDE4);
        ((byte[])type.GetMethod("BuildSelfConfusionCode",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[function])!).CopyTo(data,(int)function);
        var nightmare=typeof(Legacy.Gen7NightmareSleepV76Patcher);
        var callback=(uint[])nightmare.GetField("StockCallback",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        for(int i=0;i<callback.Length;i++)Set32(data,0xC9628+i*4,callback[i]);
        foreach(var pair in new Dictionary<int,uint>{{0xB24F4,0xE92D4FF0},{0xB24F8,0xE24DD04C},{0xB26F8,0xE1A07005},{0xB2714,0xE1A00004},{0xB2728,0xE1A00006},{0xB2790,0xE28DD04C},{0xB2794,0xE8BD8FF0},{0x841C8,0xE5900000},{0x841CC,0xE1A00C00},{0x841D0,0xE1A00DA0},{0x841D4,0xE12FFF1E},{0x92298,0xE5900008},{0x9229C,0xE1A00000},{0x922A0,0xE92D4070}})Set32(data,pair.Key,pair.Value);
        foreach(var pair in new Dictionary<int,int>{{0xB2704,0xB08BC},{0xB2718,0x8205C},{0xB272C,0x841C8},{0xB274C,0x876F8},{0xB2774,0x89CC4},{0xB278C,0x7CCF4}})Set32(data,pair.Key,0xEB000000u|((uint)((pair.Value-pair.Key-8)/4)&0xFFFFFF));
        var meditate=typeof(Legacy.Gen7MeditateV73Patcher);Copy(meditate,"CombinedPayload",0xFCB00);Copy(meditate,"Recipient1",0xC34E0);Copy(meditate,"Recipient2",0xC34E8);
        Copy(meditate,"ProtectId",(int)table+0x100);Copy(meditate,"DetectId",(int)table+0x108);Copy(meditate,"PurifyId",(int)table+0x9C0);
        Set32(data,0x8731C,0xEA01D5F7);Set32(data,0x87388,0xEA01D5EC);
        return data;
        void Copy(Type t,string name,int at)=>((byte[])t.GetField(name,BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!).CopyTo(data,at);
        void Reloc(int i,uint write,uint target)
        {int segment=write>=sectionData?2:write>=rodata?1:0;uint start=segment==2?sectionData:segment==1?rodata:code;int targetSegment=target>=sectionData?2:target>=rodata?1:0;uint targetStart=targetSegment==2?sectionData:targetSegment==1?rodata:code;int at=(int)relocations+i*12;Set32(data,at,((write-start)<<4)|(uint)segment);Set32(data,at+4,(uint)targetSegment<<8);Set32(data,at+8,target-targetStart);}
    }
    private static void Set16(byte[] bytes,int at,int value)=>BitConverter.GetBytes((ushort)value).CopyTo(bytes,at);
    private static void Set32(byte[] bytes,int at,uint value)=>BitConverter.GetBytes(value).CopyTo(bytes,at);
}
