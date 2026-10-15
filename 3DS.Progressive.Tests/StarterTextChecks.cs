using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

internal static class StarterTextChecks
{
    internal static string CreateBanks(Fixture f,int language)
    {
        var names=f.Game.GameTextStrings.Select(row=>(string[])row.Clone()).ToArray();int species=f.Game.GameText.First(row=>row.Name==TextName.SpeciesNames).Index;
        names[species]=Enumerable.Range(0,1000).Select(i=>$"Species-{i:000}-L{language}").ToArray();
        var reference=f.Game.GetGARCReference("gametext").GetRelativeGARC(language);string np=Path.Combine(f.Game.RomFS,reference.Reference);f.WriteGarc(np,names.Select(row=>TextFile.GetBytes(f.Game,row)).ToArray(),reference);
        int index=f.Game.USUM?39:41;string[] lines=Enumerable.Range(0,20).Select(i=>"Unchanged "+i).ToArray();for(int i=0;i<3;i++){lines[i+1]=$"Prefix Species-{i+1:000}-L{language} Type1 suffix";lines[i+14]=$"Species-{i+1:000}-L{language}";}
        byte[][] records=Enumerable.Range(0,index+1).Select(_=>TextFile.GetBytes(f.Game,["Other bank"])).ToArray();records[index]=TextFile.GetBytes(f.Game,lines);reference=f.Game.GetGARCReference("storytext").GetRelativeGARC(language);string sp=Path.Combine(f.Game.RomFS,reference.Reference);f.WriteGarc(sp,records,reference);return sp;
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        check(GameStaticEncounterService.ReplaceStarterName("Mewtwo suffix",["","Mew","Mewtwo"],2,2,"Mew")=="Mew suffix","starter text recognizes a new species whose name is a prefix of the old name");
        check(GameStaticEncounterService.ReplaceStarterName("Mewtwo suffix",["","Mew","Mewtwo"],1,2,"Pikachu")=="Pikachu suffix","starter text repairs a mismatched longer species name without partial replacement");
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var records=ModuleChecks.Read(f,"encounterstatic");records[0]=Enumerable.Range(1,3).SelectMany(id=>{byte[] gift=(byte[])records[0].Clone();BitConverter.GetBytes((ushort)id).CopyTo(gift,0);return gift;}).ToArray();string path=ModuleChecks.PathFor(f,"encounterstatic");f.WriteGarc(path,records,f.Game.GetGARCReference("encounterstatic"));
            var paths=new[]{CreateBanks(f,0),CreateBanks(f,6)};var before=paths.ToDictionary(p=>p,p=>new GARC.MemGARC(File.ReadAllBytes(p)).Files);var editor=new GameStaticEncounterService(f.Session);check(editor.StarterTextLanguages==2,version+" detects available localized starter banks without changing active language");
            f.Game.Personal[4].Types=[4,4];var values=EditorValues.Flatten(editor.Read(1));values["Species"]=4;values["Form"]=0;editor.Save(1,values);int index=f.Game.USUM?39:41;
            foreach(string p in paths)
            {
                var after=new GARC.MemGARC(File.ReadAllBytes(p)).Files;var lines=new TextFile(f.Game,after[index]).Lines;
                check(lines[1].Contains("Species-004")&&lines[1].StartsWith("Prefix ")&&lines[1].EndsWith(" suffix"),version+" starter text changes name while preserving dialogue");
                check(f.Game.USUM?lines[14].Contains("Species-004"):lines[1].Contains("Type4"),version+" starter standalone name or primary type synchronized");
                check(before[p].Where((_,i)=>i!=index).Zip(after.Where((_,i)=>i!=index)).All(pair=>pair.First.SequenceEqual(pair.Second)),version+" starter sync preserves unrelated banks");
                var original=new TextFile(f.Game,before[p][index]).Lines;check(original.Where((_,i)=>i!=1&&(f.Game.SM||i!=14)).SequenceEqual(lines.Where((_,i)=>i!=1&&(f.Game.SM||i!=14))),version+" starter sync preserves unrelated lines");
            }
            values=EditorValues.Flatten(editor.Read(1));values["Species"]=5;editor.Save(1,values);check(paths.All(p=>new TextFile(f.Game,new GARC.MemGARC(File.ReadAllBytes(p)).Files[index]).Lines[1].Contains("Species-005")),version+" consecutive starter saves refresh all source fingerprints");
            var options=EditorValues.Flatten(editor.RandomOptions);options["Static"]=options["Trades"]=0;var preview=editor.Preview(options,[1,2,3]);byte[] stable=File.ReadAllBytes(path);string np=Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("gametext"));File.AppendAllText(np,"external");reject(()=>editor.Apply(preview),version+" naming bank modified after preview rejects full transaction");check(File.ReadAllBytes(path).SequenceEqual(stable),version+" rejected text transaction leaves encounter table unchanged");
        }
    }
}
