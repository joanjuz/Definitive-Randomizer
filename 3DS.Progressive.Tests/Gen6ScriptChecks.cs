using System.IO;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

internal static class Gen6ScriptChecks
{
    private static byte[] ScriptBytes(byte[] code,bool debug=false,byte[]? suffix=null)
    {
        byte[] encoded=Gen6ScriptCodec.Encode(code);suffix??=[];byte[] raw=Enumerable.Repeat((byte)0xA5,32+encoded.Length+suffix.Length).ToArray();
        BitConverter.GetBytes(raw.Length).CopyTo(raw,0);BitConverter.GetBytes(debug?0x0A0AF1EF:0x0A0AF1E0).CopyTo(raw,4);
        raw[8]=0xA4;
        BitConverter.GetBytes(32).CopyTo(raw,12);BitConverter.GetBytes(32).CopyTo(raw,16);BitConverter.GetBytes(32+code.Length).CopyTo(raw,20);
        encoded.CopyTo(raw,32);suffix.CopyTo(raw,32+encoded.Length);return raw;
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        int[] edges=[int.MinValue,int.MaxValue,-134217729,-134217728,134217727,134217728,-1048577,-1048576,1048575,1048576,-8193,-8192,8191,8192,-65,-64,-1,0,1,63,64];
        var random=new Random(4312);int[] values=edges.Concat(Enumerable.Range(0,1000).Select(_=>(int)random.NextInt64(int.MinValue,(long)int.MaxValue+1))).ToArray();
        byte[] code=values.SelectMany(BitConverter.GetBytes).ToArray();
        foreach(bool debug in new[]{false,true})
        {
            byte[] raw=ScriptBytes(code,debug);var decoded=Gen6ScriptCodec.Decode(raw);
            check(decoded.Instructions.SequenceEqual(code),"Gen6 script signed limits and random 32-bit words round trip debug "+debug);
            check(new Script(raw).DecompressedInstructions.SequenceEqual(values.Select(v=>unchecked((uint)v))),"Gen6 script encoding matches original Core decoder debug "+debug);
            check(Gen6ScriptCodec.Rebuild(raw,code).SequenceEqual(raw),"Gen6 script no-op preserves original compression debug "+debug);
            byte[] changed=(byte[])code.Clone();BitConverter.GetBytes(5).CopyTo(changed,0);byte[] result=Gen6ScriptCodec.Rebuild(raw,changed);
            check(new Script(result).DecompressedInstructions.SequenceEqual(changed.Chunk(4).Select(b=>BitConverter.ToUInt32(b))),"Gen6 script modified length decodes through Core debug "+debug);
            check(raw.AsSpan(4,28).SequenceEqual(result.AsSpan(4,28))&&BitConverter.ToInt32(result,0)==result.Length,"Gen6 script preserves all header fields except physical length debug "+debug);
        }
        byte[] tailScript=ScriptBytes(BitConverter.GetBytes(1),suffix:[0xAA,0xBB,0xCC]);byte[] rebuilt=Gen6ScriptCodec.Rebuild(tailScript,BitConverter.GetBytes(int.MinValue));
        check(rebuilt.AsSpan(^3).SequenceEqual(new byte[]{0xAA,0xBB,0xCC}),"Gen6 script preserves opaque trailing bytes");
        reject(()=>Gen6ScriptCodec.Decode([]),"Gen6 script short header rejected");
        byte[] bad=ScriptBytes(BitConverter.GetBytes(1));bad[4]=0;reject(()=>Gen6ScriptCodec.Decode(bad),"Gen6 script unknown magic rejected");
        bad=ScriptBytes(BitConverter.GetBytes(1));bad[^1]=128;reject(()=>Gen6ScriptCodec.Decode(bad),"Gen6 script unterminated word rejected");
        bad=ScriptBytes(BitConverter.GetBytes(int.MaxValue));bad[32]=0x90;
        check(new Script(bad).DecompressedInstructions.SequenceEqual(Gen6ScriptCodec.Decode(bad).Instructions.Chunk(4).Select(b=>BitConverter.ToUInt32(b))),"Gen6 script noncanonical unsigned word matches Core truncation");
        check(Gen6ScriptCodec.Rebuild(bad,Gen6ScriptCodec.Decode(bad).Instructions).SequenceEqual(bad),"Gen6 script noncanonical encoding preserved on no-op");
        byte[] mixed=ScriptBytes(BitConverter.GetBytes(int.MaxValue).Concat(BitConverter.GetBytes(1)).ToArray());mixed[32]=0x90;
        var mixedDecoded=Gen6ScriptCodec.Decode(mixed);byte[] edited=(byte[])mixedDecoded.Instructions.Clone();BitConverter.GetBytes(10000).CopyTo(edited,4);
        byte[] mixedResult=Gen6ScriptCodec.Rebuild(mixed,edited);
        check(mixed.AsSpan(32,mixedDecoded.Words[0].Length).SequenceEqual(mixedResult.AsSpan(32,mixedDecoded.Words[0].Length)),"Gen6 script untouched noncanonical word keeps exact compressed bytes during another edit");
        bad=ScriptBytes(BitConverter.GetBytes(1));BitConverter.GetBytes(10000).CopyTo(bad,20);reject(()=>Gen6ScriptCodec.Decode(bad),"Gen6 script oversized decompressed count rejected");
        reject(()=>Gen6ScriptCodec.Encode([1,2]),"Gen6 script unaligned input rejected");
        reject(()=>Gen6ScriptCodec.Rebuild(ScriptBytes(BitConverter.GetBytes(1)),[]),"Gen6 script changing instruction count rejected");
    }
}
