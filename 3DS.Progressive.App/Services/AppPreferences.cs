using System.IO;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

/// <summary>Local-only defaults. Never stored inside RomFS or sent to third parties.</summary>
internal sealed class AppPreferences
{
    public string DefaultProjectFolder {get;set;}="";
    public string DefaultBackupFolder {get;set;}="";
    private static readonly string FilePath=Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "3DS-Progressive","settings.json");
    internal static AppPreferences Load()
    {
        try
        {
            return File.Exists(FilePath)?JsonSerializer.Deserialize<AppPreferences>(
                File.ReadAllText(FilePath))??new():new();
        }
        catch(IOException){return new();}
        catch(JsonException){return new();}
    }
    internal void Save()
    {
        foreach(var path in new[]{DefaultProjectFolder,DefaultBackupFolder})
            if(!string.IsNullOrWhiteSpace(path)&&!Directory.Exists(path))
                throw new DirectoryNotFoundException("La carpeta indicada no existe: "+path);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string tmp=FilePath+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(tmp,FilePath,true);
    }
}
