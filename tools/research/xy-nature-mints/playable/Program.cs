using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive.Services;

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
            texts.Add(new { Language = language, Banks = new[]{1,47}.Concat(Enumerable.Range(90, 12)).Select(index => new { Index = index, Lines = new TextFile(config, banks[index], config.RemapCharacters).Lines }).ToArray() });
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
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "extract") Extract(args[1], args[2]);
            else if (args.Length == 3 && args[0] == "inspect") Inspect(args[1], args[2]);
            else if (args.Length == 3 && args[0] == "scripts") Scripts(args[1], args[2]);
            else if (args.Length == 3 && args[0] == "references") References(args[1], args[2]);
            else { Console.Error.WriteLine("extract SOURCE_ROM NEW_PruebasMentasXY_ROOT | inspect ISOLATED_ROOT NEW_PRIVATE_JSON | scripts ISOLATED_ROOT NEW_JSON | references ISOLATED_ROOT NEW_JSON"); return 2; }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
