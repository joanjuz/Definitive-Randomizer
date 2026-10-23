using System;
using System.IO;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Text;
namespace pk3DS.Core.NX;
// SWSH message format: kwsch/pkNX TextFile (GPL-3.0). Read only; no text reserialization.
public static class SwordShieldText
{
    public static IReadOnlyList<string> Read(byte[] data)
    {
        if(data.Length<16||BinaryPrimitives.ReadUInt16LittleEndian(data)!=1||BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8))!=0)throw new InvalidDataException("Banco de nombres no compatible.");
        int count=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)),size=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4))),section=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)));
        if(section<16||size<4||section>data.Length-size||section+size!=data.Length||BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(section))!=size||count>(size-4)/8)throw new InvalidDataException("Banco de nombres truncado.");
        var names=new List<string>();
        for(int line=0;line<count;line++)
        {
            int d=section+4+line*8,offset=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(d)),words=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(d+4));
            if(offset<4+count*8||offset>size-words*2)throw new InvalidDataException("Nombre fuera del banco.");
            ushort key=unchecked((ushort)(0x7C89+line*0x2983));var text=new StringBuilder();
            for(int w=0;w<words;w++){ushort c=(ushort)(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(section+offset+w*2))^key);key=(ushort)((key<<3)|(key>>13));if(c==0)break;if(c==0x10||c>=0xBDFF&&c<=0xBE02){text.Clear();break;}text.Append((char)c);}names.Add(text.ToString());
        }
        return names.AsReadOnly();
    }
}
