using System;
using System.Collections.Generic;
namespace pk3DS.Core.NX;
// SWSH 1.3.2 mapping, pkNX Tables8 at d191cd0e5c05f2af81d9a41c1f1d82e6621b351a (GPL-3.0).
public static class SwordShieldMachines
{
    private static readonly int[] TM={5,25,6,7,8,9,19,42,63,416,345,76,669,83,86,91,103,113,115,219,120,156,157,168,173,182,184,196,202,204,211,213,201,240,241,258,250,251,261,263,129,270,279,280,286,291,311,313,317,328,331,333,340,341,350,362,369,371,372,374,384,385,683,409,419,421,422,423,424,427,433,472,478,440,474,490,496,506,512,514,521,523,527,534,541,555,566,577,580,581,604,678,595,598,206,403,684,693,707,784};
    private static readonly int[] TR={14,34,53,56,57,58,59,67,85,87,89,94,97,116,118,126,127,133,141,161,164,179,188,191,200,473,203,214,224,226,227,231,242,247,248,253,257,269,271,276,285,299,304,315,322,330,334,337,339,347,348,349,360,370,390,394,396,398,399,402,404,405,406,408,411,412,413,414,417,428,430,437,438,441,442,444,446,447,482,484,486,492,500,502,503,526,528,529,535,542,583,599,605,663,667,675,676,706,710,776};
    public static IEnumerable<int> Compatible(byte[] personal,int entry)
    {
        if(entry<0||entry>=SwordShieldPersonalTable.EntryCount||personal.Length!=SwordShieldPersonalTable.EntryCount*176)throw new ArgumentOutOfRangeException(nameof(entry));
        for(int i=0;i<100;i++){if((personal[entry*176+0x28+i/8]&(1<<(i%8)))!=0)yield return TM[i];if((personal[entry*176+0x3C+i/8]&(1<<(i%8)))!=0)yield return TR[i];}
    }
}
