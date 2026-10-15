using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;

internal sealed record ExtractedFile(string Path,long Bytes,string Sha256);
internal sealed record RomExtractionResult(string Folder,int Files,string SourceSha256);
internal sealed record RomExtractionManifest(int Version,string Container,long SourceBytes,string SourceSha256,
    bool CodeDecompressed,IReadOnlyList<ExtractedFile> Files);

/// <summary>Read-only container parsing, bounded streaming copies, and publication of
/// a new folder only after readback. No hard-coded NCSD partition start or legacy recursion.</summary>
internal sealed class GameRomExtractionService(string? protectedProject=null)
{
    private sealed record Part(string Path,long Offset,long Length);
    private sealed record Plan(List<Part> Files,List<string> Directories,bool CodeCompressed);
    private static readonly UnicodeEncoding Names=new(false,false,true);
    internal async Task<RomExtractionResult> ExtractAsync(string input,string destination,CancellationToken cancellation=default)
    {
        input=Path.GetFullPath(input);destination=Path.GetFullPath(destination);
        if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("El destino ya existe. Elegí una carpeta nueva.");
        string parent=Path.GetDirectoryName(destination)??throw new ArgumentException("Destino inválido.");
        if(!Directory.Exists(parent))throw new DirectoryNotFoundException("La carpeta de destino no existe.");
        if(protectedProject is not null)
        {
            string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedProject));
            if(destination.Equals(root,StringComparison.OrdinalIgnoreCase)||destination.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new IOException("Extraé en una carpeta fuera del proyecto abierto.");
        }
        string stage=Path.Combine(parent,".progressive-extract-"+Guid.NewGuid().ToString("N"));
        try
        {
            return await Task.Run(()=>
            {
                cancellation.ThrowIfCancellationRequested();
                using var source=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read);
                UiProgress.Stage("Leyendo cabeceras y límites de la ROM…");
                Directory.CreateDirectory(stage);
                string decodedPath=Path.Combine(stage,".fixed-key-container.tmp");
                using var decoded=DecodeFixedKey(source,decodedPath,cancellation);
                var content=decoded??source;
                Plan plan=Inspect(content,cancellation);
                foreach(string dir in plan.Directories)Directory.CreateDirectory(Path.Combine(stage,dir));
                var records=new List<ExtractedFile>();int done=0;
                foreach(var part in plan.Files)
                {
                    cancellation.ThrowIfCancellationRequested();UiProgress.Stage("Extrayendo "+part.Path);
                    string path=Path.Combine(stage,part.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    string hash=Copy(content,part.Offset,part.Length,path,cancellation);
                    using(var verify=File.OpenRead(path))
                        if(verify.Length!=part.Length||Convert.ToHexString(SHA256.HashData(verify))!=hash)
                            throw new IOException("La relectura no coincide: "+part.Path);
                    records.Add(new(part.Path.Replace('\\','/'),part.Length,hash));
                    UiProgress.Count("Archivos extraídos y verificados",++done,plan.Files.Count+1);
                }
                string code=Path.Combine(stage,"ExeFS",".code.bin");bool decompressed=false;
                if(File.Exists(code))
                {
                    long length=new FileInfo(code).Length;
                    if(plan.CodeCompressed||length%0x200!=0)
                    {
                        UiProgress.Stage("Preparando code.bin descomprimido…");
                        long expected=BlzDecodedSize(code);
                        if(expected>0)
                        {
                            using var progress=GameRomRebuildService.CreateLegacyProgressBar();
                            _=new BLZCoder(["-d",code],progress);
                            cancellation.ThrowIfCancellationRequested();
                            if(new FileInfo(code).Length!=expected)throw new InvalidDataException("No se pudo verificar la descompresión de code.bin.");
                            decompressed=true;
                            int at=records.FindIndex(f=>f.Path=="ExeFS/.code.bin");
                            using var stream=File.OpenRead(code);records[at]=new("ExeFS/.code.bin",stream.Length,Convert.ToHexString(SHA256.HashData(stream)));
                        }
                        else if(length%0x200!=0)throw new InvalidDataException("code.bin comprimido o incompatible; la extracción no se publicó.");
                    }
                }
                UiProgress.Stage("Calculando SHA-256 del contenedor original…");
                source.Position=0;string sourceHash=Hash(source,cancellation);
                cancellation.ThrowIfCancellationRequested();
                using(var manifest=new FileStream(Path.Combine(stage,"extraction-manifest.json"),FileMode.CreateNew,FileAccess.Write))
                    JsonSerializer.Serialize(manifest,new RomExtractionManifest(1,Path.GetFileName(input),source.Length,sourceHash,decompressed,records),new JsonSerializerOptions{WriteIndented=true});
                decoded?.Dispose();
                if(File.Exists(decodedPath))File.Delete(decodedPath);
                // The source handle stays open without write/delete sharing until publication.
                Directory.Move(stage,destination);
                UiProgress.Count("Extracción verificada",plan.Files.Count+1,plan.Files.Count+1);
                return new RomExtractionResult(destination,records.Count,sourceHash);
            },cancellation).ConfigureAwait(false);
        }
        finally
        {
            // Only this unique staging directory is removed; never the destination or source.
            if(Directory.Exists(stage))Directory.Delete(stage,true);
        }
    }
    // CTRUtil.BuildROM uses fixed-zero-key AES for ordinary application titles.
    // Decode only that documented local rebuild format; no external keys or
    // fallback attempts for retail or system-title encryption.
    private static FileStream? DecodeFixedKey(FileStream source,string output,CancellationToken cancellation)
    {
        byte[] top=Read(source,0,0x200);long start=0,length=source.Length;
        if(top.AsSpan(0x100,4).SequenceEqual("NCSD"u8))
        {
            start=BitConverter.ToUInt32(top,0x120)*0x200L;length=BitConverter.ToUInt32(top,0x124)*0x200L;Bounds(start,length,source.Length);
        }
        byte[] header=Read(source,start,0x200);
        if(!header.AsSpan(0x100,4).SequenceEqual("NCCH"u8))return null;
        byte flags=header[0x18F];if((flags&4)!=0||(flags&1)==0)return null;
        ulong program=BitConverter.ToUInt64(header,0x118);
        if(program>>32!=0x00040000||BitConverter.ToUInt16(header,0x112)!=2||header[0x18B]!=0||header[0x18E]!=0)
            throw new InvalidDataException("Solo se admite el cifrado de clave fija de reconstrucciones pk3DS para aplicaciones normales.");
        long size=BitConverter.ToUInt32(header,0x104)*0x200L;
        if(size<0x200||size>length)throw new InvalidDataException("Partición de clave fija truncada.");
        var regions=new[]{(Offset:0x200L,Length:BitConverter.ToUInt32(header,0x180)*2L),
            (Offset:BitConverter.ToUInt32(header,0x1A0)*0x200L,Length:BitConverter.ToUInt32(header,0x1A4)*0x200L),
            (Offset:BitConverter.ToUInt32(header,0x1B0)*0x200L,Length:BitConverter.ToUInt32(header,0x1B4)*0x200L)};
        for(int i=0;i<regions.Length;i++)
        {
            var region=regions[i];Bounds(region.Offset,region.Length,size);
            if(region.Offset<0x200||region.Length==0||region.Length%16!=0||regions.Take(i).Any(r=>region.Offset<r.Offset+r.Length&&region.Offset+region.Length>r.Offset))throw new InvalidDataException("Regiones de clave fija inválidas.");
        }
        UiProgress.Stage("Preparando copia del contenedor reconstruido con clave fija…");
        using(var target=new FileStream(output,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
        {
            source.Position=0;byte[] buffer=new byte[0x100000];
            while(true){cancellation.ThrowIfCancellationRequested();int count=source.Read(buffer);if(count==0)break;target.Write(buffer,0,count);}
            byte[] plain=new byte[buffer.Length];
            for(int i=0;i<regions.Length;i++)
            {
                var cipher=new AesCtr(new byte[16],program,(ulong)(i+1)<<56);long position=start+regions[i].Offset,remaining=regions[i].Length;
                while(remaining>0)
                {
                    cancellation.ThrowIfCancellationRequested();int count=(int)Math.Min(buffer.Length,remaining);target.Position=position;target.ReadExactly(buffer.AsSpan(0,count));
                    cipher.TransformBlock(buffer,0,count,plain,0);target.Position=position;target.Write(plain,0,count);position+=count;remaining-=count;
                }
            }
            target.Position=start+0x18F;target.WriteByte((byte)((flags&~1)|4));
        }
        return new FileStream(output,FileMode.Open,FileAccess.Read,FileShare.Read);
    }
    private static Plan Inspect(FileStream source,CancellationToken cancellation)
    {
        byte[] top=Read(source,0,0x200);long start=0,length=source.Length;
        if(top.AsSpan(0x100,4).SequenceEqual("NCSD"u8))
        {
            if(top[0x18E]!=0)throw new InvalidDataException("Tamaño de unidad NCSD no admitido.");
            start=BitConverter.ToUInt32(top,0x120)*0x200L;length=BitConverter.ToUInt32(top,0x124)*0x200L;
            Bounds(start,length,source.Length);if(start<0x200||length<0x200)throw new InvalidDataException("Partición principal NCSD inválida.");
        }
        byte[] header=Read(source,start,0x200);
        if(!header.AsSpan(0x100,4).SequenceEqual("NCCH"u8)||header[0x18E]!=0)
            throw new InvalidDataException("Se requiere un contenedor CXI/3DS con partición NCCH compatible.");
        long content=BitConverter.ToUInt32(header,0x104)*0x200L;
        if(content<0x200||content>length)throw new InvalidDataException("La partición NCCH está truncada.");
        length=content;var files=new List<Part>{new("ncchheader.bin",start,0x200)};
        long exheader=BitConverter.ToUInt32(header,0x180)*2L;
        if(exheader<0x800||exheader>0x20000)throw new InvalidDataException("ExHeader ejecutable incompleto o incompatible.");
        Bounds(0x200,exheader,length);files.Add(new("exheader.bin",start+0x200,exheader));
        byte[] exh=Read(source,start+0x200,0x400);
        var regions=new List<(long Offset,long Length)>();
        Optional(0x190,"plain.bin");Optional(0x198,"logo.bcma.lz");
        var exefs=Region(0x1A0);var romfs=Region(0x1B0);
        if(exefs.Length<0x200||romfs.Length<0x60)throw new InvalidDataException("RomFS y ExeFS son necesarios para extraer un proyecto.");
        byte[] exe=Read(source,exefs.Offset,0x200);var ranges=new List<(long Start,long End)>();
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<10;i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var bytes=exe.AsSpan(i*0x10,8);int terminator=bytes.IndexOf((byte)0);
            if(terminator>=0)bytes=bytes[..terminator];if(bytes.Length==0)continue;
            if(bytes.ToArray().Any(b=>b<0x21||b>0x7E))throw new InvalidDataException("Nombre ExeFS incompatible; ROM cifrada o dañada.");
            string name=Encoding.ASCII.GetString(bytes);CheckName(name);
            long offset=BitConverter.ToUInt32(exe,i*0x10+8),size=BitConverter.ToUInt32(exe,i*0x10+12);
            Bounds(0x200+offset,size,exefs.Length);
            if(size==0&&name is ".code" or "code")throw new InvalidDataException("code.bin está vacío.");
            if(ranges.Any(r=>offset<r.End&&offset+size>r.Start)||!paths.Add(name))throw new InvalidDataException("Entradas ExeFS superpuestas o duplicadas.");
            ranges.Add((offset,offset+size));
            var part=new Part(Path.Combine("ExeFS",name+".bin"),exefs.Offset+0x200+offset,size);
            source.Position=part.Offset;string hash=HashRegion(source,size,cancellation);
            if(hash!=Convert.ToHexString(exe.AsSpan(0x200-32*(i+1),32)))throw new InvalidDataException("El hash ExeFS no coincide: "+name+". Se requiere una ROM descifrada y válida.");
            files.Add(part);
        }
        if(!paths.Contains(".code")&&!paths.Contains("code"))throw new InvalidDataException("ExeFS no contiene code.bin.");
        // Normalize the code file name for the existing editor, retaining other names.
        for(int i=0;i<files.Count;i++)if(files[i].Path==Path.Combine("ExeFS","code.bin"))files[i]=files[i] with{Path=Path.Combine("ExeFS",".code.bin")};
        if(paths.Contains(".code")&&paths.Contains("code"))throw new InvalidDataException("ExeFS tiene dos ejecutables code.bin.");
        var dirs=new List<string>{"ExeFS","RomFS"};ParseRomFs(source,romfs.Offset,romfs.Length,files,dirs,cancellation);
        return new(files,dirs,(exh[0xD]&1)!=0);
        (long Offset,long Length) Region(int at)
        {
            long offset=BitConverter.ToUInt32(header,at)*0x200L,size=BitConverter.ToUInt32(header,at+4)*0x200L;
            Bounds(offset,size,length);if(size>0&&offset<0x200+exheader)throw new InvalidDataException("Segmentos NCCH superpuestos.");
            if(size>0)
            {
                if(regions.Any(r=>offset<r.Offset+r.Length&&offset+size>r.Offset))throw new InvalidDataException("Segmentos NCCH superpuestos.");
                regions.Add((offset,size));
            }
            return(start+offset,size);
        }
        void Optional(int at,string name){var region=Region(at);if(region.Length>0)files.Add(new(name,region.Offset,region.Length));}
    }
    private static void ParseRomFs(FileStream source,long start,long length,List<Part> files,List<string> directories,CancellationToken cancellation)
    {
        byte[] ivfc=Read(source,start,0x60);
        if(!ivfc.AsSpan(0,4).SequenceEqual("IVFC"u8)||BitConverter.ToUInt32(ivfc,4)!=0x10000)
            throw new InvalidDataException("RomFS IVFC no reconocido; se requiere una ROM descifrada.");
        int exponent=BitConverter.ToInt32(ivfc,0x4C);
        if(exponent is <9 or >20)throw new InvalidDataException("Bloque IVFC incompatible.");
        long block=1L<<exponent,master=BitConverter.ToUInt32(ivfc,8),body=((0x60+master+block-1)/block)*block;
        ulong declared=BitConverter.ToUInt64(ivfc,0x44);if(declared>long.MaxValue)throw new InvalidDataException("Tamaño RomFS inválido.");
        long bodyLength=(long)declared;Bounds(body,bodyLength,length);Bounds(0,0x28,bodyLength);
        byte[] info=Read(source,start+body,0x28);
        if(BitConverter.ToUInt32(info,0)!=0x28)throw new InvalidDataException("Cabecera de metadatos RomFS inválida.");
        byte[] dir=Metadata(0xC),file=Metadata(0x1C);long data=BitConverter.ToUInt32(info,0x24);
        Bounds(data,0,bodyLength);
        var seenDirs=new HashSet<uint>();var seenFiles=new HashSet<uint>();
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"RomFS"};
        var pending=new Stack<(uint Offset,uint Parent,string ParentPath,int Depth)>();pending.Push((0,0,"RomFS",0));
        while(pending.TryPop(out var node))
        {
            cancellation.ThrowIfCancellationRequested();
            if(!seenDirs.Add(node.Offset)||node.Depth>128)throw new InvalidDataException("Ciclo o profundidad inválida en directorios RomFS.");
            int at=Index(dir,node.Offset,24);uint parent=BitConverter.ToUInt32(dir,at);
            if(node.Offset!=0&&parent!=node.Parent)throw new InvalidDataException("Padre de directorio RomFS incompatible.");
            string name=Name(dir,at,24,BitConverter.ToUInt32(dir,at+20),node.Offset==0);
            string path=node.Offset==0?node.ParentPath:Path.Combine(node.ParentPath,name);
            if(node.Offset!=0&&!paths.Add(path))throw new InvalidDataException("Rutas RomFS duplicadas.");
            directories.Add(path);
            uint sibling=BitConverter.ToUInt32(dir,at+4),child=BitConverter.ToUInt32(dir,at+8),item=BitConverter.ToUInt32(dir,at+12);
            if(sibling!=uint.MaxValue)pending.Push((sibling,node.Parent,node.ParentPath,node.Depth));
            if(child!=uint.MaxValue)pending.Push((child,node.Offset,path,node.Depth+1));
            while(item!=uint.MaxValue)
            {
                cancellation.ThrowIfCancellationRequested();
                if(!seenFiles.Add(item))throw new InvalidDataException("Ciclo o referencia duplicada en archivos RomFS.");
                int f=Index(file,item,32);if(BitConverter.ToUInt32(file,f)!=node.Offset)throw new InvalidDataException("Padre de archivo RomFS incompatible.");
                string filePath=Path.Combine(path,Name(file,f,32,BitConverter.ToUInt32(file,f+28),false));
                if(!paths.Add(filePath))throw new InvalidDataException("Archivo RomFS duplicado.");
                ulong relative=BitConverter.ToUInt64(file,f+8),size=BitConverter.ToUInt64(file,f+16);
                if(relative>long.MaxValue||size>long.MaxValue)throw new InvalidDataException("Tamaño de archivo RomFS inválido.");
                if((long)relative>bodyLength-data)throw new InvalidDataException("Archivo RomFS fuera de límites.");
                long offset=data+(long)relative;Bounds(offset,(long)size,bodyLength);
                files.Add(new(filePath,start+body+offset,(long)size));item=BitConverter.ToUInt32(file,f+4);
            }
        }
        byte[] Metadata(int at)
        {
            long offset=BitConverter.ToUInt32(info,at),size=BitConverter.ToUInt32(info,at+4);
            Bounds(offset,size,bodyLength);if(size>64*1024*1024)throw new InvalidDataException("Metadatos RomFS demasiado grandes.");
            return Read(source,start+body+offset,(int)size);
        }
    }
    private static int Index(byte[] metadata,uint offset,int header)
    {
        if((offset&3)!=0||(long)offset+header>metadata.Length)throw new InvalidDataException("Referencia de metadatos RomFS inválida.");
        return(int)offset;
    }
    private static string Name(byte[] bytes,int offset,int header,uint length,bool root)
    {
        if((length&1)!=0||(long)offset+header+length>bytes.Length||length>510)throw new InvalidDataException("Nombre RomFS inválido.");
        string name=Names.GetString(bytes,offset+header,(int)length);
        if(root){if(name.Length!=0)throw new InvalidDataException("La raíz RomFS no está vacía.");}
        else CheckName(name);return name;
    }
    private static void CheckName(string name)
    {
        if(string.IsNullOrEmpty(name)||name is "." or ".."||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.EndsWith('.')||name.EndsWith(' '))
            throw new InvalidDataException("Nombre de archivo incompatible con Windows.");
        string stem=name.Split('.')[0].ToUpperInvariant();
        if(stem is "CON" or "PRN" or "AUX" or "NUL"||stem.Length==4&&(stem.StartsWith("COM")||stem.StartsWith("LPT"))&&stem[3] is >= '1' and <= '9')
            throw new InvalidDataException("Nombre reservado de Windows en la ROM.");
    }
    private static void Bounds(long offset,long size,long length)
    {
        if(offset<0||size<0||offset>length||size>length-offset)throw new InvalidDataException("Segmento fuera de los límites del contenedor.");
    }
    private static byte[] Read(FileStream source,long offset,int size)
    {
        Bounds(offset,size,source.Length);byte[] result=new byte[size];source.Position=offset;source.ReadExactly(result);return result;
    }
    private static string Copy(FileStream source,long offset,long size,string output,CancellationToken cancellation)
    {
        Bounds(offset,size,source.Length);source.Position=offset;
        using var target=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        return HashRegion(source,size,cancellation,target);
    }
    private static string Hash(FileStream source,CancellationToken cancellation)=>HashRegion(source,source.Length-source.Position,cancellation);
    private static string HashRegion(FileStream source,long size,CancellationToken cancellation,Stream? target=null)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] buffer=new byte[1024*1024];
        while(size>0){cancellation.ThrowIfCancellationRequested();int wanted=(int)Math.Min(size,buffer.Length);source.ReadExactly(buffer.AsSpan(0,wanted));target?.Write(buffer,0,wanted);hash.AppendData(buffer,0,wanted);size-=wanted;}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static long BlzDecodedSize(string path)
    {
        using var stream=File.OpenRead(path);if(stream.Length<8)return 0;
        byte[] footer=Read(stream,stream.Length-8,8);uint coded=BitConverter.ToUInt32(footer,0);
        int header=(int)(coded>>24),encoded=(int)(coded&0xFFFFFF),extra=BitConverter.ToInt32(footer,4);
        long decoded=stream.Length+extra;
        return extra>0&&header>=8&&header<=encoded&&encoded<=stream.Length&&decoded<=0xFFFFFF&&decoded%0x200==0?decoded:0;
    }
}
