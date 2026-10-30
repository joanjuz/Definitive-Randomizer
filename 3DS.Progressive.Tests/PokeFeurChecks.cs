using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
internal static class PokeFeurChecks
{
    internal static byte[] Registry()
    {
        var b=new byte[384];void U16(int at,ushort v)=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at),v);void I32(int at,int v)=>BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at),v);void U64(int at,ulong v)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(at),v);
        void Str(int at,string text){byte[] s=Encoding.UTF8.GetBytes(text);I32(at,s.Length);s.CopyTo(b,at+4);}
        I32(0,12);U16(4,6);U16(6,8);U16(8,4);I32(12,8);I32(16,4);I32(20,2);I32(24,40);I32(28,100);
        U16(40,10);U16(42,24);U16(44,8);U16(46,16);U16(48,20);I32(64,24);U64(72,0x1234);I32(80,112);I32(84,140);Str(192,"bin/script/amx/original.amx");Str(224,"script/keep.dat");
        U16(104,10);U16(106,24);U16(108,8);U16(110,16);U16(112,20);I32(128,24);U64(136,13435777772824609090UL);I32(144,112);I32(148,152);Str(256,"bin/script/amx/pokemoncenter.amx");Str(300,"script/pokemoncenter.dat");return b;
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var builtin=PokeFeurPackage.BuiltIn();
        check(Convert.ToHexString(SHA256.HashData(builtin.File("exefs/subsdk9")))==PokeFeurPackage.ModuleHash,"Poke_BuiltInRuntimeMatchesValidatedModule");
        check(Convert.ToHexString(SHA256.HashData(builtin.File("exefs/main.npdm")))==PokeFeurPackage.NpdmHash,"Poke_BuiltInMetadataMatchesValidatedProfile");
        check(PokeFeurPackage.ScriptHashes.All(p=>Convert.ToHexString(SHA256.HashData(builtin.File("romfs/"+p.Key)))==p.Value),"Poke_BuiltInIncludesAllPinnedOptionalScripts");
        check(Convert.ToHexString(SHA256.HashData(builtin.File("LICENSE-PokeFeur.txt")))==PokeFeurPackage.LicenseHash,"Poke_BuiltInPreservesUpstreamLicense");
        var copy=builtin.File("exefs/subsdk9");copy[0]^=255;check(Convert.ToHexString(SHA256.HashData(builtin.File("exefs/subsdk9")))==PokeFeurPackage.ModuleHash,"Poke_BuiltInResourcesAreDetachedSnapshots");
        int[] present={1,4,7,25,133,835};var baseline=new PokeFeurOptions().Normalize(present);string config=baseline.Config();
        check(!baseline.Enabled&&!config.Contains("active = true")&&config.Contains("[runtime_pc]\nactive = false")&&config.Contains("[runtime_status]\nactive = false"),"Poke_DefaultsDoNotActivateRuntimeOrScripts");
        check(config.Contains("[exp_share]\nenabled = true"),"Poke_DefaultPreservesVanillaExpShare");
        var options=(baseline with{Randomizer=true,BattlePool=new[]{1,4,7},Seed=123}).Normalize(present);string text=options.Config();
        check(text.Contains("visible_mode = \"vanilla\"")&&text.Contains("battle_mode = \"random\"")&&text.Contains("battle_pool = [1, 4, 7]")&&text.Contains("seed = 123"),"Poke_ValidatedSurpriseConfigurationUsesOfficialKeys");
        var retired=baseline with{Features=new[]{PokeFeurFeature.DexAnimations}};
        reject(()=>retired.Normalize(present),"Poke_RetiredDexRejectedByNormalization");reject(()=>retired.Config(),"Poke_RetiredDexCannotGenerateConfig");reject(()=>retired.RecipeJson(),"Poke_RetiredDexCannotExportRecipe");
        string legacy=System.Text.Json.JsonSerializer.Serialize(new{Version=1,Provider="PokeFeur",TitleId="0100ABF008968000",Options=retired});reject(()=>PokeFeurOptions.ReadRecipe(legacy),"Poke_RetiredDexRejectedInLegacyRecipe");
        check(!PokeFeurOptions.AvailableCatalog.Any(f=>f.Feature==PokeFeurFeature.DexAnimations)&&baseline.Config().Contains("[dex_animations]\nactive = false")&&(int)PokeFeurFeature.DexAnimations==9&&(int)PokeFeurFeature.EarlyBike==10,"Poke_RetiredDexExplicitlyOffAndSerializedIdsPreserved");
        foreach(var feature in PokeFeurOptions.AvailableCatalog){var o=(baseline with{Features=new[]{feature.Feature}}).Normalize(present);check(o.Enabled&&o.Config().Contains("["+feature.Section+"]\nactive = true")&&PokeFeurOptions.Catalog.Where(f=>f!=feature).All(f=>o.Config().Contains("["+f.Section+"]\nactive = false")),"Poke_FeatureIsolated_"+feature.Feature);}
        foreach(PokeFeurVisibleMode mode in Enum.GetValues<PokeFeurVisibleMode>())check((options with{Visible=mode}).Config().Contains("visible_mode = \""+new[]{"vanilla","random","zone","global","decouverte","decouverte_random"}[(int)mode]+"\""),"Poke_VisibleMode_"+mode);
        foreach(PokeFeurBattleMode mode in Enum.GetValues<PokeFeurBattleMode>())check((options with{Battle=mode}).Config().Contains("battle_mode = \""+mode.ToString().ToLowerInvariant()+"\""),"Poke_BattleMode_"+mode);
        check((options with{Caps=true,CapMode=PokeFeurCapMode.StoryGymLeague}).Config().Contains("mode = \"story_gym_league\""),"Poke_CapStoryModeOfficialKey");check((options with{Caps=true,CapMode=PokeFeurCapMode.ReceiverVsDefeated}).Config().Contains("mode = \"receiver_vs_defeated\""),"Poke_CapReceiverModeOfficialKey");
        var culture=System.Globalization.CultureInfo.CurrentCulture;try{System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("es-ES");check((options with{ExpRuntime=true,ExpMultiplier=2.5}).Config().Contains("multiplier = 2.5"),"Poke_ExpTomlAlwaysUsesDecimalPoint");}finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
        check((options with{ExpRuntime=true,ExpMultiplier=1}).Config().Contains("multiplier = 1.0"),"Poke_ExpIntegerStillSerializesAsTomlFloat");
        check((baseline with{ExpShare=false}).Enabled&&(baseline with{ExpShare=false}).Config().Contains("[exp_share]\nenabled = false"),"Poke_DisableExpShareActivatesBackend");
        int[] mutable={1,4,7};var frozen=(options with{BattlePool=mutable}).Normalize(present);mutable[0]=835;check(frozen.BattlePool.SequenceEqual(new[]{1,4,7}),"Poke_PreviewPoolsAreDetachedFromCaller");
        var recipe=PokeFeurOptions.ReadRecipe(options.RecipeJson()).Normalize(present);check(recipe.Config()==options.Config(),"Poke_RuntimeRecipeRoundTripPreservesAllOptions");
        var comprehensive=(options with{Visible=PokeFeurVisibleMode.RandomDiscovery,Battle=PokeFeurBattleMode.Global,Caps=true,CapsFromTrainers=true,CapMode=PokeFeurCapMode.ReceiverVsDefeated,ExpRuntime=true,ExpMultiplier=0.01,ExpShare=false,ShinyBoost=100,GlimwoodSpawns=20,FullyUncap=false,KeepFishingChain=false,Features=PokeFeurOptions.AvailableCatalog.Select(f=>f.Feature).ToArray()}).Normalize(present);
        var restored=PokeFeurOptions.ReadRecipe(comprehensive.RecipeJson()).Normalize(present);check(restored.Config()==comprehensive.Config()&&restored.CapsFromTrainers,"Poke_RecipeRoundTripAllNonDefaultRuntimeTools");
        reject(()=>PokeFeurOptions.ReadRecipe(options.RecipeJson().Replace("\"Version\": 1","\"Version\": 2")),"Poke_RejectUnknownRecipeVersion");reject(()=>PokeFeurOptions.ReadRecipe(options.RecipeJson().Replace("\"Options\":","\"Foreign\": false,\"Options\":")),"Poke_RejectUnknownRecipeFields");
        reject(()=>(options with{BattlePool=new[]{1,898}}).Normalize(present),"Poke_RejectAbsentPoolSpecies");reject(()=>(options with{BattlePool=new[]{1,1}}).Normalize(present),"Poke_RejectDuplicatePoolSpecies");
        reject(()=>(options with{StoryCaps=new[]{20,30}}).Normalize(present),"Poke_RejectMissingCapStages");reject(()=>(options with{StoryCaps=new[]{0,24,27,36,38,42,46,48,65}}).Normalize(present),"Poke_RejectCapBelowOne");reject(()=>(options with{StoryCaps=Enumerable.Repeat(101,9).ToArray()}).Normalize(present),"Poke_RejectCapAbove100");
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,0,100.01})reject(()=>(options with{ExpMultiplier=bad}).Normalize(present),"Poke_RejectExpBoundary_"+bad.ToString(System.Globalization.CultureInfo.InvariantCulture));
        check((options with{ExpMultiplier=0.01,ShinyBoost=100,GlimwoodSpawns=20,StoryCaps=Enumerable.Repeat(1,9).ToArray()}).Normalize(present).ExpMultiplier==0.01,"Poke_AcceptsExactParameterBoundaries");
        reject(()=>(options with{ShinyBoost=101}).Normalize(present),"Poke_RejectInvalidShinyBoost");reject(()=>(options with{GlimwoodSpawns=0}).Normalize(present),"Poke_RejectZeroSpawns");reject(()=>(options with{Seed=ulong.MaxValue}).Normalize(present),"Poke_RejectUnsignedSeedOutsideTomlRange");reject(()=>(options with{Battle=(PokeFeurBattleMode)100}).Normalize(present),"Poke_RejectUnknownRuntimeMode");reject(()=>(options with{Features=new[]{(PokeFeurFeature)999}}).Normalize(present),"Poke_RejectUnknownFeature");
        byte[] registry=Registry(),saved=(byte[])registry.Clone();var original=PokeFeurScriptRegistry.Read(registry);check(original.Count==2&&original[0].Id==0x1234&&original[1].Amx.EndsWith("pokemoncenter.amx",StringComparison.Ordinal),"Poke_RegistryReadsIndependentFixture");
        check(PokeFeurScriptRegistry.Patch(registry,false,false,false).SequenceEqual(registry),"Poke_NoScriptSelectionPreservesExactRegistryBytes");
        foreach(var selection in new[]{(Pc:false,Bike:true,Dex:false),(Pc:false,Bike:false,Dex:true),(Pc:true,Bike:false,Dex:false),(Pc:true,Bike:true,Dex:true)})
        {
            var patched=PokeFeurScriptRegistry.Patch(registry,selection.Pc,selection.Bike,selection.Dex);var records=PokeFeurScriptRegistry.Read(patched);int added=(selection.Pc?1:0)+(selection.Bike?1:0)+(selection.Dex?1:0);
            check(records.Count==2+added&&records.Single(e=>e.Id==0x1234)==original[0]&&registry.SequenceEqual(saved),"Poke_ScriptPatchPreservesUnrelatedAndSource_"+(selection.Pc?"Pc":"")+(selection.Bike?"Bike":"")+(selection.Dex?"Dex":""));
            check(PokeFeurScriptRegistry.Patch(patched,selection.Pc,selection.Bike,selection.Dex).SequenceEqual(patched),"Poke_ScriptPatchIsIdempotent_"+(selection.Pc?"Pc":"")+(selection.Bike?"Bike":"")+(selection.Dex?"Dex":""));
            if(selection.Pc)check(records.Single(e=>e.Id==13435777772824609090UL).Amx=="bin/script/amx/randomizer_pc_menu.amx","Poke_PcMenuReplacesOnlyExpectedCenterRecord");
        }
        var unknown=(byte[])registry.Clone();BinaryPrimitives.WriteUInt16LittleEndian(unknown.AsSpan(40),12);reject(()=>PokeFeurScriptRegistry.Read(unknown),"Poke_RegistryRejectsUnknownFieldsInsteadOfDroppingThem");
        var duplicate=(byte[])registry.Clone();BinaryPrimitives.WriteUInt64LittleEndian(duplicate.AsSpan(136),0x1234);reject(()=>PokeFeurScriptRegistry.Read(duplicate),"Poke_RegistryRejectsDuplicateIds");reject(()=>PokeFeurScriptRegistry.Read(new byte[8]),"Poke_RegistryRejectsTruncatedData");
        string temp=Path.Combine(Path.GetTempPath(),"dr-poke-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            string sd=Path.Combine(temp,"sd"),source=Path.Combine(temp,"source");Directory.CreateDirectory(sd);Directory.CreateDirectory(source);
            var plan=PokeFeurSdInstall.Preview(sd,text,source);check(!File.Exists(plan.Destination),"Poke_SdPreviewNeverWrites");PokeFeurSdInstall.Apply(plan,false);check(File.ReadAllText(plan.Destination)==text,"Poke_SdInstallCreatesExactConfig");
            var same=PokeFeurSdInstall.Preview(sd,text,source);PokeFeurSdInstall.Apply(same,false);check(same.Backup is null&&Directory.GetFiles(Path.GetDirectoryName(same.Destination)!).Length==1,"Poke_SdSameConfigIsIdempotent");
            var replace=PokeFeurSdInstall.Preview(sd,config,source);reject(()=>PokeFeurSdInstall.Apply(replace,false),"Poke_SdDifferentConfigRequiresExplicitReplace");check(File.ReadAllText(plan.Destination)==text,"Poke_SdRejectedReplacePreservesExisting");PokeFeurSdInstall.Apply(replace,true);check(File.ReadAllText(replace.Destination)==config&&File.ReadAllText(replace.Backup!)==text,"Poke_SdReplaceKeepsExactBackup");
            var stale=PokeFeurSdInstall.Preview(sd,text,source);File.WriteAllText(stale.Destination,"external edit");reject(()=>PokeFeurSdInstall.Apply(stale,true),"Poke_SdRejectsConcurrentConfigEdit");check(File.ReadAllText(stale.Destination)=="external edit","Poke_SdConcurrentEditNotOverwritten");
            reject(()=>PokeFeurSdInstall.Preview(source,text,source),"Poke_SdRejectsOriginalExtractionAsDestination");
            string fake=Path.Combine(temp,"package/exefs");Directory.CreateDirectory(fake);File.WriteAllBytes(Path.Combine(fake,"subsdk9"),new byte[256]);reject(()=>PokeFeurPackage.Read(Path.GetDirectoryName(fake)!),"Poke_PackageRejectsUnpinnedRuntime");
        }
        finally{Directory.Delete(temp,true);}
    }
    internal static void RunUi(Action<bool,string> check)
    {
        string temp=Path.Combine(Path.GetTempPath(),"dr-poke-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        var project=SwordShieldProject.Open(SwordShieldChecks.Fixture(temp,"source"));var window=new ThreeDS.Progressive.SwordPokeFeurWindow(project);
        var messages=new StringBuilder();var listener=new System.Diagnostics.TextWriterTraceListener(new StringWriter(messages));var trace=System.Diagnostics.PresentationTraceSources.DataBindingSource;var previous=trace.Switch.Level;trace.Switch.Level=System.Diagnostics.SourceLevels.Error;trace.Listeners.Add(listener);
        try
        {
            check(window.FindName("PokeFeatureDexAnimations") is null&&window.FindName("PokeFeatureEarlyBike") is not null,"Poke_UIRetiredDexAbsentOtherFeaturesRemain");
            bool rejected=false;try{window.SetOptions(new PokeFeurOptions{Features=new[]{PokeFeurFeature.DexAnimations}});}catch(InvalidDataException){rejected=true;}check(rejected&&!project.HasChanges,"Poke_UIRetiredRecipeRejectedWithoutApplying");
            var provider=(System.Windows.Controls.TextBox)window.FindName("PokePackagePath");
            check(provider.IsReadOnly&&provider.Text.StartsWith("Integrado en la aplicación"),"Poke_UIRuntimeReadyWithoutImport");
            var apply=(System.Windows.Controls.Button)window.FindName("PokeApply");var install=(System.Windows.Controls.Button)window.FindName("PokeInstallSd");
            check(!apply.IsEnabled&&!install.IsEnabled,"Poke_UIRequiresPreviewAndApplyBeforeSdInstall");var seed=(System.Windows.Controls.TextBox)window.FindName("PokeSeed");check(System.Windows.Automation.AutomationProperties.GetLabeledBy(seed) is not null,"Poke_UISeedHasAssociatedLabel");
            check(((System.Windows.Controls.TextBox)window.FindName("PokeDetails")).IsReadOnly,"Poke_UIProposalIsReadOnly");window.SetOptions(new PokeFeurOptions{Caps=true,ExpMultiplier=2.5});check(window.Options().Caps&&window.Options().ExpMultiplier==2.5&&!apply.IsEnabled,"Poke_UIRecipeLoadingDoesNotApply");
            var root=(System.Windows.Controls.Grid)window.Content;var tabs=root.Children.OfType<System.Windows.Controls.TabControl>().Single();check(tabs.Items.Count==5&&apply.IsTabStop&&install.IsTabStop,"Poke_UIGroupedTabsAndKeyboardActions");
            foreach(var size in new[]{new System.Windows.Size(600,450),new System.Windows.Size(1000,790)})
            {
                foreach(int tab in Enumerable.Range(0,5)){tabs.SelectedIndex=tab;root.Measure(size);root.Arrange(new System.Windows.Rect(size));root.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.DataBind);}
                var bounds=apply.TransformToAncestor(root).TransformBounds(new System.Windows.Rect(apply.RenderSize));
                check(apply.ActualWidth>0&&bounds.Left>=0&&bounds.Right<=root.RenderSize.Width+1&&bounds.Bottom<=root.RenderSize.Height+1,"Poke_UIFooterActionsStayInsideLayout_"+size.Width);
                check(!project.HasChanges,"Poke_UILayoutDoesNotApply_"+size.Width);
            }
            listener.Flush();check(!messages.ToString().Contains("Error:",StringComparison.Ordinal),"Poke_UIAllTabsHaveNoBindingErrors");
        }
        finally{trace.Listeners.Remove(listener);trace.Switch.Level=previous;listener.Dispose();window.Close();Directory.Delete(temp,true);}
    }
}
