using System;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Randomizers;

/// <summary>Known OR/AS selector layouts. Both context anchors must identify exactly one layout.</summary>
public static class Gen6TradeSelectorProfile
{
    private static readonly int[][] Layouts = [[0x3196C8,0x319F38],[0x3196C0,0x319F30]];
    private static readonly byte[][] Before = [Convert.FromHexString("0100A0E31080BDE80000A0E31080BDE8F0412DE9"),Convert.FromHexString("485080E59C4040E24C50C0E5EC009FE5")];
    private static readonly byte[][] After = [Convert.FromHexString("0070A0E10260A0E10150A0E3BC2EC0E1"),Convert.FromHexString("BC7EC4E1")];
    private static readonly byte[][] Clean = [Convert.FromHexString("0140A0E1"),Convert.FromHexString("0050D0E5")];
    private static readonly byte[][] Patched = [Convert.FromHexString("0340A0E3"),Convert.FromHexString("0350A0E3")];
    public static int[] Resolve(byte[] code)
    {
        bool Match(int at,int index) => at>=Before[index].Length&&at+4+After[index].Length<=code.Length&&
            code.AsSpan(at-Before[index].Length,Before[index].Length).SequenceEqual(Before[index])&&
            code.AsSpan(at+4,After[index].Length).SequenceEqual(After[index])&&
            (code.AsSpan(at,4).SequenceEqual(Clean[index])||code.AsSpan(at,4).SequenceEqual(Patched[index]));
        var matches=Layouts.Where(layout=>Match(layout[0],0)&&Match(layout[1],1)).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Selector PC OR/AS desconocido o ambiguo; no se aplicará el parche de intercambios.");
        return (int[])matches[0].Clone();
    }
}
