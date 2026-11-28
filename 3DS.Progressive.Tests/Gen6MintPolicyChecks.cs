using System.IO;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Reflection;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;

/// <summary>Synthetic policy checks; the fixture item IDs establish no native XY compatibility.</summary>
internal static class Gen6MintPolicyChecks
{
    private static Gen6MintItem[] SyntheticItems()=>Gen6NatureMintCatalog.Natures.Select((n,i)=>new Gen6MintItem(600+i,n.NatureId)).ToArray();
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        Gen6MintIconChecks.Run(check,reject);
        var rows=SyntheticItems();var map=new Gen6MintItemMap(rows);
        check(Gen6NatureMintCatalog.Natures.Count==21&&Gen6NatureMintCatalog.Natures.Count(n=>n.NatureId%6==0)==1&&
            Gen6NatureMintCatalog.Natures.Single(n=>n.NatureId%6==0).NatureId==12,"Gen6 mint metadata:20 nonneutral and Serious only");
        check(Gen6NatureMintCatalog.OfficialItemMap is null,"Gen6 official item slots remain unaccredited");
        check(Gen6NatureMintCatalog.Natures.Select(n=>n.NatureId).Order().SequenceEqual(new[]{1,2,3,4,5,7,8,9,10,11,12,13,14,15,16,17,19,20,21,22,23})&&
            Gen6NatureMintCatalog.Natures.Single(n=>n.NatureId==12).EnglishNature=="Serious","Gen6 mint nature set matches native nature numbering without item assignment");
        reject(()=>new Gen6MintItemMap(rows[..20]),"Gen6 mint map rejects missing entry");
        reject(()=>new Gen6MintItemMap(rows.Append(rows[0])),"Gen6 mint map rejects extra entry");
        foreach(int bad in new[]{-1,0,718,719})
            reject(()=>new Gen6MintItemMap(rows.Select((r,i)=>i==0?r with{ItemId=bad}:r)),"Gen6 mint map rejects out-of-range item "+bad);
        foreach(int good in new[]{1,717})
            check(new Gen6MintItemMap(rows.Select((r,i)=>i==0?r with{ItemId=good}:r)).Items[0].ItemId==good,"Gen6 mint map accepts item boundary "+good);
        reject(()=>new Gen6MintItemMap(rows.Select((r,i)=>i==1?r with{ItemId=rows[0].ItemId}:r)),"Gen6 mint map rejects duplicate item");
        reject(()=>new Gen6MintItemMap(rows.Select((r,i)=>i==1?r with{NatureId=rows[0].NatureId}:r)),"Gen6 mint map rejects duplicate nature");
        foreach(int bad in new[]{0,6,18,24,25,-1})
            reject(()=>new Gen6MintItemMap(rows.Select((r,i)=>i==0?r with{NatureId=bad}:r)),"Gen6 mint map rejects wrong nature "+bad);
        rows[0]=new(1,1);check(map.Items[0].ItemId==600,"Gen6 mint map defensively copies input");

