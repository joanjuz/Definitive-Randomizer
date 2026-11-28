using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed record Gen6MintPreview(Gen6MintNativeState State,string Report,EditorProposal? Proposal);

/// <summary>Experimental XY installer only. Production distribution and WPF remain gated.
/// Captures detached buffers, verifies immutable members/rows, then commits through history.</summary>
internal sealed class Gen6NatureMintService(GameProjectSession project,bool allowExperimental=false,
    Func<ulong,JsonElement>? resolveProfile=null)
{
    private readonly Guid _owner=Guid.NewGuid();
    internal const string ActionId="xy.nature-mints.experimental.install";
    private static readonly int[] Banks=[95,96,97,98,99,140];
    private static readonly HashSet<int> Ids=Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).ToHashSet();
    private static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    internal Gen6MintPreview Inspect()
    {
        try{return Prepare();}
        catch(Exception ex) when(ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {return new(Gen6MintNativeState.Incompatible,ex.Message,null);}
    }
    internal static JsonElement Profile(ulong title)
    {
        using var document=JsonDocument.Parse(typeof(Gen6NatureMintService).Assembly.GetManifestResourceStream("ThreeDS.Progressive.Gen6MintObservedProfiles")
            ??throw new InvalidDataException("Faltan los perfiles XY-MINT."));
        return document.RootElement.GetProperty("titles").GetProperty(title.ToString("X16")).Clone();
    }
    private Gen6MintPreview Prepare()
    {
        if(!project.GameConfiguration.XY)throw new NotSupportedException("Mentas Gen6: solo Pokémon X/Y.");
        var config=project.GameConfiguration;
        var snapshots=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[] Read(string path)
        {
            path=Path.GetFullPath(path);CheckPath(path);
            if(!snapshots.TryGetValue(path,out byte[]? data))snapshots.Add(path,data=File.ReadAllBytes(path));
            return data;
        }
        string Header(params string[] names)
        {
            var paths=names.Select(n=>Path.Combine(project.ProjectRoot,n)).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(paths.Length!=1)throw new InvalidDataException("Falta una cabecera única: "+names[0]);return paths[0];
        }
        string ncchPath=Header("ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin");
        string exPath=Header("exheader.bin","DecryptedExHeader.bin","ExHeader.bin");
        byte[] ncch=Read(ncchPath),ex=Read(exPath);
        if(ncch.Length<0x120||!ncch.AsSpan(0x100,4).SequenceEqual("NCCH"u8)||ex.Length<0x208)
            throw new InvalidDataException("Cabeceras X/Y truncadas.");
        ulong title=BitConverter.ToUInt64(ncch,0x118);
        if(title is not (0x0004000000055D00UL or 0x0004000000055E00UL)||BitConverter.ToUInt64(ex,0x200)!=title)
            throw new InvalidDataException("Title ID cruzado o ajeno a X/Y.");
        JsonElement observed=(resolveProfile??Profile)(title),pins=observed.GetProperty("sha256"),native=observed.GetProperty("native");
        bool composed=resolveProfile is null;
        Gen6MintInstallation? admission=composed?Gen6MintRuntimeDependencies.Capture(project,requireInstalled:false):null;
        string PathOf(string relative)=>relative switch
        {
            "ncchheader.bin"=>ncchPath,"exheader.bin"=>exPath,
            "ExeFS/.code.bin"=>Gen6MintRuntimeDependencies.CodePath(project),
            _ when relative.StartsWith("RomFS/",StringComparison.Ordinal)=>Path.Combine(config.RomFS,relative[6..]),
            _ when relative.StartsWith("ExeFS/",StringComparison.Ordinal)=>Path.Combine(config.ExeFS,relative[6..]),
            _=>throw new InvalidDataException("Dependencia fuera del contrato XY.")
        };
        var names=Directory.GetFiles(config.RomFS).Select(Path.GetFileName).Where(n=>n!.Equals("DllBag.cro",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(names.Length!=1||names[0]!="DllBag.cro")throw new InvalidDataException("Se requiere el nombre físico único DllBag.cro.");
        string bagPath=PathOf("RomFS/DllBag.cro");byte[] bag=Read(bagPath);
        var profile=new Gen6MintNativeProfile("XY-"+title.ToString("X16")+"-experiment-v1",title,
            pins.GetProperty("ExeFS/.code.bin").GetString()!,pins.GetProperty("RomFS/DllBag.cro").GetString()!);
        var state=Gen6NatureMintPatch.Inspect(bag,profile);
        if(state.State is not (Gen6MintNativeState.Original or Gen6MintNativeState.Installed))return new(state.State,state.Report,null);
        bool installed=state.State==Gen6MintNativeState.Installed;
        foreach(var pin in pins.EnumerateObject())
        {
            byte[] data=Read(PathOf(pin.Name));
            byte[] accredited=data;
            bool knownShopCode=false;
            if(installed&&pin.Name=="ExeFS/.code.bin")
            {
                accredited=Gen6MintIconService.NormalizeCode(data);
                knownShopCode=native.TryGetProperty("experimental_shop_code_sha256",out var shop)&&Hash(accredited)==shop.GetString();
            }
            if(installed&&pin.Name=="RomFS/a/0/9/4")accredited=Gen6MintIconService.NormalizeArchive(data);
            bool mutable=pin.Name is "RomFS/DllBag.cro" or "RomFS/a/2/2/0" or "RomFS/.crr/static.crr"||
                Enumerable.Range(72,8).Any(n=>pin.Name==$"RomFS/a/0/{n/10}/{n%10}");
            if(!composed&&(!installed||!mutable)&&!knownShopCode&&Hash(accredited)!=pin.Value.GetString())throw new InvalidDataException("Dependencia modificada o cruzada: "+pin.Name);
        }
        if(installed&&pins.TryGetProperty("RomFS/a/0/9/4",out _))
        {
            byte[] code=Read(PathOf("ExeFS/.code.bin"));var iconRows=Gen6MintIconService.Records(Read(PathOf("RomFS/a/0/9/4")));
            bool iconsInstalled=Gen6MintIconService.Mapping(code,true);
            if(!iconsInstalled&&!Gen6MintIconService.Mapping(code,false)||iconsInstalled!=(iconRows.Length==Gen6MintIconService.InstalledCount))
                throw new InvalidDataException("Estado de iconos XY parcial o modificado.");
            if(iconsInstalled)
            {
                string[] expected=native.GetProperty("icon_texture_sha256").EnumerateArray().Select(v=>v.GetString()!).ToArray();
                if(expected.Length!=21||expected.Where((h,i)=>Hash(Gen6MintIconService.Decompress(iconRows[Gen6MintIconService.OriginalCount+i]))!=h).Any())
                    throw new InvalidDataException("Textura de menta XY modificada.");
            }
        }
        var files=new List<FileChange>();
        string itemPath=PathOf("RomFS/a/2/2/0");byte[] itemSource=Read(itemPath);
        var items=new GARC.MemGARC(itemSource).Files;
        if(items.Length!=718||items.Any(r=>r.Length!=36)||!composed&&DigestMembers(items,i=>!Ids.Contains(i))!=native.GetProperty("items_opaque_sha256").GetString())
            throw new InvalidDataException("Tabla de objetos Gen6 ajena al perfil.");
        foreach(var item in Gen6NatureMintCatalog.ExperimentalItemMap.Items)
        {
            if(installed)
            {
                if(!items[item.ItemId].AsSpan(2).SequenceEqual(ItemRecord().AsSpan(2)))throw new InvalidDataException("Propiedades de menta modificadas: "+item.ItemId);
            }
            else items[item.ItemId]=ItemRecord();
        }
        if(!installed)files.Add(new(itemPath,itemSource,GarcTransaction.Build(itemSource,config.GetGARCReference("item"),items)));
        for(int language=0;language<8;language++)
        {
            var reference=config.GetGARCReference("gametext").GetRelativeGARC(language);
            string path=Path.Combine(config.RomFS,reference.Reference);byte[] source=Read(path);
            byte[][] records=new GARC.MemGARC(source).Files;
            JsonElement textProfile=native.GetProperty("texts").GetProperty(language.ToString());
            if(records.Length<=140||!composed&&DigestMembers(records,i=>!Banks.Contains(i))!=textProfile.GetProperty("opaque_banks_sha256").GetString())
                throw new InvalidDataException("Bancos ajenos de texto modificados: idioma "+language);
            string[] natures=new TextFile(config,records[47],config.RemapCharacters).Lines;
            if(natures.Length!=26)throw new InvalidDataException("Banco de naturalezas XY incompatible.");
            foreach(int bank in Banks)
            {
                byte[] before=records[bank];var previous=new TextFile(config,before,config.RemapCharacters).Lines;
                int count=bank==140?179:718;
                if(previous.Length!=count+(bank==140&&installed?1:0))throw new InvalidDataException("Cantidad de filas incompatible: "+bank);
                var rowProfile=textProfile.GetProperty("banks").GetProperty(bank.ToString());
                if(!composed)ValidateRows(before,count,bank==140,rowProfile);
                string[] next=bank==140?[..previous.Take(179),Message(language)]:(string[])previous.Clone();
                if(bank!=140)foreach(var item in Gen6NatureMintCatalog.ExperimentalItemMap.Items)
                    next[item.ItemId]=bank==99?Description(item.NatureId,language):Name(item.NatureId,natures,language,bank);
                if(installed)
                {
                    if(!next.SequenceEqual(previous))throw new InvalidDataException("Texto de menta modificado: idioma "+language+", banco "+bank);
                }
                else
                {
                    // Shared text-envelope primitive only: no USUM slot, index or game profile is used.
                    records[bank]=UsumMintTextRows.Patch(config,before,next);
                    if(!composed)ValidateRows(records[bank],count,bank==140,rowProfile);
                }
            }
            if(!installed)files.Add(new(path,source,GarcTransaction.Build(source,reference,records,preserveRecordSize:false)));
        }
        byte[] outputBag=installed?bag:Gen6NatureMintPatch.Prepare(bag,profile);
        if(!installed)files.Add(new(bagPath,bag,outputBag));
        string[] inventory=Directory.GetFiles(config.RomFS).Where(p=>Path.GetExtension(p).Equals(".cro",StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        byte[][] images=inventory.Select(Read).ToArray();
        for(int i=0;i<images.Length;i++)
        {
            byte[] rebuilt=CroIntegrity.Rebuild(images[i]);
            if(composed&&!rebuilt.AsSpan().SequenceEqual(images[i])&&inventory[i]!=bagPath)files.Add(new(inventory[i],images[i],rebuilt));
            else if(!composed&&!rebuilt.AsSpan().SequenceEqual(images[i]))
                throw new InvalidDataException("Inventario CRO inconsistente.");
        }
        string crrPath=PathOf("RomFS/.crr/static.crr");byte[] crr=Read(crrPath);
        if(!composed&&!CroIntegrity.RebuildCrr(crr,images).AsSpan().SequenceEqual(crr))throw new InvalidDataException("CRR no acredita el inventario CRO completo.");
        if(composed)
        {
            var nextImages=inventory.Select(path=>files.FirstOrDefault(f=>f.Path.Equals(Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))?.Proposed()??Read(path)).ToArray();
            byte[] nextCrr=CroIntegrity.RebuildCrr(crr,nextImages);if(!nextCrr.AsSpan().SequenceEqual(crr))files.Add(new(crrPath,crr,nextCrr));
        }
        else if(installed)
        {
            byte[] normalized=ReplaceCrr(crr,Hash(bag.AsSpan(0,128).ToArray()),native.GetProperty("original_bag_header_sha256").GetString()!,inventory.Length);
            if(Hash(normalized)!=pins.GetProperty("RomFS/.crr/static.crr").GetString())throw new InvalidDataException("CRR modificado fuera del candidato.");
        }
        else files.Add(new(crrPath,crr,ReplaceCrr(crr,Hash(bag.AsSpan(0,128).ToArray()),Hash(outputBag.AsSpan(0,128).ToArray()),inventory.Length)));
        foreach(var (path,data) in snapshots)GarcTransaction.ReadUnchanged(path,SHA256.HashData(data));
        admission?.VerifyUnchanged();
        if(installed&&!files.Any(f=>f.Changed))return new(state.State,state.Report+"\n21 mentas verificadas; datos randomizados conservados.",null);
        if(!allowExperimental)return new(state.State,"Producción bloqueada; solo candidato aislado autorizado.",null);
        var proposal=new EditorProposal(_owner,"Instalar 21 mentas X/Y · candidato experimental",0,files,
            Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>new BatchChange(m.ItemId,"Menta "+m.NatureId,"Naturaleza real","Sin handler",m.NatureId.ToString())),
            new Dictionary<string,string>{{"ActionId",ActionId},{"Profile",profile.Id},{"ProductionEnabled","false"},{"Semantics","RealNature"}},
            snapshots.Select(p=>(p.Key,SHA256.HashData(p.Value))));
        return new(state.State,profile.Id+"\n21 mentas experimentales, banco de mensajes 140/179. No distribuye objetos ni toca guardados.\n"+
            string.Join("\n",files.Select(f=>Path.GetRelativePath(project.ProjectRoot,f.Path))),proposal);
    }
    internal void Apply(Gen6MintPreview preview)
    {
        if(!allowExperimental||preview.State is not (Gen6MintNativeState.Original or Gen6MintNativeState.Installed)||preview.Proposal is not {} offered||offered.Owner!=_owner)
            throw new InvalidOperationException("No hay una instalación experimental propia preparada.");
        // Recompute canonical output and commit that output, never caller-mutated proposal buffers.
        var current=Prepare().Proposal??throw new InvalidOperationException("La instalación ya no está disponible.");
        if(current.Files.Count!=offered.Files.Count||current.Files.Where((f,i)=>f.Path!=offered.Files[i].Path||
            !f.Original().AsSpan().SequenceEqual(offered.Files[i].Original())||!f.Proposed().AsSpan().SequenceEqual(offered.Files[i].Proposed())).Any()||
            current.Action!=offered.Action||current.Seed!=offered.Seed||
            current.Dependencies.Count!=offered.Dependencies.Count||current.Dependencies.Where((d,i)=>d.Path!=offered.Dependencies[i].Path||
                !d.Fingerprint.AsSpan().SequenceEqual(offered.Dependencies[i].Fingerprint)).Any()||
            !current.Parameters.OrderBy(p=>p.Key).SequenceEqual(offered.Parameters.OrderBy(p=>p.Key))||!current.Changes.SequenceEqual(offered.Changes))
            throw new InvalidDataException("Vista previa obsoleta o alterada.");
        var locks=new List<FileStream>();
        try
        {
            foreach(var dependency in current.Dependencies)
            {
                CheckPath(dependency.Path);
                if(current.Files.Any(f=>f.Path.Equals(dependency.Path,StringComparison.OrdinalIgnoreCase)))
                {GarcTransaction.ReadUnchanged(dependency.Path,dependency.Fingerprint);continue;}
                var stream=new FileStream(dependency.Path,FileMode.Open,FileAccess.Read,FileShare.Read);locks.Add(stream);
                if(!SHA256.HashData(stream).AsSpan().SequenceEqual(dependency.Fingerprint))throw new IOException("Cambió una dependencia del candidato.");
            }
            string[] expected=current.Dependencies.Select(d=>d.Path).Where(p=>Path.GetExtension(p).Equals(".cro",StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
            if(!expected.SequenceEqual(Directory.GetFiles(project.GameConfiguration.RomFS).Where(p=>Path.GetExtension(p).Equals(".cro",StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)))
                throw new IOException("Cambió el inventario CRO.");
            project.History.Commit(current.Files,"Mecánicas/CRO",current.Action,current.Changes,current.Parameters,section:"Gen6NatureMints");
        }
        finally{foreach(var stream in locks)stream.Dispose();}
        project.RefreshItemNames();
        if(Inspect().State!=Gen6MintNativeState.Installed)throw new InvalidDataException("La relectura no reconoce el candidato instalado.");
    }
    private void CheckPath(string path)
    {
        string root=Path.GetFullPath(project.ProjectRoot),full=Path.GetFullPath(path);
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Dependencia fuera del proyecto.");
        for(string? at=full;at is not null;at=Path.GetDirectoryName(at))
        {if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Enlaces no permitidos en el candidato.");}
    }
    internal static byte[] ItemRecord()=>new Item(new byte[36]){Packed=0x9f,EffectField=1,Unk_0xC=1,SortIndex=52,BuyPrice=20000,UseConsume=true}.Write();
    private static string DigestMembers(byte[][] records,Func<int,bool> include)
    {
        using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for(int i=0;i<records.Length;i++)if(include(i)){h.AppendData(BitConverter.GetBytes(i));h.AppendData(BitConverter.GetBytes(records[i].Length));h.AppendData(records[i]);}
        return Convert.ToHexString(h.GetHashAndReset());
    }
    internal static void ValidateRows(byte[] source,int count,bool messages,JsonElement profile)
    {
        if(source.Length<20||BitConverter.ToInt32(source,12)!=16)throw new InvalidDataException("Sección de texto Gen6 incompatible.");
        byte[] head=source.AsSpan(0,16).ToArray();Array.Clear(head,2,6);
        if(Hash(head)!=profile.GetProperty("header_sha256").GetString())throw new InvalidDataException("Cabecera opaca de texto modificada.");
        var rows=new byte[count][];var flags=new byte[count][];int actual=BitConverter.ToUInt16(source,2);
        for(int i=0;i<count;i++)
        {
            int table=checked(20+i*8),offset=checked(16+BitConverter.ToInt32(source,table)),length=checked(BitConverter.ToUInt16(source,table+4)*2);
            if(offset<20+actual*8||offset>source.Length-length)throw new InvalidDataException("Fila de texto fuera de límites.");
            flags[i]=source.AsSpan(table+6,2).ToArray();rows[i]=[..source.AsSpan(offset,length),..flags[i]];
        }
        if(DigestMembers(rows,i=>messages||!Ids.Contains(i))!=profile.GetProperty("opaque_rows_sha256").GetString()||
            DigestMembers(flags,i=>!messages&&Ids.Contains(i))!=profile.GetProperty("edited_flags_sha256").GetString())
            throw new InvalidDataException("Filas o metadatos ajenos modificados.");
    }
    private static byte[] ReplaceCrr(byte[] source,string before,string after,int count)
    {
        if(source.Length<0x358||BitConverter.ToInt32(source,0x354)!=count)throw new InvalidDataException("Inventario CRR incompatible.");
        int at=BitConverter.ToInt32(source,0x350);
        if(at<0x358||count<1||at>source.Length-count*32)throw new InvalidDataException("Tabla CRR truncada.");
        var entries=Enumerable.Range(0,count).Select(i=>Convert.ToHexString(source.AsSpan(at+i*32,32))).ToArray();
        if(entries.Count(h=>h==before)!=1)throw new InvalidDataException("CRR no contiene un único DllBag acreditado.");
        entries[Array.IndexOf(entries,before)]=after;Array.Sort(entries,StringComparer.Ordinal);byte[] output=(byte[])source.Clone();
        for(int i=0;i<count;i++)Convert.FromHexString(entries[i]).CopyTo(output,at+i*32);return output;
    }
    internal static string Message(int language)=>language switch
    {
        0 or 1=>"[VAR PKNICK(0000)]の\\nせいかくが かわった！",3=>"La nature de [VAR PKNICK(0000)]\\na changé !",
        4=>"La natura di [VAR PKNICK(0000)]\\nè cambiata!",5=>"Das Wesen von [VAR PKNICK(0000)]\\nhat sich geändert!",
        6=>"¡Cambió la naturaleza de\\n[VAR PKNICK(0000)]!",7=>"[VAR PKNICK(0000)]의\\n성격이 바뀌었다!",_=>"[VAR PKNICK(0000)]'s Nature changed!"
    };
    internal static string Name(int nature,string[] natures,int language,int bank)
    {
        string n=natures[nature];bool plural=bank==97,branch=bank==95;
        return language switch
        {
            0 or 1=>n+"ミント",7=>n+"민트",
            3=>(plural?"Aromates ":branch?"Aromate[VAR NUMBRNCH(00FE,0100)]s ":"Aromate ")+n,
            4=>(plural?"Mente ":"Menta ")+n,
            5=>(plural?"Minzen ":branch?"Minze[VAR NUMBRNCH(00FE,0100)]n ":"Minze ")+n,
            6=>(plural?"Mentas ":branch?"Menta[VAR NUMBRNCH(00FE,0100)]s ":"Menta ")+n,
            _=>n+(plural?" Mints":branch?" Mint[VAR NUMBRNCH(00FE,0100)]s":" Mint")
        };
    }
    internal static string Description(int nature,int language)
    {
        string[][] stats=[["こうげき","ぼうぎょ","すばやさ","とくこう","とくぼう"],["攻撃","防御","素早さ","特攻","特防"],
            ["Attack","Defense","Speed","Sp. Atk","Sp. Def"],["Attaque","Défense","Vitesse","At. Spé.","Déf. Spé."],
            ["Attacco","Difesa","Velocità","Att. Sp.","Dif. Sp."],["Angriff","Verteidigung","Initiative","Sp.-Ang.","Sp.-Vert."],
            ["Ataque","Defensa","Velocidad","At. Esp.","Def. Esp."],["공격","방어","스피드","특수공격","특수방어"]];
        string[] start=["せいかくを かえる。","性格を 変える。","Changes the real Nature.","Change la nature réelle.","Cambia la natura reale.","Ändert das echte Wesen.","Cambia la naturaleza real.","실제 성격을 바꾼다."];
        string[] neutral=["のうりょくの ほせい なし。","能力の 補正 なし。","Neutral stat growth.","Croissance neutre.","Statistiche neutre.","Neutrale Statuswerte.","Crecimiento neutro.","능력치 보정 없음."];
        string[] consumed=["こうかが あれば なくなる。","効果が あれば なくなる。","Consumed only when effective.","Consommé si efficace.","Si consuma se ha effetto.","Verbrauch nur bei Wirkung.","Se consume si tiene efecto.","효과가 있을 때만 소모된다."];
        string effect=nature==12?neutral[language]:"+"+stats[language][nature/5]+" / -"+stats[language][nature%5]+".";
        return start[language]+"\\n"+effect+"\\n"+consumed[language];
    }
}
