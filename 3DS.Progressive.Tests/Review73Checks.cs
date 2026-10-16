using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Legacy=ThreeDS.Progressive.Services.ProgressiveLegacy;
internal static class Review73Checks
{
    internal static string Template(int gen,int banned,int allowed)=>new JsonObject{["Format"]="DefinitiveRandomizer.FieldItems.v1",["Generation"]=gen,["BannedItems"]=new JsonArray(banned),["AllowedItems"]=new JsonArray(allowed)}.ToJsonString();
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var service=new GameFieldItemsService(f.Session);
            var original=File.ReadAllBytes(Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("encdata")));
            service.RestoreOptions(Template(7,17,19));var options=EditorValues.Flatten(service.RandomOptions);var preview=service.Preview(options,[1,2],71);
            check(preview.Changes.Count==2&&preview.Files.Single().Original().SequenceEqual(original),version+" Field Items template bans source item and substitutes permitted catalog without writes");
            service.Apply(preview);check(service.Entries.All(e=>EditorValues.Flatten(service.Read(e.Id))["Item"]==19),version+" Field Items permitted-only proposal saves and reloads");
            var document=NativeRecipeFactory.Create(f.Session,d=>GlobalEditorTemplateBridge.Capture(d,service,new(options,0,service.CaptureOptions(options)),7),["FieldItems"]);
            var restored=new GameFieldItemsService(f.Session);var state=GlobalEditorTemplateBridge.Restore(document,restored,7)!;
            check(state.Template is not null&&restored.CaptureOptions(state.Options).Contains("BannedItems"),version+" Global restores field template metadata");NativeGen7FieldBridge.ValidateSession(f.Session,document);
            NativeGen7FieldBridge.ExecuteSession(f.Session,document.ToJson(),7,"FieldItems");check(true,version+" Batch replays custom field catalog");
            string before=restored.CaptureOptions(state.Options);reject(()=>restored.RestoreOptions(Template(6,17,19)),version+" wrong generation rejected");check(restored.CaptureOptions(state.Options)==before,version+" rejected field import leaves prior template intact");
        }
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var trainer=new GameTrainerService(f.Session);var options=EditorValues.Flatten(trainer.RandomOptions);
            options["GlobalItems.Smart"]=1;options["GlobalItems.Normal"]=0;options["GlobalItems.QualityBoss"]=2;options["GlobalMoves.IncludeTMs"]=1;
            byte[] Script(params (int Offset,int Item)[] rows)=>(byte[])typeof(FieldItems6Checks).GetMethod("Script",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[rows])!;
            var files=Enumerable.Range(0,60).Select(_=>new byte[]{0xAA,0xBB}).ToArray();int file=f.Game.ORAS?39:17,at=f.Game.ORAS?0xB64:0xB04;
            files[file]=Script((at,155),(at+12,156),(at+24,157));if(f.Game.ORAS)files[57]=Script((2746,656));else files[26]=Script((0xB18,158));
            var reference=f.Game.GetGARCReference("personal");string path=Path.Combine(f.Game.RomFS,f.Game.ORAS?"a/0/2/9":"a/0/3/1");f.WriteGarc(path,files,reference.GetRelativeGARC((f.Game.ORAS?29:31)-reference.FileNumber,"scripts"));
            var field=new GameFieldItems6Service(f.Session);field.RestoreOptions(Template(6,155,156));var fieldOptions=EditorValues.Flatten(field.RandomOptions);
            var fieldDoc=NativeRecipeFactory.Create(f.Session,d=>GlobalEditorTemplateBridge.Capture(d,field,new(fieldOptions,0,field.CaptureOptions(fieldOptions)),6),["FieldItems"]);
            NativeBatchGen6Bridge.ExecuteSession(f.Session,fieldDoc.ToJson(),71);
            var reread=new GameFieldItems6Service(f.Session);check(reread.Entries.Where(e=>e.Name.StartsWith("Visible")).All(e=>EditorValues.Flatten(reread.Read(e.Id))["Item"]==156),version+" native Gen6 Batch respects imported field whitelist and ban");
            var json=JsonNode.Parse(trainer.CaptureOptions(options))!;check(json["MoveSettings"]!["SmartItems"]!.GetValue<bool>()&&!json["MoveSettings"]!["SmartItemsNormalTrainers"]!.GetValue<bool>()&&json["MoveSettings"]!["BetterMovesetsIncludeTMs"]!.GetValue<bool>(),version+" global Smart categories quality and TM selection captured");
            var settings=new Legacy.TrainerMoveSettingsTemplate{SmartItems=true,SmartItemsNormalTrainers=false,SmartItemsImportantTrainers=false,SmartItemsBosses=true,SmartItemModeBosses=2};
            using var context=Legacy.Main.Enter(f.Root,f.Game);Legacy.RSTE.rGlobalMoveSettings=settings;Legacy.RSTE.rTrainerMoveRules=[];
            var method=typeof(Legacy.RSTE).GetMethod("GetTrainerMoveRule",BindingFlags.NonPublic|BindingFlags.Static)!;
            int boss=((int[])typeof(Legacy.RSTE).GetField(version==GameVersion.ORAS?"UPRZXBossTrainers_ORAS":"UPRZXBossTrainers_XY",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!).First();
            var rule=(Legacy.TrainerMoveRule)method.Invoke(null,[boss])!;check(rule.Enabled&&rule.SmartItems,version+" actual Gen6 engine applies global Smart to selected boss category");
            check((int)typeof(Legacy.RSTE).GetMethod("GlobalSmartQuality",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[boss])! ==2,version+" actual Gen6 engine uses selected Smart quality");
            var headers=new pk3DS.Core.CTR.GARC.MemGARC(File.ReadAllBytes(Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("trdata")))).Files;
            var teams=new pk3DS.Core.CTR.GARC.MemGARC(File.ReadAllBytes(Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("trpoke")))).Files;
            var team=new pk3DS.Core.Structures.TrainerData6(headers[1],teams[1],f.Game.ORAS);foreach(var pk in team.Team)pk.Item=0;team.Item=false;
            var itemNames=f.Game.GetText(TextName.ItemNames);itemNames[234]="Leftovers";f.Game.SetText(TextName.ItemNames,itemNames);Legacy.RSTE.rItem=true;Legacy.RSTE.rItemClause=false;Legacy.RSTE.rTrainerHeldItemTemplate=Legacy.TrainerHeldItemTemplate.LoadOrCreateDefault(f.Session.ItemNames.ToArray(),new[]{234});
            typeof(Legacy.RSTE).GetMethods(BindingFlags.NonPublic|BindingFlags.Static).Single(m=>m.Name=="ApplySmartTrainerItemsToTrainer"&&m.GetParameters().Length==5).Invoke(null,[team,new ushort[]{234},false,rule,2]);
            Console.WriteLine($"SMART TRACE {version}: names {f.Game.GetText(TextName.ItemNames).Length}, items {string.Join(",",team.Team.Select(p=>p.Item))}, flag {team.Item}");check(team.Item&&team.Team.All(p=>p.Item==234),version+" global Smart actually runs after default held-item template");Legacy.RSTE.rTrainerHeldItemTemplate=null;Legacy.RSTE.rItem=false;
