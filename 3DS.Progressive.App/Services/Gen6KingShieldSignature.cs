using System.IO;

namespace ThreeDS.Progressive.Services;

internal static class Gen6KingShieldSignature
{
    internal static readonly byte[] Original=Convert.FromHexString("0120A0E3FE30A0E3");
    internal static readonly byte[] Patched=Convert.FromHexString("0120A0E3FF30A0E3");
    internal static void Validate(byte[] cro)
    {
        int count=Count(Original)+Count(Patched);
        if(count!=1)throw new InvalidDataException("Escudo Real Gen6 requiere una firma original o parcheada única en DLLBattle.cro.");
        int Count(byte[] pattern)
        {
            int found=0,start=0;
            while(start<=cro.Length-pattern.Length)
            {int relative=cro.AsSpan(start).IndexOf(pattern);if(relative<0)break;found++;start+=relative+1;}
            return found;
        }
    }
}
