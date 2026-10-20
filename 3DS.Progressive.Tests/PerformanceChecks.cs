using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class PerformanceChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var fixture=new Fixture(version);ModuleChecks.CreateArchives(fixture);
            string path=ModuleChecks.PathFor(fixture,"encdata");var records=ModuleChecks.Read(fixture,"encdata");
            if(fixture.Game.Generation==7)records[1]=new byte[8*1024*1024];
            else records=[..records,new byte[8*1024*1024]];
            fixture.WriteGarc(path,records,fixture.Game.GetGARCReference("encdata"));
            using(var canceled=new CancellationTokenSource())
            {
                canceled.Cancel();bool rejected=false;
                try{_=new GameEncounterService(fixture.Session,canceled.Token);}catch(OperationCanceledException){rejected=true;}
                check(rejected,version+"_Encounters_CancellationStopsConstruction");
                if(fixture.Game.Generation==6)
                {
                    rejected=false;try{_=new GameFieldItems6Service(fixture.Session,canceled.Token);}catch(OperationCanceledException){rejected=true;}
                    check(rejected,version+"_FieldItems_CancellationStopsConstruction");
                }
            }
            IProjectEditor editor=new GameEncounterService(fixture.Session);
            VerifySnapshot(editor,path,version+"_Encounters",check,reject);
            if(fixture.Game.Generation==7)
            {
                editor=new GameFieldItemsService(fixture.Session);
                VerifySnapshot(editor,path,version+"_FieldItems",check,reject);
                using var canceled=new CancellationTokenSource();canceled.Cancel();
                bool rejected=false;try{_=new GameFieldItemsService(fixture.Session,canceled.Token);}catch(OperationCanceledException){rejected=true;}
                check(rejected,version+"_FieldItems_CancellationStopsConstruction");
            }
        }
        FieldItems6Checks.Run(check,reject);
        RebuildCacheChecks.Run(check);
        CheckRomFs(check);
    }
    private static void VerifySnapshot(IProjectEditor editor,string path,string tag,Action<bool,string> check,Action<Action,string> reject)
    {
        int id=editor.Entries[0].Id;var first=EditorValues.Flatten(editor.Read(id));
        long start=GC.GetAllocatedBytesForCurrentThread();_=editor.Read(id);long bytes=GC.GetAllocatedBytesForCurrentThread()-start;
        check(bytes<1024*1024,tag+"_SelectionDoesNotCopyLargeArchive");
        var options=EditorValues.Flatten(editor.RandomOptions);int[] ids=editor.Entries.Select(e=>e.Id).ToArray();
        EditorProposal Preview()=>editor switch
        {
            GameFieldItemsService field=>field.Preview(options,ids,1),
            GameEncounterService wild=>wild.Preview(options,ids,1),
            _=>editor.Preview(options,ids)
        };
        var preview=Preview();
        check(first.OrderBy(p=>p.Key).SequenceEqual(EditorValues.Flatten(editor.Read(id)).OrderBy(p=>p.Key)),tag+"_PreviewDoesNotMutateReadSnapshot");
        var edited=new Dictionary<string,int>(first);
        string key=edited.ContainsKey("Item")?"Item":edited.ContainsKey("Min")?"Min":"S0.Min";
        edited[key]=edited[key]==1?2:1;editor.Save(id,edited);
        check(EditorValues.Flatten(editor.Read(id))[key]==edited[key],tag+"_SaveRefreshesSnapshot");
        preview=Preview();
        byte[] original=File.ReadAllBytes(path);var time=File.GetLastWriteTimeUtc(path);byte[] foreign=(byte[])original.Clone();foreign[^1]^=0x5A;
        File.WriteAllBytes(path,foreign);File.SetLastWriteTimeUtc(path,time);
        reject(()=>editor.Preview(options,ids),tag+"_SameSizeTimestampChangeRejectsPreview");
        reject(()=>editor.Save(id,first),tag+"_SameSizeTimestampChangeRejectsSave");
        reject(()=>editor.Apply(preview),tag+"_SameSizeTimestampChangeRejectsApply");
        check(File.ReadAllBytes(path).SequenceEqual(foreign),tag+"_RejectedWritePreservesExternalBytes");
        File.WriteAllBytes(path,original);File.SetLastWriteTimeUtc(path,time);
    }
    internal static void CheckRomFs(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"romfs-perf-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string source=Path.Combine(root,"input");Directory.CreateDirectory(source);
            foreach(int size in new[]{0,1,4095,4096,4097,1048577})File.WriteAllBytes(Path.Combine(source,size+".bin"),Enumerable.Range(0,size).Select(i=>(byte)(i*37+11)).ToArray());
            var before=Directory.GetFiles(source).ToDictionary(p=>Path.GetFileName(p)!,p=>SHA256.HashData(File.ReadAllBytes(p)));
            string output=Path.Combine(root,"romfs.bin");var timings=new Dictionary<string,TimeSpan>();
            using var progress=GameRomRebuildService.CreateLegacyProgressBar();
            RomFS.BuildRomFS(source,output,null,progress,(name,time)=>timings[name]=time);
            byte[] image=File.ReadAllBytes(output);
            for(int level=0;level<3;level++)
            {
                int header=12+level*24;long hashOffset=BitConverter.ToInt64(image,header),length=BitConverter.ToInt64(image,header+8);
                int blockSize=1<<BitConverter.ToInt32(image,header+16);long masterLength=BitConverter.ToUInt32(image,8);
                long Align(long v,long n)=>(v+n-1)/n*n;
                long dataBase=Align(masterLength+0x60,4096);
                long level2Length=BitConverter.ToInt64(image,12+2*24+8);
                long hashBase=Align(dataBase+level2Length,4096);
                long dataAt=level==2?dataBase:hashBase+hashOffset;
                long hashesAt=level==0?0x60:hashBase+BitConverter.ToInt64(image,12+(level-1)*24);
                bool valid=true;
                for(long at=0;at<length;at+=blockSize)
                {
                    var hash=SHA256.HashData(image.AsSpan(checked((int)(dataAt+at)),blockSize));
                    valid&=hash.AsSpan().SequenceEqual(image.AsSpan(checked((int)(hashesAt+(at/blockSize)*32)),32));
                }
                check(valid,"RomFS_IndependentIVFC_Level"+level);
            }
            check(timings.Keys.Order().SequenceEqual(new[]{"RomFS.Copy","RomFS.IVFC.Level0","RomFS.IVFC.Level1","RomFS.IVFC.Level2","RomFS.Metadata"}.Order()),"RomFS_AllPhasesMeasured");
            check(before.All(p=>SHA256.HashData(File.ReadAllBytes(Path.Combine(source,p.Key!))).SequenceEqual(p.Value)),"RomFS_StreamingPreservesAllSourceFiles");
        }
        finally{Directory.Delete(root,true);}
    }
}
