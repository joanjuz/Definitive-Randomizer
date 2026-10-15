using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class Gen6EncounterOrderChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            byte[] map=new byte[32+(f.Game.ORAS?0xF6:0x178)];map[0]=0x7C;
            BitConverter.GetBytes(f.Game.ORAS?18:16).CopyTo(map,16);
            BitConverter.GetBytes(map.Length).CopyTo(map,20);
            BitConverter.GetBytes((ushort)25).CopyTo(map,32);map[34]=10;map[35]=12;
            byte[] shared=new byte[32+0xF4];BitConverter.GetBytes(18).CopyTo(shared,4);map.AsSpan(32,0xF4).CopyTo(shared.AsSpan(32));
            byte[][] physical=f.Game.ORAS?[GameCatchZoneService.Compress(map),new byte[56],shared]:[GameCatchZoneService.Compress(map),new byte[56]];
            string path=ModuleChecks.PathFor(f,"encdata");f.WriteGarc(path,physical,f.Game.GetGARCReference("encdata"));
            byte[] source=File.ReadAllBytes(path);var decoded=new GARC.LazyGARC(source);
            var inOrder=Gen6EncounterArchiveOrder.EditorOrder(source,Enumerable.Range(0,physical.Length).Select(i=>decoded[i]).ToArray());
            check(inOrder[^1].SequenceEqual(map)&&inOrder[0].Length==56,version+" real GARC member order maps to legacy editor order");
            check(Gen6EncounterArchiveOrder.PhysicalOrder(source,inOrder)[0].SequenceEqual(map),version+" editor order maps back to original physical member");
            var editor=new GameEncounterService(f.Session);var entry=editor.Entries.Single();var values=EditorValues.Flatten(editor.Read(entry.Id));
            check(values["S0.Species"]==25&&values["S0.Min"]==10,version+" first compressed map read using actual physical GARC order");
            values["S0.Species"]=7;editor.Save(entry.Id,values);
            var after=new GARC.LazyGARC(File.ReadAllBytes(path));
            check(BitConverter.ToUInt16(after[0],32)==7&&new GARC.MemGARC(File.ReadAllBytes(path)).GetFile(1).SequenceEqual(physical[1]),version+" save edits compressed map and preserves raw zone member");
            if(f.Game.ORAS)check(after[2].AsSpan(32,0xF4).SequenceEqual(after[0].AsSpan(32,0xF4)),"ORAS physical shared EN mirror synchronized after native edit");
            reject(()=>Gen6EncounterArchiveOrder.PhysicalOrder(source,[]),version+" mismatched logical archive length rejected");
        }
    }
}
