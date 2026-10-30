using System.IO;
using pk3DS.Core.NX;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed partial class SwordShieldProject
{
    private IReadOnlyList<string>? _englishTrainerNames;
    internal string TrainerKind(int id)
    {
        if(_englishTrainerNames is null) {string path=Path.Combine(Root,"romfs/bin/message/English/common/trname.dat");if(File.Exists(path)){CheckNoLinks(path);_englishTrainerNames=SwordShieldText.Read(File.ReadAllBytes(path));}else _englishTrainerNames=Array.Empty<string>();}
        string name=id>=0&&id<_englishTrainerNames.Count?_englishTrainerNames[id]:TrainerName(id);
        return ClassifyTrainerName(name);
    }
    internal static string ClassifyTrainerName(string name)
    {
        var words=System.Text.RegularExpressions.Regex.Split(name.ToLowerInvariant(),@"[^\p{L}]+");
        if(words.Any(w=>new[]{"hop","paul","marnie","roxy","bede","berto"}.Contains(w)))return "Importantes · Rivales";
        if(words.Any(w=>new[]{"milo","percy","nessa","cathy","kabu","bea","judith","allister","alistair","opal","sally","gordie","morris","melony","mel","piers","nerio","raihan","roy"}.Contains(w)))return "Jefes · Líderes de gimnasio";
        if(words.Any(w=>new[]{"leon","lionel","rose","oleana","olivia","sordward","tizonio","shielbert","dargo","klara","sophora","avery","dreo","mustard","mostaz","peony"}.Contains(w)))return "Importantes · Otros jefes";
        return "Normal";
    }
}
