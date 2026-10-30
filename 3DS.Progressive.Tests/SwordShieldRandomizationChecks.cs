using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;

internal static class SwordShieldRandomizationChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var original=SwordShieldChecks.Table();
        foreach(var field in new[]{SwordShieldField.Ability1,SwordShieldField.Ability2,SwordShieldField.AbilityHidden})
        {
            var t=new SwordShieldPersonalTable(original);int at=0xB0+SwordShieldPersonalTable.FieldOffset(field);
            var edited=t.WithValue(1,field,267);
            check(edited[at]==11&&edited[at+1]==1&&new SwordShieldPersonalTable(edited).Read(1).Values[(int)field]==267&&edited.Where((_,i)=>i!=at&&i!=at+1).SequenceEqual(original.Where((_,i)=>i!=at&&i!=at+1)),$"Swsh_Rand_AbilityUshortPreservesOtherBytes_{field}");
            reject(()=>t.WithValue(1,field,0),$"Swsh_Rand_AbilityRejectZero_{field}");reject(()=>t.WithValue(1,field,268),$"Swsh_Rand_AbilityRejectAfterMax_{field}");
        }
        var options=new SwordShieldRandomOptions(123,true,true);
        var result=SwordShieldPersonalRandomizer.Generate(original,original,options);var changed=result.ToArray();
        check(changed.SequenceEqual(SwordShieldPersonalRandomizer.Generate(original,original,options).ToArray())&&original.SequenceEqual(SwordShieldChecks.Table()),"Swsh_Rand_SameSeedReproducesWithoutSourceWrites");
        check(!changed.SequenceEqual(SwordShieldPersonalRandomizer.Generate(original,original,options with{Seed=124}).ToArray()),"Swsh_Rand_DifferentSeedChangesResult");
        var e=new SwordShieldPersonalTable(changed).Read(1);
        check(e.Values[6]==0&&e.Values[7]==17&&e.Values[9]==3&&e.Values[10]==1&&e.Values[11]==3,"Swsh_Rand_V1GoldenSeed123");
        var allowed=new HashSet<int>{6,7,0x18,0x19,0x1A,0x1B,0x1C,0x1D};
        check(changed.Select((b,i)=>(b,i)).Where(x=>x.b!=original[x.i]).All(x=>x.i/176 is >=1 and <=3&&allowed.Contains(x.i%176)),"Swsh_Rand_OnlyEnabledFieldBytesChange");
        var noop=SwordShieldPersonalRandomizer.Generate(original,original,options with{Types=false,Abilities=false});
        check(noop.ToArray().SequenceEqual(original)&&noop.Changes.Count==0&&noop.EligibleEntries==0,"Swsh_Rand_NoOptionsMeansNoChanges");
        var onlyTypes=SwordShieldPersonalRandomizer.Generate(original,original,options with{Abilities=false}).ToArray();
        var onlyAbilities=SwordShieldPersonalRandomizer.Generate(original,original,options with{Types=false}).ToArray();
        for(int entry=1;entry<=3;entry++)
        {
            var both=new SwordShieldPersonalTable(changed).Read(entry);var t=new SwordShieldPersonalTable(onlyTypes).Read(entry);var a=new SwordShieldPersonalTable(onlyAbilities).Read(entry);
            check(both.Values.Skip(6).Take(2).SequenceEqual(t.Values.Skip(6).Take(2))&&both.Values.Skip(9).SequenceEqual(a.Values.Skip(9)),$"Swsh_Rand_FeatureStreamsIndependent_{entry}");
            check(a.Values.Take(9).SequenceEqual(new SwordShieldPersonalTable(original).Read(entry).Values.Take(9)),$"Swsh_Rand_AbilitiesOnlyPreservesTypesAndStats_{entry}");
            check(both.Values[9]!=both.Values[10]&&both.Values.Skip(9).Take(3).All(id=>id>=1&&id<=3),$"Swsh_Rand_DistinctNormalsAndObservedPool_{entry}");
        }
        var onlyOne=SwordShieldPersonalRandomizer.Generate(original,original,options with{OnlyEntry=2}).ToArray();
        check(onlyOne.Where((_,i)=>i/176!=2).SequenceEqual(original.Where((_,i)=>i/176!=2)),"Swsh_Rand_SelectedScopePreservesOtherEntries");
        reject(()=>SwordShieldPersonalRandomizer.Generate(original,original,options with{OnlyEntry=4}),"Swsh_Rand_RejectUnavailableScope");
        var mono=(byte[])original.Clone();mono[176+7]=mono[176+6];
        var preserved=new SwordShieldPersonalTable(SwordShieldPersonalRandomizer.Generate(mono,mono,options).ToArray());
        check(preserved.Read(1).Values[6]==preserved.Read(1).Values[7]&&preserved.Read(2).Values[6]!=preserved.Read(2).Values[7],"Swsh_Rand_PreservesMonoAndDualTypeCount");
        var forms=(byte[])original.Clone();BinaryPrimitives.WriteUInt16LittleEndian(forms.AsSpan(176+0x1E),899);forms[176+0x20]=2;Array.Copy(original,176,forms,899*176,176);
        var noForms=SwordShieldPersonalRandomizer.Generate(forms,forms,options with{IncludeForms=false}).ToArray();
        check(noForms.AsSpan(899*176,176).SequenceEqual(forms.AsSpan(899*176,176)),"Swsh_Rand_ExcludedFormsPreserved");
        reject(()=>SwordShieldPersonalRandomizer.Generate(forms,forms,options with{IncludeForms=false,OnlyEntry=899}),"Swsh_Rand_RejectExcludedSelectedForm");
        var yesForms=SwordShieldPersonalRandomizer.Generate(forms,forms,options);
        check(yesForms.Changes.Any(c=>c.Entry==899),"Swsh_Rand_IncludesResolvedAlternateForms");
        var protectedTable=(byte[])forms.Clone();BinaryPrimitives.WriteUInt16LittleEndian(protectedTable.AsSpan(176+0x18),176);
        var safe=SwordShieldPersonalRandomizer.Generate(protectedTable,protectedTable,options);
        check(safe.ProtectedSpecies.SequenceEqual(new[]{1})&&safe.ToArray().AsSpan(176,176).SequenceEqual(protectedTable.AsSpan(176,176))&&safe.ToArray().AsSpan(899*176,176).SequenceEqual(protectedTable.AsSpan(899*176,176)),"Swsh_Rand_ProtectsMechanicSpeciesAndAllForms");
        var guard=(byte[])original.Clone();BinaryPrimitives.WriteUInt16LittleEndian(guard.AsSpan(176+0x1C),25);
        check(!SwordShieldPersonalRandomizer.AbilityPool(guard,false).Contains(25)&&SwordShieldPersonalRandomizer.AbilityPool(guard,true).Contains(25)&&!SwordShieldPersonalRandomizer.AbilityPool(protectedTable,true).Contains(176),"Swsh_Rand_WonderGuardOptInAndMechanicsExcluded");
        var emptyPool=(byte[])original.Clone();for(int i=1;i<=3;i++)for(int slot=0;slot<3;slot++)BinaryPrimitives.WriteUInt16LittleEndian(emptyPool.AsSpan(i*176+0x18+slot*2),0);
        reject(()=>SwordShieldPersonalRandomizer.Generate(emptyPool,emptyPool,options),"Swsh_Rand_RejectEmptyAbilityPool");
        check(SwordShieldPersonalRandomizer.Generate(emptyPool,emptyPool,options with{Abilities=false}).Changes.Count>0,"Swsh_Rand_TypesDoNotRequireAbilityPool");
        var singleton=(byte[])emptyPool.Clone();for(int i=1;i<=3;i++)for(int slot=0;slot<3;slot++)BinaryPrimitives.WriteUInt16LittleEndian(singleton.AsSpan(i*176+0x18+slot*2),1);
        reject(()=>SwordShieldPersonalRandomizer.Generate(singleton,singleton,options),"Swsh_Rand_RejectSingletonDistinctPool");
        check(SwordShieldPersonalRandomizer.Generate(singleton,singleton,options with{DistinctNormalAbilities=false}).EligibleEntries==3,"Swsh_Rand_SingletonPoolAllowsRepeatedNormals");
        var invalid=(byte[])original.Clone();BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(176+0x18),268);
        reject(()=>SwordShieldPersonalRandomizer.Generate(invalid,invalid,options),"Swsh_Rand_RejectUnsupportedObservedAbility");
        foreach(uint seed in new[]{0u,uint.MaxValue})check(SwordShieldPersonalRandomizer.Generate(original,original,options with{Seed=seed}).Changes.Count>0,$"Swsh_Rand_SeedBoundary_{seed}");
        string temp=Path.Combine(Path.GetTempPath(),"DR-Swsh-Random-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            string root=SwordShieldChecks.Fixture(temp,"source");var session=SwordShieldProject.Open(root);
            var preview=session.PreviewRandom(options);check(!session.HasChanges&&session.History.Count==0&&session.Randomizations.Count==0&&preview.RandomOptions==options,"Swsh_Rand_ProjectPreviewWaitsForConfirmation");
            var stale=session.PreviewRandom(options with{Seed=321});session.Apply(preview);
            reject(()=>session.Apply(stale),"Swsh_Rand_ProjectRejectStaleBulkPreview");
            check(session.History.Count==preview.Changes.Count&&session.Randomizations.Single().Options==options&&session.Randomizations.Single().InputSha256==session.SourceHash&&session.Randomizations.Single().ResultSha256==session.DraftHash,"Swsh_Rand_ProjectJournalStoresSeedOptionsAndHashes");
            string output=Path.Combine(temp,"mod");session.Export(output);
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,"definitive-mod.json")));
            var op=manifest.RootElement.GetProperty("Randomizations")[0];
            check(op.GetProperty("Algorithm").GetString()==SwordShieldPersonalRandomizer.Algorithm&&op.GetProperty("Options").GetProperty("Seed").GetUInt32()==123&&op.GetProperty("Options").GetProperty("Abilities").GetBoolean()&&manifest.RootElement.GetProperty("Changes").GetArrayLength()==session.NetChanges().Count,"Swsh_Rand_ExportCarriesReproductionMetadata");
            check(File.ReadAllBytes(Path.Combine(root,"romfs",SwordShieldProject.PersonalRelativePath)).SequenceEqual(original),"Swsh_Rand_BulkExportPreservesOriginalSource");
            string ryujinx=Path.Combine(temp,"ryujinx");session.Export(ryujinx,SwitchModLayout.Ryujinx);
            check(File.ReadAllBytes(Path.Combine(ryujinx,"romfs",SwordShieldProject.PersonalRelativePath)).SequenceEqual(preview.Result)&&!Directory.Exists(Path.Combine(ryujinx,"atmosphere")),"Swsh_Rand_RyujinxLayoutContainsDirectRomfs");
            using(var r=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ryujinx,"definitive-mod.json"))))check(r.RootElement.GetProperty("Layout").GetString()=="Ryujinx","Swsh_Rand_ManifestIdentifiesExportLayout");
            reject(()=>session.Export(Path.Combine(temp,"bad-layout"),(SwitchModLayout)42),"Swsh_Rand_RejectUnknownExportLayout");
            session.Reset();check(!session.HasChanges&&session.History.Count==0&&session.Randomizations.Count==0,"Swsh_Rand_ResetClearsRandomizationJournal");
            string unknown=SwordShieldChecks.Fixture(temp,"unknown");string main=Path.Combine(unknown,"exefs/main");byte[] nso=File.ReadAllBytes(main);nso[0x40]=0;File.WriteAllBytes(main,nso);
            reject(()=>SwordShieldProject.Open(unknown).PreviewRandom(options),"Swsh_Rand_UnknownBuildRejectsBulkPreview");
        }
        finally{Directory.Delete(temp,true);}
    }
    internal static void RunUi(Action<bool,string> check)
    {
        string temp=Path.Combine(Path.GetTempPath(),"DR-Swsh-RandUi-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        var window=new ThreeDS.Progressive.SwordShieldPrototypeWindow();
        try
        {
            var project=SwordShieldProject.Open(SwordShieldChecks.Fixture(temp,"ui"));window.PresentProject(project);
            var types=(System.Windows.Controls.CheckBox)window.FindName("SwitchRandomTypes");
            var abilities=(System.Windows.Controls.CheckBox)window.FindName("SwitchRandomAbilities");
            var seed=(System.Windows.Controls.TextBox)window.FindName("SwitchSeed");
            var apply=(System.Windows.Controls.Button)window.FindName("SwitchApply");
            var export=(System.Windows.Controls.Button)window.FindName("SwitchExport");
            var journal=(System.Windows.Controls.TextBox)window.FindName("SwitchChanges");
            check(types.IsChecked==false&&abilities.IsChecked==false&&System.Windows.Automation.AutomationProperties.GetLabeledBy(seed) is not null,"Swsh_Rand_UI_DefaultsAndSeedAccessibility");
            Pump(window.PrepareRandomizationAsync());
            check(!apply.IsEnabled&&!project.HasChanges,"Swsh_Rand_UI_EmptySelectionIsNoOp");
            types.IsChecked=true;abilities.IsChecked=true;seed.Text="123";Pump(window.PrepareRandomizationAsync());
            check(apply.IsEnabled&&!project.HasChanges&&journal.Text.Contains("semilla 123"),"Swsh_Rand_UI_BulkPreviewWaitsForApply");
            seed.Text="124";check(!apply.IsEnabled,"Swsh_Rand_UI_SeedChangeInvalidatesPreview");
            Pump(window.PrepareRandomizationAsync());apply.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            check(project.HasChanges&&project.Randomizations.Single().Options.Seed==124&&export.IsEnabled&&journal.Text.Contains("Semilla 124"),"Swsh_Rand_UI_ApplyRecordsSeedAndEnablesExport");
            Pump(window.PrepareRandomizationAsync());abilities.IsChecked=false;check(!apply.IsEnabled,"Swsh_Rand_UI_OptionChangeInvalidatesPreview");
            types.IsChecked=false;Pump(window.PrepareRandomizationAsync());
            check(export.IsEnabled&&!apply.IsEnabled&&project.HasChanges,"Swsh_Rand_UI_NoOptionsDoesNotBlockExistingModExport");
            var layout=(System.Windows.Controls.ComboBox)window.FindName("SwitchExportLayout");layout.SelectedIndex=1;
            check(!layout.IsEditable&&System.Windows.Automation.AutomationProperties.GetLabeledBy(layout) is not null&&layout.SelectedIndex==1,"Swsh_Rand_UI_ExportLayoutSelectorIsAccessible");

        }
        finally{ThreeDS.Progressive.StudioDialog.AutomatedChecks=true;window.Close();Directory.Delete(temp,true);}
    }
    internal static void ProbeReal(string root,string output,uint seed)
    {
        var project=SwordShieldProject.Open(root);
        byte[] before=File.ReadAllBytes(Path.Combine(root,"romfs",SwordShieldProject.PersonalRelativePath));
        var options=new SwordShieldRandomOptions(seed,true,true);
        var preview=project.PreviewRandom(options);
        var repeat=SwordShieldProject.Open(root).PreviewRandom(options);
        if(!preview.Result.SequenceEqual(repeat.Result))throw new InvalidDataException("Same seed produced different output.");
        project.Apply(preview);var result=project.Export(output,SwitchModLayout.Ryujinx);
        byte[] emitted=File.ReadAllBytes(Path.Combine(output,"romfs",SwordShieldProject.PersonalRelativePath));
        var allowed=new HashSet<int>{6,7,0x18,0x19,0x1A,0x1B,0x1C,0x1D};
        if(!emitted.SequenceEqual(preview.Result)||emitted.Length!=before.Length||!File.ReadAllBytes(Path.Combine(root,"romfs",SwordShieldProject.PersonalRelativePath)).SequenceEqual(before))throw new InvalidDataException("Byte verification or source preservation failed.");
        var oldTable=new SwordShieldPersonalTable(before);var newTable=new SwordShieldPersonalTable(emitted);
        int differentBytes=0;
        for(int i=0;i<before.Length;i++)if(before[i]!=emitted[i])
        {
            differentBytes++;var old=oldTable.Read(i/176);
            if(!allowed.Contains(i%176)||!old.Present||old.Species==0||preview.ProtectedSpecies.Contains(old.Species))throw new InvalidDataException("Unexpected byte difference.");
        }
        var pool=SwordShieldPersonalRandomizer.AbilityPool(before,false);
        foreach(var entry in oldTable.Entries().Where(e=>e.Species!=0&&!preview.ProtectedSpecies.Contains(e.Species)))
        {
            var edited=newTable.Read(entry.Index);
            if(edited.Values[9]==edited.Values[10]||!edited.Values.Skip(9).Take(3).All(pool.Contains)||(entry.Values[6]==entry.Values[7])!=(edited.Values[6]==edited.Values[7]))throw new InvalidDataException("Randomization constraint failed on real data.");
        }
        Console.WriteLine($"PASS real {project.Identity.Game}: seed {seed}; eligible {preview.EligibleEntries}; protected species {preview.ProtectedSpecies.Count}; changes {preview.Changes.Count}; byte differences {differentBytes}; candidate abilities {pool.Count}.");
        Console.WriteLine($"PASS deterministic repeat, allowed fields, ability pool, distinct normal slots, type-count preservation and unchanged source. SHA256 {result.ResourceSha256}");
        Console.WriteLine($"Ryujinx mod folder: {result.Folder}. Gameplay NOT tested.");
    }
    private static void Pump(Task task)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted)
        {
            if(watch.Elapsed>TimeSpan.FromSeconds(15))throw new TimeoutException("SWSH UI component task did not finish.");
            Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.Background);
        }
        task.GetAwaiter().GetResult();
    }
}
