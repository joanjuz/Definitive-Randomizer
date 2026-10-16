using System.IO;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

internal static class CatchZoneIntegrationChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);string tag=version+" Catch Zones integration ";
            byte[] source=Enumerable.Range(0,100).Select(i=>(byte)i).ToArray(),target=source.Append((byte)250).ToArray();
            string enc=Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("encdata"));
            string map=Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("mapGR"));
            byte[] compressed=GameCatchZoneService.Compress(source);
            f.WriteGarc(enc,[compressed,[7,8,9]],f.Game.GetGARCReference("encdata"));
            f.WriteGarc(map,[source,[6,5,4]],f.Game.GetGARCReference("mapGR"));
            string raw=Path.Combine(f.Game.RomFS,"extra.bin");File.WriteAllBytes(raw,source);
            string manifestPath=Path.Combine(f.Root,"catch.json");
            CatchZoneFile FileEntry(string scope,string path)=>new(){Scope=scope,Path=path,Length=source.Length,TargetLength=target.Length,
                SourceSha256=CatchZonePatchEngine.Hash(source),TargetSha256=CatchZonePatchEngine.Hash(target),PatchMode="delta",
                Delta=[new(){CopyOffset=0,Length=source.Length},new(){Length=1,Data="+g=="}]};
            var manifest=new CatchZoneManifest{Format=2,Game=version==GameVersion.XY?"XY":"ORAS",Files=[FileEntry("encdata","dec_000.bin"),FileEntry("mapGR","000.bin"),FileEntry("raw","extra.bin")]};
            void Write()=>File.WriteAllBytes(manifestPath,JsonSerializer.SerializeToUtf8Bytes(manifest));Write();
            var service=new GameCatchZoneService(f.Session,manifestPath);byte[] encBefore=File.ReadAllBytes(enc),mapBefore=File.ReadAllBytes(map);
            File.WriteAllBytes(raw,[9]);f.WriteGarc(map,[[9],[6,5,4]],f.Game.GetGARCReference("mapGR"));
            try { service.Preview();throw new Exception("Expected incompatible sources"); }
            catch(InvalidDataException ex) { check(ex.Message.Contains("2 archivos")&&ex.Message.Contains("mapGR/000.bin")&&ex.Message.Contains("raw/extra.bin"),tag+"all source mismatches reported before applying any changes"); }
            check(File.ReadAllBytes(enc).SequenceEqual(encBefore)&&File.ReadAllBytes(raw).SequenceEqual(new byte[]{9}),tag+"multi-source failure preserves unchanged and incompatible files");
            File.WriteAllBytes(raw,source);File.WriteAllBytes(map,mapBefore);
            var preview=service.Preview();
            check(preview.Changes.Count==3&&File.ReadAllBytes(enc).SequenceEqual(encBefore)&&File.ReadAllBytes(map).SequenceEqual(mapBefore)&&File.ReadAllBytes(raw).SequenceEqual(source),tag+"preview leaves all ROM files unchanged");
            service.Apply(preview);
            check(new GARC.LazyGARC(File.ReadAllBytes(enc))[0].SequenceEqual(target),tag+"compressed member resized and decoded exactly");
            check(new GARC.MemGARC(File.ReadAllBytes(enc)).Files[1].SequenceEqual(new byte[]{7,8,9})&&new GARC.MemGARC(File.ReadAllBytes(map)).Files[1].SequenceEqual(new byte[]{6,5,4}),tag+"unselected members preserve opaque bytes");
            check(new GARC.MemGARC(File.ReadAllBytes(map)).Files[0].SequenceEqual(target)&&File.ReadAllBytes(raw).SequenceEqual(target),tag+"map and raw applied together");
            check(f.Session.History.Read().Entries.Last().Files.Count==3,tag+"single history operation records all files");
            byte[] applied=File.ReadAllBytes(enc);int history=f.Session.History.Read().Entries.Count;var noop=service.Preview();service.Apply(noop);
            check(noop.Changes.Count==0&&File.ReadAllBytes(enc).SequenceEqual(applied)&&f.Session.History.Read().Entries.Count==history,tag+"already applied preserves container bytes and history");
            File.WriteAllBytes(raw,source);preview=service.Preview();File.WriteAllBytes(raw,[9]);reject(()=>service.Apply(preview),tag+"external source change blocks joint save");
            check(File.ReadAllBytes(enc).SequenceEqual(applied)&&File.ReadAllBytes(raw).SequenceEqual(new byte[]{9}),tag+"conflicting bytes preserved");
            File.WriteAllBytes(raw,source);preview=service.Preview();File.AppendAllText(manifestPath," ");reject(()=>service.Apply(preview),tag+"manifest change invalidates preview");Write();
            manifest.Files![0].Path="000.bin";Write();reject(()=>service.Preview(),tag+"wrong compression naming rejected by hash");manifest.Files[0].Path="dec_000.bin";
            manifest.Files[1].Path="999999.bin";Write();reject(()=>service.Preview(),tag+"missing GARC member rejected");manifest.Files[1].Path="000.bin";
            manifest.Files[2].Operation="delete";Write();reject(()=>service.Preview(),tag+"unsupported delete blocks whole preview");manifest.Files[2].Operation="patch";Write();
            reject(()=>service.Apply(new EditorProposal(Guid.NewGuid(),"foreign",0,[],[],new Dictionary<string,string>())),tag+"foreign proposal rejected");
        }
        using var gen7=new Fixture(GameVersion.USUM);reject(()=>new GameCatchZoneService(gen7.Session),"Catch Zones service rejects Gen7");
    }
}
