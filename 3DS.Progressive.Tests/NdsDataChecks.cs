using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.NDS;

// Species, moves, learnsets, evolutions and TM/HM data for Gen4 and Gen5: synthetic layouts for CI and a real-dump probe.
internal static class NdsDataChecks
{
    private sealed record Game(string Name, NdsDataSource Source, Func<NdsSection,IReadOnlyList<NdsCell>> Read,
        Func<NdsSection,IReadOnlyDictionary<string,int>,Gen5RandomizerResult> Write, Func<NdsSection,NdsDataOptions,Gen5RandomizerResult> Random,
        Func<IReadOnlyDictionary<string,byte[]>,Game> Draft, Func<Gen5RandomizerOptions,Gen5RandomizerResult> Species, Func<NdsTrainerOptions,Gen5RandomizerResult> Trainers);

    private static Game Of(string name,Gen5Data data)=>new(name,NdsDataSource.Of(data),s=>NdsCells.Read(data,s),(s,e)=>NdsCells.Write(data,s,e),
        (s,o)=>NdsDataRandomizer.Generate(data,s,o),files=>Of(name,data.WithDraft(files,new Dictionary<int,byte[]>())),o=>Gen5Randomizer.Generate(data,o),o=>NdsTrainerTools.Generate(data,o));
    private static Game Of(string name,Gen4Data data)=>new(name,NdsDataSource.Of(data),s=>NdsCells.Read(data,s),(s,e)=>NdsCells.Write(data,s,e),
        (s,o)=>NdsDataRandomizer.Generate(data,s,o),files=>Of(name,data.WithDraft(files,new Dictionary<int,byte[]>(),null)),o=>Gen4Randomizer.Generate(data,o),o=>NdsTrainerTools.Generate(data,o));

    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        // IPS: a plain record and a run-length record, then a patch that would grow the target is rejected.
        byte[] target=new byte[16];
        int written=Ips.Apply(target,[.."PATCH"u8,0,0,2,0,3,1,2,3,0,0,8,0,0,0,4,9,.."EOF"u8]);
        check(written==7&&target.AsSpan(2,3).SequenceEqual(new byte[]{1,2,3})&&target.AsSpan(8,4).ToArray().All(b=>b==9)&&target[0]==0&&target[12]==0,"NdsTweaks_IpsWritesRecordsAndRuns");
        reject(()=>Ips.Apply(new byte[4],[.."PATCH"u8,0,0,2,0,3,1,2,3,.."EOF"u8]),"NdsTweaks_IpsRejectsRecordsBeyondTheTarget");
        reject(()=>Ips.Apply(new byte[4],[.."NOPE!"u8,.."EOF"u8]),"NdsTweaks_IpsRejectsOtherFormats");
        string temp=Path.Combine(Path.GetTempPath(),"nds-data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            string black=Path.Combine(temp,"black.nds");File.WriteAllBytes(black,NdsGen5RandomizerChecks.SyntheticBlackRom());
            using(var rom=NdsRom.Open(black))Exercise(Of("Gen5",Gen5Data.Open(rom,requireOriginalTables:false)),check,reject);
            foreach(var profile in Gen4Profile.All.Where(p=>p.Kind!=Gen4Kind.Platinum))
            {
                string path=Path.Combine(temp,profile.GameCode+".nds");File.WriteAllBytes(path,NdsGen4Checks.SyntheticRom(profile));
                using var rom=NdsRom.Open(path);Exercise(Of(profile.Kind==Gen4Kind.DiamondPearl?"Dp":"Hgss",Gen4Data.Open(rom,requireOriginalTables:false)),check,reject);
            }
        }
        finally{try{Directory.Delete(temp,true);}catch(IOException){}}
    }

    private static Dictionary<int,Dictionary<string,int>> Table(IEnumerable<NdsCell> cells)=>cells.GroupBy(c=>c.Entry).ToDictionary(g=>g.Key,g=>g.ToDictionary(c=>c.Local,c=>c.Value));
    private static int Total(Dictionary<string,int> row)=>Enumerable.Range(0,6).Sum(i=>row["Stat"+i]);

    private static void Exercise(Game game,Action<bool,string> check,Action<Action,string> reject)
    {
        string n=game.Name;var s=game.Source;
        var pokemon=Table(game.Read(NdsSection.Pokemon));var learn=Table(game.Read(NdsSection.Learnsets));var evo=Table(game.Read(NdsSection.Evolutions));var tms=Table(game.Read(NdsSection.Machines));
        check(pokemon.Count==s.MaxSpecies&&game.Read(NdsSection.Moves).Select(c=>c.Entry).Distinct().Count()==s.MoveNames.Count-1&&learn[1]["M0.Move"]==33&&learn[1]["M1.Move"]==45&&learn[1]["M1.Level"]==3
            &&evo[1]["E0.Method"]==4&&evo[1]["E0.Param"]==16&&evo[1]["E0.Species"]==2&&tms[1].Count==s.TmCount+s.HmCount,$"NdsData_{n}_ReadsSpeciesMovesLearnsetsEvolutionsAndMachines");
        // Manual edits, including a packed Gen4 learnset level and one TM bit, read back through a draft.
        var stats=game.Write(NdsSection.Pokemon,new Dictionary<string,int>{["1:Stat0"]=99,["1:Type2"]=5});
        var level=game.Write(NdsSection.Learnsets,new Dictionary<string,int>{["1:M1.Level"]=7});
        var machine=game.Write(NdsSection.Machines,new Dictionary<string,int>{["1:B3"]=1});
        var draft=game.Draft(stats.Files.Concat(level.Files).GroupBy(f=>f.Key).ToDictionary(g=>g.Key,g=>g.Last().Value));
        var tmDraft=game.Draft(machine.Files);
        var edited=Table(draft.Read(NdsSection.Pokemon))[1];var editedLearn=Table(draft.Read(NdsSection.Learnsets))[1];var editedTm=Table(tmDraft.Read(NdsSection.Machines))[1];
        check(edited["Stat0"]==99&&edited["Type2"]==5&&editedLearn["M1.Level"]==7&&editedLearn["M1.Move"]==45&&editedTm["B3"]==1&&editedTm["B2"]==tms[1]["B2"],$"NdsData_{n}_ManualEditsReadBackAndKeepNeighbourBits");
        reject(()=>game.Write(NdsSection.Moves,new Dictionary<string,int>{["1:Priority"]=9}),$"NdsData_{n}_RejectsValuesOutOfRange");

        Dictionary<int,Dictionary<string,int>> After(NdsSection section,NdsDataOptions options)=>Table(game.Draft(game.Random(section,options).Files).Read(section));
        var shuffled=After(NdsSection.Pokemon,new NdsDataOptions(5){Stats=NdsStatsMode.Shuffle});var spread=After(NdsSection.Pokemon,new NdsDataOptions(5){Stats=NdsStatsMode.Random});
        check(pokemon.All(p=>Total(shuffled[p.Key])==Total(p.Value)&&Total(spread[p.Key])==Total(p.Value))&&pokemon.Any(p=>spread[p.Key]["Stat0"]!=p.Value["Stat0"]),$"NdsData_{n}_StatsShuffleAndRandomKeepEachTotal");
        var typed=After(NdsSection.Pokemon,new NdsDataOptions(6){Types=true,Abilities=true});
        check(typed[2]["Type1"]==typed[1]["Type1"]&&typed[2]["Type2"]==typed[1]["Type2"]&&typed[3]["Type1"]==typed[1]["Type1"]&&typed.Values.All(r=>s.UsableTypes.Contains(r["Type1"])&&s.UsableTypes.Contains(r["Type2"]))
            &&typed.Values.All(r=>r.Where(p=>p.Key.StartsWith("Ability")).All(a=>a.Value is not (25 or 59 or 121 or 122 or 149 or 161))),$"NdsData_{n}_TypesAndAbilitiesFollowEvolutionsAndSkipBannedOnes");
        var caught=After(NdsSection.Pokemon,new NdsDataOptions(6){MinCatchRate=200});
        check(caught.Values.All(r=>r["CatchRate"]>=200),$"NdsData_{n}_MinimumCatchRate");
        var moves=Table(game.Read(NdsSection.Moves));var powered=After(NdsSection.Moves,new NdsDataOptions(7){MovePower=true,MovePp=true});
        check(powered.All(p=>moves[p.Key]["Power"]<=1?p.Value["Power"]==moves[p.Key]["Power"]:p.Value["Power"] is >=20 and <=150)&&powered.Values.All(r=>r["PP"] is >=5 and <=40),$"NdsData_{n}_MovePowerAndPpWithinRange");
        var random=After(NdsSection.Learnsets,new NdsDataOptions(8){Learnsets=NdsLearnsetMode.Random});var metronome=After(NdsSection.Learnsets,new NdsDataOptions(8){Learnsets=NdsLearnsetMode.Metronome});
        check(random.Values.All(r=>moves[r["M0.Move"]]["Power"]>0&&r["M0.Level"]==1)&&random.Values.Any(r=>r["M0.Move"]!=33)&&metronome.Values.All(r=>r.Where(p=>p.Key.EndsWith(".Move")).All(m=>m.Value==118)),$"NdsData_{n}_LearnsetsRandomStartDamagingAndMetronomeOnly");
        var capped=After(NdsSection.Evolutions,new NdsDataOptions(9){MaxEvolutionLevel=20});
        check(capped[1]["E0.Param"]==16&&capped[2]["E0.Param"]==20&&capped[4]["E0.Method"]==evo[4]["E0.Method"],$"NdsData_{n}_MaxEvolutionLevelOnlyLowersLevelMethods");
        var traded=After(NdsSection.Evolutions,new NdsDataOptions(9){RemoveTradeEvolutions=true});
        check(traded[4]["E0.Method"]==4&&traded[4]["E0.Param"]==37&&traded[4]["E0.Species"]==5&&traded[1]["E0.Method"]==4&&traded[1]["E0.Param"]==16,$"NdsData_{n}_TradeEvolutionsBecomeLevel37");
        var full=After(NdsSection.Machines,new NdsDataOptions(10){Machines=NdsMachineMode.Full});var kept=After(NdsSection.Machines,new NdsDataOptions(10){Machines=NdsMachineMode.Random,MachinePercent=100});
        check(full.Values.All(r=>r.Values.All(v=>v==1))&&kept.All(p=>p.Value.Where(b=>int.Parse(b.Key[1..])>=s.TmCount).All(b=>b.Value==tms[p.Key][b.Key])),$"NdsData_{n}_MachinesFullOrRandomKeepingHms");
        // Trades: given and requested species randomized, the empty request (0) kept.
        var trades=Table(game.Read(NdsSection.Trades));var traded2=After(NdsSection.Trades,new NdsDataOptions(12){TradeGiven=true,TradeRequested=true});
        check(trades.Count==2&&trades[0]["Given"]==25&&trades[0]["Requested"]==100&&trades[0]["Item"]==1&&traded2[1]["Requested"]==0&&traded2.Values.All(r=>r["Given"] is >=1&&r["Given"]<=s.MaxSpecies)
            &&traded2.Values.Any(r=>r["Given"]!=25&&r["Given"]!=129),$"NdsData_{n}_TradesRandomizeGivenAndRequested");
        // Trainers: one type per trainer, levels +50 % and final evolutions from level 30; wild levels -50 % and a global mapping.
        var themed=game.Species(new Gen5RandomizerOptions(13){Trainers=Gen5SpeciesMode.Random,TypeThemedTrainers=true,TrainerLevelPercent=50,ForceFullyEvolvedLevel=30});
        var themedDraft=game.Draft(themed.Files);var team=Table(themedDraft.Read(NdsSection.Trainers));var newStats=Table(themedDraft.Read(NdsSection.Pokemon));
        bool SharesType(Dictionary<string,int> row)=>Enumerable.Range(0,6).Where(i=>row.ContainsKey($"P{i}.Species")).Select(i=>newStats[row[$"P{i}.Species"]]).Select(p=>new[]{p["Type1"],p["Type2"]})
            .Aggregate((IEnumerable<int>?)null,(acc,t)=>acc is null?t:acc.Intersect(t)) is {} common&&common.Any();
        var oldTeam=Table(game.Read(NdsSection.Trainers));
        check(team.Values.All(SharesType)&&team.All(t=>t.Value.Where(p=>p.Key.EndsWith(".Level")).All(l=>l.Value==Math.Clamp((int)Math.Round(oldTeam[t.Key][l.Key]*1.5),1,100))),$"NdsData_{n}_TrainersTypeThemedWithLevelsRaised");
        var forced=game.Species(new Gen5RandomizerOptions(14){ForceFullyEvolvedLevel=1});var forcedTeam=Table(game.Draft(forced.Files).Read(NdsSection.Trainers));
        check(forced.Changes.All(c=>c.Field=="Especie")&&forcedTeam.Values.SelectMany(r=>r.Where(p=>p.Key.EndsWith(".Species"))).All(p=>p.Value is not (1 or 2)),$"NdsData_{n}_ForceFullyEvolvedKeepsOtherFields");
        var wild=game.Species(new Gen5RandomizerOptions(15){Wild=Gen5SpeciesMode.Random,WildGlobalMapping=true,WildLevelPercent=-50});
        var oldWild=game.Read(NdsSection.Encounters);var newWild=game.Draft(wild.Files).Read(NdsSection.Encounters);
        var mapping=oldWild.Zip(newWild).Where(p=>p.First.Kind==NdsValueKind.Species&&p.First.Value>0).GroupBy(p=>p.First.Value).ToArray();
        check(mapping.All(g=>g.Select(p=>p.Second.Value).Distinct().Count()==1)&&oldWild.Zip(newWild).Where(p=>p.First.Field.Contains("nivel")).All(p=>p.Second.Value==Math.Clamp((int)Math.Round(p.First.Value*0.5),1,100)),
            $"NdsData_{n}_WildGlobalMappingAndLevelsLowered");
        // Trainer rules: three Pokémon minimum, level-up moves and held items added to the format, IVs and AI at the maximum.
        var ruled=game.Trainers(new NdsTrainerOptions(16){MinTeamSize=3,Moves=NdsTrainerMoveMode.LevelUp,Items=NdsTrainerItemMode.Smart,MaxIvs=true,MaxAi=true});
        var ruledTeams=Table(game.Draft(ruled.Files).Read(NdsSection.Trainers));var learnTable=Table(game.Read(NdsSection.Learnsets));
        int[] AtLevel(int species,int level){var row=learnTable[species];var l=new List<int>();for(int i=0;row.ContainsKey($"M{i}.Move");i++)if(row[$"M{i}.Level"]<=level){l.Remove(row[$"M{i}.Move"]);l.Add(row[$"M{i}.Move"]);}var t=l.TakeLast(4).ToList();while(t.Count<4)t.Add(0);return [..t];}
        bool Ruled(Dictionary<string,int> team)=>Enumerable.Range(0,3).All(i=>team.ContainsKey($"P{i}.Species")&&team.ContainsKey($"P{i}.Item")&&team[$"P{i}.Item"]>0
            &&Enumerable.Range(0,4).Select(m=>team[$"P{i}.Move{m+1}"]).SequenceEqual(AtLevel(team[$"P{i}.Species"],team[$"P{i}.Level"])));
        check(ruledTeams.Count==oldTeam.Count&&ruledTeams.Values.All(Ruled)&&ruled.Changes.Any(c=>c.Field=="IA")&&ruled.Changes.Any(c=>c.Field.EndsWith("Dificultad (IV)")),$"NdsData_{n}_TrainerRulesRewriteTeams");
        var stripped=game.Draft(ruled.Files).Trainers(new NdsTrainerOptions(17){Items=NdsTrainerItemMode.None});
        check(Table(game.Draft(stripped.Files).Read(NdsSection.Trainers)).Values.All(t=>!t.Keys.Any(k=>k.EndsWith(".Item"))&&t.ContainsKey("P0.Move1")),$"NdsData_{n}_TrainerItemsRemovedKeepsMoves");
        var better=game.Trainers(new NdsTrainerOptions(18){Moves=NdsTrainerMoveMode.Better});var betterTeams=Table(game.Draft(better.Files).Read(NdsSection.Trainers));var moveTable=Table(game.Read(NdsSection.Moves));
        check(betterTeams.Values.All(t=>Enumerable.Range(0,6).Where(i=>t.ContainsKey($"P{i}.Move1")).All(i=>Enumerable.Range(1,4).Select(m=>t[$"P{i}.Move{m}"]).Where(m=>m>0).Distinct().Count()==Enumerable.Range(1,4).Count(m=>t[$"P{i}.Move{m}"]>0))),
            $"NdsData_{n}_BetterMovesetsWithoutRepeats");
        // Growth standardized (legendaries slow), wild items from the held-item list or empty; no legendaries for trainers and wild.
        var grown=After(NdsSection.Pokemon,new NdsDataOptions(19){StandardizeGrowth=true,WildHeldItems=true});
        check(grown.All(p=>p.Value["Growth"]==(NdsDataRandomizer.IsLegendary(p.Key)?5:0)&&p.Value.Where(k=>k.Key.StartsWith("Item")).All(k=>k.Value==0||NdsTrainerTools.HeldItemIds.Contains(k.Value)))
            &&grown.Values.Any(r=>r["Item1"]>0),$"NdsData_{n}_GrowthStandardizedAndWildItems");
        var common=game.Species(new Gen5RandomizerOptions(20){Trainers=Gen5SpeciesMode.Random,Wild=Gen5SpeciesMode.Random,BanLegendaries=true});
        check(common.Changes.Where(c=>c.Field=="Especie"||c.Field.EndsWith("especie")).All(c=>!NdsDataRandomizer.IsLegendary(c.After)),$"NdsData_{n}_NoLegendariesForTrainersAndWild");
        // Egg moves (Gen5 and HeartGold layouts): read, randomized keeping each list's length, Metronome only.
        if(n!="Dp")
        {
            var eggs=Table(game.Read(NdsSection.EggMoves));var randomEggs=After(NdsSection.EggMoves,new NdsDataOptions(21){EggMoves=NdsLearnsetMode.Random});
            var metronomeEggs=After(NdsSection.EggMoves,new NdsDataOptions(21){EggMoves=NdsLearnsetMode.Metronome});
            check(eggs[1]["E0"]==33&&eggs[1]["E1"]==45&&randomEggs.All(p=>p.Value.Count==eggs[p.Key].Count)&&randomEggs[1]["E0"]!=randomEggs[1]["E1"]&&metronomeEggs.Values.All(r=>r.Values.All(m=>m==118))
                &&game.Write(NdsSection.EggMoves,new Dictionary<string,int>{["1:E1"]=50}).Changes.Single().After==50,$"NdsData_{n}_EggMovesReadRandomizedAndEdited");
        }
        // Black and Diamond have no move tutors.
        if(n!="Hgss")check(game.Read(NdsSection.Tutors).Count==0&&game.Read(NdsSection.MoveLists).All(c=>c.Entry==0),$"NdsData_{n}_NoTutorsInBlackAndDiamond");
        var a=game.Random(NdsSection.Pokemon,new NdsDataOptions(11){Stats=NdsStatsMode.Random,Types=true});var b=game.Random(NdsSection.Pokemon,new NdsDataOptions(11){Stats=NdsStatsMode.Random,Types=true});
        var scoped=game.Random(NdsSection.Pokemon,new NdsDataOptions(11){Stats=NdsStatsMode.Random,Scope=new HashSet<int>{1}});
        check(a.Changes.SequenceEqual(b.Changes)&&scoped.Changes.All(c=>c.Location.StartsWith(s.SpeciesNames[1]+" ",StringComparison.Ordinal)),$"NdsData_{n}_DeterministicAndScoped");
    }

    // Real dumps: every data section edited and randomized, stacked through drafts, exported once and read back.
    internal static void Probe(string output,IEnumerable<string> roms)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name+(name.Contains("Shuffled")?"":""));checks++;Console.WriteLine("PASS "+name);}
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");Directory.CreateDirectory(output);
        NdsSection[] sections=[NdsSection.Pokemon,NdsSection.Moves,NdsSection.Learnsets,NdsSection.Evolutions,NdsSection.Machines,NdsSection.Statics,NdsSection.Trades,NdsSection.Trainers,NdsSection.Encounters];
        foreach(string source in roms.Select(Path.GetFullPath))
        {
            string before=Hash(source),code;
            using(var rom=NdsRom.Open(source))
            {
                bool gen4=NdsIdentity.Identify(rom).Profile.Generation==4;
                Gen5Data? g5=gen4?null:Gen5Data.Open(rom);Gen4Data? g4=gen4?Gen4Data.Open(rom):null;
                code=rom.Header.GameCode;var game=g4 is not null?Of(code,g4):Of(code,g5!);var s=game.Source;
                // First TM, first HM and the Bulbasaur line as every game has them.
                int firstTm=gen4?264:468;
                Check(s.MachineMoves.Count==s.TmCount+s.HmCount&&s.MachineMoves[0]==firstTm&&s.MachineMoves[s.TmCount]==15,$"NdsData_Real_{code}_TmListFromTheArm9 ({s.MachineName(0)}, {s.MachineName(s.TmCount)})");
                var pokemon=Table(game.Read(NdsSection.Pokemon));var evo=Table(game.Read(NdsSection.Evolutions));var learn=Table(game.Read(NdsSection.Learnsets));
                Check(pokemon.Count==s.MaxSpecies&&Total(pokemon[1])==318&&evo[1]["E0.Species"]==2&&evo[1]["E0.Param"]==16&&learn[1]["M0.Move"]==33&&s.TypeNames[pokemon[1]["Type1"]] is var grass&&(grass.Equals("Grass",StringComparison.OrdinalIgnoreCase)||grass.Equals("Planta",StringComparison.OrdinalIgnoreCase)),
                    $"NdsData_Real_{code}_ReadsBulbasaur");
                var files=new Dictionary<string,byte[]>();var current=game;int changes=0;byte[]? tweakArm9=null;
                void Stack(Gen5RandomizerResult result){foreach(var (path,data) in result.Files)files[path]=data;current=game.Draft(new Dictionary<string,byte[]>(files));changes+=result.Changes.Count;}
                Stack(current.Random(NdsSection.Pokemon,new NdsDataOptions(21){Stats=NdsStatsMode.Shuffle,Types=true,Abilities=true,MinCatchRate=60}));
                Stack(current.Random(NdsSection.Moves,new NdsDataOptions(22){MovePower=true,MoveAccuracy=true,MovePp=true}));
                Stack(current.Random(NdsSection.Learnsets,new NdsDataOptions(23){Learnsets=NdsLearnsetMode.Random}));
                Stack(current.Random(NdsSection.Evolutions,new NdsDataOptions(24){RemoveTradeEvolutions=true}));
                Stack(current.Random(NdsSection.Machines,new NdsDataOptions(25){Machines=NdsMachineMode.TypeBased}));
                Stack(current.Write(NdsSection.Pokemon,new Dictionary<string,int>{["1:Stat0"]=99}));
                var statics=Table(current.Read(NdsSection.Statics));var trades=Table(current.Read(NdsSection.Trades));
                Check(statics.Count>=10&&trades.Count>=3,$"NdsData_Real_{code}_ReadsStaticsAndTrades ({statics.Count} estáticos, {trades.Count} intercambios)");
                Stack(current.Random(NdsSection.Statics,new NdsDataOptions(26){Statics=NdsStaticMode.SwapLegendaries}));
                Stack(current.Random(NdsSection.Trades,new NdsDataOptions(27){TradeGiven=true,TradeRequested=true}));
                Stack(current.Species(new Gen5RandomizerOptions(28){Trainers=Gen5SpeciesMode.ProgressiveBst,TypeThemedTrainers=true,ForceFullyEvolvedLevel=40,TrainerLevelPercent=10,Wild=Gen5SpeciesMode.SimilarBst,WildGlobalMapping=true,WildLevelPercent=10}));
                Stack(current.Random(NdsSection.Evolutions,new NdsDataOptions(29){RemoveTimeEvolutions=true,MaxEvolutionLevel=40}));
                var timed=Table(current.Read(NdsSection.Evolutions));
                bool NoTime(Dictionary<string,int> row)=>row.Where(p=>p.Key.EndsWith(".Method")).All(m=>m.Value>=s.EvolutionMethods.Count||!s.EvolutionMethods[m.Value].Contains(" de día")&&!s.EvolutionMethods[m.Value].Contains(" de noche"));
                Check(timed.Values.All(NoTime)&&timed.Values.All(r=>Enumerable.Range(0,7).All(i=>s.EvolutionMethods[Math.Min(r["E"+i+".Method"],s.EvolutionMethods.Count-1)]!="Nivel"||r["E"+i+".Param"]<=40)),$"NdsData_Real_{code}_NoTimeEvolutionsAndLevelsCapped");
                if(g4 is not null)
                {
                    // Gen4 tweaks: IPS on the ARM9 and the national-dex script, the running-shoes jump cleared; a second script patch is refused.
                    var tweaks=NdsTweaks.Apply(g4.WithDraft(files,new Dictionary<int,byte[]>(),null),new NdsTweakOptions{FastestText=true,NationalDexAtStart=true,RunWithoutRunningShoes=true,FastDistortionWorld=g4.Profile.Kind==Gen4Kind.Platinum});
                    foreach(var (path,bytes) in tweaks.Files)files[path]=bytes;
                    var tweakedArm9=tweaks.Arm9!;var originalArm9=g4.Arm9();
                    int differ=Enumerable.Range(0,originalArm9.Length).Count(i=>tweakedArm9[i]!=originalArm9[i]);
                    Check(differ is >0 and <64&&tweakedArm9.Length==originalArm9.Length&&tweaks.Changes.Count==(g4.Profile.Kind==Gen4Kind.Platinum?4:3),$"NdsData_Real_{code}_TweaksPatchTheArm9AndScripts ({differ} bytes del ARM9)");
                    var tweakedDraft=g4.WithDraft(files,new Dictionary<int,byte[]>(),tweakedArm9);
                    bool refused=false;try{NdsTweaks.Apply(tweakedDraft,new NdsTweakOptions{NationalDexAtStart=true});}catch(InvalidDataException){refused=true;}
                    Check(refused,$"NdsData_Real_{code}_ScriptTweaksApplyOnlyOnce");
                    current=Of(code,g4.WithDraft(new Dictionary<string,byte[]>(files),new Dictionary<int,byte[]>(),tweakedArm9));tweakArm9=tweakedArm9;
                }
                var swapped=Table(current.Read(NdsSection.Statics));
                Check(swapped.All(p=>NdsDataRandomizer.IsLegendary(p.Value["Species"])==NdsDataRandomizer.IsLegendary(statics[p.Key]["Species"]))&&swapped.Any(p=>p.Value["Species"]!=statics[p.Key]["Species"]),$"NdsData_Real_{code}_StaticsSwapLegendariesForLegendaries");
                var final=sections.ToDictionary(x=>x,x=>current.Read(x));
                var traded=Table(final[NdsSection.Evolutions]);
                Check(Table(final[NdsSection.Pokemon]).Values.All(r=>r["CatchRate"]>=60)&&traded.Values.All(r=>r.Where(p=>p.Key.EndsWith(".Method")).All(m=>!s.IsTradeMethod(m.Value)))&&Table(final[NdsSection.Pokemon])[1]["Stat0"]==99,
                    $"NdsData_Real_{code}_RandomizationsAndEditsStack ({changes} cambios)");
                string target=Path.Combine(output,code+"-data.nds");
                var all=new Gen5RandomizerResult(files,[],new Dictionary<int,byte[]>()){Arm9=tweakArm9};
                if(g4 is not null)g4.Export(target,all);else g5!.Export(target,all);
                using var exported=NdsRom.Open(target);
                var again=gen4?Of(code,Gen4Data.Open(exported,requireOriginalTables:false)):Of(code,Gen5Data.Open(exported,requireOriginalTables:false));
                // A recompressed ARM9 (HeartGold) differs only in the 4-byte "compressed end" of its module parameters.
                byte[]? readArm9=tweakArm9 is null?null:exported.ReadArm9();int[] arm9Diff=readArm9 is null?[]:Enumerable.Range(0,readArm9.Length).Where(i=>readArm9[i]!=tweakArm9![i]).ToArray();
                Check((tweakArm9 is null||readArm9!.Length==tweakArm9.Length&&(arm9Diff.Length==0||arm9Diff[^1]-arm9Diff[0]<4))&&sections.All(x=>again.Read(x).Select(c=>(c.Key,c.Value)).SequenceEqual(final[x].Select(c=>(c.Key,c.Value)))),$"NdsData_Real_{code}_ExportReadsBackEverySection");
                var changedIds=files.Keys.Select(p=>rom.File(p).Id).ToHashSet();
                Check(exported.Files.Where(f=>!changedIds.Contains(f.Id)).All(f=>exported.ReadFile(f).SequenceEqual(rom.ReadFile(rom.Files[f.Id]))),$"NdsData_Real_{code}_EveryOtherFileIdentical");
            }
            // Trainer rules and double battles on every trainer, exported and read back; a second application patches no code again.
            using(var rom=NdsRom.Open(source))
            {
                bool gen4=NdsIdentity.Identify(rom).Profile.Generation==4;
                Gen5Data? g5=gen4?null:Gen5Data.Open(rom);Gen4Data? g4=gen4?Gen4Data.Open(rom):null;
                var rules=new NdsTrainerOptions(31){MinTeamSize=2,Moves=NdsTrainerMoveMode.Better,Items=NdsTrainerItemMode.Smart,MaxIvs=true,MaxAi=true,DoubleChance=100};
                var first=gen4?NdsTrainerTools.Generate(g4!,rules):NdsTrainerTools.Generate(g5!,rules);
                int doubles=first.Changes.Count(c=>c.Field=="Combate doble");
                Check(doubles>100&&first.Changes.Any(c=>c.Location=="Combates dobles")&&(gen4?first.Arm9 is not null:first.Overlays.Count==1),$"NdsData_Real_{code}_DoubleBattlesPatchCodeOnce ({doubles} entrenadores)");
                var again=gen4?NdsTrainerTools.Generate(g4!.WithDraft(first.Files,first.Overlays,first.Arm9),rules):NdsTrainerTools.Generate(g5!.WithDraft(first.Files,first.Overlays),rules);
                Check(!again.Changes.Any(c=>c.Location=="Combates dobles"||c.Field=="Combate doble")&&again.Arm9 is null&&again.Overlays.Count==0,$"NdsData_Real_{code}_DoubleBattlesNotPatchedTwice");
                string target=Path.Combine(output,code+"-trainers.nds");
                if(gen4)g4!.Export(target,first);else g5!.Export(target,first);
                using var exported=NdsRom.Open(target);
                var trainers=gen4?Gen4Data.Open(exported,requireOriginalTables:false).Trainers():Gen5Data.Open(exported,requireOriginalTables:false).Trainers();
                var originals=gen4?g4!.Trainers():g5!.Trainers();
                bool teams=trainers.Zip(originals).All(p=>p.Second.Team.Count==0||p.First.Team.Count>=2&&p.First.HasMoves&&p.First.HasItems&&p.First.Team.All(m=>m.Difficulty==255&&m.HeldItem>0&&m.Moves.Count(x=>x>0)>=1));
                Check(teams&&trainers.Count(t=>t.BattleType!=0)>=doubles,$"NdsData_Real_{code}_TrainerRulesExportAndReadBack");
            }
            // Phase 14: egg moves, TM and tutor lists, tutor compatibility, starter item, trainer names and overlay tweaks, stacked, exported and read back.
            using(var rom=NdsRom.Open(source))
            {
                bool gen4=NdsIdentity.Identify(rom).Profile.Generation==4;
                Gen5Data? g5=gen4?null:Gen5Data.Open(rom);Gen4Data? g4=gen4?Gen4Data.Open(rom):null;
                var files=new Dictionary<string,byte[]>();var overlays=new Dictionary<int,byte[]>();var recompress=new HashSet<int>();byte[]? arm9=null;
                Gen4Data D4()=>g4!.WithDraft(new Dictionary<string,byte[]>(files),new Dictionary<int,byte[]>(overlays),arm9);
                Gen5Data D5()=>g5!.WithDraft(new Dictionary<string,byte[]>(files),new Dictionary<int,byte[]>(overlays));
                IReadOnlyList<NdsCell> Read(NdsSection x)=>gen4?NdsCells.Read(D4(),x):NdsCells.Read(D5(),x);
                Gen5RandomizerResult Data(NdsSection x,NdsDataOptions o)=>gen4?NdsDataRandomizer.Generate(D4(),x,o):NdsDataRandomizer.Generate(D5(),x,o);
                void Stack(Gen5RandomizerResult r){foreach(var (p,d) in r.Files)files[p]=d;foreach(var (i,d) in r.Overlays)overlays[i]=d;recompress.UnionWith(r.RecompressOverlays);if(r.Arm9 is not null)arm9=r.Arm9;}
                var eggs=Table(Read(NdsSection.EggMoves));int eggCount=eggs.Values.Sum(r=>r.Count);
                Check(eggs.Count>=190&&eggCount>=1700&&eggs.Values.All(r=>r.Values.All(m=>m is >0 and <560)),$"NdsData_Real_{code}_ReadsEggMoves ({eggs.Count} especies, {eggCount} movimientos)");
                Stack(Data(NdsSection.EggMoves,new NdsDataOptions(41){EggMoves=NdsLearnsetMode.Random}));
                var newEggs=Table(Read(NdsSection.EggMoves));
                Check(newEggs.Keys.Order().SequenceEqual(eggs.Keys.Order())&&newEggs.All(p=>p.Value.Count==eggs[p.Key].Count)&&newEggs.Count(p=>!p.Value.OrderBy(k=>k.Key).Select(k=>k.Value).SequenceEqual(eggs[p.Key].OrderBy(k=>k.Key).Select(k=>k.Value)))>eggs.Count/2,
                    $"NdsData_Real_{code}_EggMovesRandomKeepCounts");
                var lists=Table(Read(NdsSection.MoveLists));
                (int Tms,int Tutors,int FirstTutor) expected=code switch{"ADAE"=>(92,0,0),"CPUE"=>(92,38,291),"IPKE"=>(92,52,291),"IRDO"=>(0,60,20),_=>(0,0,0)};
                Check((lists.TryGetValue(0,out var tmRow)?tmRow.Count:0)==expected.Tms&&(lists.TryGetValue(1,out var tutorRow)?tutorRow.Count:0)==expected.Tutors&&(expected.Tutors==0||tutorRow!["Tutor0"]==expected.FirstTutor),
                    $"NdsData_Real_{code}_ReadsTmAndTutorLists ({expected.Tms} MT, {expected.Tutors} tutores)");
                if(lists.Count>0)
                {
                    var s=gen4?NdsDataSource.Of(g4!):NdsDataSource.Of(g5!);var hms=s.MachineMoves.Skip(s.TmCount).ToHashSet();
                    var listed=Data(NdsSection.MoveLists,new NdsDataOptions(42){RandomTmMoves=true,RandomTutorMoves=true});Stack(listed);
                    var newLists=Table(Read(NdsSection.MoveLists));var taught=newLists.Values.SelectMany(r=>r.Values).ToArray();
                    bool fieldKept=(expected.Tms==0||newLists[0]["TM69"]==148)&&(code!="IPKE"||newLists[1]["Tutor51"]==29);
                    Check(taught.Distinct().Count()==taught.Length&&!taught.Any(hms.Contains)&&fieldKept&&newLists.Sum(p=>p.Value.Count(k=>k.Value!=lists[p.Key][k.Key]))>(expected.Tms+expected.Tutors)/2,
                        $"NdsData_Real_{code}_TmAndTutorMovesRandomWithoutRepeatsKeepingHmsAndFieldMoves");
                    if(expected.Tms>0)
                    {
                        // TM01's item description and disc colour follow its new move.
                        var text=Narc.Read(files[g4!.PathOf(Gen4Profile.Text)]);var oldDesc=g4.Text(g4.Profile.ItemDescriptionsText)[328];var newDesc=Gen4Text.Read(text.Files[g4.Profile.ItemDescriptionsText])[328];
                        Check(newDesc!=oldDesc&&newDesc.Split("\\n").All(l=>l.Length<=38)&&listed.Changes.Any(c=>c.Field=="Textos actualizados")&&!arm9!.SequenceEqual(g4.Arm9()),$"NdsData_Real_{code}_TmDescriptionsAndColoursFollowTheMoves");
                        // A second change of the lists still finds every table (TM01's colour is part of the colour table's prefix).
                        var relisted=Data(NdsSection.MoveLists,new NdsDataOptions(47){RandomTmMoves=true});Stack(relisted);
                        Check(relisted.Changes.Count(c=>c.Field.StartsWith("Movimiento de la MT"))>40&&relisted.Changes.Any(c=>c.Field=="Textos actualizados"),$"NdsData_Real_{code}_TmListChangesTwice");
                    }
                }
                if(expected.Tutors>0)
                {
                    var compat=Table(Read(NdsSection.Tutors));
                    Check(compat.Count==(gen4?Gen4Data.MaxSpecies:Gen5Randomizer.MaxSpecies)&&compat.Values.All(r=>r.Count==expected.Tutors)&&compat.Values.Any(r=>r.Values.Contains(1))&&compat.Values.Any(r=>r.Values.Contains(0)),
                        $"NdsData_Real_{code}_ReadsTutorCompatibility");
                    Stack(Data(NdsSection.Tutors,new NdsDataOptions(43){Tutors=NdsMachineMode.Full}));
                    Check(Table(Read(NdsSection.Tutors)).Values.All(r=>r.Values.All(v=>v==1))&&Table(Read(NdsSection.Pokemon))[1]["Stat0"]==Table(gen4?NdsCells.Read(g4!,NdsSection.Pokemon):NdsCells.Read(g5!,NdsSection.Pokemon))[1]["Stat0"],
                        $"NdsData_Real_{code}_TutorCompatibilityFullKeepsSpeciesData");
                }
                if(code is "ADAE" or "CPUE")
                {
                    var starters=NdsCells.Read(D4(),NdsSection.Starters);
                    Stack(Gen4Randomizer.Generate(D4(),new Gen5RandomizerOptions(44){RandomStarterItem=true}));
                    int item=NdsCells.Read(D4(),NdsSection.Starters).Single(c=>c.Local=="Item").Value;
                    Check(starters.Single(c=>c.Local=="Item").Value==0&&NdsTrainerTools.HeldItemIds.Contains(item),$"NdsData_Real_{code}_StarterHeldItem ({g4!.Text(g4.Profile.ItemNamesText)[item]})");
                }
                var names=gen4?NdsTrainerTools.Generate(D4(),new NdsTrainerOptions(45){ShuffleNames=true,ShuffleClassNames=true}):NdsTrainerTools.Generate(D5(),new NdsTrainerOptions(45){ShuffleNames=true,ShuffleClassNames=true});
                var oldTrainers=gen4?g4!.Trainers():g5!.Trainers();Stack(names);var newTrainers=gen4?D4().Trainers():D5().Trainers();
                Check(newTrainers.Count==oldTrainers.Count&&newTrainers.Zip(oldTrainers).Count(p=>p.First.Name!=p.Second.Name)>oldTrainers.Count/3&&newTrainers.Zip(oldTrainers).Count(p=>p.First.ClassName!=p.Second.ClassName)>oldTrainers.Count/3
                    &&newTrainers.Zip(oldTrainers).All(p=>p.First.Team.Count==p.Second.Team.Count),$"NdsData_Real_{code}_TrainerNamesAndClassesShuffled ({newTrainers.Zip(oldTrainers).Count(p=>p.First.Name!=p.Second.Name)} nombres y {newTrainers.Zip(oldTrainers).Count(p=>p.First.ClassName!=p.Second.ClassName)} clases de {oldTrainers.Count})");
                var tweakOptions=gen4?new NdsTweakOptions{UpdateTypeEffectiveness=true,GuaranteedCatch=true,RandomPickupItems=true,Seed=46}
                    :new NdsTweakOptions{RunWithoutRunningShoes=true,UpdateTypeEffectiveness=true,GuaranteedCatch=true,DisableLowHpMusic=true,RandomPickupItems=true,Seed=46};
                var untweaked=new Dictionary<int,byte[]>(overlays);var tweaks=gen4?NdsTweaks.Apply(D4(),tweakOptions):NdsTweaks.Apply(D5(),tweakOptions);Stack(tweaks);
                int patched=tweaks.Overlays.Sum(o=>{var original=untweaked.TryGetValue(o.Key,out var draft)?draft:gen4?g4!.ReadOverlay(o.Key):g5!.ReadOverlay(o.Key);return Enumerable.Range(0,original.Length).Count(i=>original[i]!=o.Value[i]);});
                Check(patched is >4 and <80&&tweaks.Changes.Count(c=>c.Location=="Recogida")==29&&tweaks.Overlays.Keys.All(id=>(gen4?g4!.KnownOverlays:g5!.KnownOverlays).ContainsKey(id)),
                    $"NdsData_Real_{code}_OverlayTweaksAndPickup ({patched} bytes en {tweaks.Overlays.Count} overlays)");
                bool twice=false;try{if(gen4)NdsTweaks.Apply(D4(),new NdsTweakOptions{UpdateTypeEffectiveness=true});else NdsTweaks.Apply(D5(),new NdsTweakOptions{GuaranteedCatch=true});}catch(InvalidDataException){twice=true;}
                Check(twice,$"NdsData_Real_{code}_OverlayTweaksApplyOnlyOnce");
                NdsSection[] read=[NdsSection.EggMoves,NdsSection.MoveLists,NdsSection.Tutors,NdsSection.Starters,NdsSection.Trainers];
                var final=read.ToDictionary(x=>x,x=>Read(x).Select(c=>(c.Key,c.Value)).ToArray());
                string target=Path.Combine(output,code+"-fase14.nds");
                var all=new Gen5RandomizerResult(files,[],overlays){Arm9=arm9,RecompressOverlays=recompress};
                if(gen4)g4!.Export(target,all);else g5!.Export(target,all);
                using var exported=NdsRom.Open(target);
                Func<NdsSection,IReadOnlyList<NdsCell>> again=gen4?x=>NdsCells.Read(Gen4Data.Open(exported,requireOriginalTables:false),x):x=>NdsCells.Read(Gen5Data.Open(exported,requireOriginalTables:false),x);
                Check(read.All(x=>again(x).Select(c=>(c.Key,c.Value)).SequenceEqual(final[x])),$"NdsData_Real_{code}_Phase14ExportReadsBack");
            }
            foreach(string f in Directory.GetFiles(output))File.Delete(f);
            Check(Hash(source)==before,$"NdsData_Real_{code}_SourceUnchanged");
        }
        Console.WriteLine($"Completed {checks} DS data real-dump checks; outputs deleted from {output}.");
    }

    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
}
