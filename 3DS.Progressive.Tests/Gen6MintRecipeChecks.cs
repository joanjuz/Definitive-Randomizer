using System.Text.Json.Nodes;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using pk3DS.Core.Modding.Research;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

/// <summary>Pure synthetic recipe contracts. Does not read the owner's recipe/CSVs or admit a native installation.</summary>
internal static class Gen6MintRecipeChecks
{
    private static JsonObject Recipe(JsonObject? options=null,params string[] actions)
    {
        var root=new JsonObject{{"Version",1},{"ActionCoverageVersion",8},{"Game","XY"},{"Generation",6},
            {"Actions",new JsonArray(actions.Select(id=>(JsonNode)new JsonObject{{"Id",id},{"Parameters",new JsonObject{{"FutureParameter","preserve"}}}}).ToArray())}};
        if(options is not null)root["ProgressiveApp"]=new JsonObject{{Gen6MintExecutionContract.Node,options}};
        return root;
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(bool marts in new[]{false,true})foreach(bool field in new[]{false,true})
        {
            string tag=$"marts{marts}_field{field}";
            string json=Recipe(new(){{"IncludeMarts",marts},{"IncludeFieldItems",field}}).ToJsonString();
            var options=Gen6MintExecutionContract.ReadOptions(json);
            check(options.IncludeMarts==marts&&options.IncludeFieldItems==field&&!options.InstallMints&&!options.InstallIcons,
                "Gen6 mint recipe source toggles independent "+tag);
            check(Gen6MintExecutionContract.Read(json)==(marts||field),"Gen6 mint recipe compatibility Read is source union "+tag);
            Gen6MintExecutionContract.ValidateForExecution(json);
            check(true,"Gen6 mint recipe structural opt-in permits preinstalled dependency validation later "+tag);
        }
        foreach(bool include in new[]{false,true})
        {
            string legacy=Recipe(new(){{"Include",include}}).ToJsonString();var options=Gen6MintExecutionContract.ReadOptions(legacy);
            check(options.IncludeMarts==include&&options.IncludeFieldItems==include,"Gen6 mint recipe legacy inclusion maps both sources "+include);
            string consistent=Recipe(new(){{"Include",include},{"IncludeMarts",include},{"IncludeFieldItems",include}}).ToJsonString();
            check(Gen6MintExecutionContract.ReadOptions(consistent)==options,"Gen6 mint recipe consistent legacy and specific flags agree "+include);
            foreach(string key in new[]{"IncludeMarts","IncludeFieldItems"})
                reject(()=>Gen6MintExecutionContract.ReadOptions(Recipe(new(){{"Include",include},{key,!include}}).ToJsonString()),
                    "Gen6 mint recipe contradictory legacy and specific flag rejected "+include+" "+key);
        }
        foreach(string key in new[]{"IncludeMarts","IncludeFieldItems"})
        {
            var options=Gen6MintExecutionContract.ReadOptions(Recipe(new(){{key,true}}).ToJsonString());
            check(options.IncludeMarts==(key=="IncludeMarts")&&options.IncludeFieldItems==(key=="IncludeFieldItems"),
                "Gen6 mint recipe omitted sibling defaults false "+key);
        }
        foreach(string json in new[]{"{}","{\"ProgressiveApp\":null}","{\"ProgressiveApp\":{\"Unknown\":{\"IncludeMarts\":true}}}",
            Recipe(new(){{"IncludeMarts",false},{"IncludeFieldItems",false}}).ToJsonString()})
        {
            var options=Gen6MintExecutionContract.ReadOptions(json);Gen6MintExecutionContract.ValidateForExecution(json);
            check(!options.InstallMints&&!options.InstallIcons&&!options.IncludeMarts&&!options.IncludeFieldItems,
                "Gen6 mint recipe absent and off perform no implicit installation "+json);
        }
        foreach(bool install in new[]{false,true})foreach(bool icons in new[]{false,true})
        {
            var actions=new List<string>();if(install)actions.Add(Gen6MintExecutionContract.InstallAction);if(icons)actions.Add(Gen6MintExecutionContract.IconsAction);
            string json=Recipe(null,actions.ToArray()).ToJsonString();var options=Gen6MintExecutionContract.ReadOptions(json);
            Gen6MintExecutionContract.ValidateForExecution(json);
            check(options.InstallMints==install&&options.InstallIcons==icons&&!options.IncludeMarts&&!options.IncludeFieldItems&&!Gen6MintExecutionContract.Read(json),
                $"Gen6 mint recipe install actions never imply pool opt-in mint{install}_icons{icons}");
        }
        var reversed=Gen6MintExecutionContract.ReadOptions(Recipe(null,Gen6MintExecutionContract.IconsAction,Gen6MintExecutionContract.InstallAction).ToJsonString());
        check(reversed.InstallMints&&reversed.InstallIcons,"Gen6 mint recipe reader identifies bootstrap actions independent of array order");
        var unknown=Gen6MintExecutionContract.ReadOptions(Recipe(null,"future.module.action").ToJsonString());
        check(!unknown.InstallMints&&!unknown.InstallIcons,"Gen6 mint recipe unrelated actions do not request installation");
        var caseInsensitive=Gen6MintExecutionContract.ReadOptions(Recipe(null,Gen6MintExecutionContract.InstallAction.ToUpperInvariant()).ToJsonString());
        check(caseInsensitive.InstallMints,"Gen6 mint recipe action IDs match established case-insensitive recipe contract");

        foreach(string field in new[]{"Include","IncludeMarts","IncludeFieldItems"})
        {
            foreach(string value in new[]{"null","0","1","\"true\"","[]","{}"})
            {
                string json="{\"Game\":\"XY\",\"Generation\":6,\"ProgressiveApp\":{\"Gen6MintItems\":{\""+field+"\":"+value+"}}}";
                reject(()=>Gen6MintExecutionContract.ReadOptions(json),"Gen6 mint recipe nonboolean rejected "+field+" "+value);
            }
            string duplicate="{\"Game\":\"XY\",\"Generation\":6,\"ProgressiveApp\":{\"Gen6MintItems\":{\""+field+"\":true,\""+field+"\":false}}}";
            reject(()=>Gen6MintExecutionContract.ReadOptions(duplicate),"Gen6 mint recipe duplicate boolean rejected "+field);
        }
        foreach(var family in new[]{("ORAS",6),("USUM",7),("SM",7),("ANY",6),("XY",7)})
        {
            foreach(string source in new[]{"marts","field","mint action","icon action"})
            {
                var root=source=="marts"?Recipe(new(){{"IncludeMarts",true}}):source=="field"?Recipe(new(){{"IncludeFieldItems",true}}):
                    Recipe(null,source=="mint action"?Gen6MintExecutionContract.InstallAction:Gen6MintExecutionContract.IconsAction);
                root["Game"]=family.Item1;root["Generation"]=family.Item2;
                reject(()=>Gen6MintExecutionContract.ValidateForExecution(root.ToJsonString()),"Gen6 mint recipe foreign activation rejected "+family+" "+source);
            }
            var off=Recipe(new(){{"Include",false}});off["Game"]=family.Item1;off["Generation"]=family.Item2;
            var options=Gen6MintExecutionContract.ReadOptions(off.ToJsonString());
            check(!options.IncludeMarts&&!options.IncludeFieldItems,"Gen6 mint recipe foreign legacy false stays inactive "+family);
        }
        foreach(string json in new[]{"{","[]","{\"ProgressiveApp\":7}","{\"ProgressiveApp\":{\"Gen6MintItems\":null}}",
            "{\"ProgressiveApp\":{\"Gen6MintItems\":{\"IncludeMarts\":true}},\"ProgressiveApp\":{}}",
            "{\"ProgressiveApp\":{\"Gen6MintItems\":{\"IncludeFieldItems\":true},\"Gen6MintItems\":{}}}"})
            reject(()=>Gen6MintExecutionContract.ReadOptions(json),"Gen6 mint recipe malformed or ambiguous extension rejected "+json);
        string duplicateAction=Recipe(null,Gen6MintExecutionContract.InstallAction,Gen6MintExecutionContract.InstallAction).ToJsonString();
        reject(()=>Gen6MintExecutionContract.ReadOptions(duplicateAction),"Gen6 mint recipe duplicate native install action rejected");
        Roundtrip(check,reject);
        Seeds(check,reject);
        FirstMoveReplay(check);
        RuntimeCro(check,reject);
        BundledCsv(check);
        DependencyMerge(check,reject);
        var legacyAssembly=Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));
        var gen7=(IEnumerable<string>)legacyAssembly.GetType("pk3DS.WinForms.BatchGen7ActionExecutor",true)!.GetField("Supported",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        check(!gen7.Intersect(Gen6MintRecipeBridge.ActionIds,StringComparer.OrdinalIgnoreCase).Any(),"Gen6MintRecipe_Gen7ExecutorDoesNotAdvertiseXyInstallActions");
        var fromHistory=GlobalTemplateDocument.Empty("XY",6);
        fromHistory.MergeHistoryActions(new HistoryDocument(1,"XY","synthetic",new[]{new HistoryEntry(Guid.NewGuid(),DateTimeOffset.UnixEpoch,"Mints","Install",[],
            new Dictionary<string,string>{{"ActionId",Gen6NatureMintService.ActionId}},[])}));
        check(fromHistory.Actions.SequenceEqual(new[]{Gen6MintExecutionContract.InstallAction}),"Gen6MintRecipe_HistoryExportsExecutableInstallAction");
    }
    private static void FirstMoveReplay(Action<bool,string> check)
    {
        int[] Replay(int seed){Util.ReseedRand(seed);pk3DS.Core.Randomizers.MoveRandomizer.ResetFirstMoveSequence();return Enumerable.Range(0,40).Select(_=>pk3DS.Core.Randomizers.MoveRandomizer.GetRandomFirstMoveAny()).ToArray();}
        int[] first=Replay(77);_=Replay(88);
        check(first.SequenceEqual(Replay(77)),"Gen6MintRecipe_FirstMovePoolDoesNotLeakBetweenBatchRoms");
        check(!first.SequenceEqual(Replay(88)),"Gen6MintRecipe_FirstMovePoolUsesRequestedSeed");
        Util.ReseedRand(77);pk3DS.Core.Randomizers.MoveRandomizer.ResetFirstMoveSequence();
        check(Util.Rand.Next()==new Random(77).Next(),"Gen6MintRecipe_FirstMoveResetDrawsNoRng");
    }
    private static void DependencyMerge(Action<bool,string> check,Action<Action,string> reject)
    {
        using var f=new Fixture(GameVersion.XY);string path=Path.Combine(f.Root,"dependency.bin");byte[] bytes=[1,2,3];File.WriteAllBytes(path,bytes);
        byte[] hash=System.Security.Cryptography.SHA256.HashData(bytes);
        var proposal=new EditorProposal(Guid.NewGuid(),"Synthetic",1,[],[],new Dictionary<string,string>(),new[]{(path.Replace('\\','/'),hash)});
        var same=new Gen6MintInstallation(0x0004000000055D00UL,Gen6NatureMintCatalog.ExperimentalItemMap,new Dictionary<string,string>{{path,Convert.ToHexString(hash)}},()=>{});
        check(Gen6MintRecipeBridge.WithEvidence(proposal,same,"Gen6MintItems.IncludeMarts",true).Dependencies.Count==1,"Gen6MintRecipe_CanonicalDependencyPathsMergeWithoutDoubleLock");
        var different=new Gen6MintInstallation(0x0004000000055D00UL,Gen6NatureMintCatalog.ExperimentalItemMap,new Dictionary<string,string>{{path,new string('A',64)}},()=>{});
        reject(()=>Gen6MintRecipeBridge.WithEvidence(proposal,different,"Gen6MintItems.IncludeMarts",true),"Gen6MintRecipe_ConcurrentDependencyFingerprintsRejectBeforeWrite");
        check(File.ReadAllBytes(path).SequenceEqual(bytes),"Gen6MintRecipe_ConflictingEvidenceLeavesSourceIntact");
    }
    private static void BundledCsv(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.XY);
        string assets=Path.Combine(f.Root,"custom_balance_templates");Directory.CreateDirectory(assets);
        string installed=Path.Combine(f.Root,"old-installation");Directory.CreateDirectory(installed);
        string selected=Path.Combine(assets,"moves_gen6.csv"),old=Path.Combine(installed,"moves_gen6.csv");
        File.WriteAllText(selected,"Move,Power\n1,123\n");File.WriteAllText(old,"Move,Power\n1,40\n");
        var doc=GlobalTemplateDocument.Empty("XY",6);
        NativeMasterBatchBridge.Capture(doc,new(ThreeDS.Progressive.Models.BatchAction.MoveBalance,new(){TemplatePath=selected},0),6);
        var snapshot=(JsonObject)doc.Section("ProgressiveApp")!;snapshot["MasterBatches"]!["Moves"]!["Settings"]!["TemplatePath"]=old;
        doc.SetSection("ProgressiveApp",snapshot);
        var portable=GlobalTemplateDocument.Parse(doc.ToJson(),"XY",6,Path.Combine(f.Root,"recipe.json"));
        check(NativeMasterBatchBridge.Read(portable,"Moves")!.Settings.TemplatePath==selected,"Gen6MintRecipe_BundledHashMatchedCsvWinsOverInstalledPath");
        check(doc.Section("ProgressiveApp")!["MasterBatches"]!["Moves"]!["Settings"]!["TemplatePath"]!.GetValue<string>()==old,"Gen6MintRecipe_CsvResolutionDoesNotRewriteStoredOptions");
    }
    internal static void RunUi(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.XY);ModuleChecks.CreateArchives(f);
        string path=Path.Combine(f.Root,"mint-selection.json");GlobalTemplateDocument.Empty("XY",6).Save(path);
        var view=new GlobalTemplateView(f.Session);view.OpenPath(path);
        var fields=BindingFlags.Instance|BindingFlags.NonPublic;
        var marts=(CheckBox)typeof(GlobalTemplateView).GetField("_xyMintMarts",fields)!.GetValue(view)!;
        var soil=(CheckBox)typeof(GlobalTemplateView).GetField("_xyMintField",fields)!.GetValue(view)!;
        check(!Gen6MintExecutionContract.Read(view.ExecutionSnapshot().ToJson()),"Gen6MintUi_DefaultPoolsOff");
        var rows=(System.Collections.IEnumerable)typeof(GlobalTemplateView).GetField("_actions",fields)!.GetValue(view)!;
        foreach(var row in rows)row.GetType().GetProperty("Selected")!.SetValue(row,true);
        foreach(bool shops in new[]{false,true})foreach(bool field in new[]{false,true})
        {
            marts.IsChecked=shops;soil.IsChecked=field;var snapshot=view.ExecutionSnapshot();
            var options=Gen6MintExecutionContract.ReadOptions(snapshot.ToJson());
            check(options.InstallMints&&options.InstallIcons&&options.IncludeMarts==shops&&options.IncludeFieldItems==field&&snapshot.Actions.Contains("xy.persistent-items"),
                $"Gen6MintUi_CombinesThreeInstallsAndIndependentPools_{shops}_{field}");
            string saved=Path.Combine(f.Root,$"mint-{shops}-{field}.json");snapshot.Save(saved);
            var reopened=new GlobalTemplateView(f.Session);reopened.OpenPath(saved);
            check(reopened.ExecutionSnapshot().ToJson()==snapshot.ToJson(),$"Gen6MintUi_SaveLoadPreservesSelections_{shops}_{field}");
        }
        check(f.Session.History.Read().Entries.Count==0,"Gen6MintUi_ConfiguringRecipeNeverInstallsOrWritesHistory");
        view.Measure(new System.Windows.Size(752,370));view.Arrange(new System.Windows.Rect(0,0,752,370));view.UpdateLayout();
        var scroll=(ScrollViewer)view.Content;scroll.ScrollToBottom();view.UpdateLayout();
        var status=(TextBlock)typeof(GlobalTemplateView).GetField("_status",fields)!.GetValue(view)!;
        var bounds=status.TransformToAncestor(view).TransformBounds(new System.Windows.Rect(status.RenderSize));
        check(status.ActualHeight>0&&bounds.Top>=0&&bounds.Bottom<=view.ActualHeight+1&&scroll.VerticalScrollBarVisibility==ScrollBarVisibility.Auto,"Gen6MintUi_SmallHeightKeepsStatusReachableByScrolling");
    }
    private static void Seeds(Action<bool,string> check,Action<Action,string> reject)
    {
        string Seed(string value)=>"{\"Game\":\"XY\",\"Actions\":[{\"Id\":\"rng.seed\",\"Parameters\":{\"value\":"+value+"}}]}";
        foreach(int seed in new[]{0,1,123456,int.MaxValue})
        {
            string json=Seed(seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            check(Gen6MintExecutionContract.SeedForRom(json,0,37)==seed,"Gen6MintSeed_ExactSavedSeed_"+seed);
            check(Gen6MintExecutionContract.TrainerSeed(seed)==(seed^0x58594D54),"Gen6MintSeed_TrainerModuleUsesActualRomSeed_"+seed);
            var sequence=Enumerable.Range(0,50).Select(i=>Gen6MintExecutionContract.SeedForRom(json,i,37)).ToArray();
            check(sequence.Distinct().Count()==50&&sequence.All(s=>s>=0),"Gen6MintSeed_DistinctNonnegativeBatch_"+seed);
            check(sequence.SequenceEqual(Enumerable.Range(0,50).Select(i=>Gen6MintExecutionContract.SeedForRom(json,i,99))),"Gen6MintSeed_ReplayIndependentOfGeneratedSeed_"+seed);
        }
        check(Gen6MintExecutionContract.SeedForRom(Recipe().ToJsonString(),0,654321)==654321,"Gen6MintSeed_AbsentKeepsGeneratedSeed");
        check(Gen6MintExecutionContract.SeedForRom(Seed("123").Replace("XY","USUM"),0,456)==456,"Gen6MintSeed_UsumPreservesHistoricalSeed");
        check(Gen6MintExecutionContract.SeedForRom(Seed("\"123456\""),0,0)==123456,"Gen6MintSeed_StringValueAccepted");
        foreach(string value in new[]{"-1","2147483648","1.5","true","null","\" 1\"","{}","[]"})
            reject(()=>Gen6MintExecutionContract.SeedForRom(Seed(value),0,0),"Gen6MintSeed_InvalidValueRejected_"+value);
        reject(()=>Gen6MintExecutionContract.SeedForRom(Seed("1"),-1,0),"Gen6MintSeed_NegativeIndexRejected");
        reject(()=>Gen6MintExecutionContract.TrainerSeed(-1),"Gen6MintSeed_TrainerModuleRejectsUnknownSeed");
    }
    private static void RuntimeCro(Action<bool,string> check,Action<Action,string> reject)
    {
        // A synthetic CRO with typed encounter rows. No commercial executable or
        // synthetic hash is admitted as a native game profile by these checks.
        byte[] data=new byte[0xfa000];"CRO0"u8.CopyTo(data.AsSpan(0x80));
        BitConverter.GetBytes(0x180).CopyTo(data,0xb0);BitConverter.GetBytes(data.Length-0x180).CopyTo(data,0xb4);
        BitConverter.GetBytes(data.Length).CopyTo(data,0xb8);BitConverter.GetBytes(data.Length).CopyTo(data,0xc0);
        data=CroIntegrity.Rebuild(data);
        byte[] baseline=Gen6MintRuntimeDependencies.NormalizeCro("DllField.cro",data);
        byte[] Edit(int at,byte value){var b=(byte[])data.Clone();b[at]=value;return CroIntegrity.Rebuild(b);}
        foreach(var edit in new[]{(0xee46c,25),(0xee46c+3,50),(0xf805c,25),(0xf805c+12,1)})
            check(Gen6MintRuntimeDependencies.NormalizeCro("DllField.cro",Edit(edit.Item1,(byte)edit.Item2)).SequenceEqual(baseline),"Gen6MintRuntime_AccreditedEncounterDataNormalizes_"+edit.Item1);
        foreach(int at in new[]{0x500,0xee46c+8,0xee46c+6,0xf805c+2,0xf805c+9,0xf805c+23})
        {
            byte value=at==0xee46c+6?(byte)0x80:(byte)1;
            check(!Gen6MintRuntimeDependencies.NormalizeCro("DllField.cro",Edit(at,value)).SequenceEqual(baseline),"Gen6MintRuntime_InstructionsAndOpaqueFieldsRemainPinned_"+at);
        }
        reject(()=>Gen6MintRuntimeDependencies.NormalizeCro("DllField.cro",Edit(0xee46c+3,101)),"Gen6MintRuntime_InvalidLevelRejected");
        reject(()=>Gen6MintRuntimeDependencies.NormalizeCro("DllField.cro",data[..0x800]),"Gen6MintRuntime_TruncatedCroRejected");
        var stale=(byte[])data.Clone();stale[0x500]^=1;
        reject(()=>Gen6MintRuntimeDependencies.NormalizeCro("DllField.cro",stale),"Gen6MintRuntime_StaleHashesRejected");
    }
    private static void Roundtrip(Action<bool,string> check,Action<Action,string> reject)
    {
        var doc=GlobalTemplateDocument.Empty("XY",6);
        doc.SetSection("RandSettings",new JsonArray("TrainerEditor6","NUD_MinBST\t300","NUD_MaxBST\t600","FutureControl\t77",""));
        doc.SetSection("Wild",new JsonObject{{"MinBST",250},{"MaxBST",550},{"UnknownWildOption",new JsonArray(8,9)}});
        doc.SetSection("FutureRoot",new JsonObject{{"Nested",new JsonArray("retain",null,37)}});
        doc.SetSection("Actions",new JsonArray(new JsonObject{{"Id","moves.balance"},{"Parameters",new JsonObject{{"Csv","custom_balance_templates/moves_gen6.csv"},{"FutureParameter","keep"}}}},
            new JsonObject{{"Id","evolutions.normalize"},{"Parameters",new JsonObject{{"Csv","custom_balance_templates/evolutions_gen6.csv"},{"SeedPolicy","FreshPerRun"}}}}));
        doc.SetSection("Assets",new JsonArray(new JsonObject{{"Role","MoveBalance"},{"FileName","moves_gen6.csv"},{"Sha256",new string('A',64)}},
            new JsonObject{{"Role","Evolutions"},{"FileName","evolutions_gen6.csv"},{"Sha256",new string('B',64)}}));
        doc.SetSection("ProgressiveApp",new JsonObject{{"UnknownModule",new JsonObject{{"Array",new JsonArray(4,"opaque")}}},
            {Gen6MintExecutionContract.Node,new JsonObject{{"FutureMintOption",new JsonArray("keep",42)}}}});
        string before=doc.ToJson();
        foreach(bool marts in new[]{false,true})foreach(bool field in new[]{false,true})
        {
            var candidate=doc.Clone();Gen6MintItemPolicy.Capture(candidate,marts,field);
            var parsed=GlobalTemplateDocument.Parse(candidate.ToJson(),"XY",6);var options=Gen6MintExecutionContract.ReadOptions(parsed.ToJson());
            check(options.IncludeMarts==marts&&options.IncludeFieldItems==field,$"Gen6 mint recipe independent flags survive global roundtrip marts{marts}_field{field}");
            foreach(string section in new[]{"RandSettings","Wild","Actions","Assets","FutureRoot"})
                check(JsonNode.DeepEquals(doc.Section(section),parsed.Section(section)),$"Gen6 mint recipe preserves synthetic BST CSV assets unknown sections marts{marts}_field{field} {section}");
            check(JsonNode.DeepEquals(doc.Section("ProgressiveApp")?["UnknownModule"],parsed.Section("ProgressiveApp")?["UnknownModule"])&&
                JsonNode.DeepEquals(doc.Section("ProgressiveApp")?[Gen6MintExecutionContract.Node]?["FutureMintOption"],parsed.Section("ProgressiveApp")?[Gen6MintExecutionContract.Node]?["FutureMintOption"]),
                $"Gen6 mint recipe capture preserves unknown sibling and mint fields marts{marts}_field{field}");
            check(doc.ToJson()==before,$"Gen6 mint recipe capture on clone never mutates source document marts{marts}_field{field}");
        }
        var legacy=GlobalTemplateDocument.Empty("XY",6);Gen6MintItemPolicy.Capture(legacy,true);
        var old=Gen6MintExecutionContract.ReadOptions(legacy.ToJson());
        check(old.IncludeMarts&&old.IncludeFieldItems,"Gen6 mint recipe legacy Capture overload still enables both sources");
        Gen6MintItemPolicy.Capture(legacy,false,true);
        var independent=Gen6MintExecutionContract.ReadOptions(legacy.ToJson());
        check(!independent.IncludeMarts&&independent.IncludeFieldItems,"Gen6 mint recipe independent Capture replaces obsolete legacy aggregate without contradiction");
        var off=GlobalTemplateDocument.Empty("XY",6);string offBefore=off.ToJson();Gen6MintItemPolicy.Capture(off,false,false);
        check(off.ToJson()==offBefore,"Gen6 mint recipe default off Capture adds no extension or action");
        var foreign=GlobalTemplateDocument.Empty("USUM",7);string foreignBefore=foreign.ToJson();
        reject(()=>Gen6MintItemPolicy.Capture(foreign,true,false),"Gen6 mint recipe foreign Capture rejects activation");
        check(foreign.ToJson()==foreignBefore,"Gen6 mint recipe foreign Capture rejection remains atomic");
    }
}
