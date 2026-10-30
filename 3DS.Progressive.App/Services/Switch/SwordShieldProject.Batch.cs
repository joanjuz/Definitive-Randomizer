using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed record SwitchBatchOutput(string Folder,string ManifestSha256);
internal sealed partial class SwordShieldProject
{
    internal IReadOnlyList<SwitchBatchOutput> BatchGlobal(JsonObject document,int count,string destination,string name,SwitchModLayout layout,IProgress<string>? progress=null,CancellationToken token=default)
    {
        RequireEditable();VerifySource();_ = ValidateGlobal(document);if(count is <1 or >50)throw new ArgumentOutOfRangeException(nameof(count));if(string.IsNullOrWhiteSpace(name)||name.Length>80||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Path.GetFileName(name)!=name)throw new ArgumentException("Nombre base inválido.");
        string folder=Path.GetFullPath(destination);CheckNoLinks(folder);if(!Directory.Exists(folder)||Contains(Root,folder))throw new IOException("Selecciona una carpeta existente fuera de la extracción.");
        if(layout is not SwitchModLayout.Atmosphere and not SwitchModLayout.Ryujinx)throw new ArgumentOutOfRangeException(nameof(layout));
        var targets=Enumerable.Range(1,count).Select(i=>Path.Combine(folder,name+"_"+i.ToString("00"))).ToArray();string manifest=Path.Combine(folder,name+"_switch-batch-manifest.json");if(File.Exists(manifest)||Directory.Exists(manifest)||targets.Any(p=>File.Exists(p)||Directory.Exists(p)))throw new IOException("El destino contiene salidas con esos nombres. No se sobrescribirá nada.");
        var recipe=(JsonObject)document.DeepClone();var outputs=new List<SwitchBatchOutput>();var entries=new JsonArray();bool ownsManifest=false;foreach(string target in targets)entries.Add(new JsonObject{["Folder"]=target,["Status"]="pending"});var log=new JsonObject{["Format"]="DefinitiveRandomizer.SwitchBatch",["Version"]=1,["TitleId"]=Identity.TitleId,["BuildId"]=Identity.BuildId,["Recipe"]=recipe.DeepClone(),["Status"]="running",["Outputs"]=entries};
        void Save(){string temp=manifest+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,log.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,manifest,ownsManifest);ownsManifest=true;}finally{if(File.Exists(temp))File.Delete(temp);}}
        Save();try
        {
            for(int i=0;i<count;i++)
            {
                token.ThrowIfCancellationRequested();((JsonObject)entries[i]!)["Status"]="running";Save();VerifySource();progress?.Report($"Mod {i+1} de {count}: preparando receta…");var fresh=Open(Root);var preview=fresh.PreviewGlobal(recipe,token);fresh.ApplyGlobal(preview);token.ThrowIfCancellationRequested();var exported=fresh.Export(targets[i],layout);
                string applied=Path.Combine(exported.Folder,"switch-recipe-applied.json");File.WriteAllText(applied,preview.AppliedRecipe.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));string hash=Hash(File.ReadAllBytes(Path.Combine(exported.Folder,"definitive-mod.json")));outputs.Add(new(exported.Folder,hash));var entry=(JsonObject)entries[i]!;entry["Status"]="completed";entry["ManifestSha256"]=hash;entry["AppliedRecipe"]=Path.GetFileName(applied);entry["AppliedRecipeSha256"]=Hash(File.ReadAllBytes(applied));Save();progress?.Report($"Mod {i+1} de {count}: exportado y verificado.");
            }
            log["Status"]="completed";Save();return outputs.AsReadOnly();
        }
        catch(Exception ex){log["Status"]=ex is OperationCanceledException?"cancelled":"failed";log["Error"]=ex.Message;try{Save();}catch(Exception persist){throw new AggregateException("El trabajo falló y tampoco se pudo actualizar el manifiesto.",ex,persist);}throw;}
    }
}
