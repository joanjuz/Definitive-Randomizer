using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using pk3DS.Core.Modding.Research;

/// <summary>Isolated, synthetic installation fixtures; these profiles are never production evidence.</summary>
internal static class Gen6MintServiceChecks
{
    private static readonly int[] Banks=[95,96,97,98,99,140];
    private static readonly HashSet<int> Ids=Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).ToHashSet();
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static string Framed(byte[][] records,Func<int,bool> include)
    {
        using var stream=new MemoryStream();
        for(int i=0;i<records.Length;i++)if(include(i))
        {stream.Write(BitConverter.GetBytes(i));stream.Write(BitConverter.GetBytes(records[i].Length));stream.Write(records[i]);}
        return Hash(stream.ToArray());
    }
    private static JsonObject RowProfile(byte[] source,int count,bool messages)
    {
        byte[] header=source[..16];header.AsSpan(2,6).Clear();
        byte[][] flags=new byte[count][],rows=new byte[count][];
        for(int i=0;i<count;i++)
        {
            int table=20+i*8,offset=16+BitConverter.ToInt32(source,table),length=BitConverter.ToUInt16(source,table+4)*2;
            flags[i]=source.AsSpan(table+6,2).ToArray();rows[i]=[..source.AsSpan(offset,length),..flags[i]];
        }
        return new(){{"header_sha256",Hash(header)},{"opaque_rows_sha256",Framed(rows,i=>messages||!Ids.Contains(i))},
            {"edited_flags_sha256",Framed(flags,i=>!messages&&Ids.Contains(i))}};
    }
    internal static JsonElement Setup(Fixture f,bool y)
    {
        ModuleChecks.CreateArchives(f);Gen6PersistentItemsChecks.Setup(f,y);
        File.WriteAllBytes(Path.Combine(f.Game.RomFS,"DllBag.cro"),Gen6MintNativeChecks.SyntheticBag());
        File.WriteAllBytes(Path.Combine(f.Game.RomFS,"DllField.cro"),CroIntegrity.Rebuild(CroIntegrityChecks.Cro(7)));
        var items=Enumerable.Range(0,718).Select(i=>Enumerable.Range(0,36).Select(j=>(byte)((i+j*3)&255)).ToArray()).ToArray();
        f.WriteGarc(Path.Combine(f.Game.RomFS,"a/2/2/0"),items,f.Game.GetGARCReference("item"));
        var texts=new JsonObject();
        for(int language=0;language<8;language++)
        {
            var reference=f.Game.GetGARCReference("gametext").GetRelativeGARC(language);
            var lines=Enumerable.Range(0,141).Select(i=>new[]{"Opaque bank "+i}).ToArray();
            lines[47]=Enumerable.Range(0,26).Select(i=>"Nature"+i).ToArray();
            foreach(int bank in Banks)lines[bank]=Enumerable.Range(0,bank==140?179:718).Select(i=>"Original "+language+"/"+bank+"/"+i).ToArray();
            var records=lines.Select(rows=>TextFile.GetBytes(f.Game,rows)).ToArray();
            var bankProfiles=new JsonObject();
            foreach(int bank in Banks)
            {
                for(int i=0;i<lines[bank].Length;i++)records[bank][20+i*8+6]=(byte)(i*7+3);
                bankProfiles[bank.ToString()]=RowProfile(records[bank],lines[bank].Length,bank==140);
            }
            texts[language.ToString()]=new JsonObject{{"opaque_banks_sha256",Framed(records,i=>!Banks.Contains(i))},{"banks",bankProfiles}};
            f.WriteGarc(Path.Combine(f.Game.RomFS,reference.Reference),records,reference);
        }
        var cros=Directory.GetFiles(f.Game.RomFS,"*.cro").Select(File.ReadAllBytes).ToArray();
        byte[] crr=new byte[0x500];BitConverter.GetBytes(0x380).CopyTo(crr,0x350);BitConverter.GetBytes(cros.Length).CopyTo(crr,0x354);
        File.WriteAllBytes(Path.Combine(f.Game.RomFS,".crr/static.crr"),CroIntegrity.RebuildCrr(crr,cros));
        var pins=new JsonObject();
        var paths=new Dictionary<string,string>{{"ncchheader.bin",Path.Combine(f.Root,"HeaderNCCH0.bin")},
            {"exheader.bin",Path.Combine(f.Root,"DecryptedExHeader.bin")},{"ExeFS/.code.bin",Path.Combine(f.Game.ExeFS,".code.bin")},
            {"RomFS/DllBag.cro",Path.Combine(f.Game.RomFS,"DllBag.cro")},{"RomFS/DllBattle.cro",Path.Combine(f.Game.RomFS,"DllBattle.cro")},
            {"RomFS/DllField.cro",Path.Combine(f.Game.RomFS,"DllField.cro")},{"RomFS/a/2/2/0",Path.Combine(f.Game.RomFS,"a/2/2/0")},
            {"RomFS/.crr/static.crr",Path.Combine(f.Game.RomFS,".crr/static.crr")}};
        for(int language=0;language<8;language++)
        {var relative=f.Game.GetGARCReference("gametext").GetRelativeGARC(language).Reference;paths["RomFS/"+relative.Replace('\\','/')]=Path.Combine(f.Game.RomFS,relative);}
        foreach(var (name,path) in paths)pins[name]=Hash(File.ReadAllBytes(path));
        var profile=new JsonObject{{"sha256",pins},{"native",new JsonObject{{"items_opaque_sha256",Framed(items,i=>!Ids.Contains(i))},
            {"original_bag_header_sha256",Hash(File.ReadAllBytes(paths["RomFS/DllBag.cro"])[..128])},{"texts",texts}}}};
        return JsonSerializer.SerializeToElement(profile);
    }
    private static Dictionary<string,string> Snapshot(Fixture f)=>Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Hash(File.ReadAllBytes(p)));
    private static bool Unchanged(Fixture f,Dictionary<string,string> snapshot)=>Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories).Length==snapshot.Count&&snapshot.All(p=>Hash(File.ReadAllBytes(p.Key))==p.Value);
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(bool y in new[]{false,true})
        {
            using var f=new Fixture(GameVersion.XY);string tag=y?"Y":"X";JsonElement profile=Setup(f,y);
            // X deliberately uses the lowercase physical alias: Windows resolves ExHeader.bin
            // to this same file, which must count as one dependency rather than two headers.
            if(!y)File.Move(Path.Combine(f.Root,"DecryptedExHeader.bin"),Path.Combine(f.Root,"exheader.bin"));
            Gen6NatureMintService Service(bool experimental=true)=>new(f.Session,experimental,_=>profile);
            var before=Snapshot(f);var blocked=Service(false).Inspect();
            check(blocked.State==Gen6MintNativeState.Original&&blocked.Proposal is null&&blocked.Report.Contains("Producción bloqueada"),"Gen6 mint service production default blocks proposal "+tag);
            var service=Service();var preview=service.Inspect();
            if(preview.Proposal is not {} proposal)throw new Exception("Synthetic Gen6 mint fixture rejected: "+preview.Report);
            check(preview.State==Gen6MintNativeState.Original&&proposal.Files.Count==11&&Unchanged(f,before)&&f.Session.History.Read().Entries.Count==0,
                "Gen6 mint service preview detached and read-only with eleven files "+tag);
            reject(()=>Service().Apply(preview),"Gen6 mint service foreign owner rejected "+tag);
            reject(()=>Service(false).Apply(preview),"Gen6 mint service production Apply remains blocked "+tag);
            foreach(int failureAt in new[]{1,(proposal.Files.Count+1)/2,proposal.Files.Count})
            {
                reject(()=>FileTransaction.Commit(proposal.Files,index=>{if(index==failureAt)throw new IOException("Synthetic mint replacement failure");}),
                    "Gen6 mint multi-file transaction injected failure "+tag+" index"+failureAt);
                check(Unchanged(f,before)&&f.Session.History.Read().Entries.Count==0,"Gen6 mint rollback restores every byte without history "+tag+" index"+failureAt);
            }
            var alteredFiles=proposal.Files.Select((file,index)=>index==0?new FileChange(file.Path,file.Original(),Mutated(file.Proposed())):file).ToArray();
            var forged=new EditorProposal(proposal.Owner,proposal.Action,proposal.Seed,alteredFiles,proposal.Changes,proposal.Parameters,proposal.Dependencies);
            reject(()=>service.Apply(preview with{Proposal=forged}),"Gen6 mint canonical altered output rejected "+tag);
            var metadata=new Dictionary<string,string>(proposal.Parameters){{"Tampered","true"}};
            forged=new EditorProposal(proposal.Owner,proposal.Action,proposal.Seed,proposal.Files,proposal.Changes,metadata,proposal.Dependencies);
            reject(()=>service.Apply(preview with{Proposal=forged}),"Gen6 mint canonical altered metadata rejected "+tag);
            forged=new EditorProposal(proposal.Owner,proposal.Action,proposal.Seed+1,proposal.Files,proposal.Changes,proposal.Parameters,proposal.Dependencies);
            reject(()=>service.Apply(preview with{Proposal=forged}),"Gen6 mint canonical altered seed rejected "+tag);
            var dependencies=proposal.Dependencies.Select(d=>(d.Path,Fingerprint:(byte[])d.Fingerprint.Clone())).ToArray();dependencies[0].Fingerprint[0]^=1;
            forged=new EditorProposal(proposal.Owner,proposal.Action,proposal.Seed,proposal.Files,proposal.Changes,proposal.Parameters,dependencies);
            reject(()=>service.Apply(preview with{Proposal=forged}),"Gen6 mint canonical altered dependency fingerprint rejected "+tag);
            check(Unchanged(f,before),"Gen6 mint rejected proposals change no source files "+tag);
            string codePath=Path.Combine(f.Game.ExeFS,".code.bin");byte[] code=File.ReadAllBytes(codePath);File.WriteAllBytes(codePath,Mutated(code));
            reject(()=>service.Apply(preview),"Gen6 mint changed code dependency rejects stale preview "+tag);
            File.WriteAllBytes(codePath,code);check(Unchanged(f,before),"Gen6 mint stale dependency rejection performs no extra writes "+tag);
            using(var writer=new FileStream(codePath,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite))
                reject(()=>service.Apply(preview),"Gen6 mint concurrent dependency writer rejects apply "+tag);
            check(Unchanged(f,before),"Gen6 mint concurrent writer rejection preserves source files "+tag);
            service.Apply(preview);var installed=Snapshot(f);var entry=f.Session.History.Read().Entries.Last();
            check(service.Inspect().State==Gen6MintNativeState.Installed&&service.Inspect().Proposal is null&&entry.Files.Count==11&&
                entry.Parameters["ProductionEnabled"]=="false"&&entry.Parameters["ActionId"]==Gen6NatureMintService.ActionId,
                "Gen6 mint install verified and journaled as experimental "+tag);
            check(Directory.GetFiles(f.Game.RomFS).Count(p=>Path.GetFileName(p)=="DllBag.cro")==1&&
                Directory.GetFiles(f.Game.RomFS).All(p=>Path.GetFileName(p)!="DLLBag.cro"),"Gen6 mint physical DllBag case preserved "+tag);
            reject(()=>service.Apply(preview),"Gen6 mint stale already-applied preview rejected "+tag);
            check(Unchanged(f,installed)&&f.Session.History.Read().Entries.Count==1,"Gen6 mint repeated attempt adds no writes or history "+tag);
            var reopened=Fixture.CreateSession(f.Game,f.Root,f.PersonalPath);
            check(new Gen6NatureMintService(reopened,true,_=>profile).Inspect().State==Gen6MintNativeState.Installed,"Gen6 mint reopened session verifies installed resources "+tag);
            string itemPath=Path.Combine(f.Game.RomFS,"a/2/2/0");byte[] itemBytes=File.ReadAllBytes(itemPath);var items=new GARC.MemGARC(itemBytes).Files;
            items[113][0]^=1;File.WriteAllBytes(itemPath,GarcTransaction.Build(itemBytes,f.Game.GetGARCReference("item"),items));
            check(service.Inspect().State==Gen6MintNativeState.Installed,"Gen6 mint installed price remains independently editable "+tag);
            File.WriteAllBytes(itemPath,itemBytes);
            RestorePoints.Undo(f.Session.History,f.Root,entry.Id);
            check(before.All(p=>Hash(File.ReadAllBytes(p.Key))==p.Value)&&service.Inspect().State==Gen6MintNativeState.Original,
                "Gen6 mint history undo restores exact original resources "+tag);
        }
    }
    private static byte[] Mutated(byte[] bytes){var changed=(byte[])bytes.Clone();changed[0]^=1;return changed;}
}
