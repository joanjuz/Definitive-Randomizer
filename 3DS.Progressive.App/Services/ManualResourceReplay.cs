using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;

/// <summary>Lossless local dependencies for resource edits without a semantic record editor.</summary>
internal static class ManualResourceReplay
{
    internal static bool ShouldCapture(string action,IReadOnlyDictionary<string,string> options)
    {
        if(Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY")=="1"||options.ContainsKey("ManualEdits")||options.ContainsKey("ManualOperation"))return false;
        if(options.TryGetValue("ActionId",out var id)&&id.Contains("randomize",StringComparison.OrdinalIgnoreCase))return false;
        if(action is "Editar Pokémon" or "Editar Game Text" or "Editar Story Text")return false;
        if(action.StartsWith("Randomizar",StringComparison.OrdinalIgnoreCase)||action.StartsWith("Restaurar archivos",StringComparison.OrdinalIgnoreCase)||options.ContainsKey("Settings"))return false;
        return action.StartsWith("Editar",StringComparison.Ordinal)||action.StartsWith("Guardar",StringComparison.Ordinal)||action.StartsWith("Reemplazar",StringComparison.Ordinal)||action.StartsWith("Sustituir",StringComparison.Ordinal)||action.StartsWith("Importar",StringComparison.Ordinal)||action.StartsWith("Aplicar recetas CRO",StringComparison.Ordinal)||action.StartsWith("Aplicar USUM Relearner",StringComparison.Ordinal)||action.StartsWith("Restaurar Shiny",StringComparison.Ordinal)||action.StartsWith("Instalar Player Level Caps",StringComparison.Ordinal);
    }
    private static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));
    private static JsonObject Asset(byte[] bytes,string folder)
    {
        string hash=Hash(bytes),name=hash+".bin";Directory.CreateDirectory(folder);string path=Path.Combine(folder,name);
        if(!File.Exists(path))File.WriteAllBytes(path,bytes);
        if(Hash(File.ReadAllBytes(path))!=hash)throw new InvalidDataException("Dependencia manual con hash distinto.");
        return new JsonObject{["Name"]=name,["Sha256"]=hash,["Directory"]=folder};
    }
    internal static JsonObject Capture(string root,string storage,IReadOnlyList<FileChange> files)
    {
        string folder=Path.Combine(storage,"manual-assets");var entries=new JsonArray();
        foreach(var file in files)
        {
            string relative=Path.GetRelativePath(root,file.Path);_=SafePath(root,relative);
            byte[] before=file.Original(),after=file.Proposed();
            if(before.Length>=4&&BitConverter.ToUInt32(before)==0x47415243&&after.Length>=4&&BitConverter.ToUInt32(after)==0x47415243)
            {
                var a=new GARC.LazyGARC(before);var b=new GARC.LazyGARC(after);
                if(a.FileCount!=b.FileCount)throw new InvalidDataException("Cambió la cantidad de miembros GARC; se requiere un adaptador explícito.");
                for(int i=0;i<a.FileCount;i++)if(!a[i].SequenceEqual(b[i]))entries.Add(new JsonObject{["Path"]=relative,["Mode"]="garc",["Index"]=i,["Count"]=a.FileCount,["BeforeHash"]=Hash(a[i]),["Asset"]=Asset(b[i],folder)});
            }
            else if(before.Length==after.Length&&!Path.GetExtension(relative).Equals(".moflex",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileName(relative).Equals("icon.bin",StringComparison.OrdinalIgnoreCase))
            {
                var blocks=new JsonArray();
                bool cro=before.Length>0x84&&before.AsSpan(0x80,4).SequenceEqual("CRO0"u8);
                for(int i=cro?0x80:0;i<before.Length;i+=4)
                {
                    int count=Math.Min(4,before.Length-i);
                    if(before.AsSpan(i,count).SequenceEqual(after.AsSpan(i,count)))continue;
                    blocks.Add(new JsonObject{["Offset"]=i,["Before"]=Convert.ToHexString(before.AsSpan(i,count)),["After"]=Convert.ToHexString(after.AsSpan(i,count))});
                }
                entries.Add(new JsonObject{["Path"]=relative,["Mode"]="blocks",["Length"]=before.Length,["Blocks"]=blocks});
            }
            else entries.Add(new JsonObject{["Path"]=relative,["Mode"]="file",["BeforeHash"]=Hash(before),["Asset"]=Asset(after,folder)});
        }
        return new JsonObject{["Kind"]="resources",["Key"]="resources:"+Guid.NewGuid().ToString("N"),["Payload"]=new JsonObject{["Files"]=entries}};
    }
    internal static string SafePath(string root,string relative)
    {
        if(Path.IsPathRooted(relative)||relative.Split(['\\','/']).Any(part=>part==".."))throw new InvalidDataException("Ruta manual fuera del proyecto.");
        string full=Path.GetFullPath(Path.Combine(root,relative));
        if(!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Ruta manual no segura.");
        string? at=Path.GetDirectoryName(full);
        while(at is not null&&at.Length>Path.GetFullPath(root).Length){if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("La ruta manual atraviesa un enlace.");at=Path.GetDirectoryName(at);}
        if(File.Exists(full)&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Archivo manual enlazado.");
        return full;
    }
    private static string ResolvePath(GameProjectSession project,string relative)
    {
        string path=SafePath(project.ProjectRoot,relative);
        if(!File.Exists(path)&&(Path.GetFileName(path).Equals("code.bin",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path).Equals(".code.bin",StringComparison.OrdinalIgnoreCase))&&string.Equals(Path.GetDirectoryName(path),Path.GetFullPath(project.GameConfiguration.ExeFS),StringComparison.OrdinalIgnoreCase))
        {
            var aliases=Directory.GetFiles(project.GameConfiguration.ExeFS).Where(f=>Path.GetFileName(f).Equals("code.bin",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(f).Equals(".code.bin",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(aliases.Length!=1)throw new InvalidDataException("No existe un code.bin único para la modificación manual.");
            path=SafePath(project.ProjectRoot,Path.GetRelativePath(project.ProjectRoot,aliases[0]));
        }
        return path;
    }
    internal static byte[] ReadAsset(JsonObject asset,GlobalTemplateDocument doc)
    {
        string name=asset["Name"]!.GetValue<string>(),hash=asset["Sha256"]!.GetValue<string>();
        if(name!=Path.GetFileName(name)||name!=hash+".bin"||hash.Length!=64||!hash.All(Uri.IsHexDigit))throw new InvalidDataException("Dependencia manual no válida.");
        string directory=asset["Directory"]!.GetValue<string>();
        if(!Path.IsPathRooted(directory))directory=Path.Combine(Path.GetDirectoryName(doc.SourcePath)??throw new InvalidDataException("Falta la ruta del template para sus dependencias."),directory);
        string path=Path.Combine(directory,name);if(!File.Exists(path))throw new FileNotFoundException("Falta una dependencia manual del template.",path);
        var bytes=File.ReadAllBytes(path);if(Hash(bytes)!=hash)throw new InvalidDataException("SHA-256 distinto en dependencia manual: "+name);return bytes;
    }
    internal static void Apply(GameProjectSession project,GlobalTemplateDocument doc,JsonObject payload,bool validateOnly)
    {
        var pending=new Dictionary<string,(byte[] Before,byte[] After)>(StringComparer.OrdinalIgnoreCase);
        foreach(var node in payload["Files"]!.AsArray())
        {
            var file=node!.AsObject();string path=ResolvePath(project,file["Path"]!.GetValue<string>());
            if(!pending.TryGetValue(path,out var pair)){var bytes=File.ReadAllBytes(path);pair=(bytes,(byte[])bytes.Clone());}
            switch(file["Mode"]!.GetValue<string>())
            {
                case "blocks":
                    if(pair.After.Length!=file["Length"]!.GetValue<int>())throw new InvalidDataException("El tamaño del recurso manual cambió: "+path);
                    foreach(var block in file["Blocks"]!.AsArray())
                    {
                        int offset=block!["Offset"]!.GetValue<int>();byte[] before=Convert.FromHexString(block["Before"]!.GetValue<string>()),after=Convert.FromHexString(block["After"]!.GetValue<string>());
                        if(offset<0||after.Length!=before.Length||after.Length is <1 or >4||offset>pair.After.Length-after.Length)throw new InvalidDataException("Bloque manual fuera de límites.");
                        if(!pair.After.AsSpan(offset,before.Length).SequenceEqual(before)&&!pair.After.AsSpan(offset,after.Length).SequenceEqual(after))throw new InvalidDataException("Conflicto en un bloque manual de "+Path.GetFileName(path)+$" (+0x{offset:X}). La receta no se aplica sobre bytes desconocidos.");
                        after.CopyTo(pair.After,offset);
                    }
                    break;
                case "garc":
                    var garc=new GARC.LazyGARC(pair.After);int index=file["Index"]!.GetValue<int>();
                    if(garc.FileCount!=file["Count"]!.GetValue<int>()||index<0||index>=garc.FileCount)throw new InvalidDataException("Miembro GARC manual incompatible.");
                    var data=ReadAsset(file["Asset"]!.AsObject(),doc);garc[index]=data;pair.After=garc.Save();
                    if(!new GARC.LazyGARC(pair.After)[index].SequenceEqual(data))throw new InvalidDataException("No coincide el recurso GARC reemplazado.");break;
                case "file":
                    var replacement=ReadAsset(file["Asset"]!.AsObject(),doc);
                    if(Hash(pair.After)!=file["BeforeHash"]!.GetValue<string>()&&!pair.After.SequenceEqual(replacement))throw new InvalidDataException("El recurso completo cambió antes de su reemplazo: "+path);
                    pair.After=replacement;break;
                default:throw new InvalidDataException("Tipo de recurso manual desconocido.");
            }
            pending[path]=pair;
        }
        if(validateOnly)return;
        foreach(string path in pending.Keys.ToArray())
        {
            var pair=pending[path];if(pair.After.Length>0x84&&pair.After.AsSpan(0x80,4).SequenceEqual("CRO0"u8))pending[path]=(pair.Before,CroIntegrity.Rebuild(pair.After));
        }
        var recorded=(JsonObject)payload.DeepClone();
        foreach(var file in recorded["Files"]!.AsArray())if(file?["Asset"] is JsonObject asset)
        {
            string directory=asset["Directory"]!.GetValue<string>();if(!Path.IsPathRooted(directory))asset["Directory"]=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc.SourcePath)!,directory));
        }
        project.History.Commit(pending.Select(p=>new FileChange(p.Key,p.Value.Before,p.Value.After)).ToArray(),"Modificaciones manuales","Reproducir recursos manuales",[],new Dictionary<string,string>{{"ManualOperation",new JsonObject{["Kind"]="resources",["Key"]="resources:"+Guid.NewGuid(),["Payload"]=recorded}.ToJsonString()}});
    }
    internal static void ExportDependencies(GlobalTemplateDocument doc,string target)
    {
        var native=doc.Section("ProgressiveApp") as JsonObject;if(native?["ManualEdits"]?["Operations"] is not JsonArray operations)return;
        string folder=Path.GetFileNameWithoutExtension(target)+".assets",directory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!,folder);
        var assets=operations.Where(o=>o?["Kind"]?.GetValue<string>()=="resources").SelectMany(o=>o!["Payload"]!["Files"]!.AsArray()).Select(f=>f?["Asset"] as JsonObject).OfType<JsonObject>()
            .Concat(operations.Where(o=>o?["Kind"]?.GetValue<string>()=="authored-template").Select(o=>o!["Payload"]!["Asset"]!.AsObject()));
        foreach(var asset in assets)
        {
            var bytes=ReadAsset(asset,doc);Directory.CreateDirectory(directory);string path=Path.Combine(directory,asset["Name"]!.GetValue<string>());
            if(File.Exists(path)){if(!File.ReadAllBytes(path).SequenceEqual(bytes))throw new IOException("Dependencia de destino distinta; no se sobrescribió.");}else File.WriteAllBytes(path,bytes);
            asset["Directory"]=folder;
        }
        doc.SetSection("ProgressiveApp",native);
    }
}
