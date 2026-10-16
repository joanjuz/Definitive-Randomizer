using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

internal static class Tm6CompatibilityChecks
{
    internal static void RunReal(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            string[] Inventory()=>Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Order().Select(path=>{var file=new FileInfo(path);return path+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks;}).ToArray();
            var before=Inventory();var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
            var proposal=new GameTmCompatibilityService(project).Preview(true,true);
            if(!before.SequenceEqual(Inventory()))throw new IOException("La ROM original cambió.");
            string personal=Path.Combine(project.GameConfiguration.RomFS,project.GameConfiguration.GetGARCFileName("personal"));
            if(!File.ReadAllBytes(personal).SequenceEqual(proposal.PersonalOriginal))throw new IOException("Personal original cambió.");
            Console.WriteLine($"PASS READ ONLY TM/HM: {root}; sanity +{proposal.AddedSanityFlags}; inherited +{proposal.AddedInheritedFlags}; affected {proposal.Changes.Count}; original inventory and Personal bytes intact; no Apply.");
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);Machine6Checks.Setup(f);
            var main=new ThreeDS.Progressive.MainWindow();
            try
            {
                main.LoadProject(f.Session);
                ((System.Windows.Controls.Button)main.FindName("MachineTutorButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var view=((System.Windows.Controls.ContentControl)main.FindName("SectionHost")).Content as ThreeDS.Progressive.Gen6MachineView;
                check(view?.Editor.Editor is GameMachine6Service,version+" WPF MT/MO panel preserves native editor and Global options");
                IEnumerable<System.Windows.DependencyObject> Tree(System.Windows.DependencyObject node)
                {yield return node;foreach(var child in System.Windows.LogicalTreeHelper.GetChildren(node).OfType<System.Windows.DependencyObject>())foreach(var descendant in Tree(child))yield return descendant;}
                var flags=Tree(view!).OfType<System.Windows.Controls.CheckBox>().ToArray();
                check(flags.Any(flag=>flag.Content?.ToString()?.StartsWith("MT/MO Sanity")==true)&&flags.Any(flag=>flag.Content?.ToString()?.StartsWith("Follow Evolutions")==true),version+" WPF Gen6 exposes both machine compatibility operations");
                foreach(var flag in flags.Where(flag=>flag.Content?.ToString()?.StartsWith("MT/MO Sanity")==true||flag.Content?.ToString()?.StartsWith("Follow Evolutions")==true))flag.IsChecked=true;
                ((System.Windows.Controls.Button)main.FindName("GlobalTemplateButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var doc=GlobalTemplateDocument.Empty(f.Game.Version.ToString(),6);
                typeof(ThreeDS.Progressive.MainWindow).GetMethod("CaptureGlobalOptions",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,new object[]{doc});
                check(MachineCompatibilityTemplate.Read(doc)==new MachineCompatibilityState(true,true),version+" WPF navigation captures compatibility flags into Global");
                MachineCompatibilityTemplate.Capture(doc,new(false,true));
                typeof(ThreeDS.Progressive.MainWindow).GetMethod("RestoreGlobalOptions",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,new object[]{doc});
                ((System.Windows.Controls.Button)main.FindName("MachineTutorButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                view=((System.Windows.Controls.ContentControl)main.FindName("SectionHost")).Content as ThreeDS.Progressive.Gen6MachineView;
                check(view!.RememberCompatibility()==new MachineCompatibilityState(false,true),version+" WPF restores Global flags when reopening MT/MO");
            }
            finally{main.Close();}
        }
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var machines=Machine6Checks.Setup(f);
            var recipe=GlobalTemplateDocument.Empty(f.Game.Version.ToString(),6);
            recipe.SetSection("Actions",new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject{["Id"]="rng.seed",["Parameters"]=new System.Text.Json.Nodes.JsonObject{["Seed"]=73}}));
            MachineCompatibilityTemplate.Capture(recipe,new(true,true));
            check(recipe.Actions.SequenceEqual(new[]{"rng.seed","tms.sanity","tms.follow-evolutions"})&&MachineCompatibilityTemplate.Read(recipe)==new MachineCompatibilityState(true,true),version+" Global captures both compatibility options and preserves unrelated actions");
            MachineCompatibilityTemplate.Capture(recipe,new(true,false));
            check(!recipe.Actions.Contains("tms.follow-evolutions")&&MachineCompatibilityTemplate.Read(recipe)==new MachineCompatibilityState(true,false),version+" Global removes disabled compatibility actions");
            MachineCompatibilityTemplate.Capture(recipe,new(false,false));
            check(recipe.Actions.SequenceEqual(new[]{"rng.seed"}),version+" Global explicitly preserves disabled flags without executing actions");
            MachineCompatibilityTemplate.Capture(recipe,new(true,true));var invalid=recipe.Section("ProgressiveApp")!;invalid["MachineCompatibility"]!["Scope"]=1;recipe.SetSection("ProgressiveApp",invalid);
            reject(()=>NativeBatchGen6Bridge.ValidateDocument(recipe),version+" Global rejects partial compatibility scope");
            MachineCompatibilityTemplate.Capture(recipe,new(true,true));recipe.SelectActions(new HashSet<string>{"rng.seed","tms.sanity"});
            reject(()=>NativeBatchGen6Bridge.ValidateDocument(recipe),version+" Batch rejects missing action from compatibility snapshot");
            var values=EditorValues.Flatten(new GameMachine6Service(f.Session).Read(1));
            var personal=new GARC.MemGARC(File.ReadAllBytes(f.PersonalPath)).Files;
            foreach(var record in personal.Take(personal.Length-1))record.AsSpan(0x28,16).Clear();
            // Preserve an unused high bit and existing tutor data.
            personal[1][0x37]=0x80;personal[1][0x38]=0x40;
            GarcTransaction.RebuildPersonalMaster(personal);File.WriteAllBytes(f.PersonalPath,GarcTransaction.Build(File.ReadAllBytes(f.PersonalPath),f.Game.GetGARCReference("personal"),personal));
            byte[] learned=new byte[8];BitConverter.GetBytes((ushort)values["HM01"]).CopyTo(learned,0);BitConverter.GetBytes((ushort)1).CopyTo(learned,2);learned.AsSpan(4).Fill(0xFF);
            var sets=ModuleChecks.Read(f,"levelup");sets[1]=learned;f.WriteGarc(ModuleChecks.PathFor(f,"levelup"),sets,f.Game.GetGARCReference("levelup"));
            var evolutions=ModuleChecks.Read(f,"evolution");foreach(var record in evolutions)Array.Clear(record);
            for(int i=1;i<=2;i++){BitConverter.GetBytes((ushort)4).CopyTo(evolutions[i],0);BitConverter.GetBytes((ushort)(i+1)).CopyTo(evolutions[i],4);}
            f.WriteGarc(ModuleChecks.PathFor(f,"evolution"),evolutions,f.Game.GetGARCReference("evolution"));
            byte[] before=File.ReadAllBytes(f.PersonalPath);var service=new GameTmCompatibilityService(f.Session);var proposal=service.Preview(true,true);
            check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(before),version+" MT/MO compatibility preview leaves Personal unchanged");
            var result=new GARC.MemGARC(proposal.PersonalOutput).Files;
            PersonalInfo Decode(byte[] bytes)=>f.Game.ORAS?new PersonalInfoORAS(bytes):new PersonalInfoXY(bytes);
            check(Enumerable.Range(1,3).All(i=>Decode(result[i]).TMHM[100]),version+" HM sanity is inherited through two evolution steps");
            check(result[1][0x37]==0x80&&Enumerable.Range(0,result.Length-1).All(i=>result[i].Where((value,at)=>at<0x28||at>=0x38).SequenceEqual(personal[i].Where((value,at)=>at<0x28||at>=0x38))),version+" compatibility preserves padding tutor and all unrelated fields");
            check(result[^1].SequenceEqual(result.Take(result.Length-1).SelectMany(x=>x)),version+" MT/MO compatibility rebuilds Personal master");
            service.Apply(proposal);check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(proposal.PersonalOutput),version+" MT/MO compatibility apply rereads exact output");
            check(new GameTmCompatibilityService(f.Session).Preview(true,true).Changes.Count==0,version+" MT/MO compatibility is idempotent");
            reject(()=>service.Apply(proposal),version+" stale compatibility proposal rejected");
            service=new GameTmCompatibilityService(f.Session);proposal=service.Preview(true,false);
            // The dependency check is exercised with a nonempty captured proposal on a restored baseline.
            File.WriteAllBytes(f.PersonalPath,before);service=new GameTmCompatibilityService(f.Session);proposal=service.Preview(true,true);
            var code=File.ReadAllBytes(machines.Code);code[0]^=1;File.WriteAllBytes(machines.Code,code);
            reject(()=>service.Apply(proposal),version+" changed machine source rejects compatibility commit");
            check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(before),version+" dependency conflict leaves Personal untouched");
            File.WriteAllBytes(machines.Code,machines.CodeBytes);
            var doc=GlobalTemplateDocument.Empty(f.Game.Version.ToString(),6);
            doc.SetSection("Actions",new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject{["Id"]="tms.sanity",["Parameters"]=new System.Text.Json.Nodes.JsonObject()},new System.Text.Json.Nodes.JsonObject{["Id"]="tms.follow-evolutions",["Parameters"]=new System.Text.Json.Nodes.JsonObject()}));
            MachineCompatibilityTemplate.Capture(doc,new(true,true));
            GlobalEditorTemplateBridge.ValidateBatchReplay(doc);NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),71);
            check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(proposal.PersonalOutput),version+" Batch Sanity then Follow produces exact combined manual output");
            byte[] stable=File.ReadAllBytes(f.PersonalPath);NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),71);
            check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(stable),version+" Batch MT/MO compatibility replay is idempotent");
        }
    }
}
