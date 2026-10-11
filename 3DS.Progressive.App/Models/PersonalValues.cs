namespace ThreeDS.Progressive.Models;

/// <summary>
/// Campos incluidos en el primer hito. El resto del registro personal se preserva byte por byte.
/// </summary>
public sealed record PersonalValues(
    int HP, int Attack, int Defense, int SpecialAttack, int SpecialDefense,
    int Speed, int CatchRate, int FirstType, int SecondType)
{
    public int Ability1 { get; init; }
    public int Ability2 { get; init; }
    public int AbilityHidden { get; init; }
    public int Item1 { get; init; }
    public int Item2 { get; init; }
    public int Item3 { get; init; }
    public int EvHp { get; init; }
    public int EvAttack { get; init; }
    public int EvDefense { get; init; }
    public int EvSpecialAttack { get; init; }
    public int EvSpecialDefense { get; init; }
    public int EvSpeed { get; init; }
    public int BaseExperience { get; init; }
    public int BaseFriendship { get; init; }
    public int HatchCycles { get; init; }
    public int GenderRatio { get; init; }
    public int EggGroup1 { get; init; }
    public int EggGroup2 { get; init; }
    public int ExperienceGrowth { get; init; }
    public int EscapeRate { get; init; }
}

public sealed record PokemonListEntry(int Index, string Name)
{
    public string Display => Name + "   #" + Index.ToString("000");
}
