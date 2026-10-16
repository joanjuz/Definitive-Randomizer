using System;
using System.Collections.Generic;
using System.Linq;

namespace ThreeDS.Progressive.Models;

/// <summary>Índice idéntico al selector del editor WinForms original; no se cambia ningún ID.</summary>
public sealed record MoveChoice(int Id, string Name)
{
    public string Display => $"{Id:000} · {Name}";
    public override string ToString() => Name;
}

public static class MoveEditorCatalog
{
    public static readonly string[] QualityNames =
    [
        "Only DMG",
        "No DMG -> Inflict Status",
        "No DMG -> -Target/+User Stat",
        "No DMG | Heal User",
        "DMG | Inflict Status",
        "No DMG | STATUS | +Target Stat",
        "DMG | -Target Stat",
        "DMG | +User Stat",
        "DMG | Absorbs DMG",
        "One-Hit KO",
        "Affects Whole Field",
        "Affect One Side of the Field",
        "Forces Target to Switch",
        "Unique Effect",
    ];

    public static readonly string[] ZEffectNames =
    [
        "None",
        "+1 Attack",
        "+2 Attack",
        "+3 Attack",
        "+1 Defense",
        "+2 Defense",
        "+3 Defense",
        "+1 Special Attack",
        "+2 Special Attack",
        "+3 Special Attack",
        "+1 Special Defense",
        "+2 Special Defense",
        "+3 Special Defense",
        "+1 Speed",
        "+2 Speed",
        "+3 Speed",
        "+1 Accuracy",
        "+2 Accuracy",
        "+3 Accuracy",
        "+1 Evasiveness",
        "+2 Evasiveness",
        "+3 Evasiveness",
        "+1 to all (except Accuracy or Evasiveness)",
        "+2 to all (except Accuracy or Evasiveness)",
        "+3 to all (except Accuracy or Evasiveness)",
        "raises critical-hit ratio two stages",
        "resets lowered stats of the user",
        "recovers all of user's HP",
        "recovers all Hp of the Pokémon switching-in (Memento and Parting Shot)",
        "makes the user the center of attention",
        "only on Curse: recovers all HP if the user's a Ghost type, +1 Attack otherwise",
    ];

    public static readonly string[] InflictionNames =
    [
        "None",
        "Paralyze",
        "Sleep",
        "Freeze",
        "Burn",
        "Poison",
        "Confusion",
        "Attract",
        "Capture",
        "Nightmare",
        "Curse",
        "Taunt",
        "Torment",
        "Disable",
        "Yawn",
        "Heal Block",
        "?",
        "Detect",
        "Leech Seed",
        "Embargo",
        "Perish Song",
        "Ingrain",
        "??? 0x16",
        "??? 0x17",
        "Mute",
    ];

    public static readonly string[] TargetNames =
    [
        "Single Adjacent Ally/Foe",
        "Any Ally",
        "Any Adjacent Ally",
        "Single Adjacent Foe",
        "Everyone but User",
        "All Foes",
        "All Allies",
        "Self",
        "All Pokémon on Field",
        "Single Adjacent Foe (2)",
        "Entire Field",
        "Opponent's Field",
        "User's Field",
        "Self",
    ];

    public static readonly string[] StatNames =
    [
        "None",
        "Attack",
        "Defense",
        "Special Attack",
        "Special Defense",
        "Speed",
        "Accuracy",
        "Evasion",
        "All",
    ];

    public static IReadOnlyList<MoveChoice> IndexedChoices(string[] names, int max = 256) =>
        Enumerable.Range(0, max).Select(i =>
            new MoveChoice(i, i < names.Length ? names[i] : "Código no catalogado")).ToArray();

    public static IReadOnlyList<MoveChoice> InflictionChoices =>
        IndexedChoices(InflictionNames, 25).Concat(
            [new MoveChoice(65535, "Especial (valor -1)")]).ToArray();
}
