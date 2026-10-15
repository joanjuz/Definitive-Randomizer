using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;

internal static class RebuildCacheChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.XY);GlobalCompletionChecks.SetupCode(f);
        string icon=Path.Combine(f.Game.ExeFS,"icon.bin");File.WriteAllBytes(icon,new byte[]{1,2,3});
        string outside=Path.Combine(Path.GetTempPath(),"rebuild-cache-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(outside);
        try
        {
            foreach(byte flag in new byte[]{0,1,0xA4,0xA5})
            {
                byte[] source=Enumerable.Range(0,0x800).Select(i=>(byte)i).ToArray();source[0xD]=flag;
                string input=Path.Combine(outside,"exheader-source-"+flag),prepared=Path.Combine(outside,"exheader-stage-"+flag);File.WriteAllBytes(input,source);
                GameRomRebuildService.PrepareCompressedExheader(input,prepared);byte[] actual=File.ReadAllBytes(prepared);
                check(actual[0xD]==(flag|1)&&actual.Where((_,i)=>i!=0xD).SequenceEqual(source.Where((_,i)=>i!=0xD))&&File.ReadAllBytes(input).SequenceEqual(source),"Rebuild stages BLZ compression flag without changing source or other header bytes: "+flag);
            }
            string codePath=Directory.GetFiles(f.Game.ExeFS).Single(p=>Path.GetFileName(p).Contains("code",StringComparison.OrdinalIgnoreCase));byte[] originalCode=File.ReadAllBytes(codePath);var originalNames=Directory.GetFiles(f.Game.ExeFS).Select(Path.GetFileName).Order().ToArray();
            var shared=new ExeFsBuildCache();var service=new GameRomRebuildService(f.Session,shared);var stages=new List<(string Stage,int Value,int Max)>();
            string a=Path.Combine(outside,"a.bin"),b=Path.Combine(outside,"b.bin"),c=Path.Combine(outside,"c.bin");
            Task.Run(()=>service.BuildAsync(RomRebuildKind.ExeFS,a)).GetAwaiter().GetResult();var nextService=new GameRomRebuildService(f.Session,shared);Task.Run(()=>nextService.BuildAsync(RomRebuildKind.ExeFS,b,(stage,value,max)=>stages.Add((stage,value,max)))).GetAwaiter().GetResult();
            check(stages.Any(s=>s.Stage.Contains("reutilizando"))&&stages.Last().Value==100&&stages.Zip(stages.Skip(1)).All(pair=>pair.First.Value<=pair.Second.Value),"Batch rebuild across service instances reports cached ExeFS and monotonic completed phases");
            check(service.ExeFsCompressionCount==1&&File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)),"Repeated ExeFS rebuild reuses identical verified bytes with one compression");
            File.WriteAllBytes(icon,new byte[]{4,5,6});Task.Run(()=>service.BuildAsync(RomRebuildKind.ExeFS,c)).GetAwaiter().GetResult();
            check(service.ExeFsCompressionCount==2&&!File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(c)),"Changed ExeFS input invalidates cache and rebuilds");
            string unpack=Path.Combine(outside,"unpacked");ExeFS.UnpackExeFS(c,unpack);
            check(File.ReadAllBytes(Path.Combine(unpack,"icon.bin")).SequenceEqual(new byte[]{4,5,6}),"Cache invalidation exports the current icon bytes");
            check(File.Exists(Path.Combine(unpack,".code.bin"))&&!File.Exists(Path.Combine(unpack,"code.bin")),"Rebuild canonicalizes toolkit code.bin to mandatory .code ExeFS entry");
            check(File.ReadAllBytes(codePath).SequenceEqual(originalCode)&&Directory.GetFiles(f.Game.ExeFS).Select(Path.GetFileName).Order().SequenceEqual(originalNames),"Rebuild canonicalization preserves source code bytes and filenames");
            string duplicate=Path.Combine(f.Game.ExeFS,Path.GetFileName(codePath).StartsWith('.')?"code.bin":".code.bin");File.Copy(codePath,duplicate);
            bool rejected=false;try{Task.Run(()=>service.BuildAsync(RomRebuildKind.ExeFS,Path.Combine(outside,"duplicate.bin"))).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}
            check(rejected&&!File.Exists(Path.Combine(outside,"duplicate.bin")),"Rebuild rejects duplicate code/.code before publishing output");
        }
        finally{Directory.Delete(outside,true);}
    }
}
