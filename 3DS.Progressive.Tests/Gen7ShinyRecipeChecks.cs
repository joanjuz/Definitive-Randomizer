using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Services;
internal static class Gen7ShinyRecipeChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);GlobalCompletionChecks.SetupCode(f);string code=Path.Combine(f.Game.ExeFS,"code.bin");byte[] original=File.ReadAllBytes(code);
            var doc=GlobalTemplateDocument.Empty(f.Session.GameName,7);doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});ShinyRateTemplate.Capture(doc,new(127,true));
            check(NativeRecipeCompletion.TryComplete(f.Session,doc)&&File.ReadAllBytes(code).SequenceEqual(original),version+" Gen7 shiny recipe completes with explicit parameters without modifying code");GlobalEditorTemplateBridge.ValidateBatchReplay(doc);
            string json=Path.Combine(f.Root,"shiny.json");doc.Save(json);string output=Path.Combine(Path.GetTempPath(),"gen7-shiny-output-"+Guid.NewGuid().ToString("N"));_=new BatchRomExecution(f.Session).Preflight(new(json,output,"Check",1,true,false,false,false));check(true,version+" Gen7 shiny preflight accepts fully native recipe without RandSettings");
            var bad=doc.Clone();var actions=bad.Section("Actions")!.AsArray();actions[0]!["Parameters"]!.AsObject().Remove("rerolls");bad.SetSection("Actions",actions);reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(bad),version+" native Gen7 shiny recipe rejects missing parameters");
            File.WriteAllBytes(code,new byte[original.Length]);reject(()=>NativeRecipeCompletion.TryComplete(f.Session,Partial(doc)),version+" unknown Gen7 shiny ARM routine prevents recipe completion");
        }
    }
    private static GlobalTemplateDocument Partial(GlobalTemplateDocument doc){var copy=doc.Clone();var native=copy.Section("ProgressiveApp")!;native["PartialSnapshot"]=true;copy.SetSection("ProgressiveApp",native);return copy;}
}
