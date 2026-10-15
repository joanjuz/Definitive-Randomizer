using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class EvolutionParityChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            string path=ModuleChecks.PathFor(f,"evolution");byte[] original=File.ReadAllBytes(path);
            var originalRecords=new GARC.MemGARC(original).Files;
            for(int method=5;method<=7;method++)BitConverter.GetBytes((ushort)method).CopyTo(originalRecords[method-4],0);
            f.WriteGarc(path,originalRecords,f.Game.GetGARCReference("evolution"));original=File.ReadAllBytes(path);
            foreach(bool filtered in new[]{false,true})
            foreach(int operation in new[]{1,2,3})
            {
                var service=new GameEvolutionService(f.Session);var options=EditorValues.Flatten(service.RandomOptions);options["Operation"]=operation;
                options["OriginalBST"]=options["Experience"]=options["Type"]=filtered?1:0;
                var preview=service.Preview(options,[1,2,3]);
                var expected=originalRecords.Select(bytes=>f.Game.Generation==6?(EvolutionSet)new EvolutionSet6(bytes):new EvolutionSet7(bytes)).ToArray();
                var engine=new EvolutionRandomizer(f.Game,expected);
                engine.Randomizer.L=true;engine.Randomizer.E=true;engine.Randomizer.rBST=filtered;engine.Randomizer.rEXP=filtered;engine.Randomizer.rType=filtered;
                Util.ReseedRand(preview.Seed);engine.Randomizer.Initialize();
                if(operation==1)engine.Execute();else if(operation==2){engine.ExecuteEvolveEveryLevel();engine.Execute();}else engine.ExecuteTrade();
                var result=new GARC.MemGARC(preview.Files.Single().Proposed()).Files;
                check(new[]{1,2,3}.All(i=>result[i].SequenceEqual(expected[i].Write())),version+" original evolution operation byte parity "+operation);
                check(Enumerable.Range(0,result.Length).Except([1,2,3]).All(i=>result[i].SequenceEqual(originalRecords[i])),version+" evolution operation preserves unselected records "+operation);
                check(File.ReadAllBytes(path).SequenceEqual(original),version+" evolution preview is read-only "+operation);
                if(operation==3)
                {
                    var trade=new EvolutionSet6(f.Game.Generation==6?result[1]:new byte[48]);
                    check(f.Game.Generation==6?trade.PossibleEvolutions[0].Argument==30:new EvolutionSet7(result[1]).PossibleEvolutions[0].Level==30,version+" vanilla plain trade becomes level thirty");
                    check(BitConverter.ToUInt16(result[2],0)==19&&BitConverter.ToUInt16(result[3],0)==22,version+" original trade conversion preserves held-item and party methods");
                }
                var doc=GlobalTemplateDocument.Empty(version.ToString(),f.Game.Generation);
                GlobalEditorTemplateBridge.Capture(doc,service,new(options,0),f.Game.Generation);
                doc.SetSection("Actions",new JsonArray(new JsonObject{["Id"]=preview.Parameters["ActionId"],["Parameters"]=new JsonObject()}));
                GlobalEditorTemplateBridge.ValidateBatchReplay(doc);check(true,version+" Batch accepts matching original evolution action "+operation);
                GlobalEditorTemplateBridge.Capture(doc,service,new(options,1),f.Game.Generation);
                reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(doc),version+" Batch rejects partial evolution scope "+operation);
            }
            var legacy=GlobalTemplateDocument.Empty(version.ToString(),f.Game.Generation);
            string form=f.Game.Generation==6?"EvolutionEditor6":"EvolutionEditor7";
            legacy.SetSection("RandSettings",new JsonArray(form,"CHK_BST\tTrue","CHK_Exp\tTrue","CHK_Type\tFalse","CHK_L\tFalse","CHK_E\tTrue","FutureControl\t18",""));
            legacy.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="evolutions.every-level",["Parameters"]=new JsonObject()}));
            var editor=new GameEvolutionService(f.Session);var state=GlobalEditorTemplateBridge.Restore(legacy,editor,f.Game.Generation)!;
            check(state.Options["Operation"]==2&&state.Options["OriginalBST"]==1&&state.Options["Experience"]==1&&state.Options["Legends"]==0,version+" Global imports original evolution filters and action");
            GlobalEditorTemplateBridge.Capture(legacy,editor,state,f.Game.Generation);
            check(legacy.Section("RandSettings")!.ToJsonString().Contains("FutureControl"),version+" Global preserves future evolution controls");
            var mismatch=legacy.Clone();mismatch.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="evolutions.normalize",["Parameters"]=new JsonObject()}));
            reject(()=>GlobalEditorTemplateBridge.ValidateBatchReplay(mismatch),version+" Batch rejects incompatible evolution action instead of ignoring native mode");
        }
    }
}
