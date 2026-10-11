namespace ThreeDS.Progressive.Models;

/// <summary>
/// Campos incluidos en el primer hito. El resto del registro personal se preserva byte por byte.
/// </summary>
public sealed record PersonalValues(
    int HP, int Attack, int Defense, int SpecialAttack, int SpecialDefense,
    int Speed, int CatchRate, int FirstType, int SecondType);

public sealed record PokemonListEntry(int Index, string Name)
{
    public string Display => Name + "   #" + Index.ToString("000");
}
