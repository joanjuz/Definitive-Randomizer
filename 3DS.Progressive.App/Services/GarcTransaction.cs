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
        byte[] bytes = Build(source, reference, records, mini);
        FileTransaction.Commit([new FileChange(path, source, bytes)]);
        return SHA256.HashData(bytes);
    }

    internal static byte[] Build(byte[] source, GARCReference reference, byte[][] records,
        bool mini = false, bool preserveRecordSize = true)
    {
        var original = new GARC.MemGARC(source);
        byte[][] originalRecords = mini ? Mini.UnpackMini(original.GetFile(0), "WD") : original.Files;
        if (mini && original.FileCount != 1)
            throw new InvalidDataException("El GARC WD contiene archivos inesperados.");
        if (originalRecords is null || originalRecords.Length != records.Length ||
            (preserveRecordSize && originalRecords.Where((r, i) => r.Length != records[i].Length).Any()))
            throw new InvalidDataException("La cantidad o el tamaño de los registros cambió. No se guardó nada.");
        byte[][] outer = mini ? [Mini.PackMini(records, "WD")] : records;
        original.Files = outer;
        string temp = Path.Combine(Path.GetTempPath(), "3ds-progressive-garc-" + Guid.NewGuid().ToString("N") + ".tmp");
        byte[] bytes;
        try
        {
            new GARCFile(original, reference, temp).Save();
            bytes = File.ReadAllBytes(temp);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        var check = new GARC.MemGARC(bytes);
        if (check.FileCount != outer.Length)
            throw new InvalidDataException("La cantidad de archivos del GARC regenerado no coincide.");
        byte[][] verified = mini ? Mini.UnpackMini(check.GetFile(0), "WD") : check.Files;
        if (verified is null || verified.Length != records.Length ||
            records.Where((r, i) => !r.AsSpan().SequenceEqual(verified[i])).Any())
            throw new InvalidDataException("La relectura no coincide con la propuesta. No se guardó nada.");
        return bytes;
    }
}
