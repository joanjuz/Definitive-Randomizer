using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

internal static class FullBatchRealChecks
{
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    internal static void Run(string output,IEnumerable<string> roots,bool tutors=false,bool statics=false,bool master=false,bool tutors7=false,bool marts7=false,bool marts6=false)
    {
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("El destino debe ser nuevo.");
        var sources=roots.Select(Path.GetFullPath).ToArray();
        if(sources.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=sources.Length)throw new IOException("Raíces repetidas.");
        foreach(string root in sources)if(output.Equals(root,StringComparison.OrdinalIgnoreCase)||output.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||root.StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("La salida debe estar separada de los originales.");
        Directory.CreateDirectory(output);
        foreach(string root in sources)
        {
            var project=GameProjectSession.Open(root,recoverInterruptedSave:false);var config=project.GameConfiguration;
            var files=Directory.GetFiles(config.RomFS,"*",SearchOption.AllDirectories).Concat(Directory.GetFiles(config.ExeFS,"*",SearchOption.AllDirectories)).ToArray();
            var hashes=files.ToDictionary(p=>p,Hash,StringComparer.OrdinalIgnoreCase);var inventory=Directory.GetFileSystemEntries(root,"*",SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);
            FileChange expected;List<FileChange>? expectedMany=null;GlobalTemplateDocument recipe;
            if(marts6)
            {
                if(project.Generation!=6)throw new IOException("Se requiere Gen6.");var editor=new GameMart6Service(project);var options=EditorValues.Flatten(editor.RandomOptions);options["UseChecks"]=1;options["Candies"]=1;options["EVItems"]=1;options["BanCapsule"]=1;
                var preview=editor.Preview(options,editor.Entries.Select(e=>e.Id).ToArray(),1);expectedMany=preview.Files.ToList();expected=expectedMany[0];
                recipe=NativeRecipeFactory.Create(project,d=>GlobalEditorTemplateBridge.Capture(d,editor,new(options,0),6),["Marts"]);if(!NativeRecipeCompletion.TryComplete(project,recipe))throw new IOException("Receta Gen6 incompleta.");
            }
            else if(marts7)
            {
                if(project.Generation!=7)throw new IOException("La prueba de tiendas requiere SM o USUM.");
                var state=new Gen7MartState(Candies:true,Vitamins:true,FreeMega:true);var editor=new GameMartService(project);var preview=NativeGen7MartBridge.Prepare(editor,state,1);string shop=Path.Combine(config.RomFS,"Shop.cro");expected=new FileChange(shop,preview.Original,preview.Output);expectedMany=[expected];if(preview.ItemOriginal is not null&&preview.ItemOutput is not null)expectedMany.Add(new FileChange(Path.Combine(config.RomFS,config.GetGARCFileName("item")),preview.ItemOriginal,preview.ItemOutput));
                recipe=GlobalTemplateDocument.Empty(project.GameName,7);recipe.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});NativeGen7MartBridge.Capture(recipe,state);if(!NativeRecipeCompletion.TryComplete(project,recipe))throw new IOException("La receta nativa de tiendas no quedó completa.");
                var randomState=new Gen7MartState(RandomRegular:true,RandomBP:true,BanCapsule:true,SpecialsOnly:true,ProtectX:false);var a=NativeGen7MartBridge.Prepare(new GameMartService(project),randomState,765);var b=NativeGen7MartBridge.Prepare(new GameMartService(project),randomState,765);if(!a.Output.SequenceEqual(b.Output))throw new IOException("La semilla de tiendas no es reproducible.");Console.WriteLine($"PASS: {config.Version} shop randomization, BP, capsule ban and filters reproduce exact seed on detached bytes.");
            }
            else if(tutors7)
            {
                if(!config.USUM)throw new IOException("La prueba requiere USUM.");
                var editor=new GameMachineTutorService(project);foreach(var row in editor.Tutors)row.Price=0;var preview=editor.PreviewTutorEdits();string shop=Path.Combine(config.RomFS,"Shop.cro");expected=new FileChange(shop,preview.ShopOriginal,preview.ShopOutput);
                recipe=GlobalTemplateDocument.Empty(project.GameName,7);recipe.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});NativeGen7MachineBridge.Capture(recipe,new(FreeTutors:true));if(!NativeRecipeCompletion.TryComplete(project,recipe))throw new IOException("La receta nativa de tutores no quedó completa.");
            }
            else if(master)
            {
                string directory=Path.Combine(output,Path.GetFileName(root));Directory.CreateDirectory(directory);string csv=Path.Combine(directory,"custom-moves.csv");File.WriteAllText(csv,"Move,Power,Accuracy,PP\n1,99,88,12\n");
                var personalSettings=new BatchSettings{QuickHatch=true,SetCatch=true,CatchValue=234,ScaleExperience=true,ExperiencePercent=125,FullTms=true,FullHms=project.Generation==6};
                var personal=project.PreviewPersonal(BatchAction.PersonalModify,personalSettings,project.Entries.Select(row=>row.Index).ToArray());string personalPath=Path.Combine(config.RomFS,config.GetGARCFileName("personal"));byte[] sourcePersonal=File.ReadAllBytes(personalPath);
                expected=new FileChange(personalPath,sourcePersonal,GarcTransaction.Build(sourcePersonal,config.GetGARCReference("personal"),personal.CopyRecords()));
                var moves=new GameMoveService(project);var moveSettings=new BatchSettings{TemplatePath=csv,BattlePatches=false,MoveDescriptions=false};var movePreview=moves.PreviewMoves(BatchAction.MoveBalance,moveSettings,moves.Entries.Select(row=>row.Id).ToArray());string movePath=Path.Combine(config.RomFS,config.GetGARCFileName("move"));byte[] sourceMoves=File.ReadAllBytes(movePath);
                expectedMany=[expected,new FileChange(movePath,sourceMoves,GarcTransaction.Build(sourceMoves,config.GetGARCReference("move"),movePreview.CopyRecords(),mini:!config.XY))];
                recipe=GlobalTemplateDocument.Empty(project.GameName,project.Generation);recipe.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});NativeMasterBatchBridge.Capture(recipe,new(BatchAction.PersonalModify,personalSettings,0),project.Generation);NativeMasterBatchBridge.Capture(recipe,new(BatchAction.MoveBalance,moveSettings,0),project.Generation);recipe.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="moves.balance",["Parameters"]=new JsonObject()},new JsonObject{["Id"]="personal.modify-all",["Parameters"]=new JsonObject()}));
                if(!NativeRecipeCompletion.TryComplete(project,recipe))throw new IOException("La receta nativa combinada no quedó completa.");
            }
            else if(statics)
            {
                if(project.Generation!=7)throw new IOException("El diagnóstico de estáticos requiere Gen7.");
                var editor=new GameStaticEncounterService(project);var options=EditorValues.Flatten(editor.RandomOptions);options["Species"]=0;options["Gifts"]=0;options["Static"]=1;options["Trades"]=0;options["ScaleLevels"]=1;options["LevelScale"]=100;options["LevelFlat"]=5;
                expected=editor.Preview(options,editor.Entries.Select(row=>row.Id).ToArray(),1).Files[0];
                recipe=GlobalTemplateDocument.Empty(project.GameName,7);recipe.SetSection("ProgressiveApp",new JsonObject{["PartialSnapshot"]=true});GlobalEditorTemplateBridge.Capture(recipe,editor,new ThreeDS.Progressive.RecordEditorView.UiState(options,0),7);recipe.SetSection("Actions",new JsonArray(new JsonObject{["Id"]="static-encounters.modify-levels",["Parameters"]=new JsonObject()}));
                if(!NativeRecipeCompletion.TryComplete(project,recipe))throw new IOException("La receta de estáticos no quedó completa.");
            }
            else if(tutors)
            {
                var editor=new GameTutor6Service(project);var options=EditorValues.Flatten(editor.RandomOptions);options["Moves"]=0;options["Prices"]=1;
                expected=editor.Preview(options,editor.Entries.Select(row=>row.Id).ToArray(),1).Files[0];
                recipe=NativeRecipeFactory.Create(project,document=>GlobalEditorTemplateBridge.Capture(document,editor,new ThreeDS.Progressive.RecordEditorView.UiState(options,0),6),modules:["Tutors"]);
                if(!NativeRecipeCompletion.TryComplete(project,recipe))throw new IOException("La receta de tutores no quedó completa.");
            }
            else
            {
                expected=new GameShinyRateService(project).Preview(333,true).File;
                recipe=GlobalTemplateDocument.Empty(project.GameName,project.Generation);ShinyRateTemplate.Capture(recipe,new(333,true));
                if(project.Generation==7)recipe.SetSection("RandSettings",new JsonArray("ShinyRate","NUD_Rerolls\t333","CHK_EverythingShiny\tTrue",""));
            }
            string game=Path.Combine(output,Path.GetFileName(root));Directory.CreateDirectory(game);
            string template=Path.Combine(game,"recipe.json");recipe.Save(template);
            var runner=new BatchRomExecution(project);
            var journal=runner.RunAsync(new RomBatchOptions(template,game,"Integration",1,true,false,false,false),snapshot=>Console.WriteLine($"{Path.GetFileName(root)}: {snapshot.Stage}")).GetAwaiter().GetResult();
            if(journal.Roms.Count!=1)throw new IOException("El lote no produjo exactamente una ROM.");
            string built=Path.Combine(game,"Integration_01.3ds"),extracted=Path.Combine(game,"Roundtrip");
            new GameRomExtractionService(root).ExtractAsync(built,extracted).GetAwaiter().GetResult();
            foreach(var (file,hash) in hashes)
            {
                bool rom=file.StartsWith(config.RomFS+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
                string relative=Path.GetRelativePath(rom?config.RomFS:config.ExeFS,file);if(!rom&&relative is "code.bin" or ".code.bin")relative=".code.bin";
                string recovered=Path.Combine(extracted,rom?"RomFS":"ExeFS",relative);
                var proposed=(expectedMany??[expected]).FirstOrDefault(change=>file.Equals(change.Path,StringComparison.OrdinalIgnoreCase));
                if(proposed is not null)
                {if(!File.ReadAllBytes(recovered).SequenceEqual(proposed.Proposed()))throw new IOException("El lote real no coincide con el parche manual.");}
                else if(Hash(recovered)!=hash)throw new IOException("El lote cambió un archivo ajeno al parche: "+file);
                if(Hash(file)!=hash)throw new IOException("Original cambiado: "+file);
            }
            if(!inventory.SetEquals(Directory.GetFileSystemEntries(root,"*",SearchOption.AllDirectories)))throw new IOException("Inventario original cambiado.");
            Console.WriteLine($"PASS: {Path.GetFileName(root)} WPF preflight -> original process -> native/original replay -> Trimmed3DS -> extraction; exact manual patch; {files.Length} source hashes and original inventory intact.");
        }
    }
}
