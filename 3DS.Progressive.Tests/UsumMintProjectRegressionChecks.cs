using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;

// Opt-in private regression. All writes are confined to a new output copy.
internal static class UsumMintProjectRegressionChecks
{
    internal static void Run(string source,string output,Action<bool,string> check)
    {
        source=Path.GetFullPath(source);output=Path.GetFullPath(output);
        if(Directory.Exists(output)||File.Exists(output)||output.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||source.StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||source.Equals(output,StringComparison.OrdinalIgnoreCase))
            throw new IOException("La salida debe ser nueva y estar separada del proyecto fuente.");
        string romfs=new[]{"ExtractedRomFS","RomFS"}.First(n=>Directory.Exists(Path.Combine(source,n)));
        string exefs=new[]{"ExtractedExeFS","ExeFS"}.First(n=>Directory.Exists(Path.Combine(source,n)));
        string[] paths=Directory.GetFiles(Path.Combine(source,romfs),"*",SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(source,exefs),"*",SearchOption.AllDirectories))
            .Concat(new[]{"HeaderNCCH0.bin","HeaderNCCH.bin","ncchheader.bin","exheader.bin","DecryptedExHeader.bin"}.Select(n=>Path.Combine(source,n)).Where(File.Exists)).ToArray();
        var originals=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(output);
        foreach(string path in paths){
            for(string? at=path;at is not null&&at.Length>=source.Length;at=Path.GetDirectoryName(at))
                if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("No se copian enlaces del proyecto.");
            string relative=Path.GetRelativePath(source,path),target=Path.Combine(output,relative);
            originals.Add(relative,Hash(path));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(path,target);
        }
        check(originals.All(p=>Hash(Path.Combine(output,p.Key))==p.Value),"MintRealProjectCopyMatchesSource");
        var session=GameProjectSession.Open(output,false);var service=new UsumNatureMintService(session);
        var preview=service.Inspect();Console.WriteLine("MintRealProjectInspection: "+preview.State+" · "+preview.Report);
        if(preview.State==UsumMintState.Installed){
            check(preview.Proposal is null,"MintRealInstalledProjectRecognized");
        }else{
            check(preview.State==UsumMintState.NotInstalled&&preview.Proposal is not null,"MintRealRandomizedProjectReady");
            var changed=preview.Proposal!.Files.Where(f=>f.Changed).Select(f=>Path.GetRelativePath(output,f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var config=session.GameConfiguration;
            var itemFile=preview.Proposal.Files.Single(f=>f.Path.Equals(Path.GetFullPath(Path.Combine(config.RomFS,config.GetGARCFileName("item"))),StringComparison.OrdinalIgnoreCase));
            var oldItems=new GARC.MemGARC(itemFile.Original()).Files;var newItems=new GARC.MemGARC(itemFile.Proposed()).Files;
            check(oldItems.Length==newItems.Length&&Enumerable.Range(0,oldItems.Length).Where(id=>!UsumMintItemPolicy.Ids.Contains(id)).All(id=>oldItems[id].SequenceEqual(newItems[id])),"MintRealUnrelatedItemRecordsPreserved");
            var pocketFile=preview.Proposal.Files.Single(f=>f.Path.Equals(Path.GetFullPath(Path.Combine(config.RomFS,"a/1/0/3")),StringComparison.OrdinalIgnoreCase));
            var oldPockets=new GARC.MemGARC(pocketFile.Original()).Files;var newPockets=new GARC.MemGARC(pocketFile.Proposed()).Files;
            check(oldPockets.Length==newPockets.Length&&Enumerable.Range(1,oldPockets.Length-1).All(i=>oldPockets[i].SequenceEqual(newPockets[i]))&&Enumerable.Range(0,960).Where(id=>!UsumMintItemPolicy.Ids.Contains(id)).All(id=>UsumItemPocketTable.Read(oldPockets[0],id)==UsumItemPocketTable.Read(newPockets[0],id)),"MintRealUnrelatedPocketNibblesAndMembersPreserved");
            bool textPreserved=true;
            foreach(int language in Enumerable.Range(0,10)){
                string path=Path.Combine(config.RomFS,config.GetGARCReference("gametext").GetRelativeGARC(language).Reference);
                var file=preview.Proposal.Files.Single(f=>f.Path.Equals(Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase));
                var beforeText=new GARC.MemGARC(file.Original()).Files;var afterText=new GARC.MemGARC(file.Proposed()).Files;
                textPreserved&=beforeText.Length==afterText.Length;
                for(int bank=0;bank<beforeText.Length;bank++){
                    if(bank is not(1 or 39 or 40 or 41 or 42)){textPreserved&=beforeText[bank].SequenceEqual(afterText[bank]);continue;}
                    var rowsBefore=new TextFile(config,beforeText[bank]);var rowsAfter=new TextFile(config,afterText[bank]);
                    textPreserved&=rowsAfter.Lines.Length==rowsBefore.Lines.Length+(bank==1?1:0);
                    for(int row=0;row<rowsBefore.Lines.Length;row++)
                        if(bank==1||!UsumMintItemPolicy.Ids.Contains(row))textPreserved&=rowsBefore.LineData[row].SequenceEqual(rowsAfter.LineData[row]);
                }
            }
            check(textPreserved,"MintRealUnrelatedTextBanksAndEncryptedRowsPreserved");
            check(originals.All(p=>Hash(Path.Combine(output,p.Key))==p.Value)&&session.History.Read().Entries.Count==0,"MintRealPreparationPreservesProjectAndHistory");
            service.Apply(preview);
            var reopened=GameProjectSession.Open(output,false);
            check(new UsumNatureMintService(reopened).Inspect().State==UsumMintState.Installed,"MintRealRandomizedInstallReopens");
            check(originals.Where(p=>!changed.Contains(p.Key)).All(p=>Hash(Path.Combine(output,p.Key))==p.Value),"MintRealRandomizedUnrelatedFilesPreserved");
            var entry=reopened.History.Read().Entries.Single();
            check(entry.Files.Count==14,"MintRealRandomizedUsesExistingHistory");
            RestorePoints.Undo(reopened.History,output,entry.Id);
            check(originals.All(p=>Hash(Path.Combine(output,p.Key))==p.Value),"MintRealRandomizedUndoExactAllFiles");
            check(new UsumNatureMintService(GameProjectSession.Open(output,false)).Inspect().State==UsumMintState.NotInstalled,"MintRealRandomizedReadyAfterUndo");
        }
        check(originals.All(p=>Hash(Path.Combine(source,p.Key))==p.Value),"MintRealSourceProjectUntouched");
        Console.WriteLine("Private project copy: "+output+". No gameplay/emulator claim.");
    }
    private static string Hash(string path){using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file));}
}
