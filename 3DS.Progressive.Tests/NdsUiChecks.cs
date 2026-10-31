using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using Button=System.Windows.Controls.Button;

// DS workspace and home entry, rendered off-screen. RunUi is synthetic (CI); Probe drives the workspace with a real dump.
internal static class NdsUiChecks
{
    internal static void RunUi(Action<bool,string> check)
    {
        bool opened=false;var start=new StartView(null,false,_=>{},()=>{},()=>{},()=>{},null,()=>opened=true);
        var entry=Buttons(start).SingleOrDefault(b=>AutomationProperties.GetName(b).Contains("Nintendo DS"));
        check(entry is not null&&!Buttons(new StartView(null,false,_=>{},()=>{},()=>{},()=>{})).Any(b=>AutomationProperties.GetName(b).Contains("Nintendo DS")),"NdsUi_HomeEntryAppearsOnlyWhenProvided");
        entry!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));check(opened,"NdsUi_HomeEntryOpensDsWorkspace");

        var owner=new Window();var workspace=new NdsWorkspaceView(owner);
        string[] sections=["Inicio","Pokémon","Movimientos","Level Up","Evoluciones","MT/MO","Entrenadores","Encuentros","Iniciales","Estáticos y regalos","Intercambios","Objetos del mapa","Tiendas","Ajustes del juego","Global Template y Batch","Historial de cambios"];
        check(sections.All(s=>Buttons(workspace).Any(b=>AutomationProperties.GetName(b)=="Sección DS · "+s))&&workspace.Project is null&&workspace.CurrentRoute=="Home","NdsUi_WorkspaceShowsTheSidebarSectionsWithoutARom");
        Pump(workspace.NavigateAsync("Trainers"));
        check(workspace.CurrentRoute=="Home","NdsUi_SectionsNeedAnOpenRom");
        check(Descendants<FrameworkElement>(workspace).Where(e=>e is Button or System.Windows.Controls.CheckBox or System.Windows.Controls.ListBox).All(e=>AutomationProperties.GetName(e).Length>0),"NdsUi_EveryControlHasAnAccessibleName");
        Capture(workspace,"nds-workspace-home");

        string temp=Path.Combine(Path.GetTempPath(),"nds-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            // Correct game code but tables that do not match the profile: rejected, nothing opens.
            string fake=Path.Combine(temp,"fake.nds");File.WriteAllBytes(fake,NdsGen5RandomizerChecks.BuildRom(new Dictionary<string,byte[]>{["a/0/0/2"]=NdsGen5RandomizerChecks.BuildNarc([])},"IRBO"));
            Pump(workspace.LoadAsync(fake));
            check(workspace.Project is null,"NdsUi_RejectsRomWhoseTablesDoNotMatch");
            // With "Admitir romhacks", a structurally valid ROM opens as a romhack; sections whose code is not original show why.
            Descendants<System.Windows.Controls.CheckBox>(workspace).Single().IsChecked=true;
            string hack=Path.Combine(temp,"romhack.nds");File.WriteAllBytes(hack,NdsGen5RandomizerChecks.SyntheticBlackRomhack());
            Pump(workspace.LoadAsync(hack));var project=workspace.Project;
            check(project is {IsRomhack:true},"NdsUi_RomhackOptionOpensModifiedRom");
            Pump(workspace.NavigateAsync("Trainers"));
            check(Descendants<RecordEditorView>(workspace).Count()==1&&workspace.CurrentRoute=="Trainers","NdsUi_TrainersOpenInTheSharedRecordEditor");
            Pump(workspace.NavigateAsync("Starters"));
            check(workspace.CurrentRoute=="Starters"&&!Descendants<RecordEditorView>(workspace).Any(),"NdsUi_UnavailableSectionShowsANoticeInsteadOfAnEditor");
            Capture(workspace,"nds-workspace-notice");

            // The adapter behind the shared editor: manual save, scoped randomization, then export.
            var trainers=new NdsRecordEditor(project!,NdsSection.Trainers);
            var team=trainers.Read(1).SelectMany(g=>g.Fields).ToDictionary(f=>f.Key);
            check(trainers.UsesTrainerLayout&&trainers.Entries.Count==2&&team["P0.Species"].Choices!.Count==Gen5Randomizer.MaxSpecies&&team.ContainsKey("P0.Item")&&team.ContainsKey("P1.Move4"),"NdsUi_TrainerFieldsUseTheSharedKeysAndNames");
            var values=team.ToDictionary(p=>p.Key,p=>p.Value.Value);values["P0.Species"]=25;values["P0.Level"]=33;trainers.Save(1,values);
            check(project!.HasChanges&&project.History.Count==1&&trainers.Read(1).SelectMany(g=>g.Fields).Single(f=>f.Key=="P0.Species").Value==25,"NdsUi_ManualSaveStacksAChange");
            var proposal=trainers.Preview(Defaults(trainers,("Mode",0)),[2]);
            check(proposal.Changes.Count>0&&proposal.Changes.All(c=>c.Name.StartsWith("Entrenador 2",StringComparison.Ordinal)),"NdsUi_RandomizationRespectsTheChosenScope");
            trainers.Apply(proposal);
            check(project.History.Count==2&&trainers.Read(1).SelectMany(g=>g.Fields).Single(f=>f.Key=="P0.Species").Value==25,"NdsUi_ApplyKeepsEarlierEdits");
            reject(()=>trainers.Apply(proposal),"NdsUi_AProposalAppliesOnlyOnce");
            string output=Path.Combine(temp,"out.nds");Pump(workspace.ExportToAsync(output));
            using(var exported=NdsRom.Open(output))
            {
                var again=Gen5Data.Open(exported,requireOriginalTables:false).Trainers();
                check(again[0].Team[0] is {Species:25,Level:33}&&File.Exists(Path.ChangeExtension(output,".cambios.txt")),"NdsUi_ExportWritesEveryStackedChange");
            }
            // A ROM whose tables do not parse is still rejected; the ROM already open stays loaded.
            Pump(workspace.LoadAsync(fake));
            check(ReferenceEquals(workspace.Project,project),"NdsUi_RomhackOptionStillRejectsTablesThatDoNotParse");
        }
        finally{workspace.Project?.Dispose();Directory.Delete(temp,true);}

        static void reject(Action action,string name){try{action();}catch(InvalidOperationException){Console.WriteLine("PASS: "+name);return;}throw new Exception("FAIL: expected rejection: "+name);}
    }

    // Full flow through the workspace with a real dump: every section edited or randomized, stacked and exported once.
    // The options dialog always sends every field: its defaults with some values changed.
    private static Dictionary<string,int> Defaults(NdsRecordEditor editor,params (string Key,int Value)[] changed)
    {
        var values=editor.RandomOptions.SelectMany(g=>g.Fields).ToDictionary(f=>f.Key,f=>f.Value);
        foreach(var (key,value) in changed)values[key]=value;
        return values;
    }

    internal static void Probe(string rom,string output)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");Directory.CreateDirectory(output);
        string before=Hash(rom);var workspace=new NdsWorkspaceView(new Window());
        try
        {
            Pump(workspace.LoadAsync(rom));var project=workspace.Project;
            Check(project is {IsRomhack:false}&&project.Identity.KnownDump,$"NdsUi_Real_LoadsVerifiedOriginal ({project?.Game})");
            foreach(string route in new[]{"Pokemon","Moves","Learnsets","Evolutions","Machines","Trainers","Encounters","Starters","Statics","Trades","FieldItems","Shops"})
            {
                Pump(workspace.NavigateAsync(route));
                Check(workspace.CurrentRoute==route&&Descendants<RecordEditorView>(workspace).Count()==1,$"NdsUi_Real_{route}OpensInTheSharedEditor");
                Capture(workspace,"nds-workspace-"+route.ToLowerInvariant());
                if(route is "Trainers" or "Encounters")
                {
                    // The shared options dialog: trainer tabs with the BST table, and the wild BST table.
                    var view=Descendants<RecordEditorView>(workspace).Single();
                    ((Button)view.FindName("RandomizerButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    var options=(StackPanel)view.FindName("OptionsPanel");
                    if(route=="Trainers")
                    {
                        var tabs=options.Children.OfType<TabControl>().Single();
                        for(int i=0;i<tabs.Items.Count;i++){tabs.SelectedIndex=i;Capture(workspace,"nds-trainer-options-"+i);}
                        Check(tabs.Items.Count==5&&view.OptionControls.ContainsKey("DoubleChance")&&view.OptionControls.ContainsKey("CB_Moves")&&Descendants<ProgressiveBstTable>(options).Count()==1,"NdsUi_Real_TrainerOptionsUseTheSharedTabsAndBstTable");
                    }
                    else
                    {
                        Capture(workspace,"nds-wild-options");
                        Check(Descendants<ProgressiveBstTable>(options).Count()==1&&view.OptionControls.ContainsKey("Progressive"),"NdsUi_Real_WildOptionsShowTheBstTable");
                    }
                }
            }
            var trainers=new NdsRecordEditor(project!,NdsSection.Trainers);int first=trainers.Entries[0].Id;
            var values=trainers.Read(first).SelectMany(g=>g.Fields).ToDictionary(f=>f.Key,f=>f.Value);values["P0.Species"]=133;trainers.Save(first,values);
            var wild=new NdsRecordEditor(project!,NdsSection.Encounters);wild.Apply(wild.Preview(Defaults(wild,("Mode",1)),wild.Entries.Select(e=>e.Id).ToArray()));
            var starters=new NdsRecordEditor(project!,NdsSection.Starters);
            // Starters twice: code patches must not stack; the second choice wins everywhere.
            starters.Save(0,new Dictionary<string,int>{["Starter1"]=25,["Starter2"]=4,["Starter3"]=7});
            starters.Save(0,new Dictionary<string,int>{["Starter1"]=1,["Starter2"]=4,["Starter3"]=258});
            var items=new NdsRecordEditor(project!,NdsSection.FieldItems);items.Apply(items.Preview(Defaults(items,("Shuffle",1)),items.Entries.Select(e=>e.Id).ToArray()));
            var shops=new NdsRecordEditor(project!,NdsSection.Shops);shops.Apply(shops.Preview(Defaults(shops,("Shuffle",1)),shops.Entries.Select(e=>e.Id).ToArray()));
            Check(project!.History.Count==6&&project.HasChanges,$"NdsUi_Real_SixOperationsStack ({project.History.Sum(o=>o.Changes.Count)} cambios)");
            string target=Path.Combine(output,"random.nds");Pump(workspace.ExportToAsync(target));
            Check(File.Exists(target)&&File.Exists(Path.ChangeExtension(target,".cambios.txt")),"NdsUi_Real_ExportWritesRomAndChangeLog");
            using(var exported=NdsRom.Open(target))
            using(var original=NdsRom.Open(rom))
            {
                IReadOnlyList<int> finalStarters;int species;bool dexOnce=true;
                if(project.Generation==4)
                {
                    var data=Gen4Data.Open(exported,requireOriginalTables:false);finalStarters=data.Starters();species=data.Trainers().Single(t=>t.Id==first).Team[0].Species;
                }
                else
                {
                    var data=Gen5Data.Open(exported,requireOriginalTables:false);finalStarters=data.Starters();species=data.Trainers().Single(t=>t.Id==first).Team[0].Species;
                    var profile=data.Profile;
                    int dexLength=Narc.Read(exported.ReadFile(data.PathOf(Gen5Profile.Scripts))).Files[profile.PokedexGivenScript].Length;
                    int originalLength=Narc.Read(original.ReadFile(data.PathOf(Gen5Profile.Scripts))).Files[profile.PokedexGivenScript].Length;
                    dexOnce=dexLength==originalLength+profile.PokedexPatch.Count;
                }
                Check(finalStarters.SequenceEqual(new[]{1,4,258})&&species==133&&dexOnce,"NdsUi_Real_StartersPatchedOnceAndEditsSurviveLaterRandomizations");
            }
            // Every recipe kind in the template: manual edits, species, data randomizations and (Gen4) game tweaks.
            project.Apply(project.PreviewData("Randomizar Pokémon",NdsSection.Pokemon,new NdsDataOptions(5){Stats=NdsStatsMode.Shuffle,Types=true,Scope=new HashSet<int>{1,4,7}}));
            var ruled=new NdsRecordEditor(project,NdsSection.Trainers);
            ruled.Apply(ruled.Preview(Defaults(ruled,("TeamSize",3),("CB_Moves",2),("HeldItems",2),("MaxAI",1),("DoubleChance",50)),ruled.Entries.Select(e=>e.Id).ToArray()));
            if(project.TweaksProblem is null)project.Apply(project.PreviewTweaks(new NdsTweakOptions{FastestText=true,NationalDexAtStart=true}));
            // Global Template: the history saved, read back and replayed with its seeds on a fresh session gives the same changes.
            string templatePath=Path.Combine(output,"global.json");NdsProject.WriteGlobal(templatePath,project.CaptureGlobal());
            var template=NdsProject.ReadGlobal(templatePath);
            using(var replay=NdsProject.Open(rom,false))
            {
                replay.ApplyGlobal(template,newSeeds:false);
                Check(replay.History.Count==project.History.Count&&replay.History.Zip(project.History).All(p=>p.First.Action==p.Second.Action&&p.First.Changes.SequenceEqual(p.Second.Changes)),
                    "NdsUi_Real_GlobalTemplateReplaysTheSameChanges");
                var other=(System.Text.Json.Nodes.JsonObject)template.DeepClone();other["Game"]="XXXX";
                bool refused=false;try{replay.ApplyGlobal(other,newSeeds:false);}catch(InvalidOperationException){refused=true;}
                Check(refused&&replay.History.Count==project.History.Count,"NdsUi_Real_GlobalTemplateRefusesOtherGames");
            }
            // Batch: new ROMs with new seeds, verified on export; existing outputs are never overwritten.
            var batch=NdsProject.BuildBatch(rom,false,template,2,output,"batch",null,CancellationToken.None);
            bool kept=false;try{NdsProject.BuildBatch(rom,false,template,1,output,"batch",null,CancellationToken.None);}catch(IOException){kept=true;}
            Check(batch.Count==2&&batch.All(File.Exists)&&batch.All(b=>File.Exists(Path.ChangeExtension(b,".cambios.txt")))&&Hash(batch[0])!=Hash(batch[1])&&kept,"NdsUi_Real_BatchBuildsDistinctRomsWithoutOverwriting");
            Pump(workspace.NavigateAsync("Template"));Capture(workspace,"nds-workspace-template");
        }
        finally{workspace.Project?.Dispose();}
        Check(Hash(rom)==before,"NdsUi_Real_SourceUnchanged");
        foreach(string f in Directory.GetFiles(output))File.Delete(f);
        Console.WriteLine($"Completed {checks} DS workspace checks (off-screen; outputs deleted).");
    }

    // Same capture folder the CI uploads.
    private static void Capture(FrameworkElement root,string name)
    {
        if(Environment.GetEnvironmentVariable("PROGRESSIVE_UI_CAPTURE_DIR") is not {Length:>0} captures)return;
        const int width=1100,height=720;
        root.Measure(new System.Windows.Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
        var frame=new System.Windows.Shapes.Rectangle{Width=width,Height=height,Fill=(System.Windows.Media.Brush)Application.Current.FindResource("Bg")};frame.Measure(new System.Windows.Size(width,height));frame.Arrange(new Rect(0,0,width,height));bitmap.Render(frame);bitmap.Render(root);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(captures);using var file=File.Create(Path.Combine(captures,name+".png"));encoder.Save(file);
    }

    private static IEnumerable<Button> Buttons(DependencyObject root)=>Descendants<Button>(root);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        foreach(object child in LogicalTreeHelper.GetChildren(root))
            if(child is DependencyObject d){if(d is T t)yield return t;foreach(var nested in Descendants<T>(d))yield return nested;}
    }
    private static void Pump(Task task){var time=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(time.Elapsed.TotalSeconds>180)throw new TimeoutException();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);}task.GetAwaiter().GetResult();}
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
}
