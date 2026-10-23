using System.IO;
using System.Buffers.Binary;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services.Switch;
using pk3DS.Core.NX;
internal static class DivineSwordChecks
{
    private const string Team="bin/trainer/trainer_poke/trainer_poke_001.bin";
    private static void U16(byte[] b,int at,int v)=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at),(ushort)v);
    private static readonly int[] ExpectedIds={7,8,9,32,36,37,77,107,108,121,122,123,124,125,126,127,128,129,130,131,132,133,134,135,138,144,145,149,191,192,193,195,196,202,203,204,210,212,213,240};
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        check(DivineSwordCaps.Levels.Keys.SequenceEqual(ExpectedIds)&&DivineSwordCaps.Levels.Values.SequenceEqual(new[]{11,11,11,27,34,43,50,75,58,29,29,29,47,47,47,59,59,59,83,83,83,52,85,68,73,78,84,92,17,17,17,21,42,70,70,70,87,88,87,36}),"DivineSword_PresetContainsOnlyFortySelectedBattlesAndCaps");
        var source=SwitchParityChecks.Inputs();byte[] personal=SwordShieldChecks.Table();personal[177]=200;personal[180]=20;
        foreach(var kind in new[]{"Better","Strong","Power","Smart"})
        {
            var rules=kind switch{"Better"=>new SwordShieldTrainerOptions{BetterMovesets=true,BetterScope=new[]{1}},"Strong"=>new SwordShieldTrainerOptions{StrongestAttackStat=true,MixedTolerance=0,StrongScope=new[]{1}},"Power"=>new SwordShieldTrainerOptions{MinMovePower=60,PowerScope=new[]{1}},_=>new SwordShieldTrainerOptions{SmartItems=true,SmartItemScope=new[]{1}}};
            var input=source.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());if(kind=="Power")U16(input[Team],18,1);
            var options=new SwordShieldGameplayOptions(879,Trainers:true){RandomizeTrainerSpecies=false,TrainerRules=rules};
            var included=SwordShieldGameplay.Generate(input,personal,false,options);check(included.Changes.Any(c=>kind=="Smart"?c.Field=="Objeto equipado":c.Field.StartsWith("Movimiento")),"DivineSword_CategoryRuleAppliesToSelectedTrainer_"+kind);
            var excluded=rules with{BetterScope=Array.Empty<int>(),StrongScope=Array.Empty<int>(),PowerScope=Array.Empty<int>(),SmartItemScope=Array.Empty<int>()};check(SwordShieldGameplay.Generate(input,personal,false,options with{TrainerRules=excluded}).Changes.Count==0,"DivineSword_EmptyRuleScopePreservesEveryTrainer_"+kind);
        }
        var split=new SwordShieldTrainerOptions{BetterMovesets=true,SmartItems=true,BetterScope=new[]{1},SmartItemScope=new[]{2}};
        var scoped=SwordShieldGameplay.Generate(source,personal,false,new(879,Trainers:true){RandomizeTrainerSpecies=false,TrainerRules=split});check(scoped.Changes.Any(c=>c.Field.StartsWith("Movimiento"))&&scoped.Changes.All(c=>c.Field!="Objeto equipado"),"DivineSword_MovesAndItemsHaveIndependentScopes");
        reject(()=>SwordShieldBattlePolicy.Validate(new(1,Trainers:true){TrainerRules=new(){BetterScope=new[]{-1}}}),"DivineSword_RejectsInvalidRuleScope");
        var roundtrip=JsonSerializer.Deserialize<SwordShieldTrainerOptions>(JsonSerializer.Serialize(split))!;check(roundtrip.BetterScope!.SequenceEqual(new[]{1})&&roundtrip.SmartItemScope!.SequenceEqual(new[]{2}),"DivineSword_ScopesSurviveRecipeSerialization");
        check(JsonSerializer.Deserialize<SwordShieldTrainerOptions>("{\"BetterMovesets\":true}")!.BetterScope is null,"DivineSword_LegacyRecipeRetainsGlobalScope");
    }
    private static string Fixture(string temp)
    {
        string root=SwordShieldChecks.Fixture(temp,"source");var files=SwitchParityChecks.Inputs();foreach(int id in ExpectedIds.Concat(new[]{4,78,136,189,190})){files[$"bin/trainer/trainer_poke/trainer_poke_{id:000}.bin"]=(byte[])files[Team].Clone();files[$"bin/trainer/trainer_data/trainer_data_{id:000}.bin"]=(byte[])files["bin/trainer/trainer_data/trainer_data_001.bin"].Clone();}
        foreach(var p in files){string path=Path.Combine(root,"romfs",p.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,p.Value);}return root;
    }
    internal static void RunUi(Action<bool,string> check)
    {
        string temp=Path.Combine(Path.GetTempPath(),"dr-divine-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);try
        {
            var project=SwordShieldProject.Open(Fixture(temp));var editor=new SwordShieldRecordEditor(project,SwordShieldDataSection.TrainerTeams);var keys=editor.RandomOptions.SelectMany(g=>g.Fields).Select(f=>f.Key).ToArray();
            check(keys.Where(k=>k.EndsWith(".CapUse")).Select(k=>int.Parse(k.Split('.')[1])).Order().SequenceEqual(ExpectedIds.Order())&&!keys.Contains("Rule.4.CapUse"),"DivineSword_CapTableExposesOnlySelectedBattles");
            var view=new RecordEditorView(editor);check(((System.Windows.Controls.Button)view.FindName("TrainerMovesButton")).Visibility==Visibility.Collapsed&&!keys.Any(k=>k.EndsWith(".MoveUse")),"DivineSword_SwitchMoveRulesTableRemoved");
            var before=project.RevisionHash;((System.Windows.Controls.Button)view.FindName("TrainerCapsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));var panel=(DockPanel)((Border)((ContentControl)view.FindName("TrainerRulesHost")).Content).Child;var buttons=panel.Children.OfType<WrapPanel>().SelectMany(p=>p.Children.OfType<System.Windows.Controls.Button>()).ToArray();buttons.Single(b=>b.Content.ToString()=="DivineSword").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(ExpectedIds.All(id=>((System.Windows.Controls.CheckBox)view.OptionControls[$"Rule.{id}.CapUse"]).IsChecked==true&&((System.Windows.Controls.TextBox)view.OptionControls[$"Rule.{id}.LevelCap"]).Text==DivineSwordCaps.Levels[id].ToString())&&project.RevisionHash==before,"DivineSword_ButtonLoadsExactCapsWithoutWritingProject");
            buttons.Single(b=>b.Content.ToString()=="Cancelar").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));check(ExpectedIds.All(id=>((System.Windows.Controls.CheckBox)view.OptionControls[$"Rule.{id}.CapUse"]).IsChecked==false),"DivineSword_CancelRestoresCapsBeforePreset");
            foreach(var prefix in new[]{"GlobalMoves.Better","GlobalMoves.Strong","GlobalItems."})check(new[]{"Normal","Important","Boss"}.All(s=>view.OptionControls.ContainsKey(prefix+s)),"DivineSword_CategorySelectorsExist_"+prefix);
            var values=EditorValues.Flatten(editor.RandomOptions);values["Species"]=0;values["GlobalItems.Smart"]=1;values["GlobalItems.Normal"]=0;values["GlobalItems.Important"]=0;values["GlobalItems.Boss"]=0;check(editor.Preview(values,editor.Entries.Select(e=>e.Id).ToArray()).Changes.Count==0,"DivineSword_NoSelectedCategoryDoesNotModifyTeams");
            var capsProject=SwordShieldProject.Open(Fixture(Path.Combine(temp,"caps")));var capsEditor=new SwordShieldRecordEditor(capsProject,SwordShieldDataSection.TrainerTeams);var capValues=EditorValues.Flatten(capsEditor.RandomOptions);capValues["Species"]=0;
            foreach(var cap in DivineSwordCaps.Levels){capValues[$"Rule.{cap.Key}.CapUse"]=1;capValues[$"Rule.{cap.Key}.LevelCap"]=cap.Value;}
            var excludedBefore=capsProject.ReadData(SwordShieldDataSection.TrainerTeams).Where(c=>c.Field=="Nivel"&&(c.Entry==78||c.Entry==136)).Select(c=>c.Value).ToArray();
            capsEditor.Apply(capsEditor.Preview(capValues,capsEditor.Entries.Select(e=>e.Id).ToArray()));
            check(capsProject.PokeFeurTrainerCaps().Select(c=>c.Level).SequenceEqual(new[]{27,34,43,50,58,68,75,78,92}),"DivineSword_AppliedPresetReadsAllNineSwordBossCaps");
            check(capsProject.ReadData(SwordShieldDataSection.TrainerTeams).Where(c=>c.Field=="Nivel"&&(c.Entry==78||c.Entry==136)).Select(c=>c.Value).SequenceEqual(excludedBefore),"DivineSword_SwordPresetPreservesShieldExclusiveTeams");
            var old=new Dictionary<string,int>(values);foreach(string k in old.Keys.Where(k=>k.EndsWith("Normal")||k.EndsWith("Important")||k.EndsWith("Boss")).ToArray())old.Remove(k);old["Rule.4.MoveUse"]=1;old["Rule.4.Better"]=1;var json=JsonSerializer.Serialize(new{Format="DefinitiveRandomizer.SwitchOptions",Version=1,Section="TrainerTeams",Options=old});editor.RestoreOptions(json);check(EditorValues.Flatten(editor.RandomOptions)["GlobalItems.Normal"]==1&&!editor.RandomOptions.SelectMany(g=>g.Fields).Any(f=>f.Key=="Rule.4.MoveUse"),"DivineSword_OlderOptionsImportWithGlobalDefaultsAndNoMoveTable");
        }finally{Directory.Delete(temp,true);}
    }
    internal static void Probe(string root)
    {
        var project=SwordShieldProject.Open(root);var editor=new SwordShieldRecordEditor(project,SwordShieldDataSection.TrainerTeams);var values=EditorValues.Flatten(editor.RandomOptions);int[] actual=values.Keys.Where(k=>k.EndsWith(".CapUse")).Select(k=>int.Parse(k.Split('.')[1])).Order().ToArray();if(!actual.SequenceEqual(ExpectedIds.Order()))throw new Exception("Real selected cap rows differ.");values["Species"]=0;foreach(var c in DivineSwordCaps.Levels){values[$"Rule.{c.Key}.CapUse"]=1;values[$"Rule.{c.Key}.LevelCap"]=c.Value;}editor.Apply(editor.Preview(values,editor.Entries.Select(e=>e.Id).ToArray()));var levels=project.ReadData(SwordShieldDataSection.TrainerTeams).Where(c=>c.Field=="Nivel");if(levels.Where(c=>DivineSwordCaps.Levels.ContainsKey(c.Entry)).Any(c=>c.Value!=DivineSwordCaps.Levels[c.Entry]))throw new Exception("Real team cap mismatch.");Console.WriteLine("PASS DivineSword_RealFortySelectedCapsApplyToAllTeamSlotsInMemory");
        values=EditorValues.Flatten(editor.RandomOptions);values["Species"]=0;values["GlobalItems.Smart"]=1;values["GlobalItems.Normal"]=0;values["GlobalItems.Boss"]=0;var proposal=editor.Preview(values,editor.Entries.Select(e=>e.Id).ToArray());editor.Apply(proposal);var options=project.GameplayHistory.Last().Options.TrainerRules;if(options.SmartItemScope!.Any(id=>!project.TrainerKind(id).StartsWith("Importantes")))throw new Exception("Category mismatch.");var recipe=project.CaptureGlobal();var target=SwordShieldProject.Open(root);target.ApplyGlobal(target.PreviewGlobal(recipe));if(target.GameplayHistory.Count!=project.GameplayHistory.Count)throw new Exception("Replay history mismatch.");Console.WriteLine("PASS DivineSword_RealImportantOnlyItemsSurviveGlobalReplay");Console.WriteLine("Read-only source; in-memory changes only; no export/install/gameplay.");
    }
}
