#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using pk3DS.Core.Properties;

namespace pk3DS.Core.Modding.Research;

/// <summary>Validate known variable tables, then protect every remaining byte by executable segment.</summary>
public static class UsumMintCodeDataProfile
{
    public const int TmOffset=0x4BB98E,PidOffset=0x220668,AlwaysShinyOffset=0x2205CF;
    public const int TypeChartOffset=0x4BB558,ObedienceOffset=0x4C4644;
    // Page-aligned file segments from the two accredited exheaders. All native functions,
    // their transitive callees, literals, imports and unclassified data remain protected.
    private static readonly (int Offset,int Length,string Name)[] Segments=[
        (0,0x4BA000,"código ejecutable y funciones nativas"),
        (0x4BA000,0xAD000,"datos de solo lectura y referencias"),
        (0x567000,0x3D000,"datos del runtime")];
    private static readonly string[][] Hashes=[
        ["9BB50A592AC367E977D3CDD482409BDA875B03CE10B9D3E0A661B0BC7294E56F","57E888649437C5D4ED234068166B3BC0033ECA2BF4F1C07669330775F2454752","2689F73649A01B6AAC179D032966F90E212435BB9F8891BDAD08B5489317E7E6"],
        ["A5BE0CBD091062FC0866301759CC59F5B9395CB9984D0C30BC84AE6B4E4584ED","9BA3886B284D0F911DC39E3D73740C414323512D7C276522820D4F15C60BF07D","36FEAC7BA9444251503B8167D46276431EBAA03CD0CAFC0021548D292413C67C"]];

    public static void ValidateMemoryLayout(byte[] exheader,bool moon)
    {
        uint[] expected=[0x100000,0x4BA,moon?0x4B99F8u:0x4B99F0u,0x5BA000,0xAD,0xAC948,0x667000,0x3D,0x3CD48];
        if(exheader.Length<0x40||Enumerable.Range(0,9).Any(i=>BitConverter.ToUInt32(exheader,0x10+(i/3)*0x10+(i%3)*4)!=expected[i]))
            throw new InvalidDataException("El mapa de memoria del exheader no coincide con las direcciones nativas USUM acreditadas.");
    }
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
        byte[] normalized=Normalize(code);
        int[] candidates=[0,1];
        for(int i=0;i<Segments.Length;i++){
            var segment=Segments[i];
            string hash=Convert.ToHexString(SHA256.HashData(normalized.AsSpan(segment.Offset,segment.Length)));
            candidates=candidates.Where(p=>Hashes[p][i]==hash).ToArray();
            if(candidates.Length==0)throw new InvalidDataException($"Modificación no acreditada en {segment.Name} de code.bin (0x{segment.Offset:X}–0x{segment.Offset+segment.Length-1:X}). No se puede demostrar compatibilidad con mentas.");
        }
        return UsumNatureMintCatalog.Profiles.Single(p=>p.Moon==(candidates[0]==1));
    }
}
