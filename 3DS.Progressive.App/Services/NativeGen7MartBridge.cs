using System.IO;
using System.Text.Json.Nodes;
namespace ThreeDS.Progressive.Services;

internal sealed record Gen7MartState(bool RandomRegular=false,bool RandomBP=false,bool Candies=false,bool Vitamins=false,bool FreeMega=false,bool BanCapsule=false,bool SpecialsOnly=false,bool ProtectX=true);
public static class NativeGen7MartBridge
{
    internal static readonly string[] ActionIds=["marts.randomize","marts.randomize-bp","marts.add-rare-candies","marts.add-ev-items","marts.free-mega-stones","marts.ban-ability-capsule"];
    private static readonly string[] Keys=[..ActionIds,"SpecialsOnly","ProtectX"];
    private static bool[] Flags(Gen7MartState s)=>[s.RandomRegular,s.RandomBP,s.Candies,s.Vitamins,s.FreeMega,s.BanCapsule,s.SpecialsOnly,s.ProtectX];
    internal static Gen7MartState? Read(GlobalTemplateDocument doc)
    {
        var node=doc.Section("ProgressiveApp")?["Gen7Marts"];if(node is null)return null;
        if(doc.Generation!=7||node is not JsonObject obj||obj["Scope"]?.GetValue<int>()!=0||obj["Options"] is not JsonObject options||options.Count!=Keys.Length||!options.Select(p=>p.Key).ToHashSet().SetEquals(Keys))throw new InvalidDataException("La receta de tiendas requiere Gen7, alcance Todos y opciones completas.");
        int[] f=Keys.Select(id=>options[id]?.GetValue<int>()??-1).ToArray();if(f.Any(n=>n is <0 or >1))throw new InvalidDataException("Las opciones de tiendas deben ser 0 o 1.");
        return new(f[0]==1,f[1]==1,f[2]==1,f[3]==1,f[4]==1,f[5]==1,f[6]==1,f[7]==1);
    }
    internal static void Capture(GlobalTemplateDocument doc,Gen7MartState state)
    {
        if(doc.Generation!=7)throw new InvalidDataException("La receta requiere Gen7.");
        var native=doc.Section("ProgressiveApp") as JsonObject??new();var options=new JsonObject();var flags=Flags(state);for(int i=0;i<Keys.Length;i++)options[Keys[i]]=flags[i]?1:0;
        var snapshot=native["Gen7Marts"] as JsonObject??new();snapshot["Scope"]=0;snapshot["Options"]=options;native["Gen7Marts"]=snapshot;doc.SetSection("ProgressiveApp",native);
        var actions=new JsonArray((doc.Section("Actions") as JsonArray??new()).Where(row=>!ActionIds.Contains(row?["Id"]?.GetValue<string>()??"",StringComparer.OrdinalIgnoreCase)).Select(row=>row?.DeepClone()).ToArray());
        for(int i=0;i<ActionIds.Length;i++)if(flags[i])actions.Add(new JsonObject{["Id"]=ActionIds[i],["Parameters"]=new JsonObject()});doc.SetSection("Actions",actions);
    }
    internal static bool Selected(GlobalTemplateDocument doc)=>doc.Generation==7&&Read(doc) is not null&&doc.Actions.Any(id=>id.StartsWith("marts.",StringComparison.OrdinalIgnoreCase));
    internal static bool HasAllSelectedSnapshots(GlobalTemplateDocument doc)=>doc.Generation!=7||!doc.Actions.Any(id=>ActionIds.Contains(id,StringComparer.OrdinalIgnoreCase))||Read(doc) is not null;
    internal static void ValidateDocument(GlobalTemplateDocument doc)
    {
        var state=Read(doc);if(state is null)return;var flags=Flags(state);
        if(!ActionIds.Where((id,i)=>flags[i]).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(doc.Actions.Where(id=>id.StartsWith("marts.",StringComparison.OrdinalIgnoreCase))))throw new InvalidDataException("Las acciones de tiendas no coinciden con sus opciones nativas.");
    }
    internal static MartShopPreview? ValidateSession(GameProjectSession project,GlobalTemplateDocument doc,int seed=1,bool forReplay=true)
    {
        if(forReplay)ValidateDocument(doc);var state=Read(doc);if(state is null)return null;
        if(project.Generation!=7)throw new InvalidDataException("Las tiendas requieren SM o USUM.");
        if(!forReplay)return null; // Importing shop options must not build a ROM modification.
        return Prepare(new GameMartService(project),state,seed);
    }
    internal static MartShopPreview Prepare(GameMartService editor,Gen7MartState s,int seed)
    {
        if(s.RandomRegular)editor.RandomizeShops(false,s.SpecialsOnly,s.ProtectX,seed);
        if(s.RandomBP)editor.RandomizeShops(true,seed:seed);
        if(s.Candies)editor.AddRareCandies();if(s.Vitamins)editor.AddVitamins252();
        if(s.BanCapsule)editor.BanAbilityCapsule(seed);if(s.FreeMega)editor.FreeMegaStoneBP();return editor.Prepare();
    }
    public static void Execute(string root,string json,int seed)=>ExecuteSession(GameProjectSession.Open(root,recoverInterruptedSave:false),json,seed);
    public static void ExecuteSession(GameProjectSession project,string json,int seed)
    {
        var doc=GlobalTemplateDocument.Parse(json,project.GameName,project.Generation);_=ValidateSession(project,doc,seed);if(!Selected(doc))throw new InvalidDataException("No hay acciones nativas de tiendas seleccionadas.");
        var editor=new GameMartService(project);var preview=Prepare(editor,Read(doc)!,seed);if(preview.Changes.Count>0)editor.Apply(preview);
    }
}
