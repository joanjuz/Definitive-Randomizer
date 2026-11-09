using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed record UsumMintPreview(UsumMintState State,string Report,EditorProposal? Proposal);

/// <summary>Only prepares detached buffers; ProjectHistory owns every game-file write.</summary>
internal sealed class UsumNatureMintService(GameProjectSession project,Func<byte[],UsumNatureMintProfile>? identifyCode=null)
{
    internal const string Warning="Estas mentas modifican la naturaleza real del Pokémon. No funcionan como las mentas de generaciones posteriores, que mantienen la naturaleza original y modifican sus efectos estadísticos";
    private readonly Guid _owner=Guid.NewGuid();
    internal UsumMintPreview Inspect()
    {
        try{return Prepare();}
        catch(Exception ex) when(ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {return new(UsumMintState.Incompatible,ex.Message,null);}
    }
    private UsumMintPreview Prepare(string? trialRoot=null)
    {
        var config=project.GameConfiguration;
        if(!config.USUM)throw new NotSupportedException("Mentas Gen7 únicamente admite Ultra Sol y Ultra Luna.");
        var dependencies=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var snapshots=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        string TrialPath(string path)=>Path.Combine(trialRoot!,Path.GetRelativePath(project.ProjectRoot,path));
        byte[] Read(string path){path=Path.GetFullPath(path);ValidatePath(path);byte[] bytes=File.ReadAllBytes(trialRoot is null?path:TrialPath(path));dependencies.Add(path,SHA256.HashData(bytes));snapshots.Add(path,bytes);return bytes;}
        var codes=Directory.GetFiles(config.ExeFS).Where(p=>Path.GetFileName(p) is "code.bin" or ".code.bin").ToArray();
        if(codes.Length!=1)throw new InvalidDataException("Se requiere un único code.bin descomprimido.");
        byte[] code=Read(codes[0]);
        // NCCH selects US/UM. Only the mint native call surface is accredited;
        // unrelated executable bytes are neither normalized nor fingerprinted.
        string header=new[]{"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"}.Select(name=>Path.Combine(project.ProjectRoot,name)).FirstOrDefault(File.Exists)
            ??throw new InvalidDataException("Falta la cabecera NCCH del proyecto para acreditar el título.");
        byte[] ncch=Read(header);
        if(ncch.Length<0x160||!ncch.AsSpan(0x100,4).SequenceEqual("NCCH"u8)||BitConverter.ToUInt64(ncch,0x118) is not (0x00040000001b5000UL or 0x00040000001b5100UL))
            throw new InvalidDataException("La identidad NCCH no corresponde a Ultra Sol/Ultra Luna.");
        bool moon=BitConverter.ToUInt64(ncch,0x118)==0x00040000001b5100UL;
        var profile=identifyCode is null?UsumNatureMintCatalog.Profiles.Single(p=>p.Moon==moon):identifyCode(code);
        if(profile.Moon!=moon)throw new InvalidDataException("La identidad NCCH no coincide con el perfil de Ultra Sol/Ultra Luna.");
        if(identifyCode is null&&code.Length!=5914624)throw new InvalidDataException("Se requiere code.bin USUM descomprimido.");
        string exheader=new[]{"exheader.bin","DecryptedExHeader.bin"}.Select(name=>Path.Combine(project.ProjectRoot,name)).FirstOrDefault(File.Exists)
            ??throw new InvalidDataException("Falta el exheader para comprobar las direcciones del runtime.");
        UsumMintCodeDataProfile.ValidateMemoryLayout(Read(exheader),moon);
        if(identifyCode is null)UsumMintCodeDataProfile.ValidateNativeDependencies(code,moon);
        string bagPath=Path.Combine(config.RomFS,"Bag.cro");byte[] bag=Read(bagPath);
        var state=UsumNatureMintPatch.Inspect(bag,profile);
        if(state.State is UsumMintState.Incompatible or UsumMintState.Modified)return new(state.State,state.Detail,null);
        bool installed=state.State==UsumMintState.Installed;
        var files=new List<FileChange>();
        string itemPath=Path.Combine(config.RomFS,config.GetGARCFileName("item"));byte[] itemSource=Read(itemPath);
        var items=new GARC.MemGARC(itemSource).Files;
        if(items.Length!=960)throw new InvalidDataException("La tabla USUM de objetos no tiene 960 entradas.");
        foreach(var mint in UsumNatureMintCatalog.Mints){
            var record=items[mint.ItemId];
            if(installed){if(!record.AsSpan(2).SequenceEqual(ItemRecord().AsSpan(2)))return Modified("Propiedades de la menta #"+mint.ItemId+" modificadas externamente.");}
            else if(Convert.ToHexString(SHA256.HashData(record))!=UsumNatureMintCatalog.OriginalItemHash(mint.ItemId))
                throw new InvalidDataException("El objeto #"+mint.ItemId+" no conserva su registro vacío acreditado; no se sobrescribe.");
            if(!installed)items[mint.ItemId]=ItemRecord();
        }
        if(!installed)files.Add(new(itemPath,itemSource,GarcTransaction.Build(itemSource,config.GetGARCReference("item"),items)));
        string pocketPath=Path.Combine(config.RomFS,"a/1/0/3");byte[] pocketSource=Read(pocketPath);
        var pocketArchive=new GARC.MemGARC(pocketSource);var pockets=pocketArchive.Files;
        if(pockets.Length<1||pockets[0].Length!=480)throw new InvalidDataException("Falta la tabla de bolsillos USUM (480 bytes).");
        foreach(var mint in UsumNatureMintCatalog.Mints){
            int pocket=UsumItemPocketTable.Read(pockets[0],mint.ItemId);
            if(installed&&pocket!=1)return Modified("Bolsillo de una menta modificado externamente.");
            if(!installed&&pocket!=(mint.ItemId>=622?4:0))throw new InvalidDataException("El bolsillo del objeto #"+mint.ItemId+" no conserva su valor acreditado.");
            UsumItemPocketTable.Write(pockets[0],mint.ItemId,1);
        }
        if(!installed){var lazy=new GARC.LazyGARC(pocketSource);lazy[0]=pockets[0];byte[] output=lazy.Save();
            var verify=new GARC.MemGARC(output).Files;if(verify.Length!=pockets.Length||verify.Where((r,i)=>!r.AsSpan().SequenceEqual(pockets[i])).Any())throw new InvalidDataException("Relectura de bolsillos incorrecta.");
            files.Add(new(pocketPath,pocketSource,output));}
        // Every language receives its own names and appended message; unrelated banks remain opaque.
        for(int language=0;language<10;language++){
            var reference=config.GetGARCReference("gametext").GetRelativeGARC(language);
            string path=Path.Combine(config.RomFS,reference.Reference);byte[] source=Read(path);
            var records=new GARC.MemGARC(source).Files;
            if(records.Length<=92)throw new InvalidDataException("Banco de textos USUM incompleto.");
            string[] natures=new TextFile(config,records[92],config.RemapCharacters).Lines;
            foreach(int bank in new[]{39,40,41,42}){
                string[] lines=new TextFile(config,records[bank],config.RemapCharacters).Lines;
                if(lines.Length!=960||natures.Length!=26)throw new InvalidDataException("Textos de objetos/naturalezas fuera del perfil USUM.");
                if(!installed){
                    string key=language+":"+bank;
                    string hash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\0",UsumNatureMintCatalog.Mints.Select(m=>lines[m.ItemId])))));
                    if(!profile.TextSlotHashes.TryGetValue(key,out string? expectedHash)||hash!=expectedHash)
                        throw new InvalidDataException("Las entradas de texto reservadas cambiaron: idioma "+language+", banco "+bank+". No se sobrescriben textos desconocidos.");
                }
                foreach(var mint in UsumNatureMintCatalog.Mints){
                    string expected=bank==39?Description(mint,language):Name(mint,natures,language,bank);
                    if(installed){if(lines[mint.ItemId]!=expected)return Modified("Texto de menta modificado: idioma "+language+", banco "+bank);}
                    else {
                        lines[mint.ItemId]=expected;
                    }
                }
                if(!installed)records[bank]=UsumMintTextRows.Patch(config,records[bank],lines);
            }
            var messages=new TextFile(config,records[1],config.RemapCharacters).Lines;
            string message=language==6?"¡Cambió la naturaleza de\\n[VAR PKNICK(0000)]!":"[VAR PKNICK(0000)]'s Nature changed!";
            if(installed){if(messages.Length!=160||messages[159]!=message)return Modified("Mensaje de bolsa modificado externamente.");}
            else {
                if(messages.Length!=159)throw new InvalidDataException("El mensaje 159 de bolsa ya está ocupado; no se sobrescribe.");
                records[1]=UsumMintTextRows.Patch(config,records[1],[..messages,message]);
                files.Add(new(path,source,GarcTransaction.Build(source,reference,records,preserveRecordSize:false)));
            }
        }
        var outputBag=installed?bag:UsumNatureMintPatch.Prepare(bag,profile);
        if(!installed)files.Add(new(bagPath,bag,outputBag));
        string[] inventory=Directory.GetFiles(config.RomFS,"*.cro").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(string path in inventory.Where(path=>!path.Equals(bagPath,StringComparison.OrdinalIgnoreCase)))Read(path);
        string crrPath=Path.Combine(config.RomFS,".crr/static.crr");byte[] crr=Read(crrPath);
        byte[] updatedCrr=ReplaceCrrBag(crr,bag,outputBag,inventory.Length);
        // Bag's relocated structure/hashes are checked by UsumNatureMintPatch.
        // Other CROs stay opaque and unchanged; CRR must accredit their headers.
        if(!installed||trialRoot is not null){
            var images=inventory.Select(path=>snapshots[Path.GetFullPath(path)]).ToArray();
            if(!CroIntegrity.RebuildCrr(crr,images).AsSpan().SequenceEqual(crr))throw new InvalidDataException("La tabla CRR no acredita el inventario CRO completo.");
        }
        if(installed){
            var verified=(identifyCode??UsumNatureMintCatalog.IdentifyCode)(code);
            if(verified.Moon!=moon)throw new InvalidDataException("El ejecutable no corresponde a la identidad NCCH.");
            return new(UsumMintState.Installed,profile.Id+" · "+state.Detail+"\nLos precios son editables. Abre Tiendas y BP para añadirlas.",null);
        }
        if(trialRoot is not null)throw new InvalidDataException("La copia temporal no reconoce las 21 mentas instaladas.");
        files.Add(new(crrPath,crr,updatedCrr));
        var parameters=new Dictionary<string,string>{{"ActionId","usum.nature-mints.install"},{"Profile",profile.Id},{"Semantics","RealNature"},{"Phase","1"}};
        var proposal=new EditorProposal(_owner,"Instalar mentas Gen7",0,files,
            UsumNatureMintCatalog.Mints.Select(m=>new BatchChange(m.ItemId,"Menta "+m.EnglishNature,"Naturaleza real","Sin menta",m.NatureId.ToString())),parameters,
            dependencies.Select(d=>(d.Key,d.Value)));
        VerifyTrial(proposal,snapshots);
        return new(UsumMintState.NotInstalled,profile.Id+"\nInstalación y relectura verificadas en una copia temporal aislada. El proyecto aún no se ha modificado.\n21 mentas consumibles. Sin Ability Patch ni cambios en pools.\n"+
            string.Join("\n",files.Where(f=>f.Changed).Select(f=>Path.GetRelativePath(project.ProjectRoot,f.Path))),proposal);
    }
    private void VerifyTrial(EditorProposal proposal,IReadOnlyDictionary<string,byte[]> snapshots)
    {
        // Only files read by this installer are copied. No history or source project
        // is opened for writing. The same transaction primitive applies the trial.
        string trial=Path.Combine(Path.GetTempPath(),"DefinitiveRandomizer-Mints-"+Guid.NewGuid().ToString("N"));
        string Map(string path)=>Path.Combine(trial,Path.GetRelativePath(project.ProjectRoot,path));
        Directory.CreateDirectory(trial);
        try {
            foreach(var (path,bytes) in snapshots){string target=Map(path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllBytes(target,bytes);}
            FileTransaction.Commit(proposal.Files.Select(f=>new FileChange(Map(f.Path),f.Original(),f.Proposed())).ToArray());
            var verified=Prepare(trial);
            if(verified.State!=UsumMintState.Installed)throw new InvalidDataException("La instalación temporal no supera la relectura: "+verified.Report);
            foreach(var (path,bytes) in snapshots){
                var changed=proposal.Files.SingleOrDefault(f=>f.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
                if(!File.ReadAllBytes(Map(path)).AsSpan().SequenceEqual(changed is null?bytes:changed.Proposed()))
                    throw new InvalidDataException("La instalación temporal modificó un archivo ajeno: "+Path.GetRelativePath(project.ProjectRoot,path));
            }
            foreach(var dependency in proposal.Dependencies)GarcTransaction.ReadUnchanged(dependency.Path,dependency.Fingerprint);
        } finally{Directory.Delete(trial,true);}
    }
    private static UsumMintPreview Modified(string reason)=>new(UsumMintState.Modified,reason+" No se reinstala; revisa el historial o restaura una copia coherente.",null);
    internal void Apply(UsumMintPreview preview)
    {
        var proposal=preview.Proposal??throw new InvalidOperationException("No hay una instalación preparada. "+preview.Report);
        if(proposal.Owner!=_owner||preview.State!=UsumMintState.NotInstalled)throw new InvalidOperationException("Vista previa ajena o no instalable.");
        // Freeze the unchanged dependencies during the existing history transaction.
        var locks=new List<FileStream>();
        try {
            var expectedCros=proposal.Dependencies.Select(d=>d.Path).Where(p=>Path.GetExtension(p).Equals(".cro",StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase);
            if(!expectedCros.SequenceEqual(Directory.GetFiles(project.GameConfiguration.RomFS,"*.cro").Order(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase))
                throw new IOException("Cambió el inventario CRO después de la vista previa.");
            foreach(var dependency in proposal.Dependencies){
                ValidatePath(dependency.Path);
                if(proposal.Files.Any(f=>f.Path.Equals(dependency.Path,StringComparison.OrdinalIgnoreCase))) {
                    GarcTransaction.ReadUnchanged(dependency.Path,dependency.Fingerprint);continue;
                }
                var stream=new FileStream(dependency.Path,FileMode.Open,FileAccess.Read,FileShare.Read);locks.Add(stream);
                if(!SHA256.HashData(stream).AsSpan().SequenceEqual(dependency.Fingerprint))throw new IOException("Una dependencia cambió después de la vista previa.");
            }
            project.History.Commit(proposal.Files,"Mecánicas/CRO",proposal.Action,proposal.Changes,proposal.Parameters,section:"UsumNatureMints");
        } finally{foreach(var stream in locks)stream.Dispose();}
        project.RefreshItemNames();
    }
    private void ValidatePath(string path)
    {
        string root=Path.GetFullPath(project.ProjectRoot),full=Path.GetFullPath(path);
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Archivo fuera del proyecto.");
        for(string? at=full;at is not null&&at.Length>=root.Length;at=Path.GetDirectoryName(at))
            if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("No se permiten enlaces en los archivos de instalación.");
    }
    internal static byte[] ItemRecord()
    {
        var item=new Item(new byte[36]){Packed=0x9f,EffectField=1,SortIndex=52,BuyPrice=20000,UseConsume=true,UseKeep=false};return item.Write();
    }
    private static byte[] ReplaceCrrBag(byte[] original,byte[] before,byte[] after,int croCount)
    {
        if(original.Length<0x358)throw new InvalidDataException("CRR incompleto.");
        int start=BitConverter.ToInt32(original,0x350),count=BitConverter.ToInt32(original,0x354);
        if(count!=croCount||count<1||start<0x358||(long)start+32L*count>original.Length)throw new InvalidDataException("Inventario CRR/CRO incompatible.");
        string oldHash=Convert.ToHexString(SHA256.HashData(before.AsSpan(0,0x80)));
        string newHash=Convert.ToHexString(SHA256.HashData(after.AsSpan(0,0x80)));
        var hashes=Enumerable.Range(0,count).Select(i=>Convert.ToHexString(original.AsSpan(start+32*i,32))).ToArray();
        if(hashes.Count(hash=>hash==oldHash)!=1)throw new InvalidDataException("CRR no acredita exactamente una entrada para Bag.cro; reconstruye su integridad antes de instalar.");
        byte[] result=(byte[])original.Clone();hashes[Array.IndexOf(hashes,oldHash)]=newHash;
        var ordered=hashes.Order(StringComparer.Ordinal).ToArray();
        for(int i=0;i<count;i++)Convert.FromHexString(ordered[i]).CopyTo(result,start+32*i);
        return result;
    }
    private static string Name(UsumNatureMint mint,string[] natures,int language,int bank)
    {
        string name=language==6?"Menta "+natures[mint.NatureId]:mint.EnglishNature+" Mint";
        if(bank==41)return language==6?"Mentas "+natures[mint.NatureId]:name+"s";
        if(bank==42)return name+(language==6?"":"[VAR NUMBRNCH(00FE,0100)]s");return name;
    }
    private static string Description(UsumNatureMint mint,int language)
    {
        string[] stats=language==6?["Ataque","Defensa","Velocidad","At. Esp.","Def. Esp."]:["Attack","Defense","Speed","Sp. Atk","Sp. Def"];
        string effects=mint.NatureId==12?(language==6?"Crecimiento de estadísticas neutro.":"Neutral growth of stats."):
            "+"+stats[mint.NatureId/5]+" / -"+stats[mint.NatureId%5]+".";
        return (language==6?"Cambia la naturaleza real.":"Changes the real Nature.")+"\\n"+effects+"\\n"+
            (language==6?"Se consume si tiene efecto.":"Consumed only when effective.");
    }
}
