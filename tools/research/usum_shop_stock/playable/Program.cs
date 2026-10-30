using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;

// Research-only CLI. Writes only a freshly created experimental tree, never an input project.
internal static class Program
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static string Hash(string path) { using var s=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
    static string BytesHash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));
    static void Check(bool value,string name) { if(!value)throw new InvalidDataException(name);Console.WriteLine("PASS "+name); }
    static void Save(string path,object value)=>File.WriteAllText(path,JsonSerializer.Serialize(value,JsonOptions));
    static string Project(string root)=>Path.Combine(root,"Proyecto");
    static string Evidence(string root)=>Path.Combine(root,"evidence");
    static JsonDocument Plan(string root)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(Evidence(root),"allocation-plan.json")));
    static string Child(string root,string relative)
    {
        string full=Path.GetFullPath(Path.Combine(root,relative));
        if(!full.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Path outside experimental tree");
        return full;
    }
    static void VerifySources(string root)
    {
        var sources=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(Evidence(root),"source-hashes.json")))!;
        foreach(var (file,hash) in sources)Check(Hash(file)==hash,"source_unchanged_"+Path.GetFileName(file));
    }
    static void CopyChecked(string from,string to,Dictionary<string,string> hashes)
    {
        if((File.GetAttributes(from)&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse point: "+from);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        string hash=Hash(from);File.Copy(from,to,false);
        Check(Hash(to)==hash,"staging_readback_"+Path.GetFileName(from));hashes.Add(from,hash);
    }
    static void Prepare(string source,string root)
    {
        source=Path.GetFullPath(source);root=Path.GetFullPath(root);
        if(root.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||source.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||root.Equals(source,StringComparison.OrdinalIgnoreCase))throw new IOException("Source and output overlap");
        if(Directory.Exists(root)||File.Exists(root))throw new IOException("Destination must be new");
        string rom=Path.Combine(source,"ExtractedRomFS"),exe=Path.Combine(source,"ExtractedExeFS");
        Check(Directory.Exists(Path.Combine(rom,"a"))&&Directory.Exists(exe),"source_is_extracted_project");
        Check(Hash(Path.Combine(rom,"Shop.cro"))=="8BB08FBCD26B694CADC6ED6858616E57BE7AD51AB61A0181D30B570730A28D7A","exact_researched_randomized_shop");
        Directory.CreateDirectory(Evidence(root));Directory.CreateDirectory(Project(root));
        var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        CopyChecked(Path.Combine(source,"DecryptedExHeader.bin"),Path.Combine(Project(root),"ExHeader.bin"),hashes);
        foreach(var (folder,name) in new[]{(rom,"RomFS"),(exe,"ExeFS")})
        {
            if(Directory.EnumerateDirectories(folder,"*",SearchOption.AllDirectories).Any(d=>(File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0))throw new IOException("Linked folder");
            foreach(string file in Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories))CopyChecked(file,Path.Combine(Project(root),name,Path.GetRelativePath(folder,file)),hashes);
        }
        Save(Path.Combine(Evidence(root),"source-hashes.json"),hashes);
        byte[] original=File.ReadAllBytes(Path.Combine(rom,"Shop.cro"));File.WriteAllBytes(Path.Combine(Evidence(root),"original-Shop.cro"),original);
        Check(Gen7ShopCroLayout.TryRead(original,out var before,out string error),"read_existing_expanded_layout: "+error);
        var inventories=before.RegularMarts.Inventories.Select(x=>x.ToArray()).ToArray();
        // Shops 10/11 are special regular-money vendors: Hau'oli X items and Route 2 miscellany.
        foreach(int shop in new[]{10,11})inventories[shop]=inventories[shop].Concat((shop==10?new ushort[]{101,102,103}:new ushort[]{101,103}).Where(id=>!inventories[shop].Contains(id))).ToArray();
        Check(Gen7ExpandedMartTable.TryRebuild(original,inventories,out byte[] fixtures,out var fixtureReport,out error),"append_test_fossils_with_existing_mart_builder: "+error);
        Check(CroPatchSession.TryCreate(fixtures,out var session,out error),"create_existing_cro_patch_session: "+error);
        Check(session.TryAllocateRelocatableCode(0x700,"USUM experimental unique-stock ARM hooks",out var grant,out error),"allocate_declared_executable_storage: "+error);
        Check(session.TryWriteCode(grant,new byte[grant.Size],out error),"prepare_owned_blank_grant: "+error);
        Check(session.TryBuildImage(out byte[] prepared,out var report,out error),"existing_expander_finalizes_prepared_image: "+error);
        Check(grant.Offset>=session.Map.CodeStart&&grant.Offset+grant.Size<=session.Map.CodeEnd,"payload_grant_inside_declared_text");
        string preparedFile=Path.Combine(Evidence(root),"prepared-Shop.cro");File.WriteAllBytes(preparedFile,prepared);
        Save(Path.Combine(Evidence(root),"allocation-plan.json"),new {SourceSha256=BytesHash(original),PreparedSha256=BytesHash(prepared),PreparedFile=preparedFile,AllocationBase=grant.Offset,GrantSize=grant.Size,FixtureShops=new[]{10,11},Inventories=inventories,FixtureReport=fixtureReport,ExpansionReport=report});
        Check(Gen7ShopCroLayout.TryRead(prepared,out var after,out error),"reopen_prepared_cro_layout: "+error);
        VerifyTables(before,original,after,prepared,inventories);
        Console.WriteLine("Prepared "+root);
    }
    static byte[] Section(byte[] bytes,Gen7ShopGroupedTableSnapshot table)=>bytes.AsSpan((int)table.DataStart,table.DataLength).ToArray();
    static void VerifyTables(Gen7ShopCroLayoutSnapshot before,byte[] first,Gen7ShopCroLayoutSnapshot after,byte[] last,ushort[][] expected)
    {
        Check(after.RegularMarts.Inventories.Count==expected.Length&&expected.Select((x,i)=>x.SequenceEqual(after.RegularMarts.Inventories[i])).All(x=>x),"all_regular_mart_inventories_match_fixture_plan");
        Check(before.BPItems.Counts.SequenceEqual(after.BPItems.Counts)&&Section(first,before.BPItems).SequenceEqual(Section(last,after.BPItems)),"bp_items_and_prices_preserved");
        Check(before.BPTutors.Counts.SequenceEqual(after.BPTutors.Counts)&&Section(first,before.BPTutors).SequenceEqual(Section(last,after.BPTutors)),"bp_tutor_items_and_prices_preserved");
    }
    static void Install(string root,string testedImage)
    {
        string marker=Path.Combine(Evidence(root),"installed.json");if(File.Exists(marker))throw new IOException("Already installed");
        using var document=Plan(root);var plan=document.RootElement;
        byte[] prepared=File.ReadAllBytes(plan.GetProperty("PreparedFile").GetString()!);Check(BytesHash(prepared)==plan.GetProperty("PreparedSha256").GetString(),"allocated_source_hash_valid");
        byte[] patched=File.ReadAllBytes(testedImage);int start=plan.GetProperty("AllocationBase").GetInt32(),size=plan.GetProperty("GrantSize").GetInt32();
        Check(patched.Length==prepared.Length,"tested_payload_does_not_resize_prepared_cro");
        for(int i=0;i<prepared.Length;i++)if(prepared[i]!=patched[i]&&!(i>=start&&i<start+size)&&!(i>=0x276c&&i<0x2770)&&!(i>=0x15e4&&i<0x15e8)&&!(i>=0x164c&&i<0x1650))throw new InvalidDataException("Unexpected payload change at "+i.ToString("X"));
        Check(!patched.AsSpan(start,size).SequenceEqual(new byte[size]),"tested_payload_is_present");
        patched=CroIntegrity.Rebuild(patched);string error;Check(true,"refresh_final_cro_hashes_with_existing_service");
        Check(CroIntegrity.Rebuild(patched).SequenceEqual(patched),"final_cro_all_four_hashes_valid");
        Check(Gen7ShopCroLayout.TryRead(prepared,out var before,out error)&&Gen7ShopCroLayout.TryRead(patched,out _,out error),"final_cro_structural_readback: "+error);
        Gen7ShopCroLayout.TryRead(patched,out var after,out _);
        var expected=JsonSerializer.Deserialize<ushort[][]>(plan.GetProperty("Inventories").GetRawText())!;
        VerifyTables(before,prepared,after,patched,expected);
        string rom=Path.Combine(Project(root),"RomFS"),shopPath=Path.Combine(rom,"Shop.cro");
        Check(Hash(shopPath)==plan.GetProperty("SourceSha256").GetString(),"staged_shop_has_not_changed_before_install");
        var paths=Directory.EnumerateFiles(rom).Where(p=>Path.GetExtension(p).Equals(".cro",StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var cros=paths.Select(p=>p.Equals(shopPath,StringComparison.OrdinalIgnoreCase)?patched:File.ReadAllBytes(p)).ToArray();
        Check(cros.All(c=>CroIntegrity.Rebuild(c).SequenceEqual(c)),"all_staged_cro_headers_valid_without_altering_other_cros");
        string crrPath=Path.Combine(rom,".crr","static.crr");byte[] crrBefore=File.ReadAllBytes(crrPath);byte[] crr=CroIntegrity.RebuildCrr(crrBefore,cros);
        Check(CroIntegrity.RebuildCrr(crr,cros).SequenceEqual(crr),"final_crr_table_matches_all_cro_hashes");
        File.WriteAllBytes(shopPath,patched);File.WriteAllBytes(crrPath,crr);
        Check(Hash(shopPath)==BytesHash(patched)&&Hash(crrPath)==BytesHash(crr),"installed_copy_readback_exact");
        Save(marker,new {ShopSHA256=BytesHash(patched),CRRSHA256=BytesHash(crr),Storage="EXPERIMENTAL EventWork save offset 0xD00; isolated saves only",CheckedAt=DateTimeOffset.UtcNow});
        VerifySources(root);
    }
    static void Build(string root,string image)
    {
        if(!File.Exists(Path.Combine(Evidence(root),"installed.json")))throw new IOException("Install/verify prototype first");
        var project=GameProjectSession.Open(Project(root),recoverInterruptedSave:false);
        Check(project.GameConfiguration.USUM,"rebuild_is_usum");
        var items=new GameItemService(project);Save(Path.Combine(Evidence(root),"fixture-items.json"),new[]{101,102,103}.Select(id=>new{Id=id,Name=project.ItemNames[id],Price=items.Read(id).SelectMany(g=>g.Fields).First(f=>f.Key=="Price").Value}));
        var itemEditor=new GameItemService(project);
        Save(Path.Combine(Evidence(root),"fixture-items.json"),new[]{101,102,103}.Select(id=>new{Id=id,Name=project.ItemNames[id],Price=itemEditor.Read(id).SelectMany(g=>g.Fields).First(f=>f.Key=="Price").Value}).ToArray());
        var output=new GameRomRebuildService(project).BuildAsync(RomRebuildKind.Trimmed3DS,image,(name,n,max)=>Console.WriteLine($"BUILD {n}/{max} {name}")).GetAwaiter().GetResult();
        Save(Path.Combine(Evidence(root),"build-result.json"),new{output.File,output.Hash,output.Bytes});
        Console.WriteLine("BUILT "+output.File+" "+output.Hash+" "+output.Bytes);
        VerifySources(root);
    }
    static void VerifyImage(string root,string image,string extracted)
    {
        var output=new GameRomExtractionService(Project(root)).ExtractAsync(image,extracted).GetAwaiter().GetResult();
        var expected=Directory.GetFiles(Path.Combine(Project(root),"RomFS"),"*",SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(Project(root),"ExeFS"),"*",SearchOption.AllDirectories)).ToArray();
        foreach(string file in expected)
        {
            string relative=Path.GetRelativePath(Project(root),file);
            if(relative==Path.Combine("ExeFS","code.bin"))relative=Path.Combine("ExeFS",".code.bin");
            Check(Hash(file)==Hash(Child(extracted,relative)),"container_roundtrip_"+relative);
        }
        VerifySources(root);
        Save(Path.Combine(Evidence(root),"verified-image.json"),new{Image=Path.GetFullPath(image),SHA256=Hash(image),Bytes=new FileInfo(image).Length,ComparedFiles=expected.Length,Extracted=output.Folder,Gameplay="PENDING USER VALIDATION"});
        Console.WriteLine("ROM VERIFIED "+expected.Length+" files; gameplay pending");
    }
    static void AuditText(string root)
    {
        var p=GameProjectSession.Open(root,recoverInterruptedSave:false);var c=p.GameConfiguration;
        var data=new pk3DS.Core.CTR.GARC.MemGARC(File.ReadAllBytes(Path.Combine(c.RomFS,c.GetGARCFileName("gametext")))).Files;
        for(int bank=0;bank<data.Length;bank++)
        {
            var lines=new pk3DS.Core.TextFile(c,data[bank],c.RemapCharacters).Lines;
            if(lines.Any(x=>x=="Dinero")||lines.Any(x=>x.Contains("dinero suficiente",StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine(JsonSerializer.Serialize(new{Bank=bank,Lines=lines.Select((Text,Id)=>new{Id,Text})}));
        }
    }
    [STAThread]
    static void Main(string[] args)
    {
        if(args.Length==2&&args[0]=="audit-text")AuditText(args[1]);
        else if(args.Length==3&&args[0]=="prepare")Prepare(args[1],args[2]);
        else if(args.Length==3&&args[0]=="install")Install(Path.GetFullPath(args[1]),args[2]);
        else if(args.Length==3&&args[0]=="build")Build(Path.GetFullPath(args[1]),args[2]);
        else if(args.Length==4&&args[0]=="verify")VerifyImage(Path.GetFullPath(args[1]),args[2],args[3]);
        else throw new ArgumentException("prepare SOURCE NEW_ROOT | install ROOT TESTED_IMAGE | build ROOT NEW_3DS | verify ROOT IMAGE NEW_EXTRACTED");
    }
}
