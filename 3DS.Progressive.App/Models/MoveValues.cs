namespace ThreeDS.Progressive.Models;

public sealed record MoveListEntry(int Id, string Name)
{
    public string Display => $"{Name}   #{Id:000}";
}

public sealed record MoveValues(
    int Type, int Category, int Power, int Accuracy, int PP, int Priority)
{
    public int Quality { get; init; }
    public int HitMin { get; init; }
    public int HitMax { get; init; }
    public int Inflict { get; init; }
    public int InflictPercent { get; init; }
    public int CritStage { get; init; }
    public int Flinch { get; init; }
    public int EffectSequence { get; init; }
    public int Target { get; init; }
    public int ZMove { get; init; }
    public int ZPower { get; init; }
    public int ZEffect { get; init; }
    public uint Flags { get; init; }
}
