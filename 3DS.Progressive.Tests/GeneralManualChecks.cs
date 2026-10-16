using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class GeneralManualChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var state=new TestState();
        var implemented=typeof(IProjectEditor).Assembly.GetTypes().Where(t=>typeof(IProjectEditor).IsAssignableFrom(t)&&!t.IsAbstract&&!t.IsInterface).Select(t=>t.Name).ToHashSet();
        check(implemented.SetEquals(ManualOperations.RecordEditors),"every concrete record editor is registered for manual replay");
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var source=new Fixture(version);ModuleChecks.CreateArchives(source);
            using var target=new Fixture(version);ModuleChecks.CreateArchives(target);
            var trainers=new GameTrainerService(source.Session);var values=EditorValues.Flatten(trainers.Read(1));values["Count"]=1;values["P0.Species"]=25;values["P0.Move0"]=85;values["P0.Move1"]=33;values["P0.Level"]=19;
            if(version is GameVersion.XY or GameVersion.ORAS){values["HasMoves"]=1;values["HasItems"]=1;}
            trainers.Save(1,values);
            values=EditorValues.Flatten(trainers.Read(1));values["AI"]=44;trainers.Save(1,values);
            var moves=new GameMoveService(source.Session);moves.Save(7,moves.Read(7) with{Power=122});
            var learn=new GameLevelUpService(source.Session);learn.Save(1,[new(){Level=5,Move=33},new(){Level=15,Move=85}]);
            var items=new GameItemService(source.Session);var item=EditorValues.Flatten(items.Read(2));string key=item.Keys.First();item[key]=items.Read(2).SelectMany(g=>g.Fields).First(f=>f.Key==key).Min;items.Save(2,item);
            var exactRecords=new List<(string Editor,string Title,int Id,string Field,int Value)>();
            void EditRecord(IProjectEditor service,int id,string field,int value)
            {
                var data=EditorValues.Flatten(service.Read(id));data[field]=value;service.Save(id,data);exactRecords.Add((service.GetType().Name,service.Title,id,field,value));
            }
            var evolution=new GameEvolutionService(source.Session);string evoField=EditorValues.Flatten(evolution.Read(1)).Keys.First(k=>k.EndsWith("Species",StringComparison.Ordinal));EditRecord(evolution,1,evoField,25);
            var encounters=new GameEncounterService(source.Session);int area=encounters.Entries[0].Id;string speciesField=EditorValues.Flatten(encounters.Read(area)).Keys.First(k=>k.EndsWith("Species",StringComparison.Ordinal));EditRecord(encounters,area,speciesField,25);
            foreach(var fixture in new[]{source,target})
            {
                byte[] egg=fixture.Game.Generation==7?new EggMoves7(new byte[4]){Moves=[33,85]}.Write():new EggMoves6(new byte[2]){Moves=[33,85]}.Write();
                fixture.WriteGarc(ModuleChecks.PathFor(fixture,"eggmove"),Enumerable.Range(0,840).Select(_=>(byte[])egg.Clone()).ToArray(),fixture.Game.GetGARCReference("eggmove"));
                foreach(string suffix in new[]{"N","S"})
                {
                    fixture.WriteGarc(ModuleChecks.PathFor(fixture,"maisontr"+suffix),[new byte[]{1,0,2,0,2,0,1,0},new byte[]{2,0,0,0}],fixture.Game.GetGARCReference("maisontr"+suffix));
                    var pokemon=Enumerable.Range(0,3).Select(i=>new byte[]{(byte)(i+1),0,1,0,2,0,3,0,4,0,0,0,0,0,0,0}).ToArray();fixture.WriteGarc(ModuleChecks.PathFor(fixture,"maisonpk"+suffix),pokemon,fixture.Game.GetGARCReference("maisonpk"+suffix));
                }
            }
            EditRecord(new GameEggMoveService(source.Session),1,"Move0",52);
            foreach(bool alternate in new[]{false,true})EditRecord(new GameBattleFacilityService(source.Session,alternate),2,"Species",25);
            if(source.Game.Generation==7)
            {
                var statics=new GameStaticEncounterService(source.Session);EditRecord(statics,statics.Entries[0].Id,"Species",25);
                var fieldItems=new GameFieldItemsService(source.Session);EditRecord(fieldItems,fieldItems.Entries[0].Id,"Item",25);
            }
            byte[] personalBefore=File.ReadAllBytes(source.PersonalPath);var changedPersonal=source.PersonalRecords;changedPersonal[1][0x28]^=1;GarcTransaction.RebuildPersonalMaster(changedPersonal);byte[] personalAfter=GarcTransaction.Build(personalBefore,source.Game.GetGARCReference("personal"),changedPersonal);
            using(ManualOperationScope.Flags(personalBefore,personalAfter,"flags-test"))source.Session.History.Commit([new FileChange(source.PersonalPath,personalBefore,personalAfter)],"MT y tutores","TM Sanity",[],new Dictionary<string,string>{{"Scope","All"}});source.Session.RefreshPersonalAfterExternalEdit();
            var randomFlags=target.PersonalRecords;randomFlags[1][0x28]^=0x80;int preservedFlag=randomFlags[1][0x28]&0x80;GarcTransaction.RebuildPersonalMaster(randomFlags);target.WriteGarc(target.PersonalPath,randomFlags,target.Game.GetGARCReference("personal"));target.Session.RefreshPersonalAfterExternalEdit();
            var doc=GlobalTemplateDocument.Empty(version.ToString(),source.Game.Generation);doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});doc.MergeHistoryActions(source.Session.History);
            check(doc.Actions.SequenceEqual(new[]{ManualEditTemplate.ActionId}),version+" trainer, moves, items and learnset are modifications rather than randomization actions");
            check(ManualOperations.Read(doc).Length>=3,version+" commands captured in original verified history");
            check(NativeRecipeCompletion.TryComplete(source.Session,doc),version+" general manual-only recipe is complete");
            var before=File.ReadAllBytes(target.PersonalPath);ManualEditTemplate.ValidateSession(target.Session,doc);check(before.SequenceEqual(File.ReadAllBytes(target.PersonalPath)),version+" general preflight never writes source");
            var targetTrainers=new GameTrainerService(target.Session);var other=EditorValues.Flatten(targetTrainers.Read(1));other["Count"]=3;other["Class"]=33;other["P0.Species"]=400;other["P0.Move0"]=300;targetTrainers.Save(1,other);
            var targetMoves=new GameMoveService(target.Session);targetMoves.Save(7,targetMoves.Read(7) with{Type=3});
            byte[] trainer2=ModuleChecks.Read(target,"trpoke")[2];
            ManualEditTemplate.Apply(target.Session,doc);
            check((target.PersonalRecords[1][0x28]&1)==(changedPersonal[1][0x28]&1)&&(target.PersonalRecords[1][0x28]&0x80)==preservedFlag,version+" exact compatibility bits preserve unrelated randomized flags");
            var applied=EditorValues.Flatten(new GameTrainerService(target.Session).Read(1));
            check(applied["Count"]==1&&applied["P0.Species"]==25&&applied["P0.Move0"]==85&&applied["P0.Move1"]==33&&applied["P0.Level"]==19,version+" explicit trainer team species and moves replace previously randomized team");
            check(applied["AI"]==44&&applied["Class"]==33,version+" repeated manual edits accumulate while unedited trainer fields remain randomized");
            check(new GameMoveService(target.Session).Read(7).Type==3,version+" unedited move properties remain randomized");
            check(ModuleChecks.Read(target,"trpoke")[2].SequenceEqual(trainer2),version+" unrelated trainer stays randomized or untouched");
            check(new GameMoveService(target.Session).Read(7).Power==122,version+" exact move data replayed");
            foreach(var expected in exactRecords)check(EditorValues.Flatten(ManualOperations.Editor(target.Session,expected.Editor,expected.Title).Read(expected.Id))[expected.Field]==expected.Value,version+" exact record replay: "+expected.Title);
            var set=new GameLevelUpService(target.Session).Read(1);check(set.Count==2&&set[1].Move==85&&set[1].Level==15,version+" learnset structure and ordering replayed");
            ManualEditTemplate.Apply(target.Session,doc);check(EditorValues.Flatten(new GameTrainerService(target.Session).Read(1))["P0.Species"]==25,version+" general commands are idempotent");
            var invalid=doc.Clone();var n=invalid.Section("ProgressiveApp")!;n["ManualEdits"]!["Operations"]![0]!["Payload"]!["Editor"]="System.IO.File";invalid.SetSection("ProgressiveApp",n);reject(()=>ManualEditTemplate.Validate(invalid),version+" arbitrary class invocation rejected");
            string raw=Path.Combine(source.Game.RomFS,"manual-resource.bin"),rawTarget=Path.Combine(target.Game.RomFS,"manual-resource.bin");byte[] a=new byte[32],b=new byte[32];b[4]=7;File.WriteAllBytes(raw,a);File.WriteAllBytes(rawTarget,a);
            source.Session.History.Commit([new FileChange(raw,a,b)],"Herramientas","Editar recurso",[new(0,"Recurso","Valor","0","7")]);
            string garcPath=Path.Combine(source.Game.RomFS,"manual-images.garc"),garcTarget=Path.Combine(target.Game.RomFS,"manual-images.garc");
            var reference=source.Game.GetGARCReference("personal");source.WriteGarc(garcPath,[new byte[]{1,2,3,4},new byte[]{5,6,7,8}],reference);target.WriteGarc(garcTarget,[new byte[]{1,2,3,4},new byte[]{8,8,8,8}],reference);
            var original=File.ReadAllBytes(garcPath);var image=new GARC.LazyGARC(original);image[0]=new byte[]{9,9,9,9,9,9};source.Session.History.Commit([new FileChange(garcPath,original,image.Save())],"Imágenes","Reemplazar imágenes",[new(0,"Imagen","Reemplazo","Original","Propia")]);
            byte[] second=(byte[])b.Clone();second[4]=9;second[8]=11;source.Session.History.Commit([new FileChange(raw,b,second)],"Herramientas","Editar recurso",[new(0,"Recurso","Valor","7","9")]);
            string sourceCode=Path.Combine(source.Game.ExeFS,"code.bin"),targetCode=Path.Combine(target.Game.ExeFS,".code.bin");byte[] codeBefore=new byte[32],codeAfter=new byte[32];codeAfter[4]=77;File.WriteAllBytes(sourceCode,codeBefore);File.WriteAllBytes(targetCode,codeBefore);source.Session.History.Commit([new FileChange(sourceCode,codeBefore,codeAfter)],"Herramientas","Editar recurso",[new(0,"code","Dato","0","77")]);
            doc.MergeHistoryActions(source.Session.History);string saved=Path.Combine(source.Root,"manual.json");doc.Save(saved);doc=GlobalTemplateDocument.Load(saved,version.ToString(),source.Game.Generation);
            check(Directory.Exists(Path.Combine(source.Root,"manual.assets")),version+" template exports verified resource dependencies beside JSON");
            var targetBytes=File.ReadAllBytes(rawTarget);targetBytes[20]=42;File.WriteAllBytes(rawTarget,targetBytes);ManualEditTemplate.Apply(target.Session,doc);
            check(File.ReadAllBytes(rawTarget)[4]==9&&File.ReadAllBytes(rawTarget)[8]==11&&File.ReadAllBytes(rawTarget)[20]==42,version+" resource delta preserves unrelated changes");
            check(File.ReadAllBytes(targetCode)[4]==77&&!File.Exists(Path.Combine(target.Game.ExeFS,"code.bin")),version+" Batch code.bin normalization is respected without recreating a second executable file");
            var exportedAgain=GlobalTemplateDocument.Empty(version.ToString(),source.Game.Generation);exportedAgain.MergeHistoryActions(target.Session.History);string again=Path.Combine(target.Root,"again.json");exportedAgain.Save(again);check(Directory.Exists(Path.Combine(target.Root,"again.assets")),version+" resource histories from editable outputs can export their dependencies again");
            var result=new GARC.LazyGARC(File.ReadAllBytes(garcTarget));check(result[0].SequenceEqual(new byte[]{9,9,9,9,9,9})&&result[1].SequenceEqual(new byte[]{8,8,8,8}),version+" resource replacement preserves other GARC members");
            targetBytes=File.ReadAllBytes(rawTarget);targetBytes[4]=99;File.WriteAllBytes(rawTarget,targetBytes);reject(()=>ManualEditTemplate.ValidateSession(target.Session,doc),version+" unknown resource layout conflicts are explicit");
            reject(()=>ManualResourceReplay.SafePath(target.Root,"../outside"),version+" replay cannot escape project root");
            targetBytes[4]=9;File.WriteAllBytes(rawTarget,targetBytes);
            var old=new HistoryEntry(Guid.NewGuid(),DateTimeOffset.UtcNow,"Entrenadores","Editar Entrenadores",[new(1,"Entrenador","Especie","1","25")],new Dictionary<string,string>(),[new("RomFS/unknown","A","B")]);
            var legacy=GlobalTemplateDocument.Empty(version.ToString(),source.Game.Generation);ManualEditTemplate.Capture(legacy,new HistoryDocument(2,version.ToString(),"legacy",[old]));reject(()=>ManualEditTemplate.Validate(legacy),version+" old incomplete histories cannot silently omit a manual change");
            string asset=Directory.GetFiles(Path.Combine(source.Root,"manual.assets")).Single();File.AppendAllText(asset,"tamper");reject(()=>ManualEditTemplate.ValidateSession(target.Session,doc),version+" modified dependency rejected");
        }
    }
}
