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
    public int InflictCount { get; init; }
    public int TurnsMin { get; init; }
    public int TurnsMax { get; init; }
    public int Recoil { get; init; }
    public int Healing { get; init; }
    public int Stat1 { get; init; }
    public int Stat2 { get; init; }
    public int Stat3 { get; init; }
    public int Stat1Stage { get; init; }
    public int Stat2Stage { get; init; }
    public int Stat3Stage { get; init; }
    public int Stat1Rate { get; init; }
    public int Stat2Rate { get; init; }
    public int Stat3Rate { get; init; }
    public int RefreshType { get; init; }
    public int RefreshRate { get; init; }
}
