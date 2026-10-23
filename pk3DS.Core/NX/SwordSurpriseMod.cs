using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Security.Cryptography;
namespace pk3DS.Core.NX;
public sealed record SwordSurprisePatch(int Entry,int Stub,int PointerSlot,uint Original,uint Branch);
public sealed class SwordSurpriseMod
{
    public const string Algorithm="sword-surprise-basic-spawn-v1";
    public const string OriginalTextHash="82A40B8D3C334DBA1B5D9D1F2F99D5BA6A04D0B1953A069C39C3E9097264A37E";
    public const string RuntimeHash="2C368A9B977C0C69AE09367CE5EF2185FF6E231D4D2A802537D21928F8322B24";
    public const int StubStart=0x1900F30, PointerStart=0x2985000;
    private readonly byte[] _main,_module;
    public byte[] Main()=> (byte[])_main.Clone();
    public byte[] Module()=> (byte[])_module.Clone();
    public uint Seed {get;}
    public IReadOnlyList<int> Pool {get;}
    public IReadOnlyList<SwordSurprisePatch> Patches {get;}
    public string SourceMainSha256 {get;}
    public string PatchedTextSha256 {get;}
    private SwordSurpriseMod(byte[] main,byte[] module,uint seed,int[] pool,List<SwordSurprisePatch> patches,string sourceHash,string textHash)
    {_main=main;_module=module;Seed=seed;Pool=Array.AsReadOnly(pool);Patches=patches.AsReadOnly();SourceMainSha256=sourceHash;PatchedTextSha256=textHash;}
    public static SwordSurpriseMod Build(byte[] main,byte[] runtime,byte[] personal,uint seed)
    {
        ArgumentNullException.ThrowIfNull(main);ArgumentNullException.ThrowIfNull(runtime);
        var audit=SwordSurprisePreflight.Audit(main);
        if(!audit.SignaturesMatch||audit.TextSha256!=OriginalTextHash)throw new InvalidDataException("El código de Espada difiere del perfil original; no se combina este prototipo con otros parches ExeFS.");
        if(Convert.ToHexString(SHA256.HashData(runtime))!=RuntimeHash)throw new InvalidDataException("El módulo propio de combate sorpresa no coincide con la versión compilada.");
        int[] pool=new SwordShieldPersonalTable(personal).Entries().Where(e=>e.Species>0&&e.Form==0).Select(e=>e.Species).Distinct().Order().ToArray();
        if(pool.Length<2)throw new InvalidDataException("Se necesitan dos especies base presentes como mínimo.");
        byte[] text=ReadSegment(main,0);
        if(text.Length!=StubStart||U32(main,0x24)!=0x1901000||U32(main,0x34)!=0x24DB000||U32(main,0x38)!=0x15AF38||U32(main,0x3C)!=0x34F0C8)throw new InvalidDataException("Distribución NSO incompatible con el perfil de Espada.");
        var expanded=new byte[text.Length+96];text.CopyTo(expanded,0);var patches=new List<SwordSurprisePatch>();
        int[] entries={0xD329D0,0xD30D30,0xD60920,0xD311C0};uint[] expected={0xA9BC5FF8,0xA9BD57F6,0xD10183FF,0xD10603FF};
        for(int i=0;i<4;i++)
        {
            int entry=entries[i],stub=StubStart+i*24,slot=PointerStart+i*8;uint old=U32(text,entry);
            if(old!=expected[i])throw new InvalidDataException("La entrada nativa no coincide con el perfil: "+entry.ToString("X"));
            W32(expanded,entry,Branch(entry,stub));
            long pages=((long)slot>>12)-((long)stub>>12);uint imm=unchecked((uint)pages)&0x1FFFFF;
            W32(expanded,stub,0x90000010u|((imm&3)<<29)|((imm>>2)<<5));
            W32(expanded,stub+4,0xF9400210u|(((uint)slot&0xFFF)/8<<10));
            W32(expanded,stub+8,0xB4000050u);W32(expanded,stub+12,0xD61F0200u);
            W32(expanded,stub+16,old);W32(expanded,stub+20,Branch(stub+20,entry+4));
            patches.Add(new(entry,stub,slot,old,Branch(entry,stub)));
        }
        if(expanded.Length>U32(main,0x24))throw new InvalidDataException("No hay espacio entre .text y .rodata.");
        int appended=checked((main.Length+15)&~15);var result=new byte[checked(appended+expanded.Length)];main.CopyTo(result,0);expanded.CopyTo(result,appended);
        W32(result,0x10,(uint)appended);W32(result,0x18,(uint)expanded.Length);W32(result,0x60,(uint)expanded.Length);W32(result,0x3C,U32(main,0x3C)+32);
        // Ryujinx/LibHac rejects compressedSize > segmentSize. Literal-only LZ4 grows the data.
        // Store this changed segment uncompressed and preserve every other segment/flag.
        W32(result,0xC,(U32(main,0xC)&~1u)|8);SHA256.HashData(expanded).CopyTo(result,0xA0);
        byte[] module=Configure(runtime,pool,seed);
        if(!ReadSegment(result,0).AsSpan().SequenceEqual(expanded)||!ReadSegment(main,1).AsSpan().SequenceEqual(ReadSegment(result,1))||!ReadSegment(main,2).AsSpan().SequenceEqual(ReadSegment(result,2)))throw new InvalidDataException("Falló la reapertura de los segmentos NSO.");
        return new(result,module,seed,pool,patches,Convert.ToHexString(SHA256.HashData(main)),Convert.ToHexString(SHA256.HashData(expanded)));
    }
    private static byte[] Configure(byte[] original,int[] pool,uint seed)
    {
        var result=(byte[])original.Clone();ReadOnlySpan<byte> marker="DRSURPRISECFG1!\0"u8;int offset=result.AsSpan().IndexOf(marker);
        if(offset<0||result.AsSpan(offset+marker.Length).IndexOf(marker)>=0||offset>result.Length-(32+898*2))throw new InvalidDataException("Configuración del módulo inválida.");
        W32(result,offset+16,1);W32(result,offset+20,seed);W32(result,offset+24,(uint)pool.Length);
        result.AsSpan(offset+32,898*2).Clear();for(int i=0;i<pool.Length;i++)BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset+32+i*2),(ushort)pool[i]);
        int dataAt=checked((int)U32(result,0x30)),dataSize=checked((int)U32(result,0x38));
        if(offset<dataAt||offset+32+898*2>dataAt+dataSize)throw new InvalidDataException("La configuración no está en el segmento de datos.");
        SHA256.HashData(result.AsSpan(dataAt,dataSize)).CopyTo(result,0xE0);
        result.AsSpan(0x40,32).Clear();SHA256.HashData(result).CopyTo(result,0x40);
        for(int i=0;i<3;i++)_ = ReadSegment(result,i);return result;
    }
    public static byte[] ReadSegment(ReadOnlySpan<byte> nso,int index)
    {
        if(nso.Length<256||!nso[..4].SequenceEqual("NSO0"u8)||index<0||index>2)throw new InvalidDataException("NSO o índice de segmento inválido.");
        uint flags=U32(nso,12),offset=U32(nso,16+index*16),size=U32(nso,24+index*16),packed=(flags&(1u<<index))!=0?U32(nso,96+index*4):size;
        if(((flags&(1u<<index))!=0&&packed>size)||size>128*1024*1024||offset<256||offset>nso.Length||packed>nso.Length-offset)throw new InvalidDataException("Segmento NSO fuera de límites.");
        var data=(flags&(1u<<index))!=0?SwitchGfPack.DecodeLz4(nso.Slice((int)offset,(int)packed),(int)size):nso.Slice((int)offset,(int)size).ToArray();
        if((flags&(8u<<index))!=0&&!SHA256.HashData(data).AsSpan().SequenceEqual(nso.Slice(160+index*32,32)))throw new InvalidDataException("SHA-256 del segmento NSO incorrecto.");return data;
    }
    public static uint Branch(int from,int to)
    {long delta=(long)to-from;if((delta&3)!=0||delta<-(1L<<27)||delta>=(1L<<27))throw new ArgumentOutOfRangeException(nameof(to));return 0x14000000u|(unchecked((uint)(delta>>2))&0x3FFFFFF);}
    private static uint U32(ReadOnlySpan<byte> data,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
    private static void W32(byte[] data,int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at),value);
}
