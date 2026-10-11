namespace ThreeDS.Progressive.Models;

public sealed record MoveListEntry(int Id, string Name)
{
    public string Display => $"{Name}   #{Id:000}";
}

public sealed record MoveValues(
    int Type, int Category, int Power, int Accuracy, int PP, int Priority);
