// Data transformations and bilingual aliases ported from MoveEditor7 (f459e40).
using System.IO;
using pk3DS.Core.Structures;
namespace ThreeDS.Progressive.Services;
internal sealed partial class MoveTemplateApplier(int generation, string[] types)
{
    private readonly int _generation = generation;
    private readonly string[] types = types;
    private static readonly string[] MoveCategories = ["Status", "Physical", "Special"];
    private static readonly string[] MoveQualities = [
        "Only DMG",
        "No DMG -> Inflict Status", "No DMG -> -Target/+User Stat", "No DMG | Heal User", "DMG | Inflict Status", "No DMG | STATUS | +Target Stat",
        "DMG | -Target Stat", "DMG | +User Stat", "DMG | Absorbs DMG", "One-Hit KO", "Affects Whole Field",
        "Affect One Side of the Field", "Forces Target to Switch", "Unique Effect",
    ];
    private static readonly string[] TargetingTypes = [
        "Single Adjacent Ally/Foe",
        "Any Ally", "Any Adjacent Ally", "Single Adjacent Foe", "Everyone but User", "All Foes",
        "All Allies", "Self", "All Pokémon on Field", "Single Adjacent Foe (2)", "Entire Field",
        "Opponent's Field", "User's Field", "Self",
    ];
    private static readonly string[] InflictionTypes = [
        "None",
        "Paralyze", "Sleep", "Freeze", "Burn", "Poison",
        "Confusion", "Attract", "Capture", "Nightmare", "Curse",
        "Taunt", "Torment", "Disable", "Yawn", "Heal Block",
        "?", "Detect", "Leech Seed", "Embargo", "Perish Song",
        "Ingrain", "??? 0x16", "??? 0x17", "Mute",
    ];
    private static readonly string[] StatCategories = ["None", "Attack", "Defense", "Special Attack", "Special Defense", "Speed", "Accuracy", "Evasion", "All",
    ];
    internal int ResolveTemplateChoice(string column,string value)=>column switch
    {
        "Type"=>ResolveType(value),"Category"=>ResolveCategory(value),"Quality"=>ResolveQuality(value),
        "Targeting"=>ResolveTargeting(value),"Inflict"=>int.TryParse(value,out int id)?id:ResolveInflict(value),
        "UserStat" or "TargetStat" or "Stat1" or "Stat2" or "Stat3"=>ResolveStat(value),_=>throw new ArgumentException("Columna sin catálogo.")
    };
    internal void Apply(byte[] data, MoveTemplates.MovePatchRow patch)
    {
        ApplyCoreMovePatch(data, patch);
        if (_generation == 7) ApplyGen7ZMovePatch(data, patch);
        ApplyStatPatches(data, patch); ApplyFlagPatches(data, patch);
    }
    private void ApplyCoreMovePatch(byte[] data, MoveTemplates.MovePatchRow patch)
    {
        Validate(patch);
        SetOptionalByte(data, 0x00, ResolveType(patch.Type));
        SetOptionalByte(data, 0x01, ResolveQuality(patch.Quality));
        SetOptionalByte(data, 0x02, ResolveCategory(patch.Category));

        if (patch.Power.HasValue)
            data[0x03] = (byte)patch.Power.Value;

        if (patch.Accuracy.HasValue)
            data[0x04] = (byte)patch.Accuracy.Value;

        if (patch.PP.HasValue)
            data[0x05] = (byte)patch.PP.Value;

        if (patch.Priority.HasValue)
            data[0x06] = unchecked((byte)(sbyte)patch.Priority.Value);

        if (patch.HitMin.HasValue || patch.HitMax.HasValue)
        {
            int min = patch.HitMin ?? (data[0x07] & 0xF);
            int max = patch.HitMax ?? (data[0x07] >> 4);
            data[0x07] = (byte)((min & 0xF) | ((max & 0xF) << 4));
        }

        int inflict = patch.Inflict ?? ResolveInflict(patch.InflictToken);
        if (inflict >= 0)
            Array.Copy(BitConverter.GetBytes((short)inflict), 0, data, 0x08, 2);

        if (patch.InflictChance.HasValue)
            data[0x0A] = (byte)patch.InflictChance.Value;

        if (patch.Param0x0B.HasValue)
            data[0x0B] = (byte)patch.Param0x0B.Value;

        if (patch.TurnMin.HasValue)
            data[0x0C] = (byte)patch.TurnMin.Value;

        if (patch.TurnMax.HasValue)
            data[0x0D] = (byte)patch.TurnMax.Value;

        if (patch.CriticalStage.HasValue)
            data[0x0E] = (byte)patch.CriticalStage.Value;

        if (patch.Flinch.HasValue)
            data[0x0F] = (byte)patch.Flinch.Value;

        if (patch.Effect.HasValue)
            Array.Copy(BitConverter.GetBytes((ushort)patch.Effect.Value), 0, data, 0x10, 2);

        if (patch.Recoil.HasValue)
            data[0x12] = unchecked((byte)(sbyte)patch.Recoil.Value);

        if (patch.Heal.HasValue)
            data[0x13] = (byte)patch.Heal.Value;

        SetOptionalByte(data, 0x14, ResolveTargeting(patch.Targeting));
    }

