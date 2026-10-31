#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace pk3DS.Core.Modding.Research;

public readonly record struct SinglePurchaseOffer(ushort Shop,ushort Item);
public sealed record SinglePurchaseStockSnapshot(uint Location,int AllocationBytes,uint Identity,ushort MessageId,IReadOnlyList<SinglePurchaseOffer> Offers)
{
    /// <summary>Registered offers whose item is on the white list: their row keeps its save bit but the shop never finds it.</summary>
    public IReadOnlySet<SinglePurchaseOffer> Exempt{get;init;}=new HashSet<SinglePurchaseOffer>();
    public IReadOnlyList<ushort> ExemptItems=>Exempt.Select(o=>o.Item).Distinct().Order().ToArray();
}

/// <summary>USUM special money shops only. Detached CRO patch with a stable append-only shop/item registry.
/// EventWork serialized D00..DFF is the in-game validated candidate storage, never native story flags.
/// The eight-byte header protects foreign save state; incompatible identities fail closed in ARM.</summary>
public static partial class Gen7SinglePurchaseStock
{
    private static readonly SinglePurchaseOffer[] PrototypeOffers=[new(10,101),new(11,101),new(10,102)];
    public const int MaximumOffers=1984;
    // White list: the ARM lookup compares the row's shop with the current one (8-27), so a row with this bit never matches and the item has no limit.
    private const ushort ExemptFlag=0x8000;
    public const int AllocationBytes=0x900+4+MaximumOffers*8;
    private const int Manifest=0x800,Digest=0x850,Registry=0x900;
    private const string TextHash="0BCFC8D102C3B367A1CD7D7201800CD1D426F0FA6800B330BC6B4C1D84BDAF27";
    private static ReadOnlySpan<byte> Marker=>"DRS-USUM-STOCK1!"u8;
    private static readonly (int At,uint Word,int Entry)[] Hooks=[(0x276c,0xe92d41f0,0),(0x15e4,0xe5940020,0x100),(0x164c,0xe1d408f2,0x200),(0x3cac,0xe3500001,0x700)];
    private static uint ReadWord(byte[] data,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at,4));
    private static void Write(byte[] data,int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at,4),value);
    private static void Half(byte[] data,int at,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at,2),value);
    private static uint Branch(uint from,uint to,bool link=false)
    {
        long delta=(long)to-from-8;if((delta&3)!=0||delta< -0x2000000||delta>0x1fffffc)throw new InvalidDataException("El salto ARM sale del rango admitido.");
        return (link?0xeb000000u:0xea000000u)|((uint)(delta>>2)&0xffffff);
    }
    private static byte[] PayloadDigest(byte[] data,int at,int size)
    {
        var payload=data.AsSpan(at,size).ToArray();payload.AsSpan(Digest,32).Clear();return SHA256.HashData(payload);
    }
    // No map recursion: the allocator also uses this ownership check to avoid padding/literal slots.
    internal static (uint Start,int Length)? ReservedRange(byte[] data,uint start,uint end)
    {
        if(end>data.Length||start>end)return null;
        int relative=data.AsSpan((int)start,(int)(end-start)).IndexOf(Marker);
        if(relative<0)return null;
        int markerAt=(int)start+relative,at=markerAt-Manifest;
        if(at<(int)start||(long)at+AllocationBytes>end)throw new InvalidDataException("Reserva de compras únicas fuera del segmento ejecutable.");
        if(ReadWord(data,markerAt+0x10)!=1||ReadWord(data,markerAt+0x14)!=AllocationBytes||!data.AsSpan(at+Digest,32).SequenceEqual(PayloadDigest(data,at,AllocationBytes)))throw new InvalidDataException("El parche de compras únicas está alterado o incompleto.");
        if(data.AsSpan(markerAt+Marker.Length,(int)end-markerAt-Marker.Length).IndexOf(Marker)>=0)throw new InvalidDataException("Hay más de una reserva de compras únicas.");
        return ((uint)at,AllocationBytes);
    }
    public static SinglePurchaseStockSnapshot? Read(byte[] source)
    {
        if(!CroRelocationMap.TryCreate(source,out var map,out string error))throw new InvalidDataException(error);
        var range=ReservedRange(source,map.CodeStart,map.CodeEnd);if(range is null)return null;
        int at=(int)range.Value.Start,count=checked((int)ReadWord(source,at+Manifest+0x18));
        uint identity=ReadWord(source,at+Manifest+0x1c),message=ReadWord(source,at+Manifest+0x20);
        if(count<0||count>MaximumOffers||identity==0||message>ushort.MaxValue||ReadWord(source,at+Registry)!=count)throw new InvalidDataException("Registro de compras únicas inválido.");
        var offers=new List<SinglePurchaseOffer>(count);var distinct=new HashSet<SinglePurchaseOffer>();var exempt=new HashSet<SinglePurchaseOffer>();
        for(int i=0;i<count;i++)
        {
            int row=at+Registry+4+i*8;ushort shop=BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(row));
            var offer=new SinglePurchaseOffer((ushort)(shop&~ExemptFlag),BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(row+2)));
            if(offer.Shop is <8 or >=28||offer.Item==0||!distinct.Add(offer)||BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(row+4))!=i+1)throw new InvalidDataException("Identidad de oferta duplicada o inválida.");offers.Add(offer);
            if((shop&ExemptFlag)!=0)exempt.Add(offer);
        }
        foreach(var hook in Hooks)if(ReadWord(source,hook.At)!=Branch((uint)hook.At,(uint)at+(uint)hook.Entry))throw new InvalidDataException("El hook de compras únicas no coincide con su reserva.");
        return new((uint)at,AllocationBytes,identity,(ushort)message,offers){Exempt=exempt};
    }
    public static IReadOnlyList<SinglePurchaseOffer> CurrentOffers(byte[] source)
    {
        if(!Gen7ExpandedMartTable.TryRead(source,out var layout,out string error))throw new InvalidDataException(error);
        return layout.Inventories.SelectMany((items,shop)=>shop<8?Array.Empty<SinglePurchaseOffer>():items.Where(item=>item!=0).Select(item=>new SinglePurchaseOffer((ushort)shop,item))).Distinct().OrderBy(x=>x.Shop).ThenBy(x=>x.Item).ToArray();
    }
    /// <summary>Installs or refreshes the registry. <paramref name="exemptItems"/> is the white list; null keeps the one already in the ROM.
    /// White-listed offers are registered like the others (so the ROM remembers the list and their save bits never move) but flagged, so the shop never finds them.</summary>
    public static byte[] Apply(byte[] source,ushort messageId,IReadOnlyCollection<ushort>? exemptItems=null)
    {
        var previous=Read(source);byte[] native=source.AsSpan(0x180,0x456c).ToArray();
        if(previous is not null)foreach(var hook in Hooks)Write(native,hook.At-0x180,hook.Word);
        if(Convert.ToHexString(SHA256.HashData(native))!=TextHash)throw new InvalidDataException("Shop.cro no coincide con el ejecutable USUM verificado; no se aplicó ningún parche.");
        var exempt=exemptItems?.ToHashSet()??previous?.ExemptItems.ToHashSet()??[];
        if(exempt.Contains(0))throw new InvalidDataException("La lista blanca de compras únicas tiene un objeto vacío.");
        // The prototype offers are always registered: their bits may already be in a save.
        var offers=previous?.Offers.ToList()??[];var known=offers.ToHashSet();foreach(var item in CurrentOffers(source).Concat(PrototypeOffers))if(known.Add(item))offers.Add(item);
        if(offers.Count>MaximumOffers)throw new InvalidDataException($"Las compras únicas admiten hasta {MaximumOffers} ofertas por historial de ROM; no se omitió ningún objeto.");
        uint identity=previous?.Identity??NewIdentity(offers);uint location;byte[] result;
        if(previous is null)
        {
            if(!CroPatchSession.TryCreate(source,out var session,out string error)||!session.TryAllocateRelocatableCode(AllocationBytes,"USUM compras únicas · tiendas especiales",out var grant,out error))throw new InvalidDataException(error);
            if(!session.TryWriteCode(grant,MakePayload(grant.Offset,identity,messageId,offers,exempt),out error)||!session.TryBuildImage(out result,out _,out error))throw new InvalidDataException(error);location=grant.Offset;
        }
        else
        {
            location=previous.Location;result=(byte[])source.Clone();MakePayload(location,identity,messageId,offers,exempt).CopyTo(result,(int)location);
        }
        foreach(var hook in Hooks)Write(result,hook.At,Branch((uint)hook.At,location+(uint)hook.Entry));
        if(!CroSegmentExpander.TryUpdateHashes(result,out string problem))throw new InvalidDataException(problem);
        var confirmed=Read(result)??throw new InvalidDataException("No se pudo releer el parche.");
        if(!confirmed.Offers.SequenceEqual(offers)||!confirmed.Exempt.SetEquals(offers.Where(o=>exempt.Contains(o.Item))))throw new InvalidDataException("La tabla de ofertas no coincide después de aplicar.");
        return result;
    }
    private static uint NewIdentity(IReadOnlyList<SinglePurchaseOffer> offers)
    {
        var data=new byte[offers.Count*4];for(int i=0;i<offers.Count;i++){Half(data,i*4,offers[i].Shop);Half(data,i*4+2,offers[i].Item);}
        uint id=ReadWord(SHA256.HashData(data),0);return id==0?1:id;
    }
    private static byte[] MakePayload(uint location,uint identity,ushort messageId,IReadOnlyList<SinglePurchaseOffer> offers,IReadOnlySet<ushort> exempt)
    {
        byte[] payload=Enumerable.Repeat((byte)0xa5,AllocationBytes).ToArray();ArmPayload(location,identity,messageId).CopyTo(payload,0);Marker.CopyTo(payload.AsSpan(Manifest));
        Write(payload,Manifest+0x10,1);Write(payload,Manifest+0x14,AllocationBytes);Write(payload,Manifest+0x18,(uint)offers.Count);Write(payload,Manifest+0x1c,identity);Write(payload,Manifest+0x20,messageId);
        Write(payload,Registry,(uint)offers.Count);
        for(int i=0;i<offers.Count;i++){int row=Registry+4+i*8;Half(payload,row,(ushort)(offers[i].Shop|(exempt.Contains(offers[i].Item)?ExemptFlag:0)));Half(payload,row+2,offers[i].Item);Half(payload,row+4,(ushort)(i+1));Half(payload,row+6,ushort.MaxValue);}
        PayloadDigest(payload,0,AllocationBytes).CopyTo(payload,Digest);return payload;
    }
}
