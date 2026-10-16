using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

internal static class GlobalCompletionChecks
{
    internal static void SetupCode(Fixture f)
    {
        byte[] code=new byte[0x4000];
        new byte[]{0x23,0,0xD4,0xE5,1,0x50,0x85,0xE2,5,0,0x50,0xE1,0xDE,0xFF,0xFF,0xCA}.CopyTo(code,0x1000);
        new byte[]{0,0x20,0x22,0xE0,2,0x30,0x21,0xE2,3,0x20,0x92,0xE1,0x1C,0,0,0x0A}.CopyTo(code,0x2000);
        File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"),code);
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);SetupCode(f);
            string codePath=Path.Combine(f.Game.ExeFS,"code.bin");byte[] baseline=File.ReadAllBytes(codePath);
            byte[] other=(byte[])baseline.Clone();baseline.AsSpan(0x1004,12).CopyTo(other.AsSpan(0x3004));new byte[]{0x30,0,0x94,0xE5}.CopyTo(other,0x3000);
            File.WriteAllBytes(codePath,other);
            check(new GameShinyRateService(f.Session).Read().Vanilla,version+" ORAS-like suffix in unrelated ARM loop does not reject compatible PID routine");
            baseline.AsSpan(0x1000,16).CopyTo(other.AsSpan(0x3000));File.WriteAllBytes(codePath,other);
            reject(()=>new GameShinyRateService(f.Session),version+" Two compatible PID routines remain ambiguous and are rejected");
            File.WriteAllBytes(codePath,baseline);
            var fresh=NativeRecipeFactory.Create(f.Session,null,new[]{"Wild","Items"});
            check(fresh.Actions.Contains("wild-encounters.randomize")&&fresh.Actions.Contains("items.fix-economy")&&fresh.Section("ProgressiveApp")?["Wild"]?["Scope"]?.GetValue<int>()==0,version+" New native Global recipe captures complete editor defaults without history");
            check(NativeRecipeCompletion.TryComplete(f.Session,fresh),version+" New native recipe can complete without first applying edits");
            var doc=GlobalTemplateDocument.Empty(f.Session.GameName,6);
            doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true,["FutureSetting"]="preserved"});
            ShinyRateTemplate.Capture(doc,new(333,true));
            check(ShinyRateTemplate.Read(GlobalTemplateDocument.Parse(doc.ToJson(),f.Session.GameName,6))==new ShinyRateOptions(333,true),version+" Global Shiny options roundtrip exact requested rerolls");
            var code=File.ReadAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"));
            check(NativeRecipeCompletion.TryComplete(f.Session,doc)&&doc.Section("ProgressiveApp")?["PartialSnapshot"]?.GetValue<bool>()==false,version+" Fully configured native history recipe becomes replayable");
            check(File.ReadAllBytes(Path.Combine(f.Game.ExeFS,"code.bin")).SequenceEqual(code)&&doc.Section("ProgressiveApp")?["FutureSetting"]?.GetValue<string>()=="preserved",version+" Completion preserves source bytes and unrelated JSON");
            GlobalEditorTemplateBridge.ValidateBatchReplay(doc);NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),23);
            check(new GameShinyRateService(f.Session).Read().AlwaysShiny,version+" Completed Global native recipe executes requested patch");
            var partial=GlobalTemplateDocument.Empty(f.Session.GameName,6);partial.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});
            partial.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="trainers.randomize",["Parameters"]=new JsonObject()}));
            check(!NativeRecipeCompletion.TryComplete(f.Session,partial),version+" Legacy history recipe remains partial without full settings");
            reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(partial),version+" Partial legacy recipe remains blocked");
            partial.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="shiny-rate.apply",["Parameters"]=new JsonObject()}));
            reject(()=>NativeRecipeCompletion.TryComplete(f.Session,partial),version+" Missing Shiny options cannot mark recipe complete");
            check(partial.Section("ProgressiveApp")?["PartialSnapshot"]?.GetValue<bool>()==true,version+" Rejected completion preserves partial marker");
            var compatibility=GlobalTemplateDocument.Empty(f.Session.GameName,6);MachineCompatibilityTemplate.Capture(compatibility,new(true,true));
            compatibility.SelectActions(new HashSet<string>{"tms.follow-evolutions"});NativeRecipeCompletion.ReconcileSelection(compatibility);
            MachineCompatibilityTemplate.ValidateReplay(compatibility);
            check(MachineCompatibilityTemplate.Read(compatibility)==new MachineCompatibilityState(false,true),version+" Global deselection synchronizes compatibility snapshot");
            reject(()=>ShinyRateTemplate.Capture(doc,new(0,false)),version+" Global invalid rerolls rejected");
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);SetupCode(f);
            var main=new ThreeDS.Progressive.MainWindow();
            try
            {
                main.LoadProject(f.Session);
                var fresh=NativeRecipeFactory.Create(f.Session,null,new[]{"Wild","Items"});
            check(fresh.Actions.Contains("wild-encounters.randomize")&&fresh.Actions.Contains("items.fix-economy")&&fresh.Section("ProgressiveApp")?["Wild"]?["Scope"]?.GetValue<int>()==0,version+" New native Global recipe captures complete editor defaults without history");
            check(NativeRecipeCompletion.TryComplete(f.Session,fresh),version+" New native recipe can complete without first applying edits");
            var doc=GlobalTemplateDocument.Empty(f.Session.GameName,6);ShinyRateTemplate.Capture(doc,new(333,true));
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(ThreeDS.Progressive.MainWindow).GetMethod("RestoreGlobalOptions",flags)!.Invoke(main,new object[]{doc});
                var show=typeof(ThreeDS.Progressive.MainWindow).GetMethod("Section_Click",flags)!;
                show.Invoke(main,new object[]{new System.Windows.Controls.Button{Tag="ShinyRate"},new System.Windows.RoutedEventArgs()});
                var view=(ThreeDS.Progressive.ShinyRateView)((System.Windows.Controls.ContentControl)main.FindName("SectionHost")).Content;
                check(view.RememberOptions()==new ShinyRateOptions(333,true),version+" WPF Shiny restores Global options");
                show.Invoke(main,new object[]{new System.Windows.Controls.Button{Tag="GlobalTemplate"},new System.Windows.RoutedEventArgs()});
                var captured=GlobalTemplateDocument.Empty(f.Session.GameName,6);
                typeof(ThreeDS.Progressive.MainWindow).GetMethod("CaptureGlobalOptions",flags)!.Invoke(main,new object[]{captured});
                check(ShinyRateTemplate.Read(captured)==new ShinyRateOptions(333,true),version+" WPF Shiny options survive navigation and capture");
                show.Invoke(main,new object[]{new System.Windows.Controls.Button{Tag="ShinyRate"},new System.Windows.RoutedEventArgs()});view=(ThreeDS.Progressive.ShinyRateView)((System.Windows.Controls.ContentControl)main.FindName("SectionHost")).Content;
                check(view.RememberOptions()==new ShinyRateOptions(333,true),version+" WPF reopened Shiny preserves requested values");
            }
            finally{main.Close();}
        }
    }
}
