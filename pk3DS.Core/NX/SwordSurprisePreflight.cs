using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Buffers.Binary;
using System.Security.Cryptography;
namespace pk3DS.Core.NX;
public sealed record SwordSurpriseSignature(string Name,int Offset,bool Matches);
public sealed record SwordSurpriseAudit(string BuildId,string TextSha256,IReadOnlyList<SwordSurpriseSignature> Signatures)
{
    // Instruction matches alone never establish semantic safety or readiness of a native hook.
    public bool SignaturesMatch => Signatures.All(s=>s.Matches);
}
/// <summary>Read-only preflight for Sword 1.3.2. Does not emit any executable patch.</summary>
public static class SwordSurprisePreflight
{
    public const string BuildId="A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000";
    public const int TextSize=26218288;
    private sealed record Signature(string Name,int Offset,string Hex);
    // Source: JonatanUHC/PokeFeurRandomSWSH contract/hook-baseline-epee-1.3.2.json.
    // These public preimages identify candidate sites, not an adapter implementation.
    private static readonly Signature[] Profile = {
        new("RandomizeWildBattleSpecies[0]",0x766D64,"602A40B9E1031F2A21F7FF97E00F1F32"),
        new("RandomizeWildBattleSpecies[1]",0x766D98,"602A40B915F7FF97800280521BF7FF97"),
        new("RandomizeWildBattleSpecies[2]",0x766EBC,"602A40B974410094606200796866C079"),
        new("RandomizeWildBattleSpecies[3]",0x766EE4,"602A40B9615A4079C1F6FF97C0028052"),
        new("RandomizeGeneratedBattleSpeciesSetter",0x76F2C0,"F50F1DF8F44F01A9FD7B02A9FD830091"),
        new("RandomizeGeneratedBattleFormSetter",0x76F3F0,"F50F1DF8F44F01A9FD7B02A9FD830091"),
        new("RandomizeGeneratedBattleSpeciesGetter",0x779B90,"F70F1CF8F65701A9F44F02A9FD7B03A9"),
        new("RandomizeGeneratedBattleFormGetter",0x779C80,"F70F1CF8F65701A9F44F02A9FD7B03A9"),
        new("RandomizeGimmickSpawns",0xD30D60,"F30301AA3F0800B9F40300AABF0E01F8"),
        new("RandomizeSlotSpawns",0xD32A00,"F80302AAFBC7E897E00F1F3201C8E897"),
        new("RandomizeBattleDataStore",0xD609C4,"88420179894601798C4A01798D4E0179"),
        new("RandomizeEncountObjectBattleSpec",0xEA2868,"A84E40B908A308B9A8324B3908672F39"),
    };
    public static SwordSurpriseAudit Audit(ReadOnlySpan<byte> nso)
    {
        if(nso.Length<0x100||!nso[..4].SequenceEqual("NSO0"u8))throw new InvalidDataException("NSO truncado o inválido.");
        if(Convert.ToHexString(nso.Slice(0x40,32))!=BuildId)throw new NotSupportedException("El preflight de combate sorpresa admite únicamente Espada 1.3.2.");
        uint flags=BinaryPrimitives.ReadUInt32LittleEndian(nso[0xC..]);
        uint file=BinaryPrimitives.ReadUInt32LittleEndian(nso[0x10..]),memory=BinaryPrimitives.ReadUInt32LittleEndian(nso[0x14..]),size=BinaryPrimitives.ReadUInt32LittleEndian(nso[0x18..]);
        if(memory!=0||size!=TextSize||file<0x100||file>nso.Length)throw new InvalidDataException("El segmento .text no coincide con el perfil de Espada.");
        uint packed=(flags&1)!=0?BinaryPrimitives.ReadUInt32LittleEndian(nso[0x60..]):size;
        if(packed==0||packed>nso.Length-file)throw new InvalidDataException("Segmento NSO fuera del archivo.");
        byte[] text=(flags&1)!=0?SwitchGfPack.DecodeLz4(nso.Slice((int)file,(int)packed),(int)size):nso.Slice((int)file,(int)size).ToArray();
        byte[] hash=SHA256.HashData(text);
        if((flags&8)!=0&&!hash.AsSpan().SequenceEqual(nso.Slice(0xA0,32)))throw new InvalidDataException("El SHA-256 del segmento .text no coincide con la cabecera NSO.");
        return AuditText(text);
    }
    public static SwordSurpriseAudit AuditText(ReadOnlySpan<byte> text)
    {
        if(text.Length!=TextSize)throw new InvalidDataException("Longitud .text incompatible con Espada 1.3.2.");
        var signatures=new List<SwordSurpriseSignature>();
        foreach(var p in Profile){byte[] expected=Convert.FromHexString(p.Hex);signatures.Add(new(p.Name,p.Offset,text.Slice(p.Offset,expected.Length).SequenceEqual(expected)));}
        return new(BuildId,Convert.ToHexString(SHA256.HashData(text)),Array.AsReadOnly(signatures.ToArray()));
    }
}
