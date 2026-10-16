#nullable disable
namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

public sealed class ProgressiveBSTRule
{
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int MinBST { get; set; }
    public int MaxBST { get; set; }
    public bool FullRandom { get; set; }

    public ProgressiveBSTRule Clone()
    {
        return new ProgressiveBSTRule
        {
            MinLevel = MinLevel,
            MaxLevel = MaxLevel,
            MinBST = MinBST,
            MaxBST = MaxBST,
            FullRandom = FullRandom,
        };
    }
}