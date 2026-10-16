using System.IO;
using System.Globalization;
using System.Text.Json.Nodes;

namespace ThreeDS.Progressive.Services;

internal sealed record ShinyRateOptions(int Rerolls,bool Everything);
internal static class ShinyRateTemplate
{
    internal static ShinyRateOptions? Read(GlobalTemplateDocument document)
    {
        var action=((JsonArray)document.Section("Actions")!).FirstOrDefault(a=>string.Equals(a?["Id"]?.GetValue<string>(),"shiny-rate.apply",StringComparison.OrdinalIgnoreCase));
        if(action is null)return null;
        var parameters=action["Parameters"] as JsonObject;
        if(parameters is null||!parameters.Select(p=>p.Key).ToHashSet().SetEquals(new[]{"rerolls","everythingShiny"})||
           !int.TryParse(parameters["rerolls"]?.GetValue<string>(),NumberStyles.None,CultureInfo.InvariantCulture,out int rolls)||rolls is <1 or >9000||
           !bool.TryParse(parameters["everythingShiny"]?.GetValue<string>(),out bool everything))
            throw new InvalidDataException("Shiny Rate requiere rerolls (1–9000) y everythingShiny explícitos y válidos.");
        return new(rolls,everything);
    }
    internal static void Capture(GlobalTemplateDocument document,ShinyRateOptions state)
    {
        if(state.Rerolls is <1 or >9000)throw new InvalidDataException("Rerolls permitidos: 1–9000.");
        var actions=(JsonArray)document.Section("Actions")!;
        var action=actions.FirstOrDefault(a=>string.Equals(a?["Id"]?.GetValue<string>(),"shiny-rate.apply",StringComparison.OrdinalIgnoreCase)) as JsonObject;
        if(action is null){action=new JsonObject{["Id"]="shiny-rate.apply"};actions.Add(action);}
        action["Parameters"]=new JsonObject{["rerolls"]=state.Rerolls.ToString(CultureInfo.InvariantCulture),["everythingShiny"]=state.Everything.ToString()};
        document.SetSection("Actions",actions);
    }
}
