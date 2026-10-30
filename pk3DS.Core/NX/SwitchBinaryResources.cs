using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Text;

namespace pk3DS.Core.NX;

// Binary layout references: kwsch/pkNX (GPL-3.0), GFPack and SWSH FlatBuffers schemas.
// Only known scalar offsets are patched. No unknown tables or archive members are serialized.
public sealed class SwitchFlatBuffer
{
    public byte[] Data { get; }
    public SwitchFlatBuffer(byte[] data) { Data = (byte[])data.Clone(); _ = Root; }
    public void Bounds(int at, int length) { if(at<0 || length<0 || at>Data.Length-length) throw new InvalidDataException("FlatBuffer truncado o referencia fuera del recurso."); }
    public int I32(int at) { Bounds(at,4); return BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(at)); }
    public int U16(int at) { Bounds(at,2); return BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(at)); }
    public ulong U64(int at) { Bounds(at,8); return BinaryPrimitives.ReadUInt64LittleEndian(Data.AsSpan(at)); }
    public int Reference(int at) { int delta=I32(at); if(delta<=0)throw new InvalidDataException("Referencia FlatBuffer inválida."); if(delta>Data.Length-at-4)throw new InvalidDataException("Referencia FlatBuffer fuera del recurso."); int target=at+delta; Bounds(target,4); return target; }
    public int Root => Reference(0);
    public int Field(int table, int field, int width=4)
    {
        Bounds(table,4); long v=(long)table-I32(table); if(v<0||v>Data.Length-4)throw new InvalidDataException("Vtable fuera del recurso.");int vt=(int)v; Bounds(vt,4); int size=U16(vt), objectSize=U16(vt+2);
        if(size<4 || (size&1)!=0 || objectSize<4)throw new InvalidDataException("Vtable inválida.");
        Bounds(vt,size); Bounds(table,objectSize);
        if(4+field*2>=size)return -1;
        int offset=U16(vt+4+field*2); if(offset==0)return -1;
        if(offset<4 || offset>objectSize-width)throw new InvalidDataException("Campo fuera de su tabla.");
        Bounds(table+offset,width); return table+offset;
    }
    public int Table(int table,int field) { int at=Field(table,field); return at<0 ? -1 : Reference(at); }
    public int[] Vector(int table,int field,int stride=4,bool required=false)
    {
        int at=Field(table,field); if(at<0){if(required)throw new InvalidDataException("Vector obligatorio ausente.");return Array.Empty<int>();}
        int start=Reference(at), count=I32(start); if(count<0 || count>1000000)throw new InvalidDataException("Vector demasiado grande.");
        Bounds(start+4,checked(count*stride)); return Enumerable.Range(0,count).Select(i=>start+4+i*stride).ToArray();
    }
    public int[] Tables(int table,int field,bool required=false) => Vector(table,field,4,required).Select(Reference).ToArray();
    public string String(int table,int field)
    {
        int at=Table(table,field);if(at<0)return "";int length=I32(at);Bounds(at+4,checked(length+1));
        if(Data[at+4+length]!=0)throw new InvalidDataException("Cadena sin terminador."); return new UTF8Encoding(false,true).GetString(Data,at+4,length);
    }
}

