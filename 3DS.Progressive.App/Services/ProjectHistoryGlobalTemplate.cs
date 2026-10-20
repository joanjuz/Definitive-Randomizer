using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace ThreeDS.Progressive.Services;

public sealed partial class ProjectHistory
{
    internal void CommitGlobalTemplate(IReadOnlyList<FileChange> files,GlobalTemplateDocument template,int seed,string expectedHistoryHash)
    {
        lock(_gate)
        {
            RecoverPending();var current=Read();
            if(Hash(JsonSerializer.SerializeToUtf8Bytes(current))!=expectedHistoryHash)throw new IOException("El historial cambió desde la vista previa.");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            if(!File.Exists(_path))File.WriteAllBytes(_path,JsonSerializer.SerializeToUtf8Bytes(current,Json));
            byte[] original=File.ReadAllBytes(_path);var entries=new List<HistoryEntry>(current.Entries);var batch=Guid.NewGuid().ToString("N");
            var changed=files.Where(file=>file.Changed).ToArray();
            var resultFiles=changed.Select(file=>new HistoryFile(Path.GetRelativePath(_root,file.Path),Hash(file.Original()),Hash(file.Proposed()))).ToArray();
            entries.Add(new(Guid.NewGuid(),DateTimeOffset.UtcNow,"Global Template","Aplicar Global Template · "+template.Name,[],new Dictionary<string,string>{{"GlobalTemplateSnapshot",template.ToJson()},{"GlobalTemplateSource",template.SourcePath??""},{"GlobalApplication",batch},{"TemplateSha256",template.Sha256},{"PreviewSeed",seed.ToString(System.Globalization.CultureInfo.InvariantCulture)}},resultFiles,"Action"));
            var actions=template.Section("Actions")!.AsArray().OfType<JsonObject>().ToDictionary(row=>row["Id"]!.GetValue<string>(),StringComparer.OrdinalIgnoreCase);
            foreach(string id in template.Actions)
            {
                // Execution is grouped by the original engine. Do not invent per-action binary diffs.
                // Each row attests to the shared final result; the preceding row owns the actual transaction.
                var final=resultFiles.Select(file=>file with{BeforeSha256=file.AfterSha256}).ToArray();
                var parameters=new Dictionary<string,string>{{"ActionId",id},{"ActionParameters",actions[id]["Parameters"]?.ToJsonString()??"{}"},{"GlobalApplication",batch},{"TemplateSha256",template.Sha256},{"ResultScope","Resultado conjunto del Global Template; diferencias no aisladas por acción"}};
                entries.Add(new(Guid.NewGuid(),DateTimeOffset.UtcNow,"Global Template",id,[],parameters,final,"Action"));
            }
            var proposed=JsonSerializer.SerializeToUtf8Bytes(current with {Version=2,Entries=entries},Json);
            FileChange[] transaction=[..changed,new FileChange(_path,original,proposed)];
            using var recovery=RecoveryJournal.Begin(_path+".pending",_root,_path,transaction);
            FileTransaction.Commit(transaction,recovery:recovery);
        }
    }
}
