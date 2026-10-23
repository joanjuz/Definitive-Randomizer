using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
internal static class SwordSurpriseIntegrationChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        byte[] module=SwordShieldProject.LoadSurpriseRuntime();
        check(Convert.ToHexString(SHA256.HashData(module))==SwordSurpriseMod.RuntimeHash,"Surprise_Mod_EmbedsOnlyPinnedOwnedRuntime");
        for(int i=0;i<3;i++)check(SwordSurpriseMod.ReadSegment(module,i).Length>0,"Surprise_Mod_NativeSegmentHashValid_"+i);
        byte[] text=SwordSurpriseMod.ReadSegment(module,0),rodata=SwordSurpriseMod.ReadSegment(module,1);
        int mod0=BinaryPrimitives.ReadInt32LittleEndian(text.AsSpan(4)),robase=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(module.AsSpan(0x24)));
        check(mod0==robase&&rodata.AsSpan(0,4).SequenceEqual("MOD0"u8),"Surprise_Mod_OwnNsoHasResolvableMod0");
        check(SwordSurpriseMod.Branch(0x100,0x200)==0x14000040&&SwordSurpriseMod.Branch(0x200,0x100)==0x17FFFFC0,"Surprise_Mod_BranchesEncodeForwardAndBackward");
        reject(()=>SwordSurpriseMod.Branch(0,3),"Surprise_Mod_RejectsUnalignedBranch");reject(()=>SwordSurpriseMod.Branch(0,1<<27),"Surprise_Mod_RejectsOutOfRangeBranch");
        byte[] oversized=(byte[])module.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(oversized.AsSpan(12),BinaryPrimitives.ReadUInt32LittleEndian(oversized.AsSpan(12))|1);
        BinaryPrimitives.WriteUInt32LittleEndian(oversized.AsSpan(96),BinaryPrimitives.ReadUInt32LittleEndian(oversized.AsSpan(24))+1);
        reject(()=>SwordSurpriseMod.ReadSegment(oversized,0),"Surprise_Mod_RejectsCompressedSegmentLargerThanDecodedSize");
        byte[] bad=(byte[])module.Clone();bad[(int)BinaryPrimitives.ReadUInt32LittleEndian(bad.AsSpan(0x10))]^=1;
        reject(()=>SwordSurpriseMod.ReadSegment(bad,0),"Surprise_Mod_RejectsCorruptedNativeSegment");
        reject(()=>SwordSurpriseMod.ReadSegment(module,3),"Surprise_Mod_RejectsInvalidSegmentIndex");
        string temp=Path.Combine(Path.GetTempPath(),"dr-surprise-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            string root=SwordShieldChecks.Fixture(temp,"source");var project=SwordShieldProject.Open(root);
            string collision=Path.Combine(root,"exefs/subsdk9");File.WriteAllBytes(collision,new byte[]{1});reject(()=>project.PreviewSurprise(1),"Surprise_Mod_RefusesExistingSubsdk9WithoutOverwriting");check(File.ReadAllBytes(collision).SequenceEqual(new byte[]{1}),"Surprise_Mod_ExistingNativeModuleStaysUntouched");File.Delete(collision);
            string shield=SwordShieldChecks.Fixture(temp,"shield",true);bool blocked=false;try{SwordShieldProject.Open(shield).PreviewSurprise(1);}catch(NotSupportedException){blocked=true;}check(blocked,"Surprise_Mod_BlocksShield");
        }
        finally{Directory.Delete(temp,true);}
    }
    internal static void RunUi(Action<bool,string> check)
    {
        string temp=Path.Combine(Path.GetTempPath(),"dr-surprise-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        var window=new ThreeDS.Progressive.SwordSurpriseWindow(SwordShieldProject.Open(SwordShieldChecks.Fixture(temp,"source")));
        try
        {
            var seed=(System.Windows.Controls.TextBox)window.FindName("SurpriseSeed");var apply=(System.Windows.Controls.Button)window.FindName("SurpriseApply");
            check(!apply.IsEnabled,"Surprise_UI_NoApplyBeforeVerifiedPreview");
            check(System.Windows.Automation.AutomationProperties.GetLabeledBy(seed) is not null,"Surprise_UI_SeedHasAccessibleLabel");
            window.Measure(new System.Windows.Size(650,500));window.Arrange(new System.Windows.Rect(0,0,650,500));window.UpdateLayout();
            check(((System.Windows.Controls.TextBox)window.FindName("SurpriseDetails")).IsReadOnly,"Surprise_UI_ReadOnlyProposal");
        }
        finally{window.Close();Directory.Delete(temp,true);}
    }
    internal static void Probe(string root,string output)
    {
        void Must(bool value,string name){if(!value)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);}
        var project=SwordShieldProject.Open(root);byte[] main=File.ReadAllBytes(Path.Combine(root,"exefs/main"));string original=Convert.ToHexString(SHA256.HashData(main));byte[] npdm=File.ReadAllBytes(Path.Combine(root,"exefs/main.npdm"));
        var preview=project.PreviewSurprise(20261021);var repeat=project.PreviewSurprise(20261021);
        Must(preview.Result.Main().SequenceEqual(repeat.Result.Main())&&preview.Result.Module().SequenceEqual(repeat.Result.Module()),"Surprise_Real_SeedAndSourceReproduceBothNativeFiles");
        Must(!project.HasChanges&&project.SurpriseHistory.Count==0,"Surprise_Real_PreviewDoesNotApply");
        byte[] built=preview.Result.Main();
        Must((BinaryPrimitives.ReadUInt32LittleEndian(built.AsSpan(12))&1)==0&&BinaryPrimitives.ReadUInt32LittleEndian(built.AsSpan(96))==BinaryPrimitives.ReadUInt32LittleEndian(built.AsSpan(24)),"Surprise_Real_TextUsesLoaderCompatibleUncompressedStorage");
        byte[] before=SwordSurpriseMod.ReadSegment(main,0),after=SwordSurpriseMod.ReadSegment(preview.Result.Main(),0);var allowed=new HashSet<int>();foreach(var p in preview.Result.Patches)for(int i=0;i<4;i++)allowed.Add(p.Entry+i);
        Must(after.Length==before.Length+96&&Enumerable.Range(0,before.Length).All(i=>before[i]==after[i]||allowed.Contains(i)),"Surprise_Real_OnlyFourInstructionsAndNewStubsChange");
        for(int i=1;i<3;i++)Must(SwordSurpriseMod.ReadSegment(main,i).SequenceEqual(SwordSurpriseMod.ReadSegment(preview.Result.Main(),i)),"Surprise_Real_OriginalOtherSegmentPreserved_"+i);
        project.ApplySurprise(preview);Must(project.HasChanges&&project.SurpriseEnabled&&project.SurpriseHistory.Single().Pool.Count==preview.Result.Pool.Count,"Surprise_Real_ApplyRecordsSeedAndPresentPool");
        bool stale=false;try{project.ApplySurprise(repeat);}catch(InvalidOperationException){stale=true;}Must(stale,"Surprise_Real_RejectStaleNativePreview");
        var other=SwordShieldProject.Open(root);bool foreign=false;try{other.ApplySurprise(preview);}catch(InvalidOperationException){foreign=true;}Must(foreign,"Surprise_Real_RejectForeignNativePreview");
        project.Export(output,SwitchModLayout.Ryujinx);
        Must(File.ReadAllBytes(Path.Combine(output,"exefs/main")).SequenceEqual(preview.Result.Main())&&File.ReadAllBytes(Path.Combine(output,"exefs/subsdk9")).SequenceEqual(preview.Result.Module()),"Surprise_Real_ExportReopensNativeResources");
        Must(!File.Exists(Path.Combine(output,"exefs/main.npdm")),"Surprise_Real_NoNpdmReplacement");
        using(var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"definitive-mod.json"))))Must(manifest.RootElement.GetProperty("Surprise").GetArrayLength()==1&&manifest.RootElement.GetProperty("Resources").GetArrayLength()==2,"Surprise_Real_ManifestOwnNativeHashesAndHistory");
        Must(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"exefs/main"))))==original&&File.ReadAllBytes(Path.Combine(root,"exefs/main.npdm")).SequenceEqual(npdm),"Surprise_Real_OriginalExecutableAndNpdmUnchanged");
        var selected=project.Entries[0];int next=selected.Values[0]==255?254:selected.Values[0]+1;project.Apply(project.Preview(selected.Index,SwordShieldField.HP,next));
        string combined=output+"-combined-check";project.Export(combined,SwitchModLayout.Ryujinx);
        Must(File.Exists(Path.Combine(combined,"exefs/subsdk9"))&&File.Exists(Path.Combine(combined,"romfs",SwordShieldProject.PersonalRelativePath)),"Surprise_Real_CombinedExportPreservesPersonalAndNativeChanges");
        bool consoleBlocked=false;try{project.Export(output+"-console-rejected",SwitchModLayout.Atmosphere);}catch(NotSupportedException){consoleBlocked=true;}Must(consoleBlocked&&!Directory.Exists(output+"-console-rejected"),"Surprise_Real_RejectUntestedConsoleLayoutWithoutPartialOutput");
        project.Reset();Must(!project.HasChanges&&project.SurpriseHistory.Count==0,"Surprise_Real_ResetRemovesNativeOverlay");
        Exception? uiFailure=null;
        var thread=new Thread(()=>{try{
            var app=new ThreeDS.Progressive.App();app.InitializeComponent();WpfTestHost.SuppressStartup();ThreeDS.Progressive.StudioDialog.AutomatedChecks=true;
            var session=SwordShieldProject.Open(root);var window=new ThreeDS.Progressive.SwordSurpriseWindow(session);
            try{
                var task=window.PreviewAsync();var watch=System.Diagnostics.Stopwatch.StartNew();
                while(!task.IsCompleted){if(watch.Elapsed.TotalSeconds>45)throw new TimeoutException("Native UI preview timeout.");System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);}
                task.GetAwaiter().GetResult();var apply=(System.Windows.Controls.Button)window.FindName("SurpriseApply");
                Must(apply.IsEnabled&&!session.HasChanges,"Surprise_Real_UI_PreviewWaitsForApply");
                apply.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Must(session.SurpriseEnabled&&session.SurpriseHistory.Count==1&&!apply.IsEnabled,"Surprise_Real_UI_ApplyEnablesNativeSessionOverlay");session.Reset();
            }finally{window.Close();}
        }catch(Exception ex){uiFailure=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(uiFailure is not null)throw uiFailure;
        Console.WriteLine($"Native prototype: {preview.Result.Pool.Count} present base species, seed {preview.Result.Seed}, {output}. Gameplay NOT tested.");
    }
}
