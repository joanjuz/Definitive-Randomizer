using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core.NX;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Switch;
using ThreeDS.Progressive.Models;
internal static class SwitchShieldChecks
{
    private const string Shield="01008DB008C2C000",Sword="0100ABF008968000";
    private const string Team="bin/trainer/trainer_poke/trainer_poke_001.bin",Header="bin/trainer/trainer_data/trainer_data_001.bin";
    private const string Leader="bin/trainer/trainer_poke/trainer_poke_032.bin",LeaderHeader="bin/trainer/trainer_data/trainer_data_032.bin";
    // First gym leader fixture: doubles only apply to teams stronger than this leader's highest level.
    private static void AddFirstGym(IDictionary<string,byte[]> files,int level,int members=1)
    {var team=new byte[32*members];for(int i=0;i<members;i++){team[i*32+10]=(byte)level;team[i*32+12]=1;}var header=new byte[20];header[3]=(byte)members;files[Leader]=team;files[LeaderHeader]=header;}
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var package=PokeFeurPackage.BuiltIn(Shield);var sword=PokeFeurPackage.BuiltIn();
        check(package.TitleId==Shield&&Convert.ToHexString(SHA256.HashData(package.File("exefs/subsdk9")))==PokeFeurPackage.ShieldModuleHash&&Convert.ToHexString(SHA256.HashData(package.File("exefs/main.npdm")))==PokeFeurPackage.ShieldNpdmHash,"Shield_RuntimeUsesPinnedVersionSpecificResources");
        check(SwordShieldIdentity.Read(package.File("exefs/main.npdm"),ShieldNso()).TitleId==Shield&&!package.File("exefs/subsdk9").SequenceEqual(sword.File("exefs/subsdk9")),"Shield_RuntimeMetadataTargetsShieldAndModuleDiffersFromSword");
        bool unknown=false;try{PokeFeurPackage.BuiltIn("0100000000000000");}catch(NotSupportedException){unknown=true;}check(unknown,"Shield_RuntimeRejectsUnknownTitle");
        var caps=DivineSwordCaps.ForTitle(Shield);check(caps.Count==40&&caps[78]==50&&caps[136]==68&&!caps.ContainsKey(77)&&!caps.ContainsKey(135)&&DivineSwordCaps.Levels[77]==50&&DivineSwordCaps.Levels[135]==68,"Shield_PresetMapsExclusiveGymsWithoutChangingSword");
        var files=SwitchParityChecks.Inputs();files[Team]=files[Team].Concat(files[Team]).ToArray();files[Header][3]=2;files[Header][0]=0xA5;files[Header][12]=0x80;AddFirstGym(files,10);
        var options=new SwordShieldGameplayOptions(12345,Trainers:true){RandomizeTrainerSpecies=false,TrainerRules=new(){DoubleBattleChance=100}};
        foreach(bool shield in new[]{false,true})
        {
            var result=SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),shield,options);var actual=result.Files();
            check(actual.Count==1&&actual[Header][2]==1&&actual[Header].Where((b,i)=>i!=2).SequenceEqual(files[Header].Where((b,i)=>i!=2))&&result.Changes.Single().Field=="Combate doble","Swsh_DoubleHundredChangesOnlyBattleMode_"+shield);
            check(SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),shield,options with{TrainerRules=new()}).Changes.Count==0,"Swsh_DoubleZeroPreservesAllBytes_"+shield);
            check(SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),shield,options with{TrainerScope=new[]{2}}).Changes.Count==0,"Swsh_DoubleRespectsTrainerScope_"+shield);
            var one=files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());one[Header][3]=1;one[Team]=one[Team].Take(32).ToArray();check(SwordShieldGameplay.Generate(one,SwordShieldChecks.Table(),shield,options).Changes.Count==0,"Swsh_DoubleSkipsSinglePokemonTeam_"+shield);
            foreach(int early in new[]{4,5,6,7,8,9}){var intro=files.Where(p=>p.Key==Team||p.Key==Header).ToDictionary(p=>p.Key.Replace("001",early.ToString("000")),p=>(byte[])p.Value.Clone());AddFirstGym(intro,10);check(SwordShieldGameplay.Generate(intro,SwordShieldChecks.Table(),shield,options).Changes.Count==0,"Swsh_DoublePreservesMandatoryEarlyHopBattle_"+shield+"_"+early);}
            foreach(byte mode in new byte[]{1,2,3}){var special=files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());special[Header][2]=mode;check(SwordShieldGameplay.Generate(special,SwordShieldChecks.Table(),shield,options).Changes.Count==0,"Swsh_DoublePreservesExistingMode_"+shield+"_"+mode);}
            foreach(var (leader,converts) in new[]{(12,false),(11,true)}){var bounded=files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());AddFirstGym(bounded,leader);check((SwordShieldGameplay.Generate(bounded,SwordShieldChecks.Table(),shield,options).Changes.Count==1)==converts,"Swsh_DoubleStartsAfterFirstGymLevel_"+shield+"_"+leader);}
            var strongLeader=files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());AddFirstGym(strongLeader,10,members:2);var leaderResult=SwordShieldGameplay.Generate(strongLeader,SwordShieldChecks.Table(),shield,options);check(leaderResult.Changes.Count==1&&!leaderResult.Files().ContainsKey(LeaderHeader),"Swsh_DoubleKeepsFirstGymLeaderSingle_"+shield);
            var noLeader=files.Where(p=>p.Key is not (Leader or LeaderHeader)).ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());reject(()=>SwordShieldGameplay.Generate(noLeader,SwordShieldChecks.Table(),shield,options),"Swsh_DoubleRejectsMissingFirstGymLeader_"+shield);check(SwordShieldGameplay.Generate(noLeader,SwordShieldChecks.Table(),shield,options with{TrainerRules=new()}).Changes.Count==0,"Swsh_DoubleZeroDoesNotNeedFirstGymLeader_"+shield);
        }
        reject(()=>SwordShieldBattlePolicy.Validate(options with{TrainerRules=new(){DoubleBattleChance=-1}}),"Swsh_DoubleRejectsNegativeChance");reject(()=>SwordShieldBattlePolicy.Validate(options with{TrainerRules=new(){DoubleBattleChance=101}}),"Swsh_DoubleRejectsExcessChance");
        check(JsonSerializer.Deserialize<SwordShieldTrainerOptions>("{}")!.DoubleBattleChance==0&&JsonSerializer.Deserialize<SwordShieldTrainerOptions>(JsonSerializer.Serialize(options.TrainerRules))!.DoubleBattleChance==100,"Swsh_DoubleRecipeRoundtripAndLegacyDefault");
        var many=new Dictionary<string,byte[]>();for(int i=1;i<=100;i++){many[Team.Replace("001",i.ToString("000"))]=(byte[])files[Team].Clone();many[Header.Replace("001",i.ToString("000"))]=(byte[])files[Header].Clone();}AddFirstGym(many,10);
        var half=options with{TrainerRules=new(){DoubleBattleChance=50}};var a=SwordShieldGameplay.Generate(many,SwordShieldChecks.Table(),false,half);var b=SwordShieldGameplay.Generate(many,SwordShieldChecks.Table(),false,half);var c=SwordShieldGameplay.Generate(many,SwordShieldChecks.Table(),false,half with{Seed=54321});
        check(a.Changes.Count>10&&a.Changes.Count<90&&a.Changes.SequenceEqual(b.Changes)&&!a.Changes.SequenceEqual(c.Changes),"Swsh_DoubleChanceIsMixedDeterministicAndSeedSensitive");
    }
    private static byte[] ShieldNso(){var nso=new byte[256];"NSO0"u8.CopyTo(nso);Convert.FromHexString("A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000").CopyTo(nso,64);return nso;}
    internal static void RunUi(Action<bool,string> check)
    {
        string temp=Path.Combine(Path.GetTempPath(),"dr-shield-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);try
        {
            foreach(bool shield in new[]{false,true})
            {
                string root=SwordShieldChecks.Fixture(temp,shield?"shield":"sword",shield);var files=SwitchParityChecks.Inputs();files[Team]=files[Team].Concat(files[Team]).ToArray();files[Header][3]=2;AddFirstGym(files,10);
                foreach(var p in files){string path=Path.Combine(root,"romfs",p.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,p.Value);}
                var project=SwordShieldProject.Open(root);var editor=new SwordShieldRecordEditor(project,SwordShieldDataSection.TrainerTeams);var view=new RecordEditorView(editor);check(view.OptionControls.ContainsKey("DoubleBattleChance"),"Swsh_UIDoubleChanceAppearsInTrainerRandomization_"+shield);
                var values=EditorValues.Flatten(editor.RandomOptions);values["Species"]=0;values["DoubleBattleChance"]=100;var preview=editor.Preview(values,new[]{1});check(!project.HasChanges&&preview.Changes.Single().Field=="Combate doble","Swsh_UIDoubleOnlyPreviewDoesNotApply_"+shield);editor.Apply(preview);
                var recipe=project.CaptureGlobal();var fresh=SwordShieldProject.Open(root);fresh.ApplyGlobal(fresh.PreviewGlobal(recipe));check(fresh.GameplayHistory.Single().Options.TrainerRules.DoubleBattleChance==100&&fresh.GameplayHistory.Single().Changes.Single().Field=="Combate doble"&&fresh.GlobalHistorySummary().Contains("Combate doble"),"Swsh_DoubleGlobalReplayAndHistory_"+shield);
                var gameplayWindow=new SwordShieldGameplayWindow(project);string? route=null;gameplayWindow.EmbeddedNavigate=value=>route=value;var runtimeButton=(System.Windows.Controls.Button)gameplayWindow.FindName("SwitchGameplayRuntime");runtimeButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));check(runtimeButton.IsEnabled&&route=="Runtime"&&gameplayWindow.FindName("SwitchGameplayDoubleChance") is System.Windows.Controls.TextBox,"Swsh_GameplayWindowExposesDoubleAndRuntimeForBothVersions_"+shield);gameplayWindow.Close();
                var runtimeWindow=new SwordPokeFeurWindow(project);check(runtimeWindow.Title.StartsWith(shield?"Escudo":"Espada"),"Swsh_RuntimeUiNamesCurrentGame_"+shield);runtimeWindow.Close();
                var uiRecipe=editor.CaptureOptions(values);var restored=new SwordShieldRecordEditor(project,SwordShieldDataSection.TrainerTeams);restored.RestoreOptions(uiRecipe);check(EditorValues.Flatten(restored.RandomOptions)["DoubleBattleChance"]==100,"Swsh_DoubleUiOptionsRoundtrip_"+shield);
                var legacy=JsonNode.Parse(uiRecipe)!.AsObject();legacy["Options"]!.AsObject().Remove("DoubleBattleChance");var legacyEditor=new SwordShieldRecordEditor(project,SwordShieldDataSection.TrainerTeams);legacyEditor.RestoreOptions(legacy.ToJsonString());check(EditorValues.Flatten(legacyEditor.RandomOptions)["DoubleBattleChance"]==0,"Swsh_LegacyUiTemplateOmitsDoubleChanceSafely_"+shield);
            }
        }finally{Directory.Delete(temp,true);}
    }
    internal static void Probe(string root,string output)
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
        var project=SwordShieldProject.Open(root);bool shield=project.Identity.TitleId==Shield;Check(project.Identity.CanEdit,"Swsh_Real132Editable_"+project.Identity.Game);
        var files=Directory.GetFiles(Path.Combine(root,"romfs/bin/trainer/trainer_data"),"*.bin").ToDictionary(p=>Path.GetFileNameWithoutExtension(p),File.ReadAllBytes);var proposal=project.PreviewGameplay(new SwordShieldGameplayOptions(12345,Trainers:true){RandomizeTrainerSpecies=false,TrainerRules=new(){DoubleBattleChance=100}});
        int Level(string key){byte[] team=File.ReadAllBytes(Path.Combine(root,"romfs/bin/trainer/trainer_poke",key.Replace("trainer_data","trainer_poke")+".bin"));return Enumerable.Range(0,team.Length/32).Max(i=>(int)BitConverter.ToUInt16(team,i*32+10));}
        int firstGym=Level($"trainer_data_{SwordShieldGameplay.FirstGymLeader:000}");
        Check(proposal.Result.Changes.Count==files.Count(p=>int.Parse(p.Key.Split('_').Last()) is not (>=4 and <=9)&&p.Value[2]==0&&p.Value[3]>=2&&Level(p.Key)>firstGym)&&proposal.Result.Changes.All(c=>c.Field=="Combate doble"),"Swsh_RealDoubleConvertsExactlyEligibleSingleTeamsAfterFirstGym");project.ApplyGameplay(proposal);
        var editor=new SwordShieldRecordEditor(project,SwordShieldDataSection.TrainerTeams);var values=EditorValues.Flatten(editor.RandomOptions);values["Species"]=0;foreach(var cap in DivineSwordCaps.ForTitle(project.Identity.TitleId)){values[$"Rule.{cap.Key}.CapUse"]=1;values[$"Rule.{cap.Key}.LevelCap"]=cap.Value;}editor.Apply(editor.Preview(values,editor.Entries.Select(e=>e.Id).ToArray()));
        Check(project.PokeFeurTrainerCaps().Select(c=>c.Level).SequenceEqual(new[]{27,34,43,50,58,68,75,78,92}),"Swsh_RealNineCapsMatchVersionSpecificPreset");
        var package=PokeFeurPackage.BuiltIn(project.Identity.TitleId);bool wrongPackage=false;try{project.PreviewPokeFeur(PokeFeurPackage.BuiltIn(shield?Sword:Shield),new PokeFeurOptions{Randomizer=true});}catch(InvalidDataException){wrongPackage=true;}Check(wrongPackage,"Swsh_RealRejectsOtherVersionRuntime");var runtime=project.PreviewPokeFeur(package,new PokeFeurOptions{Randomizer=true,Caps=true,CapsFromTrainers=true,ExpRuntime=true,Features=new[]{PokeFeurFeature.Camera}});project.ApplyPokeFeur(runtime);
        Check(project.PokeFeurCurrent!.StoryCaps.SequenceEqual(new[]{27,34,43,50,58,68,75,78,92}),"Swsh_RealRuntimeDerivesCurrentCaps");var recipe=project.CaptureGlobal();var fresh=SwordShieldProject.Open(root);fresh.ApplyGlobal(fresh.PreviewGlobal(recipe));Check(fresh.PokeFeurCurrent!.StoryCaps.SequenceEqual(project.PokeFeurCurrent.StoryCaps)&&fresh.GameplayHistory[0].Changes.Count==proposal.Result.Changes.Count,"Swsh_RealDoubleCapsRuntimeGlobalReplay");
        var result=project.Export(output,SwitchModLayout.Ryujinx);Check(File.ReadAllBytes(Path.Combine(output,"exefs/subsdk9")).SequenceEqual(package.File("exefs/subsdk9"))&&SwordShieldIdentity.Read(File.ReadAllBytes(Path.Combine(output,"exefs/main.npdm")),File.ReadAllBytes(Path.Combine(root,"exefs/main"))).TitleId==project.Identity.TitleId,"Swsh_RealExportContainsCorrectTitleRuntime");
        Directory.CreateDirectory(output+"-batch");var outputs=project.BatchGlobal(recipe,2,output+"-batch","compatibility",SwitchModLayout.Ryujinx);Check(outputs.Count==2,"Swsh_RealDoubleCapsRuntimeBatchTwoMods");
        Check(files.All(p=>File.ReadAllBytes(Path.Combine(root,"romfs/bin/trainer/trainer_data",p.Key+".bin")).SequenceEqual(p.Value)),"Swsh_RealTrainerSourcesUnchanged");Console.WriteLine($"Completed {checks} version integration checks; no installation or gameplay.");
    }
}
