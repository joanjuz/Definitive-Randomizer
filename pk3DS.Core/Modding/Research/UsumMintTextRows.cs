using System;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

/// <summary>Edit mint rows while preserving original encrypted bytes and opaque row metadata elsewhere.</summary>
public static class UsumMintTextRows
{
    public static byte[] Patch(GameConfig config,byte[] source,string[] lines)
    {
        var original=new TextFile(config,source,config.RemapCharacters);
        string[] previous=original.Lines;
        if(lines.Length<previous.Length||lines.Length>ushort.MaxValue||BitConverter.ToInt32(source,12)!=16)
            throw new InvalidDataException("La edición de mentas no puede quitar filas ni cambiar la sección de texto.");
        // Encode only edited rows. Existing commands/whitespace/opaque padding are never reserialized.
        string[] edits=lines.Select((line,i)=>i<previous.Length&&line==previous[i]?"":line).ToArray();
        var encoder=new TextFile(config,remapChars:config.RemapCharacters){PreserveWhitespace=true,Lines=edits};
        var rows=new byte[lines.Length][];var metadata=new byte[lines.Length][];
        for(int i=0;i<lines.Length;i++){
            byte[] input=i<previous.Length&&lines[i]==previous[i]?source:encoder.Data;
            int table=checked(20+i*8),relative=BitConverter.ToInt32(input,table),length=BitConverter.ToUInt16(input,table+4);
            int offset=checked(16+relative),bytes=checked(length*2);
            if(length>short.MaxValue||offset<20||offset+bytes>input.Length)throw new InvalidDataException("Fila de texto fuera de límites.");
            rows[i]=input.AsSpan(offset,bytes).ToArray();
            // Preserve the extra two row bytes on an existing edited row as well.
            byte[] tableSource=i<previous.Length?source:encoder.Data;
            metadata[i]=tableSource.AsSpan(table,8).ToArray();
        }
        int sectionSize=checked(4+lines.Length*8+rows.Sum(r=>r.Length));
        byte[] output=new byte[checked(16+sectionSize)];source.AsSpan(0,16).CopyTo(output);
        BitConverter.GetBytes((ushort)lines.Length).CopyTo(output,2);
        BitConverter.GetBytes(sectionSize).CopyTo(output,4);BitConverter.GetBytes(sectionSize).CopyTo(output,16);
        int cursor=20+lines.Length*8;
        for(int i=0;i<lines.Length;i++){
            int at=20+i*8;metadata[i].CopyTo(output,at);
            BitConverter.GetBytes(cursor-16).CopyTo(output,at);BitConverter.GetBytes((ushort)(rows[i].Length/2)).CopyTo(output,at+4);
            rows[i].CopyTo(output,cursor);cursor+=rows[i].Length;
        }
        if(!new TextFile(config,output,config.RemapCharacters).Lines.SequenceEqual(lines))throw new InvalidDataException("Relectura de textos de mentas no reversible.");
        return output;
    }
}