        int captures=0,verifies=0;
        var dependencies=new Dictionary<string,string>{{"synthetic-only",new string('A',64)}};
        var installed=new Gen6MintInstallation(0x0004000000055D00UL,map,dependencies,()=>verifies++);
        dependencies["synthetic-only"]=new string('B',64);
        check(installed.Dependencies["synthetic-only"]==new string('A',64),"Gen6 installation freezes dependency hashes");
        var policy=new Gen6MintItemPolicy("XY",6,()=>{captures++;return installed;});
        check(!policy.Include,"Gen6 mint inclusion defaults false even with injected installation evidence");
        int[] weighted=[3,3,600,2,3,600];
        var off=policy.Pool(weighted,false,new HashSet<int>(),new HashSet<int>{3,600,2});
        check(off.SequenceEqual(weighted)&&captures==0&&verifies==0,"Gen6 mint off preserves weighted order and bypasses accreditation and filters");
        check(policy.Pool([],false).Length==0&&captures==0,"Gen6 mint empty off does not capture");
        var on=policy.Pool(weighted,true);
        check(on.Take(weighted.Length).SequenceEqual(weighted)&&on.Length==weighted.Length+20&&on.Count(i=>i==600)==2&&
            on.Skip(weighted.Length).SequenceEqual(Enumerable.Range(601,20)),"Gen6 mint on appends missing IDs once without changing original weighting");
        check(captures==1&&verifies==2,"Gen6 mint inclusion captures once and verifies before and after pool creation");
        check(policy.Pool([2,2,3],true,new HashSet<int>{2,600,601},new HashSet<int>{600}).SequenceEqual(new[]{2,2,601}),
            "Gen6 mint whitelist and ban intersection never revives banned mint");
        check(policy.Pool([],true,new HashSet<int>()).Length==0,"Gen6 mint explicit empty whitelist yields no candidates");
        check(policy.Pool([2],true).SequenceEqual(policy.Pool([2],true)),"Gen6 mint inclusion is deterministic without RNG");
        var blocked=new Gen6MintItemPolicy("XY",6);
        check(!blocked.Inspect().Available&&blocked.Inspect().Report==Gen6MintItemPolicy.BlockedReason,"Gen6 mint default provider reports explicit native blocker");
        reject(()=>blocked.Pool([],true),"Gen6 mint missing installation rejects inclusion");
        int failureCalls=0;
        var providerFailure=new Gen6MintItemPolicy("XY",6,()=>{failureCalls++;throw new InvalidDataException("Synthetic unrecognized installation");});
        reject(()=>providerFailure.Pool([600],true),"Gen6 mint unrecognized provider cannot admit even preexisting mint IDs");
        check(failureCalls==1,"Gen6 mint provider failure is not retried or bypassed");
        foreach(var family in new[]{("ORAS",6),("USUM",7),("XY",7)})
        {
            var incompatible=new Gen6MintItemPolicy(family.Item1,family.Item2,()=>throw new Exception("Provider must not run"));
            check(incompatible.Pool(weighted,false).SequenceEqual(weighted),"Gen6 mint incompatible off preserves original "+family);
            reject(()=>incompatible.Pool([],true),"Gen6 mint incompatible family rejects before provider "+family);
        }
        reject(()=>new Gen6MintInstallation(0,map,new Dictionary<string,string>{{"x",new string('A',64)}},()=>{}),"Gen6 mint evidence rejects foreign title");
        reject(()=>new Gen6MintInstallation(0x0004000000055D00UL,map,new Dictionary<string,string>(),()=>{}),"Gen6 mint evidence rejects absent dependencies");
        reject(()=>new Gen6MintInstallation(0x0004000000055D00UL,map,new Dictionary<string,string>{{"x",new string('Z',64)}},()=>{}),"Gen6 mint evidence rejects invalid SHA256");
        reject(()=>new Gen6MintInstallation(0x0004000000055D00UL,map,new Dictionary<string,string>{{"x",new string('A',63)}},()=>{}),"Gen6 mint evidence rejects truncated SHA256");
        reject(()=>new Gen6MintInstallation(0x0004000000055D00UL,map,new Dictionary<string,string>{{" ",new string('A',64)}},()=>{}),"Gen6 mint evidence rejects unnamed dependency");
        int checkedAtCapture=0;
        var changedBefore=new Gen6MintItemPolicy("XY",6,()=>new(0x0004000000055D00UL,map,new Dictionary<string,string>{{"x",new string('A',64)}},()=>{
            checkedAtCapture++;throw new InvalidDataException("Synthetic stale captured dependency");}));
        reject(()=>changedBefore.Pool([1],true),"Gen6 mint stale installation evidence rejects before constructing candidates");
        check(checkedAtCapture==1,"Gen6 mint stale evidence stops after first verification");
        int verification=0;
        var raced=new Gen6MintItemPolicy("XY",6,()=>new(0x0004000000055E00UL,map,new Dictionary<string,string>{{"x",new string('A',64)}},()=>{
            if(++verification==2)throw new InvalidDataException("Synthetic dependency race");}));
        reject(()=>raced.Pool(weighted,true),"Gen6 mint dependency race after pool construction rejects result");
        check(weighted.SequenceEqual(new[]{3,3,600,2,3,600}),"Gen6 mint rejected and accepted pools do not mutate input");

