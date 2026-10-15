using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Gen6 vanilla CRO offsets and Gen7 TypeChart7 signature; in-memory edits until verified commit.</summary>
internal sealed class GameTypeChartService
{
    private const int TypeCount=18;
    private readonly GameProjectSession _project;
    private readonly string _codePath;
    private byte[] _fingerprint;
    private static readonly byte[] Signature=[
        0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
        0xC3, 0x00, 0x00, 0x00, 0xCB, 0x00, 0x00, 0x00, 0xD3, 0x00, 0x00, 0x00, 0xDB, 0x00, 0x00, 0x00,
        0xF3, 0x00, 0x00, 0x00, 0xFB, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
    ];
    internal string[] Types=>_project.Types.Take(TypeCount).ToArray();
    internal GameTypeChartService(GameProjectSession project)
    {
        _project=project;
        string exe=project.GameConfiguration.ExeFS;
        _codePath=project.Generation==6 ? Path.Combine(project.GameConfiguration.RomFS,"DllBattle.cro") : new[]{Path.Combine(exe,".code.bin"),Path.Combine(exe,"code.bin")}
            .FirstOrDefault(File.Exists)??throw new FileNotFoundException("Falta code.bin sin comprimir.");
        byte[] source=File.ReadAllBytes(_codePath);
        if(project.Generation==7&&source.Length%0x200!=0)throw new InvalidDataException("code.bin no está descomprimido.");
        _fingerprint=SHA256.HashData(source);
        _=FindOffset(source);
    }
    private int FindOffset(byte[] raw)
    {
        if(_project.Generation==6)
        {
            int offset=_project.GameConfiguration.ORAS?0xDB428:0xD12A8;
            if(raw.Length<offset+TypeCount*TypeCount||!raw.AsSpan(0x80,4).SequenceEqual("CRO0"u8)||raw.AsSpan(offset,TypeCount*TypeCount).ToArray().Any(x=>x is not (0 or 2 or 4 or 8)))
                throw new InvalidDataException("DllBattle.cro no contiene una tabla de tipos Gen6 válida.");
            return offset;
        }
        int at=raw.AsSpan().IndexOf(Signature);
        if(at<0||at+Signature.Length+TypeCount*TypeCount>raw.Length)
            throw new InvalidDataException("No se encontró la firma de la tabla de tipos Gen7.");
        if(raw.AsSpan(at+1).IndexOf(Signature)>=0)
            throw new InvalidDataException("Se detectaron múltiples firmas; tabla de tipos ambigua.");
        int start=at+Signature.Length;
        if(raw.AsSpan(start,TypeCount*TypeCount).ToArray().Any(x=>x is not (0 or 2 or 4 or 8)))
            throw new InvalidDataException("Valores no reconocidos en la tabla de tipos del ejecutable.");
        return start;
    }
    internal static byte[] Inverse(byte[] table)
    {
        if(table.Length!=324||table.Any(v=>v is not (0 or 2 or 4 or 8)))throw new ArgumentException("Tabla de tipos inválida.");
        return table.Select(v=>(byte)(v switch {0 or 2=>8,8=>2,_=>4})).ToArray();
    }
    internal byte[] Read()
    {
        byte[] source=GarcTransaction.ReadUnchanged(_codePath,_fingerprint);
        return source.AsSpan(FindOffset(source),TypeCount*TypeCount).ToArray();
    }
    internal (FileChange File,IReadOnlyList<BatchChange> Changes) Preview(byte[] changed,bool repairCro=false)
    {
        if(changed.Length!=TypeCount*TypeCount||changed.Any(x=>x is not (0 or 2 or 4 or 8)))
            throw new ArgumentException("La tabla debe contener 324 valores 0, 2, 4 u 8.");
        byte[] before=GarcTransaction.ReadUnchanged(_codePath,_fingerprint);
        int offset=FindOffset(before);
        byte[] result=(byte[])before.Clone();
        changed.CopyTo(result,offset);
        var original=before.AsSpan(offset,changed.Length);
        var changes=new List<BatchChange>();
        for(int i=0;i<changed.Length;i++)
        {
            if(original[i]==changed[i])continue;
            changes.Add(new(i/TypeCount,Types[i/TypeCount],
                "Defiende "+Types[i%TypeCount],original[i].ToString(),changed[i].ToString()));
        }
        if(repairCro&&_project.Generation==6&&changes.Count>0)result=CroIntegrity.Rebuild(result);
        return(new FileChange(_codePath,before,result),changes);
    }
    internal void Apply(FileChange file,IReadOnlyList<BatchChange> changes)
    {
        GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit([file],"Mecánicas y CRO","Editar tabla de tipos",changes,
            new Dictionary<string,string>{{"CellsChanged",changes.Count.ToString()},
                {"SourceSHA",Convert.ToHexString(_fingerprint)}});
        _fingerprint=SHA256.HashData(File.ReadAllBytes(_codePath));
    }
}
