using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class BattleFacilityChecks
{
    internal static void RunReal(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
            foreach(bool alternate in new[]{false,true})
            {
                string suffix=alternate?"S":"N";
                var paths=new[]{"maisontr"+suffix,"maisonpk"+suffix}.Select(key=>Path.Combine(project.GameConfiguration.RomFS,project.GameConfiguration.GetGARCFileName(key))).ToArray();
                var hashes=paths.Select(path=>System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToArray();
                var service=new GameBattleFacilityService(project,alternate);
                _=service.Read(service.Entries[0].Id);_=service.Read(service.Entries[^1].Id);
                if(paths.Where((path,i)=>!hashes[i].SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))).Any())throw new Exception("Lectura alteró archivos de instalación");
                Console.WriteLine($"PASS FACILITY READ ONLY: {root}; {service.Title}; {service.Entries.Count} entries; source SHA-256 preserved; no Save.");
            }
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.USUM})
        {
            using var f=new Fixture(version);
            foreach(string suffix in new[]{"N","S"})
            {
                string tr="maisontr"+suffix,pk="maisonpk"+suffix;
                f.WriteGarc(ModuleChecks.PathFor(f,tr),[new byte[4]],f.Game.GetGARCReference(tr));
                f.WriteGarc(ModuleChecks.PathFor(f,pk),[new byte[16]],f.Game.GetGARCReference(pk));
            }
            var main=new ThreeDS.Progressive.MainWindow();
            try
            {
                main.LoadProject(f.Session);
                foreach(string section in new[]{"FacilityNormal","FacilityAlternate"})
                {
                    typeof(ThreeDS.Progressive.MainWindow).GetMethod("NavigateSystem",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,[section]);
                    var view=((System.Windows.Controls.ContentControl)main.FindName("SectionHost")).Content as ThreeDS.Progressive.RecordEditorView;
                    check(view?.Editor is GameBattleFacilityService,version+" WPF routes facility editor "+section);
                    check(((System.Windows.FrameworkElement)view!.FindName("RandomizerButton")).Visibility==System.Windows.Visibility.Collapsed&&((System.Windows.FrameworkElement)view.FindName("FieldExportButton")).Visibility==System.Windows.Visibility.Visible,version+" WPF facility exposes export and hides unsupported randomizer");
                }
            }
            finally{main.Close();}
        }
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        foreach(bool alternate in new[]{false,true})
        {
            using var f=new Fixture(version);string suffix=alternate?"S":"N",tr="maisontr"+suffix,pk="maisonpk"+suffix;
            byte[][] trainers=[new byte[]{1,0,2,0,2,0,1,0,0xAA,0xBB},new byte[]{2,0,0,0}];
            byte[][] pokemon=Enumerable.Range(0,3).Select(i=>{byte[] b=new byte[18];b[0]=(byte)(i+1);b[2]=1;b[4]=2;b[6]=3;b[8]=4;b[10]=0xC1;b[11]=3;b[12]=5;b[14]=7;b[16]=0xAB;b[17]=0xCD;return b;}).ToArray();
            string tp=ModuleChecks.PathFor(f,tr),pp=ModuleChecks.PathFor(f,pk);f.WriteGarc(tp,trainers,f.Game.GetGARCReference(tr));f.WriteGarc(pp,pokemon,f.Game.GetGARCReference(pk));
            byte[] beforeT=File.ReadAllBytes(tp),beforeP=File.ReadAllBytes(pp);var service=new GameBattleFacilityService(f.Session,alternate);string tag=version+" "+service.Title+" ";
            check(service.Entries.Count==5&&EditorValues.Flatten(service.Read(0))["Choice0"]==2,tag+"reads original trainer and Pokémon catalog");
            service.Save(0,EditorValues.Flatten(service.Read(0)));service.Save(2,EditorValues.Flatten(service.Read(2)));
            check(File.ReadAllBytes(tp).SequenceEqual(beforeT)&&File.ReadAllBytes(pp).SequenceEqual(beforeP),tag+"no-op save preserves unsorted choices opaque bits and GARC bytes");
            var values=EditorValues.Flatten(service.Read(0));values["Count"]=3;values["Choice2"]=0;service.Save(0,values);
            var actual=new GARC.MemGARC(File.ReadAllBytes(tp)).Files;
            check(actual[0].SequenceEqual(new byte[]{1,0,3,0,2,0,1,0,0,0,0xAA,0xBB})&&actual[1].SequenceEqual(trainers[1]),tag+"resizes choices preserving trailer and unrelated trainer");
            values=EditorValues.Flatten(service.Read(2));check(values.ContainsKey("Form")== (f.Game.Generation==7),tag+"form control only belongs to Gen7");values["Species"]=4;values["EV1"]=1;if(f.Game.Generation==7)values["Form"]=2;service.Save(2,values);
            byte[] expected=(byte[])pokemon[0].Clone();expected[0]=4;expected[10]=0xC3;if(f.Game.Generation==7)expected[14]=2;
            var result=new GARC.MemGARC(File.ReadAllBytes(pp)).Files;
            check(result[0].SequenceEqual(expected)&&result[1].SequenceEqual(pokemon[1])&&result[2].SequenceEqual(pokemon[2]),tag+"writes only selected fields preserving high EV bits and suffix");
            values=EditorValues.Flatten(service.Read(0));values["Choice0"]=999;reject(()=>service.Save(0,values),tag+"rejects new invalid catalog reference");
            check(service.ExportText().Contains("Cantidad de opciones")&&service.ExportText().Contains("Pokémon 000"),tag+"exports trainers and Pokémon");
            byte[] changed=File.ReadAllBytes(pp);changed[^1]^=1;File.WriteAllBytes(pp,changed);
            reject(()=>service.Save(0,EditorValues.Flatten(service.Read(0))),tag+"stale companion archive blocks operation");
        }
    }
}
