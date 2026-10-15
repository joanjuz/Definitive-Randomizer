using System.IO;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;

/// <summary>Convert physical Gen6 GARC order to the original editor folder order:
/// raw zone/EN members first, then decompressed maps. Gen7 is not reordered.</summary>
internal static class Gen6EncounterArchiveOrder
{
    private static int[] Indices(byte[] source)
    {
        var raw=new GARC.MemGARC(source);var decoded=new GARC.LazyGARC(source);
        return Enumerable.Range(0,raw.FileCount).OrderBy(i=>
        {
            byte[] bytes=raw.GetFile(i);
            return bytes.Length>0&&bytes[0]==0x11&&!bytes.AsSpan().SequenceEqual(decoded[i])?1:0;
        }).ThenBy(i=>i).ToArray();
    }
    internal static byte[][] EditorOrder(byte[] source,byte[][] physical)
    {
        int[] indices=Indices(source);
        if(indices.Length!=physical.Length)throw new InvalidDataException("Cantidad de miembros Gen6 incompatible.");
        return indices.Select(i=>physical[i]).ToArray();
    }
    internal static byte[][] PhysicalOrder(byte[] source,byte[][] editor)
    {
        int[] indices=Indices(source);
        if(indices.Length!=editor.Length)throw new InvalidDataException("Cantidad de miembros Gen6 incompatible.");
        var answer=new byte[indices.Length][];
        for(int i=0;i<indices.Length;i++)answer[indices[i]]=editor[i];
        return answer;
    }
}
