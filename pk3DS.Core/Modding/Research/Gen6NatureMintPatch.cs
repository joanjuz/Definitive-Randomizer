#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace pk3DS.Core.Modding.Research;

public sealed record Gen6MintNativeProfile(string Id, ulong TitleId, string CodeSha256, string BagSha256);
public enum Gen6MintNativeState { Original, Installed, Partial, Modified, Incompatible }
public sealed record Gen6MintNativeInspection(Gen6MintNativeState State, string Report, int PayloadOffset = -1);

/// <summary>Detached, experimental XY Bag patch. This primitive does not admit a project;
/// the caller must accredit its independent title/code/text/CRR/resource profile first.</summary>
public static class Gen6NatureMintPatch
{
    public const int TemplateBase=0x20000, PayloadSize=0xb00;
    private const int MarkerOffset=0xa00, TitleOffset=0xa10, SourceOffset=0xa20, DigestOffset=0xa40;
    private static readonly int[] Sites=[0x428,0x2b5c];
    private static readonly uint[] Originals=[0xe51ff004,0xe5942058];
    private static readonly int[] Entries=[0,0x100];
    private static ReadOnlySpan<byte> Marker => "XYMINT-E1\0"u8;

    public static Gen6MintNativeInspection Inspect(byte[] bag,Gen6MintNativeProfile profile)
    {
        if(profile.BagSha256 is null||profile.CodeSha256 is null||
            profile.BagSha256.Length!=64||profile.CodeSha256.Length!=64||
            !profile.BagSha256.All(Uri.IsHexDigit)||!profile.CodeSha256.All(Uri.IsHexDigit))
            return new(Gen6MintNativeState.Incompatible,"Huellas del perfil inválidas.");
        if(profile.TitleId is not (0x0004000000055D00UL or 0x0004000000055E00UL))
            return new(Gen6MintNativeState.Incompatible,"El perfil no es de X/Y.");
        if(!HashesConsistent(bag))return new(Gen6MintNativeState.Incompatible,"CRO0 o hashes inválidos.");
        int marker=bag.AsSpan().IndexOf(Marker);
        if(marker<0)
            return Hash(bag)==profile.BagSha256 && Sites.Select((at,i)=>at+4<=bag.Length&&BitConverter.ToUInt32(bag,at)==Originals[i]).All(v=>v)
                ?new(Gen6MintNativeState.Original,"DllBag.cro original compatible; candidato experimental.")
                :new(Gen6MintNativeState.Incompatible,"DllBag.cro desconocido o hooks ocupados; no se escribe.");
        int start=marker-MarkerOffset;
        try
        {
            if(start<0||start>bag.Length-PayloadSize||bag.AsSpan(marker+Marker.Length).IndexOf(Marker)>=0)
                throw new InvalidDataException("Marcador ambiguo o truncado.");
            if(BitConverter.ToUInt64(bag,start+TitleOffset)!=profile.TitleId)
                return new(Gen6MintNativeState.Incompatible,"Candidato de otro título X/Y.");
            byte[] expected=Payload(start,profile);
            if(!bag.AsSpan(start,DigestOffset).SequenceEqual(expected.AsSpan(0,DigestOffset))||
                !bag.AsSpan(start+DigestOffset+32,PayloadSize-DigestOffset-32).SequenceEqual(expected.AsSpan(DigestOffset+32)))
                throw new InvalidDataException("Código o catálogo del candidato modificado.");
            int installed=0;
            for(int i=0;i<Sites.Length;i++)
            {
                uint word=BitConverter.ToUInt32(bag,Sites[i]);
                if(word==Branch(Sites[i],start+Entries[i]))installed++;
                else if(word!=Originals[i])throw new InvalidDataException("Hook modificado externamente.");
            }
            if(installed!=Sites.Length)return new(Gen6MintNativeState.Partial,"Instalación parcial; no se repara por sobrescritura.",start);
            if(!Digest(bag,start).AsSpan().SequenceEqual(bag.AsSpan(start+DigestOffset,32)))
                throw new InvalidDataException("El CRO tiene cambios externos posteriores al candidato.");
            if(!CroRelocationMap.TryCreate(bag,out var map,out string error)||
                !map.Segments.Any(s=>s.Index==0&&start>=s.Start&&start+PayloadSize<=s.Start+s.Size))
                throw new InvalidDataException("Payload fuera del segmento de código: "+error);
            return new(Gen6MintNativeState.Installed,"21 mentas experimentales reconocidas; aceptación jugable pendiente.",start);
        }
        catch(Exception ex) when(ex is InvalidDataException or ArgumentException or OverflowException)
        {return new(Gen6MintNativeState.Modified,ex.Message);}
    }
    public static byte[] Prepare(byte[] bag,Gen6MintNativeProfile profile)
    {
        var state=Inspect(bag,profile);
        if(state.State==Gen6MintNativeState.Installed)return (byte[])bag.Clone();
        if(state.State!=Gen6MintNativeState.Original)throw new InvalidDataException(state.Report);
        if(!CroPatchSession.TryCreate(bag,out var session,out string error)||
            !session.TryAllocateRelocatableCode(PayloadSize,"XY nature mints experiment v1",out var grant,out error)||
            !session.TryWriteCode(grant,Payload(checked((int)grant.Offset),profile),out error)||
            !session.TryBuildImage(out byte[] result,out _,out error))
            throw new InvalidDataException(error);
        int start=checked((int)grant.Offset);
        for(int i=0;i<Sites.Length;i++)BitConverter.GetBytes(Branch(Sites[i],start+Entries[i])).CopyTo(result,Sites[i]);
        Digest(result,start).CopyTo(result,start+DigestOffset);
        if(!CroSegmentExpander.TryUpdateHashes(result,out error))throw new InvalidDataException(error);
        var verify=Inspect(result,profile);
        if(verify.State!=Gen6MintNativeState.Installed)throw new InvalidDataException(verify.Report);
        return result;
    }
    private static byte[] Payload(int start,Gen6MintNativeProfile profile)
    {
        byte[] result=Gen6NatureMintPayload.Create();
        for(int offset=0;offset<0x880;offset+=4)
        {
            uint word=BitConverter.ToUInt32(result,offset);
            if((word&0x0e000000)!=0x0a000000||word>>28==15)continue;
            int displacement=unchecked((int)(word<<8))>>6;
            int target=checked(TemplateBase+offset+8+displacement);
            if(target>=TemplateBase&&target<TemplateBase+PayloadSize)continue;
            uint relocated=(word&0xff000000)|(Branch(start+offset,target)&0xffffff);
            BitConverter.GetBytes(relocated).CopyTo(result,offset);
        }
        for(int i=0;i<21;i++)
        {
            var item=Gen6NatureMintCatalog.ExperimentalItemMap.Items[i];
            BitConverter.GetBytes((uint)item.ItemId|((uint)item.NatureId<<24)).CopyTo(result,0x900+i*4);
        }
        Marker.CopyTo(result.AsSpan(MarkerOffset));
        BitConverter.GetBytes(profile.TitleId).CopyTo(result,TitleOffset);
        byte[] source=Convert.FromHexString(profile.BagSha256);
        if(source.Length!=32)throw new InvalidDataException("SHA-256 de origen inválido.");
        source.CopyTo(result,SourceOffset);
        return result;
    }
    private static uint Branch(int at,int target)
    {
        int delta=checked(target-at-8);
        if((at&3)!=0||(target&3)!=0||delta is <-0x2000000 or >=0x2000000)
            throw new InvalidDataException("Salto ARM no alineado o fuera de rango.");
        return 0xea000000u|((uint)(delta>>2)&0xffffff);
    }
    private static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));
    private static byte[] Digest(byte[] bag,int start)
    {
        byte[] normalized=bag.AsSpan(0x80).ToArray();
        normalized.AsSpan(start+DigestOffset-0x80,32).Clear();return SHA256.HashData(normalized);
    }
    private static bool HashesConsistent(byte[] bag)
    {
        if(bag.Length<0x180||!bag.AsSpan(0x80,4).SequenceEqual("CRO0"u8))return false;
        var copy=(byte[])bag.Clone();
        return CroSegmentExpander.TryUpdateHashes(copy,out _)&&copy.AsSpan().SequenceEqual(bag);
    }
}
