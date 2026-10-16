using System.Buffers.Binary;
using System.Security.Cryptography;
using pk3DS.Core.CTR;

internal static class AesCtrChecks
{
    internal static void Run(Action<bool,string> check)
    {
        const int length=0x400020,inputOffset=16,outputOffset=32;
        ulong program=0x0004000000055D00,section=2UL<<56;
        byte[] input=Enumerable.Range(0,length+inputOffset).Select(i=>(byte)(i*17+3)).ToArray();
        byte[] counters=new byte[length];
        for(int i=0;i<length;i+=16)
        {BinaryPrimitives.WriteUInt64BigEndian(counters.AsSpan(i,8),program);BinaryPrimitives.WriteUInt64BigEndian(counters.AsSpan(i+8,8),section+(ulong)(i/16));}
        using var aes=Aes.Create();aes.Key=new byte[16];byte[] expected=aes.EncryptEcb(counters,PaddingMode.None);
        for(int i=0;i<length;i++)expected[i]^=input[inputOffset+i];
        byte[] output=Enumerable.Repeat((byte)0xAB,length+outputOffset+16).ToArray();
        new AesCtr(new byte[16],program,section).TransformBlock(input,inputOffset,length,output,outputOffset);
        check(output.AsSpan(outputOffset,length).SequenceEqual(expected),"AES CTR matches independent framework ECB counter reference across 4 MiB boundary and nonzero buffer offsets");
        check(output.AsSpan(0,outputOffset).ToArray().All(b=>b==0xAB)&&output.AsSpan(outputOffset+length).ToArray().All(b=>b==0xAB),"AES CTR preserves output prefix and suffix");
        var split=new AesCtr(new byte[16],program,section);byte[] chunks=new byte[length];
        split.TransformBlock(input,inputOffset,64,chunks,0);split.TransformBlock(input,inputOffset+64,length-64,chunks,64);
        check(chunks.SequenceEqual(expected),"AES CTR chunked calls preserve counter continuity");
        byte[] decoded=new byte[length];new AesCtr(new byte[16],program,section).TransformBlock(chunks,0,length,decoded,0);
        check(decoded.AsSpan().SequenceEqual(input.AsSpan(inputOffset,length)),"AES CTR decrypts back to original input");
    }
}