public sealed class SwitchGfPack
{
    private readonly byte[] _source;
    private readonly Dictionary<ulong,int> _names=new();
    private readonly int _fileTable;
    private readonly (ulong Hash,int Offset,int Packed,int Size,byte Codec)[] _files;
    public int Count => _files.Length;
    private int I32(int at) { Bound(at,4); return BinaryPrimitives.ReadInt32LittleEndian(_source.AsSpan(at)); }
    private long I64(int at) { Bound(at,8); return BinaryPrimitives.ReadInt64LittleEndian(_source.AsSpan(at)); }
    private void Bound(int at,int size) { if(at<0||size<0||at>_source.Length-size)throw new InvalidDataException("GFPack truncado."); }
    public SwitchGfPack(byte[] data)
    {
        _source=(byte[])data.Clone(); Bound(0,0x28);
        if(!_source.AsSpan(0,8).SequenceEqual("GFLXPACK"u8)||I32(8)!=0x1000||I32(12)!=0)throw new InvalidDataException("Perfil GFPack no compatible.");
        int count=I32(16), folders=I32(20);if(count<0||count>100000||folders<0||folders>100000)throw new InvalidDataException("Directorio GFPack inválido.");
        Bound(24,checked(16+folders*8)); _fileTable=checked((int)I64(24)); int hashes=checked((int)I64(32));Bound(_fileTable,checked(count*24));Bound(hashes,checked(count*8));
        for(int folder=0;folder<folders;folder++)
        {
            int at=checked((int)I64(40+folder*8));Bound(at,16);int entries=I32(at+8);if(entries<0||entries>count)throw new InvalidDataException("Índice de carpeta inválido.");Bound(at+16,checked(entries*16));
            for(int j=0;j<entries;j++){int p=at+16+j*16;ulong hash=unchecked((ulong)I64(p));int index=I32(p+8);if(index<0||index>=count)throw new InvalidDataException("Índice de miembro inválido.");if(_names.TryGetValue(hash,out int old)&&old!=index)_names[hash]=-2;else _names[hash]=index;}
        }
        _files=new (ulong,int,int,int,byte)[count]; var unique=new HashSet<ulong>();
        for(int i=0;i<count;i++)
        {
            int p=_fileTable+24*i;ulong hash=unchecked((ulong)I64(hashes+8*i));if(!unique.Add(hash))throw new InvalidDataException("Hash de miembro ambiguo.");
            int size=I32(p+4),packed=I32(p+8),offset=I32(p+16); if(size<0||size>128*1024*1024)throw new InvalidDataException("Miembro demasiado grande.");Bound(offset,packed);
            _files[i]=(hash,offset,packed,size,_source[p+2]);
        }
    }
    public static ulong NameHash(string name) { ulong hash=0xCBF29CE484222645UL;foreach(byte b in Encoding.UTF8.GetBytes(name))hash=unchecked((hash^b)*1099511628211);return hash; }
    public int Find(string name) { ulong h=NameHash(name);if(_names.TryGetValue(h,out int index)){if(index<0)throw new InvalidDataException("Nombre de miembro ambiguo: "+name);return index;}for(int i=0;i<Count;i++)if(_files[i].Hash==h)return i;return -1; }
    public byte[] Read(int index)
    {
        var f=_files[index]; var packed=_source.AsSpan(f.Offset,f.Packed);
        if(f.Codec==0){if(f.Size!=f.Packed)throw new InvalidDataException("Tamaño GFPack inválido.");return packed.ToArray();}
        if(f.Codec!=2)throw new InvalidDataException("Compresión GFPack no admitida: "+f.Codec);
        return DecodeLz4(packed,f.Size);
    }
    public byte[] Rewrite(IReadOnlyDictionary<int,byte[]> changes)
    {
        if(changes.Count==0)return (byte[])_source.Clone();
        using var stream=new MemoryStream();stream.Write(_source);
        foreach(var pair in changes.OrderBy(p=>p.Key))
        {
            var f=_files[pair.Key];if(Read(pair.Key).AsSpan().SequenceEqual(pair.Value))continue;
            byte[] packed=f.Codec switch {0=>pair.Value,2=>EncodeLz4(pair.Value),_=>throw new InvalidDataException("Compresión no admitida.")};
            while((stream.Position&15)!=0)stream.WriteByte(0);int offset=checked((int)stream.Position);stream.Write(packed);
            long end=stream.Position;stream.Position=_fileTable+24*pair.Key+4;
            using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){writer.Write(pair.Value.Length);writer.Write(packed.Length);stream.Position+=4;writer.Write(offset);}
            stream.Position=end;
        }
        byte[] result=stream.ToArray();var reopened=new SwitchGfPack(result);
        foreach(var pair in changes)if(!reopened.Read(pair.Key).AsSpan().SequenceEqual(pair.Value))throw new InvalidDataException("Falló la reapertura GFPack.");
        return result;
    }
    public static byte[] DecodeLz4(ReadOnlySpan<byte> input,int size)
    {
        if(size<0||size>128*1024*1024)throw new InvalidDataException("Tamaño LZ4 inválido.");
        var output=new byte[size];int p=0,o=0;
        int Length(int value,ref int position,ReadOnlySpan<byte> bytes){if(value!=15)return value;int extra;do{if(position>=bytes.Length)throw new InvalidDataException("LZ4 truncado.");extra=bytes[position++];value=checked(value+extra);}while(extra==255);return value;}
        while(p<input.Length)
        {
            int token=input[p++],literal=Length(token>>4,ref p,input);
            if(literal>input.Length-p||literal>size-o)throw new InvalidDataException("Literal LZ4 fuera de límites.");
            input.Slice(p,literal).CopyTo(output.AsSpan(o));p+=literal;o+=literal;if(p==input.Length)break;
            if(p>input.Length-2)throw new InvalidDataException("Offset LZ4 truncado.");int distance=input[p]|input[p+1]<<8;p+=2;
            int match=checked(Length(token&15,ref p,input)+4);if(distance==0||distance>o||match>size-o)throw new InvalidDataException("Referencia LZ4 inválida.");
            for(int j=0;j<match;j++){output[o]=output[o-distance];o++;}
        }
        if(o!=size)throw new InvalidDataException("Longitud LZ4 incorrecta.");return output;
    }
    // Valid LZ4 block with a final literal sequence. Keeps codec 2; compressed siblings remain byte-identical.
    public static byte[] EncodeLz4(byte[] data)
    {
        using var stream=new MemoryStream();stream.WriteByte((byte)(Math.Min(15,data.Length)<<4));
        if(data.Length>=15){int remaining=data.Length-15;while(remaining>=255){stream.WriteByte(255);remaining-=255;}stream.WriteByte((byte)remaining);}
        stream.Write(data);return stream.ToArray();
    }
    public static IReadOnlyList<string> AreaNames(byte[] data)
    {
        if(data.Length<8||!data.AsSpan(0,4).SequenceEqual("AHTB"u8))throw new InvalidDataException("AHTB inválido.");
        int count=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)),p=8;if(count<0||count>10000)throw new InvalidDataException("Número de áreas inválido.");
        var names=new List<string>();for(int i=0;i<count;i++){if(p>data.Length-10)throw new InvalidDataException("AHTB truncado.");int n=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p+8));if(n<1||n>data.Length-p-10||data[p+10+n-1]!=0)throw new InvalidDataException("Nombre AHTB inválido.");names.Add(new UTF8Encoding(false,true).GetString(data,p+10,n-1));p+=10+n;}return names.AsReadOnly();
    }
}
