using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
internal static class PokeFeurRealChecks
{
    internal static void Probe(string root,string packageRoot,string output)
    {
        int count=0;void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
        void Reject(Action action,string name){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}catch(IOException){rejected=true;}catch(NotSupportedException){rejected=true;}Check(rejected,name);}
        var project=SwordShieldProject.Open(root);var package=packageRoot=="--builtin"?PokeFeurPackage.BuiltIn(project.Identity.TitleId):PokeFeurPackage.Read(packageRoot);string initial=project.RevisionHash;string originalMain=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"exefs/main"))));
        Check(PokeFeurOptions.Catalog.Where(f=>f.Script is not null).All(f=>package.Has("romfs/"+f.Script)),"Poke_Real_PackageHasPinnedOptionalScripts");
        var all=new PokeFeurOptions{Randomizer=true,Caps=true,CapsFromTrainers=true,ExpRuntime=true,ExpMultiplier=2.5,Features=PokeFeurOptions.AvailableCatalog.Select(f=>f.Feature).ToArray()};
        var preview=project.PreviewPokeFeur(package,all);Check(!project.HasChanges&&project.PokeFeurHistory.Count==0&&project.RevisionHash==initial,"Poke_Real_PreviewWritesNothingAndDoesNotApply");
        Check(preview.Options.BattlePool.Count==664&&preview.Options.VisiblePool.Count==664,"Poke_Real_DefaultPoolsUseAllPresentBaseSpecies");
        Check(preview.Caps.Count==9&&preview.Caps.Select(c=>c.Trainer).SequenceEqual(new[]{32,36,37,project.Identity.TitleId=="01008DB008C2C000"?78:77,108,project.Identity.TitleId=="01008DB008C2C000"?136:135,107,144,149}),"Poke_Real_AutoCapsReadNineConfiguredBossTeams");
        foreach(var c in preview.Caps){byte[] team=File.ReadAllBytes(Path.Combine(root,c.Resource));int highest=Enumerable.Range(0,team.Length/32).Max(i=>(int)BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(i*32+10)));Check(c.Level==highest,"Poke_Real_CapMatchesOriginalTeam_"+c.Trainer);}
        var repeat=project.PreviewPokeFeur(null,all);Check(preview.Hashes.OrderBy(p=>p.Key).SequenceEqual(repeat.Hashes.OrderBy(p=>p.Key)),"Poke_Real_PreviewIsReproducible");
        project.ApplyPokeFeur(preview);Check(project.PokeFeurEnabled&&project.HasChanges&&project.PokeFeurHistory.Single().Changes.Any(c=>c.Parameter=="[randomizer].active")&&project.PokeFeurHistory.Single().Changes.Any(c=>c.Parameter=="[exp_level_cap].active"),"Poke_Real_ApplyRecordsGranularRuntimeHistory");
        Reject(()=>project.ApplyPokeFeur(repeat),"Poke_Real_RejectStalePreview");var foreign=SwordShieldProject.Open(root);Reject(()=>foreign.ApplyPokeFeur(preview),"Poke_Real_RejectForeignPreview");
        project.ApplyPokeFeur(project.PreviewPokeFeur(package,all));Check(project.PokeFeurHistory.Count==1,"Poke_Real_RepeatedApplyDoesNotDuplicateHistory");
        project.Export(output,SwitchModLayout.Ryujinx);Check(File.Exists(Path.Combine(output,"exefs/subsdk9"))&&File.Exists(Path.Combine(output,"exefs/main.npdm"))&&!File.Exists(Path.Combine(output,"exefs/main")),"Poke_Real_ExportUsesExternalModuleWithoutReplacingGameMain");
        foreach(var pair in preview.Hashes)Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output,pair.Key))))==pair.Value,"Poke_Real_ExportHash_"+Path.GetFileName(pair.Key));
        string registry="romfs/"+SwordShieldProject.ScriptRegistryRelative;var before=PokeFeurScriptRegistry.Read(File.ReadAllBytes(Path.Combine(root,registry)));var after=PokeFeurScriptRegistry.Read(File.ReadAllBytes(Path.Combine(output,registry))).ToDictionary(e=>e.Id);
        Check(before.All(e=>e.Id==13435777772824609090UL||after.TryGetValue(e.Id,out var found)&&e==found),"Poke_Real_ScriptRegistryPreservesEveryUnrelatedEntry");
        Check(after.Count==before.Count+2&&after[13435777772824609090UL].Amx=="bin/script/amx/randomizer_pc_menu.amx","Poke_Real_TwoScriptRegistrationsAndOnePcReplacement");
        using(var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"definitive-mod.json"))))Check(manifest.RootElement.GetProperty("PokeFeur").GetArrayLength()==1&&manifest.RootElement.GetProperty("Resources").EnumerateArray().Any(e=>e.GetProperty("Resource").GetString()==SwordShieldProject.PokeConfigRelative),"Poke_Real_ManifestIncludesRuntimeOptionsAndConfigHash");
        string sd=output+"-sd-check";Directory.CreateDirectory(sd);var install=project.PreviewPokeSd(sd);Check(!File.Exists(install.Destination),"Poke_Real_SdPreviewWritesNothing");project.InstallPokeSd(install,false);Check(File.ReadAllBytes(install.Destination).SequenceEqual(File.ReadAllBytes(Path.Combine(output,SwordShieldProject.PokeConfigRelative))),"Poke_Real_ExplicitSdInstallMatchesExport");
        var selected=project.Entries.First(e=>e.Form==0);project.Apply(project.Preview(selected.Index,SwordShieldField.HP,selected.Values[0]==255?254:selected.Values[0]+1));
        project.Export(output+"-combined",SwitchModLayout.Ryujinx);Check(File.Exists(Path.Combine(output+"-combined","romfs",SwordShieldProject.PersonalRelativePath))&&File.Exists(Path.Combine(output+"-combined","exefs/subsdk9")),"Poke_Real_CombinedExportKeepsPersonalAndRuntime");
        Reject(()=>project.Export(output+"-console",SwitchModLayout.Atmosphere),"Poke_Real_RejectUnvalidatedConsoleRuntime");Check(!Directory.Exists(output+"-console"),"Poke_Real_RejectedExportLeavesNoPartialFolder");
        var current=project.PokeFeurCurrent!;project.ApplyPokeFeur(project.PreviewPokeFeur(package,current with{CapsFromTrainers=false}));Check(project.PokeFeurCurrent!.CapsFromTrainers==false&&project.PokeFeurHistory.Last().Changes.Any(c=>c.Parameter=="caps.source"),"Poke_Real_ChangingAutoCapPolicyIsNotMistakenForNoOp");
        project.RemovePokeFeur();Check(!project.PokeFeurEnabled&&project.HasChanges&&project.PokeFeurHistory.Last().Enabled==false,"Poke_Real_RemoveRuntimePreservesOtherSessionEdits");project.Export(output+"-without-runtime",SwitchModLayout.Ryujinx);Check(!Directory.Exists(Path.Combine(output+"-without-runtime","exefs")),"Poke_Real_RemovedRuntimeExportsNoNativeArtifacts");project.Reset();Check(!project.HasChanges&&project.PokeFeurHistory.Count==0,"Poke_Real_ResetClearsRuntimeDraftAndHistory");
        Check(originalMain==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"exefs/main"))))&&PokeFeurScriptRegistry.Read(File.ReadAllBytes(Path.Combine(root,registry))).SequenceEqual(before),"Poke_Real_OriginalExecutableAndRegistryUnchanged");
        Exception? error=null;var thread=new Thread(()=>{try{var app=new ThreeDS.Progressive.App();app.InitializeComponent();WpfTestHost.SuppressStartup();ThreeDS.Progressive.StudioDialog.AutomatedChecks=true;var session=SwordShieldProject.Open(root);var window=new ThreeDS.Progressive.SwordPokeFeurWindow(session);try{window.SetOptions(new PokeFeurOptions{Randomizer=true});var task=window.PreviewAsync();var clock=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(clock.Elapsed.TotalSeconds>60)throw new TimeoutException("UI runtime preview timeout.");System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);}task.GetAwaiter().GetResult();var apply=(System.Windows.Controls.Button)window.FindName("PokeApply");Check(apply.IsEnabled&&!session.HasChanges,"Poke_Real_UiPreviewDoesNotApply");apply.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(session.PokeFeurEnabled&&((System.Windows.Controls.Button)window.FindName("PokeInstallSd")).IsEnabled,"Poke_Real_UiApplyEnablesExplicitSdInstall");}finally{window.Close();}}catch(Exception e){error=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
        Console.WriteLine($"Completed {count} PokeFeur real integration checks. Source read-only; UI off-screen; new gameplay features NOT tested.");
    }
}