    private void ApplyStatPatches(byte[] data, MoveTemplates.MovePatchRow patch)
    {
        if (patch.ClearStatEffects || global::MoveBalanceTemplateSanitizer.ShouldClearStatEffectsBeforeApply(patch))
            ClearMoveStatEffects(data);

        if (!string.IsNullOrWhiteSpace(patch.UserStat))
            ApplyNextStatSlot(data, patch.UserStat, patch.UserStatChange ?? 1, patch.UserStatChance ?? 100);

        if (!string.IsNullOrWhiteSpace(patch.TargetStat))
            ApplyNextStatSlot(data, patch.TargetStat, patch.TargetStatChange ?? -1, patch.TargetStatChance ?? 100);

        ApplyExplicitStatSlot(data, 0, patch.Stat1, patch.Stat1Change, patch.Stat1Chance);
        ApplyExplicitStatSlot(data, 1, patch.Stat2, patch.Stat2Change, patch.Stat2Chance);
        ApplyExplicitStatSlot(data, 2, patch.Stat3, patch.Stat3Change, patch.Stat3Chance);
    }

    private void ApplyFlagPatches(byte[] data, MoveTemplates.MovePatchRow patch)
    {
        if (!patch.ClearFlags && string.IsNullOrWhiteSpace(patch.SetFlags) && string.IsNullOrWhiteSpace(patch.UnsetFlags))
            return;

        Move move = _generation == 7 ? new Move7(data) : new Move6(data);
        uint flags = patch.ClearFlags ? 0 : Convert.ToUInt32(move.GetType().GetProperty("Flags")!.GetValue(move));
        flags |= ResolveFlags(patch.SetFlags, _generation == 7 ? typeof(MoveFlag7) : typeof(MoveFlag6));
        flags &= ~ResolveFlags(patch.UnsetFlags, _generation == 7 ? typeof(MoveFlag7) : typeof(MoveFlag6));
        if (move is Move7 m7) m7.Flags = (MoveFlag7)flags; else ((Move6)move).Flags = (MoveFlag6)flags;
    }

    private static uint ResolveFlags(string value, Type flagType)
    {
        uint result = 0;
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (string token in MoveTemplates.SplitTokens(value))
        {
            if (uint.TryParse(token, out uint numeric))
            {
                result |= numeric;
                continue;
            }

            string normalized = MoveTemplates.NormalizeToken(token);
            bool found = false;
            foreach (string name in Enum.GetNames(flagType))
            {
                if (MoveTemplates.NormalizeToken(name) != normalized)
                    continue;

                object parsed = Enum.Parse(flagType, name);
                result |= Convert.ToUInt32(parsed);
                found = true;
                break;
            }
            if (!found) throw new InvalidDataException($"Flag desconocido: {token}.");
        }

        return result;
    }

