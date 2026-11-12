using System.IO;
using System.Security.Cryptography;
using System.Text;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures.PersonalInfo;
namespace ThreeDS.Progressive.Services;

/// <summary>One Mega, Primal or Ultra form next to the species it comes from.</summary>
internal sealed record MegaAbilityRow(int Species, int Form, int PersonalIndex, string Name, int[] BaseAbilities, int[] FormAbilities)
{
    /// <summary>The form does not have one single ability: which one it gets depends on the ability slot of the Pokémon.</summary>
    internal bool SlotDependent => FormAbilities.Distinct().Count() > 1;
}

internal sealed record MegaAbilityReport(IReadOnlyList<MegaAbilityRow> Rows, string PersonalSha256)
{
    internal int SlotDependent => Rows.Count(r => r.SlotDependent);
}

/// <summary>
/// Read-only diagnosis of the abilities of Mega Evolutions in Ultra Sun / Ultra Moon, for the reported problem of
/// abilities that change or do not act as expected in online battles. It states facts of the saved Personal table
/// and a fingerprint two players can compare. It does not reproduce a battle and it changes nothing.
/// </summary>
internal static class MegaAbilityDiagnostics
{
    internal static bool Supports(GameProjectSession project) => project.GameConfiguration.USUM;

    internal static MegaAbilityReport Inspect(GameProjectSession project)
    {
        if (!Supports(project)) throw new InvalidOperationException("El diagnóstico de megaevoluciones solo admite Ultra Sol y Ultra Luna.");
        var config = project.GameConfiguration;
        string path = Path.Combine(config.RomFS, config.GetGARCFileName("personal"));
        byte[] archive = File.ReadAllBytes(path);
        var personal = new GARC.MemGARC(archive).Files;
        if (personal.Length < 2 || personal.Take(personal.Length - 1).Any(r => r.Length != PersonalInfoSM.SIZE) ||
            !personal.Take(personal.Length - 1).SelectMany(r => r).SequenceEqual(personal[^1]))
            throw new InvalidDataException("Los registros Personal y su tabla maestra no coinciden. Revisa el proyecto guardado.");
        var table = new PersonalTable(personal[^1], GameVersion.USUM).Table;
        string[] species = config.GetText(TextName.SpeciesNames);
        var rows = new List<MegaAbilityRow>();
        for (int id = 1; id <= Math.Min(config.MaxSpeciesID, table.Length - 1); id++)
        {
            var entry = table[id];
            for (int form = 1; form < entry.FormeCount; form++)
            {
                if (!Legal.IsMegaForm(id, form)) continue;
                int index = entry.FormeIndex(id, form);
                if (index <= 0 || index >= table.Length || index == id) continue;
                string name = (id < species.Length ? species[id] : "Especie " + id) + " · forma " + form;
                rows.Add(new(id, form, index, name, [.. entry.Abilities], [.. table[index].Abilities]));
            }
        }
        // The master table is what the game reads; its hash identifies the whole Personal data of this project.
        return new(rows, Convert.ToHexString(SHA256.HashData(personal[^1])));
    }

    internal static string Describe(GameProjectSession project, MegaAbilityReport report)
    {
        string[] abilities = project.GameConfiguration.GetText(TextName.AbilityNames);
        string Ability(int id) => id >= 0 && id < abilities.Length ? abilities[id] : "#" + id;
        string Slots(int[] ids) => string.Join(" / ", ids.Select(Ability));
        var text = new StringBuilder();
        text.AppendLine("Megaevoluciones · diagnóstico de habilidades (solo lectura)");
        text.AppendLine();
        text.AppendLine($"Formas Mega, Primigenias y Ultra revisadas: {report.Rows.Count}");
        text.AppendLine($"Con habilidad distinta según la ranura: {report.SlotDependent}");
        text.AppendLine($"Huella de la tabla Personal (SHA-256): {report.PersonalSha256}");
        text.AppendLine();
        text.AppendLine("Qué significa");
        text.AppendLine("- En el juego original, cada megaevolución tiene la misma habilidad en sus tres ranuras (1, 2 y oculta).");
        text.AppendLine("- Si las ranuras de una megaevolución son distintas, la habilidad que recibe un Pokémon al megaevolucionar depende de la ranura que tenía antes: dos ejemplares de la misma especie pueden megaevolucionar con habilidades diferentes.");
        text.AppendLine("- Para un combate entre dos consolas, compara esta huella con la del otro jugador. Si no coincide, los dos juegos no tienen los mismos datos de Pokémon.");
        text.AppendLine();
        text.AppendLine("Qué no demuestra");
        text.AppendLine("- Este informe lee datos guardados. No reproduce un combate ni confirma la causa del problema en línea, que sigue sin acreditarse.");
        text.AppendLine();
        foreach (var group in new[] { (Title: "Dependen de la ranura", Rows: report.Rows.Where(r => r.SlotDependent)), (Title: "Una sola habilidad", Rows: report.Rows.Where(r => !r.SlotDependent)) })
        {
            text.AppendLine(group.Title);
            foreach (var row in group.Rows)
                text.AppendLine($"- {row.Name} (Personal {row.PersonalIndex}): {Slots(row.FormAbilities)} · especie base: {Slots(row.BaseAbilities)}");
            if (!group.Rows.Any()) text.AppendLine("- Ninguna");
            text.AppendLine();
        }
        return text.ToString();
    }
}
