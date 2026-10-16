using System.IO;

namespace ThreeDS.Progressive.Services;

internal static class Gen6HmRestriction
{
    internal static readonly byte[] XY=Convert.FromHexString("0F0050E3130050130500000A390050E3460050130200000A7F0050E30000A0130000001A0100A0E31EFF2FE1");
    internal static readonly byte[] ORAS=Convert.FromHexString("3C309FE50010A0E3812083E0B8CBD2E100005CE1BA2BD211000052110600000A021081E2060051E3F6FFFF3A011C40E2231051E20000A0130000001A0100A0E31EFF2FE1EE675A00");
    private static readonly byte[] Patch=Convert.FromHexString("0000A0E31EFF2FE1");
    internal static (int Offset,bool Patched) Locate(byte[] code,bool oras)
    {
        byte[] original=oras?ORAS:XY,patched=(byte[])original.Clone();Patch.CopyTo(patched,0);
        var matches=new List<(int Offset,bool Patched)>();
        Scan(original,false);Scan(patched,true);
        if(matches.Count>1)throw new InvalidDataException("La función de restricción MO no es única.");
        return matches.Count==1?matches[0]:(-1,false);
        void Scan(byte[] pattern,bool changed)
        {
            int start=0;
            while(start<=code.Length-pattern.Length)
            {int relative=code.AsSpan(start).IndexOf(pattern);if(relative<0)break;int at=start+relative;matches.Add((at,changed));start=at+1;}
        }
    }
    internal static void Set(byte[] code,bool oras,bool forget)
    {
        var match=Locate(code,oras);
        if(match.Offset<0)
        {if(forget)throw new InvalidDataException("No hay una función MO original compatible; no se parcheará.");return;}
        (forget?Patch:(oras?ORAS:XY)[..8]).CopyTo(code,match.Offset);
    }
}
