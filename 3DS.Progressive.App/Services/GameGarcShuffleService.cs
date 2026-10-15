using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;
internal sealed record GarcShufflePreview(string Path,string Relative,FileChange File,IReadOnlyList<BatchChange> Changes,int Seed);
internal sealed class GameGarcShuffleService(GameProjectSession project)
{
    private static readonly HashSet<string> XyBans=new("005 007 012 041 042 072 073 074 075 076 078 079 080 081 082 083 084 085 086 087 101 165 218".Split(' '));
    private static readonly HashSet<string> OrasBans=new("005 008 013 039 040 071 072 073 074 075 076 078 079 080 081 082 083 084 085 086 100 152 195".Split(' '));
    internal IReadOnlyList<string> Files=>project.Generation==6?Directory.GetFiles(Path.Combine(project.GameConfiguration.RomFS,"a"),"*",SearchOption.AllDirectories).Select(path=>Path.GetRelativePath(project.GameConfiguration.RomFS,path).Replace('\\','/')).Where(path=>System.Text.RegularExpressions.Regex.IsMatch(path,@"^a/[0-9]/[0-9]/[0-9]$")&&!Banned(path)).Order().ToArray():[];
    private bool Banned(string relative)=>(project.GameConfiguration.ORAS?OrasBans:XyBans).Contains(relative.Replace("a/","").Replace("/",""));
    private static (int Offset,int Bytes,uint Vector)[] Table(byte[] source)
    {
        using var reader=new BinaryReader(new MemoryStream(source));
        if(source.Length<28||reader.ReadUInt32()!=0x47415243)throw new InvalidDataException("GARC inválido.");
        int header=checked((int)reader.ReadUInt32());if(reader.ReadUInt16()!=0xFEFF)throw new InvalidDataException("Endian GARC incompatible.");
        ushort version=reader.ReadUInt16();if(version is not (0x400 or 0x600)||header!=(version==0x400?28:36)||reader.ReadUInt32()!=4)throw new InvalidDataException("Versión GARC incompatible.");
        int dataOffset=checked((int)reader.ReadUInt32());if(reader.ReadUInt32()!=source.Length)throw new InvalidDataException("Longitud GARC inválida.");
        reader.BaseStream.Position=header;if(reader.ReadUInt32()!=0x4641544F)throw new InvalidDataException("FATO inválida.");
        int fatoSize=reader.ReadInt32(),count=reader.ReadUInt16();reader.ReadUInt16();if(fatoSize!=12+count*4||count==0)throw new InvalidDataException("FATO inconsistente.");
        int[] offsets=Enumerable.Range(0,count).Select(_=>reader.ReadInt32()).ToArray();
        int fatbStart=checked(header+fatoSize);if(reader.ReadUInt32()!=0x46415442)throw new InvalidDataException("FATB inválida.");
        int fatbSize=reader.ReadInt32();if(reader.ReadInt32()!=count||fatbSize<12||fatbStart+fatbSize>source.Length-12)throw new InvalidDataException("FATB inconsistente.");
        var rows=new List<(int,int,uint)>();int payload=source.Length-dataOffset;
        for(int i=0;i<count;i++)
        {
            int at=checked((int)reader.BaseStream.Position);if(at-fatbStart-12!=offsets[i])throw new InvalidDataException("Offset FATO inconsistente.");
            uint vector=reader.ReadUInt32();int subfiles=BitOperations.PopCount(vector);
            for(int sub=0;sub<subfiles;sub++){int start=reader.ReadInt32(),end=reader.ReadInt32(),length=reader.ReadInt32();if(start<0||end<start||end>payload||length<0||length>end-start)throw new InvalidDataException("Subarchivo GARC fuera de rango.");}
            rows.Add((at,checked(4+subfiles*12),vector));
        }
        if(reader.BaseStream.Position!=fatbStart+fatbSize||reader.ReadUInt32()!=0x46494D42||reader.ReadUInt32()!=12||reader.ReadUInt32()!=payload||reader.BaseStream.Position!=dataOffset)throw new InvalidDataException("FIMB inconsistente.");
        return rows.ToArray();
    }
    internal GarcShufflePreview Preview(string relative,int seed)
    {
        if(project.Generation!=6)throw new InvalidOperationException("El Shuffler original está disponible para Gen6.");
        if(!System.Text.RegularExpressions.Regex.IsMatch(relative,@"^a/[0-9]/[0-9]/[0-9]$")||Banned(relative))throw new InvalidDataException("GARC protegido o ruta no compatible.");
        string path=Path.Combine(project.GameConfiguration.RomFS,relative.Replace('/',Path.DirectorySeparatorChar));
        if(new FileInfo(path).Length>128*1024*1024)throw new InvalidDataException("El Shuffler admite archivos de hasta 128 MiB.");
        byte[] source=File.ReadAllBytes(path);var rows=Table(source);int[] ids=Enumerable.Range(0,rows.Length).Where(id=>BitOperations.PopCount(rows[id].Vector)==1).ToArray();
        if(ids.Length<2)throw new InvalidDataException("El GARC no tiene al menos dos archivos simples para mezclar.");
        int[] order=(int[])ids.Clone();var random=new Random(seed);for(int i=order.Length-1;i>0;i--){int j=random.Next(i+1);(order[i],order[j])=(order[j],order[i]);}
        byte[] output=(byte[])source.Clone();var changes=new List<BatchChange>();
        for(int i=0;i<ids.Length;i++){var from=rows[order[i]];var to=rows[ids[i]];if(ids[i]==order[i])continue;source.AsSpan(from.Offset,from.Bytes).CopyTo(output.AsSpan(to.Offset,to.Bytes));changes.Add(new(ids[i],"Archivo "+ids[i],"Referencia FATB",ids[i].ToString(),order[i].ToString()));}
        Table(output);return new(path,relative,new FileChange(path,source,output),changes.AsReadOnly(),seed);
    }
    internal void Apply(GarcShufflePreview preview,string backup)
    {
        if(project.Generation!=6||preview.Path!=Path.Combine(project.GameConfiguration.RomFS,preview.Relative.Replace('/',Path.DirectorySeparatorChar))||preview.Changes.Count==0)throw new InvalidOperationException("Propuesta no válida para este proyecto.");
        byte[] original=GarcTransaction.ReadUnchanged(preview.Path,preview.File.Fingerprint);backup=Path.GetFullPath(backup);
        if(backup.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.ProjectRoot))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Guardá el backup fuera del proyecto.");
        string relative=Path.GetRelativePath(project.ProjectRoot,preview.Path).Replace('\\','/');
        using(var stream=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))
        {using(var file=zip.CreateEntry("project/"+relative,CompressionLevel.Fastest).Open())file.Write(original);using var metadata=zip.CreateEntry("backup-manifest.json").Open();JsonSerializer.Serialize(metadata,new BackupManifest(1,project.GameName,DateTimeOffset.UtcNow,[new(relative,original.Length,Convert.ToHexString(preview.File.Fingerprint))]));}
        project.History.Commit([preview.File],"GARC Shuffler","Mezclar referencias FATB",preview.Changes,new Dictionary<string,string>{{"Scope",preview.Relative},{"PreviewSeed",preview.Seed.ToString()},{"Backup",backup}});
    }
}
