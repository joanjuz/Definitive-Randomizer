using System;
namespace pk3DS.Core.Randomizers;
public static class TrainerLevelProgression
{
    /// <summary>Monotone within each original-level interval and strictly below its next cap, including decreasing cap sequences.</summary>
    public static int Target(int originalAce,int previousOriginalAce,int previousCap,int nextOriginalAce,int nextCap,int gap,double power=1.6)
    {
        if(nextCap is <1 or >100||gap is <0 or >20||power<=0||double.IsNaN(power)||double.IsInfinity(power))throw new ArgumentOutOfRangeException(nameof(nextCap));
        int ceiling=Math.Max(1,nextCap-1),end=Math.Clamp(nextCap-gap,1,ceiling);
        int start=Math.Clamp(previousCap,1,end);
        double progress=Math.Clamp((originalAce-previousOriginalAce)/(double)Math.Max(1,nextOriginalAce-previousOriginalAce),0,1);
        int target=(int)Math.Round(start+(end-start)*Math.Pow(progress,power),MidpointRounding.AwayFromZero);
        return Math.Clamp(target,start,end);
    }
}
