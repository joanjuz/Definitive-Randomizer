using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Legacy=ThreeDS.Progressive.Services.ProgressiveLegacy;

internal static class RegressionFixes
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using(var f=new Fixture(GameVersion.ORAS))
        {
            string actual=Path.Combine(f.Root,"DllBattle.cro"), requested=Path.Combine(f.Root,"DLLBattle.cro");
            byte[] original=[1,2], changed=[3,4];File.WriteAllBytes(actual,original);
            FileTransaction.Commit([new FileChange(requested,original,changed)]);
            check(Directory.GetFiles(f.Root).Any(p=>Path.GetFileName(p)=="DllBattle.cro") && File.ReadAllBytes(actual).SequenceEqual(changed),"atomic save preserves case-sensitive RomFS filename despite differently cased request");
            reject(()=>FileTransaction.Commit([new FileChange(requested,changed,original)],_=>throw new IOException("rollback probe")),"case-preserving transaction rolls back injected failure");
            check(Directory.GetFiles(f.Root).Any(p=>Path.GetFileName(p)=="DllBattle.cro") && File.ReadAllBytes(actual).SequenceEqual(changed),"rollback retains both original filename spelling and prior bytes");
        }
        using(var f=new Fixture(GameVersion.USUM))
        {
            ModuleChecks.CreateArchives(f);var trainer=new GameTrainerService(f.Session);
            foreach(string source in new[]{"\"DontModify\"","0","\"LevelUpOnly\"","2"})
            {
                trainer.RestoreOptions("{\"Version\":1,\"Game\":\"USUM\",\"MoveSettings\":{\"Source\":"+source+"}}");
                check(true,"legacy trainer movement source accepts named and numeric enum: "+source);
            }
            reject(()=>trainer.RestoreOptions("{\"Version\":1,\"Game\":\"USUM\",\"MoveSettings\":{\"Source\":999}}"),"legacy trainer movement source still rejects undefined enum values");
        }
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            string path=ModuleChecks.PathFor(f,"encdata");var original=ModuleChecks.Read(f,"encdata");
            var packed=original.Select(r=>r.Length==0?r:Compress(r)).ToArray();f.WriteGarc(path,packed,f.Game.GetGARCReference("encdata"));
            byte[] source=File.ReadAllBytes(path);var editor=new GameEncounterService(f.Session);
            check(editor.Entries.Count==(f.Game.Generation==7?4:2)&&File.ReadAllBytes(path).SequenceEqual(source),version+" original lazy GARC reader recognizes compressed encounter tables without writing");
            var values=EditorValues.Flatten(editor.Read(1));string key=f.Game.Generation==7?"S0.0.Species":"S0.Species";values[key]=25;
            editor.Save(1,values);var raw=ModuleChecks.Read(f,"encdata");int changed=f.Game.Generation==7?9:f.Game.XY?1:2;
            check(raw[changed][0]==0x11&&EditorValues.Flatten(new GameEncounterService(f.Session).Read(1))[key]==25,version+" encounter changes remain compressed and reopen correctly");
            check(raw.Where((r,i)=>i!=changed&&(!f.Game.ORAS||i!=1)).Select((r,i)=>true).All(v=>v)&&
                Enumerable.Range(0,raw.Length).Where(i=>i!=changed&&(!f.Game.ORAS||i!=1)).All(i=>raw[i].SequenceEqual(packed[i])),version+" untouched compressed encounter members stay byte-identical");
            var options=EditorValues.Flatten(editor.RandomOptions);options["Species"]=0;options["LevelScale"]=150;
            var proposal=editor.Preview(options,[1]);editor.Apply(proposal);var entry=f.Session.History.Read().Entries[^1];
            check(entry.Kind=="Action"&&entry.Changes.Count==0&&entry.Parameters["SeedPolicy"]=="FreshPerRun"&&!entry.Parameters.ContainsKey("Seed"),version+" mass history stores the action rather than fixed per-Pokemon outcomes");
        }
        using(var f=new Fixture(GameVersion.USUM))
        {
            ModuleChecks.CreateArchives(f);var editor=new GameEvolutionService(f.Session);
            string csv=Path.Combine(f.Root,"custom-evolutions.csv");File.WriteAllText(csv,"Source,Target,Method,Level,Argument,Form,ItemName,AltItemName\n64,65,Level,36,,-1,,\n61,186,19,,221,-1,,\n61,186,20,,221,-1,,\n356,477,19,,325,-1,,\n");
            byte[] before=File.ReadAllBytes(ModuleChecks.PathFor(f,"evolution"));var preview=editor.PreviewTemplate(csv,[61,64,356]);
            check(File.ReadAllBytes(ModuleChecks.PathFor(f,"evolution")).SequenceEqual(before),"evolution CSV preview is read-only");
            editor.Apply(preview);var values=EditorValues.Flatten(editor.Read(64));
            check(values["E0.Species"]==2&&values["E1.Species"]==65&&values["E1.Level"]==36,"evolution template adds an alternative without replacing the original route");
            var held=EditorValues.Flatten(editor.Read(61));
            check(held["E1.Method"]==19&&held["E2.Method"]==20&&held["E1.Argument"]==221,"evolution template preserves separate held-item day and night routes");
            check(editor.PreviewTemplate(csv,[61,64,356]).Changes.Count==0,"evolution template uses original add/update semantics and is idempotent");
            var stale=editor.PreviewTemplate(csv,[61,64]);File.AppendAllText(csv,"75,76,Level,36,,-1,,\n");
            reject(()=>editor.Apply(stale),"evolution template changes invalidate the frozen proposal");
            var history=f.Session.History.Read().Entries[^1];
            check(history.Changes.Count==0&&history.Parameters["ActionId"]=="evolutions.normalize"&&history.Parameters.ContainsKey("TemplateCSV"),"evolution history retains reusable CSV rules, not output evolution tables");
        }
        using(var f=new Fixture(GameVersion.USUM))
        {
            byte[] data=FullBattle();string path=Path.Combine(f.Game.RomFS,"Battle.cro");File.WriteAllBytes(path,data);
            check(Legacy.Gen7BrutalSwingSelfConfusionPatcher.IsInstalled(data),"full battle fixture preserves exact Giro Vil V2 payload and relocated registry");
            string csv=Path.Combine(f.Root,"all-battle-effects.csv");File.WriteAllText(csv,"Move,BattlePatch\n273,Gen7WishPivot\n96,Gen7MeditateV73\n587,Gen7FairyLockAllFairyV11\n381,Gen7LuckyChantCritBoost\n171,Gen7NightmareSleepV76\n693,Gen7BrutalSwingSelfConfusionV1\n");
            var moves=new GameMoveService(f.Session);var settings=new BatchSettings {TemplatePath=csv,MoveDescriptions=false};
            var preview=moves.PreviewMoves(BatchAction.MoveBalance,settings,[273,96,587,381,171,693]);
            check(preview.AdditionalFileCount==1&&File.ReadAllBytes(path).SequenceEqual(data),"Wish, Meditate, Fairy Lock and Lucky Chant resolve beside V2 with non-page-sized relocation headroom");
            moves.ApplyMovePreview(preview);check(Legacy.Gen7BrutalSwingSelfConfusionPatcher.IsInstalled(File.ReadAllBytes(path)),"full combined battle template keeps the validated Giro Vil V2 instructions");
            check(moves.PreviewMoves(BatchAction.MoveBalance,settings,[273,96,587,381,171,693]).Changes.Count==0,"all six battle mechanics reapply without a second stock-layout installation");
            byte[] reused=File.ReadAllBytes(path);reused[0xFCFA8+80]=0x55;File.WriteAllBytes(path,reused);
            using(var context=Legacy.Main.Enter(f.Root))check(Legacy.Gen7FairyLockV11Patcher.Apply()==0,"Fairy Lock validates instructions while allowing allocator reuse of unused zero padding");

        }
    }
    private static byte[] Compress(byte[] data)
    {string input=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".bin"),output=input+".lz";try{File.WriteAllBytes(input,data);LZSS.Compress(input,output);return File.ReadAllBytes(output);}finally{File.Delete(input);if(File.Exists(output))File.Delete(output);}}
    private static byte[] FullBattle()
    {
        byte[] data=ModuleChecks.ExpandedBattle();byte[] old=data.AsSpan(0x130000+755*12,48).ToArray();
        const int relocations=0x128000;U(0x128,relocations);U(0x12C,6200);
        for(int i=0;i<6200;i++){U(relocations+i*12,3);U(relocations+i*12+4,0);U(relocations+i*12+8,0);}
        old.CopyTo(data,relocations+755*12);
        void R(int id,uint tag,int segment,uint addend){U(relocations+id*12,tag);U(relocations+id*12+4,(uint)(2|(segment<<8)));U(relocations+id*12+8,addend);}
        U(0xC171C,0xE3A01002);U(0xC1720,0xE5801000);U(0xC1724,0xE59F0000);U(0xC1728,0xE12FFF1E);
        U(0x106CD0,0xBF);U(0x106CD8,0xA5);
        R(882,0xC15AC0,1,0x8CD0);R(5913,0x8CD41,0,0xC6E08);R(5751,0x8CDC1,0,0xC8F60);
        var fairy=typeof(Legacy.Gen7FairyLockV11Patcher);U(0x106D60,0xBF);Copy(fairy,"FinalPayload",0xFCFA8);
        R(5843,0x8D641,0,(uint)(Constant(fairy,"FinalHandler")-0x180));
        var lucky=typeof(Legacy.Gen7LuckyChantCritPatcher);U(0x105260,5);U(0x1051F0,0x44);
        Copy(lucky,"SideSlotFactorySignature",0xBF908);Copy(lucky,"StockHandlerSignature",Constant(lucky,"StockHandler"));Copy(lucky,"CritFlowSignature",0x7EE90);Copy(lucky,"A1FactoryStockSignature",0xD9EE4);
        Copy(lucky,"DurationFinal",Constant(lucky,"Duration"));Copy(lucky,"FinalPayload",Constant(lucky,"Cave"));
        R(5396,0x72641,0,0xBF788);R(5413,0x71F41,0,(uint)Constant(lucky,"FinalHandlerAddend"));
        return data;
        void U(int at,uint value)=>BitConverter.GetBytes(value).CopyTo(data,at);
        void Copy(Type type,string name,int at)=>((byte[])type.GetField(name,BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!).CopyTo(data,at);
    }
    private static int Constant(Type type,string name)=>Convert.ToInt32(type.GetField(name,BindingFlags.NonPublic|BindingFlags.Static)!.GetRawConstantValue());
}