        var doc=GlobalTemplateDocument.Empty("XY",6);
        string absent=doc.ToJson();Gen6MintItemPolicy.Capture(doc,false);
        check(!Gen6MintItemPolicy.Read(doc)&&doc.ToJson()==absent,"Gen6 mint legacy absence defaults false without new node");
        doc.SetSection("ProgressiveApp",new JsonObject{{"Unknown",new JsonArray(4,"opaque")},{Gen6MintItemPolicy.Node,new JsonObject{{"Future",7},{"Include",false}}}});
        Gen6MintItemPolicy.Capture(doc,true);
        check(Gen6MintItemPolicy.Read(doc)&&doc.Section("ProgressiveApp")?[Gen6MintItemPolicy.Node]?["Future"]?.GetValue<int>()==7&&
            doc.Section("ProgressiveApp")?["Unknown"]?.ToJsonString()=="[4,\"opaque\"]","Gen6 mint capture preserves unknown policy and sibling fields");
        var parsed=GlobalTemplateDocument.Parse(doc.ToJson(),"XY",6);
        check(Gen6MintItemPolicy.Read(parsed),"Gen6 mint opt-in survives global serialization");
        Gen6MintItemPolicy.Capture(doc,false);check(!Gen6MintItemPolicy.Read(doc),"Gen6 mint explicit opt-out survives capture");
        foreach(var (caseName,malformed) in new (string,JsonNode)[]{("scalar",JsonValue.Create(true)!),
            ("missing Include",new JsonObject()),("string Include",new JsonObject{{"Include","true"}})})
        {
            var invalid=GlobalTemplateDocument.Empty("XY",6);invalid.SetSection("ProgressiveApp",new JsonObject{{Gen6MintItemPolicy.Node,malformed}});
            reject(()=>Gen6MintItemPolicy.Read(invalid),"Gen6 mint malformed policy rejects "+caseName);
        }
        var invalidCapture=GlobalTemplateDocument.Empty("XY",6);
        invalidCapture.SetSection("ProgressiveApp",new JsonObject{{Gen6MintItemPolicy.Node,7},{"Retain",true}});
        string malformedBefore=invalidCapture.ToJson();
        reject(()=>Gen6MintItemPolicy.Capture(invalidCapture,true),"Gen6 mint capture rejects scalar policy rather than replacing it");
        check(invalidCapture.ToJson()==malformedBefore,"Gen6 mint malformed capture preserves original document");
        var usum=GlobalTemplateDocument.Empty("USUM",7);string before=usum.ToJson();
        reject(()=>Gen6MintItemPolicy.Capture(usum,true),"Gen6 mint capture rejects incompatible game");
        check(usum.ToJson()==before,"Gen6 mint rejected global capture is atomic");
        usum.SetSection("ProgressiveApp",new JsonObject{{Gen6MintItemPolicy.Node,new JsonObject{{"Include",true}}}});
        reject(()=>Gen6MintItemPolicy.Read(usum),"Gen6 mint read rejects incompatible opt-in");
        ExecutionContract(check,reject);
        Gen6MintNativeChecks.Run(check,reject);
        Gen6MintServiceChecks.Run(check,reject);
    }

    private static void ExecutionContract(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(string json in new[]{"{}","{\"ProgressiveApp\":null}","{\"ProgressiveApp\":{\"Future\":[1,2]}}",
            "{\"Game\":\"USUM\",\"Generation\":7,\"ProgressiveApp\":{\"Gen6MintItems\":{\"Include\":false,\"Future\":3}}}"})
        {
            using var parsed=JsonDocument.Parse(json);string before=parsed.RootElement.GetRawText();
            Gen6MintExecutionContract.ValidateForExecution(json);
            check(!Gen6MintExecutionContract.Read(parsed.RootElement)&&before==parsed.RootElement.GetRawText(),"Gen6 mint execution absent or false remains read-only "+json);
        }
        string optIn="{\"Game\":\"XY\",\"Generation\":6,\"ProgressiveApp\":{\"Gen6MintItems\":{\"Include\":true,\"Unknown\":[1]}}}";
        check(Gen6MintExecutionContract.Read(optIn),"Gen6 mint syntactically valid opt-in can be read without implying admission");
        check(Failure(()=>Gen6MintExecutionContract.ValidateForExecution(optIn))==Gen6MintExecutionContract.BlockedReason,
            "Gen6 mint execution valid XY opt-in blocked until native accreditation");
        foreach(string json in new[]{"{", "[]", "{\"ProgressiveApp\":7}","{\"ProgressiveApp\":{\"Gen6MintItems\":null}}",
            "{\"ProgressiveApp\":{\"Gen6MintItems\":{}}}","{\"ProgressiveApp\":{\"Gen6MintItems\":{\"Include\":1}}}",
            "{\"ProgressiveApp\":{\"Gen6MintItems\":{\"Include\":\"false\"}}}",
            "{\"ProgressiveApp\":{\"Gen6MintItems\":{\"Include\":true,\"Include\":false}}}",
            "{\"ProgressiveApp\":{\"Gen6MintItems\":{\"Include\":true},\"Gen6MintItems\":{\"Include\":false}}}",
            "{\"ProgressiveApp\":{\"Gen6MintItems\":{\"Include\":true}},\"ProgressiveApp\":{}}"})
            reject(()=>Gen6MintExecutionContract.ValidateForExecution(json),"Gen6 mint execution rejects malformed or ambiguous input "+json);
        foreach(var family in new[]{("ORAS","6"),("USUM","7"),("XY","7"),("ANY","6"),("XY","\"6\""),("XY","6.5")})
            reject(()=>Gen6MintExecutionContract.Read("{\"Game\":\""+family.Item1+"\",\"Generation\":"+family.Item2+",\"ProgressiveApp\":{\"Gen6MintItems\":{\"Include\":true}}}"),
                "Gen6 mint execution rejects foreign or malformed family "+family);
        using var fixture=new Fixture(GameVersion.XY);
        var document=GlobalTemplateDocument.Empty("XY",6);
        document.SetSection("RandSettings",new JsonArray("synthetic unchanged setting"));
        document.SetSection("Actions",new JsonArray(new JsonObject{{"Id","rng.seed"},{"Parameters",new JsonObject()}}));
        document.SetSection("ProgressiveApp",new JsonObject{{"Future",new JsonArray("opaque",41)}});
        string templatePath=Path.Combine(fixture.Root,"gen6-mint-recipe.json"),output=fixture.Root+"-not-created";
        var options=new RomBatchOptions(templatePath,output,"MintGuard",1,false,false,false);
        var batch=new BatchRomExecution(fixture.Session);
        File.WriteAllText(templatePath,document.ToJson());
        NativeBatchGen6Bridge.ValidateDocument(document);
        var legacy=LegacyValidator();legacy(document.ToJson());
        var without=batch.Preflight(options);
        Gen6MintItemPolicy.Capture(document,false); // Absent opt-out deliberately stays absent.
        document.SetSection("ProgressiveApp",new JsonObject{{"Future",new JsonArray("opaque",41)},
            {Gen6MintItemPolicy.Node,new JsonObject{{"Include",false},{"Future",9}}}});
        NativeBatchGen6Bridge.ValidateDocument(document);legacy(document.ToJson());
        File.WriteAllText(templatePath,document.ToJson());var withFalse=batch.Preflight(options);
        check(without.Warnings.SequenceEqual(withFalse.Warnings)&&withFalse.Template.Actions.SequenceEqual(without.Template.Actions),
            "Gen6 mint batch explicit false and legacy absence preserve preflight results");
        Gen6MintItemPolicy.Capture(document,true);File.WriteAllText(templatePath,document.ToJson());
        var files=Directory.GetFiles(fixture.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        int history=fixture.Session.History.Read().Entries.Count;
        string beforeJson=document.ToJson();
        check(Failure(()=>NativeBatchGen6Bridge.ValidateDocument(document))==Gen6MintExecutionContract.BlockedReason,
            "Gen6 mint native Gen6 bridge rejects opt-in at document preflight");
        check(Failure(()=>batch.Preflight(options))==Gen6MintExecutionContract.BlockedReason,
            "Gen6 mint batch source preflight rejects opt-in before execution");
        check(Failure(()=>legacy(document.ToJson()))==Gen6MintExecutionContract.BlockedReason,
            "Gen6 mint legacy runner rejects opt-in without WPF bridge or resource access");
        check(Failure(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(document))==Gen6MintExecutionContract.BlockedReason,
            "Gen6 mint Global replay rejects opt-in before editor validation");
        var foreign=GlobalTemplateDocument.Empty("USUM",7);
        foreign.SetSection("ProgressiveApp",new JsonObject{{Gen6MintItemPolicy.Node,new JsonObject{{"Include",true}}}});
        reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(foreign),"Gen6 mint Global foreign family cannot bypass execution guard");
        reject(()=>legacy(foreign.ToJson()),"Gen6 mint legacy foreign family cannot bypass execution guard");
        check(document.ToJson()==beforeJson&&document.Section("ProgressiveApp")?["Future"]?.ToJsonString()=="[\"opaque\",41]",
            "Gen6 mint rejected execution preserves unknown template fields");
        check(!Directory.Exists(output)&&fixture.Session.History.Read().Entries.Count==history&&
            Directory.GetFiles(fixture.Root,"*",SearchOption.AllDirectories).Length==files.Count&&files.All(p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key)))==p.Value),
            "Gen6 mint rejected source and legacy preflight preserve all files history and output absence");
    }
    private static string Failure(Action action)
    {
        try { action(); }catch(InvalidDataException ex){return ex.Message;}
        throw new Exception("Expected explicit native-contract rejection.");
    }
    private static Action<string> LegacyValidator()
    {
        var assembly=Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));
        var method=assembly.GetType("pk3DS.WinForms.BatchGen6ActionExecutor",true)!.GetMethod("ValidateMintDependencies",BindingFlags.NonPublic|BindingFlags.Static)!;
        var templateType=method.GetParameters()[0].ParameterType;
        return json=>
        {
            var template=JsonSerializer.Deserialize(json,templateType)!;
            try { method.Invoke(null,[template]); }
            catch(TargetInvocationException ex)when(ex.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        };
    }
}
