using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal static class BatchDifferences
{
    internal static IEnumerable<BatchChange> Personal(int id, string name, PersonalInfo a, PersonalInfo b,
        IReadOnlyList<string> types, IReadOnlyList<string> abilities, IReadOnlyList<string> items, int generation)
    {
        string Named(int n, IReadOnlyList<string> names) => n >= 0 && n < names.Count ? $"{names[n]} ({n})" : $"ID {n}";
        var rows = new List<BatchChange>();
        void Add(string field, object before, object after)
        {
            if (!Equals(before, after)) rows.Add(new(id, name, field, before.ToString() ?? "", after.ToString() ?? ""));
        }
        Add("PS", a.HP, b.HP); Add("Ataque", a.ATK, b.ATK); Add("Defensa", a.DEF, b.DEF);
        Add("At. especial", a.SPA, b.SPA); Add("Def. especial", a.SPD, b.SPD); Add("Velocidad", a.SPE, b.SPE);
        for (int k = 0; k < 2; k++) Add($"Tipo {k + 1}", Named(a.Types[k], types), Named(b.Types[k], types));
        for (int k = 0; k < 3; k++)
        {
            Add(k == 2 ? "Habilidad oculta" : $"Habilidad {k + 1}", Named(a.Abilities[k], abilities), Named(b.Abilities[k], abilities));
            Add($"Objeto {k + 1}", Named(a.Items[k] & 0xFFFF, items), Named(b.Items[k] & 0xFFFF, items));
        }
        Add("Captura", a.CatchRate, b.CatchRate);
        Add("Grupos huevo", string.Join(" / ", a.EggGroups), string.Join(" / ", b.EggGroups));
        Add("EV otorgados (PS/AT/DF/AE/DE/VE)", $"{a.EV_HP}/{a.EV_ATK}/{a.EV_DEF}/{a.EV_SPA}/{a.EV_SPD}/{a.EV_SPE}",
            $"{b.EV_HP}/{b.EV_ATK}/{b.EV_DEF}/{b.EV_SPA}/{b.EV_SPD}/{b.EV_SPE}");
        Add("Experiencia base", a.BaseEXP, b.BaseEXP); Add("Curva EXP", a.EXPGrowth, b.EXPGrowth);
        Add("Ciclos eclosión", a.HatchCycles, b.HatchCycles);
        Add(generation == 7 ? "Llamada SOS" : "Huida", a.EscapeRate, b.EscapeRate);
        Add("Compatibilidad MT/MO", Flags(a.TMHM), Flags(b.TMHM));
        Add("Tutores por tipo", Flags(a.TypeTutors), Flags(b.TypeTutors));
        for (int k = 0; k < a.SpecialTutors.Length; k++)
            Add($"Tutores especiales · grupo {k + 1}", Flags(a.SpecialTutors[k]), Flags(b.SpecialTutors[k]));
        return rows;
    }

    private static string Flags(bool[] flags) => string.Join(", ", flags.Select((value, index) => (value, index))
        .Where(x => x.value).Select(x => (x.index + 1).ToString()));

    internal static IEnumerable<BatchChange> Moves(int id, string name, Move a, Move b, IReadOnlyList<string> types)
    {
        if (a.Type != b.Type) yield return new(id, name, "Tipo", Label(a.Type), Label(b.Type));
        if (a.Category != b.Category) yield return new(id, name, "Categoría", Category(a.Category), Category(b.Category));
        if (a.PP != b.PP) yield return new(id, name, "PP", a.PP.ToString(), b.PP.ToString());
        string Label(int n) => n >= 0 && n < types.Count ? $"{types[n]} ({n})" : $"ID {n}";
        string Category(int n) => n switch { 0 => "Estado", 1 => "Físico", 2 => "Especial", _ => $"ID {n}" };
    }
    internal static IEnumerable<BatchChange> AllMoveFields(int id, string name, Move a, Move b, IReadOnlyList<string> types)
    {
        foreach (var property in a.GetType().GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
        {
            object? before = property.GetValue(a), after = property.GetValue(b);
            if (before is byte[] || Equals(before, after)) continue;
            string field = property.Name switch { "Heal" => "Curación", "MinTurns" => "Turnos mín.", "MaxTurns" => "Turnos máx.", "TurnMin" => "Turnos mín.", "TurnMax" => "Turnos máx.",
                "Stat1Percent" => "Prob. stat 1", "Stat2Percent" => "Prob. stat 2", "Stat3Percent" => "Prob. stat 3",
                "InflictCount" => "Parámetro / duración del estado", _ => TableExport.Label(property.Name) };
            string Format(object? value) => property.Name switch
            {
                "Type" => TableExport.Named(value, types),
                "Category" => TableExport.Named(value, new[] { "Estado", "Físico", "Especial" }),
                "Priority" or "Recoil" or "Stat1Stage" or "Stat2Stage" or "Stat3Stage" => unchecked((sbyte)Convert.ToInt32(value)).ToString(),
                "Flags" => Convert.ToUInt32(value).ToString("X8"), _ => TableExport.Scalar(value),
            };
            yield return new(id, name, field, Format(before), Format(after));
        }

    }

}
