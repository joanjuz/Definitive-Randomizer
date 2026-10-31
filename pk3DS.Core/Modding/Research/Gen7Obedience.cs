#nullable enable
using System;
using System.Buffers.Binary;
using System.IO;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Obedience levels of Gen7 (code.bin): a six-byte table indexed by the completed grand trials (20, 35, 50, 65, 80, 100).
/// The battle setup copies the level for the current progress, and a Pokémon from another trainer above it disobeys.
/// Writing 100 everywhere gives every progress the champion's rule, so traded Pokémon always obey.
/// </summary>
public static class Gen7Obedience
{
    public const string ActionId="player.traded-obedience";
    private const uint CodeBase=0x100000;
    private static readonly byte[] Stock=[20,35,50,65,80,100];
    private static readonly byte[] Patched=[100,100,100,100,100,100];

    public enum State{Stock,Applied}

    // File offset of the table and of the battle setup's literal that points to it, checked in the four retail executables.
    private static (int Table,int Literal) Location(bool usum)=>usum?(0x4C4644,0x21BD64):(0x49E390,0x213C0C);

    public static State Read(byte[] code,bool usum)
    {
        var (table,literal)=Location(usum);
        if(code.Length<table+Stock.Length||code.Length<literal+4||BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(literal))!=CodeBase+table)
            throw new InvalidDataException("code.bin no tiene la tabla de obediencia en la posición esperada; no se aplicó nada.");
        var bytes=code.AsSpan(table,Stock.Length);
        if(bytes.SequenceEqual(Stock))
            return State.Stock;
        if(bytes.SequenceEqual(Patched))
            return State.Applied;
        throw new InvalidDataException("La tabla de obediencia de code.bin tiene valores desconocidos; no se aplicó nada.");
    }

    /// <summary>Returns false when the table was already patched.</summary>
    public static bool Apply(byte[] code,bool usum)
    {
        if(Read(code,usum)==State.Applied)
            return false;
        Patched.CopyTo(code,Location(usum).Table);
        return true;
    }
}
