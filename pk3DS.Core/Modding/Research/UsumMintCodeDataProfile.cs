#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using pk3DS.Core.Properties;

namespace pk3DS.Core.Modding.Research;

/// <summary>Validate the bounded native dependencies of mints, independently of unrelated game code.</summary>
public static class UsumMintCodeDataProfile
{
    public const int TmOffset=0x4BB98E,PidOffset=0x220668,AlwaysShinyOffset=0x2205CF;
    public const int TypeChartOffset=0x4BB558,ObedienceOffset=0x4C4644;

    public static void ValidateMemoryLayout(byte[] exheader,bool moon)
    {
        uint[] expected=[0x100000,0x4BA,moon?0x4B99F8u:0x4B99F0u,0x5BA000,0xAD,0xAC948,0x667000,0x3D,0x3CD48];
        if(exheader.Length<0x40||Enumerable.Range(0,9).Any(i=>BitConverter.ToUInt32(exheader,0x10+(i/3)*0x10+(i%3)*4)!=expected[i]))
            throw new InvalidDataException("El mapa de memoria del exheader no coincide con las direcciones nativas USUM acreditadas.");
    }
    // Historical table-normalization utility. It is NOT an installation gate;
    // Match/ValidateNativeDependencies intentionally do not call it.
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
        if(input.AsSpan(TypeChartOffset,324).ToArray().Any(v=>v is not (0 or 2 or 4 or 8)))
            throw new InvalidDataException("Tabla de efectividad de tipos desconocida; solo se admiten valores 0, 2, 4 y 8.");
        _=Gen7Obedience.Read(input,true); // Includes the unchanged literal pointing at this six-byte table.
        code.AsSpan(TypeChartOffset,324).Clear();code.AsSpan(ObedienceOffset,6).Clear();
        code.AsSpan(TmOffset,200).Clear();code.AsSpan(PidOffset,4).Clear();code[AlwaysShinyOffset]=0;
        return code;
    }
    public static UsumNatureMintProfile? Match(byte[] code)
    {
        if(code.Length!=5914624)return null;
        string[] failures=new string[2];
        for(int i=0;i<2;i++){
            failures[i]=NativeFailure(code,i==1);
            if(failures[i].Length==0)return UsumNatureMintCatalog.Profiles.Single(p=>p.Moon==(i==1));
        }
        throw new InvalidDataException(string.Join("\n",failures));
    }
    public static void ValidateNativeDependencies(byte[] code,bool moon)
    {
        if(code.Length!=5914624)throw new InvalidDataException("Tamaño de code.bin USUM descomprimido incompatible.");
        string failure=NativeFailure(code,moon);
        if(failure.Length!=0)throw new InvalidDataException(failure);
    }
    private static string NativeFailure(byte[] code,bool moon)
    {
        foreach(var range in UsumMintNativeDependencies.Profiles[moon?1:0]){
            string hash=Convert.ToHexString(SHA256.HashData(code.AsSpan(range.Offset,range.Length)));
            if(hash!=range.Sha256)return $"Dependencia de mentas {(moon?"UM":"US")} no acreditada: {range.Name}, code.bin 0x{range.Offset:X}–0x{range.Offset+range.Length-1:X} (dirección nativa 0x{range.Offset+0x100000:X}). La implementación, instrucciones o referencias de esta dependencia cambiaron; no se instala.";
        }
        return "";
    }
}
