using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.NDS;

// Species, moves, learnsets, evolutions and TM/HM data for Gen4 and Gen5: synthetic layouts for CI and a real-dump probe.
internal static class NdsDataChecks
{
    private sealed record Game(string Name, NdsDataSource Source, Func<NdsSection,IReadOnlyList<NdsCell>> Read,
        Func<NdsSection,IReadOnlyDictionary<string,int>,Gen5RandomizerResult> Write, Func<NdsSection,NdsDataOptions,Gen5RandomizerResult> Random,
        Func<IReadOnlyDictionary<string,byte[]>,Game> Draft);

    private static Game Of(string name,Gen5Data data)=>new(name,NdsDataSource.Of(data),s=>NdsCells.Read(data,s),(s,e)=>NdsCells.Write(data,s,e),
        (s,o)=>NdsDataRandomizer.Generate(data,s,o),files=>Of(name,data.WithDraft(files,new Dictionary<int,byte[]>())));
    private static Game Of(string name,Gen4Data data)=>new(name,NdsDataSource.Of(data),s=>NdsCells.Read(data,s),(s,e)=>NdsCells.Write(data,s,e),
        (s,o)=>NdsDataRandomizer.Generate(data,s,o),files=>Of(name,data.WithDraft(files,new Dictionary<int,byte[]>(),null)));

    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
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
        var traded=After(NdsSection.Evolutions,new NdsDataOptions(9){RemoveTradeEvolutions=true});
        check(traded[4]["E0.Method"]==4&&traded[4]["E0.Param"]==37&&traded[4]["E0.Species"]==5&&traded[1]["E0.Method"]==4&&traded[1]["E0.Param"]==16,$"NdsData_{n}_TradeEvolutionsBecomeLevel37");
        var full=After(NdsSection.Machines,new NdsDataOptions(10){Machines=NdsMachineMode.Full});var kept=After(NdsSection.Machines,new NdsDataOptions(10){Machines=NdsMachineMode.Random,MachinePercent=100});
        check(full.Values.All(r=>r.Values.All(v=>v==1))&&kept.All(p=>p.Value.Where(b=>int.Parse(b.Key[1..])>=s.TmCount).All(b=>b.Value==tms[p.Key][b.Key])),$"NdsData_{n}_MachinesFullOrRandomKeepingHms");
        var a=game.Random(NdsSection.Pokemon,new NdsDataOptions(11){Stats=NdsStatsMode.Random,Types=true});var b=game.Random(NdsSection.Pokemon,new NdsDataOptions(11){Stats=NdsStatsMode.Random,Types=true});
        var scoped=game.Random(NdsSection.Pokemon,new NdsDataOptions(11){Stats=NdsStatsMode.Random,Scope=new HashSet<int>{1}});
        check(a.Changes.SequenceEqual(b.Changes)&&scoped.Changes.All(c=>c.Location.StartsWith(s.SpeciesNames[1]+" ",StringComparison.Ordinal)),$"NdsData_{n}_DeterministicAndScoped");
    }

    // Real dumps: every data section edited and randomized, stacked through drafts, exported once and read back.
    internal static void Probe(string output,IEnumerable<string> roms)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");Directory.CreateDirectory(output);
        NdsSection[] sections=[NdsSection.Pokemon,NdsSection.Moves,NdsSection.Learnsets,NdsSection.Evolutions,NdsSection.Machines];
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
                var files=new Dictionary<string,byte[]>();var current=game;int changes=0;
                void Stack(Gen5RandomizerResult result){foreach(var (path,data) in result.Files)files[path]=data;current=game.Draft(new Dictionary<string,byte[]>(files));changes+=result.Changes.Count;}
                Stack(current.Random(NdsSection.Pokemon,new NdsDataOptions(21){Stats=NdsStatsMode.Shuffle,Types=true,Abilities=true,MinCatchRate=60}));
                Stack(current.Random(NdsSection.Moves,new NdsDataOptions(22){MovePower=true,MoveAccuracy=true,MovePp=true}));
                Stack(current.Random(NdsSection.Learnsets,new NdsDataOptions(23){Learnsets=NdsLearnsetMode.Random}));
                Stack(current.Random(NdsSection.Evolutions,new NdsDataOptions(24){RemoveTradeEvolutions=true}));
                Stack(current.Random(NdsSection.Machines,new NdsDataOptions(25){Machines=NdsMachineMode.TypeBased}));
                Stack(current.Write(NdsSection.Pokemon,new Dictionary<string,int>{["1:Stat0"]=99}));
                var final=sections.ToDictionary(x=>x,x=>current.Read(x));
                var traded=Table(final[NdsSection.Evolutions]);
                Check(Table(final[NdsSection.Pokemon]).Values.All(r=>r["CatchRate"]>=60)&&traded.Values.All(r=>r.Where(p=>p.Key.EndsWith(".Method")).All(m=>!s.IsTradeMethod(m.Value)))&&Table(final[NdsSection.Pokemon])[1]["Stat0"]==99,
                    $"NdsData_Real_{code}_RandomizationsAndEditsStack ({changes} cambios)");
                string target=Path.Combine(output,code+"-data.nds");
                var all=new Gen5RandomizerResult(files,[],new Dictionary<int,byte[]>());
                if(g4 is not null)g4.Export(target,all);else g5!.Export(target,all);
                using var exported=NdsRom.Open(target);
                var again=gen4?Of(code,Gen4Data.Open(exported,requireOriginalTables:false)):Of(code,Gen5Data.Open(exported,requireOriginalTables:false));
                Check(sections.All(x=>again.Read(x).Select(c=>(c.Key,c.Value)).SequenceEqual(final[x].Select(c=>(c.Key,c.Value)))),$"NdsData_Real_{code}_ExportReadsBackEverySection");
                var changedIds=files.Keys.Select(p=>rom.File(p).Id).ToHashSet();
                Check(exported.Files.Where(f=>!changedIds.Contains(f.Id)).All(f=>exported.ReadFile(f).SequenceEqual(rom.ReadFile(rom.Files[f.Id]))),$"NdsData_Real_{code}_EveryOtherFileIdentical");
            }
            foreach(string f in Directory.GetFiles(output))File.Delete(f);
            Check(Hash(source)==before,$"NdsData_Real_{code}_SourceUnchanged");
        }
        Console.WriteLine($"Completed {checks} DS data real-dump checks; outputs deleted from {output}.");
    }

    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
}
