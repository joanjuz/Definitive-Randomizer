using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;

internal static class StoryRandomChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            string[][] lines=[new[]{"Uno","Dos","[VAR TRNAME(0000)]","Texto\\ncontinuación","","Tres","Cuatro"},new[]{"Cinco","Seis","Siete","Ocho"}];
            var records=lines.Select(row=>TextFile.GetBytes(f.Game,row,f.Game.RemapCharacters)).ToArray();
            string path=ModuleChecks.PathFor(f,"storytext");f.WriteGarc(path,records,f.Game.GetGARCReference("storytext"));
            int species=f.Game.GameText.First(t=>t.Name==TextName.SpeciesNames).Index;
            string gamePath=ModuleChecks.PathFor(f,"gametext");
            f.WriteGarc(gamePath,Enumerable.Range(0,species+1).Select(i=>TextFile.GetBytes(f.Game,new[]{i==species?"Pikachu":"Texto general"},f.Game.RemapCharacters)).ToArray(),f.Game.GetGARCReference("gametext"));
            var general=new GameTextService(f.Session);
            check(general.Entries[species].Name.Contains("Nombres de Pokémon")&&general.Search("PIKACHU").Single().FileId==species,version+" Game Text uses version-specific category and content location");
            var editor=new GameTextService(f.Session,story:true);lines=Enumerable.Range(0,2).Select(id=>editor.Read(id).ToArray()).ToArray();
            byte[] original=File.ReadAllBytes(path),gameText=File.ReadAllBytes(ModuleChecks.PathFor(f,"gametext"));
            var hits=editor.Search("cINco");
            check(hits.Count==1&&hits[0].FileId==1&&hits[0].Line==0&&hits[0].Text=="Cinco",version+" Story content search finds exact file and line ignoring case");
            check(editor.Search("o",1).Count==1&&editor.Search("   ").Count==0&&File.ReadAllBytes(path).SequenceEqual(original),version+" Story search is bounded and read-only");
            check(editor.Entries[0].Name.Contains("7 líneas")&&editor.Entries[0].Name.Contains("Sin clasificar"),version+" Story catalogue includes message count and explicit unclassified tag");
            var preview=Enumerable.Range(1,32).Select(seed=>editor.PreviewRandomize(0,seed)).First(p=>p.Changes.Count>0);
            check(File.ReadAllBytes(path).SequenceEqual(original)&&editor.PreviewRandomize(0,preview.Seed).File.Proposed().SequenceEqual(preview.File.Proposed()),version+" Story random preview is read-only and deterministic");
            editor.ApplyRandomize(preview);var after=editor.Read(0);
            check(editor.Entries[0].Name.Contains("7 líneas"),version+" Story catalogue refreshes after saving changes");
            check(after.Count==lines[0].Length&&new[]{2,3,4}.All(index=>after[index]==lines[0][index]),version+" Story random preserves variable control and empty slots at original indices");
            check(after.Where((_,index)=>index is not (2 or 3 or 4)).Order().SequenceEqual(lines[0].Where((_,index)=>index is not (2 or 3 or 4)).Order()),version+" Story random permutes only the original literal dialogue pool");
            check(new GARC.MemGARC(File.ReadAllBytes(path)).Files[1].SequenceEqual(records[1])&&File.ReadAllBytes(ModuleChecks.PathFor(f,"gametext")).SequenceEqual(gameText),version+" Story selected-file random preserves other bank records and Game Text bytes");
            reject(()=>editor.ApplyRandomize(preview),version+" Story stale random proposal rejected");
            var all=Enumerable.Range(1,32).Select(seed=>editor.PreviewRandomize(null,seed)).First(p=>p.Changes.Count>0);
            reject(()=>new GameTextService(f.Session,story:true).ApplyRandomize(all),version+" Story proposal cannot be applied by a different service owner");
            byte[] tampered=File.ReadAllBytes(path);tampered[^1]^=1;File.WriteAllBytes(path,tampered);
            reject(()=>editor.ApplyRandomize(all),version+" Story external source change blocks randomization commit");
            check(File.ReadAllBytes(path).SequenceEqual(tampered),version+" Story conflict preserves external bytes");
            reject(()=>new GameTextService(f.Session).PreviewRandomize(null,1),version+" Literal story randomizer cannot shuffle Game Text catalogs");
        }
    }
}
