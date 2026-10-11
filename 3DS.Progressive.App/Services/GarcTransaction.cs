using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;

/// <summary>Shared, verified, single-file commit. Does not mutate the live game config.</summary>
internal static class GarcTransaction
{
    internal static byte[] ReadUnchanged(string path, byte[] fingerprint)
    {
        byte[] source = File.ReadAllBytes(path);
        if (!SHA256.HashData(source).AsSpan().SequenceEqual(fingerprint))
            throw new IOException("El archivo cambió desde que abriste el proyecto o generaste la vista previa. Reabrí el proyecto.");
        return source;
    }

    internal static void RebuildPersonalMaster(byte[][] files)
    {
        if (files.Length < 2 || files.Take(files.Length - 1).Sum(r => (long)r.Length) != files[^1].Length)
            throw new InvalidDataException("La tabla maestra Personal no coincide con los registros. No se guardó nada.");
        byte[] master = new byte[files[^1].Length];
        int offset = 0;
        foreach (byte[] record in files.Take(files.Length - 1))
        {
            record.CopyTo(master, offset);
            offset += record.Length;
        }
        files[^1] = master;
    }

    internal static byte[] Commit(string path, GARCReference reference, byte[] fingerprint,
        byte[][] records, bool mini = false)
    {
        byte[] source = ReadUnchanged(path, fingerprint);
        var original = new GARC.MemGARC(source);
        byte[][] originalRecords = mini ? Mini.UnpackMini(original.GetFile(0), "WD") : original.Files;
        if (mini && original.FileCount != 1)
            throw new InvalidDataException("El GARC WD contiene archivos inesperados.");
        if (originalRecords is null || originalRecords.Length != records.Length ||
            originalRecords.Where((r, i) => r.Length != records[i].Length).Any())
            throw new InvalidDataException("La cantidad o el tamaño de los registros cambió. No se guardó nada.");
        byte[][] outer = mini ? [Mini.PackMini(records, "WD")] : records;
        string temp = path + ".studio-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var output = new GARCFile(new GARC.MemGARC(source), reference, temp) { Files = outer };
            output.Save();
            byte[] bytes = File.ReadAllBytes(temp);
            var check = new GARC.MemGARC(bytes);
            if (check.FileCount != outer.Length)
                throw new InvalidDataException("La cantidad de archivos del GARC regenerado no coincide.");
            byte[][] verified = mini ? Mini.UnpackMini(check.GetFile(0), "WD") : check.Files;
            if (verified is null || verified.Length != records.Length ||
                records.Where((r, i) => !r.AsSpan().SequenceEqual(verified[i])).Any())
                throw new InvalidDataException("La relectura no coincide con la propuesta. No se guardó nada.");
            // Last check immediately before replacing the complete, verified archive.
            ReadUnchanged(path, fingerprint);
            File.Replace(temp, path, null);
            byte[] persisted = File.ReadAllBytes(path);
            if (!persisted.AsSpan().SequenceEqual(bytes))
                throw new IOException("No coincide la relectura del archivo final. Reabrí el proyecto.");
            return SHA256.HashData(persisted);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
