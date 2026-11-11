using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;

internal static class NdsTrainerParityChecks
{
    internal static void Run(Action<bool,string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-trainers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string gen5Base=Path.Combine(temp,"base.nds"); File.WriteAllBytes(gen5Base,NdsGen5RandomizerChecks.SyntheticBlackRomhack());
            var fixtures = Gen4Profile.All.Select(p => (p.GameCode, Bytes: NdsGen4Checks.SyntheticRom(p,true)))
                .Concat(Gen5Profile.All.Select(p=>(p.GameCode,Bytes:SyntheticGen5(p,gen5Base))));
            foreach (var (code, bytes) in fixtures)
            {
                string source = Path.Combine(temp, code + ".nds"); File.WriteAllBytes(source, bytes);
                Exercise(source, Path.Combine(temp, code), check);
            }
            CheckDerivedMoves(gen5Base,check);
        }
        finally { Directory.Delete(temp, true); }
    }

    internal static byte[] SyntheticGen5(Gen5Profile profile,string source)
    {
        using var rom=NdsRom.Open(source);
        var original=Gen5Profile.All.Single(p=>p.GameCode=="IRBO");
        var files=profile.Files.Where(p=>original.Files.ContainsKey(p.Key)).ToDictionary(p=>p.Value.Path,p=>rom.ReadFile(original.Files[p.Key].Path));
        var oldText=Narc.Read(rom.ReadFile(original.Files[Gen5Profile.TextStrings].Path));
        int[] oldIds=[original.SpeciesNamesText,original.TrainerNamesText,original.TrainerClassesText,original.TypeNamesText,original.ItemNamesText,original.MoveNamesText,original.AbilityNamesText];
        int[] newIds=[profile.SpeciesNamesText,profile.TrainerNamesText,profile.TrainerClassesText,profile.TypeNamesText,profile.ItemNamesText,profile.MoveNamesText,profile.AbilityNamesText];
        var messages=Enumerable.Range(0,newIds.Max()+1).Select(_=>Array.Empty<byte>()).ToArray();
        for(int i=0;i<newIds.Length;i++)messages[newIds[i]]=oldText.Files[oldIds[i]];
        files[profile.Files[Gen5Profile.TextStrings].Path]=NdsGen5RandomizerChecks.BuildNarc(messages);
        files[profile.Files[Gen5Profile.Scripts].Path]=NdsGen5RandomizerChecks.BuildNarc(Enumerable.Range(0,Math.Max(profile.ItemBallScript,profile.HiddenItemScript)+1).Select(_=>new byte[]{0x13,0xFD}).ToArray());
        if(profile.Sequel)files[profile.Files[Gen5Profile.ShopItems].Path]=NdsGen5RandomizerChecks.BuildNarc(profile.ShopItemSizes.Select(s=>new byte[s*2]).ToArray());
        return NdsGen5RandomizerChecks.BuildRom(files,profile.GameCode);
    }

    private static void CheckDerivedMoves(string source,Action<bool,string> check)
    {
        string derivedSource=Path.Combine(Path.GetDirectoryName(source)!,"derived.nds");
        using(var rom=NdsRom.Open(source))
        {
            string path=Gen5Profile.All.Single(p=>p.GameCode=="IRBO").Files[Gen5Profile.Learnsets].Path;
            var sets=Narc.Read(rom.ReadFile(path));
            sets.Files[1]=NdsLearnsets.Encode([new(33,2),new(45,3),new(52,4),new(55,5),new(33,6),new(85,7)],5,559);
            rom.Save(derivedSource,new Dictionary<int,byte[]>{[rom.File(path).Id]=sets.Write()});
        }
        using var project=NdsProject.Open(derivedSource,true);
        var editor=new NdsRecordEditor(project,NdsSection.Trainers);
        check(editor.DerivedMoves(1,0,1)!.SequenceEqual(new[]{0,0,0,0})&&editor.DerivedMoves(1,0,2)!.SequenceEqual(new[]{33,0,0,0}),"NdsTrainer_DerivedMovesRespectBeforeAndAtLearnLevel");
        check(editor.DerivedMoves(1,0,6)!.SequenceEqual(new[]{33,45,52,55})&&editor.DerivedMoves(1,0,7)!.SequenceEqual(new[]{45,52,55,85}),"NdsTrainer_DerivedMovesSkipKnownDuplicatesAndReplaceOldest");
        project.Apply(project.PreviewLearnsets("Cambiar aprendizaje",new Dictionary<int,IReadOnlyList<NdsLearnsetMove>>{[1]=[new(118,1)]}));
        check(editor.DerivedMoves(1,0,7)!.SequenceEqual(new[]{118,0,0,0}),"NdsTrainer_DerivedMovesInvalidateAfterLearnsetEdits");
    }

    internal static void Probe(string output, IEnumerable<string> sources)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("El destino debe ser nuevo.");
        Directory.CreateDirectory(output);
        int checks = 0;
        foreach (string source in sources) Exercise(source, Path.Combine(output, Path.GetFileNameWithoutExtension(source)), (ok,name) =>
        { if (!ok) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); });
        Console.WriteLine($"Completed {checks} DS trainer real-copy checks. Outputs retained at {output}.");
    }

    private static Dictionary<string,int> Values(NdsRecordEditor editor, int id) => editor.Read(id).SelectMany(g => g.Fields).ToDictionary(f => f.Key, f => f.Value);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Exercise(string source, string output, Action<bool,string> check)
    {
        Directory.CreateDirectory(output);
        string before = Hash(source);
        using var project = NdsProject.Open(source, true);
        string code = project.Identity.Profile.GameCode;
        void Check(bool ok, string test) => check(ok, "NdsTrainer_" + code + "_" + test);
        var editor = new NdsRecordEditor(project, NdsSection.Trainers);
        var trainers = project.Trainers();
        var explicitTrainer = trainers.First(t => t.HasMoves && t.HasItems && t.Team.Count > 0);
        var defaultTrainer = trainers.First(t => !t.HasMoves && !t.HasItems && t.Team.Count > 0 && t.BattleType == 0);
        int id = explicitTrainer.Id, defaults = defaultTrainer.Id;
        var fields = Values(editor, id);
        Check(Enumerable.Range(0,4).All(i => fields[$"P0.Move{i}"] == explicitTrainer.Team[0].Moves[i]) && !fields.ContainsKey("P0.Move4")
            && fields["P0.Form"] == explicitTrainer.Team[0].Form && fields["P0.Item"] == explicitTrainer.Team[0].HeldItem, "ReadsFourMovesFormAndItem");
        editor.Save(id, fields); editor.Save(defaults, Values(editor, defaults));
        Check(project.History.Count == 0, "OpeningAndNoOpSaveDoNotWriteDerivedData");
        using (var unchangedSource = NdsRom.Open(source))
        {
            var noOpPreview=project.PreviewTrainerEdits(fields.ToDictionary(p=>$"{id}:"+p.Key,p=>p.Value));
            Check(noOpPreview.Result.Changes.Count==0&&noOpPreview.Result.Files.All(p=>unchangedSource.ReadFile(p.Key).SequenceEqual(p.Value)),"SerializerNoOpKeepsEveryByte");
        }
        string noOp = Path.Combine(output, "no-op.nds");
        using (var noOpSource = NdsRom.Open(source)) noOpSource.Save(noOp,new Dictionary<int,byte[]>());
        Check(Hash(noOp) == before, "NoOpRebuildIsByteIdentical");

        var originalStates = editor.TrainerStates(id);
        fields["P0.Level"] = explicitTrainer.Team[0].Level == 33 ? 34 : 33;
        fields["P0.Form"] = 0; fields["P0.Item"] = 158;
        fields["P0.Move0"] = 33; fields["P0.Move1"] = 45; fields["P0.Move2"] = 52; fields["P0.Move3"] = 55;
        fields["P0.IVs"] = 201; fields["P0.Ability"] = 1; fields["P0.Gender"] = 2;
        editor.Save(id, fields);
        var changed = project.Trainers().Single(t => t.Id == id).Team[0];
        Check(changed.Level == fields["P0.Level"] && changed.Form == 0 && changed.HeldItem == 158 && changed.Moves.SequenceEqual(new[]{33,45,52,55})
            && changed.Difficulty == 201 && changed.AbilitySlot == 1 && changed.GenderFlag == 2, "ManualEditKeepsEachSupportedField");
        Check(editor.TrainerStates(id).Original.Team[0] == originalStates.Original.Team[0]
            && editor.TrainerStates(id).Current.Team[0].Level == fields["P0.Level"], "OriginalAndSavedComparisonStayDistinct");

        var defaultValues = Values(editor, defaults);
        int level = defaultValues["P0.Level"], species = defaultValues["P0.Species"];
        Check(editor.DerivedMoves(species, 1, level) is null, "NeverUsesBaseLearnsetForUnknownForm");
        var derived = editor.DerivedMoves(species, 0, level);
        Check(derived is {Length:4}, "BaseFormShowsFourDerivedSlots");
        defaultValues["HasMoves"] = 1; defaultValues["HasItems"] = 1;
        species = species == 201 ? 386 : 201; level = level == 33 ? 34 : 33;
        defaultValues["P0.Species"] = species; defaultValues["P0.Form"] = 1; defaultValues["P0.Level"] = level;
        for (int i=0;i<4;i++) defaultValues["P0.Move"+i] = new[]{33,45,52,55}[i];
        defaultValues["P0.Item"] = 234;
        editor.Save(defaults, defaultValues);
        Check(project.Trainers().Single(t => t.Id == defaults) is {HasMoves:true,HasItems:true} converted
            && converted.Team[0].Moves.SequenceEqual(new[]{33,45,52,55}) && converted.Team[0].HeldItem == 234
            && converted.Team[0].Species == species && converted.Team[0].Form == 1 && converted.Team[0].Level == level, "ConvertsDefaultTeamToExplicitWithoutLosingMembers");

        // Historical keys keep their old meaning; the new route does not reinterpret them.
        project.Apply(project.PreviewEdits(NdsSection.Trainers, new Dictionary<string,int>{[$"{id}:P0.Move1"] = 85, [$"{id}:P0.Move4"] = 86}));
        Check(project.Trainers().Single(t => t.Id == id).Team[0].Moves.SequenceEqual(new[]{85,45,52,86}), "HistoricalOneBasedRecipesKeepTheirMeaning");
        // Deterministic randomization is previewed from the saved state and applied once.
        var speciesOptions = new Gen5RandomizerOptions(713) { Trainers = Gen5SpeciesMode.Keep, Scope = new HashSet<int>{id} };
        var tools = new NdsTrainerOptions(714) { Scope = new HashSet<int>{id}, Moves = NdsTrainerMoveMode.Metronome };
        var preview = project.PreviewTrainers("Prueba entrenadores", speciesOptions, tools);
        var repeated = project.PreviewTrainers("Prueba entrenadores", speciesOptions, tools);
        Check(preview.Result.Files.All(p => repeated.Result.Files[p.Key].SequenceEqual(p.Value)) && project.Trainers().Single(t => t.Id == id).Team[0].Moves[0] == 85
            && preview.Result.Changes.Count > 0, "SeededPreviewIsRepeatableAndDoesNotApplyItself");
        project.Apply(preview);
        Check(project.History[^1].Seed == 713 && project.Trainers().Single(t => t.Id == id).Team.All(p => p.Moves[0] == 118), "RandomResultIsVisibleAndSeedPreserved");

        string target = Path.Combine(output, "edited.nds"); project.Export(target);
        using var reopened = NdsProject.Open(target, true);
        Check(Values(new NdsRecordEditor(reopened,NdsSection.Trainers),defaults)["P0.Move3"] == 55, "RebuildReopensFourthMove");
        var rebuilt = reopened.Trainers().Single(t => t.Id == defaults).Team[0];
        Check(rebuilt.Species == species && rebuilt.Form == 1 && rebuilt.Level == level && rebuilt.HeldItem == 234
            && rebuilt.Moves.SequenceEqual(new[]{33,45,52,55}), "RebuildReopensEditedSpeciesNonzeroFormLevelItemAndFourMoves");
        using var replay = NdsProject.Open(source, true);
        var recipe = project.CaptureGlobal(); replay.ApplyGlobal(recipe, false);
        string replayTarget = Path.Combine(output,"replay.nds"); replay.Export(replayTarget);
        Check(Hash(replayTarget) == Hash(target), "OldAndNewGlobalOperationsReplayByteIdentically");
        var batch = NdsProject.BuildBatch(source,true,recipe,1,output,"batch",null,CancellationToken.None);
        using var batched = NdsProject.Open(batch.Single(),true);
        Check(batched.Trainers().Single(t => t.Id == defaults).Team[0].Moves[3] == 55, "BatchKeepsExplicitManualValues");

        using(var original = NdsRom.Open(source))
        using(var result = NdsRom.Open(target))
        {
            string headers, teams;
            if(project.Generation == 4) { var p=Gen4Profile.All.Single(p=>p.GameCode==code); headers=p.Files[Gen4Profile.TrainerData].Path; teams=p.Files[Gen4Profile.TrainerPokemon].Path; }
            else { var p=Gen5Profile.All.Single(p=>p.GameCode==code); headers=p.Files[Gen5Profile.TrainerData].Path; teams=p.Files[Gen5Profile.TrainerPokemon].Path; }
            Check(original.Files.Where(f=>f.Path!=headers&&f.Path!=teams).All(f=>original.ReadFile(f).SequenceEqual(result.ReadFile(f.Path)))
                && original.ReadArm9().SequenceEqual(result.ReadArm9()), "UnrelatedFilesAndArm9AreUnchanged");
            var oldHeaders=Narc.Read(original.ReadFile(headers)); var newHeaders=Narc.Read(result.ReadFile(headers));
            Check(oldHeaders.Files.Select((h,i)=>i==id||i==defaults ? h.Where((b,j)=>j!=0&&j!=3).SequenceEqual(newHeaders.Files[i].Where((b,j)=>j!=0&&j!=3)) : h.SequenceEqual(newHeaders.Files[i])).All(x=>x), "HeaderAiBattleTypeAndOpaqueBytesArePreserved");
            var oldTeams=Narc.Read(original.ReadFile(teams)); var newTeams=Narc.Read(result.ReadFile(teams));
            Check(oldTeams.Files.Select((h,i)=>i==id||i==defaults||h.SequenceEqual(newTeams.Files[i])).All(x=>x), "OtherTrainerMembersAreUntouched");
            int tail=project.Generation==4&&Gen4Profile.All.Single(p=>p.GameCode==code).Kind!=Gen4Kind.DiamondPearl?2:0;
            int baseSize=project.Generation==4?6:8;
            int Size(int flags)=>baseSize+((flags&2)!=0?2:0)+((flags&1)!=0?8:0)+tail;
            Check(new[]{id,defaults}.All(t=>
            {
                int oldSize=Size(oldHeaders.Files[t][0]),newSize=Size(newHeaders.Files[t][0]),count=oldHeaders.Files[t][3],newCount=newHeaders.Files[t][3];
                return oldTeams.Files[t].AsSpan(oldSize*count).SequenceEqual(newTeams.Files[t].AsSpan(newSize*newCount))
                    &&Enumerable.Range(0,Math.Min(count,newCount)).All(s=>oldTeams.Files[t].AsSpan((s+1)*oldSize-tail,tail).SequenceEqual(newTeams.Files[t].AsSpan((s+1)*newSize-tail,tail)));
            }),"ManualConversionAndRandomizationPreserveSealsAndOpaqueSuffix");
        }

        // Explicitly discard formats, then grow/shrink a normal team through the same serializer.
        defaultValues=Values(editor,defaults); defaultValues["HasMoves"]=0; defaultValues["HasItems"]=0; editor.Save(defaults,defaultValues);
        Check(project.Trainers().Single(t=>t.Id==defaults) is {HasMoves:false,HasItems:false} plain && plain.Team[0].Species==species, "ExplicitFormatRemovalKeepsBaseData");
        defaultValues=Values(editor,defaults);defaultValues["HasItems"]=1;defaultValues["P0.Item"]=158;editor.Save(defaults,defaultValues);
        Check(project.Trainers().Single(t=>t.Id==defaults) is {HasMoves:false,HasItems:true} itemsOnly&&itemsOnly.Team[0].HeldItem==158,"ItemOnlyFormatPreservesDefaultMoves");
        defaultValues=Values(editor,defaults);defaultValues["HasItems"]=0;defaultValues["HasMoves"]=1;defaultValues["P0.Move0"]=33;editor.Save(defaults,defaultValues);
        Check(project.Trainers().Single(t=>t.Id==defaults) is {HasMoves:true,HasItems:false} movesOnly&&movesOnly.Team[0].Moves[0]==33,"MovesOnlyFormatPreservesDefaultItem");
        defaultValues=Values(editor,defaults);defaultValues["HasMoves"]=0;editor.Save(defaults,defaultValues);
        defaultValues=Values(editor,defaults); defaultValues["Count"]=6; editor.Save(defaults,defaultValues);
        Check(project.Trainers().Single(t=>t.Id==defaults).Team.Count==6,"TeamGrowsToSixUsingSharedSlots");
        defaultValues=Values(editor,defaults); defaultValues["Count"]=1; editor.Save(defaults,defaultValues);
        Check(project.Trainers().Single(t=>t.Id==defaults).Team.Count==1,"TeamShrinksUsingSharedSlots");
        bool rejected=false; try { project.PreviewTrainerEdits(new Dictionary<string,int>{[$"{defaults}:P0.Move0"]=33}); } catch(ArgumentException) { rejected=true; }
        Check(rejected,"RejectsCustomMoveWithoutFormatConversion");
        rejected=false; try { project.PreviewTrainerEdits(new Dictionary<string,int>{[$"{id}:P0.Move4"]=33}); } catch(ArgumentException) { rejected=true; }
        Check(rejected,"NewRouteRejectsHistoricalKeyInsteadOfShiftingSlots");
        Check(Hash(source)==before,"SourceHashIsUnchanged");
    }
}