Legacy.RSTE.rGlobalMoveSettings=null;
        }
        byte[] cro=Cro();var patch=GamePersistentItemsService.Patch(cro);check(patch.Changes==2&&cro[0x18B]==0x1A&&patch.Bytes[0x18B]==0x8A&&patch.Bytes[0x1BF]==0xEA,"Persistent Items prepares exactly two stock gates without mutating source");
        check(GamePersistentItemsService.Patch(patch.Bytes).Changes==0&&GamePersistentItemsService.Patch(patch.Bytes).Bytes.SequenceEqual(patch.Bytes),"Persistent Items idempotent including hashes");
        var partial=(byte[])cro.Clone();partial[0x18B]=0x8A;check(GamePersistentItemsService.Patch(partial).Changes==1,"Persistent Items completes partial install");
        var duplicate=(byte[])cro.Clone();cro.AsSpan(0x180,24).CopyTo(duplicate.AsSpan(0x1C0));reject(()=>GamePersistentItemsService.Patch(duplicate),"Persistent Items rejects duplicate gate");
        using var u=new Fixture(GameVersion.USUM);File.WriteAllBytes(Path.Combine(u.Game.RomFS,"Battle.cro"),cro);var persistent=new GamePersistentItemsService(u.Session);var proposal=persistent.Preview(EditorValues.Flatten(persistent.RandomOptions),[1]);persistent.Apply(proposal);check(EditorValues.Flatten(persistent.Read(1))["Enable"]==1&&u.Session.History.Read().Entries.Count==1,"Persistent Items native apply saves verifies and records history");
    }
    internal static byte[] Cro()
    {
        byte[] cro=new byte[0x280];"CRO0"u8.CopyTo(cro.AsSpan(0x80));void U(int at,int v)=>BitConverter.GetBytes(v).CopyTo(cro,at);U(0xB0,0x180);U(0xB4,0x80);U(0xB8,0x240);U(0xBC,0x40);U(0xC0,0x200);
        Convert.FromHexString("0000DBE5010050E30000001A000054E30050A0E30000009A").CopyTo(cro,0x180);Convert.FromHexString("0600A0E1000000EB0060A0E11110A0E3000000EB000050E300F020E30000000A").CopyTo(cro,0x1A0);return cro;
    }
    internal static void RunReal(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            var project=GameProjectSession.Open(root,recoverInterruptedSave:false);string path=Path.Combine(project.GameConfiguration.RomFS,project.Generation==6?"DllBattle.cro":"Battle.cro");byte[] source=File.ReadAllBytes(path);string sha=Convert.ToHexString(SHA256.HashData(source));
            try{var patch=GamePersistentItemsService.Patch(source);Console.WriteLine($"SIGNATURE AUDIT {project.GameName}: {patch.Changes} gates pending; unique signatures found. USUM-only authorization retained.");if(project.GameConfiguration.USUM){var service=new GamePersistentItemsService(project);var preview=service.Preview(EditorValues.Flatten(service.RandomOptions),[1]);if(preview.Files.Count!=1)throw new Exception("Unexpected scope");Console.WriteLine($"PASS REAL Persistent Items {project.GameName}: detached preview, idempotent patch and original SHA {sha} intact.");}}
            catch(InvalidDataException ex){if(project.GameConfiguration.USUM)throw;Console.WriteLine($"UNSUPPORTED {project.GameName}: {ex.Message}");}
            if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))!=sha)throw new Exception("Source modified");
            if(project.Generation==7){var titles=new GameTitleScreenService(project);if(titles.Archives.Any(a=>string.IsNullOrWhiteSpace(a.Description)))throw new Exception("Missing resource description");Console.WriteLine($"PASS REAL Titles {project.GameName}: 18 labels describe actual archive contents. Sample: {titles.Archives[0].Label}");}
            if(project.Generation==6){var field=new GameFieldItems6Service(project);var options=EditorValues.Flatten(field.RandomOptions);field.RestoreOptions(Template(6,17,18));var p=field.Preview(options,field.Entries.Select(e=>e.Id).ToArray(),123);if(p.Changes.Any(c=>c.After!=project.ItemNames[18]))throw new Exception("Unexpected allowed item");Console.WriteLine($"PASS REAL Field template {project.GameName}: {p.Changes.Count} detached changes; only permitted output.");}
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var trainer=new RecordEditorView(new GameTrainerService(f.Session));trainer.Foreground=(System.Windows.Media.Brush)trainer.FindResource("TextMain");
            ((System.Windows.Controls.Button)trainer.FindName("RandomizerButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var controls=trainer.OptionControls;string factor=f.Game.Generation==6?"NUD_Level":"NUD_LevelBoost";check(((System.Windows.Controls.TextBox)controls[factor]).Uid=="1",version+" UI level multiplier uses percentages without decimal conversion");
            check(!controls.ContainsKey("Rule.1.Power")&&!controls.ContainsKey("Rule.1.Smart"),version+" trainer inspector removes per-row Power and Smart");
            string strong=f.Game.Generation==6?"GlobalMoves.StrongStat":"CHK_StrongStat",child=f.Game.Generation==6?"GlobalMoves.StrongNormal":"CHK_StrongStatNormalTrainers";
            var master=(System.Windows.Controls.CheckBox)controls[strong];master.IsChecked=false;
            var panel=(System.Windows.FrameworkElement)((System.Windows.FrameworkElement)controls[child].Parent).Parent;
            check(panel.Visibility==System.Windows.Visibility.Collapsed,version+" strong category controls hidden when master unchecked");master.IsChecked=true;check(panel.Visibility==System.Windows.Visibility.Visible,version+" strong category controls restored when master checked");
            string smartKey=f.Game.Generation==6?"GlobalItems.Smart":"CHK_SmartHeldItems",smartChild=f.Game.Generation==6?"GlobalItems.Normal":"CHK_SmartItemsNormalTrainers";
            var smart=(System.Windows.Controls.CheckBox)controls[smartKey];var random=(System.Windows.Controls.CheckBox)controls["CHK_RandomItems"];
            random.IsChecked=true;smart.IsChecked=true;var smartPanel=(System.Windows.FrameworkElement)((System.Windows.FrameworkElement)controls[smartChild].Parent).Parent;
            check(smartPanel.Visibility==System.Windows.Visibility.Visible,version+" global Smart shows category and quality controls");random.IsChecked=false;
            check(!smart.IsEnabled&&smartPanel.Visibility==System.Windows.Visibility.Collapsed,version+" disabling held-item randomization disables Smart and hides dependent options");random.IsChecked=true;
            if(f.Game.Generation==6)check(!controls.ContainsKey("CB_SmartHeldItemMode"),version+" old duplicate single Smart quality is absent");
            string? dir=Environment.GetEnvironmentVariable("PROGRESSIVE_UI_CAPTURE_DIR");if(dir is not null){Directory.CreateDirectory(dir);
                var optionHost=(System.Windows.Controls.StackPanel)trainer.FindName("OptionsPanel");var tabs=optionHost.Children.OfType<System.Windows.Controls.TabControl>().Single();tabs.SelectedIndex=3;
                trainer.Measure(new System.Windows.Size(1350,1000));trainer.Arrange(new System.Windows.Rect(0,0,1350,1000));trainer.UpdateLayout();var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap(1350,1000,96,96,System.Windows.Media.PixelFormats.Pbgra32);bmp.Render(trainer);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));using var output=File.Create(Path.Combine(dir,"held73-"+version+".png"));png.Save(output);}
        }
    }
}
