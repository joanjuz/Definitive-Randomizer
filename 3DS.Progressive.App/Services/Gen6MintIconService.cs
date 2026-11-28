using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.CTR.Images;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed record Gen6MintIconPreview(Gen6MintNativeState State,string Report,EditorProposal? Proposal=null);

/// <summary>XY-only image references and GARC v4. Uses the pinned USUM artwork pipeline,
/// but converts to XY's native paletted RGB5A1 BCLIM, including high-nibble-first indexes.</summary>
internal sealed class Gen6MintIconService(GameProjectSession project,
    Func<ulong,System.Text.Json.JsonElement>? resolveProfile=null,IReadOnlyList<UsumMintIconSource>? sources=null)
{
    internal const int TableOffset=0x43db74, OriginalCount=579, InstalledCount=600;
    internal const string ArchivePath="a/0/9/4",ActionId="xy.nature-mints.icons.install";
    private readonly Guid _owner=Guid.NewGuid();
    private byte[][]? _preparedTextures;
    private static int[] Ids=>Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).ToArray();
    private IReadOnlyList<UsumMintIconSource> Sources=>sources??UsumMintIconSources.All.Select((s,i)=>s with{ItemId=Ids[i]}).ToArray();
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    internal static int OriginalImage(int id)=>id switch{622=>492,623=>493,_=>577};
    internal static bool Mapping(byte[] code,bool installed)=>code.Length>=TableOffset+718*4&&
        Ids.Select((id,i)=>BitConverter.ToInt32(code,TableOffset+id*4)==(installed?OriginalCount+i:OriginalImage(id))).All(v=>v);
    internal static byte[] NormalizeCode(byte[] code)
    {
        if(!Mapping(code,true))return code;
        byte[] normalized=(byte[])code.Clone();foreach(int id in Ids)BitConverter.GetBytes(OriginalImage(id)).CopyTo(normalized,TableOffset+id*4);return normalized;
    }
    internal static byte[] Pack(byte[][] records)
    {
        string temp=Path.Combine(Path.GetTempPath(),"xy-mint-icons-"+Guid.NewGuid().ToString("N"));
        var anchor=GARCReference.GARCReference_XY[0];var reference=anchor.GetRelativeGARC(94-anchor.FileNumber,"XY mint icons");
        try{new GARCFile(GARC.PackGARC(records,GARC.VER_4,4),reference,temp).Save();return File.ReadAllBytes(temp);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal static byte[][] Records(byte[] archive)
    {
        if(archive.Length<0x28||archive.Length>16*1024*1024||!archive.AsSpan(0,4).SequenceEqual("CRAG"u8)||
            BitConverter.ToInt32(archive,4)!=0x1c||BitConverter.ToUInt16(archive,8)!=0xfeff||
            BitConverter.ToUInt16(archive,10)!=GARC.VER_4||BitConverter.ToInt32(archive,12)!=4||
            BitConverter.ToInt32(archive,20)!=archive.Length)
            throw new InvalidDataException("GARC de iconos XY inválido o ajeno a v4.");
        int count=BitConverter.ToUInt16(archive,0x24);
        if(count is not (OriginalCount or InstalledCount))throw new InvalidDataException("Cantidad de imágenes XY desconocida.");
        int fatb=0x1c+12+4*count,data=checked(fatb+12+16*count+12);
        if(archive.Length<data||BitConverter.ToInt32(archive,16)!=data||!archive.AsSpan(0x1c,4).SequenceEqual("OTAF"u8)||
            BitConverter.ToInt32(archive,0x20)!=12+4*count||BitConverter.ToUInt16(archive,0x26)!=0xffff||
            !archive.AsSpan(fatb,4).SequenceEqual("BTAF"u8)||BitConverter.ToInt32(archive,fatb+4)!=12+16*count||
            BitConverter.ToInt32(archive,fatb+8)!=count||!archive.AsSpan(data-12,4).SequenceEqual("BMIF"u8)||
            BitConverter.ToInt32(archive,data-8)!=12||BitConverter.ToInt32(archive,data-4)!=archive.Length-data)throw new InvalidDataException("Tablas gráficas truncadas.");
        int previous=0;
        for(int i=0;i<count;i++)
        {
            int at=fatb+12+16*i,start=BitConverter.ToInt32(archive,at+4),end=BitConverter.ToInt32(archive,at+8),length=BitConverter.ToInt32(archive,at+12);
            if(BitConverter.ToUInt32(archive,fatb+12+16*i)!=1||BitConverter.ToInt32(archive,0x28+4*i)!=16*i)
                throw new InvalidDataException("Iconos vectorizados o tablas desconocidas.");
            if(start!=previous||start<0||length<4||(long)end!=start+(((long)length+3)&~3L)||end>archive.Length-data)
                throw new InvalidDataException("Rangos gráficos solapados o truncados.");previous=end;
        }
        if(data+previous!=archive.Length)throw new InvalidDataException("Datos gráficos adicionales.");
        byte[][] records=new GARC.MemGARC(archive).Files;
        if(!Pack(records).AsSpan().SequenceEqual(archive))throw new InvalidDataException("Layout/padding de iconos XY no canónico.");
        return records;
    }
    internal static byte[] NormalizeArchive(byte[] archive)
    {
        var rows=Records(archive);return rows.Length==OriginalCount?archive:Pack(rows.Take(OriginalCount).ToArray());
    }
    internal static byte[] ConvertTexture(byte[] source)
    {
        using var original=GameGraphicsCodec.Decode(source);if(original.Width!=32||original.Height!=32)throw new InvalidDataException("Fuente de icono incompatible.");
        using var canvas=new Bitmap(30,30);
        for(int y=0;y<30;y++)for(int x=0;x<30;x++)canvas.SetPixel(x,y,original.GetPixel(x+1,y+1));
        byte[] result=GameGraphicsCodec.Encode(canvas,XLIMEncoding.RGB5A1,false,XLIMOrientation.None);
        var histogram=new Dictionary<ushort,int>();
        for(int y=0;y<30;y++)for(int x=0;x<30;x++){var c=canvas.GetPixel(x,y);if(c.A<128)continue;ushort packed=Pixel(c);histogram[packed]=histogram.GetValueOrDefault(packed)+1;}
        var opaque=Quantize(histogram);var colors=new List<ushort>{0xfffe};colors.AddRange(opaque);
        var lookup=histogram.Keys.ToDictionary(c=>c,c=>(byte)(1+opaque.Select((p,i)=>(i,d:Distance(c,p))).OrderBy(v=>v.d).ThenBy(v=>v.i).First().i));var indexes=new byte[1024];
        var orienter=new XLIMOrienter(32,32,XLIMOrientation.None);
        for(uint i=0;i<1024;i++)
        {
            var at=orienter.Get(i);if(at.X>=30||at.Y>=30)continue;var c=canvas.GetPixel((int)at.X,(int)at.Y);if(c.A<128)continue;
            indexes[i]=lookup[Pixel(c)];
        }
        result.AsSpan(0,2048).Clear();BitConverter.GetBytes((ushort)2).CopyTo(result,0);BitConverter.GetBytes((ushort)colors.Count).CopyTo(result,2);
        for(int i=0;i<colors.Count;i++)BitConverter.GetBytes(colors[i]).CopyTo(result,4+i*2);
        int start=4+colors.Count*2;
        if(colors.Count<=16)for(int i=0;i<1024;i+=2)result[start+i/2]=(byte)(indexes[i]<<4|indexes[i+1]);
        else indexes.CopyTo(result,start);
        ExpandPalette(result);return result;
    }
    private static ushort Pixel(Color c)=>(ushort)((c.R>>3)<<11|(c.G>>3)<<6|(c.B>>3)<<1|1);
    private static int Channel(ushort c,int shift)=>(c>>shift)&31;
    private static int Distance(ushort a,ushort b)=>new[]{11,6,1}.Sum(s=>{int d=Channel(a,s)-Channel(b,s);return d*d;});
    // Deterministic, population-weighted median cut; reserves slot zero for
    // transparency. RGB5A1 alpha and tile order never participate in reduction.
    private static List<ushort> Quantize(Dictionary<ushort,int> histogram)
    {
        if(histogram.Count<=69)return histogram.Keys.Order().ToList();
        var boxes=new List<List<ushort>>{histogram.Keys.Order().ToList()};
        int Range(List<ushort> box,int shift)=>box.Max(c=>Channel(c,shift))-box.Min(c=>Channel(c,shift));
        while(boxes.Count<69)
        {
            var pick=boxes.Select((box,i)=>(box,i,score:box.Count<2?-1L:(long)new[]{11,6,1}.Max(s=>Range(box,s))*box.Sum(c=>histogram[c])))
                .OrderByDescending(v=>v.score).ThenBy(v=>v.i).First();
            if(pick.score<0)break;
            int channel=new[]{11,6,1}.OrderByDescending(s=>Range(pick.box,s)).First();
            var sorted=pick.box.OrderBy(c=>Channel(c,channel)).ThenBy(c=>c).ToList();int half=(sorted.Sum(c=>histogram[c])+1)/2,weight=0,cut=0;
            while(cut<sorted.Count-1&&weight<half)weight+=histogram[sorted[cut++]];
            cut=Math.Clamp(cut,1,sorted.Count-1);boxes[pick.i]=sorted.Take(cut).ToList();boxes.Add(sorted.Skip(cut).ToList());
        }
        return boxes.Select(box=>{int total=box.Sum(c=>histogram[c]);int Mean(int s)=>(box.Sum(c=>Channel(c,s)*histogram[c])+total/2)/total;
            return (ushort)(Mean(11)<<11|Mean(6)<<6|Mean(1)<<1|1);}).Distinct().Order().ToList();
    }
    /// <summary>Contract of original ARM 0x338B04: two bytes per palette entry,
    /// RGB5A1 halfwords copied unchanged, <=16 colors use high nibble first.</summary>
    internal static byte[] ExpandPalette(byte[] texture)
    {
        if(texture.Length!=2088||BitConverter.ToUInt16(texture,0)!=2)throw new InvalidDataException("Se requiere una paleta XY; píxeles directos no admitidos.");
        int count=BitConverter.ToUInt16(texture,2),start=4+count*2;
        if(count is <1 or >70||start+1024/(count<=16?2:1)>2048)throw new InvalidDataException("Paleta XY truncada o inválida.");
        byte[] result=new byte[2048];
        for(int i=0;i<1024;i++)
        {
            int index=count<=16?(texture[start+i/2]>>(i%2==0?4:0))&15:texture[start+i];
            if(index>=count)throw new InvalidDataException("Índice ajeno a la paleta XY.");
            texture.AsSpan(4+index*2,2).CopyTo(result.AsSpan(i*2,2));
        }
        return result;
    }
    internal static byte[] Decompress(byte[] record)
    {
        if(record.Length<4||!record.AsSpan(0,4).SequenceEqual(new byte[]{0x11,0x28,8,0}))throw new InvalidDataException("Compresión de icono XY incompatible.");
        using var output=new MemoryStream();try{LZSS.Decompress(new MemoryStream(record),record.Length,output);}
        catch(TooMuchInputException ex){throw new InvalidDataException("Datos comprimidos adicionales.",ex);}byte[] raw=output.ToArray();
        if(raw.Length!=2088)throw new InvalidDataException("Tamaño de textura XY incompatible.");
        var tex=new BCLIM(raw);
        if(!raw.AsSpan(raw.Length-40,4).SequenceEqual("CLIM"u8)||!tex.Valid||tex.Width!=30||tex.Height!=30||
            tex.Format!=XLIMEncoding.RGB5A1||tex.Orientation!=XLIMOrientation.None)throw new InvalidDataException("Se requiere BCLIM XY 30x30 RGB5A1 None.");
        ExpandPalette(raw);return raw;
    }
    private static byte[] Compress(byte[] raw)
    {
        // Compression is container-independent; the shared helper's decompressor is BFLIM-only.
        string input=Path.Combine(Path.GetTempPath(),"xy-mint-icon-"+Guid.NewGuid().ToString("N")),output=input+".lz";
        try{File.WriteAllBytes(input,raw);LZSS.Compress(input,output);byte[] compressed=File.ReadAllBytes(output);
            if(!Decompress(compressed).AsSpan().SequenceEqual(raw))throw new InvalidDataException("Relectura del icono XY falló.");return compressed;}
        finally{if(File.Exists(input))File.Delete(input);if(File.Exists(output))File.Delete(output);}
    }
    private (string Code,byte[] CodeBytes,string Icons,byte[] Archive,(string Path,byte[] Fingerprint)[] Dependencies) Read()
    {
        if(!project.GameConfiguration.XY)throw new NotSupportedException("Iconos de mentas: solo X/Y.");
        string root=Path.GetFullPath(project.ProjectRoot);
        string header=Directory.GetFiles(root).Single(p=>Path.GetFileName(p) is "ncchheader.bin" or "HeaderNCCH0.bin" or "HeaderNCCH.bin");CheckPath(header);
        byte[] identity=File.ReadAllBytes(header);if(identity.Length<0x120)throw new InvalidDataException("Cabecera truncada.");
        ulong title=BitConverter.ToUInt64(identity,0x118);if(title is not (0x0004000000055D00UL or 0x0004000000055E00UL))throw new NotSupportedException("Title ID ajeno a X/Y.");
        var profile=(resolveProfile??Gen6NatureMintService.Profile)(title);
        string Resolve(string name)=>name switch{
            "ncchheader.bin"=>header,
            "exheader.bin"=>Directory.GetFiles(root).Single(p=>Path.GetFileName(p).Equals("exheader.bin",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(p)=="DecryptedExHeader.bin"),
            _=>Path.GetFullPath(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)))};
        string[] paths=profile.GetProperty("sha256").EnumerateObject().Select(p=>Resolve(p.Name))
            .Concat(Directory.GetFiles(project.GameConfiguration.RomFS).Where(p=>Path.GetExtension(p).Equals(".cro",StringComparison.OrdinalIgnoreCase)))
            .Append(header).Append(Path.Combine(project.GameConfiguration.ExeFS,".code.bin")).Append(Path.Combine(project.GameConfiguration.RomFS,ArchivePath))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(string path in paths)CheckPath(path);
        var contents=paths.ToDictionary(p=>p,File.ReadAllBytes,StringComparer.OrdinalIgnoreCase);
        var state=new Gen6NatureMintService(project,false,resolveProfile).Inspect();
        if(state.State!=Gen6MintNativeState.Installed)throw new InvalidDataException("Mentas XY instaladas y acreditadas requeridas. "+state.Report);
        string[] codes=Directory.GetFiles(project.GameConfiguration.ExeFS).Where(p=>Path.GetFileName(p) is ".code.bin" or "code.bin").ToArray();
        if(codes.Length!=1)throw new InvalidDataException("Ejecutable ambiguo.");
        string icon=Path.GetFullPath(Path.Combine(project.GameConfiguration.RomFS,ArchivePath));byte[] code=contents[codes[0]],archive=contents[icon];
        if(!Mapping(code,false)&&!Mapping(code,true))throw new InvalidDataException("Tabla de imágenes de mentas desconocida.");
        var records=Records(archive);
        if((records.Length==InstalledCount)!=Mapping(code,true))throw new InvalidDataException("Instalación de iconos parcial.");
        if(Sources.Count!=21||!Sources.Select(s=>s.ItemId).SequenceEqual(Ids))throw new InvalidDataException("Catálogo de iconos cruzado.");
        var dependencies=contents.Select(p=>(p.Key,SHA256.HashData(p.Value))).ToArray();
        foreach(var (path,hash) in dependencies)GarcTransaction.ReadUnchanged(path,hash);
        return(codes[0],code,icon,archive,dependencies);
    }
    internal Gen6MintIconPreview Inspect()
    {
        try{var s=Read();return new(Mapping(s.CodeBytes,true)?Gen6MintNativeState.Installed:Gen6MintNativeState.Original,"Iconos XY: "+(Mapping(s.CodeBytes,true)?"21 referencias instaladas.":"pendientes de instalar."));}
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or NotSupportedException)
        {return new(Gen6MintNativeState.Incompatible,ex.Message);}
    }
    internal async Task<Gen6MintIconPreview> PrepareAsync(CancellationToken cancellation=default)
    {
        var state=Inspect();if(state.State!=Gen6MintNativeState.Original)return state;
        return Prepare(await UsumMintIconDownload.LoadAsync(UsumMintIconDownload.DefaultCache,cancellation:cancellation,sources:Sources));
    }
    internal Gen6MintIconPreview Prepare(IReadOnlyList<byte[]> textures)
    {
        var s=Read();if(Mapping(s.CodeBytes,true))return Inspect();
        if(textures.Count!=21||textures.Where((t,i)=>Hash(t)!=Sources[i].TextureSha256).Any())throw new InvalidDataException("Fuentes de iconos no acreditadas.");
        byte[] code=(byte[])s.CodeBytes.Clone();for(int i=0;i<21;i++)BitConverter.GetBytes(OriginalCount+i).CopyTo(code,TableOffset+Ids[i]*4);
        var records=Records(s.Archive);var converted=textures.Select(ConvertTexture).ToArray();
        byte[] archive=Pack(records.Concat(converted.Select(Compress)).ToArray());var verified=Records(archive);
        if(records.Where((r,i)=>!r.AsSpan().SequenceEqual(verified[i])).Any()||converted.Where((r,i)=>!r.AsSpan().SequenceEqual(Decompress(verified[OriginalCount+i]))).Any())
            throw new InvalidDataException("La reconstrucción alteró imágenes originales.");
        var files=new[]{new FileChange(s.Code,s.CodeBytes,code),new FileChange(s.Icons,s.Archive,archive)};
        var proposal=new EditorProposal(_owner,"Instalar iconos de mentas XY",0,files,
            Ids.Select((id,i)=>new BatchChange(id,"Menta XY","Icono",OriginalImage(id).ToString(),(OriginalCount+i).ToString())),
            new Dictionary<string,string>{{"ActionId",ActionId},{"Source","PokeAPI/sprites"},{"SourceCommit",UsumMintIconSources.Commit},{"Conversion","XY-BCLIM-30-PaletteRGB5A1-HighNibble-v2"}},s.Dependencies);
        _preparedTextures=textures.Select(t=>(byte[])t.Clone()).ToArray();
        return new(Gen6MintNativeState.Original,"21 iconos preparados; solo ejecutable y GARC de imágenes.",proposal);
    }
    internal void Apply(Gen6MintIconPreview preview)
    {
        if(preview.State!=Gen6MintNativeState.Original||preview.Proposal is not {} proposal||proposal.Owner!=_owner||_preparedTextures is null)
            throw new InvalidOperationException("No hay propuesta de iconos propia.");
        var canonical=Prepare(_preparedTextures).Proposal!;
        if(proposal.Action!=canonical.Action||proposal.Seed!=canonical.Seed||!proposal.Parameters.OrderBy(p=>p.Key).SequenceEqual(canonical.Parameters.OrderBy(p=>p.Key))||
            proposal.Files.Count!=canonical.Files.Count||proposal.Files.Where((f,i)=>f.Path!=canonical.Files[i].Path||!f.Original().AsSpan().SequenceEqual(canonical.Files[i].Original())||!f.Proposed().AsSpan().SequenceEqual(canonical.Files[i].Proposed())).Any()||
            !proposal.Changes.SequenceEqual(canonical.Changes)||proposal.Dependencies.Count!=canonical.Dependencies.Count||
            proposal.Dependencies.Where((d,i)=>d.Path!=canonical.Dependencies[i].Path||!d.Fingerprint.AsSpan().SequenceEqual(canonical.Dependencies[i].Fingerprint)).Any())
            throw new InvalidDataException("Vista previa de iconos alterada u obsoleta.");
        var locks=new List<FileStream>();try{
            foreach(var (path,fingerprint) in canonical.Dependencies){CheckPath(path);
                if(canonical.Files.Any(f=>f.Path==path))GarcTransaction.ReadUnchanged(path,fingerprint);
                else{var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);locks.Add(stream);if(!SHA256.HashData(stream).AsSpan().SequenceEqual(fingerprint))throw new IOException("Dependencia cambió.");}}
            project.History.Commit(canonical.Files,"Mentas XY","Instalar iconos de mentas XY",canonical.Changes,canonical.Parameters,section:"Gen6NatureMintIcons");
        }finally{foreach(var stream in locks)stream.Dispose();}
        if(Inspect().State!=Gen6MintNativeState.Installed)throw new IOException("No se reconocen los iconos tras instalar.");
    }
    private void CheckPath(string path)
    {
        string root=Path.GetFullPath(project.ProjectRoot),full=Path.GetFullPath(path);
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Archivo fuera del proyecto.");
        for(string? at=full;at is not null&&at.Length>=root.Length;at=Path.GetDirectoryName(at))
            if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("No se permiten enlaces.");
    }
}
