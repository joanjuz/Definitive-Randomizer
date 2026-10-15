using System.IO;
using System.Security.Cryptography;
namespace ThreeDS.Progressive.Services;

/// <summary>Gen6 inventories relocated within verified RO-page padding; code size, segment mappings and BSS remain unchanged.</summary>
internal sealed class Gen6MartTable
{
    private readonly string _headerPath;
    private readonly byte[] _headerHash;
    private readonly int _cave,_end,_counts,_pointer,_stock,_family;
    private readonly uint _address;
    private readonly int _shops;
    internal int Capacity=>(_end-_cave-64)/2;
    private static int I(byte[] b,int p)=>checked((int)BitConverter.ToUInt32(b,p));
    private static int Aligned(int n)=>checked((n+4095)&~4095);
    internal Gen6MartTable(GameProjectSession project,byte[] source,int stock,int[] stockCounts)
    {
        _family=project.GameConfiguration.ORAS?2:1;_shops=stockCounts.Length;_stock=stock;
        _headerPath=new[]{"DecryptedExHeader.bin","ExHeader.bin","exheader.bin"}.Select(n=>Path.Combine(project.ProjectRoot,n)).FirstOrDefault(File.Exists)??throw new InvalidDataException("Falta ExHeader para verificar el padding de tiendas.");
        byte[] header=File.ReadAllBytes(_headerPath);if(header.Length<64)throw new InvalidDataException("ExHeader truncado.");_headerHash=SHA256.HashData(header);
        int text=I(header,24),ro=I(header,40),data=I(header,56),roFile=Aligned(text);
        if((long)roFile+Aligned(ro)+Aligned(data)!=source.Length||I(header,20)!=Aligned(text)/4096||I(header,36)!=Aligned(ro)/4096)throw new InvalidDataException("Layout ExHeader/code.bin incompatible.");
        _cave=checked((roFile+ro+15)&~15);_end=roFile+Aligned(ro);
        if(Capacity<stockCounts.Sum())throw new InvalidDataException("Padding RO insuficiente para ampliar tiendas.");
        _address=checked(BitConverter.ToUInt32(header,32)+(uint)(_cave+64-roFile));
        uint stockAddress=checked(BitConverter.ToUInt32(header,32)+(uint)(stock-roFile));
        _counts=stock-(_family==2?0x236:0x1BA);
        if(_counts<roFile||_counts+_shops>roFile+ro)throw new InvalidDataException("Cantidades de tiendas fuera de RO.");
        if(source.AsSpan(_cave,4).SequenceEqual("DRM6"u8))
        {
            _pointer=I(source,_cave+24);
            if(I(source,_cave+4)!=1||I(source,_cave+8)!=_family||I(source,_cave+12)!=_end-_cave||I(source,_cave+16)!=stock||I(source,_cave+20)!=_counts||I(source,_cave+28)!=_cave+64||I(source,_cave+32)!=Capacity||BitConverter.ToUInt32(source,_cave+40)!=_address||_pointer<roFile+Aligned(ro)||_pointer>source.Length-4||_pointer%4!=0||BitConverter.ToUInt32(source,_pointer)!=_address)throw new InvalidDataException("Descriptor de tiendas Gen6 incompatible.");
            int total=source.AsSpan(_counts,_shops).ToArray().Sum(v=>(int)v);
            if(source.AsSpan(_counts,_shops).ToArray().Any(n=>n is <1 or >64)||total>Capacity||I(source,_cave+44)!=total||!Digest(source).AsSpan(0,16).SequenceEqual(source.AsSpan(_cave+48,16))||source.AsSpan(_cave+64+total*2,_end-(_cave+64+total*2)).ToArray().Any(v=>v!=0))throw new InvalidDataException("Integridad de tiendas Gen6 inválida.");
        }
        else
        {
            if(source.AsSpan(_cave,_end-_cave).ToArray().Any(v=>v!=0)||!source.AsSpan(_counts,_shops).SequenceEqual(stockCounts.Select(n=>(byte)n).ToArray()))throw new InvalidDataException("El padding o las cantidades Gen6 ya fueron modificados.");
            byte[] needle=BitConverter.GetBytes(stockAddress);var refs=new List<int>();
            for(int p=roFile+Aligned(ro);p<=source.Length-4;p+=4)if(source.AsSpan(p,4).SequenceEqual(needle))refs.Add(p);
            if(refs.Count!=1)throw new InvalidDataException("Puntero de inventarios Gen6 ausente o ambiguo.");_pointer=refs[0];
        }
    }
    internal void ValidateHeader(){if(!SHA256.HashData(File.ReadAllBytes(_headerPath)).SequenceEqual(_headerHash))throw new IOException("ExHeader cambió desde la lectura de tiendas.");}
    internal int[] Counts(byte[] code)=>code.AsSpan(_counts,_shops).ToArray().Select(n=>(int)n).ToArray();
    internal int TableOffset(byte[] code)=>code.AsSpan(_cave,4).SequenceEqual("DRM6"u8)?_cave+64:_stock;
    private byte[] Digest(byte[] code)
    {using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData(code.AsSpan(_cave,48));hash.AppendData(code.AsSpan(_counts,_shops));hash.AppendData(code.AsSpan(_cave+64,_end-_cave-64));return hash.GetHashAndReset();}
    internal byte[] Build(byte[] source,IReadOnlyList<int[]> rows)
    {
        ValidateHeader();if(rows.Count!=_shops||rows.Any(r=>r.Length is <1 or >64||r.Any(i=>i<0||i>ushort.MaxValue))||rows.Sum(r=>r.Length)>Capacity)throw new InvalidDataException($"Inventario fuera de capacidad: máximo 64 por tienda y {Capacity} en total.");
        int old=TableOffset(source);var oldCounts=Counts(source);bool changed=false;
        for(int i=0;i<_shops;i++){if(rows[i].Length!=oldCounts[i]||!rows[i].SequenceEqual(Enumerable.Range(0,oldCounts[i]).Select(n=>(int)BitConverter.ToUInt16(source,old+n*2))))changed=true;old+=oldCounts[i]*2;}
        if(!changed)return (byte[])source.Clone();
        if(TableOffset(source)==_stock&&rows.Select(r=>r.Length).SequenceEqual(oldCounts))
        {
            byte[] fixedSize=(byte[])source.Clone();int at=_stock;
            foreach(var row in rows)foreach(int item in row){BitConverter.GetBytes((ushort)item).CopyTo(fixedSize,at);at+=2;}
            return fixedSize;
        }
        byte[] next=(byte[])source.Clone();Array.Clear(next,_cave,_end-_cave);"DRM6"u8.CopyTo(next.AsSpan(_cave));
        int[] meta=[1,_family,_end-_cave,_stock,_counts,_pointer,_cave+64,Capacity,0,unchecked((int)_address),rows.Sum(r=>r.Length)];
        for(int i=0;i<meta.Length;i++)BitConverter.GetBytes(meta[i]).CopyTo(next,_cave+4+i*4);
        BitConverter.GetBytes(_address).CopyTo(next,_pointer);int cursor=_cave+64;
        for(int i=0;i<_shops;i++){next[_counts+i]=(byte)rows[i].Length;foreach(int item in rows[i]){BitConverter.GetBytes((ushort)item).CopyTo(next,cursor);cursor+=2;}}
        Digest(next).AsSpan(0,16).CopyTo(next.AsSpan(_cave+48));return next;
    }
}
