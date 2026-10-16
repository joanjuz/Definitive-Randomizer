using System.IO;
using System.Text.Json;
using ThreeDS.Progressive.Services;

internal static class CatchZoneChecks
{
    internal static void Run(Action<bool,string> check, Action<Action,string> reject)
    {
        foreach (string family in new[] { "XY", "ORAS" })
        {
            string manifestPath = Path.Combine(AppContext.BaseDirectory,"catch_zone_patches",family+".json");
            var production = CatchZonePatchEngine.Load(File.ReadAllBytes(manifestPath),family);
            check(production.Files!.Count==(family=="XY"?61:174),family+" production manifest distributed and parsed");
            check(production.Files.Any(f=>f.Scope=="raw") && production.Files.Any(f=>f.Scope=="mapGR") && production.Files.Any(f=>f.Scope=="encdata"),family+" production manifest retains all three scopes");
            byte[] source = [1,2,3,4], target = [1,9,3,4];
            var file = new CatchZoneFile { Scope="encdata", Path="000.bin", Length=4, TargetLength=4,
                SourceSha256=CatchZonePatchEngine.Hash(source), TargetSha256=CatchZonePatchEngine.Hash(target),
                Hunks=[new() {Offset=1,Data="CQ=="}] };
            var manifest = new CatchZoneManifest { Format=2, Game=family, Files=[file] };
            byte[] Json() => JsonSerializer.SerializeToUtf8Bytes(manifest);
            IReadOnlyList<CatchZonePatchEngine.Result> Run(byte[]? input) => CatchZonePatchEngine.Evaluate(Json(),family,(_,_)=>input);
            string tag = family + " Catch Zones ";
            check(Run(source)[0].Bytes!.SequenceEqual(target) && source.SequenceEqual(new byte[] {1,2,3,4}),tag+"hunks verified without mutating source");
            check(Run(target)[0].AlreadyApplied,tag+"already applied detected");
            reject(()=>Run([1,2,3,5]),tag+"incompatible original rejected");
            byte[] variantSource=[1,2,3,5],variantTarget=[1,9,3,5];
            file.SourceVariants=[new(){Label="Paired version",SourceSha256=CatchZonePatchEngine.Hash(variantSource),TargetSha256=CatchZonePatchEngine.Hash(variantTarget)}];
            check(Run(variantSource)[0].Bytes!.SequenceEqual(variantTarget),tag+"verified paired version preserves bytes outside patch");
            check(Run(variantTarget)[0].AlreadyApplied&&Run(target)[0].AlreadyApplied,tag+"both paired version targets are idempotent");
            reject(()=>Run([1,2,3,6]),tag+"unknown revision still rejected with variants installed");
            file.SourceVariants[0].TargetSha256=CatchZonePatchEngine.Hash(target);
            reject(()=>Run(variantSource),tag+"variant output must match its own verified target");
            file.SourceVariants[0].TargetSha256=CatchZonePatchEngine.Hash(variantTarget);
            file.SourceVariants.Add(file.SourceVariants[0]);reject(()=>Run(source),tag+"duplicate variant origins rejected");file.SourceVariants.RemoveAt(1);
            file.SourceVariants[0].SourceSha256="invalid";reject(()=>Run(source),tag+"variant hash validated even for base version");
            file.SourceVariants=[];
            reject(()=>Run(null),tag+"missing patch source rejected");
            foreach (string path in new[] {"../x","a/../x","/x","C:\\x","a\\..\\x","a//b","x:stream","a/.","x.","x "})
            { file.Path=path; reject(()=>Run(source),tag+"unsafe path " + path); }
            file.Path="000.bin";
            manifest.Files!.Add(file); reject(()=>Run(source),tag+"duplicate targets rejected"); manifest.Files.RemoveAt(1);
            file.Scope="exeFS"; reject(()=>Run(source),tag+"unknown scope rejected"); file.Scope="mapGR";
            check(Run(source)[0].Scope=="mapGR",tag+"map scope retained"); file.Scope="raw";
            check(Run(source)[0].Scope=="raw",tag+"raw scope retained");
            file.PatchMode="guess"; reject(()=>Run(source),tag+"unknown mode rejected"); file.PatchMode="hunks";
            file.Hunks![0].Offset=4; reject(()=>Run(source),tag+"out of bounds hunk rejected"); file.Hunks[0].Offset=1;
            file.Hunks.Add(new(){Offset=1,Data="CQ=="}); reject(()=>Run(source),tag+"overlap rejected"); file.Hunks.RemoveAt(1);
            file.Hunks[0].Data="bad!"; reject(()=>Run(source),tag+"invalid base64 rejected"); file.Hunks[0].Data="CA==";
            reject(()=>Run(source),tag+"incorrect output hash rejected"); file.Hunks[0].Data="CQ==";
            file.TargetLength=5; reject(()=>Run(source),tag+"incorrect output length rejected"); file.TargetLength=4;
            file.SourceSha256="invalid"; reject(()=>Run(source),tag+"malformed hash rejected"); file.SourceSha256=CatchZonePatchEngine.Hash(source);
            file.PatchMode="delta"; file.TargetLength=5; target=[1,2,8,9,4]; file.TargetSha256=CatchZonePatchEngine.Hash(target);
            file.Delta=[new(){CopyOffset=0,Length=2},new(){Length=2,Data="CAk="},new(){CopyOffset=3,Length=1}];
            check(Run(source)[0].Bytes!.SequenceEqual(target),tag+"delta can resize with copies and literals");
            check(Run(target)[0].AlreadyApplied,tag+"delta already applied detected");
            file.SourceVariants=[new(){Label="Legacy applied",SourceSha256=CatchZonePatchEngine.Hash(new byte[]{1,7,3,4}),TargetSha256=CatchZonePatchEngine.Hash(target),
                Delta=[new(){CopyOffset=0,Length=1},new(){Length=3,Data="AggJ"},new(){CopyOffset=3,Length=1}]}];
            check(Run([1,7,3,4])[0].Bytes!.SequenceEqual(target),tag+"legacy applied variant uses its own verified migration delta");
            file.SourceVariants[0].Delta![0].CopyOffset=4;reject(()=>Run([1,7,3,4]),tag+"migration delta validates copy bounds");file.SourceVariants=[];
            file.Delta[0].Length=6; reject(()=>Run(source),tag+"delta output bound checked before allocation"); file.Delta[0].Length=2;
            file.Delta[2].CopyOffset=4; reject(()=>Run(source),tag+"delta source range rejected"); file.Delta[2].CopyOffset=3;
            file.Delta[1].Length=1; reject(()=>Run(source),tag+"delta literal length rejected"); file.Delta[1].Length=2;
            file.Delta[1].CopyOffset=-2; reject(()=>Run(source),tag+"invalid negative copy sentinel rejected"); file.Delta[1].CopyOffset=-1;
            file.Operation="delete";
            check(Run(source)[0].Bytes is null && !Run(source)[0].AlreadyApplied,tag+"deletion proposed without writing");
            check(Run(null)[0].AlreadyApplied,tag+"missing deletion idempotent");
            reject(()=>Run([9,9,9,9]),tag+"deletion validates original hash");
            manifest.Game=family=="XY"?"ORAS":"XY"; reject(()=>Run(source),tag+"wrong family rejected"); manifest.Game=family;
            manifest.Format=1; reject(()=>Run(source),tag+"old format rejected"); manifest.Format=2;
            manifest.Files=null; reject(()=>Run(source),tag+"null file list rejected"); manifest.Files=[];
            reject(()=>Run(source),tag+"empty file list rejected");
        }
        reject(()=>CatchZonePatchEngine.Load("{"u8.ToArray(),"XY"),"Catch Zones malformed JSON rejected");
        reject(()=>CatchZonePatchEngine.Load("null"u8.ToArray(),"XY"),"Catch Zones null manifest rejected");
        reject(()=>CatchZonePatchEngine.Load("{}"u8.ToArray(),"USUM"),"Catch Zones Gen7 rejected");
    }
}