    private void ApplyNextStatSlot(byte[] data, string statToken, int change, int chance)
    {
        int stat = ResolveStat(statToken);
        if (stat <= 0)
            return;

        for (int slot = 0; slot < 3; slot++)
        {
            int statOffset = 0x15 + slot;
            if (data[statOffset] != 0)
                continue;

            ApplyStatSlot(data, slot, stat, change, chance);
            return;
        }

        ApplyStatSlot(data, 2, stat, change, chance);
    }

    private void ApplyExplicitStatSlot(byte[] data, int slot, string statToken, int? change, int? chance)
    {
        int stat = ResolveStat(statToken);
        if (stat <= 0)
            return;

        ApplyStatSlot(data, slot, stat, change ?? 0, chance ?? 100);
    }

    private static void ApplyStatSlot(byte[] data, int slot, int stat, int change, int chance)
    {
        data[0x15 + slot] = (byte)stat;
        data[0x18 + slot] = unchecked((byte)(sbyte)change);
        data[0x1B + slot] = (byte)chance;
    }

    private static void SetOptionalByte(byte[] data, int offset, int value)
    {
        if (value >= 0)
            data[offset] = (byte)value;
    }

    private int ResolveType(string value)
        => MoveTemplates.ResolveToken(value, types, TypeAliases);

    private int ResolveCategory(string value)
        => MoveTemplates.ResolveToken(value, MoveCategories, CategoryAliases);

    private int ResolveQuality(string value)
        => MoveTemplates.ResolveToken(value, MoveQualities);

    private int ResolveTargeting(string value)
        => MoveTemplates.ResolveToken(value, TargetingTypes, TargetingAliases);

    private int ResolveInflict(string value)
        => MoveTemplates.ResolveToken(value, InflictionTypes, InflictAliases);

    private int ResolveStat(string value)
        => MoveTemplates.ResolveToken(value, StatCategories, StatAliases);

    private static readonly Dictionary<string, int> TypeAliases = new()
    {
        ["normal"] = 0,
        ["fighting"] = 1,
        ["lucha"] = 1,
        ["flying"] = 2,
        ["volador"] = 2,
        ["poison"] = 3,
        ["veneno"] = 3,
        ["ground"] = 4,
        ["tierra"] = 4,
        ["rock"] = 5,
        ["roca"] = 5,
        ["bug"] = 6,
        ["bicho"] = 6,
        ["ghost"] = 7,
        ["fantasma"] = 7,
        ["steel"] = 8,
        ["acero"] = 8,
        ["fire"] = 9,
        ["fuego"] = 9,
        ["water"] = 10,
        ["agua"] = 10,
        ["grass"] = 11,
        ["planta"] = 11,
        ["electric"] = 12,
        ["electrico"] = 12,
        ["psychic"] = 13,
        ["psiquico"] = 13,
        ["ice"] = 14,
        ["hielo"] = 14,
        ["dragon"] = 15,
        ["dark"] = 16,
        ["siniestro"] = 16,
        ["fairy"] = 17,
        ["hada"] = 17,
    };

    private static readonly Dictionary<string, int> CategoryAliases = new()
    {
        ["status"] = 0,
        ["estado"] = 0,
        ["physical"] = 1,
        ["fisico"] = 1,
        ["special"] = 2,
        ["especial"] = 2,
    };

    private static readonly Dictionary<string, int> StatAliases = new()
    {
        ["none"] = 0,
        ["ninguno"] = 0,
        ["attack"] = 1,
        ["atk"] = 1,
        ["ataque"] = 1,
        ["defense"] = 2,
        ["def"] = 2,
        ["defensa"] = 2,
        ["specialattack"] = 3,
        ["spatk"] = 3,
        ["specialatk"] = 3,
        ["ataqueespecial"] = 3,
        ["atakespecial"] = 3,
        ["specialdefense"] = 4,
        ["spdef"] = 4,
        ["specialdef"] = 4,
        ["defensaespecial"] = 4,
        ["speed"] = 5,
        ["spe"] = 5,
        ["velocidad"] = 5,
        ["accuracy"] = 6,
        ["precision"] = 6,
        ["evasion"] = 7,
        ["all"] = 8,
        ["todo"] = 8,
        ["todos"] = 8,
    };

