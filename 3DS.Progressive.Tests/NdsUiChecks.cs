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
        string[] sections=["Inicio","Pokémon","Movimientos","Level Up","Evoluciones","MT y tutores","Compatibilidad MT/MO","Compatibilidad de tutores","Entrenadores","Encuentros","Iniciales","Estáticos y regalos","Intercambios","Objetos del mapa","Tiendas","Ajustes del juego","Global Template y Batch","Historial de cambios"];
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
            var proposal=trainers.Preview(Defaults(trainers),[2]);
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
            // Phase 14 sections: the shared editor where the game has them, a notice that says why where it does not.
            // «MT y tutores» is the 3DS layout in every game (Black/White show the TM list read-only).
            Pump(workspace.NavigateAsync("MoveLists"));
            var machineView=Descendants<NdsMachineTutorView>(workspace).SingleOrDefault();
            Check(machineView is not null&&machineView.MachineGrid.Items.Count==project!.Source.TmCount&&(project.Source.Tables.TutorCount>0)==(machineView.TutorGrid is not null)
                &&machineView.MachineGrid.IsReadOnly==(project.Source.MachineTable<0),$"NdsUi_Real_MachineTutorViewAsIn3ds ({machineView?.MachineGrid.Items.Count} MT, {machineView?.TutorGrid?.Items.Count??0} tutores)");
            Capture(workspace,"nds-workspace-movelists");
            foreach(var (route,section) in new[]{("Tutors",NdsSection.Tutors),("EggMoves",NdsSection.EggMoves)})
            {
                Pump(workspace.NavigateAsync(route));bool available=project!.SectionProblem(section) is null;
                Check(workspace.CurrentRoute==route&&Descendants<RecordEditorView>(workspace).Count()==(available?1:0),$"NdsUi_Real_{route}{(available?"OpensInTheSharedEditor":"ShowsWhyItIsMissing")}");
                Capture(workspace,"nds-workspace-"+route.ToLowerInvariant());
            }
            // Pokémon: the 3DS header card (name, types, BST) above the fields and the total in the Inspector.
            Pump(workspace.NavigateAsync("Pokemon"));
            {
                var card=Descendants<System.Windows.Controls.Border>(workspace).FirstOrDefault(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Resumen de la especie");
                var bst=project!.Read(NdsSection.Pokemon).Where(c=>c.Entry==1&&c.Local.StartsWith("Stat",StringComparison.Ordinal)).Sum(c=>c.Value);
                var texts=card is null?[]:Descendants<System.Windows.Controls.TextBlock>(card).Select(t=>t.Text).ToArray();
                Check(card is not null&&texts.Contains(project.SpeciesNames[1])&&texts.Contains(bst.ToString(System.Globalization.CultureInfo.InvariantCulture)),$"NdsUi_Real_PokemonHeaderCardAsIn3ds (BST {bst})");
                Capture(workspace,"nds-workspace-pokemon-card");
            }
            // Level Up opens the 3DS view, as in 3DS.
            Pump(workspace.NavigateAsync("Learnsets"));
            Check(workspace.CurrentRoute=="Learnsets"&&Descendants<LevelUpView>(workspace).Count()==1,"NdsUi_Real_LevelUpOpensThe3dsView");
            Capture(workspace,"nds-workspace-learnsets");
            foreach(string route in new[]{"Pokemon","Moves","Evolutions","Machines","Trainers","Encounters","Starters","Statics","Trades","FieldItems","Shops"})
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
                        Check(tabs.Items.Count==5&&view.OptionControls.ContainsKey("NUD_DoubleBattleChance")&&view.OptionControls.ContainsKey("CB_Moves")&&Descendants<ProgressiveBstTable>(options).Count()==1,"NdsUi_Real_TrainerOptionsUseTheSharedTabsAndBstTable");
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
            var wild=new NdsRecordEditor(project!,NdsSection.Encounters);wild.Apply(wild.Preview(Defaults(wild,("SimilarBST",1)),wild.Entries.Select(e=>e.Id).ToArray()));
            var starters=new NdsRecordEditor(project!,NdsSection.Starters);
            // Starters twice: code patches must not stack; the second choice wins everywhere.
            // Every field is saved, as the editor does (Diamond/Platinum also show the starter's item).
            Dictionary<string,int> Starters(int a,int b,int c){var v=starters.Read(0).SelectMany(g=>g.Fields).ToDictionary(f=>f.Key,f=>f.Value);v["Starter1"]=a;v["Starter2"]=b;v["Starter3"]=c;return v;}
            starters.Save(0,Starters(25,4,7));
            starters.Save(0,Starters(1,4,258));
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
            // TM Sanity and Follow Evolutions: compatibility is only added; every species becomes compatible with the TMs it learns by level.
            {
                var compatBefore=project!.Read(NdsSection.Machines).ToDictionary(c=>c.Key,c=>c.Value);
                project.Apply(project.PreviewData("TM Sanity y TM Follow Evolutions",NdsSection.Machines,new pk3DS.Core.NDS.NdsDataOptions(0){MachineSanity=true,MachineFollowEvolutions=true}));
                var compatAfter=project.Read(NdsSection.Machines).ToDictionary(c=>c.Key,c=>c.Value);
                var learned=project.Read(NdsSection.Learnsets).Where(c=>c.Local.EndsWith(".Move",StringComparison.Ordinal)).GroupBy(c=>c.Entry).ToDictionary(g=>g.Key,g=>g.Select(c=>c.Value).ToHashSet());
                var tms=project.Source.MachineMoves;
                bool sane=compatAfter.Where(p=>int.Parse(p.Key.Split(':')[1][1..]) is var bit&&bit<tms.Count&&learned.TryGetValue(int.Parse(p.Key.Split(':')[0]),out var l)&&l.Contains(tms[bit])).All(p=>p.Value==1);
                Check(compatAfter.Count(p=>p.Value==1)>compatBefore.Count(p=>p.Value==1)&&compatBefore.All(p=>p.Value==0||compatAfter[p.Key]==1)&&sane,$"NdsUi_Real_TmSanityAndFollowOnlyAdd (+{compatAfter.Count(p=>p.Value==1)-compatBefore.Count(p=>p.Value==1)})");
            }
            // Level Up view on DS: a longer list saved, Metronome for every species and the bulk randomizer, all in the history (and the template below).
            {
                var levelUp=new NdsLevelUpSource(project!);var rows=levelUp.Read(1).ToList();int count=rows.Count;
                rows.Add(new ThreeDS.Progressive.Services.LevelUpMoveEntry{Move=33,Level=99});rows.Add(new ThreeDS.Progressive.Services.LevelUpMoveEntry{Move=45,Level=2});
                levelUp.Save(1,rows);
                var saved=levelUp.Read(1);
                Check(saved.Count==count+2&&saved.Select(r=>r.Level).SequenceEqual(saved.Select(r=>r.Level).Order())&&saved[^1] is {Move:33,Level:99},"NdsUi_Real_LevelUpSavesALongerListInLevelOrder");
                var metronome=levelUp.PrepareRandomization(new(false,false,15,false,25,false,false,79,true),metronome:true);
                var proposed=levelUp.PreviewLearnsets(metronome);
                Check(proposed.Values.All(s=>s.Count==1&&s[0] is {Move:118,Level:1})&&levelUp.Read(4).Count>1,"NdsUi_Real_LevelUpMetronomePreviewWritesNothing");
                var random=levelUp.PrepareRandomization(new(false,true,30,false,25,true,false,79,true));levelUp.ApplyRandomization(random);
                Check(levelUp.Entries.Where(e=>levelUp.Read(e.Index).Count>=4).All(e=>levelUp.Read(e.Index).Take(4).All(r=>r.Level==1)),"NdsUi_Real_LevelUpBulkStartsWithFour");
                // «Expandir cantidad»: every list reaches the game's limit, without repeats, Struggle or HMs, levels in order.
                var hms=project.Source.MachineMoves.Skip(project.Source.TmCount).ToHashSet();
                var expanded=levelUp.PreviewLearnsets(levelUp.PrepareRandomization(new(false,true,40,true,levelUp.MaxMoves,true,true,60,true)));
                Check(levelUp.MaxMoves>=15&&expanded.Values.All(s=>s.Count==levelUp.MaxMoves&&s.Select(r=>r.Move).Distinct().Count()==s.Count&&s.All(r=>r.Move!=165&&!hms.Contains(r.Move)&&r.Level is >=1 and <=60)
                    &&s.Select(r=>r.Level).SequenceEqual(s.Select(r=>r.Level).Order())),$"NdsUi_Real_LevelUpExpandReachesTheGameLimit ({levelUp.MaxMoves})");
            }
            // Parity with 3DS: the Level Caps and Trainer Move Rules tables, three-stage starters and egg moves as a form.
            {
                var capsEditor=new NdsRecordEditor(project,NdsSection.Trainers);var rows=capsEditor.GetRuleRows();
                Check(rows.Any(r=>r.Group.StartsWith("Jefes · Líderes",StringComparison.Ordinal))&&rows.Any(r=>r.Group.StartsWith("Jefes · Alto Mando",StringComparison.Ordinal))&&rows.Any(r=>r.Group.StartsWith("Importantes",StringComparison.Ordinal))
                    &&rows.All(r=>!r.CapUse&&!r.MoveUse&&!r.SupportsMega&&!r.SupportsEVs),$"NdsUi_Real_RuleTableHasLeadersEliteFourAndImportantTrainers ({rows.Count} filas)");
                var teamsBefore=project.Trainers().ToDictionary(t=>t.Id);
                // The strongest boss with more than three Pokémon gets cap 50 and «Máx. 3»; a leader gets a move rule without status moves.
                var capRow=rows.Where(r=>r.Group.StartsWith("Jefes",StringComparison.Ordinal)&&teamsBefore[r.Id].Team.Count>3).OrderByDescending(r=>r.Ace).First();
                var moveRow=rows.First(r=>r.Group.StartsWith("Jefes · Líderes",StringComparison.Ordinal)&&r.Id!=capRow.Id);
                capRow.CapUse=true;capRow.Cap=50;capRow.Max3=true;capsEditor.UpdateRuleRows([capRow],true);
                moveRow.MoveUse=true;moveRow.AllowStatus=false;capsEditor.UpdateRuleRows([moveRow],false);
                capsEditor.Apply(capsEditor.Preview(Defaults(capsEditor,("CHK_RandomPKM",0),("CHK_LevelCaps",1),("GlobalCaps.ApplyPrevious",1),("GlobalCaps.PreviousGap",2)),capsEditor.Entries.Select(e=>e.Id).ToArray()));
                var after=project.Trainers().ToDictionary(t=>t.Id);
                Check(after[capRow.Id].Team.Count==3&&after[capRow.Id].Team.All(p=>p.Level==50),$"NdsUi_Real_LevelCapSetsTheTeamAndKeepsThree (#{capRow.Id} {capRow.Trainer})");
                // Trainers below the capped one move up to at most cap - gap, keeping the spread within each team.
                int capAce=teamsBefore[capRow.Id].Team.Max(p=>p.Level);
                var scaled=teamsBefore.Values.Where(t=>t.Team.Count>0&&t.Id!=capRow.Id&&t.Team.Max(p=>p.Level)<=capAce&&after[t.Id].Team.Count==t.Team.Count).ToArray();
                Check(scaled.Any(t=>after[t.Id].Team[0].Level!=t.Team[0].Level)&&scaled.All(t=>after[t.Id].Team.Max(p=>p.Level)<=48
                    &&after[t.Id].Team.Zip(t.Team).Select(p=>p.First.Level-p.Second.Level).Distinct().Count()==1||after[t.Id].Team.Any(p=>p.Level is 1 or 100)),$"NdsUi_Real_TrainersWithoutCapMoveBetweenCaps ({scaled.Length})");
                // Category by its name: Gen4 and Gen5 number the categories differently.
                var statusMoves=project.Read(NdsSection.Moves).Where(c=>c.Local=="Category"&&c.Options is {} names&&names[c.Value]=="Estado").Select(c=>c.Entry).ToHashSet();
                Check(statusMoves.Count>0&&after[moveRow.Id].Team.All(p=>p.Moves.Any(m=>m>0)&&p.Moves.All(m=>m==0||!statusMoves.Contains(m))),$"NdsUi_Real_MoveRuleGivesBetterMovesWithoutStatus (#{moveRow.Id} {moveRow.Trainer})");
                // Three-stage starters: each one is not an evolution and evolves into something that evolves again, in this ROM's table.
                var startersEditor=new NdsRecordEditor(project,NdsSection.Starters);
                startersEditor.Apply(startersEditor.Preview(Defaults(startersEditor,("BasicStarter",1),("Legends",0)),[0]));
                var chosen=startersEditor.Read(0).SelectMany(g=>g.Fields).Where(f=>f.Key.StartsWith("Starter",StringComparison.Ordinal)).Select(f=>f.Value).ToArray();
                var next=project.Read(NdsSection.Evolutions).Where(c=>c.Local.EndsWith(".Species",StringComparison.Ordinal)&&c.Value>0).GroupBy(c=>c.Entry).ToDictionary(g=>g.Key,g=>g.Select(c=>c.Value).Where(v=>v!=g.Key).ToArray());
                var evolved=next.Values.SelectMany(v=>v).ToHashSet();
                Check(chosen.Length==3&&chosen.Distinct().Count()==3&&chosen.All(s=>!evolved.Contains(s)&&next.TryGetValue(s,out var t)&&t.Any(x=>next.TryGetValue(x,out var u)&&u.Length>0)),
                    $"NdsUi_Real_StartersStartThreeStageLines ({string.Join(", ",chosen.Select(s=>project.SpeciesNames[s]))})");
                Check(project.SectionProblem(NdsSection.EggMoves) is not null||!new NdsRecordEditor(project,NdsSection.EggMoves).UsesRecordTable,"NdsUi_Real_EggMovesShowAsAForm");
            }
            // The 3DS options on DS, each one checked on its result, in a separate session of the same ROM.
            using(var p=NdsProject.Open(rom,false))
            {
                int gen=p.Generation;
                Dictionary<int,Dictionary<string,int>> Table(NdsSection s)=>p.Read(s).GroupBy(c=>c.Entry).ToDictionary(g=>g.Key,g=>g.ToDictionary(c=>c.Local,c=>c.Value));
                NdsRecordEditor Run(NdsSection section,params (string Key,int Value)[] changed){var e=new NdsRecordEditor(p,section);e.Apply(e.Preview(Defaults(e,changed),e.Entries.Select(x=>x.Id).ToArray()));return e;}
                var teams0=p.Trainers().ToDictionary(t=>t.Id);
                // Options templates (3DS «Cargar/Exportar template de entrenadores»): options and table rows survive a new editor.
                {
                    var source=new NdsRecordEditor(p,NdsSection.Trainers);var row=source.GetRuleRows().First(r=>r.Group.StartsWith("Jefes",StringComparison.Ordinal));
                    row.CapUse=true;row.Cap=42;row.Max3=true;source.UpdateRuleRows([row],true);
                    string path=Path.Combine(output,"trainer-template.json");source.ExportOptions(path,Defaults(source,("CHK_LevelCaps",1),("NUD_RMax",4)));
                    var loaded=new NdsRecordEditor(p,NdsSection.Trainers);loaded.ImportOptions(path);
                    var fields=loaded.RandomOptions.SelectMany(g=>g.Fields).ToDictionary(f=>f.Key,f=>f.Value);var again=loaded.GetRuleRows().Single(r=>r.Id==row.Id);
                    bool wrongSection=false;try{new NdsRecordEditor(p,NdsSection.Encounters).ImportOptions(path);}catch(InvalidDataException){wrongSection=true;}
                    Check(fields["CHK_LevelCaps"]==1&&fields["NUD_RMax"]==4&&again is {CapUse:true,Cap:42,Max3:true}&&wrongSection,"NdsUi_Real_TrainerTemplateRoundTrip");
                }
                // Trainers: only generation 1 species, no legendaries, at most three Pokémon, six for important trainers, strong attacks from 30.
                var genBoxes=Enumerable.Range(2,gen-1).Select(g=>("CHK_G"+g,0)).ToArray();
                Run(NdsSection.Trainers,[..genBoxes,("CHK_L",0),("CHK_E",0),("NUD_RMax",3),("CHK_6PKM",1),("CHK_ForceHighPower",1),("NUD_ForceHighPower",30),("CHK_ItemClause",1),("CHK_RandomAbilities",1)]);
                var teams1=p.Trainers().ToDictionary(t=>t.Id);
                var changedTeams=teams1.Values.Where(t=>t.Team.Count>0&&teams0[t.Id].Team.Count>0).ToArray();
                Check(changedTeams.All(t=>t.Team.All(m=>m.Species is >=1 and <=151&&!pk3DS.Core.NDS.NdsDataRandomizer.IsLegendary(m.Species))),$"NdsUi_Real_TrainerGenerationAndLegendFilters ({changedTeams.Length})");
                Check(changedTeams.All(t=>NdsTrainerTools.Kind(t.ClassName,t.Name)=="Normal"?t.Team.Count<=3:t.Team.Count==3),"NdsUi_Real_TeamSizeMaxThreeAppliesAfterSixForImportant");
                Check(changedTeams.All(t=>t.Team.Select(m=>m.HeldItem).Where(i=>i>0).Distinct().Count()==t.Team.Count(m=>m.HeldItem>0)||t.Team.Count>6),"NdsUi_Real_ItemClauseNoRepeatedItems");
                var power=Table(NdsSection.Moves);
                Check(changedTeams.SelectMany(t=>t.Team).Where(m=>m.Level>=30).All(m=>m.Moves.Where(x=>x>0).All(x=>power[x]["Power"]>1)),"NdsUi_Real_HighPowerAttacksFromTheLevel");
                Check(gen==4||changedTeams.SelectMany(t=>t.Team).All(m=>m.AbilitySlot is 1 or 2),"NdsUi_Real_Gen5RandomAbilitySlots");
                // Smart Items, competitive quality for every group: items from the smart list only; Eviolite only in Gen5 on species that evolve.
                {
                    Run(NdsSection.Trainers,("CHK_RandomPKM",0),("CHK_SmartHeldItems",1),("CB_SmartHeldItemModeNormal",2),("CB_SmartHeldItemModeImportant",2),("CB_SmartHeldItemModeBoss",2));
                    var smart=new HashSet<int>{234,158,157,266,267,287,220,297,268,275,270,281,538,271,214,285,284,283,282,251,241,244,245,237,238,222,247,233,249,243,239,242,248,246,250,240};
                    var evolves=p.Read(NdsSection.Evolutions).Where(c=>c.Local.EndsWith(".Species",StringComparison.Ordinal)&&c.Value>0&&c.Value!=c.Entry).Select(c=>c.Entry).ToHashSet();
                    var held=p.Trainers().SelectMany(t=>t.Team).Where(m=>m.HeldItem>0).ToArray();
                    Check(held.Length>0&&held.All(m=>smart.Contains(m.HeldItem)&&m.HeldItem!=272&&(m.HeldItem!=538||gen==5&&evolves.Contains(m.Species))),$"NdsUi_Real_SmartItemsCompetitiveFromTheMoveset ({held.Select(m=>m.HeldItem).Distinct().Count()} objetos)");
                }
                // Wild: only the newest generation, distinct species per table, +5 levels.
                var wildBefore=Table(NdsSection.Encounters);
                Run(NdsSection.Encounters,[..Enumerable.Range(1,gen-1).Select(g=>("G"+g,0)),("UniquePerTable",1),("LevelFlat",5)]);
                var wildNow=Table(NdsSection.Encounters);
                int firstNew=gen==4?387:494,last=gen==4?493:649;
                var wildSpecies=wildNow.SelectMany(r=>r.Value.Where(c=>c.Key.EndsWith("Species",StringComparison.Ordinal)&&c.Value>0).Select(c=>c.Value)).ToArray();
                Check(wildSpecies.Length>0&&wildSpecies.All(s=>s>=firstNew&&s<=last),$"NdsUi_Real_WildGenerationFilter ({wildSpecies.Length})");
                var levelCells=wildNow.SelectMany(r=>r.Value.Where(c=>c.Key.Contains("Level",StringComparison.Ordinal)).Select(c=>(r.Key,c.Key,c.Value))).ToArray();
                Check(levelCells.Count(c=>wildBefore[c.Item1][c.Item2]<95&&c.Value==wildBefore[c.Item1][c.Item2]+5)>levelCells.Length/2,"NdsUi_Real_WildFlatLevels");
                // Statics: legendaries stay legendary.
                var staticsBefore=Table(NdsSection.Statics);
                Run(NdsSection.Statics,("LegendToLegend",1));
                var statics=Table(NdsSection.Statics);
                Check(staticsBefore.Where(r=>pk3DS.Core.NDS.NdsDataRandomizer.IsLegendary(r.Value["Species"])).All(r=>pk3DS.Core.NDS.NdsDataRandomizer.IsLegendary(statics[r.Key]["Species"])),"NdsUi_Real_StaticLegendToLegend");
                // Level Up: four moves at level 1, levels spread up to 60, no fixed-damage moves.
                Run(NdsSection.Learnsets,("StartFour",1),("Spread",1),("SpreadTo",60),("ExcludeFixed",1));
                var learn=Table(NdsSection.Learnsets);
                Check(learn.Values.Where(r=>r.ContainsKey("M4.Level")).All(r=>r["M0.Level"]==1&&r["M3.Level"]==1&&r.Where(c=>c.Key.EndsWith(".Level",StringComparison.Ordinal)).All(c=>c.Value<=60))
                    &&learn.Values.All(r=>r.Where(c=>c.Key.EndsWith(".Move",StringComparison.Ordinal)).All(c=>c.Value is not (49 or 82))),"NdsUi_Real_LevelUpStartFourSpreadAndNoFixedDamage");
                // Egg moves without HM moves.
                if(p.SectionProblem(NdsSection.EggMoves) is null)
                {
                    Run(NdsSection.EggMoves,("Hms",0));
                    // The HMs of each game: Diamond/Pearl/Platinum, HeartGold/SoulSilver and Gen5.
                    var hms=gen==5?new HashSet<int>{15,19,57,70,127,291}:p.Game.Contains("HeartGold",StringComparison.Ordinal)?new HashSet<int>{15,19,57,70,249,250,127}:new HashSet<int>{15,19,57,70,432,249,431,127};
                    Check(Table(NdsSection.EggMoves).Values.SelectMany(r=>r.Values).All(m=>!hms.Contains(m)),"NdsUi_Real_EggMovesWithoutHms");
                }
                // Evolutions: trade evolutions at level 30.
                Run(NdsSection.Evolutions,("Species",0),("Trade",1),("TradeLevel",30));
                var evo=p.Read(NdsSection.Evolutions);
                Check(evo.Any(c=>c.Local.EndsWith(".Param",StringComparison.Ordinal)&&c.Value==30),"NdsUi_Real_TradeEvolutionLevel");
                // Pokémon: stats within ±10 %, catch rate 200, Wonder Guard allowed or not.
                var statsBefore=Table(NdsSection.Pokemon);
                Run(NdsSection.Pokemon,("Stats",1),("Deviation",10),("SetCatch",1),("CatchValue",200),("Abilities",1),("WonderGuard",0));
                var stats=Table(NdsSection.Pokemon);
                Check(stats.Where(r=>r.Key>0&&statsBefore[r.Key]["Stat0"]>20).All(r=>Enumerable.Range(0,6).All(i=>Math.Abs(r.Value["Stat"+i]-statsBefore[r.Key]["Stat"+i])<=statsBefore[r.Key]["Stat"+i]/10+1))
                    &&stats.Where(r=>r.Key>0).All(r=>r.Value["CatchRate"]==200)&&stats.Values.All(r=>r.GetValueOrDefault("Ability1")!=25&&r.GetValueOrDefault("Ability2")!=25),"NdsUi_Real_PokemonVariationCatchAndAbilities");
            }
            // Every recipe kind in the template: manual edits, species, data randomizations and (Gen4) game tweaks.
            project.Apply(project.PreviewData("Randomizar Pokémon",NdsSection.Pokemon,new NdsDataOptions(5){Stats=NdsStatsMode.Shuffle,Types=true,Scope=new HashSet<int>{1,4,7}}));
            var ruled=new NdsRecordEditor(project,NdsSection.Trainers);
            ruled.Apply(ruled.Preview(Defaults(ruled,("NUD_RMin",3),("CHK_BetterMovesets",1),("CHK_SmartHeldItems",1),("CHK_MaxAI",1),("CHK_RandomDoubleBattles",1),("NUD_DoubleBattleChance",50)),ruled.Entries.Select(e=>e.Id).ToArray()));
            // Phase 14: TM/tutor lists, tutor compatibility and egg moves, then tweaks of both generations (Pickup with its seed).
            foreach(var section in new[]{NdsSection.MoveLists,NdsSection.Tutors,NdsSection.EggMoves}.Where(s=>project.SectionProblem(s) is null))
                {var editor=new NdsRecordEditor(project,section);editor.Apply(editor.Preview(Defaults(editor),editor.Entries.Select(e=>e.Id).ToArray()));}
            project.Apply(project.PreviewTweaks(project.Generation==4?new NdsTweakOptions{FastestText=project.Arm9TweaksProblem is null,NationalDexAtStart=true,UpdateTypeEffectiveness=true,GuaranteedCatch=true,RandomPickupItems=true,Seed=9}
                :new NdsTweakOptions{RunWithoutRunningShoes=true,UpdateTypeEffectiveness=true,GuaranteedCatch=true,DisableLowHpMusic=true,RandomPickupItems=true,Seed=9}));
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
            Pump(workspace.NavigateAsync("Template"));Capture(workspace,"nds-workspace-template");Pump(workspace.NavigateAsync("Tweaks"));Capture(workspace,"nds-workspace-tweaks-gen"+project.Generation);
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
