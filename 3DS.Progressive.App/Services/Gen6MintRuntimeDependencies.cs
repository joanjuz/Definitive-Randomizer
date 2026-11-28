using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using pk3DS.Core.Structures;

namespace ThreeDS.Progressive.Services;

/// <summary>Read-only composition admission. Original installation still uses its full
/// fingerprint gate; this validator accredits the finite data edits made by Gen6 editors.
/// History is never evidence that arbitrary replacement bytes are compatible.</summary>
internal static class Gen6MintRuntimeDependencies
{
    private static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));
    private static void Need(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    internal static ulong Title(GameProjectSession p)
    {
        byte[] h=File.ReadAllBytes(Header(p.ProjectRoot,"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"));
        Need(p.GameConfiguration.XY&&h.Length>=0x120&&h.AsSpan(0x100,4).SequenceEqual("NCCH"u8),"Cabecera XY requerida.");
        ulong title=BitConverter.ToUInt64(h,0x118);Need(title is 0x0004000000055D00UL or 0x0004000000055E00UL,"Title ID XY desconocido.");return title;
    }
    internal static string Header(string root,params string[] names)
    {
        // HackingToolkit3DS emits both partition-zero NCCH and a 16-KiB NCSD
        // container named HeaderNCCH.bin. Only that recognized container is ignored.
        bool IsContainer(string path)
        {
            if(!Path.GetFileName(path).Equals("HeaderNCCH.bin",StringComparison.OrdinalIgnoreCase)||new FileInfo(path).Length!=0x4000)return false;
            using var stream=File.OpenRead(path);stream.Position=0x100;Span<byte> magic=stackalloc byte[4];stream.ReadExactly(magic);return magic.SequenceEqual("NCSD"u8);
        }
        var files=names.Select(n=>Path.Combine(root,n)).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Where(p=>!IsContainer(p)).ToArray();
        Need(files.Length==1,"Cabecera ausente o ambigua: "+names[0]+". Se requiere una cabecera de la partición del juego, no del contenedor NCSD.");return files[0];
    }
    internal static string CodePath(GameProjectSession p)=>Header(p.GameConfiguration.ExeFS,".code.bin","code.bin",".code","code");
    private static int Unique(byte[] bytes,byte[] pattern,int start)
    {
        int found=bytes.AsSpan(start).IndexOf(pattern);Need(found>=0,"Firma de tabla ausente.");int at=start+found;
        Need(bytes.AsSpan(at+1).IndexOf(pattern)<0,"Firma de tabla duplicada.");return at;
    }
    internal static byte[] NormalizeCode(GameProjectSession p,byte[] code,JsonElement shape)
        =>NormalizeCode(p.GameConfiguration,p.ProjectRoot,p.MoveNames.Count,code,shape);
    internal static byte[] NormalizeCode(GameConfig config,string root,int moveCount,byte[] code,JsonElement shape)
    {
        Need(code.Length==shape.GetProperty("length").GetInt32(),"Tamaño de ejecutable XY desconocido.");
        int tm=GameMachine6Service.FindTable(code,false),pickup=Unique(code,GamePickupService.Signature,0x400000)-58;
        var hm=Gen6HmRestriction.Locate(code,false);int stock=GameMart6Service.Find(code,GameMart6Service.CountsXY.Sum()*2);
        Need(tm==shape.GetProperty("tm").GetInt32()&&pickup==shape.GetProperty("pickup").GetInt32()&&hm.Offset==shape.GetProperty("hm").GetInt32()&&stock==shape.GetProperty("stock").GetInt32(),"Una dependencia XY se desplazó.");
        Need(hm.Offset>=0&&Enumerable.Range(0,105).All(i=>BitConverter.ToUInt16(code,tm+i*2)<moveCount),"Tabla MT/MO inválida.");
        Need(Enumerable.Range(0,29).All(i=>BitConverter.ToUInt16(code,pickup+i*2)<718),"Tabla Pickup inválida.");
        var marts=new Gen6MartTable(config.ORAS,root,code,stock,GameMart6Service.CountsXY);int cursor=marts.TableOffset(code);
        var expectedRanges=shape.GetProperty("mart_ranges").EnumerateArray().Select(r=>(r[0].GetInt32(),r[1].GetInt32())).ToArray();
        Need(marts.MintMutableRanges.SequenceEqual(expectedRanges),"Los rangos o el puntero nativo de tiendas se desplazaron.");
        foreach(int count in marts.Counts(code)){Need(count is >=1 and <=64,"Cantidad de tienda inválida.");for(int i=0;i<count;i++)Need(BitConverter.ToUInt16(code,cursor+i*2)<718,"Objeto de tienda inválido.");cursor+=count*2;}
        Need(Gen6MintIconService.Mapping(code,false)||Gen6MintIconService.Mapping(code,true),"Mapa de iconos XY desconocido.");
        byte[] result=(byte[])Gen6MintIconService.NormalizeCode(code).Clone();
        for(int i=0;i<GameTrade6Service.XYRequests.Length;i++)
        {
            int at=GameTrade6Service.XYRequests[i];ushort request=BitConverter.ToUInt16(code,at);
            Need(request==0||request==GameTrade6Service.XYExpected[i],"Solicitud de intercambio XY desconocida.");result.AsSpan(at,2).Clear();
        }
        foreach(int at in GameTrade6Service.XYOffers){Need(BitConverter.ToUInt16(code,at)<=721,"Oferta de intercambio XY inválida.");result.AsSpan(at,2).Clear();}
        foreach(var (offset,length) in marts.MintMutableRanges)result.AsSpan(offset,length).Clear();
        result.AsSpan(tm,210).Clear();result.AsSpan(pickup,58).Clear();result.AsSpan(hm.Offset,8).Clear();return result;
    }
    internal static byte[] NormalizeCro(string name,byte[] data)
    {
        Need(CroIntegrity.Rebuild(data).AsSpan().SequenceEqual(data),"CRO0/hashes inválidos: "+name);
        if(name=="DllBattle.cro")
        {
            Need(Gen6PersistentItemsPatcher.Inspect(data)!=Gen6PersistentItemsPatcher.State.Partial,"Persistent Items parcial.");
            // Its existing validator admits only the two PI branches, typed type-chart
            // values and the exact supported King's Shield instruction.
            return Convert.FromHexString(Gen6PersistentItemsPatcher.Shape(data));
        }
        byte[] normalized=(byte[])data.Clone();normalized.AsSpan(0,128).Clear();
        if(name=="DllField.cro")
        {
            for(int i=0;i<13;i++)
            {
                int at=0xee46c+i*12;var row=new EncounterStatic6(data.AsSpan(at,12).ToArray());
                Need(row.Species<=721&&row.Level<=100&&BitConverter.ToInt16(data,at+4) is >=-1 and <718,"Encuentro estático XY inválido.");
                normalized.AsSpan(at,6).Clear();normalized[at+6]&=0x80;normalized[at+7]&=0xfc;
            }
            for(int i=0;i<19;i++)
            {
                int at=0xf805c+i*24;var row=new EncounterGift6(data.AsSpan(at,24).ToArray(),false);
                Need(row.Species<=721&&row.Level<=100&&row.Ability is >=-1 and <=2&&row.Nature is >=-1 and <=24&&row.Gender is >=-1 and <=2&&row.IVs.All(v=>v is >=-1 and <=31),"Regalo XY inválido.");
                // Held items in gifts use an int32 sentinel; preserve the unrelated bytes.
                Need(row.HeldItem is >=-1 and <718,"Objeto de regalo inválido.");
                normalized.AsSpan(at,2).Clear();normalized.AsSpan(at+4,2).Clear();normalized[at+6]&=0xfd;
                normalized.AsSpan(at+7,2).Clear();normalized.AsSpan(at+12,11).Clear();
            }
        }
        else if(name=="DllPoke3Select.cro")
        {
            int start=checked(BitConverter.ToInt32(data,0xb8)+0x10);Need(start>=0x180&&(long)start+5*0x54+2<=data.Length,"Selector XY truncado.");
            for(int i=0;i<6;i++){Need(BitConverter.ToUInt16(data,start+i*0x54)<=721,"Inicial fuera de catálogo.");normalized.AsSpan(start+i*0x54,2).Clear();}
        }
        return normalized;
    }
    private static string ItemShape(byte[][] records)
    {
        Need(records.Length==718&&records.All(r=>r.Length==36),"Tabla de objetos XY incompatible.");
        var ids=Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).ToHashSet();
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for(int i=0;i<records.Length;i++)if(!ids.Contains(i)){hash.AppendData(BitConverter.GetBytes(i));hash.AppendData(records[i].AsSpan(2));}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static void TextEnvelope(byte[] source,JsonElement profile)
    {
        Need(source.Length>=20&&BitConverter.ToInt32(source,12)==16,"Cabecera de texto XY desconocida.");
        byte[] header=source[..16];header.AsSpan(2,6).Clear();Need(Hash(header)==profile.GetProperty("header_sha256").GetString(),"Metadatos opacos de texto alterados.");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(int id in Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).Order())
        {
            int at=20+id*8+6;Need(at+2<=source.Length,"Índice de texto truncado.");hash.AppendData(BitConverter.GetBytes(id));hash.AppendData(BitConverter.GetBytes(2));hash.AppendData(source.AsSpan(at,2));
        }
        Need(Convert.ToHexString(hash.GetHashAndReset())==profile.GetProperty("edited_flags_sha256").GetString(),"Flags de las filas de mentas alterados.");
    }
    internal static JsonObject DescribeOriginal(GameProjectSession p)
    {
        var originalPins=Gen6NatureMintService.Profile(Title(p)).GetProperty("sha256");
        foreach(var pin in originalPins.EnumerateObject())
        {
            string path=pin.Name switch{
                "ncchheader.bin"=>Header(p.ProjectRoot,"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"),
                "exheader.bin"=>Header(p.ProjectRoot,"exheader.bin","DecryptedExHeader.bin","ExHeader.bin"),
                "ExeFS/.code.bin"=>CodePath(p),
                _ when pin.Name.StartsWith("RomFS/",StringComparison.Ordinal)=>Path.Combine(p.GameConfiguration.RomFS,pin.Name[6..]),
                _=>Path.Combine(p.GameConfiguration.ExeFS,pin.Name[6..])};
            Need(Hash(File.ReadAllBytes(path))==pin.Value.GetString(),"La exportación de perfiles requiere originales completos: "+pin.Name);
        }
        Need(new Gen6NatureMintService(p).Inspect().State==Gen6MintNativeState.Original,"Se requiere el perfil original completo para acreditar los rangos.");
        ulong title=Title(p);var config=p.GameConfiguration;byte[] code=File.ReadAllBytes(Path.Combine(config.ExeFS,".code.bin"));
        var shape=new JsonObject{{"length",code.Length},{"tm",GameMachine6Service.FindTable(code,false)},
            {"pickup",Unique(code,GamePickupService.Signature,0x400000)-58},{"hm",Gen6HmRestriction.Locate(code,false).Offset},{"stock",GameMart6Service.Find(code,GameMart6Service.CountsXY.Sum()*2)}};
        var marts=new Gen6MartTable(p,code,shape["stock"]!.GetValue<int>(),GameMart6Service.CountsXY);
        shape["mart_ranges"]=new JsonArray(marts.MintMutableRanges.Select(r=>(JsonNode)new JsonArray(r.Offset,r.Length)).ToArray());
        shape["sha256"]=Hash(NormalizeCode(p,code,JsonSerializer.SerializeToElement(shape)));
        var cro=new JsonObject();
        foreach(string path in Directory.GetFiles(config.RomFS).Where(f=>Path.GetExtension(f).Equals(".cro",StringComparison.OrdinalIgnoreCase)).Order())
        {
            byte[] bytes=File.ReadAllBytes(path);string name=Path.GetFileName(path);
            cro[name]=new JsonObject{{"sha256",Hash(bytes)},{"header_sha256",Hash(bytes[..128])},{"shape_sha256",Hash(NormalizeCro(name,bytes))}};
        }
        var natureBanks=new JsonObject();for(int language=0;language<8;language++)
        {byte[][] records=new GARC.MemGARC(File.ReadAllBytes(Path.Combine(config.RomFS,config.GetGARCReference("gametext").GetRelativeGARC(language).Reference))).Files;natureBanks[language.ToString()]=Hash(records[47]);}
        Need(new Gen6NatureMintService(p).Inspect().State==Gen6MintNativeState.Original,"Los originales cambiaron durante la acreditación.");
        var itemRows=new GARC.MemGARC(File.ReadAllBytes(Path.Combine(config.RomFS,"a/2/2/0"))).Files;
        var reserved=new JsonObject();foreach(var mint in Gen6NatureMintCatalog.ExperimentalItemMap.Items)reserved[mint.ItemId.ToString()]=Hash(itemRows[mint.ItemId][2..]);
        string identityHash=Hash(IdentityShape(File.ReadAllBytes(Header(p.ProjectRoot,"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"))));
        string exheaderHash=Hash(ExheaderShape(File.ReadAllBytes(Header(p.ProjectRoot,"exheader.bin","DecryptedExHeader.bin","ExHeader.bin"))));
        return new(){{"title_id",title.ToString("X16")},{"code",shape},{"cros",cro},{"item_price_shape",ItemShape(itemRows)},{"nature_banks",natureBanks},
            {"identity_shape_sha256",identityHash},{"exheader_shape_sha256",exheaderHash},{"reserved_item_shapes",reserved}};
    }
    internal static byte[] IdentityShape(byte[] source)
    {
        Need(source.Length==512&&source.AsSpan(0x100,4).SequenceEqual("NCCH"u8),"Cabecera NCCH inválida.");
        Need(source[0x18f] is 0 or 1 or 4,"Cifrado NCCH no reconocido.");
        var result=(byte[])source.Clone();
        foreach(var (at,length) in new[]{(0,256),(0x104,4),(0x130,32),(0x160,32),(0x18f,1),(0x190,0x70)})result.AsSpan(at,length).Clear();
        return result;
    }
    internal static byte[] ExheaderShape(byte[] source)
    {Need(source.Length==2048,"ExHeader truncado.");var result=(byte[])source.Clone();result[0xd]&=0xfe;return result;}
    internal static Gen6MintInstallation Capture(GameProjectSession p,bool requireInstalled=true)
    {
        ulong title=Title(p);var profile=Gen6NatureMintService.Profile(title);var native=profile.GetProperty("native");var pins=profile.GetProperty("sha256");var runtime=native.GetProperty("runtime");
        Need(runtime.GetProperty("title_id").GetString()==title.ToString("X16"),"Perfil de composición de otro título.");
        var snapshots=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[] Read(string path){path=Path.GetFullPath(path);UsumMintItemPolicy.CheckPath(p,path);if(!snapshots.TryGetValue(path,out var bytes))snapshots[path]=bytes=File.ReadAllBytes(path);return bytes;}
        string root=p.ProjectRoot,rom=p.GameConfiguration.RomFS,exe=p.GameConfiguration.ExeFS;
        string ncch=Header(root,"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin"),ex=Header(root,"exheader.bin","DecryptedExHeader.bin","ExHeader.bin");
        Need(Hash(IdentityShape(Read(ncch)))==runtime.GetProperty("identity_shape_sha256").GetString()&&Hash(ExheaderShape(Read(ex)))==runtime.GetProperty("exheader_shape_sha256").GetString(),"Identidad o mapa de memoria XY alterados.");
        byte[] code=Read(CodePath(p));Need(Hash(NormalizeCode(p,code,runtime.GetProperty("code")))==runtime.GetProperty("code").GetProperty("sha256").GetString(),"El complemento nativo del ejecutable XY cambió.");
        Need(Hash(Read(Path.Combine(rom,"static.crs")))==pins.GetProperty("RomFS/static.crs").GetString(),"Relocaciones/importaciones estáticas alteradas.");
        string[] files=Directory.GetFiles(rom).Where(f=>Path.GetExtension(f).Equals(".cro",StringComparison.OrdinalIgnoreCase)).Order().ToArray();var cros=runtime.GetProperty("cros");
        Need(files.Length==cros.EnumerateObject().Count(),"Inventario CRO cambió.");var images=new List<byte[]>();var oldHeaders=new List<string>();
        bool installed=false;
        foreach(string file in files)
        {
            string name=Path.GetFileName(file);Need(cros.TryGetProperty(name,out var original),"Nombre físico CRO desconocido: "+name);byte[] bytes=Read(file);images.Add(bytes);
            byte[] rebuilt=CroIntegrity.Rebuild(bytes);if(requireInstalled)Need(rebuilt.AsSpan().SequenceEqual(bytes),"Hashes CRO inválidos: "+name);
            if(name=="DllBag.cro")
            {
                var state=Gen6NatureMintPatch.Inspect(bytes,new("XY-runtime",title,pins.GetProperty("ExeFS/.code.bin").GetString()!,pins.GetProperty("RomFS/DllBag.cro").GetString()!)).State;
                installed=state==Gen6MintNativeState.Installed;
                Need(installed||!requireInstalled&&state==Gen6MintNativeState.Original,"Handler de Bolsa desconocido o parcial.");
            }
            else if(name is "DllBattle.cro" or "DllField.cro" or "DllPoke3Select.cro")Need(Hash(NormalizeCro(name,rebuilt))==original.GetProperty("shape_sha256").GetString(),"Instrucciones/bytes opacos CRO alterados: "+name);
            else Need(Hash(rebuilt)==original.GetProperty("sha256").GetString(),"CRO ajeno a los cambios acreditados: "+name);
            oldHeaders.Add(original.GetProperty("header_sha256").GetString()!);
        }
        byte[] crr=Read(Path.Combine(rom,".crr/static.crr"));if(requireInstalled)Need(CroIntegrity.RebuildCrr(crr,images).AsSpan().SequenceEqual(crr),"CRR no corresponde al inventario actual.");
        int at=BitConverter.ToInt32(crr,0x350),count=BitConverter.ToInt32(crr,0x354);Need(count==oldHeaders.Count&&at>=0x358&&(long)at+count*32L<=crr.Length,"Tabla CRR incompatible.");
        byte[] normalized=(byte[])crr.Clone();oldHeaders.Sort(StringComparer.Ordinal);for(int i=0;i<count;i++)Convert.FromHexString(oldHeaders[i]).CopyTo(normalized,at+i*32);
        Need(Hash(normalized)==pins.GetProperty("RomFS/.crr/static.crr").GetString(),"Firma/metadatos o entradas CRR ajenos al perfil original.");
        var items=new GARC.MemGARC(Read(Path.Combine(rom,"a/2/2/0"))).Files;Need(items.Length==718&&items.All(r=>r.Length==36),"Formato de objetos XY inválido.");
        foreach(var mint in Gen6NatureMintCatalog.ExperimentalItemMap.Items)Need(installed?items[mint.ItemId].AsSpan(2).SequenceEqual(Gen6NatureMintService.ItemRecord().AsSpan(2)):Hash(items[mint.ItemId][2..])==runtime.GetProperty("reserved_item_shapes").GetProperty(mint.ItemId.ToString()).GetString(),"Slot de menta ocupado por otro cambio: "+mint.ItemId);
        for(int language=0;language<8;language++)
        {
            var rows=new GARC.MemGARC(Read(Path.Combine(rom,p.GameConfiguration.GetGARCReference("gametext").GetRelativeGARC(language).Reference))).Files;
            Need(rows.Length>140&&Hash(rows[47])==runtime.GetProperty("nature_banks").GetProperty(language.ToString()).GetString(),"Banco de naturalezas no acreditado.");
            string[] natures=new TextFile(p.GameConfiguration,rows[47],p.GameConfiguration.RemapCharacters).Lines;
            foreach(int bank in new[]{95,96,97,98,99})
            {TextEnvelope(rows[bank],native.GetProperty("texts").GetProperty(language.ToString()).GetProperty("banks").GetProperty(bank.ToString()));var text=new TextFile(p.GameConfiguration,rows[bank],p.GameConfiguration.RemapCharacters).Lines;Need(text.Length==718,"Banco de objetos truncado.");if(installed)foreach(var mint in Gen6NatureMintCatalog.ExperimentalItemMap.Items)Need(text[mint.ItemId]==(bank==99?Gen6NatureMintService.Description(mint.NatureId,language):Gen6NatureMintService.Name(mint.NatureId,natures,language,bank)),"Texto de menta alterado.");}
            var messages=new TextFile(p.GameConfiguration,rows[140],p.GameConfiguration.RemapCharacters).Lines;Need(installed?messages.Length==180&&messages[179]==Gen6NatureMintService.Message(language):messages.Length==179,"Fila de mensaje de mentas ocupada o incompatible.");
        }
        byte[] archive=Read(Path.Combine(rom,Gen6MintIconService.ArchivePath));Need(Hash(Gen6MintIconService.NormalizeArchive(archive))==pins.GetProperty("RomFS/a/0/9/4").GetString(),"Imágenes originales alteradas.");
        var records=Gen6MintIconService.Records(archive);bool icons=Gen6MintIconService.Mapping(code,true);Need(icons==(records.Length==600),"Instalación de iconos parcial.");
        if(icons)for(int i=0;i<21;i++)Need(Hash(Gen6MintIconService.Decompress(records[579+i]))==native.GetProperty("icon_texture_sha256")[i].GetString(),"Textura de menta alterada.");
        var dependencies=snapshots.ToDictionary(p=>p.Key,p=>Hash(p.Value),StringComparer.OrdinalIgnoreCase);
        var fingerprints=snapshots.Select(pair=>(pair.Key,Fingerprint:SHA256.HashData(pair.Value))).ToArray();
        void Unchanged()
        {
            foreach(var pair in fingerprints)GarcTransaction.ReadUnchanged(pair.Key,pair.Fingerprint);
            var currentNames=Directory.GetFiles(rom).Where(f=>Path.GetExtension(f).Equals(".cro",StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).Order(StringComparer.Ordinal);
            Need(currentNames.SequenceEqual(files.Select(Path.GetFileName).Order(StringComparer.Ordinal),StringComparer.Ordinal),"Inventario o nombres físicos CRO cambiaron durante la operación.");
        }
        Unchanged();return new(title,Gen6NatureMintCatalog.ExperimentalItemMap,dependencies,Unchanged);
    }
}
