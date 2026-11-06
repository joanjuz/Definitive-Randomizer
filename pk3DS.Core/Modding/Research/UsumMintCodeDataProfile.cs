#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using pk3DS.Core.Properties;

namespace pk3DS.Core.Modding.Research;

/// <summary>Only established TM data and the existing shiny editor's supported instructions may vary.</summary>
public static class UsumMintCodeDataProfile
{
    public const int TmOffset=0x4BB98E,PidOffset=0x220668,AlwaysShinyOffset=0x2205CF;
    public static byte[] Normalize(byte[] input)
    {
        if(input.Length!=5914624)throw new InvalidDataException("Tamaño de ejecutable USUM desconocido.");
        for(int i=0;i<100;i++)if(BitConverter.ToUInt16(input,TmOffset+2*i) is <1 or >728)
            throw new InvalidDataException("La tabla de MT contiene movimientos fuera del perfil USUM acreditado.");
        bool vanilla=input.AsSpan(PidOffset,4).SequenceEqual(new byte[]{0x23,0,0xD4,0xE5});
        byte[] instructions=Resources.asm_mov;
        bool supported=input[PidOffset+2]==0xA0&&input[PidOffset+3]==0xE3&&Enumerable.Range(0,instructions.Length/4)
            .Any(i=>BitConverter.ToUInt16(instructions,i*4)>0&&BitConverter.ToUInt16(instructions,i*4+2)==BitConverter.ToUInt16(input,PidOffset));
        if(!vanilla&&!supported||input[AlwaysShinyOffset] is not (0x0A or 0xEA))
            throw new InvalidDataException("Instrucciones Shiny desconocidas; no se consideran compatibles con mentas.");
        byte[] code=(byte[])UsumMintIconLayout.NormalizeCode(input).Clone();
        code.AsSpan(TmOffset,200).Clear();code.AsSpan(PidOffset,4).Clear();code[AlwaysShinyOffset]=0;
        return code;
    }
    public static UsumNatureMintProfile? Match(byte[] code)
    {
        if(code.Length!=5914624)return null;
        string hash=Convert.ToHexString(SHA256.HashData(Normalize(code)));
        return hash switch
        {
            "62581F18A234A6B5AB340FD0BB4306F1885DCBA0EB6DD134F6FDF226B38FB64F"=>UsumNatureMintCatalog.Profiles.Single(p=>!p.Moon),
            "56E482BB6E2E7960391C458E41A6B2FF8FE0373A05C6527365D62E344E873793"=>UsumNatureMintCatalog.Profiles.Single(p=>p.Moon),
            _=>null
        };
    }
}
