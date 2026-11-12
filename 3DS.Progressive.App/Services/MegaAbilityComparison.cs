using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
namespace ThreeDS.Progressive.Services;

internal sealed record MegaAbilityDifference(int Species, int Form, string Name, int[] First, int[] Second);

internal sealed record MegaAbilityComparisonReport(string FirstGame, string SecondGame, bool SameFingerprint, int FormsCompared, int FormsWithDifferentAbilities,
    int MegasCompared, IReadOnlyList<MegaAbilityDifference> MegaDifferences, IReadOnlyList<string> OnlyInOne, IReadOnlyDictionary<int, string> AbilityNames);

/// <summary>
/// Compares the abilities of two players' games from two «Definitive Game Data» exports (Ultra Sun / Ultra Moon).
/// Read-only and without any game file: it only reads the two JSON documents. It states where the saved data of the
/// two games differ; it does not reproduce a battle and does not prove the cause of the online problem.
/// </summary>
internal static class MegaAbilityComparison
{
    internal const int MaxBytes = 20 * 1024 * 1024;

    internal static MegaAbilityComparisonReport CompareFiles(string first, string second) => Compare(Read(first), Read(second));
    private static string Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("No existe el archivo.", path);
        if (info.Length > MaxBytes) throw new InvalidDataException("El archivo supera el límite de 20 MB.");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private sealed record Export(string Game, string Fingerprint, Dictionary<(int Species, int Form), (string Name, int[] Abilities)> Forms, Dictionary<int, string> Abilities);
    private static int Whole(JsonNode? node, string what)
    {
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && int.TryParse(v.ToJsonString(), out int number) && number >= 0) return number;
        throw new InvalidDataException("La exportación tiene un valor no válido en " + what + ".");
    }
    private static string Text(JsonNode? node, string what) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : throw new InvalidDataException("La exportación no indica " + what + ".");
    private static Export Parse(string json, string which)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json, null, new JsonDocumentOptions { MaxDepth = 32 }) as JsonObject ?? throw new InvalidDataException("no es un objeto JSON"); }
        catch (JsonException) { throw new InvalidDataException($"El {which} archivo no contiene JSON válido."); }
        catch (InvalidDataException) { throw new InvalidDataException($"El {which} archivo no es una exportación de datos del juego."); }
        try
        {
            if (root["format"] is not JsonValue format || format.GetValueKind() != JsonValueKind.String || format.GetValue<string>() != DefinitiveGameData.Format || Whole(root["schemaVersion"], "schemaVersion") != 1)
                throw new InvalidDataException("no es una exportación «Definitive Game Data» versión 1");
            string game = Text(root["game"]?["id"], "el juego");
            if (game is not ("ultra-sun" or "ultra-moon")) throw new InvalidDataException("no es de Ultra Sol ni de Ultra Luna");
            if (root["species"] is not JsonArray rows || rows.Count is 0 or > 4096) throw new InvalidDataException("no contiene la lista de Pokémon");
            var forms = new Dictionary<(int, int), (string, int[])>();
            foreach (var row in rows)
            {
                if (row is not JsonObject entry || entry["abilities"] is not JsonArray slots || slots.Count != 3) throw new InvalidDataException("tiene una entrada de Pokémon no válida");
                var key = (Whole(entry["speciesId"], "speciesId"), Whole(entry["formId"], "formId"));
                if (!forms.TryAdd(key, (entry["name"] is JsonValue ? Text(entry["name"], "el nombre") : "Especie " + key.Item1, slots.Select(s => Whole(s, "abilities")).ToArray())))
                    throw new InvalidDataException("repite una forma de Pokémon");
            }
            var abilities = new Dictionary<int, string>();
            if (root["abilities"] is JsonArray catalog)
                foreach (var item in catalog.OfType<JsonObject>()) abilities[Whole(item["id"], "abilities.id")] = item["name"] is JsonValue ? Text(item["name"], "el nombre") : "";
            return new(game, Text(root["source"]?["fingerprint"], "la huella"), forms, abilities);
        }
        catch (InvalidDataException e) when (!e.Message.StartsWith("El ", StringComparison.Ordinal))
        { throw new InvalidDataException($"El {which} archivo {e.Message}."); }
    }

    internal static MegaAbilityComparisonReport Compare(string firstJson, string secondJson)
    {
        Export first = Parse(firstJson, "primer"), second = Parse(secondJson, "segundo");
        var shared = first.Forms.Keys.Intersect(second.Forms.Keys).OrderBy(k => k.Species).ThenBy(k => k.Form).ToArray();
        var megas = shared.Where(k => Legal.IsMegaForm(k.Species, k.Form)).ToArray();
        var differences = megas.Where(k => !first.Forms[k].Abilities.SequenceEqual(second.Forms[k].Abilities))
            .Select(k => new MegaAbilityDifference(k.Species, k.Form, first.Forms[k].Name + " · forma " + k.Form, first.Forms[k].Abilities, second.Forms[k].Abilities)).ToArray();
        var onlyInOne = first.Forms.Keys.Except(second.Forms.Keys).Select(k => $"{first.Forms[k].Name} · forma {k.Form} (solo en el primero)")
            .Concat(second.Forms.Keys.Except(first.Forms.Keys).Select(k => $"{second.Forms[k].Name} · forma {k.Form} (solo en el segundo)")).ToArray();
        var names = new Dictionary<int, string>(second.Abilities);
        foreach (var pair in first.Abilities) names[pair.Key] = pair.Value;
        return new(first.Game, second.Game, first.Fingerprint.Equals(second.Fingerprint, StringComparison.OrdinalIgnoreCase), shared.Length,
            shared.Count(k => !first.Forms[k].Abilities.SequenceEqual(second.Forms[k].Abilities)), megas.Length, differences, onlyInOne, names);
    }

    internal static string Describe(MegaAbilityComparisonReport report)
    {
        string Ability(int id) => report.AbilityNames.TryGetValue(id, out var name) && name.Length > 0 ? name : "#" + id;
        string Slots(int[] ids) => string.Join(" / ", ids.Select(Ability));
        string Game(string id) => id == "ultra-moon" ? "Ultra Luna" : "Ultra Sol";
        var text = new StringBuilder();
        text.AppendLine("Megaevoluciones · comparación de habilidades entre dos juegos (solo lectura)");
        text.AppendLine();
        text.AppendLine($"Primer juego: {Game(report.FirstGame)} · Segundo juego: {Game(report.SecondGame)}");
        text.AppendLine(report.SameFingerprint ? "Las huellas de los datos exportados coinciden." : "Las huellas de los datos exportados NO coinciden: los dos juegos no tienen los mismos datos.");
        text.AppendLine($"Formas de Pokémon comparadas: {report.FormsCompared} · con habilidades distintas: {report.FormsWithDifferentAbilities}");
        text.AppendLine($"Formas Mega, Primigenias y Ultra comparadas: {report.MegasCompared} · con habilidades distintas: {report.MegaDifferences.Count}");
        text.AppendLine();
        text.AppendLine("Qué significa");
        text.AppendLine("- Si una megaevolución tiene habilidades distintas en los dos juegos, cada consola podría resolver su habilidad con sus propios datos. Es una hipótesis: no se ha comprobado en un combate.");
        text.AppendLine("- Si no hay diferencias, los datos de habilidades no explican por sí solos un comportamiento distinto entre las dos consolas.");
        text.AppendLine("- Esta comparación lee dos archivos exportados. No reproduce un combate ni confirma la causa del problema en línea.");
        text.AppendLine();
        text.AppendLine("Megaevoluciones con habilidades distintas (primer juego → segundo juego)");
        foreach (var d in report.MegaDifferences) text.AppendLine($"- {d.Name}: {Slots(d.First)} → {Slots(d.Second)}");
        if (report.MegaDifferences.Count == 0) text.AppendLine("- Ninguna");
        if (report.OnlyInOne.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Formas que solo aparecen en una de las dos exportaciones");
            foreach (string line in report.OnlyInOne.Take(50)) text.AppendLine("- " + line);
        }
        return text.ToString();
    }
}
