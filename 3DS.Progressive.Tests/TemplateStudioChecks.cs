using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class TemplateStudioChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var state=new TestState();
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var source=new Fixture(version);ModuleChecks.CreateArchives(source);using var target=new Fixture(version);ModuleChecks.CreateArchives(target);
            Description(check,version);
            var p=source.Session;var moves=AuthoredTemplate.Create(p,"Moves");check(moves.Rows.Count==0,version+" authoring begins with an empty move table");
            moves.Rows.Add(new(){["Move"]="7",["Type"]="3",["Category"]="1",["Power"]="121",["BattlePatch"]=""});
            byte[] before=File.ReadAllBytes(source.MovePath);int count=moves.Run(p,31,true);check(count>0&&before.SequenceEqual(File.ReadAllBytes(source.MovePath)),version+" move template preview is read-only");
            string csv=Path.Combine(source.Root,"move.csv");moves.Export(csv);moves=AuthoredTemplate.Import(p,"Moves",csv);check(moves.Rows[0]["Power"]=="121"&&moves.Rows[0]["BattlePatch"]=="",version+" move CSV import preserves optional battle choice");
            var parameterOnly=AuthoredTemplate.Create(p,"Moves");parameterOnly.Rows.Add(new(){["Move"]="300",["Param0x0B"]="17",["BattlePatch"]=""});check(parameterOnly.Run(p,1,true)>0,version+" None does not implicitly request battle patches for data parameters");
            if(version!=GameVersion.SM){var progressiveMoves=AuthoredTemplate.Progressive(p,"Moves");check(progressiveMoves.Rows.Count>10,version+" built-in Progressive move CSV imports into editable rows");check(progressiveMoves.Rows.All(row=>row.GetValueOrDefault("Category","")==""||int.TryParse(row["Category"],out _)),version+" imported move dropdown tokens are normalized to IDs");}
            var payload=AuthoredTemplate.Store(p,moves);payload["Seed"]=31;moves.Run(p,31,false,payload);p.History.RecordTemplateUse(payload);check(new GameMoveService(p).Read(7).Power==121,version+" authored move is applied");
            check(p.History.Read().Entries.All(e=>e.Parameters.ContainsKey("ManualOperation")),version+" template application records its file rather than random settings");
            var stats=AuthoredTemplate.Create(p,"Stats");stats.Rows.Add(new(){["Entry"]="1",["ATK"]="139"});var statAsset=AuthoredTemplate.Store(p,stats);stats.Run(p,1,false,statAsset);p.History.RecordTemplateUse(statAsset);
            check(p.ReadPersonal(1).Attack==139,version+" authored stat changes its exact field");stats.Run(p,1,false,statAsset);p.History.RecordTemplateUse(statAsset);
            check(p.History.Read().Entries.Last().Action.StartsWith("Usar template"),version+" an unchanged template use remains reproducible in history");
            var evo=AuthoredTemplate.Create(p,"Evolutions");evo.Rows.Add(new(){["Source"]="1",["Target"]="25",["Method"]="4",["Level"]="22",["Argument"]="0"});var evoAsset=AuthoredTemplate.Store(p,evo);evo.Run(p,1,false,evoAsset);p.History.RecordTemplateUse(evoAsset);
            var evoValues=EditorValues.Flatten(new GameEvolutionService(p).Read(1));check(evoValues["E1.Species"]==25&&evoValues[p.Generation==6?"E1.Argument":"E1.Level"]==22,version+" evolution template adds route without removing existing route");
            check(evoValues["E0.Species"]==2,version+" evolution template retains unrelated route");
            string evoCsv=Path.Combine(source.Root,"evo.csv");evo.Export(evoCsv);check(AuthoredTemplate.Import(p,"Evolutions",evoCsv).Rows.Count==1,version+" evolution CSV roundtrip");
            string statsCsv=Path.Combine(source.Root,"stats.csv");stats.Export(statsCsv);check(AuthoredTemplate.Import(p,"Stats",statsCsv).Rows[0]["ATK"]=="139",version+" stats CSV roundtrip");
            p.SavePersonal(1,p.ReadPersonal(1) with{Attack=157});
            var doc=GlobalTemplateDocument.Empty(version.ToString(),p.Generation);doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});doc.MergeHistoryActions(p.History);
            check(doc.Actions.SequenceEqual(new[]{ManualEditTemplate.ActionId})&&ManualOperations.Read(doc).Length==4,version+" authored templates coalesce into final-phase replay");
            check(NativeRecipeCompletion.TryComplete(p,doc),version+" authored history completes as a standalone native Batch recipe");
            string global=Path.Combine(source.Root,"portable","recipe.json");Directory.CreateDirectory(Path.GetDirectoryName(global)!);doc.Save(global);doc=GlobalTemplateDocument.Load(global,version.ToString(),p.Generation);
            check(ManualOperations.Read(doc).Where(o=>o["Kind"]!.GetValue<string>()=="authored-template").All(o=>!Path.IsPathRooted(o["Payload"]!["Asset"]!["Directory"]!.GetValue<string>())),version+" global template exports relative dependencies");
            foreach(var file in Directory.GetFiles(Path.Combine(p.History.StorageDirectory,"authored-templates")))File.Delete(file);
            target.Session.SavePersonal(1,target.Session.ReadPersonal(1) with{Attack=45,Defense=199});var targetMoves=new GameMoveService(target.Session);targetMoves.Save(7,targetMoves.Read(7) with{Power=44,PP=27});
            ManualEditTemplate.ValidateSession(target.Session,doc);check(target.Session.ReadPersonal(1).Attack==45,version+" preflight validates portable authored files without writes");ManualEditTemplate.Apply(target.Session,doc);
            check(target.Session.ReadPersonal(1).Attack==157&&target.Session.ReadPersonal(1).Defense==199&&new GameMoveService(target.Session).Read(7).Power==121&&new GameMoveService(target.Session).Read(7).PP==27,version+" final authored replay overrides selected fields after randomization only");
            var roundtrip=GlobalTemplateDocument.Empty(version.ToString(),p.Generation);roundtrip.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});roundtrip.MergeHistoryActions(target.Session.History);string next=Path.Combine(source.Root,"portable","next.json");roundtrip.Save(next);check(ManualOperations.Read(roundtrip).Count(o=>o["Kind"]!.GetValue<string>()=="exact-overrides")==1,version+" final field commands coalesce after reexport");check(File.Exists(next),version+" replayed authored dependencies reexport from editable project");
            var bad=AuthoredTemplate.Create(p,"Moves");bad.Rows.Add(new(){["Move"]="7",["BattlePatch"]="Gen7KingShieldMinusOne"});reject(()=>bad.Run(p,1,true),version+" incompatible battle patch cannot apply to another move");
            check(BattlePatchCatalogue.For(p,7).Count==1&&BattlePatchCatalogue.For(p,7)[0].Value=="",version+" ordinary move offers only None");
            check((BattlePatchCatalogue.For(p,588).Count>1)==(p.Generation==6||version==GameVersion.USUM),version+" battle choices limited to compatible family");
            check(BattlePatchCatalogue.For(p,588).All(c=>c.Description.Length>20),version+" battle choices have explanatory hover descriptions");
            if(p.Generation==7)
            {
                var field=AuthoredTemplate.Create(p,"FieldItems");field.AllowedItems=[17];field.BannedItems=[18];byte[] fieldBefore=File.ReadAllBytes(ModuleChecks.PathFor(source,"encdata"));field.Run(p,17,true);check(fieldBefore.SequenceEqual(File.ReadAllBytes(ModuleChecks.PathFor(source,"encdata"))),version+" field preview is read-only");field.Run(p,17,false);var fields=new GameFieldItemsService(p);check(fields.Entries.All(e=>EditorValues.Flatten(fields.Read(e.Id))["Item"]==17),version+" field catalog excludes banned items");string path=Path.Combine(source.Root,"field.json");field.Export(path);check(AuthoredTemplate.Import(p,"FieldItems",path).BannedItems.SequenceEqual(new[]{18}),version+" native field template exports and imports");
                var progressive=AuthoredTemplate.Progressive(p,"FieldItems");check(progressive.AllowedItems.Length>100&&progressive.BannedItems.Length>0&&!progressive.AllowedItems.Intersect(progressive.BannedItems).Any(),version+" Progressive item catalog exposes allowed and banned IDs");
            }
            else
            {
                Field6Fixture(source);var field=AuthoredTemplate.Create(p,"FieldItems");field.AllowedItems=[17];field.BannedItems=[18];field.Run(p,5,false);var items6=new GameFieldItems6Service(p);check(items6.Entries.All(e=>EditorValues.Flatten(items6.Read(e.Id))["Item"]==17),version+" authored Gen6 field template honors custom bans and allowed pool");
                Mart6Checks.Setup(source);var shops=AuthoredTemplate.Create(p,"Shops");shops.ShopMode="Replace";shops.Options=AuthoredTemplate.LegacyShopOptions(p);shops.Options["UseChecks"]=1;shops.Options["Operation"]=0;shops.Options["Randomize"]=1;shops.Options["SpecialOnly"]=0;shops.Options["KeepX"]=0;shops.AllowedItems=[23];shops.BannedItems=[24];shops.Rows.Add(new(){["Shop"]="1",["Slot"]="1",["Item"]="22",["Price"]="0"});
                // Preserve the current length in a fixed-size fixture; real expansion is checked by Mart6Checks.
                int slots=new GameMart6Service(p).ReadAllItems()[0].Length;for(int i=2;i<=slots;i++)shops.Rows.Add(new(){["Shop"]="1",["Slot"]=i.ToString(),["Item"]="22",["Price"]="0"});
                shops.Run(p,7,false);var actual=new GameMart6Service(p).ReadAllItems();check(actual[0].All(i=>i==22)&&actual[1].All(i=>i==23),version+" exact shop inventory wins after preset randomization with custom pool");
                string path=Path.Combine(source.Root,"shops.json");shops.Export(path);check(AuthoredTemplate.Import(p,"Shops",path).Rows.Count==slots,version+" shop template JSON roundtrip");
            }
            string dependency=Path.Combine(Path.GetDirectoryName(global)!,ManualOperations.Read(doc)[0]["Payload"]!["Asset"]!["Directory"]!.GetValue<string>(),ManualOperations.Read(doc)[0]["Payload"]!["Asset"]!["Name"]!.GetValue<string>());File.AppendAllText(dependency,"tamper");reject(()=>ManualEditTemplate.ValidateSession(target.Session,doc),version+" modified authored dependency rejected by SHA256");
        }
    }
    private static void Description(Action<bool,string> check,GameVersion version)
    {
        using var f=new Fixture(version);var p=f.Session;var reference=f.Game.GetGARCReference("gametext").GetRelativeGARC(6);int index=f.Game.GameText.First(t=>t.Name==TextName.MoveFlavor).Index;
        byte[][] files=Enumerable.Range(0,index+1).Select(_=>TextFile.GetBytes(f.Game,["Sin cambios"])).ToArray();files[index]=TextFile.GetBytes(f.Game,Enumerable.Range(0,1000).Select(_=>"Descripción original").ToArray());string path=Path.Combine(f.Game.RomFS,reference.Reference);f.WriteGarc(path,files,reference);byte[] before=File.ReadAllBytes(path);
        var d=AuthoredTemplate.Create(p,"Moves");const string description="Texto, con \"comillas\"\nSegunda línea";d.Rows.Add(new(){["Move"]="7",["InGameDescription"]=description});string csv=Path.Combine(f.Root,"description.csv");d.Export(csv);d=AuthoredTemplate.Import(p,"Moves",csv);
        check(d.Rows[0]["InGameDescription"]==description,version+" multiline quoted move description survives CSV export and import");d.Run(p,1,true);check(File.ReadAllBytes(path).SequenceEqual(before),version+" description preview preserves Spanish source bank");var descriptionAsset=AuthoredTemplate.Store(p,d);d.Run(p,1,false,descriptionAsset);p.History.RecordTemplateUse(descriptionAsset);var data=new GARC.MemGARC(File.ReadAllBytes(path)).Files;var lines=new TextFile(f.Game,data[index],false).Lines;
        check(lines[7]==description.Replace("\n","\\n")&&lines[8]=="Descripción original",version+" authored description updates only the selected Spanish move line");check(data.Where((_,i)=>i!=index).SelectMany(b=>b).SequenceEqual(files.Where((_,i)=>i!=index).SelectMany(b=>b)),version+" description application preserves unrelated text members");
        f.Game.Language=6;var textEditor=new GameTextService(p);var edited=textEditor.Read(index).ToArray();edited[7]="Texto manual posterior";textEditor.Save(index,edited);
        var recipe=GlobalTemplateDocument.Empty(version.ToString(),p.Generation);recipe.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});recipe.MergeHistoryActions(p.History);
        using var target=new Fixture(version);target.WriteGarc(Path.Combine(target.Game.RomFS,reference.Reference),files,reference);ManualEditTemplate.ValidateSession(target.Session,recipe);ManualEditTemplate.Apply(target.Session,recipe);var final=new GARC.MemGARC(File.ReadAllBytes(Path.Combine(target.Game.RomFS,reference.Reference))).Files;
        check(new TextFile(target.Game,final[index],false).Lines[7]=="Texto manual posterior",version+" later manual text wins over authored description template and preflights against clean ROM");
        var service=new GameMoveService(p);var validate=typeof(GameMoveService).GetMethod("ValidateBattleRequest",BindingFlags.Instance|BindingFlags.NonPublic)!;int count=0;
        foreach(int id in new[]{54,96,108,171,273,277,300,346,381,587,588,693})foreach(var choice in BattlePatchCatalogue.For(p,id).Where(c=>c.Value!="")){validate.Invoke(service,[new MoveTemplates.MovePatchRow{Move=id,BattlePatch=choice.Value,Param0x0B=id is 300 or 346?0:null}]);count++;}
        check(count==(version==GameVersion.USUM?12:version==GameVersion.SM?0:1),version+" all displayed battle implementations are accepted by the real validator");
    }
    internal static void Field6Fixture(Fixture f)
    {
        byte[] Script(int offset)
        {
            byte[] instructions=new byte[0x1800];BitConverter.GetBytes((ushort)17).CopyTo(instructions,offset);BitConverter.GetBytes((ushort)18).CopyTo(instructions,offset+12);byte[] code=Gen6ScriptCodec.Encode(instructions);var raw=new byte[32+code.Length];BitConverter.GetBytes(raw.Length).CopyTo(raw,0);BitConverter.GetBytes(0x0A0AF1E0).CopyTo(raw,4);BitConverter.GetBytes(32).CopyTo(raw,12);BitConverter.GetBytes(32).CopyTo(raw,16);BitConverter.GetBytes(32+instructions.Length).CopyTo(raw,20);code.CopyTo(raw,32);return raw;
        }
        var records=Enumerable.Range(0,60).Select(_=>new byte[]{0xAA,0xBB}).ToArray();records[f.Game.ORAS?39:17]=Script(f.Game.ORAS?0xB64:0xB04);if(f.Game.ORAS)records[57]=Script(2746);else records[26]=Script(0xB18);
        var personal=f.Game.GetGARCReference("personal");var reference=personal.GetRelativeGARC((f.Game.ORAS?29:31)-personal.FileNumber,"scripts");f.WriteGarc(Path.Combine(f.Game.RomFS,reference.Reference),records,reference);
    }
    internal static void ShopCopies(Action<bool,string> check,string[] paths)
    {
        using var state=new TestState();
        foreach(string path in paths)
        {
            var version=path.Contains("Ultra",StringComparison.OrdinalIgnoreCase)?GameVersion.USUM:GameVersion.SM;
            byte[] original=File.ReadAllBytes(path);using var f=new Fixture(version);ModuleChecks.CreateArchives(f);File.WriteAllBytes(Path.Combine(f.Game.RomFS,"Shop.cro"),original);
            var progressive=AuthoredTemplate.Progressive(f.Session,"Shops");check(!progressive.BannedItems.Any(GameMartService.IsProtectedItem),version+" Progressive shop import preserves protected original item classes");check(progressive.AllowedItems.SequenceEqual(GameMartService.ProgressiveCatalog.Where(id=>id>0&&id<f.Session.ItemNames.Count).Distinct().Order()),version+" Progressive shop import uses the actual native shop pool");
            var d=AuthoredTemplate.Create(f.Session,"Shops");d.ShopMode="Replace";d.Options=AuthoredTemplate.LegacyShopOptions(f.Session);d.AllowedItems=[23];d.BannedItems=[24];d.Options["RandomRegular"]=1;d.Options["RandomBP"]=1;d.Options["ProtectX"]=0;
            var opened=new GameMartService(f.Session);int count=opened.RegularShops[0].Rows.Count;
            for(int i=1;i<=count;i++)d.Rows.Add(new(){["Shop"]="1",["Slot"]=i.ToString(),["Item"]="22"});
            var asset=AuthoredTemplate.Store(f.Session,d);d.Run(f.Session,99,true);check(File.ReadAllBytes(Path.Combine(f.Game.RomFS,"Shop.cro")).SequenceEqual(original),version+" copied real shop preview preserves all source bytes");d.Run(f.Session,99,false,asset);f.Session.History.RecordTemplateUse(asset);
            var result=new GameMartService(f.Session);check(result.RegularShops[0].Rows.All(row=>row.ItemId==22),version+" copied real shop fixed inventory overrides random preset");check(result.RegularShops[1].Rows.All(row=>row.ItemId==23),version+" copied real shop uses custom allowed catalog");check(result.RegularShops.Concat(result.BpShops).All(s=>s.Rows.All(row=>row.ItemId!=24)),version+" copied real shop bans items in normal and BP inventories");
            check(result.BpShops.SelectMany(s=>s.Rows).Select(row=>row.Price).SequenceEqual(opened.BpShops.SelectMany(s=>s.Rows).Select(row=>row.Price)),version+" copied real shop preserves BP prices unless explicitly changed");
            check(File.ReadAllBytes(path).SequenceEqual(original),version+" real source Shop.cro remains byte-identical");
            var doc=GlobalTemplateDocument.Empty(version.ToString(),f.Game.Generation);doc.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});doc.MergeHistoryActions(f.Session.History);string global=Path.Combine(f.Root,"shop-global.json");doc.Save(global);doc=GlobalTemplateDocument.Load(global,version.ToString(),f.Game.Generation);
            using var target=new Fixture(version);ModuleChecks.CreateArchives(target);File.WriteAllBytes(Path.Combine(target.Game.RomFS,"Shop.cro"),original);string? previous=Environment.GetEnvironmentVariable("DEFINITIVE_TEMPLATE_SEED");Environment.SetEnvironmentVariable("DEFINITIVE_TEMPLATE_SEED","99");try{ManualEditTemplate.Apply(target.Session,doc);}finally{Environment.SetEnvironmentVariable("DEFINITIVE_TEMPLATE_SEED",previous);}
            check(File.ReadAllBytes(Path.Combine(target.Game.RomFS,"Shop.cro")).SequenceEqual(File.ReadAllBytes(Path.Combine(f.Game.RomFS,"Shop.cro"))),version+" copied real shop global replay matches same-seed authored result");
        }
    }
    internal static void Ui(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var view=new ThreeDS.Progressive.TemplateStudioView(f.Session);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var grid=(System.Windows.Controls.DataGrid)view.GetType().GetField("_grid",flags)!.GetValue(view)!;var rows=(System.Collections.IList)view.GetType().GetField("_rows",flags)!.GetValue(view)!;
            check(rows.Count==0&&grid.Columns.Count>35,version+" WPF empty move table includes complete template fields");view.GetType().GetMethod("AddRow",flags)!.Invoke(view,null);var row=(ThreeDS.Progressive.TemplateStudioRow)rows[0]!;row["Move"]="7";
            var original=new GameMoveService(f.Session).Read(7);check(row["Type"]==original.Type.ToString()&&row["Category"]==original.Category.ToString()&&row["BattlePatch"]=="",version+" WPF selecting move fills type/category and keeps patch None");check(grid.Columns.Any(c=>c.Header.ToString()!.StartsWith("InGameDescription")),version+" WPF editable description column present");check(grid.Columns.Any(c=>c.Header.ToString()=="ZEffect")== (f.Game.Generation==7),version+" WPF Z fields only in Gen7");
            view.Measure(new System.Windows.Size(1400,850));view.Arrange(new System.Windows.Rect(0,0,1400,850));view.UpdateLayout();check(view.DesiredSize.Width>0,version+" template table layout succeeds without showing a desktop window");
        }
    }
}
