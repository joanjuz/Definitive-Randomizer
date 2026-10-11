using System.Globalization;
using ThreeDS.Progressive.Services;

namespace ThreeDS.Progressive.Models;

public sealed record RecordEntry(int Id,string Name) {public string Display=>$"{Name} · #{Id:000}";}
public sealed record EditorField(string Key,string Label,int Value,int Min,int Max,IReadOnlyList<MoveChoice>? Choices=null);
public sealed record EditorGroup(string Name,IReadOnlyList<EditorField> Fields,bool Expanded=true);
public sealed class EditorProposal
{
    internal Guid Owner {get;}
    internal IReadOnlyList<FileChange> Files {get;}
    public string Action {get;}
    public int Seed {get;}
    public IReadOnlyList<BatchChange> Changes {get;}
    internal IReadOnlyDictionary<string,string> Parameters {get;}
    internal EditorProposal(Guid owner,string action,int seed,IEnumerable<FileChange> files,
        IEnumerable<BatchChange> changes,IReadOnlyDictionary<string,string> parameters)
    {Owner=owner;Action=action;Seed=seed;Files=files.ToArray();Changes=changes.ToArray();Parameters=new Dictionary<string,string>(parameters);}
}
public interface IProjectEditor
{
    string Title {get;}
    IReadOnlyList<RecordEntry> Entries {get;}
    IReadOnlyList<EditorGroup> Read(int id);
    void Save(int id,IReadOnlyDictionary<string,int> values);
    IReadOnlyList<EditorGroup> RandomOptions {get;}
    EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope);
    void Apply(EditorProposal proposal);
}
internal static class EditorValues
{
    internal static Dictionary<string,int> Flatten(IEnumerable<EditorGroup> groups)=>groups.SelectMany(g=>g.Fields).ToDictionary(f=>f.Key,f=>f.Value);
    internal static void Validate(IEnumerable<EditorGroup> groups,IReadOnlyDictionary<string,int> values)
    {
        foreach(var field in groups.SelectMany(g=>g.Fields))
            if(!values.TryGetValue(field.Key,out int v)||v<field.Min||v>field.Max)
                throw new ArgumentException($"{field.Label}: debe estar entre {field.Min} y {field.Max}.");
    }
    internal static IReadOnlyList<BatchChange> Diff(int id,string name,IEnumerable<EditorGroup> before,IEnumerable<EditorGroup> after)
    {
        var old=before.SelectMany(g=>g.Fields).ToDictionary(f=>f.Key);
        return after.SelectMany(g=>g.Fields).Where(f=>old[f.Key].Value!=f.Value)
            .Select(f=>new BatchChange(id,name,f.Label,Label(old[f.Key]),Label(f))).ToArray();
    }
    private static string Label(EditorField f)=>f.Choices?.FirstOrDefault(c=>c.Id==f.Value)?.Display??f.Value.ToString(CultureInfo.InvariantCulture);
}
