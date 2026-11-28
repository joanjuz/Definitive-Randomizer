using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Deterministic bootstrap before the legacy randomizers; never consumes their RNG.</summary>
public static class Gen6MintRecipeBridge
{
    internal static readonly string[] ActionIds=[GameGen6PersistentItemsService.ActionId,Gen6MintExecutionContract.InstallAction,Gen6MintExecutionContract.IconsAction];
    internal static void CaptureSelection(GlobalTemplateDocument doc,IReadOnlySet<string> selected,bool marts,bool field)
    {
        var actions=(JsonArray)doc.Section("Actions")!;
        foreach(string id in ActionIds.Where(selected.Contains))if(!doc.Actions.Contains(id,StringComparer.OrdinalIgnoreCase))
            actions.Add(new JsonObject{{"Id",id},{"Parameters",id==GameGen6PersistentItemsService.ActionId?new JsonObject{{"Enable","1"}}:new JsonObject()}});
        doc.SetSection("Actions",actions);Gen6MintItemPolicy.Capture(doc,marts,field);ValidateDocument(doc);
    }
    internal static bool Selected(GlobalTemplateDocument doc)
    {var o=Gen6MintExecutionContract.ReadOptions(doc.ToJson());return o.InstallMints||o.InstallIcons||o.IncludeMarts||o.IncludeFieldItems||doc.Actions.Contains(GameGen6PersistentItemsService.ActionId,StringComparer.OrdinalIgnoreCase);}
    internal static void ValidateDocument(GlobalTemplateDocument doc)
    {
        _=Gen6MintExecutionContract.ReadOptions(doc.ToJson());
        if(!Selected(doc))return;
        if(doc.Generation!=6||!doc.Game.Equals("XY",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Estas opciones requieren Pokémon X/Y.");
        foreach(var row in (JsonArray)doc.Section("Actions")!)
        {
            string id=row!["Id"]!.GetValue<string>();if(!ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase))continue;
            var parameters=(JsonObject)row["Parameters"]!;
            if(id.Equals(GameGen6PersistentItemsService.ActionId,StringComparison.OrdinalIgnoreCase))
            {if(parameters.Any(p=>p.Key!="Enable")||parameters["Enable"] is {} enabled&&enabled.ToString()!="1")throw new InvalidDataException("Persistent Items X/Y requiere Enable=1.");}
            else if(parameters.Count!=0)throw new InvalidDataException("La instalación nativa de mentas/iconos no admite parámetros opacos.");
        }
    }
    public static void Validate(string root,string json)
    {var p=GameProjectSession.Open(root,false);ValidateSession(p,GlobalTemplateDocument.Parse(json,p.GameName,p.Generation));}
    internal static void ValidateSession(GameProjectSession p,GlobalTemplateDocument doc)
    {
        ValidateDocument(doc);if(!Selected(doc))return;var o=Gen6MintExecutionContract.ReadOptions(doc.ToJson());
        bool mints=o.InstallMints||o.InstallIcons||o.IncludeMarts||o.IncludeFieldItems;
        if(mints)
        {
            var state=new Gen6NatureMintService(p).Inspect();
            if(state.State==Gen6MintNativeState.Original)
            {if(!o.InstallMints)throw new InvalidDataException("Selecciona instalar las mentas o parte de una instalación acreditada.");}
            else _=Gen6MintRuntimeDependencies.Capture(p);
        }
        else
        {var state=GameGen6PersistentItemsService.Inspect(p);if(!state.Allowed)throw new InvalidDataException(state.Reason);}
    }
    public static void Bootstrap(string root,string json)
    {var p=GameProjectSession.Open(root,false);Bootstrap(p,GlobalTemplateDocument.Parse(json,p.GameName,p.Generation));}
    internal static void Bootstrap(GameProjectSession p,GlobalTemplateDocument doc)
    {
        ValidateSession(p,doc);if(!Selected(doc))return;var o=Gen6MintExecutionContract.ReadOptions(doc.ToJson());
        if(o.InstallMints)
        {
            var service=new Gen6NatureMintService(p,true);var preview=service.Inspect();
            if(preview.State==Gen6MintNativeState.Original)service.Apply(preview);
            else _=Gen6MintRuntimeDependencies.Capture(p);
        }
        if(o.InstallIcons)
        {
            if(Gen6MintIconService.Mapping(File.ReadAllBytes(Path.Combine(p.GameConfiguration.ExeFS,".code.bin")),true))_=Gen6MintRuntimeDependencies.Capture(p);
            else
            {
                var service=new Gen6MintIconService(GameProjectSession.Open(p.ProjectRoot,false));var preview=Task.Run(()=>service.PrepareAsync()).GetAwaiter().GetResult();
                if(preview.State==Gen6MintNativeState.Original&&preview.Proposal is not null)service.Apply(preview);
                else if(preview.State!=Gen6MintNativeState.Installed)throw new InvalidDataException(preview.Report);
            }
        }
        if(doc.Actions.Contains(GameGen6PersistentItemsService.ActionId,StringComparer.OrdinalIgnoreCase))
        {
            var current=GameProjectSession.Open(p.ProjectRoot,false);Gen6PersistentItemsProfile profile;Gen6MintInstallation? runtime=null;
            if(o.InstallMints||o.InstallIcons||o.IncludeMarts||o.IncludeFieldItems)
            {
                var evidence=runtime=Gen6MintRuntimeDependencies.Capture(current);profile=Gen6PersistentItemsProfile.Known(evidence.TitleId==0x0004000000055E00UL);
                var pins=new Dictionary<string,string>();
                foreach(var pin in profile.Pins)
                {
                    string path=pin.Key switch{
                        "HeaderNCCH0.bin"=>Gen6MintRuntimeDependencies.Header(current.ProjectRoot,"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"),
                        "DecryptedExHeader.bin"=>Gen6MintRuntimeDependencies.Header(current.ProjectRoot,"exheader.bin","DecryptedExHeader.bin","ExHeader.bin"),
                        _ when pin.Key.StartsWith("ExtractedRomFS/")=>Path.Combine(current.GameConfiguration.RomFS,pin.Key[15..]),
                        _=>Path.Combine(current.GameConfiguration.ExeFS,pin.Key[15..])};
                    pins[pin.Key]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                }
                evidence.VerifyUnchanged();profile=profile with{Pins=pins};
            }
            else profile=Gen6PersistentItemsProfile.Known(Gen6MintRuntimeDependencies.Title(current)==0x0004000000055E00UL);
            var pi=new GameGen6PersistentItemsService(current,profile);var proposal=pi.Preview(new Dictionary<string,int>{{"Enable",1}},[1]);
            var changed=proposal.Files.Where(f=>f.Changed).ToList();
            if(changed.Count>0)
            {
                string crrPath=Path.GetFullPath(Path.Combine(current.GameConfiguration.RomFS,".crr/static.crr"));byte[] crr=File.ReadAllBytes(crrPath);
                var images=Directory.GetFiles(current.GameConfiguration.RomFS).Where(f=>Path.GetExtension(f).Equals(".cro",StringComparison.OrdinalIgnoreCase))
                    .Select(path=>changed.FirstOrDefault(c=>c.Path.Equals(path,StringComparison.OrdinalIgnoreCase))?.Proposed()??File.ReadAllBytes(path)).ToArray();
                changed.Add(new(crrPath,crr,CroIntegrity.RebuildCrr(crr,images)));
                runtime?.VerifyUnchanged();
                using var held=MintItemDependency.Lock(current,MergeDependencies(proposal.Dependencies,runtime),changed);
                current.History.Commit(changed,pi.Title,proposal.Action,proposal.Changes,proposal.Parameters,"Mechanics");
            }
        }
        if(o.InstallMints||o.InstallIcons||o.IncludeMarts||o.IncludeFieldItems)_=Gen6MintRuntimeDependencies.Capture(GameProjectSession.Open(p.ProjectRoot,false));
    }
    public static void FinalizeRecipe(string root,string json)
    {var p=GameProjectSession.Open(root,false);FinalizeRecipe(p,GlobalTemplateDocument.Parse(json,p.GameName,p.Generation));}
    internal static void FinalizeRecipe(GameProjectSession p,GlobalTemplateDocument doc)
    {
        if(!Selected(doc))return;
        var integrity=new GameCroIntegrityService(p);integrity.Apply(integrity.Preview(updateCrr:true));
        var o=Gen6MintExecutionContract.ReadOptions(doc.ToJson());if(o.InstallMints||o.InstallIcons||o.IncludeMarts||o.IncludeFieldItems)_=Gen6MintRuntimeDependencies.Capture(p);
        if(doc.Actions.Contains(GameGen6PersistentItemsService.ActionId,StringComparer.OrdinalIgnoreCase)&&Gen6PersistentItemsPatcher.Inspect(File.ReadAllBytes(Path.Combine(p.GameConfiguration.RomFS,"DllBattle.cro")))!=Gen6PersistentItemsPatcher.State.Installed)
            throw new InvalidDataException("Persistent Items no permanece instalado.");
    }
    internal static EditorProposal WithEvidence(EditorProposal proposal,Gen6MintInstallation? evidence,string key,bool include)
    {
        var parameters=new Dictionary<string,string>(proposal.Parameters){{key,include?"true":"false"}};
        var dependencies=MergeDependencies(proposal.Dependencies,evidence);
        return new(proposal.Owner,proposal.Action,proposal.Seed,proposal.Files,proposal.Changes,parameters,dependencies);
    }
    private static (string Path,byte[] Fingerprint)[] MergeDependencies(IReadOnlyList<(string Path,byte[] Fingerprint)> original,Gen6MintInstallation? evidence)
    {
        return original.Concat(evidence?.Dependencies.Select(p=>(p.Key,Convert.FromHexString(p.Value)))??[])
            .GroupBy(p=>Path.GetFullPath(p.Item1),StringComparer.OrdinalIgnoreCase).Select(g=>
            {
                byte[] first=g.First().Item2;
                if(g.Any(d=>!d.Item2.AsSpan().SequenceEqual(first)))throw new IOException("Una dependencia cambió mientras se preparaba la operación: "+g.Key);
                return(Path:g.Key,Fingerprint:first);
            }).ToArray();
    }
}
