using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;
internal static class Tutor6CompatibilityChecks
{
    internal static byte[] Setup(Fixture f)
    {
        ModuleChecks.CreateArchives(f);Tutor6Checks.Setup(f);var personal=f.PersonalRecords;
        for(int id=1;id<=3;id++)for(int slot=0;slot<60;slot++){var (group,flag)=Gen6TutorLayout.Compatibility(slot);int offset=0x40+group*4+flag/8;personal[id][offset]&=(byte)~(1<<(flag%8));}
        GarcTransaction.RebuildPersonalMaster(personal);File.WriteAllBytes(f.PersonalPath,GarcTransaction.Build(File.ReadAllBytes(f.PersonalPath),f.Game.GetGARCReference("personal"),personal));
        byte[] learned=new byte[8];BitConverter.GetBytes((ushort)134).CopyTo(learned,0);BitConverter.GetBytes((ushort)5).CopyTo(learned,2);learned.AsSpan(4).Fill(255);
        var sets=ModuleChecks.Read(f,"levelup");sets[1]=learned;f.WriteGarc(ModuleChecks.PathFor(f,"levelup"),sets,f.Game.GetGARCReference("levelup"));
        var evos=ModuleChecks.Read(f,"evolution");foreach(var record in evos)Array.Clear(record);
        for(int id=1;id<=2;id++){BitConverter.GetBytes((ushort)4).CopyTo(evos[id],0);BitConverter.GetBytes((ushort)(id+1)).CopyTo(evos[id],4);}f.WriteGarc(ModuleChecks.PathFor(f,"evolution"),evos,f.Game.GetGARCReference("evolution"));
        return File.ReadAllBytes(f.PersonalPath);
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var f=new Fixture(GameVersion.ORAS);byte[] original=Setup(f),code=File.ReadAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"));var before=new GARC.MemGARC(original).Files;
        var service=new GameTutor6CompatibilityService(f.Session);var preview=service.Preview(true,true);var after=new GARC.MemGARC(preview.File.Proposed()).Files;
        check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(original),"ORAS tutor compatibility preview is read-only");
        check(Enumerable.Range(1,3).All(id=>new PersonalInfoORAS(after[id]).SpecialTutors[2][0]),"ORAS tutor sanity uses shop row zero's actual legacy flag and inherits through two evolution steps");
        byte[] masks=new byte[16];for(int slot=0;slot<60;slot++){var (group,flag)=Gen6TutorLayout.Compatibility(slot);masks[group*4+flag/8]|=(byte)(1<<(flag%8));}
        check(Enumerable.Range(0,after.Length-1).All(id=>after[id].Select((value,offset)=>(value^before[id][offset])&(offset is >=0x40 and <0x50?~masks[offset-0x40]:255)).All(value=>value==0)),"ORAS tutor compatibility preserves every nonactive bit including MTs type tutors three unused entries and reserved bits");
        check(after[^1].SequenceEqual(after[..^1].SelectMany(row=>row)),"ORAS tutor compatibility rebuilds exact Personal master");
        service.Apply(preview);check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(preview.File.Proposed())&&File.ReadAllBytes(Path.Combine(f.Game.ExeFS,"code.bin")).SequenceEqual(code),"ORAS tutor compatibility commits only Personal and leaves code tables intact");
        check(new GameTutor6CompatibilityService(f.Session).Preview(true,true).Changes.Count==0,"ORAS tutor compatibility is idempotent");reject(()=>service.Apply(preview),"ORAS tutor stale compatibility proposal rejected");
        File.WriteAllBytes(f.PersonalPath,original);service=new(f.Session);preview=service.Preview(true,true);byte[] changed=(byte[])code.Clone();changed[0]^=1;File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"),changed);reject(()=>service.Apply(preview),"ORAS tutor changed code assignment dependency blocks commit");check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(original),"ORAS tutor dependency rejection preserves Personal");File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"),code);
        var doc=GlobalTemplateDocument.Empty("ORAS",6);TutorCompatibilityTemplate.Capture(doc,new(true,true));NativeBatchGen6Bridge.ValidateSession(f.Session,doc);NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),45);check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(preview.File.Proposed()),"ORAS native Batch Sanity and Follow match combined manual Personal bytes");
        check(TutorCompatibilityTemplate.Read(doc)==new TutorCompatibilityState(true,true),"ORAS Global roundtrips tutor compatibility options");doc.SelectActions(new HashSet<string>{"move-tutors.follow-evolutions"});NativeRecipeCompletion.ReconcileSelection(doc);check(TutorCompatibilityTemplate.Read(doc)==new TutorCompatibilityState(false,true),"ORAS Global deselection reconciles tutor compatibility options without MT snapshot");
        var bad=doc.Section("ProgressiveApp")!;bad["TutorCompatibility"]!["Scope"]=1;doc.SetSection("ProgressiveApp",bad);reject(()=>NativeBatchGen6Bridge.ValidateDocument(doc),"ORAS Batch rejects partial tutor compatibility scope");
        File.WriteAllBytes(f.PersonalPath,original);File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"),code);
        var randomEditor=new GameTutor6Service(f.Session);var randomOptions=EditorValues.Flatten(randomEditor.RandomOptions);
        var randomCode=randomEditor.Preview(randomOptions,randomEditor.Entries.Select(row=>row.Id).ToArray(),73+1009).Files[0].Proposed();var randomLayout=Gen6TutorLayout.Read(randomCode,f.Game.Moves.Length);ushort learnedMove=BitConverter.ToUInt16(randomCode,randomLayout.Shop);
        var learnsets=ModuleChecks.Read(f,"levelup");BitConverter.GetBytes(learnedMove).CopyTo(learnsets[1],0);f.WriteGarc(ModuleChecks.PathFor(f,"levelup"),learnsets,f.Game.GetGARCReference("levelup"));
        File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"),randomCode);byte[] expectedPersonal=new GameTutor6CompatibilityService(f.Session).Preview(true,false).File.Proposed();File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"),code);
        var ordered=NativeRecipeFactory.Create(f.Session,document=>TutorCompatibilityTemplate.Capture(document,new(true,false)),modules:["Tutors"]);var native=ordered.Section("ProgressiveApp")!;native["PartialSnapshot"]=false;ordered.SetSection("ProgressiveApp",native);
        NativeBatchGen6Bridge.ExecuteSession(f.Session,ordered.ToJson(),73);
        check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(expectedPersonal)&&File.ReadAllBytes(Path.Combine(f.Game.ExeFS,"code.bin")).SequenceEqual(randomCode),"ORAS Batch runs tutor assignment before Sanity even when capture action order was reversed");
        using var xy=new Fixture(GameVersion.XY);reject(()=>new GameTutor6CompatibilityService(xy.Session),"ORAS tutor compatibility is unavailable for XY");
    }
    internal static void RunUi(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.ORAS);Setup(f);var main=new MainWindow();main.Show();
        try
        {
            main.LoadProject(f.Session);typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,["Tutor6"]);
            var view=((System.Windows.Controls.ContentControl)main.FindName("SectionHost")).Content as Gen6TutorView;
            check(view?.Editor.Editor is GameTutor6Service,"WPF ORAS tutors integrate native record editor and compatibility panel");
            var flags=typeof(Gen6TutorView).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Where(field=>field.FieldType==typeof(System.Windows.Controls.CheckBox)).Select(field=>(System.Windows.Controls.CheckBox)field.GetValue(view)!).ToArray();foreach(var flag in flags)flag.IsChecked=true;
            ((System.Windows.Controls.Button)main.FindName("GlobalTemplateButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var doc=GlobalTemplateDocument.Empty("ORAS",6);typeof(MainWindow).GetMethod("CaptureGlobalOptions",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,[doc]);
            check(TutorCompatibilityTemplate.Read(doc)==new TutorCompatibilityState(true,true)&&doc.Section("ProgressiveApp")?["Tutors"] is not null,"WPF ORAS navigation captures tutor options and compatibility flags into Global");
            TutorCompatibilityTemplate.Capture(doc,new(false,true));typeof(MainWindow).GetMethod("RestoreGlobalOptions",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,[doc]);typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,["Tutor6"]);
            view=((System.Windows.Controls.ContentControl)main.FindName("SectionHost")).Content as Gen6TutorView;check(view!.RememberCompatibility()==new TutorCompatibilityState(false,true),"WPF ORAS Global restore repopulates tutor compatibility flags");
        }
        finally{main.Close();}
    }
}
