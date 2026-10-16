// Stats schema and validation ported from CustomBalanceTemplates (f459e40).
#nullable disable
using System.IO;
using System.Globalization;
using static ThreeDS.Progressive.Services.MoveTemplates;
namespace ThreeDS.Progressive.Services;
internal static class PokemonStatTemplates
{
    internal sealed class PokemonStatPatchRow
    {
        public int Entry { get; init; }
        public string Name { get; init; } = string.Empty;
        public int? HP { get; init; }
        public int? ATK { get; init; }
        public int? DEF { get; init; }
        public int? SPA { get; init; }
        public int? SPD { get; init; }
        public int? SPE { get; init; }
        public string Notes { get; init; } = string.Empty;
    }

    internal static PokemonStatPatchRow[] Load(string path, string[] entryNames)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No se encontró el template de estadísticas.", path);

        var rows = new List<PokemonStatPatchRow>();
        Dictionary<string, int> header = null;

        foreach (var fields in ReadCsv(path))
        {
            if (fields.Length == 0)
                continue;

            string first = Get(fields, 0);
            
            if (first.StartsWith('#'))
                continue;

            if (IsHeader(first, "Entry") || IsHeader(first, "Pokemon") || IsHeader(first, "Species"))
            {
                header = BuildHeaderMap(fields);
                continue;
            }

            string entryToken = GetField(fields, header, 0, "Entry", "PersonalEntry", "ID");
            string pokemonToken = GetField(fields, header, 1, "Pokemon", "Species", "Name");

            int entry = ResolvePokemonStatEntry(entryToken, pokemonToken, entryNames);
            if (entry <= 0 || entry >= entryNames.Length) throw new InvalidDataException($"Entrada desconocida en el template: {entryToken} / {pokemonToken}.");

            rows.Add(new PokemonStatPatchRow
            {
                Entry = entry,
                Name = entryNames[entry],
                HP = ParseNullableInt(GetField(fields, header, 2, "HP")),
                ATK = ParseNullableInt(GetField(fields, header, 3, "ATK", "Attack")),
                DEF = ParseNullableInt(GetField(fields, header, 4, "DEF", "Defense")),
                SPA = ParseNullableInt(GetField(fields, header, 5, "SPA", "SpA", "SpecialAttack")),
                SPD = ParseNullableInt(GetField(fields, header, 6, "SPD", "SpD", "SpecialDefense")),
                SPE = ParseNullableInt(GetField(fields, header, 7, "SPE", "Speed")),
                Notes = GetField(fields, header, 8, "Notes", "Note"),
            });
        }

        return [.. rows];
    }

    private static int ResolvePokemonStatEntry(string entryToken, string pokemonToken, string[] entryNames)
    {
        if (!string.IsNullOrWhiteSpace(entryToken))
        {
            if (int.TryParse(entryToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                return id;

            int byEntryName = ResolveToken(entryToken, entryNames);
            if (byEntryName >= 0)
                return byEntryName;
        }

        return string.IsNullOrWhiteSpace(pokemonToken) ? -1 : ResolveToken(pokemonToken, entryNames);
    }

    private static int ResolveBaseStat(int current, int? value, PokemonStatPatchRow row, string stat)
    {
        if (!value.HasValue)
            return current;

        int v = value.Value;
        if (v is < 1 or > 255)
            throw new InvalidDataException($"{stat}: valor {v} inválido para la entrada {row.Entry} ({row.Name}). Las estadísticas deben estar entre 1 y 255.");

        return v;
    }

    internal static void Apply(PokemonStatPatchRow row, pk3DS.Core.Structures.PersonalInfo.PersonalInfo p)
    {
        p.HP=ResolveBaseStat(p.HP,row.HP,row,"HP");p.ATK=ResolveBaseStat(p.ATK,row.ATK,row,"ATK");
        p.DEF=ResolveBaseStat(p.DEF,row.DEF,row,"DEF");p.SPA=ResolveBaseStat(p.SPA,row.SPA,row,"SPA");
        p.SPD=ResolveBaseStat(p.SPD,row.SPD,row,"SPD");p.SPE=ResolveBaseStat(p.SPE,row.SPE,row,"SPE");
    }

}
