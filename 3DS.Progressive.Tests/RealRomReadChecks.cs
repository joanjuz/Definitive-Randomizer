using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class RealRomReadChecks
{
    internal static void Run(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            Console.WriteLine("READ-ONLY ROOT: "+root);
            var before=Snapshot(root);
            var session=GameProjectSession.Open(root,recoverInterruptedSave:false);var config=session.GameConfiguration;
            Console.WriteLine($"IDENTIFIED: {config.Version}; Gen{session.Generation}; species {session.Entries.Count}; moves {session.MoveNames.Count}");
            string[] keys=session.Generation==6?["personal","move","item","gametext","encdata","mapGR","trdata","trpoke"]:["personal","move","item","gametext","encdata","trdata","trpoke","pickup"];
            var monitored=keys.Select(k=>Path.Combine(config.RomFS,config.GetGARCFileName(k)))
                .Concat(session.Generation==6?new[]{Path.Combine(config.RomFS,config.ORAS?"a/0/2/9":"a/0/3/1")}:Array.Empty<string>()).Concat(Directory.GetFiles(config.ExeFS)).Concat(Directory.GetFiles(config.RomFS).Where(p=>p.EndsWith(".cro",StringComparison.OrdinalIgnoreCase)))
                .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p=>p,Hash);
            void Probe(string name,Action read)
            {
                try { read();Console.WriteLine("PASS REAL READ: "+name); }
                catch(Exception ex){Console.WriteLine("BLOCKED REAL READ: "+name+"; "+ex.GetType().Name+": "+ex.Message);}
            }
            Probe("Wild first and last",()=>{var editor=new GameEncounterService(session);_=editor.Read(editor.Entries[0].Id);_=editor.Read(editor.Entries[^1].Id);Console.WriteLine($"Wild records {editor.Entries.Count}");});
            Probe("Static first and last",()=>{IProjectEditor editor=session.Generation==6?new GameStaticEncounter6Service(session):new GameStaticEncounterService(session);_=editor.Read(editor.Entries[0].Id);_=editor.Read(editor.Entries[^1].Id);Console.WriteLine($"Static records {editor.Entries.Count}");});
            Probe("Evolutions first and last",()=>{var editor=new GameEvolutionService(session);_=editor.Read(editor.Entries[0].Id);_=editor.Read(editor.Entries[^1].Id);});
            Probe("Egg moves first and last",()=>{var editor=new GameEggMoveService(session);_=editor.Read(editor.Entries[0].Id);_=editor.Read(editor.Entries[^1].Id);});
            if(session.Generation==7)Probe("Machines and tutors Gen7",()=>{var editor=new GameMachineTutorService(session);Console.WriteLine($"Machines {editor.Machines.Count}; tutors {editor.Tutors.Count}");});
            Probe("Pickup",()=>{_=new GamePickupService(session);});
            Probe("Type Chart",()=>{_=new GameTypeChartService(session).Read();});
            Probe("Shiny Rate",()=>{_=new GameShinyRateService(session).Read();});
            Probe("Trainers first and last",()=>{var s=new GameTrainerService(session);_=s.Read(s.Entries[0].Id);_=s.Read(s.Entries[^1].Id);Console.WriteLine("Trainer entries "+s.Entries.Count);});
            if(session.Generation==7)Probe("Field Items",()=>{var s=new GameFieldItemsService(session);if(s.Entries.Count>0){_=s.Read(s.Entries[0].Id);_=s.Read(s.Entries[^1].Id);}Console.WriteLine("Field item entries "+s.Entries.Count);});
            if(session.Generation==7)Probe("Marts Gen7",()=>{var shops=new GameMartService(session);Console.WriteLine($"Shop tables {shops.RegularShops.Count}/{shops.BpShops.Count}; expansion {shops.SupportsExpansion}");});
            Probe("economy",()=>{var rows=new GameItemService(session).PriceRows();Console.WriteLine($"Economy rows {rows.Count}, default included {rows.Count(r=>r.Included)}");});
            if(session.Generation==6)
            {
                Probe("Gen6 field script codec",()=>
                {
                    string path=Path.Combine(config.RomFS,config.ORAS?"a/0/2/9":"a/0/3/1");
                    var scripts=new pk3DS.Core.CTR.GARC.MemGARC(File.ReadAllBytes(path));
                    foreach(int index in config.ORAS?new[]{39,57}:new[]{17,26})
                    {
                        byte[] raw=scripts.GetFile(index);var decoded=Gen6ScriptCodec.Decode(raw);
                        if(!Gen6ScriptCodec.Rebuild(raw,decoded.Instructions).SequenceEqual(raw))throw new IOException("Script no-op changed bytes.");
                        if(!new pk3DS.Core.Script(raw).DecompressedInstructions.SequenceEqual(decoded.Instructions.Chunk(4).Select(b=>BitConverter.ToUInt32(b))))throw new IOException("Core script decoding differs.");
                        Console.WriteLine($"Script {index}: {decoded.Instructions.Length/4} words; identical Core decode and no-op bytes.");
                    }
                });
                Probe("Marts Gen6",()=>{var s=new GameMart6Service(session);_=s.Read(s.Entries[0].Id);_=s.Read(s.Entries[^1].Id);Console.WriteLine("Mart entries "+s.Entries.Count);});
                Probe("MT/MO",()=>{_ = new GameMachine6Service(session).Read(1);});
                Probe("O-Powers",()=>{_ = new GameOPowerService(session).Read(1);});
                Probe("Progressive trades preview",()=>{var p=new GameTrade6Service(session).Preview(new Dictionary<string,int>{{"Offers",0}},[1]);Console.WriteLine("Trade changes "+p.Changes.Count);});
                Probe("Catch Zones preview",()=>{var p=new GameCatchZoneService(session).Preview();Console.WriteLine("Catch changes "+p.Changes.Count);});
                Probe("Static Gen6",()=>{var s=new GameStaticEncounter6Service(session);_=s.Read(s.Entries[0].Id);Console.WriteLine("Static entries "+s.Entries.Count);});
            }
            if(!config.SM)Probe("move balance full preview",()=>
            {
                var moves=new GameMoveService(session);var p=moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings(),moves.Entries.Select(e=>e.Id).ToArray());
                Console.WriteLine($"Balance changes {p.Changes.Count}; additional files {p.AdditionalFileCount}");
            });
            else Console.WriteLine("NOT APPLICABLE: Progressive move balance excluded from SM by user scope.");
            foreach(var entry in monitored)if(Hash(entry.Key)!=entry.Value)throw new IOException("ROM modified during read checks: "+entry.Key);
            if(!before.SequenceEqual(Snapshot(root)))throw new IOException("ROM tree metadata changed during read checks.");
            Console.WriteLine($"PRESERVED REAL ROM: {monitored.Count} SHA-256 fingerprints and full file inventory unchanged.");
        }
    }
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    private static string[] Snapshot(string root)=>Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
        .Select(path=>{var f=new FileInfo(path);return path+"|"+f.Length+"|"+f.LastWriteTimeUtc.Ticks;}).ToArray();
}
