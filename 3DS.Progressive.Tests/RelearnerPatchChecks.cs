using System.IO;
using System.Buffers.Binary;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

/// <summary>USUM Move Relearner (CAFE.asm + Learn.asm) on copies of real executables, through the original LegacyBatch patcher.</summary>
internal static class RelearnerPatchChecks
{
    private const uint TextEnd=0x5BA000,Storage=0x6D3FFC,LegacyStorage=0x5B9D00;
    private const int LearnLiteral=0x4B9D48;

    private static string Code(string root)=>new[]{"ExeFS/.code.bin","ExeFS/code.bin","ExtractedExeFS/code.bin","ExtractedExeFS/.code.bin"}
        .Select(p=>Path.Combine(root,p)).First(File.Exists);
    private static string RomFS(string root)=>new[]{"RomFS","ExtractedRomFS"}.Select(p=>Path.Combine(root,p)).First(Directory.Exists);

    /// <summary>Runs the app's relearner preview on a fixture holding the real code.bin and the ten StoryText banks; returns the staged code.bin.</summary>
    private static byte[] Preview(string root,byte[] code,out string report)
    {
        using var f=new Fixture(GameVersion.USUM);
        ModuleChecks.CreateArchives(f);GlobalApplicationChecks.PrepareOpenable(f);
        var reference=f.Game.GetGARCReference("storytext");
        for(int language=0;language<10;language++)
        {
            var relative=reference.GetRelativeGARC(language);
            File.Copy(Path.Combine(RomFS(root),relative.Reference),Path.Combine(f.Game.RomFS,relative.Reference),true);
        }
        string path=Path.Combine(f.Game.ExeFS,"code.bin");File.WriteAllBytes(path,code);
        var preview=new GameUsumRelearnerService(f.Session).PrepareAsync(_=>{}).GetAwaiter().GetResult();
        report=preview.Report;
        return preview.Files.FirstOrDefault(p=>Path.GetFileName(p.Path).Contains("code",StringComparison.OrdinalIgnoreCase))?.After??code;
    }

    private static IEnumerable<int> Diff(byte[] a,byte[] b)=>Enumerable.Range(0,Math.Min(a.Length,b.Length)).Where(i=>a[i]!=b[i]);

    // No store of the Learn payload may target .text: its only literal must point outside the executable pages.
    private static bool StorageOutsideText(byte[] code)=>BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(LearnLiteral))==Storage&&Storage>=TextEnd;

    internal static void Run(string ultraSun,string ultraMoon,string legacyPatched,Action<bool,string> check)
    {
        var sources=new[]{Code(ultraSun),Code(ultraMoon),Code(legacyPatched)}.ToDictionary(p=>p,p=>ProjectHistory.Hash(File.ReadAllBytes(p)));
        foreach(var (root,name) in new[]{(ultraSun,"US"),(ultraMoon,"UM")})
        {
            byte[] original=File.ReadAllBytes(Code(root));
            byte[] patched=Preview(root,original,out string report);
            var changed=Diff(original,patched).ToArray();
            // Owned regions: three 4-byte hooks (US 0x29A044/0x341654/0x3417D8, UM +4 for Learn) and the two caves.
            bool owned=changed.All(i=>i is >=0x4B9D04 and <0x4B9D4C or >=0x4B9FA0 and <0x4B9FDC||new[]{0x29A044,0x341654,0x3417D8,0x341658,0x3417DC}.Any(h=>i>=h&&i<h+4));
            check(changed.Length>0&&owned&&StorageOutsideText(patched),$"Relearner_{name}_CleanInstallStoresPointerInBssTail ({changed.Length} bytes)");
            check(BinaryPrimitives.ReadUInt32LittleEndian(patched.AsSpan(0x4B9D00))==0,$"Relearner_{name}_NothingWrittenAtOldTextStorage");
            byte[] again=Preview(root,patched,out _);
            check(again.AsSpan().SequenceEqual(patched),$"Relearner_{name}_SecondApplyChangesNothing");
        }
        // A ROM built with the earlier patch (pointer in .text) is upgraded by rewriting only the literal.
        byte[] legacy=File.ReadAllBytes(Code(legacyPatched));
        check(BinaryPrimitives.ReadUInt32LittleEndian(legacy.AsSpan(LearnLiteral))==LegacyStorage,"Relearner_LegacyRomHasPointerInText");
        byte[] upgraded=Preview(legacyPatched,legacy,out _);
        var diff=Diff(legacy,upgraded).ToArray();
        check(diff.Length is >=1 and <=4&&diff.All(i=>i>=LearnLiteral&&i<LearnLiteral+4)&&StorageOutsideText(upgraded),$"Relearner_LegacyRomUpgradedOnlyTheStorageAddress ({diff.Length} bytes)");
        foreach(var (path,hash) in sources)check(ProjectHistory.Hash(File.ReadAllBytes(path))==hash,"Relearner_SourceUnchanged_"+Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))));
    }
}
