using System.IO;

namespace ThreeDS.Progressive.Services;

/// <summary>Signed big-endian 7-bit instructions used by Gen6 scripts; frozen envelope preserved.</summary>
internal static class Gen6ScriptCodec
{
    internal sealed record Decoded(int Start,int End,byte[] Instructions,(int Offset,int Length)[] Words);
    internal static Decoded Decode(byte[] raw)
    {
        if(raw.Length<28)throw new InvalidDataException("Cabecera de script Gen6 truncada.");
        int length=BitConverter.ToInt32(raw,0),start=BitConverter.ToInt32(raw,12),movement=BitConverter.ToInt32(raw,16),final=BitConverter.ToInt32(raw,20);
        uint magic=BitConverter.ToUInt32(raw,4);
        if(magic is not (0x0A0AF1E0 or 0x0A0AF1EF)||length<28||length>raw.Length||start<28||start>length||start%4!=0||
            movement<start||movement>final||(movement-start)%4!=0||final<start||(final-start)%4!=0)
            throw new InvalidDataException("Límites o firma del script Gen6 incompatibles.");
        int count=(final-start)/4;
        if(count>length-start)throw new InvalidDataException("Cantidad de instrucciones incompatible con el flujo.");
        byte[] output=new byte[count*4];int cursor=start;var words=new (int Offset,int Length)[count];
        for(int i=0;i<count;i++)
        {
            uint value=0;int groups=0;byte current;int wordStart=cursor;
            do
            {
                if(cursor>=length||groups==5)throw new InvalidDataException("Instrucción Gen6 truncada o demasiado larga.");
                current=raw[cursor++];int chunk=current&127;
                value=groups++==0?unchecked((uint)((chunk&64)!=0?chunk-128:chunk)):(value<<7)|(byte)chunk;
            }while((current&128)!=0);
            BitConverter.GetBytes(value).CopyTo(output,i*4);words[i]=(wordStart,cursor-wordStart);
        }
        return new(start,cursor,output,words);
    }
    internal static byte[] Encode(byte[] instructions)
    {
        if(instructions.Length%4!=0)throw new InvalidDataException("Instrucciones Gen6 no alineadas.");
        using var output=new MemoryStream();
        for(int at=0;at<instructions.Length;at+=4)
        {
            long value=BitConverter.ToInt32(instructions,at);int groups=5;
            for(int n=1;n<=5;n++)if(value>=-(1L<<(7*n-1))&&value<1L<<(7*n-1)){groups=n;break;}
            long encoded=value&((1L<<(groups*7))-1);
            for(int i=groups-1;i>=0;i--)output.WriteByte((byte)(((encoded>>(7*i))&127)|(i>0?128L:0L)));
        }
        return output.ToArray();
    }
    internal static byte[] Rebuild(byte[] original,byte[] instructions)
    {
        var old=Decode(original);
        if(instructions.Length!=old.Instructions.Length)throw new InvalidDataException("El editor no puede cambiar la cantidad de instrucciones.");
        if(instructions.AsSpan().SequenceEqual(old.Instructions))return (byte[])original.Clone();
        using var stream=new MemoryStream();
        for(int i=0;i<old.Words.Length;i++)
        {
            if(instructions.AsSpan(i*4,4).SequenceEqual(old.Instructions.AsSpan(i*4,4)))
                stream.Write(original,old.Words[i].Offset,old.Words[i].Length);
            else stream.Write(Encode(instructions.AsSpan(i*4,4).ToArray()));
        }
        byte[] compressed=stream.ToArray();
        int delta=compressed.Length-(old.End-old.Start);
        byte[] result=new byte[checked(original.Length+delta)];
        original.AsSpan(0,old.Start).CopyTo(result);
        compressed.CopyTo(result,old.Start);
        original.AsSpan(old.End).CopyTo(result.AsSpan(old.Start+compressed.Length));
        BitConverter.GetBytes(checked(BitConverter.ToInt32(original,0)+delta)).CopyTo(result,0);
        if(!Decode(result).Instructions.AsSpan().SequenceEqual(instructions))throw new InvalidDataException("La relectura del script Gen6 no coincide.");
        return result;
    }
}
