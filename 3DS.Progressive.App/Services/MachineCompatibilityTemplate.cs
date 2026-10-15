using System.IO;
using System.Text.Json.Nodes;

namespace ThreeDS.Progressive.Services;

internal sealed record MachineCompatibilityState(bool Sanity,bool Follow);
internal static class MachineCompatibilityTemplate
{
    private static readonly string[] Ids=["tms.sanity","tms.follow-evolutions"];
    internal static MachineCompatibilityState? Read(GlobalTemplateDocument document)
    {
        var node=document.Section("ProgressiveApp")?["MachineCompatibility"];
        if(node is not null&&node is not JsonObject)throw new InvalidDataException("Snapshot de compatibilidad MT/MO inválido.");
        if(node is not JsonObject snapshot)
            return document.Actions.Any(id=>Ids.Contains(id,StringComparer.OrdinalIgnoreCase))?
                new(document.Actions.Contains(Ids[0],StringComparer.OrdinalIgnoreCase),document.Actions.Contains(Ids[1],StringComparer.OrdinalIgnoreCase)):null;
        if(document.Generation!=6||snapshot["Scope"]?.GetValue<int>()!=0||snapshot["Options"] is not JsonObject options||options.Count!=2||!options.ContainsKey("Sanity")||!options.ContainsKey("Follow"))
            throw new InvalidDataException("Compatibilidad MT/MO requiere Gen6, alcance Todos y opciones Sanity/Follow completas.");
        int sanity=options["Sanity"]?.GetValue<int>()??-1,follow=options["Follow"]?.GetValue<int>()??-1;
        if(sanity is <0 or >1||follow is <0 or >1)throw new InvalidDataException("Sanity/Follow debe ser 0 o 1.");
        return new(sanity==1,follow==1);
    }
    internal static void Capture(GlobalTemplateDocument document,MachineCompatibilityState state)
    {
        if(document.Generation!=6)throw new InvalidDataException("El snapshot MT/MO corresponde a Gen6.");
        var native=document.Section("ProgressiveApp") as JsonObject??new();
        native["MachineCompatibility"]=new JsonObject{["Scope"]=0,["Options"]=new JsonObject{["Sanity"]=state.Sanity?1:0,["Follow"]=state.Follow?1:0}};
        document.SetSection("ProgressiveApp",native);
        var actions=document.Section("Actions") as JsonArray??new();
        var kept=actions.Where(action=>!Ids.Contains(action?["Id"]?.GetValue<string>()??"",StringComparer.OrdinalIgnoreCase)).Select(action=>action?.DeepClone()).ToArray();
        actions=new JsonArray(kept);
        if(state.Sanity)actions.Add(new JsonObject{["Id"]=Ids[0],["Parameters"]=new JsonObject()});
        if(state.Follow)actions.Add(new JsonObject{["Id"]=Ids[1],["Parameters"]=new JsonObject()});
        document.SetSection("Actions",actions);
    }
    internal static void ValidateReplay(GlobalTemplateDocument document)
    {
        var state=Read(document);if(state is null)return;
        var actual=document.Actions.Where(id=>Ids.Contains(id,StringComparer.OrdinalIgnoreCase)).ToArray();
        string[] expected=[..state.Sanity?new[]{Ids[0]}:Array.Empty<string>(),..state.Follow?new[]{Ids[1]}:Array.Empty<string>()];
        if(!actual.SequenceEqual(expected,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Las acciones Sanity/Follow deben coincidir con el snapshot y ejecutarse en ese orden.");
    }
}
