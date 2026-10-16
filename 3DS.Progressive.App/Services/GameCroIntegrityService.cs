using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>The four SHA-256 regions used by vanilla CRO.HashCRO. No RSA signing.</summary>
internal static class CroIntegrity
{
    internal static byte[] Rebuild(byte[] source)
    {
        if(source.Length<0x180||!source.AsSpan(0x80,4).SequenceEqual("CRO0"u8))
            throw new InvalidDataException("Cabecera CRO0 ausente o incompleta.");
        int code=BitConverter.ToInt32(source,0xB0),codeSize=BitConverter.ToInt32(source,0xB4);
        int data=BitConverter.ToInt32(source,0xB8),dataSize=BitConverter.ToInt32(source,0xBC);
        int readOnly=BitConverter.ToInt32(source,0xC0);
        byte[] result=(byte[])source.Clone();
        Write(0,0x80,0x100);Write(0x20,code,codeSize);
        if(data<readOnly)throw new InvalidDataException("Regiones CRO fuera de orden.");
        Write(0x40,readOnly,data-readOnly);Write(0x60,data,dataSize);
        return result;
        void Write(int output,int offset,int size)
        {
            if(offset<0||size<0||(long)offset+size>source.Length||(size>0&&offset<0x180&&output!=0))
                throw new InvalidDataException("Los límites de las regiones CRO no son válidos.");
            SHA256.HashData(source.AsSpan(offset,size)).CopyTo(result,output);
        }
    }
    internal static byte[] RebuildCrr(byte[] source,IReadOnlyList<byte[]> cros)
    {
        if(source.Length<0x358)throw new InvalidDataException("CRR incompleto.");
        int start=BitConverter.ToInt32(source,0x350),count=BitConverter.ToInt32(source,0x354);
        if(count!=cros.Count||count<1||start<0x358||(long)start+32L*count>source.Length)
            throw new InvalidDataException("La cantidad de CROs o los límites de la tabla CRR no coinciden.");
        // Hex ordinal order equals the byte order used by the original hash table.
        string[] hashes=cros.Select(c=>Convert.ToHexString(SHA256.HashData(c.AsSpan(0,0x80))))
            .Order(StringComparer.Ordinal).ToArray();
        byte[] result=(byte[])source.Clone();
        for(int i=0;i<hashes.Length;i++)Convert.FromHexString(hashes[i]).CopyTo(result,start+32*i);
        return result;
    }
}

internal sealed class GameCroIntegrityService(GameProjectSession project)
{
    private readonly Guid _owner=Guid.NewGuid();
    private string[] Inventory()=>Directory.GetFiles(project.GameConfiguration.RomFS)
        .Where(p=>Path.GetExtension(p).Equals(".cro",StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    internal EditorProposal Preview(bool updateCrr)
    {
        string[] paths=Inventory();
        if(paths.Length==0)throw new FileNotFoundException("No hay archivos CRO en RomFS.");
        var files=new List<FileChange>();var changes=new List<BatchChange>();int done=0;
        foreach(string path in paths)
        {
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("El archivo CRO debe pertenecer a la carpeta del proyecto.");
            byte[] before=File.ReadAllBytes(path),after=CroIntegrity.Rebuild(before);
            if(!CroIntegrity.Rebuild(after).AsSpan().SequenceEqual(after))
                throw new InvalidDataException("No coincide la verificación de hashes CRO.");
            var file=new FileChange(path,before,after);files.Add(file);
            if(file.Changed)changes.Add(new(++done,Path.GetFileName(path),"Hashes CRO SHA-256",
                Convert.ToHexString(SHA256.HashData(before.AsSpan(0,0x80))),Convert.ToHexString(SHA256.HashData(after.AsSpan(0,0x80)))));
            UiProgress.Count("Calculando hashes CRO",files.Count,paths.Length+(updateCrr?1:0));
        }
        if(updateCrr)
        {
            string path=Path.Combine(project.GameConfiguration.RomFS,".crr","static.crr");
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("El archivo CRR debe pertenecer al proyecto.");
            byte[] before=File.ReadAllBytes(path);
            byte[] after=CroIntegrity.RebuildCrr(before,files.Select(f=>f.Proposed()).ToArray());
            var file=new FileChange(path,before,after);files.Add(file);
            if(file.Changed)changes.Add(new(++done,"static.crr","Tabla de hashes CRR",
                Convert.ToHexString(SHA256.HashData(before)),Convert.ToHexString(SHA256.HashData(after))));
            UiProgress.Count("Tabla CRR verificada",files.Count,files.Count);
        }
        foreach(var file in files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        return new(_owner,"Reconstruir hashes CRO"+(updateCrr?" y CRR":""),0,files,changes,
            new Dictionary<string,string>{{"UpdateCRR",updateCrr.ToString()},{"CROCount",paths.Length.ToString()}},
            files.Select(f=>(f.Path,f.Fingerprint)));
    }
    internal void Apply(EditorProposal proposal)
    {
        using var manual=ManualOperationScope.Begin("crointegrity","crointegrity",new{UpdateCRR=bool.Parse(proposal.Parameters["UpdateCRR"])});
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otro proyecto.");
        string[] expected=proposal.Files.Where(f=>Path.GetExtension(f.Path).Equals(".cro",StringComparison.OrdinalIgnoreCase))
            .Select(f=>f.Path).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if(!expected.SequenceEqual(Inventory(),StringComparer.OrdinalIgnoreCase))
            throw new IOException("Cambió la lista de CROs. Generá otra vista previa.");
        foreach(var dependency in proposal.Dependencies)GarcTransaction.ReadUnchanged(dependency.Path,dependency.Fingerprint);
        project.History.Commit(proposal.Files,"Herramientas ROM",proposal.Action,proposal.Changes,proposal.Parameters);
    }
}