    private static readonly Dictionary<string, int> InflictAliases = new()
    {
        ["none"] = 0,
        ["ninguno"] = 0,
        ["paralyze"] = 1,
        ["paralysis"] = 1,
        ["paralisis"] = 1,
        ["paralizar"] = 1,
        ["sleep"] = 2,
        ["dormir"] = 2,
        ["sueno"] = 2,
        ["freeze"] = 3,
        ["congelar"] = 3,
        ["congelacion"] = 3,
        ["burn"] = 4,
        ["quemar"] = 4,
        ["quemadura"] = 4,
        ["poison"] = 5,
        ["veneno"] = 5,
        ["envenenar"] = 5,
        ["confusion"] = 6,
        ["confundir"] = 6,
        ["attract"] = 7,
        ["atraccion"] = 7,
        ["nightmare"] = 9,
        ["pesadilla"] = 9,
        ["curse"] = 10,
        ["maldicion"] = 10,
        ["taunt"] = 11,
        ["mofa"] = 11,
        ["torment"] = 12,
        ["tormento"] = 12,
        ["disable"] = 13,
        ["anulacion"] = 13,
        ["yawn"] = 14,
        ["bostezo"] = 14,
        ["healblock"] = 15,
        ["anticura"] = 15,
        ["detect"] = 17,
        ["proteccion"] = 17,
        ["leechseed"] = 18,
        ["drenadoras"] = 18,
        ["embargo"] = 19,
        ["perishsong"] = 20,
        ["cantoperish"] = 20,
        ["cantomortal"] = 20,
        ["ingrain"] = 21,
        ["arraigo"] = 21,
    };

    private static readonly Dictionary<string, int> TargetingAliases = new()
    {
        ["single"] = 0,
        ["selected"] = 0,
        ["target"] = 0,
        ["adjacent"] = 0,
        ["seleccionado"] = 0,
        ["ally"] = 1,
        ["companero"] = 1,
        ["adjacentally"] = 2,
        ["singlefoe"] = 3,
        ["foe"] = 3,
        ["rival"] = 3,
        ["opponent"] = 3,
        ["everyonebutuser"] = 4,
        ["allfoes"] = 5,
        ["allenemies"] = 5,
        ["enemigos"] = 5,
        ["rivales"] = 5,
        ["allallies"] = 6,
        ["aliados"] = 6,
        ["self"] = 7,
        ["user"] = 7,
        ["usuario"] = 7,
        ["allpokemon"] = 8,
        ["allfield"] = 8,
        ["todos"] = 8,
        ["entirefield"] = 10,
        ["field"] = 10,
        ["campo"] = 10,
        ["opponentfield"] = 11,
        ["foefield"] = 11,
        ["camporival"] = 11,
        ["userfield"] = 12,
        ["allyfield"] = 12,
        ["campousuario"] = 12,
    };

    private static void ApplyGen7ZMovePatch(byte[] data, MoveTemplates.MovePatchRow patch)
    {
        if (!patch.ZEffect.HasValue)
            return;

        // ZEffect is stored separately from the normal move Effect.
        // Move7 writes the Gen7 Z-Move metadata into the same backing byte[].
        Move7 move = new(data);
        move.ZEffect = patch.ZEffect.Value;
    }

    private static void ClearMoveStatEffects(byte[] data)
    {
        // Stat categories: None, Attack, Defense, Sp. Atk, Sp. Def, Speed, Accuracy, Evasion, All.
        data[0x15] = 0;
        data[0x16] = 0;
        data[0x17] = 0;

        // Stat stages.
        data[0x18] = 0;
        data[0x19] = 0;
        data[0x1A] = 0;

        // Stat effect chances.
        data[0x1B] = 0;
        data[0x1C] = 0;
        data[0x1D] = 0;
    }
}
