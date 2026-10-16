using System.IO;
using pk3DS.Core;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using ProgressiveLegacy = ThreeDS.Progressive.Services.ProgressiveLegacy;

internal static class StaticStarterChecks
{
    internal static void RunReal(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            string[] Inventory()=>Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
                .Select(p=>{var f=new FileInfo(p);return p+"|"+f.Length+"|"+f.LastWriteTimeUtc.Ticks;}).ToArray();
            string Hash(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            var inventory=Inventory();var session=GameProjectSession.Open(root,recoverInterruptedSave:false);var game=session.GameConfiguration;
            string[] paths=game.Generation==6?[Path.Combine(game.RomFS,"DllField.cro"),Path.Combine(game.RomFS,"DllPoke3Select.cro")]:
                [Path.Combine(game.RomFS,game.GetGARCFileName("encounterstatic")),Path.Combine(game.RomFS,game.GetGARCFileName("levelup"))];
            var hashes=paths.ToDictionary(p=>p,Hash);
            IProjectEditor editor=game.Generation==6?new GameStaticEncounter6Service(session):new GameStaticEncounterService(session);
            var options=EditorValues.Flatten(editor.RandomOptions);options["BasicStarter"]=1;
            if(game.Generation==6){options["SeparateProfiles"]=1;options["Static.Enabled"]=options["Gifts.Enabled"]=0;options["Starters.BasicStarter"]=options["Starters.StarterGeneration"]=1;}
            var preview=editor.Preview(options,game.Generation==6?editor.Entries.Select(e=>e.Id).ToArray():[1,2,3]);
            _=editor.Read(editor.Entries.Last().Id);
            if(paths.Any(p=>Hash(p)!=hashes[p])||!inventory.SequenceEqual(Inventory()))throw new IOException("La referencia real cambió durante la lectura.");
            Console.WriteLine($"PASS REAL STATIC: {root}; {game.Version}; {editor.Entries.Count} records; {preview.Changes.Count} proposed changes; {paths.Length} SHA-256 and full inventory preserved. No Apply.");
        }
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            foreach(var move in f.Game.Moves)move.Power=0;
            f.Game.Moves[33].Power=40;f.Game.Moves[44].Power=60;f.Game.Moves[55].Power=20;
            var sets=Enumerable.Range(0,840).Select(_=>(Learnset)new Learnset6(new byte[4])
                {Moves=[44,1,2,3,4,55],Levels=[1,1,1,1,1,10]}).ToArray();
            typeof(GameConfig).GetProperty("Learnsets")!.SetValue(f.Game,sets);
            check(GameStaticEncounterService.StarterSpecialMove(f.Game,1,0,5,0)==44,version+" starter restores an attack displaced from the last four natural moves");
            check(GameStaticEncounterService.StarterSpecialMove(f.Game,1,0,10,1)==55,version+" starter status gift move replaced by available natural attack at final level");
            check(GameStaticEncounterService.StarterSpecialMove(f.Game,1,0,10,0)==0,version+" natural attack needs no injected starter move");
            check(GameStaticEncounterService.StarterSpecialMove(f.Game,1,0,5,33)==33,version+" damaging starter special move preserved");
            sets[1]=new Learnset6(new byte[4]){Moves=[1,2],Levels=[1,1]};
            check(GameStaticEncounterService.StarterSpecialMove(f.Game,1,0,5,0)==33,version+" starter with only status moves falls back to Tackle");
            f.Game.Moves[33].Power=0;
            check(GameStaticEncounterService.StarterSpecialMove(f.Game,1,0,5,1)==44,version+" starter fallback respects modified ROM move powers");
            f.Game.Moves[44].Power=0;f.Game.Moves[55].Power=0;
            reject(()=>GameStaticEncounterService.StarterSpecialMove(f.Game,1,0,5,0),version+" starter preview rejected when no damaging move exists");
            f.Game.Moves[33].Power=40;
            var pool=GameStaticEncounterService.GiftMovePool(f.Game);
            check(new[]{165,464,621}.Concat(Legal.Z_Moves).All(m=>!pool.Contains(m))&&
                new[]{166,226}.All(pool.Contains)&&pool.Any(m=>m>621),version+" gift special move pool matches Progressive exclusions and includes later moves");

            var records=ModuleChecks.Read(f,"encounterstatic");
            byte[] gift=(byte[])records[0].Clone();_=new EncounterGift7(gift){SpecialMove=1};
            records[0]=Enumerable.Range(0,4).SelectMany(_=>gift).ToArray();
            records[1][0]=1;
            records[1][0x15]=252;records[1][0x1B]=77;records[1][0x30]=0xAB;
            records[4][0x30]=0xCD;
            string path=ModuleChecks.PathFor(f,"encounterstatic");
            f.WriteGarc(path,records,f.Game.GetGARCReference("encounterstatic"));
            StarterTextChecks.CreateBanks(f,0);
            var editor=new GameStaticEncounterService(f.Session);var options=EditorValues.Flatten(editor.RandomOptions);
            options["BasicStarter"]=1;options["RandomItems"]=1;options["RandomAbilities"]=1;
            byte[] before=File.ReadAllBytes(path);
            var preview=editor.Preview(options,[1,2,3,5,editor.Entries.Last().Id]);
            check(File.ReadAllBytes(path).SequenceEqual(before),version+" starter parity preview leaves archive untouched");
            editor.Apply(preview);
            check(Enumerable.Range(1,3).All(id=>EditorValues.Flatten(editor.Read(id))["Move"]==33),version+" all three native starters have a damaging special move after species randomization");
            check(EditorValues.Flatten(editor.Read(4))["Move"]==1,version+" excluded non-starter gift is preserved");
            var staticRow=EditorValues.Flatten(editor.Read(5));
            var tradeRow=EditorValues.Flatten(editor.Read(editor.Entries.Last().Id));
            check(staticRow["Ability"] is >=1 and <=3&&tradeRow["Ability"] is >=0 and <=2,version+" native static and trade ability encodings match original");
            check(staticRow["IV0"]==-4&&Enumerable.Range(1,5).All(i=>staticRow[$"IV{i}"]==-1)&&
                Enumerable.Range(0,6).All(i=>staticRow[$"EV{i}"]==0&&tradeRow[$"IV{i}"]==-1)&&
                staticRow["Nature"]==0&&staticRow["Gender"]==0&&tradeRow["Nature"] is >=0 and <=24,
                version+" species randomization resets stats like Progressive and preserves three-perfect-IV sentinel");
            var after=ModuleChecks.Read(f,"encounterstatic");
            check(after[1][0x30]==0xAB&&after[4][0x30]==0xCD,version+" advanced randomization preserves opaque event and trade bytes");
            staticRow["IV1"]=31;staticRow["EV2"]=255;staticRow["Ally1"]=137;staticRow["Ally2"]=160;staticRow["Map"]=17;
            editor.Save(5,staticRow);var reread=EditorValues.Flatten(editor.Read(5));
            check(new[]{"IV1","EV2","Ally1","Ally2","Map"}.All(k=>reread[k]==staticRow[k]),version+" advanced static stats map and allies save and reload exactly");
            int[] items=ProgressiveLegacy.Randomizer.GetRandomItemList(f.Game);
            check(new[]{1,2,3,5,editor.Entries.Last().Id}.All(id=>items.Contains(EditorValues.Flatten(editor.Read(id))["Item"])),version+" native encounters use full original held item pool");
            check(Enumerable.Range(1,15).All(aura=>Enumerable.Range(0,16).All(seed=>
            {
                int next=GameStaticEncounterService.RandomTotemAura(f.Game.USUM,4,aura,new Random(seed));
                return next!=aura&&next is >=1 and <=15&&(next-1)%3==(aura-1)%3;
            })),version+" Totem randomization changes stat and preserves boost magnitude");
            check(new[]{0,16,17,18,255}.All(aura=>GameStaticEncounterService.RandomTotemAura(f.Game.USUM,4,aura,new Random(1))==aura),version+" all-stat and unknown Totem auras preserved");
            check(GameStaticEncounterService.RandomTotemAura(true,0,3,new Random(1))==3,version+" USUM aura randomizer excludes old Totems and allies outside boss indices");
        }
    }
}
