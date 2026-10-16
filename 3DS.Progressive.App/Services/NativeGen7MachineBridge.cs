using System.IO;
using System.Text.Json.Nodes;
namespace ThreeDS.Progressive.Services;

internal sealed record Gen7MachineState(bool RandomMachines=false,bool MachineSanity=false,bool MachineFollow=false,bool RandomTutors=false,bool FreeTutors=false,bool TutorSanity=false,bool TutorFollow=false);

/// <summary>Replays WPF Gen7 machine recipe settings on an isolated Batch workspace.</summary>
public static class NativeGen7MachineBridge
{
    internal static readonly string[] ActionIds=["tms.randomize","tms.sanity","tms.follow-evolutions","move-tutors.randomize","move-tutors.free","move-tutors.sanity","move-tutors.follow-evolutions"];
    private static bool[] Flags(Gen7MachineState s)=>[s.RandomMachines,s.MachineSanity,s.MachineFollow,s.RandomTutors,s.FreeTutors,s.TutorSanity,s.TutorFollow];
    internal static Gen7MachineState? Read(GlobalTemplateDocument doc)
    {
        var node=doc.Section("ProgressiveApp")?["Gen7Machines"];
        if(node is null)return null;
        if(doc.Generation!=7||node is not JsonObject obj||obj["Scope"]?.GetValue<int>()!=0||obj["Options"] is not JsonObject options||options.Count!=ActionIds.Length||!options.Select(p=>p.Key).ToHashSet().SetEquals(ActionIds))throw new InvalidDataException("La receta de MT/tutores requiere Gen7, alcance Todos y opciones completas.");
        var flags=ActionIds.Select(id=>options[id]?.GetValue<int>()??-1).ToArray();if(flags.Any(n=>n is <0 or >1))throw new InvalidDataException("Las opciones de MT/tutores deben ser 0 o 1.");
        return new(flags[0]==1,flags[1]==1,flags[2]==1,flags[3]==1,flags[4]==1,flags[5]==1,flags[6]==1);
    }
    internal static void Capture(GlobalTemplateDocument doc,Gen7MachineState state)
    {
        if(doc.Generation!=7)throw new InvalidDataException("La receta requiere Gen7.");
        var native=doc.Section("ProgressiveApp") as JsonObject??new();var options=new JsonObject();var flags=Flags(state);
        for(int i=0;i<ActionIds.Length;i++)options[ActionIds[i]]=flags[i]?1:0;
        var snapshot=native["Gen7Machines"] as JsonObject??new();snapshot["Scope"]=0;snapshot["Options"]=options;native["Gen7Machines"]=snapshot;doc.SetSection("ProgressiveApp",native);
        var actions=new JsonArray((doc.Section("Actions") as JsonArray??new()).Where(row=>!ActionIds.Contains(row?["Id"]?.GetValue<string>()??"",StringComparer.OrdinalIgnoreCase)).Select(row=>row?.DeepClone()).ToArray());
        for(int i=0;i<ActionIds.Length;i++)if(flags[i])actions.Add(new JsonObject{["Id"]=ActionIds[i],["Parameters"]=new JsonObject()});doc.SetSection("Actions",actions);
    }
    internal static bool Selected(GlobalTemplateDocument doc,string key)=>key is ("Machines" or "Tutors")&&doc.Generation==7&&doc.Section("ProgressiveApp")?["Gen7Machines"] is not null&&doc.Actions.Any(id=>ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase)&&id.StartsWith(key=="Machines"?"tms.":"move-tutors.",StringComparison.OrdinalIgnoreCase));
    internal static bool HasAllSelectedSnapshots(GlobalTemplateDocument doc)=>doc.Generation!=7||!doc.Actions.Any(id=>ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase))||Read(doc) is not null;
    internal static void ValidateDocument(GlobalTemplateDocument doc)
    {
        var state=Read(doc);if(state is null)return;var flags=Flags(state);
        if(!ActionIds.Where((id,i)=>flags[i]).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(doc.Actions.Where(id=>ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase))))throw new InvalidDataException("Las acciones seleccionadas no coinciden con las opciones de MT/tutores.");
    }
    internal static void ValidateSession(GameProjectSession project,GlobalTemplateDocument doc,int seed=1,bool forReplay=true)
    {
        if(forReplay)ValidateDocument(doc);var state=Read(doc);if(state is null)return;
        if(project.Generation!=7)throw new InvalidDataException("El proyecto requiere Gen7.");
        if(!project.GameConfiguration.USUM&&(state.RandomTutors||state.FreeTutors||state.TutorSanity||state.TutorFollow))throw new InvalidDataException("Las acciones avanzadas de tutores requieren USUM.");
        if(!forReplay)return; // Import restores choices; it does not randomize or serialize tables.
        var service=new GameMachineTutorService(project);
        if(state.RandomMachines){service.RandomizeMachines(seed);_=service.PrepareMachines(true);}
        if(state.MachineSanity||state.MachineFollow)_=new GameTmCompatibilityService(project).Preview(state.MachineSanity,state.MachineFollow);
        if((state.RandomTutors||state.FreeTutors)&&!service.CanEditTutorMoves)throw new InvalidDataException("No se encontró la tabla USUM de 67 tutores.");
        if(state.RandomTutors||state.FreeTutors){if(state.RandomTutors)service.RandomizeTutorMoves(seed);if(state.FreeTutors)foreach(var row in service.Tutors)row.Price=0;_=service.PreviewTutorEdits();}
        if(state.TutorSanity||state.TutorFollow)_=new GameTutorCompatibilityService(project).Preview(state.TutorSanity,state.TutorFollow);
    }
    public static void Execute(string root,string json,int seed,string key)=>ExecuteSession(GameProjectSession.Open(root,recoverInterruptedSave:false),json,seed,key);
    public static void ExecuteSession(GameProjectSession project,string json,int seed,string key)
    {
        var doc=GlobalTemplateDocument.Parse(json,project.GameName,project.Generation);ValidateSession(project,doc,seed);var s=Read(doc)!;
        if(key is not ("Machines" or "Tutors")||!Selected(doc,key))throw new InvalidDataException("No hay acciones nativas seleccionadas para "+key);
        var service=new GameMachineTutorService(project);
        if(key=="Machines")
        {
            if(s.RandomMachines){service.RandomizeMachines(seed);service.SaveMachines(true);}
            if(s.MachineSanity||s.MachineFollow){var editor=new GameTmCompatibilityService(project);var preview=editor.Preview(s.MachineSanity,s.MachineFollow);if(preview.Changes.Count>0)editor.Apply(preview);}
        }
        else
        {
            if(s.RandomTutors||s.FreeTutors){if(s.RandomTutors)service.RandomizeTutorMoves(seed);if(s.FreeTutors)foreach(var row in service.Tutors)row.Price=0;var preview=service.PreviewTutorEdits();if(preview.Changes.Count>0)service.ApplyTutorEdits(preview);}
            if(s.TutorSanity||s.TutorFollow){var editor=new GameTutorCompatibilityService(project);var preview=editor.Preview(s.TutorSanity,s.TutorFollow);if(preview.Changes.Count>0)editor.Apply(preview);}
        }
    }
}
