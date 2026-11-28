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
        byte[] crr=File.ReadAllBytes(Path.Combine(root,"Proyecto/RomFS/.crr/static.crr"));
        Save(Path.Combine(root, "evidence/source.json"), new { Purpose, Rom = Path.GetFullPath(rom), result.SourceSha256,
            TitleId=Target(root).ToString("X16"),OriginalFiles=Inventory(Path.Combine(root,"Proyecto")),OriginalCrrFixedSha256=CrrFixedHash(crr) });
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
    static string CrrFixedHash(byte[] bytes)
    {
        Check(bytes.Length>=0x358,"CRR header truncated");int at=BitConverter.ToInt32(bytes,0x350),count=BitConverter.ToInt32(bytes,0x354);
        Check(at>=0x358&&count>0&&(long)at+32L*count<=bytes.Length,"Invalid CRR hash table");
        return Hash([..bytes.AsSpan(0,at),..bytes.AsSpan(at+32*count)]);
    }
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
        int[] mintIds=Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).ToArray();
        Check(after.Take(9).All(row=>mintIds.All(id=>row.Count(value=>value==id)==1)),"All 21 mints occur exactly once in each general stock");
        Check(after.Skip(9).Where((row,i)=>!row.SequenceEqual(before[i+9])).Count()==0,"Special stocks preserved");
        var codeChange=proposal.Files.Single(f=>Path.GetFileName(f.Path) is ".code.bin" or "code.bin");
        Save(output,new {CandidateDistributionOnly=true,TitleId=Target(root).ToString("X16"),BeforeCodeSha256=Hash(codeChange.Original()),AfterCodeSha256=Hash(codeChange.Proposed()),
            All21ExactlyOnce=true,SpecialStocksPreserved=true,HistoryEntry=project.History.Read().Entries.Last().Id,GeneralStocks=9,Capacity=marts.Capacity,
            Stocks=after.Select((r,i)=>new{Id=i+1,Count=r.Length,MintIds=r.Where(id=>Gen6NatureMintCatalog.ExperimentalItemMap.Items.Any(m=>m.ItemId==id))}),
            ProductionEnabled=false,Note="Explicit test distribution; production policy remains disabled. Undo this shop action before undoing mint installation."});
    }
    static void RoundTrip(string root,string output)
    {
        NewEvidence(root,output);string game=Target(root)==0x0004000000055D00?"X":"Y";
        string folder=Path.Combine(root,"Proyecto"),image=Path.Combine(root,$"Pokemon-{game}-XY-MINT-EXPERIMENTAL.3ds"),reextracted=Path.Combine(root,"Reextraido-XYMint");
        Check(!File.Exists(image)&&!Directory.Exists(reextracted),"Rebuild outputs must be new");
        var expected=Inventory(folder);var project=GameProjectSession.Open(folder,false);
        var result=new GameRomRebuildService(project).BuildAsync(RomRebuildKind.Trimmed3DS,image).GetAwaiter().GetResult();
        new GameRomExtractionService(folder).ExtractAsync(image,reextracted).GetAwaiter().GetResult();
        var actual=Inventory(reextracted);
        Check(actual.OrderBy(p=>p.Key).SequenceEqual(expected.OrderBy(p=>p.Key)),"Every rebuilt/reextracted RomFS and ExeFS file matches");
        Check(Inventory(folder).OrderBy(p=>p.Key).SequenceEqual(expected.OrderBy(p=>p.Key)),"Rebuild did not alter source project");
        Check(Directory.GetFiles(Path.Combine(reextracted,"RomFS")).Select(Path.GetFileName).Count(n=>n=="DllBag.cro")==1,"Physical DllBag.cro name preserved");
        Headers(root);
        byte[] rebuiltHeader=File.ReadAllBytes(Path.Combine(reextracted,"ncchheader.bin"));
        Check(BitConverter.ToUInt64(rebuiltHeader,0x118)==Target(root),"Rebuilt Title ID matches independent title");
        Save(output,new {result.File,result.Hash,result.Bytes,TitleId=Target(root).ToString("X16"),AllFilesExact=true,FileCount=actual.Count,ProductionEnabled=false,GameplayVerified=false});
    }
    static void VerifyCandidate(string root,string output,bool fullCycle)
    {
        NewEvidence(root,output);string folder=Path.Combine(root,"Proyecto");var project=GameProjectSession.Open(folder,false);
        Check(new Gen6NatureMintService(project).Inspect().State==Gen6MintNativeState.Installed,"Native title-specific installation required");
        Check(new Gen6MintIconService(project).Inspect().State==Gen6MintNativeState.Installed,"Corrected icons required");
        var inventory=Inventory(folder);var original=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"evidence/source.json"))).RootElement;
        var baseline=original.GetProperty("OriginalFiles").Deserialize<Dictionary<string,string>>()!;
        Check(original.GetProperty("TitleId").GetString()==Target(root).ToString("X16"),"Original manifest title mismatch");
        var cros=Directory.GetFiles(project.GameConfiguration.RomFS).Where(p=>Path.GetExtension(p).Equals(".cro",StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach(string name in new[]{"DllBag.cro","DllBattle.cro"})Check(cros.Count(p=>Path.GetFileName(p)==name)==1,"Physical CRO filename preserved: "+name);
        var croData=cros.Select(File.ReadAllBytes).ToArray();
        Check(croData.All(bytes=>CroIntegrity.Rebuild(bytes).AsSpan().SequenceEqual(bytes)),"All CRO headers, segments and hashes consistent");
        byte[] crr=File.ReadAllBytes(Path.Combine(folder,"RomFS/.crr/static.crr"));
        Check(CroIntegrity.RebuildCrr(crr,croData).AsSpan().SequenceEqual(crr),"CRR matches complete CRO inventory");
        Check(CrrFixedHash(crr)==original.GetProperty("OriginalCrrFixedSha256").GetString(),"CRR signatures and bytes outside hash table preserved");
        var stocks=new GameMart6Service(project).ReadAllItems();int[] ids=Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).ToArray();
        Check(stocks.Take(9).All(row=>ids.All(id=>row.Count(value=>value==id)==1)),"All 21 mints distributed once per general stock");
        bool nativeIdempotent=new Gen6NatureMintService(project,true).Inspect().Proposal is null;
        bool iconsIdempotent=new Gen6MintIconService(project).PrepareAsync().GetAwaiter().GetResult().Proposal is null;
        Check(nativeIdempotent&&iconsIdempotent&&Inventory(folder).OrderBy(p=>p.Key).SequenceEqual(inventory.OrderBy(p=>p.Key)),"Idempotent reads do not write");
        if(fullCycle)
        {
            var entries=project.History.Read().Entries;
            var undo=new[]{entries.Last(e=>e.Parameters.GetValueOrDefault("ActionId")==Gen6MintIconService.ActionId),
                entries.Last(e=>e.Module=="Tiendas · Gen6"&&e.Action=="Tiendas Gen6 y presets"),
                entries.Last(e=>e.Parameters.GetValueOrDefault("ActionId")==Gen6NatureMintService.ActionId)};
            foreach(var entry in undo)Check(RestorePoints.Find(project.History.StorageDirectory,entry.Id) is not null,"Exact restore point required");
            foreach(var entry in undo)RestorePoints.Undo(project.History,folder,entry.Id);
            Check(Inventory(folder).OrderBy(p=>p.Key).SequenceEqual(baseline.OrderBy(p=>p.Key)),"Full uninstall restores every original RomFS/ExeFS byte");
            var clean=GameProjectSession.Open(folder,false);var service=new Gen6NatureMintService(clean,true);var preview=service.Inspect();
            Check(preview.State==Gen6MintNativeState.Original,"Original state recognized after uninstall");service.Apply(preview);
            var marts=new GameMart6Service(clean);var rows=marts.ReadAllItems();for(int i=0;i<9;i++)rows[i]=rows[i].Concat(ids.Where(id=>!rows[i].Contains(id))).ToArray();
            var options=EditorValues.Flatten(marts.RandomOptions);options["Randomize"]=0;options["Candies"]=0;options["EVItems"]=0;options["BanCapsule"]=0;
            marts.Apply(marts.PrepareDraft(rows,options,Enumerable.Range(1,9).ToArray(),0));
            var icons=new Gen6MintIconService(GameProjectSession.Open(folder,false));icons.Apply(icons.PrepareAsync().GetAwaiter().GetResult());
            Check(Inventory(folder).OrderBy(p=>p.Key).SequenceEqual(inventory.OrderBy(p=>p.Key)),"Full reinstall restores exact final candidate");
            Check(new Gen6NatureMintService(GameProjectSession.Open(folder,false)).Inspect().State==Gen6MintNativeState.Installed,"Native installation recognized after reopen");
        }
        Save(output,new{TitleId=Target(root).ToString("X16"),NativeInstalled=true,IconsInstalled=true,CroCount=cros.Length,AllCroHashesValid=true,
            CrrInventoryMatches=true,CrrOutsideHashTablePreserved=true,GeneralStocks=9,All21ExactlyOnce=true,NativeIdempotent=nativeIdempotent,IconsIdempotent=iconsIdempotent,
            FullUninstallExact=fullCycle,FullReinstallExact=fullCycle,Files=inventory.Count,ProductionEnabled=false,GameplayVerified=false});
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if(args.Length==4&&args[0]=="randomized-install-probe")
            {
                string root=Path.GetFullPath(args[1]);NewEvidence(root,args[3]);var p=GameProjectSession.Open(Path.Combine(root,"Proyecto"),false);
                var doc=GlobalTemplateDocument.Load(args[2],"XY",6);Check(!Gen6MintRecipeBridge.Selected(doc),"Use the original randomization recipe without mint installation");
                using var preview=Task.Run(()=>new GlobalTemplateApplication(p).PreviewAsync(doc,Console.WriteLine)).GetAwaiter().GetResult();
                string stage=Path.Combine(preview.Workspace,"preview-project");var project=GameProjectSession.Open(stage,false);var randomized=Inventory(stage);
                var installer=new Gen6NatureMintService(project,true);var native=installer.Inspect();Check(native.State==Gen6MintNativeState.Original&&native.Proposal is not null,"Randomized data admitted with native code verified: "+native.Report);installer.Apply(native);
                Guid nativeEntry=project.History.Read().Entries.Last().Id;
                var icons=new Gen6MintIconService(project);var image=Task.Run(()=>icons.PrepareAsync()).GetAwaiter().GetResult();Check(image.Proposal is not null,"Icons on randomized data");icons.Apply(image);
                _=Gen6MintRuntimeDependencies.Capture(project);Guid iconEntry=project.History.Read().Entries.Last().Id;
                RestorePoints.Undo(project.History,stage,iconEntry);RestorePoints.Undo(project.History,stage,nativeEntry);
                Check(Inventory(stage).OrderBy(p=>p.Key).SequenceEqual(randomized.OrderBy(p=>p.Key)),"Undo restores exact randomized resources");
                installer=new Gen6NatureMintService(GameProjectSession.Open(stage,false),true);installer.Apply(installer.Inspect());
                icons=new Gen6MintIconService(GameProjectSession.Open(stage,false));icons.Apply(Task.Run(()=>icons.PrepareAsync()).GetAwaiter().GetResult());
                _=Gen6MintRuntimeDependencies.Capture(GameProjectSession.Open(stage,false));
                Save(args[3],new{TitleId=Target(root).ToString("X16"),RandomizedInput=true,NativeInstalled=true,IconsInstalled=true,AllCroCrrValid=true,UndoExactRandomizedBytes=true,ReinstallVerified=true,Files=randomized.Count,GameplayVerified=false});
            }
            else if(args.Length==3&&args[0]=="compare-garc")
            {
                NoLinks(args[1]);NoLinks(args[2]);var a=new GARC.MemGARC(File.ReadAllBytes(args[1])).Files;var b=new GARC.MemGARC(File.ReadAllBytes(args[2])).Files;
                Check(a.Length==b.Length,"Record count differs");var changes=Enumerable.Range(0,a.Length).Where(i=>!a[i].SequenceEqual(b[i])).ToArray();
                Console.WriteLine("Different records: "+changes.Length+" / "+a.Length);
                foreach(int i in changes.Take(12))Console.WriteLine(i+": lengths "+a[i].Length+" / "+b[i].Length+"; offsets "+string.Join(",",Enumerable.Range(0,Math.Min(a[i].Length,b[i].Length)).Where(n=>a[i][n]!=b[i][n]).Take(40)));
            }
            else if(args.Length==3&&args[0]=="runtime-code-guards")
            {
                string root=Path.GetFullPath(args[1]);NewEvidence(root,args[2]);var p=GameProjectSession.Open(Path.Combine(root,"Proyecto"),false);
                var shape=Gen6NatureMintService.Profile(Target(root)).GetProperty("native").GetProperty("runtime").GetProperty("code");
                byte[] code=File.ReadAllBytes(Path.Combine(p.GameConfiguration.ExeFS,".code.bin"));byte[] before=SHA256.HashData(code);
                Check(Hash(Gen6MintRuntimeDependencies.NormalizeCode(p,code,shape))==shape.GetProperty("sha256").GetString(),"Known code shape");
                var instruction=(byte[])code.Clone();instruction[0x100]^=1;
                Check(Hash(Gen6MintRuntimeDependencies.NormalizeCode(p,instruction,shape))!=shape.GetProperty("sha256").GetString(),"Unknown native instruction cannot hide as data");
                void Reject(byte[] candidate,string reason){try{_=Gen6MintRuntimeDependencies.NormalizeCode(p,candidate,shape);}catch(InvalidDataException){return;}throw new InvalidDataException(reason);}
                var invalidTm=(byte[])code.Clone();BitConverter.GetBytes(ushort.MaxValue).CopyTo(invalidTm,shape.GetProperty("tm").GetInt32());Reject(invalidTm,"Invalid TM accepted");
                int stock=shape.GetProperty("stock").GetInt32();var table=new Gen6MartTable(p,code,stock,GameMart6Service.CountsXY);
                var stocks=new GameMart6Service(p).ReadAllItems();stocks[0]=stocks[0].Append(113).ToArray();
                byte[] forged=table.Build(code,stocks);var ranges=shape.GetProperty("mart_ranges");
                int pointer=ranges[2][0].GetInt32(),cave=ranges[3][0].GetInt32(),end=cave+ranges[3][1].GetInt32(),counts=ranges[1][0].GetInt32();
                int alternative=Enumerable.Range(end/4,(forged.Length-end)/4).Select(i=>i*4).First(at=>at!=pointer&&BitConverter.ToUInt32(forged,at)==0);
                uint address=BitConverter.ToUInt32(forged,cave+40);BitConverter.GetBytes(0).CopyTo(forged,pointer);BitConverter.GetBytes(address).CopyTo(forged,alternative);BitConverter.GetBytes(alternative).CopyTo(forged,cave+24);
                byte[] digest=SHA256.HashData([..forged.AsSpan(cave,48),..forged.AsSpan(counts,26),..forged.AsSpan(cave+64,end-cave-64)]);digest.AsSpan(0,16).CopyTo(forged.AsSpan(cave+48));
                _=new Gen6MartTable(p,forged,stock,GameMart6Service.CountsXY); // internally consistent descriptor, wrong native pointer location
                Reject(forged,"Forged descriptor moved the pinned native pointer");
                Check(Hash(File.ReadAllBytes(Path.Combine(p.GameConfiguration.ExeFS,".code.bin")))==Convert.ToHexString(before),"Source code unchanged");
                Save(args[2],new{TitleId=Target(root).ToString("X16"),KnownShapeValid=true,UnknownInstructionRejected=true,InvalidTmRejected=true,ForgedPointerRejected=true,SourceUnchanged=true,InMemoryOnly=true});
            }
            else if(args.Length==3&&args[0]=="runtime-validate")
            {
                string root=Path.GetFullPath(args[1]);NewEvidence(root,args[2]);var p=GameProjectSession.Open(Path.Combine(root,"Proyecto"),false);var evidence=Gen6MintRuntimeDependencies.Capture(p);
                Save(args[2],new{TitleId=evidence.TitleId.ToString("X16"),ValidatedDependencies=evidence.Dependencies.Count,RuntimeValid=true,ReadOnly=true});
            }
            else if(args.Length==5&&args[0]=="batch-global")
            {
                string root=Path.GetFullPath(args[1]),recipe=Path.GetFullPath(args[2]),caseRoot=Path.GetFullPath(args[4]);Root(root);Headers(root);NoLinks(recipe);
                Check(int.TryParse(args[3],out int seed)&&seed>=0,"Explicit nonnegative seed required");
                Check(Path.GetDirectoryName(caseRoot)==Path.Combine(root,"evidence")&&!Directory.Exists(caseRoot)&&!File.Exists(caseRoot),"New isolated case folder required");
                var project=GameProjectSession.Open(Path.Combine(root,"Proyecto"),false);var before=GlobalTemplateApplication.Snapshot(project);
                var doc=GlobalTemplateDocument.Load(recipe,"XY",6);var actions=(System.Text.Json.Nodes.JsonArray)doc.Section("Actions")!;
                Check(!doc.Actions.Contains("rng.seed",StringComparer.OrdinalIgnoreCase),"Probe adds one fixed seed");
                actions.Add(new System.Text.Json.Nodes.JsonObject{{"Id","rng.seed"},{"Parameters",new System.Text.Json.Nodes.JsonObject{{"value",seed.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}});doc.SetSection("Actions",actions);
                Directory.CreateDirectory(Path.Combine(caseRoot,"custom_balance_templates"));
                foreach(var row in (System.Text.Json.Nodes.JsonArray)doc.Section("Assets")!)
                {
                    string name=row!["FileName"]!.GetValue<string>();string source=Path.Combine(Path.GetDirectoryName(recipe)!,"custom_balance_templates",name);NoLinks(source);byte[] bytes=File.ReadAllBytes(source);
                    Check(Hash(bytes)==row["Sha256"]!.GetValue<string>(),"CSV changed");using var stream=new FileStream(Path.Combine(caseRoot,"custom_balance_templates",name),FileMode.CreateNew);stream.Write(bytes);
                }
                string copy=Path.Combine(caseRoot,"PokeJuz_XY_Batch.json");doc.Save(copy);doc=GlobalTemplateDocument.Load(copy,"XY",6);
                var options=new RomBatchOptions(copy,Path.Combine(caseRoot,"batch"),"PokeJuz-XY",2,true,false,true);
                var journal=Task.Run(()=>new BatchRomExecution(project).RunAsync(options,p=>Console.WriteLine($"{p.CurrentRom}/{p.TotalRoms} {p.Stage}: {p.Detail}"))).GetAwaiter().GetResult();
                var reports=new List<object>();
                foreach(var rom in journal.Roms)
                {
                    Check(rom.Seed==Gen6MintExecutionContract.SeedForRom(doc.ToJson(),rom.Index-1,0),"Batch records requested deterministic seed sequence");
                    string editable=Path.GetFullPath(Path.Combine(options.OutputDirectory,$"PokeJuz-XY_{rom.Index:00}_Project"));
                    Check(Path.GetDirectoryName(editable)==Path.GetFullPath(options.OutputDirectory),"Owned editable staging boundary");NoLinks(editable);
                    var staged=GameProjectSession.Open(editable,false);var installed=Gen6MintRuntimeDependencies.Capture(staged);var expected=Inventory(editable);
                    string verified=Path.GetFullPath(Path.Combine(caseRoot,"verify-"+rom.Index));
                    Check(Path.GetDirectoryName(verified)==caseRoot&&!Directory.Exists(verified),"Owned fresh verification folder");
                    Task.Run(()=>new GameRomExtractionService(project.ProjectRoot).ExtractAsync(rom.File,verified)).GetAwaiter().GetResult();
                    var reopened=GameProjectSession.Open(verified,false);
                    Check(installed.TitleId==Target(root),"Independent title after Batch rebuild");
                    byte[] originalHeader=File.ReadAllBytes(Path.Combine(editable,"ncchheader.bin")),rebuiltHeader=File.ReadAllBytes(Path.Combine(verified,"ncchheader.bin"));
                    Check(BitConverter.ToUInt64(rebuiltHeader,0x118)==Target(root)&&rebuiltHeader.AsSpan(0,256).ToArray().All(b=>b==0)&&rebuiltHeader[0x18f]==4,"Known decoded rebuilt header and title");
                    foreach(var (offset,length) in new[]{(0,256),(0x104,4),(0x18f,1),(0x1a4,4),(0x1b0,8),(0x1c0,64)}){originalHeader.AsSpan(offset,length).Clear();rebuiltHeader.AsSpan(offset,length).Clear();}
                    Check(originalHeader.SequenceEqual(rebuiltHeader)&&File.ReadAllBytes(Path.Combine(editable,"exheader.bin")).SequenceEqual(File.ReadAllBytes(Path.Combine(verified,"exheader.bin"))),"Rebuild preserves immutable title metadata and exact ExHeader");
                    var actual=Inventory(verified);Check(actual.OrderBy(p=>p.Key).SequenceEqual(expected.OrderBy(p=>p.Key)),"All reextracted runtime resources match validated staging");installed.VerifyUnchanged();
                    using(var extraction=JsonDocument.Parse(File.ReadAllText(Path.Combine(verified,"extraction-manifest.json"))))Check(extraction.RootElement.GetProperty("SourceSha256").GetString()==rom.Sha256,"Reextracted container matches Batch output SHA");
                    Check(Gen6PersistentItemsPatcher.Inspect(File.ReadAllBytes(Path.Combine(reopened.GameConfiguration.RomFS,"DllBattle.cro")))==Gen6PersistentItemsPatcher.State.Installed,"PI remains installed after Batch rebuild");
                    reports.Add(new{rom.Index,rom.Seed,rom.Sha256,rom.File,Files=actual,RuntimeDependenciesValid=true,PersistentItemsInstalled=true,ReextractedResourcesExact=true,ImmutableIdentityPreserved=true,OriginalExheaderExact=true});
                    // Only this fresh child, whose absolute boundary was checked above.
                    Directory.Delete(verified,true);
                    Directory.Delete(editable,true);
                }
                Check(GlobalTemplateApplication.Snapshot(project).OrderBy(p=>p.Key).SequenceEqual(before.OrderBy(p=>p.Key)),"Clean source unchanged after Batch");
                Save(Path.Combine(caseRoot,"result.json"),new{TitleId=Target(root).ToString("X16"),SourceUnchanged=true,Reports=reports,GameplayVerified=false});
            }
            else if(args.Length is 5 or 6&&args[0]=="preview-global")
            {
                string root=Path.GetFullPath(args[1]),recipe=Path.GetFullPath(args[2]),caseRoot=Path.GetFullPath(args[4]);Root(root);Headers(root);NoLinks(recipe);
                Check(int.TryParse(args[3],out int seed)&&seed>=0,"Explicit nonnegative seed required");
                Check(Path.GetDirectoryName(caseRoot)==Path.Combine(root,"evidence")&&!Directory.Exists(caseRoot)&&!File.Exists(caseRoot),"New isolated case folder required");
                var project=GameProjectSession.Open(Path.Combine(root,"Proyecto"),false);
                if(args.Length==6){Check(int.TryParse(args[5],out int language)&&language is >=0 and <=7,"Explicit XY language");project.GameConfiguration.Initialize(project.GameConfiguration.RomFS,project.GameConfiguration.ExeFS,language);}
                var before=GlobalTemplateApplication.Snapshot(project);
                var doc=GlobalTemplateDocument.Load(recipe,"XY",6);var actions=(System.Text.Json.Nodes.JsonArray)doc.Section("Actions")!;
                Check(!doc.Actions.Contains("rng.seed",StringComparer.OrdinalIgnoreCase),"Use a source without a fixed seed for this multi-seed probe");
                actions.Add(new System.Text.Json.Nodes.JsonObject{{"Id","rng.seed"},{"Parameters",new System.Text.Json.Nodes.JsonObject{{"value",seed.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}});doc.SetSection("Actions",actions);
                Directory.CreateDirectory(Path.Combine(caseRoot,"custom_balance_templates"));
                foreach(var row in (System.Text.Json.Nodes.JsonArray)doc.Section("Assets")!)
                {
                    string name=row!["FileName"]!.GetValue<string>();string source=Path.Combine(Path.GetDirectoryName(recipe)!,"custom_balance_templates",name);NoLinks(source);byte[] bytes=File.ReadAllBytes(source);
                    Check(Hash(bytes)==row["Sha256"]!.GetValue<string>(),"CSV changed");using var stream=new FileStream(Path.Combine(caseRoot,"custom_balance_templates",name),FileMode.CreateNew);stream.Write(bytes);
                }
                string copy=Path.Combine(caseRoot,"PokeJuz_XY_Batch.json");doc.Save(copy);doc=GlobalTemplateDocument.Load(copy,"XY",6);
                using var preview=Task.Run(()=>new GlobalTemplateApplication(project).PreviewAsync(doc,Console.WriteLine)).GetAwaiter().GetResult();
                Check(preview.Seed==seed,"Recorded seed is the requested seed");string stage=Path.Combine(preview.Workspace,"preview-project");var result=GameProjectSession.Open(stage,false);
                _=Gen6MintRuntimeDependencies.Capture(result);Check(Gen6PersistentItemsPatcher.Inspect(File.ReadAllBytes(Path.Combine(result.GameConfiguration.RomFS,"DllBattle.cro")))==Gen6PersistentItemsPatcher.State.Installed,"PI remains installed");
                int[] mintIds=Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).ToArray();
                var marts=new GameMart6Service(result).ReadAllItems();var fields=new GameFieldItems6Service(result).ExportCsv().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(line=>line.Split(',')).ToArray();
                var flags=pk3DS.Core.Modding.Research.Gen6MintExecutionContract.ReadOptions(doc.ToJson());
                int martsCount=marts.Sum(row=>row.Count(mintIds.Contains)),visibleCount=fields.Count(row=>row[1]=="Visible"&&mintIds.Contains(int.Parse(row[4]))),hiddenCount=fields.Count(row=>row[1]=="Hidden"&&mintIds.Contains(int.Parse(row[4])));
                Check(flags.IncludeMarts||martsCount==0,"Marts OFF must not introduce mints");Check(flags.IncludeFieldItems||visibleCount+hiddenCount==0,"Field OFF must not introduce mints");
                var after=Inventory(stage);Check(GlobalTemplateApplication.Snapshot(project).OrderBy(p=>p.Key).SequenceEqual(before.OrderBy(p=>p.Key)),"Source clean project remained unchanged");
                File.Copy(Path.Combine(result.GameConfiguration.RomFS,result.GameConfiguration.GetGARCFileName("trpoke")),Path.Combine(caseRoot,"trpoke.private.garc"));
                Save(Path.Combine(caseRoot,"result.json"),new{TitleId=Target(root).ToString("X16"),Seed=seed,SourceUnchanged=true,RuntimeDependenciesValid=true,PersistentItemsInstalled=true,
                    flags.IncludeMarts,flags.IncludeFieldItems,MartsMintSlots=martsCount,VisibleMintSlots=visibleCount,HiddenMintSlots=hiddenCount,
                    ChangedFiles=preview.Files.Count,Files=after,RecipeSha256=doc.Sha256,GameplayVerified=false});
            }
            else if(args.Length==4&&args[0]=="prepare-global")
            {
                string root=Path.GetFullPath(args[1]),source=Path.GetFullPath(args[2]),destination=Path.GetFullPath(args[3]);
                string report=Path.Combine(root,"evidence/global-template-result.json");NewEvidence(root,report);NoLinks(source);
                Check(destination==Path.Combine(root,"GlobalTemplate")&&!Directory.Exists(destination)&&!File.Exists(destination),"New isolated GlobalTemplate output required");
                byte[] original=File.ReadAllBytes(source);var doc=GlobalTemplateDocument.Load(source,"XY",6);var assets=(System.Text.Json.Nodes.JsonArray)doc.Section("Assets")!;
                var copies=new List<(string Name,string Source,byte[] Bytes)>();
                foreach(var row in assets)
                {
                    string name=row!["FileName"]!.GetValue<string>(),sha=row["Sha256"]!.GetValue<string>();
                    string asset=Path.Combine(Path.GetDirectoryName(source)!,"custom_balance_templates",name);NoLinks(asset);byte[] data=File.ReadAllBytes(asset);
                    Check(Hash(data)==sha,"Original custom CSV fingerprint mismatch: "+name);copies.Add((name,asset,data));
                }
                Gen6MintRecipeBridge.CaptureSelection(doc,doc.Actions.Concat(Gen6MintRecipeBridge.ActionIds).ToHashSet(StringComparer.OrdinalIgnoreCase),true,true);
                Directory.CreateDirectory(Path.Combine(destination,"custom_balance_templates"));
                foreach(var copy in copies){using var stream=new FileStream(Path.Combine(destination,"custom_balance_templates",copy.Name),FileMode.CreateNew);stream.Write(copy.Bytes);}
                string output=Path.Combine(destination,"PokeJuz_XY_Batch.json");doc.Save(output);var loaded=GlobalTemplateDocument.Load(output,"XY",6);
                GlobalEditorTemplateBridge.ValidateBatchReplay(loaded);
                Check(File.ReadAllBytes(source).SequenceEqual(original)&&copies.All(c=>File.ReadAllBytes(c.Source).SequenceEqual(c.Bytes)),"Source recipe/CSVs preserved");
                Save(report,new{SourceSha256=Hash(original),UpdatedSha256=Hash(File.ReadAllBytes(output)),Template=output,
                    NewActions=Gen6MintRecipeBridge.ActionIds,Options=new[]{"ProgressiveApp.Gen6MintItems.IncludeMarts","ProgressiveApp.Gen6MintItems.IncludeFieldItems"},
                    Assets=copies.Select(c=>new{c.Name,Sha256=Hash(c.Bytes)}),OriginalFilesUnchanged=true});
            }
            else if(args.Length==3&&args[0]=="runtime-profile")
            {
                NewEvidence(args[1],args[2]);Save(args[2],Gen6MintRuntimeDependencies.DescribeOriginal(GameProjectSession.Open(Path.Combine(args[1],"Proyecto"),false)));
            }
            else if (args.Length == 3 && args[0] == "install-icons")
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
                var observations=new List<object>();string sheetPath=args[2]+".png";Check(!File.Exists(sheetPath),"Preview image output must be new");
                using var sheet=new System.Drawing.Bitmap(280,120);using var drawing=System.Drawing.Graphics.FromImage(sheet);drawing.Clear(System.Drawing.Color.LightGray);
                foreach(var m in Gen6NatureMintCatalog.ExperimentalItemMap.Items)
                {
                    int image=BitConverter.ToInt32(code,0x43db74+m.ItemId*4);using var stream=new MemoryStream();
                    LZSS.Decompress(new MemoryStream(rows[image]),rows[image].Length,stream);byte[] raw=stream.ToArray();
                    var tex=new BCLIM(raw);byte[] display=(byte[])raw.Clone();
                    if(BitConverter.ToUInt16(raw,0)==2)Gen6MintIconService.ExpandPalette(raw).CopyTo(display,0);
                    using var decoded=GameGraphicsCodec.Decode(display);int slot=observations.Count;drawing.DrawImageUnscaled(decoded,slot%7*40+5,slot/7*40+5);
                    observations.Add(new{m.ItemId,image,raw.Length,tex.Valid,tex.Width,tex.Height,Format=tex.Format.ToString(),Orientation=tex.Orientation.ToString(),DataPrefix=Convert.ToHexString(raw[..16]),Footer=Convert.ToHexString(raw[^40..])});
                }
                Save(args[2],new{Title=Target(root),Count=rows.Length,Version=BitConverter.ToUInt16(archive,10),CodeSha256=Hash(code),Observations=observations});
                sheet.Save(sheetPath,System.Drawing.Imaging.ImageFormat.Png);
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
            else if (args.Length == 3 && args[0] is "verify-candidate" or "verify-full-cycle") VerifyCandidate(args[1],args[2],args[0]=="verify-full-cycle");
            else { Console.Error.WriteLine("extract SOURCE_ROM NEW_PruebasMentasXY_ROOT | inspect/scripts/references/plan/install-experiment/verify-undo/prepare-test-shops/rebuild-reextract/icon-probe/icon-textures/install-icons/verify-candidate/verify-full-cycle/runtime-profile ISOLATED_ROOT NEW_PRIVATE_JSON | prepare-global ROOT SOURCE_RECIPE NEW_GLOBAL_FOLDER | preview-global/batch-global ROOT RECIPE SEED NEW_CASE"); return 2; }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
