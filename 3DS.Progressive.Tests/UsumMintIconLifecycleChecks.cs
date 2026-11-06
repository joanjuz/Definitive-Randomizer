using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;

internal static partial class UsumMintIconChecks
{
    private static void Lifecycle(Action<bool,string> check,Action<Action,string> reject,bool moon,byte[][] textures,UsumMintIconSource[] sources)
    {
        string game=moon?"UM":"US";
        using var f=new Fixture(GameVersion.USUM);var profile=UsumNatureMintChecks.Setup(f,moon);
        byte[] code=new byte[UsumMintIconLayout.TableOffset+UsumMintIconLayout.ItemCount*4+16];code[0]=(byte)(moon?2:1);
        for(int item=0;item<960;item++)BitConverter.GetBytes(item%769).CopyTo(code,UsumMintIconLayout.TableOffset+item*4);
        foreach(var source in sources)BitConverter.GetBytes(source.ItemId==622?492:768).CopyTo(code,UsumMintIconLayout.TableOffset+source.ItemId*4);
        string codePath=Path.Combine(f.Game.ExeFS,"code.bin");File.WriteAllBytes(codePath,code);profile=profile with{CodeSha256=Hash(code)};
        UsumNatureMintProfile Identity(byte[] bytes)=>Hash(UsumMintIconLayout.NormalizeCode(bytes))==profile.CodeSha256?profile:throw new InvalidDataException("Unrecognized fixture executable.");
        string iconPath=Path.GetFullPath(Path.Combine(f.Game.RomFS,UsumMintIconLayout.ArchivePath));
        byte[] compressed=UsumMintIconGraphics.Compress(textures[0]);
        var previousIcons=Enumerable.Range(0,769).Select(_=>compressed).ToArray();
        previousIcons[47]=UsumMintIconGraphics.Compress(textures[2]); // Existing custom artwork must survive.
        f.WriteGarc(iconPath,previousIcons,f.Game.GetGARCReference("itemicons"));
        // Opaque synthetic shop sentinel, not a playable USUM shop table.
        string shopPath=Path.Combine(f.Game.RomFS,"Shop.cro");byte[] shop=MartSmChecks.SyntheticShop();File.WriteAllBytes(shopPath,shop);
        string bagPath=Path.Combine(f.Game.RomFS,"Bag.cro"),crrPath=Path.Combine(f.Game.RomFS,".crr/static.crr");
        byte[] crr=File.ReadAllBytes(crrPath);BitConverter.GetBytes(2).CopyTo(crr,0x354);File.WriteAllBytes(crrPath,CroIntegrity.RebuildCrr(crr,[File.ReadAllBytes(bagPath),shop]));
        var phase1=new UsumNatureMintService(f.Session,Identity);
        check(new UsumMintIconService(f.Session,Identity,sources).Inspect().State==UsumMintState.Incompatible,"MintIconsRequirePhase1"+game);
        phase1.Apply(phase1.Inspect());
        var service=new UsumMintIconService(f.Session,Identity,sources);
        check(service.Inspect().State==UsumMintState.NotInstalled,"MintIconsRecognizePhase1Upgrade"+game);
        byte[] originalIcons=File.ReadAllBytes(iconPath);
        byte[] vectorized=(byte[])originalIcons.Clone();vectorized[0x24+12+4*769+12]=3;
        reject(()=>UsumMintIconGraphics.ReadArchive(vectorized,769),"MintIconsRejectVectorizedArchive"+game);
        f.WriteGarc(iconPath,[..previousIcons,compressed],f.Game.GetGARCReference("itemicons"));
        check(service.Inspect().State==UsumMintState.Incompatible,"MintIconsRejectForeignAppendedRecords"+game);
        File.WriteAllBytes(iconPath,originalIcons);
        var allBefore=Directory.GetFiles(f.Game.RomFS,"*",SearchOption.AllDirectories).Concat(Directory.GetFiles(f.Game.ExeFS)).ToDictionary(p=>p,p=>Hash(File.ReadAllBytes(p)));
        var preview=service.Prepare(textures);var proposal=preview.Proposal!;
        reject(()=>new UsumMintIconService(f.Session,Identity,sources).Apply(preview),"MintIconsRejectForeignProposal"+game);
        check(proposal.Files.Count==2&&Hash(File.ReadAllBytes(iconPath))==Hash(originalIcons)&&File.ReadAllBytes(codePath).SequenceEqual(code),"MintIconsDetachedTwoFileProposal"+game);
        var outputRecords=UsumMintIconGraphics.ReadArchive(proposal.Files.Single(p=>p.Path==iconPath).Proposed(),790);
        var originalRecords=new GARC.MemGARC(originalIcons).Files;
        check(originalRecords.Select((record,i)=>record.SequenceEqual(outputRecords[i])).All(equal=>equal),"MintIconsPreserveAll769OriginalRecords"+game);
        var proposedCode=proposal.Files.Single(p=>p.Path==codePath).Proposed();
        check(UsumMintIconLayout.NormalizeCode(proposedCode).SequenceEqual(code)&&expectedReferences(proposedCode),"MintIconsOnly21ExecutableWordsChange"+game);
        check(sources.Select((source,i)=>Hash(UsumMintIconGraphics.Decompress(outputRecords[769+i]))==source.TextureSha256).All(equal=>equal),"MintIconsEveryImageMatchesItsMint"+game);
        reject(()=>FileTransaction.Commit(proposal.Files,i=>{if(i==1)throw new IOException("Injected icon write failure");}),"MintIconsRollbackInjectedFailure"+game);
        check(File.ReadAllBytes(codePath).SequenceEqual(code)&&File.ReadAllBytes(iconPath).SequenceEqual(originalIcons)&&f.Session.History.Read().Entries.Count==1,"MintIconsRollbackExactBytesAndHistory"+game);
        var wrong=(byte[][])textures.Clone();wrong[0]=textures[1];reject(()=>service.Prepare(wrong),"MintIconsRejectSwappedGraphics"+game);
        byte[] bagBefore=File.ReadAllBytes(bagPath);byte[] changed=(byte[])bagBefore.Clone();changed[^1]^=1;File.WriteAllBytes(bagPath,changed);
        reject(()=>service.Apply(preview),"MintIconsRejectChangedDependency"+game);
        check(File.ReadAllBytes(codePath).SequenceEqual(code)&&File.ReadAllBytes(iconPath).SequenceEqual(originalIcons),"MintIconsDependencyFailureNoPartialWrite"+game);
        File.WriteAllBytes(bagPath,bagBefore);
        using(var writer=new FileStream(bagPath,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete))
            reject(()=>service.Apply(preview),"MintIconsRejectConcurrentDependencyWriter"+game);
        service.Apply(preview);
        check(new UsumMintIconService(Fixture.CreateSession(f.Game,f.Root,f.PersonalPath),Identity,sources).Inspect().State==UsumMintState.Installed,"MintIconsDetectedAfterReopen"+game);
        check(new UsumNatureMintService(f.Session,Identity).Inspect().State==UsumMintState.Installed,"MintIconsPhase1StillRecognized"+game);
        check(allBefore.Where(p=>p.Key!=iconPath&&p.Key!=codePath).All(p=>Hash(File.ReadAllBytes(p.Key))==p.Value),"MintIconsNoChangesToShopsPricesTextsOrOtherFiles"+game);
        var entry=f.Session.History.Read().Entries.Last();
        check(entry.Files.Count==2&&entry.Section=="UsumNatureMintIcons", "MintIconsHistoryAtomicPair"+game);
        check(service.Inspect().Proposal is null,"MintIconsIdempotentState"+game);
        reject(()=>service.Apply(preview),"MintIconsRejectRepeatedInstallation"+game);
        byte[] installed=File.ReadAllBytes(iconPath);var mutated=new GARC.MemGARC(installed).Files;mutated[769]=UsumMintIconGraphics.Compress(textures[1]);
        f.WriteGarc(iconPath,mutated,f.Game.GetGARCReference("itemicons"));
        check(service.Inspect().State==UsumMintState.Modified,"MintIconsDetectExternalIconChange"+game);
        File.WriteAllBytes(iconPath,installed);
        RestorePoints.Undo(f.Session.History,f.Root,entry.Id);
        check(File.ReadAllBytes(codePath).SequenceEqual(code)&&File.ReadAllBytes(iconPath).SequenceEqual(originalIcons)&&service.Inspect().State==UsumMintState.NotInstalled,"MintIconsUndoRestoresExactPair"+game);
        check(phase1.Inspect().State==UsumMintState.Installed&&File.ReadAllBytes(shopPath).SequenceEqual(shop),"MintIconsUndoKeepsFunctionalPhase1AndShops"+game);
        byte[] badCode=(byte[])code.Clone();badCode[42]^=1;File.WriteAllBytes(codePath,badCode);
        check(service.Inspect().State==UsumMintState.Incompatible,"MintIconsRejectUnknownExecutable"+game);
        File.WriteAllBytes(codePath,code);
        string header=Path.Combine(f.Root,"HeaderNCCH0.bin");byte[] ncch=File.ReadAllBytes(header);ncch[0x118]^=1;File.WriteAllBytes(header,ncch);
        check(service.Inspect().State==UsumMintState.Incompatible,"MintIconsRejectMismatchedTitle"+game);

        static bool expectedReferences(byte[] bytes)=>UsumNatureMintCatalog.Mints.Select((mint,i)=>BitConverter.ToUInt32(bytes,UsumMintIconLayout.TableOffset+mint.ItemId*4)==769+i).All(equal=>equal);
    }
}
