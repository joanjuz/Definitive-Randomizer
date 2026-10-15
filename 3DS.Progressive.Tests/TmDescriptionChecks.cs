using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;
internal static class TmDescriptionChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})foreach(int language in new[]{0,6})
        {
            using var f=new Fixture(version);f.Game.Language=language;
            byte[] code=new byte[0x402000];byte[] signature=[0x03,0x40,0x03,0x41,0x03,0x42,0x03,0x43,0x03];signature.CopyTo(code,0x400000);int offset=0x400000+signature.Length+(version==GameVersion.USUM?0x22:0);for(int i=0;i<100;i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(code,offset+i*2);
            string codePath=Path.Combine(f.Game.ExeFS,"code.bin");File.WriteAllBytes(codePath,code);
            var lines=f.Game.GameTextStrings.Select(row=>(string[])row.Clone()).ToArray();int items=f.Game.GameText.First(row=>row.Name==TextName.ItemFlavor).Index,moves=f.Game.GameText.First(row=>row.Name==TextName.MoveFlavor).Index;lines[moves][15]="Descripción nueva en idioma "+language+"  ";lines[items][328]="Descripción anterior";
            byte[][] records=lines.Select(row=>TextFile.GetBytes(f.Game,row,f.Game.RemapCharacters)).ToArray();string textPath=Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("gametext"));var reference=f.Game.GetGARCReference("gametext");if(reference.LanguageVariant)reference=reference.GetRelativeGARC(language);f.WriteGarc(textPath,records,reference);
            var seeded=new GameMachineTutorService(f.Session);seeded.RandomizeMachines(12345);var repeated=new GameMachineTutorService(f.Session);repeated.RandomizeMachines(12345);
            check(seeded.Machines.Select(row=>row.MoveId).SequenceEqual(repeated.Machines.Select(row=>row.MoveId)),version+" TM randomization repeats exact seed");
            check(seeded.Machines.All(row=>row.MoveId<f.Game.Moves.Length)&&seeded.MoveChoices.All(row=>row.Id<f.Game.Moves.Length),version+" TM pool and choices only include existing moves");
            var service=new GameMachineTutorService(f.Session);service.Machines[0].MoveId=15;service.SaveMachines(synchronizeDescriptions:true);var after=new GARC.MemGARC(File.ReadAllBytes(textPath)).Files;
            check(BitConverter.ToUInt16(File.ReadAllBytes(codePath),offset)==15&&new TextFile(f.Game,after[items],f.Game.RemapCharacters).Lines[328]==lines[moves][15].Trim(),version+" TM and localized item description saved together in language "+language);
            check(records.Where((_,id)=>id!=items).Zip(after.Where((_,id)=>id!=items)).All(pair=>pair.First.SequenceEqual(pair.Second)),version+" TM save preserves all unrelated text banks in language "+language);
            var decoded=new TextFile(f.Game,after[items],f.Game.RemapCharacters).Lines;check(lines[items].Skip(329).SequenceEqual(decoded.Skip(329))&&lines[items].Take(328).SequenceEqual(decoded.Take(328)),version+" TM save only changes the affected item description in language "+language);
            var document=NativeRecipeFactory.Create(f.Session,modules:["Machines"]);check(document.Actions.Count==1&&NativeGen7MachineBridge.Read(document)==new Gen7MachineState(RandomMachines:true),version+" native factory creates machine module without duplicate actions");
            var expectedService=new GameMachineTutorService(f.Session);expectedService.RandomizeMachines(791);var expected=expectedService.PrepareMachines(true).Files;
            byte[] unchanged=File.ReadAllBytes(codePath);check(NativeRecipeCompletion.TryComplete(f.Session,document)&&File.ReadAllBytes(codePath).SequenceEqual(unchanged),version+" machine native completion previews without writing");GlobalEditorTemplateBridge.ValidateBatchReplay(document);
            var assembly=System.Reflection.Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));var type=assembly.GetType("pk3DS.WinForms.GlobalRandomizationTemplate",true)!;string replay=System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize(document.ToJson(),type),type);
            NativeGen7MachineBridge.ExecuteSession(f.Session,replay,791,"Machines");check(expected.All(file=>File.ReadAllBytes(file.Path).SequenceEqual(file.Proposed())),version+" original typed template replays exact native TMs and descriptions");
            var invalid=document.Clone();var native=invalid.Section("ProgressiveApp")!;native["Gen7Machines"]!["Options"]!.AsObject().Remove("tms.sanity");invalid.SetSection("ProgressiveApp",native);reject(()=>NativeGen7MachineBridge.ExecuteSession(f.Session,invalid.ToJson(),1,"Machines"),version+" incomplete machine options rejected");
            invalid=document.Clone();native=invalid.Section("ProgressiveApp")!;native["Gen7Machines"]!["Scope"]=1;invalid.SetSection("ProgressiveApp",native);reject(()=>NativeGen7MachineBridge.ExecuteSession(f.Session,invalid.ToJson(),1,"Machines"),version+" selected-only machine recipe rejected for Batch");
            invalid=document.Clone();NativeGen7MachineBridge.Capture(invalid,new(RandomTutors:true));reject(()=>NativeGen7MachineBridge.ValidateSession(f.Session,invalid),version+" unsupported or absent tutor sources reject before writing");
            invalid=document.Clone();invalid.SelectActions(new System.Collections.Generic.HashSet<string>());NativeRecipeCompletion.ReconcileSelection(invalid);NativeGen7MachineBridge.ValidateDocument(invalid);check(NativeGen7MachineBridge.Read(invalid)==new Gen7MachineState(),version+" deselecting machine actions reconciles snapshot flags");
            var stale=new GameMachineTutorService(f.Session);stale.Machines[1].MoveId=16;byte[] stableCode=File.ReadAllBytes(codePath);File.AppendAllText(textPath,"external");reject(()=>stale.SaveMachines(synchronizeDescriptions:true),version+" stale description source rejected before code write in language "+language);check(stableCode.SequenceEqual(File.ReadAllBytes(codePath)),version+" rejected description transaction preserves code in language "+language);
        }
    }
}
