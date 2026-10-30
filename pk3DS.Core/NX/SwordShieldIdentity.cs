#nullable enable
using System;
using System.Buffers.Binary;
using System.IO;

namespace pk3DS.Core.NX;

public sealed record SwordShieldIdentity(string TitleId, string Game, string BuildId, string? ProfileVersion)
{
    public bool CanEdit => ProfileVersion == "1.3.2";
    public static SwordShieldIdentity Read(ReadOnlySpan<byte> npdm, ReadOnlySpan<byte> nso)
    {
        if (npdm.Length < 0x80 || !npdm[..4].SequenceEqual("META"u8))
            throw new InvalidDataException("main.npdm no contiene una cabecera META válida.");
        uint aci = BinaryPrimitives.ReadUInt32LittleEndian(npdm[0x70..]);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(npdm[0x74..]);
        if (size < 0x38 || aci < 0x80 || aci > npdm.Length || size > npdm.Length - aci ||
            !npdm.Slice((int)aci, 4).SequenceEqual("ACI0"u8))
            throw new InvalidDataException("El bloque ACI0 de main.npdm está truncado o es inválido.");
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(npdm[((int)aci + 0x10)..]);
        string game = id switch
        {
            0x0100ABF008968000 => "Pokémon Espada",
            0x01008DB008C2C000 => "Pokémon Escudo",
            _ => throw new InvalidDataException("El Title ID no pertenece a Pokémon Espada ni Escudo."),
        };
        if (nso.Length < 0x100 || !nso[..4].SequenceEqual("NSO0"u8))
            throw new InvalidDataException("exefs/main no contiene una cabecera NSO0 válida.");
        string build = Convert.ToHexString(nso.Slice(0x40, 0x20));
        // Full module IDs, not offsets. Unknown builds remain inspectable but cannot export edits.
        string expected = id == 0x0100ABF008968000
            ? "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000"
            : "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000";
        return new(id.ToString("X16"), game, build, build == expected ? "1.3.2" : null);
    }
}
