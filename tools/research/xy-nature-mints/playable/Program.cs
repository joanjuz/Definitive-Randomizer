using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Gen6;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

internal static class Program
{
    const string Purpose = "xy-mint-isolated-research-v1";
    static void Check(bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); }
    static void Save(string path, object data)
    {
        using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        JsonSerializer.Serialize(stream,data,new JsonSerializerOptions { WriteIndented = true });
    }
    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    static void NoLinks(string path)
    {
        for (FileSystemInfo? part = new FileInfo(Path.GetFullPath(path)); part is not null;
            part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
            Check((part.Attributes & FileAttributes.ReparsePoint) == 0, "No links: " + part.Name);
    }
    static void Root(string root)
    {
        _=Target(root);
        NoLinks(Directory.Exists(root) ? root : Path.GetDirectoryName(Path.GetFullPath(root))!);
    }
    static ulong Target(string root)
    {
        string name=Path.GetFileName(Path.GetFullPath(root));
        if(name.StartsWith("PruebasMentasXY-X-",StringComparison.Ordinal))return 0x0004000000055D00;
        if(name.StartsWith("PruebasMentasXY-Y-",StringComparison.Ordinal))return 0x0004000000055E00;
        throw new InvalidDataException("Explicit isolated PruebasMentasXY-X- or PruebasMentasXY-Y- folder required");
    }
    static void Headers(string root)
    {
        byte[] header=File.ReadAllBytes(Path.Combine(root,"Proyecto/ncchheader.bin"));
        byte[] ex=File.ReadAllBytes(Path.Combine(root,"Proyecto/exheader.bin"));
        Check(header.Length>=0x120&&header.AsSpan(0x100,4).SequenceEqual("NCCH"u8)&&ex.Length>=0x208&&
            BitConverter.ToUInt64(header,0x118)==Target(root)&&BitConverter.ToUInt64(ex,0x200)==Target(root),"Crossed or unsupported real headers");
    }
    static void NewEvidence(string root,string output)
    {
        Root(root); NoLinks(Path.Combine(root,"Proyecto")); NoLinks(Path.Combine(root,"evidence"));
        Check(Path.GetDirectoryName(Path.GetFullPath(output))!.Equals(Path.GetFullPath(Path.Combine(root,"evidence")),StringComparison.OrdinalIgnoreCase),
            "Output must be a new file directly in the isolated evidence directory");
        Check(!File.Exists(output)&&!Directory.Exists(output),"Output must be new");
        using var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"evidence/source.json")));
        Check(marker.RootElement.GetProperty("Purpose").GetString()==Purpose,"Missing isolation marker");
        Headers(root);
    }
    static void Extract(string rom, string root)
    {
        Check(!Directory.Exists(root) && !File.Exists(root), "Output must be new"); Root(root); NoLinks(rom);
        Directory.CreateDirectory(Path.Combine(root, "evidence"));
        var result = new GameRomExtractionService().ExtractAsync(rom, Path.Combine(root, "Proyecto")).GetAwaiter().GetResult();
        Headers(root);
        Save(Path.Combine(root, "evidence/source.json"), new { Purpose, Rom = Path.GetFullPath(rom), result.SourceSha256 });
    }
    static void Inspect(string root, string output)
    {
        NewEvidence(root,output);
        var project = GameProjectSession.Open(Path.Combine(root, "Proyecto"), false);
        Check(project.GameConfiguration.XY, "X/Y only");
        var config = project.GameConfiguration;
        var itemData = new GARC.MemGARC(File.ReadAllBytes(Path.Combine(config.RomFS, config.GetGARCFileName("item")))).Files;
        var texts = new List<object>();
        for (int language = 0; language < 8; language++)
        {
            var reference = config.GetGARCReference("gametext").GetRelativeGARC(language);
            byte[][] banks = new GARC.MemGARC(File.ReadAllBytes(Path.Combine(config.RomFS, reference.Reference))).Files;
            texts.Add(new { Language = language, Banks = new[]{1,47,140}.Concat(Enumerable.Range(90, 12)).Select(index => new { Index = index, Lines = new TextFile(config, banks[index], config.RemapCharacters).Lines }).ToArray() });
        }
        Save(output, new { Items = itemData.Select((record, id) => new { Id = id, Size = record.Length, Sha256 = Hash(record), Hex = Convert.ToHexString(record) }), Texts = texts });
        Console.WriteLine("Private item/text inventory written; no source files modified.");
    }
    static void Scripts(string root, string output)
    {
        NewEvidence(root,output);
        var project = GameProjectSession.Open(Path.Combine(root, "Proyecto"), false);
        var config = project.GameConfiguration;
        Check(config.XY, "X/Y only");
        int[] candidates = [113,114,115,120,121,122,123,124,125,126,127,128,129,130,131,132,133,426,427,622,623,624,625,626,634,636,637];
        var reports = new List<object>();
        void Read(string name, byte[] script)
        {
            try
            {
                var decoded = Gen6ScriptCodec.Decode(script);
                reports.Add(new { Name=name, Sha256=Hash(script), Decoded=true, WordCount=decoded.Instructions.Length/4,
                    Candidates=candidates.Select(id=>new { Id=id, WordIndices=Enumerable.Range(0,decoded.Instructions.Length/4)
                        .Where(i=>BitConverter.ToInt32(decoded.Instructions,i*4)==id).ToArray() }).Where(hit=>hit.WordIndices.Length>0).ToArray(),
                    Note="Numeric word occurrences only; operand semantics are not inferred." });
            }
            catch (InvalidDataException error) { reports.Add(new { Name=name, Sha256=Hash(script), Decoded=false, Error=error.Message }); }
        }
        var common = new GARC.LazyGARC(File.ReadAllBytes(Path.Combine(config.RomFS,"a/0/3/1")));
        for(int i=0;i<common.FileCount;i++) Read($"common/{i}", common[i]);
        var zones=config.GetlzGARCData("encdata");
        for(int i=0;i<zones.FileCount;i++)
        {
            byte[] raw=zones[i];
            if(raw.Length<2 || raw[0]!='Z' || raw[1]!='O')
            {
                reports.Add(new { Name=$"zone/{i}", Sha256=Hash(raw), Decoded=false, Error="Not a ZO container", Size=raw.Length });
                continue;
            }
            var zone=new Gen6Zone(raw);
            Read($"zone/{i}/script",zone.Pack.Part(2).ToArray());
            var entities=zone.Pack.Part(1);
            int scriptAt=checked(BitConverter.ToInt32(entities[..4])+4);
            Check(scriptAt<=entities.Length,"Entities script out of bounds");
            if(scriptAt<entities.Length) Read($"zone/{i}/entities-script",entities[scriptAt..].ToArray());
        }
        Save(output,new { Purpose="Read-only script inventory; absence is not proof of a free item slot", Reports=reports });
        Console.WriteLine($"Read-only script inventory: {reports.Count} envelopes.");
    }
    static void References(string root,string output)
    {
        NewEvidence(root,output);
        var project=GameProjectSession.Open(Path.Combine(root,"Proyecto"),false);
        Check(project.GameConfiguration.XY,"X/Y only");
        var marts=new GameMart6Service(project);
        var field=new GameFieldItems6Service(project);
        var pickup=new GamePickupService(project);
        var rows=new List<(string Source,int Item)>();
        foreach(var entry in marts.Entries)
            rows.AddRange(marts.Read(entry.Id).SelectMany(g=>g.Fields).Select(f=>($"mart/{entry.Id}/{f.Key}",f.Value)));
        foreach(var entry in field.Entries)
            rows.AddRange(field.Read(entry.Id).SelectMany(g=>g.Fields).Where(f=>f.Key=="Item").Select(f=>($"field/{entry.Id}",f.Value)));
        rows.AddRange(pickup.Read().Select((r,i)=>($"pickup/{i}",r.Item)));
        int[] candidates=[113,114,115,120,121,122,123,124,125,126,127,128,129,130,131,132,133,426,427,622,623];
        Save(output,new { Purpose="Typed references read by existing Gen6 editors; not complete script/native coverage",
            MartCount=marts.Entries.Count, FieldCount=field.Entries.Count, PickupCount=pickup.Read().Count,
            Candidates=candidates.Select(id=>new{Id=id,References=rows.Where(r=>r.Item==id).Select(r=>r.Source).ToArray()}) });
        Console.WriteLine($"Typed references: {marts.Entries.Count} marts, {field.Entries.Count} field slots, {pickup.Read().Count} Pickup rows.");
    }
    static void Plan(string root,string output,bool install)
    {
        NewEvidence(root,output);
        var project=GameProjectSession.Open(Path.Combine(root,"Proyecto"),false);
        var service=new Gen6NatureMintService(project,allowExperimental:true);
        var preview=service.Inspect();
        Check(preview.State==Gen6MintNativeState.Original&&preview.Proposal is not null,preview.Report);
        var proposal=preview.Proposal!;
        string bufferDirectory=output+".buffers";
        Check(!Directory.Exists(bufferDirectory)&&!File.Exists(bufferDirectory),"Buffer output must be new");
        Directory.CreateDirectory(bufferDirectory);
        var changedBag=proposal.Files.Single(f=>Path.GetFileName(f.Path)=="DllBag.cro");
        using(var stream=new FileStream(Path.Combine(bufferDirectory,"DllBag.cro"),FileMode.CreateNew))stream.Write(changedBag.Proposed());
        if(install)service.Apply(preview);
        Save(output,new {State=install?service.Inspect().State.ToString():preview.State.ToString(),preview.Report,
            ProductionEnabled=false,Installed=install,Files=proposal.Files.Select(f=>new {Path=Path.GetRelativePath(project.ProjectRoot,f.Path),Before=Hash(f.Original()),After=Hash(f.Proposed())}),
            PrivateNativeBuffer=Path.Combine(bufferDirectory,"DllBag.cro")});
        Console.WriteLine(install?"Experimental install verified and recorded in isolated project history.":"Detached experimental preview; no project files modified.");
    }
    static Dictionary<string,string> Inventory(string project)=>new[]{"RomFS","ExeFS"}.SelectMany(part=>
        Directory.EnumerateFiles(Path.Combine(project,part),"*",SearchOption.AllDirectories)).ToDictionary(
            path=>Path.GetRelativePath(project,path).Replace('\\','/'),path=>{NoLinks(path);return Hash(File.ReadAllBytes(path));},StringComparer.Ordinal);
    static void UndoCheck(string root,string output)
    {
        NewEvidence(root,output);string folder=Path.Combine(root,"Proyecto");var project=GameProjectSession.Open(folder,false);
        var service=new Gen6NatureMintService(project,true);Check(service.Inspect().State==Gen6MintNativeState.Installed,"Installed candidate required");
        var before=Inventory(folder);var entry=project.History.Read().Entries.Last();
        Check(entry.Parameters.GetValueOrDefault("ActionId")==Gen6NatureMintService.ActionId,"Newest change must be the mint installation");
        RestorePoints.Undo(project.History,folder,entry.Id);
        var original=new Gen6NatureMintService(GameProjectSession.Open(folder,false),true);
        var preview=original.Inspect();Check(preview.State==Gen6MintNativeState.Original&&preview.Proposal is not null,"Undo restored accredited originals");
        original.Apply(preview);Check(Inventory(folder).OrderBy(p=>p.Key).SequenceEqual(before.OrderBy(p=>p.Key)),"Reinstall restores exact candidate files");
        Check(new Gen6NatureMintService(GameProjectSession.Open(folder,false)).Inspect().State==Gen6MintNativeState.Installed,"Full project reopen detects candidate");
        Save(output,new {ExactUndo=true,ExactReinstall=true,Reopened=true,Files=before.Count,ProductionEnabled=false});
    }
    static void Shops(string root,string output)
    {
        NewEvidence(root,output);var project=GameProjectSession.Open(Path.Combine(root,"Proyecto"),false);
        Check(new Gen6NatureMintService(project).Inspect().State==Gen6MintNativeState.Installed,"Verified installation required before test distribution");
        var marts=new GameMart6Service(project);Check(marts.SupportsExpansion,"Original Gen6 shop expansion must be supported: "+marts.ExpansionReason);
        int[][] before=marts.ReadAllItems(),rows=before.Select(r=>(int[])r.Clone()).ToArray();
        for(int i=0;i<9;i++)rows[i]=rows[i].Concat(Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).Where(id=>!rows[i].Contains(id))).ToArray();
        var options=EditorValues.Flatten(marts.RandomOptions);options["Randomize"]=0;options["Candies"]=0;options["EVItems"]=0;options["BanCapsule"]=0;
        var proposal=marts.PrepareDraft(rows,options,Enumerable.Range(1,9).ToArray(),0);marts.Apply(proposal);
        var after=new GameMart6Service(GameProjectSession.Open(Path.Combine(root,"Proyecto"),false)).ReadAllItems();
        Check(after.Where((row,i)=>!row.SequenceEqual(rows[i])).Count()==0,"Test inventories reopen exactly");
        Save(output,new {CandidateDistributionOnly=true,GeneralStocks=9,Capacity=marts.Capacity,
            Stocks=after.Select((r,i)=>new{Id=i+1,Count=r.Length,MintIds=r.Where(id=>Gen6NatureMintCatalog.ExperimentalItemMap.Items.Any(m=>m.ItemId==id))}),
            ProductionEnabled=false,Note="Explicit test distribution; production policy remains disabled. Undo this shop action before undoing mint installation."});
    }
    static void RoundTrip(string root,string output)
    {
        NewEvidence(root,output);string folder=Path.Combine(root,"Proyecto"),image=Path.Combine(root,"Pokemon-X-XY-MINT-EXPERIMENTAL.3ds"),reextracted=Path.Combine(root,"Reextraido-XYMint");
        Check(Target(root)==0x0004000000055D00,"First gameplay candidate is X only");
        Check(!File.Exists(image)&&!Directory.Exists(reextracted),"Rebuild outputs must be new");
        var expected=Inventory(folder);var project=GameProjectSession.Open(folder,false);
        var result=new GameRomRebuildService(project).BuildAsync(RomRebuildKind.Trimmed3DS,image).GetAwaiter().GetResult();
        new GameRomExtractionService(folder).ExtractAsync(image,reextracted).GetAwaiter().GetResult();
        var actual=Inventory(reextracted);
        Check(actual.OrderBy(p=>p.Key).SequenceEqual(expected.OrderBy(p=>p.Key)),"Every rebuilt/reextracted RomFS and ExeFS file matches");
        Check(Inventory(folder).OrderBy(p=>p.Key).SequenceEqual(expected.OrderBy(p=>p.Key)),"Rebuild did not alter source project");
        Check(Directory.GetFiles(Path.Combine(reextracted,"RomFS")).Select(Path.GetFileName).Count(n=>n=="DllBag.cro")==1,"Physical DllBag.cro name preserved");
        Save(output,new {result.File,result.Hash,result.Bytes,AllFilesExact=true,FileCount=actual.Count,ProductionEnabled=false,GameplayVerified=false});
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "install-icons")
            {
                string root=Path.GetFullPath(args[1]);NewEvidence(root,args[2]);
                var project=GameProjectSession.Open(Path.Combine(root,"Proyecto"),false);var service=new Gen6MintIconService(project);
                var before=Inventory(project.ProjectRoot);var preview=service.PrepareAsync().GetAwaiter().GetResult();
                Check(preview.Proposal is not null,"New icon proposal required: "+preview.Report);service.Apply(preview);
                Check(service.Inspect().State==Gen6MintNativeState.Installed,"Icon installation reopens");var installed=Inventory(project.ProjectRoot);
                var entry=project.History.Read().Entries.Last();Check(entry.Parameters["ActionId"]==Gen6MintIconService.ActionId,"Icon action is newest");
                RestorePoints.Undo(project.History,project.ProjectRoot,entry.Id);
                Check(Inventory(project.ProjectRoot).OrderBy(p=>p.Key).SequenceEqual(before.OrderBy(p=>p.Key)),"Icon undo preserves every original candidate file");
                var next=new Gen6MintIconService(GameProjectSession.Open(project.ProjectRoot,false));var proposal=next.PrepareAsync().GetAwaiter().GetResult();next.Apply(proposal);
                Check(Inventory(project.ProjectRoot).OrderBy(p=>p.Key).SequenceEqual(installed.OrderBy(p=>p.Key)),"Icon reinstall exact");
                Save(args[2],new{Installed=true,UndoExact=true,ReinstallExact=true,FilesChanged=preview.Proposal!.Files.Select(f=>Path.GetRelativePath(project.ProjectRoot,f.Path)),GameplayVerified=false});
            }
            else if (args.Length == 3 && args[0] == "icon-textures")
            {
                string root=Path.GetFullPath(args[1]);NewEvidence(root,args[2]);
                var textures=UsumMintIconDownload.LoadAsync(UsumMintIconDownload.DefaultCache).GetAwaiter().GetResult();
                byte[] archive=File.ReadAllBytes(Path.Combine(root,"Proyecto/RomFS/a/0/9/4"));
                Save(args[2],new{TextureSha256=textures.Select(t=>Hash(Gen6MintIconService.ConvertTexture(t))),
                    CanonicalOriginalArchive=Gen6MintIconService.Pack(new GARC.MemGARC(archive).Files).AsSpan().SequenceEqual(archive)});
            }
            else if (args.Length == 3 && args[0] == "icon-probe")
            {
                string root=Path.GetFullPath(args[1]);NewEvidence(root,args[2]);
                string folder=Path.Combine(root,"Proyecto");var p=GameProjectSession.Open(folder,false);
                byte[] archive=File.ReadAllBytes(Path.Combine(folder,"RomFS/a/0/9/4"));var rows=new GARC.MemGARC(archive).Files;
                byte[] code=File.ReadAllBytes(Path.Combine(folder,"ExeFS/.code.bin"));
                var observations=new List<object>();
                foreach(var m in Gen6NatureMintCatalog.ExperimentalItemMap.Items)
                {
                    int image=BitConverter.ToInt32(code,0x43db74+m.ItemId*4);using var stream=new MemoryStream();
                    LZSS.Decompress(new MemoryStream(rows[image]),rows[image].Length,stream);byte[] raw=stream.ToArray();
                    var tex=new BCLIM(raw);using var decoded=GameGraphicsCodec.Decode(raw);
                    observations.Add(new{m.ItemId,image,raw.Length,tex.Valid,tex.Width,tex.Height,Format=tex.Format.ToString(),Orientation=tex.Orientation.ToString(),Footer=Convert.ToHexString(raw[^40..])});
                }
                Save(args[2],new{Title=Target(root),Count=rows.Length,Version=BitConverter.ToUInt16(archive,10),CodeSha256=Hash(code),Observations=observations});
            }
            else if (args.Length == 3 && args[0] == "extract") Extract(args[1], args[2]);
            else if (args.Length == 3 && args[0] == "inspect") Inspect(args[1], args[2]);
            else if (args.Length == 3 && args[0] == "scripts") Scripts(args[1], args[2]);
            else if (args.Length == 3 && args[0] == "references") References(args[1], args[2]);
            else if (args.Length == 3 && args[0] == "plan") Plan(args[1],args[2],false);
            else if (args.Length == 3 && args[0] == "install-experiment") Plan(args[1],args[2],true);
            else if (args.Length == 3 && args[0] == "verify-undo") UndoCheck(args[1],args[2]);
            else if (args.Length == 3 && args[0] == "prepare-test-shops") Shops(args[1],args[2]);
            else if (args.Length == 3 && args[0] == "rebuild-reextract") RoundTrip(args[1],args[2]);
            else { Console.Error.WriteLine("extract SOURCE_ROM NEW_PruebasMentasXY_ROOT | inspect/scripts/references/plan/install-experiment/verify-undo/prepare-test-shops/rebuild-reextract/icon-probe/icon-textures/install-icons ISOLATED_ROOT NEW_PRIVATE_JSON"); return 2; }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
