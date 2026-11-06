using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace pk3DS.Core.Modding.Research;

public enum UsumMintState { NotInstalled, Installed, Incompatible, Modified }
public sealed record UsumMintInspection(UsumMintState State,string Detail,int PayloadOffset = -1);

/// <summary>Detached, nature-only Bag patch. All input bytes remain owned by the caller.</summary>
public static class UsumNatureMintPatch
{
    public const int TemplateBase = 0x18010;
    private const int MarkerOffset=0x3c0, DigestOffset=0x3d0;
    private static readonly int[] Sites=[0xc318,0xddc8,0xc358,0x80dc];
    private static readonly uint[] Originals=[0xebffd11e,0xebffca72,0xea00013f,0xea0000db];
    private static readonly int[] Entries=[0,0,0x80,0x100];
    public static UsumMintInspection Inspect(byte[] bag,UsumNatureMintProfile profile)
    {
        if(Convert.ToHexString(SHA256.HashData(bag))==profile.BagSha256)
            return new(UsumMintState.NotInstalled,"No instalado; Bag.cro coincide con el perfil.");
        int marker=bag.AsSpan().IndexOf("DGMINT1\0"u8);
        if(marker<0)return new(UsumMintState.Incompatible,"Bag.cro desconocido, ya modificado o de otra revisión.");
        int start=marker-MarkerOffset;
        try {
            if(start<0||start+0x400>bag.Length||bag.AsSpan(marker+8).IndexOf("DGMINT1\0"u8)>=0)
                throw new InvalidDataException("Marcador de mentas ambiguo o truncado.");
            byte[] expected=Payload(start,profile.Moon);
            if(!bag.AsSpan(start,DigestOffset).SequenceEqual(expected.AsSpan(0,DigestOffset))||
               !bag.AsSpan(start+DigestOffset+32,0x400-DigestOffset-32).SequenceEqual(expected.AsSpan(DigestOffset+32)))
                throw new InvalidDataException("El código, perfil o mapa de mentas cambió externamente.");
            for(int i=0;i<Sites.Length;i++)
                if(BitConverter.ToUInt32(bag,Sites[i])!=Branch(Sites[i],start+Entries[i],i<2))
                    throw new InvalidDataException("Uno de los hooks de mentas cambió externamente.");
            if(!Digest(bag,start).AsSpan().SequenceEqual(bag.AsSpan(start+DigestOffset,32)))
                throw new InvalidDataException("Bag.cro tiene cambios posteriores a la instalación reconocida.");
            var checkedImage=(byte[])bag.Clone();
            if(!CroSegmentExpander.TryUpdateHashes(checkedImage,out string error)||!checkedImage.AsSpan().SequenceEqual(bag))
                throw new InvalidDataException("Integridad CRO incorrecta: "+error);
            return new(UsumMintState.Installed,"Instalado y reconocido · 21 mentas · naturaleza real.",start);
        } catch(InvalidDataException ex){return new(UsumMintState.Modified,ex.Message);}
    }
    public static byte[] Prepare(byte[] bag,UsumNatureMintProfile profile)
    {
        var state=Inspect(bag,profile);
        if(state.State==UsumMintState.Installed)return (byte[])bag.Clone();
        if(state.State!=UsumMintState.NotInstalled)throw new InvalidDataException(state.Detail);
        for(int i=0;i<Sites.Length;i++)
            if(Sites[i]+4>bag.Length||BitConverter.ToUInt32(bag,Sites[i])!=Originals[i])
                throw new InvalidDataException("Firma de Bag.cro no compatible en "+Sites[i].ToString("X"));
        if(!CroPatchSession.TryCreate(bag,out var session,out string error)||
           !session.TryAllocateRelocatableCode(0x400,"USUM nature mints v1",out var grant,out error)||
           !session.TryWriteCode(grant,Payload(checked((int)grant.Offset),profile.Moon),out error)||
           !session.TryBuildImage(out var result,out _,out error))
            throw new InvalidDataException("No se pudo preparar Bag.cro: "+error);
        int start=checked((int)grant.Offset);
        for(int i=0;i<Sites.Length;i++)BitConverter.GetBytes(Branch(Sites[i],start+Entries[i],i<2)).CopyTo(result,Sites[i]);
        Digest(result,start).CopyTo(result,start+DigestOffset);
        if(!CroSegmentExpander.TryUpdateHashes(result,out error))throw new InvalidDataException(error);
        var verify=Inspect(result,profile);
        if(verify.State!=UsumMintState.Installed)throw new InvalidDataException(verify.Detail);
        return result;
    }
    private static byte[] Digest(byte[] bag,int start)
    {
        byte[] normalized=bag.AsSpan(0x80).ToArray();
        normalized.AsSpan(start+DigestOffset-0x80,32).Clear();
        return SHA256.HashData(normalized);
    }
    private static uint Branch(int from,int target,bool link)
    {
        int delta=checked(target-from-8);
        if((delta&3)!=0||delta is <-0x2000000 or >=0x2000000)throw new InvalidDataException("Salto ARM fuera de rango.");
        return (link?0xeb000000u:0xea000000u)|((uint)(delta>>2)&0xffffff);
    }
    private static byte[] Payload(int address,bool moon)
    {
        byte[] result=UsumNatureMintPayload.Create();
        for(int offset=0;offset<0x300;offset+=4){
            uint word=BitConverter.ToUInt32(result,offset);
            if((word&0x0e000000)!=0x0a000000)continue;
            int displacement=unchecked((int)(word<<8))>>6;
            int target=checked(TemplateBase+offset+8+displacement);
            if(target>=TemplateBase&&target<TemplateBase+result.Length)continue;
            uint relocated=(word&0xff000000)|(Branch(address+offset,target,false)&0xffffff);
            BitConverter.GetBytes(relocated).CopyTo(result,offset);
        }
        if(moon)foreach(int literal in new[]{0x300,0x308,0x310})
            BitConverter.GetBytes(BitConverter.ToUInt32(result,literal)+8).CopyTo(result,literal);
        for(int i=0;i<UsumNatureMintCatalog.Mints.Count;i++){
            var mint=UsumNatureMintCatalog.Mints[i];
            BitConverter.GetBytes((uint)mint.ItemId|((uint)mint.NatureId<<24)).CopyTo(result,0x340+4*i);
        }
        "DGMINT1\0"u8.CopyTo(result.AsSpan(MarkerOffset));
        BitConverter.GetBytes(moon?1:0).CopyTo(result,MarkerOffset+8);
        return result;
    }
}
