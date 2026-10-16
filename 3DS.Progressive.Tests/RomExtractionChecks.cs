using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;

internal static class RomExtractionChecks
{
    private static void U32(byte[] b,int at,int value)=>BitConverter.GetBytes(value).CopyTo(b,at);
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        string root=Path.Combine(Path.GetTempPath(),"progressive-extraction-checks-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string data=Path.Combine(root,"source");Directory.CreateDirectory(Path.Combine(data,"a","0","1"));
            Directory.CreateDirectory(Path.Combine(data,"Prueba Ñ"));Directory.CreateDirectory(Path.Combine(data,"vacio"));
            var originals=new Dictionary<string,byte[]>{{"a/0/1/7",Enumerable.Range(0,257).Select(i=>(byte)i).ToArray()},
                {"DllBattle.cro",CroIntegrityChecks.Cro(8)},{"Prueba Ñ/texto.bin","UTF-8 de prueba"u8.ToArray()}};
            foreach(var p in originals)File.WriteAllBytes(Path.Combine(data,p.Key.Replace('/',Path.DirectorySeparatorChar)),p.Value);
            string romfs=Path.Combine(root,"romfs.bin"),previous=Directory.GetCurrentDirectory();
            try
            {
                // Core's builder uses a process-relative temporary file; keep it inside this fixture.
                Directory.SetCurrentDirectory(root);File.WriteAllBytes(Path.Combine(root,"tempRomFS.bin"),new byte[]{7,8,9});using var progress=GameRomRebuildService.CreateLegacyProgressBar();
                RomFS.BuildRomFS(data,romfs,PB_Show:progress);
                check(File.ReadAllBytes(Path.Combine(root,"tempRomFS.bin")).SequenceEqual(new byte[]{7,8,9}),"RomFS rebuild preserves preexisting process-relative tempRomFS.bin");
                check(!Directory.GetFiles(root,".pk3ds-romfs-*.tmp").Any(),"RomFS successful rebuild consumes its private temporary file");
            }
            finally{Directory.SetCurrentDirectory(previous);}
            byte[] rom=File.ReadAllBytes(romfs),code=Enumerable.Range(0,4096).Select(i=>(byte)(i*11+7)).ToArray();
            foreach(bool compressed in new[]{false,true})
            {
                string exeDir=Path.Combine(root,"exe-"+compressed);Directory.CreateDirectory(exeDir);
                string codePath=Path.Combine(exeDir,".code.bin"),iconPath=Path.Combine(exeDir,"icon.bin");
                File.WriteAllBytes(codePath,code);File.WriteAllBytes(iconPath,[1,2,3,4]);
                if(compressed){using var progress=GameRomRebuildService.CreateLegacyProgressBar();_=new BLZCoder(["-en",codePath],progress);}
                check(!compressed||new FileInfo(codePath).Length<code.Length,"Extraction fixture BLZ compression changes encoded size "+compressed);
                string exePath=Path.Combine(root,"exefs-"+compressed+".bin");
                check(ExeFS.PackExeFS([codePath,iconPath],exePath),"Extraction fixture packs vanilla ExeFS "+compressed);
                byte[] exe=File.ReadAllBytes(exePath);int exeSize=(exe.Length+511)/512*512,romSize=(rom.Length+511)/512*512;
                const int exeOffset=0xE00;int romOffset=exeOffset+exeSize;
                check(new ExeFS(exePath).SuperBlockHash.SequenceEqual(SHA256.HashData(exe.AsSpan(0,0x200))),"ExeFS superblock hashes the complete 512-byte header "+compressed);
                byte[] cxi=new byte[romOffset+romSize];"NCCH"u8.CopyTo(cxi.AsSpan(0x100));
                U32(cxi,0x104,cxi.Length/512);U32(cxi,0x180,0x400);cxi[0x18F]=4;
                U32(cxi,0x190,0xA00/512);U32(cxi,0x194,1);U32(cxi,0x198,0xC00/512);U32(cxi,0x19C,1);
                U32(cxi,0x1A0,exeOffset/512);U32(cxi,0x1A4,exeSize/512);U32(cxi,0x1B0,romOffset/512);U32(cxi,0x1B4,romSize/512);
                if(compressed)cxi[0x200+0xD]=1;
                cxi.AsSpan(0xA00,512).Fill(0x31);cxi.AsSpan(0xC00,512).Fill(0x42);
                exe.CopyTo(cxi,exeOffset);rom.CopyTo(cxi,romOffset);
                foreach(bool ncsd in new[]{false,true})
                {
                    string tag=$"Extraction {(ncsd?"NCSD":"CXI")} BLZ={compressed} ";
                    byte[] container;
                    if(ncsd)
                    {
                        const int partition=0x6000;container=new byte[partition+cxi.Length];
                        "NCSD"u8.CopyTo(container.AsSpan(0x100));U32(container,0x120,partition/512);U32(container,0x124,cxi.Length/512);cxi.CopyTo(container,partition);
                    }
                    else container=cxi;
                    string input=Path.Combine(root,$"input-{compressed}-{ncsd}.bin"),output=Path.Combine(root,$"output-{compressed}-{ncsd}");
                    File.WriteAllBytes(input,container);
                    var service=new GameRomExtractionService(data);var result=service.ExtractAsync(input,output).GetAwaiter().GetResult();
                    check(result.Folder==output&&result.SourceSha256==Convert.ToHexString(SHA256.HashData(container)),tag+"container hash and destination returned");
                    check(originals.All(p=>File.ReadAllBytes(Path.Combine(output,"RomFS",p.Key.Replace('/',Path.DirectorySeparatorChar))).SequenceEqual(p.Value)),tag+"all RomFS bytes and Unicode paths roundtrip");
                    check(Directory.Exists(Path.Combine(output,"RomFS","vacio")),tag+"empty directories preserved");
                    check(File.ReadAllBytes(Path.Combine(output,"ExeFS",".code.bin")).SequenceEqual(code)&&File.ReadAllBytes(Path.Combine(output,"ExeFS","icon.bin")).SequenceEqual(new byte[]{1,2,3,4}),tag+"ExeFS code decoded and other files intact");
                    check(File.ReadAllBytes(Path.Combine(output,"exheader.bin")).SequenceEqual(cxi.AsSpan(0x200,0x800).ToArray())&&File.ReadAllBytes(Path.Combine(output,"ncchheader.bin")).SequenceEqual(cxi[..0x200]),tag+"rebuild metadata preserved");
                    check(File.ReadAllBytes(Path.Combine(output,"plain.bin")).SequenceEqual(cxi.AsSpan(0xA00,512).ToArray())&&File.ReadAllBytes(Path.Combine(output,"logo.bcma.lz")).SequenceEqual(cxi.AsSpan(0xC00,512).ToArray()),tag+"plain region and logo preserved");
                    var manifest=JsonSerializer.Deserialize<RomExtractionManifest>(File.ReadAllBytes(Path.Combine(output,"extraction-manifest.json")))!;
                    check(manifest.CodeDecompressed==compressed&&manifest.Files.All(p=>new FileInfo(Path.Combine(output,p.Path)).Length==p.Bytes&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output,p.Path))))==p.Sha256),tag+"final manifest hashes every output including decoded code");
                    check(File.ReadAllBytes(input).SequenceEqual(container)&&originals.All(p=>File.ReadAllBytes(Path.Combine(data,p.Key.Replace('/',Path.DirectorySeparatorChar))).SequenceEqual(p.Value)),tag+"container and open project remain unchanged");
                    reject(()=>service.ExtractAsync(input,output).GetAwaiter().GetResult(),tag+"existing destination rejected");
                }
                byte[] encrypted=(byte[])cxi.Clone();ulong program=0x0004000000001234;
                BitConverter.GetBytes(program).CopyTo(encrypted,0x118);BitConverter.GetBytes((ushort)2).CopyTo(encrypted,0x112);encrypted[0x18F]=1;
                var regions=new[]{(Offset:0x200,Length:0x800),(Offset:exeOffset,Length:exeSize),(Offset:romOffset,Length:romSize)};
                for(int section=0;section<regions.Length;section++)
                {
                    var region=regions[section];byte[] result=new byte[region.Length];new AesCtr(new byte[16],program,(ulong)(section+1)<<56).TransformBlock(encrypted,region.Offset,region.Length,result,0);result.CopyTo(encrypted,region.Offset);
                }
                string fixedInput=Path.Combine(root,"fixed-"+compressed+".cxi"),fixedOutput=Path.Combine(root,"fixed-output-"+compressed);
                File.WriteAllBytes(fixedInput,encrypted);var fixedResult=new GameRomExtractionService(data).ExtractAsync(fixedInput,fixedOutput).GetAwaiter().GetResult();
                check(File.ReadAllBytes(Path.Combine(fixedOutput,"ExeFS",".code.bin")).SequenceEqual(code)&&originals.All(p=>File.ReadAllBytes(Path.Combine(fixedOutput,"RomFS",p.Key.Replace('/',Path.DirectorySeparatorChar))).SequenceEqual(p.Value)),"Fixed-key pk3DS extraction roundtrips code and RomFS bytes "+compressed);
                check(fixedResult.SourceSha256==Convert.ToHexString(SHA256.HashData(encrypted))&&File.ReadAllBytes(fixedInput).SequenceEqual(encrypted)&&!Directory.GetFiles(fixedOutput,"*.tmp").Any(),"Fixed-key source hash and original preserved; decoded container not published "+compressed);
                if(compressed)continue;
                string badInput=Path.Combine(root,"bad.cxi"),badOutput=Path.Combine(root,"bad-output");
                var extractor=new GameRomExtractionService(data);
                void Bad(byte[] bytes,string why)
                {
                    File.WriteAllBytes(badInput,bytes);
                    reject(()=>extractor.ExtractAsync(badInput,badOutput).GetAwaiter().GetResult(),"Extraction rejects "+why);
                    check(!Directory.Exists(badOutput)&&!Directory.EnumerateDirectories(root,".progressive-extract-*").Any(),"Extraction failure publishes nothing and leaves no staging: "+why);
                }
                Bad(cxi[..0x180],"truncated NCCH");
                byte[] invalid=(byte[])cxi.Clone();U32(invalid,0x1B4,int.MaxValue);Bad(invalid,"out-of-bounds RomFS");
                invalid=(byte[])cxi.Clone();invalid[exeOffset+0x200]^=1;Bad(invalid,"corrupt ExeFS hash");
                invalid=(byte[])cxi.Clone();invalid.AsSpan(exeOffset,8).Clear();".."u8.CopyTo(invalid.AsSpan(exeOffset));Bad(invalid,"unsafe ExeFS name");
                int body=(int)((0x60+BitConverter.ToUInt32(rom,8)+4095)/4096*4096);
                int filesAt=body+(int)BitConverter.ToUInt32(rom,body+0x1C);
                invalid=(byte[])cxi.Clone();U32(invalid,romOffset+filesAt+4,0);Bad(invalid,"RomFS sibling cycle");
                invalid=(byte[])cxi.Clone();U32(invalid,romOffset+filesAt+28,4);System.Text.Encoding.Unicode.GetBytes("..").CopyTo(invalid,romOffset+filesAt+32);Bad(invalid,"unsafe RomFS name");
                string valid=Path.Combine(root,"valid.cxi");File.WriteAllBytes(valid,cxi);
                reject(()=>extractor.ExtractAsync(valid,Path.Combine(data,"nested-output")).GetAwaiter().GetResult(),"Extraction output inside the open project rejected");
                using var cancel=new CancellationTokenSource();cancel.Cancel();bool canceled=false;
                try{extractor.ExtractAsync(valid,badOutput,cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}
                check(canceled&&!Directory.Exists(badOutput),"Extraction cancellation leaves no destination");
            }
        }
        finally{Directory.Delete(root,true);}
    }
}
