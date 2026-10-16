using System.Globalization;
using System.Reflection;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal static class TableExport
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["HP"] = "PS", ["Attack"] = "Ataque", ["Defense"] = "Defensa", ["SpecialAttack"] = "At. especial",
        ["SpecialDefense"] = "Def. especial", ["Speed"] = "Velocidad", ["CatchRate"] = "Captura",
        ["FirstType"] = "Tipo 1", ["SecondType"] = "Tipo 2", ["Ability1"] = "Habilidad 1",
        ["Ability2"] = "Habilidad 2", ["AbilityHidden"] = "Habilidad oculta", ["Item1"] = "Objeto 1",
        ["Item2"] = "Objeto 2", ["Item3"] = "Objeto 3", ["EvHp"] = "EV PS", ["EvAttack"] = "EV Ataque",
        ["EvDefense"] = "EV Defensa", ["EvSpecialAttack"] = "EV At. esp.", ["EvSpecialDefense"] = "EV Def. esp.",
        ["EvSpeed"] = "EV Velocidad", ["BaseExperience"] = "EXP base", ["BaseFriendship"] = "Amistad base",
        ["HatchCycles"] = "Ciclos eclosión", ["GenderRatio"] = "Ratio género", ["EggGroup1"] = "Grupo huevo 1",
        ["EggGroup2"] = "Grupo huevo 2", ["ExperienceGrowth"] = "Curva EXP", ["EscapeRate"] = "Huida / SOS",
        ["EvoStage"] = "Etapa", ["FormCount"] = "Cantidad formas", ["FormSprite"] = "Sprite forma",
        ["Color"] = "Color", ["Height"] = "Altura interna", ["Weight"] = "Peso interno", ["ZItem"] = "Objeto Z",
        ["ZBaseMove"] = "Movimiento base Z", ["ZSpecialMove"] = "Movimiento Z exclusivo", ["RegionalVariant"] = "Variante regional",
        ["TmFlags"] = "Bits MT/MO", ["TutorFlags"] = "Bits tutores tipo", ["SpecialTutorFlags"] = "Bits tutores especiales",
        ["Type"] = "Tipo", ["Category"] = "Categoría", ["Power"] = "Potencia", ["Accuracy"] = "Precisión",
        ["PP"] = "PP", ["Priority"] = "Prioridad", ["Quality"] = "Clase efecto", ["HitMin"] = "Golpes mín.",
        ["HitMax"] = "Golpes máx.", ["Inflict"] = "Estado", ["InflictPercent"] = "Prob. estado", ["CritStage"] = "Crítico",
        ["Flinch"] = "Retroceso", ["EffectSequence"] = "Secuencia efecto", ["Target"] = "Objetivo",
        ["ZMove"] = "Movimiento Z", ["ZPower"] = "Potencia Z", ["ZEffect"] = "Efecto Z", ["Flags"] = "Flags (hex)",
        ["InflictCount"] = "Duración estado", ["TurnsMin"] = "Turnos mín.", ["TurnsMax"] = "Turnos máx.",
        ["Recoil"] = "Daño retroceso", ["Healing"] = "Curación", ["Stat1"] = "Stat 1", ["Stat2"] = "Stat 2",
        ["Stat3"] = "Stat 3", ["Stat1Stage"] = "Etapas stat 1", ["Stat2Stage"] = "Etapas stat 2",
        ["Stat3Stage"] = "Etapas stat 3", ["Stat1Rate"] = "Prob. stat 1", ["Stat2Rate"] = "Prob. stat 2",
        ["Stat3Rate"] = "Prob. stat 3", ["RefreshType"] = "Estado Poké Relax", ["RefreshRate"] = "Prob. Poké Relax",
    };

    internal static string Build<T>(IEnumerable<(int Id, string Name, T Value)> rows,
        Func<string, object?, string> format)
    {
        PropertyInfo[] fields = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(p => p.MetadataToken).ToArray();
        var lines = new List<string> { string.Join("\t", new[] { "ID", "Nombre" }.Concat(fields.Select(p => Labels.GetValueOrDefault(p.Name, p.Name)))) };
        foreach (var row in rows)
            lines.Add(string.Join("\t", new[] { row.Id.ToString(), row.Name }
                .Concat(fields.Select(p => format(p.Name, p.GetValue(row.Value)))).Select(Clean)));
        return string.Join(Environment.NewLine, lines);
    }

    internal static string Label(string property) => Labels.GetValueOrDefault(property, property);

    internal static string Scalar(object? value) => value is uint u ? u.ToString("X8")
        : value is bool b ? (b ? "Sí" : "No") : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    internal static string Named(object? value, IReadOnlyList<string> names)
    {
        int id = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return id >= 0 && id < names.Count ? $"{names[id]} ({id})" : $"ID {id}";
    }
    private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}

public sealed partial class GameProjectSession
{
    public string ExportPersonalTable(IReadOnlyList<int> scope)
    {
        GarcTransaction.ReadUnchanged(_personalPath, _diskFingerprint);
        var ids = scope.ToHashSet();
        return TableExport.Build(Entries.Where(e => ids.Contains(e.Index)).Select(e => (e.Index, e.Name, ReadPersonal(e.Index))),
            (field, value) => field switch
            {
                "FirstType" or "SecondType" => TableExport.Named(value, Types),
                "Ability1" or "Ability2" or "AbilityHidden" => TableExport.Named(value, AbilityNames),
                "Item1" or "Item2" or "Item3" or "ZItem" => TableExport.Named(value, ItemNames),
                "ZBaseMove" or "ZSpecialMove" => TableExport.Named(value, MoveNames),
                _ => TableExport.Scalar(value),
            });
    }
}

public sealed partial class GameMoveService
{
    public string ExportMoveTable(IReadOnlyList<int> scope)
    {
        GarcTransaction.ReadUnchanged(_path, _sha);
        var ids = scope.ToHashSet();
        string[] moveNames = AllMoveNames.Select(n => n.Name).ToArray();
        return TableExport.Build(Entries.Where(e => ids.Contains(e.Id)).Select(e => (e.Id, e.Name, Read(e.Id))),
            (field, value) => field switch
            {
                "Type" => TableExport.Named(value, Types),
                "Category" => TableExport.Named(value, new[] { "Estado", "Físico", "Especial" }),
                "ZMove" => TableExport.Named(value, moveNames),
                _ => TableExport.Scalar(value),
            });
    }
}
