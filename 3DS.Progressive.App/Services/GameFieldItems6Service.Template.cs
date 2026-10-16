using System.IO;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;
public sealed partial class GameFieldItems6Service
{
    private readonly FieldItemTemplate _itemTemplate=new();
    public IReadOnlyList<EditorGroup> RandomOptions=>_itemTemplate.Apply(BaseRandomOptions);
    public void ImportOptions(string path){if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Plantilla demasiado grande.");RestoreOptions(File.ReadAllText(path));}
    public void ExportOptions(string path,IReadOnlyDictionary<string,int> options)=>File.WriteAllText(path,CaptureOptions(options));
    public string CaptureOptions(IReadOnlyDictionary<string,int> options){EditorValues.Validate(RandomOptions,options);return _itemTemplate.Capture(6,options);}
    public void RestoreOptions(string json)=>_itemTemplate.Restore(json,6,_project.ItemNames.Count,BaseRandomOptions);
}
