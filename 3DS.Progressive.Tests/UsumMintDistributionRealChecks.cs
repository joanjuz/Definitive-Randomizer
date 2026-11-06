using System.IO;
using System.Text.Json.Nodes;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;

internal static class UsumMintDistributionRealChecks
{
    internal static async Task Run(string output,string sun,string moon,Action<bool,string> check)
    {
        using var state=new TestState();output=Path.GetFullPath(output);
        if(Directory.Exists(output))throw new IOException("Use a new private output directory.");
        foreach(string input in new[]{sun,moon})if(output.StartsWith(Path.GetFullPath(input)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Output must be outside the input projects.");
        Directory.CreateDirectory(output);
        foreach(var (source,name) in new[]{(sun,"US"),(moon,"UM")})
        {
            string root=Path.Combine(output,name);Copy(source,root);
            var project=GameProjectSession.Open(root,false);
            check(new UsumNatureMintService(project).Inspect().State==UsumMintState.Installed&&new UsumMintIconService(project).Inspect().State==UsumMintState.Installed,"MintDistributionRealRecognizedFunctionalAndIcons"+name);
            var before=GlobalTemplateApplication.Snapshot(project);
            var marts=new GameMartService(project);var bp=marts.BpShops.SelectMany(s=>s.Rows).Select(r=>(r.ItemId,r.Price)).ToArray();
            var rates=new GamePickupService(project).Read().Select(r=>r.Rates).ToArray();
            project.MintItems.Include=true;
            var recipe=NativeRecipeFactory.Create(project,modules:["Marts","FieldItems","Machines"]);
            NativeGen7MartBridge.Capture(recipe,new(RandomRegular:true,AddMints:true));
            ShinyRateTemplate.Capture(recipe,new(250,false));
            var actions=recipe.Section("Actions")!.AsArray();actions.Add(new JsonObject{["Id"]="pickup.randomize",["Parameters"]=new JsonObject()});recipe.SetSection("Actions",actions);
            check(NativeRecipeCompletion.TryComplete(project,recipe),"MintDistributionRealCombinedRecipeComplete"+name);
            string recipePath=Path.Combine(root,"phase2-validation.json");recipe.Save(recipePath);
            var application=new GlobalTemplateApplication(project);
            using(var preview=await application.PreviewAsync(recipe))
            {
                check(before.All(p=>ProjectHistory.Hash(File.ReadAllBytes(Path.Combine(root,p.Key)))==p.Value),"MintDistributionRealBatchPreviewSourceUnchanged"+name);
                application.Apply(preview);
            }
            var reopened=GameProjectSession.Open(root,false);marts=new(reopened);
            check(marts.RegularShops.Take(8).All(s=>UsumNatureMintCatalog.Mints.All(m=>s.Rows.Any(r=>r.ItemId==m.ItemId))),"MintDistributionRealAllEightStocksPersist"+name);
            check(marts.BpShops.SelectMany(s=>s.Rows).Select(r=>(r.ItemId,r.Price)).SequenceEqual(bp),"MintDistributionRealBPAndPricesPreserved"+name);
            check(new GamePickupService(reopened).Read().Select((r,i)=>r.Rates.SequenceEqual(rates[i])).All(x=>x),"MintDistributionRealPickupRatesPreserved"+name);
            check(new UsumNatureMintService(reopened).Inspect().State==UsumMintState.Installed&&new UsumMintIconService(reopened).Inspect().State==UsumMintState.Installed,"MintDistributionRealTMShinyAndIconsRemainCompatible"+name);
            string iconRelative=Path.GetRelativePath(root,Path.Combine(reopened.GameConfiguration.RomFS,UsumMintIconLayout.ArchivePath));
            string itemsRelative=Path.GetRelativePath(root,Path.Combine(reopened.GameConfiguration.RomFS,reopened.GameConfiguration.GetGARCFileName("item")));
            check(ProjectHistory.Hash(File.ReadAllBytes(Path.Combine(root,iconRelative)))==before[iconRelative]&&ProjectHistory.Hash(File.ReadAllBytes(Path.Combine(root,itemsRelative)))==before[itemsRelative],"MintDistributionRealIconsAndItemPricesExact"+name);
            check(before.All(p=>ProjectHistory.Hash(File.ReadAllBytes(Path.Combine(source,p.Key)))==p.Value),"MintDistributionRealOriginalInputsUntouched"+name);
            File.WriteAllText(Path.Combine(output,name+"-validation.txt"),"PASS: real private copy; Global/Batch engine + TM + Shiny + shops + EI/EB + Pickup; reopen; eight stocks; BP/prices/icons unchanged. Gameplay NOT tested.\n"+recipe.ToJson());
        }
        static void Copy(string source,string target)
        {
            if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("Do not copy links.");Directory.CreateDirectory(target);
            foreach(string file in Directory.GetFiles(source))
            {if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Do not copy links.");File.Copy(file,Path.Combine(target,Path.GetFileName(file)));}
            foreach(string child in Directory.GetDirectories(source))Copy(child,Path.Combine(target,Path.GetFileName(child)));
        }
    }
}
