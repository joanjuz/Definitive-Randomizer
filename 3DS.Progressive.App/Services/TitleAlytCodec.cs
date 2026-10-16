using System.IO;
using System.Text;
namespace ThreeDS.Progressive.Services;

/// <summary>Validated ALYT/SARC title resource; rebuilds offsets while retaining every other payload.</summary>
internal sealed class TitleAlytCodec
{
    private readonly byte[] _source;
    private readonly int _sarc,_size,_data,_nodes;
    internal IReadOnlyList<TitleDarcEntry> Entries {get;}
    private static int I(byte[] b,int p)=>checked((int)BitConverter.ToUInt32(b,p));
    private static int Align(int n)=>checked((n+127)&~127);
    internal TitleAlytCodec(byte[] source)
    {
        if(source.Length<128||source.Length>128*1024*1024||!source.AsSpan(0,4).SequenceEqual("ALYT"u8))throw new InvalidDataException("Recurso ALYT inválido.");
        _source=(byte[])source.Clone();int offset=I(source,32),length=I(source,36);
        if(offset<40||length<24||(long)offset+length>source.Length)throw new InvalidDataException("Contenido ALYT fuera de rango.");
        int cursor=checked(offset+4+I(source,offset)*64);
        if(cursor>offset+length-4)throw new InvalidDataException("Etiquetas ALYT truncadas.");
        cursor=checked(cursor+4+I(source,cursor)*32);
        if(cursor>offset+length-20)throw new InvalidDataException("Símbolos ALYT truncados.");
        while(cursor<offset+length-20&&source[cursor]==0)cursor++;
        _sarc=cursor;
        if(!source.AsSpan(cursor,4).SequenceEqual("SARC"u8)||BitConverter.ToUInt16(source,cursor+4)!=20||BitConverter.ToUInt16(source,cursor+6)!=0xFEFF)throw new InvalidDataException("SARC de título incompatible.");
        _size=I(source,cursor+8);_data=I(source,cursor+12);
        if(_size<32||_data<32||_data>_size||(long)cursor+_size>offset+length||!source.AsSpan(cursor+20,4).SequenceEqual("SFAT"u8)||BitConverter.ToUInt16(source,cursor+24)!=12)throw new InvalidDataException("Tabla SARC inválida.");
        int count=BitConverter.ToUInt16(source,cursor+26);_nodes=cursor+32;int names=checked(_nodes+count*16);
        if(count<1||names+8>cursor+_data||!source.AsSpan(names,4).SequenceEqual("SFNT"u8)||BitConverter.ToUInt16(source,names+4)!=8)throw new InvalidDataException("Nombres SARC inválidos.");
        names+=8;var entries=new List<TitleDarcEntry>();
        for(int i=0;i<count;i++)
        {
            int at=_nodes+i*16;uint name=BitConverter.ToUInt32(source,at+4);
            if(name>>24!=1)throw new InvalidDataException("Entrada SARC sin nombre.");
            int n=checked(names+(int)(name&0xFFFFFF)*4),end=n;
            if(n<names||n>=cursor+_data)throw new InvalidDataException("Nombre SARC fuera de rango.");
            while(end<cursor+_data&&source[end]!=0)end++;
            if(end==cursor+_data)throw new InvalidDataException("Nombre SARC sin terminador.");
            int start=I(source,at+8),finish=I(source,at+12);
            if(start<0||finish<start||(long)_data+finish>_size)throw new InvalidDataException("Imagen SARC fuera de rango.");
            entries.Add(new(i,Encoding.UTF8.GetString(source,n,end-n),cursor+_data+start,finish-start,false));
        }
        Entries=entries;
    }
    internal byte[] Read(int id)
    {var e=Entries.SingleOrDefault(e=>e.Id==id)??throw new ArgumentException("Entrada inexistente.");return _source.AsSpan(e.Offset,e.Length).ToArray();}
    internal byte[] Replace(int id,byte[] bytes)
    {
        if(!Entries.Any(e=>e.Id==id)||bytes.Length==0||bytes.Length>64*1024*1024)throw new ArgumentException("Reemplazo SARC inválido.");
        if(Read(id).SequenceEqual(bytes))return (byte[])_source.Clone();
        var selected=Entries.Single(e=>e.Id==id);
        if(bytes.Length==selected.Length)
        {
            // Fixed-size texture edits must not rewrite SARC layout/alignment.
            byte[] fixedSize=(byte[])_source.Clone();bytes.CopyTo(fixedSize,selected.Offset);
            return fixedSize;
        }
        using var buffer=new MemoryStream();buffer.Write(_source.AsSpan(_sarc,_data));
        var positions=new List<(int Start,int End)>();
        foreach(var e in Entries)
        {
            int at=Align(checked((int)buffer.Length));buffer.SetLength(at);buffer.Position=at;byte[] raw=e.Id==id?bytes:Read(e.Id);buffer.Write(raw);
            positions.Add((at-_data,checked((int)buffer.Length)-_data));
        }
        buffer.SetLength(Align(checked((int)buffer.Length)));byte[] sarc=buffer.ToArray();BitConverter.GetBytes(sarc.Length).CopyTo(sarc,8);
        for(int i=0;i<positions.Count;i++){int at=_nodes-_sarc+i*16;BitConverter.GetBytes(positions[i].Start).CopyTo(sarc,at+8);BitConverter.GetBytes(positions[i].End).CopyTo(sarc,at+12);}
        int delta=sarc.Length-_size;byte[] result=new byte[Align(checked(_source.Length+delta))];
        _source.AsSpan(0,_sarc).CopyTo(result);sarc.CopyTo(result,_sarc);_source.AsSpan(_sarc+_size).CopyTo(result.AsSpan(_sarc+sarc.Length));
        BitConverter.GetBytes(checked(I(_source,36)+delta)).CopyTo(result,36);
        var check=new TitleAlytCodec(result);
        if(check.Entries.Count!=Entries.Count||Entries.Any(e=>check.Entries[e.Id].Name!=e.Name||!check.Read(e.Id).SequenceEqual(e.Id==id?bytes:Read(e.Id))))throw new InvalidDataException("El recurso de título no superó la relectura.");
        return result;
    }
}
